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
    /// Touch-side presentation only. Audio is deliberately not loaded or played here:
    /// Server coordinates the LED playback host and this page follows its reported state.
    /// </summary>
    public sealed class WelcomeExperiencePage
    {
        private readonly MonoBehaviour host;
        private readonly TouchImageLoader imageLoader;
        private readonly GameObject root;
        private readonly Image background;
        private readonly Image brandLogo;
        private readonly Text eyebrow;
        private readonly Text title;
        private readonly Text subtitle;
        private readonly Text caption;
        private readonly Text status;
        private readonly Button wakeButton;
        private readonly Button homeFallbackButton;
        private readonly List<RectTransform> lightBands = new List<RectTransform>();
        private Coroutine animation;
        private string backgroundUrl;
        private string logoUrl;
        private bool requesting;

        public bool Visible => root.activeSelf;
        public event Action WelcomeRequested;
        public event Action Entered;

        public WelcomeExperiencePage(MonoBehaviour host, TouchUiFactory factory, TouchTheme theme,
            TouchImageLoader imageLoader, Transform canvas)
        {
            this.host = host;
            this.imageLoader = imageLoader;
            background = factory.Image("Welcome Attract Background", canvas, new Color(.95f, .985f, 1f, 1));
            TouchUiFactory.Stretch(background.rectTransform);
            root = background.gameObject;

            // The full surface is the intentional wake target. All visual children are non-raycast.
            wakeButton = root.AddComponent<Button>();
            wakeButton.targetGraphic = background;
            wakeButton.onClick.AddListener(RequestWelcome);
            var buttonColors = wakeButton.colors;
            buttonColors.normalColor = Color.white;
            buttonColors.highlightedColor = new Color(.97f, .99f, 1f, 1);
            buttonColors.pressedColor = new Color(.90f, .96f, 1f, 1);
            buttonColors.fadeDuration = .08f;
            wakeButton.colors = buttonColors;

            CreateBand(factory, theme, new Vector2(-.22f, .25f), new Vector2(.52f, .09f), .16f);
            CreateBand(factory, theme, new Vector2(.68f, .68f), new Vector2(.46f, .06f), .12f);
            CreateBand(factory, theme, new Vector2(.08f, .78f), new Vector2(.40f, .05f), .10f);
            var halo = factory.Image("Welcome Brand Halo", root.transform, new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .075f));
            TouchUiFactory.Anchor(halo.rectTransform, .5f, .5f, .5f, .5f, -260, -260, 260, 260);
            halo.raycastTarget = false;
            brandLogo = factory.Image("Welcome Brand Logo", root.transform, Color.white);
            TouchUiFactory.Anchor(brandLogo.rectTransform, .5f, .5f, .5f, .5f, -90, -250, 90, -160);
            brandLogo.preserveAspect = true;
            brandLogo.raycastTarget = false;
            brandLogo.gameObject.SetActive(false);

            eyebrow = Label(factory, root.transform, "智慧展厅 · 自动讲解系统", 22, FontStyle.Bold, theme.Primary, -120, -78);
            title = Label(factory, root.transform, "欢迎莅临智慧展厅", 62, FontStyle.Bold, theme.TextPrimary, -52, 34);
            subtitle = Label(factory, root.transform, "探索 · 体验 · 发现 · 共创未来", 26, FontStyle.Normal, theme.TextSecondary, 45, 94);
            caption = Label(factory, root.transform, string.Empty, 30, FontStyle.Normal, theme.TextPrimary, 112, 172);
            status = Label(factory, root.transform, "轻触屏幕，开启智慧参观之旅", 22, FontStyle.Normal, theme.TextSecondary, 174, 230);
            homeFallbackButton = factory.TouchButton(root.transform, "进入讲解首页", false, Complete);
            TouchUiFactory.Anchor(homeFallbackButton.GetComponent<RectTransform>(), .5f, .5f, .5f, .5f, -165, -290, 165, -222);
            homeFallbackButton.gameObject.SetActive(false);
            foreach (var graphic in root.GetComponentsInChildren<Graphic>())
                if (graphic != background) graphic.raycastTarget = false;
            homeFallbackButton.GetComponent<Image>().raycastTarget = true;
        }

        public void Configure(UiExperienceConfig config, Func<string, string> resolver)
        {
            if (config == null) return;
            eyebrow.text = TextOf(config, "welcome.eyebrow", config.touchTitle ?? "智慧展厅 · 自动讲解系统");
            title.text = TextOf(config, "welcome.title", "欢迎莅临智慧展厅");
            subtitle.text = TextOf(config, "welcome.subtitle", "探索 · 体验 · 发现 · 共创未来");
            var element = config.touchElements?.LastOrDefault(item => item != null && item.key == "welcome.background");
            var url = element == null ? null : Resolve(element.assetUrl, resolver);
            if (string.IsNullOrWhiteSpace(url)) url = Resolve(config.touchBackgroundUrl, resolver);
            if (!string.Equals(backgroundUrl, url, StringComparison.OrdinalIgnoreCase))
            {
                backgroundUrl = url;
                background.sprite = null;
                background.color = new Color(.95f, .985f, 1f, 1);
                if (!string.IsNullOrWhiteSpace(url)) imageLoader.Load(background, url, _ => { });
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
                    if (string.Equals(logoUrl, logo, StringComparison.OrdinalIgnoreCase)) brandLogo.gameObject.SetActive(success);
                });
            }
        }

        public void Show()
        {
            root.transform.SetAsLastSibling();
            root.SetActive(true);
            requesting = false;
            wakeButton.interactable = true;
            homeFallbackButton.gameObject.SetActive(false);
            caption.text = string.Empty;
            status.text = "轻触屏幕，开启智慧参观之旅";
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
            switch (value.state)
            {
                case WelcomePlaybackState.Requested:
                case WelcomePlaybackState.Preparing:
                    status.text = string.IsNullOrWhiteSpace(value.message) ? "正在准备欢迎语音…" : value.message;
                    break;
                case WelcomePlaybackState.Playing:
                    status.text = string.IsNullOrWhiteSpace(value.message) ? "欢迎语音播放中" : value.message;
                    caption.text = FindCaption(value.captions, value.positionSeconds);
                    break;
                case WelcomePlaybackState.Completed:
                    caption.text = string.Empty;
                    status.text = "欢迎莅临，正在进入讲解首页…";
                    host.StartCoroutine(CompleteNextFrame());
                    break;
                case WelcomePlaybackState.Failed:
                    requesting = false;
                    wakeButton.interactable = true;
                    status.text = string.IsNullOrWhiteSpace(value.message) ? "欢迎语音暂不可用，可进入讲解首页。" : value.message;
                    homeFallbackButton.gameObject.SetActive(true);
                    break;
            }
        }

        private void RequestWelcome()
        {
            if (requesting) return;
            requesting = true;
            wakeButton.interactable = false;
            status.text = "正在连接LED播放端，准备欢迎语音…";
            WelcomeRequested?.Invoke();
        }
        private IEnumerator CompleteNextFrame() { yield return null; Complete(); }
        private void Complete() { Hide(); Entered?.Invoke(); }

        private static Text Label(TouchUiFactory factory, Transform parent, string text, int size, FontStyle style, Color color, float bottom, float top)
        {
            var label = factory.Label("Welcome " + text, parent, text, size, style, color, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(label.rectTransform, .12f, .5f, .88f, .5f, 0, bottom, 0, top);
            label.resizeTextForBestFit = true; label.resizeTextMinSize = Math.Max(16, size - 16); label.resizeTextMaxSize = size;
            label.raycastTarget = false; return label;
        }
        private void CreateBand(TouchUiFactory factory, TouchTheme theme, Vector2 anchor, Vector2 size, float alpha)
        {
            var band = factory.Image("Welcome Light Band", root.transform, new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, alpha));
            TouchUiFactory.Anchor(band.rectTransform, anchor.x, anchor.y, anchor.x, anchor.y, -size.x * 960, -size.y * 540, size.x * 960, size.y * 540);
            band.raycastTarget = false; lightBands.Add(band.rectTransform);
        }
        private void StartAnimation() { StopAnimation(); animation = host.StartCoroutine(Animate()); }
        private void StopAnimation() { if (animation != null) { host.StopCoroutine(animation); animation = null; } }
        private IEnumerator Animate()
        {
            while (root.activeSelf)
            {
                for (var i = 0; i < lightBands.Count; i++)
                {
                    var band = lightBands[i];
                    var x = Mathf.Sin(Time.unscaledTime * .16f + i) * 38f;
                    band.anchoredPosition = new Vector2(x, band.anchoredPosition.y);
                }
                yield return null;
            }
        }
        private static string FindCaption(WelcomeCaptionCue[] cues, double position) => cues == null ? string.Empty :
            (cues.FirstOrDefault(cue => cue != null && position >= cue.startSeconds && position <= cue.endSeconds)?.text ?? string.Empty);
        private static string TextOf(UiExperienceConfig config, string key, string fallback) =>
            config.touchElements?.LastOrDefault(item => item != null && item.key == key && !string.IsNullOrWhiteSpace(item.text))?.text ?? fallback;
        private static string Resolve(string url, Func<string, string> resolver) => string.IsNullOrWhiteSpace(url) ? null : (resolver == null ? url : resolver(url));
    }
}
