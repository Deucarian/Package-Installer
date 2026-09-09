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
    internal static class PackageGraphToolbarControls
    {
        internal static Button CreateBreadcrumbButton(
            string text,
            string iconId,
            Action clicked)
        {
            Button button = DeucarianEditorIconTextButton.Create(
                iconId,
                text,
                clicked,
                "Navigate to " + text,
                leading: true);
            button.AddToClassList("dpi-ecosystem-graph__breadcrumb");
            return button;
        }

        internal static VisualElement CreateBreadcrumbCurrent(
            string text,
            string iconId,
            bool packageIcon = false)
        {
            VisualElement current = DeucarianEditorIconTextButton.CreateContent(
                iconId,
                text,
                leading: true);
            if (packageIcon)
            {
                Image icon = current.Q<Image>(
                    className: DeucarianEditorIconTextButton.IconClass);
                if (icon != null)
                {
                    icon.image = DeucarianEditorIcons.GetPackageIcon(iconId);
                }
            }
            current.AddToClassList("dpi-ecosystem-graph__breadcrumb-current");
            return current;
        }

        internal static Image CreateBreadcrumbSeparator()
        {
            Image separator = CreateIconImage(
                DeucarianEditorIconIds.ChevronRight,
                "dpi-ecosystem-graph__breadcrumb-separator");
            return separator;
        }

        internal static Button CreateFilterToggleButton(Action action, string iconClass)
        {
            Button button = new Button(action);
            button.AddToClassList("dpi-ecosystem-graph__filter-toggle");
            button.AddToClassList("dpi-ecosystem-graph__filter-toggle--" + iconClass);
            return button;
        }

        internal static void UpdateFilterToggleButton(
            Button button,
            bool active,
            string label,
            int count,
            string iconClass,
            string iconId)
        {
            if (button == null)
            {
                return;
            }

            button.text = string.Empty;
            button.Clear();
            button.Add(CreateFilterIcon(iconClass, iconId));
            button.Add(CreateFilterLabel(label));
            button.Add(CreateFilterCount(count));
            button.tooltip = (active ? "Hide " : "Show ") +
                             label.ToLowerInvariant() +
                             " packages. " +
                             count +
                             " match the active search.";
            button.EnableInClassList("dpi-ecosystem-graph__filter-toggle--active", active);
            button.EnableInClassList("dpi-ecosystem-graph__filter-toggle--inactive", !active);
        }

        internal static Image CreateFilterIcon(string iconClass, string iconId)
        {
            Image icon = CreateIconImage(iconId, "dpi-ecosystem-graph__filter-icon");
            icon.AddToClassList("dpi-ecosystem-graph__filter-icon--" + iconClass);
            return icon;
        }

        internal static Label CreateFilterLabel(string label)
        {
            Label text = new Label(label);
            text.pickingMode = PickingMode.Ignore;
            text.AddToClassList("dpi-ecosystem-graph__filter-label");
            return text;
        }

        internal static Label CreateFilterCount(int count)
        {
            Label text = new Label(count.ToString());
            text.pickingMode = PickingMode.Ignore;
            text.AddToClassList("dpi-ecosystem-graph__filter-count");
            return text;
        }

        internal static Image CreateIconImage(string iconId, string className = null)
        {
            Image image = new Image
            {
                image = DeucarianEditorIcons.GetIcon(iconId),
                scaleMode = ScaleMode.ScaleToFit,
                tintColor = DeucarianEditorTheme.Text,
                pickingMode = PickingMode.Ignore
            };

            if (!string.IsNullOrWhiteSpace(className))
            {
                image.AddToClassList(className);
            }

            return image;
        }

        internal static VisualElement CreateLegendItem(
            string iconId,
            string label,
            string markerClass,
            string tooltip = null)
        {
            VisualElement item = new VisualElement();
            item.AddToClassList("dpi-graph-legend__item");
            item.tooltip = tooltip ?? label;

            Image marker = CreateIconImage(iconId, "dpi-graph-legend__line");
            marker.tintColor = GetLegendTint(markerClass);
            marker.AddToClassList(markerClass);
            item.Add(marker);

            Label labelElement = new Label(label);
            labelElement.AddToClassList("dpi-graph-legend__label");
            item.Add(labelElement);

            return item;
        }

        internal static Color GetLegendTint(string markerClass)
        {
            if (string.Equals(markerClass, "dpi-graph-legend__line--installed", StringComparison.Ordinal))
            {
                return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Installed, 0.95f);
            }

            if (string.Equals(markerClass, "dpi-graph-legend__line--available", StringComparison.Ordinal))
            {
                return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Available, 0.94f);
            }

            if (string.Equals(markerClass, "dpi-graph-legend__line--warning", StringComparison.Ordinal))
            {
                return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Update, 0.98f);
            }

            if (string.Equals(markerClass, "dpi-graph-legend__line--integration", StringComparison.Ordinal))
            {
                return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorPalette.Tideline, 0.96f);
            }

            return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Unknown, 0.86f);
        }

        internal static Button CreateToolbarButton(string iconId, string label, Action action)
        {
            Button button = DeucarianEditorIconTextButton.Create(
                iconId,
                label,
                action,
                label);
            button.AddToClassList("dpi-ecosystem-graph__toolbar-button");
            return button;
        }
    }
}
