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


        private string GetOperationDrawerPreferenceKey()
        {
            return OperationDrawerPreferencePrefix + Application.dataPath.Replace("\\", "/");
        }

        private string GetProgressStepText(OperationProgressView operation)
        {
            if (operation == null || operation.TotalSteps <= 0)
            {
                return string.Empty;
            }

            int activeStep = Mathf.Clamp(
                operation.CompletedSteps + (operation.IsBusy ? 1 : 0),
                1,
                Mathf.Max(operation.TotalSteps, 1));
            string stepText = "Step " + activeStep + " / " + operation.TotalSteps;

            if (!string.IsNullOrWhiteSpace(operation.CurrentItem))
            {
                stepText += ": " + operation.CurrentItem;
            }

            return stepText;
        }

        private static float GetOperationProgress(OperationProgressView operation)
        {
            if (operation == null || operation.TotalSteps <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp01(operation.CompletedSteps / (float)Mathf.Max(operation.TotalSteps, 1));
        }

        private IReadOnlyList<PackageInstallProgressItem> GetLastProgressItems()
        {
            if (_packageInstallService.HasProgress)
            {
                return _packageInstallService.ProgressItems;
            }

            return Array.Empty<PackageInstallProgressItem>();
        }

        private IReadOnlyList<string> GetLastOperationMessages()
        {
            if (_packageInstallService.HasProgress)
            {
                return _packageInstallService.OperationMessages;
            }

            return Array.Empty<string>();
        }

        private VisualStatusKind GetLastSummaryStatusKind(IReadOnlyList<PackageInstallProgressItem> progressItems)
        {
            if (progressItems != null && progressItems.Any(item => item.State == PackageInstallProgressItemState.Failed))
            {
                return VisualStatusKind.Failed;
            }

            if (_packageSampleImportService.LastErrorMessage.Length > 0)
            {
                return VisualStatusKind.Failed;
            }

            if (IsAnyOperationBusy())
            {
                return VisualStatusKind.Busy;
            }

            return VisualStatusKind.Installed;
        }

        private static string GetLastSummaryStatusLabel(VisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case VisualStatusKind.Failed:
                    return "Failed";
                case VisualStatusKind.Busy:
                    return "Running";
                default:
                    return "Complete";
            }
        }

        private static string GetProgressItemStateLabel(PackageInstallProgressItemState state)
        {
            switch (state)
            {
                case PackageInstallProgressItemState.Active:
                    return "Active";
                case PackageInstallProgressItemState.Completed:
                    return "Completed";
                case PackageInstallProgressItemState.Failed:
                    return "Failed";
                case PackageInstallProgressItemState.Skipped:
                    return "Skipped";
                default:
                    return "Pending";
            }
        }

        private void DrawPanel(string title, Action content, params GUILayoutOption[] options)
        {
            DeucarianEditorWorkbenchGUI.DrawPanel(title, content, options);
        }

        private Rect BeginSurface(
            GUIStyle style,
            Color backgroundColor,
            Color borderColor,
            params GUILayoutOption[] options)
        {
            Rect rect = EditorGUILayout.BeginVertical(style, options);
            DrawSurface(rect, backgroundColor, borderColor);
            return rect;
        }

        private static void DrawSurface(Rect rect, Color backgroundColor, Color borderColor)
        {
            DeucarianEditorWorkbenchGUI.DrawSurface(rect, backgroundColor, borderColor);
        }

        private void DrawHorizontalSeparator()
        {
            DeucarianEditorWorkbenchGUI.DrawSeparator();
        }

        private void DrawInlineIcon(
            Rect rect,
            string iconId,
            VisualStatusKind statusKind,
            string tooltip)
        {
            float size = Mathf.Min(rect.width, rect.height);
            Rect iconRect = new Rect(
                rect.x + Mathf.Max(0f, (rect.width - size) * 0.5f),
                rect.y + Mathf.Max(0f, (rect.height - size) * 0.5f),
                size,
                size);
            DeucarianEditorIcons.DrawIcon(
                iconRect,
                DeucarianEditorIcons.GetIcon(iconId),
                GetStatusColor(statusKind));
            GUI.Label(rect, new GUIContent(string.Empty, tooltip ?? string.Empty), GUIStyle.none);
        }

        private void DrawStatusBadge(string text, VisualStatusKind statusKind, params GUILayoutOption[] options)
        {
            GUIStyle style = _rowStatusStyle ?? EditorStyles.miniLabel;
            string safeText = text ?? string.Empty;
            GUIContent content = new GUIContent("    " + safeText, safeText);
            Rect rect = GUILayoutUtility.GetRect(content, style, options);
            DrawStatusIndicator(rect, safeText, statusKind, style);
        }

        private void DrawStatusBadge(Rect rect, string text, VisualStatusKind statusKind, GUIStyle style)
        {
            DrawStatusIndicator(rect, text, statusKind, style);
        }

        private void DrawStatusIndicator(Rect rect, string text, VisualStatusKind statusKind, GUIStyle style)
        {
            GUIStyle labelStyle = style ?? _rowStatusStyle ?? EditorStyles.miniLabel;
            string safeText = text ?? string.Empty;
            float iconSize = Mathf.Min(16f, Mathf.Min(rect.width, rect.height));
            Rect markerRect = new Rect(rect.x, rect.y + Mathf.Max(0f, (rect.height - iconSize) * 0.5f), iconSize, iconSize);
            Rect labelRect = new Rect(markerRect.xMax + 4f, rect.y, Mathf.Max(0f, rect.width - markerRect.width - 4f), rect.height);

            DeucarianEditorIcons.DrawIcon(
                markerRect,
                DeucarianEditorIcons.GetIcon(GetStatusIconId(statusKind)),
                GetStatusColor(statusKind));
            GUI.Label(markerRect, new GUIContent(string.Empty, safeText), GUIStyle.none);
            DrawColoredRectLabel(
                labelRect,
                new GUIContent(safeText, safeText),
                labelStyle,
                _textColor);
        }

        private void DrawFlatStatusRow(string iconId, string text, VisualStatusKind statusKind)
        {
            DeucarianEditorWorkbenchGUI.DrawStatusIconRow(
                iconId,
                text,
                ToEditorStatus(statusKind));
        }

        private void DrawInlineHelp(string message, VisualStatusKind statusKind)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            DeucarianEditorChrome.DrawInlineHelp(message, ToMessageType(statusKind));
        }

        private void DrawKeyValueRow(string label, string value)
        {
            DeucarianEditorWorkbenchGUI.DrawKeyValueRow(label, value);
        }

        private void DrawSelectableValue(string label, string value)
        {
            string displayValue = string.IsNullOrWhiteSpace(value) ? "-" : value;

            EditorGUILayout.LabelField(new GUIContent(label, label), _mutedMiniLabelStyle);

            GUIStyle selectableStyle = new GUIStyle(EditorStyles.textArea);
            selectableStyle.normal.textColor = _textColor;
            selectableStyle.focused.textColor = _textColor;
            selectableStyle.hover.textColor = _textColor;
            selectableStyle.wordWrap = true;

            float width = Mathf.Max(220f, GetDetailsContentWidth() - 36f);
            float height = Mathf.Clamp(
                selectableStyle.CalcHeight(new GUIContent(displayValue), width) + 8f,
                EditorGUIUtility.singleLineHeight + 8f,
                92f);
            Rect valueRect = GUILayoutUtility.GetRect(
                1f,
                height,
                GUILayout.MinHeight(height),
                GUILayout.ExpandWidth(true));
            EditorGUI.TextArea(valueRect, displayValue, selectableStyle);
            GUI.Label(valueRect, new GUIContent(string.Empty, displayValue), GUIStyle.none);
            GUILayout.Space(4f);
        }

        private void DrawColoredLabel(string text, GUIStyle style, Color color, params GUILayoutOption[] options)
        {
            Color previousColor = GUI.contentColor;
            GUI.contentColor = color;
            EditorGUILayout.LabelField(new GUIContent(text, text), style, options);
            GUI.contentColor = previousColor;
        }

        private static void DrawTruncatedRectLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            string safeText = text ?? string.Empty;
            string displayText = GetEllipsizedText(safeText, style, rect.width);
            Color previousColor = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, new GUIContent(displayText, safeText), style ?? EditorStyles.label);
            GUI.contentColor = previousColor;
        }

        private static string GetEllipsizedText(string text, GUIStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            {
                return string.Empty;
            }

            GUIStyle resolvedStyle = style ?? EditorStyles.label;
            GUIContent content = new GUIContent(text);
            if (resolvedStyle.CalcSize(content).x <= maxWidth)
            {
                return text;
            }

            const string ellipsis = "...";
            if (resolvedStyle.CalcSize(new GUIContent(ellipsis)).x > maxWidth)
            {
                return string.Empty;
            }

            int low = 0;
            int high = text.Length;
            int best = 0;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                string candidate = text.Substring(0, mid).TrimEnd() + ellipsis;

                if (resolvedStyle.CalcSize(new GUIContent(candidate)).x <= maxWidth)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return text.Substring(0, best).TrimEnd() + ellipsis;
        }

        private void DrawColoredRectLabel(Rect rect, GUIContent content, GUIStyle style, Color color)
        {
            Color previousColor = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, content, style);
            GUI.contentColor = previousColor;
        }

        private VisualStatus GetPackageVisualStatus(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return new VisualStatus(DeucarianEditorIconIds.Info, "Unknown", VisualStatusKind.Info);
            }

            if (_packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId))
            {
                return new VisualStatus(DeucarianEditorIconIds.Busy, "Busy", VisualStatusKind.Busy);
            }

            if (_packageInstallService.IsBusy &&
                _packageInstallService.CurrentPackage != null &&
                string.Equals(
                    _packageInstallService.CurrentPackage.PackageId,
                    packageDefinition.PackageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new VisualStatus(DeucarianEditorIconIds.Busy, "Busy", VisualStatusKind.Busy);
            }

            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

            if (_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                if (updateStatus.IsSourceMigrationAvailable)
                {
                    return new VisualStatus(DeucarianEditorIconIds.GitBranch, "Migrate", VisualStatusKind.UpdateAvailable);
                }

                if (updateStatus.IsReloadPending)
                {
                    return new VisualStatus(DeucarianEditorIconIds.Refresh, "Reload", VisualStatusKind.UpdateAvailable);
                }

                if (updateStatus.IsUpdateAvailable)
                {
                    if (updateStatus.Kind == PackageUpdateStatusKind.SwitchAvailable)
                    {
                        return new VisualStatus(DeucarianEditorIconIds.Compare, "Switch", VisualStatusKind.UpdateAvailable);
                    }

                    return new VisualStatus(DeucarianEditorIconIds.Update, "Update", VisualStatusKind.UpdateAvailable);
                }

                return new VisualStatus(DeucarianEditorIconIds.PackageCheck, "Installed", VisualStatusKind.Installed);
            }

            return new VisualStatus(DeucarianEditorIconIds.Optional, "Not Installed", VisualStatusKind.NotInstalled);
        }

        private static Color GetStatusColor(VisualStatusKind statusKind)
        {
            return DeucarianEditorStatusBadge.GetColor(ToEditorStatus(statusKind));
        }
    }
}
