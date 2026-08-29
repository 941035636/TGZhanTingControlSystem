using TG.Control.Touch.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>Stable mounting point for legacy pages now and dedicated page classes later.</summary>
    public sealed class ContentHost
    {
        private readonly Image frame;
        private readonly Image surface;
        private readonly RectTransform contentRoot;

        public RectTransform Root => frame.rectTransform;
        public RectTransform ContentRoot => contentRoot;

        public ContentHost(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            frame = factory.RoundedImage("Content Host Frame", parent, theme.Border);
            surface = factory.RoundedImage("Content Host Surface", frame.transform, theme.Surface);
            TouchUiFactory.Stretch(surface.rectTransform, 1, 1, -1, -1);
            contentRoot = factory.Rect("Page Host", surface.transform);
            TouchUiFactory.Stretch(contentRoot, theme.CardSpacing, theme.CardSpacing,
                -theme.CardSpacing, -theme.CardSpacing);
        }

        public void RefreshTheme(TouchTheme theme)
        {
            frame.color = theme.Border;
            surface.color = theme.Surface;
        }
    }
}
