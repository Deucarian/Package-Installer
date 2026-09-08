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
    internal static class PackageGraphEdgeRouteFactory
    {
        internal static PackageGraphEdgeRoute CreateFanOutRoute(
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
            Vector2 focusPoint = GetPort(focusRect, focusPort, PackageGraphRouteMetrics.EdgeEndpointPadding);
            Vector2 otherPoint = GetPort(otherRect, otherPort, PackageGraphRouteMetrics.EdgeEndpointPadding);
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

        internal static PackageGraphEdgeRoute CreateDirectRoute(
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

            Vector2 from = GetPort(context.FromRect, fromPort, context.ToRect, PackageGraphRouteMetrics.EdgeEndpointPadding);
            Vector2 to = GetPort(context.ToRect, toPort, context.FromRect, PackageGraphRouteMetrics.EdgeEndpointPadding);

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

        internal static PackageGraphEdgeRoute CreateValidatedRoute(
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
                cacheKey = PackageGraphRouteCacheIdentity.CreateRouteCacheKey(
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
            IReadOnlyList<Vector2> routePoints = PackageGraphRouteObstacles.IsRoutePathValid(preferredPoints, obstacles, bundle)
                ? PackageGraphRouteGeometry.SimplifyRoutePoints(preferredPoints)
                : PackageGraphRoutePathSearch.FindObstacleAwarePath(from, to, zone, obstacles, preferredPoints, bundle);

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

        internal static PackageGraphEdgeRoutePort GetFocusPort(PackageGraphEdgeRouteZone zone)
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

        internal static PackageGraphEdgeRoutePort GetRelatedPort(PackageGraphEdgeRouteZone zone)
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

        internal static Vector2 GetTrunkPoint(
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

        internal static float GetCorridorTrunkDistance(float axisDelta, float preferredDistance)
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

        internal static Vector2 GetBranchPoint(
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

        internal static Vector2 GetPort(
            Rect rect,
            PackageGraphEdgeRoutePort port,
            Rect otherRect,
            float padding)
        {
            return port == PackageGraphEdgeRoutePort.Auto
                ? GetAutoPort(rect, otherRect, padding)
                : GetPort(rect, port, padding);
        }

        internal static Vector2 GetPort(
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

        internal static Vector2 GetAutoPort(Rect fromRect, Rect toRect, float padding)
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
    }
}
