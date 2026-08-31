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

    internal sealed partial class PackageUpdateCheckService : IDisposable
    {
        private const int GitTimeoutMilliseconds = 15000;
        private const int PackageManifestTimeoutMilliseconds = 15000;
        private const int MaximumConcurrentChecks = 4;
        private const double TargetedCheckDebounceSeconds = 0.15d;

        private static readonly Regex ShaRegex =
            new Regex("(?<![0-9a-fA-F])([0-9a-fA-F]{40})(?![0-9a-fA-F])", RegexOptions.Compiled);

        private readonly PackageDetectionService _packageDetectionService;
        private readonly PackageRegistryRemoteFetchDelegate _packageManifestFetcher;
        private readonly TimeSpan _packageManifestTimeout;
        private readonly PackageUpdateCheckCache _updateCheckCache;
        private readonly PackageInstallerStateRepository _stateRepository;
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
        private static DateTime? LastCheckedUtcValue;
        private static string ActiveManifestSignature = string.Empty;
        private static PackageUpdateCheckCache ActiveUpdateCheckCache;
        private static PackageInstallerStateRepository ActiveStateRepository;
        private static bool DefaultCacheEnabled = true;
        internal static Func<PackageDefinition, PackageChannel, string, PackageVersionResult> GitPackageVersionResolverForTests;
        internal static Func<string, CancellationToken, int, GitProcessResult> GitProcessRunnerForTests;

        public bool IsChecking => CheckTask != null;

        public static bool IsAnyCheckRunning => CheckTask != null || HasTargetedChecks;

        private static bool HasTargetedChecks =>
            PendingTargetedChecks.Count > 0 ||
            ActiveTargetedChecks.Count > 0;

        public bool HasStatuses => Statuses.Count > 0;

        public DateTime? LastCheckedUtc => LastCheckedUtcValue;

        public string LastFailureMessage => LastFailureMessageValue;

        public string LastStatusMessage => LastStatusMessageValue;

        internal static bool HasTargetedChecksForTests => HasTargetedChecks;
    }
}
