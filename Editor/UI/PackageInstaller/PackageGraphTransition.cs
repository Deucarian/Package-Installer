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

        public static PackageGraphCameraState EvaluateAnchoredCameraFromAnimatedAnchor(
            PackageGraphCameraState sourceCamera,
            PackageGraphCameraState targetCamera,
            Vector2 animatedAnchorWorld,
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
            Vector2 anchorScreen = Vector2.Lerp(sourceAnchorScreen, targetAnchorScreen, eased);
            return new PackageGraphCameraState(anchorScreen - animatedAnchorWorld * zoom, zoom);
        }

        public static float SmoothStep(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            return t * t * (3f - 2f * t);
        }
    }

    internal readonly struct PackageGraphHierarchyExitResult
    {
        public PackageGraphHierarchyExitResult(
            bool consumed,
            bool committed,
            bool cancelled,
            float progress)
        {
            Consumed = consumed;
            Committed = committed;
            Cancelled = cancelled;
            Progress = Mathf.Clamp01(progress);
        }

        public bool Consumed { get; }

        public bool Committed { get; }

        public bool Cancelled { get; }

        public float Progress { get; }
    }

    internal sealed class PackageGraphHierarchyExitController
    {
        public const float CommitThreshold = 1f;
        public const float ExitProgressPerWheelNotch = 0.22f;
        public const float ReverseProgressMultiplier = 1.35f;
        public const float WheelNotchDelta = 3f;
        public const double CommitCooldownSeconds = 0.38;

        private double _lastCommitTime = double.NegativeInfinity;
        private float _progress;

        public float Progress => _progress;

        public bool IsActive => _progress > 0.001f;

        public PackageGraphHierarchyExitResult ApplyWheel(
            float wheelDeltaY,
            bool canExit,
            bool atNormalMinimum,
            double currentTime)
        {
            if (Mathf.Abs(wheelDeltaY) <= 0.01f)
            {
                return new PackageGraphHierarchyExitResult(false, false, false, _progress);
            }

            if (!canExit)
            {
                Cancel();
                return new PackageGraphHierarchyExitResult(false, false, false, _progress);
            }

            if (currentTime - _lastCommitTime < CommitCooldownSeconds)
            {
                return new PackageGraphHierarchyExitResult(IsActive, false, false, _progress);
            }

            if (wheelDeltaY > 0f)
            {
                if (!atNormalMinimum && !IsActive)
                {
                    return new PackageGraphHierarchyExitResult(false, false, false, _progress);
                }

                _progress = Mathf.Clamp01(_progress + NormalizeWheelDelta(wheelDeltaY));

                if (_progress >= CommitThreshold)
                {
                    _progress = CommitThreshold;
                    _lastCommitTime = currentTime;
                    return new PackageGraphHierarchyExitResult(true, true, false, _progress);
                }

                return new PackageGraphHierarchyExitResult(true, false, false, _progress);
            }

            if (!IsActive)
            {
                return new PackageGraphHierarchyExitResult(false, false, false, _progress);
            }

            _progress = Mathf.Clamp01(
                _progress - NormalizeWheelDelta(-wheelDeltaY) * ReverseProgressMultiplier);

            if (_progress <= 0.001f)
            {
                _progress = 0f;
                return new PackageGraphHierarchyExitResult(true, false, true, _progress);
            }

            return new PackageGraphHierarchyExitResult(true, false, false, _progress);
        }

        public void Cancel()
        {
            _progress = 0f;
        }

        public void Commit(double currentTime)
        {
            _progress = 0f;
            _lastCommitTime = currentTime;
        }

        public static float NormalizeWheelDelta(float absoluteWheelDeltaY)
        {
            return Mathf.Clamp(absoluteWheelDeltaY / WheelNotchDelta, 0f, 1f) *
                   ExitProgressPerWheelNotch;
        }
    }

    internal sealed class PackageGraphHierarchyExitWheelEvent
    {
        public PackageGraphHierarchyExitWheelEvent(
            float wheelDeltaY,
            Vector2 viewportMousePosition,
            bool atNormalMinimum)
        {
            WheelDeltaY = wheelDeltaY;
            ViewportMousePosition = viewportMousePosition;
            AtNormalMinimum = atNormalMinimum;
        }

        public float WheelDeltaY { get; }

        public Vector2 ViewportMousePosition { get; }

        public bool AtNormalMinimum { get; }
    }

    internal sealed class PackageGraphHierarchyEnterWheelEvent
    {
        public PackageGraphHierarchyEnterWheelEvent(
            float wheelDeltaY,
            Vector2 viewportMousePosition,
            bool atNormalMaximum)
        {
            WheelDeltaY = wheelDeltaY;
            ViewportMousePosition = viewportMousePosition;
            AtNormalMaximum = atNormalMaximum;
        }

        public float WheelDeltaY { get; }

        public Vector2 ViewportMousePosition { get; }

        public bool AtNormalMaximum { get; }
    }
}
