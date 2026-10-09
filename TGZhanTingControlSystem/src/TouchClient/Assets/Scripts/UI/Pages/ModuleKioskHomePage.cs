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
    /// Light exhibition-home module gallery. It owns presentation and local selection only;
    /// the caller remains responsible for all playback, LED readiness and session rules.
    /// </summary>
    public sealed class ModuleKioskHomePage
    {
        private static Sprite bottomShadeSprite;

        private sealed class ModuleCard
        {
            public ExhibitionModule Module;
            public Image Border;
            public Image Cover;
            public Image Placeholder;
            public Image Shade;
            public GameObject SelectedBadge;
            public Text SelectedOrder;
        }

        private readonly TouchUiFactory factory;
        private readonly TouchTheme theme;
        private readonly TouchImageLoader imageLoader;
        private readonly List<ModuleCard> cards = new List<ModuleCard>();
        private readonly HashSet<string> selectedModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly RectTransform root;
        private readonly RectTransform grid;
        private readonly RectTransform gridViewport;
        private readonly GridLayoutGroup gridLayout;
        private readonly Text selectionTitle;
        private readonly Text selectionDetail;
        private readonly Text readinessText;
        private readonly Image readinessIndicator;
        private readonly Button clearButton;
        private readonly Button startButton;
        private readonly Button startAllButton;
        private readonly GameObject galleryEmptyState;
        private readonly Text galleryEmptyTitle;
        private readonly Text galleryEmptyDescription;
        private Vector2 lastGridSize;
        private string signature;
        private Func<string, string> resolveUrl;
        private bool canStart;

        public RectTransform Root => root;
        public event Action<string[]> ModulesStartRequested;
        public event Action StartAllRequested;

        public ModuleKioskHomePage(TouchUiFactory factory, TouchTheme theme, TouchImageLoader imageLoader, Transform parent)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            this.imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));
            root = factory.Rect("Module Kiosk Home Page", parent);

            var pageBackground = factory.Image("Exhibition Home Background", root, theme.Surface);
            TouchUiFactory.Stretch(pageBackground.rectTransform);
            pageBackground.raycastTarget = false;

            var headingAccent = factory.Image("Home Title Accent", root, theme.Primary);
            TouchUiFactory.Anchor(headingAccent.rectTransform, 0, 1, 0, 1, 8, -38, 12, -8);
            headingAccent.raycastTarget = false;
            var eyebrow = factory.Label("Home Eyebrow", root, "探索 · 体验 · 发现", theme.Caption,
                FontStyle.Bold, theme.Primary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(eyebrow.rectTransform, 0, 1, .38f, 1, 24, -34, 0, -8);
            var title = factory.Label("Home Title", root, "展厅讲解", theme.Display,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, 1, .52f, 1, 24, -94, 0, -32);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = theme.PageTitle;
            title.resizeTextMaxSize = theme.Display;
            var subtitle = factory.Label("Home Subtitle", root, "选择您想了解的展区，开启智慧讲解之旅", theme.Body,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(subtitle.rectTransform, 0, 1, .7f, 1, 26, -119, 0, -91);

            var gallery = factory.Image("Module Photography Gallery", root, Color.clear);
            TouchUiFactory.Anchor(gallery.rectTransform, 0, 0, 1, 1, 8, 132, -8, -132);
            grid = factory.ScrollGrid(gallery.transform, "Module Photography Grid", 4,
                new Vector2(theme.ModuleGridCellSize.x, theme.ModuleGridCellSize.y),
                new Vector2(theme.CardSpacing, theme.CardSpacing));
            gridViewport = grid.parent.GetComponent<RectTransform>();
            TouchUiFactory.Stretch(gridViewport);
            gridLayout = grid.GetComponent<GridLayoutGroup>();
            gridLayout.padding = new RectOffset(0, 0, 0, 0);

            galleryEmptyState = factory.Rect("Module Gallery Empty State", gallery.transform).gameObject;
            TouchUiFactory.Stretch(galleryEmptyState.GetComponent<RectTransform>());
            var emptySymbol = factory.RoundedImage("Module Gallery Empty Symbol", galleryEmptyState.transform,
                theme.PrimarySoft);
            TouchUiFactory.Anchor(emptySymbol.rectTransform, .5f, .5f, .5f, .5f, -52, 15, 52, 119);
            emptySymbol.raycastTarget = false;
            var emptySymbolText = factory.Label("Module Gallery Empty Symbol Text", emptySymbol.transform, "展", 36,
                FontStyle.Bold, theme.Primary, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(emptySymbolText.rectTransform);
            galleryEmptyTitle = factory.Label("Module Gallery Empty Title", galleryEmptyState.transform,
                "正在连接展厅内容", theme.PageTitle, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(galleryEmptyTitle.rectTransform, .14f, .5f, .86f, .5f, 0, -30, 0, 12);
            galleryEmptyDescription = factory.Label("Module Gallery Empty Description", galleryEmptyState.transform,
                "连接完成后将在这里展示可讲解展区", theme.Body, FontStyle.Normal, theme.TextSecondary,
                TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(galleryEmptyDescription.rectTransform, .14f, .5f, .86f, .5f, 0, -66, 0, -26);
            galleryEmptyState.SetActive(false);

            var actionBarBorder = factory.RoundedImage("Home Selection Bar Border", root, theme.Border);
            TouchUiFactory.Anchor(actionBarBorder.rectTransform, 0, 0, 1, 0, 8, 8, -8, 116);
            var actionBar = factory.RoundedImage("Home Selection Bar", actionBarBorder.transform, theme.Surface);
            TouchUiFactory.Stretch(actionBar.rectTransform, 1, 1, -1, -1);
            var selectionIcon = factory.RoundedImage("Selection Count Icon", actionBar.transform, theme.Primary);
            TouchUiFactory.Anchor(selectionIcon.rectTransform, 0, .5f, 0, .5f, 22, -27, 76, 27);
            selectionIcon.raycastTarget = false;
            var selectionIconLabel = factory.Label("Selection Count Icon Label", selectionIcon.transform, "✓", 30,
                FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(selectionIconLabel.rectTransform);
            selectionTitle = factory.Label("Selection Count", actionBar.transform, "请选择展区", theme.SectionTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(selectionTitle.rectTransform, 0, .5f, 0, .5f, 96, 2, 450, 38);
            selectionDetail = factory.Label("Selection Detail", actionBar.transform, "可选择一个或多个展区", theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(selectionDetail.rectTransform, 0, .5f, 0, .5f, 98, -38, 620, -4);

            var divider = factory.Image("Selection Bar Divider", actionBar.transform, theme.Border);
            TouchUiFactory.Anchor(divider.rectTransform, 0, .5f, 0, .5f, 646, -28, 648, 28);
            divider.raycastTarget = false;
            clearButton = factory.TouchButton(actionBar.transform, "清空选择", false, ClearSelection);
            TouchUiFactory.Anchor(clearButton.GetComponent<RectTransform>(), 0, .5f, 0, .5f, 672, -31, 838, 31);
            ConfigureSecondaryButton(clearButton);

            readinessIndicator = factory.RoundedImage("Home Readiness Dot", actionBar.transform, theme.Warning);
            TouchUiFactory.Anchor(readinessIndicator.rectTransform, 0, .5f, 0, .5f, 870, -5, 880, 5);
            readinessIndicator.raycastTarget = false;
            readinessText = factory.Label("Home Readiness", actionBar.transform, "正在检查系统状态…", theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(readinessText.rectTransform, 0, .5f, 0, .5f, 892, -27, 1110, 27);
            readinessText.resizeTextForBestFit = true;
            readinessText.resizeTextMinSize = 12;
            readinessText.resizeTextMaxSize = theme.Caption;

            startAllButton = factory.TouchButton(actionBar.transform, "全部讲解", false, () => StartAllRequested?.Invoke());
            TouchUiFactory.Anchor(startAllButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f, -514, -38, -286, 38);
            ConfigureSecondaryButton(startAllButton);
            startButton = factory.TouchButton(actionBar.transform, "开始讲解  →", true, StartSelected);
            TouchUiFactory.Anchor(startButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f, -266, -38, -22, 38);
        }

        public void Render(TouchUiState state, Func<string, string> urlResolver)
        {
            if (state == null) return;
            resolveUrl = urlResolver;
            var modules = state.Content?.modules?.Where(item => item != null && item.enabled)
                .OrderBy(item => item.order).ToArray() ?? Array.Empty<ExhibitionModule>();
            UpdateGridLayout();
            UpdateGalleryEmptyState(state, modules.Length);
            var newSignature = BuildSignature(state, modules);
            if (!string.Equals(signature, newSignature, StringComparison.Ordinal))
            {
                signature = newSignature;
                Rebuild(modules);
            }
            selectedModuleIds.RemoveWhere(id => !modules.Any(module => string.Equals(module.id, id,
                StringComparison.OrdinalIgnoreCase)));
            canStart = state.Connected && state.Readiness?.canStart == true && !state.HasActiveSession;
            ApplySelection();
            startAllButton.interactable = canStart && modules.Any(HasContent);
            readinessText.text = state.HasActiveSession ? "当前已有讲解任务，请在“当前讲解”中继续操作。"
                : !state.Connected ? "服务连接中断，正在自动重连。"
                : state.Readiness?.canStart == true ? "系统已就绪，可以开始讲解。"
                : state.Readiness?.message ?? "LED播放端尚未就绪，暂时无法开始讲解。";
            readinessIndicator.color = state.HasActiveSession || !state.Connected || state.Readiness?.canStart != true
                ? theme.Warning : theme.Success;
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
            if (size.x < 1 || size.y < 1 || Vector2.Distance(size, lastGridSize) < .5f) return;
            lastGridSize = size;
            var columns = size.x >= 1240 ? 4 : size.x >= 820 ? 3 : 2;
            var gap = theme.CardSpacing;
            gridLayout.constraintCount = columns;
            gridLayout.cellSize = new Vector2((size.x - (columns - 1) * gap) / columns,
                Mathf.Max(136, (size.y - 2 * gap) / 3));
        }

        private void Rebuild(IEnumerable<ExhibitionModule> modules)
        {
            cards.Clear();
            TouchUiFactory.Clear(grid);
            var displayOrder = 0;
            foreach (var module in modules)
            {
                displayOrder++;
                var border = factory.RoundedImage("Module Photo Card - " + module.name, grid, theme.Border);
                var frame = factory.RoundedImage("Module Photo Frame", border.transform, theme.SurfaceSoft);
                TouchUiFactory.Stretch(frame.rectTransform, 1, 1, -1, -1);
                frame.raycastTarget = false;
                var mask = frame.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = true;

                var placeholder = factory.Image("Module Cover Placeholder", frame.transform, theme.PrimarySoft);
                TouchUiFactory.Stretch(placeholder.rectTransform);
                placeholder.raycastTarget = false;
                BuildPlaceholder(placeholder.transform);

                var cover = factory.Image("Module Cover", frame.transform, Color.clear);
                TouchUiFactory.Stretch(cover.rectTransform);
                cover.raycastTarget = false;
                var crop = cover.gameObject.AddComponent<AspectRatioFitter>();
                crop.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                crop.aspectRatio = 16f / 9f;

                var shade = factory.Image("Module Cover Bottom Contrast", frame.transform, Color.white);
                shade.sprite = bottomShadeSprite ?? (bottomShadeSprite = CreateBottomShadeSprite());
                TouchUiFactory.Stretch(shade.rectTransform);
                shade.raycastTarget = false;

                var index = factory.Label("Module Order", frame.transform, displayOrder.ToString("00"), theme.SectionTitle,
                    FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);
                TouchUiFactory.Anchor(index.rectTransform, 0, 1, 0, 1, 22, -50, 90, -14);
                AddShadow(index, new Color(0f, 0f, 0f, .45f));
                var title = factory.Label("Module Title", frame.transform, module.name, theme.CardTitle,
                    FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
                TouchUiFactory.Anchor(title.rectTransform, 0, 0, 1, 0, 22, 22, -86, 68);
                title.resizeTextForBestFit = true;
                title.resizeTextMinSize = Mathf.Max(16, theme.CardTitle - 5);
                title.resizeTextMaxSize = theme.CardTitle;
                AddShadow(title, new Color(0f, 0f, 0f, .75f));

                var selectedBadge = factory.RoundedImage("Module Selected Badge", frame.transform, theme.Primary);
                TouchUiFactory.Anchor(selectedBadge.rectTransform, 1, 1, 1, 1, -58, -58, -16, -16);
                selectedBadge.raycastTarget = false;
                var badgeLabel = factory.Label("Module Selected Badge Label", selectedBadge.transform, "✓", 24,
                    FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
                TouchUiFactory.Stretch(badgeLabel.rectTransform);
                selectedBadge.gameObject.SetActive(false);

                var selectionOrder = factory.Label("Module Selection Order", frame.transform, string.Empty, theme.Caption,
                    FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
                TouchUiFactory.Anchor(selectionOrder.rectTransform, 1, 0, 1, 0, -56, 18, -16, 44);
                selectionOrder.gameObject.SetActive(false);

                var button = border.gameObject.AddComponent<Button>();
                button.targetGraphic = border;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.Lerp(Color.white, theme.Primary, .08f);
                colors.pressedColor = Color.Lerp(Color.white, theme.Primary, .18f);
                colors.disabledColor = theme.DisabledControlTint;
                colors.colorMultiplier = 1;
                colors.fadeDuration = .08f;
                button.colors = colors;
                var captured = module;
                button.onClick.AddListener(() => ToggleSelection(captured.id));
                cards.Add(new ModuleCard
                {
                    Module = module,
                    Border = border,
                    Cover = cover,
                    Placeholder = placeholder,
                    Shade = shade,
                    SelectedBadge = selectedBadge.gameObject,
                    SelectedOrder = selectionOrder
                });
                LoadCover(module, cover, placeholder, shade, crop);
            }
            Canvas.ForceUpdateCanvases();
            var scroll = gridViewport.GetComponent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1;
        }

        private void LoadCover(ExhibitionModule module, Image cover, Image placeholder, Image shade,
            AspectRatioFitter crop)
        {
            if (string.IsNullOrWhiteSpace(module.coverUrl)) return;
            var url = resolveUrl?.Invoke(module.coverUrl) ?? module.coverUrl;
            imageLoader.Load(cover, url, success =>
            {
                if (placeholder != null) placeholder.gameObject.SetActive(!success);
                if (cover != null && success && cover.sprite != null)
                    crop.aspectRatio = cover.sprite.rect.width / cover.sprite.rect.height;
                if (shade != null && !success) shade.color = new Color(1f, 1f, 1f, .45f);
            });
        }

        private void ToggleSelection(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId)) return;
            if (!selectedModuleIds.Add(moduleId)) selectedModuleIds.Remove(moduleId);
            ApplySelection();
        }

        private void ClearSelection()
        {
            if (selectedModuleIds.Count == 0) return;
            selectedModuleIds.Clear();
            ApplySelection();
        }

        private void ApplySelection()
        {
            var selected = SelectedModules();
            for (var index = 0; index < cards.Count; index++)
            {
                var card = cards[index];
                var selectedIndex = selected.FindIndex(module => string.Equals(module.id, card.Module.id,
                    StringComparison.OrdinalIgnoreCase));
                var isSelected = selectedIndex >= 0;
                card.Border.color = isSelected ? theme.Primary : theme.Border;
                card.SelectedBadge.SetActive(isSelected);
                card.SelectedOrder.gameObject.SetActive(isSelected);
                card.SelectedOrder.text = isSelected ? (selectedIndex + 1).ToString("00") : string.Empty;
            }
            selectionTitle.text = selected.Count == 0 ? "请选择展区" : "已选择 " + selected.Count + " 个展区";
            selectionDetail.text = selected.Count == 0 ? "可选择一个或多个展区" : string.Join(" · ", selected.Select(item => item.name));
            clearButton.interactable = selected.Count > 0;
            startButton.interactable = canStart && selected.Count > 0 && selected.All(HasContent);
        }

        private List<ExhibitionModule> SelectedModules() => cards
            .Where(card => selectedModuleIds.Contains(card.Module.id))
            .Select(card => card.Module).ToList();

        private void StartSelected()
        {
            var moduleIds = SelectedModules().Where(HasContent).Select(module => module.id).ToArray();
            if (moduleIds.Length > 0 && startButton.interactable) ModulesStartRequested?.Invoke(moduleIds);
        }

        private static bool HasContent(ExhibitionModule module) => module?.nodes != null && module.nodes.Any(node =>
            node != null && (!string.IsNullOrWhiteSpace(node.ttsAudioUrl) ||
                             node.assets?.Any(asset => asset != null && !string.IsNullOrWhiteSpace(asset.url) &&
                                 (asset.kind == 0 || asset.kind == 2)) == true));

        private void BuildPlaceholder(Transform parent)
        {
            var panel = factory.RoundedImage("Module Placeholder Center", parent, new Color(theme.Primary.r, theme.Primary.g,
                theme.Primary.b, .12f));
            TouchUiFactory.Anchor(panel.rectTransform, .5f, .5f, .5f, .5f, -52, -38, 52, 66);
            panel.raycastTarget = false;
            var symbol = factory.Label("Module Placeholder Symbol", panel.transform, "展", 34, FontStyle.Bold,
                theme.Primary, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(symbol.rectTransform);
            var line = factory.Image("Module Placeholder Line", parent, new Color(theme.Primary.r, theme.Primary.g,
                theme.Primary.b, .42f));
            TouchUiFactory.Anchor(line.rectTransform, .25f, .5f, .75f, .5f, 0, -60, 0, -58);
            line.raycastTarget = false;
        }

        private static void AddShadow(Graphic graphic, Color color)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = new Vector2(0, -2);
        }

        private static Sprite CreateBottomShadeSprite()
        {
            var texture = new Texture2D(2, 128, TextureFormat.RGBA32, false)
            {
                name = "TG Module Photo Bottom Shade",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (var y = 0; y < texture.height; y++)
            {
                var alpha = Mathf.SmoothStep(.78f, 0f, y / (float)(texture.height - 1));
                var color = new Color(.015f, .035f, .09f, alpha);
                texture.SetPixel(0, y, color);
                texture.SetPixel(1, y, color);
            }
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private void ConfigureSecondaryButton(Button button)
        {
            var image = button.GetComponent<Image>();
            image.color = theme.Surface;
            var border = button.gameObject.AddComponent<Outline>();
            border.effectColor = theme.BorderStrong;
            border.effectDistance = new Vector2(1, -1);
        }

        private void UpdateGalleryEmptyState(TouchUiState state, int count)
        {
            galleryEmptyState.SetActive(count == 0);
            if (count > 0) return;
            var connected = state?.Connected == true;
            galleryEmptyTitle.text = connected ? "暂未发布可讲解展区" : "正在连接展厅内容";
            galleryEmptyDescription.text = connected
                ? "请在管理端发布内容后重新进入此页"
                : "连接完成后将在这里展示可讲解展区";
        }

        private static string BuildSignature(TouchUiState state, IEnumerable<ExhibitionModule> modules)
        {
            var builder = new StringBuilder().Append(state.Content?.version).Append('|');
            foreach (var item in modules) builder.Append(item.id).Append(':').Append(item.name).Append(':')
                .Append(item.description).Append(':').Append(item.coverUrl).Append(';');
            return builder.ToString();
        }
    }
}
