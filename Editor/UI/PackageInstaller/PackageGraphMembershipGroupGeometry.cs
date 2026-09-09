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
    internal static class PackageGraphMembershipGroupGeometry
    {
        internal static Rect GetGroupHubRect(PackageGraphGroupLayoutNode groupNode, Rect groupRect)
        {
            if (groupNode == null)
            {
                return groupRect;
            }

            Vector2 hubSize = groupNode.HubRect.size;
            Vector2 hubOffset = groupNode.HubRect.position - groupNode.Rect.position;
            return new Rect(
                groupRect.x + hubOffset.x,
                groupRect.y + hubOffset.y,
                hubSize.x,
                hubSize.y);
        }

        internal static Rect CenterRectOn(Rect rect, Vector2 center)
        {
            return new Rect(
                center.x - rect.width * 0.5f,
                center.y - rect.height * 0.5f,
                rect.width,
                rect.height);
        }

        internal static bool IsGroupInHoverContext(
            string groupId,
            string activeGroupId,
            IReadOnlyDictionary<string, PackageGraphGroupLayoutNode> groupNodeById)
        {
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(activeGroupId))
            {
                return false;
            }

            if (string.Equals(groupId, activeGroupId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string currentGroupId = groupId;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   groupNodeById != null &&
                   groupNodeById.TryGetValue(currentGroupId, out PackageGraphGroupLayoutNode groupNode) &&
                   groupNode.Group != null)
            {
                if (string.Equals(groupNode.Group.ParentGroupId, activeGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentGroupId = groupNode.Group.ParentGroupId;
            }

            currentGroupId = activeGroupId;
            visited.Clear();

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   groupNodeById != null &&
                   groupNodeById.TryGetValue(currentGroupId, out PackageGraphGroupLayoutNode groupNode) &&
                   groupNode.Group != null)
            {
                if (string.Equals(groupNode.Group.ParentGroupId, groupId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentGroupId = groupNode.Group.ParentGroupId;
            }

            return false;
        }
    }
}
