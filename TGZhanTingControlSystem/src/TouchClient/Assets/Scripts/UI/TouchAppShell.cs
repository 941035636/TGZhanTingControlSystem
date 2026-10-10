using System;
using TG.Control.Touch.UI.Components;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI
{
    /// <summary>
    /// Owns only the global shell: layout, navigation, page host and truthful global status.
    /// It has no Facade, API, route editing or playback-state-machine dependency.
    /// </summary>
    public sealed class TouchAppShell
    {
        private readonly TouchUiFactory factory;
        private readonly TouchTheme theme;
        private RectTransform shellRoot;
        private TopBar topBar;
        private SideNavigation navigation;
        private ContentHost contentHost;
        private Image background;
        private Image homeDarkBackground;
        private Image veil;
        private Image ambientAccent;
        private Image chromeHeader;
        private Image chromeNavigation;
        private Sprite chromeHeaderDefaultSprite;
        private Sprite chromeNavigationDefaultSprite;
        private Image.Type chromeHeaderDefaultType;
        private Image.Type chromeNavigationDefaultType;
        private GameObject exitConfirmation;
        private bool homeDarkMode;

        public Image Background => background;
        public RectTransform ContentRoot => contentHost.ContentRoot;
        public bool Visible => shellRoot != null && shellRoot.gameObject.activeSelf;
        public event Action<TouchShellSection> NavigationRequested;
        public event Action ExitConfirmed;

        public TouchAppShell(TouchUiFactory factory, TouchTheme theme)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
        }

        public void Build(Transform canvas)
        {
            shellRoot = factory.Rect("Touch App Shell", canvas);
            TouchUiFactory.Stretch(shellRoot);

            background = factory.Image("App Background", shellRoot, theme.AppBackground);
            TouchUiFactory.Stretch(background.rectTransform);
            background.raycastTarget = false;

            homeDarkBackground = factory.Image("Home Hall Background", shellRoot, Color.white);
            TouchUiFactory.Stretch(homeDarkBackground.rectTransform);
            homeDarkBackground.type = Image.Type.Simple;
            homeDarkBackground.preserveAspect = true;
            homeDarkBackground.raycastTarget = false;
            homeDarkBackground.sprite = HomeDarkVisualAssets.HallBackground;
            homeDarkBackground.gameObject.SetActive(false);

            veil = factory.Image("App Background Veil", shellRoot, theme.BackdropVeil);
            TouchUiFactory.Stretch(veil.rectTransform);
            veil.raycastTarget = false;
            ambientAccent = factory.Image("Ambient Accent", shellRoot,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .035f));
            TouchUiFactory.Anchor(ambientAccent.rectTransform, 1, 1, 1, 1, -520, -4, 0, 0);
            ambientAccent.raycastTarget = false;

            // A quiet white shell deliberately leaves visual priority to the exhibition content.
            chromeHeader = factory.Image("Shell Chrome Header", shellRoot, theme.HeaderBackground);
            TouchUiFactory.Anchor(chromeHeader.rectTransform, 0, 1, 1, 1, 0, -theme.TopBarHeight, 0, 0);
            chromeHeader.raycastTarget = false;
            chromeHeaderDefaultSprite = chromeHeader.sprite;
            chromeHeaderDefaultType = chromeHeader.type;
            chromeNavigation = factory.Image("Shell Chrome Navigation", shellRoot, theme.NavigationBackground);
            TouchUiFactory.Anchor(chromeNavigation.rectTransform, 0, 0, 0, 1, 0, 0,
                theme.SideNavigationWidth, -theme.TopBarHeight);
            chromeNavigation.raycastTarget = false;
            chromeNavigationDefaultSprite = chromeNavigation.sprite;
            chromeNavigationDefaultType = chromeNavigation.type;

            topBar = new TopBar(factory, theme, shellRoot);
            TouchUiFactory.Anchor(topBar.Root, 0, 1, 1, 1, 0, -theme.TopBarHeight, 0, 0);

            navigation = new SideNavigation(factory, theme, shellRoot);
            TouchUiFactory.Anchor(navigation.Root, 0, 0, 0, 1, 0, 0,
                theme.SideNavigationWidth, -theme.TopBarHeight);
            navigation.NavigateRequested += section => NavigationRequested?.Invoke(section);

            contentHost = new ContentHost(factory, theme, shellRoot);
            TouchUiFactory.Anchor(contentHost.Root, 0, 0, 1, 1,
                theme.SideNavigationWidth + theme.PagePadding, theme.PagePadding,
                -theme.PagePadding, -theme.TopBarHeight - theme.PagePadding);

            topBar.ExitRequested += ShowExitConfirmation;
            BuildExitConfirmation(shellRoot);
            ApplyVisualMode();
        }

        /// <summary>
        /// Welcome/standby is a mutually exclusive presentation mode. Deactivating the complete shell
        /// prevents its pages from rendering through a transparent welcome asset and also removes every
        /// underlying navigation control from the raycast hierarchy.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (shellRoot != null) shellRoot.gameObject.SetActive(visible);
        }

        public void SetBranding(string title, string subtitle) => topBar.SetBranding(title, subtitle);
        public void SetActiveSection(TouchShellSection section) => navigation.SetActive(section);
        public void Tick(DateTime now) => topBar.Tick(now);

        public void SetHomeDarkMode(bool enabled)
        {
            homeDarkMode = enabled;
            ApplyVisualMode();
        }

        public void SetGlobalState(bool connected, SystemReadiness readiness, bool hasActiveSession)
        {
            topBar.SetConnection(connected);
            navigation.SetPlaybackAvailable(hasActiveSession);
            if (!connected)
            {
                topBar.SetReadiness("服务连接中", StatusTone.Error);
                return;
            }
            if (readiness == null)
            {
                topBar.SetReadiness("状态检查中", StatusTone.Neutral);
                return;
            }
            if (!readiness.ledOnline)
            {
                topBar.SetReadiness("LED 离线", StatusTone.Error);
                return;
            }
            if (readiness.canStart && readiness.ledReady)
            {
                topBar.SetReadiness("系统可接待", StatusTone.Success);
                return;
            }
            if (readiness.canStart)
            {
                topBar.SetReadiness("受限可用", StatusTone.Warning);
                return;
            }
            topBar.SetReadiness("暂不可接待", StatusTone.Warning);
        }

        public void RefreshTheme()
        {
            ApplyVisualMode();
            topBar.RefreshTheme();
            navigation.RefreshTheme();
            contentHost.RefreshTheme(theme);
        }

        private void ApplyVisualMode()
        {
            if (background == null) return;
            background.color = homeDarkMode ? theme.HomeDarkBackground : theme.AppBackground;
            if (homeDarkBackground != null)
                homeDarkBackground.gameObject.SetActive(homeDarkMode && homeDarkBackground.sprite != null);
            veil.color = homeDarkMode ? theme.HomeDarkBackdropVeil : theme.BackdropVeil;
            ambientAccent.color = homeDarkMode
                ? new Color(theme.HomeDarkAccent.r, theme.HomeDarkAccent.g, theme.HomeDarkAccent.b, .08f)
                : new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .035f);
            if (homeDarkMode)
            {
                HomeDarkVisualAssets.ApplySliced(chromeHeader, HomeDarkVisualAssets.TitleGlass,
                    theme.HomeGlassChromeTint);
                HomeDarkVisualAssets.ApplySliced(chromeNavigation, HomeDarkVisualAssets.SidebarGlass,
                    theme.HomeGlassChromeTint);
            }
            else
            {
                chromeHeader.sprite = chromeHeaderDefaultSprite;
                chromeHeader.type = chromeHeaderDefaultType;
                chromeHeader.color = theme.HeaderBackground;
                chromeNavigation.sprite = chromeNavigationDefaultSprite;
                chromeNavigation.type = chromeNavigationDefaultType;
                chromeNavigation.color = theme.NavigationBackground;
            }
            topBar?.SetHomeDarkMode(homeDarkMode);
            navigation?.SetHomeDarkMode(homeDarkMode);
            contentHost?.SetHomeDarkMode(homeDarkMode, theme);
        }

        private void BuildExitConfirmation(Transform canvas)
        {
            var backdrop = factory.Image("Exit Confirmation Backdrop", canvas,
                new Color(0, 0, 0, .72f));
            TouchUiFactory.Stretch(backdrop.rectTransform);
            exitConfirmation = backdrop.gameObject;

            var dialog = factory.RoundedImage("Exit Confirmation Dialog", backdrop.transform,
                theme.SurfaceElevated);
            TouchUiFactory.Anchor(dialog.rectTransform, .5f, .5f, .5f, .5f,
                -350, -180, 350, 180);

            var title = factory.Label("Exit Confirmation Title", dialog.transform,
                "确认退出中控端？", theme.PageTitle, FontStyle.Bold,
                theme.TextPrimary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(title.rectTransform, 0, 1, 1, 1,
                theme.Space32, -100, -theme.Space32, -theme.Space32);

            var detail = factory.Label("Exit Confirmation Detail", dialog.transform,
                "退出后可通过运行管理程序重新启动中控端。", theme.Body, FontStyle.Normal,
                theme.TextSecondary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(detail.rectTransform, 0, .5f, 1, 1,
                theme.Space32, -20, -theme.Space32, -102);

            var cancel = factory.TouchButton(dialog.transform, "取消", false, HideExitConfirmation);
            TouchUiFactory.Anchor(cancel.GetComponent<RectTransform>(), .5f, 0, .5f, 0,
                -240, theme.Space32, -20, theme.Space32 + theme.ButtonHeight);

            var confirm = factory.TouchButton(dialog.transform, "确认退出", false, ConfirmExit);
            TouchUiFactory.Anchor(confirm.GetComponent<RectTransform>(), .5f, 0, .5f, 0,
                20, theme.Space32, 240, theme.Space32 + theme.ButtonHeight);
            confirm.GetComponent<Image>().color = Color.Lerp(theme.SecondaryButton, theme.Error, .34f);

            exitConfirmation.SetActive(false);
        }

        private void ShowExitConfirmation() => exitConfirmation.SetActive(true);
        private void HideExitConfirmation() => exitConfirmation.SetActive(false);

        private void ConfirmExit()
        {
            exitConfirmation.SetActive(false);
            ExitConfirmed?.Invoke();
        }
    }
}
