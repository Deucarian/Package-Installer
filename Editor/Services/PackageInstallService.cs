using System;
using System.Collections.Generic;
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

    internal sealed class PackageInstallService : IDisposable
    {
        private const string LogPrefix = "[JorisHoef Package Installer]";

        private readonly Queue<PackageDefinition> _installQueue = new Queue<PackageDefinition>();
        private readonly HashSet<string> _queuedOrInstallingPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private AddRequest _currentRequest;
        private PackageDefinition _currentPackage;

        public event Action StateChanged;

        public event Action<PackageDefinition, bool, string> InstallCompleted;

        public event Action QueueCompleted;

        public PackageInstallRequestState State { get; private set; } = PackageInstallRequestState.Idle;

        public PackageDefinition CurrentPackage => _currentPackage;

        public bool IsBusy => State == PackageInstallRequestState.Installing || _installQueue.Count > 0;

        public bool Install(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                Debug.LogError(LogPrefix + " Cannot install a null package definition.");
                return false;
            }

            if (!packageDefinition.HasPackageReference)
            {
                Debug.LogWarning(LogPrefix + " " + packageDefinition.DisplayName + " has no package reference to install.");
                return false;
            }

            if (_queuedOrInstallingPackageIds.Contains(packageDefinition.PackageId))
            {
                Debug.Log(LogPrefix + " " + packageDefinition.DisplayName + " is already queued or installing.");
                return false;
            }

            _installQueue.Enqueue(packageDefinition);
            _queuedOrInstallingPackageIds.Add(packageDefinition.PackageId);
            Debug.Log(LogPrefix + " Queued " + packageDefinition.DisplayName + " from " + packageDefinition.PackageReference + ".");

            StartNextRequestIfNeeded();
            NotifyStateChanged();

            return true;
        }

        public void InstallMany(IEnumerable<PackageDefinition> packageDefinitions)
        {
            if (packageDefinitions == null)
            {
                return;
            }

            foreach (PackageDefinition packageDefinition in packageDefinitions)
            {
                Install(packageDefinition);
            }
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

            _currentPackage = _installQueue.Dequeue();
            State = PackageInstallRequestState.Installing;

            try
            {
                _currentRequest = Client.Add(_currentPackage.PackageReference);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;

                Debug.Log(LogPrefix + " Installing " + _currentPackage.DisplayName + " using " + _currentPackage.PackageReference + ".");
            }
            catch (Exception exception)
            {
                Debug.LogError(LogPrefix + " Failed to start install for " + _currentPackage.DisplayName + ": " + exception.Message);
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
                string packageName = _currentRequest.Result != null ? _currentRequest.Result.name : _currentPackage.PackageId;
                string version = _currentRequest.Result != null ? _currentRequest.Result.version : "unknown";
                string message = "Installed " + _currentPackage.DisplayName + " (" + packageName + "@" + version + ").";

                Debug.Log(LogPrefix + " " + message);
                CompleteCurrentRequest(true, message);
                return;
            }

            string errorMessage = _currentRequest.Error != null
                ? _currentRequest.Error.message
                : "Package Manager returned an unknown error.";

            Debug.LogError(LogPrefix + " Failed to install " + _currentPackage.DisplayName + ": " + errorMessage);
            CompleteCurrentRequest(false, errorMessage);
        }

        private void CompleteCurrentRequest(bool success, string message)
        {
            PackageDefinition completedPackage = _currentPackage;

            if (completedPackage != null)
            {
                _queuedOrInstallingPackageIds.Remove(completedPackage.PackageId);
            }

            _currentRequest = null;
            _currentPackage = null;
            State = PackageInstallRequestState.Idle;

            InstallCompleted?.Invoke(completedPackage, success, message);
            StartNextRequestIfNeeded();

            if (_currentRequest == null && _installQueue.Count == 0)
            {
                EditorApplication.update -= Update;
                QueueCompleted?.Invoke();
            }

            NotifyStateChanged();
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
