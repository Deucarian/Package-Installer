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
            private readonly TextField packageSearch;
            private readonly Button check;
            private readonly Button refresh;
            private readonly Button update;
            private readonly Label status;
            private string search = "";
            private int category;
            private string detailsId;
            private InstallerPackageDetails details;
            private string detailsRevision;
            private PackageDefinition detailsDefinition;
            private readonly VisualElement loading;
            private readonly VisualElement loadingIcon;
            private readonly VisualElement loadingFill;
            private readonly Label loadingLabel;
            private readonly VisualElement updateBar;
            private readonly Label updateSummary;
            private readonly IVisualElementScheduledItem rowPump;
            private IEnumerator<object> pendingRows;
            private bool rowsDirty;
            internal DeucarianEditorCollectionWorkspace View { get; }
            internal bool IsLoading { get; private set; }

            internal InstallerWorkspace(PackageInstallerWindow owner)
            {
                this.owner = owner;
                View = new DeucarianEditorCollectionWorkspace(owner.PageRoot, Application.productName,
                    "Packages", "Add and update what your project needs.",
                    DeucarianToolIds.PackageInstaller, "Find a tool…");
                View.UsePanels(); View.Collection.AddToClassList("dw-package-collection");
                View.Workspace.SetScopeBeforeTabs();
                View.Workspace.Scope.AddToClassList("dw-package-filters");
                DeucarianEditorWorkspaceNavigation.Populate(View.Workspace, DeucarianToolIds.PackageInstaller);
                tabs = new DeucarianEditorChoiceBar(new[] { "Installed", "Browse", "Updates", "Dependency graph" },
                    owner._viewMode == InstallerViewMode.EcosystemGraph ? 3 : 0, true);
                tabs.Changed += value => {
                    if (value < 3) category = value;
                    owner.SetViewMode(value == 3 ? InstallerViewMode.EcosystemGraph : InstallerViewMode.List);
                    Refresh();
                };
                View.Workspace.Tabs.Add(tabs);
                refresh = DeucarianEditorWorkspaceControls.IconButton("Refresh", DeucarianEditorIconIds.Refresh, owner.RefreshPackages);
                check = DeucarianEditorWorkspaceControls.Button("Check updates", () => owner.HandleActionButton(PackageInstallerActionKind.CheckUpdates));
                update = DeucarianEditorWorkspaceControls.Button("Review updates", () => owner.HandleActionButton(PackageInstallerActionKind.UpdateAll), true);
                View.Workspace.PageActions.Add(refresh);
                var preferences = DeucarianEditorWorkspaceControls.IconButton(string.Empty, DeucarianEditorIconIds.Settings, ShowPreferences);
                preferences.tooltip = "Update preferences and activity";
                View.Workspace.PageActions.Add(preferences);
                updateBar = DeucarianEditorWorkspaceControls.Region("installer-updates", "dw-update-bar");
                updateSummary = DeucarianEditorWorkspaceControls.Label("", "dw-label");
                updateBar.Add(DeucarianEditorWorkspaceControls.Icon(DeucarianEditorIconIds.Update));
                updateBar.Add(updateSummary); updateBar.Add(check); updateBar.Add(update); View.Workspace.Content.Add(updateBar);
                scope = new DeucarianEditorWorkspaceForm(View.Workspace.Scope);
                scope.EnabledWhen(() => !owner.IsAnyOperationBusy());
                scope.Choice("installer-project-channel", "Channel",
                    new[] { "Use package sources", "Stable · main", "Development · develop" },
                    () => { var selection = owner.GetGlobalProjectChannelSelection(); return !selection.HasValue ? 0 : selection.Channel == PackageChannel.Development ? 2 : 1; },
                    value => { if (value == 0) owner.ClearGlobalChannelOverrideFromPopup(); else owner.SetGlobalChannelOverride(value == 2 ? PackageChannel.Development : PackageChannel.Stable); Refresh(); });
                status = DeucarianEditorWorkspaceControls.Label("", "dw-muted");
                View.Workspace.FooterLeading.text = "Loading packages…";
                packageSearch = DeucarianEditorSearchField.Create("Find a package…", value => { search = value ?? ""; Refresh(); });
                packageSearch.name = "installer-package-search";
                View.Workspace.Scope.Add(packageSearch);
                View.SetItems(Array.Empty<DeucarianEditorCollectionItem>(), null, "Loading packages…");
                loading = DeucarianEditorWorkspaceControls.Panel("installer-loading");
                loading.AddToClassList("dw-loading-panel");
                var loadingHeading = DeucarianEditorWorkspaceControls.Region(null, "dw-loading-heading");
                loadingIcon = DeucarianEditorWorkspaceControls.Icon(DeucarianEditorIconIds.Busy); loadingHeading.Add(loadingIcon);
                var loadingCopy = DeucarianEditorWorkspaceControls.Region(null, "dw-loading-copy");
                loadingLabel = DeucarianEditorWorkspaceControls.Label("Loading packages…", "dw-section-title"); loadingCopy.Add(loadingLabel);
                loadingCopy.Add(DeucarianEditorWorkspaceControls.Label("You can keep using the Control Center.", "dw-muted"));
                loadingHeading.Add(loadingCopy); loading.Add(loadingHeading);
                var loadingTrack = DeucarianEditorWorkspaceControls.Region(null, "dw-progress");
                loadingTrack.tooltip = "Loading is in progress; this is not a percentage estimate.";
                loadingFill = DeucarianEditorWorkspaceControls.Region(null, "dw-progress-fill"); loadingTrack.Add(loadingFill); loading.Add(loadingTrack);
                for (int i = 0; i < 3; i++)
                {
                    var placeholder = DeucarianEditorWorkspaceControls.Region(null, "dw-loading-placeholder");
                    placeholder.Add(DeucarianEditorWorkspaceControls.Region(null, "dw-loading-avatar"));
                    var lines = DeucarianEditorWorkspaceControls.Region(null, "dw-loading-lines");
                    lines.Add(DeucarianEditorWorkspaceControls.Region(null, "dw-loading-line"));
                    var description = DeucarianEditorWorkspaceControls.Region(null, "dw-loading-line"); description.AddToClassList("dw-loading-line-long"); lines.Add(description);
                    placeholder.Add(lines); placeholder.Add(DeucarianEditorWorkspaceControls.Region(null, "dw-loading-badge")); loading.Add(placeholder);
                }
                View.Workspace.Content.Insert(0, loading);
                SetLoading(true, "Loading packages…");
                rowPump = owner.PageRoot.schedule.Execute(PumpRows).Every(16);
            }

            internal void Refresh()
            {
                if (owner._packageDetectionService == null || owner._packageUpdateCheckService == null) return;
                bool graph = owner._viewMode == InstallerViewMode.EcosystemGraph;
                tabs.SetValueWithoutNotify(graph ? 3 : category);
                packageSearch.SetEnabled(!graph);
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
                DeucarianEditorWorkspaceControls.Show(updateBar, !graph && !IsLoading);
                updateSummary.text = hasUpdates ? owner.GetPackagesWithUpdates().Length + " updates available"
                    : owner._packageUpdateCheckService.LastCheckedUtc.HasValue ? "No updates found" : "Check for package updates";
                status.text = PackageRegistryProvider.StatusMessage + " · Updates: " +
                    (owner._packageUpdateCheckService.LastCheckedUtc.HasValue
                        ? owner._packageUpdateCheckService.LastCheckedUtc.Value.ToLocalTime().ToString("HH:mm:ss") : "not checked");
                updateSummary.tooltip = status.text;
                rowsDirty = true;
            }

            private void ShowPreferences()
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Check on Editor start"), PackageUpdateCheckPreferences.CheckOnEditorStart,
                    () => PackageUpdateCheckPreferences.CheckOnEditorStart = !PackageUpdateCheckPreferences.CheckOnEditorStart);
                menu.AddItem(new GUIContent("Check when opened"), PackageUpdateCheckPreferences.CheckOnWindowOpen,
                    () => PackageUpdateCheckPreferences.CheckOnWindowOpen = !PackageUpdateCheckPreferences.CheckOnWindowOpen);
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Operation activity"), owner._operationDetailsExpanded,
                    () => owner.SetOperationDetailsExpanded(!owner._operationDetailsExpanded));
                menu.ShowAsContext();
            }

            private void PumpRows()
            {
                if (loading.style.display.value != DisplayStyle.None && DeucarianEditorAmbientMotionSettings.MotionScale > 0)
                {
                    loadingIcon.transform.rotation = Quaternion.Euler(0, 0, (float)(EditorApplication.timeSinceStartup * 180 % 360));
                    loadingFill.style.marginLeft = Length.Percent((float)((Math.Sin(EditorApplication.timeSinceStartup * 2) + 1) * 25));
                }
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
                    if (!waiting)
                    {
                        detailsId = null; detailsDefinition = null; detailsRevision = null;
                        details = new InstallerPackageDetails(owner, View.Details, null);
                    }
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
                    rows.Add(new DeucarianEditorCollectionItem(package.PackageId, PackageTitle(package), version,
                        installed ? owner.GetPackageVisualStatus(package).Label : package.Category,
                        () => { owner.SelectDefinition(package, package.IsIntegration ? SelectionKind.Integration : SelectionKind.Package, false); Refresh(); },
                        iconId: PackageIcon(package)));
                    if (rows.Count % 8 == 0)
                    { SetLoading(false, ""); View.SetItems(rows, owner._selectedPackageId, "Loading packages…"); }
                }
                SetLoading(false, "");
                View.SetItems(rows, selected?.PackageId, category == 2
                    ? "No pending updates in the current results. Check updates to verify against the selected channels."
                    : "No packages match this view.");
                string selectedId = selected?.PackageId;
                string revision = selected != null && owner._packageDetectionService.TryGetInstalledPackage(selected.PackageId, out var selectedInfo) ? selectedInfo.version + "|" + selectedInfo.resolvedPath : "";
                revision += "|" + string.Join(",", owner._packageDetectionService.InstalledPackageIds.OrderBy(id => id));
                if (details == null || detailsId != selectedId || detailsRevision != revision || !ReferenceEquals(detailsDefinition, selected))
                {
                    detailsId = selectedId; detailsRevision = revision; detailsDefinition = selected;
                    details = new InstallerPackageDetails(owner, View.Details, selected);
                }
                details.Refresh();
            }

            private void SetLoading(bool value, string message)
            {
                IsLoading = value;
                bool graph = owner._viewMode == InstallerViewMode.EcosystemGraph;
                loadingLabel.text = message;
                View.Workspace.Title.text = value ? "Package Installer" : "Packages";
                View.Workspace.Subtitle.text = value ? "Discover and install packages to extend your experience." : "Add and update what your project needs.";
                DeucarianEditorWorkspaceControls.Show(View.Workspace.Tabs, !value);
                DeucarianEditorWorkspaceControls.Show(packageSearch, !value);
                DeucarianEditorWorkspaceControls.Show(loading, value && !graph);
                DeucarianEditorWorkspaceControls.Show(View.Collection, !value && !graph);
                DeucarianEditorWorkspaceControls.Show(updateBar, !value && !graph);
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
