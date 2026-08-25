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
        public string ActiveSessionId { get; private set; }
        public bool HasActiveSession => !string.IsNullOrWhiteSpace(ActiveSessionId);
        public event Action<PublishedContent> ContentLoaded;
        public event Action<string> Error;
        public event Action<string> Status;
        private int sessionMonitorGeneration;

        private void Start() => RefreshContent();

        public void RefreshContent() => apiClient.GetContent(content =>
        {
            CurrentContent = content;
            ContentLoaded?.Invoke(content);
        }, message => Error?.Invoke(message));

        public void StartAll() => StartModules(CurrentContent?.modules.Where(module => module.enabled).OrderBy(module => module.order).Select(module => module.id).ToArray());

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
                    Status?.Invoke("本次讲解已完成。");
                    yield break;
                }
                if (lookup != null && lookup.session != null)
                {
                    var session = lookup.session;
                    var phase = session.paused ? "已暂停" : session.playPublished ? "正在讲解" : "正在准备素材";
                    var currentStatus = $"{phase}：{session.moduleName} / {session.nodeName}（{session.currentNodeNumber}/{session.totalNodes}）";
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
    }
}
