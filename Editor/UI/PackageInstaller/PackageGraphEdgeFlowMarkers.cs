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
    internal static class PackageGraphEdgeFlowMarkers
    {
        internal static void DrawFlowMarkers(
            PackageGraphPainter painter,
            PackageGraphEdgeRoute route,
            PackageGraphEdgeKind kind,
            Color color,
            float animationPhase)
        {
            int markerCount = GetFlowMarkerCount(kind);
            float markerSize = GetFlowMarkerSize(kind);
            float markerWidth = GetFlowMarkerWidth(kind);
            float markerAlpha = GetFlowMarkerAlpha(kind);
            Color markerColor = new Color(color.r, color.g, color.b, Mathf.Min(1f, color.a * markerAlpha));

            for (int index = 0; index < markerCount; index++)
            {
                float branchOffset = route.UsesSharedTrunk
                    ? route.BranchIndex * 0.11f
                    : 0f;
                float phase = Mathf.Repeat(animationPhase + branchOffset + (index / (float)markerCount), 1f);
                float markerT = Mathf.Lerp(PackageGraphRouteMetrics.MarkerTravelStart, PackageGraphRouteMetrics.MarkerTravelEnd, phase);
                Vector2 point = PackageGraphRouteGeometry.GetPointOnRoute(route.Points, markerT, out Vector2 tangent);
                DrawFlowChevron(
                    painter,
                    point,
                    tangent,
                    markerColor,
                    markerSize,
                    markerWidth);
            }
        }

        internal static void DrawFlowChevron(
            PackageGraphPainter painter,
            Vector2 center,
            Vector2 tangent,
            Color color,
            float size,
            float width)
        {
            if (tangent.sqrMagnitude < 0.01f)
            {
                return;
            }

            Vector2 forward = tangent.normalized;
            Vector2 side = new Vector2(-forward.y, forward.x);
            Vector2 tip = center + forward * size * 0.62f;
            Vector2 back = center - forward * size * 0.58f;
            Vector2 left = back + side * size * 0.48f;
            Vector2 right = back - side * size * 0.48f;

            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(left);
            painter.LineTo(tip);
            painter.LineTo(right);
            painter.Stroke();
        }

        internal static int GetFlowMarkerCount(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.SuiteMembership
                ? 1
                : 2;
        }

        internal static float GetFlowMarkerSize(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 4.8f;
                case PackageGraphEdgeKind.SuiteMembership:
                    return 4.2f;
                default:
                    return 5.0f;
            }
        }

        internal static float GetFlowMarkerWidth(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 1.0f;
                default:
                    return 1.0f;
            }
        }

        internal static float GetFlowMarkerAlpha(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.SuiteMembership:
                    return 0.48f;
                default:
                    return 0.74f;
            }
        }
    }
}
