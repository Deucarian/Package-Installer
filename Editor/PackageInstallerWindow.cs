using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
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

    internal sealed class PackageInstallerWindow : EditorWindow
    {
        private const string WindowTitle = "Package Installer";
        private const string PackageId = "com.deucarian.package-installer";
        private const string PackageVersion = "1.1.58";
        private const float MinWindowWidth = 820f;
        private const float MinWindowHeight = 650f;
        private const float CompactLayoutWidth = 1180f;
        private const float NarrowLayoutWidth = 900f;
        private const float SidebarWidth = 340f;
        private const float SidebarRowMinHeight = 94f;
        private const float SidebarRowMaxHeight = 150f;
        private const float DetailLabelWidth = 118f;
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
        internal const string OperationFooterRowName = "package-installer-operation-footer";
        internal const string OperationFooterStatusGroupName = "package-installer-operation-footer-status";
        internal const string OperationFooterStatusIconName = "package-installer-operation-footer-status-icon";
        internal const string OperationFooterStatusLabelName = "package-installer-operation-footer-status-label";
        internal const string OperationFooterSummaryName = "package-installer-operation-footer-summary";
        internal const string OperationFooterDetailsButtonName = "package-installer-operation-footer-details-toggle";
        internal const string OperationFooterVersionName = "package-installer-operation-footer-version";
        internal const string WallpaperTopSafeFadeName = "package-installer-wallpaper-top-safe-fade";
        private const string ChannelPreferencePrefix = "Deucarian.PackageInstaller.SelectedChannel.";
        private const string AdvancedFoldoutPreferencePrefix = "Deucarian.PackageInstaller.AdvancedFoldout.";
        private const string CategoryFoldoutPreferencePrefix = "Deucarian.PackageInstaller.CategoryFoldout.";
        private const string OperationDrawerPreferencePrefix = "Deucarian.PackageInstaller.OperationDrawer.";
        private const string GraphStyleSheetPath =
            "Packages/com.deucarian.package-installer/Editor/UI/PackageInstaller/PackageInstallerGraph.uss";
        private const string InstallerMenuPath = "Tools/Deucarian/Package Installer";
        private const string GraphOpenTimingMenuPath =
            InstallerMenuPath + "/Diagnostics/Log Graph Open Timing";
        private const string InstalledStatusMarker = "\u2713";
        private const string NotInstalledStatusMarker = "\u25CB";
        private const string AttentionStatusMarker = "!";

        private enum InstallerViewMode
        {
            EcosystemGraph,
            List
        }

        private const InstallerViewMode DefaultInstallerViewMode = InstallerViewMode.EcosystemGraph;

        private enum SelectionKind
        {
            Package,
            Integration
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
            public const int InlinePadding = 12;
            public const int BlockPadding = 6;
            public const int RowGap = 6;
            public const int ControlGap = 8;
            public const float FooterHeight = 34f;
            public const float DrawerMaxHeight = 220f;
        }

        private sealed class VisualStatus
        {
            public VisualStatus(string marker, string label, VisualStatusKind kind)
            {
                Marker = marker ?? string.Empty;
                Label = label ?? string.Empty;
                Kind = kind;
            }

            public string Marker { get; }

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

        private PackageInstallService _packageInstallService;
        private PackageDetectionService _packageDetectionService;
        private PackageUpdateCheckService _packageUpdateCheckService;
        private PackageSampleImportService _packageSampleImportService;
        private PackageSampleDiscoveryService _packageSampleDiscoveryService;
        private PackageDependencyInstaller _packageDependencyInstaller;
        private PackageGraphBuilder _packageGraphBuilder;
        private readonly Dictionary<string, PackageChannel> _selectedChannels =
            new Dictionary<string, PackageChannel>();
        private readonly HashSet<string> _autoSelectedChannelPackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _advancedFoldouts =
            new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _categoryFoldouts =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private Vector2 _sidebarScrollPosition;
        private Vector2 _detailsScrollPosition;
        private Vector2 _operationDetailsScrollPosition;
        private SelectionKind _selectionKind = SelectionKind.Package;
        private string _selectedPackageId = string.Empty;
        private string _graphFocusedPackageId = string.Empty;
        private string _graphFocusedGroupId = string.Empty;
        private PackageGraphModel _cachedPackageGraph;
        private PackageGraphModel _lastPackageGraph;
        private bool _graphModelCacheDirty = true;
        private string _graphModelCacheInvalidationReason = "initial load";
        private readonly PackageVisibilityFilterState _visibilityFilterState =
            new PackageVisibilityFilterState();
        private bool _checkUpdatesAfterDetectionRefresh;
        private bool _operationDetailsExpanded;
        private InstallerViewMode _viewMode = DefaultInstallerViewMode;

        private Button _listViewButton;
        private Button _graphViewButton;
        private Button _graphRefreshButton;
        private Button _graphCheckUpdatesButton;
        private Button _graphUpdateAllButton;
        private Button _graphInstallAllButton;
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
        private VisualElement _operationFooterContainer;
        private VisualElement _operationFooterStatusGroup;
        private Label _operationFooterStatusIcon;
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
        private Color _panelBackgroundColor;
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

        private GUIStyle _windowStyle;
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
        private GUIStyle _markerStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _secondaryButtonStyle;

        [MenuItem(InstallerMenuPath)]
        public static void Open()
        {
            PackageInstallerWindow window = GetWindow<PackageInstallerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
            window.Show();
        }

        [MenuItem(GraphOpenTimingMenuPath)]
        private static void ToggleGraphOpenTimingDiagnostics()
        {
            PackageInstallerLoggingPreferences.GraphOpenDiagnosticsLogging =
                !PackageInstallerLoggingPreferences.GraphOpenDiagnosticsLogging;
        }

        [MenuItem(GraphOpenTimingMenuPath, true)]
        private static bool ValidateGraphOpenTimingDiagnostics()
        {
            Menu.SetChecked(
                GraphOpenTimingMenuPath,
                PackageInstallerLoggingPreferences.GraphOpenDiagnosticsLogging);
            return true;
        }

        internal static bool DefaultsToEcosystemGraphForTests => DefaultInstallerViewMode == InstallerViewMode.EcosystemGraph;

        internal static string MenuPathForTests => InstallerMenuPath;

        internal static string GraphOpenTimingMenuPathForTests => GraphOpenTimingMenuPath;

        internal static IReadOnlyList<string> ViewToggleOrderForTests => new[] { "Ecosystem Graph", "List View" };

        internal static Vector2 MinWindowSizeForTests => new Vector2(MinWindowWidth, MinWindowHeight);

        internal static string PackageIdForTests => PackageId;

        internal static string PackageVersionForTests => PackageVersion;

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
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(MinWindowWidth, MinWindowHeight);

            _packageInstallService = new PackageInstallService();
            _packageDetectionService = new PackageDetectionService();
            _packageUpdateCheckService = new PackageUpdateCheckService(_packageDetectionService);
            _packageSampleImportService = new PackageSampleImportService();
            _packageSampleDiscoveryService = new PackageSampleDiscoveryService();
            _packageDependencyInstaller = new PackageDependencyInstaller(
                _packageInstallService,
                _packageDetectionService);
            _packageGraphBuilder = new PackageGraphBuilder(
                packageId => _packageDetectionService != null && _packageDetectionService.IsInstalled(packageId),
                GetSelectedChannel,
                packageDefinition => _packageUpdateCheckService != null
                    ? _packageUpdateCheckService.GetStatus(packageDefinition, GetSelectedChannel(packageDefinition))
                    : null);
            PackageRegistryProvider.RefreshRemote();
            EnsureValidSelection();
            _operationDetailsExpanded = EditorPrefs.GetBool(GetOperationDrawerPreferenceKey(), false);

            PackageRegistryProvider.RegistryChanged += HandleRegistryChanged;
            _packageInstallService.StateChanged += Repaint;
            _packageInstallService.StateChanged += RefreshGraphView;
            _packageInstallService.StateChanged += UpdateOperationFooter;
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

            bool checkUpdatesAfterDetectionRefresh =
                PackageUpdateCheckPreferences.ShouldCheckOnWindowOpen(DateTime.UtcNow);

            if (!_packageInstallService.ResumeSavedOperation())
            {
                _checkUpdatesAfterDetectionRefresh = checkUpdatesAfterDetectionRefresh;
                _packageDetectionService.Refresh();
            }
            else if (checkUpdatesAfterDetectionRefresh)
            {
                _checkUpdatesAfterDetectionRefresh = true;
            }
        }

        private void OnDisable()
        {
            if (_packageInstallService != null)
            {
                _packageInstallService.StateChanged -= Repaint;
                _packageInstallService.StateChanged -= RefreshGraphView;
                _packageInstallService.StateChanged -= UpdateOperationFooter;
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
            }

            PackageRegistryProvider.RegistryChanged -= HandleRegistryChanged;
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
                out _operationDrawerScrollView,
                out _operationDrawerContent,
                out _operationDrawerTitleLabel,
                out _operationDrawerVerboseToggle,
                out _operationDrawerVerboseLabel,
                out _operationDrawerMessageLabel);
            content.Add(_operationDrawerContainer);

            _operationFooterContainer = CreateOperationFooterRow(() => SetOperationDetailsExpanded(!_operationDetailsExpanded));
            CacheOperationFooterElements(_operationFooterContainer);
            content.Add(_operationFooterContainer);

            SetViewMode(_viewMode);
            ApplyResponsiveLayout(position.width);
            UpdateOperationFooter();
            RefreshGraphView("window initialized");
        }

        private void ApplyResponsiveLayout(float contentWidth)
        {
            if (_windowContentRoot == null)
            {
                return;
            }

            PackageInstallerResponsiveMode nextMode = ResolveResponsiveMode(contentWidth);
            _responsiveMode = nextMode;

            _windowContentRoot.EnableInClassList("dpi-responsive--wide", nextMode == PackageInstallerResponsiveMode.Wide);
            _windowContentRoot.EnableInClassList("dpi-responsive--compact", nextMode == PackageInstallerResponsiveMode.Compact);
            _windowContentRoot.EnableInClassList("dpi-responsive--narrow", nextMode == PackageInstallerResponsiveMode.Narrow);

            _graphView?.SetResponsiveMode(nextMode);
        }

        private static PackageInstallerResponsiveMode ResolveResponsiveMode(float width)
        {
            if (width < NarrowLayoutWidth)
            {
                return PackageInstallerResponsiveMode.Narrow;
            }

            return width < CompactLayoutWidth
                ? PackageInstallerResponsiveMode.Compact
                : PackageInstallerResponsiveMode.Wide;
        }

        private static void ConfigureFixedWallpaper(VisualElement root)
        {
            ConfigureFixedWallpaper(root, null);
        }

        private static void ConfigureFixedWallpaper(VisualElement root, VisualElement wallpaperHost)
        {
            if (root == null)
            {
                return;
            }

            VisualElement host = wallpaperHost ?? root;
            host.AddToClassList("dpi-wallpaper-safe-shell");
            host.style.overflow = Overflow.Hidden;

            VisualElement background = root.Q<VisualElement>("deucarian-window-background") ??
                                       host.Q<VisualElement>("deucarian-window-background");
            VisualElement overlay = root.Q<VisualElement>("deucarian-window-overlay") ??
                                    host.Q<VisualElement>("deucarian-window-overlay");

            ReparentWallpaperLayer(host, background, 0);
            ReparentWallpaperLayer(host, overlay, background != null ? 1 : 0);

            ConfigureFixedLayer(background, "dpi-fixed-wallpaper-layer");
            ConfigureFixedLayer(overlay, "dpi-fixed-wallpaper-overlay");
            overlay?.AddToClassList("dpi-readability-overlay");
            PackageInstallerAmbientGlass.Install(host);
            EnsureWallpaperTopSafeFade(host);
        }

        internal static void ConfigureFixedWallpaperForTests(VisualElement root)
        {
            ConfigureFixedWallpaper(root);
        }

        internal static void ConfigureFixedWallpaperForTests(VisualElement root, VisualElement wallpaperHost)
        {
            ConfigureFixedWallpaper(root, wallpaperHost);
        }

        private static void ReparentWallpaperLayer(VisualElement host, VisualElement layer, int index)
        {
            if (host == null || layer == null || layer.parent == host)
            {
                return;
            }

            layer.RemoveFromHierarchy();
            host.Insert(Mathf.Clamp(index, 0, host.childCount), layer);
        }

        private static void EnsureWallpaperTopSafeFade(VisualElement host)
        {
            if (host == null || host.Q<VisualElement>(WallpaperTopSafeFadeName) != null)
            {
                return;
            }

            VisualElement fade = new VisualElement { name = WallpaperTopSafeFadeName };
            fade.AddToClassList("dpi-wallpaper-top-safe-fade");
            fade.pickingMode = PickingMode.Ignore;
            fade.style.position = Position.Absolute;
            fade.style.left = 0f;
            fade.style.right = 0f;
            fade.style.top = 0f;
            fade.style.height = 86f;
            host.Insert(host.childCount, fade);
        }

        private static void ConfigureFixedLayer(VisualElement element, string className)
        {
            if (element == null)
            {
                return;
            }

            element.AddToClassList(className);
            element.pickingMode = PickingMode.Ignore;
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.right = 0f;
            element.style.top = 0f;
            element.style.bottom = 0f;
            element.style.translate = new Translate(0f, 0f, 0f);
            element.style.scale = new Scale(Vector3.one);
            element.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
        }

        private void BuildViewToolbar(VisualElement content)
        {
            VisualElement toolbar = DeucarianEditorVisualShell.CreateToolbarRow();
            toolbar.AddToClassList("dpi-view-toolbar");

            _graphViewButton = CreateViewToggleButton("Ecosystem Graph", InstallerViewMode.EcosystemGraph);
            _listViewButton = CreateViewToggleButton("List View", InstallerViewMode.List);
            toolbar.Add(_graphViewButton);
            toolbar.Add(_listViewButton);

            _viewSummaryLabel = new Label();
            _viewSummaryLabel.AddToClassList("dpi-view-toolbar__summary");
            toolbar.Add(_viewSummaryLabel);

            VisualElement spacer = new VisualElement();
            spacer.AddToClassList("deucarian-toolbar-spacer");
            toolbar.Add(spacer);

            _graphRefreshButton = CreateGraphActionButton("Refresh", RefreshPackages);
            _graphCheckUpdatesButton = CreateGraphActionButton("Check Updates", CheckForUpdates);
            _graphUpdateAllButton = CreateGraphActionButton("Update All", UpdateAllPackages);
            _graphInstallAllButton = CreateGraphActionButton("Install All", InstallAllPackages);
            toolbar.Add(_graphRefreshButton);
            toolbar.Add(_graphCheckUpdatesButton);
            toolbar.Add(_graphUpdateAllButton);
            toolbar.Add(_graphInstallAllButton);

            content.Add(toolbar);
        }

        private Button CreateViewToggleButton(string text, InstallerViewMode viewMode)
        {
            Button button = new Button(() => SetViewMode(viewMode)) { text = text };
            button.AddToClassList("deucarian-toggle-button");
            return button;
        }

        private Button CreateGraphActionButton(string text, Action action)
        {
            Button button = new Button(() =>
            {
                action?.Invoke();
            })
            {
                text = text
            };
            button.AddToClassList("dpi-view-toolbar__action");
            return button;
        }

        private static VisualElement CreateOperationDrawer(
            Action<bool> verboseLoggingChanged,
            out ScrollView scrollView,
            out VisualElement content,
            out Label titleLabel,
            out Toggle verboseToggle,
            out Label verboseLabel,
            out Label messageLabel)
        {
            VisualElement drawer = new VisualElement { name = OperationDrawerName };
            drawer.AddToClassList("dpi-operation-surface");
            drawer.AddToClassList("dpi-operation-drawer");

            scrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                name = OperationDrawerScrollViewName,
                verticalScrollerVisibility = ScrollerVisibility.Auto,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            scrollView.AddToClassList("dpi-operation-drawer__scroll");
            drawer.Add(scrollView);

            content = new VisualElement { name = OperationDrawerContentName };
            content.AddToClassList("dpi-operation-content");
            scrollView.Add(content);

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-operation-row");
            header.AddToClassList("dpi-operation-row--header");
            content.Add(header);

            titleLabel = new Label("Last Operation Summary") { name = OperationDrawerTitleName };
            titleLabel.AddToClassList("dpi-operation-text--primary");
            titleLabel.AddToClassList("dpi-operation-drawer__title");
            titleLabel.style.color = DeucarianEditorVisualShell.Text;
            header.Add(titleLabel);

            VisualElement optionRow = new VisualElement();
            optionRow.AddToClassList("dpi-operation-row");
            optionRow.AddToClassList("dpi-operation-row--option");
            content.Add(optionRow);

            Toggle localVerboseToggle = new Toggle { name = OperationDrawerVerboseToggleName };
            localVerboseToggle.AddToClassList("dpi-operation-drawer__toggle");
            localVerboseToggle.tooltip = "Send normal Package Installer info messages to the Unity Console. Warnings and errors are always logged.";
            if (verboseLoggingChanged != null)
            {
                localVerboseToggle.RegisterValueChangedCallback(evt => verboseLoggingChanged(evt.newValue));
            }
            optionRow.Add(localVerboseToggle);
            verboseToggle = localVerboseToggle;

            verboseLabel = new Label("Verbose Console Logging") { name = OperationDrawerVerboseLabelName };
            verboseLabel.AddToClassList("dpi-operation-text--secondary");
            verboseLabel.AddToClassList("dpi-operation-drawer__option-label");
            verboseLabel.tooltip = localVerboseToggle.tooltip;
            verboseLabel.style.color = DeucarianEditorVisualShell.MutedText;
            verboseLabel.RegisterCallback<ClickEvent>(_ =>
            {
                localVerboseToggle.value = !localVerboseToggle.value;
            });
            optionRow.Add(verboseLabel);

            messageLabel = new Label("No detailed operation report is available.")
            {
                name = OperationDrawerMessageName
            };
            messageLabel.AddToClassList("dpi-operation-row");
            messageLabel.AddToClassList("dpi-operation-row--message");
            messageLabel.AddToClassList("dpi-operation-text--secondary");
            messageLabel.AddToClassList("dpi-operation-drawer__message");
            messageLabel.style.color = DeucarianEditorVisualShell.MutedText;
            content.Add(messageLabel);

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

            drawer.style.display = DisplayStyle.Flex;
            drawer.style.opacity = 1f;
            drawer.EnableInClassList("dpi-operation-drawer--expanded", expanded);
            drawer.EnableInClassList("dpi-operation-drawer--collapsed", !expanded);

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
                titleLabel.text = "Last Operation Summary";
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

        private static VisualElement CreateOperationFooterRow(Action detailsToggleAction)
        {
            VisualElement footer = new VisualElement { name = OperationFooterRowName };
            footer.AddToClassList("dpi-operation-surface");
            footer.AddToClassList("dpi-operation-footer");
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.flexShrink = 0f;
            footer.style.height = OperationFooterHeight;
            footer.style.minHeight = OperationFooterHeight;
            footer.style.maxHeight = OperationFooterHeight;
            footer.style.overflow = Overflow.Hidden;
            footer.style.opacity = 1f;
            footer.style.paddingLeft = OperationInlinePadding;
            footer.style.paddingRight = OperationInlinePadding;

            VisualElement statusGroup = new VisualElement { name = OperationFooterStatusGroupName };
            statusGroup.AddToClassList("dpi-operation-footer__status");
            statusGroup.style.flexDirection = FlexDirection.Row;
            statusGroup.style.alignItems = Align.Center;
            statusGroup.style.flexShrink = 0f;
            statusGroup.style.opacity = 1f;
            statusGroup.style.marginRight = OperationControlGap;

            Label statusIcon = new Label { name = OperationFooterStatusIconName };
            statusIcon.AddToClassList("dpi-operation-footer__status-icon");
            statusIcon.style.flexShrink = 0f;
            statusIcon.style.opacity = 1f;
            statusGroup.Add(statusIcon);

            Label statusLabel = new Label { name = OperationFooterStatusLabelName };
            statusLabel.AddToClassList("dpi-operation-footer__status-label");
            statusLabel.style.flexShrink = 0f;
            statusLabel.style.opacity = 1f;
            statusGroup.Add(statusLabel);
            footer.Add(statusGroup);

            Label summaryLabel = new Label { name = OperationFooterSummaryName };
            summaryLabel.AddToClassList("dpi-operation-footer__summary");
            summaryLabel.style.flexGrow = 1f;
            summaryLabel.style.flexShrink = 1f;
            summaryLabel.style.minWidth = 0f;
            summaryLabel.style.opacity = 1f;
            summaryLabel.style.marginRight = OperationControlGap;
            footer.Add(summaryLabel);

            VisualElement spacer = new VisualElement();
            spacer.AddToClassList("dpi-operation-footer__spacer");
            spacer.style.flexGrow = 0f;
            spacer.style.flexShrink = 0f;
            footer.Add(spacer);

            Button detailsButton = new Button { name = OperationFooterDetailsButtonName };
            if (detailsToggleAction != null)
            {
                detailsButton.clicked += detailsToggleAction;
            }

            detailsButton.AddToClassList("dpi-operation-footer__details-button");
            detailsButton.style.flexShrink = 0f;
            detailsButton.style.width = 96f;
            detailsButton.style.height = 24f;
            detailsButton.style.minHeight = 24f;
            detailsButton.style.maxHeight = 24f;
            detailsButton.style.opacity = 1f;
            detailsButton.style.marginRight = OperationControlGap;
            footer.Add(detailsButton);

            Label versionLabel = new Label { name = OperationFooterVersionName };
            versionLabel.AddToClassList("dpi-operation-footer__version");
            versionLabel.style.flexShrink = 0f;
            versionLabel.style.opacity = 1f;
            footer.Add(versionLabel);

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
            _operationFooterStatusIcon = footer.Q<Label>(OperationFooterStatusIconName);
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
            RefreshOperationDrawerContent();
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
            string safeVersionText = string.IsNullOrWhiteSpace(packageVersionText) ? PackageId : packageVersionText.Trim();
            string statusMarker = GetStatusMarker(statusKind);
            Color statusColor = GetStatusColor(statusKind);

            footer.EnableInClassList("dpi-operation-footer--expanded", detailsExpanded);
            footer.EnableInClassList("dpi-operation-footer--collapsed", !detailsExpanded);

            Label statusIcon = footer.Q<Label>(OperationFooterStatusIconName);
            Label statusLabel = footer.Q<Label>(OperationFooterStatusLabelName);
            Label summaryLabel = footer.Q<Label>(OperationFooterSummaryName);
            Button detailsButton = footer.Q<Button>(OperationFooterDetailsButtonName);
            Label versionLabel = footer.Q<Label>(OperationFooterVersionName);

            if (statusIcon != null)
            {
                statusIcon.text = string.IsNullOrWhiteSpace(statusMarker) ? "i" : statusMarker;
                statusIcon.tooltip = safeStatusText;
                statusIcon.style.color = statusColor;
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
                detailsButton.text = detailsExpanded ? "Hide Details" : "Show Details";
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

            element.RemoveFromClassList("dpi-operation-footer__status-icon--installed");
            element.RemoveFromClassList("dpi-operation-footer__status-icon--not-installed");
            element.RemoveFromClassList("dpi-operation-footer__status-icon--attention");
            element.RemoveFromClassList("dpi-operation-footer__status-icon--failed");
            element.RemoveFromClassList("dpi-operation-footer__status-icon--busy");
            element.RemoveFromClassList("dpi-operation-footer__status-icon--info");

            switch (statusKind)
            {
                case VisualStatusKind.Installed:
                    element.AddToClassList("dpi-operation-footer__status-icon--installed");
                    break;
                case VisualStatusKind.NotInstalled:
                    element.AddToClassList("dpi-operation-footer__status-icon--not-installed");
                    break;
                case VisualStatusKind.UpdateAvailable:
                    element.AddToClassList("dpi-operation-footer__status-icon--attention");
                    break;
                case VisualStatusKind.Failed:
                    element.AddToClassList("dpi-operation-footer__status-icon--failed");
                    break;
                case VisualStatusKind.Busy:
                    element.AddToClassList("dpi-operation-footer__status-icon--busy");
                    break;
                case VisualStatusKind.Info:
                case VisualStatusKind.Integration:
                default:
                    element.AddToClassList("dpi-operation-footer__status-icon--info");
                    break;
            }
        }

        private void SetViewMode(InstallerViewMode viewMode)
        {
            _viewMode = viewMode;
            UpdateViewVisibility();

            if (_viewMode == InstallerViewMode.EcosystemGraph)
            {
                RefreshGraphView("view mode changed");
            }

            Repaint();
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
                _listViewButton.EnableInClassList("deucarian-toggle-button--active", !graphMode);
            }

            if (_graphViewButton != null)
            {
                _graphViewButton.EnableInClassList("deucarian-toggle-button--active", graphMode);
            }

            bool busy = IsAnyOperationBusy();
            PackageDefinition[] packagesWithUpdates = _packageUpdateCheckService != null
                ? GetPackagesWithUpdates()
                : Array.Empty<PackageDefinition>();

            if (_graphRefreshButton != null)
            {
                _graphRefreshButton.style.display = graphMode ? DisplayStyle.Flex : DisplayStyle.None;
                _graphRefreshButton.SetEnabled(!busy);
            }

            if (_graphCheckUpdatesButton != null)
            {
                _graphCheckUpdatesButton.style.display = graphMode ? DisplayStyle.Flex : DisplayStyle.None;
                _graphCheckUpdatesButton.SetEnabled(!busy);
            }

            if (_graphUpdateAllButton != null)
            {
                _graphUpdateAllButton.style.display = graphMode ? DisplayStyle.Flex : DisplayStyle.None;
                _graphUpdateAllButton.SetEnabled(!busy && packagesWithUpdates.Length > 0);
            }

            if (_graphInstallAllButton != null)
            {
                _graphInstallAllButton.style.display = graphMode ? DisplayStyle.Flex : DisplayStyle.None;
                _graphInstallAllButton.SetEnabled(!busy);
            }

            if (_viewSummaryLabel != null)
            {
                _viewSummaryLabel.text = PackageRegistryProvider.All.Count + " packages - " + PackageRegistryProvider.StatusMessage;
            }
        }

        private void RefreshGraphView()
        {
            RefreshGraphView("refresh");
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
                       _graphFocusedPackageId,
                       _graphFocusedGroupId,
                       graphCacheDirty))
            {
                PackageGraphModel graph = GetOrBuildPackageGraphModel();
                PackageGraphOpenProfiler.Current?.SetGraphCounts(graph);

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
                        _visibilityFilterState,
                        visiblePackageIds);

                    ClearGraphSelectionIfHidden(visiblePackageIds);

                    filterCounts = PackageVisibilityFilter.CalculateCounts(
                        graph,
                        _visibilityFilterState);
                    hiddenRelatedCount = PackageVisibilityFilter.CountHiddenRelatedPackages(
                        graph,
                        _graphFocusedPackageId,
                        visiblePackageIds);
                }

                _graphView.SetGraph(
                    graph,
                    _selectedPackageId,
                    _graphFocusedPackageId,
                    _graphFocusedGroupId,
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

            if (!string.IsNullOrWhiteSpace(_graphFocusedPackageId))
            {
                return;
            }

            if (!ShouldClearGraphSelectionForFilters(
                    _selectedPackageId,
                    _graphFocusedPackageId,
                    visiblePackageIds))
            {
                return;
            }

            _selectionKind = SelectionKind.Package;
            _selectedPackageId = string.Empty;
            _graphFocusedPackageId = string.Empty;
            _graphFocusedGroupId = string.Empty;
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
            _graphFocusedPackageId = packageDefinition.PackageId;
            _graphFocusedGroupId = GetGraphPackageGroupId(packageDefinition.PackageId);
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

            if (string.Equals(group.Id, _graphFocusedGroupId, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(_graphFocusedPackageId))
            {
                NavigateGraphToGroupOrRoot(GetGraphParentGroupId(group.Id));
                return;
            }

            NavigateGraphToGroup(group.Id);
        }

        private void HandleGraphBackNavigation()
        {
            if (!string.IsNullOrWhiteSpace(_graphFocusedPackageId))
            {
                string parentGroupId = GetGraphPackageGroupId(_graphFocusedPackageId);
                NavigateGraphToGroupOrRoot(parentGroupId);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_graphFocusedGroupId))
            {
                string parentGroupId = GetGraphParentGroupId(_graphFocusedGroupId);
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

            _selectionKind = SelectionKind.Package;
            _selectedPackageId = string.Empty;
            _graphFocusedPackageId = string.Empty;
            _graphFocusedGroupId = groupId;
            _detailsScrollPosition = Vector2.zero;
            RefreshGraphView("group focus");
            Repaint();
        }

        private void NavigateGraphToRoot()
        {
            _selectionKind = SelectionKind.Package;
            _selectedPackageId = string.Empty;
            _graphFocusedPackageId = string.Empty;
            _graphFocusedGroupId = string.Empty;
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

            HandleGraphBackNavigation();
            evt.StopPropagation();
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
            _graphFocusedPackageId = packageDefinition.PackageId;
            _graphFocusedGroupId = GetGraphPackageGroupId(packageDefinition.PackageId);

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
            DrawDetailsPane();
        }

        private void DrawListViewGui()
        {
            EnsureStyles();
            DrawWindowBackground();
            EnsureValidSelection();

            using (new EditorGUILayout.VerticalScope(_windowStyle))
            {
                DrawHeader();

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

            _mainBackgroundColor = DeucarianEditorVisualShell.DeepBackground;
            _sidebarBackgroundColor = DeucarianEditorVisualShell.MainPanel;
            _detailsBackgroundColor = DeucarianEditorVisualShell.MainPanel;
            _panelBackgroundColor = DeucarianEditorVisualShell.NestedSurface;
            _headerPanelBackgroundColor = DeucarianEditorVisualShell.HeaderPanel;
            _sampleRowBackgroundColor = DeucarianEditorVisualShell.NestedSurface;
            _panelBorderColor = DeucarianEditorVisualShell.Border;
            _interactiveBorderColor = DeucarianEditorVisualShell.InteractiveBorder;
            _separatorColor = DeucarianEditorVisualShell.SubtleBorder;
            _rowBackgroundColor = new Color(32f / 255f, 47f / 255f, 56f / 255f, 0.46f);
            _rowHoverColor = new Color(32f / 255f, 47f / 255f, 56f / 255f, 0.62f);
            _rowSelectedColor = new Color(35f / 255f, 62f / 255f, 66f / 255f, 0.58f);
            _operationDrawerBackgroundColor = DeucarianEditorVisualShell.NestedSurface;
            _operationDrawerBackgroundColor.a = 0.52f;
            _operationDrawerBorderColor = DeucarianEditorVisualShell.InteractiveBorder;
            _operationDrawerBorderColor.a = 0.38f;
            _textColor = DeucarianEditorVisualShell.Text;
            _mutedTextColor = DeucarianEditorVisualShell.MutedText;

            _windowStyle = new GUIStyle();
            _windowStyle.padding = new RectOffset(12, 12, 10, 10);

            _sidebarStyle = new GUIStyle();
            _sidebarStyle.padding = new RectOffset(10, 10, 10, 10);

            _detailsStyle = new GUIStyle();
            _detailsStyle.padding = new RectOffset(10, 10, 10, 10);

            _sampleRowStyle = new GUIStyle();
            _sampleRowStyle.padding = new RectOffset(10, 10, 8, 8);
            _sampleRowStyle.margin = new RectOffset(0, 0, 2, 6);

            _titleStyle = new GUIStyle(DeucarianEditorStyles.PackageHeaderTitle);
            _titleStyle.fontSize = 15;
            _titleStyle.wordWrap = true;

            _subtitleStyle = new GUIStyle(DeucarianEditorStyles.PackageHeaderSubtitle);

            _sectionTitleStyle = new GUIStyle(DeucarianEditorStyles.SectionTitle);

            _miniLabelStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            _miniLabelStyle.normal.textColor = _textColor;
            _miniLabelStyle.wordWrap = true;
            _miniLabelStyle.clipping = TextClipping.Overflow;

            _mutedMiniLabelStyle = new GUIStyle(DeucarianEditorStyles.MutedLabel);
            _mutedMiniLabelStyle.fontSize = EditorStyles.wordWrappedMiniLabel.fontSize;
            _mutedMiniLabelStyle.wordWrap = true;
            _mutedMiniLabelStyle.clipping = TextClipping.Overflow;

            _rowTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel);
            _rowTitleStyle.normal.textColor = _textColor;
            _rowTitleStyle.wordWrap = true;
            _rowTitleStyle.clipping = TextClipping.Clip;

            _rowSubLabelStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            _rowSubLabelStyle.normal.textColor = _mutedTextColor;
            _rowSubLabelStyle.wordWrap = true;
            _rowSubLabelStyle.clipping = TextClipping.Clip;

            _rowStatusStyle = new GUIStyle(EditorStyles.miniLabel);
            _rowStatusStyle.normal.textColor = _textColor;
            _rowStatusStyle.alignment = TextAnchor.MiddleLeft;
            _rowStatusStyle.clipping = TextClipping.Clip;

            _markerStyle = new GUIStyle(EditorStyles.miniBoldLabel);
            _markerStyle.alignment = TextAnchor.MiddleCenter;
            _markerStyle.fontSize = 10;
            _markerStyle.normal.textColor = _textColor;

            _foldoutStyle = new GUIStyle(EditorStyles.foldout);
            _foldoutStyle.normal.textColor = _textColor;
            _foldoutStyle.onNormal.textColor = _textColor;
            _foldoutStyle.hover.textColor = _textColor;
            _foldoutStyle.onHover.textColor = _textColor;
            _foldoutStyle.fontStyle = FontStyle.Bold;

            _primaryButtonStyle = new GUIStyle(EditorStyles.miniButton);
            _primaryButtonStyle.fontStyle = FontStyle.Bold;
            _primaryButtonStyle.fixedHeight = 24f;

            _secondaryButtonStyle = new GUIStyle(DeucarianEditorStyles.ToolbarButton);
            _secondaryButtonStyle.fixedHeight = 24f;

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

            DeucarianEditorChrome.DrawPackageHeader(
                "package-installer",
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

                bool checkOnStartup = PackageUpdateCheckPreferences.CheckOnStartup;
                bool nextCheckOnStartup = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Check on Startup",
                        "Check for updates once per Unity editor session after startup."),
                    checkOnStartup,
                    GUILayout.Width(compact ? 136f : 142f));

                if (nextCheckOnStartup != checkOnStartup)
                {
                    PackageUpdateCheckPreferences.CheckOnStartup = nextCheckOnStartup;
                }
            }
        }

        private void DrawHeaderButtonRow()
        {
            PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawHeaderButton("Refresh", 82f, IsAnyOperationBusy(), RefreshPackages);
                DrawHeaderButton("Check Updates", 118f, IsAnyOperationBusy(), CheckForUpdates);
                DrawHeaderButton("Update All", 92f, packagesWithUpdates.Length == 0 || IsAnyOperationBusy(), UpdateAllPackages);
                DrawHeaderButton("Install All", 86f, IsAnyOperationBusy(), InstallAllPackages);
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawHeaderButton(string label, float width, bool disabled, Action action)
        {
            using (new EditorGUI.DisabledScope(disabled))
            {
                if (GUILayout.Button(label, _secondaryButtonStyle, GUILayout.Width(width)))
                {
                    action?.Invoke();
                }
            }
        }

        private void RefreshPackages()
        {
            InvalidateGraphModelCache("manual refresh");
            PackageRegistryProvider.RefreshRemote();
            _packageDetectionService.Refresh();
            _packageUpdateCheckService.InvalidateAll();
            RefreshGraphView("manual refresh");
        }

        private void CheckForUpdates()
        {
            InvalidateGraphModelCache("manual update check");
            _checkUpdatesAfterDetectionRefresh = true;
            PackageRegistryProvider.RefreshRemote();
            _packageUpdateCheckService.InvalidateAll();

            if (!_packageDetectionService.IsRefreshing)
            {
                _packageDetectionService.Refresh();
            }
            else
            {
                Repaint();
            }
        }

        private void UpdateAllPackages()
        {
            _packageDependencyInstaller.UpdateAll(
                GetPackagesWithUpdates(),
                GetSelectedChannel);
            _packageUpdateCheckService.InvalidateAll();
        }

        private void InstallAllPackages()
        {
            _packageDependencyInstaller.InstallAll(GetSelectedChannel);
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
                new GUIContent("Search", "Find packages by package name, package ID, or category."),
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
            bool drewIntegrationHeader = false;
            bool drewAnyCategory = false;

            foreach (PackageCategoryListView categoryView in categoryViews)
            {
                if (!categoryView.HasFilteredPackages)
                {
                    continue;
                }

                bool integrationCategory = string.Equals(categoryView.Category, "Integration", StringComparison.OrdinalIgnoreCase);

                if (integrationCategory)
                {
                    if (!drewIntegrationHeader && drewPackageHeader)
                    {
                        GUILayout.Space(10f);
                        DrawHorizontalSeparator();
                        GUILayout.Space(8f);
                    }

                    DrawSidebarSection("Integration Packages", categoryView, SelectionKind.Integration);
                    drewIntegrationHeader = true;
                }
                else
                {
                    DrawSidebarSection(
                        drewPackageHeader ? null : "Packages",
                        categoryView,
                        SelectionKind.Package);
                    drewPackageHeader = true;
                }

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
            PackageCategoryListView categoryView,
            SelectionKind selectionKind)
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
                DrawSidebarRow(packageDefinition, selectionKind);
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

            Rect titleRect = new Rect(
                rowRect.x + 10f,
                rowRect.y + 8f,
                rowRect.width - 20f,
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

            if (selectedDefinition == null)
            {
                PackageGraphGroup focusedGroup = GetFocusedGraphGroup();

                if (focusedGroup != null)
                {
                    DrawGraphGroupDetails(focusedGroup);
                }
                else
                {
                    DrawEcosystemOverviewDashboard();
                }
            }
            else if (_selectionKind == SelectionKind.Integration)
            {
                DrawIntegrationDetails(selectedDefinition);
            }
            else
            {
                DrawPackageDetails(selectedDefinition);
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
            PackageGraphGroup[] groups = graph == null
                ? Array.Empty<PackageGraphGroup>()
                : graph.Groups
                    .Where(group => group != null && string.IsNullOrWhiteSpace(group.ParentGroupId))
                    .OrderBy(group => group.SortOrder)
                    .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            int installedCount = nodes.Count(node => node.IsInstalled);
            int updateCount = nodes.Count(node => node.Status == PackageGraphNodeStatus.UpdateAvailable);
            int attentionCount = nodes.Count(node =>
                node.Status == PackageGraphNodeStatus.Missing ||
                node.Status == PackageGraphNodeStatus.Warning);
            int notInstalledCount = Math.Max(0, nodes.Length - installedCount);

            DrawPanel("Ecosystem Overview", () =>
            {
                EditorGUILayout.LabelField("Deucarian Unity Package System", _titleStyle);
                EditorGUILayout.LabelField(
                    "Select a group or package node to inspect details. Use pan, zoom, Fit, 100%, and Center to navigate the graph.",
                    _mutedMiniLabelStyle);
                GUILayout.Space(8f);

                DrawKeyValueRow("Packages", nodes.Length.ToString());
                DrawFlatStatusRow(InstalledStatusMarker, installedCount + " installed", VisualStatusKind.Installed);
                DrawFlatStatusRow(NotInstalledStatusMarker, notInstalledCount + " not installed", VisualStatusKind.NotInstalled);
                DrawFlatStatusRow(AttentionStatusMarker, updateCount + " updates", VisualStatusKind.UpdateAvailable);
                DrawFlatStatusRow(
                    AttentionStatusMarker,
                    attentionCount + " attention",
                    attentionCount > 0 ? VisualStatusKind.UpdateAvailable : VisualStatusKind.Info);
                GUILayout.Space(6f);
                DrawKeyValueRow("Registry", PackageRegistryProvider.StatusMessage);
                DrawKeyValueRow("Filters", GetActiveFilterSummary());
            }, GUILayout.ExpandWidth(true));

            DrawPanel("Groups", () =>
            {
                if (groups.Length == 0)
                {
                    EditorGUILayout.LabelField("No structural groups are available.", _mutedMiniLabelStyle);
                    return;
                }

                foreach (PackageGraphGroup group in groups)
                {
                    DrawEcosystemOverviewGroupRow(group);
                }
            }, GUILayout.ExpandWidth(true));
        }

        private void DrawEcosystemOverviewGroupRow(PackageGraphGroup group)
        {
            PackageGraphNode[] descendants = GetGraphGroupDescendantPackages(group.Id).ToArray();
            int installedCount = descendants.Count(node => node.IsInstalled);
            int updateCount = descendants.Count(node => node.Status == PackageGraphNodeStatus.UpdateAvailable);
            int attentionCount = descendants.Count(node =>
                node.Status == PackageGraphNodeStatus.Missing ||
                node.Status == PackageGraphNodeStatus.Warning);
            Rect rowRect = GUILayoutUtility.GetRect(1f, 30f, GUILayout.ExpandWidth(true));
            bool hover = rowRect.Contains(Event.current.mousePosition);

            if (Event.current.type == EventType.Repaint)
            {
                DeucarianEditorVisualShell.DrawInsetSurface(
                    rowRect,
                    hover ? _rowHoverColor : _sampleRowBackgroundColor,
                    hover ? _interactiveBorderColor : _separatorColor,
                    4f);
            }

            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                HandleGraphGroupFocused(group);
                Event.current.Use();
            }

            EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);

            Rect nameRect = new Rect(rowRect.x + 8f, rowRect.y + 6f, rowRect.width * 0.44f, 18f);
            GUI.Label(nameRect, new GUIContent(group.DisplayName, group.Description), _miniLabelStyle);

            Rect countRect = new Rect(nameRect.xMax + 4f, rowRect.y + 6f, 74f, 18f);
            GUI.Label(
                countRect,
                descendants.Length == 1 ? "1 package" : descendants.Length + " packages",
                _mutedMiniLabelStyle);

            Rect statusRect = new Rect(rowRect.xMax - 172f, rowRect.y + 6f, 160f, 18f);
            string summary = installedCount + " / " + descendants.Length + " installed";
            if (updateCount > 0)
            {
                summary += "  " + updateCount + " updates";
            }
            else if (attentionCount > 0)
            {
                summary += "  " + attentionCount + " attention";
            }

            DrawStatusBadge(
                statusRect,
                summary,
                updateCount > 0 || attentionCount > 0 ? VisualStatusKind.UpdateAvailable : VisualStatusKind.Installed,
                _rowStatusStyle);
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

        private float GetDetailsContentWidth()
        {
            return Mathf.Max(0f, position.width - SidebarWidth - 56f);
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
            if (packageDefinition == null || string.IsNullOrWhiteSpace(packageDefinition.PackageId))
            {
                return "package-installer";
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
            DrawKeyValueRow("Category", GetPackageHierarchyPath(packageDefinition));
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

            if (packageDefinition.Dependencies.Count > 0)
            {
                DrawKeyValueRow("Dependencies", GetDependencyDisplayNames(packageDefinition));
            }

            if (updateStatus.HasUnbumpedPackageVersionWarning)
            {
                DrawInlineHelp(updateStatus.PackageVersionWarningMessage, VisualStatusKind.UpdateAvailable);
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
                    EditorGUILayout.LabelField("Selected", _mutedMiniLabelStyle, GUILayout.Width(DetailLabelWidth));
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
            }, GUILayout.ExpandWidth(true));
        }

        private void DrawRequirementsPanel(PackageDefinition packageDefinition)
        {
            DrawPanel("Requirements", () =>
            {
                if (packageDefinition.Dependencies.Count == 0)
                {
                    EditorGUILayout.LabelField("No package dependencies.", _mutedMiniLabelStyle);
                    return;
                }

                foreach (string dependencyId in packageDefinition.Dependencies)
                {
                    DrawRequirementRow(dependencyId);
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
            DrawInlineMarker(markerRect, status.Marker, status.Kind);

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
            bool stackActions = GetDetailsContentWidth() < 460f;
            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

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
                    PackageDefinition[] dependentPackages = _packageDependencyInstaller.GetInstalledDependents(packageDefinition);

                    if (dependentPackages.Length > 0)
                    {
                        DrawInlineHelp(
                            "This package is required by installed package(s): " +
                            string.Join(", ", dependentPackages.Select(package => package.DisplayName).ToArray()) +
                            ". Remove those integration packages first.",
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
                    else
                    {
                        EditorGUILayout.LabelField("Package is installed. Reinstall uses the selected channel URL/ref.", _mutedMiniLabelStyle);
                    }
                }

                GUILayout.Space(6f);
            }

            if (!installed)
            {
                using (new EditorGUI.DisabledScope(queuedOrInstalling || actionsBusy))
                {
                    string buttonLabel = packageDefinition.IsIntegration ? "Install Integration" : "Install";

                    if (GUILayout.Button(
                            buttonLabel,
                            _primaryButtonStyle,
                            stackActions ? GUILayout.ExpandWidth(true) : GUILayout.Width(124f)))
                    {
                        _packageDependencyInstaller.InstallWithDependencies(packageDefinition, GetSelectedChannel);
                    }
                }

                return;
            }

            PackageDefinition[] installedDependents = _packageDependencyInstaller.GetInstalledDependents(packageDefinition);

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
            PackageDefinition[] installedDependents,
            bool queuedOrInstalling,
            bool actionsBusy)
        {
            using (new EditorGUI.DisabledScope(!updateStatus.IsUpdateAvailable || queuedOrInstalling || actionsBusy))
            {
                if (GUILayout.Button(
                        GetUpdateActionLabel(updateStatus, GetSelectedChannel(packageDefinition)),
                        _primaryButtonStyle,
                        GUILayout.Width(156f)))
                {
                    UpdatePackage(packageDefinition);
                }
            }

            using (new EditorGUI.DisabledScope(queuedOrInstalling || actionsBusy))
            {
                if (GUILayout.Button("Reinstall", _secondaryButtonStyle, GUILayout.Width(104f)))
                {
                    ReinstallPackage(packageDefinition);
                }
            }

            using (new EditorGUI.DisabledScope(installedDependents.Length > 0 || queuedOrInstalling || actionsBusy))
            {
                if (GUILayout.Button("Remove", _secondaryButtonStyle, GUILayout.Width(104f)))
                {
                    RemovePackage(packageDefinition);
                }
            }
        }

        private void DrawInstalledActionButtonsStacked(
            PackageDefinition packageDefinition,
            PackageUpdateStatus updateStatus,
            PackageDefinition[] installedDependents,
            bool queuedOrInstalling,
            bool actionsBusy)
        {
            using (new EditorGUI.DisabledScope(!updateStatus.IsUpdateAvailable || queuedOrInstalling || actionsBusy))
            {
                if (GUILayout.Button(
                        GetUpdateActionLabel(updateStatus, GetSelectedChannel(packageDefinition)),
                        _primaryButtonStyle,
                        GUILayout.ExpandWidth(true)))
                {
                    UpdatePackage(packageDefinition);
                }
            }

            using (new EditorGUI.DisabledScope(queuedOrInstalling || actionsBusy))
            {
                if (GUILayout.Button("Reinstall", _secondaryButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    ReinstallPackage(packageDefinition);
                }
            }

            using (new EditorGUI.DisabledScope(installedDependents.Length > 0 || queuedOrInstalling || actionsBusy))
            {
                if (GUILayout.Button("Remove", _secondaryButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    RemovePackage(packageDefinition);
                }
            }
        }

        private void UpdatePackage(PackageDefinition packageDefinition)
        {
            _packageDependencyInstaller.UpdateWithDependencies(
                packageDefinition,
                GetSelectedChannel);
            _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
            _checkUpdatesAfterDetectionRefresh = true;
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
            if (!EditorUtility.DisplayDialog(
                    "Remove Package",
                    "Remove " + packageDefinition.DisplayName + " from this Unity project?",
                    "Remove",
                    "Cancel"))
            {
                return;
            }

            _packageInstallService.Remove(packageDefinition);
            _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
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
                DrawInlineMarker(markerRect, status.Marker, status.Kind);

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

                using (new EditorGUI.DisabledScope(installed || queuedOrInstalling || actionsBusy))
                {
                    string label = installed
                        ? "Installed"
                        : companionDefinition.PackageId == "com.deucarian.diagnostics"
                            ? "Install Diagnostics"
                            : "Install";

                    if (GUILayout.Button(label, _secondaryButtonStyle, GUILayout.Width(148f)))
                    {
                        _packageDependencyInstaller.InstallWithDependencies(companionDefinition, GetSelectedChannel);
                    }
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
                DrawInlineMarker(markerRect, "SMP", VisualStatusKind.Info);

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

                using (new EditorGUI.DisabledScope(alreadyImported || IsAnyOperationBusy()))
                {
                    string buttonLabel = alreadyImported ? "Imported" : "Import";

                    if (GUILayout.Button(buttonLabel, _secondaryButtonStyle, GUILayout.Width(96f)))
                    {
                        _packageSampleImportService.ImportSample(
                            packageDefinition,
                            extraDefinition,
                            packageInfo);
                    }
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
            DrawSelectableValue("Category", GetPackageHierarchyPath(packageDefinition));
            DrawSelectableValue("Package kind", GetPackageKindDisplayName(packageDefinition));
            DrawSelectableValue("Legacy category", packageDefinition.Category);
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
            return PackageId + " " + PackageVersion;
        }

        private int GetOperationDrawerContentLineCount()
        {
            return CountOperationMessageLines(GetOperationDrawerReportText());
        }

        private string GetOperationDrawerReportText()
        {
            List<string> lines = new List<string>();
            string summary = GetLastOperationSummary();

            if (!string.IsNullOrWhiteSpace(summary))
            {
                lines.Add(summary.Trim());
            }

            IReadOnlyList<string> operationMessages = GetLastOperationMessages();

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

            IReadOnlyList<PackageInstallProgressItem> progressItems = GetLastProgressItems();

            if (progressItems != null)
            {
                foreach (PackageInstallProgressItem item in progressItems)
                {
                    if (item == null ||
                        (item.State != PackageInstallProgressItemState.Completed &&
                         item.State != PackageInstallProgressItemState.Failed &&
                         item.State != PackageInstallProgressItemState.Skipped))
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

            return lines.Count == 0
                ? "No detailed operation report is available."
                : string.Join("\n", lines.ToArray());
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
            IReadOnlyList<PackageInstallProgressItem> progressItems = GetLastProgressItems();
            IReadOnlyList<string> operationMessages = GetLastOperationMessages();

            return !string.IsNullOrWhiteSpace(GetLastOperationSummary()) ||
                   (operationMessages != null && operationMessages.Count > 0) ||
                   (progressItems != null && progressItems.Count > 0);
        }

        private bool HasLastOperationFailure()
        {
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
            if (!string.IsNullOrWhiteSpace(title))
            {
                DeucarianEditorChrome.DrawSectionHeader(title);
            }

            Rect rect = EditorGUILayout.BeginVertical(DeucarianEditorStyles.SectionBox, options);
            DrawSurface(rect, _panelBackgroundColor, _panelBorderColor);
            content?.Invoke();
            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);
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
            DeucarianEditorVisualShell.DrawFrostedSurface(rect, backgroundColor, borderColor);
        }

        private void DrawHorizontalSeparator()
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, _separatorColor);
            }
        }

        private void DrawInlineMarker(Rect rect, string text, VisualStatusKind statusKind)
        {
            DrawColoredRectLabel(rect, text, _markerStyle, GetStatusColor(statusKind));
        }

        private void DrawStatusBadge(string text, VisualStatusKind statusKind, params GUILayoutOption[] options)
        {
            GUIStyle style = _rowStatusStyle ?? EditorStyles.miniLabel;
            string safeText = text ?? string.Empty;
            GUIContent content = new GUIContent(GetStatusMarker(statusKind) + " " + safeText, safeText);
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
            Rect markerRect = new Rect(rect.x, rect.y, Mathf.Min(18f, rect.width), rect.height);
            Rect labelRect = new Rect(markerRect.xMax + 3f, rect.y, Mathf.Max(0f, rect.width - markerRect.width - 3f), rect.height);

            DrawColoredRectLabel(
                markerRect,
                new GUIContent(GetStatusMarker(statusKind), safeText),
                _markerStyle,
                GetStatusColor(statusKind));
            DrawColoredRectLabel(
                labelRect,
                new GUIContent(safeText, safeText),
                labelStyle,
                _textColor);
        }

        private void DrawFlatStatusRow(string marker, string text, VisualStatusKind statusKind)
        {
            Rect rowRect = GUILayoutUtility.GetRect(1f, 20f, GUILayout.ExpandWidth(true));
            Rect markerRect = new Rect(rowRect.x, rowRect.y + 1f, 18f, 18f);
            Rect labelRect = new Rect(markerRect.xMax + 4f, rowRect.y, rowRect.width - 22f, 20f);

            DrawColoredRectLabel(markerRect, marker, _markerStyle, GetStatusColor(statusKind));
            DrawColoredRectLabel(labelRect, new GUIContent(text, text), _miniLabelStyle, _textColor);
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
            string displayValue = string.IsNullOrWhiteSpace(value) ? "-" : value;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(label, label), _mutedMiniLabelStyle, GUILayout.Width(DetailLabelWidth));
                EditorGUILayout.LabelField(new GUIContent(displayValue, displayValue), _miniLabelStyle, GUILayout.ExpandWidth(true));
            }
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

        private void DrawColoredRectLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            DrawColoredRectLabel(rect, new GUIContent(text, text), style, color);
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
                return new VisualStatus("?", "Unknown", VisualStatusKind.Info);
            }

            if (_packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId))
            {
                return new VisualStatus("...", "Busy", VisualStatusKind.Busy);
            }

            if (_packageInstallService.IsBusy &&
                _packageInstallService.CurrentPackage != null &&
                string.Equals(
                    _packageInstallService.CurrentPackage.PackageId,
                    packageDefinition.PackageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new VisualStatus("...", "Busy", VisualStatusKind.Busy);
            }

            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

            if (_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                if (updateStatus.IsUpdateAvailable)
                {
                    if (updateStatus.Kind == PackageUpdateStatusKind.SwitchAvailable)
                    {
                        return new VisualStatus(AttentionStatusMarker, "Switch", VisualStatusKind.UpdateAvailable);
                    }

                    return new VisualStatus(AttentionStatusMarker, "Update", VisualStatusKind.UpdateAvailable);
                }

                return new VisualStatus(InstalledStatusMarker, "Installed", VisualStatusKind.Installed);
            }

            return new VisualStatus(NotInstalledStatusMarker, "Not Installed", VisualStatusKind.NotInstalled);
        }

        private static Color GetStatusColor(VisualStatusKind statusKind)
        {
            return DeucarianEditorStatusBadge.GetColor(ToEditorStatus(statusKind));
        }

        private static string GetStatusMarker(VisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case VisualStatusKind.Installed:
                    return InstalledStatusMarker;
                case VisualStatusKind.NotInstalled:
                    return NotInstalledStatusMarker;
                case VisualStatusKind.UpdateAvailable:
                case VisualStatusKind.Failed:
                    return AttentionStatusMarker;
                case VisualStatusKind.Busy:
                    return "...";
                case VisualStatusKind.Integration:
                    return "+";
                case VisualStatusKind.Info:
                default:
                    return "i";
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

            if (_selectedChannels.TryGetValue(packageDefinition.PackageId, out PackageChannel selectedChannel))
            {
                if (selectedChannel == PackageChannel.Custom &&
                    !_packageDetectionService.IsInstalled(packageDefinition.PackageId))
                {
                    return PackageChannel.Stable;
                }

                return selectedChannel;
            }

            if (TryGetStoredChannel(packageDefinition, out PackageChannel storedChannel))
            {
                _selectedChannels[packageDefinition.PackageId] = storedChannel;
                return storedChannel;
            }

            return PackageChannel.Stable;
        }

        private void SetSelectedChannel(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                return;
            }

            _selectedChannels[packageDefinition.PackageId] = channel;
            _autoSelectedChannelPackageIds.Remove(packageDefinition.PackageId);
            EditorPrefs.SetInt(GetChannelPreferenceKey(packageDefinition.PackageId), (int)channel);
            InvalidateGraphModelCache("selected channel changed");

            if (_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                _packageUpdateCheckService.CheckForUpdate(packageDefinition, channel);
            }
            else
            {
                _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
            }

            RefreshGraphView("selected channel changed");
        }

        private void SetAutoSelectedChannel(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                return;
            }

            _selectedChannels[packageDefinition.PackageId] = channel;
            _autoSelectedChannelPackageIds.Add(packageDefinition.PackageId);
            InvalidateGraphModelCache("installed channel sync");
            _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
        }

        private bool TryGetStoredChannel(PackageDefinition packageDefinition, out PackageChannel channel)
        {
            channel = PackageChannel.Stable;

            if (packageDefinition == null)
            {
                return false;
            }

            string key = GetChannelPreferenceKey(packageDefinition.PackageId);

            if (!EditorPrefs.HasKey(key))
            {
                return false;
            }

            int storedValue = EditorPrefs.GetInt(key, (int)PackageChannel.Stable);

            if (!Enum.IsDefined(typeof(PackageChannel), storedValue))
            {
                return false;
            }

            channel = (PackageChannel)storedValue;

            if (channel == PackageChannel.Custom &&
                !_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                channel = PackageChannel.Stable;
            }

            return true;
        }

        private bool HasStoredChannel(PackageDefinition packageDefinition)
        {
            return packageDefinition != null &&
                   EditorPrefs.HasKey(GetChannelPreferenceKey(packageDefinition.PackageId));
        }

        private string GetChannelPreferenceKey(string packageId)
        {
            return ChannelPreferencePrefix + Application.dataPath.Replace("\\", "/") + "." + packageId;
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

        private void SynchronizeSelectedChannelsFromInstalledPackages()
        {
            foreach (PackageDefinition packageDefinition in PackageRegistryProvider.All)
            {
                if (!_packageDetectionService.TryGetInstalledPackageChannel(
                        packageDefinition,
                        out PackageChannel installedChannel,
                        out _))
                {
                    continue;
                }

                PackageChannel currentChannel = GetSelectedChannel(packageDefinition);
                bool hasSelectedChannel = _selectedChannels.ContainsKey(packageDefinition.PackageId);
                bool hasStoredChannel = HasStoredChannel(packageDefinition);
                bool wasAutoSelectedChannel =
                    _autoSelectedChannelPackageIds.Contains(packageDefinition.PackageId);

                if (!ShouldApplyInstalledChannelSelection(
                        hasSelectedChannel,
                        hasStoredChannel,
                        wasAutoSelectedChannel))
                {
                    continue;
                }

                if (currentChannel != installedChannel)
                {
                    SetAutoSelectedChannel(packageDefinition, installedChannel);
                }
            }
        }

        internal static bool ShouldApplyInstalledChannelSelection(
            bool hasSelectedChannel,
            bool hasStoredChannel,
            bool wasAutoSelectedChannel)
        {
            return wasAutoSelectedChannel || (!hasSelectedChannel && !hasStoredChannel);
        }

        private void EnsureValidSelection()
        {
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
                   !string.IsNullOrWhiteSpace(_graphFocusedGroupId) &&
                   _lastPackageGraph.TryGetGroup(_graphFocusedGroupId, out PackageGraphGroup group)
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
            if (!string.IsNullOrWhiteSpace(_packageSampleImportService.LastErrorMessage))
            {
                return _packageSampleImportService.LastErrorMessage;
            }

            if (!string.IsNullOrWhiteSpace(_packageSampleImportService.LastStatusMessage))
            {
                return _packageSampleImportService.LastStatusMessage;
            }

            if (_packageInstallService.HasProgress &&
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
                   (_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                   (_packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking) ||
                   (_packageSampleImportService != null && _packageSampleImportService.IsBusy);
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

            if (status != null && status.HasPackageVersionTransition)
            {
                return currentVersion + " -> " + status.LatestVersion;
            }

            return currentVersion;
        }

        private static string GetUpdateActionLabel(PackageUpdateStatus status, PackageChannel channel)
        {
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
            _packageUpdateCheckService?.InvalidateAll();
            EnsureValidSelection();
            RefreshGraphView("registry changed");

            if (_checkUpdatesAfterDetectionRefresh &&
                !_packageDetectionService.IsRefreshing &&
                !PackageRegistryProvider.IsRemoteRefreshing)
            {
                RunDeferredUpdateCheck();
            }

            Repaint();
        }

        private void HandlePackageUpdateGraphStateChanged()
        {
            InvalidateGraphModelCache("update status changed");
            RefreshGraphView("update status changed");
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
            if (_packageUpdateCheckService.HasStatuses)
            {
                _checkUpdatesAfterDetectionRefresh = true;
            }

            _packageDetectionService.Refresh();
        }

        private void HandlePackageDetectionRefreshCompleted()
        {
            InvalidateGraphModelCache("installed package manifest changed");
            _packageSampleDiscoveryService?.ClearCache();
            SynchronizeSelectedChannelsFromInstalledPackages();
            RefreshGraphView("installed package refresh completed");

            if (!_checkUpdatesAfterDetectionRefresh)
            {
                return;
            }

            if (PackageRegistryProvider.IsRemoteRefreshing)
            {
                Repaint();
                return;
            }

            RunDeferredUpdateCheck();
        }

        private void RunDeferredUpdateCheck()
        {
            _checkUpdatesAfterDetectionRefresh = false;
            _packageUpdateCheckService.CheckForUpdates(PackageRegistryProvider.All, GetSelectedChannel);
        }
    }
}
