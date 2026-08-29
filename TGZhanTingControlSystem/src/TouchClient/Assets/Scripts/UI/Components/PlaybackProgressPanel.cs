using System;
using System.Collections.Generic;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>
    /// Pure presentation of the server-owned playback snapshot. It does not infer media time,
    /// coordinate playback or keep a second session state machine.
    /// </summary>
    public sealed class PlaybackProgressPanel
    {
        private readonly TouchTheme theme;
        private readonly Image frame;
        private readonly Image surface;
        private readonly Image stateAccent;
        private readonly StatusBadge stateBadge;
        private readonly Text routeName;
        private readonly Text moduleSequence;
        private readonly Text moduleName;
        private readonly Text nodeName;
        private readonly Text progressLabel;
        private readonly Image progressFill;
        private readonly Text preparationLabel;

        public RectTransform Root => frame.rectTransform;

        public PlaybackProgressPanel(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            frame = factory.RoundedImage("Playback Progress Frame", parent, theme.Border);
            frame.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            surface = factory.RoundedImage("Playback Progress Surface", frame.transform, theme.SurfaceElevated);
            TouchUiFactory.Stretch(surface.rectTransform, 1, 1, -1, -1);

            stateAccent = factory.RoundedImage("Playback State Accent", surface.transform, theme.Primary);
            TouchUiFactory.Anchor(stateAccent.rectTransform, 0, 0, 0, 1, 0, 0, 8, 0);
            stateAccent.raycastTarget = false;

            var eyebrow = factory.Label("Playback Eyebrow", surface.transform, "现场讲解执行", theme.Caption,
                FontStyle.Bold, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(eyebrow.rectTransform, 0, 1, .55f, 1, 34, -52, 0, -18);

            stateBadge = new StatusBadge(factory, theme, surface.transform, "Playback State Badge");
            TouchUiFactory.Anchor(stateBadge.Root, 1, 1, 1, 1, -248, -62, -28, -18);

            var routeKey = factory.Label("Playback Route Key", surface.transform, "当前路线", theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(routeKey.rectTransform, 0, 1, 0, 1, 34, -100, 170, -68);
            routeName = factory.Label("Playback Route Name", surface.transform, "当前讲解任务", theme.Body,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(routeName.rectTransform, 0, 1, 1, 1, 170, -104, -34, -66);
            routeName.resizeTextForBestFit = true;
            routeName.resizeTextMinSize = theme.Caption;
            routeName.resizeTextMaxSize = theme.Body;

            var divider = factory.Image("Playback Identity Divider", surface.transform, theme.Border);
            TouchUiFactory.Anchor(divider.rectTransform, 0, 1, 1, 1, 34, -120, -34, -118);
            divider.raycastTarget = false;

            var focusSurface = factory.RoundedImage("Playback Current Focus", surface.transform, theme.SurfaceGlass);
            TouchUiFactory.Anchor(focusSurface.rectTransform, 0, 1, 1, 1,
                theme.Space32, -350, -theme.Space32, -132);
            focusSurface.raycastTarget = false;

            var moduleKey = factory.Label("Playback Module Key", surface.transform, "当前讲解主题", theme.Caption,
                FontStyle.Bold, theme.Primary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(moduleKey.rectTransform, 0, 1, .5f, 1, 52, -174, 0, -144);
            moduleSequence = factory.Label("Playback Module Sequence", surface.transform, string.Empty, theme.Caption,
                FontStyle.Bold, theme.TextSecondary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(moduleSequence.rectTransform, .5f, 1, 1, 1, 0, -174, -52, -144);

            moduleName = factory.Label("Playback Module Name", surface.transform, "正在恢复当前主题", theme.Display,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(moduleName.rectTransform, 0, 1, 1, 1, 52, -238, -52, -178);
            moduleName.resizeTextForBestFit = true;
            moduleName.resizeTextMinSize = theme.H2;
            moduleName.resizeTextMaxSize = theme.Display;

            var nodeKey = factory.Label("Playback Node Key", surface.transform, "当前讲解节点", theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(nodeKey.rectTransform, 0, 1, 0, 1, 52, -278, 206, -246);
            nodeName = factory.Label("Playback Node Name", surface.transform, "正在同步大屏与语音，请稍候…", theme.PageTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(nodeName.rectTransform, 0, 1, 1, 1, 206, -290, -52, -242);
            nodeName.resizeTextForBestFit = true;
            nodeName.resizeTextMinSize = theme.Body;
            nodeName.resizeTextMaxSize = theme.PageTitle;

            var progressArea = factory.RoundedImage("Playback Overall Progress", surface.transform, theme.SurfaceSoft);
            TouchUiFactory.Anchor(progressArea.rectTransform, 0, 0, 1, 0,
                theme.Space32, theme.Space32, -theme.Space32, 250);
            progressLabel = factory.Label("Playback Progress Label", progressArea.transform,
                "正在读取讲解进度", theme.Body, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(progressLabel.rectTransform, 0, 1, .68f, 1, 22, -50, 0, -14);
            preparationLabel = factory.Label("Playback Preparation Label", progressArea.transform, string.Empty,
                theme.Caption, FontStyle.Bold, theme.Warning, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(preparationLabel.rectTransform, .58f, 1, 1, 1, 0, -50, -22, -14);

            var progressTrack = factory.RoundedImage("Playback Progress Track", progressArea.transform, theme.Border);
            TouchUiFactory.Anchor(progressTrack.rectTransform, 0, 0, 1, 0, 22, 66, -22, 88);
            progressTrack.raycastTarget = false;
            progressFill = factory.RoundedImage("Playback Progress Fill", progressTrack.transform, theme.Primary);
            TouchUiFactory.Anchor(progressFill.rectTransform, 0, 0, 0, 1, 0, 0, 0, 0);
            progressFill.raycastTarget = false;

            var granularity = factory.Label("Playback Progress Granularity", progressArea.transform,
                "节点进度来自服务端，不显示推测的媒体时间", theme.Caption, FontStyle.Normal,
                theme.TextMuted, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(granularity.rectTransform, 0, 0, 1, 0, 22, 18, -22, 54);
        }

        public void Render(TouchUiState state, string verifiedRouteName, IReadOnlyList<string> verifiedModuleIds)
        {
            if (state == null) return;
            var session = state.Session;
            var presentation = PlaybackPresentation.From(state, theme);
            stateBadge.Set(presentation.StateLabel, presentation.Tone);
            stateAccent.color = presentation.Color;
            frame.color = Color.Lerp(theme.Border, presentation.Color, .32f);
            surface.color = presentation.SurfaceColor;

            routeName.text = string.IsNullOrWhiteSpace(verifiedRouteName)
                ? "当前讲解任务" : verifiedRouteName;
            moduleName.text = string.IsNullOrWhiteSpace(session?.moduleName)
                ? presentation.EmptyModuleLabel : session.moduleName;
            nodeName.text = string.IsNullOrWhiteSpace(session?.nodeName)
                ? presentation.EmptyNodeLabel : session.nodeName;

            RenderVerifiedModuleContext(session, verifiedModuleIds);
            RenderProgress(session);
        }

        public void RefreshTheme()
        {
            stateBadge.RefreshTheme();
            progressFill.color = theme.Primary;
        }

        private void RenderVerifiedModuleContext(PlaybackSessionStatus session, IReadOnlyList<string> verifiedModuleIds)
        {
            moduleSequence.text = string.Empty;
            if (session == null || verifiedModuleIds == null || verifiedModuleIds.Count == 0) return;

            var currentIndex = IndexOf(verifiedModuleIds, session.moduleId);
            if (currentIndex < 0) return;
            moduleSequence.text = "主题进度 · " + (currentIndex + 1) + " / " + verifiedModuleIds.Count;
        }

        private void RenderProgress(PlaybackSessionStatus session)
        {
            var progress = session == null || session.totalNodes <= 0
                ? 0 : Mathf.Clamp01((float)session.currentNodeNumber / session.totalNodes);
            progressFill.rectTransform.anchorMax = new Vector2(progress, 1);
            progressFill.rectTransform.offsetMin = Vector2.zero;
            progressFill.rectTransform.offsetMax = Vector2.zero;
            progressLabel.text = session == null || session.totalNodes <= 0
                ? "正在读取讲解进度"
                : "整条路线节点进度 · " + session.currentNodeNumber + " / " + session.totalNodes;

            preparationLabel.text = session != null && !session.playPublished && session.preparationProgress > 0
                ? "素材准备 " + Mathf.Clamp01((float)session.preparationProgress).ToString("P0")
                : string.Empty;
        }

        private static int IndexOf(IReadOnlyList<string> values, string id)
        {
            for (var index = 0; index < values.Count; index++)
                if (string.Equals(values[index], id, StringComparison.OrdinalIgnoreCase)) return index;
            return -1;
        }

        private sealed class PlaybackPresentation
        {
            public string StateLabel;
            public string EmptyModuleLabel;
            public string EmptyNodeLabel;
            public StatusTone Tone;
            public Color Color;
            public Color SurfaceColor;

            public static PlaybackPresentation From(TouchUiState state, TouchTheme theme)
            {
                if (!state.Connected)
                    return Create("服务连接异常", "正在恢复讲解状态", "连接恢复后将自动更新当前节点",
                        StatusTone.Error, theme.Error, theme);
                if (!state.HasActiveSession)
                    return Create("讲解已结束", "本次讲解已结束", "可以返回接待首页开始新的讲解",
                        StatusTone.Neutral, theme.TextSecondary, theme);
                if (state.Session == null)
                    return Create("正在恢复讲解", "正在恢复当前主题", "正在向服务器读取真实讲解状态",
                        StatusTone.Warning, theme.Warning, theme);
                if (state.Session.paused)
                    return Create("讲解已暂停", "当前主题", "当前节点",
                        StatusTone.Warning, theme.Warning, theme);
                if (!state.Session.playPublished)
                    return Create("正在准备播放", "正在准备当前主题", "正在同步大屏与语音，请稍候…",
                        StatusTone.Info, theme.Primary, theme);
                return Create("正在正常讲解", "当前主题", "当前节点",
                    StatusTone.Success, theme.Success, theme);
            }

            private static PlaybackPresentation Create(string label, string emptyModule, string emptyNode,
                StatusTone tone, Color color, TouchTheme theme) => new PlaybackPresentation
            {
                StateLabel = label,
                EmptyModuleLabel = emptyModule,
                EmptyNodeLabel = emptyNode,
                Tone = tone,
                Color = color,
                SurfaceColor = Color.Lerp(theme.SurfaceElevated, color, tone == StatusTone.Warning ? .10f : .045f)
            };
        }
    }
}
