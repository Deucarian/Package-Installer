using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {
        private sealed class InstallerGraphDetails
        {
            private readonly PackageInstallerWindow owner;
            private readonly VisualElement root;
            private PackageGraphModel graph;
            private string context;
            private InstallerPackageDetails packageDetails;
            private DeucarianEditorWorkspaceForm form;

            internal InstallerGraphDetails(PackageInstallerWindow owner, VisualElement root)
            { this.owner = owner; this.root = root; }

            internal void Refresh()
            {
                var selected = owner.GetSelectedDefinition(); var group = owner.GetFocusedGraphGroup();
                string key = selected != null ? "package:" + selected.PackageId : group != null ? "group:" + group.Id : "overview";
                if (context != key || graph != owner._lastPackageGraph)
                {
                    context = key; graph = owner._lastPackageGraph; root.Clear(); packageDetails = null;
                    var content = DeucarianEditorWorkspaceControls.Region("installer-graph-context", "dw-graph-context"); root.Add(content);
                    form = new DeucarianEditorWorkspaceForm(content);
                    if (selected != null) packageDetails = new InstallerPackageDetails(owner, content, selected);
                    else if (group != null) BuildGroup(group);
                    else BuildOverview();
                    BuildNavigation();
                }
                packageDetails?.Refresh(); form?.Refresh();
            }

            private void BuildOverview()
            {
                var summary = form.Section("Ecosystem overview");
                summary.Note(() => "Select a group or package. Pan, zoom or fit the graph to explore its dependencies.");
                var nodes = graph?.Nodes.Where(node => node != null && node.IsRegistered).ToArray() ?? Array.Empty<PackageGraphNode>();
                summary.ReadOnly("graph-package-count", "Packages", () => nodes.Length.ToString());
                summary.ReadOnly("graph-installed-count", "Installed", () => nodes.Count(node => node.IsInstalled).ToString());
                summary.ReadOnly("graph-missing-count", "Not installed", () => nodes.Count(node => !node.IsInstalled).ToString());
                summary.ReadOnly("graph-updates", "Updates", () => owner.GetPackagesWithUpdates().Length.ToString());
                summary.ReadOnly("graph-filters", "Filters", owner.GetActiveFilterSummary);
                summary.Action("graph-update-all", "Review all updates", () => owner.HandleActionButton(PackageInstallerActionKind.UpdateAll),
                    () => !owner.IsAnyOperationBusy() && owner.GetPackagesWithUpdates().Length > 0, true);
            }

            private void BuildGroup(PackageGraphGroup group)
            {
                var summary = form.Section(group.DisplayName); summary.Note(() => group.Description);
                PackageGraphNode[] Nodes() => owner.GetGraphGroupDescendantPackages(group.Id).Where(node => node != null).ToArray();
                PackageDefinition[] Missing() => Nodes().Where(node => !node.IsInstalled && node.PackageDefinition != null).Select(node => node.PackageDefinition).Distinct().ToArray();
                PackageDefinition[] Updates() => Nodes().Where(node => node.Status == PackageGraphNodeStatus.UpdateAvailable && node.PackageDefinition != null).Select(node => node.PackageDefinition).Distinct().ToArray();
                summary.ReadOnly("graph-group-packages", "Packages", () => Nodes().Length.ToString());
                summary.ReadOnly("graph-group-installed", "Installed", () => Nodes().Count(node => node.IsInstalled).ToString());
                summary.ReadOnly("graph-group-missing", "Missing", () => Missing().Length.ToString());
                summary.ReadOnly("graph-group-updates", "Updates", () => Updates().Length.ToString());
                summary.Action("graph-group-install", "Install missing packages", () => owner.InstallGraphGroupPackages(group, Missing()), () => !owner.IsAnyOperationBusy() && Missing().Length > 0, true);
                summary.Action("graph-group-update", "Review available updates", () => owner.UpdateGraphGroupPackages(group, Updates()), () => !owner.IsAnyOperationBusy() && Updates().Length > 0);
            }

            private void BuildNavigation()
            {
                var tree = DeucarianEditorWorkspaceControls.Region("installer-graph-navigation", "dw-tree"); root.Add(tree);
                tree.Add(DeucarianEditorWorkspaceControls.Label("Groups", "dw-section-title"));
                var levels = new List<VisualElement> { tree };
                foreach (var row in PackageGraphNavigationModel.CreateEcosystemOverviewGroupNavigationRows(graph, owner._graphNavigationState))
                {
                    int depth = Math.Max(0, row.Depth);
                    while (levels.Count <= depth)
                    { var level = DeucarianEditorWorkspaceControls.Region(null, "dw-tree-level"); levels[levels.Count - 1].Add(level); levels.Add(level); }
                    if (levels.Count > depth + 1) levels.RemoveRange(depth + 1, levels.Count - depth - 1);
                    var button = DeucarianEditorWorkspaceControls.IconButton(row.DisplayName,
                        row.IsOverview ? DeucarianEditorIconIds.Suite : row.IsPackage ? DeucarianEditorIconIds.Package : row.IsExpanded ? DeucarianEditorIconIds.ChevronDown : DeucarianEditorIconIds.ChevronRight,
                        () => owner.ActivateEcosystemOverviewNavigationRow(row), DeucarianEditorButtonRole.Quiet);
                    button.AddToClassList("dw-tree-row"); button.EnableInClassList("dw-selected", row.IsSelected);
                    button.tooltip = row.Tooltip + "\n" + row.Summary;
                    button.RegisterCallback<MouseEnterEvent>(_ =>
                    {
                        if (row.IsPackage) owner._graphView?.SetExternalPackageHover(row.Id);
                        else if (!row.IsOverview) owner._graphView?.SetExternalGroupHover(row.Id);
                    });
                    button.RegisterCallback<MouseLeaveEvent>(_ =>
                    {
                        if (row.IsPackage) owner._graphView?.ClearExternalPackageHover(row.Id);
                        else if (!row.IsOverview) owner._graphView?.ClearExternalGroupHover(row.Id);
                    });
                    levels[depth].Add(button);
                }
            }
        }
    }
}
