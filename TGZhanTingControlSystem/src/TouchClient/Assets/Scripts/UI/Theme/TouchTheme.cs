using UnityEngine;

namespace TG.Control.Touch.UI.Theme
{
    /// <summary>Commercial TouchClient design tokens for the 1920x1080 runtime shell.</summary>
    public sealed class TouchTheme
    {
        private static readonly string[] FontFamilies = { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" };

        // Light exhibition shell. Configurable server colors never replace these structural tokens.
        public Color AppBackground { get; } = ParseColor("#F5F8FC");
        public Color Surface { get; } = ParseColor("#FFFFFF");
        public Color SurfaceElevated { get; } = ParseColor("#FFFFFF");
        public Color HeaderBackground { get; } = ParseColor("#FFFFFF");
        public Color NavigationBackground { get; } = ParseColor("#FFFFFF");
        public Color ShellPanel { get; } = ParseColor("#F7FAFE");
        public Color ShellPanelActive { get; } = ParseColor("#EAF3FF");
        public Color ShellHighlight { get; } = ParseColor("#1677FF");
        public Color ShellBorder { get; } = ParseColor("#E3EAF3");
        // Product structure always uses the fixed brand blue. Server configuration must not recolor
        // navigation, semantic states or every primary action.
        public Color Primary { get; } = ParseColor("#1677FF");
        public Color PrimaryHover { get; } = ParseColor("#3291FF");
        public Color PrimaryPressed { get; } = ParseColor("#0E60D0");
        public Color PrimaryMuted { get; } = ParseColor("#DCEBFF");
        public Color PrimarySoft { get; } = ParseColor("#EAF3FF");
        public Color SurfaceSoft { get; } = ParseColor("#F7FAFE");
        public Color SurfaceGlass { get; } = ParseColor("#FFFFFF");
        public Color TextPrimary { get; } = ParseColor("#17345C");
        public Color TextSecondary { get; } = ParseColor("#64748B");
        public Color TextMuted { get; } = ParseColor("#94A3B8");
        public Color Success { get; } = ParseColor("#18A874");
        public Color Warning { get; } = ParseColor("#E8A23A");
        public Color Error { get; } = ParseColor("#E5484D");
        public Color Border { get; } = ParseColor("#E3EAF3");
        public Color BorderStrong { get; } = ParseColor("#B9D6FF");
        public Color ConfigurableAccent { get; private set; } = ParseColor("#36A4FF");
        public Color SecondaryButton { get; } = ParseColor("#FFFFFF");
        public Color PrimaryHighlight => PrimaryHover;
        public Color SecondaryHighlight { get; } = ParseColor("#EAF3FF");
        public Color SecondaryPressed { get; } = ParseColor("#DCEBFF");
        public Color Disabled { get; } = ParseColor("#94A3B8");
        public Color DisabledSurface { get; } = ParseColor("#EEF2F7");
        public Color DisabledControlTint { get; } = new Color(.58f, .64f, .72f, .62f);
        public Color NeutralTint { get; } = Color.white;
        public Color InputBackground { get; } = ParseColor("#FFFFFF");
        public Color Divider => Border;
        public Color BackdropVeil { get; } = new Color(.96f, .98f, 1f, .72f);
        public Color HeroOverlay => new Color(1f, 1f, 1f, .58f);

        // Phase UI-11 home-only dark visual mode. These tokens do not replace the light product
        // system used by RouteEditor, Playback and SystemStatus.
        public Color HomeDarkBackground { get; } = ParseColor("#03132B");
        public Color HomeDarkHeader { get; } = new Color(.018f, .075f, .16f, .90f);
        public Color HomeDarkNavigation { get; } = new Color(.015f, .070f, .15f, .88f);
        public Color HomeDarkSurface { get; } = new Color(.018f, .095f, .21f, .74f);
        public Color HomeDarkSurfaceElevated { get; } = new Color(.025f, .12f, .27f, .88f);
        public Color HomeDarkGlass { get; } = new Color(.025f, .13f, .29f, .76f);
        public Color HomeDarkBorder { get; } = new Color(.08f, .55f, 1f, .72f);
        public Color HomeDarkBorderSoft { get; } = new Color(.22f, .64f, 1f, .46f);
        public Color HomeDarkAccent { get; } = ParseColor("#20A8FF");
        public Color HomeDarkTextPrimary { get; } = ParseColor("#F4FAFF");
        public Color HomeDarkTextSecondary { get; } = ParseColor("#B8D3F2");
        public Color HomeDarkTextMuted { get; } = ParseColor("#7F9FC7");
        public Color HomeDarkBackdropVeil { get; } = new Color(.004f, .025f, .075f, .18f);
        public Color HomeGlassContentTint { get; } = new Color(1f, 1f, 1f, .72f);
        public Color HomeGlassTitleTint { get; } = new Color(1f, 1f, 1f, .76f);
        public Color HomeGlassBottomTint { get; } = new Color(1f, 1f, 1f, .82f);
        public Color HomeGlassChromeTint { get; } = new Color(1f, 1f, 1f, .78f);

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
            // Keep the configured hue while guaranteeing readable small accents on the light UI.
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
