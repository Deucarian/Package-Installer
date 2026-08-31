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
    }
}
