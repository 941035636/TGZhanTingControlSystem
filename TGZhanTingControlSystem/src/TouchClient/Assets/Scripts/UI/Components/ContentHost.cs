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
        private readonly Sprite frameDefaultSprite;
        private readonly Sprite surfaceDefaultSprite;
        private readonly Image.Type frameDefaultType;
        private readonly Image.Type surfaceDefaultType;
        private readonly RectTransform contentRoot;
        private bool homeDarkMode;

        public RectTransform Root => frame.rectTransform;
        public RectTransform ContentRoot => contentRoot;

        public ContentHost(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            frame = factory.RoundedImage("Content Host Frame", parent, theme.Border);
            surface = factory.RoundedImage("Content Host Surface", frame.transform, theme.Surface);
            frame.raycastTarget = false;
            surface.raycastTarget = false;
            frameDefaultSprite = frame.sprite;
            surfaceDefaultSprite = surface.sprite;
            frameDefaultType = frame.type;
            surfaceDefaultType = surface.type;
            TouchUiFactory.Stretch(surface.rectTransform, 1, 1, -1, -1);
            contentRoot = factory.Rect("Page Host", surface.transform);
            TouchUiFactory.Stretch(contentRoot, theme.Space12, theme.Space12,
                -theme.Space12, -theme.Space12);
        }

        public void RefreshTheme(TouchTheme theme)
        {
            if (homeDarkMode)
            {
                HomeDarkVisualAssets.ApplySliced(frame, HomeDarkVisualAssets.ContentGlass,
                    theme.HomeGlassContentTint);
                surface.color = Color.clear;
            }
            else
            {
                frame.sprite = frameDefaultSprite;
                frame.type = frameDefaultType;
                frame.color = theme.Border;
                surface.sprite = surfaceDefaultSprite;
                surface.type = surfaceDefaultType;
                surface.color = theme.Surface;
            }
        }

        public void SetHomeDarkMode(bool enabled, TouchTheme theme)
        {
            if (homeDarkMode == enabled) return;
            homeDarkMode = enabled;
            RefreshTheme(theme);
        }
    }
}
