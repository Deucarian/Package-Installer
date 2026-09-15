using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    // Pure translation of persisted rows. The service still owns requests, state and recovery approval.
    internal static class PackageOperationRecoveryPreparation
    {
        internal static PackageDefinition CreateDefinition(PackageOperationRecoveryStep step)
        {
            string displayName = string.IsNullOrWhiteSpace(step.DisplayName)
                ? step.PackageId
                : step.DisplayName;
            string developmentUrl = step.Channel == PackageChannel.Development
                ? step.TargetUrl
                : string.Empty;

            return new PackageDefinition(
                displayName,
                step.PackageId,
                step.TargetUrl,
                string.Empty,
                Array.Empty<string>(),
                PackageKind.Library,
                developmentUrl,
                category: "Tools");
        }

        internal static PackageOperationRecoveryStep NormalizeForResume(
            PackageOperationRecoveryStep step,
            bool selfUpdateAppliedOnReload)
        {
            PackageInstallProgressItemState state = step.State == PackageInstallProgressItemState.Active
                ? PackageInstallProgressItemState.Pending
                : step.State;
            string[] prerequisites = selfUpdateAppliedOnReload
                ? step.PrerequisitePackageIds
                    .Where(id => !PackageInstallerRuntimeIdentity.IsSelf(id))
                    .ToArray()
                : step.PrerequisitePackageIds.ToArray();

            return new PackageOperationRecoveryStep(
                step.PackageId,
                step.DisplayName,
                step.Channel,
                step.TargetUrl,
                step.IsDependency,
                prerequisites,
                step.RootPackageIds,
                step.RootPaths,
                step.DependencyReason,
                state,
                step.Message,
                step.DetectedCurrentSource,
                step.DetectedCurrentVersion,
                step.DetectedCurrentIdentity,
                step.RequestedChannel);
        }

        internal static IEnumerable<PackageOperationRecoveryStep> ReconcileAppliedSelfUpdate(
            IEnumerable<PackageOperationRecoveryStep> steps)
        {
            return (steps ?? Array.Empty<PackageOperationRecoveryStep>())
                .Where(step => step != null)
                .Select(step => PackageInstallerRuntimeIdentity.IsSelf(step.PackageId)
                    ? new PackageOperationRecoveryStep(
                        step.PackageId, step.DisplayName, step.Channel, step.TargetUrl,
                        step.IsDependency, step.PrerequisitePackageIds, step.RootPackageIds,
                        step.RootPaths, step.DependencyReason,
                        PackageInstallProgressItemState.AlreadyCorrect,
                        "Installer update loaded after Unity reload.", step.DetectedCurrentSource,
                        step.DetectedCurrentVersion, step.DetectedCurrentIdentity, step.RequestedChannel)
                    : step)
                .ToArray();
        }
    }
}
