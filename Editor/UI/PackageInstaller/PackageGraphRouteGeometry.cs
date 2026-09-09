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
    internal static class PackageGraphRouteGeometry
    {
        internal static bool IsOrthogonalSegment(Vector2 from, Vector2 to)
        {
            return Mathf.Abs(from.x - to.x) <= 0.1f ||
                   Mathf.Abs(from.y - to.y) <= 0.1f;
        }

        internal static bool RectContainsPointInclusive(Rect rect, Vector2 point)
        {
            return point.x >= rect.xMin &&
                   point.x <= rect.xMax &&
                   point.y >= rect.yMin &&
                   point.y <= rect.yMax;
        }

        internal static Rect BuildSegmentBounds(Vector2 start, Vector2 end)
        {
            return Rect.MinMaxRect(
                Mathf.Min(start.x, end.x) - 0.01f,
                Mathf.Min(start.y, end.y) - 0.01f,
                Mathf.Max(start.x, end.x) + 0.01f,
                Mathf.Max(start.y, end.y) + 0.01f);
        }

        internal static bool RectsOverlap(Rect first, Rect second)
        {
            return first.xMin <= second.xMax &&
                   first.xMax >= second.xMin &&
                   first.yMin <= second.yMax &&
                   first.yMax >= second.yMin;
        }

        internal static IReadOnlyList<Vector2> SimplifyRoutePoints(IReadOnlyList<Vector2> points)
        {
            if (points == null)
            {
                return Array.Empty<Vector2>();
            }

            List<Vector2> cleaned = new List<Vector2>();

            foreach (Vector2 point in points)
            {
                if (cleaned.Count == 0 || Vector2.Distance(cleaned[cleaned.Count - 1], point) > 0.1f)
                {
                    cleaned.Add(point);
                }
            }

            for (int index = cleaned.Count - 2; index > 0; index--)
            {
                if (AreCollinear(cleaned[index - 1], cleaned[index], cleaned[index + 1]))
                {
                    cleaned.RemoveAt(index);
                }
            }

            return cleaned;
        }

        internal static bool AreCollinear(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 bc = c - b;
            return Mathf.Abs(ab.x * bc.y - ab.y * bc.x) < 0.1f;
        }

        internal static Vector2 GetPointOnRoute(
            IReadOnlyList<Vector2> points,
            float normalizedDistance,
            out Vector2 tangent)
        {
            tangent = Vector2.right;

            if (points == null || points.Count == 0)
            {
                return default(Vector2);
            }

            if (points.Count == 1)
            {
                return points[0];
            }

            float totalLength = GetRouteLength(points);

            if (totalLength <= 0.01f)
            {
                tangent = points[points.Count - 1] - points[0];
                return points[0];
            }

            float targetDistance = Mathf.Clamp01(normalizedDistance) * totalLength;
            float consumed = 0f;

            for (int index = 0; index < points.Count - 1; index++)
            {
                Vector2 start = points[index];
                Vector2 end = points[index + 1];
                Vector2 segment = end - start;
                float length = segment.magnitude;

                if (length <= 0.01f)
                {
                    continue;
                }

                if (consumed + length >= targetDistance)
                {
                    tangent = segment;
                    return Vector2.Lerp(start, end, (targetDistance - consumed) / length);
                }

                consumed += length;
            }

            tangent = points[points.Count - 1] - points[points.Count - 2];
            return points[points.Count - 1];
        }

        internal static float GetRouteLength(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 2)
            {
                return 0f;
            }

            float length = 0f;

            for (int index = 0; index < points.Count - 1; index++)
            {
                length += Vector2.Distance(points[index], points[index + 1]);
            }

            return length;
        }

        internal static Rect ShrinkRect(Rect rect, float amount)
        {
            float inset = Mathf.Max(0f, amount);
            return new Rect(
                rect.xMin + inset,
                rect.yMin + inset,
                Mathf.Max(0f, rect.width - inset * 2f),
                Mathf.Max(0f, rect.height - inset * 2f));
        }

        internal static Rect InflateRect(Rect rect, float amount)
        {
            float padding = Mathf.Max(0f, amount);
            return Rect.MinMaxRect(
                rect.xMin - padding,
                rect.yMin - padding,
                rect.xMax + padding,
                rect.yMax + padding);
        }

        internal static bool LineIntersectsRectInterior(
            Vector2 start,
            Vector2 end,
            Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return false;
            }

            if (Mathf.Max(start.x, end.x) < rect.xMin ||
                Mathf.Min(start.x, end.x) > rect.xMax ||
                Mathf.Max(start.y, end.y) < rect.yMin ||
                Mathf.Min(start.y, end.y) > rect.yMax)
            {
                return false;
            }

            if (rect.Contains(start) || rect.Contains(end))
            {
                return true;
            }

            Vector2 bottomLeft = new Vector2(rect.xMin, rect.yMin);
            Vector2 bottomRight = new Vector2(rect.xMax, rect.yMin);
            Vector2 topRight = new Vector2(rect.xMax, rect.yMax);
            Vector2 topLeft = new Vector2(rect.xMin, rect.yMax);

            return LineSegmentsIntersect(start, end, bottomLeft, bottomRight) ||
                   LineSegmentsIntersect(start, end, bottomRight, topRight) ||
                   LineSegmentsIntersect(start, end, topRight, topLeft) ||
                   LineSegmentsIntersect(start, end, topLeft, bottomLeft);
        }

        internal static bool LineSegmentsIntersect(
            Vector2 firstStart,
            Vector2 firstEnd,
            Vector2 secondStart,
            Vector2 secondEnd)
        {
            float firstOrientation = Cross(firstEnd - firstStart, secondStart - firstStart);
            float secondOrientation = Cross(firstEnd - firstStart, secondEnd - firstStart);
            float thirdOrientation = Cross(secondEnd - secondStart, firstStart - secondStart);
            float fourthOrientation = Cross(secondEnd - secondStart, firstEnd - secondStart);

            if (HasOppositeSigns(firstOrientation, secondOrientation) &&
                HasOppositeSigns(thirdOrientation, fourthOrientation))
            {
                return true;
            }

            const float Epsilon = 0.001f;
            return Mathf.Abs(firstOrientation) <= Epsilon && IsPointOnSegment(secondStart, firstStart, firstEnd) ||
                   Mathf.Abs(secondOrientation) <= Epsilon && IsPointOnSegment(secondEnd, firstStart, firstEnd) ||
                   Mathf.Abs(thirdOrientation) <= Epsilon && IsPointOnSegment(firstStart, secondStart, secondEnd) ||
                   Mathf.Abs(fourthOrientation) <= Epsilon && IsPointOnSegment(firstEnd, secondStart, secondEnd);
        }

        internal static bool HasOppositeSigns(float first, float second)
        {
            return first > 0f && second < 0f ||
                   first < 0f && second > 0f;
        }

        internal static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        internal static bool IsPointOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            const float Epsilon = 0.001f;
            return point.x >= Mathf.Min(segmentStart.x, segmentEnd.x) - Epsilon &&
                   point.x <= Mathf.Max(segmentStart.x, segmentEnd.x) + Epsilon &&
                   point.y >= Mathf.Min(segmentStart.y, segmentEnd.y) - Epsilon &&
                   point.y <= Mathf.Max(segmentStart.y, segmentEnd.y) + Epsilon;
        }
    }
}
