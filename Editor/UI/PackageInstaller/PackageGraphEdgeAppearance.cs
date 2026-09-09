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
    internal static class PackageGraphEdgeAppearance
    {
        internal static bool UsesTwoPassStroke(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.HardDependency ||
                   kind == PackageGraphEdgeKind.IntegrationConnection ||
                   kind == PackageGraphEdgeKind.OptionalCompanion ||
                   kind == PackageGraphEdgeKind.SuiteMembership;
        }

        internal static bool IsRouteEmphasized(
            PackageGraphEdgeRoute route,
            PackageGraphFocus focus,
            string previewPackageId)
        {
            if (focus == null ||
                !route.Bundle.Edges.Any(edge => focus.IsEdgeEmphasized(edge)))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(previewPackageId) ||
                   route.Bundle.ConnectsPackage(previewPackageId);
        }

        internal static bool IsDashed(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.Recommended ||
                   kind == PackageGraphEdgeKind.SuiteMembership;
        }

        internal static bool IsDotted(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.OptionalCompanion;
        }

        internal static bool ShouldAnimate(PackageGraphEdgeKind kind)
        {
            return SupportsDirectionalFlowMarkers(kind);
        }

        internal static bool SupportsDirectionalFlowMarkers(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.HardDependency ||
                   kind == PackageGraphEdgeKind.IntegrationConnection;
        }

        internal static Color GetEdgeColor(
            PackageGraphModel graph,
            PackageGraphEdge edge,
            bool emphasized,
            bool focusMode)
        {
            PackageGraphCategoryStatusKey statusKey = PackageGraphCategoryStatusKey.Unknown;
            bool packageStatusResolved = false;
            if (graph != null && edge != null)
            {
                if (graph.TryGetNode(edge.FromPackageId, out PackageGraphNode sourcePackage))
                {
                    statusKey = PackageGraphCategoryStatusClassifier.Classify(sourcePackage);
                    packageStatusResolved = true;
                }
                else if (graph.TryGetNode(edge.ToPackageId, out PackageGraphNode targetPackage))
                {
                    statusKey = PackageGraphCategoryStatusClassifier.Classify(targetPackage);
                    packageStatusResolved = true;
                }
            }

            if (!packageStatusResolved && edge != null && edge.State == PackageGraphEdgeState.Warning)
            {
                statusKey = PackageGraphCategoryStatusKey.Attention;
            }

            Color color = PackageGraphCategoryStatusVisuals.GetColor(statusKey);
            bool optional = edge != null && edge.Kind == PackageGraphEdgeKind.OptionalCompanion;
            color.a = emphasized
                ? optional ? 0.64f : 0.84f
                : focusMode
                    ? optional ? 0.18f : 0.30f
                    : optional ? 0.18f : 0.30f;
            return color;
        }

        internal static float GetEdgeWidth(PackageGraphEdge edge, bool emphasized, bool focusMode)
        {
            if (emphasized)
            {
                if (edge.State == PackageGraphEdgeState.Warning)
                {
                    return 2.1f;
                }

                if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
                {
                    return 1.9f;
                }

                return edge.Kind == PackageGraphEdgeKind.OptionalCompanion ? 1.15f : 2.15f;
            }

            if (focusMode)
            {
                return edge.Kind == PackageGraphEdgeKind.OptionalCompanion ? 0.75f : 0.95f;
            }

            switch (edge.Kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                    return 1.45f;
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 1.2f;
                case PackageGraphEdgeKind.OptionalCompanion:
                    return 0.9f;
                default:
                    return 1f;
            }
        }
    }
}
