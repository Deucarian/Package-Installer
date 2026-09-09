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
    internal static class PackageGraphRouteChannels
    {
        internal static float[] LimitRouteChannels(float[] channels, float pivot)
        {
            if (channels == null || channels.Length <= PackageGraphRouteMetrics.MaxRouteChannelCount)
            {
                return channels ?? Array.Empty<float>();
            }

            List<float> limited = new List<float>(channels);
            limited.Sort((first, second) =>
            {
                int comparison = Mathf.Abs(first - pivot).CompareTo(Mathf.Abs(second - pivot));
                return comparison != 0 ? comparison : first.CompareTo(second);
            });
            limited.RemoveRange(PackageGraphRouteMetrics.MaxRouteChannelCount, limited.Count - PackageGraphRouteMetrics.MaxRouteChannelCount);
            limited.Sort();
            return limited.ToArray();
        }

        internal static IEnumerable<float> GetCandidateXChannels(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles)
        {
            return GetCandidateXChannels(from, to, obstacles, default(PackageGraphConnectionBundle));
        }

        internal static float[] GetCandidateXChannels(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphConnectionBundle bundle)
        {
            List<float> channels = new List<float>
            {
                (from.x + to.x) * 0.5f,
                from.x - 86f,
                from.x + 86f,
                from.x - 220f,
                from.x + 220f,
                to.x - 86f,
                to.x + 86f,
                to.x - 220f,
                to.x + 220f,
                Mathf.Min(from.x, to.x) - 520f,
                Mathf.Max(from.x, to.x) + 520f
            };
            float min = Mathf.Min(from.x, to.x) - 760f;
            float max = Mathf.Max(from.x, to.x) + 760f;

            foreach (PackageGraphRouteObstacle obstacle in obstacles ?? Array.Empty<PackageGraphRouteObstacle>())
            {
                if (PackageGraphRouteObstacles.ShouldIgnoreObstacleForBundle(obstacle, bundle))
                {
                    continue;
                }

                channels.Add(obstacle.Rect.xMin - PackageGraphRouteMetrics.RouteObstacleMargin);
                channels.Add(obstacle.Rect.xMax + PackageGraphRouteMetrics.RouteObstacleMargin);
            }

            channels.Sort();
            List<float> unique = new List<float>(channels.Count);

            foreach (float value in channels)
            {
                if (value < min || value > max)
                {
                    continue;
                }

                if (unique.Count == 0 || Mathf.Abs(unique[unique.Count - 1] - value) > 1f)
                {
                    unique.Add(value);
                }
            }

            return unique.ToArray();
        }

        internal static IEnumerable<float> GetCandidateYChannels(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles)
        {
            return GetCandidateYChannels(from, to, obstacles, default(PackageGraphConnectionBundle));
        }

        internal static float[] GetCandidateYChannels(
            Vector2 from,
            Vector2 to,
            IReadOnlyList<PackageGraphRouteObstacle> obstacles,
            PackageGraphConnectionBundle bundle)
        {
            List<float> channels = new List<float>
            {
                (from.y + to.y) * 0.5f,
                from.y - 86f,
                from.y + 86f,
                from.y - 220f,
                from.y + 220f,
                to.y - 86f,
                to.y + 86f,
                to.y - 220f,
                to.y + 220f,
                Mathf.Min(from.y, to.y) - 520f,
                Mathf.Max(from.y, to.y) + 520f
            };
            float min = Mathf.Min(from.y, to.y) - 760f;
            float max = Mathf.Max(from.y, to.y) + 760f;

            foreach (PackageGraphRouteObstacle obstacle in obstacles ?? Array.Empty<PackageGraphRouteObstacle>())
            {
                if (PackageGraphRouteObstacles.ShouldIgnoreObstacleForBundle(obstacle, bundle))
                {
                    continue;
                }

                channels.Add(obstacle.Rect.yMin - PackageGraphRouteMetrics.RouteObstacleMargin);
                channels.Add(obstacle.Rect.yMax + PackageGraphRouteMetrics.RouteObstacleMargin);
            }

            channels.Sort();
            List<float> unique = new List<float>(channels.Count);

            foreach (float value in channels)
            {
                if (value < min || value > max)
                {
                    continue;
                }

                if (unique.Count == 0 || Mathf.Abs(unique[unique.Count - 1] - value) > 1f)
                {
                    unique.Add(value);
                }
            }

            return unique.ToArray();
        }
    }
}
