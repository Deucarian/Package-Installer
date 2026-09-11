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
            private InstallerPackageDetails details;
            private string detailsRevision;
            private readonly VisualElement loading;
            private readonly VisualElement loadingIcon;
            private readonly Label loadingLabel;
            private readonly VisualElement updateBar;
            private readonly Label updateSummary;
            private readonly IVisualElementScheduledItem rowPump;
            private IEnumerator<object> pendingRows;
            private bool rowsDirty;
            internal DeucarianEditorCollectionWorkspace View { get; }

            internal InstallerWorkspace(PackageInstallerWindow owner)
            {
                this.owner = owner;
                View = new DeucarianEditorCollectionWorkspace(owner.PageRoot, Application.productName,
                    "Package Installer", "Find and manage your Deucarian packages.",
                    DeucarianToolIds.PackageInstaller, "Search packages…");
                View.UsePanels(); View.Collection.AddToClassList("dw-package-collection");
                tabs = new DeucarianEditorChoiceBar(new[] { "Installed", "Browse", "Updates", "Dependency graph" },
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
                updateBar = DeucarianEditorWorkspaceControls.Region("installer-updates", "dw-update-bar");
                updateSummary = DeucarianEditorWorkspaceControls.Label("", "dw-label");
                updateBar.Add(DeucarianEditorWorkspaceControls.Icon(DeucarianEditorIconIds.Update));
                updateBar.Add(updateSummary); updateBar.Add(update); View.Workspace.Content.Add(updateBar);
                scope = new DeucarianEditorWorkspaceForm(View.Workspace.Scope);
                scope.EnabledWhen(() => !owner.IsAnyOperationBusy());
                scope.Choice("installer-project-channel", "Project channel",
                    new[] { "Use package sources", "Stable · main", "Development · develop" },
                    () => { var selection = owner.GetGlobalProjectChannelSelection(); return !selection.HasValue ? 0 : selection.Channel == PackageChannel.Development ? 2 : 1; },
                    value => { if (value == 0) owner.ClearGlobalChannelOverrideFromPopup(); else owner.SetGlobalChannelOverride(value == 2 ? PackageChannel.Development : PackageChannel.Stable); Refresh(); });
                status = DeucarianEditorWorkspaceControls.Label("", "dw-muted");
                View.Workspace.FooterLeading.text = "Loading packages…";
                var options = new DeucarianEditorWorkspaceForm(View.Workspace.Scope).Section("Update preferences", true);
                options.Toggle("installer-check-start", "Check on Editor start", () => PackageUpdateCheckPreferences.CheckOnEditorStart, value => PackageUpdateCheckPreferences.CheckOnEditorStart = value);
                options.Toggle("installer-check-open", "Check when opened", () => PackageUpdateCheckPreferences.CheckOnWindowOpen, value => PackageUpdateCheckPreferences.CheckOnWindowOpen = value);
                options.Root.Add(status);
                View.Workspace.SearchField.RegisterValueChangedCallback(evt => { search = evt.newValue ?? ""; Refresh(); });
                View.SetItems(Array.Empty<DeucarianEditorCollectionItem>(), null, "Loading packages…");
                loading = DeucarianEditorWorkspaceControls.Panel("installer-loading");
                loading.AddToClassList("dw-loading-panel");
                loadingIcon = DeucarianEditorWorkspaceControls.Icon(DeucarianEditorIconIds.Busy); loading.Add(loadingIcon);
                loadingLabel = DeucarianEditorWorkspaceControls.Label("Loading packages…", "dw-section-title"); loading.Add(loadingLabel);
                loading.Add(DeucarianEditorWorkspaceControls.Label("Checking your installed packages and catalog.", "dw-muted"));
                for (int i = 0; i < 3; i++) loading.Add(DeucarianEditorWorkspaceControls.Region(null, "dw-loading-placeholder"));
                View.Workspace.Content.Insert(0, loading);
                SetLoading(true, "Loading packages…");
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
                DeucarianEditorWorkspaceControls.Show(updateBar, hasUpdates && !graph);
                updateSummary.text = owner.GetPackagesWithUpdates().Length + " updates available";
                status.text = PackageRegistryProvider.StatusMessage + " · Updates: " +
                    (owner._packageUpdateCheckService.LastCheckedUtc.HasValue
                        ? owner._packageUpdateCheckService.LastCheckedUtc.Value.ToLocalTime().ToString("HH:mm:ss") : "not checked");
                rowsDirty = true;
            }

            private void PumpRows()
            {
                if (loading.style.display.value != DisplayStyle.None && DeucarianEditorAmbientMotionSettings.MotionScale > 0)
                    loadingIcon.transform.rotation = Quaternion.Euler(0, 0, (float)(EditorApplication.timeSinceStartup * 180 % 360));
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
                    bool waiting = PackageRegistryProvider.IsLocalLoading || owner._packageDetectionService.IsRefreshing;
                    SetLoading(waiting, PackageRegistryProvider.IsLocalLoading ? "Loading package catalog…" : "Finding installed packages…");
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
                    if (category == 0 && !installed || category == 2 && !updateStatus.NeedsAttention) continue;
                    if ((package.DisplayName + " " + package.PackageId + " " + package.Description).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (package.PackageId == owner._selectedPackageId) selected = package;
                    string version = owner._packageDetectionService.TryGetInstalledPackage(package.PackageId, out var info) ? info.version : "Not installed";
                    rows.Add(new DeucarianEditorCollectionItem(package.PackageId, package.DisplayName, version,
                        installed ? owner.GetPackageVisualStatus(package).Label : package.Category,
                        () => { owner.SelectDefinition(package, package.IsIntegration ? SelectionKind.Integration : SelectionKind.Package, false); Refresh(); },
                        iconId: package.IsIntegration ? DeucarianEditorIconIds.Integration : DeucarianEditorIconIds.Package));
                    if (rows.Count % 8 == 0)
                    { SetLoading(false, ""); View.SetItems(rows, owner._selectedPackageId, "Loading packages…"); }
                }
                SetLoading(false, "");
                View.SetItems(rows, selected?.PackageId, category == 2
                    ? "No pending updates in the current results. Check updates to verify against the selected channels."
                    : "No packages match this view.");
                string selectedId = selected?.PackageId;
                string revision = selected != null && owner._packageDetectionService.TryGetInstalledPackage(selected.PackageId, out var selectedInfo) ? selectedInfo.version + "|" + selectedInfo.resolvedPath : "";
                if (details == null || detailsId != selectedId || detailsRevision != revision)
                {
                    detailsId = selectedId; detailsRevision = revision;
                    details = new InstallerPackageDetails(owner, View.Details, selected);
                }
                details.Refresh();
            }

            private void SetLoading(bool value, string message)
            {
                bool graph = owner._viewMode == InstallerViewMode.EcosystemGraph;
                loadingLabel.text = message;
                DeucarianEditorWorkspaceControls.Show(loading, value && !graph);
                DeucarianEditorWorkspaceControls.Show(View.Collection, !value && !graph);
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
