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
                PackageGraphNodeAction.Install,
                graph.Nodes.Single(node => node.PackageId == missing.PackageId).PrimaryAction);
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

            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-edge-layer"));
            Assert.AreEqual(3, FindByClass(view, "dpi-graph-node").Count);
            Assert.AreEqual(1, FindByClass(view, "dpi-graph-node--bridge").Count);
            Assert.IsTrue(FindByClass(view, "dpi-graph-node__action").Count >= 1);
        }

        private static PackageDefinition CreatePackage(
            string displayName,
            string packageId,
            string category,
            string metadataType = null,
            string[] dependencies = null,
            string[] optionalIntegrations = null,
            string[] bridgeTargets = null,
            string[] suiteMembers = null)
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
                suiteMembers: suiteMembers);
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
