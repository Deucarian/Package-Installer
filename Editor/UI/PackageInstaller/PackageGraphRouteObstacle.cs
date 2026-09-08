using System;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal readonly struct PackageGraphRouteObstacle
    {
        public PackageGraphRouteObstacle(string id, Rect rect)
        {
            Id = id ?? string.Empty;
            Rect = rect;
        }

        public string Id { get; }

        public Rect Rect { get; }
    }
}
