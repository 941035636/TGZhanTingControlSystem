using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TG.Control.Touch.UI.Components;
using TG.Control.Touch.UI.Services;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Pages
{
    /// <summary>
    /// Exclusive full-screen welcome presentation. Audio remains authoritative on the LED playback host;
    /// this page only renders the Server-coordinated state and captions.
    /// </summary>
    public sealed class WelcomeExperiencePage
    {
        private static Sprite defaultBackgroundSprite;
        private static Sprite circleSprite;

        private readonly MonoBehaviour host;
        private readonly TouchImageLoader imageLoader;
        private readonly GameObject root;
        private readonly Image background;
        private readonly Image backgroundVeil;
        private readonly Image brandLogo;
        private readonly Text eyebrow;
        private readonly Text title;
        private readonly Text subtitle;
        private readonly Text caption;
        private readonly Text status;
        private Text clock;
        private readonly Text wakeLabel;
        private readonly Text wakeHint;
        private readonly Button wakeButton;
        private readonly Button homeFallbackButton;
        private readonly List<RectTransform> pulseRings = new List<RectTransform>();
        private Coroutine animation;
        private string backgroundUrl;
        private string logoUrl;
        private bool requesting;
        private int renderedSecond = -1;

        public bool Visible => root.activeSelf;
        public bool IsRequestPending => requesting;
        public event Action WelcomeRequested;
        public event Action Entered;

        public WelcomeExperiencePage(MonoBehaviour host, TouchUiFactory factory, TouchTheme theme,
            TouchImageLoader imageLoader, Transform canvas)
        {
            this.host = host;
            this.imageLoader = imageLoader;

            background = factory.Image("Welcome Technology Background", canvas, Color.white);
            TouchUiFactory.Stretch(background.rectTransform);
            background.sprite = LoadDefaultBackground();
            background.type = Image.Type.Simple;
            background.preserveAspect = false;
            background.raycastTarget = false;
            root = background.gameObject;

            backgroundVeil = factory.Image("Welcome Contrast Veil", root.transform,
                new Color(.01f, .055f, .19f, .22f));
            TouchUiFactory.Stretch(backgroundVeil.rectTransform);
            backgroundVeil.raycastTarget = false;

            BuildTopBrand(factory);

            brandLogo = factory.Image("Welcome Brand Logo", root.transform, Color.white);
            TouchUiFactory.Anchor(brandLogo.rectTransform, .5f, 1, .5f, 1, -88, -152, 88, -88);
            brandLogo.preserveAspect = true;
            brandLogo.raycastTarget = false;
            brandLogo.gameObject.SetActive(false);

            eyebrow = Label(factory, root.transform, "展厅自动讲解系统", 23, FontStyle.Bold,
                new Color(.60f, .86f, 1f, 1), 196, 232);
            title = Label(factory, root.transform, "欢迎莅临智慧展厅", 68, FontStyle.Bold, Color.white, 90, 186);
            AddShadow(title, new Color(0, .08f, .24f, .78f), new Vector2(0, -3));
            subtitle = Label(factory, root.transform, "探索 · 体验 · 发现 · 共创未来", 27, FontStyle.Normal,
                new Color(.80f, .91f, 1f, 1), 36, 82);

            BuildPulseRings(factory, theme);
            var wakeImage = factory.Image("Welcome Touch Action", root.transform, new Color(.04f, .38f, .92f, .96f));
            wakeImage.sprite = GetCircleSprite();
            TouchUiFactory.Anchor(wakeImage.rectTransform, .5f, .5f, .5f, .5f, -96, -190, 96, 2);
            wakeButton = wakeImage.gameObject.AddComponent<Button>();
            wakeButton.targetGraphic = wakeImage;
            wakeButton.onClick.AddListener(RequestWelcome);
            var colors = wakeButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.88f, .96f, 1f, 1);
            colors.pressedColor = new Color(.64f, .84f, 1f, 1);
            colors.disabledColor = new Color(.62f, .73f, .84f, .72f);
            colors.fadeDuration = .08f;
            wakeButton.colors = colors;

            wakeLabel = factory.Label("Welcome Touch Label", wakeImage.transform, "触碰开启", 28,
                FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(wakeLabel.rectTransform, 0, .5f, 1, 1, 18, -4, -18, -6);
            wakeHint = factory.Label("Welcome Touch Hint", wakeImage.transform, "开启参观之旅", 16,
                FontStyle.Normal, new Color(.78f, .90f, 1f, 1), TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(wakeHint.rectTransform, 0, 0, 1, .5f, 12, 23, -12, 7);

            caption = Label(factory, root.transform, string.Empty, 31, FontStyle.Normal, Color.white, -304, -246);
            caption.resizeTextMinSize = 20;
            AddShadow(caption, new Color(0, .06f, .18f, .9f), new Vector2(0, -2));
            status = Label(factory, root.transform, "触碰中央按钮，开启智慧参观之旅", 20,
                FontStyle.Normal, new Color(.72f, .86f, 1f, 1), -356, -316);

            homeFallbackButton = factory.TouchButton(root.transform, "进入讲解首页", false, Complete);
            TouchUiFactory.Anchor(homeFallbackButton.GetComponent<RectTransform>(), .5f, .5f, .5f, .5f,
                -170, -426, 170, -362);
            homeFallbackButton.gameObject.SetActive(false);
        }

        public void Configure(UiExperienceConfig config, Func<string, string> resolver)
        {
            if (config == null) return;
            eyebrow.text = TextOf(config, "welcome.eyebrow", config.touchTitle ?? "展厅自动讲解系统");
            title.text = TextOf(config, "welcome.title", "欢迎莅临智慧展厅");
            subtitle.text = TextOf(config, "welcome.subtitle", "探索 · 体验 · 发现 · 共创未来");

            var element = config.touchElements?.LastOrDefault(item => item != null && item.key == "welcome.background");
            var url = element == null ? null : Resolve(element.assetUrl, resolver);
            if (string.IsNullOrWhiteSpace(url)) url = Resolve(config.touchBackgroundUrl, resolver);
            if (!string.Equals(backgroundUrl, url, StringComparison.OrdinalIgnoreCase))
            {
                backgroundUrl = url;
                background.sprite = LoadDefaultBackground();
                background.color = Color.white;
                if (!string.IsNullOrWhiteSpace(url)) imageLoader.Load(background, url, success =>
                {
                    if (!success && string.Equals(backgroundUrl, url, StringComparison.OrdinalIgnoreCase))
                        background.sprite = LoadDefaultBackground();
                });
            }

            var logoElement = config.touchElements?.LastOrDefault(item => item != null && item.key == "welcome.logo");
            var logo = logoElement == null ? null : Resolve(logoElement.assetUrl, resolver);
            if (!string.Equals(logoUrl, logo, StringComparison.OrdinalIgnoreCase))
            {
                logoUrl = logo;
                brandLogo.sprite = null;
                brandLogo.gameObject.SetActive(false);
                if (!string.IsNullOrWhiteSpace(logo)) imageLoader.Load(brandLogo, logo, success =>
                {
                    if (string.Equals(logoUrl, logo, StringComparison.OrdinalIgnoreCase))
                        brandLogo.gameObject.SetActive(success);
                });
            }
        }

        public void Show()
        {
            root.transform.SetAsLastSibling();
            root.SetActive(true);
            requesting = false;
            wakeButton.gameObject.SetActive(true);
            wakeButton.interactable = true;
            homeFallbackButton.gameObject.SetActive(false);
            caption.text = string.Empty;
            status.text = "触碰中央按钮，开启智慧参观之旅";
            SetPulseVisible(true);
            StartAnimation();
        }

        public void Hide()
        {
            StopAnimation();
            requesting = false;
            root.SetActive(false);
        }

        public void SetWelcomeStatus(WelcomePlaybackStatus value)
        {
            if (!Visible || value == null) return;
            // Failed is a terminal Server state and may remain visible until acknowledgement.
            // Do not let an old failed request dismiss a newly displayed standby page before
            // the visitor has actually pressed the central wake button.
            if (value.state == WelcomePlaybackState.Failed && !requesting) return;
            switch (value.state)
            {
                case WelcomePlaybackState.Requested:
                case WelcomePlaybackState.Preparing:
                    wakeButton.interactable = false;
                    wakeLabel.text = "正在准备";
                    wakeHint.text = "连接欢迎语音";
                    status.text = string.IsNullOrWhiteSpace(value.message) ? "正在准备欢迎语音…" : value.message;
                    break;
                case WelcomePlaybackState.Playing:
                    wakeButton.gameObject.SetActive(false);
                    SetPulseVisible(false);
                    caption.text = FindCaption(value.captions, value.positionSeconds);
                    if (string.IsNullOrWhiteSpace(caption.text)) caption.text = "欢迎来到智慧展厅";
                    status.text = string.IsNullOrWhiteSpace(value.message) ? "欢迎语音播放中" : value.message;
                    break;
                case WelcomePlaybackState.Completed:
                    caption.text = "欢迎莅临，讲解首页已为您准备就绪";
                    status.text = "正在进入讲解首页…";
                    host.StartCoroutine(CompleteNextFrame());
                    break;
                case WelcomePlaybackState.Failed:
                    if (IsWelcomeAudioUnavailable(value.message))
                    {
                        requesting = false;
                        caption.text = string.Empty;
                        status.text = "欢迎语音未配置，正在进入讲解首页…";
                        host.StartCoroutine(CompleteNextFrame());
                        break;
                    }
                    ShowRequestFailure(value.message);
                    break;
            }
        }

        public void ShowRequestFailure(string message)
        {
            if (!Visible) return;
            requesting = false;
            wakeButton.gameObject.SetActive(false);
            SetPulseVisible(false);
            caption.text = "欢迎语音暂时无法播放";
            status.text = string.IsNullOrWhiteSpace(message)
                ? "连接未成功，您仍可进入讲解首页继续使用。"
                : message;
            homeFallbackButton.gameObject.SetActive(true);
        }

        private void RequestWelcome()
        {
            if (requesting) return;
            requesting = true;
            wakeButton.interactable = false;
            wakeLabel.text = "正在连接";
            wakeHint.text = "请稍候";
            status.text = "正在连接LED播放端，准备欢迎语音…";
            WelcomeRequested?.Invoke();
        }

        private void BuildTopBrand(TouchUiFactory factory)
        {
            var mark = factory.Image("Welcome Brand Mark", root.transform, new Color(.18f, .65f, 1f, 1));
            mark.sprite = GetCircleSprite();
            TouchUiFactory.Anchor(mark.rectTransform, 0, 1, 0, 1, 42, -76, 90, -28);
            mark.raycastTarget = false;
            var markText = factory.Label("Welcome Brand Mark Text", mark.transform, "TG", 16,
                FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(markText.rectTransform);
            var systemName = factory.Label("Welcome System Name", root.transform, "展厅自动讲解系统", 25,
                FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(systemName.rectTransform, 0, 1, .36f, 1, 104, -66, 0, -24);
            var systemSubtitle = factory.Label("Welcome System Subtitle", root.transform, "智慧展厅 · 中控终端", 14,
                FontStyle.Normal, new Color(.68f, .82f, .96f, 1), TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(systemSubtitle.rectTransform, 0, 1, .36f, 1, 104, -91, 0, -62);
            clock = factory.Label("Welcome Local Clock", root.transform, string.Empty, 20,
                FontStyle.Bold, Color.white, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(clock.rectTransform, .70f, 1, 1, 1, 0, -70, -42, -24);
        }

        private void BuildPulseRings(TouchUiFactory factory, TouchTheme theme)
        {
            for (var index = 0; index < 3; index++)
            {
                var alpha = .22f - index * .045f;
                var ring = factory.Image("Welcome Touch Pulse " + index, root.transform,
                    new Color(theme.Primary.r, .72f, 1f, alpha));
                ring.sprite = GetCircleSprite();
                var radius = 122 + index * 34;
                TouchUiFactory.Anchor(ring.rectTransform, .5f, .5f, .5f, .5f,
                    -radius, -94 - radius, radius, -94 + radius);
                ring.raycastTarget = false;
                pulseRings.Add(ring.rectTransform);
            }
        }

        private void SetPulseVisible(bool visible)
        {
            for (var index = 0; index < pulseRings.Count; index++)
                pulseRings[index].gameObject.SetActive(visible);
        }

        private IEnumerator CompleteNextFrame()
        {
            yield return null;
            Complete();
        }

        private void Complete()
        {
            Hide();
            Entered?.Invoke();
        }

        private static Text Label(TouchUiFactory factory, Transform parent, string text, int size,
            FontStyle style, Color color, float bottom, float top)
        {
            var label = factory.Label("Welcome " + text, parent, text, size, style, color, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(label.rectTransform, .10f, .5f, .90f, .5f, 0, bottom, 0, top);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Math.Max(16, size - 16);
            label.resizeTextMaxSize = size;
            label.raycastTarget = false;
            return label;
        }

        private void StartAnimation()
        {
            StopAnimation();
            animation = host.StartCoroutine(Animate());
        }

        private void StopAnimation()
        {
            if (animation == null) return;
            host.StopCoroutine(animation);
            animation = null;
        }

        private IEnumerator Animate()
        {
            while (root.activeSelf)
            {
                for (var index = 0; index < pulseRings.Count; index++)
                {
                    var scale = 1f + Mathf.Sin(Time.unscaledTime * 1.3f - index * .7f) * .035f;
                    pulseRings[index].localScale = new Vector3(scale, scale, 1);
                }
                var now = DateTime.Now;
                if (renderedSecond != now.Second)
                {
                    renderedSecond = now.Second;
                    clock.text = now.ToString("yyyy-MM-dd   HH:mm:ss");
                }
                yield return null;
            }
        }

        private static void AddShadow(Graphic graphic, Color color, Vector2 distance)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
        }

        private static Sprite LoadDefaultBackground()
        {
            if (defaultBackgroundSprite != null) return defaultBackgroundSprite;
            var texture = Resources.Load<Texture2D>("Touch/touch-technology-background");
            if (texture == null) return null;
            defaultBackgroundSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100);
            defaultBackgroundSprite.name = "TG Welcome Technology Background";
            defaultBackgroundSprite.hideFlags = HideFlags.HideAndDontSave;
            return defaultBackgroundSprite;
        }

        private static Sprite GetCircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TG Welcome Circle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[size * size];
            var center = (size - 1) * .5f;
            var radius = center - 1;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                var alpha = Mathf.Clamp01(radius - distance + 1f);
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            circleSprite.name = "TG Welcome Circle";
            circleSprite.hideFlags = HideFlags.HideAndDontSave;
            return circleSprite;
        }

        private static string FindCaption(WelcomeCaptionCue[] cues, double position) => cues == null
            ? string.Empty
            : (cues.FirstOrDefault(cue => cue != null && position >= cue.startSeconds && position <= cue.endSeconds)?.text
               ?? string.Empty);

        private static bool IsWelcomeAudioUnavailable(string message) =>
            !string.IsNullOrWhiteSpace(message) &&
            (message.Contains("未配置") || message.Contains("未启用"));

        private static string TextOf(UiExperienceConfig config, string key, string fallback) =>
            config.touchElements?.LastOrDefault(item => item != null && item.key == key &&
                                                  !string.IsNullOrWhiteSpace(item.text))?.text ?? fallback;

        private static string Resolve(string url, Func<string, string> resolver) =>
            string.IsNullOrWhiteSpace(url) ? null : (resolver == null ? url : resolver(url));
    }
}
