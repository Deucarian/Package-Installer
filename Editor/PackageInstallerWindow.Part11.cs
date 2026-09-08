using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {

        private static void DrawSingleLineLabel(Rect rect, GUIContent content, GUIStyle style)
        {
            if (style == null)
            {
                GUI.Label(rect, content);
                return;
            }

            bool previousWordWrap = style.wordWrap;
            TextClipping previousClipping = style.clipping;

            try
            {
                style.wordWrap = false;
                style.clipping = TextClipping.Clip;
                GUI.Label(rect, content, style);
            }
            finally
            {
                style.wordWrap = previousWordWrap;
                style.clipping = previousClipping;
            }
        }

        private void SynchronizeDetailsNavigationHover(PackageGraphNavigationRow? hoveredRow)
        {
            if (_graphView == null || !ShouldSynchronizeDetailsHover(Event.current.type))
            {
                return;
            }

            PackageGraphNavigationTargetKind nextTargetKind = hoveredRow.HasValue
                ? hoveredRow.Value.TargetKind
                : PackageGraphNavigationTargetKind.Overview;
            string nextTargetId = hoveredRow.HasValue && !hoveredRow.Value.IsOverview
                ? hoveredRow.Value.Id
                : string.Empty;

            if (_detailsPreviewedGraphTargetKind == nextTargetKind &&
                string.Equals(
                    _detailsPreviewedGraphTargetId,
                    nextTargetId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ClearDetailsGraphHover();
            _detailsPreviewedGraphTargetKind = nextTargetKind;
            _detailsPreviewedGraphTargetId = nextTargetId;

            if (string.IsNullOrWhiteSpace(_detailsPreviewedGraphTargetId))
            {
                return;
            }

            if (_detailsPreviewedGraphTargetKind == PackageGraphNavigationTargetKind.Group)
            {
                _graphView.SetExternalGroupHover(_detailsPreviewedGraphTargetId);
            }
            else if (_detailsPreviewedGraphTargetKind == PackageGraphNavigationTargetKind.Package)
            {
                _graphView.SetExternalPackageHover(_detailsPreviewedGraphTargetId);
            }
        }

        private void ClearDetailsGraphHover()
        {
            if (_graphView == null || string.IsNullOrWhiteSpace(_detailsPreviewedGraphTargetId))
            {
                _detailsPreviewedGraphTargetKind = PackageGraphNavigationTargetKind.Overview;
                _detailsPreviewedGraphTargetId = string.Empty;
                return;
            }

            PackageGraphNavigationTargetKind previousTargetKind = _detailsPreviewedGraphTargetKind;
            string previousTargetId = _detailsPreviewedGraphTargetId;
            _detailsPreviewedGraphTargetKind = PackageGraphNavigationTargetKind.Overview;
            _detailsPreviewedGraphTargetId = string.Empty;

            if (previousTargetKind == PackageGraphNavigationTargetKind.Group)
            {
                _graphView.ClearExternalGroupHover(previousTargetId);
            }
            else if (previousTargetKind == PackageGraphNavigationTargetKind.Package)
            {
                _graphView.ClearExternalPackageHover(previousTargetId);
            }
        }

        private void ClearGraphHoverState()
        {
            _detailsPreviewedGraphTargetKind = PackageGraphNavigationTargetKind.Overview;
            _detailsPreviewedGraphTargetId = string.Empty;
            _graphView?.ClearHoverState();
        }

        private static bool ShouldSynchronizeDetailsHover(EventType eventType)
        {
            return eventType == EventType.Repaint ||
                   eventType == EventType.MouseMove ||
                   eventType == EventType.MouseDrag ||
                   eventType == EventType.MouseDown ||
                   eventType == EventType.MouseUp ||
                   eventType == EventType.MouseLeaveWindow;
        }

        private string GetActiveFilterSummary()
        {
            if (_visibilityFilterState == null || _visibilityFilterState.IsDefault)
            {
                return "All packages";
            }

            List<string> parts = new List<string>();

            if (!_visibilityFilterState.ShowInstalled)
            {
                parts.Add("Installed hidden");
            }

            if (!_visibilityFilterState.ShowNotInstalled)
            {
                parts.Add("Not installed hidden");
            }

            if (!string.IsNullOrWhiteSpace(_visibilityFilterState.SearchText))
            {
                parts.Add("Search: " + _visibilityFilterState.SearchText);
            }

            return parts.Count == 0 ? "All packages" : string.Join(", ", parts.ToArray());
        }

        private void DrawPackageDetails(PackageDefinition packageDefinition)
        {
            DrawDetailHeader(packageDefinition);
            DrawStatusPanel(packageDefinition);
            DrawRequirementsPanel(packageDefinition);
            DrawChannelPanel(packageDefinition);
            if (packageDefinition.IsTemplate && packageDefinition.CompositionPresets.Count > 0)
            {
                DrawTemplateCompositionPanel(packageDefinition);
            }

            DrawActionsPanel(packageDefinition);
            if (!packageDefinition.IsTemplate || packageDefinition.CompositionPresets.Count == 0)
            {
                DrawOptionalCompanionsPanel(packageDefinition);
            }

            DrawExtrasPanel(packageDefinition);
            DrawAdvancedPanel(packageDefinition);
        }

        private void DrawGraphGroupDetails(PackageGraphGroup group)
        {
            PackageGraphNode[] descendants = GetGraphGroupDescendantPackages(group.Id).ToArray();
            PackageDefinition[] missingPackages = descendants
                .Where(node => node != null && !node.IsInstalled && node.PackageDefinition != null)
                .Select(node => node.PackageDefinition)
                .Distinct()
                .ToArray();
            PackageDefinition[] packagesWithUpdates = descendants
                .Where(node => node != null &&
                               node.Status == PackageGraphNodeStatus.UpdateAvailable &&
                               node.PackageDefinition != null)
                .Select(node => node.PackageDefinition)
                .Distinct()
                .ToArray();
            int installedCount = descendants.Count(node => node.IsInstalled);
            int updateCount = descendants.Count(node => node.Status == PackageGraphNodeStatus.UpdateAvailable);
            int missingCount = descendants.Count(node =>
                node.Status == PackageGraphNodeStatus.NotInstalled ||
                node.Status == PackageGraphNodeStatus.Missing ||
                node.Status == PackageGraphNodeStatus.Warning);

            ImGui.DrawPanel("Group", () =>
            {
                EditorGUILayout.LabelField(group.DisplayName, _styles.TitleStyle);

                if (!string.IsNullOrWhiteSpace(group.Description))
                {
                    EditorGUILayout.LabelField(group.Description, _styles.SubtitleStyle);
                }

                ImGui.DrawKeyValueRow("Packages", descendants.Length.ToString());
                ImGui.DrawKeyValueRow("Installed", installedCount.ToString());
                ImGui.DrawKeyValueRow("Missing", missingCount.ToString());
                ImGui.DrawKeyValueRow("Updates", updateCount.ToString());
            }, GUILayout.ExpandWidth(true));

            if (missingPackages.Length > 0 || packagesWithUpdates.Length > 0)
            {
                ImGui.DrawPanel("Actions", () =>
                {
                    if (missingPackages.Length > 0 &&
                        DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                            DeucarianEditorIconIds.Download,
                            "Install missing (" + missingPackages.Length + ")",
                            "Install every missing package in this group.",
                            !IsAnyOperationBusy(),
                            true,
                            GUILayout.ExpandWidth(true)))
                    {
                        InstallGraphGroupPackages(group, missingPackages);
                    }

                    if (packagesWithUpdates.Length > 0 &&
                        DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                            DeucarianEditorIconIds.Update,
                            "Update available (" + packagesWithUpdates.Length + ")",
                            "Update every package with an available update in this group.",
                            !IsAnyOperationBusy(),
                            GUILayout.ExpandWidth(true)))
                    {
                        UpdateGraphGroupPackages(group, packagesWithUpdates);
                    }
                }, GUILayout.ExpandWidth(true));
            }
        }

        private void DrawIntegrationDetails(PackageDefinition packageDefinition)
        {
            DrawDetailHeader(packageDefinition);
            DrawStatusPanel(packageDefinition);
            DrawRequirementsPanel(packageDefinition);
            DrawChannelPanel(packageDefinition);
            DrawActionsPanel(packageDefinition);
            DrawOptionalCompanionsPanel(packageDefinition);
            DrawExtrasPanel(packageDefinition);
            DrawAdvancedPanel(packageDefinition);
        }
    }
}
