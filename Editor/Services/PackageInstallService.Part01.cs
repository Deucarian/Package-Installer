using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallService
    {


        public event Action StateChanged;

        public event Action<PackageDefinition, bool, string> InstallCompleted;

        public event Action QueueCompleted;

        internal Func<string, string, string, bool> ExactTargetAlreadyInstalled { get; set; }

        public PackageInstallService()
            : this(new UnityPackageInstallClient(), new PackageOperationStateRepository())
        {
        }

        internal PackageInstallService(
            IPackageInstallClient packageClient,
            PackageOperationStateRepository operationStateRepository)
        {
            _packageClient = packageClient ?? throw new ArgumentNullException(nameof(packageClient));
            _operationStateRepository = operationStateRepository ??
                                        throw new ArgumentNullException(nameof(operationStateRepository));
            PackageInstallerSelfUpdateState.ReconcileCurrentRuntime();
            PackageInstallerEditorStatus.PublishOperation(this);
        }

        public bool HasSavedOperation
        {
            get
            {
                return TryGetSavedOperation(out _, out _);
            }
        }

        public bool TryGetSavedOperation(
            out PackageOperationRecoveryRecord record,
            out string errorMessage)
        {
            if (_operationStateRepository.TryLoad(out record, out errorMessage))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return false;
            }

            bool loadedLegacy = TryLoadSavedOperation(_operationStateRepository, out record);
            errorMessage = loadedLegacy
                ? string.Empty
                : "No saved package operation is available.";
            return loadedLegacy;
        }

        public bool ResumeSavedOperation(string currentRegistryFingerprint)
        {
            if (IsBusy)
            {
                return false;
            }

            bool selfUpdateAppliedOnReload =
                PackageInstallerSelfUpdateState.ReconcileCurrentRuntime() ==
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload;

            if (!TryPrepareSavedOperationForResume(
                    _operationStateRepository,
                    selfUpdateAppliedOnReload,
                    out PackageOperationRecoveryRecord recoveryRecord))
            {
                return false;
            }

            if (recoveryRecord == null || recoveryRecord.Steps.Count == 0)
            {
                ClearSavedOperationState(_operationStateRepository);
                return false;
            }

            if (!CanReuseSavedTargets(recoveryRecord, currentRegistryFingerprint))
            {
                ReportUnsafeSavedTargetReuse(recoveryRecord.RegistryFingerprint);
                return false;
            }

            RestoreOperation(recoveryRecord);

            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            bool completedDuringReconciliation = CompleteRecoveredOperationWithoutRequestIfNeeded();
            NotifyStateChanged();

            return IsBusy || completedDuringReconciliation;
        }

        public bool RestartSavedOperation(string currentRegistryFingerprint)
        {
            if (IsBusy)
            {
                return false;
            }

            bool selfUpdateAppliedOnReload =
                PackageInstallerSelfUpdateState.ReconcileCurrentRuntime() ==
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload;

            if (!TryPrepareSavedOperationForResume(
                    _operationStateRepository,
                    selfUpdateAppliedOnReload,
                    out PackageOperationRecoveryRecord recoveryRecord))
            {
                return false;
            }

            if (!CanReuseSavedTargets(recoveryRecord, currentRegistryFingerprint))
            {
                ReportUnsafeSavedTargetReuse(recoveryRecord.RegistryFingerprint);
                return false;
            }

            PackageOperationRecoveryStep[] restartedSteps = recoveryRecord.Steps
                .Select(step => new PackageOperationRecoveryStep(
                    step.PackageId,
                    step.DisplayName,
                    step.Channel,
                    step.TargetUrl,
                    step.IsDependency,
                    step.PrerequisitePackageIds,
                    step.RootPackageIds,
                    step.RootPaths,
                    step.DependencyReason,
                    PackageInstallProgressItemState.Pending,
                    string.Empty,
                    step.DetectedCurrentSource,
                    step.DetectedCurrentVersion,
                    step.DetectedCurrentIdentity,
                    step.RequestedChannel))
                .ToArray();
            PackageOperationRecoveryRecord restartedRecord = new PackageOperationRecoveryRecord(
                Guid.NewGuid().ToString("N"),
                recoveryRecord.OperationName,
                recoveryRecord.RegistryFingerprint,
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.Ticks,
                restartedSteps,
                recoveryRecord.Messages,
                recoveryRecord.RootRequests);

            if (!_operationStateRepository.Save(restartedRecord, out string saveError))
            {
                _lastErrorMessage = saveError;
                PackageInstallerLog.Install.Warning(saveError);
                NotifyStateChanged();
                return false;
            }

            RestoreOperation(restartedRecord);
            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            bool completedDuringReconciliation = CompleteRecoveredOperationWithoutRequestIfNeeded();
            if (!completedDuringReconciliation)
            {
                SavePendingOperationState();
            }
            NotifyStateChanged();
            return IsBusy || completedDuringReconciliation;
        }

        internal static bool CanReuseSavedTargets(
            PackageOperationRecoveryRecord recoveryRecord,
            string currentRegistryFingerprint)
        {
            return recoveryRecord != null &&
                   !string.IsNullOrWhiteSpace(recoveryRecord.RegistryFingerprint) &&
                   !string.IsNullOrWhiteSpace(currentRegistryFingerprint) &&
                   string.Equals(
                       recoveryRecord.RegistryFingerprint,
                       currentRegistryFingerprint,
                       StringComparison.Ordinal);
        }

        private void ReportUnsafeSavedTargetReuse(string savedRegistryFingerprint)
        {
            _lastErrorMessage = string.IsNullOrWhiteSpace(savedRegistryFingerprint)
                ? "The saved operation has no registry fingerprint and must be replanned before its exact URLs can be reused."
                : "The registry fingerprint changed; the saved operation must be replanned before its exact URLs can be reused.";
            PackageInstallerLog.Install.Warning(_lastErrorMessage);
            PackageInstallerActivityService.Record(
                "Packages",
                PackageInstallerActivitySeverity.Warning,
                _lastErrorMessage,
                retryKind: PackageInstallerRetryKind.ResumeOperation);
            NotifyStateChanged();
        }

        private bool CompleteRecoveredOperationWithoutRequestIfNeeded()
        {
            if (IsBusy || !HasProgress)
            {
                return false;
            }

            ClearSavedOperationState(_operationStateRepository);
            QueueCompleted?.Invoke();
            return true;
        }

        public bool DiscardSavedOperation()
        {
            if (IsBusy)
            {
                return false;
            }

            bool hadSavedOperation = TryLoadSavedOperation(
                _operationStateRepository,
                out _);
            ClearSavedOperationState(_operationStateRepository);
            PackageInstallerSelfUpdateState.AcknowledgeApplied();
            NotifyStateChanged();
            return hadSavedOperation;
        }

        public bool CancelCurrentOperation()
        {
            if (!IsBusy)
            {
                return false;
            }

            if (_cancelRequested)
            {
                return true;
            }

            _cancelRequested = true;
            _operationCanceled = true;
            CancelQueuedInstalls("Canceled before starting.");
            ClearSavedOperationState(_operationStateRepository);

            if (_currentRequest == null && _currentRemoveRequest == null)
            {
                EditorApplication.update -= Update;
                State = PackageInstallRequestState.Idle;
                SetOperationCompleteSummary();
                _cancelRequested = false;
                QueueCompleted?.Invoke();
                NotifyStateChanged();
                return true;
            }

            _lastStatusMessage = State == PackageInstallRequestState.Removing
                ? "Cancel requested. Waiting for current remove operation to finish..."
                : "Cancel requested. Waiting for current package operation to finish...";
            NotifyStateChanged();
            return true;
        }

        public bool Install(PackageDefinition packageDefinition)
        {
            return Install(packageDefinition, PackageChannel.Stable);
        }

        public bool Install(PackageDefinition packageDefinition, PackageChannel channel)
        {
            string operationName = packageDefinition != null
                ? "Install " + packageDefinition.DisplayName
                : "Install Package";

            return Install(packageDefinition, channel, operationName);
        }

        public bool Install(PackageDefinition packageDefinition, PackageChannel channel, string operationName)
        {
            if (packageDefinition == null)
            {
                PackageInstallerLog.Install.Error("Cannot install a null package definition.");
                return false;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + packageDefinition.DisplayName + " because another package operation is already running.";
                PackageInstallerLog.Install.Warning(_lastErrorMessage);
                NotifyStateChanged();
                return false;
            }

            string targetUrl = packageDefinition.GetUrl(channel);
            PackageDependencyInstallStep step = new PackageDependencyInstallStep(
                packageDefinition,
                channel,
                isDependency: false,
                targetUrl: targetUrl,
                rootPackageIds: new[] { packageDefinition.PackageId },
                rootPaths: new[] { packageDefinition.DisplayName });
            PackageDependencyInstallPlan plan = PackageDependencyInstallPlan.Success(
                new[] { step },
                Array.Empty<string>());

            return InstallPlan(
                plan,
                string.IsNullOrWhiteSpace(operationName)
                    ? "Install " + packageDefinition.DisplayName
                    : operationName);
        }

        private bool QueueInstall(PackageDefinition packageDefinition, PackageChannel channel)
        {
            return QueueInstall(new PackageDependencyInstallStep(
                packageDefinition,
                channel,
                isDependency: false,
                targetUrl: packageDefinition.GetUrl(channel),
                rootPackageIds: new[] { packageDefinition.PackageId },
                rootPaths: new[] { packageDefinition.DisplayName }));
        }

        private bool QueueInstall(PackageDependencyInstallStep step)
        {
            PackageDefinition packageDefinition = step != null ? step.PackageDefinition : null;
            string packageUrl = step != null ? step.TargetUrl : string.Empty;

            if (packageDefinition == null || string.IsNullOrWhiteSpace(packageUrl))
            {
                string displayName = packageDefinition != null ? packageDefinition.DisplayName : "Package";
                string message = displayName + " has no package URL to install.";
                MarkProgressItem(packageDefinition, PackageInstallProgressItemState.Failed, message);
                PackageInstallerLog.Install.Warning(message);
                return false;
            }

            if (_queuedOrInstallingPackageIds.Contains(packageDefinition.PackageId))
            {
                string message = packageDefinition.DisplayName + " is already queued or installing.";
                MarkProgressItem(packageDefinition, PackageInstallProgressItemState.Skipped, message);
                PackageInstallerLog.Install.Info(message);
                return false;
            }

            QueuedPackageInstall install = new QueuedPackageInstall(step);
            _installQueue.Add(install);
            _operationInstallsByPackageId[packageDefinition.PackageId] = install;
            _queuedOrInstallingPackageIds.Add(packageDefinition.PackageId);
            _lastStatusMessage = "Queued " + packageDefinition.DisplayName + ".";
            PackageInstallerLog.Install.Info(
                "Queued " + packageDefinition.DisplayName + " from " + packageUrl + " (" + step.Channel + ").");

            return true;
        }

        public void InstallMany(IEnumerable<PackageDefinition> packageDefinitions)
        {
            InstallMany(packageDefinitions, PackageChannel.Stable);
        }

        public void InstallMany(IEnumerable<PackageDefinition> packageDefinitions, PackageChannel channel)
        {
            InstallMany(packageDefinitions, _ => channel);
        }

        public void InstallMany(IEnumerable<PackageDefinition> packageDefinitions, Func<PackageDefinition, PackageChannel> channelSelector)
        {
            InstallMany(packageDefinitions, channelSelector, "Install Packages");
        }

        public void InstallMany(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            string operationName)
        {
            InstallMany(packageDefinitions, channelSelector, operationName, Array.Empty<string>());
        }
    }
}
