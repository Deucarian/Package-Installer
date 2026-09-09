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
        private readonly PackageGraphLayoutAnimation _animation = new PackageGraphLayoutAnimation();
        private readonly Dictionary<string, PackageGraphNodeElement> _nodeElements =
            new Dictionary<string, PackageGraphNodeElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageGraphGroupElement> _groupElements =
            new Dictionary<string, PackageGraphGroupElement>(StringComparer.OrdinalIgnoreCase);

        private readonly VisualElement _guideLayer;
        private readonly PackageGraphMembershipLayer _membershipLayer;
        private readonly PackageGraphEdgeLayer _edgeLayer;
        private readonly VisualElement _occlusionLayer;
        private readonly PackageGraphOcclusionView _occlusions;
        private readonly VisualElement _nodeLayer;
        private VisualElement _rootHubElement;

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
            PackageGraphCanvasVisuals.StretchToCanvas(_guideLayer);
            Add(_guideLayer);

            _membershipLayer = new PackageGraphMembershipLayer { name = "ecosystem-graph-membership-layer" };
            _membershipLayer.AddToClassList("dpi-ecosystem-graph__membership-layer");
            _membershipLayer.pickingMode = PickingMode.Ignore;
            PackageGraphCanvasVisuals.StretchToCanvas(_membershipLayer);
            Add(_membershipLayer);

            _edgeLayer = new PackageGraphEdgeLayer { name = "ecosystem-graph-edge-layer" };
            _edgeLayer.AddToClassList("dpi-ecosystem-graph__edge-layer");
            _edgeLayer.pickingMode = PickingMode.Ignore;
            PackageGraphCanvasVisuals.StretchToCanvas(_edgeLayer);
            Add(_edgeLayer);

            _occlusionLayer = new VisualElement { name = "ecosystem-graph-occlusion-layer" };
            _occlusionLayer.AddToClassList("dpi-ecosystem-graph__occlusion-layer");
            _occlusionLayer.pickingMode = PickingMode.Ignore;
            PackageGraphCanvasVisuals.StretchToCanvas(_occlusionLayer);
            Add(_occlusionLayer);
            _occlusions = new PackageGraphOcclusionView(_occlusionLayer);

            _nodeLayer = new VisualElement { name = "ecosystem-graph-node-layer" };
            _nodeLayer.AddToClassList("dpi-ecosystem-graph__node-layer");
            PackageGraphCanvasVisuals.StretchToCanvas(_nodeLayer);
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
                PackageGraphCanvasBounds.AddRouteBounds(ref bounds, ref hasBounds, route.Points);
            }

            foreach (PackageGraphStructuralMembershipRoute route in _membershipLayer.FocusMembershipRoutesForTests())
            {
                PackageGraphCanvasBounds.AddRouteBounds(ref bounds, ref hasBounds, route.Segments);
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

        public string ActiveHoverPackageId => _hoveredPackageId;

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

        internal IReadOnlyDictionary<string, float> AnimatedGroupOrbitRadiiForTests => _animation.GroupOrbitRadii;

        internal IReadOnlyDictionary<string, Vector2> AnimatedGroupCentersForTests => _animation.GroupCenters;

        internal IReadOnlyDictionary<string, Rect> AnimatedGroupRectsForTests => _animation.Groups;

        internal IReadOnlyDictionary<string, Rect> AnimatedNodeRectsForTests => _animation.Nodes;

        internal IReadOnlyDictionary<string, PackageGraphNodeVisualState> NodeVisualStatesForTests => _animation.NodeStates;

        internal IReadOnlyList<PackageGraphOrbitVisualState> OrbitVisualStatesForTests =>
            _membershipLayer.BuildOrbitVisualStatesForTests();

        internal IReadOnlyList<CategoryStatusRingVisualState> StatusRingVisualStatesForTests =>
            _membershipLayer.BuildStatusRingVisualStatesForTests();

        internal IReadOnlyList<PackageGraphStructuralMembershipRoute> StructuralMembershipRoutesForTests =>
            _membershipLayer.FocusMembershipRoutesForTests();

        internal IReadOnlyList<PackageGraphEdgeRoute> EdgeRoutesForTests =>
            _edgeLayer.BuildRoutesSnapshotForTests();

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
                    if (_animation.Nodes.TryGetValue(anchor.Id, out Rect animatedNodeRect))
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
                    if (_animation.GroupCenters.TryGetValue(anchor.Id, out Vector2 animatedGroupCenter))
                    {
                        center = animatedGroupCenter;
                        return true;
                    }

                    if (_animation.Groups.TryGetValue(anchor.Id, out Rect animatedGroupRect))
                    {
                        PackageGraphGroupLayoutNode animatedGroupNode = PackageGraphTransitionGeometry.FindGroupLayoutNode(_layoutResult, anchor.Id);
                        center = animatedGroupNode != null
                            ? PackageGraphTransitionGeometry.GetGroupHubRect(animatedGroupNode, animatedGroupRect).center
                            : animatedGroupRect.center;
                        return true;
                    }

                    if (_layoutResult != null)
                    {
                        PackageGraphGroupLayoutNode groupNode = PackageGraphTransitionGeometry.FindGroupLayoutNode(_layoutResult, anchor.Id);

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

        public void SetExternalHoverPackage(string packageId)
        {
            SetExternalHoverPackage(packageId, respectInteractionLock: true);
        }

        private void SetExternalHoverPackage(string packageId, bool respectInteractionLock)
        {
            if (respectInteractionLock && _interactionsLocked)
            {
                return;
            }

            string nextPackageId = packageId ?? string.Empty;

            if (string.Equals(_hoveredPackageId, nextPackageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredPackageId = nextPackageId;
            NotifyActiveHoverGroupChanged();
            RefreshHoverVisualState();
        }

        internal void SetPreviewPackageForTests(string packageId)
        {
            SetExternalHoverPackage(packageId, respectInteractionLock: false);
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

        public void ClearExternalHoverPackage(string packageId)
        {
            if (_interactionsLocked ||
                !string.Equals(_hoveredPackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredPackageId = string.Empty;
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
                   current.OwningCategoryCount == next.OwningCategoryCount &&
                   current.ContextPackageCount == next.ContextPackageCount;
        }

        private void Rebuild()
        {
            Dictionary<string, Rect> previousRects = PackageGraphLayoutSnapshots.CaptureCurrentNodeRects(_animation, _layoutResult);
            Dictionary<string, PackageGraphNodeVisualState> previousNodeVisualStates = PackageGraphLayoutSnapshots.CaptureCurrentNodeVisualStates(_animation, _layoutResult);
            Dictionary<string, Rect> previousGroupRects = PackageGraphLayoutSnapshots.CaptureCurrentGroupRects(_animation, _layoutResult);
            Dictionary<string, Vector2> previousGroupCenters = PackageGraphLayoutSnapshots.CaptureCurrentGroupCenters(_animation, _layoutResult);
            Dictionary<string, float> previousGroupOrbitRadii = PackageGraphLayoutSnapshots.CaptureCurrentGroupOrbitRadii(_animation, _layoutResult);
            _guideLayer.Clear();
            _occlusions.Clear();
            _nodeLayer.Clear();
            _nodeElements.Clear();
            _groupElements.Clear();
            _rootHubElement = null;
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
                _layoutResult = new PackageGraphLayoutProjection(_visibleGraph, _visiblePackageIds)
                    .CreateProjectedLayoutResult(fullLayoutResult, _currentFocus);
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
                _occlusionLayer.style.width = _layoutResult.CanvasWidth;
                _occlusionLayer.style.height = _layoutResult.CanvasHeight;
                _nodeLayer.style.width = _layoutResult.CanvasWidth;
                _nodeLayer.style.height = _layoutResult.CanvasHeight;

                DrawGuides();
                _membershipLayer.SetLayout(
                    _visibleGraph,
                    _layoutResult,
                    PackageGraphCategoryStatusSummary.Create(_visibleGraph.Nodes),
                    _searchState,
                    _animation.Nodes,
                    _animation.Groups,
                    _animation.GroupCenters,
                    _animation.GroupOrbitRadii,
                    GetActiveHoverGroupId(),
                    _interactionsLocked);
                StartLayoutTransition(
                    previousRects,
                    previousNodeVisualStates,
                    previousGroupRects,
                    previousGroupCenters,
                    previousGroupOrbitRadii);
                DrawConnectionOcclusions();
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

        private void StartLayoutTransition(
            IReadOnlyDictionary<string, Rect> previousRects,
            IReadOnlyDictionary<string, PackageGraphNodeVisualState> previousNodeVisualStates,
            IReadOnlyDictionary<string, Rect> previousGroupRects,
            IReadOnlyDictionary<string, Vector2> previousGroupCenters,
            IReadOnlyDictionary<string, float> previousGroupOrbitRadii)
        {
            bool shouldAnimate = _animation.Begin(_layoutResult,
                new PackageGraphTransitionOrigins(_visibleGraph, _layoutResult, GetActiveCenter()),
                previousRects, previousNodeVisualStates, previousGroupRects, previousGroupCenters, previousGroupOrbitRadii);
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
            ApplyLayoutTransitionProgress(t);

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

        private void ApplyLayoutTransitionProgress(float linearProgress)
        {
            _animation.Evaluate(linearProgress);
            ApplyAnimatedLayout();
        }

        internal void EvaluateLayoutTransitionForTests(float linearProgress)
        {
            ApplyLayoutTransitionProgress(linearProgress);
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
            _animation.Snap(_layoutResult);
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
                PackageGraphCanvasVisuals.SetGraphActionButtonsInteractive(nodeElement, !_interactionsLocked && _actionsEnabled);
            }

            foreach (PackageGraphGroupElement groupElement in _groupElements.Values)
            {
                groupElement.pickingMode = _interactionsLocked ? PickingMode.Ignore : PickingMode.Position;
            }
        }

        internal static void ProjectOrbitalChildrenForTests(
            PackageGraphModel graph,
            PackageGraphLayoutResult layout,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, Rect> groupRects,
            IDictionary<string, Vector2> groupCenters,
            IDictionary<string, float> groupOrbitRadii)
        {
            PackageGraphTransitionGeometry.ProjectOrbitalChildren(graph, layout, nodeRects, groupRects, groupCenters, groupOrbitRadii);
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

                _animation.Nodes.Remove(packageId);
                _animation.NodeStates.Remove(packageId);

                if (_nodeElements.TryGetValue(packageId, out PackageGraphNodeElement element))
                {
                    element.RemoveFromHierarchy();
                    _nodeElements.Remove(packageId);
                }
            }
        }

        private void ApplyAnimatedLayout(bool updateEdgeLayer = true)
        {
            PackageGraphTransitionGeometry.ProjectOrbitalChildren(
                _visibleGraph,
                _layoutResult,
                _animation.Nodes,
                _animation.Groups,
                _animation.GroupCenters,
                _animation.GroupOrbitRadii);
            _animation.SyncNodeVisualStateRects();
            UpdateConnectionOcclusionLayout();

            foreach (KeyValuePair<string, PackageGraphNodeElement> nodeElement in _nodeElements)
            {
                if (_animation.NodeStates.TryGetValue(nodeElement.Key, out PackageGraphNodeVisualState state))
                {
                    PackageGraphCanvasVisuals.SetElementVisualState(nodeElement.Value, state);
                    nodeElement.Value.pickingMode = _interactionsLocked
                        ? PickingMode.Ignore
                        : nodeElement.Value.pickingMode;
                }
                else if (_animation.Nodes.TryGetValue(nodeElement.Key, out Rect rect))
                {
                    PackageGraphCanvasVisuals.SetElementVisualState(
                        nodeElement.Value,
                        PackageGraphNodeVisualState.Stable(rect, _animation.GetPresentationLevel(nodeElement.Key)));
                    nodeElement.Value.pickingMode = _interactionsLocked
                        ? PickingMode.Ignore
                        : nodeElement.Value.pickingMode;
                }
            }

            foreach (KeyValuePair<string, PackageGraphGroupElement> groupElement in _groupElements)
            {
                if (_animation.Groups.TryGetValue(groupElement.Key, out Rect rect))
                {
                    PackageGraphCanvasVisuals.SetElementRect(groupElement.Value, rect);
                }
            }

            if (updateEdgeLayer)
            {
                if (!_layoutAnimationActive)
                {
                    IReadOnlyDictionary<string, Rect> routeNodeRects = _layoutResult != null
                        ? _layoutResult.NodeRects
                        : _animation.Nodes;
                    _edgeLayer.UpdateRects(routeNodeRects, CreateTargetGroupRectSnapshot());
                }

                _edgeLayer.MarkDirtyRepaint();
            }

            _membershipLayer.UpdateRects(
                _animation.Nodes,
                _animation.Groups,
                _animation.GroupCenters,
                _animation.GroupOrbitRadii);
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
                label.AddToClassList("dpi-graph-sector-label--" + PackageGraphCanvasLabels.GetSectorClass(sectorLabel));
                label.style.left = sectorLabel.Position.x - 92f;
                label.style.top = sectorLabel.Position.y - 12f;
                label.style.width = 184f;
                label.style.height = 24f;
                _nodeLayer.Add(label);
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
                image = DeucarianEditorIcons.GetIcon(DeucarianEditorIconIds.Network),
                scaleMode = ScaleMode.ScaleToFit,
                tintColor = DeucarianEditorTheme.Text
            };
            hubIcon.AddToClassList("dpi-graph-hub__icon");
            hub.Add(hubIcon);

            Label title = new Label("Deucarian");
            title.AddToClassList("dpi-graph-hub__title");
            hub.Add(title);

            Label subtitle = new Label("Unity Package System");
            subtitle.AddToClassList("dpi-graph-hub__subtitle");
            hub.Add(subtitle);
            _nodeLayer.Add(hub);
            _rootHubElement = hub;
        }

        private void DrawConnectionOcclusions()
        {
            _occlusions.Build(_layoutResult, _animation);
            UpdateConnectionOcclusionLayout();
        }

        private void UpdateConnectionOcclusionLayout()
        {
            _occlusions.Update(_layoutResult, _animation, _rootHubElement, _nodeElements, _groupElements);
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
                PackageGraphCanvasVisuals.ApplySearchClasses(
                    groupElement,
                    _searchState.IsDirectCategoryMatch(groupNode.GroupId),
                    _searchState.IsOwningCategory(groupNode.GroupId),
                    _searchState.IsCategoryContext(groupNode.GroupId),
                    _searchState.HasQuery && _layoutResult.Mode != PackageGraphLayoutMode.Focus);
                PackageGraphCanvasVisuals.SetElementRect(groupElement, groupNode.Rect);
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
                    if (!_animation.Nodes.TryGetValue(node.PackageId, out Rect rect))
                    {
                        continue;
                    }

                    PackageGraphNodeElement nodeElement = CreateNodeElement(node, focus, _layoutResult);
                    if (_animation.NodeStates.TryGetValue(node.PackageId, out PackageGraphNodeVisualState visualState))
                    {
                        PackageGraphCanvasVisuals.SetElementVisualState(nodeElement, visualState);
                    }
                    else
                    {
                        PackageGraphCanvasVisuals.SetElementRect(nodeElement, rect);
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
            PackageGraphCanvasVisuals.ApplySearchClasses(
                nodeElement,
                _searchState.IsDirectPackageMatch(node.PackageId),
                false,
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
                .Select(edge => PackageGraphCanvasLabels.FormatRelationshipTooltipLabel(edge.Kind, edge.Label))
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return labels.Length == 0
                ? string.Empty
                : "Relationship: " + string.Join("; ", labels);
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
            PackageGraphCanvasVisuals.SetElementRect(summary, _layoutResult.UnrelatedSummaryRect);
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
                summary.name = "ecosystem-graph-overflow-" + PackageGraphCanvasLabels.GetOverflowZoneClass(overflow.Zone);
                summary.AddToClassList("dpi-graph-unrelated-summary");
                summary.AddToClassList("dpi-graph-overflow-summary");
                summary.AddToClassList("dpi-graph-overflow-summary--" + PackageGraphCanvasLabels.GetOverflowZoneClass(overflow.Zone));
                summary.tooltip = "Additional " + PackageGraphCanvasLabels.GetOverflowZoneLabel(overflow.Zone) +
                                  " are summarized to keep this dense relationship view readable. " +
                                  "Click or press Enter/Space to copy their IDs.";
                PackageGraphCanvasVisuals.SetElementRect(summary, overflow.Rect);
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
                "Additional " + PackageGraphCanvasLabels.GetOverflowZoneLabel(overflow.Zone) + ": " + overflow.HiddenCount
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
            UpdateConnectionOcclusionLayout();
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

    }
}
