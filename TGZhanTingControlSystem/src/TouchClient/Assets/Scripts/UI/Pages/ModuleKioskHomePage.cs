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
        private static readonly Color ElectricCyan = TouchTheme.ParseColor("#35DFFF");
        private static readonly Color Cobalt = TouchTheme.ParseColor("#1254AD");
        private static readonly Color FrameBlue = TouchTheme.ParseColor("#286BAF");
        private static Sprite workspaceGradient;
        private static Sprite cardGradient;
        private static Sprite actionGradient;

        private sealed class ModuleCard
        {
            public ExhibitionModule Module;
            public Image Border;
            public Image Placeholder;
            public Image Cover;
            public Image CoverShade;
            public Image SelectionGlow;
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

            var stage = factory.Image("Kiosk Cobalt Workspace", root, Color.white);
            stage.sprite = workspaceGradient ?? (workspaceGradient = CreateGradientSprite(
                "Kiosk Cobalt Workspace Gradient", new Color(.025f, .13f, .31f),
                new Color(.015f, .065f, .19f), new Color(.04f, .32f, .61f)));
            TouchUiFactory.Stretch(stage.rectTransform);
            stage.raycastTarget = false;

            var topBeam = factory.Image("Kiosk Top Beam", root, ElectricCyan);
            TouchUiFactory.Anchor(topBeam.rectTransform, 0, 1, 0, 1, 28, -5, 344, -1);
            topBeam.raycastTarget = false;

            var headingGlow = factory.Image("Heading Light Wash", root,
                new Color(.08f, .43f, .9f, .19f));
            TouchUiFactory.Anchor(headingGlow.rectTransform, 0, 1, 1, 1, 20, -139, -20, -10);
            headingGlow.raycastTarget = false;

            var headingRightRail = factory.Image("Heading Right Light Rail", root,
                new Color(ElectricCyan.r, ElectricCyan.g, ElectricCyan.b, .6f));
            TouchUiFactory.Anchor(headingRightRail.rectTransform, 1, 1, 1, 1,
                -434, -139, -28, -137);
            headingRightRail.raycastTarget = false;

            var headingMark = factory.Image("Kiosk Heading Mark", root, ElectricCyan);
            TouchUiFactory.Anchor(headingMark.rectTransform, 0, 1, 0, 1, 28, -89, 33, -35);
            headingMark.raycastTarget = false;
            var modeLabel = factory.Label("Kiosk Mode Label", root, "智慧展厅  /  自助讲解", 18,
                FontStyle.Bold, ElectricCyan, TextAnchor.MiddleLeft);
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
                new Color(.06f, .27f, .48f, .95f));
            TouchUiFactory.Anchor(countSurface.rectTransform, 1, 1, 1, 1, -280, -97, -28, -45);
            var countDot = factory.RoundedImage("Module Count Dot", countSurface.transform, ElectricCyan);
            TouchUiFactory.Anchor(countDot.rectTransform, 0, .5f, 0, .5f, 22, -5, 32, 5);
            countDot.raycastTarget = false;
            moduleCount = factory.Label("Module Count", countSurface.transform, "正在载入板块", 18,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Stretch(moduleCount.rectTransform, 48, 0, -16, 0);

            var galleryFrame = factory.RoundedImage("Module Gallery", root,
                new Color(.025f, .13f, .29f, .96f));
            TouchUiFactory.Anchor(galleryFrame.rectTransform, 0, 0, 1, 1, 20, 140, -20, -145);
            var galleryEdge = factory.Image("Gallery Edge", galleryFrame.transform,
                new Color(ElectricCyan.r, ElectricCyan.g, ElectricCyan.b, .8f));
            TouchUiFactory.Anchor(galleryEdge.rectTransform, 0, 1, 1, 1, 18, -3, -18, 0);
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
                new Color(.05f, .20f, .40f, .98f));
            TouchUiFactory.Anchor(actionBar.rectTransform, 0, 0, 1, 0, 20, 16, -20, 124);
            var actionLight = factory.Image("Playback Action Blue Light", actionBar.transform, Color.white);
            actionLight.sprite = actionGradient ?? (actionGradient = CreateGradientSprite(
                "Playback Action Gradient", new Color(.04f, .22f, .49f),
                new Color(.02f, .11f, .28f), new Color(.06f, .40f, .75f)));
            TouchUiFactory.Stretch(actionLight.rectTransform, 3, 3, -3, -3);
            actionLight.raycastTarget = false;
            var actionLine = factory.Image("Kiosk Action Line", actionBar.transform,
                ElectricCyan);
            TouchUiFactory.Anchor(actionLine.rectTransform, 0, 1, 1, 1, 22, -3, -22, 0);
            actionLine.raycastTarget = false;

            var actionMarker = factory.Image("Action Left Signal", actionBar.transform, ElectricCyan);
            TouchUiFactory.Anchor(actionMarker.rectTransform, 0, 0, 0, 1, 3, 18, 8, -18);
            actionMarker.raycastTarget = false;

            var selectedCaption = factory.Label("Selected Caption", actionBar.transform,
                "当前选择", 16, FontStyle.Bold, ElectricCyan, TextAnchor.MiddleLeft);
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
            startButton.GetComponent<Image>().color = Cobalt;
            startAllButton = factory.TouchButton(actionBar.transform, "全部讲解", false,
                () => StartAllRequested?.Invoke());
            TouchUiFactory.Anchor(startAllButton.GetComponent<RectTransform>(), 1, 0, 1, 1,
                -298, 18, -18, -18);
            startAllButton.GetComponent<Image>().color = new Color(.055f, .30f, .56f, 1);
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
                var border = factory.RoundedImage("Module - " + module.name, grid, FrameBlue);
                var frame = factory.RoundedImage("Photo Card", border.transform,
                    new Color(.035f, .18f, .38f, 1));
                TouchUiFactory.Stretch(frame.rectTransform, 3, 3, -3, -3);
                frame.raycastTarget = false;
                var mask = frame.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = true;

                var placeholder = factory.Image("Technology Placeholder", frame.transform, Color.white);
                placeholder.sprite = cardGradient ?? (cardGradient = CreateGradientSprite(
                    "Kiosk Card Gradient", new Color(.05f, .32f, .66f),
                    new Color(.02f, .13f, .37f), new Color(.07f, .52f, .85f)));
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
                    new Color(.007f, .055f, .15f, .94f));
                TouchUiFactory.Anchor(titleBand.rectTransform, 0, 0, 1, 0, 0, 0, 0, 68);
                titleBand.raycastTarget = false;
                var titleSeparator = factory.Image("Title Band Edge", frame.transform,
                    new Color(ElectricCyan.r, ElectricCyan.g, ElectricCyan.b, .74f));
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

                AddCornerBracket(frame.transform, "Upper Left", 14, -14, false, false);
                AddCornerBracket(frame.transform, "Upper Right", -14, -14, true, false);
                AddCornerBracket(frame.transform, "Lower Left", 14, 82, false, true);
                AddCornerBracket(frame.transform, "Lower Right", -14, 82, true, true);

                var selectionGlow = factory.Image("Selected Card Light Rail", frame.transform, ElectricCyan);
                TouchUiFactory.Anchor(selectionGlow.rectTransform, 0, 0, 0, 1, 0, 70, 7, -2);
                selectionGlow.raycastTarget = false;
                selectionGlow.gameObject.SetActive(false);
                var selectedTag = factory.RoundedImage("Selected Tag", frame.transform,
                    new Color(.015f, .52f, .73f, .97f));
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
                    SelectionGlow = selectionGlow,
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
                card.Border.color = active ? ElectricCyan : FrameBlue;
                card.SelectionGlow.gameObject.SetActive(active);
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
            var aperture = factory.RoundedImage("Cobalt Aperture", parent,
                new Color(.08f, .57f, .94f, .3f));
            TouchUiFactory.Anchor(aperture.rectTransform, .29f, .34f, .71f, .94f, 0, 0, 0, 0);
            aperture.raycastTarget = false;
            var apertureCore = factory.RoundedImage("Aperture Core", parent,
                new Color(.045f, .22f, .53f, .75f));
            TouchUiFactory.Anchor(apertureCore.rectTransform, .31f, .34f, .69f, .91f, 0, 0, 0, 0);
            apertureCore.raycastTarget = false;
            for (var index = 0; index < 6; index++)
            {
                var x = .13f + index * .145f;
                var beam = factory.Image("Data Column " + index, parent,
                    new Color(ElectricCyan.r, ElectricCyan.g, ElectricCyan.b,
                        index == 2 || index == 3 ? .34f : .19f));
                TouchUiFactory.Anchor(beam.rectTransform, x, .41f, x, .89f, 0, 0, 2, 0);
                beam.raycastTarget = false;
            }
            var horizonGlow = factory.Image("Horizon Glow", parent,
                new Color(ElectricCyan.r, ElectricCyan.g, ElectricCyan.b, .33f));
            TouchUiFactory.Anchor(horizonGlow.rectTransform, .08f, .36f, .92f, .36f, 0, 0, 0, 3);
            horizonGlow.raycastTarget = false;
            var horizon = factory.Image("Horizon Core", parent,
                new Color(.63f, .96f, 1f, .66f));
            TouchUiFactory.Anchor(horizon.rectTransform, .22f, .37f, .78f, .37f, 0, 0, 0, 1);
            horizon.raycastTarget = false;
        }

        private void AddCornerBracket(Transform parent, string name, float x, float y,
            bool right, bool lower)
        {
            var edge = new Color(ElectricCyan.r, ElectricCyan.g, ElectricCyan.b, .9f);
            var horizontal = factory.Image(name + " Horizontal", parent, edge);
            var vertical = factory.Image(name + " Vertical", parent, edge);
            var anchorX = right ? 1 : 0;
            var anchorY = lower ? 0 : 1;
            var horizontalMinX = right ? x - 40 : x;
            var horizontalMaxX = right ? x : x + 40;
            TouchUiFactory.Anchor(horizontal.rectTransform, anchorX, anchorY, anchorX, anchorY,
                horizontalMinX, y, horizontalMaxX, y + 2);
            var verticalMinX = right ? x - 2 : x;
            var verticalMaxX = right ? x : x + 2;
            TouchUiFactory.Anchor(vertical.rectTransform, anchorX, anchorY, anchorX, anchorY,
                verticalMinX, lower ? y : y - 15, verticalMaxX, lower ? y + 15 : y);
            horizontal.raycastTarget = false;
            vertical.raycastTarget = false;
        }

        private static Sprite CreateGradientSprite(string name, Color upperLeft, Color lowerRight, Color glow)
        {
            const int width = 256;
            const int height = 128;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var u = x / (float)(width - 1);
                var v = y / (float)(height - 1);
                var color = Color.Lerp(lowerRight, upperLeft, .6f * v + .4f * (1 - u));
                var dx = (u - .22f) * 1.1f;
                var dy = (v - .76f) * .8f;
                var light = Mathf.Clamp01(1 - (dx * dx + dy * dy) * 3.2f);
                pixels[y * width + x] = Color.Lerp(color, glow, light * .24f);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height),
                new Vector2(.5f, .5f), 100);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
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
