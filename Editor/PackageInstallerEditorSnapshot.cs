using System;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    public readonly struct PackageInstallerEditorSnapshot
    {
        public PackageInstallerEditorSnapshot(
            bool catalogIsValid,
            int catalogPackageCount,
            bool catalogRefreshInProgress,
            bool hasInstalledPackageSnapshot,
            int installedPackageCount,
            bool installedRefreshInProgress,
            int availableUpdateCount,
            int updateCheckFailureCount,
            bool updateCheckInProgress,
            DateTime? lastUpdateCheckedUtc,
            bool operationInProgress,
            int completedOperationSteps,
            int totalOperationSteps,
            int failedOperationSteps)
        {
            CatalogIsValid = catalogIsValid;
            CatalogPackageCount = Math.Max(0, catalogPackageCount);
            CatalogRefreshInProgress = catalogRefreshInProgress;
            HasInstalledPackageSnapshot = hasInstalledPackageSnapshot;
            InstalledPackageCount = Math.Max(0, installedPackageCount);
            InstalledRefreshInProgress = installedRefreshInProgress;
            AvailableUpdateCount = Math.Max(0, availableUpdateCount);
            UpdateCheckFailureCount = Math.Max(0, updateCheckFailureCount);
            UpdateCheckInProgress = updateCheckInProgress;
            LastUpdateCheckedUtc = lastUpdateCheckedUtc;
            OperationInProgress = operationInProgress;
            CompletedOperationSteps = Math.Max(0, completedOperationSteps);
            TotalOperationSteps = Math.Max(0, totalOperationSteps);
            FailedOperationSteps = Math.Max(0, failedOperationSteps);
        }

        public bool CatalogIsValid { get; }
        public int CatalogPackageCount { get; }
        public bool CatalogRefreshInProgress { get; }
        public bool HasInstalledPackageSnapshot { get; }
        public int InstalledPackageCount { get; }
        public bool InstalledRefreshInProgress { get; }
        public int AvailableUpdateCount { get; }
        public int UpdateCheckFailureCount { get; }
        public bool UpdateCheckInProgress { get; }
        public DateTime? LastUpdateCheckedUtc { get; }
        public bool OperationInProgress { get; }
        public int CompletedOperationSteps { get; }
        public int TotalOperationSteps { get; }
        public int FailedOperationSteps { get; }
    }

    public static class PackageInstallerEditorStatus
    {
        private static bool hasInstalledPackageSnapshot;
        private static int installedPackageCount;
        private static bool installedRefreshInProgress;
        private static bool operationInProgress;
        private static int completedOperationSteps;
        private static int totalOperationSteps;
        private static int failedOperationSteps;

        public static PackageInstallerEditorSnapshot Capture()
        {
            PackageRegistryCachedStatus catalog =
                PackageRegistryProvider.CaptureCachedStatus();
            PackageUpdateCheckCachedStatus updates =
                PackageUpdateCheckService.CaptureCachedStatus();

            return new PackageInstallerEditorSnapshot(
                catalog.IsValid,
                catalog.PackageCount,
                catalog.IsRefreshing,
                hasInstalledPackageSnapshot,
                installedPackageCount,
                installedRefreshInProgress,
                updates.AvailableCount,
                updates.FailureCount,
                updates.IsChecking,
                updates.LastCheckedUtc,
                operationInProgress,
                completedOperationSteps,
                totalOperationSteps,
                failedOperationSteps);
        }

        internal static void PublishInstalledPackages(
            int installedCount,
            bool hasSuccessfulRefresh,
            bool isRefreshing)
        {
            if (hasSuccessfulRefresh)
            {
                hasInstalledPackageSnapshot = true;
                installedPackageCount = Math.Max(0, installedCount);
            }

            installedRefreshInProgress = isRefreshing;
        }

        internal static void PublishOperation(PackageInstallService service)
        {
            if (service == null)
            {
                operationInProgress = false;
                completedOperationSteps = 0;
                totalOperationSteps = 0;
                failedOperationSteps = 0;
                return;
            }

            operationInProgress = service.IsBusy;
            completedOperationSteps = service.CompletedSteps;
            totalOperationSteps = service.TotalSteps;
            failedOperationSteps = service.FailedSteps;
        }

        internal static void ResetPublishedStateForTests()
        {
            hasInstalledPackageSnapshot = false;
            installedPackageCount = 0;
            installedRefreshInProgress = false;
            PublishOperation(null);
        }
    }

    internal readonly struct PackageRegistryCachedStatus
    {
        public PackageRegistryCachedStatus(bool isValid, int packageCount, bool isRefreshing)
        {
            IsValid = isValid;
            PackageCount = Math.Max(0, packageCount);
            IsRefreshing = isRefreshing;
        }

        public bool IsValid { get; }
        public int PackageCount { get; }
        public bool IsRefreshing { get; }
    }

    internal readonly struct PackageUpdateCheckCachedStatus
    {
        public PackageUpdateCheckCachedStatus(
            int availableCount,
            int failureCount,
            bool isChecking,
            DateTime? lastCheckedUtc)
        {
            AvailableCount = Math.Max(0, availableCount);
            FailureCount = Math.Max(0, failureCount);
            IsChecking = isChecking;
            LastCheckedUtc = lastCheckedUtc;
        }

        public int AvailableCount { get; }
        public int FailureCount { get; }
        public bool IsChecking { get; }
        public DateTime? LastCheckedUtc { get; }
    }
}
