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


        private void PromptForSavedOperationRecovery()
        {
            if (_packageInstallService == null || _packageInstallService.IsBusy)
            {
                return;
            }

            if (_confirmationState != null && _confirmationState.IsPending)
            {
                _promptSavedOperationAfterDetectionRefresh = true;
                return;
            }

            if (!_packageInstallService.TryGetSavedOperation(
                    out PackageOperationRecoveryRecord recovery,
                    out string recoveryError) ||
                recovery == null)
            {
                PackageOperationAutoResumeState.ClearReloadMarker();
                if (!string.IsNullOrWhiteSpace(recoveryError) &&
                    recoveryError.IndexOf("No saved", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    ShowInformationDialog(
                        "Package operation recovery unavailable",
                        recoveryError,
                        DeucarianEditorIconIds.Error);
                }
                return;
            }

            PackageDependencyInstallPlan freshPlan = CreateFreshRecoveryPlan(recovery);
            bool canReuseExactTargets = CanReuseSavedExactTargets(recovery, freshPlan);
            bool hasMatchingReloadMarker =
                PackageOperationAutoResumeState.HasMatchingReloadMarker(
                    recovery.OperationId,
                    recovery.RegistryFingerprint);

            if (GetRecoveryDisposition(
                    recovery,
                    freshPlan,
                    hasMatchingReloadMarker) ==
                PackageOperationRecoveryDisposition.AutoResume)
            {
                bool resumed = _packageInstallService.ResumeSavedOperation(
                    freshPlan.RegistryFingerprint);
                bool reconciledWithoutRemainingWork =
                    !resumed && !_packageInstallService.HasSavedOperation;

                if (resumed || reconciledWithoutRemainingWork)
                {
                    PackageOperationAutoResumeState.AcknowledgeReloadMarker(
                        recovery.OperationId);
                    string operationName = string.IsNullOrWhiteSpace(recovery.OperationName)
                        ? "Bulk package operation"
                        : recovery.OperationName;
                    string message = resumed
                        ? "Resuming " + operationName + " after Unity script reload."
                        : operationName +
                          " completed after Unity script reload; all targets are already correct.";
                    PackageInstallerLog.Install.Info(message);
                    PackageInstallerActivityService.Record(
                        "Packages",
                        PackageInstallerActivitySeverity.Info,
                        message);
                    UpdateOperationFooter();
                    return;
                }
            }

            PackageOperationAutoResumeState.ClearReloadMarker();
            int remainingSteps = recovery.Steps.Count(step =>
                step.State == PackageInstallProgressItemState.Pending ||
                step.State == PackageInstallProgressItemState.Active ||
                step.State == PackageInstallProgressItemState.Failed ||
                step.State == PackageInstallProgressItemState.Blocked ||
                step.State == PackageInstallProgressItemState.Canceled);
            string interruptedSummary =
                (string.IsNullOrWhiteSpace(recovery.OperationName)
                    ? "A package operation"
                    : recovery.OperationName) +
                " was interrupted with " + remainingSteps + " step(s) remaining.";
            string summary = interruptedSummary + "\n\n" +
                "Resume keeps its exact saved URLs and skips completed steps. " +
                "Restart repeats the saved plan. Discard removes only the recovery record.";

            if (!canReuseExactTargets && freshPlan != null && freshPlan.IsValid)
            {
                string planDelta = FormatRecoveryPlanDelta(recovery, freshPlan);
                var restartFreshAction = new DeucarianEditorDialogAction(
                    "restart-fresh",
                    "Restart Fresh Plan",
                    DeucarianEditorIconIds.Refresh,
                    DeucarianEditorDialogActionStyle.Primary);
                var keepAction = new DeucarianEditorDialogAction(
                    "keep",
                    "Keep for Later",
                    DeucarianEditorIconIds.History);
                var discardAction = new DeucarianEditorDialogAction(
                    "discard",
                    "Discard",
                    DeucarianEditorIconIds.Remove,
                    DeucarianEditorDialogActionStyle.Destructive);
                var options = new DeucarianEditorDialogOptions(
                    "Package registry changed",
                    interruptedSummary,
                    DeucarianEditorIconIds.Warning,
                    new[] { restartFreshAction, keepAction, discardAction })
                {
                    Details = BuildRecoveryRegistryDriftReview(
                        interruptedSummary,
                        freshPlan,
                        planDelta),
                    DefaultActionId = restartFreshAction.Id,
                    CancelActionId = keepAction.Id
                };
                TryShowManagedDialog(options, result =>
                {
                    if (result.WasCanceled || this == null)
                    {
                        return;
                    }

                    if (!IsRecoveryStillCurrent(recovery) ||
                        !_packageDependencyInstaller.IsPlanStillCurrent(freshPlan))
                    {
                        RejectStaleRecoveryConfirmation(recovery);
                        return;
                    }

                    if (string.Equals(result.ActionId, restartFreshAction.Id, StringComparison.Ordinal))
                    {
                        _packageInstallService.DiscardSavedOperation();
                        _packageInstallService.InstallPlan(
                            freshPlan,
                            string.IsNullOrWhiteSpace(recovery.OperationName)
                                ? "Restart Package Operation"
                                : recovery.OperationName);
                    }
                    else if (string.Equals(result.ActionId, discardAction.Id, StringComparison.Ordinal))
                    {
                        _packageInstallService.DiscardSavedOperation();
                    }
                });
                return;
            }

            if (!canReuseExactTargets)
            {
                string planningFailure = freshPlan == null
                    ? "One or more saved root packages are no longer registered."
                    : freshPlan.ErrorMessage;
                var keepAction = new DeucarianEditorDialogAction(
                    "keep",
                    "Keep for Later",
                    DeucarianEditorIconIds.History,
                    DeucarianEditorDialogActionStyle.Primary);
                var discardAction = new DeucarianEditorDialogAction(
                    "discard",
                    "Discard",
                    DeucarianEditorIconIds.Remove,
                    DeucarianEditorDialogActionStyle.Destructive);
                var closeAction = new DeucarianEditorDialogAction(
                    "close",
                    "Close",
                    DeucarianEditorIconIds.Clear);
                var options = new DeucarianEditorDialogOptions(
                    "Package operation needs replanning",
                    "The current registry cannot reproduce a complete valid plan, so its saved URLs cannot be resumed safely.",
                    DeucarianEditorIconIds.Error,
                    new[] { keepAction, discardAction, closeAction })
                {
                    Details = summary +
                              (string.IsNullOrWhiteSpace(planningFailure)
                                  ? string.Empty
                                  : "\n\n" + planningFailure),
                    DefaultActionId = keepAction.Id,
                    CancelActionId = closeAction.Id
                };
                TryShowManagedDialog(options, result =>
                {
                    if (!result.WasCanceled &&
                        string.Equals(result.ActionId, discardAction.Id, StringComparison.Ordinal) &&
                        this != null)
                    {
                        if (!IsRecoveryStillCurrent(recovery))
                        {
                            RejectStaleRecoveryConfirmation(recovery);
                            return;
                        }

                        _packageInstallService.DiscardSavedOperation();
                    }
                });
                return;
            }

            var resumeAction = new DeucarianEditorDialogAction(
                "resume",
                recovery.CanResume ? "Resume" : "Restart",
                recovery.CanResume
                    ? DeucarianEditorIconIds.Play
                    : DeucarianEditorIconIds.Refresh,
                DeucarianEditorDialogActionStyle.Primary);
            var restartAction = new DeucarianEditorDialogAction(
                "restart",
                recovery.CanResume ? "Restart" : "Restart from Beginning",
                DeucarianEditorIconIds.Refresh);
            var discardRecoveryAction = new DeucarianEditorDialogAction(
                "discard",
                "Discard",
                DeucarianEditorIconIds.Remove,
                DeucarianEditorDialogActionStyle.Destructive);
            var recoveryOptions = new DeucarianEditorDialogOptions(
                "Resume package operation",
                summary,
                DeucarianEditorIconIds.History,
                new[] { resumeAction, restartAction, discardRecoveryAction })
            {
                DefaultActionId = resumeAction.Id,
                CancelActionId = string.Empty
            };
            TryShowManagedDialog(recoveryOptions, result =>
            {
                if (result.WasCanceled || this == null)
                {
                    return;
                }

                if (!IsRecoveryStillCurrent(recovery) ||
                    !_packageDependencyInstaller.IsPlanStillCurrent(freshPlan))
                {
                    RejectStaleRecoveryConfirmation(recovery);
                    return;
                }

                if (string.Equals(result.ActionId, resumeAction.Id, StringComparison.Ordinal))
                {
                    if (recovery.CanResume)
                    {
                        _packageInstallService.ResumeSavedOperation(freshPlan.RegistryFingerprint);
                    }
                    else
                    {
                        _packageInstallService.RestartSavedOperation(freshPlan.RegistryFingerprint);
                    }
                }
                else if (string.Equals(result.ActionId, restartAction.Id, StringComparison.Ordinal))
                {
                    _packageInstallService.RestartSavedOperation(freshPlan.RegistryFingerprint);
                }
                else if (string.Equals(result.ActionId, discardRecoveryAction.Id, StringComparison.Ordinal))
                {
                    _packageInstallService.DiscardSavedOperation();
                }
            });
        }

        private bool IsRecoveryStillCurrent(PackageOperationRecoveryRecord expectedRecovery)
        {
            if (expectedRecovery == null ||
                _packageInstallService == null ||
                _packageInstallService.IsBusy ||
                !_packageInstallService.TryGetSavedOperation(
                    out PackageOperationRecoveryRecord currentRecovery,
                    out _) ||
                currentRecovery == null)
            {
                return false;
            }

            return string.Equals(
                       currentRecovery.OperationId,
                       expectedRecovery.OperationId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       currentRecovery.RegistryFingerprint,
                       expectedRecovery.RegistryFingerprint,
                       StringComparison.Ordinal) &&
                   currentRecovery.CreatedAtUtcTicks == expectedRecovery.CreatedAtUtcTicks &&
                   currentRecovery.UpdatedAtUtcTicks == expectedRecovery.UpdatedAtUtcTicks;
        }

        private void RejectStaleRecoveryConfirmation(PackageOperationRecoveryRecord recovery)
        {
            _promptSavedOperationAfterDetectionRefresh =
                _packageInstallService != null && _packageInstallService.HasSavedOperation;
            RecordStaleConfirmation(
                string.IsNullOrWhiteSpace(recovery?.OperationName)
                    ? "Package operation recovery"
                    : recovery.OperationName,
                "Saved package operation state changed while the recovery dialog was open.");
        }

        private static void RecordStaleConfirmation(string operationName, string reason)
        {
            string message = (string.IsNullOrWhiteSpace(operationName)
                    ? "Package operation"
                    : operationName) +
                " was not changed. " + (reason ?? string.Empty).Trim();
            PackageInstallerLog.Install.Warning(message);
            PackageInstallerActivityService.Record(
                "Packages",
                PackageInstallerActivitySeverity.Warning,
                message);
        }

        private PackageDependencyInstallPlan CreateFreshRecoveryPlan(
            PackageOperationRecoveryRecord recovery)
        {
            if (recovery == null || _packageDependencyInstaller == null)
            {
                return null;
            }

            HashSet<string> rootIds = new HashSet<string>(
                recovery.Steps
                    .Where(step => step.State != PackageInstallProgressItemState.Completed &&
                                   step.State != PackageInstallProgressItemState.AlreadyCorrect)
                    .SelectMany(step => step.RootPackageIds),
                StringComparer.OrdinalIgnoreCase);
            if (rootIds.Count == 0)
            {
                rootIds.UnionWith(recovery.Steps
                    .Where(step => !step.IsDependency)
                    .Select(step => step.PackageId));
            }

            PackageDefinition[] roots = rootIds
                .Select(packageId =>
                    PackageRegistryProvider.TryGetPackage(packageId, out PackageDefinition definition)
                        ? definition
                        : null)
                .Where(definition => definition != null)
                .ToArray();
            return roots.Length == 0 || roots.Length != rootIds.Count
                ? null
                : _packageDependencyInstaller.CreateInstallPlan(
                    roots,
                    package => GetRecoveryRequestedChannel(
                        recovery,
                        package.PackageId,
                        GetSelectedChannel(package)),
                    includeInstalledRequestedPackages: true);
        }

        internal static PackageChannel GetRecoveryRequestedChannelForTests(
            PackageOperationRecoveryRecord recovery,
            string rootPackageId,
            PackageChannel fallback)
        {
            return GetRecoveryRequestedChannel(recovery, rootPackageId, fallback);
        }

        private static PackageChannel GetRecoveryRequestedChannel(
            PackageOperationRecoveryRecord recovery,
            string rootPackageId,
            PackageChannel fallback)
        {
            if (recovery == null || string.IsNullOrWhiteSpace(rootPackageId))
            {
                return fallback;
            }

            PackageOperationRootRequest rootRequest = recovery.RootRequests.FirstOrDefault(root =>
                root != null && string.Equals(
                    root.PackageId,
                    rootPackageId,
                    StringComparison.OrdinalIgnoreCase));
            if (rootRequest != null)
            {
                return rootRequest.Channel;
            }

            return recovery.Steps
                .Where(step => step.RootPackageIds.Contains(
                    rootPackageId,
                    StringComparer.OrdinalIgnoreCase))
                .Select(step => step.RequestedChannel)
                .DefaultIfEmpty(fallback)
                .First();
        }

        internal static string FormatRecoveryPlanDeltaForTests(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            return FormatRecoveryPlanDelta(recovery, freshPlan);
        }
    }
}
