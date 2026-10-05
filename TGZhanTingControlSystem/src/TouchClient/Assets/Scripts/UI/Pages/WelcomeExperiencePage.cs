using System;
using System.Collections;
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
        private readonly MonoBehaviour host;
        private readonly TouchImageLoader imageLoader;
        private readonly GameObject root;
        private readonly Image background;
        private readonly Text title;
        private readonly Text subtitle;
        private readonly Text status;
        private readonly Button enterButton;
        private readonly Button skipButton;
        private readonly AudioSource audioSource;
        private string audioUrl;
        private string loadedBackgroundUrl;
        private Coroutine audioRoutine;

        public bool Visible => root.activeSelf;
        public event Action Entered;

        public WelcomeExperiencePage(MonoBehaviour host, TouchUiFactory factory, TouchTheme theme,
            TouchImageLoader imageLoader, Transform canvas)
        {
            this.host = host;
            this.imageLoader = imageLoader;
            background = factory.Image("Welcome Experience", canvas, theme.AppBackground);
            TouchUiFactory.Stretch(background.rectTransform);
            root = background.gameObject;
            audioSource = root.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;

            var embedded = Resources.Load<Texture2D>("Touch/touch-technology-background");
            if (embedded != null)
            {
                background.sprite = Sprite.Create(embedded, new Rect(0, 0, embedded.width, embedded.height),
                    new Vector2(.5f, .5f), 100);
                background.color = Color.white;
            }
            var veil = factory.Image("Welcome Contrast Veil", root.transform, new Color(.01f, .04f, .12f, .36f));
            TouchUiFactory.Stretch(veil.rectTransform);
            veil.raycastTarget = false;

            var eyebrow = factory.Label("Welcome Eyebrow", root.transform, "石横特钢 · 智慧展厅", 20,
                FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(eyebrow.rectTransform, .2f, 1, .8f, 1, 0, -214, 0, -174);
            title = factory.Label("Welcome Title", root.transform, "欢迎开启自动讲解之旅", 50,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(title.rectTransform, .08f, 1, .92f, 1, 0, -306, 0, -214);
            subtitle = factory.Label("Welcome Subtitle", root.transform,
                "触碰下方光环，开启钢铁与科技交融的探索之旅", 22,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(subtitle.rectTransform, .14f, 1, .86f, 1, 0, -366, 0, -310);

            BuildRings(factory, theme, root.transform);
            enterButton = factory.TouchButton(root.transform, "触碰开启", true, BeginWelcome);
            TouchUiFactory.Anchor(enterButton.GetComponent<RectTransform>(), .5f, .5f, .5f, .5f,
                -132, -54, 132, 24);
            status = factory.Label("Welcome Status", root.transform, "请将手掌轻触屏幕", theme.Secondary,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(status.rectTransform, .25f, .5f, .75f, .5f, 0, -108, 0, -66);
            skipButton = factory.TouchButton(root.transform, "跳过欢迎词", false, Complete);
            TouchUiFactory.Anchor(skipButton.GetComponent<RectTransform>(), .5f, 0, .5f, 0,
                -118, 48, 118, 108);
            skipButton.gameObject.SetActive(false);
        }

        public void Configure(UiExperienceConfig config, Func<string, string> resolver)
        {
            if (config == null) return;
            var titleValue = Find(config.touchElements, "welcome.title")?.text;
            var subtitleValue = Find(config.touchElements, "welcome.subtitle")?.text;
            title.text = string.IsNullOrWhiteSpace(titleValue) ? "欢迎开启自动讲解之旅" : titleValue;
            subtitle.text = string.IsNullOrWhiteSpace(subtitleValue)
                ? "触碰下方光环，开启钢铁与科技交融的探索之旅" : subtitleValue;
            audioUrl = Resolve(Find(config.touchElements, "welcome.audio")?.assetUrl, resolver);
            var welcomeBackground = Resolve(Find(config.touchElements, "welcome.background")?.assetUrl, resolver);
            if (string.IsNullOrWhiteSpace(welcomeBackground)) welcomeBackground = Resolve(config.touchBackgroundUrl, resolver);
            SetBackground(welcomeBackground);
        }

        public void Show()
        {
            StopAudio();
            root.transform.SetAsLastSibling();
            root.SetActive(true);
            enterButton.gameObject.SetActive(true);
            skipButton.gameObject.SetActive(false);
            status.text = "请将手掌轻触屏幕";
        }

        public void Hide()
        {
            StopAudio();
            root.SetActive(false);
        }

        private void BeginWelcome()
        {
            enterButton.gameObject.SetActive(false);
            skipButton.gameObject.SetActive(true);
            if (string.IsNullOrWhiteSpace(audioUrl))
            {
                status.text = "欢迎进入石横特钢智慧展厅";
                Complete();
                return;
            }
            status.text = "正在播放欢迎词…";
            audioRoutine = host.StartCoroutine(PlayAudio());
        }

        private IEnumerator PlayAudio()
        {
            using (var request = UnityWebRequestMultimedia.GetAudioClip(audioUrl, AudioType.UNKNOWN))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    audioSource.clip = DownloadHandlerAudioClip.GetContent(request);
                    audioSource.Play();
                    while (audioSource.isPlaying) yield return null;
                }
                else
                {
                    Debug.LogWarning("欢迎词音频加载失败：" + request.error);
                }
            }
            audioRoutine = null;
            Complete();
        }

        private void Complete()
        {
            Hide();
            Entered?.Invoke();
        }

        private void StopAudio()
        {
            if (audioRoutine != null)
            {
                host.StopCoroutine(audioRoutine);
                audioRoutine = null;
            }
            if (audioSource.isPlaying) audioSource.Stop();
            audioSource.clip = null;
        }

        private void SetBackground(string url)
        {
            if (string.Equals(loadedBackgroundUrl, url, StringComparison.OrdinalIgnoreCase)) return;
            loadedBackgroundUrl = url;
            if (string.IsNullOrWhiteSpace(url)) return;
            var requested = url;
            imageLoader.Load(background, url, success =>
            {
                if (!string.Equals(loadedBackgroundUrl, requested, StringComparison.OrdinalIgnoreCase)) return;
                if (!success) Debug.LogWarning("欢迎页背景加载失败，继续使用内置科技背景。");
            });
        }

        private static void BuildRings(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            for (var i = 0; i < 4; i++)
            {
                var color = theme.ConfigurableAccent;
                color.a = .11f + i * .045f;
                var size = 390 - i * 52;
                var ring = factory.RoundedImage("Touch Energy Ring " + i, parent, color);
                TouchUiFactory.Anchor(ring.rectTransform, .5f, .5f, .5f, .5f,
                    -size / 2f, -size / 2f - 18, size / 2f, size / 2f - 18);
                ring.raycastTarget = false;
            }
        }

        private static UiElementOverride Find(UiElementOverride[] items, string key) => items?.FirstOrDefault(item =>
            item != null && string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase));
        private static string Resolve(string value, Func<string, string> resolver) =>
            string.IsNullOrWhiteSpace(value) ? null : resolver?.Invoke(value) ?? value;
    }
}
