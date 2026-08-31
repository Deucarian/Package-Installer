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
                new UpdateCheckRunContext(
                    CancellationToken.None,
                    PackageRegistryRemoteFetch.FetchAsync,
                    TimeSpan.FromMilliseconds(PackageManifestTimeoutMilliseconds)));
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

                if (RequiresCanonicalStableMigration(item, sourceType))
                {
                    return CheckSourceMigrationItem(item, cancellationToken, context);
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
            string sourceDescription = GetSourceMigrationDescription(item);
            string message = isSelf
                ? "Package Installer is installed from " + sourceDescription +
                  ". Open Bootstrap to migrate it safely to the selected Git channel."
                : item.PackageDefinition.DisplayName +
                  " is installed from " + sourceDescription +
                  ". Migrate it to the selected catalog Git URL.";

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

        private static bool RequiresCanonicalStableMigration(
            UpdateCheckItem item,
            PackageInstallSourceType sourceType)
        {
            if (item == null)
            {
                return false;
            }

            if (sourceType == PackageInstallSourceType.Registry)
            {
                return true;
            }

            if (item.Channel != PackageChannel.Stable)
            {
                return false;
            }

            if (sourceType == PackageInstallSourceType.Local ||
                sourceType == PackageInstallSourceType.Embedded)
            {
                return true;
            }

            return item.HasInstalledChannel &&
                   item.InstalledChannel != PackageChannel.Stable;
        }

        private static string GetSourceMigrationDescription(UpdateCheckItem item)
        {
            if (item == null)
            {
                return "a noncanonical source";
            }

            switch (item.SourceType)
            {
                case PackageInstallSourceType.Registry:
                    return "a registry source";
                case PackageInstallSourceType.Local:
                    return "a local source";
                case PackageInstallSourceType.Embedded:
                    return "an embedded source";
                default:
                    return item.HasInstalledChannel &&
                           item.InstalledChannel == PackageChannel.Development
                        ? "the development Git channel"
                        : "a noncanonical Git source";
            }
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
    }
}
