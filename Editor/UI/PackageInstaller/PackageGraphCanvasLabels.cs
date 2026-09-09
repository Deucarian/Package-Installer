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
    internal static class PackageGraphCanvasLabels
    {
        internal static string FormatRelationshipTooltipLabel(
            PackageGraphEdgeKind kind,
            string relationshipLabel)
        {
            string kindLabel = GetRelationshipKindLabel(kind);
            string detail = string.IsNullOrWhiteSpace(relationshipLabel)
                ? string.Empty
                : relationshipLabel.Trim();
            return string.IsNullOrWhiteSpace(detail) ||
                   string.Equals(detail, kindLabel, StringComparison.OrdinalIgnoreCase)
                ? kindLabel
                : kindLabel + " - " + detail;
        }

        internal static string GetRelationshipKindLabel(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                    return "Required dependency";
                case PackageGraphEdgeKind.IntegrationConnection:
                    return "Integration connection";
                case PackageGraphEdgeKind.OptionalCompanion:
                    return "Optional companion";
                case PackageGraphEdgeKind.SuiteMembership:
                    return "Suite membership";
                default:
                    return "Recommended relationship";
            }
        }

        internal static string GetOverflowZoneClass(PackageGraphEgoLayoutZone zone)
        {
            switch (zone)
            {
                case PackageGraphEgoLayoutZone.Providers:
                    return "providers";
                case PackageGraphEgoLayoutZone.Dependents:
                    return "dependents";
                case PackageGraphEgoLayoutZone.Integrations:
                    return "integrations";
                default:
                    return "companions";
            }
        }

        internal static string GetOverflowZoneLabel(PackageGraphEgoLayoutZone zone)
        {
            switch (zone)
            {
                case PackageGraphEgoLayoutZone.Providers:
                    return "prerequisites";
                case PackageGraphEgoLayoutZone.Dependents:
                    return "dependent packages";
                case PackageGraphEgoLayoutZone.Integrations:
                    return "integration packages";
                default:
                    return "companions and suite packages";
            }
        }

        internal static string GetRingClass(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return "runtime";
                case PackageGraphLayoutRing.Integration:
                    return "integration";
                case PackageGraphLayoutRing.Suite:
                    return "suite";
                default:
                    return "infrastructure";
            }
        }

        internal static string GetSectorClass(PackageGraphSectorLabel label)
        {
            return label != null && !string.IsNullOrWhiteSpace(label.ClassName)
                ? label.ClassName
                : GetRingClass(label != null ? label.Ring : PackageGraphLayoutRing.Infrastructure);
        }
    }
}
