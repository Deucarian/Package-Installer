using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Deucarian.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    /// <summary>
    /// Visual-equivalence contracts for the Package Installer workbench adoption.
    /// They preserve the released hierarchy, geometry, and state styling while
    /// proving that the effective visual source now comes from Deucarian Editor.
    /// </summary>
    internal sealed class PackageInstallerUiBaselineContractTests
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void Toolbar_VisualTreeNamesClassesAndOrderMatchBaseline()
        {
            PackageInstallerWindow window = ScriptableObject.CreateInstance<PackageInstallerWindow>();

            try
            {
                // Keep the serialized label deterministic instead of depending on a
                // developer's persisted project-channel override.
                SetPrivateField(window, "_stateRepository", null);

                VisualElement content = new VisualElement();
                InvokePrivate(window, "BuildViewToolbar", content);

                Assert.AreEqual(1, content.childCount);
                Assert.AreEqual(
                    NormalizeLines(@"
VisualElement|-|deucarian-toolbar-row,deucarian-workbench-toolbar,dpi-view-toolbar|-
  Button|-|deucarian-toggle-button|Ecosystem Graph
  Label|-|deucarian-workbench-toolbar__summary,dpi-view-toolbar__summary|-
  VisualElement|-|deucarian-workbench-toolbar__spacer,deucarian-toolbar-spacer|-
  Button|package-installer-global-channel-override|deucarian-workbench-toolbar__action,deucarian-workbench-toolbar__action--emphasized,dpi-view-toolbar__action,dpi-view-toolbar__channel-button|Channel: Stable
  Button|-|deucarian-workbench-toolbar__action,deucarian-workbench-toolbar__action--standard,dpi-view-toolbar__action,dpi-view-toolbar__graph-action|Refresh
  Button|-|deucarian-workbench-toolbar__action,deucarian-workbench-toolbar__action--standard,dpi-view-toolbar__action,dpi-view-toolbar__graph-action|Check Updates"),
                    SerializeVisualTree(content.ElementAt(0)));

                Assert.IsTrue(
                    content.ElementAt(0).ClassListContains(
                        DeucarianEditorWorkbenchToolbar.ToolbarClass));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ResponsiveModes_UseExactNineHundredAndElevenEightyBoundaries()
        {
            Assert.AreEqual(
                PackageInstallerResponsiveMode.Narrow,
                PackageInstallerWindow.ResolveResponsiveModeForTests(899.999f));
            Assert.AreEqual(
                PackageInstallerResponsiveMode.Compact,
                PackageInstallerWindow.ResolveResponsiveModeForTests(900f));
            Assert.AreEqual(
                PackageInstallerResponsiveMode.Compact,
                PackageInstallerWindow.ResolveResponsiveModeForTests(1179.999f));
            Assert.AreEqual(
                PackageInstallerResponsiveMode.Wide,
                PackageInstallerWindow.ResolveResponsiveModeForTests(1180f));

            Assert.AreEqual(
                DeucarianEditorLayoutMode.Narrow,
                DeucarianEditorResponsiveLayout.ResolveMode(899.999f));
            Assert.AreEqual(
                DeucarianEditorLayoutMode.Compact,
                DeucarianEditorResponsiveLayout.ResolveMode(900f));
            Assert.AreEqual(
                DeucarianEditorLayoutMode.Compact,
                DeucarianEditorResponsiveLayout.ResolveMode(1179.999f));
            Assert.AreEqual(
                DeucarianEditorLayoutMode.Wide,
                DeucarianEditorResponsiveLayout.ResolveMode(1180f));

            VisualElement root = new VisualElement();
            AssertResponsiveClasses(
                root,
                899.999f,
                PackageInstallerResponsiveMode.Narrow,
                DeucarianEditorResponsiveLayout.NarrowClass,
                "dpi-responsive--narrow");
            AssertResponsiveClasses(
                root,
                900f,
                PackageInstallerResponsiveMode.Compact,
                DeucarianEditorResponsiveLayout.CompactClass,
                "dpi-responsive--compact");
            AssertResponsiveClasses(
                root,
                1180f,
                PackageInstallerResponsiveMode.Wide,
                DeucarianEditorResponsiveLayout.WideClass,
                "dpi-responsive--wide");
        }

        [Test]
        public void FixedWallpaper_LayerOrderAndGeometryMatchBaseline()
        {
            VisualElement root = new VisualElement();
            VisualElement background = new VisualElement { name = "deucarian-window-background" };
            VisualElement overlay = new VisualElement { name = "deucarian-window-overlay" };
            VisualElement shell = new VisualElement { name = "deucarian-application-shell" };
            root.Add(background);
            root.Add(overlay);
            root.Add(shell);

            PackageInstallerWindow.ConfigureFixedWallpaperForTests(root, shell);

            CollectionAssert.AreEqual(
                new[]
                {
                    "deucarian-window-background",
                    DeucarianEditorAmbientGlass.AmbientLayerName,
                    DeucarianEditorAmbientGlass.GrainLayerName,
                    DeucarianEditorAmbientGlass.VignetteLayerName,
                    "deucarian-window-overlay",
                    PackageInstallerWindow.WallpaperTopSafeFadeName
                },
                shell.Children().Select(element => element.name).ToArray());

            Assert.IsTrue(shell.ClassListContains(DeucarianEditorWindowChrome.SafeShellClass));
            Assert.AreEqual(Overflow.Hidden, shell.style.overflow.value);

            AssertFixedWallpaperLayer(
                background,
                DeucarianEditorWindowChrome.BackgroundLayerClass);
            AssertFixedWallpaperLayer(
                overlay,
                DeucarianEditorWindowChrome.OverlayLayerClass);
            Assert.IsTrue(overlay.ClassListContains(DeucarianEditorWindowChrome.ReadabilityOverlayClass));

            VisualElement fade = shell.Q<VisualElement>(PackageInstallerWindow.WallpaperTopSafeFadeName);
            Assert.NotNull(fade);
            Assert.IsTrue(fade.ClassListContains(DeucarianEditorWindowChrome.TopSafeFadeClass));
            Assert.AreEqual(PickingMode.Ignore, fade.pickingMode);
            Assert.AreEqual(Position.Absolute, fade.style.position.value);
            Assert.AreEqual(0f, fade.style.left.value.value);
            Assert.AreEqual(0f, fade.style.right.value.value);
            Assert.AreEqual(0f, fade.style.top.value.value);
            Assert.AreEqual(86f, fade.style.height.value.value);
        }

        [Test]
        public void OperationDrawerAndFooter_GeometryAndChildOrderMatchBaseline()
        {
            VisualElement collapsedDrawer = PackageInstallerWindow.CreateOperationDrawerForTests(
                expanded: false,
                report: "One line");
            Assert.IsTrue(collapsedDrawer.ClassListContains("dpi-operation-drawer--collapsed"));
            Assert.AreEqual(0f, collapsedDrawer.style.height.value.value);
            Assert.AreEqual(0f, collapsedDrawer.style.minHeight.value.value);
            Assert.AreEqual(0f, collapsedDrawer.style.maxHeight.value.value);

            VisualElement expandedDrawer = PackageInstallerWindow.CreateOperationDrawerForTests(
                expanded: true,
                report: "One line");
            Assert.IsTrue(expandedDrawer.ClassListContains("dpi-operation-drawer--expanded"));
            Assert.AreEqual(88f, expandedDrawer.style.height.value.value);
            Assert.AreEqual(88f, expandedDrawer.style.minHeight.value.value);
            Assert.AreEqual(88f, expandedDrawer.style.maxHeight.value.value);
            Assert.AreEqual(
                210f,
                PackageInstallerWindow.CalculateOperationDrawerContainerHeightForTests(
                    expanded: true,
                    contentLineCount: 40));

            ScrollView scroll = expandedDrawer.Q<ScrollView>(PackageInstallerWindow.OperationDrawerScrollViewName);
            VisualElement drawerContent = expandedDrawer.Q<VisualElement>(PackageInstallerWindow.OperationDrawerContentName);
            Assert.AreSame(scroll, expandedDrawer.ElementAt(0));
            Assert.AreSame(drawerContent, scroll.contentContainer.ElementAt(0));
            CollectionAssert.AreEqual(
                new[]
                {
                    "dpi-operation-row dpi-operation-row--header",
                    "dpi-operation-row dpi-operation-row--option",
                    "dpi-operation-row dpi-operation-row--message dpi-operation-text--secondary dpi-operation-drawer__message",
                    "dpi-operation-row dpi-operation-row--option"
                },
                drawerContent.Children().Select(SerializeCustomClasses).ToArray());

            VisualElement footer = PackageInstallerWindow.CreateOperationFooterForTests();
            Assert.AreEqual(34f, footer.style.height.value.value);
            Assert.AreEqual(34f, footer.style.minHeight.value.value);
            Assert.AreEqual(34f, footer.style.maxHeight.value.value);
            Assert.AreEqual(12f, footer.style.paddingLeft.value.value);
            Assert.AreEqual(12f, footer.style.paddingRight.value.value);
            Assert.AreEqual(Overflow.Hidden, footer.style.overflow.value);
            CollectionAssert.AreEqual(
                new[]
                {
                    PackageInstallerWindow.OperationFooterStatusGroupName,
                    PackageInstallerWindow.OperationFooterSummaryName,
                    string.Empty,
                    PackageInstallerWindow.OperationFooterCancelButtonName,
                    PackageInstallerWindow.OperationFooterDetailsButtonName,
                    PackageInstallerWindow.OperationFooterVersionName
                },
                footer.Children().Select(element => element.name ?? string.Empty).ToArray());

            Button cancel = footer.Q<Button>(PackageInstallerWindow.OperationFooterCancelButtonName);
            Button details = footer.Q<Button>(PackageInstallerWindow.OperationFooterDetailsButtonName);
            Assert.AreEqual(92f, cancel.style.width.value.value);
            Assert.AreEqual(24f, cancel.style.height.value.value);
            Assert.AreEqual(DisplayStyle.None, cancel.style.display.value);
            Assert.AreEqual(96f, details.style.width.value.value);
            Assert.AreEqual(24f, details.style.height.value.value);
            Assert.AreEqual(8f, details.style.marginRight.value.value);
        }

        [Test]
        public void Stylesheets_ToolbarActionsAndToggleStatesMatchBaseline()
        {
            string installerCss = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/UI/PackageInstaller/PackageInstallerGraph.uss");
            string editorCss = ReadPackageFile(
                "com.deucarian.editor",
                "Editor/Assets/Styles/DeucarianEditor.uss");
            string installerSource = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/PackageInstallerWindow.cs");

            Assert.That(
                installerSource,
                Does.Contain("DeucarianEditorWorkbenchToolbar.CreateToolbar();"));
            Assert.That(
                installerSource,
                Does.Contain("DeucarianEditorWorkbenchToolbar.CreateToggleButton("));
            Assert.That(
                installerSource,
                Does.Contain("DeucarianEditorWorkbenchToolbar.CreateActionButton("));
            Assert.That(
                installerSource,
                Does.Contain("DeucarianEditorWorkbenchToolbar.CreateSummary(string.Empty);"));
            Assert.That(
                installerSource,
                Does.Contain("DeucarianEditorWorkbenchToolbar.CreateSpacer();"));

            foreach (string migratedSelector in new[]
                     {
                         ".dpi-view-toolbar",
                         ".dpi-view-toolbar__summary",
                         ".dpi-view-toolbar__action",
                         ".dpi-view-toolbar__graph-action:hover",
                         ".dpi-view-toolbar__graph-action:active",
                         ".dpi-view-toolbar__channel-button",
                         ".dpi-view-toolbar__channel-button:hover",
                         ".dpi-responsive--compact .dpi-view-toolbar__action",
                         ".dpi-responsive--compact .dpi-view-toolbar__channel-button",
                         ".dpi-responsive--narrow .dpi-view-toolbar",
                         ".dpi-responsive--narrow .dpi-view-toolbar__summary"
                     })
            {
                AssertNoRuleSelector(installerCss, migratedSelector);
            }

            AssertRuleValues(editorCss, ".dpi-view-toolbar",
                "margin-bottom", "8px",
                "border-top-left-radius", "7px",
                "border-top-right-radius", "7px",
                "border-bottom-right-radius", "7px",
                "border-bottom-left-radius", "7px",
                "border-left-width", "1px",
                "border-right-width", "1px",
                "border-top-width", "1px",
                "border-bottom-width", "1px",
                "border-left-color", "rgba(90, 111, 160, 0.24)",
                "border-right-color", "rgba(90, 111, 160, 0.20)",
                "border-top-color", "rgba(128, 160, 192, 0.26)",
                "border-bottom-color", "rgba(59, 166, 154, 0.14)",
                "background-color", "rgba(12, 22, 31, 0.48)");

            AssertRuleValues(editorCss, ".dpi-view-toolbar__action",
                "height", "28px",
                "min-width", "86px",
                "margin-left", "4px",
                "border-top-left-radius", "5px",
                "border-top-right-radius", "5px",
                "border-bottom-right-radius", "5px",
                "border-bottom-left-radius", "5px",
                "border-left-width", "1px",
                "border-right-width", "1px",
                "border-top-width", "1px",
                "border-bottom-width", "1px",
                "border-left-color", "rgba(90, 111, 160, 0.30)",
                "border-right-color", "rgba(90, 111, 160, 0.24)",
                "border-top-color", "rgba(126, 172, 202, 0.26)",
                "border-bottom-color", "rgba(59, 166, 154, 0.18)",
                "background-color", "rgba(22, 38, 48, 0.52)");
            AssertRuleValues(editorCss, ".dpi-view-toolbar__graph-action:hover",
                "border-left-color", "rgba(59, 209, 191, 0.48)",
                "border-right-color", "rgba(59, 209, 191, 0.38)",
                "border-top-color", "rgba(126, 172, 202, 0.38)",
                "border-bottom-color", "rgba(59, 209, 191, 0.34)",
                "background-color", "rgba(28, 56, 64, 0.66)",
                "color", "rgba(230, 244, 246, 0.96)");
            AssertRuleValues(editorCss, ".dpi-view-toolbar__graph-action:active",
                "border-left-color", "rgba(59, 209, 191, 0.72)",
                "border-right-color", "rgba(59, 209, 191, 0.58)",
                "border-top-color", "rgba(126, 172, 202, 0.52)",
                "border-bottom-color", "rgba(59, 209, 191, 0.52)",
                "background-color", "rgba(19, 43, 52, 0.78)",
                "color", "rgba(238, 250, 251, 1.00)");
            AssertRuleValues(editorCss, ".dpi-view-toolbar__channel-button",
                "min-width", "124px",
                "margin-right", "2px",
                "border-left-color", "rgba(59, 166, 154, 0.42)",
                "border-right-color", "rgba(59, 166, 154, 0.32)",
                "border-top-color", "rgba(126, 172, 202, 0.30)",
                "border-bottom-color", "rgba(59, 166, 154, 0.26)",
                "background-color", "rgba(25, 53, 57, 0.54)");
            AssertRuleValues(editorCss, ".dpi-view-toolbar__channel-button:hover",
                "border-left-color", "rgba(59, 209, 191, 0.58)",
                "border-right-color", "rgba(59, 209, 191, 0.44)",
                "border-top-color", "rgba(126, 172, 202, 0.42)",
                "border-bottom-color", "rgba(59, 209, 191, 0.38)",
                "background-color", "rgba(31, 70, 73, 0.68)");

            AssertRuleValues(editorCss, ".deucarian-toolbar-row",
                "height", "46px",
                "min-height", "46px",
                "max-height", "46px",
                "margin-bottom", "8px",
                "padding-left", "10px",
                "padding-right", "10px",
                "padding-top", "8px",
                "padding-bottom", "8px",
                "border-top-left-radius", "8px",
                "border-top-right-radius", "8px",
                "border-bottom-right-radius", "8px",
                "border-bottom-left-radius", "8px",
                "border-left-width", "1px",
                "border-right-width", "1px",
                "border-top-width", "1px",
                "border-bottom-width", "1px");
            AssertRuleValues(editorCss, ".deucarian-toggle-button",
                "height", "24px",
                "min-width", "116px",
                "margin-right", "4px",
                "border-top-left-radius", "5px",
                "border-top-right-radius", "5px",
                "border-bottom-right-radius", "5px",
                "border-bottom-left-radius", "5px",
                "background-color", "rgba(32, 47, 56, 0.44)",
                "border-left-color", "rgba(90, 111, 160, 0.25)",
                "border-right-color", "rgba(90, 111, 160, 0.25)",
                "border-top-color", "rgba(90, 111, 160, 0.25)",
                "border-bottom-color", "rgba(90, 111, 160, 0.25)");
            AssertRuleValues(editorCss, ".deucarian-toggle-button:hover",
                "background-color", "rgba(32, 47, 56, 0.68)",
                "color", "var(--deucarian-text)");
            AssertRuleValues(editorCss, ".deucarian-toggle-button--active",
                "background-color", "rgba(39, 96, 101, 0.52)",
                "border-left-color", "var(--deucarian-border-active)",
                "border-right-color", "var(--deucarian-border-active)",
                "border-top-color", "var(--deucarian-border-active)",
                "border-bottom-color", "var(--deucarian-border-active)",
                "color", "var(--deucarian-text)",
                "-unity-font-style", "bold");
        }

        [Test]
        public void Stylesheets_OperationDrawerAndFooterGeometryMatchBaseline()
        {
            string installerCss = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/UI/PackageInstaller/PackageInstallerGraph.uss");
            string editorCss = ReadPackageFile(
                "com.deucarian.editor",
                "Editor/Assets/Styles/DeucarianEditor.uss");

            foreach (string migratedSelector in new[]
                     {
                         ".dpi-operation-surface",
                         ".dpi-operation-drawer",
                         ".dpi-operation-drawer--expanded",
                         ".dpi-operation-drawer--collapsed",
                         ".dpi-operation-drawer__scroll",
                         ".dpi-operation-content",
                         ".dpi-operation-row",
                         ".dpi-operation-row--header",
                         ".dpi-operation-row--option",
                         ".dpi-operation-row--message",
                         ".dpi-operation-text--primary",
                         ".dpi-operation-text--secondary",
                         ".dpi-operation-drawer__title",
                         ".dpi-operation-drawer__toggle",
                         ".dpi-operation-drawer__option-label",
                         ".dpi-operation-drawer__message",
                         ".dpi-operation-footer",
                         ".dpi-operation-footer__status",
                         ".dpi-operation-footer__status-icon",
                         ".dpi-operation-footer__status-label",
                         ".dpi-operation-footer__summary",
                         ".dpi-operation-footer__spacer",
                         ".dpi-operation-footer__details-button",
                         ".dpi-operation-footer__version"
                     })
            {
                AssertNoRuleSelector(installerCss, migratedSelector);
            }

            AssertRuleValues(editorCss, ".dpi-operation-surface",
                "--deucarian-workbench-operation-inline-padding", "10px",
                "--deucarian-workbench-operation-block-padding", "8px",
                "--deucarian-workbench-operation-row-gap", "6px",
                "--deucarian-workbench-operation-control-gap", "8px",
                "--deucarian-workbench-operation-footer-inline-padding", "10px",
                "--deucarian-workbench-operation-footer-block-padding", "0px",
                "--deucarian-workbench-operation-footer-height", "34px",
                "--dpi-operation-inline-padding", "10px",
                "--dpi-operation-block-padding", "8px",
                "--dpi-operation-row-gap", "6px",
                "--dpi-operation-control-gap", "8px",
                "--dpi-operation-footer-inline-padding", "10px",
                "--dpi-operation-footer-block-padding", "0px",
                "--dpi-operation-footer-height", "34px");
            AssertRuleValues(editorCss, ".dpi-operation-drawer",
                "flex-shrink", "0",
                "height", "0",
                "min-height", "0",
                "max-height", "0",
                "margin-top", "0",
                "padding-left", "0",
                "padding-right", "0",
                "padding-top", "0",
                "padding-bottom", "0",
                "border-left-width", "0",
                "border-right-width", "0",
                "border-top-width", "0",
                "border-bottom-width", "0",
                "border-top-left-radius", "8px",
                "border-top-right-radius", "8px",
                "border-bottom-right-radius", "8px",
                "border-bottom-left-radius", "8px",
                "overflow", "hidden");
            AssertRuleValues(editorCss, ".dpi-operation-drawer--expanded",
                "margin-bottom", "6px",
                "border-left-width", "1px",
                "border-right-width", "1px",
                "border-top-width", "1px",
                "border-bottom-width", "1px",
                "background-color", "rgba(10, 20, 30, 0.96)");
            AssertRuleValues(editorCss, ".dpi-operation-footer",
                "height", "var(--deucarian-workbench-operation-footer-height)",
                "min-height", "var(--deucarian-workbench-operation-footer-height)",
                "max-height", "var(--deucarian-workbench-operation-footer-height)",
                "padding-left", "var(--deucarian-workbench-operation-inline-padding)",
                "padding-right", "var(--deucarian-workbench-operation-inline-padding)",
                "border-top-left-radius", "6px",
                "border-top-right-radius", "6px",
                "border-bottom-right-radius", "6px",
                "border-bottom-left-radius", "6px",
                "border-left-width", "1px",
                "border-right-width", "1px",
                "border-top-width", "1px",
                "border-bottom-width", "1px",
                "overflow", "hidden");
            AssertRuleValues(editorCss, ".dpi-operation-footer__details-button",
                "width", "96px",
                "min-width", "96px",
                "max-width", "96px",
                "height", "24px",
                "min-height", "24px",
                "max-height", "24px",
                "margin-right", "var(--deucarian-workbench-operation-control-gap)",
                "border-top-left-radius", "5px",
                "border-top-right-radius", "5px",
                "border-bottom-right-radius", "5px",
                "border-bottom-left-radius", "5px",
                "border-left-width", "1px",
                "border-right-width", "1px",
                "border-top-width", "1px",
                "border-bottom-width", "1px");
        }

        [Test]
        public void ImGuiStyleDeclarations_PanelMetricsAndColorsMatchBaseline()
        {
            string installerSource = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/PackageInstallerWindow.cs");
            string editorStylesSource = ReadPackageFile(
                "com.deucarian.editor",
                "Editor/DeucarianEditorStyles.cs");
            string workbenchSource = ReadPackageFile(
                "com.deucarian.editor",
                "Editor/DeucarianEditorWorkbenchGUI.cs");

            foreach (string declaration in new[]
                     {
                         "_mainBackgroundColor = DeucarianEditorWorkbenchGUI.MainBackgroundColor;",
                         "_sidebarBackgroundColor = DeucarianEditorWorkbenchGUI.SidebarBackgroundColor;",
                         "_detailsBackgroundColor = DeucarianEditorWorkbenchGUI.DetailsBackgroundColor;",
                         "_headerPanelBackgroundColor = DeucarianEditorWorkbenchGUI.HeaderPanelBackgroundColor;",
                         "_sampleRowBackgroundColor = DeucarianEditorWorkbenchGUI.SampleRowBackgroundColor;",
                         "_panelBorderColor = DeucarianEditorWorkbenchGUI.PanelBorderColor;",
                         "_interactiveBorderColor = DeucarianEditorWorkbenchGUI.InteractiveBorderColor;",
                         "_separatorColor = DeucarianEditorWorkbenchGUI.SeparatorColor;",
                         "_rowBackgroundColor = DeucarianEditorWorkbenchGUI.RowBackgroundColor;",
                         "_rowHoverColor = DeucarianEditorWorkbenchGUI.RowHoverColor;",
                         "_rowSelectedColor = DeucarianEditorWorkbenchGUI.RowSelectedColor;",
                         "_operationDrawerBackgroundColor.a = 0.52f;",
                         "_operationDrawerBorderColor.a = 0.38f;",
                         "_windowStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.WindowStyle);",
                         "_sidebarStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.SidebarStyle);",
                         "_detailsStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.DetailsStyle);",
                         "_sampleRowStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.SampleRowStyle);",
                         "_titleStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.TitleStyle);",
                         "_subtitleStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.SubtitleStyle);",
                         "_sectionTitleStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.SectionTitleStyle);",
                         "_miniLabelStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.MiniLabelStyle);",
                         "_mutedMiniLabelStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.MutedMiniLabelStyle);",
                         "_rowTitleStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.RowTitleStyle);",
                         "_rowSubLabelStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.RowSubLabelStyle);",
                         "_rowStatusStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.RowStatusStyle);",
                         "_markerStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.MarkerStyle);",
                         "_foldoutStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.FoldoutStyle);",
                         "_primaryButtonStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.PrimaryButtonStyle);",
                         "_secondaryButtonStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.SecondaryButtonStyle);",
                         "DeucarianEditorWorkbenchGUI.DrawPanel(title, content, options);",
                         "DeucarianEditorWorkbenchGUI.DrawSurface(rect, backgroundColor, borderColor);",
                         "DeucarianEditorWorkbenchGUI.DrawSeparator();",
                         "DeucarianEditorWorkbenchGUI.DrawStatusRow(",
                         "DeucarianEditorWorkbenchGUI.DrawKeyValueRow(label, value);"
                     })
            {
                Assert.That(installerSource, Does.Contain(declaration));
            }

            Assert.AreEqual(10, DeucarianEditorLayoutMetrics.SurfaceHorizontalPadding);
            Assert.AreEqual(8, DeucarianEditorLayoutMetrics.SurfaceVerticalPadding);
            Assert.AreEqual(8, DeucarianEditorLayoutMetrics.SurfaceSpacing);
            Assert.That(
                editorStylesSource,
                Does.Match(
                    @"sectionBox\.padding\s*=\s*new RectOffset\(\s*" +
                    @"DeucarianEditorLayoutMetrics\.SurfaceHorizontalPadding,\s*" +
                    @"DeucarianEditorLayoutMetrics\.SurfaceHorizontalPadding,\s*" +
                    @"DeucarianEditorLayoutMetrics\.SurfaceVerticalPadding,\s*" +
                    @"DeucarianEditorLayoutMetrics\.SurfaceVerticalPadding\s*\);"));
            Assert.That(
                editorStylesSource,
                Does.Match(
                    @"sectionBox\.margin\s*=\s*new RectOffset\(\s*0,\s*0,\s*0,\s*" +
                    @"DeucarianEditorLayoutMetrics\.SurfaceSpacing\s*\);"));

            foreach (string declaration in new[]
                     {
                         "public const float DetailLabelWidth = 118f;",
                         "public const float ButtonHeight = DeucarianEditorLayoutMetrics.CommandControlHeight;",
                         "public const float PanelSpacing = DeucarianEditorLayoutMetrics.SurfaceSpacing;",
                         "DeucarianEditorLayoutMetrics.PageHorizontalPadding",
                         "DeucarianEditorLayoutMetrics.PageTopPadding",
                         "DeucarianEditorLayoutMetrics.PageBottomPadding",
                         "DeucarianEditorLayoutMetrics.SurfaceHorizontalPadding",
                         "DeucarianEditorLayoutMetrics.SurfaceVerticalPadding",
                         "DeucarianEditorLayoutMetrics.SurfaceSpacing",
                         "titleStyle.fontSize = 15;",
                         "primaryButtonStyle.fixedHeight = ButtonHeight;",
                         "secondaryButtonStyle.fixedHeight = ButtonHeight;"
                     })
            {
                Assert.That(workbenchSource, Does.Contain(declaration));
            }

            Assert.That(installerSource, Does.Not.Contain("_windowStyle.padding ="));
            Assert.That(installerSource, Does.Not.Contain("_rowBackgroundColor = new Color"));

            // Preserve the released status-row color composition exactly: the
            // caller tint is applied through GUI.contentColor and multiplied by
            // each style state's existing text color. The shared helper must also
            // restore the global IMGUI tint after drawing.
            Assert.That(workbenchSource, Does.Contain("Color previousColor = GUI.contentColor;"));
            Assert.That(workbenchSource, Does.Contain("GUI.contentColor = color;"));
            Assert.That(workbenchSource, Does.Contain("GUI.Label(rect, content, style);"));
            Assert.That(workbenchSource, Does.Contain("GUI.contentColor = previousColor;"));

            int drawColoredLabelStart = workbenchSource.IndexOf(
                "private static void DrawColoredLabel",
                StringComparison.Ordinal);
            Assert.GreaterOrEqual(drawColoredLabelStart, 0);
            int ensureStylesStart = workbenchSource.IndexOf(
                "private static void EnsureStyles",
                drawColoredLabelStart,
                StringComparison.Ordinal);
            Assert.Greater(ensureStylesStart, drawColoredLabelStart);
            string drawColoredLabelSource = workbenchSource.Substring(
                drawColoredLabelStart,
                ensureStylesStart - drawColoredLabelStart);
            Assert.That(drawColoredLabelSource, Does.Not.Contain("new GUIStyle(style)"));

            Assert.AreEqual(8f, DeucarianEditorVisualShell.SurfaceRadius);
            Assert.AreEqual(118f, DeucarianEditorWorkbenchGUI.DetailLabelWidth);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.PageHorizontalPadding,
                DeucarianEditorWorkbenchGUI.WindowStyle.padding.left);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.PageHorizontalPadding,
                DeucarianEditorWorkbenchGUI.WindowStyle.padding.right);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.PageTopPadding,
                DeucarianEditorWorkbenchGUI.WindowStyle.padding.top);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.PageBottomPadding,
                DeucarianEditorWorkbenchGUI.WindowStyle.padding.bottom);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.SurfaceHorizontalPadding,
                DeucarianEditorWorkbenchGUI.SidebarStyle.padding.left);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.SurfaceHorizontalPadding,
                DeucarianEditorWorkbenchGUI.DetailsStyle.padding.left);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.SurfaceHorizontalPadding,
                DeucarianEditorWorkbenchGUI.SampleRowStyle.padding.left);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.SurfaceVerticalPadding,
                DeucarianEditorWorkbenchGUI.SampleRowStyle.padding.top);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.SurfaceSpacing,
                DeucarianEditorWorkbenchGUI.SampleRowStyle.margin.bottom);
            Assert.AreEqual(15, DeucarianEditorWorkbenchGUI.TitleStyle.fontSize);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.CommandControlHeight,
                DeucarianEditorWorkbenchGUI.PrimaryButtonStyle.fixedHeight);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.CommandControlHeight,
                DeucarianEditorWorkbenchGUI.SecondaryButtonStyle.fixedHeight);
            Assert.AreEqual(
                DeucarianEditorVisualShell.DeepBackground,
                DeucarianEditorWorkbenchGUI.MainBackgroundColor);
            Assert.AreEqual(
                DeucarianEditorVisualShell.MainPanel,
                DeucarianEditorWorkbenchGUI.SidebarBackgroundColor);
            Assert.AreEqual(
                DeucarianEditorVisualShell.NestedSurface,
                DeucarianEditorWorkbenchGUI.PanelBackgroundColor);
            Assert.AreEqual(
                DeucarianEditorVisualShell.HeaderPanel,
                DeucarianEditorWorkbenchGUI.HeaderPanelBackgroundColor);
            Assert.AreEqual(
                DeucarianEditorVisualShell.Border,
                DeucarianEditorWorkbenchGUI.PanelBorderColor);
            Assert.AreEqual(
                DeucarianEditorVisualShell.InteractiveBorder,
                DeucarianEditorWorkbenchGUI.InteractiveBorderColor);
            Assert.AreEqual(
                DeucarianEditorVisualShell.SubtleBorder,
                DeucarianEditorWorkbenchGUI.SeparatorColor);
            AssertColor(DeucarianEditorVisualShell.DeepBackground, 0.012f, 0.020f, 0.035f, 1f);
            AssertColor(DeucarianEditorVisualShell.MainPanel, 23f / 255f, 32f / 255f, 39f / 255f, 0.72f);
            AssertColor(DeucarianEditorVisualShell.NestedSurface, 32f / 255f, 47f / 255f, 56f / 255f, 0.62f);
            AssertColor(DeucarianEditorVisualShell.HeaderPanel, 35f / 255f, 52f / 255f, 61f / 255f, 0.68f);
            AssertColor(DeucarianEditorVisualShell.Border, 90f / 255f, 111f / 255f, 160f / 255f, 0.35f);
            AssertColor(DeucarianEditorVisualShell.InteractiveBorder, 59f / 255f, 166f / 255f, 154f / 255f, 0.55f);
            AssertColor(DeucarianEditorVisualShell.SubtleBorder, 90f / 255f, 111f / 255f, 160f / 255f, 0.24f);
            AssertColor(DeucarianEditorVisualShell.Text, 0.88f, 0.93f, 0.96f, 1f);
            AssertColor(DeucarianEditorVisualShell.MutedText, 0.58f, 0.68f, 0.75f, 1f);
        }

        private static void AssertFixedWallpaperLayer(VisualElement element, string expectedClass)
        {
            Assert.NotNull(element);
            Assert.IsTrue(element.ClassListContains(expectedClass));
            Assert.AreEqual(PickingMode.Ignore, element.pickingMode);
            Assert.AreEqual(Position.Absolute, element.style.position.value);
            Assert.AreEqual(0f, element.style.left.value.value);
            Assert.AreEqual(0f, element.style.right.value.value);
            Assert.AreEqual(0f, element.style.top.value.value);
            Assert.AreEqual(0f, element.style.bottom.value.value);
            Assert.AreEqual(ScaleMode.ScaleAndCrop, element.style.unityBackgroundScaleMode.value);
        }

        private static void AssertResponsiveClasses(
            VisualElement element,
            float width,
            PackageInstallerResponsiveMode expectedMode,
            string expectedSharedClass,
            string expectedLegacyClass)
        {
            Assert.AreEqual(
                expectedMode,
                PackageInstallerWindow.ApplyResponsiveClassesForTests(element, width));

            string[] sharedClasses =
            {
                DeucarianEditorResponsiveLayout.WideClass,
                DeucarianEditorResponsiveLayout.CompactClass,
                DeucarianEditorResponsiveLayout.NarrowClass
            };
            string[] legacyClasses =
            {
                "dpi-responsive--wide",
                "dpi-responsive--compact",
                "dpi-responsive--narrow"
            };

            Assert.AreEqual(
                1,
                sharedClasses.Count(element.ClassListContains),
                "Exactly one shared responsive class must be active.");
            Assert.AreEqual(
                1,
                legacyClasses.Count(element.ClassListContains),
                "Exactly one legacy responsive alias must be active.");
            Assert.IsTrue(element.ClassListContains(expectedSharedClass));
            Assert.IsTrue(element.ClassListContains(expectedLegacyClass));
        }

        private static string SerializeVisualTree(VisualElement root)
        {
            StringBuilder builder = new StringBuilder();
            AppendVisualTree(builder, root, 0);
            return NormalizeLines(builder.ToString());
        }

        private static void AppendVisualTree(StringBuilder builder, VisualElement element, int depth)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(' ', depth * 2);
            builder.Append(element.GetType().Name);
            builder.Append('|');
            builder.Append(string.IsNullOrEmpty(element.name) ? "-" : element.name);
            builder.Append('|');
            string classes = string.Join(",", element.GetClasses().Where(IsPackageVisualClass));
            builder.Append(string.IsNullOrEmpty(classes) ? "-" : classes);
            builder.Append('|');
            TextElement textElement = element as TextElement;
            builder.Append(textElement == null || string.IsNullOrEmpty(textElement.text) ? "-" : textElement.text);

            foreach (VisualElement child in element.Children())
            {
                AppendVisualTree(builder, child, depth + 1);
            }
        }

        private static bool IsPackageVisualClass(string className)
        {
            return className.StartsWith("deucarian-", StringComparison.Ordinal) ||
                   className.StartsWith("dpi-", StringComparison.Ordinal);
        }

        private static string SerializeCustomClasses(VisualElement element)
        {
            return string.Join(" ", element.GetClasses().Where(IsPackageVisualClass));
        }

        private static string NormalizeLines(string value)
        {
            return string.Join(
                "\n",
                (value ?? string.Empty)
                    .Replace("\r\n", "\n")
                    .Split('\n')
                    .Select(line => line.TrimEnd())
                    .SkipWhile(string.IsNullOrWhiteSpace)
                    .Reverse()
                    .SkipWhile(string.IsNullOrWhiteSpace)
                    .Reverse());
        }

        private static string ReadPackageFile(string packageId, string relativePath)
        {
            string assetPath = "Packages/" + packageId + "/" + relativePath.Replace('\\', '/');
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            Assert.NotNull(package, "Could not resolve package owning '" + assetPath + "'.");

            string fullPath = Path.Combine(
                package.resolvedPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(fullPath), "Expected package file at '" + fullPath + "'.");
            return File.ReadAllText(fullPath);
        }

        private static void AssertRuleValues(string css, string selector, params string[] nameValuePairs)
        {
            Assert.AreEqual(0, nameValuePairs.Length % 2, "CSS assertions must be name/value pairs.");
            IReadOnlyDictionary<string, string> declarations = ParseRule(css, selector);

            for (int index = 0; index < nameValuePairs.Length; index += 2)
            {
                string property = nameValuePairs[index];
                string expected = nameValuePairs[index + 1];
                Assert.IsTrue(
                    declarations.TryGetValue(property, out string actual),
                    "Selector '" + selector + "' did not declare '" + property + "'.");
                Assert.AreEqual(expected, actual, selector + " -> " + property);
            }
        }

        private static void AssertNoRuleSelector(string css, string selector)
        {
            Assert.IsFalse(
                EnumerateRules(css).Any(rule => rule.Selectors.Contains(selector)),
                "Package-local CSS must not redeclare shared selector '" + selector + "'.");
        }

        private static IReadOnlyDictionary<string, string> ParseRule(string css, string selector)
        {
            CssRule rule = EnumerateRules(css)
                .FirstOrDefault(candidate => candidate.Selectors.Contains(selector));
            Assert.NotNull(rule, "Could not find CSS selector '" + selector + "'.");

            Dictionary<string, string> declarations = new Dictionary<string, string>(StringComparer.Ordinal);
            MatchCollection matches = Regex.Matches(
                rule.Body,
                @"(?m)^\s*(?<name>[-\w]+)\s*:\s*(?<value>[^;]+);\s*$");

            foreach (Match declaration in matches)
            {
                declarations.Add(
                    declaration.Groups["name"].Value,
                    declaration.Groups["value"].Value.Trim());
            }

            return declarations;
        }

        private static IEnumerable<CssRule> EnumerateRules(string css)
        {
            string withoutComments = Regex.Replace(
                css ?? string.Empty,
                @"/\*.*?\*/",
                string.Empty,
                RegexOptions.Singleline);
            MatchCollection rules = Regex.Matches(
                withoutComments,
                @"(?s)(?<selectors>[^{}]+)\{(?<body>[^{}]*)\}");

            foreach (Match rule in rules)
            {
                string[] selectors = rule.Groups["selectors"].Value
                    .Split(',')
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .ToArray();
                yield return new CssRule(selectors, rule.Groups["body"].Value);
            }
        }

        private sealed class CssRule
        {
            public CssRule(IReadOnlyCollection<string> selectors, string body)
            {
                Selectors = selectors;
                Body = body ?? string.Empty;
            }

            public IReadOnlyCollection<string> Selectors { get; }

            public string Body { get; }
        }

        private static void InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, InstancePrivate);
            Assert.NotNull(method, "Missing private method '" + methodName + "'.");
            method.Invoke(target, arguments);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstancePrivate);
            Assert.NotNull(field, "Missing private field '" + fieldName + "'.");
            field.SetValue(target, value);
        }

        private static void AssertColor(Color actual, float red, float green, float blue, float alpha)
        {
            Assert.That(actual.r, Is.EqualTo(red).Within(0.000001f));
            Assert.That(actual.g, Is.EqualTo(green).Within(0.000001f));
            Assert.That(actual.b, Is.EqualTo(blue).Within(0.000001f));
            Assert.That(actual.a, Is.EqualTo(alpha).Within(0.000001f));
        }
    }
}
