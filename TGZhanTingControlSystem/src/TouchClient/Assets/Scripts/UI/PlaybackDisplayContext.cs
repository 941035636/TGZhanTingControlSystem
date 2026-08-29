using System;
using System.Linq;
using UnityEngine;

namespace TG.Control.Touch.UI
{
    /// <summary>
    /// Session-bound presentation metadata for route and module labels that are not part of
    /// PlaybackSessionStatus. It never controls playback and is ignored unless the session id matches.
    /// </summary>
    public sealed class PlaybackDisplayContext
    {
        private const string SessionKey = "TG.Playback.Context.Session";
        private const string RouteKey = "TG.Playback.Context.Route";
        private const string ModulesKey = "TG.Playback.Context.Modules";

        private string pendingRouteName;
        private string[] pendingModuleIds = Array.Empty<string>();
        private string sessionId;

        public string SessionId => sessionId;
        public string RouteName { get; private set; }
        public string[] ModuleIds { get; private set; } = Array.Empty<string>();

        public void Prepare(string routeName, string[] moduleIds)
        {
            pendingRouteName = string.IsNullOrWhiteSpace(routeName) ? null : routeName.Trim();
            pendingModuleIds = moduleIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray()
                               ?? Array.Empty<string>();
        }

        public void Bind(string activeSessionId)
        {
            if (string.IsNullOrWhiteSpace(activeSessionId)) return;
            if (string.Equals(sessionId, activeSessionId, StringComparison.Ordinal)) return;
            sessionId = activeSessionId;

            if (!string.IsNullOrWhiteSpace(pendingRouteName) || pendingModuleIds.Length > 0)
            {
                RouteName = pendingRouteName;
                ModuleIds = pendingModuleIds.ToArray();
                PlayerPrefs.SetString(SessionKey, activeSessionId);
                PlayerPrefs.SetString(RouteKey, RouteName ?? string.Empty);
                PlayerPrefs.SetString(ModulesKey, string.Join("\n", ModuleIds));
                PlayerPrefs.Save();
                ClearPending();
                return;
            }

            if (string.Equals(PlayerPrefs.GetString(SessionKey, string.Empty), activeSessionId,
                    StringComparison.Ordinal))
            {
                RouteName = PlayerPrefs.GetString(RouteKey, string.Empty);
                var stored = PlayerPrefs.GetString(ModulesKey, string.Empty);
                ModuleIds = string.IsNullOrWhiteSpace(stored)
                    ? Array.Empty<string>()
                    : stored.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                RouteName = null;
                ModuleIds = Array.Empty<string>();
            }
        }

        public void ClearPending()
        {
            pendingRouteName = null;
            pendingModuleIds = Array.Empty<string>();
        }

        public void Clear()
        {
            sessionId = null;
            RouteName = null;
            ModuleIds = Array.Empty<string>();
            ClearPending();
            PlayerPrefs.DeleteKey(SessionKey);
            PlayerPrefs.DeleteKey(RouteKey);
            PlayerPrefs.DeleteKey(ModulesKey);
            PlayerPrefs.Save();
        }
    }
}
