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
    internal static class PackageGraphCanvasVisuals
    {
        internal static void SetGraphActionButtonsInteractive(VisualElement root, bool interactive)
        {
            if (root == null)
            {
                return;
            }

            foreach (VisualElement child in root.Children())
            {
                if (child is Button button &&
                    button.ClassListContains("dpi-graph-node__action"))
                {
                    button.SetEnabled(interactive);
                    button.pickingMode = interactive ? PickingMode.Position : PickingMode.Ignore;
                    button.style.opacity = interactive ? 1f : 0f;
                }

                SetGraphActionButtonsInteractive(child, interactive);
            }
        }

        internal static void ApplySearchClasses(
            VisualElement element,
            bool directMatch,
            bool owningCategory,
            bool contextMatch,
            bool searchActive)
        {
            if (element == null)
            {
                return;
            }

            element.EnableInClassList("dpi-graph-search--active", searchActive);
            element.EnableInClassList("dpi-graph-search--match", searchActive && directMatch);
            element.EnableInClassList(
                "dpi-graph-search--owner",
                searchActive && !directMatch && owningCategory);
            element.EnableInClassList(
                "dpi-graph-search--context",
                searchActive && !directMatch && !owningCategory && contextMatch);
            element.EnableInClassList(
                "dpi-graph-search--dimmed",
                searchActive && !directMatch && !owningCategory && !contextMatch);
        }

        internal static void SetElementRect(VisualElement element, Rect rect)
        {
            if (element == null)
            {
                return;
            }

            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.width = rect.width;
            element.style.height = rect.height;
        }

        internal static void SetElementVisualState(
            VisualElement element,
            PackageGraphNodeVisualState state)
        {
            if (element == null)
            {
                return;
            }

            SetElementRect(element, state.Rect);
            element.style.opacity = state.Opacity * ResolvePackageElementOpacity(element);
            element.style.scale = new Scale(new Vector3(state.Scale, state.Scale, 1f));
            element.style.transformOrigin = new TransformOrigin(
                new Length(50f, LengthUnit.Percent),
                new Length(50f, LengthUnit.Percent),
                0f);
            element.pickingMode = state.Visible && state.Opacity > 0.75f
                ? PickingMode.Position
                : PickingMode.Ignore;
        }

        internal static float ResolveRootHubOpacity(VisualElement element)
        {
            return HasClass(element, "dpi-graph-hub--focus") ? 0.52f : 1f;
        }

        internal static float ResolveGroupElementOpacity(VisualElement element)
        {
            if (HasClass(element, "dpi-graph-search--dimmed"))
            {
                return HasClass(element, "dpi-graph-group--hover-context") ? 0.30f : 0.22f;
            }

            if (HasClass(element, "dpi-graph-search--context"))
            {
                return 0.64f;
            }

            if (HasClass(element, "dpi-graph-search--owner"))
            {
                return 1f;
            }

            if (HasClass(element, "dpi-graph-group--hover-dimmed"))
            {
                return 0.50f;
            }

            if (HasClass(element, "dpi-graph-group--empty"))
            {
                return 0.42f;
            }

            return HasClass(element, "dpi-graph-group--locked") ? 0.70f : 1f;
        }

        internal static float ResolvePackageElementOpacity(VisualElement element)
        {
            if (HasClass(element, "dpi-graph-search--dimmed"))
            {
                return HasClass(element, "dpi-graph-node--hover-context") ? 0.34f : 0.24f;
            }

            if (HasClass(element, "dpi-graph-search--context"))
            {
                return 0.68f;
            }

            if (HasClass(element, "dpi-graph-search--owner"))
            {
                return 1f;
            }

            if (HasClass(element, "dpi-graph-node--locked"))
            {
                return 0.72f;
            }

            if (HasClass(element, "dpi-graph-node--stack"))
            {
                return 0.34f;
            }

            if (HasClass(element, "dpi-graph-node--dimmed"))
            {
                return 0.42f;
            }

            return HasClass(element, "dpi-graph-node--hover-dimmed") ? 0.54f : 1f;
        }

        internal static bool HasClass(VisualElement element, string className)
        {
            return element != null && element.ClassListContains(className);
        }

        internal static void StretchToCanvas(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.top = 0f;
            element.style.width = PackageGraphLayout.CanvasWidth;
            element.style.height = PackageGraphLayout.CanvasHeight;
        }
    }
}
