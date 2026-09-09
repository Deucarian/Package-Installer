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
    internal sealed class PackageGraphEdgeLayer : VisualElement
    {

        private readonly PackageGraphGeometrySnapshot geometry = new PackageGraphGeometrySnapshot();

        private IReadOnlyDictionary<string, Rect> _nodeRects => geometry.Nodes;
        private IReadOnlyDictionary<string, Rect> _groupRects => geometry.Groups;

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphFocus _focus = PackageGraphFocus.Create(null, string.Empty);
        private string _previewPackageId = string.Empty;
        private readonly PackageGraphEdgeRouteCache _routeCache = new PackageGraphEdgeRouteCache();
        private IReadOnlyList<PackageGraphEdgeRoute> _routes = Array.Empty<PackageGraphEdgeRoute>();
        public PackageGraphEdgeLayer()
        {
            generateVisualContent += GenerateEdges;
        }

        public void SetGraph(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            float canvasHeight,
            PackageGraphFocus focus)
        {
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _focus = focus ?? PackageGraphFocus.Create(_graph, string.Empty);
            geometry.Update(nodeRects, groupRects);
            long styleStartTicks = Stopwatch.GetTimestamp();
            style.height = canvasHeight;
            long styleTicks = Stopwatch.GetTimestamp() - styleStartTicks;
            PackageGraphEdgeRouteBuildDiagnostics diagnostics = RebuildRoutes();
            diagnostics.AddStyleClassUpdateTicks(styleTicks);
            long visualReuseStartTicks = Stopwatch.GetTimestamp();
            diagnostics.AddVisualElementReuseTicks(Stopwatch.GetTimestamp() - visualReuseStartTicks);
            MarkDirtyRepaint();
            PackageGraphOpenProfiler.Current?.AddEdgeRouteDiagnostics(diagnostics);
        }

        public void UpdateRects(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            geometry.Update(nodeRects, groupRects);
            PackageGraphEdgeRouteBuildDiagnostics diagnostics = RebuildRoutes();
            MarkDirtyRepaint();
            PackageGraphOpenProfiler.Current?.AddEdgeRouteDiagnostics(diagnostics);
        }

        public void SetPreviewPackage(string packageId)
        {
            string nextPackageId = packageId ?? string.Empty;

            if (string.Equals(_previewPackageId, nextPackageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _previewPackageId = nextPackageId;
            MarkDirtyRepaint();
        }

        private void GenerateEdges(MeshGenerationContext context)
        {
            if (_graph == null || _graph.Edges.Count == 0)
            {
                return;
            }

            long painterStartTicks = Stopwatch.GetTimestamp();
            PackageGraphPainter painter = PackageGraphPainterCompatibility.Create(context);

            foreach (PackageGraphEdgeRoute route in _routes)
            {
                PackageGraphEdgePainter.DrawEdge(
                    painter,
                    _graph,
                    route,
                    PackageGraphEdgeAppearance.IsRouteEmphasized(route, _focus, _previewPackageId),
                    _focus.HasFocus,
                    PackageGraphRouteMetrics.StaticEdgePhase);
            }

            PackageGraphPainterCompatibility.Complete(painter);

            PackageGraphOpenProfiler.Current?.AddEdgeRouteDiagnostics(
                new PackageGraphEdgeRouteBuildDiagnostics
                {
                    PainterPassTicks = Stopwatch.GetTimestamp() - painterStartTicks
                });
        }

        internal static bool IsRouteEmphasized(
            PackageGraphEdgeRoute route,
            PackageGraphFocus focus,
            string previewPackageId)
        {
            return PackageGraphEdgeAppearance.IsRouteEmphasized(route, focus, previewPackageId);
        }

        internal IReadOnlyList<PackageGraphEdgeRoute> BuildRoutesSnapshotForTests()
        {
            return _routes;
        }

        private PackageGraphEdgeRouteBuildDiagnostics RebuildRoutes()
        {
            PackageGraphEdgeRouteBuildDiagnostics diagnostics = new PackageGraphEdgeRouteBuildDiagnostics();

            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.EdgeCreation))
            {
                long geometryStartTicks = Stopwatch.GetTimestamp();
                string layoutSignature = PackageGraphRouteCacheIdentity.BuildRouteLayoutSignature(_nodeRects, _groupRects);
                diagnostics.AddGeometryLayoutReadTicks(Stopwatch.GetTimestamp() - geometryStartTicks);

                _routes = PackageGraphEdgeRoutePlanner.BuildRoutes(
                    _graph,
                    _nodeRects,
                    _groupRects,
                    _focus,
                    _routeCache,
                    layoutSignature,
                    ref diagnostics);
            }

            PackageGraphOpenProfiler.Current?.SetRenderCounts(0, _routes.Count);
            diagnostics.RouteCount = _routes.Count;
            return diagnostics;
        }

        internal static IReadOnlyList<PackageGraphEdgeRoute> BuildRoutesForTests(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            PackageGraphFocus focus)
        {
            return PackageGraphEdgeRoutePlanner.BuildRoutes(graph, nodeRects, null, focus);
        }

        internal static IReadOnlyList<PackageGraphEdgeRoute> BuildRoutesForTests(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            PackageGraphFocus focus)
        {
            return PackageGraphEdgeRoutePlanner.BuildRoutes(graph, nodeRects, groupRects, focus);
        }

        internal static IReadOnlyList<PackageGraphEdgeRoute> BuildRoutesWithCacheForTests(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            PackageGraphFocus focus,
            PackageGraphEdgeRouteCache routeCache,
            out PackageGraphEdgeRouteBuildDiagnostics diagnostics)
        {
            diagnostics = new PackageGraphEdgeRouteBuildDiagnostics();
            long geometryStartTicks = Stopwatch.GetTimestamp();
            string layoutSignature = PackageGraphRouteCacheIdentity.BuildRouteLayoutSignature(nodeRects, groupRects);
            diagnostics.AddGeometryLayoutReadTicks(Stopwatch.GetTimestamp() - geometryStartTicks);
            IReadOnlyList<PackageGraphEdgeRoute> routes = PackageGraphEdgeRoutePlanner.BuildRoutes(
                graph,
                nodeRects,
                groupRects,
                focus,
                routeCache,
                layoutSignature,
                ref diagnostics);
            diagnostics.RouteCount = routes.Count;
            return routes;
        }

        internal static bool UsesDirectionalFlowMarkersForTests(PackageGraphEdgeKind kind)
        {
            return PackageGraphEdgeAppearance.SupportsDirectionalFlowMarkers(kind);
        }

        internal static bool AnimatesEdgeForTests(PackageGraphEdgeKind kind)
        {
            return PackageGraphEdgeAppearance.ShouldAnimate(kind);
        }

        internal static bool UsesTwoPassStrokeForTests(PackageGraphEdgeKind kind)
        {
            return PackageGraphEdgeAppearance.UsesTwoPassStroke(kind);
        }

        internal static Color ResolveEdgeStatusColorForTests(
            PackageGraphModel graph,
            PackageGraphEdge edge,
            bool emphasized = true,
            bool focusMode = true)
        {
            return PackageGraphEdgeAppearance.GetEdgeColor(graph, edge, emphasized, focusMode);
        }

        internal static bool RouteCrossesNodeInteriorForTests(
            PackageGraphEdgeRoute route,
            IReadOnlyDictionary<string, Rect> nodeRects)
        {
            if (route.Points == null || route.Points.Count < 2 || nodeRects == null || route.Edge == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, Rect> nodeRect in nodeRects)
            {
                if (string.Equals(nodeRect.Key, route.Bundle.SourcePackageId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(nodeRect.Key, route.Bundle.TargetPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Rect interior = PackageGraphRouteGeometry.ShrinkRect(nodeRect.Value, 2f);

                for (int index = 0; index < route.Points.Count - 1; index++)
                {
                    if (PackageGraphRouteGeometry.LineIntersectsRectInterior(route.Points[index], route.Points[index + 1], interior))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool RouteCrossesGraphObstacleForTests(
            PackageGraphEdgeRoute route,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            if (route.Points == null || route.Points.Count < 2)
            {
                return false;
            }

            IReadOnlyList<PackageGraphRouteObstacle> obstacles =
                PackageGraphRouteObstacles.BuildRouteObstacles(route.Bundle, nodeRects, groupRects);
            return !PackageGraphRouteObstacles.IsRoutePathValid(route.Points, obstacles);
        }

        internal static float RouteLengthForTests(PackageGraphEdgeRoute route)
        {
            return route.Points == null ? 0f : PackageGraphRouteGeometry.GetRouteLength(route.Points);
        }

        internal static float DirectRouteDistanceForTests(PackageGraphEdgeRoute route)
        {
            return route.Points == null || route.Points.Count < 2
                ? 0f
                : Vector2.Distance(route.Points[0], route.Points[route.Points.Count - 1]);
        }

    }
}
