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
