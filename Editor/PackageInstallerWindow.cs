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

        private PackageInstallService _packageInstallService;
        private PackageDetectionService _packageDetectionService;
        private ScriptingDefineService _scriptingDefineService;
        private IntegrationInstaller _integrationInstaller;

        private Vector2 _scrollPosition;

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
            _scriptingDefineService = new ScriptingDefineService();
            _integrationInstaller = new IntegrationInstaller(
                _packageInstallService,
                _packageDetectionService,
                _scriptingDefineService);

            _packageInstallService.StateChanged += Repaint;
            _packageInstallService.QueueCompleted += RefreshInstalledPackages;
            _packageDetectionService.StateChanged += Repaint;

            _packageDetectionService.Refresh();
        }

        private void OnDisable()
        {
            if (_packageInstallService != null)
            {
                _packageInstallService.StateChanged -= Repaint;
                _packageInstallService.QueueCompleted -= RefreshInstalledPackages;
                _packageInstallService.Dispose();
            }

            if (_packageDetectionService != null)
            {
                _packageDetectionService.StateChanged -= Repaint;
                _packageDetectionService.Dispose();
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
                EditorGUILayout.SelectableLabel(
                    _scriptingDefineService.SelectedBuildTargetGroup.ToString(),
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));

                using (new EditorGUI.DisabledScope(_packageDetectionService.IsRefreshing))
                {
                    if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
                    {
                        _packageDetectionService.Refresh();
                    }
                }
            }

            DrawRequestStatus();
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
        }

        private void DrawStandalonePackages()
        {
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);

            foreach (PackageDefinition packageDefinition in PackageRegistry.StandalonePackages)
            {
                DrawPackageCard(packageDefinition);
            }
        }

        private void DrawIntegrations()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Integrations", EditorStyles.boldLabel);

            foreach (PackageDefinition packageDefinition in PackageRegistry.Integrations)
            {
                DrawIntegrationCard(packageDefinition);
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_packageInstallService.IsBusy || _packageDetectionService.IsRefreshing))
                {
                    if (GUILayout.Button("Install All", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                    {
                        _integrationInstaller.InstallAll();
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
                    DrawPackageStatus(packageDefinition);
                }

                EditorGUILayout.LabelField(packageDefinition.Description, EditorStyles.wordWrappedLabel);
                DrawSelectableValue("Package ID", packageDefinition.PackageId);
                DrawSelectableValue("Reference", packageDefinition.PackageReference);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    bool installed = _packageDetectionService.IsInstalled(packageDefinition.PackageId);
                    bool queuedOrInstalling = _packageInstallService.IsQueuedOrInstalling(packageDefinition.PackageId);

                    using (new EditorGUI.DisabledScope(installed || queuedOrInstalling || _packageDetectionService.IsRefreshing))
                    {
                        if (GUILayout.Button(installed ? "Installed" : "Install", GUILayout.Width(100f)))
                        {
                            _integrationInstaller.InstallPackage(packageDefinition);
                        }
                    }
                }
            }
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
                DrawSelectableValue("Dependencies", string.Join(", ", packageDefinition.Dependencies.ToArray()));
                DrawSelectableValue("Defines", string.Join(", ", packageDefinition.ScriptingDefineSymbols.ToArray()));

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    bool complete = _integrationInstaller.IsIntegrationComplete(packageDefinition);

                    using (new EditorGUI.DisabledScope(complete || _packageDetectionService.IsRefreshing))
                    {
                        if (GUILayout.Button(complete ? "Enabled" : "Install Integration", GUILayout.Width(140f)))
                        {
                            _integrationInstaller.InstallIntegration(packageDefinition);
                        }
                    }
                }
            }
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

        private void DrawIntegrationStatus(PackageDefinition packageDefinition)
        {
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

        private void RefreshInstalledPackages()
        {
            _packageDetectionService.Refresh();
        }
    }
}
