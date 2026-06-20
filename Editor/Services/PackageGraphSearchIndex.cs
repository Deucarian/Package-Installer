using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageGraphSearchResultType
    {
        Category,
        Package
    }

    internal sealed class PackageGraphSearchResult
    {
        public PackageGraphSearchResult(
            PackageGraphSearchResultType type,
            string id,
            string displayName,
            int rank)
        {
            Type = type;
            Id = id ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
            Rank = rank;
        }

        public PackageGraphSearchResultType Type { get; }

        public string Id { get; }

        public string DisplayName { get; }

        public int Rank { get; }
    }

    internal sealed class PackageGraphSearchState
    {
        private static readonly PackageGraphSearchResult[] EmptyResults =
            Array.Empty<PackageGraphSearchResult>();

        private readonly HashSet<string> _directCategoryMatchIds;
        private readonly HashSet<string> _directPackageMatchIds;
        private readonly HashSet<string> _contextCategoryIds;
        private readonly HashSet<string> _contextPackageIds;

        public PackageGraphSearchState(
            string query,
            IEnumerable<PackageGraphSearchResult> results,
            IEnumerable<string> directCategoryMatchIds,
            IEnumerable<string> directPackageMatchIds,
            IEnumerable<string> contextCategoryIds,
            IEnumerable<string> contextPackageIds)
        {
            Query = query ?? string.Empty;
            Results = results == null ? EmptyResults : results.ToArray();
            _directCategoryMatchIds = ToSet(directCategoryMatchIds);
            _directPackageMatchIds = ToSet(directPackageMatchIds);
            _contextCategoryIds = ToSet(contextCategoryIds);
            _contextPackageIds = ToSet(contextPackageIds);
        }

        public static PackageGraphSearchState Empty { get; } =
            new PackageGraphSearchState(
                string.Empty,
                EmptyResults,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());

        public string Query { get; }

        public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

        public IReadOnlyList<PackageGraphSearchResult> Results { get; }

        public int DirectCategoryMatchCount => _directCategoryMatchIds.Count;

        public int DirectPackageMatchCount => _directPackageMatchIds.Count;

        public int DirectMatchCount => _directCategoryMatchIds.Count + _directPackageMatchIds.Count;

        public int ContextPackageCount => _contextPackageIds.Count;

        public PackageGraphSearchResult BestResult => Results.Count > 0 ? Results[0] : null;

        public IReadOnlyCollection<string> DirectCategoryMatchIds => _directCategoryMatchIds;

        public IReadOnlyCollection<string> DirectPackageMatchIds => _directPackageMatchIds;

        public IReadOnlyCollection<string> ContextCategoryIds => _contextCategoryIds;

        public IReadOnlyCollection<string> ContextPackageIds => _contextPackageIds;

        public bool IsDirectCategoryMatch(string groupId)
        {
            return !string.IsNullOrWhiteSpace(groupId) && _directCategoryMatchIds.Contains(groupId);
        }

        public bool IsDirectPackageMatch(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && _directPackageMatchIds.Contains(packageId);
        }

        public bool IsCategoryContext(string groupId)
        {
            return !string.IsNullOrWhiteSpace(groupId) && _contextCategoryIds.Contains(groupId);
        }

        public bool IsPackageContext(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && _contextPackageIds.Contains(packageId);
        }

        private static HashSet<string> ToSet(IEnumerable<string> values)
        {
            return new HashSet<string>(
                values == null
                    ? Array.Empty<string>()
                    : values.Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static class PackageGraphSearchIndex
    {
        public static PackageGraphSearchState Create(
            PackageGraphModel graph,
            PackageVisibilityFilterState filterState,
            ISet<string> statusVisiblePackageIds)
        {
            string query = filterState != null ? filterState.SearchText : string.Empty;

            if (graph == null || !TryCreateSearchTokens(query, out string[] tokens))
            {
                return PackageGraphSearchState.Empty;
            }

            HashSet<string> statusVisibleIds = statusVisiblePackageIds == null
                ? new HashSet<string>(
                    graph.Nodes.Select(node => node.PackageId),
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(statusVisiblePackageIds, StringComparer.OrdinalIgnoreCase);
            List<PackageGraphSearchResult> results = new List<PackageGraphSearchResult>();
            HashSet<string> directCategoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> directPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> contextCategoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> contextPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphGroup group in graph.Groups)
            {
                int rank = GetCategoryMatchRank(graph, group, tokens);

                if (rank < 0)
                {
                    continue;
                }

                directCategoryIds.Add(group.Id);
                AddAncestorGroups(graph, group.Id, contextCategoryIds);
                AddDescendantGroups(graph, group.Id, contextCategoryIds);

                foreach (PackageGraphNode descendantPackage in GetDescendantPackages(graph, group.Id))
                {
                    if (!statusVisibleIds.Contains(descendantPackage.PackageId))
                    {
                        continue;
                    }

                    contextPackageIds.Add(descendantPackage.PackageId);
                }

                results.Add(new PackageGraphSearchResult(
                    PackageGraphSearchResultType.Category,
                    group.Id,
                    group.DisplayName,
                    rank));
            }

            foreach (PackageGraphNode node in graph.Nodes)
            {
                if (!statusVisibleIds.Contains(node.PackageId))
                {
                    continue;
                }

                int rank = GetPackageMatchRank(node, tokens);

                if (rank < 0)
                {
                    continue;
                }

                directPackageIds.Add(node.PackageId);
                contextPackageIds.Add(node.PackageId);
                AddAncestorGroups(graph, node.GroupId, contextCategoryIds);
                results.Add(new PackageGraphSearchResult(
                    PackageGraphSearchResultType.Package,
                    node.PackageId,
                    node.DisplayName,
                    rank));
            }

            PackageGraphSearchResult[] orderedResults = results
                .OrderBy(result => result.Rank)
                .ThenBy(result => result.Type == PackageGraphSearchResultType.Category ? 0 : 1)
                .ThenBy(result => result.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new PackageGraphSearchState(
                query,
                orderedResults,
                directCategoryIds,
                directPackageIds,
                contextCategoryIds,
                contextPackageIds);
        }

        internal static int GetPackageMatchRankForTests(PackageGraphNode node, string query)
        {
            return TryCreateSearchTokens(query, out string[] tokens)
                ? GetPackageMatchRank(node, tokens)
                : -1;
        }

        internal static int GetCategoryMatchRankForTests(PackageGraphModel graph, PackageGraphGroup group, string query)
        {
            return TryCreateSearchTokens(query, out string[] tokens)
                ? GetCategoryMatchRank(graph, group, tokens)
                : -1;
        }

        private static int GetPackageMatchRank(PackageGraphNode node, IReadOnlyList<string> tokens)
        {
            if (node == null)
            {
                return -1;
            }

            string displayName = node.DisplayName ?? string.Empty;
            string packageId = node.PackageId ?? string.Empty;
            string explicitSearchText = CreatePackageExplicitSearchText(node);

            if (MatchesExact(displayName, tokens))
            {
                return 0;
            }

            if (MatchesTokenPrefix(displayName, tokens))
            {
                return 1;
            }

            if (MatchesSubstring(displayName, tokens))
            {
                return 2;
            }

            if (MatchesSubstring(packageId, tokens))
            {
                return 3;
            }

            return MatchesSubstring(explicitSearchText, tokens) ? 4 : -1;
        }

        private static int GetCategoryMatchRank(
            PackageGraphModel graph,
            PackageGraphGroup group,
            IReadOnlyList<string> tokens)
        {
            if (group == null)
            {
                return -1;
            }

            string displayName = group.DisplayName ?? string.Empty;
            string path = GetCategoryPath(graph, group.Id);

            if (MatchesExact(displayName, tokens))
            {
                return 0;
            }

            if (MatchesTokenPrefix(displayName, tokens))
            {
                return 1;
            }

            if (MatchesSubstring(displayName, tokens) || MatchesSubstring(path, tokens))
            {
                return 2;
            }

            return -1;
        }

        private static bool MatchesExact(string value, IReadOnlyList<string> tokens)
        {
            return string.Equals(
                NormalizeIdentity(value),
                NormalizeIdentity(string.Join(" ", tokens.ToArray())),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesTokenPrefix(string value, IReadOnlyList<string> tokens)
        {
            string[] valueTokens = CreateIdentityTokens(value);
            return tokens.All(token =>
                valueTokens.Any(valueToken =>
                    valueToken.StartsWith(token, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool MatchesSubstring(string value, IReadOnlyList<string> tokens)
        {
            string normalized = NormalizeIdentity(value);
            return tokens.All(token =>
                normalized.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string[] CreateIdentityTokens(string value)
        {
            return NormalizeIdentity(value)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string NormalizeIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] chars = value
                .Trim()
                .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
                .ToArray();
            return string.Join(
                " ",
                new string(chars)
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string CreatePackageExplicitSearchText(PackageGraphNode node)
        {
            if (node == null || node.PackageDefinition == null)
            {
                return string.Empty;
            }

            return string.Join(
                " ",
                node.PackageDefinition.SearchAliases
                    .Concat(node.PackageDefinition.SearchTags)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray());
        }

        private static bool TryCreateSearchTokens(string searchText, out string[] tokens)
        {
            tokens = string.IsNullOrWhiteSpace(searchText)
                ? Array.Empty<string>()
                : CreateIdentityTokens(searchText);

            return tokens.Length > 0;
        }

        private static string GetCategoryPath(PackageGraphModel graph, string groupId)
        {
            List<string> path = new List<string>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = groupId;

            while (graph != null &&
                   !string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                path.Add(group.DisplayName);
                currentGroupId = group.ParentGroupId;
            }

            path.Reverse();
            return string.Join(" ", path.ToArray());
        }

        private static void AddAncestorGroups(
            PackageGraphModel graph,
            string groupId,
            ISet<string> groupIds)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = groupId;

            while (graph != null &&
                   !string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                groupIds.Add(group.Id);
                currentGroupId = group.ParentGroupId;
            }
        }

        private static void AddDescendantGroups(
            PackageGraphModel graph,
            string groupId,
            ISet<string> groupIds)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId) || !groupIds.Add(groupId))
            {
                return;
            }

            foreach (PackageGraphGroup childGroup in graph.Groups.Where(group =>
                         group != null &&
                         string.Equals(group.ParentGroupId, groupId, StringComparison.OrdinalIgnoreCase)))
            {
                AddDescendantGroups(graph, childGroup.Id, groupIds);
            }
        }

        private static IEnumerable<PackageGraphNode> GetDescendantPackages(
            PackageGraphModel graph,
            string groupId)
        {
            HashSet<string> groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDescendantGroups(graph, groupId, groupIds);
            return graph.Nodes.Where(node => node != null && groupIds.Contains(node.GroupId));
        }

        public static PackageGraphModel CreateFilteredGraph(
            PackageGraphModel graph,
            PackageGraphSearchState searchState,
            ISet<string> statusVisiblePackageIds,
            IEnumerable<string> requiredCategoryIds = null)
        {
            if (graph == null || searchState == null || !searchState.HasQuery)
            {
                return graph ?? new PackageGraphModel(
                    Array.Empty<PackageGraphNode>(),
                    Array.Empty<PackageGraphEdge>(),
                    Array.Empty<PackageGraphSuiteRegion>(),
                    Array.Empty<PackageGraphGroup>());
            }

            HashSet<string> statusVisibleIds = statusVisiblePackageIds == null
                ? new HashSet<string>(
                    graph.Nodes.Select(node => node.PackageId),
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(statusVisiblePackageIds, StringComparer.OrdinalIgnoreCase);
            HashSet<string> visiblePackageIds = new HashSet<string>(
                searchState.ContextPackageIds.Where(statusVisibleIds.Contains),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> visibleGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphNode node in graph.Nodes)
            {
                if (node != null && visiblePackageIds.Contains(node.PackageId))
                {
                    AddAncestorGroups(graph, node.GroupId, visibleGroupIds);
                }
            }

            if (requiredCategoryIds != null)
            {
                foreach (string groupId in requiredCategoryIds)
                {
                    AddAncestorGroups(graph, groupId, visibleGroupIds);
                }
            }

            PackageGraphNode[] visibleNodes = graph.Nodes
                .Where(node => node != null && visiblePackageIds.Contains(node.PackageId))
                .ToArray();
            PackageGraphEdge[] visibleEdges = graph.Edges
                .Where(edge => edge != null &&
                               visiblePackageIds.Contains(edge.FromPackageId) &&
                               visiblePackageIds.Contains(edge.ToPackageId))
                .ToArray();
            PackageGraphSuiteRegion[] visibleSuiteRegions = graph.SuiteRegions
                .Where(region => region != null && visiblePackageIds.Contains(region.SuitePackageId))
                .Select(region => new PackageGraphSuiteRegion(
                    region.SuitePackageId,
                    region.MemberPackageIds.Where(visiblePackageIds.Contains)))
                .Where(region => region.MemberPackageIds.Count > 0)
                .ToArray();
            PackageGraphGroup[] visibleGroups = graph.Groups
                .Where(group => group != null && visibleGroupIds.Contains(group.Id))
                .ToArray();

            return new PackageGraphModel(
                visibleNodes,
                visibleEdges,
                visibleSuiteRegions,
                visibleGroups);
        }
    }
}
