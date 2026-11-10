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
        public string PreviewMemberId { get; private set; }
        public bool IsPreview => !string.IsNullOrEmpty(PreviewMemberId);
        private readonly Queue<Dictionary<string, object>> operations = new Queue<Dictionary<string, object>>();
        private Coroutine poll, sender;
        private string SessionPath => Path.Combine(Application.persistentDataPath, "session.json");
        public string RoomPath => "/api/rooms/" + Session.code;

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

        public void Leave()
        {
            if (poll != null) StopCoroutine(poll);
            if (sender != null) StopCoroutine(sender);
            poll = sender = null;
            operations.Clear();
            State = null;
            PreviewMemberId = null;
            Session = null;
            try { if (File.Exists(SessionPath)) File.Delete(SessionPath); }
            catch (IOException) { Notice?.Invoke("Не удалось удалить сохранённую сессию"); }
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

        public void UploadMap(byte[] bytes, string mime)
        {
            if (State == null || !State.IsGm) return;
            if (bytes.Length > 6 * 1024 * 1024) { Notice?.Invoke("Карта должна быть меньше 6 МБ"); return; }
            string sceneId = State.sceneId;
            StartCoroutine(Upload(bytes, mime, sceneId));
        }

        private IEnumerator Upload(byte[] bytes, string mime, string sceneId)
        {
            using (var request = Build(Session.server + RoomPath + "/assets", "POST",
                Json.Encode(Json.Fields("mime", mime, "data", Convert.ToBase64String(bytes))), Session.secret))
            {
                yield return request.SendWebRequest();
                if (Success(request))
                {
                    var result = JsonUtility.FromJson<AssetReply>(request.downloadHandler.text);
                    Act("scene.update", "sceneId", sceneId, "mapId", result.assetId);
                }
            }
        }

        public IEnumerator DownloadMap(string assetId, Action<Texture2D> done)
        {
            if (Session == null) yield break;
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
            StopAllCoroutines();
            operations.Clear();
        }
    }
}
