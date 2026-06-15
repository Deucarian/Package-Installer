using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageInstallerWindow : EditorWindow
    {
        private const string WindowTitle = "Package Installer";
        private const float MinWindowWidth = 850f;
        private const float MinWindowHeight = 650f;
        private const float SidebarWidth = 340f;
        private const float SidebarRowMinHeight = 94f;
        private const float SidebarRowMaxHeight = 150f;
        private const float DetailLabelWidth = 118f;
        private const float ProgressAreaHeight = 96f;
        private const float SummaryAreaHeight = 106f;
        private const string ChannelPreferencePrefix = "Deucarian.PackageInstaller.SelectedChannel.";
        private const string CategoryFoldoutPreferencePrefix = "Deucarian.PackageInstaller.CategoryFoldout.";

        private enum SelectionKind
        {
            Package,
            Bridge
        }

        private enum VisualStatusKind
        {
            Installed,
            NotInstalled,
            UpdateAvailable,
            Failed,
            Busy,
            Info,
            Bridge
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
        private SelectionKind _selectionKind = SelectionKind.Package;
        private string _selectedPackageId = string.Empty;
        private string _packageSearchText = string.Empty;
        private bool _showInstalledPackages = true;
        private bool _showNotInstalledPackages = true;
        private bool _checkUpdatesAfterDetectionRefresh;

        private bool _stylesInitialized;
        private bool _lastProSkin;
        private Color _mainBackgroundColor;
        private Color _headerBackgroundColor;
        private Color _sidebarBackgroundColor;
        private Color _detailsBackgroundColor;
        private Color _panelBackgroundColor;
        private Color _sampleRowBackgroundColor;
        private Color _panelBorderColor;
        private Color _separatorColor;
        private Color _rowBackgroundColor;
        private Color _rowHoverColor;
        private Color _rowSelectedColor;
        private Color _textColor;
        private Color _mutedTextColor;
        private Color _installedColor;
        private Color _notInstalledColor;
        private Color _updateColor;
        private Color _failedColor;
        private Color _busyColor;
        private Color _infoColor;
        private Color _bridgeColor;

        private GUIStyle _windowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _sidebarStyle;
        private GUIStyle _detailsStyle;
        private GUIStyle _panelStyle;
        private GUIStyle _detailHeaderStyle;
        private GUIStyle _sampleRowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _panelTitleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _miniLabelStyle;
        private GUIStyle _mutedMiniLabelStyle;
        private GUIStyle _rowTitleStyle;
        private GUIStyle _rowSubLabelStyle;
        private GUIStyle _rowStatusStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _markerStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _secondaryButtonStyle;

        [MenuItem("Tools/Deucarian/Package Installer")]
        public static void Open()
        {
            PackageInstallerWindow window = GetWindow<PackageInstallerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
            window.Show();
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
            PackageRegistryProvider.EnsureLoaded();
            EnsureValidSelection();

            PackageRegistryProvider.RegistryChanged += HandleRegistryChanged;
            _packageInstallService.StateChanged += Repaint;
            _packageInstallService.QueueCompleted += HandlePackageOperationCompleted;
            _packageDetectionService.StateChanged += Repaint;
            _packageDetectionService.RefreshCompleted += HandlePackageDetectionRefreshCompleted;
            _packageUpdateCheckService.StateChanged += Repaint;
            _packageSampleImportService.StateChanged += Repaint;

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
                _packageInstallService.QueueCompleted -= HandlePackageOperationCompleted;
                _packageInstallService.Dispose();
            }

            if (_packageDetectionService != null)
            {
                _packageDetectionService.StateChanged -= Repaint;
                _packageDetectionService.RefreshCompleted -= HandlePackageDetectionRefreshCompleted;
                _packageDetectionService.Dispose();
            }

            if (_packageUpdateCheckService != null)
            {
                _packageUpdateCheckService.StateChanged -= Repaint;
                _packageUpdateCheckService.Dispose();
            }

            if (_packageSampleImportService != null)
            {
                _packageSampleImportService.StateChanged -= Repaint;
            }

            PackageRegistryProvider.RegistryChanged -= HandleRegistryChanged;
        }

        private void OnGUI()
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
                    DrawRightPane();
                }
            }
        }

        private void DrawRightPane()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                DrawDetailsPane();
                DrawProgressFooter();
                DrawLastOperationSummaryPanel();
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

            _mainBackgroundColor = new Color(0.105f, 0.115f, 0.125f);
            _headerBackgroundColor = new Color(0.145f, 0.155f, 0.165f);
            _sidebarBackgroundColor = new Color(0.115f, 0.125f, 0.135f);
            _detailsBackgroundColor = new Color(0.12f, 0.13f, 0.14f);
            _panelBackgroundColor = new Color(0.16f, 0.17f, 0.18f);
            _sampleRowBackgroundColor = new Color(0.135f, 0.145f, 0.155f);
            _panelBorderColor = new Color(0.30f, 0.32f, 0.35f);
            _separatorColor = new Color(0.26f, 0.28f, 0.30f);
            _rowBackgroundColor = new Color(0.14f, 0.15f, 0.16f);
            _rowHoverColor = new Color(0.18f, 0.20f, 0.22f);
            _rowSelectedColor = new Color(0.18f, 0.28f, 0.39f);
            _textColor = new Color(0.88f, 0.90f, 0.92f);
            _mutedTextColor = new Color(0.58f, 0.63f, 0.68f);
            _installedColor = new Color(0.34f, 0.78f, 0.50f);
            _notInstalledColor = new Color(0.56f, 0.60f, 0.64f);
            _updateColor = new Color(0.96f, 0.68f, 0.25f);
            _failedColor = new Color(0.95f, 0.34f, 0.34f);
            _busyColor = new Color(0.35f, 0.62f, 0.95f);
            _infoColor = new Color(0.42f, 0.72f, 0.90f);
            _bridgeColor = new Color(0.44f, 0.67f, 0.95f);

            _windowStyle = new GUIStyle();
            _windowStyle.padding = new RectOffset(12, 12, 10, 10);

            _headerStyle = new GUIStyle();
            _headerStyle.padding = new RectOffset(14, 14, 10, 10);
            _headerStyle.margin = new RectOffset(0, 0, 0, 8);

            _sidebarStyle = new GUIStyle();
            _sidebarStyle.padding = new RectOffset(10, 10, 10, 10);

            _detailsStyle = new GUIStyle();
            _detailsStyle.padding = new RectOffset(10, 10, 10, 10);

            _panelStyle = new GUIStyle();
            _panelStyle.padding = new RectOffset(12, 12, 10, 10);
            _panelStyle.margin = new RectOffset(0, 0, 0, 8);

            _detailHeaderStyle = new GUIStyle();
            _detailHeaderStyle.padding = new RectOffset(14, 14, 12, 12);
            _detailHeaderStyle.margin = new RectOffset(0, 0, 0, 8);

            _sampleRowStyle = new GUIStyle();
            _sampleRowStyle.padding = new RectOffset(10, 10, 8, 8);
            _sampleRowStyle.margin = new RectOffset(0, 0, 2, 6);

            _titleStyle = new GUIStyle(EditorStyles.boldLabel);
            _titleStyle.fontSize = 18;
            _titleStyle.normal.textColor = _textColor;
            _titleStyle.wordWrap = true;

            _subtitleStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            _subtitleStyle.fontSize = 11;
            _subtitleStyle.normal.textColor = _mutedTextColor;

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel);
            _sectionTitleStyle.fontSize = 12;
            _sectionTitleStyle.normal.textColor = _textColor;

            _panelTitleStyle = new GUIStyle(EditorStyles.boldLabel);
            _panelTitleStyle.fontSize = 12;
            _panelTitleStyle.normal.textColor = _textColor;

            _labelStyle = new GUIStyle(EditorStyles.label);
            _labelStyle.normal.textColor = _textColor;
            _labelStyle.wordWrap = true;

            _miniLabelStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            _miniLabelStyle.normal.textColor = _textColor;

            _mutedMiniLabelStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            _mutedMiniLabelStyle.normal.textColor = _mutedTextColor;

            _rowTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel);
            _rowTitleStyle.normal.textColor = _textColor;
            _rowTitleStyle.wordWrap = true;
            _rowTitleStyle.clipping = TextClipping.Clip;

            _rowSubLabelStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            _rowSubLabelStyle.normal.textColor = _mutedTextColor;
            _rowSubLabelStyle.wordWrap = true;
            _rowSubLabelStyle.clipping = TextClipping.Clip;

            _rowStatusStyle = new GUIStyle(EditorStyles.miniBoldLabel);
            _rowStatusStyle.alignment = TextAnchor.MiddleCenter;
            _rowStatusStyle.clipping = TextClipping.Clip;

            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel);
            _badgeStyle.alignment = TextAnchor.MiddleCenter;
            _badgeStyle.normal.textColor = _textColor;
            _badgeStyle.clipping = TextClipping.Clip;

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

            _secondaryButtonStyle = new GUIStyle(EditorStyles.miniButton);
            _secondaryButtonStyle.fixedHeight = 24f;
        }

        private void DrawWindowBackground()
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), _mainBackgroundColor);
            }
        }

        private void DrawHeader()
        {
            bool compact = position.width < 1100f;
            Rect rect = BeginSurface(
                _headerStyle,
                _headerBackgroundColor,
                _panelBorderColor,
                GUILayout.MinHeight(compact ? 118f : 88f),
                GUILayout.ExpandWidth(true));

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    EditorGUILayout.LabelField("Deucarian Package Installer", _titleStyle);
                    EditorGUILayout.LabelField(
                        "Install, update, remove, and compose Deucarian packages through first-class bridge packages.",
                        _subtitleStyle);
                }

                if (!compact)
                {
                    GUILayout.Space(12f);

                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(420f)))
                    {
                        DrawRegistrySummary();
                        DrawUpdateSummary(false);
                    }
                }
            }

            if (compact)
            {
                GUILayout.Space(4f);
                DrawRegistrySummary();
                DrawUpdateSummary(true);
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
            _packageDetectionService.Refresh();
            _packageUpdateCheckService.InvalidateAll();
        }

        private void CheckForUpdates()
        {
            _checkUpdatesAfterDetectionRefresh = true;
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
                new PackageListFilterOptions(
                    _packageSearchText,
                    _showInstalledPackages,
                    _showNotInstalledPackages),
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
                new GUIContent("Search", "Search package names, package IDs, descriptions, URLs, versions, and dependencies."),
                _packageSearchText);

            if (EditorGUI.EndChangeCheck() && nextSearchText != _packageSearchText)
            {
                _packageSearchText = nextSearchText ?? string.Empty;
                Repaint();
            }

            EditorGUILayout.LabelField("Visibility", _mutedMiniLabelStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool nextShowInstalled = EditorGUILayout.ToggleLeft(
                    new GUIContent("Installed", "Show packages that Unity reports as installed."),
                    _showInstalledPackages,
                    GUILayout.Width(92f));
                bool nextShowNotInstalled = EditorGUILayout.ToggleLeft(
                    new GUIContent("Not Installed", "Show packages that Unity does not report as installed."),
                    _showNotInstalledPackages,
                    GUILayout.Width(118f));

                if (nextShowInstalled != _showInstalledPackages ||
                    nextShowNotInstalled != _showNotInstalledPackages)
                {
                    _showInstalledPackages = nextShowInstalled;
                    _showNotInstalledPackages = nextShowNotInstalled;
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
            bool drewBridgeHeader = false;
            bool drewAnyCategory = false;

            foreach (PackageCategoryListView categoryView in categoryViews)
            {
                if (!categoryView.HasFilteredPackages)
                {
                    continue;
                }

                bool bridgeCategory = string.Equals(categoryView.Category, "Bridge", StringComparison.OrdinalIgnoreCase);

                if (bridgeCategory)
                {
                    if (!drewBridgeHeader && drewPackageHeader)
                    {
                        GUILayout.Space(10f);
                        DrawHorizontalSeparator();
                        GUILayout.Space(8f);
                    }

                    DrawSidebarSection("Bridge Packages", categoryView, SelectionKind.Bridge);
                    drewBridgeHeader = true;
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
                EditorGUI.DrawRect(rowRect, background);
                DrawBorder(rowRect, selected ? GetStatusColor(status.Kind) : _separatorColor);

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
            string typeSummary = selectionKind == SelectionKind.Bridge ? "Bridge package" : packageDefinition.Category;

            if (packageDefinition.HasDisplayVersion)
            {
                return typeSummary + " | " + channelSummary + " | " + packageDefinition.DisplayVersion;
            }

            return typeSummary + " | " + channelSummary;
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

            _detailsScrollPosition = EditorGUILayout.BeginScrollView(_detailsScrollPosition, false, false);

            PackageDefinition selectedDefinition = GetSelectedDefinition();

            if (selectedDefinition == null)
            {
                DrawPanel("Selection", () =>
                {
                    EditorGUILayout.LabelField("Select a package or bridge package.", _mutedMiniLabelStyle);
                });
            }
            else if (_selectionKind == SelectionKind.Bridge)
            {
                DrawBridgeDetails(selectedDefinition);
            }
            else
            {
                DrawPackageDetails(selectedDefinition);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPackageDetails(PackageDefinition packageDefinition)
        {
            DrawDetailHeader(packageDefinition);
            DrawStatusPanel(packageDefinition);
            DrawChannelPanel(packageDefinition);
            DrawSourcePanel(packageDefinition);
            DrawActionsPanel(packageDefinition);
            DrawExtrasPanel(packageDefinition);
            DrawAdvancedPanel(packageDefinition);
        }

        private void DrawBridgeDetails(PackageDefinition packageDefinition)
        {
            DrawDetailHeader(packageDefinition);
            DrawStatusPanel(packageDefinition);
            DrawRequirementsPanel(packageDefinition);
            DrawChannelPanel(packageDefinition);
            DrawSourcePanel(packageDefinition);
            DrawActionsPanel(packageDefinition);
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
            Color accentColor = packageDefinition.IsBridge ? _bridgeColor : GetStatusColor(status.Kind);

            Rect rect = BeginSurface(
                _detailHeaderStyle,
                _panelBackgroundColor,
                accentColor,
                GUILayout.ExpandWidth(true));

            EditorGUILayout.LabelField("Overview", _panelTitleStyle);
            GUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect markerRect = GUILayoutUtility.GetRect(48f, 42f, GUILayout.Width(48f), GUILayout.Height(42f));
                DrawInlineMarker(markerRect, packageDefinition.IsBridge ? "LINK" : status.Marker, packageDefinition.IsBridge ? VisualStatusKind.Bridge : status.Kind);

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

            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);
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
            DrawPanel(packageDefinition.IsBridge ? "Bridge Status" : "Status", () =>
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
            DrawKeyValueRow("Type", packageDefinition.Category);

            if (_packageDetectionService.TryGetInstalledPackage(
                    packageDefinition.PackageId,
                    out PackageManagerPackageInfo packageInfo))
            {
                DrawKeyValueRow("Package", "Installed");
                DrawKeyValueRow("Version", packageInfo.version);
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

            if (updateStatus.Kind == PackageUpdateStatusKind.CannotDetermine && !string.IsNullOrWhiteSpace(updateStatus.Message))
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

        private void DrawSourcePanel(PackageDefinition packageDefinition)
        {
            DrawPanel("Source", () =>
            {
                PackageChannel selectedChannel = GetSelectedChannel(packageDefinition);
                string selectedUrl = packageDefinition.GetUrl(selectedChannel);

                DrawSelectableValue("Package ID", packageDefinition.PackageId);
                DrawSelectableValue("Selected URL", selectedUrl);
                DrawSelectableValue("Stable URL", packageDefinition.StableUrl);
                DrawSelectableValue("Development URL", packageDefinition.DevelopmentUrl);

                if (_packageDetectionService.TryGetInstalledPackageReference(
                        packageDefinition.PackageId,
                        out string installedReference))
                {
                    DrawSelectableValue("Installed ref", installedReference);
                }
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
                EditorGUI.DrawRect(rowRect, _sampleRowBackgroundColor);
                DrawBorder(rowRect, _separatorColor);
            }

            Rect markerRect = new Rect(rowRect.x + 8f, rowRect.y + 5f, 28f, 18f);
            DrawInlineMarker(markerRect, status.Marker, status.Kind);

            Rect nameRect = new Rect(rowRect.x + 44f, rowRect.y + 5f, rowRect.width - 164f, 18f);
            GUI.Label(
                nameRect,
                new GUIContent(dependencyDefinition.DisplayName, GetPackageTooltip(dependencyDefinition)),
                _rowTitleStyle);

            Rect statusRect = new Rect(rowRect.xMax - 108f, rowRect.y + 5f, 96f, 18f);
            DrawColoredRectLabel(statusRect, new GUIContent(status.Label, status.Label), _rowStatusStyle, GetStatusColor(status.Kind));
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
                            ". Remove those bridge packages first.",
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
                    string buttonLabel = packageDefinition.IsBridge ? "Install Bridge" : "Install";

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
                if (GUILayout.Button("Update", _primaryButtonStyle, GUILayout.Width(104f)))
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
                if (GUILayout.Button("Update", _primaryButtonStyle, GUILayout.ExpandWidth(true)))
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
            DrawSelectableValue("Type", packageDefinition.Category);
            DrawSelectableValue("Git URL", packageDefinition.GetUrl(selectedChannel));
            DrawSelectableValue("Stable URL", packageDefinition.StableUrl);
            DrawSelectableValue("Development URL", packageDefinition.DevelopmentUrl);
            DrawSelectableValue("Selected ref", GetChannelLabel(selectedChannel));

            if (_packageDetectionService.TryGetInstalledPackageReference(
                    packageDefinition.PackageId,
                    out string installedReference))
            {
                DrawSelectableValue("Installed ref", installedReference);
            }

            DrawSelectableValue("Installed rev", updateStatus.InstalledRevision);
            DrawSelectableValue("Latest rev", updateStatus.LatestRevision);
            DrawSelectableValue("Dependencies", packageDefinition.Dependencies.Count == 0
                ? "-"
                : string.Join(", ", packageDefinition.Dependencies.ToArray()));

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

            _advancedFoldouts.TryGetValue(key, out bool expanded);
            bool nextExpanded = EditorGUILayout.Foldout(expanded, "Advanced", true, _foldoutStyle);

            if (nextExpanded != expanded)
            {
                _advancedFoldouts[key] = nextExpanded;
            }

            return nextExpanded;
        }

        private void DrawProgressFooter()
        {
            DrawPanel("Current Operation", () =>
            {
                OperationProgressView operation = GetCurrentOperationProgress();

                if (operation == null)
                {
                    EditorGUILayout.LabelField(new GUIContent("No operation running.", "No operation running."), _mutedMiniLabelStyle);
                    Rect idleRect = GUILayoutUtility.GetRect(1f, 18f, GUILayout.ExpandWidth(true));
                    EditorGUI.ProgressBar(idleRect, 0f, "Idle");
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                    {
                        EditorGUILayout.LabelField(new GUIContent(operation.OperationName, operation.OperationName), _labelStyle);
                        string progressStepText = GetProgressStepText(operation);
                        EditorGUILayout.LabelField(new GUIContent(progressStepText, progressStepText), _mutedMiniLabelStyle);
                    }

                    DrawStatusBadge(operation.IsBusy ? "Running" : "Complete", operation.IsBusy ? VisualStatusKind.Busy : VisualStatusKind.Installed, GUILayout.Width(92f));
                }

                Rect progressRect = GUILayoutUtility.GetRect(1f, 18f, GUILayout.ExpandWidth(true));
                float progress = GetOperationProgress(operation);
                EditorGUI.ProgressBar(progressRect, progress, Mathf.RoundToInt(progress * 100f) + "%");

                DrawProgressMessage(operation.Message, operation.ErrorMessage, operation.FailedSteps);
            }, GUILayout.Height(ProgressAreaHeight), GUILayout.ExpandWidth(true));
        }

        private void DrawLastOperationSummaryPanel()
        {
            DrawPanel("Last Operation Summary", () =>
            {
                string summary = GetLastOperationSummary();
                IReadOnlyList<PackageInstallProgressItem> progressItems = GetLastProgressItems();

                if (string.IsNullOrWhiteSpace(summary) && (progressItems == null || progressItems.Count == 0))
                {
                    EditorGUILayout.LabelField(
                        new GUIContent("No operations have completed yet.", "No operations have completed yet."),
                        _mutedMiniLabelStyle);
                    return;
                }

                VisualStatusKind summaryKind = GetLastSummaryStatusKind(progressItems);
                DrawStatusBadge(GetLastSummaryStatusLabel(summaryKind), summaryKind, GUILayout.Width(118f));

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    EditorGUILayout.LabelField(new GUIContent(summary, summary), _miniLabelStyle);
                }

                DrawProgressItemSummary(progressItems);
            }, GUILayout.Height(SummaryAreaHeight), GUILayout.ExpandWidth(true));
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

        private void DrawProgressMessage(string statusMessage, string errorMessage, int failedSteps)
        {
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                EditorGUILayout.LabelField(new GUIContent(statusMessage, statusMessage), _mutedMiniLabelStyle);
            }

            if (failedSteps > 0 && !string.IsNullOrWhiteSpace(errorMessage))
            {
                DrawInlineHelp(errorMessage, VisualStatusKind.Failed);
            }
        }

        private IReadOnlyList<PackageInstallProgressItem> GetLastProgressItems()
        {
            if (_packageInstallService.HasProgress)
            {
                return _packageInstallService.ProgressItems;
            }

            return Array.Empty<PackageInstallProgressItem>();
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

        private void DrawProgressItemSummary(IReadOnlyList<PackageInstallProgressItem> progressItems)
        {
            if (progressItems == null || progressItems.Count == 0)
            {
                return;
            }

            int drawn = 0;

            foreach (PackageInstallProgressItem item in progressItems)
            {
                if (item == null ||
                    (item.State != PackageInstallProgressItemState.Completed &&
                     item.State != PackageInstallProgressItemState.Failed &&
                     item.State != PackageInstallProgressItemState.Skipped))
                {
                    continue;
                }

                VisualStatusKind kind = GetProgressItemStatusKind(item.State);
                DrawColoredLabel(GetProgressItemStateLabel(item.State) + ": " + item.DisplayName, _mutedMiniLabelStyle, GetStatusColor(kind));
                drawn++;

                if (drawn >= 3)
                {
                    break;
                }
            }
        }

        private static VisualStatusKind GetProgressItemStatusKind(PackageInstallProgressItemState state)
        {
            switch (state)
            {
                case PackageInstallProgressItemState.Completed:
                    return VisualStatusKind.Installed;
                case PackageInstallProgressItemState.Failed:
                    return VisualStatusKind.Failed;
                case PackageInstallProgressItemState.Skipped:
                    return VisualStatusKind.Info;
                case PackageInstallProgressItemState.Active:
                    return VisualStatusKind.Busy;
                default:
                    return VisualStatusKind.NotInstalled;
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
            Rect rect = BeginSurface(_panelStyle, _panelBackgroundColor, _panelBorderColor, options);

            if (!string.IsNullOrWhiteSpace(title))
            {
                EditorGUILayout.LabelField(title, _panelTitleStyle);
                GUILayout.Space(4f);
            }

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
            if (Event.current.type != EventType.Repaint || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            EditorGUI.DrawRect(rect, backgroundColor);
            DrawBorder(rect, borderColor);
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
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
            if (Event.current.type == EventType.Repaint)
            {
                Color color = GetStatusColor(statusKind);
                EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.16f));
                DrawBorder(rect, new Color(color.r, color.g, color.b, 0.65f));
            }

            DrawColoredRectLabel(rect, text, _markerStyle, GetStatusColor(statusKind));
        }

        private void DrawStatusBadge(string text, VisualStatusKind statusKind, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), _badgeStyle, options);

            if (rect.height < 20f)
            {
                rect.height = 20f;
            }

            if (Event.current.type == EventType.Repaint)
            {
                Color color = GetStatusColor(statusKind);
                EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.14f));
                DrawBorder(rect, new Color(color.r, color.g, color.b, 0.70f));
            }

            DrawColoredRectLabel(rect, new GUIContent(text, text), _badgeStyle, GetStatusColor(statusKind));
        }

        private void DrawStatusBadge(Rect rect, string text, VisualStatusKind statusKind, GUIStyle style)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Color color = GetStatusColor(statusKind);
                EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.14f));
                DrawBorder(rect, new Color(color.r, color.g, color.b, 0.70f));
            }

            DrawColoredRectLabel(rect, new GUIContent(text, text), style, GetStatusColor(statusKind));
        }

        private void DrawInlineHelp(string message, VisualStatusKind statusKind)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Rect rect = EditorGUILayout.BeginVertical(_sampleRowStyle, GUILayout.ExpandWidth(true));
            DrawSurface(rect, _sampleRowBackgroundColor, GetStatusColor(statusKind));
            DrawColoredLabel(message, _mutedMiniLabelStyle, GetStatusColor(statusKind));
            EditorGUILayout.EndVertical();
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
                    return new VisualStatus("UP", "Update", VisualStatusKind.UpdateAvailable);
                }

                return new VisualStatus("OK", "Installed", VisualStatusKind.Installed);
            }

            return new VisualStatus("--", "Not Installed", VisualStatusKind.NotInstalled);
        }

        private Color GetStatusColor(VisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case VisualStatusKind.Installed:
                    return _installedColor;
                case VisualStatusKind.UpdateAvailable:
                    return _updateColor;
                case VisualStatusKind.Failed:
                    return _failedColor;
                case VisualStatusKind.Busy:
                    return _busyColor;
                case VisualStatusKind.Info:
                    return _infoColor;
                case VisualStatusKind.Bridge:
                    return _bridgeColor;
                default:
                    return _notInstalledColor;
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
            _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
        }

        private void SetAutoSelectedChannel(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                return;
            }

            _selectedChannels[packageDefinition.PackageId] = channel;
            _autoSelectedChannelPackageIds.Add(packageDefinition.PackageId);
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

            PackageDefinition defaultSelection = PackageRegistryProvider.All.FirstOrDefault(package => !package.IsBridge);

            if (defaultSelection == null)
            {
                defaultSelection = PackageRegistryProvider.BridgePackages.FirstOrDefault();
                _selectionKind = SelectionKind.Bridge;
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

        private void SelectDefinition(PackageDefinition packageDefinition, SelectionKind selectionKind)
        {
            if (packageDefinition == null || IsSelected(packageDefinition, selectionKind))
            {
                return;
            }

            _selectionKind = selectionKind;
            _selectedPackageId = packageDefinition.PackageId;
            _detailsScrollPosition = Vector2.zero;
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

            return string.Empty;
        }

        private bool IsAnyOperationBusy()
        {
            return _packageInstallService.IsBusy ||
                   _packageDetectionService.IsRefreshing ||
                   _packageUpdateCheckService.IsChecking ||
                   _packageSampleImportService.IsBusy;
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

            if (status.Kind == PackageUpdateStatusKind.UpdateAvailable)
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
            _packageUpdateCheckService?.InvalidateAll();
            EnsureValidSelection();

            if (_checkUpdatesAfterDetectionRefresh &&
                !_packageDetectionService.IsRefreshing &&
                !PackageRegistryProvider.IsRemoteRefreshing)
            {
                RunDeferredUpdateCheck();
            }

            Repaint();
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
            _packageSampleDiscoveryService?.ClearCache();
            SynchronizeSelectedChannelsFromInstalledPackages();

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
