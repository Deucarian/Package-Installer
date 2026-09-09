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
    internal static class PackageGraphEdgeRoutePlanner
    {
        internal static IReadOnlyList<PackageGraphEdgeRoute> BuildRoutes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            PackageGraphFocus focus)
        {
            PackageGraphEdgeRouteBuildDiagnostics diagnostics = new PackageGraphEdgeRouteBuildDiagnostics();
            return BuildRoutes(graph, nodeRects, groupRects, focus, null, string.Empty, ref diagnostics);
        }

        internal static IReadOnlyList<PackageGraphEdgeRoute> BuildRoutes(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            PackageGraphFocus focus,
            PackageGraphEdgeRouteCache routeCache,
            string layoutSignature,
            ref PackageGraphEdgeRouteBuildDiagnostics diagnostics)
        {
            if (graph == null || nodeRects == null || graph.Edges.Count == 0)
            {
                return Array.Empty<PackageGraphEdgeRoute>();
            }

            PackageGraphFocus safeFocus = focus ?? PackageGraphFocus.Create(graph, string.Empty);
            string focusGraphSignature = PackageGraphRouteCacheIdentity.BuildRouteFocusGraphSignature(graph, safeFocus);
            long routeCalculationStartTicks = Stopwatch.GetTimestamp();
            List<PackageGraphEdge> visibleEdges = new List<PackageGraphEdge>();

            foreach (PackageGraphEdge edge in graph.Edges)
            {
                if (edge != null &&
                    safeFocus.IsEdgeVisible(edge) &&
                    nodeRects.ContainsKey(edge.FromPackageId) &&
                    nodeRects.ContainsKey(edge.ToPackageId))
                {
                    visibleEdges.Add(edge);
                }
            }

            if (visibleEdges.Count == 0)
            {
                diagnostics.AddRouteCalculationTicks(Stopwatch.GetTimestamp() - routeCalculationStartTicks);
                return Array.Empty<PackageGraphEdgeRoute>();
            }

            IReadOnlyList<PackageGraphRouteObstacle> obstacles = PackageGraphRouteObstacles.BuildRouteObstacles(nodeRects, groupRects);
            List<PackageGraphEdgeRouteContext> visibleBundles = BuildConnectionBundleContexts(
                graph,
                visibleEdges,
                nodeRects,
                safeFocus);
            List<PackageGraphEdgeRoute> routes = new List<PackageGraphEdgeRoute>(visibleBundles.Count);
            HashSet<string> routedEdgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            diagnostics.AddRouteCalculationTicks(Stopwatch.GetTimestamp() - routeCalculationStartTicks);

            if (safeFocus.HasFocus &&
                !string.IsNullOrWhiteSpace(safeFocus.FocusPackageId) &&
                nodeRects.TryGetValue(safeFocus.FocusPackageId, out Rect focusRect))
            {
                Dictionary<string, List<PackageGraphEdgeRouteContext>> fanOutGroups =
                    new Dictionary<string, List<PackageGraphEdgeRouteContext>>(StringComparer.OrdinalIgnoreCase);

                foreach (PackageGraphEdgeRouteContext context in visibleBundles)
                {
                    if (context.Zone == PackageGraphEdgeRouteZone.Direct)
                    {
                        continue;
                    }

                    string groupKey = GetRouteGroupKey(context.Bundle, context.Zone, safeFocus.FocusPackageId);

                    if (!fanOutGroups.TryGetValue(groupKey, out List<PackageGraphEdgeRouteContext> contexts))
                    {
                        contexts = new List<PackageGraphEdgeRouteContext>();
                        fanOutGroups[groupKey] = contexts;
                    }

                    contexts.Add(context);
                }

                foreach (KeyValuePair<string, List<PackageGraphEdgeRouteContext>> group in fanOutGroups)
                {
                    List<PackageGraphEdgeRouteContext> contexts = group.Value;
                    if (contexts.Count > 1)
                    {
                        contexts.Sort((first, second) => CompareRouteContexts(first, second, graph, safeFocus.FocusPackageId));

                        for (int index = 0; index < contexts.Count; index++)
                        {
                            routes.Add(PackageGraphEdgeRouteFactory.CreateFanOutRoute(
                                contexts[index],
                                focusRect,
                                safeFocus.FocusPackageId,
                                group.Key,
                                index,
                                contexts.Count,
                                nodeRects,
                                groupRects,
                                obstacles,
                                routeCache,
                                layoutSignature,
                                focusGraphSignature,
                                ref diagnostics));
                            routedEdgeKeys.Add(contexts[index].Bundle.Key);
                        }
                    }
                }
            }

            foreach (PackageGraphEdgeRouteContext context in visibleBundles)
            {
                if (routedEdgeKeys.Contains(context.Bundle.Key))
                {
                    continue;
                }

                routes.Add(PackageGraphEdgeRouteFactory.CreateDirectRoute(
                    context,
                    safeFocus.FocusPackageId,
                    nodeRects,
                    groupRects,
                    obstacles,
                    routeCache,
                    layoutSignature,
                    focusGraphSignature,
                    ref diagnostics));
            }

            return routes;
        }

        internal static List<PackageGraphEdgeRouteContext> BuildConnectionBundleContexts(
            PackageGraphModel graph,
            IReadOnlyList<PackageGraphEdge> visibleEdges,
            IReadOnlyDictionary<string, Rect> nodeRects,
            PackageGraphFocus focus)
        {
            List<PackageGraphEdgeRouteContext> contexts = new List<PackageGraphEdgeRouteContext>();
            Dictionary<string, List<PackageGraphEdge>> groupedEdges =
                new Dictionary<string, List<PackageGraphEdge>>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphEdge edge in visibleEdges)
            {
                string bundleKey = GetConnectionBundleKey(edge);

                if (!groupedEdges.TryGetValue(bundleKey, out List<PackageGraphEdge> edges))
                {
                    edges = new List<PackageGraphEdge>();
                    groupedEdges[bundleKey] = edges;
                }

                edges.Add(edge);
            }

            foreach (List<PackageGraphEdge> group in groupedEdges.Values)
            {
                PackageGraphConnectionBundle bundle = CreateConnectionBundle(group);

                if (!nodeRects.TryGetValue(bundle.SourcePackageId, out Rect fromRect) ||
                    !nodeRects.TryGetValue(bundle.TargetPackageId, out Rect toRect))
                {
                    continue;
                }

                PackageGraphEdgeRouteZone zone = focus != null &&
                                                 focus.HasFocus &&
                                                 bundle.ConnectsPackage(focus.FocusPackageId)
                    ? GetFocusRouteZone(graph, bundle, focus.FocusPackageId)
                    : PackageGraphEdgeRouteZone.Direct;
                contexts.Add(new PackageGraphEdgeRouteContext(bundle, fromRect, toRect, zone));
            }

            return contexts;
        }

        internal static int CompareRouteContexts(
            PackageGraphEdgeRouteContext first,
            PackageGraphEdgeRouteContext second,
            PackageGraphModel graph,
            string focusPackageId)
        {
            int comparison = GetRouteSortValue(first, focusPackageId)
                .CompareTo(GetRouteSortValue(second, focusPackageId));

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = string.Compare(
                GetOtherNodeDisplayName(graph, first.Bundle, focusPackageId),
                GetOtherNodeDisplayName(graph, second.Bundle, focusPackageId),
                StringComparison.OrdinalIgnoreCase);

            return comparison != 0
                ? comparison
                : string.Compare(first.Bundle.Key, second.Bundle.Key, StringComparison.OrdinalIgnoreCase);
        }

        internal static string GetConnectionBundleKey(PackageGraphEdge edge)
        {
            if (edge == null)
            {
                return string.Empty;
            }

            if (edge.Kind != PackageGraphEdgeKind.HardDependency &&
                edge.Kind != PackageGraphEdgeKind.IntegrationConnection)
            {
                return edge.Key;
            }

            string first = edge.FromPackageId ?? string.Empty;
            string second = edge.ToPackageId ?? string.Empty;

            if (string.Compare(first, second, StringComparison.OrdinalIgnoreCase) > 0)
            {
                string temp = first;
                first = second;
                second = temp;
            }

            return "relationship:" + first + "<>" + second;
        }

        internal static PackageGraphConnectionBundle CreateConnectionBundle(IEnumerable<PackageGraphEdge> edges)
        {
            PackageGraphEdge[] safeEdges = (edges ?? Array.Empty<PackageGraphEdge>())
                .Where(edge => edge != null)
                .OrderBy(edge => PackageGraphConnectionBundle.GetSemanticPriority(edge.Kind))
                .ThenBy(edge => edge.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PackageGraphEdge dependencyEdge = safeEdges.FirstOrDefault(edge => edge.Kind == PackageGraphEdgeKind.HardDependency);
            PackageGraphEdge primaryEdge = dependencyEdge ?? safeEdges.FirstOrDefault();

            return primaryEdge == null
                ? new PackageGraphConnectionBundle(string.Empty, string.Empty, Array.Empty<PackageGraphEdge>())
                : new PackageGraphConnectionBundle(primaryEdge.FromPackageId, primaryEdge.ToPackageId, safeEdges);
        }

        internal static PackageGraphEdgeRouteZone GetFocusRouteZone(
            PackageGraphModel graph,
            PackageGraphConnectionBundle bundle,
            string focusPackageId)
        {
            if (string.IsNullOrWhiteSpace(focusPackageId))
            {
                return PackageGraphEdgeRouteZone.Direct;
            }

            if (bundle.IsCompositeDependencyIntegration &&
                string.Equals(bundle.SourcePackageId, focusPackageId, StringComparison.OrdinalIgnoreCase) &&
                graph != null &&
                graph.TryGetNode(bundle.TargetPackageId, out PackageGraphNode targetNode) &&
                targetNode.IsIntegration)
            {
                return PackageGraphEdgeRouteZone.Integrations;
            }

            if (bundle.HasDependency)
            {
                if (string.Equals(bundle.TargetPackageId, focusPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    return PackageGraphEdgeRouteZone.Providers;
                }

                if (string.Equals(bundle.SourcePackageId, focusPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    return PackageGraphEdgeRouteZone.Dependents;
                }
            }

            if (bundle.HasIntegration)
            {
                return PackageGraphEdgeRouteZone.Integrations;
            }

            if (bundle.HasOptionalCompanion ||
                bundle.HasRecommended ||
                bundle.HasSuiteMembership)
            {
                return PackageGraphEdgeRouteZone.CompanionsAndSuites;
            }

            return PackageGraphEdgeRouteZone.Direct;
        }

        internal static string GetRouteGroupKey(
            PackageGraphConnectionBundle bundle,
            PackageGraphEdgeRouteZone zone,
            string focusPackageId)
        {
            return bundle.Semantics + ":" + zone + ":" + (focusPackageId ?? string.Empty);
        }

        internal static float GetRouteSortValue(
            PackageGraphEdgeRouteContext context,
            string focusPackageId)
        {
            bool focusIsSource = string.Equals(
                context.Bundle.SourcePackageId,
                focusPackageId,
                StringComparison.OrdinalIgnoreCase);
            Rect otherRect = focusIsSource ? context.ToRect : context.FromRect;

            switch (context.Zone)
            {
                case PackageGraphEdgeRouteZone.Providers:
                case PackageGraphEdgeRouteZone.Dependents:
                    return otherRect.center.y;
                case PackageGraphEdgeRouteZone.Integrations:
                case PackageGraphEdgeRouteZone.CompanionsAndSuites:
                    return otherRect.center.x;
                default:
                    return otherRect.center.x + otherRect.center.y;
            }
        }

        internal static string GetOtherNodeDisplayName(
            PackageGraphModel graph,
            PackageGraphConnectionBundle bundle,
            string focusPackageId)
        {
            string otherPackageId = bundle.GetOtherPackageId(focusPackageId);
            return graph != null &&
                   !string.IsNullOrWhiteSpace(otherPackageId) &&
                   graph.TryGetNode(otherPackageId, out PackageGraphNode node)
                ? node.DisplayName
                : otherPackageId ?? string.Empty;
        }
    }
}
