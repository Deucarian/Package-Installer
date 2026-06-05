using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JorisHoef.PackageInstaller.Editor
{
    internal sealed class IntegrationInstaller : IDisposable
    {
        private const string LogPrefix = "[JorisHoef Package Installer]";
        private const string PendingIntegrationsKey = "JorisHoef.PackageInstaller.PendingIntegrations";

        private readonly PackageInstallService _packageInstallService;
        private readonly PackageDetectionService _packageDetectionService;
        private readonly ScriptingDefineService _scriptingDefineService;
        private readonly List<PackageDefinition> _pendingIntegrations = new List<PackageDefinition>();
        private readonly HashSet<string> _trackedPackageStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _trackedIntegrationStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PackageInstallProgressItem> _progressItems = new List<PackageInstallProgressItem>();
        private readonly Dictionary<string, PackageInstallProgressItem> _progressItemsByPackageId =
            new Dictionary<string, PackageInstallProgressItem>(StringComparer.OrdinalIgnoreCase);

        private string _currentOperationName = string.Empty;
        private string _currentPackageName = string.Empty;
        private string _lastStatusMessage = string.Empty;
        private string _lastErrorMessage = string.Empty;
        private int _completedSteps;
        private int _successfulSteps;
        private int _failedSteps;
        private int _skippedSteps;
        private int _totalSteps;

        public IntegrationInstaller(
            PackageInstallService packageInstallService,
            PackageDetectionService packageDetectionService,
            ScriptingDefineService scriptingDefineService)
        {
            _packageInstallService = packageInstallService ?? throw new ArgumentNullException(nameof(packageInstallService));
            _packageDetectionService = packageDetectionService ?? throw new ArgumentNullException(nameof(packageDetectionService));
            _scriptingDefineService = scriptingDefineService ?? throw new ArgumentNullException(nameof(scriptingDefineService));

            _packageInstallService.InstallCompleted += HandlePackageInstallCompleted;
            _packageInstallService.QueueCompleted += RefreshInstalledPackages;
            _packageDetectionService.RefreshCompleted += CompletePendingIntegrations;

            LoadPendingIntegrations();
        }

        public event Action StateChanged;

        public bool HasProgress => _totalSteps > 0 || !string.IsNullOrWhiteSpace(_currentOperationName);

        public bool IsBusy => HasProgress && _completedSteps < _totalSteps;

        public string CurrentOperationName => _currentOperationName;

        public string CurrentPackageName => _packageInstallService.CurrentPackage != null
            ? _packageInstallService.CurrentPackage.DisplayName
            : _currentPackageName;

        public int CompletedSteps => _completedSteps;

        public int TotalSteps => _totalSteps;

        public int SuccessfulSteps => _successfulSteps;

        public int FailedSteps => _failedSteps;

        public int SkippedSteps => _skippedSteps;

        public string LastStatusMessage => _lastStatusMessage;

        public string LastErrorMessage => _lastErrorMessage;

        public IReadOnlyList<PackageInstallProgressItem> ProgressItems => _progressItems;

        public void Dispose()
        {
            _packageInstallService.InstallCompleted -= HandlePackageInstallCompleted;
            _packageInstallService.QueueCompleted -= RefreshInstalledPackages;
            _packageDetectionService.RefreshCompleted -= CompletePendingIntegrations;
        }

        public void InstallPackage(PackageDefinition packageDefinition)
        {
            InstallPackage(packageDefinition, PackageChannel.Stable);
        }

        public void InstallPackage(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                return;
            }

            if (!packageDefinition.HasPackageReference)
            {
                Debug.LogWarning(LogPrefix + " " + packageDefinition.DisplayName + " is not a standalone installable package.");
                return;
            }

            if (_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                Debug.Log(LogPrefix + " " + packageDefinition.DisplayName + " is already installed.");
                return;
            }

            _packageInstallService.Install(
                packageDefinition,
                channel,
                "Install " + packageDefinition.DisplayName);
        }

        public void InstallIntegration(PackageDefinition integrationDefinition)
        {
            InstallIntegration(integrationDefinition, null);
        }

        public void InstallIntegration(PackageDefinition integrationDefinition, Func<PackageDefinition, PackageChannel> channelSelector)
        {
            if (integrationDefinition == null)
            {
                return;
            }

            if (ArePackageDependenciesInstalled(integrationDefinition))
            {
                BeginOperation(
                    "Install " + integrationDefinition.DisplayName,
                    Array.Empty<PackageDefinition>(),
                    new[] { integrationDefinition });
                bool enabled = EnableIntegrationSymbols(integrationDefinition);
                MarkIntegrationStep(
                    integrationDefinition,
                    enabled,
                    enabled
                        ? "Enabled " + integrationDefinition.DisplayName + "."
                        : "Could not enable " + integrationDefinition.DisplayName + ".");
                CompleteOperationIfFinished();
                return;
            }

            QueueIntegrationUntilDependenciesAreDetected(integrationDefinition);
            PackageDefinition[] missingDependencies = GetMissingInstallableDependencies(integrationDefinition);
            BeginOperation(
                "Install " + integrationDefinition.DisplayName,
                missingDependencies,
                new[] { integrationDefinition });

            if (missingDependencies.Length == 0 && !_packageInstallService.IsBusy)
            {
                RemovePendingIntegration(integrationDefinition);
                string message = "Cannot enable " + integrationDefinition.DisplayName +
                                 " because its dependencies are not installed or installable from PackageRegistry.";
                MarkIntegrationStep(integrationDefinition, false, message);
                CompleteOperationIfFinished();
                Debug.LogWarning(LogPrefix + " " + message);
                return;
            }

            _packageInstallService.InstallMany(
                missingDependencies,
                channelSelector,
                _currentOperationName);

            if (!_packageInstallService.IsBusy)
            {
                RefreshInstalledPackages();
            }

            Debug.Log(LogPrefix + " Waiting to enable " + integrationDefinition.DisplayName +
                      " until required packages are installed and detected.");
            _lastStatusMessage = "Waiting to enable " + integrationDefinition.DisplayName +
                                 " until required packages are installed and detected.";
            NotifyStateChanged();
        }

        public void InstallAll()
        {
            InstallAll(null);
        }

        public void InstallAll(Func<PackageDefinition, PackageChannel> channelSelector)
        {
            PackageDefinition[] missingPackages = PackageRegistry.StandalonePackages
                .Where(package => !_packageDetectionService.IsInstalled(package.PackageId))
                .ToArray();
            PackageDefinition[] incompleteIntegrations = PackageRegistry.Integrations
                .Where(integration => !IsIntegrationComplete(integration))
                .ToArray();

            BeginOperation("Install All", missingPackages, incompleteIntegrations);

            foreach (PackageDefinition installedPackage in PackageRegistry.StandalonePackages
                         .Where(package => _packageDetectionService.IsInstalled(package.PackageId)))
            {
                MarkPackageStep(
                    installedPackage,
                    PackageInstallProgressItemState.Skipped,
                    installedPackage.DisplayName + " is already installed.");
            }

            if (missingPackages.Length == 0)
            {
                foreach (PackageDefinition integration in incompleteIntegrations)
                {
                    if (ArePackageDependenciesInstalled(integration))
                    {
                        bool enabled = EnableIntegrationSymbols(integration);
                        MarkIntegrationStep(
                            integration,
                            enabled,
                            enabled
                                ? "Enabled " + integration.DisplayName + "."
                                : "Could not enable " + integration.DisplayName + ".");
                    }
                    else
                    {
                        QueueIntegrationUntilDependenciesAreDetected(integration);
                    }
                }

                if (_pendingIntegrations.Count > 0)
                {
                    RefreshInstalledPackages();
                }

                CompleteOperationIfFinished();
                Debug.Log(LogPrefix + " Processed Install All.");
                return;
            }

            foreach (PackageDefinition integration in incompleteIntegrations)
            {
                QueueIntegrationUntilDependenciesAreDetected(integration);
            }

            _packageInstallService.InstallMany(
                missingPackages,
                channelSelector,
                _currentOperationName);

            if (!_packageInstallService.IsBusy)
            {
                RefreshInstalledPackages();
            }

            Debug.Log(LogPrefix + " Waiting to enable integrations until required packages are installed and detected.");
            _lastStatusMessage = "Waiting to enable integrations until required packages are installed and detected.";
            NotifyStateChanged();
        }

        public bool ArePackageDependenciesInstalled(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null)
            {
                return false;
            }

            return integrationDefinition.Dependencies.All(_packageDetectionService.IsInstalled);
        }

        public bool AreIntegrationSymbolsEnabled(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null)
            {
                return false;
            }

            return _scriptingDefineService.HasSymbols(
                _scriptingDefineService.SelectedBuildTargetGroup,
                integrationDefinition.ScriptingDefineSymbols);
        }

        public bool IsIntegrationComplete(PackageDefinition integrationDefinition)
        {
            return ArePackageDependenciesInstalled(integrationDefinition) && AreIntegrationSymbolsEnabled(integrationDefinition);
        }

        public bool HasPendingIntegration(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null)
            {
                return false;
            }

            return _pendingIntegrations.Any(pendingIntegration =>
                string.Equals(pendingIntegration.PackageId, integrationDefinition.PackageId, StringComparison.OrdinalIgnoreCase));
        }

        private PackageDefinition[] GetMissingInstallableDependencies(PackageDefinition integrationDefinition)
        {
            return PackageRegistry
                .GetInstallableDependencies(integrationDefinition)
                .Where(dependency => !_packageDetectionService.IsInstalled(dependency.PackageId))
                .ToArray();
        }

        private void QueueIntegrationUntilDependenciesAreDetected(PackageDefinition integrationDefinition)
        {
            if (HasPendingIntegration(integrationDefinition))
            {
                return;
            }

            _pendingIntegrations.Add(integrationDefinition);
            SavePendingIntegrations();
        }

        private void RemovePendingIntegration(PackageDefinition integrationDefinition)
        {
            _pendingIntegrations.RemoveAll(pendingIntegration =>
                string.Equals(pendingIntegration.PackageId, integrationDefinition.PackageId, StringComparison.OrdinalIgnoreCase));
            SavePendingIntegrations();
        }

        private void CompletePendingIntegrations()
        {
            if (_pendingIntegrations.Count == 0)
            {
                return;
            }

            foreach (PackageDefinition pendingIntegration in _pendingIntegrations.ToArray())
            {
                if (!ArePackageDependenciesInstalled(pendingIntegration))
                {
                    string message = "Cannot enable " + pendingIntegration.DisplayName +
                                     " because one or more required packages are still not installed.";
                    Debug.LogWarning(LogPrefix + " " + message);
                    MarkIntegrationStep(pendingIntegration, false, message);
                    RemovePendingIntegration(pendingIntegration);
                    continue;
                }

                bool enabled = EnableIntegrationSymbols(pendingIntegration);
                MarkIntegrationStep(
                    pendingIntegration,
                    enabled,
                    enabled
                        ? "Enabled " + pendingIntegration.DisplayName + "."
                        : "Could not enable " + pendingIntegration.DisplayName + ".");
                RemovePendingIntegration(pendingIntegration);
            }

            CompleteOperationIfFinished();
        }

        private bool EnableIntegrationSymbols(PackageDefinition integrationDefinition)
        {
            if (!ArePackageDependenciesInstalled(integrationDefinition))
            {
                Debug.LogWarning(LogPrefix + " Cannot enable " + integrationDefinition.DisplayName +
                                 " because one or more required packages are not installed.");
                return false;
            }

            _scriptingDefineService.AddSymbolsToSelectedBuildTargetGroup(integrationDefinition.ScriptingDefineSymbols);
            Debug.Log(LogPrefix + " Processed integration " + integrationDefinition.DisplayName + ".");
            return true;
        }

        private void RefreshInstalledPackages()
        {
            RestorePendingIntegrationsForCompletedPackageOperation();
            _packageDetectionService.Refresh();
        }

        private void RestorePendingIntegrationsForCompletedPackageOperation()
        {
            string operationName = _packageInstallService.CurrentOperationName;

            if (string.IsNullOrWhiteSpace(operationName))
            {
                return;
            }

            if (string.Equals(operationName, "Install All", StringComparison.OrdinalIgnoreCase))
            {
                foreach (PackageDefinition integration in PackageRegistry.Integrations)
                {
                    if (!HasPendingIntegration(integration) && !IsIntegrationComplete(integration))
                    {
                        QueueIntegrationUntilDependenciesAreDetected(integration);
                    }
                }

                return;
            }

            foreach (PackageDefinition integration in PackageRegistry.Integrations)
            {
                if (!string.Equals(operationName, "Install " + integration.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!HasPendingIntegration(integration) && !IsIntegrationComplete(integration))
                {
                    QueueIntegrationUntilDependenciesAreDetected(integration);
                }

                return;
            }
        }

        private void HandlePackageInstallCompleted(PackageDefinition packageDefinition, bool success, string message)
        {
            if (packageDefinition == null || !_trackedPackageStepIds.Remove(packageDefinition.PackageId))
            {
                return;
            }

            MarkPackageStep(
                packageDefinition,
                success ? PackageInstallProgressItemState.Completed : PackageInstallProgressItemState.Failed,
                message);
        }

        private void BeginOperation(
            string operationName,
            IEnumerable<PackageDefinition> packageSteps,
            IEnumerable<PackageDefinition> integrationSteps)
        {
            _currentOperationName = operationName ?? string.Empty;
            _currentPackageName = string.Empty;
            _lastStatusMessage = "Queued " + _currentOperationName + ".";
            _lastErrorMessage = string.Empty;
            _completedSteps = 0;
            _successfulSteps = 0;
            _failedSteps = 0;
            _skippedSteps = 0;
            _trackedPackageStepIds.Clear();
            _trackedIntegrationStepIds.Clear();
            _progressItems.Clear();
            _progressItemsByPackageId.Clear();

            foreach (PackageDefinition packageDefinition in packageSteps ?? Array.Empty<PackageDefinition>())
            {
                if (packageDefinition != null)
                {
                    _trackedPackageStepIds.Add(packageDefinition.PackageId);
                    AddProgressItem(packageDefinition);
                }
            }

            foreach (PackageDefinition integrationDefinition in integrationSteps ?? Array.Empty<PackageDefinition>())
            {
                if (integrationDefinition != null)
                {
                    _trackedIntegrationStepIds.Add(integrationDefinition.PackageId);
                    AddProgressItem(integrationDefinition);
                }
            }

            _totalSteps = _progressItems.Count;
            NotifyStateChanged();
        }

        private void MarkIntegrationStep(PackageDefinition integrationDefinition, bool success, string message)
        {
            if (integrationDefinition == null || !_trackedIntegrationStepIds.Remove(integrationDefinition.PackageId))
            {
                return;
            }

            _currentPackageName = integrationDefinition.DisplayName;
            MarkOperationStep(
                integrationDefinition,
                success ? PackageInstallProgressItemState.Completed : PackageInstallProgressItemState.Failed,
                message);
        }

        private void MarkPackageStep(
            PackageDefinition packageDefinition,
            PackageInstallProgressItemState state,
            string message)
        {
            if (packageDefinition == null)
            {
                return;
            }

            _trackedPackageStepIds.Remove(packageDefinition.PackageId);
            MarkOperationStep(packageDefinition, state, message);
        }

        private void MarkOperationStep(
            PackageDefinition packageDefinition,
            PackageInstallProgressItemState state,
            string message)
        {
            if (packageDefinition == null)
            {
                return;
            }

            PackageInstallProgressItem item = AddProgressItem(packageDefinition);
            PackageInstallProgressItemState previousState = item.State;
            item.State = state;
            item.Message = message ?? string.Empty;

            if (previousState == PackageInstallProgressItemState.Completed ||
                previousState == PackageInstallProgressItemState.Failed ||
                previousState == PackageInstallProgressItemState.Skipped)
            {
                return;
            }

            _completedSteps++;
            _lastStatusMessage = message ?? string.Empty;

            if (state == PackageInstallProgressItemState.Completed)
            {
                _successfulSteps++;
            }
            else if (state == PackageInstallProgressItemState.Failed)
            {
                _failedSteps++;
                _lastErrorMessage = message ?? string.Empty;
            }
            else if (state == PackageInstallProgressItemState.Skipped)
            {
                _skippedSteps++;
            }

            NotifyStateChanged();
        }

        private void CompleteOperationIfFinished()
        {
            if (!HasProgress || _completedSteps < _totalSteps)
            {
                return;
            }

            if (_failedSteps > 0)
            {
                _lastStatusMessage = _currentOperationName + " finished with " +
                                     _successfulSteps + " succeeded and " +
                                     _failedSteps + " failed" +
                                     FormatSkippedSummarySuffix() + ".";
            }
            else
            {
                _lastStatusMessage = _currentOperationName + " completed successfully" +
                                     FormatSkippedSummarySuffix() + ".";
            }

            NotifyStateChanged();
        }

        private PackageInstallProgressItem AddProgressItem(PackageDefinition packageDefinition)
        {
            if (_progressItemsByPackageId.TryGetValue(packageDefinition.PackageId, out PackageInstallProgressItem item))
            {
                return item;
            }

            item = new PackageInstallProgressItem(packageDefinition.PackageId, packageDefinition.DisplayName);
            _progressItems.Add(item);
            _progressItemsByPackageId[packageDefinition.PackageId] = item;
            _totalSteps = _progressItems.Count;
            return item;
        }

        private string FormatSkippedSummarySuffix()
        {
            return _skippedSteps > 0 ? " and " + _skippedSteps + " skipped" : string.Empty;
        }

        private void LoadPendingIntegrations()
        {
            string pendingIds = SessionState.GetString(PendingIntegrationsKey, string.Empty);

            if (string.IsNullOrWhiteSpace(pendingIds))
            {
                return;
            }

            foreach (string packageId in pendingIds.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (PackageRegistry.TryGetPackage(packageId, out PackageDefinition packageDefinition) &&
                    packageDefinition.IsIntegration &&
                    !HasPendingIntegration(packageDefinition))
                {
                    _pendingIntegrations.Add(packageDefinition);
                }
            }
        }

        private void SavePendingIntegrations()
        {
            if (_pendingIntegrations.Count == 0)
            {
                SessionState.SetString(PendingIntegrationsKey, string.Empty);
                return;
            }

            SessionState.SetString(
                PendingIntegrationsKey,
                string.Join("\n", _pendingIntegrations.Select(integration => integration.PackageId).ToArray()));
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
