using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageGraphLayoutRing
    {
        Foundation,
        Runtime,
        Bridge,
        Suite
    }

    internal sealed class PackageGraphLayoutResult
    {
        public PackageGraphLayoutResult(
            float canvasWidth,
            float canvasHeight,
            Rect hubRect,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings,
            IEnumerable<PackageGraphRingGuide> ringGuides)
        {
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            HubRect = hubRect;
            NodeRects = nodeRects ?? new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
            NodeRings = nodeRings ?? new Dictionary<string, PackageGraphLayoutRing>(StringComparer.OrdinalIgnoreCase);
            RingGuides = ringGuides == null
                ? Array.Empty<PackageGraphRingGuide>()
                : ringGuides.Where(guide => guide != null).ToArray();
        }

        public float CanvasWidth { get; }

        public float CanvasHeight { get; }

        public Rect HubRect { get; }

        public IReadOnlyDictionary<string, Rect> NodeRects { get; }

        public IReadOnlyDictionary<string, PackageGraphLayoutRing> NodeRings { get; }

        public IReadOnlyList<PackageGraphRingGuide> RingGuides { get; }
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

    internal sealed class PackageGraphLayout
    {
        public const float CanvasWidth = 1500f;
        public const float CanvasHeight = 1080f;
        public const float NodeWidth = 238f;
        public const float NodeHeight = 136f;

        private const float HubWidth = 210f;
        private const float HubHeight = 112f;
        private const float NodeGap = 18f;
        public static readonly Vector2 GraphCenter = new Vector2(730f, 500f);

        public PackageGraphLayoutResult Calculate(PackageGraphModel graph)
        {
            PackageGraphNode[] nodes = graph == null
                ? Array.Empty<PackageGraphNode>()
                : graph.Nodes.Where(node => node != null).ToArray();
            Dictionary<string, Rect> nodeRects = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphLayoutRing> nodeRings =
                new Dictionary<string, PackageGraphLayoutRing>(StringComparer.OrdinalIgnoreCase);

            PlaceRing(nodes, PackageGraphLayoutRing.Foundation, nodeRects, nodeRings);
            PlaceRing(nodes, PackageGraphLayoutRing.Runtime, nodeRects, nodeRings);
            PlaceRing(nodes, PackageGraphLayoutRing.Bridge, nodeRects, nodeRings);
            PlaceRing(nodes, PackageGraphLayoutRing.Suite, nodeRects, nodeRings);
            ResolveOverlaps(nodeRects, nodeRings);

            Rect hubRect = new Rect(
                GraphCenter.x - HubWidth * 0.5f,
                GraphCenter.y - HubHeight * 0.5f,
                HubWidth,
                HubHeight);

            return new PackageGraphLayoutResult(
                CanvasWidth,
                CanvasHeight,
                hubRect,
                nodeRects,
                nodeRings,
                CreateRingGuides());
        }

        private static void PlaceRing(
            IEnumerable<PackageGraphNode> allNodes,
            PackageGraphLayoutRing ring,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, PackageGraphLayoutRing> nodeRings)
        {
            PackageGraphNode[] ringNodes = allNodes
                .Where(node => GetRing(node) == ring)
                .OrderBy(node => GetRingSortIndex(node, ring))
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ringNodes.Length == 0)
            {
                return;
            }

            Vector2 radius = GetRingRadius(ring);
            float[] angles = GetRingAngles(ring, ringNodes.Length);

            for (int index = 0; index < ringNodes.Length; index++)
            {
                PackageGraphNode node = ringNodes[index];
                float radians = angles[index] * Mathf.Deg2Rad;
                Vector2 nodeCenter = new Vector2(
                    GraphCenter.x + Mathf.Cos(radians) * radius.x,
                    GraphCenter.y + Mathf.Sin(radians) * radius.y);
                Rect rect = CenteredRect(nodeCenter);

                nodeRects[node.PackageId] = ClampToCanvas(rect);
                nodeRings[node.PackageId] = ring;
            }
        }

        private static PackageGraphLayoutRing GetRing(PackageGraphNode node)
        {
            if (node == null)
            {
                return PackageGraphLayoutRing.Foundation;
            }

            if (node.NodeType == PackageGraphNodeType.Bridge)
            {
                return PackageGraphLayoutRing.Bridge;
            }

            if (node.NodeType == PackageGraphNodeType.Suite)
            {
                return PackageGraphLayoutRing.Suite;
            }

            if (node.NodeType == PackageGraphNodeType.Integration ||
                string.Equals(node.Category, "UI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Category, "World", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.PackageId, "com.deucarian.diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                return PackageGraphLayoutRing.Runtime;
            }

            return PackageGraphLayoutRing.Foundation;
        }

        private static Vector2 GetRingRadius(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return new Vector2(560f, 380f);
                case PackageGraphLayoutRing.Bridge:
                    return new Vector2(620f, 350f);
                case PackageGraphLayoutRing.Suite:
                    return new Vector2(470f, 450f);
                default:
                    return new Vector2(330f, 245f);
            }
        }

        private static float[] GetRingAngles(PackageGraphLayoutRing ring, int count)
        {
            if (count <= 0)
            {
                return Array.Empty<float>();
            }

            switch (ring)
            {
                case PackageGraphLayoutRing.Foundation:
                    return GetPreferredAngles(
                        new[] { -98f, -48f, 2f, 52f, 112f, 172f, 232f },
                        -104f,
                        236f,
                        count);
                case PackageGraphLayoutRing.Runtime:
                    return GetPreferredAngles(
                        new[] { -170f, -118f, 128f, 170f, -76f, 76f },
                        -172f,
                        172f,
                        count);
                case PackageGraphLayoutRing.Bridge:
                    return CreateEvenAngles(-72f, 72f, count);
                case PackageGraphLayoutRing.Suite:
                    return CreateEvenAngles(count <= 3 ? 100f : 72f, count <= 3 ? 110f : 128f, count);
                default:
                    return CreateEvenAngles(0f, 360f, count);
            }
        }

        private static float[] GetPreferredAngles(float[] preferredAngles, float fallbackStart, float fallbackEnd, int count)
        {
            if (count <= preferredAngles.Length)
            {
                return preferredAngles.Take(count).ToArray();
            }

            return CreateEvenAngles(fallbackStart, fallbackEnd, count);
        }

        private static float[] CreateEvenAngles(float startAngle, float endAngle, int count)
        {
            if (count == 1)
            {
                return new[] { (startAngle + endAngle) * 0.5f };
            }

            float[] angles = new float[count];
            float span = endAngle - startAngle;

            for (int index = 0; index < count; index++)
            {
                angles[index] = startAngle + span * (index / (float)(count - 1));
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
                    "com.deucarian.session",
                    "com.deucarian.package-installer");
            }

            if (ring == PackageGraphLayoutRing.Runtime)
            {
                return GetKnownPackageIndex(
                    packageId,
                    "com.deucarian.ui-binding",
                    "com.deucarian.object-selection",
                    "com.deucarian.theming",
                    "com.deucarian.diagnostics");
            }

            if (ring == PackageGraphLayoutRing.Bridge)
            {
                return GetKnownPackageIndex(
                    packageId,
                    "com.deucarian.session.api-bridge",
                    "com.deucarian.object-loading.api-bridge",
                    "com.deucarian.ui-binding.core-state-bridge",
                    "com.deucarian.object-selection.core-state-bridge");
            }

            return 1000;
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
            return new Rect(
                center.x - NodeWidth * 0.5f,
                center.y - NodeHeight * 0.5f,
                NodeWidth,
                NodeHeight);
        }

        private static Rect ClampToCanvas(Rect rect)
        {
            float x = Mathf.Clamp(rect.x, 24f, CanvasWidth - rect.width - 24f);
            float y = Mathf.Clamp(rect.y, 24f, CanvasHeight - rect.height - 24f);
            return new Rect(x, y, rect.width, rect.height);
        }

        private static void ResolveOverlaps(
            IDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings)
        {
            string[] keys = nodeRects.Keys
                .OrderBy(key => nodeRings.TryGetValue(key, out PackageGraphLayoutRing ring) ? GetRingPriority(ring) : 0)
                .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (int pass = 0; pass < 12; pass++)
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

                        string moveKey = GetMoveKey(firstKey, secondKey, nodeRings);
                        Rect moveRect = nodeRects[moveKey];
                        Vector2 direction = moveRect.center - GraphCenter;

                        if (direction.sqrMagnitude < 0.01f)
                        {
                            direction = new Vector2(0f, 1f);
                        }

                        float overlapX = Mathf.Min(firstRect.xMax, secondRect.xMax) -
                                         Mathf.Max(firstRect.xMin, secondRect.xMin);
                        float overlapY = Mathf.Min(firstRect.yMax, secondRect.yMax) -
                                         Mathf.Max(firstRect.yMin, secondRect.yMin);
                        float moveDistance = Mathf.Max(10f, Mathf.Min(overlapX, overlapY) + 10f);
                        moveRect.center += direction.normalized * moveDistance;
                        nodeRects[moveKey] = ClampToCanvas(moveRect);
                        movedAny = true;
                    }
                }

                if (!movedAny)
                {
                    return;
                }
            }
        }

        private static string GetMoveKey(
            string firstKey,
            string secondKey,
            IReadOnlyDictionary<string, PackageGraphLayoutRing> nodeRings)
        {
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
                case PackageGraphLayoutRing.Bridge:
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

        private static IEnumerable<PackageGraphRingGuide> CreateRingGuides()
        {
            yield return new PackageGraphRingGuide(
                "Core / Foundation",
                PackageGraphLayoutRing.Foundation,
                GraphCenter,
                364f,
                275f);
            yield return new PackageGraphRingGuide(
                "UI / World / Runtime",
                PackageGraphLayoutRing.Runtime,
                GraphCenter,
                602f,
                418f);
            yield return new PackageGraphRingGuide(
                "Bridge / Integration",
                PackageGraphLayoutRing.Bridge,
                GraphCenter,
                666f,
                382f);
            yield return new PackageGraphRingGuide(
                "Suites",
                PackageGraphLayoutRing.Suite,
                GraphCenter,
                506f,
                492f);
        }
    }
}
