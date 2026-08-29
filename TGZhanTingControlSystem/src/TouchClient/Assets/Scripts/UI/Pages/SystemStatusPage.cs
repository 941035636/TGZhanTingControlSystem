using System;
using System.Linq;
using TG.Control.Touch.UI.Components;
using TG.Control.Touch.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Pages
{
    /// <summary>
    /// Field-operator status center. It renders the presenter snapshot and emits navigation intent only;
    /// it owns no polling, API access, readiness policy or playback behavior.
    /// </summary>
    public sealed class SystemStatusPage
    {
        private readonly TouchTheme theme;
        private readonly RectTransform root;
        private readonly ReadinessSummaryPanel summary;
        private readonly SystemHealthCard serverCard;
        private readonly SystemHealthCard ledCard;
        private readonly SystemHealthCard contentCard;
        private readonly SystemHealthCard narrationCard;
        private readonly Image sessionFrame;
        private readonly Image sessionSurface;
        private readonly Text sessionTitle;
        private readonly Text sessionDetail;
        private readonly Button viewPlaybackButton;

        public RectTransform Root => root;
        public event Action ViewPlaybackRequested;

        public SystemStatusPage(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            root = factory.Rect("System Status Page", parent);
            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = theme.SectionSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            summary = new ReadinessSummaryPanel(factory, theme, root);

            var gridRoot = factory.Rect("System Health Grid", root);
            var gridElement = gridRoot.gameObject.AddComponent<LayoutElement>();
            gridElement.minHeight = theme.SystemHealthCardHeight * 2 + theme.SectionSpacing;
            gridElement.preferredHeight = gridElement.minHeight;
            gridElement.flexibleHeight = 1;
            var grid = gridRoot.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(795, theme.SystemHealthCardHeight);
            grid.spacing = new Vector2(theme.SectionSpacing, theme.SectionSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;

            serverCard = new SystemHealthCard(factory, theme, gridRoot, "Server 服务", "01");
            ledCard = new SystemHealthCard(factory, theme, gridRoot, "LED 播放端", "02");
            contentCard = new SystemHealthCard(factory, theme, gridRoot, "正式内容", "03");
            narrationCard = new SystemHealthCard(factory, theme, gridRoot, "讲解服务", "04");

            sessionFrame = factory.RoundedImage("Active Narration Summary", root, theme.Border);
            var sessionElement = sessionFrame.gameObject.AddComponent<LayoutElement>();
            sessionElement.minHeight = theme.SystemStatusSessionHeight;
            sessionElement.preferredHeight = theme.SystemStatusSessionHeight;
            sessionElement.flexibleHeight = 0;
            sessionSurface = factory.RoundedImage("Active Narration Surface", sessionFrame.transform, theme.PrimarySoft);
            TouchUiFactory.Stretch(sessionSurface.rectTransform, 1, 1, -1, -1);
            var accent = factory.Image("Active Narration Accent", sessionSurface.transform, theme.Primary);
            TouchUiFactory.Anchor(accent.rectTransform, 0, 0, 0, 1, 0, 0, 6, 0);
            sessionTitle = factory.Label("Active Narration Title", sessionSurface.transform,
                "当前有讲解任务正在执行", theme.CardTitle, FontStyle.Bold,
                theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(sessionTitle.rectTransform, 0, 1, .68f, 1,
                theme.PanelPadding, -56, 0, -14);
            sessionDetail = factory.Label("Active Narration Detail", sessionSurface.transform,
                string.Empty, theme.Body, FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(sessionDetail.rectTransform, 0, 0, .72f, 1,
                theme.PanelPadding, 18, 0, -58);
            viewPlaybackButton = factory.TouchButton(sessionSurface.transform, "查看当前讲解", true,
                () => ViewPlaybackRequested?.Invoke());
            TouchUiFactory.Anchor(viewPlaybackButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -260, -theme.ButtonHeight * .5f, -theme.PanelPadding, theme.ButtonHeight * .5f);
            sessionFrame.gameObject.SetActive(false);
        }

        public void Render(TouchUiState state, string verifiedRouteName)
        {
            if (state == null) return;
            summary.Render(state);
            RenderServer(state);
            RenderLed(state);
            RenderContent(state);
            RenderNarration(state);
            RenderActiveSession(state, verifiedRouteName);
        }

        public void RefreshTheme()
        {
            summary.RefreshTheme();
            serverCard.RefreshTheme();
            ledCard.RefreshTheme();
            contentCard.RefreshTheme();
            narrationCard.RefreshTheme();
            sessionFrame.color = theme.Border;
            sessionSurface.color = theme.PrimarySoft;
        }

        private void RenderServer(TouchUiState state)
        {
            if (state.Connected)
            {
                serverCard.Render("服务在线", "触控终端已连接展厅服务，状态变化会自动同步到当前页面。",
                    StatusTone.Success, "连接状态：在线", "自动状态更新正常");
                return;
            }
            serverCard.Render("服务连接中断", "系统正在自动重连；连接恢复前无法确认LED、内容和讲解状态。",
                StatusTone.Error, "连接状态：离线", "无需重复点击刷新");
        }

        private void RenderLed(TouchUiState state)
        {
            var readiness = state.Readiness;
            if (!state.Connected || readiness == null)
            {
                ledCard.Render("状态暂不可用", "连接展厅服务后将自动检查LED播放端。",
                    StatusTone.Neutral, "在线状态：待确认", "就绪状态：待确认");
                return;
            }
            if (!readiness.ledOnline)
            {
                ledCard.Render("LED播放端离线", "暂时无法开始新的自动讲解，请检查LED播放主机。",
                    StatusTone.Error, "在线状态：离线", "就绪状态：未就绪");
                return;
            }
            var versionMatches = readiness.contentVersion > 0 &&
                                 readiness.ledContentVersion == readiness.contentVersion;
            if (readiness.ledReady && versionMatches)
            {
                ledCard.Render("播放端已就绪", "LED播放端在线，当前正式内容已准备完成。",
                    StatusTone.Success, "在线状态：在线",
                    "内容版本：V" + readiness.ledContentVersion);
                return;
            }
            var detail = versionMatches
                ? "LED播放端在线，但素材准备尚未完成；完成后状态会自动更新。"
                : "LED播放端内容版本与当前正式内容不一致，正在等待素材同步。";
            ledCard.Render("播放端未就绪", detail, StatusTone.Warning, "在线状态：在线",
                "LED V" + readiness.ledContentVersion + " / 正式 V" + readiness.contentVersion);
        }

        private void RenderContent(TouchUiState state)
        {
            var content = state.Content;
            var modules = content?.modules;
            if (content != null && content.version > 0)
            {
                var total = modules?.Length ?? 0;
                var enabled = modules?.Count(module => module != null && module.enabled) ?? 0;
                contentCard.Render("正式内容 V" + content.version,
                    "触控终端已加载当前发布版本，可用于路线与讲解。", StatusTone.Success,
                    "模块数量：" + total, "已启用：" + enabled);
                return;
            }
            var serverVersion = state.Readiness?.contentVersion ?? 0;
            if (serverVersion > 0)
            {
                contentCard.Render("内容正在加载", "服务器已有正式内容，触控终端正在获取当前版本。",
                    StatusTone.Warning, "服务器版本：V" + serverVersion, "触控端：尚未加载");
                return;
            }
            contentCard.Render("暂无正式内容", "请先在管理端完成内容发布，发布后本页会自动更新。",
                StatusTone.Warning, "内容版本：无", "模块数量：0");
        }

        private void RenderNarration(TouchUiState state)
        {
            if (!state.HasActiveSession)
            {
                narrationCard.Render("当前空闲", "当前没有进行中的讲解任务，可以开始新的接待。",
                    StatusTone.Neutral, "活动任务：无", "讲解状态：空闲");
                return;
            }
            var session = state.Session;
            if (session == null)
            {
                narrationCard.Render("讲解状态恢复中", "正在从服务端恢复当前讲解状态，请稍候。",
                    StatusTone.Info, "活动任务：存在", "状态：恢复中");
                return;
            }
            if (session.paused)
            {
                narrationCard.Render("讲解已暂停", "当前讲解任务处于暂停状态，可进入讲解页继续。",
                    StatusTone.Warning, "当前主题：" + Safe(session.moduleName),
                    "当前节点：" + Safe(session.nodeName));
                return;
            }
            if (!session.playPublished)
            {
                var progress = Math.Max(0, Math.Min(100, session.preparationProgress * 100));
                narrationCard.Render("讲解准备中", "正在等待播放端完成本节点准备，请勿重复发起任务。",
                    StatusTone.Info, "当前主题：" + Safe(session.moduleName),
                    "准备进度：" + progress.ToString("0") + "%");
                return;
            }
            narrationCard.Render("讲解进行中", "当前任务正在按既定路线执行，可进入讲解页查看和控制。",
                StatusTone.Info, "当前主题：" + Safe(session.moduleName),
                "当前节点：" + Safe(session.nodeName));
        }

        private void RenderActiveSession(TouchUiState state, string verifiedRouteName)
        {
            sessionFrame.gameObject.SetActive(state.HasActiveSession);
            if (!state.HasActiveSession) return;
            var session = state.Session;
            var stateLabel = session == null ? "状态恢复中" : session.paused ? "已暂停" :
                session.playPublished ? "正在讲解" : "准备中";
            sessionTitle.text = "当前有讲解任务正在执行 · " + stateLabel;
            if (session == null)
            {
                sessionDetail.text = "正在恢复任务详情，进入当前讲解可查看最新状态。";
            }
            else
            {
                var route = string.IsNullOrWhiteSpace(verifiedRouteName) ? string.Empty :
                    "路线：" + verifiedRouteName + " · ";
                sessionDetail.text = route + "主题：" + Safe(session.moduleName) + " · 节点：" + Safe(session.nodeName);
            }
            viewPlaybackButton.interactable = state.HasActiveSession;
        }

        private static string Safe(string value) => string.IsNullOrWhiteSpace(value) ? "状态同步中" : value.Trim();
    }
}
