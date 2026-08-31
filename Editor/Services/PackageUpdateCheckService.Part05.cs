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

                    PersistCachedState();
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

        internal static void SetDefaultCacheEnabledForTests(bool enabled)
        {
            DefaultCacheEnabled = enabled;
        }

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
            LastCheckedUtcValue = null;
            ActiveManifestSignature = string.Empty;
            ActiveUpdateCheckCache = null;
            ActiveStateRepository = null;
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
    }
}
