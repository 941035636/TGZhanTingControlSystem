using System;
using System.Collections;
using System.Text;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.Networking;

namespace TG.Control.LedPlayer
{
    public sealed class LedApiClient : MonoBehaviour
    {
        [SerializeField] private string serverBaseUrl = "http://127.0.0.1:5080";
        [SerializeField] private string clientId = "led-main";
        [SerializeField] private string terminalApiKey = "TG-TERMINAL-2026";
        public event Action<PlaybackCommand> CommandReceived;
        public event Action<bool> ConnectionChanged;
        public event Action<ContentSyncProgress> ContentSyncChanged;
        public event Action<UiExperienceConfig> UiExperienceChanged;
        private bool running;
        private bool connected;
        private bool syncStarted;
        private bool syncing;
        private bool contentReady;
        private long syncedContentVersion;
        private long uiExperienceVersion = -1;
        private float nextContentCheckAt;

        public string ClientId => clientId;
        public bool IsSyncing => syncing;
        public long SyncedContentVersion => syncedContentVersion;
        private string BaseUrl => serverBaseUrl.TrimEnd('/');

        private void OnEnable() { running = true; StartCoroutine(PollLoop()); StartCoroutine(UiExperienceLoop()); }
        private void OnDisable() => running = false;

        public void Report(PlaybackCommand command, PlaybackState state, double position = 0, string error = null, double progress = 0) =>
            StartCoroutine(Post("/api/playback/status", new PlaybackStatusReport
            {
                clientId = clientId, commandId = command.commandId, sessionId = command.sessionId, nodeId = command.nodeId,
                state = state, positionSeconds = position, error = error, reportedAtUtc = DateTimeOffset.UtcNow.ToString("O"), progress = progress
            }));

        private IEnumerator PollLoop()
        {
            while (running)
            {
                var registered = false;
                yield return Post("/api/clients/register", new ClientRegistration
                {
                    clientId = clientId, kind = ClientKind.LedPlayer, appVersion = Application.version,
                    contentVersion = syncedContentVersion, ready = contentReady,
                    status = contentReady ? "LED内容已就绪" : "LED正在检查内容版本"
                }, () => registered = true);
                SetConnected(registered);
                if (!registered) { syncStarted = false; yield return new WaitForSecondsRealtime(2); continue; }
                if (!syncStarted)
                {
                    syncStarted = true;
                    yield return SyncPublishedContent();
                    nextContentCheckAt = Time.realtimeSinceStartup + 10;
                }

                while (running)
                {
                    using (var request = UnityWebRequest.Get(BaseUrl + "/api/commands/next?clientId=" + UnityWebRequest.EscapeURL(clientId)))
                    {
                        ApplyTerminalHeader(request);
                        request.timeout = 25;
                        yield return request.SendWebRequest();
                        if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
                            CommandReceived?.Invoke(JsonUtility.FromJson<PlaybackCommand>(request.downloadHandler.text));
                        else if (request.responseCode != 204) { SetConnected(false); break; }
                    }
                    if (!syncing && Time.realtimeSinceStartup >= nextContentCheckAt)
                    {
                        nextContentCheckAt = Time.realtimeSinceStartup + 10;
                        yield return SyncPublishedContent();
                    }
                }
            }
        }

        private IEnumerator SyncPublishedContent()
        {
            syncing = true;
            using (var request = UnityWebRequest.Get(BaseUrl + "/api/content/manifest"))
            {
                ApplyTerminalHeader(request);
                request.timeout = 30;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    syncing = false;
                    contentReady = false;
                    ContentSyncChanged?.Invoke(new ContentSyncProgress { error = "获取内容清单失败：" + request.error, finished = true });
                    yield return ReportPresence(false, "获取内容清单失败：" + request.error);
                    yield break;
                }

                var manifest = JsonUtility.FromJson<ContentSyncManifest>(request.downloadHandler.text);
                if (manifest == null)
                {
                    syncing = false;
                    contentReady = false;
                    ContentSyncChanged?.Invoke(new ContentSyncProgress { error = "内容清单格式无效。", finished = true });
                    yield return ReportPresence(false, "内容清单格式无效");
                    yield break;
                }

                var assets = manifest.assets ?? new ContentSyncAsset[0];
                var progress = new ContentSyncProgress { version = manifest.version, total = assets.Length, completed = 0 };
                ContentSyncChanged?.Invoke(progress);
                for (var index = 0; index < assets.Length && running; index++)
                {
                    var asset = assets[index];
                    progress.currentUrl = asset.url;
                    ContentSyncChanged?.Invoke(progress);
                    var completed = false;
                    string error = null;
                    yield return LedContentCache.Shared.Resolve(NormalizeUrl(asset.url), _ => completed = true, value => error = value, asset.sizeBytes);
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        Debug.LogWarning("LED内容同步失败：" + error + " URL=" + asset.url);
                        progress.error = error;
                    }
                    if (completed) progress.completed++;
                    ContentSyncChanged?.Invoke(progress);
                }

                var succeeded = progress.completed == progress.total && string.IsNullOrWhiteSpace(progress.error);
                contentReady = succeeded;
                if (succeeded) syncedContentVersion = manifest.version;
                syncing = false;
                progress.currentUrl = null;
                progress.finished = true;
                ContentSyncChanged?.Invoke(progress);
                yield return ReportPresence(succeeded, succeeded ? "LED内容已就绪" : "部分素材同步失败");
                Debug.Log("LED内容同步完成：版本 V" + manifest.version + "，" + progress.completed + "/" + progress.total + " 个素材已缓存。");
            }
        }

        public string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return url;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
                    (absolute.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || absolute.Host == "127.0.0.1" || absolute.Host == "::1"))
                    return BaseUrl + absolute.PathAndQuery;
                return url;
            }
            return BaseUrl + (url.StartsWith("/") ? url : "/" + url);
        }

        private IEnumerator UiExperienceLoop()
        {
            while (running)
            {
                using (var request = UnityWebRequest.Get(BaseUrl + "/api/ui/current"))
                {
                    ApplyTerminalHeader(request);
                    request.timeout = 15;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var config = JsonUtility.FromJson<UiExperienceConfig>(request.downloadHandler.text);
                        if (config != null && config.version != uiExperienceVersion)
                        {
                            uiExperienceVersion = config.version;
                            UiExperienceChanged?.Invoke(config);
                        }
                    }
                }
                yield return new WaitForSecondsRealtime(10);
            }
        }

        private void SetConnected(bool value)
        {
            if (connected == value) return;
            connected = value;
            ConnectionChanged?.Invoke(value);
        }

        private IEnumerator Post(string path, object body, Action success = null)
        {
            var data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
            using (var request = new UnityWebRequest(BaseUrl + path, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(data);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                ApplyTerminalHeader(request);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) success?.Invoke();
                else Debug.LogError("LED服务请求失败: " + request.error + " " + request.downloadHandler.text);
            }
        }

        private IEnumerator ReportPresence(bool ready, string state)
        {
            yield return Post("/api/clients/register", new ClientRegistration
            {
                clientId = clientId,
                kind = ClientKind.LedPlayer,
                appVersion = Application.version,
                contentVersion = syncedContentVersion,
                ready = ready,
                status = state
            });
        }

        private void ApplyTerminalHeader(UnityWebRequest request)
        {
            if (!string.IsNullOrWhiteSpace(terminalApiKey)) request.SetRequestHeader("X-TG-Terminal-Key", terminalApiKey);
        }
    }
}
