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

        public string Key => CreateKey(FromPackageId, ToPackageId, Kind);

        public bool ConnectsPackage(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) &&
                   (string.Equals(FromPackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ToPackageId, packageId, StringComparison.OrdinalIgnoreCase));
        }

        public string GetOtherPackageId(string packageId)
        {
            if (string.Equals(FromPackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                return ToPackageId;
            }

            return string.Equals(ToPackageId, packageId, StringComparison.OrdinalIgnoreCase)
                ? FromPackageId
                : string.Empty;
        }

        public static string CreateKey(string fromPackageId, string toPackageId, PackageGraphEdgeKind kind)
        {
            return (fromPackageId ?? string.Empty).Trim() +
                   ">" +
                   (toPackageId ?? string.Empty).Trim() +
                   ":" +
                   kind;
        }
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

    internal sealed class PackageGraphFocus
    {
        private readonly HashSet<string> _relatedPackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _visibleEdgeKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _emphasizedEdgeKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _visibleSuiteRegionIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private PackageGraphFocus(string focusPackageId, bool hasFocus)
        {
            FocusPackageId = focusPackageId ?? string.Empty;
            HasFocus = hasFocus;
        }

        public string FocusPackageId { get; }

        public bool HasFocus { get; }

        public IReadOnlyCollection<string> RelatedPackageIds => _relatedPackageIds;

        public IReadOnlyCollection<string> VisibleEdgeKeys => _visibleEdgeKeys;

        public IReadOnlyCollection<string> EmphasizedEdgeKeys => _emphasizedEdgeKeys;

        public static PackageGraphFocus Create(
            PackageGraphModel graph,
            string focusPackageId,
            IEnumerable<string> expandedSuiteIds)
        {
            if (graph == null)
            {
                return new PackageGraphFocus(string.Empty, false);
            }

            string normalizedFocusPackageId = string.IsNullOrWhiteSpace(focusPackageId)
                ? string.Empty
                : focusPackageId.Trim();

            bool hasFocus = !string.IsNullOrWhiteSpace(normalizedFocusPackageId) &&
                            graph.TryGetNode(normalizedFocusPackageId, out _);
            PackageGraphFocus focus = new PackageGraphFocus(normalizedFocusPackageId, hasFocus);
            HashSet<string> expandedSuites = ToSet(expandedSuiteIds);

            if (!hasFocus)
            {
                focus.AddOverviewEdges(graph, expandedSuites);
                return focus;
            }

            focus._relatedPackageIds.Add(normalizedFocusPackageId);
            focus.AddDirectEdges(graph, normalizedFocusPackageId);
            focus.AddBridgeContext(graph);
            focus.AddSuiteContext(graph, normalizedFocusPackageId);
            focus.AddWarningContext(graph);

            return focus;
        }

        public bool IsPackageRelated(string packageId)
        {
            return !HasFocus ||
                   (!string.IsNullOrWhiteSpace(packageId) && _relatedPackageIds.Contains(packageId.Trim()));
        }

        public bool IsEdgeVisible(PackageGraphEdge edge)
        {
            return edge != null && _visibleEdgeKeys.Contains(edge.Key);
        }

        public bool IsEdgeEmphasized(PackageGraphEdge edge)
        {
            return edge != null && _emphasizedEdgeKeys.Contains(edge.Key);
        }

        public bool IsSuiteRegionVisible(PackageGraphSuiteRegion region)
        {
            return region != null && _visibleSuiteRegionIds.Contains(region.SuitePackageId);
        }

        private void AddOverviewEdges(PackageGraphModel graph, ISet<string> expandedSuites)
        {
            foreach (PackageGraphEdge edge in graph.Edges)
            {
                if (edge.State == PackageGraphEdgeState.Warning ||
                    (edge.Kind == PackageGraphEdgeKind.HardDependency &&
                     edge.State == PackageGraphEdgeState.Active) ||
                    (edge.Kind == PackageGraphEdgeKind.SuiteMembership &&
                     expandedSuites.Contains(edge.FromPackageId)))
                {
                    AddVisibleEdge(edge, emphasized: edge.State == PackageGraphEdgeState.Warning);
                }
            }

            foreach (PackageGraphSuiteRegion region in graph.SuiteRegions)
            {
                if (expandedSuites.Contains(region.SuitePackageId))
                {
                    _visibleSuiteRegionIds.Add(region.SuitePackageId);
                }
            }
        }

        private void AddDirectEdges(PackageGraphModel graph, string packageId)
        {
            foreach (PackageGraphEdge edge in graph.Edges)
            {
                if (!edge.ConnectsPackage(packageId))
                {
                    continue;
                }

                AddFocusEdge(edge);
                _relatedPackageIds.Add(edge.GetOtherPackageId(packageId));
            }
        }

        private void AddBridgeContext(PackageGraphModel graph)
        {
            string[] bridgePackageIds = graph.Nodes
                .Where(node => node.NodeType == PackageGraphNodeType.Bridge &&
                               _relatedPackageIds.Contains(node.PackageId))
                .Select(node => node.PackageId)
                .ToArray();

            foreach (string bridgePackageId in bridgePackageIds)
            {
                foreach (PackageGraphEdge edge in graph.Edges.Where(edge =>
                             edge.Kind == PackageGraphEdgeKind.Bridge &&
                             edge.ConnectsPackage(bridgePackageId)))
                {
                    AddFocusEdge(edge);
                    _relatedPackageIds.Add(edge.FromPackageId);
                    _relatedPackageIds.Add(edge.ToPackageId);
                }
            }
        }

        private void AddSuiteContext(PackageGraphModel graph, string packageId)
        {
            foreach (PackageGraphSuiteRegion region in graph.SuiteRegions)
            {
                bool packageIsSuite = string.Equals(
                    region.SuitePackageId,
                    packageId,
                    StringComparison.OrdinalIgnoreCase);
                bool packageIsMember = region.MemberPackageIds.Any(memberPackageId =>
                    string.Equals(memberPackageId, packageId, StringComparison.OrdinalIgnoreCase));

                if (!packageIsSuite && !packageIsMember)
                {
                    continue;
                }

                _visibleSuiteRegionIds.Add(region.SuitePackageId);
                _relatedPackageIds.Add(region.SuitePackageId);

                foreach (string memberPackageId in region.MemberPackageIds)
                {
                    _relatedPackageIds.Add(memberPackageId);
                }

                foreach (PackageGraphEdge edge in graph.Edges.Where(edge =>
                             edge.Kind == PackageGraphEdgeKind.SuiteMembership &&
                             string.Equals(
                                 edge.FromPackageId,
                                 region.SuitePackageId,
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    AddFocusEdge(edge);
                }
            }
        }

        private void AddWarningContext(PackageGraphModel graph)
        {
            foreach (PackageGraphEdge edge in graph.Edges)
            {
                if (edge.State != PackageGraphEdgeState.Warning)
                {
                    continue;
                }

                if (_relatedPackageIds.Contains(edge.FromPackageId) ||
                    _relatedPackageIds.Contains(edge.ToPackageId))
                {
                    AddFocusEdge(edge);
                    _relatedPackageIds.Add(edge.FromPackageId);
                    _relatedPackageIds.Add(edge.ToPackageId);
                }
            }
        }

        private void AddFocusEdge(PackageGraphEdge edge)
        {
            AddVisibleEdge(edge, emphasized: true);
        }

        private void AddVisibleEdge(PackageGraphEdge edge, bool emphasized)
        {
            if (edge == null)
            {
                return;
            }

            _visibleEdgeKeys.Add(edge.Key);

            if (emphasized)
            {
                _emphasizedEdgeKeys.Add(edge.Key);
            }
        }

        private static HashSet<string> ToSet(IEnumerable<string> values)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (values == null)
            {
                return set;
            }

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    set.Add(value.Trim());
                }
            }

            return set;
        }
    }
}
