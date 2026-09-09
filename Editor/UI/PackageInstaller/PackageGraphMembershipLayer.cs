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
    internal sealed class PackageGraphMembershipLayer : VisualElement
    {

        private readonly PackageGraphGeometrySnapshot geometry = new PackageGraphGeometrySnapshot();

        private IReadOnlyDictionary<string, Rect> _nodeRects => geometry.Nodes;
        private IReadOnlyDictionary<string, Rect> _groupRects => geometry.Groups;
        private IReadOnlyDictionary<string, Vector2> _groupCenters => geometry.Centers;
        private IReadOnlyDictionary<string, float> _groupOrbitRadii => geometry.Radii;

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphLayoutResult _layout;
        private PackageGraphCategoryStatusSummary _rootStatusSummary;
        private PackageGraphSearchState _searchState = PackageGraphSearchState.Empty;
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
            PackageGraphSearchState searchState,
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
            _searchState = searchState ?? PackageGraphSearchState.Empty;
            _hoveredGroupId = hoveredGroupId ?? string.Empty;
            _interactionsLocked = interactionsLocked;
            geometry.Update(nodeRects, groupRects, groupCenters, groupOrbitRadii);
            MarkDirtyRepaint();
        }

        public void UpdateRects(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            IReadOnlyDictionary<string, Vector2> groupCenters,
            IReadOnlyDictionary<string, float> groupOrbitRadii)
        {
            geometry.Update(nodeRects, groupRects, groupCenters, groupOrbitRadii);
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
                PackageGraphMembershipPainter.DrawOrbitCircle(painter, orbit);
            }

            foreach (CategoryStatusRingVisualState statusRing in BuildStatusRingVisualStates(groupNodeById))
            {
                PackageGraphMembershipPainter.DrawStatusRing(painter, statusRing);
            }

            if (_layout.Mode == PackageGraphLayoutMode.Focus)
            {
                DrawFocusMembershipGuides(painter, groupNodeById);
                PackageGraphPainterCompatibility.Complete(painter);
                return;
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
                                  PackageGraphMembershipGroupGeometry.IsGroupInHoverContext(groupNode.GroupId, _hoveredGroupId, groupNodeById);
                bool searchMuted = IsGroupSearchMuted(groupNode.GroupId);
                states.Add(new CategoryStatusRingVisualState(
                    "group:" + groupNode.GroupId + ":status",
                    groupHubRect.center,
                    Mathf.Min(groupHubRect.width, groupHubRect.height) * 0.5f + PackageGraphMembershipMetrics.GroupStatusRingOffset,
                    emphasized ? 4.8f : 4f,
                    PackageGraphCategoryStatusVisuals.CreateSlices(groupNode.StatusSummary),
                    emphasized,
                    searchMuted));
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
                                  PackageGraphMembershipGroupGeometry.IsGroupInHoverContext(groupNode.GroupId, _hoveredGroupId, groupNodeById);
                bool muted = (hoverActive && !emphasized) || IsGroupSearchMuted(groupNode.GroupId);
                bool empty = groupNode.PackageCount == 0;
                states.Add(new PackageGraphOrbitVisualState(
                    "group:" + groupNode.GroupId,
                    groupHubRect.center,
                    orbitRadius,
                    empty ? 0.012f : emphasized ? 0.055f : muted ? 0.010f : 0.026f,
                    empty ? 0.075f : emphasized ? 0.52f : muted ? 0.040f : 0.22f,
                    emphasized,
                    muted,
                    true,
                    empty));
            }

            return states;
        }

        private bool IsGroupSearchMuted(string groupId)
        {
            return _layout != null &&
                   _layout.Mode != PackageGraphLayoutMode.Focus &&
                   _searchState != null &&
                   _searchState.HasQuery &&
                   !_searchState.IsDirectCategoryMatch(groupId) &&
                   !_searchState.IsOwningCategory(groupId) &&
                   !_searchState.IsCategoryContext(groupId);
        }

        private void DrawFocusMembershipGuides(
            PackageGraphPainter painter,
            IReadOnlyDictionary<string, PackageGraphGroupLayoutNode> groupNodeById)
        {
            foreach (PackageGraphStructuralMembershipRoute route in BuildFocusMembershipRoutes(groupNodeById))
            {
                bool hoverActive = !_interactionsLocked && !string.IsNullOrWhiteSpace(_hoveredGroupId);
                bool emphasized = hoverActive && PackageGraphMembershipGroupGeometry.IsGroupInHoverContext(route.GroupId, _hoveredGroupId, groupNodeById);
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
                Rect groupPortRect = PackageGraphMembershipSegments.ExpandRect(groupHubRect, PackageGraphMembershipMetrics.GroupStatusRingOffset);
                List<KeyValuePair<string, Rect>> packageRects = groupNode.RepresentedPackageIds
                    .Where(packageId => !string.IsNullOrWhiteSpace(packageId) &&
                                        _nodeRects.ContainsKey(packageId))
                    .Select(packageId => new KeyValuePair<string, Rect>(packageId, _nodeRects[packageId]))
                    .ToList();

                if (packageRects.Count == 0)
                {
                    continue;
                }

                routes.Add(PackageGraphMembershipRoutePlanner.CreateStructuralMembershipRoute(
                    groupNode.GroupId,
                    groupPortRect,
                    groupRect,
                    packageRects));
            }

            return routes;
        }

        private void DrawStructuralMembershipRoute(
            PackageGraphPainter painter,
            PackageGraphStructuralMembershipRoute route,
            bool emphasized,
            bool muted)
        {
            PackageGraphCategoryStatusKey aggregateStatus = ResolveAggregateStatus(route.PackageIds);

            foreach (PackageGraphStructuralMembershipSegment segment in route.Segments
                         .OrderBy(candidate => candidate.PackageIds != null && candidate.PackageIds.Count == 1 ? 1 : 0))
            {
                PackageGraphCategoryStatusKey statusKey = ResolveSegmentStatus(segment, aggregateStatus);
                Color color = PackageGraphCategoryStatusVisuals.GetColor(statusKey);
                Color neutral = DeucarianEditorGraphTheme.EdgeEmphasis;
                neutral.a = 1f;
                color = Color.Lerp(neutral, color, emphasized ? 0.20f : 0.08f);
                color.a = muted ? 0.045f : emphasized ? 0.82f : 0.48f;
                painter.strokeColor = color;
                painter.lineWidth = emphasized ? 1.8f : 1.25f;
                PackageGraphMembershipPainter.StrokeLine(painter, segment.From, segment.To);
            }
        }

        private PackageGraphCategoryStatusKey ResolveSegmentStatus(
            PackageGraphStructuralMembershipSegment segment,
            PackageGraphCategoryStatusKey aggregateStatus)
        {
            PackageGraphCategoryStatusKey? resolvedStatus = null;

            foreach (string packageId in segment.PackageIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(packageId) ||
                    !_graph.TryGetNode(packageId, out PackageGraphNode package))
                {
                    continue;
                }

                PackageGraphCategoryStatusKey packageStatus =
                    PackageGraphCategoryStatusClassifier.Classify(package);

                if (resolvedStatus.HasValue && resolvedStatus.Value != packageStatus)
                {
                    return PackageGraphCategoryStatusKey.Unknown;
                }

                resolvedStatus = packageStatus;
            }

            return resolvedStatus ?? aggregateStatus;
        }

        private PackageGraphCategoryStatusKey ResolveAggregateStatus(IEnumerable<string> packageIds)
        {
            bool hasInstalled = false;
            bool hasNotInstalled = false;
            bool hasUnknown = false;

            foreach (string packageId in packageIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(packageId) ||
                    !_graph.TryGetNode(packageId, out PackageGraphNode package))
                {
                    continue;
                }

                switch (PackageGraphCategoryStatusClassifier.Classify(package))
                {
                    case PackageGraphCategoryStatusKey.Attention:
                        return PackageGraphCategoryStatusKey.Attention;
                    case PackageGraphCategoryStatusKey.Unknown:
                        hasUnknown = true;
                        break;
                    case PackageGraphCategoryStatusKey.NotInstalled:
                        hasNotInstalled = true;
                        break;
                    case PackageGraphCategoryStatusKey.Installed:
                        hasInstalled = true;
                        break;
                }
            }

            if (hasUnknown)
            {
                return PackageGraphCategoryStatusKey.Unknown;
            }

            if (hasNotInstalled)
            {
                return PackageGraphCategoryStatusKey.NotInstalled;
            }

            return hasInstalled
                ? PackageGraphCategoryStatusKey.Installed
                : PackageGraphCategoryStatusKey.Unknown;
        }

        private bool IsPackageSearchMuted(string packageId)
        {
            return _layout != null &&
                   _layout.Mode != PackageGraphLayoutMode.Focus &&
                   _searchState != null &&
                   _searchState.HasQuery &&
                   !_searchState.IsDirectPackageMatch(packageId) &&
                   !_searchState.IsPackageContext(packageId);
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
                return PackageGraphMembershipGroupGeometry.CenterRectOn(groupNode.HubRect, center);
            }

            return PackageGraphMembershipGroupGeometry.GetGroupHubRect(groupNode, groupRect);
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

    }
}
