using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphBuilder
    {
        private readonly Func<string, bool> _isInstalled;
        private readonly Func<PackageDefinition, PackageChannel> _selectChannel;
        private readonly Func<PackageDefinition, PackageUpdateStatus> _getUpdateStatus;
        private readonly Dictionary<string, PackageGraphNode> _nodes =
            new Dictionary<string, PackageGraphNode>(StringComparer.OrdinalIgnoreCase);

        public PackageGraphBuilder(Func<string, bool> isInstalled)
            : this(isInstalled, _ => PackageChannel.Stable, null)
        {
        }

        public PackageGraphBuilder(
            Func<string, bool> isInstalled,
            Func<PackageDefinition, PackageChannel> selectChannel,
            Func<PackageDefinition, PackageUpdateStatus> getUpdateStatus)
        {
            _isInstalled = isInstalled ?? (_ => false);
            _selectChannel = selectChannel ?? (_ => PackageChannel.Stable);
            _getUpdateStatus = getUpdateStatus;
        }

        public PackageGraphModel Build(IEnumerable<PackageDefinition> packages)
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

            List<PackageGraphEdge> edges = new List<PackageGraphEdge>();
            List<PackageGraphSuiteRegion> suiteRegions = new List<PackageGraphSuiteRegion>();
            HashSet<string> edgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageDefinition package in definitions)
            {
                if (package.IsSuite)
                {
                    string[] suiteMembers = GetSuiteMembers(package).ToArray();
                    suiteRegions.Add(new PackageGraphSuiteRegion(package.PackageId, suiteMembers));

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
                            "Bridge target");
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

                foreach (string companionId in package.OptionalCompanions)
                {
                    AddRelationshipEdge(
                        edges,
                        edgeKeys,
                        package.PackageId,
                        companionId,
                        PackageGraphEdgeKind.OptionalIntegration,
                        GetPeerRelationshipState(package.PackageId, companionId),
                        "Optional companion");
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

            ApplyRequiredByInstalledPackageWarnings(edges);

            return new PackageGraphModel(
                _nodes.Values
                    .OrderBy(node => GetNodeTypeSortIndex(node.NodeType))
                    .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase),
                edges
                    .OrderBy(edge => GetEdgeSortIndex(edge.Kind))
                    .ThenBy(edge => edge.FromPackageId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(edge => edge.ToPackageId, StringComparer.OrdinalIgnoreCase),
                suiteRegions);
        }

        private void ApplyRequiredByInstalledPackageWarnings(IEnumerable<PackageGraphEdge> edges)
        {
            foreach (PackageGraphEdge edge in edges ?? Array.Empty<PackageGraphEdge>())
            {
                if (edge.State != PackageGraphEdgeState.Warning)
                {
                    continue;
                }

                string requiredPackageId = GetRequiredPackageId(edge);

                if (string.IsNullOrWhiteSpace(requiredPackageId) ||
                    !_nodes.TryGetValue(requiredPackageId, out PackageGraphNode requiredNode) ||
                    !requiredNode.IsRegistered ||
                    requiredNode.IsInstalled ||
                    requiredNode.Status != PackageGraphNodeStatus.NotInstalled)
                {
                    continue;
                }

                _nodes[requiredPackageId] = new PackageGraphNode(
                    requiredNode.PackageId,
                    requiredNode.DisplayName,
                    requiredNode.Category,
                    requiredNode.Description,
                    requiredNode.NodeType,
                    PackageGraphNodeStatus.Warning,
                    requiredNode.SelectedChannel,
                    requiredNode.IsInstalled,
                    requiredNode.IsRegistered,
                    requiredNode.HasPackageReference,
                    requiredNode.IconKey,
                    "Required by installed package",
                    requiredNode.PackageDefinition);
            }
        }

        private static string GetRequiredPackageId(PackageGraphEdge edge)
        {
            switch (edge.Kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                case PackageGraphEdgeKind.Bridge:
                    return edge.FromPackageId;
                case PackageGraphEdgeKind.SuiteMembership:
                    return edge.ToPackageId;
                default:
                    return string.Empty;
            }
        }

        private PackageGraphNode EnsureNode(string packageId, PackageDefinition package)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                packageId = "unknown-package";
            }

            if (_nodes.TryGetValue(packageId, out PackageGraphNode existingNode))
            {
                return existingNode;
            }

            PackageGraphNode node;

            if (package != null)
            {
                bool installed = _isInstalled(package.PackageId);
                PackageChannel selectedChannel = _selectChannel(package);
                PackageUpdateStatus updateStatus = _getUpdateStatus != null
                    ? _getUpdateStatus(package)
                    : null;

                node = new PackageGraphNode(
                    package.PackageId,
                    package.DisplayName,
                    package.Category,
                    package.Description,
                    GetNodeType(package),
                    GetNodeStatus(installed, updateStatus),
                    selectedChannel,
                    installed,
                    true,
                    package.HasPackageReference,
                    GetIconKey(package),
                    updateStatus != null ? updateStatus.Label : string.Empty,
                    package);
            }
            else
            {
                node = new PackageGraphNode(
                    packageId.Trim(),
                    packageId.Trim(),
                    "Missing",
                    "Registry relationship target is not registered.",
                    PackageGraphNodeType.Integration,
                    PackageGraphNodeStatus.Missing,
                    PackageChannel.Stable,
                    false,
                    false,
                    false,
                    "package",
                    "Missing registry entry",
                    null);
            }

            _nodes[node.PackageId] = node;
            return node;
        }

        private void AddRelationshipEdge(
            ICollection<PackageGraphEdge> edges,
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

            edges.Add(new PackageGraphEdge(
                fromPackageId.Trim(),
                toPackageId.Trim(),
                kind,
                state,
                label));
        }

        private PackageGraphEdgeState GetOwnedRelationshipState(string ownerPackageId, string targetPackageId)
        {
            PackageGraphNode owner = EnsureNode(ownerPackageId, null);
            PackageGraphNode target = EnsureNode(targetPackageId, null);

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
            PackageGraphNode from = EnsureNode(fromPackageId, null);
            PackageGraphNode to = EnsureNode(toPackageId, null);

            if (!from.IsRegistered || !to.IsRegistered)
            {
                return PackageGraphEdgeState.Warning;
            }

            return from.IsInstalled && to.IsInstalled
                ? PackageGraphEdgeState.Active
                : PackageGraphEdgeState.Possible;
        }

        private static PackageGraphNodeStatus GetNodeStatus(bool installed, PackageUpdateStatus updateStatus)
        {
            if (!installed)
            {
                return PackageGraphNodeStatus.NotInstalled;
            }

            if (updateStatus == null)
            {
                return PackageGraphNodeStatus.Installed;
            }

            switch (updateStatus.Kind)
            {
                case PackageUpdateStatusKind.Checking:
                    return PackageGraphNodeStatus.Checking;
                case PackageUpdateStatusKind.UpdateAvailable:
                case PackageUpdateStatusKind.SwitchAvailable:
                    return PackageGraphNodeStatus.UpdateAvailable;
                case PackageUpdateStatusKind.CannotDetermine:
                case PackageUpdateStatusKind.Failed:
                    return PackageGraphNodeStatus.Warning;
                default:
                    return PackageGraphNodeStatus.Installed;
            }
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
                string.Equals(package.Category, "Tools", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "Editor", StringComparison.OrdinalIgnoreCase))
            {
                return PackageGraphNodeType.Tool;
            }

            if (string.Equals(package.MetadataType, "OptionalIntegration", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.MetadataType, "Integration", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "UI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "World", StringComparison.OrdinalIgnoreCase))
            {
                return PackageGraphNodeType.Integration;
            }

            return PackageGraphNodeType.Core;
        }

        private static string GetIconKey(PackageDefinition package)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.PackageId))
            {
                return "package";
            }

            string key = package.PackageId.Trim();

            if (key.StartsWith("com.deucarian.", StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring("com.deucarian.".Length);
            }

            if (key.StartsWith("object-selection", StringComparison.OrdinalIgnoreCase))
            {
                return "selection";
            }

            if (key.StartsWith("api", StringComparison.OrdinalIgnoreCase))
            {
                return "api-helper";
            }

            if (key.StartsWith("ui-binding", StringComparison.OrdinalIgnoreCase))
            {
                return "generic-ui-items";
            }

            int separatorIndex = key.IndexOf('.');
            if (separatorIndex >= 0)
            {
                key = key.Substring(0, separatorIndex);
            }

            return string.IsNullOrWhiteSpace(key) ? "package" : key;
        }

        private static int GetNodeTypeSortIndex(PackageDefinition package)
        {
            return GetNodeTypeSortIndex(GetNodeType(package));
        }

        internal static int GetNodeTypeSortIndex(PackageGraphNodeType nodeType)
        {
            switch (nodeType)
            {
                case PackageGraphNodeType.Tool:
                    return 0;
                case PackageGraphNodeType.Core:
                    return 1;
                case PackageGraphNodeType.Integration:
                    return 2;
                case PackageGraphNodeType.Bridge:
                    return 3;
                case PackageGraphNodeType.Suite:
                    return 4;
                default:
                    return 5;
            }
        }

        private static int GetEdgeSortIndex(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                    return 0;
                case PackageGraphEdgeKind.Bridge:
                    return 1;
                case PackageGraphEdgeKind.OptionalIntegration:
                    return 2;
                case PackageGraphEdgeKind.Recommended:
                    return 3;
                case PackageGraphEdgeKind.SuiteMembership:
                    return 4;
                default:
                    return 5;
            }
        }
    }
}
