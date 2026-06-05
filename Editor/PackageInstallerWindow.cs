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
            Integration
        }

        private PackageInstallService _packageInstallService;
        private PackageDetectionService _packageDetectionService;
        private PackageUpdateCheckService _packageUpdateCheckService;
        private PackageSampleImportService _packageSampleImportService;
        private ScriptingDefineService _scriptingDefineService;
        private IntegrationInstaller _integrationInstaller;
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
            _scriptingDefineService = new ScriptingDefineService();
            _integrationInstaller = new IntegrationInstaller(
                _packageInstallService,
                _packageDetectionService,
                _scriptingDefineService);
            EnsureValidSelection();

            _packageInstallService.StateChanged += Repaint;
            _packageInstallService.QueueCompleted += HandlePackageInstallQueueCompleted;
            _packageDetectionService.StateChanged += Repaint;
            _packageDetectionService.RefreshCompleted += HandlePackageDetectionRefreshCompleted;
            _integrationInstaller.StateChanged += Repaint;
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
                _packageInstallService.QueueCompleted -= HandlePackageInstallQueueCompleted;
                _packageInstallService.Dispose();
            }

            if (_integrationInstaller != null)
            {
                _integrationInstaller.StateChanged -= Repaint;
                _integrationInstaller.Dispose();
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
                    "Install standalone packages and enable optional integrations for the active build target.",
                    EditorStyles.wordWrappedLabel);

                EditorGUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Build Target Group", GUILayout.Width(130f));
                    using (new EditorGUI.DisabledScope(IsAnyOperationBusy()))
                    {
                        BuildTargetGroup selectedBuildTargetGroup = _scriptingDefineService.SelectedBuildTargetGroup;
                        BuildTargetGroup nextBuildTargetGroup = (BuildTargetGroup)EditorGUILayout.EnumPopup(
                            selectedBuildTargetGroup,
                            GUILayout.Width(170f));

                        if (nextBuildTargetGroup != selectedBuildTargetGroup)
                        {
                            _scriptingDefineService.SelectedBuildTargetGroup = nextBuildTargetGroup;
                            Repaint();
                        }
                    }

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
                            _packageUpdateCheckService.CheckForUpdates(PackageRegistry.StandalonePackages, GetSelectedChannel);
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
                            _integrationInstaller.InstallAll(GetSelectedChannel);
                        }
                    }
                }
            }

            EditorGUILayout.Space(8f);
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

        private void DrawOperationProgress()
        {
            if (_packageInstallService.HasProgress)
            {
                DrawPackageInstallProgress();
            }

            if (_integrationInstaller.HasProgress &&
                (!_packageInstallService.HasProgress || _integrationInstaller.IsBusy || !_packageInstallService.IsBusy))
            {
                DrawIntegrationProgress();
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

                DrawStepProgressBar(
                    _packageInstallService.CompletedSteps,
                    _packageInstallService.TotalSteps);

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

        private void DrawIntegrationProgress()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(_integrationInstaller.CurrentOperationName)
                        ? "Integration Operation"
                        : _integrationInstaller.CurrentOperationName,
                    EditorStyles.boldLabel);

                DrawStepProgressBar(
                    _integrationInstaller.CompletedSteps,
                    _integrationInstaller.TotalSteps);

                if (_integrationInstaller.IsBusy && !string.IsNullOrWhiteSpace(_integrationInstaller.CurrentPackageName))
                {
                    EditorGUILayout.LabelField("Current item", _integrationInstaller.CurrentPackageName);
                }

                DrawProgressMessage(
                    _integrationInstaller.LastStatusMessage,
                    _integrationInstaller.LastErrorMessage,
                    _integrationInstaller.FailedSteps);
                DrawPackageProgressItems(_integrationInstaller.ProgressItems);
            }
        }

        private static void DrawStepProgressBar(int completedSteps, int totalSteps)
        {
            int safeTotalSteps = Mathf.Max(totalSteps, 1);
            float progress = Mathf.Clamp01(completedSteps / (float)safeTotalSteps);
            Rect progressRect = GUILayoutUtility.GetRect(1f, 18f);

            EditorGUI.ProgressBar(
                progressRect,
                progress,
                "Completed " + completedSteps + " / " + totalSteps);
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

        private void DrawSidebar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true)))
            {
                _sidebarScrollPosition = EditorGUILayout.BeginScrollView(_sidebarScrollPosition);
                DrawPackageList();
                EditorGUILayout.Space(10f);
                DrawIntegrationList();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPackageList()
        {
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);

            foreach (PackageDefinition packageDefinition in PackageRegistry.StandalonePackages)
            {
                DrawSidebarItem(packageDefinition, SelectionKind.Package);
            }
        }

        private void DrawIntegrationList()
        {
            EditorGUILayout.LabelField("Integrations", EditorStyles.boldLabel);

            foreach (PackageDefinition packageDefinition in PackageRegistry.Integrations)
            {
                DrawSidebarItem(packageDefinition, SelectionKind.Integration);
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

            string statusText = selectionKind == SelectionKind.Package
                ? GetPackageStatusText(packageDefinition)
                : GetIntegrationStatusText(packageDefinition);
            EditorGUILayout.LabelField(statusText, EditorStyles.miniLabel);
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
                    EditorGUILayout.HelpBox("Select a package or integration.", MessageType.Info);
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
            }
        }

        private void DrawPackageDetails(PackageDefinition packageDefinition)
        {
            DrawDetailHeader(packageDefinition);
            DrawStatusCard(packageDefinition);
            DrawChannelCard(packageDefinition);
            DrawActionsCard(packageDefinition);
            DrawExtrasCard(packageDefinition);
            DrawAdvancedFoldout(packageDefinition);
        }

        private void DrawIntegrationDetails(PackageDefinition packageDefinition)
        {
            DrawDetailHeader(packageDefinition);
            DrawStatusCard(packageDefinition);
            DrawRequirementsCard(packageDefinition);
            DrawActionsCard(packageDefinition);
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

                if (packageDefinition.IsIntegration)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Integration", GUILayout.Width(90f));
                        DrawIntegrationStatus(packageDefinition);
                    }

                    EditorGUILayout.LabelField("Requires", GetDependencyDisplayNames(packageDefinition), EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Package", GUILayout.Width(90f));
                    DrawPackageStatus(packageDefinition);
                }

                DrawPackageUpdateStatus(packageDefinition);
            }
        }

        private void DrawChannelCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Channel", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Selected", GUILayout.Width(90f));
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

        private void DrawActionsCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

                if (packageDefinition.IsIntegration)
                {
                    DrawIntegrationActions(packageDefinition);
                    return;
                }

                DrawPackageActions(packageDefinition);
            }
        }

        private void DrawRequirementsCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Requirements", EditorStyles.boldLabel);

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
                EditorGUILayout.LabelField(dependencyDefinition.DisplayName, GUILayout.MinWidth(130f));
                EditorGUILayout.LabelField(GetPackageStatusText(dependencyDefinition), EditorStyles.miniLabel, GUILayout.Width(95f));
                DrawChannelPopup(dependencyDefinition);
            }
        }

        private void DrawExtrasCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Extras / Samples", EditorStyles.boldLabel);

                if (packageDefinition.Extras.Count == 0)
                {
                    EditorGUILayout.LabelField("No extras or samples are registered for this package.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                if (!_packageDetectionService.TryGetInstalledPackage(
                        packageDefinition.PackageId,
                        out PackageManagerPackageInfo packageInfo))
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

        private void DrawPackageActions(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                bool installed = _packageDetectionService.IsInstalled(packageDefinition.PackageId);
                bool queuedOrInstalling = _packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId);
                PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                    packageDefinition,
                    GetSelectedChannel(packageDefinition));
                bool actionsBusy = IsAnyOperationBusy();

                if (installed)
                {
                    if (updateStatus.IsUpdateAvailable)
                    {
                        using (new EditorGUI.DisabledScope(queuedOrInstalling || actionsBusy))
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

                        return;
                    }

                    EditorGUILayout.LabelField("Installed", EditorStyles.miniBoldLabel, GUILayout.Width(100f));
                    return;
                }

                using (new EditorGUI.DisabledScope(queuedOrInstalling || actionsBusy))
                {
                    if (GUILayout.Button("Install", GUILayout.Width(100f)))
                    {
                        _integrationInstaller.InstallPackage(packageDefinition, GetSelectedChannel(packageDefinition));
                    }
                }
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

                    bool alreadyImported = _packageSampleImportService.IsSampleImported(
                        packageDefinition,
                        extraDefinition,
                        packageInfo);

                    using (new EditorGUI.DisabledScope(alreadyImported || IsAnyOperationBusy()))
                    {
                        string buttonLabel = alreadyImported ? "Already imported" : "Import";

                        if (GUILayout.Button(buttonLabel, GUILayout.Width(120f)))
                        {
                            _packageSampleImportService.ImportSample(
                                packageDefinition,
                                extraDefinition,
                                packageInfo);
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

        private void DrawIntegrationActions(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                bool complete = _integrationInstaller.IsIntegrationComplete(packageDefinition);
                bool pending = _integrationInstaller.HasPendingIntegration(packageDefinition);

                using (new EditorGUI.DisabledScope(complete || pending || IsAnyOperationBusy()))
                {
                    string buttonLabel = complete ? "Enabled" : pending ? "Pending" : "Install Integration";

                    if (GUILayout.Button(buttonLabel, GUILayout.Width(140f)))
                    {
                        _integrationInstaller.InstallIntegration(packageDefinition, GetSelectedChannel);
                    }
                }
            }
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

                if (packageDefinition.IsIntegration)
                {
                    DrawIntegrationAdvancedFields(packageDefinition);
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

            if (_packageDetectionService.TryGetInstalledPackageReference(
                    packageDefinition.PackageId,
                    out string installedReference))
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

        private void DrawIntegrationAdvancedFields(PackageDefinition packageDefinition)
        {
            EditorGUI.indentLevel++;
            DrawSelectableValue("Package ID", packageDefinition.PackageId);
            DrawSelectableValue("Dependencies", string.Join(", ", packageDefinition.Dependencies.ToArray()));
            DrawSelectableValue("Defines", string.Join(", ", packageDefinition.ScriptingDefineSymbols.ToArray()));
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

        private void EnsureValidSelection()
        {
            if (GetSelectedDefinition() != null)
            {
                return;
            }

            PackageDefinition defaultSelection = PackageRegistry.StandalonePackages.FirstOrDefault();

            if (defaultSelection == null)
            {
                defaultSelection = PackageRegistry.Integrations.FirstOrDefault();
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

            IEnumerable<PackageDefinition> source = _selectionKind == SelectionKind.Integration
                ? PackageRegistry.Integrations
                : PackageRegistry.StandalonePackages;

            return source.FirstOrDefault(packageDefinition =>
                string.Equals(packageDefinition.PackageId, _selectedPackageId, StringComparison.OrdinalIgnoreCase));
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

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Update", GUILayout.Width(90f));
                EditorGUILayout.LabelField(GetUpdateStatusText(status), EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawIntegrationStatus(PackageDefinition packageDefinition)
        {
            if (_integrationInstaller.HasPendingIntegration(packageDefinition))
            {
                DrawStatusLabel("Pending", MessageType.Info);
                return;
            }

            bool dependenciesInstalled = _integrationInstaller.ArePackageDependenciesInstalled(packageDefinition);
            bool symbolsEnabled = _integrationInstaller.AreIntegrationSymbolsEnabled(packageDefinition);

            if (dependenciesInstalled && symbolsEnabled)
            {
                DrawStatusLabel("Enabled", MessageType.None);
                return;
            }

            if (!dependenciesInstalled && symbolsEnabled)
            {
                DrawStatusLabel("Defines enabled", MessageType.Info);
                return;
            }

            if (dependenciesInstalled)
            {
                DrawStatusLabel("Packages installed", MessageType.Info);
                return;
            }

            DrawStatusLabel("Not enabled", MessageType.Warning);
        }

        private string GetIntegrationStatusText(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return "Unknown";
            }

            if (_integrationInstaller.HasPendingIntegration(packageDefinition))
            {
                return "Pending";
            }

            bool dependenciesInstalled = _integrationInstaller.ArePackageDependenciesInstalled(packageDefinition);
            bool symbolsEnabled = _integrationInstaller.AreIntegrationSymbolsEnabled(packageDefinition);

            if (dependenciesInstalled && symbolsEnabled)
            {
                return "Enabled";
            }

            if (!dependenciesInstalled && symbolsEnabled)
            {
                return "Defines enabled";
            }

            if (dependenciesInstalled)
            {
                return "Packages installed";
            }

            return "Not enabled";
        }

        private static void DrawDisplayVersion(PackageDefinition packageDefinition)
        {
            if (!packageDefinition.HasDisplayVersion)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Version", GUILayout.Width(90f));
                EditorGUILayout.LabelField(packageDefinition.DisplayVersion, EditorStyles.miniLabel);
            }
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
                    GUILayout.Width(115f));
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
            foreach (PackageDefinition packageDefinition in PackageRegistry.StandalonePackages)
            {
                if (!_packageDetectionService.TryGetInstalledPackageChannel(
                        packageDefinition,
                        out PackageChannel installedChannel,
                        out _))
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

            GUILayout.Label(label, style, GUILayout.Width(120f));
            GUI.contentColor = previousColor;
        }

        private static void DrawSelectableValue(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(90f));
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
                .GetPackagesWithUpdates(PackageRegistry.StandalonePackages, GetSelectedChannel)
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

            if (_integrationInstaller.HasProgress &&
                !string.IsNullOrWhiteSpace(_integrationInstaller.LastStatusMessage))
            {
                return _integrationInstaller.LastStatusMessage;
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
                   _integrationInstaller.IsBusy ||
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

        private void HandlePackageInstallQueueCompleted()
        {
            if (_packageUpdateCheckService.HasStatuses)
            {
                _checkUpdatesAfterDetectionRefresh = true;
            }
        }

        private void HandlePackageDetectionRefreshCompleted()
        {
            SynchronizeSelectedChannelsFromInstalledPackages();

            if (!_checkUpdatesAfterDetectionRefresh)
            {
                return;
            }

            _checkUpdatesAfterDetectionRefresh = false;
            _packageUpdateCheckService.CheckForUpdates(PackageRegistry.StandalonePackages, GetSelectedChannel);
        }

    }
}
