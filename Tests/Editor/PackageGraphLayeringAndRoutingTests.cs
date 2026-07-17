using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageGraphLayeringAndRoutingTests
    {
        private const string GroupId = "infrastructure";
        private const string PackageId = "com.example.editor";
        private const string ParentGroupId = "experience";

        [Test]
        public void Canvas_OrdersConnectionOcclusionsBetweenConnectorsAndInteractiveContent()
        {
            PackageGraphCanvas canvas = CreateFocusedCanvas();
            VisualElement membershipLayer = canvas.Q<VisualElement>("ecosystem-graph-membership-layer");
            VisualElement edgeLayer = canvas.Q<VisualElement>("ecosystem-graph-edge-layer");
            VisualElement occlusionLayer = canvas.Q<VisualElement>("ecosystem-graph-occlusion-layer");
            VisualElement nodeLayer = canvas.Q<VisualElement>("ecosystem-graph-node-layer");

            Assert.NotNull(membershipLayer);
            Assert.NotNull(edgeLayer);
            Assert.NotNull(occlusionLayer);
            Assert.NotNull(nodeLayer);
            Assert.Less(canvas.IndexOf(membershipLayer), canvas.IndexOf(occlusionLayer));
            Assert.Less(canvas.IndexOf(edgeLayer), canvas.IndexOf(occlusionLayer));
            Assert.Less(canvas.IndexOf(occlusionLayer), canvas.IndexOf(nodeLayer));
            Assert.AreEqual(PickingMode.Ignore, occlusionLayer.pickingMode);
        }

        [Test]
        public void EdgeLayer_HasNoPersistentIdleAnimationSchedule()
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            Assert.IsFalse(
                typeof(PackageGraphEdgeLayer)
                    .GetFields(flags)
                    .Any(field => field.FieldType == typeof(IVisualElementScheduledItem)));
            Assert.IsNull(typeof(PackageGraphEdgeLayer).GetMethod("UpdateAnimation", flags));
        }

        [Test]
        public void Canvas_CreatesSizedOcclusionMasksForPackageAndCategorySymbol()
        {
            PackageGraphCanvas canvas = CreateFocusedCanvas();
            VisualElement packageMask = canvas.Q<VisualElement>("package-occlusion-" + PackageId);
            VisualElement groupMask = canvas.Q<VisualElement>("group-symbol-occlusion-" + GroupId);
            VisualElement rootMask = canvas.Q<VisualElement>("ecosystem-graph-root-hub-occlusion");
            Rect packageRect = canvas.NodeRectsForTests[PackageId];
            PackageGraphGroupLayoutNode group = canvas.GroupLayoutNodesForTests.Single(candidate =>
                string.Equals(candidate.GroupId, GroupId, StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(packageMask);
            Assert.NotNull(groupMask);
            Assert.NotNull(rootMask);
            Assert.IsTrue(packageMask.ClassListContains("dpi-graph-occlusion--package"));
            Assert.IsTrue(groupMask.ClassListContains("dpi-graph-occlusion--group-symbol"));
            Assert.IsTrue(rootMask.ClassListContains("dpi-graph-occlusion--hub"));
            Assert.AreEqual(PickingMode.Ignore, packageMask.pickingMode);
            Assert.AreEqual(PickingMode.Ignore, groupMask.pickingMode);
            AssertStyleRect(packageMask, packageRect);
            AssertStyleRect(groupMask, group.HubRect);
        }

        [Test]
        public void FocusedRootHub_OcclusionMaskUsesTheSameFadedOpacity()
        {
            PackageGraphCanvas canvas = CreateFocusedCanvas();
            VisualElement hub = canvas.Q<VisualElement>(className: "dpi-graph-hub");
            VisualElement mask = canvas.Q<VisualElement>("ecosystem-graph-root-hub-occlusion");

            Assert.NotNull(hub);
            Assert.NotNull(mask);
            Assert.That(hub.style.opacity.value, Is.EqualTo(0.52f).Within(0.001f));
            Assert.That(mask.style.opacity.value, Is.EqualTo(hub.style.opacity.value).Within(0.001f));
        }

        [Test]
        public void SearchDimmedPackageAndGroup_OcclusionMasksUseTheSameOpacity()
        {
            PackageGraphCanvas canvas = CreateFocusedCanvas();
            VisualElement package = canvas.Q<VisualElement>(PackageId);
            VisualElement packageMask = canvas.Q<VisualElement>("package-occlusion-" + PackageId);
            VisualElement group = canvas.Q<VisualElement>("group-" + GroupId);
            VisualElement groupMask = canvas.Q<VisualElement>("group-symbol-occlusion-" + GroupId);

            ApplySearchClasses(package, searchActive: true);
            ApplySearchClasses(group, searchActive: true);
            SynchronizeOcclusions(canvas);

            Assert.That(package.style.opacity.value, Is.EqualTo(0.24f).Within(0.001f));
            Assert.That(packageMask.style.opacity.value, Is.EqualTo(package.style.opacity.value).Within(0.001f));
            Assert.That(group.style.opacity.value, Is.EqualTo(0.22f).Within(0.001f));
            Assert.That(groupMask.style.opacity.value, Is.EqualTo(group.style.opacity.value).Within(0.001f));
        }

        [Test]
        public void HoverDimmedPackageAndGroup_OcclusionMasksUseTheSameOpacity()
        {
            PackageGraphCanvas canvas = CreateFocusedCanvas();
            VisualElement package = canvas.Q<VisualElement>(PackageId);
            VisualElement packageMask = canvas.Q<VisualElement>("package-occlusion-" + PackageId);
            VisualElement group = canvas.Q<VisualElement>("group-" + GroupId);
            VisualElement groupMask = canvas.Q<VisualElement>("group-symbol-occlusion-" + GroupId);

            package.EnableInClassList("dpi-graph-node--hover-dimmed", true);
            group.EnableInClassList("dpi-graph-group--hover-dimmed", true);
            SynchronizeOcclusions(canvas);

            Assert.That(package.style.opacity.value, Is.EqualTo(0.54f).Within(0.001f));
            Assert.That(packageMask.style.opacity.value, Is.EqualTo(package.style.opacity.value).Within(0.001f));
            Assert.That(group.style.opacity.value, Is.EqualTo(0.50f).Within(0.001f));
            Assert.That(groupMask.style.opacity.value, Is.EqualTo(group.style.opacity.value).Within(0.001f));
        }

        [Test]
        public void EnteringPackage_OcclusionMaskFadesAndScalesWithTheCard()
        {
            PackageGraphCanvas canvas = CreateFocusedCanvas();
            const string enteringPackageId = "com.example.entering";
            PackageDefinition enteringPackage = new PackageDefinition(
                "Entering Package",
                enteringPackageId,
                "https://example.invalid/entering.git",
                "Package added during the layout transition.",
                category: "Editor",
                ecosystemGroup: "Infrastructure",
                groupId: GroupId,
                dependencies: new[] { PackageId });
            PackageDefinition originalPackage = new PackageDefinition(
                "Example Editor",
                PackageId,
                "https://example.invalid/editor.git",
                "Editor package used by graph layout tests.",
                category: "Editor",
                ecosystemGroup: "Infrastructure",
                groupId: GroupId);
            PackageGraphGroup group = CreateGroup();
            PackageGraphModel expandedGraph = new PackageGraphBuilder(_ => true).Build(
                new[] { originalPackage, enteringPackage },
                new[] { group });

            canvas.SetGraph(expandedGraph, PackageId, PackageId, actionsEnabled: true);

            VisualElement package = canvas.Q<VisualElement>(enteringPackageId);
            VisualElement mask = canvas.Q<VisualElement>("package-occlusion-" + enteringPackageId);

            Assert.NotNull(package);
            Assert.NotNull(mask);
            Assert.That(package.style.opacity.value, Is.LessThan(1f));
            Assert.That(mask.style.opacity.value, Is.EqualTo(package.style.opacity.value).Within(0.001f));
            Assert.That(mask.style.scale.value.value.x, Is.EqualTo(package.style.scale.value.value.x).Within(0.001f));
            Assert.That(mask.style.scale.value.value.y, Is.EqualTo(package.style.scale.value.value.y).Within(0.001f));
        }

        [Test]
        public void FocusedOwningCategory_UsesOneDirectVerticalMembershipSegment()
        {
            PackageGraphCanvas canvas = CreateFocusedCanvas();
            PackageGraphStructuralMembershipRoute route = canvas.StructuralMembershipRoutesForTests.Single(candidate =>
                string.Equals(candidate.GroupId, GroupId, StringComparison.OrdinalIgnoreCase));
            PackageGraphStructuralMembershipSegment segment = route.Segments.Single();
            Rect packageRect = canvas.NodeRectsForTests[PackageId];
            PackageGraphGroupLayoutNode group = canvas.GroupLayoutNodesForTests.Single(candidate =>
                string.Equals(candidate.GroupId, GroupId, StringComparison.OrdinalIgnoreCase));

            Assert.IsFalse(route.UsesBus);
            CollectionAssert.AreEqual(new[] { PackageId }, route.PackageIds);
            CollectionAssert.AreEqual(new[] { PackageId }, segment.PackageIds);
            Assert.That(segment.From.x, Is.EqualTo(group.HubRect.center.x).Within(0.01f));
            Assert.That(segment.To.x, Is.EqualTo(packageRect.center.x).Within(0.01f));
            Assert.That(segment.From.x, Is.EqualTo(segment.To.x).Within(0.01f));
            Assert.Less(segment.From.y, segment.To.y);
            Assert.That(segment.To.y, Is.EqualTo(packageRect.yMin + 2f).Within(0.05f));
        }

        [Test]
        public void FocusedOwningCategory_RoutesContinuouslyAroundCaptionBounds()
        {
            PackageGraphCanvas canvas = CreateFocusedCanvas();
            PackageGraphMembershipLayer membershipLayer =
                canvas.Q<PackageGraphMembershipLayer>("ecosystem-graph-membership-layer");
            PackageGraphStructuralMembershipSegment segment = canvas.StructuralMembershipRoutesForTests
                .Single(candidate => string.Equals(candidate.GroupId, GroupId, StringComparison.OrdinalIgnoreCase))
                .Segments.Single();
            MethodInfo captionMethod = typeof(PackageGraphMembershipLayer).GetMethod(
                "GetGroupCaptionOcclusionRect",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);

            Assert.NotNull(membershipLayer);
            Assert.NotNull(captionMethod);

            Rect captionRect = (Rect)captionMethod.Invoke(membershipLayer, new object[] { GroupId });
            IReadOnlyList<Vector2> path = PackageGraphMembershipLayer.BuildCaptionAvoidingPathForTests(
                segment.From,
                segment.To,
                captionRect);

            Assert.That(captionRect.width, Is.GreaterThan(0f));
            Assert.That(captionRect.height, Is.GreaterThan(0f));
            Assert.That(path.Count, Is.GreaterThan(2));
            Assert.That(Vector2.Distance(path[0], segment.From), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(path[path.Count - 1], segment.To), Is.LessThan(0.01f));

            for (int index = 0; index < path.Count - 1; index++)
            {
                Assert.IsFalse(SegmentIntersectsRect(
                    new PackageGraphStructuralMembershipSegment(path[index], path[index + 1]),
                    captionRect));
            }
        }

        [Test]
        public void CaptionAvoidingPath_LeavesUnobstructedSegmentStraight()
        {
            Vector2 from = new Vector2(0f, 0f);
            Vector2 to = new Vector2(0f, 100f);
            IReadOnlyList<Vector2> path = PackageGraphMembershipLayer.BuildCaptionAvoidingPathForTests(
                from,
                to,
                new Rect(20f, 30f, 20f, 40f));

            Assert.AreEqual(2, path.Count);
            Assert.AreEqual(from, path[0]);
            Assert.AreEqual(to, path[1]);
        }

        [Test]
        public void NestedGroupOrbit_UsesShortTangentAttachmentCaps()
        {
            PackageGraphCanvas canvas = CreateNestedGroupCanvas();
            PackageGraphGroupLayoutNode parent = canvas.GroupLayoutNodesForTests.Single(candidate =>
                string.Equals(candidate.GroupId, ParentGroupId, StringComparison.OrdinalIgnoreCase));
            PackageGraphGroupLayoutNode[] children = canvas.GroupLayoutNodesForTests
                .Where(candidate =>
                    candidate.Group != null &&
                    string.Equals(candidate.Group.ParentGroupId, ParentGroupId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.Group.SortOrder)
                .ToArray();
            float orbitRadius = canvas.AnimatedGroupOrbitRadiiForTests[ParentGroupId];

            Assert.AreEqual(3, children.Length);
            Assert.That(orbitRadius, Is.GreaterThan(0f));

            foreach (PackageGraphGroupLayoutNode child in children)
            {
                IReadOnlyList<PackageGraphStructuralMembershipSegment> caps =
                    PackageGraphMembershipLayer.CreateOrbitAttachmentCapsForTests(
                        parent.HubCenter,
                        orbitRadius,
                        child.HubRect);
                Vector2 radial = (child.HubCenter - parent.HubCenter).normalized;
                Vector2 tangent = new Vector2(-radial.y, radial.x);

                Assert.AreEqual(2, caps.Count, child.GroupId);
                foreach (PackageGraphStructuralMembershipSegment cap in caps)
                {
                    Assert.That(cap.Length, Is.EqualTo(10f).Within(0.05f), child.GroupId);
                    Assert.That(
                        Mathf.Abs(Vector2.Dot((cap.To - cap.From).normalized, tangent)),
                        Is.GreaterThan(0.99f),
                        child.GroupId);
                    Assert.IsFalse(SegmentIntersectsRect(cap, child.HubRect), child.GroupId);
                }
            }
        }

        [Test]
        public void OrbitAttachmentCaps_AreStableForAnimatedRadialPositions()
        {
            Vector2 center = new Vector2(400f, 300f);
            float[] radii = { 96f, 180f, 260f };

            foreach (float radius in radii)
            {
                Rect card = new Rect(center.x + radius - 60f, center.y - 32f, 120f, 64f);
                IReadOnlyList<PackageGraphStructuralMembershipSegment> caps =
                    PackageGraphMembershipLayer.CreateOrbitAttachmentCapsForTests(center, radius, card);

                Assert.AreEqual(2, caps.Count);
                Assert.IsTrue(caps.All(cap => cap.Length > 9.9f && cap.Length < 10.1f));
            }
        }

        [Test]
        public void OverviewToGroupFocus_OrbitAttachmentsRemainContinuousThroughoutTransition()
        {
            PackageGraphModel graph = CreateNestedGroupGraph();
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                visiblePackageIds: null);
            canvas.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                ParentGroupId,
                actionsEnabled: true,
                visiblePackageIds: null);

            PackageGraphGroupLayoutNode[] children = canvas.GroupLayoutNodesForTests
                .Where(candidate =>
                    candidate.Group != null &&
                    string.Equals(candidate.Group.ParentGroupId, ParentGroupId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.Group.SortOrder)
                .ToArray();
            float[] samples = { 0.05f, 0.1f, 0.25f, 0.5f, 0.75f, 0.9f, 1f };

            Assert.AreEqual(3, children.Length);

            foreach (float progress in samples)
            {
                canvas.EvaluateLayoutTransitionForTests(progress);
                Vector2 parentCenter = canvas.AnimatedGroupCentersForTests[ParentGroupId];
                float parentRadius = canvas.AnimatedGroupOrbitRadiiForTests[ParentGroupId];

                Assert.IsTrue(IsFinite(parentCenter), $"Parent center at {progress:0.00}");
                Assert.That(parentRadius, Is.GreaterThan(0f), $"Parent radius at {progress:0.00}");

                foreach (PackageGraphGroupLayoutNode child in children)
                {
                    Rect childHubRect = GetAnimatedHubRect(
                        child,
                        canvas.AnimatedGroupRectsForTests[child.GroupId]);
                    IReadOnlyList<PackageGraphStructuralMembershipSegment> caps =
                        PackageGraphMembershipLayer.CreateOrbitAttachmentCapsForTests(
                            parentCenter,
                            parentRadius,
                            childHubRect);

                    Assert.AreEqual(2, caps.Count, $"{child.GroupId} at {progress:0.00}");
                    foreach (PackageGraphStructuralMembershipSegment cap in caps)
                    {
                        Assert.IsTrue(IsFinite(cap.From), $"{child.GroupId} start at {progress:0.00}");
                        Assert.IsTrue(IsFinite(cap.To), $"{child.GroupId} end at {progress:0.00}");
                        Assert.That(
                            cap.Length,
                            Is.EqualTo(10f).Within(0.05f),
                            $"{child.GroupId} at {progress:0.00}");
                        Assert.IsFalse(
                            SegmentIntersectsRect(cap, childHubRect),
                            $"{child.GroupId} at {progress:0.00}");
                    }
                }
            }
        }

        private static PackageGraphCanvas CreateFocusedCanvas()
        {
            PackageDefinition package = new PackageDefinition(
                "Example Editor",
                PackageId,
                "https://example.invalid/editor.git",
                "Editor package used by graph layout tests.",
                category: "Editor",
                ecosystemGroup: "Infrastructure",
                groupId: GroupId);
            PackageGraphGroup group = CreateGroup();
            PackageGraphModel graph = new PackageGraphBuilder(_ => true).Build(
                new[] { package },
                new[] { group });
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(graph, PackageId, PackageId, actionsEnabled: true);
            return canvas;
        }

        private static PackageGraphGroup CreateGroup()
        {
            return new PackageGraphGroup(
                GroupId,
                "Infrastructure",
                string.Empty,
                "Shared editor infrastructure.",
                10,
                "editor",
                GroupId);
        }

        private static void ApplySearchClasses(VisualElement element, bool searchActive)
        {
            MethodInfo method = typeof(PackageGraphCanvas).GetMethod(
                "ApplySearchClasses",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);
            method.Invoke(null, new object[] { element, false, false, false, searchActive });
        }

        private static void SynchronizeOcclusions(PackageGraphCanvas canvas)
        {
            MethodInfo method = typeof(PackageGraphCanvas).GetMethod(
                "UpdateConnectionOcclusionLayout",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            method.Invoke(canvas, Array.Empty<object>());
        }

        private static PackageGraphCanvas CreateNestedGroupCanvas()
        {
            PackageGraphModel graph = CreateNestedGroupGraph();
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                ParentGroupId,
                actionsEnabled: true,
                visiblePackageIds: null);
            return canvas;
        }

        private static PackageGraphModel CreateNestedGroupGraph()
        {
            PackageGraphGroup[] groups =
            {
                new PackageGraphGroup(
                    ParentGroupId,
                    "Experience",
                    string.Empty,
                    "Parent group with nested categories.",
                    10,
                    "package",
                    ParentGroupId),
                new PackageGraphGroup(
                    "child-a",
                    "Child A",
                    ParentGroupId,
                    "First nested category.",
                    11,
                    "package",
                    "child-a"),
                new PackageGraphGroup(
                    "child-b",
                    "Child B",
                    ParentGroupId,
                    "Second nested category.",
                    12,
                    "package",
                    "child-b"),
                new PackageGraphGroup(
                    "child-c",
                    "Child C",
                    ParentGroupId,
                    "Third nested category.",
                    13,
                    "package",
                    "child-c")
            };
            PackageDefinition[] packages =
            {
                CreateNestedPackage("Child A Package", "com.example.child-a", "child-a"),
                CreateNestedPackage("Child B Package", "com.example.child-b", "child-b"),
                CreateNestedPackage("Child C Package", "com.example.child-c", "child-c")
            };
            return new PackageGraphBuilder(_ => true).Build(packages, groups);
        }

        private static PackageDefinition CreateNestedPackage(
            string displayName,
            string packageId,
            string groupId)
        {
            return new PackageDefinition(
                displayName,
                packageId,
                "https://example.invalid/" + packageId + ".git",
                "Package used by nested caption routing tests.",
                category: "Nested",
                ecosystemGroup: "Experience",
                groupId: groupId);
        }

        private static bool SegmentIntersectsRect(
            PackageGraphStructuralMembershipSegment segment,
            Rect rect)
        {
            MethodInfo clipMethod = typeof(PackageGraphMembershipLayer).GetMethod(
                "TryGetSegmentRectInterval",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(clipMethod);
            object[] arguments = { segment.From, segment.To, rect, 0f, 0f };
            return (bool)clipMethod.Invoke(null, arguments);
        }

        private static Rect GetAnimatedHubRect(PackageGraphGroupLayoutNode groupNode, Rect animatedGroupRect)
        {
            Vector2 offset = groupNode.HubRect.position - groupNode.Rect.position;
            return new Rect(
                animatedGroupRect.x + offset.x,
                animatedGroupRect.y + offset.y,
                groupNode.HubRect.width,
                groupNode.HubRect.height);
        }

        private static bool IsFinite(Vector2 point)
        {
            return !float.IsNaN(point.x) &&
                   !float.IsInfinity(point.x) &&
                   !float.IsNaN(point.y) &&
                   !float.IsInfinity(point.y);
        }

        private static void AssertStyleRect(VisualElement element, Rect expected)
        {
            Assert.That(element.style.left.value.value, Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(element.style.top.value.value, Is.EqualTo(expected.y).Within(0.01f));
            Assert.That(element.style.width.value.value, Is.EqualTo(expected.width).Within(0.01f));
            Assert.That(element.style.height.value.value, Is.EqualTo(expected.height).Within(0.01f));
        }
    }
}
