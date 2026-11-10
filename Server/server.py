"""FoxTable authoritative encounter server. Python 3.10+, standard library only."""
import argparse
import base64
import copy
import hashlib
import hmac
import json
import math
import re
import secrets
import sqlite3
import struct
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from visibility import center, clear_path, vision_cells, token_in_cells, cells_json, upgrade

MAX_BODY = 9 * 1024 * 1024
CONDITIONS = {'Ослеплён', 'Очарован', 'Глухота', 'Испуган', 'Схвачен', 'Недееспособен',
              'Невидим', 'Парализован', 'Окаменел', 'Отравлен', 'Сбит с ног', 'Опутан',
              'Оглушён', 'Без сознания', 'Концентрация', 'Мёртв'}


class ApiError(Exception):
    def __init__(self, status, message):
        self.status, self.message = status, message


def require(test, message, status=400):
    if not test:
        raise ApiError(status, message)


def number(value, low, high):
    require(isinstance(value, (int, float)) and not isinstance(value, bool), 'Нужно число')
    require(math.isfinite(value) and low <= value <= high, 'Число вне допустимого диапазона')
    return value


def text(value, limit=80):
    require(isinstance(value, str), 'Нужна строка')
    value = value.strip()
    require(0 < len(value) <= limit, 'Пустой или слишком длинный текст')
    return value


def uid():
    return secrets.token_hex(8)


def image_dimensions(data, mime):
    if mime == 'image/png':
        require(len(data) >= 33 and data[:8] == b'\x89PNG\r\n\x1a\n' and data[12:16] == b'IHDR', 'Повреждённый PNG')
        return struct.unpack('>II', data[16:24])
    require(data[:3] == b'\xff\xd8\xff', 'Повреждённый JPEG')
    pos = 2
    while pos + 4 <= len(data):
        require(data[pos] == 255, 'Повреждённый JPEG')
        while pos < len(data) and data[pos] == 255:
            pos += 1
        require(pos < len(data), 'Повреждённый JPEG')
        marker = data[pos]
        pos += 1
        if marker in (0xD8, 0xD9) or 0xD0 <= marker <= 0xD7:
            continue
        require(pos+2 <= len(data), 'Повреждённый JPEG')
        length = int.from_bytes(data[pos:pos+2], 'big')
        require(length >= 2 and pos+length <= len(data), 'Повреждённый JPEG')
        if marker in (0xC0, 0xC1, 0xC2):
            require(length >= 7, 'Повреждённый JPEG')
            height, width = struct.unpack('>HH', data[pos+3:pos+7])
            return width, height
        pos += length
    raise ApiError(400, 'Не удалось прочитать размеры JPEG')


def scene(name='Забытое святилище'):
    return dict(id=uid(), name=name, width=30, height=22, cellFeet=5, grid=True,
                snap=True, mapId='', fog=False, revealed=[], tokens=[], drawings=[],
                turnId='', round=1, activeCombat=False)


def token(name, x, y, kind='enemy', hp=12, color='#b9605c'):
    return dict(id=uid(), name=name, x=x, y=y, size=1, kind=kind, hp=hp, maxHp=hp,
                ac=12, initiative=0, ownerId='', hidden=False, color=color, conditions=[])


class Store:
    def __init__(self, path):
        self.lock = threading.RLock()
        self.db = sqlite3.connect(path, check_same_thread=False)
        self.db.execute('PRAGMA journal_mode=WAL')
        self.db.executescript('''
          CREATE TABLE IF NOT EXISTS rooms(code TEXT PRIMARY KEY, state TEXT NOT NULL);
          CREATE TABLE IF NOT EXISTS members(secret TEXT PRIMARY KEY, room TEXT NOT NULL,
            member_id TEXT NOT NULL, name TEXT NOT NULL, role TEXT NOT NULL, seen REAL NOT NULL);
          CREATE TABLE IF NOT EXISTS assets(id TEXT PRIMARY KEY, room TEXT NOT NULL,
            mime TEXT NOT NULL, data BLOB NOT NULL);
          CREATE TABLE IF NOT EXISTS operations(room TEXT, member TEXT, id TEXT,
            PRIMARY KEY(room, member, id));
        ''')
        self.db.commit()

    def load(self, code):
        row = self.db.execute('SELECT state FROM rooms WHERE code=?', (code,)).fetchone()
        require(row is not None, 'Комната не найдена', 404)
        state = json.loads(row[0])
        require(state.get("formatVersion") in (1, 2), "Неизвестная версия кампании", 409)
        return upgrade(state)

    def save(self, code, state):
        upgrade(state)
        self.db.execute('INSERT OR REPLACE INTO rooms VALUES(?,?)',
                        (code, json.dumps(state, ensure_ascii=False)))

    def member(self, secret, code):
        require(isinstance(secret, str) and len(secret) == 64, 'Войдите в комнату', 401)
        row = self.db.execute('SELECT member_id,name,role FROM members WHERE secret=? AND room=?',
                              (hashlib.sha256(secret.encode()).hexdigest(), code)).fetchone()
        require(row is not None, 'Нет доступа к комнате', 401)
        self.db.execute('UPDATE members SET seen=? WHERE secret=?',
                        (time.time(), hashlib.sha256(secret.encode()).hexdigest()))
        return dict(id=row[0], name=row[1], role=row[2])

    def add_member(self, code, name, role):
        secret, member_id = secrets.token_hex(32), uid()
        self.db.execute('INSERT INTO members VALUES(?,?,?,?,?,?)',
                        (hashlib.sha256(secret.encode()).hexdigest(), code, member_id, name, role, time.time()))
        return dict(secret=secret, memberId=member_id, role=role, code=code)

    def create(self, body):
        with self.lock, self.db:
            name = text(body.get('name', 'Мастер'), 40)
            code = ''.join(secrets.choice('ABCDEFGHJKLMNPQRSTUVWXYZ23456789') for _ in range(8))
            while self.db.execute('SELECT 1 FROM rooms WHERE code=?', (code,)).fetchone():
                code = ''.join(secrets.choice('ABCDEFGHJKLMNPQRSTUVWXYZ23456789') for _ in range(8))
            s = scene()
            s['tokens'] = [token('Воин', 10, 13, 'hero', 38, '#75baa7'),
                           token('Маг', 12, 14, 'hero', 24, '#aaa0d3'),
                           token('Гоблин', 16, 8), token('Гоблин-лучник', 19, 10)]
            state = dict(formatVersion=2, title=text(body.get('title', 'Новая кампания')),
                         revision=1, sceneId=s['id'], scenes=[s], messages=[],
                         gmNotes='', invite=secrets.token_urlsafe(18), locked=False)
            self.save(code, state)
            return self.add_member(code, name, 'gm')

    def join(self, code, body):
        with self.lock, self.db:
            state = self.load(code)
            require(not state['locked'], 'Мастер закрыл вход', 403)
            supplied = body.get('invite', '')
            require(isinstance(supplied, str) and hmac.compare_digest(state['invite'], supplied),
                    'Неверный ключ приглашения', 403)
            count = self.db.execute('SELECT COUNT(*) FROM members WHERE room=?', (code,)).fetchone()[0]
            require(count < 50, 'В комнате уже 50 участников', 409)
            return self.add_member(code, text(body.get('name'), 40), 'player')

    def resume(self, code, body):
        with self.lock, self.db:
            actor = self.member(body.get('secret'), code)
            return dict(code=code, secret=body['secret'], memberId=actor['id'], role=actor['role'])

    def view(self, code, actor, remember=True):
        original = self.load(code)
        state = copy.deepcopy(original)
        if actor['role'] != 'gm':
            state.pop('gmNotes', None)
            state.pop('invite', None)
            state['scenes'] = [s for s in state['scenes'] if s['id'] == state['sceneId']]
            for s in state['scenes']:
                current = vision_cells(s, actor)
                explored = set()
                if current is not None and s['explorer']:
                    explored = {(p['x'], p['y']) for p in s['exploredBy'].get(actor['id'], [])}
                    combined = explored | current
                    if remember and combined != explored:
                        raw = next(v for v in original['scenes'] if v['id'] == s['id'])
                        raw['exploredBy'][actor['id']] = cells_json(combined)
                        self.save(code, original)
                    explored = combined
                s['currentCells'] = cells_json(current or set())
                s['exploredCells'] = cells_json(explored)
                s['barriers'] = [w for w in s['barriers'] if w['kind'] in ('door', 'window') and
                    not w['secret'] and (current is None or any(
                        (x, y) in current for x in range(max(0,int(min(w['x'],w['ex']))-1), min(s['width'],math.ceil(max(w['x'],w['ex']))+1))
                        for y in range(max(0,int(min(w['y'],w['ey']))-1), min(s['height'],math.ceil(max(w['y'],w['ey']))+1))))]
                s['tokens'] = [t for t in s['tokens'] if visible(t, s, actor, current)]
                ids = {t['id'] for t in s['tokens']}
                state['messages'] = [m for m in state['messages'] if not m.get('refs') or
                    m.get('authorId') == actor['id'] or
                    (m.get('sceneId') == s['id'] and all(ref in ids for ref in m['refs']))]
                if state.get('effect', {}).get('tokenId') not in ids:
                    state.pop('effect', None)
                if s['turnId'] not in ids:
                    s['turnId'] = ''
        for s in state['scenes']:
            s.pop('exploredBy', None)
            s.setdefault('currentCells', [])
            s.setdefault('exploredCells', [])
        state['members'] = [dict(id=r[0], name=r[1], role=r[2], online=r[3] > time.time()-25)
                            for r in self.db.execute('SELECT member_id,name,role,seen FROM members WHERE room=?', (code,))]
        state['selfId'], state['role'] = actor['id'], actor['role']
        return state

    def preview(self, code, secret, body):
        with self.lock, self.db:
            actor = self.member(secret, code)
            gm(actor)
            row = self.db.execute('SELECT member_id,name,role FROM members WHERE room=? AND member_id=?',
                                  (code, body.get('memberId'))).fetchone()
            require(row is not None and row[2] == 'player', 'Выберите игрока этой комнаты', 404)
            return self.view(code, dict(id=row[0], name=row[1], role='player'), remember=False)

    def get(self, code, secret):
        with self.lock, self.db:
            return self.view(code, self.member(secret, code))

    def action(self, code, secret, body):
        with self.lock, self.db:
            actor = self.member(secret, code)
            opid = text(body.get('requestId'), 64)
            if self.db.execute('SELECT 1 FROM operations WHERE room=? AND member=? AND id=?',
                               (code, actor['id'], opid)).fetchone():
                return self.view(code, actor)
            state = self.load(code)
            apply_action(state, actor, body)
            state['revision'] += 1
            state['messages'] = state['messages'][-200:]
            self.save(code, state)
            self.db.execute('INSERT INTO operations VALUES(?,?,?)', (code, actor['id'], opid))
            return self.view(code, actor)

    def upload(self, code, secret, body):
        with self.lock, self.db:
            actor = self.member(secret, code)
            require(actor['role'] == 'gm', 'Только мастер может загружать карту', 403)
            mime = body.get('mime')
            require(mime in ('image/png', 'image/jpeg'), 'Поддерживаются PNG и JPEG')
            try:
                data = base64.b64decode(body.get('data', ''), validate=True)
            except (ValueError, TypeError):
                raise ApiError(400, 'Повреждённое изображение')
            require(0 < len(data) <= 6*1024*1024, 'Размер карты: до 6 МБ')
            width, height = image_dimensions(data, mime)
            require(0 < width <= 8192 and 0 < height <= 8192 and width*height <= 16*1024*1024,
                    'Карта: до 8192 пикселей по стороне и до 16 мегапикселей')
            total = self.db.execute('SELECT COALESCE(SUM(LENGTH(data)),0) FROM assets WHERE room=?', (code,)).fetchone()[0]
            require(total + len(data) <= 100*1024*1024, 'Лимит карт комнаты: 100 МБ', 413)
            asset_id = uid()
            self.db.execute('INSERT INTO assets VALUES(?,?,?,?)', (asset_id, code, mime, data))
            return {'assetId': asset_id}

    def asset(self, code, secret, asset_id):
        with self.lock, self.db:
            actor = self.member(secret, code)
            state = self.load(code)
            allowed = [s for s in state['scenes'] if actor['role'] == 'gm' or s['id'] == state['sceneId']]
            require(any(s['mapId'] == asset_id for s in allowed), 'Карта недоступна', 403)
            row = self.db.execute('SELECT mime,data FROM assets WHERE id=? AND room=?', (asset_id, code)).fetchone()
            require(row is not None, 'Карта не найдена', 404)
            return row


def visible(t, s, actor, current=None):
    if t['hidden'] or t['layer'] == 'gm':
        return False
    if t['ownerId'] == actor['id']:
        return True
    if s['lighting']:
        if current is None:
            current = vision_cells(s, actor)
        return token_in_cells(t, current)
    return not s['fog'] or any(int(t['x']) == p['x'] and int(t['y']) == p['y'] for p in s['revealed'])


def log(state, actor, content, kind='chat', refs=None):
    state['messages'].append(dict(id=uid(), name=actor['name'], text=content, kind=kind,
                                  timestamp=int(time.time()), authorId=actor['id'],
                                  refs=refs or [], sceneId=state['sceneId']))


def parse_dice(expression):
    expression = text(expression, 40).lower().replace(' ', '')
    match = re.fullmatch(r'(\d{1,2})?d(4|6|8|10|12|20|100)([+-]\d{1,3})?', expression)
    require(match is not None, 'Формат: 1d20+5, 2d6, d100')
    count, sides, modifier = int(match[1] or 1), int(match[2]), int(match[3] or 0)
    require(1 <= count <= 30, 'Можно бросить от 1 до 30 кубиков')
    return count, sides, modifier


def change_hp(token, delta):
    if delta < 0:
        absorbed = min(token['tempHp'], -delta)
        token['tempHp'] -= absorbed
        delta += absorbed
    token['hp'] = min(token['maxHp'], max(0, token['hp']+delta))


def perform_attack(state, scene, attacker, actor, body):
    target = next((t for t in scene['tokens'] if t['id'] == body.get('targetId')), None)
    require(target is not None, 'Цель недоступна', 404)
    require(actor['role'] == 'gm' or visible(target, scene, actor), 'Цель недоступна', 403)
    require(target['id'] != attacker['id'], 'Выберите другой жетон')
    a, b = center(attacker), center(target)
    distance = max(0, max(abs(a[0]-b[0]), abs(a[1]-b[1])) - (attacker['size']+target['size']-2)/2)*scene['cellFeet']
    require(distance <= attacker['attackRange']+1e-6, 'Цель вне дальности атаки')
    require(clear_path(scene, a, b, movement=True), 'Атака перекрыта преградой')
    mode = body.get('mode','normal')
    require(mode in ('normal','advantage','disadvantage'), 'Неизвестный режим броска')
    rolls = [secrets.randbelow(20)+1 for _ in range(1 if mode == 'normal' else 2)]
    die = min(rolls) if mode == 'disadvantage' else max(rolls)
    hit = die == 20 or (die != 1 and die+attacker['attackBonus'] >= target['ac'])
    summary = f'{attacker["name"]} → {target["name"]}: d20 [{", ".join(map(str, rolls))}] {attacker["attackBonus"]:+d}'
    refs = [attacker['id'],target['id']]
    if hit:
        count, sides, modifier = parse_dice(attacker['damage'])
        values = [secrets.randbelow(sides)+1 for _ in range(count*(2 if die == 20 else 1))]
        damage = max(0,sum(values)+modifier)
        change_hp(target, -damage)
        summary += (' · КРИТ!' if die == 20 else ' · попадание')+f' · урон {damage} · HP {target["hp"]}/{target["maxHp"]}'
        if damage > 0 and 'Концентрация' in target['conditions']:
            summary += f' · проверка концентрации СЛ {max(10,damage//2)}'
        state['effect'] = dict(id=uid(), sceneId=scene['id'], tokenId=target['id'],
                               kind='critical' if die == 20 else 'hit', timestamp=int(time.time()))
    else:
        summary += ' · промах'
    log(state,actor,summary,'combat',refs)


def gm(actor):
    require(actor['role'] == 'gm', 'Это действие доступно только мастеру', 403)


def current_scene(state, body):
    scene_id = body.get('sceneId', state['sceneId'])
    found = next((s for s in state['scenes'] if s['id'] == scene_id), None)
    require(found is not None, 'Сцена не найдена', 404)
    return found


def apply_action(state, actor, b):
    kind = b.get('type')
    if kind == 'chat':
        log(state, actor, text(b.get('text'), 1000))
        return
    if kind == 'roll':
        expression = text(b.get('text'), 40).lower().replace(' ', '')
        count, sides, mod = parse_dice(expression)
        mode = b.get('mode', 'normal')
        require(mode in ('normal', 'advantage', 'disadvantage'), 'Неизвестный режим броска')
        values = [secrets.randbelow(sides)+1 for _ in range(count)]
        if mode != 'normal':
            require(count == 1 and sides == 20, 'Преимущество применяется к d20')
            values.append(secrets.randbelow(20)+1)
            total = (max(values) if mode == 'advantage' else min(values)) + mod
        else:
            total = sum(values)+mod
        log(state, actor, f'{expression} [{", ".join(map(str, values))}] = {total}' +
            (' · преимущество' if mode == 'advantage' else ' · помеха' if mode == 'disadvantage' else ''), 'roll')
        return
    if kind == 'notes':
        gm(actor)
        require(isinstance(b.get('text'), str) and len(b['text']) <= 20000, 'Лимит заметок: 20000 символов')
        state['gmNotes'] = b['text']
        return
    if kind == 'room':
        gm(actor)
        if 'locked' in b:
            require(type(b['locked']) is bool, 'Нужно логическое значение')
            state['locked'] = b['locked']
        if b.get('rotateInvite') is True:
            state['invite'] = secrets.token_urlsafe(18)
        return
    if kind == 'scene.add':
        gm(actor)
        require(len(state['scenes']) < 30, 'Лимит: 30 сцен')
        s = scene(text(b.get('name')))
        state['scenes'].append(s)
        state['sceneId'] = s['id']
        return
    s = current_scene(state, b)
    require(actor['role'] == 'gm' or s['id'] == state['sceneId'], 'Сцена недоступна', 403)
    if kind == 'scene.switch':
        gm(actor)
        state['sceneId'] = s['id']
    elif kind == 'scene.delete':
        gm(actor)
        require(len(state['scenes']) > 1, 'Нельзя удалить последнюю сцену')
        state['scenes'].remove(s)
        if state['sceneId'] == s['id']:
            state['sceneId'] = state['scenes'][0]['id']
    elif kind == 'scene.update':
        gm(actor)
        for field in ('width', 'height'):
            if field in b:
                value = int(number(b[field], 5, 80))
                require(all(t['x']+t['size'] <= value for t in s['tokens']) if field == 'width'
                        else all(t['y']+t['size'] <= value for t in s['tokens']), 'Жетоны выходят за новые границы карты')
                s[field] = value
        if 'cellFeet' in b:
            s['cellFeet'] = int(number(b['cellFeet'], 1, 100))
        for field in ('grid', 'snap', 'fog', 'lighting', 'daylight', 'explorer', 'sharedVision', 'restrictMovement', 'enforceSpeed'):
            if field in b:
                require(type(b[field]) is bool, 'Нужно логическое значение')
                s[field] = b[field]
        if 'name' in b:
            s['name'] = text(b['name'])
        if 'mapId' in b:
            require(isinstance(b['mapId'], str) and re.fullmatch(r'[0-9a-f]{16}|', b['mapId']), 'Неверный ID карты')
            s['mapId'] = b['mapId']
    elif kind == 'token.add':
        gm(actor)
        require(len(s['tokens']) < 100, 'Лимит: 100 жетонов на сцену')
        t = token(text(b.get('name')), 0, 0, b.get('kind', 'enemy'))
        require(t['kind'] in ('hero', 'enemy', 'object'), 'Неизвестный тип жетона')
        s['tokens'].append(t)
        upgrade(state)
    elif kind in ('token.move', 'token.update', 'token.delete', 'token.duplicate', 'token.hp', 'token.attack', 'token.macro'):
        t = next((t for t in s['tokens'] if t['id'] == b.get('tokenId')), None)
        require(t is not None, 'Жетон не найден', 404)
        require(actor['role'] == 'gm' or (t['ownerId'] == actor['id'] and t['layer'] == 'tokens' and visible(t, s, actor)), 'Это чужой или скрытый жетон', 403)
        if kind == 'token.duplicate':
            gm(actor)
            require(len(s['tokens']) < 100, 'Лимит: 100 жетонов')
            duplicate = copy.deepcopy(t)
            duplicate.update(id=uid(), name=t['name']+' (копия)', movedFeet=0)
            duplicate['x'] = min(s['width']-t['size'], t['x']+1)
            s['tokens'].append(duplicate)
        elif kind == 'token.hp':
            delta = int(number(b.get('delta'), -9999, 9999))
            change_hp(t, delta)
            log(state, actor, t['name']+f': HP {delta:+d} → {t["hp"]}/{t["maxHp"]}', 'combat', [t['id']])
        elif kind == 'token.attack':
            perform_attack(state, s, t, actor, b)
        elif kind == 'token.macro':
            macro = next((m for m in t['macros'] if m['id'] == b.get('macroId')), None)
            require(macro is not None, 'Действие не найдено', 404)
            apply_action(state, actor, dict(type='roll', text=macro['formula'], mode=b.get('mode','normal')))
        elif kind == 'token.move':
            x, y = number(b.get('x'), 0, s['width']-t['size']), number(b.get('y'), 0, s['height']-t['size'])
            if s['snap']:
                x, y = math.floor(x+0.5), math.floor(y+0.5)
            if actor['role'] != 'gm':
                require(t['layer'] == 'tokens' and not t['locked'], 'Жетон заблокирован мастером', 403)
                if s['restrictMovement']:
                    require(clear_path(s, center(t), (x+t['size']/2,y+t['size']/2), movement=True), 'Путь перекрыт стеной или дверью')
                if s['activeCombat'] and s['enforceSpeed']:
                    require(s['turnId'] == t['id'], 'Сейчас ход другого участника', 403)
                    cost = max(abs(x-t['x']), abs(y-t['y']))*s['cellFeet']
                    require(t['movedFeet']+cost <= t['speed']+1e-6, 'На этом ходу не хватает перемещения')
                    t['movedFeet'] += cost
            t['x'], t['y'] = x, y
        elif kind == 'token.delete':
            gm(actor)
            s['tokens'].remove(t)
            if s['turnId'] == t['id']:
                s['turnId'] = ''
        else:
            if actor['role'] != 'gm':
                require(set(b) <= {'type', 'requestId', 'sceneId', 'tokenId', 'hp', 'conditions'}, 'Только HP и состояния своего жетона', 403)
            if 'name' in b:
                t['name'] = text(b['name'])
            for field, lo, hi in [('maxHp', 1, 9999), ('hp', 0, 9999), ('ac', 0, 99), ('initiative', -100, 100),
                    ('visionRange',0,300), ('nightVision',0,300), ('lightRadius',0,300),
                    ('auraFeet',0,100), ('speed',0,300), ('tempHp',0,9999),
                    ('attackBonus',-50,50), ('attackRange',0,600)]:
                if field in b:
                    t[field] = int(number(b[field], lo, hi))
            t['hp'] = min(t['hp'], t['maxHp'])
            if 'size' in b:
                size = int(number(b['size'], 1, 4))
                require(t['x']+size <= s['width'] and t['y']+size <= s['height'], 'Жетон за границей карты')
                t['size'] = size
            if 'ownerId' in b:
                t['ownerId'] = text(b['ownerId']) if b['ownerId'] else ''
            if 'hidden' in b:
                require(type(b['hidden']) is bool, 'Нужно логическое значение')
                t['hidden'] = b['hidden']
            if 'color' in b:
                require(isinstance(b['color'], str) and re.fullmatch(r'#[0-9a-fA-F]{6}', b['color']), 'Нужен цвет #RRGGBB')
                t['color'] = b['color']
            for field in ('locked',):
                if field in b:
                    require(type(b[field]) is bool, 'Нужно логическое значение')
                    t[field] = b[field]
            if 'layer' in b:
                require(b['layer'] in ('tokens','map','gm'), 'Неизвестный слой')
                t['layer'] = b['layer']
            if 'damage' in b:
                parse_dice(b['damage'])
                t['damage'] = b['damage']
            if 'macros' in b:
                require(isinstance(b['macros'],list) and len(b['macros']) <= 12, 'До 12 действий на жетон')
                macros = []
                for macro in b['macros']:
                    require(isinstance(macro,dict), 'Неверное действие')
                    parse_dice(macro.get('formula'))
                    macros.append(dict(id=uid(), name=text(macro.get('name'),40), formula=macro['formula']))
                t['macros'] = macros
            if 'conditions' in b:
                require(isinstance(b['conditions'], list) and all(isinstance(v, str) and v in CONDITIONS for v in b['conditions']), 'Неизвестное состояние')
                t['conditions'] = list(dict.fromkeys(b['conditions']))
    elif kind == 'barrier.add':
        gm(actor)
        require(len(s['barriers']) < 200, 'Лимит: 200 отрезков стен и дверей')
        barrier_kind = b.get('kind','wall')
        require(barrier_kind in ('wall','door','window'), 'Неизвестная преграда')
        w = dict(id=uid(), kind=barrier_kind, open=False, locked=False, secret=False,
                 x=number(b.get('x'),0,s['width']), y=number(b.get('y'),0,s['height']),
                 ex=number(b.get('ex'),0,s['width']), ey=number(b.get('ey'),0,s['height']))
        require(math.dist((w['x'],w['y']),(w['ex'],w['ey'])) >= 0.1, 'Слишком короткая преграда')
        s['barriers'].append(w)
    elif kind in ('barrier.update','barrier.delete','door.toggle'):
        w = next((w for w in s['barriers'] if w['id'] == b.get('barrierId')),None)
        require(w is not None, 'Преграда не найдена',404)
        if kind == 'door.toggle':
            require(w['kind'] in ('door','window'), 'Это не дверь или окно')
            if actor['role'] != 'gm':
                require(not w['secret'] and not w['locked'], 'Дверь недоступна',403)
                midpoint = ((w['x']+w['ex'])/2,(w['y']+w['ey'])/2)
                current = vision_cells(s, actor)
                require(any(t['ownerId'] == actor['id'] and visible(t,s,actor,current) and
                    math.dist(center(t),midpoint) <= 1.5 and clear_path(s,center(t),
                    (center(t)[0]+(midpoint[0]-center(t)[0])*0.999,
                     center(t)[1]+(midpoint[1]-center(t)[1])*0.999), movement=True)
                    for t in s['tokens']), 'Подойдите к двери своим жетоном',403)
            require(not w['locked'], 'Сначала снимите замок')
            w['open'] = not w['open']
        elif kind == 'barrier.delete':
            gm(actor)
            s['barriers'].remove(w)
        else:
            gm(actor)
            for field in ('open','locked','secret'):
                if field in b:
                    require(type(b[field]) is bool,'Нужно логическое значение')
                    w[field] = b[field]
            require(not (w['open'] and w['locked']), 'Открытую дверь нельзя запереть')
    elif kind == 'barrier.clear':
        gm(actor)
        s['barriers'] = []
    elif kind == 'vision.reset':
        gm(actor)
        s['exploredBy'] = {}
    elif kind == 'fog.paint':
        gm(actor)
        x, y = int(number(b.get('x'), 0, s['width']-1)), int(number(b.get('y'), 0, s['height']-1))
        radius = int(number(b.get('radius', 1), 0, 10))
        cells = {(p['x'], p['y']) for p in s['revealed']}
        for xx in range(max(0, x-radius), min(s['width'], x+radius+1)):
            for yy in range(max(0, y-radius), min(s['height'], y+radius+1)):
                if b.get('reveal', True):
                    cells.add((xx, yy))
                else:
                    cells.discard((xx, yy))
        s['revealed'] = [dict(x=xx, y=yy) for xx, yy in sorted(cells)]
    elif kind == 'fog.reset':
        gm(actor)
        s['revealed'] = []
    elif kind == 'drawing.add':
        gm(actor)
        require(len(s['drawings']) < 200, 'Лимит: 200 рисунков')
        shape = b.get('shape')
        require(shape in ('line', 'circle', 'cone'), 'Неизвестный шаблон')
        s['drawings'].append(dict(id=uid(), shape=shape,
            x=number(b.get('x'), 0, s['width']), y=number(b.get('y'), 0, s['height']),
            ex=number(b.get('ex'), 0, s['width']), ey=number(b.get('ey'), 0, s['height'])))
    elif kind == 'drawing.clear':
        gm(actor)
        s['drawings'] = []
    elif kind == 'ping':
        state['ping'] = dict(x=number(b.get('x'), 0, s['width']), y=number(b.get('y'), 0, s['height']),
                             sceneId=s['id'], timestamp=int(time.time()), name=actor['name'])
    elif kind == 'combat.start':
        gm(actor)
        ordered = sorted((t for t in s['tokens'] if t['kind'] != 'object' and t['layer'] != 'map'), key=lambda t: (-t['initiative'], t['id']))
        require(len(ordered) > 0, 'Добавьте участников боя')
        for t in s['tokens']:
            t['movedFeet'] = 0
        s.update(activeCombat=True, round=1, turnId=ordered[0]['id'])
    elif kind == 'combat.next':
        gm(actor)
        require(s['activeCombat'], 'Сначала начните бой')
        ordered = sorted((t for t in s['tokens'] if t['kind'] != 'object' and t['layer'] != 'map'), key=lambda t: (-t['initiative'], t['id']))
        require(len(ordered) > 0, 'Нет участников боя')
        index = next((i for i, t in enumerate(ordered) if t['id'] == s['turnId']), -1) + 1
        if index >= len(ordered):
            index = 0
            s['round'] += 1
        s['turnId'] = ordered[index]['id']
        ordered[index]['movedFeet'] = 0
    elif kind == 'combat.stop':
        gm(actor)
        s.update(activeCombat=False, turnId='')
        for t in s['tokens']:
            t['movedFeet'] = 0
    else:
        raise ApiError(400, 'Неизвестное действие')


class Handler(BaseHTTPRequestHandler):
    server_version = 'FoxTable/0.2'

    def log_message(self, fmt, *args):
        pass  # Never log invite keys or bearer credentials.

    def send(self, status, payload, mime='application/json; charset=utf-8'):
        data = json.dumps(payload, ensure_ascii=False).encode() if not isinstance(payload, bytes) else payload
        self.send_response(status)
        self.send_header('Content-Type', mime)
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Cache-Control', 'no-store')
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.end_headers()
        self.wfile.write(data)

    def route(self):
        try:
            path = self.path.split('?')[0].strip('/').split('/')
            body = {}
            if self.command == 'POST':
                try:
                    length = int(self.headers.get('Content-Length', '0'))
                except ValueError:
                    raise ApiError(400, 'Неверная длина запроса')
                require(0 < length <= MAX_BODY, 'Слишком большой запрос', 413)
                try:
                    body = json.loads(self.rfile.read(length))
                except (ValueError, UnicodeDecodeError):
                    raise ApiError(400, 'Неверный JSON')
                require(isinstance(body, dict), 'Нужен объект JSON')
            store = self.server.store
            if path == ['health'] and self.command == 'GET':
                return self.send(200, {'status': 'ok', 'version': '0.2.0'})
            if path == ['api', 'rooms'] and self.command == 'POST':
                return self.send(201, store.create(body))
            require(len(path) >= 3 and path[:2] == ['api', 'rooms'], 'Не найдено', 404)
            code = path[2].upper()
            require(re.fullmatch('[A-Z2-9]{8}', code) is not None, 'Неверный код комнаты')
            secret = self.headers.get('Authorization', '').removeprefix('Bearer ')
            if len(path) == 4 and path[3] == 'join' and self.command == 'POST':
                return self.send(200, store.join(code, body))
            if len(path) == 4 and path[3] == 'resume' and self.command == 'POST':
                return self.send(200, store.resume(code, body))
            if len(path) == 3 and self.command == 'GET':
                return self.send(200, store.get(code, secret))
            if len(path) == 4 and path[3] == 'preview' and self.command == 'POST':
                return self.send(200, store.preview(code, secret, body))
            if len(path) == 4 and path[3] == 'actions' and self.command == 'POST':
                return self.send(200, store.action(code, secret, body))
            if len(path) == 4 and path[3] == 'assets' and self.command == 'POST':
                return self.send(201, store.upload(code, secret, body))
            if len(path) == 5 and path[3] == 'assets' and self.command == 'GET':
                mime, data = store.asset(code, secret, path[4])
                return self.send(200, data, mime)
            raise ApiError(404, 'Не найдено')
        except ApiError as ex:
            self.send(ex.status, {'error': ex.message})
        except (KeyError, TypeError, ValueError):
            self.send(400, {'error': 'Некорректные поля запроса'})
        except Exception:
            self.send(500, {'error': 'Ошибка сервера'})

    do_GET = route
    do_POST = route


def make_server(host='127.0.0.1', port=8787, database='foxtable.sqlite3'):
    server = ThreadingHTTPServer((host, port), Handler)
    server.store = Store(database)
    return server


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--host', default='127.0.0.1')
    parser.add_argument('--port', type=int, default=8787)
    parser.add_argument('--database', default='foxtable.sqlite3')
    args = parser.parse_args()
    server = make_server(args.host, args.port, args.database)
    print(f'FoxTable listening on {args.host}:{args.port}', flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
