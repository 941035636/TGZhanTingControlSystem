using System;
using System.Collections;
using System.Linq;
using TG.Control.UnityContracts;
using UnityEngine;

namespace TG.Control.Touch
{
    public sealed class TouchControlFacade : MonoBehaviour
    {
        [SerializeField] private TouchApiClient apiClient;
        public PublishedContent CurrentContent { get; private set; }
        public NarrationRoute[] CurrentRoutes { get; private set; } = Array.Empty<NarrationRoute>();
        public SystemReadiness CurrentReadiness { get; private set; }
        public string ActiveSessionId { get; private set; }
        public bool HasActiveSession => !string.IsNullOrWhiteSpace(ActiveSessionId);
        public event Action<PublishedContent> ContentLoaded;
        public event Action<string> Error;
        public event Action<string> Status;
        public event Action<PlaybackSessionStatus> SessionChanged;
        public event Action<NarrationRoute[]> RoutesLoaded;
        public event Action<NarrationRoute> RouteSaved;
        public event Action<SystemReadiness> ReadinessChanged;
        private int sessionMonitorGeneration;

        private void Start()
        {
            RefreshContent();
            RefreshRoutes();
            StartCoroutine(ReadinessLoop());
            StartCoroutine(RestoreActiveSession());
        }

        public void RefreshContent() => apiClient.GetContent(content =>
        {
            CurrentContent = content;
            apiClient.SetContentVersion(content.version);
            ContentLoaded?.Invoke(content);
        }, message => Error?.Invoke(message));

        public void RefreshRoutes() => apiClient.GetRoutes(collection =>
        {
            CurrentRoutes = collection?.routes ?? Array.Empty<NarrationRoute>();
            RoutesLoaded?.Invoke(CurrentRoutes);
        }, message => Error?.Invoke("读取常用路线失败：" + message));

        public void SaveRoute(string routeId, string routeName, string[] moduleIds)
        {
            if (string.IsNullOrWhiteSpace(routeName)) { Error?.Invoke("请输入路线名称。"); return; }
            if (moduleIds == null || moduleIds.Length == 0) { Error?.Invoke("请先选择讲解主题。"); return; }
            apiClient.SaveRoute(new SaveNarrationRouteRequest { id = routeId, name = routeName.Trim(), moduleIds = moduleIds },
                saved => { Status?.Invoke("讲解路线已保存。"); RouteSaved?.Invoke(saved); RefreshRoutes(); },
                message => Error?.Invoke("保存路线失败：" + message));
        }

        public void DeleteRoute(string routeId)
        {
            if (string.IsNullOrWhiteSpace(routeId)) return;
            apiClient.DeleteRoute(routeId,
                () => { Status?.Invoke("讲解路线已删除。"); RefreshRoutes(); },
                message => Error?.Invoke("删除路线失败：" + message));
        }

        public void StartAll() => StartModules(CurrentContent?.modules.Where(module => module.enabled && module.nodes != null && module.nodes.Length > 0).OrderBy(module => module.order).Select(module => module.id).ToArray());

        public void StartModules(string[] moduleIds)
        {
            if (HasActiveSession)
            {
                Error?.Invoke("当前讲解尚未结束，请先终止后再启动新的讲解。");
                return;
            }
            if (moduleIds == null || moduleIds.Length == 0)
            {
                Error?.Invoke("请至少选择一个讲解模块。");
                return;
            }
            if (CurrentReadiness == null || !CurrentReadiness.canStart)
            {
                Error?.Invoke(CurrentReadiness?.message ?? "正在检查LED播放端和内容版本，请稍候。");
                return;
            }
            apiClient.StartNarration(moduleIds, Environment.UserName,
                response =>
                {
                    ActiveSessionId = response.sessionId;
                    Status?.Invoke($"讲解任务已启动，共 {response.nodeCount} 个节点。");
                    StartCoroutine(MonitorSession(response.sessionId, ++sessionMonitorGeneration));
                },
                message => Error?.Invoke(message));
        }

        public void Pause() => Control(PlaybackAction.Pause);
        public void Resume() => Control(PlaybackAction.Resume);
        public void Skip() => Control(PlaybackAction.Skip);
        public void Retry() => Control(PlaybackAction.Retry);
        public void Stop() => Control(PlaybackAction.Stop);

        private void Control(PlaybackAction action)
        {
            if (!HasActiveSession)
            {
                Error?.Invoke("当前没有正在执行的讲解任务。");
                return;
            }

            apiClient.ControlNarration(ActiveSessionId, action, response =>
            {
                Status?.Invoke(response.message);
                if (action == PlaybackAction.Stop)
                {
                    ActiveSessionId = null;
                    sessionMonitorGeneration++;
                    SessionChanged?.Invoke(null);
                }
            }, message => Error?.Invoke(message));
        }

        private IEnumerator MonitorSession(string sessionId, int generation)
        {
            string lastStatus = null;
            while (generation == sessionMonitorGeneration && string.Equals(ActiveSessionId, sessionId, StringComparison.Ordinal))
            {
                var completed = false;
                PlaybackSessionLookup lookup = null;
                string failure = null;
                apiClient.GetNarrationSession(sessionId,
                    value => { lookup = value; completed = true; },
                    message => { failure = message; completed = true; });
                while (!completed && generation == sessionMonitorGeneration) yield return null;
                if (generation != sessionMonitorGeneration) yield break;

                if (lookup != null && !lookup.active)
                {
                    ActiveSessionId = null;
                    SessionChanged?.Invoke(null);
                    Status?.Invoke("本次讲解已完成。");
                    yield break;
                }
                if (lookup != null && lookup.session != null)
                {
                    var session = lookup.session;
                    SessionChanged?.Invoke(session);
                    var phase = session.paused ? "已暂停" : session.playPublished ? "正在讲解" : "正在准备素材";
                    var preparation = !session.playPublished && session.preparationProgress > 0
                        ? $" {session.preparationProgress * 100:0}%" : string.Empty;
                    var currentStatus = $"{phase}{preparation}：{session.moduleName} / {session.nodeName}（{session.currentNodeNumber}/{session.totalNodes}）";
                    if (!string.Equals(lastStatus, currentStatus, StringComparison.Ordinal))
                    {
                        lastStatus = currentStatus;
                        Status?.Invoke(currentStatus);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(failure))
                {
                    var currentStatus = "讲解状态暂时不可用，正在重试…";
                    if (!string.Equals(lastStatus, currentStatus, StringComparison.Ordinal))
                    {
                        lastStatus = currentStatus;
                        Status?.Invoke(currentStatus);
                    }
                }
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private IEnumerator RestoreActiveSession()
        {
            var completed = false;
            PlaybackSessionLookup lookup = null;
            apiClient.GetActiveNarrationSession(value => { lookup = value; completed = true; }, _ => completed = true);
            while (!completed) yield return null;
            if (lookup?.active != true || lookup.session == null || HasActiveSession) yield break;
            ActiveSessionId = lookup.session.sessionId;
            SessionChanged?.Invoke(lookup.session);
            Status?.Invoke("已恢复正在执行的讲解任务。 ");
            StartCoroutine(MonitorSession(ActiveSessionId, ++sessionMonitorGeneration));
        }

        private IEnumerator ReadinessLoop()
        {
            while (enabled)
            {
                var completed = false;
                apiClient.GetReadiness(value =>
                {
                    CurrentReadiness = value;
                    ReadinessChanged?.Invoke(value);
                    completed = true;
                }, _ => completed = true);
                while (!completed && enabled) yield return null;
                yield return new WaitForSecondsRealtime(2);
            }
        }
    }
}
