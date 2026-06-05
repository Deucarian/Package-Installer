using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace JorisHoef.PackageInstaller.Editor
{
    internal sealed class PackageInstallerWindow : EditorWindow
    {
        private const string WindowTitle = "Package Installer";
        private const float MinWindowWidth = 760f;
        private const float MinWindowHeight = 480f;
        private const float SidebarWidth = 240f;
        private const string ChannelPreferencePrefix = "JorisHoef.PackageInstaller.SelectedChannel.";

        private enum SelectionKind
        {
            Package,
            Bridge
        }

        private PackageInstallService _packageInstallService;
        private PackageDetectionService _packageDetectionService;
        private PackageUpdateCheckService _packageUpdateCheckService;
        private PackageSampleImportService _packageSampleImportService;
        private PackageDependencyInstaller _packageDependencyInstaller;

        private readonly Dictionary<string, PackageChannel> _selectedChannels =
            new Dictionary<string, PackageChannel>();
        private readonly Dictionary<string, bool> _advancedFoldouts =
            new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _sampleFoldouts =
            new Dictionary<string, bool>();

        private Vector2 _sidebarScrollPosition;
        private Vector2 _detailsScrollPosition;
        private SelectionKind _selectionKind = SelectionKind.Package;
        private string _selectedPackageId = string.Empty;
        private bool _checkUpdatesAfterDetectionRefresh;

        [MenuItem("Tools/JorisHoef/Package Installer")]
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
            _packageDependencyInstaller = new PackageDependencyInstaller(
                _packageInstallService,
                _packageDetectionService);
            EnsureValidSelection();

            _packageInstallService.StateChanged += Repaint;
            _packageInstallService.QueueCompleted += HandlePackageOperationCompleted;
            _packageDetectionService.StateChanged += Repaint;
            _packageDetectionService.RefreshCompleted += HandlePackageDetectionRefreshCompleted;
            _packageUpdateCheckService.StateChanged += Repaint;
            _packageSampleImportService.StateChanged += Repaint;

            if (!_packageInstallService.ResumeSavedOperation())
            {
                _packageDetectionService.Refresh();
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
        }

        private void OnGUI()
        {
            EnsureValidSelection();
            DrawHeader();

            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                DrawSidebar();
                DrawDetailsPane();
            }

            DrawProgressArea();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("JorisHoef Package Installer", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Install, update, remove, and compose JorisHoef packages through first-class bridge packages.",
                    EditorStyles.wordWrappedLabel);

                EditorGUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(IsAnyOperationBusy()))
                    {
                        if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
                        {
                            _packageDetectionService.Refresh();
                            _packageUpdateCheckService.InvalidateAll();
                        }
                    }

                    using (new EditorGUI.DisabledScope(IsAnyOperationBusy()))
                    {
                        if (GUILayout.Button("Check for Updates", GUILayout.Width(130f)))
                        {
                            _packageUpdateCheckService.CheckForUpdates(PackageRegistry.All, GetSelectedChannel);
                        }
                    }

                    PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();

                    using (new EditorGUI.DisabledScope(packagesWithUpdates.Length == 0 || IsAnyOperationBusy()))
                    {
                        if (GUILayout.Button("Update All", GUILayout.Width(95f)))
                        {
                            _packageInstallService.InstallMany(
                                packagesWithUpdates,
                                GetSelectedChannel,
                                "Update All Installed Packages");
                            _packageUpdateCheckService.InvalidateAll();
                        }
                    }

                    using (new EditorGUI.DisabledScope(IsAnyOperationBusy()))
                    {
                        if (GUILayout.Button("Install All", GUILayout.Width(90f)))
                        {
                            _packageDependencyInstaller.InstallAll(GetSelectedChannel);
                        }
                    }
                }
            }

            EditorGUILayout.Space(8f);
        }

        private void DrawSidebar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true)))
            {
                _sidebarScrollPosition = EditorGUILayout.BeginScrollView(_sidebarScrollPosition);
                DrawPackageList();
                EditorGUILayout.Space(10f);
                DrawBridgeList();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPackageList()
        {
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);
            DrawPackageGroup("Core", PackageRegistry.CorePackages);
            EditorGUILayout.Space(4f);
            DrawPackageGroup("UI", PackageRegistry.UiPackages);
        }

        private void DrawBridgeList()
        {
            EditorGUILayout.LabelField("Bridge Packages", EditorStyles.boldLabel);

            foreach (PackageDefinition packageDefinition in PackageRegistry.BridgePackages)
            {
                DrawSidebarItem(packageDefinition, SelectionKind.Bridge);
            }
        }

        private void DrawPackageGroup(string title, IEnumerable<PackageDefinition> packageDefinitions)
        {
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);

            foreach (PackageDefinition packageDefinition in packageDefinitions)
            {
                DrawSidebarItem(packageDefinition, SelectionKind.Package);
            }
        }

        private void DrawSidebarItem(PackageDefinition packageDefinition, SelectionKind selectionKind)
        {
            bool selected = IsSelected(packageDefinition, selectionKind);
            Color previousBackgroundColor = GUI.backgroundColor;

            if (selected)
            {
                GUI.backgroundColor = new Color(0.45f, 0.62f, 0.9f);
            }

            string label = selected ? "> " + packageDefinition.DisplayName : packageDefinition.DisplayName;

            if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Height(24f)))
            {
                SelectDefinition(packageDefinition, selectionKind);
            }

            GUI.backgroundColor = previousBackgroundColor;

            EditorGUILayout.LabelField(GetPackageStatusText(packageDefinition), EditorStyles.miniLabel);
            EditorGUILayout.Space(2f);
        }

        private void DrawDetailsPane()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                _detailsScrollPosition = EditorGUILayout.BeginScrollView(_detailsScrollPosition);

                PackageDefinition selectedDefinition = GetSelectedDefinition();

                if (selectedDefinition == null)
                {
                    EditorGUILayout.HelpBox("Select a package or bridge package.", MessageType.Info);
                }
                else if (selectedDefinition.IsBridge)
                {
                    DrawBridgeDetails(selectedDefinition);
                }
                else
                {
                    DrawPackageDetails(selectedDefinition);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPackageDetails(PackageDefinition packageDefinition)
        {
            DrawDetailHeader(packageDefinition);
            DrawStatusCard(packageDefinition);
            DrawChannelCard(packageDefinition);
            DrawRequirementsCard(packageDefinition);
            DrawActionsCard(packageDefinition);
            DrawExtrasCard(packageDefinition);
            DrawAdvancedFoldout(packageDefinition);
        }

        private void DrawBridgeDetails(PackageDefinition packageDefinition)
        {
            DrawDetailHeader(packageDefinition);
            DrawStatusCard(packageDefinition);
            DrawRequirementsCard(packageDefinition);
            DrawChannelCard(packageDefinition);
            DrawActionsCard(packageDefinition);
            DrawExtrasCard(packageDefinition);
            DrawAdvancedFoldout(packageDefinition);
        }

        private static void DrawDetailHeader(PackageDefinition packageDefinition)
        {
            EditorGUILayout.LabelField(packageDefinition.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(packageDefinition.Description, EditorStyles.wordWrappedLabel);
            DrawDisplayVersion(packageDefinition);
            EditorGUILayout.Space(6f);
        }

        private void DrawStatusCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
                DrawReadOnlyLabel("Type", GetPackageTypeLabel(packageDefinition.PackageType));

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Package", GUILayout.Width(110f));
                    DrawPackageStatus(packageDefinition);
                }

                if (_packageDetectionService.TryGetInstalledPackage(packageDefinition.PackageId, out PackageManagerPackageInfo packageInfo))
                {
                    DrawReadOnlyLabel("Installed version", packageInfo.version);
                }

                DrawPackageUpdateStatus(packageDefinition);
                DrawAvailableUpdateStatus(packageDefinition);

                if (packageDefinition.Dependencies.Count > 0)
                {
                    DrawReadOnlyLabel("Dependencies", GetDependencyDisplayNames(packageDefinition));
                }
            }
        }

        private void DrawChannelCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Channel", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Selected", GUILayout.Width(110f));
                    DrawChannelPopup(packageDefinition);
                    GUILayout.FlexibleSpace();
                }

                PackageChannel selectedChannel = GetSelectedChannel(packageDefinition);
                string selectedUrl = packageDefinition.GetUrl(selectedChannel);

                if (!string.IsNullOrWhiteSpace(selectedUrl))
                {
                    EditorGUILayout.LabelField(
                        GetChannelLabel(selectedChannel) + " will install from the configured package URL.",
                        EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("No package URL is configured for this channel.", MessageType.Warning);
                }
            }
        }

        private void DrawRequirementsCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(packageDefinition.IsBridge ? "Requirements" : "Dependencies", EditorStyles.boldLabel);

                if (packageDefinition.Dependencies.Count == 0)
                {
                    EditorGUILayout.LabelField("No package dependencies.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                foreach (string dependencyId in packageDefinition.Dependencies)
                {
                    DrawRequirementRow(dependencyId);
                }
            }
        }

        private void DrawRequirementRow(string dependencyId)
        {
            if (!PackageRegistry.TryGetPackage(dependencyId, out PackageDefinition dependencyDefinition))
            {
                EditorGUILayout.LabelField(dependencyId, "Not registered", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(dependencyDefinition.DisplayName, GUILayout.MinWidth(150f));
                EditorGUILayout.LabelField(GetPackageStatusText(dependencyDefinition), EditorStyles.miniLabel, GUILayout.Width(115f));
                DrawChannelPopup(dependencyDefinition);
            }
        }

        private void DrawActionsCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
                DrawPackageActions(packageDefinition);
            }
        }

        private void DrawPackageActions(PackageDefinition packageDefinition)
        {
            bool installed = _packageDetectionService.IsInstalled(packageDefinition.PackageId);
            bool queuedOrInstalling = _packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId);
            bool actionsBusy = IsAnyOperationBusy();
            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

            if (!installed)
            {
                PackageDefinition[] missingDependencies = _packageDependencyInstaller.GetMissingDependencies(packageDefinition);

                if (missingDependencies.Length > 0)
                {
                    EditorGUILayout.HelpBox(
                        "Missing dependencies will be installed first: " +
                        string.Join(", ", missingDependencies.Select(package => package.DisplayName).ToArray()),
                        MessageType.Info);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(queuedOrInstalling || actionsBusy))
                    {
                        string buttonLabel = packageDefinition.IsBridge ? "Install Bridge" : "Install";

                        if (GUILayout.Button(buttonLabel, GUILayout.Width(120f)))
                        {
                            _packageDependencyInstaller.InstallWithDependencies(packageDefinition, GetSelectedChannel);
                        }
                    }
                }

                return;
            }

            PackageDefinition[] installedDependents = _packageDependencyInstaller.GetInstalledDependents(packageDefinition);

            if (installedDependents.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    "This package is required by installed package(s): " +
                    string.Join(", ", installedDependents.Select(package => package.DisplayName).ToArray()) +
                    ". Remove those bridge packages first.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!updateStatus.IsUpdateAvailable || queuedOrInstalling || actionsBusy))
                {
                    if (GUILayout.Button("Update", GUILayout.Width(100f)))
                    {
                        _packageInstallService.Install(
                            packageDefinition,
                            GetSelectedChannel(packageDefinition),
                            "Update " + packageDefinition.DisplayName);
                        _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
                    }
                }

                using (new EditorGUI.DisabledScope(installedDependents.Length > 0 || queuedOrInstalling || actionsBusy))
                {
                    if (GUILayout.Button("Remove", GUILayout.Width(100f)) &&
                        EditorUtility.DisplayDialog(
                            "Remove Package",
                            "Remove " + packageDefinition.DisplayName + " from this Unity project?",
                            "Remove",
                            "Cancel"))
                    {
                        _packageInstallService.Remove(packageDefinition);
                        _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
                    }
                }
            }
        }

        private void DrawExtrasCard(PackageDefinition packageDefinition)
        {
            if (packageDefinition.Extras.Count == 0)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Extras / Samples", EditorStyles.boldLabel);

                if (!_packageDetectionService.TryGetInstalledPackage(packageDefinition.PackageId, out PackageManagerPackageInfo packageInfo))
                {
                    EditorGUILayout.HelpBox("Install this package before importing samples.", MessageType.Info);
                    return;
                }

                if (!DrawSampleFoldout(packageDefinition.PackageId))
                {
                    return;
                }

                EditorGUI.indentLevel++;

                foreach (PackageExtraDefinition extraDefinition in packageDefinition.Extras)
                {
                    DrawPackageSampleRow(packageDefinition, extraDefinition, packageInfo);
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawPackageSampleRow(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(extraDefinition.DisplayName, EditorStyles.miniBoldLabel);

                        if (!string.IsNullOrWhiteSpace(extraDefinition.Description))
                        {
                            EditorGUILayout.LabelField(extraDefinition.Description, EditorStyles.wordWrappedMiniLabel);
                        }

                        PackageSampleImportStatus status = _packageSampleImportService.GetStatus(
                            packageDefinition,
                            extraDefinition,
                            packageInfo);
                        string statusText = GetSampleImportStatusText(status);

                        if (!string.IsNullOrWhiteSpace(statusText))
                        {
                            EditorGUILayout.LabelField(statusText, EditorStyles.wordWrappedMiniLabel);
                        }
                    }

                    bool alreadyImported = _packageSampleImportService.IsSampleImported(packageDefinition, extraDefinition, packageInfo);

                    using (new EditorGUI.DisabledScope(alreadyImported || IsAnyOperationBusy()))
                    {
                        string buttonLabel = alreadyImported ? "Already imported" : "Import";

                        if (GUILayout.Button(buttonLabel, GUILayout.Width(120f)))
                        {
                            _packageSampleImportService.ImportSample(packageDefinition, extraDefinition, packageInfo);
                        }
                    }
                }
            }
        }

        private bool DrawSampleFoldout(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            _sampleFoldouts.TryGetValue(packageId, out bool expanded);
            bool nextExpanded = EditorGUILayout.Foldout(expanded, "Samples", true);

            if (nextExpanded != expanded)
            {
                _sampleFoldouts[packageId] = nextExpanded;
            }

            return nextExpanded;
        }

        private void DrawAdvancedFoldout(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!DrawAdvancedFoldout(packageDefinition.PackageId))
                {
                    return;
                }

                DrawPackageAdvancedFields(packageDefinition);
            }
        }

        private void DrawPackageAdvancedFields(PackageDefinition packageDefinition)
        {
            PackageChannel selectedChannel = GetSelectedChannel(packageDefinition);
            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(packageDefinition, selectedChannel);

            EditorGUI.indentLevel++;
            DrawSelectableValue("Package ID", packageDefinition.PackageId);
            DrawSelectableValue("Reference", packageDefinition.GetUrl(selectedChannel));
            DrawSelectableValue("Stable URL", packageDefinition.StableUrl);
            DrawSelectableValue("Development URL", packageDefinition.DevelopmentUrl);
            DrawSelectableValue("Dependencies", string.Join(", ", packageDefinition.Dependencies.ToArray()));

            if (_packageDetectionService.TryGetInstalledPackageReference(packageDefinition.PackageId, out string installedReference))
            {
                DrawSelectableValue("Installed ref", installedReference);
            }

            if (!string.IsNullOrWhiteSpace(updateStatus.InstalledRevision))
            {
                DrawSelectableValue("Installed rev", updateStatus.InstalledRevision);
            }

            if (!string.IsNullOrWhiteSpace(updateStatus.LatestRevision))
            {
                DrawSelectableValue("Latest rev", updateStatus.LatestRevision);
            }

            if (!string.IsNullOrWhiteSpace(updateStatus.Message))
            {
                DrawSelectableValue("Details", updateStatus.Message);
            }

            EditorGUI.indentLevel--;
        }

        private bool DrawAdvancedFoldout(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            _advancedFoldouts.TryGetValue(key, out bool expanded);
            bool nextExpanded = EditorGUILayout.Foldout(expanded, "Advanced", true);

            if (nextExpanded != expanded)
            {
                _advancedFoldouts[key] = nextExpanded;
            }

            return nextExpanded;
        }

        private void DrawProgressArea()
        {
            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Progress", EditorStyles.boldLabel);
                DrawUpdateAllStatus();
                DrawRequestStatus();
                DrawOperationProgress();
                DrawLastOperationSummary();
            }
        }

        private void DrawRequestStatus()
        {
            if (_packageDetectionService.IsRefreshing)
            {
                EditorGUILayout.HelpBox("Refreshing installed packages...", MessageType.Info);
            }

            if (_packageInstallService.State == PackageInstallRequestState.Installing &&
                _packageInstallService.CurrentPackage != null)
            {
                EditorGUILayout.HelpBox("Installing " + _packageInstallService.CurrentPackage.DisplayName + "...", MessageType.Info);
            }

            if (_packageInstallService.State == PackageInstallRequestState.Removing &&
                _packageInstallService.CurrentPackage != null)
            {
                EditorGUILayout.HelpBox("Removing " + _packageInstallService.CurrentPackage.DisplayName + "...", MessageType.Info);
            }

            if (_packageUpdateCheckService.IsChecking)
            {
                EditorGUILayout.HelpBox("Checking installed packages for updates...", MessageType.Info);
            }

            if (_packageSampleImportService.IsBusy)
            {
                EditorGUILayout.HelpBox(
                    "Importing sample " + _packageSampleImportService.CurrentExtraName + "...",
                    MessageType.Info);
            }
            else if (!string.IsNullOrWhiteSpace(_packageSampleImportService.LastErrorMessage))
            {
                EditorGUILayout.HelpBox(_packageSampleImportService.LastErrorMessage, MessageType.Warning);
            }
            else if (!string.IsNullOrWhiteSpace(_packageSampleImportService.LastStatusMessage))
            {
                EditorGUILayout.HelpBox(_packageSampleImportService.LastStatusMessage, MessageType.Info);
            }
        }

        private void DrawOperationProgress()
        {
            if (_packageInstallService.HasProgress)
            {
                DrawPackageInstallProgress();
            }
        }

        private void DrawPackageInstallProgress()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(_packageInstallService.CurrentOperationName)
                        ? "Package Operation"
                        : _packageInstallService.CurrentOperationName,
                    EditorStyles.boldLabel);

                DrawStepProgressBar(_packageInstallService.CompletedSteps, _packageInstallService.TotalSteps);

                if (_packageInstallService.IsBusy && !string.IsNullOrWhiteSpace(_packageInstallService.CurrentPackageName))
                {
                    EditorGUILayout.LabelField("Current item", _packageInstallService.CurrentPackageName);
                }

                DrawProgressMessage(
                    _packageInstallService.LastStatusMessage,
                    _packageInstallService.LastErrorMessage,
                    _packageInstallService.FailedSteps);

                DrawPackageProgressItems(_packageInstallService.ProgressItems);
            }
        }

        private void DrawLastOperationSummary()
        {
            string summary = GetLastOperationSummary();

            if (string.IsNullOrWhiteSpace(summary))
            {
                EditorGUILayout.LabelField("Last operation", "No operations have completed yet.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField("Last operation", summary, EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawStepProgressBar(int completedSteps, int totalSteps)
        {
            int safeTotalSteps = Mathf.Max(totalSteps, 1);
            float progress = Mathf.Clamp01(completedSteps / (float)safeTotalSteps);
            Rect progressRect = GUILayoutUtility.GetRect(1f, 18f);

            EditorGUI.ProgressBar(progressRect, progress, "Completed " + completedSteps + " / " + totalSteps);
        }

        private static void DrawProgressMessage(string statusMessage, string errorMessage, int failedSteps)
        {
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                EditorGUILayout.LabelField(statusMessage, EditorStyles.wordWrappedMiniLabel);
            }

            if (failedSteps > 0 && !string.IsNullOrWhiteSpace(errorMessage))
            {
                EditorGUILayout.HelpBox(errorMessage, MessageType.Warning);
            }
        }

        private static void DrawPackageProgressItems(IReadOnlyList<PackageInstallProgressItem> progressItems)
        {
            if (progressItems == null || progressItems.Count <= 1)
            {
                return;
            }

            DrawProgressItemStateLine(progressItems, PackageInstallProgressItemState.Pending);
            DrawProgressItemStateLine(progressItems, PackageInstallProgressItemState.Active);
            DrawProgressItemStateLine(progressItems, PackageInstallProgressItemState.Completed);
            DrawProgressItemStateLine(progressItems, PackageInstallProgressItemState.Failed);
            DrawProgressItemStateLine(progressItems, PackageInstallProgressItemState.Skipped);
        }

        private static void DrawProgressItemStateLine(
            IReadOnlyList<PackageInstallProgressItem> progressItems,
            PackageInstallProgressItemState state)
        {
            string[] names = progressItems
                .Where(item => item.State == state)
                .Select(item => item.DisplayName)
                .ToArray();

            if (names.Length == 0)
            {
                return;
            }

            EditorGUILayout.LabelField(
                GetProgressItemStateLabel(state) + ": " + string.Join(", ", names),
                EditorStyles.wordWrappedMiniLabel);
        }

        private static string GetProgressItemStateLabel(PackageInstallProgressItemState state)
        {
            switch (state)
            {
                case PackageInstallProgressItemState.Active:
                    return "Active";
                case PackageInstallProgressItemState.Completed:
                    return "Succeeded";
                case PackageInstallProgressItemState.Failed:
                    return "Failed";
                case PackageInstallProgressItemState.Skipped:
                    return "Skipped";
                default:
                    return "Pending";
            }
        }

        private void EnsureValidSelection()
        {
            if (GetSelectedDefinition() != null)
            {
                return;
            }

            PackageDefinition defaultSelection = PackageRegistry.CorePackages.FirstOrDefault() ??
                                                 PackageRegistry.UiPackages.FirstOrDefault() ??
                                                 PackageRegistry.BridgePackages.FirstOrDefault();

            _selectedPackageId = defaultSelection != null ? defaultSelection.PackageId : string.Empty;
            _selectionKind = defaultSelection != null && defaultSelection.IsBridge
                ? SelectionKind.Bridge
                : SelectionKind.Package;
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

            return PackageRegistry.All.FirstOrDefault(packageDefinition =>
                string.Equals(packageDefinition.PackageId, _selectedPackageId, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetDependencyDisplayNames(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null || packageDefinition.Dependencies.Count == 0)
            {
                return "-";
            }

            return string.Join(", ", packageDefinition.Dependencies.Select(GetDependencyDisplayName).ToArray());
        }

        private static string GetDependencyDisplayName(string packageId)
        {
            return PackageRegistry.TryGetPackage(packageId, out PackageDefinition packageDefinition)
                ? packageDefinition.DisplayName
                : packageId;
        }

        private void DrawPackageStatus(PackageDefinition packageDefinition)
        {
            if (_packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId))
            {
                DrawStatusLabel("Queued", MessageType.Info);
                return;
            }

            if (_packageDetectionService.TryGetInstalledPackage(packageDefinition.PackageId, out PackageManagerPackageInfo packageInfo))
            {
                DrawStatusLabel("Installed " + packageInfo.version, MessageType.None);
                return;
            }

            DrawStatusLabel("Not installed", MessageType.Warning);
        }

        private string GetPackageStatusText(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return "Unknown";
            }

            if (_packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId))
            {
                return "Queued";
            }

            if (_packageDetectionService.TryGetInstalledPackage(packageDefinition.PackageId, out PackageManagerPackageInfo packageInfo))
            {
                return "Installed " + packageInfo.version;
            }

            return "Not installed";
        }

        private void DrawPackageUpdateStatus(PackageDefinition packageDefinition)
        {
            PackageUpdateStatus status = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

            DrawReadOnlyLabel("Update", GetUpdateStatusText(status));
        }

        private void DrawAvailableUpdateStatus(PackageDefinition packageDefinition)
        {
            PackageUpdateStatus status = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

            string label = status.IsUpdateAvailable && !string.IsNullOrWhiteSpace(status.ShortLatestRevision)
                ? status.ShortLatestRevision
                : "-";

            DrawReadOnlyLabel("Available update", label);
        }

        private static void DrawDisplayVersion(PackageDefinition packageDefinition)
        {
            if (!packageDefinition.HasDisplayVersion)
            {
                return;
            }

            DrawReadOnlyLabel("Version", packageDefinition.DisplayVersion);
        }

        private void DrawChannelPopup(PackageDefinition packageDefinition)
        {
            PackageChannel selectedChannel = GetSelectedChannel(packageDefinition);
            PackageChannel[] channelOptions = GetChannelOptions(packageDefinition, selectedChannel);
            string[] channelLabels = channelOptions.Select(GetChannelLabel).ToArray();
            int selectedIndex = Mathf.Max(0, Array.IndexOf(channelOptions, selectedChannel));

            using (new EditorGUI.DisabledScope(channelOptions.Length <= 1 || IsAnyOperationBusy()))
            {
                int nextIndex = EditorGUILayout.Popup(selectedIndex, channelLabels, GUILayout.Width(115f));
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
            EditorPrefs.SetInt(GetChannelPreferenceKey(packageDefinition.PackageId), (int)channel);
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

        private string GetChannelPreferenceKey(string packageId)
        {
            return ChannelPreferencePrefix + Application.dataPath.Replace("\\", "/") + "." + packageId;
        }

        private static PackageChannel[] GetChannelOptions(PackageDefinition packageDefinition, PackageChannel selectedChannel)
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
            foreach (PackageDefinition packageDefinition in PackageRegistry.All)
            {
                if (!_packageDetectionService.TryGetInstalledPackageChannel(packageDefinition, out PackageChannel installedChannel, out _))
                {
                    continue;
                }

                PackageChannel currentChannel = GetSelectedChannel(packageDefinition);

                if (currentChannel != installedChannel)
                {
                    SetSelectedChannel(packageDefinition, installedChannel);
                }
            }
        }

        private static void DrawStatusLabel(string label, MessageType messageType)
        {
            GUIStyle style = EditorStyles.miniBoldLabel;
            Color previousColor = GUI.contentColor;

            if (messageType == MessageType.Warning)
            {
                GUI.contentColor = new Color(0.9f, 0.62f, 0.2f);
            }
            else if (messageType == MessageType.Info)
            {
                GUI.contentColor = new Color(0.35f, 0.62f, 0.95f);
            }
            else
            {
                GUI.contentColor = new Color(0.35f, 0.75f, 0.35f);
            }

            GUILayout.Label(label, style, GUILayout.Width(140f));
            GUI.contentColor = previousColor;
        }

        private static void DrawReadOnlyLabel(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(110f));
                EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(value) ? "-" : value, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void DrawSelectableValue(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(110f));
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrWhiteSpace(value) ? "-" : value,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void DrawUpdateAllStatus()
        {
            if (!_packageUpdateCheckService.HasStatuses)
            {
                return;
            }

            if (_packageUpdateCheckService.IsChecking)
            {
                EditorGUILayout.HelpBox("Checking installed packages for updates...", MessageType.Info);
                return;
            }

            PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();

            if (packagesWithUpdates.Length == 0)
            {
                EditorGUILayout.HelpBox("No package updates available for installed packages.", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(
                "Will update: " + string.Join(", ", packagesWithUpdates.Select(package => package.DisplayName).ToArray()),
                MessageType.Info);
        }

        private PackageDefinition[] GetPackagesWithUpdates()
        {
            return _packageUpdateCheckService
                .GetPackagesWithUpdates(PackageRegistry.All, GetSelectedChannel)
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

        private static string GetPackageTypeLabel(PackageType packageType)
        {
            switch (packageType)
            {
                case PackageType.UI:
                    return "UI";
                case PackageType.Bridge:
                    return "Bridge";
                default:
                    return "Core";
            }
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
                    return string.Empty;
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
            SynchronizeSelectedChannelsFromInstalledPackages();

            if (!_checkUpdatesAfterDetectionRefresh)
            {
                return;
            }

            _checkUpdatesAfterDetectionRefresh = false;
            _packageUpdateCheckService.CheckForUpdates(PackageRegistry.All, GetSelectedChannel);
        }
    }
}
