using System;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageGraphTransitionAnchorKind
    {
        Root,
        Group,
        Package
    }

    internal readonly struct PackageGraphTransitionAnchor : IEquatable<PackageGraphTransitionAnchor>
    {
        public PackageGraphTransitionAnchor(PackageGraphTransitionAnchorKind kind, string id)
        {
            Kind = kind;
            Id = id ?? string.Empty;
        }

        public PackageGraphTransitionAnchorKind Kind { get; }

        public string Id { get; }

        public string Key => Kind + ":" + Id;

        public static PackageGraphTransitionAnchor Root =>
            new PackageGraphTransitionAnchor(PackageGraphTransitionAnchorKind.Root, string.Empty);

        public bool Equals(PackageGraphTransitionAnchor other)
        {
            return Kind == other.Kind &&
                   string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is PackageGraphTransitionAnchor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^
                       StringComparer.OrdinalIgnoreCase.GetHashCode(Id ?? string.Empty);
            }
        }
    }

    internal readonly struct PackageGraphCameraState
    {
        public PackageGraphCameraState(Vector2 pan, float zoom)
        {
            Pan = pan;
            Zoom = zoom;
        }

        public Vector2 Pan { get; }

        public float Zoom { get; }

        public Vector2 WorldToViewport(Vector2 worldPoint)
        {
            return worldPoint * Zoom + Pan;
        }
    }

    internal static class PackageGraphTransition
    {
        public const float DefaultDurationSeconds = 0.28f;

        public static PackageGraphCameraState EvaluateAnchoredCamera(
            PackageGraphCameraState sourceCamera,
            PackageGraphCameraState targetCamera,
            Vector2 sourceAnchorWorld,
            Vector2 targetAnchorWorld,
            Vector2 sourceAnchorScreen,
            Vector2 targetAnchorScreen,
            float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);

            if (t <= 0f)
            {
                return sourceCamera;
            }

            if (t >= 1f)
            {
                return targetCamera;
            }

            float eased = SmoothStep(t);
            float zoom = Mathf.Lerp(sourceCamera.Zoom, targetCamera.Zoom, eased);
            Vector2 anchorWorld = Vector2.Lerp(sourceAnchorWorld, targetAnchorWorld, eased);
            Vector2 anchorScreen = Vector2.Lerp(sourceAnchorScreen, targetAnchorScreen, eased);
            return new PackageGraphCameraState(anchorScreen - anchorWorld * zoom, zoom);
        }

        public static float SmoothStep(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            return t * t * (3f - 2f * t);
        }
    }
}
