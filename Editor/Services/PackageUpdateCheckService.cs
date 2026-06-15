using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageUpdateCheckService : IDisposable
    {
        private const int GitTimeoutMilliseconds = 15000;

        private static readonly Regex ShaRegex =
            new Regex("(?<![0-9a-fA-F])([0-9a-fA-F]{40})(?![0-9a-fA-F])", RegexOptions.Compiled);

        private readonly PackageDetectionService _packageDetectionService;
        private static readonly Dictionary<string, PackageUpdateStatus> Statuses =
            new Dictionary<string, PackageUpdateStatus>(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<string> _packageLockPaths;
        private readonly Action _sharedStateChangedHandler;

        private static Task<PackageUpdateStatus[]> CheckTask;
        private static IReadOnlyList<UpdateCheckItem> ActiveCheckItems = Array.Empty<UpdateCheckItem>();
        private static string LastFailureMessageValue = string.Empty;
        private static event Action SharedStateChanged;

        public PackageUpdateCheckService(PackageDetectionService packageDetectionService)
        {
            _packageDetectionService = packageDetectionService ?? throw new ArgumentNullException(nameof(packageDetectionService));
            _packageLockPaths = GetPackageLockPaths();
            _sharedStateChangedHandler = NotifyStateChanged;
            SharedStateChanged += _sharedStateChangedHandler;
        }

        public event Action StateChanged;

        public bool IsChecking => CheckTask != null;

        public static bool IsAnyCheckRunning => CheckTask != null;

        public bool HasStatuses => Statuses.Count > 0;

        public DateTime? LastCheckedUtc => PackageUpdateCheckPreferences.LastCheckedUtc;

        public string LastFailureMessage => LastFailureMessageValue;

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

                _packageDetectionService.TryGetInstalledPackageReference(
                    packageDefinition.PackageId,
                    out string installedPackageReference);

                checkItems.Add(new UpdateCheckItem(
                    packageDefinition,
                    channel,
                    selectedUrl,
                    packageInfo != null ? packageInfo.packageId : string.Empty,
                    packageInfo != null ? packageInfo.resolvedPath : string.Empty,
                    installedPackageReference,
                    _packageLockPaths));
            }

            LastFailureMessageValue = string.Empty;

            if (checkItems.Count == 0)
            {
                PackageInstallerLog.UpdateChecks.Info("No installed registry packages found for update checking.");
                RecordCheckCompleted(Array.Empty<PackageUpdateStatus>());
                return;
            }

            ActiveCheckItems = checkItems;
            CheckTask = Task.Run(() => checkItems.Select(CheckItem).ToArray());

            EditorApplication.update -= UpdateShared;
            EditorApplication.update += UpdateShared;
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

        public void Invalidate(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

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
            LastFailureMessageValue = string.Empty;
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
                packageLockPaths));
        }

        private static PackageUpdateStatus CheckItem(UpdateCheckItem item)
        {
            try
            {
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
                        "The package is installed, but Unity did not expose a Git revision for this package.");
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

                if (RevisionsMatch(installedRevision, latestRevision))
                {
                    return PackageUpdateStatus.UpToDate(
                        item.PackageDefinition,
                        item.Channel,
                        item.SelectedUrl,
                        installedRevision,
                        latestRevision);
                }

                return PackageUpdateStatus.UpdateAvailable(
                    item.PackageDefinition,
                    item.Channel,
                    item.SelectedUrl,
                    installedRevision,
                    latestRevision);
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

        private static void RecordCheckCompleted(PackageUpdateStatus[] results)
        {
            results = results ?? Array.Empty<PackageUpdateStatus>();

            foreach (PackageUpdateStatus status in results)
            {
                Statuses[status.PackageId] = status;
                LogStatus(status);
            }

            LastFailureMessageValue = GetFailureSummary(results);
            PackageUpdateCheckPreferences.LastCheckedUtc = DateTime.UtcNow;
            CheckTask = null;
            ActiveCheckItems = Array.Empty<UpdateCheckItem>();
            NotifySharedStateChanged();
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

            LogType logType = GetLogType(status);
            string message;

            if (status.IsUpdateAvailable)
            {
                message = "Update available for " + status.DisplayName + ": " +
                          status.ShortInstalledRevision + " -> " + status.ShortLatestRevision + ".";
            }
            else if (status.Kind == PackageUpdateStatusKind.Failed)
            {
                message = "Update check failed for " + status.DisplayName + ": " + status.Message;
            }
            else if (status.Kind == PackageUpdateStatusKind.CannotDetermine)
            {
                message = "Update check failed for " + status.DisplayName + ": " + status.Label + ". " + status.Message;
            }
            else
            {
                message = "Update check for " + status.DisplayName + ": " + status.Label + ".";
            }

            LogMessage(logType, message);
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
                case PackageUpdateStatusKind.CannotDetermine:
                    return LogType.Error;
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

            PackageInstallerLog.UpdateChecks.Info(message);
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

        private sealed class UpdateCheckItem
        {
            public UpdateCheckItem(
                PackageDefinition packageDefinition,
                PackageChannel channel,
                string selectedUrl,
                string packageManagerPackageId,
                string resolvedPath,
                string installedPackageReference,
                IReadOnlyList<string> packageLockPaths)
            {
                PackageDefinition = packageDefinition;
                Channel = channel;
                SelectedUrl = selectedUrl ?? string.Empty;
                PackageManagerPackageId = packageManagerPackageId ?? string.Empty;
                ResolvedPath = resolvedPath ?? string.Empty;
                InstalledPackageReference = installedPackageReference ?? string.Empty;
                PackageLockPaths = packageLockPaths ?? Array.Empty<string>();
            }

            public PackageDefinition PackageDefinition { get; }

            public PackageChannel Channel { get; }

            public string SelectedUrl { get; }

            public string PackageManagerPackageId { get; }

            public string ResolvedPath { get; }

            public string InstalledPackageReference { get; }

            public IReadOnlyList<string> PackageLockPaths { get; }
        }
    }
}
