using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageGraphBuilderTests
    {
        [Test]
        public void Window_DefaultsToEcosystemGraphAndOrdersToggleFirst()
        {
            Assert.IsTrue(PackageInstallerWindow.DefaultsToEcosystemGraphForTests);
            CollectionAssert.AreEqual(
                new[] { "Ecosystem Graph", "List View" },
                PackageInstallerWindow.ViewToggleOrderForTests.ToArray());
        }

        [Test]
        public void Window_ResponsiveBreakpointsPreserveUsableMinimumWidth()
        {
            Assert.AreEqual(PackageInstallerResponsiveMode.Wide, PackageInstallerWindow.ResolveResponsiveModeForTests(1280f));
            Assert.AreEqual(PackageInstallerResponsiveMode.Compact, PackageInstallerWindow.ResolveResponsiveModeForTests(1040f));
            Assert.AreEqual(PackageInstallerResponsiveMode.Narrow, PackageInstallerWindow.ResolveResponsiveModeForTests(860f));
            Assert.AreEqual(PackageInstallerResponsiveMode.Narrow, PackageInstallerWindow.ResolveResponsiveModeForTests(820f));
            Assert.AreEqual(820f, PackageInstallerWindow.MinWindowSizeForTests.x);
            Assert.AreEqual(650f, PackageInstallerWindow.MinWindowSizeForTests.y);
        }

        [Test]
        public void Window_FixedWallpaperLayerIsAbsoluteAndNonInteractive()
        {
            VisualElement root = new VisualElement();
            VisualElement background = new VisualElement { name = "deucarian-window-background" };
            VisualElement overlay = new VisualElement { name = "deucarian-window-overlay" };
            root.Add(background);
            root.Add(overlay);

            PackageInstallerWindow.ConfigureFixedWallpaperForTests(root);

            Assert.IsTrue(background.ClassListContains("dpi-fixed-wallpaper-layer"));
            Assert.IsTrue(overlay.ClassListContains("dpi-fixed-wallpaper-overlay"));
            Assert.AreEqual(PickingMode.Ignore, background.pickingMode);
            Assert.AreEqual(PickingMode.Ignore, overlay.pickingMode);
            Assert.AreEqual(Position.Absolute, background.style.position.value);
            Assert.AreEqual(Position.Absolute, overlay.style.position.value);
        }

        [Test]
        public void Build_MapsRegistryMetadataToNodeTypesAndRelationships()
        {
            PackageDefinition core = CreatePackage("Core", "com.example.core", "Core");
            PackageDefinition tool = CreatePackage("Tool", "com.example.tool", "Tools", "Tool");
            PackageDefinition optional = CreatePackage("Optional", "com.example.optional", "UI", "OptionalIntegration");
            PackageDefinition integration = CreatePackage(
                "Integration",
                "com.example.integration",
                "Integration",
                "Integration",
                dependencies: new[] { core.PackageId, optional.PackageId },
                integrationTargets: new[] { core.PackageId, optional.PackageId });
            PackageDefinition suite = CreatePackage(
                "Suite",
                "com.example.suite",
                "Suites",
                "Suite",
                dependencies: new[] { core.PackageId, optional.PackageId, integration.PackageId },
                suiteMembers: new[] { core.PackageId, optional.PackageId, integration.PackageId });

            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == core.PackageId || packageId == integration.PackageId)
                .Build(new[] { core, tool, optional, integration, suite });

            Assert.AreEqual(PackageGraphNodeType.Core, graph.Nodes.Single(node => node.PackageId == core.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Tool, graph.Nodes.Single(node => node.PackageId == tool.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Companion, graph.Nodes.Single(node => node.PackageId == optional.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Integration, graph.Nodes.Single(node => node.PackageId == integration.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Suite, graph.Nodes.Single(node => node.PackageId == suite.PackageId).NodeType);

            Assert.IsTrue(graph.Edges.Any(edge =>
                edge.Kind == PackageGraphEdgeKind.HardDependency &&
                edge.FromPackageId == core.PackageId &&
                edge.ToPackageId == integration.PackageId));
            Assert.IsTrue(graph.Edges.Any(edge =>
                edge.Kind == PackageGraphEdgeKind.IntegrationConnection &&
                edge.FromPackageId == integration.PackageId &&
                edge.ToPackageId == core.PackageId));
            Assert.IsFalse(graph.Edges.Any(edge =>
                edge.Kind == PackageGraphEdgeKind.OptionalCompanion &&
                edge.ToPackageId == integration.PackageId));
            Assert.IsTrue(graph.Edges.Any(edge =>
                edge.Kind == PackageGraphEdgeKind.SuiteMembership &&
                edge.FromPackageId == suite.PackageId &&
                edge.ToPackageId == optional.PackageId));
            Assert.AreEqual(1, graph.SuiteRegions.Count);
            CollectionAssert.Contains(graph.SuiteRegions[0].MemberPackageIds.ToArray(), integration.PackageId);
        }

        [Test]
        public void Build_CreatesStructuralGroupsAndKeepsSuiteAndIntegrationAsPackages()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "infrastructure",
                    "state-data",
                    "runtime-services",
                    "experience-interaction",
                    "tools-quality",
                    "integrations",
                    "suites"
                },
                graph.Groups.Select(group => group.Id).ToArray());
            Assert.IsFalse(graph.Groups.Any(group => string.Equals(group.DisplayName, "Foundation", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual("infrastructure", graph.Nodes.Single(node => node.PackageId == "com.deucarian.editor").GroupId);
            Assert.AreEqual("infrastructure", graph.Nodes.Single(node => node.PackageId == "com.deucarian.logging").GroupId);
            Assert.AreEqual("state-data", graph.Nodes.Single(node => node.PackageId == "com.deucarian.core-state").GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == "com.deucarian.api").GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == "com.deucarian.session").GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == "com.deucarian.object-loading").GroupId);
            Assert.AreEqual("experience-interaction", graph.Nodes.Single(node => node.PackageId == "com.deucarian.ui-binding").GroupId);
            Assert.AreEqual("experience-interaction", graph.Nodes.Single(node => node.PackageId == "com.deucarian.theming").GroupId);
            Assert.AreEqual("experience-interaction", graph.Nodes.Single(node => node.PackageId == "com.deucarian.object-selection").GroupId);
            Assert.AreEqual("tools-quality", graph.Nodes.Single(node => node.PackageId == "com.deucarian.package-installer").GroupId);
            Assert.AreEqual("tools-quality", graph.Nodes.Single(node => node.PackageId == "com.deucarian.diagnostics").GroupId);
            Assert.AreEqual("integrations", graph.Nodes.Single(node => node.PackageId == "com.deucarian.session.api-integration").GroupId);
            Assert.AreEqual("suites", graph.Nodes.Single(node => node.PackageId == "com.deucarian.selection-suite").GroupId);
            Assert.AreEqual(PackageGraphNodeType.Integration, graph.Nodes.Single(node => node.PackageId == "com.deucarian.session.api-integration").NodeType);
            Assert.AreEqual(PackageGraphNodeType.Suite, graph.Nodes.Single(node => node.PackageId == "com.deucarian.selection-suite").NodeType);
        }

        [Test]
        public void Build_MigratesLegacyGroupAliasesToCurrentTaxonomy()
        {
            PackageGraphGroup[] groups =
            {
                new PackageGraphGroup("infrastructure", "Infrastructure", string.Empty, string.Empty, 10, string.Empty, string.Empty),
                new PackageGraphGroup("state-data", "State & Data", string.Empty, string.Empty, 20, string.Empty, string.Empty),
                new PackageGraphGroup("runtime-services", "Runtime Services", string.Empty, string.Empty, 30, string.Empty, string.Empty),
                new PackageGraphGroup("experience-interaction", "Experience & Interaction", string.Empty, string.Empty, 40, string.Empty, string.Empty),
                new PackageGraphGroup("tools-quality", "Tools & Quality", string.Empty, string.Empty, 50, string.Empty, string.Empty),
                new PackageGraphGroup("integrations", "Integrations", string.Empty, string.Empty, 60, string.Empty, string.Empty),
                new PackageGraphGroup("suites", "Suites", string.Empty, string.Empty, 70, string.Empty, string.Empty)
            };
            PackageDefinition logging = CreatePackage(
                "Logging",
                "com.deucarian.logging",
                "Core",
                groupId: "Foundation");
            PackageDefinition session = CreatePackage(
                "Session",
                "com.deucarian.session",
                "Core",
                ecosystemGroup: "ServicesRuntime");
            PackageDefinition ui = CreatePackage(
                "UI Binding",
                "com.deucarian.ui-binding",
                "UI",
                ecosystemGroup: "ExperienceUiWorld");

            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(new[] { logging, session, ui }, groups);

            Assert.AreEqual("infrastructure", graph.Nodes.Single(node => node.PackageId == logging.PackageId).GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == session.PackageId).GroupId);
            Assert.AreEqual("experience-interaction", graph.Nodes.Single(node => node.PackageId == ui.PackageId).GroupId);
            Assert.IsFalse(graph.Groups.Any(group => string.Equals(group.DisplayName, "Foundation", StringComparison.OrdinalIgnoreCase)));
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
        public void Build_LabelsDependencyEdgesAsDependentUsesRequiredPackage()
        {
            PackageDefinition logging = CreatePackage("Logging", "com.example.logging", "Core");
            PackageDefinition session = CreatePackage(
                "Session",
                "com.example.session",
                "Core",
                dependencies: new[] { logging.PackageId });

            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(new[] { logging, session });
            PackageGraphEdge dependencyEdge = graph.Edges.Single(edge =>
                edge.Kind == PackageGraphEdgeKind.HardDependency &&
                edge.FromPackageId == logging.PackageId &&
                edge.ToPackageId == session.PackageId);

            Assert.AreEqual("Session uses Logging", dependencyEdge.Label);
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
            Assert.IsEmpty(FindByClass(view, "dpi-graph-node__action"));

            PackageGraphView selectedView = new PackageGraphView(_ => { }, (_, __) => { });

            selectedView.SetGraph(graph, update.PackageId, actionsEnabled: true);

            Button selectedAction = FindByClass(selectedView, "dpi-graph-node__action")
                .OfType<Button>()
                .Single();
            Assert.AreEqual("Update", selectedAction.text);
            Assert.IsTrue(selectedAction.enabledSelf);
        }

        [Test]
        public void GraphView_ExposesMajorStatusVisualClassesAndLabels()
        {
            PackageDefinition installed = CreatePackage("Installed", "com.example.installed", "Core");
            PackageDefinition notInstalled = CreatePackage("Not Installed", "com.example.not-installed", "Core");
            PackageDefinition update = CreatePackage("Update", "com.example.update", "Core");
            PackageDefinition dependency = CreatePackage("Dependency", "com.example.dependency", "Core");
            PackageDefinition consumer = CreatePackage(
                "Consumer",
                "com.example.consumer",
                "Core",
                dependencies: new[] { dependency.PackageId },
                optionalIntegrations: new[] { "com.example.missing" });

            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == installed.PackageId ||
                                 packageId == update.PackageId ||
                                 packageId == consumer.PackageId,
                    _ => PackageChannel.Stable,
                    package => package.PackageId == update.PackageId
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
                .Build(new[] { installed, notInstalled, update, dependency, consumer });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            Assert.IsTrue(FindGraphNode(view, installed.PackageId).ClassListContains("dpi-graph-node--status-installed"));
            Assert.IsTrue(FindGraphNode(view, notInstalled.PackageId).ClassListContains("dpi-graph-node--status-available"));
            Assert.IsTrue(FindGraphNode(view, update.PackageId).ClassListContains("dpi-graph-node--status-update"));
            Assert.IsTrue(FindGraphNode(view, dependency.PackageId).ClassListContains("dpi-graph-node--status-warning"));
            Assert.IsTrue(FindGraphNode(view, "com.example.missing").ClassListContains("dpi-graph-node--status-missing"));
            Assert.AreEqual(1, FindByClass(FindGraphNode(view, installed.PackageId), "dpi-graph-node__status-rail--installed").Count);
            Assert.AreEqual(1, FindByClass(FindGraphNode(view, notInstalled.PackageId), "dpi-graph-node__status-icon--available").Count);
            Assert.AreEqual(1, FindByClass(FindGraphNode(view, update.PackageId), "dpi-graph-node__badge--update").Count);
            Assert.AreEqual(1, FindByClass(FindGraphNode(view, dependency.PackageId), "dpi-graph-node__badge--warning").Count);
            Assert.AreEqual(1, FindByClass(FindGraphNode(view, "com.example.missing"), "dpi-graph-node__badge--missing").Count);
            Assert.AreEqual(
                "\u2713",
                FindByClass(FindGraphNode(view, installed.PackageId), "dpi-graph-node__status-icon--installed")
                    .OfType<Label>()
                    .Single()
                    .text);
            Assert.AreEqual(
                "\u25CB",
                FindByClass(FindGraphNode(view, notInstalled.PackageId), "dpi-graph-node__status-icon--available")
                    .OfType<Label>()
                    .Single()
                    .text);
            Assert.AreEqual(
                "!",
                FindByClass(FindGraphNode(view, update.PackageId), "dpi-graph-node__status-icon--update")
                    .OfType<Label>()
                    .Single()
                    .text);
            Assert.IsTrue(
                FindByClass(FindGraphNode(view, dependency.PackageId), "dpi-graph-node__badge--warning")
                    .OfType<Label>()
                    .Any(label => label.text == "Required by installed package"));
            Assert.IsTrue(
                FindByClass(FindGraphNode(view, "com.example.missing"), "dpi-graph-node__badge--missing")
                    .OfType<Label>()
                    .Any(label => label.text == "Missing dependency"));
            Assert.IsEmpty(FindByClass(view, "deucarian-badge"));
        }

        [Test]
        public void Layout_PlacesTopLevelGroupsOnOneGlobalOrbit()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(graph);
            PackageGraphGroupLayoutNode[] topGroups = layout.GroupNodes
                .Where(groupNode => !groupNode.Collapsed)
                .OrderBy(groupNode => groupNode.Group.SortOrder)
                .ToArray();
            float globalRadius = Vector2.Distance(topGroups[0].HubCenter, PackageGraphLayout.GraphCenter);

            Assert.AreEqual(7, topGroups.Length);
            Assert.That(globalRadius, Is.GreaterThanOrEqualTo(560f));
            foreach (PackageGraphGroupLayoutNode groupNode in topGroups)
            {
                Assert.That(
                    Vector2.Distance(groupNode.HubCenter, PackageGraphLayout.GraphCenter),
                    Is.EqualTo(globalRadius).Within(0.1f),
                    groupNode.GroupId);
            }

            PackageGraphGroupLayoutNode infrastructure = topGroups.Single(groupNode => groupNode.GroupId == "infrastructure");
            foreach (string packageId in new[]
                     {
                         "com.deucarian.editor",
                         "com.deucarian.logging"
                     })
            {
                Assert.That(
                    Vector2.Distance(layout.NodeRects[packageId].center, infrastructure.HubCenter),
                    Is.EqualTo(Vector2.Distance(layout.NodeRects["com.deucarian.editor"].center, infrastructure.HubCenter)).Within(1.5f),
                    packageId);
            }

            AssertNoOverlaps(layout.NodeRects.Values.ToArray());
            AssertGroupClustersSeparated(graph, layout, 40f);
        }

        [Test]
        public void GraphCanvas_FitBoundsUseOnlyVisibleNodesAndHub()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            Vector2 compactViewport = new Vector2(900f, 620f);
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Overview,
                string.Empty,
                string.Empty,
                compactViewport,
                PackageGraphNodePresentationLevel.OverviewCompact);
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetViewportSize(compactViewport);
            canvas.SetGraph(graph, string.Empty, string.Empty, actionsEnabled: true);

            AssertRectsEqual(CreateExpectedFitBounds(layout), canvas.GetContentBounds(), 0.1f);
        }

        [Test]
        public void VisibilityFilter_UpdateAvailablePackagesCountAsInstalled()
        {
            PackageDefinition installed = CreatePackage("Installed", "com.example.installed", "Core");
            PackageDefinition update = CreatePackage("Update", "com.example.update", "Core");
            PackageDefinition absent = CreatePackage("Absent", "com.example.absent", "Core");
            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == installed.PackageId || packageId == update.PackageId,
                    _ => PackageChannel.Stable,
                    package => package.PackageId == update.PackageId
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
                .Build(new[] { installed, update, absent });
            PackageVisibilityFilterState installedOnly = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: true,
                showNotInstalled: false);

            HashSet<string> visibleIds = PackageVisibilityFilter.CreateVisiblePackageIdSet(graph, installedOnly);
            PackageVisibilityFilterCounts counts = PackageVisibilityFilter.CalculateCounts(graph, installedOnly);

            Assert.AreEqual(2, counts.InstalledCount);
            Assert.AreEqual(1, counts.NotInstalledCount);
            Assert.AreEqual(2, counts.VisibleCount);
            CollectionAssert.Contains(visibleIds, update.PackageId);
            CollectionAssert.DoesNotContain(visibleIds, absent.PackageId);
        }

        [Test]
        public void VisibilityFilter_CreateVisibleGraphRemovesHiddenNodesAndEdges()
        {
            PackageDefinition logging = CreatePackage("Logging", "com.example.logging", "Core");
            PackageDefinition session = CreatePackage(
                "Session",
                "com.example.session",
                "Core",
                dependencies: new[] { logging.PackageId });
            PackageGraphModel graph = new PackageGraphBuilder(packageId => packageId == logging.PackageId)
                .Build(new[] { logging, session });
            PackageVisibilityFilterState installedOnly = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: true,
                showNotInstalled: false);

            HashSet<string> visibleIds = PackageVisibilityFilter.CreateVisiblePackageIdSet(graph, installedOnly);
            PackageGraphModel visibleGraph = PackageVisibilityFilter.CreateVisibleGraph(graph, visibleIds);

            Assert.AreEqual(1, visibleGraph.Nodes.Count);
            Assert.AreEqual(logging.PackageId, visibleGraph.Nodes[0].PackageId);
            Assert.IsEmpty(visibleGraph.Edges);
        }

        [Test]
        public void GraphCanvas_FilteredOverviewRebuildsVisibleHierarchyAndFitBounds()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            Vector2 compactViewport = new Vector2(900f, 620f);
            HashSet<string> visibleIds = new HashSet<string>(
                new[]
                {
                    "com.deucarian.logging",
                    "com.deucarian.session"
                },
                StringComparer.OrdinalIgnoreCase);
            PackageGraphModel visibleGraph = PackageVisibilityFilter.CreateVisibleGraph(graph, visibleIds);
            PackageGraphLayoutResult visibleLayout = new PackageGraphLayout().Calculate(
                visibleGraph,
                PackageGraphLayoutMode.Overview,
                string.Empty,
                string.Empty,
                compactViewport,
                PackageGraphNodePresentationLevel.OverviewCompact);
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetViewportSize(compactViewport);
            canvas.SetGraph(graph, string.Empty, string.Empty, actionsEnabled: true, visibleIds);

            Assert.AreEqual(2, canvas.NodeRectsForTests.Count);
            AssertRectsEqual(
                visibleLayout.NodeRects["com.deucarian.logging"],
                canvas.NodeRectsForTests["com.deucarian.logging"],
                0.1f);
            AssertRectsEqual(
                visibleLayout.NodeRects["com.deucarian.session"],
                canvas.NodeRectsForTests["com.deucarian.session"],
                0.1f);
            Assert.IsFalse(canvas.NodeRectsForTests.ContainsKey("com.deucarian.api"));
            AssertRectsEqual(
                CreateExpectedFitBounds(visibleLayout, visibleIds),
                canvas.GetContentBounds(),
                0.1f);
        }

        [Test]
        public void VisibilityFilter_HidingFocusedPackageClearsFocusAndSelection()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            HashSet<string> visibleIds = new HashSet<string>(
                graph.Nodes
                    .Select(node => node.PackageId)
                    .Where(packageId => !string.Equals(
                        packageId,
                        "com.deucarian.session",
                        StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(
                graph,
                "com.deucarian.session",
                "com.deucarian.session",
                actionsEnabled: true,
                visibleIds);

            Assert.IsTrue(PackageInstallerWindow.ShouldClearGraphSelectionForFilters(
                "com.deucarian.session",
                "com.deucarian.session",
                visibleIds));
            Assert.AreEqual(PackageGraphLayoutMode.Overview, canvas.LayoutMode);
        }

        [Test]
        public void VisibilityFilter_CountsHiddenRelatedPackages()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            HashSet<string> visibleIds = new HashSet<string>(
                graph.Nodes
                    .Select(node => node.PackageId)
                    .Where(packageId => !string.Equals(
                        packageId,
                        "com.deucarian.logging",
                        StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

            int hiddenRelatedCount = PackageVisibilityFilter.CountHiddenRelatedPackages(
                graph,
                "com.deucarian.session",
                visibleIds);

            Assert.AreEqual(1, hiddenRelatedCount);
        }

        [Test]
        public void GraphView_FilterEmptyStateDistinguishesDisabledTogglesFromNoMatches()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState disabledState = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: false,
                showNotInstalled: false);
            PackageGraphView disabledView = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                disabledState,
                null);
            HashSet<string> disabledIds = PackageVisibilityFilter.CreateVisiblePackageIdSet(graph, disabledState);

            disabledView.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                disabledIds,
                PackageVisibilityFilter.CalculateCounts(graph, disabledState),
                hiddenRelatedCount: 0);

            Assert.IsEmpty(FindByClass(disabledView, "dpi-graph-node"));
            Assert.AreEqual(
                "No package visibility filters selected.",
                FindByClass(disabledView, "dpi-ecosystem-graph__empty-title")
                    .OfType<Label>()
                    .Single()
                    .text);

            PackageVisibilityFilterState searchState = new PackageVisibilityFilterState(
                "no-such-package",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphView searchView = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                searchState,
                null);
            HashSet<string> searchIds = PackageVisibilityFilter.CreateVisiblePackageIdSet(graph, searchState);

            searchView.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                searchIds,
                PackageVisibilityFilter.CalculateCounts(graph, searchState),
                hiddenRelatedCount: 0);

            Assert.AreEqual(
                "No packages match the current filters.",
                FindByClass(searchView, "dpi-ecosystem-graph__empty-title")
                    .OfType<Label>()
                    .Single()
                    .text);
        }

        [Test]
        public void GraphView_FilterChipsUseStatusIconsInsteadOfBracketMarkers()
        {
            PackageDefinition installed = CreatePackage("Installed", "com.example.installed", "Core");
            PackageDefinition notInstalled = CreatePackage("Not Installed", "com.example.not-installed", "Core");
            PackageGraphModel graph = new PackageGraphBuilder(packageId => packageId == installed.PackageId)
                .Build(new[] { installed, notInstalled });
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState();
            PackageGraphView view = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                filterState,
                null);

            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                null,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            Button[] filterButtons = FindByClass(view, "dpi-ecosystem-graph__filter-toggle")
                .OfType<Button>()
                .ToArray();
            Label installedIcon = FindByClass(view, "dpi-ecosystem-graph__filter-icon--installed")
                .OfType<Label>()
                .Single();
            Label notInstalledIcon = FindByClass(view, "dpi-ecosystem-graph__filter-icon--not-installed")
                .OfType<Label>()
                .Single();

            Assert.AreEqual(2, filterButtons.Length);
            Assert.IsFalse(filterButtons.Any(button => (button.text ?? string.Empty).Contains("[")));
            Assert.AreEqual("\u2713", installedIcon.text);
            Assert.AreEqual("\u25CB", notInstalledIcon.text);
            CollectionAssert.AreEquivalent(
                new[] { "Installed", "Not installed" },
                FindByClass(view, "dpi-ecosystem-graph__filter-label")
                    .OfType<Label>()
                    .Select(label => label.text)
                    .ToArray());
        }

        [Test]
        public void GraphView_OverviewLegendShowsOnlyStructuralItems()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            string[] labels = FindByClass(view, "dpi-graph-legend__label")
                .OfType<Label>()
                .Select(label => label.text)
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "Deucarian root", "Group", "Package", "Installed", "Not installed", "Attention" },
                labels);
            CollectionAssert.DoesNotContain(labels, "Dependency flow");
            CollectionAssert.DoesNotContain(labels, "Integration connection");
        }

        [Test]
        public void PresentationPolicy_UsesHysteresisForOverviewZoomLevels()
        {
            Assert.AreEqual(
                PackageGraphNodePresentationLevel.OverviewMicro,
                PackageGraphPresentationPolicy.ResolveForZoom(
                    PackageGraphLayoutMode.Overview,
                    0.60f,
                    PackageGraphNodePresentationLevel.OverviewMicro));
            Assert.AreEqual(
                PackageGraphNodePresentationLevel.OverviewCompact,
                PackageGraphPresentationPolicy.ResolveForZoom(
                    PackageGraphLayoutMode.Overview,
                    0.76f,
                    PackageGraphNodePresentationLevel.OverviewCompact));
            Assert.AreEqual(
                PackageGraphNodePresentationLevel.Standard,
                PackageGraphPresentationPolicy.ResolveForZoom(
                    PackageGraphLayoutMode.Overview,
                    1.24f,
                    PackageGraphNodePresentationLevel.Standard));
            Assert.AreEqual(
                PackageGraphNodePresentationLevel.OverviewCompact,
                PackageGraphPresentationPolicy.ResolveForZoom(
                    PackageGraphLayoutMode.Overview,
                    1.24f,
                    PackageGraphNodePresentationLevel.OverviewCompact));
        }

        [Test]
        public void PresentationMetrics_UseMateriallyDifferentFootprints()
        {
            PackageGraphNodeMetrics micro =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.OverviewMicro);
            PackageGraphNodeMetrics compact =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.OverviewCompact);
            PackageGraphNodeMetrics standard =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Standard);
            PackageGraphNodeMetrics full =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Full);

            Assert.Less(micro.Width, compact.Width);
            Assert.Less(compact.Width, standard.Width);
            Assert.Less(standard.Width, full.Width);
            Assert.Less(micro.Height, compact.Height);
            Assert.Less(compact.Height, standard.Height);
            Assert.Less(standard.Height, full.Height);
        }

        [Test]
        public void GraphViewport_SemanticZoomClassesTrackZoomThresholds()
        {
            PackageGraphViewport viewport = new PackageGraphViewport(null);
            VisualElement contentRoot = viewport.Q<VisualElement>("ecosystem-graph-content");
            System.Reflection.FieldInfo zoomField = typeof(PackageGraphViewport).GetField(
                "_zoom",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo applyTransform = typeof(PackageGraphViewport).GetMethod(
                "ApplyTransform",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            zoomField.SetValue(viewport, 0.50f);
            applyTransform.Invoke(viewport, null);

            Assert.IsTrue(contentRoot.ClassListContains("dpi-ecosystem-graph__content--low-zoom"));

            zoomField.SetValue(viewport, 0.80f);
            applyTransform.Invoke(viewport, null);

            Assert.IsTrue(contentRoot.ClassListContains("dpi-ecosystem-graph__content--medium-zoom"));

            zoomField.SetValue(viewport, 1.20f);
            applyTransform.Invoke(viewport, null);

            Assert.IsTrue(contentRoot.ClassListContains("dpi-ecosystem-graph__content--high-zoom"));
        }

        [Test]
        public void GraphViewport_LeftPanPolicyAllowsBackgroundAndRootOnly()
        {
            VisualElement packageNode = new VisualElement();
            packageNode.AddToClassList("dpi-graph-node");
            VisualElement groupNode = new VisualElement();
            groupNode.AddToClassList("dpi-graph-group");
            VisualElement hub = new VisualElement();
            hub.AddToClassList("dpi-graph-hub");
            VisualElement hubChild = new VisualElement();
            hub.Add(hubChild);
            VisualElement canvas = new VisualElement();
            canvas.AddToClassList("dpi-ecosystem-graph__canvas");

            Assert.IsFalse(PackageGraphViewport.IsLeftPanTargetForTests(packageNode));
            Assert.IsFalse(PackageGraphViewport.IsLeftPanTargetForTests(groupNode));
            Assert.IsTrue(PackageGraphViewport.IsLeftPanTargetForTests(hubChild));
            Assert.IsTrue(PackageGraphViewport.IsLeftPanTargetForTests(canvas));
        }

        [Test]
        public void GraphViewport_EffectiveMinimumZoomAllowsFitBelowNormalClamp()
        {
            Assert.That(
                PackageGraphViewport.CalculateEffectiveMinZoomForTests(0.35f, 0.18f),
                Is.EqualTo(0.18f).Within(0.001f));
            Assert.That(
                PackageGraphViewport.CalculateEffectiveMinZoomForTests(0.35f, 0.04f),
                Is.EqualTo(0.10f).Within(0.001f));
            Assert.That(
                PackageGraphViewport.CalculateEffectiveMinZoomForTests(0.35f, 0.90f),
                Is.EqualTo(0.35f).Within(0.001f));
        }

        [Test]
        public void GraphView_DisablesSelectedNodeActionDuringLayoutTransition()
        {
            PackageDefinition package = CreatePackage("Installable", "com.example.installable", "Core");
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(new[] { package });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);
            view.SetGraph(graph, package.PackageId, actionsEnabled: true);

            Button selectedAction = FindByClass(view, "dpi-graph-node__action")
                .OfType<Button>()
                .Single();
            Assert.AreEqual("Install", selectedAction.text);
            Assert.IsFalse(selectedAction.enabledSelf);
        }

        [Test]
        public void GraphView_ShowsActionsOnlyForFocusedRelationshipContext()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView overview = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView focused = new PackageGraphView(_ => { }, (_, __) => { });

            overview.SetGraph(graph, string.Empty, actionsEnabled: true);
            focused.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            Assert.IsEmpty(FindByClass(overview, "dpi-graph-node__action"));
            Assert.AreEqual(
                "Install",
                FindGraphNodeAction(focused, "com.deucarian.session").text);
            Assert.AreEqual(
                "Install",
                FindGraphNodeAction(focused, "com.deucarian.logging").text);
            Assert.AreEqual(
                "Install Integration",
                FindGraphNodeAction(focused, "com.deucarian.session.api-integration").text);
            Assert.AreEqual(graph.Nodes.Count, FindByClass(overview, "dpi-graph-node--overview").Count);
            Assert.Less(FindByClass(focused, "dpi-graph-node").Count, graph.Nodes.Count);
            Assert.IsEmpty(FindByClass(overview, "dpi-graph-node__package-id"));
            Assert.IsEmpty(FindByClass(overview, "dpi-graph-unrelated-summary"));
            Assert.IsFalse(HasGraphNode(focused, "com.deucarian.theming"));
            Assert.IsTrue(FindByClass(focused, "dpi-graph-group--collapsed").Count > 0);
            Assert.IsTrue(
                FindByClass(focused, "dpi-graph-group__subtitle")
                    .OfType<Label>()
                    .Any(label => label.text.Contains("related package")));
            Assert.IsTrue(FindGraphNode(focused, "com.deucarian.logging").ClassListContains("dpi-graph-node--focus"));
        }

        [Test]
        public void GraphView_GroupFocusShowsGroupCardsWithoutPackageActions()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView groupFocused = new PackageGraphView(_ => { }, (_, __) => { });

            groupFocused.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "infrastructure",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);

            Assert.AreEqual(1, FindByClass(groupFocused, "dpi-graph-group--focused").Count);
            Assert.IsEmpty(FindByClass(groupFocused, "dpi-graph-node__action"));
            Assert.IsTrue(FindGraphNode(groupFocused, "com.deucarian.logging").ClassListContains("dpi-graph-node--overview"));
            Assert.IsTrue(
                FindByClass(groupFocused, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Structural membership"));
            Assert.IsFalse(
                FindByClass(groupFocused, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Dependency flow"));
        }

        [Test]
        public void GraphView_GroupHubsUseCircularSymbolsWithExternalLabels()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            VisualElement runtimeGroup = FindByClass(view, "dpi-graph-group")
                .Single(element => element.name == "group-runtime-services");
            VisualElement symbol = FindByClass(runtimeGroup, "dpi-graph-group__symbol").Single();

            Assert.That(
                symbol.style.width.value.value,
                Is.EqualTo(symbol.style.height.value.value).Within(0.1f));
            Assert.IsEmpty(FindByClass(symbol, "dpi-graph-group__title"));
            Assert.IsTrue(
                FindByClass(runtimeGroup, "dpi-graph-group__title")
                    .OfType<Label>()
                    .Any(label => label.text == "Runtime Services"));
            Assert.IsEmpty(FindByClass(runtimeGroup, "dpi-graph-node__action"));
        }

        [Test]
        public void GraphView_CategoryRailStaysVisibleAndHighlightsCurrentContext()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView overview = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView groupFocused = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView focused = new PackageGraphView(_ => { }, (_, __) => { });

            overview.SetGraph(graph, string.Empty, actionsEnabled: true);
            groupFocused.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "infrastructure",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);
            focused.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            Assert.AreEqual(8, FindByClass(overview, "dpi-category-rail__item").Count);
            Assert.IsTrue(
                FindByClass(overview, "dpi-category-rail__item")
                    .Single(item => item.name == "category-rail-overview")
                    .ClassListContains("dpi-category-rail__item--active"));
            Assert.AreEqual(8, FindByClass(groupFocused, "dpi-category-rail__item").Count);
            Assert.IsTrue(
                FindByClass(groupFocused, "dpi-category-rail__item")
                    .Single(item => item.name == "category-rail-infrastructure")
                    .ClassListContains("dpi-category-rail__item--active"));
            Assert.AreEqual(8, FindByClass(focused, "dpi-category-rail__item").Count);
            Assert.IsTrue(
                FindByClass(focused, "dpi-category-rail__item")
                    .Single(item => item.name == "category-rail-runtime-services")
                    .ClassListContains("dpi-category-rail__item--active"));
        }

        [Test]
        public void GraphView_ResponsiveModeClassesAreStable()
        {
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetResponsiveMode(PackageInstallerResponsiveMode.Compact);

            Assert.IsTrue(view.ClassListContains("dpi-ecosystem-graph--compact"));
            Assert.IsFalse(view.ClassListContains("dpi-ecosystem-graph--wide"));
            Assert.IsFalse(view.ClassListContains("dpi-ecosystem-graph--narrow"));

            view.SetResponsiveMode(PackageInstallerResponsiveMode.Narrow);

            Assert.IsTrue(view.ClassListContains("dpi-ecosystem-graph--narrow"));
            Assert.IsFalse(view.ClassListContains("dpi-ecosystem-graph--compact"));
        }

        [Test]
        public void GraphView_BreadcrumbShowsCurrentSegmentWithoutClickablePill()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView groupFocused = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView packageFocused = new PackageGraphView(_ => { }, (_, __) => { });

            groupFocused.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "runtime-services",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);
            packageFocused.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            Assert.IsTrue(
                FindByClass(groupFocused, "dpi-ecosystem-graph__breadcrumb-current")
                    .OfType<Label>()
                    .Any(label => label.text == "Runtime Services"));
            Assert.IsFalse(
                FindByClass(groupFocused, "dpi-ecosystem-graph__breadcrumb")
                    .OfType<Button>()
                    .Any(button => button.text == "Runtime Services"));
            Assert.IsTrue(
                FindByClass(packageFocused, "dpi-ecosystem-graph__breadcrumb-current")
                    .OfType<Label>()
                    .Any(label => label.text == "Deucarian Session"));
            Assert.IsTrue(
                FindByClass(packageFocused, "dpi-ecosystem-graph__breadcrumb")
                    .OfType<Button>()
                    .Any(button => button.text == "Runtime Services"));
            Assert.IsTrue(
                FindByClass(packageFocused, "dpi-ecosystem-graph__breadcrumb-separator")
                    .OfType<Label>()
                    .All(label => label.text == ">"));
        }

        [Test]
        public void GraphView_PackageFocusShowsPlainCategoryPath()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            Assert.IsTrue(
                FindByClass(FindGraphNode(view, "com.deucarian.session"), "dpi-graph-node__category-path")
                    .OfType<Label>()
                    .Any(label => label.text == "Runtime Services"));
        }

        [Test]
        public void HierarchyDisplay_SeparatesStructuralCategoryFromPackageKind()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            AssertCategoryAndKind(graph, "com.deucarian.api", "Runtime Services", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.session", "Runtime Services", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.core-state", "State & Data", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.package-installer", "Tools & Quality", "Tool");
            AssertCategoryAndKind(graph, "com.deucarian.session.api-integration", "Integrations", "Integration");
            AssertCategoryAndKind(graph, "com.deucarian.selection-suite", "Suites", "Suite");
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
            PackageDefinition companion = CreatePackage("Companion", "com.example.companion", "UI", "OptionalIntegration");
            PackageDefinition integration = CreatePackage(
                "Integration",
                "com.example.integration",
                "Integration",
                "Integration",
                integrationTargets: new[] { core.PackageId, companion.PackageId });

            PackageGraphModel graph = new PackageGraphBuilder(packageId => packageId == core.PackageId)
                .Build(new[] { core, companion, integration });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, core.PackageId, actionsEnabled: true);

            Assert.IsNull(view.Q<ScrollView>());
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-viewport"));
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-content"));
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-membership-layer"));
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-edge-layer"));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Dependency flow"));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__item")
                    .Any(item => item.tooltip.Contains("Animated flow markers")));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Integration connection"));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Optional companion"));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__item")
                    .Any(item => item.tooltip == "Recommended alongside, not required"));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Suite membership"));
            Assert.AreEqual(2, FindByClass(view, "dpi-graph-node").Count);
            Assert.AreEqual(1, FindByClass(view, "dpi-graph-group--collapsed").Count);
            Assert.AreEqual(1, FindByClass(view, "dpi-graph-node--integration").Count);
            Assert.IsTrue(FindByClass(view, "dpi-graph-node__action").Count >= 1);
        }

        [Test]
        public void GraphEdges_TreatOptionalCompanionsAsNonDirectional()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphEdge optionalEdge = graph.Edges.Single(edge =>
                edge.Kind == PackageGraphEdgeKind.OptionalCompanion &&
                edge.FromPackageId == "com.deucarian.object-loading" &&
                edge.ToPackageId == "com.deucarian.diagnostics");

            Assert.AreEqual("Optional companion", optionalEdge.Label);
            Assert.IsFalse(PackageGraphEdgeLayer.AnimatesEdgeForTests(PackageGraphEdgeKind.OptionalCompanion));
            Assert.IsFalse(PackageGraphEdgeLayer.UsesDirectionalFlowMarkersForTests(PackageGraphEdgeKind.OptionalCompanion));
            Assert.IsTrue(PackageGraphEdgeLayer.AnimatesEdgeForTests(PackageGraphEdgeKind.HardDependency));
            Assert.IsTrue(PackageGraphEdgeLayer.UsesDirectionalFlowMarkersForTests(PackageGraphEdgeKind.HardDependency));
            Assert.IsTrue(PackageGraphEdgeLayer.AnimatesEdgeForTests(PackageGraphEdgeKind.IntegrationConnection));
            Assert.IsTrue(PackageGraphEdgeLayer.UsesDirectionalFlowMarkersForTests(PackageGraphEdgeKind.IntegrationConnection));
        }

        [Test]
        public void Layout_CalculatesHierarchicalOverviewWithoutNodeOverlap()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(graph);
            PackageGraphNodeMetrics microMetrics =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.OverviewMicro);

            Assert.AreEqual(PackageGraphLayoutMode.Overview, layout.Mode);
            Assert.AreEqual(graph.Nodes.Count, layout.NodeRects.Count);
            Assert.AreEqual(1, layout.RingGuides.Count);
            Assert.IsEmpty(layout.SectorLabels);
            Assert.AreEqual(7, layout.GroupNodes.Count(groupNode => !groupNode.Collapsed));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "infrastructure",
                    "state-data",
                    "runtime-services",
                    "experience-interaction",
                    "tools-quality",
                    "integrations",
                    "suites"
                },
                layout.GroupNodes
                    .Where(groupNode => !groupNode.Collapsed)
                    .Select(groupNode => groupNode.GroupId)
                    .ToArray());
            Assert.AreEqual(
                PackageGraphLayoutRing.Infrastructure,
                layout.NodeRings["com.deucarian.logging"]);
            Assert.AreEqual(
                PackageGraphLayoutRing.Runtime,
                layout.NodeRings["com.deucarian.object-loading"]);
            Assert.AreEqual(
                PackageGraphLayoutRing.Runtime,
                layout.NodeRings["com.deucarian.diagnostics"]);
            Assert.AreEqual(
                PackageGraphLayoutRing.Runtime,
                layout.NodeRings["com.deucarian.package-installer"]);
            Assert.IsTrue(layout.NodePresentationLevels.Values.All(level =>
                level == PackageGraphNodePresentationLevel.OverviewMicro));
            Assert.That(layout.NodeRects["com.deucarian.session"].width, Is.EqualTo(microMetrics.Width).Within(0.1f));
            Assert.That(layout.NodeRects["com.deucarian.session"].height, Is.EqualTo(microMetrics.Height).Within(0.1f));
            PackageGraphGroupLayoutNode infrastructure = layout.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");
            PackageGraphGroupLayoutNode integrations = layout.GroupNodes.Single(groupNode => groupNode.GroupId == "integrations");
            float globalRadius = Vector2.Distance(infrastructure.HubCenter, PackageGraphLayout.GraphCenter);
            Assert.That(globalRadius, Is.GreaterThanOrEqualTo(560f));
            Assert.That(Vector2.Distance(integrations.HubCenter, PackageGraphLayout.GraphCenter), Is.EqualTo(globalRadius).Within(0.1f));
            Assert.That(
                Vector2.Distance(layout.NodeRects["com.deucarian.editor"].center, infrastructure.HubCenter),
                Is.EqualTo(Vector2.Distance(layout.NodeRects["com.deucarian.logging"].center, infrastructure.HubCenter)).Within(1.5f));
            Assert.That(
                Vector2.Distance(layout.NodeRects["com.deucarian.session.api-integration"].center, integrations.HubCenter),
                Is.EqualTo(Vector2.Distance(layout.NodeRects["com.deucarian.object-loading.api-integration"].center, integrations.HubCenter)).Within(1.5f));
            AssertNoOverlaps(layout.NodeRects.Values.Concat(layout.GroupNodes.Select(groupNode => groupNode.Rect)).ToArray());
            AssertGroupClustersSeparated(graph, layout, 40f);
        }

        [Test]
        public void Layout_CompactOverviewUsesSmallerRootOrbitThanStandardCards()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayout layout = new PackageGraphLayout();

            PackageGraphLayoutResult compact = layout.Calculate(
                graph,
                PackageGraphLayoutMode.Overview,
                string.Empty,
                string.Empty,
                Vector2.zero,
                PackageGraphNodePresentationLevel.OverviewCompact);
            PackageGraphLayoutResult standard = layout.Calculate(
                graph,
                PackageGraphLayoutMode.Overview,
                string.Empty,
                string.Empty,
                Vector2.zero,
                PackageGraphNodePresentationLevel.Standard);
            PackageGraphGroupLayoutNode compactInfrastructure =
                compact.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");
            PackageGraphGroupLayoutNode standardInfrastructure =
                standard.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");

            Assert.Less(
                Vector2.Distance(compactInfrastructure.HubCenter, PackageGraphLayout.GraphCenter),
                Vector2.Distance(standardInfrastructure.HubCenter, PackageGraphLayout.GraphCenter));
            Assert.Less(compactInfrastructure.OrbitRadius, standardInfrastructure.OrbitRadius);
            AssertGroupClustersSeparated(graph, compact, 40f);
            AssertGroupClustersSeparated(graph, standard, 40f);
        }

        [Test]
        public void Layout_FocusUsesFullSelectedCardAndStandardRelatedCards()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.session");
            PackageGraphNodeMetrics fullMetrics =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Full);
            PackageGraphNodeMetrics standardMetrics =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Standard);

            Assert.AreEqual(PackageGraphNodePresentationLevel.Full, layout.NodePresentationLevels["com.deucarian.session"]);
            Assert.AreEqual(PackageGraphNodePresentationLevel.Standard, layout.NodePresentationLevels["com.deucarian.logging"]);
            Assert.That(layout.NodeRects["com.deucarian.session"].width, Is.EqualTo(fullMetrics.Width).Within(0.1f));
            Assert.That(layout.NodeRects["com.deucarian.session"].height, Is.EqualTo(fullMetrics.Height).Within(0.1f));
            Assert.That(layout.NodeRects["com.deucarian.logging"].width, Is.EqualTo(standardMetrics.Width).Within(0.1f));
            Assert.That(layout.NodeRects["com.deucarian.logging"].height, Is.EqualTo(standardMetrics.Height).Within(0.1f));
        }

        [Test]
        public void Layout_StoresPerfectVisibleOrbitRadiusSeparateFromRootGuide()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(graph);

            Assert.AreEqual(1, layout.RingGuides.Count);

            foreach (PackageGraphGroupLayoutNode groupNode in layout.GroupNodes.Where(groupNode => !groupNode.Collapsed))
            {
                Assert.That(groupNode.HubRect.width, Is.EqualTo(groupNode.HubRect.height).Within(0.1f));

                Rect[] directChildRects = graph.Nodes
                    .Where(node => string.Equals(node.GroupId, groupNode.GroupId, StringComparison.OrdinalIgnoreCase))
                    .Where(node => layout.NodeRects.ContainsKey(node.PackageId))
                    .Select(node => layout.NodeRects[node.PackageId])
                    .ToArray();

                if (directChildRects.Length == 0)
                {
                    Assert.AreEqual(0f, groupNode.OrbitRadius);
                    continue;
                }

                Assert.Greater(groupNode.OrbitRadius, 0f);

                foreach (Rect childRect in directChildRects)
                {
                    Assert.That(
                        Vector2.Distance(childRect.center, groupNode.HubCenter),
                        Is.EqualTo(groupNode.OrbitRadius).Within(0.1f));
                }
            }
        }

        [Test]
        public void Layout_RootOverviewSemanticLevelsRecalculateStableNonOverlappingOrbits()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayout layout = new PackageGraphLayout();
            PackageGraphNodePresentationLevel[] levels =
            {
                PackageGraphNodePresentationLevel.OverviewMicro,
                PackageGraphNodePresentationLevel.OverviewCompact,
                PackageGraphNodePresentationLevel.Standard
            };
            List<float> rootRadii = new List<float>();

            foreach (PackageGraphNodePresentationLevel level in levels)
            {
                PackageGraphLayoutResult result = layout.Calculate(
                    graph,
                    PackageGraphLayoutMode.Overview,
                    string.Empty,
                    string.Empty,
                    new Vector2(900f, 620f),
                    level);
                PackageGraphNodeMetrics metrics = PackageGraphPresentationPolicy.GetMetrics(level);
                PackageGraphGroupLayoutNode[] topGroups = result.GroupNodes
                    .Where(groupNode => !groupNode.Collapsed)
                    .OrderBy(groupNode => groupNode.Group.SortOrder)
                    .ToArray();
                float rootRadius = Vector2.Distance(topGroups[0].HubCenter, PackageGraphLayout.GraphCenter);

                rootRadii.Add(rootRadius);
                Assert.AreEqual(graph.Nodes.Count, result.NodeRects.Count);
                Assert.IsTrue(result.NodePresentationLevels.Values.All(activeLevel => activeLevel == level));
                Assert.That(result.NodeRects["com.deucarian.session"].width, Is.EqualTo(metrics.Width).Within(0.1f));
                Assert.That(result.NodeRects["com.deucarian.session"].height, Is.EqualTo(metrics.Height).Within(0.1f));
                AssertNoOverlaps(result.NodeRects.Values.Concat(result.GroupNodes.Select(groupNode => groupNode.Rect)).ToArray());
                AssertGroupClustersSeparated(graph, result, 40f);

                foreach (PackageGraphGroupLayoutNode groupNode in topGroups)
                {
                    Assert.That(Vector2.Distance(groupNode.HubRect.center, groupNode.HubCenter), Is.LessThan(0.01f));
                    Assert.That(groupNode.HubRect.width, Is.EqualTo(groupNode.HubRect.height).Within(0.1f));

                    Rect[] directChildRects = graph.Nodes
                        .Where(node => string.Equals(node.GroupId, groupNode.GroupId, StringComparison.OrdinalIgnoreCase))
                        .Where(node => result.NodeRects.ContainsKey(node.PackageId))
                        .Select(node => result.NodeRects[node.PackageId])
                        .ToArray();

                    if (directChildRects.Length == 0)
                    {
                        continue;
                    }

                    foreach (Rect childRect in directChildRects)
                    {
                        Assert.That(
                            Vector2.Distance(childRect.center, groupNode.HubCenter),
                            Is.EqualTo(groupNode.OrbitRadius).Within(0.1f),
                            groupNode.GroupId);
                    }
                }

                Rect activeBounds = PackageGraphActiveLayoutBounds.Calculate(result);
                foreach (Rect rect in result.NodeRects.Values.Concat(result.GroupNodes.Select(groupNode => groupNode.Rect)))
                {
                    Assert.IsTrue(activeBounds.Contains(rect.min), rect + " min outside active bounds");
                    Assert.IsTrue(activeBounds.Contains(rect.max), rect + " max outside active bounds");
                }
            }

            Assert.LessOrEqual(rootRadii[0], rootRadii[1]);
            Assert.LessOrEqual(rootRadii[1], rootRadii[2]);
        }

        [Test]
        public void Layout_GroupFocusCentersGroupAndExpandsOnlyDirectChildren()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                "infrastructure",
                Vector2.zero);
            PackageGraphGroupLayoutNode focusedGroup = layout.GroupNodes.Single(groupNode =>
                groupNode.GroupId == "infrastructure" && groupNode.Focused);

            Assert.AreEqual(PackageGraphLayoutMode.GroupFocus, layout.Mode);
            Assert.AreEqual("infrastructure", layout.FocusGroupId);
            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, focusedGroup.HubCenter), Is.LessThan(0.1f));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "com.deucarian.editor",
                    "com.deucarian.logging"
                },
                layout.NodeRects.Keys.ToArray());
            Assert.IsFalse(layout.GroupNodes.Any(groupNode => groupNode.Collapsed && groupNode.GroupId == "runtime-services"));
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.session"));
            AssertNoOverlaps(layout.NodeRects.Values.Concat(layout.GroupNodes.Select(groupNode => groupNode.Rect)).ToArray());
        }

        [Test]
        public void Layout_CalculatesEgoFocusPositionsAroundSelectedPackage()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.session");

            Rect session = layout.NodeRects["com.deucarian.session"];
            Rect logging = layout.NodeRects["com.deucarian.logging"];
            Rect sessionIntegration = layout.NodeRects["com.deucarian.session.api-integration"];

            Assert.AreEqual(PackageGraphLayoutMode.Focus, layout.Mode);
            Assert.AreEqual("com.deucarian.session", layout.FocusPackageId);
            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, session.center), Is.LessThan(0.1f));
            Assert.Less(logging.center.x, session.center.x);
            Assert.That(logging.center.x, Is.EqualTo(PackageGraphLayout.GraphCenter.x - 455f).Within(0.1f));
            Assert.That(logging.center.y, Is.EqualTo(PackageGraphLayout.GraphCenter.y).Within(0.1f));
            Assert.That(sessionIntegration.center.x, Is.EqualTo(PackageGraphLayout.GraphCenter.x).Within(0.1f));
            Assert.That(sessionIntegration.center.y, Is.EqualTo(PackageGraphLayout.GraphCenter.y + 365f).Within(0.1f));
            Assert.Greater(sessionIntegration.center.y, session.center.y);
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.api"));
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.theming"));
            Assert.IsFalse(layout.HasUnrelatedSummary);
            Assert.IsTrue(layout.GroupNodes.Any(groupNode => groupNode.Collapsed && groupNode.SummaryLabel.Contains("related package")));
            PackageGraphGroupLayoutNode integrationContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "integrations");
            CollectionAssert.Contains(
                integrationContext.RepresentedPackageIds.ToArray(),
                "com.deucarian.session.api-integration");
            Assert.AreEqual(layout.ActiveCenter, session.center);
            Assert.IsEmpty(layout.RingGuides);
            AssertNoOverlaps(layout.NodeRects.Values.Concat(layout.GroupNodes.Select(groupNode => groupNode.Rect)).ToArray());
        }

        [Test]
        public void Layout_CentersLoggingAndStacksDependentsInRightColumn()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.logging");

            Rect logging = layout.NodeRects["com.deucarian.logging"];
            Rect editor = layout.NodeRects["com.deucarian.editor"];

            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, logging.center), Is.LessThan(0.1f));
            Assert.Less(editor.center.x, logging.center.x);
            foreach (string packageId in new[]
                     {
                         "com.deucarian.api",
                         "com.deucarian.object-loading",
                         "com.deucarian.session",
                         "com.deucarian.object-selection",
                         "com.deucarian.theming",
                         "com.deucarian.diagnostics",
                         "com.deucarian.package-installer"
                     })
            {
                Rect dependent = layout.NodeRects[packageId];
                Assert.Greater(dependent.center.x, logging.center.x);
                Assert.That(dependent.center.x, Is.EqualTo(PackageGraphLayout.GraphCenter.x + 455f).Within(0.1f));
            }

            Assert.IsFalse(layout.HasUnrelatedSummary);
            Assert.IsTrue(layout.GroupNodes.Any(groupNode => groupNode.Collapsed));
            PackageGraphGroupLayoutNode runtimeContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "runtime-services");
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "com.deucarian.api",
                    "com.deucarian.object-loading",
                    "com.deucarian.session"
                },
                runtimeContext.RepresentedPackageIds.ToArray());
            AssertNoOverlaps(layout.NodeRects.Values.Concat(layout.GroupNodes.Select(groupNode => groupNode.Rect)).ToArray());
        }

        [Test]
        public void Layout_InvalidFocusFallsBackToOverview()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.missing");

            Assert.AreEqual(PackageGraphLayoutMode.Overview, layout.Mode);
            Assert.IsEmpty(layout.FocusPackageId);
        }

        [Test]
        public void Focus_SelectingObjectLoadingShowsDependencyIntegrationAndCompanionContext()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphFocus focus = PackageGraphFocus.Create(
                graph,
                "com.deucarian.object-loading");

            Assert.IsTrue(focus.IsPackageRelated("com.deucarian.logging"));
            Assert.IsTrue(focus.IsPackageRelated("com.deucarian.object-loading.api-integration"));
            Assert.IsFalse(focus.IsPackageRelated("com.deucarian.api"));
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
                "com.deucarian.object-loading.api-integration",
                PackageGraphEdgeKind.HardDependency);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading.api-integration",
                "com.deucarian.object-loading",
                PackageGraphEdgeKind.IntegrationConnection);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.diagnostics",
                PackageGraphEdgeKind.OptionalCompanion);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.object-loading.api-integration",
                PackageGraphEdgeKind.OptionalCompanion);

            PackageGraphEdge apiRequirementEdge = graph.Edges.Single(edge =>
                edge.FromPackageId == "com.deucarian.api" &&
                edge.ToPackageId == "com.deucarian.object-loading.api-integration" &&
                edge.Kind == PackageGraphEdgeKind.HardDependency);
            Assert.IsFalse(focus.IsEdgeVisible(apiRequirementEdge));

            PackageGraphEdge apiIntegrationEdge = graph.Edges.Single(edge =>
                edge.FromPackageId == "com.deucarian.object-loading.api-integration" &&
                edge.ToPackageId == "com.deucarian.api" &&
                edge.Kind == PackageGraphEdgeKind.IntegrationConnection);
            Assert.IsFalse(focus.IsEdgeVisible(apiIntegrationEdge));

            PackageGraphEdge unrelatedThemingEdge = graph.Edges.Single(edge =>
                edge.FromPackageId == "com.deucarian.editor" &&
                edge.ToPackageId == "com.deucarian.theming");
            Assert.IsFalse(focus.IsEdgeVisible(unrelatedThemingEdge));
        }

        [Test]
        public void Focus_SelectingIntegrationShowsHardRequirementsAndIntegrationConnectionsSeparately()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphFocus focus = PackageGraphFocus.Create(
                graph,
                "com.deucarian.object-loading.api-integration");

            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.object-loading.api-integration",
                PackageGraphEdgeKind.HardDependency);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.api",
                "com.deucarian.object-loading.api-integration",
                PackageGraphEdgeKind.HardDependency);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading.api-integration",
                "com.deucarian.object-loading",
                PackageGraphEdgeKind.IntegrationConnection);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading.api-integration",
                "com.deucarian.api",
                PackageGraphEdgeKind.IntegrationConnection);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.object-loading.api-integration",
                PackageGraphEdgeKind.OptionalCompanion);
        }

        [Test]
        public void Focus_ShowsSuiteMembershipEdgesWithoutSuiteRegions()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphSuiteRegion selectionSuite = graph.SuiteRegions.Single(region =>
                region.SuitePackageId == "com.deucarian.selection-suite");
            PackageGraphEdge suiteMembershipEdge = graph.Edges.Single(edge =>
                edge.Kind == PackageGraphEdgeKind.SuiteMembership &&
                edge.FromPackageId == selectionSuite.SuitePackageId &&
                edge.ToPackageId == "com.deucarian.ui-binding");

            PackageGraphFocus overview = PackageGraphFocus.Create(graph, string.Empty);

            Assert.IsFalse(overview.IsSuiteRegionVisible(selectionSuite));
            Assert.IsFalse(overview.IsEdgeVisible(suiteMembershipEdge));

            PackageGraphFocus focusedSuite = PackageGraphFocus.Create(
                graph,
                selectionSuite.SuitePackageId);

            Assert.IsFalse(focusedSuite.IsSuiteRegionVisible(selectionSuite));
            Assert.IsTrue(focusedSuite.IsEdgeVisible(suiteMembershipEdge));
            Assert.IsTrue(focusedSuite.IsPackageRelated("com.deucarian.object-selection.core-state-integration"));

            PackageGraphLayoutResult suiteLayout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                selectionSuite.SuitePackageId);
            Rect suiteRect = suiteLayout.NodeRects[selectionSuite.SuitePackageId];

            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, suiteRect.center), Is.LessThan(0.1f));
            Assert.IsEmpty(suiteLayout.RingGuides);
            Assert.IsEmpty(suiteLayout.SectorLabels);
            Assert.IsFalse(suiteLayout.HasUnrelatedSummary);
            Assert.IsTrue(suiteLayout.GroupNodes.Any(groupNode => groupNode.Collapsed));
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
            PackageGraphFocus overview = PackageGraphFocus.Create(graph, string.Empty);
            PackageGraphEdge dependencyEdge = graph.Edges.Single(edge =>
                edge.Kind == PackageGraphEdgeKind.HardDependency &&
                edge.FromPackageId == editor.PackageId &&
                edge.ToPackageId == logging.PackageId);

            Assert.IsFalse(overview.IsEdgeVisible(dependencyEdge));

            PackageGraphFocus focused = PackageGraphFocus.Create(graph, logging.PackageId);

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
            string[] integrationTargets = null,
            string[] suiteMembers = null,
            string[] optionalCompanions = null,
            string[] recommendedWith = null,
            string ecosystemGroup = null,
            string groupId = null)
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
                integrationTargets: integrationTargets,
                suiteMembers: suiteMembers,
                optionalCompanions: optionalCompanions,
                recommendedWith: recommendedWith,
                ecosystemGroup: ecosystemGroup,
                groupId: groupId);
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
                        "com.deucarian.session.api-integration",
                        "com.deucarian.object-loading.api-integration"
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
                    optionalIntegrations: new[] { "com.deucarian.object-loading.api-integration" },
                    optionalCompanions: new[]
                    {
                        "com.deucarian.diagnostics",
                        "com.deucarian.object-loading.api-integration"
                    }),
                CreatePackage(
                    "Deucarian Session",
                    "com.deucarian.session",
                    "Core",
                    dependencies: new[] { "com.deucarian.logging" },
                    optionalIntegrations: new[] { "com.deucarian.session.api-integration" }),
                CreatePackage(
                    "Deucarian UI Binding",
                    "com.deucarian.ui-binding",
                    "UI",
                    "OptionalIntegration",
                    optionalIntegrations: new[] { "com.deucarian.ui-binding.core-state-integration" },
                    recommendedWith: new[] { "com.deucarian.selection-suite" }),
                CreatePackage(
                    "Deucarian Object Selection",
                    "com.deucarian.object-selection",
                    "World",
                    "OptionalIntegration",
                    dependencies: new[] { "com.deucarian.logging" },
                    optionalIntegrations: new[] { "com.deucarian.object-selection.core-state-integration" },
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
                    "Deucarian Session API Integration",
                    "com.deucarian.session.api-integration",
                    "Integration",
                    "Integration",
                    dependencies: new[]
                    {
                        "com.deucarian.session",
                        "com.deucarian.api"
                    },
                    integrationTargets: new[]
                    {
                        "com.deucarian.session",
                        "com.deucarian.api"
                    }),
                CreatePackage(
                    "Deucarian Object Loading API Integration",
                    "com.deucarian.object-loading.api-integration",
                    "Integration",
                    "Integration",
                    dependencies: new[]
                    {
                        "com.deucarian.api",
                        "com.deucarian.object-loading"
                    },
                    integrationTargets: new[]
                    {
                        "com.deucarian.object-loading",
                        "com.deucarian.api"
                    }),
                CreatePackage(
                    "Deucarian UI Binding Core State Integration",
                    "com.deucarian.ui-binding.core-state-integration",
                    "Integration",
                    "Integration",
                    dependencies: new[]
                    {
                        "com.deucarian.ui-binding",
                        "com.deucarian.core-state"
                    },
                    integrationTargets: new[]
                    {
                        "com.deucarian.ui-binding",
                        "com.deucarian.core-state"
                    }),
                CreatePackage(
                    "Deucarian Object Selection Core State Integration",
                    "com.deucarian.object-selection.core-state-integration",
                    "Integration",
                    "Integration",
                    dependencies: new[]
                    {
                        "com.deucarian.object-selection",
                        "com.deucarian.core-state"
                    },
                    integrationTargets: new[]
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
                        "com.deucarian.ui-binding.core-state-integration",
                        "com.deucarian.object-selection.core-state-integration"
                    },
                    suiteMembers: new[]
                    {
                        "com.deucarian.core-state",
                        "com.deucarian.ui-binding",
                        "com.deucarian.object-selection",
                        "com.deucarian.ui-binding.core-state-integration",
                        "com.deucarian.object-selection.core-state-integration"
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

        private static void AssertCategoryAndKind(
            PackageGraphModel graph,
            string packageId,
            string expectedCategory,
            string expectedKind)
        {
            Assert.IsTrue(graph.TryGetNode(packageId, out PackageGraphNode node), packageId);
            Assert.AreEqual(
                expectedCategory,
                PackageGraphHierarchyDisplay.GetPackageHierarchyPath(graph, node.PackageDefinition));
            Assert.AreEqual(expectedKind, PackageGraphHierarchyDisplay.GetPackageKind(node.PackageDefinition));
        }

        private static float GetAngle(Rect rect)
        {
            Vector2 direction = rect.center - PackageGraphLayout.GraphCenter;
            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        private static float CircularMean(params float[] angles)
        {
            float x = 0f;
            float y = 0f;

            foreach (float angle in angles)
            {
                float radians = angle * Mathf.Deg2Rad;
                x += Mathf.Cos(radians);
                y += Mathf.Sin(radians);
            }

            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }

        private static float DeltaAngle(float first, float second)
        {
            return Mathf.Abs(Mathf.DeltaAngle(first, second));
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

        private static void AssertGroupClustersSeparated(
            PackageGraphModel graph,
            PackageGraphLayoutResult layout,
            float minimumGap)
        {
            PackageGraphGroupLayoutNode[] topGroups = layout.GroupNodes
                .Where(groupNode => groupNode != null && !groupNode.Collapsed)
                .OrderBy(groupNode => groupNode.Group.SortOrder)
                .ToArray();

            for (int firstIndex = 0; firstIndex < topGroups.Length; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < topGroups.Length; secondIndex++)
                {
                    PackageGraphGroupLayoutNode first = topGroups[firstIndex];
                    PackageGraphGroupLayoutNode second = topGroups[secondIndex];
                    float firstRadius = CalculateClusterRadius(graph, layout, first);
                    float secondRadius = CalculateClusterRadius(graph, layout, second);
                    float distance = Vector2.Distance(first.HubCenter, second.HubCenter);

                    Assert.That(
                        distance + 1.0f,
                        Is.GreaterThanOrEqualTo(firstRadius + secondRadius + minimumGap),
                        first.GroupId + " overlaps cluster space for " + second.GroupId);
                }
            }
        }

        private static float CalculateClusterRadius(
            PackageGraphModel graph,
            PackageGraphLayoutResult layout,
            PackageGraphGroupLayoutNode groupNode)
        {
            Rect bounds = groupNode.Rect;

            foreach (PackageGraphNode node in graph.Nodes)
            {
                if (node == null ||
                    !string.Equals(node.GroupId, groupNode.GroupId, StringComparison.OrdinalIgnoreCase) ||
                    !layout.NodeRects.TryGetValue(node.PackageId, out Rect nodeRect))
                {
                    continue;
                }

                bounds = Union(bounds, nodeRect);
            }

            float radius = 0f;
            Vector2 center = groupNode.HubCenter;
            Vector2[] corners =
            {
                new Vector2(bounds.xMin, bounds.yMin),
                new Vector2(bounds.xMax, bounds.yMin),
                new Vector2(bounds.xMax, bounds.yMax),
                new Vector2(bounds.xMin, bounds.yMax)
            };

            foreach (Vector2 corner in corners)
            {
                radius = Mathf.Max(radius, Vector2.Distance(center, corner));
            }

            return radius;
        }

        private static Rect CreateExpectedFitBounds(PackageGraphLayoutResult layout)
        {
            return PackageGraphActiveLayoutBounds.Calculate(layout);
        }

        private static Rect CreateExpectedFitBounds(
            PackageGraphLayoutResult layout,
            IEnumerable<string> visiblePackageIds)
        {
            return PackageGraphActiveLayoutBounds.Calculate(layout);
        }

        private static void AssertRectsEqual(Rect expected, Rect actual, float tolerance)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(tolerance));
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(tolerance));
        }

        private static Rect Union(Rect first, Rect second)
        {
            float xMin = Mathf.Min(first.xMin, second.xMin);
            float yMin = Mathf.Min(first.yMin, second.yMin);
            float xMax = Mathf.Max(first.xMax, second.xMax);
            float yMax = Mathf.Max(first.yMax, second.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(
                rect.x - amount,
                rect.y - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }

        private static List<VisualElement> FindByClass(VisualElement root, string className)
        {
            List<VisualElement> matches = new List<VisualElement>();
            CollectByClass(root, className, matches);
            return matches;
        }

        private static VisualElement FindGraphNode(VisualElement root, string packageId)
        {
            return FindByClass(root, "dpi-graph-node")
                .Single(node => string.Equals(node.name, packageId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasGraphNode(VisualElement root, string packageId)
        {
            return FindByClass(root, "dpi-graph-node")
                .Any(node => string.Equals(node.name, packageId, StringComparison.OrdinalIgnoreCase));
        }

        private static Button FindGraphNodeAction(VisualElement root, string packageId)
        {
            return FindByClass(FindGraphNode(root, packageId), "dpi-graph-node__action")
                .OfType<Button>()
                .Single();
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
