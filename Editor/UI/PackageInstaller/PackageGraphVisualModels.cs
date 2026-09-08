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
    internal sealed class PackageGraphContextMenuRequest
    {
        public PackageGraphContextMenuRequest(VisualElement target, Vector2 viewportPosition, Vector2 worldPosition)
        {
            Target = target;
            ViewportPosition = viewportPosition;
            WorldPosition = worldPosition;
        }

        public VisualElement Target { get; }

        public Vector2 ViewportPosition { get; }

        public Vector2 WorldPosition { get; }
    }

    internal enum PackageGraphSpotlightKind
    {
        None,
        Root,
        Category,
        Package,
        Attention
    }

    internal readonly struct PackageGraphNodeVisualState
    {
        public PackageGraphNodeVisualState(
            Rect rect,
            float opacity,
            float scale,
            PackageGraphNodePresentationLevel presentationLevel,
            bool visible,
            bool entering,
            bool leaving)
        {
            Rect = rect;
            Opacity = Mathf.Clamp01(opacity);
            Scale = Mathf.Max(0.01f, scale);
            PresentationLevel = presentationLevel;
            Visible = visible;
            Entering = entering;
            Leaving = leaving;
        }

        public Rect Rect { get; }

        public float Opacity { get; }

        public float Scale { get; }

        public PackageGraphNodePresentationLevel PresentationLevel { get; }

        public bool Visible { get; }

        public bool Entering { get; }

        public bool Leaving { get; }

        public static PackageGraphNodeVisualState Stable(
            Rect rect,
            PackageGraphNodePresentationLevel presentationLevel)
        {
            return new PackageGraphNodeVisualState(rect, 1f, 1f, presentationLevel, true, false, false);
        }

        public PackageGraphNodeVisualState WithRect(Rect rect)
        {
            return new PackageGraphNodeVisualState(
                rect,
                Opacity,
                Scale,
                PresentationLevel,
                Visible,
                Entering,
                Leaving);
        }
    }

    internal readonly struct PackageGraphOrbitVisualState
    {
        public PackageGraphOrbitVisualState(
            string orbitId,
            Vector2 center,
            float radius,
            float fillOpacity,
            float strokeOpacity,
            bool emphasized,
            bool muted,
            bool visible,
            bool empty)
        {
            OrbitId = orbitId ?? string.Empty;
            Center = center;
            Radius = Mathf.Max(0f, radius);
            FillOpacity = Mathf.Clamp01(fillOpacity);
            StrokeOpacity = Mathf.Clamp01(strokeOpacity);
            Emphasized = emphasized;
            Muted = muted;
            Visible = visible;
            Empty = empty;
        }

        public string OrbitId { get; }

        public Vector2 Center { get; }

        public float Radius { get; }

        public float FillOpacity { get; }

        public float StrokeOpacity { get; }

        public bool Emphasized { get; }

        public bool Muted { get; }

        public bool Visible { get; }

        public bool Empty { get; }
    }

    internal enum PackageGraphNodeVisualMode
    {
        Overview,
        Focus,
        Stack
    }
}
