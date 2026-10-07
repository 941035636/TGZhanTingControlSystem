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
        private TopBar topBar;
        private SideNavigation navigation;
        private ContentHost contentHost;
        private Image background;
        private Image ambientAccent;
        private Image chromeHeader;
        private Image chromeNavigation;
        private GameObject exitConfirmation;

        public Image Background => background;
        public RectTransform ContentRoot => contentHost.ContentRoot;
        public event Action<TouchShellSection> NavigationRequested;
        public event Action ExitConfirmed;

        public TouchAppShell(TouchUiFactory factory, TouchTheme theme)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
        }

        public void Build(Transform canvas)
        {
            background = factory.Image("App Background", canvas, theme.AppBackground);
            TouchUiFactory.Stretch(background.rectTransform);
            background.raycastTarget = false;

            var veil = factory.Image("App Background Veil", canvas, theme.BackdropVeil);
            TouchUiFactory.Stretch(veil.rectTransform);
            veil.raycastTarget = false;
            ambientAccent = factory.Image("Ambient Accent", canvas,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .03f));
            TouchUiFactory.Anchor(ambientAccent.rectTransform, 1, 1, 1, 1, -520, -4, 0, 0);
            ambientAccent.raycastTarget = false;

            // Leave the shell open around the floating controls. Full-width opaque
            // chrome made the header and navigation read like an admin dashboard.
            chromeHeader = factory.Image("Shell Chrome Header", canvas, Color.clear);
            TouchUiFactory.Anchor(chromeHeader.rectTransform, 0, 1, 1, 1, 0, -theme.TopBarHeight, 0, 0);
            chromeHeader.raycastTarget = false;
            chromeNavigation = factory.Image("Shell Chrome Navigation", canvas, Color.clear);
            TouchUiFactory.Anchor(chromeNavigation.rectTransform, 0, 0, 0, 1, 0, 0,
                theme.SideNavigationWidth, -theme.TopBarHeight);
            chromeNavigation.raycastTarget = false;

            topBar = new TopBar(factory, theme, canvas);
            TouchUiFactory.Anchor(topBar.Root, 0, 1, 1, 1, 0, -theme.TopBarHeight, 0, 0);

            navigation = new SideNavigation(factory, theme, canvas);
            TouchUiFactory.Anchor(navigation.Root, 0, 0, 0, 1, 0, 0,
                theme.SideNavigationWidth, -theme.TopBarHeight);
            navigation.NavigateRequested += section => NavigationRequested?.Invoke(section);

            contentHost = new ContentHost(factory, theme, canvas);
            TouchUiFactory.Anchor(contentHost.Root, 0, 0, 1, 1,
                theme.SideNavigationWidth + theme.PagePadding, theme.PagePadding,
                -theme.PagePadding, -theme.TopBarHeight - theme.PagePadding);

            topBar.ExitRequested += ShowExitConfirmation;
            BuildExitConfirmation(canvas);
        }

        public void SetBranding(string title, string subtitle) => topBar.SetBranding(title, subtitle);
        public void SetActiveSection(TouchShellSection section) => navigation.SetActive(section);
        public void Tick(DateTime now) => topBar.Tick(now);

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
            ambientAccent.color = new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .03f);
            chromeHeader.color = Color.clear;
            chromeNavigation.color = Color.clear;
            topBar.RefreshTheme();
            navigation.RefreshTheme();
            contentHost.RefreshTheme(theme);
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
