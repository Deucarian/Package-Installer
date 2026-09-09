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
    internal static class PackageOperationPlanReview
    {
        internal static string FormatTerminalRetryPlanDelta(
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
                        "\n  was [" + PackageChannelPolicy.GetChannelLabel(oldStep.Channel) + "] " + oldStep.TargetUrl +
                        "\n  now [" + PackageChannelPolicy.GetChannelLabel(newStep.Channel) + "] " + newStep.TargetUrl);
                }
            }

            return string.Join("\n", lines.ToArray());
        }

        internal static List<string> DescribeRecoveryStepChanges(
            PackageOperationRecoveryStep oldStep,
            PackageDependencyInstallStep newStep)
        {
            List<string> changes = new List<string>();
            if (oldStep.Channel != newStep.Channel ||
                !string.Equals(oldStep.TargetUrl, newStep.TargetUrl, StringComparison.Ordinal))
            {
                changes.Add(
                    "target:\n    was [" + PackageChannelPolicy.GetChannelLabel(oldStep.Channel) + "] " + oldStep.TargetUrl +
                    "\n    now [" + PackageChannelPolicy.GetChannelLabel(newStep.Channel) + "] " + newStep.TargetUrl);
            }

            if (oldStep.RequestedChannel != newStep.RequestedChannel)
            {
                changes.Add(
                    "requested channel: " + PackageChannelPolicy.GetChannelLabel(oldStep.RequestedChannel) +
                    " -> " + PackageChannelPolicy.GetChannelLabel(newStep.RequestedChannel));
            }

            if (oldStep.IsDependency != newStep.IsDependency)
            {
                changes.Add(
                    "role: " + FormatOperationStepRole(oldStep.IsDependency) +
                    " -> " + FormatOperationStepRole(newStep.IsDependency));
            }

            AddStringSetChange(
                changes,
                "prerequisites",
                oldStep.PrerequisitePackageIds,
                newStep.PrerequisitePackageIds);
            AddStringSetChange(
                changes,
                "root packages",
                oldStep.RootPackageIds,
                newStep.RootPackageIds);
            AddStringSetChange(
                changes,
                "root paths",
                oldStep.RootPaths,
                newStep.RootPaths);

            string oldReason = (oldStep.DependencyReason ?? string.Empty).Trim();
            string newReason = (newStep.DependencyReason ?? string.Empty).Trim();
            if (!string.Equals(oldReason, newReason, StringComparison.Ordinal))
            {
                changes.Add(
                    "dependency reason: " + FormatOptionalPlanDetail(oldReason) +
                    " -> " + FormatOptionalPlanDetail(newReason));
            }

            AddStringValueChange(
                changes,
                "detected source",
                oldStep.DetectedCurrentSource,
                newStep.DetectedCurrentSource);
            AddStringValueChange(
                changes,
                "detected version",
                oldStep.DetectedCurrentVersion,
                newStep.DetectedCurrentVersion);
            AddStringValueChange(
                changes,
                "detected identity",
                oldStep.DetectedCurrentIdentity,
                newStep.DetectedCurrentIdentity);

            return changes;
        }

        internal static void AddStringValueChange(
            ICollection<string> changes,
            string label,
            string previousValue,
            string currentValue)
        {
            string previous = (previousValue ?? string.Empty).Trim();
            string current = (currentValue ?? string.Empty).Trim();
            if (string.Equals(previous, current, StringComparison.Ordinal))
            {
                return;
            }

            changes.Add(
                label + ": " + FormatOptionalPlanDetail(previous) +
                " -> " + FormatOptionalPlanDetail(current));
        }

        internal static void AddStringSetChange(
            ICollection<string> changes,
            string label,
            IEnumerable<string> previousValues,
            IEnumerable<string> currentValues)
        {
            string[] previous = NormalizePlanDetailSet(previousValues);
            string[] current = NormalizePlanDetailSet(currentValues);
            if (new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase).SetEquals(current))
            {
                return;
            }

            changes.Add(
                label + ": " + FormatPlanDetailSet(previous) +
                " -> " + FormatPlanDetailSet(current));
        }

        internal static string[] NormalizePlanDetailSet(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static string FormatPlanDetailSet(IEnumerable<string> values)
        {
            string[] normalized = NormalizePlanDetailSet(values);
            return normalized.Length > 0
                ? string.Join(", ", normalized)
                : "(none)";
        }

        internal static string FormatOptionalPlanDetail(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
        }

        internal static string FormatOperationStepRole(bool isDependency)
        {
            return isDependency ? "dependency" : "requested root";
        }

        internal static string BuildTerminalRetryReview(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstallPlan freshPlan,
            string delta)
        {
            List<string> lines = new List<string>
            {
                "Installed and registry state were refreshed. " +
                (string.IsNullOrWhiteSpace(snapshot.OperationName)
                    ? "The affected roots"
                    : snapshot.OperationName + "'s affected roots") +
                " were planned again from the current registry.",
                string.Empty
            };
            if (!string.IsNullOrWhiteSpace(delta))
            {
                lines.Add("Plan changes:");
                lines.Add(delta);
                lines.Add(string.Empty);
            }

            lines.Add("Fresh plan:");
            lines.AddRange(freshPlan.Steps.Select(step =>
                "- " + step.PackageDefinition.DisplayName +
                " [" + PackageChannelPolicy.GetChannelLabel(step.Channel) + "]\n  " + step.TargetUrl));
            if (freshPlan.RequiresPreflight)
            {
                lines.Add(string.Empty);
                lines.Add("This plan requires review because it is bulk, multi-step, or carries migration, fallback, downgrade, conflict, or destructive risk.");
            }

            return string.Join("\n", lines.Where(line => line != null).ToArray()).Trim();
        }

        internal static string FormatRecoveryPlanDelta(
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

        internal static string BuildRecoveryRegistryDriftReview(
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
                " [" + PackageChannelPolicy.GetChannelLabel(step.Channel) + "]\n  " + step.TargetUrl));

            if (freshPlan.RequiresPreflight)
            {
                lines.Add(string.Empty);
                lines.Add("Attention: this plan is bulk, multi-step, or carries migration, fallback, downgrade, conflict, or destructive risk.");
            }

            return string.Join("\n", lines.Where(line => line != null).ToArray()).Trim();
        }
    }
}
