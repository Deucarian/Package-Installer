using System;
using System.Linq;
using Deucarian.Editor;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {
        private sealed partial class InstallerPackageDetails
        {
            private void BuildComposition()
            {
                var section = form.Section("Viewer setup", true);
                var presets = package.CompositionPresets;
                section.Choice("installer-composition-preset", "Setup", new[] { "Custom setup" }.Concat(presets.Select(preset => preset.DisplayName)).ToArray(),
                    () => 1 + presets.ToList().FindIndex(preset => owner.IsTemplateCompositionPresetSelected(package, preset)), value =>
                    {
                        if (!CanChange) return;
                        if (value > 0) owner.ApplyTemplateCompositionPreset(package, presets[value - 1]);
                        else owner._templateCompositionPresetIds[package.PackageId] = string.Empty;
                        Refresh();
                    });
                section.Note(() => owner.GetActiveTemplateCompositionDescription(package));
                foreach (string id in package.OptionalCompanions)
                {
                    if (!PackageRegistryProvider.TryGetPackage(id, out var companion))
                    { section.Note(() => "Unavailable connection: " + id); continue; }
                    bool Required() => IsRequiredByAnotherTemplateSelection(package, owner.GetOrCreateTemplateCompositionSelection(package), id);
                    var toggle = section.Toggle("installer-connection-" + id, companion.DisplayName,
                        () => ResolveTemplateCompositionPackageIds(package, owner.GetOrCreateTemplateCompositionSelection(package)).Contains(id), value =>
                        {
                            if (!CanChange || Required()) return;
                            var selection = owner.GetOrCreateTemplateCompositionSelection(package);
                            if (value) selection.Add(id); else selection.Remove(id);
                            owner._templateCompositionPresetIds[package.PackageId] = string.Empty;
                            Refresh();
                        });
                    refreshers.Add(() => { toggle.SetEnabled(CanChange && !Required()); toggle.tooltip = Required() ? "Included by another selected connection" : companion.Description; });
                }
                section.Action("installer-install-connections", "Install selected connections", () => owner.InstallTemplateComposition(package, false),
                    () => CanChange && Installed && owner.GetTemplateCompositionRoots(package, false).Any(item => !owner._packageDetectionService.IsInstalled(item.PackageId)));
                section.EnabledWhen(() => CanChange);
            }

            private void BuildCompanions()
            {
                if (package.OptionalCompanions.Count == 0) return;
                var section = form.Section("Optional companions", true);
                foreach (string id in package.OptionalCompanions)
                {
                    if (!PackageRegistryProvider.TryGetPackage(id, out var companion))
                    { section.Note(() => "Unavailable companion: " + id); continue; }
                    var button = section.Action("installer-companion-" + id, "Install " + companion.DisplayName,
                        () => owner._packageDependencyInstaller.InstallWithDependencies(companion, owner.GetSelectedChannel),
                        () => CanChange && !owner._packageDetectionService.IsInstalled(id) && !owner._packageInstallService.IsQueuedOrInstalling(id));
                    button.tooltip = GetOptionalCompanionDescription(companion);
                    refreshers.Add(() => button.text = (owner._packageDetectionService.IsInstalled(id) ? "Installed · " : "Install ") + companion.DisplayName);
                }
            }

            private void BuildSamples()
            {
                var section = form.Section("Samples", true);
                if (!owner._packageDetectionService.TryGetInstalledPackage(package.PackageId, out var info))
                { section.Note(() => "Install this package to discover and import its samples."); return; }
                var samples = MergeSampleDefinitions(package.Extras, owner._packageSampleDiscoveryService.GetSamples(info));
                if (samples.Length == 0) { section.Note(() => "This package has no samples."); return; }
                foreach (var sample in samples)
                {
                    bool Imported() => IsImportedSampleStatus(owner._packageSampleImportService.GetStatus(package, sample, info)) ||
                        owner._packageSampleImportService.IsSampleImported(package, sample, info);
                    var button = section.Action("installer-sample-" + sample.SamplePath, "Import " + sample.DisplayName,
                        () => owner._packageSampleImportService.ImportSample(package, sample, info), () => CanChange && !Imported());
                    refreshers.Add(() =>
                    {
                        button.text = (Imported() ? "Imported · " : "Import ") + sample.DisplayName;
                        button.tooltip = sample.Description + "\n" + GetSampleImportStatusText(owner._packageSampleImportService.GetStatus(package, sample, info));
                    });
                }
            }
        }
    }
}
