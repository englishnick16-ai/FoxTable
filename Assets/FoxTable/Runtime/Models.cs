using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FoxTable
{
    [Serializable] public sealed class Session
    {
        public string server, code, secret, memberId, role;
    }
    [Serializable] public sealed class ApiError { public string error; }
    [Serializable] public sealed class AssetReply { public string assetId; }
    [Serializable] public sealed class RoomState
    {
        public int formatVersion, revision;
        public string title, sceneId, gmNotes, invite, selfId, role;
        public bool locked;
        public SceneState[] scenes = Array.Empty<SceneState>();
        public Member[] members = Array.Empty<Member>();
        public Message[] messages = Array.Empty<Message>();
        public Ping ping;
        public BattleEffect effect;
        public SceneState Active => Array.Find(scenes, s => s.id == sceneId);
        public bool IsGm => role == "gm";
    }
    [Serializable] public sealed class Member { public string id, name, role; public bool online; }
    [Serializable] public sealed class Message { public string id, name, text, kind; public long timestamp; }
    [Serializable] public sealed class Ping { public float x, y; public string sceneId, name; public long timestamp; }
    [Serializable] public sealed class BattleEffect { public string id, sceneId, tokenId, kind; public long timestamp; }
    [Serializable] public sealed class Barrier
    {
        public string id, kind;
        public float x, y, ex, ey;
        public bool open, locked, secret;
    }
    [Serializable] public sealed class Cell { public int x, y; }
    [Serializable] public sealed class SceneState
    {
        public string id, name, mapId, turnId;
        public int width, height, cellFeet, round;
        public bool grid, snap, fog, activeCombat;
        public bool lighting, daylight, explorer, sharedVision, restrictMovement, enforceSpeed;
        public Barrier[] barriers = Array.Empty<Barrier>();
        public Cell[] currentCells = Array.Empty<Cell>(), exploredCells = Array.Empty<Cell>();
        public Cell[] revealed = Array.Empty<Cell>();
        public TokenState[] tokens = Array.Empty<TokenState>();
        public Drawing[] drawings = Array.Empty<Drawing>();
    }
    [Serializable] public sealed class TokenState
    {
        public string id, name, kind, ownerId, color;
        public float x, y;
        public int size, hp, maxHp, ac, initiative;
        public bool hidden;
        public string layer, damage;
        public bool locked;
        public int visionRange, nightVision, lightRadius, auraFeet, speed, tempHp, attackBonus, attackRange;
        public float movedFeet;
        public TokenMacro[] macros = Array.Empty<TokenMacro>();
        public string[] conditions = Array.Empty<string>();
    }
    [Serializable] public sealed class Drawing { public string id, shape; public float x, y, ex, ey; }
    [Serializable] public sealed class TokenMacro { public string id, name, formula; }

    // Only specified operation fields are sent. JsonUtility cannot express optional fields.
    public static class Json
    {
        public static string Encode(object value)
        {
            var output = new StringBuilder();
            Write(output, value);
            return output.ToString();
        }

        private static void Write(StringBuilder output, object value)
        {
            if (value == null) { output.Append("null"); return; }
            if (value is string str)
            {
                output.Append('"');
                foreach (char c in str)
                {
                    switch (c)
                    {
                        case '"': output.Append("\\\""); break;
                        case '\\': output.Append("\\\\"); break;
                        case '\n': output.Append("\\n"); break;
                        case '\r': output.Append("\\r"); break;
                        case '\t': output.Append("\\t"); break;
                        default:
                            if (c < 32) output.Append("\\u" + ((int)c).ToString("x4"));
                            else output.Append(c);
                            break;
                    }
                }
                output.Append('"');
                return;
            }
            if (value is bool b) { output.Append(b ? "true" : "false"); return; }
            if (value is IDictionary dictionary)
            {
                output.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!first) output.Append(',');
                    first = false;
                    Write(output, entry.Key.ToString());
                    output.Append(':');
                    Write(output, entry.Value);
                }
                output.Append('}');
                return;
            }
            if (value is IEnumerable list)
            {
                output.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) output.Append(',');
                    first = false;
                    Write(output, item);
                }
                output.Append(']');
                return;
            }
            if (value is IFormattable formattable)
            {
                output.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            }
            throw new ArgumentException("Unsupported JSON value");
        }

        public static Dictionary<string, object> Fields(params object[] pairs)
        {
            var result = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) result[(string)pairs[i]] = pairs[i + 1];
            return result;
        }
    }
}
