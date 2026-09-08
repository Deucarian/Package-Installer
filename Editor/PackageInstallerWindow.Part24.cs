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

        internal static bool CanReuseSavedExactTargetsForTests(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            return PackageOperationRecoveryPolicy.CanReuseSavedExactTargets(recovery, freshPlan);
        }

        internal static PackageOperationRecoveryDisposition GetRecoveryDispositionForTests(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan,
            bool hasMatchingReloadMarker)
        {
            return PackageOperationRecoveryPolicy.GetRecoveryDisposition(recovery, freshPlan, hasMatchingReloadMarker);
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
