using System;
using TG.Control.Touch.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>Branding, local time and truthful global runtime state.</summary>
    public sealed class TopBar
    {
        private readonly TouchTheme theme;
        private readonly Image root;
        private readonly Image brandMark;
        private readonly Text title;
        private readonly Text subtitle;
        private readonly Text dateLabel;
        private readonly Text timeLabel;
        private readonly Image timeSurface;
        private readonly StatusBadge connectionBadge;
        private readonly StatusBadge readinessBadge;
        private readonly Button exitButton;
        private int renderedSecond = -1;

        public RectTransform Root => root.rectTransform;
        public event Action ExitRequested;

        public TopBar(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            this.theme = theme;
            root = factory.Image("Top Bar", parent, Color.clear);
            root.raycastTarget = false;
            var signalLine = factory.Image("Top Bar Signal Line", root.transform, theme.Primary);
            TouchUiFactory.Anchor(signalLine.rectTransform, 0, 0, 0, 0, 36, 10, 184, 12);
            signalLine.raycastTarget = false;

            brandMark = factory.Image("Brand Light Mark", root.transform, theme.ShellHighlight);
            TouchUiFactory.Anchor(brandMark.rectTransform, 0, 0, 0, 1, 36, 28, 40, -28);
            brandMark.raycastTarget = false;

            title = factory.Label("Product Name", root.transform, "展厅自动讲解系统", 27,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, .46f, .5f, 1,
                58, 0, 0, -theme.CardSpacing / 2);
            subtitle = factory.Label("Product Subtitle", root.transform, "智慧展陈 · 中控终端",
                16, FontStyle.Normal, theme.TextSecondary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(subtitle.rectTransform, 0, 0, .5f, .5f,
                58, theme.CardSpacing / 2, 0, 0);

            timeSurface = factory.Image("Local Time Display", root.transform, Color.clear);
            TouchUiFactory.Anchor(timeSurface.rectTransform, 1, .5f, 1, .5f, -768, -24, -574, 24);
            timeSurface.raycastTarget = false;
            dateLabel = factory.Label("Current Date", timeSurface.transform, string.Empty, theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(dateLabel.rectTransform, 0, .5f, 1, 1, 0, 0, 0, 0);
            timeLabel = factory.Label("Current Time", timeSurface.transform, string.Empty, theme.Body,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(timeLabel.rectTransform, 0, 0, 1, .54f, 0, 0, 0, 0);

            var statusDivider = factory.Image("Top Bar Status Divider", root.transform,
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
            if (!string.IsNullOrWhiteSpace(productName)) title.text = productName;
            if (!string.IsNullOrWhiteSpace(productSubtitle))
                subtitle.text = productSubtitle.StartsWith("TG EXHIBITION", StringComparison.OrdinalIgnoreCase)
                    ? "智慧展陈 · 中控终端" : productSubtitle;
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
            brandMark.color = theme.ShellHighlight;
            timeSurface.color = Color.clear;
            exitButton.GetComponent<Image>().color = theme.Surface;
            connectionBadge.RefreshTheme();
            readinessBadge.RefreshTheme();
        }
    }
}
