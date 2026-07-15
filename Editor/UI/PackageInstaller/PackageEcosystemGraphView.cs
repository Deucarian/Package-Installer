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
        private const string InstalledStatusMarker = "\u2713";
        private const string NotInstalledStatusMarker = "\u25CB";

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
            _searchField.RegisterValueChangedCallback(evt =>
            {
                if (_filterState.SetSearchText(evt.newValue))
                {
                    _filterChanged?.Invoke();
                    UpdateFilterControls();
                }
            });
            _searchField.RegisterCallback<KeyDownEvent>(HandleSearchKeyDown);
            filterRow.Add(_searchField);

            _installedFilterButton = CreateFilterToggleButton(
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

            _notInstalledFilterButton = CreateFilterToggleButton(
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

            _clearFiltersButton = new Button(ClearFilters)
            {
                text = "Clear",
                tooltip = "Clear search and show all package visibility states."
            };
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
            toolbar.Add(CreateToolbarButton("Fit", FitCurrentContext));
            toolbar.Add(CreateToolbarButton("100%", ResetCurrentContextZoom));
            toolbar.Add(CreateToolbarButton("Center", CenterCurrentContext));
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
            _emptyStateTitle = new Label();
            _emptyStateTitle.AddToClassList("dpi-ecosystem-graph__empty-title");
            _emptyState.Add(_emptyStateTitle);
            _emptyStateActionButton = new Button(HandleEmptyStateAction);
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
                schedule.Execute(FitCurrentContext).ExecuteLater(80);
            }
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
            AddPackageAnchor(anchors, focusedPackageId);
            AddPackageAnchor(anchors, selectedPackageId);
            AddPackageAnchor(anchors, previousFocusedPackageId);
            AddGroupAnchor(anchors, focusedGroupId);
            AddPackageGroupAnchors(graph, anchors, focusedPackageId);
            AddPackageGroupAnchors(graph, anchors, selectedPackageId);
            AddPackageGroupAnchors(graph, anchors, previousFocusedPackageId);
            AddGroupAnchor(anchors, previousFocusedGroupId);
            AddAncestorGroupAnchors(graph, anchors, focusedGroupId);
            AddAncestorGroupAnchors(graph, anchors, previousFocusedGroupId);
            AddAnchor(anchors, PackageGraphTransitionAnchor.Root);
            return anchors.ToArray();
        }

        private static void AddPackageAnchor(
            ICollection<PackageGraphTransitionAnchor> anchors,
            string packageId)
        {
            if (!string.IsNullOrWhiteSpace(packageId))
            {
                AddAnchor(
                    anchors,
                    new PackageGraphTransitionAnchor(PackageGraphTransitionAnchorKind.Package, packageId.Trim()));
            }
        }

        private static void AddGroupAnchor(
            ICollection<PackageGraphTransitionAnchor> anchors,
            string groupId)
        {
            if (!string.IsNullOrWhiteSpace(groupId))
            {
                AddAnchor(
                    anchors,
                    new PackageGraphTransitionAnchor(PackageGraphTransitionAnchorKind.Group, groupId.Trim()));
            }
        }

        private static void AddAnchor(
            ICollection<PackageGraphTransitionAnchor> anchors,
            PackageGraphTransitionAnchor anchor)
        {
            if (!anchors.Contains(anchor))
            {
                anchors.Add(anchor);
            }
        }

        private static void AddPackageGroupAnchors(
            PackageGraphModel graph,
            ICollection<PackageGraphTransitionAnchor> anchors,
            string packageId)
        {
            if (graph == null ||
                string.IsNullOrWhiteSpace(packageId) ||
                !graph.TryGetNode(packageId, out PackageGraphNode node))
            {
                return;
            }

            AddGroupAnchor(anchors, node.GroupId);
            AddAncestorGroupAnchors(graph, anchors, node.GroupId);
        }

        private static void AddAncestorGroupAnchors(
            PackageGraphModel graph,
            ICollection<PackageGraphTransitionAnchor> anchors,
            string groupId)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = groupId;

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                AddGroupAnchor(anchors, group.Id);
                currentGroupId = group.ParentGroupId;
            }
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

                if (AreBoundsClose(beforeBounds, afterBounds))
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

        private static bool TryGetLayoutAnchorCenter(
            PackageGraphLayoutResult layout,
            PackageGraphTransitionAnchor anchor,
            out Vector2 center)
        {
            center = default(Vector2);

            if (layout == null)
            {
                return false;
            }

            switch (anchor.Kind)
            {
                case PackageGraphTransitionAnchorKind.Package:
                    if (layout.NodeRects.TryGetValue(anchor.Id, out Rect nodeRect))
                    {
                        center = nodeRect.center;
                        return true;
                    }

                    break;
                case PackageGraphTransitionAnchorKind.Group:
                    PackageGraphGroupLayoutNode groupNode = layout.GroupNodes.FirstOrDefault(candidate =>
                        candidate != null &&
                        string.Equals(candidate.GroupId, anchor.Id, StringComparison.OrdinalIgnoreCase));

                    if (groupNode != null)
                    {
                        center = groupNode.HubCenter;
                        return true;
                    }

                    break;
                default:
                    center = layout.HubRect.center;
                    return true;
            }

            return false;
        }

        private static bool AreBoundsClose(Rect first, Rect second)
        {
            return Mathf.Abs(first.x - second.x) < 0.5f &&
                   Mathf.Abs(first.y - second.y) < 0.5f &&
                   Mathf.Abs(first.width - second.width) < 0.5f &&
                   Mathf.Abs(first.height - second.height) < 0.5f;
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

            PackageGraphGroup[] groupPath = CreateGroupPath(graph, focusedGroupId, focusedPackageId);
            PackageGraphNode focusedPackage = null;

            if (graph != null &&
                !string.IsNullOrWhiteSpace(focusedPackageId) &&
                graph.TryGetNode(focusedPackageId, out PackageGraphNode packageNode))
            {
                focusedPackage = packageNode;
            }

            if (groupPath.Length == 0 && focusedPackage == null)
            {
                _breadcrumbRow.Add(CreateBreadcrumbCurrent("Deucarian"));
            }
            else
            {
                _breadcrumbRow.Add(CreateBreadcrumbButton("Deucarian", () =>
                {
                    NavigateToRoot();
                }));
            }

            for (int index = 0; index < groupPath.Length; index++)
            {
                PackageGraphGroup group = groupPath[index];
                bool currentGroup = focusedPackage == null && index == groupPath.Length - 1;
                _breadcrumbRow.Add(CreateBreadcrumbSeparator());

                if (currentGroup)
                {
                    _breadcrumbRow.Add(CreateBreadcrumbCurrent(group.DisplayName));
                    continue;
                }

                _breadcrumbRow.Add(CreateBreadcrumbButton(group.DisplayName, () =>
                {
                    NavigateToGroup(group);
                }));
            }

            if (focusedPackage != null)
            {
                _breadcrumbRow.Add(CreateBreadcrumbSeparator());
                _breadcrumbRow.Add(CreateBreadcrumbCurrent(focusedPackage.DisplayName));
            }

            _breadcrumbRow.EnableInClassList(
                "dpi-ecosystem-graph__breadcrumbs--root",
                groupPath.Length == 0 && focusedPackage == null);
        }

        private static PackageGraphGroup[] CreateGroupPath(
            PackageGraphModel graph,
            string focusedGroupId,
            string focusedPackageId)
        {
            if (graph == null || graph.Groups.Count == 0)
            {
                return Array.Empty<PackageGraphGroup>();
            }

            string groupId = focusedGroupId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(groupId) &&
                !string.IsNullOrWhiteSpace(focusedPackageId) &&
                graph.TryGetNode(focusedPackageId, out PackageGraphNode focusedPackage))
            {
                groupId = focusedPackage.GroupId;
            }

            List<PackageGraphGroup> path = new List<PackageGraphGroup>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(groupId) &&
                   visited.Add(groupId) &&
                   graph.TryGetGroup(groupId, out PackageGraphGroup group))
            {
                path.Add(group);
                groupId = group.ParentGroupId;
            }

            path.Reverse();
            return path.ToArray();
        }

        private static Button CreateBreadcrumbButton(string text, Action clicked)
        {
            Button button = new Button(clicked)
            {
                text = text,
                tooltip = "Navigate to " + text
            };
            button.AddToClassList("dpi-ecosystem-graph__breadcrumb");
            return button;
        }

        private static Label CreateBreadcrumbCurrent(string text)
        {
            Label label = new Label(text);
            label.AddToClassList("dpi-ecosystem-graph__breadcrumb-current");
            return label;
        }

        private static Label CreateBreadcrumbSeparator()
        {
            Label separator = new Label(">");
            separator.AddToClassList("dpi-ecosystem-graph__breadcrumb-separator");
            return separator;
        }

        internal string ActiveHoverGroupId => _canvas.ActiveHoverGroupId;

        internal string ActiveTopLevelHoverGroupId => ResolveTopLevelGroupId(_currentGraph, _canvas.ActiveHoverGroupId);

        internal void SetExternalGroupHover(string groupId)
        {
            _canvas.SetExternalHoverGroup(groupId);
        }

        internal void ClearExternalGroupHover(string groupId)
        {
            _canvas.ClearExternalHoverGroup(groupId);
        }

        internal void ClearHoverState()
        {
            _canvas.ClearHoverState();
        }

        private static string ResolveTopLevelGroupId(PackageGraphModel graph, string groupId)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return string.Empty;
            }

            string currentGroupId = groupId;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                if (string.IsNullOrWhiteSpace(group.ParentGroupId))
                {
                    return group.Id;
                }

                currentGroupId = group.ParentGroupId;
            }

            return string.Empty;
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
            UpdateFilterToggleButton(
                _installedFilterButton,
                _filterState.ShowInstalled,
                "Installed",
                counts.InstalledCount,
                "installed",
                InstalledStatusMarker);
            UpdateFilterToggleButton(
                _notInstalledFilterButton,
                _filterState.ShowNotInstalled,
                "Not installed",
                counts.NotInstalledCount,
                "not-installed",
                NotInstalledStatusMarker);

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
            _emptyStateActionButton.text = showAction ? actionText ?? string.Empty : string.Empty;
            _emptyStateActionButton.tooltip = showAction ? actionTooltip ?? string.Empty : string.Empty;
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

        private static Button CreateFilterToggleButton(Action action, string iconClass)
        {
            Button button = new Button(action);
            button.AddToClassList("dpi-ecosystem-graph__filter-toggle");
            button.AddToClassList("dpi-ecosystem-graph__filter-toggle--" + iconClass);
            return button;
        }

        private static void UpdateFilterToggleButton(
            Button button,
            bool active,
            string label,
            int count,
            string iconClass,
            string iconText)
        {
            if (button == null)
            {
                return;
            }

            button.text = string.Empty;
            button.Clear();
            button.Add(CreateFilterIcon(iconClass, iconText));
            button.Add(CreateFilterLabel(label));
            button.Add(CreateFilterCount(count));
            button.tooltip = (active ? "Hide " : "Show ") +
                             label.ToLowerInvariant() +
                             " packages. " +
                             count +
                             " match the active search.";
            button.EnableInClassList("dpi-ecosystem-graph__filter-toggle--active", active);
            button.EnableInClassList("dpi-ecosystem-graph__filter-toggle--inactive", !active);
        }

        private static Label CreateFilterIcon(string iconClass, string iconText)
        {
            Label icon = new Label(iconText);
            icon.pickingMode = PickingMode.Ignore;
            icon.AddToClassList("dpi-ecosystem-graph__filter-icon");
            icon.AddToClassList("dpi-ecosystem-graph__filter-icon--" + iconClass);
            return icon;
        }

        private static Label CreateFilterLabel(string label)
        {
            Label text = new Label(label);
            text.pickingMode = PickingMode.Ignore;
            text.AddToClassList("dpi-ecosystem-graph__filter-label");
            return text;
        }

        private static Label CreateFilterCount(int count)
        {
            Label text = new Label(count.ToString());
            text.pickingMode = PickingMode.Ignore;
            text.AddToClassList("dpi-ecosystem-graph__filter-count");
            return text;
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
                _legend.Add(CreateLegendItem(
                    "Flow",
                    "Dependency flow",
                    "dpi-graph-legend__line--solid",
                    "Required package -> dependent package. Animated flow markers show direction."));
                _legend.Add(CreateLegendItem(
                    "Cable",
                    "Integration connection",
                    "dpi-graph-legend__line--integration",
                    "Integration package connects systems. Animated markers show direction."));
                _legend.Add(CreateLegendItem(
                    "Dotted",
                    "Optional companion",
                    "dpi-graph-legend__line--optional",
                    "Recommended alongside, not required"));
                _legend.Add(CreateLegendItem(
                    "Line",
                    "Suite membership",
                    "dpi-graph-legend__line--suite",
                    "Package belongs to a curated bundle"));
                AddTransientStatusLegendItems(hasAttention, hasChecking);
                return;
            }

            if (layoutMode == PackageGraphLayoutMode.GroupFocus)
            {
                _legend.Add(CreateLegendItem("Group", "Group", "dpi-graph-legend__line--group"));
                _legend.Add(CreateLegendItem("Pkg", "Package", "dpi-graph-legend__line--package"));
                _legend.Add(CreateLegendItem("Line", "Structural membership", "dpi-graph-legend__line--membership", "Structural group membership, not a dependency"));
                AddTransientStatusLegendItems(hasAttention, hasChecking);
                return;
            }

            _legend.Add(CreateLegendItem("Root", "Deucarian root", "dpi-graph-legend__line--root"));
            _legend.Add(CreateLegendItem("Group", "Group", "dpi-graph-legend__line--group"));
            _legend.Add(CreateLegendItem("Pkg", "Package", "dpi-graph-legend__line--package"));
            _legend.Add(CreateLegendItem(InstalledStatusMarker, "Installed", "dpi-graph-legend__line--installed"));
            _legend.Add(CreateLegendItem(NotInstalledStatusMarker, "Not installed", "dpi-graph-legend__line--available"));
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
                _legend.Add(CreateLegendItem("...", "Checking", "dpi-graph-legend__line--checking"));
            }

            if (hasAttention)
            {
                _legend.Add(CreateLegendItem("!", "Attention", "dpi-graph-legend__line--warning"));
            }
        }

        private static VisualElement CreateLegendItem(
            string marker,
            string label,
            string markerClass,
            string tooltip = null)
        {
            VisualElement item = new VisualElement();
            item.AddToClassList("dpi-graph-legend__item");
            item.tooltip = tooltip ?? label;

            Label markerLabel = new Label(marker);
            markerLabel.AddToClassList("dpi-graph-legend__line");
            markerLabel.AddToClassList(markerClass);
            item.Add(markerLabel);

            Label labelElement = new Label(label);
            labelElement.AddToClassList("dpi-graph-legend__label");
            item.Add(labelElement);

            return item;
        }

        private static Button CreateToolbarButton(string label, Action action)
        {
            Button button = new Button(action)
            {
                text = label,
                tooltip = label
            };
            button.AddToClassList("dpi-ecosystem-graph__toolbar-button");
            return button;
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
            else if (HasAncestorClass(request.Target, "dpi-graph-hub"))
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
            menu.AddItem(new GUIContent("Fit current context"), false, FitCurrentContext);
            menu.AddItem(new GUIContent("Center current focus"), false, CenterCurrentContext);
            menu.AddItem(new GUIContent("100%"), false, ResetCurrentContextZoom);

            if (!IsOverviewLikeLayout(_canvas.LayoutMode))
            {
                menu.AddItem(new GUIContent("Back one hierarchy level"), false, NavigateBackOneLevel);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Back one hierarchy level"));
            }

            menu.AddItem(new GUIContent("Ecosystem Overview"), false, NavigateToRoot);
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
                menu.AddDisabledItem(new GUIContent("Missing Registry Target"));
                menu.AddItem(
                    new GUIContent("Copy Package ID"),
                    false,
                    () => EditorGUIUtility.systemCopyBuffer = packageNode.PackageId);
                menu.AddItem(
                    new GUIContent("Copy Diagnostic"),
                    false,
                    () => EditorGUIUtility.systemCopyBuffer = diagnostic);
                return;
            }

            PackageDefinition packageDefinition = packageNode.PackageDefinition;
            menu.AddItem(new GUIContent("Focus / Select Package"), false, () => SelectPackage(packageDefinition));
            menu.AddItem(new GUIContent("Show Package Details"), false, () => SelectPackage(packageDefinition));
            menu.AddItem(new GUIContent("Copy Package ID"), false, () => EditorGUIUtility.systemCopyBuffer = packageDefinition.PackageId);

            string repositoryUrl = GetRepositoryUrl(packageDefinition);
            if (!string.IsNullOrWhiteSpace(repositoryUrl))
            {
                menu.AddItem(new GUIContent("Open Repository"), false, () => Application.OpenURL(repositoryUrl));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Open Repository"));
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
            menu.AddItem(new GUIContent("Focus Category"), false, () => NavigateToGroup(group));
            menu.AddItem(new GUIContent("Fit Category"), false, () =>
            {
                if (!active)
                {
                    NavigateToGroup(group);
                }

                schedule.Execute(FitCurrentContext).ExecuteLater(1);
            });

            if (active)
            {
                menu.AddItem(new GUIContent("Back to Parent"), false, NavigateBackOneLevel);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Back to Parent"));
            }

            menu.AddItem(new GUIContent("Ecosystem Overview"), false, NavigateToRoot);
        }

        private void PopulateRootContextMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Ecosystem Overview"), false, NavigateToRoot);
            menu.AddItem(new GUIContent("Fit Overview"), false, FitCurrentContext);
            menu.AddItem(new GUIContent("Center Root"), false, () =>
            {
                _viewport.CenterOn(PackageGraphLayout.GraphCenter);
            });
            menu.AddItem(new GUIContent("100%"), false, () =>
            {
                _viewport.ResetZoom(PackageGraphLayout.GraphCenter);
            });
        }

        private bool TryResolvePackageFromTarget(VisualElement target, out PackageGraphNode packageNode)
        {
            packageNode = null;
            VisualElement nodeElement = FindAncestorWithClass(target, "dpi-graph-node");

            return nodeElement != null &&
                   _currentGraph != null &&
                   _currentGraph.TryGetNode(nodeElement.name, out packageNode);
        }

        private bool TryResolveGroupFromTarget(VisualElement target, out PackageGraphGroup group)
        {
            group = null;
            VisualElement groupElement = FindAncestorWithClass(target, "dpi-graph-group");

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

        private static bool HasAncestorClass(VisualElement target, string className)
        {
            return FindAncestorWithClass(target, className) != null;
        }

        private static VisualElement FindAncestorWithClass(VisualElement target, string className)
        {
            VisualElement current = target;

            while (current != null)
            {
                if (current.ClassListContains(className))
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }
    }

    internal sealed class PackageGraphContextMenuRequest
    {
        public PackageGraphContextMenuRequest(VisualElement target, Vector2 viewportPosition, Vector2 worldPosition)
        {
            Target = target;
            ViewportPosition = viewportPosition;
            WorldPosition = worldPosition;
        }

        public VisualElement Target { get; }

        public Vector2 ViewportPosition { get; }

        public Vector2 WorldPosition { get; }
    }

    internal enum PackageGraphSpotlightKind
    {
        None,
        Root,
        Category,
        Package,
        Attention
    }

    internal sealed class PackageGraphSpotlightLayer : VisualElement
    {
        public const string LayerName = "ecosystem-graph-spotlight-layer";
        private const string SpotlightName = "ecosystem-graph-spotlight";

        private static Texture2D _rootTexture;
        private static Texture2D _categoryTexture;
        private static Texture2D _packageTexture;
        private static Texture2D _attentionTexture;

        private readonly VisualElement _spotlight;

        public PackageGraphSpotlightLayer()
        {
            name = LayerName;
            AddToClassList("dpi-graph-spotlight-layer");
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0f;
            style.right = 0f;
            style.top = 0f;
            style.bottom = 0f;
            style.overflow = Overflow.Hidden;

            _spotlight = new VisualElement { name = SpotlightName };
            _spotlight.AddToClassList("dpi-graph-spotlight");
            _spotlight.pickingMode = PickingMode.Ignore;
            _spotlight.style.position = Position.Absolute;
            _spotlight.style.display = DisplayStyle.None;
            Add(_spotlight);
        }

        internal VisualElement SpotlightForTests => _spotlight;

        public void SetSpotlight(
            Vector2 viewportCenter,
            float radius,
            PackageGraphSpotlightKind kind,
            bool visible)
        {
            if (!visible || kind == PackageGraphSpotlightKind.None || radius <= 1f)
            {
                _spotlight.style.display = DisplayStyle.None;
                return;
            }

            float diameter = radius * 2f;
            _spotlight.style.display = DisplayStyle.Flex;
            _spotlight.style.left = viewportCenter.x - radius;
            _spotlight.style.top = viewportCenter.y - radius;
            _spotlight.style.width = diameter;
            _spotlight.style.height = diameter;
            _spotlight.style.backgroundImage = new StyleBackground(GetTexture(kind));
            _spotlight.style.opacity = GetOpacity(kind);
            _spotlight.EnableInClassList("dpi-graph-spotlight--root", kind == PackageGraphSpotlightKind.Root);
            _spotlight.EnableInClassList("dpi-graph-spotlight--category", kind == PackageGraphSpotlightKind.Category);
            _spotlight.EnableInClassList("dpi-graph-spotlight--package", kind == PackageGraphSpotlightKind.Package);
            _spotlight.EnableInClassList("dpi-graph-spotlight--attention", kind == PackageGraphSpotlightKind.Attention);
            MarkDirtyRepaint();
        }

        private static float GetOpacity(PackageGraphSpotlightKind kind)
        {
            switch (kind)
            {
                case PackageGraphSpotlightKind.Root:
                    return 0.28f;
                case PackageGraphSpotlightKind.Category:
                    return 0.38f;
                case PackageGraphSpotlightKind.Attention:
                    return 0.44f;
                case PackageGraphSpotlightKind.Package:
                    return 0.42f;
                default:
                    return 0f;
            }
        }

        private static Texture2D GetTexture(PackageGraphSpotlightKind kind)
        {
            switch (kind)
            {
                case PackageGraphSpotlightKind.Root:
                    return _rootTexture ?? (_rootTexture = CreateSpotlightTexture(
                        new Color(0.10f, 0.45f, 0.82f, 0.30f),
                        new Color(0.02f, 0.08f, 0.12f, 0f)));
                case PackageGraphSpotlightKind.Category:
                    return _categoryTexture ?? (_categoryTexture = CreateSpotlightTexture(
                        new Color(0.12f, 0.68f, 0.62f, 0.34f),
                        new Color(0.02f, 0.07f, 0.10f, 0f)));
                case PackageGraphSpotlightKind.Attention:
                    return _attentionTexture ?? (_attentionTexture = CreateSpotlightTexture(
                        new Color(0.86f, 0.56f, 0.15f, 0.30f),
                        new Color(0.08f, 0.05f, 0.02f, 0f)));
                case PackageGraphSpotlightKind.Package:
                default:
                    return _packageTexture ?? (_packageTexture = CreateSpotlightTexture(
                        new Color(0.12f, 0.78f, 0.72f, 0.36f),
                        new Color(0.01f, 0.07f, 0.10f, 0f)));
            }
        }

        private static Texture2D CreateSpotlightTexture(Color center, Color edge)
        {
            const int size = 96;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PackageGraphSpotlight",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 midpoint = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float maxDistance = Mathf.Max(1f, midpoint.magnitude);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), midpoint) / maxDistance;
                    float t = Mathf.SmoothStep(0f, 1f, distance);
                    texture.SetPixel(x, y, Color.Lerp(center, edge, t));
                }
            }

            texture.Apply(false, true);
            return texture;
        }
    }

    internal sealed class PackageGraphViewport : VisualElement
    {
        private const float OverviewMinZoom = 0.28f;
        private const float FocusMinZoom = 0.42f;
        private const float AbsoluteMinZoom = 0.10f;
        private const float MaxZoom = 1.75f;
        private const float FitMarginRatio = 0.10f;
        private const float MinFitPadding = 28f;
        private const float MaxFitPadding = 88f;
        private const float PanMargin = 220f;
        private const float PanDragThreshold = 5f;
        private const float PanDragThresholdSqr = PanDragThreshold * PanDragThreshold;

        private readonly PackageGraphSpotlightLayer _spotlightLayer;
        private readonly VisualElement _contentRoot;
        private readonly Action _selectionCleared;
        private Vector2 _pan;
        private Vector2 _lastMousePosition;
        private Vector2 _mouseDownPosition;
        private float _zoom = 1f;
        private float _contentWidth = PackageGraphLayout.CanvasWidth;
        private float _contentHeight = PackageGraphLayout.CanvasHeight;
        private Vector2 _lastReportedViewportSize;
        private VisualElement _mouseDownTarget;
        private PackageGraphLayoutMode _layoutMode = PackageGraphLayoutMode.Overview;
        private Rect _initialBounds;
        private Rect _activeBounds = default(Rect);
        private bool _initialized;
        private bool _hasInitialBounds;
        private bool _cameraTransitionActive;
        private bool _panCandidate;
        private bool _panning;
        private bool _panMoved;
        private int _panButton = -1;
        private float _requiredFitZoom = 1f;
        private bool _suppressNextClick;
        private double _cameraTransitionStartedAt;
        private IVisualElementScheduledItem _cameraTransitionItem;
        private PackageGraphCameraState _cameraTransitionSource;
        private PackageGraphCameraState _cameraTransitionTarget;
        private Vector2 _cameraTransitionSourceAnchorWorld;
        private Vector2 _cameraTransitionTargetAnchorWorld;
        private Vector2 _cameraTransitionSourceAnchorScreen;
        private Vector2 _cameraTransitionTargetAnchorScreen;
        private bool _cameraTransitionUsesDirectTarget;
        private Vector2 _spotlightWorldCenter;
        private PackageGraphSpotlightKind _spotlightKind = PackageGraphSpotlightKind.None;
        private bool _spotlightActive;

        public PackageGraphViewport(Action selectionCleared)
        {
            _selectionCleared = selectionCleared;
            name = "ecosystem-graph-viewport";
            AddToClassList("dpi-ecosystem-graph__viewport");
            focusable = true;

            _spotlightLayer = new PackageGraphSpotlightLayer();
            Add(_spotlightLayer);

            _contentRoot = new VisualElement { name = "ecosystem-graph-content" };
            _contentRoot.AddToClassList("dpi-ecosystem-graph__content");
            _contentRoot.style.position = Position.Absolute;
            _contentRoot.style.left = 0f;
            _contentRoot.style.top = 0f;
            _contentRoot.style.width = _contentWidth;
            _contentRoot.style.height = _contentHeight;
            Add(_contentRoot);

            RegisterCallback<WheelEvent>(HandleWheel);
            RegisterCallback<MouseDownEvent>(HandleMouseDown, TrickleDown.TrickleDown);
            RegisterCallback<MouseMoveEvent>(HandleMouseMove);
            RegisterCallback<MouseUpEvent>(HandleMouseUp);
            RegisterCallback<DetachFromPanelEvent>(_ => CancelPan());
            RegisterCallback<BlurEvent>(_ => CancelPan());
            RegisterCallback<ClickEvent>(HandleClickCapture, TrickleDown.TrickleDown);
            RegisterCallback<ClickEvent>(HandleClick);
            RegisterCallback<KeyDownEvent>(HandleKeyDown);
            RegisterCallback<MouseCaptureOutEvent>(_ => ResetPanState());
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                ReportViewportSizeIfNeeded();

                if (!_initialized && _hasInitialBounds)
                {
                    FitToContent(_initialBounds);
                    return;
                }

                ClampAndApplyTransform();
            });

            ApplyTransform();
        }

        public event Action<Vector2> ViewportSizeChanged;

        public event Action<float> ZoomChanged;

        public event Action<float> CameraTransitionCompleted;

        public event Action<PackageGraphContextMenuRequest> ContextMenuRequested;

        public float Zoom => _zoom;

        public Vector2 Pan => _pan;

        public bool IsCameraTransitionActive => _cameraTransitionActive;

        public Vector2 ViewportSize => HasViewportSize()
            ? new Vector2(contentRect.width, contentRect.height)
            : Vector2.zero;

        internal float EffectiveMinZoomForTests => GetMinZoom();

        internal float RequiredFitZoomForTests => _requiredFitZoom;

        internal PackageGraphSpotlightLayer SpotlightLayerForTests => _spotlightLayer;

        internal VisualElement ContentRootForTests => _contentRoot;

        public void SetLayoutMode(PackageGraphLayoutMode layoutMode, bool clampZoom = true)
        {
            _layoutMode = layoutMode;
            UpdateRequiredFitZoom(_activeBounds);

            if (clampZoom && _zoom < GetMinZoom())
            {
                _zoom = GetMinZoom();
                ClampAndApplyTransform();
            }
        }

        public void SetContent(VisualElement content)
        {
            _contentRoot.Clear();

            if (content != null)
            {
                _contentRoot.Add(content);
            }
        }

        public void SetSpotlightWorldCenter(Vector2 worldCenter, PackageGraphSpotlightKind kind)
        {
            _spotlightWorldCenter = worldCenter;
            _spotlightKind = kind;
            _spotlightActive = kind != PackageGraphSpotlightKind.None;
            UpdateSpotlight();
        }

        public void SetContentSize(float width, float height, bool clamp = true)
        {
            _contentWidth = Mathf.Max(1f, width);
            _contentHeight = Mathf.Max(1f, height);
            _contentRoot.style.width = _contentWidth;
            _contentRoot.style.height = _contentHeight;
            UpdateRequiredFitZoom(_activeBounds);

            if (clamp)
            {
                ClampAndApplyTransform();
            }
            else
            {
                ApplyTransform();
            }
        }

        public void SetActiveBounds(Rect worldBounds)
        {
            _activeBounds = NormalizeBounds(worldBounds);
            UpdateRequiredFitZoom(_activeBounds);
        }

        public void EnsureInitialFrame(Rect worldBounds, bool force = false)
        {
            _initialBounds = worldBounds;
            _hasInitialBounds = true;
            SetActiveBounds(worldBounds);

            if (force)
            {
                _initialized = false;
            }

            if (_initialized || !HasViewportSize())
            {
                return;
            }

            FitToContent(worldBounds);
        }

        public void FitToContent(Rect worldBounds)
        {
            if (!HasViewportSize())
            {
                return;
            }

            StopCameraTransition();
            Rect bounds = NormalizeBounds(worldBounds);
            SetActiveBounds(bounds);
            PackageGraphCameraState camera = CalculateFitCamera(bounds);
            _zoom = camera.Zoom;
            _pan = camera.Pan;
            _initialized = true;
            ClampAndApplyTransform();
            ZoomChanged?.Invoke(_zoom);
        }

        public void AnimateToContent(
            Rect worldBounds,
            Vector2 sourceAnchorWorld,
            Vector2 targetAnchorWorld,
            Vector2 sourceAnchorScreen)
        {
            if (!HasViewportSize())
            {
                return;
            }

            Rect bounds = NormalizeBounds(worldBounds);
            SetActiveBounds(bounds);
            PackageGraphCameraState targetCamera = CalculateFitCamera(bounds);
            PackageGraphCameraState sourceCamera = new PackageGraphCameraState(_pan, _zoom);
            _cameraTransitionSource = sourceCamera;
            _cameraTransitionTarget = targetCamera;
            _cameraTransitionSourceAnchorWorld = sourceAnchorWorld;
            _cameraTransitionTargetAnchorWorld = targetAnchorWorld;
            _cameraTransitionSourceAnchorScreen = sourceAnchorScreen;
            _cameraTransitionTargetAnchorScreen = targetCamera.WorldToViewport(targetAnchorWorld);
            _cameraTransitionUsesDirectTarget = false;
            _cameraTransitionStartedAt = EditorApplication.timeSinceStartup;
            _cameraTransitionActive = true;
            _initialized = true;

            StartCameraTransitionSchedule();
        }

        public PackageGraphCameraState GetCameraState()
        {
            return new PackageGraphCameraState(_pan, _zoom);
        }

        public void ApplyPreviewCamera(PackageGraphCameraState camera)
        {
            _zoom = Mathf.Clamp(camera.Zoom, AbsoluteMinZoom, MaxZoom);
            _pan = ClampPan(camera.Pan, _zoom);
            _initialized = true;
            ApplyTransform();
        }

        public void AnimateToCamera(PackageGraphCameraState targetCamera)
        {
            if (!HasViewportSize())
            {
                ApplyPreviewCamera(targetCamera);
                return;
            }

            float targetZoom = Mathf.Clamp(targetCamera.Zoom, AbsoluteMinZoom, MaxZoom);
            _cameraTransitionSource = new PackageGraphCameraState(_pan, _zoom);
            _cameraTransitionTarget = new PackageGraphCameraState(
                ClampPan(targetCamera.Pan, targetZoom),
                targetZoom);
            _cameraTransitionUsesDirectTarget = true;
            _cameraTransitionStartedAt = EditorApplication.timeSinceStartup;
            _cameraTransitionActive = true;
            _initialized = true;
            StartCameraTransitionSchedule();
        }

        public PackageGraphCameraState CalculateFitCamera(Rect worldBounds)
        {
            return CalculateFitCamera(worldBounds, _layoutMode);
        }

        public PackageGraphCameraState CalculateFitCamera(Rect worldBounds, PackageGraphLayoutMode layoutMode)
        {
            Rect bounds = NormalizeBounds(worldBounds);
            float fitZoom = CalculateFitZoom(bounds);
            float zoom = Mathf.Clamp(fitZoom, GetMinZoom(layoutMode), MaxZoom);
            Vector2 pan = GetViewportCenter() - bounds.center * zoom;
            return new PackageGraphCameraState(ClampPan(pan, zoom), zoom);
        }

        public void ResetZoom()
        {
            ResetZoom(ViewportToWorld(GetViewportCenter()));
        }

        public void ResetZoom(Vector2 worldCenter)
        {
            if (!HasViewportSize())
            {
                return;
            }

            StopCameraTransition();
            Vector2 viewportCenter = GetViewportCenter();
            _zoom = 1f;
            _pan = viewportCenter - worldCenter * _zoom;
            _initialized = true;
            ClampAndApplyTransform();
            ZoomChanged?.Invoke(_zoom);
        }

        public void CenterOn(Vector2 worldPoint)
        {
            if (!HasViewportSize())
            {
                return;
            }

            StopCameraTransition();
            _pan = GetViewportCenter() - worldPoint * _zoom;
            _initialized = true;
            ClampAndApplyTransform();
        }

        public Vector2 WorldToViewport(Vector2 worldPoint)
        {
            return worldPoint * _zoom + _pan;
        }

        public Vector2 ViewportToWorld(Vector2 viewportPoint)
        {
            return (viewportPoint - _pan) / Mathf.Max(0.001f, _zoom);
        }

        private void HandleWheel(WheelEvent evt)
        {
            if (!HasViewportSize() || Mathf.Abs(evt.delta.y) <= 0.01f)
            {
                return;
            }

            float zoomMultiplier = evt.delta.y > 0f ? 0.90f : 1.10f;
            ZoomAround(evt.localMousePosition, _zoom * zoomMultiplier);
            evt.StopPropagation();
        }

        private void HandleMouseDown(MouseDownEvent evt)
        {
            if (!ShouldConsiderPan(evt))
            {
                return;
            }

            _panCandidate = true;
            _panning = false;
            _panButton = evt.button;
            _mouseDownTarget = evt.target as VisualElement;
            _lastMousePosition = evt.localMousePosition;
            _mouseDownPosition = evt.localMousePosition;
            _panMoved = false;
            MouseCaptureController.CaptureMouse(this);
            Focus();
            evt.StopPropagation();
        }

        private void HandleMouseMove(MouseMoveEvent evt)
        {
            if (!_panCandidate || !MouseCaptureController.HasMouseCapture(this))
            {
                return;
            }

            Vector2 nextMousePosition = evt.localMousePosition;
            Vector2 totalDelta = nextMousePosition - _mouseDownPosition;

            if (!_panning && totalDelta.sqrMagnitude > PanDragThresholdSqr)
            {
                _panning = true;
                _panMoved = true;
            }

            if (_panning)
            {
                _pan += nextMousePosition - _lastMousePosition;
            }

            _lastMousePosition = nextMousePosition;
            _initialized = true;
            ClampAndApplyTransform();
            evt.StopPropagation();
        }

        private void HandleMouseUp(MouseUpEvent evt)
        {
            if (!_panCandidate)
            {
                return;
            }

            bool openedMenu = false;
            if (_panButton == 1 && !_panMoved)
            {
                ContextMenuRequested?.Invoke(new PackageGraphContextMenuRequest(
                    _mouseDownTarget,
                    evt.localMousePosition,
                    ViewportToWorld(evt.localMousePosition)));
                openedMenu = true;
            }

            _suppressNextClick = _panMoved || openedMenu;
            ResetPanState();

            if (MouseCaptureController.HasMouseCapture(this))
            {
                MouseCaptureController.ReleaseMouse(this);
            }

            evt.StopPropagation();
        }

        private void HandleClickCapture(ClickEvent evt)
        {
            if (!_suppressNextClick)
            {
                return;
            }

            _suppressNextClick = false;
            evt.StopImmediatePropagation();
        }

        private void HandleClick(ClickEvent evt)
        {
            Focus();

            if (evt.button != 0)
            {
                return;
            }

            evt.StopPropagation();
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            _selectionCleared?.Invoke();
            evt.StopPropagation();
        }

        private void ZoomAround(Vector2 viewportPoint, float targetZoom)
        {
            float nextZoom = Mathf.Clamp(targetZoom, GetMinZoom(), MaxZoom);

            if (Mathf.Approximately(nextZoom, _zoom))
            {
                return;
            }

            Vector2 worldPoint = ViewportToWorld(viewportPoint);
            StopCameraTransition();
            _zoom = nextZoom;
            _pan = viewportPoint - worldPoint * _zoom;
            _initialized = true;
            ClampAndApplyTransform();
            ZoomChanged?.Invoke(_zoom);
        }

        private void UpdateCameraTransition()
        {
            if (!_cameraTransitionActive)
            {
                return;
            }

            float elapsed = (float)(EditorApplication.timeSinceStartup - _cameraTransitionStartedAt);
            float t = Mathf.Clamp01(elapsed / PackageGraphTransition.DefaultDurationSeconds);
            PackageGraphCameraState camera = _cameraTransitionUsesDirectTarget
                ? PackageGraphTransition.EvaluateCamera(
                    _cameraTransitionSource,
                    _cameraTransitionTarget,
                    t)
                : PackageGraphTransition.EvaluateAnchoredCamera(
                    _cameraTransitionSource,
                    _cameraTransitionTarget,
                    _cameraTransitionSourceAnchorWorld,
                    _cameraTransitionTargetAnchorWorld,
                    _cameraTransitionSourceAnchorScreen,
                    _cameraTransitionTargetAnchorScreen,
                    t);
            _zoom = camera.Zoom;
            _pan = ClampPan(camera.Pan, _zoom);
            _initialized = true;
            ApplyTransform();

            if (t < 1f)
            {
                return;
            }

            _cameraTransitionActive = false;
            _cameraTransitionItem?.Pause();
            _zoom = _cameraTransitionTarget.Zoom;
            _pan = _cameraTransitionTarget.Pan;
            ClampAndApplyTransform();
            CameraTransitionCompleted?.Invoke(_zoom);
        }

        private void StartCameraTransitionSchedule()
        {
            if (_cameraTransitionItem == null)
            {
                _cameraTransitionItem = schedule.Execute(UpdateCameraTransition)
                    .Every((long)(PackageGraphTransition.DefaultDurationSeconds * 1000f / 18f));
            }

            _cameraTransitionItem.Resume();
            UpdateCameraTransition();
        }

        private void StopCameraTransition()
        {
            if (!_cameraTransitionActive)
            {
                return;
            }

            _cameraTransitionActive = false;
            _cameraTransitionItem?.Pause();
        }

        private void ClampAndApplyTransform()
        {
            ClampPan();
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            _contentRoot.style.translate = new Translate(_pan.x, _pan.y, 0f);
            _contentRoot.style.scale = new Scale(new Vector3(_zoom, _zoom, 1f));
            _contentRoot.EnableInClassList("dpi-ecosystem-graph__content--low-zoom", _zoom < 0.58f);
            _contentRoot.EnableInClassList("dpi-ecosystem-graph__content--medium-zoom", _zoom >= 0.58f && _zoom < 1.05f);
            _contentRoot.EnableInClassList("dpi-ecosystem-graph__content--high-zoom", _zoom >= 1.05f);
            UpdateSpotlight();
            MarkDirtyRepaint();
            _contentRoot.MarkDirtyRepaint();
        }

        private void UpdateSpotlight()
        {
            if (_spotlightLayer == null)
            {
                return;
            }

            if (!_spotlightActive || !HasViewportSize())
            {
                _spotlightLayer.SetSpotlight(Vector2.zero, 0f, PackageGraphSpotlightKind.None, false);
                return;
            }

            _spotlightLayer.SetSpotlight(
                WorldToViewport(_spotlightWorldCenter),
                GetSpotlightRadius(_spotlightKind),
                _spotlightKind,
                true);
        }

        private static float GetSpotlightRadius(PackageGraphSpotlightKind kind)
        {
            switch (kind)
            {
                case PackageGraphSpotlightKind.Root:
                    return 360f;
                case PackageGraphSpotlightKind.Category:
                    return 300f;
                case PackageGraphSpotlightKind.Attention:
                    return 220f;
                case PackageGraphSpotlightKind.Package:
                    return 205f;
                default:
                    return 0f;
            }
        }

        private void ClampPan()
        {
            if (!HasViewportSize())
            {
                return;
            }

            _pan = ClampPan(_pan, _zoom);
        }

        private Vector2 ClampPan(Vector2 pan, float zoom)
        {
            if (!HasViewportSize())
            {
                return pan;
            }

            pan.x = ClampAxis(pan.x, _contentWidth * zoom, contentRect.width);
            pan.y = ClampAxis(pan.y, _contentHeight * zoom, contentRect.height);
            return pan;
        }

        private static float ClampAxis(float pan, float scaledContentSize, float viewportSize)
        {
            if (scaledContentSize <= viewportSize - PanMargin * 2f)
            {
                return (viewportSize - scaledContentSize) * 0.5f;
            }

            float min = viewportSize - scaledContentSize - PanMargin;
            float max = PanMargin;
            return Mathf.Clamp(pan, min, max);
        }

        private bool HasViewportSize()
        {
            return contentRect.width > 1f && contentRect.height > 1f;
        }

        private Vector2 GetViewportCenter()
        {
            return new Vector2(contentRect.width * 0.5f, contentRect.height * 0.5f);
        }

        private float GetMinZoom()
        {
            return GetMinZoom(_layoutMode);
        }

        private float GetMinZoom(PackageGraphLayoutMode layoutMode)
        {
            float configuredMinimum =
                layoutMode == PackageGraphLayoutMode.Overview
                    ? OverviewMinZoom
                    : FocusMinZoom;
            return CalculateEffectiveMinZoom(configuredMinimum, _requiredFitZoom);
        }

        private float GetFitPadding()
        {
            float smallestViewportAxis = Mathf.Min(contentRect.width, contentRect.height);
            return Mathf.Clamp(smallestViewportAxis * FitMarginRatio, MinFitPadding, MaxFitPadding);
        }

        private void UpdateRequiredFitZoom(Rect worldBounds)
        {
            _requiredFitZoom = HasViewportSize()
                ? Mathf.Max(AbsoluteMinZoom, CalculateFitZoom(NormalizeBounds(worldBounds)) * 0.96f)
                : 1f;
        }

        private float CalculateFitZoom(Rect worldBounds)
        {
            Rect bounds = NormalizeBounds(worldBounds);
            float fitPadding = GetFitPadding();
            float availableWidth = Mathf.Max(1f, contentRect.width - fitPadding * 2f);
            float availableHeight = Mathf.Max(1f, contentRect.height - fitPadding * 2f);
            return Mathf.Min(availableWidth / bounds.width, availableHeight / bounds.height);
        }

        private void ReportViewportSizeIfNeeded()
        {
            if (!HasViewportSize())
            {
                return;
            }

            Vector2 viewportSize = new Vector2(contentRect.width, contentRect.height);

            if (Mathf.Abs(viewportSize.x - _lastReportedViewportSize.x) < 1f &&
                Mathf.Abs(viewportSize.y - _lastReportedViewportSize.y) < 1f)
            {
                return;
            }

            _lastReportedViewportSize = viewportSize;
            ViewportSizeChanged?.Invoke(viewportSize);
        }

        private static Rect NormalizeBounds(Rect bounds)
        {
            if (bounds.width <= 0.01f || bounds.height <= 0.01f)
            {
                return new Rect(
                    PackageGraphLayout.GraphCenter.x - 1f,
                    PackageGraphLayout.GraphCenter.y - 1f,
                    2f,
                    2f);
            }

            return bounds;
        }

        private void CancelPan()
        {
            if (MouseCaptureController.HasMouseCapture(this))
            {
                MouseCaptureController.ReleaseMouse(this);
            }

            ResetPanState();
        }

        private void ResetPanState()
        {
            _panCandidate = false;
            _panning = false;
            _panMoved = false;
            _panButton = -1;
            _mouseDownTarget = null;
        }

        private static bool ShouldConsiderPan(MouseDownEvent evt)
        {
            return evt != null &&
                   ShouldConsiderPan(evt.target as VisualElement, evt.button, evt.altKey);
        }

        private static bool ShouldConsiderPan(VisualElement target, int button, bool altKey)
        {
            if (HasAncestorClass(target, "dpi-ecosystem-graph__empty-state"))
            {
                return false;
            }

            return button == 2 ||
                   button == 1 ||
                   (button == 0 && (altKey || IsLeftPanTarget(target)));
        }

        internal static bool IsLeftPanTargetForTests(VisualElement target)
        {
            return IsLeftPanTarget(target);
        }

        internal static bool ShouldConsiderPanForTests(VisualElement target, int button, bool altKey = false)
        {
            return ShouldConsiderPan(target, button, altKey);
        }

        internal static float CalculateEffectiveMinZoomForTests(float configuredMinimum, float requiredFitZoom)
        {
            return CalculateEffectiveMinZoom(configuredMinimum, requiredFitZoom);
        }

        private static float CalculateEffectiveMinZoom(float configuredMinimum, float requiredFitZoom)
        {
            return Mathf.Max(AbsoluteMinZoom, Mathf.Min(configuredMinimum, requiredFitZoom));
        }

        private static bool IsLeftPanTarget(VisualElement target)
        {
            if (target == null)
            {
                return true;
            }

            if (HasAncestorClass(target, "dpi-graph-hub"))
            {
                return true;
            }

            return !HasAncestorClass(target, "dpi-graph-node") &&
                   !HasAncestorClass(target, "dpi-graph-group") &&
                   !HasAncestorClass(target, "dpi-ecosystem-graph__empty-state") &&
                   !HasAncestorClass(target, "dpi-ecosystem-graph__toolbar") &&
                   !HasAncestorClass(target, "dpi-ecosystem-graph__breadcrumbs") &&
                   !HasAncestorClass(target, "dpi-ecosystem-graph__legend");
        }

        private static bool HasAncestorClass(VisualElement element, string className)
        {
            VisualElement current = element;

            while (current != null)
            {
                if (current.ClassListContains(className))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }

    internal static class PackageGraphActiveLayoutBounds
    {
        public static Rect Calculate(PackageGraphLayoutResult layout)
        {
            if (layout == null)
            {
                return new Rect(0f, 0f, PackageGraphLayout.CanvasWidth, PackageGraphLayout.CanvasHeight);
            }

            Rect bounds = default(Rect);
            bool hasBounds = false;

            if (IsOverviewLikeLayout(layout.Mode))
            {
                AddRect(ref bounds, ref hasBounds, layout.HubRect);

                foreach (PackageGraphRingGuide guide in layout.RingGuides)
                {
                    if (guide == null)
                    {
                        continue;
                    }

                    AddRect(
                        ref bounds,
                        ref hasBounds,
                        guide.CircleRect);
                }
            }

            foreach (Rect nodeRect in layout.NodeRects.Values)
            {
                AddRect(ref bounds, ref hasBounds, nodeRect);
            }

            foreach (PackageGraphGroupLayoutNode groupNode in layout.GroupNodes)
            {
                if (groupNode == null)
                {
                    continue;
                }

                AddRect(ref bounds, ref hasBounds, groupNode.Rect);
                AddRect(ref bounds, ref hasBounds, groupNode.HubRect);

                if (!groupNode.Collapsed && groupNode.OrbitRadius > 0.01f)
                {
                    AddRect(
                        ref bounds,
                        ref hasBounds,
                        new Rect(
                            groupNode.HubCenter.x - groupNode.OrbitRadius,
                            groupNode.HubCenter.y - groupNode.OrbitRadius,
                            groupNode.OrbitRadius * 2f,
                            groupNode.OrbitRadius * 2f));
                }
            }

            foreach (PackageGraphOverflowSummary summary in layout.OverflowSummaries)
            {
                if (summary != null)
                {
                    AddRect(ref bounds, ref hasBounds, summary.Rect);
                }
            }

            if (!hasBounds)
            {
                bounds = new Rect(
                    PackageGraphLayout.GraphCenter.x - 1f,
                    PackageGraphLayout.GraphCenter.y - 1f,
                    2f,
                    2f);
            }

            return Expand(bounds, GetPadding(layout.Mode));
        }

        private static void AddRect(ref Rect bounds, ref bool hasBounds, Rect rect)
        {
            if (rect.width <= 0.01f || rect.height <= 0.01f)
            {
                return;
            }

            bounds = hasBounds ? Union(bounds, rect) : rect;
            hasBounds = true;
        }

        private static float GetPadding(PackageGraphLayoutMode mode)
        {
            return mode == PackageGraphLayoutMode.Focus ? 56f : 64f;
        }

        private static bool IsOverviewLikeLayout(PackageGraphLayoutMode mode)
        {
            return mode == PackageGraphLayoutMode.Overview;
        }

        private static Rect Union(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(
                rect.x - amount,
                rect.y - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }
    }

    internal readonly struct PackageGraphNodeVisualState
    {
        public PackageGraphNodeVisualState(
            Rect rect,
            float opacity,
            float scale,
            PackageGraphNodePresentationLevel presentationLevel,
            bool visible,
            bool entering,
            bool leaving)
        {
            Rect = rect;
            Opacity = Mathf.Clamp01(opacity);
            Scale = Mathf.Max(0.01f, scale);
            PresentationLevel = presentationLevel;
            Visible = visible;
            Entering = entering;
            Leaving = leaving;
        }

        public Rect Rect { get; }

        public float Opacity { get; }

        public float Scale { get; }

        public PackageGraphNodePresentationLevel PresentationLevel { get; }

        public bool Visible { get; }

        public bool Entering { get; }

        public bool Leaving { get; }

        public static PackageGraphNodeVisualState Stable(
            Rect rect,
            PackageGraphNodePresentationLevel presentationLevel)
        {
            return new PackageGraphNodeVisualState(rect, 1f, 1f, presentationLevel, true, false, false);
        }

        public PackageGraphNodeVisualState WithRect(Rect rect)
        {
            return new PackageGraphNodeVisualState(
                rect,
                Opacity,
                Scale,
                PresentationLevel,
                Visible,
                Entering,
                Leaving);
        }
    }

    internal readonly struct CategoryStatusSlice
    {
        public CategoryStatusSlice(
            PackageGraphCategoryStatusKey statusKey,
            int count,
            Color color,
            int sortOrder,
            string tooltipLabel)
        {
            StatusKey = statusKey;
            Count = Math.Max(0, count);
            Color = color;
            SortOrder = sortOrder;
            TooltipLabel = tooltipLabel ?? string.Empty;
        }

        public PackageGraphCategoryStatusKey StatusKey { get; }

        public int Count { get; }

        public Color Color { get; }

        public int SortOrder { get; }

        public string TooltipLabel { get; }
    }

    internal readonly struct CategoryStatusRingSegment
    {
        public CategoryStatusRingSegment(
            PackageGraphCategoryStatusKey statusKey,
            int count,
            Color color,
            float startDegrees,
            float sweepDegrees,
            float separatorAfterDegrees,
            bool fullRing)
        {
            StatusKey = statusKey;
            Count = Math.Max(0, count);
            Color = color;
            StartDegrees = startDegrees;
            SweepDegrees = Mathf.Clamp(sweepDegrees, 0f, 360f);
            SeparatorAfterDegrees = Mathf.Max(0f, separatorAfterDegrees);
            FullRing = fullRing;
        }

        public PackageGraphCategoryStatusKey StatusKey { get; }

        public int Count { get; }

        public Color Color { get; }

        public float StartDegrees { get; }

        public float SweepDegrees { get; }

        public float SeparatorAfterDegrees { get; }

        public bool FullRing { get; }
    }

    internal readonly struct CategoryStatusRingVisualState
    {
        public CategoryStatusRingVisualState(
            string ringId,
            Vector2 center,
            float radius,
            float thickness,
            IReadOnlyList<CategoryStatusSlice> slices,
            bool hoverActive)
        {
            RingId = ringId ?? string.Empty;
            Center = center;
            Radius = Mathf.Max(0f, radius);
            Thickness = Mathf.Max(1f, thickness);
            Slices = slices == null
                ? Array.Empty<CategoryStatusSlice>()
                : slices
                    .Where(slice => slice.Count > 0)
                    .OrderBy(slice => slice.SortOrder)
                    .ToArray();
            HoverActive = hoverActive;
        }

        public string RingId { get; }

        public Vector2 Center { get; }

        public float Radius { get; }

        public float Thickness { get; }

        public IReadOnlyList<CategoryStatusSlice> Slices { get; }

        public bool HoverActive { get; }

        public int TotalCount => Slices.Sum(slice => slice.Count);
    }

    internal static class PackageGraphCategoryStatusVisuals
    {
        internal const float StatusRingSeparatorDegrees = 1.4f;

        private static readonly Color InstalledColor = new Color(0.34f, 0.82f, 0.74f, 0.88f);
        private static readonly Color NotInstalledColor = new Color(0.50f, 0.46f, 0.82f, 0.82f);
        private static readonly Color AttentionColor = new Color(0.92f, 0.68f, 0.28f, 0.92f);
        private static readonly Color UnknownColor = new Color(0.50f, 0.60f, 0.66f, 0.72f);
        private static readonly Color EmptyNeutralColor = new Color(0.34f, 0.44f, 0.52f, 0.36f);

        public static IReadOnlyList<CategoryStatusSlice> CreateSlices(
            PackageGraphCategoryStatusSummary summary)
        {
            CategoryStatusSlice[] slices =
            {
                CreateSlice(PackageGraphCategoryStatusKey.Installed, summary.InstalledCount),
                CreateSlice(PackageGraphCategoryStatusKey.NotInstalled, summary.NotInstalledCount),
                CreateSlice(PackageGraphCategoryStatusKey.Attention, summary.AttentionCount),
                CreateSlice(PackageGraphCategoryStatusKey.Unknown, summary.UnknownCount)
            };
            return slices
                .Where(slice => slice.Count > 0)
                .OrderBy(slice => slice.SortOrder)
                .ToArray();
        }

        public static IReadOnlyList<CategoryStatusRingSegment> CreateRingSegments(
            PackageGraphCategoryStatusSummary summary)
        {
            return CreateRingSegments(CreateSlices(summary), summary.TotalCount, StatusRingSeparatorDegrees);
        }

        public static IReadOnlyList<CategoryStatusRingSegment> CreateRingSegments(
            IReadOnlyList<CategoryStatusSlice> slices,
            int totalCount,
            float separatorDegrees = StatusRingSeparatorDegrees)
        {
            CategoryStatusSlice[] nonZeroSlices = (slices ?? Array.Empty<CategoryStatusSlice>())
                .Where(slice => slice.Count > 0)
                .OrderBy(slice => slice.SortOrder)
                .ToArray();

            if (nonZeroSlices.Length == 0 || totalCount <= 0)
            {
                return new[]
                {
                    new CategoryStatusRingSegment(
                        PackageGraphCategoryStatusKey.Unknown,
                        0,
                        EmptyNeutralColor,
                        -90f,
                        360f,
                        0f,
                        true)
                };
            }

            if (nonZeroSlices.Length == 1)
            {
                CategoryStatusSlice slice = nonZeroSlices[0];
                return new[]
                {
                    new CategoryStatusRingSegment(
                        slice.StatusKey,
                        slice.Count,
                        slice.Color,
                        -90f,
                        360f,
                        0f,
                        true)
                };
            }

            int safeTotalCount = Math.Max(1, nonZeroSlices.Sum(slice => slice.Count));
            float safeGap = Mathf.Clamp(separatorDegrees, 0f, 24f);
            float totalGap = safeGap * nonZeroSlices.Length;
            float usableAngle = Mathf.Max(0f, 360f - totalGap);
            float cursor = -90f;
            float remainingUsableAngle = usableAngle;
            List<CategoryStatusRingSegment> segments = new List<CategoryStatusRingSegment>(nonZeroSlices.Length);

            for (int index = 0; index < nonZeroSlices.Length; index++)
            {
                CategoryStatusSlice slice = nonZeroSlices[index];
                bool last = index == nonZeroSlices.Length - 1;
                float sweep = last
                    ? remainingUsableAngle
                    : usableAngle * (slice.Count / (float)safeTotalCount);
                sweep = Mathf.Max(0f, sweep);
                remainingUsableAngle = Mathf.Max(0f, remainingUsableAngle - sweep);

                segments.Add(new CategoryStatusRingSegment(
                    slice.StatusKey,
                    slice.Count,
                    slice.Color,
                    cursor,
                    sweep,
                    safeGap,
                    false));
                cursor += sweep + safeGap;
            }

            return segments;
        }

        public static CategoryStatusSlice CreateSlice(
            PackageGraphCategoryStatusKey statusKey,
            int count)
        {
            switch (statusKey)
            {
                case PackageGraphCategoryStatusKey.Installed:
                    return new CategoryStatusSlice(statusKey, count, InstalledColor, 10, "installed");
                case PackageGraphCategoryStatusKey.NotInstalled:
                    return new CategoryStatusSlice(statusKey, count, NotInstalledColor, 20, "not installed");
                case PackageGraphCategoryStatusKey.Attention:
                    return new CategoryStatusSlice(statusKey, count, AttentionColor, 30, "attention");
                default:
                    return new CategoryStatusSlice(statusKey, count, UnknownColor, 40, "unknown");
            }
        }

        public static string FormatTotal(int totalCount)
        {
            return totalCount == 1 ? "1 package" : totalCount + " packages";
        }

        public static string FormatSummary(PackageGraphCategoryStatusSummary summary)
        {
            return "\u2713 " + summary.InstalledCount + " installed   " +
                   "\u25CB " + summary.NotInstalledCount + " not installed   " +
                   "! " + summary.AttentionCount + " attention";
        }
    }

    internal sealed class PackageGraphCanvas : VisualElement
    {
        private const float LayoutTransitionSeconds = 0.24f;
        private const float LayoutAnimationFrameMs = 16f;

        private readonly Action<PackageDefinition> _packageSelected;
        private readonly Action<PackageDefinition, PackageGraphNodeAction> _packageAction;
        private readonly Action _selectionCleared;
        private readonly Action _rootFocused;
        private readonly Action<PackageGraphGroup> _groupFocused;
        private readonly PackageGraphLayout _layout = new PackageGraphLayout();
        private readonly Dictionary<string, Rect> _animatedNodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageGraphNodeVisualState> _nodeVisualStates =
            new Dictionary<string, PackageGraphNodeVisualState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _animatedGroupRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector2> _animatedGroupCenters =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _animatedGroupOrbitRadii =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _transitionStartRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageGraphNodeVisualState> _transitionStartNodeVisualStates =
            new Dictionary<string, PackageGraphNodeVisualState>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _transitionEnteringNodeIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _transitionStartGroupRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector2> _transitionStartGroupCenters =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _transitionStartGroupOrbitRadii =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageGraphNodeElement> _nodeElements =
            new Dictionary<string, PackageGraphNodeElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageGraphGroupElement> _groupElements =
            new Dictionary<string, PackageGraphGroupElement>(StringComparer.OrdinalIgnoreCase);
        private readonly VisualElement _guideLayer;
        private readonly PackageGraphMembershipLayer _membershipLayer;
        private readonly PackageGraphEdgeLayer _edgeLayer;
        private readonly VisualElement _nodeLayer;

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphModel _visibleGraph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private HashSet<string> _visiblePackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private PackageGraphLayoutResult _baselineLayoutResult;
        private PackageGraphLayoutResult _layoutResult;
        private string _selectedPackageId = string.Empty;
        private string _focusedPackageId = string.Empty;
        private string _focusedGroupId = string.Empty;
        private string _hoveredPackageId = string.Empty;
        private string _hoveredGroupId = string.Empty;
        private string _layoutFocusPackageId = string.Empty;
        private string _layoutFocusGroupId = string.Empty;
        private PackageGraphSearchState _searchState = PackageGraphSearchState.Empty;
        private PackageGraphFocus _currentFocus = PackageGraphFocus.Create(null, string.Empty);
        private PackageGraphFocus _actionFocus = PackageGraphFocus.Create(null, string.Empty);
        private PackageGraphNodePresentationLevel _layoutPresentationLevel =
            PackageGraphNodePresentationLevel.Micro;
        private Vector2 _viewportSize;
        private float _viewportZoom = 1f;
        private IVisualElementScheduledItem _layoutAnimationItem;
        private double _layoutAnimationStartedAt;
        private bool _layoutAnimationActive;
        private bool _interactionsLocked;
        private bool _actionsEnabled;

        public PackageGraphCanvas(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared)
            : this(packageSelected, packageAction, selectionCleared, null, null)
        {
        }

        public PackageGraphCanvas(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            Action rootFocused,
            Action<PackageGraphGroup> groupFocused)
        {
            _packageSelected = packageSelected;
            _packageAction = packageAction;
            _selectionCleared = selectionCleared;
            _rootFocused = rootFocused;
            _groupFocused = groupFocused;
            name = "ecosystem-graph-canvas";
            AddToClassList("dpi-ecosystem-graph__canvas");
            style.width = PackageGraphLayout.CanvasWidth;
            style.height = PackageGraphLayout.CanvasHeight;

            _guideLayer = new VisualElement { name = "ecosystem-graph-guide-layer" };
            _guideLayer.AddToClassList("dpi-ecosystem-graph__guide-layer");
            _guideLayer.pickingMode = PickingMode.Ignore;
            StretchToCanvas(_guideLayer);
            Add(_guideLayer);

            _membershipLayer = new PackageGraphMembershipLayer { name = "ecosystem-graph-membership-layer" };
            _membershipLayer.AddToClassList("dpi-ecosystem-graph__membership-layer");
            _membershipLayer.pickingMode = PickingMode.Ignore;
            StretchToCanvas(_membershipLayer);
            Add(_membershipLayer);

            _edgeLayer = new PackageGraphEdgeLayer { name = "ecosystem-graph-edge-layer" };
            _edgeLayer.AddToClassList("dpi-ecosystem-graph__edge-layer");
            _edgeLayer.pickingMode = PickingMode.Ignore;
            StretchToCanvas(_edgeLayer);
            Add(_edgeLayer);

            _nodeLayer = new VisualElement { name = "ecosystem-graph-node-layer" };
            _nodeLayer.AddToClassList("dpi-ecosystem-graph__node-layer");
            StretchToCanvas(_nodeLayer);
            Add(_nodeLayer);
        }

        public Vector2 ContentSize
        {
            get
            {
                return _layoutResult != null
                    ? new Vector2(_layoutResult.CanvasWidth, _layoutResult.CanvasHeight)
                    : new Vector2(PackageGraphLayout.CanvasWidth, PackageGraphLayout.CanvasHeight);
            }
        }

        public Rect GetContentBounds()
        {
            PackageGraphLayoutResult boundsLayout = _layoutResult != null &&
                                                    _layoutResult.Mode == PackageGraphLayoutMode.Focus
                ? _layoutResult
                : _baselineLayoutResult ?? _layoutResult;
            Rect bounds = PackageGraphActiveLayoutBounds.Calculate(boundsLayout);
            bool hasBounds = true;

            foreach (PackageGraphEdgeRoute route in _edgeLayer.BuildRoutesSnapshotForTests())
            {
                AddRouteBounds(ref bounds, ref hasBounds, route.Points);
            }

            foreach (PackageGraphStructuralMembershipRoute route in _membershipLayer.FocusMembershipRoutesForTests())
            {
                AddRouteBounds(ref bounds, ref hasBounds, route.Segments);
            }

            return bounds;
        }

        public Vector2 GetActiveCenter()
        {
            return _layoutResult != null
                ? _layoutResult.ActiveCenter
                : PackageGraphLayout.GraphCenter;
        }

        public string LayoutFocusPackageId => _layoutFocusPackageId;

        public string LayoutFocusGroupId => _layoutFocusGroupId;

        public PackageGraphLayoutMode LayoutMode => _layoutResult != null ? _layoutResult.Mode : PackageGraphLayoutMode.Overview;

        public int RenderedPackageCount => _visibleGraph != null ? _visibleGraph.Nodes.Count : 0;

        public int SummarizedDirectRelationshipCount => _layoutResult != null
            ? _layoutResult.OverflowSummaries.Sum(summary => summary.HiddenCount)
            : 0;

        public bool InteractionsLocked => _interactionsLocked;

        internal bool LayoutTransitionActiveForTests => _layoutAnimationActive;

        public string ActiveHoverGroupId => GetActiveHoverGroupId();

        public string DirectHoverGroupId => _hoveredGroupId;

        public bool HasRenderedNodeStatus(Func<PackageGraphNodeStatus, bool> predicate)
        {
            if (predicate == null || _visibleGraph == null)
            {
                return false;
            }

            if (_layoutResult == null || _layoutResult.Mode != PackageGraphLayoutMode.Focus)
            {
                return _visibleGraph.Nodes.Any(node => node != null && predicate(node.Status));
            }

            return _layoutResult.NodeRects.Keys.Any(packageId =>
                _visibleGraph.TryGetNode(packageId, out PackageGraphNode node) &&
                node != null &&
                predicate(node.Status));
        }

        public bool HasRenderedAttentionNode()
        {
            return _layoutResult != null &&
                   _visibleGraph != null &&
                   _layoutResult.NodeRects.Keys.Any(packageId =>
                       _visibleGraph.TryGetNode(packageId, out PackageGraphNode node) &&
                       node != null &&
                       (node.Status == PackageGraphNodeStatus.Missing ||
                        node.Status == PackageGraphNodeStatus.UpdateAvailable ||
                        node.Status == PackageGraphNodeStatus.Warning));
        }

        public event Action<bool> InteractionsLockedChanged;

        public event Action<string> ActiveHoverGroupChanged;

        public event Action<Vector2> ActiveVisualCenterChanged;

        internal IReadOnlyDictionary<string, Rect> NodeRectsForTests => _layoutResult != null
            ? _layoutResult.NodeRects
            : new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        internal IReadOnlyList<PackageGraphGroupLayoutNode> GroupLayoutNodesForTests => _layoutResult != null
            ? _layoutResult.GroupNodes
            : Array.Empty<PackageGraphGroupLayoutNode>();

        internal IReadOnlyDictionary<string, float> AnimatedGroupOrbitRadiiForTests => _animatedGroupOrbitRadii;

        internal IReadOnlyDictionary<string, Vector2> AnimatedGroupCentersForTests => _animatedGroupCenters;

        internal IReadOnlyDictionary<string, PackageGraphNodeVisualState> NodeVisualStatesForTests => _nodeVisualStates;

        internal IReadOnlyList<PackageGraphOrbitVisualState> OrbitVisualStatesForTests =>
            _membershipLayer.BuildOrbitVisualStatesForTests();

        internal IReadOnlyList<CategoryStatusRingVisualState> StatusRingVisualStatesForTests =>
            _membershipLayer.BuildStatusRingVisualStatesForTests();

        internal IReadOnlyList<PackageGraphStructuralMembershipRoute> StructuralMembershipRoutesForTests =>
            _membershipLayer.FocusMembershipRoutesForTests();

        internal IReadOnlyList<PackageGraphEdgeRoute> EdgeRoutesForTests =>
            _edgeLayer.BuildRoutesSnapshotForTests();

        private static void AddRouteBounds(
            ref Rect bounds,
            ref bool hasBounds,
            IReadOnlyList<Vector2> points)
        {
            if (points == null)
            {
                return;
            }

            foreach (Vector2 point in points)
            {
                AddPointBounds(ref bounds, ref hasBounds, point);
            }
        }

        private static void AddRouteBounds(
            ref Rect bounds,
            ref bool hasBounds,
            IReadOnlyList<PackageGraphStructuralMembershipSegment> segments)
        {
            if (segments == null)
            {
                return;
            }

            foreach (PackageGraphStructuralMembershipSegment segment in segments)
            {
                AddPointBounds(ref bounds, ref hasBounds, segment.From);
                AddPointBounds(ref bounds, ref hasBounds, segment.To);
            }
        }

        private static void AddPointBounds(ref Rect bounds, ref bool hasBounds, Vector2 point)
        {
            Rect pointRect = new Rect(point.x - 1f, point.y - 1f, 2f, 2f);
            bounds = hasBounds ? Union(bounds, pointRect) : pointRect;
            hasBounds = true;
        }

        private static Rect Union(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        internal int CountNodeElementsForTests(string packageId)
        {
            return _nodeLayer.Children().Count(child =>
                child is PackageGraphNodeElement &&
                string.Equals(child.name, packageId, StringComparison.OrdinalIgnoreCase));
        }

        public bool TryGetTransitionAnchorCenter(
            PackageGraphTransitionAnchor anchor,
            out Vector2 center)
        {
            switch (anchor.Kind)
            {
                case PackageGraphTransitionAnchorKind.Package:
                    if (_animatedNodeRects.TryGetValue(anchor.Id, out Rect animatedNodeRect))
                    {
                        center = animatedNodeRect.center;
                        return true;
                    }

                    if (_layoutResult != null &&
                        _layoutResult.NodeRects.TryGetValue(anchor.Id, out Rect nodeRect))
                    {
                        center = nodeRect.center;
                        return true;
                    }

                    break;
                case PackageGraphTransitionAnchorKind.Group:
                    if (_animatedGroupCenters.TryGetValue(anchor.Id, out Vector2 animatedGroupCenter))
                    {
                        center = animatedGroupCenter;
                        return true;
                    }

                    if (_animatedGroupRects.TryGetValue(anchor.Id, out Rect animatedGroupRect))
                    {
                        PackageGraphGroupLayoutNode animatedGroupNode = FindGroupLayoutNode(_layoutResult, anchor.Id);
                        center = animatedGroupNode != null
                            ? GetGroupHubRect(animatedGroupNode, animatedGroupRect).center
                            : animatedGroupRect.center;
                        return true;
                    }

                    if (_layoutResult != null)
                    {
                        PackageGraphGroupLayoutNode groupNode = FindGroupLayoutNode(_layoutResult, anchor.Id);

                        if (groupNode != null)
                        {
                            center = groupNode.HubCenter;
                            return true;
                        }
                    }

                    break;
                default:
                    center = _layoutResult != null
                        ? _layoutResult.HubRect.center
                        : PackageGraphLayout.GraphCenter;
                    return true;
            }

            center = default(Vector2);
            return false;
        }

        public bool TryGetActiveVisualCenter(out Vector2 center)
        {
            if (!string.IsNullOrWhiteSpace(_focusedPackageId) &&
                TryGetTransitionAnchorCenter(
                    new PackageGraphTransitionAnchor(
                        PackageGraphTransitionAnchorKind.Package,
                        _focusedPackageId),
                    out center))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(_focusedGroupId) &&
                TryGetTransitionAnchorCenter(
                    new PackageGraphTransitionAnchor(
                        PackageGraphTransitionAnchorKind.Group,
                        _focusedGroupId),
                    out center))
            {
                return true;
            }

            center = GetActiveCenter();
            return true;
        }

        public void SetExternalHoverGroup(string groupId)
        {
            SetExternalHoverGroup(groupId, respectInteractionLock: true);
        }

        public void SetExternalHoverGroup(string groupId, bool respectInteractionLock)
        {
            if (respectInteractionLock && _interactionsLocked)
            {
                return;
            }

            string nextGroupId = groupId ?? string.Empty;

            if (string.Equals(_hoveredGroupId, nextGroupId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredGroupId = nextGroupId;
            NotifyActiveHoverGroupChanged();
            RefreshHoverVisualState();
        }

        internal void SetPreviewPackageForTests(string packageId)
        {
            _hoveredPackageId = packageId ?? string.Empty;
            NotifyActiveHoverGroupChanged();
            RefreshHoverVisualState();
        }

        internal void ClearPreviewPackageForTests(string packageId)
        {
            if (!string.Equals(_hoveredPackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredPackageId = string.Empty;
            NotifyActiveHoverGroupChanged();
            RefreshHoverVisualState();
        }

        public void ClearExternalHoverGroup(string groupId)
        {
            if (_interactionsLocked ||
                !string.Equals(_hoveredGroupId, groupId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredGroupId = string.Empty;
            NotifyActiveHoverGroupChanged();
            RefreshHoverVisualState();
        }

        public void ClearHoverState()
        {
            if (string.IsNullOrWhiteSpace(_hoveredGroupId) &&
                string.IsNullOrWhiteSpace(_hoveredPackageId))
            {
                return;
            }

            _hoveredGroupId = string.Empty;
            _hoveredPackageId = string.Empty;
            NotifyActiveHoverGroupChanged();
            RefreshHoverVisualState();
        }

        public bool SetViewportSize(Vector2 viewportSize)
        {
            if (viewportSize.x <= 1f || viewportSize.y <= 1f)
            {
                return false;
            }

            if (Mathf.Abs(viewportSize.x - _viewportSize.x) < 1f &&
                Mathf.Abs(viewportSize.y - _viewportSize.y) < 1f)
            {
                return false;
            }

            _viewportSize = viewportSize;

            if (_layoutResult != null)
            {
                Rebuild();
            }

            return true;
        }

        public void SetViewportZoom(float zoom)
        {
            _viewportZoom = Mathf.Max(0.01f, zoom);

            if (_layoutResult == null)
            {
                return;
            }

            PackageGraphNodePresentationLevel nextPresentation =
                PackageGraphPresentationPolicy.ResolveForZoom(
                    _layoutResult.Mode,
                    _viewportZoom,
                    _layoutPresentationLevel);

            if (nextPresentation == _layoutPresentationLevel)
            {
                return;
            }

            _layoutPresentationLevel = nextPresentation;
            Rebuild();
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled)
        {
            SetGraph(graph, selectedPackageId, focusedPackageId, string.Empty, actionsEnabled, null);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds)
        {
            SetGraph(graph, selectedPackageId, focusedPackageId, string.Empty, actionsEnabled, visiblePackageIds);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            string focusedGroupId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds)
        {
            SetGraph(graph, selectedPackageId, focusedPackageId, focusedGroupId, actionsEnabled, visiblePackageIds, null);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            string focusedGroupId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds,
            PackageGraphSearchState searchState)
        {
            PackageGraphModel nextGraph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            PackageGraphSearchState nextSearchState = searchState ?? PackageGraphSearchState.Empty;
            string nextSelectedPackageId = selectedPackageId ?? string.Empty;
            string nextFocusedPackageId = focusedPackageId ?? string.Empty;
            string nextFocusedGroupId = focusedGroupId ?? string.Empty;

            if (CanSkipRebuild(
                    nextGraph,
                    nextSelectedPackageId,
                    nextFocusedPackageId,
                    nextFocusedGroupId,
                    actionsEnabled,
                    visiblePackageIds,
                    nextSearchState))
            {
                _actionsEnabled = actionsEnabled;
                ApplyInteractionState();
                return;
            }

            _graph = nextGraph;
            _visiblePackageIds = visiblePackageIds == null
                ? new HashSet<string>(_graph.Nodes.Select(node => node.PackageId), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(visiblePackageIds, StringComparer.OrdinalIgnoreCase);
            _selectedPackageId = nextSelectedPackageId;
            _focusedPackageId = nextFocusedPackageId;
            _focusedGroupId = nextFocusedGroupId;
            _actionsEnabled = actionsEnabled;
            _searchState = nextSearchState;
            Rebuild();
        }

        private bool CanSkipRebuild(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            string focusedGroupId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds,
            PackageGraphSearchState searchState)
        {
            return ReferenceEquals(_graph, graph) &&
                   string.Equals(_selectedPackageId, selectedPackageId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(_focusedPackageId, focusedPackageId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(_focusedGroupId, focusedGroupId, StringComparison.OrdinalIgnoreCase) &&
                   _actionsEnabled == actionsEnabled &&
                   SearchStatesMatch(_searchState, searchState) &&
                   VisiblePackageIdsMatch(graph, visiblePackageIds);
        }

        private bool VisiblePackageIdsMatch(
            PackageGraphModel graph,
            IReadOnlyCollection<string> visiblePackageIds)
        {
            if (_visiblePackageIds == null)
            {
                return false;
            }

            if (visiblePackageIds == null)
            {
                if (graph == null || _visiblePackageIds.Count != graph.Nodes.Count)
                {
                    return false;
                }

                foreach (PackageGraphNode node in graph.Nodes)
                {
                    if (node == null || !_visiblePackageIds.Contains(node.PackageId))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (_visiblePackageIds.Count != visiblePackageIds.Count)
            {
                return false;
            }

            foreach (string packageId in visiblePackageIds)
            {
                if (string.IsNullOrWhiteSpace(packageId) || !_visiblePackageIds.Contains(packageId))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SearchStatesMatch(
            PackageGraphSearchState current,
            PackageGraphSearchState next)
        {
            if (ReferenceEquals(current, next))
            {
                return true;
            }

            if (current == null || next == null)
            {
                return false;
            }

            return string.Equals(current.Query, next.Query, StringComparison.OrdinalIgnoreCase) &&
                   current.DirectCategoryMatchCount == next.DirectCategoryMatchCount &&
                   current.DirectPackageMatchCount == next.DirectPackageMatchCount &&
                   current.ContextPackageCount == next.ContextPackageCount;
        }

        private void Rebuild()
        {
            Dictionary<string, Rect> previousRects = CaptureCurrentNodeRects();
            Dictionary<string, PackageGraphNodeVisualState> previousNodeVisualStates = CaptureCurrentNodeVisualStates();
            Dictionary<string, Rect> previousGroupRects = CaptureCurrentGroupRects();
            Dictionary<string, Vector2> previousGroupCenters = CaptureCurrentGroupCenters();
            Dictionary<string, float> previousGroupOrbitRadii = CaptureCurrentGroupOrbitRadii();
            _guideLayer.Clear();
            _nodeLayer.Clear();
            _nodeElements.Clear();
            _groupElements.Clear();
            _visibleGraph = CreateRenderedGraph();

            _layoutFocusPackageId = GetLayoutFocusPackageId();
            _layoutFocusGroupId = string.IsNullOrWhiteSpace(_layoutFocusPackageId)
                ? GetLayoutFocusGroupId()
                : string.Empty;
            PackageGraphLayoutMode layoutMode = !string.IsNullOrWhiteSpace(_layoutFocusPackageId)
                ? PackageGraphLayoutMode.Focus
                : (!string.IsNullOrWhiteSpace(_layoutFocusGroupId)
                    ? PackageGraphLayoutMode.GroupFocus
                    : PackageGraphLayoutMode.Overview);
            _layoutPresentationLevel = PackageGraphPresentationPolicy.ResolveForZoom(
                layoutMode,
                _viewportZoom,
                _layoutPresentationLevel);
            PackageGraphLayoutResult fullLayoutResult;
            PackageGraphFocus edgeFocus;

            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.Layout))
            {
                fullLayoutResult = _layout.Calculate(
                    _graph,
                    layoutMode,
                    _layoutFocusPackageId,
                    _layoutFocusGroupId,
                    _viewportSize,
                    _layoutPresentationLevel);
                _currentFocus = PackageGraphFocus.Create(
                    _visibleGraph,
                    _layoutFocusPackageId);
                _actionFocus = PackageGraphFocus.Create(
                    _visibleGraph,
                    _layoutFocusPackageId);
                edgeFocus = PackageGraphFocus.Create(
                    _visibleGraph,
                    _layoutFocusPackageId);
                _baselineLayoutResult = fullLayoutResult;
                _layoutResult = CreateProjectedLayoutResult(fullLayoutResult, _currentFocus);
            }

            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.LayoutRepaintScheduling))
            {
                style.width = _layoutResult.CanvasWidth;
                style.height = _layoutResult.CanvasHeight;
                _guideLayer.style.width = _layoutResult.CanvasWidth;
                _guideLayer.style.height = _layoutResult.CanvasHeight;
                _membershipLayer.style.width = _layoutResult.CanvasWidth;
                _membershipLayer.style.height = _layoutResult.CanvasHeight;
                _edgeLayer.style.width = _layoutResult.CanvasWidth;
                _edgeLayer.style.height = _layoutResult.CanvasHeight;
                _nodeLayer.style.width = _layoutResult.CanvasWidth;
                _nodeLayer.style.height = _layoutResult.CanvasHeight;

                DrawGuides();
                _membershipLayer.SetLayout(
                    _visibleGraph,
                    _layoutResult,
                    PackageGraphCategoryStatusSummary.Create(_visibleGraph.Nodes),
                    _animatedNodeRects,
                    _animatedGroupRects,
                    _animatedGroupCenters,
                    _animatedGroupOrbitRadii,
                    GetActiveHoverGroupId(),
                    _interactionsLocked);
                StartLayoutTransition(
                    previousRects,
                    previousNodeVisualStates,
                    previousGroupRects,
                    previousGroupCenters,
                    previousGroupOrbitRadii);
                DrawGroups();
            }

            DrawNodes(_currentFocus);

            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.LayoutRepaintScheduling))
            {
                DrawUnrelatedSummary();
                DrawOverflowSummaries();
                ApplyAnimatedLayout(updateEdgeLayer: false);
                _edgeLayer.SetGraph(
                    _visibleGraph,
                    _layoutResult.NodeRects,
                    CreateTargetGroupRectSnapshot(),
                    _layoutResult.CanvasHeight,
                    edgeFocus);
                _edgeLayer.SetPreviewPackage(_hoveredPackageId);
                ApplyInteractionState();
            }
        }

        private PackageGraphModel CreateRenderedGraph()
        {
            if (HasPackageEgoFocus())
            {
                return _graph;
            }

            return PackageVisibilityFilter.CreateVisibleGraph(_graph, _visiblePackageIds);
        }

        private bool HasPackageEgoFocus()
        {
            return !string.IsNullOrWhiteSpace(_focusedPackageId) &&
                   _graph.TryGetNode(_focusedPackageId, out _);
        }

        private string GetLayoutFocusPackageId()
        {
            return !string.IsNullOrWhiteSpace(_focusedPackageId) &&
                   _graph.TryGetNode(_focusedPackageId, out _)
                ? _focusedPackageId
                : string.Empty;
        }

        private string GetLayoutFocusGroupId()
        {
            return !string.IsNullOrWhiteSpace(_focusedGroupId) &&
                   _graph.TryGetGroup(_focusedGroupId, out _)
                ? _focusedGroupId
                : string.Empty;
        }

        private PackageGraphLayoutResult CreateProjectedLayoutResult(
            PackageGraphLayoutResult fullLayoutResult,
            PackageGraphFocus fullFocus)
        {
            if (fullLayoutResult == null)
            {
                return null;
            }

            bool packageFocus = fullLayoutResult.Mode == PackageGraphLayoutMode.Focus;
            Dictionary<string, Rect> projectedNodeRects = fullLayoutResult.NodeRects
                .Where(pair => packageFocus || _visiblePackageIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphLayoutRing> projectedNodeRings = fullLayoutResult.NodeRings
                .Where(pair => packageFocus || _visiblePackageIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphNodePresentationLevel> projectedNodePresentations =
                fullLayoutResult.NodePresentationLevels
                    .Where(pair => packageFocus || _visiblePackageIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            int visibleUnrelatedCount = packageFocus && fullFocus != null
                ? _visibleGraph.Nodes.Count(node => !fullFocus.IsPackageRelated(node.PackageId))
                : 0;
            Rect unrelatedSummaryRect = visibleUnrelatedCount > 0
                ? fullLayoutResult.UnrelatedSummaryRect
                : default(Rect);
            PackageGraphGroupLayoutNode[] projectedGroupNodes = fullLayoutResult.GroupNodes
                .Where(groupNode => groupNode != null)
                .Select(ProjectGroupLayoutNode)
                .ToArray();

            return new PackageGraphLayoutResult(
                fullLayoutResult.Mode,
                fullLayoutResult.FocusPackageId,
                fullLayoutResult.CanvasWidth,
                fullLayoutResult.CanvasHeight,
                fullLayoutResult.HubRect,
                fullLayoutResult.ActiveCenter,
                projectedNodeRects,
                projectedNodeRings,
                fullLayoutResult.RingGuides,
                fullLayoutResult.SectorLabels,
                visibleUnrelatedCount,
                unrelatedSummaryRect,
                projectedGroupNodes,
                fullLayoutResult.FocusGroupId,
                projectedNodePresentations,
                fullLayoutResult.OverflowSummaries);
        }

        private PackageGraphGroupLayoutNode ProjectGroupLayoutNode(PackageGraphGroupLayoutNode groupNode)
        {
            List<PackageGraphNode> shownPackageList = new List<PackageGraphNode>();

            foreach (string packageId in groupNode.RepresentedPackageIds)
            {
                if (!string.IsNullOrWhiteSpace(packageId) &&
                    _visibleGraph.TryGetNode(packageId, out PackageGraphNode node))
                {
                    shownPackageList.Add(node);
                }
            }

            PackageGraphNode[] shownPackages = shownPackageList.ToArray();
            PackageGraphCategoryStatusSummary statusSummary =
                PackageGraphCategoryStatusSummary.Create(shownPackages);

            return new PackageGraphGroupLayoutNode(
                groupNode.Group,
                groupNode.Rect,
                groupNode.HubRect,
                groupNode.Ring,
                shownPackages.Length,
                statusSummary.InstalledCount,
                statusSummary.NotInstalledCount,
                statusSummary.AttentionCount,
                statusSummary.UnknownCount,
                shownPackages.Count(node => node.Status == PackageGraphNodeStatus.UpdateAvailable),
                groupNode.Focused,
                groupNode.Collapsed,
                groupNode.OrbitRadius,
                groupNode.SummaryLabel,
                groupNode.RepresentedPackageIds);
        }

        private Dictionary<string, Rect> CaptureCurrentNodeRects()
        {
            if (_animatedNodeRects.Count > 0)
            {
                return new Dictionary<string, Rect>(_animatedNodeRects, StringComparer.OrdinalIgnoreCase);
            }

            return _layoutResult != null
                ? new Dictionary<string, Rect>(_layoutResult.NodeRects, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, PackageGraphNodeVisualState> CaptureCurrentNodeVisualStates()
        {
            if (_nodeVisualStates.Count > 0)
            {
                return new Dictionary<string, PackageGraphNodeVisualState>(
                    _nodeVisualStates,
                    StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, PackageGraphNodeVisualState> states =
                new Dictionary<string, PackageGraphNodeVisualState>(StringComparer.OrdinalIgnoreCase);

            if (_layoutResult == null)
            {
                return states;
            }

            foreach (KeyValuePair<string, Rect> nodeRect in _layoutResult.NodeRects)
            {
                states[nodeRect.Key] = PackageGraphNodeVisualState.Stable(
                    nodeRect.Value,
                    GetPresentationLevel(nodeRect.Key));
            }

            return states;
        }

        private Dictionary<string, Rect> CaptureCurrentGroupRects()
        {
            if (_animatedGroupRects.Count > 0)
            {
                return new Dictionary<string, Rect>(_animatedGroupRects, StringComparer.OrdinalIgnoreCase);
            }

            return _layoutResult != null
                ? _layoutResult.GroupNodes
                    .Where(groupNode => groupNode != null)
                    .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().Rect, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, Vector2> CaptureCurrentGroupCenters()
        {
            if (_animatedGroupCenters.Count > 0)
            {
                return new Dictionary<string, Vector2>(_animatedGroupCenters, StringComparer.OrdinalIgnoreCase);
            }

            return _layoutResult != null
                ? _layoutResult.GroupNodes
                    .Where(groupNode => groupNode != null)
                    .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().HubCenter,
                        StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, float> CaptureCurrentGroupOrbitRadii()
        {
            if (_animatedGroupOrbitRadii.Count > 0)
            {
                return new Dictionary<string, float>(_animatedGroupOrbitRadii, StringComparer.OrdinalIgnoreCase);
            }

            return _layoutResult != null
                ? _layoutResult.GroupNodes
                    .Where(groupNode => groupNode != null)
                    .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().OrbitRadius,
                        StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        private void StartLayoutTransition(
            IReadOnlyDictionary<string, Rect> previousRects,
            IReadOnlyDictionary<string, PackageGraphNodeVisualState> previousNodeVisualStates,
            IReadOnlyDictionary<string, Rect> previousGroupRects,
            IReadOnlyDictionary<string, Vector2> previousGroupCenters,
            IReadOnlyDictionary<string, float> previousGroupOrbitRadii)
        {
            _animatedNodeRects.Clear();
            _nodeVisualStates.Clear();
            _animatedGroupRects.Clear();
            _animatedGroupCenters.Clear();
            _animatedGroupOrbitRadii.Clear();
            _transitionStartRects.Clear();
            _transitionStartNodeVisualStates.Clear();
            _transitionEnteringNodeIds.Clear();
            _transitionStartGroupRects.Clear();
            _transitionStartGroupCenters.Clear();
            _transitionStartGroupOrbitRadii.Clear();
            bool shouldAnimate = false;
            bool hasPreviousNodeFrame = previousRects != null && previousRects.Count > 0;
            bool hasPreviousGroupFrame = previousGroupRects != null && previousGroupRects.Count > 0;

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                Rect previous = default(Rect);
                bool hasPreviousNodeRect = hasPreviousNodeFrame &&
                                           previousRects.TryGetValue(target.Key, out previous);
                bool entering = !hasPreviousNodeRect && (hasPreviousNodeFrame || hasPreviousGroupFrame);
                Rect start = !entering
                    ? hasPreviousNodeRect ? previous : target.Value
                    : CreateEnteringNodeStartRect(target.Key, target.Value, previousGroupRects);
                PackageGraphNodePresentationLevel presentationLevel = GetPresentationLevel(target.Key);
                PackageGraphNodeVisualState startState =
                    !entering &&
                    previousNodeVisualStates != null &&
                    previousNodeVisualStates.TryGetValue(target.Key, out PackageGraphNodeVisualState previousVisualState)
                        ? previousVisualState.WithRect(start)
                        : new PackageGraphNodeVisualState(
                            start,
                            entering ? 0f : 1f,
                            entering ? 0.24f : 1f,
                            presentationLevel,
                            true,
                            entering,
                            false);
                _transitionStartRects[target.Key] = start;
                _animatedNodeRects[target.Key] = start;
                _transitionStartNodeVisualStates[target.Key] = startState;
                _nodeVisualStates[target.Key] = startState;

                if (entering)
                {
                    _transitionEnteringNodeIds.Add(target.Key);
                }

                if (!AreRectsClose(start, target.Value) || entering)
                {
                    shouldAnimate = true;
                }
            }

            foreach (PackageGraphGroupLayoutNode target in _layoutResult.GroupNodes)
            {
                if (target == null)
                {
                    continue;
                }

                Rect previous = default(Rect);
                bool hasPreviousGroupRect = hasPreviousGroupFrame &&
                                            previousGroupRects != null &&
                                            previousGroupRects.TryGetValue(target.GroupId, out previous);
                Rect start = hasPreviousGroupRect
                    ? previous
                    : hasPreviousGroupFrame
                        ? CreateEnteringGroupStartRect(target, previousGroupRects)
                        : target.Rect;
                Vector2 startCenter = previousGroupCenters != null &&
                                      previousGroupCenters.TryGetValue(target.GroupId, out Vector2 previousCenter)
                    ? previousCenter
                    : GetGroupHubRect(target, start).center;
                float previousRadius = 0f;
                bool hasPreviousRadius = hasPreviousGroupFrame &&
                                         previousGroupOrbitRadii != null &&
                                         previousGroupOrbitRadii.TryGetValue(target.GroupId, out previousRadius);
                bool enteringGroup = hasPreviousGroupFrame && !hasPreviousRadius;
                float startRadius = !hasPreviousGroupFrame
                    ? target.OrbitRadius
                    : enteringGroup
                        ? target.OrbitRadius * 0.24f
                        : previousRadius;
                _transitionStartGroupRects[target.GroupId] = start;
                _animatedGroupRects[target.GroupId] = start;
                _transitionStartGroupCenters[target.GroupId] = startCenter;
                _animatedGroupCenters[target.GroupId] = startCenter;
                _transitionStartGroupOrbitRadii[target.GroupId] = startRadius;
                _animatedGroupOrbitRadii[target.GroupId] = startRadius;

                if (!AreRectsClose(start, target.Rect) ||
                    Vector2.Distance(startCenter, target.HubCenter) > 0.25f ||
                    Mathf.Abs(startRadius - target.OrbitRadius) > 0.25f)
                {
                    shouldAnimate = true;
                }
            }

            if (!shouldAnimate)
            {
                _layoutAnimationActive = false;
                SetInteractionsLocked(false);
                _layoutAnimationItem?.Pause();
                CopyTargetRectsToAnimatedRects();
                return;
            }

            _layoutAnimationStartedAt = EditorApplication.timeSinceStartup;
            _layoutAnimationActive = true;
            SetInteractionsLocked(true);

            if (_layoutAnimationItem == null)
            {
                _layoutAnimationItem = schedule.Execute(UpdateLayoutAnimation).Every((long)LayoutAnimationFrameMs);
            }

            _layoutAnimationItem.Resume();
        }

        private void UpdateLayoutAnimation()
        {
            if (!_layoutAnimationActive || _layoutResult == null)
            {
                return;
            }

            float elapsed = (float)(EditorApplication.timeSinceStartup - _layoutAnimationStartedAt);
            float t = Mathf.Clamp01(elapsed / LayoutTransitionSeconds);
            float eased = SmoothStep(t);

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                Rect start = _transitionStartRects.TryGetValue(target.Key, out Rect startRect)
                    ? startRect
                    : target.Value;
                _animatedNodeRects[target.Key] = LerpRect(start, target.Value, eased);
                PackageGraphNodePresentationLevel presentationLevel = GetPresentationLevel(target.Key);
                bool entering = _transitionEnteringNodeIds.Contains(target.Key);
                float opacity = entering ? EvaluateEnteringOpacity(eased) : 1f;
                float scale = entering ? EvaluateEnteringScale(eased) : 1f;
                _nodeVisualStates[target.Key] = new PackageGraphNodeVisualState(
                    _animatedNodeRects[target.Key],
                    opacity,
                    scale,
                    presentationLevel,
                    true,
                    entering,
                    false);
            }

            foreach (PackageGraphGroupLayoutNode target in _layoutResult.GroupNodes)
            {
                if (target == null)
                {
                    continue;
                }

                Rect start = _transitionStartGroupRects.TryGetValue(target.GroupId, out Rect startRect)
                    ? startRect
                    : target.Rect;
                Vector2 startCenter = _transitionStartGroupCenters.TryGetValue(
                    target.GroupId,
                    out Vector2 transitionStartCenter)
                    ? transitionStartCenter
                    : GetGroupHubRect(target, start).center;
                float startRadius = _transitionStartGroupOrbitRadii.TryGetValue(
                    target.GroupId,
                    out float transitionStartRadius)
                    ? transitionStartRadius
                    : target.OrbitRadius;
                Vector2 center = Vector2.Lerp(startCenter, target.HubCenter, eased);
                Rect lerpedRect = LerpRect(start, target.Rect, eased);
                _animatedGroupCenters[target.GroupId] = center;
                _animatedGroupRects[target.GroupId] = PositionGroupRectFromHubCenter(target, lerpedRect, center);
                _animatedGroupOrbitRadii[target.GroupId] = Mathf.Lerp(startRadius, target.OrbitRadius, eased);
            }

            ApplyAnimatedLayout();

            if (t >= 1f)
            {
                _layoutAnimationActive = false;
                SetInteractionsLocked(false);
                _layoutAnimationItem?.Pause();
                CopyTargetRectsToAnimatedRects();
                ApplyAnimatedLayout();
                Rebuild();
            }
        }

        private Rect CreateEnteringNodeStartRect(
            string packageId,
            Rect targetRect,
            IReadOnlyDictionary<string, Rect> previousGroupRects)
        {
            if (_visibleGraph != null &&
                _visibleGraph.TryGetNode(packageId, out PackageGraphNode node))
            {
                PackageGraphGroupLayoutNode targetGroup = FindGroupLayoutNode(_layoutResult, node.GroupId);

                if (targetGroup != null &&
                    targetGroup.OrbitRadius > 0.01f &&
                    TryGetTargetDirection(targetGroup.HubCenter, targetRect.center, out Vector2 direction))
                {
                    float initialRadius = Mathf.Max(32f, targetGroup.OrbitRadius * 0.24f);
                    return CenterRectOn(targetRect, targetGroup.HubCenter + direction * initialRadius);
                }

                if (TryGetPreviousGroupRect(node.GroupId, previousGroupRects, out Rect groupRect))
                {
                    return CenterRectOn(targetRect, groupRect.center);
                }
            }

            return CenterRectOn(targetRect, GetActiveCenter());
        }

        private Rect CreateEnteringGroupStartRect(
            PackageGraphGroupLayoutNode target,
            IReadOnlyDictionary<string, Rect> previousGroupRects)
        {
            if (target != null &&
                target.Group != null &&
                TryGetPreviousGroupRect(target.Group.ParentGroupId, previousGroupRects, out Rect parentRect))
            {
                return CenterRectOn(target.Rect, parentRect.center);
            }

            return CenterRectOn(target != null ? target.Rect : default(Rect), GetActiveCenter());
        }

        private bool TryGetPreviousGroupRect(
            string groupId,
            IReadOnlyDictionary<string, Rect> previousGroupRects,
            out Rect groupRect)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = groupId;

            while (!string.IsNullOrWhiteSpace(currentGroupId) && visited.Add(currentGroupId))
            {
                if (previousGroupRects != null &&
                    previousGroupRects.TryGetValue(currentGroupId, out groupRect))
                {
                    return true;
                }

                if (_visibleGraph == null ||
                    !_visibleGraph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
                {
                    break;
                }

                currentGroupId = group.ParentGroupId;
            }

            groupRect = default(Rect);
            return false;
        }

        private static Rect CenterRectOn(Rect rect, Vector2 center)
        {
            return new Rect(
                center.x - rect.width * 0.5f,
                center.y - rect.height * 0.5f,
                rect.width,
                rect.height);
        }

        private void SetInteractionsLocked(bool locked)
        {
            if (_interactionsLocked == locked)
            {
                return;
            }

            _interactionsLocked = locked;
            InteractionsLockedChanged?.Invoke(_interactionsLocked);
            ApplyInteractionState();
        }

        private void CopyTargetRectsToAnimatedRects()
        {
            _animatedNodeRects.Clear();
            _nodeVisualStates.Clear();
            _animatedGroupRects.Clear();
            _animatedGroupCenters.Clear();
            _animatedGroupOrbitRadii.Clear();

            if (_layoutResult == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                _animatedNodeRects[target.Key] = target.Value;
                _nodeVisualStates[target.Key] = PackageGraphNodeVisualState.Stable(
                    target.Value,
                    GetPresentationLevel(target.Key));
            }

            foreach (PackageGraphGroupLayoutNode target in _layoutResult.GroupNodes)
            {
                if (target != null)
                {
                    _animatedGroupRects[target.GroupId] = target.Rect;
                    _animatedGroupCenters[target.GroupId] = target.HubCenter;
                    _animatedGroupOrbitRadii[target.GroupId] = target.OrbitRadius;
                }
            }

            NotifyActiveVisualCenterChanged();
        }

        private IReadOnlyDictionary<string, Rect> CreateTargetGroupRectSnapshot()
        {
            Dictionary<string, Rect> groupRects = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

            if (_layoutResult == null || _layoutResult.GroupNodes == null)
            {
                return groupRects;
            }

            foreach (PackageGraphGroupLayoutNode groupNode in _layoutResult.GroupNodes)
            {
                if (groupNode != null && !string.IsNullOrWhiteSpace(groupNode.GroupId))
                {
                    groupRects[groupNode.GroupId] = groupNode.Rect;
                }
            }

            return groupRects;
        }

        private void ApplyInteractionState()
        {
            _guideLayer.style.opacity = 1f;
            _membershipLayer.style.opacity = 1f;
            _edgeLayer.style.opacity = 1f;

            foreach (PackageGraphNodeElement nodeElement in _nodeElements.Values)
            {
                nodeElement.pickingMode = _interactionsLocked ? PickingMode.Ignore : PickingMode.Position;
                SetGraphActionButtonsInteractive(nodeElement, !_interactionsLocked && _actionsEnabled);
            }

            foreach (PackageGraphGroupElement groupElement in _groupElements.Values)
            {
                groupElement.pickingMode = _interactionsLocked ? PickingMode.Ignore : PickingMode.Position;
            }
        }

        private static void SetGraphActionButtonsInteractive(VisualElement root, bool interactive)
        {
            if (root == null)
            {
                return;
            }

            foreach (VisualElement child in root.Children())
            {
                if (child is Button button &&
                    button.ClassListContains("dpi-graph-node__action"))
                {
                    button.SetEnabled(interactive);
                    button.pickingMode = interactive ? PickingMode.Position : PickingMode.Ignore;
                    button.style.opacity = interactive ? 1f : 0f;
                }

                SetGraphActionButtonsInteractive(child, interactive);
            }
        }

        private static PackageGraphGroupLayoutNode FindGroupLayoutNode(
            PackageGraphLayoutResult layout,
            string groupId)
        {
            return layout != null && !string.IsNullOrWhiteSpace(groupId)
                ? layout.GroupNodes.FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(candidate.GroupId, groupId, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        private static Rect GetGroupHubRect(PackageGraphGroupLayoutNode groupNode, Rect groupRect)
        {
            if (groupNode == null)
            {
                return groupRect;
            }

            Vector2 offset = groupNode.HubRect.position - groupNode.Rect.position;
            return new Rect(
                groupRect.x + offset.x,
                groupRect.y + offset.y,
                groupNode.HubRect.width,
                groupNode.HubRect.height);
        }

        private static Rect PositionGroupRectFromHubCenter(
            PackageGraphGroupLayoutNode groupNode,
            Rect currentRect,
            Vector2 hubCenter)
        {
            if (groupNode == null)
            {
                return CenterRectOn(currentRect, hubCenter);
            }

            Vector2 offset = groupNode.HubRect.position - groupNode.Rect.position;
            return new Rect(
                hubCenter.x - offset.x - groupNode.HubRect.width * 0.5f,
                hubCenter.y - offset.y - groupNode.HubRect.height * 0.5f,
                currentRect.width,
                currentRect.height);
        }

        internal static void ProjectOrbitalChildrenForTests(
            PackageGraphModel graph,
            PackageGraphLayoutResult layout,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, Rect> groupRects,
            IDictionary<string, Vector2> groupCenters,
            IDictionary<string, float> groupOrbitRadii)
        {
            ProjectOrbitalChildren(graph, layout, nodeRects, groupRects, groupCenters, groupOrbitRadii);
        }

        private static void ProjectOrbitalChildren(
            PackageGraphModel graph,
            PackageGraphLayoutResult layout,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, Rect> groupRects,
            IDictionary<string, Vector2> groupCenters,
            IDictionary<string, float> groupOrbitRadii)
        {
            if (graph == null ||
                layout == null ||
                layout.Mode == PackageGraphLayoutMode.Focus ||
                nodeRects == null ||
                groupRects == null ||
                groupCenters == null ||
                groupOrbitRadii == null)
            {
                return;
            }

            Dictionary<string, PackageGraphGroupLayoutNode> groupNodeById = layout.GroupNodes
                .Where(groupNode => groupNode != null)
                .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphGroupLayoutNode groupNode in layout.GroupNodes)
            {
                if (groupNode == null ||
                    groupNode.Group == null ||
                    groupNode.Collapsed ||
                    !groupCenters.TryGetValue(groupNode.GroupId, out Vector2 center) ||
                    !groupOrbitRadii.TryGetValue(groupNode.GroupId, out float radius) ||
                    radius <= 0.01f)
                {
                    continue;
                }

                foreach (PackageGraphGroup childGroup in graph.Groups)
                {
                    if (childGroup == null ||
                        !string.Equals(childGroup.ParentGroupId, groupNode.GroupId, StringComparison.OrdinalIgnoreCase) ||
                        !groupNodeById.TryGetValue(childGroup.Id, out PackageGraphGroupLayoutNode childGroupNode) ||
                        !groupRects.TryGetValue(childGroup.Id, out Rect childGroupRect) ||
                        !TryGetTargetDirection(groupNode.HubCenter, childGroupNode.HubCenter, out Vector2 direction))
                    {
                        continue;
                    }

                    Vector2 childCenter = center + direction * radius;
                    groupCenters[childGroup.Id] = childCenter;
                    groupRects[childGroup.Id] = PositionGroupRectFromHubCenter(
                        childGroupNode,
                        childGroupRect,
                        childCenter);
                }

                foreach (PackageGraphNode childNode in graph.Nodes)
                {
                    if (childNode == null ||
                        !string.Equals(childNode.GroupId, groupNode.GroupId, StringComparison.OrdinalIgnoreCase) ||
                        !layout.NodeRects.TryGetValue(childNode.PackageId, out Rect targetNodeRect) ||
                        !nodeRects.TryGetValue(childNode.PackageId, out Rect childRect) ||
                        !TryGetTargetDirection(groupNode.HubCenter, targetNodeRect.center, out Vector2 direction))
                    {
                        continue;
                    }

                    nodeRects[childNode.PackageId] = CenterRectOn(childRect, center + direction * radius);
                }
            }
        }

        private static bool TryGetTargetDirection(
            Vector2 center,
            Vector2 targetChildCenter,
            out Vector2 direction)
        {
            Vector2 delta = targetChildCenter - center;

            if (delta.sqrMagnitude <= 0.0001f)
            {
                direction = default(Vector2);
                return false;
            }

            direction = delta.normalized;
            return true;
        }

        private void SyncNodeVisualStateRects()
        {
            foreach (KeyValuePair<string, Rect> rect in _animatedNodeRects)
            {
                if (_nodeVisualStates.TryGetValue(rect.Key, out PackageGraphNodeVisualState state))
                {
                    _nodeVisualStates[rect.Key] = state.WithRect(rect.Value);
                    continue;
                }

                _nodeVisualStates[rect.Key] = PackageGraphNodeVisualState.Stable(
                    rect.Value,
                    GetPresentationLevel(rect.Key));
            }
        }

        private void RebuildStableNodeVisualStates(IReadOnlyDictionary<string, Rect> rects)
        {
            _nodeVisualStates.Clear();

            if (rects == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> rect in rects)
            {
                _nodeVisualStates[rect.Key] = PackageGraphNodeVisualState.Stable(
                    rect.Value,
                    GetPresentationLevel(rect.Key));
            }
        }

        private void RemoveNodeVisuals(IEnumerable<string> packageIds)
        {
            if (packageIds == null)
            {
                return;
            }

            foreach (string packageId in packageIds.ToArray())
            {
                if (string.IsNullOrWhiteSpace(packageId))
                {
                    continue;
                }

                _animatedNodeRects.Remove(packageId);
                _nodeVisualStates.Remove(packageId);

                if (_nodeElements.TryGetValue(packageId, out PackageGraphNodeElement element))
                {
                    element.RemoveFromHierarchy();
                    _nodeElements.Remove(packageId);
                }
            }
        }

        private PackageGraphNodePresentationLevel GetPresentationLevel(string packageId)
        {
            return _layoutResult != null &&
                   !string.IsNullOrWhiteSpace(packageId) &&
                   _layoutResult.NodePresentationLevels.TryGetValue(
                       packageId,
                       out PackageGraphNodePresentationLevel presentationLevel)
                ? presentationLevel
                : PackageGraphNodePresentationLevel.Compact;
        }

        private static float EvaluateEnteringOpacity(float progress)
        {
            float t = Mathf.Clamp01(progress);

            if (t <= 0.20f)
            {
                return 0f;
            }

            if (t <= 0.65f)
            {
                return Mathf.Lerp(0f, 0.75f, (t - 0.20f) / 0.45f);
            }

            return Mathf.Lerp(0.75f, 1f, (t - 0.65f) / 0.35f);
        }

        private static float EvaluateEnteringScale(float progress)
        {
            float t = Mathf.Clamp01(progress);

            if (t <= 0.20f)
            {
                return 0.24f;
            }

            if (t <= 0.65f)
            {
                return Mathf.Lerp(0.24f, 0.85f, (t - 0.20f) / 0.45f);
            }

            return Mathf.Lerp(0.85f, 1f, (t - 0.65f) / 0.35f);
        }

        private void ApplyAnimatedLayout(bool updateEdgeLayer = true)
        {
            ProjectOrbitalChildren(
                _visibleGraph,
                _layoutResult,
                _animatedNodeRects,
                _animatedGroupRects,
                _animatedGroupCenters,
                _animatedGroupOrbitRadii);
            SyncNodeVisualStateRects();

            foreach (KeyValuePair<string, PackageGraphNodeElement> nodeElement in _nodeElements)
            {
                if (_nodeVisualStates.TryGetValue(nodeElement.Key, out PackageGraphNodeVisualState state))
                {
                    SetElementVisualState(nodeElement.Value, state);
                    nodeElement.Value.pickingMode = _interactionsLocked
                        ? PickingMode.Ignore
                        : nodeElement.Value.pickingMode;
                }
                else if (_animatedNodeRects.TryGetValue(nodeElement.Key, out Rect rect))
                {
                    SetElementVisualState(
                        nodeElement.Value,
                        PackageGraphNodeVisualState.Stable(rect, GetPresentationLevel(nodeElement.Key)));
                    nodeElement.Value.pickingMode = _interactionsLocked
                        ? PickingMode.Ignore
                        : nodeElement.Value.pickingMode;
                }
            }

            foreach (KeyValuePair<string, PackageGraphGroupElement> groupElement in _groupElements)
            {
                if (_animatedGroupRects.TryGetValue(groupElement.Key, out Rect rect))
                {
                    SetElementRect(groupElement.Value, rect);
                }
            }

            if (updateEdgeLayer)
            {
                if (!_layoutAnimationActive)
                {
                    IReadOnlyDictionary<string, Rect> routeNodeRects = _layoutResult != null
                        ? _layoutResult.NodeRects
                        : _animatedNodeRects;
                    _edgeLayer.UpdateRects(routeNodeRects, CreateTargetGroupRectSnapshot());
                }

                _edgeLayer.MarkDirtyRepaint();
            }

            _membershipLayer.UpdateRects(
                _animatedNodeRects,
                _animatedGroupRects,
                _animatedGroupCenters,
                _animatedGroupOrbitRadii);
            _membershipLayer.MarkDirtyRepaint();
            NotifyActiveVisualCenterChanged();
        }

        private void NotifyActiveVisualCenterChanged()
        {
            if (ActiveVisualCenterChanged == null || !TryGetActiveVisualCenter(out Vector2 center))
            {
                return;
            }

            ActiveVisualCenterChanged(center);
        }

        private void DrawGuides()
        {
            foreach (PackageGraphSectorLabel sectorLabel in _layoutResult.SectorLabels)
            {
                Label label = new Label(sectorLabel.Label);
                label.AddToClassList("dpi-graph-sector-label");
                label.AddToClassList("dpi-graph-sector-label--" + GetSectorClass(sectorLabel));
                label.style.left = sectorLabel.Position.x - 92f;
                label.style.top = sectorLabel.Position.y - 12f;
                label.style.width = 184f;
                label.style.height = 24f;
                _guideLayer.Add(label);
            }

            VisualElement hub = new VisualElement();
            hub.AddToClassList("dpi-graph-hub");
            hub.focusable = true;
            hub.tabIndex = 0;
            hub.EnableInClassList("dpi-graph-hub--focus", _layoutResult.Mode == PackageGraphLayoutMode.Focus);
            hub.EnableInClassList("dpi-graph-hub--group-focus", _layoutResult.Mode == PackageGraphLayoutMode.GroupFocus);
            hub.style.left = _layoutResult.HubRect.x;
            hub.style.top = _layoutResult.HubRect.y;
            hub.style.width = _layoutResult.HubRect.width;
            hub.style.height = _layoutResult.HubRect.height;
            hub.tooltip = "Return to Deucarian overview";
            hub.RegisterCallback<ClickEvent>(evt =>
            {
                if (_interactionsLocked)
                {
                    evt.StopPropagation();
                    return;
                }

                _rootFocused?.Invoke();
                evt.StopPropagation();
            });
            hub.RegisterCallback<KeyDownEvent>(evt => PackageGraphKeyboard.Activate(
                evt,
                hub,
                () =>
                {
                    if (!_interactionsLocked)
                    {
                        _rootFocused?.Invoke();
                    }
                }));

            Image hubIcon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon("package-installer"),
                scaleMode = ScaleMode.ScaleToFit
            };
            hubIcon.AddToClassList("dpi-graph-hub__icon");
            hub.Add(hubIcon);

            Label title = new Label("Deucarian");
            title.AddToClassList("dpi-graph-hub__title");
            hub.Add(title);

            Label subtitle = new Label("Unity Package System");
            subtitle.AddToClassList("dpi-graph-hub__subtitle");
            hub.Add(subtitle);
            _guideLayer.Add(hub);
        }

        private void DrawGroups()
        {
            string activeHoverGroupId = GetActiveHoverGroupId();

            foreach (PackageGraphGroupLayoutNode groupNode in _layoutResult.GroupNodes)
            {
                bool hoverActive = !string.IsNullOrWhiteSpace(activeHoverGroupId);
                bool hoverContext = hoverActive && IsGroupInHoverContext(groupNode.GroupId, activeHoverGroupId);
                Action<PackageGraphGroup> groupFocused = _layoutResult.Mode == PackageGraphLayoutMode.GroupFocus &&
                                                         groupNode.Focused
                    ? _ => _selectionCleared?.Invoke()
                    : _groupFocused;
                string backTooltip = _layoutResult.Mode == PackageGraphLayoutMode.GroupFocus && groupNode.Focused
                    ? GetGroupBackTooltip(groupNode.Group)
                    : string.Empty;
                PackageGraphGroupElement groupElement = new PackageGraphGroupElement(
                    groupNode,
                    _layoutResult.Mode,
                    hoverContext,
                    hoverActive && !hoverContext,
                    backTooltip,
                    !_interactionsLocked,
                    groupFocused,
                    SetPreviewGroup,
                    ClearPreviewGroup,
                    GetRootGroupSearchMatchCount(groupNode));
                ApplySearchClasses(
                    groupElement,
                    _searchState.IsDirectCategoryMatch(groupNode.GroupId),
                    _searchState.IsCategoryContext(groupNode.GroupId),
                    _searchState.HasQuery && _layoutResult.Mode != PackageGraphLayoutMode.Focus);
                SetElementRect(groupElement, groupNode.Rect);
                _nodeLayer.Add(groupElement);
                _groupElements[groupNode.GroupId] = groupElement;
            }
        }

        private int GetRootGroupSearchMatchCount(PackageGraphGroupLayoutNode groupNode)
        {
            if (groupNode == null ||
                _layoutResult == null ||
                _layoutResult.Mode != PackageGraphLayoutMode.Overview ||
                _searchState == null ||
                !_searchState.HasQuery)
            {
                return 0;
            }

            int packageMatches = groupNode.RepresentedPackageIds.Count(packageId =>
                _visiblePackageIds.Contains(packageId) &&
                _searchState.IsDirectPackageMatch(packageId));
            int categoryMatches = _searchState.DirectCategoryMatchIds.Count(groupId =>
                IsGroupWithin(groupId, groupNode.GroupId));
            return packageMatches + categoryMatches;
        }

        private bool IsGroupWithin(string candidateGroupId, string ancestorGroupId)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = candidateGroupId;

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   _graph.TryGetGroup(currentGroupId, out PackageGraphGroup currentGroup))
            {
                if (string.Equals(currentGroup.Id, ancestorGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentGroupId = currentGroup.ParentGroupId;
            }

            return false;
        }

        private string GetGroupBackTooltip(PackageGraphGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(group.ParentGroupId))
            {
                return "Back to Ecosystem Overview";
            }

            return _visibleGraph != null &&
                   _visibleGraph.TryGetGroup(group.ParentGroupId, out PackageGraphGroup parentGroup)
                ? "Back to " + parentGroup.DisplayName
                : "Back to Ecosystem Overview";
        }

        private string GetPackageBackTooltip(string groupId)
        {
            return _visibleGraph != null &&
                   !string.IsNullOrWhiteSpace(groupId) &&
                   _visibleGraph.TryGetGroup(groupId, out PackageGraphGroup group)
                ? "Back to " + group.DisplayName
                : "Back to owning category";
        }

        private void DrawNodes(PackageGraphFocus focus)
        {
            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.VisualNodeCreation))
            {
                foreach (PackageGraphNode node in _visibleGraph.Nodes)
                {
                    if (!_animatedNodeRects.TryGetValue(node.PackageId, out Rect rect))
                    {
                        continue;
                    }

                    PackageGraphNodeElement nodeElement = CreateNodeElement(node, focus, _layoutResult);
                    if (_nodeVisualStates.TryGetValue(node.PackageId, out PackageGraphNodeVisualState visualState))
                    {
                        SetElementVisualState(nodeElement, visualState);
                    }
                    else
                    {
                        SetElementRect(nodeElement, rect);
                    }

                    _nodeLayer.Add(nodeElement);
                    _nodeElements[node.PackageId] = nodeElement;
                }
            }

            PackageGraphOpenProfiler.Current?.SetRenderCounts(_nodeElements.Count, 0);
        }

        private PackageGraphNodeElement CreateNodeElement(
            PackageGraphNode node,
            PackageGraphFocus focus,
            PackageGraphLayoutResult layout)
        {
            string activeHoverGroupId = GetActiveHoverGroupId();
            bool hoverActive = !string.IsNullOrWhiteSpace(activeHoverGroupId);
            PackageGraphLayoutRing ring = layout != null &&
                                          layout.NodeRings.TryGetValue(
                                              node.PackageId,
                                              out PackageGraphLayoutRing nodeRing)
                ? nodeRing
                : PackageGraphLayoutRing.Infrastructure;
            bool selected = string.Equals(
                node.PackageId,
                _selectedPackageId,
                StringComparison.OrdinalIgnoreCase);
            bool previewed = string.Equals(
                node.PackageId,
                _hoveredPackageId,
                StringComparison.OrdinalIgnoreCase);
            bool related = focus != null && focus.IsPackageRelated(node.PackageId);
            bool dimmed = focus != null && focus.HasFocus && !related;
            bool hoverContext = hoverActive && IsPackageInHoverContext(node, activeHoverGroupId);
            bool hoverDimmed = hoverActive && !hoverContext;
            PackageGraphNodeVisualMode visualMode = GetNodeVisualMode(dimmed);
            PackageGraphNodePresentationLevel presentationLevel =
                layout != null &&
                layout.NodePresentationLevels.TryGetValue(
                    node.PackageId,
                    out PackageGraphNodePresentationLevel resolvedPresentation)
                    ? resolvedPresentation
                    : PackageGraphPresentationPolicy.GetFocusPresentation(selected);
            string categoryPathLabel = GetGroupPathLabel(node.GroupId);
            bool showNodeAction = ShouldShowNodeAction(
                node,
                _actionFocus,
                _actionFocus.IsPackageRelated(node.PackageId),
                layout != null ? layout.Mode : PackageGraphLayoutMode.Overview);
            bool nodeActionsEnabled = showNodeAction && _actionsEnabled && !_interactionsLocked;
            string backTooltip = selected &&
                                 layout != null &&
                                 layout.Mode == PackageGraphLayoutMode.Focus
                ? GetPackageBackTooltip(node.GroupId)
                : string.Empty;
            string relationshipTooltip = GetRelationshipTooltip(node, layout);
            PackageGraphNodeElement nodeElement = new PackageGraphNodeElement(
                node,
                ring,
                visualMode,
                presentationLevel,
                selected,
                related,
                dimmed,
                hoverContext,
                hoverDimmed,
                previewed,
                showNodeAction,
                nodeActionsEnabled,
                !_interactionsLocked,
                categoryPathLabel,
                backTooltip,
                relationshipTooltip,
                _packageSelected,
                _packageAction,
                _selectionCleared,
                SetPreviewPackage,
                ClearPreviewPackage);
            ApplySearchClasses(
                nodeElement,
                _searchState.IsDirectPackageMatch(node.PackageId),
                _searchState.IsPackageContext(node.PackageId),
                _searchState.HasQuery && layout.Mode != PackageGraphLayoutMode.Focus);
            return nodeElement;
        }

        private string GetRelationshipTooltip(PackageGraphNode node, PackageGraphLayoutResult layout)
        {
            if (node == null || layout == null || layout.Mode != PackageGraphLayoutMode.Focus ||
                string.IsNullOrWhiteSpace(layout.FocusPackageId) ||
                string.Equals(node.PackageId, layout.FocusPackageId, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string[] labels = _visibleGraph.Edges
                .Where(edge => edge != null &&
                               edge.ConnectsPackage(node.PackageId) &&
                               edge.ConnectsPackage(layout.FocusPackageId))
                .Select(edge => FormatRelationshipTooltipLabel(edge.Kind, edge.Label))
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return labels.Length == 0
                ? string.Empty
                : "Relationship: " + string.Join("; ", labels);
        }

        private static string FormatRelationshipTooltipLabel(
            PackageGraphEdgeKind kind,
            string relationshipLabel)
        {
            string kindLabel = GetRelationshipKindLabel(kind);
            string detail = string.IsNullOrWhiteSpace(relationshipLabel)
                ? string.Empty
                : relationshipLabel.Trim();
            return string.IsNullOrWhiteSpace(detail) ||
                   string.Equals(detail, kindLabel, StringComparison.OrdinalIgnoreCase)
                ? kindLabel
                : kindLabel + " - " + detail;
        }

        private static string GetRelationshipKindLabel(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                    return "Required dependency";
                case PackageGraphEdgeKind.IntegrationConnection:
                    return "Integration connection";
                case PackageGraphEdgeKind.OptionalCompanion:
                    return "Optional companion";
                case PackageGraphEdgeKind.SuiteMembership:
                    return "Suite membership";
                default:
                    return "Recommended relationship";
            }
        }

        private void DrawUnrelatedSummary()
        {
            if (_layoutResult == null || !_layoutResult.HasUnrelatedSummary)
            {
                return;
            }

            Label summary = new Label("+" + _layoutResult.UnrelatedPackageCount + " unrelated packages");
            summary.name = "ecosystem-graph-unrelated-summary";
            summary.AddToClassList("dpi-graph-unrelated-summary");
            summary.tooltip = "Return to overview";
            summary.focusable = true;
            summary.tabIndex = 0;
            summary.SetEnabled(!_interactionsLocked);
            summary.RegisterCallback<ClickEvent>(evt =>
            {
                if (_interactionsLocked)
                {
                    evt.StopPropagation();
                    return;
                }

                _selectionCleared?.Invoke();
                evt.StopPropagation();
            });
            summary.RegisterCallback<KeyDownEvent>(evt => PackageGraphKeyboard.Activate(
                evt,
                summary,
                () =>
                {
                    if (!_interactionsLocked)
                    {
                        _selectionCleared?.Invoke();
                    }
                }));
            SetElementRect(summary, _layoutResult.UnrelatedSummaryRect);
            _nodeLayer.Add(summary);
        }

        private void DrawOverflowSummaries()
        {
            if (_layoutResult == null || _layoutResult.OverflowSummaries.Count == 0)
            {
                return;
            }

            foreach (PackageGraphOverflowSummary overflow in _layoutResult.OverflowSummaries)
            {
                PackageGraphOverflowSummaryElement summary = new PackageGraphOverflowSummaryElement(
                    "+" + overflow.HiddenCount + " related packages",
                    GetOverflowDiagnostic(overflow));
                summary.name = "ecosystem-graph-overflow-" + GetOverflowZoneClass(overflow.Zone);
                summary.AddToClassList("dpi-graph-unrelated-summary");
                summary.AddToClassList("dpi-graph-overflow-summary");
                summary.AddToClassList("dpi-graph-overflow-summary--" + GetOverflowZoneClass(overflow.Zone));
                summary.tooltip = "Additional " + GetOverflowZoneLabel(overflow.Zone) +
                                  " are summarized to keep this dense relationship view readable. " +
                                  "Click or press Enter/Space to copy their IDs.";
                SetElementRect(summary, overflow.Rect);
                _nodeLayer.Add(summary);
            }
        }

        private string GetOverflowDiagnostic(PackageGraphOverflowSummary overflow)
        {
            if (overflow == null)
            {
                return string.Empty;
            }

            PackageGraphNode[] hiddenNodes = GetOverflowNodes(overflow.Zone)
                .Take(overflow.HiddenCount)
                .ToArray();
            List<string> lines = new List<string>
            {
                "Additional " + GetOverflowZoneLabel(overflow.Zone) + ": " + overflow.HiddenCount
            };
            lines.AddRange(hiddenNodes.Select(node => node.DisplayName + " (" + node.PackageId + ")"));
            return string.Join("\n", lines.ToArray());
        }

        private IEnumerable<PackageGraphNode> GetOverflowNodes(PackageGraphEgoLayoutZone zone)
        {
            if (_layoutResult == null ||
                _visibleGraph == null ||
                string.IsNullOrWhiteSpace(_layoutResult.FocusPackageId))
            {
                return Enumerable.Empty<PackageGraphNode>();
            }

            string focusPackageId = _layoutResult.FocusPackageId;
            return _visibleGraph.Nodes
                .Where(node => node != null &&
                               !_layoutResult.NodeRects.ContainsKey(node.PackageId) &&
                               ResolveEgoZone(node, focusPackageId) == zone)
                .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private PackageGraphEgoLayoutZone ResolveEgoZone(
            PackageGraphNode node,
            string focusPackageId)
        {
            if (node == null || string.IsNullOrWhiteSpace(focusPackageId))
            {
                return PackageGraphEgoLayoutZone.OwningCategory;
            }

            if (_visibleGraph.GetHardDependencyProviderEdges(focusPackageId).Any(edge =>
                    string.Equals(edge.FromPackageId, node.PackageId, StringComparison.OrdinalIgnoreCase)))
            {
                return PackageGraphEgoLayoutZone.Providers;
            }

            if (node.NodeType != PackageGraphNodeType.Integration &&
                _visibleGraph.GetHardDependencyDependentEdges(focusPackageId).Any(edge =>
                    string.Equals(edge.ToPackageId, node.PackageId, StringComparison.OrdinalIgnoreCase)))
            {
                return PackageGraphEgoLayoutZone.Dependents;
            }

            if (node.NodeType == PackageGraphNodeType.Integration &&
                (_visibleGraph.GetIntegrationEdges(focusPackageId).Any(edge =>
                     edge.ConnectsPackage(node.PackageId)) ||
                 _visibleGraph.GetEdgesForPackage(focusPackageId).Any(edge =>
                     edge.Kind == PackageGraphEdgeKind.HardDependency &&
                     edge.ConnectsPackage(node.PackageId))))
            {
                return PackageGraphEgoLayoutZone.Integrations;
            }

            bool optionalCompanion = _visibleGraph.GetOptionalCompanionEdges(focusPackageId).Any(edge =>
                edge.ConnectsPackage(node.PackageId));
            bool suiteRelationship = _visibleGraph.GetSuiteRegionsForPackage(focusPackageId).Any(region =>
                (string.Equals(
                     region.SuitePackageId,
                     focusPackageId,
                     StringComparison.OrdinalIgnoreCase) &&
                 region.MemberPackageIds.Any(memberPackageId => string.Equals(
                     memberPackageId,
                     node.PackageId,
                     StringComparison.OrdinalIgnoreCase))) ||
                (region.MemberPackageIds.Any(memberPackageId => string.Equals(
                     memberPackageId,
                     focusPackageId,
                     StringComparison.OrdinalIgnoreCase)) &&
                 string.Equals(
                     region.SuitePackageId,
                     node.PackageId,
                     StringComparison.OrdinalIgnoreCase)));
            return optionalCompanion || suiteRelationship
                ? PackageGraphEgoLayoutZone.CompanionsAndSuites
                : PackageGraphEgoLayoutZone.OwningCategory;
        }

        private static string GetOverflowZoneClass(PackageGraphEgoLayoutZone zone)
        {
            switch (zone)
            {
                case PackageGraphEgoLayoutZone.Providers:
                    return "providers";
                case PackageGraphEgoLayoutZone.Dependents:
                    return "dependents";
                case PackageGraphEgoLayoutZone.Integrations:
                    return "integrations";
                default:
                    return "companions";
            }
        }

        private static string GetOverflowZoneLabel(PackageGraphEgoLayoutZone zone)
        {
            switch (zone)
            {
                case PackageGraphEgoLayoutZone.Providers:
                    return "prerequisites";
                case PackageGraphEgoLayoutZone.Dependents:
                    return "dependent packages";
                case PackageGraphEgoLayoutZone.Integrations:
                    return "integration packages";
                default:
                    return "companions and suite packages";
            }
        }

        private PackageGraphNodeVisualMode GetNodeVisualMode(bool dimmed)
        {
            if (_layoutResult != null && _layoutResult.Mode != PackageGraphLayoutMode.Focus)
            {
                return PackageGraphNodeVisualMode.Overview;
            }

            return dimmed ? PackageGraphNodeVisualMode.Stack : PackageGraphNodeVisualMode.Focus;
        }

        private static bool ShouldShowNodeAction(
            PackageGraphNode node,
            PackageGraphFocus focus,
            bool related,
            PackageGraphLayoutMode layoutMode)
        {
            return node != null &&
                   focus != null &&
                   layoutMode == PackageGraphLayoutMode.Focus &&
                   focus.HasFocus &&
                   related;
        }

        private static void ApplySearchClasses(
            VisualElement element,
            bool directMatch,
            bool contextMatch,
            bool searchActive)
        {
            if (element == null)
            {
                return;
            }

            element.EnableInClassList("dpi-graph-search--active", searchActive);
            element.EnableInClassList("dpi-graph-search--match", searchActive && directMatch);
            element.EnableInClassList("dpi-graph-search--context", searchActive && !directMatch && contextMatch);
            element.EnableInClassList(
                "dpi-graph-search--dimmed",
                searchActive && !directMatch && !contextMatch);
        }

        private void SetPreviewPackage(string packageId)
        {
            if (_interactionsLocked)
            {
                return;
            }

            if (string.Equals(_hoveredPackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredPackageId = packageId ?? string.Empty;
            NotifyActiveHoverGroupChanged();
            RefreshHoverVisualState();
        }

        private void ClearPreviewPackage(string packageId)
        {
            if (_interactionsLocked)
            {
                return;
            }

            if (!string.Equals(_hoveredPackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredPackageId = string.Empty;
            NotifyActiveHoverGroupChanged();
            RefreshHoverVisualState();
        }

        private void SetPreviewGroup(string groupId)
        {
            if (_interactionsLocked)
            {
                return;
            }

            if (string.Equals(_hoveredGroupId, groupId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredGroupId = groupId ?? string.Empty;
            NotifyActiveHoverGroupChanged();
            RefreshHoverVisualState();
        }

        private void ClearPreviewGroup(string groupId)
        {
            if (_interactionsLocked)
            {
                return;
            }

            if (!string.Equals(_hoveredGroupId, groupId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredGroupId = string.Empty;
            NotifyActiveHoverGroupChanged();
            RefreshHoverVisualState();
        }

        private void RefreshHoverVisualState()
        {
            string activeHoverGroupId = GetActiveHoverGroupId();
            bool hoverActive = !string.IsNullOrWhiteSpace(activeHoverGroupId);

            foreach (KeyValuePair<string, PackageGraphGroupElement> pair in _groupElements)
            {
                bool hoverContext = hoverActive &&
                                    IsGroupInHoverContext(pair.Key, activeHoverGroupId);
                pair.Value.SetHoverState(hoverContext, hoverActive && !hoverContext);
            }

            foreach (KeyValuePair<string, PackageGraphNodeElement> pair in _nodeElements)
            {
                if (!_visibleGraph.TryGetNode(pair.Key, out PackageGraphNode node))
                {
                    continue;
                }

                bool hoverContext = hoverActive &&
                                    IsPackageInHoverContext(node, activeHoverGroupId);
                pair.Value.SetPreviewState(
                    string.Equals(pair.Key, _hoveredPackageId, StringComparison.OrdinalIgnoreCase),
                    hoverContext,
                    hoverActive && !hoverContext);
            }

            _membershipLayer.SetHoverState(activeHoverGroupId, _interactionsLocked);
            _edgeLayer.SetPreviewPackage(_hoveredPackageId);
        }

        private void NotifyActiveHoverGroupChanged()
        {
            ActiveHoverGroupChanged?.Invoke(GetActiveHoverGroupId());
        }

        private string GetActiveHoverGroupId()
        {
            if (!string.IsNullOrWhiteSpace(_hoveredGroupId))
            {
                return _hoveredGroupId;
            }

            if (!string.IsNullOrWhiteSpace(_hoveredPackageId) &&
                _visibleGraph.TryGetNode(_hoveredPackageId, out PackageGraphNode node))
            {
                return node.GroupId;
            }

            return string.Empty;
        }

        private string GetGroupPathLabel(string groupId)
        {
            if (_visibleGraph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return string.Empty;
            }

            List<string> path = new List<string>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = groupId;

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   _visibleGraph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                path.Add(group.DisplayName);
                currentGroupId = group.ParentGroupId;
            }

            path.Reverse();
            return string.Join(" / ", path.ToArray());
        }

        private bool IsPackageInHoverContext(PackageGraphNode node, string groupId)
        {
            return node != null &&
                   !string.IsNullOrWhiteSpace(groupId) &&
                   IsGroupInStructuralContext(node.GroupId, groupId, includeAncestors: false);
        }

        private bool IsGroupInHoverContext(string groupId, string activeGroupId)
        {
            return IsGroupInStructuralContext(groupId, activeGroupId, includeAncestors: true);
        }

        private bool IsGroupInStructuralContext(
            string candidateGroupId,
            string activeGroupId,
            bool includeAncestors)
        {
            if (string.IsNullOrWhiteSpace(candidateGroupId) || string.IsNullOrWhiteSpace(activeGroupId))
            {
                return false;
            }

            if (string.Equals(candidateGroupId, activeGroupId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string currentGroupId = candidateGroupId;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   _visibleGraph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                if (string.Equals(group.ParentGroupId, activeGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (includeAncestors &&
                    string.Equals(currentGroupId, activeGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentGroupId = group.ParentGroupId;
            }

            if (!includeAncestors)
            {
                return false;
            }

            currentGroupId = activeGroupId;
            visited.Clear();

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   _visibleGraph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                if (string.Equals(group.ParentGroupId, candidateGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentGroupId = group.ParentGroupId;
            }

            return false;
        }

        private static void SetElementRect(VisualElement element, Rect rect)
        {
            if (element == null)
            {
                return;
            }

            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.width = rect.width;
            element.style.height = rect.height;
        }

        private static void SetElementVisualState(
            VisualElement element,
            PackageGraphNodeVisualState state)
        {
            if (element == null)
            {
                return;
            }

            SetElementRect(element, state.Rect);
            element.style.opacity = state.Opacity;
            element.style.scale = new Scale(new Vector3(state.Scale, state.Scale, 1f));
            element.style.transformOrigin = new TransformOrigin(
                new Length(50f, LengthUnit.Percent),
                new Length(50f, LengthUnit.Percent),
                0f);
            element.pickingMode = state.Visible && state.Opacity > 0.75f
                ? PickingMode.Position
                : PickingMode.Ignore;
        }

        private static Rect LerpRect(Rect start, Rect end, float t)
        {
            return new Rect(
                Mathf.Lerp(start.x, end.x, t),
                Mathf.Lerp(start.y, end.y, t),
                Mathf.Lerp(start.width, end.width, t),
                Mathf.Lerp(start.height, end.height, t));
        }

        private static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static bool AreRectsClose(Rect first, Rect second)
        {
            return Mathf.Abs(first.x - second.x) < 0.5f &&
                   Mathf.Abs(first.y - second.y) < 0.5f &&
                   Mathf.Abs(first.width - second.width) < 0.5f &&
                   Mathf.Abs(first.height - second.height) < 0.5f;
        }

        private static void StretchToCanvas(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.top = 0f;
            element.style.width = PackageGraphLayout.CanvasWidth;
            element.style.height = PackageGraphLayout.CanvasHeight;
        }

        private static string GetRingClass(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return "runtime";
                case PackageGraphLayoutRing.Integration:
                    return "integration";
                case PackageGraphLayoutRing.Suite:
                    return "suite";
                default:
                    return "infrastructure";
            }
        }

        private static string GetSectorClass(PackageGraphSectorLabel label)
        {
            return label != null && !string.IsNullOrWhiteSpace(label.ClassName)
                ? label.ClassName
                : GetRingClass(label != null ? label.Ring : PackageGraphLayoutRing.Infrastructure);
        }
    }

    internal readonly struct PackageGraphOrbitVisualState
    {
        public PackageGraphOrbitVisualState(
            string orbitId,
            Vector2 center,
            float radius,
            float fillOpacity,
            float strokeOpacity,
            bool emphasized,
            bool muted,
            bool visible,
            bool empty)
        {
            OrbitId = orbitId ?? string.Empty;
            Center = center;
            Radius = Mathf.Max(0f, radius);
            FillOpacity = Mathf.Clamp01(fillOpacity);
            StrokeOpacity = Mathf.Clamp01(strokeOpacity);
            Emphasized = emphasized;
            Muted = muted;
            Visible = visible;
            Empty = empty;
        }

        public string OrbitId { get; }

        public Vector2 Center { get; }

        public float Radius { get; }

        public float FillOpacity { get; }

        public float StrokeOpacity { get; }

        public bool Emphasized { get; }

        public bool Muted { get; }

        public bool Visible { get; }

        public bool Empty { get; }
    }

    internal readonly struct PackageGraphStructuralMembershipSegment
    {
        public PackageGraphStructuralMembershipSegment(Vector2 from, Vector2 to)
        {
            From = from;
            To = to;
        }

        public Vector2 From { get; }

        public Vector2 To { get; }

        public float Length => Vector2.Distance(From, To);
    }

    internal readonly struct PackageGraphStructuralMembershipRoute
    {
        public PackageGraphStructuralMembershipRoute(
            string groupId,
            IReadOnlyList<string> packageIds,
            IReadOnlyList<PackageGraphStructuralMembershipSegment> segments,
            bool usesBus)
        {
            GroupId = groupId ?? string.Empty;
            PackageIds = packageIds == null
                ? Array.Empty<string>()
                : packageIds
                    .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            Segments = segments == null
                ? Array.Empty<PackageGraphStructuralMembershipSegment>()
                : segments.Where(segment => segment.Length > 0.01f).ToArray();
            UsesBus = usesBus;
        }

        public string GroupId { get; }

        public IReadOnlyList<string> PackageIds { get; }

        public IReadOnlyList<PackageGraphStructuralMembershipSegment> Segments { get; }

        public bool UsesBus { get; }

        public PackageGraphRouteKind RouteKind => PackageGraphRouteKind.StructuralMembership;
    }

    internal sealed class PackageGraphMembershipLayer : VisualElement
    {
        private readonly Dictionary<string, Rect> _nodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _groupRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector2> _groupCenters =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _groupOrbitRadii =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphLayoutResult _layout;
        private PackageGraphCategoryStatusSummary _rootStatusSummary;
        private string _hoveredGroupId = string.Empty;
        private bool _interactionsLocked;

        public PackageGraphMembershipLayer()
        {
            generateVisualContent += GenerateMembershipGuides;
        }

        public void SetLayout(
            PackageGraphModel graph,
            PackageGraphLayoutResult layout,
            PackageGraphCategoryStatusSummary rootStatusSummary,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            IReadOnlyDictionary<string, Vector2> groupCenters,
            IReadOnlyDictionary<string, float> groupOrbitRadii,
            string hoveredGroupId,
            bool interactionsLocked)
        {
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _layout = layout;
            _rootStatusSummary = rootStatusSummary;
            _hoveredGroupId = hoveredGroupId ?? string.Empty;
            _interactionsLocked = interactionsLocked;
            CopyNodeRects(nodeRects);
            CopyGroupRects(groupRects);
            CopyGroupCenters(groupCenters);
            CopyGroupOrbitRadii(groupOrbitRadii);
            MarkDirtyRepaint();
        }

        public void UpdateRects(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            IReadOnlyDictionary<string, Vector2> groupCenters,
            IReadOnlyDictionary<string, float> groupOrbitRadii)
        {
            CopyNodeRects(nodeRects);
            CopyGroupRects(groupRects);
            CopyGroupCenters(groupCenters);
            CopyGroupOrbitRadii(groupOrbitRadii);
            MarkDirtyRepaint();
        }

        public void SetHoverState(string hoveredGroupId, bool interactionsLocked)
        {
            string nextHoveredGroupId = hoveredGroupId ?? string.Empty;
            if (string.Equals(
                    _hoveredGroupId,
                    nextHoveredGroupId,
                    StringComparison.OrdinalIgnoreCase) &&
                _interactionsLocked == interactionsLocked)
            {
                return;
            }

            _hoveredGroupId = nextHoveredGroupId;
            _interactionsLocked = interactionsLocked;
            MarkDirtyRepaint();
        }

        private void CopyNodeRects(IReadOnlyDictionary<string, Rect> nodeRects)
        {
            _nodeRects.Clear();

            if (nodeRects == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> nodeRect in nodeRects)
            {
                _nodeRects[nodeRect.Key] = nodeRect.Value;
            }
        }

        private void CopyGroupRects(IReadOnlyDictionary<string, Rect> groupRects)
        {
            _groupRects.Clear();

            if (groupRects == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> groupRect in groupRects)
            {
                _groupRects[groupRect.Key] = groupRect.Value;
            }
        }

        private void CopyGroupCenters(IReadOnlyDictionary<string, Vector2> groupCenters)
        {
            _groupCenters.Clear();

            if (groupCenters == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Vector2> groupCenter in groupCenters)
            {
                _groupCenters[groupCenter.Key] = groupCenter.Value;
            }
        }

        private void CopyGroupOrbitRadii(IReadOnlyDictionary<string, float> groupOrbitRadii)
        {
            _groupOrbitRadii.Clear();

            if (groupOrbitRadii == null)
            {
                return;
            }

            foreach (KeyValuePair<string, float> radius in groupOrbitRadii)
            {
                _groupOrbitRadii[radius.Key] = Mathf.Max(0f, radius.Value);
            }
        }

        private void GenerateMembershipGuides(MeshGenerationContext context)
        {
            if (_layout == null ||
                _layout.GroupNodes.Count == 0)
            {
                return;
            }

            PackageGraphPainter painter = PackageGraphPainterCompatibility.Create(context);
            Dictionary<string, PackageGraphGroupLayoutNode> groupNodeById = _layout.GroupNodes
                .Where(groupNode => groupNode != null && groupNode.Group != null)
                .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphOrbitVisualState orbit in BuildOrbitVisualStates(groupNodeById))
            {
                DrawOrbitCircle(painter, orbit);
            }

            foreach (CategoryStatusRingVisualState statusRing in BuildStatusRingVisualStates(groupNodeById))
            {
                DrawStatusRing(painter, statusRing);
            }

            if (_layout.Mode == PackageGraphLayoutMode.Focus)
            {
                DrawFocusMembershipGuides(painter, groupNodeById);
                PackageGraphPainterCompatibility.Complete(painter);
                return;
            }

            foreach (PackageGraphGroupLayoutNode groupNode in _layout.GroupNodes)
            {
                if (groupNode == null || groupNode.Group == null || groupNode.Collapsed)
                {
                    continue;
                }

                Rect[] childRects = GetDirectChildRects(groupNode.GroupId, groupNodeById).ToArray();
                float orbitRadius = GetGroupOrbitRadius(groupNode);

                if (orbitRadius <= 0.01f)
                {
                    continue;
                }

                Rect groupRect = GetGroupRect(groupNode);
                Rect groupHubRect = GetAnimatedGroupHubRect(groupNode, groupRect);
                foreach (Rect childRect in childRects)
                {
                    bool hoverActive = !_interactionsLocked && !string.IsNullOrWhiteSpace(_hoveredGroupId);
                    bool emphasized = hoverActive &&
                                      IsGroupInHoverContext(groupNode.GroupId, _hoveredGroupId, groupNodeById);
                    bool muted = hoverActive && !emphasized;
                    DrawSpoke(painter, groupHubRect, childRect, emphasized, muted);
                }
            }

            PackageGraphPainterCompatibility.Complete(painter);
        }

        internal IReadOnlyList<PackageGraphOrbitVisualState> BuildOrbitVisualStatesForTests()
        {
            Dictionary<string, PackageGraphGroupLayoutNode> groupNodeById = _layout != null
                ? _layout.GroupNodes
                    .Where(groupNode => groupNode != null && groupNode.Group != null)
                    .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, PackageGraphGroupLayoutNode>(StringComparer.OrdinalIgnoreCase);
            return BuildOrbitVisualStates(groupNodeById);
        }

        internal IReadOnlyList<CategoryStatusRingVisualState> BuildStatusRingVisualStatesForTests()
        {
            Dictionary<string, PackageGraphGroupLayoutNode> groupNodeById = _layout != null
                ? _layout.GroupNodes
                    .Where(groupNode => groupNode != null && groupNode.Group != null)
                    .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, PackageGraphGroupLayoutNode>(StringComparer.OrdinalIgnoreCase);
            return BuildStatusRingVisualStates(groupNodeById);
        }

        private IReadOnlyList<CategoryStatusRingVisualState> BuildStatusRingVisualStates(
            IReadOnlyDictionary<string, PackageGraphGroupLayoutNode> groupNodeById)
        {
            List<CategoryStatusRingVisualState> states = new List<CategoryStatusRingVisualState>();

            if (_layout == null)
            {
                return states;
            }

            states.Add(new CategoryStatusRingVisualState(
                "root:status",
                _layout.HubRect.center,
                Mathf.Min(_layout.HubRect.width, _layout.HubRect.height) * 0.5f + 6f,
                4f,
                PackageGraphCategoryStatusVisuals.CreateSlices(_rootStatusSummary),
                false));

            foreach (PackageGraphGroupLayoutNode groupNode in _layout.GroupNodes)
            {
                if (groupNode == null || groupNode.Group == null)
                {
                    continue;
                }

                Rect groupRect = GetGroupRect(groupNode);
                Rect groupHubRect = GetAnimatedGroupHubRect(groupNode, groupRect);
                bool hoverActive = !_interactionsLocked && !string.IsNullOrWhiteSpace(_hoveredGroupId);
                bool emphasized = hoverActive &&
                                  IsGroupInHoverContext(groupNode.GroupId, _hoveredGroupId, groupNodeById);
                states.Add(new CategoryStatusRingVisualState(
                    "group:" + groupNode.GroupId + ":status",
                    groupHubRect.center,
                    Mathf.Min(groupHubRect.width, groupHubRect.height) * 0.5f + 5f,
                    emphasized ? 4.8f : 4f,
                    PackageGraphCategoryStatusVisuals.CreateSlices(groupNode.StatusSummary),
                    emphasized));
            }

            return states;
        }

        private IReadOnlyList<PackageGraphOrbitVisualState> BuildOrbitVisualStates(
            IReadOnlyDictionary<string, PackageGraphGroupLayoutNode> groupNodeById)
        {
            List<PackageGraphOrbitVisualState> states = new List<PackageGraphOrbitVisualState>();

            if (_layout == null)
            {
                return states;
            }

            for (int index = 0; index < _layout.RingGuides.Count; index++)
            {
                PackageGraphRingGuide guide = _layout.RingGuides[index];

                if (guide == null || guide.Radius <= 0.01f)
                {
                    continue;
                }

                states.Add(new PackageGraphOrbitVisualState(
                    "root:" + index + ":" + guide.Ring,
                    guide.Center,
                    guide.Radius,
                    0.018f,
                    0.075f,
                    false,
                    false,
                    true,
                    false));
            }

            if (_layout.Mode == PackageGraphLayoutMode.Focus)
            {
                return states;
            }

            foreach (PackageGraphGroupLayoutNode groupNode in _layout.GroupNodes)
            {
                if (groupNode == null || groupNode.Group == null || groupNode.Collapsed)
                {
                    continue;
                }

                float orbitRadius = GetGroupOrbitRadius(groupNode);

                if (orbitRadius <= 0.01f)
                {
                    continue;
                }

                Rect groupRect = GetGroupRect(groupNode);
                Rect groupHubRect = GetAnimatedGroupHubRect(groupNode, groupRect);
                bool hoverActive = !_interactionsLocked && !string.IsNullOrWhiteSpace(_hoveredGroupId);
                bool emphasized = hoverActive &&
                                  IsGroupInHoverContext(groupNode.GroupId, _hoveredGroupId, groupNodeById);
                bool muted = hoverActive && !emphasized;
                bool empty = groupNode.PackageCount == 0;
                states.Add(new PackageGraphOrbitVisualState(
                    "group:" + groupNode.GroupId,
                    groupHubRect.center,
                    orbitRadius,
                    empty ? 0.018f : emphasized ? 0.065f : muted ? 0.015f : 0.038f,
                    empty ? 0.045f : emphasized ? 0.18f : muted ? 0.035f : 0.085f,
                    emphasized,
                    muted,
                    true,
                    empty));
            }

            return states;
        }

        private void DrawFocusMembershipGuides(
            PackageGraphPainter painter,
            IReadOnlyDictionary<string, PackageGraphGroupLayoutNode> groupNodeById)
        {
            foreach (PackageGraphStructuralMembershipRoute route in BuildFocusMembershipRoutes(groupNodeById))
            {
                bool hoverActive = !_interactionsLocked && !string.IsNullOrWhiteSpace(_hoveredGroupId);
                bool emphasized = hoverActive && IsGroupInHoverContext(route.GroupId, _hoveredGroupId, groupNodeById);
                bool muted = hoverActive && !emphasized;
                DrawStructuralMembershipRoute(painter, route, emphasized, muted);
            }
        }

        internal IReadOnlyList<PackageGraphStructuralMembershipRoute> FocusMembershipRoutesForTests()
        {
            Dictionary<string, PackageGraphGroupLayoutNode> groupNodeById = _layout != null
                ? _layout.GroupNodes
                    .Where(groupNode => groupNode != null && groupNode.Group != null)
                    .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, PackageGraphGroupLayoutNode>(StringComparer.OrdinalIgnoreCase);
            return BuildFocusMembershipRoutes(groupNodeById);
        }

        private IReadOnlyList<PackageGraphStructuralMembershipRoute> BuildFocusMembershipRoutes(
            IReadOnlyDictionary<string, PackageGraphGroupLayoutNode> groupNodeById)
        {
            List<PackageGraphStructuralMembershipRoute> routes = new List<PackageGraphStructuralMembershipRoute>();

            if (_layout == null || _layout.Mode != PackageGraphLayoutMode.Focus)
            {
                return routes;
            }

            foreach (PackageGraphGroupLayoutNode groupNode in _layout.GroupNodes)
            {
                if (groupNode == null ||
                    groupNode.Group == null ||
                    groupNode.RepresentedPackageIds.Count == 0)
                {
                    continue;
                }

                Rect groupRect = GetGroupRect(groupNode);
                Rect groupHubRect = GetAnimatedGroupHubRect(groupNode, groupRect);
                List<KeyValuePair<string, Rect>> packageRects = groupNode.RepresentedPackageIds
                    .Where(packageId => !string.IsNullOrWhiteSpace(packageId) &&
                                        _nodeRects.ContainsKey(packageId))
                    .Select(packageId => new KeyValuePair<string, Rect>(packageId, _nodeRects[packageId]))
                    .ToList();

                if (packageRects.Count == 0)
                {
                    continue;
                }

                routes.Add(CreateStructuralMembershipRoute(groupNode.GroupId, groupRect, groupHubRect, packageRects));
            }

            return routes;
        }

        private static PackageGraphStructuralMembershipRoute CreateStructuralMembershipRoute(
            string groupId,
            Rect groupRect,
            Rect groupHubRect,
            IReadOnlyList<KeyValuePair<string, Rect>> packageRects)
        {
            Vector2 packageAverage = CalculateAverageRectCenter(packageRects.Select(pair => pair.Value));
            Vector2 direction = packageAverage - groupHubRect.center;
            bool horizontalBus = Mathf.Abs(direction.y) >= Mathf.Abs(direction.x);
            List<PackageGraphStructuralMembershipSegment> segments = new List<PackageGraphStructuralMembershipSegment>();
            string[] packageIds = packageRects.Select(pair => pair.Key).ToArray();

            if (packageRects.Count == 1)
            {
                AddDirectStructuralSegments(segments, groupRect, groupHubRect, packageRects[0].Value);
                return new PackageGraphStructuralMembershipRoute(groupId, packageIds, segments, usesBus: false);
            }

            if (horizontalBus)
            {
                KeyValuePair<string, Rect>[] ordered = packageRects
                    .OrderBy(pair => pair.Value.center.x)
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                float busY = GetBusAxis(groupHubRect.center.y, packageAverage.y);
                float busXMin = ordered.Min(pair => pair.Value.center.x);
                float busXMax = ordered.Max(pair => pair.Value.center.x);
                AddCategoryToHorizontalBusSegments(segments, groupRect, groupHubRect, busY);
                segments.Add(new PackageGraphStructuralMembershipSegment(
                    new Vector2(busXMin, busY),
                    new Vector2(busXMax, busY)));

                foreach (KeyValuePair<string, Rect> package in ordered)
                {
                    Vector2 branch = new Vector2(package.Value.center.x, busY);
                    Vector2 endpoint = GetRectBorderPoint(package.Value, branch, 2f);
                    segments.Add(new PackageGraphStructuralMembershipSegment(branch, endpoint));
                }

                return new PackageGraphStructuralMembershipRoute(groupId, packageIds, segments, usesBus: true);
            }

            KeyValuePair<string, Rect>[] verticalOrdered = packageRects
                .OrderBy(pair => pair.Value.center.y)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            float busX = GetBusAxis(groupHubRect.center.x, packageAverage.x);
            float busYMin = verticalOrdered.Min(pair => pair.Value.center.y);
            float busYMax = verticalOrdered.Max(pair => pair.Value.center.y);
            Vector2 hubStart = GetHubBorderPoint(groupHubRect, new Vector2(busX, groupHubRect.center.y), 2f);
            Vector2 trunk = new Vector2(busX, groupHubRect.center.y);
            segments.Add(new PackageGraphStructuralMembershipSegment(hubStart, trunk));
            segments.Add(new PackageGraphStructuralMembershipSegment(
                new Vector2(busX, busYMin),
                new Vector2(busX, busYMax)));

            foreach (KeyValuePair<string, Rect> package in verticalOrdered)
            {
                Vector2 branch = new Vector2(busX, package.Value.center.y);
                Vector2 endpoint = GetRectBorderPoint(package.Value, branch, 2f);
                segments.Add(new PackageGraphStructuralMembershipSegment(branch, endpoint));
            }

            return new PackageGraphStructuralMembershipRoute(groupId, packageIds, segments, usesBus: true);
        }

        private static void AddDirectStructuralSegments(
            ICollection<PackageGraphStructuralMembershipSegment> segments,
            Rect groupRect,
            Rect groupHubRect,
            Rect packageRect)
        {
            Vector2 direction = packageRect.center - groupHubRect.center;

            if (Mathf.Abs(direction.y) > Mathf.Abs(direction.x) &&
                direction.y > 0f)
            {
                float sideX = groupRect.xMax + 22f;
                Vector2 start = GetHubBorderPoint(groupHubRect, new Vector2(sideX, groupHubRect.center.y), 2f);
                Vector2 side = new Vector2(sideX, groupHubRect.center.y);
                Vector2 turn = new Vector2(sideX, packageRect.center.y);
                Vector2 end = GetRectBorderPoint(packageRect, turn, 2f);
                segments.Add(new PackageGraphStructuralMembershipSegment(start, side));
                segments.Add(new PackageGraphStructuralMembershipSegment(side, turn));
                segments.Add(new PackageGraphStructuralMembershipSegment(turn, end));
                return;
            }

            Vector2 from = GetHubBorderPoint(groupHubRect, packageRect.center, 2f);
            Vector2 to = GetRectBorderPoint(packageRect, groupHubRect.center, 2f);
            segments.Add(new PackageGraphStructuralMembershipSegment(from, to));
        }

        private static void AddCategoryToHorizontalBusSegments(
            ICollection<PackageGraphStructuralMembershipSegment> segments,
            Rect groupRect,
            Rect groupHubRect,
            float busY)
        {
            if (busY > groupHubRect.center.y)
            {
                float sideX = groupRect.xMax + 22f;
                Vector2 start = GetHubBorderPoint(groupHubRect, new Vector2(sideX, groupHubRect.center.y), 2f);
                Vector2 side = new Vector2(sideX, groupHubRect.center.y);
                Vector2 down = new Vector2(sideX, busY);
                Vector2 bus = new Vector2(groupHubRect.center.x, busY);
                segments.Add(new PackageGraphStructuralMembershipSegment(start, side));
                segments.Add(new PackageGraphStructuralMembershipSegment(side, down));
                segments.Add(new PackageGraphStructuralMembershipSegment(down, bus));
                return;
            }

            Vector2 startDirect = GetHubBorderPoint(groupHubRect, new Vector2(groupHubRect.center.x, busY), 2f);
            segments.Add(new PackageGraphStructuralMembershipSegment(
                startDirect,
                new Vector2(groupHubRect.center.x, busY)));
        }

        private static float GetBusAxis(float categoryAxis, float packageAxis)
        {
            return Mathf.Lerp(categoryAxis, packageAxis, 0.52f);
        }

        private static Vector2 CalculateAverageRectCenter(IEnumerable<Rect> rects)
        {
            Vector2 total = Vector2.zero;
            int count = 0;

            foreach (Rect rect in rects ?? Array.Empty<Rect>())
            {
                total += rect.center;
                count++;
            }

            return count == 0 ? Vector2.zero : total / count;
        }

        private static Vector2 GetHubBorderPoint(Rect hubRect, Vector2 externalPoint, float padding)
        {
            Vector2 center = hubRect.center;
            Vector2 direction = externalPoint - center;

            if (direction.sqrMagnitude <= 0.01f)
            {
                return center;
            }

            return center + direction.normalized *
                   (Mathf.Min(hubRect.width, hubRect.height) * 0.5f + Mathf.Max(0f, padding));
        }

        private static void DrawStructuralMembershipRoute(
            PackageGraphPainter painter,
            PackageGraphStructuralMembershipRoute route,
            bool emphasized,
            bool muted)
        {
            Color color = emphasized
                ? new Color(0.48f, 0.80f, 0.84f, 0.26f)
                : muted
                    ? new Color(0.38f, 0.55f, 0.64f, 0.024f)
                    : new Color(0.38f, 0.62f, 0.70f, 0.078f);
            painter.strokeColor = color;
            painter.lineWidth = emphasized ? 1.15f : 0.72f;

            foreach (PackageGraphStructuralMembershipSegment segment in route.Segments)
            {
                painter.BeginPath();
                painter.MoveTo(segment.From);
                painter.LineTo(segment.To);
                painter.Stroke();
            }
        }

        private IEnumerable<Rect> GetDirectChildRects(
            string groupId,
            IReadOnlyDictionary<string, PackageGraphGroupLayoutNode> groupNodeById)
        {
            foreach (PackageGraphNode node in _graph.Nodes)
            {
                if (node == null ||
                    !string.Equals(node.GroupId, groupId, StringComparison.OrdinalIgnoreCase) ||
                    !_nodeRects.TryGetValue(node.PackageId, out Rect rect))
                {
                    continue;
                }

                yield return rect;
            }

            foreach (PackageGraphGroup group in _graph.Groups)
            {
                if (group == null ||
                    !string.Equals(group.ParentGroupId, groupId, StringComparison.OrdinalIgnoreCase) ||
                    !groupNodeById.TryGetValue(group.Id, out PackageGraphGroupLayoutNode groupNode))
                {
                    continue;
                }

                yield return GetAnimatedGroupHubRect(groupNode, GetGroupRect(groupNode));
            }
        }

        private Rect GetGroupRect(PackageGraphGroupLayoutNode groupNode)
        {
            return groupNode != null &&
                   _groupRects.TryGetValue(groupNode.GroupId, out Rect rect)
                ? rect
                : groupNode != null
                    ? groupNode.Rect
                    : default(Rect);
        }

        private Rect GetAnimatedGroupHubRect(PackageGraphGroupLayoutNode groupNode, Rect groupRect)
        {
            if (groupNode != null &&
                _groupCenters.TryGetValue(groupNode.GroupId, out Vector2 center))
            {
                return CenterRectOn(groupNode.HubRect, center);
            }

            return GetGroupHubRect(groupNode, groupRect);
        }

        private static Rect GetGroupHubRect(PackageGraphGroupLayoutNode groupNode, Rect groupRect)
        {
            if (groupNode == null)
            {
                return groupRect;
            }

            Vector2 hubSize = groupNode.HubRect.size;
            Vector2 hubOffset = groupNode.HubRect.position - groupNode.Rect.position;
            return new Rect(
                groupRect.x + hubOffset.x,
                groupRect.y + hubOffset.y,
                hubSize.x,
                hubSize.y);
        }

        private static Rect CenterRectOn(Rect rect, Vector2 center)
        {
            return new Rect(
                center.x - rect.width * 0.5f,
                center.y - rect.height * 0.5f,
                rect.width,
                rect.height);
        }

        private float GetGroupOrbitRadius(PackageGraphGroupLayoutNode groupNode)
        {
            return groupNode != null &&
                   _groupOrbitRadii.TryGetValue(groupNode.GroupId, out float radius)
                ? Mathf.Max(0f, radius)
                : groupNode != null
                    ? groupNode.OrbitRadius
                    : 0f;
        }

        private static void DrawOrbitCircle(PackageGraphPainter painter, PackageGraphOrbitVisualState orbit)
        {
            if (!orbit.Visible || orbit.Radius <= 0.01f)
            {
                return;
            }

            painter.fillColor = new Color(0.20f, 0.54f, 0.62f, orbit.FillOpacity);
            painter.strokeColor = new Color(0.42f, 0.70f, 0.78f, orbit.StrokeOpacity);
            painter.lineWidth = orbit.Emphasized ? 1.25f : 0.85f;
            DrawCircle(painter, orbit.Center, orbit.Radius);
        }

        private static void DrawStatusRing(PackageGraphPainter painter, CategoryStatusRingVisualState ring)
        {
            if (ring.Radius <= 0.01f || ring.Thickness <= 0.01f)
            {
                return;
            }

            foreach (CategoryStatusRingSegment segment in PackageGraphCategoryStatusVisuals.CreateRingSegments(
                         ring.Slices,
                         ring.TotalCount))
            {
                Color color = segment.Color;
                color.a = Mathf.Clamp01(color.a + (ring.HoverActive ? 0.12f : 0f));

                if (segment.FullRing)
                {
                    DrawFullStatusRing(painter, ring.Center, ring.Radius, ring.Thickness, color);
                }
                else
                {
                    DrawRingSegment(
                        painter,
                        ring.Center,
                        ring.Radius,
                        ring.Thickness,
                        segment.StartDegrees,
                        segment.StartDegrees + segment.SweepDegrees,
                        color);
                }
            }
        }

        private static void DrawFullStatusRing(
            PackageGraphPainter painter,
            Vector2 center,
            float radius,
            float thickness,
            Color color)
        {
            float strokeRadius = Mathf.Max(0.01f, radius - thickness * 0.5f);
            painter.fillColor = new Color(0f, 0f, 0f, 0f);
            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(0.01f, thickness);
            DrawCircleStroke(painter, center, strokeRadius);
        }

        private static void DrawRingSegment(
            PackageGraphPainter painter,
            Vector2 center,
            float radius,
            float thickness,
            float startDegrees,
            float endDegrees,
            Color color)
        {
            float sweep = Mathf.Max(0f, endDegrees - startDegrees);

            if (sweep <= 0.01f)
            {
                return;
            }

            float strokeRadius = Mathf.Max(0.01f, radius - thickness * 0.5f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(sweep / 8f), 4, 48);
            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(0.01f, thickness);
            painter.BeginPath();

            for (int index = 0; index <= steps; index++)
            {
                float angle = Mathf.Deg2Rad * Mathf.Lerp(startDegrees, endDegrees, index / (float)steps);
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * strokeRadius;

                if (index == 0)
                {
                    painter.MoveTo(point);
                }
                else
                {
                    painter.LineTo(point);
                }
            }

            painter.Stroke();
        }

        private static void DrawSpoke(PackageGraphPainter painter, Rect fromHubRect, Rect toRect, bool emphasized, bool muted)
        {
            Vector2 fromCenter = fromHubRect.center;
            Vector2 toCenter = toRect.center;
            Vector2 direction = toCenter - fromCenter;

            if (direction.sqrMagnitude <= 0.01f)
            {
                return;
            }

            Vector2 from = fromCenter + direction.normalized * (Mathf.Min(fromHubRect.width, fromHubRect.height) * 0.5f + 2f);
            Vector2 to = GetRectBorderPoint(toRect, fromCenter, 2f);
            Color color = emphasized
                ? new Color(0.48f, 0.80f, 0.84f, 0.24f)
                : muted
                    ? new Color(0.38f, 0.55f, 0.64f, 0.025f)
                    : new Color(0.38f, 0.62f, 0.70f, 0.085f);
            painter.strokeColor = color;
            painter.lineWidth = emphasized ? 1.2f : 0.75f;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        private static Vector2 GetRectBorderPoint(Rect rect, Vector2 externalPoint, float padding)
        {
            Vector2 center = rect.center;
            Vector2 direction = center - externalPoint;

            if (direction.sqrMagnitude <= 0.01f)
            {
                return center;
            }

            float halfWidth = Mathf.Max(0.01f, rect.width * 0.5f);
            float halfHeight = Mathf.Max(0.01f, rect.height * 0.5f);
            Vector2 normalized = direction.normalized;
            float scaleX = Mathf.Abs(normalized.x) > 0.001f ? halfWidth / Mathf.Abs(normalized.x) : float.PositiveInfinity;
            float scaleY = Mathf.Abs(normalized.y) > 0.001f ? halfHeight / Mathf.Abs(normalized.y) : float.PositiveInfinity;
            float distance = Mathf.Max(0f, Mathf.Min(scaleX, scaleY) - padding);
            return center - normalized * distance;
        }

        private static void DrawCircle(PackageGraphPainter painter, Vector2 center, float radius)
        {
            DrawCirclePath(painter, center, radius);
            painter.Fill();
            painter.Stroke();
        }

        private static void DrawCircleStroke(PackageGraphPainter painter, Vector2 center, float radius)
        {
            DrawCirclePath(painter, center, radius);
            painter.Stroke();
        }

        private static void DrawCirclePath(PackageGraphPainter painter, Vector2 center, float radius)
        {
            const float Kappa = 0.55228475f;
            float safeRadius = Mathf.Max(0f, radius);
            float offset = safeRadius * Kappa;
            float left = center.x - safeRadius;
            float right = center.x + safeRadius;
            float top = center.y - safeRadius;
            float bottom = center.y + safeRadius;

            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x, top));
            painter.BezierCurveTo(
                new Vector2(center.x + offset, top),
                new Vector2(right, center.y - offset),
                new Vector2(right, center.y));
            painter.BezierCurveTo(
                new Vector2(right, center.y + offset),
                new Vector2(center.x + offset, bottom),
                new Vector2(center.x, bottom));
            painter.BezierCurveTo(
                new Vector2(center.x - offset, bottom),
                new Vector2(left, center.y + offset),
                new Vector2(left, center.y));
            painter.BezierCurveTo(
                new Vector2(left, center.y - offset),
                new Vector2(center.x - offset, top),
                new Vector2(center.x, top));
            painter.ClosePath();
        }

        private static bool IsGroupInHoverContext(
            string groupId,
            string activeGroupId,
            IReadOnlyDictionary<string, PackageGraphGroupLayoutNode> groupNodeById)
        {
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(activeGroupId))
            {
                return false;
            }

            if (string.Equals(groupId, activeGroupId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string currentGroupId = groupId;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   groupNodeById != null &&
                   groupNodeById.TryGetValue(currentGroupId, out PackageGraphGroupLayoutNode groupNode) &&
                   groupNode.Group != null)
            {
                if (string.Equals(groupNode.Group.ParentGroupId, activeGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentGroupId = groupNode.Group.ParentGroupId;
            }

            currentGroupId = activeGroupId;
            visited.Clear();

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   groupNodeById != null &&
                   groupNodeById.TryGetValue(currentGroupId, out PackageGraphGroupLayoutNode groupNode) &&
                   groupNode.Group != null)
            {
                if (string.Equals(groupNode.Group.ParentGroupId, groupId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentGroupId = groupNode.Group.ParentGroupId;
            }

            return false;
        }
    }

    internal enum PackageGraphEdgeRoutePort
    {
        Auto,
        Left,
        Right,
        Top,
        Bottom
    }

    internal enum PackageGraphEdgeRouteZone
    {
        Direct,
        Providers,
        Dependents,
        Integrations,
        CompanionsAndSuites
    }

    internal enum PackageGraphRouteKind
    {
        StructuralMembership,
        Dependency,
        Integration,
        OptionalCompanion,
        SuiteMembership,
        CompositeDependencyIntegration
    }

    [Flags]
    internal enum PackageGraphConnectionSemantics
    {
        None = 0,
        Dependency = 1 << 0,
        Integration = 1 << 1,
        OptionalCompanion = 1 << 2,
        SuiteMembership = 1 << 3,
        Recommended = 1 << 4
    }

    internal readonly struct PackageGraphConnectionBundle
    {
        public PackageGraphConnectionBundle(
            string sourcePackageId,
            string targetPackageId,
            IReadOnlyList<PackageGraphEdge> edges)
        {
            SourcePackageId = sourcePackageId ?? string.Empty;
            TargetPackageId = targetPackageId ?? string.Empty;
            Edges = edges == null
                ? Array.Empty<PackageGraphEdge>()
                : edges
                    .Where(edge => edge != null)
                    .OrderBy(edge => GetSemanticPriority(edge.Kind))
                    .ThenBy(edge => edge.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            PrimaryEdge = Edges.Count > 0
                ? Edges[0]
                : new PackageGraphEdge(SourcePackageId, TargetPackageId, PackageGraphEdgeKind.HardDependency, PackageGraphEdgeState.Active, string.Empty);
            Semantics = BuildSemantics(Edges);
            Key = SourcePackageId + ">" + TargetPackageId + ":" + Semantics;
        }

        public string SourcePackageId { get; }

        public string TargetPackageId { get; }

        public IReadOnlyList<PackageGraphEdge> Edges { get; }

        public PackageGraphEdge PrimaryEdge { get; }

        public PackageGraphConnectionSemantics Semantics { get; }

        public string Key { get; }

        public bool HasDependency => HasSemantic(PackageGraphConnectionSemantics.Dependency);

        public bool HasIntegration => HasSemantic(PackageGraphConnectionSemantics.Integration);

        public bool HasOptionalCompanion => HasSemantic(PackageGraphConnectionSemantics.OptionalCompanion);

        public bool HasSuiteMembership => HasSemantic(PackageGraphConnectionSemantics.SuiteMembership);

        public bool HasRecommended => HasSemantic(PackageGraphConnectionSemantics.Recommended);

        public bool IsCompositeDependencyIntegration => HasDependency && HasIntegration;

        public bool ConnectsPackage(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) &&
                   (string.Equals(SourcePackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(TargetPackageId, packageId, StringComparison.OrdinalIgnoreCase));
        }

        public string GetOtherPackageId(string packageId)
        {
            if (string.Equals(SourcePackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                return TargetPackageId;
            }

            return string.Equals(TargetPackageId, packageId, StringComparison.OrdinalIgnoreCase)
                ? SourcePackageId
                : string.Empty;
        }

        public bool HasKind(PackageGraphEdgeKind kind)
        {
            return Edges.Any(edge => edge.Kind == kind);
        }

        public PackageGraphEdge GetEdge(PackageGraphEdgeKind kind)
        {
            return Edges.FirstOrDefault(edge => edge.Kind == kind);
        }

        public bool HasSemantic(PackageGraphConnectionSemantics semantic)
        {
            return (Semantics & semantic) == semantic;
        }

        private static PackageGraphConnectionSemantics BuildSemantics(IEnumerable<PackageGraphEdge> edges)
        {
            PackageGraphConnectionSemantics semantics = PackageGraphConnectionSemantics.None;

            foreach (PackageGraphEdge edge in edges ?? Array.Empty<PackageGraphEdge>())
            {
                switch (edge.Kind)
                {
                    case PackageGraphEdgeKind.HardDependency:
                        semantics |= PackageGraphConnectionSemantics.Dependency;
                        break;
                    case PackageGraphEdgeKind.IntegrationConnection:
                        semantics |= PackageGraphConnectionSemantics.Integration;
                        break;
                    case PackageGraphEdgeKind.OptionalCompanion:
                        semantics |= PackageGraphConnectionSemantics.OptionalCompanion;
                        break;
                    case PackageGraphEdgeKind.SuiteMembership:
                        semantics |= PackageGraphConnectionSemantics.SuiteMembership;
                        break;
                    case PackageGraphEdgeKind.Recommended:
                        semantics |= PackageGraphConnectionSemantics.Recommended;
                        break;
                }
            }

            return semantics;
        }

        internal static int GetSemanticPriority(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                    return 0;
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 1;
                case PackageGraphEdgeKind.OptionalCompanion:
                    return 2;
                case PackageGraphEdgeKind.SuiteMembership:
                    return 3;
                case PackageGraphEdgeKind.Recommended:
                    return 4;
                default:
                    return 10;
            }
        }
    }

    internal readonly struct PackageGraphEdgeRoute
    {
        public PackageGraphEdgeRoute(
            PackageGraphEdge edge,
            PackageGraphEdgeRoutePort sourcePort,
            PackageGraphEdgeRoutePort targetPort,
            PackageGraphEdgeRouteZone zone,
            string sharedTrunkId,
            int branchIndex,
            int branchCount,
            IReadOnlyList<Vector2> points)
            : this(
                new PackageGraphConnectionBundle(
                    edge != null ? edge.FromPackageId : string.Empty,
                    edge != null ? edge.ToPackageId : string.Empty,
                    edge != null ? new[] { edge } : Array.Empty<PackageGraphEdge>()),
                sourcePort,
                targetPort,
                zone,
                sharedTrunkId,
                branchIndex,
                branchCount,
                points)
        {
        }

        public PackageGraphEdgeRoute(
            PackageGraphConnectionBundle bundle,
            PackageGraphEdgeRoutePort sourcePort,
            PackageGraphEdgeRoutePort targetPort,
            PackageGraphEdgeRouteZone zone,
            string sharedTrunkId,
            int branchIndex,
            int branchCount,
            IReadOnlyList<Vector2> points)
        {
            Bundle = bundle;
            Edge = bundle.PrimaryEdge;
            SourcePort = sourcePort;
            TargetPort = targetPort;
            Zone = zone;
            SharedTrunkId = sharedTrunkId ?? string.Empty;
            BranchIndex = Mathf.Max(0, branchIndex);
            BranchCount = Mathf.Max(1, branchCount);
            Points = points ?? Array.Empty<Vector2>();
            RouteKind = ResolveRouteKind(bundle);
        }

        public PackageGraphEdge Edge { get; }

        public PackageGraphConnectionBundle Bundle { get; }

        public PackageGraphEdgeRoutePort SourcePort { get; }

        public PackageGraphEdgeRoutePort TargetPort { get; }

        public PackageGraphEdgeRouteZone Zone { get; }

        public string SharedTrunkId { get; }

        public int BranchIndex { get; }

        public int BranchCount { get; }

        public IReadOnlyList<Vector2> Points { get; }

        public PackageGraphRouteKind RouteKind { get; }

        public bool UsesSharedTrunk => BranchCount > 1 && !string.IsNullOrWhiteSpace(SharedTrunkId);

        public bool HasSemantic(PackageGraphConnectionSemantics semantic)
        {
            return Bundle.HasSemantic(semantic);
        }

        public bool HasKind(PackageGraphEdgeKind kind)
        {
            return Bundle.HasKind(kind);
        }

        private static PackageGraphRouteKind ResolveRouteKind(PackageGraphConnectionBundle bundle)
        {
            if (bundle.IsCompositeDependencyIntegration)
            {
                return PackageGraphRouteKind.CompositeDependencyIntegration;
            }

            if (bundle.HasDependency)
            {
                return PackageGraphRouteKind.Dependency;
            }

            if (bundle.HasIntegration)
            {
                return PackageGraphRouteKind.Integration;
            }

            if (bundle.HasOptionalCompanion || bundle.HasRecommended)
            {
                return PackageGraphRouteKind.OptionalCompanion;
            }

            return bundle.HasSuiteMembership
                ? PackageGraphRouteKind.SuiteMembership
                : PackageGraphRouteKind.Dependency;
        }
    }

    internal struct PackageGraphEdgeRouteBuildDiagnostics
    {
        public int RouteCount;
        public int RouteCacheHits;
        public int RouteCacheMisses;
        public int RouteCacheNoEntryMisses;
        public int RouteCacheLayoutMisses;
        public int RouteCacheEndpointMisses;
        public int RouteCacheFocusGraphMisses;
        public int RouteCacheStyleMisses;
        public long RouteCacheLookupTicks;
        public long RouteCalculationTicks;
        public long GeometryLayoutReadTicks;
        public long VisualElementReuseTicks;
        public long StyleClassUpdateTicks;
        public long PainterPassTicks;

        public void AddRouteCacheLookupTicks(long ticks)
        {
            RouteCacheLookupTicks += Math.Max(0L, ticks);
        }

        public void AddRouteCacheMiss(PackageGraphEdgeRouteCacheMissReason reason)
        {
            RouteCacheMisses++;

            switch (reason)
            {
                case PackageGraphEdgeRouteCacheMissReason.LayoutSignatureChanged:
                    RouteCacheLayoutMisses++;
                    break;
                case PackageGraphEdgeRouteCacheMissReason.EndpointGeometryChanged:
                    RouteCacheEndpointMisses++;
                    break;
                case PackageGraphEdgeRouteCacheMissReason.FocusGraphChanged:
                    RouteCacheFocusGraphMisses++;
                    break;
                case PackageGraphEdgeRouteCacheMissReason.RouteStyleOptionsChanged:
                    RouteCacheStyleMisses++;
                    break;
                default:
                    RouteCacheNoEntryMisses++;
                    break;
            }
        }

        public void AddRouteCalculationTicks(long ticks)
        {
            RouteCalculationTicks += Math.Max(0L, ticks);
        }

        public void AddGeometryLayoutReadTicks(long ticks)
        {
            GeometryLayoutReadTicks += Math.Max(0L, ticks);
        }

        public void AddVisualElementReuseTicks(long ticks)
        {
            VisualElementReuseTicks += Math.Max(0L, ticks);
        }

        public void AddStyleClassUpdateTicks(long ticks)
        {
            StyleClassUpdateTicks += Math.Max(0L, ticks);
        }

        public void AddPainterPassTicks(long ticks)
        {
            PainterPassTicks += Math.Max(0L, ticks);
        }
    }

    internal enum PackageGraphEdgeRouteCacheMissReason
    {
        None,
        NoExistingEntry,
        LayoutSignatureChanged,
        EndpointGeometryChanged,
        FocusGraphChanged,
        RouteStyleOptionsChanged
    }

    internal readonly struct PackageGraphEdgeRouteCacheKey
    {
        public PackageGraphEdgeRouteCacheKey(
            string identityKey,
            string focusGraphKey,
            string layoutSignature,
            string endpointGeometryKey,
            string routeStyleKey)
        {
            IdentityKey = identityKey ?? string.Empty;
            FocusGraphKey = focusGraphKey ?? string.Empty;
            LayoutSignature = layoutSignature ?? string.Empty;
            EndpointGeometryKey = endpointGeometryKey ?? string.Empty;
            RouteStyleKey = routeStyleKey ?? string.Empty;
            FullKey =
                IdentityKey +
                "|fg=" + FocusGraphKey +
                "|layout=" + LayoutSignature +
                "|endpoints=" + EndpointGeometryKey +
                "|style=" + RouteStyleKey;
        }

        public string FullKey { get; }

        public string IdentityKey { get; }

        public string FocusGraphKey { get; }

        public string LayoutSignature { get; }

        public string EndpointGeometryKey { get; }

        public string RouteStyleKey { get; }
    }

    internal sealed class PackageGraphEdgeRouteCache
    {
        private const int MaxCachedRoutes = 512;

        private readonly Dictionary<string, CachedPackageGraphEdgeRoute> _routes =
            new Dictionary<string, CachedPackageGraphEdgeRoute>(StringComparer.Ordinal);
        private readonly Dictionary<string, PackageGraphEdgeRouteCacheKey> _lastKeyByIdentity =
            new Dictionary<string, PackageGraphEdgeRouteCacheKey>(StringComparer.Ordinal);

        public int Count => _routes.Count;

        public bool TryGet(
            PackageGraphEdgeRouteCacheKey key,
            PackageGraphConnectionBundle bundle,
            out PackageGraphEdgeRoute route,
            out PackageGraphEdgeRouteCacheMissReason missReason)
        {
            if (!string.IsNullOrEmpty(key.FullKey) &&
                _routes.TryGetValue(key.FullKey, out CachedPackageGraphEdgeRoute cachedRoute))
            {
                route = cachedRoute.CreateRoute(bundle);
                missReason = PackageGraphEdgeRouteCacheMissReason.None;
                return true;
            }

            route = default(PackageGraphEdgeRoute);
            missReason = GetMissReason(key);
            return false;
        }

        public void Store(PackageGraphEdgeRouteCacheKey key, PackageGraphEdgeRoute route)
        {
            if (string.IsNullOrEmpty(key.FullKey) ||
                string.IsNullOrEmpty(key.IdentityKey) ||
                route.Points == null ||
                route.Points.Count < 2)
            {
                return;
            }

            // Cache invalidation is encoded in the stable key parts: focus graph, target layout,
            // endpoint geometry, and route style. Transient animation state intentionally stays out.
            if (_routes.Count >= MaxCachedRoutes)
            {
                _routes.Clear();
                _lastKeyByIdentity.Clear();
            }

            _routes[key.FullKey] = new CachedPackageGraphEdgeRoute(route);
            _lastKeyByIdentity[key.IdentityKey] = key;
        }

        private PackageGraphEdgeRouteCacheMissReason GetMissReason(PackageGraphEdgeRouteCacheKey key)
        {
            if (string.IsNullOrEmpty(key.IdentityKey) ||
                !_lastKeyByIdentity.TryGetValue(key.IdentityKey, out PackageGraphEdgeRouteCacheKey previousKey))
            {
                return PackageGraphEdgeRouteCacheMissReason.NoExistingEntry;
            }

            if (!string.Equals(previousKey.FocusGraphKey, key.FocusGraphKey, StringComparison.Ordinal))
            {
                return PackageGraphEdgeRouteCacheMissReason.FocusGraphChanged;
            }

            if (!string.Equals(previousKey.LayoutSignature, key.LayoutSignature, StringComparison.Ordinal))
            {
                return PackageGraphEdgeRouteCacheMissReason.LayoutSignatureChanged;
            }

            if (!string.Equals(previousKey.EndpointGeometryKey, key.EndpointGeometryKey, StringComparison.Ordinal))
            {
                return PackageGraphEdgeRouteCacheMissReason.EndpointGeometryChanged;
            }

            if (!string.Equals(previousKey.RouteStyleKey, key.RouteStyleKey, StringComparison.Ordinal))
            {
                return PackageGraphEdgeRouteCacheMissReason.RouteStyleOptionsChanged;
            }

            return PackageGraphEdgeRouteCacheMissReason.NoExistingEntry;
        }

        private readonly struct CachedPackageGraphEdgeRoute
        {
            private readonly PackageGraphEdgeRoutePort _sourcePort;
            private readonly PackageGraphEdgeRoutePort _targetPort;
            private readonly PackageGraphEdgeRouteZone _zone;
            private readonly string _sharedTrunkId;
            private readonly int _branchIndex;
            private readonly int _branchCount;
            private readonly Vector2[] _points;

            public CachedPackageGraphEdgeRoute(PackageGraphEdgeRoute route)
            {
                _sourcePort = route.SourcePort;
                _targetPort = route.TargetPort;
                _zone = route.Zone;
                _sharedTrunkId = route.SharedTrunkId;
                _branchIndex = route.BranchIndex;
                _branchCount = route.BranchCount;
                _points = route.Points.ToArray();
            }

            public PackageGraphEdgeRoute CreateRoute(PackageGraphConnectionBundle bundle)
            {
                return new PackageGraphEdgeRoute(
                    bundle,
                    _sourcePort,
                    _targetPort,
                    _zone,
                    _sharedTrunkId,
                    _branchIndex,
                    _branchCount,
                    _points);
            }
        }
    }

    internal sealed class PackageGraphEdgeLayer : VisualElement
    {
        private const float AnimationFrameMs = 40f;
        private const float AnimatedDashLength = 12f;
        private const float AnimatedDashGap = 12f;
        private const float EdgeEndpointPadding = 6f;
        private const float RouteObstacleMargin = 16f;
        private const float RouteSearchBoundsPadding = 520f;
        private const int MaxRouteChannelCount = 12;
        private const int MaxRouteCandidateCount = 320;
        private const float RouteBendPenalty = 32f;
        private const float RouteDetourPenalty = 18f;
        private const float MarkerTravelStart = 0.045f;
        private const float MarkerTravelEnd = 0.955f;

        private readonly Dictionary<string, Rect> _nodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _groupRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphFocus _focus = PackageGraphFocus.Create(null, string.Empty);
        private string _previewPackageId = string.Empty;
        private readonly PackageGraphEdgeRouteCache _routeCache = new PackageGraphEdgeRouteCache();
        private IReadOnlyList<PackageGraphEdgeRoute> _routes = Array.Empty<PackageGraphEdgeRoute>();
        private IVisualElementScheduledItem _animationItem;
        private float _animationPhase;
        private bool _animationEnabled;

        public PackageGraphEdgeLayer()
        {
            generateVisualContent += GenerateEdges;
            RegisterCallback<AttachToPanelEvent>(_ => UpdateAnimationSchedule());
            RegisterCallback<DetachFromPanelEvent>(_ => PauseAnimation());
        }

        public void SetGraph(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            float canvasHeight,
            PackageGraphFocus focus)
        {
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _focus = focus ?? PackageGraphFocus.Create(_graph, string.Empty);
            CopyNodeRects(nodeRects);
            CopyGroupRects(groupRects);
            long styleStartTicks = Stopwatch.GetTimestamp();
            style.height = canvasHeight;
            long styleTicks = Stopwatch.GetTimestamp() - styleStartTicks;
            PackageGraphEdgeRouteBuildDiagnostics diagnostics = RebuildRoutes();
            diagnostics.AddStyleClassUpdateTicks(styleTicks);
            long visualReuseStartTicks = Stopwatch.GetTimestamp();
            diagnostics.AddVisualElementReuseTicks(Stopwatch.GetTimestamp() - visualReuseStartTicks);
            _animationEnabled = HasAnimatedEdges();
            UpdateAnimationSchedule();
            MarkDirtyRepaint();
            PackageGraphOpenProfiler.Current?.AddEdgeRouteDiagnostics(diagnostics);
        }

        public void UpdateRects(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            CopyNodeRects(nodeRects);
            CopyGroupRects(groupRects);
            PackageGraphEdgeRouteBuildDiagnostics diagnostics = RebuildRoutes();
            MarkDirtyRepaint();
            PackageGraphOpenProfiler.Current?.AddEdgeRouteDiagnostics(diagnostics);
        }

        public void SetPreviewPackage(string packageId)
        {
            string nextPackageId = packageId ?? string.Empty;

            if (string.Equals(_previewPackageId, nextPackageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _previewPackageId = nextPackageId;
            MarkDirtyRepaint();
        }

        private void CopyNodeRects(IReadOnlyDictionary<string, Rect> nodeRects)
        {
            _nodeRects.Clear();

            if (nodeRects == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> nodeRect in nodeRects)
            {
                _nodeRects[nodeRect.Key] = nodeRect.Value;
            }
        }

        private bool HasAnimatedEdges()
        {
            if (_graph == null || _graph.Edges.Count == 0 || _focus == null || !_focus.HasFocus)
            {
                return false;
            }

            return _graph.Edges.Any(edge =>
                ShouldAnimate(edge.Kind) &&
                _focus.IsEdgeVisible(edge) &&
                _focus.IsEdgeEmphasized(edge));
        }

        private void UpdateAnimationSchedule()
        {
            if (_animationItem == null)
            {
                _animationItem = schedule.Execute(UpdateAnimation).Every((long)AnimationFrameMs);
            }

            if (_animationEnabled)
            {
                _animationItem.Resume();
            }
            else
            {
                PauseAnimation();
            }
        }

        private void PauseAnimation()
        {
            _animationItem?.Pause();
        }

        private void UpdateAnimation()
        {
            if (!_animationEnabled || !IsVisibleInPanel())
            {
                return;
            }

            _animationPhase = Mathf.Repeat((float)(EditorApplication.timeSinceStartup * 0.30d), 1f);
            MarkDirtyRepaint();
        }

        private bool IsVisibleInPanel()
        {
            if (panel == null)
            {
                return false;
            }

            VisualElement element = this;

            while (element != null)
            {
                if (element.resolvedStyle.display == DisplayStyle.None ||
                    element.resolvedStyle.visibility == Visibility.Hidden)
                {
                    return false;
                }

                element = element.parent;
            }

            return true;
        }

        private void GenerateEdges(MeshGenerationContext context)
        {
            if (_graph == null || _graph.Edges.Count == 0)
            {
                return;
            }

            long painterStartTicks = Stopwatch.GetTimestamp();
            PackageGraphPainter painter = PackageGraphPainterCompatibility.Create(context);

            foreach (PackageGraphEdgeRoute route in _routes)
            {
                DrawEdge(
                    painter,
                    route,
                    IsRouteEmphasized(route, _focus, _previewPackageId),
                    _focus.HasFocus,
                    _animationPhase);
            }

            PackageGraphPainterCompatibility.Complete(painter);

            PackageGraphOpenProfiler.Current?.AddEdgeRouteDiagnostics(
                new PackageGraphEdgeRouteBuildDiagnostics
                {
                    PainterPassTicks = Stopwatch.GetTimestamp() - painterStartTicks
                });
        }

        internal static bool IsRouteEmphasized(
            PackageGraphEdgeRoute route,
            PackageGraphFocus focus,
            string previewPackageId)
        {
            if (focus == null ||
                !route.Bundle.Edges.Any(edge => focus.IsEdgeEmphasized(edge)))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(previewPackageId) ||
                   route.Bundle.ConnectsPackage(previewPackageId);
        }

        private void CopyGroupRects(IReadOnlyDictionary<string, Rect> groupRects)
        {
            _groupRects.Clear();

            if (groupRects == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> groupRect in groupRects)
            {
                _groupRects[groupRect.Key] = groupRect.Value;
            }
        }

        private IReadOnlyList<PackageGraphEdgeRoute> BuildRoutes()
        {
            return _routes;
        }

        internal IReadOnlyList<PackageGraphEdgeRoute> BuildRoutesSnapshotForTests()
        {
            return BuildRoutes();
        }

        private PackageGraphEdgeRouteBuildDiagnostics RebuildRoutes()
        {
            PackageGraphEdgeRouteBuildDiagnostics diagnostics = new PackageGraphEdgeRouteBuildDiagnostics();

            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.EdgeCreation))
            {
                long geometryStartTicks = Stopwatch.GetTimestamp();
                string layoutSignature = BuildRouteLayoutSignature(_nodeRects, _groupRects);
                diagnostics.AddGeometryLayoutReadTicks(Stopwatch.GetTimestamp() - geometryStartTicks);

                _routes = BuildRoutes(
                    _graph,
                    _nodeRects,
                    _groupRects,
                    _focus,
                    _routeCache,
                    layoutSignature,
                    ref diagnostics);
            }

            PackageGraphOpenProfiler.Current?.SetRenderCounts(0, _routes.Count);
            diagnostics.RouteCount = _routes.Count;
            return diagnostics;
        }

        internal static IReadOnlyList<PackageGraphEdgeRoute> BuildRoutesForTests(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            PackageGraphFocus focus)
        {
            return BuildRoutes(graph, nodeRects, null, focus);
        }

        internal static IReadOnlyList<PackageGraphEdgeRoute> BuildRoutesForTests(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            PackageGraphFocus focus)
        {
            return BuildRoutes(graph, nodeRects, groupRects, focus);
        }

        internal static IReadOnlyList<PackageGraphEdgeRoute> BuildRoutesWithCacheForTests(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            PackageGraphFocus focus,
            PackageGraphEdgeRouteCache routeCache,
            out PackageGraphEdgeRouteBuildDiagnostics diagnostics)
        {
            diagnostics = new PackageGraphEdgeRouteBuildDiagnostics();
            long geometryStartTicks = Stopwatch.GetTimestamp();
            string layoutSignature = BuildRouteLayoutSignature(nodeRects, groupRects);
            diagnostics.AddGeometryLayoutReadTicks(Stopwatch.GetTimestamp() - geometryStartTicks);
            IReadOnlyList<PackageGraphEdgeRoute> routes = BuildRoutes(
                graph,
                nodeRects,
                groupRects,
                focus,
                routeCache,
                layoutSignature,
                ref diagnostics);
            diagnostics.RouteCount = routes.Count;
            return routes;
        }

        private static IReadOnlyList<PackageGraphEdgeRoute> BuildRoutes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            PackageGraphFocus focus)
        {
            PackageGraphEdgeRouteBuildDiagnostics diagnostics = new PackageGraphEdgeRouteBuildDiagnostics();
            return BuildRoutes(graph, nodeRects, groupRects, focus, null, string.Empty, ref diagnostics);
        }

        private static IReadOnlyList<PackageGraphEdgeRoute> BuildRoutes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            PackageGraphFocus focus,
            PackageGraphEdgeRouteCache routeCache,
            string layoutSignature,
            ref PackageGraphEdgeRouteBuildDiagnostics diagnostics)
        {
            if (graph == null || nodeRects == null || graph.Edges.Count == 0)
            {
                return Array.Empty<PackageGraphEdgeRoute>();
            }

            PackageGraphFocus safeFocus = focus ?? PackageGraphFocus.Create(graph, string.Empty);
            string focusGraphSignature = BuildRouteFocusGraphSignature(graph, safeFocus);
            long routeCalculationStartTicks = Stopwatch.GetTimestamp();
            List<PackageGraphEdge> visibleEdges = new List<PackageGraphEdge>();

            foreach (PackageGraphEdge edge in graph.Edges)
            {
                if (edge != null &&
                    safeFocus.IsEdgeVisible(edge) &&
                    nodeRects.ContainsKey(edge.FromPackageId) &&
                    nodeRects.ContainsKey(edge.ToPackageId))
                {
                    visibleEdges.Add(edge);
                }
            }

            if (visibleEdges.Count == 0)
            {
                diagnostics.AddRouteCalculationTicks(Stopwatch.GetTimestamp() - routeCalculationStartTicks);
                return Array.Empty<PackageGraphEdgeRoute>();
            }

            IReadOnlyList<PackageGraphRouteObstacle> obstacles = BuildRouteObstacles(nodeRects, groupRects);
            List<PackageGraphEdgeRouteContext> visibleBundles = BuildConnectionBundleContexts(
                graph,
                visibleEdges,
                nodeRects,
                safeFocus);
            List<PackageGraphEdgeRoute> routes = new List<PackageGraphEdgeRoute>(visibleBundles.Count);
            HashSet<string> routedEdgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            diagnostics.AddRouteCalculationTicks(Stopwatch.GetTimestamp() - routeCalculationStartTicks);

            if (safeFocus.HasFocus &&
                !string.IsNullOrWhiteSpace(safeFocus.FocusPackageId) &&
                nodeRects.TryGetValue(safeFocus.FocusPackageId, out Rect focusRect))
            {
                Dictionary<string, List<PackageGraphEdgeRouteContext>> fanOutGroups =
                    new Dictionary<string, List<PackageGraphEdgeRouteContext>>(StringComparer.OrdinalIgnoreCase);

                foreach (PackageGraphEdgeRouteContext context in visibleBundles)
                {
                    if (context.Zone == PackageGraphEdgeRouteZone.Direct)
                    {
                        continue;
                    }

                    string groupKey = GetRouteGroupKey(context.Bundle, context.Zone, safeFocus.FocusPackageId);

                    if (!fanOutGroups.TryGetValue(groupKey, out List<PackageGraphEdgeRouteContext> contexts))
                    {
                        contexts = new List<PackageGraphEdgeRouteContext>();
                        fanOutGroups[groupKey] = contexts;
                    }

                    contexts.Add(context);
                }

                foreach (KeyValuePair<string, List<PackageGraphEdgeRouteContext>> group in fanOutGroups)
                {
                    List<PackageGraphEdgeRouteContext> contexts = group.Value;
                    if (contexts.Count > 1)
                    {
                        contexts.Sort((first, second) => CompareRouteContexts(first, second, graph, safeFocus.FocusPackageId));

                        for (int index = 0; index < contexts.Count; index++)
                        {
                            routes.Add(CreateFanOutRoute(
                                contexts[index],
                                focusRect,
                                safeFocus.FocusPackageId,
                                group.Key,
                                index,
                                contexts.Count,
                                nodeRects,
                                groupRects,
                                obstacles,
                                routeCache,
                                layoutSignature,
                                focusGraphSignature,
                                ref diagnostics));
                            routedEdgeKeys.Add(contexts[index].Bundle.Key);
                        }
                    }
                }
            }

            foreach (PackageGraphEdgeRouteContext context in visibleBundles)
            {
                if (routedEdgeKeys.Contains(context.Bundle.Key))
                {
                    continue;
                }

                routes.Add(CreateDirectRoute(
                    context,
                    safeFocus.FocusPackageId,
                    nodeRects,
                    groupRects,
                    obstacles,
                    routeCache,
                    layoutSignature,
                    focusGraphSignature,
                    ref diagnostics));
            }

            return routes;
        }

        private static List<PackageGraphEdgeRouteContext> BuildConnectionBundleContexts(
            PackageGraphModel graph,
            IReadOnlyList<PackageGraphEdge> visibleEdges,
            IReadOnlyDictionary<string, Rect> nodeRects,
            PackageGraphFocus focus)
        {
            List<PackageGraphEdgeRouteContext> contexts = new List<PackageGraphEdgeRouteContext>();
            Dictionary<string, List<PackageGraphEdge>> groupedEdges =
                new Dictionary<string, List<PackageGraphEdge>>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphEdge edge in visibleEdges)
            {
                string bundleKey = GetConnectionBundleKey(edge);

                if (!groupedEdges.TryGetValue(bundleKey, out List<PackageGraphEdge> edges))
                {
                    edges = new List<PackageGraphEdge>();
                    groupedEdges[bundleKey] = edges;
                }

                edges.Add(edge);
            }

            foreach (List<PackageGraphEdge> group in groupedEdges.Values)
            {
                PackageGraphConnectionBundle bundle = CreateConnectionBundle(group);

                if (!nodeRects.TryGetValue(bundle.SourcePackageId, out Rect fromRect) ||
                    !nodeRects.TryGetValue(bundle.TargetPackageId, out Rect toRect))
                {
                    continue;
                }

                PackageGraphEdgeRouteZone zone = focus != null &&
                                                 focus.HasFocus &&
                                                 bundle.ConnectsPackage(focus.FocusPackageId)
                    ? GetFocusRouteZone(graph, bundle, focus.FocusPackageId)
                    : PackageGraphEdgeRouteZone.Direct;
                contexts.Add(new PackageGraphEdgeRouteContext(bundle, fromRect, toRect, zone));
            }

            return contexts;
        }

        private static int CompareRouteContexts(
            PackageGraphEdgeRouteContext first,
            PackageGraphEdgeRouteContext second,
            PackageGraphModel graph,
            string focusPackageId)
        {
            int comparison = GetRouteSortValue(first, focusPackageId)
                .CompareTo(GetRouteSortValue(second, focusPackageId));

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = string.Compare(
                GetOtherNodeDisplayName(graph, first.Bundle, focusPackageId),
                GetOtherNodeDisplayName(graph, second.Bundle, focusPackageId),
                StringComparison.OrdinalIgnoreCase);

            return comparison != 0
                ? comparison
                : string.Compare(first.Bundle.Key, second.Bundle.Key, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetConnectionBundleKey(PackageGraphEdge edge)
        {
            if (edge == null)
            {
                return string.Empty;
            }

            if (edge.Kind != PackageGraphEdgeKind.HardDependency &&
                edge.Kind != PackageGraphEdgeKind.IntegrationConnection)
            {
                return edge.Key;
            }

            string first = edge.FromPackageId ?? string.Empty;
            string second = edge.ToPackageId ?? string.Empty;

            if (string.Compare(first, second, StringComparison.OrdinalIgnoreCase) > 0)
            {
                string temp = first;
                first = second;
                second = temp;
            }

            return "relationship:" + first + "<>" + second;
        }

        private static PackageGraphConnectionBundle CreateConnectionBundle(IEnumerable<PackageGraphEdge> edges)
        {
            PackageGraphEdge[] safeEdges = (edges ?? Array.Empty<PackageGraphEdge>())
                .Where(edge => edge != null)
                .OrderBy(edge => PackageGraphConnectionBundle.GetSemanticPriority(edge.Kind))
                .ThenBy(edge => edge.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PackageGraphEdge dependencyEdge = safeEdges.FirstOrDefault(edge => edge.Kind == PackageGraphEdgeKind.HardDependency);
            PackageGraphEdge primaryEdge = dependencyEdge ?? safeEdges.FirstOrDefault();

            return primaryEdge == null
                ? new PackageGraphConnectionBundle(string.Empty, string.Empty, Array.Empty<PackageGraphEdge>())
                : new PackageGraphConnectionBundle(primaryEdge.FromPackageId, primaryEdge.ToPackageId, safeEdges);
        }

        private static void DrawEdge(
            PackageGraphPainter painter,
            PackageGraphEdgeRoute route,
            bool emphasized,
            bool focusMode,
            float animationPhase)
        {
            if (route.Bundle.IsCompositeDependencyIntegration)
            {
                DrawCompositeDependencyIntegrationRoute(
                    painter,
                    route,
                    emphasized,
                    focusMode,
                    animationPhase);
                return;
            }

            PackageGraphEdge edge = route.Edge;
            Color color = GetEdgeColor(edge, emphasized, focusMode);
            float width = GetEdgeWidth(edge, emphasized, focusMode);
            bool animate = emphasized &&
                           focusMode &&
                           ShouldAnimate(edge.Kind);

            if (color.a <= 0.01f || width <= 0.01f || route.Points.Count < 2)
            {
                return;
            }

            DrawRouteUnderlay(painter, route.Points, edge.Kind, width, emphasized, focusMode);

            if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
            {
                DrawIntegrationCableRoute(
                    painter,
                    route.Points,
                    color,
                    width,
                    emphasized);
            }
            else if (IsDotted(edge.Kind))
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                DrawDashedPolyline(
                    painter,
                    route.Points,
                    2.5f,
                    7.5f,
                    animate ? (1f - animationPhase) * 10f : 0f);
            }
            else if (IsDashed(edge.Kind))
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                DrawDashedPolyline(
                    painter,
                    route.Points,
                    AnimatedDashLength,
                    AnimatedDashGap,
                    animate ? (1f - animationPhase) * (AnimatedDashLength + AnimatedDashGap) : 0f);
            }
            else
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                DrawPolylineStroke(painter, route.Points);
            }

            if (animate)
            {
                if (SupportsDirectionalFlowMarkers(edge.Kind))
                {
                    Color pulseColor = new Color(color.r, color.g, color.b, Mathf.Min(0.64f, color.a + 0.06f));
                    DrawFlowMarkers(
                        painter,
                        route,
                        edge.Kind,
                        pulseColor,
                        animationPhase);
                }
            }

            if (edge.State == PackageGraphEdgeState.Warning)
            {
                DrawWarningMarker(painter, GetPointOnRoute(route.Points, 0.5f, out _));
            }
        }

        private static void DrawCompositeDependencyIntegrationRoute(
            PackageGraphPainter painter,
            PackageGraphEdgeRoute route,
            bool emphasized,
            bool focusMode,
            float animationPhase)
        {
            PackageGraphEdge dependencyEdge = route.Bundle.GetEdge(PackageGraphEdgeKind.HardDependency) ?? route.Edge;
            PackageGraphEdge integrationEdge = route.Bundle.GetEdge(PackageGraphEdgeKind.IntegrationConnection) ?? route.Edge;
            Color cableColor = GetEdgeColor(integrationEdge, emphasized, focusMode);
            Color flowColor = GetEdgeColor(dependencyEdge, emphasized, focusMode);
            float cableWidth = Mathf.Max(0.9f, GetEdgeWidth(integrationEdge, emphasized, focusMode) * 0.82f);
            float flowWidth = Mathf.Max(1.1f, GetEdgeWidth(dependencyEdge, emphasized, focusMode) * 0.72f);

            if (route.Points.Count < 2 || (cableColor.a <= 0.01f && flowColor.a <= 0.01f))
            {
                return;
            }

            Color underlay = new Color(0.03f, 0.09f, 0.13f, emphasized ? 0.36f : 0.18f);
            painter.strokeColor = underlay;
            painter.lineWidth = Mathf.Max(2.4f, flowWidth + 1.4f);
            DrawPolylineStroke(painter, route.Points);

            DrawIntegrationCableRoute(
                painter,
                route.Points,
                cableColor,
                cableWidth,
                emphasized);

            painter.strokeColor = flowColor;
            painter.lineWidth = flowWidth;
            DrawPolylineStroke(painter, route.Points);

            bool animate = emphasized &&
                           focusMode &&
                           SupportsDirectionalFlowMarkers(PackageGraphEdgeKind.HardDependency);

            if (animate)
            {
                Color markerColor = new Color(
                    flowColor.r,
                    flowColor.g,
                    flowColor.b,
                    Mathf.Min(0.70f, flowColor.a + 0.06f));
                DrawFlowMarkers(
                    painter,
                    route,
                    PackageGraphEdgeKind.HardDependency,
                    markerColor,
                    animationPhase);
            }

            if (route.Bundle.Edges.Any(edge => edge.State == PackageGraphEdgeState.Warning))
            {
                DrawWarningMarker(painter, GetPointOnRoute(route.Points, 0.5f, out _));
            }
        }

        private readonly struct PackageGraphEdgeRouteContext
        {
            public PackageGraphEdgeRouteContext(
                PackageGraphConnectionBundle bundle,
                Rect fromRect,
                Rect toRect,
                PackageGraphEdgeRouteZone zone)
            {
                Bundle = bundle;
                FromRect = fromRect;
                ToRect = toRect;
                Zone = zone;
            }

            public PackageGraphConnectionBundle Bundle { get; }

            public PackageGraphEdge Edge => Bundle.PrimaryEdge;

            public Rect FromRect { get; }

            public Rect ToRect { get; }

            public PackageGraphEdgeRouteZone Zone { get; }
        }

        private readonly struct PackageGraphRouteObstacle
        {
            public PackageGraphRouteObstacle(string id, Rect rect)
            {
                Id = id ?? string.Empty;
                Rect = rect;
            }

            public string Id { get; }

            public Rect Rect { get; }
        }

        private static PackageGraphEdgeRoute CreateFanOutRoute(
            PackageGraphEdgeRouteContext context,
            Rect focusRect,
            string focusPackageId,
            string sharedTrunkId,
            int branchIndex,
            int branchCount,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphEdgeRouteCache routeCache,
            string layoutSignature,
            string focusGraphSignature,
            ref PackageGraphEdgeRouteBuildDiagnostics diagnostics)
        {
            bool focusIsSource = string.Equals(
                context.Bundle.SourcePackageId,
                focusPackageId,
                StringComparison.OrdinalIgnoreCase);
            Rect otherRect = focusIsSource ? context.ToRect : context.FromRect;
            PackageGraphEdgeRoutePort focusPort = GetFocusPort(context.Zone);
            PackageGraphEdgeRoutePort otherPort = GetRelatedPort(context.Zone);
            Vector2 focusPoint = GetPort(focusRect, focusPort, EdgeEndpointPadding);
            Vector2 otherPoint = GetPort(otherRect, otherPort, EdgeEndpointPadding);
            Vector2 trunkPoint = GetTrunkPoint(focusPoint, otherPoint, context.Zone);
            Vector2 branchPoint = GetBranchPoint(trunkPoint, otherPoint, context.Zone);
            Vector2[] selectedToOther =
            {
                focusPoint,
                trunkPoint,
                branchPoint,
                otherPoint
            };
            Vector2[] routePoints = focusIsSource
                ? selectedToOther
                : selectedToOther.Reverse().ToArray();

            return CreateValidatedRoute(
                context.Bundle,
                focusIsSource ? focusPort : otherPort,
                focusIsSource ? otherPort : focusPort,
                context.Zone,
                sharedTrunkId,
                branchIndex,
                branchCount,
                routePoints,
                obstacles,
                routeCache,
                layoutSignature,
                focusGraphSignature,
                ref diagnostics);
        }

        private static PackageGraphEdgeRoute CreateDirectRoute(
            PackageGraphEdgeRouteContext context,
            string focusPackageId,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphEdgeRouteCache routeCache,
            string layoutSignature,
            string focusGraphSignature,
            ref PackageGraphEdgeRouteBuildDiagnostics diagnostics)
        {
            PackageGraphEdgeRouteZone zone = context.Zone;
            bool focusIsSource = !string.IsNullOrWhiteSpace(focusPackageId) &&
                                 string.Equals(
                                     context.Bundle.SourcePackageId,
                                     focusPackageId,
                                     StringComparison.OrdinalIgnoreCase);
            bool focusIsTarget = !string.IsNullOrWhiteSpace(focusPackageId) &&
                                 string.Equals(
                                     context.Bundle.TargetPackageId,
                                     focusPackageId,
                                     StringComparison.OrdinalIgnoreCase);
            PackageGraphEdgeRoutePort fromPort = PackageGraphEdgeRoutePort.Auto;
            PackageGraphEdgeRoutePort toPort = PackageGraphEdgeRoutePort.Auto;

            if (zone != PackageGraphEdgeRouteZone.Direct && (focusIsSource || focusIsTarget))
            {
                PackageGraphEdgeRoutePort focusPort = GetFocusPort(zone);
                PackageGraphEdgeRoutePort otherPort = GetRelatedPort(zone);
                fromPort = focusIsSource ? focusPort : otherPort;
                toPort = focusIsSource ? otherPort : focusPort;
            }

            Vector2 from = GetPort(context.FromRect, fromPort, context.ToRect, EdgeEndpointPadding);
            Vector2 to = GetPort(context.ToRect, toPort, context.FromRect, EdgeEndpointPadding);

            return CreateValidatedRoute(
                context.Bundle,
                fromPort,
                toPort,
                zone,
                string.Empty,
                0,
                1,
                new[] { from, to },
                obstacles,
                routeCache,
                layoutSignature,
                focusGraphSignature,
                ref diagnostics);
        }

        private static PackageGraphEdgeRoute CreateValidatedRoute(
            PackageGraphConnectionBundle bundle,
            PackageGraphEdgeRoutePort sourcePort,
            PackageGraphEdgeRoutePort targetPort,
            PackageGraphEdgeRouteZone zone,
            string sharedTrunkId,
            int branchIndex,
            int branchCount,
            IReadOnlyList<Vector2> preferredPoints,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphEdgeRouteCache routeCache,
            string layoutSignature,
            string focusGraphSignature,
            ref PackageGraphEdgeRouteBuildDiagnostics diagnostics)
        {
            PackageGraphEdgeRouteCacheKey cacheKey = default(PackageGraphEdgeRouteCacheKey);

            if (routeCache != null)
            {
                cacheKey = CreateRouteCacheKey(
                    layoutSignature,
                    focusGraphSignature,
                    bundle,
                    sourcePort,
                    targetPort,
                    zone,
                    sharedTrunkId,
                    branchIndex,
                    branchCount,
                    preferredPoints);
                long lookupStartTicks = Stopwatch.GetTimestamp();

                if (routeCache.TryGet(
                    cacheKey,
                    bundle,
                    out PackageGraphEdgeRoute cachedRoute,
                    out PackageGraphEdgeRouteCacheMissReason missReason))
                {
                    diagnostics.RouteCacheHits++;
                    diagnostics.AddRouteCacheLookupTicks(Stopwatch.GetTimestamp() - lookupStartTicks);
                    return cachedRoute;
                }

                diagnostics.AddRouteCacheMiss(missReason);
                diagnostics.AddRouteCacheLookupTicks(Stopwatch.GetTimestamp() - lookupStartTicks);
            }

            long calculationStartTicks = Stopwatch.GetTimestamp();
            Vector2 from = preferredPoints != null && preferredPoints.Count > 0
                ? preferredPoints[0]
                : Vector2.zero;
            Vector2 to = preferredPoints != null && preferredPoints.Count > 0
                ? preferredPoints[preferredPoints.Count - 1]
                : Vector2.zero;
            IReadOnlyList<Vector2> routePoints = IsRoutePathValid(preferredPoints, obstacles, bundle)
                ? SimplifyRoutePoints(preferredPoints)
                : FindObstacleAwarePath(from, to, zone, obstacles, preferredPoints, bundle);

            PackageGraphEdgeRoute route = new PackageGraphEdgeRoute(
                bundle,
                sourcePort,
                targetPort,
                zone,
                sharedTrunkId,
                branchIndex,
                branchCount,
                routePoints);
            diagnostics.AddRouteCalculationTicks(Stopwatch.GetTimestamp() - calculationStartTicks);
            routeCache?.Store(cacheKey, route);
            return route;
        }

        private static string BuildRouteLayoutSignature(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            StringBuilder builder = new StringBuilder(512);
            AppendRectDictionarySignature(builder, "n", nodeRects);
            AppendRectDictionarySignature(builder, "g", groupRects);
            return builder.ToString();
        }

        private static void AppendRectDictionarySignature(
            StringBuilder builder,
            string prefix,
            IReadOnlyDictionary<string, Rect> rects)
        {
            if (rects == null || rects.Count == 0)
            {
                builder.Append(prefix);
                builder.Append(":0|");
                return;
            }

            List<string> keys = new List<string>(rects.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            builder.Append(prefix);
            builder.Append(':');
            builder.Append(keys.Count);
            builder.Append('|');

            foreach (string key in keys)
            {
                if (!rects.TryGetValue(key, out Rect rect))
                {
                    continue;
                }

                builder.Append(key);
                builder.Append('=');
                AppendRectKey(builder, rect);
                builder.Append(';');
            }
        }

        private static string BuildRouteFocusGraphSignature(
            PackageGraphModel graph,
            PackageGraphFocus focus)
        {
            StringBuilder builder = new StringBuilder(128);
            builder.Append("hasFocus=");
            builder.Append(focus != null && focus.HasFocus ? '1' : '0');
            builder.Append("|focus=");
            builder.Append(focus != null ? focus.FocusPackageId ?? string.Empty : string.Empty);
            builder.Append("|nodes=");
            builder.Append(graph != null ? graph.Nodes.Count : 0);
            builder.Append("|edges=");
            builder.Append(graph != null ? graph.Edges.Count : 0);
            builder.Append("|visibleEdges=");
            builder.Append(focus != null && focus.VisibleEdgeKeys != null ? focus.VisibleEdgeKeys.Count : 0);
            return builder.ToString();
        }

        private static PackageGraphEdgeRouteCacheKey CreateRouteCacheKey(
            string layoutSignature,
            string focusGraphSignature,
            PackageGraphConnectionBundle bundle,
            PackageGraphEdgeRoutePort sourcePort,
            PackageGraphEdgeRoutePort targetPort,
            PackageGraphEdgeRouteZone zone,
            string sharedTrunkId,
            int branchIndex,
            int branchCount,
            IReadOnlyList<Vector2> preferredPoints)
        {
            string identityKey = bundle.Key;
            StringBuilder styleBuilder = new StringBuilder(96);
            styleBuilder.Append("sp=");
            styleBuilder.Append((int)sourcePort);
            styleBuilder.Append("|tp=");
            styleBuilder.Append((int)targetPort);
            styleBuilder.Append("|z=");
            styleBuilder.Append((int)zone);
            styleBuilder.Append("|tr=");
            styleBuilder.Append(sharedTrunkId ?? string.Empty);
            styleBuilder.Append("|bi=");
            styleBuilder.Append(branchIndex);
            styleBuilder.Append("|bc=");
            styleBuilder.Append(branchCount);

            StringBuilder endpointBuilder = new StringBuilder(128);

            if (preferredPoints != null)
            {
                foreach (Vector2 point in preferredPoints)
                {
                    AppendPointKey(endpointBuilder, point);
                    endpointBuilder.Append(';');
                }
            }

            return new PackageGraphEdgeRouteCacheKey(
                identityKey,
                focusGraphSignature,
                layoutSignature,
                endpointBuilder.ToString(),
                styleBuilder.ToString());
        }

        private static void AppendRectKey(StringBuilder builder, Rect rect)
        {
            AppendRoundedFloat(builder, rect.x);
            builder.Append(',');
            AppendRoundedFloat(builder, rect.y);
            builder.Append(',');
            AppendRoundedFloat(builder, rect.width);
            builder.Append(',');
            AppendRoundedFloat(builder, rect.height);
        }

        private static void AppendPointKey(StringBuilder builder, Vector2 point)
        {
            AppendRoundedFloat(builder, point.x);
            builder.Append(',');
            AppendRoundedFloat(builder, point.y);
        }

        private static void AppendRoundedFloat(StringBuilder builder, float value)
        {
            builder.Append(Mathf.RoundToInt(value));
        }

        private static IReadOnlyList<PackageGraphRouteObstacle> BuildRouteObstacles(
            PackageGraphConnectionBundle bundle,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            return BuildRouteObstacles(nodeRects, groupRects)
                .Where(obstacle => !ShouldIgnoreObstacleForBundle(obstacle, bundle))
                .ToArray();
        }

        private static IReadOnlyList<PackageGraphRouteObstacle> BuildRouteObstacles(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            List<PackageGraphRouteObstacle> obstacles = new List<PackageGraphRouteObstacle>();

            if (nodeRects != null)
            {
                foreach (KeyValuePair<string, Rect> nodeRect in nodeRects)
                {
                    obstacles.Add(new PackageGraphRouteObstacle(
                        "package:" + nodeRect.Key,
                        InflateRect(nodeRect.Value, RouteObstacleMargin)));
                }
            }

            if (groupRects != null)
            {
                foreach (KeyValuePair<string, Rect> groupRect in groupRects)
                {
                    obstacles.Add(new PackageGraphRouteObstacle(
                        "category:" + groupRect.Key,
                        InflateRect(groupRect.Value, RouteObstacleMargin)));
                }
            }

            return obstacles;
        }

        private static bool ShouldIgnoreObstacleForBundle(
            PackageGraphRouteObstacle obstacle,
            PackageGraphConnectionBundle bundle)
        {
            if (string.IsNullOrWhiteSpace(obstacle.Id) ||
                !obstacle.Id.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string packageId = obstacle.Id.Substring("package:".Length);
            return string.Equals(packageId, bundle.SourcePackageId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(packageId, bundle.TargetPackageId, StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<Vector2> FindObstacleAwarePath(
            Vector2 from,
            Vector2 to,
            PackageGraphEdgeRouteZone zone,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            IReadOnlyList<Vector2> preferredPoints,
            PackageGraphConnectionBundle bundle)
        {
            List<IReadOnlyList<Vector2>> simpleCandidates = new List<IReadOnlyList<Vector2>>(4);
            AddRouteCandidate(simpleCandidates, preferredPoints);
            AddRouteCandidate(simpleCandidates, new[] { from, to });
            AddRouteCandidate(simpleCandidates, new[] { from, new Vector2(to.x, from.y), to });
            AddRouteCandidate(simpleCandidates, new[] { from, new Vector2(from.x, to.y), to });

            IReadOnlyList<Vector2> simpleRoute = FindBestValidRoute(simpleCandidates, obstacles, bundle, zone);

            if (simpleRoute != null)
            {
                return simpleRoute;
            }

            List<IReadOnlyList<Vector2>> candidates = new List<IReadOnlyList<Vector2>>(MaxRouteCandidateCount);
            AddRouteCandidate(candidates, preferredPoints);
            AddRouteCandidate(candidates, new[] { from, to });
            AddRouteCandidate(candidates, new[] { from, new Vector2(to.x, from.y), to });
            AddRouteCandidate(candidates, new[] { from, new Vector2(from.x, to.y), to });

            IReadOnlyList<PackageGraphRouteObstacle> channelObstacles = GetRelevantRouteObstacles(
                from,
                to,
                preferredPoints,
                obstacles,
                bundle);
            float[] xChannels = LimitRouteChannels(
                GetCandidateXChannels(from, to, channelObstacles, bundle),
                (from.x + to.x) * 0.5f);
            float[] yChannels = LimitRouteChannels(
                GetCandidateYChannels(from, to, channelObstacles, bundle),
                (from.y + to.y) * 0.5f);

            foreach (float x in xChannels)
            {
                if (candidates.Count >= MaxRouteCandidateCount)
                {
                    break;
                }

                AddRouteCandidate(candidates, new[]
                {
                    from,
                    new Vector2(x, from.y),
                    new Vector2(x, to.y),
                    to
                });
            }

            foreach (float y in yChannels)
            {
                if (candidates.Count >= MaxRouteCandidateCount)
                {
                    break;
                }

                AddRouteCandidate(candidates, new[]
                {
                    from,
                    new Vector2(from.x, y),
                    new Vector2(to.x, y),
                    to
                });
            }

            foreach (float x in xChannels)
            {
                foreach (float y in yChannels)
                {
                    if (candidates.Count >= MaxRouteCandidateCount)
                    {
                        break;
                    }

                    AddRouteCandidate(candidates, new[]
                    {
                        from,
                        new Vector2(x, from.y),
                        new Vector2(x, y),
                        new Vector2(to.x, y),
                        to
                    });
                    AddRouteCandidate(candidates, new[]
                    {
                        from,
                        new Vector2(from.x, y),
                        new Vector2(x, y),
                        new Vector2(x, to.y),
                        to
                    });
                }

                if (candidates.Count >= MaxRouteCandidateCount)
                {
                    break;
                }
            }

            IReadOnlyList<Vector2> cappedRoute = FindBestValidRoute(candidates, obstacles, bundle, zone);

            if (cappedRoute != null)
            {
                return cappedRoute;
            }

            float[] allXChannels = GetCandidateXChannels(from, to, obstacles, bundle);
            float[] allYChannels = GetCandidateYChannels(from, to, obstacles, bundle);

            if (allXChannels.Length != xChannels.Length || allYChannels.Length != yChannels.Length)
            {
                List<IReadOnlyList<Vector2>> fallbackCandidates = new List<IReadOnlyList<Vector2>>();

                foreach (float x in allXChannels)
                {
                    AddRouteCandidate(fallbackCandidates, new[]
                    {
                        from,
                        new Vector2(x, from.y),
                        new Vector2(x, to.y),
                        to
                    });
                }

                foreach (float y in allYChannels)
                {
                    AddRouteCandidate(fallbackCandidates, new[]
                    {
                        from,
                        new Vector2(from.x, y),
                        new Vector2(to.x, y),
                        to
                    });
                }

                foreach (float x in allXChannels)
                {
                    foreach (float y in allYChannels)
                    {
                        AddRouteCandidate(fallbackCandidates, new[]
                        {
                            from,
                            new Vector2(x, from.y),
                            new Vector2(x, y),
                            new Vector2(to.x, y),
                            to
                        });
                        AddRouteCandidate(fallbackCandidates, new[]
                        {
                            from,
                            new Vector2(from.x, y),
                            new Vector2(x, y),
                            new Vector2(x, to.y),
                            to
                        });
                    }
                }

                IReadOnlyList<Vector2> fallbackRoute = FindBestValidRoute(
                    fallbackCandidates,
                    obstacles,
                    bundle,
                    zone);

                if (fallbackRoute != null)
                {
                    return fallbackRoute;
                }
            }

            return SimplifyRoutePoints(preferredPoints ?? new[] { from, to });
        }

        private static IReadOnlyList<Vector2> FindBestValidRoute(
            IEnumerable<IReadOnlyList<Vector2>> candidates,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphConnectionBundle bundle,
            PackageGraphEdgeRouteZone zone)
        {
            if (candidates == null)
            {
                return null;
            }

            IReadOnlyList<Vector2> best = null;
            float bestScore = float.PositiveInfinity;

            foreach (IReadOnlyList<Vector2> candidate in candidates)
            {
                IReadOnlyList<Vector2> simplified = SimplifyRoutePoints(candidate);

                if (!IsRoutePathValid(simplified, obstacles, bundle))
                {
                    continue;
                }

                float score = ScoreRoutePath(simplified, zone);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = simplified;
                }
            }

            return best;
        }

        private static IReadOnlyList<PackageGraphRouteObstacle> GetRelevantRouteObstacles(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<Vector2> preferredPoints,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphConnectionBundle bundle)
        {
            if (obstacles == null || obstacles.Count == 0)
            {
                return Array.Empty<PackageGraphRouteObstacle>();
            }

            Rect routeBounds = BuildRouteSearchBounds(from, to, preferredPoints);
            List<PackageGraphRouteObstacle> relevant = new List<PackageGraphRouteObstacle>();

            foreach (PackageGraphRouteObstacle obstacle in obstacles)
            {
                if (ShouldIgnoreObstacleForBundle(obstacle, bundle) ||
                    !RectsOverlap(routeBounds, obstacle.Rect))
                {
                    continue;
                }

                relevant.Add(obstacle);
            }

            return relevant;
        }

        private static Rect BuildRouteSearchBounds(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<Vector2> preferredPoints)
        {
            float minX = Mathf.Min(from.x, to.x);
            float maxX = Mathf.Max(from.x, to.x);
            float minY = Mathf.Min(from.y, to.y);
            float maxY = Mathf.Max(from.y, to.y);

            if (preferredPoints != null)
            {
                foreach (Vector2 point in preferredPoints)
                {
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    minY = Mathf.Min(minY, point.y);
                    maxY = Mathf.Max(maxY, point.y);
                }
            }

            return Rect.MinMaxRect(
                minX - RouteSearchBoundsPadding,
                minY - RouteSearchBoundsPadding,
                maxX + RouteSearchBoundsPadding,
                maxY + RouteSearchBoundsPadding);
        }

        private static float[] LimitRouteChannels(float[] channels, float pivot)
        {
            if (channels == null || channels.Length <= MaxRouteChannelCount)
            {
                return channels ?? Array.Empty<float>();
            }

            List<float> limited = new List<float>(channels);
            limited.Sort((first, second) =>
            {
                int comparison = Mathf.Abs(first - pivot).CompareTo(Mathf.Abs(second - pivot));
                return comparison != 0 ? comparison : first.CompareTo(second);
            });
            limited.RemoveRange(MaxRouteChannelCount, limited.Count - MaxRouteChannelCount);
            limited.Sort();
            return limited.ToArray();
        }

        private static IEnumerable<float> GetCandidateXChannels(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles)
        {
            return GetCandidateXChannels(from, to, obstacles, default(PackageGraphConnectionBundle));
        }

        private static float[] GetCandidateXChannels(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphConnectionBundle bundle)
        {
            List<float> channels = new List<float>
            {
                (from.x + to.x) * 0.5f,
                from.x - 86f,
                from.x + 86f,
                from.x - 220f,
                from.x + 220f,
                to.x - 86f,
                to.x + 86f,
                to.x - 220f,
                to.x + 220f,
                Mathf.Min(from.x, to.x) - 520f,
                Mathf.Max(from.x, to.x) + 520f
            };
            float min = Mathf.Min(from.x, to.x) - 760f;
            float max = Mathf.Max(from.x, to.x) + 760f;

            foreach (PackageGraphRouteObstacle obstacle in obstacles ?? Array.Empty<PackageGraphRouteObstacle>())
            {
                if (ShouldIgnoreObstacleForBundle(obstacle, bundle))
                {
                    continue;
                }

                channels.Add(obstacle.Rect.xMin - RouteObstacleMargin);
                channels.Add(obstacle.Rect.xMax + RouteObstacleMargin);
            }

            channels.Sort();
            List<float> unique = new List<float>(channels.Count);

            foreach (float value in channels)
            {
                if (value < min || value > max)
                {
                    continue;
                }

                if (unique.Count == 0 || Mathf.Abs(unique[unique.Count - 1] - value) > 1f)
                {
                    unique.Add(value);
                }
            }

            return unique.ToArray();
        }

        private static IEnumerable<float> GetCandidateYChannels(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles)
        {
            return GetCandidateYChannels(from, to, obstacles, default(PackageGraphConnectionBundle));
        }

        private static float[] GetCandidateYChannels(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphConnectionBundle bundle)
        {
            List<float> channels = new List<float>
            {
                (from.y + to.y) * 0.5f,
                from.y - 86f,
                from.y + 86f,
                from.y - 220f,
                from.y + 220f,
                to.y - 86f,
                to.y + 86f,
                to.y - 220f,
                to.y + 220f,
                Mathf.Min(from.y, to.y) - 520f,
                Mathf.Max(from.y, to.y) + 520f
            };
            float min = Mathf.Min(from.y, to.y) - 760f;
            float max = Mathf.Max(from.y, to.y) + 760f;

            foreach (PackageGraphRouteObstacle obstacle in obstacles ?? Array.Empty<PackageGraphRouteObstacle>())
            {
                if (ShouldIgnoreObstacleForBundle(obstacle, bundle))
                {
                    continue;
                }

                channels.Add(obstacle.Rect.yMin - RouteObstacleMargin);
                channels.Add(obstacle.Rect.yMax + RouteObstacleMargin);
            }

            channels.Sort();
            List<float> unique = new List<float>(channels.Count);

            foreach (float value in channels)
            {
                if (value < min || value > max)
                {
                    continue;
                }

                if (unique.Count == 0 || Mathf.Abs(unique[unique.Count - 1] - value) > 1f)
                {
                    unique.Add(value);
                }
            }

            return unique.ToArray();
        }

        private static void AddRouteCandidate(
            ICollection<IReadOnlyList<Vector2>> candidates,
            IReadOnlyList<Vector2> points)
        {
            IReadOnlyList<Vector2> simplified = SimplifyRoutePoints(points);

            if (simplified.Count >= 2)
            {
                candidates.Add(simplified);
            }
        }

        private static bool IsRoutePathValid(
            IReadOnlyList<Vector2> points,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles)
        {
            return IsRoutePathValid(points, obstacles, default(PackageGraphConnectionBundle));
        }

        private static bool IsRoutePathValid(
            IReadOnlyList<Vector2> points,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphConnectionBundle bundle)
        {
            if (points == null || points.Count < 2)
            {
                return false;
            }

            for (int index = 0; index < points.Count - 1; index++)
            {
                bool isFirstSegment = index == 0;
                bool isLastSegment = index == points.Count - 2;
                Rect segmentBounds = BuildSegmentBounds(points[index], points[index + 1]);

                foreach (PackageGraphRouteObstacle obstacle in obstacles ?? Array.Empty<PackageGraphRouteObstacle>())
                {
                    if (ShouldIgnoreObstacleForSegment(
                            obstacle,
                            bundle,
                            points[0],
                            points[points.Count - 1],
                            isFirstSegment,
                            isLastSegment))
                    {
                        continue;
                    }

                    if (RectsOverlap(segmentBounds, obstacle.Rect) &&
                        LineIntersectsRectInterior(points[index], points[index + 1], obstacle.Rect))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ShouldIgnoreObstacleForSegment(
            PackageGraphRouteObstacle obstacle,
            PackageGraphConnectionBundle bundle,
            Vector2 routeStart,
            Vector2 routeEnd,
            bool isFirstSegment,
            bool isLastSegment)
        {
            if (ShouldIgnoreObstacleForBundle(obstacle, bundle))
            {
                return true;
            }

            if (!IsCategoryObstacle(obstacle))
            {
                return false;
            }

            return (isFirstSegment && RectContainsPointInclusive(obstacle.Rect, routeStart)) ||
                   (isLastSegment && RectContainsPointInclusive(obstacle.Rect, routeEnd));
        }

        private static bool IsCategoryObstacle(PackageGraphRouteObstacle obstacle)
        {
            return !string.IsNullOrWhiteSpace(obstacle.Id) &&
                   obstacle.Id.StartsWith("category:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RectContainsPointInclusive(Rect rect, Vector2 point)
        {
            return point.x >= rect.xMin &&
                   point.x <= rect.xMax &&
                   point.y >= rect.yMin &&
                   point.y <= rect.yMax;
        }

        private static Rect BuildSegmentBounds(Vector2 start, Vector2 end)
        {
            return Rect.MinMaxRect(
                Mathf.Min(start.x, end.x) - 0.01f,
                Mathf.Min(start.y, end.y) - 0.01f,
                Mathf.Max(start.x, end.x) + 0.01f,
                Mathf.Max(start.y, end.y) + 0.01f);
        }

        private static bool RectsOverlap(Rect first, Rect second)
        {
            return first.xMin <= second.xMax &&
                   first.xMax >= second.xMin &&
                   first.yMin <= second.yMax &&
                   first.yMax >= second.yMin;
        }

        private static float ScoreRoutePath(
            IReadOnlyList<Vector2> points,
            PackageGraphEdgeRouteZone zone)
        {
            float length = GetRouteLength(points);
            float direct = Vector2.Distance(points[0], points[points.Count - 1]);
            float detourRatio = direct > 1f ? length / direct : 1f;
            float bendCost = Mathf.Max(0, points.Count - 2) * RouteBendPenalty;
            float detourCost = detourRatio > 1.75f
                ? (detourRatio - 1.75f) * RouteDetourPenalty * direct
                : 0f;
            return length + bendCost + detourCost + GetRouteZonePenalty(points, zone);
        }

        private static float GetRouteZonePenalty(
            IReadOnlyList<Vector2> points,
            PackageGraphEdgeRouteZone zone)
        {
            if (points == null || points.Count < 2)
            {
                return 0f;
            }

            Vector2 from = points[0];
            Vector2 to = points[points.Count - 1];

            switch (zone)
            {
                case PackageGraphEdgeRouteZone.Providers:
                case PackageGraphEdgeRouteZone.Dependents:
                    return Mathf.Abs(to.y - from.y) * 0.03f;
                case PackageGraphEdgeRouteZone.Integrations:
                case PackageGraphEdgeRouteZone.CompanionsAndSuites:
                    return Mathf.Abs(to.x - from.x) * 0.03f;
                default:
                    return 0f;
            }
        }

        private static IReadOnlyList<Vector2> SimplifyRoutePoints(IReadOnlyList<Vector2> points)
        {
            if (points == null)
            {
                return Array.Empty<Vector2>();
            }

            List<Vector2> cleaned = new List<Vector2>();

            foreach (Vector2 point in points)
            {
                if (cleaned.Count == 0 || Vector2.Distance(cleaned[cleaned.Count - 1], point) > 0.1f)
                {
                    cleaned.Add(point);
                }
            }

            for (int index = cleaned.Count - 2; index > 0; index--)
            {
                if (AreCollinear(cleaned[index - 1], cleaned[index], cleaned[index + 1]))
                {
                    cleaned.RemoveAt(index);
                }
            }

            return cleaned;
        }

        private static bool AreCollinear(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 bc = c - b;
            return Mathf.Abs(ab.x * bc.y - ab.y * bc.x) < 0.1f;
        }

        private static PackageGraphEdgeRouteZone GetFocusRouteZone(
            PackageGraphModel graph,
            PackageGraphConnectionBundle bundle,
            string focusPackageId)
        {
            if (string.IsNullOrWhiteSpace(focusPackageId))
            {
                return PackageGraphEdgeRouteZone.Direct;
            }

            if (bundle.IsCompositeDependencyIntegration &&
                string.Equals(bundle.SourcePackageId, focusPackageId, StringComparison.OrdinalIgnoreCase) &&
                graph != null &&
                graph.TryGetNode(bundle.TargetPackageId, out PackageGraphNode targetNode) &&
                targetNode.IsIntegration)
            {
                return PackageGraphEdgeRouteZone.Integrations;
            }

            if (bundle.HasDependency)
            {
                if (string.Equals(bundle.TargetPackageId, focusPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    return PackageGraphEdgeRouteZone.Providers;
                }

                if (string.Equals(bundle.SourcePackageId, focusPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    return PackageGraphEdgeRouteZone.Dependents;
                }
            }

            if (bundle.HasIntegration)
            {
                return PackageGraphEdgeRouteZone.Integrations;
            }

            if (bundle.HasOptionalCompanion ||
                bundle.HasRecommended ||
                bundle.HasSuiteMembership)
            {
                return PackageGraphEdgeRouteZone.CompanionsAndSuites;
            }

            return PackageGraphEdgeRouteZone.Direct;
        }

        private static string GetRouteGroupKey(
            PackageGraphConnectionBundle bundle,
            PackageGraphEdgeRouteZone zone,
            string focusPackageId)
        {
            return bundle.Semantics + ":" + zone + ":" + (focusPackageId ?? string.Empty);
        }

        private static float GetRouteSortValue(
            PackageGraphEdgeRouteContext context,
            string focusPackageId)
        {
            bool focusIsSource = string.Equals(
                context.Bundle.SourcePackageId,
                focusPackageId,
                StringComparison.OrdinalIgnoreCase);
            Rect otherRect = focusIsSource ? context.ToRect : context.FromRect;

            switch (context.Zone)
            {
                case PackageGraphEdgeRouteZone.Providers:
                case PackageGraphEdgeRouteZone.Dependents:
                    return otherRect.center.y;
                case PackageGraphEdgeRouteZone.Integrations:
                case PackageGraphEdgeRouteZone.CompanionsAndSuites:
                    return otherRect.center.x;
                default:
                    return otherRect.center.x + otherRect.center.y;
            }
        }

        private static string GetOtherNodeDisplayName(
            PackageGraphModel graph,
            PackageGraphConnectionBundle bundle,
            string focusPackageId)
        {
            string otherPackageId = bundle.GetOtherPackageId(focusPackageId);
            return graph != null &&
                   !string.IsNullOrWhiteSpace(otherPackageId) &&
                   graph.TryGetNode(otherPackageId, out PackageGraphNode node)
                ? node.DisplayName
                : otherPackageId ?? string.Empty;
        }

        private static PackageGraphEdgeRoutePort GetFocusPort(PackageGraphEdgeRouteZone zone)
        {
            switch (zone)
            {
                case PackageGraphEdgeRouteZone.Providers:
                    return PackageGraphEdgeRoutePort.Left;
                case PackageGraphEdgeRouteZone.Dependents:
                    return PackageGraphEdgeRoutePort.Right;
                case PackageGraphEdgeRouteZone.Integrations:
                    return PackageGraphEdgeRoutePort.Bottom;
                case PackageGraphEdgeRouteZone.CompanionsAndSuites:
                    return PackageGraphEdgeRoutePort.Top;
                default:
                    return PackageGraphEdgeRoutePort.Auto;
            }
        }

        private static PackageGraphEdgeRoutePort GetRelatedPort(PackageGraphEdgeRouteZone zone)
        {
            switch (zone)
            {
                case PackageGraphEdgeRouteZone.Providers:
                    return PackageGraphEdgeRoutePort.Right;
                case PackageGraphEdgeRouteZone.Dependents:
                    return PackageGraphEdgeRoutePort.Left;
                case PackageGraphEdgeRouteZone.Integrations:
                    return PackageGraphEdgeRoutePort.Top;
                case PackageGraphEdgeRouteZone.CompanionsAndSuites:
                    return PackageGraphEdgeRoutePort.Bottom;
                default:
                    return PackageGraphEdgeRoutePort.Auto;
            }
        }

        private static Vector2 GetTrunkPoint(
            Vector2 focusPoint,
            Vector2 otherPoint,
            PackageGraphEdgeRouteZone zone)
        {
            const float PreferredTrunkLength = 94f;
            float distance;
            float direction;

            switch (zone)
            {
                case PackageGraphEdgeRouteZone.Providers:
                    direction = otherPoint.x < focusPoint.x ? -1f : 1f;
                    distance = GetCorridorTrunkDistance(otherPoint.x - focusPoint.x, PreferredTrunkLength);
                    return new Vector2(focusPoint.x + direction * distance, focusPoint.y);
                case PackageGraphEdgeRouteZone.Dependents:
                    direction = otherPoint.x >= focusPoint.x ? 1f : -1f;
                    distance = GetCorridorTrunkDistance(otherPoint.x - focusPoint.x, PreferredTrunkLength);
                    return new Vector2(focusPoint.x + direction * distance, focusPoint.y);
                case PackageGraphEdgeRouteZone.Integrations:
                    direction = otherPoint.y >= focusPoint.y ? 1f : -1f;
                    distance = GetCorridorTrunkDistance(otherPoint.y - focusPoint.y, PreferredTrunkLength);
                    return new Vector2(focusPoint.x, focusPoint.y + direction * distance);
                case PackageGraphEdgeRouteZone.CompanionsAndSuites:
                    direction = otherPoint.y < focusPoint.y ? -1f : 1f;
                    distance = GetCorridorTrunkDistance(otherPoint.y - focusPoint.y, PreferredTrunkLength);
                    return new Vector2(focusPoint.x, focusPoint.y + direction * distance);
                default:
                    return (focusPoint + otherPoint) * 0.5f;
            }
        }

        private static float GetCorridorTrunkDistance(float axisDelta, float preferredDistance)
        {
            float axisDistance = Mathf.Abs(axisDelta);

            if (axisDistance <= 1f)
            {
                return 28f;
            }

            float maximum = Mathf.Min(preferredDistance, axisDistance * 0.58f);
            float desired = axisDistance * 0.38f;

            if (maximum < 28f)
            {
                return Mathf.Max(12f, maximum);
            }

            return Mathf.Clamp(desired, 28f, maximum);
        }

        private static Vector2 GetBranchPoint(
            Vector2 trunkPoint,
            Vector2 otherPoint,
            PackageGraphEdgeRouteZone zone)
        {
            switch (zone)
            {
                case PackageGraphEdgeRouteZone.Providers:
                case PackageGraphEdgeRouteZone.Dependents:
                    return new Vector2(trunkPoint.x, otherPoint.y);
                case PackageGraphEdgeRouteZone.Integrations:
                case PackageGraphEdgeRouteZone.CompanionsAndSuites:
                    return new Vector2(otherPoint.x, trunkPoint.y);
                default:
                    return (trunkPoint + otherPoint) * 0.5f;
            }
        }

        private static Vector2 GetPort(
            Rect rect,
            PackageGraphEdgeRoutePort port,
            Rect otherRect,
            float padding)
        {
            return port == PackageGraphEdgeRoutePort.Auto
                ? GetAutoPort(rect, otherRect, padding)
                : GetPort(rect, port, padding);
        }

        private static Vector2 GetPort(
            Rect rect,
            PackageGraphEdgeRoutePort port,
            float padding)
        {
            switch (port)
            {
                case PackageGraphEdgeRoutePort.Left:
                    return new Vector2(rect.xMin - padding, rect.center.y);
                case PackageGraphEdgeRoutePort.Right:
                    return new Vector2(rect.xMax + padding, rect.center.y);
                case PackageGraphEdgeRoutePort.Top:
                    return new Vector2(rect.center.x, rect.yMin - padding);
                case PackageGraphEdgeRoutePort.Bottom:
                    return new Vector2(rect.center.x, rect.yMax + padding);
                default:
                    return rect.center;
            }
        }

        private static Vector2 GetAutoPort(Rect fromRect, Rect toRect, float padding)
        {
            Vector2 delta = toRect.center - fromRect.center;

            if (delta.sqrMagnitude < 0.01f)
            {
                return fromRect.center;
            }

            float scaleX = Mathf.Abs(delta.x) > 0.01f
                ? (fromRect.width * 0.5f) / Mathf.Abs(delta.x)
                : float.PositiveInfinity;
            float scaleY = Mathf.Abs(delta.y) > 0.01f
                ? (fromRect.height * 0.5f) / Mathf.Abs(delta.y)
                : float.PositiveInfinity;
            float scale = Mathf.Min(scaleX, scaleY);
            return fromRect.center +
                   delta.normalized * ((delta.magnitude * Mathf.Clamp01(scale)) + Mathf.Max(0f, padding));
        }

        private static bool IsDashed(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.Recommended ||
                   kind == PackageGraphEdgeKind.SuiteMembership;
        }

        private static bool IsDotted(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.OptionalCompanion;
        }

        private static bool ShouldAnimate(PackageGraphEdgeKind kind)
        {
            return SupportsDirectionalFlowMarkers(kind);
        }

        internal static bool UsesDirectionalFlowMarkersForTests(PackageGraphEdgeKind kind)
        {
            return SupportsDirectionalFlowMarkers(kind);
        }

        internal static bool AnimatesEdgeForTests(PackageGraphEdgeKind kind)
        {
            return ShouldAnimate(kind);
        }

        internal static bool UsesTwoPassStrokeForTests(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.HardDependency ||
                   kind == PackageGraphEdgeKind.IntegrationConnection ||
                   kind == PackageGraphEdgeKind.OptionalCompanion ||
                   kind == PackageGraphEdgeKind.SuiteMembership;
        }

        private static bool SupportsDirectionalFlowMarkers(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.HardDependency ||
                   kind == PackageGraphEdgeKind.IntegrationConnection;
        }

        private static Color GetEdgeColor(PackageGraphEdge edge, bool emphasized, bool focusMode)
        {
            if (edge.State == PackageGraphEdgeState.Warning)
            {
                return new Color(0.94f, 0.64f, 0.27f, emphasized ? 0.94f : 0.62f);
            }

            if (emphasized)
            {
                if (edge.Kind == PackageGraphEdgeKind.HardDependency)
                {
                    return new Color(0.34f, 0.70f, 0.98f, 0.76f);
                }

                if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
                {
                    return new Color(0.24f, 0.82f, 0.75f, 0.72f);
                }

                if (edge.Kind == PackageGraphEdgeKind.OptionalCompanion)
                {
                    return new Color(0.62f, 0.75f, 0.84f, 0.44f);
                }

                return new Color(0.48f, 0.74f, 0.78f, 0.50f);
            }

            if (focusMode)
            {
                return edge.Kind == PackageGraphEdgeKind.OptionalCompanion
                    ? new Color(0.42f, 0.54f, 0.70f, 0.12f)
                    : new Color(0.42f, 0.54f, 0.70f, 0.22f);
            }

            switch (edge.Kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                    return new Color(0.42f, 0.54f, 0.72f, 0.24f);
                case PackageGraphEdgeKind.IntegrationConnection:
                    return new Color(0.20f, 0.70f, 0.66f, 0.18f);
                case PackageGraphEdgeKind.OptionalCompanion:
                    return new Color(0.50f, 0.62f, 0.72f, 0.12f);
                default:
                    return new Color(0.28f, 0.58f, 0.76f, 0.14f);
            }
        }

        private static float GetEdgeWidth(PackageGraphEdge edge, bool emphasized, bool focusMode)
        {
            if (emphasized)
            {
                if (edge.State == PackageGraphEdgeState.Warning)
                {
                    return 2.1f;
                }

                if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
                {
                    return 1.9f;
                }

                return edge.Kind == PackageGraphEdgeKind.OptionalCompanion ? 1.15f : 2.15f;
            }

            if (focusMode)
            {
                return edge.Kind == PackageGraphEdgeKind.OptionalCompanion ? 0.75f : 0.95f;
            }

            switch (edge.Kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                    return 1.45f;
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 1.2f;
                case PackageGraphEdgeKind.OptionalCompanion:
                    return 0.9f;
                default:
                    return 1f;
            }
        }

        private static void DrawPolylineStroke(
            PackageGraphPainter painter,
            IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            painter.BeginPath();
            painter.MoveTo(points[0]);

            for (int index = 1; index < points.Count; index++)
            {
                painter.LineTo(points[index]);
            }

            painter.Stroke();
        }

        private static void DrawRouteUnderlay(
            PackageGraphPainter painter,
            IReadOnlyList<Vector2> points,
            PackageGraphEdgeKind kind,
            float semanticWidth,
            bool emphasized,
            bool focusMode)
        {
            if (points == null || points.Count < 2 || !UsesTwoPassStrokeForTests(kind))
            {
                return;
            }

            float alpha = emphasized
                ? 0.34f
                : focusMode
                    ? 0.16f
                    : 0.12f;
            float extraWidth = emphasized ? 2.15f : 1.45f;
            painter.strokeColor = new Color(0.01f, 0.03f, 0.05f, alpha);
            painter.lineWidth = Mathf.Max(1.2f, semanticWidth + extraWidth);
            DrawPolylineStroke(painter, points);
        }

        private static void DrawIntegrationCableRoute(
            PackageGraphPainter painter,
            IReadOnlyList<Vector2> points,
            Color color,
            float width,
            bool emphasized)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            float offset = emphasized ? 1.8f : 1.35f;
            Color underlay = new Color(0.04f, 0.12f, 0.14f, Mathf.Min(0.34f, color.a * 0.50f));

            painter.strokeColor = underlay;
            painter.lineWidth = Mathf.Max(1.2f, width + 0.85f);
            DrawPolylineStroke(painter, points);

            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(0.65f, width * 0.42f);
            DrawOffsetPolyline(painter, points, offset);
            DrawOffsetPolyline(painter, points, -offset);
        }

        private static void DrawOffsetPolyline(
            PackageGraphPainter painter,
            IReadOnlyList<Vector2> points,
            float offset)
        {
            for (int index = 0; index < points.Count - 1; index++)
            {
                Vector2 start = points[index];
                Vector2 end = points[index + 1];
                Vector2 delta = end - start;

                if (delta.sqrMagnitude <= 0.01f)
                {
                    continue;
                }

                Vector2 side = new Vector2(-delta.y, delta.x).normalized * offset;
                painter.BeginPath();
                painter.MoveTo(start + side);
                painter.LineTo(end + side);
                painter.Stroke();
            }
        }

        private static void DrawFlowMarkers(
            PackageGraphPainter painter,
            PackageGraphEdgeRoute route,
            PackageGraphEdgeKind kind,
            Color color,
            float animationPhase)
        {
            int markerCount = GetFlowMarkerCount(kind);
            float markerSize = GetFlowMarkerSize(kind);
            float markerWidth = GetFlowMarkerWidth(kind);
            float markerAlpha = GetFlowMarkerAlpha(kind);
            Color markerColor = new Color(color.r, color.g, color.b, Mathf.Min(1f, color.a * markerAlpha));

            for (int index = 0; index < markerCount; index++)
            {
                float branchOffset = route.UsesSharedTrunk
                    ? route.BranchIndex * 0.11f
                    : 0f;
                float phase = Mathf.Repeat(animationPhase + branchOffset + (index / (float)markerCount), 1f);
                float markerT = Mathf.Lerp(MarkerTravelStart, MarkerTravelEnd, phase);
                Vector2 point = GetPointOnRoute(route.Points, markerT, out Vector2 tangent);
                DrawFlowChevron(
                    painter,
                    point,
                    tangent,
                    markerColor,
                    markerSize,
                    markerWidth);
            }
        }

        private static void DrawFlowChevron(
            PackageGraphPainter painter,
            Vector2 center,
            Vector2 tangent,
            Color color,
            float size,
            float width)
        {
            if (tangent.sqrMagnitude < 0.01f)
            {
                return;
            }

            Vector2 forward = tangent.normalized;
            Vector2 side = new Vector2(-forward.y, forward.x);
            Vector2 tip = center + forward * size * 0.62f;
            Vector2 back = center - forward * size * 0.58f;
            Vector2 left = back + side * size * 0.48f;
            Vector2 right = back - side * size * 0.48f;

            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(left);
            painter.LineTo(tip);
            painter.LineTo(right);
            painter.Stroke();
        }

        private static int GetFlowMarkerCount(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.SuiteMembership
                ? 1
                : 2;
        }

        private static float GetFlowMarkerSize(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 4.8f;
                case PackageGraphEdgeKind.SuiteMembership:
                    return 4.2f;
                default:
                    return 5.0f;
            }
        }

        private static float GetFlowMarkerWidth(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 1.0f;
                default:
                    return 1.0f;
            }
        }

        private static float GetFlowMarkerAlpha(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.SuiteMembership:
                    return 0.48f;
                default:
                    return 0.74f;
            }
        }

        private static void DrawDashedPolyline(
            PackageGraphPainter painter,
            IReadOnlyList<Vector2> points,
            float dashLength,
            float gapLength,
            float dashOffset)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            float patternLength = dashLength + gapLength;
            float normalizedOffset = Mathf.Repeat(dashOffset, patternLength);

            bool draw = normalizedOffset < dashLength;
            float segmentCursor = draw ? normalizedOffset : normalizedOffset - dashLength;

            for (int index = 0; index < points.Count - 1; index++)
            {
                Vector2 previous = points[index];
                Vector2 current = points[index + 1];
                Vector2 delta = current - previous;
                float length = delta.magnitude;

                if (length <= 0.1f)
                {
                    continue;
                }

                Vector2 direction = delta / length;
                float consumed = 0f;

                while (consumed < length)
                {
                    float targetLength = draw ? dashLength : gapLength;
                    float step = Mathf.Min(targetLength - segmentCursor, length - consumed);
                    Vector2 segmentStart = previous + direction * consumed;
                    Vector2 segmentEnd = previous + direction * (consumed + step);

                    if (draw)
                    {
                        painter.BeginPath();
                        painter.MoveTo(segmentStart);
                        painter.LineTo(segmentEnd);
                        painter.Stroke();
                    }

                    consumed += step;
                    segmentCursor += step;

                    if (segmentCursor >= targetLength - 0.01f)
                    {
                        segmentCursor = 0f;
                        draw = !draw;
                    }
                }
            }
        }

        private static Vector2 GetPointOnRoute(
            IReadOnlyList<Vector2> points,
            float normalizedDistance,
            out Vector2 tangent)
        {
            tangent = Vector2.right;

            if (points == null || points.Count == 0)
            {
                return default(Vector2);
            }

            if (points.Count == 1)
            {
                return points[0];
            }

            float totalLength = GetRouteLength(points);

            if (totalLength <= 0.01f)
            {
                tangent = points[points.Count - 1] - points[0];
                return points[0];
            }

            float targetDistance = Mathf.Clamp01(normalizedDistance) * totalLength;
            float consumed = 0f;

            for (int index = 0; index < points.Count - 1; index++)
            {
                Vector2 start = points[index];
                Vector2 end = points[index + 1];
                Vector2 segment = end - start;
                float length = segment.magnitude;

                if (length <= 0.01f)
                {
                    continue;
                }

                if (consumed + length >= targetDistance)
                {
                    tangent = segment;
                    return Vector2.Lerp(start, end, (targetDistance - consumed) / length);
                }

                consumed += length;
            }

            tangent = points[points.Count - 1] - points[points.Count - 2];
            return points[points.Count - 1];
        }

        private static float GetRouteLength(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 2)
            {
                return 0f;
            }

            float length = 0f;

            for (int index = 0; index < points.Count - 1; index++)
            {
                length += Vector2.Distance(points[index], points[index + 1]);
            }

            return length;
        }

        internal static bool RouteCrossesNodeInteriorForTests(
            PackageGraphEdgeRoute route,
            IReadOnlyDictionary<string, Rect> nodeRects)
        {
            if (route.Points == null || route.Points.Count < 2 || nodeRects == null || route.Edge == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, Rect> nodeRect in nodeRects)
            {
                if (string.Equals(nodeRect.Key, route.Bundle.SourcePackageId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(nodeRect.Key, route.Bundle.TargetPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Rect interior = ShrinkRect(nodeRect.Value, 2f);

                for (int index = 0; index < route.Points.Count - 1; index++)
                {
                    if (LineIntersectsRectInterior(route.Points[index], route.Points[index + 1], interior))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool RouteCrossesGraphObstacleForTests(
            PackageGraphEdgeRoute route,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            if (route.Points == null || route.Points.Count < 2)
            {
                return false;
            }

            IReadOnlyList<PackageGraphRouteObstacle> obstacles =
                BuildRouteObstacles(route.Bundle, nodeRects, groupRects);
            return !IsRoutePathValid(route.Points, obstacles);
        }

        internal static float RouteLengthForTests(PackageGraphEdgeRoute route)
        {
            return route.Points == null ? 0f : GetRouteLength(route.Points);
        }

        internal static float DirectRouteDistanceForTests(PackageGraphEdgeRoute route)
        {
            return route.Points == null || route.Points.Count < 2
                ? 0f
                : Vector2.Distance(route.Points[0], route.Points[route.Points.Count - 1]);
        }

        private static Rect ShrinkRect(Rect rect, float amount)
        {
            float inset = Mathf.Max(0f, amount);
            return new Rect(
                rect.xMin + inset,
                rect.yMin + inset,
                Mathf.Max(0f, rect.width - inset * 2f),
                Mathf.Max(0f, rect.height - inset * 2f));
        }

        private static Rect InflateRect(Rect rect, float amount)
        {
            float padding = Mathf.Max(0f, amount);
            return Rect.MinMaxRect(
                rect.xMin - padding,
                rect.yMin - padding,
                rect.xMax + padding,
                rect.yMax + padding);
        }

        private static bool LineIntersectsRectInterior(
            Vector2 start,
            Vector2 end,
            Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return false;
            }

            if (Mathf.Max(start.x, end.x) < rect.xMin ||
                Mathf.Min(start.x, end.x) > rect.xMax ||
                Mathf.Max(start.y, end.y) < rect.yMin ||
                Mathf.Min(start.y, end.y) > rect.yMax)
            {
                return false;
            }

            if (rect.Contains(start) || rect.Contains(end))
            {
                return true;
            }

            Vector2 bottomLeft = new Vector2(rect.xMin, rect.yMin);
            Vector2 bottomRight = new Vector2(rect.xMax, rect.yMin);
            Vector2 topRight = new Vector2(rect.xMax, rect.yMax);
            Vector2 topLeft = new Vector2(rect.xMin, rect.yMax);

            return LineSegmentsIntersect(start, end, bottomLeft, bottomRight) ||
                   LineSegmentsIntersect(start, end, bottomRight, topRight) ||
                   LineSegmentsIntersect(start, end, topRight, topLeft) ||
                   LineSegmentsIntersect(start, end, topLeft, bottomLeft);
        }

        private static bool LineSegmentsIntersect(
            Vector2 firstStart,
            Vector2 firstEnd,
            Vector2 secondStart,
            Vector2 secondEnd)
        {
            float firstOrientation = Cross(firstEnd - firstStart, secondStart - firstStart);
            float secondOrientation = Cross(firstEnd - firstStart, secondEnd - firstStart);
            float thirdOrientation = Cross(secondEnd - secondStart, firstStart - secondStart);
            float fourthOrientation = Cross(secondEnd - secondStart, firstEnd - secondStart);

            if (HasOppositeSigns(firstOrientation, secondOrientation) &&
                HasOppositeSigns(thirdOrientation, fourthOrientation))
            {
                return true;
            }

            const float Epsilon = 0.001f;
            return Mathf.Abs(firstOrientation) <= Epsilon && IsPointOnSegment(secondStart, firstStart, firstEnd) ||
                   Mathf.Abs(secondOrientation) <= Epsilon && IsPointOnSegment(secondEnd, firstStart, firstEnd) ||
                   Mathf.Abs(thirdOrientation) <= Epsilon && IsPointOnSegment(firstStart, secondStart, secondEnd) ||
                   Mathf.Abs(fourthOrientation) <= Epsilon && IsPointOnSegment(firstEnd, secondStart, secondEnd);
        }

        private static bool HasOppositeSigns(float first, float second)
        {
            return first > 0f && second < 0f ||
                   first < 0f && second > 0f;
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static bool IsPointOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            const float Epsilon = 0.001f;
            return point.x >= Mathf.Min(segmentStart.x, segmentEnd.x) - Epsilon &&
                   point.x <= Mathf.Max(segmentStart.x, segmentEnd.x) + Epsilon &&
                   point.y >= Mathf.Min(segmentStart.y, segmentEnd.y) - Epsilon &&
                   point.y <= Mathf.Max(segmentStart.y, segmentEnd.y) + Epsilon;
        }

        private static void DrawWarningMarker(PackageGraphPainter painter, Vector2 center)
        {
            painter.fillColor = new Color(0.94f, 0.64f, 0.27f, 0.90f);
            painter.strokeColor = new Color(0.16f, 0.12f, 0.06f, 0.86f);
            painter.lineWidth = 1.2f;

            painter.BeginPath();
            painter.MoveTo(center + new Vector2(0f, -7f));
            painter.LineTo(center + new Vector2(7f, 6f));
            painter.LineTo(center + new Vector2(-7f, 6f));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }
    }

    internal static class PackageGraphKeyboard
    {
        public static bool Activate(KeyDownEvent evt, VisualElement owner, Action action)
        {
            if (evt == null || owner == null || evt.target != owner || !IsActivationKey(evt.keyCode))
            {
                return false;
            }

            action?.Invoke();
            evt.StopPropagation();
            return true;
        }

        internal static bool IsActivationKey(KeyCode keyCode)
        {
            return keyCode == KeyCode.Return ||
                   keyCode == KeyCode.KeypadEnter ||
                   keyCode == KeyCode.Space;
        }
    }

    internal sealed class PackageGraphOverflowSummaryElement : Label
    {
        private readonly string _diagnostic;

        public PackageGraphOverflowSummaryElement(string text, string diagnostic)
            : base(text)
        {
            _diagnostic = diagnostic ?? string.Empty;
            focusable = true;
            tabIndex = 0;
            pickingMode = PickingMode.Position;
            RegisterCallback<ClickEvent>(evt =>
            {
                Activate();
                evt.StopPropagation();
            });
            RegisterCallback<KeyDownEvent>(evt =>
                PackageGraphKeyboard.Activate(evt, this, Activate));
        }

        internal bool HasKeyboardActivationForTests => !string.IsNullOrEmpty(_diagnostic);

        internal void ActivateForTests()
        {
            Activate();
        }

        private void Activate()
        {
            EditorGUIUtility.systemCopyBuffer = _diagnostic;
        }
    }

    internal enum PackageGraphNodeVisualMode
    {
        Overview,
        Focus,
        Stack
    }

    internal sealed class PackageGraphGroupElement : VisualElement
    {
        public PackageGraphGroupElement(
            PackageGraphGroupLayoutNode groupNode,
            PackageGraphLayoutMode layoutMode,
            bool hoverContext,
            bool hoverDimmed,
            string backTooltip,
            bool interactionsEnabled,
            Action<PackageGraphGroup> groupFocused,
            Action<string> previewGroup,
            Action<string> clearPreviewGroup,
            int searchMatchCount = 0)
        {
            if (groupNode == null || groupNode.Group == null)
            {
                throw new ArgumentNullException(nameof(groupNode));
            }

            name = "group-" + groupNode.GroupId;
            AddToClassList("dpi-graph-group");
            AddToClassList("dpi-graph-group--" + GetRingClass(groupNode.Ring));
            EnableInClassList("dpi-graph-group--focused", groupNode.Focused);
            EnableInClassList("dpi-graph-group--collapsed", groupNode.Collapsed);
            EnableInClassList(
                "dpi-graph-group--overview",
                layoutMode == PackageGraphLayoutMode.Overview);
            EnableInClassList("dpi-graph-group--locked", !interactionsEnabled);
            EnableInClassList("dpi-graph-group--attention", groupNode.AttentionCount > 0);
            EnableInClassList("dpi-graph-group--empty", groupNode.PackageCount == 0);
            EnableInClassList("dpi-graph-group--hover-context", hoverContext);
            EnableInClassList("dpi-graph-group--hover-dimmed", hoverDimmed);
            bool hasBackAffordance = !string.IsNullOrWhiteSpace(backTooltip);
            EnableInClassList("dpi-graph-group--has-back", hasBackAffordance);
            tooltip = hasBackAffordance ? backTooltip : groupNode.Group.DisplayName;
            focusable = interactionsEnabled;
            tabIndex = interactionsEnabled ? 0 : -1;

            if (interactionsEnabled)
            {
                RegisterCallback<MouseEnterEvent>(_ => previewGroup?.Invoke(groupNode.GroupId));
                RegisterCallback<MouseLeaveEvent>(_ => clearPreviewGroup?.Invoke(groupNode.GroupId));
                RegisterCallback<FocusInEvent>(_ => previewGroup?.Invoke(groupNode.GroupId));
                RegisterCallback<FocusOutEvent>(_ => clearPreviewGroup?.Invoke(groupNode.GroupId));
                RegisterCallback<ClickEvent>(evt =>
                {
                    groupFocused?.Invoke(groupNode.Group);
                    evt.StopPropagation();
                });
                RegisterCallback<KeyDownEvent>(evt => PackageGraphKeyboard.Activate(
                    evt,
                    this,
                    () => groupFocused?.Invoke(groupNode.Group)));
            }

            float symbolSize = Mathf.Min(groupNode.HubRect.width, groupNode.HubRect.height);
            float symbolLeft = groupNode.HubRect.x - groupNode.Rect.x;
            float symbolTop = groupNode.HubRect.y - groupNode.Rect.y;

            VisualElement symbol = new VisualElement();
            symbol.AddToClassList("dpi-graph-group__symbol");
            symbol.style.position = Position.Absolute;
            symbol.style.left = symbolLeft;
            symbol.style.top = symbolTop;
            symbol.style.width = symbolSize;
            symbol.style.height = symbolSize;
            Add(symbol);

            VisualElement symbolHighlight = new VisualElement();
            symbolHighlight.AddToClassList("dpi-graph-group__glass-highlight");
            symbolHighlight.pickingMode = PickingMode.Ignore;
            symbol.Add(symbolHighlight);

            VisualElement symbolSheen = DeucarianEditorGlassSheen.Create();
            symbol.Add(symbolSheen);

            if (interactionsEnabled)
            {
                RegisterCallback<MouseEnterEvent>(_ => DeucarianEditorGlassSheen.Play(symbolSheen));
            }

            Image icon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon(groupNode.Group.IconKey),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList("dpi-graph-group__icon");
            symbol.Add(icon);

            VisualElement caption = new VisualElement();
            caption.AddToClassList("dpi-graph-group__caption");
            caption.style.position = Position.Absolute;
            caption.style.left = 0f;
            caption.style.top = symbolTop + symbolSize + 7f;
            caption.style.width = groupNode.Rect.width;
            Add(caption);

            VisualElement titleRow = new VisualElement();
            titleRow.AddToClassList("dpi-graph-category-caption-row");
            caption.Add(titleRow);

            if (hasBackAffordance)
            {
                Label back = new Label("\u2039");
                back.AddToClassList("dpi-graph-back-hint");
                back.AddToClassList("dpi-graph-back-hint--category");
                back.AddToClassList("dpi-graph-group__back-hint");
                back.pickingMode = PickingMode.Ignore;
                back.tooltip = backTooltip;
                titleRow.Add(back);
            }

            Label title = new Label(GetTitle(groupNode));
            title.AddToClassList("dpi-graph-group__title");
            titleRow.Add(title);

            string subtitleText = layoutMode == PackageGraphLayoutMode.Overview && searchMatchCount > 0
                ? searchMatchCount + (searchMatchCount == 1 ? " match" : " matches")
                : (groupNode.Collapsed && !string.IsNullOrWhiteSpace(groupNode.SummaryLabel)
                    ? groupNode.SummaryLabel
                    : PackageGraphCategoryStatusVisuals.FormatTotal(groupNode.PackageCount));
            Label subtitle = new Label(subtitleText);
            subtitle.AddToClassList("dpi-graph-group__subtitle");
            caption.Add(subtitle);

            VisualElement stats = new VisualElement();
            stats.AddToClassList("dpi-graph-group__stats");
            stats.Add(CreateStat("Installed", groupNode.InstalledCount, "installed"));
            stats.Add(CreateStat("Not installed", groupNode.NotInstalledCount, "available"));
            if (groupNode.AttentionCount > 0)
            {
                stats.Add(CreateStat("Attention", groupNode.AttentionCount, "attention"));
            }

            if (groupNode.UnknownCount > 0)
            {
                stats.Add(CreateStat("Unknown", groupNode.UnknownCount, "unknown"));
            }

            caption.Add(stats);

        }

        public void SetHoverState(bool hoverContext, bool hoverDimmed)
        {
            EnableInClassList("dpi-graph-group--hover-context", hoverContext);
            EnableInClassList("dpi-graph-group--hover-dimmed", hoverDimmed);
        }

        private static string GetTitle(PackageGraphGroupLayoutNode groupNode)
        {
            return groupNode.Group.DisplayName;
        }

        private static Label CreateStat(string label, int count, string className)
        {
            Label stat = new Label(GetStatusMarker(className) + " " + count + " " + label.ToLowerInvariant());
            stat.AddToClassList("dpi-graph-group__stat");
            stat.AddToClassList("dpi-graph-group__stat--" + className);
            return stat;
        }

        private static string GetStatusMarker(string className)
        {
            switch (className)
            {
                case "installed":
                    return "\u2713";
                case "update":
                case "attention":
                    return "!";
                default:
                    return "\u25CB";
            }
        }

        private static string GetRingClass(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return "runtime";
                case PackageGraphLayoutRing.Integration:
                    return "integration";
                case PackageGraphLayoutRing.Suite:
                    return "suite";
                default:
                    return "infrastructure";
            }
        }
    }

    internal sealed class PackageGraphNodeElement : VisualElement
    {
        private readonly PackageGraphNode _node;
        private readonly Action _activationAction;

        public PackageGraphNodeElement(
            PackageGraphNode node,
            PackageGraphLayoutRing ring,
            PackageGraphNodeVisualMode visualMode,
            PackageGraphNodePresentationLevel presentationLevel,
            bool selected,
            bool related,
            bool dimmed,
            bool hoverContext,
            bool hoverDimmed,
            bool previewed,
            bool showActions,
            bool actionsEnabled,
            bool interactionsEnabled,
            string categoryPathLabel,
            string backTooltip,
            string relationshipTooltip,
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            Action<string> previewPackage,
            Action<string> clearPreviewPackage)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            bool isIconOnly = presentationLevel == PackageGraphNodePresentationLevel.IconOnly;
            bool isCompact = presentationLevel == PackageGraphNodePresentationLevel.Compact;
            bool isFull = presentationLevel == PackageGraphNodePresentationLevel.Full;
            bool showTitle = !isIconOnly;
            bool showHierarchy = isCompact || isFull;
            bool showBadges = isCompact || isFull;
            bool showTypeBadge = isFull;
            bool showPackageId = isFull;
            bool showActionShortcut = (isCompact || isFull) &&
                                      showActions &&
                                      IsGraphShortcutAction(node.PrimaryAction);
            bool showFooter = isFull || showActionShortcut;

            name = node.PackageId;
            AddToClassList("dpi-graph-node");
            AddToClassList("dpi-graph-node--" + GetNodeClass(node.NodeType));
            AddToClassList("dpi-graph-node--" + GetVisualModeClass(visualMode));
            AddToClassList("dpi-graph-node--presentation-" + GetPresentationClass(presentationLevel));
            AddToClassList("dpi-graph-node--status-" + GetStatusClass(node.Status));
            AddToClassList("dpi-graph-node--ring-" + GetRingClass(ring));
            EnableInClassList("dpi-graph-node--installed", node.IsInstalled);
            EnableInClassList("dpi-graph-node--actionable", showActionShortcut);
            EnableInClassList("dpi-graph-node--selected", selected);
            EnableInClassList("dpi-graph-node--related", related && !selected);
            EnableInClassList("dpi-graph-node--dimmed", dimmed);
            EnableInClassList("dpi-graph-node--hover-context", hoverContext);
            EnableInClassList("dpi-graph-node--hover-dimmed", hoverDimmed);
            EnableInClassList("dpi-graph-node--locked", !interactionsEnabled);
            EnableInClassList("dpi-graph-node--previewed", previewed);
            EnableInClassList("dpi-graph-node--missing", !node.IsRegistered);
            bool hasBackAffordance = selected && !string.IsNullOrWhiteSpace(backTooltip);
            EnableInClassList("dpi-graph-node--has-back", hasBackAffordance);
            tooltip = GetCompactTooltip(node, relationshipTooltip);
            if (!node.IsRegistered)
            {
                tooltip += "\nPress Enter or Space to copy this diagnostic.";
            }
            focusable = interactionsEnabled;
            tabIndex = interactionsEnabled ? 0 : -1;

            VisualElement statusRail = new VisualElement();
            statusRail.AddToClassList("dpi-graph-node__status-rail");
            statusRail.AddToClassList("dpi-graph-node__status-rail--" + GetStatusClass(node.Status));
            Add(statusRail);

            VisualElement glassHighlight = new VisualElement();
            glassHighlight.AddToClassList("dpi-graph-node__glass-highlight");
            glassHighlight.pickingMode = PickingMode.Ignore;
            Add(glassHighlight);

            VisualElement glassSheen = DeucarianEditorGlassSheen.Create();
            Add(glassSheen);

            if (interactionsEnabled)
            {
                RegisterCallback<MouseEnterEvent>(_ =>
                {
                    previewPackage?.Invoke(node.PackageId);
                    DeucarianEditorGlassSheen.Play(glassSheen);
                });
                RegisterCallback<MouseLeaveEvent>(_ => clearPreviewPackage?.Invoke(node.PackageId));
                RegisterCallback<FocusInEvent>(_ => previewPackage?.Invoke(node.PackageId));
                RegisterCallback<FocusOutEvent>(_ => clearPreviewPackage?.Invoke(node.PackageId));
            }

            if (node.PackageDefinition != null && packageSelected != null)
            {
                Action activate = () =>
                {
                    if (!interactionsEnabled)
                    {
                        return;
                    }

                    if (selected)
                    {
                        selectionCleared?.Invoke();
                    }
                    else
                    {
                        packageSelected(node.PackageDefinition);
                    }
                };
                _activationAction = activate;
                RegisterCallback<ClickEvent>(evt =>
                {
                    activate();
                    evt.StopPropagation();
                });
                RegisterCallback<KeyDownEvent>(evt => PackageGraphKeyboard.Activate(evt, this, activate));
            }
            else if (!node.IsRegistered && interactionsEnabled)
            {
                Action copyDiagnostic = () =>
                    EditorGUIUtility.systemCopyBuffer = PackageGraphView.GetMissingPackageDiagnostic(node);
                _activationAction = copyDiagnostic;
                RegisterCallback<KeyDownEvent>(evt =>
                    PackageGraphKeyboard.Activate(evt, this, copyDiagnostic));
            }

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-graph-node__header");
            Add(header);

            if (hasBackAffordance)
            {
                Label backHint = new Label("\u2039")
                {
                    tooltip = backTooltip
                };
                backHint.AddToClassList("dpi-graph-back-hint");
                backHint.AddToClassList("dpi-graph-back-hint--package");
                backHint.AddToClassList("dpi-graph-node__back-hint");
                backHint.pickingMode = PickingMode.Ignore;
                header.Add(backHint);
            }

            Image icon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon(node.IconKey),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList("dpi-graph-node__icon");
            header.Add(icon);

            VisualElement titleBlock = null;

            if (showTitle)
            {
                titleBlock = new VisualElement();
                titleBlock.AddToClassList("dpi-graph-node__title-block");
                header.Add(titleBlock);

                Label title = new Label(PackageGraphPresentationPolicy.GetGraphTitle(node.DisplayName, presentationLevel));
                title.AddToClassList("dpi-graph-node__title");
                titleBlock.Add(title);
            }

            Label statusMarker = new Label(GetStatusMarker(node.Status));
            statusMarker.AddToClassList("dpi-graph-node__status-icon");
            statusMarker.AddToClassList("dpi-graph-node__status-icon--" + GetStatusClass(node.Status));
            header.Add(statusMarker);

            if (showPackageId && titleBlock != null)
            {
                Label packageId = new Label(node.PackageId);
                packageId.AddToClassList("dpi-graph-node__package-id");
                titleBlock.Add(packageId);
            }

            if (showHierarchy && !string.IsNullOrWhiteSpace(categoryPathLabel))
            {
                Label categoryPath = new Label(categoryPathLabel);
                categoryPath.AddToClassList("dpi-graph-node__category-path");
                Add(categoryPath);
            }

            if (showBadges)
            {
                VisualElement badges = new VisualElement();
                badges.AddToClassList("dpi-graph-node__badges");

                if (showTypeBadge)
                {
                    badges.Add(CreateBadge(GetNodeTypeLabel(node.NodeType), "dpi-graph-node__badge--type"));
                }

                badges.Add(CreateBadge(GetStatusLabel(node), GetStatusBadgeClass(node.Status)));
                Add(badges);
            }

            if (!showFooter)
            {
                return;
            }

            VisualElement footer = new VisualElement();
            footer.AddToClassList("dpi-graph-node__footer");
            Add(footer);

            if (isFull)
            {
                Label channel = new Label(node.SelectedChannel.ToString());
                channel.AddToClassList("dpi-graph-node__channel");
                footer.Add(channel);
            }

            if (showActionShortcut)
            {
                Button actionButton = new Button(() =>
                {
                    if (actionsEnabled && interactionsEnabled)
                    {
                        packageAction?.Invoke(node.PackageDefinition, node.PrimaryAction);
                    }
                })
                {
                    text = node.PrimaryActionLabel
                };
                actionButton.AddToClassList("dpi-graph-node__action");
                actionButton.SetEnabled(actionsEnabled);
                actionButton.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                footer.Add(actionButton);
            }
        }

        private static string GetVisualModeClass(PackageGraphNodeVisualMode visualMode)
        {
            switch (visualMode)
            {
                case PackageGraphNodeVisualMode.Overview:
                    return "overview";
                case PackageGraphNodeVisualMode.Stack:
                    return "stack";
                default:
                    return "focus";
            }
        }

        private static string GetPresentationClass(PackageGraphNodePresentationLevel presentationLevel)
        {
            switch (presentationLevel)
            {
                case PackageGraphNodePresentationLevel.IconOnly:
                    return "icon-only";
                case PackageGraphNodePresentationLevel.Micro:
                    return "micro";
                case PackageGraphNodePresentationLevel.Compact:
                    return "compact";
                default:
                    return "full";
            }
        }

        private static bool IsGraphShortcutAction(PackageGraphNodeAction action)
        {
            switch (action)
            {
                case PackageGraphNodeAction.Install:
                case PackageGraphNodeAction.Update:
                case PackageGraphNodeAction.Reinstall:
                    return true;
                default:
                    return false;
            }
        }

        private static Label CreateBadge(string text, string className)
        {
            Label badge = new Label(text);
            badge.AddToClassList("dpi-graph-node__badge");
            badge.AddToClassList(className);
            return badge;
        }

        private static string GetNodeClass(PackageGraphNodeType nodeType)
        {
            switch (nodeType)
            {
                case PackageGraphNodeType.Tool:
                    return "tool";
                case PackageGraphNodeType.Companion:
                    return "companion";
                case PackageGraphNodeType.Suite:
                    return "suite";
                case PackageGraphNodeType.Integration:
                    return "integration";
                default:
                    return "core";
            }
        }

        private static string GetRingClass(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return "runtime";
                case PackageGraphLayoutRing.Integration:
                    return "integration";
                case PackageGraphLayoutRing.Suite:
                    return "suite";
                default:
                    return "infrastructure";
            }
        }

        public void SetPreviewState(bool previewed, bool hoverContext, bool hoverDimmed)
        {
            EnableInClassList("dpi-graph-node--previewed", previewed);
            EnableInClassList("dpi-graph-node--hover-context", hoverContext);
            EnableInClassList("dpi-graph-node--hover-dimmed", hoverDimmed);
        }

        internal bool HasKeyboardActivationForTests => _activationAction != null;

        internal void ActivateForTests()
        {
            _activationAction?.Invoke();
        }

        private static string GetNodeTypeLabel(PackageGraphNodeType nodeType)
        {
            switch (nodeType)
            {
                case PackageGraphNodeType.Tool:
                    return "Tool";
                case PackageGraphNodeType.Companion:
                    return "Library";
                case PackageGraphNodeType.Suite:
                    return "Suite";
                case PackageGraphNodeType.Integration:
                    return "Integration";
                default:
                    return "Library";
            }
        }

        private static string GetStatusLabel(PackageGraphNode node)
        {
            switch (node.Status)
            {
                case PackageGraphNodeStatus.Missing:
                    return "Missing dependency";
                case PackageGraphNodeStatus.NotInstalled:
                    return "Not installed";
                case PackageGraphNodeStatus.UpdateAvailable:
                    return "Update available";
                case PackageGraphNodeStatus.Checking:
                    return "Checking";
                case PackageGraphNodeStatus.Warning:
                    return string.IsNullOrWhiteSpace(node.UpdateStatusLabel)
                        ? "Attention"
                        : node.UpdateStatusLabel;
                default:
                    return "Installed";
            }
        }

        private static string GetStatusMarker(PackageGraphNodeStatus status)
        {
            switch (status)
            {
                case PackageGraphNodeStatus.Missing:
                    return "!";
                case PackageGraphNodeStatus.NotInstalled:
                    return "\u25CB";
                case PackageGraphNodeStatus.UpdateAvailable:
                    return "!";
                case PackageGraphNodeStatus.Checking:
                    return "...";
                case PackageGraphNodeStatus.Warning:
                    return "!";
                default:
                    return "\u2713";
            }
        }

        private static string GetStatusClass(PackageGraphNodeStatus status)
        {
            switch (status)
            {
                case PackageGraphNodeStatus.Missing:
                    return "missing";
                case PackageGraphNodeStatus.NotInstalled:
                    return "available";
                case PackageGraphNodeStatus.UpdateAvailable:
                    return "update";
                case PackageGraphNodeStatus.Checking:
                    return "checking";
                case PackageGraphNodeStatus.Warning:
                    return "warning";
                default:
                    return "installed";
            }
        }

        private static string GetStatusBadgeClass(PackageGraphNodeStatus status)
        {
            return "dpi-graph-node__badge--" + GetStatusClass(status);
        }

        private static string GetCompactTooltip(PackageGraphNode node, string relationshipTooltip)
        {
            string status = string.IsNullOrWhiteSpace(node.UpdateStatusLabel)
                ? GetStatusLabel(node)
                : node.UpdateStatusLabel;
            StringBuilder tooltip = new StringBuilder()
                .Append(node.DisplayName)
                .Append("\nStatus: ")
                .Append(status);

            if (!string.IsNullOrWhiteSpace(relationshipTooltip))
            {
                tooltip.Append('\n').Append(relationshipTooltip);
            }

            if (!node.IsRegistered && !string.IsNullOrWhiteSpace(node.Description))
            {
                tooltip.Append("\nDiagnostic: ").Append(node.Description.Trim());
            }

            return tooltip.ToString();
        }
    }
}
