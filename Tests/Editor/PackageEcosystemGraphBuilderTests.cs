using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageGraphBuilderTests
    {
        [Test]
        public void Build_MapsRegistryMetadataToNodeTypesAndRelationships()
        {
            PackageDefinition core = CreatePackage("Core", "com.example.core", "Core");
            PackageDefinition tool = CreatePackage("Tool", "com.example.tool", "Tools", "Tool");
            PackageDefinition optional = CreatePackage("Optional", "com.example.optional", "UI", "OptionalIntegration");
            PackageDefinition bridge = CreatePackage(
                "Bridge",
                "com.example.bridge",
                "Bridge",
                "Bridge",
                dependencies: new[] { core.PackageId, optional.PackageId },
                bridgeTargets: new[] { core.PackageId, optional.PackageId });
            PackageDefinition suite = CreatePackage(
                "Suite",
                "com.example.suite",
                "Suites",
                "Suite",
                dependencies: new[] { core.PackageId, optional.PackageId, bridge.PackageId },
                suiteMembers: new[] { core.PackageId, optional.PackageId, bridge.PackageId });

            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == core.PackageId || packageId == bridge.PackageId)
                .Build(new[] { core, tool, optional, bridge, suite });

            Assert.AreEqual(PackageGraphNodeType.Core, graph.Nodes.Single(node => node.PackageId == core.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Tool, graph.Nodes.Single(node => node.PackageId == tool.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Integration, graph.Nodes.Single(node => node.PackageId == optional.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Bridge, graph.Nodes.Single(node => node.PackageId == bridge.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Suite, graph.Nodes.Single(node => node.PackageId == suite.PackageId).NodeType);

            Assert.IsTrue(graph.Edges.Any(edge =>
                edge.Kind == PackageGraphEdgeKind.Bridge &&
                edge.FromPackageId == core.PackageId &&
                edge.ToPackageId == bridge.PackageId));
            Assert.IsTrue(graph.Edges.Any(edge =>
                edge.Kind == PackageGraphEdgeKind.SuiteMembership &&
                edge.FromPackageId == suite.PackageId &&
                edge.ToPackageId == optional.PackageId));
            Assert.AreEqual(1, graph.SuiteRegions.Count);
            CollectionAssert.Contains(graph.SuiteRegions[0].MemberPackageIds.ToArray(), bridge.PackageId);
        }

        [Test]
        public void Build_CreatesWarningNodeForMissingOptionalRelationship()
        {
            PackageDefinition package = CreatePackage(
                "Package",
                "com.example.package",
                "Core",
                "Core",
                optionalIntegrations: new[] { "com.example.missing" });

            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(new[] { package });

            PackageGraphNode missingNode = graph.Nodes.Single(node => node.PackageId == "com.example.missing");
            PackageGraphEdge warningEdge = graph.Edges.Single(edge => edge.ToPackageId == missingNode.PackageId);

            Assert.IsFalse(missingNode.IsRegistered);
            Assert.AreEqual(PackageGraphNodeStatus.Missing, missingNode.Status);
            Assert.AreEqual(PackageGraphEdgeState.Warning, warningEdge.State);
        }

        [Test]
        public void Build_CreatesNodeActionsFromInstallAndUpdateState()
        {
            PackageDefinition installed = CreatePackage("Installed", "com.example.installed", "Core");
            PackageDefinition update = CreatePackage("Update", "com.example.update", "Core");
            PackageDefinition missing = CreatePackage("Missing", "com.example.not-installed", "Core");

            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == installed.PackageId || packageId == update.PackageId,
                    _ => PackageChannel.Development,
                    package => package.PackageId == update.PackageId
                        ? PackageUpdateStatus.UpdateAvailable(
                            package,
                            PackageChannel.Development,
                            package.GetUrl(PackageChannel.Development),
                            "1111111",
                            "2222222")
                        : PackageUpdateStatus.UpToDate(
                            package,
                            PackageChannel.Development,
                            package.GetUrl(PackageChannel.Development),
                            "1111111",
                            "1111111"))
                .Build(new[] { installed, update, missing });

            Assert.AreEqual(
                PackageGraphNodeAction.Reinstall,
                graph.Nodes.Single(node => node.PackageId == installed.PackageId).PrimaryAction);
            Assert.AreEqual(
                PackageGraphNodeAction.Update,
                graph.Nodes.Single(node => node.PackageId == update.PackageId).PrimaryAction);
            Assert.AreEqual(
                PackageGraphNodeStatus.UpdateAvailable,
                graph.Nodes.Single(node => node.PackageId == update.PackageId).Status);
            Assert.AreEqual(
                PackageGraphNodeAction.Install,
                graph.Nodes.Single(node => node.PackageId == missing.PackageId).PrimaryAction);
        }

        [Test]
        public void GraphView_UsesAttentionStylingForUpdateAvailableNodes()
        {
            PackageDefinition update = CreatePackage("Update", "com.example.update", "Core");
            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == update.PackageId,
                    _ => PackageChannel.Stable,
                    package => PackageUpdateStatus.UpdateAvailable(
                        package,
                        PackageChannel.Stable,
                        package.GetUrl(PackageChannel.Stable),
                        "1111111",
                        "2222222"))
                .Build(new[] { update });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            Assert.AreEqual(1, FindByClass(view, "dpi-graph-node--status-update").Count);
            Label updateBadge = FindByClass(view, "dpi-graph-node__badge--update")
                .OfType<Label>()
                .Single();
            Assert.AreEqual("Update available", updateBadge.text);
            Assert.AreEqual(
                "Update",
                FindByClass(view, "dpi-graph-node__action").OfType<Button>().Single().text);
        }

        [Test]
        public void Build_MarksMissingInstalledDependencyRequirementAsWarningNode()
        {
            PackageDefinition dependency = CreatePackage("Dependency", "com.example.dependency", "Core");
            PackageDefinition installed = CreatePackage(
                "Installed",
                "com.example.installed",
                "Core",
                dependencies: new[] { dependency.PackageId });

            PackageGraphModel graph = new PackageGraphBuilder(packageId => packageId == installed.PackageId)
                .Build(new[] { dependency, installed });
            PackageGraphNode dependencyNode = graph.Nodes.Single(node => node.PackageId == dependency.PackageId);

            Assert.AreEqual(PackageGraphNodeStatus.Warning, dependencyNode.Status);
            Assert.AreEqual("Required by installed package", dependencyNode.UpdateStatusLabel);
            Assert.AreEqual(PackageGraphNodeAction.Install, dependencyNode.PrimaryAction);
        }

        [Test]
        public void GraphView_CreatesNodeElementsAndPainterEdgeLayer()
        {
            PackageDefinition core = CreatePackage("Core", "com.example.core", "Core");
            PackageDefinition integration = CreatePackage("Integration", "com.example.integration", "UI", "OptionalIntegration");
            PackageDefinition bridge = CreatePackage(
                "Bridge",
                "com.example.bridge",
                "Bridge",
                "Bridge",
                bridgeTargets: new[] { core.PackageId, integration.PackageId });

            PackageGraphModel graph = new PackageGraphBuilder(packageId => packageId == core.PackageId)
                .Build(new[] { core, integration, bridge });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, core.PackageId, actionsEnabled: true);

            Assert.IsNull(view.Q<ScrollView>());
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-viewport"));
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-content"));
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-edge-layer"));
            Assert.AreEqual(3, FindByClass(view, "dpi-graph-node").Count);
            Assert.AreEqual(1, FindByClass(view, "dpi-graph-node--bridge").Count);
            Assert.IsTrue(FindByClass(view, "dpi-graph-node__action").Count >= 1);
        }

        [Test]
        public void Layout_CalculatesOrbitalPositionsWithoutNodeOverlap()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(graph);

            Assert.AreEqual(graph.Nodes.Count, layout.NodeRects.Count);
            Assert.IsTrue(layout.RingGuides.Count >= 4);
            Assert.AreEqual(
                PackageGraphLayoutRing.Foundation,
                layout.NodeRings["com.deucarian.object-loading"]);
            Assert.AreEqual(
                PackageGraphLayoutRing.Runtime,
                layout.NodeRings["com.deucarian.diagnostics"]);
            AssertNoOverlaps(layout.NodeRects.Values.ToArray());
        }

        [Test]
        public void Focus_SelectingObjectLoadingShowsDependencyBridgeAndCompanionContext()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphFocus focus = PackageGraphFocus.Create(
                graph,
                "com.deucarian.object-loading",
                Array.Empty<string>());

            Assert.IsTrue(focus.IsPackageRelated("com.deucarian.logging"));
            Assert.IsTrue(focus.IsPackageRelated("com.deucarian.object-loading.api-bridge"));
            Assert.IsTrue(focus.IsPackageRelated("com.deucarian.api"));
            Assert.IsTrue(focus.IsPackageRelated("com.deucarian.diagnostics"));
            Assert.IsFalse(focus.IsPackageRelated("com.deucarian.theming"));

            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.logging",
                "com.deucarian.object-loading",
                PackageGraphEdgeKind.HardDependency);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.object-loading.api-bridge",
                PackageGraphEdgeKind.Bridge);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.api",
                "com.deucarian.object-loading.api-bridge",
                PackageGraphEdgeKind.Bridge);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.diagnostics",
                PackageGraphEdgeKind.OptionalIntegration);

            PackageGraphEdge unrelatedThemingEdge = graph.Edges.Single(edge =>
                edge.FromPackageId == "com.deucarian.editor" &&
                edge.ToPackageId == "com.deucarian.theming");
            Assert.IsFalse(focus.IsEdgeVisible(unrelatedThemingEdge));
        }

        [Test]
        public void Focus_ExpandsSuiteMembershipOnlyWhenFocusedOrExpanded()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphSuiteRegion selectionSuite = graph.SuiteRegions.Single(region =>
                region.SuitePackageId == "com.deucarian.selection-suite");
            PackageGraphEdge suiteMembershipEdge = graph.Edges.Single(edge =>
                edge.Kind == PackageGraphEdgeKind.SuiteMembership &&
                edge.FromPackageId == selectionSuite.SuitePackageId &&
                edge.ToPackageId == "com.deucarian.ui-binding");

            PackageGraphFocus overview = PackageGraphFocus.Create(graph, string.Empty, Array.Empty<string>());

            Assert.IsFalse(overview.IsSuiteRegionVisible(selectionSuite));
            Assert.IsFalse(overview.IsEdgeVisible(suiteMembershipEdge));

            PackageGraphFocus expandedOverview = PackageGraphFocus.Create(
                graph,
                string.Empty,
                new[] { selectionSuite.SuitePackageId });

            Assert.IsTrue(expandedOverview.IsSuiteRegionVisible(selectionSuite));
            Assert.IsTrue(expandedOverview.IsEdgeVisible(suiteMembershipEdge));

            PackageGraphFocus focusedSuite = PackageGraphFocus.Create(
                graph,
                selectionSuite.SuitePackageId,
                Array.Empty<string>());

            Assert.IsTrue(focusedSuite.IsSuiteRegionVisible(selectionSuite));
            Assert.IsTrue(focusedSuite.IsEdgeVisible(suiteMembershipEdge));
            Assert.IsTrue(focusedSuite.IsPackageRelated("com.deucarian.object-selection.core-state-bridge"));
        }

        [Test]
        public void Focus_OverviewHidesNormalDependencyEdges()
        {
            PackageDefinition editor = CreatePackage("Editor", "com.example.editor", "Editor");
            PackageDefinition logging = CreatePackage(
                "Logging",
                "com.example.logging",
                "Core",
                dependencies: new[] { editor.PackageId });
            PackageGraphModel graph = new PackageGraphBuilder(_ => true)
                .Build(new[] { editor, logging });
            PackageGraphFocus overview = PackageGraphFocus.Create(graph, string.Empty, Array.Empty<string>());
            PackageGraphEdge dependencyEdge = graph.Edges.Single(edge =>
                edge.Kind == PackageGraphEdgeKind.HardDependency &&
                edge.FromPackageId == editor.PackageId &&
                edge.ToPackageId == logging.PackageId);

            Assert.IsFalse(overview.IsEdgeVisible(dependencyEdge));

            PackageGraphFocus focused = PackageGraphFocus.Create(graph, logging.PackageId, Array.Empty<string>());

            Assert.IsTrue(focused.IsEdgeVisible(dependencyEdge));
            Assert.IsTrue(focused.IsEdgeEmphasized(dependencyEdge));
        }

        private static PackageDefinition CreatePackage(
            string displayName,
            string packageId,
            string category,
            string metadataType = null,
            string[] dependencies = null,
            string[] optionalIntegrations = null,
            string[] bridgeTargets = null,
            string[] suiteMembers = null,
            string[] optionalCompanions = null,
            string[] recommendedWith = null)
        {
            return new PackageDefinition(
                displayName,
                packageId,
                "https://example.com/" + packageId + ".git#main",
                displayName + " package.",
                dependencies ?? Array.Empty<string>(),
                PackageType.Core,
                "https://example.com/" + packageId + ".git#develop",
                category: category,
                metadataType: metadataType,
                optionalIntegrations: optionalIntegrations,
                bridgeTargets: bridgeTargets,
                suiteMembers: suiteMembers,
                optionalCompanions: optionalCompanions,
                recommendedWith: recommendedWith);
        }

        private static PackageDefinition[] CreateDefaultGraphPackages()
        {
            return new[]
            {
                CreatePackage("Deucarian Editor", "com.deucarian.editor", "Editor"),
                CreatePackage(
                    "Deucarian Logging",
                    "com.deucarian.logging",
                    "Core",
                    dependencies: new[] { "com.deucarian.editor" }),
                CreatePackage(
                    "Deucarian API",
                    "com.deucarian.api",
                    "Core",
                    dependencies: new[] { "com.deucarian.logging" },
                    optionalIntegrations: new[]
                    {
                        "com.deucarian.session.api-bridge",
                        "com.deucarian.object-loading.api-bridge"
                    }),
                CreatePackage(
                    "Deucarian Core State",
                    "com.deucarian.core-state",
                    "Core",
                    recommendedWith: new[] { "com.deucarian.selection-suite" }),
                CreatePackage(
                    "Deucarian Object Loading",
                    "com.deucarian.object-loading",
                    "Core",
                    dependencies: new[] { "com.deucarian.logging" },
                    optionalIntegrations: new[] { "com.deucarian.object-loading.api-bridge" },
                    optionalCompanions: new[] { "com.deucarian.diagnostics" }),
                CreatePackage(
                    "Deucarian Session",
                    "com.deucarian.session",
                    "Core",
                    dependencies: new[] { "com.deucarian.logging" },
                    optionalIntegrations: new[] { "com.deucarian.session.api-bridge" }),
                CreatePackage(
                    "Deucarian UI Binding",
                    "com.deucarian.ui-binding",
                    "UI",
                    "OptionalIntegration",
                    optionalIntegrations: new[] { "com.deucarian.ui-binding.core-state-bridge" },
                    recommendedWith: new[] { "com.deucarian.selection-suite" }),
                CreatePackage(
                    "Deucarian Object Selection",
                    "com.deucarian.object-selection",
                    "World",
                    "OptionalIntegration",
                    dependencies: new[] { "com.deucarian.logging" },
                    optionalIntegrations: new[] { "com.deucarian.object-selection.core-state-bridge" },
                    recommendedWith: new[] { "com.deucarian.selection-suite" }),
                CreatePackage(
                    "Deucarian Theming",
                    "com.deucarian.theming",
                    "UI",
                    "OptionalIntegration",
                    dependencies: new[]
                    {
                        "com.deucarian.editor",
                        "com.deucarian.logging"
                    }),
                CreatePackage(
                    "Deucarian Diagnostics",
                    "com.deucarian.diagnostics",
                    "Tools",
                    "OptionalIntegration",
                    dependencies: new[]
                    {
                        "com.deucarian.editor",
                        "com.deucarian.logging"
                    }),
                CreatePackage(
                    "Deucarian Package Installer",
                    "com.deucarian.package-installer",
                    "Tools",
                    "Tool",
                    dependencies: new[]
                    {
                        "com.deucarian.editor",
                        "com.deucarian.logging"
                    }),
                CreatePackage(
                    "Deucarian Session API Bridge",
                    "com.deucarian.session.api-bridge",
                    "Bridge",
                    "Bridge",
                    dependencies: new[]
                    {
                        "com.deucarian.session",
                        "com.deucarian.api"
                    },
                    bridgeTargets: new[]
                    {
                        "com.deucarian.session",
                        "com.deucarian.api"
                    }),
                CreatePackage(
                    "Deucarian Object Loading API Bridge",
                    "com.deucarian.object-loading.api-bridge",
                    "Bridge",
                    "Bridge",
                    dependencies: new[]
                    {
                        "com.deucarian.object-loading",
                        "com.deucarian.api"
                    },
                    bridgeTargets: new[]
                    {
                        "com.deucarian.object-loading",
                        "com.deucarian.api"
                    }),
                CreatePackage(
                    "Deucarian UI Binding Core State Bridge",
                    "com.deucarian.ui-binding.core-state-bridge",
                    "Bridge",
                    "Bridge",
                    dependencies: new[]
                    {
                        "com.deucarian.ui-binding",
                        "com.deucarian.core-state"
                    },
                    bridgeTargets: new[]
                    {
                        "com.deucarian.ui-binding",
                        "com.deucarian.core-state"
                    }),
                CreatePackage(
                    "Deucarian Object Selection Core State Bridge",
                    "com.deucarian.object-selection.core-state-bridge",
                    "Bridge",
                    "Bridge",
                    dependencies: new[]
                    {
                        "com.deucarian.object-selection",
                        "com.deucarian.core-state"
                    },
                    bridgeTargets: new[]
                    {
                        "com.deucarian.object-selection",
                        "com.deucarian.core-state"
                    }),
                CreatePackage(
                    "Deucarian Selection Suite",
                    "com.deucarian.selection-suite",
                    "Suites",
                    "Suite",
                    dependencies: new[]
                    {
                        "com.deucarian.core-state",
                        "com.deucarian.ui-binding",
                        "com.deucarian.object-selection",
                        "com.deucarian.ui-binding.core-state-bridge",
                        "com.deucarian.object-selection.core-state-bridge"
                    },
                    suiteMembers: new[]
                    {
                        "com.deucarian.core-state",
                        "com.deucarian.ui-binding",
                        "com.deucarian.object-selection",
                        "com.deucarian.ui-binding.core-state-bridge",
                        "com.deucarian.object-selection.core-state-bridge"
                    })
            };
        }

        private static void AssertEdgeVisible(
            PackageGraphModel graph,
            PackageGraphFocus focus,
            string fromPackageId,
            string toPackageId,
            PackageGraphEdgeKind kind)
        {
            PackageGraphEdge edge = graph.Edges.Single(candidate =>
                candidate.Kind == kind &&
                candidate.FromPackageId == fromPackageId &&
                candidate.ToPackageId == toPackageId);

            Assert.IsTrue(focus.IsEdgeVisible(edge), edge.Key);
            Assert.IsTrue(focus.IsEdgeEmphasized(edge), edge.Key);
        }

        private static void AssertNoOverlaps(IReadOnlyList<UnityEngine.Rect> rects)
        {
            for (int firstIndex = 0; firstIndex < rects.Count; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < rects.Count; secondIndex++)
                {
                    Assert.IsFalse(
                        rects[firstIndex].Overlaps(rects[secondIndex]),
                        rects[firstIndex] + " overlaps " + rects[secondIndex]);
                }
            }
        }

        private static List<VisualElement> FindByClass(VisualElement root, string className)
        {
            List<VisualElement> matches = new List<VisualElement>();
            CollectByClass(root, className, matches);
            return matches;
        }

        private static void CollectByClass(VisualElement element, string className, ICollection<VisualElement> matches)
        {
            if (element == null)
            {
                return;
            }

            if (element.ClassListContains(className))
            {
                matches.Add(element);
            }

            foreach (VisualElement child in element.Children())
            {
                CollectByClass(child, className, matches);
            }
        }
    }
}
