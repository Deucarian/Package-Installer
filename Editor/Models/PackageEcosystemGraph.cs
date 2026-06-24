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
        private static readonly IReadOnlyList<PackageGraphNode> EmptyNodes =
            Array.Empty<PackageGraphNode>();
        private static readonly IReadOnlyList<PackageGraphGroup> EmptyGroups =
            Array.Empty<PackageGraphGroup>();
        private static readonly IReadOnlyList<PackageGraphEdge> EmptyEdges =
            Array.Empty<PackageGraphEdge>();
        private static readonly IReadOnlyList<PackageGraphSuiteRegion> EmptySuiteRegions =
            Array.Empty<PackageGraphSuiteRegion>();

        private readonly Dictionary<string, PackageGraphNode> _nodeById =
            new Dictionary<string, PackageGraphNode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageGraphGroup> _groupById =
            new Dictionary<string, PackageGraphGroup>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<PackageGraphNode>> _nodesByGroupId =
            new Dictionary<string, IReadOnlyList<PackageGraphNode>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<PackageGraphNode>> _descendantNodesByGroupId =
            new Dictionary<string, IReadOnlyList<PackageGraphNode>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<PackageGraphGroup>> _groupsByParentId =
            new Dictionary<string, IReadOnlyList<PackageGraphGroup>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<PackageGraphEdge>> _edgesByPackageId =
            new Dictionary<string, IReadOnlyList<PackageGraphEdge>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<PackageGraphEdge>> _hardDependencyProvidersByPackageId =
            new Dictionary<string, IReadOnlyList<PackageGraphEdge>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<PackageGraphEdge>> _hardDependencyDependentsByPackageId =
            new Dictionary<string, IReadOnlyList<PackageGraphEdge>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<PackageGraphEdge>> _integrationEdgesByPackageId =
            new Dictionary<string, IReadOnlyList<PackageGraphEdge>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<PackageGraphEdge>> _optionalCompanionEdgesByPackageId =
            new Dictionary<string, IReadOnlyList<PackageGraphEdge>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<PackageGraphSuiteRegion>> _suiteRegionsByPackageId =
            new Dictionary<string, IReadOnlyList<PackageGraphSuiteRegion>>(StringComparer.OrdinalIgnoreCase);

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
            BuildIndexes();
        }

        public IReadOnlyList<PackageGraphNode> Nodes { get; }

        public IReadOnlyList<PackageGraphEdge> Edges { get; }

        public IReadOnlyList<PackageGraphSuiteRegion> SuiteRegions { get; }

        public IReadOnlyList<PackageGraphGroup> Groups { get; }

        public bool TryGetNode(string packageId, out PackageGraphNode node)
        {
            node = null;

            return !string.IsNullOrWhiteSpace(packageId) &&
                   _nodeById.TryGetValue(packageId.Trim(), out node);
        }

        public bool TryGetGroup(string groupId, out PackageGraphGroup group)
        {
            group = null;

            return !string.IsNullOrWhiteSpace(groupId) &&
                   _groupById.TryGetValue(groupId.Trim(), out group);
        }

        public IReadOnlyList<PackageGraphNode> GetDirectPackages(string groupId)
        {
            return GetListOrEmpty(_nodesByGroupId, NormalizeKey(groupId), EmptyNodes);
        }

        public IReadOnlyList<PackageGraphNode> GetDescendantPackages(string groupId)
        {
            return GetListOrEmpty(_descendantNodesByGroupId, NormalizeKey(groupId), EmptyNodes);
        }

        public IReadOnlyList<PackageGraphGroup> GetChildGroups(string parentGroupId)
        {
            return GetListOrEmpty(_groupsByParentId, NormalizeKey(parentGroupId), EmptyGroups);
        }

        public IReadOnlyList<PackageGraphGroup> GetRootGroups()
        {
            return GetChildGroups(string.Empty);
        }

        public IReadOnlyList<PackageGraphEdge> GetEdgesForPackage(string packageId)
        {
            return GetListOrEmpty(_edgesByPackageId, NormalizeKey(packageId), EmptyEdges);
        }

        public IReadOnlyList<PackageGraphEdge> GetHardDependencyProviderEdges(string packageId)
        {
            return GetListOrEmpty(_hardDependencyProvidersByPackageId, NormalizeKey(packageId), EmptyEdges);
        }

        public IReadOnlyList<PackageGraphEdge> GetHardDependencyDependentEdges(string packageId)
        {
            return GetListOrEmpty(_hardDependencyDependentsByPackageId, NormalizeKey(packageId), EmptyEdges);
        }

        public IReadOnlyList<PackageGraphEdge> GetIntegrationEdges(string packageId)
        {
            return GetListOrEmpty(_integrationEdgesByPackageId, NormalizeKey(packageId), EmptyEdges);
        }

        public IReadOnlyList<PackageGraphEdge> GetOptionalCompanionEdges(string packageId)
        {
            return GetListOrEmpty(_optionalCompanionEdgesByPackageId, NormalizeKey(packageId), EmptyEdges);
        }

        public IReadOnlyList<PackageGraphSuiteRegion> GetSuiteRegionsForPackage(string packageId)
        {
            return GetListOrEmpty(_suiteRegionsByPackageId, NormalizeKey(packageId), EmptySuiteRegions);
        }

        private static IReadOnlyList<T> ToReadOnlyList<T>(IEnumerable<T> values)
        {
            return values == null ? Array.Empty<T>() : values.Where(value => value != null).ToArray();
        }

        private void BuildIndexes()
        {
            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.PackageLookup))
            {
                foreach (PackageGraphNode node in Nodes)
                {
                    if (node == null || string.IsNullOrWhiteSpace(node.PackageId))
                    {
                        continue;
                    }

                    _nodeById[node.PackageId] = node;
                    AddToLookup(_nodesByGroupId, NormalizeKey(node.GroupId), node);
                }

                foreach (PackageGraphGroup group in Groups)
                {
                    if (group == null || string.IsNullOrWhiteSpace(group.Id))
                    {
                        continue;
                    }

                    _groupById[group.Id] = group;
                    AddToLookup(_groupsByParentId, NormalizeKey(group.ParentGroupId), group);
                }
            }

            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.DependencyResolution))
            {
                foreach (PackageGraphEdge edge in Edges)
                {
                    if (edge == null)
                    {
                        continue;
                    }

                    AddToLookup(_edgesByPackageId, NormalizeKey(edge.FromPackageId), edge);
                    AddToLookup(_edgesByPackageId, NormalizeKey(edge.ToPackageId), edge);

                    switch (edge.Kind)
                    {
                        case PackageGraphEdgeKind.HardDependency:
                            AddToLookup(_hardDependencyProvidersByPackageId, NormalizeKey(edge.ToPackageId), edge);
                            AddToLookup(_hardDependencyDependentsByPackageId, NormalizeKey(edge.FromPackageId), edge);
                            break;
                        case PackageGraphEdgeKind.IntegrationConnection:
                            AddToLookup(_integrationEdgesByPackageId, NormalizeKey(edge.FromPackageId), edge);
                            AddToLookup(_integrationEdgesByPackageId, NormalizeKey(edge.ToPackageId), edge);
                            break;
                        case PackageGraphEdgeKind.OptionalCompanion:
                            AddToLookup(_optionalCompanionEdgesByPackageId, NormalizeKey(edge.FromPackageId), edge);
                            AddToLookup(_optionalCompanionEdgesByPackageId, NormalizeKey(edge.ToPackageId), edge);
                            break;
                    }
                }

                foreach (PackageGraphSuiteRegion region in SuiteRegions)
                {
                    if (region == null)
                    {
                        continue;
                    }

                    AddToLookup(_suiteRegionsByPackageId, NormalizeKey(region.SuitePackageId), region);

                    foreach (string memberPackageId in region.MemberPackageIds)
                    {
                        AddToLookup(_suiteRegionsByPackageId, NormalizeKey(memberPackageId), region);
                    }
                }

                BuildDescendantPackageIndex();
            }

            SortLookupValues(_groupsByParentId, CompareGroups);
        }

        private void BuildDescendantPackageIndex()
        {
            foreach (PackageGraphGroup group in Groups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.Id))
                {
                    continue;
                }

                HashSet<string> descendantGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectDescendantGroupIds(group.Id, descendantGroupIds);

                List<PackageGraphNode> descendantNodes = new List<PackageGraphNode>();

                foreach (string groupId in descendantGroupIds)
                {
                    if (!_nodesByGroupId.TryGetValue(groupId, out IReadOnlyList<PackageGraphNode> directNodes))
                    {
                        continue;
                    }

                    descendantNodes.AddRange(directNodes);
                }

                _descendantNodesByGroupId[group.Id] = descendantNodes.ToArray();
            }
        }

        private void CollectDescendantGroupIds(string groupId, ISet<string> groupIds)
        {
            string normalizedGroupId = NormalizeKey(groupId);

            if (string.IsNullOrWhiteSpace(normalizedGroupId) || !groupIds.Add(normalizedGroupId))
            {
                return;
            }

            if (!_groupsByParentId.TryGetValue(normalizedGroupId, out IReadOnlyList<PackageGraphGroup> childGroups))
            {
                return;
            }

            foreach (PackageGraphGroup childGroup in childGroups)
            {
                if (childGroup != null)
                {
                    CollectDescendantGroupIds(childGroup.Id, groupIds);
                }
            }
        }

        private static void AddToLookup<T>(
            IDictionary<string, IReadOnlyList<T>> lookup,
            string key,
            T value)
        {
            if (key == null || value == null)
            {
                return;
            }

            if (!lookup.TryGetValue(key, out IReadOnlyList<T> existingValues))
            {
                lookup[key] = new List<T> { value };
                return;
            }

            if (existingValues is List<T> mutableValues)
            {
                mutableValues.Add(value);
                return;
            }

            List<T> values = new List<T>(existingValues) { value };
            lookup[key] = values;
        }

        private static IReadOnlyList<T> GetListOrEmpty<T>(
            IDictionary<string, IReadOnlyList<T>> lookup,
            string key,
            IReadOnlyList<T> empty)
        {
            return key != null &&
                   lookup.TryGetValue(key, out IReadOnlyList<T> values)
                ? values
                : empty;
        }

        private static void SortLookupValues<T>(
            IDictionary<string, IReadOnlyList<T>> lookup,
            Comparison<T> comparison)
        {
            foreach (string key in lookup.Keys.ToArray())
            {
                if (!(lookup[key] is List<T> values))
                {
                    continue;
                }

                values.Sort(comparison);
                lookup[key] = values.ToArray();
            }
        }

        private static int CompareGroups(PackageGraphGroup first, PackageGraphGroup second)
        {
            if (ReferenceEquals(first, second))
            {
                return 0;
            }

            if (first == null)
            {
                return 1;
            }

            if (second == null)
            {
                return -1;
            }

            int sortOrderComparison = first.SortOrder.CompareTo(second.SortOrder);
            if (sortOrderComparison != 0)
            {
                return sortOrderComparison;
            }

            int displayNameComparison = string.Compare(
                first.DisplayName,
                second.DisplayName,
                StringComparison.OrdinalIgnoreCase);
            return displayNameComparison != 0
                ? displayNameComparison
                : string.Compare(first.Id, second.Id, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
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
            foreach (PackageGraphEdge edge in graph.GetEdgesForPackage(packageId))
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
