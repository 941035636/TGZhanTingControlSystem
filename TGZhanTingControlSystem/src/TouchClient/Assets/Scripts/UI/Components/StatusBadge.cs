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
            background = factory.RoundedImage(name, parent, theme.ShellPanel);
            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = theme.StatusBadgeHeight;
            layout.minWidth = 168;

            var rail = factory.Image("Badge Electric Blue Rail", background.transform,
                new Color(theme.ShellHighlight.r, theme.ShellHighlight.g, theme.ShellHighlight.b, .52f));
            TouchUiFactory.Anchor(rail.rectTransform, 0, 1, 1, 1, theme.Space12, -2,
                -theme.Space12, 0);
            rail.raycastTarget = false;

            indicator = factory.RoundedImage("State Indicator", background.transform, theme.TextSecondary);
            TouchUiFactory.Anchor(indicator.rectTransform, 0, .5f, 0, .5f,
                theme.Space16, -4, theme.Space16 + 8, 4);
            label = factory.Label("State Label", background.transform, string.Empty, theme.Caption,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Stretch(label.rectTransform, theme.Space16 + 18, 0, -theme.Space16, 0);
        }

        public void Set(string text, StatusTone value)
        {
            tone = value;
            label.text = text ?? string.Empty;
            var stateColor = ToneColor(tone);
            indicator.color = stateColor;
            // Keep the shell's royal-blue surface consistent; only the indicator carries
            // the semantic online/warning/error color.
            background.color = theme.ShellPanel;
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
