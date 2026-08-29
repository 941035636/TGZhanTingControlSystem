using TG.Control.Touch.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>Non-blocking reception error with operator-facing language.</summary>
    public sealed class ErrorBanner
    {
        private readonly TouchTheme theme;
        private readonly Image root;
        private readonly Image indicator;
        private readonly Text message;

        public RectTransform Root => root.rectTransform;

        public ErrorBanner(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            this.theme = theme;
            root = factory.RoundedImage("Error Banner", parent,
                Color.Lerp(theme.SurfaceElevated, theme.Error, .18f));
            root.raycastTarget = false;
            indicator = factory.Image("Error Indicator", root.transform, theme.Error);
            TouchUiFactory.Anchor(indicator.rectTransform, 0, 0, 0, 1, 0, 0, 6, 0);
            message = factory.Label("Error Message", root.transform, string.Empty, theme.Secondary,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Stretch(message.rectTransform, theme.Space24, theme.Space8,
                -theme.Space24, -theme.Space8);
            message.resizeTextForBestFit = true;
            message.resizeTextMinSize = theme.Caption;
            message.resizeTextMaxSize = theme.Secondary;
            root.gameObject.SetActive(false);
        }

        public void Show(string value)
        {
            message.text = string.IsNullOrWhiteSpace(value) ? "操作未完成，请稍后重试。" : value;
            root.gameObject.SetActive(true);
        }

        public void Hide() => root.gameObject.SetActive(false);

        public void RefreshTheme()
        {
            root.color = Color.Lerp(theme.SurfaceElevated, theme.Error, .18f);
            indicator.color = theme.Error;
        }
    }
}
