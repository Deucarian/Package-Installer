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

    internal readonly struct PackageGraphNavigationRow
    {
        public PackageGraphNavigationRow(
            PackageGraphNavigationTargetKind targetKind,
            string id,
            string displayName,
            string summary,
            PackageGraphCategoryStatusSummary statusSummary,
            string iconKey,
            string tooltip,
            int depth,
            bool hasChildren,
            bool isExpanded,
            bool isInActivePath,
            bool isSelected,
            bool hasAttention)
        {
            TargetKind = targetKind;
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Summary = summary ?? string.Empty;
            StatusSummary = statusSummary;
            IconKey = string.IsNullOrWhiteSpace(iconKey) ? "package" : iconKey.Trim();
            Tooltip = tooltip ?? string.Empty;
            Depth = Math.Max(0, depth);
            HasChildren = hasChildren;
            IsExpanded = isExpanded && hasChildren;
            IsInActivePath = isInActivePath;
            IsSelected = isSelected;
            HasAttention = hasAttention || statusSummary.AttentionCount > 0;
        }

        public PackageGraphNavigationTargetKind TargetKind { get; }

        public string Id { get; }

        public string DisplayName { get; }

        public string Summary { get; }

        public PackageGraphCategoryStatusSummary StatusSummary { get; }

        public string IconKey { get; }

        public string Tooltip { get; }

        public int Depth { get; }

        public bool HasChildren { get; }

        public bool IsExpanded { get; }

        public bool IsInActivePath { get; }

        public bool IsSelected { get; }

        public bool HasAttention { get; }

        public bool IsOverview => TargetKind == PackageGraphNavigationTargetKind.Overview;

        public bool IsGroup => TargetKind == PackageGraphNavigationTargetKind.Group;

        public bool IsPackage => TargetKind == PackageGraphNavigationTargetKind.Package;
    }

    internal sealed partial class PackageInstallerWindow : EditorWindow
    {
        private const string WindowTitle = "Package Installer";
        private const string BootstrapMenuPath = "Tools/Deucarian/Set Up or Repair...";
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
        private const string InstallerMenuPath = "Tools/Deucarian/Package Installer...";
        private const float GlobalChannelOverridePopupWidth = 286f;
        private const float GlobalChannelOverridePopupMargin = 8f;
        private static readonly string[] GlobalChannelOptionLabels = { "Development", "Stable" };

        private const InstallerViewMode DefaultInstallerViewMode = InstallerViewMode.EcosystemGraph;
        private static readonly bool ListViewEnabled = false;

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
        private readonly Dictionary<string, HashSet<string>> _templateCompositionSelections =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _templateCompositionPresetIds =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
        private PackageGraphNavigationTargetKind _detailsPreviewedGraphTargetKind =
            PackageGraphNavigationTargetKind.Overview;
        private string _detailsPreviewedGraphTargetId = string.Empty;
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

        internal static bool DefaultsToEcosystemGraphForTests => DefaultInstallerViewMode == InstallerViewMode.EcosystemGraph;

        internal static string MenuPathForTests => InstallerMenuPath;

        internal static IReadOnlyList<string> UserFacingMenuPathsForTests => new[] { InstallerMenuPath };

        internal static IReadOnlyList<string> ViewToggleOrderForTests =>
            GetEnabledInstallerViewModes().Select(GetInstallerViewModeLabel).ToArray();

        internal static bool ListViewRequestResolvesToEcosystemGraphForTests =>
            ResolveInstallerViewMode(InstallerViewMode.List) == InstallerViewMode.EcosystemGraph;

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

        internal static string BootstrapMenuPathForTests => BootstrapMenuPath;
    }
}
