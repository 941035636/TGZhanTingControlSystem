using System;
using System.Collections;
using TG.Control.UnityContracts;
using UMP;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TG.Control.LedPlayer
{
    /// <summary>UGUI idle layer. It covers the video surface while idle and returns after playback.</summary>
    public sealed class LedStatusOverlay : MonoBehaviour
    {
        [SerializeField] private LedApiClient apiClient;
        [SerializeField] private LedPlaybackController playbackController;

        private bool connected;
        private bool playbackActive;
        private string status = "正在连接展厅控制服务";
        private Font font;
        private GameObject idleRoot;
        private Image solidBackground;
        private Image idleImage;
        private RawImage idleVideo;
        private Text title;
        private Text subtitle;
        private Text brand;
        private Text statusText;
        private Text connectionText;
        private Image connectionPill;
        private UniversalMediaPlayer idleMediaPlayer;
        private int mediaGeneration;

        private void Awake()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" }, 32);
            BuildUi();
            CreateIdleVideoPlayer();
        }

        private void Start()
        {
            apiClient.ConnectionChanged += OnConnectionChanged;
            apiClient.CommandReceived += OnCommand;
            apiClient.ContentSyncChanged += OnContentSyncChanged;
            apiClient.UiExperienceChanged += ApplyConfig;
            if (playbackController != null) playbackController.PlaybackActiveChanged += OnPlaybackActiveChanged;
            OnConnectionChanged(apiClient.IsConnected);
        }

        private void OnDestroy()
        {
            if (apiClient != null)
            {
                apiClient.ConnectionChanged -= OnConnectionChanged;
                apiClient.CommandReceived -= OnCommand;
                apiClient.ContentSyncChanged -= OnContentSyncChanged;
                apiClient.UiExperienceChanged -= ApplyConfig;
            }
            if (playbackController != null) playbackController.PlaybackActiveChanged -= OnPlaybackActiveChanged;
            if (idleMediaPlayer != null) idleMediaPlayer.RemovePreparedEvent(OnIdleVideoPrepared);
        }

        private void BuildUi()
        {
            var canvasObject = new GameObject("LED UGUI Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            solidBackground = Image("Idle Screen", canvas.transform, Hex("#0A1F1B"));
            Stretch(solidBackground.rectTransform);
            idleRoot = solidBackground.gameObject;
            idleImage = Image("Idle Image", idleRoot.transform, Color.white);
            Stretch(idleImage.rectTransform);
            idleImage.gameObject.SetActive(false);
            idleVideo = new GameObject("Idle Video", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            idleVideo.transform.SetParent(idleRoot.transform, false);
            Stretch(idleVideo.rectTransform);
            idleVideo.color = Color.white;
            idleVideo.gameObject.SetActive(false);
            var veil = Image("Readability Veil", idleRoot.transform, new Color(.02f, .10f, .08f, .52f));
            Stretch(veil.rectTransform);

            brand = Label("Brand", idleRoot.transform, "TG", 70, FontStyle.Bold, Hex("#D2B46F"), TextAnchor.MiddleCenter);
            Anchor(brand.rectTransform, .35f, .60f, .65f, .72f, 0, 0, 0, 0);
            title = Label("Title", idleRoot.transform, "展厅自动讲解系统", 58, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, .12f, .46f, .88f, .60f, 0, 0, 0, 0);
            subtitle = Label("Subtitle", idleRoot.transform, "等待触控终端启动讲解", 28, FontStyle.Normal, Hex("#C3D2CC"), TextAnchor.MiddleCenter);
            Anchor(subtitle.rectTransform, .18f, .38f, .82f, .47f, 0, 0, 0, 0);
            statusText = Label("Status", idleRoot.transform, status, 21, FontStyle.Normal, Hex("#AABFB7"), TextAnchor.MiddleCenter);
            Anchor(statusText.rectTransform, .18f, .28f, .82f, .37f, 0, 0, 0, 0);

            connectionPill = Image("Connection", canvas.transform, Hex("#804C25"));
            Anchor(connectionPill.rectTransform, 0, 1, 0, 1, 48, -94, 408, -42);
            connectionText = Label("Connection Label", connectionPill.transform, "● LED 播放端连接中", 19, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);
            Stretch(connectionText.rectTransform, 10, 2, -10, -2);
        }

        private void CreateIdleVideoPlayer()
        {
            idleMediaPlayer = gameObject.AddComponent<UniversalMediaPlayer>();
            idleMediaPlayer.AutoPlay = false;
            idleMediaPlayer.Loop = true;
            idleMediaPlayer.RenderingObjects = new[] { idleVideo.gameObject };
            idleMediaPlayer.AddPreparedEvent(OnIdleVideoPrepared);
        }

        private void OnConnectionChanged(bool value)
        {
            connected = value;
            status = value ? "系统已就绪，等待触控终端启动讲解" : "服务连接中断，正在自动重连";
            RefreshStatus();
        }

        private void OnCommand(PlaybackCommand command)
        {
            status = command.action == PlaybackAction.Prepare ? "正在准备展厅展示素材" : "正在执行同步播放指令";
            RefreshStatus();
        }

        private void OnPlaybackActiveChanged(bool value)
        {
            playbackActive = value;
            idleRoot.SetActive(!value);
            if (idleMediaPlayer == null) return;
            if (value) idleMediaPlayer.Pause();
            else if (idleVideo.gameObject.activeSelf) idleMediaPlayer.Play();
        }

        private void OnContentSyncChanged(ContentSyncProgress progress)
        {
            if (playbackActive) return;
            if (progress.finished)
                status = string.IsNullOrWhiteSpace(progress.error) ? "内容已同步，等待启动讲解" : "部分内容尚未缓存，播放时将继续下载";
            else
                status = progress.total > 0 ? $"正在同步展厅素材 {progress.completed}/{progress.total}" : "正在获取展厅素材清单";
            RefreshStatus();
        }

        private void ApplyConfig(UiExperienceConfig config)
        {
            if (config == null) return;
            if (!string.IsNullOrWhiteSpace(config.ledTitle)) title.text = config.ledTitle;
            if (!string.IsNullOrWhiteSpace(config.ledSubtitle)) subtitle.text = config.ledSubtitle;
            if (ColorUtility.TryParseHtmlString(config.ledBackgroundColor, out var color)) solidBackground.color = color;
            brand.gameObject.SetActive(config.ledShowBranding);
            title.gameObject.SetActive(config.ledShowBranding);
            subtitle.gameObject.SetActive(config.ledShowBranding);
            statusText.gameObject.SetActive(config.ledShowBranding);
            connectionPill.gameObject.SetActive(config.ledShowStatus);
            LoadIdleMedia(config.ledIdleMediaKind, config.ledIdleMediaUrl);
        }

        private void LoadIdleMedia(string kind, string url)
        {
            mediaGeneration++;
            if (idleMediaPlayer != null) idleMediaPlayer.Stop(false);
            idleImage.gameObject.SetActive(false);
            idleVideo.gameObject.SetActive(false);
            if (string.IsNullOrWhiteSpace(url) || string.Equals(kind, "none", StringComparison.OrdinalIgnoreCase)) return;
            var normalized = apiClient.NormalizeUrl(url);
            if (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase))
                StartCoroutine(LoadImage(normalized, mediaGeneration));
            else if (string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase))
                StartCoroutine(LoadVideo(normalized, mediaGeneration));
        }

        private IEnumerator LoadImage(string url, int generation)
        {
            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();
                if (generation != mediaGeneration || request.result != UnityWebRequest.Result.Success) yield break;
                var texture = DownloadHandlerTexture.GetContent(request);
                if (texture == null) yield break;
                idleImage.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
                idleImage.gameObject.SetActive(true);
            }
        }

        private IEnumerator LoadVideo(string url, int generation)
        {
            string localUrl = null;
            string error = null;
            yield return LedContentCache.Shared.Resolve(url, value => localUrl = value, value => error = value);
            if (generation != mediaGeneration || !string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(localUrl)) yield break;
            idleVideo.gameObject.SetActive(true);
            idleMediaPlayer.Path = localUrl;
            idleMediaPlayer.Prepare();
        }

        private void OnIdleVideoPrepared(int width, int height)
        {
            if (!playbackActive && idleVideo.gameObject.activeSelf) idleMediaPlayer.Play();
        }

        private void RefreshStatus()
        {
            if (statusText != null) statusText.text = status;
            if (connectionText != null) connectionText.text = connected ? "● LED 播放端在线" : "● LED 播放端连接中";
            if (connectionPill != null) connectionPill.color = connected ? Hex("#1C654D") : Hex("#804C25");
        }

        private Image Image(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text Label(string name, Transform parent, string value, int size, FontStyle style, Color color, TextAnchor alignment)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false);
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.text = value;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        private static void Stretch(RectTransform rect, float left = 0, float bottom = 0, float right = 0, float top = 0) =>
            Anchor(rect, 0, 0, 1, 1, left, bottom, right, top);
        private static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY, float left, float bottom, float right, float top)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }
        private static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var color); return color; }
    }
}
