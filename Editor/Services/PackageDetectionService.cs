using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace JorisHoef.PackageInstaller.Editor
{
    internal sealed class PackageDetectionService : IDisposable
    {
        private const string LogPrefix = "[JorisHoef Package Installer]";

        private readonly Dictionary<string, PackageManagerPackageInfo> _installedPackages =
            new Dictionary<string, PackageManagerPackageInfo>(StringComparer.OrdinalIgnoreCase);

        private ListRequest _listRequest;

        public event Action StateChanged;

        public event Action RefreshCompleted;

        public bool IsRefreshing => _listRequest != null && !_listRequest.IsCompleted;

        public void Refresh()
        {
            if (IsRefreshing)
            {
                return;
            }

            try
            {
                _listRequest = Client.List(true, true);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                NotifyStateChanged();
            }
            catch (Exception exception)
            {
                Debug.LogError(LogPrefix + " Failed to start installed-package refresh: " + exception.Message);
                _listRequest = null;
                NotifyStateChanged();
            }
        }

        public bool IsInstalled(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && _installedPackages.ContainsKey(packageId);
        }

        public bool TryGetInstalledPackage(string packageId, out PackageManagerPackageInfo packageInfo)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                packageInfo = null;
                return false;
            }

            return _installedPackages.TryGetValue(packageId, out packageInfo);
        }

        public void Dispose()
        {
            EditorApplication.update -= Update;
        }

        private void Update()
        {
            if (_listRequest == null || !_listRequest.IsCompleted)
            {
                return;
            }

            if (_listRequest.Status == StatusCode.Success)
            {
                _installedPackages.Clear();

                foreach (PackageManagerPackageInfo packageInfo in _listRequest.Result)
                {
                    if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.name))
                    {
                        _installedPackages[packageInfo.name] = packageInfo;
                    }
                }
            }
            else
            {
                string errorMessage = _listRequest.Error != null
                    ? _listRequest.Error.message
                    : "Package Manager returned an unknown error.";

                Debug.LogError(LogPrefix + " Failed to refresh installed-package state: " + errorMessage);
            }

            _listRequest = null;
            EditorApplication.update -= Update;
            NotifyStateChanged();
            RefreshCompleted?.Invoke();
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
