using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageGraphStructuralMembershipRouteTests
    {
        private const string AttentionFirstId = "com.example.attention-first";
        private const string AttentionSecondId = "com.example.attention-second";
        private const string InstalledId = "com.example.installed";
        private const string NotInstalledId = "com.example.not-installed";
        private const float PositionTolerance = 0.01f;

        [Test]
        public void VerticalBus_SplitsAtEveryDivergenceAndTracksTraversingPackages()
        {
            PackageGraphStructuralMembershipRoute route = CreateVerticalRoute();

            Assert.IsTrue(route.UsesBus);
            Assert.IsFalse(route.Segments.Any(segment =>
                segment.PackageIds == null || segment.PackageIds.Count == 0));

            AssertOwners(
                FindVerticalSegment(route, 100f, 300f),
                AttentionFirstId);
            AssertOwners(
                FindVerticalSegment(route, 300f, 450f),
                AttentionFirstId,
                AttentionSecondId);
            AssertOwners(
                FindVerticalSegment(route, 450f, 600f),
                InstalledId,
                NotInstalledId);
            AssertOwners(
                FindVerticalSegment(route, 600f, 800f),
                NotInstalledId);
        }

        [Test]
        public void HorizontalBus_SplitsAtEveryDivergenceAndTracksTraversingPackages()
        {
            PackageGraphStructuralMembershipRoute route = CreateHorizontalRoute();

            Assert.IsTrue(route.UsesBus);
            Assert.IsFalse(route.Segments.Any(segment =>
                segment.PackageIds == null || segment.PackageIds.Count == 0));

            AssertOwners(
                FindHorizontalSegment(route, 100f, 300f),
                AttentionFirstId);
            AssertOwners(
                FindHorizontalSegment(route, 300f, 450f),
                AttentionFirstId,
                AttentionSecondId);
            AssertOwners(
                FindHorizontalSegment(route, 450f, 600f),
                InstalledId,
                NotInstalledId);
            AssertOwners(
                FindHorizontalSegment(route, 600f, 800f),
                NotInstalledId);
        }

        [Test]
        public void SegmentStatus_MixedSharedPathIsNeutralAndSingletonBranchesStayExact()
        {
            PackageGraphStructuralMembershipRoute route = CreateVerticalRoute();
            PackageGraphModel graph = CreateStatusGraph();
            PackageGraphMembershipLayer layer = new PackageGraphMembershipLayer();
            layer.SetLayout(
                graph,
                null,
                PackageGraphCategoryStatusSummary.Create(graph.Nodes),
                PackageGraphSearchState.Empty,
                null,
                null,
                null,
                null,
                string.Empty,
                false);

            PackageGraphStructuralMembershipSegment sameStatusShared =
                FindVerticalSegment(route, 300f, 450f);
            PackageGraphStructuralMembershipSegment mixedShared =
                FindVerticalSegment(route, 450f, 600f);
            PackageGraphStructuralMembershipSegment attentionBranch =
                FindPackageBranch(route, AttentionFirstId);
            PackageGraphStructuralMembershipSegment installedBranch =
                FindPackageBranch(route, InstalledId);
            PackageGraphStructuralMembershipSegment notInstalledBranch =
                FindPackageBranch(route, NotInstalledId);

            Assert.AreEqual(
                PackageGraphCategoryStatusKey.Attention,
                ResolveSegmentStatus(layer, sameStatusShared));
            Assert.AreEqual(
                PackageGraphCategoryStatusKey.Unknown,
                ResolveSegmentStatus(layer, mixedShared));
            Assert.AreEqual(
                PackageGraphCategoryStatusKey.Attention,
                ResolveSegmentStatus(layer, attentionBranch));
            Assert.AreEqual(
                PackageGraphCategoryStatusKey.Installed,
                ResolveSegmentStatus(layer, installedBranch));
            Assert.AreEqual(
                PackageGraphCategoryStatusKey.NotInstalled,
                ResolveSegmentStatus(layer, notInstalledBranch));

            Assert.IsEmpty(mixedShared.PackageId);
            Assert.AreEqual(InstalledId, installedBranch.PackageId);
            Assert.AreEqual(NotInstalledId, notInstalledBranch.PackageId);
            AssertColorEqual(
                PackageGraphCategoryStatusVisuals.GetColor(PackageGraphCategoryStatusKey.Unknown),
                PackageGraphCategoryStatusVisuals.GetColor(ResolveSegmentStatus(layer, mixedShared)));
        }

        private static PackageGraphStructuralMembershipRoute CreateVerticalRoute()
        {
            return CreateRoute(
                CenteredRect(700f, 450f, 220f, 760f),
                CenteredRect(700f, 450f, 80f, 80f),
                new[]
                {
                    PackageRect(AttentionFirstId, 100f, 100f),
                    PackageRect(AttentionSecondId, 100f, 300f),
                    PackageRect(InstalledId, 100f, 600f),
                    PackageRect(NotInstalledId, 100f, 800f)
                });
        }

        private static PackageGraphStructuralMembershipRoute CreateHorizontalRoute()
        {
            return CreateRoute(
                CenteredRect(450f, 700f, 760f, 220f),
                CenteredRect(450f, 700f, 80f, 80f),
                new[]
                {
                    PackageRect(AttentionFirstId, 100f, 100f),
                    PackageRect(AttentionSecondId, 300f, 100f),
                    PackageRect(InstalledId, 600f, 100f),
                    PackageRect(NotInstalledId, 800f, 100f)
                });
        }

        private static PackageGraphStructuralMembershipRoute CreateRoute(
            Rect groupRect,
            Rect groupHubRect,
            IReadOnlyList<KeyValuePair<string, Rect>> packageRects)
        {
            MethodInfo method = typeof(PackageGraphMembershipLayer).GetMethod(
                "CreateStructuralMembershipRoute",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Structural membership route factory was not found.");
            return (PackageGraphStructuralMembershipRoute)method.Invoke(
                null,
                new object[] { "tools-quality", groupRect, groupHubRect, packageRects });
        }

        private static PackageGraphCategoryStatusKey ResolveSegmentStatus(
            PackageGraphMembershipLayer layer,
            PackageGraphStructuralMembershipSegment segment)
        {
            MethodInfo method = typeof(PackageGraphMembershipLayer).GetMethod(
                "ResolveSegmentStatus",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Structural membership status resolver was not found.");
            return (PackageGraphCategoryStatusKey)method.Invoke(
                layer,
                new object[] { segment, PackageGraphCategoryStatusKey.Attention });
        }

        private static PackageGraphModel CreateStatusGraph()
        {
            PackageDefinition[] packages =
            {
                CreatePackage("Attention First", AttentionFirstId),
                CreatePackage("Attention Second", AttentionSecondId),
                CreatePackage("Installed", InstalledId),
                CreatePackage("Not Installed", NotInstalledId)
            };
            HashSet<string> installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                AttentionFirstId,
                AttentionSecondId,
                InstalledId
            };

            return new PackageGraphBuilder(
                    packageId => installedIds.Contains(packageId),
                    _ => PackageChannel.Stable,
                    package => package.PackageId == AttentionFirstId ||
                               package.PackageId == AttentionSecondId
                        ? PackageUpdateStatus.UpdateAvailable(
                            package,
                            PackageChannel.Stable,
                            package.GetUrl(PackageChannel.Stable),
                            "1111111",
                            "2222222")
                        : PackageUpdateStatus.UpToDate(
                            package,
                            PackageChannel.Stable,
                            package.GetUrl(PackageChannel.Stable),
                            "1111111",
                            "1111111"))
                .Build(packages);
        }

        private static PackageDefinition CreatePackage(string displayName, string packageId)
        {
            return new PackageDefinition(
                displayName,
                packageId,
                "https://example.com/" + packageId + ".git#main",
                displayName + " package.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://example.com/" + packageId + ".git#develop",
                category: "Tools",
                metadataType: "Tool",
                ecosystemGroup: "Tools & Quality",
                groupId: "tools-quality");
        }

        private static KeyValuePair<string, Rect> PackageRect(string packageId, float centerX, float centerY)
        {
            return new KeyValuePair<string, Rect>(
                packageId,
                CenteredRect(centerX, centerY, 160f, 80f));
        }

        private static Rect CenteredRect(float centerX, float centerY, float width, float height)
        {
            return new Rect(centerX - width * 0.5f, centerY - height * 0.5f, width, height);
        }

        private static PackageGraphStructuralMembershipSegment FindVerticalSegment(
            PackageGraphStructuralMembershipRoute route,
            float minimumY,
            float maximumY)
        {
            return route.Segments.Single(segment =>
                Mathf.Abs(segment.From.x - segment.To.x) <= PositionTolerance &&
                Mathf.Abs(Mathf.Min(segment.From.y, segment.To.y) - minimumY) <= PositionTolerance &&
                Mathf.Abs(Mathf.Max(segment.From.y, segment.To.y) - maximumY) <= PositionTolerance);
        }

        private static PackageGraphStructuralMembershipSegment FindHorizontalSegment(
            PackageGraphStructuralMembershipRoute route,
            float minimumX,
            float maximumX)
        {
            return route.Segments.Single(segment =>
                Mathf.Abs(segment.From.y - segment.To.y) <= PositionTolerance &&
                Mathf.Abs(Mathf.Min(segment.From.x, segment.To.x) - minimumX) <= PositionTolerance &&
                Mathf.Abs(Mathf.Max(segment.From.x, segment.To.x) - maximumX) <= PositionTolerance);
        }

        private static PackageGraphStructuralMembershipSegment FindPackageBranch(
            PackageGraphStructuralMembershipRoute route,
            string packageId)
        {
            return route.Segments.Single(segment =>
                string.Equals(segment.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
                Mathf.Abs(segment.From.y - segment.To.y) <= PositionTolerance);
        }

        private static void AssertOwners(
            PackageGraphStructuralMembershipSegment segment,
            params string[] expectedPackageIds)
        {
            CollectionAssert.AreEquivalent(expectedPackageIds, segment.PackageIds.ToArray());

            if (expectedPackageIds.Length == 1)
            {
                Assert.AreEqual(expectedPackageIds[0], segment.PackageId);
            }
            else
            {
                Assert.IsEmpty(segment.PackageId);
            }
        }

        private static void AssertColorEqual(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 0.001f);
            Assert.AreEqual(expected.g, actual.g, 0.001f);
            Assert.AreEqual(expected.b, actual.b, 0.001f);
            Assert.AreEqual(expected.a, actual.a, 0.001f);
        }
    }
}
