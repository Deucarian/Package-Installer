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
    internal readonly struct PackageGraphStructuralMembershipSegment
    {
        public PackageGraphStructuralMembershipSegment(Vector2 from, Vector2 to)
            : this(from, to, Array.Empty<string>())
        {
        }

        public PackageGraphStructuralMembershipSegment(Vector2 from, Vector2 to, string packageId)
            : this(
                from,
                to,
                string.IsNullOrWhiteSpace(packageId)
                    ? Array.Empty<string>()
                    : new[] { packageId })
        {
        }

        public PackageGraphStructuralMembershipSegment(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<string> packageIds)
        {
            From = from;
            To = to;
            PackageIds = packageIds == null
                ? Array.Empty<string>()
                : packageIds
                    .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
                    .Select(packageId => packageId.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            PackageId = PackageIds.Count == 1 ? PackageIds[0] : string.Empty;
        }

        public Vector2 From { get; }

        public Vector2 To { get; }

        public string PackageId { get; }

        public IReadOnlyList<string> PackageIds { get; }

        public float Length => Vector2.Distance(From, To);
    }

    internal readonly struct PackageGraphStructuralMembershipRoute
    {
        public PackageGraphStructuralMembershipRoute(
            string groupId,
            IReadOnlyList<string> packageIds,
            IReadOnlyList<PackageGraphStructuralMembershipSegment> segments,
            bool usesBus)
        {
            GroupId = groupId ?? string.Empty;
            PackageIds = packageIds == null
                ? Array.Empty<string>()
                : packageIds
                    .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            Segments = segments == null
                ? Array.Empty<PackageGraphStructuralMembershipSegment>()
                : segments.Where(segment => segment.Length > 0.01f).ToArray();
            UsesBus = usesBus;
        }

        public string GroupId { get; }

        public IReadOnlyList<string> PackageIds { get; }

        public IReadOnlyList<PackageGraphStructuralMembershipSegment> Segments { get; }

        public bool UsesBus { get; }

        public PackageGraphRouteKind RouteKind => PackageGraphRouteKind.StructuralMembership;
    }
}
