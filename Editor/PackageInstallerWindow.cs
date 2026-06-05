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
        private const float MinWindowWidth = 520f;
        private const float MinWindowHeight = 480f;
        private const string ChannelPreferencePrefix = "JorisHoef.PackageInstaller.SelectedChannel.";

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

        private Vector2 _scrollPosition;
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
            DrawHeader();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawStandalonePackages();
            DrawIntegrations();

            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("JorisHoef Package Installer", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Install standalone packages and enable optional integrations for the active build target.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(6f);

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
            }

            DrawRequestStatus();
            DrawOperationProgress();
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

        private void DrawStandalonePackages()
        {
            EditorGUILayout.LabelField("Standalone Packages", EditorStyles.boldLabel);

            foreach (PackageDefinition packageDefinition in PackageRegistry.StandalonePackages)
            {
                DrawPackageCard(packageDefinition);
            }
        }

        private void DrawIntegrations()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Integrations", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Optional integrations install required packages first, then enable define symbols for the selected build target group.",
                MessageType.None);

            foreach (PackageDefinition packageDefinition in PackageRegistry.Integrations)
            {
                DrawIntegrationCard(packageDefinition);
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(8f);
            DrawUpdateAllStatus();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                PackageDefinition[] packagesWithUpdates = GetPackagesWithUpdates();

                using (new EditorGUI.DisabledScope(packagesWithUpdates.Length == 0 || IsAnyOperationBusy()))
                {
                    if (GUILayout.Button("Update All Installed Packages", EditorStyles.toolbarButton, GUILayout.Width(190f)))
                    {
                        _packageInstallService.InstallMany(
                            packagesWithUpdates,
                            GetSelectedChannel,
                            "Update All Installed Packages");
                        _packageUpdateCheckService.InvalidateAll();
                    }
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(IsAnyOperationBusy()))
                {
                    if (GUILayout.Button("Install All", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                    {
                        _integrationInstaller.InstallAll(GetSelectedChannel);
                    }
                }
            }
        }

        private void DrawPackageCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(packageDefinition.DisplayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    DrawChannelPopup(packageDefinition);
                    GUILayout.Space(8f);
                    DrawPackageStatus(packageDefinition);
                }

                EditorGUILayout.LabelField(packageDefinition.Description, EditorStyles.wordWrappedLabel);
                DrawDisplayVersion(packageDefinition);
                DrawPackageUpdateStatus(packageDefinition);
                DrawPackageActions(packageDefinition);
                DrawPackageSamples(packageDefinition);
                DrawPackageAdvanced(packageDefinition);
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

        private void DrawPackageSamples(PackageDefinition packageDefinition)
        {
            if (packageDefinition.Extras.Count == 0 ||
                !_packageDetectionService.TryGetInstalledPackage(
                    packageDefinition.PackageId,
                    out PackageManagerPackageInfo packageInfo))
            {
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

        private void DrawIntegrationCard(PackageDefinition packageDefinition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(packageDefinition.DisplayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    DrawIntegrationStatus(packageDefinition);
                }

                EditorGUILayout.LabelField(packageDefinition.Description, EditorStyles.wordWrappedLabel);
                DrawDisplayVersion(packageDefinition);
                EditorGUILayout.LabelField("Requires", GetDependencyDisplayNames(packageDefinition), EditorStyles.wordWrappedMiniLabel);

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

                DrawIntegrationAdvanced(packageDefinition);
            }
        }

        private void DrawPackageAdvanced(PackageDefinition packageDefinition)
        {
            if (!DrawAdvancedFoldout(packageDefinition.PackageId))
            {
                return;
            }

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

        private void DrawIntegrationAdvanced(PackageDefinition packageDefinition)
        {
            if (!DrawAdvancedFoldout(packageDefinition.PackageId))
            {
                return;
            }

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
