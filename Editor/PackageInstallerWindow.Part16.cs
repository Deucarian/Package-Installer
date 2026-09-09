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

        private void DrawOptionalCompanionsPanel(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null || packageDefinition.OptionalCompanions.Count == 0)
            {
                return;
            }

            ImGui.DrawPanel("Optional Companions", () =>
            {
                DeucarianEditorTextGUI.LabelField("Install optional tooling that enhances this package without becoming a required dependency.", _styles.MutedMiniLabelStyle);
                GUILayout.Space(6f);

                foreach (string companionId in packageDefinition.OptionalCompanions)
                {
                    if (!PackageRegistryProvider.TryGetPackage(companionId, out PackageDefinition companionDefinition))
                    {
                        ImGui.DrawInlineHelp("Optional companion is unavailable: " + companionId, PackageInstallerVisualStatusKind.Failed);
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

            Rect rect = ImGui.BeginSurface(
                _styles.SampleRowStyle,
                _styles.SampleRowBackgroundColor,
                _styles.SeparatorColor,
                GUILayout.ExpandWidth(true));

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect markerRect = GUILayoutUtility.GetRect(30f, 30f, GUILayout.Width(30f), GUILayout.Height(30f));
                ImGui.DrawInlineIcon(markerRect, status.IconId, status.Kind, status.Label);

                GUILayout.Space(8f);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    DeucarianEditorTextGUI.LabelField(
                        new GUIContent(companionDefinition.DisplayName, GetPackageTooltip(companionDefinition)),
                        _styles.RowTitleStyle);

                    string description = GetOptionalCompanionDescription(companionDefinition);
                    DeucarianEditorTextGUI.LabelField(
                        new GUIContent(description, description),
                        _styles.MutedMiniLabelStyle);
                }

                GUILayout.Space(8f);

                string label = installed
                    ? "Installed"
                    : companionDefinition.PackageId == "com.deucarian.diagnostics"
                        ? "Install Diagnostics"
                        : "Install";
                if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                        installed
                            ? DeucarianEditorIconIds.PackageCheck
                            : DeucarianEditorIconIds.Download,
                        label,
                        installed
                            ? "This optional companion is installed."
                            : "Install this optional companion.",
                        !installed && !queuedOrInstalling && !actionsBusy,
                        GUILayout.Width(164f)))
                {
                    _packageDependencyInstaller.InstallWithDependencies(companionDefinition, GetSelectedChannel);
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
            ImGui.DrawPanel("Extras / Samples", () =>
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
                        DeucarianEditorTextGUI.LabelField("Install this package to discover package samples.", _styles.MutedMiniLabelStyle);
                    }
                    else
                    {
                        ImGui.DrawInlineHelp("Install this package before importing samples.", PackageInstallerVisualStatusKind.Info);
                    }

                    return;
                }

                if (sampleDefinitions.Length == 0)
                {
                    DeucarianEditorTextGUI.LabelField("No package samples declared in package.json.", _styles.MutedMiniLabelStyle);
                    return;
                }

                DeucarianEditorTextGUI.LabelField("Import optional samples and examples for this package.", _styles.MutedMiniLabelStyle);
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
            Rect rect = ImGui.BeginSurface(
                _styles.SampleRowStyle,
                _styles.SampleRowBackgroundColor,
                _styles.SeparatorColor,
                GUILayout.ExpandWidth(true));

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect markerRect = GUILayoutUtility.GetRect(30f, 30f, GUILayout.Width(30f), GUILayout.Height(30f));
                ImGui.DrawInlineIcon(
                    markerRect,
                    DeucarianEditorIconIds.Sample,
                    PackageInstallerVisualStatusKind.Info,
                    "Package sample");

                GUILayout.Space(8f);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    DeucarianEditorTextGUI.LabelField(
                        new GUIContent(extraDefinition.DisplayName, extraDefinition.DisplayName),
                        _styles.RowTitleStyle);

                    if (!string.IsNullOrWhiteSpace(extraDefinition.Description))
                    {
                        DeucarianEditorTextGUI.LabelField(
                            new GUIContent(extraDefinition.Description, extraDefinition.Description),
                            _styles.MutedMiniLabelStyle);
                    }

                    string statusText = GetSampleImportStatusText(status);

                    if (!string.IsNullOrWhiteSpace(statusText))
                    {
                        ImGui.DrawColoredLabel(
                            statusText,
                            _styles.MutedMiniLabelStyle,
                            PackageInstallerStatusPresentation.GetStatusColor(GetSampleImportStatusKind(status)));
                    }
                }

                bool alreadyImported = IsImportedSampleStatus(status) ||
                                       _packageSampleImportService.IsSampleImported(
                                           packageDefinition,
                                           extraDefinition,
                                           packageInfo);

                string buttonLabel = alreadyImported ? "Imported" : "Import";
                if (DeucarianEditorWorkbenchGUI.DrawCompactIconAction(
                        alreadyImported
                            ? DeucarianEditorIconIds.Success
                            : DeucarianEditorIconIds.Download,
                        buttonLabel,
                        alreadyImported
                            ? "This sample has already been imported."
                            : "Import this package sample.",
                        !alreadyImported && !IsAnyOperationBusy(),
                        GUILayout.Width(108f)))
                {
                    _packageSampleImportService.ImportSample(
                        packageDefinition,
                        extraDefinition,
                        packageInfo);
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

            ImGui.DrawPanel(null, () =>
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

            ImGui.DrawSelectableValue("Package ID", packageDefinition.PackageId);
            ImGui.DrawSelectableValue("Domain", GetPackageHierarchyPath(packageDefinition));
            ImGui.DrawSelectableValue("Package kind", GetPackageKindDisplayName(packageDefinition));
            ImGui.DrawSelectableValue("Selected URL", packageDefinition.GetUrl(selectedChannel));
            ImGui.DrawSelectableValue("Stable URL", packageDefinition.StableUrl);
            ImGui.DrawSelectableValue("Development URL", packageDefinition.DevelopmentUrl);
            ImGui.DrawSelectableValue("Selected ref", PackageChannelPolicy.GetChannelLabel(selectedChannel));

            if (_packageDetectionService.TryGetInstalledPackage(
                    packageDefinition.PackageId,
                    out PackageManagerPackageInfo packageInfo))
            {
                ImGui.DrawSelectableValue("Installed source", packageInfo.source.ToString());
                ImGui.DrawSelectableValue("Installed version", packageInfo.version);
                ImGui.DrawSelectableValue("Installed path", packageInfo.resolvedPath);
            }

            if (_packageDetectionService.TryGetInstalledPackageReference(
                    packageDefinition.PackageId,
                    out string installedReference))
            {
                ImGui.DrawSelectableValue("Installed ref", installedReference);
            }

            ImGui.DrawSelectableValue("Installed rev", updateStatus.InstalledRevision);
            ImGui.DrawSelectableValue("Latest rev", updateStatus.LatestRevision);
            ImGui.DrawSelectableValue("Installed version", updateStatus.InstalledVersion);
            ImGui.DrawSelectableValue("Target version", updateStatus.LatestVersion);
            ImGui.DrawSelectableValue("Dependencies", packageDefinition.Dependencies.Count == 0
                ? "-"
                : string.Join(", ", packageDefinition.Dependencies.ToArray()));
            ImGui.DrawSelectableValue("Optional companions", packageDefinition.OptionalCompanions.Count == 0
                ? "-"
                : string.Join(", ", packageDefinition.OptionalCompanions.ToArray()));

            if (!string.IsNullOrWhiteSpace(updateStatus.Message))
            {
                ImGui.DrawSelectableValue("State", updateStatus.Message);
            }
        }

        private bool DrawAdvancedFoldout(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            bool expanded = _preferences.IsAdvancedExpanded(key);

            bool nextExpanded = DeucarianEditorInputGUI.Foldout(expanded, "Advanced", true, _styles.FoldoutStyle);

            if (nextExpanded != expanded)
            {
                _preferences.SetAdvancedExpanded(key, nextExpanded);
            }

            return nextExpanded;
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
    }
}
