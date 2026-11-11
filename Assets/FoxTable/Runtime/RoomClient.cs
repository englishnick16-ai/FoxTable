using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace FoxTable
{
    public sealed class RoomClient : MonoBehaviour
    {
        public event Action<RoomState> Changed;
        public event Action<string> Notice;
        public event Action<string> ConnectionChanged;
        public Session Session { get; private set; }
        public RoomState State { get; private set; }
        public bool IsLocal { get; private set; }
        private LocalCampaignStore localStore;
        private readonly LocalGame localGame = new LocalGame();
        private readonly List<RoomState> undo=new List<RoomState>();
        public bool CanUndo=>IsLocal && (undo.Count>0 || operations.Count>0);
        public bool HasOnlineSession=>File.Exists(SessionPath);
        private bool localDirty;
        private Coroutine localSave;
        private LocalCampaignStore LocalStore => localStore ?? (localStore = new LocalCampaignStore(Path.Combine(Application.persistentDataPath, "local")));

        public void OpenLocal()
        {
            RoomState saved;
            bool recovered;
            try { saved = LocalStore.Load(out recovered) ?? LocalGame.Create(); }
            catch (Exception e) { Notice?.Invoke(e.Message); return; }
            StopAllCoroutines(); poll = sender = localSave = null; operations.Clear();
            IsLocal = true; PreviewMemberId = null;
            undo.Clear();
            Session = new Session { code = "LOCAL", memberId = saved.selfId, role = "gm" };
            State = saved;
            localDirty = true; SaveLocal();
            Changed?.Invoke(State);
            ConnectionChanged?.Invoke("Локально · без сервера");
            if (recovered) Notice?.Invoke("Кампания восстановлена из резервной копии");
        }

        private void SaveLocal()
        {
            if (!IsLocal || !localDirty || State == null) return;
            try { LocalStore.Write(State); localDirty = false; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                Notice?.Invoke("Не удалось сохранить локальную игру. Освободите место на телефоне.");
            }
        }

        private IEnumerator SaveLocalSoon()
        {
            yield return new WaitForSecondsRealtime(0.4f);
            SaveLocal(); localSave = null;
        }
        private void OnApplicationPause(bool paused) { if (paused) { DrainLocalQueue(); SaveLocal(); } }
        private void OnApplicationQuit() { DrainLocalQueue(); SaveLocal(); }
        private void DrainLocalQueue()
        {
            if (!IsLocal) return;
            while (operations.Count > 0) ApplyLocal(operations.Dequeue());
        }
        private void ApplyLocal(Dictionary<string, object> operation)
        {
            RoomState next;
            var previous=State;
            try {
                next = localGame.ApplyCopy(State, operation);
            } catch (Exception e) when (e is ArgumentException || e is InvalidCastException || e is OverflowException) {
                Notice?.Invoke(e.Message); return;
            }
            State = next; localDirty = true;
            undo.Add(previous);if(undo.Count>20)undo.RemoveAt(0);
            if (localSave == null) localSave = StartCoroutine(SaveLocalSoon());
            Changed?.Invoke(State);
        }
        public string PreviewMemberId { get; private set; }
        public bool IsPreview => !string.IsNullOrEmpty(PreviewMemberId);
        private readonly Queue<Dictionary<string, object>> operations = new Queue<Dictionary<string, object>>();
        private Coroutine poll, sender;
        private string SessionPath => Path.Combine(Application.persistentDataPath, "session.json");
        public string RoomPath => "/api/rooms/" + Session.code;

        public void UndoLocal()
        {
            if(!IsLocal)return;
            DrainLocalQueue();
            if(undo.Count==0){Notice?.Invoke("Нет действий для отмены");return;}
            int revision=State.revision+1;
            State=undo[undo.Count-1];undo.RemoveAt(undo.Count-1);State.revision=revision;
            localDirty=true;SaveLocal();Changed?.Invoke(State);
        }

        public void Create(string server, string name, string title)
        {
            StartCoroutine(Connect(server, "/api/rooms", Json.Fields("name", name, "title", title)));
        }

        public void Join(string server, string code, string name, string invite)
        {
            StartCoroutine(Connect(server, "/api/rooms/" + code.Trim().ToUpperInvariant() + "/join",
                Json.Fields("name", name, "invite", invite.Trim())));
        }

        public void Restore(string server, string code, string secret)
        {
            StartCoroutine(Connect(server, "/api/rooms/" + code.Trim().ToUpperInvariant() + "/resume",
                Json.Fields("secret", secret.Trim())));
        }

        private IEnumerator Connect(string server, string path, Dictionary<string, object> body)
        {
            if (!Uri.TryCreate(server.Trim(), UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http"))
            { Notice?.Invoke("Укажите адрес сервера: https://example.com"); yield break; }
            if (IsLocal) { Notice?.Invoke("Сначала выйдите из локальной игры"); yield break; }
            string origin = server.Trim().TrimEnd('/');
            ConnectionChanged?.Invoke("Подключение…");
            using (var request = Build(origin + path, "POST", Json.Encode(body), null))
            {
                yield return request.SendWebRequest();
                if (!Success(request)) yield break;
                Session = JsonUtility.FromJson<Session>(request.downloadHandler.text);
                Session.server = origin;
                SaveSession();
                StartPolling();
            }
        }

        public void Resume()
        {
            try
            {
                if (!File.Exists(SessionPath)) return;
                Session = JsonUtility.FromJson<Session>(File.ReadAllText(SessionPath));
                if (Session == null || string.IsNullOrEmpty(Session.secret) || string.IsNullOrEmpty(Session.server)) return;
                StartPolling();
            }
            catch (Exception) { Notice?.Invoke("Не удалось прочитать сохранённую сессию"); }
        }

        public bool Leave(bool forgetOnline=true)
        {
            DrainLocalQueue();
            SaveLocal();
            if (IsLocal && localDirty) return false;
            bool wasLocal = IsLocal;
            StopAllCoroutines();
            localSave = null; IsLocal = false;
            if (poll != null) StopCoroutine(poll);
            if (sender != null) StopCoroutine(sender);
            poll = sender = null;
            operations.Clear();undo.Clear();
            State = null;
            PreviewMemberId = null;
            Session = null;
            try { if (!wasLocal && forgetOnline && File.Exists(SessionPath)) File.Delete(SessionPath); }
            catch (IOException) { Notice?.Invoke("Не удалось удалить сохранённую сессию"); }
            return true;
        }

        private void SaveSession()
        {
            try { File.WriteAllText(SessionPath, JsonUtility.ToJson(Session)); }
            catch (IOException) { Notice?.Invoke("Сессия открыта, но её не удалось сохранить на устройстве"); }
        }

        private void StartPolling()
        {
            if (poll != null) StopCoroutine(poll);
            State = null;
            poll = StartCoroutine(Poll());
        }

        private IEnumerator Poll()
        {
            while (Session != null)
            {
                using (var request = Build(Session.server + RoomPath + (IsPreview ? "/preview" : ""),
                    IsPreview ? "POST" : "GET", IsPreview ? Json.Encode(Json.Fields("memberId", PreviewMemberId)) : null, Session.secret))
                {
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Accept(request.downloadHandler.text);
                        ConnectionChanged?.Invoke(IsPreview ? "Вид игрока · без изменений" : "В сети");
                    }
                    else ConnectionChanged?.Invoke("Нет связи · переподключение");
                }
                yield return new WaitForSecondsRealtime(1.25f);
            }
        }

        public void Act(string type, params object[] pairs)
        {
            if (IsPreview) { Notice?.Invoke("Выйдите из просмотра глазами игрока, чтобы менять игру"); return; }
            if (Session == null || State == null) return;
            if (operations.Count >= 100) { Notice?.Invoke("Дождитесь соединения с сервером"); return; }
            var body = Json.Fields(pairs);
            body["type"] = type;
            body["requestId"] = Guid.NewGuid().ToString("N");
            if (!body.ContainsKey("sceneId")) body["sceneId"] = State.sceneId;
            operations.Enqueue(body);
            if (sender == null) sender = StartCoroutine(SendQueued());
        }

        public void Preview(string memberId)
        {
            if (IsLocal) { Notice?.Invoke("В локальной игре вы управляете столом как мастер"); return; }
            if (Session == null || Session.role != "gm") return;
            if (operations.Count > 0 || sender != null) { Notice?.Invoke("Дождитесь отправки текущих действий"); return; }
            PreviewMemberId = memberId;
            StartPolling();
        }

        private IEnumerator SendQueued()
        {
            // First yield prevents a synchronously completed coroutine from leaving a stale handle.
            yield return null;
            while (Session != null && operations.Count > 0)
            {
                if (IsLocal)
                {
                    ApplyLocal(operations.Dequeue());
                    yield return null;
                    continue;
                }
                using (var request = Build(Session.server + RoomPath + "/actions", "POST",
                    Json.Encode(operations.Peek()), Session.secret))
                {
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.ConnectionError || request.responseCode >= 500)
                    {
                        ConnectionChanged?.Invoke("Нет связи · повтор действия");
                        yield return new WaitForSecondsRealtime(2);
                        continue; // Same requestId makes retries idempotent.
                    }
                    operations.Dequeue();
                    if (Success(request)) Accept(request.downloadHandler.text);
                }
            }
            sender = null;
        }

        private void Accept(string json)
        {
            RoomState next;
            try { next = JsonUtility.FromJson<RoomState>(json); }
            catch (Exception) { Notice?.Invoke("Сервер прислал некорректные данные"); return; }
            if (next == null || next.Active == null) return;
            if (State != null && next.revision < State.revision) return;
            State = next;
            Changed?.Invoke(State);
        }

        public void UploadMap(string path)
        {
            if (State == null || !State.IsGm) return;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception) { Notice?.Invoke("Не удалось открыть файл карты"); return; }
            UploadMap(bytes, path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
        }

        public void UploadMap(byte[] bytes, string mime,Dictionary<string,object> settings=null)
        {
            if (State == null || !State.IsGm) return;
            if (!MapImage.Fits(bytes)) { Notice?.Invoke("Карта: PNG / JPEG до 6 МБ и 16 мегапикселей"); return; }
            var fields=settings==null?new Dictionary<string,object>():new Dictionary<string,object>(settings);
            string sceneId=fields.TryGetValue("sceneId",out var idValue)?(string)idValue:State.sceneId;
            fields["sceneId"]=sceneId;
            if (IsLocal) {
                var check = new Texture2D(2, 2);
                try {
                    if (!check.LoadImage(bytes) || check.width > 8192 || check.height > 8192 || (long)check.width*check.height>16*1024*1024) {
                        Notice?.Invoke("Нужна карта PNG / JPEG размером до 8192 × 8192"); return;
                    }
                    string id = LocalStore.WriteMap(bytes);
                    fields["mapId"]=id;Act("scene.update",Json.Pairs(fields));
                } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                    Notice?.Invoke("Не удалось сохранить карту на телефоне");
                } finally { Destroy(check); }
                return;
            }
            StartCoroutine(Upload(bytes, mime,fields));
        }

        private IEnumerator Upload(byte[] bytes, string mime,Dictionary<string,object> fields)
        {
            using (var request = Build(Session.server + RoomPath + "/assets", "POST",
                Json.Encode(Json.Fields("mime", mime, "data", Convert.ToBase64String(bytes))), Session.secret))
            {
                yield return request.SendWebRequest();
                if (Success(request))
                {
                    var result = JsonUtility.FromJson<AssetReply>(request.downloadHandler.text);
                    fields["mapId"]=result.assetId;Act("scene.update",Json.Pairs(fields));
                }
            }
        }

        public IEnumerator DownloadMap(string assetId, Action<Texture2D> done)
        {
            if (Session == null) yield break;
            if (IsLocal) {
                Texture2D texture = null;
                try {
                    byte[] bytes = LocalStore.ReadMap(assetId);
                    texture = new Texture2D(2, 2);
                    if (!texture.LoadImage(bytes)) { Destroy(texture); texture = null; }
                } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
                    if (texture != null) Destroy(texture); texture = null;
                    Notice?.Invoke("Не удалось прочитать сохранённую карту");
                }
                done(texture); yield break;
            }
            using (var request = UnityWebRequestTexture.GetTexture(Session.server + RoomPath + "/assets/" + assetId))
            {
                request.SetRequestHeader("Authorization", "Bearer " + Session.secret);
                request.timeout = 25;
                yield return request.SendWebRequest();
                if (Success(request)) done(DownloadHandlerTexture.GetContent(request));
                else done(null);
            }
        }

        private static UnityWebRequest Build(string url, string method, string body, string secret)
        {
            var request = new UnityWebRequest(url, method) { timeout = 20, downloadHandler = new DownloadHandlerBuffer() };
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            if (!string.IsNullOrEmpty(secret)) request.SetRequestHeader("Authorization", "Bearer " + secret);
            return request;
        }

        private bool Success(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.Success) return true;
            string message = "Сервер недоступен";
            try
            {
                var error = JsonUtility.FromJson<ApiError>(request.downloadHandler.text);
                if (!string.IsNullOrEmpty(error.error)) message = error.error;
            }
            catch (Exception) { }
            Notice?.Invoke(message);
            return false;
        }

        private void OnDestroy()
        {
            DrainLocalQueue();
            SaveLocal();
            StopAllCoroutines();
            operations.Clear();
        }
    }
}
