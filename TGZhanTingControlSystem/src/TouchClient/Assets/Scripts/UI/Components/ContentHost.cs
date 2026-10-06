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
            frame = factory.RoundedImage("Content Host Frame", parent,
                new Color(theme.HeaderBackground.r, theme.HeaderBackground.g, theme.HeaderBackground.b, .92f));
            surface = factory.RoundedImage("Content Host Surface", frame.transform,
                new Color(theme.SurfaceGlass.r, theme.SurfaceGlass.g, theme.SurfaceGlass.b, .08f));
            TouchUiFactory.Stretch(surface.rectTransform, 1, 1, -1, -1);
            contentRoot = factory.Rect("Page Host", surface.transform);
            TouchUiFactory.Stretch(contentRoot, theme.Space12, theme.Space12,
                -theme.Space12, -theme.Space12);
        }

        public void RefreshTheme(TouchTheme theme)
        {
            frame.color = new Color(theme.HeaderBackground.r, theme.HeaderBackground.g,
                theme.HeaderBackground.b, .92f);
            surface.color = new Color(theme.SurfaceGlass.r, theme.SurfaceGlass.g, theme.SurfaceGlass.b, .08f);
        }
    }
}
