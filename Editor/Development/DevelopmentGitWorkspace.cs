using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor.Development
{
    /// <summary>One reviewed checkout, one operation at a time, with an explicitly owned staging baseline.</summary>
    internal sealed class DevelopmentGitWorkspace
    {
        private readonly DevelopmentRepository _repository;
        private readonly DevelopmentRepositoryService _repositories;
        private readonly IDevelopmentGitRunner _git;
        private readonly IDevelopmentFileSystem _files;
        private readonly DevelopmentGitStatusReader _reader;
        private readonly DevelopmentGitInspection _inspection;
        private readonly Action _assertClaim;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private DevelopmentGitSnapshot _snapshot;
        private string _ownedIndex;
        private string _stagingBlock;

        public DevelopmentGitWorkspace(DevelopmentRepository repository, DevelopmentRepositoryService repositories,
            IDevelopmentGitRunner git, IDevelopmentFileSystem files, Action assertClaim = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
            _git = git ?? throw new ArgumentNullException(nameof(git));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _reader = new DevelopmentGitStatusReader(git, files);
            _inspection = new DevelopmentGitInspection(repository, repositories, git, files);
            _assertClaim = assertClaim;
        }

        public async Task<DevelopmentGitSnapshot> RefreshAsync(CancellationToken token)
        {
            await Enter(token).ConfigureAwait(false);
            try
            {
                await _repositories.RevalidateAsync(_repository, token).ConfigureAwait(false);
                _snapshot = await _reader.ReadAsync(_repository, token).ConfigureAwait(false);
                if (_ownedIndex == null)
                {
                    _ownedIndex = _snapshot.IndexFingerprint;
                    if (_snapshot.Files.Any(file => file.IsStaged))
                        _stagingBlock = "Existing staged work is preserved. Finish it in your Git client, then reopen this package's development page.";
                }
                if (_snapshot.IndexFingerprint != _ownedIndex)
                    _stagingBlock = "The index changed outside this page. Existing staged work is preserved; review it in your Git client and reopen the page.";
                _snapshot.StagingBlockReason = _stagingBlock ?? "";
                return _snapshot;
            }
            finally { _gate.Release(); }
        }

        public Task<IReadOnlyList<string>> GetBranchesAsync(CancellationToken token) => _inspection.GetBranchesAsync(token);
        public Task<string> GetDiffAsync(string path, bool staged, CancellationToken token) => _inspection.GetDiffAsync(path, staged, token);
        public Task<string> GetHistoryAsync(CancellationToken token) => _inspection.GetHistoryAsync(token);

        public Task CreateBranchAsync(string branch, CancellationToken token) => ChangeBranch(branch, true, token);
        public Task SelectBranchAsync(string branch, CancellationToken token) => ChangeBranch(branch, false, token);
        private Task ChangeBranch(string branch, bool create, CancellationToken token)
        {
            DevelopmentGitPolicy.RequireFeatureBranch(branch);
            return Mutate(async current =>
            {
                if (!current.IsClean) throw new DevelopmentGitException("Branch changes require a clean checkout. Preserve your changes in your Git client first.");
                if (!create)
                    (await _git.RunAsync(_repository.Root, new[] { "show-ref", "--verify", "refs/heads/" + branch }, token).ConfigureAwait(false)).RequireSuccess();
                await Require(token, create ? new[] { "checkout", "--no-overwrite-ignore", "-b", branch }
                    : new[] { "checkout", "--no-overwrite-ignore", "--no-guess", branch }).ConfigureAwait(false);
                string selected = (await _git.RunAsync(_repository.Root,
                    new[] { "symbolic-ref", "--quiet", "--short", "HEAD" }, token).ConfigureAwait(false)).RequireSuccess().Trim();
                if (selected != branch) throw new DevelopmentGitException("Git did not select the requested local branch. Refresh and review the repository.");
            }, token, false, false);
        }

        public Task StageAsync(IReadOnlyList<string> selectedPaths, CancellationToken token) =>
            ChangeIndex(selectedPaths, true, token);
        public Task UnstageAsync(IReadOnlyList<string> selectedPaths, CancellationToken token) =>
            ChangeIndex(selectedPaths, false, token);
        private Task ChangeIndex(IReadOnlyList<string> selectedPaths, bool stage, CancellationToken token)
        {
            return Mutate(async current =>
            {
                RequireStagingOwnership();
                string[] selected = RequireExplicitSelection(selectedPaths, current);
                foreach (string path in selected)
                {
                    DevelopmentChangedFile reviewed = _snapshot.Files.FirstOrDefault(file => file.Path == path);
                    if (reviewed != null && _files.Fingerprint(Path.Combine(_repository.Root, path)) != reviewed.WorktreeFingerprint)
                        throw new DevelopmentGitException("A selected file changed after review. Refresh and review the diff again.");
                }
                List<string> args = new List<string> { "--literal-pathspecs" };
                args.AddRange(stage ? new[] { "add", "--all", "--" } : new[] { "restore", "--staged", "--" });
                args.AddRange(selected);
                await Require(token, args.ToArray()).ConfigureAwait(false);
                string updated = (await _git.RunAsync(_repository.Root, new[] { "ls-files", "--stage", "-z" }, token).ConfigureAwait(false)).RequireSuccess();
                if (DevelopmentGitPolicy.UnselectedIndex(current.IndexRecords, selected) != DevelopmentGitPolicy.UnselectedIndex(updated, selected))
                {
                    _stagingBlock = "Unselected staging changed during this operation. All staged work is preserved; review it in your Git client before continuing.";
                    throw new DevelopmentGitException(_stagingBlock);
                }
            }, token, true);
        }

        public async Task<string> CommitAsync(string message, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(message) || message.Length > 4096 || message.IndexOf('\0') >= 0)
                throw new DevelopmentGitException("Enter a commit message of at most 4096 characters.");
            await Mutate(async current =>
            {
                RequireStagingOwnership();
                if (!current.Files.Any(file => file.IsStaged)) throw new DevelopmentGitException("Stage the reviewed package files first.");
                string reviewedTree = (await _git.RunAsync(_repository.Root, new[] { "write-tree" }, token).ConfigureAwait(false)).RequireSuccess().Trim();
                // Normal Git hooks and signing apply. No --all, file arguments, alternate index, or bypass flags.
                await Require(token, "commit", "-m", message).ConfigureAwait(false);
                string committedTree = (await _git.RunAsync(_repository.Root, new[] { "rev-parse", "HEAD^{tree}" }, token).ConfigureAwait(false)).RequireSuccess().Trim();
                if (committedTree != reviewedTree)
                {
                    _stagingBlock = "A Git hook changed the reviewed staged content. The commit is preserved; inspect it in your Git client before pushing.";
                    throw new DevelopmentGitException(_stagingBlock);
                }
            }, token, true).ConfigureAwait(false);
            return _snapshot.Head;
        }

        public Task FetchAsync(CancellationToken token) => Mutate(async current =>
        {
            // The one validated origin remote is shown by the UI; no pruning or arbitrary refspecs.
            await Require(token, "fetch", "--no-recurse-submodules", "--no-prune", "--no-tags", "origin",
                "+refs/heads/*:refs/remotes/origin/*").ConfigureAwait(false);
        }, token, false, false);

        public Task PushAsync(CancellationToken token) => Mutate(async current =>
        {
            RequireStagingOwnership();
            if (current.Upstream.Length != 0 && current.Upstream != "origin/" + current.Branch)
                throw new DevelopmentGitException("The upstream differs from origin and the selected branch. Review it in your Git client before pushing.");
            if (current.Behind > 0) throw new DevelopmentGitException("The branch is behind its upstream. Fetch and resolve divergence in your Git client.");
            // Push exactly this local feature branch to the same named origin branch; never force.
            await Require(token, "push", "--porcelain", "--set-upstream", "--no-follow-tags", "--no-mirror",
                "--recurse-submodules=no", "origin",
                "refs/heads/" + current.Branch + ":refs/heads/" + current.Branch).ConfigureAwait(false);
        }, token, false);

        private async Task Mutate(Func<DevelopmentGitSnapshot, Task> operation, CancellationToken token,
            bool changesIndex, bool requireFeature = true)
        {
            await Enter(token).ConfigureAwait(false);
            try
            {
                using (_files.Lock(_repository.CommonDirectory, ".deucarian-development-operation.lock"))
                {
                    _assertClaim?.Invoke();
                    await _repositories.RevalidateAsync(_repository, token).ConfigureAwait(false);
                    if (_snapshot == null) throw new DevelopmentGitException("Refresh and review the repository before acting.");
                    DevelopmentGitSnapshot current = await _reader.ReadAsync(_repository, token).ConfigureAwait(false);
                    if (current.Head != _snapshot.Head || current.Branch != _snapshot.Branch)
                        throw new DevelopmentGitException("The checked-out branch or commit changed. Refresh before acting.");
                    if (current.IndexFingerprint != _snapshot.IndexFingerprint || current.IndexFingerprint != _ownedIndex)
                        throw new DevelopmentGitException("Staged content changed outside this page. Refresh and review it in your Git client.");
                    if (current.Files.Any(file => file.IsConflict))
                        throw new DevelopmentGitException("Resolve the repository's conflicts in your external Git client first.");
                    if (requireFeature) DevelopmentGitPolicy.RequireFeatureBranch(current.Branch);
                    await operation(current).ConfigureAwait(false);
                    _snapshot = await _reader.ReadAsync(_repository, token).ConfigureAwait(false);
                    if (changesIndex || _snapshot.IsClean) _ownedIndex = _snapshot.IndexFingerprint;
                    _snapshot.StagingBlockReason = _stagingBlock ?? "";
                }
            }
            finally { _gate.Release(); }
        }
        private void RequireStagingOwnership()
        {
            if (!string.IsNullOrEmpty(_stagingBlock)) throw new DevelopmentGitException(_stagingBlock);
        }
        private string[] RequireExplicitSelection(IReadOnlyList<string> selected, DevelopmentGitSnapshot current)
        {
            if (selected == null || selected.Count == 0 || selected.Count > 256)
                throw new DevelopmentGitException("Select between 1 and 256 reviewed files.");
            foreach (string path in selected) _reader.ValidateFile(_repository.Root, path);
            HashSet<string> requested = new HashSet<string>(selected, StringComparer.Ordinal);
            string[] primary = selected.Where(path => current.Files.Any(file => file.Path == path)).ToArray();
            IReadOnlyList<string> required = DevelopmentGitPolicy.IncludeMeta(primary, current.Files);
            if (!requested.SetEquals(required))
                throw new DevelopmentGitException("Review and explicitly include the related .meta and renamed paths before staging or unstaging.");
            return requested.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }
        private async Task Enter(CancellationToken token)
        {
            if (!await _gate.WaitAsync(0, token).ConfigureAwait(false)) throw new DevelopmentGitException("A package repository operation is already running.");
        }
        private async Task Require(CancellationToken token, params string[] args) =>
            (await _git.RunAsync(_repository.Root, args, token).ConfigureAwait(false)).RequireSuccess();
    }
}
