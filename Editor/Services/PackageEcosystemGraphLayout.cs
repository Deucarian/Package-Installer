using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageGraphLayoutMode
    {
        Overview,
        GroupFocus,
        Focus
    }

    internal enum PackageGraphLayoutRing
    {
        Infrastructure,
        Runtime,
        Integration,
        Suite
    }

    internal enum PackageGraphNodePresentationLevel
    {
        OverviewMicro,
        OverviewCompact,
        Standard,
        Full
    }

    internal readonly struct PackageGraphNodeMetrics
    {
        public PackageGraphNodeMetrics(float width, float height)
        {
            Width = width;
            Height = height;
        }

        public float Width { get; }

        public float Height { get; }

        public Vector2 Size => new Vector2(Width, Height);
    }

    internal static class PackageGraphPresentationPolicy
    {
        private const float OverviewMicroToCompactZoom = 0.82f;
        private const float OverviewCompactToMicroZoom = 0.70f;
        private const float OverviewCompactToStandardZoom = 1.36f;
        private const float OverviewStandardToCompactZoom = 1.18f;
        private const float GroupCompactToStandardZoom = 0.92f;
        private const float GroupStandardToCompactZoom = 0.78f;

        public static PackageGraphNodeMetrics GetMetrics(PackageGraphNodePresentationLevel level)
        {
            switch (level)
            {
                case PackageGraphNodePresentationLevel.OverviewMicro:
                    return new PackageGraphNodeMetrics(104f, 46f);
                case PackageGraphNodePresentationLevel.OverviewCompact:
                    return new PackageGraphNodeMetrics(132f, 60f);
                case PackageGraphNodePresentationLevel.Standard:
                    return new PackageGraphNodeMetrics(168f, 78f);
                default:
                    return new PackageGraphNodeMetrics(PackageGraphLayout.NodeWidth, PackageGraphLayout.NodeHeight);
            }
        }

        public static PackageGraphNodePresentationLevel GetDefaultForMode(PackageGraphLayoutMode mode)
        {
            switch (mode)
            {
                case PackageGraphLayoutMode.Overview:
                    return PackageGraphNodePresentationLevel.OverviewMicro;
                case PackageGraphLayoutMode.GroupFocus:
                    return PackageGraphNodePresentationLevel.OverviewCompact;
                default:
                    return PackageGraphNodePresentationLevel.Standard;
            }
        }

        public static PackageGraphNodePresentationLevel ResolveForZoom(
            PackageGraphLayoutMode mode,
            float zoom,
            PackageGraphNodePresentationLevel current)
        {
            if (mode == PackageGraphLayoutMode.Overview)
            {
                switch (current)
                {
                    case PackageGraphNodePresentationLevel.OverviewMicro:
                        return zoom >= OverviewMicroToCompactZoom
                            ? PackageGraphNodePresentationLevel.OverviewCompact
                            : PackageGraphNodePresentationLevel.OverviewMicro;
                    case PackageGraphNodePresentationLevel.Standard:
                        return zoom <= OverviewStandardToCompactZoom
                            ? PackageGraphNodePresentationLevel.OverviewCompact
                            : PackageGraphNodePresentationLevel.Standard;
                    default:
                        if (zoom <= OverviewCompactToMicroZoom)
                        {
                            return PackageGraphNodePresentationLevel.OverviewMicro;
                        }

                        return zoom >= OverviewCompactToStandardZoom
                            ? PackageGraphNodePresentationLevel.Standard
                            : PackageGraphNodePresentationLevel.OverviewCompact;
                }
            }

            if (mode == PackageGraphLayoutMode.GroupFocus)
            {
                if (current == PackageGraphNodePresentationLevel.OverviewCompact)
                {
                    return zoom >= GroupCompactToStandardZoom
                        ? PackageGraphNodePresentationLevel.Standard
                        : PackageGraphNodePresentationLevel.OverviewCompact;
                }

                return zoom <= GroupStandardToCompactZoom
                    ? PackageGraphNodePresentationLevel.OverviewCompact
                    : PackageGraphNodePresentationLevel.Standard;
            }

            return PackageGraphNodePresentationLevel.Standard;
        }

        public static PackageGraphNodePresentationLevel GetFocusPresentation(bool selected)
        {
            return selected
                ? PackageGraphNodePresentationLevel.Full
                : PackageGraphNodePresentationLevel.Standard;
        }
    }

    internal sealed class PackageGraphLayoutResult
    {
        public PackageGraphLayoutResult(
            PackageGraphLayoutMode mode,
            string focusPackageId,
            float canvasWidth,
            float canvasHeight,
            Rect hubRect,
            Vector2 activeCenter,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings,
            IEnumerable<PackageGraphRingGuide> ringGuides,
            IEnumerable<PackageGraphSectorLabel> sectorLabels = null,
            int unrelatedPackageCount = 0,
            Rect unrelatedSummaryRect = default(Rect),
            IEnumerable<PackageGraphGroupLayoutNode> groupNodes = null,
            string focusGroupId = null,
            IReadOnlyDictionary<string, PackageGraphNodePresentationLevel> nodePresentationLevels = null)
        {
            Mode = mode;
            FocusPackageId = focusPackageId ?? string.Empty;
            FocusGroupId = focusGroupId ?? string.Empty;
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            HubRect = hubRect;
            ActiveCenter = activeCenter;
            NodeRects = nodeRects ?? new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
            NodeRings = nodeRings ?? new Dictionary<string, PackageGraphLayoutRing>(StringComparer.OrdinalIgnoreCase);
            RingGuides = ringGuides == null
                ? Array.Empty<PackageGraphRingGuide>()
                : ringGuides.Where(guide => guide != null).ToArray();
            SectorLabels = sectorLabels == null
                ? Array.Empty<PackageGraphSectorLabel>()
                : sectorLabels.Where(label => label != null).ToArray();
            UnrelatedPackageCount = Math.Max(0, unrelatedPackageCount);
            UnrelatedSummaryRect = unrelatedSummaryRect;
            GroupNodes = groupNodes == null
                ? Array.Empty<PackageGraphGroupLayoutNode>()
                : groupNodes.Where(node => node != null).ToArray();
            NodePresentationLevels = nodePresentationLevels ??
                                     new Dictionary<string, PackageGraphNodePresentationLevel>(StringComparer.OrdinalIgnoreCase);
        }

        public PackageGraphLayoutMode Mode { get; }

        public string FocusPackageId { get; }

        public string FocusGroupId { get; }

        public float CanvasWidth { get; }

        public float CanvasHeight { get; }

        public Rect HubRect { get; }

        public Vector2 ActiveCenter { get; }

        public IReadOnlyDictionary<string, Rect> NodeRects { get; }

        public IReadOnlyDictionary<string, PackageGraphLayoutRing> NodeRings { get; }

        public IReadOnlyList<PackageGraphRingGuide> RingGuides { get; }

        public IReadOnlyList<PackageGraphSectorLabel> SectorLabels { get; }

        public int UnrelatedPackageCount { get; }

        public Rect UnrelatedSummaryRect { get; }

        public IReadOnlyList<PackageGraphGroupLayoutNode> GroupNodes { get; }

        public IReadOnlyDictionary<string, PackageGraphNodePresentationLevel> NodePresentationLevels { get; }

        public bool HasUnrelatedSummary => UnrelatedPackageCount > 0 && UnrelatedSummaryRect.width > 0.01f;
    }

    internal sealed class PackageGraphGroupLayoutNode
    {
        public PackageGraphGroupLayoutNode(
            PackageGraphGroup group,
            Rect rect,
            Rect hubRect,
            PackageGraphLayoutRing ring,
            int packageCount,
            int installedCount,
            int missingCount,
            int updateCount,
            bool focused,
            bool collapsed,
            float orbitRadius = 0f,
            string summaryLabel = null,
            IEnumerable<string> representedPackageIds = null)
        {
            Group = group;
            Rect = rect;
            HubRect = hubRect.width > 0.01f && hubRect.height > 0.01f ? hubRect : rect;
            Ring = ring;
            PackageCount = Math.Max(0, packageCount);
            InstalledCount = Math.Max(0, installedCount);
            MissingCount = Math.Max(0, missingCount);
            UpdateCount = Math.Max(0, updateCount);
            Focused = focused;
            Collapsed = collapsed;
            OrbitRadius = Mathf.Max(0f, orbitRadius);
            SummaryLabel = summaryLabel ?? string.Empty;
            RepresentedPackageIds = representedPackageIds == null
                ? Array.Empty<string>()
                : representedPackageIds
                    .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        public PackageGraphGroup Group { get; }

        public string GroupId => Group != null ? Group.Id : string.Empty;

        public Rect Rect { get; }

        public Rect HubRect { get; }

        public Vector2 HubCenter => HubRect.center;

        public float HubRadius => Mathf.Min(HubRect.width, HubRect.height) * 0.5f;

        public PackageGraphLayoutRing Ring { get; }

        public int PackageCount { get; }

        public int InstalledCount { get; }

        public int MissingCount { get; }

        public int UpdateCount { get; }

        public bool Focused { get; }

        public bool Collapsed { get; }

        public float OrbitRadius { get; }

        public string SummaryLabel { get; }

        public IReadOnlyList<string> RepresentedPackageIds { get; }
    }

    internal sealed class PackageGraphRingGuide
    {
        public PackageGraphRingGuide(string label, PackageGraphLayoutRing ring, Vector2 center, float radiusX, float radiusY)
        {
            Label = label ?? string.Empty;
            Ring = ring;
            Center = center;
            RadiusX = radiusX;
            RadiusY = radiusY;
        }

        public string Label { get; }

        public PackageGraphLayoutRing Ring { get; }

        public Vector2 Center { get; }

        public float RadiusX { get; }

        public float RadiusY { get; }
    }

    internal sealed class PackageGraphSectorLabel
    {
        public PackageGraphSectorLabel(
            string label,
            PackageGraphLayoutRing ring,
            Vector2 position,
            string className = null)
        {
            Label = label ?? string.Empty;
            Ring = ring;
            Position = position;
            ClassName = string.IsNullOrWhiteSpace(className) ? string.Empty : className.Trim();
        }

        public string Label { get; }

        public PackageGraphLayoutRing Ring { get; }

        public Vector2 Position { get; }

        public string ClassName { get; }
    }

    internal sealed class PackageGraphLayout
    {
        public const float CanvasWidth = 4000f;
        public const float CanvasHeight = 3800f;
        public const float NodeWidth = 206f;
        public const float NodeHeight = 104f;

        private const float HubWidth = 188f;
        private const float HubHeight = 188f;
        private const float GroupHubSize = 72f;
        private const float FocusGroupHubSize = 88f;
        private const float GroupChipHubSize = 58f;
        private const float GroupCaptionWidth = 154f;
        private const float FocusGroupCaptionWidth = 178f;
        private const float GroupChipCaptionWidth = 132f;
        private const float GroupCaptionHeight = 78f;
        private const float FocusGroupCaptionHeight = 88f;
        private const float GroupChipCaptionHeight = 58f;
        private const float NodeGap = 22f;
        private const float MinimumGlobalGroupOrbitRadius = 560f;
        private const float MinimumClusterGap = 56f;
        private const float FocusOrbitRadius = 335f;
        private const float FocusGridGapX = 48f;
        private const float FocusGridGapY = 28f;
        private const float ContextGroupBaseOffset = 118f;
        private const float ContextGroupCollisionPadding = 18f;
        private const float CategoryCaptionClearance = 24f;

        public static readonly Vector2 GraphCenter = new Vector2(2000f, 1850f);

        public PackageGraphLayoutResult Calculate(PackageGraphModel graph)
        {
            return Calculate(graph, PackageGraphLayoutMode.Overview, string.Empty, string.Empty, Vector2.zero);
        }

        public PackageGraphLayoutResult Calculate(
            PackageGraphModel graph,
            PackageGraphLayoutMode mode,
            string focusPackageId)
        {
            return Calculate(graph, mode, focusPackageId, string.Empty, Vector2.zero);
        }

        public PackageGraphLayoutResult Calculate(
            PackageGraphModel graph,
            PackageGraphLayoutMode mode,
            string focusPackageId,
            Vector2 viewportSize)
        {
            return Calculate(graph, mode, focusPackageId, string.Empty, viewportSize);
        }

        public PackageGraphLayoutResult Calculate(
            PackageGraphModel graph,
            PackageGraphLayoutMode mode,
            string focusPackageId,
            string focusGroupId,
            Vector2 viewportSize)
        {
            return Calculate(
                graph,
                mode,
                focusPackageId,
                focusGroupId,
                viewportSize,
                PackageGraphPresentationPolicy.GetDefaultForMode(mode));
        }

        public PackageGraphLayoutResult Calculate(
            PackageGraphModel graph,
            PackageGraphLayoutMode mode,
            string focusPackageId,
            string focusGroupId,
            Vector2 viewportSize,
            PackageGraphNodePresentationLevel presentationLevel)
        {
            PackageGraphModel safeGraph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());

            if (mode == PackageGraphLayoutMode.GroupFocus &&
                TryGetGroup(safeGraph, focusGroupId, out PackageGraphGroup focusedGroup))
            {
                return CalculateGroupFocus(safeGraph, focusedGroup, presentationLevel);
            }

            if (mode == PackageGraphLayoutMode.Focus &&
                TryGetPackage(safeGraph, focusPackageId, out PackageGraphNode focusedPackage))
            {
                return CalculatePackageFocus(safeGraph, focusedPackage);
            }

            return CalculateRootOverview(safeGraph, presentationLevel);
        }

        private static PackageGraphLayoutResult CalculateRootOverview(
            PackageGraphModel graph,
            PackageGraphNodePresentationLevel presentationLevel)
        {
            Dictionary<string, Rect> nodeRects = CreateNodeRingDictionary(graph, out Dictionary<string, PackageGraphLayoutRing> nodeRings);
            Dictionary<string, PackageGraphNodePresentationLevel> nodePresentations =
                new Dictionary<string, PackageGraphNodePresentationLevel>(StringComparer.OrdinalIgnoreCase);
            List<PackageGraphGroupLayoutNode> groupNodes = new List<PackageGraphGroupLayoutNode>();
            List<PackageGraphRingGuide> ringGuides = new List<PackageGraphRingGuide>();
            Rect hubRect = CreateOverviewHubRect();
            PackageGraphNodeMetrics packageMetrics = PackageGraphPresentationPolicy.GetMetrics(presentationLevel);

            PackageGraphGroup[] topGroups = GetTopLevelGroups(graph).ToArray();
            float globalOrbitRadius = CalculateGlobalGroupOrbitRadius(graph, topGroups, packageMetrics);

            if (topGroups.Length > 0)
            {
                ringGuides.Add(new PackageGraphRingGuide(
                    string.Empty,
                    PackageGraphLayoutRing.Infrastructure,
                    GraphCenter,
                    globalOrbitRadius,
                    globalOrbitRadius));
            }

            float[] groupAngles = CreateEvenCircleAngles(topGroups.Length, -90f);

            for (int index = 0; index < topGroups.Length; index++)
            {
                PackageGraphGroup group = topGroups[index];
                Vector2 groupCenter = PointOnOrbit(GraphCenter, groupAngles[index], globalOrbitRadius);
                Rect groupRect = CreateGroupElementRect(groupCenter, GroupHubSize, GroupCaptionWidth, GroupCaptionHeight);
                Rect groupHubRect = CreateHubRect(groupCenter, GroupHubSize);
                IReadOnlyList<PackageGraphNode> directPackages = GetDirectPackages(graph, group.Id);
                IReadOnlyList<PackageGraphGroup> directGroups = GetChildGroups(graph, group.Id);
                int childCount = directPackages.Count + directGroups.Count;
                float localRadius = CalculateLocalOrbitRadius(
                    childCount,
                    packageMetrics,
                    GroupChipCaptionWidth,
                    GroupChipHubSize + GroupChipCaptionHeight);

                groupNodes.Add(CreateGroupLayoutNode(
                    graph,
                    group,
                    groupRect,
                    groupHubRect,
                    focused: false,
                    collapsed: false,
                    orbitRadius: localRadius));

                PlaceDirectChildren(
                    graph,
                    group,
                    groupCenter,
                    localRadius,
                    directPackages,
                    directGroups,
                    nodeRects,
                    nodeRings,
                    nodePresentations,
                    groupNodes,
                    presentationLevel);
            }

            return new PackageGraphLayoutResult(
                PackageGraphLayoutMode.Overview,
                string.Empty,
                CanvasWidth,
                CanvasHeight,
                hubRect,
                hubRect.center,
                nodeRects,
                nodeRings,
                ringGuides,
                Array.Empty<PackageGraphSectorLabel>(),
                groupNodes: groupNodes,
                nodePresentationLevels: nodePresentations);
        }

        private static PackageGraphLayoutResult CalculateGroupFocus(
            PackageGraphModel graph,
            PackageGraphGroup focusedGroup,
            PackageGraphNodePresentationLevel presentationLevel)
        {
            Dictionary<string, Rect> nodeRects = CreateNodeRingDictionary(graph, out Dictionary<string, PackageGraphLayoutRing> nodeRings);
            Dictionary<string, PackageGraphNodePresentationLevel> nodePresentations =
                new Dictionary<string, PackageGraphNodePresentationLevel>(StringComparer.OrdinalIgnoreCase);
            List<PackageGraphGroupLayoutNode> groupNodes = new List<PackageGraphGroupLayoutNode>();
            List<PackageGraphRingGuide> ringGuides = new List<PackageGraphRingGuide>();
            PackageGraphNodeMetrics packageMetrics = PackageGraphPresentationPolicy.GetMetrics(presentationLevel);

            IReadOnlyList<PackageGraphNode> directPackages = GetDirectPackages(graph, focusedGroup.Id);
            IReadOnlyList<PackageGraphGroup> directGroups = GetChildGroups(graph, focusedGroup.Id);
            int childCount = directPackages.Count + directGroups.Count;
            float localRadius = Mathf.Max(
                260f,
                CalculateLocalOrbitRadius(
                    childCount,
                    packageMetrics,
                    GroupChipCaptionWidth,
                    GroupChipHubSize + GroupChipCaptionHeight));
            Rect focusedGroupRect = CreateGroupElementRect(
                GraphCenter,
                FocusGroupHubSize,
                FocusGroupCaptionWidth,
                FocusGroupCaptionHeight);
            Rect focusedGroupHubRect = CreateHubRect(GraphCenter, FocusGroupHubSize);
            groupNodes.Add(CreateGroupLayoutNode(
                graph,
                focusedGroup,
                focusedGroupRect,
                focusedGroupHubRect,
                focused: true,
                collapsed: false,
                orbitRadius: childCount > 0 ? localRadius : 0f));

            PlaceDirectChildren(
                graph,
                focusedGroup,
                GraphCenter,
                localRadius,
                directPackages,
                directGroups,
                nodeRects,
                nodeRings,
                nodePresentations,
                groupNodes,
                presentationLevel);

            return new PackageGraphLayoutResult(
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                CanvasWidth,
                CanvasHeight,
                CreateFocusHubRect(),
                GraphCenter,
                nodeRects,
                nodeRings,
                ringGuides,
                Array.Empty<PackageGraphSectorLabel>(),
                groupNodes: groupNodes,
                focusGroupId: focusedGroup.Id,
                nodePresentationLevels: nodePresentations);
        }

        private static PackageGraphLayoutResult CalculatePackageFocus(
            PackageGraphModel graph,
            PackageGraphNode focusNode)
        {
            Dictionary<string, Rect> nodeRects = CreateNodeRingDictionary(graph, out Dictionary<string, PackageGraphLayoutRing> nodeRings);
            Dictionary<string, PackageGraphNodePresentationLevel> nodePresentations =
                new Dictionary<string, PackageGraphNodePresentationLevel>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphNode> nodeById = graph.Nodes
                .Where(node => node != null)
                .GroupBy(node => node.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            nodeRects[focusNode.PackageId] = CenteredRect(
                GraphCenter,
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Full));
            nodePresentations[focusNode.PackageId] = PackageGraphNodePresentationLevel.Full;
            placed.Add(focusNode.PackageId);

            string focusPackageId = focusNode.PackageId;
            List<PackageGraphNode> providers = GetProviderNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> integrationNodes = GetIntegrationNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> dependents = GetDependentNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> optionalCompanions = GetOptionalCompanionNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> suiteNodes = GetSuiteNodes(graph, nodeById, focusPackageId, placed);

            PlaceColumn(
                providers,
                new Vector2(GraphCenter.x - 455f, GraphCenter.y),
                nodeRects,
                nodePresentations,
                placed);
            PlaceColumn(
                dependents,
                new Vector2(GraphCenter.x + 455f, GraphCenter.y),
                nodeRects,
                nodePresentations,
                placed);
            PlaceRow(
                integrationNodes,
                new Vector2(GraphCenter.x, GraphCenter.y + 365f),
                nodeRects,
                nodePresentations,
                placed);
            PlaceRow(
                MergeGroups(optionalCompanions, suiteNodes),
                new Vector2(GraphCenter.x, GraphCenter.y - 365f),
                nodeRects,
                nodePresentations,
                placed);

            List<PackageGraphGroupLayoutNode> contextGroups = CreateContextRelatedGroups(
                graph,
                focusNode,
                nodeRects,
                placed);

            return new PackageGraphLayoutResult(
                PackageGraphLayoutMode.Focus,
                focusNode.PackageId,
                CanvasWidth,
                CanvasHeight,
                CreateFocusHubRect(),
                GraphCenter,
                nodeRects,
                nodeRings,
                Array.Empty<PackageGraphRingGuide>(),
                Array.Empty<PackageGraphSectorLabel>(),
                groupNodes: contextGroups,
                focusGroupId: focusNode.GroupId,
                nodePresentationLevels: nodePresentations);
        }

        private static Dictionary<string, Rect> CreateNodeRingDictionary(
            PackageGraphModel graph,
            out Dictionary<string, PackageGraphLayoutRing> nodeRings)
        {
            nodeRings = new Dictionary<string, PackageGraphLayoutRing>(StringComparer.OrdinalIgnoreCase);

            if (graph != null)
            {
                foreach (PackageGraphNode node in graph.Nodes)
                {
                    if (node != null)
                    {
                        nodeRings[node.PackageId] = ResolveRing(node);
                    }
                }
            }

            return new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        }

        private static void PlaceDirectChildren(
            PackageGraphModel graph,
            PackageGraphGroup parentGroup,
            Vector2 center,
            float radius,
            IReadOnlyList<PackageGraphNode> directPackages,
            IReadOnlyList<PackageGraphGroup> directGroups,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphLayoutRing> nodeRings,
            IDictionary<string, PackageGraphNodePresentationLevel> nodePresentations,
            ICollection<PackageGraphGroupLayoutNode> groupNodes,
            PackageGraphNodePresentationLevel packagePresentationLevel)
        {
            List<ChildPlacement> children = new List<ChildPlacement>();
            PackageGraphNodeMetrics packageMetrics = PackageGraphPresentationPolicy.GetMetrics(packagePresentationLevel);

            foreach (PackageGraphGroup childGroup in directGroups)
            {
                children.Add(ChildPlacement.ForGroup(childGroup));
            }

            foreach (PackageGraphNode package in directPackages)
            {
                children.Add(ChildPlacement.ForPackage(package));
            }

            ChildPlacement[] orderedChildren = children
                .OrderBy(child => child.SortOrder)
                .ThenBy(child => child.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(child => child.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            float[] angles = CreateEvenCircleAngles(
                orderedChildren.Length,
                orderedChildren.Length > 1 ? -90f + 180f / orderedChildren.Length : -90f);

            for (int index = 0; index < orderedChildren.Length; index++)
            {
                Vector2 childCenter = PointOnOrbit(center, angles[index], radius);
                ChildPlacement child = orderedChildren[index];

                if (child.Group != null)
                {
                    Rect groupRect = CreateGroupElementRect(
                        childCenter,
                        GroupChipHubSize,
                        GroupChipCaptionWidth,
                        GroupChipCaptionHeight);
                    Rect groupHubRect = CreateHubRect(childCenter, GroupChipHubSize);
                    groupNodes.Add(CreateGroupLayoutNode(
                        graph,
                        child.Group,
                        groupRect,
                        groupHubRect,
                        focused: false,
                        collapsed: true));
                    continue;
                }

                Rect packageRect = CenteredRect(childCenter, packageMetrics);
                nodeRects[child.Package.PackageId] = ClampToCanvas(packageRect);
                nodeRings[child.Package.PackageId] = ResolveRing(parentGroup.Id);
                nodePresentations[child.Package.PackageId] = packagePresentationLevel;
            }
        }

        private static List<PackageGraphGroupLayoutNode> CreateContextRelatedGroups(
            PackageGraphModel graph,
            PackageGraphNode focusNode,
            IReadOnlyDictionary<string, Rect> nodeRects,
            ISet<string> placedPackageIds)
        {
            List<PackageGraphGroupLayoutNode> groupNodes = new List<PackageGraphGroupLayoutNode>();
            Dictionary<string, List<PackageGraphNode>> relatedByTopGroup =
                new Dictionary<string, List<PackageGraphNode>>(StringComparer.OrdinalIgnoreCase);
            PackageGraphGroup focusTopGroup = GetTopLevelGroupForPackage(graph, focusNode);
            string focusTopGroupId = focusTopGroup != null ? focusTopGroup.Id : string.Empty;

            foreach (PackageGraphNode node in graph.Nodes)
            {
                if (node == null ||
                    focusNode == null ||
                    string.Equals(node.PackageId, focusNode.PackageId, StringComparison.OrdinalIgnoreCase) ||
                    !placedPackageIds.Contains(node.PackageId) ||
                    !nodeRects.ContainsKey(node.PackageId))
                {
                    continue;
                }

                PackageGraphGroup topGroup = GetTopLevelGroupForPackage(graph, node);
                if (topGroup == null ||
                    string.Equals(topGroup.Id, focusTopGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!relatedByTopGroup.TryGetValue(topGroup.Id, out List<PackageGraphNode> packageNodes))
                {
                    packageNodes = new List<PackageGraphNode>();
                    relatedByTopGroup[topGroup.Id] = packageNodes;
                }

                packageNodes.Add(node);
            }

            PackageGraphGroup[] orderedGroups = relatedByTopGroup.Keys
                .Select(groupId => graph.Groups.FirstOrDefault(group =>
                    string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase)))
                .Where(group => group != null)
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (PackageGraphGroup group in orderedGroups)
            {
                List<PackageGraphNode> packages = relatedByTopGroup[group.Id]
                    .OrderBy(GetRelationshipSortIndex)
                    .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Vector2 averageCenter = CalculateAverageCenter(packages, nodeRects);
                Vector2 direction = averageCenter - GraphCenter;

                if (direction.sqrMagnitude < 1f)
                {
                    direction = Vector2.right;
                }

                Rect rect = FindContextGroupRect(
                    averageCenter,
                    direction.normalized,
                    nodeRects.Values,
                    groupNodes.Select(groupNode => groupNode.Rect));
                Rect hubRect = CreateGroupHubRectFromElement(rect, GroupChipHubSize);
                groupNodes.Add(CreateGroupLayoutNode(
                    graph,
                    group,
                    rect,
                    hubRect,
                    focused: false,
                    collapsed: true,
                    packageScope: packages,
                    summaryLabel: packages.Count == 1 ? "1 related package" : packages.Count + " related packages"));
            }

            return groupNodes;
        }

        private static Rect FindContextGroupRect(
            Vector2 anchor,
            Vector2 direction,
            IEnumerable<Rect> nodeRects,
            IEnumerable<Rect> groupRects)
        {
            Vector2 safeDirection = direction.sqrMagnitude < 0.01f ? Vector2.right : direction.normalized;
            Vector2 perpendicular = new Vector2(-safeDirection.y, safeDirection.x);
            List<Rect> occupiedRects = nodeRects
                .Concat(groupRects)
                .Select(rect => ExpandRect(rect, ContextGroupCollisionPadding))
                .ToList();
            float[] offsets =
            {
                ContextGroupBaseOffset,
                ContextGroupBaseOffset + 76f,
                ContextGroupBaseOffset + 152f,
                ContextGroupBaseOffset + 228f
            };
            float[] perpendicularOffsets =
            {
                0f,
                GroupChipHubSize + 24f,
                -(GroupChipHubSize + 24f),
                (GroupChipHubSize + 24f) * 1.55f,
                -(GroupChipHubSize + 24f) * 1.55f
            };

            foreach (float offset in offsets)
            {
                foreach (float perpendicularOffset in perpendicularOffsets)
                {
                    Rect candidate = ClampToCanvas(CreateGroupElementRect(
                        anchor + safeDirection * offset + perpendicular * perpendicularOffset,
                        GroupChipHubSize,
                        GroupChipCaptionWidth,
                        GroupChipCaptionHeight));

                    if (!occupiedRects.Any(rect => rect.Overlaps(candidate)))
                    {
                        return candidate;
                    }
                }
            }

            return ClampToCanvas(CreateGroupElementRect(
                anchor + safeDirection * offsets[offsets.Length - 1],
                GroupChipHubSize,
                GroupChipCaptionWidth,
                GroupChipCaptionHeight));
        }

        private static Vector2 CalculateAverageCenter(
            IReadOnlyList<PackageGraphNode> packages,
            IReadOnlyDictionary<string, Rect> nodeRects)
        {
            if (packages == null || packages.Count == 0)
            {
                return GraphCenter;
            }

            Vector2 total = Vector2.zero;
            int count = 0;

            foreach (PackageGraphNode package in packages)
            {
                if (package != null && nodeRects.TryGetValue(package.PackageId, out Rect rect))
                {
                    total += rect.center;
                    count++;
                }
            }

            return count == 0 ? GraphCenter : total / count;
        }

        private static PackageGraphGroupLayoutNode CreateGroupLayoutNode(
            PackageGraphModel graph,
            PackageGraphGroup group,
            Rect rect,
            Rect hubRect,
            bool focused,
            bool collapsed,
            IEnumerable<PackageGraphNode> packageScope = null,
            float orbitRadius = 0f,
            string summaryLabel = null)
        {
            PackageGraphNode[] packages = packageScope == null
                ? GetDescendantPackages(graph, group.Id).ToArray()
                : packageScope.Where(node => node != null).ToArray();
            int installedCount = packages.Count(node => node.IsInstalled);
            int missingCount = packages.Count(node =>
                node.Status == PackageGraphNodeStatus.NotInstalled ||
                node.Status == PackageGraphNodeStatus.Missing ||
                node.Status == PackageGraphNodeStatus.Warning);
            int updateCount = packages.Count(node => node.Status == PackageGraphNodeStatus.UpdateAvailable);
            Rect clampedRect = ClampToCanvas(rect);
            Vector2 hubOffset = hubRect.position - rect.position;
            Rect clampedHubRect = new Rect(
                clampedRect.x + hubOffset.x,
                clampedRect.y + hubOffset.y,
                hubRect.width,
                hubRect.height);

            return new PackageGraphGroupLayoutNode(
                group,
                clampedRect,
                clampedHubRect,
                ResolveRing(group.Id),
                packages.Length,
                installedCount,
                missingCount,
                updateCount,
                focused,
                collapsed,
                orbitRadius,
                summaryLabel,
                packages.Select(package => package.PackageId));
        }

        private static List<PackageGraphNode> GetProviderNodes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, PackageGraphNode> nodeById,
            string focusPackageId,
            ISet<string> placed)
        {
            return graph.Edges
                .Where(edge => edge.Kind == PackageGraphEdgeKind.HardDependency &&
                               string.Equals(edge.ToPackageId, focusPackageId, StringComparison.OrdinalIgnoreCase))
                .Select(edge => GetNode(nodeById, edge.FromPackageId))
                .Where(node => node != null && !placed.Contains(node.PackageId))
                .OrderBy(GetRelationshipSortIndex)
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<PackageGraphNode> GetDependentNodes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, PackageGraphNode> nodeById,
            string focusPackageId,
            ISet<string> placed)
        {
            return graph.Edges
                .Where(edge => edge.Kind == PackageGraphEdgeKind.HardDependency &&
                               string.Equals(edge.FromPackageId, focusPackageId, StringComparison.OrdinalIgnoreCase))
                .Select(edge => GetNode(nodeById, edge.ToPackageId))
                .Where(node => node != null &&
                               node.NodeType != PackageGraphNodeType.Integration &&
                               !placed.Contains(node.PackageId))
                .OrderBy(GetRelationshipSortIndex)
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<PackageGraphNode> GetIntegrationNodes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, PackageGraphNode> nodeById,
            string focusPackageId,
            ISet<string> placed)
        {
            return graph.Edges
                .Where(edge =>
                    (edge.Kind == PackageGraphEdgeKind.IntegrationConnection ||
                     edge.Kind == PackageGraphEdgeKind.HardDependency) &&
                    edge.ConnectsPackage(focusPackageId))
                .Select(edge => GetOtherNode(nodeById, edge, focusPackageId))
                .Where(node => node != null &&
                               node.NodeType == PackageGraphNodeType.Integration &&
                               !placed.Contains(node.PackageId))
                .Distinct(PackageGraphNodePackageIdComparer.Instance)
                .OrderBy(GetRelationshipSortIndex)
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<PackageGraphNode> GetOptionalCompanionNodes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, PackageGraphNode> nodeById,
            string focusPackageId,
            ISet<string> placed)
        {
            return graph.Edges
                .Where(edge => edge.Kind == PackageGraphEdgeKind.OptionalCompanion &&
                               edge.ConnectsPackage(focusPackageId))
                .Select(edge => GetOtherNode(nodeById, edge, focusPackageId))
                .Where(node => node != null && !placed.Contains(node.PackageId))
                .Distinct(PackageGraphNodePackageIdComparer.Instance)
                .OrderBy(GetRelationshipSortIndex)
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<PackageGraphNode> GetSuiteNodes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, PackageGraphNode> nodeById,
            string focusPackageId,
            ISet<string> placed)
        {
            HashSet<string> packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphSuiteRegion region in graph.SuiteRegions)
            {
                bool focusIsSuite = string.Equals(region.SuitePackageId, focusPackageId, StringComparison.OrdinalIgnoreCase);
                bool focusIsMember = region.MemberPackageIds.Any(memberPackageId =>
                    string.Equals(memberPackageId, focusPackageId, StringComparison.OrdinalIgnoreCase));

                if (!focusIsSuite && !focusIsMember)
                {
                    continue;
                }

                if (focusIsSuite)
                {
                    foreach (string memberPackageId in region.MemberPackageIds)
                    {
                        packageIds.Add(memberPackageId);
                    }
                }
                else
                {
                    packageIds.Add(region.SuitePackageId);
                }
            }

            return packageIds
                .Select(packageId => GetNode(nodeById, packageId))
                .Where(node => node != null && !placed.Contains(node.PackageId))
                .OrderBy(GetRelationshipSortIndex)
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<PackageGraphNode> MergeGroups(params IEnumerable<PackageGraphNode>[] groups)
        {
            return groups
                .Where(group => group != null)
                .SelectMany(group => group)
                .Where(node => node != null)
                .Distinct(PackageGraphNodePackageIdComparer.Instance)
                .OrderBy(GetRelationshipSortIndex)
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static PackageGraphNode GetNode(
            IReadOnlyDictionary<string, PackageGraphNode> nodeById,
            string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) &&
                   nodeById.TryGetValue(packageId, out PackageGraphNode node)
                ? node
                : null;
        }

        private static PackageGraphNode GetOtherNode(
            IReadOnlyDictionary<string, PackageGraphNode> nodeById,
            PackageGraphEdge edge,
            string packageId)
        {
            return edge == null ? null : GetNode(nodeById, edge.GetOtherPackageId(packageId));
        }

        private static void PlaceColumn(
            IReadOnlyList<PackageGraphNode> nodes,
            Vector2 center,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphNodePresentationLevel> nodePresentations,
            ISet<string> placed)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            PackageGraphNodeMetrics metrics = PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Standard);
            float stepY = metrics.Height + Mathf.Max(FocusGridGapY, NodeGap * 2f + 4f);
            float originY = center.y - ((nodes.Count - 1) * stepY) * 0.5f;

            for (int index = 0; index < nodes.Count; index++)
            {
                PackageGraphNode node = nodes[index];
                Vector2 nodeCenter = new Vector2(center.x, originY + index * stepY);
                nodeRects[node.PackageId] = ClampToCanvas(CenteredRect(nodeCenter, metrics));
                nodePresentations[node.PackageId] = PackageGraphNodePresentationLevel.Standard;
                placed.Add(node.PackageId);
            }
        }

        private static void PlaceRow(
            IReadOnlyList<PackageGraphNode> nodes,
            Vector2 center,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphNodePresentationLevel> nodePresentations,
            ISet<string> placed)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            PackageGraphNodeMetrics metrics = PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Standard);
            float stepX = metrics.Width + FocusGridGapX;
            float originX = center.x - ((nodes.Count - 1) * stepX) * 0.5f;

            for (int index = 0; index < nodes.Count; index++)
            {
                PackageGraphNode node = nodes[index];
                Vector2 nodeCenter = new Vector2(originX + index * stepX, center.y);
                nodeRects[node.PackageId] = ClampToCanvas(CenteredRect(nodeCenter, metrics));
                nodePresentations[node.PackageId] = PackageGraphNodePresentationLevel.Standard;
                placed.Add(node.PackageId);
            }
        }

        private static IReadOnlyList<PackageGraphGroup> GetTopLevelGroups(PackageGraphModel graph)
        {
            return graph.Groups
                .Where(group => group != null && string.IsNullOrWhiteSpace(group.ParentGroupId))
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyList<PackageGraphGroup> GetChildGroups(PackageGraphModel graph, string parentGroupId)
        {
            return graph.Groups
                .Where(group => group != null &&
                                string.Equals(group.ParentGroupId, parentGroupId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyList<PackageGraphNode> GetDirectPackages(PackageGraphModel graph, string groupId)
        {
            return graph.Nodes
                .Where(node => node != null &&
                               string.Equals(node.GroupId, groupId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(GetRelationshipSortIndex)
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyList<PackageGraphNode> GetDescendantPackages(PackageGraphModel graph, string groupId)
        {
            HashSet<string> descendantGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectDescendantGroupIds(graph, groupId, descendantGroupIds);
            return graph.Nodes
                .Where(node => node != null && descendantGroupIds.Contains(node.GroupId))
                .ToArray();
        }

        private static void CollectDescendantGroupIds(
            PackageGraphModel graph,
            string groupId,
            ISet<string> result)
        {
            if (string.IsNullOrWhiteSpace(groupId) || !result.Add(groupId))
            {
                return;
            }

            foreach (PackageGraphGroup childGroup in GetChildGroups(graph, groupId))
            {
                CollectDescendantGroupIds(graph, childGroup.Id, result);
            }
        }

        private static PackageGraphGroup GetTopLevelGroupForPackage(PackageGraphModel graph, PackageGraphNode node)
        {
            if (node == null || !TryGetGroup(graph, node.GroupId, out PackageGraphGroup group))
            {
                return null;
            }

            while (!string.IsNullOrWhiteSpace(group.ParentGroupId) &&
                   TryGetGroup(graph, group.ParentGroupId, out PackageGraphGroup parentGroup))
            {
                group = parentGroup;
            }

            return group;
        }

        private static bool TryGetPackage(PackageGraphModel graph, string packageId, out PackageGraphNode node)
        {
            node = graph.Nodes.FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            return node != null;
        }

        private static bool TryGetGroup(PackageGraphModel graph, string groupId, out PackageGraphGroup group)
        {
            group = graph.Groups.FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.Id, groupId, StringComparison.OrdinalIgnoreCase));
            return group != null;
        }

        private static float[] CreateEvenCircleAngles(int count, float startAngleDegrees)
        {
            if (count <= 0)
            {
                return Array.Empty<float>();
            }

            float[] angles = new float[count];
            float step = 360f / count;

            for (int index = 0; index < count; index++)
            {
                angles[index] = startAngleDegrees + step * index;
            }

            return angles;
        }

        private static float CalculateLocalOrbitRadius(
            int childCount,
            PackageGraphNodeMetrics packageMetrics,
            float groupWidth,
            float groupHeight)
        {
            float childRadialHalfExtent = Mathf.Max(
                Mathf.Max(packageMetrics.Width, packageMetrics.Height),
                Mathf.Max(groupWidth, groupHeight)) * 0.5f;
            float groupRadialHalfExtent = CalculateHalfDiagonal(GroupCaptionWidth, GroupHubSize + GroupCaptionHeight);
            float centerClearanceRadius = childRadialHalfExtent + groupRadialHalfExtent + NodeGap + CategoryCaptionClearance;

            if (childCount <= 0)
            {
                return 0f;
            }

            if (childCount == 1)
            {
                return Mathf.Max(118f, centerClearanceRadius);
            }

            if (childCount == 2)
            {
                return Mathf.Max(148f, centerClearanceRadius);
            }

            float chordFootprint = Mathf.Max(packageMetrics.Width, groupWidth) + NodeGap;
            float chordRadius = chordFootprint / Mathf.Max(0.01f, 2f * Mathf.Sin(Mathf.PI / childCount));
            float radialFootprint = Mathf.Max(packageMetrics.Height, groupHeight) * 0.5f;
            return Mathf.Clamp(chordRadius + radialFootprint * 0.28f, Mathf.Max(centerClearanceRadius, 168f), 520f);
        }

        private static float CalculateGlobalGroupOrbitRadius(
            PackageGraphModel graph,
            IReadOnlyList<PackageGraphGroup> topGroups,
            PackageGraphNodeMetrics packageMetrics)
        {
            if (topGroups == null || topGroups.Count <= 1)
            {
                return MinimumGlobalGroupOrbitRadius;
            }

            float[] clusterRadii = new float[topGroups.Count];

            for (int index = 0; index < topGroups.Count; index++)
            {
                PackageGraphGroup group = topGroups[index];
                int childCount = GetDirectPackages(graph, group.Id).Count + GetChildGroups(graph, group.Id).Count;
                float localRadius = CalculateLocalOrbitRadius(
                    childCount,
                    packageMetrics,
                    GroupChipCaptionWidth,
                    GroupChipHubSize + GroupChipCaptionHeight);
                clusterRadii[index] = CalculateClusterCollisionRadius(childCount, localRadius, packageMetrics);
            }

            float largestClusterRadius = clusterRadii.Length == 0 ? 0f : clusterRadii.Max();
            float canvasLimitedRadius = Mathf.Min(
                GraphCenter.x - largestClusterRadius - 48f,
                GraphCenter.y - largestClusterRadius - 48f,
                CanvasWidth - GraphCenter.x - largestClusterRadius - 48f,
                CanvasHeight - GraphCenter.y - largestClusterRadius - 48f);
            float candidateRadius = Mathf.Min(MinimumGlobalGroupOrbitRadius, canvasLimitedRadius);
            float[] groupAngles = CreateEvenCircleAngles(topGroups.Count, -90f);

            while (candidateRadius < canvasLimitedRadius)
            {
                if (ClustersAreSeparated(candidateRadius, groupAngles, clusterRadii))
                {
                    return candidateRadius;
                }

                candidateRadius += 24f;
            }

            return canvasLimitedRadius;
        }

        private static float CalculateClusterCollisionRadius(
            int childCount,
            float localRadius,
            PackageGraphNodeMetrics packageMetrics)
        {
            float groupHalfExtent = CalculateHalfDiagonal(GroupCaptionWidth, GroupHubSize + GroupCaptionHeight);

            if (childCount <= 0 || localRadius <= 0.01f)
            {
                return groupHalfExtent;
            }

            float childHalfDiagonal = CalculateHalfDiagonal(
                Mathf.Max(packageMetrics.Width, GroupChipCaptionWidth),
                Mathf.Max(packageMetrics.Height, GroupChipHubSize));
            float axisAlignedOrbitPadding = Mathf.Max(packageMetrics.Width, GroupChipCaptionWidth) * 0.4f;
            return Mathf.Max(groupHalfExtent, localRadius + childHalfDiagonal + axisAlignedOrbitPadding);
        }

        private static bool ClustersAreSeparated(
            float radius,
            IReadOnlyList<float> groupAngles,
            IReadOnlyList<float> clusterRadii)
        {
            for (int firstIndex = 0; firstIndex < groupAngles.Count; firstIndex++)
            {
                Vector2 first = PointOnOrbit(GraphCenter, groupAngles[firstIndex], radius);

                for (int secondIndex = firstIndex + 1; secondIndex < groupAngles.Count; secondIndex++)
                {
                    Vector2 second = PointOnOrbit(GraphCenter, groupAngles[secondIndex], radius);
                    float requiredDistance = clusterRadii[firstIndex] + clusterRadii[secondIndex] + MinimumClusterGap;

                    if (Vector2.Distance(first, second) < requiredDistance)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static float CalculateHalfDiagonal(float width, float height)
        {
            return Mathf.Sqrt(width * width + height * height) * 0.5f;
        }

        private static PackageGraphLayoutRing ResolveRing(PackageGraphNode node)
        {
            if (node == null)
            {
                return PackageGraphLayoutRing.Infrastructure;
            }

            if (node.NodeType == PackageGraphNodeType.Integration)
            {
                return PackageGraphLayoutRing.Integration;
            }

            if (node.NodeType == PackageGraphNodeType.Suite)
            {
                return PackageGraphLayoutRing.Suite;
            }

            return ResolveRing(node.GroupId);
        }

        private static PackageGraphLayoutRing ResolveRing(string groupId)
        {
            switch ((groupId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case PackageGraphHierarchyBuilder.IntegrationsGroupId:
                    return PackageGraphLayoutRing.Integration;
                case PackageGraphHierarchyBuilder.SuitesGroupId:
                    return PackageGraphLayoutRing.Suite;
                case PackageGraphHierarchyBuilder.RuntimeServicesGroupId:
                case PackageGraphHierarchyBuilder.ExperienceInteractionGroupId:
                case PackageGraphHierarchyBuilder.ToolsQualityGroupId:
                    return PackageGraphLayoutRing.Runtime;
                default:
                    return PackageGraphLayoutRing.Infrastructure;
            }
        }

        private static int GetRelationshipSortIndex(PackageGraphNode node)
        {
            if (node == null)
            {
                return 1000000;
            }

            return GetRingPriority(ResolveRing(node)) * 10000 + GetKnownPackageIndex(
                node.PackageId,
                "com.deucarian.editor",
                "com.deucarian.logging",
                "com.deucarian.api",
                "com.deucarian.core-state",
                "com.deucarian.session",
                "com.deucarian.object-loading",
                "com.deucarian.object-selection",
                "com.deucarian.ui-binding",
                "com.deucarian.theming",
                "com.deucarian.package-installer",
                "com.deucarian.diagnostics",
                "com.deucarian.session.api-integration",
                "com.deucarian.object-loading.api-integration",
                "com.deucarian.ui-binding.core-state-integration",
                "com.deucarian.object-selection.core-state-integration",
                "com.deucarian.selection-suite");
        }

        private static int GetKnownPackageIndex(string packageId, params string[] orderedPackageIds)
        {
            for (int index = 0; index < orderedPackageIds.Length; index++)
            {
                if (string.Equals(packageId, orderedPackageIds[index], StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return 1000;
        }

        private static int GetRingPriority(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Infrastructure:
                    return 0;
                case PackageGraphLayoutRing.Runtime:
                    return 1;
                case PackageGraphLayoutRing.Integration:
                    return 2;
                case PackageGraphLayoutRing.Suite:
                    return 3;
                default:
                    return 4;
            }
        }

        private static Vector2 PointOnOrbit(Vector2 center, float angleDegrees, float radius)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(
                center.x + Mathf.Cos(radians) * radius,
                center.y + Mathf.Sin(radians) * radius);
        }

        private static Rect CenteredRect(Vector2 center)
        {
            return CenteredRect(center, NodeWidth, NodeHeight);
        }

        private static Rect CenteredRect(Vector2 center, PackageGraphNodeMetrics metrics)
        {
            return CenteredRect(center, metrics.Width, metrics.Height);
        }

        private static Rect CenteredRect(Vector2 center, float width, float height)
        {
            return new Rect(
                center.x - width * 0.5f,
                center.y - height * 0.5f,
                width,
                height);
        }

        private static Rect CreateGroupElementRect(
            Vector2 hubCenter,
            float hubSize,
            float captionWidth,
            float captionHeight)
        {
            float width = Mathf.Max(hubSize, captionWidth);
            return new Rect(
                hubCenter.x - width * 0.5f,
                hubCenter.y - hubSize * 0.5f,
                width,
                hubSize + Mathf.Max(0f, captionHeight));
        }

        private static Rect CreateHubRect(Vector2 hubCenter, float hubSize)
        {
            return CenteredRect(hubCenter, hubSize, hubSize);
        }

        private static Rect CreateGroupHubRectFromElement(Rect elementRect, float hubSize)
        {
            return new Rect(
                elementRect.center.x - hubSize * 0.5f,
                elementRect.y,
                hubSize,
                hubSize);
        }

        private static Rect CreateOverviewHubRect()
        {
            return CenteredRect(GraphCenter, HubWidth, HubHeight);
        }

        private static Rect CreateFocusHubRect()
        {
            return new Rect(42f, 36f, HubWidth * 0.72f, HubHeight * 0.62f);
        }

        private static Rect ClampToCanvas(Rect rect)
        {
            float x = Mathf.Clamp(rect.x, 24f, CanvasWidth - rect.width - 24f);
            float y = Mathf.Clamp(rect.y, 24f, CanvasHeight - rect.height - 24f);
            return new Rect(x, y, rect.width, rect.height);
        }

        private static Rect ExpandRect(Rect rect, float padding)
        {
            return new Rect(
                rect.x - padding,
                rect.y - padding,
                rect.width + padding * 2f,
                rect.height + padding * 2f);
        }

        private readonly struct ChildPlacement
        {
            private ChildPlacement(PackageGraphGroup group, PackageGraphNode package)
            {
                Group = group;
                Package = package;
            }

            public PackageGraphGroup Group { get; }

            public PackageGraphNode Package { get; }

            public string Id => Group != null ? Group.Id : Package.PackageId;

            public string DisplayName => Group != null ? Group.DisplayName : Package.DisplayName;

            public int SortOrder => Group != null
                ? Group.SortOrder
                : (Package.PackageDefinition != null && Package.PackageDefinition.HasOverviewOrder
                    ? Package.PackageDefinition.OverviewOrder
                    : GetRelationshipSortIndex(Package));

            public static ChildPlacement ForGroup(PackageGraphGroup group)
            {
                return new ChildPlacement(group, null);
            }

            public static ChildPlacement ForPackage(PackageGraphNode package)
            {
                return new ChildPlacement(null, package);
            }
        }

        private sealed class PackageGraphNodePackageIdComparer : IEqualityComparer<PackageGraphNode>
        {
            public static readonly PackageGraphNodePackageIdComparer Instance =
                new PackageGraphNodePackageIdComparer();

            public bool Equals(PackageGraphNode x, PackageGraphNode y)
            {
                return ReferenceEquals(x, y) ||
                       (x != null &&
                        y != null &&
                        string.Equals(x.PackageId, y.PackageId, StringComparison.OrdinalIgnoreCase));
            }

            public int GetHashCode(PackageGraphNode obj)
            {
                return obj == null || obj.PackageId == null
                    ? 0
                    : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageId);
            }
        }
    }
}
