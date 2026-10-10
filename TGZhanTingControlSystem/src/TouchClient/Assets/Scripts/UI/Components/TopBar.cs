using System;
using TG.Control.Touch.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>Branding, local time and truthful global runtime state.</summary>
    public sealed class TopBar
    {
        private static Sprite brandCircle;
        private readonly TouchTheme theme;
        private readonly Image root;
        private readonly Image signalLine;
        private readonly Image brandMark;
        private readonly Text title;
        private readonly Text subtitle;
        private readonly Text dateLabel;
        private readonly Text timeLabel;
        private readonly Image timeSurface;
        private readonly Image statusDivider;
        private readonly StatusBadge connectionBadge;
        private readonly StatusBadge readinessBadge;
        private readonly Button exitButton;
        private bool homeDarkMode;
        private int renderedSecond = -1;

        public RectTransform Root => root.rectTransform;
        public event Action ExitRequested;

        public TopBar(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            this.theme = theme;
            root = factory.Image("Top Bar", parent, Color.clear);
            root.raycastTarget = false;
            signalLine = factory.Image("Top Bar Signal Line", root.transform, theme.Primary);
            TouchUiFactory.Anchor(signalLine.rectTransform, 0, 0, 0, 0, 36, 10, 184, 12);
            signalLine.raycastTarget = false;

            brandMark = factory.Image("TG Brand Circle", root.transform, theme.ShellHighlight);
            brandMark.sprite = brandCircle ?? (brandCircle = CreateBrandCircle());
            TouchUiFactory.Anchor(brandMark.rectTransform, 0, .5f, 0, .5f, 32, -28, 88, 28);
            brandMark.raycastTarget = false;
            var brandLetters = factory.Label("TG Brand Letters", brandMark.transform, "TG", 20,
                FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(brandLetters.rectTransform);

            title = factory.Label("Product Name", root.transform, "展厅自动讲解系统", 24,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, .5f, 0, .5f, 104, 0, 430, 31);
            subtitle = factory.Label("Product Subtitle", root.transform, "智慧展厅 · 中控终端",
                14, FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(subtitle.rectTransform, 0, .5f, 0, .5f, 104, -29, 430, -1);

            timeSurface = factory.Image("Local Time Display", root.transform, Color.clear);
            TouchUiFactory.Anchor(timeSurface.rectTransform, 1, .5f, 1, .5f, -768, -24, -574, 24);
            timeSurface.raycastTarget = false;
            dateLabel = factory.Label("Current Date", timeSurface.transform, string.Empty, theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(dateLabel.rectTransform, 0, .5f, 1, 1, 0, 0, 0, 0);
            timeLabel = factory.Label("Current Time", timeSurface.transform, string.Empty, theme.Body,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(timeLabel.rectTransform, 0, 0, 1, .54f, 0, 0, 0, 0);

            statusDivider = factory.Image("Top Bar Status Divider", root.transform,
                new Color(theme.ShellBorder.r, theme.ShellBorder.g, theme.ShellBorder.b, .5f));
            TouchUiFactory.Anchor(statusDivider.rectTransform, 1, .5f, 1, .5f,
                -565, -22, -563, 22);
            statusDivider.raycastTarget = false;

            connectionBadge = new StatusBadge(factory, theme, root.transform, "Server Status");
            TouchUiFactory.Anchor(connectionBadge.Root, 1, .5f, 1, .5f, -554, -22, -362, 22);
            readinessBadge = new StatusBadge(factory, theme, root.transform, "Reception Status");
            TouchUiFactory.Anchor(readinessBadge.Root, 1, .5f, 1, .5f, -346, -22, -154, 22);
            exitButton = factory.TouchButton(root.transform, "退出", false,
                () => ExitRequested?.Invoke());
            TouchUiFactory.Anchor(exitButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -138, -22, -theme.PagePadding, 22);
            exitButton.GetComponent<Image>().color = theme.Surface;
            connectionBadge.Set("服务连接中", StatusTone.Warning);
            readinessBadge.Set("状态检查中", StatusTone.Neutral);
            Tick(DateTime.Now, true);
        }

        public void SetBranding(string productName, string productSubtitle)
        {
            // The shared shell keeps the same independently rendered brand as the standby page.
            title.text = "展厅自动讲解系统";
            subtitle.text = "智慧展厅 · 中控终端";
        }

        private static Sprite CreateBrandCircle()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TG Top Brand Circle",
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x + .5f, y + .5f),
                    new Vector2(size / 2f, size / 2f));
                texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(31f - distance)));
            }
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        public void SetConnection(bool connected) =>
            connectionBadge.Set(connected ? "服务在线" : "服务重连中",
                connected ? StatusTone.Success : StatusTone.Error);

        public void SetReadiness(string text, StatusTone tone) => readinessBadge.Set(text, tone);

        public void Tick(DateTime now, bool force = false)
        {
            if (!force && renderedSecond == now.Second) return;
            renderedSecond = now.Second;
            dateLabel.text = now.ToString("yyyy-MM-dd");
            timeLabel.text = now.ToString("HH:mm:ss");
        }

        public void RefreshTheme()
        {
            root.color = Color.clear;
            signalLine.color = homeDarkMode ? theme.HomeDarkAccent : theme.Primary;
            brandMark.color = homeDarkMode ? theme.HomeDarkAccent : theme.ShellHighlight;
            title.color = homeDarkMode ? theme.HomeDarkTextPrimary : theme.TextPrimary;
            subtitle.color = homeDarkMode ? theme.HomeDarkTextSecondary : theme.TextSecondary;
            dateLabel.color = homeDarkMode ? theme.HomeDarkTextSecondary : theme.TextSecondary;
            timeLabel.color = homeDarkMode ? theme.HomeDarkTextPrimary : theme.TextPrimary;
            timeSurface.color = Color.clear;
            statusDivider.color = homeDarkMode
                ? new Color(theme.HomeDarkBorder.r, theme.HomeDarkBorder.g, theme.HomeDarkBorder.b, .38f)
                : new Color(theme.ShellBorder.r, theme.ShellBorder.g, theme.ShellBorder.b, .5f);
            exitButton.GetComponent<Image>().color = homeDarkMode ? theme.HomeDarkGlass : theme.Surface;
            var exitLabel = exitButton.GetComponentInChildren<Text>();
            if (exitLabel != null) exitLabel.color = homeDarkMode ? theme.HomeDarkTextPrimary : theme.TextPrimary;
            connectionBadge.RefreshTheme();
            readinessBadge.RefreshTheme();
        }

        public void SetHomeDarkMode(bool enabled)
        {
            if (homeDarkMode == enabled) return;
            homeDarkMode = enabled;
            connectionBadge.SetDarkMode(enabled);
            readinessBadge.SetDarkMode(enabled);
            RefreshTheme();
        }
    }
}
