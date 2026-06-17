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
        OptionalIntegration
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

    internal sealed class PackageEcosystemGraph
    {
        public PackageEcosystemGraph(
            IEnumerable<PackageEcosystemNode> nodes,
            IEnumerable<PackageEcosystemEdge> edges,
            IEnumerable<PackageEcosystemSuiteRegion> suiteRegions)
        {
            Nodes = ToReadOnlyList(nodes);
            Edges = ToReadOnlyList(edges);
            SuiteRegions = ToReadOnlyList(suiteRegions);
        }

        public IReadOnlyList<PackageEcosystemNode> Nodes { get; }

        public IReadOnlyList<PackageEcosystemEdge> Edges { get; }

        public IReadOnlyList<PackageEcosystemSuiteRegion> SuiteRegions { get; }

        private static IReadOnlyList<T> ToReadOnlyList<T>(IEnumerable<T> values)
        {
            return values == null ? Array.Empty<T>() : values.Where(value => value != null).ToArray();
        }
    }

    internal sealed class PackageEcosystemNode
    {
        public PackageEcosystemNode(
            string packageId,
            string displayName,
            string category,
            string description,
            PackageGraphNodeType nodeType,
            bool isInstalled,
            bool isRegistered,
            bool hasPackageReference,
            PackageDefinition packageDefinition)
        {
            PackageId = packageId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? PackageId : displayName.Trim();
            Category = category ?? string.Empty;
            Description = description ?? string.Empty;
            NodeType = nodeType;
            IsInstalled = isInstalled;
            IsRegistered = isRegistered;
            HasPackageReference = hasPackageReference;
            PackageDefinition = packageDefinition;
        }

        public string PackageId { get; }

        public string DisplayName { get; }

        public string Category { get; }

        public string Description { get; }

        public PackageGraphNodeType NodeType { get; }

        public bool IsInstalled { get; }

        public bool IsRegistered { get; }

        public bool HasPackageReference { get; }

        public PackageDefinition PackageDefinition { get; }
    }

    internal sealed class PackageEcosystemEdge
    {
        public PackageEcosystemEdge(
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

    internal sealed class PackageEcosystemSuiteRegion
    {
        public PackageEcosystemSuiteRegion(string suitePackageId, IEnumerable<string> memberPackageIds)
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
