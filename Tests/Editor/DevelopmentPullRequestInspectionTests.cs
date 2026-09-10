using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentPullRequestInspectionTests
    {
        [UnityTest]
        public IEnumerator BrowserHandoffUsesTheInstalledPackagesDevelopmentTargetAndOnlyReadsGit()
            => DevelopmentAsyncTest.Run(async () =>
            {
                foreach (string host in new[] { "github.com", "bitbucket.org" })
                {
                    var ports = new HandoffPorts(host);
                    using (var workflow = await Inspect(ports))
                    {
                        ports.Commands.Clear();
                        var opened = new List<string>();
                        await workflow.OpenPullRequestAsync(opened.Add);
                        Assert.That(opened.Count, Is.EqualTo(1));
                        string expected = host == "github.com"
                            ? "https://github.com/deucarian/handoff/compare/integration%2Fnext...feature%2Fextension?quick_pull=1"
                            : "https://bitbucket.org/deucarian/handoff/pull-requests/new?source=feature%2Fextension&t=1";
                        Assert.That(opened[0], Is.EqualTo(expected));
                        Assert.That(ports.Commands.Count(command => command == HandoffPorts.RemoteCommand), Is.EqualTo(1));
                        Assert.That(ports.Commands.First(), Is.EqualTo("rev-parse --show-toplevel"));
                        Assert.That(ports.Commands.Last(), Is.Not.EqualTo(HandoffPorts.RemoteCommand), "Local state must be revalidated after contacting origin.");
                        Assert.That(workflow.Busy, Is.False);
                        StringAssert.Contains("browser", workflow.Status);
                        Assert.That(workflow.Session.State, Is.EqualTo(DevelopmentSourceState.Connected));
                        ports.AssertNoWrites();
                    }
                }
            });

        [UnityTest]
        public IEnumerator RestoredPackageSourcesCanStillHandOffTheirRetainedPushedCheckout()
            => DevelopmentAsyncTest.Run(async () =>
            {
                var ports = new HandoffPorts();
                using (var workflow = await Inspect(ports))
                {
                    workflow.Session.State = DevelopmentSourceState.Restored;
                    Assert.That(workflow.Managed, Is.False);
                    Assert.That(workflow.Connected, Is.False);
                    var opened = new List<string>();
                    await workflow.OpenPullRequestAsync(opened.Add);
                    Assert.That(opened, Has.Count.EqualTo(1));
                    StringAssert.StartsWith("https://github.com/deucarian/handoff/compare/", opened[0]);
                    Assert.That(workflow.Session.State, Is.EqualTo(DevelopmentSourceState.Restored));
                    ports.AssertNoWrites();
                }
            });

        [UnityTest]
        public IEnumerator BranchHeadAndOriginDriftBeforeHandoffNeverReachTheRemote()
            => DevelopmentAsyncTest.Run(async () =>
            {
                foreach (Action<HandoffPorts> drift in new Action<HandoffPorts>[] {
                    p => p.Branch = "feature/changed", p => p.Head = HandoffPorts.OtherHead,
                    p => p.Remote = "https://github.com/Deucarian/Other.git",
                    p => p.PushRemote = "https://github.com/Deucarian/Other.git" })
                {
                    var ports = new HandoffPorts();
                    using (var workflow = await Inspect(ports))
                    {
                        ports.Commands.Clear(); drift(ports); int opened = 0;
                        await workflow.OpenPullRequestAsync(url => opened++);
                        Assert.That(opened, Is.Zero);
                        CollectionAssert.DoesNotContain(ports.Commands, HandoffPorts.RemoteCommand);
                        ports.AssertNoWrites();
                    }
                }
            });

        [UnityTest]
        public IEnumerator BranchHeadOriginAndUpstreamDriftDuringRemoteReadSuppressTheBrowser()
            => DevelopmentAsyncTest.Run(async () =>
            {
                foreach (Action<HandoffPorts> drift in new Action<HandoffPorts>[] {
                    p => p.Branch = "feature/changed", p => p.Head = HandoffPorts.OtherHead,
                    p => p.Remote = "https://bitbucket.org/Deucarian/Other.git",
                    p => p.PushRemote = "https://github.com/Deucarian/Other.git",
                    p => p.Upstream = "upstream/feature/extension", p => p.Upstream = "" })
                {
                    var ports = new HandoffPorts();
                    using (var workflow = await Inspect(ports))
                    {
                        ports.AfterRemoteRead = () => drift(ports); int opened = 0;
                        await workflow.OpenPullRequestAsync(url => opened++);
                        Assert.That(opened, Is.Zero);
                        Assert.That(workflow.Busy, Is.False);
                        ports.AssertNoWrites();
                    }
                }
            });

        [UnityTest]
        public IEnumerator MissingRemoteBranchesChangedRemoteHeadAndAuthenticationFailureKeepHandoffLocal()
            => DevelopmentAsyncTest.Run(async () =>
            {
                foreach (DevelopmentGitResult result in new[] {
                    new DevelopmentGitResult(0, "", ""),
                    new DevelopmentGitResult(0, HandoffPorts.InitialHead + "\trefs/heads/feature/extension\n", ""),
                    new DevelopmentGitResult(0, HandoffPorts.OtherHead + "\trefs/heads/feature/extension\n" +
                        HandoffPorts.OtherHead + "\trefs/heads/integration/next\n", ""),
                    new DevelopmentGitResult(128, "", "Git authentication failed. Use your existing credential manager.") })
                {
                    var ports = new HandoffPorts();
                    using (var workflow = await Inspect(ports))
                    {
                        ports.RemoteResult = result; int opened = 0;
                        await workflow.OpenPullRequestAsync(url => opened++);
                        Assert.That(opened, Is.Zero);
                        Assert.That(workflow.Status, Is.Not.Empty);
                        ports.AssertNoWrites();
                    }
                }
            });

        [UnityTest]
        public IEnumerator CancelAndDisposeSuppressLateSuccessfulRemoteResponses()
            => DevelopmentAsyncTest.Run(async () =>
            {
                foreach (bool dispose in new[] { false, true })
                {
                    var ports = new HandoffPorts();
                    using (var workflow = await Inspect(ports))
                    {
                        ports.PendingRemote = new TaskCompletionSource<DevelopmentGitResult>();
                        int opened = 0;
                        Task handoff = workflow.OpenPullRequestAsync(url => opened++);
                        Assert.That(workflow.Busy, Is.True);
                        Assert.That(ports.RemoteCalls, Is.EqualTo(1));
                        if (dispose) workflow.Dispose(); else workflow.Cancel();
                        Assert.That(ports.RemoteCancellation.IsCancellationRequested, Is.True);
                        // The adapter deliberately returns success late instead of honoring cancellation.
                        ports.PendingRemote.SetResult(ports.RemoteResult);
                        await handoff;
                        Assert.That(opened, Is.Zero);
                        Assert.That(workflow.Busy, Is.False);
                        ports.AssertNoWrites();
                    }
                }
            });

        [UnityTest]
        public IEnumerator UninspectedOrBusyPagesCannotStartAnAdditionalBrowserHandoff()
            => DevelopmentAsyncTest.Run(async () =>
            {
                var ports = new HandoffPorts();
                using (var workflow = ports.CreateWorkflow())
                {
                    workflow.SelectPackage(ports.Package()); int opened = 0;
                    await workflow.OpenPullRequestAsync(url => opened++);
                    Assert.That(opened, Is.Zero);
                    Assert.That(ports.Commands, Is.Empty);
                }
                using (var workflow = await Inspect(ports))
                {
                    ports.PendingRemote = new TaskCompletionSource<DevelopmentGitResult>();
                    int opened = 0;
                    Task first = workflow.OpenPullRequestAsync(url => opened++);
                    await workflow.OpenPullRequestAsync(url => opened++);
                    Assert.That(ports.RemoteCalls, Is.EqualTo(1));
                    ports.PendingRemote.SetResult(ports.RemoteResult);
                    await first;
                    Assert.That(opened, Is.EqualTo(1));
                    ports.AssertNoWrites();
                }
            });

        private static async Task<DevelopmentWorkflow> Inspect(HandoffPorts ports)
        {
            var workflow = ports.CreateWorkflow();
            workflow.SelectPackage(ports.Package());
            await workflow.InspectAsync(ports.Root, false);
            Assert.That(workflow.Repository, Is.Not.Null, workflow.Status);
            Assert.That(workflow.Snapshot.Upstream, Is.EqualTo("origin/feature/extension"));
            return workflow;
        }

        // Strict in-memory boundaries exercise the real repository service, workspace and workflow.
        // No filesystem path is accessed, no Git process starts, and every unexpected command fails.
        private sealed class HandoffPorts : IDevelopmentGitRunner, IDevelopmentFileSystem,
            IDevelopmentSourceFileSystem, IDevelopmentSessionStore, IDevelopmentSourceResolver, IDevelopmentCheckoutClaims
        {
            internal const string InitialHead = "1111111111111111111111111111111111111111";
            internal const string OtherHead = "2222222222222222222222222222222222222222";
            internal const string RemoteCommand = "ls-remote --heads origin refs/heads/feature/extension refs/heads/integration/next";
            private const string PackageId = "com.deucarian.handoff";
            internal readonly string Root, Consumer;
            internal readonly List<string> Commands = new List<string>();
            internal string Branch = "feature/extension", Head = InitialHead, Upstream = "origin/feature/extension", Remote, PushRemote;
            internal Action AfterRemoteRead;
            internal TaskCompletionSource<DevelopmentGitResult> PendingRemote;
            internal CancellationToken RemoteCancellation;
            internal int RemoteCalls;
            private int writes;
            internal DevelopmentGitResult RemoteResult = new DevelopmentGitResult(0,
                InitialHead + "\trefs/heads/feature/extension\n" + OtherHead + "\trefs/heads/integration/next\n", "");

            internal HandoffPorts(string host = "github.com")
            {
                string storage = Path.DirectorySeparatorChar == '\\'
                    ? "D:/Codex-storage/validation/package-development-20260910/handoff-memory"
                    : "/tmp/deucarian-handoff-memory";
                Root = Path.GetFullPath(Path.Combine(storage, "Package"));
                Consumer = Path.GetFullPath(Path.Combine(storage, "Consumer"));
                Remote = PushRemote = "https://" + host + "/Deucarian/Handoff.git";
            }
            internal DevelopmentInstalledPackage Package() => new DevelopmentInstalledPackage(PackageId, "Handoff fixture",
                Remote, Remote + "#main", InitialHead, "Git", "", "integration/next");
            internal DevelopmentWorkflow CreateWorkflow() => new DevelopmentWorkflow(Consumer,
                new DevelopmentRepositoryService(this, this), this, this,
                new PackageDevelopmentSourceService(Consumer, this, this, this, this));

            public Task<DevelopmentGitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                string command = string.Join(" ", arguments); Commands.Add(command);
                if (directory == Consumer && command == "rev-parse --show-toplevel")
                    return Task.FromResult(new DevelopmentGitResult(128, "", DevelopmentGitProcessRunner.NotRepository));
                if (directory != Root) return Unexpected("Git escaped its package root.");
                string output;
                switch (command)
                {
                    case "rev-parse --show-toplevel": output = Root; break;
                    case "rev-parse --git-common-dir": case "rev-parse --git-dir": output = Path.Combine(Root, ".git"); break;
                    case "remote get-url --all origin": output = Remote; break;
                    case "remote get-url --push --all origin": output = PushRemote; break;
                    case "worktree list --porcelain": output = "worktree " + Root + "\n"; break;
                    case "symbolic-ref --quiet --short HEAD": output = Branch; break;
                    case "rev-parse --verify HEAD": output = Head; break;
                    case "rev-parse --abbrev-ref --symbolic-full-name @{upstream}":
                        return Task.FromResult(new DevelopmentGitResult(Upstream.Length == 0 ? 128 : 0, Upstream,
                            Upstream.Length == 0 ? "The branch has no upstream." : ""));
                    case "for-each-ref --format=%(refname:short) refs/heads/": output = Branch + "\nintegration/next\n"; break;
                    case "rev-list --left-right --count HEAD...@{upstream}": output = "0\t0\n"; break;
                    case "status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none":
                    case "diff --numstat -z --no-renames --no-ext-diff --no-textconv":
                    case "diff --cached --numstat -z --no-renames --no-ext-diff --no-textconv":
                    case "ls-files --stage -z": output = ""; break;
                    case RemoteCommand:
                        RemoteCalls++; RemoteCancellation = token; AfterRemoteRead?.Invoke();
                        return PendingRemote?.Task ?? Task.FromResult(RemoteResult);
                    default: return Unexpected("Unexpected Git command: " + command);
                }
                return Task.FromResult(new DevelopmentGitResult(0, output, ""));
            }
            private Task<DevelopmentGitResult> Unexpected(string reason) { writes++; throw new InvalidOperationException(reason); }
            private void RejectWrite() { writes++; throw new InvalidOperationException("A PR handoff must not mutate source or Git state."); }
            public string Canonicalize(string path) => Path.GetFullPath(path);
            public bool FileExists(string path) => path == Path.Combine(Root, "package.json");
            public bool DirectoryExists(string path) => true;
            public string ReadText(string path)
            {
                if (path != Path.Combine(Root, "package.json")) throw new InvalidOperationException("Unexpected file read.");
                return "{\"name\":\"" + PackageId + "\"}";
            }
            public bool IsBinary(string path) => false;
            public string Fingerprint(string path) => "memory";
            public IDisposable Lock(string path, string name) { RejectWrite(); return null; }
            public byte[] ReadManifest() => throw new InvalidOperationException("The PR handoff must not read application payloads.");
            public void CompareExchangeManifest(byte[] expected, byte[] replacement) => RejectWrite();
            public IDisposable AcquireProjectLock() { RejectWrite(); return null; }
            public IReadOnlyList<PackageDevelopmentSession> LoadAll() => new[] { new PackageDevelopmentSession {
                Id = "handoff-memory", PackageId = PackageId, ProjectRoot = Consumer, RepositoryRoot = Root,
                CommonDirectory = Path.Combine(Root, ".git"), State = DevelopmentSourceState.Connected, Message = "Connected."
            } };
            public void Save(PackageDevelopmentSession session) => RejectWrite();
            public Task<DevelopmentSourceResolution> ResolveAsync(DevelopmentSourceExpectation expectation, bool request, CancellationToken token)
            { RejectWrite(); return null; }
            public void Acquire(PackageDevelopmentSession session) => RejectWrite();
            public void AssertOwned(PackageDevelopmentSession session) { }
            public void Release(PackageDevelopmentSession session) => RejectWrite();
            internal void AssertNoWrites() => Assert.That(writes, Is.Zero, "Handoff must only inspect the package and open a browser URL.");
        }
    }
}
