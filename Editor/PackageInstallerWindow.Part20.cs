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


        private void ValidatePendingReloadState(PackageGraphModel graph)
        {
            if (!_reloadStatePendingValidation)
            {
                return;
            }

            PackageInstallerWindowReloadResolution resolution =
                PackageInstallerWindowReloadState.Resolve(_pendingReloadSnapshot, graph);
            _selectedPackageId = resolution.SelectedPackageId;
            _selectionKind = resolution.SelectedPackageIsIntegration
                ? SelectionKind.Integration
                : SelectionKind.Package;
            _graphNavigationState = resolution.Navigation;
            _pendingReloadSnapshot = null;
            _reloadStatePendingValidation = false;
        }

        private static PackageGraphNavigationState RestoreNavigation(
            PackageInstallerWindowReloadSnapshot snapshot)
        {
            PackageGraphNavigationTargetKind targetKind =
                Enum.IsDefined(typeof(PackageGraphNavigationTargetKind), snapshot.navigationTargetKind)
                    ? (PackageGraphNavigationTargetKind)snapshot.navigationTargetKind
                    : PackageGraphNavigationTargetKind.Overview;
            switch (targetKind)
            {
                case PackageGraphNavigationTargetKind.Package:
                    return PackageGraphNavigationState.Package(
                        snapshot.focusedPackageId,
                        snapshot.focusedGroupId);
                case PackageGraphNavigationTargetKind.Group:
                    return PackageGraphNavigationState.Group(snapshot.focusedGroupId);
                default:
                    return PackageGraphNavigationState.Overview();
            }
        }

        private bool IsSelected(PackageDefinition packageDefinition, SelectionKind selectionKind)
        {
            return packageDefinition != null &&
                   _selectionKind == selectionKind &&
                   string.Equals(_selectedPackageId, packageDefinition.PackageId, StringComparison.OrdinalIgnoreCase);
        }

        private void SelectDefinition(
            PackageDefinition packageDefinition,
            SelectionKind selectionKind,
            bool refreshGraph = true)
        {
            if (packageDefinition == null || IsSelected(packageDefinition, selectionKind))
            {
                return;
            }

            _selectionKind = selectionKind;
            _selectedPackageId = packageDefinition.PackageId;
            _detailsScrollPosition = Vector2.zero;

            if (refreshGraph)
            {
                RefreshGraphView("selection");
            }

            Repaint();
        }

        private PackageDefinition GetSelectedDefinition()
        {
            if (string.IsNullOrWhiteSpace(_selectedPackageId))
            {
                return null;
            }

            return PackageRegistryProvider.All.FirstOrDefault(packageDefinition =>
                string.Equals(packageDefinition.PackageId, _selectedPackageId, StringComparison.OrdinalIgnoreCase));
        }

        private PackageGraphGroup GetFocusedGraphGroup()
        {
            return _lastPackageGraph != null &&
                   !string.IsNullOrWhiteSpace(_graphNavigationState.FocusedGroupId) &&
                   _lastPackageGraph.TryGetGroup(_graphNavigationState.FocusedGroupId, out PackageGraphGroup group)
                ? group
                : null;
        }

        private string GetGraphPackageGroupId(string packageId)
        {
            return _lastPackageGraph != null &&
                   !string.IsNullOrWhiteSpace(packageId) &&
                   _lastPackageGraph.TryGetNode(packageId, out PackageGraphNode node)
                ? node.GroupId
                : string.Empty;
        }

        private string GetPackageHierarchyPath(PackageDefinition packageDefinition)
        {
            return PackageGraphHierarchyDisplay.GetPackageHierarchyPath(_lastPackageGraph, packageDefinition);
        }

        private static string GetPackageKindDisplayName(PackageDefinition packageDefinition)
        {
            return PackageGraphHierarchyDisplay.GetPackageKind(packageDefinition);
        }

        private string GetGraphParentGroupId(string groupId)
        {
            return _lastPackageGraph != null &&
                   !string.IsNullOrWhiteSpace(groupId) &&
                   _lastPackageGraph.TryGetGroup(groupId, out PackageGraphGroup group)
                ? group.ParentGroupId
                : string.Empty;
        }

        private bool IsGraphNavigationRowHoverContext(PackageGraphNavigationRow row)
        {
            if (_graphView == null || row.IsOverview || string.IsNullOrWhiteSpace(row.Id))
            {
                return false;
            }

            if (row.IsPackage)
            {
                return string.Equals(
                    row.Id,
                    _graphView.ActiveHoverPackageId,
                    StringComparison.OrdinalIgnoreCase);
            }

            string activeHoverGroupId = _graphView.ActiveHoverGroupId;

            if (string.IsNullOrWhiteSpace(activeHoverGroupId))
            {
                return false;
            }

            if (row.Depth > 0)
            {
                return string.Equals(
                    row.Id,
                    activeHoverGroupId,
                    StringComparison.OrdinalIgnoreCase);
            }

            string hoverTopLevelGroupId = ResolveTopLevelGroupId(_lastPackageGraph, activeHoverGroupId);

            return !string.IsNullOrWhiteSpace(hoverTopLevelGroupId) &&
                   string.Equals(
                       hoverTopLevelGroupId,
                       row.Id,
                       StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<PackageGraphNode> GetGraphGroupDescendantPackages(string groupId)
        {
            if (_lastPackageGraph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return Enumerable.Empty<PackageGraphNode>();
            }

            return _lastPackageGraph.GetDescendantPackages(groupId)
                .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        private PackageDefinition[] GetPackagesWithUpdates()
        {
            return _packageUpdateCheckService
                .GetPackagesWithUpdates(PackageRegistryProvider.All, GetSelectedChannel)
                .ToArray();
        }

        private string GetLastOperationSummary()
        {
            if (_packageSampleImportService.IsBusy &&
                !string.IsNullOrWhiteSpace(_packageSampleImportService.LastStatusMessage))
            {
                return _packageSampleImportService.LastStatusMessage;
            }

            if (_packageInstallService.IsBusy &&
                !string.IsNullOrWhiteSpace(_packageInstallService.LastStatusMessage))
            {
                return _packageInstallService.LastStatusMessage;
            }

            if (_packageUpdateCheckService.IsChecking)
            {
                return "Checking installed packages for updates...";
            }

            if (_packageDetectionService.IsRefreshing)
            {
                return "Refreshing installed packages...";
            }

            PackageInstallerActivityEntry latestActivity = PackageInstallerActivityService.Latest;
            if (latestActivity != null && !string.IsNullOrWhiteSpace(latestActivity.Summary))
            {
                return latestActivity.Summary;
            }

            if (_packageInstallService.HasProgress &&
                !string.IsNullOrWhiteSpace(_packageInstallService.LastStatusMessage))
            {
                return _packageInstallService.LastStatusMessage;
            }

            if (!string.IsNullOrWhiteSpace(_packageSampleImportService.LastErrorMessage))
            {
                return _packageSampleImportService.LastErrorMessage;
            }

            if (!string.IsNullOrWhiteSpace(_packageSampleImportService.LastStatusMessage))
            {
                return _packageSampleImportService.LastStatusMessage;
            }

            if (!string.IsNullOrWhiteSpace(_packageUpdateCheckService.LastFailureMessage))
            {
                return _packageUpdateCheckService.LastFailureMessage;
            }

            if (!string.IsNullOrWhiteSpace(_packageUpdateCheckService.LastStatusMessage))
            {
                return _packageUpdateCheckService.LastStatusMessage;
            }

            return string.Empty;
        }

        private bool IsAnyOperationBusy()
        {
            return (_packageInstallService != null && _packageInstallService.IsBusy) ||
                   (_confirmationState != null && _confirmationState.IsPending) ||
                   (_packageDependencyInstaller != null &&
                    _packageDependencyInstaller.IsAwaitingPreflight) ||
                   (_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                   (_packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking) ||
                   (_packageSampleImportService != null && _packageSampleImportService.IsBusy) ||
                   _plannerFailureRetryAfterRefresh ||
                   IsActiveActionStillBusy();
        }

        private static string GetUpdateStatusText(PackageUpdateStatus status)
        {
            if (status == null)
            {
                return "Unknown";
            }

            if (status.Kind == PackageUpdateStatusKind.UpToDate && !string.IsNullOrWhiteSpace(status.ShortLatestRevision))
            {
                return status.Label + " (" + status.ShortLatestRevision + ")";
            }

            if (status.Kind == PackageUpdateStatusKind.UpdateAvailable ||
                status.Kind == PackageUpdateStatusKind.SwitchAvailable)
            {
                return status.Label + " (" + status.ShortInstalledRevision + " -> " + status.ShortLatestRevision + ")";
            }

            if (status.IsSourceMigrationAvailable && !string.IsNullOrWhiteSpace(status.ShortLatestRevision))
            {
                return status.Label + " (Git " + status.ShortLatestRevision + ")";
            }

            if (status.IsReloadPending && !string.IsNullOrWhiteSpace(status.Message))
            {
                return status.Label + ": " + status.Message;
            }

            if (status.Kind == PackageUpdateStatusKind.Failed && !string.IsNullOrWhiteSpace(status.Message))
            {
                return status.Label + ": " + status.Message;
            }

            if (status.Kind == PackageUpdateStatusKind.CannotDetermine && !string.IsNullOrWhiteSpace(status.Message))
            {
                return status.Label + ": " + status.Message;
            }

            return status.Label;
        }

        private static string GetPackageVersionText(string installedVersion, PackageUpdateStatus status)
        {
            string resolvedInstalledVersion = status != null && !string.IsNullOrWhiteSpace(status.InstalledVersion)
                ? status.InstalledVersion
                : installedVersion;
            string currentVersion = string.IsNullOrWhiteSpace(resolvedInstalledVersion)
                ? "-"
                : resolvedInstalledVersion.Trim();

            if (status != null &&
                status.IsReloadPending &&
                !string.IsNullOrWhiteSpace(status.RunningVersion))
            {
                return status.RunningVersion + " running; " + currentVersion + " resolved";
            }

            if (status != null && status.HasPackageVersionTransition)
            {
                return currentVersion + " -> " + status.LatestVersion;
            }

            return currentVersion;
        }

        private static string GetUpdateActionLabel(PackageUpdateStatus status, PackageChannel channel)
        {
            if (status != null && status.IsReloadPending)
            {
                return "Retry Script Reload";
            }

            if (status != null && status.IsSourceMigrationAvailable)
            {
                return PackageInstallerRuntimeIdentity.IsSelf(status.PackageId)
                    ? "Open Bootstrap"
                    : "Migrate to Git";
            }

            if (status != null && status.Kind == PackageUpdateStatusKind.SwitchAvailable)
            {
                return "Switch to " + GetChannelLabel(channel);
            }

            return "Update to " + GetChannelLabel(channel);
        }

        private static string GetSampleImportStatusText(PackageSampleImportStatus status)
        {
            if (status == null)
            {
                return string.Empty;
            }

            switch (status.State)
            {
                case PackageSampleImportState.Importing:
                    return "Importing sample...";
                case PackageSampleImportState.Imported:
                    return string.IsNullOrWhiteSpace(status.Message) ? "Imported sample." : status.Message;
                case PackageSampleImportState.AlreadyImported:
                    return "Sample already imported.";
                case PackageSampleImportState.Canceled:
                    return string.IsNullOrWhiteSpace(status.Message) ? "Sample import canceled." : status.Message;
                case PackageSampleImportState.Failed:
                    return string.IsNullOrWhiteSpace(status.Message) ? "Import failed." : status.Message;
                default:
                    return "Not imported.";
            }
        }

        private static VisualStatusKind GetSampleImportStatusKind(PackageSampleImportStatus status)
        {
            if (status == null)
            {
                return VisualStatusKind.NotInstalled;
            }

            switch (status.State)
            {
                case PackageSampleImportState.Importing:
                    return VisualStatusKind.Busy;
                case PackageSampleImportState.Imported:
                case PackageSampleImportState.AlreadyImported:
                    return VisualStatusKind.Installed;
                case PackageSampleImportState.Canceled:
                    return VisualStatusKind.NotInstalled;
                case PackageSampleImportState.Failed:
                    return VisualStatusKind.Failed;
                default:
                    return VisualStatusKind.NotInstalled;
            }
        }

        private static bool IsImportedSampleStatus(PackageSampleImportStatus status)
        {
            return status != null &&
                   (status.State == PackageSampleImportState.Imported ||
                    status.State == PackageSampleImportState.AlreadyImported);
        }
    }
}
