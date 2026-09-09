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
    internal static class PackageGraphLayoutSnapshots
    {

        internal static Dictionary<string, Rect> CaptureCurrentNodeRects(PackageGraphLayoutAnimation animation, PackageGraphLayoutResult layout)
        {
            if (animation.Nodes.Count > 0)
            {
                return new Dictionary<string, Rect>(animation.Nodes, StringComparer.OrdinalIgnoreCase);
            }

            return layout != null
                ? new Dictionary<string, Rect>(layout.NodeRects, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        }

        internal static Dictionary<string, PackageGraphNodeVisualState> CaptureCurrentNodeVisualStates(PackageGraphLayoutAnimation animation, PackageGraphLayoutResult layout)
        {
            if (animation.NodeStates.Count > 0)
            {
                return new Dictionary<string, PackageGraphNodeVisualState>(
                    animation.NodeStates,
                    StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, PackageGraphNodeVisualState> states =
                new Dictionary<string, PackageGraphNodeVisualState>(StringComparer.OrdinalIgnoreCase);

            if (layout == null)
            {
                return states;
            }

            foreach (KeyValuePair<string, Rect> nodeRect in layout.NodeRects)
            {
                states[nodeRect.Key] = PackageGraphNodeVisualState.Stable(
                    nodeRect.Value,
                    animation.GetPresentationLevel(nodeRect.Key));
            }

            return states;
        }

        internal static Dictionary<string, Rect> CaptureCurrentGroupRects(PackageGraphLayoutAnimation animation, PackageGraphLayoutResult layout)
        {
            return CaptureGroups(animation.Groups, layout, group => group.Rect);
        }

        internal static Dictionary<string, Vector2> CaptureCurrentGroupCenters(PackageGraphLayoutAnimation animation, PackageGraphLayoutResult layout)
        {
            return CaptureGroups(animation.GroupCenters, layout, group => group.HubCenter);
        }

        internal static Dictionary<string, float> CaptureCurrentGroupOrbitRadii(PackageGraphLayoutAnimation animation, PackageGraphLayoutResult layout)
        {
            return CaptureGroups(animation.GroupOrbitRadii, layout, group => group.OrbitRadius);
        }

        private static Dictionary<string, T> CaptureGroups<T>(Dictionary<string, T> animated,
            PackageGraphLayoutResult layout, Func<PackageGraphGroupLayoutNode, T> select)
        {
            if (animated.Count > 0)
                return new Dictionary<string, T>(animated, StringComparer.OrdinalIgnoreCase);
            return layout != null
                ? layout.GroupNodes
                    .Where(group => group != null)
                    .GroupBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => select(group.First()), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
