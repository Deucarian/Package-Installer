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

        private void DrawPackageStatusContent(PackageDefinition packageDefinition)
        {
            VisualStatus status = GetPackageVisualStatus(packageDefinition);
            PackageUpdateStatus updateStatus = _packageUpdateCheckService.GetStatus(
                packageDefinition,
                GetSelectedChannel(packageDefinition));

            ImGui.DrawStatusBadge(status.Label, status.Kind, GUILayout.Width(150f));
            GUILayout.Space(6f);
            ImGui.DrawKeyValueRow("Domain", GetPackageHierarchyPath(packageDefinition));
            ImGui.DrawKeyValueRow("Package kind", GetPackageKindDisplayName(packageDefinition));
            ImGui.DrawKeyValueRow("Package ID", packageDefinition.PackageId);

            if (_packageDetectionService.TryGetInstalledPackage(
                    packageDefinition.PackageId,
                    out PackageManagerPackageInfo packageInfo))
            {
                ImGui.DrawKeyValueRow("Package", "Installed");
                ImGui.DrawKeyValueRow("Version", GetPackageVersionText(packageInfo.version, updateStatus));
            }
            else
            {
                ImGui.DrawKeyValueRow("Package", "Not installed");
                ImGui.DrawKeyValueRow("Version", "-");
            }

            ImGui.DrawKeyValueRow("Update", GetUpdateStatusText(updateStatus));
            ImGui.DrawKeyValueRow("Installed rev", string.IsNullOrWhiteSpace(updateStatus.ShortInstalledRevision) ? "-" : updateStatus.ShortInstalledRevision);
            ImGui.DrawKeyValueRow("Latest rev", string.IsNullOrWhiteSpace(updateStatus.ShortLatestRevision) ? "-" : updateStatus.ShortLatestRevision);

            if (updateStatus.HasUnbumpedPackageVersionWarning)
            {
                ImGui.DrawInlineHelp(updateStatus.PackageVersionWarningMessage, PackageInstallerVisualStatusKind.UpdateAvailable);
            }
            else if ((updateStatus.IsSourceMigrationAvailable || updateStatus.IsReloadPending) &&
                     !string.IsNullOrWhiteSpace(updateStatus.Message))
            {
                ImGui.DrawInlineHelp(updateStatus.Message, PackageInstallerVisualStatusKind.UpdateAvailable);
            }
            else if (updateStatus.Kind == PackageUpdateStatusKind.CannotDetermine && !string.IsNullOrWhiteSpace(updateStatus.Message))
            {
                ImGui.DrawInlineHelp(updateStatus.Message, PackageInstallerVisualStatusKind.Info);
            }
            else if (updateStatus.Kind == PackageUpdateStatusKind.Failed && !string.IsNullOrWhiteSpace(updateStatus.Message))
            {
                ImGui.DrawInlineHelp(updateStatus.Message, PackageInstallerVisualStatusKind.Failed);
            }
        }

        private void DrawChannelPanel(PackageDefinition packageDefinition)
        {
            ImGui.DrawPanel("Channel", () =>
            {
                PackageChannel selectedChannel = GetSelectedChannel(packageDefinition);
                string selectedUrl = packageDefinition.GetUrl(selectedChannel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "Selected",
                        _styles.MutedMiniLabelStyle,
                        GUILayout.Width(DeucarianEditorWorkbenchGUI.DetailLabelWidth));
                    DrawChannelPopup(packageDefinition);
                    GUILayout.Space(6f);
                    ImGui.DrawStatusBadge(PackageChannelPolicy.GetChannelLabel(selectedChannel), PackageInstallerVisualStatusKind.Info, GUILayout.Width(104f));
                    GUILayout.FlexibleSpace();
                }

                GUILayout.Space(6f);

                if (!string.IsNullOrWhiteSpace(selectedUrl))
                {
                    EditorGUILayout.LabelField(
                        PackageChannelPolicy.GetChannelLabel(selectedChannel) + " installs from the configured package URL/ref.",
                        _styles.MutedMiniLabelStyle);
                }
                else
                {
                    ImGui.DrawInlineHelp("No package URL is configured for this channel.", PackageInstallerVisualStatusKind.Failed);
                }

                ImGui.DrawKeyValueRow("Stable", string.IsNullOrWhiteSpace(packageDefinition.StableUrl) ? "Not configured" : "Configured");
                ImGui.DrawKeyValueRow("Development", string.IsNullOrWhiteSpace(packageDefinition.DevelopmentUrl) ? "Not configured" : "Configured");

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
                string provenance = PackageChannelPolicy.GetContextualChannelProvenance(
                    packageDefinition,
                    projectSelection,
                    packageSelection,
                    hasInstalledChannel,
                    installedChannel,
                    installedSourceReason);

                if (!string.IsNullOrWhiteSpace(provenance))
                {
                    GUILayout.Space(6f);
                    ImGui.DrawKeyValueRow("Source", provenance);

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

            ImGui.DrawPanel("Requirements", () =>
            {
                if (packageDefinition.Dependencies.Count > 0)
                {
                    EditorGUILayout.LabelField("Dependencies", _styles.MiniLabelStyle);

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

                    EditorGUILayout.LabelField("Required by", _styles.MiniLabelStyle);

                    foreach (PackageReverseDependency dependent in dependents)
                    {
                        ImGui.DrawKeyValueRow(
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
                ImGui.DrawKeyValueRow(dependencyId, "Not registered");
                return;
            }

            VisualStatus status = GetPackageVisualStatus(dependencyDefinition);
            Rect rowRect = GUILayoutUtility.GetRect(1f, 28f, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                DeucarianEditorVisualShell.DrawInsetSurface(rowRect, _styles.SampleRowBackgroundColor, _styles.SeparatorColor, 6f);
            }

            Rect markerRect = new Rect(rowRect.x + 8f, rowRect.y + 5f, 28f, 18f);
            ImGui.DrawInlineIcon(markerRect, status.IconId, status.Kind, status.Label);

            Rect nameRect = new Rect(rowRect.x + 44f, rowRect.y + 5f, rowRect.width - 164f, 18f);
            GUI.Label(
                nameRect,
                new GUIContent(dependencyDefinition.DisplayName, GetPackageTooltip(dependencyDefinition)),
                _styles.RowTitleStyle);

            Rect statusRect = new Rect(rowRect.xMax - 108f, rowRect.y + 5f, 96f, 18f);
            ImGui.DrawStatusBadge(statusRect, status.Label, status.Kind, _styles.RowStatusStyle);
        }

        private void DrawActionsPanel(PackageDefinition packageDefinition)
        {
            ImGui.DrawPanel("Actions", () =>
            {
                DrawPackageActionButtons(packageDefinition, true);
            });
        }
    }
}
