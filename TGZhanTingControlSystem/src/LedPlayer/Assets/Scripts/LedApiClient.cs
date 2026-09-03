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
        [SerializeField] private string terminalApiKey = "TG-DEVELOPMENT-ONLY";
        [SerializeField] private float contentCheckIntervalSeconds = 10f;
        [SerializeField] private float failedSyncRetrySeconds = 2f;
        [SerializeField] private int assetDownloadAttempts = 3;
        [SerializeField] private float assetRetryDelaySeconds = 2f;
        public event Action<PlaybackCommand> CommandReceived;
        public event Action<bool> ConnectionChanged;
        public event Action<ContentSyncProgress> ContentSyncChanged;
        public event Action<UiExperienceConfig> UiExperienceChanged;
        private bool running;
        private readonly string instanceId = Guid.NewGuid().ToString("N");
        private bool connected;
        private bool syncing;
        private bool contentReady;
        private long syncedContentVersion;
        private long uiExperienceVersion = -1;
        private Coroutine contentSyncCoroutine;
        private bool playbackPriorityActive;
        private string contentStatus = "LED正在检查内容版本";

        public string ClientId => clientId;
        public bool IsConnected => connected;
        public bool IsSyncing => syncing;
        public long SyncedContentVersion => syncedContentVersion;
        private string BaseUrl => serverBaseUrl.TrimEnd('/');

        private void OnEnable()
        {
            running = true;
            StartCoroutine(PollLoop());
            StartCoroutine(ContentSyncLoop());
            StartCoroutine(UiExperienceLoop());
        }

        private void OnDisable()
        {
            running = false;
            playbackPriorityActive = false;
            if (contentSyncCoroutine != null) StopCoroutine(contentSyncCoroutine);
            contentSyncCoroutine = null;
            syncing = false;
        }

        public void BeginPlaybackPriority()
        {
            playbackPriorityActive = true;
            if (contentSyncCoroutine != null)
            {
                StopCoroutine(contentSyncCoroutine);
                contentSyncCoroutine = null;
                syncing = false;
                contentStatus = "内容 V" + syncedContentVersion + " 已识别，后台同步已暂停并优先准备当前讲解素材";
                ContentSyncChanged?.Invoke(new ContentSyncProgress
                {
                    version = syncedContentVersion,
                    error = "后台同步已暂停，正在优先准备当前讲解素材。",
                    finished = true
                });
            }
        }

        public void EndPlaybackPriority()
        {
            playbackPriorityActive = false;
        }

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
                    status = contentReady ? "LED内容已就绪" : contentStatus,
                    instanceId = instanceId
                }, () => registered = true);
                SetConnected(registered);
                if (!registered) { yield return new WaitForSecondsRealtime(2); continue; }

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
                }
            }
        }

        private IEnumerator ContentSyncLoop()
        {
            while (running)
            {
                if (connected && !syncing && !playbackPriorityActive) StartContentSync();
                var delay = contentReady ? contentCheckIntervalSeconds : failedSyncRetrySeconds;
                yield return new WaitForSecondsRealtime(Mathf.Max(1f, delay));
            }
        }

        private void StartContentSync()
        {
            if (!running || syncing || playbackPriorityActive) return;
            syncing = true;
            contentSyncCoroutine = StartCoroutine(SyncPublishedContent());
        }

        private IEnumerator SyncPublishedContent()
        {
            using (var request = UnityWebRequest.Get(BaseUrl + "/api/content/manifest"))
            {
                ApplyTerminalHeader(request);
                request.timeout = 30;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    contentStatus = contentReady
                        ? "已保留内容 V" + syncedContentVersion + "，获取最新清单失败：" + request.error
                        : "获取内容清单失败：" + request.error;
                    ContentSyncChanged?.Invoke(new ContentSyncProgress { error = contentStatus, finished = true });
                    yield return ReportPresence(contentReady, contentStatus);
                    syncing = false;
                    contentSyncCoroutine = null;
                    yield break;
                }

                var manifest = JsonUtility.FromJson<ContentSyncManifest>(request.downloadHandler.text);
                if (manifest == null)
                {
                    contentReady = false;
                    contentStatus = "内容清单格式无效";
                    ContentSyncChanged?.Invoke(new ContentSyncProgress { error = contentStatus + "。", finished = true });
                    yield return ReportPresence(false, contentStatus);
                    syncing = false;
                    contentSyncCoroutine = null;
                    yield break;
                }

                var assets = manifest.assets ?? new ContentSyncAsset[0];
                for (var index = 0; index < assets.Length; index++)
                {
                    var asset = assets[index];
                    LedContentCache.Shared.RegisterValidation(NormalizeUrl(asset.url), asset.sizeBytes, asset.sha256);
                }

                // A published content version is immutable. Once every asset for this version has passed
                // size and SHA validation, periodic manifest checks must not make the terminal temporarily unready.
                if (contentReady && syncedContentVersion == manifest.version)
                {
                    var allFilesPresent = true;
                    for (var index = 0; index < assets.Length; index++)
                    {
                        var asset = assets[index];
                        if (LedContentCache.Shared.HasExpectedFile(NormalizeUrl(asset.url), asset.sizeBytes)) continue;
                        allFilesPresent = false;
                        break;
                    }
                    if (allFilesPresent)
                    {
                        syncing = false;
                        contentSyncCoroutine = null;
                        yield break;
                    }
                }

                var progress = new ContentSyncProgress { version = manifest.version, total = assets.Length, completed = 0 };
                contentReady = assets.Length == 0;
                if (contentReady) syncedContentVersion = manifest.version;
                contentStatus = contentReady
                    ? "LED内容已就绪"
                    : "正在同步内容 V" + manifest.version + " 的 " + assets.Length + " 个素材";
                ContentSyncChanged?.Invoke(progress);
                yield return ReportPresence(contentReady, contentStatus);
                for (var index = 0; index < assets.Length && running; index++)
                {
                    var asset = assets[index];
                    progress.currentUrl = asset.url;
                    ContentSyncChanged?.Invoke(progress);
                    var completed = false;
                    string error = null;
                    var attempts = Math.Max(1, assetDownloadAttempts);
                    for (var attempt = 1; attempt <= attempts && running && !playbackPriorityActive; attempt++)
                    {
                        completed = false;
                        error = null;
                        yield return LedContentCache.Shared.Resolve(NormalizeUrl(asset.url), _ => completed = true,
                            value => error = value, asset.sizeBytes, expectedSha256: asset.sha256);
                        if (completed) break;
                        Debug.LogWarning("LED素材同步第 " + attempt + "/" + attempts + " 次失败：" + error + " URL=" + asset.url);
                        if (attempt < attempts)
                            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, assetRetryDelaySeconds));
                    }
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        Debug.LogWarning("LED内容同步失败：" + error + " URL=" + asset.url);
                        progress.error = error;
                    }
                    if (completed) progress.completed++;
                    ContentSyncChanged?.Invoke(progress);
                }

                var succeeded = running && !playbackPriorityActive &&
                                progress.completed == progress.total && string.IsNullOrWhiteSpace(progress.error);
                if (succeeded) syncedContentVersion = manifest.version;
                contentReady = succeeded;
                syncing = false;
                contentSyncCoroutine = null;
                progress.currentUrl = null;
                progress.finished = true;
                ContentSyncChanged?.Invoke(progress);
                var failedCount = Math.Max(0, progress.total - progress.completed);
                contentStatus = succeeded
                    ? "LED内容已就绪"
                    : playbackPriorityActive
                        ? "内容 V" + manifest.version + " 同步已暂停，讲解结束后自动继续"
                        : "内容 V" + manifest.version + " 有 " + failedCount + " 个素材同步失败，将自动重试";
                yield return ReportPresence(succeeded, contentStatus);
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
                status = state,
                instanceId = instanceId
            });
        }

        private void ApplyTerminalHeader(UnityWebRequest request)
        {
            if (!string.IsNullOrWhiteSpace(terminalApiKey)) request.SetRequestHeader("X-TG-Terminal-Key", terminalApiKey);
        }
    }
}
