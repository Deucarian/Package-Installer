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
        public void FocusedOwningCategory_DirectSegmentIsClippedAcrossCaptionBounds()
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
            MethodInfo clipMethod = typeof(PackageGraphMembershipLayer).GetMethod(
                "TryGetSegmentRectInterval",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(membershipLayer);
            Assert.NotNull(captionMethod);
            Assert.NotNull(clipMethod);

            Rect captionRect = (Rect)captionMethod.Invoke(membershipLayer, new object[] { GroupId });
            object[] clipArguments = { segment.From, segment.To, captionRect, 0f, 0f };
            bool intersects = (bool)clipMethod.Invoke(null, clipArguments);
            float entry = (float)clipArguments[3];
            float exit = (float)clipArguments[4];

            Assert.That(captionRect.width, Is.GreaterThan(0f));
            Assert.That(captionRect.height, Is.GreaterThan(0f));
            Assert.IsTrue(intersects);
            Assert.That(entry, Is.InRange(0f, 1f));
            Assert.That(exit, Is.InRange(0f, 1f));
            Assert.Less(entry, exit);
            Assert.IsTrue(captionRect.Contains(Vector2.Lerp(segment.From, segment.To, (entry + exit) * 0.5f)));
        }

        [Test]
        public void CaptionClipInterval_RejectsSegmentThatMissesCaption()
        {
            MethodInfo clipMethod = typeof(PackageGraphMembershipLayer).GetMethod(
                "TryGetSegmentRectInterval",
                BindingFlags.Static | BindingFlags.NonPublic);
            object[] arguments =
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 100f),
                new Rect(20f, 30f, 20f, 40f),
                0f,
                0f
            };

            Assert.NotNull(clipMethod);
            Assert.IsFalse((bool)clipMethod.Invoke(null, arguments));
        }

        [Test]
        public void NestedGroupSpokes_ClipBothParentAndChildCaptionText()
        {
            PackageGraphCanvas canvas = CreateNestedGroupCanvas();
            PackageGraphMembershipLayer membershipLayer =
                canvas.Q<PackageGraphMembershipLayer>("ecosystem-graph-membership-layer");
            PackageGraphGroupLayoutNode parent = canvas.GroupLayoutNodesForTests.Single(candidate =>
                string.Equals(candidate.GroupId, ParentGroupId, StringComparison.OrdinalIgnoreCase));
            PackageGraphGroupLayoutNode[] children = canvas.GroupLayoutNodesForTests
                .Where(candidate =>
                    candidate.Group != null &&
                    string.Equals(candidate.Group.ParentGroupId, ParentGroupId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.Group.SortOrder)
                .ToArray();
            bool parentCaptionCrossed = false;
            bool childCaptionCrossed = false;

            Assert.NotNull(membershipLayer);
            Assert.AreEqual(3, children.Length);

            foreach (PackageGraphGroupLayoutNode child in children)
            {
                IReadOnlyList<Rect> captionOcclusions =
                    membershipLayer.SpokeCaptionOcclusionsForTests(ParentGroupId, child.GroupId);

                Assert.AreEqual(2, captionOcclusions.Count, child.GroupId);
                Assert.That(captionOcclusions[0].width, Is.GreaterThan(0f));
                Assert.That(captionOcclusions[1].width, Is.GreaterThan(0f));
                Assert.IsTrue(PackageGraphMembershipLayer.TryCreateSpokeSegmentForTests(
                    parent.HubRect,
                    child.HubRect,
                    out PackageGraphStructuralMembershipSegment spoke));

                parentCaptionCrossed |= SegmentIntersectsRect(spoke, captionOcclusions[0]);
                childCaptionCrossed |= SegmentIntersectsRect(spoke, captionOcclusions[1]);

                IReadOnlyList<PackageGraphStructuralMembershipSegment> visibleSegments =
                    PackageGraphMembershipLayer.VisibleLineSegmentsForTests(
                        spoke.From,
                        spoke.To,
                        captionOcclusions[0],
                        captionOcclusions[1]);

                Assert.IsNotEmpty(visibleSegments, child.GroupId);
                Assert.IsFalse(visibleSegments.Any(segment =>
                    SegmentIntersectsRect(segment, captionOcclusions[0]) ||
                    SegmentIntersectsRect(segment, captionOcclusions[1])), child.GroupId);
            }

            Assert.IsTrue(parentCaptionCrossed, "No nested spoke exercised the parent caption clip.");
            Assert.IsTrue(childCaptionCrossed, "No nested spoke exercised the child caption clip.");
        }

        [Test]
        public void TwoCaptionClip_PreservesOnlyTheThreeReadableLineSections()
        {
            Vector2 from = new Vector2(0f, 0f);
            Vector2 to = new Vector2(0f, 100f);
            Rect sourceCaption = new Rect(-5f, 20f, 10f, 10f);
            Rect targetCaption = new Rect(-5f, 60f, 10f, 10f);

            IReadOnlyList<PackageGraphStructuralMembershipSegment> visibleSegments =
                PackageGraphMembershipLayer.VisibleLineSegmentsForTests(
                    from,
                    to,
                    sourceCaption,
                    targetCaption);

            Assert.AreEqual(3, visibleSegments.Count);
            Assert.That(visibleSegments[0].From.y, Is.EqualTo(0f).Within(0.01f));
            Assert.Less(visibleSegments[0].To.y, sourceCaption.yMin);
            Assert.Greater(visibleSegments[1].From.y, sourceCaption.yMax);
            Assert.Less(visibleSegments[1].To.y, targetCaption.yMin);
            Assert.Greater(visibleSegments[2].From.y, targetCaption.yMax);
            Assert.That(visibleSegments[2].To.y, Is.EqualTo(100f).Within(0.01f));
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
            PackageGraphModel graph = new PackageGraphBuilder(_ => true).Build(packages, groups);
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

        private static void AssertStyleRect(VisualElement element, Rect expected)
        {
            Assert.That(element.style.left.value.value, Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(element.style.top.value.value, Is.EqualTo(expected.y).Within(0.01f));
            Assert.That(element.style.width.value.value, Is.EqualTo(expected.width).Within(0.01f));
            Assert.That(element.style.height.value.value, Is.EqualTo(expected.height).Within(0.01f));
        }
    }
}
