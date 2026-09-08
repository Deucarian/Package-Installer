using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageGraphNavigationModel
    {
        internal static IReadOnlyList<PackageGraphNavigationRow> CreateEcosystemOverviewGroupNavigationRows(
            PackageGraphModel graph,
            PackageGraphNavigationState navigationState)
        {
            List<PackageGraphNavigationRow> rows = new List<PackageGraphNavigationRow>();
            PackageGraphNode[] graphNodes = graph == null
                ? Array.Empty<PackageGraphNode>()
                : graph.Nodes.Where(node => node != null).ToArray();
            PackageGraphCategoryStatusSummary overviewStatusSummary =
                PackageGraphCategoryStatusSummary.Create(graphNodes);
            rows.Add(new PackageGraphNavigationRow(
                PackageGraphNavigationTargetKind.Overview,
                "overview",
                "Deucarian Overview",
                FormatEcosystemOverviewGroupStatusSummary(overviewStatusSummary),
                overviewStatusSummary,
                "package-installer",
                "Navigate to Deucarian Overview",
                depth: 0,
                hasChildren: graph != null && graph.GetRootGroups().Count > 0,
                isExpanded: !navigationState.IsOverview,
                isInActivePath: navigationState.IsOverview,
                isSelected: navigationState.IsOverview,
                hasAttention: overviewStatusSummary.AttentionCount > 0));

            if (graph == null)
            {
                return rows;
            }

            string activeGroupId = !string.IsNullOrWhiteSpace(navigationState.FocusedPackageId)
                ? GetGraphPackageGroupId(graph, navigationState.FocusedPackageId)
                : navigationState.FocusedGroupId;
            HashSet<string> activeGroupPath = CreateActiveGraphGroupPath(graph, activeGroupId);

            foreach (PackageGraphGroup group in graph.GetRootGroups())
            {
                AddEcosystemGroupNavigationRows(
                    rows,
                    graph,
                    group,
                    0,
                    activeGroupPath,
                    navigationState);
            }

            return rows;
        }

        internal static HashSet<string> CreateActiveGraphGroupPath(
            PackageGraphModel graph,
            string activeGroupId)
        {
            HashSet<string> activeGroupPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = activeGroupId ?? string.Empty;

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   activeGroupPath.Add(currentGroupId) &&
                   graph != null &&
                   graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                currentGroupId = group.ParentGroupId;
            }

            return activeGroupPath;
        }

        internal static void AddEcosystemGroupNavigationRows(
            ICollection<PackageGraphNavigationRow> rows,
            PackageGraphModel graph,
            PackageGraphGroup group,
            int depth,
            ISet<string> activeGroupPath,
            PackageGraphNavigationState navigationState)
        {
            if (rows == null || graph == null || group == null)
            {
                return;
            }

            PackageGraphGroup[] childGroups = graph.GetChildGroups(group.Id)
                .Where(childGroup => childGroup != null)
                .ToArray();
            PackageGraphNode[] directPackages = graph.GetDirectPackages(group.Id)
                .Where(node => node != null && node.IsRegistered && node.PackageDefinition != null)
                .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PackageGraphCategoryStatusSummary groupStatusSummary =
                PackageGraphCategoryStatusSummary.Create(graph.GetDescendantPackages(group.Id));
            bool isInActivePath = activeGroupPath != null && activeGroupPath.Contains(group.Id);
            bool hasChildren = childGroups.Length > 0 || directPackages.Length > 0;
            bool isSelected = navigationState.TargetKind == PackageGraphNavigationTargetKind.Group &&
                              string.Equals(
                                  navigationState.FocusedGroupId,
                                  group.Id,
                                  StringComparison.OrdinalIgnoreCase);
            rows.Add(new PackageGraphNavigationRow(
                PackageGraphNavigationTargetKind.Group,
                group.Id,
                group.DisplayName,
                FormatEcosystemOverviewGroupStatusSummary(groupStatusSummary),
                groupStatusSummary,
                group.IconKey,
                group.Description,
                depth,
                hasChildren,
                isInActivePath,
                isInActivePath,
                isSelected,
                groupStatusSummary.AttentionCount > 0));

            if (!isInActivePath || !hasChildren)
            {
                return;
            }

            foreach (PackageGraphGroup childGroup in childGroups)
            {
                AddEcosystemGroupNavigationRows(
                    rows,
                    graph,
                    childGroup,
                    depth + 1,
                    activeGroupPath,
                    navigationState);
            }

            foreach (PackageGraphNode packageNode in directPackages)
            {
                PackageGraphCategoryStatusSummary packageStatusSummary =
                    PackageGraphCategoryStatusSummary.Create(new[] { packageNode });
                bool packageSelected = navigationState.TargetKind == PackageGraphNavigationTargetKind.Package &&
                                       string.Equals(
                                           navigationState.FocusedPackageId,
                                           packageNode.PackageId,
                                           StringComparison.OrdinalIgnoreCase);
                rows.Add(new PackageGraphNavigationRow(
                    PackageGraphNavigationTargetKind.Package,
                    packageNode.PackageId,
                    packageNode.DisplayName,
                    FormatPackageGraphNavigationStatus(packageNode),
                    packageStatusSummary,
                    packageNode.IconKey,
                    packageNode.Description,
                    depth + 1,
                    hasChildren: false,
                    isExpanded: false,
                    isInActivePath: packageSelected,
                    isSelected: packageSelected,
                    hasAttention: packageStatusSummary.AttentionCount > 0));
            }
        }

        internal static string FormatPackageGraphNavigationStatus(PackageGraphNode node)
        {
            if (node == null)
            {
                return "Unknown";
            }

            switch (node.Status)
            {
                case PackageGraphNodeStatus.Missing:
                    return "Missing dependency";
                case PackageGraphNodeStatus.NotInstalled:
                    return "Not installed";
                case PackageGraphNodeStatus.UpdateAvailable:
                    return "Update available";
                case PackageGraphNodeStatus.Checking:
                    return "Checking";
                case PackageGraphNodeStatus.Warning:
                    return string.IsNullOrWhiteSpace(node.UpdateStatusLabel)
                        ? "Attention"
                        : node.UpdateStatusLabel;
                default:
                    return "Installed";
            }
        }

        internal static string GetGraphPackageGroupId(PackageGraphModel graph, string packageId)
        {
            return graph != null &&
                   !string.IsNullOrWhiteSpace(packageId) &&
                   graph.TryGetNode(packageId, out PackageGraphNode node)
                ? node.GroupId
                : string.Empty;
        }

        internal static string ResolveTopLevelGroupId(PackageGraphModel graph, string groupId)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return string.Empty;
            }

            string currentGroupId = groupId;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                if (string.IsNullOrWhiteSpace(group.ParentGroupId))
                {
                    return group.Id;
                }

                currentGroupId = group.ParentGroupId;
            }

            return string.Empty;
        }

        internal static string FormatEcosystemOverviewGroupStatusSummary(
            PackageGraphCategoryStatusSummary statusSummary)
        {
            List<string> parts = new List<string>();

            if (statusSummary.AttentionCount > 0)
            {
                parts.Add(statusSummary.AttentionCount + " attention");
            }

            if (statusSummary.InstalledCount > 0)
            {
                parts.Add(statusSummary.InstalledCount + " installed");
            }

            if (statusSummary.NotInstalledCount > 0)
            {
                parts.Add(statusSummary.NotInstalledCount + " not installed");
            }

            if (statusSummary.UnknownCount > 0)
            {
                parts.Add(statusSummary.UnknownCount + " unknown");
            }

            return parts.Count == 0 ? "0 packages" : string.Join("   ", parts.ToArray());
        }

        internal static string FormatCompactEcosystemOverviewGroupStatusSummary(
            PackageGraphCategoryStatusSummary statusSummary)
        {
            List<string> parts = new List<string>();

            if (statusSummary.AttentionCount > 0)
            {
                parts.Add(statusSummary.AttentionCount + " attention");
            }

            if (statusSummary.InstalledCount > 0)
            {
                parts.Add(statusSummary.InstalledCount + " installed");
            }

            if (statusSummary.NotInstalledCount > 0)
            {
                parts.Add(statusSummary.NotInstalledCount + " not installed");
            }

            if (statusSummary.UnknownCount > 0)
            {
                parts.Add(statusSummary.UnknownCount + " unknown");
            }

            return parts.Count == 0 ? "0" : string.Join("   ", parts.ToArray());
        }
    }
}
