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
            CancellationToken cancellationToken,
            PackageRegistryRemoteFetchDelegate packageManifestFetcher,
            TimeSpan packageManifestTimeout)
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

            string packageJsonUrl = string.Empty;
            bool resolvedPackageJsonUrl =
                PackageGitReference.TryParse(
                    item.SelectedUrl,
                    out PackageGitReference packageReference) &&
                packageReference.TryCreateGitHubPackageJsonUrl(
                    referenceOverride,
                    out packageJsonUrl);
            if (!resolvedPackageJsonUrl &&
                !PackageRegistryPackageNameValidator.TryCreateGitHubPackageJsonUrl(
                    item.SelectedUrl,
                    referenceOverride,
                    out packageJsonUrl))
            {
                return PackageVersionResult.Fail("Could not resolve target package.json URL.");
            }

            return FetchPackageVersion(
                packageJsonUrl,
                cancellationToken,
                packageManifestFetcher,
                packageManifestTimeout);
        }

        private static PackageVersionResult FetchPackageVersion(
            string packageJsonUrl,
            CancellationToken cancellationToken,
            PackageRegistryRemoteFetchDelegate packageManifestFetcher,
            TimeSpan packageManifestTimeout)
        {
            if (string.IsNullOrWhiteSpace(packageJsonUrl))
            {
                return PackageVersionResult.Fail("Cannot fetch package version without a package.json URL.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                PackageRegistryRemoteFetchResponse response =
                    PackageRegistryRemoteFetch.ExecuteAsync(
                            packageManifestFetcher ?? PackageRegistryRemoteFetch.FetchAsync,
                            packageJsonUrl,
                            cancellationToken,
                            packageManifestTimeout)
                        .GetAwaiter()
                        .GetResult();
                cancellationToken.ThrowIfCancellationRequested();
                string packageJson = response != null ? response.Content : string.Empty;

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
            catch (OperationCanceledException)
            {
                throw;
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
            private readonly PackageRegistryRemoteFetchDelegate _packageManifestFetcher;
            private readonly TimeSpan _packageManifestTimeout;
            private readonly ConcurrentDictionary<string, Lazy<RemoteRevisionResult>> _remoteRevisions =
                new ConcurrentDictionary<string, Lazy<RemoteRevisionResult>>(StringComparer.Ordinal);
            private readonly ConcurrentDictionary<string, Lazy<PackageVersionResult>> _packageVersions =
                new ConcurrentDictionary<string, Lazy<PackageVersionResult>>(StringComparer.Ordinal);

            public UpdateCheckRunContext(
                CancellationToken cancellationToken,
                PackageRegistryRemoteFetchDelegate packageManifestFetcher,
                TimeSpan packageManifestTimeout)
            {
                _cancellationToken = cancellationToken;
                _packageManifestFetcher = packageManifestFetcher ?? PackageRegistryRemoteFetch.FetchAsync;
                _packageManifestTimeout = packageManifestTimeout > TimeSpan.Zero
                    ? packageManifestTimeout
                    : TimeSpan.FromMilliseconds(PackageManifestTimeoutMilliseconds);
            }

            public bool TryGetRemoteRevision(
                string remoteUrl,
                string reference,
                out string revision,
                out string message)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                string key = CreateRemoteRevisionProbeKey(
                    remoteUrl,
                    reference,
                    out string normalizedReference);
                Lazy<RemoteRevisionResult> lookup = _remoteRevisions.GetOrAdd(
                    key,
                    _ => new Lazy<RemoteRevisionResult>(
                        () =>
                        {
                            bool success = PackageUpdateCheckService.TryGetRemoteRevision(
                                remoteUrl,
                                normalizedReference,
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
                string key = CreatePackageManifestProbeKey(item, targetRevision);
                return _packageVersions.GetOrAdd(
                        key,
                        _ => new Lazy<PackageVersionResult>(
                            () => PackageUpdateCheckService.ResolveGitPackageVersion(
                                item,
                                targetRevision,
                                _cancellationToken,
                                _packageManifestFetcher,
                                _packageManifestTimeout),
                            LazyThreadSafetyMode.ExecutionAndPublication))
                    .Value;
            }

            private static string CreateRemoteRevisionProbeKey(
                string remoteUrl,
                string reference,
                out string normalizedReference)
            {
                if (PackageGitReference.TryParse(
                        (remoteUrl ?? string.Empty).Trim() + "#" + (reference ?? string.Empty).Trim(),
                        out PackageGitReference packageReference))
                {
                    normalizedReference = packageReference.ReferenceName;
                    return packageReference.RepositoryReferenceIdentity;
                }

                normalizedReference = (reference ?? string.Empty).Trim();
                return NormalizeProbeKey(remoteUrl) + "#" + NormalizeProbeKey(reference);
            }

            private static string CreatePackageManifestProbeKey(
                UpdateCheckItem item,
                string targetRevision)
            {
                string selectedUrl = item != null ? item.SelectedUrl : string.Empty;
                if (PackageGitReference.TryParse(
                        selectedUrl,
                        out PackageGitReference packageReference))
                {
                    return packageReference
                        .WithReferenceName(targetRevision)
                        .PackageReferenceIdentity;
                }

                return NormalizeProbeKey(selectedUrl) + "#" + NormalizeProbeKey(targetRevision);
            }

            private static string NormalizeProbeKey(string value) =>
                (value ?? string.Empty).Trim().Replace('\\', '/');
        }
    }
}
