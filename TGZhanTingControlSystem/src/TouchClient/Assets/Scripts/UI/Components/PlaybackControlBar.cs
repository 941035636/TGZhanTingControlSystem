using System;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>Large touch-safe playback controls that only emit operator intent.</summary>
    public sealed class PlaybackControlBar
    {
        private readonly TouchTheme theme;
        private readonly Image frame;
        private readonly Image surface;
        private readonly GameObject normalControls;
        private readonly GameObject stopConfirmation;
        private readonly Button primaryButton;
        private readonly Button retryButton;
        private readonly Button skipButton;
        private readonly Button stopButton;
        private readonly Button cancelStopButton;
        private readonly Button confirmStopButton;
        private bool paused;

        public RectTransform Root => frame.rectTransform;
        public event Action PauseRequested;
        public event Action ResumeRequested;
        public event Action RetryRequested;
        public event Action SkipRequested;
        public event Action StopRequested;
        public event Action StopCancelled;

        public PlaybackControlBar(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            frame = factory.RoundedImage("Playback Control Frame", parent, theme.Border);
            var element = frame.gameObject.AddComponent<LayoutElement>();
            element.minHeight = theme.PlaybackControlHeight;
            element.preferredHeight = theme.PlaybackControlHeight;
            element.flexibleHeight = 0;
            surface = factory.RoundedImage("Playback Control Surface", frame.transform, theme.SurfaceElevated);
            TouchUiFactory.Stretch(surface.rectTransform, 1, 1, -1, -1);

            normalControls = factory.Rect("Playback Normal Controls", surface.transform).gameObject;
            TouchUiFactory.Stretch(normalControls.GetComponent<RectTransform>());
            var heading = factory.Label("Playback Control Heading", normalControls.transform, "讲解控制", theme.SectionTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(heading.rectTransform, 0, 1, 0, 1, 26, -52, 220, -16);
            var hint = factory.Label("Playback Control Hint", normalControls.transform,
                "所有操作以服务器返回的真实讲解状态为准", theme.Caption, FontStyle.Normal,
                theme.TextMuted, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(hint.rectTransform, 0, 1, .55f, 1, 26, -82, 0, -50);

            primaryButton = factory.TouchButton(normalControls.transform, "暂停讲解", true, OnPrimary);
            TouchUiFactory.Anchor(primaryButton.GetComponent<RectTransform>(), 0, 0, 0, 0,
                26, 18, 346, 90);
            retryButton = factory.TouchButton(normalControls.transform, "重试当前", false,
                () => RetryRequested?.Invoke());
            TouchUiFactory.Anchor(retryButton.GetComponent<RectTransform>(), 0, 0, 0, 0,
                364, 22, 574, 86);
            skipButton = factory.TouchButton(normalControls.transform, "跳过当前", false,
                () => SkipRequested?.Invoke());
            TouchUiFactory.Anchor(skipButton.GetComponent<RectTransform>(), 0, 0, 0, 0,
                592, 22, 802, 86);
            skipButton.GetComponent<Image>().color = Color.Lerp(theme.SecondaryButton, theme.Warning, .14f);

            var dangerDivider = factory.Image("Playback Danger Divider", normalControls.transform, theme.Border);
            TouchUiFactory.Anchor(dangerDivider.rectTransform, 1, 0, 1, 1, -392, 18, -390, -18);
            var dangerLabel = factory.Label("Playback Danger Label", normalControls.transform, "危险操作",
                theme.Caption, FontStyle.Bold, theme.Error, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(dangerLabel.rectTransform, 1, 1, 1, 1, -364, -50, -196, -16);
            stopButton = factory.TouchButton(normalControls.transform, "终止讲解", false,
                () => StopRequested?.Invoke());
            TouchUiFactory.Anchor(stopButton.GetComponent<RectTransform>(), 1, 0, 1, 0,
                -364, 22, -26, 86);
            StyleDangerButton(stopButton, false);

            stopConfirmation = factory.RoundedImage("Stop Confirmation", surface.transform,
                Color.Lerp(theme.SurfaceElevated, theme.Error, .14f)).gameObject;
            TouchUiFactory.Stretch(stopConfirmation.GetComponent<RectTransform>(), 10, 10, -10, -10);
            var confirmTitle = factory.Label("Stop Confirmation Title", stopConfirmation.transform,
                "确认终止本次讲解？", theme.H2, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(confirmTitle.rectTransform, 0, 1, .58f, 1, 26, -66, 0, -20);
            var confirmDetail = factory.Label("Stop Confirmation Detail", stopConfirmation.transform,
                "终止后当前讲解任务结束，如需继续必须重新发起。", theme.Body, FontStyle.Normal,
                theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(confirmDetail.rectTransform, 0, 0, .62f, 1, 26, 18, 0, -66);
            cancelStopButton = factory.TouchButton(stopConfirmation.transform, "取消", false,
                () => StopCancelled?.Invoke());
            TouchUiFactory.Anchor(cancelStopButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -494, -36, -274, 36);
            confirmStopButton = factory.TouchButton(stopConfirmation.transform, "确认终止", false,
                () => StopRequested?.Invoke());
            TouchUiFactory.Anchor(confirmStopButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -258, -36, -26, 36);
            StyleDangerButton(confirmStopButton, true);
            stopConfirmation.SetActive(false);
        }

        public void Render(bool connected, bool hasActiveSession, PlaybackSessionStatus session,
            bool stopConfirmationPending)
        {
            paused = session?.paused == true;
            var canControl = connected && hasActiveSession;
            var hasSnapshot = session != null;
            primaryButton.GetComponentInChildren<Text>().text = !hasSnapshot
                ? "正在恢复状态" : paused ? "继续讲解" : "暂停讲解";
            primaryButton.interactable = canControl && hasSnapshot;
            retryButton.interactable = canControl;
            skipButton.interactable = canControl;
            stopButton.interactable = canControl;
            cancelStopButton.interactable = true;
            confirmStopButton.interactable = canControl;
            normalControls.SetActive(!stopConfirmationPending);
            stopConfirmation.SetActive(stopConfirmationPending);
        }

        public void RefreshTheme()
        {
            frame.color = theme.Border;
            surface.color = theme.SurfaceElevated;
            skipButton.GetComponent<Image>().color = Color.Lerp(theme.SecondaryButton, theme.Warning, .14f);
            StyleDangerButton(stopButton, false);
            StyleDangerButton(confirmStopButton, true);
        }

        private void OnPrimary()
        {
            if (paused) ResumeRequested?.Invoke();
            else PauseRequested?.Invoke();
        }

        private void StyleDangerButton(Button button, bool solid)
        {
            button.GetComponent<Image>().color = solid
                ? theme.Error : Color.Lerp(theme.SurfaceSoft, theme.Error, .18f);
            button.GetComponentInChildren<Text>().color = solid ? theme.TextPrimary : theme.Error;
        }
    }
}
