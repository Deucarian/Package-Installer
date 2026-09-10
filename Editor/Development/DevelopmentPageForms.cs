using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Development
{
    // Only binds package values/actions to Editor-owned forms. No local visual tokens or layout rules.
    internal sealed class DevelopmentPageForms
    {
        private readonly DevelopmentWorkflow workflow;
        private readonly DeucarianEditorWorkspace workspace;
        private readonly DeucarianEditorChangeReview review;
        private readonly DeucarianEditorWorkspaceForm packageForm;
        private readonly DeucarianEditorWorkspaceForm setup;
        private readonly DeucarianEditorWorkspaceForm branches;
        private readonly Button cancel;
        private string branch = "feature/package-change";
        private string message = "";
        private string existingBranch = "";
        internal string RepositoryPath { get; set; } = "";

        internal DevelopmentPageForms(DevelopmentWorkflow workflow, DeucarianEditorWorkspace workspace,
            DeucarianEditorChangeReview review, Action<bool> stage)
        {
            this.workflow = workflow; this.workspace = workspace; this.review = review;
            packageForm = new DeucarianEditorWorkspaceForm(workspace.Scope);
            setup = review.Context.Section("Local source");
            setup.Note(() => workflow.Session == null ? "A local package reference changes this project's manifest. Restore it before committing project configuration." :
                "Source state: " + workflow.Session.State + ". " + (workflow.Session.WasDirectDependency ? "Original direct reference saved." : "Temporary direct override of a transitive dependency; finish removes that override."));
            setup.ReadOnly("development-installed", "Installed revision", () => workflow.Package?.InstalledRevision ?? "Select a package");
            setup.ReadOnly("development-source", "Installed source", () => workflow.Package?.Source ?? "");
            setup.ReadOnly("development-original", "Original reference", () => workflow.Session?.OriginalReference ?? "Saved when the repository is inspected");
            setup.Text("development-path", "Repository folder", () => RepositoryPath, value => RepositoryPath = value);
            setup.Action("development-browse", "Choose existing checkout…", PickFolder, () => !workflow.Busy && workflow.Package != null);
            setup.Action("development-inspect", "Inspect existing checkout", () => _ = workflow.InspectAsync(RepositoryPath, false), CanInspect, true);
            setup.Action("development-clone", "Clone to this new folder", Clone, () => CanInspect() && !workflow.Managed);
            setup.ReadOnly("development-remote", "Origin", () => workflow.Repository?.RemoteDisplay ?? workflow.Package?.Remote ?? "");
            setup.ReadOnly("development-verified-path", "Verified repository", () => workflow.Repository?.Root ?? "Not inspected");
            setup.ReadOnly("development-branch", "Current branch", () => workflow.Snapshot?.Branch ?? workflow.Repository?.Branch ?? "Not inspected");
            setup.ReadOnly("development-head", "Repository revision", () => workflow.Snapshot?.Head ?? workflow.Repository?.Head ?? "");
            setup.Action("development-connect", "Connect local source", Connect,
                () => !workflow.Busy && workflow.Repository != null && workflow.Snapshot != null &&
                    workflow.Session?.State == DevelopmentSourceState.Prepared, true);
            setup.Action("development-recover", "Check source / recover", () => _ = workflow.RecoverAsync(), () => !workflow.Busy && workflow.Managed);
            setup.Action("development-restore", "Restore original source", Restore, () => !workflow.Busy && workflow.Managed);

            branches = review.Context.Section("Branch and repository tools", true);
            branches.Note(() => "Create or select a feature branch explicitly. Existing files and commits are retained. A branch change does not merge anything.");
            branches.Text("development-new-branch", "New feature branch", () => branch, value => branch = value);
            branches.Action("development-create-branch", "Create feature branch", () => _ = workflow.BranchAsync(branch, true), CanChange);
            branches.ReadOnly("development-branches", "Local branches", () => string.Join(" · ", workflow.Branches.Take(30)));
            branches.Text("development-existing-branch", "Existing feature branch", () => existingBranch, value => existingBranch = value);
            branches.Action("development-select-branch", "Select feature branch", () => _ = workflow.BranchAsync(existingBranch, false), CanChange);
            branches.Action("development-folder", "Open repository / external Git client", () => EditorUtility.RevealInFinder(workflow.Repository.Root), () => !workflow.Busy && workflow.Repository != null);
            branches.Action("development-ide", "Open package manifest in IDE", () => UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(Path.Combine(workflow.Repository.Root, "package.json"), 1), () => !workflow.Busy && workflow.Repository != null);
            branches.Note(() => "Use your Git client for conflicts, pre-existing staging and advanced operations. Only feature/, codex/, fix/ and bugfix/ branches can be changed here.");

            review.Actions.Action("development-refresh", "Refresh changes", () => _ = workflow.RefreshAsync(), () => !workflow.Busy && workflow.Repository != null);
            review.Actions.Action("development-stage", "Stage selected…", () => stage(false), CanChange);
            review.Actions.Action("development-unstage", "Unstage selected…", () => stage(true), CanChange);
            review.Actions.Action("development-history", "Load recent history", () => _ = workflow.HistoryAsync(), () => !workflow.Busy && workflow.Repository != null);
            review.Commit.Text("development-message", "Commit message", () => message, value => message = value, true);
            review.Commit.Action("development-commit", "Commit staged files", Commit,
                () => CanChange() && !string.IsNullOrWhiteSpace(message) && workflow.Snapshot.Files.Any(f => f.IsStaged), true);
            review.Commit.ReadOnly("development-upstream", "Upstream", () => workflow.Snapshot == null ? "Not inspected" :
                (string.IsNullOrWhiteSpace(workflow.Snapshot.Upstream) ? "No upstream; Push creates origin/" + workflow.Snapshot.Branch : workflow.Snapshot.Upstream));
            review.Commit.ReadOnly("development-ahead-behind", "Ahead / behind", () => workflow.Snapshot == null ? "Unknown" :
                workflow.Snapshot.Ahead + " ahead · " + workflow.Snapshot.Behind + " behind (last local remote snapshot; fetch to refresh)");
            review.Commit.Action("development-fetch", "Fetch origin", () => _ = workflow.TransferAsync(false), CanChange);
            review.Commit.Action("development-push", "Push feature branch…", Push, CanChange);
            review.Commit.ReadOnly("development-pr-branches", "Pull request", () =>
                (workflow.Snapshot?.Branch ?? "Feature branch") + " → " + (workflow.Package?.DevelopmentBranch ?? "development channel"));
            review.Commit.Action("development-open-pr", "Open pull request in browser", () =>
                _ = workflow.OpenPullRequestAsync(UnityEngine.Application.OpenURL), () => !workflow.Busy &&
                    DevelopmentPullRequest.UnavailableReason(workflow.Repository, workflow.Snapshot, workflow.Package?.DevelopmentBranch).Length == 0);
            review.Commit.Note(() => "Only pushed commits enter the pull request. Verify the destination branch in your browser; reviewers, checks and merging stay there. " +
                DevelopmentPullRequest.UnavailableReason(workflow.Repository, workflow.Snapshot, workflow.Package?.DevelopmentBranch));
            cancel = DeucarianEditorWorkspaceControls.Button("Cancel operation", workflow.Cancel);
            workspace.PageActions.Add(cancel);
        }

        internal void SetPackages(IReadOnlyList<DevelopmentInstalledPackage> packages, Action<DevelopmentInstalledPackage> select)
        {
            if (packages.Count == 0) { packageForm.Note(() => "No supported installed Deucarian packages. Open Package Installer to install one."); return; }
            packageForm.Choice("development-package", "Package", packages.Select(p => p.Name).ToArray(),
                () => Math.Max(0, packages.ToList().FindIndex(p => p.Id == workflow.Package?.Id)), i => select(packages[i]));
            packageForm.EnabledWhen(() => !workflow.Busy);
        }
        internal void Refresh() { packageForm.Refresh(); review.RefreshForms(); cancel.SetEnabled(workflow.Busy); }
        private bool CanInspect() => !workflow.Busy && workflow.Package != null && !string.IsNullOrWhiteSpace(RepositoryPath);
        private bool CanChange() => !workflow.Busy && workflow.Connected && workflow.Snapshot != null;
        private void PickFolder()
        { string path = EditorUtility.OpenFolderPanel("Select the package repository root", RepositoryPath, ""); if (!string.IsNullOrEmpty(path)) RepositoryPath = path; Refresh(); }
        private void Clone()
        {
            string destination = RepositoryPath;
            DevelopmentConfirmation.Show(workflow, "Clone package repository", workflow.Package.Remote + "\n\nNew folder: " + destination +
                "\n\nA partial clone is preserved if canceled. The project is connected only after a separate review.", "Clone",
                () => _ = workflow.InspectAsync(destination, true));
        }
        private void Connect()
        {
            DevelopmentConfirmation.Show(workflow, "Connect local package source", workflow.Repository.Root + "\n" + workflow.Repository.RemoteDisplay +
                "\nBranch: " + workflow.Snapshot.Branch + "\nInstalled revision: " + workflow.Package.InstalledRevision +
                (workflow.Snapshot.IsClean ? "\nCheckout is clean." : "\nCheckout has existing changes; they are kept and will be used by this project.") +
                "\n\nUnity will resolve a local file reference. Restore the original source before committing consumer configuration.", "Connect this checkout",
                () => _ = workflow.ConnectAsync());
        }
        private void Restore()
        {
            DevelopmentConfirmation.Show(workflow, "Finish local development", "Restore the saved original source for " + workflow.Package.Name +
                "? Local commits and uncommitted files stay in the repository. A transitive package's temporary direct override is removed.", "Restore source",
                () => _ = workflow.RestoreAsync());
        }
        private void Commit()
        {
            string commitMessage = message;
            DevelopmentConfirmation.Show(workflow, "Commit package changes", workflow.Repository.Root + "\nBranch: " + workflow.Snapshot.Branch +
                "\n\nCommit only the reviewed staged files:\n" + string.Join("\n", workflow.Snapshot.Files.Where(f => f.IsStaged).Select(f => f.Path)) +
                "\n\n" + commitMessage, "Commit staged", () => _ = workflow.CommitAsync(commitMessage));
        }
        private void Push()
        {
            DevelopmentConfirmation.Show(workflow, "Push package feature branch", workflow.Repository.RemoteDisplay + "\nRemote: origin\nBranch: " +
                workflow.Snapshot.Branch + "\n\nPush local commits to this branch. Normal hooks and server protections apply.", "Push branch",
                () => _ = workflow.TransferAsync(true));
        }
    }
}
