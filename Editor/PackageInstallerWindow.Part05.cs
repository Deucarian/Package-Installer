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


        private static void ApplyOperationFooterData(
            VisualElement footer,
            VisualStatusKind statusKind,
            string statusText,
            string summaryText,
            bool detailsExpanded,
            string packageVersionText)
        {
            if (footer == null)
            {
                return;
            }

            string safeStatusText = string.IsNullOrWhiteSpace(statusText) ? "Idle" : statusText.Trim();
            string safeSummaryText = string.IsNullOrWhiteSpace(summaryText) ? "No operation running." : summaryText.Trim();
            string safeVersionText = string.IsNullOrWhiteSpace(packageVersionText)
                ? PackageInstallerRuntimeIdentity.PackageId
                : packageVersionText.Trim();
            Color statusColor = GetStatusColor(statusKind);

            Image statusIcon = footer.Q<Image>(OperationFooterStatusIconName);
            Label statusLabel = footer.Q<Label>(OperationFooterStatusLabelName);
            Label summaryLabel = footer.Q<Label>(OperationFooterSummaryName);
            Button detailsButton = footer.Q<Button>(OperationFooterDetailsButtonName);
            Label versionLabel = footer.Q<Label>(OperationFooterVersionName);

            if (statusIcon != null)
            {
                statusIcon.image = DeucarianEditorIcons.GetIcon(GetStatusIconId(statusKind));
                statusIcon.style.display = DisplayStyle.Flex;
                statusIcon.tooltip = safeStatusText;
                statusIcon.tintColor = statusColor;
                SetFooterStatusClass(statusIcon, statusKind);
            }

            if (statusLabel != null)
            {
                statusLabel.text = safeStatusText;
                statusLabel.tooltip = safeStatusText;
            }

            if (summaryLabel != null)
            {
                summaryLabel.text = safeSummaryText;
                summaryLabel.tooltip = safeSummaryText;
            }

            if (detailsButton != null)
            {
                DeucarianEditorIconTextButton.SetText(
                    detailsButton,
                    detailsExpanded ? "Hide Details" : "Show Details");
                DeucarianEditorIconTextButton.SetIcon(
                    detailsButton,
                    detailsExpanded
                        ? DeucarianEditorIconIds.HideDetails
                        : DeucarianEditorIconIds.ShowDetails);
                detailsButton.tooltip = detailsExpanded
                    ? "Hide the last operation details."
                    : "Show the last operation details.";
            }

            if (versionLabel != null)
            {
                versionLabel.text = safeVersionText;
                versionLabel.tooltip = safeVersionText;
            }
        }

        private static void SetFooterStatusClass(VisualElement element, VisualStatusKind statusKind)
        {
            if (element == null)
            {
                return;
            }

            element.RemoveFromClassList(
                DeucarianEditorWorkbenchSurfaces.FooterStatusSuccessClass);
            element.RemoveFromClassList(
                DeucarianEditorWorkbenchSurfaces.FooterStatusNeutralClass);
            element.RemoveFromClassList(
                DeucarianEditorWorkbenchSurfaces.FooterStatusWarningClass);
            element.RemoveFromClassList(
                DeucarianEditorWorkbenchSurfaces.FooterStatusErrorClass);
            element.RemoveFromClassList(
                DeucarianEditorWorkbenchSurfaces.FooterStatusBusyClass);

            switch (statusKind)
            {
                case VisualStatusKind.Installed:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusSuccessClass);
                    break;
                case VisualStatusKind.NotInstalled:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusNeutralClass);
                    break;
                case VisualStatusKind.UpdateAvailable:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusWarningClass);
                    break;
                case VisualStatusKind.Failed:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusErrorClass);
                    break;
                case VisualStatusKind.Busy:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusBusyClass);
                    break;
                case VisualStatusKind.Info:
                case VisualStatusKind.Integration:
                default:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusNeutralClass);
                    break;
            }
        }

        private void SetViewMode(InstallerViewMode viewMode)
        {
            bool wasGraphMode = _viewMode == InstallerViewMode.EcosystemGraph;
            _viewMode = ResolveInstallerViewMode(viewMode);
            UpdateViewVisibility();

            if (_viewMode == InstallerViewMode.EcosystemGraph)
            {
                RefreshGraphView("view mode changed");

                if (!wasGraphMode)
                {
                    RequestAutomaticGraphUpdateCheck();
                }
            }

            Repaint();
        }

        private static InstallerViewMode ResolveInstallerViewMode(InstallerViewMode requestedViewMode)
        {
            return ListViewEnabled || requestedViewMode != InstallerViewMode.List
                ? requestedViewMode
                : DefaultInstallerViewMode;
        }

        private bool ShouldCheckForUpdatesOnGraphOpen()
        {
            return _viewMode == InstallerViewMode.EcosystemGraph &&
                   PackageUpdateCheckPreferences.ShouldCheckOnWindowOpen(
                       DateTime.UtcNow,
                       _packageUpdateCheckService != null
                           ? _packageUpdateCheckService.LastCheckedUtc
                           : null);
        }

        private void RequestAutomaticGraphUpdateCheck()
        {
            if (!ShouldCheckForUpdatesOnGraphOpen())
            {
                return;
            }

            InvalidateGraphModelCache("automatic graph update check");
            QueueDeferredUpdateCheck(PackageInstallerActionKind.CheckUpdates);

            if (_packageInstallService != null && _packageInstallService.IsBusy)
            {
                UpdateViewVisibility();
                Repaint();
                return;
            }

            PackageRegistryProvider.RefreshRemote();
            _packageUpdateCheckService.PrepareForUpdateCheck();

            if (!_packageDetectionService.IsRefreshing)
            {
                _packageDetectionService.Refresh();
            }

            TryRunDeferredUpdateCheck();
            RefreshGraphView("automatic graph update check");
            UpdateViewVisibility();
            Repaint();
        }

        private void QueueDeferredUpdateCheck(PackageInstallerActionKind actionKind)
        {
            _checkUpdatesAfterDetectionRefresh = true;
            _deferredUpdateCheckActionKind = actionKind;

            if (actionKind != PackageInstallerActionKind.None &&
                _activeActionKind == PackageInstallerActionKind.None &&
                (_packageInstallService == null || !_packageInstallService.IsBusy))
            {
                _activeActionKind = actionKind;
                _cancelingActionKind = PackageInstallerActionKind.None;
            }
        }

        private void TryRunDeferredUpdateCheck()
        {
            if (!_checkUpdatesAfterDetectionRefresh)
            {
                ClearActiveActionIfIdle();
                return;
            }

            if ((_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                PackageRegistryProvider.IsRemoteRefreshing)
            {
                UpdateViewVisibility();
                Repaint();
                return;
            }

            RunDeferredUpdateCheck();
        }

        private void RunDeferredUpdateCheck()
        {
            if (!_checkUpdatesAfterDetectionRefresh)
            {
                ClearActiveActionIfIdle();
                return;
            }

            PackageInstallerActionKind actionKind = _deferredUpdateCheckActionKind;
            _checkUpdatesAfterDetectionRefresh = false;
            _deferredUpdateCheckActionKind = PackageInstallerActionKind.None;

            if (actionKind != PackageInstallerActionKind.None &&
                _activeActionKind == PackageInstallerActionKind.None)
            {
                _activeActionKind = actionKind;
                _cancelingActionKind = PackageInstallerActionKind.None;
            }

            _packageUpdateCheckService.CheckForUpdates(PackageRegistryProvider.All, GetSelectedChannel);
            ClearActiveActionIfIdle();
        }

        private void ClearActiveActionIfIdle()
        {
            if (_activeActionKind == PackageInstallerActionKind.None || IsActiveActionStillBusy())
            {
                return;
            }

            _activeActionKind = PackageInstallerActionKind.None;
            _cancelingActionKind = PackageInstallerActionKind.None;

            if (!_checkUpdatesAfterDetectionRefresh)
            {
                _deferredUpdateCheckActionKind = PackageInstallerActionKind.None;
            }

            UpdateViewVisibility();
            Repaint();
        }

        private void HandlePreflightCompleted()
        {
            if (_packageInstallService == null || !_packageInstallService.IsBusy)
            {
                _pendingUpdateStatusInvalidationPackageIds.Clear();
            }

            ClearActiveActionIfIdle();
            UpdateViewVisibility();
            Repaint();
        }

        private bool IsActiveActionStillBusy()
        {
            switch (_activeActionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    return _checkUpdatesAfterDetectionRefresh ||
                           (_packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking) ||
                           (_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                           PackageRegistryProvider.IsRemoteRefreshing;
                case PackageInstallerActionKind.UpdateAll:
                case PackageInstallerActionKind.InstallAll:
                    return (_packageInstallService != null && _packageInstallService.IsBusy) ||
                           (_packageDependencyInstaller != null &&
                            _packageDependencyInstaller.IsAwaitingPreflight);
                default:
                    return false;
            }
        }
    }
}
