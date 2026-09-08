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
    internal static class PackageGraphEdgePainter
    {
        internal static void DrawEdge(
            PackageGraphPainter painter,
            PackageGraphModel graph,
            PackageGraphEdgeRoute route,
            bool emphasized,
            bool focusMode,
            float animationPhase)
        {
            if (route.Bundle.IsCompositeDependencyIntegration)
            {
                DrawCompositeDependencyIntegrationRoute(
                    painter,
                    graph,
                    route,
                    emphasized,
                    focusMode,
                    animationPhase);
                return;
            }

            PackageGraphEdge edge = route.Edge;
            Color color = PackageGraphEdgeAppearance.GetEdgeColor(graph, edge, emphasized, focusMode);
            float width = PackageGraphEdgeAppearance.GetEdgeWidth(edge, emphasized, focusMode);
            bool animate = emphasized &&
                           focusMode &&
                           PackageGraphEdgeAppearance.ShouldAnimate(edge.Kind);

            if (color.a <= 0.01f || width <= 0.01f || route.Points.Count < 2)
            {
                return;
            }

            DrawRouteUnderlay(painter, route.Points, edge.Kind, width, emphasized, focusMode);

            if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
            {
                DrawIntegrationCableRoute(
                    painter,
                    route.Points,
                    color,
                    width,
                    emphasized);
            }
            else if (PackageGraphEdgeAppearance.IsDotted(edge.Kind))
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                DrawDashedPolyline(
                    painter,
                    route.Points,
                    2.5f,
                    7.5f,
                    animate ? (1f - animationPhase) * 10f : 0f);
            }
            else if (PackageGraphEdgeAppearance.IsDashed(edge.Kind))
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                DrawDashedPolyline(
                    painter,
                    route.Points,
                    PackageGraphRouteMetrics.AnimatedDashLength,
                    PackageGraphRouteMetrics.AnimatedDashGap,
                    animate ? (1f - animationPhase) * (PackageGraphRouteMetrics.AnimatedDashLength + PackageGraphRouteMetrics.AnimatedDashGap) : 0f);
            }
            else
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                DrawPolylineStroke(painter, route.Points);
            }

            if (animate)
            {
                if (PackageGraphEdgeAppearance.SupportsDirectionalFlowMarkers(edge.Kind))
                {
                    Color pulseColor = new Color(color.r, color.g, color.b, Mathf.Min(0.64f, color.a + 0.06f));
                    PackageGraphEdgeFlowMarkers.DrawFlowMarkers(
                        painter,
                        route,
                        edge.Kind,
                        pulseColor,
                        animationPhase);
                }
            }

            if (edge.State == PackageGraphEdgeState.Warning)
            {
                DrawWarningMarker(painter, PackageGraphRouteGeometry.GetPointOnRoute(route.Points, 0.5f, out _));
            }
        }

        internal static void DrawCompositeDependencyIntegrationRoute(
            PackageGraphPainter painter,
            PackageGraphModel graph,
            PackageGraphEdgeRoute route,
            bool emphasized,
            bool focusMode,
            float animationPhase)
        {
            PackageGraphEdge dependencyEdge = route.Bundle.GetEdge(PackageGraphEdgeKind.HardDependency) ?? route.Edge;
            PackageGraphEdge integrationEdge = route.Bundle.GetEdge(PackageGraphEdgeKind.IntegrationConnection) ?? route.Edge;
            Color cableColor = PackageGraphEdgeAppearance.GetEdgeColor(graph, integrationEdge, emphasized, focusMode);
            Color flowColor = PackageGraphEdgeAppearance.GetEdgeColor(graph, dependencyEdge, emphasized, focusMode);
            float cableWidth = Mathf.Max(0.9f, PackageGraphEdgeAppearance.GetEdgeWidth(integrationEdge, emphasized, focusMode) * 0.82f);
            float flowWidth = Mathf.Max(1.1f, PackageGraphEdgeAppearance.GetEdgeWidth(dependencyEdge, emphasized, focusMode) * 0.72f);

            if (route.Points.Count < 2 || (cableColor.a <= 0.01f && flowColor.a <= 0.01f))
            {
                return;
            }

            Color underlay = DeucarianEditorGraphTheme.WithAlpha(
                DeucarianEditorGraphTheme.EdgeUnderlay,
                emphasized ? 0.36f : 0.18f);
            painter.strokeColor = underlay;
            painter.lineWidth = Mathf.Max(2.4f, flowWidth + 1.4f);
            DrawPolylineStroke(painter, route.Points);

            DrawIntegrationCableRoute(
                painter,
                route.Points,
                cableColor,
                cableWidth,
                emphasized);

            painter.strokeColor = flowColor;
            painter.lineWidth = flowWidth;
            DrawPolylineStroke(painter, route.Points);

            bool animate = emphasized &&
                           focusMode &&
                           PackageGraphEdgeAppearance.SupportsDirectionalFlowMarkers(PackageGraphEdgeKind.HardDependency);

            if (animate)
            {
                Color markerColor = new Color(
                    flowColor.r,
                    flowColor.g,
                    flowColor.b,
                    Mathf.Min(0.70f, flowColor.a + 0.06f));
                PackageGraphEdgeFlowMarkers.DrawFlowMarkers(
                    painter,
                    route,
                    PackageGraphEdgeKind.HardDependency,
                    markerColor,
                    animationPhase);
            }

            if (route.Bundle.Edges.Any(edge => edge.State == PackageGraphEdgeState.Warning))
            {
                DrawWarningMarker(painter, PackageGraphRouteGeometry.GetPointOnRoute(route.Points, 0.5f, out _));
            }
        }

        internal static void DrawPolylineStroke(
            PackageGraphPainter painter,
            IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            painter.BeginPath();
            painter.MoveTo(points[0]);

            for (int index = 1; index < points.Count; index++)
            {
                painter.LineTo(points[index]);
            }

            painter.Stroke();
        }

        internal static void DrawRouteUnderlay(
            PackageGraphPainter painter,
            IReadOnlyList<Vector2> points,
            PackageGraphEdgeKind kind,
            float semanticWidth,
            bool emphasized,
            bool focusMode)
        {
            if (points == null || points.Count < 2 || !UsesTwoPassStrokeForTests(kind))
            {
                return;
            }

            float alpha = emphasized
                ? 0.34f
                : focusMode
                    ? 0.16f
                    : 0.12f;
            float extraWidth = emphasized ? 2.15f : 1.45f;
            painter.strokeColor = DeucarianEditorGraphTheme.WithAlpha(
                DeucarianEditorGraphTheme.EdgeUnderlay,
                alpha);
            painter.lineWidth = Mathf.Max(1.2f, semanticWidth + extraWidth);
            DrawPolylineStroke(painter, points);
        }

        internal static void DrawIntegrationCableRoute(
            PackageGraphPainter painter,
            IReadOnlyList<Vector2> points,
            Color color,
            float width,
            bool emphasized)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            float offset = emphasized ? 1.8f : 1.35f;
            Color underlay = DeucarianEditorGraphTheme.WithAlpha(
                DeucarianEditorGraphTheme.EdgeUnderlay,
                Mathf.Min(0.34f, color.a * 0.50f));

            painter.strokeColor = underlay;
            painter.lineWidth = Mathf.Max(1.2f, width + 0.85f);
            DrawPolylineStroke(painter, points);

            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(0.65f, width * 0.42f);
            DrawOffsetPolyline(painter, points, offset);
            DrawOffsetPolyline(painter, points, -offset);
        }

        internal static void DrawOffsetPolyline(
            PackageGraphPainter painter,
            IReadOnlyList<Vector2> points,
            float offset)
        {
            for (int index = 0; index < points.Count - 1; index++)
            {
                Vector2 start = points[index];
                Vector2 end = points[index + 1];
                Vector2 delta = end - start;

                if (delta.sqrMagnitude <= 0.01f)
                {
                    continue;
                }

                Vector2 side = new Vector2(-delta.y, delta.x).normalized * offset;
                painter.BeginPath();
                painter.MoveTo(start + side);
                painter.LineTo(end + side);
                painter.Stroke();
            }
        }

        internal static void DrawDashedPolyline(
            PackageGraphPainter painter,
            IReadOnlyList<Vector2> points,
            float dashLength,
            float gapLength,
            float dashOffset)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            float patternLength = dashLength + gapLength;
            float normalizedOffset = Mathf.Repeat(dashOffset, patternLength);

            bool draw = normalizedOffset < dashLength;
            float segmentCursor = draw ? normalizedOffset : normalizedOffset - dashLength;

            for (int index = 0; index < points.Count - 1; index++)
            {
                Vector2 previous = points[index];
                Vector2 current = points[index + 1];
                Vector2 delta = current - previous;
                float length = delta.magnitude;

                if (length <= 0.1f)
                {
                    continue;
                }

                Vector2 direction = delta / length;
                float consumed = 0f;

                while (consumed < length)
                {
                    float targetLength = draw ? dashLength : gapLength;
                    float step = Mathf.Min(targetLength - segmentCursor, length - consumed);
                    Vector2 segmentStart = previous + direction * consumed;
                    Vector2 segmentEnd = previous + direction * (consumed + step);

                    if (draw)
                    {
                        painter.BeginPath();
                        painter.MoveTo(segmentStart);
                        painter.LineTo(segmentEnd);
                        painter.Stroke();
                    }

                    consumed += step;
                    segmentCursor += step;

                    if (segmentCursor >= targetLength - 0.01f)
                    {
                        segmentCursor = 0f;
                        draw = !draw;
                    }
                }
            }
        }

        internal static void DrawWarningMarker(PackageGraphPainter painter, Vector2 center)
        {
            painter.fillColor = DeucarianEditorGraphTheme.WithAlpha(
                DeucarianEditorGraphTheme.WarningMarkerFill,
                0.90f);
            painter.strokeColor = DeucarianEditorGraphTheme.WithAlpha(
                DeucarianEditorGraphTheme.WarningMarkerStroke,
                0.86f);
            painter.lineWidth = 1.2f;

            painter.BeginPath();
            painter.MoveTo(center + new Vector2(0f, -7f));
            painter.LineTo(center + new Vector2(7f, 6f));
            painter.LineTo(center + new Vector2(-7f, 6f));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }
    }
}
