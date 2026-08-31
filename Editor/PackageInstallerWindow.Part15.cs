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


        private void RemovePackage(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return;
            }

            IReadOnlyList<PackageReverseDependency> dependents =
                ResolveInstalledDependents(packageDefinition);
            string dependentWarning = BuildRemoveDependentWarning(dependents);
            var removeAction = new DeucarianEditorDialogAction(
                "remove",
                "Remove",
                DeucarianEditorIconIds.Remove,
                DeucarianEditorDialogActionStyle.Destructive);
            var cancelAction = new DeucarianEditorDialogAction(
                "cancel",
                "Cancel",
                DeucarianEditorIconIds.Stop);
            var options = new DeucarianEditorDialogOptions(
                "Remove Package",
                "Remove " + packageDefinition.DisplayName + " from this Unity project?",
                DeucarianEditorIconIds.Remove,
                new[] { removeAction, cancelAction })
            {
                Details = dependentWarning.Trim(),
                DefaultActionId = cancelAction.Id,
                CancelActionId = cancelAction.Id
            };
            TryShowManagedDialog(options, result =>
            {
                if (result.WasCanceled ||
                    !string.Equals(result.ActionId, removeAction.Id, StringComparison.Ordinal) ||
                    this == null)
                {
                    return;
                }

                if (_packageInstallService == null ||
                    _packageInstallService.IsBusy ||
                    _packageDetectionService == null ||
                    !_packageDetectionService.IsInstalled(packageDefinition.PackageId))
                {
                    RecordStaleConfirmation(
                        "Remove " + packageDefinition.DisplayName,
                        "Package state changed while the removal confirmation was open.");
                    return;
                }

                _packageInstallService.Remove(packageDefinition);
                _packageUpdateCheckService.Invalidate(packageDefinition.PackageId);
            });
        }

        private IReadOnlyList<PackageReverseDependency> ResolveInstalledDependents(
            PackageDefinition packageDefinition)
        {
            if (packageDefinition == null || _packageReverseDependencyResolver == null)
            {
                return Array.Empty<PackageReverseDependency>();
            }

            return _packageReverseDependencyResolver.Resolve(
                packageDefinition.PackageId,
                _packageDetectionService?.InstalledPackageIds);
        }

        internal static bool CanRemovePackageForTests(
            IReadOnlyList<PackageReverseDependency> installedDependents,
            bool queuedOrInstalling,
            bool actionsBusy)
        {
            return CanRemovePackage(installedDependents, queuedOrInstalling, actionsBusy);
        }

        private static bool CanRemovePackage(
            IReadOnlyList<PackageReverseDependency> installedDependents,
            bool queuedOrInstalling,
            bool actionsBusy)
        {
            // Installed dependents are a removal warning, not a hidden hard block.
            // Unity still permits removal and the confirmation dialog must remain reachable.
            return !queuedOrInstalling && !actionsBusy;
        }

        internal static string BuildRemoveDependentWarningForTests(
            IReadOnlyList<PackageReverseDependency> dependents)
        {
            return BuildRemoveDependentWarning(dependents);
        }

        private static string BuildRemoveDependentWarning(
            IReadOnlyList<PackageReverseDependency> dependents)
        {
            return dependents == null || dependents.Count == 0
                ? string.Empty
                : "\n\nInstalled packages that currently depend on it:\n" +
                  string.Join("\n", dependents
                      .Select(dependent => "- " + dependent.DisplayName + " (" + dependent.PackageId + ")")
                      .ToArray()) +
                  "\n\nRemoving it may break those packages.";
        }

        private void DrawTemplateCompositionPanel(PackageDefinition templateDefinition)
        {
            HashSet<string> explicitSelection =
                GetOrCreateTemplateCompositionSelection(templateDefinition);
            HashSet<string> resolvedSelection = ResolveTemplateCompositionPackageIds(
                templateDefinition,
                explicitSelection);

            DrawPanel("Viewer Setup", () =>
            {
                EditorGUILayout.LabelField(
                    "Choose the reusable viewer core, then add only the connection your project needs.",
                    _mutedMiniLabelStyle);
                GUILayout.Space(8f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (PackageCompositionPresetDefinition preset in
                             templateDefinition.CompositionPresets)
                    {
                        bool selectedPreset = IsTemplateCompositionPresetSelected(
                            templateDefinition,
                            preset);
                        bool pressed = GUILayout.Toggle(
                            selectedPreset,
                            new GUIContent(preset.DisplayName, preset.Description),
                            EditorStyles.miniButton,
                            GUILayout.MinWidth(96f));
                        if (pressed && !selectedPreset)
                        {
                            ApplyTemplateCompositionPreset(templateDefinition, preset);
                            explicitSelection = GetOrCreateTemplateCompositionSelection(
                                templateDefinition);
                            resolvedSelection = ResolveTemplateCompositionPackageIds(
                                templateDefinition,
                                explicitSelection);
                        }
                    }
                }

                string activePresetDescription = GetActiveTemplateCompositionDescription(
                    templateDefinition);
                if (!string.IsNullOrWhiteSpace(activePresetDescription))
                {
                    GUILayout.Space(5f);
                    EditorGUILayout.LabelField(activePresetDescription, _mutedMiniLabelStyle);
                }

                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Connections", _miniLabelStyle);
                GUILayout.Space(3f);

                foreach (string companionId in templateDefinition.OptionalCompanions)
                {
                    if (!PackageRegistryProvider.TryGetPackage(
                            companionId,
                            out PackageDefinition companionDefinition))
                    {
                        DrawInlineHelp(
                            "Optional connection is unavailable: " + companionId,
                            VisualStatusKind.Failed);
                        continue;
                    }

                    bool requiredByAnotherSelection = IsRequiredByAnotherTemplateSelection(
                        templateDefinition,
                        explicitSelection,
                        companionId);
                    bool currentValue = resolvedSelection.Contains(companionId);
                    bool nextValue;
                    using (new EditorGUI.DisabledScope(requiredByAnotherSelection))
                    {
                        string suffix = requiredByAnotherSelection
                            ? "  (included automatically)"
                            : _packageDetectionService.IsInstalled(companionId)
                                ? "  (installed)"
                                : string.Empty;
                        nextValue = EditorGUILayout.ToggleLeft(
                            new GUIContent(
                                companionDefinition.DisplayName + suffix,
                                GetPackageTooltip(companionDefinition)),
                            currentValue);
                    }

                    if (!requiredByAnotherSelection && nextValue != currentValue)
                    {
                        if (nextValue)
                        {
                            explicitSelection.Add(companionId);
                        }
                        else
                        {
                            explicitSelection.Remove(companionId);
                        }

                        _templateCompositionPresetIds[templateDefinition.PackageId] = string.Empty;
                        resolvedSelection = ResolveTemplateCompositionPackageIds(
                            templateDefinition,
                            explicitSelection);
                    }
                }

                GUILayout.Space(5f);
                string selectionSummary = resolvedSelection.Count == 0
                    ? "Core viewer only"
                    : "Core viewer + " + string.Join(
                        " + ",
                        resolvedSelection
                            .Select(id => PackageRegistryProvider.TryGetPackage(
                                    id,
                                    out PackageDefinition selectedPackage)
                                ? selectedPackage.DisplayName
                                : id)
                            .ToArray());
                DrawInlineHelp(selectionSummary, VisualStatusKind.Info);
            });
        }

        private HashSet<string> GetOrCreateTemplateCompositionSelection(
            PackageDefinition templateDefinition)
        {
            if (_templateCompositionSelections.TryGetValue(
                    templateDefinition.PackageId,
                    out HashSet<string> selection))
            {
                return selection;
            }

            selection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _templateCompositionSelections[templateDefinition.PackageId] = selection;

            PackageCompositionPresetDefinition initialPreset =
                templateDefinition.CompositionPresets.FirstOrDefault(preset => preset.Recommended) ??
                templateDefinition.CompositionPresets.FirstOrDefault();
            if (initialPreset != null)
            {
                foreach (string packageId in initialPreset.PackageIds)
                {
                    selection.Add(packageId);
                }

                _templateCompositionPresetIds[templateDefinition.PackageId] = initialPreset.Id;
            }

            return selection;
        }

        private bool IsTemplateCompositionPresetSelected(
            PackageDefinition templateDefinition,
            PackageCompositionPresetDefinition preset)
        {
            GetOrCreateTemplateCompositionSelection(templateDefinition);
            return _templateCompositionPresetIds.TryGetValue(
                       templateDefinition.PackageId,
                       out string selectedPresetId) &&
                   string.Equals(selectedPresetId, preset.Id, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyTemplateCompositionPreset(
            PackageDefinition templateDefinition,
            PackageCompositionPresetDefinition preset)
        {
            HashSet<string> selection = GetOrCreateTemplateCompositionSelection(
                templateDefinition);
            selection.Clear();
            foreach (string packageId in preset.PackageIds)
            {
                selection.Add(packageId);
            }

            _templateCompositionPresetIds[templateDefinition.PackageId] = preset.Id;
        }

        private string GetActiveTemplateCompositionDescription(
            PackageDefinition templateDefinition)
        {
            GetOrCreateTemplateCompositionSelection(templateDefinition);
            if (!_templateCompositionPresetIds.TryGetValue(
                    templateDefinition.PackageId,
                    out string selectedPresetId) ||
                string.IsNullOrWhiteSpace(selectedPresetId))
            {
                return "Custom setup";
            }

            PackageCompositionPresetDefinition preset = templateDefinition.CompositionPresets
                .FirstOrDefault(candidate => string.Equals(
                    candidate.Id,
                    selectedPresetId,
                    StringComparison.OrdinalIgnoreCase));
            return preset?.Description ?? string.Empty;
        }

        private static bool IsRequiredByAnotherTemplateSelection(
            PackageDefinition templateDefinition,
            IEnumerable<string> explicitSelection,
            string companionId)
        {
            HashSet<string> otherSelection = new HashSet<string>(
                explicitSelection ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            otherSelection.Remove(companionId);
            return ResolveTemplateCompositionPackageIds(
                templateDefinition,
                otherSelection).Contains(companionId);
        }

        private static HashSet<string> ResolveTemplateCompositionPackageIds(
            PackageDefinition templateDefinition,
            IEnumerable<string> explicitSelection)
        {
            HashSet<string> optionalIds = new HashSet<string>(
                templateDefinition?.OptionalCompanions ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> resolved = new HashSet<string>(
                (explicitSelection ?? Array.Empty<string>()).Where(optionalIds.Contains),
                StringComparer.OrdinalIgnoreCase);
            Queue<string> pending = new Queue<string>(resolved);

            while (pending.Count > 0)
            {
                string packageId = pending.Dequeue();
                if (!PackageRegistryProvider.TryGetPackage(
                        packageId,
                        out PackageDefinition packageDefinition))
                {
                    continue;
                }

                foreach (string dependencyId in packageDefinition.Dependencies)
                {
                    if (optionalIds.Contains(dependencyId) && resolved.Add(dependencyId))
                    {
                        pending.Enqueue(dependencyId);
                    }
                }
            }

            return resolved;
        }

        private PackageDefinition[] GetTemplateCompositionRoots(
            PackageDefinition templateDefinition,
            bool includeTemplate)
        {
            HashSet<string> resolvedSelection = ResolveTemplateCompositionPackageIds(
                templateDefinition,
                GetOrCreateTemplateCompositionSelection(templateDefinition));
            List<PackageDefinition> roots = new List<PackageDefinition>();
            if (includeTemplate)
            {
                roots.Add(templateDefinition);
            }

            foreach (string packageId in resolvedSelection)
            {
                if (PackageRegistryProvider.TryGetPackage(
                        packageId,
                        out PackageDefinition packageDefinition))
                {
                    roots.Add(packageDefinition);
                }
            }

            return roots.ToArray();
        }

        private void InstallTemplateComposition(
            PackageDefinition templateDefinition,
            bool includeTemplate)
        {
            PackageDefinition[] roots = GetTemplateCompositionRoots(
                templateDefinition,
                includeTemplate)
                .Where(package => !_packageDetectionService.IsInstalled(package.PackageId))
                .ToArray();
            if (roots.Length == 0)
            {
                return;
            }

            _activeActionKind = PackageInstallerActionKind.InstallAll;
            _cancelingActionKind = PackageInstallerActionKind.None;
            _packageDependencyInstaller.InstallManyWithDependencies(
                roots,
                GetSelectedChannel,
                "Install " + templateDefinition.DisplayName + " setup");
            ClearActiveActionIfIdle();
            UpdateViewVisibility();
        }
    }
}
