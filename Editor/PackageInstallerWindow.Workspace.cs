using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {
        private sealed class InstallerWorkspace : IDisposable
        {
            private readonly PackageInstallerWindow owner;
            private readonly DeucarianEditorChoiceBar tabs;
            private readonly DeucarianEditorWorkspaceForm scope;
            private readonly Button check;
            private readonly Button refresh;
            private readonly Button update;
            private readonly Label status;
            private string search = "";
            private int category;
            private string detailsId;
            private DeucarianEditorWorkspaceForm details;
            private readonly IVisualElementScheduledItem rowPump;
            private IEnumerator<object> pendingRows;
            private bool rowsDirty;
            internal DeucarianEditorCollectionWorkspace View { get; }

            internal InstallerWorkspace(PackageInstallerWindow owner)
            {
                this.owner = owner;
                View = new DeucarianEditorCollectionWorkspace(owner.PageRoot, Application.productName,
                    "Package Installer", "Find, review and update your project’s packages.",
                    DeucarianToolIds.PackageInstaller, "Search packages…");
                tabs = new DeucarianEditorChoiceBar(new[] { "Installed", "Updates", "Browse", "Dependency graph" },
                    owner._viewMode == InstallerViewMode.EcosystemGraph ? 3 : 0, true);
                tabs.Changed += value => {
                    if (value < 3) category = value;
                    owner.SetViewMode(value == 3 ? InstallerViewMode.EcosystemGraph : InstallerViewMode.List);
                    Refresh();
                };
                View.Workspace.Tabs.Add(tabs);
                refresh = DeucarianEditorWorkspaceControls.Button("Refresh catalog", owner.RefreshPackages);
                check = DeucarianEditorWorkspaceControls.Button("Check updates", () => owner.HandleActionButton(PackageInstallerActionKind.CheckUpdates));
                update = DeucarianEditorWorkspaceControls.Button("Review updates", () => owner.HandleActionButton(PackageInstallerActionKind.UpdateAll), true);
                View.Workspace.PageActions.Add(refresh);
                View.Workspace.PageActions.Add(check);
                View.Workspace.PageActions.Add(update);
                scope = new DeucarianEditorWorkspaceForm(View.Workspace.Scope);
                scope.EnabledWhen(() => !owner.IsAnyOperationBusy());
                scope.Choice("installer-project-channel", "Project channel",
                    new[] { "Use package sources", "Stable · main", "Development · develop" },
                    () => { var selection = owner.GetGlobalProjectChannelSelection(); return !selection.HasValue ? 0 : selection.Channel == PackageChannel.Development ? 2 : 1; },
                    value => { if (value == 0) owner.ClearGlobalChannelOverrideFromPopup(); else owner.SetGlobalChannelOverride(value == 2 ? PackageChannel.Development : PackageChannel.Stable); Refresh(); });
                status = DeucarianEditorWorkspaceControls.Label("", "dw-muted");
                View.Workspace.Scope.Add(status);
                View.Workspace.SearchField.RegisterValueChangedCallback(evt => { search = evt.newValue ?? ""; Refresh(); });
                View.SetItems(Array.Empty<DeucarianEditorCollectionItem>(), null, "Loading packages…");
                rowPump = owner.PageRoot.schedule.Execute(PumpRows).Every(16);
            }

            internal void Refresh()
            {
                if (owner._packageDetectionService == null || owner._packageUpdateCheckService == null) return;
                bool graph = owner._viewMode == InstallerViewMode.EcosystemGraph;
                tabs.SetValueWithoutNotify(graph ? 3 : category);
                View.Workspace.SearchField.SetEnabled(!graph);
                View.Workspace.SetSearchPrompt(graph ? "Use graph search below" : "Search packages…");
                scope.Refresh();
                bool busy = owner.IsAnyOperationBusy();
                refresh.SetEnabled(!busy);
                bool hasUpdates = owner.GetPackagesWithUpdates().Length > 0;
                var checkState = CreateActionButtonState(PackageInstallerActionKind.CheckUpdates, owner._activeActionKind,
                    owner._cancelingActionKind, busy, hasUpdates);
                check.text = checkState.Label;
                check.SetEnabled(checkState.Enabled);
                update.SetEnabled(!busy && hasUpdates);
                DeucarianEditorWorkspaceControls.Show(update, hasUpdates);
                status.text = PackageRegistryProvider.StatusMessage + " · Updates: " +
                    (owner._packageUpdateCheckService.LastCheckedUtc.HasValue
                        ? owner._packageUpdateCheckService.LastCheckedUtc.Value.ToLocalTime().ToString("HH:mm:ss") : "not checked");
                rowsDirty = true;
            }

            private void PumpRows()
            {
                if (rowsDirty)
                {
                    pendingRows?.Dispose();
                    pendingRows = PopulateRows();
                    rowsDirty = false;
                }
                var budget = System.Diagnostics.Stopwatch.StartNew();
                for (int count = 0; pendingRows != null && count < 8 && budget.ElapsedMilliseconds < 4; count++)
                {
                    if (pendingRows.MoveNext()) continue;
                    pendingRows.Dispose();
                    pendingRows = null;
                }
            }

            private IEnumerator<object> PopulateRows()
            {
                if (PackageRegistryProvider.IsLocalLoading || !owner._packageDetectionService.HasSuccessfulRefresh)
                {
                    View.SetItems(Array.Empty<DeucarianEditorCollectionItem>(), null,
                        PackageRegistryProvider.IsLocalLoading ? "Loading package catalog…" :
                        owner._packageDetectionService.IsRefreshing ? "Finding installed packages…" :
                        "Installed packages could not be loaded. Use Refresh catalog to retry.");
                    yield break;
                }
                var rows = new List<DeucarianEditorCollectionItem>();
                PackageDefinition selected = null;
                foreach (var package in PackageRegistryProvider.All.OrderBy(p => p.DisplayName))
                {
                    yield return null;
                    bool installed = owner._packageDetectionService.IsInstalled(package.PackageId);
                    var updateStatus = owner._packageUpdateCheckService.GetStatus(package, owner.GetSelectedChannel(package));
                    if (category == 0 && !installed || category == 1 && !updateStatus.NeedsAttention) continue;
                    if ((package.DisplayName + " " + package.PackageId + " " + package.Description).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (package.PackageId == owner._selectedPackageId) selected = package;
                    string version = owner._packageDetectionService.TryGetInstalledPackage(package.PackageId, out var info) ? info.version : "Not installed";
                    rows.Add(new DeucarianEditorCollectionItem(package.PackageId, package.DisplayName, version,
                        installed ? owner.GetPackageVisualStatus(package).Label : package.Category,
                        () => { owner.SelectDefinition(package, package.IsIntegration ? SelectionKind.Integration : SelectionKind.Package, false); Refresh(); }));
                    if (rows.Count % 8 == 0)
                        View.SetItems(rows, owner._selectedPackageId, "Loading packages…");
                }
                View.SetItems(rows, selected?.PackageId, category == 1
                    ? "No pending updates in the current results. Check updates to verify against the selected channels."
                    : "No packages match this view.");
                string selectedId = selected?.PackageId;
                if (details == null || detailsId != selectedId)
                {
                    detailsId = selectedId;
                    BuildDetails(selected);
                }
                details.Refresh();
            }

            private void BuildDetails(PackageDefinition package)
            {
                View.Details.Clear();
                details = new DeucarianEditorWorkspaceForm(View.Details);
                if (package == null)
                {
                    details.Section("Package details").Note(() => "Select a package to review its source, dependencies and available actions. Changes are confirmed before they are applied.");
                    return;
                }
                var summary = details.Section(package.DisplayName);
                summary.Note(() => package.Description);
                summary.ReadOnly("installer-selected-id", "Package", () => package.PackageId);
                summary.Action("installer-develop", "Develop locally", () => DeucarianEditorNavigation.Open(View.Workspace.Root,
                    Development.DevelopmentPage.ToolId, package.PackageId), () => owner._packageDetectionService.IsInstalled(package.PackageId));
                summary.ReadOnly("installer-selected-version", "Installed", () => owner._packageDetectionService.TryGetInstalledPackage(package.PackageId, out var info) ? info.version : "Not installed");
                summary.ReadOnly("installer-selected-channel", "Channel", () => PackageChannelPolicy.GetChannelLabel(owner.GetSelectedChannel(package)));
                summary.ReadOnly("installer-selected-update", "Update", () => GetUpdateStatusText(owner._packageUpdateCheckService.GetStatus(package, owner.GetSelectedChannel(package))));
                summary.Root.Add(DeucarianEditorWorkspaceControls.Embedded(() => {
                    owner.EnsureStyles();
                    using (DeucarianEditorWorkbenchGUI.BeginEmbeddedPage())
                        owner.DrawPackageActionButtons(package, true);
                }, "installer-package-actions"));
                var advanced = details.Section("Source, dependencies & options", true);
                advanced.Root.Add(DeucarianEditorWorkspaceControls.Embedded(() => {
                    owner.EnsureStyles();
                    using (DeucarianEditorWorkbenchGUI.BeginEmbeddedPage())
                    {
                        owner.DrawRequirementsPanel(package);
                        owner.DrawChannelPanel(package);
                        if (package.IsTemplate && package.CompositionPresets.Count > 0) owner.DrawTemplateCompositionPanel(package);
                        owner.DrawOptionalCompanionsPanel(package);
                        owner.DrawExtrasPanel(package);
                        owner.DrawAdvancedPanel(package);
                    }
                }, "installer-package-options"));
            }
            public void Dispose()
            {
                rowPump.Pause();
                pendingRows?.Dispose();
                pendingRows = null;
                View.Dispose();
            }
        }
    }
}
