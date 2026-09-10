using System;
using System.Collections;
using UnityEngine.TestTools;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentGitBoundaryTests
    {
        [UnityTest]
        public IEnumerator MissingGitAndAuthenticationFailuresAreSanitizedAndPropagated() => DevelopmentAsyncTest.Run(MissingGitAndAuthenticationFailuresAreSanitizedAndPropagatedAsync);

        public async Task MissingGitAndAuthenticationFailuresAreSanitizedAndPropagatedAsync()
        {
            DevelopmentGitResult missing = await new FailureRunner("Git is unavailable.").RunAsync(
                Path.GetFullPath("."), new[] { "status" }, CancellationToken.None);
            Assert.Throws<DevelopmentGitException>(() => missing.RequireSuccess());
            DevelopmentGitResult auth = new DevelopmentGitResult(128, "", "Git authentication failed. Use your existing credential manager.");
            StringAssert.Contains("credential manager", Assert.Throws<DevelopmentGitException>(() => auth.RequireSuccess()).Message);
        }

        [UnityTest]
        public IEnumerator RunnerHonorsTimeoutAndCancellationWhileGitIsRunning() => DevelopmentAsyncTest.Run(RunnerHonorsTimeoutAndCancellationWhileGitIsRunningAsync);

        public async Task RunnerHonorsTimeoutAndCancellationWhileGitIsRunningAsync()
        {
            string root = NewFixtureDirectory();
            string[] args = { "-c", "alias.fixture-wait=!sleep 2", "fixture-wait" };
            var timed = await new DevelopmentGitProcessRunner(75).RunAsync(root, args, CancellationToken.None);
            Assert.IsFalse(timed.Success);
            StringAssert.Contains("timed out", timed.Failure);
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(75);
                await DevelopmentAsyncTest.ThrowsAsync<OperationCanceledException>( async () =>
                    await new DevelopmentGitProcessRunner().RunAsync(root, args, cancellation.Token));
            }
        }

        [UnityTest]
        public IEnumerator CancellationTerminatesDescendantsBeforeTheyCanWriteLater() => DevelopmentAsyncTest.Run(CancellationTerminatesDescendantsBeforeTheyCanWriteLaterAsync);

        public async Task CancellationTerminatesDescendantsBeforeTheyCanWriteLaterAsync()
        {
            string root = NewFixtureDirectory();
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(150);
                await DevelopmentAsyncTest.ThrowsAsync<OperationCanceledException>( async () =>
                    await new DevelopmentGitProcessRunner().RunAsync(root,
                        new[] { "-c", "alias.fixture-child=!sh -c '(sleep 1; echo escaped > escaped.txt) & wait'", "fixture-child" }, cancellation.Token));
            }
            await Task.Delay(1300);
            Assert.IsFalse(File.Exists(Path.Combine(root, "escaped.txt")), "A Git descendant survived cancellation and wrote after the operation released.");
        }

        [UnityTest]
        public IEnumerator InheritedGitDirectoryCannotRedirectTheProcessToConsumer() => DevelopmentAsyncTest.Run(InheritedGitDirectoryCannotRedirectTheProcessToConsumerAsync);

        public async Task InheritedGitDirectoryCannotRedirectTheProcessToConsumerAsync()
        {
            string root = NewFixtureDirectory();
            string package = Path.Combine(root, "Package"), consumer = Path.Combine(root, "Consumer");
            Directory.CreateDirectory(package); Directory.CreateDirectory(consumer);
            var runner = new DevelopmentGitProcessRunner();
            (await runner.RunAsync(package, new[] { "init" }, CancellationToken.None)).RequireSuccess();
            (await runner.RunAsync(consumer, new[] { "init" }, CancellationToken.None)).RequireSuccess();
            string previousDirectory = Environment.GetEnvironmentVariable("GIT_DIR");
            string previousWorktree = Environment.GetEnvironmentVariable("GIT_WORK_TREE");
            try
            {
                Environment.SetEnvironmentVariable("GIT_DIR", Path.Combine(consumer, ".git"));
                Environment.SetEnvironmentVariable("GIT_WORK_TREE", consumer);
                string output = (await runner.RunAsync(package, new[] { "rev-parse", "--show-toplevel" }, CancellationToken.None)).RequireSuccess();
                Assert.IsTrue(DevelopmentGitPolicy.SamePath(new DevelopmentFileSystem().Canonicalize(package),
                    new DevelopmentFileSystem().Canonicalize(output.Trim())));
            }
            finally
            {
                Environment.SetEnvironmentVariable("GIT_DIR", previousDirectory);
                Environment.SetEnvironmentVariable("GIT_WORK_TREE", previousWorktree);
            }
        }

        [Test]
        public void JunctionsOrSymlinksResolvePhysicallyAndCannotHideConsumerFiles()
        {
            string root = NewFixtureDirectory();
            string package = Path.Combine(root, "Package"), consumer = Path.Combine(root, "Consumer");
            Directory.CreateDirectory(package); Directory.CreateDirectory(consumer);
            File.WriteAllText(Path.Combine(consumer, "consumer.txt"), "consumer\n");
            string alias = Path.Combine(root, "ConsumerAlias");
            MakeJunction(alias, consumer);
            var files = new DevelopmentFileSystem();
            Assert.IsTrue(DevelopmentGitPolicy.SamePath(files.Canonicalize(alias), files.Canonicalize(consumer)));
            string escape = Path.Combine(package, "linked");
            MakeJunction(escape, consumer);
            var reader = new DevelopmentGitStatusReader(new DevelopmentGitProcessRunner(), files);
            Assert.Throws<DevelopmentGitException>(() => reader.ValidateFile(files.Canonicalize(package), "linked/consumer.txt"));
        }

        [Test]
        public void ReplacedRepositoryMetadataCannotCreateALockInConsumer()
        {
            string root = NewFixtureDirectory();
            string metadata = Path.Combine(root, "metadata"), consumer = Path.Combine(root, "Consumer");
            Directory.CreateDirectory(metadata); Directory.CreateDirectory(consumer);
            var files = new DevelopmentFileSystem();
            string previouslyValidated = files.Canonicalize(metadata);
            Directory.Delete(metadata); // Empty fixture directory only; replace it with a hostile alias.
            MakeJunction(metadata, consumer);
            Assert.Throws<DevelopmentGitException>(() => files.Lock(previouslyValidated, ".deucarian-development-operation.lock"));
            Assert.IsFalse(File.Exists(Path.Combine(consumer, ".deucarian-development-operation.lock")));
        }

        private static string NewFixtureDirectory()
        {
            string root = Path.DirectorySeparatorChar == '\\'
                ? "D:/Codex-storage/validation/package-development-20260910/git-boundaries"
                : Path.Combine(Path.GetTempPath(), "deucarian-git-boundaries");
            string path = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path); return path;
        }
        private static void MakeJunction(string alias, string destination)
        {
            // Windows junction creation is a fixture-only operation. Both directories belong to this fixture.
            string executable = Path.DirectorySeparatorChar == '\\' ? "cmd.exe" : "/bin/ln";
            string arguments = Path.DirectorySeparatorChar == '\\' ? "/c mklink /J " +
                DevelopmentGitProcessRunner.QuoteArgument(alias) + " " + DevelopmentGitProcessRunner.QuoteArgument(destination)
                : "-s " + DevelopmentGitProcessRunner.QuoteArgument(destination) + " " + DevelopmentGitProcessRunner.QuoteArgument(alias);
            using (Process process = Process.Start(new ProcessStartInfo(executable, arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                Assert.IsTrue(process.WaitForExit(10000));
                Assert.AreEqual(0, process.ExitCode, "Could not create the disposable junction fixture.");
            }
        }
        private sealed class FailureRunner : IDevelopmentGitRunner
        {
            private readonly string _failure;
            public FailureRunner(string failure) { _failure = failure; }
            public Task<DevelopmentGitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
                Task.FromResult(new DevelopmentGitResult(-1, "", _failure));
        }
    }
}
