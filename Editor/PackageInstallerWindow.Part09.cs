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


        private string GetLastUpdateCheckTooltip()
        {
            DateTime? lastCheckedUtc = _packageUpdateCheckService.LastCheckedUtc;

            if (!lastCheckedUtc.HasValue)
            {
                return "Updates have not been checked yet.";
            }

            return "Last checked at " +
                   lastCheckedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
                   ".";
        }

        private void DrawSidebarSection(
            string title,
            PackageCategoryListView categoryView)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                EditorGUILayout.LabelField(title, _sectionTitleStyle);
            }

            GUILayout.Space(4f);

            bool expanded = DrawCategoryFoldoutHeader(categoryView);

            if (!expanded)
            {
                return;
            }

            IReadOnlyList<PackageDefinition> packagesToDraw = categoryView.FilteredPackages;

            foreach (PackageDefinition packageDefinition in packagesToDraw)
            {
                DrawSidebarRow(
                    packageDefinition,
                    packageDefinition.IsIntegration
                        ? SelectionKind.Integration
                        : SelectionKind.Package);
                GUILayout.Space(5f);
            }
        }

        private bool DrawCategoryFoldoutHeader(PackageCategoryListView categoryView)
        {
            if (categoryView == null)
            {
                return false;
            }

            bool expanded = IsCategoryExpanded(categoryView.Category);
            GUIContent content = new GUIContent(
                GetCategoryHeaderText(categoryView),
                GetCategoryHeaderTooltip(categoryView));
            bool nextExpanded = EditorGUILayout.Foldout(expanded, content, true, _foldoutStyle);

            if (nextExpanded != expanded)
            {
                SetCategoryExpanded(categoryView.Category, nextExpanded);
                Repaint();
            }

            return nextExpanded;
        }

        private string GetCategoryHeaderText(PackageCategoryListView categoryView)
        {
            string summary = categoryView.InstalledCount + "/" + categoryView.PackageCount + " installed";

            if (categoryView.FilteredPackageCount != categoryView.PackageCount)
            {
                summary += ", " + categoryView.FilteredPackageCount + " shown";
            }

            return categoryView.Category + " (" + summary + ")";
        }

        private static string GetCategoryHeaderTooltip(PackageCategoryListView categoryView)
        {
            if (categoryView == null)
            {
                return string.Empty;
            }

            return categoryView.Category + "\n" +
                   categoryView.InstalledCount + " installed out of " + categoryView.PackageCount + " packages.\n" +
                   categoryView.FilteredPackageCount + " packages match the active filters.";
        }

        private void DrawSidebarRow(PackageDefinition packageDefinition, SelectionKind selectionKind)
        {
            bool selected = IsSelected(packageDefinition, selectionKind);
            float rowHeight = GetSidebarRowHeight(packageDefinition);
            Rect rowRect = GUILayoutUtility.GetRect(1f, rowHeight, GUILayout.ExpandWidth(true));
            bool hover = rowRect.Contains(Event.current.mousePosition);
            VisualStatus status = GetPackageVisualStatus(packageDefinition);
            GUIContent displayNameContent = new GUIContent(
                GetDisplayNameForSidebar(packageDefinition),
                GetPackageTooltip(packageDefinition));
            GUIContent packageIdContent = new GUIContent(
                packageDefinition.PackageId,
                packageDefinition.PackageId);
            GUIContent metadataContent = new GUIContent(
                GetSidebarMetadata(packageDefinition, selectionKind),
                GetSidebarMetadataTooltip(packageDefinition, selectionKind));

            if (Event.current.type == EventType.Repaint)
            {
                Color background = selected ? _rowSelectedColor : hover ? _rowHoverColor : _rowBackgroundColor;
                DeucarianEditorVisualShell.DrawInsetSurface(
                    rowRect,
                    background,
                    selected || hover ? _interactiveBorderColor : _separatorColor,
                    6f);

                if (selected)
                {
                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3f, rowRect.height), GetStatusColor(status.Kind));
                }
            }

            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                SelectDefinition(packageDefinition, selectionKind);
                Event.current.Use();
            }

            EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);

            Rect packageIconRect = new Rect(rowRect.x + 10f, rowRect.y + 9f, 24f, 24f);
            DeucarianEditorIcons.DrawIcon(
                packageIconRect,
                DeucarianEditorIcons.GetPackageIcon(GetPackageIconKey(packageDefinition)),
                GetStatusColor(status.Kind));

            Rect titleRect = new Rect(
                rowRect.x + 42f,
                rowRect.y + 8f,
                rowRect.width - 52f,
                Mathf.Max(22f, rowHeight - 68f));
            GUI.Label(titleRect, displayNameContent, _rowTitleStyle);

            Rect packageIdRect = new Rect(rowRect.x + 10f, titleRect.yMax + 4f, rowRect.width - 20f, 16f);
            GUI.Label(packageIdRect, packageIdContent, _rowSubLabelStyle);

            Rect statusRect = new Rect(rowRect.xMax - 112f, rowRect.yMax - 28f, 102f, 20f);
            DrawStatusBadge(statusRect, status.Label, status.Kind, _rowStatusStyle);

            Rect metadataRect = new Rect(rowRect.x + 10f, rowRect.yMax - 26f, rowRect.width - 132f, 18f);
            GUI.Label(metadataRect, metadataContent, _rowSubLabelStyle);
        }

        private float GetSidebarRowHeight(PackageDefinition packageDefinition)
        {
            float titleWidth = Mathf.Max(160f, SidebarWidth - 42f);
            float titleHeight = _rowTitleStyle.CalcHeight(
                new GUIContent(GetDisplayNameForSidebar(packageDefinition)),
                titleWidth);

            return Mathf.Clamp(70f + titleHeight, SidebarRowMinHeight, SidebarRowMaxHeight);
        }

        private string GetSidebarMetadata(PackageDefinition packageDefinition, SelectionKind selectionKind)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            string channelSummary = GetChannelSummary(packageDefinition);
            string hierarchySummary = GetPackageHierarchyPath(packageDefinition);

            if (packageDefinition.HasDisplayVersion)
            {
                return hierarchySummary + " | " + channelSummary + " | " + packageDefinition.DisplayVersion;
            }

            return hierarchySummary + " | " + channelSummary;
        }

        private string GetSidebarMetadataTooltip(PackageDefinition packageDefinition, SelectionKind selectionKind)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            return GetSidebarMetadata(packageDefinition, selectionKind) + "\n" +
                   "Stable URL: " + (string.IsNullOrWhiteSpace(packageDefinition.StableUrl) ? "Not configured" : packageDefinition.StableUrl) + "\n" +
                   "Development URL: " + (string.IsNullOrWhiteSpace(packageDefinition.DevelopmentUrl) ? "Not configured" : packageDefinition.DevelopmentUrl);
        }

        private static string GetPackageTooltip(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            return packageDefinition.DisplayName + "\n" +
                   packageDefinition.PackageId + "\n" +
                   packageDefinition.Description;
        }

        private string GetDisplayNameForSidebar(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            return packageDefinition.DisplayName
                .Replace("UI Binding + Core State", "UI Binding + Core State")
                .Replace("Session + API", "Session + API");
        }

        private string GetChannelSummary(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            return packageDefinition.HasDevelopmentUrl ? "Stable / Development" : "Stable";
        }

        private void DrawDetailsPane()
        {
            Rect rect = BeginSurface(
                _detailsStyle,
                _detailsBackgroundColor,
                _panelBorderColor,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));

            _detailsScrollPosition = EditorGUILayout.BeginScrollView(
                _detailsScrollPosition,
                false,
                false,
                GUILayout.ExpandHeight(true));

            PackageDefinition selectedDefinition = GetSelectedDefinition();
            bool drawGraphNavigation = _viewMode == InstallerViewMode.EcosystemGraph;
            bool drawGraphNavigationBeforeContext =
                drawGraphNavigation && ShouldDrawGraphNavigationBeforeContext(_responsiveMode);

            if (selectedDefinition == null)
            {
                PackageGraphGroup focusedGroup = GetFocusedGraphGroup();

                if (focusedGroup != null)
                {
                    if (drawGraphNavigationBeforeContext)
                    {
                        DrawEcosystemOverviewGroupsPanel();
                    }

                    DrawGraphGroupDetails(focusedGroup);

                    if (drawGraphNavigation && !drawGraphNavigationBeforeContext)
                    {
                        DrawEcosystemOverviewGroupsPanel();
                    }
                }
                else
                {
                    DrawEcosystemOverviewDashboard();

                    if (drawGraphNavigation)
                    {
                        DrawEcosystemOverviewGroupsPanel();
                    }
                }
            }
            else if (_selectionKind == SelectionKind.Integration)
            {
                if (drawGraphNavigationBeforeContext)
                {
                    DrawEcosystemOverviewGroupsPanel();
                }

                DrawIntegrationDetails(selectedDefinition);

                if (drawGraphNavigation && !drawGraphNavigationBeforeContext)
                {
                    DrawEcosystemOverviewGroupsPanel();
                }
            }
            else
            {
                if (drawGraphNavigationBeforeContext)
                {
                    DrawEcosystemOverviewGroupsPanel();
                }

                DrawPackageDetails(selectedDefinition);

                if (drawGraphNavigation && !drawGraphNavigationBeforeContext)
                {
                    DrawEcosystemOverviewGroupsPanel();
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawEcosystemOverviewDashboard()
        {
            PackageGraphModel graph = _lastPackageGraph;
            PackageGraphNode[] nodes = graph == null
                ? Array.Empty<PackageGraphNode>()
                : graph.Nodes.Where(node => node != null && node.IsRegistered).ToArray();
            int installedCount = nodes.Count(node => node.IsInstalled);
            int updateCount = GetPackagesWithUpdates().Length;
            int attentionCount = nodes.Count(node =>
                node.Status == PackageGraphNodeStatus.Missing ||
                node.Status == PackageGraphNodeStatus.Warning);
            int notInstalledCount = Math.Max(0, nodes.Length - installedCount);
            EcosystemOverviewAction[] actions = CreateEcosystemOverviewActions(updateCount);

            DrawPanel("Ecosystem Overview", () =>
            {
                EditorGUILayout.LabelField("Deucarian Unity Package System", _titleStyle);
                EditorGUILayout.LabelField(
                    "Select a group or package node to inspect details. Use pan, zoom, Fit, 100%, and Center to navigate the graph.",
                    _mutedMiniLabelStyle);
                GUILayout.Space(8f);

                DrawKeyValueRow("Packages", nodes.Length.ToString());
                DrawFlatStatusRow(
                    DeucarianEditorIconIds.Success,
                    installedCount + " installed",
                    VisualStatusKind.Installed);
                DrawFlatStatusRow(
                    DeucarianEditorIconIds.Optional,
                    notInstalledCount + " not installed",
                    VisualStatusKind.NotInstalled);
                DrawFlatStatusRow(
                    DeucarianEditorIconIds.Update,
                    updateCount + " updates",
                    VisualStatusKind.UpdateAvailable);
                if (ShouldShowEcosystemAttention(attentionCount))
                {
                    DrawFlatStatusRow(
                        DeucarianEditorIconIds.Warning,
                        attentionCount + " attention",
                        VisualStatusKind.UpdateAvailable);
                }
                GUILayout.Space(6f);
                DrawKeyValueRow("Registry", PackageRegistryProvider.StatusMessage);
                DrawKeyValueRow("Filters", GetActiveFilterSummary());
            }, GUILayout.ExpandWidth(true));

            if (actions.Length > 0)
            {
                DrawPanel("Actions", () =>
                {
                    foreach (EcosystemOverviewAction action in actions)
                    {
                        if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                                GetActionIconId(action.Kind),
                                action.Label,
                                action.Label,
                                !IsAnyOperationBusy(),
                                GUILayout.ExpandWidth(true)))
                        {
                            HandleActionButton(action.Kind);
                        }
                    }
                }, GUILayout.ExpandWidth(true));
            }
        }
    }
}
