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


        private static string FormatRecoveryPlanDelta(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            if (recovery == null || freshPlan == null || !freshPlan.IsValid)
            {
                return string.Empty;
            }

            Dictionary<string, PackageOperationRecoveryStep> previous = recovery.Steps
                .Where(step => step != null && !string.IsNullOrWhiteSpace(step.PackageId))
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
                bool hadPrevious = previous.TryGetValue(packageId, out PackageOperationRecoveryStep oldStep);
                bool hasCurrent = current.TryGetValue(packageId, out PackageDependencyInstallStep newStep);

                if (!hadPrevious)
                {
                    lines.Add("Added: " + newStep.PackageDefinition.DisplayName + " -> " + newStep.TargetUrl);
                }
                else if (!hasCurrent)
                {
                    lines.Add("Now skipped: " + oldStep.DisplayName + " is already correct or no longer required.");
                }
                else
                {
                    List<string> changes = DescribeRecoveryStepChanges(oldStep, newStep);
                    if (changes.Count > 0)
                    {
                        lines.Add(
                            "Changed: " + newStep.PackageDefinition.DisplayName +
                            "\n  " + string.Join("\n  ", changes.ToArray()));
                    }
                }
            }

            return string.Join("\n", lines.ToArray());
        }

        private static string BuildRecoveryRegistryDriftReview(
            string summary,
            PackageDependencyInstallPlan freshPlan,
            string planDelta)
        {
            List<string> lines = new List<string>
            {
                (summary ?? string.Empty).Trim(),
                string.Empty,
                "The registry fingerprint changed. Saved exact URLs will not be reused.",
                string.Empty,
                "Plan changes:",
                string.IsNullOrWhiteSpace(planDelta)
                    ? "No target URL changed in the remaining plan; registry metadata changed elsewhere."
                    : planDelta,
                string.Empty,
                "Fresh plan:"
            };
            lines.AddRange(freshPlan.Steps.Select(step =>
                "- " + step.PackageDefinition.DisplayName +
                " [" + GetChannelLabel(step.Channel) + "]\n  " + step.TargetUrl));

            if (freshPlan.RequiresPreflight)
            {
                lines.Add(string.Empty);
                lines.Add("Attention: this plan is bulk, multi-step, or carries migration, fallback, downgrade, conflict, or destructive risk.");
            }

            return string.Join("\n", lines.Where(line => line != null).ToArray()).Trim();
        }

        internal static bool CanReuseSavedExactTargetsForTests(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            return CanReuseSavedExactTargets(recovery, freshPlan);
        }

        private static bool CanReuseSavedExactTargets(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            return freshPlan != null &&
                   freshPlan.IsValid &&
                   PackageInstallService.CanReuseSavedTargets(
                       recovery,
                       freshPlan.RegistryFingerprint);
        }

        internal static PackageOperationRecoveryDisposition GetRecoveryDispositionForTests(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan,
            bool hasMatchingReloadMarker)
        {
            return GetRecoveryDisposition(recovery, freshPlan, hasMatchingReloadMarker);
        }

        private static PackageOperationRecoveryDisposition GetRecoveryDisposition(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan,
            bool hasMatchingReloadMarker)
        {
            return hasMatchingReloadMarker &&
                   recovery != null &&
                   recovery.CanResume &&
                   !recovery.RequiresManualRecovery &&
                   CanReuseSavedExactTargets(recovery, freshPlan)
                ? PackageOperationRecoveryDisposition.AutoResume
                : PackageOperationRecoveryDisposition.Prompt;
        }

        private void TrackPendingUpdateStatusInvalidations(IEnumerable<PackageDefinition> packageDefinitions)
        {
            foreach (PackageDefinition packageDefinition in packageDefinitions ?? Array.Empty<PackageDefinition>())
            {
                TrackPendingUpdateStatusInvalidation(packageDefinition);
            }
        }

        internal static bool ShouldRetainPendingUpdateStatusInvalidationsForTests(
            bool installBusy,
            bool awaitingPreflight)
        {
            return ShouldRetainPendingUpdateStatusInvalidations(installBusy, awaitingPreflight);
        }

        private static bool ShouldRetainPendingUpdateStatusInvalidations(
            bool installBusy,
            bool awaitingPreflight)
        {
            return installBusy || awaitingPreflight;
        }

        private void TrackPendingUpdateStatusInvalidation(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null || string.IsNullOrWhiteSpace(packageDefinition.PackageId))
            {
                return;
            }

            _pendingUpdateStatusInvalidationPackageIds.Add(packageDefinition.PackageId);
        }

        internal static bool TryConsumePendingUpdateStatusInvalidationForTests(
            ISet<string> pendingPackageIds,
            PackageDefinition completedPackage,
            bool success)
        {
            return TryConsumePendingUpdateStatusInvalidation(pendingPackageIds, completedPackage, success);
        }

        private static bool TryConsumePendingUpdateStatusInvalidation(
            ISet<string> pendingPackageIds,
            PackageDefinition completedPackage,
            bool success)
        {
            if (pendingPackageIds == null ||
                completedPackage == null ||
                string.IsNullOrWhiteSpace(completedPackage.PackageId) ||
                !pendingPackageIds.Remove(completedPackage.PackageId))
            {
                return false;
            }

            return success;
        }
    }
}
