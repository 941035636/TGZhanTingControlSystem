using System;
using System.Collections;
using System.IO;
using TG.Control.UnityContracts;
using RenderHeads.Media.AVProVideo;
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
        [SerializeField] private LedImageDisplayController galleryController;

        private bool connected;
        private bool playbackActive;
        private bool galleryActive;
        private string status = "正在连接展厅控制服务";
        private Font font;
        private GameObject idleRoot;
        private Image solidBackground;
        private Image idleImage;
        private readonly DisplayUGUI[] idleVideos = new DisplayUGUI[2];
        private readonly MediaPlayer[] idleMediaPlayers = new MediaPlayer[2];
        private readonly bool[] idleVideoPrepared = new bool[2];
        private readonly bool[] idleVideoReady = new bool[2];
        private Text title;
        private Text subtitle;
        private Text brand;
        private Image brandLogo;
        private Text statusText;
        private Text connectionText;
        private Image connectionPill;
        private int mediaGeneration;
        private int activeIdleVideoIndex;
        private bool idleVideoTransitioning;
        private double idleVideoTransitionStartedAt;

        private const double SeamlessLoopBlendSeconds = 0.35;

        private void Awake()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" }, 32);
            BuildUi();
            CreateIdleVideoPlayer();
            TryApplyPersistedConfig();
        }

        private void Start()
        {
            apiClient.ConnectionChanged += OnConnectionChanged;
            apiClient.CommandReceived += OnCommand;
            apiClient.ContentSyncChanged += OnContentSyncChanged;
            apiClient.UiExperienceChanged += ApplyConfig;
            if (playbackController != null) playbackController.PlaybackActiveChanged += OnPlaybackActiveChanged;
            if (galleryController != null) galleryController.GalleryActiveChanged += OnGalleryActiveChanged;
            // UiExperienceLoop can complete before this Start method subscribes on a
            // fresh installation. Replay the retained snapshot so the first launch
            // applies the server-configured idle media without requiring a republish.
            if (apiClient.LatestUiExperience != null) ApplyConfig(apiClient.LatestUiExperience);
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
            if (galleryController != null) galleryController.GalleryActiveChanged -= OnGalleryActiveChanged;
            foreach (var player in idleMediaPlayers)
                if (player != null) player.Events.RemoveListener(OnIdleVideoEvent);
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

            solidBackground = Image("Idle Screen", canvas.transform, Hex("#061427"));
            Stretch(solidBackground.rectTransform);
            idleRoot = solidBackground.gameObject;
            idleImage = Image("Idle Image", idleRoot.transform, Color.white);
            Stretch(idleImage.rectTransform);
            idleImage.gameObject.SetActive(false);
            for (var i = 0; i < idleVideos.Length; i++)
            {
                var display = new GameObject("Idle Video " + (i == 0 ? "A" : "B"), typeof(RectTransform), typeof(DisplayUGUI))
                    .GetComponent<DisplayUGUI>();
                display.transform.SetParent(idleRoot.transform, false);
                Stretch(display.rectTransform);
                display.color = new Color(1f, 1f, 1f, 0f);
                display.NoDefaultDisplay = true;
                display.DisplayInEditor = false;
                display.gameObject.SetActive(false);
                idleVideos[i] = display;
            }
            brand = Label("Brand", idleRoot.transform, "TG", 70, FontStyle.Bold, Hex("#43B8FF"), TextAnchor.MiddleCenter);
            Anchor(brand.rectTransform, .35f, .60f, .65f, .72f, 0, 0, 0, 0);
            brandLogo = Image("Brand Logo", idleRoot.transform, Color.white);
            Anchor(brandLogo.rectTransform, .35f, .60f, .65f, .72f, 0, 0, 0, 0);
            brandLogo.preserveAspect = true;
            brandLogo.gameObject.SetActive(false);
            title = Label("Title", idleRoot.transform, "展厅自动讲解系统", 58, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, .12f, .46f, .88f, .60f, 0, 0, 0, 0);
            subtitle = Label("Subtitle", idleRoot.transform, "等待触控终端启动讲解", 28, FontStyle.Normal, Hex("#B7D7F2"), TextAnchor.MiddleCenter);
            Anchor(subtitle.rectTransform, .18f, .38f, .82f, .47f, 0, 0, 0, 0);
            statusText = Label("Status", idleRoot.transform, status, 21, FontStyle.Normal, Hex("#83A9CF"), TextAnchor.MiddleCenter);
            Anchor(statusText.rectTransform, .18f, .28f, .82f, .37f, 0, 0, 0, 0);

            connectionPill = Image("Connection", canvas.transform, Hex("#804C25"));
            Anchor(connectionPill.rectTransform, 0, 1, 0, 1, 48, -94, 408, -42);
            connectionText = Label("Connection Label", connectionPill.transform, "● LED 播放端连接中", 19, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);
            Stretch(connectionText.rectTransform, 10, 2, -10, -2);
            // The production LED idle surface is media-only. Keep legacy objects
            // allocated for schema compatibility, but never show them by default.
            brand.gameObject.SetActive(false);
            title.gameObject.SetActive(false);
            subtitle.gameObject.SetActive(false);
            statusText.gameObject.SetActive(false);
            connectionPill.gameObject.SetActive(false);
        }

        private void CreateIdleVideoPlayer()
        {
            for (var i = 0; i < idleMediaPlayers.Length; i++)
            {
                var player = gameObject.AddComponent<MediaPlayer>();
                // Idle media is opened explicitly after the persisted/site
                // configuration is known; never let AVPro auto-open its empty
                // default MediaReference during startup.
                player.AutoOpen = false;
                player.AutoStart = false;
                // A second pre-opened player takes over just before the end. This avoids
                // the decoder seek/rebuffer pause that can be visible with native looping.
                player.Loop = false;
                player.Events.AddListener(OnIdleVideoEvent);
                idleMediaPlayers[i] = player;
                idleVideos[i].Player = player;
                idleVideos[i].ScaleMode = ScaleMode.ScaleToFit;
            }
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
            if (value && idleVideoTransitioning) CancelIdleVideoTransition();
            RefreshPresentationVisibility();
        }

        private void OnGalleryActiveChanged(bool value)
        {
            galleryActive = value;
            if (value && idleVideoTransitioning) CancelIdleVideoTransition();
            RefreshPresentationVisibility();
        }

        private void RefreshPresentationVisibility()
        {
            var showIdle = !playbackActive && !galleryActive;
            idleRoot.SetActive(showIdle);
            foreach (var player in idleMediaPlayers)
                if (!showIdle) player?.Control?.Pause();
            if (showIdle && IsIdleVideoVisible()) idleMediaPlayers[activeIdleVideoIndex]?.Control?.Play();
        }

        private void OnContentSyncChanged(ContentSyncProgress progress)
        {
            if (playbackActive || galleryActive) return;
            if (progress.finished)
                status = string.IsNullOrWhiteSpace(progress.error) ? "内容已同步，等待启动讲解" : "部分内容尚未缓存，播放时将继续下载";
            else
                status = progress.total > 0 ? $"正在同步展厅素材 {progress.completed}/{progress.total}" : "正在获取展厅素材清单";
            RefreshStatus();
        }

        private void ApplyConfig(UiExperienceConfig config)
        {
            if (config == null) return;
            var titleOverride = FindOverride(config.ledElements, "idle.title");
            var subtitleOverride = FindOverride(config.ledElements, "idle.subtitle");
            if (title != null && !string.IsNullOrWhiteSpace(titleOverride?.text)) title.text = titleOverride.text;
            else if (title != null && !string.IsNullOrWhiteSpace(config.ledTitle)) title.text = config.ledTitle;
            if (subtitle != null && !string.IsNullOrWhiteSpace(subtitleOverride?.text)) subtitle.text = subtitleOverride.text;
            else if (subtitle != null && !string.IsNullOrWhiteSpace(config.ledSubtitle)) subtitle.text = config.ledSubtitle;
            if (ColorUtility.TryParseHtmlString(config.ledBackgroundColor, out var color)) solidBackground.color = color;
            // Production idle mode is media-only. Keep the legacy configuration fields
            // for compatibility, but do not overlay text or status badges over customer media.
            var showBranding = false;
            var showStatus = false;
            if (brand != null) brand.gameObject.SetActive(showBranding);
            if (title != null) title.gameObject.SetActive(showBranding);
            if (subtitle != null) subtitle.gameObject.SetActive(showBranding);
            if (statusText != null) statusText.gameObject.SetActive(showBranding);
            if (connectionPill != null) connectionPill.gameObject.SetActive(showStatus);
            var logo = FindOverride(config.ledElements, "idle.logo");
            if (brand != null) brand.gameObject.SetActive(showBranding && string.IsNullOrWhiteSpace(logo?.assetUrl));
            if (brandLogo != null) brandLogo.gameObject.SetActive(false);
            if (showBranding && !string.IsNullOrWhiteSpace(logo?.assetUrl)) StartCoroutine(LoadBrandLogo(apiClient.NormalizeUrl(logo.assetUrl)));
            LoadIdleMedia(config.ledIdleMediaKind, config.ledIdleMediaUrl);
            PersistConfig(config);
        }

        private string PersistedConfigPath => Path.Combine(Application.persistentDataPath, "led-ui-experience.json");

        private void TryApplyPersistedConfig()
        {
            try
            {
                if (!File.Exists(PersistedConfigPath)) return;
                var config = JsonUtility.FromJson<UiExperienceConfig>(File.ReadAllText(PersistedConfigPath));
                if (config == null) return;
                Debug.Log($"LED已加载本地待机界面配置：版本 V{config.version}");
                ApplyConfig(config);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("LED本地待机界面配置读取失败，将等待服务端配置：" + exception.Message);
            }
        }

        private void PersistConfig(UiExperienceConfig config)
        {
            try
            {
                File.WriteAllText(PersistedConfigPath, JsonUtility.ToJson(config));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("LED本地待机界面配置保存失败：" + exception.Message);
            }
        }

        private IEnumerator LoadBrandLogo(string url)
        {
            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) yield break;
                var texture = DownloadHandlerTexture.GetContent(request);
                if (texture == null) yield break;
                brandLogo.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
                brandLogo.gameObject.SetActive(true);
            }
        }

        private static UiElementOverride FindOverride(UiElementOverride[] items, string key)
        {
            if (items == null) return null;
            for (var i = 0; i < items.Length; i++)
                if (items[i] != null && string.Equals(items[i].key, key, StringComparison.OrdinalIgnoreCase)) return items[i];
            return null;
        }

        private void LoadIdleMedia(string kind, string url)
        {
            mediaGeneration++;
            idleVideoTransitioning = false;
            activeIdleVideoIndex = 0;
            for (var i = 0; i < idleMediaPlayers.Length; i++)
            {
                idleVideoPrepared[i] = false;
                idleVideoReady[i] = false;
                idleMediaPlayers[i]?.CloseMedia();
                SetIdleVideoAlpha(i, 0f);
                if (idleVideos[i] != null) idleVideos[i].gameObject.SetActive(false);
            }
            idleImage.gameObject.SetActive(false);
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
            for (var i = 0; i < idleMediaPlayers.Length; i++)
            {
                idleVideos[i].gameObject.SetActive(true);
                idleMediaPlayers[i].OpenMedia(MediaPathType.AbsolutePathOrURL, localUrl, false);
            }
        }

        private void OnIdleVideoEvent(MediaPlayer source, MediaPlayerEvent.EventType eventType, ErrorCode errorCode)
        {
            var index = Array.IndexOf(idleMediaPlayers, source);
            if (index < 0) return;
            if (eventType == MediaPlayerEvent.EventType.Error)
            {
                Debug.LogError("LED待机视频加载失败：" + errorCode);
                return;
            }

            if (eventType == MediaPlayerEvent.EventType.ReadyToPlay)
            {
                idleVideoPrepared[index] = true;
                if (index == activeIdleVideoIndex && !playbackActive && !galleryActive)
                    source.Control?.Play();
                else
                {
                    source.Control?.Pause();
                    source.Control?.Seek(0d);
                }
            }
            else if (eventType == MediaPlayerEvent.EventType.FirstFrameReady)
            {
                idleVideoReady[index] = true;
                if (index == activeIdleVideoIndex && !playbackActive && !galleryActive) SetIdleVideoAlpha(index, 1f);
            }
            else if (eventType == MediaPlayerEvent.EventType.FinishedPlaying && index == activeIdleVideoIndex &&
                     !playbackActive && !galleryActive)
            {
                CompleteIdleVideoLoop();
            }
        }

        private void Update()
        {
            if (playbackActive || galleryActive || !IsIdleVideoVisible()) return;
            var current = idleMediaPlayers[activeIdleVideoIndex];
            if (current?.Control == null || current.Info == null || !current.Control.HasMetaData()) return;
            var duration = current.Info.GetDuration();
            if (duration <= SeamlessLoopBlendSeconds) return;
            var currentTime = current.Control.GetCurrentTime();
            var standbyIndex = 1 - activeIdleVideoIndex;

            if (!idleVideoTransitioning && idleVideoPrepared[standbyIndex] &&
                currentTime >= duration - SeamlessLoopBlendSeconds)
            {
                var standby = idleMediaPlayers[standbyIndex];
                standby.Control?.Seek(0d);
                standby.AudioVolume = 0f;
                standby.Control?.Play();
                idleVideoTransitionStartedAt = currentTime;
                idleVideoTransitioning = true;
            }

            if (!idleVideoTransitioning) return;
            if (!idleVideoReady[standbyIndex]) return;
            var progress = Mathf.Clamp01((float)((currentTime - idleVideoTransitionStartedAt) / SeamlessLoopBlendSeconds));
            SetIdleVideoAlpha(activeIdleVideoIndex, 1f - progress);
            SetIdleVideoAlpha(standbyIndex, progress);
            current.AudioVolume = 1f - progress;
            idleMediaPlayers[standbyIndex].AudioVolume = progress;
            if (progress >= 1f || current.Control.IsFinished()) CompleteIdleVideoLoop();
        }

        private void CompleteIdleVideoLoop()
        {
            var previousIndex = activeIdleVideoIndex;
            var nextIndex = 1 - previousIndex;
            if (!idleVideoReady[nextIndex])
            {
                SetIdleVideoAlpha(previousIndex, 1f);
                SetIdleVideoAlpha(nextIndex, 0f);
                idleMediaPlayers[previousIndex].AudioVolume = 1f;
                idleMediaPlayers[nextIndex]?.Control?.Pause();
                idleMediaPlayers[nextIndex]?.Control?.Seek(0d);
                idleMediaPlayers[previousIndex]?.Control?.Seek(0d);
                idleMediaPlayers[previousIndex]?.Control?.Play();
                idleVideoTransitioning = false;
                return;
            }

            SetIdleVideoAlpha(previousIndex, 0f);
            SetIdleVideoAlpha(nextIndex, 1f);
            idleMediaPlayers[previousIndex].AudioVolume = 0f;
            idleMediaPlayers[previousIndex].Control?.Pause();
            idleMediaPlayers[previousIndex].Control?.Seek(0d);
            idleMediaPlayers[nextIndex].AudioVolume = 1f;
            if (!idleMediaPlayers[nextIndex].Control.IsPlaying()) idleMediaPlayers[nextIndex].Control.Play();
            activeIdleVideoIndex = nextIndex;
            idleVideoTransitioning = false;
        }

        private void CancelIdleVideoTransition()
        {
            var standbyIndex = 1 - activeIdleVideoIndex;
            SetIdleVideoAlpha(activeIdleVideoIndex, 1f);
            SetIdleVideoAlpha(standbyIndex, 0f);
            idleMediaPlayers[activeIdleVideoIndex].AudioVolume = 1f;
            idleMediaPlayers[standbyIndex].AudioVolume = 0f;
            idleMediaPlayers[standbyIndex].Control?.Pause();
            idleMediaPlayers[standbyIndex].Control?.Seek(0d);
            idleVideoTransitioning = false;
        }

        private bool IsIdleVideoVisible() => idleVideos[activeIdleVideoIndex] != null &&
                                                 idleVideos[activeIdleVideoIndex].gameObject.activeSelf;

        private void SetIdleVideoAlpha(int index, float alpha)
        {
            if (idleVideos[index] == null) return;
            idleVideos[index].color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        private void RefreshStatus()
        {
            if (statusText != null) statusText.text = status;
            if (connectionText != null) connectionText.text = connected ? "● LED 播放端在线" : "● LED 播放端连接中";
            if (connectionPill != null) connectionPill.color = connected ? Hex("#145B4C") : Hex("#244669");
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
