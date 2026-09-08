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
    internal readonly struct CategoryStatusSlice
    {
        public CategoryStatusSlice(
            PackageGraphCategoryStatusKey statusKey,
            int count,
            Color color,
            int sortOrder,
            string tooltipLabel)
        {
            StatusKey = statusKey;
            Count = Math.Max(0, count);
            Color = color;
            SortOrder = sortOrder;
            TooltipLabel = tooltipLabel ?? string.Empty;
        }

        public PackageGraphCategoryStatusKey StatusKey { get; }

        public int Count { get; }

        public Color Color { get; }

        public int SortOrder { get; }

        public string TooltipLabel { get; }
    }

    internal readonly struct CategoryStatusRingSegment
    {
        public CategoryStatusRingSegment(
            PackageGraphCategoryStatusKey statusKey,
            int count,
            Color color,
            float startDegrees,
            float sweepDegrees,
            float separatorAfterDegrees,
            bool fullRing)
        {
            StatusKey = statusKey;
            Count = Math.Max(0, count);
            Color = color;
            StartDegrees = startDegrees;
            SweepDegrees = Mathf.Clamp(sweepDegrees, 0f, 360f);
            SeparatorAfterDegrees = Mathf.Max(0f, separatorAfterDegrees);
            FullRing = fullRing;
        }

        public PackageGraphCategoryStatusKey StatusKey { get; }

        public int Count { get; }

        public Color Color { get; }

        public float StartDegrees { get; }

        public float SweepDegrees { get; }

        public float SeparatorAfterDegrees { get; }

        public bool FullRing { get; }
    }

    internal readonly struct CategoryStatusRingVisualState
    {
        public CategoryStatusRingVisualState(
            string ringId,
            Vector2 center,
            float radius,
            float thickness,
            IReadOnlyList<CategoryStatusSlice> slices,
            bool hoverActive,
            bool muted = false)
        {
            RingId = ringId ?? string.Empty;
            Center = center;
            Radius = Mathf.Max(0f, radius);
            Thickness = Mathf.Max(1f, thickness);
            Slices = slices == null
                ? Array.Empty<CategoryStatusSlice>()
                : slices
                    .Where(slice => slice.Count > 0)
                    .OrderBy(slice => slice.SortOrder)
                    .ToArray();
            HoverActive = hoverActive;
            Muted = muted;
        }

        public string RingId { get; }

        public Vector2 Center { get; }

        public float Radius { get; }

        public float Thickness { get; }

        public IReadOnlyList<CategoryStatusSlice> Slices { get; }

        public bool HoverActive { get; }

        public bool Muted { get; }

        public int TotalCount => Slices.Sum(slice => slice.Count);
    }

    internal static class PackageGraphCategoryStatusVisuals
    {
        internal const float StatusRingSeparatorDegrees = 1.4f;

        private static Color InstalledColor => DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Installed, 0.88f);
        private static Color NotInstalledColor => DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Available, 0.82f);
        private static Color AttentionColor => DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Update, 0.92f);
        private static Color UnknownColor => DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Unknown, 0.72f);
        private static Color EmptyNeutralColor => DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Edge, 0.36f);

        public static IReadOnlyList<CategoryStatusSlice> CreateSlices(
            PackageGraphCategoryStatusSummary summary)
        {
            CategoryStatusSlice[] slices =
            {
                CreateSlice(PackageGraphCategoryStatusKey.Installed, summary.InstalledCount),
                CreateSlice(PackageGraphCategoryStatusKey.NotInstalled, summary.NotInstalledCount),
                CreateSlice(PackageGraphCategoryStatusKey.Attention, summary.AttentionCount),
                CreateSlice(PackageGraphCategoryStatusKey.Unknown, summary.UnknownCount)
            };
            return slices
                .Where(slice => slice.Count > 0)
                .OrderBy(slice => slice.SortOrder)
                .ToArray();
        }

        public static IReadOnlyList<CategoryStatusRingSegment> CreateRingSegments(
            PackageGraphCategoryStatusSummary summary)
        {
            return CreateRingSegments(CreateSlices(summary), summary.TotalCount, StatusRingSeparatorDegrees);
        }

        public static IReadOnlyList<CategoryStatusRingSegment> CreateRingSegments(
            IReadOnlyList<CategoryStatusSlice> slices,
            int totalCount,
            float separatorDegrees = StatusRingSeparatorDegrees)
        {
            CategoryStatusSlice[] nonZeroSlices = (slices ?? Array.Empty<CategoryStatusSlice>())
                .Where(slice => slice.Count > 0)
                .OrderBy(slice => slice.SortOrder)
                .ToArray();

            if (nonZeroSlices.Length == 0 || totalCount <= 0)
            {
                return new[]
                {
                    new CategoryStatusRingSegment(
                        PackageGraphCategoryStatusKey.Unknown,
                        0,
                        EmptyNeutralColor,
                        -90f,
                        360f,
                        0f,
                        true)
                };
            }

            if (nonZeroSlices.Length == 1)
            {
                CategoryStatusSlice slice = nonZeroSlices[0];
                return new[]
                {
                    new CategoryStatusRingSegment(
                        slice.StatusKey,
                        slice.Count,
                        slice.Color,
                        -90f,
                        360f,
                        0f,
                        true)
                };
            }

            int safeTotalCount = Math.Max(1, nonZeroSlices.Sum(slice => slice.Count));
            float safeGap = Mathf.Clamp(separatorDegrees, 0f, 24f);
            float totalGap = safeGap * nonZeroSlices.Length;
            float usableAngle = Mathf.Max(0f, 360f - totalGap);
            float cursor = -90f;
            float remainingUsableAngle = usableAngle;
            List<CategoryStatusRingSegment> segments = new List<CategoryStatusRingSegment>(nonZeroSlices.Length);

            for (int index = 0; index < nonZeroSlices.Length; index++)
            {
                CategoryStatusSlice slice = nonZeroSlices[index];
                bool last = index == nonZeroSlices.Length - 1;
                float sweep = last
                    ? remainingUsableAngle
                    : usableAngle * (slice.Count / (float)safeTotalCount);
                sweep = Mathf.Max(0f, sweep);
                remainingUsableAngle = Mathf.Max(0f, remainingUsableAngle - sweep);

                segments.Add(new CategoryStatusRingSegment(
                    slice.StatusKey,
                    slice.Count,
                    slice.Color,
                    cursor,
                    sweep,
                    safeGap,
                    false));
                cursor += sweep + safeGap;
            }

            return segments;
        }

        public static CategoryStatusSlice CreateSlice(
            PackageGraphCategoryStatusKey statusKey,
            int count)
        {
            switch (statusKey)
            {
                case PackageGraphCategoryStatusKey.Installed:
                    return new CategoryStatusSlice(statusKey, count, InstalledColor, 10, "installed");
                case PackageGraphCategoryStatusKey.NotInstalled:
                    return new CategoryStatusSlice(statusKey, count, NotInstalledColor, 20, "not installed");
                case PackageGraphCategoryStatusKey.Attention:
                    return new CategoryStatusSlice(statusKey, count, AttentionColor, 30, "attention");
                default:
                    return new CategoryStatusSlice(statusKey, count, UnknownColor, 40, "unknown");
            }
        }

        public static Color GetColor(PackageGraphCategoryStatusKey statusKey)
        {
            switch (statusKey)
            {
                case PackageGraphCategoryStatusKey.Installed:
                    return InstalledColor;
                case PackageGraphCategoryStatusKey.NotInstalled:
                    return NotInstalledColor;
                case PackageGraphCategoryStatusKey.Attention:
                    return AttentionColor;
                default:
                    return UnknownColor;
            }
        }

        public static Color GetColor(PackageGraphNode package)
        {
            return GetColor(PackageGraphCategoryStatusClassifier.Classify(package));
        }

        public static string FormatTotal(int totalCount)
        {
            return totalCount == 1 ? "1 package" : totalCount + " packages";
        }

        public static string FormatSummary(PackageGraphCategoryStatusSummary summary)
        {
            return summary.InstalledCount + " installed   " +
                   summary.NotInstalledCount + " not installed   " +
                   summary.AttentionCount + " attention";
        }
    }
}
