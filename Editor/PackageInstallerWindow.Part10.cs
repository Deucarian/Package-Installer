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

        private void DrawEcosystemOverviewGroupsPanel()
        {
            PackageGraphNavigationRow[] navigationRows = PackageGraphNavigationModel.CreateEcosystemOverviewGroupNavigationRows(
                    _lastPackageGraph,
                    _graphNavigationState)
                .ToArray();

            ImGui.DrawPanel("Groups", () =>
            {
                if (navigationRows.Length == 0)
                {
                    EditorGUILayout.LabelField("No ecosystem navigation is available.", _styles.MutedMiniLabelStyle);
                    SynchronizeDetailsNavigationHover(null);
                    return;
                }

                PackageGraphNavigationRow? hoveredRow = null;

                foreach (PackageGraphNavigationRow row in navigationRows)
                {
                    if (DrawEcosystemOverviewNavigationRow(row))
                    {
                        hoveredRow = row;
                    }
                }

                SynchronizeDetailsNavigationHover(hoveredRow);
            }, GUILayout.ExpandWidth(true));
        }

        private bool DrawEcosystemOverviewNavigationRow(PackageGraphNavigationRow row)
        {
            const float indentWidth = 14f;
            const float disclosureWidth = 13f;
            const float iconSize = 20f;
            const float leftPadding = 9f;
            const float rightPadding = 10f;
            Rect rowRect = GUILayoutUtility.GetRect(1f, 34f, GUILayout.ExpandWidth(true));
            int controlId = GUIUtility.GetControlID(FocusType.Keyboard, rowRect);
            bool hover = rowRect.Contains(Event.current.mousePosition);
            bool graphHover = IsGraphNavigationRowHoverContext(row);
            bool selected = row.IsSelected;
            bool keyboardFocused = GUIUtility.keyboardControl == controlId;

            if (Event.current.type == EventType.Repaint)
            {
                bool highlighted = selected || hover || graphHover || keyboardFocused;
                Color activePathBackground = Color.Lerp(
                    _styles.SampleRowBackgroundColor,
                    _styles.RowSelectedColor,
                    0.22f);
                DeucarianEditorVisualShell.DrawInsetSurface(
                    rowRect,
                    selected
                        ? _styles.RowSelectedColor
                        : highlighted
                            ? _styles.RowHoverColor
                            : row.IsInActivePath
                                ? activePathBackground
                                : _styles.SampleRowBackgroundColor,
                    highlighted || row.IsInActivePath ? _styles.InteractiveBorderColor : _styles.SeparatorColor,
                    4f);

                if (selected || row.HasAttention)
                {
                    EditorGUI.DrawRect(
                        new Rect(rowRect.x, rowRect.y, 3f, rowRect.height),
                        row.HasAttention ? PackageInstallerStatusPresentation.GetStatusColor(PackageInstallerVisualStatusKind.UpdateAvailable) : _styles.InteractiveBorderColor);
                }
            }

            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                GUIUtility.keyboardControl = controlId;
                ActivateEcosystemOverviewNavigationRow(row);
                Event.current.Use();
            }
            else if (IsGraphNavigationRowKeyboardActivation(
                         keyboardFocused,
                         Event.current.type,
                         Event.current.keyCode))
            {
                ActivateEcosystemOverviewNavigationRow(row);
                Event.current.Use();
            }

            EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);

            float indent = row.IsOverview ? 0f : row.Depth * indentWidth;
            float disclosureOffset = row.IsOverview ? 0f : disclosureWidth;
            Rect disclosureRect = new Rect(
                rowRect.x + leftPadding + indent,
                rowRect.y + 8f,
                disclosureWidth,
                18f);
            if (!row.IsOverview && row.HasChildren)
            {
                DrawSingleLineLabel(
                    disclosureRect,
                    new GUIContent(row.IsExpanded ? "v" : ">", row.Tooltip),
                    row.IsInActivePath ? _styles.MiniLabelStyle : _styles.MutedMiniLabelStyle);
            }

            Rect iconRect = new Rect(
                rowRect.x + leftPadding + indent + disclosureOffset,
                rowRect.y + 7f,
                iconSize,
                iconSize);
            DrawGraphNavigationIcon(iconRect, row.IconKey);

            GUIContent nameContent = new GUIContent(row.DisplayName, row.Tooltip);
            string summaryText = !row.IsPackage && _responsiveMode != PackageInstallerResponsiveMode.Wide
                ? PackageGraphNavigationModel.FormatCompactEcosystemOverviewGroupStatusSummary(row.StatusSummary)
                : row.Summary;
            GUIContent summaryContent = new GUIContent(summaryText, row.Summary);
            float contentX = iconRect.xMax + 9f;
            Rect contentRect = new Rect(
                contentX,
                rowRect.y + 8f,
                Mathf.Max(0f, rowRect.xMax - rightPadding - contentX),
                18f);
            float gap = 8f;
            float desiredSummaryWidth = _styles.MutedMiniLabelStyle.CalcSize(summaryContent).x + 2f;
            float maximumSummaryWidth = Mathf.Max(0f, contentRect.width - 40f - gap);
            float summaryWidth = Mathf.Min(
                desiredSummaryWidth,
                Mathf.Min(Mathf.Max(58f, contentRect.width * 0.44f), maximumSummaryWidth));
            Rect summaryRect = new Rect(
                contentRect.xMax - summaryWidth,
                contentRect.y,
                summaryWidth,
                contentRect.height);
            Rect nameRect = new Rect(
                contentRect.x,
                contentRect.y,
                Mathf.Max(0f, summaryRect.x - gap - contentRect.x),
                contentRect.height);

            DrawSingleLineLabel(nameRect, nameContent, _styles.MiniLabelStyle);
            DrawSingleLineLabel(summaryRect, summaryContent, _styles.MutedMiniLabelStyle);
            return hover;
        }

        private void ActivateEcosystemOverviewNavigationRow(PackageGraphNavigationRow row)
        {
            switch (row.TargetKind)
            {
                case PackageGraphNavigationTargetKind.Overview:
                    NavigateGraphToRoot();
                    return;
                case PackageGraphNavigationTargetKind.Group:
                    if (_lastPackageGraph != null &&
                        _lastPackageGraph.TryGetGroup(row.Id, out PackageGraphGroup group))
                    {
                        HandleGraphGroupFocused(group);
                    }

                    return;
                case PackageGraphNavigationTargetKind.Package:
                    if (_lastPackageGraph != null &&
                        _lastPackageGraph.TryGetNode(row.Id, out PackageGraphNode node) &&
                        node.PackageDefinition != null)
                    {
                        HandleGraphPackageSelected(node.PackageDefinition);
                    }

                    return;
            }
        }

        private static bool IsGraphNavigationRowKeyboardActivation(
            bool hasKeyboardFocus,
            EventType eventType,
            KeyCode keyCode)
        {
            return hasKeyboardFocus &&
                   eventType == EventType.KeyDown &&
                   PackageGraphKeyboard.IsActivationKey(keyCode);
        }

        private static bool ShouldShowEcosystemAttention(int attentionCount)
        {
            return attentionCount > 0;
        }

        private static EcosystemOverviewAction[] CreateEcosystemOverviewActions(int updateCount)
        {
            return updateCount > 0
                ? new[]
                {
                    new EcosystemOverviewAction(
                        PackageInstallerActionKind.UpdateAll,
                        "Update all (" + updateCount + ")")
                }
                : Array.Empty<EcosystemOverviewAction>();
        }

        private static bool ShouldDrawGraphNavigationBeforeContext(
            PackageInstallerResponsiveMode responsiveMode)
        {
            return responsiveMode != PackageInstallerResponsiveMode.Narrow;
        }

        private void DrawGraphNavigationIcon(Rect rect, string iconKey)
        {
            Texture icon = DeucarianEditorIcons.GetPackageIcon(iconKey);

            if (icon == null)
            {
                return;
            }

            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
        }

    }
}
