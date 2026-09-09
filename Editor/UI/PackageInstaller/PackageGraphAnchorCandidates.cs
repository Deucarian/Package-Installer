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
    internal static class PackageGraphAnchorCandidates
    {
        internal static void AddPackageAnchor(
            ICollection<PackageGraphTransitionAnchor> anchors,
            string packageId)
        {
            if (!string.IsNullOrWhiteSpace(packageId))
            {
                AddAnchor(
                    anchors,
                    new PackageGraphTransitionAnchor(PackageGraphTransitionAnchorKind.Package, packageId.Trim()));
            }
        }

        internal static void AddGroupAnchor(
            ICollection<PackageGraphTransitionAnchor> anchors,
            string groupId)
        {
            if (!string.IsNullOrWhiteSpace(groupId))
            {
                AddAnchor(
                    anchors,
                    new PackageGraphTransitionAnchor(PackageGraphTransitionAnchorKind.Group, groupId.Trim()));
            }
        }

        internal static void AddAnchor(
            ICollection<PackageGraphTransitionAnchor> anchors,
            PackageGraphTransitionAnchor anchor)
        {
            if (!anchors.Contains(anchor))
            {
                anchors.Add(anchor);
            }
        }

        internal static void AddPackageGroupAnchors(
            PackageGraphModel graph,
            ICollection<PackageGraphTransitionAnchor> anchors,
            string packageId)
        {
            if (graph == null ||
                string.IsNullOrWhiteSpace(packageId) ||
                !graph.TryGetNode(packageId, out PackageGraphNode node))
            {
                return;
            }

            AddGroupAnchor(anchors, node.GroupId);
            AddAncestorGroupAnchors(graph, anchors, node.GroupId);
        }

        internal static void AddAncestorGroupAnchors(
            PackageGraphModel graph,
            ICollection<PackageGraphTransitionAnchor> anchors,
            string groupId)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = groupId;

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                AddGroupAnchor(anchors, group.Id);
                currentGroupId = group.ParentGroupId;
            }
        }

        internal static bool TryGetLayoutAnchorCenter(
            PackageGraphLayoutResult layout,
            PackageGraphTransitionAnchor anchor,
            out Vector2 center)
        {
            center = default(Vector2);

            if (layout == null)
            {
                return false;
            }

            switch (anchor.Kind)
            {
                case PackageGraphTransitionAnchorKind.Package:
                    if (layout.NodeRects.TryGetValue(anchor.Id, out Rect nodeRect))
                    {
                        center = nodeRect.center;
                        return true;
                    }

                    break;
                case PackageGraphTransitionAnchorKind.Group:
                    PackageGraphGroupLayoutNode groupNode = layout.GroupNodes.FirstOrDefault(candidate =>
                        candidate != null &&
                        string.Equals(candidate.GroupId, anchor.Id, StringComparison.OrdinalIgnoreCase));

                    if (groupNode != null)
                    {
                        center = groupNode.HubCenter;
                        return true;
                    }

                    break;
                default:
                    center = layout.HubRect.center;
                    return true;
            }

            return false;
        }

        internal static bool AreBoundsClose(Rect first, Rect second)
        {
            return Mathf.Abs(first.x - second.x) < 0.5f &&
                   Mathf.Abs(first.y - second.y) < 0.5f &&
                   Mathf.Abs(first.width - second.width) < 0.5f &&
                   Mathf.Abs(first.height - second.height) < 0.5f;
        }
    }
}
