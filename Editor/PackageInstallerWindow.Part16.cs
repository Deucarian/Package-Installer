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

            DrawPanel("Optional Companions", () =>
            {
                EditorGUILayout.LabelField("Install optional tooling that enhances this package without becoming a required dependency.", _mutedMiniLabelStyle);
                GUILayout.Space(6f);

                foreach (string companionId in packageDefinition.OptionalCompanions)
                {
                    if (!PackageRegistryProvider.TryGetPackage(companionId, out PackageDefinition companionDefinition))
                    {
                        DrawInlineHelp("Optional companion is unavailable: " + companionId, VisualStatusKind.Failed);
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

            Rect rect = BeginSurface(
                _sampleRowStyle,
                _sampleRowBackgroundColor,
                _separatorColor,
                GUILayout.ExpandWidth(true));

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect markerRect = GUILayoutUtility.GetRect(30f, 30f, GUILayout.Width(30f), GUILayout.Height(30f));
                DrawInlineIcon(markerRect, status.IconId, status.Kind, status.Label);

                GUILayout.Space(8f);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(companionDefinition.DisplayName, GetPackageTooltip(companionDefinition)),
                        _rowTitleStyle);

                    string description = GetOptionalCompanionDescription(companionDefinition);
                    EditorGUILayout.LabelField(
                        new GUIContent(description, description),
                        _mutedMiniLabelStyle);
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
                DrawInlineIcon(
                    markerRect,
                    DeucarianEditorIconIds.Sample,
                    VisualStatusKind.Info,
                    "Package sample");

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
            DrawSelectableValue("Domain", GetPackageHierarchyPath(packageDefinition));
            DrawSelectableValue("Package kind", GetPackageKindDisplayName(packageDefinition));
            DrawSelectableValue("Selected URL", packageDefinition.GetUrl(selectedChannel));
            DrawSelectableValue("Stable URL", packageDefinition.StableUrl);
            DrawSelectableValue("Development URL", packageDefinition.DevelopmentUrl);
            DrawSelectableValue("Selected ref", GetChannelLabel(selectedChannel));

            if (_packageDetectionService.TryGetInstalledPackage(
                    packageDefinition.PackageId,
                    out PackageManagerPackageInfo packageInfo))
            {
                DrawSelectableValue("Installed source", packageInfo.source.ToString());
                DrawSelectableValue("Installed version", packageInfo.version);
                DrawSelectableValue("Installed path", packageInfo.resolvedPath);
            }

            if (_packageDetectionService.TryGetInstalledPackageReference(
                    packageDefinition.PackageId,
                    out string installedReference))
            {
                DrawSelectableValue("Installed ref", installedReference);
            }

            DrawSelectableValue("Installed rev", updateStatus.InstalledRevision);
            DrawSelectableValue("Latest rev", updateStatus.LatestRevision);
            DrawSelectableValue("Installed version", updateStatus.InstalledVersion);
            DrawSelectableValue("Target version", updateStatus.LatestVersion);
            DrawSelectableValue("Dependencies", packageDefinition.Dependencies.Count == 0
                ? "-"
                : string.Join(", ", packageDefinition.Dependencies.ToArray()));
            DrawSelectableValue("Optional companions", packageDefinition.OptionalCompanions.Count == 0
                ? "-"
                : string.Join(", ", packageDefinition.OptionalCompanions.ToArray()));

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

            if (!_advancedFoldouts.TryGetValue(key, out bool expanded))
            {
                expanded = EditorPrefs.GetBool(GetAdvancedFoldoutPreferenceKey(key), false);
                _advancedFoldouts[key] = expanded;
            }

            bool nextExpanded = EditorGUILayout.Foldout(expanded, "Advanced", true, _foldoutStyle);

            if (nextExpanded != expanded)
            {
                _advancedFoldouts[key] = nextExpanded;
                EditorPrefs.SetBool(GetAdvancedFoldoutPreferenceKey(key), nextExpanded);
            }

            return nextExpanded;
        }

        private string GetAdvancedFoldoutPreferenceKey(string packageId)
        {
            return AdvancedFoldoutPreferencePrefix +
                   Application.dataPath.Replace("\\", "/") +
                   "." +
                   packageId;
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
