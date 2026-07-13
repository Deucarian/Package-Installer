using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageInstallSourceType
    {
        Unknown,
        Git,
        Registry,
        Local,
        Embedded
    }

    internal static class PackageInstallSourceUtility
    {
        public static PackageInstallSourceType Detect(
            string packageSourceName,
            string packageManagerPackageId,
            string installedPackageReference,
            string resolvedPath)
        {
            string sourceName = (packageSourceName ?? string.Empty).Trim();

            if (string.Equals(sourceName, "Git", StringComparison.OrdinalIgnoreCase))
            {
                return PackageInstallSourceType.Git;
            }

            if (string.Equals(sourceName, "Registry", StringComparison.OrdinalIgnoreCase))
            {
                return PackageInstallSourceType.Registry;
            }

            if (string.Equals(sourceName, "Embedded", StringComparison.OrdinalIgnoreCase))
            {
                return PackageInstallSourceType.Embedded;
            }

            if (string.Equals(sourceName, "Local", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceName, "LocalTarball", StringComparison.OrdinalIgnoreCase))
            {
                return PackageInstallSourceType.Local;
            }

            if (LooksLikeGitPackageReference(packageManagerPackageId) ||
                LooksLikeGitPackageReference(installedPackageReference))
            {
                return PackageInstallSourceType.Git;
            }

            if (TryExtractRegistryVersion(packageManagerPackageId, string.Empty, out _) ||
                TryExtractRegistryVersion(installedPackageReference, string.Empty, out _))
            {
                return PackageInstallSourceType.Registry;
            }

            if (!string.IsNullOrWhiteSpace(resolvedPath) &&
                (resolvedPath.IndexOf("/Packages/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 resolvedPath.IndexOf("\\Packages\\", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return PackageInstallSourceType.Embedded;
            }

            return PackageInstallSourceType.Unknown;
        }

        public static bool LooksLikeGitPackageReference(string packageReference)
        {
            if (string.IsNullOrWhiteSpace(packageReference))
            {
                return false;
            }

            string value = packageReference.Trim();
            return value.StartsWith("git+", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ||
                   value.IndexOf(".git", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TryExtractRegistryVersion(
            string packageReference,
            string packageId,
            out string version)
        {
            version = string.Empty;

            if (string.IsNullOrWhiteSpace(packageReference))
            {
                return false;
            }

            string value = packageReference.Trim();

            if (!string.IsNullOrWhiteSpace(packageId))
            {
                string prefix = packageId.Trim() + "@";
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(prefix.Length).Trim();
                }
            }
            else
            {
                int atIndex = value.LastIndexOf('@');
                if (atIndex >= 0 && atIndex < value.Length - 1)
                {
                    value = value.Substring(atIndex + 1).Trim();
                }
            }

            if (!LooksLikeStableOrPrereleaseVersion(value))
            {
                return false;
            }

            version = value;
            return true;
        }

        public static bool LooksLikeStableOrPrereleaseVersion(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   Regex.IsMatch(
                       value.Trim(),
                       @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$");
        }
    }

    internal sealed class PackageUpdateCheckService : IDisposable
    {
        private const int GitTimeoutMilliseconds = 15000;
        private const int PackageManifestTimeoutMilliseconds = 15000;
        private const double TargetedCheckDebounceSeconds = 0.15d;

        private static readonly Regex ShaRegex =
            new Regex("(?<![0-9a-fA-F])([0-9a-fA-F]{40})(?![0-9a-fA-F])", RegexOptions.Compiled);

        private readonly PackageDetectionService _packageDetectionService;
        private static readonly Dictionary<string, PackageUpdateStatus> Statuses =
            new Dictionary<string, PackageUpdateStatus>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, TargetedUpdateCheckRequest> PendingTargetedChecks =
            new Dictionary<string, TargetedUpdateCheckRequest>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, TargetedUpdateCheckRequest> ActiveTargetedChecks =
            new Dictionary<string, TargetedUpdateCheckRequest>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> LatestTargetedCheckGenerations =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<string> _packageLockPaths;
        private readonly Action _sharedStateChangedHandler;

        private static Task<PackageUpdateStatus[]> CheckTask;
        private static IReadOnlyList<UpdateCheckItem> ActiveCheckItems = Array.Empty<UpdateCheckItem>();
        private static int TargetedCheckGeneration;
        private static bool IsTargetedUpdateRegistered;
        private static string LastFailureMessageValue = string.Empty;
        private static string LastStatusMessageValue = string.Empty;
        private static event Action SharedStateChanged;
        internal static Func<PackageDefinition, PackageChannel, string, PackageVersionResult> GitPackageVersionResolverForTests;

        public PackageUpdateCheckService(PackageDetectionService packageDetectionService)
        {
            _packageDetectionService = packageDetectionService ?? throw new ArgumentNullException(nameof(packageDetectionService));
            _packageLockPaths = GetPackageLockPaths();
            _sharedStateChangedHandler = NotifyStateChanged;
            SharedStateChanged += _sharedStateChangedHandler;
        }

        public event Action StateChanged;

        public bool IsChecking => CheckTask != null;

        public static bool IsAnyCheckRunning => CheckTask != null || HasTargetedChecks;

        private static bool HasTargetedChecks =>
            PendingTargetedChecks.Count > 0 ||
            ActiveTargetedChecks.Count > 0;

        public bool HasStatuses => Statuses.Count > 0;

        public DateTime? LastCheckedUtc => PackageUpdateCheckPreferences.LastCheckedUtc;

        public string LastFailureMessage => LastFailureMessageValue;

        public string LastStatusMessage => LastStatusMessageValue;

        public void CheckForUpdates(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            if (IsChecking)
            {
                PackageInstallerLog.UpdateChecks.Info("Update check is already running.");
                return;
            }

            List<UpdateCheckItem> checkItems = new List<UpdateCheckItem>();

            foreach (PackageDefinition packageDefinition in GetInstallablePackages(packageDefinitions))
            {
                PackageChannel channel = channelSelector != null ? channelSelector(packageDefinition) : PackageChannel.Stable;
                string selectedUrl = packageDefinition.GetUrl(channel);

                if (channel == PackageChannel.Custom)
                {
                    Statuses[packageDefinition.PackageId] =
                        PackageUpdateStatus.Unknown(packageDefinition, channel);
                    continue;
                }

                if (!_packageDetectionService.TryGetInstalledPackage(
                        packageDefinition.PackageId,
                        out PackageManagerPackageInfo packageInfo))
                {
                    Statuses[packageDefinition.PackageId] =
                        PackageUpdateStatus.NotInstalled(packageDefinition, channel, selectedUrl);
                    continue;
                }

                Statuses[packageDefinition.PackageId] =
                    PackageUpdateStatus.Checking(packageDefinition, channel, selectedUrl);

                checkItems.Add(CreateUpdateCheckItem(
                    packageDefinition,
                    channel,
                    selectedUrl,
                    packageInfo));
            }

            LastFailureMessageValue = string.Empty;
            LastStatusMessageValue = "Checking for package updates...";

            if (checkItems.Count == 0)
            {
                RecordCheckCompleted(Array.Empty<PackageUpdateStatus>());
                return;
            }

            ActiveCheckItems = checkItems;
            CheckTask = Task.Run(() => checkItems.Select(CheckItem).ToArray());

            EditorApplication.update -= UpdateShared;
            EditorApplication.update += UpdateShared;
            NotifySharedStateChanged();
        }

        public bool CancelCurrentCheck()
        {
            bool hadActiveCheck = IsChecking;

            if (hadActiveCheck)
            {
                RestoreActiveCheckingStatusesToUnknown();
            }

            CheckTask = null;
            ActiveCheckItems = Array.Empty<UpdateCheckItem>();
            LastFailureMessageValue = string.Empty;
            LastStatusMessageValue = "Update check canceled.";

            EditorApplication.update -= UpdateShared;
            NotifySharedStateChanged();
            return hadActiveCheck;
        }

        public void CheckForUpdate(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null || !packageDefinition.HasPackageReference)
            {
                return;
            }

            string packageId = packageDefinition.PackageId;
            string selectedUrl = packageDefinition.GetUrl(channel);

            if (channel == PackageChannel.Custom)
            {
                CancelTargetedCheck(packageId);
                Statuses[packageId] = PackageUpdateStatus.Unknown(packageDefinition, channel);
                NotifySharedStateChanged();
                return;
            }

            if (!_packageDetectionService.TryGetInstalledPackage(packageId, out PackageManagerPackageInfo packageInfo))
            {
                CancelTargetedCheck(packageId);
                Statuses[packageId] = PackageUpdateStatus.NotInstalled(packageDefinition, channel, selectedUrl);
                NotifySharedStateChanged();
                return;
            }

            if (TryGetEquivalentTargetedCheck(
                    packageId,
                    channel,
                    selectedUrl,
                    out TargetedUpdateCheckRequest equivalentRequest))
            {
                if (PendingTargetedChecks.TryGetValue(packageId, out TargetedUpdateCheckRequest pending) &&
                    !pending.Matches(channel, selectedUrl))
                {
                    PendingTargetedChecks.Remove(packageId);
                }

                LatestTargetedCheckGenerations[packageId] = equivalentRequest.Generation;
                Statuses[packageId] = PackageUpdateStatus.Checking(packageDefinition, channel, selectedUrl);
                NotifySharedStateChanged();
                return;
            }

            int generation = ++TargetedCheckGeneration;
            UpdateCheckItem item = CreateUpdateCheckItem(
                packageDefinition,
                channel,
                selectedUrl,
                packageInfo);

            PendingTargetedChecks[packageId] = new TargetedUpdateCheckRequest(
                item,
                generation,
                EditorApplication.timeSinceStartup + TargetedCheckDebounceSeconds);
            LatestTargetedCheckGenerations[packageId] = generation;
            Statuses[packageId] = PackageUpdateStatus.Checking(packageDefinition, channel, selectedUrl);

            RegisterTargetedUpdate();
            NotifySharedStateChanged();
        }

        public PackageUpdateStatus GetStatus(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                return PackageUpdateStatus.Unknown(null, channel);
            }

            string selectedUrl = packageDefinition.GetUrl(channel);

            if (Statuses.TryGetValue(packageDefinition.PackageId, out PackageUpdateStatus status) &&
                status.Channel == channel &&
                string.Equals(status.SelectedUrl, selectedUrl, StringComparison.Ordinal))
            {
                return status;
            }

            if (!_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                return PackageUpdateStatus.NotInstalled(packageDefinition, channel, selectedUrl);
            }

            return PackageUpdateStatus.Unknown(packageDefinition, channel);
        }

        public IEnumerable<PackageDefinition> GetPackagesWithUpdates(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            foreach (PackageDefinition packageDefinition in GetInstallablePackages(packageDefinitions))
            {
                PackageChannel channel = channelSelector != null ? channelSelector(packageDefinition) : PackageChannel.Stable;

                if (GetStatus(packageDefinition, channel).IsUpdateAvailable)
                {
                    yield return packageDefinition;
                }
            }
        }

        private UpdateCheckItem CreateUpdateCheckItem(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            PackageManagerPackageInfo packageInfo)
        {
            _packageDetectionService.TryGetInstalledPackageReference(
                packageDefinition.PackageId,
                out string installedPackageReference);
            _packageDetectionService.TryGetInstalledPackageSourceType(
                packageDefinition.PackageId,
                out PackageInstallSourceType sourceType);
            _packageDetectionService.TryGetInstalledPackageVersion(
                packageDefinition.PackageId,
                out string installedVersion);

            bool hasInstalledChannel = _packageDetectionService.TryGetInstalledPackageChannel(
                packageDefinition,
                out PackageChannel installedChannel,
                out _);
            bool isSelf = PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId);

            return new UpdateCheckItem(
                packageDefinition,
                channel,
                selectedUrl,
                packageInfo != null ? packageInfo.packageId : string.Empty,
                packageInfo != null ? packageInfo.resolvedPath : string.Empty,
                installedPackageReference,
                sourceType,
                installedVersion,
                hasInstalledChannel,
                installedChannel,
                _packageLockPaths,
                isSelf ? PackageInstallerRuntimeIdentity.Version : string.Empty,
                isSelf
                    ? PackageInstallerSelfUpdateState.CaptureSnapshot()
                    : PackageInstallerSelfUpdateSnapshot.None);
        }

        public void Invalidate(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            CancelTargetedCheck(packageId);

            if (Statuses.Remove(packageId))
            {
                NotifySharedStateChanged();
            }
        }

        public void InvalidateAll()
        {
            if (Statuses.Count == 0)
            {
                return;
            }

            Statuses.Clear();
            PendingTargetedChecks.Clear();
            LatestTargetedCheckGenerations.Clear();
            UnregisterTargetedUpdateIfIdle();
            LastFailureMessageValue = string.Empty;
            LastStatusMessageValue = string.Empty;
            NotifySharedStateChanged();
        }

        public void Dispose()
        {
            SharedStateChanged -= _sharedStateChangedHandler;
        }

        private static IEnumerable<PackageDefinition> GetInstallablePackages(IEnumerable<PackageDefinition> packageDefinitions)
        {
            if (packageDefinitions == null)
            {
                yield break;
            }

            foreach (PackageDefinition packageDefinition in packageDefinitions)
            {
                if (packageDefinition != null && packageDefinition.HasPackageReference)
                {
                    yield return packageDefinition;
                }
            }
        }

        internal static PackageUpdateStatus CheckItemForTests(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string packageManagerPackageId,
            string resolvedPath,
            string installedPackageReference,
            IReadOnlyList<string> packageLockPaths)
        {
            return CheckItem(new UpdateCheckItem(
                packageDefinition,
                channel,
                selectedUrl,
                packageManagerPackageId,
                resolvedPath,
                installedPackageReference,
                PackageInstallSourceType.Git,
                string.Empty,
                false,
                PackageChannel.Stable,
                packageLockPaths,
                string.Empty,
                PackageInstallerSelfUpdateSnapshot.None));
        }

        internal static PackageUpdateStatus CheckItemForTests(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string packageManagerPackageId,
            string resolvedPath,
            string installedPackageReference,
            PackageInstallSourceType sourceType,
            string installedVersion,
            IReadOnlyList<string> packageLockPaths)
        {
            return CheckItemForTests(
                packageDefinition,
                channel,
                selectedUrl,
                packageManagerPackageId,
                resolvedPath,
                installedPackageReference,
                sourceType,
                installedVersion,
                hasInstalledChannel: false,
                installedChannel: PackageChannel.Stable,
                packageLockPaths: packageLockPaths,
                runningInstallerVersion: string.Empty,
                selfUpdateSnapshot: PackageInstallerSelfUpdateSnapshot.None);
        }

        internal static PackageUpdateStatus CheckItemForTests(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string packageManagerPackageId,
            string resolvedPath,
            string installedPackageReference,
            PackageInstallSourceType sourceType,
            string installedVersion,
            bool hasInstalledChannel,
            PackageChannel installedChannel,
            IReadOnlyList<string> packageLockPaths)
        {
            return CheckItemForTests(
                packageDefinition,
                channel,
                selectedUrl,
                packageManagerPackageId,
                resolvedPath,
                installedPackageReference,
                sourceType,
                installedVersion,
                hasInstalledChannel,
                installedChannel,
                packageLockPaths,
                string.Empty,
                PackageInstallerSelfUpdateSnapshot.None);
        }

        internal static PackageUpdateStatus CheckItemForTests(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            string selectedUrl,
            string packageManagerPackageId,
            string resolvedPath,
            string installedPackageReference,
            PackageInstallSourceType sourceType,
            string installedVersion,
            bool hasInstalledChannel,
            PackageChannel installedChannel,
            IReadOnlyList<string> packageLockPaths,
            string runningInstallerVersion,
            PackageInstallerSelfUpdateSnapshot selfUpdateSnapshot)
        {
            return CheckItem(new UpdateCheckItem(
                packageDefinition,
                channel,
                selectedUrl,
                packageManagerPackageId,
                resolvedPath,
                installedPackageReference,
                sourceType,
                installedVersion,
                hasInstalledChannel,
                installedChannel,
                packageLockPaths,
                runningInstallerVersion,
                selfUpdateSnapshot));
        }

        private static PackageUpdateStatus CheckItem(UpdateCheckItem item)
        {
            try
            {
                PackageInstallSourceType sourceType = item.SourceType == PackageInstallSourceType.Unknown
                    ? PackageInstallSourceUtility.Detect(
                        string.Empty,
                        item.PackageManagerPackageId,
                        item.InstalledPackageReference,
                        item.ResolvedPath)
                    : item.SourceType;

                if (sourceType == PackageInstallSourceType.Registry)
                {
                    return CheckSourceMigrationItem(item);
                }

                if (sourceType == PackageInstallSourceType.Local ||
                    sourceType == PackageInstallSourceType.Embedded)
                {
                    return PackageUpdateStatus.CannotDetermine(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        item.InstalledVersion,
                        "Update checks are not available for " + sourceType.ToString().ToLowerInvariant() + " packages.")
                        .WithPackageVersions(item.InstalledVersion, string.Empty);
                }

                if (string.IsNullOrWhiteSpace(item.SelectedUrl))
                {
                    return PackageUpdateStatus.Failed(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        string.Empty,
                        "Selected channel has no package URL.");
                }

                if (!TryParseGitPackageReference(
                        item.SelectedUrl,
                        out string remoteUrl,
                        out string reference,
                        out string parseMessage))
                {
                    return PackageUpdateStatus.Failed(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        string.Empty,
                        parseMessage);
                }

                if (!TryGetInstalledRevision(item, out string installedRevision))
                {
                    return PackageUpdateStatus.CannotDetermine(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        string.Empty,
                        "The package is installed, but Unity did not expose a Git revision for this package.")
                        .WithPackageVersions(item.InstalledVersion, string.Empty);
                }

                if (!TryGetRemoteRevision(remoteUrl, reference, out string latestRevision, out string remoteMessage))
                {
                    return PackageUpdateStatus.Failed(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        installedRevision,
                        remoteMessage);
                }

                PackageVersionResult latestPackageVersionResult =
                    ResolveGitPackageVersion(item, latestRevision);
                string latestPackageVersion = latestPackageVersionResult != null &&
                                              latestPackageVersionResult.Success
                    ? latestPackageVersionResult.Version
                    : string.Empty;

                if (RevisionsMatch(installedRevision, latestRevision))
                {
                    if (ShouldReportSelfReloadPending(item))
                    {
                        return CreateSelfReloadPendingStatus(
                            item,
                            installedRevision,
                            latestRevision,
                            latestPackageVersion);
                    }

                    return PackageUpdateStatus.UpToDate(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        installedRevision,
                        latestRevision)
                        .WithPackageVersions(item.InstalledVersion, latestPackageVersion);
                }

                return CreateAvailableStatus(
                    item,
                    installedRevision,
                    latestRevision,
                    "Installed revision differs from the selected channel.")
                    .WithPackageVersions(item.InstalledVersion, latestPackageVersion);
            }
            catch (Exception exception)
            {
                return PackageUpdateStatus.Failed(
                    item.PackageDefinition,
                    item.Channel,
                    item.SelectedUrl,
                    string.Empty,
                        "Update check failed: " + exception.Message);
            }
        }

        private static PackageUpdateStatus CheckSourceMigrationItem(UpdateCheckItem item)
        {
            string installedVersion = TryGetInstalledRegistryVersion(item, out string registryVersion)
                ? registryVersion
                : item.InstalledVersion;
            string latestRevision = string.Empty;
            string latestVersion = string.Empty;
            string diagnostic = string.Empty;
            string parseMessage = string.Empty;

            if (!string.IsNullOrWhiteSpace(item.SelectedUrl) &&
                TryParseGitPackageReference(
                    item.SelectedUrl,
                    out string remoteUrl,
                    out string reference,
                    out parseMessage))
            {
                if (!TryGetRemoteRevision(remoteUrl, reference, out latestRevision, out string remoteMessage))
                {
                    diagnostic = remoteMessage;
                }
            }
            else
            {
                diagnostic = string.IsNullOrWhiteSpace(item.SelectedUrl)
                    ? "The selected catalog channel has no Git URL."
                    : parseMessage;
            }

            PackageVersionResult latestPackageVersionResult = ResolveGitPackageVersion(item, latestRevision);
            if (latestPackageVersionResult != null && latestPackageVersionResult.Success)
            {
                latestVersion = latestPackageVersionResult.Version;
            }
            else if (latestPackageVersionResult != null &&
                     !string.IsNullOrWhiteSpace(latestPackageVersionResult.Message))
            {
                diagnostic = AppendDiagnostic(diagnostic, latestPackageVersionResult.Message);
            }

            bool isSelf = PackageInstallerRuntimeIdentity.IsSelf(item.PackageDefinition.PackageId);
            string message = isSelf
                ? "Package Installer is installed from a registry. Open Bootstrap to migrate it safely to the selected Git channel."
                : item.PackageDefinition.DisplayName +
                  " is installed from a registry. Migrate it to the selected catalog Git URL.";

            if (!string.IsNullOrWhiteSpace(diagnostic))
            {
                message += " Target metadata was unavailable: " + diagnostic;
            }

            return PackageUpdateStatus.SourceMigrationAvailable(
                item.PackageDefinition,
                item.Channel,
                item.SelectedUrl,
                latestRevision,
                installedVersion,
                latestVersion,
                message);
        }

        private static string AppendDiagnostic(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
            {
                return second ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(second))
            {
                return first;
            }

            return first.TrimEnd('.', ' ') + ". " + second;
        }

        private static bool ShouldReportSelfReloadPending(UpdateCheckItem item)
        {
            if (item == null ||
                item.PackageDefinition == null ||
                !PackageInstallerRuntimeIdentity.IsSelf(item.PackageDefinition.PackageId))
            {
                return false;
            }

            bool versionMismatch =
                !string.IsNullOrWhiteSpace(item.RunningInstallerVersion) &&
                !string.IsNullOrWhiteSpace(item.InstalledVersion) &&
                !string.Equals(
                    item.RunningInstallerVersion,
                    item.InstalledVersion,
                    StringComparison.OrdinalIgnoreCase);

            return versionMismatch || item.SelfUpdateSnapshot.IsAwaitingReload;
        }

        private static PackageUpdateStatus CreateSelfReloadPendingStatus(
            UpdateCheckItem item,
            string installedRevision,
            string latestRevision,
            string latestPackageVersion)
        {
            string runningVersion = !string.IsNullOrWhiteSpace(item.RunningInstallerVersion)
                ? item.RunningInstallerVersion
                : item.SelfUpdateSnapshot.SourceVersion;
            string resolvedVersion = !string.IsNullOrWhiteSpace(item.InstalledVersion)
                ? item.InstalledVersion
                : item.SelfUpdateSnapshot.ResolvedVersion;
            string targetVersion = !string.IsNullOrWhiteSpace(latestPackageVersion)
                ? latestPackageVersion
                : resolvedVersion;
            string message = !string.IsNullOrWhiteSpace(runningVersion) &&
                             !string.IsNullOrWhiteSpace(resolvedVersion) &&
                             !string.Equals(runningVersion, resolvedVersion, StringComparison.OrdinalIgnoreCase)
                ? "Unity Package Manager resolved Package Installer " + resolvedVersion +
                  ", but assembly " + runningVersion +
                  " is still running. Fix compilation errors, then retry the script reload."
                : "Unity Package Manager resolved the Package Installer update, but the previous assembly is still running. Fix compilation errors, then retry the script reload.";

            return PackageUpdateStatus.ReloadPending(
                item.PackageDefinition,
                item.Channel,
                item.SelectedUrl,
                installedRevision,
                latestRevision,
                resolvedVersion,
                targetVersion,
                runningVersion,
                message);
        }

        private static bool TryGetInstalledRegistryVersion(UpdateCheckItem item, out string installedVersion)
        {
            installedVersion = string.Empty;

            if (item == null)
            {
                return false;
            }

            if (PackageInstallSourceUtility.LooksLikeStableOrPrereleaseVersion(item.InstalledVersion))
            {
                installedVersion = item.InstalledVersion.Trim();
                return true;
            }

            string packageId = item.PackageDefinition != null
                ? item.PackageDefinition.PackageId
                : string.Empty;

            return PackageInstallSourceUtility.TryExtractRegistryVersion(
                       item.PackageManagerPackageId,
                       packageId,
                       out installedVersion) ||
                   PackageInstallSourceUtility.TryExtractRegistryVersion(
                       item.InstalledPackageReference,
                       packageId,
                       out installedVersion);
        }

        private static PackageUpdateStatus CreateAvailableStatus(
            UpdateCheckItem item,
            string installedRevision,
            string latestRevision,
            string updateMessage)
        {
            if (IsSwitchBetweenChannels(item))
            {
                return PackageUpdateStatus.SwitchAvailable(
                    item.PackageDefinition,
                    item.Channel,
                    item.SelectedUrl,
                    installedRevision,
                    latestRevision,
                    "Installed package differs from the selected " + GetChannelLabel(item.Channel) + " channel.");
            }

            return PackageUpdateStatus.UpdateAvailable(
                item.PackageDefinition,
                item.Channel,
                item.SelectedUrl,
                installedRevision,
                latestRevision,
                updateMessage);
        }

        private static bool IsSwitchBetweenChannels(UpdateCheckItem item)
        {
            return item != null &&
                   item.HasInstalledChannel &&
                   item.InstalledChannel != item.Channel &&
                   item.Channel != PackageChannel.Custom;
        }

        private static string GetChannelLabel(PackageChannel channel)
        {
            switch (channel)
            {
                case PackageChannel.Development:
                    return "Development";
                case PackageChannel.Custom:
                    return "Custom";
                default:
                    return "Stable";
            }
        }

        private static PackageVersionResult ResolveGitPackageVersion(
            UpdateCheckItem item,
            string targetRevision)
        {
            Func<PackageDefinition, PackageChannel, string, PackageVersionResult> resolver =
                GitPackageVersionResolverForTests;
            if (resolver != null)
            {
                return resolver(item.PackageDefinition, item.Channel, targetRevision);
            }

            if (item == null || string.IsNullOrWhiteSpace(item.SelectedUrl))
            {
                return PackageVersionResult.Fail("Cannot resolve target package version without a selected package URL.");
            }

            string referenceOverride = string.IsNullOrWhiteSpace(targetRevision)
                ? string.Empty
                : targetRevision.Trim();

            if (!PackageRegistryPackageNameValidator.TryCreateGitHubPackageJsonUrl(
                    item.SelectedUrl,
                    referenceOverride,
                    out string packageJsonUrl))
            {
                return PackageVersionResult.Fail("Could not resolve target package.json URL.");
            }

            return FetchPackageVersion(packageJsonUrl);
        }

        private static PackageVersionResult FetchPackageVersion(string packageJsonUrl)
        {
            if (string.IsNullOrWhiteSpace(packageJsonUrl))
            {
                return PackageVersionResult.Fail("Cannot fetch package version without a package.json URL.");
            }

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(packageJsonUrl);
                request.Method = "GET";
                request.Timeout = PackageManifestTimeoutMilliseconds;
                request.ReadWriteTimeout = PackageManifestTimeoutMilliseconds;

                using (WebResponse response = request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string packageJson = reader.ReadToEnd();

                    if (!PackageRegistryPackageNameValidator.TryReadPackageVersion(
                            packageJson,
                            out string packageVersion))
                    {
                        return PackageVersionResult.Fail("Target package.json did not include a version.");
                    }

                    if (!PackageInstallSourceUtility.LooksLikeStableOrPrereleaseVersion(packageVersion))
                    {
                        return PackageVersionResult.Fail("Target package.json version is not valid SemVer.");
                    }

                    return PackageVersionResult.Ok(packageVersion);
                }
            }
            catch (Exception exception)
            {
                return PackageVersionResult.Fail(
                    "Could not fetch target package version: " + exception.GetBaseException().Message);
            }
        }

        private static void UpdateShared()
        {
            if (CheckTask == null || !CheckTask.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= UpdateShared;

            PackageUpdateStatus[] results;

            try
            {
                results = CheckTask.Result;
            }
            catch (Exception exception)
            {
                results = ActiveCheckItems
                    .Select(item => PackageUpdateStatus.Failed(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        string.Empty,
                        "Update check failed: " + exception.GetBaseException().Message))
                    .ToArray();
            }

            RecordCheckCompleted(results);
        }

        private static void RegisterTargetedUpdate()
        {
            if (IsTargetedUpdateRegistered)
            {
                return;
            }

            EditorApplication.update += UpdateTargetedChecks;
            IsTargetedUpdateRegistered = true;
        }

        private static void UnregisterTargetedUpdateIfIdle()
        {
            if (HasTargetedChecks)
            {
                return;
            }

            EditorApplication.update -= UpdateTargetedChecks;
            IsTargetedUpdateRegistered = false;
        }

        private static bool TryGetEquivalentTargetedCheck(
            string packageId,
            PackageChannel channel,
            string selectedUrl,
            out TargetedUpdateCheckRequest request)
        {
            request = null;

            if (PendingTargetedChecks.TryGetValue(packageId, out TargetedUpdateCheckRequest pending) &&
                pending.Matches(channel, selectedUrl))
            {
                request = pending;
                return true;
            }

            if (ActiveTargetedChecks.TryGetValue(packageId, out TargetedUpdateCheckRequest active) &&
                active.Matches(channel, selectedUrl))
            {
                request = active;
                return true;
            }

            return false;
        }

        private static void CancelTargetedCheck(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            PendingTargetedChecks.Remove(packageId);
            LatestTargetedCheckGenerations.Remove(packageId);
            UnregisterTargetedUpdateIfIdle();
        }

        private static void UpdateTargetedChecks()
        {
            UpdateTargetedChecks(forceStartPending: false);
        }

        private static void UpdateTargetedChecks(bool forceStartPending)
        {
            double now = EditorApplication.timeSinceStartup;
            string[] pendingPackageIds = PendingTargetedChecks.Keys.ToArray();

            foreach (string packageId in pendingPackageIds)
            {
                if (!PendingTargetedChecks.TryGetValue(packageId, out TargetedUpdateCheckRequest request))
                {
                    continue;
                }

                if (ActiveTargetedChecks.ContainsKey(packageId))
                {
                    continue;
                }

                if (!forceStartPending && request.DueTime > now)
                {
                    continue;
                }

                PendingTargetedChecks.Remove(packageId);
                request.Start();
                ActiveTargetedChecks[packageId] = request;
            }

            string[] activePackageIds = ActiveTargetedChecks.Keys.ToArray();

            foreach (string packageId in activePackageIds)
            {
                if (!ActiveTargetedChecks.TryGetValue(packageId, out TargetedUpdateCheckRequest request) ||
                    request.Task == null ||
                    !request.Task.IsCompleted)
                {
                    continue;
                }

                ActiveTargetedChecks.Remove(packageId);
                PackageUpdateStatus status = GetTargetedResult(request);

                if (LatestTargetedCheckGenerations.TryGetValue(packageId, out int latestGeneration) &&
                    latestGeneration == request.Generation)
                {
                    Statuses[packageId] = status;

                    if (ShouldAlwaysLogStatus(status))
                    {
                        LogStatus(status);
                    }

                    NotifySharedStateChanged();
                }
            }

            UnregisterTargetedUpdateIfIdle();
        }

        private static PackageUpdateStatus GetTargetedResult(TargetedUpdateCheckRequest request)
        {
            try
            {
                return request.Task.Result;
            }
            catch (Exception exception)
            {
                return PackageUpdateStatus.Failed(
                    request.Item.PackageDefinition,
                    request.Item.Channel,
                    request.Item.SelectedUrl,
                    string.Empty,
                    "Update check failed: " + exception.GetBaseException().Message);
            }
        }

        internal static void UpdateTargetedChecksForTests(bool forceStartPending)
        {
            UpdateTargetedChecks(forceStartPending);
        }

        internal static void UpdateSharedForTests()
        {
            UpdateShared();
        }

        internal static bool HasTargetedChecksForTests => HasTargetedChecks;

        internal static void ResetForTests()
        {
            Statuses.Clear();
            PendingTargetedChecks.Clear();
            ActiveTargetedChecks.Clear();
            LatestTargetedCheckGenerations.Clear();
            CheckTask = null;
            ActiveCheckItems = Array.Empty<UpdateCheckItem>();
            LastFailureMessageValue = string.Empty;
            LastStatusMessageValue = string.Empty;
            TargetedCheckGeneration = 0;
            GitPackageVersionResolverForTests = null;
            EditorApplication.update -= UpdateShared;
            EditorApplication.update -= UpdateTargetedChecks;
            IsTargetedUpdateRegistered = false;
        }

        private static void RecordCheckCompleted(PackageUpdateStatus[] results)
        {
            results = results ?? Array.Empty<PackageUpdateStatus>();

            foreach (PackageUpdateStatus status in results)
            {
                Statuses[status.PackageId] = status;

                if (ShouldAlwaysLogStatus(status))
                {
                    LogStatus(status);
                }
            }

            LastFailureMessageValue = GetFailureSummary(results);
            LastStatusMessageValue = GetCompletionSummary(results);
            PackageUpdateCheckPreferences.LastCheckedUtc = DateTime.UtcNow;
            CheckTask = null;
            ActiveCheckItems = Array.Empty<UpdateCheckItem>();
            NotifySharedStateChanged();
        }

        private static void RestoreActiveCheckingStatusesToUnknown()
        {
            foreach (UpdateCheckItem item in ActiveCheckItems ?? Array.Empty<UpdateCheckItem>())
            {
                if (item == null ||
                    item.PackageDefinition == null ||
                    string.IsNullOrWhiteSpace(item.PackageDefinition.PackageId))
                {
                    continue;
                }

                if (!Statuses.TryGetValue(item.PackageDefinition.PackageId, out PackageUpdateStatus status) ||
                    status == null ||
                    status.Kind != PackageUpdateStatusKind.Checking ||
                    status.Channel != item.Channel ||
                    !string.Equals(status.SelectedUrl, item.SelectedUrl, StringComparison.Ordinal))
                {
                    continue;
                }

                Statuses[item.PackageDefinition.PackageId] =
                    PackageUpdateStatus.Unknown(item.PackageDefinition, item.Channel);
            }
        }

        private static bool TryParseGitPackageReference(
            string packageReference,
            out string remoteUrl,
            out string reference,
            out string message)
        {
            remoteUrl = string.Empty;
            reference = string.Empty;
            message = string.Empty;

            int hashIndex = packageReference.LastIndexOf('#');

            if (hashIndex < 0 || hashIndex == packageReference.Length - 1)
            {
                message = "Selected package reference is not a Git URL with a branch, tag, or revision.";
                return false;
            }

            string urlWithoutReference = packageReference.Substring(0, hashIndex).Trim();
            reference = packageReference.Substring(hashIndex + 1).Trim();

            int pathIndex = urlWithoutReference.IndexOf("?path=", StringComparison.OrdinalIgnoreCase);
            remoteUrl = pathIndex >= 0
                ? urlWithoutReference.Substring(0, pathIndex)
                : urlWithoutReference;

            if (remoteUrl.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
            {
                remoteUrl = remoteUrl.Substring(4);
            }

            if (string.IsNullOrWhiteSpace(remoteUrl) || !LooksLikeGitUrl(remoteUrl))
            {
                message = "Selected package reference is not a supported Git URL.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(reference))
            {
                message = "Selected Git URL has no branch, tag, or revision.";
                return false;
            }

            return true;
        }

        private static bool LooksLikeGitUrl(string remoteUrl)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                return false;
            }

            return remoteUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                   remoteUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
                   remoteUrl.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetRemoteRevision(
            string remoteUrl,
            string reference,
            out string revision,
            out string message)
        {
            revision = string.Empty;
            message = string.Empty;

            if (IsRevision(reference))
            {
                revision = reference;
                return true;
            }

            string arguments = "ls-remote " + QuoteArgument(remoteUrl) + " " + QuoteArgument(reference);

            if (!RunGit(arguments, out string output, out string error))
            {
                message = error;
                return false;
            }

            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 0 && IsRevision(parts[0]))
                {
                    revision = parts[0];
                    return true;
                }
            }

            message = "Selected Git reference could not be found on the remote.";
            return false;
        }

        private static bool RunGit(string arguments, out string output, out string error)
        {
            output = string.Empty;
            error = string.Empty;

            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                try
                {
                    process.Start();
                }
                catch (Win32Exception)
                {
                    error = "Git executable was not found on PATH.";
                    return false;
                }

                if (!process.WaitForExit(GitTimeoutMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    error = "git ls-remote timed out.";
                    return false;
                }

                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();

                if (process.ExitCode == 0)
                {
                    return true;
                }

                error = string.IsNullOrWhiteSpace(error)
                    ? "git ls-remote failed with exit code " + process.ExitCode + "."
                    : "git ls-remote failed: " + error.Trim();

                return false;
            }
        }

        private static bool TryGetInstalledRevision(UpdateCheckItem item, out string revision)
        {
            if (TryExtractRevision(item.PackageManagerPackageId, out revision))
            {
                return true;
            }

            if (TryExtractRevision(item.InstalledPackageReference, out revision))
            {
                return true;
            }

            foreach (string packageLockPath in item.PackageLockPaths)
            {
                if (TryReadPackageLockRevision(packageLockPath, item.PackageDefinition.PackageId, out revision))
                {
                    return true;
                }
            }

            return TryReadGitHeadRevision(item.ResolvedPath, out revision);
        }

        internal static bool TryReadPackageLockRevision(string packageLockPath, string packageId, out string revision)
        {
            revision = string.Empty;

            if (!PackageLockJsonReader.TryReadPackageObjectBody(
                    packageLockPath,
                    packageId,
                    out string packageBody))
            {
                return false;
            }

            return TryReadJsonField(packageBody, "hash", out revision) ||
                   TryExtractRevision(packageBody, out revision);
        }

        private static bool TryReadJsonField(string jsonBody, string fieldName, out string value)
        {
            value = string.Empty;
            Match match = Regex.Match(
                jsonBody,
                "\"" + Regex.Escape(fieldName) + "\"\\s*:\\s*\"(?<value>[^\"]+)\"",
                RegexOptions.Singleline);

            if (!match.Success)
            {
                return false;
            }

            value = match.Groups["value"].Value.Trim();
            return IsRevision(value);
        }

        private static bool TryReadGitHeadRevision(string resolvedPath, out string revision)
        {
            revision = string.Empty;

            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                return false;
            }

            string gitPath = Path.Combine(resolvedPath, ".git");

            if (!Directory.Exists(gitPath) && !File.Exists(gitPath))
            {
                return false;
            }

            string gitDirectory = gitPath;

            if (File.Exists(gitPath))
            {
                string gitFile = File.ReadAllText(gitPath).Trim();

                if (!gitFile.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string relativeGitDirectory = gitFile.Substring("gitdir:".Length).Trim();
                gitDirectory = Path.GetFullPath(Path.Combine(resolvedPath, relativeGitDirectory));
            }

            string headPath = Path.Combine(gitDirectory, "HEAD");

            if (!File.Exists(headPath))
            {
                return false;
            }

            string head = File.ReadAllText(headPath).Trim();

            if (TryExtractRevision(head, out revision))
            {
                return true;
            }

            const string RefPrefix = "ref:";

            if (!head.StartsWith(RefPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string refPath = Path.Combine(gitDirectory, head.Substring(RefPrefix.Length).Trim().Replace('/', Path.DirectorySeparatorChar));

            return File.Exists(refPath) && TryExtractRevision(File.ReadAllText(refPath), out revision);
        }

        private static bool TryExtractRevision(string value, out string revision)
        {
            revision = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            Match match = ShaRegex.Match(value);

            if (!match.Success)
            {
                return false;
            }

            revision = match.Groups[1].Value;
            return true;
        }

        private static bool IsRevision(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   Regex.IsMatch(value, "^[0-9a-fA-F]{7,40}$");
        }

        internal static bool RevisionsMatch(string installedRevision, string latestRevision)
        {
            string installed = NormalizeRevision(installedRevision);
            string latest = NormalizeRevision(latestRevision);

            if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(latest))
            {
                return false;
            }

            if (string.Equals(installed, latest, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return installed.Length >= 7 &&
                   latest.Length >= 7 &&
                   (installed.StartsWith(latest, StringComparison.OrdinalIgnoreCase) ||
                    latest.StartsWith(installed, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeRevision(string revision)
        {
            return (revision ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string QuoteArgument(string argument)
        {
            return "\"" + (argument ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static IReadOnlyList<string> GetPackageLockPaths()
        {
            string projectRoot = Directory.GetParent(Application.dataPath) != null
                ? Directory.GetParent(Application.dataPath).FullName
                : Application.dataPath;

            string packagesDirectory = Path.Combine(projectRoot, "Packages");

            return new[]
            {
                Path.Combine(packagesDirectory, "packages-lock.json"),
                Path.Combine(packagesDirectory, "package-lock.json")
            };
        }

        private static void LogStatus(PackageUpdateStatus status)
        {
            if (status == null)
            {
                return;
            }

            if (!TryCreateLogMessage(status, out LogType logType, out string message))
            {
                return;
            }

            LogMessage(logType, message);
        }

        internal static void LogStatusForTests(PackageUpdateStatus status)
        {
            LogStatus(status);
        }

        internal static bool TryCreateLogMessage(
            PackageUpdateStatus status,
            out LogType logType,
            out string message)
        {
            logType = GetLogType(status);
            message = string.Empty;

            if (status == null)
            {
                return false;
            }

            if (status.IsUpdateAvailable)
            {
                string prefix = status.Kind == PackageUpdateStatusKind.SwitchAvailable
                    ? "Switch available"
                    : "Update available";
                message = prefix + " for " + status.DisplayName + ": " +
                          status.ShortInstalledRevision + " -> " + status.ShortLatestRevision + ".";
                return true;
            }

            if (status.Kind == PackageUpdateStatusKind.Failed)
            {
                message = "Update check failed for " + status.DisplayName + ": " + status.Message;
                return true;
            }

            if (status.Kind == PackageUpdateStatusKind.SourceMigrationAvailable)
            {
                message = "Source migration available for " + status.DisplayName + ": " + status.Message;
                return true;
            }

            if (status.Kind == PackageUpdateStatusKind.ReloadPending)
            {
                message = "Script reload pending for " + status.DisplayName + ": " + status.Message;
                return true;
            }

            return false;
        }

        internal static LogType GetLogType(PackageUpdateStatus status)
        {
            if (status == null)
            {
                return LogType.Log;
            }

            switch (status.Kind)
            {
                case PackageUpdateStatusKind.Failed:
                    return LogType.Error;
                case PackageUpdateStatusKind.SourceMigrationAvailable:
                case PackageUpdateStatusKind.ReloadPending:
                    return LogType.Warning;
                default:
                    return LogType.Log;
            }
        }

        private static void LogMessage(LogType logType, string message)
        {
            if (logType == LogType.Error)
            {
                PackageInstallerLog.UpdateChecks.Error(message);
                return;
            }

            if (logType == LogType.Warning)
            {
                PackageInstallerLog.UpdateChecks.Warning(message);
                return;
            }

            PackageInstallerLog.UpdateChecks.Info(message);
        }

        private static bool ShouldAlwaysLogStatus(PackageUpdateStatus status)
        {
            return status != null &&
                   (status.IsUpdateAvailable ||
                    status.Kind == PackageUpdateStatusKind.Failed ||
                    status.Kind == PackageUpdateStatusKind.SourceMigrationAvailable ||
                    status.Kind == PackageUpdateStatusKind.ReloadPending);
        }

        internal static bool ShouldLogStatusForTests(PackageUpdateStatus status)
        {
            return ShouldAlwaysLogStatus(status);
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }

        private static void NotifySharedStateChanged()
        {
            SharedStateChanged?.Invoke();
        }

        private static string GetFailureSummary(IEnumerable<PackageUpdateStatus> statuses)
        {
            PackageUpdateStatus[] failures = statuses
                .Where(status => status != null && status.Kind == PackageUpdateStatusKind.Failed)
                .ToArray();

            if (failures.Length == 0)
            {
                return string.Empty;
            }

            if (failures.Length == 1)
            {
                return failures[0].DisplayName + ": " + failures[0].Message;
            }

            return failures.Length + " update checks failed. First: " +
                   failures[0].DisplayName + ": " + failures[0].Message;
        }

        private static string GetCompletionSummary(IEnumerable<PackageUpdateStatus> statuses)
        {
            PackageUpdateStatus[] completedStatuses = (statuses ?? Array.Empty<PackageUpdateStatus>())
                .Where(status => status != null)
                .ToArray();
            int updateCount = completedStatuses.Count(status => status.IsUpdateAvailable);
            int failureCount = completedStatuses.Count(status => status.Kind == PackageUpdateStatusKind.Failed);
            int migrationCount = completedStatuses.Count(status => status.IsSourceMigrationAvailable);
            int reloadPendingCount = completedStatuses.Count(status => status.IsReloadPending);
            List<string> summaryParts = new List<string>
            {
                updateCount + " updates available"
            };

            if (migrationCount > 0)
            {
                summaryParts.Add(migrationCount + " source migration" + (migrationCount == 1 ? string.Empty : "s") + " available");
            }

            if (reloadPendingCount > 0)
            {
                summaryParts.Add(reloadPendingCount + " reload" + (reloadPendingCount == 1 ? string.Empty : "s") + " pending");
            }

            if (failureCount > 0)
            {
                summaryParts.Add(failureCount + " failed");
            }

            return "Checked for updates. " + string.Join(", ", summaryParts.ToArray()) + ".";
        }

        internal static string GetCompletionSummaryForTests(IEnumerable<PackageUpdateStatus> statuses)
        {
            return GetCompletionSummary(statuses);
        }

        internal sealed class PackageVersionResult
        {
            private PackageVersionResult(bool success, string version, string message)
            {
                Success = success;
                Version = version ?? string.Empty;
                Message = message ?? string.Empty;
            }

            public bool Success { get; }

            public string Version { get; }

            public string Message { get; }

            public static PackageVersionResult Ok(string version)
            {
                return new PackageVersionResult(true, version, string.Empty);
            }

            public static PackageVersionResult Fail(string message)
            {
                return new PackageVersionResult(false, string.Empty, message);
            }
        }

        private sealed class UpdateCheckItem
        {
            public UpdateCheckItem(
                PackageDefinition packageDefinition,
                PackageChannel channel,
                string selectedUrl,
                string packageManagerPackageId,
                string resolvedPath,
                string installedPackageReference,
                PackageInstallSourceType sourceType,
                string installedVersion,
                bool hasInstalledChannel,
                PackageChannel installedChannel,
                IReadOnlyList<string> packageLockPaths,
                string runningInstallerVersion,
                PackageInstallerSelfUpdateSnapshot selfUpdateSnapshot)
            {
                PackageDefinition = packageDefinition;
                Channel = channel;
                SelectedUrl = selectedUrl ?? string.Empty;
                PackageManagerPackageId = packageManagerPackageId ?? string.Empty;
                ResolvedPath = resolvedPath ?? string.Empty;
                InstalledPackageReference = installedPackageReference ?? string.Empty;
                SourceType = sourceType;
                InstalledVersion = installedVersion ?? string.Empty;
                HasInstalledChannel = hasInstalledChannel;
                InstalledChannel = installedChannel;
                PackageLockPaths = packageLockPaths ?? Array.Empty<string>();
                RunningInstallerVersion = runningInstallerVersion ?? string.Empty;
                SelfUpdateSnapshot = selfUpdateSnapshot;
            }

            public PackageDefinition PackageDefinition { get; }

            public PackageChannel Channel { get; }

            public string SelectedUrl { get; }

            public string PackageManagerPackageId { get; }

            public string ResolvedPath { get; }

            public string InstalledPackageReference { get; }

            public PackageInstallSourceType SourceType { get; }

            public string InstalledVersion { get; }

            public bool HasInstalledChannel { get; }

            public PackageChannel InstalledChannel { get; }

            public IReadOnlyList<string> PackageLockPaths { get; }

            public string RunningInstallerVersion { get; }

            public PackageInstallerSelfUpdateSnapshot SelfUpdateSnapshot { get; }
        }

        private sealed class TargetedUpdateCheckRequest
        {
            public TargetedUpdateCheckRequest(
                UpdateCheckItem item,
                int generation,
                double dueTime)
            {
                Item = item;
                Generation = generation;
                DueTime = dueTime;
            }

            public UpdateCheckItem Item { get; }

            public int Generation { get; }

            public double DueTime { get; }

            public Task<PackageUpdateStatus> Task { get; private set; }

            public void Start()
            {
                Task = System.Threading.Tasks.Task.Run(() => CheckItem(Item));
            }

            public bool Matches(PackageChannel channel, string selectedUrl)
            {
                return Item != null &&
                       Item.Channel == channel &&
                       string.Equals(Item.SelectedUrl, selectedUrl ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }
}
