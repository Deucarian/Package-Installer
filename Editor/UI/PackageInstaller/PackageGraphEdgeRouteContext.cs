using System;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal readonly struct PackageGraphEdgeRouteContext
    {
        public PackageGraphEdgeRouteContext(
            PackageGraphConnectionBundle bundle,
            Rect fromRect,
            Rect toRect,
            PackageGraphEdgeRouteZone zone)
        {
            Bundle = bundle;
            FromRect = fromRect;
            ToRect = toRect;
            Zone = zone;
        }

        public PackageGraphConnectionBundle Bundle { get; }

        public PackageGraphEdge Edge => Bundle.PrimaryEdge;

        public Rect FromRect { get; }

        public Rect ToRect { get; }

        public PackageGraphEdgeRouteZone Zone { get; }
    }
}
