using System;
using TG.Control.Touch.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>Pure presentation card for a single truthful system-health domain.</summary>
    public sealed class SystemHealthCard
    {
        private readonly TouchTheme theme;
        private readonly Image frame;
        private readonly Image surface;
        private readonly Image semanticAccent;
        private readonly StatusBadge badge;
        private readonly Text status;
        private readonly Text detail;
        private readonly Text primaryFact;
        private readonly Text secondaryFact;
        private StatusTone tone;

        public RectTransform Root => frame.rectTransform;

        public SystemHealthCard(TouchUiFactory factory, TouchTheme theme, Transform parent,
            string title, string number)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));

            frame = factory.RoundedImage("System Health - " + title, parent, theme.Border);
            surface = factory.RoundedImage("Card Surface", frame.transform, theme.SurfaceElevated);
            TouchUiFactory.Stretch(surface.rectTransform, 1, 1, -1, -1);

            semanticAccent = factory.Image("Semantic Accent", surface.transform, theme.TextSecondary);
            TouchUiFactory.Anchor(semanticAccent.rectTransform, 0, 0, 0, 1, 0, 0, 5, 0);

            var index = factory.Label("Card Index", surface.transform, number, theme.Caption,
                FontStyle.Bold, theme.Primary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(index.rectTransform, 0, 1, 0, 1,
                theme.PanelPadding, -50, theme.PanelPadding + 34, -18);
            var heading = factory.Label("Card Title", surface.transform, title, theme.CardTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(heading.rectTransform, 0, 1, .55f, 1,
                theme.PanelPadding + 42, -56, 0, -14);

            badge = new StatusBadge(factory, theme, surface.transform, title + " Badge");
            TouchUiFactory.Anchor(badge.Root, 1, 1, 1, 1, -190, -62, -theme.PanelPadding, -18);

            status = factory.Label("Operator Status", surface.transform, "状态检查中", theme.H2,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(status.rectTransform, 0, 1, 1, 1,
                theme.PanelPadding, -112, -theme.PanelPadding, -66);
            status.resizeTextForBestFit = true;
            status.resizeTextMinSize = theme.CardTitle;
            status.resizeTextMaxSize = theme.H2;

            detail = factory.Label("Operator Guidance", surface.transform, string.Empty, theme.Body,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(detail.rectTransform, 0, 1, 1, 1,
                theme.PanelPadding, -174, -theme.PanelPadding, -116);

            var divider = factory.Image("Fact Divider", surface.transform, theme.Border);
            TouchUiFactory.Anchor(divider.rectTransform, 0, 0, 1, 0,
                theme.PanelPadding, 58, -theme.PanelPadding, 60);
            primaryFact = factory.Label("Primary Fact", surface.transform, string.Empty, theme.Caption,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(primaryFact.rectTransform, 0, 0, .55f, 0,
                theme.PanelPadding, 14, 0, 52);
            secondaryFact = factory.Label("Secondary Fact", surface.transform, string.Empty, theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(secondaryFact.rectTransform, .45f, 0, 1, 0,
                0, 14, -theme.PanelPadding, 52);
        }

        public void Render(string statusText, string detailText, StatusTone value,
            string primaryFactText, string secondaryFactText)
        {
            tone = value;
            status.text = statusText ?? string.Empty;
            detail.text = detailText ?? string.Empty;
            primaryFact.text = primaryFactText ?? string.Empty;
            secondaryFact.text = secondaryFactText ?? string.Empty;
            badge.Set(ToneLabel(value), value);
            semanticAccent.color = ToneColor(value);
        }

        public void RefreshTheme()
        {
            frame.color = theme.Border;
            surface.color = theme.SurfaceElevated;
            badge.RefreshTheme();
            semanticAccent.color = ToneColor(tone);
        }

        private string ToneLabel(StatusTone value)
        {
            switch (value)
            {
                case StatusTone.Success: return "正常";
                case StatusTone.Warning: return "请留意";
                case StatusTone.Error: return "需处理";
                case StatusTone.Info: return "运行中";
                default: return "待确认";
            }
        }

        private Color ToneColor(StatusTone value)
        {
            switch (value)
            {
                case StatusTone.Success: return theme.Success;
                case StatusTone.Warning: return theme.Warning;
                case StatusTone.Error: return theme.Error;
                case StatusTone.Info: return theme.Primary;
                default: return theme.TextSecondary;
            }
        }
    }
}
