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


        private void UpdateViewVisibility()
        {
            bool graphMode = _viewMode == InstallerViewMode.EcosystemGraph;

            if (_listViewContainerHost != null)
            {
                _listViewContainerHost.style.display = graphMode ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_graphModeContainer != null)
            {
                _graphModeContainer.style.display = graphMode ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_operationDrawerContainer != null)
            {
                RefreshOperationDrawerContent();
            }

            if (_operationFooterContainer != null)
            {
                _operationFooterContainer.style.display = DisplayStyle.Flex;
                _operationFooterContainer.style.height = OperationFooterHeight;
                _operationFooterContainer.style.minHeight = OperationFooterHeight;
                _operationFooterContainer.style.maxHeight = OperationFooterHeight;
            }

            UpdateOperationFooter();

            if (_listViewButton != null)
            {
                SetViewToggleActive(_listViewButton, !graphMode);
            }

            if (_graphViewButton != null)
            {
                SetViewToggleActive(_graphViewButton, graphMode);
            }

            bool busy = IsAnyOperationBusy();
            PackageDefinition[] packagesWithUpdates = _packageUpdateCheckService != null
                ? GetPackagesWithUpdates()
                : Array.Empty<PackageDefinition>();

            if (_graphGlobalChannelButton != null)
            {
                DeucarianEditorCommandBar.SetReservedVisible(
                    _graphGlobalChannelSlot,
                    graphMode);
                _graphGlobalChannelButton.SetEnabled(!busy);
                UpdateGlobalChannelOverrideButton();

                if (!graphMode)
                {
                    HideGlobalChannelOverridePopup();
                }
            }

            if (_graphRefreshButton != null)
            {
                DeucarianEditorCommandBar.SetReservedVisible(
                    _graphRefreshSlot,
                    graphMode);
                _graphRefreshButton.SetEnabled(!busy);
            }

            if (_graphCheckUpdatesButton != null)
            {
                PackageInstallerActionButtonState state = CreateActionButtonState(
                    PackageInstallerActionKind.CheckUpdates,
                    _activeActionKind,
                    _cancelingActionKind,
                    busy,
                    packagesWithUpdates.Length > 0);
                DeucarianEditorCommandBar.SetReservedVisible(
                    _graphCheckUpdatesSlot,
                    graphMode);
                DeucarianEditorCommandBar.SetText(_graphCheckUpdatesButton, state.Label);
                _graphCheckUpdatesButton.SetEnabled(state.Enabled);
            }

            if (_graphUpdateAllButton != null)
            {
                PackageInstallerActionButtonState state = CreateActionButtonState(
                    PackageInstallerActionKind.UpdateAll,
                    _activeActionKind,
                    _cancelingActionKind,
                    busy,
                    packagesWithUpdates.Length > 0);
                _graphUpdateAllButton.style.display = graphMode ? DisplayStyle.Flex : DisplayStyle.None;
                DeucarianEditorCommandBar.SetText(_graphUpdateAllButton, state.Label);
                _graphUpdateAllButton.SetEnabled(state.Enabled);
            }

            if (_graphInstallAllButton != null)
            {
                PackageInstallerActionButtonState state = CreateActionButtonState(
                    PackageInstallerActionKind.InstallAll,
                    _activeActionKind,
                    _cancelingActionKind,
                    busy,
                    packagesWithUpdates.Length > 0);
                _graphInstallAllButton.style.display = graphMode ? DisplayStyle.Flex : DisplayStyle.None;
                DeucarianEditorCommandBar.SetText(_graphInstallAllButton, state.Label);
                _graphInstallAllButton.SetEnabled(state.Enabled);
            }

            if (_viewSummaryLabel != null)
            {
                string summary = PackageRegistryProvider.All.Count +
                                 " packages - " +
                                 PackageRegistryProvider.StatusMessage;
                _viewSummaryLabel.text = summary;
                _viewSummaryLabel.tooltip = summary;
            }
        }

        private void RefreshGraphView()
        {
            RefreshGraphView("refresh");
        }

        private void RefreshExternalState(string reason)
        {
            if (_stateRepository == null)
            {
                return;
            }

            bool shouldRefreshGraph = false;
            PackageChannelSelection projectChannelSelection = _stateRepository.GetProjectChannelSelection();
            PackageChannel projectChannel = projectChannelSelection.Channel;

            if (projectChannel != _lastObservedProjectChannel ||
                projectChannelSelection.ChangedAtUtcTicks != _lastObservedProjectChannelChangedAtUtcTicks)
            {
                _lastObservedProjectChannel = projectChannel;
                _lastObservedProjectChannelChangedAtUtcTicks = projectChannelSelection.ChangedAtUtcTicks;
                _packageUpdateCheckService?.InvalidateAll();
                InvalidateGraphModelCache("selected channel changed externally");
                UpdateGlobalChannelOverrideButton();
                shouldRefreshGraph = true;
            }

            if (_packageDetectionService != null &&
                _packageDetectionService.RefreshIfManifestStateChanged())
            {
                bool hadUpdateStatuses =
                    _packageUpdateCheckService != null && _packageUpdateCheckService.HasStatuses;
                _packageUpdateCheckService?.InvalidateIfManifestStateChanged();

                if (hadUpdateStatuses)
                {
                    QueueDeferredUpdateCheck(PackageInstallerActionKind.CheckUpdates);
                }

                InvalidateGraphModelCache("project manifest changed externally");
                shouldRefreshGraph = true;
            }

            if (shouldRefreshGraph)
            {
                RefreshGraphView(reason);
                Repaint();
            }
        }

        private void RefreshGraphView(string reason)
        {
            if (_graphView == null)
            {
                return;
            }

            bool graphCacheDirty = _graphModelCacheDirty || _cachedPackageGraph == null;
            string diagnosticReason = graphCacheDirty
                ? (string.IsNullOrWhiteSpace(reason) ? "refresh" : reason) +
                  " / " + _graphModelCacheInvalidationReason
                : reason;

            using (PackageGraphOpenProfiler.Begin(
                       diagnosticReason,
                       _graphNavigationState.FocusedPackageId,
                       _graphNavigationState.FocusedGroupId,
                       graphCacheDirty))
            {
                PackageGraphModel graph = GetOrBuildPackageGraphModel();
                PackageGraphOpenProfiler.Current?.SetGraphCounts(graph);
                ValidatePendingReloadState(graph);

                HashSet<string> visiblePackageIds;
                PackageGraphSearchState searchState;
                PackageVisibilityFilterCounts filterCounts;
                int hiddenRelatedCount;

                using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.VisibilitySearch))
                {
                    visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(
                        graph,
                        _visibilityFilterState);
                    searchState = PackageGraphSearchIndex.Create(
                        graph,
                        _visibilityFilterState);

                    ClearGraphSelectionIfHidden(visiblePackageIds);

                    filterCounts = PackageVisibilityFilter.CalculateCounts(
                        graph,
                        _visibilityFilterState);
                    hiddenRelatedCount = PackageVisibilityFilter.CountHiddenRelatedPackages(
                        graph,
                        _graphNavigationState.FocusedPackageId,
                        visiblePackageIds);
                }

                _graphView.SetGraph(
                    graph,
                    _selectedPackageId,
                    _graphNavigationState.FocusedPackageId,
                    _graphNavigationState.FocusedGroupId,
                    !IsAnyOperationBusy(),
                    visiblePackageIds,
                    searchState,
                    filterCounts,
                    hiddenRelatedCount);

                using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.LayoutRepaintScheduling))
                {
                    _graphDetailsContainer?.MarkDirtyRepaint();
                    _operationDrawerContainer?.MarkDirtyRepaint();
                    UpdateOperationFooter();
                    UpdateViewVisibility();
                }
            }
        }

        private PackageGraphModel GetOrBuildPackageGraphModel()
        {
            if (!_graphModelCacheDirty && _cachedPackageGraph != null)
            {
                _lastPackageGraph = _cachedPackageGraph;
                return _cachedPackageGraph;
            }

            IReadOnlyList<PackageDefinition> packages;
            IReadOnlyList<PackageGraphGroup> groups;

            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.RegistryLookup))
            {
                packages = PackageRegistryProvider.All;
                groups = PackageRegistryProvider.EcosystemGroups;
            }

            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.GraphRebuild))
            {
                PackageGraphBuilder builder = _packageGraphBuilder ?? new PackageGraphBuilder(
                    packageId => _packageDetectionService != null && _packageDetectionService.IsInstalled(packageId),
                    GetSelectedChannel,
                    packageDefinition => _packageUpdateCheckService != null
                        ? _packageUpdateCheckService.GetStatus(packageDefinition, GetSelectedChannel(packageDefinition))
                        : null);
                _cachedPackageGraph = builder.Build(packages, groups);
                _lastPackageGraph = _cachedPackageGraph;
                _graphModelCacheDirty = false;
                PackageGraphOpenProfiler.Current?.MarkGraphRebuilt();
            }

            return _cachedPackageGraph;
        }

        private void InvalidateGraphModelCache(string reason)
        {
            // Registry, manifest/install state, update status, and channel changes alter graph node state.
            // Focus-only navigation intentionally does not invalidate this cache.
            _graphModelCacheDirty = true;
            _graphModelCacheInvalidationReason = string.IsNullOrWhiteSpace(reason)
                ? "graph data changed"
                : reason.Trim();
        }

        private void HandleVisibilityFilterChanged()
        {
            RefreshGraphView("visibility filter changed");
            Repaint();
        }

        private void ClearGraphSelectionIfHidden(ISet<string> visiblePackageIds)
        {
            if (_viewMode != InstallerViewMode.EcosystemGraph || visiblePackageIds == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_graphNavigationState.FocusedPackageId))
            {
                return;
            }

            if (!ShouldClearGraphSelectionForFilters(
                    _selectedPackageId,
                    _graphNavigationState.FocusedPackageId,
                    visiblePackageIds))
            {
                return;
            }

            _selectionKind = SelectionKind.Package;
            _selectedPackageId = string.Empty;
            _graphNavigationState = PackageGraphNavigationState.Overview();
            _detailsScrollPosition = Vector2.zero;
        }

        private void HandleGraphPackageSelected(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return;
            }

            SelectionKind selectionKind = packageDefinition.IsIntegration ? SelectionKind.Integration : SelectionKind.Package;

            if (IsSelected(packageDefinition, selectionKind))
            {
                NavigateGraphToPackageOwner(packageDefinition.PackageId);
                return;
            }

            SelectDefinition(
                packageDefinition,
                selectionKind,
                refreshGraph: false);
            _graphNavigationState = PackageGraphNavigationState.Package(
                packageDefinition.PackageId,
                GetGraphPackageGroupId(packageDefinition.PackageId));
            RefreshGraphView("package focus");
        }

        private void ClearGraphSelection()
        {
            NavigateGraphToRoot();
        }

        private void HandleGraphRootFocused()
        {
            NavigateGraphToRoot();
        }

        private void HandleGraphGroupFocused(PackageGraphGroup group)
        {
            if (group == null)
            {
                NavigateGraphToRoot();
                return;
            }

            if (string.Equals(group.Id, _graphNavigationState.FocusedGroupId, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(_graphNavigationState.FocusedPackageId))
            {
                NavigateGraphToGroupOrRoot(GetGraphParentGroupId(group.Id));
                return;
            }

            NavigateGraphToGroup(group.Id);
        }

        private void HandleGraphBackNavigation()
        {
            if (!string.IsNullOrWhiteSpace(_graphNavigationState.FocusedPackageId))
            {
                string parentGroupId = GetGraphPackageGroupId(_graphNavigationState.FocusedPackageId);
                NavigateGraphToGroupOrRoot(parentGroupId);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_graphNavigationState.FocusedGroupId))
            {
                string parentGroupId = GetGraphParentGroupId(_graphNavigationState.FocusedGroupId);
                NavigateGraphToGroupOrRoot(parentGroupId);
                return;
            }

            NavigateGraphToRoot();
        }

        private void NavigateGraphToPackageOwner(string packageId)
        {
            NavigateGraphToGroupOrRoot(GetGraphPackageGroupId(packageId));
        }
    }
}
