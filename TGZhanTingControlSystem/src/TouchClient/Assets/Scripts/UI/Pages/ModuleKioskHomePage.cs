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
    /// Bright 12-module reception gallery. It owns presentation and local selection only;
    /// all readiness, session and playback decisions stay in the existing facade path.
    /// </summary>
    public sealed class ModuleKioskHomePage
    {
        private static readonly Dictionary<int, Sprite> PreviewComposites = new Dictionary<int, Sprite>();
        private static Sprite bottomShadeSprite;

        private sealed class ModuleCard
        {
            public ExhibitionModule Module;
            public Image Border;
            public Image Cover;
            public Image Placeholder;
            public Image Shade;
            public Text DataIndex;
            public Text DataTitle;
            public GameObject SelectedBadge;
            public Text SelectedOrder;
            public bool UsesReferenceComposite;
        }

        private readonly TouchUiFactory factory;
        private readonly TouchTheme theme;
        private readonly TouchImageLoader imageLoader;
        private readonly List<ModuleCard> cards = new List<ModuleCard>();
        private readonly HashSet<string> selectedModuleIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        public ModuleKioskHomePage(TouchUiFactory factory, TouchTheme theme, TouchImageLoader imageLoader,
            Transform parent)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            this.imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));
            root = factory.Rect("Module Kiosk Home Page", parent);

            var pageBackground = factory.Image("Exhibition Home Scene", root, Color.white);
            TouchUiFactory.Stretch(pageBackground.rectTransform);
            var homeTexture = Resources.Load<Texture2D>("Touch/HomeFinal/Home_Light_1920x1080");
            if (homeTexture != null)
                pageBackground.sprite = Sprite.Create(homeTexture,
                    new Rect(0, 0, homeTexture.width, homeTexture.height), new Vector2(.5f, .5f));
            else
                pageBackground.color = theme.AppBackground;
            pageBackground.raycastTarget = false;
            var readability = factory.Image("Exhibition Home Readability Veil", root,
                new Color(1f, 1f, 1f, .36f));
            TouchUiFactory.Stretch(readability.rectTransform);
            readability.raycastTarget = false;

            BuildHeader();

            var gallery = factory.Image("Module Photography Gallery", root, Color.clear);
            TouchUiFactory.Anchor(gallery.rectTransform, 0, 0, 1, 1, 0, 126, 0, -126);
            grid = factory.ScrollGrid(gallery.transform, "Module Photography Grid", 4,
                new Vector2(theme.ModuleGridCellSize.x, theme.ModuleGridCellSize.y),
                new Vector2(theme.Space12, theme.Space12));
            gridViewport = grid.parent.GetComponent<RectTransform>();
            TouchUiFactory.Stretch(gridViewport);
            gridLayout = grid.GetComponent<GridLayoutGroup>();
            gridLayout.padding = new RectOffset(0, 0, 0, 0);

            galleryEmptyState = factory.RoundedImage("Module Gallery Empty State", gallery.transform,
                new Color(1f, 1f, 1f, .88f)).gameObject;
            TouchUiFactory.Stretch(galleryEmptyState.GetComponent<RectTransform>(), 0, 22, 0, -22);
            var emptyLine = factory.Image("Module Gallery Empty Accent", galleryEmptyState.transform, theme.Primary);
            TouchUiFactory.Anchor(emptyLine.rectTransform, .5f, .5f, .5f, .5f, -72, 39, 72, 43);
            emptyLine.raycastTarget = false;
            galleryEmptyTitle = factory.Label("Module Gallery Empty Title", galleryEmptyState.transform,
                "正在连接展厅内容", theme.PageTitle, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(galleryEmptyTitle.rectTransform, .14f, .5f, .86f, .5f, 0, -12, 0, 32);
            galleryEmptyDescription = factory.Label("Module Gallery Empty Description", galleryEmptyState.transform,
                "连接完成后将在这里展示可讲解展区", theme.Body, FontStyle.Normal, theme.TextSecondary,
                TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(galleryEmptyDescription.rectTransform, .14f, .5f, .86f, .5f, 0, -58, 0, -16);
            galleryEmptyState.SetActive(false);

            var actionBarBorder = factory.RoundedImage("Home Selection Bar Border", root,
                new Color(.64f, .78f, .96f, .8f));
            TouchUiFactory.Anchor(actionBarBorder.rectTransform, 0, 0, 1, 0, 0, 0, 0, 108);
            var actionBar = factory.RoundedImage("Home Selection Bar", actionBarBorder.transform,
                new Color(1f, 1f, 1f, .96f));
            TouchUiFactory.Stretch(actionBar.rectTransform, 1, 1, -1, -1);

            var selectionIcon = factory.RoundedImage("Selection Count Icon", actionBar.transform, theme.Primary);
            TouchUiFactory.Anchor(selectionIcon.rectTransform, 0, .5f, 0, .5f, 24, -29, 82, 29);
            selectionIcon.raycastTarget = false;
            var selectionIconLabel = factory.Label("Selection Count Icon Label", selectionIcon.transform, "✓", 30,
                FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(selectionIconLabel.rectTransform);
            selectionTitle = factory.Label("Selection Count", actionBar.transform, "请选择展区", theme.SectionTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(selectionTitle.rectTransform, 0, .5f, 0, .5f, 102, 3, 470, 39);
            selectionDetail = factory.Label("Selection Detail", actionBar.transform, "可选择一个或多个展区",
                theme.Body, FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(selectionDetail.rectTransform, 0, .5f, 0, .5f, 104, -38, 610, -3);
            selectionDetail.resizeTextForBestFit = true;
            selectionDetail.resizeTextMinSize = theme.Secondary;
            selectionDetail.resizeTextMaxSize = theme.Body;

            clearButton = factory.TouchButton(actionBar.transform, "清空选择", false, ClearSelection);
            TouchUiFactory.Anchor(clearButton.GetComponent<RectTransform>(), 0, .5f, 0, .5f, 630, -31, 798, 31);
            ConfigureSecondaryButton(clearButton);

            readinessIndicator = factory.RoundedImage("Home Readiness Dot", actionBar.transform, theme.Warning);
            TouchUiFactory.Anchor(readinessIndicator.rectTransform, 0, .5f, 0, .5f, 816, -5, 826, 5);
            readinessIndicator.raycastTarget = false;
            readinessText = factory.Label("Home Readiness", actionBar.transform, "正在检查系统状态…", theme.Body,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(readinessText.rectTransform, 0, .5f, 0, .5f, 840, -27, 1096, 27);
            readinessText.resizeTextForBestFit = true;
            readinessText.resizeTextMinSize = theme.Secondary;
            readinessText.resizeTextMaxSize = theme.Body;

            startAllButton = factory.TouchButton(actionBar.transform, "全部讲解", false,
                () => StartAllRequested?.Invoke());
            TouchUiFactory.Anchor(startAllButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -496, -38, -276, 38);
            ConfigureSecondaryButton(startAllButton);
            startButton = factory.TouchButton(actionBar.transform, "开始讲解  →", true, StartSelected);
            TouchUiFactory.Anchor(startButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -258, -38, -18, 38);
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
            readinessText.text = state.HasActiveSession ? "讲解进行中"
                : !state.Connected ? "服务连接中断"
                : state.Readiness == null ? "状态检查中"
                : state.Readiness.canStart ? "系统已就绪"
                : state.Readiness.ledOnline ? "系统暂未就绪" : "LED播放端离线";
            readinessIndicator.color = state.HasActiveSession || !state.Connected || state.Readiness?.canStart != true
                ? theme.Warning : theme.Success;
        }

        public void RefreshTheme() => ApplySelection();

        private void BuildHeader()
        {
            var eyebrow = factory.Label("Home Eyebrow", root, "智慧讲解  ·  12 MODULES", theme.Caption,
                FontStyle.Bold, theme.Primary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(eyebrow.rectTransform, 0, 1, .42f, 1, 6, -30, 0, -4);
            var title = factory.Label("Home Title", root, "展厅讲解", theme.Display,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, 1, .42f, 1, 4, -84, 0, -28);
            var subtitle = factory.Label("Home Subtitle", root, "选择您想了解的展区，开启智慧讲解之旅", theme.Body,
                FontStyle.Normal, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(subtitle.rectTransform, 0, 1, .62f, 1, 6, -116, 0, -82);
            var slogan = factory.Label("Home Slogan", root, "探索 · 体验 · 发现", theme.SectionTitle,
                FontStyle.Bold, theme.PrimaryPressed, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(slogan.rectTransform, .62f, 1, 1, 1, 0, -67, -8, -25);
            var sloganDetail = factory.Label("Home Slogan Detail", root, "科技引领未来", theme.Secondary,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(sloganDetail.rectTransform, .68f, 1, 1, 1, 0, -100, -10, -66);
            var underline = factory.Image("Home Title Underline", root, theme.Primary);
            TouchUiFactory.Anchor(underline.rectTransform, 0, 1, 0, 1, 4, -122, 126, -118);
            underline.raycastTarget = false;
        }

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
            var gap = theme.Space12;
            gridLayout.constraintCount = columns;
            gridLayout.cellSize = new Vector2((size.x - (columns - 1) * gap) / columns,
                Mathf.Max(148, (size.y - 2 * gap) / 3));
        }

        private void Rebuild(IEnumerable<ExhibitionModule> modules)
        {
            cards.Clear();
            TouchUiFactory.Clear(grid);
            var displayOrder = 0;
            foreach (var module in modules)
            {
                displayOrder++;
                var border = factory.RoundedImage("Module Photography Card - " + module.name, grid,
                    new Color(.72f, .82f, .94f, 1));
                var frame = factory.RoundedImage("Module Photography Frame", border.transform, theme.SurfaceSoft);
                TouchUiFactory.Stretch(frame.rectTransform, 3, 3, -3, -3);
                frame.raycastTarget = false;
                var mask = frame.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = true;

                var placeholder = factory.Image("Module Visual Missing", frame.transform,
                    new Color(.91f, .95f, 1f, 1));
                TouchUiFactory.Stretch(placeholder.rectTransform);
                placeholder.raycastTarget = false;
                BuildMissingAssetState(placeholder.transform);

                var cover = factory.Image("Module Photography", frame.transform, Color.white);
                TouchUiFactory.Stretch(cover.rectTransform);
                cover.raycastTarget = false;
                var crop = cover.gameObject.AddComponent<AspectRatioFitter>();
                crop.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                crop.aspectRatio = 1.84f;

                var shade = factory.Image("Module Photography Contrast", frame.transform, Color.white);
                shade.sprite = bottomShadeSprite ?? (bottomShadeSprite = CreateBottomShadeSprite());
                TouchUiFactory.Stretch(shade.rectTransform);
                shade.raycastTarget = false;

                var dataIndex = factory.Label("Module Data Order", frame.transform, displayOrder.ToString("00"),
                    theme.SectionTitle, FontStyle.Normal, Color.white, TextAnchor.MiddleLeft);
                TouchUiFactory.Anchor(dataIndex.rectTransform, 0, 1, 0, 1, 22, -50, 92, -12);
                AddShadow(dataIndex, new Color(0, 0, 0, .5f));
                var dataTitle = factory.Label("Module Data Title", frame.transform, module.name, theme.CardTitle,
                    FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
                TouchUiFactory.Anchor(dataTitle.rectTransform, 0, 0, 1, 0, 22, 18, -76, 66);
                dataTitle.resizeTextForBestFit = true;
                dataTitle.resizeTextMinSize = Mathf.Max(15, theme.CardTitle - 6);
                dataTitle.resizeTextMaxSize = theme.CardTitle;
                AddShadow(dataTitle, new Color(0, 0, 0, .78f));

                var selectedBadge = factory.RoundedImage("Module Selected Order", frame.transform, theme.Primary);
                TouchUiFactory.Anchor(selectedBadge.rectTransform, 1, 1, 1, 1, -64, -64, -16, -16);
                selectedBadge.raycastTarget = false;
                var selectedOrder = factory.Label("Module Selected Order Text", selectedBadge.transform, string.Empty,
                    21, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
                TouchUiFactory.Stretch(selectedOrder.rectTransform);
                selectedBadge.gameObject.SetActive(false);

                var button = border.gameObject.AddComponent<Button>();
                button.targetGraphic = border;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.Lerp(Color.white, theme.Primary, .10f);
                colors.pressedColor = Color.Lerp(Color.white, theme.Primary, .24f);
                colors.disabledColor = theme.DisabledControlTint;
                colors.colorMultiplier = 1;
                colors.fadeDuration = .08f;
                button.colors = colors;
                var captured = module;
                button.onClick.AddListener(() => ToggleSelection(captured.id));

                var card = new ModuleCard
                {
                    Module = module,
                    Border = border,
                    Cover = cover,
                    Placeholder = placeholder,
                    Shade = shade,
                    DataIndex = dataIndex,
                    DataTitle = dataTitle,
                    SelectedBadge = selectedBadge.gameObject,
                    SelectedOrder = selectedOrder
                };
                cards.Add(card);
                LoadVisual(card, displayOrder, crop);
            }
            Canvas.ForceUpdateCanvases();
            var scroll = gridViewport.GetComponent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1;
        }

        private void LoadVisual(ModuleCard card, int displayOrder, AspectRatioFitter crop)
        {
            var preview = LoadPreviewComposite(displayOrder);
            if (preview != null)
            {
                card.UsesReferenceComposite = true;
                card.Cover.sprite = preview;
                card.Cover.color = Color.white;
                crop.aspectRatio = preview.rect.width / preview.rect.height;
                card.Placeholder.gameObject.SetActive(false);
                // The temporary extracted card already contains its number, title, subtitle and arrow.
                card.DataIndex.gameObject.SetActive(false);
                card.DataTitle.gameObject.SetActive(false);
                card.Shade.gameObject.SetActive(false);
                return;
            }

            card.Cover.color = Color.clear;
            if (string.IsNullOrWhiteSpace(card.Module.coverUrl)) return;
            var url = resolveUrl?.Invoke(card.Module.coverUrl) ?? card.Module.coverUrl;
            imageLoader.Load(card.Cover, url, success =>
            {
                if (card.Placeholder != null) card.Placeholder.gameObject.SetActive(!success);
                if (card.Cover != null && success && card.Cover.sprite != null)
                {
                    card.Cover.color = Color.white;
                    crop.aspectRatio = card.Cover.sprite.rect.width / card.Cover.sprite.rect.height;
                }
            });
        }

        private static Sprite LoadPreviewComposite(int displayOrder)
        {
            if (displayOrder < 1 || displayOrder > 12) return null;
            Sprite cached;
            if (PreviewComposites.TryGetValue(displayOrder, out cached)) return cached;
            var resourcePath = "TGExhibitionUI/ModuleCards/Module_" + displayOrder.ToString("00") + "_Composite";
            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) return null;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            sprite.name = "Temporary Reference Card " + displayOrder.ToString("00");
            sprite.hideFlags = HideFlags.HideAndDontSave;
            PreviewComposites[displayOrder] = sprite;
            return sprite;
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
                card.Border.color = isSelected ? theme.Primary : new Color(.72f, .82f, .94f, 1);
                card.SelectedBadge.SetActive(isSelected);
                card.SelectedOrder.text = isSelected ? (selectedIndex + 1).ToString("00") : string.Empty;
            }
            selectionTitle.text = selected.Count == 0 ? "请选择展区" : "已选择 " + selected.Count + " 个展区";
            selectionDetail.text = selected.Count == 0
                ? "可选择一个或多个展区"
                : string.Join(" · ", selected.Take(2).Select(item => item.name)) +
                  (selected.Count > 2 ? " 等 " + selected.Count + " 个展区" : string.Empty);
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

        private void BuildMissingAssetState(Transform parent)
        {
            var accent = factory.Image("Missing Visual Accent", parent, new Color(theme.Primary.r, theme.Primary.g,
                theme.Primary.b, .55f));
            TouchUiFactory.Anchor(accent.rectTransform, .28f, .5f, .72f, .5f, 0, 20, 0, 23);
            accent.raycastTarget = false;
            var label = factory.Label("Missing Visual Message", parent, "视觉素材待补充", theme.Secondary,
                FontStyle.Bold, theme.TextSecondary, TextAnchor.MiddleCenter);
            TouchUiFactory.Anchor(label.rectTransform, .12f, .5f, .88f, .5f, 0, -28, 0, 14);
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
