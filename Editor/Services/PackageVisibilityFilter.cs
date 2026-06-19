using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageVisibilityFilter
    {
        public static bool IsVisible(
            PackageDefinition packageDefinition,
            PackageVisibilityFilterState filterState,
            Func<PackageDefinition, bool> isInstalled)
        {
            if (packageDefinition == null)
            {
                return false;
            }

            PackageVisibilityFilterState activeState = filterState ?? new PackageVisibilityFilterState();
            bool installed = isInstalled != null && isInstalled(packageDefinition);

            return IsVisibleByInstalledState(installed, activeState) &&
                   MatchesSearch(packageDefinition, activeState.SearchText);
        }

        public static bool IsVisible(
            PackageGraphNode node,
            PackageVisibilityFilterState filterState)
        {
            if (node == null)
            {
                return false;
            }

            PackageVisibilityFilterState activeState = filterState ?? new PackageVisibilityFilterState();
            return IsVisibleByInstalledState(node.IsInstalled, activeState) &&
                   MatchesSearch(node, activeState.SearchText);
        }

        public static PackageVisibilityFilterCounts CalculateCounts(
            IEnumerable<PackageDefinition> packages,
            PackageVisibilityFilterState filterState,
            Func<PackageDefinition, bool> isInstalled)
        {
            PackageDefinition[] packageArray = packages != null
                ? packages.Where(package => package != null).ToArray()
                : Array.Empty<PackageDefinition>();
            Func<PackageDefinition, bool> installedPredicate = isInstalled ?? (_ => false);
            int installedCount = packageArray.Count(installedPredicate);
            int visibleCount = packageArray.Count(package =>
                IsVisible(package, filterState, installedPredicate));

            return new PackageVisibilityFilterCounts(
                packageArray.Length,
                installedCount,
                packageArray.Length - installedCount,
                visibleCount);
        }

        public static PackageVisibilityFilterCounts CalculateCounts(
            PackageGraphModel graph,
            PackageVisibilityFilterState filterState)
        {
            PackageGraphNode[] nodes = graph != null
                ? graph.Nodes.Where(node => node != null).ToArray()
                : Array.Empty<PackageGraphNode>();
            int installedCount = nodes.Count(node => node.IsInstalled);
            int visibleCount = nodes.Count(node => IsVisible(node, filterState));

            return new PackageVisibilityFilterCounts(
                nodes.Length,
                installedCount,
                nodes.Length - installedCount,
                visibleCount);
        }

        public static HashSet<string> CreateVisiblePackageIdSet(
            PackageGraphModel graph,
            PackageVisibilityFilterState filterState)
        {
            return new HashSet<string>(
                graph == null
                    ? Array.Empty<string>()
                    : graph.Nodes
                        .Where(node => IsVisible(node, filterState))
                        .Select(node => node.PackageId),
                StringComparer.OrdinalIgnoreCase);
        }

        public static HashSet<string> CreateStatusVisiblePackageIdSet(
            PackageGraphModel graph,
            PackageVisibilityFilterState filterState)
        {
            PackageVisibilityFilterState activeState = filterState ?? new PackageVisibilityFilterState();
            return new HashSet<string>(
                graph == null
                    ? Array.Empty<string>()
                    : graph.Nodes
                        .Where(node => node != null &&
                                       IsVisibleByInstalledState(node.IsInstalled, activeState))
                        .Select(node => node.PackageId),
                StringComparer.OrdinalIgnoreCase);
        }

        public static PackageGraphModel CreateVisibleGraph(
            PackageGraphModel graph,
            ISet<string> visiblePackageIds)
        {
            if (graph == null)
            {
                return new PackageGraphModel(
                    Array.Empty<PackageGraphNode>(),
                    Array.Empty<PackageGraphEdge>(),
                    Array.Empty<PackageGraphSuiteRegion>(),
                    Array.Empty<PackageGraphGroup>());
            }

            ISet<string> visibleIds = visiblePackageIds ??
                                      new HashSet<string>(
                                          graph.Nodes.Select(node => node.PackageId),
                                          StringComparer.OrdinalIgnoreCase);
            PackageGraphNode[] visibleNodes = graph.Nodes
                .Where(node => node != null && visibleIds.Contains(node.PackageId))
                .ToArray();
            PackageGraphEdge[] visibleEdges = graph.Edges
                .Where(edge => edge != null &&
                               visibleIds.Contains(edge.FromPackageId) &&
                               visibleIds.Contains(edge.ToPackageId))
                .ToArray();
            PackageGraphSuiteRegion[] visibleSuiteRegions = graph.SuiteRegions
                .Where(region => region != null && visibleIds.Contains(region.SuitePackageId))
                .Select(region => new PackageGraphSuiteRegion(
                    region.SuitePackageId,
                    region.MemberPackageIds.Where(visibleIds.Contains)))
                .Where(region => region.MemberPackageIds.Count > 0)
                .ToArray();

            return new PackageGraphModel(visibleNodes, visibleEdges, visibleSuiteRegions, graph.Groups);
        }

        public static int CountHiddenRelatedPackages(
            PackageGraphModel fullGraph,
            string focusPackageId,
            ISet<string> visiblePackageIds)
        {
            if (fullGraph == null ||
                string.IsNullOrWhiteSpace(focusPackageId) ||
                visiblePackageIds == null ||
                !visiblePackageIds.Contains(focusPackageId))
            {
                return 0;
            }

            PackageGraphFocus focus = PackageGraphFocus.Create(fullGraph, focusPackageId);

            return focus.RelatedPackageIds.Count(packageId =>
                !string.Equals(packageId, focusPackageId, StringComparison.OrdinalIgnoreCase) &&
                !visiblePackageIds.Contains(packageId));
        }

        public static bool MatchesSearch(PackageDefinition packageDefinition, string searchText)
        {
            if (packageDefinition == null)
            {
                return false;
            }

            if (!TryCreateSearchTokens(searchText, out string[] tokens))
            {
                return true;
            }

            return MatchesSearchTokens(tokens, CreateSearchText(packageDefinition));
        }

        public static bool MatchesSearch(PackageGraphNode node, string searchText)
        {
            if (node == null)
            {
                return false;
            }

            if (!TryCreateSearchTokens(searchText, out string[] tokens))
            {
                return true;
            }

            string searchableText = node.PackageDefinition != null
                ? CreateSearchText(node.PackageDefinition)
                : string.Join(
                    "\n",
                    new[]
                    {
                        node.DisplayName,
                        node.PackageId,
                        node.Category
                    });

            return MatchesSearchTokens(tokens, searchableText);
        }

        private static bool IsVisibleByInstalledState(
            bool installed,
            PackageVisibilityFilterState filterState)
        {
            return installed ? filterState.ShowInstalled : filterState.ShowNotInstalled;
        }

        private static bool TryCreateSearchTokens(string searchText, out string[] tokens)
        {
            tokens = string.IsNullOrWhiteSpace(searchText)
                ? Array.Empty<string>()
                : searchText
                    .Trim()
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            return tokens.Length > 0;
        }

        private static bool MatchesSearchTokens(
            IEnumerable<string> tokens,
            string searchableText)
        {
            string haystack = searchableText ?? string.Empty;
            return tokens.All(token =>
                haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string CreateSearchText(PackageDefinition packageDefinition)
        {
            return string.Join(
                "\n",
                new[]
                {
                    packageDefinition.DisplayName,
                    packageDefinition.PackageId,
                    packageDefinition.Category
                });
        }
    }
}
