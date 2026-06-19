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
            string focusGroupId = null)
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

        public bool HasUnrelatedSummary => UnrelatedPackageCount > 0 && UnrelatedSummaryRect.width > 0.01f;
    }

    internal sealed class PackageGraphGroupLayoutNode
    {
        public PackageGraphGroupLayoutNode(
            PackageGraphGroup group,
            Rect rect,
            PackageGraphLayoutRing ring,
            int packageCount,
            int installedCount,
            int missingCount,
            int updateCount,
            bool focused,
            bool collapsed,
            float orbitRadius = 0f,
            string summaryLabel = null)
        {
            Group = group;
            Rect = rect;
            Ring = ring;
            PackageCount = Math.Max(0, packageCount);
            InstalledCount = Math.Max(0, installedCount);
            MissingCount = Math.Max(0, missingCount);
            UpdateCount = Math.Max(0, updateCount);
            Focused = focused;
            Collapsed = collapsed;
            OrbitRadius = Mathf.Max(0f, orbitRadius);
            SummaryLabel = summaryLabel ?? string.Empty;
        }

        public PackageGraphGroup Group { get; }

        public string GroupId => Group != null ? Group.Id : string.Empty;

        public Rect Rect { get; }

        public PackageGraphLayoutRing Ring { get; }

        public int PackageCount { get; }

        public int InstalledCount { get; }

        public int MissingCount { get; }

        public int UpdateCount { get; }

        public bool Focused { get; }

        public bool Collapsed { get; }

        public float OrbitRadius { get; }

        public string SummaryLabel { get; }
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
        public const float NodeWidth = 238f;
        public const float NodeHeight = 136f;

        private const float HubWidth = 188f;
        private const float HubHeight = 188f;
        private const float GroupWidth = 168f;
        private const float GroupHeight = 168f;
        private const float FocusGroupWidth = 188f;
        private const float FocusGroupHeight = 188f;
        private const float GroupChipWidth = 150f;
        private const float GroupChipHeight = 150f;
        private const float NodeGap = 22f;
        private const float MinimumGlobalGroupOrbitRadius = 960f;
        private const float MinimumClusterGap = 56f;
        private const float FocusOrbitRadius = 335f;
        private const float FocusGridGapX = 48f;
        private const float FocusGridGapY = 28f;

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
            PackageGraphModel safeGraph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());

            if (mode == PackageGraphLayoutMode.GroupFocus &&
                TryGetGroup(safeGraph, focusGroupId, out PackageGraphGroup focusedGroup))
            {
                return CalculateGroupFocus(safeGraph, focusedGroup);
            }

            if (mode == PackageGraphLayoutMode.Focus &&
                TryGetPackage(safeGraph, focusPackageId, out PackageGraphNode focusedPackage))
            {
                return CalculatePackageFocus(safeGraph, focusedPackage);
            }

            return CalculateRootOverview(safeGraph);
        }

        private static PackageGraphLayoutResult CalculateRootOverview(PackageGraphModel graph)
        {
            Dictionary<string, Rect> nodeRects = CreateNodeRingDictionary(graph, out Dictionary<string, PackageGraphLayoutRing> nodeRings);
            List<PackageGraphGroupLayoutNode> groupNodes = new List<PackageGraphGroupLayoutNode>();
            List<PackageGraphRingGuide> ringGuides = new List<PackageGraphRingGuide>();
            Rect hubRect = CreateOverviewHubRect();

            PackageGraphGroup[] topGroups = GetTopLevelGroups(graph).ToArray();
            float globalOrbitRadius = CalculateGlobalGroupOrbitRadius(graph, topGroups);

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
                Rect groupRect = CenteredRect(groupCenter, GroupWidth, GroupHeight);
                IReadOnlyList<PackageGraphNode> directPackages = GetDirectPackages(graph, group.Id);
                IReadOnlyList<PackageGraphGroup> directGroups = GetChildGroups(graph, group.Id);
                int childCount = directPackages.Count + directGroups.Count;
                float localRadius = CalculateLocalOrbitRadius(childCount, NodeWidth, NodeHeight, GroupChipWidth, GroupChipHeight);

                groupNodes.Add(CreateGroupLayoutNode(
                    graph,
                    group,
                    groupRect,
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
                    groupNodes);
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
                groupNodes: groupNodes);
        }

        private static PackageGraphLayoutResult CalculateGroupFocus(
            PackageGraphModel graph,
            PackageGraphGroup focusedGroup)
        {
            Dictionary<string, Rect> nodeRects = CreateNodeRingDictionary(graph, out Dictionary<string, PackageGraphLayoutRing> nodeRings);
            List<PackageGraphGroupLayoutNode> groupNodes = new List<PackageGraphGroupLayoutNode>();
            List<PackageGraphRingGuide> ringGuides = new List<PackageGraphRingGuide>();

            IReadOnlyList<PackageGraphNode> directPackages = GetDirectPackages(graph, focusedGroup.Id);
            IReadOnlyList<PackageGraphGroup> directGroups = GetChildGroups(graph, focusedGroup.Id);
            int childCount = directPackages.Count + directGroups.Count;
            float localRadius = Mathf.Max(FocusOrbitRadius, CalculateLocalOrbitRadius(childCount, NodeWidth, NodeHeight, GroupChipWidth, GroupChipHeight));
            Rect focusedGroupRect = CenteredRect(GraphCenter, FocusGroupWidth, FocusGroupHeight);
            groupNodes.Add(CreateGroupLayoutNode(
                graph,
                focusedGroup,
                focusedGroupRect,
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
                groupNodes);

            PlaceSiblingGroupChips(graph, focusedGroup, groupNodes);

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
                focusGroupId: focusedGroup.Id);
        }

        private static PackageGraphLayoutResult CalculatePackageFocus(
            PackageGraphModel graph,
            PackageGraphNode focusNode)
        {
            Dictionary<string, Rect> nodeRects = CreateNodeRingDictionary(graph, out Dictionary<string, PackageGraphLayoutRing> nodeRings);
            Dictionary<string, PackageGraphNode> nodeById = graph.Nodes
                .Where(node => node != null)
                .GroupBy(node => node.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            nodeRects[focusNode.PackageId] = CenteredRect(GraphCenter);
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
                placed);
            PlaceColumn(
                dependents,
                new Vector2(GraphCenter.x + 455f, GraphCenter.y),
                nodeRects,
                placed);
            PlaceRow(
                integrationNodes,
                new Vector2(GraphCenter.x, GraphCenter.y + 365f),
                nodeRects,
                placed);
            PlaceRow(
                MergeGroups(optionalCompanions, suiteNodes),
                new Vector2(GraphCenter.x, GraphCenter.y - 365f),
                nodeRects,
                placed);

            List<PackageGraphGroupLayoutNode> collapsedGroups = CreateCollapsedUnrelatedGroups(graph, placed);

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
                groupNodes: collapsedGroups,
                focusGroupId: focusNode.GroupId);
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
            ICollection<PackageGraphGroupLayoutNode> groupNodes)
        {
            List<ChildPlacement> children = new List<ChildPlacement>();

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
                    Rect groupRect = CenteredRect(childCenter, GroupChipWidth, GroupChipHeight);
                    groupNodes.Add(CreateGroupLayoutNode(
                        graph,
                        child.Group,
                        groupRect,
                        focused: false,
                        collapsed: true));
                    continue;
                }

                Rect packageRect = CenteredRect(childCenter);
                nodeRects[child.Package.PackageId] = ClampToCanvas(packageRect);
                nodeRings[child.Package.PackageId] = ResolveRing(parentGroup.Id);
            }
        }

        private static void PlaceSiblingGroupChips(
            PackageGraphModel graph,
            PackageGraphGroup focusedGroup,
            ICollection<PackageGraphGroupLayoutNode> groupNodes)
        {
            PackageGraphGroup[] siblings = graph.Groups
                .Where(group => group != null &&
                                !string.Equals(group.Id, focusedGroup.Id, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(
                                    group.ParentGroupId,
                                    focusedGroup.ParentGroupId,
                                    StringComparison.OrdinalIgnoreCase) &&
                                GetDescendantPackages(graph, group.Id).Count > 0)
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            PlaceGroupChipStack(
                graph,
                siblings,
                GraphCenter.x + 720f,
                GraphCenter.y,
                groupNodes,
                "sibling");
        }

        private static List<PackageGraphGroupLayoutNode> CreateCollapsedUnrelatedGroups(
            PackageGraphModel graph,
            ISet<string> placedPackageIds)
        {
            List<PackageGraphGroupLayoutNode> groupNodes = new List<PackageGraphGroupLayoutNode>();
            Dictionary<string, List<PackageGraphNode>> unrelatedByTopGroup =
                new Dictionary<string, List<PackageGraphNode>>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphNode node in graph.Nodes)
            {
                if (node == null || placedPackageIds.Contains(node.PackageId))
                {
                    continue;
                }

                PackageGraphGroup topGroup = GetTopLevelGroupForPackage(graph, node);
                string groupId = topGroup != null ? topGroup.Id : PackageGraphHierarchyBuilder.InfrastructureGroupId;

                if (!unrelatedByTopGroup.TryGetValue(groupId, out List<PackageGraphNode> packageNodes))
                {
                    packageNodes = new List<PackageGraphNode>();
                    unrelatedByTopGroup[groupId] = packageNodes;
                }

                packageNodes.Add(node);
            }

            PackageGraphGroup[] orderedGroups = unrelatedByTopGroup.Keys
                .Select(groupId => graph.Groups.FirstOrDefault(group =>
                    string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase)))
                .Where(group => group != null)
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            float stepY = GroupChipHeight + 16f;
            float originY = GraphCenter.y - ((orderedGroups.Length - 1) * stepY) * 0.5f;

            for (int index = 0; index < orderedGroups.Length; index++)
            {
                PackageGraphGroup group = orderedGroups[index];
                List<PackageGraphNode> packages = unrelatedByTopGroup[group.Id];
                Rect rect = ClampToCanvas(CenteredRect(
                    new Vector2(GraphCenter.x + 760f, originY + index * stepY),
                    GroupChipWidth,
                    GroupChipHeight));
                groupNodes.Add(CreateGroupLayoutNode(
                    graph,
                    group,
                    rect,
                    focused: false,
                    collapsed: true,
                    packageScope: packages,
                    summaryLabel: group.DisplayName + " - " + packages.Count + " unrelated"));
            }

            return groupNodes;
        }

        private static PackageGraphGroupLayoutNode CreateGroupLayoutNode(
            PackageGraphModel graph,
            PackageGraphGroup group,
            Rect rect,
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

            return new PackageGraphGroupLayoutNode(
                group,
                ClampToCanvas(rect),
                ResolveRing(group.Id),
                packages.Length,
                installedCount,
                missingCount,
                updateCount,
                focused,
                collapsed,
                orbitRadius,
                summaryLabel);
        }

        private static void PlaceGroupChipStack(
            PackageGraphModel graph,
            IReadOnlyList<PackageGraphGroup> groups,
            float x,
            float centerY,
            ICollection<PackageGraphGroupLayoutNode> groupNodes,
            string summarySuffix)
        {
            if (groups == null || groups.Count == 0)
            {
                return;
            }

            float stepY = GroupChipHeight + 14f;
            float originY = centerY - ((groups.Count - 1) * stepY) * 0.5f;

            for (int index = 0; index < groups.Count; index++)
            {
                PackageGraphGroup group = groups[index];
                IReadOnlyList<PackageGraphNode> packages = GetDescendantPackages(graph, group.Id);
                Rect rect = ClampToCanvas(CenteredRect(
                    new Vector2(x, originY + index * stepY),
                    GroupChipWidth,
                    GroupChipHeight));
                groupNodes.Add(CreateGroupLayoutNode(
                    graph,
                    group,
                    rect,
                    focused: false,
                    collapsed: true,
                    packageScope: packages,
                    summaryLabel: group.DisplayName + " - " + packages.Count + " " + summarySuffix));
            }
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
            ISet<string> placed)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            float stepY = NodeHeight + Mathf.Max(FocusGridGapY, NodeGap * 2f + 4f);
            float originY = center.y - ((nodes.Count - 1) * stepY) * 0.5f;

            for (int index = 0; index < nodes.Count; index++)
            {
                PackageGraphNode node = nodes[index];
                Vector2 nodeCenter = new Vector2(center.x, originY + index * stepY);
                nodeRects[node.PackageId] = ClampToCanvas(CenteredRect(nodeCenter));
                placed.Add(node.PackageId);
            }
        }

        private static void PlaceRow(
            IReadOnlyList<PackageGraphNode> nodes,
            Vector2 center,
            IDictionary<string, Rect> nodeRects,
            ISet<string> placed)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            float stepX = NodeWidth + FocusGridGapX;
            float originX = center.x - ((nodes.Count - 1) * stepX) * 0.5f;

            for (int index = 0; index < nodes.Count; index++)
            {
                PackageGraphNode node = nodes[index];
                Vector2 nodeCenter = new Vector2(originX + index * stepX, center.y);
                nodeRects[node.PackageId] = ClampToCanvas(CenteredRect(nodeCenter));
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
            float packageWidth,
            float packageHeight,
            float groupWidth,
            float groupHeight)
        {
            if (childCount <= 0)
            {
                return 0f;
            }

            if (childCount == 1)
            {
                return 190f;
            }

            if (childCount == 2)
            {
                return 250f;
            }

            float maxDiameter = Mathf.Max(Mathf.Max(packageWidth, packageHeight), Mathf.Max(groupWidth, groupHeight));
            float circumferenceRadius = (childCount * (maxDiameter + NodeGap)) / (Mathf.PI * 2f);
            return Mathf.Clamp(circumferenceRadius + maxDiameter * 0.18f, 270f, 360f);
        }

        private static float CalculateGlobalGroupOrbitRadius(
            PackageGraphModel graph,
            IReadOnlyList<PackageGraphGroup> topGroups)
        {
            if (topGroups == null || topGroups.Count <= 1)
            {
                return MinimumGlobalGroupOrbitRadius;
            }

            float largestClusterRadius = 0f;

            foreach (PackageGraphGroup group in topGroups)
            {
                int childCount = GetDirectPackages(graph, group.Id).Count + GetChildGroups(graph, group.Id).Count;
                float localRadius = CalculateLocalOrbitRadius(childCount, NodeWidth, NodeHeight, GroupChipWidth, GroupChipHeight);
                float childHalfWidth = childCount > 0
                    ? Mathf.Max(NodeWidth, GroupChipWidth) * 0.5f
                    : 0f;
                float childHalfHeight = childCount > 0
                    ? Mathf.Max(NodeHeight, GroupChipHeight) * 0.5f
                    : 0f;
                float childClusterHalfDiagonal = childCount > 0
                    ? CalculateHalfDiagonal(
                        localRadius * 2f + childHalfWidth * 2f,
                        localRadius * 2f + childHalfHeight * 2f)
                    : 0f;
                float groupHalfExtent = CalculateHalfDiagonal(GroupWidth, GroupHeight);
                largestClusterRadius = Mathf.Max(largestClusterRadius, Mathf.Max(groupHalfExtent, childClusterHalfDiagonal));
            }

            float angleStepRadians = Mathf.PI * 2f / topGroups.Count;
            float requiredChord = largestClusterRadius * 2f + MinimumClusterGap;
            float radiusForGap = requiredChord / Mathf.Max(0.01f, 2f * Mathf.Sin(angleStepRadians * 0.5f));
            float canvasLimitedRadius = Mathf.Min(
                GraphCenter.x - largestClusterRadius - 48f,
                GraphCenter.y - largestClusterRadius - 48f,
                CanvasWidth - GraphCenter.x - largestClusterRadius - 48f,
                CanvasHeight - GraphCenter.y - largestClusterRadius - 48f);
            return Mathf.Min(Mathf.Max(MinimumGlobalGroupOrbitRadius, radiusForGap), canvasLimitedRadius);
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

        private static Rect CenteredRect(Vector2 center, float width, float height)
        {
            return new Rect(
                center.x - width * 0.5f,
                center.y - height * 0.5f,
                width,
                height);
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
