using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {


        private static string GetDefaultActionLabel(PackageInstallerActionKind actionKind)
        {
            switch (actionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    return "Check Updates";
                case PackageInstallerActionKind.UpdateAll:
                    return "Update All";
                case PackageInstallerActionKind.InstallAll:
                    return "Install All";
                default:
                    return string.Empty;
            }
        }

        private static string GetCancelActionLabel(PackageInstallerActionKind actionKind)
        {
            switch (actionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    return "Cancel Check";
                case PackageInstallerActionKind.UpdateAll:
                    return "Cancel Update";
                case PackageInstallerActionKind.InstallAll:
                    return "Cancel Install";
                default:
                    return "Cancel";
            }
        }

        private void RefreshPackages()
        {
            InvalidateGraphModelCache("manual refresh");
            PackageRegistryProvider.RefreshRemote();
            _packageDetectionService.Refresh();
            RefreshGraphView("manual refresh");
        }

        private void HandleActionButton(PackageInstallerActionKind actionKind)
        {
            if (_activeActionKind == actionKind)
            {
                CancelAction(actionKind);
                return;
            }

            if (IsAnyOperationBusy())
            {
                return;
            }

            switch (actionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    CheckForUpdates();
                    break;
                case PackageInstallerActionKind.UpdateAll:
                    UpdateAllPackages();
                    break;
                case PackageInstallerActionKind.InstallAll:
                    InstallAllPackages();
                    break;
            }
        }

        private void CancelAction(PackageInstallerActionKind actionKind)
        {
            if (_activeActionKind != actionKind ||
                _cancelingActionKind != PackageInstallerActionKind.None)
            {
                return;
            }

            _cancelingActionKind = actionKind;

            switch (actionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    _checkUpdatesAfterDetectionRefresh = false;
                    _deferredUpdateCheckActionKind = PackageInstallerActionKind.None;
                    _packageUpdateCheckService.CancelCurrentCheck();
                    if (PackageRegistryProvider.CancelRemoteRefresh())
                    {
                        PackageInstallerActivityService.Record(
                            "Registry",
                            PackageInstallerActivitySeverity.Warning,
                            "Registry refresh canceled.",
                            retryKind: PackageInstallerRetryKind.CheckUpdates);
                    }
                    break;
                case PackageInstallerActionKind.UpdateAll:
                case PackageInstallerActionKind.InstallAll:
                    if (!TryCancelAwaitingPreflight())
                    {
                        _packageInstallService.CancelCurrentOperation();
                    }
                    break;
            }

            UpdateViewVisibility();
            ClearActiveActionIfIdle();
            Repaint();
        }

        private bool TryCancelAwaitingPreflight()
        {
            return CancelAwaitingPreflight(
                _packageDependencyInstaller,
                () => DismissPendingConfirmation(refreshUi: false));
        }

        internal static bool CancelAwaitingPreflightForTests(
            PackageDependencyInstaller installer,
            Action dismissConfirmation)
        {
            return CancelAwaitingPreflight(installer, dismissConfirmation);
        }

        private static bool CancelAwaitingPreflight(
            PackageDependencyInstaller installer,
            Action dismissConfirmation)
        {
            if (installer == null || !installer.IsAwaitingPreflight)
            {
                return false;
            }

            dismissConfirmation?.Invoke();
            installer.CancelPendingPreflight();
            return true;
        }

        private void CheckForUpdates()
        {
            RequestUpdateCheck(PackageInstallerActionKind.CheckUpdates, "manual update check");
        }

        private void RequestUpdateCheck(PackageInstallerActionKind actionKind, string reason)
        {
            InvalidateGraphModelCache(reason);
            _activeActionKind = actionKind;
            _cancelingActionKind = PackageInstallerActionKind.None;
            QueueDeferredUpdateCheck(actionKind);
            PackageRegistryProvider.RefreshRemote();
            _packageUpdateCheckService.PrepareForUpdateCheck();

            if (!_packageDetectionService.IsRefreshing)
            {
                _packageDetectionService.Refresh();
            }

            TryRunDeferredUpdateCheck();
            RefreshGraphView(reason);
            UpdateViewVisibility();
            Repaint();
        }

        private void UpdateAllPackages()
        {
            PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();
            TrackPendingUpdateStatusInvalidations(packagesWithUpdates);

            _activeActionKind = PackageInstallerActionKind.UpdateAll;
            _cancelingActionKind = PackageInstallerActionKind.None;
            _packageDependencyInstaller.UpdateAll(
                packagesWithUpdates,
                GetSelectedChannel);

            if (!ShouldRetainPendingUpdateStatusInvalidations(
                    _packageInstallService != null && _packageInstallService.IsBusy,
                    _packageDependencyInstaller != null &&
                    _packageDependencyInstaller.IsAwaitingPreflight))
            {
                _pendingUpdateStatusInvalidationPackageIds.Clear();
            }

            ClearActiveActionIfIdle();
            UpdateViewVisibility();
        }

        private void InstallAllPackages()
        {
            _activeActionKind = PackageInstallerActionKind.InstallAll;
            _cancelingActionKind = PackageInstallerActionKind.None;
            _packageDependencyInstaller.InstallAll(GetSelectedChannel);
            ClearActiveActionIfIdle();
            UpdateViewVisibility();
        }

        private void DrawSidebar()
        {
            Rect rect = BeginSurface(
                _sidebarStyle,
                _sidebarBackgroundColor,
                _panelBorderColor,
                GUILayout.Width(SidebarWidth),
                GUILayout.ExpandHeight(true));

            IReadOnlyList<PackageCategoryListView> categoryViews = GetPackageCategoryViews();
            DrawSidebarFilterControls(categoryViews);
            GUILayout.Space(8f);
            DrawHorizontalSeparator();
            GUILayout.Space(8f);

            _sidebarScrollPosition = EditorGUILayout.BeginScrollView(_sidebarScrollPosition);
            DrawRegistrySidebarSections(categoryViews);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private IReadOnlyList<PackageCategoryListView> GetPackageCategoryViews()
        {
            return PackageListFilter.CreateCategoryViews(
                PackageRegistryProvider.All,
                PackageRegistryProvider.Categories,
                _visibilityFilterState,
                package => _packageDetectionService.IsInstalled(package.PackageId),
                IsCategoryExpanded);
        }

        private void DrawSidebarFilterControls(IReadOnlyList<PackageCategoryListView> categoryViews)
        {
            int totalCount = categoryViews.Sum(view => view.PackageCount);
            int matchedCount = categoryViews.Sum(view => view.FilteredPackageCount);
            int visibleCount = categoryViews.Sum(view => view.VisiblePackages.Count);

            EditorGUILayout.LabelField("Package List", _sectionTitleStyle);

            EditorGUI.BeginChangeCheck();
            string nextSearchText = EditorGUILayout.TextField(
                new GUIContent("Search", "Find packages by package name, package ID, domain, or kind."),
                _visibilityFilterState.SearchText);

            if (EditorGUI.EndChangeCheck() && _visibilityFilterState.SetSearchText(nextSearchText))
            {
                RefreshGraphView("search changed");
                Repaint();
            }

            EditorGUILayout.LabelField("Visibility", _mutedMiniLabelStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool nextShowInstalled = EditorGUILayout.ToggleLeft(
                    new GUIContent("Installed", "Show packages that Unity reports as installed."),
                    _visibilityFilterState.ShowInstalled,
                    GUILayout.Width(92f));
                bool nextShowNotInstalled = EditorGUILayout.ToggleLeft(
                    new GUIContent("Not Installed", "Show packages that Unity does not report as installed."),
                    _visibilityFilterState.ShowNotInstalled,
                    GUILayout.Width(118f));

                if (_visibilityFilterState.Set(
                        _visibilityFilterState.SearchText,
                        nextShowInstalled,
                        nextShowNotInstalled))
                {
                    RefreshGraphView("visibility filter changed");
                    Repaint();
                }
            }

            EditorGUILayout.LabelField(
                GetSidebarFilterSummary(totalCount, matchedCount, visibleCount),
                _mutedMiniLabelStyle);
        }

        private string GetSidebarFilterSummary(int totalCount, int matchedCount, int visibleCount)
        {
            if (totalCount == 0)
            {
                return "No packages in the active registry.";
            }

            if (matchedCount == totalCount)
            {
                return visibleCount + " visible / " + totalCount + " packages";
            }

            return visibleCount + " visible / " + matchedCount + " matched / " + totalCount + " packages";
        }

        private void DrawRegistrySidebarSections(IReadOnlyList<PackageCategoryListView> categoryViews)
        {
            bool drewPackageHeader = false;
            bool drewAnyCategory = false;

            foreach (PackageCategoryListView categoryView in categoryViews)
            {
                if (!categoryView.HasFilteredPackages)
                {
                    continue;
                }

                DrawSidebarSection(
                    drewPackageHeader ? null : "Packages",
                    categoryView);
                drewPackageHeader = true;

                drewAnyCategory = true;
                GUILayout.Space(8f);
            }

            if (!drewAnyCategory)
            {
                string message = categoryViews.Any(view => view.PackageCount > 0)
                    ? "No packages match the active filters."
                    : "No package entries are available in the active registry.";
                DrawInlineHelp(message, VisualStatusKind.Info);
            }
        }

        private void DrawUpdateSummary(bool compact)
        {
            PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();
            int updateCount = packagesWithUpdates.Length;
            bool checking = _packageUpdateCheckService.IsChecking;
            VisualStatusKind updateKind = checking
                ? VisualStatusKind.Busy
                : updateCount > 0
                    ? VisualStatusKind.UpdateAvailable
                    : VisualStatusKind.Installed;

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawStatusBadge(
                    checking ? "Checking updates" : "Updates Available: " + updateCount,
                    updateKind,
                    GUILayout.Width(compact ? 132f : 146f));

                EditorGUILayout.LabelField(
                    new GUIContent(
                        "Last Checked: " + GetLastUpdateCheckLabel(),
                        GetLastUpdateCheckTooltip()),
                    _mutedMiniLabelStyle,
                    GUILayout.MinWidth(compact ? 120f : 170f));
            }

            if (!string.IsNullOrWhiteSpace(_packageUpdateCheckService.LastFailureMessage))
            {
                EditorGUILayout.LabelField(
                    new GUIContent(
                        _packageUpdateCheckService.LastFailureMessage,
                        _packageUpdateCheckService.LastFailureMessage),
                    _mutedMiniLabelStyle);
            }
        }

        private string GetLastUpdateCheckLabel()
        {
            DateTime? lastCheckedUtc = _packageUpdateCheckService.LastCheckedUtc;

            if (!lastCheckedUtc.HasValue)
            {
                return "Never";
            }

            TimeSpan elapsed = DateTime.UtcNow - lastCheckedUtc.Value.ToUniversalTime();

            if (elapsed.TotalSeconds < 60d)
            {
                return "Just now";
            }

            if (elapsed.TotalMinutes < 60d)
            {
                int minutes = Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalMinutes));
                return minutes == 1 ? "1 minute ago" : minutes + " minutes ago";
            }

            if (elapsed.TotalHours < 24d)
            {
                int hours = Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalHours));
                return hours == 1 ? "1 hour ago" : hours + " hours ago";
            }

            int days = Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalDays));
            return days == 1 ? "1 day ago" : days + " days ago";
        }
    }
}
