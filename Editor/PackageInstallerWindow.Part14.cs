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
                if (packageDefinition.IsTemplate && packageDefinition.CompositionPresets.Count > 0)
                {
                    string setupName = GetActiveTemplateCompositionName(packageDefinition);
                    if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                            DeucarianEditorIconIds.Download,
                            "Install " + setupName,
                            "Install the template and every package selected in this viewer setup.",
                            !queuedOrInstalling && !actionsBusy,
                            true,
                            stackActions ? GUILayout.ExpandWidth(true) : GUILayout.Width(190f)))
                    {
                        InstallTemplateComposition(packageDefinition, includeTemplate: true);
                    }

                    return;
                }

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

            if (packageDefinition.IsTemplate && packageDefinition.CompositionPresets.Count > 0)
            {
                PackageDefinition[] missingCompositionRoots = GetTemplateCompositionRoots(
                    packageDefinition,
                    includeTemplate: false)
                    .Where(package => !_packageDetectionService.IsInstalled(package.PackageId))
                    .ToArray();
                if (missingCompositionRoots.Length > 0)
                {
                    if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                            DeucarianEditorIconIds.Download,
                            "Install selected connections (" + missingCompositionRoots.Length + ")",
                            "Install the selected optional viewer connections and their required dependencies.",
                            !queuedOrInstalling && !actionsBusy,
                            true,
                            GUILayout.ExpandWidth(true)))
                    {
                        InstallTemplateComposition(packageDefinition, includeTemplate: false);
                    }

                    GUILayout.Space(8f);
                }
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

        private string GetActiveTemplateCompositionName(PackageDefinition templateDefinition)
        {
            GetOrCreateTemplateCompositionSelection(templateDefinition);
            if (_templateCompositionPresetIds.TryGetValue(
                    templateDefinition.PackageId,
                    out string selectedPresetId) &&
                !string.IsNullOrWhiteSpace(selectedPresetId))
            {
                PackageCompositionPresetDefinition preset = templateDefinition.CompositionPresets
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.Id,
                        selectedPresetId,
                        StringComparison.OrdinalIgnoreCase));
                if (preset != null)
                {
                    return preset.DisplayName;
                }
            }

            return "Custom Setup";
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
            if (BootstrapEditorOpenBridge.TryOpen())
            {
                PackageInstallerLog.Install.DiagnosticInfo(
                    "Opened Bootstrap for Package Installer source migration.");
                return;
            }

            PackageChannel channel = GetSelectedChannel(packageDefinition);
            string bootstrapUrl = GetBootstrapGitUrl(channel);
            string message =
                "Bootstrap is not installed or its setup API is unavailable. Add " + bootstrapUrl +
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
    }
}
