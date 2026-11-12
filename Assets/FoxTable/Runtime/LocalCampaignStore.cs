using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FoxTable
{
    // Separate from session.json: entering a local table never replaces an online login.
    public sealed class LocalCampaignStore
    {
        [Serializable] private sealed class Save { public int version = 1; public RoomState state; }
        private readonly string directory;
        private string SavePath => Path.Combine(directory, "campaign.json");
        public LocalCampaignStore(string directory) { this.directory = directory; }
        public bool Exists => File.Exists(SavePath) || File.Exists(SavePath + ".bak");
        public RoomState Load(out bool recovered)
        {
            recovered = false;
            if (!Exists) return null;
            try { return Read(SavePath); }
            catch (Exception original) when (original is IOException || original is ArgumentException || original is InvalidDataException) {
                try {
                    var state = Read(SavePath + ".bak"); recovered = true;
                    // Preserve damaged data, and repair the primary before the next backup rotation.
                    if (File.Exists(SavePath)) File.Copy(SavePath, SavePath + ".damaged", true);
                    File.Copy(SavePath + ".bak", SavePath, true);
                    return state;
                } catch (Exception backup) when (backup is IOException || backup is ArgumentException || backup is InvalidDataException) {
                    throw new IOException("Не удалось открыть локальное сохранение. Файлы сохранены для восстановления.", original);
                }
            }
        }
        private static RoomState Read(string path)
        {
            var save = JsonUtility.FromJson<Save>(File.ReadAllText(path));
            if (save == null || save.version != 1 || save.state == null || save.state.formatVersion != 2 ||
                !save.state.IsGm || save.state.Active == null || save.state.members == null || save.state.messages == null)
                throw new InvalidDataException("Неподдерживаемое локальное сохранение");
            foreach (var scene in save.state.scenes)
                if (scene == null || scene.width < 5 || scene.height < 5 || scene.width>MapBuilder.MaxCells || scene.height>MapBuilder.MaxCells || scene.tokens == null || scene.barriers == null ||
                    scene.drawings == null || scene.revealed == null || scene.currentCells == null || scene.exploredCells == null)
                    throw new InvalidDataException("Неполная сцена в сохранении");
            return save.state;
        }
        public void Write(RoomState state)
        {
            Directory.CreateDirectory(directory);
            string temp = SavePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(new Save { state = state }));
            // Replace is atomic; the previous complete save remains available as .bak.
            if (File.Exists(SavePath)) File.Replace(temp, SavePath, SavePath + ".bak");
            else File.Move(temp, SavePath);
        }
        public string WriteMap(byte[] bytes)
        {
            Directory.CreateDirectory(directory);
            string id = Guid.NewGuid().ToString("N").Substring(0, 16);
            File.WriteAllBytes(MapPath(id), bytes); return id;
        }
        public byte[] ReadMap(string id) => File.ReadAllBytes(MapPath(id));
        private string MapPath(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[0-9a-f]{16}$")) throw new ArgumentException("Неверный ID карты");
            return Path.Combine(directory, id + ".image");
        }
    }
}
