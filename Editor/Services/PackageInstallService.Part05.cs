using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallService
    {


        private static PackageOperationRecoveryStep NormalizeStepForResume(
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

        private static bool IsResumableState(PackageInstallProgressItemState state)
        {
            return state == PackageInstallProgressItemState.Pending ||
                   state == PackageInstallProgressItemState.Active;
        }

        private void RestoreOperation(PackageOperationRecoveryRecord record)
        {
            List<PackageDependencyInstallStep> planSteps = new List<PackageDependencyInstallStep>();
            Dictionary<string, PackageDefinition> definitions =
                new Dictionary<string, PackageDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageOperationRecoveryStep recoveryStep in record.Steps)
            {
                PackageDefinition packageDefinition = CreateRecoveredPackageDefinition(recoveryStep);
                definitions[recoveryStep.PackageId] = packageDefinition;
                planSteps.Add(new PackageDependencyInstallStep(
                    packageDefinition,
                    recoveryStep.Channel,
                    recoveryStep.IsDependency,
                    recoveryStep.TargetUrl,
                    recoveryStep.PrerequisitePackageIds,
                    recoveryStep.RootPackageIds,
                    recoveryStep.RootPaths,
                    recoveryStep.DependencyReason,
                    recoveryStep.DetectedCurrentSource,
                    recoveryStep.DetectedCurrentVersion,
                    recoveryStep.DetectedCurrentIdentity,
                    recoveryStep.RequestedChannel));
            }

            PackageDependencyInstallPlan plan = PackageDependencyInstallPlan.Restore(
                record.OperationId,
                record.RegistryFingerprint,
                record.CreatedAtUtcTicks,
                planSteps,
                record.Messages,
                record.RootRequests);
            BeginOperation(
                string.IsNullOrWhiteSpace(record.OperationName)
                    ? "Resume Package Operation"
                    : record.OperationName,
                plan);

            foreach (PackageOperationRecoveryStep recoveryStep in record.Steps)
            {
                PackageDependencyInstallStep step = plan.Steps.First(planStep =>
                    string.Equals(
                        planStep.PackageDefinition.PackageId,
                        recoveryStep.PackageId,
                        StringComparison.OrdinalIgnoreCase));
                QueuedPackageInstall install = new QueuedPackageInstall(step);
                _operationInstallsByPackageId[recoveryStep.PackageId] = install;

                if (IsResumableState(recoveryStep.State))
                {
                    if (ExactTargetAlreadyInstalled != null &&
                        ExactTargetAlreadyInstalled(
                            recoveryStep.PackageId,
                            recoveryStep.TargetUrl,
                            recoveryStep.DetectedCurrentIdentity))
                    {
                        MarkProgressItem(
                            definitions[recoveryStep.PackageId],
                            PackageInstallProgressItemState.AlreadyCorrect,
                            "Already at the saved exact target after refresh.");
                        continue;
                    }

                    _installQueue.Add(install);
                    _queuedOrInstallingPackageIds.Add(recoveryStep.PackageId);
                    continue;
                }

                MarkProgressItem(
                    definitions[recoveryStep.PackageId],
                    recoveryStep.State,
                    recoveryStep.Message);
            }
        }

        private static PackageDefinition CreateRecoveredPackageDefinition(
            PackageOperationRecoveryStep step)
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

        private void NotifyStateChanged()
        {
            PackageInstallerEditorStatus.PublishOperation(this);
            StateChanged?.Invoke();
        }

        private sealed class QueuedPackageInstall
        {
            public QueuedPackageInstall(PackageDependencyInstallStep step)
            {
                Step = step ?? throw new ArgumentNullException(nameof(step));
            }

            public PackageDependencyInstallStep Step { get; }

            public PackageDefinition PackageDefinition => Step.PackageDefinition;

            public PackageChannel Channel => Step.Channel;

            public string Url => Step.TargetUrl;

            public bool IsDependency => Step.IsDependency;

            public IReadOnlyList<string> PrerequisitePackageIds => Step.PrerequisitePackageIds;

            public IReadOnlyList<string> RootPackageIds => Step.RootPackageIds;

            public IReadOnlyList<string> RootPaths => Step.RootPaths;

            public string DependencyReason => Step.DependencyReason;
        }
    }
}
