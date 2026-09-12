using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal sealed class DevelopmentRepositoryService
    {
        private readonly IDevelopmentGitRunner _git;
        private readonly IDevelopmentFileSystem _files;
        private readonly bool _allowFixtureRemotes;
        public DevelopmentRepositoryService(IDevelopmentGitRunner git, IDevelopmentFileSystem files,
            bool allowFixtureRemotes = false)
        { _git = git ?? throw new ArgumentNullException(nameof(git)); _files = files ?? throw new ArgumentNullException(nameof(files)); _allowFixtureRemotes = allowFixtureRemotes; }

        public async Task<DevelopmentRepository> PreviewAsync(string packageId, string selectedPath,
            string consumerRoot, string expectedRemote, CancellationToken token)
        {
            DevelopmentGitPolicy.RequireAbsolutePath(selectedPath);
            DevelopmentGitPolicy.RequireAbsolutePath(consumerRoot);
            if (!Regex.IsMatch(packageId ?? "", @"^com\.deucarian\.[a-z0-9][a-z0-9.-]*$"))
                throw new DevelopmentGitException("Select an installed Deucarian package.");
            string expectedIdentity = DevelopmentGitPolicy.RemoteIdentity(expectedRemote, _allowFixtureRemotes);
            string root = _files.Canonicalize(selectedPath);
            string consumer = _files.Canonicalize(consumerRoot);
            DevelopmentCheckoutLocation.RequireAllowed(root, consumer, packageId);
            if (DevelopmentCheckoutLocation.IsDefault(consumer, packageId, root))
                await new DevelopmentLocalCheckoutStorage(_git, _files).RequireIgnoredAsync(consumer, packageId, token).ConfigureAwait(false);
            string actualRoot = _files.Canonicalize(await Required(root, token, "rev-parse", "--show-toplevel").ConfigureAwait(false));
            if (!DevelopmentGitPolicy.SamePath(root, actualRoot))
                throw new DevelopmentGitException("Select the package repository root. Nested package layouts are not supported in this version.");
            string common = await GitPath(root, "--git-common-dir", token).ConfigureAwait(false);
            string gitDirectory = await GitPath(root, "--git-dir", token).ConfigureAwait(false);
            DevelopmentCheckoutLocation.RequireAllowed(common, consumer, packageId, true);
            DevelopmentCheckoutLocation.RequireAllowed(gitDirectory, consumer, packageId, true);
            await RejectConsumerGit(root, common, consumer, token).ConfigureAwait(false);
            RequirePackageIdentity(root, packageId);
            string[] urls = (await Required(root, token, "remote", "get-url", "--all", "origin").ConfigureAwait(false)).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string[] pushUrls = (await Required(root, token, "remote", "get-url", "--push", "--all", "origin").ConfigureAwait(false)).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (urls.Length != 1 || pushUrls.Length != 1 ||
                DevelopmentGitPolicy.RemoteIdentity(urls[0], _allowFixtureRemotes) != expectedIdentity ||
                DevelopmentGitPolicy.RemoteIdentity(pushUrls[0], _allowFixtureRemotes) != expectedIdentity)
                throw new DevelopmentGitException("The origin fetch/push remote differs from the selected package repository. Review it in your Git client.");
            string worktrees = await Required(root, token, "worktree", "list", "--porcelain").ConfigureAwait(false);
            string branch = await Optional(root, token, "symbolic-ref", "--quiet", "--short", "HEAD").ConfigureAwait(false);
            return new DevelopmentRepository
            {
                Root = root, ConsumerRoot = consumer, PackageId = packageId, ExpectedRemote = expectedRemote,
                CommonDirectory = common, GitDirectory = gitDirectory, RemoteIdentity = expectedIdentity,
                Branch = branch, Head = await Required(root, token, "rev-parse", "--verify", "HEAD").ConfigureAwait(false),
                HasLinkedWorktrees = worktrees.Split('\n').Count(line => line.StartsWith("worktree ", StringComparison.Ordinal)) > 1
            };
        }

        public async Task<DevelopmentRepository> RevalidateAsync(DevelopmentRepository repository, CancellationToken token)
        {
            DevelopmentRepository current = await PreviewAsync(repository.PackageId, repository.Root,
                repository.ConsumerRoot, repository.ExpectedRemote, token).ConfigureAwait(false);
            if (!DevelopmentGitPolicy.SamePath(current.CommonDirectory, repository.CommonDirectory) ||
                !DevelopmentGitPolicy.SamePath(current.GitDirectory, repository.GitDirectory))
                throw new DevelopmentGitException("Repository metadata moved. Disconnect and select the repository again.");
            return current;
        }

        public async Task<DevelopmentRepository> CloneAsync(string packageId, string remote, string destination,
            string consumerRoot, CancellationToken token, string sourceBranch = "develop")
        {
            DevelopmentGitPolicy.RemoteIdentity(remote, _allowFixtureRemotes);
            DevelopmentGitPolicy.RequireAbsolutePath(destination);
            DevelopmentGitPolicy.RequireAbsolutePath(consumerRoot);
            DevelopmentGitPolicy.RequireSourceBranch(sourceBranch);
            string full = Path.GetFullPath(destination);
            if (DevelopmentCheckoutLocation.IsDefault(consumerRoot, packageId, full))
                await new DevelopmentLocalCheckoutStorage(_git, _files).PrepareAsync(consumerRoot, packageId, token).ConfigureAwait(false);
            string parent = _files.Canonicalize(Path.GetDirectoryName(full));
            string canonicalDestination = Path.Combine(parent, Path.GetFileName(full));
            DevelopmentCheckoutLocation.RequireAllowed(canonicalDestination, _files.Canonicalize(consumerRoot), packageId);
            if (_files.FileExists(canonicalDestination) || _files.DirectoryExists(canonicalDestination))
                throw new DevelopmentGitException("Choose a new, absent clone folder. Existing repositories require explicit reuse.");
            // Git clone creates this folder. Cancellation/failure never removes a partial clone or user files.
            (await _git.RunAsync(parent, new[] { "clone", "--no-recurse-submodules", "--origin", "origin", "--branch", sourceBranch, "--", remote, canonicalDestination }, token).ConfigureAwait(false)).RequireSuccess();
            DevelopmentRepository repository = await PreviewAsync(packageId, canonicalDestination, consumerRoot, remote, token).ConfigureAwait(false);
            if (repository.Branch != sourceBranch)
                throw new DevelopmentGitException("The cloned revision is not the chosen branch. Review the retained clone in your Git client.");
            return repository;
        }

        private void RequirePackageIdentity(string root, string packageId)
        {
            string manifest = Path.Combine(root, "package.json");
            if (!_files.FileExists(manifest) || !DevelopmentGitPolicy.ContainsPath(root, _files.Canonicalize(manifest)))
                throw new DevelopmentGitException("The selected repository has no package manifest at its root.");
            string identity;
            try { identity = new SourceManifestJson(_files.ReadText(manifest)).Parse().Members.SingleOrDefault(member => member.Name == "name")?.Value; }
            catch (InvalidOperationException) { throw new DevelopmentGitException("The selected package manifest is invalid."); }
            if (identity != packageId)
                throw new DevelopmentGitException("The selected repository package ID differs from the installed package.");
        }
        private async Task RejectConsumerGit(string root, string common, string consumer, CancellationToken token)
        {
            DevelopmentGitResult consumerGit = await _git.RunAsync(consumer, new[] { "rev-parse", "--show-toplevel" }, token).ConfigureAwait(false);
            if (!consumerGit.Success)
            {
                // A disposable consumer need not itself use Git; other failures are not evidence of absence.
                if (consumerGit.Failure == DevelopmentGitProcessRunner.NotRepository) return;
                consumerGit.RequireSuccess();
            }
            string consumerRepository = _files.Canonicalize(consumerGit.Output.Trim());
            string consumerCommon = await GitPath(consumer, "--git-common-dir", token).ConfigureAwait(false);
            if (DevelopmentGitPolicy.SamePath(root, consumerRepository) || DevelopmentGitPolicy.SamePath(common, consumerCommon) ||
                DevelopmentGitPolicy.ContainsPath(common, consumerRepository))
                throw new DevelopmentGitException("The consumer repository and its linked worktrees cannot be a package Git target.");
        }
        private async Task<string> GitPath(string root, string option, CancellationToken token)
        {
            string path = await Required(root, token, "rev-parse", option).ConfigureAwait(false);
            return _files.Canonicalize(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        }
        private async Task<string> Required(string root, CancellationToken token, params string[] args) =>
            (await _git.RunAsync(root, args, token).ConfigureAwait(false)).RequireSuccess().Trim();
        private async Task<string> Optional(string root, CancellationToken token, params string[] args)
        {
            DevelopmentGitResult result = await _git.RunAsync(root, args, token).ConfigureAwait(false);
            return result.Success ? result.Output.Trim() : "";
        }
    }
}
