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


        private void UpdateActivityRetryButton()
        {
            if (_operationDrawerRetryButton == null)
            {
                return;
            }

            PackageInstallerActivityEntry latest = PackageInstallerActivityService.Latest;
            PackageInstallerRetryKind retryKind = ResolveContextualRetryKind(
                latest,
                _packageInstallService?.TerminalOperationSnapshot);
            if (retryKind == PackageInstallerRetryKind.ReplanOperation &&
                (_packageDependencyInstaller == null ||
                 !_packageDependencyInstaller.CanRetryLastPlannerFailure))
            {
                retryKind = PackageInstallerRetryKind.None;
            }
            ApplyContextualRetryButtonState(
                _operationDrawerRetryButton,
                retryKind,
                IsAnyOperationBusy());
        }

        internal static void ApplyContextualRetryButtonStateForTests(
            Button retryButton,
            PackageInstallerRetryKind retryKind,
            bool isBusy)
        {
            ApplyContextualRetryButtonState(retryButton, retryKind, isBusy);
        }

        private static void ApplyContextualRetryButtonState(
            Button retryButton,
            PackageInstallerRetryKind retryKind,
            bool isBusy)
        {
            if (retryButton == null)
            {
                return;
            }

            bool canRetry = retryKind != PackageInstallerRetryKind.None && !isBusy;
            string text = retryKind == PackageInstallerRetryKind.RestartOperation
                ? "Retry package operation"
                : retryKind == PackageInstallerRetryKind.ReplanOperation
                    ? "Retry package plan"
                    : "Retry";
            DeucarianEditorIconTextButton.SetText(retryButton, text);
            DeucarianEditorIconTextButton.SetIcon(
                retryButton,
                GetRetryIconId(retryKind));
            retryButton.tooltip = retryKind == PackageInstallerRetryKind.RestartOperation
                ? "Refresh installed and registry state, then replan the affected package operation."
                : retryKind == PackageInstallerRetryKind.ReplanOperation
                    ? "Rebuild the failed package plan from the current registry and installed state."
                    : "Retry the latest failed or canceled activity.";
            retryButton.style.display = canRetry
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            retryButton.SetEnabled(canRetry);
        }

        private static string GetRetryIconId(PackageInstallerRetryKind retryKind)
        {
            switch (retryKind)
            {
                case PackageInstallerRetryKind.CheckUpdates:
                    return DeucarianEditorIconIds.SearchCheck;
                case PackageInstallerRetryKind.ImportSample:
                    return DeucarianEditorIconIds.Sample;
                case PackageInstallerRetryKind.ResumeOperation:
                    return DeucarianEditorIconIds.Play;
                case PackageInstallerRetryKind.ReplanOperation:
                    return DeucarianEditorIconIds.Puzzle;
                case PackageInstallerRetryKind.RestartOperation:
                case PackageInstallerRetryKind.Refresh:
                default:
                    return DeucarianEditorIconIds.Refresh;
            }
        }

        private void RetryLatestActivity()
        {
            PackageInstallerActivityEntry latest = PackageInstallerActivityService.Latest;
            PackageOperationTerminalSnapshot snapshot =
                _packageInstallService?.TerminalOperationSnapshot;
            PackageInstallerRetryKind retryKind = ResolveContextualRetryKind(latest, snapshot);
            if (retryKind == PackageInstallerRetryKind.ReplanOperation &&
                (_packageDependencyInstaller == null ||
                 !_packageDependencyInstaller.CanRetryLastPlannerFailure))
            {
                retryKind = PackageInstallerRetryKind.None;
            }
            if (retryKind == PackageInstallerRetryKind.None || IsAnyOperationBusy())
            {
                return;
            }

            switch (retryKind)
            {
                case PackageInstallerRetryKind.Refresh:
                    _packageDetectionService?.Refresh();
                    break;
                case PackageInstallerRetryKind.CheckUpdates:
                    CheckForUpdates();
                    break;
                case PackageInstallerRetryKind.ImportSample:
                    _packageSampleImportService?.RetryLastImport();
                    break;
                case PackageInstallerRetryKind.ResumeOperation:
                    _promptSavedOperationAfterDetectionRefresh =
                        _packageInstallService != null && _packageInstallService.HasSavedOperation;
                    PackageRegistryProvider.RefreshRemote();
                    _packageDetectionService?.Refresh();
                    break;
                case PackageInstallerRetryKind.RestartOperation:
                    if (snapshot == null || !snapshot.CanRestart)
                    {
                        return;
                    }

                    _terminalOperationRetryAfterRefresh = snapshot;
                    PackageRegistryProvider.RefreshRemote();
                    _packageDetectionService?.Refresh();
                    break;
                case PackageInstallerRetryKind.ReplanOperation:
                    _plannerFailureRetryAfterRefresh = true;
                    PackageRegistryProvider.RefreshRemote();
                    _packageDetectionService?.Refresh();
                    break;
            }
        }

        internal static PackageInstallerRetryKind ResolveContextualRetryKindForTests(
            PackageInstallerActivityEntry latest,
            PackageOperationTerminalSnapshot terminalSnapshot)
        {
            return ResolveContextualRetryKind(latest, terminalSnapshot);
        }

        private static PackageInstallerRetryKind ResolveContextualRetryKind(
            PackageInstallerActivityEntry latest,
            PackageOperationTerminalSnapshot terminalSnapshot)
        {
            if (terminalSnapshot != null && terminalSnapshot.CanRestart)
            {
                return PackageInstallerRetryKind.RestartOperation;
            }

            return latest != null ? latest.RetryKind : PackageInstallerRetryKind.None;
        }

        private static void ApplyOperationDrawerData(
            VisualElement drawer,
            ScrollView scrollView,
            VisualElement content,
            Label titleLabel,
            Toggle verboseToggle,
            Label verboseLabel,
            Label messageLabel,
            bool expanded,
            bool verboseConsoleLogging,
            string report)
        {
            if (drawer == null)
            {
                return;
            }

            drawer.style.opacity = 1f;
            DeucarianEditorWorkbenchSurfaces.SetDrawerExpanded(drawer, expanded);

            float drawerHeight = CalculateOperationDrawerContainerHeight(
                expanded,
                CountOperationMessageLines(report));
            drawer.style.height = drawerHeight;
            drawer.style.minHeight = drawerHeight;
            drawer.style.maxHeight = drawerHeight;

            if (scrollView != null)
            {
                scrollView.style.display = DisplayStyle.Flex;
                scrollView.style.opacity = 1f;
            }

            if (content != null)
            {
                content.style.display = DisplayStyle.Flex;
                content.style.opacity = 1f;
            }

            if (titleLabel != null)
            {
                titleLabel.text = "Activity";
                titleLabel.style.display = DisplayStyle.Flex;
                titleLabel.style.opacity = 1f;
                titleLabel.style.color = DeucarianEditorVisualShell.Text;
            }

            if (verboseToggle != null)
            {
                verboseToggle.SetValueWithoutNotify(verboseConsoleLogging);
                verboseToggle.style.display = DisplayStyle.Flex;
                verboseToggle.style.opacity = 1f;
            }

            if (verboseLabel != null)
            {
                verboseLabel.text = "Verbose Console Logging";
                verboseLabel.style.display = DisplayStyle.Flex;
                verboseLabel.style.opacity = 1f;
                verboseLabel.style.color = DeucarianEditorVisualShell.MutedText;
            }

            if (messageLabel != null)
            {
                messageLabel.text = string.IsNullOrWhiteSpace(report)
                    ? "No detailed operation report is available."
                    : report.Trim();
                messageLabel.style.display = DisplayStyle.Flex;
                messageLabel.style.opacity = 1f;
                messageLabel.style.color = DeucarianEditorVisualShell.MutedText;
            }
        }

        private static VisualElement CreateOperationFooterRow(
            Action detailsToggleAction,
            Action cancelAction = null)
        {
            DeucarianEditorWorkbenchFooter sharedFooter =
                DeucarianEditorWorkbenchSurfaces.CreateFooter(
                    string.Empty,
                    "Idle",
                    "No operation running.",
                    "Cancel",
                    cancelAction,
                    GetFooterVersionText());
            VisualElement footer = sharedFooter.Root;
            footer.name = OperationFooterRowName;
            footer.style.height = OperationFooterHeight;
            footer.style.minHeight = OperationFooterHeight;
            footer.style.maxHeight = OperationFooterHeight;
            footer.style.paddingLeft = DeucarianEditorLayoutMetrics.FooterHorizontalPadding;
            footer.style.paddingRight = DeucarianEditorLayoutMetrics.FooterHorizontalPadding;
            footer.style.paddingTop = DeucarianEditorLayoutMetrics.FooterVerticalPadding;
            footer.style.paddingBottom = DeucarianEditorLayoutMetrics.FooterVerticalPadding;
            footer.style.opacity = 1f;

            sharedFooter.Status.name = OperationFooterStatusGroupName;
            sharedFooter.StatusImage.name = OperationFooterStatusIconName;
            sharedFooter.StatusLabel.name = OperationFooterStatusLabelName;
            sharedFooter.Summary.name = OperationFooterSummaryName;
            sharedFooter.Version.name = OperationFooterVersionName;
            sharedFooter.Status.style.opacity = 1f;
            sharedFooter.StatusImage.style.opacity = 1f;
            sharedFooter.StatusLabel.style.opacity = 1f;
            sharedFooter.Summary.style.opacity = 1f;
            sharedFooter.Version.style.opacity = 1f;

            Button cancelButton = sharedFooter.Action;
            cancelButton.name = OperationFooterCancelButtonName;
            DeucarianEditorWorkbenchToolbar.SetButtonIcon(
                cancelButton,
                DeucarianEditorIconIds.Stop,
                "Cancel",
                "Cancel the active Package Installer operation.");
            cancelButton.style.width = 124f;
            cancelButton.style.minWidth = 124f;
            cancelButton.style.maxWidth = 124f;
            cancelButton.style.display = DisplayStyle.None;

            Button detailsButton = DeucarianEditorWorkbenchSurfaces.AddFooterAction(
                sharedFooter,
                DeucarianEditorIconIds.ShowDetails,
                "Show Details",
                detailsToggleAction,
                "Show the last operation details.",
                128f);
            detailsButton.name = OperationFooterDetailsButtonName;
            detailsButton.style.opacity = 1f;

            ApplyOperationFooterData(
                footer,
                VisualStatusKind.Info,
                "Idle",
                "No operation running.",
                false,
                GetFooterVersionText());

            return footer;
        }

        private void CacheOperationFooterElements(VisualElement footer)
        {
            if (footer == null)
            {
                _operationFooterStatusGroup = null;
                _operationFooterStatusIcon = null;
                _operationFooterStatusLabel = null;
                _operationFooterSummaryLabel = null;
                _operationFooterDetailsButton = null;
                _operationFooterVersionLabel = null;
                return;
            }

            _operationFooterStatusGroup = footer.Q<VisualElement>(OperationFooterStatusGroupName);
            _operationFooterStatusIcon = footer.Q<Image>(OperationFooterStatusIconName);
            _operationFooterStatusLabel = footer.Q<Label>(OperationFooterStatusLabelName);
            _operationFooterSummaryLabel = footer.Q<Label>(OperationFooterSummaryName);
            _operationFooterDetailsButton = footer.Q<Button>(OperationFooterDetailsButtonName);
            _operationFooterVersionLabel = footer.Q<Label>(OperationFooterVersionName);
        }

        private void UpdateOperationFooter()
        {
            if (_operationFooterContainer == null)
            {
                return;
            }

            OperationProgressView operation = GetCurrentOperationProgress();
            ApplyOperationFooterData(
                _operationFooterContainer,
                GetGlobalOperationStatusKind(operation),
                GetGlobalOperationStateLabel(operation),
                GetOperationFooterSummaryLine(operation),
                _operationDetailsExpanded,
                GetFooterVersionText());

            CacheOperationFooterElements(_operationFooterContainer);
            UpdateOperationCancelButton();
            RefreshOperationDrawerContent();
        }

        private void UpdateOperationCancelButton()
        {
            Button cancelButton = _operationFooterContainer?.Q<Button>(OperationFooterCancelButtonName);
            if (cancelButton == null)
            {
                return;
            }

            bool installBusy = _packageInstallService != null && _packageInstallService.IsBusy;
            bool sampleBusy = _packageSampleImportService != null && _packageSampleImportService.IsBusy;
            bool checkBusy = _packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking;
            bool preflightBusy = _packageDependencyInstaller != null &&
                                 _packageDependencyInstaller.IsAwaitingPreflight;
            bool registryBusy = _activeActionKind == PackageInstallerActionKind.CheckUpdates &&
                                PackageRegistryProvider.IsRemoteRefreshing;
            cancelButton.style.display = installBusy || sampleBusy || checkBusy || preflightBusy || registryBusy
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            string text = sampleBusy
                ? "Cancel Import"
                : preflightBusy
                    ? "Cancel Confirmation"
                    : checkBusy
                        ? "Cancel Check"
                        : registryBusy
                            ? "Cancel Check"
                            : "Cancel";
            DeucarianEditorIconTextButton.SetText(cancelButton, text);
        }

        private void CancelCurrentContextualOperation()
        {
            if (TryCancelAwaitingPreflight())
            {
                // The confirmation itself is the active operation for single-package
                // reinstall and other risky flows that do not own a bulk action kind.
            }
            else if (_packageSampleImportService != null && _packageSampleImportService.IsBusy)
            {
                _packageSampleImportService.CancelCurrentImport();
            }
            else if (_packageInstallService != null && _packageInstallService.IsBusy)
            {
                _packageInstallService.CancelCurrentOperation();
            }
            else if (_packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking)
            {
                _packageUpdateCheckService.CancelCurrentCheck();
            }
            else if (PackageRegistryProvider.IsRemoteRefreshing)
            {
                if (_activeActionKind == PackageInstallerActionKind.CheckUpdates)
                {
                    CancelAction(PackageInstallerActionKind.CheckUpdates);
                }
                else
                {
                    PackageRegistryProvider.CancelRemoteRefresh();
                }
            }

            UpdateOperationFooter();
        }
    }
}
