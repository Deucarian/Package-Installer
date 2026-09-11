using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphEgoCluster
    {
        public PackageGraphEgoCluster(PackageGraphGroup group, IReadOnlyList<PackageGraphNode> packages)
        {
            Group = group;
            Packages = packages == null
                ? Array.Empty<PackageGraphNode>()
                : packages.Where(package => package != null).ToArray();
        }

        public PackageGraphGroup Group { get; }
        public IReadOnlyList<PackageGraphNode> Packages { get; }

        public float GetVerticalStackHeight(PackageGraphEgoLayoutMetrics metrics) =>
            Packages.Count == 0 ? 0f : Packages.Count * metrics.RelatedPackageMetrics.Height +
                                      Mathf.Max(0, Packages.Count - 1) * metrics.PackageCardGap;

        public float GetVerticalUpExtent(PackageGraphEgoLayoutMetrics metrics) =>
            Mathf.Max(GetVerticalStackHeight(metrics) * 0.5f, metrics.ContextGroupUpExtent);

        public float GetVerticalDownExtent(PackageGraphEgoLayoutMetrics metrics) =>
            Mathf.Max(GetVerticalStackHeight(metrics) * 0.5f, metrics.ContextGroupDownExtent);

        public static float GetVerticalAnchorSpan(
            IReadOnlyList<PackageGraphEgoCluster> clusters, PackageGraphEgoLayoutMetrics metrics)
        {
            float span = 0f;
            for (int index = 0; index < clusters.Count - 1; index++)
            {
                span += clusters[index].GetVerticalDownExtent(metrics) +
                        metrics.PackageSubclusterGap + clusters[index + 1].GetVerticalUpExtent(metrics);
            }
            return span;
        }

        public static bool NeedsDenseLayout(
            IReadOnlyList<PackageGraphEgoCluster> clusters, PackageGraphEgoLayoutMetrics metrics)
        {
            if (clusters.Count == 0) return false;
            // Leave room for the horizontal package lane and its category below/above this zone.
            float laneHeight = PackageGraphEgoLayoutMetrics.FocusNodeEdgeGap +
                               metrics.RelatedPackageMetrics.Height + metrics.CategoryNodeGap +
                               metrics.ContextGroupUpExtent + metrics.ContextGroupDownExtent +
                               PackageGraphLayout.MinimumDenseZoneClearance;
            float halfSpan = GetVerticalAnchorSpan(clusters, metrics) * 0.5f;
            return metrics.SelectedNodeCenter.y - halfSpan - clusters[0].GetVerticalUpExtent(metrics) < laneHeight ||
                   metrics.SelectedNodeCenter.y + halfSpan + clusters[clusters.Count - 1].GetVerticalDownExtent(metrics) >
                   PackageGraphLayout.CanvasHeight - laneHeight;
        }
    }
}
