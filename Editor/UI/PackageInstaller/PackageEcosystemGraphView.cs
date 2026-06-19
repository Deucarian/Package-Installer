using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphView : VisualElement
    {
        private const string InstalledStatusMarker = "\u2713";
        private const string NotInstalledStatusMarker = "\u25CB";

        private readonly PackageGraphCanvas _canvas;
        private readonly PackageGraphViewport _viewport;
        private readonly VisualElement _graphBody;
        private readonly VisualElement _categoryRail;
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
        private readonly PackageGraphHierarchyExitController _hierarchyExitController =
            new PackageGraphHierarchyExitController();
        private readonly PackageGraphHierarchyExitController _hierarchyEnterController =
            new PackageGraphHierarchyExitController();
        private PackageVisibilityFilterCounts _filterCounts =
            new PackageVisibilityFilterCounts(0, 0, 0, 0);
        private PackageGraphModel _currentGraph;
        private IReadOnlyCollection<string> _currentVisiblePackageIds = Array.Empty<string>();
        private PackageGraphSearchState _searchState = PackageGraphSearchState.Empty;
        private string _activeTopLevelGroupId = string.Empty;
        private string _hoveredTopLevelGroupId = string.Empty;
        private string _hierarchyEnterStableGroupId = string.Empty;
        private double _hierarchyEnterStableSince = double.NegativeInfinity;
        private PackageGraphHierarchyExitPreview _hierarchyExitPreview;
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
            _filterState = filterState ?? new PackageVisibilityFilterState();
            _packageSelected = packageSelected;
            _filterChanged = filterChanged;
            _selectionCleared = selectionCleared;
            _rootFocused = rootFocused;
            _groupFocused = groupFocused;

            _canvas = new PackageGraphCanvas(packageSelected, packageAction, selectionCleared, _rootFocused, _groupFocused);
            _canvas.InteractionsLockedChanged += _ => UpdateCategoryRailState();
            _canvas.ActiveHoverGroupChanged += groupId =>
            {
                _hoveredTopLevelGroupId = ResolveTopLevelGroupId(_currentGraph, groupId);
                UpdateCategoryRailState();
            };
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
            _viewport.HierarchyExitWheel += HandleHierarchyExitWheel;
            _viewport.HierarchyEnterWheel += HandleHierarchyEnterWheel;
            _viewport.SetContent(_canvas);

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-ecosystem-graph__header");
            Add(header);

            VisualElement filterRow = new VisualElement();
            filterRow.AddToClassList("dpi-ecosystem-graph__filter-row");
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
                    ResetHierarchyExitPreview();
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
                        ResetHierarchyExitPreview();
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
                        ResetHierarchyExitPreview();
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

            _visibleCountLabel = new Label("0 visible");
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

            _categoryRail = new VisualElement();
            _categoryRail.AddToClassList("dpi-category-rail");
            _categoryRail.AddToClassList("dpi-category-rail--hidden");
            _graphBody.Add(_categoryRail);

            _graphBody.Add(_viewport);

            _emptyState = new VisualElement();
            _emptyState.AddToClassList("dpi-ecosystem-graph__empty-state");
            _emptyStateTitle = new Label();
            _emptyStateTitle.AddToClassList("dpi-ecosystem-graph__empty-title");
            _emptyState.Add(_emptyStateTitle);
            _emptyStateActionButton = new Button(ClearFilters);
            _emptyStateActionButton.AddToClassList("dpi-ecosystem-graph__empty-action");
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
            ResetHierarchyExitPreview();
            string previousLayoutFocusPackageId = _canvas.LayoutFocusPackageId;
            string previousLayoutFocusGroupId = _canvas.LayoutFocusGroupId;
            bool shouldForceInitialFrame = !_hasAppliedGraphFrame && graph != null && graph.Nodes.Count > 0;
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
            _currentVisiblePackageIds = visiblePackageIds == null
                ? (IReadOnlyCollection<string>)(graph != null
                    ? graph.Nodes.Select(node => node.PackageId).ToArray()
                    : Array.Empty<string>())
                : visiblePackageIds.ToArray();
            _searchState = searchState ?? PackageGraphSearchState.Empty;
            _canvas.SetGraph(
                graph,
                selectedPackageId,
                focusedPackageId,
                focusedGroupId,
                actionsEnabled,
                visiblePackageIds,
                _searchState);
            bool layoutTargetChanged =
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
            UpdateLegend(_canvas.LayoutMode);
            _hasAppliedGraphFrame = _hasAppliedGraphFrame || shouldForceInitialFrame;
            _filterCounts = filterCounts ?? PackageVisibilityFilter.CalculateCounts(graph, _filterState);
            _hiddenRelatedCount = Math.Max(0, hiddenRelatedCount);
            UpdateFilterControls();
            UpdateBreadcrumbs(graph, focusedGroupId, focusedPackageId);
            UpdateCategoryRail(graph, focusedGroupId, focusedPackageId);

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
        }

        private void FitCurrentContext()
        {
            ResetHierarchyExitPreview();

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
            ResetHierarchyExitPreview();
            _viewport.ResetZoom(_canvas.GetActiveCenter());
        }

        private void CenterCurrentContext()
        {
            ResetHierarchyExitPreview();
            _viewport.CenterOn(_canvas.GetActiveCenter());
        }

        private void NavigateToRoot()
        {
            ResetHierarchyExitPreview();
            _rootFocused?.Invoke();
        }

        private void NavigateToGroup(PackageGraphGroup group)
        {
            ResetHierarchyExitPreview();
            _groupFocused?.Invoke(group);
        }

        private void NavigateBackOneLevel()
        {
            ResetHierarchyExitPreview();
            _selectionCleared?.Invoke();
        }

        private void SelectPackage(PackageDefinition packageDefinition)
        {
            ResetHierarchyExitPreview();
            _packageSelected?.Invoke(packageDefinition);
        }

        private bool HandleHierarchyExitWheel(PackageGraphHierarchyExitWheelEvent evt)
        {
            if (evt == null)
            {
                return false;
            }

            bool hadPreview = _hierarchyExitPreview != null;
            bool canExit = hadPreview ||
                           (!_viewport.IsCameraTransitionActive &&
                            !_canvas.InteractionsLocked &&
                            TryBeginHierarchyExitPreview());
            PackageGraphHierarchyExitResult result = _hierarchyExitController.ApplyWheel(
                evt.WheelDeltaY,
                canExit,
                evt.AtNormalMinimum,
                EditorApplication.timeSinceStartup);

            if (!result.Consumed)
            {
                if (!hadPreview && _hierarchyExitPreview != null)
                {
                    ResetHierarchyExitPreview();
                }

                return false;
            }

            if (result.Cancelled)
            {
                ResetHierarchyExitPreview();
                return true;
            }

            if (result.Committed)
            {
                ApplyHierarchyExitPreview(1f);
                CommitHierarchyExitPreview();
                return true;
            }

            ApplyHierarchyExitPreview(result.Progress);
            return true;
        }

        private bool HandleHierarchyEnterWheel(PackageGraphHierarchyEnterWheelEvent evt)
        {
            if (evt == null)
            {
                return false;
            }

            double currentTime = EditorApplication.timeSinceStartup;
            string hoverGroupId = _canvas.DirectHoverGroupId;
            bool hadPreview = _hierarchyExitPreview != null && _hierarchyEnterController.IsActive;

            if (!UpdateHierarchyEnterStableHover(hoverGroupId, currentTime, hadPreview))
            {
                if (hadPreview)
                {
                    ResetHierarchyExitPreview();
                    return true;
                }

                return false;
            }

            bool hoverStable = currentTime - _hierarchyEnterStableSince >= 0.12d;
            bool canEnter = hadPreview ||
                            (hoverStable &&
                             !_viewport.IsCameraTransitionActive &&
                             !_canvas.InteractionsLocked &&
                             TryBeginHierarchyEnterPreview(hoverGroupId));
            PackageGraphHierarchyExitResult result = _hierarchyEnterController.ApplyWheel(
                -evt.WheelDeltaY,
                canEnter,
                evt.AtNormalMaximum,
                currentTime);

            if (!result.Consumed)
            {
                if (!hadPreview && _hierarchyExitPreview != null)
                {
                    ResetHierarchyExitPreview();
                }

                return false;
            }

            if (result.Cancelled)
            {
                ResetHierarchyExitPreview();
                return true;
            }

            if (result.Committed)
            {
                ApplyHierarchyExitPreview(1f);
                CommitHierarchyEnterPreview();
                return true;
            }

            ApplyHierarchyExitPreview(result.Progress);
            return true;
        }

        private bool UpdateHierarchyEnterStableHover(
            string hoverGroupId,
            double currentTime,
            bool hadPreview)
        {
            if (string.IsNullOrWhiteSpace(hoverGroupId))
            {
                _hierarchyEnterStableGroupId = string.Empty;
                _hierarchyEnterStableSince = double.NegativeInfinity;
                return false;
            }

            if (hadPreview &&
                _hierarchyExitPreview != null &&
                !string.Equals(
                    _hierarchyExitPreview.Target.GroupId,
                    hoverGroupId,
                    StringComparison.OrdinalIgnoreCase))
            {
                _hierarchyEnterStableGroupId = hoverGroupId;
                _hierarchyEnterStableSince = currentTime;
                return false;
            }

            if (!string.Equals(_hierarchyEnterStableGroupId, hoverGroupId, StringComparison.OrdinalIgnoreCase))
            {
                _hierarchyEnterStableGroupId = hoverGroupId;
                _hierarchyEnterStableSince = currentTime;
                _hierarchyEnterController.Cancel();
            }

            return true;
        }

        private bool TryBeginHierarchyEnterPreview(string groupId)
        {
            if (_hierarchyExitPreview != null)
            {
                return true;
            }

            if (!TryCreateHierarchyEnterTarget(groupId, out PackageGraphHierarchyExitTarget target))
            {
                return false;
            }

            if (!_canvas.TryGetTransitionAnchorCenter(target.Anchor, out Vector2 sourceAnchorWorld))
            {
                sourceAnchorWorld = _canvas.GetActiveCenter();
            }

            if (!TryGetLayoutAnchorCenter(target.Layout, target.Anchor, out Vector2 targetAnchorWorld))
            {
                targetAnchorWorld = target.Layout != null
                    ? target.Layout.ActiveCenter
                    : _canvas.GetActiveCenter();
            }

            PackageGraphCameraState sourceCamera = _viewport.GetCameraState();
            PackageGraphCameraState targetCamera = _viewport.CalculateFitCamera(target.Bounds, target.Mode);
            _hierarchyExitPreview = new PackageGraphHierarchyExitPreview(
                target,
                sourceCamera,
                targetCamera,
                sourceAnchorWorld,
                targetAnchorWorld,
                sourceCamera.WorldToViewport(sourceAnchorWorld),
                targetCamera.WorldToViewport(targetAnchorWorld));

            if (!_canvas.BeginHierarchyExitPreview(target.Layout))
            {
                _hierarchyExitPreview = null;
                return false;
            }

            _viewport.SetHierarchyEnterPreviewActive(true);
            return true;
        }

        private bool TryCreateHierarchyEnterTarget(
            string groupId,
            out PackageGraphHierarchyExitTarget target)
        {
            target = null;

            if (_currentGraph == null ||
                string.IsNullOrWhiteSpace(groupId) ||
                _canvas.LayoutMode == PackageGraphLayoutMode.Focus ||
                _viewport.ViewportSize.x <= 1f ||
                _viewport.ViewportSize.y <= 1f)
            {
                return false;
            }

            PackageGraphModel visibleGraph = CreateCurrentVisibleGraph();

            string activeGroupId = _canvas.LayoutMode == PackageGraphLayoutMode.GroupFocus
                ? _canvas.LayoutFocusGroupId
                : string.Empty;

            if (!CanEnterHierarchy(visibleGraph, groupId, _canvas.LayoutMode, activeGroupId, out PackageGraphGroup group))
            {
                return false;
            }

            PackageGraphNodePresentationLevel presentation =
                PackageGraphPresentationPolicy.ResolveForZoom(
                    PackageGraphLayoutMode.GroupFocus,
                    _viewport.Zoom,
                    PackageGraphPresentationPolicy.GetDefaultForMode(PackageGraphLayoutMode.GroupFocus));
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                visibleGraph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                group.Id,
                _viewport.ViewportSize,
                presentation);
            Rect bounds = PackageGraphActiveLayoutBounds.Calculate(layout);
            target = new PackageGraphHierarchyExitTarget(
                PackageGraphLayoutMode.GroupFocus,
                group.Id,
                group,
                new PackageGraphTransitionAnchor(PackageGraphTransitionAnchorKind.Group, group.Id),
                layout,
                bounds);
            return true;
        }

        internal static bool CanEnterHierarchyForTests(
            PackageGraphModel visibleGraph,
            string groupId,
            PackageGraphLayoutMode layoutMode,
            string activeGroupId)
        {
            return CanEnterHierarchy(visibleGraph, groupId, layoutMode, activeGroupId, out _);
        }

        private static bool CanEnterHierarchy(
            PackageGraphModel visibleGraph,
            string groupId,
            PackageGraphLayoutMode layoutMode,
            string activeGroupId,
            out PackageGraphGroup group)
        {
            group = null;

            if (visibleGraph == null ||
                string.IsNullOrWhiteSpace(groupId) ||
                layoutMode == PackageGraphLayoutMode.Focus ||
                !visibleGraph.TryGetGroup(groupId, out group) ||
                !IsEligibleHierarchyEnterGroup(visibleGraph, group))
            {
                return false;
            }

            string safeActiveGroupId = activeGroupId ?? string.Empty;

            if (string.Equals(safeActiveGroupId, group.Id, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (layoutMode == PackageGraphLayoutMode.Overview)
            {
                return string.IsNullOrWhiteSpace(group.ParentGroupId);
            }

            return layoutMode == PackageGraphLayoutMode.GroupFocus &&
                   string.Equals(group.ParentGroupId, safeActiveGroupId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEligibleHierarchyEnterGroup(
            PackageGraphModel graph,
            PackageGraphGroup group)
        {
            if (graph == null || group == null)
            {
                return false;
            }

            bool hasDirectPackage = graph.Nodes.Any(node =>
                node != null &&
                string.Equals(node.GroupId, group.Id, StringComparison.OrdinalIgnoreCase));
            bool hasDirectGroup = graph.Groups.Any(candidate =>
                candidate != null &&
                string.Equals(candidate.ParentGroupId, group.Id, StringComparison.OrdinalIgnoreCase));
            return hasDirectPackage || hasDirectGroup;
        }

        private bool TryBeginHierarchyExitPreview()
        {
            if (_hierarchyExitPreview != null)
            {
                return true;
            }

            if (!TryCreateHierarchyExitTarget(out PackageGraphHierarchyExitTarget target))
            {
                return false;
            }

            if (!_canvas.TryGetTransitionAnchorCenter(target.Anchor, out Vector2 sourceAnchorWorld))
            {
                sourceAnchorWorld = _canvas.GetActiveCenter();
            }

            if (!TryGetLayoutAnchorCenter(target.Layout, target.Anchor, out Vector2 targetAnchorWorld))
            {
                targetAnchorWorld = target.Layout != null
                    ? target.Layout.ActiveCenter
                    : _canvas.GetActiveCenter();
            }

            PackageGraphCameraState sourceCamera = _viewport.GetCameraState();
            PackageGraphCameraState targetCamera = _viewport.CalculateFitCamera(target.Bounds, target.Mode);
            _hierarchyExitPreview = new PackageGraphHierarchyExitPreview(
                target,
                sourceCamera,
                targetCamera,
                sourceAnchorWorld,
                targetAnchorWorld,
                sourceCamera.WorldToViewport(sourceAnchorWorld),
                targetCamera.WorldToViewport(targetAnchorWorld));

            if (!_canvas.BeginHierarchyExitPreview(target.Layout))
            {
                _hierarchyExitPreview = null;
                return false;
            }

            _viewport.SetHierarchyExitPreviewActive(true);
            return true;
        }

        private bool TryCreateHierarchyExitTarget(out PackageGraphHierarchyExitTarget target)
        {
            target = null;

            if (_currentGraph == null ||
                _canvas.LayoutMode == PackageGraphLayoutMode.Overview ||
                _viewport.ViewportSize.x <= 1f ||
                _viewport.ViewportSize.y <= 1f)
            {
                return false;
            }

            PackageGraphModel visibleGraph = CreateCurrentVisibleGraph();
            PackageGraphLayoutMode targetMode;
            string targetGroupId = string.Empty;
            PackageGraphGroup targetGroup = null;
            PackageGraphTransitionAnchor anchor;

            if (_canvas.LayoutMode == PackageGraphLayoutMode.Focus)
            {
                string packageId = _canvas.LayoutFocusPackageId;

                if (string.IsNullOrWhiteSpace(packageId) ||
                    !visibleGraph.TryGetNode(packageId, out PackageGraphNode node) ||
                    !visibleGraph.TryGetGroup(node.GroupId, out targetGroup))
                {
                    return false;
                }

                targetMode = PackageGraphLayoutMode.GroupFocus;
                targetGroupId = targetGroup.Id;
                anchor = new PackageGraphTransitionAnchor(PackageGraphTransitionAnchorKind.Package, packageId);
            }
            else
            {
                string groupId = _canvas.LayoutFocusGroupId;

                if (string.IsNullOrWhiteSpace(groupId) ||
                    !visibleGraph.TryGetGroup(groupId, out PackageGraphGroup group))
                {
                    return false;
                }

                anchor = new PackageGraphTransitionAnchor(PackageGraphTransitionAnchorKind.Group, groupId);

                if (string.IsNullOrWhiteSpace(group.ParentGroupId))
                {
                    targetMode = PackageGraphLayoutMode.Overview;
                }
                else if (visibleGraph.TryGetGroup(group.ParentGroupId, out targetGroup))
                {
                    targetMode = PackageGraphLayoutMode.GroupFocus;
                    targetGroupId = targetGroup.Id;
                }
                else
                {
                    targetMode = PackageGraphLayoutMode.Overview;
                }
            }

            PackageGraphNodePresentationLevel presentation =
                PackageGraphPresentationPolicy.ResolveForZoom(
                    targetMode,
                    _viewport.Zoom,
                    PackageGraphPresentationPolicy.GetDefaultForMode(targetMode));
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                visibleGraph,
                targetMode,
                string.Empty,
                targetGroupId,
                _viewport.ViewportSize,
                presentation);
            Rect bounds = PackageGraphActiveLayoutBounds.Calculate(layout);
            target = new PackageGraphHierarchyExitTarget(
                targetMode,
                targetGroupId,
                targetGroup,
                anchor,
                layout,
                bounds);
            return true;
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
                        center = groupNode.Rect.center;
                        return true;
                    }

                    break;
                default:
                    center = layout.HubRect.center;
                    return true;
            }

            return false;
        }

        private void ApplyHierarchyExitPreview(float progress)
        {
            if (_hierarchyExitPreview == null)
            {
                return;
            }

            float clampedProgress = Mathf.Clamp01(progress);
            PackageGraphCameraState camera = PackageGraphTransition.EvaluateAnchoredCamera(
                _hierarchyExitPreview.SourceCamera,
                _hierarchyExitPreview.TargetCamera,
                _hierarchyExitPreview.SourceAnchorWorld,
                _hierarchyExitPreview.TargetAnchorWorld,
                _hierarchyExitPreview.SourceAnchorScreen,
                _hierarchyExitPreview.TargetAnchorScreen,
                clampedProgress);
            _canvas.SetHierarchyExitPreview(clampedProgress);
            _viewport.SetHierarchyExitPreviewActive(clampedProgress > 0.001f);
            _viewport.ApplyPreviewCamera(camera);
        }

        private void CommitHierarchyExitPreview()
        {
            if (_hierarchyExitPreview == null)
            {
                return;
            }

            PackageGraphHierarchyExitTarget target = _hierarchyExitPreview.Target;
            _hierarchyExitPreview = null;
            _hierarchyExitController.Commit(EditorApplication.timeSinceStartup);
            _canvas.EndHierarchyExitPreview(restoreSource: false);
            _viewport.SetHierarchyExitPreviewActive(false);

            if (target.Mode == PackageGraphLayoutMode.Overview)
            {
                _rootFocused?.Invoke();
            }
            else
            {
                _groupFocused?.Invoke(target.Group);
            }
        }

        private void CommitHierarchyEnterPreview()
        {
            if (_hierarchyExitPreview == null)
            {
                return;
            }

            PackageGraphHierarchyExitTarget target = _hierarchyExitPreview.Target;
            _hierarchyExitPreview = null;
            _hierarchyEnterController.Commit(EditorApplication.timeSinceStartup);
            _canvas.EndHierarchyExitPreview(restoreSource: false);
            _viewport.SetHierarchyEnterPreviewActive(false);

            if (target.Group != null)
            {
                _groupFocused?.Invoke(target.Group);
            }
        }

        private void ResetHierarchyExitPreview(bool restoreCamera = true)
        {
            if (_hierarchyExitPreview != null)
            {
                PackageGraphCameraState sourceCamera = _hierarchyExitPreview.SourceCamera;
                _canvas.EndHierarchyExitPreview(restoreCamera);

                if (restoreCamera)
                {
                    _viewport.ApplyPreviewCamera(sourceCamera);
                }
            }

            _hierarchyExitPreview = null;
            _hierarchyExitController.Cancel();
            _hierarchyEnterController.Cancel();
            _hierarchyEnterStableGroupId = string.Empty;
            _hierarchyEnterStableSince = double.NegativeInfinity;
            _viewport.SetHierarchyExitPreviewActive(false);
            _viewport.SetHierarchyEnterPreviewActive(false);
        }

        private sealed class PackageGraphHierarchyExitTarget
        {
            public PackageGraphHierarchyExitTarget(
                PackageGraphLayoutMode mode,
                string groupId,
                PackageGraphGroup group,
                PackageGraphTransitionAnchor anchor,
                PackageGraphLayoutResult layout,
                Rect bounds)
            {
                Mode = mode;
                GroupId = groupId ?? string.Empty;
                Group = group;
                Anchor = anchor;
                Layout = layout;
                Bounds = bounds;
            }

            public PackageGraphLayoutMode Mode { get; }

            public string GroupId { get; }

            public PackageGraphGroup Group { get; }

            public PackageGraphTransitionAnchor Anchor { get; }

            public PackageGraphLayoutResult Layout { get; }

            public Rect Bounds { get; }
        }

        private sealed class PackageGraphHierarchyExitPreview
        {
            public PackageGraphHierarchyExitPreview(
                PackageGraphHierarchyExitTarget target,
                PackageGraphCameraState sourceCamera,
                PackageGraphCameraState targetCamera,
                Vector2 sourceAnchorWorld,
                Vector2 targetAnchorWorld,
                Vector2 sourceAnchorScreen,
                Vector2 targetAnchorScreen)
            {
                Target = target;
                SourceCamera = sourceCamera;
                TargetCamera = targetCamera;
                SourceAnchorWorld = sourceAnchorWorld;
                TargetAnchorWorld = targetAnchorWorld;
                SourceAnchorScreen = sourceAnchorScreen;
                TargetAnchorScreen = targetAnchorScreen;
            }

            public PackageGraphHierarchyExitTarget Target { get; }

            public PackageGraphCameraState SourceCamera { get; }

            public PackageGraphCameraState TargetCamera { get; }

            public Vector2 SourceAnchorWorld { get; }

            public Vector2 TargetAnchorWorld { get; }

            public Vector2 SourceAnchorScreen { get; }

            public Vector2 TargetAnchorScreen { get; }
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
                ResetHierarchyExitPreview();
                _searchField.SetValueWithoutNotify(string.Empty);
                _filterChanged?.Invoke();
            }

            UpdateFilterControls();
        }

        private void ClearFilters()
        {
            if (_filterState.Reset())
            {
                ResetHierarchyExitPreview();
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

        private void UpdateCategoryRail(
            PackageGraphModel graph,
            string focusedGroupId,
            string focusedPackageId)
        {
            if (_categoryRail == null)
            {
                return;
            }

            _categoryRail.Clear();
            _categoryRail.EnableInClassList("dpi-category-rail--hidden", false);
            _categoryRail.style.display = DisplayStyle.Flex;
            _activeTopLevelGroupId = ResolveActiveTopLevelGroupId(graph, focusedGroupId, focusedPackageId);
            _categoryRail.Add(CreateOverviewRailItem());

            foreach (PackageGraphGroup group in GetTopLevelGroups(graph))
            {
                _categoryRail.Add(CreateCategoryRailItem(graph, group));
            }

            UpdateCategoryRailState();
        }

        private VisualElement CreateOverviewRailItem()
        {
            bool active = IsOverviewRailActive();
            Button item = new Button(() =>
            {
                if (_canvas.InteractionsLocked)
                {
                    return;
                }

                if (IsOverviewRailActive())
                {
                    FitCurrentContext();
                    return;
                }

                NavigateToRoot();
            })
            {
                name = "category-rail-overview",
                tooltip = active ? "Refit ecosystem overview" : "Navigate to ecosystem overview"
            };
            item.AddToClassList("dpi-category-rail__item");
            item.AddToClassList("dpi-category-rail__item--overview");
            item.EnableInClassList("dpi-category-rail__item--active", active);

            VisualElement symbol = new VisualElement();
            symbol.AddToClassList("dpi-category-rail__symbol");
            symbol.style.width = 30f;
            symbol.style.height = 30f;
            Image icon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon("package-installer"),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList("dpi-category-rail__icon");
            symbol.Add(icon);
            item.Add(symbol);

            VisualElement textBlock = new VisualElement();
            textBlock.AddToClassList("dpi-category-rail__text");
            Label title = new Label("Deucarian");
            title.AddToClassList("dpi-category-rail__label");
            textBlock.Add(title);
            Label count = new Label("Overview");
            count.AddToClassList("dpi-category-rail__count");
            textBlock.Add(count);
            item.Add(textBlock);
            return item;
        }

        private VisualElement CreateCategoryRailItem(PackageGraphModel graph, PackageGraphGroup group)
        {
            PackageGraphNode[] descendants = GetDescendantPackageNodes(graph, group.Id).ToArray();
            int updateCount = descendants.Count(node => node.Status == PackageGraphNodeStatus.UpdateAvailable);
            int attentionCount = descendants.Count(node =>
                node.Status == PackageGraphNodeStatus.Missing ||
                node.Status == PackageGraphNodeStatus.Warning);
            bool active = string.Equals(group.Id, _activeTopLevelGroupId, StringComparison.OrdinalIgnoreCase);
            bool hovered = string.Equals(group.Id, _hoveredTopLevelGroupId, StringComparison.OrdinalIgnoreCase);

            Button item = new Button(() =>
            {
                if (_canvas.InteractionsLocked)
                {
                    return;
                }

                if (string.Equals(group.Id, _activeTopLevelGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    FitCurrentContext();
                    return;
                }

                NavigateToGroup(group);
            })
            {
                name = "category-rail-" + group.Id,
                tooltip = active ? "Refit " + group.DisplayName : "Navigate to " + group.DisplayName
            };
            item.AddToClassList("dpi-category-rail__item");
            item.EnableInClassList("dpi-category-rail__item--active", active);
            item.EnableInClassList("dpi-category-rail__item--hover-context", hovered);
            item.EnableInClassList("dpi-category-rail__item--attention", updateCount > 0 || attentionCount > 0);
            item.RegisterCallback<MouseEnterEvent>(_ => SetRailCategoryHover(group.Id));
            item.RegisterCallback<MouseLeaveEvent>(_ => ClearRailCategoryHover(group.Id));
            item.RegisterCallback<FocusInEvent>(_ => SetRailCategoryHover(group.Id));
            item.RegisterCallback<FocusOutEvent>(_ => ClearRailCategoryHover(group.Id));

            VisualElement symbol = new VisualElement();
            symbol.AddToClassList("dpi-category-rail__symbol");
            symbol.style.width = 30f;
            symbol.style.height = 30f;
            Image icon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon(group.IconKey),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList("dpi-category-rail__icon");
            symbol.Add(icon);

            if (updateCount > 0 || attentionCount > 0)
            {
                Label attention = new Label("!");
                attention.AddToClassList("dpi-category-rail__attention");
                symbol.Add(attention);
            }

            item.Add(symbol);

            VisualElement textBlock = new VisualElement();
            textBlock.AddToClassList("dpi-category-rail__text");
            Label title = new Label(group.DisplayName);
            title.AddToClassList("dpi-category-rail__label");
            textBlock.Add(title);
            Label count = new Label(descendants.Length + " packages");
            count.AddToClassList("dpi-category-rail__count");
            textBlock.Add(count);
            item.Add(textBlock);
            return item;
        }

        private void SetRailCategoryHover(string groupId)
        {
            if (_canvas.InteractionsLocked || _currentGraph == null)
            {
                return;
            }

            ApplyCategoryHover(groupId, respectInteractionLock: true);
        }

        private void ClearRailCategoryHover(string groupId)
        {
            if (_canvas.InteractionsLocked)
            {
                return;
            }

            _canvas.ClearExternalHoverGroup(groupId);
            _hoveredTopLevelGroupId = string.Empty;
            UpdateCategoryRailState();
        }

        internal void PreviewCategoryHoverForTests(string groupId)
        {
            ApplyCategoryHover(groupId, respectInteractionLock: false);
        }

        internal void PreviewPackageHoverForTests(string packageId)
        {
            _canvas.SetPreviewPackageForTests(packageId);
        }

        private void ApplyCategoryHover(string groupId, bool respectInteractionLock)
        {
            if (_currentGraph == null)
            {
                return;
            }

            _hoveredTopLevelGroupId = ResolveTopLevelGroupId(_currentGraph, groupId);
            _canvas.SetExternalHoverGroup(groupId, respectInteractionLock);
            UpdateCategoryRailState();
        }

        private void UpdateCategoryRailState()
        {
            if (_categoryRail == null)
            {
                return;
            }

            bool locked = _canvas.InteractionsLocked;
            _categoryRail.EnableInClassList("dpi-category-rail--locked", locked);

            foreach (VisualElement element in _categoryRail.Children())
            {
                if (element is Button button)
                {
                    button.SetEnabled(!locked);
                }

                string groupId = GetRailGroupId(element);
                element.EnableInClassList(
                    "dpi-category-rail__item--active",
                    IsRailItemActive(groupId));
                element.EnableInClassList(
                    "dpi-category-rail__item--hover-context",
                    string.Equals(groupId, _hoveredTopLevelGroupId, StringComparison.OrdinalIgnoreCase));
            }
        }

        private bool IsRailItemActive(string groupId)
        {
            return IsOverviewRailId(groupId)
                ? IsOverviewRailActive()
                : string.Equals(groupId, _activeTopLevelGroupId, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOverviewRailActive()
        {
            return _canvas.LayoutMode == PackageGraphLayoutMode.Overview &&
                   string.IsNullOrWhiteSpace(_activeTopLevelGroupId);
        }

        private static bool IsOverviewRailId(string groupId)
        {
            return string.Equals(groupId, "overview", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRailGroupId(VisualElement element)
        {
            const string prefix = "category-rail-";
            return element != null &&
                   !string.IsNullOrWhiteSpace(element.name) &&
                   element.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? element.name.Substring(prefix.Length)
                : string.Empty;
        }

        private static string ResolveActiveTopLevelGroupId(
            PackageGraphModel graph,
            string focusedGroupId,
            string focusedPackageId)
        {
            string groupId = focusedGroupId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(groupId) &&
                graph != null &&
                !string.IsNullOrWhiteSpace(focusedPackageId) &&
                graph.TryGetNode(focusedPackageId, out PackageGraphNode node))
            {
                groupId = node.GroupId;
            }

            return ResolveTopLevelGroupId(graph, groupId);
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

        private static IEnumerable<PackageGraphGroup> GetTopLevelGroups(PackageGraphModel graph)
        {
            return graph == null
                ? Enumerable.Empty<PackageGraphGroup>()
                : graph.Groups
                    .Where(group => group != null && string.IsNullOrWhiteSpace(group.ParentGroupId))
                    .OrderBy(group => group.SortOrder)
                    .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<PackageGraphNode> GetDescendantPackageNodes(
            PackageGraphModel graph,
            string groupId)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return Enumerable.Empty<PackageGraphNode>();
            }

            HashSet<string> groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectDescendantGroupIds(graph, groupId, groupIds);
            return graph.Nodes.Where(node => node != null && groupIds.Contains(node.GroupId));
        }

        private static void CollectDescendantGroupIds(
            PackageGraphModel graph,
            string groupId,
            ISet<string> groupIds)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId) || !groupIds.Add(groupId))
            {
                return;
            }

            foreach (PackageGraphGroup childGroup in graph.Groups.Where(group =>
                         group != null &&
                         string.Equals(group.ParentGroupId, groupId, StringComparison.OrdinalIgnoreCase)))
            {
                CollectDescendantGroupIds(graph, childGroup.Id, groupIds);
            }
        }

        private void ShowAllPackages()
        {
            if (_filterState.Set(string.Empty, showInstalled: true, showNotInstalled: true))
            {
                ResetHierarchyExitPreview();
                _searchField.SetValueWithoutNotify(string.Empty);
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
                ? (_searchState != null ? _searchState.DirectMatchCount : 0) + " direct matches"
                : counts.VisibleCount + " visible";
            _hiddenRelatedLabel.text = !_filterState.HasSearch && _hiddenRelatedCount > 0
                ? _hiddenRelatedCount + " related hidden by filters"
                : string.Empty;
            _hiddenRelatedLabel.style.display = !string.IsNullOrWhiteSpace(_hiddenRelatedLabel.text)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            UpdateEmptyState(counts);
        }

        private void UpdateEmptyState(PackageVisibilityFilterCounts counts)
        {
            bool noSearchResults =
                _filterState.HasSearch &&
                _searchState != null &&
                _searchState.DirectMatchCount == 0;
            bool searchMatchesOnlyFilteredPackages =
                _filterState.HasSearch &&
                _searchState != null &&
                _searchState.DirectMatchCount > 0 &&
                _searchState.ContextPackageCount == 0;
            bool showEmptyState =
                counts != null &&
                counts.TotalCount > 0 &&
                (!_filterState.HasAnyVisibilityEnabled ||
                 noSearchResults ||
                 searchMatchesOnlyFilteredPackages ||
                 (!_filterState.HasSearch && counts.VisibleCount == 0));
            _emptyState.style.display = showEmptyState ? DisplayStyle.Flex : DisplayStyle.None;

            if (!showEmptyState)
            {
                return;
            }

            if (!_filterState.HasAnyVisibilityEnabled)
            {
                _emptyStateTitle.text = "No package visibility filters selected.";
                _emptyStateActionButton.text = "Show all packages";
                _emptyStateActionButton.tooltip = "Enable Installed and Not installed packages.";
                _emptyStateActionButton.clicked -= ClearFilters;
                _emptyStateActionButton.clicked -= ShowAllPackages;
                _emptyStateActionButton.clicked -= ClearSearch;
                _emptyStateActionButton.clicked += ShowAllPackages;
                return;
            }

            if (noSearchResults)
            {
                _emptyStateTitle.text = _filterState.ShowInstalled && _filterState.ShowNotInstalled
                    ? "No categories or packages match this search."
                    : "No visible categories or packages match this search with the active visibility filters.";
                _emptyStateActionButton.text = "Clear search";
                _emptyStateActionButton.tooltip = "Clear the structural category/package search.";
                _emptyStateActionButton.clicked -= ClearFilters;
                _emptyStateActionButton.clicked -= ShowAllPackages;
                _emptyStateActionButton.clicked -= ClearSearch;
                _emptyStateActionButton.clicked += ClearSearch;
                return;
            }

            _emptyStateTitle.text = _filterState.HasSearch || searchMatchesOnlyFilteredPackages
                ? "No packages pass the active visibility filters for this search."
                : "No packages match the current filters.";
            _emptyStateActionButton.text = "Clear filters";
            _emptyStateActionButton.tooltip = "Clear search and show all package visibility states.";
            _emptyStateActionButton.clicked -= ClearFilters;
            _emptyStateActionButton.clicked -= ShowAllPackages;
            _emptyStateActionButton.clicked -= ClearSearch;
            _emptyStateActionButton.clicked += ClearFilters;
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
                _legend.Add(CreateLegendItem("!", "Attention", "dpi-graph-legend__line--warning"));
                return;
            }

            if (layoutMode == PackageGraphLayoutMode.GroupFocus)
            {
                _legend.Add(CreateLegendItem("Group", "Group", "dpi-graph-legend__line--group"));
                _legend.Add(CreateLegendItem("Pkg", "Package", "dpi-graph-legend__line--package"));
                _legend.Add(CreateLegendItem("Line", "Structural membership", "dpi-graph-legend__line--membership", "Structural group membership, not a dependency"));
                _legend.Add(CreateLegendItem("!", "Attention", "dpi-graph-legend__line--warning"));
                return;
            }

            _legend.Add(CreateLegendItem("Root", "Deucarian root", "dpi-graph-legend__line--root"));
            _legend.Add(CreateLegendItem("Group", "Group", "dpi-graph-legend__line--group"));
            _legend.Add(CreateLegendItem("Pkg", "Package", "dpi-graph-legend__line--package"));
            _legend.Add(CreateLegendItem(InstalledStatusMarker, "Installed", "dpi-graph-legend__line--installed"));
            _legend.Add(CreateLegendItem(NotInstalledStatusMarker, "Not installed", "dpi-graph-legend__line--available"));
            _legend.Add(CreateLegendItem("!", "Attention", "dpi-graph-legend__line--warning"));
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

            if (_canvas.LayoutMode != PackageGraphLayoutMode.Overview)
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
            if (packageNode == null || packageNode.PackageDefinition == null)
            {
                PopulateCanvasContextMenu(menu);
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
                ResetHierarchyExitPreview();
                _viewport.CenterOn(PackageGraphLayout.GraphCenter);
            });
            menu.AddItem(new GUIContent("100%"), false, () =>
            {
                ResetHierarchyExitPreview();
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
        private bool _hierarchyExitPreviewActive;
        private bool _hierarchyEnterPreviewActive;
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

        public PackageGraphViewport(Action selectionCleared)
        {
            _selectionCleared = selectionCleared;
            name = "ecosystem-graph-viewport";
            AddToClassList("dpi-ecosystem-graph__viewport");
            focusable = true;

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

        public event Func<PackageGraphHierarchyExitWheelEvent, bool> HierarchyExitWheel;

        public event Func<PackageGraphHierarchyEnterWheelEvent, bool> HierarchyEnterWheel;

        public float Zoom => _zoom;

        public Vector2 Pan => _pan;

        public bool IsCameraTransitionActive => _cameraTransitionActive;

        public Vector2 ViewportSize => HasViewportSize()
            ? new Vector2(contentRect.width, contentRect.height)
            : Vector2.zero;

        internal float EffectiveMinZoomForTests => GetMinZoom();

        internal float RequiredFitZoomForTests => _requiredFitZoom;

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

        public void SetHierarchyExitPreviewActive(bool active)
        {
            _hierarchyExitPreviewActive = active;

            if (active)
            {
                _hierarchyEnterPreviewActive = false;
            }
        }

        public void SetHierarchyEnterPreviewActive(bool active)
        {
            _hierarchyEnterPreviewActive = active;

            if (active)
            {
                _hierarchyExitPreviewActive = false;
            }
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
            _cameraTransitionStartedAt = EditorApplication.timeSinceStartup;
            _cameraTransitionActive = true;
            _initialized = true;

            if (_cameraTransitionItem == null)
            {
                _cameraTransitionItem = schedule.Execute(UpdateCameraTransition)
                    .Every((long)(PackageGraphTransition.DefaultDurationSeconds * 1000f / 18f));
            }

            _cameraTransitionItem.Resume();
            UpdateCameraTransition();
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
            float normalMinimumZoom = GetMinZoom();
            bool zoomingOut = evt.delta.y > 0f;
            bool zoomingIn = evt.delta.y < 0f;
            bool atNormalMinimum = _zoom <= normalMinimumZoom + 0.002f;
            bool atNormalMaximum = _zoom >= MaxZoom - 0.002f;

            if (_hierarchyExitPreviewActive ||
                (zoomingOut &&
                 _layoutMode != PackageGraphLayoutMode.Overview &&
                 atNormalMinimum))
            {
                PackageGraphHierarchyExitWheelEvent hierarchyExitEvent =
                    new PackageGraphHierarchyExitWheelEvent(
                        evt.delta.y,
                        evt.localMousePosition,
                        atNormalMinimum);

                if (HierarchyExitWheel?.Invoke(hierarchyExitEvent) == true)
                {
                    evt.StopPropagation();
                    return;
                }
            }

            if (_hierarchyEnterPreviewActive ||
                (zoomingIn &&
                 _layoutMode != PackageGraphLayoutMode.Focus &&
                 atNormalMaximum))
            {
                PackageGraphHierarchyEnterWheelEvent hierarchyEnterEvent =
                    new PackageGraphHierarchyEnterWheelEvent(
                        evt.delta.y,
                        evt.localMousePosition,
                        atNormalMaximum);

                if (HierarchyEnterWheel?.Invoke(hierarchyEnterEvent) == true)
                {
                    evt.StopPropagation();
                    return;
                }
            }

            ZoomAround(evt.localMousePosition, _zoom * zoomMultiplier);
            evt.StopPropagation();
        }

        private void HandleMouseDown(MouseDownEvent evt)
        {
            if (_hierarchyExitPreviewActive || _hierarchyEnterPreviewActive)
            {
                evt.StopPropagation();
                return;
            }

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
            PackageGraphCameraState camera = PackageGraphTransition.EvaluateAnchoredCamera(
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
            MarkDirtyRepaint();
            _contentRoot.MarkDirtyRepaint();
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
            float configuredMinimum = layoutMode == PackageGraphLayoutMode.Overview ? OverviewMinZoom : FocusMinZoom;
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
            return evt.button == 2 ||
                   evt.button == 1 ||
                   (evt.button == 0 && (evt.altKey || IsLeftPanTarget(evt.target as VisualElement)));
        }

        internal static bool IsLeftPanTargetForTests(VisualElement target)
        {
            return IsLeftPanTarget(target);
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
                   !HasAncestorClass(target, "dpi-category-rail") &&
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

            if (layout.Mode == PackageGraphLayoutMode.Overview)
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
                        new Rect(
                            guide.Center.x - guide.RadiusX,
                            guide.Center.y - guide.RadiusY,
                            guide.RadiusX * 2f,
                            guide.RadiusY * 2f));
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
        private readonly Dictionary<string, Rect> _animatedGroupRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _transitionStartRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _transitionStartGroupRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
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
            PackageGraphNodePresentationLevel.OverviewCompact;
        private Vector2 _viewportSize;
        private float _viewportZoom = 1f;
        private IVisualElementScheduledItem _layoutAnimationItem;
        private double _layoutAnimationStartedAt;
        private bool _layoutAnimationActive;
        private bool _interactionsLocked;
        private bool _actionsEnabled;
        private readonly Dictionary<string, Rect> _hierarchyExitStartNodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _hierarchyExitTargetNodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _hierarchyExitStartGroupRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _hierarchyExitTargetGroupRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private PackageGraphLayoutResult _hierarchyExitTargetLayout;
        private float _hierarchyExitProgress;

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
            return PackageGraphActiveLayoutBounds.Calculate(_layoutResult);
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

        public bool InteractionsLocked => _interactionsLocked;

        public string ActiveHoverGroupId => GetActiveHoverGroupId();

        public string DirectHoverGroupId => _hoveredGroupId;

        internal float HierarchyExitProgressForTests => _hierarchyExitProgress;

        public event Action<bool> InteractionsLockedChanged;

        public event Action<string> ActiveHoverGroupChanged;

        internal IReadOnlyDictionary<string, Rect> NodeRectsForTests => _layoutResult != null
            ? _layoutResult.NodeRects
            : new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

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
                    if (_animatedGroupRects.TryGetValue(anchor.Id, out Rect animatedGroupRect))
                    {
                        center = animatedGroupRect.center;
                        return true;
                    }

                    if (_layoutResult != null)
                    {
                        PackageGraphGroupLayoutNode groupNode = _layoutResult.GroupNodes.FirstOrDefault(candidate =>
                            candidate != null &&
                            string.Equals(candidate.GroupId, anchor.Id, StringComparison.OrdinalIgnoreCase));

                        if (groupNode != null)
                        {
                            center = groupNode.Rect.center;
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
            Rebuild();
        }

        internal void SetPreviewPackageForTests(string packageId)
        {
            _hoveredPackageId = packageId ?? string.Empty;
            NotifyActiveHoverGroupChanged();
            Rebuild();
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
            Rebuild();
        }

        public bool BeginHierarchyExitPreview(PackageGraphLayoutResult targetLayout)
        {
            if (targetLayout == null || _layoutResult == null || _layoutAnimationActive)
            {
                return false;
            }

            _hierarchyExitTargetLayout = targetLayout;
            _hierarchyExitStartNodeRects.Clear();
            _hierarchyExitTargetNodeRects.Clear();
            _hierarchyExitStartGroupRects.Clear();
            _hierarchyExitTargetGroupRects.Clear();

            Dictionary<string, Rect> currentNodeRects = CaptureCurrentNodeRects();
            Dictionary<string, Rect> currentGroupRects = CaptureCurrentGroupRects();
            Vector2 sourceCenter = _layoutResult.ActiveCenter;
            Vector2 targetCenter = targetLayout.ActiveCenter;

            foreach (KeyValuePair<string, Rect> current in currentNodeRects)
            {
                _hierarchyExitStartNodeRects[current.Key] = current.Value;
                _hierarchyExitTargetNodeRects[current.Key] = targetLayout.NodeRects.TryGetValue(current.Key, out Rect targetRect)
                    ? targetRect
                    : CenterRectOn(current.Value, targetCenter);
            }

            foreach (KeyValuePair<string, Rect> target in targetLayout.NodeRects)
            {
                if (_hierarchyExitStartNodeRects.ContainsKey(target.Key))
                {
                    continue;
                }

                _hierarchyExitStartNodeRects[target.Key] = CenterRectOn(target.Value, sourceCenter);
                _hierarchyExitTargetNodeRects[target.Key] = target.Value;
            }

            foreach (KeyValuePair<string, Rect> current in currentGroupRects)
            {
                _hierarchyExitStartGroupRects[current.Key] = current.Value;
                _hierarchyExitTargetGroupRects[current.Key] = TryGetGroupRect(targetLayout, current.Key, out Rect targetRect)
                    ? targetRect
                    : CenterRectOn(current.Value, targetCenter);
            }

            foreach (PackageGraphGroupLayoutNode targetGroup in targetLayout.GroupNodes)
            {
                if (targetGroup == null ||
                    _hierarchyExitStartGroupRects.ContainsKey(targetGroup.GroupId))
                {
                    continue;
                }

                _hierarchyExitStartGroupRects[targetGroup.GroupId] = CenterRectOn(targetGroup.Rect, sourceCenter);
                _hierarchyExitTargetGroupRects[targetGroup.GroupId] = targetGroup.Rect;
            }

            _hierarchyExitProgress = 0f;
            ApplyHierarchyExitVisuals();
            return true;
        }

        public void SetHierarchyExitPreview(float progress)
        {
            if (_hierarchyExitTargetLayout == null)
            {
                return;
            }

            _hierarchyExitProgress = Mathf.Clamp01(progress);
            ApplyHierarchyExitPreviewRects(PackageGraphTransition.SmoothStep(_hierarchyExitProgress));
            SetInteractionsLocked(_layoutAnimationActive || _hierarchyExitProgress > 0.001f);
            ApplyHierarchyExitVisuals();
        }

        public void EndHierarchyExitPreview(bool restoreSource)
        {
            if (_hierarchyExitTargetLayout == null)
            {
                _hierarchyExitProgress = 0f;
                SetInteractionsLocked(_layoutAnimationActive);
                ApplyHierarchyExitVisuals();
                return;
            }

            if (restoreSource)
            {
                CopyPreviewRects(_hierarchyExitStartNodeRects, _animatedNodeRects);
                CopyPreviewRects(_hierarchyExitStartGroupRects, _animatedGroupRects);
            }
            else
            {
                _layoutResult = _hierarchyExitTargetLayout;
                _layoutFocusPackageId = _layoutResult.FocusPackageId;
                _layoutFocusGroupId = _layoutResult.FocusGroupId;
                CopyPreviewRects(_hierarchyExitTargetNodeRects, _animatedNodeRects);
                CopyPreviewRects(_hierarchyExitTargetGroupRects, _animatedGroupRects);
            }

            _hierarchyExitTargetLayout = null;
            _hierarchyExitStartNodeRects.Clear();
            _hierarchyExitTargetNodeRects.Clear();
            _hierarchyExitStartGroupRects.Clear();
            _hierarchyExitTargetGroupRects.Clear();
            _hierarchyExitProgress = 0f;
            SetInteractionsLocked(_layoutAnimationActive);
            ApplyAnimatedLayout();
            ApplyHierarchyExitVisuals();
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
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _visiblePackageIds = visiblePackageIds == null
                ? new HashSet<string>(_graph.Nodes.Select(node => node.PackageId), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(visiblePackageIds, StringComparer.OrdinalIgnoreCase);
            _visibleGraph = PackageVisibilityFilter.CreateVisibleGraph(_graph, _visiblePackageIds);
            _selectedPackageId = selectedPackageId ?? string.Empty;
            _focusedPackageId = focusedPackageId ?? string.Empty;
            _focusedGroupId = focusedGroupId ?? string.Empty;
            _actionsEnabled = actionsEnabled;
            _searchState = searchState ?? PackageGraphSearchState.Empty;
            Rebuild();
        }

        private void Rebuild()
        {
            Dictionary<string, Rect> previousRects = CaptureCurrentNodeRects();
            Dictionary<string, Rect> previousGroupRects = CaptureCurrentGroupRects();
            _guideLayer.Clear();
            _nodeLayer.Clear();
            _nodeElements.Clear();
            _groupElements.Clear();

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
            PackageGraphLayoutResult fullLayoutResult = _layout.Calculate(
                _visibleGraph,
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
            PackageGraphFocus edgeFocus = PackageGraphFocus.Create(
                _visibleGraph,
                _layoutFocusPackageId);
            _layoutResult = CreateVisibleLayoutResult(fullLayoutResult, _currentFocus);
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
                _animatedNodeRects,
                _animatedGroupRects,
                GetActiveHoverGroupId(),
                _interactionsLocked);
            StartLayoutTransition(previousRects, previousGroupRects);
            DrawGroups();
            DrawNodes(_currentFocus);
            DrawUnrelatedSummary();
            ApplyAnimatedLayout();
            _edgeLayer.SetGraph(_visibleGraph, _animatedNodeRects, _layoutResult.CanvasHeight, edgeFocus);
            ApplyHierarchyExitVisuals();
        }

        private string GetLayoutFocusPackageId()
        {
            return !string.IsNullOrWhiteSpace(_focusedPackageId) &&
                   _graph.TryGetNode(_focusedPackageId, out _) &&
                   _visiblePackageIds.Contains(_focusedPackageId)
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

        private PackageGraphLayoutResult CreateVisibleLayoutResult(
            PackageGraphLayoutResult fullLayoutResult,
            PackageGraphFocus fullFocus)
        {
            if (fullLayoutResult == null)
            {
                return null;
            }

            Dictionary<string, Rect> visibleNodeRects = fullLayoutResult.NodeRects
                .Where(pair => _visiblePackageIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphLayoutRing> visibleNodeRings = fullLayoutResult.NodeRings
                .Where(pair => _visiblePackageIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphNodePresentationLevel> visibleNodePresentations =
                fullLayoutResult.NodePresentationLevels
                    .Where(pair => _visiblePackageIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            int visibleUnrelatedCount = fullLayoutResult.Mode == PackageGraphLayoutMode.Focus && fullFocus != null
                ? _visibleGraph.Nodes.Count(node => !fullFocus.IsPackageRelated(node.PackageId))
                : 0;
            Rect unrelatedSummaryRect = visibleUnrelatedCount > 0
                ? fullLayoutResult.UnrelatedSummaryRect
                : default(Rect);
            PackageGraphGroupLayoutNode[] visibleGroupNodes = fullLayoutResult.GroupNodes
                .Where(groupNode => groupNode != null &&
                                    (groupNode.PackageCount > 0 ||
                                     fullLayoutResult.Mode == PackageGraphLayoutMode.GroupFocus ||
                                     fullLayoutResult.Mode == PackageGraphLayoutMode.Overview))
                .ToArray();

            return new PackageGraphLayoutResult(
                fullLayoutResult.Mode,
                fullLayoutResult.FocusPackageId,
                fullLayoutResult.CanvasWidth,
                fullLayoutResult.CanvasHeight,
                fullLayoutResult.HubRect,
                fullLayoutResult.ActiveCenter,
                visibleNodeRects,
                visibleNodeRings,
                fullLayoutResult.RingGuides,
                fullLayoutResult.SectorLabels,
                visibleUnrelatedCount,
                unrelatedSummaryRect,
                visibleGroupNodes,
                fullLayoutResult.FocusGroupId,
                visibleNodePresentations);
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

        private void StartLayoutTransition(
            IReadOnlyDictionary<string, Rect> previousRects,
            IReadOnlyDictionary<string, Rect> previousGroupRects)
        {
            _animatedNodeRects.Clear();
            _animatedGroupRects.Clear();
            _transitionStartRects.Clear();
            _transitionStartGroupRects.Clear();
            bool shouldAnimate = false;

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                Rect start = previousRects != null && previousRects.TryGetValue(target.Key, out Rect previous)
                    ? previous
                    : CreateEnteringNodeStartRect(target.Key, target.Value, previousGroupRects);
                _transitionStartRects[target.Key] = start;
                _animatedNodeRects[target.Key] = start;

                if (!AreRectsClose(start, target.Value))
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

                Rect start = previousGroupRects != null && previousGroupRects.TryGetValue(target.GroupId, out Rect previous)
                    ? previous
                    : CreateEnteringGroupStartRect(target, previousGroupRects);
                _transitionStartGroupRects[target.GroupId] = start;
                _animatedGroupRects[target.GroupId] = start;

                if (!AreRectsClose(start, target.Rect))
                {
                    shouldAnimate = true;
                }
            }

            if (!shouldAnimate)
            {
                _layoutAnimationActive = false;
                SetInteractionsLocked(_hierarchyExitProgress > 0.001f);
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
                _animatedGroupRects[target.GroupId] = LerpRect(start, target.Rect, eased);
            }

            ApplyAnimatedLayout();

            if (t >= 1f)
            {
                _layoutAnimationActive = false;
                SetInteractionsLocked(_hierarchyExitProgress > 0.001f);
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
                _visibleGraph.TryGetNode(packageId, out PackageGraphNode node) &&
                TryGetPreviousGroupRect(node.GroupId, previousGroupRects, out Rect groupRect))
            {
                return CenterRectOn(targetRect, groupRect.center);
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
            ApplyHierarchyExitVisuals();
        }

        private void CopyTargetRectsToAnimatedRects()
        {
            _animatedNodeRects.Clear();
            _animatedGroupRects.Clear();

            if (_layoutResult == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                _animatedNodeRects[target.Key] = target.Value;
            }

            foreach (PackageGraphGroupLayoutNode target in _layoutResult.GroupNodes)
            {
                if (target != null)
                {
                    _animatedGroupRects[target.GroupId] = target.Rect;
                }
            }
        }

        private void ApplyHierarchyExitPreviewRects(float easedProgress)
        {
            foreach (KeyValuePair<string, Rect> start in _hierarchyExitStartNodeRects)
            {
                Rect target = _hierarchyExitTargetNodeRects.TryGetValue(start.Key, out Rect targetRect)
                    ? targetRect
                    : start.Value;
                _animatedNodeRects[start.Key] = LerpRect(start.Value, target, easedProgress);
            }

            foreach (KeyValuePair<string, Rect> start in _hierarchyExitStartGroupRects)
            {
                Rect target = _hierarchyExitTargetGroupRects.TryGetValue(start.Key, out Rect targetRect)
                    ? targetRect
                    : start.Value;
                _animatedGroupRects[start.Key] = LerpRect(start.Value, target, easedProgress);
            }

            ApplyAnimatedLayout();
        }

        private void ApplyHierarchyExitVisuals()
        {
            bool active = _hierarchyExitProgress > 0.001f;
            bool interactionsBlocked = active || _interactionsLocked;
            float eased = PackageGraphTransition.SmoothStep(_hierarchyExitProgress);
            EnableInClassList("dpi-ecosystem-graph__canvas--hierarchy-exit-preview", active);
            _guideLayer.style.opacity = Mathf.Lerp(1f, 0.72f, eased);
            _membershipLayer.style.opacity = Mathf.Lerp(1f, 0.58f, eased);
            _edgeLayer.style.opacity = Mathf.Lerp(1f, 0.42f, eased);

            foreach (PackageGraphNodeElement nodeElement in _nodeElements.Values)
            {
                nodeElement.pickingMode = interactionsBlocked ? PickingMode.Ignore : PickingMode.Position;
                SetGraphActionButtonsInteractive(nodeElement, !interactionsBlocked && _actionsEnabled);
            }

            foreach (PackageGraphGroupElement groupElement in _groupElements.Values)
            {
                groupElement.pickingMode = interactionsBlocked ? PickingMode.Ignore : PickingMode.Position;
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

        private static void CopyPreviewRects(
            IReadOnlyDictionary<string, Rect> source,
            IDictionary<string, Rect> target)
        {
            target.Clear();

            foreach (KeyValuePair<string, Rect> rect in source)
            {
                target[rect.Key] = rect.Value;
            }
        }

        private static bool TryGetGroupRect(
            PackageGraphLayoutResult layout,
            string groupId,
            out Rect rect)
        {
            if (layout != null && !string.IsNullOrWhiteSpace(groupId))
            {
                PackageGraphGroupLayoutNode groupNode = layout.GroupNodes.FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(candidate.GroupId, groupId, StringComparison.OrdinalIgnoreCase));

                if (groupNode != null)
                {
                    rect = groupNode.Rect;
                    return true;
                }
            }

            rect = default(Rect);
            return false;
        }

        private void ApplyAnimatedLayout()
        {
            foreach (KeyValuePair<string, PackageGraphNodeElement> nodeElement in _nodeElements)
            {
                if (_animatedNodeRects.TryGetValue(nodeElement.Key, out Rect rect))
                {
                    SetElementRect(nodeElement.Value, rect);
                }
            }

            foreach (KeyValuePair<string, PackageGraphGroupElement> groupElement in _groupElements)
            {
                if (_animatedGroupRects.TryGetValue(groupElement.Key, out Rect rect))
                {
                    SetElementRect(groupElement.Value, rect);
                }
            }

            _edgeLayer.UpdateNodeRects(_animatedNodeRects);
            _membershipLayer.UpdateRects(_animatedNodeRects, _animatedGroupRects);
            _edgeLayer.MarkDirtyRepaint();
            _membershipLayer.MarkDirtyRepaint();
        }

        private void DrawGuides()
        {
            foreach (PackageGraphRingGuide guide in _layoutResult.RingGuides)
            {
                VisualElement ringGuide = new VisualElement();
                ringGuide.AddToClassList("dpi-graph-ring-guide");
                ringGuide.AddToClassList("dpi-graph-ring-guide--" + GetRingClass(guide.Ring));
                ringGuide.pickingMode = PickingMode.Ignore;
                ringGuide.style.left = guide.Center.x - guide.RadiusX;
                ringGuide.style.top = guide.Center.y - guide.RadiusY;
                ringGuide.style.width = guide.RadiusX * 2f;
                ringGuide.style.height = guide.RadiusY * 2f;

                if (!string.IsNullOrWhiteSpace(guide.Label))
                {
                    Label label = new Label(guide.Label);
                    label.AddToClassList("dpi-graph-ring-guide__label");
                    ringGuide.Add(label);
                }

                _guideLayer.Add(ringGuide);
            }

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
                PackageGraphGroupElement groupElement = new PackageGraphGroupElement(
                    groupNode,
                    _layoutResult.Mode,
                    hoverContext,
                    hoverActive && !hoverContext,
                    !_interactionsLocked,
                    groupFocused,
                    SetPreviewGroup,
                    ClearPreviewGroup);
                ApplySearchClasses(
                    groupElement,
                    _searchState.IsDirectCategoryMatch(groupNode.GroupId),
                    _searchState.IsCategoryContext(groupNode.GroupId),
                    _searchState.HasQuery);
                SetElementRect(groupElement, groupNode.Rect);
                _nodeLayer.Add(groupElement);
                _groupElements[groupNode.GroupId] = groupElement;
            }
        }

        private void DrawNodes(PackageGraphFocus focus)
        {
            string activeHoverGroupId = GetActiveHoverGroupId();
            bool hoverActive = !string.IsNullOrWhiteSpace(activeHoverGroupId);

            foreach (PackageGraphNode node in _graph.Nodes)
            {
                if (!_animatedNodeRects.TryGetValue(node.PackageId, out Rect rect))
                {
                    continue;
                }

                PackageGraphLayoutRing ring = _layoutResult.NodeRings.TryGetValue(
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
                bool related = focus.IsPackageRelated(node.PackageId);
                bool dimmed = focus.HasFocus && !related;
                bool hoverContext = hoverActive && IsPackageInHoverContext(node, activeHoverGroupId);
                bool hoverDimmed = hoverActive && !hoverContext;
                PackageGraphNodeVisualMode visualMode = GetNodeVisualMode(dimmed);
                PackageGraphNodePresentationLevel presentationLevel =
                    _layoutResult.NodePresentationLevels.TryGetValue(
                        node.PackageId,
                        out PackageGraphNodePresentationLevel resolvedPresentation)
                        ? resolvedPresentation
                        : PackageGraphPresentationPolicy.GetFocusPresentation(selected);
                string categoryPathLabel = GetGroupPathLabel(node.GroupId);
                bool showNodeAction = ShouldShowNodeAction(
                    node,
                    _actionFocus,
                    _actionFocus.IsPackageRelated(node.PackageId),
                    _layoutResult.Mode);
                bool nodeActionsEnabled = showNodeAction && _actionsEnabled && !_interactionsLocked;
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
                    _packageSelected,
                    _packageAction,
                    _selectionCleared,
                    SetPreviewPackage,
                    ClearPreviewPackage);
                ApplySearchClasses(
                    nodeElement,
                    _searchState.IsDirectPackageMatch(node.PackageId),
                    _searchState.IsPackageContext(node.PackageId),
                    _searchState.HasQuery);
                nodeElement.style.left = rect.x;
                nodeElement.style.top = rect.y;
                nodeElement.style.width = rect.width;
                nodeElement.style.height = rect.height;
                _nodeLayer.Add(nodeElement);
                _nodeElements[node.PackageId] = nodeElement;
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
            SetElementRect(summary, _layoutResult.UnrelatedSummaryRect);
            _nodeLayer.Add(summary);
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
            element.EnableInClassList("dpi-graph-search--dimmed", searchActive && !directMatch && !contextMatch);
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
            Rebuild();
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
            Rebuild();
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
            Rebuild();
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
            Rebuild();
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

        private static Rect Union(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
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

    internal sealed class PackageGraphMembershipLayer : VisualElement
    {
        private readonly Dictionary<string, Rect> _nodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _groupRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphLayoutResult _layout;
        private string _hoveredGroupId = string.Empty;
        private bool _interactionsLocked;

        public PackageGraphMembershipLayer()
        {
            generateVisualContent += GenerateMembershipGuides;
        }

        public void SetLayout(
            PackageGraphModel graph,
            PackageGraphLayoutResult layout,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            string hoveredGroupId,
            bool interactionsLocked)
        {
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _layout = layout;
            _hoveredGroupId = hoveredGroupId ?? string.Empty;
            _interactionsLocked = interactionsLocked;
            CopyNodeRects(nodeRects);
            CopyGroupRects(groupRects);
            MarkDirtyRepaint();
        }

        public void UpdateRects(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            CopyNodeRects(nodeRects);
            CopyGroupRects(groupRects);
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

        private void GenerateMembershipGuides(MeshGenerationContext context)
        {
            if (_layout == null ||
                _layout.GroupNodes.Count == 0)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            Dictionary<string, PackageGraphGroupLayoutNode> groupNodeById = _layout.GroupNodes
                .Where(groupNode => groupNode != null && groupNode.Group != null)
                .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            if (_layout.Mode == PackageGraphLayoutMode.Focus)
            {
                DrawFocusMembershipGuides(painter, groupNodeById);
                return;
            }

            foreach (PackageGraphGroupLayoutNode groupNode in _layout.GroupNodes)
            {
                if (groupNode == null || groupNode.Group == null || groupNode.Collapsed)
                {
                    continue;
                }

                Rect[] childRects = GetDirectChildRects(groupNode.GroupId, groupNodeById).ToArray();
                if (groupNode.OrbitRadius <= 0.01f)
                {
                    continue;
                }

                Rect groupRect = GetGroupRect(groupNode);
                Rect groupHubRect = GetGroupHubRect(groupNode, groupRect);
                bool hoverActive = !_interactionsLocked && !string.IsNullOrWhiteSpace(_hoveredGroupId);
                bool emphasized = hoverActive && IsGroupInHoverContext(groupNode.GroupId, _hoveredGroupId, groupNodeById);
                bool muted = hoverActive && !emphasized;

                DrawOrbitCircle(
                    painter,
                    groupHubRect.center,
                    groupNode.OrbitRadius,
                    emphasized,
                    muted,
                    groupNode.PackageCount == 0);

                foreach (Rect childRect in childRects)
                {
                    DrawSpoke(painter, groupHubRect, childRect, emphasized, muted);
                }
            }
        }

        private void DrawFocusMembershipGuides(
            Painter2D painter,
            IReadOnlyDictionary<string, PackageGraphGroupLayoutNode> groupNodeById)
        {
            foreach (PackageGraphGroupLayoutNode groupNode in _layout.GroupNodes)
            {
                if (groupNode == null ||
                    groupNode.Group == null ||
                    groupNode.RepresentedPackageIds.Count == 0)
                {
                    continue;
                }

                Rect groupRect = GetGroupRect(groupNode);
                Rect groupHubRect = GetGroupHubRect(groupNode, groupRect);
                bool hoverActive = !_interactionsLocked && !string.IsNullOrWhiteSpace(_hoveredGroupId);
                bool emphasized = hoverActive && IsGroupInHoverContext(groupNode.GroupId, _hoveredGroupId, groupNodeById);
                bool muted = hoverActive && !emphasized;

                foreach (string packageId in groupNode.RepresentedPackageIds)
                {
                    if (!_nodeRects.TryGetValue(packageId, out Rect packageRect))
                    {
                        continue;
                    }

                    DrawSpoke(painter, groupHubRect, packageRect, emphasized, muted);
                }
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

                yield return GetGroupHubRect(groupNode, GetGroupRect(groupNode));
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

        private static void DrawOrbitCircle(
            Painter2D painter,
            Vector2 center,
            float radius,
            bool emphasized,
            bool muted,
            bool empty)
        {
            float alpha = empty ? 0.018f : emphasized ? 0.065f : muted ? 0.015f : 0.038f;
            float borderAlpha = empty ? 0.045f : emphasized ? 0.18f : muted ? 0.035f : 0.085f;
            painter.fillColor = new Color(0.20f, 0.54f, 0.62f, alpha);
            painter.strokeColor = new Color(0.42f, 0.70f, 0.78f, borderAlpha);
            painter.lineWidth = emphasized ? 1.25f : 0.85f;
            DrawCircle(painter, center, radius);
        }

        private static void DrawSpoke(Painter2D painter, Rect fromHubRect, Rect toRect, bool emphasized, bool muted)
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

        private static void DrawCircle(Painter2D painter, Vector2 center, float radius)
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
            painter.Fill();
            painter.Stroke();
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

    internal sealed class PackageGraphEdgeLayer : VisualElement
    {
        private const int CurveSamples = 32;
        private const float AnimationFrameMs = 40f;
        private const float AnimatedDashLength = 12f;
        private const float AnimatedDashGap = 12f;
        private const float EdgeEndpointPadding = 6f;
        private const float MarkerTravelStart = 0.045f;
        private const float MarkerTravelEnd = 0.955f;

        private readonly Dictionary<string, Rect> _nodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphFocus _focus = PackageGraphFocus.Create(null, string.Empty);
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
            float canvasHeight,
            PackageGraphFocus focus)
        {
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _focus = focus ?? PackageGraphFocus.Create(_graph, string.Empty);
            CopyNodeRects(nodeRects);
            style.height = canvasHeight;
            _animationEnabled = HasAnimatedEdges();
            UpdateAnimationSchedule();
            MarkDirtyRepaint();
        }

        public void UpdateNodeRects(IReadOnlyDictionary<string, Rect> nodeRects)
        {
            CopyNodeRects(nodeRects);
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

            Painter2D painter = context.painter2D;

            foreach (PackageGraphEdge edge in _graph.Edges)
            {
                if (!_focus.IsEdgeVisible(edge) ||
                    !_nodeRects.TryGetValue(edge.FromPackageId, out Rect fromRect) ||
                    !_nodeRects.TryGetValue(edge.ToPackageId, out Rect toRect))
                {
                    continue;
                }

                DrawEdge(
                    painter,
                    edge,
                    fromRect,
                    toRect,
                    _focus.IsEdgeEmphasized(edge),
                    _focus.HasFocus,
                    _animationPhase);
            }
        }

        private static void DrawEdge(
            Painter2D painter,
            PackageGraphEdge edge,
            Rect fromRect,
            Rect toRect,
            bool emphasized,
            bool focusMode,
            float animationPhase)
        {
            Vector2 start = GetPort(fromRect, toRect, EdgeEndpointPadding);
            Vector2 end = GetPort(toRect, fromRect, EdgeEndpointPadding);
            Vector2 control = GetArcControlPoint(start, end, edge, emphasized);
            Vector2 controlA = Vector2.Lerp(start, control, 0.72f);
            Vector2 controlB = Vector2.Lerp(end, control, 0.72f);
            Color color = GetEdgeColor(edge, emphasized, focusMode);
            float width = GetEdgeWidth(edge, emphasized, focusMode);
            bool animate = emphasized &&
                           focusMode &&
                           ShouldAnimate(edge.Kind);

            if (color.a <= 0.01f || width <= 0.01f)
            {
                return;
            }

            if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
            {
                DrawIntegrationCableBezier(
                    painter,
                    start,
                    controlA,
                    controlB,
                    end,
                    color,
                    width,
                    emphasized);
            }
            else if (IsDotted(edge.Kind))
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                DrawDashedBezier(
                    painter,
                    start,
                    controlA,
                    controlB,
                    end,
                    2.5f,
                    7.5f,
                    animate ? (1f - animationPhase) * 10f : 0f);
            }
            else if (IsDashed(edge.Kind))
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                DrawDashedBezier(
                    painter,
                    start,
                    controlA,
                    controlB,
                    end,
                    AnimatedDashLength,
                    AnimatedDashGap,
                    animate ? (1f - animationPhase) * (AnimatedDashLength + AnimatedDashGap) : 0f);
            }
            else
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                painter.BeginPath();
                painter.MoveTo(start);
                painter.BezierCurveTo(controlA, controlB, end);
                painter.Stroke();
            }

            if (animate)
            {
                if (SupportsDirectionalFlowMarkers(edge.Kind))
                {
                    Color pulseColor = new Color(color.r, color.g, color.b, Mathf.Min(0.64f, color.a + 0.06f));
                    DrawFlowMarkers(
                        painter,
                        edge.Kind,
                        start,
                        controlA,
                        controlB,
                        end,
                        pulseColor,
                        animationPhase);
                }
            }

            if (edge.State == PackageGraphEdgeState.Warning)
            {
                DrawWarningMarker(painter, GetBezierPoint(start, controlA, controlB, end, 0.5f));
            }
        }

        private static Vector2 GetPort(Rect fromRect, Rect toRect, float padding)
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

        private static Vector2 GetArcControlPoint(
            Vector2 start,
            Vector2 end,
            PackageGraphEdge edge,
            bool emphasized)
        {
            Vector2 midpoint = (start + end) * 0.5f;
            Vector2 outward = midpoint - PackageGraphLayout.GraphCenter;

            if (outward.sqrMagnitude < 1f)
            {
                Vector2 delta = end - start;
                outward = new Vector2(-delta.y, delta.x);
            }

            if (outward.sqrMagnitude < 1f)
            {
                outward = Vector2.up;
            }

            float distance = Mathf.Max(46f, Vector2.Distance(start, end) * 0.10f);

            if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
            {
                distance += 10f;
            }
            else if (edge.Kind == PackageGraphEdgeKind.SuiteMembership)
            {
                distance += 18f;
            }
            else if (!emphasized)
            {
                distance *= 0.72f;
            }

            return midpoint + outward.normalized * distance;
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
            return SupportsDirectionalFlowMarkers(kind) ||
                   kind == PackageGraphEdgeKind.Recommended ||
                   kind == PackageGraphEdgeKind.SuiteMembership;
        }

        internal static bool UsesDirectionalFlowMarkersForTests(PackageGraphEdgeKind kind)
        {
            return SupportsDirectionalFlowMarkers(kind);
        }

        internal static bool AnimatesEdgeForTests(PackageGraphEdgeKind kind)
        {
            return ShouldAnimate(kind);
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

        private static void DrawIntegrationCableBezier(
            Painter2D painter,
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end,
            Color color,
            float width,
            bool emphasized)
        {
            Vector2 tangent = GetBezierTangent(start, controlA, controlB, end, 0.5f);

            if (tangent.sqrMagnitude < 0.01f)
            {
                tangent = end - start;
            }

            Vector2 side = tangent.sqrMagnitude < 0.01f
                ? Vector2.up
                : new Vector2(-tangent.y, tangent.x).normalized;
            float offset = emphasized ? 3.0f : 2.2f;
            Color underlay = new Color(0.04f, 0.12f, 0.14f, Mathf.Min(0.34f, color.a * 0.50f));

            painter.strokeColor = underlay;
            painter.lineWidth = Mathf.Max(1.4f, width + 1.1f);
            DrawBezierStroke(painter, start, controlA, controlB, end);

            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(0.85f, width * 0.54f);
            DrawBezierStroke(
                painter,
                start + side * offset,
                controlA + side * offset,
                controlB + side * offset,
                end + side * offset);
            DrawBezierStroke(
                painter,
                start - side * offset,
                controlA - side * offset,
                controlB - side * offset,
                end - side * offset);

        }

        private static void DrawBezierStroke(
            Painter2D painter,
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end)
        {
            painter.BeginPath();
            painter.MoveTo(start);
            painter.BezierCurveTo(controlA, controlB, end);
            painter.Stroke();
        }

        private static void DrawFlowMarkers(
            Painter2D painter,
            PackageGraphEdgeKind kind,
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end,
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
                float phase = Mathf.Repeat(animationPhase + (index / (float)markerCount), 1f);
                float markerT = Mathf.Lerp(MarkerTravelStart, MarkerTravelEnd, phase);
                DrawFlowChevron(
                    painter,
                    GetBezierPoint(start, controlA, controlB, end, markerT),
                    GetBezierTangent(start, controlA, controlB, end, markerT),
                    markerColor,
                    markerSize,
                    markerWidth);
            }
        }

        private static void DrawFlowChevron(
            Painter2D painter,
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

        private static void DrawDashedBezier(
            Painter2D painter,
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end,
            float dashLength,
            float gapLength,
            float dashOffset)
        {
            float patternLength = dashLength + gapLength;
            float normalizedOffset = Mathf.Repeat(dashOffset, patternLength);

            bool draw = normalizedOffset < dashLength;
            float segmentCursor = draw ? normalizedOffset : normalizedOffset - dashLength;
            Vector2 previous = start;

            for (int index = 1; index <= CurveSamples; index++)
            {
                float t = index / (float)CurveSamples;
                Vector2 current = GetBezierPoint(start, controlA, controlB, end, t);
                Vector2 delta = current - previous;
                float length = delta.magnitude;

                if (length <= 0.1f)
                {
                    previous = current;
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

                previous = current;
            }
        }

        private static Vector2 GetBezierPoint(
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end,
            float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * inverse * start +
                   3f * inverse * inverse * t * controlA +
                   3f * inverse * t * t * controlB +
                   t * t * t * end;
        }

        private static Vector2 GetBezierTangent(
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end,
            float t)
        {
            float inverse = 1f - t;
            return 3f * inverse * inverse * (controlA - start) +
                   6f * inverse * t * (controlB - controlA) +
                   3f * t * t * (end - controlB);
        }

        private static void DrawWarningMarker(Painter2D painter, Vector2 center)
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
            bool interactionsEnabled,
            Action<PackageGraphGroup> groupFocused,
            Action<string> previewGroup,
            Action<string> clearPreviewGroup)
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
            EnableInClassList("dpi-graph-group--overview", layoutMode == PackageGraphLayoutMode.Overview);
            EnableInClassList("dpi-graph-group--locked", !interactionsEnabled);
            EnableInClassList("dpi-graph-group--attention", groupNode.UpdateCount > 0 || groupNode.MissingCount > 0);
            EnableInClassList("dpi-graph-group--empty", groupNode.PackageCount == 0);
            EnableInClassList("dpi-graph-group--hover-context", hoverContext);
            EnableInClassList("dpi-graph-group--hover-dimmed", hoverDimmed);
            tooltip = GetTooltip(groupNode);

            if (interactionsEnabled)
            {
                RegisterCallback<MouseEnterEvent>(_ => previewGroup?.Invoke(groupNode.GroupId));
                RegisterCallback<MouseLeaveEvent>(_ => clearPreviewGroup?.Invoke(groupNode.GroupId));
                RegisterCallback<ClickEvent>(evt =>
                {
                    groupFocused?.Invoke(groupNode.Group);
                    evt.StopPropagation();
                });
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

            Image icon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon(groupNode.Group.IconKey),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList("dpi-graph-group__icon");
            symbol.Add(icon);

            Label count = new Label(groupNode.PackageCount.ToString());
            count.AddToClassList("dpi-graph-group__count");
            symbol.Add(count);

            if (groupNode.UpdateCount > 0 || groupNode.MissingCount > 0)
            {
                Label attention = new Label("!");
                attention.AddToClassList("dpi-graph-group__attention");
                symbol.Add(attention);
            }

            VisualElement caption = new VisualElement();
            caption.AddToClassList("dpi-graph-group__caption");
            caption.style.position = Position.Absolute;
            caption.style.left = 0f;
            caption.style.top = symbolTop + symbolSize + 7f;
            caption.style.width = groupNode.Rect.width;
            Add(caption);

            Label title = new Label(GetTitle(groupNode));
            title.AddToClassList("dpi-graph-group__title");
            caption.Add(title);

            string subtitleText = groupNode.Collapsed && !string.IsNullOrWhiteSpace(groupNode.SummaryLabel)
                ? groupNode.SummaryLabel
                : groupNode.PackageCount + " packages";
            Label subtitle = new Label(subtitleText);
            subtitle.AddToClassList("dpi-graph-group__subtitle");
            caption.Add(subtitle);

            VisualElement stats = new VisualElement();
            stats.AddToClassList("dpi-graph-group__stats");
            stats.Add(CreateStat("Installed", groupNode.InstalledCount, "installed"));
            stats.Add(CreateStat("Missing", groupNode.MissingCount, "missing"));

            if (groupNode.UpdateCount > 0)
            {
                stats.Add(CreateStat("Updates", groupNode.UpdateCount, "update"));
            }

            caption.Add(stats);
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
                    return "!";
                default:
                    return "\u25CB";
            }
        }

        private static string GetTooltip(PackageGraphGroupLayoutNode groupNode)
        {
            string description = string.IsNullOrWhiteSpace(groupNode.Group.Description)
                ? "Structural package group."
                : groupNode.Group.Description;
            return groupNode.Group.DisplayName + "\n" +
                   description + "\n" +
                   groupNode.PackageCount + " packages, " +
                   groupNode.InstalledCount + " installed, " +
                   groupNode.UpdateCount + " updates";
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
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            Action<string> previewPackage,
            Action<string> clearPreviewPackage)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));

            name = node.PackageId;
            AddToClassList("dpi-graph-node");
            AddToClassList("dpi-graph-node--" + GetNodeClass(node.NodeType));
            AddToClassList("dpi-graph-node--" + GetVisualModeClass(visualMode));
            AddToClassList("dpi-graph-node--presentation-" + GetPresentationClass(presentationLevel));
            AddToClassList("dpi-graph-node--status-" + GetStatusClass(node.Status));
            AddToClassList("dpi-graph-node--ring-" + GetRingClass(ring));
            EnableInClassList("dpi-graph-node--installed", node.IsInstalled);
            EnableInClassList("dpi-graph-node--actionable", showActions && IsGraphShortcutAction(node.PrimaryAction));
            EnableInClassList("dpi-graph-node--selected", selected);
            EnableInClassList("dpi-graph-node--related", related && !selected);
            EnableInClassList("dpi-graph-node--dimmed", dimmed);
            EnableInClassList("dpi-graph-node--hover-context", hoverContext);
            EnableInClassList("dpi-graph-node--hover-dimmed", hoverDimmed);
            EnableInClassList("dpi-graph-node--locked", !interactionsEnabled);
            EnableInClassList("dpi-graph-node--previewed", previewed);
            EnableInClassList("dpi-graph-node--missing", !node.IsRegistered);
            tooltip = GetTooltip(node);

            VisualElement statusRail = new VisualElement();
            statusRail.AddToClassList("dpi-graph-node__status-rail");
            statusRail.AddToClassList("dpi-graph-node__status-rail--" + GetStatusClass(node.Status));
            Add(statusRail);

            if (interactionsEnabled)
            {
                RegisterCallback<MouseEnterEvent>(_ => previewPackage?.Invoke(node.PackageId));
                RegisterCallback<MouseLeaveEvent>(_ => clearPreviewPackage?.Invoke(node.PackageId));
            }

            if (node.PackageDefinition != null && packageSelected != null)
            {
                RegisterCallback<ClickEvent>(evt =>
                {
                    if (!interactionsEnabled)
                    {
                        evt.StopPropagation();
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

                    evt.StopPropagation();
                });
            }

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-graph-node__header");
            Add(header);

            Image icon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon(node.IconKey),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList("dpi-graph-node__icon");
            header.Add(icon);

            VisualElement titleBlock = new VisualElement();
            titleBlock.AddToClassList("dpi-graph-node__title-block");
            header.Add(titleBlock);

            Label title = new Label(node.DisplayName);
            title.AddToClassList("dpi-graph-node__title");
            titleBlock.Add(title);

            Label statusMarker = new Label(GetStatusMarker(node.Status));
            statusMarker.AddToClassList("dpi-graph-node__status-icon");
            statusMarker.AddToClassList("dpi-graph-node__status-icon--" + GetStatusClass(node.Status));
            header.Add(statusMarker);

            bool showHierarchy = presentationLevel == PackageGraphNodePresentationLevel.OverviewCompact ||
                                 presentationLevel == PackageGraphNodePresentationLevel.Standard ||
                                 presentationLevel == PackageGraphNodePresentationLevel.Full;
            bool showBadges = presentationLevel != PackageGraphNodePresentationLevel.OverviewMicro;
            bool showTypeBadge = presentationLevel == PackageGraphNodePresentationLevel.Standard ||
                                 presentationLevel == PackageGraphNodePresentationLevel.Full;
            bool showPackageId = presentationLevel == PackageGraphNodePresentationLevel.Full;
            bool showFooter = presentationLevel == PackageGraphNodePresentationLevel.Standard ||
                              presentationLevel == PackageGraphNodePresentationLevel.Full;

            if (showPackageId)
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
            Label channel = new Label(node.SelectedChannel.ToString());
            channel.AddToClassList("dpi-graph-node__channel");
            footer.Add(channel);

            if (showActions && IsGraphShortcutAction(node.PrimaryAction))
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
                case PackageGraphNodePresentationLevel.OverviewMicro:
                    return "micro";
                case PackageGraphNodePresentationLevel.OverviewCompact:
                    return "compact";
                case PackageGraphNodePresentationLevel.Standard:
                    return "standard";
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

        private static string GetTooltip(PackageGraphNode node)
        {
            string status = string.IsNullOrWhiteSpace(node.UpdateStatusLabel)
                ? GetStatusLabel(node)
                : node.UpdateStatusLabel;
            string description = string.IsNullOrWhiteSpace(node.Description)
                ? node.PackageId
                : node.Description;

            return node.DisplayName + "\n" +
                   description + "\n" +
                   node.PackageId + "\n" +
                   "Status: " + status;
        }
    }
}
