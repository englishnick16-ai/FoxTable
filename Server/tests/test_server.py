import base64
import json
import tempfile
import threading
import unittest
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from server import Store, ApiError, make_server


class RoomTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = str(Path(self.temp.name) / 'test.sqlite3')
        self.store = Store(self.path)
        self.host = self.store.create({'name': 'Мастер', 'title': 'Тестовая игра'})
        self.code = self.host['code']
        self.state = self.store.get(self.code, self.host['secret'])
        self.player = self.store.join(self.code, {'name': 'Игрок', 'invite': self.state['invite']})

    def tearDown(self):
        self.store.db.close()
        self.temp.cleanup()

    def act(self, who, action_type, **fields):
        return self.store.action(self.code, who['secret'], {'requestId': uuid.uuid4().hex, 'type': action_type, **fields})

    def view(self, who=None):
        return self.store.get(self.code, (who or self.host)['secret'])

    def scene(self, who=None):
        state = self.view(who)
        return next(s for s in state['scenes'] if s['id'] == state['sceneId'])

    def assertDenied(self, fn, code=403):
        with self.assertRaises(ApiError) as ex:
            fn()
        self.assertEqual(ex.exception.status, code)

    def test_new_player_has_no_private_notes_or_invite(self):
        self.act(self.host, 'notes', text='Секретный босс')
        self.act(self.host, 'scene.add', name='Секретная сцена')
        state = self.view(self.player)
        self.assertNotIn('gmNotes', state)
        self.assertNotIn('invite', state)
        self.assertEqual(len(state['scenes']), 1)

    def test_wrong_invite_and_room_lock(self):
        self.assertDenied(lambda: self.store.join(self.code, {'name': 'Чужой', 'invite': 'wrong'}))
        self.act(self.host, 'room', locked=True)
        self.assertDenied(lambda: self.store.join(self.code, {'name': 'Поздний', 'invite': self.state['invite']}))
        self.assertEqual(self.view(self.player)['role'], 'player')

    def test_invite_rotation(self):
        new = self.act(self.host, 'room', rotateInvite=True)['invite']
        self.assertNotEqual(new, self.state['invite'])
        self.assertDenied(lambda: self.store.join(self.code, {'name': 'Поздний', 'invite': self.state['invite']}))

    def test_unauthenticated_state_denied(self):
        self.assertDenied(lambda: self.store.get(self.code, '0'*64), 401)

    def test_foreign_room_denied(self):
        other = self.store.create({'name': 'Другой'})
        self.assertDenied(lambda: self.store.get(other['code'], self.host['secret']), 401)

    def test_owned_token_move_and_other_token_rejection(self):
        hero, other = self.scene()['tokens'][:2]
        self.act(self.host, 'token.update', tokenId=hero['id'], ownerId=self.player['memberId'])
        self.act(self.player, 'token.move', tokenId=hero['id'], x=5.2, y=6.8)
        self.assertEqual((self.scene()['tokens'][0]['x'], self.scene()['tokens'][0]['y']), (5, 7))
        self.assertDenied(lambda: self.act(self.player, 'token.move', tokenId=other['id'], x=2, y=3))

    def test_player_cannot_escalate_permissions(self):
        hero = self.scene()['tokens'][0]
        self.act(self.host, 'token.update', tokenId=hero['id'], ownerId=self.player['memberId'])
        self.assertDenied(lambda: self.act(self.player, 'token.update', tokenId=hero['id'], maxHp=999))
        self.assertDenied(lambda: self.act(self.player, 'scene.update', fog=False))
        self.assertDenied(lambda: self.act(self.player, 'notes', text='Заменить'))
        self.assertDenied(lambda: self.act(self.player, 'combat.start'))

    def test_hp_conditions_and_maximum(self):
        hero = self.scene()['tokens'][0]
        self.act(self.host, 'token.update', tokenId=hero['id'], ownerId=self.player['memberId'])
        self.act(self.player, 'token.update', tokenId=hero['id'], hp=999, conditions=['Концентрация'])
        updated = self.scene()['tokens'][0]
        self.assertEqual(updated['hp'], updated['maxHp'])
        self.assertEqual(updated['conditions'], ['Концентрация'])

    def test_hidden_token_not_in_payload(self):
        hero = self.scene()['tokens'][0]
        self.act(self.host, 'token.update', tokenId=hero['id'], hidden=True)
        ids = [t['id'] for t in self.scene(self.player)['tokens']]
        self.assertNotIn(hero['id'], ids)

    def test_fog_reveal_and_refog(self):
        hero = self.scene()['tokens'][0]
        self.act(self.host, 'scene.update', fog=True)
        self.assertEqual(self.scene(self.player)['tokens'], [])
        self.act(self.host, 'fog.paint', x=hero['x'], y=hero['y'], radius=0, reveal=True)
        self.assertEqual([t['id'] for t in self.scene(self.player)['tokens']], [hero['id']])
        self.act(self.host, 'fog.paint', x=hero['x'], y=hero['y'], radius=0, reveal=False)
        self.assertEqual(self.scene(self.player)['tokens'], [])

    def test_owner_visible_in_fog_but_hidden_overrides_ownership(self):
        hero = self.scene()['tokens'][0]
        self.act(self.host, 'token.update', tokenId=hero['id'], ownerId=self.player['memberId'])
        self.act(self.host, 'scene.update', fog=True)
        self.assertEqual(len(self.scene(self.player)['tokens']), 1)
        self.act(self.host, 'token.update', tokenId=hero['id'], hidden=True)
        self.assertEqual(len(self.scene(self.player)['tokens']), 0)

    def test_request_replay_not_applied_twice(self):
        body = {'type': 'chat', 'text': 'Один раз', 'requestId': 'unique-request'}
        first = self.store.action(self.code, self.player['secret'], body)
        second = self.store.action(self.code, self.player['secret'], body)
        self.assertEqual(first['revision'], second['revision'])
        self.assertEqual(len(second['messages']), 1)

    def test_initiative_round_rollover(self):
        tokens = self.scene()['tokens']
        for i, t in enumerate(tokens):
            self.act(self.host, 'token.update', tokenId=t['id'], initiative=20-i)
        self.act(self.host, 'combat.start')
        self.assertEqual(self.scene()['turnId'], tokens[0]['id'])
        for _ in tokens:
            self.act(self.host, 'combat.next')
        self.assertEqual(self.scene()['round'], 2)
        self.assertEqual(self.scene()['turnId'], tokens[0]['id'])

    def test_failed_operation_does_not_partially_commit(self):
        t = self.scene()['tokens'][0]
        before = self.view()['revision']
        self.assertDenied(lambda: self.act(self.host, 'token.update', tokenId=t['id'], hp=1, color='bad'), 400)
        self.assertEqual(self.scene()['tokens'][0]['hp'], t['hp'])
        self.assertEqual(self.view()['revision'], before)

    def test_out_of_bounds_and_nonfinite_rejected(self):
        t = self.scene()['tokens'][0]
        for x in [-1, 999, float('nan'), float('inf'), True]:
            self.assertDenied(lambda: self.act(self.host, 'token.move', tokenId=t['id'], x=x, y=1), 400)

    def test_dice_bounds_and_chat(self):
        for expression in ('1d20+5', '2d6', 'd100'):
            state = self.act(self.player, 'roll', text=expression)
            self.assertEqual(state['messages'][-1]['kind'], 'roll')
        self.act(self.player, 'roll', text='1d20+3', mode='advantage')
        for expression in ('0d6', '99d20', 'eval(1)', '1d7'):
            self.assertDenied(lambda: self.act(self.player, 'roll', text=expression), 400)
        self.act(self.player, 'chat', text='Привет, мир!')
        self.assertEqual(self.view()['messages'][-1]['text'], 'Привет, мир!')

    def test_scene_switch_and_last_scene_protection(self):
        self.assertDenied(lambda: self.act(self.host, 'scene.delete'), 400)
        previous = self.view()['sceneId']
        next_state = self.act(self.host, 'scene.add', name='Второй этаж')
        self.assertNotEqual(next_state['sceneId'], previous)
        self.assertEqual(self.view(self.player)['sceneId'], next_state['sceneId'])
        self.assertDenied(lambda: self.act(self.player, 'ping', sceneId=previous, x=1, y=1))
        self.act(self.host, 'scene.delete')
        self.assertEqual(self.view()['sceneId'], previous)

    def test_persistence_and_late_join(self):
        self.act(self.host, 'notes', text='Сохранено')
        self.store.db.close()
        self.store = Store(self.path)
        self.assertEqual(self.view()['gmNotes'], 'Сохранено')
        new = self.store.join(self.code, {'name': 'Поздний', 'invite': self.state['invite']})
        self.assertEqual(len(self.view(new)['members']), 3)

    def test_map_upload_and_access(self):
        png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nX8AAAAASUVORK5CYII='
        body = {'mime': 'image/png', 'data': png}
        self.assertDenied(lambda: self.store.upload(self.code, self.player['secret'], body))
        asset = self.store.upload(self.code, self.host['secret'], body)['assetId']
        self.act(self.host, 'scene.update', mapId=asset)
        self.assertEqual(self.store.asset(self.code, self.player['secret'], asset)[0], 'image/png')
        self.act(self.host, 'scene.add', name='Новая сцена')
        self.assertDenied(lambda: self.store.asset(self.code, self.player['secret'], asset))

    def test_recovery_keeps_master_role(self):
        restored = self.store.resume(self.code, {'secret': self.host['secret']})
        self.assertEqual(restored['role'], 'gm')
        self.assertEqual(restored['memberId'], self.host['memberId'])
        self.assertDenied(lambda: self.store.resume(self.code, {'secret': '0'*64}), 401)

    def test_corrupt_and_oversized_image_rejected(self):
        self.assertDenied(lambda: self.store.upload(self.code, self.host['secret'],
            {'mime': 'image/png', 'data': base64.b64encode(b'not-png').decode()}), 400)
        header = b'\x89PNG\r\n\x1a\n\x00\x00\x00\x0dIHDR' + (99999).to_bytes(4,'big')*2 + b'\x00'*9
        self.assertDenied(lambda: self.store.upload(self.code, self.host['secret'],
            {'mime': 'image/png', 'data': base64.b64encode(header).decode()}), 400)

    def test_drawings_and_gm_only_clear(self):
        self.act(self.host, 'drawing.add', shape='cone', x=1, y=1, ex=5, ey=3)
        self.assertEqual(self.scene(self.player)['drawings'][0]['shape'], 'cone')
        self.assertDenied(lambda: self.act(self.player, 'drawing.clear'))
        self.act(self.host, 'drawing.clear')
        self.assertEqual(self.scene()['drawings'], [])

    def test_concurrent_operations_no_lost_updates(self):
        def send(i):
            self.act(self.player, 'chat', text=f'Сообщение {i}')
        with ThreadPoolExecutor(max_workers=8) as executor:
            list(executor.map(send, range(20)))
        self.assertEqual(len(self.view()['messages']), 20)


class HttpIntegrationTests(unittest.TestCase):
    def test_two_http_participants_share_state(self):
        with tempfile.TemporaryDirectory() as tmp:
            server = make_server(port=0, database=str(Path(tmp)/'live.db'))
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            origin = f'http://127.0.0.1:{server.server_port}'
            def request(path, body=None, secret=None):
                headers = {'Content-Type': 'application/json'}
                if secret:
                    headers['Authorization'] = 'Bearer '+secret
                req = Request(origin+path, data=json.dumps(body).encode() if body is not None else None, headers=headers)
                with urlopen(req, timeout=5) as response:
                    return json.load(response)
            try:
                gm = request('/api/rooms', {'name': 'Мастер', 'title': 'Сетевая проверка'})
                path = '/api/rooms/'+gm['code']
                state = request(path, secret=gm['secret'])
                player = request(path+'/join', {'name': 'Игрок', 'invite': state['invite']})
                token = state['scenes'][0]['tokens'][0]
                request(path+'/actions', {'type': 'token.update', 'tokenId': token['id'],
                    'ownerId': player['memberId'], 'requestId': uuid.uuid4().hex}, gm['secret'])
                request(path+'/actions', {'type': 'token.move', 'tokenId': token['id'],
                    'x': 7, 'y': 9, 'requestId': uuid.uuid4().hex}, player['secret'])
                observer = request(path, secret=gm['secret'])
                moved = observer['scenes'][0]['tokens'][0]
                self.assertEqual((moved['x'], moved['y']), (7, 9))
                enemy = state['scenes'][0]['tokens'][2]
                def action(who, action_type, **fields):
                    return request(path+'/actions', {'type':action_type,'requestId':uuid.uuid4().hex,**fields},who['secret'])
                action(gm,'scene.update',lighting=True)
                action(gm,'barrier.add',kind='door',x=9,y=0,ex=9,ey=22)
                restricted = request(path,secret=player['secret'])
                self.assertNotIn(enemy['id'],[t['id'] for t in restricted['scenes'][0]['tokens']])
                preview = request(path+'/preview',{'memberId':player['memberId']},gm['secret'])
                self.assertEqual(preview['scenes'],restricted['scenes'])
                door = request(path,secret=gm['secret'])['scenes'][0]['barriers'][0]
                action(gm,'door.toggle',barrierId=door['id'])
                opened = request(path,secret=player['secret'])
                self.assertIn(enemy['id'],[t['id'] for t in opened['scenes'][0]['tokens']])
                with self.assertRaises(HTTPError) as ex:
                    request(path)
                self.assertEqual(ex.exception.code, 401)
                with self.assertRaises(HTTPError) as ex:
                    request(path+'/actions', {'type': 'combat.start', 'requestId': 'bad'}, player['secret'])
                self.assertEqual(ex.exception.code, 403)
            finally:
                server.shutdown()
                thread.join(timeout=3)
                server.server_close()
                server.store.db.close()


if __name__ == '__main__':
    unittest.main()
