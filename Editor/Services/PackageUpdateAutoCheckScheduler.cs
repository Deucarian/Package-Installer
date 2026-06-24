using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    [InitializeOnLoad]
    internal static class PackageUpdateAutoCheckScheduler
    {
        private static bool _startupCheckRequestedThisSession;
        private static PackageDetectionService _detectionService;
        private static PackageUpdateCheckService _updateCheckService;

        static PackageUpdateAutoCheckScheduler()
        {
            EditorApplication.delayCall += TryRunStartupCheck;
        }

        private static void TryRunStartupCheck()
        {
            EditorApplication.delayCall -= TryRunStartupCheck;

            if (Application.isBatchMode ||
                _startupCheckRequestedThisSession ||
                !PackageUpdateCheckPreferences.CheckOnStartup ||
                PackageUpdateCheckService.IsAnyCheckRunning)
            {
                return;
            }

            _startupCheckRequestedThisSession = true;
            PackageRegistryProvider.EnsureLoaded();

            _detectionService = new PackageDetectionService();
            _updateCheckService = new PackageUpdateCheckService(_detectionService);
            _detectionService.RefreshCompleted += HandleDetectionRefreshCompleted;
            _updateCheckService.StateChanged += HandleUpdateCheckStateChanged;
            _detectionService.Refresh();
        }

        private static void HandleDetectionRefreshCompleted()
        {
            if (_updateCheckService == null || _detectionService == null)
            {
                Cleanup();
                return;
            }

            if (PackageRegistryProvider.IsRemoteRefreshing)
            {
                PackageRegistryProvider.RegistryChanged -= HandleRegistryChanged;
                PackageRegistryProvider.RegistryChanged += HandleRegistryChanged;
                return;
            }

            StartUpdateCheck();
        }

        private static void HandleRegistryChanged()
        {
            if (_detectionService == null || _updateCheckService == null)
            {
                Cleanup();
                return;
            }

            if (PackageRegistryProvider.IsRemoteRefreshing)
            {
                return;
            }

            StartUpdateCheck();
        }

        private static void StartUpdateCheck()
        {
            PackageRegistryProvider.RegistryChanged -= HandleRegistryChanged;

            if (PackageUpdateCheckService.IsAnyCheckRunning)
            {
                Cleanup();
                return;
            }

            PackageUpdateCheckService updateCheckService = _updateCheckService;

            if (updateCheckService == null)
            {
                Cleanup();
                return;
            }

            updateCheckService.CheckForUpdates(PackageRegistryProvider.All, GetAutoSelectedChannel);

            if (_updateCheckService == updateCheckService && !updateCheckService.IsChecking)
            {
                Cleanup();
            }
        }

        private static PackageChannel GetAutoSelectedChannel(PackageDefinition packageDefinition)
        {
            PackageChannel channel = new PackageInstallerStateRepository().GetProjectChannel();

            if (channel == PackageChannel.Development &&
                packageDefinition != null &&
                packageDefinition.HasDevelopmentUrl)
            {
                return PackageChannel.Development;
            }

            return PackageChannel.Stable;
        }

        private static void HandleUpdateCheckStateChanged()
        {
            if (_updateCheckService != null && !_updateCheckService.IsChecking)
            {
                Cleanup();
            }
        }

        private static void Cleanup()
        {
            if (_detectionService != null)
            {
                _detectionService.RefreshCompleted -= HandleDetectionRefreshCompleted;
                _detectionService.Dispose();
                _detectionService = null;
            }

            if (_updateCheckService != null)
            {
                _updateCheckService.StateChanged -= HandleUpdateCheckStateChanged;
                _updateCheckService.Dispose();
                _updateCheckService = null;
            }

            PackageRegistryProvider.RegistryChanged -= HandleRegistryChanged;
        }
    }
}
