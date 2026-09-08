using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphGeometrySnapshot
    {
        private readonly Dictionary<string, Rect> nodes = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> groups = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector2> centers = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> radii = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        internal IReadOnlyDictionary<string, Rect> Nodes => nodes;
        internal IReadOnlyDictionary<string, Rect> Groups => groups;
        internal IReadOnlyDictionary<string, Vector2> Centers => centers;
        internal IReadOnlyDictionary<string, float> Radii => radii;

        internal void Update(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects,
            IReadOnlyDictionary<string, Vector2> groupCenters = null,
            IReadOnlyDictionary<string, float> groupOrbitRadii = null)
        {
            Replace(nodes, nodeRects);
            Replace(groups, groupRects);
            Replace(centers, groupCenters);
            radii.Clear();
            if (groupOrbitRadii != null)
            {
                foreach (KeyValuePair<string, float> radius in groupOrbitRadii)
                {
                    radii[radius.Key] = Mathf.Max(0f, radius.Value);
                }
            }
        }

        private static void Replace<T>(Dictionary<string, T> target, IReadOnlyDictionary<string, T> source)
        {
            target.Clear();
            if (source == null)
            {
                return;
            }

            foreach (KeyValuePair<string, T> item in source)
            {
                target[item.Key] = item.Value;
            }
        }
    }
}
