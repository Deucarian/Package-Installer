using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageGraphNodeType
    {
        Core,
        Tool,
        Bridge,
        Suite,
        Integration
    }

    internal enum PackageGraphEdgeKind
    {
        HardDependency,
        OptionalIntegration,
        Bridge,
        SuiteMembership,
        Recommended
    }

    internal enum PackageGraphEdgeState
    {
        Active,
        Possible,
        Warning
    }

    internal enum PackageGraphNodeStatus
    {
        Missing,
        NotInstalled,
        Installed,
        UpdateAvailable,
        Checking,
        Warning
    }

    internal enum PackageGraphNodeAction
    {
        None,
        Install,
        Update,
        Reinstall
    }

    internal sealed class PackageGraphModel
    {
        public PackageGraphModel(
            IEnumerable<PackageGraphNode> nodes,
            IEnumerable<PackageGraphEdge> edges,
            IEnumerable<PackageGraphSuiteRegion> suiteRegions)
        {
            Nodes = ToReadOnlyList(nodes);
            Edges = ToReadOnlyList(edges);
            SuiteRegions = ToReadOnlyList(suiteRegions);
        }

        public IReadOnlyList<PackageGraphNode> Nodes { get; }

        public IReadOnlyList<PackageGraphEdge> Edges { get; }

        public IReadOnlyList<PackageGraphSuiteRegion> SuiteRegions { get; }

        public bool TryGetNode(string packageId, out PackageGraphNode node)
        {
            node = Nodes.FirstOrDefault(candidate =>
                string.Equals(candidate.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            return node != null;
        }

        private static IReadOnlyList<T> ToReadOnlyList<T>(IEnumerable<T> values)
        {
            return values == null ? Array.Empty<T>() : values.Where(value => value != null).ToArray();
        }
    }

    internal sealed class PackageGraphNode
    {
        public PackageGraphNode(
            string packageId,
            string displayName,
            string category,
            string description,
            PackageGraphNodeType nodeType,
            PackageGraphNodeStatus status,
            PackageChannel selectedChannel,
            bool isInstalled,
            bool isRegistered,
            bool hasPackageReference,
            string iconKey,
            string updateStatusLabel,
            PackageDefinition packageDefinition)
        {
            PackageId = packageId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? PackageId : displayName.Trim();
            Category = category ?? string.Empty;
            Description = description ?? string.Empty;
            NodeType = nodeType;
            Status = status;
            SelectedChannel = selectedChannel;
            IsInstalled = isInstalled;
            IsRegistered = isRegistered;
            HasPackageReference = hasPackageReference;
            IconKey = iconKey ?? string.Empty;
            UpdateStatusLabel = updateStatusLabel ?? string.Empty;
            PackageDefinition = packageDefinition;
        }

        public string PackageId { get; }

        public string DisplayName { get; }

        public string Category { get; }

        public string Description { get; }

        public PackageGraphNodeType NodeType { get; }

        public PackageGraphNodeStatus Status { get; }

        public PackageChannel SelectedChannel { get; }

        public bool IsInstalled { get; }

        public bool IsRegistered { get; }

        public bool HasPackageReference { get; }

        public string IconKey { get; }

        public string UpdateStatusLabel { get; }

        public PackageDefinition PackageDefinition { get; }

        public PackageGraphNodeAction PrimaryAction
        {
            get
            {
                if (!IsRegistered || PackageDefinition == null || !HasPackageReference)
                {
                    return PackageGraphNodeAction.None;
                }

                if (!IsInstalled)
                {
                    return PackageGraphNodeAction.Install;
                }

                return Status == PackageGraphNodeStatus.UpdateAvailable
                    ? PackageGraphNodeAction.Update
                    : PackageGraphNodeAction.Reinstall;
            }
        }

        public string PrimaryActionLabel
        {
            get
            {
                switch (PrimaryAction)
                {
                    case PackageGraphNodeAction.Install:
                        return IsBridge ? "Install Bridge" : "Install";
                    case PackageGraphNodeAction.Update:
                        return "Update";
                    case PackageGraphNodeAction.Reinstall:
                        return "Reinstall";
                    default:
                        return string.Empty;
                }
            }
        }

        public bool IsBridge => NodeType == PackageGraphNodeType.Bridge;
    }

    internal sealed class PackageGraphEdge
    {
        public PackageGraphEdge(
            string fromPackageId,
            string toPackageId,
            PackageGraphEdgeKind kind,
            PackageGraphEdgeState state,
            string label)
        {
            FromPackageId = fromPackageId ?? string.Empty;
            ToPackageId = toPackageId ?? string.Empty;
            Kind = kind;
            State = state;
            Label = label ?? string.Empty;
        }

        public string FromPackageId { get; }

        public string ToPackageId { get; }

        public PackageGraphEdgeKind Kind { get; }

        public PackageGraphEdgeState State { get; }

        public string Label { get; }
    }

    internal sealed class PackageGraphSuiteRegion
    {
        public PackageGraphSuiteRegion(string suitePackageId, IEnumerable<string> memberPackageIds)
        {
            SuitePackageId = suitePackageId ?? string.Empty;
            MemberPackageIds = memberPackageIds == null
                ? Array.Empty<string>()
                : memberPackageIds
                    .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
                    .Select(packageId => packageId.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        public string SuitePackageId { get; }

        public IReadOnlyList<string> MemberPackageIds { get; }
    }
}
