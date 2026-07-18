using System;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    [Serializable]
    internal sealed class PackageInstallerWindowReloadSnapshot
    {
        public int schemaVersion;
        public string projectPath;
        public long capturedAtUtcTicks;
        public string searchText;
        public bool showInstalled = true;
        public bool showNotInstalled = true;
        public string selectedPackageId;
        public int navigationTargetKind;
        public string focusedPackageId;
        public string focusedGroupId;
        public int viewMode;
        public float sidebarScrollX;
        public float sidebarScrollY;
        public float detailsScrollX;
        public float detailsScrollY;
        public float operationScrollX;
        public float operationScrollY;
        public bool hasGraphCamera;
        public float graphPanX;
        public float graphPanY;
        public float graphZoom = 1f;
    }

    internal readonly struct PackageInstallerWindowReloadResolution
    {
        public PackageInstallerWindowReloadResolution(
            string selectedPackageId,
            bool selectedPackageIsIntegration,
            PackageGraphNavigationState navigation)
        {
            SelectedPackageId = selectedPackageId ?? string.Empty;
            SelectedPackageIsIntegration = selectedPackageIsIntegration;
            Navigation = navigation;
        }

        public string SelectedPackageId { get; }

        public bool SelectedPackageIsIntegration { get; }

        public PackageGraphNavigationState Navigation { get; }
    }

    internal static class PackageInstallerWindowReloadState
    {
        private const int CurrentSchemaVersion = 1;
        private const string StateKey =
            "Deucarian.PackageInstaller.WindowReloadState";

        private static bool _isAssemblyReloading;

        static PackageInstallerWindowReloadState()
        {
            EditorApplication.quitting -= HandleEditorQuitting;
            EditorApplication.quitting += HandleEditorQuitting;
        }

        internal static bool IsAssemblyReloading => _isAssemblyReloading;

        internal static void SaveForAssemblyReload(PackageInstallerWindowReloadSnapshot snapshot)
        {
            _isAssemblyReloading = true;

            if (snapshot == null)
            {
                ClearSavedState();
                return;
            }

            snapshot.schemaVersion = CurrentSchemaVersion;
            snapshot.projectPath = GetCurrentProjectPath();
            snapshot.capturedAtUtcTicks = DateTime.UtcNow.Ticks;
            Normalize(snapshot);
            SessionState.SetString(StateKey, JsonUtility.ToJson(snapshot));
        }

        internal static bool TryConsume(out PackageInstallerWindowReloadSnapshot snapshot)
        {
            snapshot = null;
            string json = SessionState.GetString(StateKey, string.Empty);
            SessionState.EraseString(StateKey);

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                snapshot = JsonUtility.FromJson<PackageInstallerWindowReloadSnapshot>(json);
            }
            catch (ArgumentException)
            {
                snapshot = null;
                return false;
            }

            if (!IsValid(snapshot))
            {
                snapshot = null;
                return false;
            }

            Normalize(snapshot);
            return true;
        }

        internal static void ClearForNormalDisable()
        {
            if (!_isAssemblyReloading)
            {
                ClearSavedState();
            }
        }

        internal static PackageInstallerWindowReloadResolution Resolve(
            PackageInstallerWindowReloadSnapshot snapshot,
            PackageGraphModel graph)
        {
            if (snapshot == null || graph == null)
            {
                return new PackageInstallerWindowReloadResolution(
                    string.Empty,
                    false,
                    PackageGraphNavigationState.Overview());
            }

            string selectedPackageId = string.Empty;
            bool selectedPackageIsIntegration = false;
            if (!string.IsNullOrWhiteSpace(snapshot.selectedPackageId) &&
                graph.TryGetNode(snapshot.selectedPackageId, out PackageGraphNode selectedNode))
            {
                selectedPackageId = selectedNode.PackageId;
                selectedPackageIsIntegration =
                    selectedNode.PackageDefinition != null && selectedNode.PackageDefinition.IsIntegration;
            }

            PackageGraphNavigationTargetKind targetKind =
                Enum.IsDefined(typeof(PackageGraphNavigationTargetKind), snapshot.navigationTargetKind)
                    ? (PackageGraphNavigationTargetKind)snapshot.navigationTargetKind
                    : PackageGraphNavigationTargetKind.Overview;

            if (targetKind == PackageGraphNavigationTargetKind.Package &&
                !string.IsNullOrWhiteSpace(snapshot.focusedPackageId) &&
                graph.TryGetNode(snapshot.focusedPackageId, out PackageGraphNode focusedNode))
            {
                if (string.IsNullOrWhiteSpace(selectedPackageId))
                {
                    selectedPackageId = focusedNode.PackageId;
                    selectedPackageIsIntegration =
                        focusedNode.PackageDefinition != null && focusedNode.PackageDefinition.IsIntegration;
                }

                return new PackageInstallerWindowReloadResolution(
                    selectedPackageId,
                    selectedPackageIsIntegration,
                    PackageGraphNavigationState.Package(focusedNode.PackageId, focusedNode.GroupId));
            }

            if (!string.IsNullOrWhiteSpace(snapshot.focusedGroupId) &&
                graph.TryGetGroup(snapshot.focusedGroupId, out PackageGraphGroup focusedGroup))
            {
                return new PackageInstallerWindowReloadResolution(
                    selectedPackageId,
                    selectedPackageIsIntegration,
                    PackageGraphNavigationState.Group(focusedGroup.Id));
            }

            return new PackageInstallerWindowReloadResolution(
                selectedPackageId,
                selectedPackageIsIntegration,
                PackageGraphNavigationState.Overview());
        }

        internal static void SetRawStateForTests(string json)
        {
            SessionState.SetString(StateKey, json ?? string.Empty);
        }

        internal static bool HasSavedStateForTests =>
            !string.IsNullOrWhiteSpace(SessionState.GetString(StateKey, string.Empty));

        internal static void SimulateNewDomainForTests()
        {
            _isAssemblyReloading = false;
        }

        internal static void ResetForTests()
        {
            _isAssemblyReloading = false;
            ClearSavedState();
        }

        private static bool IsValid(PackageInstallerWindowReloadSnapshot snapshot)
        {
            return snapshot != null &&
                   snapshot.schemaVersion == CurrentSchemaVersion &&
                   snapshot.capturedAtUtcTicks > 0 &&
                   string.Equals(
                       snapshot.projectPath ?? string.Empty,
                       GetCurrentProjectPath(),
                       StringComparison.OrdinalIgnoreCase) &&
                   (!snapshot.hasGraphCamera ||
                    (IsFinite(snapshot.graphPanX) &&
                     IsFinite(snapshot.graphPanY) &&
                     IsFinite(snapshot.graphZoom) &&
                     snapshot.graphZoom > 0f));
        }

        private static void Normalize(PackageInstallerWindowReloadSnapshot snapshot)
        {
            snapshot.projectPath = snapshot.projectPath ?? string.Empty;
            snapshot.searchText = snapshot.searchText ?? string.Empty;
            snapshot.selectedPackageId = snapshot.selectedPackageId ?? string.Empty;
            snapshot.focusedPackageId = snapshot.focusedPackageId ?? string.Empty;
            snapshot.focusedGroupId = snapshot.focusedGroupId ?? string.Empty;
            snapshot.sidebarScrollX = NormalizeScroll(snapshot.sidebarScrollX);
            snapshot.sidebarScrollY = NormalizeScroll(snapshot.sidebarScrollY);
            snapshot.detailsScrollX = NormalizeScroll(snapshot.detailsScrollX);
            snapshot.detailsScrollY = NormalizeScroll(snapshot.detailsScrollY);
            snapshot.operationScrollX = NormalizeScroll(snapshot.operationScrollX);
            snapshot.operationScrollY = NormalizeScroll(snapshot.operationScrollY);
        }

        private static float NormalizeScroll(float value)
        {
            return IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string GetCurrentProjectPath()
        {
            return (Application.dataPath ?? string.Empty)
                .Replace('\\', '/')
                .TrimEnd('/');
        }

        private static void HandleEditorQuitting()
        {
            _isAssemblyReloading = false;
            ClearSavedState();
        }

        private static void ClearSavedState()
        {
            SessionState.EraseString(StateKey);
        }
    }
}
