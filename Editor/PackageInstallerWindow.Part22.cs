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


        private static List<string> DescribeRecoveryStepChanges(
            PackageOperationRecoveryStep oldStep,
            PackageDependencyInstallStep newStep)
        {
            List<string> changes = new List<string>();
            if (oldStep.Channel != newStep.Channel ||
                !string.Equals(oldStep.TargetUrl, newStep.TargetUrl, StringComparison.Ordinal))
            {
                changes.Add(
                    "target:\n    was [" + GetChannelLabel(oldStep.Channel) + "] " + oldStep.TargetUrl +
                    "\n    now [" + GetChannelLabel(newStep.Channel) + "] " + newStep.TargetUrl);
            }

            if (oldStep.RequestedChannel != newStep.RequestedChannel)
            {
                changes.Add(
                    "requested channel: " + GetChannelLabel(oldStep.RequestedChannel) +
                    " -> " + GetChannelLabel(newStep.RequestedChannel));
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

        private static void AddStringValueChange(
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

        private static void AddStringSetChange(
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

        private static string[] NormalizePlanDetailSet(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string FormatPlanDetailSet(IEnumerable<string> values)
        {
            string[] normalized = NormalizePlanDetailSet(values);
            return normalized.Length > 0
                ? string.Join(", ", normalized)
                : "(none)";
        }

        private static string FormatOptionalPlanDetail(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
        }

        private static string FormatOperationStepRole(bool isDependency)
        {
            return isDependency ? "dependency" : "requested root";
        }

        private static string BuildTerminalRetryReview(
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
                " [" + GetChannelLabel(step.Channel) + "]\n  " + step.TargetUrl));
            if (freshPlan.RequiresPreflight)
            {
                lines.Add(string.Empty);
                lines.Add("This plan requires review because it is bulk, multi-step, or carries migration, fallback, downgrade, conflict, or destructive risk.");
            }

            return string.Join("\n", lines.Where(line => line != null).ToArray()).Trim();
        }

        private void TryPromptForSavedOperationRecovery()
        {
            if (!_promptSavedOperationAfterDetectionRefresh ||
                (_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                (_packageDetectionService != null && !_packageDetectionService.HasSuccessfulRefresh) ||
                PackageRegistryProvider.IsRemoteRefreshing)
            {
                return;
            }

            _promptSavedOperationAfterDetectionRefresh = false;
            PromptForSavedOperationRecovery();
        }
    }
}
