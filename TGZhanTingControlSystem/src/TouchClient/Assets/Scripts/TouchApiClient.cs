using System;
using System.Collections;
using System.Text;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.Networking;

namespace TG.Control.Touch
{
    public sealed class TouchApiClient : MonoBehaviour
    {
        [SerializeField] private string serverBaseUrl = "http://127.0.0.1:5080";
        [SerializeField] private string clientId = "touch-main";
        [SerializeField] private float retryDelaySeconds = 2f;
        [SerializeField] private string terminalApiKey = "TG-TERMINAL-2026";

        public event Action<PlaybackCommand> CommandReceived;
        public event Action<bool> ConnectionChanged;

        private bool running;
        private readonly string instanceId = Guid.NewGuid().ToString("N");
        private bool connected;
        private long reportedContentVersion;

        public string ServerBaseUrl => serverBaseUrl.TrimEnd('/');
        public string ClientId => clientId;
        public bool IsConnected => connected;

        public void SetContentVersion(long version)
        {
            reportedContentVersion = version;
            if (connected) StartCoroutine(PostJson<ClientRegistration, EmptyResponse>("/api/clients/register", Registration(), null, null));
        }

        private void OnEnable()
        {
            running = true;
            StartCoroutine(ConnectionLoop());
        }

        private void OnDisable() => running = false;

        public void GetContent(Action<PublishedContent> success, Action<string> failure) =>
            StartCoroutine(GetJson("/api/content/current", success, failure));

        public void GetRoutes(Action<NarrationRouteCollection> success, Action<string> failure) =>
            StartCoroutine(GetJson("/api/routes", success, failure));

        public void GetUiExperience(Action<UiExperienceConfig> success, Action<string> failure) =>
            StartCoroutine(GetJson("/api/ui/current", success, failure));

        public void GetReadiness(Action<SystemReadiness> success, Action<string> failure) =>
            StartCoroutine(GetJson("/api/readiness", success, failure));

        public void GetActiveNarrationSession(Action<PlaybackSessionLookup> success, Action<string> failure) =>
            StartCoroutine(GetJson("/api/playback/active", success, failure));

        public string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
                    (absolute.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || absolute.Host == "127.0.0.1" || absolute.Host == "::1"))
                    return ServerBaseUrl + absolute.PathAndQuery;
                return url;
            }
            return ServerBaseUrl + (url.StartsWith("/") ? url : "/" + url);
        }

        public void SaveRoute(SaveNarrationRouteRequest route, Action<NarrationRoute> success, Action<string> failure) =>
            StartCoroutine(PostJson("/api/routes", route, success, failure));

        public void DeleteRoute(string routeId, Action success, Action<string> failure) =>
            StartCoroutine(Delete("/api/routes/" + UnityWebRequest.EscapeURL(routeId), success, failure));

        public void StartNarration(string[] moduleIds, string requestedBy, Action<StartNarrationResponse> success, Action<string> failure)
        {
            var request = new StartNarrationRequest { moduleIds = moduleIds, requestedBy = requestedBy };
            StartCoroutine(PostJson("/api/playback/start", request, success, failure));
        }

        public void ControlNarration(string sessionId, PlaybackAction action, Action<ControlNarrationResponse> success, Action<string> failure)
        {
            var request = new ControlNarrationRequest { sessionId = sessionId, action = action };
            StartCoroutine(PostJson("/api/playback/control", request, success, failure));
        }

        public void GetNarrationSession(string sessionId, Action<PlaybackSessionLookup> success, Action<string> failure) =>
            StartCoroutine(GetJson("/api/playback/sessions/" + UnityWebRequest.EscapeURL(sessionId), success, failure));

        public void Report(PlaybackCommand command, PlaybackState state, double positionSeconds = 0, string error = null)
        {
            var report = new PlaybackStatusReport
            {
                clientId = clientId,
                commandId = command.commandId,
                sessionId = command.sessionId,
                nodeId = command.nodeId,
                state = state,
                positionSeconds = positionSeconds,
                error = error,
                reportedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                progress = 0
            };
            StartCoroutine(PostJson<object, EmptyResponse>("/api/playback/status", report, null, null));
        }

        private IEnumerator ConnectionLoop()
        {
            while (running)
            {
                var registration = Registration();
                var registered = false;
                yield return PostJson<ClientRegistration, EmptyResponse>("/api/clients/register", registration, _ => registered = true, _ => { });
                SetConnected(registered);
                if (!registered)
                {
                    yield return new WaitForSecondsRealtime(retryDelaySeconds);
                    continue;
                }

                while (running && connected)
                {
                    using (var request = UnityWebRequest.Get(ServerBaseUrl + "/api/commands/next?clientId=" + UnityWebRequest.EscapeURL(clientId)))
                    {
                        ApplyTerminalHeader(request);
                        request.timeout = 25;
                        yield return request.SendWebRequest();
                        if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
                        {
                            CommandReceived?.Invoke(JsonUtility.FromJson<PlaybackCommand>(request.downloadHandler.text));
                        }
                        else if (request.responseCode != 204)
                        {
                            SetConnected(false);
                        }
                    }
                }
            }
        }

        private IEnumerator GetJson<T>(string path, Action<T> success, Action<string> failure)
        {
            using (var request = UnityWebRequest.Get(ServerBaseUrl + path))
            {
                ApplyTerminalHeader(request);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) success?.Invoke(JsonUtility.FromJson<T>(request.downloadHandler.text));
                else failure?.Invoke(request.error);
            }
        }

        private IEnumerator PostJson<TRequest, TResponse>(string path, TRequest body, Action<TResponse> success, Action<string> failure)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
            using (var request = new UnityWebRequest(ServerBaseUrl + path, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                ApplyTerminalHeader(request);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    success?.Invoke(string.IsNullOrWhiteSpace(request.downloadHandler.text)
                        ? default
                        : JsonUtility.FromJson<TResponse>(request.downloadHandler.text));
                }
                else failure?.Invoke(request.downloadHandler.text + " " + request.error);
            }
        }

        private IEnumerator PostJson<TResponse>(string path, object body, Action<TResponse> success, Action<string> failure) =>
            PostJson<object, TResponse>(path, body, success, failure);

        private IEnumerator Delete(string path, Action success, Action<string> failure)
        {
            using (var request = UnityWebRequest.Delete(ServerBaseUrl + path))
            {
                ApplyTerminalHeader(request);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) success?.Invoke();
                else failure?.Invoke(request.downloadHandler?.text + " " + request.error);
            }
        }

        private void SetConnected(bool value)
        {
            if (connected == value) return;
            connected = value;
            ConnectionChanged?.Invoke(value);
        }

        private void ApplyTerminalHeader(UnityWebRequest request)
        {
            if (!string.IsNullOrWhiteSpace(terminalApiKey)) request.SetRequestHeader("X-TG-Terminal-Key", terminalApiKey);
        }

        private ClientRegistration Registration() => new ClientRegistration
        {
            clientId = clientId,
            kind = ClientKind.Touch,
            appVersion = Application.version,
            contentVersion = reportedContentVersion,
            ready = true,
            status = reportedContentVersion > 0 ? "触控内容已加载" : "触控端正在加载内容",
            instanceId = instanceId
        };

        [Serializable] private sealed class EmptyResponse { }
    }
}
