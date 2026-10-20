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
        private static Sprite globeSprite;
        private static Sprite ringSprite;
        private static Sprite glowSprite;
        private static Sprite buttonSprite;
        private static Sprite handSprite;

        private readonly MonoBehaviour host;
        private readonly TouchImageLoader imageLoader;
        private readonly GameObject root;
        private readonly Image background;
        private readonly Image globe;
        private readonly WelcomeAmbientEffects ambientEffects;
        private Image brandLogo;
        private Text brandMarkText;
        private readonly CanvasGroup headingGroup;
        private readonly Image wakeImage;
        private readonly Image wakeGlow;
        private readonly Image transitionCover;
        private readonly Text eyebrow;
        private readonly Text title;
        private readonly Text subtitle;
        private readonly Text caption;
        private readonly Text status;
        private Text clock;
        private readonly Button wakeButton;
        private readonly Button homeFallbackButton;
        private readonly List<Image> pulseRings = new List<Image>();
        private Coroutine animation;
        private Coroutine transition;
        private string logoUrl;
        private bool requesting;
        private bool completing;
        private float shownAt;
        private float pressFeedbackStartedAt = -1f;
        private float pressFeedbackUntil;
        private int renderedSecond = -1;

        public bool Visible => root.activeSelf;
        public bool IsRequestPending => requesting;
        public WelcomeAmbientEffects AmbientEffects => ambientEffects;
        public event Action WelcomeRequested;
        public event Action Entered;

        public WelcomeExperiencePage(MonoBehaviour host, TouchUiFactory factory, TouchTheme theme,
            TouchImageLoader imageLoader, Transform canvas)
        {
            this.host = host;
            this.imageLoader = imageLoader;

            background = factory.Image("Standby Clean Exhibition Background", canvas, Color.white);
            TouchUiFactory.Stretch(background.rectTransform);
            background.sprite = LoadDefaultBackground();
            background.type = Image.Type.Simple;
            background.preserveAspect = false;
            background.raycastTarget = false;
            root = background.gameObject;

            globe = factory.Image("Standby Holographic Globe", root.transform, Color.white);
            TouchUiFactory.Stretch(globe.rectTransform);
            globe.sprite = LoadGlobe();
            globe.type = Image.Type.Simple;
            globe.preserveAspect = false;
            // The source globe already contains a luminous edge. A small opacity reduction
            // keeps the continents crisp without letting the halo overpower the headings.
            globe.color = new Color(.94f, .97f, 1f, .93f);
            globe.raycastTarget = false;

            var ambientLayer = factory.Rect("Welcome Ambient Effects", root.transform);
            TouchUiFactory.Stretch(ambientLayer);
            ambientEffects = ambientLayer.gameObject.AddComponent<WelcomeAmbientEffects>();
            ambientEffects.Initialize(GetGlowSprite());

            BuildTopBrand(factory);

            var headings = factory.Rect("Standby Welcome Headings", root.transform);
            TouchUiFactory.Stretch(headings);
            headingGroup = headings.gameObject.AddComponent<CanvasGroup>();
            headingGroup.interactable = false;
            headingGroup.blocksRaycasts = false;
            var headingAccent = factory.Image("Standby Heading Accent", headings, new Color(.51f, .94f, 1f, .92f));
            TouchUiFactory.Anchor(headingAccent.rectTransform, .5f, 1, .5f, 1, -44, -115, 44, -112);
            headingAccent.raycastTarget = false;
            eyebrow = TopLabel(factory, headings, "展厅自动讲解系统", 24, FontStyle.Bold,
                new Color(.88f, .96f, 1f, 1), 132, 40);
            title = TopLabel(factory, headings, "欢迎莅临智慧展厅", 76, FontStyle.Bold,
                Color.white, 184, 88);
            AddShadow(title, new Color(0, .08f, .24f, .78f), new Vector2(0, -3));
            subtitle = TopLabel(factory, headings, "探索 · 体验 · 发现 · 共创未来", 28, FontStyle.Normal,
                new Color(.88f, .96f, 1f, 1), 286, 50);

            BuildPulseRings(factory, theme);
            wakeGlow = factory.Image("Standby Touch Soft Glow", root.transform, new Color(.11f, .72f, 1f, .58f));
            wakeGlow.sprite = GetGlowSprite();
            TouchUiFactory.Anchor(wakeGlow.rectTransform, .5f, .5f, .5f, .5f, -126, -421, 126, -169);
            wakeGlow.raycastTarget = false;
            wakeImage = factory.Image("Welcome Touch Action", root.transform, Color.white);
            wakeImage.sprite = GetButtonSprite();
            TouchUiFactory.Anchor(wakeImage.rectTransform, .5f, .5f, .5f, .5f, -86, -381, 86, -209);
            wakeButton = wakeImage.gameObject.AddComponent<Button>();
            wakeButton.targetGraphic = wakeImage;
            wakeButton.onClick.AddListener(RequestWelcome);
            var colors = wakeButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.88f, .96f, 1f, 1);
            colors.pressedColor = new Color(.64f, .84f, 1f, 1);
            colors.disabledColor = new Color(.78f, .88f, 1f, .86f);
            colors.fadeDuration = .08f;
            wakeButton.colors = colors;

            var hand = factory.Image("Standby White Touch Gesture", wakeImage.transform, Color.white);
            hand.sprite = GetHandSprite();
            TouchUiFactory.Anchor(hand.rectTransform, .5f, .5f, .5f, .5f, -43, -44, 43, 44);
            hand.raycastTarget = false;
            AddShadow(hand, new Color(.0f, .18f, .55f, .46f), new Vector2(0, -2));
            var gestureRipple = factory.Image("Standby Gesture Ripple", wakeImage.transform,
                new Color(.88f, .98f, 1f, .88f));
            gestureRipple.sprite = GetRingSprite();
            TouchUiFactory.Anchor(gestureRipple.rectTransform, .5f, .5f, .5f, .5f, -17, 25, 17, 59);
            gestureRipple.raycastTarget = false;

            caption = BottomLabel(factory, root.transform, string.Empty, 32, FontStyle.Normal,
                Color.white, 166, 238);
            caption.resizeTextMinSize = 22;
            AddShadow(caption, new Color(0, .06f, .18f, .9f), new Vector2(0, -2));
            AddOutline(caption, new Color(0, .055f, .16f, .58f), new Vector2(1.5f, 1.5f));
            status = BottomLabel(factory, root.transform, "轻触屏幕，开启智慧参观之旅", 27,
                FontStyle.Bold, new Color(.94f, .985f, 1f, 1f), 88, 150);
            AddShadow(status, new Color(0, .08f, .22f, .88f), new Vector2(0, -2));
            AddOutline(status, new Color(0, .055f, .16f, .68f), new Vector2(1.5f, 1.5f));

            homeFallbackButton = factory.TouchButton(root.transform, "进入讲解首页", false, Complete);
            TouchUiFactory.Anchor(homeFallbackButton.GetComponent<RectTransform>(), .5f, 0, .5f, 0,
                -170, 27, 170, 91);
            homeFallbackButton.gameObject.SetActive(false);

            transitionCover = factory.Image("Standby Page Transition", canvas, new Color(.0f, .035f, .14f, 0));
            TouchUiFactory.Stretch(transitionCover.rectTransform);
            transitionCover.raycastTarget = true;
            transitionCover.gameObject.SetActive(false);
        }

        public void Configure(UiExperienceConfig config, Func<string, string> resolver)
        {
            if (config == null) return;
            eyebrow.text = TextOf(config, "welcome.eyebrow", config.touchTitle ?? "展厅自动讲解系统");
            title.text = TextOf(config, "welcome.title", "欢迎莅临智慧展厅");
            subtitle.text = TextOf(config, "welcome.subtitle", "探索 · 体验 · 发现 · 共创未来");

            // The approved standby artwork is fixed. Server-configured images may still be used
            // elsewhere, but cannot replace this page with the previous tunnel background.
            var logoElement = config.touchElements?.LastOrDefault(item => item != null && item.key == "welcome.logo");
            var logo = logoElement == null ? null : Resolve(logoElement.assetUrl, resolver);
            if (!string.Equals(logoUrl, logo, StringComparison.OrdinalIgnoreCase))
            {
                logoUrl = logo;
                UseLocalBrandLogo();
                if (!string.IsNullOrWhiteSpace(logo)) imageLoader.Load(brandLogo, logo, success =>
                {
                    if (string.Equals(logoUrl, logo, StringComparison.OrdinalIgnoreCase))
                    {
                        if (success)
                        {
                            brandLogo.gameObject.SetActive(true);
                            brandMarkText.gameObject.SetActive(false);
                        }
                        else UseLocalBrandLogo();
                    }
                });
            }
        }

        public void Show()
        {
            root.transform.SetAsLastSibling();
            root.SetActive(true);
            requesting = false;
            completing = false;
            wakeButton.gameObject.SetActive(true);
            wakeButton.interactable = true;
            wakeGlow.gameObject.SetActive(true);
            wakeImage.rectTransform.localScale = Vector3.one;
            homeFallbackButton.gameObject.SetActive(false);
            caption.text = string.Empty;
            status.text = "轻触屏幕，开启智慧参观之旅";
            shownAt = Time.unscaledTime;
            pressFeedbackStartedAt = -1f;
            pressFeedbackUntil = -1f;
            renderedSecond = -1;
            headingGroup.alpha = 0;
            SetPulseVisible(true);
            StartAnimation();
            StartTransition(FadeIntoStandby());
        }

        public void Hide()
        {
            StopAnimation();
            StopTransition();
            requesting = false;
            completing = false;
            root.SetActive(false);
            transitionCover.gameObject.SetActive(false);
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
                    status.text = string.IsNullOrWhiteSpace(value.message) ? "正在准备欢迎语音…" : value.message;
                    break;
                case WelcomePlaybackState.Playing:
                    wakeButton.gameObject.SetActive(false);
                    wakeGlow.gameObject.SetActive(false);
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
            wakeGlow.gameObject.SetActive(false);
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
            pressFeedbackStartedAt = Time.unscaledTime;
            pressFeedbackUntil = pressFeedbackStartedAt + .24f;
            wakeButton.interactable = false;
            status.text = "正在连接LED播放端，准备欢迎语音…";
            WelcomeRequested?.Invoke();
        }

        private void BuildTopBrand(TouchUiFactory factory)
        {
            var markGlow = factory.Image("Standby Brand Soft Halo", root.transform,
                new Color(.13f, .77f, 1f, .18f));
            markGlow.sprite = GetGlowSprite();
            TouchUiFactory.Anchor(markGlow.rectTransform, 0, 1, 0, 1, 20, -99, 102, -17);
            markGlow.raycastTarget = false;
            brandLogo = factory.Image("Standby Brand Logo", root.transform, Color.white);
            brandLogo.sprite = BrandVisualAssets.Logo;
            brandLogo.preserveAspect = true;
            TouchUiFactory.Anchor(brandLogo.rectTransform, 0, 1, 0, 1, 28, -92, 94, -26);
            brandLogo.raycastTarget = false;
            brandMarkText = factory.Label("Standby Brand Mark Text", brandLogo.transform, "TG", 23,
                FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(brandMarkText.rectTransform);
            brandMarkText.gameObject.SetActive(brandLogo.sprite == null);
            var systemName = factory.Label("Welcome System Name", root.transform, "展厅自动讲解系统", 25,
                FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(systemName.rectTransform, 0, 1, 0, 1, 106, -65, 410, -26);
            AddShadow(systemName, new Color(.0f, .10f, .28f, .62f), new Vector2(0, -1));
            var systemSubtitle = factory.Label("Welcome System Subtitle", root.transform, "智慧展厅 · 中控终端", 14,
                FontStyle.Normal, new Color(.80f, .92f, 1f, 1), TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(systemSubtitle.rectTransform, 0, 1, 0, 1, 106, -89, 402, -61);
            clock = factory.Label("Welcome Local Clock", root.transform, string.Empty, 22,
                FontStyle.Bold, Color.white, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(clock.rectTransform, 1, 1, 1, 1, -365, -75, -36, -31);
            AddShadow(clock, new Color(.0f, .10f, .28f, .58f), new Vector2(0, -1));
        }

        private void UseLocalBrandLogo()
        {
            brandLogo.sprite = BrandVisualAssets.Logo;
            brandLogo.color = Color.white;
            brandLogo.preserveAspect = true;
            brandLogo.gameObject.SetActive(brandLogo.sprite != null);
            brandMarkText.gameObject.SetActive(brandLogo.sprite == null);
        }

        private void BuildPulseRings(TouchUiFactory factory, TouchTheme theme)
        {
            for (var index = 0; index < 2; index++)
            {
                var alpha = .23f - index * .055f;
                var ring = factory.Image("Welcome Touch Pulse " + index, root.transform,
                    new Color(.23f, .86f, 1f, alpha));
                ring.sprite = GetRingSprite();
                var radius = 97 + index * 14;
                TouchUiFactory.Anchor(ring.rectTransform, .5f, .5f, .5f, .5f,
                    -radius, -295 - radius, radius, -295 + radius);
                ring.raycastTarget = false;
                pulseRings.Add(ring);
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
            if (completing) return;
            completing = true;
            wakeButton.interactable = false;
            StartTransition(FadeToHome());
        }

        private static Text TopLabel(TouchUiFactory factory, Transform parent, string text, int size,
            FontStyle style, Color color, float top, float height)
        {
            var label = factory.Label("Welcome " + text, parent, text, size, style, color, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(label.rectTransform, .10f, 1, .90f, 1, 0, -top - height, 0, -top);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Math.Max(16, size - 16);
            label.resizeTextMaxSize = size;
            label.raycastTarget = false;
            return label;
        }

        private static Text BottomLabel(TouchUiFactory factory, Transform parent, string text, int size,
            FontStyle style, Color color, float bottom, float top)
        {
            var label = factory.Label("Welcome " + text, parent, text, size, style, color, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(label.rectTransform, .10f, 0, .90f, 0, 0, bottom, 0, top);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Math.Max(16, size - 8);
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
                var elapsed = Time.unscaledTime - shownAt;
                headingGroup.alpha = Mathf.Clamp01(elapsed / .75f);
                var breathPhase = Time.unscaledTime * Mathf.PI * 2f / 4.8f;
                var breathWave = Mathf.Sin(breathPhase);
                var breath = 1f + breathWave * .018f;
                var clickWave = 0f;
                if (Time.unscaledTime < pressFeedbackUntil && pressFeedbackStartedAt >= 0f)
                {
                    var progress = Mathf.Clamp01((Time.unscaledTime - pressFeedbackStartedAt) / .24f);
                    clickWave = Mathf.Sin(progress * Mathf.PI);
                }
                wakeImage.rectTransform.localScale = Vector3.one * breath * (1f - clickWave * .075f);
                wakeGlow.color = new Color(.11f, .72f, 1f,
                    .34f + breathWave * .065f + clickWave * .13f);
                for (var index = 0; index < pulseRings.Count; index++)
                {
                    var phase = Time.unscaledTime * Mathf.PI * 2f / 5.4f - index * 1.1f;
                    var scale = 1f + Mathf.Sin(phase) * .025f + clickWave * .018f;
                    pulseRings[index].rectTransform.localScale = Vector3.one * scale;
                    pulseRings[index].color = new Color(.23f, .86f, 1f,
                        (.22f - index * .055f) * (.80f + .20f * Mathf.Sin(phase)) + clickWave * .045f);
                }
                var now = DateTime.Now;
                if (renderedSecond != now.Second)
                {
                    renderedSecond = now.Second;
                    clock.text = now.ToString("yyyy-MM-dd HH:mm:ss");
                }
                yield return null;
            }
        }

        private void StartTransition(IEnumerator routine)
        {
            StopTransition();
            transition = host.StartCoroutine(routine);
        }

        private void StopTransition()
        {
            if (transition == null) return;
            host.StopCoroutine(transition);
            transition = null;
        }

        private IEnumerator FadeIntoStandby()
        {
            transitionCover.transform.SetAsLastSibling();
            transitionCover.gameObject.SetActive(true);
            yield return FadeCover(1f, 0f, .40f);
            transitionCover.gameObject.SetActive(false);
            transition = null;
        }

        private IEnumerator FadeToHome()
        {
            transitionCover.transform.SetAsLastSibling();
            transitionCover.gameObject.SetActive(true);
            yield return FadeCover(0f, 1f, .30f);
            StopAnimation();
            root.SetActive(false);
            requesting = false;
            Entered?.Invoke();
            yield return FadeCover(1f, 0f, .35f);
            transitionCover.gameObject.SetActive(false);
            completing = false;
            transition = null;
        }

        private IEnumerator FadeCover(float start, float end, float seconds)
        {
            for (var elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
            {
                var amount = Mathf.SmoothStep(start, end, Mathf.Clamp01(elapsed / seconds));
                transitionCover.color = new Color(0f, .035f, .14f, amount);
                yield return null;
            }
            transitionCover.color = new Color(0f, .035f, .14f, end);
        }

        private static void AddShadow(Graphic graphic, Color color, Vector2 distance)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
        }

        private static void AddOutline(Graphic graphic, Color color, Vector2 distance)
        {
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
        }

        private static Sprite LoadDefaultBackground()
        {
            if (defaultBackgroundSprite != null) return defaultBackgroundSprite;
            var texture = Resources.Load<Texture2D>(
                "TGExhibitionUI/Unity_4K_Standby_Layers/01_Exhibition_Clean_Background_3840x2160");
            if (texture == null)
            {
                Debug.LogError("Layered standby background is missing; using the legacy composite fallback.");
                texture = Resources.Load<Texture2D>("Touch/standby-clean-background");
                if (texture == null) return null;
            }
            defaultBackgroundSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100);
            defaultBackgroundSprite.name = "TG Standby Layered Clean Background";
            defaultBackgroundSprite.hideFlags = HideFlags.HideAndDontSave;
            return defaultBackgroundSprite;
        }

        private static Sprite LoadGlobe()
        {
            if (globeSprite != null) return globeSprite;
            var texture = Resources.Load<Texture2D>(
                "TGExhibitionUI/Unity_4K_Standby_Layers/02_Holographic_Globe_Transparent_3840x2160");
            if (texture == null)
            {
                Debug.LogError("Layered standby globe is missing from Resources.");
                return null;
            }
            globeSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100);
            globeSprite.name = "TG Standby Holographic Globe";
            globeSprite.hideFlags = HideFlags.HideAndDontSave;
            return globeSprite;
        }

        private static Sprite GetRingSprite()
        {
            if (ringSprite != null) return ringSprite;
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TG Standby Soft Ring",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[size * size];
            var center = (size - 1) * .5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var radius = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                var edge = Mathf.Exp(-Mathf.Pow((radius - .83f) / .035f, 2));
                var halo = Mathf.Exp(-Mathf.Pow((radius - .83f) / .13f, 2)) * .23f;
                var alpha = Mathf.Clamp01(edge + halo) * Mathf.Clamp01((1f - radius) * 16f);
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            ringSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            ringSprite.name = "TG Standby Soft Ring";
            ringSprite.hideFlags = HideFlags.HideAndDontSave;
            return ringSprite;
        }

        private static Sprite GetGlowSprite()
        {
            if (glowSprite != null) return glowSprite;
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TG Standby Touch Glow",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[size * size];
            var center = (size - 1) * .5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var radius = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                var alpha = Mathf.Pow(Mathf.Clamp01(1f - radius), 2) * .78f;
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            glowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            glowSprite.name = "TG Standby Touch Glow";
            glowSprite.hideFlags = HideFlags.HideAndDontSave;
            return glowSprite;
        }

        private static Sprite GetButtonSprite()
        {
            if (buttonSprite != null) return buttonSprite;
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TG Standby Touch Button",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[size * size];
            var center = (size - 1) * .5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var radius = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                var radial = Mathf.Clamp01(radius * radius);
                var color = Color.Lerp(new Color(.11f, .62f, 1f), new Color(.018f, .24f, .68f), radial);
                var glassHighlight = Mathf.Clamp01((y / (float)size - .48f) * 1.25f) * (1f - radial) * .15f;
                color = Color.Lerp(color, new Color(.42f, .90f, 1f), glassHighlight);
                var innerRim = Mathf.Exp(-Mathf.Pow((radius - .79f) / .018f, 2)) * .22f;
                var outerRim = Mathf.Exp(-Mathf.Pow((radius - .925f) / .018f, 2));
                color = Color.Lerp(color, new Color(.37f, .91f, 1f), outerRim * .88f + innerRim);
                color.a = Mathf.Clamp01((.99f - radius) * 45f);
                pixels[y * size + x] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            buttonSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            buttonSprite.name = "TG Standby Touch Button";
            buttonSprite.hideFlags = HideFlags.HideAndDontSave;
            return buttonSprite;
        }

        private static Sprite GetHandSprite()
        {
            if (handSprite != null) return handSprite;
            const int size = 192;
            var outline = new[]
            {
                new Vector2(63, 20), new Vector2(113, 20), new Vector2(126, 28),
                new Vector2(135, 42), new Vector2(139, 75), new Vector2(136, 87),
                new Vector2(129, 92), new Vector2(121, 90), new Vector2(116, 98),
                new Vector2(107, 101), new Vector2(98, 96), new Vector2(94, 104),
                new Vector2(85, 107), new Vector2(77, 102), new Vector2(77, 151),
                new Vector2(74, 162), new Vector2(66, 168), new Vector2(57, 165),
                new Vector2(52, 157), new Vector2(52, 94), new Vector2(43, 105),
                new Vector2(34, 109), new Vector2(25, 106), new Vector2(19, 98),
                new Vector2(18, 88), new Vector2(22, 79), new Vector2(35, 61),
                new Vector2(48, 37), new Vector2(56, 25)
            };
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TG White Touch Gesture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var coverage = 0;
                for (var sampleY = 0; sampleY < 3; sampleY++)
                for (var sampleX = 0; sampleX < 3; sampleX++)
                    if (InsidePolygon(new Vector2(x + (sampleX + .5f) / 3f,
                            y + (sampleY + .5f) / 3f), outline)) coverage++;
                pixels[y * size + x] = new Color(1, 1, 1, coverage / 9f);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            handSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            handSprite.name = "TG White Touch Gesture";
            handSprite.hideFlags = HideFlags.HideAndDontSave;
            return handSprite;
        }

        private static bool InsidePolygon(Vector2 point, Vector2[] vertices)
        {
            var inside = false;
            for (int index = 0, previous = vertices.Length - 1; index < vertices.Length; previous = index++)
            {
                var a = vertices[index];
                var b = vertices[previous];
                if ((a.y > point.y) == (b.y > point.y)) continue;
                if (point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
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
