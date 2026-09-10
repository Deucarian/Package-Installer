using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor.Development
{
    // Coordinates injected owners. The page supplies explicit user intent; no command runs on refresh.
    internal sealed class DevelopmentWorkflow : IDisposable
    {
        private readonly string projectRoot;
        private readonly DevelopmentRepositoryService repositories;
        private readonly IDevelopmentGitRunner runner;
        private readonly IDevelopmentFileSystem files;
        private readonly PackageDevelopmentSourceService sources;
        private CancellationTokenSource cancellation;
        private DevelopmentGitWorkspace git;
        private bool disposed;

        internal DevelopmentWorkflow(string projectRoot, DevelopmentRepositoryService repositories,
            IDevelopmentGitRunner runner, IDevelopmentFileSystem files, PackageDevelopmentSourceService sources)
        { this.projectRoot = projectRoot; this.repositories = repositories; this.runner = runner; this.files = files; this.sources = sources; }

        internal DevelopmentInstalledPackage Package { get; private set; }
        internal DevelopmentRepository Repository { get; private set; }
        internal DevelopmentGitSnapshot Snapshot { get; private set; }
        internal PackageDevelopmentSession Session { get; private set; }
        internal IReadOnlyList<string> Branches { get; private set; } = Array.Empty<string>();
        internal bool Busy => cancellation != null;
        internal bool Managed => Session != null && Session.IsManaged && Session.State != DevelopmentSourceState.Prepared;
        internal bool Connected => Session?.State == DevelopmentSourceState.Connected;
        internal string Status { get; private set; } = "Select an installed package, then inspect an existing checkout or clone a new one.";
        internal string Diff { get; private set; } = "Select a changed file to inspect it.";
        internal string History { get; private set; } = "";
        internal event Action Changed;
        internal string ConfirmationScope => Package?.Id + "|" + Repository?.Root + "|" + Snapshot?.Head + "|" + Snapshot?.Branch + "|" + Session?.State;

        internal void AcceptConfirmation(string scope, Action accepted)
        {
            if (disposed) return;
            if (!Busy && scope == ConfirmationScope) accepted();
            else { Status = "The selected operation changed while its confirmation was open. Review it again."; Changed?.Invoke(); }
        }

        internal void SelectPackage(DevelopmentInstalledPackage package)
        {
            if (Busy) return;
            Package = package; Repository = null; Snapshot = null; git = null;
            Branches = Array.Empty<string>(); Diff = "Select a changed file to inspect it."; History = "";
            try
            {
                Session = sources.GetSessions().FirstOrDefault(s => s.PackageId == package.Id && s.IsManaged);
                Status = Session?.Message ?? "Inspect the repository before connecting this project.";
            }
            catch (InvalidOperationException exception) { Session = null; Status = exception.Message; }
            Changed?.Invoke();
        }

        internal Task InspectAsync(string path, bool clone) => RunAsync(clone ? "Cloning package repository…" : "Inspecting package repository…", async token =>
        {
            var candidate = clone
                ? await repositories.CloneAsync(Package.Id, Package.Remote, path, projectRoot, token)
                : await repositories.PreviewAsync(Package.Id, path, projectRoot, Package.Remote, token);
            if (Managed && (!DevelopmentGitPolicy.SamePath(candidate.Root, Session.RepositoryRoot) ||
                !DevelopmentGitPolicy.SamePath(candidate.CommonDirectory, Session.CommonDirectory)))
                throw new InvalidOperationException("This project is connected to a different checkout. Restore its source before selecting another repository.");
            var prepared = Managed ? Session : sources.Prepare(Package.Id, candidate.Root, candidate.CommonDirectory,
                Package.InstalledReference, Package.InstalledResolvedPath, Package.Source);
            var candidateGit = new DevelopmentGitWorkspace(candidate, repositories, runner, files,
                () => sources.AssertCheckoutOwned(prepared));
            var snapshot = await candidateGit.RefreshAsync(token);
            var branches = await candidateGit.GetBranchesAsync(token);
            Repository = candidate; Session = prepared; git = candidateGit; Snapshot = snapshot; Branches = branches;
            Status = Managed ? "Local development repository inspected." : "Review the repository, current branch and installed revision, then connect local source.";
        });

        internal Task ConnectAsync() => RunAsync("Connecting local source; waiting for Unity resolution and compilation…", async token =>
        {
            if (Repository == null || Snapshot == null || Session?.State != DevelopmentSourceState.Prepared)
                throw new InvalidOperationException("Complete repository inspection before connecting local source.");
            var current = await repositories.RevalidateAsync(Repository, token);
            if (current.Branch != Snapshot.Branch || current.Head != Snapshot.Head)
                throw new InvalidOperationException("The repository branch changed since inspection. Inspect it again before connecting.");
            await sources.ConnectAsync(Session, token);
            ReloadSession();
            Status = Session.Message;
        });

        internal Task RecoverAsync() => RunAsync("Checking source resolution and compilation…", async token =>
        { await sources.RecoverAsync(Package.Id, token); ReloadSession(); Status = Session.Message; });

        internal Task RestoreAsync() => RunAsync("Restoring original source; keeping the local repository…", async token =>
        { await sources.RestoreAsync(Package.Id, token); ReloadSession(); Status = Session.Message; });

        internal Task RefreshAsync() => RunAsync("Refreshing repository status…", async token =>
        { await RefreshGit(token); Status = "Repository status refreshed. Compilation and tests are separate checks."; });

        internal Task BranchAsync(string branch, bool create) => RunAsync("Changing the package feature branch…", async token =>
        { RequireConnected(); if (create) await git.CreateBranchAsync(branch, token); else await git.SelectBranchAsync(branch, token); await RefreshGit(token); Status = "Feature branch selected."; });

        internal Task StageAsync(IReadOnlyList<string> paths, bool unstage) => RunAsync(unstage ? "Unstaging selected package files…" : "Staging selected package files…", async token =>
        { RequireConnected(); if (unstage) await git.UnstageAsync(paths, token); else await git.StageAsync(paths, token); await RefreshGit(token); Status = "Selected package staging updated."; });

        internal Task CommitAsync(string message) => RunAsync("Committing reviewed staged package files…", async token =>
        { RequireConnected(); await git.CommitAsync(message, token); await RefreshGit(token); Status = "Package commit created. Push is a separate action."; });

        internal Task TransferAsync(bool push) => RunAsync(push ? "Pushing package branch to origin…" : "Fetching origin…", async token =>
        { RequireConnected(); if (push) await git.PushAsync(token); else await git.FetchAsync(token); await RefreshGit(token); Status = push ? "Package branch pushed to origin." : "Origin fetched. No merge or working-file changes requested."; });

        internal Task InspectDiffAsync(string path, bool staged) => RunAsync("Reading selected package diff…", async token =>
        { Diff = await git.GetDiffAsync(path, staged, token); Status = "Selected diff loaded locally."; });

        internal Task HistoryAsync() => RunAsync("Reading recent package history…", async token =>
        { History = await git.GetHistoryAsync(token); Status = "Recent package history loaded."; });

        internal IReadOnlyList<string> ExpandSelection(IEnumerable<string> paths)
            => DevelopmentGitPolicy.IncludeMeta(paths, Snapshot.Files);

        internal void Cancel() { if (!Busy) return; cancellation.Cancel(); Status = "Cancellation requested; waiting for the active operation to settle."; Changed?.Invoke(); }

        private void ReloadSession() => Session = sources.GetSessions().FirstOrDefault(s => s.PackageId == Package.Id);
        private void RequireConnected()
        { if (!Connected) throw new InvalidOperationException("Connect and resolve this package source before changing its repository."); }
        private async Task RefreshGit(CancellationToken token)
        { Snapshot = await git.RefreshAsync(token); Branches = await git.GetBranchesAsync(token); }

        private async Task RunAsync(string message, Func<CancellationToken, Task> operation)
        {
            if (Busy || disposed) return;
            cancellation = new CancellationTokenSource(); Status = message; Changed?.Invoke();
            try { await operation(cancellation.Token); }
            catch (OperationCanceledException) { Status = "Operation canceled. Refresh the repository or check source recovery before continuing."; }
            catch (DevelopmentGitException exception) { Status = exception.Message; }
            catch (InvalidOperationException exception) { Status = exception.Message; }
            catch (Exception) { Status = "Operation failed. No automatic retry was made. Inspect the repository and source recovery before continuing."; }
            finally { cancellation.Dispose(); cancellation = null; if (!disposed) Changed?.Invoke(); }
        }

        public void Dispose() { if (disposed) return; disposed = true; cancellation?.Cancel(); Changed = null; }
    }
}
