using System;
using System.Linq;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageEcosystemGraphBuilderTests
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

            PackageEcosystemGraph graph = new PackageEcosystemGraphBuilder(
                    packageId => packageId == core.PackageId || packageId == bridge.PackageId)
                .Build(new[] { core, tool, optional, bridge, suite });

            Assert.AreEqual(PackageGraphNodeType.Core, graph.Nodes.Single(node => node.PackageId == core.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Tool, graph.Nodes.Single(node => node.PackageId == tool.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.OptionalIntegration, graph.Nodes.Single(node => node.PackageId == optional.PackageId).NodeType);
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

            PackageEcosystemGraph graph = new PackageEcosystemGraphBuilder(_ => false)
                .Build(new[] { package });

            PackageEcosystemNode missingNode = graph.Nodes.Single(node => node.PackageId == "com.example.missing");
            PackageEcosystemEdge warningEdge = graph.Edges.Single(edge => edge.ToPackageId == missingNode.PackageId);

            Assert.IsFalse(missingNode.IsRegistered);
            Assert.AreEqual(PackageGraphEdgeState.Warning, warningEdge.State);
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
                category: category,
                metadataType: metadataType,
                optionalIntegrations: optionalIntegrations,
                bridgeTargets: bridgeTargets,
                suiteMembers: suiteMembers);
        }
    }
}
