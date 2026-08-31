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
                PersistCachedState();
                NotifySharedStateChanged();
            }
        }

        public void InvalidateAll()
        {
            InvalidateAll(deletePersistedState: true);
        }

        public void PrepareForUpdateCheck()
        {
            InvalidateAll(deletePersistedState: false);
        }

        private void InvalidateAll(bool deletePersistedState)
        {
            if (Statuses.Count == 0 &&
                LatestCheckIntents.Count == 0 &&
                !HasTargetedChecks &&
                !IsChecking)
            {
                if (deletePersistedState)
                {
                    LastCheckedUtcValue = null;
                    LastFailureMessageValue = string.Empty;
                    LastStatusMessageValue = string.Empty;
                    DeleteCachedState();
                }

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

            if (deletePersistedState)
            {
                LastCheckedUtcValue = null;
                DeleteCachedState();
            }

            NotifySharedStateChanged();
        }

        public bool InvalidateIfManifestStateChanged()
        {
            string manifestSignature = _stateRepository.GetManifestStateSignature();

            if (string.Equals(
                    ActiveManifestSignature,
                    manifestSignature,
                    StringComparison.Ordinal))
            {
                return false;
            }

            ActiveManifestSignature = manifestSignature;
            InvalidateAll();
            return true;
        }

        public void ReconcileCachedStatuses(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            ReconcileCachedStatuses(
                packageDefinitions,
                channelSelector,
                requireSuccessfulDetection: true);
        }

        internal void ReconcileCachedStatusesForTests(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            ReconcileCachedStatuses(
                packageDefinitions,
                channelSelector,
                requireSuccessfulDetection: false);
        }

        private void ReconcileCachedStatuses(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            bool requireSuccessfulDetection)
        {
            if (IsAnyCheckRunning ||
                (requireSuccessfulDetection && !_packageDetectionService.HasSuccessfulRefresh))
            {
                return;
            }

            Dictionary<string, PackageDefinition> packagesById =
                new Dictionary<string, PackageDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageDefinition packageDefinition in
                     packageDefinitions ?? Array.Empty<PackageDefinition>())
            {
                if (packageDefinition != null &&
                    !string.IsNullOrWhiteSpace(packageDefinition.PackageId))
                {
                    packagesById[packageDefinition.PackageId] = packageDefinition;
                }
            }

            bool changed = false;

            foreach (string packageId in Statuses.Keys.ToArray())
            {
                PackageUpdateStatus status = Statuses[packageId];

                if (!PackageUpdateCheckCache.IsPersistable(status))
                {
                    continue;
                }

                if (!packagesById.TryGetValue(packageId, out PackageDefinition packageDefinition))
                {
                    Statuses.Remove(packageId);
                    changed = true;
                    continue;
                }

                PackageChannel channel = channelSelector != null
                    ? channelSelector(packageDefinition)
                    : PackageChannel.Stable;
                string selectedUrl = packageDefinition.GetUrl(channel);

                if (status.Channel != channel ||
                    !string.Equals(status.SelectedUrl, selectedUrl, StringComparison.Ordinal))
                {
                    Statuses.Remove(packageId);
                    changed = true;
                    continue;
                }

                if (!string.Equals(
                        status.DisplayName,
                        packageDefinition.DisplayName,
                        StringComparison.Ordinal))
                {
                    Statuses[packageId] = status.WithPackageDefinition(packageDefinition);
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            PersistCachedState();
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

        private void JoinOrCreateSharedCheckDomain()
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
            SharedCheckContext = new UpdateCheckRunContext(
                CheckCancellation.Token,
                _packageManifestFetcher,
                _packageManifestTimeout);
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
    }
}
