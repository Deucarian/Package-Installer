using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageGraphTransitionGeometry
    {
        internal static Rect CenterRectOn(Rect rect, Vector2 center)
        {
            return new Rect(
                center.x - rect.width * 0.5f,
                center.y - rect.height * 0.5f,
                rect.width,
                rect.height);
        }

        internal static Rect GetGroupHubRect(PackageGraphGroupLayoutNode groupNode, Rect groupRect)
        {
            if (groupNode == null)
            {
                return groupRect;
            }

            Vector2 offset = groupNode.HubRect.position - groupNode.Rect.position;
            return new Rect(
                groupRect.x + offset.x,
                groupRect.y + offset.y,
                groupNode.HubRect.width,
                groupNode.HubRect.height);
        }

        internal static Rect PositionGroupRectFromHubCenter(
            PackageGraphGroupLayoutNode groupNode,
            Rect currentRect,
            Vector2 hubCenter)
        {
            if (groupNode == null)
            {
                return CenterRectOn(currentRect, hubCenter);
            }

            Vector2 offset = groupNode.HubRect.position - groupNode.Rect.position;
            return new Rect(
                hubCenter.x - offset.x - groupNode.HubRect.width * 0.5f,
                hubCenter.y - offset.y - groupNode.HubRect.height * 0.5f,
                currentRect.width,
                currentRect.height);
        }

        internal static PackageGraphGroupLayoutNode FindGroupLayoutNode(
            PackageGraphLayoutResult layout,
            string groupId)
        {
            return layout != null && !string.IsNullOrWhiteSpace(groupId)
                ? layout.GroupNodes.FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(candidate.GroupId, groupId, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        internal static bool TryGetTargetDirection(
            Vector2 center,
            Vector2 targetChildCenter,
            out Vector2 direction)
        {
            Vector2 delta = targetChildCenter - center;

            if (delta.sqrMagnitude <= 0.0001f)
            {
                direction = default(Vector2);
                return false;
            }

            direction = delta.normalized;
            return true;
        }

        internal static void ProjectOrbitalChildren(
            PackageGraphModel graph,
            PackageGraphLayoutResult layout,
            IDictionary<string, Rect> nodeRects,
            IDictionary<string, Rect> groupRects,
            IDictionary<string, Vector2> groupCenters,
            IDictionary<string, float> groupOrbitRadii)
        {
            if (graph == null ||
                layout == null ||
                layout.Mode == PackageGraphLayoutMode.Focus ||
                nodeRects == null ||
                groupRects == null ||
                groupCenters == null ||
                groupOrbitRadii == null)
            {
                return;
            }

            Dictionary<string, PackageGraphGroupLayoutNode> groupNodeById = layout.GroupNodes
                .Where(groupNode => groupNode != null)
                .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphGroupLayoutNode groupNode in layout.GroupNodes)
            {
                if (groupNode == null ||
                    groupNode.Group == null ||
                    groupNode.Collapsed ||
                    !groupCenters.TryGetValue(groupNode.GroupId, out Vector2 center) ||
                    !groupOrbitRadii.TryGetValue(groupNode.GroupId, out float radius) ||
                    radius <= 0.01f)
                {
                    continue;
                }

                foreach (PackageGraphGroup childGroup in graph.Groups)
                {
                    if (childGroup == null ||
                        !string.Equals(childGroup.ParentGroupId, groupNode.GroupId, StringComparison.OrdinalIgnoreCase) ||
                        !groupNodeById.TryGetValue(childGroup.Id, out PackageGraphGroupLayoutNode childGroupNode) ||
                        !groupRects.TryGetValue(childGroup.Id, out Rect childGroupRect) ||
                        !TryGetTargetDirection(groupNode.HubCenter, childGroupNode.HubCenter, out Vector2 direction))
                    {
                        continue;
                    }

                    Vector2 childCenter = center + direction * radius;
                    groupCenters[childGroup.Id] = childCenter;
                    groupRects[childGroup.Id] = PositionGroupRectFromHubCenter(
                        childGroupNode,
                        childGroupRect,
                        childCenter);
                }

                foreach (PackageGraphNode childNode in graph.Nodes)
                {
                    if (childNode == null ||
                        !string.Equals(childNode.GroupId, groupNode.GroupId, StringComparison.OrdinalIgnoreCase) ||
                        !layout.NodeRects.TryGetValue(childNode.PackageId, out Rect targetNodeRect) ||
                        !nodeRects.TryGetValue(childNode.PackageId, out Rect childRect) ||
                        !TryGetTargetDirection(groupNode.HubCenter, targetNodeRect.center, out Vector2 direction))
                    {
                        continue;
                    }

                    nodeRects[childNode.PackageId] = CenterRectOn(childRect, center + direction * radius);
                }
            }
        }

        internal static float EvaluateEnteringOpacity(float progress)
        {
            float t = Mathf.Clamp01(progress);

            if (t <= 0.20f)
            {
                return 0f;
            }

            if (t <= 0.65f)
            {
                return Mathf.Lerp(0f, 0.75f, (t - 0.20f) / 0.45f);
            }

            return Mathf.Lerp(0.75f, 1f, (t - 0.65f) / 0.35f);
        }

        internal static float EvaluateEnteringScale(float progress)
        {
            float t = Mathf.Clamp01(progress);

            if (t <= 0.20f)
            {
                return 0.24f;
            }

            if (t <= 0.65f)
            {
                return Mathf.Lerp(0.24f, 0.85f, (t - 0.20f) / 0.45f);
            }

            return Mathf.Lerp(0.85f, 1f, (t - 0.65f) / 0.35f);
        }

        internal static Rect LerpRect(Rect start, Rect end, float t)
        {
            return new Rect(
                Mathf.Lerp(start.x, end.x, t),
                Mathf.Lerp(start.y, end.y, t),
                Mathf.Lerp(start.width, end.width, t),
                Mathf.Lerp(start.height, end.height, t));
        }

        internal static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        internal static bool AreRectsClose(Rect first, Rect second)
        {
            return Mathf.Abs(first.x - second.x) < 0.5f &&
                   Mathf.Abs(first.y - second.y) < 0.5f &&
                   Mathf.Abs(first.width - second.width) < 0.5f &&
                   Mathf.Abs(first.height - second.height) < 0.5f;
        }
    }
}
