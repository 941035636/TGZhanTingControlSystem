using System;
using TG.Control.UnityContracts;

namespace TG.Control.Touch.UI
{
    /// <summary>Read-only UI snapshot maintained by <see cref="TouchUiPresenter"/>.</summary>
    public sealed class TouchUiState
    {
        public PublishedContent Content { get; internal set; }
        public PlaybackSessionStatus Session { get; internal set; }
        public SystemReadiness Readiness { get; internal set; }
        public NarrationRoute[] Routes { get; internal set; } = Array.Empty<NarrationRoute>();
        public bool Connected { get; internal set; }
        public string Status { get; internal set; } = "正在连接展厅服务…";
    }
}
