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
        public event Action<PlaybackCommand> CommandReceived;
        public event Action<bool> ConnectionChanged;
        private bool running;
        private bool connected;

        public string ClientId => clientId;
        private string BaseUrl => serverBaseUrl.TrimEnd('/');

        private void OnEnable() { running = true; StartCoroutine(PollLoop()); }
        private void OnDisable() => running = false;

        public void Report(PlaybackCommand command, PlaybackState state, double position = 0, string error = null) =>
            StartCoroutine(Post("/api/playback/status", new PlaybackStatusReport
            {
                clientId = clientId, commandId = command.commandId, sessionId = command.sessionId, nodeId = command.nodeId,
                state = state, positionSeconds = position, error = error, reportedAtUtc = DateTimeOffset.UtcNow.ToString("O")
            }));

        private IEnumerator PollLoop()
        {
            while (running)
            {
                var registered = false;
                yield return Post("/api/clients/register", new ClientRegistration
                {
                    clientId = clientId, kind = ClientKind.LedPlayer, appVersion = Application.version
                }, () => registered = true);
                SetConnected(registered);
                if (!registered) { yield return new WaitForSecondsRealtime(2); continue; }

                while (running)
                {
                    using (var request = UnityWebRequest.Get(BaseUrl + "/api/commands/next?clientId=" + UnityWebRequest.EscapeURL(clientId)))
                    {
                        request.timeout = 25;
                        yield return request.SendWebRequest();
                        if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
                            CommandReceived?.Invoke(JsonUtility.FromJson<PlaybackCommand>(request.downloadHandler.text));
                        else if (request.responseCode != 204) { SetConnected(false); break; }
                    }
                }
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
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) success?.Invoke();
                else Debug.LogError("LED服务请求失败: " + request.error + " " + request.downloadHandler.text);
            }
        }
    }
}
