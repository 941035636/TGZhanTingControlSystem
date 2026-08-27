using TG.Control.Touch.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    public enum StatusTone { Neutral, Info, Success, Warning, Error }

    /// <summary>Small global-status component. It only renders caller-provided truthful state.</summary>
    public sealed class StatusBadge
    {
        private readonly TouchTheme theme;
        private readonly Image background;
        private readonly Image indicator;
        private readonly Text label;
        private StatusTone tone;

        public RectTransform Root => background.rectTransform;

        public StatusBadge(TouchUiFactory factory, TouchTheme theme, Transform parent, string name)
        {
            this.theme = theme;
            background = factory.Image(name, parent, theme.SurfaceElevated);
            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = theme.StatusBadgeHeight;
            layout.minWidth = 156;

            indicator = factory.Image("State Indicator", background.transform, theme.TextSecondary);
            TouchUiFactory.Anchor(indicator.rectTransform, 0, .5f, 0, .5f,
                theme.CardSpacing, -5, theme.CardSpacing + 10, 5);
            label = factory.Label("State Label", background.transform, string.Empty, theme.Caption,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Stretch(label.rectTransform, theme.CardSpacing + 20, 0, -theme.CardSpacing, 0);
        }

        public void Set(string text, StatusTone value)
        {
            tone = value;
            label.text = text ?? string.Empty;
            var stateColor = ToneColor(tone);
            indicator.color = stateColor;
            background.color = Color.Lerp(theme.SurfaceElevated, stateColor, .12f);
        }

        public void RefreshTheme() => Set(label.text, tone);

        private Color ToneColor(StatusTone value)
        {
            switch (value)
            {
                case StatusTone.Info: return theme.Primary;
                case StatusTone.Success: return theme.Success;
                case StatusTone.Warning: return theme.Warning;
                case StatusTone.Error: return theme.Error;
                default: return theme.TextSecondary;
            }
        }
    }
}
