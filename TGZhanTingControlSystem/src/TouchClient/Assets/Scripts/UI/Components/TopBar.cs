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
        private readonly StatusBadge connectionBadge;
        private readonly StatusBadge readinessBadge;
        private int renderedSecond = -1;

        public RectTransform Root => root.rectTransform;

        public TopBar(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            this.theme = theme;
            root = factory.Image("Top Bar", parent, theme.HeaderBackground);
            var divider = factory.Image("Top Bar Border", root.transform, theme.Border);
            TouchUiFactory.Anchor(divider.rectTransform, 0, 0, 1, 0, 0, 0, 0, 2);

            brandMark = factory.Image("Brand Mark", root.transform, theme.Primary);
            TouchUiFactory.Anchor(brandMark.rectTransform, 0, .5f, 0, .5f,
                theme.PagePadding, -26, theme.PagePadding + 52, 26);
            var brandLetters = factory.Label("Brand Letters", brandMark.transform, "TG", theme.CardTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(brandLetters.rectTransform);

            title = factory.Label("Product Name", root.transform, "展厅自动讲解系统", theme.H2,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, .47f, .5f, 1,
                theme.PagePadding + 72, 0, 0, -theme.CardSpacing / 2);
            subtitle = factory.Label("Product Subtitle", root.transform, "TG EXHIBITION · 智慧展陈中控终端",
                theme.Caption, FontStyle.Normal, theme.TextSecondary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(subtitle.rectTransform, 0, 0, .5f, .5f,
                theme.PagePadding + 72, theme.CardSpacing / 2, 0, 0);

            dateLabel = factory.Label("Current Date", root.transform, string.Empty, theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(dateLabel.rectTransform, 1, 0, 1, 1, -690, 0, -585, 0);
            timeLabel = factory.Label("Current Time", root.transform, string.Empty, theme.Body,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(timeLabel.rectTransform, 1, 0, 1, 1, -570, 0, -450, 0);

            connectionBadge = new StatusBadge(factory, theme, root.transform, "Server Status");
            TouchUiFactory.Anchor(connectionBadge.Root, 1, .5f, 1, .5f, -430, -22, -242, 22);
            readinessBadge = new StatusBadge(factory, theme, root.transform, "Reception Status");
            TouchUiFactory.Anchor(readinessBadge.Root, 1, .5f, 1, .5f, -226, -22, -theme.PagePadding, 22);
            connectionBadge.Set("服务连接中", StatusTone.Warning);
            readinessBadge.Set("状态检查中", StatusTone.Neutral);
            Tick(DateTime.Now, true);
        }

        public void SetBranding(string productName, string productSubtitle)
        {
            if (!string.IsNullOrWhiteSpace(productName)) title.text = productName;
            if (!string.IsNullOrWhiteSpace(productSubtitle)) subtitle.text = productSubtitle;
        }

        public void SetConnection(bool connected) =>
            connectionBadge.Set(connected ? "Server 在线" : "Server 重连中",
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
            brandMark.color = theme.Primary;
            connectionBadge.RefreshTheme();
            readinessBadge.RefreshTheme();
        }
    }
}
