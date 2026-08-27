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

        public Image Background => background;
        public RectTransform ContentRoot => contentHost.ContentRoot;
        public event Action<TouchShellSection> NavigationRequested;

        public TouchAppShell(TouchUiFactory factory, TouchTheme theme)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
        }

        public void Build(Transform canvas)
        {
            background = factory.Image("App Background", canvas, theme.Background);
            TouchUiFactory.Stretch(background.rectTransform);
            background.raycastTarget = false;

            var veil = factory.Image("App Background Veil", canvas, theme.BackdropVeil);
            TouchUiFactory.Stretch(veil.rectTransform);
            veil.raycastTarget = false;
            ambientAccent = factory.Image("Ambient Accent", canvas, Color.Lerp(theme.Background, theme.Primary, .2f));
            TouchUiFactory.Anchor(ambientAccent.rectTransform, 1, 1, 1, 1, -520, -4, 0, 0);
            ambientAccent.raycastTarget = false;

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
            ambientAccent.color = Color.Lerp(theme.Background, theme.Primary, .2f);
            topBar.RefreshTheme();
            navigation.RefreshTheme();
            contentHost.RefreshTheme(theme);
        }
    }
}
