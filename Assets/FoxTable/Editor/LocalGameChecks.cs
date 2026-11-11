using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FoxTable.Editor
{
    // Dependency-free build gate: executed by Prepare in the actual Unity Editor.
    public static class LocalGameChecks
    {
        private static int checks;
        private static void Check(bool value, string label)
        { if (!value) throw new Exception("Local game check failed: " + label); checks++; }
        private static void Reject(Action action, string label)
        {
            try { action(); } catch (ArgumentException) { checks++; return; }
            throw new Exception("Expected rejection: " + label);
        }
        [UnityEditor.MenuItem("FoxTable/Check local game")]
        public static void Run()
        {
            checks = 0;
            var game = new LocalGame(new System.Random(42));
            var state = LocalGame.Create();
            void Act(string type, params object[] pairs) {
                var b = Json.Fields(pairs); b["type"] = type; state = game.ApplyCopy(state, b);
            }
            Check(state.IsGm && state.Active.tokens.Length == 4, "offline table opens with ready map and tokens");
            string hero = state.Active.tokens[0].id, enemy = state.Active.tokens[2].id;
            Act("token.move", "tokenId", hero, "x", 4.7f, "y", 5.2f);
            Check(state.Active.tokens[0].x == 5 && state.Active.tokens[0].y == 5, "snap movement");
            Act("token.update", "tokenId", hero, "tempHp", 5, "initiative", 20);
            Act("token.hp", "tokenId", hero, "delta", -8);
            Check(state.Active.tokens[0].tempHp == 0 && state.Active.tokens[0].hp == 35, "temporary HP absorbs damage");
            Act("token.hp", "tokenId", hero, "delta", 9999);
            Check(state.Active.tokens[0].hp == 38, "healing capped at max HP");
            Act("combat.start"); Check(state.Active.turnId == hero && state.Active.round == 1, "initiative ordering");
            for (int i = 0; i < 4; i++) Act("combat.next");
            Check(state.Active.turnId == hero && state.Active.round == 2, "round wraps");
            Act("combat.stop"); Check(!state.Active.activeCombat && state.Active.turnId == "", "stop combat");
            Act("token.update", "tokenId", hero, "conditions", new[] { "Концентрация" });
            Act("token.duplicate", "tokenId", hero);
            string duplicate = state.Active.tokens.Last().id;
            Act("token.update", "tokenId", duplicate, "conditions", new[] { "Ослеплён" });
            Check(state.Active.tokens[0].conditions[0] == "Концентрация", "duplicate independent state");
            int revision = state.revision;
            Reject(() => game.ApplyCopy(state, Json.Fields("type", "token.update", "tokenId", hero, "name", "BAD", "size", 99)), "invalid token update");
            Check(state.revision == revision && state.Active.tokens[0].name == "Воин", "rejected operation rolls back all fields");
            Reject(() => game.ApplyCopy(state, Json.Fields("type", "scene.update", "width", 5)), "map resize excludes token");
            Reject(() => game.ApplyCopy(state, Json.Fields("type", "scene.delete")), "last scene protection");
            Act("fog.paint", "x", 0, "y", 0, "radius", 1, "reveal", true);
            Check(state.Active.revealed.Length == 4, "fog edge brush bounded");
            Act("fog.paint", "x", 0, "y", 0, "radius", 1, "reveal", false);
            Check(state.Active.revealed.Length == 0, "conceal brush");
            Act("drawing.add", "shape", "cone", "x", 1, "y", 1, "ex", 4, "ey", 4);
            Check(state.Active.drawings.Length == 1, "spell template");
            Act("drawing.clear"); Check(state.Active.drawings.Length == 0, "clear templates");
            Act("roll", "text", "1d20+5", "mode", "advantage");
            Check(state.messages.Last().kind == "roll" && state.messages.Last().text.Contains("преимущество"), "advantage log");
            Reject(() => game.ApplyCopy(state, Json.Fields("type", "roll", "text", "2d6", "mode", "advantage")), "invalid advantage");
            Act("token.update", "tokenId", hero, "macros", new object[] { Json.Fields("name", "Проверка", "formula", "d20+2") });
            Act("token.macro", "tokenId", hero, "macroId", state.Active.tokens[0].macros[0].id);
            Check(state.messages.Last().text.StartsWith("d20+2"), "token macro roll");
            Act("token.move", "tokenId", enemy, "x", 6, "y", 5);
            Act("token.update", "tokenId", hero, "attackBonus", 50, "damage", "1d4+10");
            Act("barrier.add", "kind", "door", "x", 6, "y", 5, "ex", 6, "ey", 6);
            string door = state.Active.barriers[0].id;
            Reject(() => game.ApplyCopy(state, Json.Fields("type", "token.attack", "tokenId", hero, "targetId", enemy)), "closed door blocks attack");
            Act("door.toggle", "barrierId", door);
            Act("token.attack", "tokenId", hero, "targetId", enemy);
            Check(state.messages.Last().text.Contains("→"), "attack resolves and logs");
            Act("barrier.update", "barrierId", door, "open", false, "locked", true);
            Reject(() => game.ApplyCopy(state, Json.Fields("type", "door.toggle", "barrierId", door)), "locked door");
            Act("notes", "text", "Святилище ждёт героев");
            Act("chat", "text", "Запись без интернета");
            string firstScene = state.sceneId;
            Act("scene.add", "name", "Подвал"); Check(state.scenes.Length == 2 && state.Active.name == "Подвал", "new scene");
            Act("scene.switch", "sceneId", firstScene); Check(state.Active.tokens.Length == 5, "scene state retained");

            string directory = Path.Combine(Path.GetTempPath(), "FoxTable-check-" + Guid.NewGuid().ToString("N"));
            try {
                var store = new LocalCampaignStore(directory);
                Check(!store.Exists && store.Load(out _) == null, "first launch without save");
                string map = store.WriteMap(new byte[] { 1, 2, 3, 4 });
                Act("scene.update", "mapId", map);
                store.Write(state);
                var loaded = new LocalCampaignStore(directory).Load(out bool recovered);
                Check(!recovered && loaded.Active.mapId == map && loaded.gmNotes == state.gmNotes &&
                    loaded.messages.Last().text == "Запись без интернета" && loaded.scenes.Length == 2, "save and reopen full campaign");
                Check(store.ReadMap(map).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "map bytes persist");
                Act("notes", "text", "Новая запись"); store.Write(state);
                Check(store.Load(out _).gmNotes == "Новая запись", "atomic update replaces save");
                File.WriteAllText(Path.Combine(directory, "campaign.json"), "broken");
                Check(store.Load(out recovered).gmNotes == "Святилище ждёт героев" && recovered, "backup recovery");
                Check(File.Exists(Path.Combine(directory, "campaign.json.damaged")), "damaged save preserved");
                store.Write(state); Check(store.Load(out _).gmNotes == "Новая запись", "save after recovery");
                Reject(() => store.ReadMap("../session.json"), "map path traversal");
                var restored = store.Load(out _); string doomed = restored.scenes.Last().id;
                restored = game.ApplyCopy(restored, Json.Fields("type", "scene.delete", "sceneId", doomed));
                Check(restored.scenes.Length == 1 && restored.Active != null, "delete non-last scene");
            } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            Debug.Log("FoxTable local checks passed: " + checks);
        }
    }
}
