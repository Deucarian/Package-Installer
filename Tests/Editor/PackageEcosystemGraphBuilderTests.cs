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
        public void Window_FixedWallpaperUsesApplicationShellHostAndTopSafeFade()
        {
            VisualElement root = new VisualElement();
            VisualElement shell = new VisualElement { name = "deucarian-application-shell" };
            VisualElement background = new VisualElement { name = "deucarian-window-background" };
            VisualElement overlay = new VisualElement { name = "deucarian-window-overlay" };
            root.Add(background);
            root.Add(overlay);
            root.Add(shell);

            PackageInstallerWindow.ConfigureFixedWallpaperForTests(root, shell);

            Assert.AreSame(shell, background.parent);
            Assert.AreSame(shell, overlay.parent);
            Assert.IsTrue(shell.ClassListContains("dpi-wallpaper-safe-shell"));
            Assert.AreEqual(Overflow.Hidden, shell.style.overflow.value);

            VisualElement fade = shell.Q<VisualElement>(PackageInstallerWindow.WallpaperTopSafeFadeName);
            Assert.NotNull(fade);
            Assert.IsTrue(fade.ClassListContains("dpi-wallpaper-top-safe-fade"));
            Assert.AreEqual(PickingMode.Ignore, fade.pickingMode);
            Assert.AreEqual(Position.Absolute, fade.style.position.value);
            Assert.AreEqual(86f, fade.style.height.value.value);
        }

        [Test]
        public void Window_OperationFooterBuildsStableVisibleHierarchy()
        {
            VisualElement footer = PackageInstallerWindow.CreateOperationFooterForTests();

            Assert.AreEqual(PackageInstallerWindow.OperationFooterRowName, footer.name);
            Assert.IsTrue(footer.ClassListContains("dpi-operation-surface"));
            Assert.IsTrue(footer.ClassListContains("dpi-operation-footer"));
            Assert.AreEqual(FlexDirection.Row, footer.style.flexDirection.value);
            Assert.AreEqual(Align.Center, footer.style.alignItems.value);
            Assert.AreEqual(0f, footer.style.flexShrink.value);
            Assert.AreEqual(34f, footer.style.height.value.value);
            Assert.AreEqual(PackageInstallerWindow.OperationInlinePaddingForTests, footer.style.paddingLeft.value.value);
            Assert.AreEqual(PackageInstallerWindow.OperationInlinePaddingForTests, footer.style.paddingRight.value.value);

            VisualElement statusGroup = footer.Q<VisualElement>(PackageInstallerWindow.OperationFooterStatusGroupName);
            Label statusIcon = footer.Q<Label>(PackageInstallerWindow.OperationFooterStatusIconName);
            Label statusLabel = footer.Q<Label>(PackageInstallerWindow.OperationFooterStatusLabelName);
            Label summaryLabel = footer.Q<Label>(PackageInstallerWindow.OperationFooterSummaryName);
            Button detailsButton = footer.Q<Button>(PackageInstallerWindow.OperationFooterDetailsButtonName);
            Label versionLabel = footer.Q<Label>(PackageInstallerWindow.OperationFooterVersionName);

            AssertFooterElementVisible(statusGroup);
            AssertFooterElementVisible(statusIcon);
            AssertFooterElementVisible(statusLabel);
            AssertFooterElementVisible(summaryLabel);
            AssertFooterElementVisible(detailsButton);
            AssertFooterElementVisible(versionLabel);

            Assert.IsFalse(string.IsNullOrWhiteSpace(statusIcon.text));
            Assert.IsFalse(string.IsNullOrWhiteSpace(statusLabel.text));
            Assert.IsFalse(string.IsNullOrWhiteSpace(summaryLabel.text));
            Assert.AreEqual(PackageInstallerWindow.OperationControlGapForTests, statusGroup.style.marginRight.value.value);
            Assert.AreEqual(PackageInstallerWindow.OperationControlGapForTests, summaryLabel.style.marginRight.value.value);
            Assert.AreEqual(PackageInstallerWindow.OperationControlGapForTests, detailsButton.style.marginRight.value.value);
            Assert.IsTrue(detailsButton.text == "Show Details" || detailsButton.text == "Hide Details");
            Assert.IsFalse(string.IsNullOrWhiteSpace(versionLabel.text));
            StringAssert.Contains(PackageInstallerWindow.PackageIdForTests, versionLabel.text);
            StringAssert.Contains(PackageInstallerWindow.PackageVersionForTests, versionLabel.text);
        }

        [Test]
        public void Window_OperationFooterDrawerToggleKeepsVersionVisible()
        {
            VisualElement footer = PackageInstallerWindow.CreateOperationFooterForTests();
            Button detailsButton = footer.Q<Button>(PackageInstallerWindow.OperationFooterDetailsButtonName);
            Label versionLabel = footer.Q<Label>(PackageInstallerWindow.OperationFooterVersionName);

            Assert.AreEqual("Show Details", detailsButton.text);
            StringAssert.Contains(PackageInstallerWindow.PackageIdForTests, versionLabel.text);

            PackageInstallerWindow.SetOperationFooterExpandedForTests(footer, true);

            Assert.AreEqual("Hide Details", detailsButton.text);
            AssertFooterElementVisible(versionLabel);
            StringAssert.Contains(PackageInstallerWindow.PackageIdForTests, versionLabel.text);
            StringAssert.Contains(PackageInstallerWindow.PackageVersionForTests, versionLabel.text);
        }

        [Test]
        public void Window_OperationFooterResponsiveClassesDoNotHideContent()
        {
            VisualElement root = new VisualElement();
            VisualElement footer = PackageInstallerWindow.CreateOperationFooterForTests();
            root.Add(footer);

            foreach (string responsiveClass in new[]
                     {
                         "dpi-responsive--wide",
                         "dpi-responsive--compact",
                         "dpi-responsive--narrow"
                     })
            {
                root.RemoveFromClassList("dpi-responsive--wide");
                root.RemoveFromClassList("dpi-responsive--compact");
                root.RemoveFromClassList("dpi-responsive--narrow");
                root.AddToClassList(responsiveClass);

                AssertFooterElementVisible(footer.Q<VisualElement>(PackageInstallerWindow.OperationFooterStatusGroupName));
                AssertFooterElementVisible(footer.Q<Label>(PackageInstallerWindow.OperationFooterSummaryName));
                AssertFooterElementVisible(footer.Q<Button>(PackageInstallerWindow.OperationFooterDetailsButtonName));
                AssertFooterElementVisible(footer.Q<Label>(PackageInstallerWindow.OperationFooterVersionName));
            }
        }

        [Test]
        public void Window_OperationDrawerHeightShrinksToContentAndCapsLargeSummaries()
        {
            float collapsedHeight = PackageInstallerWindow.CalculateOperationDrawerContainerHeightForTests(
                expanded: false,
                contentLineCount: 20);
            float smallHeight = PackageInstallerWindow.CalculateOperationDrawerContainerHeightForTests(
                expanded: true,
                contentLineCount: 1);
            float largeHeight = PackageInstallerWindow.CalculateOperationDrawerContainerHeightForTests(
                expanded: true,
                contentLineCount: 40);

            Assert.AreEqual(0f, collapsedHeight);
            Assert.That(smallHeight, Is.GreaterThan(PackageInstallerWindow.OperationFooterHeightForTests));
            Assert.That(smallHeight, Is.LessThan(110f));
            Assert.That(largeHeight, Is.GreaterThan(smallHeight));
            Assert.That(largeHeight, Is.LessThanOrEqualTo(PackageInstallerWindow.OperationDrawerExpandedMaxHeightForTests));
            Assert.That(largeHeight, Is.GreaterThan(180f));
        }

        [Test]
        public void Window_OperationSpacingTokensKeepFooterPixelValuesStable()
        {
            VisualElement footer = PackageInstallerWindow.CreateOperationFooterForTests();
            VisualElement statusGroup = footer.Q<VisualElement>(PackageInstallerWindow.OperationFooterStatusGroupName);
            Label summaryLabel = footer.Q<Label>(PackageInstallerWindow.OperationFooterSummaryName);
            Button detailsButton = footer.Q<Button>(PackageInstallerWindow.OperationFooterDetailsButtonName);

            Assert.AreEqual(12, PackageInstallerWindow.OperationInlinePaddingForTests);
            Assert.AreEqual(8, PackageInstallerWindow.OperationControlGapForTests);
            Assert.AreEqual(34f, PackageInstallerWindow.OperationFooterHeightForTests);
            Assert.AreEqual(12f, footer.style.paddingLeft.value.value);
            Assert.AreEqual(12f, footer.style.paddingRight.value.value);
            Assert.AreEqual(8f, statusGroup.style.marginRight.value.value);
            Assert.AreEqual(8f, summaryLabel.style.marginRight.value.value);
            Assert.AreEqual(8f, detailsButton.style.marginRight.value.value);
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
                    "ui-presentation",
                    "world-interaction",
                    "tools-quality",
                    "integrations",
                    "suites"
                },
                graph.Groups.Select(group => group.Id).ToArray());
            Assert.AreEqual(
                "experience-interaction",
                graph.Groups.Single(group => group.Id == "ui-presentation").ParentGroupId);
            Assert.AreEqual(
                "experience-interaction",
                graph.Groups.Single(group => group.Id == "world-interaction").ParentGroupId);
            Assert.IsFalse(graph.Groups.Any(group => string.Equals(group.DisplayName, "Foundation", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual("infrastructure", graph.Nodes.Single(node => node.PackageId == "com.deucarian.editor").GroupId);
            Assert.AreEqual("infrastructure", graph.Nodes.Single(node => node.PackageId == "com.deucarian.logging").GroupId);
            Assert.AreEqual("state-data", graph.Nodes.Single(node => node.PackageId == "com.deucarian.core-state").GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == "com.deucarian.api").GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == "com.deucarian.session").GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == "com.deucarian.object-loading").GroupId);
            Assert.AreEqual("ui-presentation", graph.Nodes.Single(node => node.PackageId == "com.deucarian.ui-binding").GroupId);
            Assert.AreEqual("ui-presentation", graph.Nodes.Single(node => node.PackageId == "com.deucarian.theming").GroupId);
            Assert.AreEqual("world-interaction", graph.Nodes.Single(node => node.PackageId == "com.deucarian.object-selection").GroupId);
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
                new PackageGraphGroup("ui-presentation", "UI & Presentation", "experience-interaction", string.Empty, 41, string.Empty, string.Empty),
                new PackageGraphGroup("world-interaction", "World Interaction", "experience-interaction", string.Empty, 42, string.Empty, string.Empty),
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
            Assert.AreEqual("ui-presentation", graph.Nodes.Single(node => node.PackageId == ui.PackageId).GroupId);
            Assert.IsFalse(graph.Groups.Any(group => string.Equals(group.DisplayName, "Foundation", StringComparison.OrdinalIgnoreCase)));
        }

        [Test]
        public void Build_CreatesNestedExperienceInteractionCategories()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphGroup experience = graph.Groups.Single(group => group.Id == "experience-interaction");
            PackageGraphGroup uiPresentation = graph.Groups.Single(group => group.Id == "ui-presentation");
            PackageGraphGroup worldInteraction = graph.Groups.Single(group => group.Id == "world-interaction");

            Assert.IsTrue(string.IsNullOrWhiteSpace(experience.ParentGroupId));
            Assert.AreEqual(experience.Id, uiPresentation.ParentGroupId);
            Assert.AreEqual(experience.Id, worldInteraction.ParentGroupId);
            Assert.IsFalse(graph.Nodes.Any(node => node.GroupId == experience.Id));
            CollectionAssert.AreEquivalent(
                new[] { "com.deucarian.ui-binding", "com.deucarian.theming" },
                graph.Nodes
                    .Where(node => node.GroupId == uiPresentation.Id)
                    .Select(node => node.PackageId)
                    .ToArray());
            CollectionAssert.AreEquivalent(
                new[] { "com.deucarian.object-selection" },
                graph.Nodes
                    .Where(node => node.GroupId == worldInteraction.Id)
                    .Select(node => node.PackageId)
                    .ToArray());
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
        public void CategoryStatusSummary_ClassifiesPackagesIntoMutuallyExclusiveBuckets()
        {
            PackageDefinition installed = CreatePackage("Installed", "com.example.installed", "Core");
            PackageDefinition update = CreatePackage("Update", "com.example.update", "Core");
            PackageDefinition notInstalled = CreatePackage("Not Installed", "com.example.not-installed", "Core");
            PackageDefinition dependency = CreatePackage("Dependency", "com.example.dependency", "Core");
            PackageDefinition consumer = CreatePackage(
                "Consumer",
                "com.example.consumer",
                "Core",
                dependencies: new[] { dependency.PackageId });
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
                .Build(new[] { installed, update, notInstalled, dependency, consumer });

            PackageGraphCategoryStatusSummary summary =
                PackageGraphCategoryStatusSummary.Create(graph.Nodes);

            Assert.AreEqual(2, summary.InstalledCount);
            Assert.AreEqual(1, summary.NotInstalledCount);
            Assert.AreEqual(2, summary.AttentionCount);
            Assert.AreEqual(0, summary.UnknownCount);
            Assert.AreEqual(graph.Nodes.Count, summary.TotalCount);
            Assert.AreEqual(
                PackageGraphCategoryStatusKey.Attention,
                PackageGraphCategoryStatusClassifier.Classify(
                    graph.Nodes.Single(node => node.PackageId == update.PackageId)));
            Assert.AreEqual(
                PackageGraphCategoryStatusKey.Attention,
                PackageGraphCategoryStatusClassifier.Classify(
                    graph.Nodes.Single(node => node.PackageId == dependency.PackageId)));
        }

        [Test]
        public void CategoryStatusSlices_AreOrderedAndProportionalToCounts()
        {
            IReadOnlyList<CategoryStatusSlice> mixedSlices =
                PackageGraphCategoryStatusVisuals.CreateSlices(
                    new PackageGraphCategoryStatusSummary(2, 1, 1, 0));
            IReadOnlyList<CategoryStatusSlice> singleSlices =
                PackageGraphCategoryStatusVisuals.CreateSlices(
                    new PackageGraphCategoryStatusSummary(0, 4, 0, 0));
            IReadOnlyList<CategoryStatusSlice> futureSlices =
                PackageGraphCategoryStatusVisuals.CreateSlices(
                    new PackageGraphCategoryStatusSummary(1, 1, 1, 1));
            IReadOnlyList<CategoryStatusSlice> emptySlices =
                PackageGraphCategoryStatusVisuals.CreateSlices(
                    new PackageGraphCategoryStatusSummary(0, 0, 0, 0));

            CollectionAssert.AreEqual(
                new[]
                {
                    PackageGraphCategoryStatusKey.Installed,
                    PackageGraphCategoryStatusKey.NotInstalled,
                    PackageGraphCategoryStatusKey.Attention
                },
                mixedSlices.Select(slice => slice.StatusKey).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 1, 1 }, mixedSlices.Select(slice => slice.Count).ToArray());
            Assert.AreEqual(4, mixedSlices.Sum(slice => slice.Count));
            Assert.AreEqual(0.50f, mixedSlices[0].Count / 4f, 0.001f);
            Assert.AreEqual(0.25f, mixedSlices[1].Count / 4f, 0.001f);
            Assert.AreEqual(0.25f, mixedSlices[2].Count / 4f, 0.001f);
            Assert.AreEqual(1, singleSlices.Count);
            Assert.AreEqual(PackageGraphCategoryStatusKey.NotInstalled, singleSlices[0].StatusKey);
            Assert.AreEqual(4, futureSlices.Count);
            Assert.IsTrue(futureSlices.Any(slice => slice.StatusKey == PackageGraphCategoryStatusKey.Unknown));
            Assert.IsEmpty(emptySlices);
        }

        [Test]
        public void CategoryStatusRingSegments_EmptyCategoryRendersCompleteNeutralRing()
        {
            IReadOnlyList<CategoryStatusRingSegment> segments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(
                    new PackageGraphCategoryStatusSummary(0, 0, 0, 0));

            Assert.AreEqual(1, segments.Count);
            Assert.AreEqual(PackageGraphCategoryStatusKey.Unknown, segments[0].StatusKey);
            Assert.IsTrue(segments[0].FullRing);
            Assert.AreEqual(360f, segments[0].SweepDegrees, 0.001f);
            Assert.AreEqual(0f, segments[0].SeparatorAfterDegrees, 0.001f);
        }

        [TestCase(1, 0, 0, PackageGraphCategoryStatusKey.Installed)]
        [TestCase(2, 0, 0, PackageGraphCategoryStatusKey.Installed)]
        [TestCase(0, 1, 0, PackageGraphCategoryStatusKey.NotInstalled)]
        [TestCase(0, 0, 1, PackageGraphCategoryStatusKey.Attention)]
        public void CategoryStatusRingSegments_SingleNonZeroStatusRendersOneCompleteRing(
            int installed,
            int notInstalled,
            int attention,
            PackageGraphCategoryStatusKey expectedStatus)
        {
            IReadOnlyList<CategoryStatusRingSegment> segments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(
                    new PackageGraphCategoryStatusSummary(installed, notInstalled, attention, 0));

            Assert.AreEqual(1, segments.Count);
            Assert.AreEqual(expectedStatus, segments[0].StatusKey);
            Assert.IsTrue(segments[0].FullRing);
            Assert.AreEqual(360f, segments[0].SweepDegrees, 0.001f);
            Assert.AreEqual(0f, segments[0].SeparatorAfterDegrees, 0.001f);
        }

        [Test]
        public void CategoryStatusRingSegments_MultipleStatusesUseOnlyConfiguredSeparators()
        {
            IReadOnlyList<CategoryStatusRingSegment> equalSegments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(
                    new PackageGraphCategoryStatusSummary(1, 1, 0, 0));
            IReadOnlyList<CategoryStatusRingSegment> mixedSegments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(
                    new PackageGraphCategoryStatusSummary(2, 1, 1, 0));

            Assert.AreEqual(2, equalSegments.Count);
            Assert.IsTrue(equalSegments.All(segment => !segment.FullRing));
            Assert.AreEqual(
                180f - PackageGraphCategoryStatusVisuals.StatusRingSeparatorDegrees,
                equalSegments[0].SweepDegrees,
                0.001f);
            Assert.AreEqual(
                180f - PackageGraphCategoryStatusVisuals.StatusRingSeparatorDegrees,
                equalSegments[1].SweepDegrees,
                0.001f);
            Assert.AreEqual(
                360f,
                equalSegments.Sum(segment => segment.SweepDegrees + segment.SeparatorAfterDegrees),
                0.001f);

            Assert.AreEqual(3, mixedSegments.Count);
            float usableAngle = 360f - PackageGraphCategoryStatusVisuals.StatusRingSeparatorDegrees * 3f;
            Assert.AreEqual(usableAngle * 0.50f, mixedSegments[0].SweepDegrees, 0.001f);
            Assert.AreEqual(usableAngle * 0.25f, mixedSegments[1].SweepDegrees, 0.001f);
            Assert.AreEqual(usableAngle * 0.25f, mixedSegments[2].SweepDegrees, 0.001f);
            Assert.AreEqual(
                360f,
                mixedSegments.Sum(segment => segment.SweepDegrees + segment.SeparatorAfterDegrees),
                0.001f);
        }

        [Test]
        public void CategoryStatusRingSegments_BackHintAndHoverDoNotChangeCoverage()
        {
            PackageGraphCategoryStatusSummary summary = new PackageGraphCategoryStatusSummary(2, 1, 1, 0);
            IReadOnlyList<CategoryStatusRingSegment> normalSegments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(summary);
            CategoryStatusRingVisualState hoveredRing = new CategoryStatusRingVisualState(
                "group:test:status",
                new Vector2(10f, 12f),
                44f,
                5f,
                PackageGraphCategoryStatusVisuals.CreateSlices(summary),
                true);
            IReadOnlyList<CategoryStatusRingSegment> hoveredSegments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(
                    hoveredRing.Slices,
                    hoveredRing.TotalCount);

            Assert.AreEqual(
                normalSegments.Sum(segment => segment.SweepDegrees + segment.SeparatorAfterDegrees),
                hoveredSegments.Sum(segment => segment.SweepDegrees + segment.SeparatorAfterDegrees),
                0.001f);
            Assert.AreEqual(10f, hoveredRing.Center.x, 0.001f);
            Assert.AreEqual(12f, hoveredRing.Center.y, 0.001f);
            Assert.AreEqual(88f, hoveredRing.Radius * 2f, 0.001f);
            Assert.That(hoveredRing.Radius, Is.GreaterThan(hoveredRing.Thickness));
        }

        [Test]
        public void CategoryStatusRings_AggregateRootAndNestedCategoryPackages()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(graph, string.Empty, string.Empty, true);

            IReadOnlyList<CategoryStatusRingVisualState> rings = canvas.StatusRingVisualStatesForTests;
            CategoryStatusRingVisualState rootRing =
                rings.Single(ring => ring.RingId == "root:status");
            CategoryStatusRingVisualState experienceRing =
                rings.Single(ring => ring.RingId == "group:experience-interaction:status");

            Assert.AreEqual(graph.Nodes.Count, rootRing.TotalCount);
            Assert.AreEqual(3, experienceRing.TotalCount);
            Assert.AreEqual(
                3,
                experienceRing.Slices.Single(slice => slice.StatusKey == PackageGraphCategoryStatusKey.NotInstalled).Count);
            Assert.That(experienceRing.Radius, Is.GreaterThan(experienceRing.Thickness));
            Assert.That(
                Vector2.Distance(
                    canvas.AnimatedGroupCentersForTests["experience-interaction"],
                    experienceRing.Center),
                Is.LessThan(0.1f));
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
            HashSet<string> searchIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, searchState);
            PackageGraphSearchState graphSearchState = PackageGraphSearchIndex.Create(graph, searchState, searchIds);

            searchView.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                searchIds,
                graphSearchState,
                PackageVisibilityFilter.CalculateCounts(graph, searchState),
                hiddenRelatedCount: 0);

            Assert.AreEqual(
                "No categories or packages match this search.",
                FindByClass(searchView, "dpi-ecosystem-graph__empty-title")
                    .OfType<Label>()
                    .Single()
                    .text);
        }

        [Test]
        public void GraphSearch_CategoryMatchIncludesOnlyStructuralDescendants()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "Experience Interaction",
                showInstalled: true,
                showNotInstalled: true);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState, visiblePackageIds);

            Assert.IsTrue(searchState.IsDirectCategoryMatch("experience-interaction"));
            Assert.IsTrue(searchState.IsCategoryContext("ui-presentation"));
            Assert.IsTrue(searchState.IsCategoryContext("world-interaction"));
            Assert.IsTrue(searchState.IsPackageContext("com.deucarian.ui-binding"));
            Assert.IsTrue(searchState.IsPackageContext("com.deucarian.theming"));
            Assert.IsTrue(searchState.IsPackageContext("com.deucarian.object-selection"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.session"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.session.api-integration"));
        }

        [Test]
        public void GraphSearch_PackageMatchDoesNotExpandRelationships()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState loggingFilterState = new PackageVisibilityFilterState(
                "Logging",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphSearchState loggingSearch = PackageGraphSearchIndex.Create(
                graph,
                loggingFilterState,
                PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, loggingFilterState));

            Assert.IsTrue(loggingSearch.IsDirectPackageMatch("com.deucarian.logging"));
            Assert.IsTrue(loggingSearch.IsPackageContext("com.deucarian.logging"));
            Assert.IsFalse(loggingSearch.IsDirectPackageMatch("com.deucarian.session"));
            Assert.IsFalse(loggingSearch.IsPackageContext("com.deucarian.session"));
            Assert.IsFalse(loggingSearch.IsPackageContext("com.deucarian.api"));

            PackageVisibilityFilterState integrationFilterState = new PackageVisibilityFilterState(
                "integration",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphSearchState integrationSearch = PackageGraphSearchIndex.Create(
                graph,
                integrationFilterState,
                PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, integrationFilterState));

            Assert.IsTrue(integrationSearch.IsDirectCategoryMatch("integrations"));
            Assert.IsTrue(integrationSearch.IsDirectPackageMatch("com.deucarian.session.api-integration"));
            Assert.IsTrue(integrationSearch.IsDirectPackageMatch("com.deucarian.object-loading.api-integration"));
            Assert.IsFalse(integrationSearch.IsDirectPackageMatch("com.deucarian.session"));
            Assert.IsFalse(integrationSearch.IsPackageContext("com.deucarian.session"));
            Assert.IsFalse(integrationSearch.IsPackageContext("com.deucarian.api"));
        }

        [Test]
        public void GraphSearch_InstalledFiltersLimitPackageContext()
        {
            PackageGraphModel graph = new PackageGraphBuilder(packageId =>
                    string.Equals(packageId, "com.deucarian.logging", StringComparison.OrdinalIgnoreCase))
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "Infrastructure",
                showInstalled: true,
                showNotInstalled: false);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState, visiblePackageIds);

            Assert.IsTrue(searchState.IsDirectCategoryMatch("infrastructure"));
            Assert.IsTrue(searchState.IsPackageContext("com.deucarian.logging"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.editor"));
        }

        [Test]
        public void GraphView_SearchPreviewHighlightsWithoutChangingNodeSet()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView normalView = new PackageGraphView(_ => { }, (_, __) => { });
            normalView.SetGraph(graph, string.Empty, actionsEnabled: true);

            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "Logging",
                showInstalled: true,
                showNotInstalled: true);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState, visiblePackageIds);
            PackageGraphView searchView = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                filterState,
                null);

            searchView.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                visiblePackageIds,
                searchState,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            Assert.AreEqual(
                FindByClass(normalView, "dpi-graph-node").Count,
                FindByClass(searchView, "dpi-graph-node").Count);
            Assert.IsTrue(FindGraphNode(searchView, "com.deucarian.logging").ClassListContains("dpi-graph-search--match"));
            Assert.IsTrue(FindGraphNode(searchView, "com.deucarian.session").ClassListContains("dpi-graph-search--dimmed"));
            Assert.AreEqual(
                "1 direct matches",
                FindByClass(searchView, "dpi-ecosystem-graph__visible-count")
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
        public void PresentationMetrics_ReserveRowsForStandardAndFullCards()
        {
            PackageGraphNodeMetrics standard =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Standard);
            PackageGraphNodeMetrics full =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Full);

            Assert.That(standard.Width, Is.GreaterThanOrEqualTo(218f));
            Assert.That(standard.Height, Is.GreaterThanOrEqualTo(150f));
            Assert.That(full.Width, Is.GreaterThanOrEqualTo(268f));
            Assert.That(full.Height, Is.GreaterThanOrEqualTo(190f));
        }

        [Test]
        public void GraphView_NodePresentationProfilesUseDedicatedRows()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView focused = new PackageGraphView(_ => { }, (_, __) => { });

            focused.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            VisualElement selected = FindGraphNode(focused, "com.deucarian.session");
            VisualElement related = FindGraphNode(focused, "com.deucarian.logging");

            Assert.IsTrue(selected.ClassListContains("dpi-graph-node--presentation-full"));
            Assert.IsTrue(related.ClassListContains("dpi-graph-node--presentation-standard"));
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__header").Count);
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__package-id").Count);
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__category-path").Count);
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__badges").Count);
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__footer").Count);
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__action").Count);

            Assert.IsEmpty(FindByClass(related, "dpi-graph-node__package-id"));
            Assert.AreEqual(1, FindByClass(related, "dpi-graph-node__category-path").Count);
            Assert.AreEqual(1, FindByClass(related, "dpi-graph-node__badges").Count);
            Assert.AreEqual(1, FindByClass(related, "dpi-graph-node__footer").Count);
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
        public void GraphTransition_AnchoredCameraUsesExactEndpointsAndContinuousPath()
        {
            PackageGraphCameraState source = new PackageGraphCameraState(new Vector2(40f, 24f), 0.72f);
            PackageGraphCameraState target = new PackageGraphCameraState(new Vector2(-180f, 90f), 1.18f);
            Vector2 sourceAnchorWorld = new Vector2(120f, 80f);
            Vector2 targetAnchorWorld = new Vector2(360f, 220f);
            Vector2 sourceAnchorScreen = source.WorldToViewport(sourceAnchorWorld);
            Vector2 targetAnchorScreen = target.WorldToViewport(targetAnchorWorld);

            PackageGraphCameraState frame0 = PackageGraphTransition.EvaluateAnchoredCamera(
                source,
                target,
                sourceAnchorWorld,
                targetAnchorWorld,
                sourceAnchorScreen,
                targetAnchorScreen,
                0f);
            PackageGraphCameraState middle = PackageGraphTransition.EvaluateAnchoredCamera(
                source,
                target,
                sourceAnchorWorld,
                targetAnchorWorld,
                sourceAnchorScreen,
                targetAnchorScreen,
                0.5f);
            PackageGraphCameraState frame1 = PackageGraphTransition.EvaluateAnchoredCamera(
                source,
                target,
                sourceAnchorWorld,
                targetAnchorWorld,
                sourceAnchorScreen,
                targetAnchorScreen,
                1f);

            AssertCameraClose(source, frame0);
            AssertCameraClose(target, frame1);
            Assert.That(middle.Zoom, Is.InRange(source.Zoom, target.Zoom));

            Vector2 expectedMiddleAnchorScreen = Vector2.Lerp(
                sourceAnchorScreen,
                targetAnchorScreen,
                PackageGraphTransition.SmoothStep(0.5f));
            Vector2 middleAnchorWorld = Vector2.Lerp(
                sourceAnchorWorld,
                targetAnchorWorld,
                PackageGraphTransition.SmoothStep(0.5f));
            AssertVectorClose(expectedMiddleAnchorScreen, middle.WorldToViewport(middleAnchorWorld), 0.001f);
        }

        [Test]
        public void GraphTransition_AnimatedAnchorCameraUsesRenderedAnchorWorld()
        {
            PackageGraphCameraState source = new PackageGraphCameraState(new Vector2(40f, 24f), 0.72f);
            PackageGraphCameraState target = new PackageGraphCameraState(new Vector2(-180f, 90f), 1.18f);
            Vector2 sourceAnchorScreen = new Vector2(126.4f, 81.6f);
            Vector2 targetAnchorScreen = new Vector2(244.8f, 349.6f);
            Vector2 animatedAnchorWorld = new Vector2(264f, 148f);
            float eased = PackageGraphTransition.SmoothStep(0.5f);

            PackageGraphCameraState middle = PackageGraphTransition.EvaluateAnchoredCameraFromAnimatedAnchor(
                source,
                target,
                animatedAnchorWorld,
                sourceAnchorScreen,
                targetAnchorScreen,
                0.5f);

            Vector2 expectedAnchorScreen = Vector2.Lerp(sourceAnchorScreen, targetAnchorScreen, eased);
            Assert.That(middle.Zoom, Is.EqualTo(Mathf.Lerp(source.Zoom, target.Zoom, eased)).Within(0.001f));
            AssertVectorClose(expectedAnchorScreen, middle.WorldToViewport(animatedAnchorWorld), 0.001f);
        }

        [Test]
        public void GraphProjection_KeepsPreviewChildrenOnAnimatedGroupOrbit()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayout layout = new PackageGraphLayout();
            PackageGraphLayoutResult source = layout.Calculate(
                graph,
                PackageGraphLayoutMode.Overview,
                string.Empty,
                string.Empty,
                Vector2.zero,
                PackageGraphNodePresentationLevel.OverviewCompact);
            PackageGraphLayoutResult target = layout.Calculate(
                graph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                "integrations",
                Vector2.zero,
                PackageGraphNodePresentationLevel.Standard);
            float frameProgress = 0.5f;
            Dictionary<string, Rect> nodeRects = CreateInterpolatedNodeRects(source, target, frameProgress);
            Dictionary<string, Rect> groupRects = CreateInterpolatedGroupRects(source, target, frameProgress);
            Dictionary<string, Vector2> groupCenters = CreateInterpolatedGroupCenters(source, target, frameProgress);
            Dictionary<string, float> groupRadii = CreateInterpolatedGroupRadii(source, target, frameProgress);

            PackageGraphCanvas.ProjectOrbitalChildrenForTests(
                graph,
                target,
                nodeRects,
                groupRects,
                groupCenters,
                groupRadii);

            Vector2 integrationCenter = groupCenters["integrations"];
            float integrationRadius = groupRadii["integrations"];
            Assert.That(integrationRadius, Is.GreaterThan(0f));

            foreach (PackageGraphNode node in graph.Nodes.Where(node =>
                         node != null &&
                         string.Equals(node.GroupId, "integrations", StringComparison.OrdinalIgnoreCase) &&
                         target.NodeRects.ContainsKey(node.PackageId)))
            {
                Assert.That(
                    Vector2.Distance(nodeRects[node.PackageId].center, integrationCenter),
                    Is.EqualTo(integrationRadius).Within(0.5f),
                    node.DisplayName);
            }
        }

        [Test]
        public void GraphCanvas_UsesPainterOrbitStatesWithoutCssRingGuideElements()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(graph, string.Empty, string.Empty, actionsEnabled: true);

            Assert.IsEmpty(FindByClass(canvas, "dpi-graph-ring-guide"));
            IReadOnlyList<PackageGraphOrbitVisualState> orbitStates = canvas.OrbitVisualStatesForTests;
            Assert.That(orbitStates.Count, Is.GreaterThan(0));
            Assert.AreEqual(
                orbitStates.Count,
                orbitStates.Select(orbit => orbit.OrbitId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.AreEqual(1, orbitStates.Count(orbit => orbit.OrbitId.StartsWith("root:", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(orbitStates.Any(orbit => orbit.OrbitId == "group:integrations"));
        }

        [Test]
        public void GraphTransition_TargetOnlyCategoryChildrenStartSmallTransparentAndSeparated()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
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
                "ui-presentation",
                actionsEnabled: true,
                visiblePackageIds: null);

            PackageGraphNodeVisualState uiBinding = canvas.NodeVisualStatesForTests["com.deucarian.ui-binding"];
            PackageGraphNodeVisualState theming = canvas.NodeVisualStatesForTests["com.deucarian.theming"];

            Assert.AreEqual(0f, uiBinding.Opacity);
            Assert.AreEqual(0f, theming.Opacity);
            Assert.That(uiBinding.Scale, Is.LessThan(0.30f));
            Assert.That(theming.Scale, Is.LessThan(0.30f));
            Assert.That(Vector2.Distance(uiBinding.Rect.center, theming.Rect.center), Is.GreaterThan(1f));
            Assert.AreEqual(1, canvas.CountNodeElementsForTests("com.deucarian.ui-binding"));
            Assert.AreEqual(1, canvas.CountNodeElementsForTests("com.deucarian.theming"));
        }

        [Test]
        public void GraphViewport_RemovesWheelDrivenHierarchyNavigationArchitecture()
        {
            System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static;
            System.Reflection.Assembly graphAssembly = typeof(PackageGraphViewport).Assembly;

            Assert.IsNull(typeof(PackageGraphViewport).GetEvent("HierarchyExitWheel", flags));
            Assert.IsNull(typeof(PackageGraphViewport).GetEvent("HierarchyEnterWheel", flags));
            Assert.IsNull(typeof(PackageGraphViewport).GetMethod("GetHierarchyExitIntentZoomForTests", flags));
            Assert.IsNull(graphAssembly.GetType("Deucarian.PackageInstaller.Editor.PackageGraphHierarchyExitController"));
            Assert.IsNull(graphAssembly.GetType("Deucarian.PackageInstaller.Editor.PackageGraphHierarchyExitWheelEvent"));
            Assert.IsNull(graphAssembly.GetType("Deucarian.PackageInstaller.Editor.PackageGraphHierarchyEnterWheelEvent"));
        }

        [Test]
        public void GraphCategoryHover_UsesDirectGroupHoverNotPackageParent()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, null);

            canvas.SetGraph(graph, string.Empty, string.Empty, true);
            canvas.SetPreviewPackageForTests("com.deucarian.logging");

            Assert.AreEqual("infrastructure", canvas.ActiveHoverGroupId);
            Assert.AreEqual(string.Empty, canvas.DirectHoverGroupId);

            canvas.SetExternalHoverGroup("infrastructure", respectInteractionLock: false);

            Assert.AreEqual("infrastructure", canvas.ActiveHoverGroupId);
            Assert.AreEqual("infrastructure", canvas.DirectHoverGroupId);
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
            int rootVisiblePackageCount = graph.Nodes.Count(node =>
                node.GroupId != "ui-presentation" &&
                node.GroupId != "world-interaction");
            Assert.AreEqual(rootVisiblePackageCount, FindByClass(overview, "dpi-graph-node--overview").Count);
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
            Assert.IsEmpty(FindByClass(symbol, "dpi-graph-group__count"));
            Assert.IsEmpty(FindByClass(symbol, "dpi-graph-group__attention"));
            Assert.IsTrue(
                FindByClass(runtimeGroup, "dpi-graph-group__title")
                    .OfType<Label>()
                    .Any(label => label.text == "Runtime Services"));
            Assert.IsTrue(
                FindByClass(runtimeGroup, "dpi-graph-group__subtitle")
                    .OfType<Label>()
                    .Any(label => label.text.Contains("package")));
            Assert.IsTrue(
                FindByClass(runtimeGroup, "dpi-graph-group__stat--installed")
                    .OfType<Label>()
                    .Any(label => label.text.Contains("installed")));
            Assert.IsTrue(
                FindByClass(runtimeGroup, "dpi-graph-group__stat--available")
                    .OfType<Label>()
                    .Any(label => label.text.Contains("not installed")));
            Assert.IsTrue(
                FindByClass(runtimeGroup, "dpi-graph-group__stat--attention")
                    .OfType<Label>()
                    .Any(label => label.text.Contains("attention")));
            Assert.IsEmpty(FindByClass(runtimeGroup, "dpi-graph-node__action"));
        }

        [Test]
        public void GraphView_ActiveCategoryAndPackageExposeBackAffordance()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView overview = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView groupFocused = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView nestedGroupFocused = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView packageFocused = new PackageGraphView(_ => { }, (_, __) => { });

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
            nestedGroupFocused.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "ui-presentation",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);
            packageFocused.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            VisualElement inactiveOverviewGroup = FindGraphGroup(overview, "infrastructure");
            VisualElement activeTopLevelGroup = FindGraphGroup(groupFocused, "infrastructure");
            VisualElement activeNestedGroup = FindGraphGroup(nestedGroupFocused, "ui-presentation");
            VisualElement selectedPackage = FindGraphNode(packageFocused, "com.deucarian.session");
            VisualElement relatedPackage = FindGraphNode(packageFocused, "com.deucarian.logging");
            Label packageBack = FindByClass(selectedPackage, "dpi-graph-node__back-hint")
                .OfType<Label>()
                .Single();

            Assert.IsFalse(inactiveOverviewGroup.ClassListContains("dpi-graph-group--has-back"));
            Assert.IsEmpty(FindByClass(inactiveOverviewGroup, "dpi-graph-group__back-hint"));
            Assert.IsTrue(activeTopLevelGroup.ClassListContains("dpi-graph-group--has-back"));
            Assert.AreEqual(1, FindByClass(activeTopLevelGroup, "dpi-graph-group__back-hint").Count);
            Assert.AreEqual(1, FindByClass(activeTopLevelGroup, "dpi-graph-category-caption-row").Count);
            Assert.AreEqual(1, FindByClass(activeTopLevelGroup, "dpi-graph-back-hint--category").Count);
            Assert.AreEqual("Back to Ecosystem Overview", activeTopLevelGroup.tooltip);
            Assert.IsTrue(activeNestedGroup.ClassListContains("dpi-graph-group--has-back"));
            Assert.AreEqual("Back to Experience & Interaction", activeNestedGroup.tooltip);
            Assert.IsTrue(selectedPackage.ClassListContains("dpi-graph-node--has-back"));
            Assert.AreEqual(PickingMode.Ignore, packageBack.pickingMode);
            Assert.AreEqual("Back to Runtime Services", packageBack.tooltip);
            Assert.AreEqual(1, FindByClass(selectedPackage, "dpi-graph-back-hint--package").Count);
            Assert.IsFalse((selectedPackage.tooltip ?? string.Empty).Contains("package."));
            Assert.AreEqual("Infrastructure", inactiveOverviewGroup.tooltip);
            Assert.IsEmpty(FindByClass(relatedPackage, "dpi-graph-node__back-hint"));
            Assert.IsEmpty(FindByClass(selectedPackage, "dpi-graph-node__back"));
            Assert.IsEmpty(FindByClass(activeTopLevelGroup, "dpi-graph-group__back"));
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

            PackageGraphView nestedFocused = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView nestedPackageFocused = new PackageGraphView(_ => { }, (_, __) => { });
            nestedFocused.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "ui-presentation",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);
            nestedPackageFocused.SetGraph(graph, "com.deucarian.ui-binding", actionsEnabled: true);

            Assert.IsTrue(
                FindByClass(nestedFocused, "dpi-category-rail__item")
                    .Single(item => item.name == "category-rail-experience-interaction")
                    .ClassListContains("dpi-category-rail__item--active"));
            Assert.IsTrue(
                FindByClass(nestedPackageFocused, "dpi-category-rail__item")
                    .Single(item => item.name == "category-rail-experience-interaction")
                    .ClassListContains("dpi-category-rail__item--active"));
        }

        [Test]
        public void GraphView_CategoryRailHoverHighlightsGraphStructuralContext()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);
            view.PreviewCategoryHoverForTests("infrastructure");

            Assert.IsTrue(
                FindByClass(view, "dpi-category-rail__item")
                    .Single(item => item.name == "category-rail-infrastructure")
                    .ClassListContains("dpi-category-rail__item--hover-context"));
            Assert.IsTrue(FindGraphGroup(view, "infrastructure").ClassListContains("dpi-graph-group--hover-context"));
            Assert.IsTrue(FindGraphNode(view, "com.deucarian.logging").ClassListContains("dpi-graph-node--hover-context"));
            Assert.IsTrue(FindGraphNode(view, "com.deucarian.session").ClassListContains("dpi-graph-node--hover-dimmed"));
        }

        [Test]
        public void GraphView_GraphPackageHoverHighlightsTopLevelRailParent()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView nestedPackageFocused = new PackageGraphView(_ => { }, (_, __) => { });

            nestedPackageFocused.SetGraph(graph, "com.deucarian.ui-binding", actionsEnabled: true);
            nestedPackageFocused.PreviewPackageHoverForTests("com.deucarian.ui-binding");

            Assert.IsTrue(
                FindByClass(nestedPackageFocused, "dpi-category-rail__item")
                    .Single(item => item.name == "category-rail-experience-interaction")
                    .ClassListContains("dpi-category-rail__item--hover-context"));
            Assert.IsTrue(
                FindGraphNode(nestedPackageFocused, "com.deucarian.ui-binding")
                    .ClassListContains("dpi-graph-node--hover-context"));
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
            PackageGraphView nestedPackageFocused = new PackageGraphView(_ => { }, (_, __) => { });

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
            nestedPackageFocused.SetGraph(graph, "com.deucarian.ui-binding", actionsEnabled: true);

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
            Assert.IsTrue(
                FindByClass(nestedPackageFocused, "dpi-ecosystem-graph__breadcrumb")
                    .OfType<Button>()
                    .Any(button => button.text == "Experience & Interaction"));
            Assert.IsTrue(
                FindByClass(nestedPackageFocused, "dpi-ecosystem-graph__breadcrumb")
                    .OfType<Button>()
                    .Any(button => button.text == "UI & Presentation"));
            Assert.IsTrue(
                FindByClass(nestedPackageFocused, "dpi-ecosystem-graph__breadcrumb-current")
                    .OfType<Label>()
                    .Any(label => label.text == "Deucarian UI Binding"));
        }

        [Test]
        public void GraphView_ReturningToRootClearsStaleCategoryState()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "experience-interaction",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);
            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            Assert.IsTrue(
                FindByClass(view, "dpi-category-rail__item")
                    .Single(item => item.name == "category-rail-overview")
                    .ClassListContains("dpi-category-rail__item--active"));
            Assert.IsFalse(
                FindByClass(view, "dpi-category-rail__item")
                    .Single(item => item.name == "category-rail-experience-interaction")
                    .ClassListContains("dpi-category-rail__item--active"));
            Assert.AreEqual(
                "Deucarian",
                FindByClass(view, "dpi-ecosystem-graph__breadcrumb-current")
                    .OfType<Label>()
                    .Single()
                    .text);
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

            PackageGraphView nestedView = new PackageGraphView(_ => { }, (_, __) => { });
            nestedView.SetGraph(graph, "com.deucarian.ui-binding", actionsEnabled: true);

            Assert.IsTrue(
                FindByClass(FindGraphNode(nestedView, "com.deucarian.ui-binding"), "dpi-graph-node__category-path")
                    .OfType<Label>()
                    .Any(label => label.text == "Experience & Interaction / UI & Presentation"));
        }

        [Test]
        public void HierarchyDisplay_SeparatesStructuralCategoryFromPackageKind()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            AssertCategoryAndKind(graph, "com.deucarian.api", "Runtime Services", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.session", "Runtime Services", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.core-state", "State & Data", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.ui-binding", "Experience & Interaction / UI & Presentation", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.object-selection", "Experience & Interaction / World Interaction", "Library");
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
            Assert.AreEqual(2, FindByClass(view, "dpi-graph-group--collapsed").Count);
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
        public void GraphEdgeRoutes_FanOutMultipleIntegrationTargetsThroughSharedTrunk()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.api");
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, "com.deucarian.api");

            PackageGraphEdgeRoute[] integrationRoutes =
                PackageGraphEdgeLayer.BuildRoutesForTests(graph, layout.NodeRects, focus)
                    .Where(route => route.HasKind(PackageGraphEdgeKind.IntegrationConnection) &&
                                    route.Bundle.ConnectsPackage("com.deucarian.api"))
                    .OrderBy(route => route.BranchIndex)
                    .ToArray();

            Assert.AreEqual(2, integrationRoutes.Length);
            Assert.IsTrue(integrationRoutes.All(route => route.UsesSharedTrunk));
            Assert.AreEqual(1, integrationRoutes.Select(route => route.SharedTrunkId).Distinct().Count());
            Assert.IsTrue(integrationRoutes.All(route => route.Zone == PackageGraphEdgeRouteZone.Integrations));
            Assert.IsTrue(integrationRoutes.All(route => route.BranchCount == 2));
            Assert.IsTrue(integrationRoutes.All(route => route.Points.Count == 4));
            Assert.IsTrue(integrationRoutes.All(route => route.Bundle.IsCompositeDependencyIntegration));
            Assert.IsTrue(integrationRoutes.All(route => route.HasKind(PackageGraphEdgeKind.HardDependency)));
            Assert.That(
                Vector2.Distance(
                    integrationRoutes[0].Points[1],
                    integrationRoutes[1].Points[1]),
                Is.LessThan(0.1f));
            Assert.IsTrue(integrationRoutes.All(route =>
                !PackageGraphEdgeLayer.RouteCrossesNodeInteriorForTests(route, layout.NodeRects)));
            Assert.IsTrue(integrationRoutes.All(route =>
                PackageGraphEdgeLayer.RouteLengthForTests(route) <=
                PackageGraphEdgeLayer.DirectRouteDistanceForTests(route) * 1.8f));
        }

        [Test]
        public void GraphEdgeRoutes_BundleDependencyAndIntegrationByEndpointPair()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.api");
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, "com.deucarian.api");

            PackageGraphEdgeRoute route =
                PackageGraphEdgeLayer.BuildRoutesForTests(graph, layout.NodeRects, focus)
                    .Single(candidate =>
                        candidate.Bundle.SourcePackageId == "com.deucarian.api" &&
                        candidate.Bundle.TargetPackageId == "com.deucarian.session.api-integration");

            Assert.IsTrue(route.Bundle.IsCompositeDependencyIntegration);
            Assert.AreEqual(2, route.Bundle.Edges.Count);
            Assert.IsTrue(route.HasKind(PackageGraphEdgeKind.HardDependency));
            Assert.IsTrue(route.HasKind(PackageGraphEdgeKind.IntegrationConnection));
            Assert.AreEqual(PackageGraphEdgeKind.HardDependency, route.Edge.Kind);
            Assert.AreEqual("com.deucarian.api", route.Edge.FromPackageId);
            Assert.AreEqual("com.deucarian.session.api-integration", route.Edge.ToPackageId);
        }

        [Test]
        public void GraphEdgeRoutes_UseDirectBorderRouteForSingleTarget()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.session");
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, "com.deucarian.session");

            PackageGraphEdgeRoute dependencyRoute =
                PackageGraphEdgeLayer.BuildRoutesForTests(graph, layout.NodeRects, focus)
                    .Single(route => route.Edge.Kind == PackageGraphEdgeKind.HardDependency &&
                                     route.Edge.FromPackageId == "com.deucarian.logging" &&
                                     route.Edge.ToPackageId == "com.deucarian.session");

            Assert.IsFalse(dependencyRoute.UsesSharedTrunk);
            Assert.AreEqual(2, dependencyRoute.Points.Count);
            Assert.AreEqual(PackageGraphEdgeRouteZone.Providers, dependencyRoute.Zone);
            Assert.AreEqual(PackageGraphEdgeRoutePort.Right, dependencyRoute.SourcePort);
            Assert.AreEqual(PackageGraphEdgeRoutePort.Left, dependencyRoute.TargetPort);
            Assert.IsFalse(PackageGraphEdgeLayer.RouteCrossesNodeInteriorForTests(dependencyRoute, layout.NodeRects));
        }

        [Test]
        public void GraphEdgeRoutes_DoNotBundleStructuralMembershipWithRelationshipRoutes()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.core-state");
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, "com.deucarian.core-state");

            PackageGraphEdgeRoute[] routes =
                PackageGraphEdgeLayer.BuildRoutesForTests(graph, layout.NodeRects, focus)
                    .ToArray();

            Assert.IsTrue(routes.All(route => route.Edge != null));
            Assert.IsFalse(routes.Any(route => route.SharedTrunkId.IndexOf("membership", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsTrue(routes.Any(route => route.HasKind(PackageGraphEdgeKind.IntegrationConnection)));
            Assert.IsFalse(routes.Any(route => route.Zone == PackageGraphEdgeRouteZone.Direct &&
                                               route.HasKind(PackageGraphEdgeKind.IntegrationConnection) &&
                                               route.Bundle.ConnectsPackage("com.deucarian.core-state")));
        }

        [Test]
        public void Layout_CalculatesHierarchicalOverviewWithoutNodeOverlap()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(graph);
            PackageGraphNodeMetrics microMetrics =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.OverviewMicro);
            int hiddenNestedPackageCount = graph.Nodes.Count(node =>
                node.GroupId == "ui-presentation" ||
                node.GroupId == "world-interaction");

            Assert.AreEqual(PackageGraphLayoutMode.Overview, layout.Mode);
            Assert.AreEqual(graph.Nodes.Count - hiddenNestedPackageCount, layout.NodeRects.Count);
            Assert.AreEqual(1, layout.RingGuides.Count);
            Assert.IsEmpty(layout.SectorLabels);
            Assert.AreEqual(7, layout.GroupNodes.Count(groupNode => !groupNode.Collapsed));
            Assert.IsTrue(layout.GroupNodes.Any(groupNode => groupNode.GroupId == "ui-presentation" && groupNode.Collapsed));
            Assert.IsTrue(layout.GroupNodes.Any(groupNode => groupNode.GroupId == "world-interaction" && groupNode.Collapsed));
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.ui-binding"));
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.theming"));
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.object-selection"));
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
        public void Layout_NestedCategoryFocusShowsImmediateChildrenOnly()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayout layoutCalculator = new PackageGraphLayout();

            PackageGraphLayoutResult experienceFocus = layoutCalculator.Calculate(
                graph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                "experience-interaction",
                Vector2.zero);
            PackageGraphLayoutResult uiFocus = layoutCalculator.Calculate(
                graph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                "ui-presentation",
                Vector2.zero);
            PackageGraphLayoutResult worldFocus = layoutCalculator.Calculate(
                graph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                "world-interaction",
                Vector2.zero);

            Assert.AreEqual("experience-interaction", experienceFocus.FocusGroupId);
            Assert.IsTrue(experienceFocus.GroupNodes.Any(groupNode => groupNode.GroupId == "ui-presentation"));
            Assert.IsTrue(experienceFocus.GroupNodes.Any(groupNode => groupNode.GroupId == "world-interaction"));
            Assert.IsFalse(experienceFocus.NodeRects.ContainsKey("com.deucarian.ui-binding"));
            Assert.IsFalse(experienceFocus.NodeRects.ContainsKey("com.deucarian.object-selection"));

            CollectionAssert.AreEquivalent(
                new[] { "com.deucarian.ui-binding", "com.deucarian.theming" },
                uiFocus.NodeRects.Keys.ToArray());
            CollectionAssert.AreEquivalent(
                new[] { "com.deucarian.object-selection" },
                worldFocus.NodeRects.Keys.ToArray());
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
            PackageGraphRingGuide rootGuide = layout.RingGuides.Single();
            Assert.That(rootGuide.Radius, Is.GreaterThan(0f));
            Assert.That(rootGuide.CircleRect.width, Is.EqualTo(rootGuide.CircleRect.height).Within(0.01f));
            Assert.That(rootGuide.CircleRect.width, Is.EqualTo(rootGuide.Radius * 2f).Within(0.01f));
            AssertVectorClose(rootGuide.Center, rootGuide.CircleRect.center, 0.01f);

            foreach (PackageGraphGroupLayoutNode groupNode in layout.GroupNodes.Where(groupNode => !groupNode.Collapsed))
            {
                Assert.That(groupNode.HubRect.width, Is.EqualTo(groupNode.HubRect.height).Within(0.1f));

                Vector2[] directChildCenters = graph.Nodes
                    .Where(node => string.Equals(node.GroupId, groupNode.GroupId, StringComparison.OrdinalIgnoreCase))
                    .Where(node => layout.NodeRects.ContainsKey(node.PackageId))
                    .Select(node => layout.NodeRects[node.PackageId].center)
                    .Concat(layout.GroupNodes
                        .Where(childGroupNode => childGroupNode.Collapsed &&
                                                 graph.TryGetGroup(childGroupNode.GroupId, out PackageGraphGroup childGroup) &&
                                                 string.Equals(
                                                     childGroup.ParentGroupId,
                                                     groupNode.GroupId,
                                                     StringComparison.OrdinalIgnoreCase))
                        .Select(childGroupNode => childGroupNode.HubCenter))
                    .ToArray();

                if (directChildCenters.Length == 0)
                {
                    Assert.AreEqual(0f, groupNode.OrbitRadius);
                    continue;
                }

                Assert.Greater(groupNode.OrbitRadius, 0f);

                foreach (Vector2 childCenter in directChildCenters)
                {
                    Assert.That(
                        Vector2.Distance(childCenter, groupNode.HubCenter),
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
                int hiddenNestedPackageCount = graph.Nodes.Count(node =>
                    node.GroupId == "ui-presentation" ||
                    node.GroupId == "world-interaction");

                rootRadii.Add(rootRadius);
                Assert.AreEqual(graph.Nodes.Count - hiddenNestedPackageCount, result.NodeRects.Count);
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
            PackageGraphGroupLayoutNode owningContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "runtime-services");
            PackageGraphGroupLayoutNode providerContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");
            PackageGraphGroupLayoutNode integrationContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "integrations");

            Assert.AreEqual(PackageGraphLayoutMode.Focus, layout.Mode);
            Assert.AreEqual("com.deucarian.session", layout.FocusPackageId);
            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, session.center), Is.LessThan(0.1f));
            Assert.Less(logging.center.x, session.center.x);
            Assert.That(logging.center.y, Is.EqualTo(PackageGraphLayout.GraphCenter.y).Within(0.1f));
            Assert.That(sessionIntegration.center.x, Is.EqualTo(PackageGraphLayout.GraphCenter.x).Within(0.1f));
            Assert.Greater(sessionIntegration.center.y, session.center.y);
            Assert.Less(owningContext.HubCenter.y, session.yMin);
            Assert.That(owningContext.HubCenter.x, Is.EqualTo(session.center.x).Within(0.1f));
            Assert.Less(providerContext.HubCenter.x, logging.xMin);
            Assert.That(providerContext.HubCenter.y, Is.EqualTo(logging.center.y).Within(0.1f));
            Assert.Greater(integrationContext.HubCenter.y, sessionIntegration.yMax);
            Assert.That(integrationContext.HubCenter.x, Is.EqualTo(sessionIntegration.center.x).Within(0.1f));
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.api"));
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.theming"));
            Assert.IsFalse(layout.HasUnrelatedSummary);
            Assert.IsTrue(layout.GroupNodes.Any(groupNode => groupNode.Collapsed && groupNode.SummaryLabel.Contains("related package")));
            CollectionAssert.Contains(
                integrationContext.RepresentedPackageIds.ToArray(),
                "com.deucarian.session.api-integration");
            CollectionAssert.Contains(
                owningContext.RepresentedPackageIds.ToArray(),
                "com.deucarian.session");
            Assert.AreEqual(layout.ActiveCenter, session.center);
            Assert.IsEmpty(layout.RingGuides);
            AssertNoOverlaps(layout.NodeRects.Values.Concat(layout.GroupNodes.Select(groupNode => groupNode.Rect)).ToArray());
        }

        [Test]
        public void Layout_EgoFocusUsesFixedCategoryRailsForApi()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.api");

            Rect api = layout.NodeRects["com.deucarian.api"];
            Rect logging = layout.NodeRects["com.deucarian.logging"];
            Rect sessionIntegration = layout.NodeRects["com.deucarian.session.api-integration"];
            Rect objectLoadingIntegration = layout.NodeRects["com.deucarian.object-loading.api-integration"];
            PackageGraphGroupLayoutNode owningContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "runtime-services");
            PackageGraphGroupLayoutNode providerContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");
            PackageGraphGroupLayoutNode integrationContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "integrations");

            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, api.center), Is.LessThan(0.1f));
            Assert.Less(logging.center.x, api.center.x);
            Assert.Less(providerContext.HubCenter.x, logging.xMin);
            Assert.That(providerContext.HubCenter.y, Is.EqualTo(logging.center.y).Within(0.1f));
            Assert.Less(owningContext.HubCenter.y, api.yMin);
            Assert.That(owningContext.HubCenter.x, Is.EqualTo(api.center.x).Within(0.1f));
            Assert.Greater(sessionIntegration.center.y, api.center.y);
            Assert.That(sessionIntegration.center.y, Is.EqualTo(objectLoadingIntegration.center.y).Within(0.1f));
            Assert.Greater(integrationContext.HubCenter.y, sessionIntegration.yMax);
            Assert.That(
                integrationContext.HubCenter.x,
                Is.EqualTo((sessionIntegration.center.x + objectLoadingIntegration.center.x) * 0.5f).Within(0.1f));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "com.deucarian.session.api-integration",
                    "com.deucarian.object-loading.api-integration"
                },
                integrationContext.RepresentedPackageIds.ToArray());
        }

        [Test]
        public void GraphMembershipRoutes_UseBusForMultiPackageContextCategory()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(graph, "com.deucarian.api", "com.deucarian.api", actionsEnabled: true);

            PackageGraphStructuralMembershipRoute integrationRoute =
                canvas.StructuralMembershipRoutesForTests.Single(route => route.GroupId == "integrations");
            PackageGraphStructuralMembershipRoute owningRoute =
                canvas.StructuralMembershipRoutesForTests.Single(route => route.GroupId == "runtime-services");

            Assert.IsTrue(integrationRoute.UsesBus);
            Assert.AreEqual(2, integrationRoute.PackageIds.Count);
            Assert.GreaterOrEqual(integrationRoute.Segments.Count, 4);
            Assert.IsTrue(integrationRoute.Segments.All(segment => segment.Length > 0.01f));
            Assert.IsFalse(owningRoute.UsesBus);
            Assert.AreEqual(1, owningRoute.PackageIds.Count);
            Assert.AreEqual("com.deucarian.api", owningRoute.PackageIds.Single());
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
            Rect api = layout.NodeRects["com.deucarian.api"];

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
                Assert.That(dependent.center.x, Is.EqualTo(api.center.x).Within(0.1f));
            }

            Assert.IsFalse(layout.HasUnrelatedSummary);
            Assert.IsTrue(layout.GroupNodes.Any(groupNode => groupNode.Collapsed));
            PackageGraphGroupLayoutNode runtimeContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "runtime-services");
            PackageGraphGroupLayoutNode owningContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");
            Assert.Less(owningContext.HubCenter.y, logging.yMin);
            Assert.That(owningContext.HubCenter.x, Is.EqualTo(logging.center.x).Within(0.1f));
            Assert.Greater(runtimeContext.HubCenter.x, api.xMax);
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

        private static Dictionary<string, Rect> CreateInterpolatedNodeRects(
            PackageGraphLayoutResult source,
            PackageGraphLayoutResult target,
            float progress)
        {
            Dictionary<string, Rect> result = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, Rect> targetRect in target.NodeRects)
            {
                Rect start = source.NodeRects.TryGetValue(targetRect.Key, out Rect sourceRect)
                    ? sourceRect
                    : CenterRectOnForTests(targetRect.Value, source.ActiveCenter);
                result[targetRect.Key] = LerpRectForTests(start, targetRect.Value, progress);
            }

            return result;
        }

        private static Dictionary<string, Rect> CreateInterpolatedGroupRects(
            PackageGraphLayoutResult source,
            PackageGraphLayoutResult target,
            float progress)
        {
            Dictionary<string, Rect> result = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphGroupLayoutNode targetGroup in target.GroupNodes)
            {
                PackageGraphGroupLayoutNode sourceGroup = FindGroupForTests(source, targetGroup.GroupId);
                Rect start = sourceGroup != null
                    ? sourceGroup.Rect
                    : CenterRectOnForTests(targetGroup.Rect, source.ActiveCenter);
                result[targetGroup.GroupId] = LerpRectForTests(start, targetGroup.Rect, progress);
            }

            return result;
        }

        private static Dictionary<string, Vector2> CreateInterpolatedGroupCenters(
            PackageGraphLayoutResult source,
            PackageGraphLayoutResult target,
            float progress)
        {
            Dictionary<string, Vector2> result = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphGroupLayoutNode targetGroup in target.GroupNodes)
            {
                PackageGraphGroupLayoutNode sourceGroup = FindGroupForTests(source, targetGroup.GroupId);
                Vector2 start = sourceGroup != null ? sourceGroup.HubCenter : source.ActiveCenter;
                result[targetGroup.GroupId] = Vector2.Lerp(start, targetGroup.HubCenter, progress);
            }

            return result;
        }

        private static Dictionary<string, float> CreateInterpolatedGroupRadii(
            PackageGraphLayoutResult source,
            PackageGraphLayoutResult target,
            float progress)
        {
            Dictionary<string, float> result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphGroupLayoutNode targetGroup in target.GroupNodes)
            {
                PackageGraphGroupLayoutNode sourceGroup = FindGroupForTests(source, targetGroup.GroupId);
                float start = sourceGroup != null ? sourceGroup.OrbitRadius : 0f;
                result[targetGroup.GroupId] = Mathf.Lerp(start, targetGroup.OrbitRadius, progress);
            }

            return result;
        }

        private static PackageGraphGroupLayoutNode FindGroupForTests(
            PackageGraphLayoutResult layout,
            string groupId)
        {
            return layout.GroupNodes.FirstOrDefault(groupNode =>
                groupNode != null &&
                string.Equals(groupNode.GroupId, groupId, StringComparison.OrdinalIgnoreCase));
        }

        private static Rect LerpRectForTests(Rect start, Rect end, float progress)
        {
            return new Rect(
                Mathf.Lerp(start.x, end.x, progress),
                Mathf.Lerp(start.y, end.y, progress),
                Mathf.Lerp(start.width, end.width, progress),
                Mathf.Lerp(start.height, end.height, progress));
        }

        private static Rect CenterRectOnForTests(Rect rect, Vector2 center)
        {
            return new Rect(
                center.x - rect.width * 0.5f,
                center.y - rect.height * 0.5f,
                rect.width,
                rect.height);
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

        private static void AssertCameraClose(
            PackageGraphCameraState expected,
            PackageGraphCameraState actual)
        {
            AssertVectorClose(expected.Pan, actual.Pan, 0.001f);
            Assert.That(actual.Zoom, Is.EqualTo(expected.Zoom).Within(0.001f));
        }

        private static void AssertVectorClose(
            Vector2 expected,
            Vector2 actual,
            float tolerance)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
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

        private static void AssertFooterElementVisible(VisualElement element)
        {
            Assert.NotNull(element);
            Assert.AreNotEqual(DisplayStyle.None, element.style.display.value);
            Assert.That(element.style.opacity.value, Is.GreaterThan(0.01f));
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

        private static VisualElement FindGraphGroup(VisualElement root, string groupId)
        {
            return FindByClass(root, "dpi-graph-group")
                .Single(group => string.Equals(group.name, "group-" + groupId, StringComparison.OrdinalIgnoreCase));
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
