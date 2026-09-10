using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal sealed class DevelopmentPage : IDeucarianEditorPage
    {
        internal const string ToolId = "deucarian.package-development";
        private readonly DevelopmentWorkflow workflow;
        private readonly DeucarianEditorWorkspace workspace;
        private readonly DeucarianEditorChangeReview review;
        private readonly DevelopmentPageForms forms;
        private readonly Func<bool> catalogLoading;
        private readonly Func<IReadOnlyList<DevelopmentInstalledPackage>> capturePackages;
        private readonly HashSet<string> selection = new HashSet<string>(StringComparer.Ordinal);
        private IReadOnlyList<DevelopmentInstalledPackage> packages = Array.Empty<DevelopmentInstalledPackage>();
        private string requestedPackage;
        private string inspected;
        private bool loaded;
        private bool disposed;

        private static DevelopmentWorkflow CreateWorkflow()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            var runner = new DevelopmentGitProcessRunner();
            var files = new DevelopmentFileSystem();
            return new DevelopmentWorkflow(project, new DevelopmentRepositoryService(runner, files), runner, files,
                DevelopmentSourceComposition.Create(project));
        }

        internal DevelopmentPage() : this(CreateWorkflow(), PackageRegistryProvider.LoadInBackground,
            () => PackageRegistryProvider.IsLocalLoading,
            () => DevelopmentInstalledPackage.Capture(PackageRegistryProvider.All, PackageInfo.GetAllRegisteredPackages())) { }

        internal DevelopmentPage(DevelopmentWorkflow workflow, Action beginCatalogLoad, Func<bool> catalogLoading,
            Func<IReadOnlyList<DevelopmentInstalledPackage>> capturePackages)
        {
            this.workflow = workflow;
            this.catalogLoading = catalogLoading;
            this.capturePackages = capturePackages;
            Root = new VisualElement();
            workspace = new DeucarianEditorWorkspace(Root, Application.productName);
            workspace.Title.text = "Package development";
            workspace.Subtitle.text = "Edit and test one package in this project, then review and push its changes.";
            DeucarianEditorWorkspaceNavigation.Populate(workspace, ToolId);
            review = new DeucarianEditorChangeReview(workspace.Content);
            forms = new DevelopmentPageForms(workflow, workspace, review, StageSelected);
            workspace.FooterLeading.text = "Loading installed packages…";
            workflow.Changed += Refresh;
            beginCatalogLoad();
        }

        public VisualElement Root { get; }
        public void Activate(string route)
        {
            if (!string.IsNullOrWhiteSpace(route)) requestedPackage = route;
            if (loaded) ApplyRequestedPackage();
        }
        public void Deactivate() { }
        public void Update(Rect bounds)
        {
            if (disposed || loaded || catalogLoading()) return;
            packages = capturePackages();
            loaded = true;
            forms.SetPackages(packages, Select);
            ApplyRequestedPackage();
            Refresh();
        }

        private void ApplyRequestedPackage()
        {
            if (string.IsNullOrEmpty(requestedPackage) && workflow.Package != null) return;
            var package = packages.FirstOrDefault(p => p.Id == requestedPackage) ?? packages.FirstOrDefault();
            requestedPackage = null;
            if (package != null && workflow.Package?.Id != package.Id) Select(package);
        }

        private void Select(DevelopmentInstalledPackage package)
        {
            selection.Clear(); inspected = null;
            workflow.SelectPackage(package);
            forms.RepositoryPath = workflow.Session?.RepositoryRoot ?? "";
            Refresh();
        }

        private void Refresh()
        {
            if (disposed) return;
            forms.Refresh();
            workspace.FooterLeading.text = workflow.Status;
            workspace.FooterTrailing.text = workflow.Managed ? "Local development · original source saved" : "Package repository scope only";
            var rows = new List<DeucarianEditorChangeItem>();
            foreach (var file in workflow.Snapshot?.Files ?? Array.Empty<DevelopmentChangedFile>())
            {
                if (file.IsStaged) AddRow(rows, file, true);
                if (file.IsUnstaged || file.IsUntracked) AddRow(rows, file, false);
            }
            inspected = ReconcileVisibleSelection(rows, selection, inspected);
            review.SetChanges(rows, inspected);
            var snapshot = workflow.Snapshot;
            review.SetSummary(snapshot == null ? "Inspect a repository to see its changes." :
                snapshot.Files.Count(f => f.IsStaged) + " staged · " + snapshot.Files.Count(f => f.IsUnstaged || f.IsUntracked) + " unstaged · " +
                (string.IsNullOrEmpty(snapshot.StagingBlockReason) ? "Select files explicitly; related Unity .meta files are reviewed together." : snapshot.StagingBlockReason));
            review.SetDiff(inspected == null ? "Select a file to inspect its diff." : inspected.Substring(2),
                inspected == null ? string.Empty : workflow.Diff,
                inspected != null && snapshot != null && snapshot.Files.Any(f => f.Path == inspected.Substring(2) && f.IsBinary));
            review.SetHistory(string.IsNullOrWhiteSpace(workflow.History) ? Array.Empty<DeucarianEditorHistoryItem>() :
                workflow.History.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select((line, i) => new DeucarianEditorHistoryItem(i.ToString(), line, "")).ToArray());
        }

        private void AddRow(List<DeucarianEditorChangeItem> rows, DevelopmentChangedFile file, bool staged)
        {
            string id = (staged ? "S:" : "W:") + file.Path;
            rows.Add(new DeucarianEditorChangeItem(id, file.Path,
                (staged ? "Staged " + file.IndexStatus : "Unstaged " + file.WorktreeStatus) + (file.IsBinary ? " · binary" : ""),
                staged, rows.Count < DeucarianEditorChangeReview.MaximumVisibleChanges && selection.Contains(id),
                () => { if (workflow.Busy) return; inspected = id; _ = workflow.InspectDiffAsync(file.Path, staged); },
                value => { if (value) selection.Add(id); else selection.Remove(id); },
                string.IsNullOrEmpty(file.OriginalPath) ? "Asset and .meta changes are paired when staging." : "Renamed from " + file.OriginalPath,
                !workflow.Busy && workflow.Connected && !file.IsConflict));
        }

        // Only currently reviewable rows may remain selected for a later explicit stage action.
        internal static string ReconcileVisibleSelection(IReadOnlyList<DeucarianEditorChangeItem> rows,
            ISet<string> selected, string inspectedId)
        {
            var visible = new HashSet<string>(rows.Take(DeucarianEditorChangeReview.MaximumVisibleChanges)
                .Select(row => row.Id), StringComparer.Ordinal);
            selected.IntersectWith(visible);
            return inspectedId != null && visible.Contains(inspectedId) ? inspectedId : null;
        }

        private void StageSelected(bool unstage)
        {
            string prefix = unstage ? "S:" : "W:";
            var paths = selection.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).Select(id => id.Substring(2)).ToArray();
            if (paths.Length == 0) return;
            IReadOnlyList<string> expanded;
            try { expanded = workflow.ExpandSelection(paths); }
            catch (DevelopmentGitException exception) { DevelopmentConfirmation.Show(workflow, "Package changes", exception.Message, "Close", () => { }); return; }
            DevelopmentConfirmation.Show(workflow, unstage ? "Unstage package files" : "Stage package files",
                workflow.Repository.Root + "\n\nThese files, including related .meta and rename paths, will be " +
                (unstage ? "unstaged" : "staged") + ":\n\n" + string.Join("\n", expanded), unstage ? "Unstage selected" : "Stage selected", () => {
                    selection.Clear();
                    _ = workflow.StageAsync(expanded, unstage);
                });
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            workflow.Changed -= Refresh; workflow.Dispose(); review.Dispose(); workspace.Dispose();
        }
    }
}
