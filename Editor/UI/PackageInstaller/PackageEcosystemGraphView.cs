using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_2022_1_OR_NEWER
using PackageGraphPainter = UnityEngine.UIElements.Painter2D;
#else
using PackageGraphPainter = Deucarian.PackageInstaller.Editor.PackageGraphMeshPainter;
#endif

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphView : VisualElement
    {
        private readonly PackageGraphCanvas _canvas;
        private readonly PackageGraphViewport _viewport;
        private readonly VisualElement _graphBody;
        private readonly PackageVisibilityFilterState _filterState;
        private readonly Action<PackageDefinition> _packageSelected;
        private readonly Action _filterChanged;
        private readonly Action _selectionCleared;
        private readonly Action _rootFocused;
        private readonly Action<PackageGraphGroup> _groupFocused;
        private readonly TextField _searchField;
        private readonly VisualElement _breadcrumbRow;
        private readonly Button _installedFilterButton;
        private readonly Button _notInstalledFilterButton;
        private readonly Button _clearFiltersButton;
        private readonly Label _visibleCountLabel;
        private readonly Label _hiddenRelatedLabel;
        private readonly VisualElement _legend;
        private readonly VisualElement _emptyState;
        private readonly Label _emptyStateTitle;
        private readonly Button _emptyStateActionButton;
        private EmptyStateAction _emptyStateAction;
        private PackageVisibilityFilterCounts _filterCounts =
            new PackageVisibilityFilterCounts(0, 0, 0, 0);
        private PackageGraphModel _currentGraph;
        private IReadOnlyCollection<string> _currentVisiblePackageIds = Array.Empty<string>();
        private PackageGraphSearchState _searchState = PackageGraphSearchState.Empty;
        private string _currentFocusedPackageId = string.Empty;
        private string _currentFocusedGroupId = string.Empty;
        private PackageGraphSpotlightKind _currentSpotlightKind = PackageGraphSpotlightKind.None;
        private int _hiddenRelatedCount;
        private bool _hasAppliedGraphFrame;
        private PackageInstallerResponsiveMode _responsiveMode = PackageInstallerResponsiveMode.Wide;
        private bool _suppressNextResponsiveFit;

        public PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction)
            : this(packageSelected, packageAction, null)
        {
        }

        public PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared)
            : this(packageSelected, packageAction, selectionCleared, null, null)
        {
        }

        public PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            Action rootFocused,
            Action<PackageGraphGroup> groupFocused,
            PackageVisibilityFilterState filterState,
            Action filterChanged)
            : this(packageSelected, packageAction, selectionCleared, filterState, filterChanged, rootFocused, groupFocused)
        {
        }

        public PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            PackageVisibilityFilterState filterState,
            Action filterChanged)
            : this(packageSelected, packageAction, selectionCleared, filterState, filterChanged, null, null)
        {
        }

        private PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            PackageVisibilityFilterState filterState,
            Action filterChanged,
            Action rootFocused,
            Action<PackageGraphGroup> groupFocused)
        {
            AddToClassList("dpi-ecosystem-graph");
            focusable = true;
            RegisterCallback<KeyDownEvent>(HandleGraphKeyDown, TrickleDown.TrickleDown);
            _filterState = filterState ?? new PackageVisibilityFilterState();
            _packageSelected = packageSelected;
            _filterChanged = filterChanged;
            _selectionCleared = selectionCleared;
            _rootFocused = rootFocused;
            _groupFocused = groupFocused;

            _canvas = new PackageGraphCanvas(packageSelected, packageAction, selectionCleared, _rootFocused, _groupFocused);
            _viewport = new PackageGraphViewport(selectionCleared);
            _viewport.ViewportSizeChanged += HandleViewportSizeChanged;
            _viewport.ZoomChanged += zoom =>
            {
                if (!_viewport.IsCameraTransitionActive)
                {
                    _canvas.SetViewportZoom(zoom);
                }
            };
            _viewport.CameraTransitionCompleted += zoom => _canvas.SetViewportZoom(zoom);
            _viewport.ContextMenuRequested += ShowContextMenu;
            _viewport.SetContent(_canvas);
            _canvas.ActiveVisualCenterChanged += UpdateGraphSpotlightCenter;

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-ecosystem-graph__header");
            Add(header);

            VisualElement filterRow = new VisualElement();
            filterRow.AddToClassList("dpi-ecosystem-graph__filter-row");
            // Package focus can add contextual text at otherwise wide window sizes.
            // Keep the navigation controls as one wrap-safe item so no action is clipped.
            filterRow.style.flexWrap = Wrap.Wrap;
            filterRow.style.alignContent = Align.FlexStart;
            header.Add(filterRow);

            _breadcrumbRow = new VisualElement();
            _breadcrumbRow.AddToClassList("dpi-ecosystem-graph__breadcrumbs");
            header.Add(_breadcrumbRow);

            _searchField = new TextField
            {
                name = "ecosystem-graph-search"
            };
            _searchField.AddToClassList("dpi-ecosystem-graph__search");
            _searchField.tooltip = "Find category or package by name or package ID.";
            _searchField.SetValueWithoutNotify(_filterState.SearchText);
            _searchField.RegisterValueChangedCallback(evt => ApplySearchText(evt.newValue));
            _searchField.RegisterCallback<KeyDownEvent>(HandleSearchKeyDown);
            VisualElement searchControl = new VisualElement();
            searchControl.AddToClassList("dpi-ecosystem-graph__search-control");
            searchControl.Add(PackageGraphToolbarControls.CreateIconImage(
                DeucarianEditorIconIds.Search,
                "dpi-ecosystem-graph__search-icon"));
            searchControl.Add(_searchField);
            filterRow.Add(searchControl);

            _installedFilterButton = PackageGraphToolbarControls.CreateFilterToggleButton(
                () =>
                {
                    if (_filterState.SetShowInstalled(!_filterState.ShowInstalled))
                    {
                        _filterChanged?.Invoke();
                        UpdateFilterControls();
                    }
                },
                "installed");
            filterRow.Add(_installedFilterButton);

            _notInstalledFilterButton = PackageGraphToolbarControls.CreateFilterToggleButton(
                () =>
                {
                    if (_filterState.SetShowNotInstalled(!_filterState.ShowNotInstalled))
                    {
                        _filterChanged?.Invoke();
                        UpdateFilterControls();
                    }
                },
                "not-installed");
            filterRow.Add(_notInstalledFilterButton);

            _clearFiltersButton = DeucarianEditorIconTextButton.Create(
                DeucarianEditorIconIds.Clear,
                "Clear",
                ClearFilters,
                "Clear search and show all package visibility states.");
            _clearFiltersButton.AddToClassList("dpi-ecosystem-graph__filter-clear");
            filterRow.Add(_clearFiltersButton);

            _visibleCountLabel = new Label("0 shown");
            _visibleCountLabel.AddToClassList("dpi-ecosystem-graph__visible-count");
            filterRow.Add(_visibleCountLabel);

            _hiddenRelatedLabel = new Label();
            _hiddenRelatedLabel.AddToClassList("dpi-ecosystem-graph__hidden-related");
            filterRow.Add(_hiddenRelatedLabel);

            VisualElement filterSpacer = new VisualElement();
            filterSpacer.AddToClassList("deucarian-toolbar-spacer");
            filterRow.Add(filterSpacer);

            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("dpi-ecosystem-graph__toolbar");
            toolbar.style.flexGrow = 1f;
            toolbar.Add(PackageGraphToolbarControls.CreateToolbarButton(
                DeucarianEditorIconIds.Fit,
                "Fit",
                FitCurrentContext));
            toolbar.Add(PackageGraphToolbarControls.CreateToolbarButton(
                DeucarianEditorIconIds.ActualSize,
                "100%",
                ResetCurrentContextZoom));
            toolbar.Add(PackageGraphToolbarControls.CreateToolbarButton(
                DeucarianEditorIconIds.Center,
                "Center",
                CenterCurrentContext));
            filterRow.Add(toolbar);

            _legend = new VisualElement();
            _legend.AddToClassList("dpi-ecosystem-graph__legend");
            header.Add(_legend);
            UpdateLegend(PackageGraphLayoutMode.Overview);

            _graphBody = new VisualElement();
            _graphBody.AddToClassList("dpi-ecosystem-graph__body");
            Add(_graphBody);

            _graphBody.Add(_viewport);

            _emptyState = new VisualElement();
            _emptyState.AddToClassList("dpi-ecosystem-graph__empty-state");
            _emptyState.RegisterCallback<GeometryChangedEvent>(HandleEmptyStateGeometryChanged);
            _emptyStateTitle = new Label();
            _emptyStateTitle.AddToClassList("dpi-ecosystem-graph__empty-title");
            _emptyState.Add(_emptyStateTitle);
            _emptyStateActionButton = DeucarianEditorIconTextButton.Create(
                DeucarianEditorIconIds.Info,
                string.Empty,
                HandleEmptyStateAction);
            _emptyStateActionButton.AddToClassList("dpi-ecosystem-graph__empty-action");
            _emptyStateActionButton.RegisterCallback<KeyDownEvent>(HandleEmptyStateActionKeyDown);
            _emptyState.Add(_emptyStateActionButton);
            _viewport.Add(_emptyState);

            SetResponsiveMode(PackageInstallerResponsiveMode.Wide);
            UpdateFilterControls();
        }

        public void SetResponsiveMode(PackageInstallerResponsiveMode responsiveMode)
        {
            bool changed = responsiveMode != _responsiveMode;
            _responsiveMode = responsiveMode;
            EnableInClassList("dpi-ecosystem-graph--wide", responsiveMode == PackageInstallerResponsiveMode.Wide);
            EnableInClassList("dpi-ecosystem-graph--compact", responsiveMode == PackageInstallerResponsiveMode.Compact);
            EnableInClassList("dpi-ecosystem-graph--narrow", responsiveMode == PackageInstallerResponsiveMode.Narrow);

            if (changed)
            {
                if (_suppressNextResponsiveFit)
                {
                    _suppressNextResponsiveFit = false;
                }
                else
                {
                    schedule.Execute(FitCurrentContext).ExecuteLater(80);
                }
            }
        }

        internal PackageGraphCameraState GetCameraStateForReload()
        {
            return _viewport.GetCameraState();
        }

        internal void PrepareCameraRestoreAfterReload()
        {
            _suppressNextResponsiveFit = true;
        }

        internal void RestoreCameraAfterReload(PackageGraphCameraState camera)
        {
            _suppressNextResponsiveFit = false;
            _viewport.ApplyPreviewCamera(camera);
            _canvas.SetViewportZoom(_viewport.Zoom);
        }

        public void SetGraph(PackageGraphModel graph, string selectedPackageId, bool actionsEnabled)
        {
            SetGraph(
                graph,
                selectedPackageId,
                selectedPackageId,
                string.Empty,
                actionsEnabled,
                null,
                null,
                null,
                0);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled)
        {
            SetGraph(
                graph,
                selectedPackageId,
                focusedPackageId,
                string.Empty,
                actionsEnabled,
                null,
                null,
                null,
                0);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds,
            PackageVisibilityFilterCounts filterCounts,
            int hiddenRelatedCount)
        {
            SetGraph(
                graph,
                selectedPackageId,
                focusedPackageId,
                string.Empty,
                actionsEnabled,
                visiblePackageIds,
                null,
                filterCounts,
                hiddenRelatedCount);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds,
            PackageGraphSearchState searchState,
            PackageVisibilityFilterCounts filterCounts,
            int hiddenRelatedCount)
        {
            SetGraph(
                graph,
                selectedPackageId,
                focusedPackageId,
                string.Empty,
                actionsEnabled,
                visiblePackageIds,
                searchState,
                filterCounts,
                hiddenRelatedCount);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            string focusedGroupId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds,
            PackageVisibilityFilterCounts filterCounts,
            int hiddenRelatedCount)
        {
            SetGraph(
                graph,
                selectedPackageId,
                focusedPackageId,
                focusedGroupId,
                actionsEnabled,
                visiblePackageIds,
                null,
                filterCounts,
                hiddenRelatedCount);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            string focusedGroupId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds,
            PackageGraphSearchState searchState,
            PackageVisibilityFilterCounts filterCounts,
            int hiddenRelatedCount)
        {
            string previousLayoutFocusPackageId = _canvas.LayoutFocusPackageId;
            string previousLayoutFocusGroupId = _canvas.LayoutFocusGroupId;
            PackageGraphLayoutMode previousLayoutMode = _canvas.LayoutMode;
            bool shouldForceInitialFrame = !_hasAppliedGraphFrame && graph != null && graph.Nodes.Count > 0;
            PackageGraphSearchState nextSearchState = searchState ?? PackageGraphSearchState.Empty;
            PackageGraphTransitionAnchor[] anchorCandidates = CreateTransitionAnchorCandidates(
                graph,
                selectedPackageId,
                focusedPackageId,
                focusedGroupId,
                previousLayoutFocusPackageId,
                previousLayoutFocusGroupId);
            Dictionary<PackageGraphTransitionAnchor, Vector2> sourceAnchorCenters =
                CaptureTransitionAnchorCenters(anchorCandidates);
            Dictionary<PackageGraphTransitionAnchor, Vector2> sourceAnchorScreens =
                sourceAnchorCenters.ToDictionary(
                    pair => pair.Key,
                    pair => _viewport.WorldToViewport(pair.Value));
            _currentGraph = graph;
            _currentFocusedPackageId = focusedPackageId ?? string.Empty;
            _currentFocusedGroupId = focusedGroupId ?? string.Empty;
            _currentVisiblePackageIds = visiblePackageIds == null
                ? (IReadOnlyCollection<string>)(graph != null
                    ? graph.Nodes.Select(node => node.PackageId).ToArray()
                    : Array.Empty<string>())
                : visiblePackageIds.ToArray();
            _searchState = nextSearchState;
            _canvas.SetGraph(
                graph,
                selectedPackageId,
                focusedPackageId,
                focusedGroupId,
                actionsEnabled,
                visiblePackageIds,
                _searchState);
            bool layoutTargetChanged =
                previousLayoutMode != _canvas.LayoutMode ||
                !string.Equals(
                    previousLayoutFocusPackageId,
                    _canvas.LayoutFocusPackageId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    previousLayoutFocusGroupId,
                    _canvas.LayoutFocusGroupId,
                    StringComparison.OrdinalIgnoreCase);
            _viewport.SetLayoutMode(_canvas.LayoutMode, !layoutTargetChanged);
            if (!layoutTargetChanged)
            {
                _canvas.SetViewportZoom(_viewport.Zoom);
            }
            _viewport.SetContentSize(_canvas.ContentSize.x, _canvas.ContentSize.y, !layoutTargetChanged);
            _viewport.SetActiveBounds(_canvas.GetContentBounds());
            _viewport.EnsureInitialFrame(_canvas.GetContentBounds(), shouldForceInitialFrame);
            UpdateGraphSpotlight(graph, focusedPackageId, focusedGroupId);
            UpdateLegend(_canvas.LayoutMode);
            _hasAppliedGraphFrame = _hasAppliedGraphFrame || shouldForceInitialFrame;
            _filterCounts = filterCounts ?? PackageVisibilityFilter.CalculateCounts(graph, _filterState);
            _hiddenRelatedCount = Math.Max(0, hiddenRelatedCount);
            UpdateFilterControls();
            UpdateBreadcrumbs(graph, focusedGroupId, focusedPackageId);

            if (layoutTargetChanged && !shouldForceInitialFrame)
            {
                AnimateToCurrentContext(anchorCandidates, sourceAnchorCenters, sourceAnchorScreens);
            }
        }

        private PackageGraphTransitionAnchor[] CreateTransitionAnchorCandidates(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            string focusedGroupId,
            string previousFocusedPackageId,
            string previousFocusedGroupId)
        {
            List<PackageGraphTransitionAnchor> anchors = new List<PackageGraphTransitionAnchor>();
            PackageGraphAnchorCandidates.AddPackageAnchor(anchors, focusedPackageId);
            PackageGraphAnchorCandidates.AddPackageAnchor(anchors, selectedPackageId);
            PackageGraphAnchorCandidates.AddPackageAnchor(anchors, previousFocusedPackageId);
            PackageGraphAnchorCandidates.AddGroupAnchor(anchors, focusedGroupId);
            PackageGraphAnchorCandidates.AddPackageGroupAnchors(graph, anchors, focusedPackageId);
            PackageGraphAnchorCandidates.AddPackageGroupAnchors(graph, anchors, selectedPackageId);
            PackageGraphAnchorCandidates.AddPackageGroupAnchors(graph, anchors, previousFocusedPackageId);
            PackageGraphAnchorCandidates.AddGroupAnchor(anchors, previousFocusedGroupId);
            PackageGraphAnchorCandidates.AddAncestorGroupAnchors(graph, anchors, focusedGroupId);
            PackageGraphAnchorCandidates.AddAncestorGroupAnchors(graph, anchors, previousFocusedGroupId);
            PackageGraphAnchorCandidates.AddAnchor(anchors, PackageGraphTransitionAnchor.Root);
            return anchors.ToArray();
        }

        private Dictionary<PackageGraphTransitionAnchor, Vector2> CaptureTransitionAnchorCenters(
            IEnumerable<PackageGraphTransitionAnchor> anchors)
        {
            Dictionary<PackageGraphTransitionAnchor, Vector2> centers =
                new Dictionary<PackageGraphTransitionAnchor, Vector2>();

            foreach (PackageGraphTransitionAnchor anchor in anchors)
            {
                if (_canvas.TryGetTransitionAnchorCenter(anchor, out Vector2 center))
                {
                    centers[anchor] = center;
                }
            }

            return centers;
        }

        private void AnimateToCurrentContext(
            IEnumerable<PackageGraphTransitionAnchor> anchorCandidates,
            IReadOnlyDictionary<PackageGraphTransitionAnchor, Vector2> sourceAnchorCenters,
            IReadOnlyDictionary<PackageGraphTransitionAnchor, Vector2> sourceAnchorScreens)
        {
            Rect bounds = _canvas.GetContentBounds();
            _viewport.SetActiveBounds(bounds);

            foreach (PackageGraphTransitionAnchor anchor in anchorCandidates)
            {
                if (sourceAnchorCenters != null &&
                    sourceAnchorScreens != null &&
                    sourceAnchorCenters.TryGetValue(anchor, out Vector2 sourceWorld) &&
                    sourceAnchorScreens.TryGetValue(anchor, out Vector2 sourceScreen) &&
                    _canvas.TryGetTransitionAnchorCenter(anchor, out Vector2 targetWorld))
                {
                    _viewport.AnimateToContent(bounds, sourceWorld, targetWorld, sourceScreen);
                    return;
                }
            }

            Vector2 activeCenter = _canvas.GetActiveCenter();
            _viewport.AnimateToContent(
                bounds,
                activeCenter,
                activeCenter,
                _viewport.WorldToViewport(activeCenter));
        }

        private void HandleViewportSizeChanged(Vector2 viewportSize)
        {
            if (!_canvas.SetViewportSize(viewportSize))
            {
                return;
            }

            _viewport.SetLayoutMode(_canvas.LayoutMode, !_viewport.IsCameraTransitionActive);
            _viewport.SetContentSize(
                _canvas.ContentSize.x,
                _canvas.ContentSize.y,
                !_viewport.IsCameraTransitionActive);
            _viewport.SetActiveBounds(_canvas.GetContentBounds());
            _viewport.EnsureInitialFrame(_canvas.GetContentBounds(), force: !_hasAppliedGraphFrame);
            UpdateGraphSpotlight(_currentGraph, _currentFocusedPackageId, _currentFocusedGroupId);
        }

        private void UpdateGraphSpotlight(
            PackageGraphModel graph,
            string focusedPackageId,
            string focusedGroupId)
        {
            PackageGraphSpotlightKind kind = ResolveSpotlightKind(graph, focusedPackageId, focusedGroupId);
            _currentSpotlightKind = kind;

            if (!_canvas.TryGetActiveVisualCenter(out Vector2 center))
            {
                _viewport.SetSpotlightWorldCenter(Vector2.zero, PackageGraphSpotlightKind.None);
                return;
            }

            _viewport.SetSpotlightWorldCenter(center, kind);
        }

        private void UpdateGraphSpotlightCenter(Vector2 center)
        {
            _viewport.SetSpotlightWorldCenter(center, _currentSpotlightKind);
        }

        private static PackageGraphSpotlightKind ResolveSpotlightKind(
            PackageGraphModel graph,
            string focusedPackageId,
            string focusedGroupId)
        {
            if (graph != null &&
                !string.IsNullOrWhiteSpace(focusedPackageId) &&
                graph.TryGetNode(focusedPackageId, out PackageGraphNode node))
            {
                return IsAttentionStatus(node.Status)
                    ? PackageGraphSpotlightKind.Attention
                    : PackageGraphSpotlightKind.Package;
            }

            if (graph != null &&
                !string.IsNullOrWhiteSpace(focusedGroupId) &&
                graph.TryGetGroup(focusedGroupId, out _))
            {
                return PackageGraphSpotlightKind.Category;
            }

            return PackageGraphSpotlightKind.Root;
        }

        private static bool IsAttentionStatus(PackageGraphNodeStatus status)
        {
            return status == PackageGraphNodeStatus.UpdateAvailable ||
                   status == PackageGraphNodeStatus.Missing ||
                   status == PackageGraphNodeStatus.Warning;
        }

        private void FitCurrentContext()
        {
            if (_viewport.IsCameraTransitionActive || _canvas.InteractionsLocked)
            {
                Rect animatedBounds = _canvas.GetContentBounds();
                Vector2 activeCenter = _canvas.GetActiveCenter();
                _viewport.AnimateToContent(
                    animatedBounds,
                    activeCenter,
                    activeCenter,
                    _viewport.WorldToViewport(activeCenter));
                return;
            }

            for (int iteration = 0; iteration < 4; iteration++)
            {
                Rect beforeBounds = _canvas.GetContentBounds();
                _viewport.SetActiveBounds(beforeBounds);
                _viewport.FitToContent(beforeBounds);
                _viewport.SetLayoutMode(_canvas.LayoutMode);
                _viewport.SetContentSize(_canvas.ContentSize.x, _canvas.ContentSize.y);
                Rect afterBounds = _canvas.GetContentBounds();
                _viewport.SetActiveBounds(afterBounds);

                if (PackageGraphAnchorCandidates.AreBoundsClose(beforeBounds, afterBounds))
                {
                    return;
                }
            }

            _viewport.FitToContent(_canvas.GetContentBounds());
        }

        private void ResetCurrentContextZoom()
        {
            _viewport.ResetZoom(_canvas.GetActiveCenter());
        }

        private void CenterCurrentContext()
        {
            _viewport.CenterOn(_canvas.GetActiveCenter());
        }

        private void NavigateToRoot()
        {
            _rootFocused?.Invoke();
        }

        private void NavigateToGroup(PackageGraphGroup group)
        {
            _groupFocused?.Invoke(group);
        }

        private void NavigateBackOneLevel()
        {
            _selectionCleared?.Invoke();
        }

        private void SelectPackage(PackageDefinition packageDefinition)
        {
            _packageSelected?.Invoke(packageDefinition);
        }

        private PackageGraphModel CreateCurrentVisibleGraph()
        {
            if (_currentGraph == null)
            {
                return new PackageGraphModel(
                    Array.Empty<PackageGraphNode>(),
                    Array.Empty<PackageGraphEdge>(),
                    Array.Empty<PackageGraphSuiteRegion>());
            }

            HashSet<string> visiblePackageIds = _currentVisiblePackageIds == null
                ? new HashSet<string>(
                    _currentGraph.Nodes.Select(node => node.PackageId),
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(_currentVisiblePackageIds, StringComparer.OrdinalIgnoreCase);
            return PackageVisibilityFilter.CreateVisibleGraph(_currentGraph, visiblePackageIds);
        }

        private void HandleSearchKeyDown(KeyDownEvent evt)
        {
            if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) &&
                CommitBestSearchResult())
            {
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode != KeyCode.Escape || string.IsNullOrWhiteSpace(_filterState.SearchText))
            {
                return;
            }

            ClearSearch();
            evt.StopPropagation();
        }

        private void ApplySearchText(string searchText)
        {
            if (_filterState.SetSearchText(searchText))
            {
                _filterChanged?.Invoke();
                UpdateFilterControls();
            }
        }

        internal void ApplySearchTextForTests(string searchText)
        {
            _searchField.SetValueWithoutNotify(searchText ?? string.Empty);
            ApplySearchText(searchText);
        }

        internal bool ActivateBestSearchResultForTests(KeyCode keyCode)
        {
            return (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter) &&
                   CommitBestSearchResult();
        }

        private void HandleGraphKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            HandleEscapeNavigation();

            evt.StopPropagation();
        }

        private void HandleEscapeNavigation()
        {
            if (!string.IsNullOrWhiteSpace(_filterState.SearchText))
            {
                ClearSearch();
                return;
            }

            _selectionCleared?.Invoke();
        }

        internal void HandleEscapeForTests()
        {
            HandleEscapeNavigation();
        }

        internal void HandleEscapeFromWindow()
        {
            HandleEscapeNavigation();
        }

        private bool CommitBestSearchResult()
        {
            PackageGraphSearchResult result = _searchState != null ? _searchState.BestResult : null;

            if (result == null || _currentGraph == null)
            {
                return false;
            }

            if (result.Type == PackageGraphSearchResultType.Category &&
                _currentGraph.TryGetGroup(result.Id, out PackageGraphGroup group))
            {
                NavigateToGroup(group);
                return true;
            }

            if (result.Type == PackageGraphSearchResultType.Package &&
                _currentGraph.TryGetNode(result.Id, out PackageGraphNode node) &&
                node.PackageDefinition != null)
            {
                SelectPackage(node.PackageDefinition);
                return true;
            }

            return false;
        }

        private void ClearSearch()
        {
            if (_filterState.SetSearchText(string.Empty))
            {
                _searchField.SetValueWithoutNotify(string.Empty);
                _filterChanged?.Invoke();
            }

            UpdateFilterControls();
        }

        private void ClearFilters()
        {
            if (_filterState.Reset())
            {
                _searchField.SetValueWithoutNotify(string.Empty);
                _filterChanged?.Invoke();
            }

            UpdateFilterControls();
        }

        private void UpdateBreadcrumbs(
            PackageGraphModel graph,
            string focusedGroupId,
            string focusedPackageId)
        {
            _breadcrumbRow.Clear();

            PackageGraphGroup[] groupPath = PackageGraphNavigationPath.CreateGroupPath(graph, focusedGroupId, focusedPackageId);
            PackageGraphNode focusedPackage = null;

            if (graph != null &&
                !string.IsNullOrWhiteSpace(focusedPackageId) &&
                graph.TryGetNode(focusedPackageId, out PackageGraphNode packageNode))
            {
                focusedPackage = packageNode;
            }

            if (groupPath.Length == 0 && focusedPackage == null)
            {
                _breadcrumbRow.Add(PackageGraphToolbarControls.CreateBreadcrumbCurrent(
                    "Deucarian",
                    DeucarianEditorIconIds.Network));
            }
            else
            {
                _breadcrumbRow.Add(PackageGraphToolbarControls.CreateBreadcrumbButton(
                    "Deucarian",
                    DeucarianEditorIconIds.Network,
                    NavigateToRoot));
            }

            for (int index = 0; index < groupPath.Length; index++)
            {
                PackageGraphGroup group = groupPath[index];
                bool currentGroup = focusedPackage == null && index == groupPath.Length - 1;
                _breadcrumbRow.Add(PackageGraphToolbarControls.CreateBreadcrumbSeparator());

                if (currentGroup)
                {
                    _breadcrumbRow.Add(PackageGraphToolbarControls.CreateBreadcrumbCurrent(
                        group.DisplayName,
                        group.IconKey));
                    continue;
                }

                _breadcrumbRow.Add(PackageGraphToolbarControls.CreateBreadcrumbButton(
                    group.DisplayName,
                    group.IconKey,
                    () => NavigateToGroup(group)));
            }

            if (focusedPackage != null)
            {
                _breadcrumbRow.Add(PackageGraphToolbarControls.CreateBreadcrumbSeparator());
                _breadcrumbRow.Add(PackageGraphToolbarControls.CreateBreadcrumbCurrent(
                    focusedPackage.DisplayName,
                    focusedPackage.IconKey,
                    packageIcon: true));
            }

            _breadcrumbRow.EnableInClassList(
                "dpi-ecosystem-graph__breadcrumbs--root",
                groupPath.Length == 0 && focusedPackage == null);
        }

        internal string ActiveHoverGroupId => _canvas.ActiveHoverGroupId;

        internal string ActiveHoverPackageId => _canvas.ActiveHoverPackageId;

        internal string ActiveTopLevelHoverGroupId => PackageGraphNavigationPath.ResolveTopLevelGroupId(_currentGraph, _canvas.ActiveHoverGroupId);

        internal void SetExternalGroupHover(string groupId)
        {
            _canvas.SetExternalHoverGroup(groupId);
        }

        internal void ClearExternalGroupHover(string groupId)
        {
            _canvas.ClearExternalHoverGroup(groupId);
        }

        internal void SetExternalPackageHover(string packageId)
        {
            _canvas.SetExternalHoverPackage(packageId);
        }

        internal void ClearExternalPackageHover(string packageId)
        {
            _canvas.ClearExternalHoverPackage(packageId);
        }

        internal void ClearHoverState()
        {
            _canvas.ClearHoverState();
        }

        internal void PreviewCategoryHoverForTests(string groupId)
        {
            _canvas.SetExternalHoverGroup(groupId, respectInteractionLock: false);
        }

        internal void PreviewPackageHoverForTests(string packageId)
        {
            _canvas.SetPreviewPackageForTests(packageId);
        }

        internal void ClearPackageHoverForTests(string packageId)
        {
            _canvas.ClearPreviewPackageForTests(packageId);
        }

        internal PackageGraphCameraState CameraStateForTests => _viewport.GetCameraState();

        internal bool CameraTransitionActiveForTests => _viewport.IsCameraTransitionActive;

        internal bool LayoutTransitionActiveForTests => _canvas.LayoutTransitionActiveForTests;

        internal void ApplyCameraForTests(PackageGraphCameraState camera)
        {
            _viewport.ApplyPreviewCamera(camera);
        }

        private static bool IsOverviewLikeLayout(PackageGraphLayoutMode mode)
        {
            return mode == PackageGraphLayoutMode.Overview;
        }

        private void ShowAllPackages()
        {
            if (_filterState.Set(
                    _filterState.SearchText,
                    showInstalled: true,
                    showNotInstalled: true))
            {
                _filterChanged?.Invoke();
            }

            UpdateFilterControls();
        }

        private void ShowMatchingPackages()
        {
            PackageGraphNode[] candidates = GetSearchMatchCandidates();
            bool showInstalled = _filterState.ShowInstalled || candidates.Any(node => node.IsInstalled);
            bool showNotInstalled = _filterState.ShowNotInstalled || candidates.Any(node => !node.IsInstalled);

            if (showInstalled == _filterState.ShowInstalled &&
                showNotInstalled == _filterState.ShowNotInstalled)
            {
                showInstalled = true;
                showNotInstalled = true;
            }

            if (_filterState.Set(_filterState.SearchText, showInstalled, showNotInstalled))
            {
                _filterChanged?.Invoke();
            }

            UpdateFilterControls();
        }

        private void UpdateFilterControls()
        {
            PackageVisibilityFilterCounts counts = _filterCounts ?? new PackageVisibilityFilterCounts(0, 0, 0, 0);
            _searchField.SetValueWithoutNotify(_filterState.SearchText);
            PackageGraphToolbarControls.UpdateFilterToggleButton(
                _installedFilterButton,
                _filterState.ShowInstalled,
                "Installed",
                counts.InstalledCount,
                "installed",
                DeucarianEditorIconIds.Success);
            PackageGraphToolbarControls.UpdateFilterToggleButton(
                _notInstalledFilterButton,
                _filterState.ShowNotInstalled,
                "Not installed",
                counts.NotInstalledCount,
                "not-installed",
                DeucarianEditorIconIds.Available);

            _clearFiltersButton.SetEnabled(!_filterState.IsDefault);
            _visibleCountLabel.text = _filterState.HasSearch
                ? (_searchState != null ? _searchState.DirectMatchCount : 0) + " matching"
                : counts.VisibleCount + " shown";
            bool packageFocusShowsFullEgo = _canvas != null &&
                                            _canvas.LayoutMode == PackageGraphLayoutMode.Focus;
            int summarizedDirectRelationshipCount = _canvas != null
                ? _canvas.SummarizedDirectRelationshipCount
                : 0;
            bool showFocusContext = packageFocusShowsFullEgo &&
                                    (!_filterState.IsDefault ||
                                     _hiddenRelatedCount > 0 ||
                                     summarizedDirectRelationshipCount > 0);
            _hiddenRelatedLabel.text = showFocusContext
                ? (summarizedDirectRelationshipCount > 0
                    ? "Focus includes direct relations (" +
                      summarizedDirectRelationshipCount +
                      " summarized)"
                    : "Focus includes direct relations")
                : (!_filterState.HasSearch && _hiddenRelatedCount > 0
                    ? _hiddenRelatedCount + " related hidden by filters"
                    : string.Empty);
            _hiddenRelatedLabel.tooltip = showFocusContext
                ? (summarizedDirectRelationshipCount > 0
                    ? "Search and visibility filters are preserved. All direct relationships remain represented; " +
                      summarizedDirectRelationshipCount +
                      " dense direct " +
                      (summarizedDirectRelationshipCount == 1 ? "relationship is" : "relationships are") +
                      " summarized behind the +N overflow summary."
                    : "Search and visibility filters are preserved. Direct relationships remain represented; " +
                      "dense extras may be summarized behind a +N overflow summary when needed.")
                : string.Empty;
            _hiddenRelatedLabel.style.display = !string.IsNullOrWhiteSpace(_hiddenRelatedLabel.text)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            UpdateEmptyState(counts);
        }

        private void UpdateEmptyState(PackageVisibilityFilterCounts counts)
        {
            if (_canvas != null && _canvas.LayoutMode == PackageGraphLayoutMode.Focus)
            {
                HideEmptyState();
                return;
            }

            if (_currentGraph != null && counts != null && counts.TotalCount == 0)
            {
                ShowEmptyState(
                    "No package entries are available in the active registry.",
                    string.Empty,
                    string.Empty,
                    EmptyStateAction.None);
                return;
            }

            if (!_filterState.HasAnyVisibilityEnabled)
            {
                ShowEmptyState(
                    "No package visibility filters selected.",
                    "Show all packages",
                    "Enable Installed and Not installed packages while keeping the current search.",
                    EmptyStateAction.ShowAllPackages);
                return;
            }

            bool lexicalMiss =
                _filterState.HasSearch &&
                (_searchState == null || _searchState.DirectMatchCount == 0);

            if (lexicalMiss)
            {
                ShowEmptyState(
                    "No categories or packages match the current search.",
                    "Clear search",
                    "Clear search while keeping the current package visibility filters and focus.",
                    EmptyStateAction.ClearSearch);
                return;
            }

            PackageGraphNode[] matchedCandidates = GetSearchMatchCandidates();
            PackageGraphNode[] focusedCandidates = matchedCandidates
                .Where(IsInCurrentGroupScope)
                .ToArray();
            bool hasCategoryMatchInCurrentScope = HasDirectCategoryMatchInCurrentScope();
            bool hasVisibleCategoryMatchInCurrentScope = string.IsNullOrWhiteSpace(_currentFocusedGroupId)
                ? _searchState != null && _searchState.DirectCategoryMatchCount > 0
                : hasCategoryMatchInCurrentScope;
            bool hasMatchInCurrentScope = focusedCandidates.Length > 0 || hasCategoryMatchInCurrentScope;
            bool hasEligibleInCurrentScope = focusedCandidates.Any(IsVisibleByStatus);
            bool hasHiddenInCurrentScope = focusedCandidates.Any(node => !IsVisibleByStatus(node));
            bool hasMatchOutsideCurrentScope =
                !string.IsNullOrWhiteSpace(_currentFocusedGroupId) &&
                (matchedCandidates.Any(node => !IsInCurrentGroupScope(node)) ||
                 HasDirectCategoryMatchOutsideCurrentScope());
            bool hasHiddenMatch = matchedCandidates.Any(node => !IsVisibleByStatus(node));
            bool noVisibleUnscopedPackages =
                !_filterState.HasSearch &&
                counts != null &&
                counts.TotalCount > 0 &&
                counts.VisibleCount == 0;

            if (_filterState.HasSearch &&
                !string.IsNullOrWhiteSpace(_currentFocusedGroupId) &&
                !hasMatchInCurrentScope &&
                hasMatchOutsideCurrentScope)
            {
                ShowEmptyState(
                    "No matches in this group.",
                    "Search all groups",
                    "Return to the ecosystem overview while keeping the current search.",
                    EmptyStateAction.SearchAllGroups);
                return;
            }

            if (!hasVisibleCategoryMatchInCurrentScope &&
                !hasEligibleInCurrentScope &&
                (hasHiddenInCurrentScope ||
                 (string.IsNullOrWhiteSpace(_currentFocusedGroupId) && hasHiddenMatch) ||
                 noVisibleUnscopedPackages))
            {
                ShowEmptyState(
                    _filterState.HasSearch
                        ? "Matching packages are hidden by the current status filters."
                        : "Packages are hidden by the current status filters.",
                    "Show matching packages",
                    "Enable the relevant package visibility states while keeping the current search and focus.",
                    EmptyStateAction.ShowMatchingPackages);
                return;
            }

            HideEmptyState();
        }

        private void HandleEmptyStateAction()
        {
            switch (_emptyStateAction)
            {
                case EmptyStateAction.ShowAllPackages:
                    ShowAllPackages();
                    break;
                case EmptyStateAction.ShowMatchingPackages:
                    ShowMatchingPackages();
                    break;
                case EmptyStateAction.ClearSearch:
                    ClearSearch();
                    break;
                case EmptyStateAction.SearchAllGroups:
                    NavigateToRoot();
                    break;
            }
        }

        private void HandleEmptyStateActionKeyDown(KeyDownEvent evt)
        {
            if (!PackageGraphKeyboard.IsActivationKey(evt.keyCode))
            {
                return;
            }

            HandleEmptyStateAction();
            evt.PreventDefault();
            evt.StopPropagation();
        }

        internal bool ActivateEmptyStateActionFromKeyboardForTests(KeyCode keyCode)
        {
            if (!PackageGraphKeyboard.IsActivationKey(keyCode))
            {
                return false;
            }

            HandleEmptyStateAction();
            return true;
        }

        private void ShowEmptyState(
            string title,
            string actionText,
            string actionTooltip,
            EmptyStateAction action)
        {
            _emptyState.style.display = DisplayStyle.Flex;
            _emptyStateTitle.text = title ?? string.Empty;
            _emptyStateAction = action;
            bool showAction = action != EmptyStateAction.None;
            _emptyStateActionButton.style.display = showAction ? DisplayStyle.Flex : DisplayStyle.None;
            DeucarianEditorIconTextButton.SetText(
                _emptyStateActionButton,
                showAction ? actionText ?? string.Empty : string.Empty);
            DeucarianEditorIconTextButton.SetIcon(
                _emptyStateActionButton,
                showAction ? GetEmptyStateActionIcon(action) : string.Empty);
            _emptyStateActionButton.tooltip = showAction ? actionTooltip ?? string.Empty : string.Empty;
        }

        private static string GetEmptyStateActionIcon(EmptyStateAction action)
        {
            switch (action)
            {
                case EmptyStateAction.ShowAllPackages:
                    return DeucarianEditorIconIds.Suite;
                case EmptyStateAction.ShowMatchingPackages:
                    return DeucarianEditorIconIds.Filter;
                case EmptyStateAction.ClearSearch:
                    return DeucarianEditorIconIds.SearchClear;
                case EmptyStateAction.SearchAllGroups:
                    return DeucarianEditorIconIds.Network;
                default:
                    return DeucarianEditorIconIds.Info;
            }
        }

        private static void HandleEmptyStateGeometryChanged(GeometryChangedEvent evt)
        {
            if (!(evt.currentTarget is VisualElement emptyState) ||
                evt.newRect.width <= 0f ||
                evt.newRect.height <= 0f)
            {
                return;
            }

            ApplyEmptyStateCenteredMargins(emptyState, evt.newRect.size);
        }

        internal static void ApplyEmptyStateCenteredMargins(
            VisualElement emptyState,
            Vector2 size)
        {
            if (emptyState == null || size.x <= 0f || size.y <= 0f)
            {
                return;
            }

            Vector2 centeredMargins = CalculateEmptyStateCenteredMargins(size);
            if (!Mathf.Approximately(emptyState.resolvedStyle.marginLeft, centeredMargins.x))
            {
                emptyState.style.marginLeft = centeredMargins.x;
            }

            if (!Mathf.Approximately(emptyState.resolvedStyle.marginTop, centeredMargins.y))
            {
                emptyState.style.marginTop = centeredMargins.y;
            }
        }

        internal static Vector2 CalculateEmptyStateCenteredMargins(Vector2 size)
        {
            return new Vector2(
                -Mathf.Max(0f, size.x) * 0.5f,
                -Mathf.Max(0f, size.y) * 0.5f);
        }

        private void HideEmptyState()
        {
            _emptyState.style.display = DisplayStyle.None;
            _emptyStateAction = EmptyStateAction.None;
        }

        private PackageGraphNode[] GetSearchMatchCandidates()
        {
            if (_currentGraph == null)
            {
                return Array.Empty<PackageGraphNode>();
            }

            if (!_filterState.HasSearch || _searchState == null)
            {
                return _currentGraph.Nodes.Where(node => node != null).ToArray();
            }

            HashSet<string> candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string packageId in _searchState.DirectPackageMatchIds)
            {
                candidateIds.Add(packageId);
            }

            foreach (string groupId in _searchState.DirectCategoryMatchIds)
            {
                foreach (PackageGraphNode node in _currentGraph.GetDescendantPackages(groupId))
                {
                    if (node != null)
                    {
                        candidateIds.Add(node.PackageId);
                    }
                }
            }

            return _currentGraph.Nodes
                .Where(node => node != null && candidateIds.Contains(node.PackageId))
                .ToArray();
        }

        private bool IsVisibleByStatus(PackageGraphNode node)
        {
            return node != null &&
                   (node.IsInstalled ? _filterState.ShowInstalled : _filterState.ShowNotInstalled);
        }

        private bool IsInCurrentGroupScope(PackageGraphNode node)
        {
            if (node == null ||
                _currentGraph == null ||
                string.IsNullOrWhiteSpace(_currentFocusedGroupId))
            {
                return node != null;
            }

            return _currentGraph.GetDescendantPackages(_currentFocusedGroupId)
                .Any(candidate => candidate != null &&
                                  string.Equals(
                                      candidate.PackageId,
                                      node.PackageId,
                                      StringComparison.OrdinalIgnoreCase));
        }

        private bool HasDirectCategoryMatchOutsideCurrentScope()
        {
            if (_currentGraph == null ||
                _searchState == null ||
                string.IsNullOrWhiteSpace(_currentFocusedGroupId))
            {
                return false;
            }

            return _searchState.DirectCategoryMatchIds.Any(groupId =>
                !AreGroupsInSameScope(_currentFocusedGroupId, groupId));
        }

        private bool HasDirectCategoryMatchInCurrentScope()
        {
            if (_currentGraph == null ||
                _searchState == null ||
                string.IsNullOrWhiteSpace(_currentFocusedGroupId))
            {
                return false;
            }

            return _searchState.DirectCategoryMatchIds.Any(groupId =>
                AreGroupsInSameScope(_currentFocusedGroupId, groupId));
        }

        private bool AreGroupsInSameScope(string firstGroupId, string secondGroupId)
        {
            return IsGroupOrDescendant(firstGroupId, secondGroupId) ||
                   IsGroupOrDescendant(secondGroupId, firstGroupId);
        }

        private bool IsGroupOrDescendant(string ancestorGroupId, string candidateGroupId)
        {
            if (_currentGraph == null ||
                string.IsNullOrWhiteSpace(ancestorGroupId) ||
                string.IsNullOrWhiteSpace(candidateGroupId))
            {
                return false;
            }

            string currentGroupId = candidateGroupId;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   _currentGraph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                if (string.Equals(currentGroupId, ancestorGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentGroupId = group.ParentGroupId;
            }

            return false;
        }

        private enum EmptyStateAction
        {
            None,
            ShowAllPackages,
            ShowMatchingPackages,
            ClearSearch,
            SearchAllGroups
        }

        private void UpdateLegend(PackageGraphLayoutMode layoutMode)
        {
            if (_legend == null)
            {
                return;
            }

            _legend.Clear();
            bool hasAttention = _canvas != null && _canvas.HasRenderedAttentionNode();
            bool hasChecking = HasNodeStatus(status => status == PackageGraphNodeStatus.Checking);

            if (layoutMode == PackageGraphLayoutMode.Focus)
            {
                _legend.Add(PackageGraphToolbarControls.CreateLegendItem(
                    DeucarianEditorIconIds.GitBranch,
                    "Dependency flow",
                    "dpi-graph-legend__line--solid",
                    "Required package -> dependent package. The routed edge direction follows the dependency flow."));
                _legend.Add(PackageGraphToolbarControls.CreateLegendItem(
                    DeucarianEditorIconIds.Integration,
                    "Integration connection",
                    "dpi-graph-legend__line--integration",
                    "Integration package connects systems. The routed edge shows the connection direction."));
                _legend.Add(PackageGraphToolbarControls.CreateLegendItem(
                    DeucarianEditorIconIds.Optional,
                    "Optional companion",
                    "dpi-graph-legend__line--optional",
                    "Recommended alongside, not required"));
                _legend.Add(PackageGraphToolbarControls.CreateLegendItem(
                    DeucarianEditorIconIds.Suite,
                    "Suite membership",
                    "dpi-graph-legend__line--suite",
                    "Package belongs to a curated bundle"));
                AddTransientStatusLegendItems(hasAttention, hasChecking);
                return;
            }

            if (layoutMode == PackageGraphLayoutMode.GroupFocus)
            {
                _legend.Add(PackageGraphToolbarControls.CreateLegendItem(DeucarianEditorIconIds.FolderTree, "Group", "dpi-graph-legend__line--group"));
                _legend.Add(PackageGraphToolbarControls.CreateLegendItem(DeucarianEditorIconIds.Package, "Package", "dpi-graph-legend__line--package"));
                _legend.Add(PackageGraphToolbarControls.CreateLegendItem(DeucarianEditorIconIds.Network, "Category orbit", "dpi-graph-legend__line--membership", "The orbit shows structural category membership, not a dependency."));
                AddTransientStatusLegendItems(hasAttention, hasChecking);
                return;
            }

            _legend.Add(PackageGraphToolbarControls.CreateLegendItem(DeucarianEditorIconIds.Network, "Deucarian root", "dpi-graph-legend__line--root"));
            _legend.Add(PackageGraphToolbarControls.CreateLegendItem(DeucarianEditorIconIds.FolderTree, "Group", "dpi-graph-legend__line--group"));
            _legend.Add(PackageGraphToolbarControls.CreateLegendItem(DeucarianEditorIconIds.Package, "Package", "dpi-graph-legend__line--package"));
            _legend.Add(PackageGraphToolbarControls.CreateLegendItem(DeucarianEditorIconIds.Success, "Installed", "dpi-graph-legend__line--installed"));
            _legend.Add(PackageGraphToolbarControls.CreateLegendItem(DeucarianEditorIconIds.Available, "Not installed", "dpi-graph-legend__line--available"));
            AddTransientStatusLegendItems(hasAttention, hasChecking);
        }

        private bool HasNodeStatus(Func<PackageGraphNodeStatus, bool> predicate)
        {
            return predicate != null &&
                   _canvas != null &&
                   _canvas.HasRenderedNodeStatus(predicate);
        }

        private void AddTransientStatusLegendItems(bool hasAttention, bool hasChecking)
        {
            if (hasChecking)
            {
                _legend.Add(PackageGraphToolbarControls.CreateLegendItem(DeucarianEditorIconIds.Busy, "Checking", "dpi-graph-legend__line--checking"));
            }

            if (hasAttention)
            {
                _legend.Add(PackageGraphToolbarControls.CreateLegendItem(DeucarianEditorIconIds.Warning, "Attention", "dpi-graph-legend__line--warning"));
            }
        }

        private void ShowContextMenu(PackageGraphContextMenuRequest request)
        {
            if (request == null || _canvas.InteractionsLocked)
            {
                return;
            }

            GenericMenu menu = new GenericMenu();

            if (TryResolvePackageFromTarget(request.Target, out PackageGraphNode packageNode))
            {
                PopulatePackageContextMenu(menu, packageNode);
            }
            else if (TryResolveGroupFromTarget(request.Target, out PackageGraphGroup group))
            {
                PopulateGroupContextMenu(menu, group);
            }
            else if (PackageGraphVisualQueries.HasAncestorClass(request.Target, "dpi-graph-hub"))
            {
                PopulateRootContextMenu(menu);
            }
            else
            {
                PopulateCanvasContextMenu(menu);
            }

            menu.ShowAsContext();
        }

        private void PopulateCanvasContextMenu(GenericMenu menu)
        {
            menu.AddItem(MenuContent(DeucarianEditorIconIds.Fit, "Fit current context"), false, FitCurrentContext);
            menu.AddItem(MenuContent(DeucarianEditorIconIds.Center, "Center current focus"), false, CenterCurrentContext);
            menu.AddItem(MenuContent(DeucarianEditorIconIds.ActualSize, "100%"), false, ResetCurrentContextZoom);

            if (!IsOverviewLikeLayout(_canvas.LayoutMode))
            {
                menu.AddItem(MenuContent(DeucarianEditorIconIds.Back, "Back one hierarchy level"), false, NavigateBackOneLevel);
            }
            else
            {
                menu.AddDisabledItem(MenuContent(DeucarianEditorIconIds.Back, "Back one hierarchy level"));
            }

            menu.AddItem(MenuContent(DeucarianEditorIconIds.Network, "Ecosystem Overview"), false, NavigateToRoot);
        }

        private void PopulatePackageContextMenu(GenericMenu menu, PackageGraphNode packageNode)
        {
            if (packageNode == null)
            {
                PopulateCanvasContextMenu(menu);
                return;
            }

            if (packageNode.PackageDefinition == null)
            {
                string diagnostic = GetMissingPackageDiagnostic(packageNode);
                menu.AddDisabledItem(MenuContent(DeucarianEditorIconIds.MissingPackage, "Missing Registry Target"));
                menu.AddItem(
                    MenuContent(DeucarianEditorIconIds.Copy, "Copy Package ID"),
                    false,
                    () => EditorGUIUtility.systemCopyBuffer = packageNode.PackageId);
                menu.AddItem(
                    MenuContent(DeucarianEditorIconIds.Info, "Copy Diagnostic"),
                    false,
                    () => EditorGUIUtility.systemCopyBuffer = diagnostic);
                return;
            }

            PackageDefinition packageDefinition = packageNode.PackageDefinition;
            menu.AddItem(MenuContent(DeucarianEditorIconIds.Focus, "Focus / Select Package"), false, () => SelectPackage(packageDefinition));
            menu.AddItem(MenuContent(DeucarianEditorIconIds.Details, "Show Package Details"), false, () => SelectPackage(packageDefinition));
            menu.AddItem(MenuContent(DeucarianEditorIconIds.Copy, "Copy Package ID"), false, () => EditorGUIUtility.systemCopyBuffer = packageDefinition.PackageId);

            string repositoryUrl = GetRepositoryUrl(packageDefinition);
            if (!string.IsNullOrWhiteSpace(repositoryUrl))
            {
                menu.AddItem(MenuContent(DeucarianEditorIconIds.ExternalLink, "Open Repository"), false, () => Application.OpenURL(repositoryUrl));
            }
            else
            {
                menu.AddDisabledItem(MenuContent(DeucarianEditorIconIds.ExternalLink, "Open Repository"));
            }
        }

        internal static string GetMissingPackageDiagnostic(PackageGraphNode packageNode)
        {
            if (packageNode == null)
            {
                return string.Empty;
            }

            string reason = !string.IsNullOrWhiteSpace(packageNode.Description)
                ? packageNode.Description.Trim()
                : (!string.IsNullOrWhiteSpace(packageNode.UpdateStatusLabel)
                    ? packageNode.UpdateStatusLabel.Trim()
                    : "Registry relationship target is not registered.");
            return "Package ID: " + packageNode.PackageId + "\nReason: " + reason;
        }

        private void PopulateGroupContextMenu(GenericMenu menu, PackageGraphGroup group)
        {
            if (group == null)
            {
                PopulateCanvasContextMenu(menu);
                return;
            }

            bool active = string.Equals(group.Id, _canvas.LayoutFocusGroupId, StringComparison.OrdinalIgnoreCase);
            menu.AddItem(MenuContent(DeucarianEditorIconIds.Focus, "Focus Category"), false, () => NavigateToGroup(group));
            menu.AddItem(MenuContent(DeucarianEditorIconIds.Fit, "Fit Category"), false, () =>
            {
                if (!active)
                {
                    NavigateToGroup(group);
                }

                schedule.Execute(FitCurrentContext).ExecuteLater(1);
            });

            if (active)
            {
                menu.AddItem(MenuContent(DeucarianEditorIconIds.Back, "Back to Parent"), false, NavigateBackOneLevel);
            }
            else
            {
                menu.AddDisabledItem(MenuContent(DeucarianEditorIconIds.Back, "Back to Parent"));
            }

            menu.AddItem(MenuContent(DeucarianEditorIconIds.Network, "Ecosystem Overview"), false, NavigateToRoot);
        }

        private void PopulateRootContextMenu(GenericMenu menu)
        {
            menu.AddItem(MenuContent(DeucarianEditorIconIds.Network, "Ecosystem Overview"), false, NavigateToRoot);
            menu.AddItem(MenuContent(DeucarianEditorIconIds.Fit, "Fit Overview"), false, FitCurrentContext);
            menu.AddItem(MenuContent(DeucarianEditorIconIds.Center, "Center Root"), false, () =>
            {
                _viewport.CenterOn(PackageGraphLayout.GraphCenter);
            });
            menu.AddItem(MenuContent(DeucarianEditorIconIds.ActualSize, "100%"), false, () =>
            {
                _viewport.ResetZoom(PackageGraphLayout.GraphCenter);
            });
        }

        private static GUIContent MenuContent(string iconId, string text, string tooltip = null)
        {
            return DeucarianEditorIcons.GetIconContent(iconId, text, tooltip ?? text);
        }

        private bool TryResolvePackageFromTarget(VisualElement target, out PackageGraphNode packageNode)
        {
            packageNode = null;
            VisualElement nodeElement = PackageGraphVisualQueries.FindAncestorWithClass(target, "dpi-graph-node");

            return nodeElement != null &&
                   _currentGraph != null &&
                   _currentGraph.TryGetNode(nodeElement.name, out packageNode);
        }

        private bool TryResolveGroupFromTarget(VisualElement target, out PackageGraphGroup group)
        {
            group = null;
            VisualElement groupElement = PackageGraphVisualQueries.FindAncestorWithClass(target, "dpi-graph-group");

            if (groupElement == null ||
                string.IsNullOrWhiteSpace(groupElement.name) ||
                !groupElement.name.StartsWith("group-", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string groupId = groupElement.name.Substring("group-".Length);
            return _currentGraph != null && _currentGraph.TryGetGroup(groupId, out group);
        }

        private static string GetRepositoryUrl(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            string url = !string.IsNullOrWhiteSpace(packageDefinition.StableUrl)
                ? packageDefinition.StableUrl
                : packageDefinition.DevelopmentUrl;

            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            int hashIndex = url.IndexOf('#');
            if (hashIndex >= 0)
            {
                url = url.Substring(0, hashIndex);
            }

            return url.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? url.Substring(0, url.Length - 4)
                : url;
        }

    }

}
