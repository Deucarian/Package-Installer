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

        private string GetProgressStepText(OperationProgressView operation)
        {
            if (operation == null || operation.TotalSteps <= 0)
            {
                return string.Empty;
            }

            int activeStep = Mathf.Clamp(
                operation.CompletedSteps + (operation.IsBusy ? 1 : 0),
                1,
                Mathf.Max(operation.TotalSteps, 1));
            string stepText = "Step " + activeStep + " / " + operation.TotalSteps;

            if (!string.IsNullOrWhiteSpace(operation.CurrentItem))
            {
                stepText += ": " + operation.CurrentItem;
            }

            return stepText;
        }

        private static float GetOperationProgress(OperationProgressView operation)
        {
            if (operation == null || operation.TotalSteps <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp01(operation.CompletedSteps / (float)Mathf.Max(operation.TotalSteps, 1));
        }

        private IReadOnlyList<PackageInstallProgressItem> GetLastProgressItems()
        {
            if (_packageInstallService.HasProgress)
            {
                return _packageInstallService.ProgressItems;
            }

            return Array.Empty<PackageInstallProgressItem>();
        }

        private IReadOnlyList<string> GetLastOperationMessages()
        {
            if (_packageInstallService.HasProgress)
            {
                return _packageInstallService.OperationMessages;
            }

            return Array.Empty<string>();
        }

        private PackageInstallerVisualStatusKind GetLastSummaryStatusKind(IReadOnlyList<PackageInstallProgressItem> progressItems)
        {
            if (progressItems != null && progressItems.Any(item => item.State == PackageInstallProgressItemState.Failed))
            {
                return PackageInstallerVisualStatusKind.Failed;
            }

            if (_packageSampleImportService.LastErrorMessage.Length > 0)
            {
                return PackageInstallerVisualStatusKind.Failed;
            }

            if (IsAnyOperationBusy())
            {
                return PackageInstallerVisualStatusKind.Busy;
            }

            return PackageInstallerVisualStatusKind.Installed;
        }

        private static string GetLastSummaryStatusLabel(PackageInstallerVisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case PackageInstallerVisualStatusKind.Failed:
                    return "Failed";
                case PackageInstallerVisualStatusKind.Busy:
                    return "Running";
                default:
                    return "Complete";
            }
        }

        private static string GetProgressItemStateLabel(PackageInstallProgressItemState state)
        {
            switch (state)
            {
                case PackageInstallProgressItemState.Active:
                    return "Active";
                case PackageInstallProgressItemState.Completed:
                    return "Completed";
                case PackageInstallProgressItemState.Failed:
                    return "Failed";
                case PackageInstallProgressItemState.Skipped:
                    return "Skipped";
                default:
                    return "Pending";
            }
        }

        private VisualStatus GetPackageVisualStatus(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return new VisualStatus(DeucarianEditorIconIds.Info, "Unknown", PackageInstallerVisualStatusKind.Info);
            }

            if (_packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId))
            {
                return new VisualStatus(DeucarianEditorIconIds.Busy, "Busy", PackageInstallerVisualStatusKind.Busy);
            }

            if (_packageInstallService.IsBusy &&
                _packageInstallService.CurrentPackage != null &&
                string.Equals(
                    _packageInstallService.CurrentPackage.PackageId,
                    packageDefinition.PackageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new VisualStatus(DeucarianEditorIconIds.Busy, "Busy", PackageInstallerVisualStatusKind.Busy);
            }

            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

            if (_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                if (updateStatus.IsSourceMigrationAvailable)
                {
                    return new VisualStatus(DeucarianEditorIconIds.GitBranch, "Migrate", PackageInstallerVisualStatusKind.UpdateAvailable);
                }

                if (updateStatus.IsReloadPending)
                {
                    return new VisualStatus(DeucarianEditorIconIds.Refresh, "Reload", PackageInstallerVisualStatusKind.UpdateAvailable);
                }

                if (updateStatus.IsUpdateAvailable)
                {
                    if (updateStatus.Kind == PackageUpdateStatusKind.SwitchAvailable)
                    {
                        return new VisualStatus(DeucarianEditorIconIds.Compare, "Switch", PackageInstallerVisualStatusKind.UpdateAvailable);
                    }

                    return new VisualStatus(DeucarianEditorIconIds.Update, "Update", PackageInstallerVisualStatusKind.UpdateAvailable);
                }

                return new VisualStatus(DeucarianEditorIconIds.PackageCheck, "Installed", PackageInstallerVisualStatusKind.Installed);
            }

            return new VisualStatus(DeucarianEditorIconIds.Optional, "Not Installed", PackageInstallerVisualStatusKind.NotInstalled);
        }

    }
}
