using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentGitIntegrationTests
    {
        private const string PackageId = "com.deucarian.development-fixture";
        private readonly CancellationToken _token = CancellationToken.None;
        private string _root, _repositoryPath, _remotePath, _consumer;
        private DevelopmentGitProcessRunner _runner;
        private DevelopmentFileSystem _files;
        private DevelopmentRepositoryService _repositories;
        private DevelopmentRepository _repository;
        private DevelopmentGitWorkspace _workspace;

        [SetUp]
        public async Task SetUp()
        {
            string storage = Environment.GetEnvironmentVariable("DEUCARIAN_TEST_ARTIFACT_ROOT");
            if (string.IsNullOrEmpty(storage)) storage = Path.DirectorySeparatorChar == '\\'
                ? "D:/Codex-storage/validation/package-development-20260910/git-tests"
                : Path.Combine(Path.GetTempPath(), "deucarian-package-development-git-tests");
            _root = Path.Combine(storage, Guid.NewGuid().ToString("N"));
            _repositoryPath = Path.Combine(_root, "Package");
            _remotePath = Path.Combine(_root, "Fixture.git");
            _consumer = Path.Combine(_root, "Consumer");
            Directory.CreateDirectory(_repositoryPath);
            Directory.CreateDirectory(_consumer);
            _runner = new DevelopmentGitProcessRunner();
            _files = new DevelopmentFileSystem();
            _repositories = new DevelopmentRepositoryService(_runner, _files, true);
            await Git(_repositoryPath, "init", "--initial-branch=develop");
            await ConfigureIdentity(_repositoryPath);
            File.WriteAllText(Path.Combine(_repositoryPath, "package.json"), "{\"name\":\"" + PackageId + "\",\"version\":\"1.0.0\"}");
            Write("Asset.txt", "original\n"); Write("Asset.txt.meta", "guid: fixture-asset\n"); Write("Other.txt", "other\n");
            await Git(_repositoryPath, "add", ".");
            await Git(_repositoryPath, "commit", "-m", "Seed disposable package");
            await Git(_root, "init", "--bare", _remotePath);
            await Git(_repositoryPath, "remote", "add", "origin", _remotePath);
            await Git(_repositoryPath, "push", "-u", "origin", "develop");
            await Git(_repositoryPath, "checkout", "-b", "feature/fixture");
            _repository = await _repositories.PreviewAsync(PackageId, _repositoryPath, _consumer, _remotePath, _token);
            _workspace = new DevelopmentGitWorkspace(_repository, _repositories, _runner, _files);
            await _workspace.RefreshAsync(_token);
        }

        // Fixtures are retained beneath the explicit validation root as inspectable evidence.
        [Test]
        public async Task SelectedStageCommitPushPreservesUnselectedConsumerAndWorkingFiles()
        {
            await Git(_consumer, "init", "--initial-branch=develop");
            await ConfigureIdentity(_consumer);
            File.WriteAllText(Path.Combine(_consumer, "consumer.txt"), "untouched\n");
            await Git(_consumer, "add", ".");
            await Git(_consumer, "commit", "-m", "Consumer baseline");
            string consumerHead = await Git(_consumer, "rev-parse", "HEAD");
            string consumerIndex = await Git(_consumer, "ls-files", "--stage", "-z");
            Write("Asset.txt", "package change\n"); Write("Asset.txt.meta", "guid: changed-pair\n"); Write("Other.txt", "keep unstaged\n");
            DevelopmentGitSnapshot snapshot = await _workspace.RefreshAsync(_token);
            await _workspace.StageAsync(DevelopmentGitPolicy.IncludeMeta(new[] { "Asset.txt" }, snapshot.Files), _token);
            string commit = await _workspace.CommitAsync("Selected package and meta only", _token);
            StringAssert.Contains("Other.txt", await Git(_repositoryPath, "diff", "--name-only"));
            StringAssert.DoesNotContain("Other.txt", await Git(_repositoryPath, "show", "--format=", "--name-only", "HEAD"));
            await _workspace.PushAsync(_token);
            Assert.AreEqual(commit, (await Git(_remotePath, "rev-parse", "refs/heads/feature/fixture")).Trim());
            Assert.AreEqual(consumerHead, await Git(_consumer, "rev-parse", "HEAD"));
            Assert.AreEqual(consumerIndex, await Git(_consumer, "ls-files", "--stage", "-z"));
        }

        [Test]
        public async Task PreStagedWorkAndExternalIndexDriftFailClosed()
        {
            Write("Other.txt", "pre-staged\n");
            await Git(_repositoryPath, "add", "Other.txt");
            string existing = await Git(_repositoryPath, "ls-files", "--stage", "-z");
            DevelopmentGitWorkspace attached = new DevelopmentGitWorkspace(_repository, _repositories, _runner, _files);
            DevelopmentGitSnapshot state = await attached.RefreshAsync(_token);
            Assert.IsNotEmpty(state.StagingBlockReason);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await attached.CommitAsync("Do not absorb", _token));
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.CommitAsync("Drift blocked", _token));
            Assert.AreEqual(existing, await Git(_repositoryPath, "ls-files", "--stage", "-z"));
        }

        [Test]
        public async Task StageRequiresReviewedMetaAndDetectsConcurrentFileEdit()
        {
            Write("Asset.txt", "first\n"); Write("Asset.txt.meta", "guid: pair\n");
            var state = await _workspace.RefreshAsync(_token);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.StageAsync(new[] { "Asset.txt" }, _token));
            Write("Asset.txt", "concurrent\n");
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.StageAsync(
                DevelopmentGitPolicy.IncludeMeta(new[] { "Asset.txt" }, state.Files), _token));
            Assert.AreEqual("", (await Git(_repositoryPath, "diff", "--cached", "--name-only")).Trim());
        }

        [Test]
        public async Task NewBinaryDeletedAndLiteralPathsRoundTripThroughIndex()
        {
            string literal = "a [1] $(literal); & name.txt";
            Write(literal, "literal\n");
            File.WriteAllBytes(Path.Combine(_repositoryPath, "binary.bin"), new byte[] { 1, 0, 2, 3 });
            File.Delete(Path.Combine(_repositoryPath, "Other.txt"));
            var state = await _workspace.RefreshAsync(_token);
            Assert.IsTrue(state.Files.Single(file => file.Path == "binary.bin").IsBinary);
            StringAssert.Contains("Binary file", await _workspace.GetDiffAsync("binary.bin", false, _token));
            await _workspace.StageAsync(new[] { literal, "binary.bin", "Other.txt" }, _token);
            await _workspace.UnstageAsync(new[] { literal, "binary.bin", "Other.txt" }, _token);
            Assert.AreEqual("", (await Git(_repositoryPath, "diff", "--cached", "--name-only")).Trim());
            Assert.IsTrue(File.Exists(Path.Combine(_repositoryPath, literal)));
            Assert.IsFalse(File.Exists(Path.Combine(_repositoryPath, "Other.txt")));
        }

        [Test]
        public async Task DirtyCheckoutNeverSwitchesBranchAndSharedBranchesCannotCommit()
        {
            Write("Other.txt", "dirty\n");
            await _workspace.RefreshAsync(_token);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.CreateBranchAsync("feature/new", _token));
            Assert.AreEqual("feature/fixture", (await Git(_repositoryPath, "branch", "--show-current")).Trim());
            Write("Other.txt", "other\n");
            await Git(_repositoryPath, "checkout", "develop");
            await _workspace.RefreshAsync(_token);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.CommitAsync("Protected", _token));
            await _workspace.CreateBranchAsync("codex/explicit", _token);
            CollectionAssert.Contains(await _workspace.GetBranchesAsync(_token), "codex/explicit");
        }

        [Test]
        public async Task ConsumerParentWorktreesIdentityAndCacheAreRejected()
        {
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _repositories.PreviewAsync(PackageId,
                _repositoryPath, _repositoryPath, _remotePath, _token));
            string nestedConsumer = Path.Combine(_repositoryPath, "ConsumerChild");
            Directory.CreateDirectory(nestedConsumer);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _repositories.PreviewAsync(PackageId,
                _repositoryPath, nestedConsumer, _remotePath, _token));
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _repositories.PreviewAsync("com.deucarian.wrong",
                _repositoryPath, _consumer, _remotePath, _token));
            string consumerWorktree = Path.Combine(_root, "ConsumerWorktree");
            await Git(_repositoryPath, "worktree", "add", "-b", "feature/consumer", consumerWorktree);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _repositories.PreviewAsync(PackageId,
                _repositoryPath, consumerWorktree, _remotePath, _token));
        }

        [Test]
        public async Task LinkedPackageWorktreeIsValidButRemoteDriftBlocksMutation()
        {
            string linked = Path.Combine(_root, "LinkedPackage");
            await Git(_repositoryPath, "worktree", "add", "-b", "feature/linked", linked);
            var preview = await _repositories.PreviewAsync(PackageId, linked, _consumer, _remotePath, _token);
            Assert.IsTrue(preview.HasLinkedWorktrees);
            Assert.IsFalse(DevelopmentGitPolicy.SamePath(preview.CommonDirectory, preview.GitDirectory));
            await Git(_repositoryPath, "remote", "set-url", "--push", "origin", Path.Combine(_root, "Unexpected.git"));
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.PushAsync(_token));
        }

        [Test]
        public async Task CloneUsesDevelopAndNeverOverwritesExistingDestination()
        {
            string destination = Path.Combine(_root, "ClonedPackage");
            DevelopmentRepository clone = await _repositories.CloneAsync(PackageId, _remotePath, destination, _consumer, _token);
            Assert.AreEqual("develop", clone.Branch);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _repositories.CloneAsync(PackageId,
                _remotePath, destination, _consumer, _token));
            Assert.IsTrue(File.Exists(Path.Combine(destination, "package.json")));
        }

        [Test]
        public async Task PushRejectionAndAheadBehindAreTruthfulWithoutForce()
        {
            await _workspace.PushAsync(_token);
            string peer = Path.Combine(_root, "Peer");
            await Git(_root, "clone", "--branch", "feature/fixture", _remotePath, peer);
            await ConfigureIdentity(peer);
            File.WriteAllText(Path.Combine(peer, "peer.txt"), "peer\n");
            await Git(peer, "add", "peer.txt"); await Git(peer, "commit", "-m", "Peer commit"); await Git(peer, "push");
            Write("Other.txt", "local divergence\n");
            await _workspace.RefreshAsync(_token); await _workspace.StageAsync(new[] { "Other.txt" }, _token);
            await _workspace.CommitAsync("Local divergence", _token);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.PushAsync(_token));
            await _workspace.FetchAsync(_token);
            var state = await _workspace.RefreshAsync(_token);
            Assert.AreEqual(1, state.Ahead); Assert.AreEqual(1, state.Behind);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.PushAsync(_token));
            Assert.AreEqual((await Git(peer, "rev-parse", "HEAD")).Trim(),
                (await Git(_remotePath, "rev-parse", "feature/fixture")).Trim());
        }

        [Test]
        public async Task CancellationAndOperationLockPreventMutation()
        {
            using (CancellationTokenSource cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () =>
                    await _workspace.CreateBranchAsync("feature/cancelled", cancelled.Token));
            }
            using (_files.Lock(_repository.CommonDirectory, ".deucarian-development-operation.lock"))
                Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.CreateBranchAsync("feature/locked", _token));
            StringAssert.DoesNotContain("feature/cancelled", await Git(_repositoryPath, "branch", "--list"));
            await Task.CompletedTask;
        }

        [Test]
        public async Task RawGitErrorsAndOutputOverflowAreNotExposed()
        {
            DevelopmentGitResult overflow = await new DevelopmentGitProcessRunner(120000, 1).RunAsync(
                _repositoryPath, new[] { "log", "--oneline" }, _token);
            Assert.IsFalse(overflow.Success); StringAssert.Contains("safe display limit", overflow.Failure);
            Assert.IsEmpty(overflow.Output);
            DevelopmentGitResult invalid = await _runner.RunAsync(_repositoryPath,
                new[] { "show", "not-a-revision-with-private-data" }, _token);
            StringAssert.DoesNotContain("private-data", invalid.Failure);
        }

        [Test]
        public async Task TagOrTrackedPathCannotMasqueradeAsSelectedBranch()
        {
            await Git(_repositoryPath, "tag", "feature/tag-only");
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.SelectBranchAsync("feature/tag-only", _token));
            Directory.CreateDirectory(Path.Combine(_repositoryPath, "feature"));
            Write("feature/path-only", "tracked file\n");
            await _workspace.RefreshAsync(_token);
            await _workspace.StageAsync(new[] { "feature/path-only" }, _token);
            await _workspace.CommitAsync("Fixture path", _token);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.SelectBranchAsync("feature/path-only", _token));
            Assert.AreEqual("feature/fixture", (await Git(_repositoryPath, "branch", "--show-current")).Trim());
        }

        [Test]
        public async Task NormalHookStagingDriftIsDetectedAndPushRemainsBlocked()
        {
            string hooks = Path.Combine(_root, "fixture-hooks");
            Directory.CreateDirectory(hooks);
            File.WriteAllText(Path.Combine(hooks, "pre-commit"), "#!/bin/sh\ngit add -- Other.txt\n");
            if (Path.DirectorySeparatorChar != '\\')
            {
                var permissions = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("chmod",
                    "+x " + DevelopmentGitProcessRunner.QuoteArgument(Path.Combine(hooks, "pre-commit")))
                { UseShellExecute = false, CreateNoWindow = true });
                permissions.WaitForExit(); Assert.AreEqual(0, permissions.ExitCode); permissions.Dispose();
            }
            await Git(_repositoryPath, "config", "core.hooksPath", hooks);
            Write("Asset.txt", "reviewed\n"); Write("Other.txt", "hook adds this\n");
            await _workspace.RefreshAsync(_token);
            await _workspace.StageAsync(new[] { "Asset.txt" }, _token);
            string priorHead = await Git(_repositoryPath, "rev-parse", "HEAD");
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.CommitAsync("Normal hook runs", _token));
            Assert.AreNotEqual(priorHead, await Git(_repositoryPath, "rev-parse", "HEAD"));
            await _workspace.RefreshAsync(_token);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await _workspace.PushAsync(_token));
        }

        [Test]
        public async Task RenameAndMetaOriginsAreExplicitlyStagedAndPreserved()
        {
            File.Move(Path.Combine(_repositoryPath, "Asset.txt"), Path.Combine(_repositoryPath, "Moved.txt"));
            File.Move(Path.Combine(_repositoryPath, "Asset.txt.meta"), Path.Combine(_repositoryPath, "Moved.txt.meta"));
            var state = await _workspace.RefreshAsync(_token);
            var selected = DevelopmentGitPolicy.IncludeMeta(new[] { "Asset.txt", "Moved.txt" }, state.Files);
            await _workspace.StageAsync(selected, _token);
            state = await _workspace.RefreshAsync(_token);
            Assert.IsTrue(state.Files.Any(file => file.OriginalPath == "Asset.txt" && file.Path == "Moved.txt"));
            await _workspace.CommitAsync("Move asset and meta together", _token);
            Assert.IsTrue(File.Exists(Path.Combine(_repositoryPath, "Moved.txt.meta")));
            Assert.IsFalse(File.Exists(Path.Combine(_repositoryPath, "Asset.txt.meta")));
        }

        [Test]
        public async Task UnselectedIndexChangeDuringStageIsPreservedAndNotAdopted()
        {
            Write("Asset.txt", "selected\n"); Write("Other.txt", "external staged change\n");
            var race = new StageRaceRunner(_runner);
            var workspace = new DevelopmentGitWorkspace(_repository, _repositories, race, _files);
            await workspace.RefreshAsync(_token);
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await workspace.StageAsync(new[] { "Asset.txt" }, _token));
            var state = await workspace.RefreshAsync(_token);
            Assert.IsNotEmpty(state.StagingBlockReason);
            StringAssert.Contains("Other.txt", await Git(_repositoryPath, "diff", "--cached", "--name-only"));
            Assert.ThrowsAsync<DevelopmentGitException>(async () => await workspace.CommitAsync("Do not absorb external work", _token));
        }

        private sealed class StageRaceRunner : IDevelopmentGitRunner
        {
            private readonly IDevelopmentGitRunner _runner;
            public StageRaceRunner(IDevelopmentGitRunner runner) { _runner = runner; }
            public async Task<DevelopmentGitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken token)
            {
                var result = await _runner.RunAsync(directory, arguments, token).ConfigureAwait(false);
                if (arguments.Contains("add"))
                    (await _runner.RunAsync(directory, new[] { "add", "Other.txt" }, token).ConfigureAwait(false)).RequireSuccess();
                return result;
            }
        }

        private void Write(string relative, string content) => File.WriteAllText(Path.Combine(_repositoryPath, relative), content);
        private async Task ConfigureIdentity(string root)
        {
            await Git(root, "config", "user.name", "Disposable Fixture");
            await Git(root, "config", "user.email", "fixture@example.invalid");
            await Git(root, "config", "commit.gpgsign", "false");
        }
        private async Task<string> Git(string root, params string[] arguments) =>
            (await _runner.RunAsync(root, arguments, _token)).RequireSuccess();
    }
}
