using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageEcosystemGroup
    {
        Foundation,
        ServicesRuntime,
        ExperienceUiWorld,
        ToolsQuality
    }

    internal sealed class PackageOverviewPlacement
    {
        public PackageOverviewPlacement(
            PackageGraphNode node,
            PackageEcosystemGroup group,
            float angleDegrees,
            float radius,
            PackageGraphLayoutRing ring)
        {
            Node = node;
            Group = group;
            AngleDegrees = NormalizeAngle(angleDegrees);
            Radius = radius;
            Ring = ring;
        }

        public PackageGraphNode Node { get; }

        public PackageEcosystemGroup Group { get; }

        public float AngleDegrees { get; }

        public float Radius { get; }

        public PackageGraphLayoutRing Ring { get; }

        private static float NormalizeAngle(float angle)
        {
            while (angle <= -180f)
            {
                angle += 360f;
            }

            while (angle > 180f)
            {
                angle -= 360f;
            }

            return angle;
        }
    }

    internal static class PackageEcosystemSemanticWheelLayout
    {
        private const float OverviewNodeWidth = 212f;
        private const float OverviewNodeHeight = 94f;
        private const float PrimaryOrbitRadius = 650f;
        private const float IntegrationOrbitRadius = 505f;
        private const float SuiteOrbitRadius = 790f;
        private const float SectorLabelRadius = 768f;
        private const float OverviewNodeGap = 16f;

        private static readonly float[] IntegrationAngleOffsets =
        {
            0f,
            -10f,
            10f,
            -20f,
            20f,
            -30f,
            30f
        };

        private static readonly float[] IntegrationRadii =
        {
            IntegrationOrbitRadius,
            390f,
            560f,
            450f,
            335f
        };

        private static readonly float[] SuiteAngleOffsets =
        {
            0f,
            -10f,
            10f,
            -20f,
            20f
        };

        private static readonly float[] SuiteRadii =
        {
            SuiteOrbitRadius,
            860f,
            730f
        };

        private static readonly GroupDefinition[] Groups =
        {
            new GroupDefinition(
                PackageEcosystemGroup.Foundation,
                "Foundation",
                "foundation",
                PackageGraphLayoutRing.Foundation,
                -135f,
                -45f,
                -90f),
            new GroupDefinition(
                PackageEcosystemGroup.ServicesRuntime,
                "Services / Runtime",
                "runtime",
                PackageGraphLayoutRing.Runtime,
                -45f,
                45f,
                0f),
            new GroupDefinition(
                PackageEcosystemGroup.ExperienceUiWorld,
                "Experience / UI / World",
                "experience",
                PackageGraphLayoutRing.Runtime,
                45f,
                135f,
                90f),
            new GroupDefinition(
                PackageEcosystemGroup.ToolsQuality,
                "Tools / Quality",
                "tools",
                PackageGraphLayoutRing.Runtime,
                135f,
                225f,
                180f)
        };

        public static void PlaceOverview(
            PackageGraphModel graph,
            IReadOnlyList<PackageGraphNode> nodes,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphLayoutRing> nodeRings)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            Dictionary<string, float> angleByPackageId = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            PlaceNormalPackages(nodes, nodeRects, nodeRings, angleByPackageId);
            PlaceIntegrationPackages(graph, nodes, nodeRects, nodeRings, angleByPackageId);
            PlaceSuitePackages(graph, nodes, nodeRects, nodeRings, angleByPackageId);
        }

        public static PackageGraphLayoutRing ResolveRing(PackageGraphNode node)
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

            return ResolveGroup(node) == PackageEcosystemGroup.Foundation
                ? PackageGraphLayoutRing.Foundation
                : PackageGraphLayoutRing.Runtime;
        }

        public static IEnumerable<PackageGraphRingGuide> CreateRingGuides()
        {
            yield return new PackageGraphRingGuide(
                "Semantic Ecosystem Wheel",
                PackageGraphLayoutRing.Foundation,
                PackageGraphLayout.GraphCenter,
                PrimaryOrbitRadius,
                PrimaryOrbitRadius);
        }

        public static IEnumerable<PackageGraphSectorLabel> CreateSectorLabels()
        {
            foreach (GroupDefinition group in Groups)
            {
                yield return new PackageGraphSectorLabel(
                    group.Label,
                    group.Ring,
                    PointOnOrbit(group.LabelAngleDegrees, SectorLabelRadius),
                    group.ClassName);
            }
        }

        public static PackageEcosystemGroup ResolveGroup(PackageGraphNode node)
        {
            if (node == null)
            {
                return PackageEcosystemGroup.Foundation;
            }

            if (TryParseGroup(node.PackageDefinition != null ? node.PackageDefinition.EcosystemGroup : string.Empty, out PackageEcosystemGroup group))
            {
                return group;
            }

            if (TryGetKnownGroup(node.PackageId, out group))
            {
                return group;
            }

            if (string.Equals(node.Category, "UI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Category, "World", StringComparison.OrdinalIgnoreCase))
            {
                return PackageEcosystemGroup.ExperienceUiWorld;
            }

            if (string.Equals(node.Category, "Tools", StringComparison.OrdinalIgnoreCase))
            {
                return PackageEcosystemGroup.ToolsQuality;
            }

            if (string.Equals(node.Category, "Services", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Category, "Runtime", StringComparison.OrdinalIgnoreCase))
            {
                return PackageEcosystemGroup.ServicesRuntime;
            }

            return PackageEcosystemGroup.Foundation;
        }

        private static void PlaceNormalPackages(
            IEnumerable<PackageGraphNode> nodes,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphLayoutRing> nodeRings,
            IDictionary<string, float> angleByPackageId)
        {
            foreach (GroupDefinition group in Groups)
            {
                PackageGraphNode[] groupNodes = nodes
                    .Where(node => node != null &&
                                   node.NodeType != PackageGraphNodeType.Integration &&
                                   node.NodeType != PackageGraphNodeType.Suite &&
                                   ResolveGroup(node) == group.Group)
                    .OrderBy(GetOverviewOrder)
                    .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(node => node.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (groupNodes.Length == 0)
                {
                    continue;
                }

                float[] angles = CreateSectorAngles(group, groupNodes.Length);

                for (int index = 0; index < groupNodes.Length; index++)
                {
                    PackageGraphNode node = groupNodes[index];
                    float angle = angles[index];
                    nodeRects[node.PackageId] = CenteredRect(PointOnOrbit(angle, PrimaryOrbitRadius));
                    nodeRings[node.PackageId] = group.Ring;
                    angleByPackageId[node.PackageId] = angle;
                }
            }
        }

        private static void PlaceIntegrationPackages(
            PackageGraphModel graph,
            IEnumerable<PackageGraphNode> nodes,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphLayoutRing> nodeRings,
            IDictionary<string, float> angleByPackageId)
        {
            PackageOverviewPlacement[] preferredPlacements = nodes
                .Where(node => node != null && node.NodeType == PackageGraphNodeType.Integration)
                .Select(node => new PackageOverviewPlacement(
                    node,
                    PackageEcosystemGroup.ServicesRuntime,
                    ResolveIntegrationAngle(graph, node, angleByPackageId),
                    IntegrationOrbitRadius,
                    PackageGraphLayoutRing.Integration))
                .OrderBy(placement => UnwrapAngleNear(placement.AngleDegrees, 0f))
                .ThenBy(placement => GetOverviewOrder(placement.Node))
                .ThenBy(placement => placement.Node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (PackageOverviewPlacement placement in preferredPlacements)
            {
                PackageGraphNode node = placement.Node;
                PlacementCandidate candidate = FindPlacementCandidate(
                    placement.AngleDegrees,
                    IntegrationAngleOffsets,
                    IntegrationRadii,
                    nodeRects.Values);
                nodeRects[node.PackageId] = candidate.Rect;
                nodeRings[node.PackageId] = PackageGraphLayoutRing.Integration;
                angleByPackageId[node.PackageId] = candidate.AngleDegrees;
            }
        }

        private static void PlaceSuitePackages(
            PackageGraphModel graph,
            IEnumerable<PackageGraphNode> nodes,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphLayoutRing> nodeRings,
            IDictionary<string, float> angleByPackageId)
        {
            PackageOverviewPlacement[] preferredPlacements = nodes
                .Where(node => node != null && node.NodeType == PackageGraphNodeType.Suite)
                .Select(node => new PackageOverviewPlacement(
                    node,
                    PackageEcosystemGroup.ExperienceUiWorld,
                    ResolveSuiteAngle(graph, node, angleByPackageId),
                    SuiteOrbitRadius,
                    PackageGraphLayoutRing.Suite))
                .OrderBy(placement => UnwrapAngleNear(placement.AngleDegrees, 90f))
                .ThenBy(placement => GetOverviewOrder(placement.Node))
                .ThenBy(placement => placement.Node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (PackageOverviewPlacement placement in preferredPlacements)
            {
                PackageGraphNode node = placement.Node;
                PlacementCandidate candidate = FindPlacementCandidate(
                    placement.AngleDegrees,
                    SuiteAngleOffsets,
                    SuiteRadii,
                    nodeRects.Values);
                nodeRects[node.PackageId] = candidate.Rect;
                nodeRings[node.PackageId] = PackageGraphLayoutRing.Suite;
                angleByPackageId[node.PackageId] = candidate.AngleDegrees;
            }
        }

        private static float ResolveIntegrationAngle(
            PackageGraphModel graph,
            PackageGraphNode node,
            IDictionary<string, float> angleByPackageId)
        {
            string[] targetPackageIds = GetIntegrationTargetPackageIds(graph, node).ToArray();
            float[] targetAngles = targetPackageIds
                .Where(angleByPackageId.ContainsKey)
                .Select(packageId => angleByPackageId[packageId])
                .ToArray();

            return targetAngles.Length > 0
                ? CircularMean(targetAngles, 0f)
                : GetFallbackAngle(node, PackageEcosystemGroup.ServicesRuntime);
        }

        private static float ResolveSuiteAngle(
            PackageGraphModel graph,
            PackageGraphNode node,
            IDictionary<string, float> angleByPackageId)
        {
            string[] memberPackageIds = GetSuiteMemberPackageIds(graph, node).ToArray();
            float[] memberAngles = memberPackageIds
                .Where(angleByPackageId.ContainsKey)
                .Select(packageId => angleByPackageId[packageId])
                .ToArray();

            return memberAngles.Length > 0
                ? CircularMean(memberAngles, 90f)
                : GetFallbackAngle(node, PackageEcosystemGroup.ExperienceUiWorld);
        }

        private static IEnumerable<string> GetIntegrationTargetPackageIds(
            PackageGraphModel graph,
            PackageGraphNode node)
        {
            if (node.PackageDefinition != null)
            {
                if (node.PackageDefinition.IntegrationTargets.Count > 0)
                {
                    return node.PackageDefinition.IntegrationTargets;
                }

                if (node.PackageDefinition.Dependencies.Count > 0)
                {
                    return node.PackageDefinition.Dependencies;
                }
            }

            return graph == null
                ? Array.Empty<string>()
                : graph.Edges
                    .Where(edge => edge.Kind == PackageGraphEdgeKind.IntegrationConnection &&
                                   edge.ConnectsPackage(node.PackageId))
                    .Select(edge => edge.GetOtherPackageId(node.PackageId))
                    .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        private static IEnumerable<string> GetSuiteMemberPackageIds(
            PackageGraphModel graph,
            PackageGraphNode node)
        {
            if (node.PackageDefinition != null && node.PackageDefinition.SuiteMembers.Count > 0)
            {
                return node.PackageDefinition.SuiteMembers;
            }

            PackageGraphSuiteRegion region = graph == null
                ? null
                : graph.SuiteRegions.FirstOrDefault(candidate =>
                    string.Equals(candidate.SuitePackageId, node.PackageId, StringComparison.OrdinalIgnoreCase));

            return region == null ? Array.Empty<string>() : region.MemberPackageIds;
        }

        private static float[] CreateSectorAngles(GroupDefinition group, int count)
        {
            if (count <= 0)
            {
                return Array.Empty<float>();
            }

            if (count == 1)
            {
                return new[] { NormalizeAngle(group.CenterAngleDegrees) };
            }

            float span = group.EndAngleDegrees - group.StartAngleDegrees;
            float step = span / (count + 1f);
            float[] angles = new float[count];

            for (int index = 0; index < count; index++)
            {
                angles[index] = NormalizeAngle(group.StartAngleDegrees + step * (index + 1));
            }

            return angles;
        }

        private static PlacementCandidate FindPlacementCandidate(
            float preferredAngle,
            IEnumerable<float> angleOffsets,
            IEnumerable<float> radii,
            IEnumerable<Rect> existingRects)
        {
            float[] radiusValues = radii == null ? Array.Empty<float>() : radii.ToArray();
            float[] angleOffsetValues = angleOffsets == null ? new[] { 0f } : angleOffsets.ToArray();

            foreach (float offset in angleOffsetValues)
            {
                foreach (float radius in radiusValues)
                {
                    float angle = NormalizeAngle(preferredAngle + offset);
                    Rect rect = CenteredRect(PointOnOrbit(angle, radius));

                    if (!OverlapsAny(rect, existingRects))
                    {
                        return new PlacementCandidate(angle, radius, rect);
                    }
                }
            }

            float fallbackAngle = NormalizeAngle(preferredAngle);
            float fallbackRadius = radiusValues.Length > 0 ? radiusValues[0] : IntegrationOrbitRadius;
            return new PlacementCandidate(
                fallbackAngle,
                fallbackRadius,
                CenteredRect(PointOnOrbit(fallbackAngle, fallbackRadius)));
        }

        private static bool OverlapsAny(Rect rect, IEnumerable<Rect> existingRects)
        {
            Rect expanded = Expand(rect, OverviewNodeGap);

            foreach (Rect existingRect in existingRects)
            {
                if (expanded.Overlaps(Expand(existingRect, OverviewNodeGap)))
                {
                    return true;
                }
            }

            return false;
        }

        private static float CircularMean(IReadOnlyList<float> angles, float fallbackAngle)
        {
            if (angles == null || angles.Count == 0)
            {
                return NormalizeAngle(fallbackAngle);
            }

            float x = 0f;
            float y = 0f;

            foreach (float angle in angles)
            {
                float radians = angle * Mathf.Deg2Rad;
                x += Mathf.Cos(radians);
                y += Mathf.Sin(radians);
            }

            if (Mathf.Abs(x) < 0.0001f && Mathf.Abs(y) < 0.0001f)
            {
                return NormalizeAngle(fallbackAngle);
            }

            return NormalizeAngle(Mathf.Atan2(y, x) * Mathf.Rad2Deg);
        }

        private static float GetFallbackAngle(PackageGraphNode node, PackageEcosystemGroup group)
        {
            GroupDefinition definition = GetGroupDefinition(group);
            float order = Mathf.Clamp(GetOverviewOrder(node), 1f, 100f);
            float t = Mathf.Clamp01(order / 100f);
            return NormalizeAngle(Mathf.Lerp(definition.StartAngleDegrees, definition.EndAngleDegrees, t));
        }

        private static int GetOverviewOrder(PackageGraphNode node)
        {
            if (node == null)
            {
                return 1000;
            }

            if (node.PackageDefinition != null && node.PackageDefinition.HasOverviewOrder)
            {
                return node.PackageDefinition.OverviewOrder;
            }

            int knownIndex = GetKnownPackageIndex(
                node.PackageId,
                "com.deucarian.editor",
                "com.deucarian.logging",
                "com.deucarian.core-state",
                "com.deucarian.api",
                "com.deucarian.session",
                "com.deucarian.object-loading",
                "com.deucarian.ui-binding",
                "com.deucarian.object-selection",
                "com.deucarian.theming",
                "com.deucarian.diagnostics",
                "com.deucarian.package-installer",
                "com.deucarian.session.api-integration",
                "com.deucarian.object-loading.api-integration",
                "com.deucarian.ui-binding.core-state-integration",
                "com.deucarian.object-selection.core-state-integration",
                "com.deucarian.selection-suite");

            return knownIndex < 1000 ? (knownIndex + 1) * 10 : 1000;
        }

        private static bool TryParseGroup(string value, out PackageEcosystemGroup group)
        {
            string normalized = NormalizeToken(value);

            switch (normalized)
            {
                case "foundation":
                case "core":
                    group = PackageEcosystemGroup.Foundation;
                    return true;
                case "services":
                case "service":
                case "runtime":
                case "servicesruntime":
                    group = PackageEcosystemGroup.ServicesRuntime;
                    return true;
                case "experience":
                case "ui":
                case "world":
                case "experienceuiworld":
                    group = PackageEcosystemGroup.ExperienceUiWorld;
                    return true;
                case "tools":
                case "quality":
                case "toolsquality":
                    group = PackageEcosystemGroup.ToolsQuality;
                    return true;
                default:
                    group = PackageEcosystemGroup.Foundation;
                    return false;
            }
        }

        private static bool TryGetKnownGroup(string packageId, out PackageEcosystemGroup group)
        {
            switch ((packageId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "com.deucarian.editor":
                case "com.deucarian.logging":
                case "com.deucarian.core-state":
                    group = PackageEcosystemGroup.Foundation;
                    return true;
                case "com.deucarian.api":
                case "com.deucarian.session":
                case "com.deucarian.object-loading":
                    group = PackageEcosystemGroup.ServicesRuntime;
                    return true;
                case "com.deucarian.ui-binding":
                case "com.deucarian.object-selection":
                case "com.deucarian.theming":
                    group = PackageEcosystemGroup.ExperienceUiWorld;
                    return true;
                case "com.deucarian.diagnostics":
                case "com.deucarian.package-installer":
                    group = PackageEcosystemGroup.ToolsQuality;
                    return true;
                default:
                    group = PackageEcosystemGroup.Foundation;
                    return false;
            }
        }

        private static GroupDefinition GetGroupDefinition(PackageEcosystemGroup group)
        {
            return Groups.First(definition => definition.Group == group);
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

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] chars = value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray();
            return new string(chars);
        }

        private static Rect CenteredRect(Vector2 center)
        {
            return new Rect(
                center.x - OverviewNodeWidth * 0.5f,
                center.y - OverviewNodeHeight * 0.5f,
                OverviewNodeWidth,
                OverviewNodeHeight);
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(
                rect.x - amount,
                rect.y - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }

        private static Vector2 PointOnOrbit(float angleDegrees, float radius)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(
                PackageGraphLayout.GraphCenter.x + Mathf.Cos(radians) * radius,
                PackageGraphLayout.GraphCenter.y + Mathf.Sin(radians) * radius);
        }

        private static float UnwrapAngleNear(float angle, float reference)
        {
            float unwrapped = NormalizeAngle(angle);

            while (unwrapped - reference > 180f)
            {
                unwrapped -= 360f;
            }

            while (unwrapped - reference < -180f)
            {
                unwrapped += 360f;
            }

            return unwrapped;
        }

        private static float NormalizeAngle(float angle)
        {
            while (angle <= -180f)
            {
                angle += 360f;
            }

            while (angle > 180f)
            {
                angle -= 360f;
            }

            return angle;
        }

        private sealed class GroupDefinition
        {
            public GroupDefinition(
                PackageEcosystemGroup group,
                string label,
                string className,
                PackageGraphLayoutRing ring,
                float startAngleDegrees,
                float endAngleDegrees,
                float labelAngleDegrees)
            {
                Group = group;
                Label = label;
                ClassName = className;
                Ring = ring;
                StartAngleDegrees = startAngleDegrees;
                EndAngleDegrees = endAngleDegrees;
                CenterAngleDegrees = (startAngleDegrees + endAngleDegrees) * 0.5f;
                LabelAngleDegrees = labelAngleDegrees;
            }

            public PackageEcosystemGroup Group { get; }

            public string Label { get; }

            public string ClassName { get; }

            public PackageGraphLayoutRing Ring { get; }

            public float StartAngleDegrees { get; }

            public float EndAngleDegrees { get; }

            public float CenterAngleDegrees { get; }

            public float LabelAngleDegrees { get; }
        }

        private readonly struct PlacementCandidate
        {
            public PlacementCandidate(float angleDegrees, float radius, Rect rect)
            {
                AngleDegrees = angleDegrees;
                Radius = radius;
                Rect = rect;
            }

            public float AngleDegrees { get; }

            public float Radius { get; }

            public Rect Rect { get; }
        }
    }
}
