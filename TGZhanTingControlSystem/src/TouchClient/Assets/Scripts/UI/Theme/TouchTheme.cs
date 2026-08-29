using UnityEngine;

namespace TG.Control.Touch.UI.Theme
{
    /// <summary>Commercial TouchClient design tokens for the 1920x1080 runtime shell.</summary>
    public sealed class TouchTheme
    {
        private static readonly string[] FontFamilies = { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" };

        public Color Background { get; } = ParseColor("#061427");
        public Color Surface { get; } = ParseColor("#0B213B");
        public Color SurfaceElevated { get; } = ParseColor("#102C4C");
        public Color HeaderBackground { get; } = ParseColor("#081B32");
        public Color NavigationBackground { get; } = ParseColor("#071A30");
        // Product structure always uses the fixed brand blue. Server configuration must not recolor
        // navigation, semantic states or every primary action.
        public Color Primary { get; } = ParseColor("#1677FF");
        public Color PrimaryMuted { get; } = ParseColor("#123F70");
        public Color PrimarySoft { get; } = ParseColor("#0D3159");
        public Color SurfaceSoft { get; } = ParseColor("#0C1D33");
        public Color SurfaceGlass { get; } = ParseColor("#0D2948");
        public Color TextPrimary { get; } = ParseColor("#F4F8FF");
        public Color TextSecondary { get; } = ParseColor("#A9BCD2");
        public Color Success { get; } = ParseColor("#2ED7A2");
        public Color Warning { get; } = ParseColor("#F0B45A");
        public Color Error { get; } = ParseColor("#F05D6C");
        public Color Border { get; } = ParseColor("#1E4770");
        public Color ConfigurableAccent { get; private set; } = ParseColor("#36A4FF");
        public Color SecondaryButton { get; } = ParseColor("#143453");
        public Color PrimaryHighlight { get; } = ParseColor("#3291FF");
        public Color SecondaryHighlight { get; } = ParseColor("#1A456E");
        public Color PrimaryPressed { get; } = ParseColor("#0E60D0");
        public Color SecondaryPressed { get; } = ParseColor("#0E2A46");
        public Color Disabled { get; } = ParseColor("#526579");
        public Color NeutralTint { get; } = Color.white;
        public Color InputBackground { get; } = ParseColor("#0D2744");
        public Color Divider => Border;
        public Color BackdropVeil { get; } = new Color(.015f, .055f, .105f, .88f);

        public int H1 { get; } = 36;
        public int H2 { get; } = 26;
        public int CardTitle { get; } = 21;
        public int Body { get; } = 18;
        public int Caption { get; } = 15;
        public int ButtonText { get; } = 16;
        public float PagePadding { get; } = 24;
        public float CardSpacing { get; } = 16;
        public float SectionSpacing { get; } = 18;
        public float PanelPadding { get; } = 20;
        public float CornerRadius { get; } = 14;
        public float ButtonHeight { get; } = 64;
        public float StatusBadgeHeight { get; } = 44;
        public float TopBarHeight { get; } = 96;
        public float SideNavigationWidth { get; } = 224;
        public float HomeHeroHeight { get; } = 300;
        public float HomeStatusPanelWidth { get; } = 370;
        public float HomeQuickActionHeight { get; } = 124;
        public float RouteEditorHeaderHeight { get; } = 150;
        public float RouteEditorSelectionWidth { get; } = 440;
        public float RouteEditorSequenceItemHeight { get; } = 78;
        public float PlaybackHeaderHeight { get; } = 86;
        public float PlaybackControlHeight { get; } = 172;
        public Vector2 RouteGridCellSize { get; } = new Vector2(500, 240);
        public Vector2 ModuleGridCellSize { get; } = new Vector2(370, 185);
        public Vector2 RouteEditorModuleCellSize { get; } = new Vector2(265, 166);

        // Compatibility aliases retained until the legacy page internals are migrated in later stages.
        public Color Ink => TextPrimary;
        public Color Muted => TextSecondary;
        public Color Gold => Warning;

        public static TouchTheme CreateDefault() => new TouchTheme();
        public Font CreateFont(int size = 32) => Font.CreateDynamicFontFromOSFont(FontFamilies, size);
        public void SetConfigurableAccent(Color color)
        {
            Color.RGBToHSV(color, out var hue, out var saturation, out var value);
            // Keep the configured hue while guaranteeing readable small accents on the navy UI.
            ConfigurableAccent = Color.HSVToRGB(hue, Mathf.Min(saturation, .78f), Mathf.Max(value, .72f));
            ConfigurableAccent = new Color(ConfigurableAccent.r, ConfigurableAccent.g, ConfigurableAccent.b, 1);
        }

        public static Color ParseColor(string value)
        {
            ColorUtility.TryParseHtmlString(value, out var color);
            return color;
        }
    }
}
