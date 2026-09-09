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

        private void NavigateGraphToGroupOrRoot(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                NavigateGraphToRoot();
                return;
            }

            NavigateGraphToGroup(groupId);
        }

        private void NavigateGraphToGroup(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                NavigateGraphToRoot();
                return;
            }

            ClearDetailsGraphHover();
            _selectionKind = SelectionKind.Package;
            _selectedPackageId = string.Empty;
            _graphNavigationState = PackageGraphNavigationState.Group(groupId);
            _detailsScrollPosition = Vector2.zero;
            RefreshGraphView("group focus");
            Repaint();
        }

        private void NavigateGraphToRoot()
        {
            ClearGraphHoverState();
            _selectionKind = SelectionKind.Package;
            _selectedPackageId = string.Empty;
            _graphNavigationState = PackageGraphNavigationState.Overview();
            _detailsScrollPosition = Vector2.zero;
            RefreshGraphView("root focus");
            Repaint();
        }

        private void HandleRootKeyDown(KeyDownEvent evt)
        {
            if (_viewMode != InstallerViewMode.EcosystemGraph || evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            HandleGraphEscape(_graphView, HandleGraphBackNavigation);
            evt.StopPropagation();
        }

        private static void HandleGraphEscape(
            PackageGraphView graphView,
            Action fallbackBackNavigation)
        {
            if (graphView != null)
            {
                graphView.HandleEscapeFromWindow();
                return;
            }

            fallbackBackNavigation?.Invoke();
        }

        private void HandleGraphPackageAction(PackageDefinition packageDefinition, PackageGraphNodeAction action)
        {
            if (packageDefinition == null || action == PackageGraphNodeAction.None || IsAnyOperationBusy())
            {
                return;
            }

            SelectDefinition(
                packageDefinition,
                packageDefinition.IsIntegration ? SelectionKind.Integration : SelectionKind.Package,
                refreshGraph: false);
            _graphNavigationState = PackageGraphNavigationState.Package(
                packageDefinition.PackageId,
                PackageGraphNavigationModel.GetGraphPackageGroupId(_lastPackageGraph, packageDefinition.PackageId));

            switch (action)
            {
                case PackageGraphNodeAction.Install:
                    _packageDependencyInstaller.InstallWithDependencies(packageDefinition, GetSelectedChannel);
                    break;
                case PackageGraphNodeAction.Update:
                    UpdatePackage(packageDefinition);
                    break;
                case PackageGraphNodeAction.Reinstall:
                    ReinstallPackage(packageDefinition);
                    break;
            }

            RefreshGraphView("package action");
        }

        private void DrawGraphDetailsGui()
        {
            EnsureStyles();
            using (DeucarianEditorWorkbenchGUI.BeginEmbeddedPage(
                       GUILayout.ExpandHeight(true)))
            {
                DrawDetailsPane();
            }
        }

        private void DrawListViewGui()
        {
            EnsureStyles();
            EnsureValidSelection();

            using (DeucarianEditorWorkbenchGUI.BeginEmbeddedPage(
                       GUILayout.ExpandHeight(true)))
            {
                // DrawHeader();

                using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
                {
                    DrawSidebar();
                    GUILayout.Space(8f);
                    DrawDetailsPane();
                }

            }
        }

        private void DrawWindowBackground()
        {
            DeucarianEditorVisualShell.DrawWindowBackground(
                new Rect(0f, 0f, position.width, position.height),
                _styles.MainBackgroundColor);
        }

        private void DrawHeader()
        {
            bool compact = position.width < 1100f;

            DeucarianEditorChrome.DrawBrandHeader(
                "Deucarian Package Installer",
                "Install, update, remove, and compose Deucarian packages through first-class integration packages.");

            ImGui.BeginSurface(
                DeucarianEditorStyles.SectionBox,
                _styles.HeaderPanelBackgroundColor,
                _styles.PanelBorderColor,
                GUILayout.ExpandWidth(true));

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    DrawRegistrySummary();

                    if (compact)
                    {
                        GUILayout.Space(4f);
                        DrawUpdateSummary(true);
                    }
                }

                if (!compact)
                {
                    GUILayout.Space(12f);

                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(420f)))
                    {
                        DrawUpdateSummary(false);
                    }
                }
            }

            GUILayout.Space(6f);
            DrawHeaderUpdateControls(compact);

            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);
        }

        private void DrawRegistrySummary()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Registry", _styles.MutedMiniLabelStyle, GUILayout.Width(54f));
                ImGui.DrawStatusBadge(PackageRegistryProvider.All.Count + " packages", PackageInstallerVisualStatusKind.Info, GUILayout.Width(104f));

                if (PackageRegistryProvider.IsRemoteRefreshing)
                {
                    ImGui.DrawStatusBadge("Refreshing", PackageInstallerVisualStatusKind.Busy, GUILayout.Width(92f));
                }
                else
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(PackageRegistryProvider.StatusMessage, PackageRegistryProvider.StatusMessage),
                        _styles.MutedMiniLabelStyle,
                        GUILayout.ExpandWidth(true));
                }
            }
        }

        private void DrawHeaderUpdateControls(bool compact)
        {
            if (compact)
            {
                DrawUpdatePreferenceToggles(compact);
                GUILayout.Space(2f);
                DrawHeaderButtonRow();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawUpdatePreferenceToggles(compact);
                GUILayout.FlexibleSpace();
                DrawHeaderButtonRow();
            }
        }

        private void DrawUpdatePreferenceToggles(bool compact)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool checkOnStart = PackageUpdateCheckPreferences.CheckOnEditorStart;
                bool nextCheckOnStart = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Check on Start",
                        "Run one delayed background update check per Unity editor session."),
                    checkOnStart,
                    GUILayout.Width(compact ? 118f : 124f));

                if (nextCheckOnStart != checkOnStart)
                {
                    PackageUpdateCheckPreferences.CheckOnEditorStart = nextCheckOnStart;
                }

                bool checkOnOpen = PackageUpdateCheckPreferences.CheckOnWindowOpen;
                bool nextCheckOnOpen = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Check on Open",
                        "Check for updates when the Package Installer window opens. Throttled to once every 30 minutes."),
                    checkOnOpen,
                    GUILayout.Width(compact ? 118f : 124f));

                if (nextCheckOnOpen != checkOnOpen)
                {
                    PackageUpdateCheckPreferences.CheckOnWindowOpen = nextCheckOnOpen;
                }
            }
        }

        private void DrawHeaderButtonRow()
        {
            PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();
            bool busy = IsAnyOperationBusy();
            bool hasPackagesWithUpdates = packagesWithUpdates.Length > 0;

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawHeaderButton(
                    DeucarianEditorIconIds.Refresh,
                    "Refresh",
                    96f,
                    IsAnyOperationBusy(),
                    RefreshPackages);
                DrawActionHeaderButton(PackageInstallerActionKind.CheckUpdates, 118f, busy, hasPackagesWithUpdates);
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawActionHeaderButton(
            PackageInstallerActionKind buttonKind,
            float width,
            bool anyOperationBusy,
            bool hasPackagesWithUpdates)
        {
            PackageInstallerActionButtonState state = CreateActionButtonState(
                buttonKind,
                _activeActionKind,
                _cancelingActionKind,
                anyOperationBusy,
                hasPackagesWithUpdates);

            DrawHeaderButton(
                state.Label.StartsWith("Cancel", StringComparison.Ordinal)
                    ? DeucarianEditorIconIds.Stop
                    : GetActionIconId(buttonKind),
                state.Label,
                width,
                !state.Enabled,
                () => HandleActionButton(buttonKind));
        }

        private static string GetActionIconId(PackageInstallerActionKind actionKind)
        {
            switch (actionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    return DeucarianEditorIconIds.SearchCheck;
                case PackageInstallerActionKind.UpdateAll:
                    return DeucarianEditorIconIds.Update;
                case PackageInstallerActionKind.InstallAll:
                    return DeucarianEditorIconIds.Download;
                default:
                    return DeucarianEditorIconIds.Package;
            }
        }

        private void DrawHeaderButton(string iconId, string label, float width, bool disabled, Action action)
        {
            if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                    iconId,
                    label,
                    label,
                    !disabled,
                    GUILayout.Width(width)))
            {
                action?.Invoke();
            }
        }

        private static PackageInstallerActionButtonState CreateActionButtonState(
            PackageInstallerActionKind buttonKind,
            PackageInstallerActionKind activeActionKind,
            PackageInstallerActionKind cancelingActionKind,
            bool anyOperationBusy,
            bool hasPackagesWithUpdates)
        {
            bool isOwner = activeActionKind == buttonKind && buttonKind != PackageInstallerActionKind.None;

            if (isOwner)
            {
                return cancelingActionKind == buttonKind
                    ? new PackageInstallerActionButtonState("Canceling...", false)
                    : new PackageInstallerActionButtonState(GetCancelActionLabel(buttonKind), true);
            }

            bool enabled = !anyOperationBusy &&
                           (buttonKind != PackageInstallerActionKind.UpdateAll || hasPackagesWithUpdates);
            return new PackageInstallerActionButtonState(GetDefaultActionLabel(buttonKind), enabled);
        }
    }
}
