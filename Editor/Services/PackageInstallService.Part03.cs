using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallService
    {


        private void UpdateRemoveRequest()
        {
            if (_currentRemoveRequest == null || !_currentRemoveRequest.IsCompleted)
            {
                return;
            }

            PackageDefinition packageDefinition = _currentRemovePackage;
            string packageName = packageDefinition != null ? packageDefinition.DisplayName : "package";

            if (_currentRemoveRequest.IsSuccess)
            {
                string message = "Removed " + packageName + ".";
                CompleteCurrentRemoveRequest(true, message);
                PackageInstallerLog.Install.Info(message);
                return;
            }

            string errorMessage = string.IsNullOrWhiteSpace(_currentRemoveRequest.ErrorMessage)
                ? "Package Manager returned an unknown error."
                : _currentRemoveRequest.ErrorMessage;

            CompleteCurrentRemoveRequest(false, errorMessage);
            PackageInstallerLog.Install.Error("Failed to remove " + packageName + ": " + errorMessage);
        }

        private void CompleteCurrentRequest(bool success, string message)
        {
            PackageDefinition completedPackage = _currentInstall != null ? _currentInstall.PackageDefinition : null;

            ReconcileSelfUpdateAfterInstallForTests(completedPackage, success);

            if (!success)
            {
                PackageOperationAutoResumeState.DisqualifyOperation(_currentOperationId);
            }

            if (completedPackage != null)
            {
                _queuedOrInstallingPackageIds.Remove(completedPackage.PackageId);
                MarkProgressItem(
                    completedPackage,
                    success ? PackageInstallProgressItemState.Completed : PackageInstallProgressItemState.Failed,
                    message);
            }

            _currentRequest = null;
            _currentInstall = null;
            State = PackageInstallRequestState.Idle;

            InstallCompleted?.Invoke(completedPackage, success, message);
            if (!success)
            {
                BlockInstallsWithFailedPrerequisites();
            }

            if (!_cancelRequested)
            {
                StartNextRequestIfNeeded();
            }

            if (_currentRequest == null && _installQueue.Count == 0)
            {
                EditorApplication.update -= Update;
                SetOperationCompleteSummary();
                _cancelRequested = false;
                ClearSavedOperationState(_operationStateRepository);
                QueueCompleted?.Invoke();
            }
            else
            {
                SavePendingOperationState();
            }

            NotifyStateChanged();
        }

        private void CompleteCurrentRemoveRequest(bool success, string message)
        {
            PackageDefinition completedPackage = _currentRemovePackage;

            if (completedPackage != null)
            {
                MarkProgressItem(
                    completedPackage,
                    success ? PackageInstallProgressItemState.Completed : PackageInstallProgressItemState.Failed,
                    message);
            }

            _currentRemoveRequest = null;
            _currentRemovePackage = null;
            State = PackageInstallRequestState.Idle;
            EditorApplication.update -= Update;
            SetOperationCompleteSummary();
            _cancelRequested = false;
            ClearSavedOperationState(_operationStateRepository);
            QueueCompleted?.Invoke();
            NotifyStateChanged();
        }

        private void BeginOperation(
            string operationName,
            IEnumerable<PackageDefinition> packages,
            IEnumerable<string> operationMessages = null)
        {
            PackageDependencyInstallStep[] steps = (packages ?? Array.Empty<PackageDefinition>())
                .Where(package => package != null)
                .Select(package => new PackageDependencyInstallStep(
                    package,
                    PackageChannel.Stable,
                    isDependency: false,
                    targetUrl: package.GetUrl(PackageChannel.Stable),
                    rootPackageIds: new[] { package.PackageId },
                    rootPaths: new[] { package.DisplayName }))
                .ToArray();
            BeginOperation(
                operationName,
                PackageDependencyInstallPlan.Success(steps, operationMessages));
        }

        private void BeginOperation(
            string operationName,
            PackageDependencyInstallPlan plan)
        {
            _currentOperationName = operationName ?? string.Empty;
            _currentOperationId = plan != null ? plan.OperationId : Guid.NewGuid().ToString("N");
            _currentRegistryFingerprint = plan != null ? plan.RegistryFingerprint : string.Empty;
            _currentOperationCreatedAtUtcTicks = plan != null
                ? plan.CreatedAtUtcTicks
                : DateTime.UtcNow.Ticks;
            _lastStatusMessage = "Queued " + _currentOperationName + ".";
            _lastErrorMessage = string.Empty;
            _cancelRequested = false;
            _operationCanceled = false;
            _completionActivityRecorded = false;
            _completedSteps = 0;
            _successfulSteps = 0;
            _failedSteps = 0;
            _skippedSteps = 0;
            _blockedSteps = 0;
            _canceledSteps = 0;
            _totalSteps = 0;
            _installQueue.Clear();
            _queuedOrInstallingPackageIds.Clear();
            _progressItems.Clear();
            _operationMessages.Clear();
            _progressItemsByPackageId.Clear();
            _operationInstallsByPackageId.Clear();
            _currentRootRequests.Clear();

            foreach (PackageOperationRootRequest rootRequest in plan != null
                         ? plan.RootRequests
                         : Array.Empty<PackageOperationRootRequest>())
            {
                if (rootRequest != null && !string.IsNullOrWhiteSpace(rootRequest.PackageId))
                {
                    _currentRootRequests.Add(new PackageOperationRootRequest(
                        rootRequest.PackageId,
                        rootRequest.Channel));
                }
            }

            foreach (string message in plan != null
                         ? plan.Messages
                         : Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    _operationMessages.Add(message.Trim());
                }
            }

            foreach (PackageDependencyInstallStep step in plan != null
                         ? plan.Steps
                         : Array.Empty<PackageDependencyInstallStep>())
            {
                PackageDefinition packageDefinition = step != null ? step.PackageDefinition : null;
                if (packageDefinition == null)
                {
                    continue;
                }

                PackageInstallProgressItem item = new PackageInstallProgressItem(
                    packageDefinition.PackageId,
                    packageDefinition.DisplayName);

                _progressItems.Add(item);
                _progressItemsByPackageId[packageDefinition.PackageId] = item;
            }

            _totalSteps = _progressItems.Count;
            PackageOperationAutoResumeState.TrackActiveOperation(
                _currentOperationId,
                _currentRegistryFingerprint,
                plan != null && (plan.IsMultiStep || plan.IsBulk));
        }

        private void MarkProgressItem(
            PackageDefinition packageDefinition,
            PackageInstallProgressItemState state,
            string message)
        {
            if (packageDefinition == null)
            {
                return;
            }

            if (!_progressItemsByPackageId.TryGetValue(packageDefinition.PackageId, out PackageInstallProgressItem item))
            {
                item = new PackageInstallProgressItem(packageDefinition.PackageId, packageDefinition.DisplayName);
                _progressItems.Add(item);
                _progressItemsByPackageId[packageDefinition.PackageId] = item;
                _totalSteps = _progressItems.Count;
            }

            PackageInstallProgressItemState previousState = item.State;
            item.State = state;
            item.Message = message ?? string.Empty;

            if (IsTerminalState(state) && !IsTerminalState(previousState))
            {
                _completedSteps++;

                if (state == PackageInstallProgressItemState.Completed)
                {
                    _successfulSteps++;
                }
                else if (state == PackageInstallProgressItemState.Failed)
                {
                    _failedSteps++;
                    _lastErrorMessage = message ?? string.Empty;
                }
                else if (state == PackageInstallProgressItemState.Blocked)
                {
                    _blockedSteps++;
                }
                else if (state == PackageInstallProgressItemState.Canceled)
                {
                    _canceledSteps++;
                }
                else
                {
                    _skippedSteps++;
                }
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                _lastStatusMessage = message;
            }
        }

        private void CompleteOperationIfIdle()
        {
            if (_currentRequest != null || _currentRemoveRequest != null || _installQueue.Count > 0)
            {
                return;
            }

            SetOperationCompleteSummary();
        }

        private void SetOperationCompleteSummary()
        {
            if (!HasProgress)
            {
                return;
            }

            if (_operationCanceled)
            {
                _lastStatusMessage = _currentOperationName + " canceled" + FormatOperationOutcomeSuffix() + ".";
                RecordCompletionActivity(PackageInstallerActivitySeverity.Warning);
                return;
            }

            if (_failedSteps > 0 || _blockedSteps > 0)
            {
                _lastStatusMessage = _currentOperationName + " finished" +
                                     FormatOperationOutcomeSuffix() + ".";
                RecordCompletionActivity(PackageInstallerActivitySeverity.Error);
                return;
            }

            _lastStatusMessage = _currentOperationName + " completed successfully" +
                                 FormatSkippedSummarySuffix() + ".";
            RecordCompletionActivity(PackageInstallerActivitySeverity.Success);
        }

        private void RecordCompletionActivity(PackageInstallerActivitySeverity severity)
        {
            if (_completionActivityRecorded || string.IsNullOrWhiteSpace(_lastStatusMessage))
            {
                return;
            }

            _completionActivityRecorded = true;
            _terminalOperationSnapshot = CreateTerminalOperationSnapshot(severity);
            List<string> details = new List<string>(_operationMessages);
            details.AddRange(_progressItems
                .Where(item => item != null && IsTerminalState(item.State))
                .Select(item =>
                    item.State + ": " +
                    (string.IsNullOrWhiteSpace(item.Message)
                        ? item.DisplayName
                        : item.Message)));
            PackageInstallerActivityService.Record(
                "Packages",
                severity,
                _lastStatusMessage,
                details.Count > 0 ? string.Join("\n", details.ToArray()) : string.Empty,
                packageId: _terminalOperationSnapshot.RestartRoots.Count == 1
                    ? _terminalOperationSnapshot.RestartRoots[0].PackageId
                    : string.Empty,
                retryKind: _terminalOperationSnapshot.CanRestart
                    ? PackageInstallerRetryKind.RestartOperation
                    : PackageInstallerRetryKind.None);
        }

        private PackageOperationTerminalSnapshot CreateTerminalOperationSnapshot(
            PackageInstallerActivitySeverity severity)
        {
            PackageOperationTerminalOutcome outcome = _operationCanceled
                ? PackageOperationTerminalOutcome.Canceled
                : severity == PackageInstallerActivitySeverity.Error
                    ? PackageOperationTerminalOutcome.Failed
                    : PackageOperationTerminalOutcome.Succeeded;
            List<PackageOperationStepSnapshot> stepSnapshots = new List<PackageOperationStepSnapshot>();

            foreach (PackageInstallProgressItem progress in _progressItems.Where(item => item != null))
            {
                _operationInstallsByPackageId.TryGetValue(
                    progress.PackageId,
                    out QueuedPackageInstall install);
                stepSnapshots.Add(new PackageOperationStepSnapshot(
                    progress.PackageId,
                    progress.DisplayName,
                    install != null ? install.Channel : PackageChannel.Stable,
                    install != null ? install.Url : string.Empty,
                    install != null && install.IsDependency,
                    install != null ? install.RootPackageIds : Array.Empty<string>(),
                    progress.State,
                    progress.Message,
                    install != null ? install.Step.RequestedChannel : PackageChannel.Stable));
            }

            List<string> affectedRootIds = new List<string>();
            HashSet<string> seenRootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PackageOperationStepSnapshot step in stepSnapshots.Where(IsRetryableTerminalStep))
            {
                foreach (string rootId in step.RootPackageIds)
                {
                    if (seenRootIds.Add(rootId))
                    {
                        affectedRootIds.Add(rootId);
                    }
                }
            }

            List<PackageOperationRootRequest> restartRoots = new List<PackageOperationRootRequest>();
            foreach (string rootId in affectedRootIds)
            {
                PackageOperationRootRequest requestedRoot = _currentRootRequests.FirstOrDefault(
                    root => string.Equals(
                        root.PackageId,
                        rootId,
                        StringComparison.OrdinalIgnoreCase));
                PackageChannel channel = requestedRoot != null
                    ? requestedRoot.Channel
                    : _operationInstallsByPackageId.TryGetValue(
                        rootId,
                        out QueuedPackageInstall rootInstall)
                        ? rootInstall.Step.RequestedChannel
                        : _operationInstallsByPackageId.Values
                            .Where(install => install != null &&
                                              install.RootPackageIds.Contains(
                                                  rootId,
                                                  StringComparer.OrdinalIgnoreCase))
                            .Select(install => install.Step.RequestedChannel)
                            .DefaultIfEmpty(PackageChannel.Stable)
                            .First();
                restartRoots.Add(new PackageOperationRootRequest(rootId, channel));
            }

            return new PackageOperationTerminalSnapshot(
                _currentOperationId,
                _currentOperationName,
                outcome,
                _lastStatusMessage,
                _lastErrorMessage,
                restartRoots,
                stepSnapshots,
                _operationMessages,
                DateTime.UtcNow);
        }

        private static bool IsRetryableTerminalStep(PackageOperationStepSnapshot step)
        {
            return step != null &&
                   (step.State == PackageInstallProgressItemState.Failed ||
                    step.State == PackageInstallProgressItemState.Blocked ||
                    step.State == PackageInstallProgressItemState.Canceled);
        }
    }
}
