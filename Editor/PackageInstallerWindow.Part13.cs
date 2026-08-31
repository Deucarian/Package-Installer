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

            DrawStatusBadge(status.Label, status.Kind, GUILayout.Width(150f));
            GUILayout.Space(6f);
            DrawKeyValueRow("Domain", GetPackageHierarchyPath(packageDefinition));
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

            if (updateStatus.HasUnbumpedPackageVersionWarning)
            {
                DrawInlineHelp(updateStatus.PackageVersionWarningMessage, VisualStatusKind.UpdateAvailable);
            }
            else if ((updateStatus.IsSourceMigrationAvailable || updateStatus.IsReloadPending) &&
                     !string.IsNullOrWhiteSpace(updateStatus.Message))
            {
                DrawInlineHelp(updateStatus.Message, VisualStatusKind.UpdateAvailable);
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
                    EditorGUILayout.LabelField(
                        "Selected",
                        _mutedMiniLabelStyle,
                        GUILayout.Width(DeucarianEditorWorkbenchGUI.DetailLabelWidth));
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
                string provenance = GetContextualChannelProvenance(
                    packageDefinition,
                    projectSelection,
                    packageSelection,
                    hasInstalledChannel,
                    installedChannel,
                    installedSourceReason);

                if (!string.IsNullOrWhiteSpace(provenance))
                {
                    GUILayout.Space(6f);
                    DrawKeyValueRow("Source", provenance);

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

        internal static string GetContextualChannelProvenance(
            PackageDefinition packageDefinition,
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection,
            bool hasInstalledChannel,
            PackageChannel installedChannel,
            string installedSourceReason)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            if (hasInstalledChannel && installedChannel == PackageChannel.Custom)
            {
                return string.IsNullOrWhiteSpace(installedSourceReason)
                    ? "Custom installed source"
                    : "Custom installed source - " + installedSourceReason.Trim();
            }

            PackageChannelSelection explicitSelection = GetLatestExplicitChannelSelection(
                projectSelection,
                packageSelection);

            if (!explicitSelection.HasValue)
            {
                return string.Empty;
            }

            bool packageOverride = packageSelection.HasValue &&
                                   (!projectSelection.HasValue ||
                                    packageSelection.ChangedAtUtcTicks > projectSelection.ChangedAtUtcTicks);
            string scope = packageOverride ? "Package override" : "Project override";

            if (explicitSelection.Channel == PackageChannel.Development &&
                !packageDefinition.HasDevelopmentUrl)
            {
                return scope + " requested Development - using Stable fallback";
            }

            return scope + " - " + GetChannelLabel(explicitSelection.Channel);
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

            DrawPanel("Requirements", () =>
            {
                if (packageDefinition.Dependencies.Count > 0)
                {
                    EditorGUILayout.LabelField("Dependencies", _miniLabelStyle);

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

                    EditorGUILayout.LabelField("Required by", _miniLabelStyle);

                    foreach (PackageReverseDependency dependent in dependents)
                    {
                        DrawKeyValueRow(
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
            DrawInlineIcon(markerRect, status.IconId, status.Kind, status.Label);

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
    }
}
