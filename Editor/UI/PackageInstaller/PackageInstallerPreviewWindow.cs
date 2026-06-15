using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageInstallerPreviewWindow : EditorWindow
    {
        internal const string CheckOnOpenPreferenceKey =
            "Deucarian.PackageInstaller.Preview.CheckOnOpen";
        internal const string CheckOnStartupPreferenceKey =
            "Deucarian.PackageInstaller.Preview.CheckOnStartup";

        private const string WindowTitle = "Package Installer Preview";
        private const string PreviewMenuPath = "Tools/Deucarian/Package Installer/Open Preview";
        private const float MinWindowWidth = 980f;
        private const float MinWindowHeight = 640f;

        private enum PreviewView
        {
            Browse,
            Installed,
            Updates,
            Collections,
            Settings
        }

        private readonly Dictionary<string, PackageChannel> _selectedChannels =
            new Dictionary<string, PackageChannel>(StringComparer.OrdinalIgnoreCase);

        private PackageInstallService _packageInstallService;
        private PackageDetectionService _packageDetectionService;
        private PackageUpdateCheckService _packageUpdateCheckService;
        private PackageDependencyInstaller _packageDependencyInstaller;

        private VisualElement _root;
        private Image _logoImage;
        private TextField _searchField;
        private Label _packageCountLabel;
        private Label _updatesCountLabel;
        private Label _registrySourceLabel;
        private Label _sidebarStatusLabel;
        private Button _browseButton;
        private Button _installedButton;
        private Button _updatesButton;
        private Button _collectionsButton;
        private Button _settingsButton;
        private Button _refreshButton;
        private Button _checkUpdatesButton;
        private ScrollView _mainScroll;
        private ScrollView _detailScroll;

        private Texture2D _placeholderLogo;
        private Texture2D _placeholderHero;
        private Texture2D _placeholderPackageIcon;
        private PreviewView _activeView = PreviewView.Browse;
        private string _selectedPackageId = string.Empty;
        private string _searchQuery = string.Empty;
        private bool _checkUpdatesAfterDetectionRefresh;

        [MenuItem(PreviewMenuPath)]
        public static void OpenPreview()
        {
            PackageInstallerPreviewWindow window = GetWindow<PackageInstallerPreviewWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(MinWindowWidth, MinWindowHeight);

            EnsureServices();
            PackageRegistryProvider.EnsureLoaded();
            EnsureValidSelection();

            PackageRegistryProvider.RegistryChanged += HandleRegistryChanged;
            _packageInstallService.StateChanged += ScheduleRefreshUi;
            _packageInstallService.QueueCompleted += HandlePackageOperationCompleted;
            _packageDetectionService.StateChanged += ScheduleRefreshUi;
            _packageDetectionService.RefreshCompleted += HandlePackageDetectionRefreshCompleted;
            _packageUpdateCheckService.StateChanged += ScheduleRefreshUi;

            _checkUpdatesAfterDetectionRefresh = EditorPrefs.GetBool(CheckOnOpenPreferenceKey, false);

            if (!_packageInstallService.ResumeSavedOperation())
            {
                _packageDetectionService.Refresh();
            }
        }

        private void OnDisable()
        {
            PackageRegistryProvider.RegistryChanged -= HandleRegistryChanged;

            if (_packageInstallService != null)
            {
                _packageInstallService.StateChanged -= ScheduleRefreshUi;
                _packageInstallService.QueueCompleted -= HandlePackageOperationCompleted;
                _packageInstallService.Dispose();
                _packageInstallService = null;
            }

            if (_packageDetectionService != null)
            {
                _packageDetectionService.StateChanged -= ScheduleRefreshUi;
                _packageDetectionService.RefreshCompleted -= HandlePackageDetectionRefreshCompleted;
                _packageDetectionService.Dispose();
                _packageDetectionService = null;
            }

            if (_packageUpdateCheckService != null)
            {
                _packageUpdateCheckService.StateChanged -= ScheduleRefreshUi;
                _packageUpdateCheckService.Dispose();
                _packageUpdateCheckService = null;
            }

            _packageDependencyInstaller = null;
        }

        private void CreateGUI()
        {
            BuildVisualTree();
            CacheElements();
            WireEvents();
            LoadSharedAssets();
            RefreshUi();
        }

        private void EnsureServices()
        {
            if (_packageInstallService != null)
            {
                return;
            }

            _packageInstallService = new PackageInstallService();
            _packageDetectionService = new PackageDetectionService();
            _packageUpdateCheckService = new PackageUpdateCheckService(_packageDetectionService);
            _packageDependencyInstaller = new PackageDependencyInstaller(
                _packageInstallService,
                _packageDetectionService);
        }

        private void BuildVisualTree()
        {
            rootVisualElement.Clear();
            PackageInstallerPreviewResources.TryAddSharedStyleSheet(rootVisualElement);

            StyleSheet previewStyleSheet = PackageInstallerPreviewResources.LoadPreviewStyleSheet();
            if (previewStyleSheet != null)
            {
                rootVisualElement.styleSheets.Add(previewStyleSheet);
            }

            VisualTreeAsset tree = PackageInstallerPreviewResources.LoadVisualTree();
            if (tree != null)
            {
                tree.CloneTree(rootVisualElement);
                return;
            }

            BuildFallbackTree(rootVisualElement);
        }

        private static void BuildFallbackTree(VisualElement parent)
        {
            VisualElement root = new VisualElement { name = "dpi-root" };
            root.AddToClassList("deucarian-editor");
            root.AddToClassList("deucarian-window");
            root.AddToClassList("dpi-root");
            parent.Add(root);

            VisualElement header = new VisualElement { name = "dpi-header" };
            header.AddToClassList("deucarian-header");
            header.AddToClassList("dpi-header");
            root.Add(header);

            header.Add(new Image { name = "dpi-logo" });

            VisualElement titleBlock = new VisualElement();
            titleBlock.AddToClassList("dpi-title-block");
            titleBlock.Add(new Label("Deucarian Package Installer") { name = "dpi-window-title" });
            titleBlock.Add(new Label("Discover. Install. Elevate.") { name = "dpi-window-tagline" });
            header.Add(titleBlock);

            TextField search = new TextField("Search") { name = "dpi-search" };
            header.Add(search);

            VisualElement actions = new VisualElement();
            actions.Add(new Label("0 packages") { name = "dpi-package-count" });
            actions.Add(new Label("0 updates") { name = "dpi-updates-count" });
            actions.Add(new Label("Using bundled registry") { name = "dpi-registry-source" });
            actions.Add(new Button { name = "dpi-refresh-button", text = "Refresh" });
            actions.Add(new Button { name = "dpi-check-updates-button", text = "Check Updates" });
            header.Add(actions);

            VisualElement body = new VisualElement { name = "dpi-body" };
            body.AddToClassList("dpi-body");
            root.Add(body);

            VisualElement sidebar = new VisualElement { name = "dpi-sidebar" };
            sidebar.AddToClassList("deucarian-sidebar");
            body.Add(sidebar);

            sidebar.Add(new Button { name = "dpi-nav-browse", text = "Browse" });
            sidebar.Add(new Button { name = "dpi-nav-installed", text = "Installed" });
            sidebar.Add(new Button { name = "dpi-nav-updates", text = "Updates" });
            sidebar.Add(new Button { name = "dpi-nav-collections", text = "Collections" });
            sidebar.Add(new Button { name = "dpi-nav-settings", text = "Settings" });
            sidebar.Add(new Label("All systems operational") { name = "dpi-sidebar-status" });

            VisualElement content = new VisualElement { name = "dpi-content" };
            content.AddToClassList("dpi-content");
            body.Add(content);

            content.Add(new ScrollView { name = "dpi-main-scroll" });
            content.Add(new ScrollView { name = "dpi-detail-scroll" });
        }

        private void CacheElements()
        {
            _root = rootVisualElement.Q<VisualElement>("dpi-root");
            _logoImage = rootVisualElement.Q<Image>("dpi-logo");
            _searchField = rootVisualElement.Q<TextField>("dpi-search");
            _packageCountLabel = rootVisualElement.Q<Label>("dpi-package-count");
            _updatesCountLabel = rootVisualElement.Q<Label>("dpi-updates-count");
            _registrySourceLabel = rootVisualElement.Q<Label>("dpi-registry-source");
            _sidebarStatusLabel = rootVisualElement.Q<Label>("dpi-sidebar-status");
            _browseButton = rootVisualElement.Q<Button>("dpi-nav-browse");
            _installedButton = rootVisualElement.Q<Button>("dpi-nav-installed");
            _updatesButton = rootVisualElement.Q<Button>("dpi-nav-updates");
            _collectionsButton = rootVisualElement.Q<Button>("dpi-nav-collections");
            _settingsButton = rootVisualElement.Q<Button>("dpi-nav-settings");
            _refreshButton = rootVisualElement.Q<Button>("dpi-refresh-button");
            _checkUpdatesButton = rootVisualElement.Q<Button>("dpi-check-updates-button");
            _mainScroll = rootVisualElement.Q<ScrollView>("dpi-main-scroll");
            _detailScroll = rootVisualElement.Q<ScrollView>("dpi-detail-scroll");
        }

        private void WireEvents()
        {
            RegisterNavigation(_browseButton, PreviewView.Browse);
            RegisterNavigation(_installedButton, PreviewView.Installed);
            RegisterNavigation(_updatesButton, PreviewView.Updates);
            RegisterNavigation(_collectionsButton, PreviewView.Collections);
            RegisterNavigation(_settingsButton, PreviewView.Settings);

            if (_refreshButton != null)
            {
                _refreshButton.clicked += RefreshPackages;
            }

            if (_checkUpdatesButton != null)
            {
                _checkUpdatesButton.clicked += CheckForUpdates;
            }

            if (_searchField != null)
            {
                _searchField.SetValueWithoutNotify(_searchQuery);
                _searchField.RegisterValueChangedCallback(evt =>
                {
                    _searchQuery = evt.newValue ?? string.Empty;
                    EnsureValidSelection();
                    RefreshUi();
                });
            }
        }

        private void RegisterNavigation(Button button, PreviewView view)
        {
            if (button == null)
            {
                return;
            }

            button.clicked += () =>
            {
                _activeView = view;
                EnsureValidSelection();
                RefreshUi();
            };
        }

        private void LoadSharedAssets()
        {
            _placeholderLogo = PackageInstallerPreviewResources.LoadPlaceholderLogo();
            _placeholderHero = PackageInstallerPreviewResources.LoadPackageInstallerPlaceholderHero();
            _placeholderPackageIcon = PackageInstallerPreviewResources.LoadPackagePlaceholderIcon();

            if (_logoImage != null)
            {
                _logoImage.image = _placeholderLogo;
                _logoImage.scaleMode = ScaleMode.ScaleToFit;
            }
        }

        private void RefreshUi()
        {
            if (_mainScroll == null || _detailScroll == null)
            {
                Repaint();
                return;
            }

            EnsureValidSelection();
            UpdateHeader();
            UpdateNavigation();

            _mainScroll.Clear();

            switch (_activeView)
            {
                case PreviewView.Installed:
                    DrawInstalledView();
                    break;
                case PreviewView.Updates:
                    DrawUpdatesView();
                    break;
                case PreviewView.Collections:
                    DrawCollectionsView();
                    break;
                case PreviewView.Settings:
                    DrawSettingsView();
                    break;
                default:
                    DrawBrowseView();
                    break;
            }

            DrawDetailsView();
            Repaint();
        }

        private void UpdateHeader()
        {
            IReadOnlyList<PackageDefinition> packages = PackageRegistryProvider.All;
            PackageDefinition[] updates = GetPackagesWithUpdates();

            SetLabel(_packageCountLabel, packages.Count + " packages");
            SetLabel(_updatesCountLabel, updates.Length + " updates");

            string registryText = PackageRegistryProvider.IsRemoteRefreshing
                ? "Refreshing registry"
                : PackageRegistryProvider.StatusMessage;
            SetLabel(_registrySourceLabel, registryText);
            SetLabel(_sidebarStatusLabel, GetSidebarStatusText());

            bool busy = IsAnyOperationBusy();
            _refreshButton?.SetEnabled(!busy);
            _checkUpdatesButton?.SetEnabled(!busy);
        }

        private void UpdateNavigation()
        {
            SetNavigationActive(_browseButton, _activeView == PreviewView.Browse);
            SetNavigationActive(_installedButton, _activeView == PreviewView.Installed);
            SetNavigationActive(_updatesButton, _activeView == PreviewView.Updates);
            SetNavigationActive(_collectionsButton, _activeView == PreviewView.Collections);
            SetNavigationActive(_settingsButton, _activeView == PreviewView.Settings);
        }

        private static void SetNavigationActive(VisualElement element, bool active)
        {
            if (element == null)
            {
                return;
            }

            if (active)
            {
                element.AddToClassList("deucarian-sidebar__button--active");
            }
            else
            {
                element.RemoveFromClassList("deucarian-sidebar__button--active");
            }
        }

        private void DrawBrowseView()
        {
            AddHero();

            PackageDefinition[] visiblePackages = GetVisiblePackages().ToArray();
            PackageDefinition[] featured = visiblePackages
                .Where(package => !package.IsBridge)
                .Take(4)
                .ToArray();

            AddSection("Featured packages", section =>
            {
                VisualElement grid = new VisualElement();
                grid.AddToClassList("dpi-card-grid");
                section.Add(grid);

                foreach (PackageDefinition packageDefinition in featured)
                {
                    grid.Add(CreatePackageCard(packageDefinition));
                }

                if (featured.Length == 0)
                {
                    section.Add(CreateMutedLabel("No packages match the current search."));
                }
            });

            AddSection("Recently updated", section =>
            {
                PackageDefinition[] recent = visiblePackages
                    .Where(package => !package.IsBridge)
                    .Skip(4)
                    .Take(5)
                    .ToArray();

                if (recent.Length == 0)
                {
                    recent = visiblePackages
                        .Where(package => !package.IsBridge)
                        .Take(5)
                        .ToArray();
                }

                foreach (PackageDefinition packageDefinition in recent)
                {
                    section.Add(CreatePackageRow(packageDefinition, includeActions: false));
                }
            });

            AddSection("Categories and suites", section =>
            {
                foreach (string category in PackageRegistryProvider.Categories)
                {
                    PackageDefinition[] categoryPackages = GetVisiblePackages()
                        .Where(package => string.Equals(
                            package.Category,
                            category,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (categoryPackages.Length == 0)
                    {
                        continue;
                    }

                    section.Add(CreateKeyValueRow(category, categoryPackages.Length + " packages"));
                }
            });
        }

        private void DrawInstalledView()
        {
            AddSection("Installed packages", section =>
            {
                PackageDefinition[] installed = GetVisiblePackages()
                    .Where(package => _packageDetectionService.IsInstalled(package.PackageId))
                    .ToArray();

                if (installed.Length == 0)
                {
                    section.Add(CreateMutedLabel("No registry packages are currently installed."));
                    return;
                }

                foreach (PackageDefinition packageDefinition in installed)
                {
                    section.Add(CreatePackageRow(packageDefinition, includeActions: true));
                }
            });
        }

        private void DrawUpdatesView()
        {
            PackageDefinition[] updates = GetPackagesWithUpdates()
                .Where(MatchesSearch)
                .ToArray();

            AddSection("Updates available", section =>
            {
                Button updateAllButton = CreateButton("Update All", UpdateAllPackages, true);
                updateAllButton.SetEnabled(updates.Length > 0 && !IsAnyOperationBusy());
                section.Add(updateAllButton);

                if (updates.Length == 0)
                {
                    section.Add(CreateMutedLabel("No checked packages currently report an available update."));
                    return;
                }

                foreach (PackageDefinition packageDefinition in updates)
                {
                    section.Add(CreatePackageRow(packageDefinition, includeActions: true));
                }
            });
        }

        private void DrawCollectionsView()
        {
            AddSection("Suites", section =>
            {
                PackageDefinition[] suites = GetVisiblePackages()
                    .Where(package => string.Equals(
                        package.Category,
                        "Suites",
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (suites.Length == 0)
                {
                    section.Add(CreateMutedLabel("No suite packages are present in the current registry."));
                    return;
                }

                foreach (PackageDefinition suite in suites)
                {
                    section.Add(CreatePackageRow(suite, includeActions: true));
                }
            });

            AddSection("Bridge groups", section =>
            {
                PackageDefinition[] bridgePackages = GetVisiblePackages()
                    .Where(package => package.IsBridge)
                    .ToArray();

                if (bridgePackages.Length == 0)
                {
                    section.Add(CreateMutedLabel("No bridge packages match the current search."));
                    return;
                }

                foreach (PackageDefinition bridgePackage in bridgePackages)
                {
                    section.Add(CreatePackageRow(bridgePackage, includeActions: true));
                }
            });
        }

        private void DrawSettingsView()
        {
            AddSection("Update checks", section =>
            {
                Toggle checkOnOpen = new Toggle("Check on open")
                {
                    value = EditorPrefs.GetBool(CheckOnOpenPreferenceKey, false)
                };
                checkOnOpen.RegisterValueChangedCallback(evt =>
                    EditorPrefs.SetBool(CheckOnOpenPreferenceKey, evt.newValue));
                section.Add(checkOnOpen);

                Toggle checkOnStartup = new Toggle("Check on startup")
                {
                    value = EditorPrefs.GetBool(CheckOnStartupPreferenceKey, false)
                };
                checkOnStartup.RegisterValueChangedCallback(evt =>
                    EditorPrefs.SetBool(CheckOnStartupPreferenceKey, evt.newValue));
                section.Add(checkOnStartup);
            });

            AddSection("Registry source", section =>
            {
                PackageRegistryLoadResult result = PackageRegistryProvider.CurrentLoadResult;
                section.Add(CreateKeyValueRow("Source", result != null ? result.Source.ToString() : "Unknown"));
                section.Add(CreateKeyValueRow("Status", result != null ? result.StatusMessage : "Unknown"));

                if (result != null && !string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    section.Add(CreateKeyValueRow("Message", result.ErrorMessage));
                }
            });

            AddSection("Advanced diagnostics", section =>
            {
                section.Add(CreateKeyValueRow("Detection", _packageDetectionService.IsRefreshing ? "Refreshing" : "Idle"));
                section.Add(CreateKeyValueRow("Updates", _packageUpdateCheckService.IsChecking ? "Checking" : "Idle"));
                section.Add(CreateKeyValueRow("Operations", _packageInstallService.IsBusy ? "Running" : "Idle"));
                section.Add(CreateKeyValueRow("UI Toolkit UXML", PackageInstallerPreviewResources.UxmlPath));
                section.Add(CreateKeyValueRow("UI Toolkit USS", PackageInstallerPreviewResources.UssPath));
            });
        }

        private void DrawDetailsView()
        {
            _detailScroll.Clear();

            PackageDefinition packageDefinition = GetSelectedPackage();
            if (packageDefinition == null)
            {
                _detailScroll.Add(CreateLabel("No package selected", "dpi-detail-title"));
                _detailScroll.Add(CreateMutedLabel("Select a package to inspect status, dependencies, and diagnostics."));
                return;
            }

            _detailScroll.Add(CreateLabel(packageDefinition.DisplayName, "dpi-detail-title"));
            _detailScroll.Add(CreateLabel(packageDefinition.Description, "dpi-detail-subtitle"));

            AddDetailSection("Overview", section =>
            {
                section.Add(CreateKeyValueRow("Status", GetPackageStatusLabel(packageDefinition)));
                section.Add(CreateKeyValueRow("Version", GetPackageVersionText(packageDefinition)));
                section.Add(CreateKeyValueRow("Channel", GetChannelLabel(GetSelectedChannel(packageDefinition))));
                section.Add(CreateChannelField(packageDefinition));
                section.Add(CreateActionRow(packageDefinition));
            });

            AddDetailSection("Dependencies", section =>
            {
                if (packageDefinition.Dependencies.Count == 0)
                {
                    section.Add(CreateMutedLabel("No package dependencies declared."));
                    return;
                }

                foreach (string dependencyId in packageDefinition.Dependencies)
                {
                    string label = PackageRegistryProvider.TryGetPackage(
                        dependencyId,
                        out PackageDefinition dependencyDefinition)
                        ? dependencyDefinition.DisplayName
                        : dependencyId;
                    string status = _packageDetectionService.IsInstalled(dependencyId) ? "Installed" : "Missing";
                    section.Add(CreateKeyValueRow(label, status));
                }
            });

            AddDetailSection("Links", section =>
            {
                section.Add(CreateMutedLabel("Docs and changelog links are not present in the registry metadata yet."));
            });

            Foldout advanced = new Foldout
            {
                text = "Advanced",
                value = false
            };
            advanced.AddToClassList("dpi-foldout");
            advanced.Add(CreateKeyValueRow("Package ID", packageDefinition.PackageId));
            advanced.Add(CreateKeyValueRow("Stable URL", packageDefinition.StableUrl));
            advanced.Add(CreateKeyValueRow("Stable ref", GetReferenceName(packageDefinition.StableUrl)));
            advanced.Add(CreateKeyValueRow("Development URL", packageDefinition.DevelopmentUrl));
            advanced.Add(CreateKeyValueRow("Development ref", GetReferenceName(packageDefinition.DevelopmentUrl)));
            advanced.Add(CreateKeyValueRow("Registry source", PackageRegistryProvider.StatusMessage));
            advanced.Add(CreateKeyValueRow("Detection", GetDetectionDetails(packageDefinition)));
            advanced.Add(CreateKeyValueRow(
                "Update status",
                GetUpdateStatusText(_packageUpdateCheckService.GetStatus(
                    packageDefinition,
                    GetSelectedChannel(packageDefinition)))));
            _detailScroll.Add(advanced);
        }

        private void AddHero()
        {
            VisualElement hero = new VisualElement();
            hero.AddToClassList("dpi-hero");

            Image image = new Image
            {
                image = _placeholderHero,
                scaleMode = ScaleMode.ScaleAndCrop
            };
            image.AddToClassList("dpi-hero-image");
            hero.Add(image);

            VisualElement copy = new VisualElement();
            copy.AddToClassList("dpi-hero-copy");
            copy.Add(CreateLabel("Deucarian Package Installer", "dpi-hero-title"));
            copy.Add(CreateLabel("Discover. Install. Elevate.", "dpi-hero-subtitle"));
            hero.Add(copy);

            _mainScroll.Add(hero);
        }

        private void AddSection(string title, Action<VisualElement> populate)
        {
            VisualElement section = new VisualElement();
            section.AddToClassList("deucarian-section");
            section.Add(CreateLabel(title, "deucarian-section__title"));
            populate?.Invoke(section);
            _mainScroll.Add(section);
        }

        private void AddDetailSection(string title, Action<VisualElement> populate)
        {
            VisualElement section = new VisualElement();
            section.AddToClassList("deucarian-section");
            section.Add(CreateLabel(title, "deucarian-section__title"));
            populate?.Invoke(section);
            _detailScroll.Add(section);
        }

        private VisualElement CreatePackageCard(PackageDefinition packageDefinition)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("deucarian-card");
            card.AddToClassList("dpi-package-card");
            if (IsSelected(packageDefinition))
            {
                card.AddToClassList("dpi-package-card--selected");
            }

            card.RegisterCallback<ClickEvent>(_ => SelectPackage(packageDefinition));

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-card-header");
            header.Add(CreatePackageIcon());
            header.Add(CreateLabel(packageDefinition.DisplayName, "dpi-card-title"));
            header.Add(CreateBadge(GetPackageStatusLabel(packageDefinition), GetPackageBadgeClass(packageDefinition)));
            card.Add(header);
            card.Add(CreateLabel(packageDefinition.Description, "dpi-card-description"));

            return card;
        }

        private VisualElement CreatePackageRow(PackageDefinition packageDefinition, bool includeActions)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("deucarian-card");
            row.AddToClassList("dpi-row");
            if (IsSelected(packageDefinition))
            {
                row.AddToClassList("dpi-package-card--selected");
            }

            row.RegisterCallback<ClickEvent>(_ => SelectPackage(packageDefinition));

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-row-header");
            header.Add(CreatePackageIcon());

            VisualElement body = new VisualElement();
            body.AddToClassList("dpi-row-body");
            body.Add(CreateLabel(packageDefinition.DisplayName, "dpi-row-title"));
            body.Add(CreateLabel(packageDefinition.Description, "dpi-row-description"));
            header.Add(body);
            header.Add(CreateBadge(GetPackageStatusLabel(packageDefinition), GetPackageBadgeClass(packageDefinition)));
            row.Add(header);

            if (includeActions)
            {
                row.Add(CreateActionRow(packageDefinition));
            }

            return row;
        }

        private VisualElement CreateActionRow(PackageDefinition packageDefinition)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dpi-detail-actions");

            bool installed = _packageDetectionService.IsInstalled(packageDefinition.PackageId);
            bool busy = IsAnyOperationBusy() ||
                        _packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId);
            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));
            PackageDefinition[] installedDependents =
                _packageDependencyInstaller.GetInstalledDependents(packageDefinition);

            Button installButton = CreateButton(installed ? "Reinstall" : "Install", () =>
            {
                _packageDependencyInstaller.InstallWithDependencies(packageDefinition, GetSelectedChannel);
                _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
                RefreshUi();
            }, true);
            installButton.SetEnabled(!busy && packageDefinition.HasPackageReference);
            row.Add(installButton);

            Button updateButton = CreateButton("Update", () =>
            {
                _packageInstallService.Install(
                    packageDefinition,
                    GetSelectedChannel(packageDefinition),
                    "Update " + packageDefinition.DisplayName);
                _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
                RefreshUi();
            }, false);
            updateButton.SetEnabled(!busy && updateStatus.IsUpdateAvailable);
            row.Add(updateButton);

            Button removeButton = CreateButton("Remove", () =>
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
                RefreshUi();
            }, false);
            removeButton.SetEnabled(!busy && installed && installedDependents.Length == 0);
            row.Add(removeButton);

            return row;
        }

        private VisualElement CreateChannelField(PackageDefinition packageDefinition)
        {
            DropdownField channelField = new DropdownField("Channel");
            channelField.choices = GetChannelOptions(packageDefinition)
                .Select(GetChannelLabel)
                .ToList();
            channelField.value = GetChannelLabel(GetSelectedChannel(packageDefinition));
            channelField.RegisterValueChangedCallback(evt =>
            {
                SetSelectedChannel(packageDefinition, ParseChannelLabel(evt.newValue));
                RefreshUi();
            });
            return channelField;
        }

        private Image CreatePackageIcon()
        {
            Image image = new Image
            {
                image = _placeholderPackageIcon,
                scaleMode = ScaleMode.ScaleToFit
            };
            image.AddToClassList("dpi-package-icon");
            return image;
        }

        private static Label CreateLabel(string text, string className = null)
        {
            Label label = new Label(text ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(className))
            {
                label.AddToClassList(className);
            }

            return label;
        }

        private static Label CreateMutedLabel(string text)
        {
            Label label = CreateLabel(text, "deucarian-muted");
            return label;
        }

        private static Label CreateBadge(string text, string modifierClass)
        {
            Label label = CreateLabel(text, "deucarian-badge");
            if (!string.IsNullOrWhiteSpace(modifierClass))
            {
                label.AddToClassList(modifierClass);
            }

            return label;
        }

        private static VisualElement CreateKeyValueRow(string key, string value)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dpi-key-row");
            row.Add(CreateLabel(key, "dpi-key"));
            row.Add(CreateLabel(string.IsNullOrWhiteSpace(value) ? "Not available" : value, "dpi-value"));
            return row;
        }

        private static Button CreateButton(string text, Action action, bool primary)
        {
            Button button = new Button(action)
            {
                text = text ?? string.Empty
            };
            button.AddToClassList("deucarian-button");
            if (primary)
            {
                button.AddToClassList("deucarian-button--primary");
            }

            return button;
        }

        private IEnumerable<PackageDefinition> GetVisiblePackages()
        {
            return PackageRegistryProvider.All
                .Where(MatchesSearch)
                .OrderBy(package => GetCategorySortIndex(package.Category))
                .ThenBy(package => package.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        private bool MatchesSearch(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                return true;
            }

            string query = _searchQuery.Trim();
            return Contains(packageDefinition.DisplayName, query) ||
                   Contains(packageDefinition.PackageId, query) ||
                   Contains(packageDefinition.Description, query) ||
                   Contains(packageDefinition.Category, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SelectPackage(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return;
            }

            _selectedPackageId = packageDefinition.PackageId;
            RefreshUi();
        }

        private bool IsSelected(PackageDefinition packageDefinition)
        {
            return packageDefinition != null &&
                   string.Equals(
                       _selectedPackageId,
                       packageDefinition.PackageId,
                       StringComparison.OrdinalIgnoreCase);
        }

        private PackageDefinition GetSelectedPackage()
        {
            if (string.IsNullOrWhiteSpace(_selectedPackageId))
            {
                return null;
            }

            return PackageRegistryProvider.All.FirstOrDefault(package =>
                string.Equals(package.PackageId, _selectedPackageId, StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureValidSelection()
        {
            if (GetSelectedPackage() != null)
            {
                return;
            }

            PackageDefinition nextSelection = GetVisiblePackages().FirstOrDefault(package => !package.IsBridge) ??
                                              GetVisiblePackages().FirstOrDefault();
            _selectedPackageId = nextSelection != null ? nextSelection.PackageId : string.Empty;
        }

        private PackageDefinition[] GetPackagesWithUpdates()
        {
            return _packageUpdateCheckService != null
                ? _packageUpdateCheckService
                    .GetPackagesWithUpdates(PackageRegistryProvider.All, GetSelectedChannel)
                    .ToArray()
                : Array.Empty<PackageDefinition>();
        }

        private PackageChannel GetSelectedChannel(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return PackageChannel.Stable;
            }

            return _selectedChannels.TryGetValue(packageDefinition.PackageId, out PackageChannel channel)
                ? channel
                : PackageChannel.Stable;
        }

        private void SetSelectedChannel(PackageDefinition packageDefinition, PackageChannel channel)
        {
            if (packageDefinition == null)
            {
                return;
            }

            _selectedChannels[packageDefinition.PackageId] = channel;
            _packageUpdateCheckService?.Invalidate(packageDefinition.PackageId);
        }

        private IEnumerable<PackageChannel> GetChannelOptions(PackageDefinition packageDefinition)
        {
            yield return PackageChannel.Stable;

            if (packageDefinition != null && packageDefinition.HasDevelopmentUrl)
            {
                yield return PackageChannel.Development;
            }

            if (GetSelectedChannel(packageDefinition) == PackageChannel.Custom)
            {
                yield return PackageChannel.Custom;
            }
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

        private static PackageChannel ParseChannelLabel(string label)
        {
            if (string.Equals(label, "Development", StringComparison.OrdinalIgnoreCase))
            {
                return PackageChannel.Development;
            }

            if (string.Equals(label, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                return PackageChannel.Custom;
            }

            return PackageChannel.Stable;
        }

        private void SynchronizeSelectedChannelsFromInstalledPackages()
        {
            foreach (PackageDefinition packageDefinition in PackageRegistryProvider.All)
            {
                if (_packageDetectionService.TryGetInstalledPackageChannel(
                        packageDefinition,
                        out PackageChannel installedChannel,
                        out _))
                {
                    _selectedChannels[packageDefinition.PackageId] = installedChannel;
                }
            }
        }

        private void RefreshPackages()
        {
            _packageDetectionService.Refresh();
            _packageUpdateCheckService.InvalidateAll();
            RefreshUi();
        }

        private void CheckForUpdates()
        {
            _packageUpdateCheckService.CheckForUpdates(PackageRegistryProvider.All, GetSelectedChannel);
            RefreshUi();
        }

        private void UpdateAllPackages()
        {
            _packageInstallService.InstallMany(
                GetPackagesWithUpdates(),
                GetSelectedChannel,
                "Update All Installed Packages");
            _packageUpdateCheckService.InvalidateAll();
            RefreshUi();
        }

        private bool IsAnyOperationBusy()
        {
            return _packageInstallService != null && _packageInstallService.IsBusy ||
                   _packageDetectionService != null && _packageDetectionService.IsRefreshing ||
                   _packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking;
        }

        private string GetSidebarStatusText()
        {
            if (_packageInstallService != null && _packageInstallService.IsBusy)
            {
                return "Package operation running";
            }

            if (_packageDetectionService != null && _packageDetectionService.IsRefreshing)
            {
                return "Refreshing installed packages";
            }

            if (_packageUpdateCheckService != null && _packageUpdateCheckService.IsChecking)
            {
                return "Checking package updates";
            }

            PackageRegistryLoadResult result = PackageRegistryProvider.CurrentLoadResult;
            if (result != null && !result.IsValid)
            {
                return "Registry errors detected";
            }

            if (result != null && result.Source == PackageRegistrySource.RemoteFailedUsingBundled)
            {
                return "Registry fallback active";
            }

            return "All systems operational";
        }

        private string GetPackageStatusLabel(PackageDefinition packageDefinition)
        {
            if (_packageInstallService != null &&
                _packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId))
            {
                return "Running";
            }

            bool installed = _packageDetectionService.IsInstalled(packageDefinition.PackageId);
            if (!installed)
            {
                return "Not installed";
            }

            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

            if (updateStatus.IsUpdateAvailable)
            {
                return "Update";
            }

            if (updateStatus.Kind == PackageUpdateStatusKind.UpToDate)
            {
                return "Current";
            }

            if (updateStatus.Kind == PackageUpdateStatusKind.Failed)
            {
                return "Check failed";
            }

            return "Installed";
        }

        private string GetPackageBadgeClass(PackageDefinition packageDefinition)
        {
            string label = GetPackageStatusLabel(packageDefinition);
            if (string.Equals(label, "Update", StringComparison.OrdinalIgnoreCase))
            {
                return "deucarian-badge--warning";
            }

            if (string.Equals(label, "Current", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, "Installed", StringComparison.OrdinalIgnoreCase))
            {
                return "deucarian-badge--success";
            }

            if (string.Equals(label, "Check failed", StringComparison.OrdinalIgnoreCase))
            {
                return "deucarian-badge--error";
            }

            return string.Empty;
        }

        private string GetPackageVersionText(PackageDefinition packageDefinition)
        {
            if (_packageDetectionService.TryGetInstalledPackage(
                    packageDefinition.PackageId,
                    out PackageManagerPackageInfo packageInfo) &&
                packageInfo != null &&
                !string.IsNullOrWhiteSpace(packageInfo.version))
            {
                return "Installed " + packageInfo.version;
            }

            return packageDefinition.HasDisplayVersion
                ? packageDefinition.DisplayVersion
                : "Catalog version not provided";
        }

        private string GetDetectionDetails(PackageDefinition packageDefinition)
        {
            if (_packageDetectionService.TryGetInstalledPackageReference(
                    packageDefinition.PackageId,
                    out string installedReference))
            {
                return installedReference;
            }

            return _packageDetectionService.IsInstalled(packageDefinition.PackageId)
                ? "Installed; reference not available"
                : "Not installed";
        }

        private static string GetUpdateStatusText(PackageUpdateStatus status)
        {
            if (status == null)
            {
                return "Unknown";
            }

            if (status.Kind == PackageUpdateStatusKind.UpdateAvailable)
            {
                return status.Label + " (" + status.ShortInstalledRevision + " -> " + status.ShortLatestRevision + ")";
            }

            if (status.Kind == PackageUpdateStatusKind.UpToDate && !string.IsNullOrWhiteSpace(status.ShortLatestRevision))
            {
                return status.Label + " (" + status.ShortLatestRevision + ")";
            }

            if (status.Kind == PackageUpdateStatusKind.Failed && !string.IsNullOrWhiteSpace(status.Message))
            {
                return status.Label + ": " + status.Message;
            }

            return status.Label;
        }

        private static string GetReferenceName(string packageReference)
        {
            if (string.IsNullOrWhiteSpace(packageReference))
            {
                return string.Empty;
            }

            int hashIndex = packageReference.LastIndexOf('#');
            return hashIndex >= 0 && hashIndex < packageReference.Length - 1
                ? packageReference.Substring(hashIndex + 1).Trim()
                : string.Empty;
        }

        private static int GetCategorySortIndex(string category)
        {
            if (string.Equals(category, "Core", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(category, "UI", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(category, "World", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(category, "Bridge", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (string.Equals(category, "Suites", StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            return 5;
        }

        private static void SetLabel(Label label, string text)
        {
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }

        private void ScheduleRefreshUi()
        {
            RefreshUi();
        }

        private void HandleRegistryChanged()
        {
            _packageUpdateCheckService?.InvalidateAll();
            EnsureValidSelection();
            RefreshUi();
        }

        private void HandlePackageOperationCompleted()
        {
            if (_packageUpdateCheckService != null && _packageUpdateCheckService.HasStatuses)
            {
                _checkUpdatesAfterDetectionRefresh = true;
            }

            _packageDetectionService?.Refresh();
        }

        private void HandlePackageDetectionRefreshCompleted()
        {
            SynchronizeSelectedChannelsFromInstalledPackages();

            if (_checkUpdatesAfterDetectionRefresh)
            {
                _checkUpdatesAfterDetectionRefresh = false;
                _packageUpdateCheckService.CheckForUpdates(PackageRegistryProvider.All, GetSelectedChannel);
            }

            RefreshUi();
        }
    }

    [InitializeOnLoad]
    internal static class PackageInstallerPreviewStartupUpdateCheck
    {
        private static PackageDetectionService _detectionService;
        private static PackageUpdateCheckService _updateCheckService;

        static PackageInstallerPreviewStartupUpdateCheck()
        {
            EditorApplication.delayCall += StartIfEnabled;
        }

        private static void StartIfEnabled()
        {
            EditorApplication.delayCall -= StartIfEnabled;

            if (!EditorPrefs.GetBool(PackageInstallerPreviewWindow.CheckOnStartupPreferenceKey, false))
            {
                return;
            }

            PackageRegistryProvider.EnsureLoaded();
            _detectionService = new PackageDetectionService();
            _updateCheckService = new PackageUpdateCheckService(_detectionService);
            _detectionService.RefreshCompleted += HandleDetectionRefreshCompleted;
            _detectionService.Refresh();
        }

        private static void HandleDetectionRefreshCompleted()
        {
            if (_detectionService == null || _updateCheckService == null)
            {
                DisposeServices();
                return;
            }

            _detectionService.RefreshCompleted -= HandleDetectionRefreshCompleted;
            _updateCheckService.StateChanged += DisposeWhenUpdateCheckCompletes;
            _updateCheckService.CheckForUpdates(PackageRegistryProvider.All, _ => PackageChannel.Stable);
            DisposeWhenUpdateCheckCompletes();
        }

        private static void DisposeWhenUpdateCheckCompletes()
        {
            if (_updateCheckService != null && _updateCheckService.IsChecking)
            {
                return;
            }

            DisposeServices();
        }

        private static void DisposeServices()
        {
            if (_updateCheckService != null)
            {
                _updateCheckService.StateChanged -= DisposeWhenUpdateCheckCompletes;
                _updateCheckService.Dispose();
                _updateCheckService = null;
            }

            if (_detectionService != null)
            {
                _detectionService.RefreshCompleted -= HandleDetectionRefreshCompleted;
                _detectionService.Dispose();
                _detectionService = null;
            }
        }
    }
}
