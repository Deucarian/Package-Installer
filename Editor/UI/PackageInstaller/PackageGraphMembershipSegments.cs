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
    internal static class PackageGraphMembershipSegments
    {
        internal static void AddOrthogonalStructuralSegment(
            ICollection<PackageGraphStructuralMembershipSegment> segments,
            Vector2 from,
            Vector2 to,
            string packageId)
        {
            if (segments == null || Vector2.Distance(from, to) <= 0.01f)
            {
                return;
            }

            segments.Add(new PackageGraphStructuralMembershipSegment(from, to, packageId));
        }

        internal static void AddCategoryToHorizontalBusSegments(
            ICollection<PackageGraphStructuralMembershipSegment> segments,
            Vector2 categoryAnchor,
            float busY,
            IReadOnlyList<string> packageIds)
        {
            Vector2 busJoin = new Vector2(categoryAnchor.x, busY);
            segments.Add(new PackageGraphStructuralMembershipSegment(
                categoryAnchor,
                busJoin,
                packageIds));
        }

        internal static void AddSplitStructuralBusSegments(
            ICollection<PackageGraphStructuralMembershipSegment> segments,
            IReadOnlyList<KeyValuePair<string, float>> branches,
            float joinAxis,
            Func<float, Vector2> pointAtAxis)
        {
            if (segments == null || branches == null || branches.Count == 0 || pointAtAxis == null)
            {
                return;
            }

            List<float> junctions = new List<float> { joinAxis };

            foreach (KeyValuePair<string, float> branch in branches)
            {
                if (!junctions.Any(axis => Mathf.Abs(axis - branch.Value) <= 0.01f))
                {
                    junctions.Add(branch.Value);
                }
            }

            junctions.Sort();

            for (int index = 0; index < junctions.Count - 1; index++)
            {
                float fromAxis = junctions[index];
                float toAxis = junctions[index + 1];

                if (toAxis - fromAxis <= 0.01f)
                {
                    continue;
                }

                float midpoint = (fromAxis + toAxis) * 0.5f;
                string[] owners = branches
                    .Where(branch => AxisIntervalContains(midpoint, joinAxis, branch.Value))
                    .Select(branch => branch.Key)
                    .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (owners.Length == 0)
                {
                    continue;
                }

                segments.Add(new PackageGraphStructuralMembershipSegment(
                    pointAtAxis(fromAxis),
                    pointAtAxis(toAxis),
                    owners));
            }
        }

        internal static bool AxisIntervalContains(float midpoint, float joinAxis, float branchAxis)
        {
            return midpoint >= Mathf.Min(joinAxis, branchAxis) - 0.01f &&
                   midpoint <= Mathf.Max(joinAxis, branchAxis) + 0.01f;
        }

        internal static float GetClearBusAxis(
            float facingPackageEdge,
            float categoryAnchor,
            float preferredClearance)
        {
            float gap = categoryAnchor - facingPackageEdge;
            float direction = Mathf.Sign(gap);
            float distance = Mathf.Abs(gap);

            if (distance <= preferredClearance * 2f)
            {
                return (facingPackageEdge + categoryAnchor) * 0.5f;
            }

            return facingPackageEdge + direction * preferredClearance;
        }

        internal static Vector2 CalculateAverageRectCenter(IEnumerable<Rect> rects)
        {
            Vector2 total = Vector2.zero;
            int count = 0;

            foreach (Rect rect in rects ?? Array.Empty<Rect>())
            {
                total += rect.center;
                count++;
            }

            return count == 0 ? Vector2.zero : total / count;
        }

        internal static Rect ExpandRect(Rect rect, float amount)
        {
            float safeAmount = Mathf.Max(0f, amount);
            return new Rect(
                rect.xMin - safeAmount,
                rect.yMin - safeAmount,
                rect.width + safeAmount * 2f,
                rect.height + safeAmount * 2f);
        }

        internal static bool TryGetSegmentRectInterval(
            Vector2 from,
            Vector2 to,
            Rect rect,
            out float entry,
            out float exit)
        {
            entry = 0f;
            exit = 1f;

            if (rect.width <= 0.01f || rect.height <= 0.01f)
            {
                return false;
            }

            Vector2 delta = to - from;
            return ClipSegment(-delta.x, from.x - rect.xMin, ref entry, ref exit) &&
                   ClipSegment(delta.x, rect.xMax - from.x, ref entry, ref exit) &&
                   ClipSegment(-delta.y, from.y - rect.yMin, ref entry, ref exit) &&
                   ClipSegment(delta.y, rect.yMax - from.y, ref entry, ref exit) &&
                   exit >= entry &&
                   exit >= 0f &&
                   entry <= 1f;
        }

        internal static bool ClipSegment(float direction, float distance, ref float entry, ref float exit)
        {
            if (Mathf.Abs(direction) <= 0.0001f)
            {
                return distance >= 0f;
            }

            float ratio = distance / direction;

            if (direction < 0f)
            {
                if (ratio > exit)
                {
                    return false;
                }

                entry = Mathf.Max(entry, ratio);
                return true;
            }

            if (ratio < entry)
            {
                return false;
            }

            exit = Mathf.Min(exit, ratio);
            return true;
        }
    }
}
