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
    /// <summary>A complete twelve-module wall with a fixed, easy-to-find playback action.</summary>
    public sealed class ModuleKioskHomePage
    {
        private sealed class ModuleCard
        {
            public ExhibitionModule Module;
            public Image Border;
            public Image Placeholder;
            public Image Cover;
            public Image CoverShade;
            public GameObject SelectedTag;
        }

        private readonly TouchUiFactory factory;
        private readonly TouchTheme theme;
        private readonly TouchImageLoader imageLoader;
        private readonly List<ModuleCard> cards = new List<ModuleCard>();
        private readonly RectTransform root;
        private readonly RectTransform grid;
        private readonly RectTransform gridViewport;
        private readonly GridLayoutGroup gridLayout;
        private readonly Text pageTitle;
        private readonly Text pageSubtitle;
        private readonly Text moduleCount;
        private readonly Text galleryEmptyTitle;
        private readonly Text galleryEmptyDescription;
        private readonly GameObject galleryEmptyState;
        private readonly Text selectedTitle;
        private readonly Text readinessText;
        private readonly Image readinessIndicator;
        private readonly Button startButton;
        private readonly Button startAllButton;
        private Vector2 lastGridSize;
        private string selectedModuleId;
        private string signature;
        private Func<string, string> resolveUrl;

        public RectTransform Root => root;
        public event Action<ExhibitionModule> ModuleStartRequested;
        public event Action StartAllRequested;

        public ModuleKioskHomePage(TouchUiFactory factory, TouchTheme theme, TouchImageLoader imageLoader, Transform parent)
        {
            this.factory = factory;
            this.theme = theme;
            this.imageLoader = imageLoader;
            root = factory.Rect("Module Kiosk Home Page", parent);

            var stage = factory.RoundedImage("Kiosk Dark Glass Stage", root,
                new Color(.018f, .065f, .12f, .91f));
            TouchUiFactory.Stretch(stage.rectTransform);
            stage.raycastTarget = false;

            var topBeam = factory.Image("Kiosk Top Beam", root,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .8f));
            TouchUiFactory.Anchor(topBeam.rectTransform, 0, 1, 0, 1, 28, -3, 214, 0);
            topBeam.raycastTarget = false;

            var headingMark = factory.Image("Kiosk Heading Mark", root, theme.ConfigurableAccent);
            TouchUiFactory.Anchor(headingMark.rectTransform, 0, 1, 0, 1, 28, -89, 33, -35);
            headingMark.raycastTarget = false;
            var modeLabel = factory.Label("Kiosk Mode Label", root, "智慧展厅  /  自助讲解", 18,
                FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(modeLabel.rectTransform, 0, 1, .65f, 1, 50, -45, 0, -13);

            pageTitle = factory.Label("Kiosk Title", root, "选择讲解板块", 38,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(pageTitle.rectTransform, 0, 1, .72f, 1, 50, -99, 0, -48);
            pageTitle.resizeTextForBestFit = true;
            pageTitle.resizeTextMinSize = 28;
            pageTitle.resizeTextMaxSize = 38;
            pageSubtitle = factory.Label("Kiosk Subtitle", root,
                "轻触一张图片选择板块，随后点击下方按钮开始讲解", 19,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(pageSubtitle.rectTransform, 0, 1, .78f, 1, 50, -130, 0, -99);

            var countSurface = factory.RoundedImage("Module Count Surface", root,
                new Color(.035f, .16f, .28f, .88f));
            TouchUiFactory.Anchor(countSurface.rectTransform, 1, 1, 1, 1, -280, -97, -28, -45);
            var countDot = factory.RoundedImage("Module Count Dot", countSurface.transform, theme.ConfigurableAccent);
            TouchUiFactory.Anchor(countDot.rectTransform, 0, .5f, 0, .5f, 22, -5, 32, 5);
            countDot.raycastTarget = false;
            moduleCount = factory.Label("Module Count", countSurface.transform, "正在载入板块", 18,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Stretch(moduleCount.rectTransform, 48, 0, -16, 0);

            var galleryFrame = factory.RoundedImage("Module Gallery", root,
                new Color(.005f, .033f, .075f, .92f));
            TouchUiFactory.Anchor(galleryFrame.rectTransform, 0, 0, 1, 1, 20, 140, -20, -145);
            var galleryEdge = factory.Image("Gallery Edge", galleryFrame.transform,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .34f));
            TouchUiFactory.Anchor(galleryEdge.rectTransform, 0, 1, 1, 1, 18, -2, -18, 0);
            galleryEdge.raycastTarget = false;
            grid = factory.ScrollGrid(galleryFrame.transform, "Module Wall", 4,
                new Vector2(360, 190), new Vector2(14, 14));
            gridViewport = grid.parent.GetComponent<RectTransform>();
            TouchUiFactory.Anchor(gridViewport, 0, 0, 1, 1, 14, 14, -14, -14);
            gridLayout = grid.GetComponent<GridLayoutGroup>();
            gridLayout.padding = new RectOffset(0, 0, 0, 0);

            galleryEmptyState = factory.Rect("Module Gallery Empty State", galleryFrame.transform).gameObject;
            TouchUiFactory.Stretch(galleryEmptyState.GetComponent<RectTransform>(), 14, 14, -14, -14);
            var emptyGlow = factory.RoundedImage("Module Gallery Empty Glow", galleryEmptyState.transform,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .10f));
            TouchUiFactory.Anchor(emptyGlow.rectTransform, .5f, .5f, .5f, .5f, -104, -54, 104, 54);
            emptyGlow.raycastTarget = false;
            galleryEmptyTitle = factory.Label("Module Gallery Empty Title", galleryEmptyState.transform,
                "正在连接展厅内容", 30, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(galleryEmptyTitle.rectTransform, .18f, .5f, .82f, .5f, 0, -12, 0, 38);
            galleryEmptyDescription = factory.Label("Module Gallery Empty Description", galleryEmptyState.transform,
                "连接完成后将在这里展示可讲解板块", 19, FontStyle.Normal, theme.TextSecondary,
                TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(galleryEmptyDescription.rectTransform, .18f, .5f, .82f, .5f, 0, -53, 0, -13);
            galleryEmptyState.SetActive(false);

            var actionBar = factory.RoundedImage("Kiosk Playback Action Bar", root,
                new Color(.022f, .092f, .17f, .97f));
            TouchUiFactory.Anchor(actionBar.rectTransform, 0, 0, 1, 0, 20, 16, -20, 124);
            var actionLine = factory.Image("Kiosk Action Line", actionBar.transform,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .38f));
            TouchUiFactory.Anchor(actionLine.rectTransform, 0, 1, 1, 1, 22, -2, -22, 0);
            actionLine.raycastTarget = false;

            var selectedCaption = factory.Label("Selected Caption", actionBar.transform,
                "当前选择", 16, FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(selectedCaption.rectTransform, 0, 0, 0, 1, 28, 65, 150, -14);
            selectedTitle = factory.Label("Selected Module", actionBar.transform,
                "请选择讲解板块", 30, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(selectedTitle.rectTransform, 0, 0, 0, 1, 28, 13, 560, -40);
            selectedTitle.resizeTextForBestFit = true;
            selectedTitle.resizeTextMinSize = 20;
            selectedTitle.resizeTextMaxSize = 30;

            var statusDivider = factory.Image("Playback Status Divider", actionBar.transform,
                new Color(theme.Border.r, theme.Border.g, theme.Border.b, .7f));
            TouchUiFactory.Anchor(statusDivider.rectTransform, 0, 0, 0, 1, 580, 22, 582, -22);
            statusDivider.raycastTarget = false;
            readinessIndicator = factory.RoundedImage("Playback Readiness Dot", actionBar.transform, theme.Warning);
            TouchUiFactory.Anchor(readinessIndicator.rectTransform, 0, .5f, 0, .5f, 602, -6, 614, 6);
            readinessIndicator.raycastTarget = false;
            readinessText = factory.Label("Kiosk Readiness", actionBar.transform,
                "正在检查系统状态…", 18, FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(readinessText.rectTransform, 0, 0, 1, 1, 626, 16, -606, -16);
            readinessText.resizeTextForBestFit = true;
            readinessText.resizeTextMinSize = 14;
            readinessText.resizeTextMaxSize = 18;

            startButton = factory.TouchButton(actionBar.transform, "开始讲解", true, StartSelected);
            TouchUiFactory.Anchor(startButton.GetComponent<RectTransform>(), 1, 0, 1, 1,
                -594, 18, -314, -18);
            startAllButton = factory.TouchButton(actionBar.transform, "全部讲解", false,
                () => StartAllRequested?.Invoke());
            TouchUiFactory.Anchor(startAllButton.GetComponent<RectTransform>(), 1, 0, 1, 1,
                -298, 18, -18, -18);
            startAllButton.GetComponent<Image>().color = new Color(.06f, .24f, .36f, 1);
        }

        public void Render(TouchUiState state, Func<string, string> urlResolver)
        {
            if (state == null) return;
            resolveUrl = urlResolver;
            var titleOverride = Find(state.UiExperience?.touchElements, "home.kiosk.title");
            var subtitleOverride = Find(state.UiExperience?.touchElements, "home.kiosk.subtitle");
            pageTitle.text = string.IsNullOrWhiteSpace(titleOverride?.text) ? "选择讲解板块" : titleOverride.text;
            pageSubtitle.text = string.IsNullOrWhiteSpace(subtitleOverride?.text)
                ? "轻触一张图片选择板块，随后点击下方按钮开始讲解" : subtitleOverride.text;

            var modules = state.Content?.modules?.Where(item => item != null && item.enabled)
                .OrderBy(item => item.order).ToArray() ?? Array.Empty<ExhibitionModule>();
            moduleCount.text = modules.Length + " 个讲解板块";
            UpdateGridLayout();
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
            readinessText.text = state.HasActiveSession ? "讲解正在进行，可在“当前讲解”中操作。"
                : !state.Connected ? "服务器正在连接，请稍候。"
                : state.Readiness?.canStart == true ? "系统已就绪，可以开始讲解。"
                : state.Readiness?.message ?? "LED大屏尚未就绪。";
            readinessIndicator.color = canStart ? theme.Success : theme.Warning;
        }

        public void RefreshTheme() => ApplySelection();

        private void UpdateGridLayout()
        {
            var size = gridViewport.rect.size;
            if (size.x < 1 || size.y < 1)
            {
                Canvas.ForceUpdateCanvases();
                size = gridViewport.rect.size;
            }
            if (size.x < 1 || size.y < 1) return;
            if (Vector2.Distance(size, lastGridSize) < .5f) return;
            lastGridSize = size;
            var columns = size.x >= 1220 ? 4 : 3;
            const float gap = 14;
            gridLayout.constraintCount = columns;
            gridLayout.cellSize = new Vector2(
                (size.x - (columns - 1) * gap) / columns,
                Mathf.Max(172, (size.y - 2 * gap) / 3));
        }

        private void Rebuild(ExhibitionModule[] modules)
        {
            cards.Clear();
            TouchUiFactory.Clear(grid);
            foreach (var module in modules)
            {
                var border = factory.RoundedImage("Module - " + module.name, grid, theme.Border);
                var frame = factory.RoundedImage("Photo Card", border.transform,
                    new Color(.025f, .10f, .19f, 1));
                TouchUiFactory.Stretch(frame.rectTransform, 3, 3, -3, -3);
                frame.raycastTarget = false;
                var mask = frame.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = true;

                var placeholder = factory.Image("Technology Placeholder", frame.transform,
                    new Color(.018f, .11f, .22f, 1));
                TouchUiFactory.Stretch(placeholder.rectTransform);
                placeholder.raycastTarget = false;
                BuildPlaceholderVisual(placeholder.transform);

                var cover = factory.Image("Module Cover", frame.transform, Color.clear);
                TouchUiFactory.Stretch(cover.rectTransform);
                cover.raycastTarget = false;
                var crop = cover.gameObject.AddComponent<AspectRatioFitter>();
                crop.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                crop.aspectRatio = 16f / 9f;

                var shade = factory.Image("Photo Contrast Veil", frame.transform,
                    new Color(.004f, .025f, .07f, .16f));
                TouchUiFactory.Stretch(shade.rectTransform);
                shade.raycastTarget = false;
                if (!string.IsNullOrWhiteSpace(module.coverUrl))
                {
                    imageLoader.Load(cover, resolveUrl?.Invoke(module.coverUrl) ?? module.coverUrl, success =>
                    {
                        if (placeholder != null) placeholder.gameObject.SetActive(!success);
                        if (cover != null && success && cover.sprite != null)
                            crop.aspectRatio = (float)cover.sprite.rect.width / cover.sprite.rect.height;
                        if (shade != null) shade.color = new Color(.004f, .025f, .07f, success ? .18f : .06f);
                    });
                }

                var titleBand = factory.Image("Readable Title Band", frame.transform,
                    new Color(.006f, .033f, .078f, .94f));
                TouchUiFactory.Anchor(titleBand.rectTransform, 0, 0, 1, 0, 0, 0, 0, 68);
                titleBand.raycastTarget = false;
                var titleSeparator = factory.Image("Title Band Edge", frame.transform,
                    new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .6f));
                TouchUiFactory.Anchor(titleSeparator.rectTransform, 0, 0, 1, 0, 0, 68, 0, 70);
                titleSeparator.raycastTarget = false;
                var title = factory.Label("Module Title", frame.transform, module.name, 25,
                    FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
                TouchUiFactory.Anchor(title.rectTransform, 0, 0, 1, 0, 22, 8, -18, 64);
                title.resizeTextForBestFit = true;
                title.resizeTextMinSize = 20;
                title.resizeTextMaxSize = 25;
                var shadow = title.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0, 0, 0, .85f);
                shadow.effectDistance = new Vector2(1, -2);

                var corner = factory.Image("Photo Corner Detail", frame.transform,
                    new Color(theme.ConfigurableAccent.r, theme.ConfigurableAccent.g,
                        theme.ConfigurableAccent.b, .8f));
                TouchUiFactory.Anchor(corner.rectTransform, 0, 1, 0, 1, 18, -18, 72, -15);
                corner.raycastTarget = false;
                var selectedTag = factory.RoundedImage("Selected Tag", frame.transform,
                    new Color(.02f, .36f, .52f, .94f));
                TouchUiFactory.Anchor(selectedTag.rectTransform, 1, 1, 1, 1, -100, -48, -15, -15);
                selectedTag.raycastTarget = false;
                var selectedTagText = factory.Label("Selected Tag Text", selectedTag.transform, "已选择", 16,
                    FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleCenter);
                TouchUiFactory.Stretch(selectedTagText.rectTransform);
                selectedTag.gameObject.SetActive(false);

                var button = border.gameObject.AddComponent<Button>();
                button.targetGraphic = border;
                var captured = module;
                button.onClick.AddListener(() => { selectedModuleId = captured.id; ApplySelection(); });
                cards.Add(new ModuleCard
                {
                    Module = module,
                    Border = border,
                    Placeholder = placeholder,
                    Cover = cover,
                    CoverShade = shade,
                    SelectedTag = selectedTag.gameObject
                });
            }
            Canvas.ForceUpdateCanvases();
            var scroll = gridViewport.GetComponent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1;
        }

        private void ApplySelection()
        {
            ExhibitionModule selected = null;
            foreach (var card in cards)
            {
                var active = string.Equals(card.Module.id, selectedModuleId, StringComparison.OrdinalIgnoreCase);
                card.Border.color = active ? theme.ConfigurableAccent : new Color(.14f, .36f, .51f, .8f);
                card.SelectedTag.SetActive(active);
                if (active) selected = card.Module;
            }
            selectedTitle.text = selected?.name ?? "请选择讲解板块";
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
            var halo = factory.RoundedImage("Placeholder Light", parent,
                new Color(theme.Primary.r, theme.Primary.g, theme.Primary.b, .11f));
            TouchUiFactory.Anchor(halo.rectTransform, .22f, .20f, .78f, .86f, 0, 0, 0, 0);
            halo.raycastTarget = false;
            for (var index = 0; index < 4; index++)
            {
                var beam = factory.Image("Placeholder Light Beam", parent,
                    new Color(theme.ConfigurableAccent.r, theme.ConfigurableAccent.g,
                        theme.ConfigurableAccent.b, .13f + index * .03f));
                var x = .26f + index * .16f;
                TouchUiFactory.Anchor(beam.rectTransform, x, .28f, x, .82f, 0, 0, 2, 0);
                beam.raycastTarget = false;
            }
            var horizon = factory.Image("Placeholder Horizon", parent,
                new Color(theme.ConfigurableAccent.r, theme.ConfigurableAccent.g,
                    theme.ConfigurableAccent.b, .3f));
            TouchUiFactory.Anchor(horizon.rectTransform, .08f, .35f, .92f, .35f, 0, 0, 0, 2);
            horizon.raycastTarget = false;
        }

        private void UpdateGalleryEmptyState(TouchUiState state, int count)
        {
            galleryEmptyState.SetActive(count == 0);
            if (count > 0) return;
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
