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
    internal static class PackageGraphMembershipPainter
    {
        internal static void DrawOrbitCircle(PackageGraphPainter painter, PackageGraphOrbitVisualState orbit)
        {
            if (!orbit.Visible || orbit.Radius <= 0.01f)
            {
                return;
            }

            painter.fillColor = DeucarianEditorGraphTheme.WithAlpha(
                DeucarianEditorGraphTheme.OrbitFill,
                orbit.FillOpacity);
            painter.strokeColor = DeucarianEditorGraphTheme.WithAlpha(
                DeucarianEditorGraphTheme.OrbitStroke,
                orbit.StrokeOpacity);
            painter.lineWidth = orbit.Emphasized ? 1.25f : 0.85f;
            DrawCircle(painter, orbit.Center, orbit.Radius);
        }

        internal static void DrawStatusRing(PackageGraphPainter painter, CategoryStatusRingVisualState ring)
        {
            if (ring.Radius <= 0.01f || ring.Thickness <= 0.01f)
            {
                return;
            }

            foreach (CategoryStatusRingSegment segment in PackageGraphCategoryStatusVisuals.CreateRingSegments(
                         ring.Slices,
                         ring.TotalCount))
            {
                Color color = segment.Color;
                color.a = Mathf.Clamp01(color.a + (ring.HoverActive ? 0.12f : 0f));
                if (ring.Muted)
                {
                    color.a *= 0.22f;
                }

                if (segment.FullRing)
                {
                    DrawFullStatusRing(painter, ring.Center, ring.Radius, ring.Thickness, color);
                }
                else
                {
                    DrawRingSegment(
                        painter,
                        ring.Center,
                        ring.Radius,
                        ring.Thickness,
                        segment.StartDegrees,
                        segment.StartDegrees + segment.SweepDegrees,
                        color);
                }
            }
        }

        internal static void DrawFullStatusRing(
            PackageGraphPainter painter,
            Vector2 center,
            float radius,
            float thickness,
            Color color)
        {
            float strokeRadius = Mathf.Max(0.01f, radius - thickness * 0.5f);
            painter.fillColor = Color.clear;
            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(0.01f, thickness);
            DrawCircleStroke(painter, center, strokeRadius);
        }

        internal static void DrawRingSegment(
            PackageGraphPainter painter,
            Vector2 center,
            float radius,
            float thickness,
            float startDegrees,
            float endDegrees,
            Color color)
        {
            float sweep = Mathf.Max(0f, endDegrees - startDegrees);

            if (sweep <= 0.01f)
            {
                return;
            }

            float strokeRadius = Mathf.Max(0.01f, radius - thickness * 0.5f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(sweep / 8f), 4, 48);
            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(0.01f, thickness);
            painter.BeginPath();

            for (int index = 0; index <= steps; index++)
            {
                float angle = Mathf.Deg2Rad * Mathf.Lerp(startDegrees, endDegrees, index / (float)steps);
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * strokeRadius;

                if (index == 0)
                {
                    painter.MoveTo(point);
                }
                else
                {
                    painter.LineTo(point);
                }
            }

            painter.Stroke();
        }

        internal static void StrokeLine(PackageGraphPainter painter, Vector2 from, Vector2 to)
        {
            if ((to - from).sqrMagnitude <= 0.01f)
            {
                return;
            }

            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        internal static void DrawCircle(PackageGraphPainter painter, Vector2 center, float radius)
        {
            DrawCirclePath(painter, center, radius);
            painter.Fill();
            painter.Stroke();
        }

        internal static void DrawCircleStroke(PackageGraphPainter painter, Vector2 center, float radius)
        {
            DrawCirclePath(painter, center, radius);
            painter.Stroke();
        }

        internal static void DrawCirclePath(PackageGraphPainter painter, Vector2 center, float radius)
        {
            const float Kappa = 0.55228475f;
            float safeRadius = Mathf.Max(0f, radius);
            float offset = safeRadius * Kappa;
            float left = center.x - safeRadius;
            float right = center.x + safeRadius;
            float top = center.y - safeRadius;
            float bottom = center.y + safeRadius;

            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x, top));
            painter.BezierCurveTo(
                new Vector2(center.x + offset, top),
                new Vector2(right, center.y - offset),
                new Vector2(right, center.y));
            painter.BezierCurveTo(
                new Vector2(right, center.y + offset),
                new Vector2(center.x + offset, bottom),
                new Vector2(center.x, bottom));
            painter.BezierCurveTo(
                new Vector2(center.x - offset, bottom),
                new Vector2(left, center.y + offset),
                new Vector2(left, center.y));
            painter.BezierCurveTo(
                new Vector2(left, center.y - offset),
                new Vector2(center.x - offset, top),
                new Vector2(center.x, top));
            painter.ClosePath();
        }
    }
}
