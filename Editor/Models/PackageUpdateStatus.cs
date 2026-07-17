namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageUpdateStatusKind
    {
        Unknown,
        NotInstalled,
        Checking,
        UpToDate,
        UpdateAvailable,
        SwitchAvailable,
        SourceMigrationAvailable,
        ReloadPending,
        CannotDetermine,
        Failed
    }

    internal sealed class PackageUpdateStatus
    {
        private const int ShortRevisionLength = 7;

        private PackageUpdateStatus(
            PackageUpdateStatusKind kind,
            string packageId,
            string displayName,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision,
            string installedVersion,
            string latestVersion,
            string runningVersion,
            string message)
        {
            Kind = kind;
            PackageId = packageId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Channel = channel;
            SelectedUrl = selectedUrl ?? string.Empty;
            InstalledRevision = installedRevision ?? string.Empty;
            LatestRevision = latestRevision ?? string.Empty;
            InstalledVersion = installedVersion ?? string.Empty;
            LatestVersion = latestVersion ?? string.Empty;
            RunningVersion = runningVersion ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public PackageUpdateStatusKind Kind { get; }

        public string PackageId { get; }

        public string DisplayName { get; }

        public PackageChannel Channel { get; }

        public string SelectedUrl { get; }

        public string InstalledRevision { get; }

        public string LatestRevision { get; }

        public string InstalledVersion { get; }

        public string LatestVersion { get; }

        public string RunningVersion { get; }

        public string Message { get; }

        public bool IsChecking => Kind == PackageUpdateStatusKind.Checking;

        public bool IsUpdateAvailable =>
            Kind == PackageUpdateStatusKind.UpdateAvailable ||
            Kind == PackageUpdateStatusKind.SwitchAvailable;

        public bool IsSourceMigrationAvailable => Kind == PackageUpdateStatusKind.SourceMigrationAvailable;

        public bool IsReloadPending => Kind == PackageUpdateStatusKind.ReloadPending;

        public bool NeedsAttention => IsUpdateAvailable || IsSourceMigrationAvailable || IsReloadPending;

        public string ShortInstalledRevision => ShortenRevision(InstalledRevision);

        public string ShortLatestRevision => ShortenRevision(LatestRevision);

        public bool HasPackageVersionTransition =>
            !string.IsNullOrWhiteSpace(InstalledVersion) &&
            !string.IsNullOrWhiteSpace(LatestVersion) &&
            !string.Equals(InstalledVersion, LatestVersion, System.StringComparison.OrdinalIgnoreCase);

        public bool HasUnbumpedPackageVersionWarning =>
            IsUpdateAvailable &&
            !string.IsNullOrWhiteSpace(InstalledVersion) &&
            !string.IsNullOrWhiteSpace(LatestVersion) &&
            string.Equals(InstalledVersion, LatestVersion, System.StringComparison.OrdinalIgnoreCase);

        public string PackageVersionWarningMessage =>
            HasUnbumpedPackageVersionWarning
                ? Kind == PackageUpdateStatusKind.SwitchAvailable
                    ? "Switch available, but package version was not bumped."
                    : "Update available, but package version was not bumped."
                : string.Empty;

        public string Label
        {
            get
            {
                switch (Kind)
                {
                    case PackageUpdateStatusKind.NotInstalled:
                        return "Not installed";
                    case PackageUpdateStatusKind.Checking:
                        return "Checking...";
                    case PackageUpdateStatusKind.UpToDate:
                        return "Up to date";
                    case PackageUpdateStatusKind.UpdateAvailable:
                        return "Update available";
                    case PackageUpdateStatusKind.SwitchAvailable:
                        return "Switch available";
                    case PackageUpdateStatusKind.SourceMigrationAvailable:
                        return "Source migration available";
                    case PackageUpdateStatusKind.ReloadPending:
                        return "Reload pending";
                    case PackageUpdateStatusKind.CannotDetermine:
                        return "Cannot determine update";
                    case PackageUpdateStatusKind.Failed:
                        return "Check failed";
                    default:
                        return "Not checked";
                }
            }
        }

        public static PackageUpdateStatus Unknown(PackageDefinition packageDefinition, PackageChannel channel)
        {
            return Create(
                PackageUpdateStatusKind.Unknown,
                packageDefinition,
                channel,
                packageDefinition != null ? packageDefinition.GetUrl(channel) : string.Empty,
                string.Empty,
                string.Empty,
                "Updates have not been checked for this package/channel yet.");
        }

        public static PackageUpdateStatus NotInstalled(PackageDefinition packageDefinition, PackageChannel channel, string selectedUrl)
        {
            return Create(
                PackageUpdateStatusKind.NotInstalled,
                packageDefinition,
                channel,
                selectedUrl,
                string.Empty,
                string.Empty,
                "Install the package before checking for updates.");
        }

        public static PackageUpdateStatus Checking(PackageDefinition packageDefinition, PackageChannel channel, string selectedUrl)
        {
            return Create(
                PackageUpdateStatusKind.Checking,
                packageDefinition,
                channel,
                selectedUrl,
                string.Empty,
                string.Empty,
                "Checking the selected Git reference.");
        }

        public static PackageUpdateStatus UpToDate(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision)
        {
            return UpToDate(
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                "Installed revision matches the selected channel.");
        }

        public static PackageUpdateStatus UpToDate(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision,
            string message)
        {
            return Create(
                PackageUpdateStatusKind.UpToDate,
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                message);
        }

        public static PackageUpdateStatus UpdateAvailable(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision)
        {
            return UpdateAvailable(
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                "Installed revision differs from the selected channel.");
        }

        public static PackageUpdateStatus UpdateAvailable(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision,
            string message)
        {
            return Create(
                PackageUpdateStatusKind.UpdateAvailable,
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                message);
        }

        public static PackageUpdateStatus SwitchAvailable(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision)
        {
            return SwitchAvailable(
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                "Installed package differs from the selected channel.");
        }

        public static PackageUpdateStatus SwitchAvailable(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision,
            string message)
        {
            return Create(
                PackageUpdateStatusKind.SwitchAvailable,
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                message);
        }

        public static PackageUpdateStatus SourceMigrationAvailable(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string latestRevision,
            string installedVersion,
            string latestVersion,
            string message)
        {
            string packageId = packageDefinition != null ? packageDefinition.PackageId : string.Empty;
            string displayName = packageDefinition != null ? packageDefinition.DisplayName : string.Empty;

            return new PackageUpdateStatus(
                PackageUpdateStatusKind.SourceMigrationAvailable,
                packageId,
                displayName,
                channel,
                selectedUrl,
                string.Empty,
                latestRevision,
                installedVersion,
                latestVersion,
                string.Empty,
                message);
        }

        public static PackageUpdateStatus ReloadPending(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision,
            string installedVersion,
            string latestVersion,
            string runningVersion,
            string message)
        {
            string packageId = packageDefinition != null ? packageDefinition.PackageId : string.Empty;
            string displayName = packageDefinition != null ? packageDefinition.DisplayName : string.Empty;

            return new PackageUpdateStatus(
                PackageUpdateStatusKind.ReloadPending,
                packageId,
                displayName,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                installedVersion,
                latestVersion,
                runningVersion,
                message);
        }

        public static PackageUpdateStatus CannotDetermine(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string message)
        {
            return Create(
                PackageUpdateStatusKind.CannotDetermine,
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                string.Empty,
                message);
        }

        public static PackageUpdateStatus Failed(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string message)
        {
            return Create(
                PackageUpdateStatusKind.Failed,
                packageDefinition,
                channel,
                selectedUrl,
                installedRevision,
                string.Empty,
                message);
        }

        private static PackageUpdateStatus Create(
            PackageUpdateStatusKind kind,
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision,
            string message)
        {
            string packageId = packageDefinition != null ? packageDefinition.PackageId : string.Empty;
            string displayName = packageDefinition != null ? packageDefinition.DisplayName : string.Empty;

            return new PackageUpdateStatus(
                kind,
                packageId,
                displayName,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                string.Empty,
                string.Empty,
                string.Empty,
                message);
        }

        public PackageUpdateStatus WithPackageVersions(string installedVersion, string latestVersion)
        {
            return new PackageUpdateStatus(
                Kind,
                PackageId,
                DisplayName,
                Channel,
                SelectedUrl,
                InstalledRevision,
                LatestRevision,
                installedVersion,
                latestVersion,
                RunningVersion,
                Message);
        }

        public PackageUpdateStatus WithRunningVersion(string runningVersion)
        {
            return new PackageUpdateStatus(
                Kind,
                PackageId,
                DisplayName,
                Channel,
                SelectedUrl,
                InstalledRevision,
                LatestRevision,
                InstalledVersion,
                LatestVersion,
                runningVersion,
                Message);
        }

        internal PackageUpdateStatus WithPackageDefinition(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return this;
            }

            return new PackageUpdateStatus(
                Kind,
                packageDefinition.PackageId,
                packageDefinition.DisplayName,
                Channel,
                SelectedUrl,
                InstalledRevision,
                LatestRevision,
                InstalledVersion,
                LatestVersion,
                RunningVersion,
                Message);
        }

        internal static PackageUpdateStatus Restore(
            PackageUpdateStatusKind kind,
            string packageId,
            string displayName,
            PackageChannel channel,
            string selectedUrl,
            string installedRevision,
            string latestRevision,
            string installedVersion,
            string latestVersion,
            string runningVersion,
            string message)
        {
            return new PackageUpdateStatus(
                kind,
                packageId,
                displayName,
                channel,
                selectedUrl,
                installedRevision,
                latestRevision,
                installedVersion,
                latestVersion,
                runningVersion,
                message);
        }

        private static string ShortenRevision(string revision)
        {
            if (string.IsNullOrWhiteSpace(revision) || revision.Length <= ShortRevisionLength)
            {
                return revision ?? string.Empty;
            }

            return revision.Substring(0, ShortRevisionLength);
        }
    }
}
