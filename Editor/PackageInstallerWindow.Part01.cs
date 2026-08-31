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


        private enum InstallerViewMode
        {
            EcosystemGraph,
            List
        }

        private enum SelectionKind
        {
            Package,
            Integration
        }

        internal enum PackageInstallerActionKind
        {
            None,
            CheckUpdates,
            UpdateAll,
            InstallAll
        }

        private enum VisualStatusKind
        {
            Installed,
            NotInstalled,
            UpdateAvailable,
            Failed,
            Busy,
            Info,
            Integration
        }

        internal readonly struct OperationLayoutMetrics
        {
            public const int InlinePadding = DeucarianEditorLayoutMetrics.SurfaceHorizontalPadding;
            public const int BlockPadding = DeucarianEditorLayoutMetrics.SurfaceVerticalPadding;
            public const int RowGap = 6;
            public const int ControlGap = 8;
            public const float FooterHeight = DeucarianEditorLayoutMetrics.FooterHeight;
            public const float DrawerMaxHeight = 220f;
        }

        private sealed class VisualStatus
        {
            public VisualStatus(string iconId, string label, VisualStatusKind kind)
            {
                IconId = string.IsNullOrWhiteSpace(iconId)
                    ? DeucarianEditorIconIds.Info
                    : iconId.Trim();
                Label = label ?? string.Empty;
                Kind = kind;
            }

            public string IconId { get; }

            public string Label { get; }

            public VisualStatusKind Kind { get; }
        }

        private sealed class OperationProgressView
        {
            public string Title = string.Empty;
            public string OperationName = string.Empty;
            public string CurrentItem = string.Empty;
            public string Message = string.Empty;
            public string ErrorMessage = string.Empty;
            public int CompletedSteps;
            public int TotalSteps;
            public int FailedSteps;
            public bool IsBusy;
            public IReadOnlyList<PackageInstallProgressItem> ProgressItems = Array.Empty<PackageInstallProgressItem>();
        }

        internal readonly struct PackageInstallerActionButtonState
        {
            public PackageInstallerActionButtonState(string label, bool enabled)
            {
                Label = label ?? string.Empty;
                Enabled = enabled;
            }

            public string Label { get; }

            public bool Enabled { get; }
        }

        internal readonly struct EcosystemOverviewAction
        {
            public EcosystemOverviewAction(PackageInstallerActionKind kind, string label)
            {
                Kind = kind;
                Label = label ?? string.Empty;
            }

            public PackageInstallerActionKind Kind { get; }

            public string Label { get; }
        }

        [MenuItem(InstallerMenuPath)]
        public static void Open()
        {
            PackageInstallerWindow window = GetWindow<PackageInstallerWindow>();
            window.titleContent = DeucarianEditorIcons.GetIconContent(
                DeucarianEditorIconIds.CreatePackage,
                WindowTitle,
                "Open the Deucarian Package Installer.");
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
            window.Show();
        }

        internal static string FormatEcosystemOverviewGroupStatusSummaryForTests(
            int installedCount,
            int notInstalledCount,
            int attentionCount,
            int unknownCount)
        {
            return FormatEcosystemOverviewGroupStatusSummary(
                new PackageGraphCategoryStatusSummary(
                    installedCount,
                    notInstalledCount,
                    attentionCount,
                    unknownCount));
        }

        internal static IReadOnlyList<PackageGraphNavigationRow> CreateEcosystemOverviewGroupNavigationRowsForTests(
            PackageGraphModel graph,
            PackageGraphNavigationState navigationState)
        {
            return CreateEcosystemOverviewGroupNavigationRows(graph, navigationState);
        }

        internal static PackageGraphNavigationState CreatePackageNavigationStateForTests(
            PackageGraphModel graph,
            string packageId)
        {
            return PackageGraphNavigationState.Package(packageId, GetGraphPackageGroupId(graph, packageId));
        }

        internal static PackageInstallerActionButtonState GetActionButtonStateForTests(
            PackageInstallerActionKind buttonKind,
            PackageInstallerActionKind activeActionKind,
            PackageInstallerActionKind cancelingActionKind,
            bool anyOperationBusy,
            bool hasPackagesWithUpdates)
        {
            return CreateActionButtonState(
                buttonKind,
                activeActionKind,
                cancelingActionKind,
                anyOperationBusy,
                hasPackagesWithUpdates);
        }

        internal static PackageInstallerResponsiveMode ResolveResponsiveModeForTests(float width)
        {
            return ResolveResponsiveMode(width);
        }

        internal static PackageInstallerResponsiveMode ApplyResponsiveClassesForTests(
            VisualElement element,
            float width)
        {
            return ApplyResponsiveClasses(element, width);
        }

        internal static float ResolveDetailsContentWidthForTests(
            float windowWidth,
            bool isEcosystemGraph,
            float graphDetailsContentWidth)
        {
            return ResolveDetailsContentWidth(
                windowWidth,
                isEcosystemGraph,
                graphDetailsContentWidth);
        }

        internal static bool ShouldStackDetailsActionsForTests(float detailsContentWidth)
        {
            return ShouldStackDetailsActions(detailsContentWidth);
        }

        internal static bool IsGraphNavigationRowKeyboardActivationForTests(
            bool hasKeyboardFocus,
            EventType eventType,
            KeyCode keyCode)
        {
            return IsGraphNavigationRowKeyboardActivation(
                hasKeyboardFocus,
                eventType,
                keyCode);
        }

        internal static void HandleGraphEscapeForTests(
            PackageGraphView graphView,
            Action fallbackBackNavigation)
        {
            HandleGraphEscape(graphView, fallbackBackNavigation);
        }

        internal static bool ShouldShowEcosystemAttentionForTests(int attentionCount)
        {
            return ShouldShowEcosystemAttention(attentionCount);
        }

        internal static IReadOnlyList<EcosystemOverviewAction> CreateEcosystemOverviewActionsForTests(
            int updateCount)
        {
            return CreateEcosystemOverviewActions(updateCount);
        }

        internal static string FormatGlobalChannelButtonLabelForTests(PackageChannelSelection selection)
        {
            return FormatGlobalChannelButtonLabel(selection);
        }

        internal static bool ShouldShowGlobalChannelResetForTests(PackageChannelSelection selection)
        {
            return ShouldShowGlobalChannelReset(selection);
        }

        internal static bool ShouldDrawGraphNavigationBeforeContextForTests(
            PackageInstallerResponsiveMode responsiveMode)
        {
            return ShouldDrawGraphNavigationBeforeContext(responsiveMode);
        }

        internal static VisualElement CreateOperationFooterForTests(bool expanded = false)
        {
            VisualElement footer = CreateOperationFooterRow(null);
            ApplyOperationFooterData(
                footer,
                VisualStatusKind.Installed,
                "Complete",
                "Last operation complete.",
                expanded,
                GetFooterVersionText());
            return footer;
        }

        internal static VisualElement CreateOperationDrawerForTests(
            bool expanded = true,
            string report = "Package operation completed.\nInstalled package.")
        {
            VisualElement drawer = CreateOperationDrawer(
                null,
                null,
                out ScrollView scrollView,
                out VisualElement content,
                out Label titleLabel,
                out Toggle verboseToggle,
                out Label verboseLabel,
                out Label messageLabel);
            ApplyOperationDrawerData(
                drawer,
                scrollView,
                content,
                titleLabel,
                verboseToggle,
                verboseLabel,
                messageLabel,
                expanded,
                false,
                report);
            return drawer;
        }

        internal static void SetOperationFooterExpandedForTests(VisualElement footer, bool expanded)
        {
            ApplyOperationFooterData(
                footer,
                VisualStatusKind.Installed,
                "Complete",
                "Last operation complete.",
                expanded,
                GetFooterVersionText());
        }

        internal static bool ShouldClearGraphSelectionForFilters(
            string selectedPackageId,
            string focusedPackageId,
            ISet<string> visiblePackageIds)
        {
            if (visiblePackageIds == null || !string.IsNullOrWhiteSpace(focusedPackageId))
            {
                return false;
            }

            bool selectionHidden = !string.IsNullOrWhiteSpace(selectedPackageId) &&
                                   !visiblePackageIds.Contains(selectedPackageId);
            return selectionHidden;
        }

        private void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;
            bool restoredAfterReload = PackageInstallerWindowReloadState.TryConsume(
                out PackageInstallerWindowReloadSnapshot reloadSnapshot);
            if (restoredAfterReload)
            {
                RestoreReloadSnapshot(reloadSnapshot);
            }

            _confirmationState = new PackageInstallerConfirmationState();
            _activeConfirmationWindow = null;
            titleContent = DeucarianEditorIcons.GetIconContent(
                DeucarianEditorIconIds.CreatePackage,
                WindowTitle,
                "Open the Deucarian Package Installer.");
            minSize = new Vector2(MinWindowWidth, MinWindowHeight);
            _viewMode = ResolveInstallerViewMode(_viewMode);

            _stateRepository = new PackageInstallerStateRepository();
            PackageChannelSelection projectChannelSelection = _stateRepository.GetProjectChannelSelection();
            _lastObservedProjectChannel = projectChannelSelection.Channel;
            _lastObservedProjectChannelChangedAtUtcTicks = projectChannelSelection.ChangedAtUtcTicks;
            _packageInstallService = new PackageInstallService();
            _packageDetectionService = new PackageDetectionService();
            _packageInstallService.ExactTargetAlreadyInstalled =
                _packageDetectionService.IsInstalledAtExactTargetAfterChange;
            _packageUpdateCheckService = new PackageUpdateCheckService(_packageDetectionService);
            _packageSampleImportService = new PackageSampleImportService();
            _packageSampleDiscoveryService = new PackageSampleDiscoveryService();
            _packageReverseDependencyResolver = new PackageReverseDependencyResolver();
            _packageDependencyInstaller = new PackageDependencyInstaller(
                _packageInstallService,
                _packageDetectionService);
            _packageDependencyInstaller.PreflightConfirmation = ConfirmContextualOperation;
            _packageDependencyInstaller.PreflightCompleted += HandlePreflightCompleted;
            _packageGraphBuilder = new PackageGraphBuilder(
                packageId => _packageDetectionService != null && _packageDetectionService.IsInstalled(packageId),
                GetSelectedChannel,
                packageDefinition => _packageUpdateCheckService != null
                    ? _packageUpdateCheckService.GetStatus(packageDefinition, GetSelectedChannel(packageDefinition))
                    : null);
            PackageRegistryProvider.RefreshRemote();
            if (!restoredAfterReload)
            {
                EnsureValidSelection();
            }
            _operationDetailsExpanded = EditorPrefs.GetBool(GetOperationDrawerPreferenceKey(), false);

            PackageRegistryProvider.RegistryChanged += HandleRegistryChanged;
            _packageInstallService.StateChanged += Repaint;
            _packageInstallService.StateChanged += RefreshGraphView;
            _packageInstallService.StateChanged += UpdateOperationFooter;
            _packageInstallService.InstallCompleted += HandlePackageInstallCompleted;
            _packageInstallService.QueueCompleted += HandlePackageOperationCompleted;
            _packageDetectionService.StateChanged += Repaint;
            _packageDetectionService.StateChanged += HandlePackageDetectionGraphStateChanged;
            _packageDetectionService.StateChanged += UpdateOperationFooter;
            _packageDetectionService.RefreshCompleted += HandlePackageDetectionRefreshCompleted;
            _packageUpdateCheckService.StateChanged += Repaint;
            _packageUpdateCheckService.StateChanged += HandlePackageUpdateGraphStateChanged;
            _packageUpdateCheckService.StateChanged += UpdateOperationFooter;
            _packageSampleImportService.StateChanged += Repaint;
            _packageSampleImportService.StateChanged += RefreshGraphView;
            _packageSampleImportService.StateChanged += UpdateOperationFooter;
            PackageInstallerActivityService.Changed += Repaint;
            PackageInstallerActivityService.Changed += UpdateOperationFooter;

            bool checkUpdatesAfterDetectionRefresh =
                !restoredAfterReload && ShouldCheckForUpdatesOnGraphOpen();
            _promptSavedOperationAfterDetectionRefresh = _packageInstallService.HasSavedOperation;

            if (checkUpdatesAfterDetectionRefresh)
            {
                QueueDeferredUpdateCheck(PackageInstallerActionKind.CheckUpdates);
            }

            _packageDetectionService.Refresh();
        }

        private void OnFocus()
        {
            RefreshExternalState("window focus");
        }
    }
}
