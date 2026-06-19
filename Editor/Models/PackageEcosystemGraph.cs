using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageGraphNodeType
    {
        Core,
        Tool,
        Integration,
        Suite,
        Companion
    }

    internal enum PackageGraphNodeKind
    {
        Root,
        Group,
        Package
    }

    internal enum PackageGraphEdgeKind
    {
        HardDependency,
        IntegrationConnection,
        OptionalCompanion,
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

    internal enum PackageGraphCategoryStatusKey
    {
        Installed,
        NotInstalled,
        Attention,
        Unknown
    }

    internal readonly struct PackageGraphCategoryStatusSummary
    {
        public PackageGraphCategoryStatusSummary(
            int installedCount,
            int notInstalledCount,
            int attentionCount,
            int unknownCount)
        {
            InstalledCount = Math.Max(0, installedCount);
            NotInstalledCount = Math.Max(0, notInstalledCount);
            AttentionCount = Math.Max(0, attentionCount);
            UnknownCount = Math.Max(0, unknownCount);
        }

        public int InstalledCount { get; }

        public int NotInstalledCount { get; }

        public int AttentionCount { get; }

        public int UnknownCount { get; }

        public int TotalCount => InstalledCount + NotInstalledCount + AttentionCount + UnknownCount;

        public int GetCount(PackageGraphCategoryStatusKey statusKey)
        {
            switch (statusKey)
            {
                case PackageGraphCategoryStatusKey.Installed:
                    return InstalledCount;
                case PackageGraphCategoryStatusKey.NotInstalled:
                    return NotInstalledCount;
                case PackageGraphCategoryStatusKey.Attention:
                    return AttentionCount;
                default:
                    return UnknownCount;
            }
        }

        public static PackageGraphCategoryStatusSummary Create(IEnumerable<PackageGraphNode> packages)
        {
            int installed = 0;
            int notInstalled = 0;
            int attention = 0;
            int unknown = 0;

            foreach (PackageGraphNode package in packages ?? Enumerable.Empty<PackageGraphNode>())
            {
                switch (PackageGraphCategoryStatusClassifier.Classify(package))
                {
                    case PackageGraphCategoryStatusKey.Attention:
                        attention++;
                        break;
                    case PackageGraphCategoryStatusKey.Installed:
                        installed++;
                        break;
                    case PackageGraphCategoryStatusKey.NotInstalled:
                        notInstalled++;
                        break;
                    default:
                        unknown++;
                        break;
                }
            }

            return new PackageGraphCategoryStatusSummary(installed, notInstalled, attention, unknown);
        }
    }

    internal static class PackageGraphCategoryStatusClassifier
    {
        public static PackageGraphCategoryStatusKey Classify(PackageGraphNode package)
        {
            if (package == null)
            {
                return PackageGraphCategoryStatusKey.Unknown;
            }

            switch (package.Status)
            {
                case PackageGraphNodeStatus.UpdateAvailable:
                case PackageGraphNodeStatus.Missing:
                case PackageGraphNodeStatus.Warning:
                    return PackageGraphCategoryStatusKey.Attention;
                case PackageGraphNodeStatus.Installed:
                    return PackageGraphCategoryStatusKey.Installed;
                case PackageGraphNodeStatus.NotInstalled:
                    return PackageGraphCategoryStatusKey.NotInstalled;
                default:
                    return package.IsInstalled
                        ? PackageGraphCategoryStatusKey.Unknown
                        : PackageGraphCategoryStatusKey.NotInstalled;
            }
        }
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
            IEnumerable<PackageGraphSuiteRegion> suiteRegions,
            IEnumerable<PackageGraphGroup> groups = null)
        {
            Nodes = ToReadOnlyList(nodes);
            Edges = ToReadOnlyList(edges);
            SuiteRegions = ToReadOnlyList(suiteRegions);
            Groups = ToReadOnlyList(groups);
        }

        public IReadOnlyList<PackageGraphNode> Nodes { get; }

        public IReadOnlyList<PackageGraphEdge> Edges { get; }

        public IReadOnlyList<PackageGraphSuiteRegion> SuiteRegions { get; }

        public IReadOnlyList<PackageGraphGroup> Groups { get; }

        public bool TryGetNode(string packageId, out PackageGraphNode node)
        {
            node = Nodes.FirstOrDefault(candidate =>
                string.Equals(candidate.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            return node != null;
        }

        public bool TryGetGroup(string groupId, out PackageGraphGroup group)
        {
            group = Groups.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, groupId, StringComparison.OrdinalIgnoreCase));
            return group != null;
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
            string groupId,
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
            GroupId = string.IsNullOrWhiteSpace(groupId) ? string.Empty : groupId.Trim();
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

        public PackageGraphNodeKind Kind => PackageGraphNodeKind.Package;

        public string GroupId { get; }

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
                        return IsIntegration ? "Install Integration" : "Install";
                    case PackageGraphNodeAction.Update:
                        return "Update";
                    case PackageGraphNodeAction.Reinstall:
                        return "Reinstall";
                    default:
                        return string.Empty;
                }
            }
        }

        public bool IsIntegration => NodeType == PackageGraphNodeType.Integration;
    }

    internal sealed class PackageGraphGroup
    {
        public PackageGraphGroup(
            string id,
            string displayName,
            string parentGroupId,
            string description,
            int sortOrder,
            string iconKey = null,
            string styleKey = null)
        {
            Id = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
            ParentGroupId = string.IsNullOrWhiteSpace(parentGroupId) ? string.Empty : parentGroupId.Trim();
            Description = description ?? string.Empty;
            SortOrder = Math.Max(0, sortOrder);
            IconKey = string.IsNullOrWhiteSpace(iconKey) ? "package" : iconKey.Trim();
            StyleKey = string.IsNullOrWhiteSpace(styleKey) ? Id : styleKey.Trim();
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string ParentGroupId { get; }

        public string Description { get; }

        public int SortOrder { get; }

        public string IconKey { get; }

        public string StyleKey { get; }

        public PackageGraphNodeKind Kind => PackageGraphNodeKind.Group;
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
            string focusPackageId)
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

            if (!hasFocus)
            {
                focus.AddOverviewEdges(graph);
                return focus;
            }

            focus._relatedPackageIds.Add(normalizedFocusPackageId);
            focus.AddDirectEdges(graph, normalizedFocusPackageId);

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
            return false;
        }

        private void AddOverviewEdges(PackageGraphModel graph)
        {
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

    }
}
