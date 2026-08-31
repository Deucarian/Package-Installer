using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {


        private static float CalculateOperationDrawerScrollHeight(int contentLineCount)
        {
            const float lineHeight = 18f;
            float verticalPadding = OperationBlockPadding;
            int lineCount = Mathf.Max(1, contentLineCount);
            float contentHeight = lineCount * lineHeight + verticalPadding;

            return Mathf.Clamp(contentHeight, OperationDrawerMinHeight, OperationDrawerMaxHeight);
        }

        private static string GetFooterVersionText()
        {
            return PackageInstallerRuntimeIdentity.PackageId + " " + PackageInstallerRuntimeIdentity.Version;
        }

        private int GetOperationDrawerContentLineCount()
        {
            return CountOperationMessageLines(GetOperationDrawerReportText());
        }

        private string GetOperationDrawerReportText()
        {
            IReadOnlyList<PackageInstallerActivityEntry> recentActivity =
                PackageInstallerActivityService.Recent;
            List<string> lines = new List<string>();
            bool packageOperationActive = _packageInstallService != null && _packageInstallService.IsBusy;
            bool liveOperationActive = packageOperationActive ||
                                       (_packageSampleImportService != null && _packageSampleImportService.IsBusy) ||
                                       (_packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking) ||
                                       (_packageDetectionService != null && _packageDetectionService.IsRefreshing);
            string summary = liveOperationActive ? GetLastOperationSummary() : string.Empty;

            if (!string.IsNullOrWhiteSpace(summary))
            {
                lines.Add(summary.Trim());
            }

            IReadOnlyList<string> operationMessages = packageOperationActive
                ? GetLastOperationMessages()
                : Array.Empty<string>();

            if (operationMessages != null)
            {
                foreach (string message in operationMessages)
                {
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        lines.Add(message.Trim());
                    }
                }
            }

            IReadOnlyList<PackageInstallProgressItem> progressItems = packageOperationActive
                ? GetLastProgressItems()
                : Array.Empty<PackageInstallProgressItem>();

            if (progressItems != null)
            {
                foreach (PackageInstallProgressItem item in progressItems)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    string itemMessage = string.IsNullOrWhiteSpace(item.Message)
                        ? item.DisplayName
                        : item.Message;
                    string line = GetProgressItemStateLabel(item.State) + ": " + itemMessage;

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        lines.Add(line.Trim());
                    }
                }
            }

            return MergeLiveOperationWithActivity(
                lines.Count == 0 ? string.Empty : string.Join("\n", lines.ToArray()),
                recentActivity);
        }

        internal static string MergeLiveOperationWithActivityForTests(
            string liveReport,
            IReadOnlyList<PackageInstallerActivityEntry> activity)
        {
            return MergeLiveOperationWithActivity(liveReport, activity);
        }

        private static string MergeLiveOperationWithActivity(
            string liveReport,
            IReadOnlyList<PackageInstallerActivityEntry> activity)
        {
            string live = (liveReport ?? string.Empty).Trim();
            string history = activity != null && activity.Count > 0
                ? FormatActivityReport(activity).Trim()
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(live) && !string.IsNullOrWhiteSpace(history))
            {
                return "Current\n" + live + "\n\nHistory\n" + history;
            }

            if (!string.IsNullOrWhiteSpace(live))
            {
                return "Current\n" + live;
            }

            if (!string.IsNullOrWhiteSpace(history))
            {
                return "History\n" + history;
            }

            return "No detailed operation report is available.";
        }

        internal static string FormatActivityReportForTests(
            IReadOnlyList<PackageInstallerActivityEntry> entries)
        {
            return FormatActivityReport(entries);
        }

        private static string FormatActivityReport(
            IReadOnlyList<PackageInstallerActivityEntry> entries)
        {
            PackageInstallerActivityEntry[] visibleEntries = (entries ??
                    Array.Empty<PackageInstallerActivityEntry>())
                .Where(entry => entry != null)
                .Skip(Math.Max(0, (entries?.Count ?? 0) - 20))
                .ToArray();
            if (visibleEntries.Length == 0)
            {
                return "No activity has been recorded yet.";
            }

            List<string> lines = new List<string>();
            foreach (PackageInstallerActivityEntry entry in visibleEntries)
            {
                lines.Add(
                    entry.TimestampUtc.ToString("u") +
                    " | " + entry.Source +
                    " | " + entry.Severity +
                    " | " + entry.Summary);
                if (!string.IsNullOrWhiteSpace(entry.Details) &&
                    !string.Equals(entry.Details, entry.Summary, StringComparison.Ordinal))
                {
                    lines.Add(entry.Details.Trim());
                }
            }

            return string.Join("\n", lines.ToArray());
        }

        private static int CountOperationMessageLines(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return 0;
            }

            return Mathf.Max(
                1,
                message
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Length);
        }

        private OperationProgressView GetCurrentOperationProgress()
        {
            if (_packageInstallService.IsBusy)
            {
                return new OperationProgressView
                {
                    Title = "Package Operation",
                    OperationName = string.IsNullOrWhiteSpace(_packageInstallService.CurrentOperationName)
                        ? "Package Operation"
                        : _packageInstallService.CurrentOperationName,
                    CurrentItem = _packageInstallService.CurrentPackageName,
                    Message = _packageInstallService.LastStatusMessage,
                    ErrorMessage = _packageInstallService.LastErrorMessage,
                    CompletedSteps = _packageInstallService.CompletedSteps,
                    TotalSteps = _packageInstallService.TotalSteps,
                    FailedSteps = _packageInstallService.FailedSteps,
                    IsBusy = _packageInstallService.IsBusy,
                    ProgressItems = _packageInstallService.ProgressItems
                };
            }

            if (_packageSampleImportService.IsBusy)
            {
                return new OperationProgressView
                {
                    Title = "Sample Import",
                    OperationName = string.IsNullOrWhiteSpace(_packageSampleImportService.CurrentOperationName)
                        ? "Import Sample"
                        : _packageSampleImportService.CurrentOperationName,
                    CurrentItem = _packageSampleImportService.CurrentExtraName,
                    Message = _packageSampleImportService.LastStatusMessage,
                    ErrorMessage = _packageSampleImportService.LastErrorMessage,
                    CompletedSteps = 0,
                    TotalSteps = 1,
                    IsBusy = true
                };
            }

            if (_packageUpdateCheckService.IsChecking)
            {
                return new OperationProgressView
                {
                    Title = "Update Check",
                    OperationName = "Checking for package updates",
                    Message = "Resolving selected Git references...",
                    CompletedSteps = 0,
                    TotalSteps = 1,
                    IsBusy = true
                };
            }

            if (_packageDetectionService.IsRefreshing)
            {
                return new OperationProgressView
                {
                    Title = "Refresh",
                    OperationName = "Refreshing installed packages",
                    Message = "Reading Unity Package Manager state...",
                    CompletedSteps = 0,
                    TotalSteps = 1,
                    IsBusy = true
                };
            }

            return null;
        }

        private string GetGlobalOperationStateLabel(OperationProgressView operation)
        {
            if (operation != null)
            {
                if (operation.FailedSteps > 0 && !operation.IsBusy)
                {
                    return "Failed";
                }

                if (_packageUpdateCheckService.IsChecking)
                {
                    return "Checking for updates";
                }

                if (_packageDetectionService.IsRefreshing)
                {
                    return "Refreshing";
                }

                if (_packageSampleImportService.IsBusy)
                {
                    return "Installing";
                }

                if (_packageInstallService.State == PackageInstallRequestState.Removing)
                {
                    return "Removing";
                }

                return IsUpdateOperation(operation.OperationName) ? "Updating" : "Installing";
            }

            if (HasLastOperationFailure())
            {
                return "Failed";
            }

            if (HasLastOperationDetails())
            {
                return "Complete";
            }

            return "Idle";
        }

        private VisualStatusKind GetGlobalOperationStatusKind(OperationProgressView operation)
        {
            string stateLabel = GetGlobalOperationStateLabel(operation);

            if (string.Equals(stateLabel, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return VisualStatusKind.Failed;
            }

            if (string.Equals(stateLabel, "Idle", StringComparison.OrdinalIgnoreCase))
            {
                return VisualStatusKind.Info;
            }

            if (string.Equals(stateLabel, "Complete", StringComparison.OrdinalIgnoreCase))
            {
                return VisualStatusKind.Installed;
            }

            return VisualStatusKind.Busy;
        }

        private string GetOperationBarTitle(OperationProgressView operation)
        {
            if (operation != null)
            {
                return string.IsNullOrWhiteSpace(operation.OperationName)
                    ? GetGlobalOperationStateLabel(operation)
                    : operation.OperationName;
            }

            if (HasLastOperationFailure())
            {
                return "Last operation failed.";
            }

            if (HasLastOperationDetails())
            {
                return "Last operation complete.";
            }

            return "No operation running.";
        }

        private string GetOperationBarSubtitle(OperationProgressView operation)
        {
            if (operation != null)
            {
                if (!string.IsNullOrWhiteSpace(operation.ErrorMessage))
                {
                    return operation.ErrorMessage;
                }

                string progressStepText = GetProgressStepText(operation);

                if (!string.IsNullOrWhiteSpace(progressStepText))
                {
                    return progressStepText;
                }

                return operation.Message;
            }

            return GetLastOperationSummary();
        }

        private bool HasLastOperationDetails()
        {
            if (PackageInstallerActivityService.Recent.Count > 0)
            {
                return true;
            }

            IReadOnlyList<PackageInstallProgressItem> progressItems = GetLastProgressItems();
            IReadOnlyList<string> operationMessages = GetLastOperationMessages();

            return !string.IsNullOrWhiteSpace(GetLastOperationSummary()) ||
                   (operationMessages != null && operationMessages.Count > 0) ||
                   (progressItems != null && progressItems.Count > 0);
        }

        private bool HasLastOperationFailure()
        {
            PackageInstallerActivityEntry latestActivity = PackageInstallerActivityService.Latest;
            if (latestActivity != null)
            {
                return latestActivity.Severity == PackageInstallerActivitySeverity.Error;
            }

            IReadOnlyList<PackageInstallProgressItem> progressItems = GetLastProgressItems();

            return !string.IsNullOrWhiteSpace(_packageSampleImportService.LastErrorMessage) ||
                   !string.IsNullOrWhiteSpace(_packageInstallService.LastErrorMessage) ||
                   !string.IsNullOrWhiteSpace(_packageUpdateCheckService.LastFailureMessage) ||
                   (progressItems != null && progressItems.Any(item => item.State == PackageInstallProgressItemState.Failed));
        }

        private static bool IsUpdateOperation(string operationName)
        {
            return !string.IsNullOrWhiteSpace(operationName) &&
                   operationName.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetOperationDetailsExpanded(bool expanded)
        {
            _operationDetailsExpanded = expanded;
            EditorPrefs.SetBool(GetOperationDrawerPreferenceKey(), expanded);
            UpdateViewVisibility();
            _operationDrawerContainer?.MarkDirtyRepaint();
            RefreshOperationDrawerContent();
            UpdateOperationFooter();
            Repaint();
        }
    }
}
