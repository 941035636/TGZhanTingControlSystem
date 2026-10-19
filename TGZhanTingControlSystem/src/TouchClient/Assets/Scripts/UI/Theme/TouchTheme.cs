using UnityEngine;

namespace TG.Control.Touch.UI.Theme
{
    /// <summary>Commercial TouchClient design tokens for the 1920x1080 runtime shell.</summary>
    public sealed class TouchTheme
    {
        private static readonly string[] FontFamilies =
            { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimHei", "Noto Sans SC", "Arial" };

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
        public Color HomeDarkHeader { get; } = new Color(.008f, .04f, .105f, .84f);
        public Color HomeDarkNavigation { get; } = new Color(.008f, .045f, .11f, .82f);
        public Color HomeDarkSurface { get; } = new Color(.012f, .055f, .135f, .76f);
        public Color HomeDarkSurfaceElevated { get; } = new Color(.018f, .075f, .17f, .86f);
        public Color HomeDarkGlass { get; } = new Color(.02f, .10f, .22f, .70f);
        public Color HomeDarkBorder { get; } = new Color(.08f, .55f, 1f, .40f);
        public Color HomeDarkBorderSoft { get; } = new Color(.18f, .58f, 1f, .24f);
        public Color HomeDarkAccent { get; } = ParseColor("#20A8FF");
        public Color HomeDarkTextPrimary { get; } = ParseColor("#F4FAFF");
        public Color HomeDarkTextSecondary { get; } = ParseColor("#B8D3F2");
        public Color HomeDarkTextMuted { get; } = ParseColor("#7F9FC7");
        public Color HomeDarkBackdropVeil { get; } = new Color(.004f, .02f, .065f, .30f);
        // The 4K exhibition hall already contains the intended blue illumination. These home-only
        // glass values reveal that authored light without changing Playback/SystemStatus surfaces.
        public Color HomeHallBackdropVeil { get; } = new Color(.004f, .02f, .065f, .07f);
        public Color HomeHallHeader { get; } = new Color(.006f, .035f, .105f, .50f);
        public Color HomeHallNavigation { get; } = new Color(.005f, .04f, .12f, .58f);
        public Color HomeHallContentSurface { get; } = new Color(.006f, .045f, .12f, .28f);
        public Color HomeHallPanelSurface { get; } = new Color(.008f, .055f, .15f, .42f);
        public Color HomeHallActionSurface { get; } = new Color(.008f, .05f, .14f, .56f);
        public Color HomeHallBorder { get; } = new Color(.08f, .60f, 1f, .48f);
        // Playback keeps more visual weight than the home grid so long narration text remains
        // readable, while avoiding the former stack of 70%-86% opaque navy surfaces.
        public Color PlaybackBackdropVeil { get; } = new Color(.004f, .02f, .065f, .13f);
        public Color PlaybackHeader { get; } = new Color(.006f, .035f, .105f, .60f);
        public Color PlaybackNavigation { get; } = new Color(.005f, .04f, .12f, .66f);
        public Color PlaybackContentSurface { get; } = new Color(.006f, .04f, .115f, .38f);
        public Color PlaybackPanelSurface { get; } = new Color(.008f, .05f, .14f, .58f);
        public Color PlaybackPanelElevated { get; } = new Color(.01f, .065f, .17f, .66f);
        public Color PlaybackControlSurface { get; } = new Color(.008f, .055f, .15f, .68f);
        public Color PlaybackGlass { get; } = new Color(.015f, .105f, .24f, .58f);
        public Color PlaybackBorder { get; } = new Color(.08f, .62f, 1f, .52f);
        public Color PlaybackBorderSoft { get; } = new Color(.16f, .62f, 1f, .30f);
        // Shared technology-blue glass surfaces. Gallery adopts these first; other pages keep
        // their accepted visual parameters until separately reviewed.
        public Color TechBackdropVeil { get; } = new Color(.004f, .02f, .065f, .08f);
        public Color TechHeader { get; } = new Color(.006f, .045f, .13f, .50f);
        public Color TechNavigation { get; } = new Color(.006f, .05f, .14f, .58f);
        public Color TechContentSurface { get; } = new Color(.008f, .06f, .16f, .24f);
        public Color TechPanelSurface { get; } = new Color(.012f, .085f, .21f, .56f);
        public Color TechPanelElevated { get; } = new Color(.012f, .09f, .22f, .68f);
        public Color TechPreviewSurface { get; } = new Color(.009f, .055f, .14f, .66f);
        public Color TechBorder { get; } = new Color(.12f, .66f, 1f, .54f);
        public Color TechBorderSoft { get; } = new Color(.16f, .63f, 1f, .34f);
        public Color TechSelectionSurface { get; } = new Color(.035f, .35f, .74f, .86f);
        public Color TechPrimaryButton { get; } = ParseColor("#0878E9");
        public Color TechPrimaryHover { get; } = ParseColor("#239AFF");
        public Color TechPrimaryPressed { get; } = ParseColor("#075CBC");
        public Color TechSecondaryButton { get; } = new Color(.055f, .27f, .55f, .88f);
        public Color TechSecondaryHover { get; } = new Color(.075f, .38f, .70f, .94f);
        public Color TechSecondaryPressed { get; } = new Color(.045f, .22f, .46f, .96f);
        public Color TechDangerButton { get; } = new Color(.44f, .08f, .15f, .80f);
        public Color TechDisabledButton { get; } = new Color(.24f, .34f, .48f, .82f);
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
