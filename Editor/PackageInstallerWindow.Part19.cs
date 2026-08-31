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


        private static string GetStatusIconId(VisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case VisualStatusKind.Installed:
                    return DeucarianEditorIconIds.Success;
                case VisualStatusKind.NotInstalled:
                    return DeucarianEditorIconIds.Optional;
                case VisualStatusKind.UpdateAvailable:
                    return DeucarianEditorIconIds.Update;
                case VisualStatusKind.Failed:
                    return DeucarianEditorIconIds.Error;
                case VisualStatusKind.Busy:
                    return DeucarianEditorIconIds.Busy;
                case VisualStatusKind.Integration:
                    return DeucarianEditorIconIds.Integration;
                case VisualStatusKind.Info:
                default:
                    return DeucarianEditorIconIds.Info;
            }
        }

        private static DeucarianEditorStatus ToEditorStatus(VisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case VisualStatusKind.Installed:
                    return DeucarianEditorStatus.Success;
                case VisualStatusKind.UpdateAvailable:
                    return DeucarianEditorStatus.Warning;
                case VisualStatusKind.Failed:
                    return DeucarianEditorStatus.Error;
                case VisualStatusKind.NotInstalled:
                    return DeucarianEditorStatus.Disabled;
                case VisualStatusKind.Busy:
                case VisualStatusKind.Info:
                case VisualStatusKind.Integration:
                default:
                    return DeucarianEditorStatus.Info;
            }
        }

        private static MessageType ToMessageType(VisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case VisualStatusKind.Failed:
                    return MessageType.Error;
                case VisualStatusKind.UpdateAvailable:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }

        private static string GetDependencyDisplayNames(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null || integrationDefinition.Dependencies.Count == 0)
            {
                return "-";
            }

            return string.Join(
                ", ",
                integrationDefinition.Dependencies
                    .Select(GetDependencyDisplayName)
                    .ToArray());
        }

        private static string GetDependencyDisplayName(string packageId)
        {
            return PackageRegistryProvider.TryGetPackage(packageId, out PackageDefinition packageDefinition)
                ? packageDefinition.DisplayName
                : packageId;
        }

        private void DrawChannelPopup(PackageDefinition packageDefinition)
        {
            PackageChannel selectedChannel = GetSelectedChannel(packageDefinition);
            PackageChannel[] channelOptions = GetChannelOptions(packageDefinition, selectedChannel);
            string[] channelLabels = channelOptions.Select(GetChannelLabel).ToArray();
            int selectedIndex = Mathf.Max(0, Array.IndexOf(channelOptions, selectedChannel));

            using (new EditorGUI.DisabledScope(channelOptions.Length <= 1 || IsAnyOperationBusy()))
            {
                int nextIndex = EditorGUILayout.Popup(
                    selectedIndex,
                    channelLabels,
                    GUILayout.Width(118f));
                PackageChannel nextChannel = channelOptions[Mathf.Clamp(nextIndex, 0, channelOptions.Length - 1)];

                if (nextChannel != selectedChannel)
                {
                    SetSelectedChannel(packageDefinition, nextChannel);
                }
            }
        }

        private PackageChannel GetSelectedChannel(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return PackageChannel.Stable;
            }

            PackageChannelSelection projectSelection = _stateRepository != null
                ? _stateRepository.GetProjectChannelSelection()
                : PackageChannelSelection.None;
            PackageChannelSelection packageSelection = _stateRepository != null
                ? _stateRepository.GetPackageChannelSelection(packageDefinition.PackageId)
                : PackageChannelSelection.None;
            PackageChannel installedChannel = PackageChannel.Stable;
            bool hasInstalledChannel = _packageDetectionService != null &&
                _packageDetectionService.TryGetInstalledPackageChannel(
                    packageDefinition,
                    out installedChannel,
                    out _);

            return ResolveSelectedChannel(
                packageDefinition,
                projectSelection,
                packageSelection,
                hasInstalledChannel,
                installedChannel);
        }

        internal static PackageChannel ResolveSelectedChannelForTests(
            PackageDefinition packageDefinition,
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection,
            bool hasInstalledChannel,
            PackageChannel installedChannel)
        {
            return ResolveSelectedChannel(
                packageDefinition,
                projectSelection,
                packageSelection,
                hasInstalledChannel,
                installedChannel);
        }

        internal static PackageChannel ResolveSelectedChannel(
            PackageDefinition packageDefinition,
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection,
            bool hasInstalledChannel,
            PackageChannel installedChannel)
        {
            if (packageDefinition == null)
            {
                return PackageChannel.Stable;
            }

            PackageChannelSelection latestExplicitSelection = GetLatestExplicitChannelSelection(
                projectSelection,
                packageSelection);

            if (latestExplicitSelection.HasValue)
            {
                return ResolveConfiguredChannel(packageDefinition, latestExplicitSelection.Channel);
            }

            return PackageChannel.Stable;
        }

        private static PackageChannelSelection GetLatestExplicitChannelSelection(
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection)
        {
            if (packageSelection.HasValue &&
                (!projectSelection.HasValue ||
                 packageSelection.ChangedAtUtcTicks > projectSelection.ChangedAtUtcTicks))
            {
                return packageSelection;
            }

            return projectSelection.HasValue
                ? projectSelection
                : PackageChannelSelection.None;
        }

        private static PackageChannel ResolveConfiguredChannel(
            PackageDefinition packageDefinition,
            PackageChannel channel)
        {
            if (channel == PackageChannel.Development &&
                packageDefinition != null &&
                packageDefinition.HasDevelopmentUrl)
            {
                return PackageChannel.Development;
            }

            return PackageChannel.Stable;
        }

        private void SetSelectedChannel(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                return;
            }

            if (channel == PackageChannel.Custom)
            {
                return;
            }

            _stateRepository?.SetPackageChannel(packageDefinition.PackageId, channel);
            _packageUpdateCheckService?.Invalidate(packageDefinition.PackageId);
            InvalidateGraphModelCache("package channel changed");

            if (_packageDetectionService != null &&
                _packageUpdateCheckService != null &&
                _packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                _packageUpdateCheckService.CheckForUpdate(packageDefinition, channel);
            }
            else
            {
                _packageUpdateCheckService?.Invalidate(packageDefinition.PackageId);
            }

            RefreshGraphView("package channel changed");
        }

        private bool IsCategoryExpanded(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return true;
            }

            if (_categoryFoldouts.TryGetValue(category, out bool expanded))
            {
                return expanded;
            }

            string key = GetCategoryFoldoutPreferenceKey(category);
            expanded = EditorPrefs.GetBool(key, true);
            _categoryFoldouts[category] = expanded;
            return expanded;
        }

        private void SetCategoryExpanded(string category, bool expanded)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return;
            }

            _categoryFoldouts[category] = expanded;
            EditorPrefs.SetBool(GetCategoryFoldoutPreferenceKey(category), expanded);
        }

        private string GetCategoryFoldoutPreferenceKey(string category)
        {
            return CategoryFoldoutPreferencePrefix +
                   Application.dataPath.Replace("\\", "/") +
                   "." +
                   category.Trim();
        }

        private static PackageChannel[] GetChannelOptions(
            PackageDefinition packageDefinition,
            PackageChannel selectedChannel)
        {
            List<PackageChannel> channels = new List<PackageChannel>
            {
                PackageChannel.Stable
            };

            if (packageDefinition != null && packageDefinition.HasDevelopmentUrl)
            {
                channels.Add(PackageChannel.Development);
            }

            if (selectedChannel == PackageChannel.Custom)
            {
                channels.Add(PackageChannel.Custom);
            }

            return channels.Distinct().ToArray();
        }

        private static string GetChannelLabel(PackageChannel channel)
        {
            switch (channel)
            {
                case PackageChannel.Development:
                    return "Development";
                case PackageChannel.Custom:
                    return "Custom";
                default:
                    return "Stable";
            }
        }

        private void EnsureValidSelection()
        {
            if (_reloadStatePendingValidation)
            {
                return;
            }

            if (GetSelectedDefinition() != null)
            {
                return;
            }

            if (_viewMode == InstallerViewMode.EcosystemGraph)
            {
                _selectedPackageId = string.Empty;
                return;
            }

            PackageDefinition defaultSelection = PackageRegistryProvider.All.FirstOrDefault(package => !package.IsIntegration);

            if (defaultSelection == null)
            {
                defaultSelection = PackageRegistryProvider.IntegrationPackages.FirstOrDefault();
                _selectionKind = SelectionKind.Integration;
            }
            else
            {
                _selectionKind = SelectionKind.Package;
            }

            _selectedPackageId = defaultSelection != null ? defaultSelection.PackageId : string.Empty;
        }

        private void HandleBeforeAssemblyReload()
        {
            PackageGraphCameraState camera = _graphView != null
                ? _graphView.GetCameraStateForReload()
                : new PackageGraphCameraState(Vector2.zero, 1f);
            PackageInstallerWindowReloadState.SaveForAssemblyReload(
                new PackageInstallerWindowReloadSnapshot
                {
                    searchText = _visibilityFilterState.SearchText,
                    showInstalled = _visibilityFilterState.ShowInstalled,
                    showNotInstalled = _visibilityFilterState.ShowNotInstalled,
                    selectedPackageId = _selectedPackageId,
                    navigationTargetKind = (int)_graphNavigationState.TargetKind,
                    focusedPackageId = _graphNavigationState.FocusedPackageId,
                    focusedGroupId = _graphNavigationState.FocusedGroupId,
                    viewMode = (int)_viewMode,
                    sidebarScrollX = _sidebarScrollPosition.x,
                    sidebarScrollY = _sidebarScrollPosition.y,
                    detailsScrollX = _detailsScrollPosition.x,
                    detailsScrollY = _detailsScrollPosition.y,
                    operationScrollX = _operationDetailsScrollPosition.x,
                    operationScrollY = _operationDetailsScrollPosition.y,
                    hasGraphCamera = _graphView != null,
                    graphPanX = camera.Pan.x,
                    graphPanY = camera.Pan.y,
                    graphZoom = camera.Zoom
                });
        }

        private void RestoreReloadSnapshot(PackageInstallerWindowReloadSnapshot snapshot)
        {
            _pendingReloadSnapshot = snapshot;
            _reloadStatePendingValidation = true;
            _visibilityFilterState.Set(
                snapshot.searchText,
                snapshot.showInstalled,
                snapshot.showNotInstalled);
            _selectedPackageId = snapshot.selectedPackageId;
            _graphNavigationState = RestoreNavigation(snapshot);
            _viewMode = Enum.IsDefined(typeof(InstallerViewMode), snapshot.viewMode)
                ? ResolveInstallerViewMode((InstallerViewMode)snapshot.viewMode)
                : DefaultInstallerViewMode;
            _sidebarScrollPosition = new Vector2(snapshot.sidebarScrollX, snapshot.sidebarScrollY);
            _detailsScrollPosition = new Vector2(snapshot.detailsScrollX, snapshot.detailsScrollY);
            _operationDetailsScrollPosition = new Vector2(
                snapshot.operationScrollX,
                snapshot.operationScrollY);
            _hasPendingReloadCamera = snapshot.hasGraphCamera;
            if (_hasPendingReloadCamera)
            {
                _pendingReloadCamera = new PackageGraphCameraState(
                    new Vector2(snapshot.graphPanX, snapshot.graphPanY),
                    snapshot.graphZoom);
            }
        }
    }
}
