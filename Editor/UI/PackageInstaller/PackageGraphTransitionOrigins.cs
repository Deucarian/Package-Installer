using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphTransitionOrigins
    {
        private readonly PackageGraphModel graph;
        private readonly PackageGraphLayoutResult layout;
        private readonly Vector2 activeCenter;
        internal PackageGraphTransitionOrigins(PackageGraphModel graph, PackageGraphLayoutResult layout, Vector2 activeCenter)
        { this.graph = graph; this.layout = layout; this.activeCenter = activeCenter; }

        internal Rect CreateEnteringNodeStartRect(
            string packageId,
            Rect targetRect,
            IReadOnlyDictionary<string, Rect> previousGroupRects)
        {
            if (graph != null &&
                graph.TryGetNode(packageId, out PackageGraphNode node))
            {
                PackageGraphGroupLayoutNode targetGroup = PackageGraphTransitionGeometry.FindGroupLayoutNode(layout, node.GroupId);

                if (targetGroup != null &&
                    targetGroup.OrbitRadius > 0.01f &&
                    PackageGraphTransitionGeometry.TryGetTargetDirection(targetGroup.HubCenter, targetRect.center, out Vector2 direction))
                {
                    float initialRadius = Mathf.Max(32f, targetGroup.OrbitRadius * 0.24f);
                    return PackageGraphTransitionGeometry.CenterRectOn(targetRect, targetGroup.HubCenter + direction * initialRadius);
                }

                if (TryGetPreviousGroupRect(node.GroupId, previousGroupRects, out Rect groupRect))
                {
                    return PackageGraphTransitionGeometry.CenterRectOn(targetRect, groupRect.center);
                }
            }

            return PackageGraphTransitionGeometry.CenterRectOn(targetRect, activeCenter);
        }

        internal Rect CreateEnteringGroupStartRect(
            PackageGraphGroupLayoutNode target,
            IReadOnlyDictionary<string, Rect> previousGroupRects)
        {
            if (target != null &&
                target.Group != null &&
                TryGetPreviousGroupRect(target.Group.ParentGroupId, previousGroupRects, out Rect parentRect))
            {
                return PackageGraphTransitionGeometry.CenterRectOn(target.Rect, parentRect.center);
            }

            return PackageGraphTransitionGeometry.CenterRectOn(target != null ? target.Rect : default(Rect), activeCenter);
        }

        internal bool TryGetPreviousGroupRect(
            string groupId,
            IReadOnlyDictionary<string, Rect> previousGroupRects,
            out Rect groupRect)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = groupId;

            while (!string.IsNullOrWhiteSpace(currentGroupId) && visited.Add(currentGroupId))
            {
                if (previousGroupRects != null &&
                    previousGroupRects.TryGetValue(currentGroupId, out groupRect))
                {
                    return true;
                }

                if (graph == null ||
                    !graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
                {
                    break;
                }

                currentGroupId = group.ParentGroupId;
            }

            groupRect = default(Rect);
            return false;
        }
    }
}
