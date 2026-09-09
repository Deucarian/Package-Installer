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
    internal static class PackageGraphRouteObstacles
    {
        internal static IReadOnlyList<PackageGraphRouteObstacle> BuildRouteObstacles(
            PackageGraphConnectionBundle bundle,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            return BuildRouteObstacles(nodeRects, groupRects)
                .Where(obstacle => !ShouldIgnoreObstacleForBundle(obstacle, bundle))
                .ToArray();
        }

        internal static IReadOnlyList<PackageGraphRouteObstacle> BuildRouteObstacles(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            List<PackageGraphRouteObstacle> obstacles = new List<PackageGraphRouteObstacle>();

            if (nodeRects != null)
            {
                foreach (KeyValuePair<string, Rect> nodeRect in nodeRects)
                {
                    obstacles.Add(new PackageGraphRouteObstacle(
                        "package:" + nodeRect.Key,
                        PackageGraphRouteGeometry.InflateRect(nodeRect.Value, PackageGraphRouteMetrics.RouteObstacleMargin)));
                }
            }

            if (groupRects != null)
            {
                foreach (KeyValuePair<string, Rect> groupRect in groupRects)
                {
                    obstacles.Add(new PackageGraphRouteObstacle(
                        "category:" + groupRect.Key,
                        PackageGraphRouteGeometry.InflateRect(groupRect.Value, PackageGraphRouteMetrics.RouteObstacleMargin)));
                }
            }

            return obstacles;
        }

        internal static bool ShouldIgnoreObstacleForBundle(
            PackageGraphRouteObstacle obstacle,
            PackageGraphConnectionBundle bundle)
        {
            if (string.IsNullOrWhiteSpace(obstacle.Id) ||
                !obstacle.Id.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string packageId = obstacle.Id.Substring("package:".Length);
            return string.Equals(packageId, bundle.SourcePackageId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(packageId, bundle.TargetPackageId, StringComparison.OrdinalIgnoreCase);
        }

        internal static IReadOnlyList<PackageGraphRouteObstacle> GetRelevantRouteObstacles(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<Vector2> preferredPoints,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphConnectionBundle bundle)
        {
            if (obstacles == null || obstacles.Count == 0)
            {
                return Array.Empty<PackageGraphRouteObstacle>();
            }

            Rect routeBounds = BuildRouteSearchBounds(from, to, preferredPoints);
            List<PackageGraphRouteObstacle> relevant = new List<PackageGraphRouteObstacle>();

            foreach (PackageGraphRouteObstacle obstacle in obstacles)
            {
                if (ShouldIgnoreObstacleForBundle(obstacle, bundle) ||
                    !PackageGraphRouteGeometry.RectsOverlap(routeBounds, obstacle.Rect))
                {
                    continue;
                }

                relevant.Add(obstacle);
            }

            return relevant;
        }

        internal static Rect BuildRouteSearchBounds(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<Vector2> preferredPoints)
        {
            float minX = Mathf.Min(from.x, to.x);
            float maxX = Mathf.Max(from.x, to.x);
            float minY = Mathf.Min(from.y, to.y);
            float maxY = Mathf.Max(from.y, to.y);

            if (preferredPoints != null)
            {
                foreach (Vector2 point in preferredPoints)
                {
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    minY = Mathf.Min(minY, point.y);
                    maxY = Mathf.Max(maxY, point.y);
                }
            }

            return Rect.MinMaxRect(
                minX - PackageGraphRouteMetrics.RouteSearchBoundsPadding,
                minY - PackageGraphRouteMetrics.RouteSearchBoundsPadding,
                maxX + PackageGraphRouteMetrics.RouteSearchBoundsPadding,
                maxY + PackageGraphRouteMetrics.RouteSearchBoundsPadding);
        }

        internal static bool IsRoutePathValid(
            IReadOnlyList<Vector2> points,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles)
        {
            return IsRoutePathValid(points, obstacles, default(PackageGraphConnectionBundle));
        }

        internal static bool IsRoutePathValid(
            IReadOnlyList<Vector2> points,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphConnectionBundle bundle)
        {
            if (points == null || points.Count < 2)
            {
                return false;
            }

            for (int index = 0; index < points.Count - 1; index++)
            {
                if (!PackageGraphRouteGeometry.IsOrthogonalSegment(points[index], points[index + 1]))
                {
                    return false;
                }

                bool isFirstSegment = index == 0;
                bool isLastSegment = index == points.Count - 2;
                Rect segmentBounds = PackageGraphRouteGeometry.BuildSegmentBounds(points[index], points[index + 1]);

                foreach (PackageGraphRouteObstacle obstacle in obstacles ?? Array.Empty<PackageGraphRouteObstacle>())
                {
                    if (ShouldIgnoreObstacleForSegment(
                            obstacle,
                            bundle,
                            points[0],
                            points[points.Count - 1],
                            isFirstSegment,
                            isLastSegment))
                    {
                        continue;
                    }

                    if (PackageGraphRouteGeometry.RectsOverlap(segmentBounds, obstacle.Rect) &&
                        PackageGraphRouteGeometry.LineIntersectsRectInterior(points[index], points[index + 1], obstacle.Rect))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool ShouldIgnoreObstacleForSegment(
            PackageGraphRouteObstacle obstacle,
            PackageGraphConnectionBundle bundle,
            Vector2 routeStart,
            Vector2 routeEnd,
            bool isFirstSegment,
            bool isLastSegment)
        {
            if (ShouldIgnoreObstacleForBundle(obstacle, bundle))
            {
                return true;
            }

            if (!IsCategoryObstacle(obstacle))
            {
                return false;
            }

            return (isFirstSegment && PackageGraphRouteGeometry.RectContainsPointInclusive(obstacle.Rect, routeStart)) ||
                   (isLastSegment && PackageGraphRouteGeometry.RectContainsPointInclusive(obstacle.Rect, routeEnd));
        }

        internal static bool IsCategoryObstacle(PackageGraphRouteObstacle obstacle)
        {
            return !string.IsNullOrWhiteSpace(obstacle.Id) &&
                   obstacle.Id.StartsWith("category:", StringComparison.OrdinalIgnoreCase);
        }
    }
}
