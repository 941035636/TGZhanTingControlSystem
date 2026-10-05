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

            pageTitle = factory.Label("Kiosk Title", root, "选择讲解板块", theme.PageTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(pageTitle.rectTransform, 0, 1, .7f, 1, 0, -52, 0, 0);
            pageSubtitle = factory.Label("Kiosk Subtitle", root, "轻触板块后开始讲解，或按顺序讲解全部内容", theme.Body,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(pageSubtitle.rectTransform, 0, 1, .72f, 1, 0, -84, 0, -50);

            var gridFrame = factory.RoundedImage("Module Gallery Frame", root, new Color(theme.SurfaceGlass.r, theme.SurfaceGlass.g, theme.SurfaceGlass.b, .76f));
            TouchUiFactory.Anchor(gridFrame.rectTransform, 0, 0, 1, 1, 0, 0, -386, -102);
            grid = factory.ScrollGrid(gridFrame.transform, "Module Gallery", 3, new Vector2(382, 278), new Vector2(16, 16));
            TouchUiFactory.Anchor(grid.parent.GetComponent<RectTransform>(), 0, 0, 1, 1, 22, 22, -22, -22);

            var actionPanel = factory.RoundedImage("Selected Module Actions", root,
                new Color(theme.SurfaceElevated.r, theme.SurfaceElevated.g, theme.SurfaceElevated.b, .94f));
            TouchUiFactory.Anchor(actionPanel.rectTransform, 1, 0, 1, 1, -362, 0, 0, -102);
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
                var surface = factory.RoundedImage("Module - " + module.name, grid, theme.SurfaceSoft);
                var cover = factory.Image("Module Cover", surface.transform, Color.clear);
                TouchUiFactory.Stretch(cover.rectTransform);
                cover.raycastTarget = false;
                if (!string.IsNullOrWhiteSpace(module.coverUrl))
                    imageLoader.Load(cover, resolveUrl?.Invoke(module.coverUrl) ?? module.coverUrl);
                var shade = factory.Image("Module Cover Shade", surface.transform, new Color(.01f, .04f, .09f, .48f));
                TouchUiFactory.Stretch(shade.rectTransform);
                shade.raycastTarget = false;
                var bottom = factory.Image("Module Title Shade", surface.transform, new Color(.01f, .04f, .09f, .84f));
                TouchUiFactory.Anchor(bottom.rectTransform, 0, 0, 1, 0, 0, 0, 0, 78);
                bottom.raycastTarget = false;
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
                cards.Add(new ModuleCard { Module = module, Surface = surface, Cover = cover, Border = border, Title = title });
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
                card.Surface.color = active ? theme.PrimaryMuted : theme.SurfaceSoft;
                if (active) selected = card.Module;
            }
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
