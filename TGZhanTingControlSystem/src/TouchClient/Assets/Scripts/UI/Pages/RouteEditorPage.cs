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
    /// Touch-first visual route composer. It renders server-backed content and a RouteDraftState,
    /// then emits operator intent without calling APIs or the business facade.
    /// </summary>
    public sealed class RouteEditorPage
    {
        private readonly TouchUiFactory factory;
        private readonly TouchTheme theme;
        private readonly TouchImageLoader imageLoader;
        private readonly List<ModuleCard> moduleCardViews = new List<ModuleCard>();
        private readonly RectTransform root;
        private readonly Image headerFrame;
        private readonly Image headerSurface;
        private readonly Image moduleFrame;
        private readonly Image selectedFrame;
        private readonly Text modeLabel;
        private readonly Text saveStateLabel;
        private readonly Image saveStateBadge;
        private readonly InputField routeNameInput;
        private readonly Text statusLabel;
        private readonly Text moduleCaption;
        private readonly Text selectedCaption;
        private readonly Text sequencePreview;
        private readonly Button backButton;
        private readonly Button saveButton;
        private readonly Button saveAsButton;
        private readonly Button deleteButton;
        private readonly Button clearButton;
        private readonly Button startButton;
        private readonly RectTransform moduleGrid;
        private readonly RectTransform selectedGrid;
        private readonly ScrollRect moduleScroll;
        private readonly ScrollRect selectedScroll;
        private string moduleSignature;
        private string selectionSignature;
        private Func<string, string> assetUrlResolver;

        public RectTransform Root => root;
        public event Action BackRequested;
        public event Action<string> NameChanged;
        public event Action SaveRequested;
        public event Action SaveAsRequested;
        public event Action DeleteRequested;
        public event Action ClearRequested;
        public event Action StartRequested;
        public event Action<string> ModuleToggled;
        public event Action<string, int> ModuleMoveRequested;
        public event Action<string> ModuleRemoveRequested;

        public RouteEditorPage(TouchUiFactory factory, TouchTheme theme, TouchImageLoader imageLoader, Transform parent)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            this.imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));

            root = factory.Rect("Route Editor Page", parent);
            var pageLayout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            pageLayout.spacing = theme.SectionSpacing;
            pageLayout.childControlWidth = true;
            pageLayout.childControlHeight = true;
            pageLayout.childForceExpandHeight = false;

            headerFrame = factory.RoundedImage("Route Editor Header Frame", root, theme.Border);
            var headerElement = headerFrame.gameObject.AddComponent<LayoutElement>();
            headerElement.minHeight = theme.RouteEditorHeaderHeight;
            headerElement.preferredHeight = theme.RouteEditorHeaderHeight;
            headerElement.flexibleHeight = 0;
            headerSurface = factory.RoundedImage("Route Editor Header", headerFrame.transform, theme.SurfaceElevated);
            TouchUiFactory.Stretch(headerSurface.rectTransform, 1, 1, -1, -1);

            backButton = factory.TouchButton(headerSurface.transform, "← 返回", false,
                () => BackRequested?.Invoke());
            TouchUiFactory.Anchor(backButton.GetComponent<RectTransform>(), 0, .5f, 0, .5f,
                20, -32, 170, 32);

            var identity = factory.Rect("Route Draft Identity", headerSurface.transform);
            TouchUiFactory.Anchor(identity, 0, 0, 1, 1, 194, 14, -520, -14);
            modeLabel = factory.Label("Route Mode", identity, "临时组合", theme.Caption,
                FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(modeLabel.rectTransform, 0, 1, .55f, 1, 0, -30, 0, 0);
            saveStateBadge = factory.RoundedImage("Route Save State Badge", identity, theme.PrimarySoft);
            TouchUiFactory.Anchor(saveStateBadge.rectTransform, 1, 1, 1, 1, -176, -32, 0, 0);
            saveStateLabel = factory.Label("Route Save State", saveStateBadge.transform, "草稿已保存",
                theme.Caption, FontStyle.Bold, theme.TextSecondary, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(saveStateLabel.rectTransform, 8, 2, -8, -2);

            routeNameInput = factory.Input(identity, "输入路线名称（临时组合可不填写）", theme.Body, 48);
            routeNameInput.characterLimit = 60;
            TouchUiFactory.Anchor(routeNameInput.GetComponent<RectTransform>(), 0, 0, 1, 0,
                0, 34, 0, 82);
            routeNameInput.onValueChanged.AddListener(value => NameChanged?.Invoke(value));
            statusLabel = factory.Label("Route Editor Status", identity, "请选择讲解主题。", theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(statusLabel.rectTransform, 0, 0, 1, 0, 0, 0, 0, 28);

            saveButton = factory.TouchButton(headerSurface.transform, "保存路线", false,
                () => SaveRequested?.Invoke());
            TouchUiFactory.Anchor(saveButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -500, -32, -340, 32);
            saveAsButton = factory.TouchButton(headerSurface.transform, "另存为", false,
                () => SaveAsRequested?.Invoke());
            TouchUiFactory.Anchor(saveAsButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -328, -32, -178, 32);
            deleteButton = factory.TouchButton(headerSurface.transform, "删除路线", false,
                () => DeleteRequested?.Invoke());
            TouchUiFactory.Anchor(deleteButton.GetComponent<RectTransform>(), 1, .5f, 1, .5f,
                -166, -32, -20, 32);

            var workspace = factory.Rect("Route Editor Workspace", root);
            workspace.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var workspaceLayout = workspace.gameObject.AddComponent<HorizontalLayoutGroup>();
            workspaceLayout.spacing = theme.SectionSpacing;
            workspaceLayout.childControlWidth = true;
            workspaceLayout.childControlHeight = true;
            workspaceLayout.childForceExpandWidth = false;

            moduleFrame = factory.RoundedImage("Module Library Frame", workspace, theme.Border);
            moduleFrame.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var moduleSurface = factory.RoundedImage("Module Library Surface", moduleFrame.transform, theme.SurfaceElevated);
            TouchUiFactory.Stretch(moduleSurface.rectTransform, 1, 1, -1, -1);
            var moduleTitle = factory.Label("Module Library Title", moduleSurface.transform, "选择讲解主题",
                theme.CardTitle, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(moduleTitle.rectTransform, 0, 1, .55f, 1,
                theme.PanelPadding, -58, 0, -16);
            moduleCaption = factory.Label("Module Library Caption", moduleSurface.transform,
                "正在读取主题…", theme.Caption, FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(moduleCaption.rectTransform, .45f, 1, 1, 1,
                0, -58, -theme.PanelPadding, -16);
            moduleGrid = factory.ScrollGrid(moduleSurface.transform, "Route Module", 4,
                theme.RouteEditorModuleCellSize, new Vector2(theme.CardSpacing, theme.CardSpacing));
            moduleScroll = moduleGrid.parent.GetComponent<ScrollRect>();
            TouchUiFactory.Anchor(moduleGrid.parent.GetComponent<RectTransform>(), 0, 0, 1, 1,
                theme.PanelPadding, theme.PanelPadding, -theme.PanelPadding, -68);

            selectedFrame = factory.RoundedImage("Selected Route Frame", workspace, theme.Border);
            var selectedElement = selectedFrame.gameObject.AddComponent<LayoutElement>();
            selectedElement.minWidth = theme.RouteEditorSelectionWidth;
            selectedElement.preferredWidth = theme.RouteEditorSelectionWidth;
            var selectedSurface = factory.RoundedImage("Selected Route Surface", selectedFrame.transform, theme.SurfaceElevated);
            TouchUiFactory.Stretch(selectedSurface.rectTransform, 1, 1, -1, -1);
            var selectedTitle = factory.Label("Selected Route Title", selectedSurface.transform, "已选参观路线",
                theme.CardTitle, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(selectedTitle.rectTransform, 0, 1, .65f, 1,
                theme.PanelPadding, -54, 0, -16);
            selectedCaption = factory.Label("Selected Route Count", selectedSurface.transform, "0 个主题",
                theme.Caption, FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(selectedCaption.rectTransform, .55f, 1, 1, 1,
                0, -54, -theme.PanelPadding, -16);
            sequencePreview = factory.Label("Route Sequence Preview", selectedSurface.transform,
                "尚未选择主题", theme.Caption, FontStyle.Normal, theme.TextSecondary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(sequencePreview.rectTransform, 0, 1, 1, 1,
                theme.PanelPadding, -118, -theme.PanelPadding, -62);

            selectedGrid = factory.ScrollGrid(selectedSurface.transform, "Selected Route", 1,
                new Vector2(theme.RouteEditorSelectionWidth - 42, theme.RouteEditorSequenceItemHeight),
                new Vector2(0, 10));
            selectedScroll = selectedGrid.parent.GetComponent<ScrollRect>();
            TouchUiFactory.Anchor(selectedGrid.parent.GetComponent<RectTransform>(), 0, 0, 1, 1,
                theme.PanelPadding, 92, -theme.PanelPadding, -128);

            clearButton = factory.TouchButton(selectedSurface.transform, "清空", false,
                () => ClearRequested?.Invoke());
            TouchUiFactory.Anchor(clearButton.GetComponent<RectTransform>(), 0, 0, 0, 0,
                theme.PanelPadding, 18, 142, 78);
            startButton = factory.TouchButton(selectedSurface.transform, "开始讲解", true,
                () => StartRequested?.Invoke());
            TouchUiFactory.Anchor(startButton.GetComponent<RectTransform>(), 0, 0, 1, 0,
                154, 18, -theme.PanelPadding, 78);
        }

        public void Render(TouchUiState state, RouteDraftState draft, string status,
            Func<string, string> urlResolver)
        {
            if (state == null || draft == null) return;
            assetUrlResolver = urlResolver;
            if (!string.Equals(routeNameInput.text, draft.Name, StringComparison.Ordinal))
                routeNameInput.SetTextWithoutNotify(draft.Name);

            modeLabel.text = draft.IsTemporary ? "临时组合 · 不会自动保存" : "正式路线 · 编辑现有预案";
            modeLabel.color = theme.ConfigurableAccent;
            saveStateLabel.text = draft.IsDirty ? "存在未保存修改" : "当前草稿已保存";
            saveStateLabel.color = draft.IsDirty ? theme.Warning : theme.Success;
            saveStateBadge.color = draft.IsDirty
                ? Color.Lerp(theme.SurfaceSoft, theme.Warning, .18f)
                : Color.Lerp(theme.SurfaceSoft, theme.Success, .14f);
            statusLabel.text = string.IsNullOrWhiteSpace(status) ? "请选择讲解主题。" : status;

            var modules = state.Content?.modules?.Where(module => module != null && module.enabled)
                              .OrderBy(module => module.order).ToArray()
                          ?? Array.Empty<ExhibitionModule>();
            var selectedModules = draft.ModuleIds.Select(id => modules.FirstOrDefault(module =>
                    string.Equals(module.id, id, StringComparison.OrdinalIgnoreCase)))
                .Where(module => module != null).ToArray();
            moduleCaption.text = modules.Length + " 个主题 · 点击卡片添加或移除";
            selectedCaption.text = selectedModules.Length + " 个主题";
            sequencePreview.text = BuildSequencePreview(selectedModules);

            var idle = !state.HasActiveSession;
            backButton.GetComponentInChildren<Text>().text = draft.LeaveConfirmationPending ? "放弃修改" : "← 返回";
            saveButton.interactable = idle && selectedModules.Length > 0;
            saveAsButton.interactable = idle && selectedModules.Length > 0;
            deleteButton.interactable = idle && !draft.IsTemporary;
            clearButton.interactable = idle && selectedModules.Length > 0;
            startButton.interactable = idle && state.Connected && state.Readiness?.canStart == true &&
                                       selectedModules.Any(HasConfiguredNarration);
            startButton.GetComponent<Image>().color = startButton.interactable
                ? theme.Primary : theme.SecondaryButton;
            StyleDeleteButton(draft.DeleteConfirmationPending);

            RebuildModulesIfNeeded(state, draft, modules);
            RebuildSelectionIfNeeded(state, draft, selectedModules);
        }

        public void OnShown()
        {
            Canvas.ForceUpdateCanvases();
            if (moduleScroll != null) moduleScroll.verticalNormalizedPosition = 1;
            if (selectedScroll != null) selectedScroll.verticalNormalizedPosition = 1;
        }

        public void FocusNameInput()
        {
            routeNameInput.ActivateInputField();
            routeNameInput.MoveTextEnd(false);
        }

        public void RefreshTheme()
        {
            headerFrame.color = theme.Border;
            moduleFrame.color = theme.Border;
            selectedFrame.color = theme.Border;
            modeLabel.color = theme.ConfigurableAccent;
            selectedCaption.color = theme.ConfigurableAccent;
            moduleSignature = null;
            selectionSignature = null;
            foreach (var card in moduleCardViews) card.RefreshTheme();
        }

        private void RebuildModulesIfNeeded(TouchUiState state, RouteDraftState draft, ExhibitionModule[] modules)
        {
            var signature = BuildModuleSignature(state, draft, modules);
            if (string.Equals(moduleSignature, signature, StringComparison.Ordinal)) return;
            moduleSignature = signature;
            var previousScroll = moduleScroll == null ? 1 : moduleScroll.verticalNormalizedPosition;
            moduleCardViews.Clear();
            TouchUiFactory.Clear(moduleGrid);
            for (var index = 0; index < modules.Length; index++)
            {
                var module = modules[index];
                var selectedIndex = IndexOf(draft.ModuleIds, module.id);
                var coverUrl = Resolve(module.coverUrl);
                var card = new ModuleCard(factory, theme, imageLoader, moduleGrid, module,
                    selectedIndex < 0 ? 0 : selectedIndex + 1, HasConfiguredNarration(module), coverUrl,
                    !state.HasActiveSession, id => ModuleToggled?.Invoke(id));
                moduleCardViews.Add(card);
            }
            Canvas.ForceUpdateCanvases();
            if (moduleScroll != null) moduleScroll.verticalNormalizedPosition = previousScroll;
        }

        private void RebuildSelectionIfNeeded(TouchUiState state, RouteDraftState draft,
            ExhibitionModule[] selectedModules)
        {
            var signature = BuildSelectionSignature(state, draft, selectedModules);
            if (string.Equals(selectionSignature, signature, StringComparison.Ordinal)) return;
            selectionSignature = signature;
            var previousScroll = selectedScroll == null ? 1 : selectedScroll.verticalNormalizedPosition;
            TouchUiFactory.Clear(selectedGrid);
            if (selectedModules.Length == 0)
            {
                var empty = factory.RoundedImage("Empty Selected Route", selectedGrid, theme.SurfaceSoft);
                var label = factory.Label("Empty Selected Route Label", empty.transform,
                    "点击左侧主题卡片\n按参观顺序加入路线", theme.Body, FontStyle.Bold,
                    theme.TextSecondary, TextAnchor.MiddleCenter);
                TouchUiFactory.Stretch(label.rectTransform, 12, 8, -12, -8);
            }
            else
            {
                for (var index = 0; index < selectedModules.Length; index++)
                    BuildSelectedItem(selectedModules[index], index, selectedModules.Length, !state.HasActiveSession);
            }
            Canvas.ForceUpdateCanvases();
            if (selectedScroll != null) selectedScroll.verticalNormalizedPosition = previousScroll;
        }

        private void BuildSelectedItem(ExhibitionModule module, int index, int count, bool interactable)
        {
            var item = factory.RoundedImage("Selected Module - " + module.name, selectedGrid, theme.SurfaceSoft);
            var order = factory.RoundedImage("Selected Module Order", item.transform, theme.ConfigurableAccent);
            TouchUiFactory.Anchor(order.rectTransform, 0, .5f, 0, .5f, 12, -25, 58, 25);
            var orderText = factory.Label("Selected Module Order Label", order.transform,
                (index + 1).ToString("00"), theme.Body, FontStyle.Bold, theme.Background, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(orderText.rectTransform, 4, 2, -4, -2);

            var name = factory.Label("Selected Module Name", item.transform, module.name, theme.Caption,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(name.rectTransform, 0, 0, 1, 1, 70, 8, -214, -8);
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 12;
            name.resizeTextMaxSize = theme.Caption;

            var up = factory.TouchButton(item.transform, "上移", false,
                () => ModuleMoveRequested?.Invoke(module.id, -1));
            TouchUiFactory.Anchor(up.GetComponent<RectTransform>(), 1, .5f, 1, .5f, -208, -31, -144, 31);
            var down = factory.TouchButton(item.transform, "下移", false,
                () => ModuleMoveRequested?.Invoke(module.id, 1));
            TouchUiFactory.Anchor(down.GetComponent<RectTransform>(), 1, .5f, 1, .5f, -138, -31, -74, 31);
            var remove = factory.TouchButton(item.transform, "移除", false,
                () => ModuleRemoveRequested?.Invoke(module.id));
            TouchUiFactory.Anchor(remove.GetComponent<RectTransform>(), 1, .5f, 1, .5f, -68, -31, -4, 31);
            up.interactable = interactable && index > 0;
            down.interactable = interactable && index < count - 1;
            remove.interactable = interactable;
        }

        private void StyleDeleteButton(bool confirming)
        {
            var image = deleteButton.GetComponent<Image>();
            var label = deleteButton.GetComponentInChildren<Text>();
            label.text = confirming ? "确认删除" : "删除路线";
            image.color = confirming ? theme.Error : Color.Lerp(theme.SurfaceSoft, theme.Error, .12f);
            label.color = confirming ? theme.TextPrimary : theme.Error;
        }

        private string Resolve(string url) => string.IsNullOrWhiteSpace(url)
            ? null
            : assetUrlResolver?.Invoke(url) ?? url;

        private static string BuildSequencePreview(IEnumerable<ExhibitionModule> modules)
        {
            var values = modules.Select((module, index) => (index + 1).ToString("00") + " " + module.name).ToArray();
            return values.Length == 0 ? "尚未选择主题 · 从左侧开始编排" : string.Join("  →  ", values);
        }

        private static int IndexOf(IReadOnlyList<string> values, string id)
        {
            for (var index = 0; index < values.Count; index++)
                if (string.Equals(values[index], id, StringComparison.OrdinalIgnoreCase)) return index;
            return -1;
        }

        private static bool HasConfiguredNarration(ExhibitionModule module) => module?.nodes != null &&
            module.nodes.Any(node => node != null &&
                (!string.IsNullOrWhiteSpace(node.narrationText) || !string.IsNullOrWhiteSpace(node.ttsAudioUrl)));

        private static string BuildModuleSignature(TouchUiState state, RouteDraftState draft,
            IEnumerable<ExhibitionModule> modules)
        {
            var builder = new StringBuilder();
            builder.Append(state.Content?.version).Append('|').Append(state.HasActiveSession).Append('|')
                .Append(string.Join(",", draft.ModuleIds)).Append('|');
            foreach (var module in modules)
                builder.Append(module.id).Append(':').Append(module.name).Append(':').Append(module.description)
                    .Append(':').Append(module.coverUrl).Append(':').Append(module.order).Append(';');
            return builder.ToString();
        }

        private static string BuildSelectionSignature(TouchUiState state, RouteDraftState draft,
            IEnumerable<ExhibitionModule> modules) =>
            state.HasActiveSession + "|" + string.Join(",", draft.ModuleIds) + "|" +
            string.Join(";", modules.Select(module => module.id + ":" + module.name));
    }
}
