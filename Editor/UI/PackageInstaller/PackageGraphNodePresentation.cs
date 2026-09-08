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
    internal static class PackageGraphNodePresentation
    {
        internal static string GetVisualModeClass(PackageGraphNodeVisualMode visualMode)
        {
            switch (visualMode)
            {
                case PackageGraphNodeVisualMode.Overview:
                    return "overview";
                case PackageGraphNodeVisualMode.Stack:
                    return "stack";
                default:
                    return "focus";
            }
        }

        internal static string GetPresentationClass(PackageGraphNodePresentationLevel presentationLevel)
        {
            switch (presentationLevel)
            {
                case PackageGraphNodePresentationLevel.IconOnly:
                    return "icon-only";
                case PackageGraphNodePresentationLevel.Micro:
                    return "micro";
                case PackageGraphNodePresentationLevel.Compact:
                    return "compact";
                default:
                    return "full";
            }
        }

        internal static bool IsGraphShortcutAction(PackageGraphNodeAction action)
        {
            switch (action)
            {
                case PackageGraphNodeAction.Install:
                case PackageGraphNodeAction.Update:
                case PackageGraphNodeAction.Reinstall:
                    return true;
                default:
                    return false;
            }
        }

        internal static string GetNodeClass(PackageGraphNodeType nodeType)
        {
            switch (nodeType)
            {
                case PackageGraphNodeType.Tool:
                    return "tool";
                case PackageGraphNodeType.Companion:
                    return "companion";
                case PackageGraphNodeType.Suite:
                    return "suite";
                case PackageGraphNodeType.Integration:
                    return "integration";
                case PackageGraphNodeType.Template:
                    return "core";
                default:
                    return "core";
            }
        }

        internal static string GetRingClass(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return "runtime";
                case PackageGraphLayoutRing.Integration:
                    return "integration";
                case PackageGraphLayoutRing.Suite:
                    return "suite";
                default:
                    return "infrastructure";
            }
        }

        internal static string GetNodeTypeLabel(PackageGraphNodeType nodeType)
        {
            switch (nodeType)
            {
                case PackageGraphNodeType.Tool:
                    return "Tool";
                case PackageGraphNodeType.Companion:
                    return "Library";
                case PackageGraphNodeType.Suite:
                    return "Suite";
                case PackageGraphNodeType.Integration:
                    return "Integration";
                case PackageGraphNodeType.Template:
                    return "Template";
                default:
                    return "Library";
            }
        }

        internal static string GetStatusLabel(PackageGraphNode node)
        {
            switch (node.Status)
            {
                case PackageGraphNodeStatus.Missing:
                    return "Missing dependency";
                case PackageGraphNodeStatus.NotInstalled:
                    return "Not installed";
                case PackageGraphNodeStatus.UpdateAvailable:
                    return "Update available";
                case PackageGraphNodeStatus.Checking:
                    return "Checking";
                case PackageGraphNodeStatus.Warning:
                    return string.IsNullOrWhiteSpace(node.UpdateStatusLabel)
                        ? "Attention"
                        : node.UpdateStatusLabel;
                default:
                    return "Installed";
            }
        }

        internal static string GetStatusIcon(PackageGraphNodeStatus status)
        {
            switch (status)
            {
                case PackageGraphNodeStatus.Missing:
                    return DeucarianEditorIconIds.MissingPackage;
                case PackageGraphNodeStatus.NotInstalled:
                    return DeucarianEditorIconIds.Available;
                case PackageGraphNodeStatus.UpdateAvailable:
                    return DeucarianEditorIconIds.Update;
                case PackageGraphNodeStatus.Checking:
                    return DeucarianEditorIconIds.Busy;
                case PackageGraphNodeStatus.Warning:
                    return DeucarianEditorIconIds.Warning;
                default:
                    return DeucarianEditorIconIds.Success;
            }
        }

        internal static Color GetStatusColor(PackageGraphNodeStatus status)
        {
            switch (status)
            {
                case PackageGraphNodeStatus.Missing:
                    return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Missing, 0.98f);
                case PackageGraphNodeStatus.NotInstalled:
                    return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Available, 0.94f);
                case PackageGraphNodeStatus.UpdateAvailable:
                    return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Update, 0.98f);
                case PackageGraphNodeStatus.Checking:
                    return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Checking, 0.98f);
                case PackageGraphNodeStatus.Warning:
                    return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Warning, 0.98f);
                default:
                    return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Installed, 0.98f);
            }
        }

        internal static string GetActionIcon(PackageGraphNodeAction action)
        {
            switch (action)
            {
                case PackageGraphNodeAction.Install:
                    return DeucarianEditorIconIds.CreatePackage;
                case PackageGraphNodeAction.Update:
                    return DeucarianEditorIconIds.Update;
                case PackageGraphNodeAction.Reinstall:
                    return DeucarianEditorIconIds.Reset;
                default:
                    return DeucarianEditorIconIds.Package;
            }
        }

        internal static string GetStatusClass(PackageGraphNodeStatus status)
        {
            switch (status)
            {
                case PackageGraphNodeStatus.Missing:
                    return "missing";
                case PackageGraphNodeStatus.NotInstalled:
                    return "available";
                case PackageGraphNodeStatus.UpdateAvailable:
                    return "update";
                case PackageGraphNodeStatus.Checking:
                    return "checking";
                case PackageGraphNodeStatus.Warning:
                    return "warning";
                default:
                    return "installed";
            }
        }

        internal static string GetStatusBadgeClass(PackageGraphNodeStatus status)
        {
            return "dpi-graph-node__badge--" + GetStatusClass(status);
        }

        internal static string GetCompactTooltip(PackageGraphNode node, string relationshipTooltip)
        {
            string status = string.IsNullOrWhiteSpace(node.UpdateStatusLabel)
                ? GetStatusLabel(node)
                : node.UpdateStatusLabel;
            StringBuilder tooltip = new StringBuilder()
                .Append(node.DisplayName)
                .Append("\nStatus: ")
                .Append(status);

            if (!string.IsNullOrWhiteSpace(relationshipTooltip))
            {
                tooltip.Append('\n').Append(relationshipTooltip);
            }

            if (!node.IsRegistered && !string.IsNullOrWhiteSpace(node.Description))
            {
                tooltip.Append("\nDiagnostic: ").Append(node.Description.Trim());
            }

            return tooltip.ToString();
        }
    }
}
