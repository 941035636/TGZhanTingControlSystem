using UnityEngine;

namespace TG.Control.Touch.UI.Theme
{
    /// <summary>Central color and typography tokens matching the existing TouchClient visuals.</summary>
    public sealed class TouchTheme
    {
        private static readonly string[] FontFamilies = { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" };

        public Color Ink { get; } = ParseColor("#19372E");
        public Color Muted { get; } = ParseColor("#70827B");
        public Color Gold { get; } = ParseColor("#BE974F");
        public Color Accent { get; private set; } = ParseColor("#1C5B46");
        public Color SecondaryButton { get; } = ParseColor("#EDF1EF");
        public Color PrimaryHighlight { get; } = ParseColor("#26775A");
        public Color SecondaryHighlight { get; } = ParseColor("#E2E9E5");
        public Color PrimaryPressed { get; } = ParseColor("#174C3A");
        public Color SecondaryPressed { get; } = ParseColor("#D7E1DC");
        public Color InputBackground { get; } = ParseColor("#F5F7F6");
        public Color Divider { get; } = ParseColor("#DEE5E1");

        public static TouchTheme CreateDefault() => new TouchTheme();
        public Font CreateFont(int size = 32) => Font.CreateDynamicFontFromOSFont(FontFamilies, size);
        public void SetAccent(Color color) => Accent = color;

        public static Color ParseColor(string value)
        {
            ColorUtility.TryParseHtmlString(value, out var color);
            return color;
        }
    }
}
