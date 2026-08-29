using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TG.Control.Touch.UI.Components;
using TG.Control.Touch.UI.Services;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Pages
{
    /// <summary>
    /// Commercial reception workbench. It renders TouchUiState and emits operator intent only;
    /// no API, polling, route persistence or playback coordination lives here.
    /// </summary>
    public sealed class ReceptionHomePage
    {
        private readonly TouchUiFactory factory;
        private readonly TouchTheme theme;
        private readonly TouchImageLoader imageLoader;
        private readonly List<RouteCard> routeCardViews = new List<RouteCard>();
        private readonly RectTransform root;
        private readonly Image heroFrame;
        private readonly Image heroSurface;
        private readonly Image heroImage;
        private readonly GameObject heroPlaceholder;
        private readonly Image heroAccent;
        private readonly Text heroEyebrow;
        private readonly Text heroTitle;
        private readonly Text heroSubtitle;
        private readonly Text receptionState;
        private readonly Text receptionDetail;
        private readonly Button continueButton;
        private readonly SystemStatusPanel systemStatus;
        private readonly Button temporaryButton;
        private readonly Button startAllButton;
        private readonly GameObject activeQuickNotice;
        private readonly RectTransform routeGrid;
        private readonly Text routeCaption;
        private readonly ErrorBanner errorBanner;
        private string loadedHeroUrl;
        private string routeSignature;
        private Func<string, string> assetUrlResolver;

        public RectTransform Root => root;
        public event Action TemporaryRequested;
        public event Action StartAllRequested;
        public event Action ContinuePlaybackRequested;
        public event Action<NarrationRoute> RouteStartRequested;
        public event Action<NarrationRoute> RouteEditRequested;

        public ReceptionHomePage(TouchUiFactory factory, TouchTheme theme, TouchImageLoader imageLoader, Transform parent)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            this.imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));

            root = factory.Rect("Reception Home Page", parent);
            var pageLayout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            pageLayout.spacing = theme.SectionSpacing;
            pageLayout.childControlWidth = true;
            pageLayout.childControlHeight = true;
            pageLayout.childForceExpandHeight = false;

            var topRow = factory.Rect("Reception Overview", root);
            var topElement = topRow.gameObject.AddComponent<LayoutElement>();
            topElement.minHeight = theme.HomeHeroHeight;
            topElement.preferredHeight = theme.HomeHeroHeight;
            topElement.flexibleHeight = 0;
            var topLayout = topRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            topLayout.spacing = theme.SectionSpacing;
            topLayout.childControlHeight = true;
            topLayout.childControlWidth = true;
            topLayout.childForceExpandWidth = false;

            heroFrame = factory.RoundedImage("Reception Hero Frame", topRow, theme.Border);
            heroFrame.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            heroSurface = factory.RoundedImage("Reception Hero", heroFrame.transform, theme.SurfaceSoft);
            TouchUiFactory.Stretch(heroSurface.rectTransform, 1, 1, -1, -1);
            var heroMask = heroSurface.gameObject.AddComponent<Mask>();
            heroMask.showMaskGraphic = true;

            heroPlaceholder = factory.Rect("Hero Technology Placeholder", heroSurface.transform).gameObject;
            TouchUiFactory.Stretch(heroPlaceholder.GetComponent<RectTransform>());
            BuildHeroPlaceholder(heroPlaceholder.transform);

            heroImage = factory.Image("Configurable Hero Image", heroSurface.transform, Color.clear);
            TouchUiFactory.Stretch(heroImage.rectTransform);
            heroImage.raycastTarget = false;
            var overlay = factory.Image("Hero Contrast Overlay", heroSurface.transform, theme.HeroOverlay);
            TouchUiFactory.Stretch(overlay.rectTransform);
            overlay.raycastTarget = false;

            heroAccent = factory.Image("Hero Configurable Accent", heroSurface.transform, theme.ConfigurableAccent);
            TouchUiFactory.Anchor(heroAccent.rectTransform, 0, 1, 0, 1, 28, -34, 92, -29);
            heroEyebrow = factory.Label("Hero Eyebrow", heroSurface.transform, "智慧展厅 · 接待工作台", theme.Caption,
                FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(heroEyebrow.rectTransform, 0, 1, 1, 1, 28, -64, -28, -36);
            heroTitle = factory.Label("Hero Product Title", heroSurface.transform, "展厅自动讲解系统", theme.H1,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(heroTitle.rectTransform, 0, 1, 1, 1, 28, -112, -28, -66);
            heroTitle.resizeTextForBestFit = true;
            heroTitle.resizeTextMinSize = theme.H2;
            heroTitle.resizeTextMaxSize = theme.H1;
            heroSubtitle = factory.Label("Hero Product Description", heroSurface.transform,
                "选择接待路线，可靠完成语音讲解与LED画面同步", theme.Body,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(heroSubtitle.rectTransform, 0, 1, 1, 1, 28, -148, -28, -114);

            receptionState = factory.Label("Reception State", heroSurface.transform, "状态检查中", theme.H2,
                FontStyle.Bold, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(receptionState.rectTransform, 0, 0, 1, 0, 28, 63, -318, 105);
            receptionDetail = factory.Label("Reception Detail", heroSurface.transform,
                "正在读取展厅服务与LED播放端状态。", theme.Caption, FontStyle.Normal,
                theme.TextSecondary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(receptionDetail.rectTransform, 0, 0, 1, 0, 28, 20, -318, 62);

            continueButton = factory.TouchButton(heroSurface.transform, "继续当前讲解", true,
                () => ContinuePlaybackRequested?.Invoke());
            TouchUiFactory.Anchor(continueButton.GetComponent<RectTransform>(), 1, 0, 1, 0,
                -292, 24, -24, 92);
            continueButton.gameObject.SetActive(false);

            systemStatus = new SystemStatusPanel(factory, theme, topRow);
            var statusElement = systemStatus.Root.gameObject.AddComponent<LayoutElement>();
            statusElement.preferredWidth = theme.HomeStatusPanelWidth;
            statusElement.minWidth = theme.HomeStatusPanelWidth;

            var quickFrame = factory.RoundedImage("Quick Reception Frame", root, theme.Border);
            var quickElement = quickFrame.gameObject.AddComponent<LayoutElement>();
            quickElement.minHeight = theme.HomeQuickActionHeight;
            quickElement.preferredHeight = theme.HomeQuickActionHeight;
            quickElement.flexibleHeight = 0;
            var quickSurface = factory.RoundedImage("Quick Reception Surface", quickFrame.transform, theme.SurfaceElevated);
            TouchUiFactory.Stretch(quickSurface.rectTransform, 1, 1, -1, -1);
            var quickTitle = factory.Label("Quick Reception Title", quickSurface.transform, "快速接待", theme.CardTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(quickTitle.rectTransform, 0, 0, 0, 1,
                theme.PanelPadding, 45, 250, -18);
            var quickCaption = factory.Label("Quick Reception Caption", quickSurface.transform,
                "临时编排主题，或按正式内容顺序完整讲解", theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(quickCaption.rectTransform, 0, 0, 0, 0,
                theme.PanelPadding, 17, 430, 48);

            temporaryButton = factory.TouchButton(quickSurface.transform, "临时组合", false,
                () => TemporaryRequested?.Invoke());
            TouchUiFactory.Anchor(temporaryButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -570, -36, -310, 36);
            startAllButton = factory.TouchButton(quickSurface.transform, "全部主题讲解", true,
                () => StartAllRequested?.Invoke());
            TouchUiFactory.Anchor(startAllButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -294, -36, -20, 36);
            var activeNoticeImage = factory.RoundedImage("Active Session Quick Notice", quickSurface.transform,
                theme.PrimarySoft);
            TouchUiFactory.Anchor(activeNoticeImage.rectTransform, 1, .5f, 1, .5f,
                -570, -36, -20, 36);
            var activeNoticeLabel = factory.Label("Active Session Quick Notice Label", activeNoticeImage.transform,
                "当前讲解进行中 · 结束后可发起新的接待", theme.Body, FontStyle.Bold,
                theme.TextPrimary, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(activeNoticeLabel.rectTransform, 16, 4, -16, -4);
            activeQuickNotice = activeNoticeImage.gameObject;
            activeQuickNotice.SetActive(false);

            var routesFrame = factory.RoundedImage("Saved Routes Frame", root, theme.Border);
            var routesElement = routesFrame.gameObject.AddComponent<LayoutElement>();
            routesElement.minHeight = 501;
            routesElement.flexibleHeight = 1;
            var routesSurface = factory.RoundedImage("Saved Routes Surface", routesFrame.transform, theme.SurfaceElevated);
            TouchUiFactory.Stretch(routesSurface.rectTransform, 1, 1, -1, -1);
            var routeTitle = factory.Label("Saved Routes Title", routesSurface.transform, "常用讲解路线", theme.CardTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(routeTitle.rectTransform, 0, 1, .5f, 1,
                theme.PanelPadding, -56, 0, -14);
            routeCaption = factory.Label("Saved Routes Caption", routesSurface.transform, "正在读取路线…", theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(routeCaption.rectTransform, .5f, 1, 1, 1,
                0, -56, -theme.PanelPadding, -14);
            routeGrid = factory.ScrollGrid(routesSurface.transform, "Reception Route", 2,
                theme.RouteGridCellSize, new Vector2(theme.CardSpacing, theme.CardSpacing));
            TouchUiFactory.Anchor(routeGrid.parent.GetComponent<RectTransform>(), 0, 0, 1, 1,
                theme.PanelPadding, theme.PanelPadding, -theme.PanelPadding, -68);

            errorBanner = new ErrorBanner(factory, theme, root);
            TouchUiFactory.Anchor(errorBanner.Root, 0, 1, 1, 1,
                theme.Space16, -76, -theme.Space16, -theme.Space8);
        }

        public void Render(TouchUiState state, Func<string, string> urlResolver)
        {
            if (state == null) return;
            assetUrlResolver = urlResolver;
            var experience = state.UiExperience;
            heroTitle.text = string.IsNullOrWhiteSpace(experience?.touchTitle)
                ? "展厅自动讲解系统" : experience.touchTitle;
            heroSubtitle.text = string.IsNullOrWhiteSpace(experience?.touchSubtitle)
                ? "选择接待路线，可靠完成语音讲解与LED画面同步" : experience.touchSubtitle;
            SetHeroImage(Resolve(experience?.touchBackgroundUrl));

            var presentation = ReceptionStatePresentation.From(state, theme);
            receptionState.text = presentation.Title;
            receptionState.color = presentation.Color;
            if (state.HasActiveSession)
            {
                var active = state.Session;
                receptionDetail.text = active == null
                    ? "正在恢复讲解状态，点击进入当前讲解查看进度。"
                    : BuildSessionDetail(active);
            }
            else
            {
                receptionDetail.text = presentation.Detail;
            }
            continueButton.gameObject.SetActive(state.HasActiveSession);
            systemStatus.Render(state);

            temporaryButton.gameObject.SetActive(!state.HasActiveSession);
            startAllButton.gameObject.SetActive(!state.HasActiveSession);
            activeQuickNotice.SetActive(state.HasActiveSession);
            temporaryButton.interactable = state.Content != null;
            startAllButton.interactable = CanStart(state) &&
                                          state.Content?.modules?.Any(module => module.enabled && HasContent(module)) == true;
            startAllButton.GetComponent<Image>().color = startAllButton.interactable
                ? theme.Primary : theme.SecondaryButton;
            RebuildRoutesIfNeeded(state);
        }

        public void ShowError(string message) => errorBanner.Show(message);
        public void ClearError() => errorBanner.Hide();

        public void RefreshTheme()
        {
            heroFrame.color = theme.Border;
            heroAccent.color = theme.ConfigurableAccent;
            heroEyebrow.color = theme.ConfigurableAccent;
            systemStatus.RefreshTheme();
            errorBanner.RefreshTheme();
            routeSignature = null;
            foreach (var card in routeCardViews) card.RefreshTheme();
        }

        private void RebuildRoutesIfNeeded(TouchUiState state)
        {
            var signature = BuildRouteSignature(state);
            if (string.Equals(routeSignature, signature, StringComparison.Ordinal)) return;
            routeSignature = signature;
            routeCardViews.Clear();
            TouchUiFactory.Clear(routeGrid);

            var routes = state.Routes ?? Array.Empty<NarrationRoute>();
            routeCaption.text = routes.Length == 0 ? "暂无常用路线" : routes.Length + " 条可用路线";
            if (routes.Length == 0)
            {
                BuildEmptyRoutes(state);
                return;
            }

            var modules = state.Content?.modules ?? Array.Empty<ExhibitionModule>();
            for (var index = 0; index < routes.Length; index++)
            {
                var route = routes[index];
                var ids = route.moduleIds ?? Array.Empty<string>();
                var routeModules = ids.Select(id => modules.FirstOrDefault(module => module.id == id && module.enabled))
                    .Where(module => module != null).ToArray();
                var names = routeModules.Select(module => module.name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Take(4).ToList();
                if (routeModules.Length > names.Count) names.Add("…");
                var coverUrl = Resolve(routeModules.FirstOrDefault(module => !string.IsNullOrWhiteSpace(module.coverUrl))?.coverUrl);
                var card = new RouteCard(factory, theme, imageLoader, routeGrid, route, names.ToArray(), coverUrl,
                    CanStart(state) && routeModules.Any(HasContent), state.HasActiveSession, index);
                card.EditRequested += value => RouteEditRequested?.Invoke(value);
                card.StartRequested += value => RouteStartRequested?.Invoke(value);
                routeCardViews.Add(card);
            }

            Canvas.ForceUpdateCanvases();
            routeGrid.anchoredPosition = new Vector2(routeGrid.anchoredPosition.x, 0);
            var scroll = routeGrid.parent.GetComponent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1;
        }

        private void BuildEmptyRoutes(TouchUiState state)
        {
            var card = factory.RoundedImage("No Saved Routes", routeGrid, theme.SurfaceSoft);
            var title = factory.Label("Empty Route Title", card.transform, "尚未保存常用路线", theme.CardTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, 1, 1, 1, 22, -60, -22, -20);
            var detail = factory.Label("Empty Route Detail", card.transform,
                "使用临时组合选择主题并保存，之后即可在首页一键接待。", theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(detail.rectTransform, 0, 1, 1, 1, 22, -116, -22, -66);
            var create = factory.TouchButton(card.transform, "创建第一条路线", true,
                () => TemporaryRequested?.Invoke());
            TouchUiFactory.Anchor(create.GetComponent<RectTransform>(), 0, 0, 1, 0, 22, 18, -22, 74);
            create.interactable = !state.HasActiveSession && state.Content != null;
        }

        private void SetHeroImage(string url)
        {
            if (string.Equals(loadedHeroUrl, url, StringComparison.OrdinalIgnoreCase)) return;
            loadedHeroUrl = url;
            heroImage.sprite = null;
            heroImage.color = Color.clear;
            heroPlaceholder.SetActive(true);
            if (string.IsNullOrWhiteSpace(url)) return;
            var requestedUrl = url;
            imageLoader.Load(heroImage, url, success =>
            {
                if (!string.Equals(loadedHeroUrl, requestedUrl, StringComparison.OrdinalIgnoreCase)) return;
                heroPlaceholder.SetActive(!success);
                if (!success)
                {
                    heroImage.sprite = null;
                    heroImage.color = Color.clear;
                }
            });
        }

        private void BuildHeroPlaceholder(Transform parent)
        {
            var glowColor = theme.Primary;
            glowColor.a = .24f;
            var glow = factory.RoundedImage("Technology Glow", parent, glowColor);
            TouchUiFactory.Anchor(glow.rectTransform, .55f, -.25f, 1.12f, 1.35f, 0, 0, 0, 0);
            glow.raycastTarget = false;
            for (var i = 0; i < 6; i++)
            {
                var lineColor = theme.Primary;
                lineColor.a = .055f + i * .012f;
                var line = factory.Image("Technology Line " + i, parent, lineColor);
                TouchUiFactory.Anchor(line.rectTransform, .48f + i * .07f, 0, .48f + i * .07f, 1,
                    0, 0, 2, 0);
                line.raycastTarget = false;
            }
            var horizonColor = theme.Primary;
            horizonColor.a = .10f;
            var horizon = factory.Image("Technology Horizon", parent, horizonColor);
            TouchUiFactory.Anchor(horizon.rectTransform, .35f, .30f, 1, .30f, 0, 0, 0, 2);
            horizon.raycastTarget = false;
        }

        private string Resolve(string url) => string.IsNullOrWhiteSpace(url) ? null : assetUrlResolver?.Invoke(url) ?? url;

        private static string BuildSessionDetail(PlaybackSessionStatus session)
        {
            var module = string.IsNullOrWhiteSpace(session.moduleName) ? "正在准备当前主题" : session.moduleName;
            var node = string.IsNullOrWhiteSpace(session.nodeName) ? "正在同步大屏与语音" : session.nodeName;
            var progress = session.totalNodes > 0 ? $"（{session.currentNodeNumber}/{session.totalNodes}）" : string.Empty;
            return module + " · " + node + progress;
        }

        private static bool CanStart(TouchUiState state) => state.Connected && state.Readiness?.canStart == true;
        private static bool HasContent(ExhibitionModule module) => module?.nodes != null &&
            module.nodes.Any(node => node != null &&
                (!string.IsNullOrWhiteSpace(node.ttsAudioUrl) ||
                 node.assets?.Any(asset => asset != null && !string.IsNullOrWhiteSpace(asset.url) &&
                                           (asset.kind == 0 || asset.kind == 2)) == true));

        private static string BuildRouteSignature(TouchUiState state)
        {
            var builder = new StringBuilder();
            builder.Append(state.Connected).Append('|').Append(state.Readiness?.canStart).Append('|')
                .Append(state.HasActiveSession).Append('|').Append(state.Content?.version).Append('|');
            foreach (var route in state.Routes ?? Array.Empty<NarrationRoute>())
                builder.Append(route.id).Append(':').Append(route.name).Append(':')
                    .Append(string.Join(",", route.moduleIds ?? Array.Empty<string>())).Append(';');
            foreach (var module in state.Content?.modules ?? Array.Empty<ExhibitionModule>())
                builder.Append(module.id).Append(':').Append(module.name).Append(':').Append(module.coverUrl).Append(':')
                    .Append(module.enabled).Append(';');
            return builder.ToString();
        }
    }
}
