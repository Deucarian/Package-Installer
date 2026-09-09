using System;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageInstallerWindowStyles
    {
        private bool _stylesInitialized;
        private bool _lastProSkin;
        private Color _mainBackgroundColor;
        private Color _sidebarBackgroundColor;
        private Color _detailsBackgroundColor;
        private Color _headerPanelBackgroundColor;
        private Color _sampleRowBackgroundColor;
        private Color _panelBorderColor;
        private Color _interactiveBorderColor;
        private Color _separatorColor;
        private Color _rowBackgroundColor;
        private Color _rowHoverColor;
        private Color _rowSelectedColor;
        private Color _operationDrawerBackgroundColor;
        private Color _operationDrawerBorderColor;
        private Color _textColor;
        private Color _mutedTextColor;
        private GUIStyle _sidebarStyle;
        private GUIStyle _detailsStyle;
        private GUIStyle _sampleRowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _miniLabelStyle;
        private GUIStyle _mutedMiniLabelStyle;
        private GUIStyle _rowTitleStyle;
        private GUIStyle _rowSubLabelStyle;
        private GUIStyle _rowStatusStyle;
        private GUIStyle _foldoutStyle;

        internal Color MainBackgroundColor => _mainBackgroundColor;
        internal Color SidebarBackgroundColor => _sidebarBackgroundColor;
        internal Color DetailsBackgroundColor => _detailsBackgroundColor;
        internal Color HeaderPanelBackgroundColor => _headerPanelBackgroundColor;
        internal Color SampleRowBackgroundColor => _sampleRowBackgroundColor;
        internal Color PanelBorderColor => _panelBorderColor;
        internal Color InteractiveBorderColor => _interactiveBorderColor;
        internal Color SeparatorColor => _separatorColor;
        internal Color RowBackgroundColor => _rowBackgroundColor;
        internal Color RowHoverColor => _rowHoverColor;
        internal Color RowSelectedColor => _rowSelectedColor;
        internal Color OperationDrawerBackgroundColor => _operationDrawerBackgroundColor;
        internal Color OperationDrawerBorderColor => _operationDrawerBorderColor;
        internal Color TextColor => _textColor;
        internal Color MutedTextColor => _mutedTextColor;
        internal GUIStyle SidebarStyle => _sidebarStyle;
        internal GUIStyle DetailsStyle => _detailsStyle;
        internal GUIStyle SampleRowStyle => _sampleRowStyle;
        internal GUIStyle TitleStyle => _titleStyle;
        internal GUIStyle SubtitleStyle => _subtitleStyle;
        internal GUIStyle SectionTitleStyle => _sectionTitleStyle;
        internal GUIStyle MiniLabelStyle => _miniLabelStyle;
        internal GUIStyle MutedMiniLabelStyle => _mutedMiniLabelStyle;
        internal GUIStyle RowTitleStyle => _rowTitleStyle;
        internal GUIStyle RowSubLabelStyle => _rowSubLabelStyle;
        internal GUIStyle RowStatusStyle => _rowStatusStyle;
        internal GUIStyle FoldoutStyle => _foldoutStyle;

        internal void Ensure()
        {
            bool proSkin = EditorGUIUtility.isProSkin;

            if (_stylesInitialized && _lastProSkin == proSkin)
            {
                return;
            }

            _stylesInitialized = true;
            _lastProSkin = proSkin;

            _mainBackgroundColor = DeucarianEditorWorkbenchGUI.MainBackgroundColor;
            _sidebarBackgroundColor = DeucarianEditorWorkbenchGUI.SidebarBackgroundColor;
            _detailsBackgroundColor = DeucarianEditorWorkbenchGUI.DetailsBackgroundColor;
            _headerPanelBackgroundColor = DeucarianEditorWorkbenchGUI.HeaderPanelBackgroundColor;
            _sampleRowBackgroundColor = DeucarianEditorWorkbenchGUI.SampleRowBackgroundColor;
            _panelBorderColor = DeucarianEditorWorkbenchGUI.PanelBorderColor;
            _interactiveBorderColor = DeucarianEditorWorkbenchGUI.InteractiveBorderColor;
            _separatorColor = DeucarianEditorWorkbenchGUI.SeparatorColor;
            _rowBackgroundColor = DeucarianEditorWorkbenchGUI.RowBackgroundColor;
            _rowHoverColor = DeucarianEditorWorkbenchGUI.RowHoverColor;
            _rowSelectedColor = DeucarianEditorWorkbenchGUI.RowSelectedColor;
            _operationDrawerBackgroundColor = DeucarianEditorWorkbenchGUI.PanelBackgroundColor;
            _operationDrawerBackgroundColor.a = 0.52f;
            _operationDrawerBorderColor = DeucarianEditorWorkbenchGUI.InteractiveBorderColor;
            _operationDrawerBorderColor.a = 0.38f;
            _textColor = DeucarianEditorWorkbenchGUI.TextColor;
            _mutedTextColor = DeucarianEditorWorkbenchGUI.MutedTextColor;

            // Keep the released per-window ownership semantics while sourcing every
            // initial value from the shared Editor workbench contract.
            _sidebarStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.SidebarStyle);
            _detailsStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.DetailsStyle);
            _sampleRowStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.SampleRowStyle);
            _titleStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.TitleStyle);
            _subtitleStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.SubtitleStyle);
            _sectionTitleStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.SectionTitleStyle);
            _miniLabelStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.MiniLabelStyle);
            _mutedMiniLabelStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.MutedMiniLabelStyle);
            _rowTitleStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.RowTitleStyle);
            _rowSubLabelStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.RowSubLabelStyle);
            _rowStatusStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.RowStatusStyle);
            _foldoutStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.FoldoutStyle);

        }
    }
}
