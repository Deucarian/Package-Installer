using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageUpdateCheckService
    {

        private static event Action SharedStateChanged;

        public PackageUpdateCheckService(PackageDetectionService packageDetectionService)
            : this(
                packageDetectionService,
                PackageRegistryRemoteFetch.FetchAsync,
                TimeSpan.FromMilliseconds(PackageManifestTimeoutMilliseconds),
                new PackageUpdateCheckCache(),
                new PackageInstallerStateRepository(),
                DefaultCacheEnabled)
        {
        }

        internal PackageUpdateCheckService(
            PackageDetectionService packageDetectionService,
            PackageRegistryRemoteFetchDelegate packageManifestFetcher,
            TimeSpan packageManifestTimeout)
            : this(
                packageDetectionService,
                packageManifestFetcher,
                packageManifestTimeout,
                new PackageUpdateCheckCache(),
                new PackageInstallerStateRepository(),
                DefaultCacheEnabled)
        {
        }

        internal PackageUpdateCheckService(
            PackageDetectionService packageDetectionService,
            PackageRegistryRemoteFetchDelegate packageManifestFetcher,
            TimeSpan packageManifestTimeout,
            PackageUpdateCheckCache updateCheckCache,
            PackageInstallerStateRepository stateRepository)
            : this(
                packageDetectionService,
                packageManifestFetcher,
                packageManifestTimeout,
                updateCheckCache,
                stateRepository,
                enableCache: true)
        {
        }

        private PackageUpdateCheckService(
            PackageDetectionService packageDetectionService,
            PackageRegistryRemoteFetchDelegate packageManifestFetcher,
            TimeSpan packageManifestTimeout,
            PackageUpdateCheckCache updateCheckCache,
            PackageInstallerStateRepository stateRepository,
            bool enableCache)
        {
            _packageDetectionService = packageDetectionService ?? throw new ArgumentNullException(nameof(packageDetectionService));
            _packageManifestFetcher = packageManifestFetcher ?? PackageRegistryRemoteFetch.FetchAsync;
            _packageManifestTimeout = packageManifestTimeout > TimeSpan.Zero
                ? packageManifestTimeout
                : TimeSpan.FromMilliseconds(PackageManifestTimeoutMilliseconds);
            _updateCheckCache = updateCheckCache ?? throw new ArgumentNullException(nameof(updateCheckCache));
            _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
            _packageLockPaths = GetPackageLockPaths();
            _sharedStateChangedHandler = NotifyStateChanged;
            SharedStateChanged += _sharedStateChangedHandler;
            ActiveUpdateCheckCache = enableCache ? _updateCheckCache : null;
            ActiveStateRepository = _stateRepository;
            RestoreCachedState();
        }

        public event Action StateChanged;

        internal static PackageUpdateCheckCachedStatus CaptureCachedStatus()
        {
            return new PackageUpdateCheckCachedStatus(
                Statuses.Values.Count(status => status.IsUpdateAvailable),
                Statuses.Values.Count(status => status.Kind == PackageUpdateStatusKind.Failed),
                IsAnyCheckRunning,
                LastCheckedUtcValue);
        }
        private static void RestoreCachedState()
        {
            if (ActiveUpdateCheckCache == null || ActiveStateRepository == null)
            {
                return;
            }

            string manifestSignature = ActiveStateRepository.GetManifestStateSignature();

            if (!string.Equals(
                    ActiveManifestSignature,
                    manifestSignature,
                    StringComparison.Ordinal))
            {
                Statuses.Clear();
                LastCheckedUtcValue = null;
                LastFailureMessageValue = string.Empty;
                LastStatusMessageValue = string.Empty;
                ActiveManifestSignature = manifestSignature;
            }

            if (IsAnyCheckRunning)
            {
                return;
            }

            if (!ActiveUpdateCheckCache.TryRead(
                    manifestSignature,
                    out PackageUpdateCheckCacheSnapshot snapshot,
                    out string errorMessage))
            {
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    PackageInstallerLog.UpdateChecks.Warning(errorMessage);
                }

                return;
            }

            foreach (PackageUpdateStatus cachedStatus in snapshot.Statuses)
            {
                if (!Statuses.TryGetValue(
                        cachedStatus.PackageId,
                        out PackageUpdateStatus currentStatus) ||
                    !PackageUpdateCheckCache.IsPersistable(currentStatus))
                {
                    Statuses[cachedStatus.PackageId] = cachedStatus;
                }
            }

            if (!LastCheckedUtcValue.HasValue ||
                (snapshot.LastCheckedUtc.HasValue &&
                 snapshot.LastCheckedUtc.Value >= LastCheckedUtcValue.Value))
            {
                LastCheckedUtcValue = snapshot.LastCheckedUtc;
                LastFailureMessageValue = snapshot.LastFailureMessage;
                LastStatusMessageValue = snapshot.LastStatusMessage;
            }
        }

        private static void PersistCachedState()
        {
            if (ActiveUpdateCheckCache == null)
            {
                return;
            }

            string manifestSignature = ActiveStateRepository != null
                ? ActiveStateRepository.GetManifestStateSignature()
                : ActiveManifestSignature;
            ActiveManifestSignature = manifestSignature;

            if (!ActiveUpdateCheckCache.TryWrite(
                    manifestSignature,
                    LastCheckedUtcValue,
                    LastStatusMessageValue,
                    LastFailureMessageValue,
                    Statuses.Values,
                    out string errorMessage) &&
                !string.IsNullOrWhiteSpace(errorMessage))
            {
                PackageInstallerLog.UpdateChecks.Warning(errorMessage);
            }
        }

        private static void DeleteCachedState()
        {
            if (ActiveUpdateCheckCache != null &&
                !ActiveUpdateCheckCache.TryDelete(out string errorMessage) &&
                !string.IsNullOrWhiteSpace(errorMessage))
            {
                PackageInstallerLog.UpdateChecks.Warning(errorMessage);
            }
        }

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
                PersistCachedState();
                NotifySharedStateChanged();
                return;
            }

            if (!_packageDetectionService.TryGetInstalledPackage(packageId, out PackageManagerPackageInfo packageInfo))
            {
                RegisterPackageIntent(packageId, channel, selectedUrl);
                CancelTargetedCheck(packageId);
                Statuses[packageId] = PackageUpdateStatus.NotInstalled(packageDefinition, channel, selectedUrl);
                PersistCachedState();
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
    }
}
