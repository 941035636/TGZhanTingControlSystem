using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TG.Control.Touch.UI.Components;
using TG.Control.Touch.UI.Services;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Pages
{
    /// <summary>Attract/welcome layer shown before the reception console and after idle timeout.</summary>
    public sealed class WelcomeExperiencePage
    {
        private static Sprite circleSprite;
        private static Sprite ringSprite;

        private readonly MonoBehaviour host;
        private readonly TouchImageLoader imageLoader;
        private readonly GameObject root;
        private readonly Image background;
        private readonly Sprite fallbackBackground;
        private readonly Image brandLogo;
        private readonly Text eyebrow;
        private readonly Text title;
        private readonly Text subtitle;
        private readonly Text actionLabel;
        private readonly Text actionHint;
        private readonly Text status;
        private readonly Button enterButton;
        private readonly Button skipButton;
        private readonly RectTransform touchTarget;
        private readonly List<Image> pulseRings = new List<Image>();
        private readonly AudioSource audioSource;

        private string audioUrl;
        private string loadedBackgroundUrl;
        private string loadedLogoUrl;
        private string actionText = "触碰开启";
        private string hintText = "轻触屏幕任意位置进入";
        private Coroutine audioRoutine;
        private Coroutine preloadRoutine;
        private Coroutine pulseRoutine;
        private AudioClip preloadedClip;
        private float audioLoadProgress;
        private bool audioLoadFailed;
        private bool entering;

        public bool Visible => root.activeSelf;
        public event Action Entered;

        public WelcomeExperiencePage(MonoBehaviour host, TouchUiFactory factory, TouchTheme theme,
            TouchImageLoader imageLoader, Transform canvas)
        {
            this.host = host;
            this.imageLoader = imageLoader;
            background = factory.Image("Welcome Experience", canvas, theme.AppBackground);
            TouchUiFactory.Stretch(background.rectTransform);
            background.preserveAspect = false;
            root = background.gameObject;

            audioSource = root.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;

            var embedded = Resources.Load<Texture2D>("Touch/touch-technology-background");
            if (embedded != null)
            {
                fallbackBackground = Sprite.Create(embedded, new Rect(0, 0, embedded.width, embedded.height),
                    new Vector2(.5f, .5f), 100);
                background.sprite = fallbackBackground;
                background.color = Color.white;
            }

            var veil = factory.Image("Welcome Contrast Veil", root.transform, new Color(.008f, .03f, .085f, .55f));
            TouchUiFactory.Stretch(veil.rectTransform);
            veil.raycastTarget = false;

            var accentLine = factory.Image("Welcome Accent Line", root.transform, theme.ConfigurableAccent);
            TouchUiFactory.Anchor(accentLine.rectTransform, .5f, 1, .5f, 1, -62, -128, 62, -124);
            accentLine.raycastTarget = false;

            brandLogo = factory.Image("Welcome Brand Logo", root.transform, Color.white);
            TouchUiFactory.Anchor(brandLogo.rectTransform, .5f, 1, .5f, 1, -84, -112, 84, -48);
            brandLogo.preserveAspect = true;
            brandLogo.raycastTarget = false;
            brandLogo.gameObject.SetActive(false);

            eyebrow = factory.Label("Welcome Eyebrow", root.transform, "智慧展厅 · 自动讲解系统", 22,
                FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(eyebrow.rectTransform, .18f, 1, .82f, 1, 0, -184, 0, -136);
            ConfigureBestFit(eyebrow, 16, 22);

            title = factory.Label("Welcome Title", root.transform, "欢迎开启自动讲解之旅", 68,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(title.rectTransform, .1f, 1, .9f, 1, 0, -286, 0, -184);
            ConfigureBestFit(title, 42, 68);

            subtitle = factory.Label("Welcome Subtitle", root.transform,
                "轻触屏幕，开启展厅参观与自动讲解", 28,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(subtitle.rectTransform, .14f, 1, .86f, 1, 0, -350, 0, -292);
            ConfigureBestFit(subtitle, 20, 28);

            BuildRings(factory, theme, root.transform);

            var touchImage = factory.Image("Welcome Touch Target", root.transform, theme.PrimarySoft);
            touchImage.sprite = GetCircleSprite();
            touchImage.type = Image.Type.Simple;
            touchTarget = touchImage.rectTransform;
            TouchUiFactory.Anchor(touchTarget, .5f, .5f, .5f, .5f, -158, -228, 158, 88);
            enterButton = touchImage.gameObject.AddComponent<Button>();
            enterButton.targetGraphic = touchImage;
            enterButton.onClick.AddListener(BeginWelcome);
            var colors = enterButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.Lerp(Color.white, theme.PrimaryHighlight, .22f);
            colors.pressedColor = Color.Lerp(Color.white, theme.PrimaryPressed, .38f);
            colors.disabledColor = theme.DisabledControlTint;
            colors.fadeDuration = .08f;
            enterButton.colors = colors;

            actionLabel = factory.Label("Welcome Action", touchTarget, actionText, 32,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(actionLabel.rectTransform, .1f, .5f, .9f, .5f, 0, -4, 0, 54);
            ConfigureBestFit(actionLabel, 24, 32);
            actionHint = factory.Label("Welcome Action Hint", touchTarget, hintText, 18,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(actionHint.rectTransform, .08f, .5f, .92f, .5f, 0, -62, 0, -12);
            ConfigureBestFit(actionHint, 15, 18);

            status = factory.Label("Welcome Status", root.transform, string.Empty, 20,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(status.rectTransform, .24f, .5f, .76f, .5f, 0, -288, 0, -242);
            ConfigureBestFit(status, 16, 20);

            skipButton = factory.TouchButton(root.transform, "跳过欢迎词", false, Complete);
            TouchUiFactory.Anchor(skipButton.GetComponent<RectTransform>(), .5f, .5f, .5f, .5f,
                -150, -374, 150, -302);
            skipButton.gameObject.SetActive(false);
        }

        public void Configure(UiExperienceConfig config, Func<string, string> resolver)
        {
            if (config == null) return;
            var eyebrowValue = Find(config.touchElements, "welcome.eyebrow")?.text;
            var titleValue = Find(config.touchElements, "welcome.title")?.text;
            var subtitleValue = Find(config.touchElements, "welcome.subtitle")?.text;
            var actionValue = Find(config.touchElements, "welcome.action")?.text;
            var hintValue = Find(config.touchElements, "welcome.hint")?.text;
            eyebrow.text = string.IsNullOrWhiteSpace(eyebrowValue)
                ? (string.IsNullOrWhiteSpace(config.touchTitle) ? "智慧展厅 · 自动讲解系统" : config.touchTitle)
                : eyebrowValue;
            title.text = string.IsNullOrWhiteSpace(titleValue) ? "欢迎开启自动讲解之旅" : titleValue;
            subtitle.text = string.IsNullOrWhiteSpace(subtitleValue)
                ? "轻触屏幕，开启展厅参观与自动讲解" : subtitleValue;
            actionText = string.IsNullOrWhiteSpace(actionValue) ? "触碰开启" : actionValue;
            hintText = string.IsNullOrWhiteSpace(hintValue) ? "轻触屏幕任意位置进入" : hintValue;

            SetAudioUrl(Resolve(Find(config.touchElements, "welcome.audio")?.assetUrl, resolver));
            SetLogo(Resolve(Find(config.touchElements, "welcome.logo")?.assetUrl, resolver));
            var welcomeBackground = Resolve(Find(config.touchElements, "welcome.background")?.assetUrl, resolver);
            if (string.IsNullOrWhiteSpace(welcomeBackground))
                welcomeBackground = Resolve(config.touchBackgroundUrl, resolver);
            SetBackground(welcomeBackground);
            if (Visible && !entering) ResetIdlePresentation();
        }

        public void Show()
        {
            StopPlayback();
            root.transform.SetAsLastSibling();
            root.SetActive(true);
            entering = false;
            enterButton.interactable = true;
            enterButton.gameObject.SetActive(true);
            skipButton.gameObject.SetActive(false);
            ResetIdlePresentation();
            BeginPreloadIfRequired();
            StartPulse();
        }

        public void Hide()
        {
            StopPlayback();
            StopPulse();
            entering = false;
            root.SetActive(false);
        }

        private void BeginWelcome()
        {
            if (entering) return;
            entering = true;
            enterButton.interactable = false;
            audioRoutine = host.StartCoroutine(BeginWelcomeRoutine());
        }

        private IEnumerator BeginWelcomeRoutine()
        {
            yield return PressFeedback();
            if (string.IsNullOrWhiteSpace(audioUrl))
            {
                actionLabel.text = "正在进入";
                actionHint.text = "欢迎参观";
                status.text = string.Empty;
                yield return new WaitForSecondsRealtime(.22f);
                audioRoutine = null;
                FinishEntry();
                yield break;
            }

            BeginPreloadIfRequired();
            actionLabel.text = "正在准备欢迎词";
            actionHint.text = "请稍候";
            while (preloadedClip == null && !audioLoadFailed)
            {
                status.text = $"欢迎词加载中 {Mathf.RoundToInt(Mathf.Clamp01(audioLoadProgress) * 100)}%";
                yield return null;
            }

            if (preloadedClip == null)
            {
                actionLabel.text = "欢迎词暂时无法播放";
                actionHint.text = "正在进入讲解首页";
                status.text = string.Empty;
                yield return new WaitForSecondsRealtime(.8f);
                audioRoutine = null;
                FinishEntry();
                yield break;
            }

            actionLabel.text = "欢迎词播放中";
            actionHint.text = "您可以随时跳过";
            status.text = string.Empty;
            skipButton.gameObject.SetActive(true);
            audioSource.clip = preloadedClip;
            audioSource.Play();
            while (audioSource.isPlaying) yield return null;
            audioRoutine = null;
            FinishEntry();
        }

        private IEnumerator PressFeedback()
        {
            const float duration = .18f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / duration);
                var scale = progress < .5f
                    ? Mathf.Lerp(1f, .91f, progress * 2f)
                    : Mathf.Lerp(.91f, 1f, (progress - .5f) * 2f);
                touchTarget.localScale = Vector3.one * scale;
                yield return null;
            }
            touchTarget.localScale = Vector3.one;
        }

        private void SetAudioUrl(string value)
        {
            if (string.Equals(audioUrl, value, StringComparison.OrdinalIgnoreCase)) return;
            StopPlayback();
            StopPreload();
            if (preloadedClip != null) UnityEngine.Object.Destroy(preloadedClip);
            preloadedClip = null;
            audioUrl = value;
            audioLoadProgress = 0;
            audioLoadFailed = false;
            BeginPreloadIfRequired();
        }

        private void BeginPreloadIfRequired()
        {
            if (string.IsNullOrWhiteSpace(audioUrl) || preloadedClip != null || preloadRoutine != null || audioLoadFailed)
                return;
            preloadRoutine = host.StartCoroutine(PreloadAudio(audioUrl));
        }

        private IEnumerator PreloadAudio(string requestedUrl)
        {
            using (var request = UnityWebRequestMultimedia.GetAudioClip(requestedUrl, AudioType.UNKNOWN))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (!string.Equals(audioUrl, requestedUrl, StringComparison.OrdinalIgnoreCase)) yield break;
                    audioLoadProgress = Mathf.Max(0, request.downloadProgress);
                    yield return null;
                }

                if (!string.Equals(audioUrl, requestedUrl, StringComparison.OrdinalIgnoreCase)) yield break;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    preloadedClip = DownloadHandlerAudioClip.GetContent(request);
                    audioLoadProgress = 1;
                    audioLoadFailed = preloadedClip == null;
                }
                else
                {
                    audioLoadFailed = true;
                    Debug.LogWarning("欢迎词音频预加载失败：" + request.error);
                }
            }
            preloadRoutine = null;
        }

        private void Complete()
        {
            if (audioRoutine != null)
            {
                var routine = audioRoutine;
                audioRoutine = null;
                host.StopCoroutine(routine);
            }
            FinishEntry();
        }

        private void FinishEntry()
        {
            Hide();
            Entered?.Invoke();
        }

        private void StopPlayback()
        {
            if (audioRoutine != null)
            {
                host.StopCoroutine(audioRoutine);
                audioRoutine = null;
            }
            if (audioSource.isPlaying) audioSource.Stop();
            audioSource.clip = null;
            touchTarget.localScale = Vector3.one;
        }

        private void StopPreload()
        {
            if (preloadRoutine == null) return;
            host.StopCoroutine(preloadRoutine);
            preloadRoutine = null;
        }

        private void ResetIdlePresentation()
        {
            actionLabel.text = actionText;
            actionHint.text = hintText;
            status.text = string.Empty;
        }

        private void SetBackground(string url)
        {
            if (string.Equals(loadedBackgroundUrl, url, StringComparison.OrdinalIgnoreCase)) return;
            loadedBackgroundUrl = url;
            ApplyFallbackBackground();
            if (string.IsNullOrWhiteSpace(url)) return;
            var requested = url;
            imageLoader.Load(background, url, success =>
            {
                if (!string.Equals(loadedBackgroundUrl, requested, StringComparison.OrdinalIgnoreCase)) return;
                if (!success)
                {
                    ApplyFallbackBackground();
                    Debug.LogWarning("欢迎页背景加载失败，继续使用内置科技背景。");
                }
            });
        }

        private void ApplyFallbackBackground()
        {
            background.sprite = fallbackBackground;
            background.color = fallbackBackground == null ? new Color(.02f, .08f, .16f, 1) : Color.white;
        }

        private void SetLogo(string url)
        {
            if (string.Equals(loadedLogoUrl, url, StringComparison.OrdinalIgnoreCase)) return;
            loadedLogoUrl = url;
            brandLogo.sprite = null;
            brandLogo.gameObject.SetActive(false);
            if (string.IsNullOrWhiteSpace(url)) return;
            var requested = url;
            imageLoader.Load(brandLogo, url, success =>
            {
                if (!string.Equals(loadedLogoUrl, requested, StringComparison.OrdinalIgnoreCase)) return;
                brandLogo.preserveAspect = true;
                brandLogo.gameObject.SetActive(success);
                if (!success) Debug.LogWarning("欢迎页品牌标志加载失败，继续使用文字品牌。");
            });
        }

        private void BuildRings(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            var sizes = new[] { 520f, 444f, 374f };
            for (var i = 0; i < sizes.Length; i++)
            {
                var color = theme.ConfigurableAccent;
                color.a = .28f - i * .045f;
                var ring = factory.Image("Touch Pulse Ring " + i, parent, color);
                ring.sprite = GetRingSprite();
                ring.type = Image.Type.Simple;
                var size = sizes[i];
                TouchUiFactory.Anchor(ring.rectTransform, .5f, .5f, .5f, .5f,
                    -size / 2f, -size / 2f - 70, size / 2f, size / 2f - 70);
                ring.raycastTarget = false;
                pulseRings.Add(ring);
            }
        }

        private void StartPulse()
        {
            StopPulse();
            pulseRoutine = host.StartCoroutine(PulseRings());
        }

        private void StopPulse()
        {
            if (pulseRoutine != null)
            {
                host.StopCoroutine(pulseRoutine);
                pulseRoutine = null;
            }
            foreach (var ring in pulseRings)
            {
                if (ring == null) continue;
                ring.rectTransform.localScale = Vector3.one;
            }
        }

        private IEnumerator PulseRings()
        {
            while (root.activeSelf)
            {
                for (var i = 0; i < pulseRings.Count; i++)
                {
                    var ring = pulseRings[i];
                    if (ring == null) continue;
                    var phase = Mathf.Repeat(Time.unscaledTime * .28f + i * .28f, 1f);
                    ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(.9f, 1.16f, phase);
                    var color = ring.color;
                    color.a = Mathf.Lerp(.34f, .025f, phase);
                    ring.color = color;
                }
                if (!entering)
                    touchTarget.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 2.2f) * .012f);
                yield return null;
            }
            pulseRoutine = null;
        }

        private static void ConfigureBestFit(Text text, int minimum, int maximum)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = minimum;
            text.resizeTextMaxSize = maximum;
        }

        private static Sprite GetCircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            circleSprite = CreateRadialSprite("TG Welcome Touch Circle", false);
            return circleSprite;
        }

        private static Sprite GetRingSprite()
        {
            if (ringSprite != null) return ringSprite;
            ringSprite = CreateRadialSprite("TG Welcome Pulse Ring", true);
            return ringSprite;
        }

        private static Sprite CreateRadialSprite(string name, bool ring)
        {
            const int size = 256;
            const float radius = 122f;
            const float halfThickness = 2.8f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[size * size];
            var center = (size - 1) * .5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                var alpha = ring
                    ? Mathf.Clamp01(halfThickness + .8f - Mathf.Abs(distance - radius))
                    : Mathf.Clamp01(radius + .6f - distance);
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100,
                0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static UiElementOverride Find(UiElementOverride[] items, string key) => items?.FirstOrDefault(item =>
            item != null && string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase));
        private static string Resolve(string value, Func<string, string> resolver) =>
            string.IsNullOrWhiteSpace(value) ? null : resolver?.Invoke(value) ?? value;
    }
}
