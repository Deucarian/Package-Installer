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

        private void OnDisable()
        {
            navigation?.Dispose();
            navigation = null;
            _workspace?.Dispose();
            _workspace = null;
            PageRoot.UnregisterCallback<KeyDownEvent>(HandleRootKeyDown);
            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
            DismissPendingConfirmation(refreshUi: false);

            if (_packageDependencyInstaller != null)
            {
                _packageDependencyInstaller.PreflightCompleted -= HandlePreflightCompleted;
                _packageDependencyInstaller.CancelPendingPreflight();
            }

            if (_packageInstallService != null)
            {
                _packageInstallService.StateChanged -= Repaint;
                _packageInstallService.StateChanged -= RefreshGraphView;
                _packageInstallService.StateChanged -= UpdateOperationFooter;
                _packageInstallService.InstallCompleted -= HandlePackageInstallCompleted;
                _packageInstallService.QueueCompleted -= HandlePackageOperationCompleted;
                _packageInstallService.Dispose();
            }

            if (_packageDetectionService != null)
            {
                _packageDetectionService.StateChanged -= Repaint;
                _packageDetectionService.StateChanged -= HandlePackageDetectionGraphStateChanged;
                _packageDetectionService.StateChanged -= UpdateOperationFooter;
                _packageDetectionService.RefreshCompleted -= HandlePackageDetectionRefreshCompleted;
                _packageDetectionService.Dispose();
            }

            if (_packageUpdateCheckService != null)
            {
                _packageUpdateCheckService.StateChanged -= Repaint;
                _packageUpdateCheckService.StateChanged -= HandlePackageUpdateGraphStateChanged;
                _packageUpdateCheckService.StateChanged -= UpdateOperationFooter;
                _packageUpdateCheckService.Dispose();
            }

            if (_packageSampleImportService != null)
            {
                _packageSampleImportService.StateChanged -= Repaint;
                _packageSampleImportService.StateChanged -= RefreshGraphView;
                _packageSampleImportService.StateChanged -= UpdateOperationFooter;
                _packageSampleImportService.Dispose();
            }

            PackageRegistryProvider.RegistryChanged -= HandleRegistryChanged;
            PackageInstallerActivityService.Changed -= Repaint;
            PackageInstallerActivityService.Changed -= UpdateOperationFooter;
            HideGlobalChannelOverridePopup();
            _stateRepository = null;
            PackageInstallerWindowReloadState.ClearForNormalDisable();
        }
        private void CreateGUI()
        {
            navigation?.Dispose();
            navigation = new DeucarianEditorPageSession(this, DeucarianToolIds.PackageInstaller, BuildPage);
        }

        internal static IDeucarianEditorPage CreatePage() =>
            DeucarianEditorWindowPages.Create<PackageInstallerWindow>(
                (window, root) => window.BuildPage(root), activate: (window, route) => window.OnFocus());

        private DeucarianEditorPageSession navigation;
        private VisualElement pageRoot;
        private VisualElement PageRoot => pageRoot ?? rootVisualElement;

        private void BuildPage(VisualElement root)
        {
            pageRoot = root;
            _workspace?.Dispose();
            PageRoot.Clear();
            _workspace = new InstallerWorkspace(this);
            VisualElement content = _workspace.View.Workspace.Content;
            _windowContentRoot = content;

            StyleSheet graphStyleSheet = DeucarianEditorUIResources.LoadStyleSheet(GraphStyleSheetPath);

            if (graphStyleSheet != null)
            {
                PageRoot.styleSheets.Add(graphStyleSheet);
            }

            PageRoot.RegisterCallback<KeyDownEvent>(HandleRootKeyDown);
            content.RegisterCallback<GeometryChangedEvent>(evt => ApplyResponsiveLayout(evt.newRect.width));
            _listViewContainerHost = _workspace.View.Collection;

            _graphModeContainer = new VisualElement();
            _graphModeContainer.AddToClassList("dpi-mode-container");
            _graphModeContainer.AddToClassList("dpi-graph-mode");

            _graphContentRow = new VisualElement();
            _graphContentRow.AddToClassList("dpi-graph-content-row");
            _graphModeContainer.Add(_graphContentRow);

            content.Add(_graphModeContainer);

            _operationDrawerContainer = CreateOperationDrawer(
                HandleVerboseConsoleLoggingChanged,
                RetryLatestActivity,
                out _operationDrawerScrollView,
                out _operationDrawerContent,
                out _operationDrawerTitleLabel,
                out _operationDrawerVerboseToggle,
                out _operationDrawerVerboseLabel,
                out _operationDrawerMessageLabel);
            _operationDrawerRetryButton = _operationDrawerContainer.Q<Button>(
                OperationDrawerRetryButtonName);
            content.Add(_operationDrawerContainer);

            _operationFooterContainer = CreateOperationFooterRow(
                () => SetOperationDetailsExpanded(!_operationDetailsExpanded),
                CancelCurrentContextualOperation);
            CacheOperationFooterElements(_operationFooterContainer);
            _workspace.View.Workspace.Footer.Clear();
            _workspace.View.Workspace.Footer.Add(_operationFooterContainer);

            SetViewMode(_viewMode);
            if (_hasPendingReloadCamera)
            {
                EnsureGraphView();
                _graphView.PrepareCameraRestoreAfterReload();
            }

            ApplyResponsiveLayout(position.width);
            if (_hasPendingReloadCamera)
            {
                _graphView.RestoreCameraAfterReload(_pendingReloadCamera);
                _hasPendingReloadCamera = false;
            }

            UpdateOperationFooter();
            RefreshGraphView("window initialized");
            _workspace.Refresh();
        }

        private void EnsureGraphView()
        {
            if (_graphView != null && _graphView.parent == _graphContentRow) return;
            _graphView = new PackageGraphView(
                HandleGraphPackageSelected, HandleGraphPackageAction, HandleGraphBackNavigation,
                HandleGraphRootFocused, HandleGraphGroupFocused, _visibilityFilterState,
                HandleVisibilityFilterChanged);
            _graphContentRow.Add(_graphView);
            _graphDetailsContainer = DeucarianEditorWorkspaceControls.Scroll("installer-graph-details");
            _nativeGraphDetails = new InstallerGraphDetails(this, _graphDetailsContainer);
            _graphDetailsContainer.AddToClassList("dpi-graph-details");
            _graphContentRow.Add(_graphDetailsContainer);
        }

        private void ApplyResponsiveLayout(float contentWidth)
        {
            if (_windowContentRoot == null)
            {
                return;
            }

            PackageInstallerResponsiveMode nextMode = ApplyResponsiveClasses(
                _windowContentRoot,
                contentWidth);
            _responsiveMode = nextMode;

            _graphView?.SetResponsiveMode(nextMode);
            PositionGlobalChannelOverridePopup();
        }

        private static PackageInstallerResponsiveMode ResolveResponsiveMode(float width)
        {
            return ToPackageInstallerResponsiveMode(
                DeucarianEditorResponsiveLayout.ResolveMode(width));
        }

        private static PackageInstallerResponsiveMode ApplyResponsiveClasses(
            VisualElement element,
            float width)
        {
            DeucarianEditorLayoutMode sharedMode =
                DeucarianEditorResponsiveLayout.ApplyResponsiveClasses(element, width);
            return ToPackageInstallerResponsiveMode(sharedMode);
        }

        private static PackageInstallerResponsiveMode ToPackageInstallerResponsiveMode(
            DeucarianEditorLayoutMode mode)
        {
            switch (mode)
            {
                case DeucarianEditorLayoutMode.Narrow:
                    return PackageInstallerResponsiveMode.Narrow;
                case DeucarianEditorLayoutMode.Compact:
                    return PackageInstallerResponsiveMode.Compact;
                case DeucarianEditorLayoutMode.Wide:
                default:
                    return PackageInstallerResponsiveMode.Wide;
            }
        }

        private static void ConfigureFixedWallpaper(VisualElement root)
        {
            ConfigureFixedWallpaper(root, null);
        }

        private static void ConfigureFixedWallpaper(VisualElement root, VisualElement wallpaperHost)
        {
            DeucarianEditorWindowChrome.ConfigureFixedWallpaper(root, wallpaperHost, WallpaperTopSafeFadeName);
        }

        internal static void ConfigureFixedWallpaperForTests(VisualElement root)
        {
            ConfigureFixedWallpaper(root);
        }

        internal static void ConfigureFixedWallpaperForTests(VisualElement root, VisualElement wallpaperHost)
        {
            ConfigureFixedWallpaper(root, wallpaperHost);
        }

        private static VisualElement CreateCommandSlot(float width, VisualElement content)
        {
            VisualElement slot = DeucarianEditorCommandBar.CreateReservedSlot(width);
            DeucarianEditorCommandBar.SetReservedContent(slot, content);
            return slot;
        }

        private Button CreateViewToggleButton(string text, InstallerViewMode viewMode)
        {
            return DeucarianEditorCommandBar.CreateToggle(
                text,
                () => SetViewMode(viewMode),
                false,
                viewMode == InstallerViewMode.EcosystemGraph
                    ? DeucarianEditorIconIds.Network
                    : DeucarianEditorIconIds.Details,
                "Show " + text + ".");
        }

        private static void SetViewToggleActive(VisualElement toggle, bool active)
        {
            DeucarianEditorCommandBar.SetActive(toggle, active);
        }

        private static InstallerViewMode[] GetEnabledInstallerViewModes()
        {
            return ListViewEnabled
                ? new[] { InstallerViewMode.EcosystemGraph, InstallerViewMode.List }
                : new[] { InstallerViewMode.EcosystemGraph };
        }

        private static string GetInstallerViewModeLabel(InstallerViewMode viewMode)
        {
            return viewMode == InstallerViewMode.List ? "List View" : "Ecosystem Graph";
        }

        private Button CreateGlobalChannelOverrideButton()
        {
            PackageChannelSelection selection = GetGlobalProjectChannelSelection();
            Button button = DeucarianEditorCommandBar.CreateAction(
                DeucarianEditorIconIds.GitBranch,
                PackageChannelPolicy.FormatGlobalChannelButtonLabel(selection),
                ToggleGlobalChannelOverridePopup,
                emphasized: true,
                PackageChannelPolicy.GetGlobalChannelButtonTooltip(selection));
            button.name = GlobalChannelOverrideButtonName;
            return button;
        }

        private Button CreateGraphActionButton(string text, Action action)
        {
            string iconId = string.Equals(text, "Refresh", StringComparison.Ordinal)
                ? DeucarianEditorIconIds.Refresh
                : DeucarianEditorIconIds.SearchCheck;
            return DeucarianEditorCommandBar.CreateAction(
                iconId,
                text,
                () => action?.Invoke(),
                false,
                text);
        }

        private void ToggleGlobalChannelOverridePopup()
        {
            if (IsGlobalChannelOverridePopupVisible())
            {
                HideGlobalChannelOverridePopup();
                return;
            }

            ShowGlobalChannelOverridePopup();
        }

        private void ShowGlobalChannelOverridePopup()
        {
            if (PageRoot == null || _graphGlobalChannelButton == null)
            {
                return;
            }

            if (_globalChannelPopup == null)
            {
                _globalChannelPopup = CreateGlobalChannelOverridePopup();
                PageRoot.Add(_globalChannelPopup);
            }

            UpdateGlobalChannelOverridePopup();
            PositionGlobalChannelOverridePopup();
            _globalChannelPopup.style.display = DisplayStyle.Flex;
            _globalChannelPopup.BringToFront();
            PageRoot.RegisterCallback<MouseDownEvent>(
                HandleGlobalChannelOverrideRootMouseDown,
                TrickleDown.TrickleDown);
            PageRoot.RegisterCallback<KeyDownEvent>(
                HandleGlobalChannelOverrideRootKeyDown,
                TrickleDown.TrickleDown);
        }
    }
}
