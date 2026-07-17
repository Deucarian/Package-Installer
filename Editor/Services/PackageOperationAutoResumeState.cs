using System;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageOperationAutoResumeState
    {
        private const int CurrentSchemaVersion = 1;
        private const string ReloadMarkerKey =
            "Deucarian.PackageInstaller.BulkOperationReloadMarker";

        private static string _activeOperationId = string.Empty;
        private static string _activeRegistryFingerprint = string.Empty;
        private static bool _isAssemblyReloading;

        static PackageOperationAutoResumeState()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;
            EditorApplication.quitting -= HandleEditorQuitting;
            EditorApplication.quitting += HandleEditorQuitting;
        }

        internal static bool IsAssemblyReloading => _isAssemblyReloading;

        internal static void TrackActiveOperation(
            string operationId,
            string registryFingerprint,
            bool isBulk)
        {
            if (!isBulk ||
                string.IsNullOrWhiteSpace(operationId) ||
                string.IsNullOrWhiteSpace(registryFingerprint))
            {
                _activeOperationId = string.Empty;
                _activeRegistryFingerprint = string.Empty;
                return;
            }

            _activeOperationId = operationId.Trim();
            _activeRegistryFingerprint = registryFingerprint.Trim();
        }

        internal static void DetachOperation(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId) ||
                !string.Equals(
                    _activeOperationId,
                    operationId.Trim(),
                    StringComparison.Ordinal))
            {
                return;
            }

            _activeOperationId = string.Empty;
            _activeRegistryFingerprint = string.Empty;

            if (!_isAssemblyReloading)
            {
                ClearMatchingReloadMarker(operationId);
            }
        }

        internal static void DisqualifyOperation(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId) ||
                string.Equals(
                    _activeOperationId,
                    operationId.Trim(),
                    StringComparison.Ordinal))
            {
                _activeOperationId = string.Empty;
                _activeRegistryFingerprint = string.Empty;
            }

            ClearMatchingReloadMarker(operationId);
        }

        internal static bool HasMatchingReloadMarker(
            string operationId,
            string registryFingerprint)
        {
            return TryLoadReloadMarker(out PersistedReloadMarker marker) &&
                   !string.IsNullOrWhiteSpace(operationId) &&
                   !string.IsNullOrWhiteSpace(registryFingerprint) &&
                   string.Equals(
                       marker.operationId,
                       operationId.Trim(),
                       StringComparison.Ordinal) &&
                   string.Equals(
                       marker.registryFingerprint,
                       registryFingerprint.Trim(),
                       StringComparison.Ordinal);
        }

        internal static void AcknowledgeReloadMarker(string operationId)
        {
            ClearMatchingReloadMarker(operationId);
        }

        internal static void ClearReloadMarker()
        {
            SessionState.EraseString(ReloadMarkerKey);
        }

        internal static void Clear()
        {
            _activeOperationId = string.Empty;
            _activeRegistryFingerprint = string.Empty;
            ClearReloadMarker();
        }

        internal static void SimulateBeforeAssemblyReloadForTests()
        {
            HandleBeforeAssemblyReload();
        }

        internal static void SimulateEditorRestartForTests()
        {
            HandleEditorQuitting();
            _isAssemblyReloading = false;
        }

        internal static void SimulateNewDomainForTests()
        {
            _activeOperationId = string.Empty;
            _activeRegistryFingerprint = string.Empty;
            _isAssemblyReloading = false;
        }

        internal static void ResetForTests()
        {
            Clear();
            _isAssemblyReloading = false;
        }

        private static void HandleBeforeAssemblyReload()
        {
            _isAssemblyReloading = true;

            if (string.IsNullOrWhiteSpace(_activeOperationId) ||
                string.IsNullOrWhiteSpace(_activeRegistryFingerprint))
            {
                return;
            }

            PersistedReloadMarker marker = new PersistedReloadMarker
            {
                schemaVersion = CurrentSchemaVersion,
                operationId = _activeOperationId,
                registryFingerprint = _activeRegistryFingerprint,
                createdAtUtcTicks = DateTime.UtcNow.Ticks
            };
            SessionState.SetString(ReloadMarkerKey, JsonUtility.ToJson(marker));
        }

        private static void HandleEditorQuitting()
        {
            Clear();
        }

        private static void ClearMatchingReloadMarker(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                SessionState.EraseString(ReloadMarkerKey);
                return;
            }

            if (!TryLoadReloadMarker(out PersistedReloadMarker marker) ||
                string.Equals(
                    marker.operationId,
                    operationId.Trim(),
                    StringComparison.Ordinal))
            {
                SessionState.EraseString(ReloadMarkerKey);
            }
        }

        private static bool TryLoadReloadMarker(out PersistedReloadMarker marker)
        {
            marker = null;
            string json = SessionState.GetString(ReloadMarkerKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                marker = JsonUtility.FromJson<PersistedReloadMarker>(json);
            }
            catch (ArgumentException)
            {
                SessionState.EraseString(ReloadMarkerKey);
                return false;
            }

            if (marker == null ||
                marker.schemaVersion != CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(marker.operationId) ||
                string.IsNullOrWhiteSpace(marker.registryFingerprint) ||
                marker.createdAtUtcTicks <= 0)
            {
                SessionState.EraseString(ReloadMarkerKey);
                marker = null;
                return false;
            }

            return true;
        }

        [Serializable]
        private sealed class PersistedReloadMarker
        {
            public int schemaVersion;
            public string operationId;
            public string registryFingerprint;
            public long createdAtUtcTicks;
        }
    }
}
