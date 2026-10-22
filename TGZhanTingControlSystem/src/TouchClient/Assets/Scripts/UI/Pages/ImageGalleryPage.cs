using System;
using System.Collections.Generic;
using System.Linq;
using TG.Control.Touch.UI.Components;
using TG.Control.Touch.UI.Services;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Pages
{
    /// <summary>
    /// Operator-facing gallery browser. It owns preview selection only and emits business intent;
    /// Server-authoritative gallery state remains in TouchControlFacade/TouchUiPresenter.
    /// </summary>
    public sealed class ImageGalleryPage
    {
        private sealed class ModuleItem
        {
            public string ModuleId;
            public Image Surface;
            public Text Title;
            public Text Count;
        }

        private readonly TouchUiFactory factory;
        private readonly TouchTheme theme;
        private readonly TouchImageLoader imageLoader;
        private readonly RectTransform root;
        private readonly RectTransform moduleList;
        private readonly RectTransform thumbnailList;
        private readonly ScrollRect thumbnailScroll;
        private readonly Image preview;
        private readonly GameObject previewPlaceholder;
        private readonly Text previewTitle;
        private readonly Text previewCounter;
        private readonly Text statusTitle;
        private readonly Text statusDetail;
        private readonly Text feedback;
        private readonly Button displayButton;
        private readonly Button previousButton;
        private readonly Button nextButton;
        private readonly Button slideshowButton;
        private readonly Button stopButton;
        private readonly Button thumbnailScrollPreviousButton;
        private readonly Button thumbnailScrollNextButton;
        private readonly List<ModuleItem> moduleItems = new List<ModuleItem>();
        private readonly List<Image> thumbnailSurfaces = new List<Image>();

        private PublishedContent content;
        private ExhibitionModule selectedModule;
        private string observedSessionId;
        private int previewIndex;
        private long renderedContentVersion = long.MinValue;
        private bool previewDirtyByUser;
        private bool slideshowActive;

        private const float ThumbnailWidth = 174f;
        private const float ThumbnailSpacing = 12f;

        public RectTransform Root => root;
        public event Action<string, int> DisplayRequested;
        public event Action PreviousRequested;
        public event Action NextRequested;
        public event Action StartSlideshowRequested;
        public event Action PauseSlideshowRequested;
        public event Action StopRequested;

        public ImageGalleryPage(TouchUiFactory factory, TouchTheme theme, TouchImageLoader imageLoader,
            Transform parent)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            this.imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));
            root = factory.Rect("Image Gallery Page", parent);

            var header = factory.RoundedImage("Gallery Header", root, theme.TechPanelSurface);
            TouchUiFactory.Anchor(header.rectTransform, 0, 1, 1, 1, 0, -100, 0, 0);
            header.raycastTarget = false;
            var eyebrow = factory.Label("Gallery Eyebrow", header.transform, "LED 大屏 · 图片展示", theme.Caption,
                FontStyle.Bold, theme.HomeDarkAccent, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(eyebrow.rectTransform, 0, 1, .6f, 1, 24, -34, 0, -10);
            var title = factory.Label("Gallery Title", header.transform, "模块图集", theme.PageTitle,
                FontStyle.Bold, theme.HomeDarkTextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, 0, .55f, 1, 24, 12, 0, -36);
            var hint = factory.Label("Gallery Hint", header.transform,
                "选择缩略图只会更新本机预览，点击“投放到大屏”后才会改变 LED 画面。", theme.Secondary,
                FontStyle.Normal, theme.HomeDarkTextSecondary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(hint.rectTransform, .48f, 0, 1, 1, 0, 12, -24, -14);

            var moduleFrame = factory.TechPanel("Gallery Module", root, theme.TechBorderSoft,
                theme.TechPanelSurface, out var moduleSurface);
            TouchUiFactory.Anchor(moduleFrame.rectTransform, 0, 0, 0, 1, 0, 0, 310, -112);
            var moduleTitle = factory.Label("Gallery Module Heading", moduleSurface.transform, "选择展厅模块",
                theme.SectionTitle, FontStyle.Bold, theme.HomeDarkTextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(moduleTitle.rectTransform, 0, 1, 1, 1, 20, -62, -20, -14);
            var moduleViewport = factory.Image("Gallery Module Viewport", moduleSurface.transform, Color.clear);
            TouchUiFactory.Anchor(moduleViewport.rectTransform, 0, 0, 1, 1, 14, 14, -14, -74);
            moduleViewport.gameObject.AddComponent<RectMask2D>();
            moduleList = factory.Rect("Gallery Module List", moduleViewport.transform);
            TouchUiFactory.Anchor(moduleList, 0, 1, 1, 1, 0, 0, 0, 0);
            moduleList.pivot = new Vector2(.5f, 1);
            var moduleLayout = moduleList.gameObject.AddComponent<VerticalLayoutGroup>();
            moduleLayout.spacing = 10;
            moduleLayout.childControlWidth = true;
            moduleLayout.childControlHeight = true;
            moduleLayout.childForceExpandHeight = false;
            moduleList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var moduleScroll = moduleViewport.gameObject.AddComponent<ScrollRect>();
            moduleScroll.viewport = moduleViewport.rectTransform;
            moduleScroll.content = moduleList;
            moduleScroll.horizontal = false;
            moduleScroll.scrollSensitivity = 32;

            var previewFrame = factory.TechPanel("Gallery Preview", root, theme.TechBorder,
                theme.TechPreviewSurface, out var previewSurface, 2f);
            TouchUiFactory.Anchor(previewFrame.rectTransform, 0, 0, 1, 1, 326, 174, -350, -112);
            preview = factory.Image("Gallery Large Preview", previewSurface.transform, Color.clear);
            TouchUiFactory.Stretch(preview.rectTransform, 20, 54, -20, -20);
            preview.preserveAspect = true;
            preview.raycastTarget = false;
            previewPlaceholder = factory.RoundedImage("Gallery Preview Placeholder", previewSurface.transform,
                theme.TechPanelElevated).gameObject;
            TouchUiFactory.Anchor(previewPlaceholder.GetComponent<RectTransform>(), .5f, .5f, .5f, .5f,
                -150, -64, 150, 64);
            var placeholderText = factory.Label("Gallery Preview Placeholder Text", previewPlaceholder.transform,
                "请选择图集图片", theme.Body, FontStyle.Bold, theme.HomeDarkTextSecondary,
                TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(placeholderText.rectTransform, 12, 8, -12, -8);
            previewTitle = factory.Label("Gallery Preview Title", previewSurface.transform, string.Empty,
                theme.CardTitle, FontStyle.Bold, theme.HomeDarkTextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(previewTitle.rectTransform, 0, 0, .78f, 0, 20, 10, 0, 48);
            previewCounter = factory.Label("Gallery Preview Counter", previewSurface.transform, string.Empty,
                theme.Secondary, FontStyle.Bold, theme.HomeDarkAccent, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(previewCounter.rectTransform, .72f, 0, 1, 0, 0, 10, -20, 48);

            var thumbnailFrame = factory.TechPanel("Gallery Thumbnail", root, theme.TechBorderSoft,
                theme.TechPanelSurface, out var thumbnailSurface);
            TouchUiFactory.Anchor(thumbnailFrame.rectTransform, 0, 0, 1, 0, 326, 0, -350, 158);
            var thumbnailViewport = factory.Image("Gallery Thumbnail Viewport", thumbnailSurface.transform, Color.clear);
            TouchUiFactory.Stretch(thumbnailViewport.rectTransform, 76, 12, -76, -12);
            thumbnailViewport.gameObject.AddComponent<RectMask2D>();
            thumbnailList = factory.Rect("Gallery Thumbnail List", thumbnailViewport.transform);
            TouchUiFactory.Anchor(thumbnailList, 0, 0, 0, 1, 0, 0, 0, 0);
            thumbnailList.pivot = new Vector2(0, .5f);
            var thumbnailLayout = thumbnailList.gameObject.AddComponent<HorizontalLayoutGroup>();
            thumbnailLayout.spacing = ThumbnailSpacing;
            thumbnailLayout.childControlWidth = false;
            thumbnailLayout.childControlHeight = true;
            thumbnailLayout.childForceExpandHeight = true;
            thumbnailList.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            thumbnailScroll = thumbnailViewport.gameObject.AddComponent<ScrollRect>();
            thumbnailScroll.viewport = thumbnailViewport.rectTransform;
            thumbnailScroll.content = thumbnailList;
            thumbnailScroll.horizontal = true;
            thumbnailScroll.vertical = false;
            thumbnailScroll.scrollSensitivity = 36;
            thumbnailScroll.movementType = ScrollRect.MovementType.Clamped;
            thumbnailScroll.onValueChanged.AddListener(_ => UpdateThumbnailScrollButtons());

            thumbnailScrollPreviousButton = factory.TechButton(thumbnailSurface.transform, "<", false,
                () => ScrollThumbnails(-1));
            TouchUiFactory.Anchor(thumbnailScrollPreviousButton.GetComponent<RectTransform>(), 0, 0, 0, 1,
                14, 24, 64, -24);
            StyleThumbnailScrollButton(thumbnailScrollPreviousButton);

            thumbnailScrollNextButton = factory.TechButton(thumbnailSurface.transform, ">", false,
                () => ScrollThumbnails(1));
            TouchUiFactory.Anchor(thumbnailScrollNextButton.GetComponent<RectTransform>(), 1, 0, 1, 1,
                -64, 24, -14, -24);
            StyleThumbnailScrollButton(thumbnailScrollNextButton);
            UpdateThumbnailScrollButtons();

            var controlFrame = factory.TechPanel("Gallery Control", root, theme.TechBorderSoft,
                theme.TechPanelElevated, out var controlSurface);
            TouchUiFactory.Anchor(controlFrame.rectTransform, 1, 0, 1, 1, -334, 0, 0, -112);
            var statusCaption = factory.Label("Gallery Status Caption", controlSurface.transform, "大屏状态",
                theme.Caption, FontStyle.Bold, theme.HomeDarkAccent, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(statusCaption.rectTransform, 0, 1, 1, 1, 20, -36, -20, -12);
            statusTitle = factory.Label("Gallery Status Title", controlSurface.transform, "尚未投放",
                theme.SectionTitle, FontStyle.Bold, theme.HomeDarkTextPrimary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(statusTitle.rectTransform, 0, 1, 1, 1, 20, -86, -20, -42);
            statusDetail = factory.Label("Gallery Status Detail", controlSurface.transform,
                "选择一张图片并投放到LED大屏。", theme.Secondary, FontStyle.Normal,
                theme.HomeDarkTextSecondary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(statusDetail.rectTransform, 0, .52f, 1, 1, 20, 0, -20, -96);
            feedback = factory.Label("Gallery Feedback", controlSurface.transform, string.Empty, theme.Caption,
                FontStyle.Normal, theme.Warning, TextAnchor.LowerLeft);
            TouchUiFactory.Anchor(feedback.rectTransform, 0, 0, 1, .48f, 20, 316, -20, 0);

            displayButton = factory.TechButton(controlSurface.transform, "投放到大屏", true, OnDisplay);
            TouchUiFactory.Anchor(displayButton.GetComponent<RectTransform>(), 0, 0, 1, 0,
                20, 238, -20, 306);
            previousButton = factory.TechButton(controlSurface.transform, "上一张", false, OnPrevious);
            TouchUiFactory.Anchor(previousButton.GetComponent<RectTransform>(), 0, 0, .5f, 0,
                20, 164, -6, 224);
            nextButton = factory.TechButton(controlSurface.transform, "下一张", false, OnNext);
            TouchUiFactory.Anchor(nextButton.GetComponent<RectTransform>(), .5f, 0, 1, 0,
                6, 164, -20, 224);
            slideshowButton = factory.TechButton(controlSurface.transform, "开始轮播 · 8秒", false,
                OnSlideshow);
            TouchUiFactory.Anchor(slideshowButton.GetComponent<RectTransform>(), 0, 0, 1, 0,
                20, 90, -20, 150);
            stopButton = factory.TechButton(controlSurface.transform, "停止展示", false,
                () => StopRequested?.Invoke(), true);
            TouchUiFactory.Anchor(stopButton.GetComponent<RectTransform>(), 0, 0, 1, 0,
                20, 16, -20, 76);
        }

        public void Render(TouchUiState state, Func<string, string> normalizeUrl)
        {
            if (state == null) return;
            content = state.Content;
            if (content != null && content.version != renderedContentVersion)
            {
                renderedContentVersion = content.version;
                BuildModules(state, normalizeUrl);
            }

            var session = state.Gallery;
            if (session != null && !string.Equals(observedSessionId, session.sessionId, StringComparison.Ordinal))
            {
                observedSessionId = session.sessionId;
                previewDirtyByUser = false;
                SelectModule(session.moduleId, normalizeUrl);
                previewIndex = Mathf.Max(0, session.currentImageNumber - 1);
                LoadPreview(normalizeUrl);
            }
            else if (session == null && !state.HasActiveGallery)
            {
                observedSessionId = null;
            }

            if (session != null && !previewDirtyByUser && selectedModule != null &&
                string.Equals(selectedModule.id, session.moduleId, StringComparison.OrdinalIgnoreCase))
            {
                var serverIndex = Mathf.Max(0, session.currentImageNumber - 1);
                if (serverIndex != previewIndex)
                {
                    previewIndex = serverIndex;
                    LoadPreview(normalizeUrl);
                }
            }

            RenderModuleSelection(session);
            RenderThumbnailSelection(session);
            RenderStatus(state);
        }

        public void RefreshTheme()
        {
            // The gallery uses fixed structural blue tokens; status and interactions stay state-bound.
        }

        public void OnShown()
        {
            // Re-entering the page always presents the strip from its natural left edge. The preview
            // selection remains server/user-driven and will scroll back into view when it changes.
            ResetThumbnailScroll();
        }

        public void ShowFeedback(string message)
        {
            feedback.text = message ?? string.Empty;
            feedback.color = theme.Error;
        }

        public void ClearFeedback()
        {
            feedback.text = string.Empty;
        }

        private void BuildModules(TouchUiState state, Func<string, string> normalizeUrl)
        {
            TouchUiFactory.Clear(moduleList);
            moduleItems.Clear();
            var modules = state.Content?.modules?.Where(module => module != null && module.enabled &&
                module.galleryImages != null && module.galleryImages.Length > 0)
                .OrderBy(module => module.order).ToArray() ?? Array.Empty<ExhibitionModule>();
            foreach (var module in modules)
            {
                var captured = module;
                var surface = factory.RoundedImage("Gallery Module - " + module.name, moduleList,
                    theme.TechPanelSurface);
                surface.gameObject.AddComponent<LayoutElement>().preferredHeight = 78;
                var button = surface.gameObject.AddComponent<Button>();
                button.targetGraphic = surface;
                button.onClick.AddListener(() =>
                {
                    previewDirtyByUser = false;
                    SelectModule(captured.id, normalizeUrl);
                });
                var title = factory.Label("Module Name", surface.transform, module.name, theme.Body,
                    FontStyle.Bold, theme.HomeDarkTextPrimary, TextAnchor.MiddleLeft);
                TouchUiFactory.Anchor(title.rectTransform, 0, 0, 1, 1, 16, 24, -14, 0);
                var count = factory.Label("Module Image Count", surface.transform,
                    module.galleryImages.Length + " 张图片", theme.Caption, FontStyle.Normal,
                    theme.HomeDarkTextSecondary, TextAnchor.MiddleLeft);
                TouchUiFactory.Anchor(count.rectTransform, 0, 0, 1, 0, 16, 4, -14, 26);
                moduleItems.Add(new ModuleItem { ModuleId = module.id, Surface = surface, Title = title, Count = count });
            }

            var preferredId = state.Gallery?.moduleId;
            if (string.IsNullOrWhiteSpace(preferredId) || modules.All(module => module.id != preferredId))
                preferredId = selectedModule != null && modules.Any(module => module.id == selectedModule.id)
                    ? selectedModule.id : modules.FirstOrDefault()?.id;
            SelectModule(preferredId, normalizeUrl);
            if (modules.Length == 0)
            {
                var empty = factory.Label("Gallery Modules Empty", moduleList,
                    "当前正式内容尚未配置模块图集。", theme.Secondary, FontStyle.Normal,
                    theme.HomeDarkTextSecondary, TextAnchor.MiddleCenter);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120;
            }
        }

        private void SelectModule(string moduleId, Func<string, string> normalizeUrl)
        {
            var module = content?.modules?.FirstOrDefault(item => item != null &&
                string.Equals(item.id, moduleId, StringComparison.OrdinalIgnoreCase));
            if (module == null || module.galleryImages == null || module.galleryImages.Length == 0)
            {
                selectedModule = null;
                previewIndex = 0;
                BuildThumbnails(normalizeUrl);
                return;
            }
            if (selectedModule != null && string.Equals(selectedModule.id, module.id, StringComparison.OrdinalIgnoreCase))
            {
                RenderModuleSelection(null);
                return;
            }
            selectedModule = module;
            previewIndex = 0;
            feedback.text = string.Empty;
            BuildThumbnails(normalizeUrl);
            LoadPreview(normalizeUrl);
            RenderModuleSelection(null);
        }

        private void BuildThumbnails(Func<string, string> normalizeUrl)
        {
            TouchUiFactory.Clear(thumbnailList);
            thumbnailSurfaces.Clear();
            if (selectedModule?.galleryImages == null)
            {
                ResetThumbnailScroll();
                return;
            }
            for (var index = 0; index < selectedModule.galleryImages.Length; index++)
            {
                var capturedIndex = index;
                var asset = selectedModule.galleryImages[index];
                var frame = factory.RoundedImage("Gallery Thumbnail " + (index + 1), thumbnailList,
                    theme.TechBorderSoft);
                var element = frame.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = ThumbnailWidth;
                element.minWidth = ThumbnailWidth;
                var image = factory.Image("Thumbnail Image", frame.transform, theme.TechPreviewSurface);
                TouchUiFactory.Stretch(image.rectTransform, 3, 3, -3, -3);
                image.preserveAspect = true;
                image.raycastTarget = false;
                var button = frame.gameObject.AddComponent<Button>();
                button.targetGraphic = frame;
                button.onClick.AddListener(() =>
                {
                    previewDirtyByUser = true;
                    previewIndex = capturedIndex;
                    LoadPreview(normalizeUrl);
                    RenderThumbnailSelection(null);
                });
                var number = factory.Label("Thumbnail Number", frame.transform, (index + 1).ToString("00"),
                    theme.Caption, FontStyle.Bold, Color.white, TextAnchor.UpperLeft);
                TouchUiFactory.Anchor(number.rectTransform, 0, .7f, .35f, 1, 10, 0, 0, -7);
                thumbnailSurfaces.Add(frame);
                if (!string.IsNullOrWhiteSpace(asset?.url))
                    imageLoader.LoadContain(image, normalizeUrl(asset.url));
            }
            ResetThumbnailScroll();
        }

        private void LoadPreview(Func<string, string> normalizeUrl)
        {
            var images = selectedModule?.galleryImages;
            if (images == null || images.Length == 0)
            {
                preview.sprite = null;
                preview.color = Color.clear;
                previewPlaceholder.SetActive(true);
                previewTitle.text = "当前模块没有可展示图片";
                previewCounter.text = "0 / 0";
                return;
            }
            previewIndex = Mathf.Clamp(previewIndex, 0, images.Length - 1);
            var asset = images[previewIndex];
            preview.sprite = null;
            preview.color = Color.clear;
            previewPlaceholder.SetActive(true);
            previewTitle.text = string.IsNullOrWhiteSpace(asset?.name)
                ? selectedModule.name + " · 图片 " + (previewIndex + 1)
                : asset.name;
            previewCounter.text = (previewIndex + 1) + " / " + images.Length;
            EnsureThumbnailVisible(previewIndex);
            if (asset == null || string.IsNullOrWhiteSpace(asset.url)) return;
            var expectedModule = selectedModule.id;
            var expectedIndex = previewIndex;
            imageLoader.LoadContain(preview, normalizeUrl(asset.url), loaded =>
            {
                if (selectedModule != null && selectedModule.id == expectedModule && previewIndex == expectedIndex)
                    previewPlaceholder.SetActive(!loaded);
            });
        }

        private void StyleThumbnailScrollButton(Button button)
        {
            var label = button.GetComponentInChildren<Text>();
            if (label == null) return;
            label.fontSize = 32;
            label.fontStyle = FontStyle.Bold;
            label.color = theme.HomeDarkTextPrimary;
        }

        private void ResetThumbnailScroll()
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(thumbnailList);
            thumbnailScroll.StopMovement();
            thumbnailScroll.horizontalNormalizedPosition = 0;
            UpdateThumbnailScrollButtons();
        }

        private void ScrollThumbnails(int direction)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(thumbnailList);
            var viewportWidth = thumbnailScroll.viewport.rect.width;
            var scrollableWidth = Mathf.Max(0, thumbnailList.rect.width - viewportWidth);
            if (scrollableWidth <= .5f)
            {
                UpdateThumbnailScrollButtons();
                return;
            }

            var currentOffset = thumbnailScroll.horizontalNormalizedPosition * scrollableWidth;
            var step = Mathf.Max((ThumbnailWidth + ThumbnailSpacing) * 2, viewportWidth * .65f);
            var targetOffset = Mathf.Clamp(currentOffset + direction * step, 0, scrollableWidth);
            thumbnailScroll.StopMovement();
            thumbnailScroll.horizontalNormalizedPosition = targetOffset / scrollableWidth;
            UpdateThumbnailScrollButtons();
        }

        private void EnsureThumbnailVisible(int index)
        {
            if (index < 0 || index >= thumbnailSurfaces.Count) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(thumbnailList);
            var viewportWidth = thumbnailScroll.viewport.rect.width;
            var scrollableWidth = Mathf.Max(0, thumbnailList.rect.width - viewportWidth);
            if (scrollableWidth <= .5f)
            {
                thumbnailScroll.horizontalNormalizedPosition = 0;
                UpdateThumbnailScrollButtons();
                return;
            }

            var currentOffset = thumbnailScroll.horizontalNormalizedPosition * scrollableWidth;
            var itemStart = index * (ThumbnailWidth + ThumbnailSpacing);
            var itemEnd = itemStart + ThumbnailWidth;
            var targetOffset = currentOffset;
            if (itemStart < currentOffset)
                targetOffset = itemStart;
            else if (itemEnd > currentOffset + viewportWidth)
                targetOffset = itemEnd - viewportWidth;

            thumbnailScroll.horizontalNormalizedPosition = Mathf.Clamp01(targetOffset / scrollableWidth);
            UpdateThumbnailScrollButtons();
        }

        private void UpdateThumbnailScrollButtons()
        {
            if (thumbnailScrollPreviousButton == null || thumbnailScrollNextButton == null ||
                thumbnailScroll == null) return;
            var hasOverflow = thumbnailList.rect.width > thumbnailScroll.viewport.rect.width + .5f;
            thumbnailScrollPreviousButton.interactable = hasOverflow &&
                                                        thumbnailScroll.horizontalNormalizedPosition > .001f;
            thumbnailScrollNextButton.interactable = hasOverflow &&
                                                    thumbnailScroll.horizontalNormalizedPosition < .999f;
        }

        private void RenderModuleSelection(GallerySessionStatus session)
        {
            foreach (var item in moduleItems)
            {
                var selected = selectedModule != null && string.Equals(item.ModuleId, selectedModule.id,
                    StringComparison.OrdinalIgnoreCase);
                var active = session != null && string.Equals(item.ModuleId, session.moduleId,
                    StringComparison.OrdinalIgnoreCase);
                item.Surface.color = selected ? theme.TechSelectionSurface : theme.TechPanelSurface;
                item.Title.color = selected ? Color.white : theme.HomeDarkTextPrimary;
                item.Count.text = (active ? "大屏展示中 · " : string.Empty) +
                                  (content?.modules?.FirstOrDefault(module => module.id == item.ModuleId)
                                      ?.galleryImages?.Length ?? 0) + " 张图片";
                item.Count.color = active ? theme.Success : theme.HomeDarkTextSecondary;
            }
        }

        private void RenderThumbnailSelection(GallerySessionStatus session)
        {
            for (var index = 0; index < thumbnailSurfaces.Count; index++)
            {
                var selected = index == previewIndex;
                var showing = session != null && selectedModule != null &&
                              string.Equals(session.moduleId, selectedModule.id, StringComparison.OrdinalIgnoreCase) &&
                              index == session.currentImageNumber - 1;
                thumbnailSurfaces[index].color = showing ? theme.Success
                    : selected ? theme.TechBorder : theme.TechBorderSoft;
            }
        }

        private void RenderStatus(TouchUiState state)
        {
            var gallery = state.Gallery;
            slideshowActive = gallery?.slideshowActive == true;
            var active = state.HasActiveGallery;
            var activeSameModule = gallery != null && selectedModule != null &&
                                   string.Equals(gallery.moduleId, selectedModule.id,
                                       StringComparison.OrdinalIgnoreCase);
            if (!state.Connected)
            {
                statusTitle.text = "Server连接中断";
                statusDetail.text = "正在自动重连，连接恢复前无法投放或控制大屏图片。";
            }
            else if (active && gallery == null)
            {
                statusTitle.text = "正在恢复图集状态";
                statusDetail.text = "Server已记录活动图集，正在读取当前大屏图片。";
            }
            else if (gallery == null)
            {
                statusTitle.text = "尚未投放";
                statusDetail.text = state.Readiness?.ledOnline == true
                    ? "LED播放端在线。选择图片后点击“投放到大屏”。"
                    : "LED播放端离线，暂时不能开始图片展示。";
            }
            else
            {
                var stateText = gallery.state == GallerySessionState.Failed ? "展示异常"
                    : gallery.state == GallerySessionState.Showing ? "正在展示" : "正在准备";
                statusTitle.text = stateText + " · " + gallery.moduleName;
                statusDetail.text = $"当前图片：{gallery.imageName}\n进度：{gallery.currentImageNumber} / {gallery.imageCount}" +
                                    (gallery.slideshowActive
                                        ? $"\n自动轮播：{gallery.slideshowIntervalSeconds}秒"
                                        : "\n自动轮播：未开启") +
                                    (string.IsNullOrWhiteSpace(gallery.error) ? string.Empty : "\n" + gallery.error);
            }

            var welcomeBusy = state.Welcome != null &&
                              (state.Welcome.state == WelcomePlaybackState.Requested ||
                               state.Welcome.state == WelcomePlaybackState.Preparing ||
                               state.Welcome.state == WelcomePlaybackState.Playing);
            var canStart = state.Connected && state.Readiness?.ledOnline == true && !state.HasActiveSession &&
                           !welcomeBusy && selectedModule != null && !active;
            var canShow = state.Connected && activeSameModule && !state.HasActiveSession;
            displayButton.interactable = canStart || canShow;
            displayButton.GetComponentInChildren<Text>().text = activeSameModule ? "投放所选图片" : "投放到大屏";
            previousButton.interactable = activeSameModule;
            nextButton.interactable = activeSameModule;
            slideshowButton.interactable = activeSameModule;
            stopButton.interactable = state.Connected && active;
            slideshowButton.GetComponentInChildren<Text>().text = slideshowActive
                ? "暂停自动轮播" : "开始轮播 · 8秒";
            RenderModuleSelection(gallery);
            RenderThumbnailSelection(gallery);
        }

        private void OnDisplay()
        {
            if (selectedModule == null) return;
            DisplayRequested?.Invoke(selectedModule.id, previewIndex);
        }

        private void OnPrevious()
        {
            previewDirtyByUser = false;
            PreviousRequested?.Invoke();
        }

        private void OnNext()
        {
            previewDirtyByUser = false;
            NextRequested?.Invoke();
        }

        private void OnSlideshow()
        {
            if (!string.IsNullOrWhiteSpace(observedSessionId) && slideshowActive)
                PauseSlideshowRequested?.Invoke();
            else
                StartSlideshowRequested?.Invoke();
        }

    }
}
