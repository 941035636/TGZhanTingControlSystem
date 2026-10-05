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
    /// <summary>Large-cover, one-touch module selection designed for reception operators.</summary>
    public sealed class ModuleKioskHomePage
    {
        private sealed class ModuleCard
        {
            public ExhibitionModule Module;
            public Image Surface;
            public Image Cover;
            public Image Placeholder;
            public Image Shade;
            public Image Border;
            public Text Title;
        }

        private readonly TouchUiFactory factory;
        private readonly TouchTheme theme;
        private readonly TouchImageLoader imageLoader;
        private readonly List<ModuleCard> cards = new List<ModuleCard>();
        private readonly RectTransform root;
        private readonly RectTransform grid;
        private readonly Text pageTitle;
        private readonly Text pageSubtitle;
        private readonly Text galleryEmptyTitle;
        private readonly Text galleryEmptyDescription;
        private readonly GameObject galleryEmptyState;
        private readonly GameObject selectionBeacon;
        private readonly Text selectedTitle;
        private readonly Text selectedDescription;
        private readonly Text readinessText;
        private readonly Button startButton;
        private readonly Button startAllButton;
        private string selectedModuleId;
        private string signature;
        private Func<string, string> resolveUrl;
        private TouchUiState lastState;

        public RectTransform Root => root;
        public event Action<ExhibitionModule> ModuleStartRequested;
        public event Action StartAllRequested;

        public ModuleKioskHomePage(TouchUiFactory factory, TouchTheme theme, TouchImageLoader imageLoader, Transform parent)
        {
            this.factory = factory;
            this.theme = theme;
            this.imageLoader = imageLoader;
            root = factory.Rect("Module Kiosk Home Page", parent);

            var modeLabel = factory.Label("Kiosk Mode Label", root, "智能讲解  ·  模块点播", theme.Caption,
                FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(modeLabel.rectTransform, 0, 1, .5f, 1, 0, -28, 0, -4);

            pageTitle = factory.Label("Kiosk Title", root, "选择讲解板块", 34,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(pageTitle.rectTransform, 0, 1, .7f, 1, 0, -78, 0, -28);
            pageSubtitle = factory.Label("Kiosk Subtitle", root, "轻触板块后开始讲解，或按顺序讲解全部内容", theme.Body,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(pageSubtitle.rectTransform, 0, 1, .72f, 1, 0, -108, 0, -78);

            var titleRule = factory.Image("Kiosk Title Accent", root,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .82f));
            TouchUiFactory.Anchor(titleRule.rectTransform, 0, 1, 0, 1, 0, -122, 132, -118);
            titleRule.raycastTarget = false;

            var gridOutline = factory.RoundedImage("Module Gallery Outline", root,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .42f));
            TouchUiFactory.Anchor(gridOutline.rectTransform, 0, 0, 1, 1, 0, 0, -386, -132);
            var gridFrame = factory.RoundedImage("Module Gallery Frame", gridOutline.transform,
                Color.clear);
            TouchUiFactory.Stretch(gridFrame.rectTransform, 2, 2, -2, -2);
            grid = factory.ScrollGrid(gridFrame.transform, "Module Gallery", 3, new Vector2(382, 278), new Vector2(16, 16));
            TouchUiFactory.Anchor(grid.parent.GetComponent<RectTransform>(), 0, 0, 1, 1, 22, 22, -22, -22);
            galleryEmptyState = factory.Rect("Module Gallery Empty State", gridFrame.transform).gameObject;
            TouchUiFactory.Stretch(galleryEmptyState.GetComponent<RectTransform>());
            var emptyHaloOuter = factory.RoundedImage("Module Gallery Empty Halo Outer", galleryEmptyState.transform,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .10f));
            TouchUiFactory.Anchor(emptyHaloOuter.rectTransform, .5f, .5f, .5f, .5f, -158, -158, 158, 158);
            emptyHaloOuter.raycastTarget = false;
            var emptyHaloInner = factory.RoundedImage("Module Gallery Empty Halo Inner", galleryEmptyState.transform,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .16f));
            TouchUiFactory.Anchor(emptyHaloInner.rectTransform, .5f, .5f, .5f, .5f, -104, -104, 104, 104);
            emptyHaloInner.raycastTarget = false;
            galleryEmptyTitle = factory.Label("Module Gallery Empty Title", galleryEmptyState.transform,
                "正在连接展厅内容", 30, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(galleryEmptyTitle.rectTransform, .2f, .5f, .8f, .5f, 0, -20, 0, 28);
            galleryEmptyDescription = factory.Label("Module Gallery Empty Description", galleryEmptyState.transform,
                "连接完成后将在这里展示可讲解板块", theme.Body, FontStyle.Normal, theme.TextSecondary,
                TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(galleryEmptyDescription.rectTransform, .2f, .5f, .8f, .5f, 0, -58, 0, -18);
            galleryEmptyState.SetActive(false);

            var actionOutline = factory.RoundedImage("Selected Module Outline", root,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .48f));
            TouchUiFactory.Anchor(actionOutline.rectTransform, 1, 0, 1, 1, -362, 0, 0, -132);
            var actionPanel = factory.RoundedImage("Selected Module Actions", actionOutline.transform,
                new Color(theme.SurfaceElevated.r, theme.SurfaceElevated.g, theme.SurfaceElevated.b, .26f));
            TouchUiFactory.Stretch(actionPanel.rectTransform, 2, 2, -2, -2);
            var actionTopLine = factory.Image("Selected Module Top Accent", actionPanel.transform,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .88f));
            TouchUiFactory.Anchor(actionTopLine.rectTransform, 0, 1, 1, 1, 26, -14, -26, -10);
            actionTopLine.raycastTarget = false;
            var eyebrow = factory.Label("Action Eyebrow", actionPanel.transform, "当前选择", theme.Caption,
                FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(eyebrow.rectTransform, 0, 1, 1, 1, 28, -56, -28, -24);
            selectedTitle = factory.Label("Selected Module", actionPanel.transform, "请选择讲解板块", 30,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(selectedTitle.rectTransform, 0, 1, 1, 1, 28, -130, -28, -62);
            selectedDescription = factory.Label("Selected Module Description", actionPanel.transform,
                "从左侧选择一个板块，即可开始单独讲解。", theme.Body, FontStyle.Normal,
                theme.TextSecondary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(selectedDescription.rectTransform, 0, .46f, 1, 1, 28, 0, -28, -144);
            selectionBeacon = factory.Rect("Selected Module Beacon", actionPanel.transform).gameObject;
            var beaconOuter = factory.RoundedImage("Selected Module Beacon Outer", selectionBeacon.transform,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .10f));
            TouchUiFactory.Anchor(beaconOuter.rectTransform, .5f, .48f, .5f, .48f, -88, -88, 88, 88);
            beaconOuter.raycastTarget = false;
            var beaconInner = factory.RoundedImage("Selected Module Beacon Inner", selectionBeacon.transform,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .16f));
            TouchUiFactory.Anchor(beaconInner.rectTransform, .5f, .48f, .5f, .48f, -48, -48, 48, 48);
            beaconInner.raycastTarget = false;
            var beaconLine = factory.Image("Selected Module Beacon Line", selectionBeacon.transform,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .72f));
            TouchUiFactory.Anchor(beaconLine.rectTransform, .5f, .48f, .5f, .48f, -22, -1, 22, 1);
            beaconLine.raycastTarget = false;
            selectionBeacon.transform.SetAsFirstSibling();
            readinessText = factory.Label("Kiosk Readiness", actionPanel.transform, "正在检查系统状态…", theme.Secondary,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.LowerLeft);
            TouchUiFactory.Anchor(readinessText.rectTransform, 0, 0, 1, .46f, 28, 178, -28, 0);

            startButton = factory.TouchButton(actionPanel.transform, "开始讲解", true, StartSelected);
            TouchUiFactory.Anchor(startButton.GetComponent<RectTransform>(), 0, 0, 1, 0, 28, 98, -28, 166);
            startAllButton = factory.TouchButton(actionPanel.transform, "全部讲解", false,
                () => StartAllRequested?.Invoke());
            TouchUiFactory.Anchor(startAllButton.GetComponent<RectTransform>(), 0, 0, 1, 0, 28, 24, -28, 86);
        }

        public void Render(TouchUiState state, Func<string, string> urlResolver)
        {
            if (state == null) return;
            lastState = state;
            resolveUrl = urlResolver;
            var titleOverride = Find(state.UiExperience?.touchElements, "home.kiosk.title");
            var subtitleOverride = Find(state.UiExperience?.touchElements, "home.kiosk.subtitle");
            pageTitle.text = string.IsNullOrWhiteSpace(titleOverride?.text) ? "选择讲解板块" : titleOverride.text;
            pageSubtitle.text = string.IsNullOrWhiteSpace(subtitleOverride?.text)
                ? "轻触板块后开始讲解，或按顺序讲解全部内容" : subtitleOverride.text;

            var modules = state.Content?.modules?.Where(item => item != null && item.enabled)
                .OrderBy(item => item.order).ToArray() ?? Array.Empty<ExhibitionModule>();
            UpdateGalleryEmptyState(state, modules.Length);
            var newSignature = BuildSignature(state, modules);
            if (!string.Equals(signature, newSignature, StringComparison.Ordinal))
            {
                signature = newSignature;
                Rebuild(modules);
            }
            if (modules.Length > 0 && !modules.Any(item => item.id == selectedModuleId))
                selectedModuleId = modules[0].id;
            ApplySelection();
            var canStart = state.Connected && state.Readiness?.canStart == true && !state.HasActiveSession;
            startButton.interactable = canStart && modules.Any(item => item.id == selectedModuleId && HasContent(item));
            startAllButton.interactable = canStart && modules.Any(HasContent);
            readinessText.text = state.HasActiveSession ? "当前已有讲解正在进行，请进入“当前讲解”操作。"
                : !state.Connected ? "服务器正在连接，请稍候。"
                : state.Readiness?.canStart == true ? "系统已就绪，可以开始讲解。"
                : state.Readiness?.message ?? "LED大屏尚未就绪。";
        }

        public void RefreshTheme() => ApplySelection();

        private void Rebuild(ExhibitionModule[] modules)
        {
            cards.Clear();
            TouchUiFactory.Clear(grid);
            foreach (var module in modules)
            {
                var surface = factory.RoundedImage("Module - " + module.name, grid,
                    new Color(theme.SurfaceGlass.r, theme.SurfaceGlass.g, theme.SurfaceGlass.b, .62f));
                var placeholder = factory.Image("Module Technology Placeholder", surface.transform,
                    new Color(theme.PrimarySoft.r, theme.PrimarySoft.g, theme.PrimarySoft.b, .74f));
                TouchUiFactory.Stretch(placeholder.rectTransform);
                placeholder.raycastTarget = false;
                BuildPlaceholderVisual(placeholder.transform);
                var cover = factory.Image("Module Cover", surface.transform, Color.clear);
                TouchUiFactory.Stretch(cover.rectTransform);
                cover.raycastTarget = false;
                var shade = factory.Image("Module Cover Shade", surface.transform,
                    new Color(.01f, .06f, .14f, string.IsNullOrWhiteSpace(module.coverUrl) ? .12f : .42f));
                TouchUiFactory.Stretch(shade.rectTransform);
                shade.raycastTarget = false;
                if (!string.IsNullOrWhiteSpace(module.coverUrl))
                {
                    imageLoader.Load(cover, resolveUrl?.Invoke(module.coverUrl) ?? module.coverUrl, success =>
                    {
                        if (placeholder != null) placeholder.gameObject.SetActive(!success);
                        if (shade != null) shade.color = new Color(.01f, .06f, .14f, success ? .42f : .12f);
                    });
                }
                var bottom = factory.Image("Module Title Shade", surface.transform, new Color(.01f, .055f, .13f, .76f));
                TouchUiFactory.Anchor(bottom.rectTransform, 0, 0, 1, 0, 0, 0, 0, 78);
                bottom.raycastTarget = false;
                var topLine = factory.Image("Module Card Top Accent", surface.transform,
                    new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .38f));
                TouchUiFactory.Anchor(topLine.rectTransform, 0, 1, 1, 1, 18, -10, -18, -7);
                topLine.raycastTarget = false;
                var title = factory.Label("Module Title", surface.transform, module.name, 24, FontStyle.Bold,
                    theme.TextPrimary, TextAnchor.MiddleLeft);
                TouchUiFactory.Anchor(title.rectTransform, 0, 0, 1, 0, 24, 10, -24, 68);
                var border = factory.RoundedImage("Selected Border", surface.transform, theme.ConfigurableAccent);
                TouchUiFactory.Stretch(border.rectTransform, -3, -3, 3, 3);
                border.raycastTarget = false;
                border.transform.SetAsFirstSibling();
                var button = surface.gameObject.AddComponent<Button>();
                button.targetGraphic = surface;
                var captured = module;
                button.onClick.AddListener(() => { selectedModuleId = captured.id; ApplySelection(); });
                cards.Add(new ModuleCard { Module = module, Surface = surface, Cover = cover, Placeholder = placeholder,
                    Shade = shade, Border = border, Title = title });
            }
            Canvas.ForceUpdateCanvases();
            var scroll = grid.parent.GetComponent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1;
        }

        private void ApplySelection()
        {
            ExhibitionModule selected = null;
            foreach (var card in cards)
            {
                var active = string.Equals(card.Module.id, selectedModuleId, StringComparison.OrdinalIgnoreCase);
                card.Border.gameObject.SetActive(active);
                card.Surface.color = active
                    ? new Color(theme.PrimarySoft.r, theme.PrimarySoft.g, theme.PrimarySoft.b, .88f)
                    : new Color(theme.SurfaceGlass.r, theme.SurfaceGlass.g, theme.SurfaceGlass.b, .62f);
                if (active) selected = card.Module;
            }
            selectionBeacon.SetActive(selected == null);
            selectedTitle.text = selected?.name ?? "请选择讲解板块";
            selectedDescription.text = selected == null ? "从左侧选择一个板块，即可开始单独讲解。"
                : string.IsNullOrWhiteSpace(selected.description) ? "点击“开始讲解”播放本板块内容。" : selected.description;
        }

        private void StartSelected()
        {
            var selected = cards.FirstOrDefault(card => string.Equals(card.Module.id, selectedModuleId,
                StringComparison.OrdinalIgnoreCase))?.Module;
            if (selected != null && startButton.interactable) ModuleStartRequested?.Invoke(selected);
        }

        private static bool HasContent(ExhibitionModule module) => module?.nodes != null && module.nodes.Any(node =>
            node != null && (!string.IsNullOrWhiteSpace(node.ttsAudioUrl) ||
                             node.assets?.Any(asset => asset != null && !string.IsNullOrWhiteSpace(asset.url) &&
                                                               (asset.kind == 0 || asset.kind == 2)) == true));

        private void BuildPlaceholderVisual(Transform parent)
        {
            var glow = factory.Image("Placeholder Glow", parent,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .16f));
            TouchUiFactory.Anchor(glow.rectTransform, .24f, .22f, .76f, .78f, 0, 0, 0, 0);
            glow.raycastTarget = false;

            for (var index = 0; index < 3; index++)
            {
                var horizontal = factory.Image("Placeholder Horizontal Line", parent,
                    new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .18f));
                var y = .34f + index * .14f;
                TouchUiFactory.Anchor(horizontal.rectTransform, .17f, y, .83f, y, 0, 0, 0, 1);
                horizontal.raycastTarget = false;

                var vertical = factory.Image("Placeholder Vertical Line", parent,
                    new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .12f));
                var x = .35f + index * .15f;
                TouchUiFactory.Anchor(vertical.rectTransform, x, .20f, x, .80f, 0, 0, 1, 0);
                vertical.raycastTarget = false;
            }
        }

        private void UpdateGalleryEmptyState(TouchUiState state, int moduleCount)
        {
            var noModules = moduleCount == 0;
            galleryEmptyState.SetActive(noModules);
            if (!noModules) return;

            var connected = state?.Connected == true;
            galleryEmptyTitle.text = connected ? "暂未发布可讲解板块" : "正在连接展厅内容";
            galleryEmptyDescription.text = connected
                ? "请在管理端发布内容后重新进入此页"
                : "连接完成后将在这里展示可讲解板块";
        }

        private static UiElementOverride Find(UiElementOverride[] items, string key) => items?.FirstOrDefault(item =>
            item != null && string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase));

        private static string BuildSignature(TouchUiState state, IEnumerable<ExhibitionModule> modules)
        {
            var builder = new StringBuilder().Append(state.Content?.version).Append('|');
            foreach (var item in modules) builder.Append(item.id).Append(':').Append(item.name).Append(':')
                .Append(item.description).Append(':').Append(item.coverUrl).Append(';');
            return builder.ToString();
        }
    }
}
