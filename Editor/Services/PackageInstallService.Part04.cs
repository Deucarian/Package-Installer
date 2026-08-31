using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallService
    {


        private string FormatSkippedSummarySuffix()
        {
            return _skippedSteps > 0 ? " and " + _skippedSteps + " skipped" : string.Empty;
        }

        private string FormatOperationOutcomeSuffix()
        {
            List<string> parts = new List<string>();

            if (_successfulSteps > 0)
            {
                parts.Add(_successfulSteps + " succeeded");
            }

            if (_failedSteps > 0)
            {
                parts.Add(_failedSteps + " failed");
            }

            if (_skippedSteps > 0)
            {
                parts.Add(_skippedSteps + " skipped");
            }

            if (_blockedSteps > 0)
            {
                parts.Add(_blockedSteps + " blocked");
            }

            if (_canceledSteps > 0)
            {
                parts.Add(_canceledSteps + " canceled");
            }

            return parts.Count > 0 ? " with " + string.Join(", ", parts.ToArray()) : string.Empty;
        }

        private void CancelQueuedInstalls(string message)
        {
            while (_installQueue.Count > 0)
            {
                QueuedPackageInstall install = _installQueue[0];
                _installQueue.RemoveAt(0);

                if (install == null || install.PackageDefinition == null)
                {
                    continue;
                }

                _queuedOrInstallingPackageIds.Remove(install.PackageDefinition.PackageId);
                MarkProgressItem(
                    install.PackageDefinition,
                    PackageInstallProgressItemState.Canceled,
                    message);
            }
        }

        private static bool IsTerminalState(PackageInstallProgressItemState state)
        {
            return state == PackageInstallProgressItemState.Completed ||
                   state == PackageInstallProgressItemState.Failed ||
                   state == PackageInstallProgressItemState.Skipped ||
                   state == PackageInstallProgressItemState.Blocked ||
                   state == PackageInstallProgressItemState.Canceled ||
                   state == PackageInstallProgressItemState.AlreadyCorrect;
        }

        private bool CanStartInstall(QueuedPackageInstall install)
        {
            if (install == null)
            {
                return false;
            }

            foreach (string prerequisitePackageId in install.PrerequisitePackageIds)
            {
                if (!_progressItemsByPackageId.TryGetValue(
                        prerequisitePackageId,
                        out PackageInstallProgressItem prerequisite))
                {
                    return false;
                }

                if (prerequisite.State != PackageInstallProgressItemState.Completed &&
                    prerequisite.State != PackageInstallProgressItemState.AlreadyCorrect)
                {
                    return false;
                }
            }

            return true;
        }

        private void BlockInstallsWithFailedPrerequisites()
        {
            bool changed;

            do
            {
                changed = false;

                foreach (QueuedPackageInstall install in _installQueue.ToArray())
                {
                    string failedPrerequisiteId = install.PrerequisitePackageIds.FirstOrDefault(
                        prerequisiteId =>
                            _progressItemsByPackageId.TryGetValue(
                                prerequisiteId,
                                out PackageInstallProgressItem prerequisite) &&
                            (prerequisite.State == PackageInstallProgressItemState.Failed ||
                             prerequisite.State == PackageInstallProgressItemState.Blocked ||
                             prerequisite.State == PackageInstallProgressItemState.Canceled));

                    if (string.IsNullOrWhiteSpace(failedPrerequisiteId))
                    {
                        continue;
                    }

                    _installQueue.Remove(install);
                    _queuedOrInstallingPackageIds.Remove(install.PackageDefinition.PackageId);
                    MarkProgressItem(
                        install.PackageDefinition,
                        PackageInstallProgressItemState.Blocked,
                        "Blocked because prerequisite " + failedPrerequisiteId + " did not complete.");
                    changed = true;
                }
            }
            while (changed);
        }

        private void BlockUnresolvableInstalls()
        {
            foreach (QueuedPackageInstall install in _installQueue.ToArray())
            {
                _installQueue.Remove(install);
                _queuedOrInstallingPackageIds.Remove(install.PackageDefinition.PackageId);
                MarkProgressItem(
                    install.PackageDefinition,
                    PackageInstallProgressItemState.Blocked,
                    "Blocked because the prerequisite graph could not be satisfied.");
            }
        }

        private void SavePendingOperationState()
        {
            if (!IsBusy || State == PackageInstallRequestState.Removing)
            {
                ClearSavedOperationState(_operationStateRepository);
                return;
            }

            PackageOperationRecoveryStep[] steps = _progressItems
                .Where(item => item != null &&
                               _operationInstallsByPackageId.ContainsKey(item.PackageId))
                .Select(item =>
                {
                    QueuedPackageInstall install = _operationInstallsByPackageId[item.PackageId];
                    return new PackageOperationRecoveryStep(
                        item.PackageId,
                        item.DisplayName,
                        install.Channel,
                        install.Url,
                        install.IsDependency,
                        install.PrerequisitePackageIds,
                        install.RootPackageIds,
                        install.RootPaths,
                        install.DependencyReason,
                        item.State,
                        item.Message,
                        install.Step.DetectedCurrentSource,
                        install.Step.DetectedCurrentVersion,
                        install.Step.DetectedCurrentIdentity,
                        install.Step.RequestedChannel);
                })
                .ToArray();

            if (steps.Length == 0)
            {
                ClearSavedOperationState(_operationStateRepository);
                return;
            }

            PackageOperationRecoveryRecord record = new PackageOperationRecoveryRecord(
                _currentOperationId,
                _currentOperationName,
                _currentRegistryFingerprint,
                _currentOperationCreatedAtUtcTicks,
                DateTime.UtcNow.Ticks,
                steps,
                _operationMessages,
                _currentRootRequests);

            if (!_operationStateRepository.Save(record, out string errorMessage))
            {
                PackageInstallerLog.Install.Warning(errorMessage);
            }

            ClearLegacySavedOperationState();
        }

        private static void ClearSavedOperationState(PackageOperationStateRepository repository)
        {
            repository?.Clear();
            PackageOperationAutoResumeState.Clear();
            ClearLegacySavedOperationState();
        }

        private static void ClearLegacySavedOperationState()
        {
            SessionState.SetString(PendingOperationNameKey, string.Empty);
            SessionState.SetString(PendingQueueKey, string.Empty);
        }

        private static bool TryLoadSavedOperation(
            PackageOperationStateRepository repository,
            out PackageOperationRecoveryRecord record)
        {
            record = null;

            if (repository.TryLoad(out record, out string repositoryError))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(repositoryError))
            {
                PackageInstallerLog.Install.Warning(repositoryError);
                return false;
            }

            string operationName = SessionState.GetString(PendingOperationNameKey, string.Empty);
            string queue = SessionState.GetString(PendingQueueKey, string.Empty);

            if (string.IsNullOrWhiteSpace(queue))
            {
                return false;
            }

            List<PackageOperationRecoveryStep> legacySteps = new List<PackageOperationRecoveryStep>();

            foreach (string line in queue.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('|');

                if (parts.Length < 2 ||
                    !PackageRegistryProvider.TryGetPackage(parts[0], out PackageDefinition packageDefinition) ||
                    !int.TryParse(parts[1], out int channelValue))
                {
                    continue;
                }

                PackageChannel channel = Enum.IsDefined(typeof(PackageChannel), channelValue)
                    ? (PackageChannel)channelValue
                    : PackageChannel.Stable;
                string url = packageDefinition.GetUrl(channel);

                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                legacySteps.Add(new PackageOperationRecoveryStep(
                    packageDefinition.PackageId,
                    packageDefinition.DisplayName,
                    channel,
                    url,
                    isDependency: false,
                    prerequisitePackageIds: Array.Empty<string>(),
                    rootPackageIds: new[] { packageDefinition.PackageId },
                    rootPaths: new[] { packageDefinition.DisplayName },
                    dependencyReason: string.Empty,
                    state: PackageInstallProgressItemState.Pending,
                    message: string.Empty));
            }

            if (legacySteps.Count == 0)
            {
                return false;
            }

            record = new PackageOperationRecoveryRecord(
                Guid.NewGuid().ToString("N"),
                operationName,
                string.Empty,
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.Ticks,
                legacySteps,
                Array.Empty<string>());

            if (!repository.Save(record, out string saveError))
            {
                PackageInstallerLog.Install.Warning(saveError);
            }
            else
            {
                ClearLegacySavedOperationState();
            }

            return true;
        }

        private static bool TryPrepareSavedOperationForResume(
            PackageOperationStateRepository repository,
            bool selfUpdateAppliedOnReload,
            out PackageOperationRecoveryRecord record)
        {
            if (!TryLoadSavedOperation(repository, out record))
            {
                ClearSavedOperationState(repository);

                if (selfUpdateAppliedOnReload)
                {
                    PackageInstallerSelfUpdateState.AcknowledgeApplied();
                }

                return false;
            }

            IEnumerable<PackageOperationRecoveryStep> preparedSteps = record.Steps;

            if (selfUpdateAppliedOnReload)
            {
                preparedSteps = FilterAppliedSelfUpdate(preparedSteps);
            }

            PackageOperationRecoveryStep[] normalizedSteps = preparedSteps
                .Where(step => step != null)
                .Select(step => NormalizeStepForResume(step, selfUpdateAppliedOnReload))
                .ToArray();
            PackageOperationRootRequest[] normalizedRootRequests = record.RootRequests
                .Where(root => normalizedSteps.Any(step => step.RootPackageIds.Contains(
                    root.PackageId,
                    StringComparer.OrdinalIgnoreCase)))
                .ToArray();

            if (!normalizedSteps.Any(step => IsResumableState(step.State)))
            {
                ClearSavedOperationState(repository);

                if (selfUpdateAppliedOnReload)
                {
                    PackageInstallerSelfUpdateState.AcknowledgeApplied();
                }

                record = null;
                return false;
            }

            record = new PackageOperationRecoveryRecord(
                record.OperationId,
                record.OperationName,
                record.RegistryFingerprint,
                record.CreatedAtUtcTicks,
                DateTime.UtcNow.Ticks,
                normalizedSteps,
                record.Messages,
                normalizedRootRequests);

            if (!repository.Save(record, out string saveError))
            {
                PackageInstallerLog.Install.Warning(saveError);
                return false;
            }

            if (selfUpdateAppliedOnReload)
            {
                PackageInstallerSelfUpdateState.AcknowledgeApplied();
            }

            return true;
        }

        private static IEnumerable<PackageOperationRecoveryStep> FilterAppliedSelfUpdate(
            IEnumerable<PackageOperationRecoveryStep> steps)
        {
            return (steps ?? Array.Empty<PackageOperationRecoveryStep>())
                .Where(step =>
                    step != null &&
                    !PackageInstallerRuntimeIdentity.IsSelf(step.PackageId))
                .ToArray();
        }
    }
}
