using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TG.Control.Touch.UI;
using TG.Control.Touch.UI.Components;
using TG.Control.Touch.UI.Pages;
using TG.Control.Touch.UI.Services;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TG.Control.Touch
{
    /// <summary>Runtime UGUI console; independent from scene layout and configurable by the server.</summary>
    public sealed class TouchOperatorUi : MonoBehaviour
    {
        [SerializeField] private TouchApiClient apiClient;
        [SerializeField] private TouchControlFacade facade;

        private enum PageState { Home, RouteEditor, Playback }

        private readonly HashSet<string> selected = new HashSet<string>();
        private readonly List<string> selectionOrder = new List<string>();
        private PublishedContent content;
        private PlaybackSessionStatus session;
        private SystemReadiness readiness;
        private NarrationRoute[] routes = Array.Empty<NarrationRoute>();
        private string activeRouteId;
        private string routeName = string.Empty;
        private bool connected;
        private bool routeDirty;
        private bool confirmStop;
        private bool confirmDelete;
        private bool confirmLeaveEditor;
        private string status = "正在连接展厅服务…";
        private TouchUiPresenter presenter;
        private TouchTheme theme;
        private TouchUiFactory uiFactory;
        private Color accent;
        private PageState pageState;
        private bool navigateToPlaybackWhenSessionArrives;

        private Image background;
        private TouchAppShell appShell;
        private ReceptionHomePage receptionHomePage;
        private TouchImageLoader imageLoader;
        private InputField routeInput;
        private Text routeCaption;
        private Text selectionSummary;
        private Text editorStatusLabel;
        private Text statusLabel;
        private RectTransform moduleCards;
        private ScrollRect moduleScroll;
        private GameObject homePage;
        private GameObject editorPage;
        private GameObject playbackPage;
        private Text playbackModuleLabel;
        private Text playbackNodeLabel;
        private Text playbackProgressLabel;
        private Image playbackProgressFill;
        private Button saveButton;
        private Button backButton;
        private Button deleteButton;
        private Button startButton;
        private Button pauseButton;
        private Button resumeButton;
        private Button skipButton;
        private Button retryButton;
        private Button stopButton;

        private Color Ink => theme.Ink;
        private Color Muted => theme.Muted;
        private Color Gold => theme.Gold;

        private void Awake()
        {
            theme = TouchTheme.CreateDefault();
            accent = theme.Primary;
            uiFactory = new TouchUiFactory(theme.CreateFont(), () => accent, theme);
            BuildUi();
        }

        private void Start()
        {
            presenter = new TouchUiPresenter(apiClient, facade);
            presenter.ConnectionChanged += OnConnectionChanged;
            presenter.ContentLoaded += OnContentLoaded;
            presenter.StatusChanged += OnStatus;
            presenter.SessionChanged += OnSessionChanged;
            presenter.RoutesLoaded += OnRoutesLoaded;
            presenter.RouteSaved += OnRouteSaved;
            presenter.ReadinessChanged += OnReadinessChanged;
            presenter.UiExperienceChanged += ApplyUiExperience;
            presenter.UiExperienceLoadFailed += OnUiExperienceLoadFailed;
            presenter.Error += OnError;
            presenter.Attach();

            connected = presenter.State.Connected;
            routes = presenter.State.Routes;
            readiness = presenter.State.Readiness;
            StartCoroutine(UiExperienceLoop());
            if (presenter.State.Content != null) OnContentLoaded(presenter.State.Content);
            Refresh();
        }

        private void Update() => appShell?.Tick(DateTime.Now);

        private void OnDestroy()
        {
            if (appShell != null) appShell.NavigationRequested -= OnShellNavigationRequested;
            if (receptionHomePage != null)
            {
                receptionHomePage.TemporaryRequested -= NewTemporaryRoute;
                receptionHomePage.StartAllRequested -= StartAllFromHome;
                receptionHomePage.ContinuePlaybackRequested -= ContinueCurrentPlayback;
                receptionHomePage.RouteStartRequested -= StartRoute;
                receptionHomePage.RouteEditRequested -= EditRoute;
            }
            if (presenter == null) return;
            presenter.ConnectionChanged -= OnConnectionChanged;
            presenter.ContentLoaded -= OnContentLoaded;
            presenter.StatusChanged -= OnStatus;
            presenter.SessionChanged -= OnSessionChanged;
            presenter.RoutesLoaded -= OnRoutesLoaded;
            presenter.RouteSaved -= OnRouteSaved;
            presenter.ReadinessChanged -= OnReadinessChanged;
            presenter.UiExperienceChanged -= ApplyUiExperience;
            presenter.UiExperienceLoadFailed -= OnUiExperienceLoadFailed;
            presenter.Error -= OnError;
            presenter.Dispose();
        }

        private void BuildUi()
        {
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)).transform.SetParent(transform, false);

            var canvasObject = new GameObject("Touch UGUI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            appShell = new TouchAppShell(uiFactory, theme);
            appShell.Build(canvas.transform);
            appShell.NavigationRequested += OnShellNavigationRequested;
            background = appShell.Background;

            var body = appShell.ContentRoot;
            imageLoader = new TouchImageLoader(this);
            receptionHomePage = new ReceptionHomePage(uiFactory, theme, imageLoader, body);
            receptionHomePage.TemporaryRequested += NewTemporaryRoute;
            receptionHomePage.StartAllRequested += StartAllFromHome;
            receptionHomePage.ContinuePlaybackRequested += ContinueCurrentPlayback;
            receptionHomePage.RouteStartRequested += StartRoute;
            receptionHomePage.RouteEditRequested += EditRoute;
            homePage = receptionHomePage.Root.gameObject;
            editorPage = BuildEditorPage(body).gameObject;
            playbackPage = BuildPlaybackPage(body).gameObject;
            Stretch(homePage.GetComponent<RectTransform>());
            Stretch(editorPage.GetComponent<RectTransform>());
            Stretch(playbackPage.GetComponent<RectTransform>());
            ShowPage(PageState.Home);
        }

        private RectTransform BuildEditorPage(Transform parent)
        {
            var page = Rect("Route Editor Page", parent);
            var pageLayout = page.gameObject.AddComponent<VerticalLayoutGroup>();
            pageLayout.spacing = 12;
            pageLayout.childControlWidth = true;
            pageLayout.childControlHeight = true;
            pageLayout.childForceExpandHeight = false;

            var toolbar = Panel("Route Editor Toolbar", page, theme.SurfaceElevated);
            toolbar.gameObject.AddComponent<LayoutElement>().preferredHeight = 176;
            var toolbarLayout = toolbar.gameObject.AddComponent<VerticalLayoutGroup>();
            toolbarLayout.padding = new RectOffset(14, 14, 10, 10);
            toolbarLayout.spacing = 8;
            toolbarLayout.childControlWidth = true;
            toolbarLayout.childControlHeight = true;
            toolbarLayout.childForceExpandHeight = false;

            var routeRow = Row(toolbar, 10, 72);
            routeRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            backButton = Button(routeRow, "← 返回首页", false, ReturnHome);
            backButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 145;
            var editorInfo = Rect("Route Identity", routeRow);
            editorInfo.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var infoLayout = editorInfo.gameObject.AddComponent<VerticalLayoutGroup>();
            infoLayout.spacing = 4;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandHeight = false;
            routeCaption = LayoutLabel(editorInfo, "当前路线：临时组合", 16, FontStyle.Bold, Ink, 25);
            routeInput = Input(editorInfo, "输入路线名称", 15, 40);
            routeInput.onValueChanged.AddListener(value => { routeName = value; routeDirty = true; confirmLeaveEditor = false; RefreshRouteState(); });
            saveButton = Button(routeRow, "保存路线", true, SaveRoute);
            saveButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 140;
            var saveAs = Button(routeRow, "另存为", false, SaveAsRoute);
            saveAs.gameObject.AddComponent<LayoutElement>().preferredWidth = 120;
            deleteButton = Button(routeRow, "删除路线", false, DeleteRoute);
            deleteButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 120;

            var actionRow = Row(toolbar, 10, 76);
            actionRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var information = Rect("Selection And Status", actionRow);
            information.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var informationLayout = information.gameObject.AddComponent<VerticalLayoutGroup>();
            informationLayout.spacing = 2;
            informationLayout.childControlWidth = true;
            informationLayout.childControlHeight = true;
            informationLayout.childForceExpandHeight = false;
            selectionSummary = LayoutLabel(information, "尚未选择主题", 14, FontStyle.Bold, Ink, 34);
            editorStatusLabel = LayoutLabel(information, status, 13, FontStyle.Normal, Muted, 30);

            var clear = Button(actionRow, "清空选择", false, () =>
            {
                if (facade.HasActiveSession) return;
                selected.Clear(); selectionOrder.Clear(); routeDirty = true; confirmLeaveEditor = false; RebuildModules(); RefreshRouteState();
            });
            clear.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
            startButton = Button(actionRow, "开始此路线", true, StartSelected);
            startButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 220;

            var modules = Panel("Theme Editor", page, theme.SurfaceElevated);
            modules.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var moduleLayout = modules.gameObject.AddComponent<VerticalLayoutGroup>();
            moduleLayout.padding = new RectOffset(10, 10, 10, 10);
            moduleLayout.spacing = 8;
            moduleLayout.childControlWidth = true;
            moduleLayout.childControlHeight = true;
            moduleLayout.childForceExpandHeight = false;
            LayoutLabel(modules, "选择并编排讲解主题", 21, FontStyle.Bold, Ink, 34);
            moduleCards = ScrollGrid(modules, "Module", 4, theme.ModuleGridCellSize,
                new Vector2(theme.CardSpacing, theme.CardSpacing));
            moduleScroll = moduleCards.parent.GetComponent<ScrollRect>();
            return page;
        }

        private RectTransform BuildPlaybackPage(Transform parent)
        {
            var page = Panel("Playback Page", parent, theme.SurfaceElevated);
            var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(150, 150, 90, 70);
            layout.spacing = 14;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var state = LayoutLabel(page, "正在讲解", 20, FontStyle.Bold, accent, 38);
            state.alignment = TextAnchor.MiddleCenter;
            playbackModuleLabel = LayoutLabel(page, "正在准备讲解内容", 42, FontStyle.Bold, Ink, 84);
            playbackModuleLabel.alignment = TextAnchor.MiddleCenter;
            playbackNodeLabel = LayoutLabel(page, "请稍候…", 22, FontStyle.Normal, Muted, 52);
            playbackNodeLabel.alignment = TextAnchor.MiddleCenter;

            var spacer = Rect("Playback Spacer", page);
            spacer.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
            playbackProgressLabel = LayoutLabel(page, "0 / 0", 18, FontStyle.Bold, Ink, 34);
            playbackProgressLabel.alignment = TextAnchor.MiddleCenter;
            var progressTrack = Image("Playback Progress", page, theme.Border);
            progressTrack.gameObject.AddComponent<LayoutElement>().preferredHeight = 18;
            playbackProgressFill = Image("Progress Fill", progressTrack.transform, accent);
            Anchor(playbackProgressFill.rectTransform, 0, 0, 0, 1, 0, 0, 0, 0);

            var flexible = Rect("Playback Flexible Space", page);
            flexible.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            statusLabel = LayoutLabel(page, status, 15, FontStyle.Normal, Muted, 42);
            statusLabel.alignment = TextAnchor.MiddleCenter;

            var controls = Row(page, 14, 86);
            controls.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(180, 180, 0, 0);
            pauseButton = Button(controls, "暂停讲解", false, () => facade.Pause());
            resumeButton = Button(controls, "继续讲解", true, () => facade.Resume());
            retryButton = Button(controls, "重试当前节点", false, () => facade.Retry());
            skipButton = Button(controls, "跳过当前节点", false, () => facade.Skip());
            stopButton = Button(controls, "终止讲解", false, ConfirmStop);
            return page;
        }

        private void OnConnectionChanged(bool value)
        {
            connected = value;
            status = value ? "系统已连接，可以开始讲解。" : "服务器连接中断，正在自动重连…";
            if (value) receptionHomePage?.ClearError();
            Refresh();
        }

        private void OnContentLoaded(PublishedContent value)
        {
            content = value;
            selected.RemoveWhere(id => value.modules.All(module => module.id != id || !module.enabled));
            selectionOrder.RemoveAll(id => !selected.Contains(id));
            status = $"内容版本 V{value.version} 已加载，共 {value.modules.Length} 个主题。";
            RebuildModules();
            Refresh();
        }

        private void OnReadinessChanged(SystemReadiness value)
        {
            readiness = value;
            Refresh();
        }

        private void OnRoutesLoaded(NarrationRoute[] value)
        {
            routes = value ?? Array.Empty<NarrationRoute>();
            if (string.IsNullOrWhiteSpace(activeRouteId))
            {
                var lastId = PlayerPrefs.GetString("TG.LastRouteId", string.Empty);
                var remembered = routes.FirstOrDefault(item => item.id == lastId) ?? routes.FirstOrDefault();
                if (remembered != null) LoadRoute(remembered);
            }
            Refresh();
        }

        private void OnRouteSaved(NarrationRoute value)
        {
            if (value != null) LoadRoute(value);
            routeDirty = false;
            confirmLeaveEditor = false;
            status = value == null ? "路线已保存。" : "路线“" + value.name + "”已保存，可直接开始讲解。";
            Refresh();
        }

        private void OnSessionChanged(PlaybackSessionStatus value)
        {
            session = value;
            confirmStop = false;
            if (value != null && navigateToPlaybackWhenSessionArrives)
            {
                navigateToPlaybackWhenSessionArrives = false;
                ShowPage(PageState.Playback);
            }
            else if (value == null && pageState == PageState.Playback) ShowPage(PageState.Home);
            Refresh();
        }

        private void OnStatus(string value)
        {
            status = value;
            receptionHomePage?.ClearError();
            if (facade.HasActiveSession && navigateToPlaybackWhenSessionArrives)
            {
                navigateToPlaybackWhenSessionArrives = false;
                ShowPage(PageState.Playback);
            }
            Refresh();
        }

        private void OnError(string value)
        {
            navigateToPlaybackWhenSessionArrives = false;
            status = "操作失败：" + value;
            receptionHomePage?.ShowError(status);
            Refresh();
        }

        private void LoadRoute(NarrationRoute route)
        {
            selected.Clear();
            selectionOrder.Clear();
            foreach (var id in route.moduleIds ?? Array.Empty<string>())
            {
                if (content?.modules.Any(module => module.id == id && module.enabled) == false) continue;
                if (selected.Add(id)) selectionOrder.Add(id);
            }
            activeRouteId = route.id;
            routeName = route.name;
            routeDirty = false;
            confirmLeaveEditor = false;
            routeInput.SetTextWithoutNotify(routeName);
            PlayerPrefs.SetString("TG.LastRouteId", route.id);
            PlayerPrefs.Save();
            status = "已加载常用路线：" + route.name;
            RebuildModules();
            RefreshRouteState();
        }

        private void NewTemporaryRoute()
        {
            activeRouteId = null;
            routeName = string.Empty;
            selected.Clear();
            selectionOrder.Clear();
            routeDirty = false;
            confirmDelete = false;
            confirmLeaveEditor = false;
            routeInput.SetTextWithoutNotify(string.Empty);
            status = "临时组合：请按讲解顺序选择主题，也可以保存为常用路线。";
            RebuildModules();
            RefreshRouteState();
            ShowPage(PageState.RouteEditor);
        }

        private void EditRoute(NarrationRoute route)
        {
            LoadRoute(route);
            confirmLeaveEditor = false;
            ShowPage(PageState.RouteEditor);
        }

        private void ReturnHome()
        {
            if (!routeDirty)
            {
                confirmLeaveEditor = false;
                ShowPage(PageState.Home);
                return;
            }
            if (!confirmLeaveEditor)
            {
                confirmLeaveEditor = true;
                status = "当前路线有未保存修改。请先保存，或再次点击“放弃修改”返回首页。";
                Refresh();
                return;
            }

            confirmLeaveEditor = false;
            var saved = routes.FirstOrDefault(item => item.id == activeRouteId);
            if (saved != null) LoadRoute(saved);
            else
            {
                selected.Clear();
                selectionOrder.Clear();
                routeName = string.Empty;
                routeDirty = false;
                routeInput.SetTextWithoutNotify(string.Empty);
                RebuildModules();
            }
            status = "已放弃未保存的路线修改。";
            ShowPage(PageState.Home);
            Refresh();
        }

        private void StartRoute(NarrationRoute route)
        {
            LoadRoute(route);
            status = "正在启动路线：“" + route.name + "”…";
            navigateToPlaybackWhenSessionArrives = true;
            facade.StartModules(route.moduleIds ?? Array.Empty<string>());
            Refresh();
        }

        private void StartAllFromHome()
        {
            status = "正在准备全部主题讲解…";
            navigateToPlaybackWhenSessionArrives = true;
            facade.StartAll();
            Refresh();
        }

        private void ContinueCurrentPlayback()
        {
            if (!facade.HasActiveSession) return;
            ShowPage(PageState.Playback);
            Refresh();
        }

        private void SaveRoute()
        {
            if (!connected) { OnError("保存路线需要连接展厅服务器。"); return; }
            if (string.IsNullOrWhiteSpace(routeName))
            {
                routeName = "常用路线 " + (routes.Length + 1);
                routeInput.SetTextWithoutNotify(routeName);
            }
            facade.SaveRoute(activeRouteId, routeName, selectionOrder.ToArray());
        }

        private void SaveAsRoute()
        {
            activeRouteId = null;
            routeName = "路线 " + (routes.Length + 1);
            routeDirty = true;
            confirmLeaveEditor = false;
            routeInput.SetTextWithoutNotify(routeName);
            RefreshRouteState();
            routeInput.ActivateInputField();
        }

        private void DeleteRoute()
        {
            if (string.IsNullOrWhiteSpace(activeRouteId)) return;
            if (!confirmDelete)
            {
                confirmDelete = true;
                status = "再次点击“确认删除”将永久删除当前路线。";
                Refresh();
                return;
            }
            confirmDelete = false;
            var id = activeRouteId;
            activeRouteId = null;
            routeName = string.Empty;
            selected.Clear();
            selectionOrder.Clear();
            routeDirty = false;
            routeInput.SetTextWithoutNotify(string.Empty);
            PlayerPrefs.DeleteKey("TG.LastRouteId");
            facade.DeleteRoute(id);
            RebuildModules();
            ShowPage(PageState.Home);
        }

        private void StartSelected()
        {
            if (selectionOrder.Count == 0) return;
            status = "正在准备讲解路线…";
            navigateToPlaybackWhenSessionArrives = true;
            facade.StartModules(selectionOrder.ToArray());
            Refresh();
        }

        private void ConfirmStop()
        {
            if (!confirmStop)
            {
                confirmStop = true;
                status = "再次点击“确认终止”停止本次讲解。";
                Refresh();
                return;
            }
            confirmStop = false;
            facade.Stop();
        }

        private void AddSelection(string moduleId)
        {
            if (facade.HasActiveSession || !selected.Add(moduleId)) return;
            selectionOrder.Add(moduleId);
            routeDirty = true;
            confirmLeaveEditor = false;
            RebuildModules();
            RefreshRouteState();
        }

        private void RemoveSelection(string moduleId)
        {
            if (facade.HasActiveSession) return;
            selected.Remove(moduleId);
            selectionOrder.Remove(moduleId);
            routeDirty = true;
            confirmLeaveEditor = false;
            RebuildModules();
            RefreshRouteState();
        }

        private void MoveSelection(string moduleId, int direction)
        {
            if (facade.HasActiveSession) return;
            var index = selectionOrder.IndexOf(moduleId);
            var destination = index + direction;
            if (index < 0 || destination < 0 || destination >= selectionOrder.Count) return;
            selectionOrder.RemoveAt(index);
            selectionOrder.Insert(destination, moduleId);
            routeDirty = true;
            confirmLeaveEditor = false;
            RebuildModules();
            RefreshRouteState();
        }

        private void ShowPage(PageState state)
        {
            var changed = pageState != state;
            pageState = state;
            if (homePage != null) homePage.SetActive(state == PageState.Home);
            if (editorPage != null) editorPage.SetActive(state == PageState.RouteEditor);
            if (playbackPage != null) playbackPage.SetActive(state == PageState.Playback);
            if (appShell != null)
            {
                var section = state == PageState.Playback ? TouchShellSection.Playback
                    : state == PageState.RouteEditor
                        ? (string.IsNullOrWhiteSpace(activeRouteId) ? TouchShellSection.Combination : TouchShellSection.Routes)
                        : TouchShellSection.ReceptionHome;
                appShell.SetActiveSection(section);
            }
            if (changed && state == PageState.RouteEditor && moduleScroll != null)
            {
                Canvas.ForceUpdateCanvases();
                moduleScroll.verticalNormalizedPosition = 1;
            }
        }

        private void OnShellNavigationRequested(TouchShellSection section)
        {
            if (section == TouchShellSection.Playback)
            {
                if (facade.HasActiveSession) ShowPage(PageState.Playback);
                return;
            }
            if (section == TouchShellSection.Combination)
            {
                if (facade.HasActiveSession)
                {
                    status = "当前讲解尚未结束，请先完成或终止后再编辑主题组合。";
                    Refresh();
                    return;
                }
                NewTemporaryRoute();
                return;
            }

            if (pageState == PageState.RouteEditor)
            {
                ReturnHome();
                if (pageState != PageState.Home) return;
            }
            else
            {
                ShowPage(PageState.Home);
            }
            appShell.SetActiveSection(section);
        }

        private void RebuildModules()
        {
            if (moduleCards == null) return;
            Clear(moduleCards);
            foreach (var item in content?.modules.Where(value => value.enabled).OrderBy(value => value.order) ?? Enumerable.Empty<ExhibitionModule>())
            {
                var module = item;
                var isSelected = selected.Contains(module.id);
                var card = Panel("Module " + module.order, moduleCards,
                    isSelected ? theme.PrimaryMuted : theme.Surface);
                var element = card.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 447;
                element.preferredHeight = 185;
                FreeLabel(card, module.order.ToString("00"), 16, FontStyle.Bold, Gold, 18, 10, 16, 26);
                FreeLabel(card, module.name, 20, FontStyle.Bold, Ink, 18, 39, 16, 34);
                var description = string.IsNullOrWhiteSpace(module.description) ? "展厅主题讲解内容" : module.description;
                if (!HasContent(module)) description = "内容待配置，讲解时将自动跳过";
                FreeLabel(card, description, 13, FontStyle.Normal, Muted, 18, 76, 16, 38);

                var actions = Rect("Module Actions", card);
                Anchor(actions, 0, 0, 1, 0, 14, 12, -14, 56);
                var actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
                actionLayout.spacing = 6;
                actionLayout.childControlWidth = true;
                actionLayout.childControlHeight = true;
                actionLayout.childForceExpandWidth = true;
                if (!isSelected)
                {
                    var add = Button(actions, HasContent(module) ? "+ 加入路线" : "+ 加入（内容待配置）", false, () => AddSelection(module.id));
                    add.interactable = !facade.HasActiveSession;
                }
                else
                {
                    var index = selectionOrder.IndexOf(module.id);
                    var order = Button(actions, "顺序 " + (index + 1), true, () => { });
                    order.interactable = false;
                    var up = Button(actions, "↑", false, () => MoveSelection(module.id, -1));
                    var down = Button(actions, "↓", false, () => MoveSelection(module.id, 1));
                    var remove = Button(actions, "移除", false, () => RemoveSelection(module.id));
                    up.interactable = index > 0;
                    down.interactable = index >= 0 && index < selectionOrder.Count - 1;
                    remove.interactable = !facade.HasActiveSession;
                }
            }
        }

        private void Refresh()
        {
            if (appShell == null) return;
            appShell.SetGlobalState(connected, readiness, facade.HasActiveSession);
            if (presenter != null) receptionHomePage?.Render(presenter.State, presenter.NormalizeAssetUrl);
            if (editorStatusLabel != null) editorStatusLabel.text = status;
            if (statusLabel != null) statusLabel.text = status;
            if (playbackModuleLabel != null)
                playbackModuleLabel.text = string.IsNullOrWhiteSpace(session?.moduleName) ? "正在准备讲解内容" : session.moduleName;
            if (playbackNodeLabel != null)
                playbackNodeLabel.text = string.IsNullOrWhiteSpace(session?.nodeName) ? "正在同步大屏与语音，请稍候…" : session.nodeName;
            if (playbackProgressLabel != null)
                playbackProgressLabel.text = session == null ? "0 / 0" : session.currentNodeNumber + " / " + session.totalNodes;
            if (playbackProgressFill != null)
            {
                var progress = session == null || session.totalNodes <= 0 ? 0 : Mathf.Clamp01((float)session.currentNodeNumber / session.totalNodes);
                playbackProgressFill.rectTransform.anchorMax = new Vector2(progress, 1);
                playbackProgressFill.rectTransform.offsetMin = Vector2.zero;
                playbackProgressFill.rectTransform.offsetMax = Vector2.zero;
            }
            RefreshRouteState();
        }

        private void RefreshRouteState()
        {
            if (routeCaption == null) return;
            routeCaption.text = "当前路线：" + (string.IsNullOrWhiteSpace(activeRouteId) ? "临时组合" : routeName) + (routeDirty ? " · 未保存" : string.Empty);
            var modules = selectionOrder.Select(id => content?.modules.FirstOrDefault(item => item.id == id))
                .Where(item => item != null).ToArray();
            selectionSummary.text = modules.Length == 0
                ? "尚未选择主题 · 点击主题卡片加入讲解路线"
                : "讲解顺序（" + modules.Length + " 个）：" + string.Join(" → ", modules.Select((item, index) => (index + 1) + ". " + item.name));
            var idle = !facade.HasActiveSession;
            saveButton.interactable = idle && modules.Length > 0;
            deleteButton.interactable = idle && !string.IsNullOrWhiteSpace(activeRouteId);
            deleteButton.GetComponentInChildren<Text>().text = confirmDelete ? "确认删除" : "删除路线";
            backButton.GetComponentInChildren<Text>().text = confirmLeaveEditor ? "放弃修改" : "← 返回首页";
            startButton.interactable = CanStart && idle && modules.Any(HasContent);
            pauseButton.gameObject.SetActive(session != null && !session.paused);
            resumeButton.gameObject.SetActive(session != null && session.paused);
            pauseButton.interactable = resumeButton.interactable = retryButton.interactable = skipButton.interactable = stopButton.interactable = connected && !idle;
            stopButton.GetComponentInChildren<Text>().text = confirmStop ? "确认终止" : "终止讲解";
        }

        private void ApplyUiExperience(UiExperienceConfig config)
        {
            if (config == null) return;
            appShell.SetBranding(config.touchTitle, config.touchSubtitle);
            if (ColorUtility.TryParseHtmlString(config.touchAccentColor, out var color))
            {
                theme.SetConfigurableAccent(color);
                receptionHomePage?.RefreshTheme();
            }
            background.sprite = null;
            background.color = ColorUtility.TryParseHtmlString(config.touchBackgroundColor, out color)
                ? color : theme.Background;
            Refresh();
        }

        private IEnumerator UiExperienceLoop()
        {
            while (enabled)
            {
                presenter?.RefreshUiExperience();
                yield return new WaitForSecondsRealtime(10);
            }
        }

        private void OnUiExperienceLoadFailed(string message) =>
            Debug.LogWarning("读取中控界面配置失败：" + message);

        private bool CanStart => connected && readiness?.canStart == true;
        private static bool HasContent(ExhibitionModule module) => module?.nodes != null && module.nodes.Any(node => node != null && (!string.IsNullOrWhiteSpace(node.narrationText) || !string.IsNullOrWhiteSpace(node.ttsAudioUrl)));

        private RectTransform ScrollGrid(Transform parent, string prefix, int columns, Vector2 cellSize, Vector2 spacing) => uiFactory.ScrollGrid(parent, prefix, columns, cellSize, spacing);
        private RectTransform Row(Transform parent, float spacing, float height) => uiFactory.Row(parent, spacing, height);
        private Button Button(Transform parent, string text, bool primary, UnityEngine.Events.UnityAction action) => uiFactory.Button(parent, text, primary, action);
        private InputField Input(Transform parent, string placeholder, int size, float height) => uiFactory.Input(parent, placeholder, size, height);
        private RectTransform Panel(string name, Transform parent, Color color) => uiFactory.Panel(name, parent, color);
        private Image Image(string name, Transform parent, Color color) => uiFactory.Image(name, parent, color);
        private RectTransform Rect(string name, Transform parent) => uiFactory.Rect(name, parent);
        private Text Label(string name, Transform parent, string value, int size, FontStyle style, Color color, TextAnchor alignment) => uiFactory.Label(name, parent, value, size, style, color, alignment);
        private Text LayoutLabel(Transform parent, string value, int size, FontStyle style, Color color, float height) => uiFactory.LayoutLabel(parent, value, size, style, color, height);
        private void FreeLabel(Transform parent, string value, int size, FontStyle style, Color color, float left, float top, float right, float height, TextAnchor alignment = TextAnchor.MiddleLeft) => uiFactory.FreeLabel(parent, value, size, style, color, left, top, right, height, alignment);
        private void Divider(Transform parent) => uiFactory.Divider(parent);
        private static void Clear(Transform root) => TouchUiFactory.Clear(root);
        private static void Stretch(RectTransform rect, float left = 0, float bottom = 0, float right = 0, float top = 0) => TouchUiFactory.Stretch(rect, left, bottom, right, top);
        private static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY, float left, float bottom, float right, float top) => TouchUiFactory.Anchor(rect, minX, minY, maxX, maxY, left, bottom, right, top);
    }
}
