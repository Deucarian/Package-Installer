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
    internal static class PackageOperationRecoveryPolicy
    {
        internal static bool CanReuseSavedExactTargets(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            return freshPlan != null &&
                   freshPlan.IsValid &&
                   PackageInstallService.CanReuseSavedTargets(
                       recovery,
                       freshPlan.RegistryFingerprint);
        }

        internal static PackageOperationRecoveryDisposition GetRecoveryDisposition(
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

        internal static PackageChannel GetRecoveryRequestedChannel(
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

        internal static PackageDependencyInstallPlan CreateFreshTerminalRetryPlan(
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

        internal static void RecordStaleConfirmation(string operationName, string reason)
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
    }
}
