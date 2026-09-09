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
    internal static class PackageGraphActiveLayoutBounds
    {
        public static Rect Calculate(PackageGraphLayoutResult layout)
        {
            if (layout == null)
            {
                return new Rect(0f, 0f, PackageGraphLayout.CanvasWidth, PackageGraphLayout.CanvasHeight);
            }

            Rect bounds = default(Rect);
            bool hasBounds = false;

            if (IsOverviewLikeLayout(layout.Mode))
            {
                AddRect(ref bounds, ref hasBounds, layout.HubRect);

                foreach (PackageGraphRingGuide guide in layout.RingGuides)
                {
                    if (guide == null)
                    {
                        continue;
                    }

                    AddRect(
                        ref bounds,
                        ref hasBounds,
                        guide.CircleRect);
                }
            }

            foreach (Rect nodeRect in layout.NodeRects.Values)
            {
                AddRect(ref bounds, ref hasBounds, nodeRect);
            }

            foreach (PackageGraphGroupLayoutNode groupNode in layout.GroupNodes)
            {
                if (groupNode == null)
                {
                    continue;
                }

                AddRect(ref bounds, ref hasBounds, groupNode.Rect);
                AddRect(ref bounds, ref hasBounds, groupNode.HubRect);

                if (!groupNode.Collapsed && groupNode.OrbitRadius > 0.01f)
                {
                    AddRect(
                        ref bounds,
                        ref hasBounds,
                        new Rect(
                            groupNode.HubCenter.x - groupNode.OrbitRadius,
                            groupNode.HubCenter.y - groupNode.OrbitRadius,
                            groupNode.OrbitRadius * 2f,
                            groupNode.OrbitRadius * 2f));
                }
            }

            foreach (PackageGraphOverflowSummary summary in layout.OverflowSummaries)
            {
                if (summary != null)
                {
                    AddRect(ref bounds, ref hasBounds, summary.Rect);
                }
            }

            if (!hasBounds)
            {
                bounds = new Rect(
                    PackageGraphLayout.GraphCenter.x - 1f,
                    PackageGraphLayout.GraphCenter.y - 1f,
                    2f,
                    2f);
            }

            return Expand(bounds, GetPadding(layout.Mode));
        }

        private static void AddRect(ref Rect bounds, ref bool hasBounds, Rect rect)
        {
            if (rect.width <= 0.01f || rect.height <= 0.01f)
            {
                return;
            }

            bounds = hasBounds ? Union(bounds, rect) : rect;
            hasBounds = true;
        }

        private static float GetPadding(PackageGraphLayoutMode mode)
        {
            return mode == PackageGraphLayoutMode.Focus ? 56f : 64f;
        }

        private static bool IsOverviewLikeLayout(PackageGraphLayoutMode mode)
        {
            return mode == PackageGraphLayoutMode.Overview;
        }

        private static Rect Union(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(
                rect.x - amount,
                rect.y - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }
    }
}
