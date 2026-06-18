using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageGraphLayoutMode
    {
        Overview,
        Focus
    }

    internal enum PackageGraphLayoutRing
    {
        Foundation,
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
            IEnumerable<PackageGraphSectorLabel> sectorLabels = null)
        {
            Mode = mode;
            FocusPackageId = focusPackageId ?? string.Empty;
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
        }

        public PackageGraphLayoutMode Mode { get; }

        public string FocusPackageId { get; }

        public float CanvasWidth { get; }

        public float CanvasHeight { get; }

        public Rect HubRect { get; }

        public Vector2 ActiveCenter { get; }

        public IReadOnlyDictionary<string, Rect> NodeRects { get; }

        public IReadOnlyDictionary<string, PackageGraphLayoutRing> NodeRings { get; }

        public IReadOnlyList<PackageGraphRingGuide> RingGuides { get; }

        public IReadOnlyList<PackageGraphSectorLabel> SectorLabels { get; }
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
        public PackageGraphSectorLabel(string label, PackageGraphLayoutRing ring, Vector2 position)
        {
            Label = label ?? string.Empty;
            Ring = ring;
            Position = position;
        }

        public string Label { get; }

        public PackageGraphLayoutRing Ring { get; }

        public Vector2 Position { get; }
    }

    internal sealed class PackageGraphLayout
    {
        public const float CanvasWidth = 2100f;
        public const float CanvasHeight = 1660f;
        public const float NodeWidth = 238f;
        public const float NodeHeight = 136f;

        private const float OverviewNodeWidth = 212f;
        private const float OverviewNodeHeight = 94f;
        private const float StackNodeWidth = 198f;
        private const float StackNodeHeight = 84f;
        private const float HubWidth = 250f;
        private const float HubHeight = 128f;
        private const float NodeGap = 18f;
        private const float OverviewOrbitRadius = 650f;
        private const float FocusGridGapX = 42f;
        private const float FocusGridGapY = 26f;

        public static readonly Vector2 GraphCenter = new Vector2(1050f, 830f);

        public PackageGraphLayoutResult Calculate(PackageGraphModel graph)
        {
            return Calculate(graph, PackageGraphLayoutMode.Overview, string.Empty);
        }

        public PackageGraphLayoutResult Calculate(
            PackageGraphModel graph,
            PackageGraphLayoutMode mode,
            string focusPackageId)
        {
            PackageGraphNode[] nodes = graph == null
                ? Array.Empty<PackageGraphNode>()
                : graph.Nodes.Where(node => node != null).ToArray();
            Dictionary<string, Rect> nodeRects = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphLayoutRing> nodeRings =
                new Dictionary<string, PackageGraphLayoutRing>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphNode node in nodes)
            {
                nodeRings[node.PackageId] = GetRing(node);
            }

            PackageGraphNode focusNode = GetFocusNode(graph, mode, focusPackageId);

            if (focusNode == null)
            {
                PlaceOverview(nodes, nodeRects, nodeRings);

                Rect overviewHubRect = CreateOverviewHubRect();
                return new PackageGraphLayoutResult(
                    PackageGraphLayoutMode.Overview,
                    string.Empty,
                    CanvasWidth,
                    CanvasHeight,
                    overviewHubRect,
                    overviewHubRect.center,
                    nodeRects,
                    nodeRings,
                    CreateOverviewRingGuides(),
                    CreateOverviewSectorLabels(nodeRects, nodeRings));
            }

            PlaceFocus(graph, nodes, focusNode, nodeRects, nodeRings);
            ResolveOverlaps(nodeRects, nodeRings, focusNode.PackageId);

            Rect focusHubRect = CreateFocusHubRect();
            Rect focusRect = nodeRects.TryGetValue(focusNode.PackageId, out Rect selectedRect)
                ? selectedRect
                : CenteredRect(GraphCenter);

            return new PackageGraphLayoutResult(
                PackageGraphLayoutMode.Focus,
                focusNode.PackageId,
                CanvasWidth,
                CanvasHeight,
                focusHubRect,
                focusRect.center,
                nodeRects,
                nodeRings,
                CreateFocusGuides());
        }

        private static PackageGraphNode GetFocusNode(
            PackageGraphModel graph,
            PackageGraphLayoutMode mode,
            string focusPackageId)
        {
            if (mode != PackageGraphLayoutMode.Focus ||
                graph == null ||
                string.IsNullOrWhiteSpace(focusPackageId))
            {
                return null;
            }

            return graph.Nodes.FirstOrDefault(node =>
                node != null &&
                string.Equals(node.PackageId, focusPackageId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static void PlaceOverview(
            PackageGraphNode[] nodes,
            IDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings)
        {
            PackageGraphNode[] orderedNodes = nodes
                .Where(node => node != null)
                .OrderBy(node => nodeRings.TryGetValue(node.PackageId, out PackageGraphLayoutRing ring)
                    ? GetRingPriority(ring)
                    : 0)
                .ThenBy(node => GetRingSortIndex(node, nodeRings.TryGetValue(node.PackageId, out PackageGraphLayoutRing ring)
                    ? ring
                    : PackageGraphLayoutRing.Foundation))
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (orderedNodes.Length == 0)
            {
                return;
            }

            float[] angles = CreateEvenAngles(-132f, 228f, orderedNodes.Length, includeEnd: false);

            for (int index = 0; index < orderedNodes.Length; index++)
            {
                PackageGraphNode node = orderedNodes[index];
                float radians = angles[index] * Mathf.Deg2Rad;
                Vector2 nodeCenter = new Vector2(
                    GraphCenter.x + Mathf.Cos(radians) * OverviewOrbitRadius,
                    GraphCenter.y + Mathf.Sin(radians) * OverviewOrbitRadius);
                nodeRects[node.PackageId] = ClampToCanvas(CenteredRect(
                    nodeCenter,
                    OverviewNodeWidth,
                    OverviewNodeHeight));
            }
        }

        private static void PlaceFocus(
            PackageGraphModel graph,
            PackageGraphNode[] nodes,
            PackageGraphNode focusNode,
            IDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings)
        {
            Dictionary<string, PackageGraphNode> nodeById = nodes
                .GroupBy(node => node.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            nodeRects[focusNode.PackageId] = CenteredRect(GraphCenter);
            placed.Add(focusNode.PackageId);

            string focusPackageId = focusNode.PackageId;
            List<PackageGraphNode> providers = GetProviderNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> integrationNodes = GetIntegrationNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> supportProviders = GetIntegrationSupportProviderNodes(
                graph,
                nodeById,
                integrationNodes,
                placed,
                focusPackageId);
            List<PackageGraphNode> dependents = GetDependentNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> optionalCompanions = GetOptionalCompanionNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> suiteNodes = GetSuiteNodes(graph, nodeById, focusPackageId, placed);

            PlaceCluster(
                MergeGroups(providers, supportProviders),
                new Vector2(620f, 800f),
                1,
                nodeRects,
                placed);
            PlaceCluster(
                dependents,
                new Vector2(1435f, 800f),
                2,
                nodeRects,
                placed);
            PlaceCluster(
                integrationNodes,
                new Vector2(1050f, 1145f),
                2,
                nodeRects,
                placed);
            PlaceCluster(
                optionalCompanions,
                new Vector2(650f, 475f),
                1,
                nodeRects,
                placed);
            PlaceCluster(
                suiteNodes,
                new Vector2(1440f, 475f),
                1,
                nodeRects,
                placed);

            PlaceUnrelatedStack(nodes, nodeRects, nodeRings, placed);
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
                .OrderBy(node => GetRelationshipSortIndex(node))
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
                .OrderBy(node => GetRelationshipSortIndex(node))
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
                .OrderBy(node => GetRelationshipSortIndex(node))
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<PackageGraphNode> GetIntegrationSupportProviderNodes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, PackageGraphNode> nodeById,
            IEnumerable<PackageGraphNode> integrationNodes,
            ISet<string> placed,
            string focusPackageId)
        {
            HashSet<string> integrationIds = new HashSet<string>(
                (integrationNodes ?? Enumerable.Empty<PackageGraphNode>()).Select(node => node.PackageId),
                StringComparer.OrdinalIgnoreCase);

            if (integrationIds.Count == 0)
            {
                return new List<PackageGraphNode>();
            }

            return graph.Edges
                .Where(edge => edge.Kind == PackageGraphEdgeKind.HardDependency &&
                               integrationIds.Contains(edge.ToPackageId))
                .Select(edge => GetNode(nodeById, edge.FromPackageId))
                .Where(node => node != null &&
                               !placed.Contains(node.PackageId) &&
                               !integrationIds.Contains(node.PackageId) &&
                               !string.Equals(node.PackageId, focusPackageId, StringComparison.OrdinalIgnoreCase))
                .Distinct(PackageGraphNodePackageIdComparer.Instance)
                .OrderBy(node => GetRelationshipSortIndex(node))
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
                .OrderBy(node => GetRelationshipSortIndex(node))
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<PackageGraphNode> GetSuiteNodes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, PackageGraphNode> nodeById,
            string focusPackageId,
            ISet<string> placed)
        {
            HashSet<string> suitePackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphSuiteRegion region in graph.SuiteRegions)
            {
                bool focusIsSuite = string.Equals(region.SuitePackageId, focusPackageId, StringComparison.OrdinalIgnoreCase);
                bool focusIsMember = region.MemberPackageIds.Any(memberPackageId =>
                    string.Equals(memberPackageId, focusPackageId, StringComparison.OrdinalIgnoreCase));

                if (focusIsSuite || focusIsMember)
                {
                    suitePackageIds.Add(region.SuitePackageId);
                }
            }

            return suitePackageIds
                .Select(packageId => GetNode(nodeById, packageId))
                .Where(node => node != null && !placed.Contains(node.PackageId))
                .OrderBy(node => GetRelationshipSortIndex(node))
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
                .OrderBy(node => GetRelationshipSortIndex(node))
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

        private static void PlaceCluster(
            IReadOnlyList<PackageGraphNode> nodes,
            Vector2 center,
            int columns,
            IDictionary<string, Rect> nodeRects,
            ISet<string> placed)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            int columnCount = Mathf.Clamp(columns, 1, Mathf.Max(1, nodes.Count));
            int rowCount = Mathf.CeilToInt(nodes.Count / (float)columnCount);
            float stepX = NodeWidth + FocusGridGapX;
            float stepY = NodeHeight + FocusGridGapY;
            float originX = center.x - ((columnCount - 1) * stepX) * 0.5f;
            float originY = center.y - ((rowCount - 1) * stepY) * 0.5f;

            for (int index = 0; index < nodes.Count; index++)
            {
                PackageGraphNode node = nodes[index];
                int column = index % columnCount;
                int row = index / columnCount;
                Vector2 nodeCenter = new Vector2(originX + column * stepX, originY + row * stepY);
                nodeRects[node.PackageId] = ClampToCanvas(CenteredRect(nodeCenter));
                placed.Add(node.PackageId);
            }
        }

        private static void PlaceUnrelatedStack(
            IEnumerable<PackageGraphNode> nodes,
            IDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings,
            ISet<string> placed)
        {
            PackageGraphNode[] remainingNodes = nodes
                .Where(node => node != null && !placed.Contains(node.PackageId))
                .OrderBy(node => nodeRings.TryGetValue(node.PackageId, out PackageGraphLayoutRing ring)
                    ? GetRingPriority(ring)
                    : 0)
                .ThenBy(node => GetRingSortIndex(node, nodeRings.TryGetValue(node.PackageId, out PackageGraphLayoutRing ring)
                    ? ring
                    : PackageGraphLayoutRing.Foundation))
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (remainingNodes.Length == 0)
            {
                return;
            }

            const int stackColumns = 2;
            const float stackCenterX = 1850f;
            const float stackTopY = 330f;
            const float stackGapX = 16f;
            const float stackGapY = 14f;
            float stepX = StackNodeWidth + stackGapX;
            float stepY = StackNodeHeight + stackGapY;

            for (int index = 0; index < remainingNodes.Length; index++)
            {
                PackageGraphNode node = remainingNodes[index];
                int column = index % stackColumns;
                int row = index / stackColumns;
                float originX = stackCenterX - ((stackColumns - 1) * stepX) * 0.5f;
                Vector2 nodeCenter = new Vector2(
                    originX + column * stepX,
                    stackTopY + row * stepY);
                nodeRects[node.PackageId] = ClampToCanvas(CenteredRect(
                    nodeCenter,
                    StackNodeWidth,
                    StackNodeHeight));
                placed.Add(node.PackageId);
            }
        }

        private static PackageGraphLayoutRing GetRing(PackageGraphNode node)
        {
            if (node == null)
            {
                return PackageGraphLayoutRing.Foundation;
            }

            if (node.NodeType == PackageGraphNodeType.Integration)
            {
                return PackageGraphLayoutRing.Integration;
            }

            if (node.NodeType == PackageGraphNodeType.Suite)
            {
                return PackageGraphLayoutRing.Suite;
            }

            if (node.NodeType == PackageGraphNodeType.Tool ||
                node.NodeType == PackageGraphNodeType.Companion ||
                string.Equals(node.Category, "UI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Category, "World", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Category, "Tools", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.PackageId, "com.deucarian.diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                return PackageGraphLayoutRing.Runtime;
            }

            return PackageGraphLayoutRing.Foundation;
        }

        private static float[] CreateEvenAngles(float startAngle, float endAngle, int count, bool includeEnd)
        {
            if (count == 1)
            {
                return new[] { (startAngle + endAngle) * 0.5f };
            }

            float[] angles = new float[count];
            float span = endAngle - startAngle;
            float denominator = includeEnd ? count - 1f : count;

            for (int index = 0; index < count; index++)
            {
                angles[index] = startAngle + span * (index / denominator);
            }

            return angles;
        }

        private static int GetRingSortIndex(PackageGraphNode node, PackageGraphLayoutRing ring)
        {
            string packageId = node.PackageId ?? string.Empty;

            if (ring == PackageGraphLayoutRing.Foundation)
            {
                return GetKnownPackageIndex(
                    packageId,
                    "com.deucarian.editor",
                    "com.deucarian.logging",
                    "com.deucarian.api",
                    "com.deucarian.core-state",
                    "com.deucarian.object-loading",
                    "com.deucarian.session");
            }

            if (ring == PackageGraphLayoutRing.Runtime)
            {
                return GetKnownPackageIndex(
                    packageId,
                    "com.deucarian.ui-binding",
                    "com.deucarian.object-selection",
                    "com.deucarian.theming",
                    "com.deucarian.diagnostics",
                    "com.deucarian.package-installer");
            }

            if (ring == PackageGraphLayoutRing.Integration)
            {
                return GetKnownPackageIndex(
                    packageId,
                    "com.deucarian.object-loading.api-integration",
                    "com.deucarian.session.api-integration",
                    "com.deucarian.ui-binding.core-state-integration",
                    "com.deucarian.object-selection.core-state-integration");
            }

            if (ring == PackageGraphLayoutRing.Suite)
            {
                return GetKnownPackageIndex(
                    packageId,
                    "com.deucarian.selection-suite");
            }

            return 1000;
        }

        private static int GetRelationshipSortIndex(PackageGraphNode node)
        {
            PackageGraphLayoutRing ring = GetRing(node);
            return GetRingPriority(ring) * 10000 + GetRingSortIndex(node, ring);
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
            return new Rect(
                GraphCenter.x - HubWidth * 0.5f,
                GraphCenter.y - HubHeight * 0.5f,
                HubWidth,
                HubHeight);
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

        private static void ResolveOverlaps(
            IDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings,
            string lockedPackageId)
        {
            string[] keys = nodeRects.Keys
                .OrderBy(key => string.Equals(key, lockedPackageId, StringComparison.OrdinalIgnoreCase) ? -1 : 0)
                .ThenBy(key => nodeRings.TryGetValue(key, out PackageGraphLayoutRing ring) ? GetRingPriority(ring) : 0)
                .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (int pass = 0; pass < 40; pass++)
            {
                bool movedAny = false;

                for (int firstIndex = 0; firstIndex < keys.Length; firstIndex++)
                {
                    for (int secondIndex = firstIndex + 1; secondIndex < keys.Length; secondIndex++)
                    {
                        string firstKey = keys[firstIndex];
                        string secondKey = keys[secondIndex];
                        Rect firstRect = Expand(nodeRects[firstKey], NodeGap);
                        Rect secondRect = Expand(nodeRects[secondKey], NodeGap);

                        if (!firstRect.Overlaps(secondRect))
                        {
                            continue;
                        }

                        string moveKey = GetMoveKey(firstKey, secondKey, nodeRings, lockedPackageId);
                        float overlapX = Mathf.Min(firstRect.xMax, secondRect.xMax) -
                                         Mathf.Max(firstRect.xMin, secondRect.xMin);
                        float overlapY = Mathf.Min(firstRect.yMax, secondRect.yMax) -
                                         Mathf.Max(firstRect.yMin, secondRect.yMin);
                        string staticKey = string.Equals(moveKey, firstKey, StringComparison.OrdinalIgnoreCase)
                            ? secondKey
                            : firstKey;
                        Rect moveRect = nodeRects[moveKey];
                        Rect staticRect = nodeRects[staticKey];
                        Vector2 direction = moveRect.center - staticRect.center;

                        if (overlapX < overlapY)
                        {
                            float sign = Mathf.Abs(direction.x) > 0.01f
                                ? Mathf.Sign(direction.x)
                                : Mathf.Sign(moveRect.center.x - GraphCenter.x);
                            moveRect.x += (Mathf.Abs(sign) < 0.01f ? 1f : sign) * (overlapX + NodeGap);
                        }
                        else
                        {
                            float sign = Mathf.Abs(direction.y) > 0.01f
                                ? Mathf.Sign(direction.y)
                                : Mathf.Sign(moveRect.center.y - GraphCenter.y);
                            moveRect.y += (Mathf.Abs(sign) < 0.01f ? 1f : sign) * (overlapY + NodeGap);
                        }

                        nodeRects[moveKey] = ClampToCanvas(moveRect);
                        movedAny = true;
                    }
                }

                if (!movedAny)
                {
                    break;
                }
            }

            PackRemainingOverlaps(nodeRects, nodeRings, lockedPackageId);
        }

        private static void PackRemainingOverlaps(
            IDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings,
            string lockedPackageId)
        {
            string[] keys = nodeRects.Keys
                .OrderBy(key => string.Equals(key, lockedPackageId, StringComparison.OrdinalIgnoreCase) ? -1 : 0)
                .ThenBy(key => nodeRings.TryGetValue(key, out PackageGraphLayoutRing ring) ? GetRingPriority(ring) : 0)
                .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            List<Rect> placedRects = new List<Rect>();

            foreach (string key in keys)
            {
                Rect rect = nodeRects[key];

                if (IsFree(rect, placedRects))
                {
                    placedRects.Add(rect);
                    continue;
                }

                Rect packed = FindNearestFreeRect(rect, placedRects);
                nodeRects[key] = packed;
                placedRects.Add(packed);
            }
        }

        private static Rect FindNearestFreeRect(Rect preferredRect, IReadOnlyList<Rect> placedRects)
        {
            float stepX = preferredRect.width + NodeGap + 10f;
            float stepY = preferredRect.height + NodeGap + 10f;
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);

            for (int radius = 1; radius <= 6; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != radius)
                        {
                            continue;
                        }

                        Rect candidate = ClampToCanvas(new Rect(
                            preferredRect.x + x * stepX,
                            preferredRect.y + y * stepY,
                            preferredRect.width,
                            preferredRect.height));
                        string key = Mathf.RoundToInt(candidate.x) + ":" + Mathf.RoundToInt(candidate.y);

                        if (!visited.Add(key))
                        {
                            continue;
                        }

                        if (IsFree(candidate, placedRects))
                        {
                            return candidate;
                        }
                    }
                }
            }

            Rect best = preferredRect;
            float bestDistance = float.MaxValue;

            for (float y = 24f; y <= CanvasHeight - preferredRect.height - 24f; y += stepY)
            {
                for (float x = 24f; x <= CanvasWidth - preferredRect.width - 24f; x += stepX)
                {
                    Rect candidate = new Rect(x, y, preferredRect.width, preferredRect.height);

                    if (!IsFree(candidate, placedRects))
                    {
                        continue;
                    }

                    float distance = (candidate.center - preferredRect.center).sqrMagnitude;

                    if (distance < bestDistance)
                    {
                        best = candidate;
                        bestDistance = distance;
                    }
                }
            }

            return best;
        }

        private static bool IsFree(Rect rect, IEnumerable<Rect> placedRects)
        {
            Rect expanded = Expand(rect, NodeGap * 0.5f);

            foreach (Rect placedRect in placedRects)
            {
                if (expanded.Overlaps(Expand(placedRect, NodeGap * 0.5f)))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetMoveKey(
            string firstKey,
            string secondKey,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings,
            string lockedPackageId)
        {
            if (string.Equals(firstKey, lockedPackageId, StringComparison.OrdinalIgnoreCase))
            {
                return secondKey;
            }

            if (string.Equals(secondKey, lockedPackageId, StringComparison.OrdinalIgnoreCase))
            {
                return firstKey;
            }

            int firstPriority = nodeRings.TryGetValue(firstKey, out PackageGraphLayoutRing firstRing)
                ? GetRingPriority(firstRing)
                : 0;
            int secondPriority = nodeRings.TryGetValue(secondKey, out PackageGraphLayoutRing secondRing)
                ? GetRingPriority(secondRing)
                : 0;

            return secondPriority >= firstPriority ? secondKey : firstKey;
        }

        private static int GetRingPriority(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Foundation:
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

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(
                rect.x - amount,
                rect.y - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }

        private static IEnumerable<PackageGraphRingGuide> CreateOverviewRingGuides()
        {
            yield return new PackageGraphRingGuide(
                "Deucarian Ecosystem Orbit",
                PackageGraphLayoutRing.Foundation,
                GraphCenter,
                OverviewOrbitRadius,
                OverviewOrbitRadius);
        }

        private static IEnumerable<PackageGraphSectorLabel> CreateOverviewSectorLabels(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings)
        {
            foreach (PackageGraphLayoutRing ring in new[]
                     {
                         PackageGraphLayoutRing.Foundation,
                         PackageGraphLayoutRing.Runtime,
                         PackageGraphLayoutRing.Integration,
                         PackageGraphLayoutRing.Suite
                     })
            {
                Vector2 direction = Vector2.zero;
                int count = 0;

                foreach (KeyValuePair<string, Rect> nodeRect in nodeRects)
                {
                    if (!nodeRings.TryGetValue(nodeRect.Key, out PackageGraphLayoutRing nodeRing) ||
                        nodeRing != ring)
                    {
                        continue;
                    }

                    Vector2 nodeDirection = nodeRect.Value.center - GraphCenter;

                    if (nodeDirection.sqrMagnitude < 0.01f)
                    {
                        continue;
                    }

                    direction += nodeDirection.normalized;
                    count++;
                }

                if (count == 0)
                {
                    continue;
                }

                if (direction.sqrMagnitude < 0.01f)
                {
                    direction = GetFallbackSectorDirection(ring);
                }

                yield return new PackageGraphSectorLabel(
                    GetSectorLabel(ring),
                    ring,
                    GraphCenter + direction.normalized * (OverviewOrbitRadius + 82f));
            }
        }

        private static Vector2 GetFallbackSectorDirection(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return new Vector2(1f, -0.2f).normalized;
                case PackageGraphLayoutRing.Integration:
                    return new Vector2(0.45f, 1f).normalized;
                case PackageGraphLayoutRing.Suite:
                    return new Vector2(-0.9f, 0.4f).normalized;
                default:
                    return new Vector2(-0.75f, -0.55f).normalized;
            }
        }

        private static string GetSectorLabel(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return "UI / Runtime / Tooling";
                case PackageGraphLayoutRing.Integration:
                    return "Integrations";
                case PackageGraphLayoutRing.Suite:
                    return "Suites";
                default:
                    return "Core / Foundation";
            }
        }

        private static IEnumerable<PackageGraphRingGuide> CreateFocusGuides()
        {
            yield return new PackageGraphRingGuide(
                "Required packages",
                PackageGraphLayoutRing.Foundation,
                new Vector2(620f, 800f),
                182f,
                190f);
            yield return new PackageGraphRingGuide(
                "Dependent packages",
                PackageGraphLayoutRing.Runtime,
                new Vector2(1435f, 800f),
                328f,
                292f);
            yield return new PackageGraphRingGuide(
                "Integrations",
                PackageGraphLayoutRing.Integration,
                new Vector2(1050f, 1145f),
                316f,
                178f);
            yield return new PackageGraphRingGuide(
                "Companions / Suites",
                PackageGraphLayoutRing.Suite,
                new Vector2(1045f, 475f),
                548f,
                170f);
            yield return new PackageGraphRingGuide(
                "Unrelated package stack",
                PackageGraphLayoutRing.Runtime,
                new Vector2(1850f, 660f),
                255f,
                420f);
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
