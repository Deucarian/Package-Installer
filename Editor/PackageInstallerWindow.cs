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
    internal enum PackageInstallerResponsiveMode
    {
        Wide,
        Compact,
        Narrow
    }

    internal enum PackageSourceMigrationAction
    {
        InstallSelectedGitUrl,
        OpenBootstrap
    }

    internal enum PackageGraphNavigationTargetKind
    {
        Overview,
        Group,
        Package
    }

    internal enum PackageOperationRecoveryDisposition
    {
        Prompt,
        AutoResume
    }

    internal sealed class PackageInstallerConfirmationState
    {
        private long _generation;

        internal bool IsPending { get; private set; }

        internal bool TryBegin(out long generation)
        {
            generation = 0;
            if (IsPending)
            {
                return false;
            }

            IsPending = true;
            generation = ++_generation;
            return true;
        }

        internal bool IsCurrent(long generation)
        {
            return IsPending && generation == _generation;
        }

        internal bool TryComplete(long generation)
        {
            if (!IsCurrent(generation))
            {
                return false;
            }

            IsPending = false;
            return true;
        }

        internal bool CancelPending()
        {
            if (!IsPending)
            {
                return false;
            }

            IsPending = false;
            _generation++;
            return true;
        }
    }

    internal readonly struct PackageGraphNavigationState
    {
        private PackageGraphNavigationState(
            PackageGraphNavigationTargetKind targetKind,
            string focusedPackageId,
            string focusedGroupId)
        {
            TargetKind = targetKind;
            FocusedPackageId = focusedPackageId ?? string.Empty;
            FocusedGroupId = focusedGroupId ?? string.Empty;
        }

        public PackageGraphNavigationTargetKind TargetKind { get; }

        public string FocusedPackageId { get; }

        public string FocusedGroupId { get; }

        public bool IsOverview =>
            TargetKind == PackageGraphNavigationTargetKind.Overview ||
            (string.IsNullOrWhiteSpace(FocusedPackageId) && string.IsNullOrWhiteSpace(FocusedGroupId));

        public static PackageGraphNavigationState Overview()
        {
            return new PackageGraphNavigationState(
                PackageGraphNavigationTargetKind.Overview,
                string.Empty,
                string.Empty);
        }

        public static PackageGraphNavigationState Group(string groupId)
        {
            return new PackageGraphNavigationState(
                PackageGraphNavigationTargetKind.Group,
                string.Empty,
                groupId);
        }

        public static PackageGraphNavigationState Package(string packageId, string groupId)
        {
            return new PackageGraphNavigationState(
                PackageGraphNavigationTargetKind.Package,
                packageId,
                groupId);
        }
    }

    internal readonly struct PackageGraphGroupNavigationRow
    {
        public PackageGraphGroupNavigationRow(
            string id,
            string displayName,
            string summary,
            PackageGraphCategoryStatusSummary statusSummary,
            string iconKey,
            string tooltip,
            bool isOverview,
            bool isSelected,
            bool hasAttention)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Summary = summary ?? string.Empty;
            StatusSummary = statusSummary;
            IconKey = string.IsNullOrWhiteSpace(iconKey) ? "package" : iconKey.Trim();
            Tooltip = tooltip ?? string.Empty;
            IsOverview = isOverview;
            IsSelected = isSelected;
            HasAttention = hasAttention || statusSummary.AttentionCount > 0;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Summary { get; }

        public PackageGraphCategoryStatusSummary StatusSummary { get; }

        public string IconKey { get; }

        public string Tooltip { get; }

        public bool IsOverview { get; }

        public bool IsSelected { get; }

        public bool HasAttention { get; }
    }

    internal sealed class PackageInstallerWindow : EditorWindow
    {
        private const string WindowTitle = "Package Installer";
        private const string BootstrapMenuPath = "Tools/Deucarian/Bootstrap/Open Bootstrapper";
        private const string BootstrapStableGitUrl = "https://github.com/Deucarian/Bootstrap.git#main";
        private const string BootstrapDevelopmentGitUrl = "https://github.com/Deucarian/Bootstrap.git#develop";
        private const float MinWindowWidth = 820f;
        private const float MinWindowHeight = 650f;
        private const float ViewActionSlotWidth = 152f;
        private const float ChannelActionSlotWidth = 184f;
        private const float RefreshActionSlotWidth = 104f;
        private const float CheckUpdatesActionSlotWidth = 140f;
        private const float SidebarWidth = 340f;
        private const float SidebarRowMinHeight = 94f;
        private const float SidebarRowMaxHeight = 150f;
        private const float DetailsActionsStackWidth = 460f;
        private const int OperationInlinePadding = OperationLayoutMetrics.InlinePadding;
        private const int OperationBlockPadding = OperationLayoutMetrics.BlockPadding;
        private const int OperationControlGap = OperationLayoutMetrics.ControlGap;
        private const int OperationRowGap = OperationLayoutMetrics.RowGap;
        private const float OperationDrawerMinHeight = 30f;
        private const float OperationDrawerMaxHeight = 152f;
        private const float OperationDrawerExpandedBaseHeight = 58f;
        private const float OperationDrawerExpandedMaxHeight = OperationLayoutMetrics.DrawerMaxHeight;
        private const float OperationFooterHeight = OperationLayoutMetrics.FooterHeight;
        internal const string OperationDrawerName = "package-installer-operation-drawer";
        internal const string OperationDrawerScrollViewName = "package-installer-operation-drawer-scroll-view";
        internal const string OperationDrawerContentName = "package-installer-operation-drawer-content";
        internal const string OperationDrawerTitleName = "package-installer-operation-drawer-title";
        internal const string OperationDrawerVerboseToggleName = "package-installer-operation-drawer-verbose-toggle";
        internal const string OperationDrawerVerboseLabelName = "package-installer-operation-drawer-verbose-label";
        internal const string OperationDrawerMessageName = "package-installer-operation-drawer-message";
        internal const string OperationDrawerRetryButtonName = "package-installer-operation-drawer-retry";
        internal const string OperationFooterRowName = "package-installer-operation-footer";
        internal const string OperationFooterStatusGroupName = "package-installer-operation-footer-status";
        internal const string OperationFooterStatusIconName = "package-installer-operation-footer-status-icon";
        internal const string OperationFooterStatusLabelName = "package-installer-operation-footer-status-label";
        internal const string OperationFooterSummaryName = "package-installer-operation-footer-summary";
        internal const string OperationFooterCancelButtonName = "package-installer-operation-footer-cancel";
        internal const string OperationFooterDetailsButtonName = "package-installer-operation-footer-details-toggle";
        internal const string OperationFooterVersionName = "package-installer-operation-footer-version";
        internal const string WallpaperTopSafeFadeName = "package-installer-wallpaper-top-safe-fade";
        internal const string GlobalChannelOverrideButtonName = "package-installer-global-channel-override";
        internal const string GlobalChannelOverridePopupName = "package-installer-global-channel-override-popup";
        internal const string GlobalChannelOverrideResetButtonName = "package-installer-global-channel-override-reset";
        private const string AdvancedFoldoutPreferencePrefix = "Deucarian.PackageInstaller.AdvancedFoldout.";
        private const string CategoryFoldoutPreferencePrefix = "Deucarian.PackageInstaller.CategoryFoldout.";
        private const string OperationDrawerPreferencePrefix = "Deucarian.PackageInstaller.OperationDrawer.";
        private const string GraphStyleSheetPath =
            "Packages/com.deucarian.package-installer/Editor/UI/PackageInstaller/PackageInstallerGraph.uss";
        private const string InstallerMenuPath = "Tools/Deucarian/Package Installer";
        private const float GlobalChannelOverridePopupWidth = 286f;
        private const float GlobalChannelOverridePopupMargin = 8f;
        private static readonly string[] GlobalChannelOptionLabels = { "Development", "Stable" };

        private enum InstallerViewMode
        {
            EcosystemGraph,
            List
        }

        private const InstallerViewMode DefaultInstallerViewMode = InstallerViewMode.EcosystemGraph;
        private static readonly bool ListViewEnabled = false;

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

        private PackageInstallService _packageInstallService;
        private PackageDetectionService _packageDetectionService;
        private PackageUpdateCheckService _packageUpdateCheckService;
        private PackageSampleImportService _packageSampleImportService;
        private PackageSampleDiscoveryService _packageSampleDiscoveryService;
        private PackageReverseDependencyResolver _packageReverseDependencyResolver;
        private PackageDependencyInstaller _packageDependencyInstaller;
        private PackageGraphBuilder _packageGraphBuilder;
        private PackageInstallerStateRepository _stateRepository;
        private readonly Dictionary<string, bool> _advancedFoldouts =
            new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _categoryFoldouts =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private Vector2 _sidebarScrollPosition;
        private Vector2 _detailsScrollPosition;
        private Vector2 _operationDetailsScrollPosition;
        private SelectionKind _selectionKind = SelectionKind.Package;
        private string _selectedPackageId = string.Empty;
        private PackageGraphNavigationState _graphNavigationState = PackageGraphNavigationState.Overview();
        private PackageInstallerWindowReloadSnapshot _pendingReloadSnapshot;
        private bool _reloadStatePendingValidation;
        private bool _hasPendingReloadCamera;
        private PackageGraphCameraState _pendingReloadCamera;
        private string _detailsPreviewedGraphGroupId = string.Empty;
        private PackageGraphModel _cachedPackageGraph;
        private PackageGraphModel _lastPackageGraph;
        private bool _graphModelCacheDirty = true;
        private string _graphModelCacheInvalidationReason = "initial load";
        private readonly PackageVisibilityFilterState _visibilityFilterState =
            new PackageVisibilityFilterState();
        private readonly HashSet<string> _pendingUpdateStatusInvalidationPackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _checkUpdatesAfterDetectionRefresh;
        private PackageInstallerActionKind _deferredUpdateCheckActionKind = PackageInstallerActionKind.None;
        private PackageInstallerActionKind _activeActionKind = PackageInstallerActionKind.None;
        private PackageInstallerActionKind _cancelingActionKind = PackageInstallerActionKind.None;
        private PackageInstallerConfirmationState _confirmationState =
            new PackageInstallerConfirmationState();
        private EditorWindow _activeConfirmationWindow;
        private bool _operationDetailsExpanded;
        private bool _promptSavedOperationAfterDetectionRefresh;
        private PackageOperationTerminalSnapshot _terminalOperationRetryAfterRefresh;
        private bool _plannerFailureRetryAfterRefresh;
        private InstallerViewMode _viewMode = DefaultInstallerViewMode;
        private PackageChannel _lastObservedProjectChannel = PackageChannel.Stable;
        private long _lastObservedProjectChannelChangedAtUtcTicks;

        private Button _listViewButton;
        private Button _graphViewButton;
        private Button _graphGlobalChannelButton;
        private Button _graphRefreshButton;
        private Button _graphCheckUpdatesButton;
        private VisualElement _graphGlobalChannelSlot;
        private VisualElement _graphRefreshSlot;
        private VisualElement _graphCheckUpdatesSlot;
        private Button _graphUpdateAllButton;
        private Button _graphInstallAllButton;
        private VisualElement _globalChannelPopup;
        private DropdownField _globalChannelDropdown;
        private Button _globalChannelResetButton;
        private Label _viewSummaryLabel;
        private VisualElement _listViewContainerHost;
        private VisualElement _graphModeContainer;
        private VisualElement _graphContentRow;
        private VisualElement _windowContentRoot;
        private IMGUIContainer _listViewContainer;
        private IMGUIContainer _graphDetailsContainer;
        private VisualElement _operationDrawerContainer;
        private ScrollView _operationDrawerScrollView;
        private VisualElement _operationDrawerContent;
        private Label _operationDrawerTitleLabel;
        private Toggle _operationDrawerVerboseToggle;
        private Label _operationDrawerVerboseLabel;
        private Label _operationDrawerMessageLabel;
        private Button _operationDrawerRetryButton;
        private VisualElement _operationFooterContainer;
        private VisualElement _operationFooterStatusGroup;
        private Image _operationFooterStatusIcon;
        private Label _operationFooterStatusLabel;
        private Label _operationFooterSummaryLabel;
        private Button _operationFooterDetailsButton;
        private Label _operationFooterVersionLabel;
        private PackageGraphView _graphView;
        private PackageInstallerResponsiveMode _responsiveMode = PackageInstallerResponsiveMode.Wide;

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

        internal static bool DefaultsToEcosystemGraphForTests => DefaultInstallerViewMode == InstallerViewMode.EcosystemGraph;

        internal static string MenuPathForTests => InstallerMenuPath;

        internal static IReadOnlyList<string> UserFacingMenuPathsForTests => new[] { InstallerMenuPath };

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

        internal static IReadOnlyList<PackageGraphGroupNavigationRow> CreateEcosystemOverviewGroupNavigationRowsForTests(
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

        internal static IReadOnlyList<string> ViewToggleOrderForTests =>
            GetEnabledInstallerViewModes().Select(GetInstallerViewModeLabel).ToArray();

        internal static bool ListViewRequestResolvesToEcosystemGraphForTests =>
            ResolveInstallerViewMode(InstallerViewMode.List) == InstallerViewMode.EcosystemGraph;

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

        internal static Vector2 MinWindowSizeForTests => new Vector2(MinWindowWidth, MinWindowHeight);

        internal static string PackageIdForTests => PackageInstallerRuntimeIdentity.PackageId;

        internal static string PackageVersionForTests => PackageInstallerRuntimeIdentity.Version;

        internal static float OperationFooterHeightForTests => OperationFooterHeight;

        internal static int OperationGridOuterPaddingForTests => OperationInlinePadding;

        internal static int OperationGridColumnGapForTests => OperationControlGap;

        internal static int OperationInlinePaddingForTests => OperationInlinePadding;

        internal static int OperationBlockPaddingForTests => OperationBlockPadding;

        internal static int OperationControlGapForTests => OperationControlGap;

        internal static int OperationRowGapForTests => OperationRowGap;

        internal static float OperationDrawerExpandedMaxHeightForTests => OperationDrawerExpandedMaxHeight;

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

        private void OnDisable()
        {
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
            VisualElement content = DeucarianEditorVisualShell.CreateWindowShell(rootVisualElement);

            if (content == null)
            {
                return;
            }

            _windowContentRoot = content;
            ConfigureFixedWallpaper(rootVisualElement, content);

            StyleSheet graphStyleSheet = DeucarianEditorUIResources.LoadStyleSheet(GraphStyleSheetPath);

            if (graphStyleSheet != null)
            {
                rootVisualElement.styleSheets.Add(graphStyleSheet);
            }

            rootVisualElement.RegisterCallback<KeyDownEvent>(HandleRootKeyDown);
            content.RegisterCallback<GeometryChangedEvent>(evt => ApplyResponsiveLayout(evt.newRect.width));
            content.Add(DeucarianEditorPackageHeader.CreateBrand(
                "Deucarian Package Installer",
                "Install, update, and compose the Deucarian package ecosystem."));
            BuildViewToolbar(content);

            _listViewContainerHost = new VisualElement();
            _listViewContainerHost.AddToClassList("dpi-mode-container");
            _listViewContainer = new IMGUIContainer(DrawListViewGui);
            _listViewContainer.style.flexGrow = 1f;
            _listViewContainerHost.Add(_listViewContainer);
            content.Add(_listViewContainerHost);

            _graphModeContainer = new VisualElement();
            _graphModeContainer.AddToClassList("dpi-mode-container");
            _graphModeContainer.AddToClassList("dpi-graph-mode");

            _graphContentRow = new VisualElement();
            _graphContentRow.AddToClassList("dpi-graph-content-row");
            _graphModeContainer.Add(_graphContentRow);

            _graphView = new PackageGraphView(
                HandleGraphPackageSelected,
                HandleGraphPackageAction,
                HandleGraphBackNavigation,
                HandleGraphRootFocused,
                HandleGraphGroupFocused,
                _visibilityFilterState,
                HandleVisibilityFilterChanged);
            _graphContentRow.Add(_graphView);

            _graphDetailsContainer = new IMGUIContainer(DrawGraphDetailsGui);
            _graphDetailsContainer.AddToClassList("dpi-graph-details");
            _graphContentRow.Add(_graphDetailsContainer);

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
            content.Add(_operationFooterContainer);

            SetViewMode(_viewMode);
            if (_hasPendingReloadCamera)
            {
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

        private void BuildViewToolbar(VisualElement content)
        {
            VisualElement toolbar = DeucarianEditorCommandBar.Create(
                DeucarianEditorWorkbenchToolbarLayout.StableActionLanes);
            toolbar.name = null;
            DeucarianEditorCommandBarLanes lanes =
                DeucarianEditorCommandBar.CreateLanes(toolbar);

            foreach (InstallerViewMode viewMode in GetEnabledInstallerViewModes())
            {
                Button viewButton = CreateViewToggleButton(GetInstallerViewModeLabel(viewMode), viewMode);

                if (viewMode == InstallerViewMode.EcosystemGraph)
                {
                    _graphViewButton = viewButton;
                }
                else
                {
                    _listViewButton = viewButton;
                }

                VisualElement viewSlot = DeucarianEditorCommandBar.CreateReservedSlot(
                    ViewActionSlotWidth);
                DeucarianEditorCommandBar.SetReservedContent(viewSlot, viewButton);
                lanes.Leading.Add(viewSlot);
            }

            _viewSummaryLabel = lanes.Summary;
            _viewSummaryLabel.tooltip = string.Empty;
            _viewSummaryLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _viewSummaryLabel.style.overflow = Overflow.Hidden;
            _viewSummaryLabel.style.textOverflow = TextOverflow.Ellipsis;

            _graphGlobalChannelButton = CreateGlobalChannelOverrideButton();
            _graphRefreshButton = CreateGraphActionButton("Refresh", RefreshPackages);
            _graphCheckUpdatesButton = CreateGraphActionButton("Check Updates", () => HandleActionButton(PackageInstallerActionKind.CheckUpdates));

            _graphGlobalChannelSlot = CreateCommandSlot(
                ChannelActionSlotWidth,
                _graphGlobalChannelButton);
            _graphRefreshSlot = CreateCommandSlot(
                RefreshActionSlotWidth,
                _graphRefreshButton);
            _graphCheckUpdatesSlot = CreateCommandSlot(
                CheckUpdatesActionSlotWidth,
                _graphCheckUpdatesButton);
            lanes.Trailing.Add(_graphGlobalChannelSlot);
            lanes.Trailing.Add(_graphRefreshSlot);
            lanes.Trailing.Add(_graphCheckUpdatesSlot);

            content.Add(toolbar);
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
                FormatGlobalChannelButtonLabel(selection),
                ToggleGlobalChannelOverridePopup,
                emphasized: true,
                GetGlobalChannelButtonTooltip(selection));
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
            if (rootVisualElement == null || _graphGlobalChannelButton == null)
            {
                return;
            }

            if (_globalChannelPopup == null)
            {
                _globalChannelPopup = CreateGlobalChannelOverridePopup();
                rootVisualElement.Add(_globalChannelPopup);
            }

            UpdateGlobalChannelOverridePopup();
            PositionGlobalChannelOverridePopup();
            _globalChannelPopup.style.display = DisplayStyle.Flex;
            _globalChannelPopup.BringToFront();
            rootVisualElement.RegisterCallback<MouseDownEvent>(
                HandleGlobalChannelOverrideRootMouseDown,
                TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<KeyDownEvent>(
                HandleGlobalChannelOverrideRootKeyDown,
                TrickleDown.TrickleDown);
        }

        private VisualElement CreateGlobalChannelOverridePopup()
        {
            VisualElement popup = new VisualElement { name = GlobalChannelOverridePopupName };
            popup.AddToClassList("dpi-global-channel-popup");
            popup.style.display = DisplayStyle.None;

            VisualElement title = DeucarianEditorIconTextButton.CreateContent(
                DeucarianEditorIconIds.GitBranch,
                "Global Channel Override",
                true);
            title.AddToClassList("dpi-global-channel-popup__title");
            popup.Add(title);

            Label message = new Label(
                "This will override all package states. An individual package dropdown can take over again when changed later.");
            message.AddToClassList("dpi-global-channel-popup__message");
            popup.Add(message);

            _globalChannelDropdown = new DropdownField
            {
                label = "Channel",
                choices = GlobalChannelOptionLabels.ToList()
            };
            _globalChannelDropdown.AddToClassList("dpi-global-channel-popup__dropdown");
            popup.Add(_globalChannelDropdown);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("dpi-global-channel-popup__actions");

            Button applyButton = DeucarianEditorIconTextButton.Create(
                DeucarianEditorIconIds.Check,
                "Apply Override",
                ApplyGlobalChannelOverrideFromPopup,
                "Apply the selected project-wide package channel override.");
            applyButton.AddToClassList("dpi-global-channel-popup__apply");
            applyButton.AddToClassList("dpi-global-channel-popup__apply--primary");

            _globalChannelResetButton = DeucarianEditorIconTextButton.Create(
                DeucarianEditorIconIds.Reset,
                "Use Default",
                ClearGlobalChannelOverrideFromPopup,
                "Remove the explicit project override and use inherited/default channel selection.");
            _globalChannelResetButton.name = GlobalChannelOverrideResetButtonName;
            _globalChannelResetButton.AddToClassList("dpi-global-channel-popup__apply");
            actions.Add(_globalChannelResetButton);
            actions.Add(applyButton);

            popup.Add(actions);
            return popup;
        }

        private void UpdateGlobalChannelOverridePopup()
        {
            if (_globalChannelDropdown == null)
            {
                return;
            }

            PackageChannelSelection selection = GetGlobalProjectChannelSelection();
            _globalChannelDropdown.SetValueWithoutNotify(GetChannelLabel(selection.Channel));

            if (_globalChannelResetButton != null)
            {
                bool showReset = ShouldShowGlobalChannelReset(selection);
                _globalChannelResetButton.style.display = showReset
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                _globalChannelResetButton.SetEnabled(showReset);
            }
        }

        private PackageChannel GetGlobalProjectChannel()
        {
            return GetGlobalProjectChannelSelection().Channel;
        }

        private PackageChannelSelection GetGlobalProjectChannelSelection()
        {
            return _stateRepository != null
                ? _stateRepository.GetProjectChannelSelection()
                : PackageChannelSelection.None;
        }

        private void UpdateGlobalChannelOverrideButton()
        {
            if (_graphGlobalChannelButton == null)
            {
                return;
            }

            PackageChannelSelection selection = GetGlobalProjectChannelSelection();
            DeucarianEditorCommandBar.SetText(
                _graphGlobalChannelButton,
                FormatGlobalChannelButtonLabel(selection));
            _graphGlobalChannelButton.tooltip = GetGlobalChannelButtonTooltip(selection);
        }

        private void SetGlobalChannelOverride(PackageChannel channel)
        {
            PackageChannel safeChannel = channel == PackageChannel.Development
                ? PackageChannel.Development
                : PackageChannel.Stable;

            _stateRepository?.SetProjectChannel(safeChannel);

            PackageChannelSelection projectChannelSelection = _stateRepository != null
                ? _stateRepository.GetProjectChannelSelection()
                : PackageChannelSelection.Create(safeChannel, DateTime.UtcNow.Ticks);
            _lastObservedProjectChannel = projectChannelSelection.Channel;
            _lastObservedProjectChannelChangedAtUtcTicks = projectChannelSelection.ChangedAtUtcTicks;

            _packageUpdateCheckService?.InvalidateAll();
            InvalidateGraphModelCache("global channel override changed");
            UpdateGlobalChannelOverrideButton();
            UpdateGlobalChannelOverridePopup();
            RefreshGraphView("global channel override changed");
            Repaint();
        }

        private void ClearGlobalChannelOverrideFromPopup()
        {
            _stateRepository?.ClearProjectChannel();

            PackageChannelSelection selection = GetGlobalProjectChannelSelection();
            _lastObservedProjectChannel = selection.Channel;
            _lastObservedProjectChannelChangedAtUtcTicks = selection.ChangedAtUtcTicks;

            _packageUpdateCheckService?.InvalidateAll();
            InvalidateGraphModelCache("global channel override cleared");
            UpdateGlobalChannelOverrideButton();
            UpdateGlobalChannelOverridePopup();
            RefreshGraphView("global channel override cleared");
            HideGlobalChannelOverridePopup();
            Repaint();
        }

        private static string FormatGlobalChannelButtonLabel(PackageChannelSelection selection)
        {
            return (selection.HasValue ? "Override: " : "Channel: ") +
                   GetChannelLabel(selection.Channel);
        }

        private static string GetGlobalChannelButtonTooltip(PackageChannelSelection selection)
        {
            return selection.HasValue
                ? "An explicit project channel override is active. Open to change it or return to the inherited/default channel."
                : "No explicit project override is active. Open to set a project channel override.";
        }

        private static bool ShouldShowGlobalChannelReset(PackageChannelSelection selection)
        {
            return selection.HasValue;
        }

        private static PackageChannel ParseChannelLabel(string label)
        {
            return string.Equals(label, GetChannelLabel(PackageChannel.Development), StringComparison.OrdinalIgnoreCase)
                ? PackageChannel.Development
                : PackageChannel.Stable;
        }

        private void PositionGlobalChannelOverridePopup()
        {
            if (_globalChannelPopup == null ||
                _graphGlobalChannelButton == null ||
                rootVisualElement == null)
            {
                return;
            }

            Rect rootBounds = rootVisualElement.worldBound;
            Rect buttonBounds = _graphGlobalChannelButton.worldBound;
            float maxLeft = Mathf.Max(
                GlobalChannelOverridePopupMargin,
                rootBounds.width - GlobalChannelOverridePopupWidth - GlobalChannelOverridePopupMargin);
            float left = Mathf.Clamp(
                buttonBounds.xMin - rootBounds.xMin,
                GlobalChannelOverridePopupMargin,
                maxLeft);
            float top = Mathf.Max(
                GlobalChannelOverridePopupMargin,
                buttonBounds.yMax - rootBounds.yMin + 5f);

            _globalChannelPopup.style.left = left;
            _globalChannelPopup.style.top = top;
            _globalChannelPopup.style.width = GlobalChannelOverridePopupWidth;
        }

        private void HideGlobalChannelOverridePopup()
        {
            if (_globalChannelPopup != null)
            {
                _globalChannelPopup.style.display = DisplayStyle.None;
            }

            if (rootVisualElement != null)
            {
                rootVisualElement.UnregisterCallback<MouseDownEvent>(
                    HandleGlobalChannelOverrideRootMouseDown,
                    TrickleDown.TrickleDown);
                rootVisualElement.UnregisterCallback<KeyDownEvent>(
                    HandleGlobalChannelOverrideRootKeyDown,
                    TrickleDown.TrickleDown);
            }
        }

        private bool IsGlobalChannelOverridePopupVisible()
        {
            return _globalChannelPopup != null &&
                   _globalChannelPopup.style.display.value == DisplayStyle.Flex;
        }

        private void HandleGlobalChannelOverrideRootMouseDown(MouseDownEvent evt)
        {
            VisualElement target = evt.target as VisualElement;

            if (IsElementOrDescendant(_globalChannelPopup, target) ||
                IsElementOrDescendant(_graphGlobalChannelButton, target))
            {
                return;
            }

            HideGlobalChannelOverridePopup();
        }

        private void HandleGlobalChannelOverrideRootKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            HideGlobalChannelOverridePopup();
            evt.StopPropagation();
        }

        private void ApplyGlobalChannelOverrideFromPopup()
        {
            PackageChannel channel = ParseChannelLabel(
                _globalChannelDropdown != null
                    ? _globalChannelDropdown.value
                    : GetChannelLabel(GetGlobalProjectChannel()));
            SetGlobalChannelOverride(channel);
            HideGlobalChannelOverridePopup();
        }

        private static bool IsElementOrDescendant(VisualElement root, VisualElement target)
        {
            for (VisualElement current = target; current != null; current = current.parent)
            {
                if (current == root)
                {
                    return true;
                }
            }

            return false;
        }

        private static VisualElement CreateOperationDrawer(
            Action<bool> verboseLoggingChanged,
            Action retryAction,
            out ScrollView scrollView,
            out VisualElement content,
            out Label titleLabel,
            out Toggle verboseToggle,
            out Label verboseLabel,
            out Label messageLabel)
        {
            DeucarianEditorWorkbenchDrawer sharedDrawer =
                DeucarianEditorWorkbenchSurfaces.CreateDrawer(false);
            VisualElement drawer = sharedDrawer.Root;
            drawer.name = OperationDrawerName;

            scrollView = sharedDrawer.ScrollView;
            scrollView.name = OperationDrawerScrollViewName;
            scrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            content = sharedDrawer.Content;
            content.name = OperationDrawerContentName;

            VisualElement header = DeucarianEditorWorkbenchSurfaces.CreateRow(
                DeucarianEditorWorkbenchSurfaces.HeaderRowClass);
            content.Add(header);

            VisualElement titleContent = DeucarianEditorIconTextButton.CreateContent(
                DeucarianEditorIconIds.Activity,
                "Last Operation Summary",
                true);
            titleLabel = titleContent.Q<Label>(
                className: DeucarianEditorIconTextButton.LabelClass);
            titleLabel.name = OperationDrawerTitleName;
            titleLabel.AddToClassList(DeucarianEditorWorkbenchSurfaces.PrimaryTextClass);
            titleLabel.AddToClassList("deucarian-workbench-operation-drawer__title");
            titleLabel.style.color = DeucarianEditorVisualShell.Text;
            header.Add(titleContent);

            VisualElement optionRow = DeucarianEditorWorkbenchSurfaces.CreateRow(
                DeucarianEditorWorkbenchSurfaces.OptionRowClass);
            content.Add(optionRow);

            Toggle localVerboseToggle = new Toggle { name = OperationDrawerVerboseToggleName };
            localVerboseToggle.AddToClassList(
                "deucarian-workbench-operation-drawer__toggle");
            localVerboseToggle.tooltip = "Send normal Package Installer info messages to the Unity Console. Warnings and errors are always logged.";
            if (verboseLoggingChanged != null)
            {
                localVerboseToggle.RegisterValueChangedCallback(evt => verboseLoggingChanged(evt.newValue));
            }
            optionRow.Add(localVerboseToggle);
            verboseToggle = localVerboseToggle;

            VisualElement verboseContent = DeucarianEditorIconTextButton.CreateContent(
                DeucarianEditorIconIds.Logging,
                "Verbose Console Logging",
                true);
            verboseLabel = verboseContent.Q<Label>(
                className: DeucarianEditorIconTextButton.LabelClass);
            verboseLabel.name = OperationDrawerVerboseLabelName;
            verboseLabel.AddToClassList(DeucarianEditorWorkbenchSurfaces.SecondaryTextClass);
            verboseLabel.AddToClassList(
                "deucarian-workbench-operation-drawer__option-label");
            verboseLabel.tooltip = localVerboseToggle.tooltip;
            verboseLabel.style.color = DeucarianEditorVisualShell.MutedText;
            verboseLabel.RegisterCallback<ClickEvent>(_ =>
            {
                localVerboseToggle.value = !localVerboseToggle.value;
            });
            optionRow.Add(verboseContent);

            Label localMessageLabel = new Label("No detailed operation report is available.")
            {
                name = OperationDrawerMessageName
            };
            localMessageLabel.AddToClassList(DeucarianEditorWorkbenchSurfaces.RowClass);
            localMessageLabel.AddToClassList(DeucarianEditorWorkbenchSurfaces.MessageRowClass);
            localMessageLabel.AddToClassList(DeucarianEditorWorkbenchSurfaces.SecondaryTextClass);
            localMessageLabel.AddToClassList(
                "deucarian-workbench-operation-drawer__message");
            localMessageLabel.style.color = DeucarianEditorVisualShell.MutedText;
            content.Add(localMessageLabel);
            messageLabel = localMessageLabel;

            VisualElement reportActions = DeucarianEditorWorkbenchSurfaces.CreateRow(
                DeucarianEditorWorkbenchSurfaces.OptionRowClass);
            Button retryButton = DeucarianEditorWorkbenchSurfaces.CreateDrawerAction(
                DeucarianEditorIconIds.Refresh,
                "Retry",
                retryAction,
                "Retry the latest failed or canceled activity.");
            retryButton.name = OperationDrawerRetryButtonName;
            retryButton.style.display = DisplayStyle.None;
            reportActions.Add(retryButton);
            Button copyDetailsButton = DeucarianEditorWorkbenchSurfaces.CreateDrawerAction(
                DeucarianEditorIconIds.Copy,
                "Copy details",
                () => GUIUtility.systemCopyBuffer = localMessageLabel.text ?? string.Empty,
                "Copy the chronological operation report to the clipboard.");
            reportActions.Add(copyDetailsButton);
            content.Add(reportActions);

            return drawer;
        }

        private void HandleVerboseConsoleLoggingChanged(bool enabled)
        {
            if (PackageInstallerLoggingPreferences.VerboseConsoleLogging == enabled)
            {
                return;
            }

            PackageInstallerLoggingPreferences.VerboseConsoleLogging = enabled;
            RefreshOperationDrawerContent();
        }

        private void RefreshOperationDrawerContent()
        {
            ApplyOperationDrawerData(
                _operationDrawerContainer,
                _operationDrawerScrollView,
                _operationDrawerContent,
                _operationDrawerTitleLabel,
                _operationDrawerVerboseToggle,
                _operationDrawerVerboseLabel,
                _operationDrawerMessageLabel,
                _operationDetailsExpanded,
                PackageInstallerLoggingPreferences.VerboseConsoleLogging,
                GetOperationDrawerReportText());
            UpdateActivityRetryButton();
        }

        private void UpdateActivityRetryButton()
        {
            if (_operationDrawerRetryButton == null)
            {
                return;
            }

            PackageInstallerActivityEntry latest = PackageInstallerActivityService.Latest;
            PackageInstallerRetryKind retryKind = ResolveContextualRetryKind(
                latest,
                _packageInstallService?.TerminalOperationSnapshot);
            if (retryKind == PackageInstallerRetryKind.ReplanOperation &&
                (_packageDependencyInstaller == null ||
                 !_packageDependencyInstaller.CanRetryLastPlannerFailure))
            {
                retryKind = PackageInstallerRetryKind.None;
            }
            ApplyContextualRetryButtonState(
                _operationDrawerRetryButton,
                retryKind,
                IsAnyOperationBusy());
        }

        internal static void ApplyContextualRetryButtonStateForTests(
            Button retryButton,
            PackageInstallerRetryKind retryKind,
            bool isBusy)
        {
            ApplyContextualRetryButtonState(retryButton, retryKind, isBusy);
        }

        private static void ApplyContextualRetryButtonState(
            Button retryButton,
            PackageInstallerRetryKind retryKind,
            bool isBusy)
        {
            if (retryButton == null)
            {
                return;
            }

            bool canRetry = retryKind != PackageInstallerRetryKind.None && !isBusy;
            string text = retryKind == PackageInstallerRetryKind.RestartOperation
                ? "Retry package operation"
                : retryKind == PackageInstallerRetryKind.ReplanOperation
                    ? "Retry package plan"
                    : "Retry";
            DeucarianEditorIconTextButton.SetText(retryButton, text);
            DeucarianEditorIconTextButton.SetIcon(
                retryButton,
                GetRetryIconId(retryKind));
            retryButton.tooltip = retryKind == PackageInstallerRetryKind.RestartOperation
                ? "Refresh installed and registry state, then replan the affected package operation."
                : retryKind == PackageInstallerRetryKind.ReplanOperation
                    ? "Rebuild the failed package plan from the current registry and installed state."
                    : "Retry the latest failed or canceled activity.";
            retryButton.style.display = canRetry
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            retryButton.SetEnabled(canRetry);
        }

        private static string GetRetryIconId(PackageInstallerRetryKind retryKind)
        {
            switch (retryKind)
            {
                case PackageInstallerRetryKind.CheckUpdates:
                    return DeucarianEditorIconIds.SearchCheck;
                case PackageInstallerRetryKind.ImportSample:
                    return DeucarianEditorIconIds.Sample;
                case PackageInstallerRetryKind.ResumeOperation:
                    return DeucarianEditorIconIds.Play;
                case PackageInstallerRetryKind.ReplanOperation:
                    return DeucarianEditorIconIds.Puzzle;
                case PackageInstallerRetryKind.RestartOperation:
                case PackageInstallerRetryKind.Refresh:
                default:
                    return DeucarianEditorIconIds.Refresh;
            }
        }

        private void RetryLatestActivity()
        {
            PackageInstallerActivityEntry latest = PackageInstallerActivityService.Latest;
            PackageOperationTerminalSnapshot snapshot =
                _packageInstallService?.TerminalOperationSnapshot;
            PackageInstallerRetryKind retryKind = ResolveContextualRetryKind(latest, snapshot);
            if (retryKind == PackageInstallerRetryKind.ReplanOperation &&
                (_packageDependencyInstaller == null ||
                 !_packageDependencyInstaller.CanRetryLastPlannerFailure))
            {
                retryKind = PackageInstallerRetryKind.None;
            }
            if (retryKind == PackageInstallerRetryKind.None || IsAnyOperationBusy())
            {
                return;
            }

            switch (retryKind)
            {
                case PackageInstallerRetryKind.Refresh:
                    _packageDetectionService?.Refresh();
                    break;
                case PackageInstallerRetryKind.CheckUpdates:
                    CheckForUpdates();
                    break;
                case PackageInstallerRetryKind.ImportSample:
                    _packageSampleImportService?.RetryLastImport();
                    break;
                case PackageInstallerRetryKind.ResumeOperation:
                    _promptSavedOperationAfterDetectionRefresh =
                        _packageInstallService != null && _packageInstallService.HasSavedOperation;
                    PackageRegistryProvider.RefreshRemote();
                    _packageDetectionService?.Refresh();
                    break;
                case PackageInstallerRetryKind.RestartOperation:
                    if (snapshot == null || !snapshot.CanRestart)
                    {
                        return;
                    }

                    _terminalOperationRetryAfterRefresh = snapshot;
                    PackageRegistryProvider.RefreshRemote();
                    _packageDetectionService?.Refresh();
                    break;
                case PackageInstallerRetryKind.ReplanOperation:
                    _plannerFailureRetryAfterRefresh = true;
                    PackageRegistryProvider.RefreshRemote();
                    _packageDetectionService?.Refresh();
                    break;
            }
        }

        internal static PackageInstallerRetryKind ResolveContextualRetryKindForTests(
            PackageInstallerActivityEntry latest,
            PackageOperationTerminalSnapshot terminalSnapshot)
        {
            return ResolveContextualRetryKind(latest, terminalSnapshot);
        }

        private static PackageInstallerRetryKind ResolveContextualRetryKind(
            PackageInstallerActivityEntry latest,
            PackageOperationTerminalSnapshot terminalSnapshot)
        {
            if (terminalSnapshot != null && terminalSnapshot.CanRestart)
            {
                return PackageInstallerRetryKind.RestartOperation;
            }

            return latest != null ? latest.RetryKind : PackageInstallerRetryKind.None;
        }

        private static void ApplyOperationDrawerData(
            VisualElement drawer,
            ScrollView scrollView,
            VisualElement content,
            Label titleLabel,
            Toggle verboseToggle,
            Label verboseLabel,
            Label messageLabel,
            bool expanded,
            bool verboseConsoleLogging,
            string report)
        {
            if (drawer == null)
            {
                return;
            }

            drawer.style.opacity = 1f;
            DeucarianEditorWorkbenchSurfaces.SetDrawerExpanded(drawer, expanded);

            float drawerHeight = CalculateOperationDrawerContainerHeight(
                expanded,
                CountOperationMessageLines(report));
            drawer.style.height = drawerHeight;
            drawer.style.minHeight = drawerHeight;
            drawer.style.maxHeight = drawerHeight;

            if (scrollView != null)
            {
                scrollView.style.display = DisplayStyle.Flex;
                scrollView.style.opacity = 1f;
            }

            if (content != null)
            {
                content.style.display = DisplayStyle.Flex;
                content.style.opacity = 1f;
            }

            if (titleLabel != null)
            {
                titleLabel.text = "Activity";
                titleLabel.style.display = DisplayStyle.Flex;
                titleLabel.style.opacity = 1f;
                titleLabel.style.color = DeucarianEditorVisualShell.Text;
            }

            if (verboseToggle != null)
            {
                verboseToggle.SetValueWithoutNotify(verboseConsoleLogging);
                verboseToggle.style.display = DisplayStyle.Flex;
                verboseToggle.style.opacity = 1f;
            }

            if (verboseLabel != null)
            {
                verboseLabel.text = "Verbose Console Logging";
                verboseLabel.style.display = DisplayStyle.Flex;
                verboseLabel.style.opacity = 1f;
                verboseLabel.style.color = DeucarianEditorVisualShell.MutedText;
            }

            if (messageLabel != null)
            {
                messageLabel.text = string.IsNullOrWhiteSpace(report)
                    ? "No detailed operation report is available."
                    : report.Trim();
                messageLabel.style.display = DisplayStyle.Flex;
                messageLabel.style.opacity = 1f;
                messageLabel.style.color = DeucarianEditorVisualShell.MutedText;
            }
        }

        private static VisualElement CreateOperationFooterRow(
            Action detailsToggleAction,
            Action cancelAction = null)
        {
            DeucarianEditorWorkbenchFooter sharedFooter =
                DeucarianEditorWorkbenchSurfaces.CreateFooter(
                    string.Empty,
                    "Idle",
                    "No operation running.",
                    "Cancel",
                    cancelAction,
                    GetFooterVersionText());
            VisualElement footer = sharedFooter.Root;
            footer.name = OperationFooterRowName;
            footer.style.height = OperationFooterHeight;
            footer.style.minHeight = OperationFooterHeight;
            footer.style.maxHeight = OperationFooterHeight;
            footer.style.paddingLeft = DeucarianEditorLayoutMetrics.FooterHorizontalPadding;
            footer.style.paddingRight = DeucarianEditorLayoutMetrics.FooterHorizontalPadding;
            footer.style.paddingTop = DeucarianEditorLayoutMetrics.FooterVerticalPadding;
            footer.style.paddingBottom = DeucarianEditorLayoutMetrics.FooterVerticalPadding;
            footer.style.opacity = 1f;

            sharedFooter.Status.name = OperationFooterStatusGroupName;
            sharedFooter.StatusImage.name = OperationFooterStatusIconName;
            sharedFooter.StatusLabel.name = OperationFooterStatusLabelName;
            sharedFooter.Summary.name = OperationFooterSummaryName;
            sharedFooter.Version.name = OperationFooterVersionName;
            sharedFooter.Status.style.opacity = 1f;
            sharedFooter.StatusImage.style.opacity = 1f;
            sharedFooter.StatusLabel.style.opacity = 1f;
            sharedFooter.Summary.style.opacity = 1f;
            sharedFooter.Version.style.opacity = 1f;

            Button cancelButton = sharedFooter.Action;
            cancelButton.name = OperationFooterCancelButtonName;
            DeucarianEditorWorkbenchToolbar.SetButtonIcon(
                cancelButton,
                DeucarianEditorIconIds.Stop,
                "Cancel",
                "Cancel the active Package Installer operation.");
            cancelButton.style.width = 124f;
            cancelButton.style.minWidth = 124f;
            cancelButton.style.maxWidth = 124f;
            cancelButton.style.display = DisplayStyle.None;

            Button detailsButton = DeucarianEditorWorkbenchSurfaces.AddFooterAction(
                sharedFooter,
                DeucarianEditorIconIds.ShowDetails,
                "Show Details",
                detailsToggleAction,
                "Show the last operation details.",
                128f);
            detailsButton.name = OperationFooterDetailsButtonName;
            detailsButton.style.opacity = 1f;

            ApplyOperationFooterData(
                footer,
                VisualStatusKind.Info,
                "Idle",
                "No operation running.",
                false,
                GetFooterVersionText());

            return footer;
        }

        private void CacheOperationFooterElements(VisualElement footer)
        {
            if (footer == null)
            {
                _operationFooterStatusGroup = null;
                _operationFooterStatusIcon = null;
                _operationFooterStatusLabel = null;
                _operationFooterSummaryLabel = null;
                _operationFooterDetailsButton = null;
                _operationFooterVersionLabel = null;
                return;
            }

            _operationFooterStatusGroup = footer.Q<VisualElement>(OperationFooterStatusGroupName);
            _operationFooterStatusIcon = footer.Q<Image>(OperationFooterStatusIconName);
            _operationFooterStatusLabel = footer.Q<Label>(OperationFooterStatusLabelName);
            _operationFooterSummaryLabel = footer.Q<Label>(OperationFooterSummaryName);
            _operationFooterDetailsButton = footer.Q<Button>(OperationFooterDetailsButtonName);
            _operationFooterVersionLabel = footer.Q<Label>(OperationFooterVersionName);
        }

        private void UpdateOperationFooter()
        {
            if (_operationFooterContainer == null)
            {
                return;
            }

            OperationProgressView operation = GetCurrentOperationProgress();
            ApplyOperationFooterData(
                _operationFooterContainer,
                GetGlobalOperationStatusKind(operation),
                GetGlobalOperationStateLabel(operation),
                GetOperationFooterSummaryLine(operation),
                _operationDetailsExpanded,
                GetFooterVersionText());

            CacheOperationFooterElements(_operationFooterContainer);
            UpdateOperationCancelButton();
            RefreshOperationDrawerContent();
        }

        private void UpdateOperationCancelButton()
        {
            Button cancelButton = _operationFooterContainer?.Q<Button>(OperationFooterCancelButtonName);
            if (cancelButton == null)
            {
                return;
            }

            bool installBusy = _packageInstallService != null && _packageInstallService.IsBusy;
            bool sampleBusy = _packageSampleImportService != null && _packageSampleImportService.IsBusy;
            bool checkBusy = _packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking;
            bool preflightBusy = _packageDependencyInstaller != null &&
                                 _packageDependencyInstaller.IsAwaitingPreflight;
            bool registryBusy = _activeActionKind == PackageInstallerActionKind.CheckUpdates &&
                                PackageRegistryProvider.IsRemoteRefreshing;
            cancelButton.style.display = installBusy || sampleBusy || checkBusy || preflightBusy || registryBusy
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            string text = sampleBusy
                ? "Cancel Import"
                : preflightBusy
                    ? "Cancel Confirmation"
                    : checkBusy
                        ? "Cancel Check"
                        : registryBusy
                            ? "Cancel Check"
                            : "Cancel";
            DeucarianEditorIconTextButton.SetText(cancelButton, text);
        }

        private void CancelCurrentContextualOperation()
        {
            if (TryCancelAwaitingPreflight())
            {
                // The confirmation itself is the active operation for single-package
                // reinstall and other risky flows that do not own a bulk action kind.
            }
            else if (_packageSampleImportService != null && _packageSampleImportService.IsBusy)
            {
                _packageSampleImportService.CancelCurrentImport();
            }
            else if (_packageInstallService != null && _packageInstallService.IsBusy)
            {
                _packageInstallService.CancelCurrentOperation();
            }
            else if (_packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking)
            {
                _packageUpdateCheckService.CancelCurrentCheck();
            }
            else if (PackageRegistryProvider.IsRemoteRefreshing)
            {
                if (_activeActionKind == PackageInstallerActionKind.CheckUpdates)
                {
                    CancelAction(PackageInstallerActionKind.CheckUpdates);
                }
                else
                {
                    PackageRegistryProvider.CancelRemoteRefresh();
                }
            }

            UpdateOperationFooter();
        }

        private static void ApplyOperationFooterData(
            VisualElement footer,
            VisualStatusKind statusKind,
            string statusText,
            string summaryText,
            bool detailsExpanded,
            string packageVersionText)
        {
            if (footer == null)
            {
                return;
            }

            string safeStatusText = string.IsNullOrWhiteSpace(statusText) ? "Idle" : statusText.Trim();
            string safeSummaryText = string.IsNullOrWhiteSpace(summaryText) ? "No operation running." : summaryText.Trim();
            string safeVersionText = string.IsNullOrWhiteSpace(packageVersionText)
                ? PackageInstallerRuntimeIdentity.PackageId
                : packageVersionText.Trim();
            Color statusColor = GetStatusColor(statusKind);

            Image statusIcon = footer.Q<Image>(OperationFooterStatusIconName);
            Label statusLabel = footer.Q<Label>(OperationFooterStatusLabelName);
            Label summaryLabel = footer.Q<Label>(OperationFooterSummaryName);
            Button detailsButton = footer.Q<Button>(OperationFooterDetailsButtonName);
            Label versionLabel = footer.Q<Label>(OperationFooterVersionName);

            if (statusIcon != null)
            {
                statusIcon.image = DeucarianEditorIcons.GetIcon(GetStatusIconId(statusKind));
                statusIcon.style.display = DisplayStyle.Flex;
                statusIcon.tooltip = safeStatusText;
                statusIcon.tintColor = statusColor;
                SetFooterStatusClass(statusIcon, statusKind);
            }

            if (statusLabel != null)
            {
                statusLabel.text = safeStatusText;
                statusLabel.tooltip = safeStatusText;
            }

            if (summaryLabel != null)
            {
                summaryLabel.text = safeSummaryText;
                summaryLabel.tooltip = safeSummaryText;
            }

            if (detailsButton != null)
            {
                DeucarianEditorIconTextButton.SetText(
                    detailsButton,
                    detailsExpanded ? "Hide Details" : "Show Details");
                DeucarianEditorIconTextButton.SetIcon(
                    detailsButton,
                    detailsExpanded
                        ? DeucarianEditorIconIds.HideDetails
                        : DeucarianEditorIconIds.ShowDetails);
                detailsButton.tooltip = detailsExpanded
                    ? "Hide the last operation details."
                    : "Show the last operation details.";
            }

            if (versionLabel != null)
            {
                versionLabel.text = safeVersionText;
                versionLabel.tooltip = safeVersionText;
            }
        }

        private static void SetFooterStatusClass(VisualElement element, VisualStatusKind statusKind)
        {
            if (element == null)
            {
                return;
            }

            element.RemoveFromClassList(
                DeucarianEditorWorkbenchSurfaces.FooterStatusSuccessClass);
            element.RemoveFromClassList(
                DeucarianEditorWorkbenchSurfaces.FooterStatusNeutralClass);
            element.RemoveFromClassList(
                DeucarianEditorWorkbenchSurfaces.FooterStatusWarningClass);
            element.RemoveFromClassList(
                DeucarianEditorWorkbenchSurfaces.FooterStatusErrorClass);
            element.RemoveFromClassList(
                DeucarianEditorWorkbenchSurfaces.FooterStatusBusyClass);

            switch (statusKind)
            {
                case VisualStatusKind.Installed:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusSuccessClass);
                    break;
                case VisualStatusKind.NotInstalled:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusNeutralClass);
                    break;
                case VisualStatusKind.UpdateAvailable:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusWarningClass);
                    break;
                case VisualStatusKind.Failed:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusErrorClass);
                    break;
                case VisualStatusKind.Busy:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusBusyClass);
                    break;
                case VisualStatusKind.Info:
                case VisualStatusKind.Integration:
                default:
                    element.AddToClassList(
                        DeucarianEditorWorkbenchSurfaces.FooterStatusNeutralClass);
                    break;
            }
        }

        private void SetViewMode(InstallerViewMode viewMode)
        {
            bool wasGraphMode = _viewMode == InstallerViewMode.EcosystemGraph;
            _viewMode = ResolveInstallerViewMode(viewMode);
            UpdateViewVisibility();

            if (_viewMode == InstallerViewMode.EcosystemGraph)
            {
                RefreshGraphView("view mode changed");

                if (!wasGraphMode)
                {
                    RequestAutomaticGraphUpdateCheck();
                }
            }

            Repaint();
        }

        private static InstallerViewMode ResolveInstallerViewMode(InstallerViewMode requestedViewMode)
        {
            return ListViewEnabled || requestedViewMode != InstallerViewMode.List
                ? requestedViewMode
                : DefaultInstallerViewMode;
        }

        private bool ShouldCheckForUpdatesOnGraphOpen()
        {
            return _viewMode == InstallerViewMode.EcosystemGraph &&
                   PackageUpdateCheckPreferences.ShouldCheckOnWindowOpen(
                       DateTime.UtcNow,
                       _packageUpdateCheckService != null
                           ? _packageUpdateCheckService.LastCheckedUtc
                           : null);
        }

        private void RequestAutomaticGraphUpdateCheck()
        {
            if (!ShouldCheckForUpdatesOnGraphOpen())
            {
                return;
            }

            InvalidateGraphModelCache("automatic graph update check");
            QueueDeferredUpdateCheck(PackageInstallerActionKind.CheckUpdates);

            if (_packageInstallService != null && _packageInstallService.IsBusy)
            {
                UpdateViewVisibility();
                Repaint();
                return;
            }

            PackageRegistryProvider.RefreshRemote();
            _packageUpdateCheckService.PrepareForUpdateCheck();

            if (!_packageDetectionService.IsRefreshing)
            {
                _packageDetectionService.Refresh();
            }

            TryRunDeferredUpdateCheck();
            RefreshGraphView("automatic graph update check");
            UpdateViewVisibility();
            Repaint();
        }

        private void QueueDeferredUpdateCheck(PackageInstallerActionKind actionKind)
        {
            _checkUpdatesAfterDetectionRefresh = true;
            _deferredUpdateCheckActionKind = actionKind;

            if (actionKind != PackageInstallerActionKind.None &&
                _activeActionKind == PackageInstallerActionKind.None &&
                (_packageInstallService == null || !_packageInstallService.IsBusy))
            {
                _activeActionKind = actionKind;
                _cancelingActionKind = PackageInstallerActionKind.None;
            }
        }

        private void TryRunDeferredUpdateCheck()
        {
            if (!_checkUpdatesAfterDetectionRefresh)
            {
                ClearActiveActionIfIdle();
                return;
            }

            if ((_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                PackageRegistryProvider.IsRemoteRefreshing)
            {
                UpdateViewVisibility();
                Repaint();
                return;
            }

            RunDeferredUpdateCheck();
        }

        private void RunDeferredUpdateCheck()
        {
            if (!_checkUpdatesAfterDetectionRefresh)
            {
                ClearActiveActionIfIdle();
                return;
            }

            PackageInstallerActionKind actionKind = _deferredUpdateCheckActionKind;
            _checkUpdatesAfterDetectionRefresh = false;
            _deferredUpdateCheckActionKind = PackageInstallerActionKind.None;

            if (actionKind != PackageInstallerActionKind.None &&
                _activeActionKind == PackageInstallerActionKind.None)
            {
                _activeActionKind = actionKind;
                _cancelingActionKind = PackageInstallerActionKind.None;
            }

            _packageUpdateCheckService.CheckForUpdates(PackageRegistryProvider.All, GetSelectedChannel);
            ClearActiveActionIfIdle();
        }

        private void ClearActiveActionIfIdle()
        {
            if (_activeActionKind == PackageInstallerActionKind.None || IsActiveActionStillBusy())
            {
                return;
            }

            _activeActionKind = PackageInstallerActionKind.None;
            _cancelingActionKind = PackageInstallerActionKind.None;

            if (!_checkUpdatesAfterDetectionRefresh)
            {
                _deferredUpdateCheckActionKind = PackageInstallerActionKind.None;
            }

            UpdateViewVisibility();
            Repaint();
        }

        private void HandlePreflightCompleted()
        {
            if (_packageInstallService == null || !_packageInstallService.IsBusy)
            {
                _pendingUpdateStatusInvalidationPackageIds.Clear();
            }

            ClearActiveActionIfIdle();
            UpdateViewVisibility();
            Repaint();
        }

        private bool IsActiveActionStillBusy()
        {
            switch (_activeActionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    return _checkUpdatesAfterDetectionRefresh ||
                           (_packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking) ||
                           (_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                           PackageRegistryProvider.IsRemoteRefreshing;
                case PackageInstallerActionKind.UpdateAll:
                case PackageInstallerActionKind.InstallAll:
                    return (_packageInstallService != null && _packageInstallService.IsBusy) ||
                           (_packageDependencyInstaller != null &&
                            _packageDependencyInstaller.IsAwaitingPreflight);
                default:
                    return false;
            }
        }

        private void UpdateViewVisibility()
        {
            bool graphMode = _viewMode == InstallerViewMode.EcosystemGraph;

            if (_listViewContainerHost != null)
            {
                _listViewContainerHost.style.display = graphMode ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_graphModeContainer != null)
            {
                _graphModeContainer.style.display = graphMode ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_operationDrawerContainer != null)
            {
                RefreshOperationDrawerContent();
            }

            if (_operationFooterContainer != null)
            {
                _operationFooterContainer.style.display = DisplayStyle.Flex;
                _operationFooterContainer.style.height = OperationFooterHeight;
                _operationFooterContainer.style.minHeight = OperationFooterHeight;
                _operationFooterContainer.style.maxHeight = OperationFooterHeight;
            }

            UpdateOperationFooter();

            if (_listViewButton != null)
            {
                SetViewToggleActive(_listViewButton, !graphMode);
            }

            if (_graphViewButton != null)
            {
                SetViewToggleActive(_graphViewButton, graphMode);
            }

            bool busy = IsAnyOperationBusy();
            PackageDefinition[] packagesWithUpdates = _packageUpdateCheckService != null
                ? GetPackagesWithUpdates()
                : Array.Empty<PackageDefinition>();

            if (_graphGlobalChannelButton != null)
            {
                DeucarianEditorCommandBar.SetReservedVisible(
                    _graphGlobalChannelSlot,
                    graphMode);
                _graphGlobalChannelButton.SetEnabled(!busy);
                UpdateGlobalChannelOverrideButton();

                if (!graphMode)
                {
                    HideGlobalChannelOverridePopup();
                }
            }

            if (_graphRefreshButton != null)
            {
                DeucarianEditorCommandBar.SetReservedVisible(
                    _graphRefreshSlot,
                    graphMode);
                _graphRefreshButton.SetEnabled(!busy);
            }

            if (_graphCheckUpdatesButton != null)
            {
                PackageInstallerActionButtonState state = CreateActionButtonState(
                    PackageInstallerActionKind.CheckUpdates,
                    _activeActionKind,
                    _cancelingActionKind,
                    busy,
                    packagesWithUpdates.Length > 0);
                DeucarianEditorCommandBar.SetReservedVisible(
                    _graphCheckUpdatesSlot,
                    graphMode);
                DeucarianEditorCommandBar.SetText(_graphCheckUpdatesButton, state.Label);
                _graphCheckUpdatesButton.SetEnabled(state.Enabled);
            }

            if (_graphUpdateAllButton != null)
            {
                PackageInstallerActionButtonState state = CreateActionButtonState(
                    PackageInstallerActionKind.UpdateAll,
                    _activeActionKind,
                    _cancelingActionKind,
                    busy,
                    packagesWithUpdates.Length > 0);
                _graphUpdateAllButton.style.display = graphMode ? DisplayStyle.Flex : DisplayStyle.None;
                DeucarianEditorCommandBar.SetText(_graphUpdateAllButton, state.Label);
                _graphUpdateAllButton.SetEnabled(state.Enabled);
            }

            if (_graphInstallAllButton != null)
            {
                PackageInstallerActionButtonState state = CreateActionButtonState(
                    PackageInstallerActionKind.InstallAll,
                    _activeActionKind,
                    _cancelingActionKind,
                    busy,
                    packagesWithUpdates.Length > 0);
                _graphInstallAllButton.style.display = graphMode ? DisplayStyle.Flex : DisplayStyle.None;
                DeucarianEditorCommandBar.SetText(_graphInstallAllButton, state.Label);
                _graphInstallAllButton.SetEnabled(state.Enabled);
            }

            if (_viewSummaryLabel != null)
            {
                string summary = PackageRegistryProvider.All.Count +
                                 " packages - " +
                                 PackageRegistryProvider.StatusMessage;
                _viewSummaryLabel.text = summary;
                _viewSummaryLabel.tooltip = summary;
            }
        }

        private void RefreshGraphView()
        {
            RefreshGraphView("refresh");
        }

        private void RefreshExternalState(string reason)
        {
            if (_stateRepository == null)
            {
                return;
            }

            bool shouldRefreshGraph = false;
            PackageChannelSelection projectChannelSelection = _stateRepository.GetProjectChannelSelection();
            PackageChannel projectChannel = projectChannelSelection.Channel;

            if (projectChannel != _lastObservedProjectChannel ||
                projectChannelSelection.ChangedAtUtcTicks != _lastObservedProjectChannelChangedAtUtcTicks)
            {
                _lastObservedProjectChannel = projectChannel;
                _lastObservedProjectChannelChangedAtUtcTicks = projectChannelSelection.ChangedAtUtcTicks;
                _packageUpdateCheckService?.InvalidateAll();
                InvalidateGraphModelCache("selected channel changed externally");
                UpdateGlobalChannelOverrideButton();
                shouldRefreshGraph = true;
            }

            if (_packageDetectionService != null &&
                _packageDetectionService.RefreshIfManifestStateChanged())
            {
                bool hadUpdateStatuses =
                    _packageUpdateCheckService != null && _packageUpdateCheckService.HasStatuses;
                _packageUpdateCheckService?.InvalidateIfManifestStateChanged();

                if (hadUpdateStatuses)
                {
                    QueueDeferredUpdateCheck(PackageInstallerActionKind.CheckUpdates);
                }

                InvalidateGraphModelCache("project manifest changed externally");
                shouldRefreshGraph = true;
            }

            if (shouldRefreshGraph)
            {
                RefreshGraphView(reason);
                Repaint();
            }
        }

        private void RefreshGraphView(string reason)
        {
            if (_graphView == null)
            {
                return;
            }

            bool graphCacheDirty = _graphModelCacheDirty || _cachedPackageGraph == null;
            string diagnosticReason = graphCacheDirty
                ? (string.IsNullOrWhiteSpace(reason) ? "refresh" : reason) +
                  " / " + _graphModelCacheInvalidationReason
                : reason;

            using (PackageGraphOpenProfiler.Begin(
                       diagnosticReason,
                       _graphNavigationState.FocusedPackageId,
                       _graphNavigationState.FocusedGroupId,
                       graphCacheDirty))
            {
                PackageGraphModel graph = GetOrBuildPackageGraphModel();
                PackageGraphOpenProfiler.Current?.SetGraphCounts(graph);
                ValidatePendingReloadState(graph);

                HashSet<string> visiblePackageIds;
                PackageGraphSearchState searchState;
                PackageVisibilityFilterCounts filterCounts;
                int hiddenRelatedCount;

                using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.VisibilitySearch))
                {
                    visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(
                        graph,
                        _visibilityFilterState);
                    searchState = PackageGraphSearchIndex.Create(
                        graph,
                        _visibilityFilterState);

                    ClearGraphSelectionIfHidden(visiblePackageIds);

                    filterCounts = PackageVisibilityFilter.CalculateCounts(
                        graph,
                        _visibilityFilterState);
                    hiddenRelatedCount = PackageVisibilityFilter.CountHiddenRelatedPackages(
                        graph,
                        _graphNavigationState.FocusedPackageId,
                        visiblePackageIds);
                }

                _graphView.SetGraph(
                    graph,
                    _selectedPackageId,
                    _graphNavigationState.FocusedPackageId,
                    _graphNavigationState.FocusedGroupId,
                    !IsAnyOperationBusy(),
                    visiblePackageIds,
                    searchState,
                    filterCounts,
                    hiddenRelatedCount);

                using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.LayoutRepaintScheduling))
                {
                    _graphDetailsContainer?.MarkDirtyRepaint();
                    _operationDrawerContainer?.MarkDirtyRepaint();
                    UpdateOperationFooter();
                    UpdateViewVisibility();
                }
            }
        }

        private PackageGraphModel GetOrBuildPackageGraphModel()
        {
            if (!_graphModelCacheDirty && _cachedPackageGraph != null)
            {
                _lastPackageGraph = _cachedPackageGraph;
                return _cachedPackageGraph;
            }

            IReadOnlyList<PackageDefinition> packages;
            IReadOnlyList<PackageGraphGroup> groups;

            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.RegistryLookup))
            {
                packages = PackageRegistryProvider.All;
                groups = PackageRegistryProvider.EcosystemGroups;
            }

            using (PackageGraphOpenProfiler.Measure(PackageGraphOpenTiming.GraphRebuild))
            {
                PackageGraphBuilder builder = _packageGraphBuilder ?? new PackageGraphBuilder(
                    packageId => _packageDetectionService != null && _packageDetectionService.IsInstalled(packageId),
                    GetSelectedChannel,
                    packageDefinition => _packageUpdateCheckService != null
                        ? _packageUpdateCheckService.GetStatus(packageDefinition, GetSelectedChannel(packageDefinition))
                        : null);
                _cachedPackageGraph = builder.Build(packages, groups);
                _lastPackageGraph = _cachedPackageGraph;
                _graphModelCacheDirty = false;
                PackageGraphOpenProfiler.Current?.MarkGraphRebuilt();
            }

            return _cachedPackageGraph;
        }

        private void InvalidateGraphModelCache(string reason)
        {
            // Registry, manifest/install state, update status, and channel changes alter graph node state.
            // Focus-only navigation intentionally does not invalidate this cache.
            _graphModelCacheDirty = true;
            _graphModelCacheInvalidationReason = string.IsNullOrWhiteSpace(reason)
                ? "graph data changed"
                : reason.Trim();
        }

        private void HandleVisibilityFilterChanged()
        {
            RefreshGraphView("visibility filter changed");
            Repaint();
        }

        private void ClearGraphSelectionIfHidden(ISet<string> visiblePackageIds)
        {
            if (_viewMode != InstallerViewMode.EcosystemGraph || visiblePackageIds == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_graphNavigationState.FocusedPackageId))
            {
                return;
            }

            if (!ShouldClearGraphSelectionForFilters(
                    _selectedPackageId,
                    _graphNavigationState.FocusedPackageId,
                    visiblePackageIds))
            {
                return;
            }

            _selectionKind = SelectionKind.Package;
            _selectedPackageId = string.Empty;
            _graphNavigationState = PackageGraphNavigationState.Overview();
            _detailsScrollPosition = Vector2.zero;
        }

        private void HandleGraphPackageSelected(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return;
            }

            SelectionKind selectionKind = packageDefinition.IsIntegration ? SelectionKind.Integration : SelectionKind.Package;

            if (IsSelected(packageDefinition, selectionKind))
            {
                NavigateGraphToPackageOwner(packageDefinition.PackageId);
                return;
            }

            SelectDefinition(
                packageDefinition,
                selectionKind,
                refreshGraph: false);
            _graphNavigationState = PackageGraphNavigationState.Package(
                packageDefinition.PackageId,
                GetGraphPackageGroupId(packageDefinition.PackageId));
            RefreshGraphView("package focus");
        }

        private void ClearGraphSelection()
        {
            NavigateGraphToRoot();
        }

        private void HandleGraphRootFocused()
        {
            NavigateGraphToRoot();
        }

        private void HandleGraphGroupFocused(PackageGraphGroup group)
        {
            if (group == null)
            {
                NavigateGraphToRoot();
                return;
            }

            if (string.Equals(group.Id, _graphNavigationState.FocusedGroupId, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(_graphNavigationState.FocusedPackageId))
            {
                NavigateGraphToGroupOrRoot(GetGraphParentGroupId(group.Id));
                return;
            }

            NavigateGraphToGroup(group.Id);
        }

        private void HandleGraphBackNavigation()
        {
            if (!string.IsNullOrWhiteSpace(_graphNavigationState.FocusedPackageId))
            {
                string parentGroupId = GetGraphPackageGroupId(_graphNavigationState.FocusedPackageId);
                NavigateGraphToGroupOrRoot(parentGroupId);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_graphNavigationState.FocusedGroupId))
            {
                string parentGroupId = GetGraphParentGroupId(_graphNavigationState.FocusedGroupId);
                NavigateGraphToGroupOrRoot(parentGroupId);
                return;
            }

            NavigateGraphToRoot();
        }

        private void NavigateGraphToPackageOwner(string packageId)
        {
            NavigateGraphToGroupOrRoot(GetGraphPackageGroupId(packageId));
        }

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
                GetGraphPackageGroupId(packageDefinition.PackageId));

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

        private void EnsureStyles()
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

        private void DrawWindowBackground()
        {
            DeucarianEditorVisualShell.DrawWindowBackground(
                new Rect(0f, 0f, position.width, position.height),
                _mainBackgroundColor);
        }

        private void DrawHeader()
        {
            bool compact = position.width < 1100f;

            DeucarianEditorChrome.DrawBrandHeader(
                "Deucarian Package Installer",
                "Install, update, remove, and compose Deucarian packages through first-class integration packages.");

            BeginSurface(
                DeucarianEditorStyles.SectionBox,
                _headerPanelBackgroundColor,
                _panelBorderColor,
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
                EditorGUILayout.LabelField("Registry", _mutedMiniLabelStyle, GUILayout.Width(54f));
                DrawStatusBadge(PackageRegistryProvider.All.Count + " packages", VisualStatusKind.Info, GUILayout.Width(104f));

                if (PackageRegistryProvider.IsRemoteRefreshing)
                {
                    DrawStatusBadge("Refreshing", VisualStatusKind.Busy, GUILayout.Width(92f));
                }
                else
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(PackageRegistryProvider.StatusMessage, PackageRegistryProvider.StatusMessage),
                        _mutedMiniLabelStyle,
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

        private static string GetDefaultActionLabel(PackageInstallerActionKind actionKind)
        {
            switch (actionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    return "Check Updates";
                case PackageInstallerActionKind.UpdateAll:
                    return "Update All";
                case PackageInstallerActionKind.InstallAll:
                    return "Install All";
                default:
                    return string.Empty;
            }
        }

        private static string GetCancelActionLabel(PackageInstallerActionKind actionKind)
        {
            switch (actionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    return "Cancel Check";
                case PackageInstallerActionKind.UpdateAll:
                    return "Cancel Update";
                case PackageInstallerActionKind.InstallAll:
                    return "Cancel Install";
                default:
                    return "Cancel";
            }
        }

        private void RefreshPackages()
        {
            InvalidateGraphModelCache("manual refresh");
            PackageRegistryProvider.RefreshRemote();
            _packageDetectionService.Refresh();
            RefreshGraphView("manual refresh");
        }

        private void HandleActionButton(PackageInstallerActionKind actionKind)
        {
            if (_activeActionKind == actionKind)
            {
                CancelAction(actionKind);
                return;
            }

            if (IsAnyOperationBusy())
            {
                return;
            }

            switch (actionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    CheckForUpdates();
                    break;
                case PackageInstallerActionKind.UpdateAll:
                    UpdateAllPackages();
                    break;
                case PackageInstallerActionKind.InstallAll:
                    InstallAllPackages();
                    break;
            }
        }

        private void CancelAction(PackageInstallerActionKind actionKind)
        {
            if (_activeActionKind != actionKind ||
                _cancelingActionKind != PackageInstallerActionKind.None)
            {
                return;
            }

            _cancelingActionKind = actionKind;

            switch (actionKind)
            {
                case PackageInstallerActionKind.CheckUpdates:
                    _checkUpdatesAfterDetectionRefresh = false;
                    _deferredUpdateCheckActionKind = PackageInstallerActionKind.None;
                    _packageUpdateCheckService.CancelCurrentCheck();
                    if (PackageRegistryProvider.CancelRemoteRefresh())
                    {
                        PackageInstallerActivityService.Record(
                            "Registry",
                            PackageInstallerActivitySeverity.Warning,
                            "Registry refresh canceled.",
                            retryKind: PackageInstallerRetryKind.CheckUpdates);
                    }
                    break;
                case PackageInstallerActionKind.UpdateAll:
                case PackageInstallerActionKind.InstallAll:
                    if (!TryCancelAwaitingPreflight())
                    {
                        _packageInstallService.CancelCurrentOperation();
                    }
                    break;
            }

            UpdateViewVisibility();
            ClearActiveActionIfIdle();
            Repaint();
        }

        private bool TryCancelAwaitingPreflight()
        {
            return CancelAwaitingPreflight(
                _packageDependencyInstaller,
                () => DismissPendingConfirmation(refreshUi: false));
        }

        internal static bool CancelAwaitingPreflightForTests(
            PackageDependencyInstaller installer,
            Action dismissConfirmation)
        {
            return CancelAwaitingPreflight(installer, dismissConfirmation);
        }

        private static bool CancelAwaitingPreflight(
            PackageDependencyInstaller installer,
            Action dismissConfirmation)
        {
            if (installer == null || !installer.IsAwaitingPreflight)
            {
                return false;
            }

            dismissConfirmation?.Invoke();
            installer.CancelPendingPreflight();
            return true;
        }

        private void CheckForUpdates()
        {
            RequestUpdateCheck(PackageInstallerActionKind.CheckUpdates, "manual update check");
        }

        private void RequestUpdateCheck(PackageInstallerActionKind actionKind, string reason)
        {
            InvalidateGraphModelCache(reason);
            _activeActionKind = actionKind;
            _cancelingActionKind = PackageInstallerActionKind.None;
            QueueDeferredUpdateCheck(actionKind);
            PackageRegistryProvider.RefreshRemote();
            _packageUpdateCheckService.PrepareForUpdateCheck();

            if (!_packageDetectionService.IsRefreshing)
            {
                _packageDetectionService.Refresh();
            }

            TryRunDeferredUpdateCheck();
            RefreshGraphView(reason);
            UpdateViewVisibility();
            Repaint();
        }

        private void UpdateAllPackages()
        {
            PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();
            TrackPendingUpdateStatusInvalidations(packagesWithUpdates);

            _activeActionKind = PackageInstallerActionKind.UpdateAll;
            _cancelingActionKind = PackageInstallerActionKind.None;
            _packageDependencyInstaller.UpdateAll(
                packagesWithUpdates,
                GetSelectedChannel);

            if (!ShouldRetainPendingUpdateStatusInvalidations(
                    _packageInstallService != null && _packageInstallService.IsBusy,
                    _packageDependencyInstaller != null &&
                    _packageDependencyInstaller.IsAwaitingPreflight))
            {
                _pendingUpdateStatusInvalidationPackageIds.Clear();
            }

            ClearActiveActionIfIdle();
            UpdateViewVisibility();
        }

        private void InstallAllPackages()
        {
            _activeActionKind = PackageInstallerActionKind.InstallAll;
            _cancelingActionKind = PackageInstallerActionKind.None;
            _packageDependencyInstaller.InstallAll(GetSelectedChannel);
            ClearActiveActionIfIdle();
            UpdateViewVisibility();
        }

        private void DrawSidebar()
        {
            Rect rect = BeginSurface(
                _sidebarStyle,
                _sidebarBackgroundColor,
                _panelBorderColor,
                GUILayout.Width(SidebarWidth),
                GUILayout.ExpandHeight(true));

            IReadOnlyList<PackageCategoryListView> categoryViews = GetPackageCategoryViews();
            DrawSidebarFilterControls(categoryViews);
            GUILayout.Space(8f);
            DrawHorizontalSeparator();
            GUILayout.Space(8f);

            _sidebarScrollPosition = EditorGUILayout.BeginScrollView(_sidebarScrollPosition);
            DrawRegistrySidebarSections(categoryViews);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private IReadOnlyList<PackageCategoryListView> GetPackageCategoryViews()
        {
            return PackageListFilter.CreateCategoryViews(
                PackageRegistryProvider.All,
                PackageRegistryProvider.Categories,
                _visibilityFilterState,
                package => _packageDetectionService.IsInstalled(package.PackageId),
                IsCategoryExpanded);
        }

        private void DrawSidebarFilterControls(IReadOnlyList<PackageCategoryListView> categoryViews)
        {
            int totalCount = categoryViews.Sum(view => view.PackageCount);
            int matchedCount = categoryViews.Sum(view => view.FilteredPackageCount);
            int visibleCount = categoryViews.Sum(view => view.VisiblePackages.Count);

            EditorGUILayout.LabelField("Package List", _sectionTitleStyle);

            EditorGUI.BeginChangeCheck();
            string nextSearchText = EditorGUILayout.TextField(
                new GUIContent("Search", "Find packages by package name, package ID, domain, or kind."),
                _visibilityFilterState.SearchText);

            if (EditorGUI.EndChangeCheck() && _visibilityFilterState.SetSearchText(nextSearchText))
            {
                RefreshGraphView("search changed");
                Repaint();
            }

            EditorGUILayout.LabelField("Visibility", _mutedMiniLabelStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool nextShowInstalled = EditorGUILayout.ToggleLeft(
                    new GUIContent("Installed", "Show packages that Unity reports as installed."),
                    _visibilityFilterState.ShowInstalled,
                    GUILayout.Width(92f));
                bool nextShowNotInstalled = EditorGUILayout.ToggleLeft(
                    new GUIContent("Not Installed", "Show packages that Unity does not report as installed."),
                    _visibilityFilterState.ShowNotInstalled,
                    GUILayout.Width(118f));

                if (_visibilityFilterState.Set(
                        _visibilityFilterState.SearchText,
                        nextShowInstalled,
                        nextShowNotInstalled))
                {
                    RefreshGraphView("visibility filter changed");
                    Repaint();
                }
            }

            EditorGUILayout.LabelField(
                GetSidebarFilterSummary(totalCount, matchedCount, visibleCount),
                _mutedMiniLabelStyle);
        }

        private string GetSidebarFilterSummary(int totalCount, int matchedCount, int visibleCount)
        {
            if (totalCount == 0)
            {
                return "No packages in the active registry.";
            }

            if (matchedCount == totalCount)
            {
                return visibleCount + " visible / " + totalCount + " packages";
            }

            return visibleCount + " visible / " + matchedCount + " matched / " + totalCount + " packages";
        }

        private void DrawRegistrySidebarSections(IReadOnlyList<PackageCategoryListView> categoryViews)
        {
            bool drewPackageHeader = false;
            bool drewAnyCategory = false;

            foreach (PackageCategoryListView categoryView in categoryViews)
            {
                if (!categoryView.HasFilteredPackages)
                {
                    continue;
                }

                DrawSidebarSection(
                    drewPackageHeader ? null : "Packages",
                    categoryView);
                drewPackageHeader = true;

                drewAnyCategory = true;
                GUILayout.Space(8f);
            }

            if (!drewAnyCategory)
            {
                string message = categoryViews.Any(view => view.PackageCount > 0)
                    ? "No packages match the active filters."
                    : "No package entries are available in the active registry.";
                DrawInlineHelp(message, VisualStatusKind.Info);
            }
        }

        private void DrawUpdateSummary(bool compact)
        {
            PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();
            int updateCount = packagesWithUpdates.Length;
            bool checking = _packageUpdateCheckService.IsChecking;
            VisualStatusKind updateKind = checking
                ? VisualStatusKind.Busy
                : updateCount > 0
                    ? VisualStatusKind.UpdateAvailable
                    : VisualStatusKind.Installed;

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawStatusBadge(
                    checking ? "Checking updates" : "Updates Available: " + updateCount,
                    updateKind,
                    GUILayout.Width(compact ? 132f : 146f));

                EditorGUILayout.LabelField(
                    new GUIContent(
                        "Last Checked: " + GetLastUpdateCheckLabel(),
                        GetLastUpdateCheckTooltip()),
                    _mutedMiniLabelStyle,
                    GUILayout.MinWidth(compact ? 120f : 170f));
            }

            if (!string.IsNullOrWhiteSpace(_packageUpdateCheckService.LastFailureMessage))
            {
                EditorGUILayout.LabelField(
                    new GUIContent(
                        _packageUpdateCheckService.LastFailureMessage,
                        _packageUpdateCheckService.LastFailureMessage),
                    _mutedMiniLabelStyle);
            }
        }

        private string GetLastUpdateCheckLabel()
        {
            DateTime? lastCheckedUtc = _packageUpdateCheckService.LastCheckedUtc;

            if (!lastCheckedUtc.HasValue)
            {
                return "Never";
            }

            TimeSpan elapsed = DateTime.UtcNow - lastCheckedUtc.Value.ToUniversalTime();

            if (elapsed.TotalSeconds < 60d)
            {
                return "Just now";
            }

            if (elapsed.TotalMinutes < 60d)
            {
                int minutes = Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalMinutes));
                return minutes == 1 ? "1 minute ago" : minutes + " minutes ago";
            }

            if (elapsed.TotalHours < 24d)
            {
                int hours = Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalHours));
                return hours == 1 ? "1 hour ago" : hours + " hours ago";
            }

            int days = Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalDays));
            return days == 1 ? "1 day ago" : days + " days ago";
        }

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

        private void DrawEcosystemOverviewGroupsPanel()
        {
            PackageGraphGroupNavigationRow[] groupRows = CreateEcosystemOverviewGroupNavigationRows(
                    _lastPackageGraph,
                    _graphNavigationState)
                .ToArray();

            DrawPanel("Groups", () =>
            {
                if (groupRows.Length == 0)
                {
                    EditorGUILayout.LabelField("No ecosystem navigation is available.", _mutedMiniLabelStyle);
                    SynchronizeDetailsGroupHover(string.Empty);
                    return;
                }

                string hoveredGroupId = string.Empty;

                foreach (PackageGraphGroupNavigationRow row in groupRows)
                {
                    if (DrawEcosystemOverviewGroupRow(row) && !row.IsOverview)
                    {
                        hoveredGroupId = row.Id;
                    }
                }

                SynchronizeDetailsGroupHover(hoveredGroupId);
            }, GUILayout.ExpandWidth(true));
        }

        private bool DrawEcosystemOverviewGroupRow(PackageGraphGroupNavigationRow row)
        {
            Rect rowRect = GUILayoutUtility.GetRect(1f, 34f, GUILayout.ExpandWidth(true));
            int controlId = GUIUtility.GetControlID(FocusType.Keyboard, rowRect);
            bool hover = rowRect.Contains(Event.current.mousePosition);
            bool graphHover = !row.IsOverview && IsGraphGroupHoverContext(row.Id);
            bool selected = row.IsSelected;
            bool keyboardFocused = GUIUtility.keyboardControl == controlId;

            if (Event.current.type == EventType.Repaint)
            {
                bool highlighted = selected || hover || graphHover || keyboardFocused;
                DeucarianEditorVisualShell.DrawInsetSurface(
                    rowRect,
                    selected ? _rowSelectedColor : highlighted ? _rowHoverColor : _sampleRowBackgroundColor,
                    highlighted ? _interactiveBorderColor : _separatorColor,
                    4f);

                if (selected || row.HasAttention)
                {
                    EditorGUI.DrawRect(
                        new Rect(rowRect.x, rowRect.y, 3f, rowRect.height),
                        row.HasAttention ? GetStatusColor(VisualStatusKind.UpdateAvailable) : _interactiveBorderColor);
                }
            }

            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                GUIUtility.keyboardControl = controlId;
                ActivateEcosystemOverviewGroupRow(row);
                Event.current.Use();
            }
            else if (IsGraphNavigationRowKeyboardActivation(
                         keyboardFocused,
                         Event.current.type,
                         Event.current.keyCode))
            {
                ActivateEcosystemOverviewGroupRow(row);
                Event.current.Use();
            }

            EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);

            Rect iconRect = new Rect(rowRect.x + 9f, rowRect.y + 7f, 20f, 20f);
            DrawGraphNavigationIcon(iconRect, row.IconKey);

            GUIContent nameContent = new GUIContent(row.DisplayName, row.Tooltip);
            string summaryText = _responsiveMode == PackageInstallerResponsiveMode.Compact
                ? FormatCompactEcosystemOverviewGroupStatusSummary(row.StatusSummary)
                : row.Summary;
            GUIContent summaryContent = new GUIContent(summaryText, row.Summary);
            Rect contentRect = new Rect(rowRect.x + 38f, rowRect.y + 8f, rowRect.width - 48f, 18f);
            float gap = 10f;
            float summaryWidth = Mathf.Min(
                _mutedMiniLabelStyle.CalcSize(summaryContent).x + 2f,
                Mathf.Max(84f, contentRect.width * 0.45f));
            float availableNameWidth = Mathf.Max(40f, contentRect.width - summaryWidth - gap);
            float nameWidth = Mathf.Min(_miniLabelStyle.CalcSize(nameContent).x + 2f, availableNameWidth);
            Rect nameRect = new Rect(contentRect.x, contentRect.y, nameWidth, contentRect.height);
            Rect summaryRect = new Rect(nameRect.xMax + gap, contentRect.y, summaryWidth, contentRect.height);

            DrawSingleLineLabel(nameRect, nameContent, _miniLabelStyle);
            DrawSingleLineLabel(summaryRect, summaryContent, _mutedMiniLabelStyle);
            return hover;
        }

        private void ActivateEcosystemOverviewGroupRow(PackageGraphGroupNavigationRow row)
        {
            if (row.IsOverview)
            {
                NavigateGraphToRoot();
                return;
            }

            NavigateGraphToGroup(row.Id);
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

        private static string FormatEcosystemOverviewGroupStatusSummary(
            PackageGraphCategoryStatusSummary statusSummary)
        {
            List<string> parts = new List<string>();

            if (statusSummary.AttentionCount > 0)
            {
                parts.Add(statusSummary.AttentionCount + " attention");
            }

            if (statusSummary.InstalledCount > 0)
            {
                parts.Add(statusSummary.InstalledCount + " installed");
            }

            if (statusSummary.NotInstalledCount > 0)
            {
                parts.Add(statusSummary.NotInstalledCount + " not installed");
            }

            if (statusSummary.UnknownCount > 0)
            {
                parts.Add(statusSummary.UnknownCount + " unknown");
            }

            return parts.Count == 0 ? "0 packages" : string.Join("   ", parts.ToArray());
        }

        private static string FormatCompactEcosystemOverviewGroupStatusSummary(
            PackageGraphCategoryStatusSummary statusSummary)
        {
            List<string> parts = new List<string>();

            if (statusSummary.AttentionCount > 0)
            {
                parts.Add(statusSummary.AttentionCount + " attention");
            }

            if (statusSummary.InstalledCount > 0)
            {
                parts.Add(statusSummary.InstalledCount + " installed");
            }

            if (statusSummary.NotInstalledCount > 0)
            {
                parts.Add(statusSummary.NotInstalledCount + " not installed");
            }

            if (statusSummary.UnknownCount > 0)
            {
                parts.Add(statusSummary.UnknownCount + " unknown");
            }

            return parts.Count == 0 ? "0" : string.Join("   ", parts.ToArray());
        }

        private static IReadOnlyList<PackageGraphGroupNavigationRow> CreateEcosystemOverviewGroupNavigationRows(
            PackageGraphModel graph,
            PackageGraphNavigationState navigationState)
        {
            List<PackageGraphGroupNavigationRow> rows = new List<PackageGraphGroupNavigationRow>();
            PackageGraphNode[] graphNodes = graph == null
                ? Array.Empty<PackageGraphNode>()
                : graph.Nodes.Where(node => node != null).ToArray();
            PackageGraphCategoryStatusSummary overviewStatusSummary =
                PackageGraphCategoryStatusSummary.Create(graphNodes);
            rows.Add(new PackageGraphGroupNavigationRow(
                "overview",
                "Deucarian Overview",
                FormatEcosystemOverviewGroupStatusSummary(overviewStatusSummary),
                overviewStatusSummary,
                "package-installer",
                "Navigate to Deucarian Overview",
                isOverview: true,
                isSelected: navigationState.IsOverview,
                hasAttention: overviewStatusSummary.AttentionCount > 0));

            if (graph == null)
            {
                return rows;
            }

            string activeTopLevelGroupId = ResolveTopLevelGroupId(
                graph,
                string.IsNullOrWhiteSpace(navigationState.FocusedGroupId)
                    ? GetGraphPackageGroupId(graph, navigationState.FocusedPackageId)
                    : navigationState.FocusedGroupId);

            foreach (PackageGraphGroup group in graph.Groups
                         .Where(group => group != null && string.IsNullOrWhiteSpace(group.ParentGroupId))
                         .OrderBy(group => group.SortOrder)
                         .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                PackageGraphNode[] descendants = graph.GetDescendantPackages(group.Id)
                    .Where(node => node != null)
                    .ToArray();
                PackageGraphCategoryStatusSummary groupStatusSummary =
                    PackageGraphCategoryStatusSummary.Create(descendants);
                rows.Add(new PackageGraphGroupNavigationRow(
                    group.Id,
                    group.DisplayName,
                    FormatEcosystemOverviewGroupStatusSummary(groupStatusSummary),
                    groupStatusSummary,
                    group.IconKey,
                    group.Description,
                    isOverview: false,
                    isSelected: string.Equals(group.Id, activeTopLevelGroupId, StringComparison.OrdinalIgnoreCase),
                    hasAttention: groupStatusSummary.AttentionCount > 0));
            }

            return rows;
        }

        private static string GetGraphPackageGroupId(PackageGraphModel graph, string packageId)
        {
            return graph != null &&
                   !string.IsNullOrWhiteSpace(packageId) &&
                   graph.TryGetNode(packageId, out PackageGraphNode node)
                ? node.GroupId
                : string.Empty;
        }

        private static string ResolveTopLevelGroupId(PackageGraphModel graph, string groupId)
        {
            if (graph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return string.Empty;
            }

            string currentGroupId = groupId;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   graph.TryGetGroup(currentGroupId, out PackageGraphGroup group))
            {
                if (string.IsNullOrWhiteSpace(group.ParentGroupId))
                {
                    return group.Id;
                }

                currentGroupId = group.ParentGroupId;
            }

            return string.Empty;
        }

        private static void DrawSingleLineLabel(Rect rect, GUIContent content, GUIStyle style)
        {
            if (style == null)
            {
                GUI.Label(rect, content);
                return;
            }

            bool previousWordWrap = style.wordWrap;
            TextClipping previousClipping = style.clipping;

            try
            {
                style.wordWrap = false;
                style.clipping = TextClipping.Clip;
                GUI.Label(rect, content, style);
            }
            finally
            {
                style.wordWrap = previousWordWrap;
                style.clipping = previousClipping;
            }
        }

        private void SynchronizeDetailsGroupHover(string hoveredGroupId)
        {
            if (_graphView == null || !ShouldSynchronizeDetailsHover(Event.current.type))
            {
                return;
            }

            string nextGroupId = hoveredGroupId ?? string.Empty;

            if (string.Equals(
                    _detailsPreviewedGraphGroupId,
                    nextGroupId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ClearDetailsGraphHover();
            _detailsPreviewedGraphGroupId = nextGroupId;

            if (!string.IsNullOrWhiteSpace(_detailsPreviewedGraphGroupId))
            {
                _graphView.SetExternalGroupHover(_detailsPreviewedGraphGroupId);
            }
        }

        private void ClearDetailsGraphHover()
        {
            if (_graphView == null || string.IsNullOrWhiteSpace(_detailsPreviewedGraphGroupId))
            {
                _detailsPreviewedGraphGroupId = string.Empty;
                return;
            }

            string previousGroupId = _detailsPreviewedGraphGroupId;
            _detailsPreviewedGraphGroupId = string.Empty;
            _graphView.ClearExternalGroupHover(previousGroupId);
        }

        private void ClearGraphHoverState()
        {
            _detailsPreviewedGraphGroupId = string.Empty;
            _graphView?.ClearHoverState();
        }

        private static bool ShouldSynchronizeDetailsHover(EventType eventType)
        {
            return eventType == EventType.Repaint ||
                   eventType == EventType.MouseMove ||
                   eventType == EventType.MouseDrag ||
                   eventType == EventType.MouseDown ||
                   eventType == EventType.MouseUp ||
                   eventType == EventType.MouseLeaveWindow;
        }

        private string GetActiveFilterSummary()
        {
            if (_visibilityFilterState == null || _visibilityFilterState.IsDefault)
            {
                return "All packages";
            }

            List<string> parts = new List<string>();

            if (!_visibilityFilterState.ShowInstalled)
            {
                parts.Add("Installed hidden");
            }

            if (!_visibilityFilterState.ShowNotInstalled)
            {
                parts.Add("Not installed hidden");
            }

            if (!string.IsNullOrWhiteSpace(_visibilityFilterState.SearchText))
            {
                parts.Add("Search: " + _visibilityFilterState.SearchText);
            }

            return parts.Count == 0 ? "All packages" : string.Join(", ", parts.ToArray());
        }

        private void DrawPackageDetails(PackageDefinition packageDefinition)
        {
            DrawDetailHeader(packageDefinition);
            DrawStatusPanel(packageDefinition);
            DrawRequirementsPanel(packageDefinition);
            DrawChannelPanel(packageDefinition);
            DrawActionsPanel(packageDefinition);
            DrawOptionalCompanionsPanel(packageDefinition);
            DrawExtrasPanel(packageDefinition);
            DrawAdvancedPanel(packageDefinition);
        }

        private void DrawGraphGroupDetails(PackageGraphGroup group)
        {
            PackageGraphNode[] descendants = GetGraphGroupDescendantPackages(group.Id).ToArray();
            PackageGraphGroup[] childGroups = GetGraphChildGroups(group.Id).ToArray();
            PackageGraphNode[] directPackages = descendants
                .Where(node => string.Equals(node.GroupId, group.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PackageDefinition[] missingPackages = descendants
                .Where(node => node != null && !node.IsInstalled && node.PackageDefinition != null)
                .Select(node => node.PackageDefinition)
                .Distinct()
                .ToArray();
            PackageDefinition[] packagesWithUpdates = descendants
                .Where(node => node != null &&
                               node.Status == PackageGraphNodeStatus.UpdateAvailable &&
                               node.PackageDefinition != null)
                .Select(node => node.PackageDefinition)
                .Distinct()
                .ToArray();
            int installedCount = descendants.Count(node => node.IsInstalled);
            int updateCount = descendants.Count(node => node.Status == PackageGraphNodeStatus.UpdateAvailable);
            int missingCount = descendants.Count(node =>
                node.Status == PackageGraphNodeStatus.NotInstalled ||
                node.Status == PackageGraphNodeStatus.Missing ||
                node.Status == PackageGraphNodeStatus.Warning);

            DrawPanel("Group", () =>
            {
                EditorGUILayout.LabelField(group.DisplayName, _titleStyle);

                if (!string.IsNullOrWhiteSpace(group.Description))
                {
                    EditorGUILayout.LabelField(group.Description, _subtitleStyle);
                }

                DrawKeyValueRow("Packages", descendants.Length.ToString());
                DrawKeyValueRow("Installed", installedCount.ToString());
                DrawKeyValueRow("Missing", missingCount.ToString());
                DrawKeyValueRow("Updates", updateCount.ToString());
            }, GUILayout.ExpandWidth(true));

            DrawPanel("Direct Children", () =>
            {
                if (childGroups.Length == 0 && directPackages.Length == 0)
                {
                    EditorGUILayout.LabelField("No visible direct children.", _mutedMiniLabelStyle);
                    return;
                }

                foreach (PackageGraphGroup childGroup in childGroups)
                {
                    EditorGUILayout.LabelField(childGroup.DisplayName + " group", _miniLabelStyle);
                }

                foreach (PackageGraphNode packageNode in directPackages)
                {
                    EditorGUILayout.LabelField(packageNode.DisplayName, _miniLabelStyle);
                }
            }, GUILayout.ExpandWidth(true));

            if (missingPackages.Length > 0 || packagesWithUpdates.Length > 0)
            {
                DrawPanel("Actions", () =>
                {
                    if (missingPackages.Length > 0 &&
                        DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                            DeucarianEditorIconIds.Download,
                            "Install missing (" + missingPackages.Length + ")",
                            "Install every missing package in this group.",
                            !IsAnyOperationBusy(),
                            true,
                            GUILayout.ExpandWidth(true)))
                    {
                        InstallGraphGroupPackages(group, missingPackages);
                    }

                    if (packagesWithUpdates.Length > 0 &&
                        DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                            DeucarianEditorIconIds.Update,
                            "Update available (" + packagesWithUpdates.Length + ")",
                            "Update every package with an available update in this group.",
                            !IsAnyOperationBusy(),
                            GUILayout.ExpandWidth(true)))
                    {
                        UpdateGraphGroupPackages(group, packagesWithUpdates);
                    }
                }, GUILayout.ExpandWidth(true));
            }
        }

        private void DrawIntegrationDetails(PackageDefinition packageDefinition)
        {
            DrawDetailHeader(packageDefinition);
            DrawStatusPanel(packageDefinition);
            DrawRequirementsPanel(packageDefinition);
            DrawChannelPanel(packageDefinition);
            DrawActionsPanel(packageDefinition);
            DrawOptionalCompanionsPanel(packageDefinition);
            DrawExtrasPanel(packageDefinition);
            DrawAdvancedPanel(packageDefinition);
        }

        private void InstallGraphGroupPackages(
            PackageGraphGroup group,
            IReadOnlyCollection<PackageDefinition> packageDefinitions)
        {
            if (_packageDependencyInstaller == null || packageDefinitions == null || packageDefinitions.Count == 0)
            {
                return;
            }

            _activeActionKind = PackageInstallerActionKind.InstallAll;
            _cancelingActionKind = PackageInstallerActionKind.None;
            _packageDependencyInstaller.InstallManyWithDependencies(
                packageDefinitions,
                GetSelectedChannel,
                "Install missing in " + group.DisplayName);
            ClearActiveActionIfIdle();
            UpdateViewVisibility();
        }

        private void UpdateGraphGroupPackages(
            PackageGraphGroup group,
            IReadOnlyCollection<PackageDefinition> packageDefinitions)
        {
            if (_packageDependencyInstaller == null || packageDefinitions == null || packageDefinitions.Count == 0)
            {
                return;
            }

            TrackPendingUpdateStatusInvalidations(packageDefinitions);
            _activeActionKind = PackageInstallerActionKind.UpdateAll;
            _cancelingActionKind = PackageInstallerActionKind.None;
            _packageDependencyInstaller.UpdateManyWithDependencies(
                packageDefinitions,
                GetSelectedChannel,
                "Update available in " + group.DisplayName);

            if (!ShouldRetainPendingUpdateStatusInvalidations(
                    _packageInstallService != null && _packageInstallService.IsBusy,
                    _packageDependencyInstaller != null &&
                    _packageDependencyInstaller.IsAwaitingPreflight))
            {
                _pendingUpdateStatusInvalidationPackageIds.Clear();
            }

            ClearActiveActionIfIdle();
            UpdateViewVisibility();
        }

        private void ConfirmContextualOperation(
            PackageDependencyInstallPlan plan,
            string operationName,
            Action<bool> completed)
        {
            if (plan == null || !plan.IsValid)
            {
                ShowInformationDialog(
                    "Package operation unavailable",
                    plan != null && !string.IsNullOrWhiteSpace(plan.ErrorMessage)
                        ? plan.ErrorMessage
                        : "The package operation could not be planned.",
                    DeucarianEditorIconIds.Error,
                    () => completed?.Invoke(false));
                return;
            }

            if (!plan.RequiresPreflight)
            {
                completed?.Invoke(true);
                return;
            }

            List<string> riskLabels = new List<string>();
            if (plan.IsMultiStep) riskLabels.Add("multiple package steps");
            if (plan.IsBulk) riskLabels.Add("multiple requested packages");
            if (plan.HasMigrationRisk) riskLabels.Add("source/channel migration");
            if (plan.HasDowngradeRisk) riskLabels.Add("possible downgrade");
            if (plan.HasChannelFallback) riskLabels.Add("channel fallback");
            if (plan.HasConflict) riskLabels.Add("channel conflict");
            if (plan.HasDestructiveRisk) riskLabels.Add("destructive reinstall/remove behavior");

            List<string> lines = new List<string>
            {
                "Review " + plan.Steps.Count + " planned package step(s).",
                riskLabels.Count == 0 ? string.Empty : "Attention: " + string.Join(", ", riskLabels.ToArray()),
                string.Empty
            };
            foreach (PackageDependencyInstallStep step in plan.Steps)
            {
                string channelLabel = step.RequestedChannel == step.Channel
                    ? GetChannelLabel(step.Channel)
                    : GetChannelLabel(step.RequestedChannel) + " requested -> " +
                      GetChannelLabel(step.Channel) + " target";
                lines.Add(
                    "- " + step.PackageDefinition.DisplayName +
                    " [" + channelLabel + "]" +
                    (step.IsDependency ? " - dependency" : string.Empty));
                lines.Add("  " + step.TargetUrl);
            }

            if (plan.Messages.Count > 0)
            {
                lines.Add(string.Empty);
                lines.AddRange(plan.Messages.Where(message => !string.IsNullOrWhiteSpace(message)));
            }

            var continueAction = new DeucarianEditorDialogAction(
                "continue",
                "Continue",
                DeucarianEditorIconIds.Play,
                DeucarianEditorDialogActionStyle.Primary);
            var cancelAction = new DeucarianEditorDialogAction(
                "cancel",
                "Cancel",
                DeucarianEditorIconIds.Stop);
            var options = new DeucarianEditorDialogOptions(
                operationName,
                lines[0] + (string.IsNullOrWhiteSpace(lines[1]) ? string.Empty : "\n" + lines[1]),
                DeucarianEditorIconIds.Warning,
                new[] { continueAction, cancelAction })
            {
                Details = string.Join("\n", lines.Skip(3).Where(line => line != null).ToArray()).Trim(),
                DefaultActionId = continueAction.Id,
                CancelActionId = cancelAction.Id
            };
            if (!TryShowManagedDialog(
                    options,
                    result => completed?.Invoke(
                        !result.WasCanceled &&
                        string.Equals(result.ActionId, continueAction.Id, StringComparison.Ordinal))))
            {
                completed?.Invoke(false);
            }
        }

        private void ShowInformationDialog(
            string title,
            string message,
            string iconId,
            Action completed = null)
        {
            var okAction = new DeucarianEditorDialogAction(
                "ok",
                "OK",
                DeucarianEditorIconIds.Check,
                DeucarianEditorDialogActionStyle.Primary);
            var options = new DeucarianEditorDialogOptions(
                title,
                message,
                iconId,
                new[] { okAction })
            {
                DefaultActionId = okAction.Id,
                CancelActionId = okAction.Id
            };
            if (!TryShowManagedDialog(options, _ => completed?.Invoke()))
            {
                completed?.Invoke();
            }
        }

        private bool TryShowManagedDialog(
            DeucarianEditorDialogOptions options,
            Action<DeucarianEditorDialogResult> completed)
        {
            if (options == null || this == null)
            {
                return false;
            }

            if (_confirmationState == null)
            {
                _confirmationState = new PackageInstallerConfirmationState();
            }

            if (!_confirmationState.TryBegin(out long generation))
            {
                return false;
            }

            try
            {
                EditorWindow dialogWindow = DeucarianEditorDialog.Show(options, result =>
                {
                    if (_confirmationState == null ||
                        !_confirmationState.TryComplete(generation))
                    {
                        return;
                    }

                    _activeConfirmationWindow = null;
                    if (this == null)
                    {
                        return;
                    }

                    try
                    {
                        completed?.Invoke(result);
                    }
                    finally
                    {
                        UpdateViewVisibility();
                        ClearActiveActionIfIdle();
                        Repaint();
                    }
                });

                if (_confirmationState.IsCurrent(generation))
                {
                    _activeConfirmationWindow = dialogWindow;
                }
                else if (dialogWindow != null)
                {
                    dialogWindow.Close();
                }

                UpdateViewVisibility();
                Repaint();
                return true;
            }
            catch
            {
                _confirmationState.CancelPending();
                _activeConfirmationWindow = null;
                throw;
            }
        }

        private bool DismissPendingConfirmation(bool refreshUi = true)
        {
            if (_confirmationState == null || !_confirmationState.CancelPending())
            {
                return false;
            }

            EditorWindow dialogWindow = _activeConfirmationWindow;
            _activeConfirmationWindow = null;
            if (dialogWindow != null)
            {
                dialogWindow.Close();
            }

            if (refreshUi && this != null)
            {
                UpdateViewVisibility();
                Repaint();
            }

            return true;
        }

        private float GetDetailsContentWidth()
        {
            float graphDetailsContentWidth = _graphDetailsContainer == null
                ? 0f
                : _graphDetailsContainer.contentRect.width;

            return ResolveDetailsContentWidth(
                position.width,
                _viewMode == InstallerViewMode.EcosystemGraph,
                graphDetailsContentWidth);
        }

        private static float ResolveDetailsContentWidth(
            float windowWidth,
            bool isEcosystemGraph,
            float graphDetailsContentWidth)
        {
            if (isEcosystemGraph &&
                graphDetailsContentWidth > 0f &&
                !float.IsNaN(graphDetailsContentWidth) &&
                !float.IsInfinity(graphDetailsContentWidth))
            {
                return graphDetailsContentWidth;
            }

            return Mathf.Max(0f, windowWidth - SidebarWidth - 56f);
        }

        private static bool ShouldStackDetailsActions(float detailsContentWidth)
        {
            return detailsContentWidth < DetailsActionsStackWidth;
        }

        private void DrawDetailHeader(PackageDefinition packageDefinition)
        {
            VisualStatus status = GetPackageVisualStatus(packageDefinition);

            DrawPanel("Overview", () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect iconRect = GUILayoutUtility.GetRect(48f, 42f, GUILayout.Width(48f), GUILayout.Height(42f));
                    DrawPackageIcon(
                        iconRect,
                        packageDefinition,
                        packageDefinition.IsIntegration ? VisualStatusKind.Integration : status.Kind);

                    GUILayout.Space(8f);

                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                    {
                        string displayName = GetDetailDisplayName(packageDefinition);
                        EditorGUILayout.LabelField(
                            new GUIContent(displayName, displayName),
                            _titleStyle,
                            GUILayout.ExpandWidth(true));

                        if (!string.IsNullOrWhiteSpace(packageDefinition.Description))
                        {
                            EditorGUILayout.LabelField(
                                new GUIContent(packageDefinition.Description, packageDefinition.Description),
                                _subtitleStyle);
                        }

                        if (packageDefinition.HasDisplayVersion)
                        {
                            DrawKeyValueRow("Version", packageDefinition.DisplayVersion);
                        }
                    }

                    GUILayout.Space(8f);
                    DrawStatusBadge(status.Label, status.Kind, GUILayout.Width(132f));
                }
            }, GUILayout.ExpandWidth(true));
        }

        private void DrawPackageIcon(Rect rect, PackageDefinition packageDefinition, VisualStatusKind statusKind)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Color color = GetStatusColor(statusKind);
                DeucarianEditorVisualShell.DrawInsetSurface(
                    rect,
                    DeucarianEditorColors.WithAlpha(color, 0.12f),
                    DeucarianEditorColors.WithAlpha(color, 0.58f),
                    6f);
            }

            Texture2D icon = DeucarianEditorIcons.GetPackageIcon(GetPackageIconKey(packageDefinition));
            Rect iconRect = new Rect(rect.x + 6f, rect.y + 3f, rect.width - 12f, rect.height - 6f);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        }

        private static string GetPackageIconKey(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return DeucarianEditorIconIds.Package;
            }

            if (!string.IsNullOrWhiteSpace(packageDefinition.IconKey))
            {
                return packageDefinition.IconKey.Trim();
            }

            if (string.IsNullOrWhiteSpace(packageDefinition.PackageId))
            {
                return DeucarianEditorIconIds.Package;
            }

            const string prefix = "com.deucarian.";
            string packageId = packageDefinition.PackageId.Trim();
            return packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? packageId.Substring(prefix.Length)
                : packageId;
        }

        private static string GetDetailDisplayName(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            return packageDefinition.DisplayName;
        }

        private void DrawStatusPanel(PackageDefinition packageDefinition)
        {
            DrawPanel(packageDefinition.IsIntegration ? "Integration Status" : "Status", () =>
            {
                DrawPackageStatusContent(packageDefinition);
            }, GUILayout.ExpandWidth(true));
        }

        private void DrawPackageStatusContent(PackageDefinition packageDefinition)
        {
            VisualStatus status = GetPackageVisualStatus(packageDefinition);
            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

            DrawStatusBadge(status.Label, status.Kind, GUILayout.Width(150f));
            GUILayout.Space(6f);
            DrawKeyValueRow("Domain", GetPackageHierarchyPath(packageDefinition));
            DrawKeyValueRow("Package kind", GetPackageKindDisplayName(packageDefinition));
            DrawKeyValueRow("Package ID", packageDefinition.PackageId);

            if (_packageDetectionService.TryGetInstalledPackage(
                    packageDefinition.PackageId,
                    out PackageManagerPackageInfo packageInfo))
            {
                DrawKeyValueRow("Package", "Installed");
                DrawKeyValueRow("Version", GetPackageVersionText(packageInfo.version, updateStatus));
            }
            else
            {
                DrawKeyValueRow("Package", "Not installed");
                DrawKeyValueRow("Version", "-");
            }

            DrawKeyValueRow("Update", GetUpdateStatusText(updateStatus));
            DrawKeyValueRow("Installed rev", string.IsNullOrWhiteSpace(updateStatus.ShortInstalledRevision) ? "-" : updateStatus.ShortInstalledRevision);
            DrawKeyValueRow("Latest rev", string.IsNullOrWhiteSpace(updateStatus.ShortLatestRevision) ? "-" : updateStatus.ShortLatestRevision);

            if (updateStatus.HasUnbumpedPackageVersionWarning)
            {
                DrawInlineHelp(updateStatus.PackageVersionWarningMessage, VisualStatusKind.UpdateAvailable);
            }
            else if ((updateStatus.IsSourceMigrationAvailable || updateStatus.IsReloadPending) &&
                     !string.IsNullOrWhiteSpace(updateStatus.Message))
            {
                DrawInlineHelp(updateStatus.Message, VisualStatusKind.UpdateAvailable);
            }
            else if (updateStatus.Kind == PackageUpdateStatusKind.CannotDetermine && !string.IsNullOrWhiteSpace(updateStatus.Message))
            {
                DrawInlineHelp(updateStatus.Message, VisualStatusKind.Info);
            }
            else if (updateStatus.Kind == PackageUpdateStatusKind.Failed && !string.IsNullOrWhiteSpace(updateStatus.Message))
            {
                DrawInlineHelp(updateStatus.Message, VisualStatusKind.Failed);
            }
        }

        private void DrawChannelPanel(PackageDefinition packageDefinition)
        {
            DrawPanel("Channel", () =>
            {
                PackageChannel selectedChannel = GetSelectedChannel(packageDefinition);
                string selectedUrl = packageDefinition.GetUrl(selectedChannel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Selected",
                        _mutedMiniLabelStyle,
                        GUILayout.Width(DeucarianEditorWorkbenchGUI.DetailLabelWidth));
                    DrawChannelPopup(packageDefinition);
                    GUILayout.Space(6f);
                    DrawStatusBadge(GetChannelLabel(selectedChannel), VisualStatusKind.Info, GUILayout.Width(104f));
                    GUILayout.FlexibleSpace();
                }

                GUILayout.Space(6f);

                if (!string.IsNullOrWhiteSpace(selectedUrl))
                {
                    EditorGUILayout.LabelField(
                        GetChannelLabel(selectedChannel) + " installs from the configured package URL/ref.",
                        _mutedMiniLabelStyle);
                }
                else
                {
                    DrawInlineHelp("No package URL is configured for this channel.", VisualStatusKind.Failed);
                }

                DrawKeyValueRow("Stable", string.IsNullOrWhiteSpace(packageDefinition.StableUrl) ? "Not configured" : "Configured");
                DrawKeyValueRow("Development", string.IsNullOrWhiteSpace(packageDefinition.DevelopmentUrl) ? "Not configured" : "Configured");

                PackageChannelSelection projectSelection = _stateRepository != null
                    ? _stateRepository.GetProjectChannelSelection()
                    : PackageChannelSelection.None;
                PackageChannelSelection packageSelection = _stateRepository != null
                    ? _stateRepository.GetPackageChannelSelection(packageDefinition.PackageId)
                    : PackageChannelSelection.None;
                PackageChannel installedChannel = PackageChannel.Stable;
                string installedSourceReason = string.Empty;
                bool hasInstalledChannel = _packageDetectionService != null &&
                    _packageDetectionService.TryGetInstalledPackageChannel(
                        packageDefinition,
                        out installedChannel,
                        out installedSourceReason);
                string provenance = GetContextualChannelProvenance(
                    packageDefinition,
                    projectSelection,
                    packageSelection,
                    hasInstalledChannel,
                    installedChannel,
                    installedSourceReason);

                if (!string.IsNullOrWhiteSpace(provenance))
                {
                    GUILayout.Space(6f);
                    DrawKeyValueRow("Source", provenance);

                    if (packageSelection.HasValue &&
                        DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                            DeucarianEditorIconIds.Undo,
                            "Reset package override",
                            "Remove the package-specific channel override.",
                            true,
                            GUILayout.ExpandWidth(true)))
                    {
                        ResetPackageChannelOverride(packageDefinition);
                    }
                }
            }, GUILayout.ExpandWidth(true));
        }

        internal static string GetContextualChannelProvenance(
            PackageDefinition packageDefinition,
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection,
            bool hasInstalledChannel,
            PackageChannel installedChannel,
            string installedSourceReason)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            if (hasInstalledChannel && installedChannel == PackageChannel.Custom)
            {
                return string.IsNullOrWhiteSpace(installedSourceReason)
                    ? "Custom installed source"
                    : "Custom installed source - " + installedSourceReason.Trim();
            }

            PackageChannelSelection explicitSelection = GetLatestExplicitChannelSelection(
                projectSelection,
                packageSelection);

            if (!explicitSelection.HasValue)
            {
                return string.Empty;
            }

            bool packageOverride = packageSelection.HasValue &&
                                   (!projectSelection.HasValue ||
                                    packageSelection.ChangedAtUtcTicks > projectSelection.ChangedAtUtcTicks);
            string scope = packageOverride ? "Package override" : "Project override";

            if (explicitSelection.Channel == PackageChannel.Development &&
                !packageDefinition.HasDevelopmentUrl)
            {
                return scope + " requested Development - using Stable fallback";
            }

            return scope + " - " + GetChannelLabel(explicitSelection.Channel);
        }

        private void ResetPackageChannelOverride(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null || _stateRepository == null)
            {
                return;
            }

            _stateRepository.ClearPackageChannel(packageDefinition.PackageId);
            _packageUpdateCheckService?.Invalidate(packageDefinition.PackageId);
            InvalidateGraphModelCache("package channel override reset");
            RefreshGraphView("package channel override reset");
        }

        private void DrawRequirementsPanel(PackageDefinition packageDefinition)
        {
            IReadOnlyList<PackageReverseDependency> dependents =
                _packageReverseDependencyResolver != null
                    ? _packageReverseDependencyResolver.Resolve(
                        packageDefinition.PackageId,
                        _packageDetectionService?.InstalledPackageIds)
                    : Array.Empty<PackageReverseDependency>();

            if (packageDefinition.Dependencies.Count == 0 && dependents.Count == 0)
            {
                return;
            }

            DrawPanel("Requirements", () =>
            {
                if (packageDefinition.Dependencies.Count > 0)
                {
                    EditorGUILayout.LabelField("Dependencies", _miniLabelStyle);

                    foreach (string dependencyId in packageDefinition.Dependencies)
                    {
                        DrawRequirementRow(dependencyId);
                    }
                }

                if (dependents.Count > 0)
                {
                    if (packageDefinition.Dependencies.Count > 0)
                    {
                        GUILayout.Space(6f);
                    }

                    EditorGUILayout.LabelField("Required by", _miniLabelStyle);

                    foreach (PackageReverseDependency dependent in dependents)
                    {
                        DrawKeyValueRow(
                            dependent.DisplayName,
                            dependent.Source == PackageReverseDependencySource.Registry
                                ? "Registry relationship"
                                : "Installed dependency");
                    }
                }
            }, GUILayout.ExpandWidth(true));
        }

        private void DrawRequirementRow(string dependencyId)
        {
            if (!PackageRegistryProvider.TryGetPackage(dependencyId, out PackageDefinition dependencyDefinition))
            {
                DrawKeyValueRow(dependencyId, "Not registered");
                return;
            }

            VisualStatus status = GetPackageVisualStatus(dependencyDefinition);
            Rect rowRect = GUILayoutUtility.GetRect(1f, 28f, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                DeucarianEditorVisualShell.DrawInsetSurface(rowRect, _sampleRowBackgroundColor, _separatorColor, 6f);
            }

            Rect markerRect = new Rect(rowRect.x + 8f, rowRect.y + 5f, 28f, 18f);
            DrawInlineIcon(markerRect, status.IconId, status.Kind, status.Label);

            Rect nameRect = new Rect(rowRect.x + 44f, rowRect.y + 5f, rowRect.width - 164f, 18f);
            GUI.Label(
                nameRect,
                new GUIContent(dependencyDefinition.DisplayName, GetPackageTooltip(dependencyDefinition)),
                _rowTitleStyle);

            Rect statusRect = new Rect(rowRect.xMax - 108f, rowRect.y + 5f, 96f, 18f);
            DrawStatusBadge(statusRect, status.Label, status.Kind, _rowStatusStyle);
        }

        private void DrawActionsPanel(PackageDefinition packageDefinition)
        {
            DrawPanel("Actions", () =>
            {
                DrawPackageActionButtons(packageDefinition, true);
            });
        }

        private void DrawPackageActionButtons(PackageDefinition packageDefinition, bool includeNotes)
        {
            bool installed = _packageDetectionService.IsInstalled(packageDefinition.PackageId);
            bool queuedOrInstalling = _packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId);
            bool actionsBusy = IsAnyOperationBusy();
            bool stackActions = ShouldStackDetailsActions(GetDetailsContentWidth());
            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));
            IReadOnlyList<PackageReverseDependency> installedDependents = installed
                ? ResolveInstalledDependents(packageDefinition)
                : Array.Empty<PackageReverseDependency>();

            if (includeNotes)
            {
                if (!installed)
                {
                    PackageDefinition[] missingDependencies = _packageDependencyInstaller.GetMissingDependencies(packageDefinition);

                    if (missingDependencies.Length > 0)
                    {
                        DrawInlineHelp(
                            "Missing dependencies will be installed first: " +
                            string.Join(", ", missingDependencies.Select(package => package.DisplayName).ToArray()),
                            VisualStatusKind.Info);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Install this package from the selected channel.", _mutedMiniLabelStyle);
                    }
                }
                else
                {
                    if (installedDependents.Count > 0)
                    {
                        DrawInlineHelp(
                            "This package is required by installed package(s): " +
                            string.Join(", ", installedDependents
                                .Select(dependent => dependent.DisplayName)
                                .ToArray()) +
                            ". Removing it may break those packages.",
                            VisualStatusKind.UpdateAvailable);
                    }
                    else if (updateStatus.Kind == PackageUpdateStatusKind.SwitchAvailable)
                    {
                        DrawInlineHelp(
                            "A switch is available for the selected channel.",
                            VisualStatusKind.UpdateAvailable);
                    }
                    else if (updateStatus.IsUpdateAvailable)
                    {
                        DrawInlineHelp("An update is available for the selected channel.", VisualStatusKind.UpdateAvailable);
                    }
                    else if (updateStatus.IsSourceMigrationAvailable)
                    {
                        string migrationHelp = PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId)
                            ? "This registry-installed Package Installer must be migrated through Bootstrap. " +
                              "Bootstrap: " + GetBootstrapGitUrl(GetSelectedChannel(packageDefinition)) +
                              ". Then open " + BootstrapMenuPath + "."
                            : "Migrate this registry-installed package to the selected catalog Git URL.";
                        DrawInlineHelp(migrationHelp, VisualStatusKind.UpdateAvailable);
                    }
                    else if (updateStatus.IsReloadPending)
                    {
                        DrawInlineHelp(updateStatus.Message, VisualStatusKind.UpdateAvailable);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Package is installed. Reinstall uses the selected channel URL/ref.", _mutedMiniLabelStyle);
                    }
                }

                GUILayout.Space(6f);
            }

            if (!installed)
            {
                string buttonLabel = packageDefinition.IsIntegration ? "Install Integration" : "Install";
                if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                        packageDefinition.IsIntegration
                            ? DeucarianEditorIconIds.Integration
                            : DeucarianEditorIconIds.Download,
                        buttonLabel,
                        "Install this package and any missing required dependencies.",
                        !queuedOrInstalling && !actionsBusy,
                        true,
                        stackActions ? GUILayout.ExpandWidth(true) : GUILayout.Width(140f)))
                {
                    _packageDependencyInstaller.InstallWithDependencies(packageDefinition, GetSelectedChannel);
                }

                return;
            }

            if (stackActions)
            {
                DrawInstalledActionButtonsStacked(
                    packageDefinition,
                    updateStatus,
                    installedDependents,
                    queuedOrInstalling,
                    actionsBusy);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawInstalledActionButtonsInline(
                    packageDefinition,
                    updateStatus,
                    installedDependents,
                    queuedOrInstalling,
                    actionsBusy);
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawInstalledActionButtonsInline(
            PackageDefinition packageDefinition,
            PackageUpdateStatus updateStatus,
            IReadOnlyList<PackageReverseDependency> installedDependents,
            bool queuedOrInstalling,
            bool actionsBusy)
        {
            string primaryLabel = GetUpdateActionLabel(updateStatus, GetSelectedChannel(packageDefinition));
            if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                    GetPrimaryPackageActionIcon(updateStatus),
                    primaryLabel,
                    primaryLabel,
                    HasPrimaryPackageAction(updateStatus) && !queuedOrInstalling && !actionsBusy,
                    true,
                    GUILayout.Width(170f)))
            {
                RunPrimaryPackageAction(packageDefinition, updateStatus);
            }

            bool canReinstall = !queuedOrInstalling &&
                                !actionsBusy &&
                                !updateStatus.IsSourceMigrationAvailable &&
                                !updateStatus.IsReloadPending;
            if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                    DeucarianEditorIconIds.Refresh,
                    "Reinstall",
                    "Reinstall this package from the selected channel.",
                    canReinstall,
                    GUILayout.Width(116f)))
            {
                ReinstallPackage(packageDefinition);
            }

            if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                    DeucarianEditorIconIds.Remove,
                    "Remove",
                    "Remove this package from the Unity project.",
                    CanRemovePackage(installedDependents, queuedOrInstalling, actionsBusy),
                    GUILayout.Width(112f)))
            {
                RemovePackage(packageDefinition);
            }
        }

        private void DrawInstalledActionButtonsStacked(
            PackageDefinition packageDefinition,
            PackageUpdateStatus updateStatus,
            IReadOnlyList<PackageReverseDependency> installedDependents,
            bool queuedOrInstalling,
            bool actionsBusy)
        {
            string primaryLabel = GetUpdateActionLabel(updateStatus, GetSelectedChannel(packageDefinition));
            if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                    GetPrimaryPackageActionIcon(updateStatus),
                    primaryLabel,
                    primaryLabel,
                    HasPrimaryPackageAction(updateStatus) && !queuedOrInstalling && !actionsBusy,
                    true,
                    GUILayout.ExpandWidth(true)))
            {
                RunPrimaryPackageAction(packageDefinition, updateStatus);
            }

            bool canReinstall = !queuedOrInstalling &&
                                !actionsBusy &&
                                !updateStatus.IsSourceMigrationAvailable &&
                                !updateStatus.IsReloadPending;
            if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                    DeucarianEditorIconIds.Refresh,
                    "Reinstall",
                    "Reinstall this package from the selected channel.",
                    canReinstall,
                    GUILayout.ExpandWidth(true)))
            {
                ReinstallPackage(packageDefinition);
            }

            if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                    DeucarianEditorIconIds.Remove,
                    "Remove",
                    "Remove this package from the Unity project.",
                    CanRemovePackage(installedDependents, queuedOrInstalling, actionsBusy),
                    GUILayout.ExpandWidth(true)))
            {
                RemovePackage(packageDefinition);
            }
        }

        private static string GetPrimaryPackageActionIcon(PackageUpdateStatus updateStatus)
        {
            if (updateStatus == null)
            {
                return DeucarianEditorIconIds.Update;
            }

            if (updateStatus.IsReloadPending)
            {
                return DeucarianEditorIconIds.Refresh;
            }

            if (updateStatus.IsSourceMigrationAvailable)
            {
                return DeucarianEditorIconIds.GitBranch;
            }

            return updateStatus.Kind == PackageUpdateStatusKind.SwitchAvailable
                ? DeucarianEditorIconIds.Compare
                : DeucarianEditorIconIds.Update;
        }

        private void UpdatePackage(PackageDefinition packageDefinition)
        {
            TrackPendingUpdateStatusInvalidation(packageDefinition);
            _packageDependencyInstaller.UpdateWithDependencies(
                packageDefinition,
                GetSelectedChannel);

            if (!_packageInstallService.IsBusy && packageDefinition != null)
            {
                _pendingUpdateStatusInvalidationPackageIds.Remove(packageDefinition.PackageId);
            }

            QueueDeferredUpdateCheck(PackageInstallerActionKind.CheckUpdates);
        }

        private static bool HasPrimaryPackageAction(PackageUpdateStatus status)
        {
            return status != null &&
                   (status.IsUpdateAvailable ||
                    status.IsSourceMigrationAvailable ||
                    status.IsReloadPending);
        }

        private void RunPrimaryPackageAction(
            PackageDefinition packageDefinition,
            PackageUpdateStatus status)
        {
            if (status != null && status.IsReloadPending)
            {
                RetryScriptReload();
                return;
            }

            if (status != null && status.IsSourceMigrationAvailable)
            {
                if (GetSourceMigrationActionForTests(packageDefinition) ==
                    PackageSourceMigrationAction.OpenBootstrap)
                {
                    OpenBootstrapForSourceMigration(packageDefinition);
                }
                else
                {
                    UpdatePackage(packageDefinition);
                }

                return;
            }

            UpdatePackage(packageDefinition);
        }

        internal static PackageSourceMigrationAction GetSourceMigrationActionForTests(
            PackageDefinition packageDefinition)
        {
            return packageDefinition != null &&
                   PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId)
                ? PackageSourceMigrationAction.OpenBootstrap
                : PackageSourceMigrationAction.InstallSelectedGitUrl;
        }

        internal static string GetBootstrapGitUrlForTests(PackageChannel channel)
        {
            return GetBootstrapGitUrl(channel);
        }

        internal static string BootstrapMenuPathForTests => BootstrapMenuPath;

        private void RetryScriptReload()
        {
            const string message =
                "Requested a fresh script compilation. Resolve any Console compile errors so Unity can load the updated Package Installer assembly.";
            CompilationPipeline.RequestScriptCompilation();
            ShowNotification(new GUIContent(message));
            PackageInstallerLog.Install.DiagnosticInfo(message);
        }

        private void OpenBootstrapForSourceMigration(PackageDefinition packageDefinition)
        {
            if (EditorApplication.ExecuteMenuItem(BootstrapMenuPath))
            {
                PackageInstallerLog.Install.DiagnosticInfo(
                    "Opened Bootstrap for Package Installer source migration.");
                return;
            }

            PackageChannel channel = GetSelectedChannel(packageDefinition);
            string bootstrapUrl = GetBootstrapGitUrl(channel);
            string message =
                "Bootstrap is not installed. Add " + bootstrapUrl +
                " with Unity Package Manager, then open " + BootstrapMenuPath +
                " to migrate Package Installer safely.";
            ShowNotification(new GUIContent(message));
            PackageInstallerLog.Install.Warning(message);
        }

        private static string GetBootstrapGitUrl(PackageChannel channel)
        {
            return channel == PackageChannel.Development
                ? BootstrapDevelopmentGitUrl
                : BootstrapStableGitUrl;
        }

        private void ReinstallPackage(PackageDefinition packageDefinition)
        {
            _packageDependencyInstaller.ReinstallWithDependencies(
                packageDefinition,
                GetSelectedChannel);
            _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
        }

        private void RemovePackage(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return;
            }

            IReadOnlyList<PackageReverseDependency> dependents =
                ResolveInstalledDependents(packageDefinition);
            string dependentWarning = BuildRemoveDependentWarning(dependents);
            var removeAction = new DeucarianEditorDialogAction(
                "remove",
                "Remove",
                DeucarianEditorIconIds.Remove,
                DeucarianEditorDialogActionStyle.Destructive);
            var cancelAction = new DeucarianEditorDialogAction(
                "cancel",
                "Cancel",
                DeucarianEditorIconIds.Stop);
            var options = new DeucarianEditorDialogOptions(
                "Remove Package",
                "Remove " + packageDefinition.DisplayName + " from this Unity project?",
                DeucarianEditorIconIds.Remove,
                new[] { removeAction, cancelAction })
            {
                Details = dependentWarning.Trim(),
                DefaultActionId = cancelAction.Id,
                CancelActionId = cancelAction.Id
            };
            TryShowManagedDialog(options, result =>
            {
                if (result.WasCanceled ||
                    !string.Equals(result.ActionId, removeAction.Id, StringComparison.Ordinal) ||
                    this == null)
                {
                    return;
                }

                if (_packageInstallService == null ||
                    _packageInstallService.IsBusy ||
                    _packageDetectionService == null ||
                    !_packageDetectionService.IsInstalled(packageDefinition.PackageId))
                {
                    RecordStaleConfirmation(
                        "Remove " + packageDefinition.DisplayName,
                        "Package state changed while the removal confirmation was open.");
                    return;
                }

                _packageInstallService.Remove(packageDefinition);
                _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
            });
        }

        private IReadOnlyList<PackageReverseDependency> ResolveInstalledDependents(
            PackageDefinition packageDefinition)
        {
            if (packageDefinition == null || _packageReverseDependencyResolver == null)
            {
                return Array.Empty<PackageReverseDependency>();
            }

            return _packageReverseDependencyResolver.Resolve(
                packageDefinition.PackageId,
                _packageDetectionService?.InstalledPackageIds);
        }

        internal static bool CanRemovePackageForTests(
            IReadOnlyList<PackageReverseDependency> installedDependents,
            bool queuedOrInstalling,
            bool actionsBusy)
        {
            return CanRemovePackage(installedDependents, queuedOrInstalling, actionsBusy);
        }

        private static bool CanRemovePackage(
            IReadOnlyList<PackageReverseDependency> installedDependents,
            bool queuedOrInstalling,
            bool actionsBusy)
        {
            // Installed dependents are a removal warning, not a hidden hard block.
            // Unity still permits removal and the confirmation dialog must remain reachable.
            return !queuedOrInstalling && !actionsBusy;
        }

        internal static string BuildRemoveDependentWarningForTests(
            IReadOnlyList<PackageReverseDependency> dependents)
        {
            return BuildRemoveDependentWarning(dependents);
        }

        private static string BuildRemoveDependentWarning(
            IReadOnlyList<PackageReverseDependency> dependents)
        {
            return dependents == null || dependents.Count == 0
                ? string.Empty
                : "\n\nInstalled packages that currently depend on it:\n" +
                  string.Join("\n", dependents
                      .Select(dependent => "- " + dependent.DisplayName + " (" + dependent.PackageId + ")")
                      .ToArray()) +
                  "\n\nRemoving it may break those packages.";
        }

        private void DrawOptionalCompanionsPanel(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null || packageDefinition.OptionalCompanions.Count == 0)
            {
                return;
            }

            DrawPanel("Optional Companions", () =>
            {
                EditorGUILayout.LabelField("Install optional tooling that enhances this package without becoming a required dependency.", _mutedMiniLabelStyle);
                GUILayout.Space(6f);

                foreach (string companionId in packageDefinition.OptionalCompanions)
                {
                    if (!PackageRegistryProvider.TryGetPackage(companionId, out PackageDefinition companionDefinition))
                    {
                        DrawInlineHelp("Optional companion is unavailable: " + companionId, VisualStatusKind.Failed);
                        continue;
                    }

                    DrawOptionalCompanionRow(companionDefinition);
                }
            });
        }

        private void DrawOptionalCompanionRow(PackageDefinition companionDefinition)
        {
            bool installed = _packageDetectionService.IsInstalled(companionDefinition.PackageId);
            bool queuedOrInstalling = _packageInstallService.IsQueuedOrInstalling(companionDefinition.PackageId);
            bool actionsBusy = IsAnyOperationBusy();
            VisualStatus status = GetPackageVisualStatus(companionDefinition);

            Rect rect = BeginSurface(
                _sampleRowStyle,
                _sampleRowBackgroundColor,
                _separatorColor,
                GUILayout.ExpandWidth(true));

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect markerRect = GUILayoutUtility.GetRect(30f, 30f, GUILayout.Width(30f), GUILayout.Height(30f));
                DrawInlineIcon(markerRect, status.IconId, status.Kind, status.Label);

                GUILayout.Space(8f);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(companionDefinition.DisplayName, GetPackageTooltip(companionDefinition)),
                        _rowTitleStyle);

                    string description = GetOptionalCompanionDescription(companionDefinition);
                    EditorGUILayout.LabelField(
                        new GUIContent(description, description),
                        _mutedMiniLabelStyle);
                }

                GUILayout.Space(8f);

                string label = installed
                    ? "Installed"
                    : companionDefinition.PackageId == "com.deucarian.diagnostics"
                        ? "Install Diagnostics"
                        : "Install";
                if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                        installed
                            ? DeucarianEditorIconIds.PackageCheck
                            : DeucarianEditorIconIds.Download,
                        label,
                        installed
                            ? "This optional companion is installed."
                            : "Install this optional companion.",
                        !installed && !queuedOrInstalling && !actionsBusy,
                        GUILayout.Width(164f)))
                {
                    _packageDependencyInstaller.InstallWithDependencies(companionDefinition, GetSelectedChannel);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static string GetOptionalCompanionDescription(PackageDefinition companionDefinition)
        {
            if (companionDefinition == null)
            {
                return string.Empty;
            }

            if (companionDefinition.PackageId == "com.deucarian.diagnostics")
            {
                return "Adds runtime/editor diagnostics support.";
            }

            return companionDefinition.Description;
        }

        private void DrawExtrasPanel(PackageDefinition packageDefinition)
        {
            DrawPanel("Extras / Samples", () =>
            {
                bool installed = _packageDetectionService.TryGetInstalledPackage(
                    packageDefinition.PackageId,
                    out PackageManagerPackageInfo packageInfo);
                IReadOnlyList<PackageExtraDefinition> packageSamples = installed
                    ? _packageSampleDiscoveryService.GetSamples(packageInfo)
                    : Array.Empty<PackageExtraDefinition>();
                PackageExtraDefinition[] sampleDefinitions = MergeSampleDefinitions(
                    packageDefinition.Extras,
                    packageSamples);

                if (!installed)
                {
                    if (packageDefinition.Extras.Count == 0)
                    {
                        EditorGUILayout.LabelField("Install this package to discover package samples.", _mutedMiniLabelStyle);
                    }
                    else
                    {
                        DrawInlineHelp("Install this package before importing samples.", VisualStatusKind.Info);
                    }

                    return;
                }

                if (sampleDefinitions.Length == 0)
                {
                    EditorGUILayout.LabelField("No package samples declared in package.json.", _mutedMiniLabelStyle);
                    return;
                }

                EditorGUILayout.LabelField("Import optional samples and examples for this package.", _mutedMiniLabelStyle);
                GUILayout.Space(6f);

                foreach (PackageExtraDefinition extraDefinition in sampleDefinitions)
                {
                    DrawPackageSampleRow(packageDefinition, extraDefinition, packageInfo);
                }
            });
        }

        private void DrawPackageSampleRow(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            PackageSampleImportStatus status = _packageSampleImportService.GetStatus(
                packageDefinition,
                extraDefinition,
                packageInfo);
            Rect rect = BeginSurface(
                _sampleRowStyle,
                _sampleRowBackgroundColor,
                _separatorColor,
                GUILayout.ExpandWidth(true));

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect markerRect = GUILayoutUtility.GetRect(30f, 30f, GUILayout.Width(30f), GUILayout.Height(30f));
                DrawInlineIcon(
                    markerRect,
                    DeucarianEditorIconIds.Sample,
                    VisualStatusKind.Info,
                    "Package sample");

                GUILayout.Space(8f);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(extraDefinition.DisplayName, extraDefinition.DisplayName),
                        _rowTitleStyle);

                    if (!string.IsNullOrWhiteSpace(extraDefinition.Description))
                    {
                        EditorGUILayout.LabelField(
                            new GUIContent(extraDefinition.Description, extraDefinition.Description),
                            _mutedMiniLabelStyle);
                    }

                    string statusText = GetSampleImportStatusText(status);

                    if (!string.IsNullOrWhiteSpace(statusText))
                    {
                        DrawColoredLabel(
                            statusText,
                            _mutedMiniLabelStyle,
                            GetStatusColor(GetSampleImportStatusKind(status)));
                    }
                }

                bool alreadyImported = IsImportedSampleStatus(status) ||
                                       _packageSampleImportService.IsSampleImported(
                                           packageDefinition,
                                           extraDefinition,
                                           packageInfo);

                string buttonLabel = alreadyImported ? "Imported" : "Import";
                if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                        alreadyImported
                            ? DeucarianEditorIconIds.Success
                            : DeucarianEditorIconIds.Download,
                        buttonLabel,
                        alreadyImported
                            ? "This sample has already been imported."
                            : "Import this package sample.",
                        !alreadyImported && !IsAnyOperationBusy(),
                        GUILayout.Width(108f)))
                {
                    _packageSampleImportService.ImportSample(
                        packageDefinition,
                        extraDefinition,
                        packageInfo);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static PackageExtraDefinition[] MergeSampleDefinitions(
            IReadOnlyList<PackageExtraDefinition> registrySamples,
            IReadOnlyList<PackageExtraDefinition> packageSamples)
        {
            List<PackageExtraDefinition> samples = new List<PackageExtraDefinition>();
            HashSet<string> seenSamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddSampleDefinitions(registrySamples, samples, seenSamples);
            AddSampleDefinitions(packageSamples, samples, seenSamples);

            return samples.ToArray();
        }

        private static void AddSampleDefinitions(
            IReadOnlyList<PackageExtraDefinition> sourceSamples,
            ICollection<PackageExtraDefinition> destinationSamples,
            ISet<string> seenSamples)
        {
            if (sourceSamples == null)
            {
                return;
            }

            foreach (PackageExtraDefinition sample in sourceSamples)
            {
                if (sample == null || !seenSamples.Add(GetSampleDefinitionKey(sample)))
                {
                    continue;
                }

                destinationSamples.Add(sample);
            }
        }

        private static string GetSampleDefinitionKey(PackageExtraDefinition sample)
        {
            if (sample == null)
            {
                return string.Empty;
            }

            string samplePath = (sample.SamplePath ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');

            if (!string.IsNullOrWhiteSpace(samplePath))
            {
                return "path:" + samplePath;
            }

            return "name:" + (sample.SampleName ?? string.Empty).Trim() + "|" + (sample.DisplayName ?? string.Empty).Trim();
        }

        private void DrawAdvancedPanel(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return;
            }

            DrawPanel(null, () =>
            {
                if (!DrawAdvancedFoldout(packageDefinition.PackageId))
                {
                    return;
                }

                GUILayout.Space(6f);

                DrawPackageAdvancedFields(packageDefinition);
            });
        }

        private void DrawPackageAdvancedFields(PackageDefinition packageDefinition)
        {
            PackageChannel selectedChannel = GetSelectedChannel(packageDefinition);
            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(packageDefinition, selectedChannel);

            DrawSelectableValue("Package ID", packageDefinition.PackageId);
            DrawSelectableValue("Domain", GetPackageHierarchyPath(packageDefinition));
            DrawSelectableValue("Package kind", GetPackageKindDisplayName(packageDefinition));
            DrawSelectableValue("Selected URL", packageDefinition.GetUrl(selectedChannel));
            DrawSelectableValue("Stable URL", packageDefinition.StableUrl);
            DrawSelectableValue("Development URL", packageDefinition.DevelopmentUrl);
            DrawSelectableValue("Selected ref", GetChannelLabel(selectedChannel));

            if (_packageDetectionService.TryGetInstalledPackage(
                    packageDefinition.PackageId,
                    out PackageManagerPackageInfo packageInfo))
            {
                DrawSelectableValue("Installed source", packageInfo.source.ToString());
                DrawSelectableValue("Installed version", packageInfo.version);
                DrawSelectableValue("Installed path", packageInfo.resolvedPath);
            }

            if (_packageDetectionService.TryGetInstalledPackageReference(
                    packageDefinition.PackageId,
                    out string installedReference))
            {
                DrawSelectableValue("Installed ref", installedReference);
            }

            DrawSelectableValue("Installed rev", updateStatus.InstalledRevision);
            DrawSelectableValue("Latest rev", updateStatus.LatestRevision);
            DrawSelectableValue("Installed version", updateStatus.InstalledVersion);
            DrawSelectableValue("Target version", updateStatus.LatestVersion);
            DrawSelectableValue("Dependencies", packageDefinition.Dependencies.Count == 0
                ? "-"
                : string.Join(", ", packageDefinition.Dependencies.ToArray()));
            DrawSelectableValue("Optional companions", packageDefinition.OptionalCompanions.Count == 0
                ? "-"
                : string.Join(", ", packageDefinition.OptionalCompanions.ToArray()));

            if (!string.IsNullOrWhiteSpace(updateStatus.Message))
            {
                DrawSelectableValue("State", updateStatus.Message);
            }
        }

        private bool DrawAdvancedFoldout(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!_advancedFoldouts.TryGetValue(key, out bool expanded))
            {
                expanded = EditorPrefs.GetBool(GetAdvancedFoldoutPreferenceKey(key), false);
                _advancedFoldouts[key] = expanded;
            }

            bool nextExpanded = EditorGUILayout.Foldout(expanded, "Advanced", true, _foldoutStyle);

            if (nextExpanded != expanded)
            {
                _advancedFoldouts[key] = nextExpanded;
                EditorPrefs.SetBool(GetAdvancedFoldoutPreferenceKey(key), nextExpanded);
            }

            return nextExpanded;
        }

        private string GetAdvancedFoldoutPreferenceKey(string packageId)
        {
            return AdvancedFoldoutPreferencePrefix +
                   Application.dataPath.Replace("\\", "/") +
                   "." +
                   packageId;
        }

        private string GetOperationFooterSummaryLine(OperationProgressView operation)
        {
            string title = GetOperationBarTitle(operation);
            string subtitle = GetOperationBarSubtitle(operation);
            return string.IsNullOrWhiteSpace(subtitle) ? title : title + " - " + subtitle;
        }

        internal static float CalculateOperationDrawerContainerHeightForTests(
            bool expanded,
            int contentLineCount)
        {
            return CalculateOperationDrawerContainerHeight(expanded, contentLineCount);
        }

        private static float CalculateOperationDrawerContainerHeight(
            bool expanded,
            int contentLineCount)
        {
            if (!expanded)
            {
                return 0f;
            }

            return Mathf.Min(
                OperationDrawerExpandedMaxHeight,
                OperationDrawerExpandedBaseHeight + CalculateOperationDrawerScrollHeight(contentLineCount));
        }

        private static float CalculateOperationDrawerScrollHeight(int contentLineCount)
        {
            const float lineHeight = 18f;
            float verticalPadding = OperationBlockPadding;
            int lineCount = Mathf.Max(1, contentLineCount);
            float contentHeight = lineCount * lineHeight + verticalPadding;

            return Mathf.Clamp(contentHeight, OperationDrawerMinHeight, OperationDrawerMaxHeight);
        }

        private static string GetFooterVersionText()
        {
            return PackageInstallerRuntimeIdentity.PackageId + " " + PackageInstallerRuntimeIdentity.Version;
        }

        private int GetOperationDrawerContentLineCount()
        {
            return CountOperationMessageLines(GetOperationDrawerReportText());
        }

        private string GetOperationDrawerReportText()
        {
            IReadOnlyList<PackageInstallerActivityEntry> recentActivity =
                PackageInstallerActivityService.Recent;
            List<string> lines = new List<string>();
            bool packageOperationActive = _packageInstallService != null && _packageInstallService.IsBusy;
            bool liveOperationActive = packageOperationActive ||
                                       (_packageSampleImportService != null && _packageSampleImportService.IsBusy) ||
                                       (_packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking) ||
                                       (_packageDetectionService != null && _packageDetectionService.IsRefreshing);
            string summary = liveOperationActive ? GetLastOperationSummary() : string.Empty;

            if (!string.IsNullOrWhiteSpace(summary))
            {
                lines.Add(summary.Trim());
            }

            IReadOnlyList<string> operationMessages = packageOperationActive
                ? GetLastOperationMessages()
                : Array.Empty<string>();

            if (operationMessages != null)
            {
                foreach (string message in operationMessages)
                {
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        lines.Add(message.Trim());
                    }
                }
            }

            IReadOnlyList<PackageInstallProgressItem> progressItems = packageOperationActive
                ? GetLastProgressItems()
                : Array.Empty<PackageInstallProgressItem>();

            if (progressItems != null)
            {
                foreach (PackageInstallProgressItem item in progressItems)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    string itemMessage = string.IsNullOrWhiteSpace(item.Message)
                        ? item.DisplayName
                        : item.Message;
                    string line = GetProgressItemStateLabel(item.State) + ": " + itemMessage;

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        lines.Add(line.Trim());
                    }
                }
            }

            return MergeLiveOperationWithActivity(
                lines.Count == 0 ? string.Empty : string.Join("\n", lines.ToArray()),
                recentActivity);
        }

        internal static string MergeLiveOperationWithActivityForTests(
            string liveReport,
            IReadOnlyList<PackageInstallerActivityEntry> activity)
        {
            return MergeLiveOperationWithActivity(liveReport, activity);
        }

        private static string MergeLiveOperationWithActivity(
            string liveReport,
            IReadOnlyList<PackageInstallerActivityEntry> activity)
        {
            string live = (liveReport ?? string.Empty).Trim();
            string history = activity != null && activity.Count > 0
                ? FormatActivityReport(activity).Trim()
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(live) && !string.IsNullOrWhiteSpace(history))
            {
                return "Current\n" + live + "\n\nHistory\n" + history;
            }

            if (!string.IsNullOrWhiteSpace(live))
            {
                return "Current\n" + live;
            }

            if (!string.IsNullOrWhiteSpace(history))
            {
                return "History\n" + history;
            }

            return "No detailed operation report is available.";
        }

        internal static string FormatActivityReportForTests(
            IReadOnlyList<PackageInstallerActivityEntry> entries)
        {
            return FormatActivityReport(entries);
        }

        private static string FormatActivityReport(
            IReadOnlyList<PackageInstallerActivityEntry> entries)
        {
            PackageInstallerActivityEntry[] visibleEntries = (entries ??
                    Array.Empty<PackageInstallerActivityEntry>())
                .Where(entry => entry != null)
                .Skip(Math.Max(0, (entries?.Count ?? 0) - 20))
                .ToArray();
            if (visibleEntries.Length == 0)
            {
                return "No activity has been recorded yet.";
            }

            List<string> lines = new List<string>();
            foreach (PackageInstallerActivityEntry entry in visibleEntries)
            {
                lines.Add(
                    entry.TimestampUtc.ToString("u") +
                    " | " + entry.Source +
                    " | " + entry.Severity +
                    " | " + entry.Summary);
                if (!string.IsNullOrWhiteSpace(entry.Details) &&
                    !string.Equals(entry.Details, entry.Summary, StringComparison.Ordinal))
                {
                    lines.Add(entry.Details.Trim());
                }
            }

            return string.Join("\n", lines.ToArray());
        }

        private static int CountOperationMessageLines(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return 0;
            }

            return Mathf.Max(
                1,
                message
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Length);
        }

        private OperationProgressView GetCurrentOperationProgress()
        {
            if (_packageInstallService.IsBusy)
            {
                return new OperationProgressView
                {
                    Title = "Package Operation",
                    OperationName = string.IsNullOrWhiteSpace(_packageInstallService.CurrentOperationName)
                        ? "Package Operation"
                        : _packageInstallService.CurrentOperationName,
                    CurrentItem = _packageInstallService.CurrentPackageName,
                    Message = _packageInstallService.LastStatusMessage,
                    ErrorMessage = _packageInstallService.LastErrorMessage,
                    CompletedSteps = _packageInstallService.CompletedSteps,
                    TotalSteps = _packageInstallService.TotalSteps,
                    FailedSteps = _packageInstallService.FailedSteps,
                    IsBusy = _packageInstallService.IsBusy,
                    ProgressItems = _packageInstallService.ProgressItems
                };
            }

            if (_packageSampleImportService.IsBusy)
            {
                return new OperationProgressView
                {
                    Title = "Sample Import",
                    OperationName = string.IsNullOrWhiteSpace(_packageSampleImportService.CurrentOperationName)
                        ? "Import Sample"
                        : _packageSampleImportService.CurrentOperationName,
                    CurrentItem = _packageSampleImportService.CurrentExtraName,
                    Message = _packageSampleImportService.LastStatusMessage,
                    ErrorMessage = _packageSampleImportService.LastErrorMessage,
                    CompletedSteps = 0,
                    TotalSteps = 1,
                    IsBusy = true
                };
            }

            if (_packageUpdateCheckService.IsChecking)
            {
                return new OperationProgressView
                {
                    Title = "Update Check",
                    OperationName = "Checking for package updates",
                    Message = "Resolving selected Git references...",
                    CompletedSteps = 0,
                    TotalSteps = 1,
                    IsBusy = true
                };
            }

            if (_packageDetectionService.IsRefreshing)
            {
                return new OperationProgressView
                {
                    Title = "Refresh",
                    OperationName = "Refreshing installed packages",
                    Message = "Reading Unity Package Manager state...",
                    CompletedSteps = 0,
                    TotalSteps = 1,
                    IsBusy = true
                };
            }

            return null;
        }

        private string GetGlobalOperationStateLabel(OperationProgressView operation)
        {
            if (operation != null)
            {
                if (operation.FailedSteps > 0 && !operation.IsBusy)
                {
                    return "Failed";
                }

                if (_packageUpdateCheckService.IsChecking)
                {
                    return "Checking for updates";
                }

                if (_packageDetectionService.IsRefreshing)
                {
                    return "Refreshing";
                }

                if (_packageSampleImportService.IsBusy)
                {
                    return "Installing";
                }

                if (_packageInstallService.State == PackageInstallRequestState.Removing)
                {
                    return "Removing";
                }

                return IsUpdateOperation(operation.OperationName) ? "Updating" : "Installing";
            }

            if (HasLastOperationFailure())
            {
                return "Failed";
            }

            if (HasLastOperationDetails())
            {
                return "Complete";
            }

            return "Idle";
        }

        private VisualStatusKind GetGlobalOperationStatusKind(OperationProgressView operation)
        {
            string stateLabel = GetGlobalOperationStateLabel(operation);

            if (string.Equals(stateLabel, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return VisualStatusKind.Failed;
            }

            if (string.Equals(stateLabel, "Idle", StringComparison.OrdinalIgnoreCase))
            {
                return VisualStatusKind.Info;
            }

            if (string.Equals(stateLabel, "Complete", StringComparison.OrdinalIgnoreCase))
            {
                return VisualStatusKind.Installed;
            }

            return VisualStatusKind.Busy;
        }

        private string GetOperationBarTitle(OperationProgressView operation)
        {
            if (operation != null)
            {
                return string.IsNullOrWhiteSpace(operation.OperationName)
                    ? GetGlobalOperationStateLabel(operation)
                    : operation.OperationName;
            }

            if (HasLastOperationFailure())
            {
                return "Last operation failed.";
            }

            if (HasLastOperationDetails())
            {
                return "Last operation complete.";
            }

            return "No operation running.";
        }

        private string GetOperationBarSubtitle(OperationProgressView operation)
        {
            if (operation != null)
            {
                if (!string.IsNullOrWhiteSpace(operation.ErrorMessage))
                {
                    return operation.ErrorMessage;
                }

                string progressStepText = GetProgressStepText(operation);

                if (!string.IsNullOrWhiteSpace(progressStepText))
                {
                    return progressStepText;
                }

                return operation.Message;
            }

            return GetLastOperationSummary();
        }

        private bool HasLastOperationDetails()
        {
            if (PackageInstallerActivityService.Recent.Count > 0)
            {
                return true;
            }

            IReadOnlyList<PackageInstallProgressItem> progressItems = GetLastProgressItems();
            IReadOnlyList<string> operationMessages = GetLastOperationMessages();

            return !string.IsNullOrWhiteSpace(GetLastOperationSummary()) ||
                   (operationMessages != null && operationMessages.Count > 0) ||
                   (progressItems != null && progressItems.Count > 0);
        }

        private bool HasLastOperationFailure()
        {
            PackageInstallerActivityEntry latestActivity = PackageInstallerActivityService.Latest;
            if (latestActivity != null)
            {
                return latestActivity.Severity == PackageInstallerActivitySeverity.Error;
            }

            IReadOnlyList<PackageInstallProgressItem> progressItems = GetLastProgressItems();

            return !string.IsNullOrWhiteSpace(_packageSampleImportService.LastErrorMessage) ||
                   !string.IsNullOrWhiteSpace(_packageInstallService.LastErrorMessage) ||
                   !string.IsNullOrWhiteSpace(_packageUpdateCheckService.LastFailureMessage) ||
                   (progressItems != null && progressItems.Any(item => item.State == PackageInstallProgressItemState.Failed));
        }

        private static bool IsUpdateOperation(string operationName)
        {
            return !string.IsNullOrWhiteSpace(operationName) &&
                   operationName.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetOperationDetailsExpanded(bool expanded)
        {
            _operationDetailsExpanded = expanded;
            EditorPrefs.SetBool(GetOperationDrawerPreferenceKey(), expanded);
            UpdateViewVisibility();
            _operationDrawerContainer?.MarkDirtyRepaint();
            RefreshOperationDrawerContent();
            UpdateOperationFooter();
            Repaint();
        }

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

        private static string GetStatusIconId(VisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case VisualStatusKind.Installed:
                    return DeucarianEditorIconIds.Success;
                case VisualStatusKind.NotInstalled:
                    return DeucarianEditorIconIds.Optional;
                case VisualStatusKind.UpdateAvailable:
                    return DeucarianEditorIconIds.Update;
                case VisualStatusKind.Failed:
                    return DeucarianEditorIconIds.Error;
                case VisualStatusKind.Busy:
                    return DeucarianEditorIconIds.Busy;
                case VisualStatusKind.Integration:
                    return DeucarianEditorIconIds.Integration;
                case VisualStatusKind.Info:
                default:
                    return DeucarianEditorIconIds.Info;
            }
        }

        private static DeucarianEditorStatus ToEditorStatus(VisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case VisualStatusKind.Installed:
                    return DeucarianEditorStatus.Success;
                case VisualStatusKind.UpdateAvailable:
                    return DeucarianEditorStatus.Warning;
                case VisualStatusKind.Failed:
                    return DeucarianEditorStatus.Error;
                case VisualStatusKind.NotInstalled:
                    return DeucarianEditorStatus.Disabled;
                case VisualStatusKind.Busy:
                case VisualStatusKind.Info:
                case VisualStatusKind.Integration:
                default:
                    return DeucarianEditorStatus.Info;
            }
        }

        private static MessageType ToMessageType(VisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case VisualStatusKind.Failed:
                    return MessageType.Error;
                case VisualStatusKind.UpdateAvailable:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }

        private static string GetDependencyDisplayNames(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null || integrationDefinition.Dependencies.Count == 0)
            {
                return "-";
            }

            return string.Join(
                ", ",
                integrationDefinition.Dependencies
                    .Select(GetDependencyDisplayName)
                    .ToArray());
        }

        private static string GetDependencyDisplayName(string packageId)
        {
            return PackageRegistryProvider.TryGetPackage(packageId, out PackageDefinition packageDefinition)
                ? packageDefinition.DisplayName
                : packageId;
        }

        private void DrawChannelPopup(PackageDefinition packageDefinition)
        {
            PackageChannel selectedChannel = GetSelectedChannel(packageDefinition);
            PackageChannel[] channelOptions = GetChannelOptions(packageDefinition, selectedChannel);
            string[] channelLabels = channelOptions.Select(GetChannelLabel).ToArray();
            int selectedIndex = Mathf.Max(0, Array.IndexOf(channelOptions, selectedChannel));

            using (new EditorGUI.DisabledScope(channelOptions.Length <= 1 || IsAnyOperationBusy()))
            {
                int nextIndex = EditorGUILayout.Popup(
                    selectedIndex,
                    channelLabels,
                    GUILayout.Width(118f));
                PackageChannel nextChannel = channelOptions[Mathf.Clamp(nextIndex, 0, channelOptions.Length - 1)];

                if (nextChannel != selectedChannel)
                {
                    SetSelectedChannel(packageDefinition, nextChannel);
                }
            }
        }

        private PackageChannel GetSelectedChannel(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return PackageChannel.Stable;
            }

            PackageChannelSelection projectSelection = _stateRepository != null
                ? _stateRepository.GetProjectChannelSelection()
                : PackageChannelSelection.None;
            PackageChannelSelection packageSelection = _stateRepository != null
                ? _stateRepository.GetPackageChannelSelection(packageDefinition.PackageId)
                : PackageChannelSelection.None;
            PackageChannel installedChannel = PackageChannel.Stable;
            bool hasInstalledChannel = _packageDetectionService != null &&
                _packageDetectionService.TryGetInstalledPackageChannel(
                    packageDefinition,
                    out installedChannel,
                    out _);

            return ResolveSelectedChannel(
                packageDefinition,
                projectSelection,
                packageSelection,
                hasInstalledChannel,
                installedChannel);
        }

        internal static PackageChannel ResolveSelectedChannelForTests(
            PackageDefinition packageDefinition,
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection,
            bool hasInstalledChannel,
            PackageChannel installedChannel)
        {
            return ResolveSelectedChannel(
                packageDefinition,
                projectSelection,
                packageSelection,
                hasInstalledChannel,
                installedChannel);
        }

        internal static PackageChannel ResolveSelectedChannel(
            PackageDefinition packageDefinition,
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection,
            bool hasInstalledChannel,
            PackageChannel installedChannel)
        {
            if (packageDefinition == null)
            {
                return PackageChannel.Stable;
            }

            PackageChannelSelection latestExplicitSelection = GetLatestExplicitChannelSelection(
                projectSelection,
                packageSelection);

            if (latestExplicitSelection.HasValue)
            {
                return ResolveConfiguredChannel(packageDefinition, latestExplicitSelection.Channel);
            }

            if (hasInstalledChannel && installedChannel == PackageChannel.Custom)
            {
                return PackageChannel.Custom;
            }

            return PackageChannel.Stable;
        }

        private static PackageChannelSelection GetLatestExplicitChannelSelection(
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection)
        {
            if (packageSelection.HasValue &&
                (!projectSelection.HasValue ||
                 packageSelection.ChangedAtUtcTicks > projectSelection.ChangedAtUtcTicks))
            {
                return packageSelection;
            }

            return projectSelection.HasValue
                ? projectSelection
                : PackageChannelSelection.None;
        }

        private static PackageChannel ResolveConfiguredChannel(
            PackageDefinition packageDefinition,
            PackageChannel channel)
        {
            if (channel == PackageChannel.Development &&
                packageDefinition != null &&
                packageDefinition.HasDevelopmentUrl)
            {
                return PackageChannel.Development;
            }

            return PackageChannel.Stable;
        }

        private void SetSelectedChannel(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                return;
            }

            if (channel == PackageChannel.Custom)
            {
                return;
            }

            _stateRepository?.SetPackageChannel(packageDefinition.PackageId, channel);
            _packageUpdateCheckService?.Invalidate(packageDefinition.PackageId);
            InvalidateGraphModelCache("package channel changed");

            if (_packageDetectionService != null &&
                _packageUpdateCheckService != null &&
                _packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                _packageUpdateCheckService.CheckForUpdate(packageDefinition, channel);
            }
            else
            {
                _packageUpdateCheckService?.Invalidate(packageDefinition.PackageId);
            }

            RefreshGraphView("package channel changed");
        }

        private bool IsCategoryExpanded(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return true;
            }

            if (_categoryFoldouts.TryGetValue(category, out bool expanded))
            {
                return expanded;
            }

            string key = GetCategoryFoldoutPreferenceKey(category);
            expanded = EditorPrefs.GetBool(key, true);
            _categoryFoldouts[category] = expanded;
            return expanded;
        }

        private void SetCategoryExpanded(string category, bool expanded)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return;
            }

            _categoryFoldouts[category] = expanded;
            EditorPrefs.SetBool(GetCategoryFoldoutPreferenceKey(category), expanded);
        }

        private string GetCategoryFoldoutPreferenceKey(string category)
        {
            return CategoryFoldoutPreferencePrefix +
                   Application.dataPath.Replace("\\", "/") +
                   "." +
                   category.Trim();
        }

        private static PackageChannel[] GetChannelOptions(
            PackageDefinition packageDefinition,
            PackageChannel selectedChannel)
        {
            List<PackageChannel> channels = new List<PackageChannel>
            {
                PackageChannel.Stable
            };

            if (packageDefinition != null && packageDefinition.HasDevelopmentUrl)
            {
                channels.Add(PackageChannel.Development);
            }

            if (selectedChannel == PackageChannel.Custom)
            {
                channels.Add(PackageChannel.Custom);
            }

            return channels.Distinct().ToArray();
        }

        private static string GetChannelLabel(PackageChannel channel)
        {
            switch (channel)
            {
                case PackageChannel.Development:
                    return "Development";
                case PackageChannel.Custom:
                    return "Custom";
                default:
                    return "Stable";
            }
        }

        private void EnsureValidSelection()
        {
            if (_reloadStatePendingValidation)
            {
                return;
            }

            if (GetSelectedDefinition() != null)
            {
                return;
            }

            if (_viewMode == InstallerViewMode.EcosystemGraph)
            {
                _selectedPackageId = string.Empty;
                return;
            }

            PackageDefinition defaultSelection = PackageRegistryProvider.All.FirstOrDefault(package => !package.IsIntegration);

            if (defaultSelection == null)
            {
                defaultSelection = PackageRegistryProvider.IntegrationPackages.FirstOrDefault();
                _selectionKind = SelectionKind.Integration;
            }
            else
            {
                _selectionKind = SelectionKind.Package;
            }

            _selectedPackageId = defaultSelection != null ? defaultSelection.PackageId : string.Empty;
        }

        private void HandleBeforeAssemblyReload()
        {
            PackageGraphCameraState camera = _graphView != null
                ? _graphView.GetCameraStateForReload()
                : new PackageGraphCameraState(Vector2.zero, 1f);
            PackageInstallerWindowReloadState.SaveForAssemblyReload(
                new PackageInstallerWindowReloadSnapshot
                {
                    searchText = _visibilityFilterState.SearchText,
                    showInstalled = _visibilityFilterState.ShowInstalled,
                    showNotInstalled = _visibilityFilterState.ShowNotInstalled,
                    selectedPackageId = _selectedPackageId,
                    navigationTargetKind = (int)_graphNavigationState.TargetKind,
                    focusedPackageId = _graphNavigationState.FocusedPackageId,
                    focusedGroupId = _graphNavigationState.FocusedGroupId,
                    viewMode = (int)_viewMode,
                    sidebarScrollX = _sidebarScrollPosition.x,
                    sidebarScrollY = _sidebarScrollPosition.y,
                    detailsScrollX = _detailsScrollPosition.x,
                    detailsScrollY = _detailsScrollPosition.y,
                    operationScrollX = _operationDetailsScrollPosition.x,
                    operationScrollY = _operationDetailsScrollPosition.y,
                    hasGraphCamera = _graphView != null,
                    graphPanX = camera.Pan.x,
                    graphPanY = camera.Pan.y,
                    graphZoom = camera.Zoom
                });
        }

        private void RestoreReloadSnapshot(PackageInstallerWindowReloadSnapshot snapshot)
        {
            _pendingReloadSnapshot = snapshot;
            _reloadStatePendingValidation = true;
            _visibilityFilterState.Set(
                snapshot.searchText,
                snapshot.showInstalled,
                snapshot.showNotInstalled);
            _selectedPackageId = snapshot.selectedPackageId;
            _graphNavigationState = RestoreNavigation(snapshot);
            _viewMode = Enum.IsDefined(typeof(InstallerViewMode), snapshot.viewMode)
                ? ResolveInstallerViewMode((InstallerViewMode)snapshot.viewMode)
                : DefaultInstallerViewMode;
            _sidebarScrollPosition = new Vector2(snapshot.sidebarScrollX, snapshot.sidebarScrollY);
            _detailsScrollPosition = new Vector2(snapshot.detailsScrollX, snapshot.detailsScrollY);
            _operationDetailsScrollPosition = new Vector2(
                snapshot.operationScrollX,
                snapshot.operationScrollY);
            _hasPendingReloadCamera = snapshot.hasGraphCamera;
            if (_hasPendingReloadCamera)
            {
                _pendingReloadCamera = new PackageGraphCameraState(
                    new Vector2(snapshot.graphPanX, snapshot.graphPanY),
                    snapshot.graphZoom);
            }
        }

        private void ValidatePendingReloadState(PackageGraphModel graph)
        {
            if (!_reloadStatePendingValidation)
            {
                return;
            }

            PackageInstallerWindowReloadResolution resolution =
                PackageInstallerWindowReloadState.Resolve(_pendingReloadSnapshot, graph);
            _selectedPackageId = resolution.SelectedPackageId;
            _selectionKind = resolution.SelectedPackageIsIntegration
                ? SelectionKind.Integration
                : SelectionKind.Package;
            _graphNavigationState = resolution.Navigation;
            _pendingReloadSnapshot = null;
            _reloadStatePendingValidation = false;
        }

        private static PackageGraphNavigationState RestoreNavigation(
            PackageInstallerWindowReloadSnapshot snapshot)
        {
            PackageGraphNavigationTargetKind targetKind =
                Enum.IsDefined(typeof(PackageGraphNavigationTargetKind), snapshot.navigationTargetKind)
                    ? (PackageGraphNavigationTargetKind)snapshot.navigationTargetKind
                    : PackageGraphNavigationTargetKind.Overview;
            switch (targetKind)
            {
                case PackageGraphNavigationTargetKind.Package:
                    return PackageGraphNavigationState.Package(
                        snapshot.focusedPackageId,
                        snapshot.focusedGroupId);
                case PackageGraphNavigationTargetKind.Group:
                    return PackageGraphNavigationState.Group(snapshot.focusedGroupId);
                default:
                    return PackageGraphNavigationState.Overview();
            }
        }

        private bool IsSelected(PackageDefinition packageDefinition, SelectionKind selectionKind)
        {
            return packageDefinition != null &&
                   _selectionKind == selectionKind &&
                   string.Equals(_selectedPackageId, packageDefinition.PackageId, StringComparison.OrdinalIgnoreCase);
        }

        private void SelectDefinition(
            PackageDefinition packageDefinition,
            SelectionKind selectionKind,
            bool refreshGraph = true)
        {
            if (packageDefinition == null || IsSelected(packageDefinition, selectionKind))
            {
                return;
            }

            _selectionKind = selectionKind;
            _selectedPackageId = packageDefinition.PackageId;
            _detailsScrollPosition = Vector2.zero;

            if (refreshGraph)
            {
                RefreshGraphView("selection");
            }

            Repaint();
        }

        private PackageDefinition GetSelectedDefinition()
        {
            if (string.IsNullOrWhiteSpace(_selectedPackageId))
            {
                return null;
            }

            return PackageRegistryProvider.All.FirstOrDefault(packageDefinition =>
                string.Equals(packageDefinition.PackageId, _selectedPackageId, StringComparison.OrdinalIgnoreCase));
        }

        private PackageGraphGroup GetFocusedGraphGroup()
        {
            return _lastPackageGraph != null &&
                   !string.IsNullOrWhiteSpace(_graphNavigationState.FocusedGroupId) &&
                   _lastPackageGraph.TryGetGroup(_graphNavigationState.FocusedGroupId, out PackageGraphGroup group)
                ? group
                : null;
        }

        private string GetGraphPackageGroupId(string packageId)
        {
            return _lastPackageGraph != null &&
                   !string.IsNullOrWhiteSpace(packageId) &&
                   _lastPackageGraph.TryGetNode(packageId, out PackageGraphNode node)
                ? node.GroupId
                : string.Empty;
        }

        private string GetPackageHierarchyPath(PackageDefinition packageDefinition)
        {
            return PackageGraphHierarchyDisplay.GetPackageHierarchyPath(_lastPackageGraph, packageDefinition);
        }

        private static string GetPackageKindDisplayName(PackageDefinition packageDefinition)
        {
            return PackageGraphHierarchyDisplay.GetPackageKind(packageDefinition);
        }

        private string GetGraphParentGroupId(string groupId)
        {
            return _lastPackageGraph != null &&
                   !string.IsNullOrWhiteSpace(groupId) &&
                   _lastPackageGraph.TryGetGroup(groupId, out PackageGraphGroup group)
                ? group.ParentGroupId
                : string.Empty;
        }

        private bool IsGraphGroupHoverContext(string groupId)
        {
            if (_graphView == null || string.IsNullOrWhiteSpace(groupId))
            {
                return false;
            }

            string activeHoverGroupId = _graphView.ActiveHoverGroupId;

            if (string.IsNullOrWhiteSpace(activeHoverGroupId))
            {
                return false;
            }

            string hoverTopLevelGroupId = ResolveTopLevelGroupId(_lastPackageGraph, activeHoverGroupId);
            string rowTopLevelGroupId = ResolveTopLevelGroupId(_lastPackageGraph, groupId);

            return !string.IsNullOrWhiteSpace(hoverTopLevelGroupId) &&
                   string.Equals(
                       hoverTopLevelGroupId,
                       rowTopLevelGroupId,
                       StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<PackageGraphGroup> GetGraphChildGroups(string groupId)
        {
            return _lastPackageGraph == null
                ? Enumerable.Empty<PackageGraphGroup>()
                : _lastPackageGraph.GetChildGroups(groupId);
        }

        private IEnumerable<PackageGraphNode> GetGraphGroupDescendantPackages(string groupId)
        {
            if (_lastPackageGraph == null || string.IsNullOrWhiteSpace(groupId))
            {
                return Enumerable.Empty<PackageGraphNode>();
            }

            return _lastPackageGraph.GetDescendantPackages(groupId)
                .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        private PackageDefinition[] GetPackagesWithUpdates()
        {
            return _packageUpdateCheckService
                .GetPackagesWithUpdates(PackageRegistryProvider.All, GetSelectedChannel)
                .ToArray();
        }

        private string GetLastOperationSummary()
        {
            if (_packageSampleImportService.IsBusy &&
                !string.IsNullOrWhiteSpace(_packageSampleImportService.LastStatusMessage))
            {
                return _packageSampleImportService.LastStatusMessage;
            }

            if (_packageInstallService.IsBusy &&
                !string.IsNullOrWhiteSpace(_packageInstallService.LastStatusMessage))
            {
                return _packageInstallService.LastStatusMessage;
            }

            if (_packageUpdateCheckService.IsChecking)
            {
                return "Checking installed packages for updates...";
            }

            if (_packageDetectionService.IsRefreshing)
            {
                return "Refreshing installed packages...";
            }

            PackageInstallerActivityEntry latestActivity = PackageInstallerActivityService.Latest;
            if (latestActivity != null && !string.IsNullOrWhiteSpace(latestActivity.Summary))
            {
                return latestActivity.Summary;
            }

            if (_packageInstallService.HasProgress &&
                !string.IsNullOrWhiteSpace(_packageInstallService.LastStatusMessage))
            {
                return _packageInstallService.LastStatusMessage;
            }

            if (!string.IsNullOrWhiteSpace(_packageSampleImportService.LastErrorMessage))
            {
                return _packageSampleImportService.LastErrorMessage;
            }

            if (!string.IsNullOrWhiteSpace(_packageSampleImportService.LastStatusMessage))
            {
                return _packageSampleImportService.LastStatusMessage;
            }

            if (!string.IsNullOrWhiteSpace(_packageUpdateCheckService.LastFailureMessage))
            {
                return _packageUpdateCheckService.LastFailureMessage;
            }

            if (!string.IsNullOrWhiteSpace(_packageUpdateCheckService.LastStatusMessage))
            {
                return _packageUpdateCheckService.LastStatusMessage;
            }

            return string.Empty;
        }

        private bool IsAnyOperationBusy()
        {
            return (_packageInstallService != null && _packageInstallService.IsBusy) ||
                   (_confirmationState != null && _confirmationState.IsPending) ||
                   (_packageDependencyInstaller != null &&
                    _packageDependencyInstaller.IsAwaitingPreflight) ||
                   (_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                   (_packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking) ||
                   (_packageSampleImportService != null && _packageSampleImportService.IsBusy) ||
                   _plannerFailureRetryAfterRefresh ||
                   IsActiveActionStillBusy();
        }

        private static string GetUpdateStatusText(PackageUpdateStatus status)
        {
            if (status == null)
            {
                return "Unknown";
            }

            if (status.Kind == PackageUpdateStatusKind.UpToDate && !string.IsNullOrWhiteSpace(status.ShortLatestRevision))
            {
                return status.Label + " (" + status.ShortLatestRevision + ")";
            }

            if (status.Kind == PackageUpdateStatusKind.UpdateAvailable ||
                status.Kind == PackageUpdateStatusKind.SwitchAvailable)
            {
                return status.Label + " (" + status.ShortInstalledRevision + " -> " + status.ShortLatestRevision + ")";
            }

            if (status.IsSourceMigrationAvailable && !string.IsNullOrWhiteSpace(status.ShortLatestRevision))
            {
                return status.Label + " (Git " + status.ShortLatestRevision + ")";
            }

            if (status.IsReloadPending && !string.IsNullOrWhiteSpace(status.Message))
            {
                return status.Label + ": " + status.Message;
            }

            if (status.Kind == PackageUpdateStatusKind.Failed && !string.IsNullOrWhiteSpace(status.Message))
            {
                return status.Label + ": " + status.Message;
            }

            if (status.Kind == PackageUpdateStatusKind.CannotDetermine && !string.IsNullOrWhiteSpace(status.Message))
            {
                return status.Label + ": " + status.Message;
            }

            return status.Label;
        }

        private static string GetPackageVersionText(string installedVersion, PackageUpdateStatus status)
        {
            string resolvedInstalledVersion = status != null && !string.IsNullOrWhiteSpace(status.InstalledVersion)
                ? status.InstalledVersion
                : installedVersion;
            string currentVersion = string.IsNullOrWhiteSpace(resolvedInstalledVersion)
                ? "-"
                : resolvedInstalledVersion.Trim();

            if (status != null &&
                status.IsReloadPending &&
                !string.IsNullOrWhiteSpace(status.RunningVersion))
            {
                return status.RunningVersion + " running; " + currentVersion + " resolved";
            }

            if (status != null && status.HasPackageVersionTransition)
            {
                return currentVersion + " -> " + status.LatestVersion;
            }

            return currentVersion;
        }

        private static string GetUpdateActionLabel(PackageUpdateStatus status, PackageChannel channel)
        {
            if (status != null && status.IsReloadPending)
            {
                return "Retry Script Reload";
            }

            if (status != null && status.IsSourceMigrationAvailable)
            {
                return PackageInstallerRuntimeIdentity.IsSelf(status.PackageId)
                    ? "Open Bootstrap"
                    : "Migrate to Git";
            }

            if (status != null && status.Kind == PackageUpdateStatusKind.SwitchAvailable)
            {
                return "Switch to " + GetChannelLabel(channel);
            }

            return "Update to " + GetChannelLabel(channel);
        }

        private static string GetSampleImportStatusText(PackageSampleImportStatus status)
        {
            if (status == null)
            {
                return string.Empty;
            }

            switch (status.State)
            {
                case PackageSampleImportState.Importing:
                    return "Importing sample...";
                case PackageSampleImportState.Imported:
                    return string.IsNullOrWhiteSpace(status.Message) ? "Imported sample." : status.Message;
                case PackageSampleImportState.AlreadyImported:
                    return "Sample already imported.";
                case PackageSampleImportState.Canceled:
                    return string.IsNullOrWhiteSpace(status.Message) ? "Sample import canceled." : status.Message;
                case PackageSampleImportState.Failed:
                    return string.IsNullOrWhiteSpace(status.Message) ? "Import failed." : status.Message;
                default:
                    return "Not imported.";
            }
        }

        private static VisualStatusKind GetSampleImportStatusKind(PackageSampleImportStatus status)
        {
            if (status == null)
            {
                return VisualStatusKind.NotInstalled;
            }

            switch (status.State)
            {
                case PackageSampleImportState.Importing:
                    return VisualStatusKind.Busy;
                case PackageSampleImportState.Imported:
                case PackageSampleImportState.AlreadyImported:
                    return VisualStatusKind.Installed;
                case PackageSampleImportState.Canceled:
                    return VisualStatusKind.NotInstalled;
                case PackageSampleImportState.Failed:
                    return VisualStatusKind.Failed;
                default:
                    return VisualStatusKind.NotInstalled;
            }
        }

        private static bool IsImportedSampleStatus(PackageSampleImportStatus status)
        {
            return status != null &&
                   (status.State == PackageSampleImportState.Imported ||
                    status.State == PackageSampleImportState.AlreadyImported);
        }

        private void HandleRegistryChanged()
        {
            InvalidateGraphModelCache("registry changed");
            if (_packageDetectionService != null &&
                _packageDetectionService.HasSuccessfulRefresh &&
                !PackageRegistryProvider.IsRemoteRefreshing)
            {
                _packageUpdateCheckService?.ReconcileCachedStatuses(
                    PackageRegistryProvider.All,
                    GetSelectedChannel);
            }

            EnsureValidSelection();
            RefreshGraphView("registry changed");

            TryPromptForSavedOperationRecovery();
            TryRestartTerminalOperationAfterRefresh();
            TryRetryPlannerFailureAfterRefresh();

            TryRunDeferredUpdateCheck();
            ClearActiveActionIfIdle();
            Repaint();
        }

        private void HandlePackageUpdateGraphStateChanged()
        {
            InvalidateGraphModelCache("update status changed");
            RefreshGraphView("update status changed");
            ClearActiveActionIfIdle();
        }

        private void HandlePackageInstallCompleted(PackageDefinition packageDefinition, bool success, string message)
        {
            if (!TryConsumePendingUpdateStatusInvalidation(
                    _pendingUpdateStatusInvalidationPackageIds,
                    packageDefinition,
                    success))
            {
                return;
            }

            _packageUpdateCheckService?.Invalidate(packageDefinition.PackageId);
            InvalidateGraphModelCache("package update completed");
            RefreshGraphView("package update completed");
            UpdateViewVisibility();
            Repaint();
        }

        private void HandlePackageDetectionGraphStateChanged()
        {
            if (_packageDetectionService != null && _packageDetectionService.IsRefreshing)
            {
                RefreshGraphView("installed package refresh started");
            }
        }

        private void HandlePackageOperationCompleted()
        {
            PackageInstallerActionKind completedActionKind = _activeActionKind;
            bool shouldCheckUpdates =
                completedActionKind == PackageInstallerActionKind.UpdateAll ||
                _checkUpdatesAfterDetectionRefresh ||
                (_packageUpdateCheckService != null && _packageUpdateCheckService.HasStatuses);

            if (completedActionKind == PackageInstallerActionKind.UpdateAll ||
                completedActionKind == PackageInstallerActionKind.InstallAll)
            {
                _activeActionKind = PackageInstallerActionKind.None;
                _cancelingActionKind = PackageInstallerActionKind.None;
            }

            _pendingUpdateStatusInvalidationPackageIds.Clear();

            if (shouldCheckUpdates)
            {
                QueueDeferredUpdateCheck(PackageInstallerActionKind.CheckUpdates);
                PackageRegistryProvider.RefreshRemote();
            }

            _packageDetectionService.Refresh();
            UpdateViewVisibility();
        }

        private void HandlePackageDetectionRefreshCompleted()
        {
            InvalidateGraphModelCache("installed package manifest changed");
            _packageSampleDiscoveryService?.ClearCache();
            bool hadUpdateStatuses =
                _packageUpdateCheckService != null && _packageUpdateCheckService.HasStatuses;
            bool manifestChanged =
                _packageUpdateCheckService != null &&
                _packageUpdateCheckService.InvalidateIfManifestStateChanged();

            if (manifestChanged && hadUpdateStatuses)
            {
                QueueDeferredUpdateCheck(PackageInstallerActionKind.CheckUpdates);
            }

            if (_packageDetectionService != null &&
                _packageDetectionService.HasSuccessfulRefresh &&
                !PackageRegistryProvider.IsRemoteRefreshing)
            {
                _packageUpdateCheckService?.ReconcileCachedStatuses(
                    PackageRegistryProvider.All,
                    GetSelectedChannel);
            }

            RefreshGraphView("installed package refresh completed");

            TryPromptForSavedOperationRecovery();
            TryRestartTerminalOperationAfterRefresh();
            TryRetryPlannerFailureAfterRefresh();

            TryRunDeferredUpdateCheck();
            ClearActiveActionIfIdle();
        }

        private void TryRestartTerminalOperationAfterRefresh()
        {
            PackageOperationTerminalSnapshot snapshot = _terminalOperationRetryAfterRefresh;
            if (snapshot == null ||
                (_confirmationState != null && _confirmationState.IsPending) ||
                PackageRegistryProvider.IsRemoteRefreshing ||
                (_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                (_packageDetectionService != null && !_packageDetectionService.HasSuccessfulRefresh))
            {
                return;
            }

            _terminalOperationRetryAfterRefresh = null;
            PackageOperationTerminalSnapshot currentSnapshot =
                _packageInstallService?.TerminalOperationSnapshot;
            if (_packageInstallService == null ||
                _packageInstallService.IsBusy ||
                currentSnapshot == null ||
                !currentSnapshot.CanRestart ||
                !string.Equals(
                    currentSnapshot.OperationId,
                    snapshot.OperationId,
                    StringComparison.Ordinal))
            {
                return;
            }

            PackageDependencyInstallPlan freshPlan = CreateFreshTerminalRetryPlan(
                snapshot,
                _packageDependencyInstaller,
                packageId => PackageRegistryProvider.TryGetPackage(
                    packageId,
                    out PackageDefinition definition)
                    ? definition
                    : null);
            if (freshPlan == null || !freshPlan.IsValid || freshPlan.Steps.Count == 0)
            {
                ShowInformationDialog(
                    "Package operation cannot be restarted",
                    freshPlan != null && !string.IsNullOrWhiteSpace(freshPlan.ErrorMessage)
                        ? freshPlan.ErrorMessage
                        : "The affected root packages are no longer available in the current registry.",
                    DeucarianEditorIconIds.Error);
                return;
            }

            string delta = FormatTerminalRetryPlanDelta(snapshot, freshPlan);
            if (!freshPlan.RequiresPreflight && string.IsNullOrWhiteSpace(delta))
            {
                StartTerminalRetryPlan(snapshot, freshPlan);
                return;
            }

            var restartAction = new DeucarianEditorDialogAction(
                "restart",
                "Restart",
                DeucarianEditorIconIds.Refresh,
                DeucarianEditorDialogActionStyle.Primary);
            var cancelAction = new DeucarianEditorDialogAction(
                "cancel",
                "Cancel",
                DeucarianEditorIconIds.Stop);
            var options = new DeucarianEditorDialogOptions(
                "Restart package operation",
                "Installed and registry state were refreshed. Review the fresh plan before restarting.",
                DeucarianEditorIconIds.Refresh,
                new[] { restartAction, cancelAction })
            {
                Details = BuildTerminalRetryReview(snapshot, freshPlan, delta),
                DefaultActionId = restartAction.Id,
                CancelActionId = cancelAction.Id
            };
            TryShowManagedDialog(options, result =>
            {
                if (!result.WasCanceled &&
                    string.Equals(result.ActionId, restartAction.Id, StringComparison.Ordinal) &&
                    this != null)
                {
                    StartTerminalRetryPlan(snapshot, freshPlan);
                }
            });
        }

        private void StartTerminalRetryPlan(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstallPlan freshPlan)
        {
            if (!CanStartTerminalRetryPlan(snapshot, freshPlan))
            {
                RecordStaleConfirmation(
                    "Retry package operation",
                    "Package or registry state changed before the retry could start.");
                return;
            }

            TrackPendingUpdateStatusInvalidations(freshPlan.Packages);
            _packageInstallService.InstallPlan(
                freshPlan,
                "Retry " + (string.IsNullOrWhiteSpace(snapshot.OperationName)
                    ? "Package Operation"
                    : snapshot.OperationName));
            UpdateViewVisibility();
        }

        private bool CanStartTerminalRetryPlan(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstallPlan freshPlan)
        {
            PackageOperationTerminalSnapshot currentSnapshot =
                _packageInstallService?.TerminalOperationSnapshot;
            return snapshot != null &&
                   freshPlan != null &&
                   _packageInstallService != null &&
                   !_packageInstallService.IsBusy &&
                   currentSnapshot != null &&
                   currentSnapshot.CanRestart &&
                   string.Equals(
                       currentSnapshot.OperationId,
                       snapshot.OperationId,
                       StringComparison.Ordinal) &&
                   _packageDependencyInstaller != null &&
                   _packageDependencyInstaller.IsPlanStillCurrent(freshPlan);
        }

        private void TryRetryPlannerFailureAfterRefresh()
        {
            if (!IsPlannerRetryRefreshReadyForTests(
                    _plannerFailureRetryAfterRefresh,
                    PackageRegistryProvider.IsRemoteRefreshing,
                    _packageDetectionService != null && _packageDetectionService.IsRefreshing))
            {
                return;
            }

            _plannerFailureRetryAfterRefresh = false;
            if (_packageDetectionService != null &&
                !_packageDetectionService.HasSuccessfulRefresh)
            {
                const string message =
                    "The package plan was not retried because installed-package refresh failed. Retry the plan to refresh again.";
                PackageInstallerLog.Install.Warning(message);
                PackageInstallerActivityService.Record(
                    "Planner",
                    PackageInstallerActivitySeverity.Warning,
                    message,
                    retryKind: PackageInstallerRetryKind.ReplanOperation);
                return;
            }

            _packageDependencyInstaller?.RetryLastPlannerFailure();
        }

        internal static bool IsPlannerRetryRefreshReadyForTests(
            bool retryPending,
            bool registryRefreshing,
            bool detectionRefreshing)
        {
            return retryPending && !registryRefreshing && !detectionRefreshing;
        }

        internal static PackageDependencyInstallPlan CreateFreshTerminalRetryPlanForTests(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstaller installer,
            IEnumerable<PackageDefinition> registeredPackages)
        {
            Dictionary<string, PackageDefinition> definitions =
                (registeredPackages ?? Array.Empty<PackageDefinition>())
                .Where(definition => definition != null &&
                                     !string.IsNullOrWhiteSpace(definition.PackageId))
                .GroupBy(definition => definition.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            return CreateFreshTerminalRetryPlan(
                snapshot,
                installer,
                packageId => definitions.TryGetValue(packageId, out PackageDefinition definition)
                    ? definition
                    : null);
        }

        private static PackageDependencyInstallPlan CreateFreshTerminalRetryPlan(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstaller installer,
            Func<string, PackageDefinition> packageResolver)
        {
            if (snapshot == null || !snapshot.CanRestart || installer == null || packageResolver == null)
            {
                return null;
            }

            PackageOperationRootRequest[] rootRequests = snapshot.RestartRoots
                .Where(root => root != null && !string.IsNullOrWhiteSpace(root.PackageId))
                .GroupBy(root => root.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            PackageDefinition[] roots = rootRequests
                .Select(root => packageResolver(root.PackageId))
                .Where(definition => definition != null)
                .ToArray();
            if (roots.Length == 0 || roots.Length != rootRequests.Length)
            {
                return null;
            }

            Dictionary<string, PackageChannel> channels = rootRequests.ToDictionary(
                root => root.PackageId,
                root => root.Channel,
                StringComparer.OrdinalIgnoreCase);
            return installer.CreateInstallPlan(
                roots,
                package => channels.TryGetValue(package.PackageId, out PackageChannel channel)
                    ? channel
                    : PackageChannel.Stable,
                includeInstalledRequestedPackages: true);
        }

        internal static string FormatTerminalRetryPlanDeltaForTests(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstallPlan freshPlan)
        {
            return FormatTerminalRetryPlanDelta(snapshot, freshPlan);
        }

        private static string FormatTerminalRetryPlanDelta(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstallPlan freshPlan)
        {
            if (snapshot == null || freshPlan == null || !freshPlan.IsValid)
            {
                return string.Empty;
            }

            HashSet<string> retryRootIds = new HashSet<string>(
                snapshot.RestartRoots.Select(root => root.PackageId),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageOperationStepSnapshot> previous = snapshot.Steps
                .Where(step => step != null &&
                               ((!step.IsDependency && retryRootIds.Contains(step.PackageId)) ||
                                 step.RootPackageIds.Any(retryRootIds.Contains)))
                .GroupBy(step => step.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageDependencyInstallStep> current = freshPlan.Steps
                .Where(step => step != null && step.PackageDefinition != null)
                .GroupBy(step => step.PackageDefinition.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            List<string> lines = new List<string>();

            foreach (string packageId in previous.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                bool hadPrevious = previous.TryGetValue(packageId, out PackageOperationStepSnapshot oldStep);
                bool hasCurrent = current.TryGetValue(packageId, out PackageDependencyInstallStep newStep);
                if (!hadPrevious)
                {
                    lines.Add("Added: " + newStep.PackageDefinition.DisplayName + " -> " + newStep.TargetUrl);
                }
                else if (!hasCurrent)
                {
                    lines.Add("Now skipped: " + oldStep.DisplayName + " is already correct or no longer required.");
                }
                else if (oldStep.Channel != newStep.Channel ||
                         !string.Equals(oldStep.TargetUrl, newStep.TargetUrl, StringComparison.Ordinal))
                {
                    lines.Add(
                        "Changed: " + newStep.PackageDefinition.DisplayName +
                        "\n  was [" + GetChannelLabel(oldStep.Channel) + "] " + oldStep.TargetUrl +
                        "\n  now [" + GetChannelLabel(newStep.Channel) + "] " + newStep.TargetUrl);
                }
            }

            return string.Join("\n", lines.ToArray());
        }

        private static List<string> DescribeRecoveryStepChanges(
            PackageOperationRecoveryStep oldStep,
            PackageDependencyInstallStep newStep)
        {
            List<string> changes = new List<string>();
            if (oldStep.Channel != newStep.Channel ||
                !string.Equals(oldStep.TargetUrl, newStep.TargetUrl, StringComparison.Ordinal))
            {
                changes.Add(
                    "target:\n    was [" + GetChannelLabel(oldStep.Channel) + "] " + oldStep.TargetUrl +
                    "\n    now [" + GetChannelLabel(newStep.Channel) + "] " + newStep.TargetUrl);
            }

            if (oldStep.RequestedChannel != newStep.RequestedChannel)
            {
                changes.Add(
                    "requested channel: " + GetChannelLabel(oldStep.RequestedChannel) +
                    " -> " + GetChannelLabel(newStep.RequestedChannel));
            }

            if (oldStep.IsDependency != newStep.IsDependency)
            {
                changes.Add(
                    "role: " + FormatOperationStepRole(oldStep.IsDependency) +
                    " -> " + FormatOperationStepRole(newStep.IsDependency));
            }

            AddStringSetChange(
                changes,
                "prerequisites",
                oldStep.PrerequisitePackageIds,
                newStep.PrerequisitePackageIds);
            AddStringSetChange(
                changes,
                "root packages",
                oldStep.RootPackageIds,
                newStep.RootPackageIds);
            AddStringSetChange(
                changes,
                "root paths",
                oldStep.RootPaths,
                newStep.RootPaths);

            string oldReason = (oldStep.DependencyReason ?? string.Empty).Trim();
            string newReason = (newStep.DependencyReason ?? string.Empty).Trim();
            if (!string.Equals(oldReason, newReason, StringComparison.Ordinal))
            {
                changes.Add(
                    "dependency reason: " + FormatOptionalPlanDetail(oldReason) +
                    " -> " + FormatOptionalPlanDetail(newReason));
            }

            AddStringValueChange(
                changes,
                "detected source",
                oldStep.DetectedCurrentSource,
                newStep.DetectedCurrentSource);
            AddStringValueChange(
                changes,
                "detected version",
                oldStep.DetectedCurrentVersion,
                newStep.DetectedCurrentVersion);
            AddStringValueChange(
                changes,
                "detected identity",
                oldStep.DetectedCurrentIdentity,
                newStep.DetectedCurrentIdentity);

            return changes;
        }

        private static void AddStringValueChange(
            ICollection<string> changes,
            string label,
            string previousValue,
            string currentValue)
        {
            string previous = (previousValue ?? string.Empty).Trim();
            string current = (currentValue ?? string.Empty).Trim();
            if (string.Equals(previous, current, StringComparison.Ordinal))
            {
                return;
            }

            changes.Add(
                label + ": " + FormatOptionalPlanDetail(previous) +
                " -> " + FormatOptionalPlanDetail(current));
        }

        private static void AddStringSetChange(
            ICollection<string> changes,
            string label,
            IEnumerable<string> previousValues,
            IEnumerable<string> currentValues)
        {
            string[] previous = NormalizePlanDetailSet(previousValues);
            string[] current = NormalizePlanDetailSet(currentValues);
            if (new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase).SetEquals(current))
            {
                return;
            }

            changes.Add(
                label + ": " + FormatPlanDetailSet(previous) +
                " -> " + FormatPlanDetailSet(current));
        }

        private static string[] NormalizePlanDetailSet(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string FormatPlanDetailSet(IEnumerable<string> values)
        {
            string[] normalized = NormalizePlanDetailSet(values);
            return normalized.Length > 0
                ? string.Join(", ", normalized)
                : "(none)";
        }

        private static string FormatOptionalPlanDetail(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
        }

        private static string FormatOperationStepRole(bool isDependency)
        {
            return isDependency ? "dependency" : "requested root";
        }

        private static string BuildTerminalRetryReview(
            PackageOperationTerminalSnapshot snapshot,
            PackageDependencyInstallPlan freshPlan,
            string delta)
        {
            List<string> lines = new List<string>
            {
                "Installed and registry state were refreshed. " +
                (string.IsNullOrWhiteSpace(snapshot.OperationName)
                    ? "The affected roots"
                    : snapshot.OperationName + "'s affected roots") +
                " were planned again from the current registry.",
                string.Empty
            };
            if (!string.IsNullOrWhiteSpace(delta))
            {
                lines.Add("Plan changes:");
                lines.Add(delta);
                lines.Add(string.Empty);
            }

            lines.Add("Fresh plan:");
            lines.AddRange(freshPlan.Steps.Select(step =>
                "- " + step.PackageDefinition.DisplayName +
                " [" + GetChannelLabel(step.Channel) + "]\n  " + step.TargetUrl));
            if (freshPlan.RequiresPreflight)
            {
                lines.Add(string.Empty);
                lines.Add("This plan requires review because it is bulk, multi-step, or carries migration, fallback, downgrade, conflict, or destructive risk.");
            }

            return string.Join("\n", lines.Where(line => line != null).ToArray()).Trim();
        }

        private void TryPromptForSavedOperationRecovery()
        {
            if (!_promptSavedOperationAfterDetectionRefresh ||
                (_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                (_packageDetectionService != null && !_packageDetectionService.HasSuccessfulRefresh) ||
                PackageRegistryProvider.IsRemoteRefreshing)
            {
                return;
            }

            _promptSavedOperationAfterDetectionRefresh = false;
            PromptForSavedOperationRecovery();
        }

        private void PromptForSavedOperationRecovery()
        {
            if (_packageInstallService == null || _packageInstallService.IsBusy)
            {
                return;
            }

            if (_confirmationState != null && _confirmationState.IsPending)
            {
                _promptSavedOperationAfterDetectionRefresh = true;
                return;
            }

            if (!_packageInstallService.TryGetSavedOperation(
                    out PackageOperationRecoveryRecord recovery,
                    out string recoveryError) ||
                recovery == null)
            {
                PackageOperationAutoResumeState.ClearReloadMarker();
                if (!string.IsNullOrWhiteSpace(recoveryError) &&
                    recoveryError.IndexOf("No saved", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    ShowInformationDialog(
                        "Package operation recovery unavailable",
                        recoveryError,
                        DeucarianEditorIconIds.Error);
                }
                return;
            }

            PackageDependencyInstallPlan freshPlan = CreateFreshRecoveryPlan(recovery);
            bool canReuseExactTargets = CanReuseSavedExactTargets(recovery, freshPlan);
            bool hasMatchingReloadMarker =
                PackageOperationAutoResumeState.HasMatchingReloadMarker(
                    recovery.OperationId,
                    recovery.RegistryFingerprint);

            if (GetRecoveryDisposition(
                    recovery,
                    freshPlan,
                    hasMatchingReloadMarker) ==
                PackageOperationRecoveryDisposition.AutoResume)
            {
                bool resumed = _packageInstallService.ResumeSavedOperation(
                    freshPlan.RegistryFingerprint);
                bool reconciledWithoutRemainingWork =
                    !resumed && !_packageInstallService.HasSavedOperation;

                if (resumed || reconciledWithoutRemainingWork)
                {
                    PackageOperationAutoResumeState.AcknowledgeReloadMarker(
                        recovery.OperationId);
                    string operationName = string.IsNullOrWhiteSpace(recovery.OperationName)
                        ? "Bulk package operation"
                        : recovery.OperationName;
                    string message = resumed
                        ? "Resuming " + operationName + " after Unity script reload."
                        : operationName +
                          " completed after Unity script reload; all targets are already correct.";
                    PackageInstallerLog.Install.Info(message);
                    PackageInstallerActivityService.Record(
                        "Packages",
                        PackageInstallerActivitySeverity.Info,
                        message);
                    UpdateOperationFooter();
                    return;
                }
            }

            PackageOperationAutoResumeState.ClearReloadMarker();
            int remainingSteps = recovery.Steps.Count(step =>
                step.State == PackageInstallProgressItemState.Pending ||
                step.State == PackageInstallProgressItemState.Active ||
                step.State == PackageInstallProgressItemState.Failed ||
                step.State == PackageInstallProgressItemState.Blocked ||
                step.State == PackageInstallProgressItemState.Canceled);
            string interruptedSummary =
                (string.IsNullOrWhiteSpace(recovery.OperationName)
                    ? "A package operation"
                    : recovery.OperationName) +
                " was interrupted with " + remainingSteps + " step(s) remaining.";
            string summary = interruptedSummary + "\n\n" +
                "Resume keeps its exact saved URLs and skips completed steps. " +
                "Restart repeats the saved plan. Discard removes only the recovery record.";

            if (!canReuseExactTargets && freshPlan != null && freshPlan.IsValid)
            {
                string planDelta = FormatRecoveryPlanDelta(recovery, freshPlan);
                var restartFreshAction = new DeucarianEditorDialogAction(
                    "restart-fresh",
                    "Restart Fresh Plan",
                    DeucarianEditorIconIds.Refresh,
                    DeucarianEditorDialogActionStyle.Primary);
                var keepAction = new DeucarianEditorDialogAction(
                    "keep",
                    "Keep for Later",
                    DeucarianEditorIconIds.History);
                var discardAction = new DeucarianEditorDialogAction(
                    "discard",
                    "Discard",
                    DeucarianEditorIconIds.Remove,
                    DeucarianEditorDialogActionStyle.Destructive);
                var options = new DeucarianEditorDialogOptions(
                    "Package registry changed",
                    interruptedSummary,
                    DeucarianEditorIconIds.Warning,
                    new[] { restartFreshAction, keepAction, discardAction })
                {
                    Details = BuildRecoveryRegistryDriftReview(
                        interruptedSummary,
                        freshPlan,
                        planDelta),
                    DefaultActionId = restartFreshAction.Id,
                    CancelActionId = keepAction.Id
                };
                TryShowManagedDialog(options, result =>
                {
                    if (result.WasCanceled || this == null)
                    {
                        return;
                    }

                    if (!IsRecoveryStillCurrent(recovery) ||
                        !_packageDependencyInstaller.IsPlanStillCurrent(freshPlan))
                    {
                        RejectStaleRecoveryConfirmation(recovery);
                        return;
                    }

                    if (string.Equals(result.ActionId, restartFreshAction.Id, StringComparison.Ordinal))
                    {
                        _packageInstallService.DiscardSavedOperation();
                        _packageInstallService.InstallPlan(
                            freshPlan,
                            string.IsNullOrWhiteSpace(recovery.OperationName)
                                ? "Restart Package Operation"
                                : recovery.OperationName);
                    }
                    else if (string.Equals(result.ActionId, discardAction.Id, StringComparison.Ordinal))
                    {
                        _packageInstallService.DiscardSavedOperation();
                    }
                });
                return;
            }

            if (!canReuseExactTargets)
            {
                string planningFailure = freshPlan == null
                    ? "One or more saved root packages are no longer registered."
                    : freshPlan.ErrorMessage;
                var keepAction = new DeucarianEditorDialogAction(
                    "keep",
                    "Keep for Later",
                    DeucarianEditorIconIds.History,
                    DeucarianEditorDialogActionStyle.Primary);
                var discardAction = new DeucarianEditorDialogAction(
                    "discard",
                    "Discard",
                    DeucarianEditorIconIds.Remove,
                    DeucarianEditorDialogActionStyle.Destructive);
                var closeAction = new DeucarianEditorDialogAction(
                    "close",
                    "Close",
                    DeucarianEditorIconIds.Clear);
                var options = new DeucarianEditorDialogOptions(
                    "Package operation needs replanning",
                    "The current registry cannot reproduce a complete valid plan, so its saved URLs cannot be resumed safely.",
                    DeucarianEditorIconIds.Error,
                    new[] { keepAction, discardAction, closeAction })
                {
                    Details = summary +
                              (string.IsNullOrWhiteSpace(planningFailure)
                                  ? string.Empty
                                  : "\n\n" + planningFailure),
                    DefaultActionId = keepAction.Id,
                    CancelActionId = closeAction.Id
                };
                TryShowManagedDialog(options, result =>
                {
                    if (!result.WasCanceled &&
                        string.Equals(result.ActionId, discardAction.Id, StringComparison.Ordinal) &&
                        this != null)
                    {
                        if (!IsRecoveryStillCurrent(recovery))
                        {
                            RejectStaleRecoveryConfirmation(recovery);
                            return;
                        }

                        _packageInstallService.DiscardSavedOperation();
                    }
                });
                return;
            }

            var resumeAction = new DeucarianEditorDialogAction(
                "resume",
                recovery.CanResume ? "Resume" : "Restart",
                recovery.CanResume
                    ? DeucarianEditorIconIds.Play
                    : DeucarianEditorIconIds.Refresh,
                DeucarianEditorDialogActionStyle.Primary);
            var restartAction = new DeucarianEditorDialogAction(
                "restart",
                recovery.CanResume ? "Restart" : "Restart from Beginning",
                DeucarianEditorIconIds.Refresh);
            var discardRecoveryAction = new DeucarianEditorDialogAction(
                "discard",
                "Discard",
                DeucarianEditorIconIds.Remove,
                DeucarianEditorDialogActionStyle.Destructive);
            var recoveryOptions = new DeucarianEditorDialogOptions(
                "Resume package operation",
                summary,
                DeucarianEditorIconIds.History,
                new[] { resumeAction, restartAction, discardRecoveryAction })
            {
                DefaultActionId = resumeAction.Id,
                CancelActionId = string.Empty
            };
            TryShowManagedDialog(recoveryOptions, result =>
            {
                if (result.WasCanceled || this == null)
                {
                    return;
                }

                if (!IsRecoveryStillCurrent(recovery) ||
                    !_packageDependencyInstaller.IsPlanStillCurrent(freshPlan))
                {
                    RejectStaleRecoveryConfirmation(recovery);
                    return;
                }

                if (string.Equals(result.ActionId, resumeAction.Id, StringComparison.Ordinal))
                {
                    if (recovery.CanResume)
                    {
                        _packageInstallService.ResumeSavedOperation(freshPlan.RegistryFingerprint);
                    }
                    else
                    {
                        _packageInstallService.RestartSavedOperation(freshPlan.RegistryFingerprint);
                    }
                }
                else if (string.Equals(result.ActionId, restartAction.Id, StringComparison.Ordinal))
                {
                    _packageInstallService.RestartSavedOperation(freshPlan.RegistryFingerprint);
                }
                else if (string.Equals(result.ActionId, discardRecoveryAction.Id, StringComparison.Ordinal))
                {
                    _packageInstallService.DiscardSavedOperation();
                }
            });
        }

        private bool IsRecoveryStillCurrent(PackageOperationRecoveryRecord expectedRecovery)
        {
            if (expectedRecovery == null ||
                _packageInstallService == null ||
                _packageInstallService.IsBusy ||
                !_packageInstallService.TryGetSavedOperation(
                    out PackageOperationRecoveryRecord currentRecovery,
                    out _) ||
                currentRecovery == null)
            {
                return false;
            }

            return string.Equals(
                       currentRecovery.OperationId,
                       expectedRecovery.OperationId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       currentRecovery.RegistryFingerprint,
                       expectedRecovery.RegistryFingerprint,
                       StringComparison.Ordinal) &&
                   currentRecovery.CreatedAtUtcTicks == expectedRecovery.CreatedAtUtcTicks &&
                   currentRecovery.UpdatedAtUtcTicks == expectedRecovery.UpdatedAtUtcTicks;
        }

        private void RejectStaleRecoveryConfirmation(PackageOperationRecoveryRecord recovery)
        {
            _promptSavedOperationAfterDetectionRefresh =
                _packageInstallService != null && _packageInstallService.HasSavedOperation;
            RecordStaleConfirmation(
                string.IsNullOrWhiteSpace(recovery?.OperationName)
                    ? "Package operation recovery"
                    : recovery.OperationName,
                "Saved package operation state changed while the recovery dialog was open.");
        }

        private static void RecordStaleConfirmation(string operationName, string reason)
        {
            string message = (string.IsNullOrWhiteSpace(operationName)
                    ? "Package operation"
                    : operationName) +
                " was not changed. " + (reason ?? string.Empty).Trim();
            PackageInstallerLog.Install.Warning(message);
            PackageInstallerActivityService.Record(
                "Packages",
                PackageInstallerActivitySeverity.Warning,
                message);
        }

        private PackageDependencyInstallPlan CreateFreshRecoveryPlan(
            PackageOperationRecoveryRecord recovery)
        {
            if (recovery == null || _packageDependencyInstaller == null)
            {
                return null;
            }

            HashSet<string> rootIds = new HashSet<string>(
                recovery.Steps
                    .Where(step => step.State != PackageInstallProgressItemState.Completed &&
                                   step.State != PackageInstallProgressItemState.AlreadyCorrect)
                    .SelectMany(step => step.RootPackageIds),
                StringComparer.OrdinalIgnoreCase);
            if (rootIds.Count == 0)
            {
                rootIds.UnionWith(recovery.Steps
                    .Where(step => !step.IsDependency)
                    .Select(step => step.PackageId));
            }

            PackageDefinition[] roots = rootIds
                .Select(packageId =>
                    PackageRegistryProvider.TryGetPackage(packageId, out PackageDefinition definition)
                        ? definition
                        : null)
                .Where(definition => definition != null)
                .ToArray();
            return roots.Length == 0 || roots.Length != rootIds.Count
                ? null
                : _packageDependencyInstaller.CreateInstallPlan(
                    roots,
                    package => GetRecoveryRequestedChannel(
                        recovery,
                        package.PackageId,
                        GetSelectedChannel(package)),
                    includeInstalledRequestedPackages: true);
        }

        internal static PackageChannel GetRecoveryRequestedChannelForTests(
            PackageOperationRecoveryRecord recovery,
            string rootPackageId,
            PackageChannel fallback)
        {
            return GetRecoveryRequestedChannel(recovery, rootPackageId, fallback);
        }

        private static PackageChannel GetRecoveryRequestedChannel(
            PackageOperationRecoveryRecord recovery,
            string rootPackageId,
            PackageChannel fallback)
        {
            if (recovery == null || string.IsNullOrWhiteSpace(rootPackageId))
            {
                return fallback;
            }

            PackageOperationRootRequest rootRequest = recovery.RootRequests.FirstOrDefault(root =>
                root != null && string.Equals(
                    root.PackageId,
                    rootPackageId,
                    StringComparison.OrdinalIgnoreCase));
            if (rootRequest != null)
            {
                return rootRequest.Channel;
            }

            return recovery.Steps
                .Where(step => step.RootPackageIds.Contains(
                    rootPackageId,
                    StringComparer.OrdinalIgnoreCase))
                .Select(step => step.RequestedChannel)
                .DefaultIfEmpty(fallback)
                .First();
        }

        internal static string FormatRecoveryPlanDeltaForTests(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            return FormatRecoveryPlanDelta(recovery, freshPlan);
        }

        private static string FormatRecoveryPlanDelta(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            if (recovery == null || freshPlan == null || !freshPlan.IsValid)
            {
                return string.Empty;
            }

            Dictionary<string, PackageOperationRecoveryStep> previous = recovery.Steps
                .Where(step => step != null && !string.IsNullOrWhiteSpace(step.PackageId))
                .GroupBy(step => step.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageDependencyInstallStep> current = freshPlan.Steps
                .Where(step => step != null && step.PackageDefinition != null)
                .GroupBy(step => step.PackageDefinition.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            List<string> lines = new List<string>();

            foreach (string packageId in previous.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                bool hadPrevious = previous.TryGetValue(packageId, out PackageOperationRecoveryStep oldStep);
                bool hasCurrent = current.TryGetValue(packageId, out PackageDependencyInstallStep newStep);

                if (!hadPrevious)
                {
                    lines.Add("Added: " + newStep.PackageDefinition.DisplayName + " -> " + newStep.TargetUrl);
                }
                else if (!hasCurrent)
                {
                    lines.Add("Now skipped: " + oldStep.DisplayName + " is already correct or no longer required.");
                }
                else
                {
                    List<string> changes = DescribeRecoveryStepChanges(oldStep, newStep);
                    if (changes.Count > 0)
                    {
                        lines.Add(
                            "Changed: " + newStep.PackageDefinition.DisplayName +
                            "\n  " + string.Join("\n  ", changes.ToArray()));
                    }
                }
            }

            return string.Join("\n", lines.ToArray());
        }

        private static string BuildRecoveryRegistryDriftReview(
            string summary,
            PackageDependencyInstallPlan freshPlan,
            string planDelta)
        {
            List<string> lines = new List<string>
            {
                (summary ?? string.Empty).Trim(),
                string.Empty,
                "The registry fingerprint changed. Saved exact URLs will not be reused.",
                string.Empty,
                "Plan changes:",
                string.IsNullOrWhiteSpace(planDelta)
                    ? "No target URL changed in the remaining plan; registry metadata changed elsewhere."
                    : planDelta,
                string.Empty,
                "Fresh plan:"
            };
            lines.AddRange(freshPlan.Steps.Select(step =>
                "- " + step.PackageDefinition.DisplayName +
                " [" + GetChannelLabel(step.Channel) + "]\n  " + step.TargetUrl));

            if (freshPlan.RequiresPreflight)
            {
                lines.Add(string.Empty);
                lines.Add("Attention: this plan is bulk, multi-step, or carries migration, fallback, downgrade, conflict, or destructive risk.");
            }

            return string.Join("\n", lines.Where(line => line != null).ToArray()).Trim();
        }

        internal static bool CanReuseSavedExactTargetsForTests(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            return CanReuseSavedExactTargets(recovery, freshPlan);
        }

        private static bool CanReuseSavedExactTargets(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            return freshPlan != null &&
                   freshPlan.IsValid &&
                   PackageInstallService.CanReuseSavedTargets(
                       recovery,
                       freshPlan.RegistryFingerprint);
        }

        internal static PackageOperationRecoveryDisposition GetRecoveryDispositionForTests(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan,
            bool hasMatchingReloadMarker)
        {
            return GetRecoveryDisposition(recovery, freshPlan, hasMatchingReloadMarker);
        }

        private static PackageOperationRecoveryDisposition GetRecoveryDisposition(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan,
            bool hasMatchingReloadMarker)
        {
            return hasMatchingReloadMarker &&
                   recovery != null &&
                   recovery.CanResume &&
                   !recovery.RequiresManualRecovery &&
                   CanReuseSavedExactTargets(recovery, freshPlan)
                ? PackageOperationRecoveryDisposition.AutoResume
                : PackageOperationRecoveryDisposition.Prompt;
        }

        private void TrackPendingUpdateStatusInvalidations(IEnumerable<PackageDefinition> packageDefinitions)
        {
            foreach (PackageDefinition packageDefinition in packageDefinitions ?? Array.Empty<PackageDefinition>())
            {
                TrackPendingUpdateStatusInvalidation(packageDefinition);
            }
        }

        internal static bool ShouldRetainPendingUpdateStatusInvalidationsForTests(
            bool installBusy,
            bool awaitingPreflight)
        {
            return ShouldRetainPendingUpdateStatusInvalidations(installBusy, awaitingPreflight);
        }

        private static bool ShouldRetainPendingUpdateStatusInvalidations(
            bool installBusy,
            bool awaitingPreflight)
        {
            return installBusy || awaitingPreflight;
        }

        private void TrackPendingUpdateStatusInvalidation(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null || string.IsNullOrWhiteSpace(packageDefinition.PackageId))
            {
                return;
            }

            _pendingUpdateStatusInvalidationPackageIds.Add(packageDefinition.PackageId);
        }

        internal static bool TryConsumePendingUpdateStatusInvalidationForTests(
            ISet<string> pendingPackageIds,
            PackageDefinition completedPackage,
            bool success)
        {
            return TryConsumePendingUpdateStatusInvalidation(pendingPackageIds, completedPackage, success);
        }

        private static bool TryConsumePendingUpdateStatusInvalidation(
            ISet<string> pendingPackageIds,
            PackageDefinition completedPackage,
            bool success)
        {
            if (pendingPackageIds == null ||
                completedPackage == null ||
                string.IsNullOrWhiteSpace(completedPackage.PackageId) ||
                !pendingPackageIds.Remove(completedPackage.PackageId))
            {
                return false;
            }

            return success;
        }
    }
}
