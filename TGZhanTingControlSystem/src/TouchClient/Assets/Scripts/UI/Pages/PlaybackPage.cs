using System;
using System.Collections.Generic;
using TG.Control.Touch.UI.Components;
using TG.Control.Touch.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Pages
{
    /// <summary>
    /// Commercial playback workbench. It renders TouchUiState and emits operator intent only;
    /// session polling, recovery and playback control remain in the existing presenter/facade path.
    /// </summary>
    public sealed class PlaybackPage
    {
        private readonly TouchTheme theme;
        private readonly RectTransform root;
        private readonly Image headerFrame;
        private readonly PlaybackProgressPanel progressPanel;
        private readonly PlaybackControlBar controlBar;
        private readonly ErrorBanner errorBanner;

        public RectTransform Root => root;
        public event Action PauseRequested;
        public event Action ResumeRequested;
        public event Action RetryRequested;
        public event Action SkipRequested;
        public event Action StopRequested;
        public event Action StopCancelled;

        public PlaybackPage(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            root = factory.Rect("Playback Page", parent);
            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = theme.SectionSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            headerFrame = factory.RoundedImage("Playback Header Frame", root, theme.Border);
            var headerElement = headerFrame.gameObject.AddComponent<LayoutElement>();
            headerElement.minHeight = theme.PlaybackHeaderHeight;
            headerElement.preferredHeight = theme.PlaybackHeaderHeight;
            headerElement.flexibleHeight = 0;
            var header = factory.RoundedImage("Playback Header", headerFrame.transform, theme.SurfaceElevated);
            TouchUiFactory.Stretch(header.rectTransform, 1, 1, -1, -1);
            var title = factory.Label("Playback Page Title", header.transform, "当前讲解", theme.PageTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, 0, .5f, 1, theme.PanelPadding, 10, 0, -10);
            var subtitle = factory.Label("Playback Page Subtitle", header.transform,
                "聚焦当前主题与节点，所有控制以服务端真实状态为准", theme.Secondary,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(subtitle.rectTransform, .42f, 0, 1, 1,
                0, 10, -theme.PanelPadding, -10);

            progressPanel = new PlaybackProgressPanel(factory, theme, root);
            controlBar = new PlaybackControlBar(factory, theme, root);
            controlBar.PauseRequested += () => PauseRequested?.Invoke();
            controlBar.ResumeRequested += () => ResumeRequested?.Invoke();
            controlBar.RetryRequested += () => RetryRequested?.Invoke();
            controlBar.SkipRequested += () => SkipRequested?.Invoke();
            controlBar.StopRequested += () => StopRequested?.Invoke();
            controlBar.StopCancelled += () => StopCancelled?.Invoke();

            errorBanner = new ErrorBanner(factory, theme, root);
            TouchUiFactory.Anchor(errorBanner.Root, 0, 1, 1, 1, theme.Space16,
                -theme.PlaybackHeaderHeight - 76, -theme.Space16, -theme.PlaybackHeaderHeight - theme.Space8);
        }

        public void Render(TouchUiState state, string verifiedRouteName,
            IReadOnlyList<string> verifiedModuleIds, bool stopConfirmationPending)
        {
            if (state == null) return;
            progressPanel.Render(state, verifiedRouteName, verifiedModuleIds);
            controlBar.Render(state.Connected, state.HasActiveSession, state.Session, stopConfirmationPending);
        }

        public void ShowError(string message) => errorBanner.Show(SanitizeOperatorError(message));
        public void ClearError() => errorBanner.Hide();

        public void RefreshTheme()
        {
            headerFrame.color = theme.Border;
            progressPanel.RefreshTheme();
            controlBar.RefreshTheme();
            errorBanner.RefreshTheme();
        }

        private static string SanitizeOperatorError(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "讲解操作未完成，请稍后重试。";
            var lower = message.ToLowerInvariant();
            if (lower.Contains("409") || lower.Contains("conflict"))
                return "当前讲解状态已变化，请等待页面刷新后再操作。";
            if (lower.Contains("exception") || lower.Contains("stack") || lower.Contains("http"))
                return "讲解服务暂时无法完成操作，请检查连接后重试。";
            return message.StartsWith("操作失败：", StringComparison.Ordinal) ? message : "操作失败：" + message;
        }
    }
}
