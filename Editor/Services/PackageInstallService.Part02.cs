using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallService
    {


        public void InstallMany(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            string operationName,
            IEnumerable<string> operationMessages)
        {
            if (packageDefinitions == null)
            {
                return;
            }

            PackageDefinition[] packages = packageDefinitions
                .Where(packageDefinition => packageDefinition != null)
                .GroupBy(packageDefinition => packageDefinition.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(packageDefinition =>
                    PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId) ? 1 : 0)
                .ToArray();

            if (packages.Length == 0)
            {
                return;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + operationName + " because another package operation is already running.";
                PackageInstallerLog.Install.Warning(_lastErrorMessage);
                NotifyStateChanged();
                return;
            }

            PackageDependencyInstallStep[] steps = packages
                .Select(packageDefinition =>
                {
                    PackageChannel channel = channelSelector != null
                        ? channelSelector(packageDefinition)
                        : PackageChannel.Stable;
                    return new PackageDependencyInstallStep(
                        packageDefinition,
                        channel,
                        isDependency: false,
                        targetUrl: packageDefinition.GetUrl(channel),
                        rootPackageIds: new[] { packageDefinition.PackageId },
                        rootPaths: new[] { packageDefinition.DisplayName });
                })
                .ToArray();

            InstallPlan(
                PackageDependencyInstallPlan.Success(steps, operationMessages),
                string.IsNullOrWhiteSpace(operationName) ? "Install Packages" : operationName);
        }

        internal bool InstallPlan(PackageDependencyInstallPlan plan, string operationName)
        {
            if (plan == null || !plan.IsValid || plan.Steps.Count == 0)
            {
                return false;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + operationName +
                                    " because another package operation is already running.";
                PackageInstallerLog.Install.Warning(_lastErrorMessage);
                NotifyStateChanged();
                return false;
            }

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Install Packages" : operationName,
                plan);

            bool queuedAny = false;
            foreach (PackageDependencyInstallStep step in plan.Steps)
            {
                queuedAny |= QueueInstall(step);
            }

            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            SavePendingOperationState();
            NotifyStateChanged();
            return queuedAny;
        }

        internal void RecordCompletedOperation(
            string operationName,
            string summaryMessage,
            IEnumerable<string> operationMessages)
        {
            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Package Operation" : operationName,
                Array.Empty<PackageDefinition>(),
                operationMessages);

            _lastStatusMessage = summaryMessage ?? string.Empty;
            _lastErrorMessage = string.Empty;
            RecordCompletionActivity(PackageInstallerActivitySeverity.Success);
            NotifyStateChanged();
        }

        internal void QueuePendingOperationForTests(
            string operationName,
            IEnumerable<PackageDefinition> packageDefinitions)
        {
            PackageDefinition[] packages = (packageDefinitions ?? Array.Empty<PackageDefinition>())
                .Where(packageDefinition => packageDefinition != null)
                .ToArray();

            PackageDependencyInstallStep[] steps = packages.Select(packageDefinition =>
                new PackageDependencyInstallStep(
                    packageDefinition,
                    PackageChannel.Stable,
                    isDependency: false,
                    targetUrl: packageDefinition.GetUrl(PackageChannel.Stable),
                    rootPackageIds: new[] { packageDefinition.PackageId },
                    rootPaths: new[] { packageDefinition.DisplayName })).ToArray();
            PackageDependencyInstallPlan plan = PackageDependencyInstallPlan.Success(
                steps,
                Array.Empty<string>());

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Package Operation" : operationName,
                plan);

            foreach (PackageDependencyInstallStep step in steps)
            {
                QueueInstall(step);
            }

            NotifyStateChanged();
        }

        internal static PackageDefinition[] OrderSelfUpdateLastForTests(
            IEnumerable<PackageDefinition> packageDefinitions)
        {
            return (packageDefinitions ?? Array.Empty<PackageDefinition>())
                .Where(packageDefinition => packageDefinition != null)
                .OrderBy(packageDefinition =>
                    PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId) ? 1 : 0)
                .ToArray();
        }

        internal static PackageDefinition[] FilterAppliedSelfUpdateForTests(
            IEnumerable<PackageDefinition> packageDefinitions)
        {
            return (packageDefinitions ?? Array.Empty<PackageDefinition>())
                .Where(packageDefinition =>
                    packageDefinition != null &&
                    !PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId))
                .ToArray();
        }

        internal void SavePendingOperationForTests()
        {
            SavePendingOperationState();
        }

        internal static string[] RestorePendingPackageIdsForTests(
            bool selfUpdateAppliedOnReload,
            out string operationName)
        {
            PackageOperationStateRepository repository = new PackageOperationStateRepository();
            if (!TryLoadSavedOperation(repository, out PackageOperationRecoveryRecord record))
            {
                operationName = string.Empty;
                return Array.Empty<string>();
            }

            operationName = record.OperationName;
            IEnumerable<PackageOperationRecoveryStep> steps = record.Steps;
            if (selfUpdateAppliedOnReload)
            {
                steps = FilterAppliedSelfUpdate(steps);
            }

            return steps
                .Where(step => step != null && IsResumableState(step.State))
                .Select(step => step.PackageId)
                .ToArray();
        }

        internal static string[] PreparePendingPackageIdsForResumeForTests(
            bool selfUpdateAppliedOnReload,
            out string operationName)
        {
            PackageOperationStateRepository repository = new PackageOperationStateRepository();
            if (!TryPrepareSavedOperationForResume(
                    repository,
                    selfUpdateAppliedOnReload,
                    out PackageOperationRecoveryRecord record))
            {
                operationName = string.Empty;
                return Array.Empty<string>();
            }

            operationName = record.OperationName;
            return record.Steps
                .Where(step => step != null && IsResumableState(step.State))
                .Select(step => step.PackageId)
                .ToArray();
        }

        internal static void ClearPendingOperationForTests()
        {
            ClearSavedOperationState(new PackageOperationStateRepository());
        }

        internal static void ReconcileSelfUpdateAfterInstallForTests(
            PackageDefinition completedPackage,
            bool success)
        {
            if (!success &&
                completedPackage != null &&
                PackageInstallerRuntimeIdentity.IsSelf(completedPackage.PackageId))
            {
                PackageInstallerSelfUpdateState.MarkInstallFailed();
            }
        }

        public bool Remove(PackageDefinition packageDefinition)
        {
            string operationName = packageDefinition != null
                ? "Remove " + packageDefinition.DisplayName
                : "Remove Package";

            return Remove(packageDefinition, operationName);
        }

        public bool Remove(PackageDefinition packageDefinition, string operationName)
        {
            if (packageDefinition == null)
            {
                PackageInstallerLog.Install.Error("Cannot remove a null package definition.");
                return false;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + packageDefinition.DisplayName + " removal because another package operation is already running.";
                PackageInstallerLog.Install.Warning(_lastErrorMessage);
                NotifyStateChanged();
                return false;
            }

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Remove " + packageDefinition.DisplayName : operationName,
                new[] { packageDefinition });

            _currentRemovePackage = packageDefinition;
            State = PackageInstallRequestState.Removing;
            MarkProgressItem(
                packageDefinition,
                PackageInstallProgressItemState.Active,
                "Removing " + packageDefinition.DisplayName + "...");
            _lastStatusMessage = "Removing " + packageDefinition.DisplayName + "...";
            ClearSavedOperationState(_operationStateRepository);

            try
            {
                _currentRemoveRequest = _packageClient.Remove(packageDefinition.PackageId);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                PackageInstallerLog.Install.Info("Removing " + packageDefinition.DisplayName + " (" + packageDefinition.PackageId + ").");
            }
            catch (Exception exception)
            {
                PackageInstallerLog.Install.Error("Failed to start remove for " + packageDefinition.DisplayName + ": " + exception.Message);
                CompleteCurrentRemoveRequest(false, exception.Message);
            }

            NotifyStateChanged();
            return _currentRemoveRequest != null;
        }

        public bool IsQueuedOrInstalling(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && _queuedOrInstallingPackageIds.Contains(packageId);
        }

        public void Dispose()
        {
            EditorApplication.update -= Update;
            PackageOperationAutoResumeState.DetachOperation(_currentOperationId);
        }

        internal void UpdateForTests()
        {
            Update();
        }

        private void StartNextRequestIfNeeded()
        {
            if (_currentRequest != null || _currentRemoveRequest != null || _cancelRequested)
            {
                return;
            }

            BlockInstallsWithFailedPrerequisites();

            if (_installQueue.Count == 0)
            {
                return;
            }

            _currentInstall = _installQueue
                .Where(CanStartInstall)
                .OrderBy(install => PackageInstallerRuntimeIdentity.IsSelf(
                    install.PackageDefinition.PackageId) ? 1 : 0)
                .ThenBy(install => _installQueue.IndexOf(install))
                .FirstOrDefault();

            if (_currentInstall == null)
            {
                BlockUnresolvableInstalls();
                return;
            }

            _installQueue.Remove(_currentInstall);
            State = PackageInstallRequestState.Installing;
            MarkProgressItem(
                _currentInstall.PackageDefinition,
                PackageInstallProgressItemState.Active,
                "Installing " + _currentInstall.PackageDefinition.DisplayName + "...");
            _lastStatusMessage = "Installing " + _currentInstall.PackageDefinition.DisplayName + "...";

            try
            {
                if (PackageInstallerRuntimeIdentity.IsSelf(_currentInstall.PackageDefinition.PackageId))
                {
                    PackageInstallerSelfUpdateState.Begin(_currentInstall.Url);
                }

                _currentRequest = _packageClient.Add(_currentInstall.Url);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                SavePendingOperationState();

                PackageInstallerLog.Install.Info("Installing " + _currentInstall.PackageDefinition.DisplayName + " using " + _currentInstall.Url + " (" + _currentInstall.Channel + ").");
            }
            catch (Exception exception)
            {
                PackageInstallerLog.Install.Error("Failed to start install for " + _currentInstall.PackageDefinition.DisplayName + ": " + exception.Message);
                CompleteCurrentRequest(false, exception.Message);
            }
        }

        private void Update()
        {
            if (_currentRemoveRequest != null)
            {
                UpdateRemoveRequest();
                return;
            }

            if (_currentRequest == null || !_currentRequest.IsCompleted)
            {
                return;
            }

            if (_currentRequest.IsSuccess)
            {
                PackageDefinition packageDefinition = _currentInstall.PackageDefinition;
                string packageName = !string.IsNullOrWhiteSpace(_currentRequest.PackageName)
                    ? _currentRequest.PackageName
                    : packageDefinition.PackageId;
                string version = !string.IsNullOrWhiteSpace(_currentRequest.PackageVersion)
                    ? _currentRequest.PackageVersion
                    : "unknown";
                string message = "Installed " + packageDefinition.DisplayName + " (" + packageName + "@" + version + ") from " + _currentInstall.Channel + ".";

                if (PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId))
                {
                    PackageInstallerSelfUpdateState.MarkResolved(version);
                    message += " Waiting for Unity to load the updated installer assembly.";
                }

                CompleteCurrentRequest(true, message);
                PackageInstallerLog.Install.Info(message);
                return;
            }

            string errorMessage = string.IsNullOrWhiteSpace(_currentRequest.ErrorMessage)
                ? "Package Manager returned an unknown error."
                : _currentRequest.ErrorMessage;
            string failedPackageName = _currentInstall != null && _currentInstall.PackageDefinition != null
                ? _currentInstall.PackageDefinition.DisplayName
                : "package";

            CompleteCurrentRequest(false, errorMessage);
            PackageInstallerLog.Install.Error("Failed to install " + failedPackageName + ": " + errorMessage);
        }
    }
}
