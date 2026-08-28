using System;
using System.Collections.Generic;
using System.Linq;
using TG.Control.UnityContracts;

namespace TG.Control.Touch.UI
{
    /// <summary>
    /// Pure UI draft for route composition. It owns selection order and confirmation state,
    /// but never persists routes or starts playback.
    /// </summary>
    public sealed class RouteDraftState
    {
        private readonly List<string> moduleIds = new List<string>();

        public string RouteId { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public IReadOnlyList<string> ModuleIds => moduleIds;
        public bool IsTemporary => string.IsNullOrWhiteSpace(RouteId);
        public bool IsDirty { get; private set; }
        public bool DeleteConfirmationPending { get; private set; }
        public bool LeaveConfirmationPending { get; private set; }

        public void Load(NarrationRoute route, IEnumerable<string> availableModuleIds = null)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            RouteId = route.id;
            Name = route.name ?? string.Empty;
            moduleIds.Clear();
            var available = availableModuleIds == null
                ? null
                : new HashSet<string>(availableModuleIds, StringComparer.OrdinalIgnoreCase);
            foreach (var id in route.moduleIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(id) || available != null && !available.Contains(id)) continue;
                if (!moduleIds.Contains(id, StringComparer.OrdinalIgnoreCase)) moduleIds.Add(id);
            }
            IsDirty = false;
            ResetConfirmations();
        }

        public void BeginTemporary()
        {
            RouteId = null;
            Name = string.Empty;
            moduleIds.Clear();
            IsDirty = false;
            ResetConfirmations();
        }

        public void DetachForSaveAs(string suggestedName)
        {
            RouteId = null;
            Name = suggestedName ?? string.Empty;
            IsDirty = true;
            ResetConfirmations();
        }

        public void SetName(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(Name, value, StringComparison.Ordinal)) return;
            Name = value;
            MarkChanged();
        }

        public bool Add(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId) || moduleIds.Contains(moduleId, StringComparer.OrdinalIgnoreCase))
                return false;
            moduleIds.Add(moduleId);
            MarkChanged();
            return true;
        }

        public bool Remove(string moduleId)
        {
            var index = IndexOf(moduleId);
            if (index < 0) return false;
            moduleIds.RemoveAt(index);
            MarkChanged();
            return true;
        }

        public bool Move(string moduleId, int direction)
        {
            var index = IndexOf(moduleId);
            var destination = index + direction;
            if (index < 0 || destination < 0 || destination >= moduleIds.Count) return false;
            var value = moduleIds[index];
            moduleIds.RemoveAt(index);
            moduleIds.Insert(destination, value);
            MarkChanged();
            return true;
        }

        public bool Clear()
        {
            if (moduleIds.Count == 0) return false;
            moduleIds.Clear();
            MarkChanged();
            return true;
        }

        public void RetainAvailable(IEnumerable<string> availableModuleIds)
        {
            if (availableModuleIds == null) return;
            var available = new HashSet<string>(availableModuleIds, StringComparer.OrdinalIgnoreCase);
            moduleIds.RemoveAll(id => !available.Contains(id));
        }

        public bool ArmLeaveConfirmation()
        {
            if (LeaveConfirmationPending) return true;
            LeaveConfirmationPending = true;
            DeleteConfirmationPending = false;
            return false;
        }

        public bool ArmDeleteConfirmation()
        {
            if (DeleteConfirmationPending) return true;
            DeleteConfirmationPending = true;
            LeaveConfirmationPending = false;
            return false;
        }

        public void CancelLeaveConfirmation() => LeaveConfirmationPending = false;
        public void CancelDeleteConfirmation() => DeleteConfirmationPending = false;
        public string[] SnapshotModuleIds() => moduleIds.ToArray();

        private int IndexOf(string moduleId) => moduleIds.FindIndex(id =>
            string.Equals(id, moduleId, StringComparison.OrdinalIgnoreCase));

        private void MarkChanged()
        {
            IsDirty = true;
            ResetConfirmations();
        }

        private void ResetConfirmations()
        {
            DeleteConfirmationPending = false;
            LeaveConfirmationPending = false;
        }
    }
}
