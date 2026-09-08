using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_2022_1_OR_NEWER
using PackageGraphPainter = UnityEngine.UIElements.Painter2D;
#else
using PackageGraphPainter = Deucarian.PackageInstaller.Editor.PackageGraphMeshPainter;
#endif

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageGraphNavigationPath
    {
        internal static PackageGraphGroup[] CreateGroupPath(
            PackageGraphModel graph,
            string focusedGroupId,
            string focusedPackageId)
        {
            if (graph == null || graph.Groups.Count == 0)
            {
                return Array.Empty<PackageGraphGroup>();
            }

            string groupId = focusedGroupId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(groupId) &&
                !string.IsNullOrWhiteSpace(focusedPackageId) &&
                graph.TryGetNode(focusedPackageId, out PackageGraphNode focusedPackage))
            {
                groupId = focusedPackage.GroupId;
            }

            List<PackageGraphGroup> path = new List<PackageGraphGroup>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(groupId) &&
                   visited.Add(groupId) &&
                   graph.TryGetGroup(groupId, out PackageGraphGroup group))
            {
                path.Add(group);
                groupId = group.ParentGroupId;
            }

            path.Reverse();
            return path.ToArray();
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
    }
}
