using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageGraphLayoutMode
    {
        Overview,
        Filtered,
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
        IconOnly,
        Micro,
        Compact,
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
        private const float OverviewIconToMicroZoom = 0.44f;
        private const float OverviewMicroToIconZoom = 0.32f;
        private const float OverviewMicroToCompactZoom = 1.18f;
        private const float OverviewCompactToMicroZoom = 1.08f;

        private const float GroupIconToMicroZoom = 0.42f;
        private const float GroupMicroToIconZoom = 0.30f;
        private const float GroupMicroToCompactZoom = 0.78f;
        private const float GroupCompactToMicroZoom = 0.62f;

        private const float FocusIconToMicroZoom = 0.36f;
        private const float FocusMicroToIconZoom = 0.24f;
        private const float FocusMicroToCompactZoom = 0.64f;
        private const float FocusCompactToMicroZoom = 0.50f;
        private const float FocusCompactToFullZoom = 0.98f;
        private const float FocusFullToCompactZoom = 0.84f;
        private const string RedundantGraphTitlePrefix = "Deucarian ";

        public static PackageGraphNodeMetrics GetMetrics(PackageGraphNodePresentationLevel level)
        {
            switch (level)
            {
                case PackageGraphNodePresentationLevel.IconOnly:
                    return new PackageGraphNodeMetrics(38f, 38f);
                case PackageGraphNodePresentationLevel.Micro:
                    return new PackageGraphNodeMetrics(116f, 44f);
                case PackageGraphNodePresentationLevel.Compact:
                    return new PackageGraphNodeMetrics(164f, 76f);
                default:
                    return new PackageGraphNodeMetrics(220f, 132f);
            }
        }

        public static PackageGraphNodePresentationLevel GetDefaultForMode(PackageGraphLayoutMode mode)
        {
            switch (mode)
            {
                case PackageGraphLayoutMode.Overview:
                case PackageGraphLayoutMode.Filtered:
                    return PackageGraphNodePresentationLevel.Micro;
                case PackageGraphLayoutMode.GroupFocus:
                    return PackageGraphNodePresentationLevel.Compact;
                default:
                    return PackageGraphNodePresentationLevel.Full;
            }
        }

        public static PackageGraphNodePresentationLevel ResolveForZoom(
            PackageGraphLayoutMode mode,
            float zoom,
            PackageGraphNodePresentationLevel current)
        {
            if (mode == PackageGraphLayoutMode.Overview || mode == PackageGraphLayoutMode.Filtered)
            {
                return ResolveOverview(zoom, current);
            }

            if (mode == PackageGraphLayoutMode.GroupFocus)
            {
                return ResolveGroupFocus(zoom, current);
            }

            return ResolvePackageFocus(zoom, current);
        }

        public static PackageGraphNodePresentationLevel GetFocusPresentation(bool selected)
        {
            return selected
                ? PackageGraphNodePresentationLevel.Full
                : PackageGraphNodePresentationLevel.Compact;
        }

        public static PackageGraphNodePresentationLevel GetFocusSelectedPresentation(
            PackageGraphNodePresentationLevel resolvedLevel)
        {
            return resolvedLevel == PackageGraphNodePresentationLevel.Full
                ? PackageGraphNodePresentationLevel.Full
                : PackageGraphNodePresentationLevel.Compact;
        }

        public static PackageGraphNodePresentationLevel GetFocusRelatedPresentation(
            PackageGraphNodePresentationLevel resolvedLevel)
        {
            switch (resolvedLevel)
            {
                case PackageGraphNodePresentationLevel.IconOnly:
                case PackageGraphNodePresentationLevel.Micro:
                    return resolvedLevel;
                default:
                    return PackageGraphNodePresentationLevel.Compact;
            }
        }

        public static string GetGraphTitle(string displayName, PackageGraphNodePresentationLevel level)
        {
            string title = string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim();

            if (level == PackageGraphNodePresentationLevel.Full ||
                !title.StartsWith(RedundantGraphTitlePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return title;
            }

            return title.Substring(RedundantGraphTitlePrefix.Length).Trim();
        }

        private static PackageGraphNodePresentationLevel ResolveOverview(
            float zoom,
            PackageGraphNodePresentationLevel current)
        {
            switch (current)
            {
                case PackageGraphNodePresentationLevel.IconOnly:
                    return zoom >= OverviewIconToMicroZoom
                        ? PackageGraphNodePresentationLevel.Micro
                        : PackageGraphNodePresentationLevel.IconOnly;
                case PackageGraphNodePresentationLevel.Compact:
                case PackageGraphNodePresentationLevel.Full:
                    return zoom <= OverviewCompactToMicroZoom
                        ? PackageGraphNodePresentationLevel.Micro
                        : PackageGraphNodePresentationLevel.Compact;
                default:
                    if (zoom <= OverviewMicroToIconZoom)
                    {
                        return PackageGraphNodePresentationLevel.IconOnly;
                    }

                    return zoom >= OverviewMicroToCompactZoom
                        ? PackageGraphNodePresentationLevel.Compact
                        : PackageGraphNodePresentationLevel.Micro;
            }
        }

        private static PackageGraphNodePresentationLevel ResolveGroupFocus(
            float zoom,
            PackageGraphNodePresentationLevel current)
        {
            switch (current)
            {
                case PackageGraphNodePresentationLevel.IconOnly:
                    return zoom >= GroupIconToMicroZoom
                        ? PackageGraphNodePresentationLevel.Micro
                        : PackageGraphNodePresentationLevel.IconOnly;
                case PackageGraphNodePresentationLevel.Micro:
                    if (zoom <= GroupMicroToIconZoom)
                    {
                        return PackageGraphNodePresentationLevel.IconOnly;
                    }

                    return zoom >= GroupMicroToCompactZoom
                        ? PackageGraphNodePresentationLevel.Compact
                        : PackageGraphNodePresentationLevel.Micro;
                case PackageGraphNodePresentationLevel.Full:
                case PackageGraphNodePresentationLevel.Compact:
                    return zoom <= GroupCompactToMicroZoom
                        ? PackageGraphNodePresentationLevel.Micro
                        : PackageGraphNodePresentationLevel.Compact;
                default:
                    return PackageGraphNodePresentationLevel.Compact;
            }
        }

        private static PackageGraphNodePresentationLevel ResolvePackageFocus(
            float zoom,
            PackageGraphNodePresentationLevel current)
        {
            switch (current)
            {
                case PackageGraphNodePresentationLevel.IconOnly:
                    if (zoom < FocusIconToMicroZoom)
                    {
                        return PackageGraphNodePresentationLevel.IconOnly;
                    }

                    return zoom >= FocusCompactToFullZoom
                        ? PackageGraphNodePresentationLevel.Full
                        : PackageGraphNodePresentationLevel.Micro;
                case PackageGraphNodePresentationLevel.Micro:
                    if (zoom <= FocusMicroToIconZoom)
                    {
                        return PackageGraphNodePresentationLevel.IconOnly;
                    }

                    if (zoom >= FocusCompactToFullZoom)
                    {
                        return PackageGraphNodePresentationLevel.Full;
                    }

                    return zoom >= FocusMicroToCompactZoom
                        ? PackageGraphNodePresentationLevel.Compact
                        : PackageGraphNodePresentationLevel.Micro;
                case PackageGraphNodePresentationLevel.Full:
                    return zoom <= FocusFullToCompactZoom
                        ? PackageGraphNodePresentationLevel.Compact
                        : PackageGraphNodePresentationLevel.Full;
                default:
                    if (zoom <= FocusCompactToMicroZoom)
                    {
                        return PackageGraphNodePresentationLevel.Micro;
                    }

                    return zoom >= FocusCompactToFullZoom
                        ? PackageGraphNodePresentationLevel.Full
                        : PackageGraphNodePresentationLevel.Compact;
            }
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
            int notInstalledCount,
            int attentionCount,
            int unknownCount,
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
            NotInstalledCount = Math.Max(0, notInstalledCount);
            AttentionCount = Math.Max(0, attentionCount);
            UnknownCount = Math.Max(0, unknownCount);
            MissingCount = AttentionCount;
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

        public int NotInstalledCount { get; }

        public int AttentionCount { get; }

        public int UnknownCount { get; }

        public PackageGraphCategoryStatusSummary StatusSummary =>
            new PackageGraphCategoryStatusSummary(
                InstalledCount,
                NotInstalledCount,
                AttentionCount,
                UnknownCount);

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
        public PackageGraphRingGuide(string label, PackageGraphLayoutRing ring, Vector2 center, float radius)
        {
            Label = label ?? string.Empty;
            Ring = ring;
            Center = center;
            Radius = Mathf.Max(0f, radius);
        }

        public string Label { get; }

        public PackageGraphLayoutRing Ring { get; }

        public Vector2 Center { get; }

        public float Radius { get; }

        public Rect CircleRect =>
            new Rect(
                Center.x - Radius,
                Center.y - Radius,
                Radius * 2f,
                Radius * 2f);
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

    internal enum PackageGraphEgoLayoutZone
    {
        OwningCategory,
        Providers,
        Dependents,
        Integrations,
        CompanionsAndSuites
    }

    internal readonly struct PackageGraphEgoLayoutMetrics
    {
        public PackageGraphEgoLayoutMetrics(
            Vector2 selectedNodeCenter,
            float horizontalLaneGap,
            float verticalLaneGap,
            float packageCardGap,
            float packageSubclusterGap,
            float categoryRailGap,
            float owningCategoryGap,
            float structuralBusGap,
            PackageGraphNodeMetrics relatedPackageMetrics)
        {
            SelectedNodeCenter = selectedNodeCenter;
            HorizontalLaneGap = Mathf.Max(1f, horizontalLaneGap);
            VerticalLaneGap = Mathf.Max(1f, verticalLaneGap);
            PackageCardGap = Mathf.Max(1f, packageCardGap);
            PackageSubclusterGap = Mathf.Max(PackageCardGap, packageSubclusterGap);
            CategoryRailGap = Mathf.Max(1f, categoryRailGap);
            OwningCategoryGap = Mathf.Max(1f, owningCategoryGap);
            StructuralBusGap = Mathf.Max(1f, structuralBusGap);
            RelatedPackageMetrics = relatedPackageMetrics;
        }

        public Vector2 SelectedNodeCenter { get; }

        public float HorizontalLaneGap { get; }

        public float VerticalLaneGap { get; }

        public float PackageCardGap { get; }

        public float PackageSubclusterGap { get; }

        public float CategoryRailGap { get; }

        public float OwningCategoryGap { get; }

        public float StructuralBusGap { get; }

        public PackageGraphNodeMetrics RelatedPackageMetrics { get; }

        public float ProviderPackageX => SelectedNodeCenter.x - HorizontalLaneGap;

        public float ProviderCategoryX => ProviderPackageX - CategoryRailGap;

        public float DependentPackageX => SelectedNodeCenter.x + HorizontalLaneGap;

        public float DependentCategoryX => DependentPackageX + CategoryRailGap;

        public float IntegrationPackageY => SelectedNodeCenter.y + VerticalLaneGap;

        public float IntegrationCategoryY => IntegrationPackageY + CategoryRailGap * 0.82f;

        public float CompanionPackageY => SelectedNodeCenter.y - VerticalLaneGap;

        public float CompanionCategoryY => CompanionPackageY - CategoryRailGap * 0.82f;

        public float OwningCategoryY => SelectedNodeCenter.y - OwningCategoryGap;

        public static PackageGraphEgoLayoutMetrics Create(
            Vector2 selectedNodeCenter,
            PackageGraphNodeMetrics selectedMetrics,
            PackageGraphNodeMetrics relatedMetrics)
        {
            float horizontalLaneGap = Mathf.Max(
                455f,
                selectedMetrics.Width * 0.5f + relatedMetrics.Width * 0.5f + 212f);
            float verticalLaneGap = Mathf.Max(
                365f,
                selectedMetrics.Height * 0.5f + relatedMetrics.Height * 0.5f + 195f);
            float cardGap = Mathf.Max(30f, relatedMetrics.Height * 0.22f);
            float subclusterGap = Mathf.Max(110f, relatedMetrics.Height * 0.74f);
            float categoryRailGap = Mathf.Max(230f, relatedMetrics.Width * 1.02f);
            float owningGap = selectedMetrics.Height * 0.5f + 108f;
            float structuralBusGap = Mathf.Max(34f, relatedMetrics.Width * 0.16f);

            return new PackageGraphEgoLayoutMetrics(
                selectedNodeCenter,
                horizontalLaneGap,
                verticalLaneGap,
                cardGap,
                subclusterGap,
                categoryRailGap,
                owningGap,
                structuralBusGap,
                relatedMetrics);
        }
    }

    internal sealed class PackageGraphLayout
    {
        public const float CanvasWidth = 4800f;
        public const float CanvasHeight = 4600f;
        public const float NodeWidth = 268f;
        public const float NodeHeight = 190f;

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
        private const float CategoryCaptionClearance = 24f;

        public static readonly Vector2 GraphCenter = new Vector2(2400f, 2250f);

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

            if (mode == PackageGraphLayoutMode.Filtered)
            {
                return CalculateFilteredOverview(safeGraph, presentationLevel);
            }

            if (mode == PackageGraphLayoutMode.GroupFocus &&
                TryGetGroup(safeGraph, focusGroupId, out PackageGraphGroup focusedGroup))
            {
                return CalculateGroupFocus(safeGraph, focusedGroup, presentationLevel);
            }

            if (mode == PackageGraphLayoutMode.Focus &&
                TryGetPackage(safeGraph, focusPackageId, out PackageGraphNode focusedPackage))
            {
                return CalculatePackageFocus(safeGraph, focusedPackage, presentationLevel);
            }

            return CalculateRootOverview(safeGraph, presentationLevel);
        }

        private static PackageGraphLayoutResult CalculateFilteredOverview(
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
            PackageGraphGroup[] topGroups = GetTopLevelGroups(graph)
                .Where(group => GetDescendantPackages(graph, group.Id).Any())
                .ToArray();

            if (topGroups.Length == 0)
            {
                return new PackageGraphLayoutResult(
                    PackageGraphLayoutMode.Filtered,
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

            float maxClusterRadius = topGroups
                .Select(group => CalculateFilteredClusterRadius(graph, group, packageMetrics, presentationLevel))
                .DefaultIfEmpty(0f)
                .Max();
            float globalOrbitRadius = CalculateFilteredGlobalOrbitRadius(topGroups.Length, maxClusterRadius);

            ringGuides.Add(new PackageGraphRingGuide(
                string.Empty,
                PackageGraphLayoutRing.Infrastructure,
                GraphCenter,
                globalOrbitRadius));

            float[] groupAngles = CreateEvenCircleAngles(topGroups.Length, -90f);

            for (int index = 0; index < topGroups.Length; index++)
            {
                PackageGraphGroup group = topGroups[index];
                Vector2 groupCenter = PointOnOrbit(GraphCenter, groupAngles[index], globalOrbitRadius);
                PlaceFilteredGroupRecursive(
                    graph,
                    group,
                    groupCenter,
                    topLevel: true,
                    nodeRects,
                    nodeRings,
                    nodePresentations,
                    groupNodes,
                    presentationLevel);
            }

            return new PackageGraphLayoutResult(
                PackageGraphLayoutMode.Filtered,
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
            PackageGraphNode focusNode,
            PackageGraphNodePresentationLevel presentationLevel)
        {
            Dictionary<string, Rect> nodeRects = CreateNodeRingDictionary(graph, out Dictionary<string, PackageGraphLayoutRing> nodeRings);
            Dictionary<string, PackageGraphNodePresentationLevel> nodePresentations =
                new Dictionary<string, PackageGraphNodePresentationLevel>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphNode> nodeById = graph.Nodes
                .Where(node => node != null)
                .GroupBy(node => node.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PackageGraphNodePresentationLevel selectedPresentationLevel =
                PackageGraphPresentationPolicy.GetFocusSelectedPresentation(presentationLevel);
            PackageGraphNodePresentationLevel relatedPresentationLevel =
                PackageGraphPresentationPolicy.GetFocusRelatedPresentation(presentationLevel);
            PackageGraphNodeMetrics selectedMetrics =
                PackageGraphPresentationPolicy.GetMetrics(selectedPresentationLevel);
            PackageGraphNodeMetrics relatedMetrics =
                PackageGraphPresentationPolicy.GetMetrics(relatedPresentationLevel);
            PackageGraphEgoLayoutMetrics metrics = PackageGraphEgoLayoutMetrics.Create(
                GraphCenter,
                selectedMetrics,
                relatedMetrics);

            nodeRects[focusNode.PackageId] = CenteredRect(
                metrics.SelectedNodeCenter,
                selectedMetrics);
            nodePresentations[focusNode.PackageId] = selectedPresentationLevel;
            placed.Add(focusNode.PackageId);

            string focusPackageId = focusNode.PackageId;
            List<PackageGraphNode> providers = GetProviderNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> integrationNodes = GetIntegrationNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> dependents = GetDependentNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> optionalCompanions = GetOptionalCompanionNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphNode> suiteNodes = GetSuiteNodes(graph, nodeById, focusPackageId, placed);
            List<PackageGraphGroupLayoutNode> contextGroups = new List<PackageGraphGroupLayoutNode>();
            HashSet<string> placedGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            PlaceOwningCategoryGroup(
                graph,
                focusNode,
                metrics,
                contextGroups,
                placedGroupIds);
            PlaceEgoZone(
                graph,
                providers,
                PackageGraphEgoLayoutZone.Providers,
                metrics,
                nodeRects,
                nodePresentations,
                relatedPresentationLevel,
                placed,
                contextGroups,
                placedGroupIds);
            PlaceEgoZone(
                graph,
                dependents,
                PackageGraphEgoLayoutZone.Dependents,
                metrics,
                nodeRects,
                nodePresentations,
                relatedPresentationLevel,
                placed,
                contextGroups,
                placedGroupIds);
            PlaceEgoZone(
                graph,
                integrationNodes,
                PackageGraphEgoLayoutZone.Integrations,
                metrics,
                nodeRects,
                nodePresentations,
                relatedPresentationLevel,
                placed,
                contextGroups,
                placedGroupIds);
            PlaceEgoZone(
                graph,
                MergeGroups(optionalCompanions, suiteNodes),
                PackageGraphEgoLayoutZone.CompanionsAndSuites,
                metrics,
                nodeRects,
                nodePresentations,
                relatedPresentationLevel,
                placed,
                contextGroups,
                placedGroupIds);

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

        private static void PlaceOwningCategoryGroup(
            PackageGraphModel graph,
            PackageGraphNode focusNode,
            PackageGraphEgoLayoutMetrics metrics,
            ICollection<PackageGraphGroupLayoutNode> groupNodes,
            ISet<string> placedGroupIds)
        {
            if (graph == null || focusNode == null || string.IsNullOrWhiteSpace(focusNode.GroupId))
            {
                return;
            }

            PackageGraphGroup owningGroup = graph.Groups.FirstOrDefault(group =>
                string.Equals(group.Id, focusNode.GroupId, StringComparison.OrdinalIgnoreCase));

            if (owningGroup == null || !placedGroupIds.Add(owningGroup.Id))
            {
                return;
            }

            Vector2 hubCenter = new Vector2(metrics.SelectedNodeCenter.x, metrics.OwningCategoryY);
            Rect rect = ClampToCanvas(CreateGroupElementRect(
                hubCenter,
                GroupChipHubSize,
                GroupChipCaptionWidth,
                GroupChipCaptionHeight));
            Rect hubRect = CreateGroupHubRectFromElement(rect, GroupChipHubSize);
            groupNodes.Add(CreateGroupLayoutNode(
                graph,
                owningGroup,
                rect,
                hubRect,
                focused: true,
                collapsed: true,
                packageScope: new[] { focusNode },
                summaryLabel: "Owning category"));
        }

        private static void PlaceEgoZone(
            PackageGraphModel graph,
            IReadOnlyList<PackageGraphNode> nodes,
            PackageGraphEgoLayoutZone zone,
            PackageGraphEgoLayoutMetrics metrics,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphNodePresentationLevel> nodePresentations,
            PackageGraphNodePresentationLevel relatedPresentationLevel,
            ISet<string> placedPackageIds,
            ICollection<PackageGraphGroupLayoutNode> groupNodes,
            ISet<string> placedGroupIds)
        {
            EgoCategoryCluster[] clusters = CreateEgoCategoryClusters(graph, nodes, placedPackageIds);

            if (clusters.Length == 0)
            {
                return;
            }

            if (zone == PackageGraphEgoLayoutZone.Providers ||
                zone == PackageGraphEgoLayoutZone.Dependents)
            {
                PlaceVerticalEgoClusters(
                    graph,
                    clusters,
                    zone,
                    metrics,
                    nodeRects,
                    nodePresentations,
                    relatedPresentationLevel,
                    placedPackageIds,
                    groupNodes,
                    placedGroupIds);
                return;
            }

            PlaceHorizontalEgoClusters(
                graph,
                clusters,
                zone,
                metrics,
                nodeRects,
                nodePresentations,
                relatedPresentationLevel,
                placedPackageIds,
                groupNodes,
                placedGroupIds);
        }

        private static EgoCategoryCluster[] CreateEgoCategoryClusters(
            PackageGraphModel graph,
            IReadOnlyList<PackageGraphNode> nodes,
            ISet<string> placedPackageIds)
        {
            if (graph == null || nodes == null || nodes.Count == 0)
            {
                return Array.Empty<EgoCategoryCluster>();
            }

            Dictionary<string, List<PackageGraphNode>> packagesByGroup =
                new Dictionary<string, List<PackageGraphNode>>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphNode node in nodes)
            {
                if (node == null ||
                    placedPackageIds.Contains(node.PackageId) ||
                    string.IsNullOrWhiteSpace(node.GroupId))
                {
                    continue;
                }

                if (!packagesByGroup.TryGetValue(node.GroupId, out List<PackageGraphNode> packageNodes))
                {
                    packageNodes = new List<PackageGraphNode>();
                    packagesByGroup[node.GroupId] = packageNodes;
                }

                packageNodes.Add(node);
            }

            return packagesByGroup
                .Select(pair =>
                {
                    PackageGraphGroup group = graph.Groups.FirstOrDefault(candidate =>
                        string.Equals(candidate.Id, pair.Key, StringComparison.OrdinalIgnoreCase));
                    return group == null
                        ? null
                        : new EgoCategoryCluster(
                            group,
                            pair.Value
                                .OrderBy(GetRelationshipSortIndex)
                                .ThenBy(node => GetPackageOrder(node))
                                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                                .ThenBy(node => node.PackageId, StringComparer.OrdinalIgnoreCase)
                                .ToArray());
                })
                .Where(cluster => cluster != null && cluster.Packages.Count > 0)
                .OrderBy(cluster => cluster.Group.SortOrder)
                .ThenBy(cluster => cluster.Group.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(cluster => cluster.Group.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void PlaceVerticalEgoClusters(
            PackageGraphModel graph,
            IReadOnlyList<EgoCategoryCluster> clusters,
            PackageGraphEgoLayoutZone zone,
            PackageGraphEgoLayoutMetrics metrics,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphNodePresentationLevel> nodePresentations,
            PackageGraphNodePresentationLevel relatedPresentationLevel,
            ISet<string> placedPackageIds,
            ICollection<PackageGraphGroupLayoutNode> groupNodes,
            ISet<string> placedGroupIds)
        {
            float totalHeight = clusters.Sum(cluster => GetVerticalClusterHeight(cluster, metrics)) +
                                Mathf.Max(0, clusters.Count - 1) * metrics.PackageSubclusterGap;
            float cursorY = metrics.SelectedNodeCenter.y - totalHeight * 0.5f;
            float packageX = zone == PackageGraphEgoLayoutZone.Providers
                ? metrics.ProviderPackageX
                : metrics.DependentPackageX;
            float categoryX = zone == PackageGraphEgoLayoutZone.Providers
                ? metrics.ProviderCategoryX
                : metrics.DependentCategoryX;

            foreach (EgoCategoryCluster cluster in clusters)
            {
                float clusterHeight = GetVerticalClusterHeight(cluster, metrics);
                float clusterCenterY = cursorY + clusterHeight * 0.5f;

                for (int index = 0; index < cluster.Packages.Count; index++)
                {
                    PackageGraphNode node = cluster.Packages[index];
                    Vector2 nodeCenter = new Vector2(
                        packageX,
                        cursorY + metrics.RelatedPackageMetrics.Height * 0.5f +
                        index * (metrics.RelatedPackageMetrics.Height + metrics.PackageCardGap));
                    nodeRects[node.PackageId] = ClampToCanvas(CenteredRect(nodeCenter, metrics.RelatedPackageMetrics));
                    nodePresentations[node.PackageId] = relatedPresentationLevel;
                    placedPackageIds.Add(node.PackageId);
                }

                AddEgoContextGroup(
                    graph,
                    cluster,
                    new Vector2(categoryX, clusterCenterY),
                    groupNodes,
                    placedGroupIds);
                cursorY += clusterHeight + metrics.PackageSubclusterGap;
            }
        }

        private static void PlaceHorizontalEgoClusters(
            PackageGraphModel graph,
            IReadOnlyList<EgoCategoryCluster> clusters,
            PackageGraphEgoLayoutZone zone,
            PackageGraphEgoLayoutMetrics metrics,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphNodePresentationLevel> nodePresentations,
            PackageGraphNodePresentationLevel relatedPresentationLevel,
            ISet<string> placedPackageIds,
            ICollection<PackageGraphGroupLayoutNode> groupNodes,
            ISet<string> placedGroupIds)
        {
            float totalWidth = clusters.Sum(cluster => GetHorizontalClusterWidth(cluster, metrics)) +
                               Mathf.Max(0, clusters.Count - 1) * metrics.PackageSubclusterGap;
            float cursorX = metrics.SelectedNodeCenter.x - totalWidth * 0.5f;
            float packageY = zone == PackageGraphEgoLayoutZone.Integrations
                ? metrics.IntegrationPackageY
                : metrics.CompanionPackageY;
            float categoryY = zone == PackageGraphEgoLayoutZone.Integrations
                ? metrics.IntegrationCategoryY
                : metrics.CompanionCategoryY;

            foreach (EgoCategoryCluster cluster in clusters)
            {
                float clusterWidth = GetHorizontalClusterWidth(cluster, metrics);
                float clusterCenterX = cursorX + clusterWidth * 0.5f;

                for (int index = 0; index < cluster.Packages.Count; index++)
                {
                    PackageGraphNode node = cluster.Packages[index];
                    Vector2 nodeCenter = new Vector2(
                        cursorX + metrics.RelatedPackageMetrics.Width * 0.5f +
                        index * (metrics.RelatedPackageMetrics.Width + FocusGridGapX),
                        packageY);
                    nodeRects[node.PackageId] = ClampToCanvas(CenteredRect(nodeCenter, metrics.RelatedPackageMetrics));
                    nodePresentations[node.PackageId] = relatedPresentationLevel;
                    placedPackageIds.Add(node.PackageId);
                }

                AddEgoContextGroup(
                    graph,
                    cluster,
                    new Vector2(clusterCenterX, categoryY),
                    groupNodes,
                    placedGroupIds);
                cursorX += clusterWidth + metrics.PackageSubclusterGap;
            }
        }

        private static void AddEgoContextGroup(
            PackageGraphModel graph,
            EgoCategoryCluster cluster,
            Vector2 hubCenter,
            ICollection<PackageGraphGroupLayoutNode> groupNodes,
            ISet<string> placedGroupIds)
        {
            if (cluster == null || cluster.Group == null || !placedGroupIds.Add(cluster.Group.Id))
            {
                return;
            }

            Rect rect = ClampToCanvas(CreateGroupElementRect(
                hubCenter,
                GroupChipHubSize,
                GroupChipCaptionWidth,
                GroupChipCaptionHeight));
            Rect hubRect = CreateGroupHubRectFromElement(rect, GroupChipHubSize);
            groupNodes.Add(CreateGroupLayoutNode(
                graph,
                cluster.Group,
                rect,
                hubRect,
                focused: false,
                collapsed: true,
                packageScope: cluster.Packages,
                summaryLabel: cluster.Packages.Count == 1
                    ? "1 related package"
                    : cluster.Packages.Count + " related packages"));
        }

        private static float GetVerticalClusterHeight(
            EgoCategoryCluster cluster,
            PackageGraphEgoLayoutMetrics metrics)
        {
            int count = cluster == null ? 0 : cluster.Packages.Count;
            return count <= 0
                ? 0f
                : count * metrics.RelatedPackageMetrics.Height +
                  Mathf.Max(0, count - 1) * metrics.PackageCardGap;
        }

        private static float GetHorizontalClusterWidth(
            EgoCategoryCluster cluster,
            PackageGraphEgoLayoutMetrics metrics)
        {
            int count = cluster == null ? 0 : cluster.Packages.Count;
            return count <= 0
                ? 0f
                : count * metrics.RelatedPackageMetrics.Width +
                  Mathf.Max(0, count - 1) * FocusGridGapX;
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

        private static void PlaceFilteredGroupRecursive(
            PackageGraphModel graph,
            PackageGraphGroup group,
            Vector2 center,
            bool topLevel,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphLayoutRing> nodeRings,
            IDictionary<string, PackageGraphNodePresentationLevel> nodePresentations,
            ICollection<PackageGraphGroupLayoutNode> groupNodes,
            PackageGraphNodePresentationLevel packagePresentationLevel)
        {
            IReadOnlyList<PackageGraphNode> directPackages = GetDirectPackages(graph, group.Id);
            PackageGraphGroup[] directGroups = GetChildGroups(graph, group.Id)
                .Where(childGroup => GetDescendantPackages(graph, childGroup.Id).Any())
                .ToArray();
            int childCount = directPackages.Count + directGroups.Length;
            float hubSize = topLevel ? GroupHubSize : GroupChipHubSize;
            float captionWidth = topLevel ? GroupCaptionWidth : GroupChipCaptionWidth;
            float captionHeight = topLevel ? GroupCaptionHeight : GroupChipCaptionHeight;
            float localRadius = CalculateFilteredLocalOrbitRadius(
                graph,
                group,
                packagePresentationLevel);
            Rect groupRect = CreateGroupElementRect(center, hubSize, captionWidth, captionHeight);
            Rect groupHubRect = CreateHubRect(center, hubSize);

            groupNodes.Add(CreateGroupLayoutNode(
                graph,
                group,
                groupRect,
                groupHubRect,
                focused: false,
                collapsed: !topLevel,
                orbitRadius: childCount > 0 ? localRadius : 0f));

            if (childCount <= 0)
            {
                return;
            }

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
                ChildPlacement child = orderedChildren[index];
                Vector2 childCenter = PointOnOrbit(center, angles[index], localRadius);

                if (child.Group != null)
                {
                    PlaceFilteredGroupRecursive(
                        graph,
                        child.Group,
                        childCenter,
                        topLevel: false,
                        nodeRects,
                        nodeRings,
                        nodePresentations,
                        groupNodes,
                        packagePresentationLevel);
                    continue;
                }

                Rect packageRect = CenteredRect(childCenter, packageMetrics);
                nodeRects[child.Package.PackageId] = ClampToCanvas(packageRect);
                nodeRings[child.Package.PackageId] = ResolveRing(group.Id);
                nodePresentations[child.Package.PackageId] = packagePresentationLevel;
            }
        }

        private static float CalculateFilteredLocalOrbitRadius(
            PackageGraphModel graph,
            PackageGraphGroup group,
            PackageGraphNodePresentationLevel packagePresentationLevel)
        {
            PackageGraphNodeMetrics packageMetrics = PackageGraphPresentationPolicy.GetMetrics(packagePresentationLevel);
            PackageGraphGroup[] directGroups = GetChildGroups(graph, group.Id)
                .Where(childGroup => GetDescendantPackages(graph, childGroup.Id).Any())
                .ToArray();
            int childCount = GetDirectPackages(graph, group.Id).Count + directGroups.Length;
            float childGroupDiameter = directGroups
                .Select(childGroup => CalculateFilteredClusterRadius(graph, childGroup, packageMetrics, packagePresentationLevel) * 2f)
                .DefaultIfEmpty(GroupChipCaptionWidth)
                .Max();
            float childGroupWidth = Mathf.Max(GroupChipCaptionWidth, childGroupDiameter);
            float childGroupHeight = Mathf.Max(GroupChipHubSize + GroupChipCaptionHeight, childGroupDiameter);

            return CalculateLocalOrbitRadius(
                childCount,
                packageMetrics,
                childGroupWidth,
                childGroupHeight);
        }

        private static float CalculateFilteredClusterRadius(
            PackageGraphModel graph,
            PackageGraphGroup group,
            PackageGraphNodeMetrics packageMetrics,
            PackageGraphNodePresentationLevel packagePresentationLevel)
        {
            IReadOnlyList<PackageGraphNode> directPackages = GetDirectPackages(graph, group.Id);
            PackageGraphGroup[] directGroups = GetChildGroups(graph, group.Id)
                .Where(childGroup => GetDescendantPackages(graph, childGroup.Id).Any())
                .ToArray();
            int childCount = directPackages.Count + directGroups.Length;
            float groupHalfExtent = CalculateGroupElementRadiusFromHub(
                GroupCaptionWidth,
                GroupHubSize,
                GroupCaptionHeight);

            if (childCount <= 0)
            {
                return groupHalfExtent;
            }

            float localRadius = CalculateFilteredLocalOrbitRadius(graph, group, packagePresentationLevel);
            float packageHalfExtent = CalculateHalfDiagonal(packageMetrics.Width, packageMetrics.Height);
            float childGroupHalfExtent = directGroups
                .Select(childGroup => CalculateFilteredClusterRadius(graph, childGroup, packageMetrics, packagePresentationLevel))
                .DefaultIfEmpty(CalculateHalfDiagonal(GroupChipCaptionWidth, GroupChipHubSize + GroupChipCaptionHeight))
                .Max();
            float childHalfExtent = Mathf.Max(packageHalfExtent, childGroupHalfExtent);
            return Mathf.Max(groupHalfExtent, localRadius + childHalfExtent + NodeGap);
        }

        private static float CalculateFilteredGlobalOrbitRadius(
            int topGroupCount,
            float maxClusterRadius)
        {
            float minimumRadius = Mathf.Max(320f, maxClusterRadius + 180f);

            if (topGroupCount <= 1)
            {
                return Mathf.Min(980f, minimumRadius);
            }

            float chordRadius = (maxClusterRadius * 2f + MinimumClusterGap) /
                                Mathf.Max(0.01f, 2f * Mathf.Sin(Mathf.PI / topGroupCount));
            return Mathf.Clamp(Mathf.Max(MinimumGlobalGroupOrbitRadius, chordRadius + 120f), 420f, 1360f);
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
            PackageGraphCategoryStatusSummary statusSummary =
                PackageGraphCategoryStatusSummary.Create(packages);
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
                statusSummary.InstalledCount,
                statusSummary.NotInstalledCount,
                statusSummary.AttentionCount,
                statusSummary.UnknownCount,
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
            float packageRadialHalfExtent = CalculateHalfDiagonal(packageMetrics.Width, packageMetrics.Height);
            float childGroupRadialHalfExtent = CalculateHalfDiagonal(groupWidth, groupHeight);
            float childRadialHalfExtent = Mathf.Max(packageRadialHalfExtent, childGroupRadialHalfExtent);
            float groupRadialHalfExtent = CalculateGroupElementRadiusFromHub(
                GroupCaptionWidth,
                GroupHubSize,
                GroupCaptionHeight);
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

        private static float CalculateGroupElementRadiusFromHub(
            float captionWidth,
            float hubSize,
            float captionHeight)
        {
            float halfWidth = Mathf.Max(captionWidth, hubSize) * 0.5f;
            float downwardExtent = hubSize * 0.5f + Mathf.Max(0f, captionHeight);
            return Mathf.Sqrt(halfWidth * halfWidth + downwardExtent * downwardExtent);
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

        private static int GetPackageOrder(PackageGraphNode node)
        {
            return node != null &&
                   node.PackageDefinition != null &&
                   node.PackageDefinition.HasOverviewOrder
                ? node.PackageDefinition.OverviewOrder
                : 10000;
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

        private sealed class EgoCategoryCluster
        {
            public EgoCategoryCluster(PackageGraphGroup group, IReadOnlyList<PackageGraphNode> packages)
            {
                Group = group;
                Packages = packages == null
                    ? Array.Empty<PackageGraphNode>()
                    : packages.Where(package => package != null).ToArray();
            }

            public PackageGraphGroup Group { get; }

            public IReadOnlyList<PackageGraphNode> Packages { get; }
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
