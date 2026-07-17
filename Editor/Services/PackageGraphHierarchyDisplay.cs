using System;
using System.Collections.Generic;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageGraphHierarchyDisplay
    {
        public static string GetPackageHierarchyPath(
            PackageGraphModel graph,
            PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return "-";
            }

            string groupId = string.Empty;

            if (graph != null &&
                graph.TryGetNode(packageDefinition.PackageId, out PackageGraphNode node))
            {
                groupId = node.GroupId;
            }

            if (string.IsNullOrWhiteSpace(groupId))
            {
                groupId = PackageGraphHierarchyBuilder.ResolvePackageGroupId(
                    packageDefinition,
                    graph != null ? graph.Groups : null);
            }

            string hierarchyPath = GetGroupPath(graph, groupId);

            if (!string.IsNullOrWhiteSpace(hierarchyPath))
            {
                return hierarchyPath;
            }

            return string.IsNullOrWhiteSpace(packageDefinition.NavigationGroup)
                ? "-"
                : packageDefinition.NavigationGroup;
        }

        public static string GetPackageRole(PackageDefinition packageDefinition)
        {
            return GetPackageKind(packageDefinition);
        }

        public static string GetPackageKind(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return "-";
            }

            return packageDefinition.Kind.ToString();
        }

        private static string GetGroupPath(PackageGraphModel graph, string groupId)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return string.Empty;
            }

            List<string> path = new List<string>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = groupId;

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                path.Add(group.DisplayName);
                currentGroupId = group.ParentGroupId;
            }

            path.Reverse();
            return path.Count == 0 ? string.Empty : string.Join(" / ", path.ToArray());
        }
    }
}
