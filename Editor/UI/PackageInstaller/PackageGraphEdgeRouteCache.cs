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
    internal enum PackageGraphEdgeRouteCacheMissReason
    {
        None,
        NoExistingEntry,
        LayoutSignatureChanged,
        EndpointGeometryChanged,
        FocusGraphChanged,
        RouteStyleOptionsChanged
    }

    internal readonly struct PackageGraphEdgeRouteCacheKey
    {
        public PackageGraphEdgeRouteCacheKey(
            string identityKey,
            string focusGraphKey,
            string layoutSignature,
            string endpointGeometryKey,
            string routeStyleKey)
        {
            IdentityKey = identityKey ?? string.Empty;
            FocusGraphKey = focusGraphKey ?? string.Empty;
            LayoutSignature = layoutSignature ?? string.Empty;
            EndpointGeometryKey = endpointGeometryKey ?? string.Empty;
            RouteStyleKey = routeStyleKey ?? string.Empty;
            FullKey =
                IdentityKey +
                "|fg=" + FocusGraphKey +
                "|layout=" + LayoutSignature +
                "|endpoints=" + EndpointGeometryKey +
                "|style=" + RouteStyleKey;
        }

        public string FullKey { get; }

        public string IdentityKey { get; }

        public string FocusGraphKey { get; }

        public string LayoutSignature { get; }

        public string EndpointGeometryKey { get; }

        public string RouteStyleKey { get; }
    }

    internal sealed class PackageGraphEdgeRouteCache
    {
        private const int MaxCachedRoutes = 512;

        private readonly Dictionary<string, CachedPackageGraphEdgeRoute> _routes =
            new Dictionary<string, CachedPackageGraphEdgeRoute>(StringComparer.Ordinal);
        private readonly Dictionary<string, PackageGraphEdgeRouteCacheKey> _lastKeyByIdentity =
            new Dictionary<string, PackageGraphEdgeRouteCacheKey>(StringComparer.Ordinal);

        public int Count => _routes.Count;

        public bool TryGet(
            PackageGraphEdgeRouteCacheKey key,
            PackageGraphConnectionBundle bundle,
            out PackageGraphEdgeRoute route,
            out PackageGraphEdgeRouteCacheMissReason missReason)
        {
            if (!string.IsNullOrEmpty(key.FullKey) &&
                _routes.TryGetValue(key.FullKey, out CachedPackageGraphEdgeRoute cachedRoute))
            {
                route = cachedRoute.CreateRoute(bundle);
                missReason = PackageGraphEdgeRouteCacheMissReason.None;
                return true;
            }

            route = default(PackageGraphEdgeRoute);
            missReason = GetMissReason(key);
            return false;
        }

        public void Store(PackageGraphEdgeRouteCacheKey key, PackageGraphEdgeRoute route)
        {
            if (string.IsNullOrEmpty(key.FullKey) ||
                string.IsNullOrEmpty(key.IdentityKey) ||
                route.Points == null ||
                route.Points.Count < 2)
            {
                return;
            }

            // Cache invalidation is encoded in the stable key parts: focus graph, target layout,
            // endpoint geometry, and route style. Transient animation state intentionally stays out.
            if (_routes.Count >= MaxCachedRoutes)
            {
                _routes.Clear();
                _lastKeyByIdentity.Clear();
            }

            _routes[key.FullKey] = new CachedPackageGraphEdgeRoute(route);
            _lastKeyByIdentity[key.IdentityKey] = key;
        }

        private PackageGraphEdgeRouteCacheMissReason GetMissReason(PackageGraphEdgeRouteCacheKey key)
        {
            if (string.IsNullOrEmpty(key.IdentityKey) ||
                !_lastKeyByIdentity.TryGetValue(key.IdentityKey, out PackageGraphEdgeRouteCacheKey previousKey))
            {
                return PackageGraphEdgeRouteCacheMissReason.NoExistingEntry;
            }

            if (!string.Equals(previousKey.FocusGraphKey, key.FocusGraphKey, StringComparison.Ordinal))
            {
                return PackageGraphEdgeRouteCacheMissReason.FocusGraphChanged;
            }

            if (!string.Equals(previousKey.LayoutSignature, key.LayoutSignature, StringComparison.Ordinal))
            {
                return PackageGraphEdgeRouteCacheMissReason.LayoutSignatureChanged;
            }

            if (!string.Equals(previousKey.EndpointGeometryKey, key.EndpointGeometryKey, StringComparison.Ordinal))
            {
                return PackageGraphEdgeRouteCacheMissReason.EndpointGeometryChanged;
            }

            if (!string.Equals(previousKey.RouteStyleKey, key.RouteStyleKey, StringComparison.Ordinal))
            {
                return PackageGraphEdgeRouteCacheMissReason.RouteStyleOptionsChanged;
            }

            return PackageGraphEdgeRouteCacheMissReason.NoExistingEntry;
        }

        private readonly struct CachedPackageGraphEdgeRoute
        {
            private readonly PackageGraphEdgeRoutePort _sourcePort;
            private readonly PackageGraphEdgeRoutePort _targetPort;
            private readonly PackageGraphEdgeRouteZone _zone;
            private readonly string _sharedTrunkId;
            private readonly int _branchIndex;
            private readonly int _branchCount;
            private readonly Vector2[] _points;

            public CachedPackageGraphEdgeRoute(PackageGraphEdgeRoute route)
            {
                _sourcePort = route.SourcePort;
                _targetPort = route.TargetPort;
                _zone = route.Zone;
                _sharedTrunkId = route.SharedTrunkId;
                _branchIndex = route.BranchIndex;
                _branchCount = route.BranchCount;
                _points = route.Points.ToArray();
            }

            public PackageGraphEdgeRoute CreateRoute(PackageGraphConnectionBundle bundle)
            {
                return new PackageGraphEdgeRoute(
                    bundle,
                    _sourcePort,
                    _targetPort,
                    _zone,
                    _sharedTrunkId,
                    _branchIndex,
                    _branchCount,
                    _points);
            }
        }
    }
}
