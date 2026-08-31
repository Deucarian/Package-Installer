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


        private static void AddEcosystemGroupNavigationRows(
            ICollection<PackageGraphNavigationRow> rows,
            PackageGraphModel graph,
            PackageGraphGroup group,
            int depth,
            ISet<string> activeGroupPath,
            PackageGraphNavigationState navigationState)
        {
            if (rows == null || graph == null || group == null)
            {
                return;
            }

            PackageGraphGroup[] childGroups = graph.GetChildGroups(group.Id)
                .Where(childGroup => childGroup != null)
                .ToArray();
            PackageGraphNode[] directPackages = graph.GetDirectPackages(group.Id)
                .Where(node => node != null && node.IsRegistered && node.PackageDefinition != null)
                .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PackageGraphCategoryStatusSummary groupStatusSummary =
                PackageGraphCategoryStatusSummary.Create(graph.GetDescendantPackages(group.Id));
            bool isInActivePath = activeGroupPath != null && activeGroupPath.Contains(group.Id);
            bool hasChildren = childGroups.Length > 0 || directPackages.Length > 0;
            bool isSelected = navigationState.TargetKind == PackageGraphNavigationTargetKind.Group &&
                              string.Equals(
                                  navigationState.FocusedGroupId,
                                  group.Id,
                                  StringComparison.OrdinalIgnoreCase);
            rows.Add(new PackageGraphNavigationRow(
                PackageGraphNavigationTargetKind.Group,
                group.Id,
                group.DisplayName,
                FormatEcosystemOverviewGroupStatusSummary(groupStatusSummary),
                groupStatusSummary,
                group.IconKey,
                group.Description,
                depth,
                hasChildren,
                isInActivePath,
                isInActivePath,
                isSelected,
                groupStatusSummary.AttentionCount > 0));

            if (!isInActivePath || !hasChildren)
            {
                return;
            }

            foreach (PackageGraphGroup childGroup in childGroups)
            {
                AddEcosystemGroupNavigationRows(
                    rows,
                    graph,
                    childGroup,
                    depth + 1,
                    activeGroupPath,
                    navigationState);
            }

            foreach (PackageGraphNode packageNode in directPackages)
            {
                PackageGraphCategoryStatusSummary packageStatusSummary =
                    PackageGraphCategoryStatusSummary.Create(new[] { packageNode });
                bool packageSelected = navigationState.TargetKind == PackageGraphNavigationTargetKind.Package &&
                                       string.Equals(
                                           navigationState.FocusedPackageId,
                                           packageNode.PackageId,
                                           StringComparison.OrdinalIgnoreCase);
                rows.Add(new PackageGraphNavigationRow(
                    PackageGraphNavigationTargetKind.Package,
                    packageNode.PackageId,
                    packageNode.DisplayName,
                    FormatPackageGraphNavigationStatus(packageNode),
                    packageStatusSummary,
                    packageNode.IconKey,
                    packageNode.Description,
                    depth + 1,
                    hasChildren: false,
                    isExpanded: false,
                    isInActivePath: packageSelected,
                    isSelected: packageSelected,
                    hasAttention: packageStatusSummary.AttentionCount > 0));
            }
        }

        private static string FormatPackageGraphNavigationStatus(PackageGraphNode node)
        {
            if (node == null)
            {
                return "Unknown";
            }

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

        private static string GetGraphPackageGroupId(PackageGraphModel graph, string packageId)
        {
            return graph != null &&
                   !string.IsNullOrWhiteSpace(packageId) &&
                   graph.TryGetNode(packageId, out PackageGraphNode node)
                ? node.GroupId
                : string.Empty;
        }

        private static string ResolveTopLevelGroupId(PackageGraphModel graph, string groupId)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return string.Empty;
            }

            string currentGroupId = groupId;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                if (string.IsNullOrWhiteSpace(group.ParentGroupId))
                {
                    return group.Id;
                }

                currentGroupId = group.ParentGroupId;
            }

            return string.Empty;
        }

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

            DrawPanel("Group", () =>
            {
                EditorGUILayout.LabelField(group.DisplayName, _titleStyle);

                if (!string.IsNullOrWhiteSpace(group.Description))
                {
                    EditorGUILayout.LabelField(group.Description, _subtitleStyle);
                }

                DrawKeyValueRow("Packages", descendants.Length.ToString());
                DrawKeyValueRow("Installed", installedCount.ToString());
                DrawKeyValueRow("Missing", missingCount.ToString());
                DrawKeyValueRow("Updates", updateCount.ToString());
            }, GUILayout.ExpandWidth(true));

            if (missingPackages.Length > 0 || packagesWithUpdates.Length > 0)
            {
                DrawPanel("Actions", () =>
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
