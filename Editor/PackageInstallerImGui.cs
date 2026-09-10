using System;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageInstallerImGui
    {
        private readonly PackageInstallerWindowStyles _styles;
        private readonly Func<float> getDetailsWidth;

        internal PackageInstallerImGui(PackageInstallerWindowStyles styles, Func<float> getDetailsWidth)
        {
            _styles = styles ?? throw new ArgumentNullException(nameof(styles));
            this.getDetailsWidth = getDetailsWidth ?? throw new ArgumentNullException(nameof(getDetailsWidth));
        }

        internal void DrawPanel(string title, Action content, params GUILayoutOption[] options)
        {
            DeucarianEditorWorkbenchGUI.DrawPanel(title, content, options);
        }

        internal Rect BeginSurface(
            GUIStyle style,
            Color backgroundColor,
            Color borderColor,
            params GUILayoutOption[] options)
        {
            Rect rect = EditorGUILayout.BeginVertical(style, options);
            DrawSurface(rect, backgroundColor, borderColor);
            return rect;
        }

        internal static void DrawSurface(Rect rect, Color backgroundColor, Color borderColor)
        {
            DeucarianEditorWorkbenchGUI.DrawSurface(rect, backgroundColor, borderColor);
        }

        internal void DrawHorizontalSeparator()
        {
            DeucarianEditorWorkbenchGUI.DrawSeparator();
        }

        internal void DrawInlineIcon(
            Rect rect,
            string iconId,
            PackageInstallerVisualStatusKind statusKind,
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
                PackageInstallerStatusPresentation.GetStatusColor(statusKind));
            GUI.Label(rect, new GUIContent(string.Empty, tooltip ?? string.Empty), GUIStyle.none);
        }

        internal void DrawStatusBadge(string text, PackageInstallerVisualStatusKind statusKind, params GUILayoutOption[] options)
        {
            GUIStyle style = _styles.RowStatusStyle ?? DeucarianEditorWorkbenchGUI.MiniLabelStyle;
            string safeText = text ?? string.Empty;
            GUIContent content = new GUIContent("    " + safeText, safeText);
            Rect rect = GUILayoutUtility.GetRect(content, style, options);
            DrawStatusIndicator(rect, safeText, statusKind, style);
        }

        internal void DrawStatusBadge(Rect rect, string text, PackageInstallerVisualStatusKind statusKind, GUIStyle style)
        {
            DrawStatusIndicator(rect, text, statusKind, style);
        }

        internal void DrawStatusIndicator(Rect rect, string text, PackageInstallerVisualStatusKind statusKind, GUIStyle style)
        {
            GUIStyle labelStyle = style ?? _styles.RowStatusStyle ?? DeucarianEditorWorkbenchGUI.MiniLabelStyle;
            string safeText = text ?? string.Empty;
            float iconSize = Mathf.Min(16f, Mathf.Min(rect.width, rect.height));
            Rect markerRect = new Rect(rect.x, rect.y + Mathf.Max(0f, (rect.height - iconSize) * 0.5f), iconSize, iconSize);
            Rect labelRect = new Rect(markerRect.xMax + 4f, rect.y, Mathf.Max(0f, rect.width - markerRect.width - 4f), rect.height);

            DeucarianEditorIcons.DrawIcon(
                markerRect,
                DeucarianEditorIcons.GetIcon(PackageInstallerStatusPresentation.GetStatusIconId(statusKind)),
                PackageInstallerStatusPresentation.GetStatusColor(statusKind));
            GUI.Label(markerRect, new GUIContent(string.Empty, safeText), GUIStyle.none);
            DrawColoredRectLabel(
                labelRect,
                new GUIContent(safeText, safeText),
                labelStyle,
                _styles.TextColor);
        }

        internal void DrawFlatStatusRow(string iconId, string text, PackageInstallerVisualStatusKind statusKind)
        {
            DeucarianEditorWorkbenchGUI.DrawStatusIconRow(
                iconId,
                text,
                PackageInstallerStatusPresentation.ToEditorStatus(statusKind));
        }

        internal void DrawInlineHelp(string message, PackageInstallerVisualStatusKind statusKind)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            DeucarianEditorChrome.DrawInlineHelp(message, PackageInstallerStatusPresentation.ToMessageType(statusKind));
        }

        internal void DrawKeyValueRow(string label, string value)
        {
            DeucarianEditorWorkbenchGUI.DrawKeyValueRow(label, value);
        }

        internal void DrawSelectableValue(string label, string value)
        {
            string displayValue = string.IsNullOrWhiteSpace(value) ? "-" : value;

            DeucarianEditorTextGUI.LabelField(new GUIContent(label, label), _styles.MutedMiniLabelStyle);

            GUIStyle selectableStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.InputStyles.TextArea);
            selectableStyle.normal.textColor = _styles.TextColor;
            selectableStyle.focused.textColor = _styles.TextColor;
            selectableStyle.hover.textColor = _styles.TextColor;
            selectableStyle.wordWrap = true;

            float width = Mathf.Max(220f, getDetailsWidth() - 36f);
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

        internal void DrawColoredLabel(string text, GUIStyle style, Color color, params GUILayoutOption[] options)
        {
            Color previousColor = GUI.contentColor;
            GUI.contentColor = color;
            DeucarianEditorTextGUI.LabelField(new GUIContent(text, text), style, options);
            GUI.contentColor = previousColor;
        }

        internal static void DrawTruncatedRectLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            string safeText = text ?? string.Empty;
            string displayText = GetEllipsizedText(safeText, style, rect.width);
            Color previousColor = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, new GUIContent(displayText, safeText), style ?? DeucarianEditorWorkbenchGUI.LabelStyle);
            GUI.contentColor = previousColor;
        }

        internal static string GetEllipsizedText(string text, GUIStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            {
                return string.Empty;
            }

            GUIStyle resolvedStyle = style ?? DeucarianEditorWorkbenchGUI.LabelStyle;
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

        internal void DrawColoredRectLabel(Rect rect, GUIContent content, GUIStyle style, Color color)
        {
            Color previousColor = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, content, style);
            GUI.contentColor = previousColor;
        }
    }
}
