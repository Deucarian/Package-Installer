using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {
        private static string PackageTitle(PackageDefinition package) =>
            package.DisplayName.StartsWith("Deucarian ", StringComparison.Ordinal)
                ? package.DisplayName.Substring("Deucarian ".Length) : package.DisplayName;

        private static string PackageIcon(PackageDefinition package) =>
            DeucarianEditorIcons.GetPackageIcon(GetPackageIconKey(package))?.name ?? DeucarianEditorIconIds.Package;

        private sealed partial class InstallerPackageDetails
        {
            private readonly PackageInstallerWindow owner;
            private readonly PackageDefinition package;
            private readonly DeucarianEditorWorkspaceForm form;
            private readonly List<Action> refreshers = new List<Action>();
            private PackageUpdateStatus Status => owner._packageUpdateCheckService.GetStatus(package, owner.GetSelectedChannel(package));
            private bool Installed => owner._packageDetectionService.IsInstalled(package.PackageId);
            private bool CanChange => !owner.IsAnyOperationBusy() && !owner._packageInstallService.IsQueuedOrInstalling(package.PackageId);

            internal InstallerPackageDetails(PackageInstallerWindow owner, VisualElement root, PackageDefinition package)
            {
                this.owner = owner; this.package = package;
                root.Clear(); form = new DeucarianEditorWorkspaceForm(root);
                if (package == null)
                {
                    form.Section("Package details").Note(() => "Select a package to review its source and available actions.");
                    return;
                }
                var heading = DeucarianEditorWorkspaceControls.Region(null, "dw-detail-heading");
                heading.Add(DeucarianEditorWorkspaceControls.Icon(PackageIcon(package)));
                heading.Add(DeucarianEditorWorkspaceControls.Label(PackageTitle(package), "dw-section-title")); root.Add(heading);
                form.Note(() => package.Description);
                form.ReadOnly("installer-selected-version", "Installed", () => owner._packageDetectionService.TryGetInstalledPackage(package.PackageId, out var info) ? info.version : "Not installed");
                form.ReadOnly("installer-selected-available", "Available", () => string.IsNullOrWhiteSpace(Status.LatestVersion) ? "Check updates" : Status.LatestVersion);
                form.ReadOnly("installer-selected-update", "Status", () => GetUpdateStatusText(Status));
                BuildRequirements();
                if (package.IsTemplate && package.CompositionPresets.Count > 0) BuildComposition();
                BuildActions();
                if (!package.IsTemplate || package.CompositionPresets.Count == 0) BuildCompanions();
                BuildSamples();
                BuildSource();
                Refresh();
            }

            internal void Refresh()
            {
                form.Refresh(); foreach (var refresh in refreshers) refresh();
            }

            private void BuildRequirements()
            {
                var dependents = owner.ResolveInstalledDependents(package);
                if (package.Dependencies.Count == 0 && dependents.Count == 0) return;
                var section = form.Section("Dependencies", true);
                foreach (string id in package.Dependencies)
                {
                    if (!PackageRegistryProvider.TryGetPackage(id, out var dependency))
                    { section.ReadOnly("dependency-" + id, id, () => "Not registered"); continue; }
                    var button = section.Action("dependency-" + id, dependency.DisplayName, () =>
                        owner.SelectDefinition(dependency, dependency.IsIntegration ? SelectionKind.Integration : SelectionKind.Package, false));
                    refreshers.Add(() => button.text = dependency.DisplayName + " · " + owner.GetPackageVisualStatus(dependency).Label);
                }
                foreach (var dependent in dependents)
                    section.ReadOnly("dependent-" + dependent.PackageId, "Required by", () => dependent.DisplayName);
            }

            private void BuildActions()
            {
                var note = DeucarianEditorWorkspaceControls.Label(string.Empty, "dw-note"); form.Root.Add(note);
                var primary = form.Action("installer-package-primary", "Install", () =>
                {
                    if (!CanChange) return;
                    if (Installed) { if (HasPrimaryPackageAction(Status)) owner.RunPrimaryPackageAction(package, Status); }
                    else if (package.IsTemplate && package.CompositionPresets.Count > 0) owner.InstallTemplateComposition(package, true);
                    else owner._packageDependencyInstaller.InstallWithDependencies(package, owner.GetSelectedChannel);
                }, () => CanChange && (!Installed || HasPrimaryPackageAction(Status)), true);
                refreshers.Add(() =>
                {
                    primary.text = Installed ? GetUpdateActionLabel(Status, owner.GetSelectedChannel(package)) :
                        package.IsTemplate && package.CompositionPresets.Count > 0 ? "Install " + owner.GetActiveTemplateCompositionName(package) : "Install package";
                    note.text = Status.HasUnbumpedPackageVersionWarning ? Status.PackageVersionWarningMessage :
                        !Installed ? MissingDependencies() : Status.Message ?? string.Empty;
                    DeucarianEditorWorkspaceControls.Show(note, !string.IsNullOrWhiteSpace(note.text));
                });
            }

            private string MissingDependencies()
            {
                var missing = owner._packageDependencyInstaller.GetMissingDependencies(package);
                return missing.Length == 0 ? string.Empty : "Installed first: " + string.Join(", ", missing.Select(item => item.DisplayName));
            }

            private void BuildSource()
            {
                var source = form.Section("Source & repository", true);
                var channels = PackageChannelPolicy.GetChannelOptions(package, owner.GetSelectedChannel(package));
                var choice = source.Choice("installer-selected-channel", "Channel", channels.Select(PackageChannelPolicy.GetChannelLabel).ToArray(),
                    () => Array.IndexOf(channels, owner.GetSelectedChannel(package)), value =>
                    { if (CanChange) owner.SetSelectedChannel(package, channels[value]); Refresh(); });
                refreshers.Add(() => choice.SetEnabled(channels.Length > 1 && CanChange));
                source.Action("installer-reset-channel", "Reset package override", () => { owner.ResetPackageChannelOverride(package); Refresh(); },
                    () => CanChange && owner._stateRepository != null && owner._stateRepository.GetPackageChannelSelection(package.PackageId).HasValue);
                source.ReadOnly("installer-source-provenance", "Channel source", ChannelProvenance);
                source.Action("installer-develop", "Develop locally", () => DeucarianEditorNavigation.Open(form.Root,
                    Development.DevelopmentPage.ToolId, package.PackageId), () => Installed && CanChange);
                source.ReadOnly("installer-selected-id", "Package ID", () => package.PackageId);
                source.ReadOnly("installer-selected-url", "Selected URL", () => package.GetUrl(owner.GetSelectedChannel(package)));
                source.ReadOnly("installer-stable-url", "Stable URL", () => package.StableUrl);
                source.ReadOnly("installer-development-url", "Development URL", () => package.DevelopmentUrl);
                source.ReadOnly("installer-installed-reference", "Installed source", () => owner._packageDetectionService.TryGetInstalledPackageReference(package.PackageId, out string value) ? value : "Not installed");
                source.ReadOnly("installer-installed-revision", "Installed revision", () => Status.InstalledRevision);
                source.ReadOnly("installer-latest-revision", "Latest revision", () => Status.LatestRevision);
                source.ReadOnly("installer-installed-path", "Installed folder", () => owner._packageDetectionService.TryGetInstalledPackage(package.PackageId, out var info) ? info.resolvedPath : "");
                source.Action("installer-copy-source", "Copy selected URL", () => EditorGUIUtility.systemCopyBuffer = package.GetUrl(owner.GetSelectedChannel(package)));
                source.Action("installer-reinstall", "Reinstall package", () => owner.ReinstallPackage(package),
                    () => CanChange && Installed && !Status.IsSourceMigrationAvailable && !Status.IsReloadPending);
                var remove = source.Action("installer-remove", "Remove package…", () => owner.RemovePackage(package), () => Installed && CanChange);
                remove.AddToClassList("dw-destructive");
            }

            private string ChannelProvenance()
            {
                bool installed = owner._packageDetectionService.TryGetInstalledPackageChannel(package, out var channel, out string reason);
                return PackageChannelPolicy.GetContextualChannelProvenance(package,
                    owner._stateRepository?.GetProjectChannelSelection() ?? PackageChannelSelection.None,
                    owner._stateRepository?.GetPackageChannelSelection(package.PackageId) ?? PackageChannelSelection.None,
                    installed, channel, reason);
            }
        }
    }
}
