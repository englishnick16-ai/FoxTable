using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FoxTable
{
    // One-device GM authority. Online rooms continue to use the Python server.
    public sealed class LocalGame
    {
        private readonly Random random;
        public LocalGame(Random random = null) { this.random = random ?? new Random(); }
        private static string Id() => Guid.NewGuid().ToString("N").Substring(0, 16);
        private static void Need(bool condition, string message)
        { if (!condition) throw new ArgumentException(message); }
        private static string Text(Dictionary<string, object> b, string key, string fallback = "", int limit = 80)
        {
            string value = b.TryGetValue(key, out var raw) ? Convert.ToString(raw, CultureInfo.InvariantCulture) : fallback;
            value = (value ?? "").Trim();
            Need(value.Length <= limit, "Слишком длинный текст: " + limit + " символов");
            return value;
        }
        private static float Num(Dictionary<string, object> b, string key, float min, float max, float fallback = 0)
        {
            float value = b.TryGetValue(key, out var raw) ? Convert.ToSingle(raw, CultureInfo.InvariantCulture) : fallback;
            Need(!float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max,
                key + ": допустимо от " + min + " до " + max);
            return value;
        }
        private static bool Flag(Dictionary<string, object> b, string key, bool fallback = false)
        { return b.TryGetValue(key, out var value) ? (bool)value : fallback; }
        private static T[] Add<T>(T[] array, T item) => array.Concat(new[] { item }).ToArray();
        public static SceneState NewScene(string name) => new SceneState {
            id = Id(), name = name, width = 30, height = 22, cellFeet = 5, grid = true, snap = true,
            mapId = "", round = 1, turnId = "", daylight = true, explorer = true, restrictMovement = true
        };
        private static TokenState NewToken(string name, string kind, float x = 0, float y = 0, int hp = 12) => new TokenState {
            id = Id(), name = name, kind = kind, x = x, y = y, size = 1, hp = hp, maxHp = hp, ac = 12,
            color = kind == "hero" ? "#75baa7" : "#b9605c", ownerId = "", layer = "tokens",
            visionRange = 60, speed = 30, attackBonus = 3, damage = "1d6+1", attackRange = 5
        };
        public static RoomState Create()
        {
            var scene = NewScene("Забытое святилище");
            scene.tokens = new[] { NewToken("Воин", "hero", 10, 13, 38), NewToken("Маг", "hero", 12, 14, 24),
                NewToken("Гоблин", "enemy", 16, 8), NewToken("Гоблин-лучник", "enemy", 19, 10) };
            return new RoomState { formatVersion = 2, revision = 1, title = "Моя локальная кампания", sceneId = scene.id,
                role = "gm", selfId = "local-gm", gmNotes = "", invite = "", scenes = new[] { scene },
                members = new[] { new Member { id = "local-gm", name = "Мастер", role = "gm", online = true } } };
        }

        public void Apply(RoomState state, Dictionary<string, object> b)
        {
            string type = Text(b, "type");
            if (type == "chat") { Log(state, Text(b, "text", limit: 1000), "chat"); return; }
            if (type == "roll") { Roll(state, Text(b, "text", limit: 40), Text(b, "mode", "normal")); return; }
            if (type == "notes") { state.gmNotes = Text(b, "text", limit: 20000); return; }
            if (type == "room") { state.title = Text(b, "title", state.title); return; }
            if (type == "scene.add") {
                Need(state.scenes.Length < 30, "Лимит: 30 сцен");
                var added = NewScene(Text(b, "name", "Новая сцена"));
                state.scenes = Add(state.scenes, added); state.sceneId = added.id; return;
            }
            string sceneId = Text(b, "sceneId", state.sceneId);
            var s = Array.Find(state.scenes, v => v.id == sceneId);
            Need(s != null, "Сцена не найдена");
            switch (type)
            {
                case "scene.switch": state.sceneId = s.id; break;
                case "scene.delete":
                    Need(state.scenes.Length > 1, "Нельзя удалить последнюю сцену");
                    state.scenes = state.scenes.Where(v => v != s).ToArray();
                    if (state.sceneId == s.id) state.sceneId = state.scenes[0].id;
                    break;
                case "scene.update": UpdateScene(s, b); break;
                case "token.add":
                    Need(s.tokens.Length < 100, "Лимит: 100 жетонов");
                    string kind = Text(b, "kind", "enemy");
                    Need(new[] { "hero", "enemy", "object" }.Contains(kind), "Неизвестный тип жетона");
                    s.tokens = Add(s.tokens, NewToken(Text(b, "name", "Жетон"), kind)); break;
                case "token.move": case "token.update": case "token.delete": case "token.duplicate":
                case "token.hp": case "token.attack": case "token.macro":
                    TokenAction(state, s, b, type); break;
                case "barrier.add":
                    Need(s.barriers.Length < 200, "Лимит: 200 преград");
                    var wall = new Barrier { id = Id(), kind = Text(b, "kind", "wall"),
                        x = Num(b, "x", 0, s.width), y = Num(b, "y", 0, s.height),
                        ex = Num(b, "ex", 0, s.width), ey = Num(b, "ey", 0, s.height) };
                    Need(new[] { "wall", "door", "window" }.Contains(wall.kind), "Неизвестная преграда");
                    Need(Math.Pow(wall.ex - wall.x, 2) + Math.Pow(wall.ey - wall.y, 2) >= 0.01, "Слишком короткая преграда");
                    s.barriers = Add(s.barriers, wall); break;
                case "barrier.update": case "barrier.delete": case "door.toggle":
                    var w = Array.Find(s.barriers, v => v.id == Text(b, "barrierId"));
                    Need(w != null, "Преграда не найдена");
                    if (type == "barrier.delete") s.barriers = s.barriers.Where(v => v != w).ToArray();
                    else if (type == "door.toggle") {
                        Need(w.kind != "wall", "Это не дверь или окно");
                        Need(!w.locked, "Сначала снимите замок"); w.open = !w.open;
                    } else {
                        w.open = Flag(b, "open", w.open); w.locked = Flag(b, "locked", w.locked);
                        w.secret = Flag(b, "secret", w.secret);
                        Need(!(w.open && w.locked), "Открытую дверь нельзя запереть");
                    } break;
                case "barrier.clear": s.barriers = Array.Empty<Barrier>(); break;
                case "vision.reset": s.exploredCells = Array.Empty<Cell>(); break;
                case "fog.paint": Paint(s, b); break;
                case "fog.reset": s.revealed = Array.Empty<Cell>(); break;
                case "drawing.add":
                    Need(s.drawings.Length < 200, "Лимит: 200 шаблонов");
                    string shape = Text(b, "shape");
                    Need(new[] { "line", "circle", "cone" }.Contains(shape), "Неизвестный шаблон");
                    s.drawings = Add(s.drawings, new Drawing { id = Id(), shape = shape,
                        x = Num(b, "x", 0, s.width), y = Num(b, "y", 0, s.height),
                        ex = Num(b, "ex", 0, s.width), ey = Num(b, "ey", 0, s.height) }); break;
                case "drawing.clear": s.drawings = Array.Empty<Drawing>(); break;
                case "ping": state.ping = new Ping { x = Num(b, "x", 0, s.width), y = Num(b, "y", 0, s.height),
                    sceneId = s.id, name = "Мастер", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }; break;
                case "combat.start": case "combat.next": case "combat.stop": Combat(s, type); break;
                default: throw new ArgumentException("Действие недоступно локально: " + type);
            }
        }

        public RoomState ApplyCopy(RoomState state, Dictionary<string, object> operation)
        {
            // A rejected operation must never leave a partially mutated table behind.
            var next = UnityEngine.JsonUtility.FromJson<RoomState>(UnityEngine.JsonUtility.ToJson(state));
            Apply(next, operation); next.revision++;
            return next;
        }

        private static void UpdateScene(SceneState s, Dictionary<string, object> b)
        {
            int width = (int)Num(b, "width", 5, 80, s.width), height = (int)Num(b, "height", 5, 80, s.height);
            Need(s.tokens.All(t => t.x + t.size <= width && t.y + t.size <= height), "Жетоны выходят за новые границы карты");
            s.width = width; s.height = height; s.cellFeet = (int)Num(b, "cellFeet", 1, 100, s.cellFeet);
            s.name = Text(b, "name", s.name); s.grid = Flag(b, "grid", s.grid); s.snap = Flag(b, "snap", s.snap);
            s.fog = Flag(b, "fog", s.fog); s.lighting = Flag(b, "lighting", s.lighting);
            s.daylight = Flag(b, "daylight", s.daylight); s.explorer = Flag(b, "explorer", s.explorer);
            s.sharedVision = Flag(b, "sharedVision", s.sharedVision); s.restrictMovement = Flag(b, "restrictMovement", s.restrictMovement);
            s.enforceSpeed = Flag(b, "enforceSpeed", s.enforceSpeed);
            if (b.ContainsKey("mapId")) {
                string map = Text(b, "mapId"); Need(Regex.IsMatch(map, "^([0-9a-f]{16})?$"), "Неверный ID карты"); s.mapId = map;
            }
        }

        private void TokenAction(RoomState state, SceneState s, Dictionary<string, object> b, string type)
        {
            var t = Array.Find(s.tokens, v => v.id == Text(b, "tokenId")); Need(t != null, "Жетон не найден");
            switch (type)
            {
                case "token.move":
                    float x = Num(b, "x", 0, s.width - t.size), y = Num(b, "y", 0, s.height - t.size);
                    t.x = s.snap ? (float)Math.Floor(x + 0.5) : x; t.y = s.snap ? (float)Math.Floor(y + 0.5) : y; break;
                case "token.delete": s.tokens = s.tokens.Where(v => v != t).ToArray(); if (s.turnId == t.id) s.turnId = ""; break;
                case "token.duplicate":
                    Need(s.tokens.Length < 100, "Лимит: 100 жетонов");
                    // Copy arrays as well: editing a duplicate must not alter its original.
                    var copy = t.Copy();
                    copy.id = Id(); copy.name += " (копия)"; copy.movedFeet = 0; copy.x = Math.Min(s.width - t.size, t.x + 1);
                    s.tokens = Add(s.tokens, copy); break;
                case "token.hp":
                    int delta = (int)Num(b, "delta", -9999, 9999); ChangeHp(t, delta);
                    Log(state, t.name + ": HP " + delta.ToString("+0;-0;0") + " → " + t.hp + "/" + t.maxHp, "combat"); break;
                case "token.attack": Attack(state, s, t, b); break;
                case "token.macro":
                    var macro = Array.Find(t.macros, v => v.id == Text(b, "macroId")); Need(macro != null, "Действие не найдено");
                    Roll(state, macro.formula, Text(b, "mode", "normal")); break;
                case "token.update": UpdateToken(t, s, b); break;
            }
        }

        private static void UpdateToken(TokenState t, SceneState s, Dictionary<string, object> b)
        {
            t.name = Text(b, "name", t.name);
            t.maxHp = (int)Num(b, "maxHp", 1, 9999, t.maxHp); t.hp = Math.Min(t.maxHp, (int)Num(b, "hp", 0, 9999, t.hp));
            t.ac = (int)Num(b, "ac", 0, 99, t.ac); t.initiative = (int)Num(b, "initiative", -100, 100, t.initiative);
            t.size = (int)Num(b, "size", 1, 4, t.size);
            Need(t.x + t.size <= s.width && t.y + t.size <= s.height, "Жетон за границей карты");
            t.color = Text(b, "color", t.color); Need(Regex.IsMatch(t.color, "^#[0-9a-fA-F]{6}$"), "Нужен цвет #RRGGBB");
            t.ownerId = Text(b, "ownerId", t.ownerId); t.hidden = Flag(b, "hidden", t.hidden); t.locked = Flag(b, "locked", t.locked);
            t.layer = Text(b, "layer", t.layer); Need(new[] { "tokens", "map", "gm" }.Contains(t.layer), "Неизвестный слой");
            t.visionRange = (int)Num(b, "visionRange", 0, 300, t.visionRange); t.nightVision = (int)Num(b, "nightVision", 0, 300, t.nightVision);
            t.lightRadius = (int)Num(b, "lightRadius", 0, 300, t.lightRadius); t.auraFeet = (int)Num(b, "auraFeet", 0, 100, t.auraFeet);
            t.speed = (int)Num(b, "speed", 0, 300, t.speed); t.tempHp = (int)Num(b, "tempHp", 0, 9999, t.tempHp);
            t.attackBonus = (int)Num(b, "attackBonus", -50, 50, t.attackBonus); t.attackRange = (int)Num(b, "attackRange", 0, 600, t.attackRange);
            t.damage = Text(b, "damage", t.damage, 40); ParseDice(t.damage, out _, out _, out _);
            if (b.TryGetValue("conditions", out var conditions)) t.conditions = ((IEnumerable)conditions).Cast<string>().Distinct().ToArray();
            if (b.TryGetValue("macros", out var raw)) {
                var macros = new List<TokenMacro>();
                foreach (Dictionary<string, object> m in (IEnumerable)raw) {
                    string formula = Text(m, "formula", limit: 40); ParseDice(formula, out _, out _, out _);
                    macros.Add(new TokenMacro { id = Id(), name = Text(m, "name", limit: 40), formula = formula });
                }
                Need(macros.Count <= 12, "До 12 действий на жетон"); t.macros = macros.ToArray();
            }
        }

        private static void ChangeHp(TokenState t, int delta)
        {
            if (delta < 0) { int absorbed = Math.Min(t.tempHp, -delta); t.tempHp -= absorbed; delta += absorbed; }
            t.hp = Math.Max(0, Math.Min(t.maxHp, t.hp + delta));
        }
        private static void ParseDice(string formula, out int count, out int sides, out int modifier)
        {
            var match = Regex.Match((formula ?? "").ToLowerInvariant().Replace(" ", ""), "^(\\d{1,2})?d(4|6|8|10|12|20|100)([+-]\\d{1,3})?$");
            Need(match.Success, "Формат: 1d20+5, 2d6, d100");
            count = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 1;
            sides = int.Parse(match.Groups[2].Value); modifier = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
            Need(count >= 1 && count <= 30, "Можно бросить от 1 до 30 кубиков");
        }
        private void Roll(RoomState state, string formula, string mode)
        {
            ParseDice(formula, out int count, out int sides, out int modifier);
            Need(new[] { "normal", "advantage", "disadvantage" }.Contains(mode), "Неизвестный режим броска");
            Need(mode == "normal" || (count == 1 && sides == 20), "Преимущество применяется к d20");
            var dice = Enumerable.Range(0, mode == "normal" ? count : 2).Select(_ => random.Next(1, sides + 1)).ToArray();
            int total = (mode == "normal" ? dice.Sum() : mode == "advantage" ? dice.Max() : dice.Min()) + modifier;
            Log(state, formula + " [" + string.Join(", ", dice) + "] = " + total +
                (mode == "advantage" ? " · преимущество" : mode == "disadvantage" ? " · помеха" : ""), "roll");
        }
        private void Attack(RoomState state, SceneState s, TokenState t, Dictionary<string, object> b)
        {
            var target = Array.Find(s.tokens, v => v.id == Text(b, "targetId"));
            Need(target != null && target != t, "Выберите другой жетон");
            float ax = t.x + t.size / 2f, ay = t.y + t.size / 2f, bx = target.x + target.size / 2f, by = target.y + target.size / 2f;
            float distance = Math.Max(0, Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by)) - (t.size + target.size - 2) / 2f) * s.cellFeet;
            Need(distance <= t.attackRange + 0.000001, "Цель вне дальности атаки");
            foreach (var w in s.barriers) {
                if (w.kind != "wall" && w.open) continue;
                Need(!Intersects(ax, ay, bx, by, w.x, w.y, w.ex, w.ey), "Атака перекрыта преградой");
            }
            string mode = Text(b, "mode", "normal");
            Need(new[] { "normal", "advantage", "disadvantage" }.Contains(mode), "Неизвестный режим броска");
            ParseDice(t.damage, out int count, out int sides, out int modifier);
            int[] dice = Enumerable.Range(0, mode == "normal" ? 1 : 2).Select(_ => random.Next(1, 21)).ToArray();
            int die = mode == "disadvantage" ? dice.Min() : dice.Max();
            bool hit = die == 20 || (die != 1 && die + t.attackBonus >= target.ac);
            string summary = t.name + " → " + target.name + ": d20 [" + string.Join(", ", dice) + "] " + t.attackBonus.ToString("+0;-0;0");
            if (hit) {
                int damage = Math.Max(0, Enumerable.Range(0, count * (die == 20 ? 2 : 1)).Sum(_ => random.Next(1, sides + 1)) + modifier);
                ChangeHp(target, -damage);
                summary += (die == 20 ? " · КРИТ!" : " · попадание") + " · урон " + damage + " · HP " + target.hp + "/" + target.maxHp;
                if (damage > 0 && target.conditions.Contains("Концентрация")) summary += " · проверка концентрации СЛ " + Math.Max(10, damage / 2);
                state.effect = new BattleEffect { id = Id(), sceneId = s.id, tokenId = target.id,
                    kind = die == 20 ? "critical" : "hit", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            } else summary += " · промах";
            Log(state, summary, "combat");
        }

        private static bool Intersects(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
        {
            double o1 = Cross(ax, ay, bx, by, cx, cy), o2 = Cross(ax, ay, bx, by, dx, dy),
                o3 = Cross(cx, cy, dx, dy, ax, ay), o4 = Cross(cx, cy, dx, dy, bx, by);
            return ((o1 > 0 && o2 < 0 || o1 < 0 && o2 > 0) && (o3 > 0 && o4 < 0 || o3 < 0 && o4 > 0)) ||
                (Math.Abs(o1) < 1e-8 && Between(ax, ay, bx, by, cx, cy)) || (Math.Abs(o2) < 1e-8 && Between(ax, ay, bx, by, dx, dy)) ||
                (Math.Abs(o3) < 1e-8 && Between(cx, cy, dx, dy, ax, ay)) || (Math.Abs(o4) < 1e-8 && Between(cx, cy, dx, dy, bx, by));
        }
        private static double Cross(float ax, float ay, float bx, float by, float x, float y) => (bx - ax) * (double)(y - ay) - (by - ay) * (double)(x - ax);
        private static bool Between(float ax, float ay, float bx, float by, float x, float y) =>
            x >= Math.Min(ax, bx) - 1e-8 && x <= Math.Max(ax, bx) + 1e-8 && y >= Math.Min(ay, by) - 1e-8 && y <= Math.Max(ay, by) + 1e-8;
        private static void Paint(SceneState s, Dictionary<string, object> b)
        {
            int x = (int)Num(b, "x", 0, s.width - 1), y = (int)Num(b, "y", 0, s.height - 1), radius = (int)Num(b, "radius", 0, 10, 1);
            var cells = new HashSet<int>(s.revealed.Select(c => c.y * 100 + c.x));
            for (int yy = Math.Max(0, y - radius); yy <= Math.Min(s.height - 1, y + radius); yy++)
                for (int xx = Math.Max(0, x - radius); xx <= Math.Min(s.width - 1, x + radius); xx++)
                    if (Flag(b, "reveal", true)) cells.Add(yy * 100 + xx); else cells.Remove(yy * 100 + xx);
            s.revealed = cells.OrderBy(v => v).Select(v => new Cell { x = v % 100, y = v / 100 }).ToArray();
        }
        private static void Combat(SceneState s, string type)
        {
            if (type == "combat.stop") { s.activeCombat = false; s.turnId = ""; foreach (var t in s.tokens) t.movedFeet = 0; return; }
            var ordered = s.tokens.Where(t => t.kind != "object" && t.layer != "map").OrderByDescending(t => t.initiative).ThenBy(t => t.id, StringComparer.Ordinal).ToArray();
            Need(ordered.Length > 0, "Добавьте участников боя");
            if (type == "combat.start") {
                s.activeCombat = true; s.round = 1; s.turnId = ordered[0].id; foreach (var t in s.tokens) t.movedFeet = 0;
            } else {
                Need(s.activeCombat, "Сначала начните бой");
                int index = Array.FindIndex(ordered, t => t.id == s.turnId) + 1;
                if (index >= ordered.Length) { index = 0; s.round++; }
                s.turnId = ordered[index].id; ordered[index].movedFeet = 0;
            }
        }
        private static void Log(RoomState state, string text, string kind)
        {
            state.messages = state.messages.Skip(Math.Max(0, state.messages.Length - 199)).Concat(new[] {
                new Message { id = Id(), name = "Мастер", text = text, kind = kind, timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
            }).ToArray();
        }
    }
}
