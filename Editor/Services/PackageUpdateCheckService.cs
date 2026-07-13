using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
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
        private const int MaximumConcurrentChecks = 4;
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
        private static readonly Dictionary<string, PackageCheckIntent> LatestCheckIntents =
            new Dictionary<string, PackageCheckIntent>(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<string> _packageLockPaths;
        private readonly Action _sharedStateChangedHandler;

        private static Task<CompletedCheckResult[]> CheckTask;
        private static IReadOnlyList<ScheduledUpdateCheck> ActiveCheckItems =
            Array.Empty<ScheduledUpdateCheck>();
        private static readonly ConcurrentQueue<CompletedCheckResult> CompletedCheckResults =
            new ConcurrentQueue<CompletedCheckResult>();
        private static readonly SemaphoreSlim CheckConcurrencyGate =
            new SemaphoreSlim(MaximumConcurrentChecks, MaximumConcurrentChecks);
        private static CancellationTokenSource CheckCancellation;
        private static UpdateCheckRunContext SharedCheckContext;
        private static int CheckGeneration;
        private static int ActiveCheckGeneration;
        private static int PublishedCheckResults;
        private static long NextPackageIntentSequence;
        private static readonly HashSet<long> IncrementallyPublishedIntentSequences =
            new HashSet<long>();
        private static bool IsTargetedUpdateRegistered;
        private static string LastFailureMessageValue = string.Empty;
        private static string LastStatusMessageValue = string.Empty;
        private static event Action SharedStateChanged;
        internal static Func<PackageDefinition, PackageChannel, string, PackageVersionResult> GitPackageVersionResolverForTests;
        internal static Func<string, CancellationToken, int, GitProcessResult> GitProcessRunnerForTests;

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

            List<ScheduledUpdateCheck> checkItems = new List<ScheduledUpdateCheck>();

            foreach (PackageDefinition packageDefinition in GetInstallablePackages(packageDefinitions))
            {
                PackageChannel channel = channelSelector != null ? channelSelector(packageDefinition) : PackageChannel.Stable;
                string selectedUrl = packageDefinition.GetUrl(channel);
                PackageCheckIntent intent = RegisterPackageIntent(
                    packageDefinition.PackageId,
                    channel,
                    selectedUrl);

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

                checkItems.Add(new ScheduledUpdateCheck(
                    CreateUpdateCheckItem(
                        packageDefinition,
                        channel,
                        selectedUrl,
                        packageInfo),
                    intent.Sequence));
            }

            LastFailureMessageValue = string.Empty;
            LastStatusMessageValue = "Checking for package updates...";

            if (checkItems.Count == 0)
            {
                RecordCheckCompleted(Array.Empty<PackageUpdateStatus>());
                return;
            }

            ActiveCheckItems = checkItems;
            PublishedCheckResults = 0;
            IncrementallyPublishedIntentSequences.Clear();
            while (CompletedCheckResults.TryDequeue(out _))
            {
            }

            JoinOrCreateSharedCheckDomain();
            ActiveCheckGeneration = CheckGeneration;
            CheckTask = RunCheckBatchAsync(
                checkItems,
                ActiveCheckGeneration,
                CheckCancellation.Token,
                SharedCheckContext);

            EditorApplication.update -= UpdateShared;
            EditorApplication.update += UpdateShared;
            NotifySharedStateChanged();
        }

        public bool CancelCurrentCheck()
        {
            bool hadActiveCheck = IsAnyCheckRunning;

            if (hadActiveCheck)
            {
                RestoreActiveCheckingStatusesToUnknown();
                foreach (TargetedUpdateCheckRequest request in ActiveTargetedChecks.Values)
                {
                    RestoreTargetedCheckingStatusToUnknown(request);
                }
                foreach (TargetedUpdateCheckRequest request in PendingTargetedChecks.Values)
                {
                    RestoreTargetedCheckingStatusToUnknown(request);
                }

                CheckGeneration++;
                CheckCancellation?.Cancel();
                foreach (TargetedUpdateCheckRequest request in ActiveTargetedChecks.Values)
                {
                    request.Cancel();
                }
            }

            CheckTask = null;
            ActiveCheckItems = Array.Empty<ScheduledUpdateCheck>();
            PendingTargetedChecks.Clear();
            ActiveTargetedChecks.Clear();
            LatestCheckIntents.Clear();
            PublishedCheckResults = 0;
            IncrementallyPublishedIntentSequences.Clear();
            while (CompletedCheckResults.TryDequeue(out _))
            {
            }
            CheckCancellation?.Dispose();
            CheckCancellation = null;
            SharedCheckContext = null;
            LastFailureMessageValue = string.Empty;
            LastStatusMessageValue = "Update check canceled.";
            if (hadActiveCheck)
            {
                PackageInstallerActivityService.Record(
                    "Update Check",
                    PackageInstallerActivitySeverity.Warning,
                    LastStatusMessageValue,
                    retryKind: PackageInstallerRetryKind.CheckUpdates);
            }

            EditorApplication.update -= UpdateShared;
            UnregisterTargetedUpdateIfIdle();
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
                RegisterPackageIntent(packageId, channel, selectedUrl);
                CancelTargetedCheck(packageId);
                Statuses[packageId] = PackageUpdateStatus.Unknown(packageDefinition, channel);
                NotifySharedStateChanged();
                return;
            }

            if (!_packageDetectionService.TryGetInstalledPackage(packageId, out PackageManagerPackageInfo packageInfo))
            {
                RegisterPackageIntent(packageId, channel, selectedUrl);
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

                Statuses[packageId] = PackageUpdateStatus.Checking(packageDefinition, channel, selectedUrl);
                NotifySharedStateChanged();
                return;
            }

            if (ActiveTargetedChecks.TryGetValue(packageId, out TargetedUpdateCheckRequest activeRequest) &&
                !activeRequest.Matches(channel, selectedUrl))
            {
                activeRequest.Cancel();
                ActiveTargetedChecks.Remove(packageId);
            }

            if (PendingTargetedChecks.TryGetValue(packageId, out TargetedUpdateCheckRequest pendingRequest))
            {
                pendingRequest.Cancel();
                PendingTargetedChecks.Remove(packageId);
            }

            JoinOrCreateSharedCheckDomain();
            PackageCheckIntent intent = RegisterPackageIntent(packageId, channel, selectedUrl);
            UpdateCheckItem item = CreateUpdateCheckItem(
                packageDefinition,
                channel,
                selectedUrl,
                packageInfo);

            PendingTargetedChecks[packageId] = new TargetedUpdateCheckRequest(
                item,
                intent.Sequence,
                CheckGeneration,
                EditorApplication.timeSinceStartup + TargetedCheckDebounceSeconds,
                CheckCancellation.Token,
                SharedCheckContext);
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
            LatestCheckIntents.Remove(packageId);

            if (Statuses.Remove(packageId))
            {
                NotifySharedStateChanged();
            }
        }

        public void InvalidateAll()
        {
            if (Statuses.Count == 0 &&
                LatestCheckIntents.Count == 0 &&
                !HasTargetedChecks &&
                !IsChecking)
            {
                return;
            }

            bool hadRunningChecks = IsAnyCheckRunning;
            if (hadRunningChecks)
            {
                CheckGeneration++;
                ActiveCheckGeneration = CheckGeneration;
                CheckCancellation?.Cancel();
            }

            foreach (TargetedUpdateCheckRequest activeRequest in ActiveTargetedChecks.Values)
            {
                activeRequest.Cancel();
            }
            foreach (TargetedUpdateCheckRequest pendingRequest in PendingTargetedChecks.Values)
            {
                pendingRequest.Cancel();
            }

            Statuses.Clear();
            CheckTask = null;
            ActiveCheckItems = Array.Empty<ScheduledUpdateCheck>();
            PendingTargetedChecks.Clear();
            ActiveTargetedChecks.Clear();
            LatestCheckIntents.Clear();
            PublishedCheckResults = 0;
            IncrementallyPublishedIntentSequences.Clear();
            while (CompletedCheckResults.TryDequeue(out _))
            {
            }
            CheckCancellation?.Dispose();
            CheckCancellation = null;
            SharedCheckContext = null;
            EditorApplication.update -= UpdateShared;
            UnregisterTargetedUpdateIfIdle();
            LastFailureMessageValue = string.Empty;
            LastStatusMessageValue = string.Empty;
            NotifySharedStateChanged();
        }

        public void Dispose()
        {
            if (IsAnyCheckRunning)
            {
                CancelCurrentCheck();
            }
            SharedStateChanged -= _sharedStateChangedHandler;
        }

        private static void JoinOrCreateSharedCheckDomain()
        {
            if (!IsAnyCheckRunning)
            {
                CheckGeneration++;
                CheckCancellation?.Cancel();
                CheckCancellation?.Dispose();
                CheckCancellation = null;
                SharedCheckContext = null;
            }

            if (CheckCancellation != null &&
                !CheckCancellation.IsCancellationRequested &&
                SharedCheckContext != null)
            {
                return;
            }

            CheckCancellation?.Dispose();
            CheckCancellation = new CancellationTokenSource();
            SharedCheckContext = new UpdateCheckRunContext(CheckCancellation.Token);
        }

        private static PackageCheckIntent RegisterPackageIntent(
            string packageId,
            PackageChannel channel,
            string selectedUrl)
        {
            PackageCheckIntent intent = new PackageCheckIntent(
                ++NextPackageIntentSequence,
                channel,
                selectedUrl);
            LatestCheckIntents[packageId ?? string.Empty] = intent;
            return intent;
        }

        private static bool IsCurrentPackageIntent(
            string packageId,
            long sequence,
            PackageChannel channel,
            string selectedUrl)
        {
            return !string.IsNullOrWhiteSpace(packageId) &&
                   LatestCheckIntents.TryGetValue(packageId, out PackageCheckIntent latest) &&
                   latest.Sequence == sequence &&
                   latest.Channel == channel &&
                   string.Equals(
                       latest.SelectedUrl,
                       selectedUrl ?? string.Empty,
                       StringComparison.Ordinal);
        }

        private static void RestoreTargetedCheckingStatusToUnknown(TargetedUpdateCheckRequest request)
        {
            if (request == null || request.Item == null || request.Item.PackageDefinition == null)
            {
                return;
            }

            string packageId = request.Item.PackageDefinition.PackageId;
            if (Statuses.TryGetValue(packageId, out PackageUpdateStatus status) &&
                status != null &&
                status.Kind == PackageUpdateStatusKind.Checking &&
                status.Channel == request.Item.Channel &&
                string.Equals(status.SelectedUrl, request.Item.SelectedUrl, StringComparison.Ordinal))
            {
                Statuses[packageId] = PackageUpdateStatus.Unknown(
                    request.Item.PackageDefinition,
                    request.Item.Channel);
            }
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
            return CheckItem(
                item,
                CancellationToken.None,
                new UpdateCheckRunContext(CancellationToken.None));
        }

        private static PackageUpdateStatus CheckItem(
            UpdateCheckItem item,
            CancellationToken cancellationToken,
            UpdateCheckRunContext context)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                PackageInstallSourceType sourceType = item.SourceType == PackageInstallSourceType.Unknown
                    ? PackageInstallSourceUtility.Detect(
                        string.Empty,
                        item.PackageManagerPackageId,
                        item.InstalledPackageReference,
                        item.ResolvedPath)
                    : item.SourceType;

                if (sourceType == PackageInstallSourceType.Registry)
                {
                    return CheckSourceMigrationItem(item, cancellationToken, context);
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

                if (!context.TryGetRemoteRevision(
                        remoteUrl,
                        reference,
                        out string latestRevision,
                        out string remoteMessage))
                {
                    return PackageUpdateStatus.Failed(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        installedRevision,
                        remoteMessage);
                }

                PackageVersionResult latestPackageVersionResult =
                    context.ResolveGitPackageVersion(item, latestRevision);
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
            catch (OperationCanceledException)
            {
                throw;
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

        private static PackageUpdateStatus CheckSourceMigrationItem(
            UpdateCheckItem item,
            CancellationToken cancellationToken,
            UpdateCheckRunContext context)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                if (!context.TryGetRemoteRevision(
                        remoteUrl,
                        reference,
                        out latestRevision,
                        out string remoteMessage))
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

            PackageVersionResult latestPackageVersionResult =
                context.ResolveGitPackageVersion(item, latestRevision);
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
            string targetRevision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            return FetchPackageVersion(packageJsonUrl, cancellationToken);
        }

        private static PackageVersionResult FetchPackageVersion(
            string packageJsonUrl,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(packageJsonUrl))
            {
                return PackageVersionResult.Fail("Cannot fetch package version without a package.json URL.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(packageJsonUrl);
                request.Method = "GET";
                request.Timeout = PackageManifestTimeoutMilliseconds;
                request.ReadWriteTimeout = PackageManifestTimeoutMilliseconds;

                using (cancellationToken.Register(request.Abort))
                using (WebResponse response = request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string packageJson = reader.ReadToEnd();
                    cancellationToken.ThrowIfCancellationRequested();

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
            catch (WebException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception exception)
            {
                return PackageVersionResult.Fail(
                    "Could not fetch target package version: " + exception.GetBaseException().Message);
            }
        }

        private static async Task<CompletedCheckResult[]> RunCheckBatchAsync(
            IReadOnlyList<ScheduledUpdateCheck> checkItems,
            int generation,
            CancellationToken cancellationToken,
            UpdateCheckRunContext context)
        {
            Task<CompletedCheckResult>[] tasks = checkItems
                .Select(async scheduled =>
                {
                    PackageUpdateStatus status = await RunCheckWithinSharedBudgetAsync(
                            scheduled.Item,
                            cancellationToken,
                            context)
                        .ConfigureAwait(false);
                    CompletedCheckResult completed = new CompletedCheckResult(
                        generation,
                        scheduled.IntentSequence,
                        status);
                    CompletedCheckResults.Enqueue(completed);
                    return completed;
                })
                .ToArray();

            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task<PackageUpdateStatus> RunCheckWithinSharedBudgetAsync(
            UpdateCheckItem item,
            CancellationToken cancellationToken,
            UpdateCheckRunContext context)
        {
            await CheckConcurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await Task.Run(
                        () => CheckItem(item, cancellationToken, context),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                CheckConcurrencyGate.Release();
            }
        }

        private sealed class UpdateCheckRunContext
        {
            private readonly CancellationToken _cancellationToken;
            private readonly ConcurrentDictionary<string, Lazy<RemoteRevisionResult>> _remoteRevisions =
                new ConcurrentDictionary<string, Lazy<RemoteRevisionResult>>(StringComparer.Ordinal);
            private readonly ConcurrentDictionary<string, Lazy<PackageVersionResult>> _packageVersions =
                new ConcurrentDictionary<string, Lazy<PackageVersionResult>>(StringComparer.Ordinal);

            public UpdateCheckRunContext(CancellationToken cancellationToken)
            {
                _cancellationToken = cancellationToken;
            }

            public bool TryGetRemoteRevision(
                string remoteUrl,
                string reference,
                out string revision,
                out string message)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                string key = NormalizeProbeKey(remoteUrl) + "#" + NormalizeProbeKey(reference);
                Lazy<RemoteRevisionResult> lookup = _remoteRevisions.GetOrAdd(
                    key,
                    _ => new Lazy<RemoteRevisionResult>(
                        () =>
                        {
                            bool success = PackageUpdateCheckService.TryGetRemoteRevision(
                                remoteUrl,
                                reference,
                                _cancellationToken,
                                out string resolvedRevision,
                                out string resolvedMessage);
                            return new RemoteRevisionResult(success, resolvedRevision, resolvedMessage);
                        },
                        LazyThreadSafetyMode.ExecutionAndPublication));

                RemoteRevisionResult result = lookup.Value;
                revision = result.Revision;
                message = result.Message;
                return result.Success;
            }

            public PackageVersionResult ResolveGitPackageVersion(
                UpdateCheckItem item,
                string targetRevision)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                string key = NormalizeProbeKey(item != null ? item.SelectedUrl : string.Empty) +
                             "#" + NormalizeProbeKey(targetRevision);
                return _packageVersions.GetOrAdd(
                        key,
                        _ => new Lazy<PackageVersionResult>(
                            () => PackageUpdateCheckService.ResolveGitPackageVersion(
                                item,
                                targetRevision,
                                _cancellationToken),
                            LazyThreadSafetyMode.ExecutionAndPublication))
                    .Value;
            }

            private static string NormalizeProbeKey(string value) =>
                (value ?? string.Empty).Trim().Replace('\\', '/');
        }

        private sealed class RemoteRevisionResult
        {
            public RemoteRevisionResult(bool success, string revision, string message)
            {
                Success = success;
                Revision = revision ?? string.Empty;
                Message = message ?? string.Empty;
            }

            public bool Success { get; }
            public string Revision { get; }
            public string Message { get; }
        }

        private sealed class CompletedCheckResult
        {
            public CompletedCheckResult(
                int generation,
                long intentSequence,
                PackageUpdateStatus status)
            {
                Generation = generation;
                IntentSequence = intentSequence;
                Status = status;
            }

            public int Generation { get; }
            public long IntentSequence { get; }
            public PackageUpdateStatus Status { get; }
        }

        private static bool CanPublishCompletedResult(CompletedCheckResult completed)
        {
            return completed != null &&
                   completed.Generation == ActiveCheckGeneration &&
                   completed.Generation == CheckGeneration &&
                   completed.Status != null &&
                   IsCurrentPackageIntent(
                       completed.Status.PackageId,
                       completed.IntentSequence,
                       completed.Status.Channel,
                       completed.Status.SelectedUrl);
        }

        private static bool PublishCompletedResult(CompletedCheckResult completed)
        {
            if (!CanPublishCompletedResult(completed) ||
                !IncrementallyPublishedIntentSequences.Add(completed.IntentSequence))
            {
                return false;
            }

            Statuses[completed.Status.PackageId] = completed.Status;
            PublishedCheckResults++;

            if (ShouldAlwaysLogStatus(completed.Status))
            {
                LogStatus(completed.Status);
            }

            return true;
        }

        private static void UpdateShared()
        {
            bool publishedResult = false;
            while (CompletedCheckResults.TryDequeue(out CompletedCheckResult completed))
            {
                publishedResult |= PublishCompletedResult(completed);
            }

            if (publishedResult)
            {
                LastStatusMessageValue = "Checked " + PublishedCheckResults + " of " +
                                         ActiveCheckItems.Count + " packages...";
                NotifySharedStateChanged();
            }

            if (CheckTask == null)
            {
                return;
            }

            if (!CheckTask.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= UpdateShared;

            CompletedCheckResult[] completedResults;

            try
            {
                completedResults = CheckTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                completedResults = ActiveCheckItems
                    .Select(scheduled => new CompletedCheckResult(
                        ActiveCheckGeneration,
                        scheduled.IntentSequence,
                        PackageUpdateStatus.Failed(
                            scheduled.Item.PackageDefinition,
                            scheduled.Item.Channel,
                            scheduled.Item.SelectedUrl,
                            string.Empty,
                            "Update check failed: " + exception.GetBaseException().Message)))
                    .ToArray();
            }

            if (ActiveCheckGeneration != CheckGeneration)
            {
                return;
            }

            foreach (CompletedCheckResult completed in completedResults)
            {
                PublishCompletedResult(completed);
            }

            PackageUpdateStatus[] acceptedResults = completedResults
                .Where(CanPublishCompletedResult)
                .Select(completed => completed.Status)
                .ToArray();
            RecordCheckCompleted(acceptedResults);
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
                pending.DomainGeneration == CheckGeneration &&
                IsCurrentPackageIntent(
                    packageId,
                    pending.IntentSequence,
                    pending.Item.Channel,
                    pending.Item.SelectedUrl) &&
                pending.Matches(channel, selectedUrl))
            {
                request = pending;
                return true;
            }

            if (ActiveTargetedChecks.TryGetValue(packageId, out TargetedUpdateCheckRequest active) &&
                active.DomainGeneration == CheckGeneration &&
                IsCurrentPackageIntent(
                    packageId,
                    active.IntentSequence,
                    active.Item.Channel,
                    active.Item.SelectedUrl) &&
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

            if (PendingTargetedChecks.TryGetValue(packageId, out TargetedUpdateCheckRequest pending))
            {
                pending.Cancel();
                PendingTargetedChecks.Remove(packageId);
            }
            if (ActiveTargetedChecks.TryGetValue(packageId, out TargetedUpdateCheckRequest active))
            {
                active.Cancel();
                ActiveTargetedChecks.Remove(packageId);
            }

            if (!IsAnyCheckRunning && CheckCancellation != null)
            {
                CheckGeneration++;
                CheckCancellation.Cancel();
                CheckCancellation.Dispose();
                CheckCancellation = null;
                SharedCheckContext = null;
            }
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

                if (request.DomainGeneration != CheckGeneration)
                {
                    PendingTargetedChecks.Remove(packageId);
                    request.Cancel();
                    RestoreTargetedCheckingStatusToUnknown(request);
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

                if (request.DomainGeneration == CheckGeneration &&
                    IsCurrentPackageIntent(
                        packageId,
                        request.IntentSequence,
                        request.Item.Channel,
                        request.Item.SelectedUrl))
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
            catch (AggregateException exception)
                when (exception.InnerExceptions.Any(inner => inner is OperationCanceledException))
            {
                return PackageUpdateStatus.Unknown(
                    request.Item.PackageDefinition,
                    request.Item.Channel);
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
            CheckCancellation?.Cancel();
            CheckCancellation?.Dispose();
            CheckCancellation = null;
            SharedCheckContext = null;
            foreach (TargetedUpdateCheckRequest request in ActiveTargetedChecks.Values)
            {
                request.Cancel();
            }
            foreach (TargetedUpdateCheckRequest request in PendingTargetedChecks.Values)
            {
                request.Cancel();
            }
            Statuses.Clear();
            PendingTargetedChecks.Clear();
            ActiveTargetedChecks.Clear();
            LatestCheckIntents.Clear();
            CheckTask = null;
            ActiveCheckItems = Array.Empty<ScheduledUpdateCheck>();
            while (CompletedCheckResults.TryDequeue(out _))
            {
            }
            LastFailureMessageValue = string.Empty;
            LastStatusMessageValue = string.Empty;
            CheckGeneration++;
            ActiveCheckGeneration = CheckGeneration;
            PublishedCheckResults = 0;
            IncrementallyPublishedIntentSequences.Clear();
            GitPackageVersionResolverForTests = null;
            GitProcessRunnerForTests = null;
            EditorApplication.update -= UpdateShared;
            EditorApplication.update -= UpdateTargetedChecks;
            IsTargetedUpdateRegistered = false;
        }

        private static void RecordCheckCompleted(PackageUpdateStatus[] results)
        {
            results = results ?? Array.Empty<PackageUpdateStatus>();

            LastFailureMessageValue = GetFailureSummary(results);
            LastStatusMessageValue = GetCompletionSummary(results);
            PackageInstallerActivityService.Record(
                "Update Check",
                string.IsNullOrWhiteSpace(LastFailureMessageValue)
                    ? PackageInstallerActivitySeverity.Success
                    : PackageInstallerActivitySeverity.Error,
                string.IsNullOrWhiteSpace(LastFailureMessageValue)
                    ? LastStatusMessageValue
                    : LastFailureMessageValue,
                LastStatusMessageValue,
                retryKind: string.IsNullOrWhiteSpace(LastFailureMessageValue)
                    ? PackageInstallerRetryKind.None
                    : PackageInstallerRetryKind.CheckUpdates);
            PackageUpdateCheckPreferences.LastCheckedUtc = DateTime.UtcNow;
            CheckTask = null;
            ActiveCheckItems = Array.Empty<ScheduledUpdateCheck>();
            PublishedCheckResults = 0;
            IncrementallyPublishedIntentSequences.Clear();
            NotifySharedStateChanged();
        }

        private static void RestoreActiveCheckingStatusesToUnknown()
        {
            foreach (ScheduledUpdateCheck scheduled in
                     ActiveCheckItems ?? Array.Empty<ScheduledUpdateCheck>())
            {
                UpdateCheckItem item = scheduled.Item;
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
            CancellationToken cancellationToken,
            out string revision,
            out string message)
        {
            cancellationToken.ThrowIfCancellationRequested();
            revision = string.Empty;
            message = string.Empty;

            if (IsRevision(reference))
            {
                revision = reference;
                return true;
            }

            string arguments = "ls-remote " + QuoteArgument(remoteUrl) + " " + QuoteArgument(reference);

            if (!RunGit(arguments, cancellationToken, out string output, out string error))
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

        internal static bool TryGetRemoteRevisionForTests(
            string remoteUrl,
            string reference,
            CancellationToken cancellationToken,
            out string revision,
            out string message)
        {
            return TryGetRemoteRevision(
                remoteUrl,
                reference,
                cancellationToken,
                out revision,
                out message);
        }

        private static bool RunGit(
            string arguments,
            CancellationToken cancellationToken,
            out string output,
            out string error)
        {
            Func<string, CancellationToken, int, GitProcessResult> runner =
                GitProcessRunnerForTests;
            GitProcessResult result = runner != null
                ? runner(arguments, cancellationToken, GitTimeoutMilliseconds)
                : RunOwnedGitProcess(arguments, cancellationToken, GitTimeoutMilliseconds);
            output = result != null ? result.Output : string.Empty;
            error = result != null
                ? result.Error
                : "git ls-remote returned no process result.";
            return result != null && result.Success;
        }

        private static GitProcessResult RunOwnedGitProcess(
            string arguments,
            CancellationToken cancellationToken,
            int timeoutMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                    cancellationToken.ThrowIfCancellationRequested();
                    process.Start();
                }
                catch (Win32Exception)
                {
                    return GitProcessResult.Fail("Git executable was not found on PATH.");
                }

                using (cancellationToken.Register(() => TryKillProcess(process)))
                {
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        TryKillProcess(process);
                        return GitProcessResult.Fail("git ls-remote timed out.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (process.ExitCode == 0)
                {
                    return GitProcessResult.Ok(output);
                }

                error = string.IsNullOrWhiteSpace(error)
                    ? "git ls-remote failed with exit code " + process.ExitCode + "."
                    : "git ls-remote failed: " + error.Trim();

                return GitProcessResult.Fail(error, output);
            }
        }

        private static void TryKillProcess(Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
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

        internal sealed class GitProcessResult
        {
            private GitProcessResult(bool success, string output, string error)
            {
                Success = success;
                Output = output ?? string.Empty;
                Error = error ?? string.Empty;
            }

            public bool Success { get; }

            public string Output { get; }

            public string Error { get; }

            public static GitProcessResult Ok(string output)
            {
                return new GitProcessResult(true, output, string.Empty);
            }

            public static GitProcessResult Fail(string error, string output = null)
            {
                return new GitProcessResult(false, output, error);
            }
        }

        private sealed class PackageCheckIntent
        {
            public PackageCheckIntent(
                long sequence,
                PackageChannel channel,
                string selectedUrl)
            {
                Sequence = sequence;
                Channel = channel;
                SelectedUrl = selectedUrl ?? string.Empty;
            }

            public long Sequence { get; }
            public PackageChannel Channel { get; }
            public string SelectedUrl { get; }
        }

        private sealed class ScheduledUpdateCheck
        {
            public ScheduledUpdateCheck(UpdateCheckItem item, long intentSequence)
            {
                Item = item ?? throw new ArgumentNullException(nameof(item));
                IntentSequence = intentSequence;
            }

            public UpdateCheckItem Item { get; }
            public long IntentSequence { get; }
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
            private readonly UpdateCheckRunContext _context;

            public TargetedUpdateCheckRequest(
                UpdateCheckItem item,
                long intentSequence,
                int domainGeneration,
                double dueTime,
                CancellationToken domainCancellationToken,
                UpdateCheckRunContext context)
            {
                Item = item;
                IntentSequence = intentSequence;
                DomainGeneration = domainGeneration;
                DueTime = dueTime;
                _context = context ?? throw new ArgumentNullException(nameof(context));
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    domainCancellationToken);
            }

            public UpdateCheckItem Item { get; }

            public long IntentSequence { get; }

            public int DomainGeneration { get; }

            public double DueTime { get; }

            public Task<PackageUpdateStatus> Task { get; private set; }

            private CancellationTokenSource Cancellation { get; }

            public void Start()
            {
                CancellationToken token = Cancellation.Token;
                Task = RunCheckWithinSharedBudgetAsync(Item, token, _context);
            }

            public void Cancel()
            {
                Cancellation?.Cancel();
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
