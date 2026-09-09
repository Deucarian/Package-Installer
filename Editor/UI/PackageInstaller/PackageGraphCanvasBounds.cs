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
    internal static class PackageGraphCanvasBounds
    {
        internal static void AddRouteBounds(
            ref Rect bounds,
            ref bool hasBounds,
            IReadOnlyList<Vector2> points)
        {
            if (points == null)
            {
                return;
            }

            foreach (Vector2 point in points)
            {
                AddPointBounds(ref bounds, ref hasBounds, point);
            }
        }

        internal static void AddRouteBounds(
            ref Rect bounds,
            ref bool hasBounds,
            IReadOnlyList<PackageGraphStructuralMembershipSegment> segments)
        {
            if (segments == null)
            {
                return;
            }

            foreach (PackageGraphStructuralMembershipSegment segment in segments)
            {
                AddPointBounds(ref bounds, ref hasBounds, segment.From);
                AddPointBounds(ref bounds, ref hasBounds, segment.To);
            }
        }

        internal static void AddPointBounds(ref Rect bounds, ref bool hasBounds, Vector2 point)
        {
            Rect pointRect = new Rect(point.x - 1f, point.y - 1f, 2f, 2f);
            bounds = hasBounds ? Union(bounds, pointRect) : pointRect;
            hasBounds = true;
        }

        internal static Rect Union(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }
    }
}
