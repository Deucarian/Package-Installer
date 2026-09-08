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
    internal static class PackageGraphRoutePathSearch
    {
        internal static IReadOnlyList<Vector2> FindObstacleAwarePath(
            Vector2 from,
            Vector2 to,
            PackageGraphEdgeRouteZone zone,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            IReadOnlyList<Vector2> preferredPoints,
            PackageGraphConnectionBundle bundle)
        {
            List<IReadOnlyList<Vector2>> simpleCandidates = new List<IReadOnlyList<Vector2>>(4);
            AddRouteCandidate(simpleCandidates, preferredPoints);
            AddRouteCandidate(simpleCandidates, new[] { from, to });
            AddRouteCandidate(simpleCandidates, new[] { from, new Vector2(to.x, from.y), to });
            AddRouteCandidate(simpleCandidates, new[] { from, new Vector2(from.x, to.y), to });

            IReadOnlyList<Vector2> simpleRoute = FindBestValidRoute(simpleCandidates, obstacles, bundle, zone);

            if (simpleRoute != null)
            {
                return simpleRoute;
            }

            List<IReadOnlyList<Vector2>> candidates = new List<IReadOnlyList<Vector2>>(PackageGraphRouteMetrics.MaxRouteCandidateCount);
            AddRouteCandidate(candidates, preferredPoints);
            AddRouteCandidate(candidates, new[] { from, to });
            AddRouteCandidate(candidates, new[] { from, new Vector2(to.x, from.y), to });
            AddRouteCandidate(candidates, new[] { from, new Vector2(from.x, to.y), to });

            IReadOnlyList<PackageGraphRouteObstacle> channelObstacles = PackageGraphRouteObstacles.GetRelevantRouteObstacles(
                from,
                to,
                preferredPoints,
                obstacles,
                bundle);
            float[] xChannels = PackageGraphRouteChannels.LimitRouteChannels(
                PackageGraphRouteChannels.GetCandidateXChannels(from, to, channelObstacles, bundle),
                (from.x + to.x) * 0.5f);
            float[] yChannels = PackageGraphRouteChannels.LimitRouteChannels(
                PackageGraphRouteChannels.GetCandidateYChannels(from, to, channelObstacles, bundle),
                (from.y + to.y) * 0.5f);

            foreach (float x in xChannels)
            {
                if (candidates.Count >= PackageGraphRouteMetrics.MaxRouteCandidateCount)
                {
                    break;
                }

                AddRouteCandidate(candidates, new[]
                {
                    from,
                    new Vector2(x, from.y),
                    new Vector2(x, to.y),
                    to
                });
            }

            foreach (float y in yChannels)
            {
                if (candidates.Count >= PackageGraphRouteMetrics.MaxRouteCandidateCount)
                {
                    break;
                }

                AddRouteCandidate(candidates, new[]
                {
                    from,
                    new Vector2(from.x, y),
                    new Vector2(to.x, y),
                    to
                });
            }

            foreach (float x in xChannels)
            {
                foreach (float y in yChannels)
                {
                    if (candidates.Count >= PackageGraphRouteMetrics.MaxRouteCandidateCount)
                    {
                        break;
                    }

                    AddRouteCandidate(candidates, new[]
                    {
                        from,
                        new Vector2(x, from.y),
                        new Vector2(x, y),
                        new Vector2(to.x, y),
                        to
                    });
                    AddRouteCandidate(candidates, new[]
                    {
                        from,
                        new Vector2(from.x, y),
                        new Vector2(x, y),
                        new Vector2(x, to.y),
                        to
                    });
                }

                if (candidates.Count >= PackageGraphRouteMetrics.MaxRouteCandidateCount)
                {
                    break;
                }
            }

            IReadOnlyList<Vector2> cappedRoute = FindBestValidRoute(candidates, obstacles, bundle, zone);

            if (cappedRoute != null)
            {
                return cappedRoute;
            }

            float[] allXChannels = PackageGraphRouteChannels.GetCandidateXChannels(from, to, obstacles, bundle);
            float[] allYChannels = PackageGraphRouteChannels.GetCandidateYChannels(from, to, obstacles, bundle);

            if (allXChannels.Length != xChannels.Length || allYChannels.Length != yChannels.Length)
            {
                List<IReadOnlyList<Vector2>> fallbackCandidates = new List<IReadOnlyList<Vector2>>();

                foreach (float x in allXChannels)
                {
                    AddRouteCandidate(fallbackCandidates, new[]
                    {
                        from,
                        new Vector2(x, from.y),
                        new Vector2(x, to.y),
                        to
                    });
                }

                foreach (float y in allYChannels)
                {
                    AddRouteCandidate(fallbackCandidates, new[]
                    {
                        from,
                        new Vector2(from.x, y),
                        new Vector2(to.x, y),
                        to
                    });
                }

                foreach (float x in allXChannels)
                {
                    foreach (float y in allYChannels)
                    {
                        AddRouteCandidate(fallbackCandidates, new[]
                        {
                            from,
                            new Vector2(x, from.y),
                            new Vector2(x, y),
                            new Vector2(to.x, y),
                            to
                        });
                        AddRouteCandidate(fallbackCandidates, new[]
                        {
                            from,
                            new Vector2(from.x, y),
                            new Vector2(x, y),
                            new Vector2(x, to.y),
                            to
                        });
                    }
                }

                IReadOnlyList<Vector2> fallbackRoute = FindBestValidRoute(
                    fallbackCandidates,
                    obstacles,
                    bundle,
                    zone);

                if (fallbackRoute != null)
                {
                    return fallbackRoute;
                }
            }

            return CreateOrthogonalFallback(from, to, zone);
        }

        internal static IReadOnlyList<Vector2> CreateOrthogonalFallback(
            Vector2 from,
            Vector2 to,
            PackageGraphEdgeRouteZone zone)
        {
            bool horizontalFirst = zone == PackageGraphEdgeRouteZone.Providers ||
                                   zone == PackageGraphEdgeRouteZone.Dependents ||
                                   (zone == PackageGraphEdgeRouteZone.Direct &&
                                    Mathf.Abs(to.x - from.x) >= Mathf.Abs(to.y - from.y));
            Vector2 corner = horizontalFirst
                ? new Vector2(to.x, from.y)
                : new Vector2(from.x, to.y);
            return PackageGraphRouteGeometry.SimplifyRoutePoints(new[] { from, corner, to });
        }

        internal static IReadOnlyList<Vector2> FindBestValidRoute(
            IEnumerable<IReadOnlyList<Vector2>> candidates,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphConnectionBundle bundle,
            PackageGraphEdgeRouteZone zone)
        {
            if (candidates == null)
            {
                return null;
            }

            IReadOnlyList<Vector2> best = null;
            float bestScore = float.PositiveInfinity;

            foreach (IReadOnlyList<Vector2> candidate in candidates)
            {
                IReadOnlyList<Vector2> simplified = PackageGraphRouteGeometry.SimplifyRoutePoints(candidate);

                if (!PackageGraphRouteObstacles.IsRoutePathValid(simplified, obstacles, bundle))
                {
                    continue;
                }

                float score = ScoreRoutePath(simplified, zone);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = simplified;
                }
            }

            return best;
        }

        internal static void AddRouteCandidate(
            ICollection<IReadOnlyList<Vector2>> candidates,
            IReadOnlyList<Vector2> points)
        {
            IReadOnlyList<Vector2> simplified = PackageGraphRouteGeometry.SimplifyRoutePoints(points);

            if (simplified.Count >= 2)
            {
                candidates.Add(simplified);
            }
        }

        internal static float ScoreRoutePath(
            IReadOnlyList<Vector2> points,
            PackageGraphEdgeRouteZone zone)
        {
            float length = PackageGraphRouteGeometry.GetRouteLength(points);
            float direct = Vector2.Distance(points[0], points[points.Count - 1]);
            float detourRatio = direct > 1f ? length / direct : 1f;
            float bendCost = Mathf.Max(0, points.Count - 2) * PackageGraphRouteMetrics.RouteBendPenalty;
            float detourCost = detourRatio > 1.75f
                ? (detourRatio - 1.75f) * PackageGraphRouteMetrics.RouteDetourPenalty * direct
                : 0f;
            return length + bendCost + detourCost + GetRouteZonePenalty(points, zone);
        }

        internal static float GetRouteZonePenalty(
            IReadOnlyList<Vector2> points,
            PackageGraphEdgeRouteZone zone)
        {
            if (points == null || points.Count < 2)
            {
                return 0f;
            }

            Vector2 from = points[0];
            Vector2 to = points[points.Count - 1];

            switch (zone)
            {
                case PackageGraphEdgeRouteZone.Providers:
                case PackageGraphEdgeRouteZone.Dependents:
                    return Mathf.Abs(to.y - from.y) * 0.03f;
                case PackageGraphEdgeRouteZone.Integrations:
                case PackageGraphEdgeRouteZone.CompanionsAndSuites:
                    return Mathf.Abs(to.x - from.x) * 0.03f;
                default:
                    return 0f;
            }
        }
    }
}
