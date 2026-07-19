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
        public void Window_UsesSharedLucideActionsStatusesAndResponsiveDialogs()
        {
            string source = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/PackageInstallerWindow.cs");

            Assert.That(source, Does.Contain("DeucarianEditorIcons.GetIconContent("));
            Assert.That(source, Does.Contain("DeucarianEditorWorkbenchGUI.DrawCompactIconAction("));
            Assert.That(source, Does.Contain("DeucarianEditorWorkbenchGUI.DrawStatusIconRow("));
            Assert.That(source, Does.Contain("DeucarianEditorDialog.Show("));
            Assert.That(source, Does.Contain("DeucarianEditorPackageHeader.CreateBrand("));
            Assert.That(source, Does.Contain("DeucarianEditorChrome.DrawBrandHeader("));
            Assert.That(source, Does.Not.Contain("EditorUtility.DisplayDialog"));
            Assert.That(source, Does.Not.Contain("GUILayout.Button("));
            Assert.That(source, Does.Not.Contain("GUI.Button("));
            Assert.That(source, Does.Not.Contain("\\u2713"));
            Assert.That(source, Does.Not.Contain("\\u25CB"));
        }

        [Test]
        public void Toolbar_UsesCanonicalLanesComposedActionsAndFixedGeometry()
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
                VisualElement toolbar = content.ElementAt(0);
                Assert.IsTrue(toolbar.ClassListContains(DeucarianEditorCommandBar.RootClass));
                Assert.IsTrue(toolbar.ClassListContains(
                    DeucarianEditorWorkbenchToolbar.StableActionLanesClass));
                Assert.IsFalse(toolbar.GetClasses().Any(className =>
                    className.StartsWith("dpi-view-toolbar", StringComparison.Ordinal)));
                Assert.AreEqual(3, toolbar.childCount);

                VisualElement leading = toolbar.ElementAt(0);
                Label summary = toolbar.ElementAt(1) as Label;
                VisualElement trailing = toolbar.ElementAt(2);
                Assert.IsTrue(leading.ClassListContains(
                    DeucarianEditorCommandBar.LeadingLaneClass));
                Assert.NotNull(summary);
                Assert.IsTrue(summary.ClassListContains(
                    DeucarianEditorCommandBar.SummaryLaneClass));
                Assert.AreEqual(WhiteSpace.NoWrap, summary.style.whiteSpace.value);
                Assert.AreEqual(Overflow.Hidden, summary.style.overflow.value);
                Assert.AreEqual(TextOverflow.Ellipsis, summary.style.textOverflow.value);
                Assert.IsTrue(trailing.ClassListContains(
                    DeucarianEditorCommandBar.TrailingLaneClass));

                Assert.AreEqual(1, leading.childCount);
                Assert.AreEqual(3, trailing.childCount);
                VisualElement viewSlot = leading.ElementAt(0);
                VisualElement channelSlot = trailing.ElementAt(0);
                VisualElement refreshSlot = trailing.ElementAt(1);
                VisualElement checkSlot = trailing.ElementAt(2);
                AssertReservedSlot(viewSlot, 152f);
                AssertReservedSlot(channelSlot, 184f);
                AssertReservedSlot(refreshSlot, 104f);
                AssertReservedSlot(checkSlot, 140f);

                AssertComposedAction(viewSlot.ElementAt(0) as Button, "Ecosystem Graph");
                AssertComposedAction(channelSlot.ElementAt(0) as Button, "Channel: Stable");
                AssertComposedAction(refreshSlot.ElementAt(0) as Button, "Refresh");
                AssertComposedAction(checkSlot.ElementAt(0) as Button, "Check Updates");
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
                DeucarianEditorResponsiveLayout.NarrowClass);
            AssertResponsiveClasses(
                root,
                900f,
                PackageInstallerResponsiveMode.Compact,
                DeucarianEditorResponsiveLayout.CompactClass);
            AssertResponsiveClasses(
                root,
                1180f,
                PackageInstallerResponsiveMode.Wide,
                DeucarianEditorResponsiveLayout.WideClass);
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
        public void OperationDrawerAndFooter_UseGenericFactoriesAndMetrics()
        {
            VisualElement collapsedDrawer = PackageInstallerWindow.CreateOperationDrawerForTests(
                expanded: false,
                report: "One line");
            Assert.IsTrue(collapsedDrawer.ClassListContains(
                DeucarianEditorWorkbenchSurfaces.DrawerCollapsedClass));
            Assert.AreEqual(0f, collapsedDrawer.style.height.value.value);
            Assert.AreEqual(0f, collapsedDrawer.style.minHeight.value.value);
            Assert.AreEqual(0f, collapsedDrawer.style.maxHeight.value.value);

            VisualElement expandedDrawer = PackageInstallerWindow.CreateOperationDrawerForTests(
                expanded: true,
                report: "One line");
            Assert.IsTrue(expandedDrawer.ClassListContains(
                DeucarianEditorWorkbenchSurfaces.DrawerExpandedClass));
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
                    "deucarian-workbench-operation-row deucarian-workbench-operation-row--header",
                    "deucarian-workbench-operation-row deucarian-workbench-operation-row--option",
                    "deucarian-workbench-operation-row deucarian-workbench-operation-row--message deucarian-workbench-operation-text--secondary deucarian-workbench-operation-drawer__message",
                    "deucarian-workbench-operation-row deucarian-workbench-operation-row--option"
                },
                drawerContent.Children().Select(SerializeCustomClasses).ToArray());
            Button retry = expandedDrawer.Q<Button>(
                PackageInstallerWindow.OperationDrawerRetryButtonName);
            AssertComposedAction(retry, "Retry");
            Assert.IsTrue(retry.ClassListContains(
                DeucarianEditorWorkbenchSurfaces.DrawerActionClass));

            VisualElement footer = PackageInstallerWindow.CreateOperationFooterForTests();
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.FooterHeight,
                footer.style.height.value.value);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.FooterHeight,
                footer.style.minHeight.value.value);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.FooterHeight,
                footer.style.maxHeight.value.value);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.FooterHorizontalPadding,
                footer.style.paddingLeft.value.value);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.FooterHorizontalPadding,
                footer.style.paddingRight.value.value);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.FooterVerticalPadding,
                footer.style.paddingTop.value.value);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.FooterVerticalPadding,
                footer.style.paddingBottom.value.value);
            Assert.IsTrue(footer.ClassListContains(
                DeucarianEditorWorkbenchSurfaces.FooterClass));
            CollectionAssert.AreEqual(
                new[]
                {
                    PackageInstallerWindow.OperationFooterStatusGroupName,
                    PackageInstallerWindow.OperationFooterSummaryName,
                    string.Empty,
                    string.Empty,
                    PackageInstallerWindow.OperationFooterVersionName
                },
                footer.Children().Select(element => element.name ?? string.Empty).ToArray());

            Button cancel = footer.Q<Button>(PackageInstallerWindow.OperationFooterCancelButtonName);
            Button details = footer.Q<Button>(PackageInstallerWindow.OperationFooterDetailsButtonName);
            Assert.AreEqual(124f, cancel.style.width.value.value);
            Assert.AreEqual(DisplayStyle.None, cancel.style.display.value);
            Assert.AreEqual(128f, details.style.width.value.value);
            AssertComposedAction(cancel, "Cancel");
            AssertComposedAction(details, "Show Details");
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

            Assert.That(installerSource, Does.Contain("DeucarianEditorCommandBar.Create("));
            Assert.That(installerSource, Does.Contain("DeucarianEditorCommandBar.CreateLanes(toolbar)"));
            Assert.That(installerSource, Does.Contain("DeucarianEditorCommandBar.CreateToggle("));
            Assert.That(installerSource, Does.Contain("DeucarianEditorCommandBar.CreateAction("));
            Assert.That(installerSource, Does.Contain("DeucarianEditorCommandBar.CreateReservedSlot("));
            Assert.That(installerSource, Does.Not.Contain("dpi-view-toolbar"));
            Assert.That(installerSource, Does.Not.Contain("dpi-responsive--"));

            foreach (string selector in new[]
                     {
                         ".deucarian-command-bar",
                         ".deucarian-command-bar__action",
                         ".deucarian-command-bar__summary",
                         ".deucarian-command-bar__reserved-slot",
                         ".deucarian-icon-text-button"
                     })
            {
                AssertNoRuleSelector(installerCss, selector);
            }

            AssertRuleValues(editorCss, ".deucarian-toolbar-row",
                "height", "46px",
                "min-height", "46px",
                "max-height", "46px",
                "padding-left", "10px",
                "padding-right", "10px",
                "padding-top", "8px",
                "padding-bottom", "8px");
            AssertRuleValues(editorCss, ".deucarian-command-bar__action",
                "height", "28px",
                "min-height", "28px",
                "max-height", "28px",
                "padding-left", "0",
                "padding-right", "0");
            AssertRuleValues(editorCss, ".deucarian-command-bar__summary",
                "height", "18px",
                "min-height", "18px",
                "max-height", "18px",
                "min-width", "0");
            AssertRuleValues(editorCss, ".deucarian-command-bar__reserved-slot",
                "height", "28px",
                "min-height", "28px",
                "max-height", "28px",
                "flex-shrink", "0");
            AssertRuleValues(editorCss, ".deucarian-icon-text-button",
                "padding-left", "8px",
                "padding-right", "8px",
                "padding-top", "0",
                "padding-bottom", "0");
        }

        [Test]
        public void GraphVisuals_ConsumeEditorOwnedSemanticThemeRoles()
        {
            string installerCss = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/UI/PackageInstaller/PackageInstallerGraph.uss");
            string editorCss = ReadPackageFile(
                "com.deucarian.editor",
                "Editor/Assets/Styles/DeucarianEditor.uss");
            string graphSource = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/UI/PackageInstaller/PackageEcosystemGraphView.cs");

            foreach (string role in new[]
                     {
                         "--deucarian-graph-canvas",
                         "--deucarian-graph-surface",
                         "--deucarian-graph-border-selected",
                         "--deucarian-graph-installed",
                         "--deucarian-graph-available",
                         "--deucarian-graph-update",
                         "--deucarian-graph-warning",
                         "--deucarian-graph-missing",
                         "--deucarian-graph-checking"
                     })
            {
                Assert.That(editorCss, Does.Contain(role), role + " must be owned by Deucarian Editor.");
                Assert.That(installerCss, Does.Contain("var(" + role + ")"), role + " must be consumed by the graph skin.");
            }

            Assert.That(graphSource, Does.Contain("DeucarianEditorGraphTheme.Installed"));
            Assert.That(graphSource, Does.Contain("DeucarianEditorGraphTheme.Available"));
            Assert.That(graphSource, Does.Contain("DeucarianEditorGraphTheme.Update"));
            Assert.That(graphSource, Does.Contain("DeucarianEditorGraphTheme.EdgeUnderlay"));
            Assert.That(graphSource, Does.Contain("DeucarianEditorPalette.Tideline"));
        }

        [Test]
        public void Stylesheets_VisibleSurfacesUseCanonicalPaddingExactlyOnce()
        {
            string installerCss = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/UI/PackageInstaller/PackageInstallerGraph.uss");

            foreach (string selector in new[]
                     {
                         ".dpi-global-channel-popup",
                         ".dpi-ecosystem-graph__header",
                         ".dpi-ecosystem-graph__empty-state",
                         ".dpi-graph-node",
                         ".dpi-graph-node--presentation-full",
                         ".dpi-graph-unrelated-summary"
                     })
            {
                AssertRuleValues(installerCss, selector,
                    "padding-left", "10px",
                    "padding-right", "10px",
                    "padding-top", "8px",
                    "padding-bottom", "8px");
            }

            // The graph header owns the surface inset. Its internal layout rows must
            // stay at zero padding so the canonical inset is never applied twice.
            foreach (string selector in new[]
                     {
                         ".dpi-ecosystem-graph__filter-row",
                         ".dpi-ecosystem-graph__breadcrumbs"
                     })
            {
                AssertRuleValues(installerCss, selector,
                    "padding-left", "0",
                    "padding-right", "0",
                    "padding-top", "0",
                    "padding-bottom", "0");
            }
        }

        [Test]
        public void Stylesheets_EmptyStateTextWrapsAndActionsGrowWithContent()
        {
            string installerCss = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/UI/PackageInstaller/PackageInstallerGraph.uss");
            string graphSource = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/UI/PackageInstaller/PackageEcosystemGraphView.cs");

            Assert.That(graphSource, Does.Contain(
                "_emptyState.RegisterCallback<GeometryChangedEvent>(HandleEmptyStateGeometryChanged);"));

            AssertRuleValues(installerCss, ".dpi-ecosystem-graph__empty-title",
                "align-self", "stretch",
                "flex-shrink", "0",
                "white-space", "normal");
            AssertRuleValues(installerCss, ".dpi-ecosystem-graph__empty-action",
                "height", "auto",
                "min-height", "24px",
                "max-width", "100%",
                "flex-shrink", "0",
                "padding-left", "8px",
                "padding-right", "8px",
                "padding-top", "3px",
                "padding-bottom", "3px",
                "white-space", "normal");
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
            string installerSource = ReadPackageFile(
                "com.deucarian.package-installer",
                "Editor/PackageInstallerWindow.cs");

            foreach (string genericSelector in new[]
                     {
                         ".deucarian-workbench-operation-surface",
                         ".deucarian-workbench-operation-drawer",
                         ".deucarian-workbench-operation-drawer--expanded",
                         ".deucarian-workbench-operation-drawer--collapsed",
                         ".deucarian-workbench-operation-content",
                         ".deucarian-workbench-operation-footer",
                         ".deucarian-workbench-operation-footer__action"
                     })
            {
                AssertNoRuleSelector(installerCss, genericSelector);
            }

            Assert.That(installerSource, Does.Contain(
                "DeucarianEditorWorkbenchSurfaces.CreateDrawer(false)"));
            Assert.That(installerSource, Does.Contain(
                "DeucarianEditorWorkbenchSurfaces.CreateFooter("));
            Assert.That(installerSource, Does.Contain(
                "DeucarianEditorWorkbenchSurfaces.AddFooterAction("));
            Assert.That(installerSource, Does.Not.Contain("dpi-operation"));
            StringAssert.Contains(".dpi-operation-surface", editorCss,
                "Editor retains the released alias as a compatibility shim.");

            AssertRuleValues(editorCss, ".deucarian-workbench-operation-surface",
                "--deucarian-workbench-operation-inline-padding", "10px",
                "--deucarian-workbench-operation-block-padding", "8px",
                "--deucarian-workbench-operation-footer-inline-padding", "10px",
                "--deucarian-workbench-operation-footer-block-padding", "0px",
                "--deucarian-workbench-operation-row-gap", "6px",
                "--deucarian-workbench-operation-control-gap", "8px",
                "--deucarian-workbench-operation-footer-height", "34px");
            AssertRuleValues(editorCss, ".deucarian-workbench-operation-drawer",
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
            AssertRuleValues(editorCss, ".deucarian-workbench-operation-drawer--expanded",
                "margin-bottom", "6px",
                "border-left-width", "1px",
                "border-right-width", "1px",
                "border-top-width", "1px",
                "border-bottom-width", "1px",
                "background-color", "var(--deucarian-panel-header)");
            AssertRuleValues(editorCss, ".deucarian-workbench-operation-footer",
                "height", "var(--deucarian-workbench-operation-footer-height)",
                "min-height", "var(--deucarian-workbench-operation-footer-height)",
                "max-height", "var(--deucarian-workbench-operation-footer-height)",
                "padding-left", "var(--deucarian-workbench-operation-footer-inline-padding)",
                "padding-right", "var(--deucarian-workbench-operation-footer-inline-padding)",
                "padding-top", "var(--deucarian-workbench-operation-footer-block-padding)",
                "padding-bottom", "var(--deucarian-workbench-operation-footer-block-padding)",
                "border-top-left-radius", "6px",
                "border-top-right-radius", "6px",
                "border-bottom-right-radius", "6px",
                "border-bottom-left-radius", "6px",
                "border-left-width", "1px",
                "border-right-width", "1px",
                "border-top-width", "1px",
                "border-bottom-width", "1px",
                "overflow", "hidden");
            AssertRuleValues(editorCss, ".deucarian-workbench-operation-footer__action",
                "width", "96px",
                "min-width", "96px",
                "max-width", "96px",
                "height", "28px",
                "min-height", "28px",
                "max-height", "28px",
                "margin-left", "0",
                "margin-right", "var(--deucarian-workbench-operation-control-gap)",
                "padding-left", "0",
                "padding-right", "0",
                "padding-top", "0",
                "padding-bottom", "0",
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
                         "_foldoutStyle = new GUIStyle(DeucarianEditorWorkbenchGUI.FoldoutStyle);",
                         "DeucarianEditorWorkbenchGUI.DrawPanel(title, content, options);",
                         "DeucarianEditorWorkbenchGUI.DrawSurface(rect, backgroundColor, borderColor);",
                         "DeucarianEditorWorkbenchGUI.DrawSeparator();",
                         "DeucarianEditorWorkbenchGUI.DrawStatusIconRow(",
                         "DeucarianEditorWorkbenchGUI.DrawKeyValueRow(label, value);",
                         "DeucarianEditorWorkbenchGUI.BeginEmbeddedPage("
                     })
            {
                Assert.That(installerSource, Does.Contain(declaration));
            }

            Assert.AreEqual(10, DeucarianEditorLayoutMetrics.SurfaceHorizontalPadding);
            Assert.AreEqual(8, DeucarianEditorLayoutMetrics.SurfaceVerticalPadding);
            Assert.AreEqual(8, DeucarianEditorLayoutMetrics.SurfaceSpacing);
            Assert.That(
                editorStylesSource,
                Does.Contain("DeucarianEditorLayoutMetrics.SurfaceHorizontalPadding"));
            Assert.That(
                editorStylesSource,
                Does.Contain("DeucarianEditorLayoutMetrics.SurfaceSpacing"));

            foreach (string declaration in new[]
                     {
                         "public const float DetailLabelWidth = 118f;",
                         "public const float ButtonHeight = DeucarianEditorLayoutMetrics.CommandControlHeight;",
                         "embeddedPageStyle = new GUIStyle",
                         "padding = new RectOffset(0, 0, 0, 0)",
                         "margin = new RectOffset(0, 0, 0, 0)",
                         "DeucarianEditorLayoutMetrics.PageHorizontalPadding",
                         "DeucarianEditorLayoutMetrics.PageTopPadding",
                         "DeucarianEditorLayoutMetrics.PageBottomPadding",
                         "DeucarianEditorLayoutMetrics.SurfaceHorizontalPadding",
                         "DeucarianEditorLayoutMetrics.SurfaceVerticalPadding",
                         "DeucarianEditorLayoutMetrics.SurfaceSpacing)",
                         "titleStyle.fontSize = 15;",
                         "primaryButtonStyle.fixedHeight = ButtonHeight;",
                         "secondaryButtonStyle.fixedHeight = ButtonHeight;"
                     })
            {
                Assert.That(workbenchSource, Does.Contain(declaration));
            }

            Assert.That(installerSource, Does.Not.Contain("_windowStyle"));
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

            Assert.AreEqual(10, DeucarianEditorLayoutMetrics.PageHorizontalPadding);
            Assert.AreEqual(8, DeucarianEditorLayoutMetrics.PageVerticalPadding);
            Assert.AreEqual(10, DeucarianEditorLayoutMetrics.SurfaceHorizontalPadding);
            Assert.AreEqual(8, DeucarianEditorLayoutMetrics.SurfaceVerticalPadding);
            Assert.AreEqual(10, DeucarianEditorLayoutMetrics.FooterHorizontalPadding);
            Assert.AreEqual(0, DeucarianEditorLayoutMetrics.FooterVerticalPadding);
            Assert.AreEqual(34, DeucarianEditorLayoutMetrics.FooterHeight);
            Assert.AreEqual(28, DeucarianEditorLayoutMetrics.CommandControlHeight);
            Assert.AreEqual(18, DeucarianEditorLayoutMetrics.TextLineHeight);
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
            Assert.AreEqual(0, DeucarianEditorWorkbenchGUI.EmbeddedPageStyle.padding.left);
            Assert.AreEqual(0, DeucarianEditorWorkbenchGUI.EmbeddedPageStyle.padding.right);
            Assert.AreEqual(0, DeucarianEditorWorkbenchGUI.EmbeddedPageStyle.padding.top);
            Assert.AreEqual(0, DeucarianEditorWorkbenchGUI.EmbeddedPageStyle.padding.bottom);
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
            if (DeucarianEditorTheme.IsDark)
            {
                AssertColor(DeucarianEditorVisualShell.DeepBackground, 27f / 255f, 26f / 255f, 24f / 255f, 1f);
                AssertColor(DeucarianEditorVisualShell.MainPanel, 37f / 255f, 36f / 255f, 33f / 255f, 0.88f);
                AssertColor(DeucarianEditorVisualShell.NestedSurface, 48f / 255f, 46f / 255f, 42f / 255f, 0.82f);
                AssertColor(DeucarianEditorVisualShell.HeaderPanel, 42f / 255f, 41f / 255f, 38f / 255f, 0.92f);
                AssertColor(DeucarianEditorVisualShell.Border, 98f / 255f, 186f / 255f, 182f / 255f, 0.24f);
                AssertColor(DeucarianEditorVisualShell.InteractiveBorder, 98f / 255f, 186f / 255f, 182f / 255f, 0.62f);
                AssertColor(DeucarianEditorVisualShell.SubtleBorder, 242f / 255f, 239f / 255f, 231f / 255f, 0.12f);
                AssertColor(DeucarianEditorVisualShell.Text, 242f / 255f, 239f / 255f, 231f / 255f, 1f);
                AssertColor(DeucarianEditorVisualShell.MutedText, 170f / 255f, 166f / 255f, 158f / 255f, 1f);
            }
            else
            {
                AssertColor(DeucarianEditorVisualShell.DeepBackground, 248f / 255f, 246f / 255f, 241f / 255f, 1f);
                AssertColor(DeucarianEditorVisualShell.MainPanel, 1f, 1f, 1f, 0.90f);
                AssertColor(DeucarianEditorVisualShell.NestedSurface, 242f / 255f, 239f / 255f, 231f / 255f, 0.88f);
                AssertColor(DeucarianEditorVisualShell.HeaderPanel, 1f, 1f, 1f, 0.94f);
                AssertColor(DeucarianEditorVisualShell.Border, 27f / 255f, 26f / 255f, 24f / 255f, 0.14f);
                AssertColor(DeucarianEditorVisualShell.InteractiveBorder, 15f / 255f, 98f / 255f, 106f / 255f, 0.58f);
                AssertColor(DeucarianEditorVisualShell.SubtleBorder, 27f / 255f, 26f / 255f, 24f / 255f, 0.09f);
                AssertColor(DeucarianEditorVisualShell.Text, 27f / 255f, 26f / 255f, 24f / 255f, 1f);
                AssertColor(DeucarianEditorVisualShell.MutedText, 121f / 255f, 118f / 255f, 111f / 255f, 1f);
            }
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

        private static void AssertReservedSlot(VisualElement slot, float expectedWidth)
        {
            Assert.NotNull(slot);
            Assert.IsTrue(slot.ClassListContains(
                DeucarianEditorCommandBar.ReservedSlotClass));
            Assert.AreEqual(expectedWidth, slot.style.width.value.value);
            Assert.AreEqual(expectedWidth, slot.style.minWidth.value.value);
            Assert.AreEqual(expectedWidth, slot.style.maxWidth.value.value);
            Assert.AreEqual(1, slot.childCount);
        }

        private static void AssertComposedAction(Button button, string expectedText)
        {
            Assert.NotNull(button);
            Assert.AreEqual(string.Empty, button.text);
            Assert.IsTrue(button.ClassListContains(DeucarianEditorIconTextButton.RootClass));
            VisualElement content = button.Q<VisualElement>(
                className: DeucarianEditorIconTextButton.ContentClass);
            Image icon = button.Q<Image>(
                className: DeucarianEditorIconTextButton.IconClass);
            VisualElement gap = button.Q<VisualElement>(
                className: DeucarianEditorIconTextButton.GapClass);
            Label label = button.Q<Label>(
                className: DeucarianEditorIconTextButton.LabelClass);
            Assert.NotNull(content);
            Assert.NotNull(icon);
            Assert.NotNull(icon.image);
            Assert.NotNull(gap);
            Assert.NotNull(label);
            Assert.AreEqual(expectedText, label.text);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.IconTextGap,
                gap.style.width.value.value);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.CommandControlHorizontalPadding,
                button.style.paddingLeft.value.value);
            Assert.AreEqual(
                DeucarianEditorLayoutMetrics.CommandControlHorizontalPadding,
                button.style.paddingRight.value.value);
        }

        private static void AssertResponsiveClasses(
            VisualElement element,
            float width,
            PackageInstallerResponsiveMode expectedMode,
            string expectedSharedClass)
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
            Assert.AreEqual(
                1,
                sharedClasses.Count(element.ClassListContains),
                "Exactly one shared responsive class must be active.");
            Assert.IsTrue(element.ClassListContains(expectedSharedClass));
            Assert.IsFalse(element.GetClasses().Any(className =>
                className.StartsWith("dpi-responsive--", StringComparison.Ordinal)));
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
