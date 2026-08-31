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


        private void HandleRegistryChanged()
        {
            InvalidateGraphModelCache("registry changed");
            if (_packageDetectionService != null &&
                _packageDetectionService.HasSuccessfulRefresh &&
                !PackageRegistryProvider.IsRemoteRefreshing)
            {
                _packageUpdateCheckService?.ReconcileCachedStatuses(
                    PackageRegistryProvider.All,
                    GetSelectedChannel);
            }

            EnsureValidSelection();
            RefreshGraphView("registry changed");

            TryPromptForSavedOperationRecovery();
            TryRestartTerminalOperationAfterRefresh();
            TryRetryPlannerFailureAfterRefresh();

            TryRunDeferredUpdateCheck();
            ClearActiveActionIfIdle();
            Repaint();
        }

        private void HandlePackageUpdateGraphStateChanged()
        {
            InvalidateGraphModelCache("update status changed");
            RefreshGraphView("update status changed");
            ClearActiveActionIfIdle();
        }

        private void HandlePackageInstallCompleted(PackageDefinition packageDefinition, bool success, string message)
        {
            if (!TryConsumePendingUpdateStatusInvalidation(
                    _pendingUpdateStatusInvalidationPackageIds,
                    packageDefinition,
                    success))
            {
                return;
            }

            _packageUpdateCheckService?.Invalidate(packageDefinition.PackageId);
            InvalidateGraphModelCache("package update completed");
            RefreshGraphView("package update completed");
            UpdateViewVisibility();
            Repaint();
        }

        private void HandlePackageDetectionGraphStateChanged()
        {
            if (_packageDetectionService != null && _packageDetectionService.IsRefreshing)
            {
                RefreshGraphView("installed package refresh started");
            }
        }

        private void HandlePackageOperationCompleted()
        {
            PackageInstallerActionKind completedActionKind = _activeActionKind;
            bool shouldCheckUpdates =
                completedActionKind == PackageInstallerActionKind.UpdateAll ||
                _checkUpdatesAfterDetectionRefresh ||
                (_packageUpdateCheckService != null && _packageUpdateCheckService.HasStatuses);

            if (completedActionKind == PackageInstallerActionKind.UpdateAll ||
                completedActionKind == PackageInstallerActionKind.InstallAll)
            {
                _activeActionKind = PackageInstallerActionKind.None;
                _cancelingActionKind = PackageInstallerActionKind.None;
            }

            _pendingUpdateStatusInvalidationPackageIds.Clear();

            if (shouldCheckUpdates)
            {
                QueueDeferredUpdateCheck(PackageInstallerActionKind.CheckUpdates);
                PackageRegistryProvider.RefreshRemote();
            }

            _packageDetectionService.Refresh();
            UpdateViewVisibility();
        }

        private void HandlePackageDetectionRefreshCompleted()
        {
            InvalidateGraphModelCache("installed package manifest changed");
            _packageSampleDiscoveryService?.ClearCache();
            bool hadUpdateStatuses =
                _packageUpdateCheckService != null && _packageUpdateCheckService.HasStatuses;
            bool manifestChanged =
                _packageUpdateCheckService != null &&
                _packageUpdateCheckService.InvalidateIfManifestStateChanged();

            if (manifestChanged && hadUpdateStatuses)
            {
                QueueDeferredUpdateCheck(PackageInstallerActionKind.CheckUpdates);
            }

            if (_packageDetectionService != null &&
                _packageDetectionService.HasSuccessfulRefresh &&
                !PackageRegistryProvider.IsRemoteRefreshing)
            {
                _packageUpdateCheckService?.ReconcileCachedStatuses(
                    PackageRegistryProvider.All,
                    GetSelectedChannel);
            }

            RefreshGraphView("installed package refresh completed");

            TryPromptForSavedOperationRecovery();
            TryRestartTerminalOperationAfterRefresh();
            TryRetryPlannerFailureAfterRefresh();

            TryRunDeferredUpdateCheck();
            ClearActiveActionIfIdle();
        }

        private void TryRestartTerminalOperationAfterRefresh()
        {
            PackageOperationTerminalSnapshot snapshot = _terminalOperationRetryAfterRefresh;
            if (snapshot == null ||
                (_confirmationState != null && _confirmationState.IsPending) ||
                PackageRegistryProvider.IsRemoteRefreshing ||
                (_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                (_packageDetectionService != null && !_packageDetectionService.HasSuccessfulRefresh))
            {
                return;
            }

            _terminalOperationRetryAfterRefresh = null;
            PackageOperationTerminalSnapshot currentSnapshot =
                _packageInstallService?.TerminalOperationSnapshot;
            if (_packageInstallService == null ||
                _packageInstallService.IsBusy ||
                currentSnapshot == null ||
                !currentSnapshot.CanRestart ||
                !string.Equals(
                    currentSnapshot.OperationId,
                    snapshot.OperationId,
                    StringComparison.Ordinal))
            {
                return;
            }

            PackageDependencyInstallPlan freshPlan = CreateFreshTerminalRetryPlan(
                snapshot,
                _packageDependencyInstaller,
                packageId => PackageRegistryProvider.TryGetPackage(
                    packageId,
                    out PackageDefinition definition)
                    ? definition
                    : null);
            if (freshPlan == null || !freshPlan.IsValid || freshPlan.Steps.Count == 0)
            {
                ShowInformationDialog(
                    "Package operation cannot be restarted",
                    freshPlan != null && !string.IsNullOrWhiteSpace(freshPlan.ErrorMessage)
                        ? freshPlan.ErrorMessage
                        : "The affected root packages are no longer available in the current registry.",
                    DeucarianEditorIconIds.Error);
                return;
            }

            string delta = FormatTerminalRetryPlanDelta(snapshot, freshPlan);
            if (!freshPlan.RequiresPreflight && string.IsNullOrWhiteSpace(delta))
            {
                StartTerminalRetryPlan(snapshot, freshPlan);
                return;
            }

            var restartAction = new DeucarianEditorDialogAction(
                "restart",
                "Restart",
                DeucarianEditorIconIds.Refresh,
                DeucarianEditorDialogActionStyle.Primary);
            var cancelAction = new DeucarianEditorDialogAction(
                "cancel",
                "Cancel",
                DeucarianEditorIconIds.Stop);
            var options = new DeucarianEditorDialogOptions(
                "Restart package operation",
                "Installed and registry state were refreshed. Review the fresh plan before restarting.",
                DeucarianEditorIconIds.Refresh,
                new[] { restartAction, cancelAction })
            {
                Details = BuildTerminalRetryReview(snapshot, freshPlan, delta),
                DefaultActionId = restartAction.Id,
                CancelActionId = cancelAction.Id
            };
            TryShowManagedDialog(options, result =>
            {
                if (!result.WasCanceled &&
                    string.Equals(result.ActionId, restartAction.Id, StringComparison.Ordinal) &&
                    this != null)
                {
                    StartTerminalRetryPlan(snapshot, freshPlan);
                }
            });
        }

        private void StartTerminalRetryPlan(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstallPlan freshPlan)
        {
            if (!CanStartTerminalRetryPlan(snapshot, freshPlan))
            {
                RecordStaleConfirmation(
                    "Retry package operation",
                    "Package or registry state changed before the retry could start.");
                return;
            }

            TrackPendingUpdateStatusInvalidations(freshPlan.Packages);
            _packageInstallService.InstallPlan(
                freshPlan,
                "Retry " + (string.IsNullOrWhiteSpace(snapshot.OperationName)
                    ? "Package Operation"
                    : snapshot.OperationName));
            UpdateViewVisibility();
        }

        private bool CanStartTerminalRetryPlan(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstallPlan freshPlan)
        {
            PackageOperationTerminalSnapshot currentSnapshot =
                _packageInstallService?.TerminalOperationSnapshot;
            return snapshot != null &&
                   freshPlan != null &&
                   _packageInstallService != null &&
                   !_packageInstallService.IsBusy &&
                   currentSnapshot != null &&
                   currentSnapshot.CanRestart &&
                   string.Equals(
                       currentSnapshot.OperationId,
                       snapshot.OperationId,
                       StringComparison.Ordinal) &&
                   _packageDependencyInstaller != null &&
                   _packageDependencyInstaller.IsPlanStillCurrent(freshPlan);
        }

        private void TryRetryPlannerFailureAfterRefresh()
        {
            if (!IsPlannerRetryRefreshReadyForTests(
                    _plannerFailureRetryAfterRefresh,
                    PackageRegistryProvider.IsRemoteRefreshing,
                    _packageDetectionService != null && _packageDetectionService.IsRefreshing))
            {
                return;
            }

            _plannerFailureRetryAfterRefresh = false;
            if (_packageDetectionService != null &&
                !_packageDetectionService.HasSuccessfulRefresh)
            {
                const string message =
                    "The package plan was not retried because installed-package refresh failed. Retry the plan to refresh again.";
                PackageInstallerLog.Install.Warning(message);
                PackageInstallerActivityService.Record(
                    "Planner",
                    PackageInstallerActivitySeverity.Warning,
                    message,
                    retryKind: PackageInstallerRetryKind.ReplanOperation);
                return;
            }

            _packageDependencyInstaller?.RetryLastPlannerFailure();
        }

        internal static bool IsPlannerRetryRefreshReadyForTests(
            bool retryPending,
            bool registryRefreshing,
            bool detectionRefreshing)
        {
            return retryPending && !registryRefreshing && !detectionRefreshing;
        }

        internal static PackageDependencyInstallPlan CreateFreshTerminalRetryPlanForTests(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstaller installer,
            IEnumerable<PackageDefinition> registeredPackages)
        {
            Dictionary<string, PackageDefinition> definitions =
                (registeredPackages ?? Array.Empty<PackageDefinition>())
                .Where(definition => definition != null &&
                                     !string.IsNullOrWhiteSpace(definition.PackageId))
                .GroupBy(definition => definition.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            return CreateFreshTerminalRetryPlan(
                snapshot,
                installer,
                packageId => definitions.TryGetValue(packageId, out PackageDefinition definition)
                    ? definition
                    : null);
        }

        private static PackageDependencyInstallPlan CreateFreshTerminalRetryPlan(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstaller installer,
            Func<string, PackageDefinition> packageResolver)
        {
            if (snapshot == null || !snapshot.CanRestart || installer == null || packageResolver == null)
            {
                return null;
            }

            PackageOperationRootRequest[] rootRequests = snapshot.RestartRoots
                .Where(root => root != null && !string.IsNullOrWhiteSpace(root.PackageId))
                .GroupBy(root => root.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            PackageDefinition[] roots = rootRequests
                .Select(root => packageResolver(root.PackageId))
                .Where(definition => definition != null)
                .ToArray();
            if (roots.Length == 0 || roots.Length != rootRequests.Length)
            {
                return null;
            }

            Dictionary<string, PackageChannel> channels = rootRequests.ToDictionary(
                root => root.PackageId,
                root => root.Channel,
                StringComparer.OrdinalIgnoreCase);
            return installer.CreateInstallPlan(
                roots,
                package => channels.TryGetValue(package.PackageId, out PackageChannel channel)
                    ? channel
                    : PackageChannel.Stable,
                includeInstalledRequestedPackages: true);
        }

        internal static string FormatTerminalRetryPlanDeltaForTests(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstallPlan freshPlan)
        {
            return FormatTerminalRetryPlanDelta(snapshot, freshPlan);
        }

        private static string FormatTerminalRetryPlanDelta(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstallPlan freshPlan)
        {
            if (snapshot == null || freshPlan == null || !freshPlan.IsValid)
            {
                return string.Empty;
            }

            HashSet<string> retryRootIds = new HashSet<string>(
                snapshot.RestartRoots.Select(root => root.PackageId),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageOperationStepSnapshot> previous = snapshot.Steps
                .Where(step => step != null &&
                               ((!step.IsDependency && retryRootIds.Contains(step.PackageId)) ||
                                 step.RootPackageIds.Any(retryRootIds.Contains)))
                .GroupBy(step => step.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageDependencyInstallStep> current = freshPlan.Steps
                .Where(step => step != null && step.PackageDefinition != null)
                .GroupBy(step => step.PackageDefinition.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            List<string> lines = new List<string>();

            foreach (string packageId in previous.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                bool hadPrevious = previous.TryGetValue(packageId, out PackageOperationStepSnapshot oldStep);
                bool hasCurrent = current.TryGetValue(packageId, out PackageDependencyInstallStep newStep);
                if (!hadPrevious)
                {
                    lines.Add("Added: " + newStep.PackageDefinition.DisplayName + " -> " + newStep.TargetUrl);
                }
                else if (!hasCurrent)
                {
                    lines.Add("Now skipped: " + oldStep.DisplayName + " is already correct or no longer required.");
                }
                else if (oldStep.Channel != newStep.Channel ||
                         !string.Equals(oldStep.TargetUrl, newStep.TargetUrl, StringComparison.Ordinal))
                {
                    lines.Add(
                        "Changed: " + newStep.PackageDefinition.DisplayName +
                        "\n  was [" + GetChannelLabel(oldStep.Channel) + "] " + oldStep.TargetUrl +
                        "\n  now [" + GetChannelLabel(newStep.Channel) + "] " + newStep.TargetUrl);
                }
            }

            return string.Join("\n", lines.ToArray());
        }
    }
}
