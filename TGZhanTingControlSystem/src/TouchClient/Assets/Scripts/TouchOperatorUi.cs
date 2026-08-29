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
        private readonly PlaybackDisplayContext playbackDisplay = new PlaybackDisplayContext();
        private PublishedContent content;
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
        private PlaybackPage playbackPageView;
        private TouchImageLoader imageLoader;
        private GameObject homePage;
        private GameObject editorPage;
        private GameObject playbackPageRoot;

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
            if (playbackPageView != null)
            {
                playbackPageView.PauseRequested -= PausePlayback;
                playbackPageView.ResumeRequested -= ResumePlayback;
                playbackPageView.RetryRequested -= RetryPlayback;
                playbackPageView.SkipRequested -= SkipPlayback;
                playbackPageView.StopRequested -= ConfirmStop;
                playbackPageView.StopCancelled -= CancelStopConfirmation;
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
            playbackPageView = new PlaybackPage(uiFactory, theme, body);
            playbackPageView.PauseRequested += PausePlayback;
            playbackPageView.ResumeRequested += ResumePlayback;
            playbackPageView.RetryRequested += RetryPlayback;
            playbackPageView.SkipRequested += SkipPlayback;
            playbackPageView.StopRequested += ConfirmStop;
            playbackPageView.StopCancelled += CancelStopConfirmation;
            playbackPageRoot = playbackPageView.Root.gameObject;
            Stretch(homePage.GetComponent<RectTransform>());
            Stretch(editorPage.GetComponent<RectTransform>());
            Stretch(playbackPageRoot.GetComponent<RectTransform>());
            ShowPage(PageState.Home);
        }

        private void OnConnectionChanged(bool value)
        {
            connected = value;
            status = value ? "系统已连接，可以开始讲解。" : "服务器连接中断，正在自动重连…";
            if (value)
            {
                receptionHomePage?.ClearError();
                playbackPageView?.ClearError();
            }
            else if (facade.HasActiveSession)
            {
                playbackPageView?.ShowError("服务器连接中断，系统正在自动重连。讲解状态恢复后可继续操作。");
            }
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
            var sameSession = value != null && string.Equals(playbackDisplay.SessionId, value.sessionId,
                StringComparison.Ordinal);
            if (!sameSession) confirmStop = false;
            if (value != null) playbackDisplay.Bind(value.sessionId);
            else playbackDisplay.Clear();
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
            playbackPageView?.ClearError();
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
            playbackPageView?.ShowError(value);
            if (!facade.HasActiveSession) playbackDisplay.ClearPending();
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
            var moduleIds = route.moduleIds ?? Array.Empty<string>();
            playbackDisplay.Prepare(route.name, moduleIds);
            facade.StartModules(moduleIds);
            Refresh();
        }

        private void StartAllFromHome()
        {
            status = "正在准备全部主题讲解…";
            navigateToPlaybackWhenSessionArrives = true;
            playbackDisplay.Prepare("全部主题讲解", content?.modules?
                .Where(module => module.enabled && module.nodes != null && module.nodes.Length > 0)
                .OrderBy(module => module.order).Select(module => module.id).ToArray() ?? Array.Empty<string>());
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
            var moduleIds = routeDraft.SnapshotModuleIds();
            var routeName = routeDraft.IsTemporary
                ? (string.IsNullOrWhiteSpace(routeDraft.Name) ? "临时组合" : routeDraft.Name)
                : routeDraft.Name;
            playbackDisplay.Prepare(routeName, moduleIds);
            facade.StartModules(moduleIds);
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

        private void CancelStopConfirmation()
        {
            confirmStop = false;
            status = "已取消终止，本次讲解继续保持当前状态。";
            Refresh();
        }

        private void PausePlayback() => facade.Pause();
        private void ResumePlayback() => facade.Resume();
        private void RetryPlayback() => facade.Retry();
        private void SkipPlayback() => facade.Skip();

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
            if (playbackPageRoot != null) playbackPageRoot.SetActive(state == PageState.Playback);
            if (state != PageState.Playback) confirmStop = false;
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
            if (presenter != null) playbackPageView?.Render(presenter.State, playbackDisplay.RouteName,
                playbackDisplay.ModuleIds, confirmStop);
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
                playbackPageView?.RefreshTheme();
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

        private static void Stretch(RectTransform rect, float left = 0, float bottom = 0, float right = 0, float top = 0) => TouchUiFactory.Stretch(rect, left, bottom, right, top);
    }
}
