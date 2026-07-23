using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageGraphBundledCatalogLayoutTests
    {
        private const float ExpectedFocusGap = 72f;
        private const float ExpectedCategoryGap = 48f;
        private const float Tolerance = 0.1f;

        [Test]
        public void BundledCatalog_EveryPackageFocusHasNonOverlappingMeasuredBounds()
        {
            PackageGraphModel graph = CreateBundledGraph();
            PackageGraphLayout layoutEngine = new PackageGraphLayout();

            Assert.AreEqual(48, graph.Nodes.Count, "The bundled catalog coverage changed; audit the new focus states.");

            foreach (PackageGraphNode focusNode in graph.Nodes)
            {
                PackageGraphLayoutResult layout = layoutEngine.Calculate(
                    graph,
                    PackageGraphLayoutMode.Focus,
                    focusNode.PackageId,
                    string.Empty,
                    Vector2.zero,
                    PackageGraphNodePresentationLevel.Full);
                Rect[] visibleBounds = layout.NodeRects.Values
                    .Concat(layout.GroupNodes.Select(groupNode => groupNode.Rect))
                    .Concat(layout.OverflowSummaries.Select(summary => summary.Rect))
                    .ToArray();

                AssertNoOverlaps(visibleBounds, focusNode.PackageId);
                AssertAllBoundsInsideCanvas(visibleBounds, focusNode.PackageId);
            }
        }

        [Test]
        public void BundledCatalog_EveryCategoryFocusHasNonOverlappingMeasuredBounds()
        {
            PackageGraphModel graph = CreateBundledGraph();
            PackageGraphLayout layoutEngine = new PackageGraphLayout();

            Assert.AreEqual(15, graph.Groups.Count, "The bundled category coverage changed; audit the new category states.");

            foreach (PackageGraphGroup group in graph.Groups)
            {
                PackageGraphLayoutResult layout = layoutEngine.Calculate(
                    graph,
                    PackageGraphLayoutMode.GroupFocus,
                    string.Empty,
                    group.Id,
                    Vector2.zero,
                    PackageGraphNodePresentationLevel.Compact);
                Rect[] visibleBounds = layout.NodeRects.Values
                    .Concat(layout.GroupNodes.Select(groupNode => groupNode.Rect))
                    .ToArray();

                AssertNoOverlaps(visibleBounds, group.Id);
                AssertAllBoundsInsideCanvas(visibleBounds, group.Id);
            }
        }

        [Test]
        public void BundledCatalog_EveryFocusRouteUsesOnlyOrthogonalSegments()
        {
            PackageGraphModel graph = CreateBundledGraph();
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            foreach (PackageGraphNode focusNode in graph.Nodes)
            {
                canvas.SetGraph(
                    graph,
                    focusNode.PackageId,
                    focusNode.PackageId,
                    actionsEnabled: true);

                foreach (PackageGraphStructuralMembershipRoute route in canvas.StructuralMembershipRoutesForTests)
                {
                    foreach (PackageGraphStructuralMembershipSegment segment in route.Segments)
                    {
                        AssertOrthogonal(
                            segment.From,
                            segment.To,
                            focusNode.PackageId + " structural membership " + route.GroupId);
                    }
                }

                foreach (PackageGraphEdgeRoute route in canvas.EdgeRoutesForTests)
                {
                    for (int index = 1; index < route.Points.Count; index++)
                    {
                        AssertOrthogonal(
                            route.Points[index - 1],
                            route.Points[index],
                            focusNode.PackageId + " relationship " + route.Bundle.Semantics);
                    }
                }
            }
        }

        [Test]
        public void SessionFocus_UsesConsistentEdgeClearanceWithoutDenseLayoutPadding()
        {
            PackageGraphModel graph = CreateBundledGraph();
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.session",
                string.Empty,
                Vector2.zero,
                PackageGraphNodePresentationLevel.Full);
            Rect session = layout.NodeRects["com.deucarian.session"];
            Rect sessionApiIntegration = layout.NodeRects["com.deucarian.session.api-integration"];
            PackageGraphGroupLayoutNode runtimeServices = layout.GroupNodes.Single(groupNode =>
                groupNode.GroupId == "runtime-services" &&
                groupNode.RepresentedPackageIds.Contains("com.deucarian.session", StringComparer.OrdinalIgnoreCase));
            Assert.That(session.yMin - runtimeServices.Rect.yMax, Is.EqualTo(ExpectedCategoryGap).Within(Tolerance));
            Assert.That(sessionApiIntegration.yMin - session.yMax, Is.EqualTo(ExpectedFocusGap).Within(Tolerance));
        }

        private static PackageGraphModel CreateBundledGraph()
        {
            PackageRegistryLoadResult result = new PackageRegistryLoader().LoadBundled();
            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.IsNotNull(result.Registry);
            IReadOnlyList<PackageDefinition> packages = PackageRegistryProvider.CreatePackageDefinitions(result.Registry);
            IReadOnlyList<PackageGraphGroup> groups = PackageGraphHierarchyBuilder.CreateGroups(result.Registry.groups);
            return new PackageGraphBuilder(_ => false).Build(packages, groups);
        }

        private static void AssertNoOverlaps(IReadOnlyList<Rect> rects, string context)
        {
            for (int left = 0; left < rects.Count; left++)
            {
                for (int right = left + 1; right < rects.Count; right++)
                {
                    Assert.IsFalse(
                        rects[left].Overlaps(rects[right]),
                        context + " contains overlapping bounds: " + rects[left] + " and " + rects[right]);
                }
            }
        }

        private static void AssertAllBoundsInsideCanvas(IEnumerable<Rect> rects, string context)
        {
            foreach (Rect rect in rects)
            {
                Assert.GreaterOrEqual(rect.xMin, -Tolerance, context + " extends beyond the left canvas edge.");
                Assert.GreaterOrEqual(rect.yMin, -Tolerance, context + " extends beyond the top canvas edge.");
                Assert.LessOrEqual(rect.xMax, PackageGraphLayout.CanvasWidth + Tolerance,
                    context + " extends beyond the right canvas edge.");
                Assert.LessOrEqual(rect.yMax, PackageGraphLayout.CanvasHeight + Tolerance,
                    context + " extends beyond the bottom canvas edge.");
            }
        }

        private static void AssertOrthogonal(Vector2 from, Vector2 to, string context)
        {
            bool vertical = Mathf.Abs(from.x - to.x) <= Tolerance;
            bool horizontal = Mathf.Abs(from.y - to.y) <= Tolerance;
            Assert.IsTrue(
                vertical || horizontal,
                context + " contains a diagonal segment from " + from + " to " + to + ".");
        }
    }
}
