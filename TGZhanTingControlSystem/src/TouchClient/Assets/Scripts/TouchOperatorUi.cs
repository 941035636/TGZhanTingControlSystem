using System;
using System.Collections;
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

        private readonly RouteDraftState routeDraft = new RouteDraftState();
        private PublishedContent content;
        private PlaybackSessionStatus session;
        private SystemReadiness readiness;
        private NarrationRoute[] routes = Array.Empty<NarrationRoute>();
        private bool connected;
        private bool confirmStop;
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
        private RouteEditorPage routeEditorPage;
        private TouchImageLoader imageLoader;
        private Text statusLabel;
        private GameObject homePage;
        private GameObject editorPage;
        private GameObject playbackPage;
        private Text playbackModuleLabel;
        private Text playbackNodeLabel;
        private Text playbackProgressLabel;
        private Image playbackProgressFill;
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
            if (routeEditorPage != null)
            {
                routeEditorPage.BackRequested -= ReturnHome;
                routeEditorPage.NameChanged -= ChangeRouteName;
                routeEditorPage.SaveRequested -= SaveRoute;
                routeEditorPage.SaveAsRequested -= SaveAsRoute;
                routeEditorPage.DeleteRequested -= DeleteRoute;
                routeEditorPage.ClearRequested -= ClearSelection;
                routeEditorPage.StartRequested -= StartSelected;
                routeEditorPage.ModuleToggled -= ToggleSelection;
                routeEditorPage.ModuleMoveRequested -= MoveSelection;
                routeEditorPage.ModuleRemoveRequested -= RemoveSelection;
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
            routeEditorPage = new RouteEditorPage(uiFactory, theme, imageLoader, body);
            routeEditorPage.BackRequested += ReturnHome;
            routeEditorPage.NameChanged += ChangeRouteName;
            routeEditorPage.SaveRequested += SaveRoute;
            routeEditorPage.SaveAsRequested += SaveAsRoute;
            routeEditorPage.DeleteRequested += DeleteRoute;
            routeEditorPage.ClearRequested += ClearSelection;
            routeEditorPage.StartRequested += StartSelected;
            routeEditorPage.ModuleToggled += ToggleSelection;
            routeEditorPage.ModuleMoveRequested += MoveSelection;
            routeEditorPage.ModuleRemoveRequested += RemoveSelection;
            editorPage = routeEditorPage.Root.gameObject;
            playbackPage = BuildPlaybackPage(body).gameObject;
            Stretch(homePage.GetComponent<RectTransform>());
            Stretch(editorPage.GetComponent<RectTransform>());
            Stretch(playbackPage.GetComponent<RectTransform>());
            ShowPage(PageState.Home);
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
            routeDraft.RetainAvailable(value.modules.Where(module => module.enabled).Select(module => module.id));
            status = $"内容版本 V{value.version} 已加载，共 {value.modules.Length} 个主题。";
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
            if (routeDraft.IsTemporary)
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
            var available = content?.modules.Where(module => module.enabled).Select(module => module.id);
            routeDraft.Load(route, available);
            PlayerPrefs.SetString("TG.LastRouteId", route.id);
            PlayerPrefs.Save();
            status = "已加载常用路线：" + route.name;
            Refresh();
        }

        private void NewTemporaryRoute()
        {
            routeDraft.BeginTemporary();
            status = "临时组合：请按讲解顺序选择主题，也可以保存为常用路线。";
            Refresh();
            ShowPage(PageState.RouteEditor);
        }

        private void EditRoute(NarrationRoute route)
        {
            LoadRoute(route);
            ShowPage(PageState.RouteEditor);
        }

        private void ReturnHome()
        {
            if (!routeDraft.IsDirty)
            {
                routeDraft.CancelLeaveConfirmation();
                ShowPage(PageState.Home);
                return;
            }
            if (!routeDraft.ArmLeaveConfirmation())
            {
                status = "当前路线有未保存修改。请先保存，或再次点击“放弃修改”返回首页。";
                Refresh();
                return;
            }

            routeDraft.CancelLeaveConfirmation();
            var saved = routes.FirstOrDefault(item => item.id == routeDraft.RouteId);
            if (saved != null) LoadRoute(saved);
            else routeDraft.BeginTemporary();
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
            if (string.IsNullOrWhiteSpace(routeDraft.Name))
            {
                routeDraft.SetName("常用路线 " + (routes.Length + 1));
            }
            facade.SaveRoute(routeDraft.RouteId, routeDraft.Name, routeDraft.SnapshotModuleIds());
        }

        private void SaveAsRoute()
        {
            routeDraft.DetachForSaveAs("路线 " + (routes.Length + 1));
            status = "已切换为新路线，请确认名称后保存。";
            Refresh();
            routeEditorPage?.FocusNameInput();
        }

        private void DeleteRoute()
        {
            if (routeDraft.IsTemporary) return;
            if (!routeDraft.ArmDeleteConfirmation())
            {
                status = "再次点击“确认删除”将永久删除当前路线。";
                Refresh();
                return;
            }
            var id = routeDraft.RouteId;
            routeDraft.BeginTemporary();
            PlayerPrefs.DeleteKey("TG.LastRouteId");
            facade.DeleteRoute(id);
            ShowPage(PageState.Home);
        }

        private void StartSelected()
        {
            if (routeDraft.ModuleIds.Count == 0) return;
            status = "正在准备讲解路线…";
            navigateToPlaybackWhenSessionArrives = true;
            facade.StartModules(routeDraft.SnapshotModuleIds());
            Refresh();
        }

        private void ChangeRouteName(string value)
        {
            routeDraft.SetName(value);
            Refresh();
        }

        private void ToggleSelection(string moduleId)
        {
            if (facade.HasActiveSession) return;
            if (routeDraft.ModuleIds.Any(id => string.Equals(id, moduleId, StringComparison.OrdinalIgnoreCase)))
                routeDraft.Remove(moduleId);
            else
                routeDraft.Add(moduleId);
            Refresh();
        }

        private void ClearSelection()
        {
            if (facade.HasActiveSession) return;
            if (routeDraft.Clear()) status = "已清空当前路线，请重新选择讲解主题。";
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

        private void RemoveSelection(string moduleId)
        {
            if (facade.HasActiveSession) return;
            routeDraft.Remove(moduleId);
            Refresh();
        }

        private void MoveSelection(string moduleId, int direction)
        {
            if (facade.HasActiveSession) return;
            routeDraft.Move(moduleId, direction);
            Refresh();
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
                        ? (routeDraft.IsTemporary ? TouchShellSection.Combination : TouchShellSection.Routes)
                        : TouchShellSection.ReceptionHome;
                appShell.SetActiveSection(section);
            }
            if (changed && state == PageState.RouteEditor) routeEditorPage?.OnShown();
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

        private void Refresh()
        {
            if (appShell == null) return;
            appShell.SetGlobalState(connected, readiness, facade.HasActiveSession);
            if (presenter != null) receptionHomePage?.Render(presenter.State, presenter.NormalizeAssetUrl);
            if (presenter != null) routeEditorPage?.Render(presenter.State, routeDraft, status, presenter.NormalizeAssetUrl);
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
            var idle = !facade.HasActiveSession;
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
                routeEditorPage?.RefreshTheme();
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

        private RectTransform Row(Transform parent, float spacing, float height) => uiFactory.Row(parent, spacing, height);
        private Button Button(Transform parent, string text, bool primary, UnityEngine.Events.UnityAction action) => uiFactory.Button(parent, text, primary, action);
        private RectTransform Panel(string name, Transform parent, Color color) => uiFactory.Panel(name, parent, color);
        private Image Image(string name, Transform parent, Color color) => uiFactory.Image(name, parent, color);
        private RectTransform Rect(string name, Transform parent) => uiFactory.Rect(name, parent);
        private Text Label(string name, Transform parent, string value, int size, FontStyle style, Color color, TextAnchor alignment) => uiFactory.Label(name, parent, value, size, style, color, alignment);
        private Text LayoutLabel(Transform parent, string value, int size, FontStyle style, Color color, float height) => uiFactory.LayoutLabel(parent, value, size, style, color, height);
        private void Divider(Transform parent) => uiFactory.Divider(parent);
        private static void Stretch(RectTransform rect, float left = 0, float bottom = 0, float right = 0, float top = 0) => TouchUiFactory.Stretch(rect, left, bottom, right, top);
        private static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY, float left, float bottom, float right, float top) => TouchUiFactory.Anchor(rect, minX, minY, maxX, maxY, left, bottom, right, top);
    }
}
