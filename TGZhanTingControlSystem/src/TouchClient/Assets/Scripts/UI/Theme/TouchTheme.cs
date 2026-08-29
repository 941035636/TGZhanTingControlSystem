using UnityEngine;

namespace TG.Control.Touch.UI.Theme
{
    /// <summary>Commercial TouchClient design tokens for the 1920x1080 runtime shell.</summary>
    public sealed class TouchTheme
    {
        private static readonly string[] FontFamilies = { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" };

        // Core product surfaces. Configurable server colors never replace these structural colors.
        public Color AppBackground { get; } = ParseColor("#061427");
        public Color Surface { get; } = ParseColor("#0A1F38");
        public Color SurfaceElevated { get; } = ParseColor("#102E50");
        public Color HeaderBackground { get; } = ParseColor("#081B32");
        public Color NavigationBackground { get; } = ParseColor("#071A30");
        // Product structure always uses the fixed brand blue. Server configuration must not recolor
        // navigation, semantic states or every primary action.
        public Color Primary { get; } = ParseColor("#1677FF");
        public Color PrimaryHover { get; } = ParseColor("#3291FF");
        public Color PrimaryPressed { get; } = ParseColor("#0E60D0");
        public Color PrimaryMuted { get; } = ParseColor("#123F70");
        public Color PrimarySoft { get; } = ParseColor("#0D3159");
        public Color SurfaceSoft { get; } = ParseColor("#0A1A2F");
        public Color SurfaceGlass { get; } = ParseColor("#0D2948");
        public Color TextPrimary { get; } = ParseColor("#F4F8FF");
        public Color TextSecondary { get; } = ParseColor("#AFC1D6");
        public Color TextMuted { get; } = ParseColor("#758AA3");
        public Color Success { get; } = ParseColor("#2ED7A2");
        public Color Warning { get; } = ParseColor("#F0B45A");
        public Color Error { get; } = ParseColor("#F05D6C");
        public Color Border { get; } = ParseColor("#1A4267");
        public Color BorderStrong { get; } = ParseColor("#286594");
        public Color ConfigurableAccent { get; private set; } = ParseColor("#36A4FF");
        public Color SecondaryButton { get; } = ParseColor("#143453");
        public Color PrimaryHighlight => PrimaryHover;
        public Color SecondaryHighlight { get; } = ParseColor("#1A456E");
        public Color SecondaryPressed { get; } = ParseColor("#0E2A46");
        public Color Disabled { get; } = ParseColor("#4C6075");
        public Color DisabledSurface { get; } = ParseColor("#13283D");
        public Color DisabledControlTint { get; } = new Color(.52f, .58f, .66f, .62f);
        public Color NeutralTint { get; } = Color.white;
        public Color InputBackground { get; } = ParseColor("#0D2744");
        public Color Divider => Border;
        public Color BackdropVeil { get; } = new Color(.015f, .055f, .105f, .94f);
        public Color HeroOverlay => new Color(AppBackground.r, AppBackground.g, AppBackground.b, .76f);

        // Typography scale for a fixed 55-inch, 1920x1080 touch terminal.
        public int Display { get; } = 40;
        public int PageTitle { get; } = 28;
        public int SectionTitle { get; } = 23;
        public int CardTitle { get; } = 20;
        public int Body { get; } = 18;
        public int Secondary { get; } = 16;
        public int Caption { get; } = 14;
        public int ButtonText { get; } = 17;

        // Spacing scale. Layout code should compose from these values instead of inventing new gaps.
        public float Space8 { get; } = 8;
        public float Space12 { get; } = 12;
        public float Space16 { get; } = 16;
        public float Space24 { get; } = 24;
        public float Space32 { get; } = 32;
        public float Space48 { get; } = 48;
        public float PagePadding => Space24;
        public float CardSpacing => Space16;
        public float SectionSpacing => Space16;
        public float PanelPadding => Space24;
        public float CornerRadius { get; } = 14;
        public float ButtonHeight { get; } = 64;
        public float PrimaryButtonHeight { get; } = 72;
        public float CompactButtonHeight { get; } = 56;
        public float StatusBadgeHeight { get; } = 44;
        public float TopBarHeight { get; } = 96;
        public float SideNavigationWidth { get; } = 224;
        public float NavigationItemHeight { get; } = 68;
        public float HomeHeroHeight { get; } = 262;
        public float HomeStatusPanelWidth { get; } = 360;
        public float HomeQuickActionHeight { get; } = 104;
        public float RouteEditorHeaderHeight { get; } = 150;
        public float RouteEditorSelectionWidth { get; } = 440;
        public float RouteEditorSequenceItemHeight { get; } = 78;
        public float PlaybackHeaderHeight { get; } = 80;
        public float PlaybackControlHeight { get; } = 168;
        public float SystemStatusSummaryHeight { get; } = 190;
        public float SystemHealthCardHeight { get; } = 254;
        public float SystemStatusSessionHeight { get; } = 132;
        public Vector2 RouteGridCellSize { get; } = new Vector2(774, 190);
        public Vector2 ModuleGridCellSize { get; } = new Vector2(370, 185);
        public Vector2 RouteEditorModuleCellSize { get; } = new Vector2(265, 166);

        // Compatibility aliases retained for already migrated pages.
        public Color Background => AppBackground;
        public int H1 => Display;
        public int H2 => PageTitle;
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
