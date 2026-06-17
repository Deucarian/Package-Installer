using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageEcosystemGraphBuilder
    {
        private readonly Func<string, bool> _isInstalled;
        private readonly Dictionary<string, PackageEcosystemNode> _nodes =
            new Dictionary<string, PackageEcosystemNode>(StringComparer.OrdinalIgnoreCase);

        public PackageEcosystemGraphBuilder(Func<string, bool> isInstalled)
        {
            _isInstalled = isInstalled ?? (_ => false);
        }

        public PackageEcosystemGraph Build(IEnumerable<PackageDefinition> packages)
        {
            _nodes.Clear();

            PackageDefinition[] definitions = packages == null
                ? Array.Empty<PackageDefinition>()
                : packages
                    .Where(package => package != null)
                    .OrderBy(GetNodeTypeSortIndex)
                    .ThenBy(package => package.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            foreach (PackageDefinition package in definitions)
            {
                EnsureNode(package.PackageId, package);
            }

            List<PackageEcosystemEdge> edges = new List<PackageEcosystemEdge>();
            List<PackageEcosystemSuiteRegion> suiteRegions = new List<PackageEcosystemSuiteRegion>();
            HashSet<string> edgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageDefinition package in definitions)
            {
                if (package.IsSuite)
                {
                    string[] suiteMembers = GetSuiteMembers(package).ToArray();
                    suiteRegions.Add(new PackageEcosystemSuiteRegion(package.PackageId, suiteMembers));

                    foreach (string memberId in suiteMembers)
                    {
                        AddRelationshipEdge(
                            edges,
                            edgeKeys,
                            package.PackageId,
                            memberId,
                            PackageGraphEdgeKind.SuiteMembership,
                            GetOwnedRelationshipState(package.PackageId, memberId),
                            "Suite member");
                    }

                    continue;
                }

                if (package.IsBridge)
                {
                    foreach (string targetId in GetBridgeTargets(package))
                    {
                        AddRelationshipEdge(
                            edges,
                            edgeKeys,
                            targetId,
                            package.PackageId,
                            PackageGraphEdgeKind.Bridge,
                            GetOwnedRelationshipState(package.PackageId, targetId),
                            "Bridge");
                    }
                }
                else
                {
                    foreach (string dependencyId in package.Dependencies)
                    {
                        AddRelationshipEdge(
                            edges,
                            edgeKeys,
                            dependencyId,
                            package.PackageId,
                            PackageGraphEdgeKind.HardDependency,
                            GetOwnedRelationshipState(package.PackageId, dependencyId),
                            "Dependency");
                    }
                }

                foreach (string integrationId in package.OptionalIntegrations)
                {
                    AddRelationshipEdge(
                        edges,
                        edgeKeys,
                        package.PackageId,
                        integrationId,
                        PackageGraphEdgeKind.OptionalIntegration,
                        GetPeerRelationshipState(package.PackageId, integrationId),
                        "Optional integration");
                }

                foreach (string recommendedId in package.RecommendedWith)
                {
                    AddRelationshipEdge(
                        edges,
                        edgeKeys,
                        package.PackageId,
                        recommendedId,
                        PackageGraphEdgeKind.Recommended,
                        GetPeerRelationshipState(package.PackageId, recommendedId),
                        "Recommended");
                }
            }

            return new PackageEcosystemGraph(
                _nodes.Values
                    .OrderBy(node => GetNodeTypeSortIndex(node.NodeType))
                    .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase),
                edges,
                suiteRegions);
        }

        private PackageEcosystemNode EnsureNode(string packageId, PackageDefinition package)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                packageId = "unknown-package";
            }

            if (_nodes.TryGetValue(packageId, out PackageEcosystemNode existingNode))
            {
                return existingNode;
            }

            PackageEcosystemNode node;

            if (package != null)
            {
                node = new PackageEcosystemNode(
                    package.PackageId,
                    package.DisplayName,
                    package.Category,
                    package.Description,
                    GetNodeType(package),
                    _isInstalled(package.PackageId),
                    true,
                    package.HasPackageReference,
                    package);
            }
            else
            {
                node = new PackageEcosystemNode(
                    packageId.Trim(),
                    packageId.Trim(),
                    "Missing",
                    "Registry relationship target is not registered.",
                    PackageGraphNodeType.OptionalIntegration,
                    false,
                    false,
                    false,
                    null);
            }

            _nodes[node.PackageId] = node;
            return node;
        }

        private void AddRelationshipEdge(
            ICollection<PackageEcosystemEdge> edges,
            ISet<string> edgeKeys,
            string fromPackageId,
            string toPackageId,
            PackageGraphEdgeKind kind,
            PackageGraphEdgeState state,
            string label)
        {
            if (string.IsNullOrWhiteSpace(fromPackageId) || string.IsNullOrWhiteSpace(toPackageId))
            {
                return;
            }

            EnsureNode(fromPackageId, null);
            EnsureNode(toPackageId, null);

            string edgeKey = fromPackageId.Trim() + ">" + toPackageId.Trim() + ":" + kind;

            if (!edgeKeys.Add(edgeKey))
            {
                return;
            }

            edges.Add(new PackageEcosystemEdge(
                fromPackageId.Trim(),
                toPackageId.Trim(),
                kind,
                state,
                label));
        }

        private PackageGraphEdgeState GetOwnedRelationshipState(string ownerPackageId, string targetPackageId)
        {
            PackageEcosystemNode owner = EnsureNode(ownerPackageId, null);
            PackageEcosystemNode target = EnsureNode(targetPackageId, null);

            if (!owner.IsRegistered || !target.IsRegistered)
            {
                return PackageGraphEdgeState.Warning;
            }

            if (owner.IsInstalled && !target.IsInstalled)
            {
                return PackageGraphEdgeState.Warning;
            }

            return owner.IsInstalled && target.IsInstalled
                ? PackageGraphEdgeState.Active
                : PackageGraphEdgeState.Possible;
        }

        private PackageGraphEdgeState GetPeerRelationshipState(string fromPackageId, string toPackageId)
        {
            PackageEcosystemNode from = EnsureNode(fromPackageId, null);
            PackageEcosystemNode to = EnsureNode(toPackageId, null);

            if (!from.IsRegistered || !to.IsRegistered)
            {
                return PackageGraphEdgeState.Warning;
            }

            return from.IsInstalled && to.IsInstalled
                ? PackageGraphEdgeState.Active
                : PackageGraphEdgeState.Possible;
        }

        private static IEnumerable<string> GetBridgeTargets(PackageDefinition package)
        {
            return package.BridgeTargets.Count > 0 ? package.BridgeTargets : package.Dependencies;
        }

        private static IEnumerable<string> GetSuiteMembers(PackageDefinition package)
        {
            return package.SuiteMembers.Count > 0 ? package.SuiteMembers : package.Dependencies;
        }

        private static PackageGraphNodeType GetNodeType(PackageDefinition package)
        {
            if (package.IsBridge)
            {
                return PackageGraphNodeType.Bridge;
            }

            if (package.IsSuite)
            {
                return PackageGraphNodeType.Suite;
            }

            if (string.Equals(package.MetadataType, "Tool", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "Tools", StringComparison.OrdinalIgnoreCase))
            {
                return PackageGraphNodeType.Tool;
            }

            if (string.Equals(package.MetadataType, "OptionalIntegration", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.MetadataType, "Integration", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "UI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "World", StringComparison.OrdinalIgnoreCase))
            {
                return PackageGraphNodeType.OptionalIntegration;
            }

            return PackageGraphNodeType.Core;
        }

        private static int GetNodeTypeSortIndex(PackageDefinition package)
        {
            return GetNodeTypeSortIndex(GetNodeType(package));
        }

        private static int GetNodeTypeSortIndex(PackageGraphNodeType nodeType)
        {
            switch (nodeType)
            {
                case PackageGraphNodeType.Core:
                    return 0;
                case PackageGraphNodeType.Tool:
                    return 1;
                case PackageGraphNodeType.OptionalIntegration:
                    return 2;
                case PackageGraphNodeType.Bridge:
                    return 3;
                case PackageGraphNodeType.Suite:
                    return 4;
                default:
                    return 5;
            }
        }
    }
}
