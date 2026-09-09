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
    internal enum PackageGraphEdgeRoutePort
    {
        Auto,
        Left,
        Right,
        Top,
        Bottom
    }

    internal enum PackageGraphEdgeRouteZone
    {
        Direct,
        Providers,
        Dependents,
        Integrations,
        CompanionsAndSuites
    }

    internal enum PackageGraphRouteKind
    {
        StructuralMembership,
        Dependency,
        Integration,
        OptionalCompanion,
        SuiteMembership,
        CompositeDependencyIntegration
    }

    [Flags]

    internal enum PackageGraphConnectionSemantics
    {
        None = 0,
        Dependency = 1 << 0,
        Integration = 1 << 1,
        OptionalCompanion = 1 << 2,
        SuiteMembership = 1 << 3,
        Recommended = 1 << 4
    }

    internal readonly struct PackageGraphConnectionBundle
    {
        public PackageGraphConnectionBundle(
            string sourcePackageId,
            string targetPackageId,
            IReadOnlyList<PackageGraphEdge> edges)
        {
            SourcePackageId = sourcePackageId ?? string.Empty;
            TargetPackageId = targetPackageId ?? string.Empty;
            Edges = edges == null
                ? Array.Empty<PackageGraphEdge>()
                : edges
                    .Where(edge => edge != null)
                    .OrderBy(edge => GetSemanticPriority(edge.Kind))
                    .ThenBy(edge => edge.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            PrimaryEdge = Edges.Count > 0
                ? Edges[0]
                : new PackageGraphEdge(SourcePackageId, TargetPackageId, PackageGraphEdgeKind.HardDependency, PackageGraphEdgeState.Active, string.Empty);
            Semantics = BuildSemantics(Edges);
            Key = SourcePackageId + ">" + TargetPackageId + ":" + Semantics;
        }

        public string SourcePackageId { get; }

        public string TargetPackageId { get; }

        public IReadOnlyList<PackageGraphEdge> Edges { get; }

        public PackageGraphEdge PrimaryEdge { get; }

        public PackageGraphConnectionSemantics Semantics { get; }

        public string Key { get; }

        public bool HasDependency => HasSemantic(PackageGraphConnectionSemantics.Dependency);

        public bool HasIntegration => HasSemantic(PackageGraphConnectionSemantics.Integration);

        public bool HasOptionalCompanion => HasSemantic(PackageGraphConnectionSemantics.OptionalCompanion);

        public bool HasSuiteMembership => HasSemantic(PackageGraphConnectionSemantics.SuiteMembership);

        public bool HasRecommended => HasSemantic(PackageGraphConnectionSemantics.Recommended);

        public bool IsCompositeDependencyIntegration => HasDependency && HasIntegration;

        public bool ConnectsPackage(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) &&
                   (string.Equals(SourcePackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(TargetPackageId, packageId, StringComparison.OrdinalIgnoreCase));
        }

        public string GetOtherPackageId(string packageId)
        {
            if (string.Equals(SourcePackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                return TargetPackageId;
            }

            return string.Equals(TargetPackageId, packageId, StringComparison.OrdinalIgnoreCase)
                ? SourcePackageId
                : string.Empty;
        }

        public bool HasKind(PackageGraphEdgeKind kind)
        {
            return Edges.Any(edge => edge.Kind == kind);
        }

        public PackageGraphEdge GetEdge(PackageGraphEdgeKind kind)
        {
            return Edges.FirstOrDefault(edge => edge.Kind == kind);
        }

        public bool HasSemantic(PackageGraphConnectionSemantics semantic)
        {
            return (Semantics & semantic) == semantic;
        }

        private static PackageGraphConnectionSemantics BuildSemantics(IEnumerable<PackageGraphEdge> edges)
        {
            PackageGraphConnectionSemantics semantics = PackageGraphConnectionSemantics.None;

            foreach (PackageGraphEdge edge in edges ?? Array.Empty<PackageGraphEdge>())
            {
                switch (edge.Kind)
                {
                    case PackageGraphEdgeKind.HardDependency:
                        semantics |= PackageGraphConnectionSemantics.Dependency;
                        break;
                    case PackageGraphEdgeKind.IntegrationConnection:
                        semantics |= PackageGraphConnectionSemantics.Integration;
                        break;
                    case PackageGraphEdgeKind.OptionalCompanion:
                        semantics |= PackageGraphConnectionSemantics.OptionalCompanion;
                        break;
                    case PackageGraphEdgeKind.SuiteMembership:
                        semantics |= PackageGraphConnectionSemantics.SuiteMembership;
                        break;
                    case PackageGraphEdgeKind.Recommended:
                        semantics |= PackageGraphConnectionSemantics.Recommended;
                        break;
                }
            }

            return semantics;
        }

        internal static int GetSemanticPriority(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                    return 0;
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 1;
                case PackageGraphEdgeKind.OptionalCompanion:
                    return 2;
                case PackageGraphEdgeKind.SuiteMembership:
                    return 3;
                case PackageGraphEdgeKind.Recommended:
                    return 4;
                default:
                    return 10;
            }
        }
    }

    internal readonly struct PackageGraphEdgeRoute
    {
        public PackageGraphEdgeRoute(
            PackageGraphEdge edge,
            PackageGraphEdgeRoutePort sourcePort,
            PackageGraphEdgeRoutePort targetPort,
            PackageGraphEdgeRouteZone zone,
            string sharedTrunkId,
            int branchIndex,
            int branchCount,
            IReadOnlyList<Vector2> points)
            : this(
                new PackageGraphConnectionBundle(
                    edge != null ? edge.FromPackageId : string.Empty,
                    edge != null ? edge.ToPackageId : string.Empty,
                    edge != null ? new[] { edge } : Array.Empty<PackageGraphEdge>()),
                sourcePort,
                targetPort,
                zone,
                sharedTrunkId,
                branchIndex,
                branchCount,
                points)
        {
        }

        public PackageGraphEdgeRoute(
            PackageGraphConnectionBundle bundle,
            PackageGraphEdgeRoutePort sourcePort,
            PackageGraphEdgeRoutePort targetPort,
            PackageGraphEdgeRouteZone zone,
            string sharedTrunkId,
            int branchIndex,
            int branchCount,
            IReadOnlyList<Vector2> points)
        {
            Bundle = bundle;
            Edge = bundle.PrimaryEdge;
            SourcePort = sourcePort;
            TargetPort = targetPort;
            Zone = zone;
            SharedTrunkId = sharedTrunkId ?? string.Empty;
            BranchIndex = Mathf.Max(0, branchIndex);
            BranchCount = Mathf.Max(1, branchCount);
            Points = points ?? Array.Empty<Vector2>();
            RouteKind = ResolveRouteKind(bundle);
        }

        public PackageGraphEdge Edge { get; }

        public PackageGraphConnectionBundle Bundle { get; }

        public PackageGraphEdgeRoutePort SourcePort { get; }

        public PackageGraphEdgeRoutePort TargetPort { get; }

        public PackageGraphEdgeRouteZone Zone { get; }

        public string SharedTrunkId { get; }

        public int BranchIndex { get; }

        public int BranchCount { get; }

        public IReadOnlyList<Vector2> Points { get; }

        public PackageGraphRouteKind RouteKind { get; }

        public bool UsesSharedTrunk => BranchCount > 1 && !string.IsNullOrWhiteSpace(SharedTrunkId);

        public bool HasSemantic(PackageGraphConnectionSemantics semantic)
        {
            return Bundle.HasSemantic(semantic);
        }

        public bool HasKind(PackageGraphEdgeKind kind)
        {
            return Bundle.HasKind(kind);
        }

        private static PackageGraphRouteKind ResolveRouteKind(PackageGraphConnectionBundle bundle)
        {
            if (bundle.IsCompositeDependencyIntegration)
            {
                return PackageGraphRouteKind.CompositeDependencyIntegration;
            }

            if (bundle.HasDependency)
            {
                return PackageGraphRouteKind.Dependency;
            }

            if (bundle.HasIntegration)
            {
                return PackageGraphRouteKind.Integration;
            }

            if (bundle.HasOptionalCompanion || bundle.HasRecommended)
            {
                return PackageGraphRouteKind.OptionalCompanion;
            }

            return bundle.HasSuiteMembership
                ? PackageGraphRouteKind.SuiteMembership
                : PackageGraphRouteKind.Dependency;
        }
    }

    internal struct PackageGraphEdgeRouteBuildDiagnostics
    {
        public int RouteCount;
        public int RouteCacheHits;
        public int RouteCacheMisses;
        public int RouteCacheNoEntryMisses;
        public int RouteCacheLayoutMisses;
        public int RouteCacheEndpointMisses;
        public int RouteCacheFocusGraphMisses;
        public int RouteCacheStyleMisses;
        public long RouteCacheLookupTicks;
        public long RouteCalculationTicks;
        public long GeometryLayoutReadTicks;
        public long VisualElementReuseTicks;
        public long StyleClassUpdateTicks;
        public long PainterPassTicks;

        public void AddRouteCacheLookupTicks(long ticks)
        {
            RouteCacheLookupTicks += Math.Max(0L, ticks);
        }

        public void AddRouteCacheMiss(PackageGraphEdgeRouteCacheMissReason reason)
        {
            RouteCacheMisses++;

            switch (reason)
            {
                case PackageGraphEdgeRouteCacheMissReason.LayoutSignatureChanged:
                    RouteCacheLayoutMisses++;
                    break;
                case PackageGraphEdgeRouteCacheMissReason.EndpointGeometryChanged:
                    RouteCacheEndpointMisses++;
                    break;
                case PackageGraphEdgeRouteCacheMissReason.FocusGraphChanged:
                    RouteCacheFocusGraphMisses++;
                    break;
                case PackageGraphEdgeRouteCacheMissReason.RouteStyleOptionsChanged:
                    RouteCacheStyleMisses++;
                    break;
                default:
                    RouteCacheNoEntryMisses++;
                    break;
            }
        }

        public void AddRouteCalculationTicks(long ticks)
        {
            RouteCalculationTicks += Math.Max(0L, ticks);
        }

        public void AddGeometryLayoutReadTicks(long ticks)
        {
            GeometryLayoutReadTicks += Math.Max(0L, ticks);
        }

        public void AddVisualElementReuseTicks(long ticks)
        {
            VisualElementReuseTicks += Math.Max(0L, ticks);
        }

        public void AddStyleClassUpdateTicks(long ticks)
        {
            StyleClassUpdateTicks += Math.Max(0L, ticks);
        }

        public void AddPainterPassTicks(long ticks)
        {
            PainterPassTicks += Math.Max(0L, ticks);
        }
    }
}
