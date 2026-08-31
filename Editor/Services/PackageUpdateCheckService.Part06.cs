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
            LastCheckedUtcValue = DateTime.UtcNow;
            CheckTask = null;
            ActiveCheckItems = Array.Empty<ScheduledUpdateCheck>();
            PublishedCheckResults = 0;
            IncrementallyPublishedIntentSequences.Clear();
            PersistCachedState();
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
    }
}
