using System;
using TG.Control.UnityContracts;

namespace TG.Control.Touch.UI
{
    /// <summary>
    /// Owns subscriptions and the current UI snapshot. Business commands remain on TouchControlFacade.
    /// </summary>
    public sealed class TouchUiPresenter : IDisposable
    {
        private readonly TouchApiClient apiClient;
        private readonly TouchControlFacade facade;
        private bool attached;
        private bool uiExperiencePending;

        public TouchUiState State { get; }
        public event Action<bool> ConnectionChanged;
        public event Action<PublishedContent> ContentLoaded;
        public event Action<string> StatusChanged;
        public event Action<PlaybackSessionStatus> SessionChanged;
        public event Action<NarrationRoute[]> RoutesLoaded;
        public event Action<NarrationRoute> RouteSaved;
        public event Action<SystemReadiness> ReadinessChanged;
        public event Action<UiExperienceConfig> UiExperienceChanged;
        public event Action<string> UiExperienceLoadFailed;
        public event Action<string> Error;

        public TouchUiPresenter(TouchApiClient apiClient, TouchControlFacade facade)
        {
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            this.facade = facade ?? throw new ArgumentNullException(nameof(facade));
            State = new TouchUiState
            {
                Connected = apiClient.IsConnected,
                Content = facade.CurrentContent,
                Readiness = facade.CurrentReadiness,
                Routes = facade.CurrentRoutes ?? Array.Empty<NarrationRoute>(),
                HasActiveSession = facade.HasActiveSession
            };
        }

        public void Attach()
        {
            if (attached) return;
            attached = true;
            apiClient.ConnectionChanged += HandleConnectionChanged;
            facade.ContentLoaded += HandleContentLoaded;
            facade.Status += HandleStatus;
            facade.SessionChanged += HandleSessionChanged;
            facade.RoutesLoaded += HandleRoutesLoaded;
            facade.RouteSaved += HandleRouteSaved;
            facade.ReadinessChanged += HandleReadinessChanged;
            facade.Error += HandleError;
        }

        public void Dispose()
        {
            if (!attached) return;
            attached = false;
            apiClient.ConnectionChanged -= HandleConnectionChanged;
            facade.ContentLoaded -= HandleContentLoaded;
            facade.Status -= HandleStatus;
            facade.SessionChanged -= HandleSessionChanged;
            facade.RoutesLoaded -= HandleRoutesLoaded;
            facade.RouteSaved -= HandleRouteSaved;
            facade.ReadinessChanged -= HandleReadinessChanged;
            facade.Error -= HandleError;
        }

        private void HandleConnectionChanged(bool value) { State.Connected = value; ConnectionChanged?.Invoke(value); }
        private void HandleContentLoaded(PublishedContent value) { State.Content = value; ContentLoaded?.Invoke(value); }
        private void HandleStatus(string value)
        {
            State.Status = value;
            State.HasActiveSession = facade.HasActiveSession;
            StatusChanged?.Invoke(value);
        }
        private void HandleSessionChanged(PlaybackSessionStatus value)
        {
            State.Session = value;
            State.HasActiveSession = value != null || facade.HasActiveSession;
            SessionChanged?.Invoke(value);
        }
        private void HandleRoutesLoaded(NarrationRoute[] value) { State.Routes = value ?? Array.Empty<NarrationRoute>(); RoutesLoaded?.Invoke(State.Routes); }
        private void HandleRouteSaved(NarrationRoute value) { RouteSaved?.Invoke(value); }
        private void HandleReadinessChanged(SystemReadiness value) { State.Readiness = value; ReadinessChanged?.Invoke(value); }
        private void HandleError(string value)
        {
            State.Status = "操作失败：" + value;
            State.HasActiveSession = facade.HasActiveSession;
            Error?.Invoke(value);
        }

        public void RefreshUiExperience()
        {
            if (uiExperiencePending) return;
            uiExperiencePending = true;
            apiClient.GetUiExperience(value =>
            {
                uiExperiencePending = false;
                State.UiExperience = value;
                UiExperienceChanged?.Invoke(value);
            }, message =>
            {
                uiExperiencePending = false;
                UiExperienceLoadFailed?.Invoke(message);
            });
        }

        public string NormalizeAssetUrl(string url) => apiClient.NormalizeUrl(url);
    }
}
