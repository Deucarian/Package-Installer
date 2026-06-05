using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace JorisHoef.PackageInstaller.Editor
{
    internal enum PackageInstallRequestState
    {
        Idle,
        Installing
    }

    internal enum PackageInstallProgressItemState
    {
        Pending,
        Active,
        Completed,
        Failed
    }

    internal sealed class PackageInstallProgressItem
    {
        public PackageInstallProgressItem(string packageId, string displayName)
        {
            PackageId = packageId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            State = PackageInstallProgressItemState.Pending;
            Message = string.Empty;
        }

        public string PackageId { get; }

        public string DisplayName { get; }

        public PackageInstallProgressItemState State { get; internal set; }

        public string Message { get; internal set; }
    }

    internal sealed class PackageInstallService : IDisposable
    {
        private const string LogPrefix = "[JorisHoef Package Installer]";

        private readonly Queue<QueuedPackageInstall> _installQueue = new Queue<QueuedPackageInstall>();
        private readonly HashSet<string> _queuedOrInstallingPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PackageInstallProgressItem> _progressItems = new List<PackageInstallProgressItem>();
        private readonly Dictionary<string, PackageInstallProgressItem> _progressItemsByPackageId =
            new Dictionary<string, PackageInstallProgressItem>(StringComparer.OrdinalIgnoreCase);

        private AddRequest _currentRequest;
        private QueuedPackageInstall _currentInstall;
        private string _currentOperationName = string.Empty;
        private string _lastStatusMessage = string.Empty;
        private string _lastErrorMessage = string.Empty;
        private int _completedSteps;
        private int _successfulSteps;
        private int _failedSteps;
        private int _totalSteps;

        public event Action StateChanged;

        public event Action<PackageDefinition, bool, string> InstallCompleted;

        public event Action QueueCompleted;

        public PackageInstallRequestState State { get; private set; } = PackageInstallRequestState.Idle;

        public PackageDefinition CurrentPackage => _currentInstall != null ? _currentInstall.PackageDefinition : null;

        public PackageChannel CurrentChannel => _currentInstall != null ? _currentInstall.Channel : PackageChannel.Stable;

        public string CurrentUrl => _currentInstall != null ? _currentInstall.Url : string.Empty;

        public bool IsBusy => State == PackageInstallRequestState.Installing || _installQueue.Count > 0;

        public bool HasProgress => _totalSteps > 0 || !string.IsNullOrWhiteSpace(_currentOperationName);

        public string CurrentOperationName => _currentOperationName;

        public string CurrentPackageName => CurrentPackage != null ? CurrentPackage.DisplayName : string.Empty;

        public int CompletedSteps => _completedSteps;

        public int TotalSteps => _totalSteps;

        public int SuccessfulSteps => _successfulSteps;

        public int FailedSteps => _failedSteps;

        public string LastStatusMessage => _lastStatusMessage;

        public string LastErrorMessage => _lastErrorMessage;

        public IReadOnlyList<PackageInstallProgressItem> ProgressItems => _progressItems;

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
                Debug.LogError(LogPrefix + " Cannot install a null package definition.");
                return false;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + packageDefinition.DisplayName + " because another package operation is already running.";
                Debug.LogWarning(LogPrefix + " " + _lastErrorMessage);
                NotifyStateChanged();
                return false;
            }

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Install " + packageDefinition.DisplayName : operationName,
                new[] { packageDefinition });

            bool queued = QueueInstall(packageDefinition, channel);
            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            NotifyStateChanged();

            return queued;
        }

        private bool QueueInstall(PackageDefinition packageDefinition, PackageChannel channel)
        {
            string packageUrl = packageDefinition.GetUrl(channel);

            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                string message = packageDefinition.DisplayName + " has no package URL to install.";
                MarkProgressItem(packageDefinition, PackageInstallProgressItemState.Failed, message);
                Debug.LogWarning(LogPrefix + " " + message);
                return false;
            }

            if (_queuedOrInstallingPackageIds.Contains(packageDefinition.PackageId))
            {
                string message = packageDefinition.DisplayName + " is already queued or installing.";
                MarkProgressItem(packageDefinition, PackageInstallProgressItemState.Failed, message);
                Debug.Log(LogPrefix + " " + message);
                return false;
            }

            _installQueue.Enqueue(new QueuedPackageInstall(packageDefinition, channel, packageUrl));
            _queuedOrInstallingPackageIds.Add(packageDefinition.PackageId);
            _lastStatusMessage = "Queued " + packageDefinition.DisplayName + ".";
            Debug.Log(LogPrefix + " Queued " + packageDefinition.DisplayName + " from " + packageUrl + " (" + channel + ").");

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
            if (packageDefinitions == null)
            {
                return;
            }

            PackageDefinition[] packages = packageDefinitions
                .Where(packageDefinition => packageDefinition != null)
                .ToArray();

            if (packages.Length == 0)
            {
                return;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + operationName + " because another package operation is already running.";
                Debug.LogWarning(LogPrefix + " " + _lastErrorMessage);
                NotifyStateChanged();
                return;
            }

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Install Packages" : operationName,
                packages);

            foreach (PackageDefinition packageDefinition in packages)
            {
                PackageChannel channel = channelSelector != null ? channelSelector(packageDefinition) : PackageChannel.Stable;
                QueueInstall(packageDefinition, channel);
            }

            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            NotifyStateChanged();
        }

        public bool IsQueuedOrInstalling(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && _queuedOrInstallingPackageIds.Contains(packageId);
        }

        public void Dispose()
        {
            EditorApplication.update -= Update;
        }

        private void StartNextRequestIfNeeded()
        {
            if (_currentRequest != null || _installQueue.Count == 0)
            {
                return;
            }

            _currentInstall = _installQueue.Dequeue();
            State = PackageInstallRequestState.Installing;
            MarkProgressItem(
                _currentInstall.PackageDefinition,
                PackageInstallProgressItemState.Active,
                "Installing " + _currentInstall.PackageDefinition.DisplayName + "...");
            _lastStatusMessage = "Installing " + _currentInstall.PackageDefinition.DisplayName + "...";

            try
            {
                _currentRequest = Client.Add(_currentInstall.Url);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;

                Debug.Log(LogPrefix + " Installing " + _currentInstall.PackageDefinition.DisplayName + " using " + _currentInstall.Url + " (" + _currentInstall.Channel + ").");
            }
            catch (Exception exception)
            {
                Debug.LogError(LogPrefix + " Failed to start install for " + _currentInstall.PackageDefinition.DisplayName + ": " + exception.Message);
                CompleteCurrentRequest(false, exception.Message);
            }
        }

        private void Update()
        {
            if (_currentRequest == null || !_currentRequest.IsCompleted)
            {
                return;
            }

            if (_currentRequest.Status == StatusCode.Success)
            {
                PackageDefinition packageDefinition = _currentInstall.PackageDefinition;
                string packageName = _currentRequest.Result != null ? _currentRequest.Result.name : packageDefinition.PackageId;
                string version = _currentRequest.Result != null ? _currentRequest.Result.version : "unknown";
                string message = "Installed " + packageDefinition.DisplayName + " (" + packageName + "@" + version + ") from " + _currentInstall.Channel + ".";

                Debug.Log(LogPrefix + " " + message);
                CompleteCurrentRequest(true, message);
                return;
            }

            string errorMessage = _currentRequest.Error != null
                ? _currentRequest.Error.message
                : "Package Manager returned an unknown error.";

            Debug.LogError(LogPrefix + " Failed to install " + _currentInstall.PackageDefinition.DisplayName + ": " + errorMessage);
            CompleteCurrentRequest(false, errorMessage);
        }

        private void CompleteCurrentRequest(bool success, string message)
        {
            PackageDefinition completedPackage = _currentInstall != null ? _currentInstall.PackageDefinition : null;

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
            StartNextRequestIfNeeded();

            if (_currentRequest == null && _installQueue.Count == 0)
            {
                EditorApplication.update -= Update;
                SetOperationCompleteSummary();
                QueueCompleted?.Invoke();
            }

            NotifyStateChanged();
        }

        private void BeginOperation(string operationName, IEnumerable<PackageDefinition> packages)
        {
            _currentOperationName = operationName ?? string.Empty;
            _lastStatusMessage = "Queued " + _currentOperationName + ".";
            _lastErrorMessage = string.Empty;
            _completedSteps = 0;
            _successfulSteps = 0;
            _failedSteps = 0;
            _progressItems.Clear();
            _progressItemsByPackageId.Clear();

            foreach (PackageDefinition packageDefinition in packages)
            {
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

            if ((state == PackageInstallProgressItemState.Completed ||
                 state == PackageInstallProgressItemState.Failed) &&
                previousState != PackageInstallProgressItemState.Completed &&
                previousState != PackageInstallProgressItemState.Failed)
            {
                _completedSteps++;

                if (state == PackageInstallProgressItemState.Completed)
                {
                    _successfulSteps++;
                }
                else
                {
                    _failedSteps++;
                    _lastErrorMessage = message ?? string.Empty;
                }
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                _lastStatusMessage = message;
            }
        }

        private void CompleteOperationIfIdle()
        {
            if (_currentRequest != null || _installQueue.Count > 0)
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

            if (_failedSteps > 0)
            {
                _lastStatusMessage = _currentOperationName + " finished with " +
                                     _successfulSteps + " succeeded and " +
                                     _failedSteps + " failed.";
                return;
            }

            _lastStatusMessage = _currentOperationName + " completed successfully.";
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }

        private sealed class QueuedPackageInstall
        {
            public QueuedPackageInstall(PackageDefinition packageDefinition, PackageChannel channel, string url)
            {
                PackageDefinition = packageDefinition;
                Channel = channel;
                Url = url;
            }

            public PackageDefinition PackageDefinition { get; }

            public PackageChannel Channel { get; }

            public string Url { get; }
        }
    }
}
