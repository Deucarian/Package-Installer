using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.Editor;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentPageTests
    {
        private const string Project = "D:/Codex-storage/validation/package-development-20260910/page-test-consumer";
        private const string Checkout = "D:/Codex-storage/validation/package-development-20260910/page-test-package";

        [Test]
        public void OpeningAndRoutingDeferCatalogCaptureAndDoNotRunRepositoryOrSourceCommands()
        {
            var ports = new PagePorts();
            var workflow = CreateWorkflow(ports);
            bool loading = true;
            int starts = 0, captures = 0;
            var packages = Packages();
            using (var page = new DevelopmentPage(workflow, () => starts++, () => loading,
                () => { captures++; return packages; }))
            {
                page.Activate(packages[1].Id);
                page.Update(new Rect(0, 0, 1180, 800));
                Assert.That(starts, Is.EqualTo(1));
                Assert.That(captures, Is.Zero, "Catalog data is not read on the opening path.");
                Assert.That(workflow.Package, Is.Null);
                loading = false;
                page.Update(new Rect(0, 0, 1180, 800));
                Assert.That(captures, Is.EqualTo(1));
                Assert.That(workflow.Package.Id, Is.EqualTo(packages[1].Id), "A route survives deferred loading.");
                page.Update(new Rect(0, 0, 820, 650));
                Assert.That(captures, Is.EqualTo(1));
                Assert.That(page.Root.Q<Button>("development-stage").enabledInHierarchy, Is.False);
                ports.AssertNoMutationOrGit();
            }
        }

        [Test]
        public void ReturningWithoutARouteKeepsTheChosenManagedPackageAndItsDraft()
        {
            var ports = new PagePorts();
            var packages = Packages();
            ports.Session = new PackageDevelopmentSession {
                Id = "page-session", PackageId = packages[1].Id, ProjectRoot = Project,
                RepositoryRoot = Checkout, CommonDirectory = Checkout + "/.git",
                State = DevelopmentSourceState.Connected, Message = "Local source is connected.", WasDirectDependency = true
            };
            var workflow = CreateWorkflow(ports);
            using (var page = new DevelopmentPage(workflow, () => { }, () => false, () => packages))
            {
                page.Activate(packages[1].Id);
                page.Update(new Rect(0, 0, 1180, 800));
                var picker = page.Root.Q<PopupField<string>>("development-package");
                var draft = page.Root.Q<TextField>("development-message");
                draft.value = "Retain this package commit draft";
                Assert.That(workflow.Managed, Is.True);
                Assert.That(page.Root.Q<TextField>("development-path").value, Is.EqualTo(Checkout));
                page.Deactivate();
                page.Activate(null);
                Assert.That(workflow.Package.Id, Is.EqualTo(packages[1].Id));
                Assert.That(workflow.Session, Is.SameAs(ports.Session));
                Assert.That(picker.index, Is.EqualTo(1));
                Assert.That(draft.value, Is.EqualTo("Retain this package commit draft"));
                Assert.That(page.Root.Q<Button>("development-restore").enabledInHierarchy, Is.True);
                ports.AssertNoMutationOrGit();
            }
        }

        [Test]
        public void PackageSelectionIsIndependentAcrossCachedPageInstances()
        {
            var firstPorts = new PagePorts();
            var secondPorts = new PagePorts();
            var firstWorkflow = CreateWorkflow(firstPorts);
            var secondWorkflow = CreateWorkflow(secondPorts);
            var packages = Packages();
            using (var first = new DevelopmentPage(firstWorkflow, () => { }, () => false, () => packages))
            using (var second = new DevelopmentPage(secondWorkflow, () => { }, () => false, () => packages))
            {
                first.Activate(packages[0].Id);
                second.Activate(packages[1].Id);
                first.Update(default);
                second.Update(default);
                first.Root.Q<TextField>("development-message").value = "Only first page";
                Assert.That(firstWorkflow.Package.Id, Is.EqualTo(packages[0].Id));
                Assert.That(secondWorkflow.Package.Id, Is.EqualTo(packages[1].Id));
                Assert.That(second.Root.Q<TextField>("development-message").value, Is.Empty);
                Assert.That(first.Root.Q("review-scroll"), Is.Not.SameAs(second.Root.Q("review-scroll")));
                firstPorts.AssertNoMutationOrGit();
                secondPorts.AssertNoMutationOrGit();
            }
        }

        [Test]
        public void IdleCancellationPreservesStatusAndDisposalStopsDeferredCapture()
        {
            var ports = new PagePorts();
            var workflow = CreateWorkflow(ports);
            int captures = 0;
            bool loading = true;
            var page = new DevelopmentPage(workflow, () => { }, () => loading,
                () => { captures++; return Packages(); });
            string initialStatus = workflow.Status;
            workflow.Cancel();
            Assert.That(workflow.Status, Is.EqualTo(initialStatus));
            page.Dispose();
            loading = false;
            page.Update(default);
            Assert.That(captures, Is.Zero);
            ports.AssertNoMutationOrGit();
        }

        [Test]
        public async Task DisposalCancelsAnInFlightInspectionAndSuppressesLatePageNotifications()
        {
            var ports = new PagePorts();
            var workflow = CreateWorkflow(ports);
            var page = new DevelopmentPage(workflow, () => { }, () => false, Packages);
            page.Update(default);
            int notifications = 0;
            workflow.Changed += () => notifications++;
            Task operation = workflow.InspectAsync(Checkout, false);
            Assert.That(workflow.Busy, Is.True);
            Assert.That(ports.GitCalls, Is.EqualTo(1), "Only the explicit inspect action reaches Git.");
            int beforeDispose = notifications;
            page.Dispose();
            page.Dispose();
            await operation;
            Assert.That(ports.LastCancellation.IsCancellationRequested, Is.True);
            Assert.That(workflow.Busy, Is.False);
            Assert.That(notifications, Is.EqualTo(beforeDispose));
            Assert.That(page.Root.Q("review-scroll"), Is.Null);
            await workflow.InspectAsync(Checkout, false);
            Assert.That(ports.GitCalls, Is.EqualTo(1), "Disposed pages cannot start another repository operation.");
            Assert.That(ports.SourceMutations, Is.Zero);
        }

        [Test]
        public void PackageDevelopmentIsRegisteredOnceAsAPlainPageUnderPackages()
        {
            var matches = DeucarianToolRegistry.GetTools().Where(tool => tool.Id == DevelopmentPage.ToolId).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1));
            Assert.That(matches[0].OwningPackage, Is.EqualTo("com.deucarian.package-installer"));
            Assert.That(matches[0].NavigationPath, Is.EqualTo("Packages"));
            Assert.That(matches[0].CreatePage, Is.Not.Null);
            Assert.That(typeof(IDeucarianEditorPage).IsAssignableFrom(typeof(DevelopmentPage)), Is.True);
            Assert.That(typeof(EditorWindow).IsAssignableFrom(typeof(DevelopmentPage)), Is.False);
        }

        [UnityTest]
        public IEnumerator RealPageUsesSharedReviewAtNormalNarrowAndScaledSizes()
        {
            int previous = DeucarianEditorAppearance.WorkspaceScalePercent;
            var ports = new PagePorts();
            var window = ScriptableObject.CreateInstance<DevelopmentPageTestWindow>();
            window.Show();
            using (var page = new DevelopmentPage(CreateWorkflow(ports), () => { }, () => false, Packages))
            {
                try
                {
                    window.rootVisualElement.Add(page.Root);
                    page.Root.style.flexGrow = 1;
                    page.Update(default);
                    var input = page.Root.Q<TextField>("development-path");
                    input.value = Checkout;
                    var review = page.Root.Q<ScrollView>("review-scroll");
                    var button = page.Root.Q<Button>("development-inspect");
                    foreach (var size in new[] { new Vector2(1480, 850), new Vector2(820, 650), new Vector2(620, 800) })
                    {
                        window.rootVisualElement.style.width = size.x;
                        window.rootVisualElement.style.height = size.y;
                        foreach (int scale in new[] { 100, 150 })
                        {
                            DeucarianEditorAppearance.WorkspaceScalePercent = scale;
                            for (int i = 0; i < 10; i++) yield return null;
                            string context = size + " at " + scale + "%";
                            Assert.That(review.resolvedStyle.height, Is.GreaterThan(40), context);
                            Assert.That(button.worldBound.xMin, Is.GreaterThanOrEqualTo(review.worldBound.xMin - 1), context);
                            Assert.That(button.worldBound.xMax, Is.LessThanOrEqualTo(review.worldBound.xMax + 1), context);
                            Assert.That(input.value, Is.EqualTo(Checkout), context);
                            Assert.That(page.Root.Query<SliderInt>("workspace-scale-slider").ToList().Count, Is.EqualTo(1));
                            var dock = page.Root.Q("workspace-scale");
                            Assert.That(review.worldBound.yMax, Is.LessThanOrEqualTo(dock.worldBound.yMin + 1), context);
                        }
                    }
                    ports.AssertNoMutationOrGit();
                }
                finally { DeucarianEditorAppearance.WorkspaceScalePercent = previous; window.Close(); }
            }
        }

        private static IReadOnlyList<DevelopmentInstalledPackage> Packages() => new[] {
            new DevelopmentInstalledPackage("com.deucarian.page-first", "First package", "https://github.com/Deucarian/Page-First.git",
                "https://github.com/Deucarian/Page-First.git#develop", "1111111", "Git"),
            new DevelopmentInstalledPackage("com.deucarian.page-second", "Second package", "https://github.com/Deucarian/Page-Second.git",
                "https://github.com/Deucarian/Page-Second.git#develop", "2222222", "Git")
        };

        private static DevelopmentWorkflow CreateWorkflow(PagePorts ports) => new DevelopmentWorkflow(Project,
            new DevelopmentRepositoryService(ports, ports), ports, ports,
            new PackageDevelopmentSourceService(Project, ports, ports, ports, ports));

        // All side-effect boundaries are memory-only. An accidental opening command is observable, never executed.
        private sealed class PagePorts : IDevelopmentGitRunner, IDevelopmentFileSystem, IDevelopmentSourceFileSystem,
            IDevelopmentSessionStore, IDevelopmentSourceResolver, IDevelopmentCheckoutClaims
        {
            internal PackageDevelopmentSession Session;
            internal int GitCalls, SourceMutations;
            internal CancellationToken LastCancellation;
            public Task<DevelopmentGitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken token)
            {
                GitCalls++;
                LastCancellation = token;
                var pending = new TaskCompletionSource<DevelopmentGitResult>();
                token.Register(() => pending.TrySetCanceled());
                return pending.Task;
            }
            public string Canonicalize(string path) => Path.GetFullPath(path);
            public bool FileExists(string path) => false;
            public bool DirectoryExists(string path) => true;
            public string ReadText(string path) => throw new InvalidOperationException("Unexpected file read.");
            public bool IsBinary(string path) => false;
            public string Fingerprint(string path) => "memory";
            public IDisposable Lock(string path, string name) => throw new InvalidOperationException("Unexpected Git mutation lock.");
            public byte[] ReadManifest() => throw new InvalidOperationException("Opening a page must not edit or prepare its manifest.");
            public void CompareExchangeManifest(byte[] expected, byte[] replacement) { SourceMutations++; }
            public IDisposable AcquireProjectLock() { SourceMutations++; throw new InvalidOperationException("Unexpected source command."); }
            public IReadOnlyList<PackageDevelopmentSession> LoadAll() => Session == null ? Array.Empty<PackageDevelopmentSession>() : new[] { Session };
            public void Save(PackageDevelopmentSession session) { SourceMutations++; }
            public Task<DevelopmentSourceResolution> ResolveAsync(DevelopmentSourceExpectation expectation, bool request, CancellationToken token)
            { SourceMutations++; return Task.FromResult(new DevelopmentSourceResolution(DevelopmentResolutionState.Failed, "Unexpected resolve.")); }
            public void Acquire(PackageDevelopmentSession session) { SourceMutations++; }
            public void AssertOwned(PackageDevelopmentSession session) { }
            public void Release(PackageDevelopmentSession session) { SourceMutations++; }
            internal void AssertNoMutationOrGit()
            {
                Assert.That(GitCalls, Is.Zero, "Page presentation must not run Git.");
                Assert.That(SourceMutations, Is.Zero, "Page presentation must not connect, resolve or mutate source state.");
            }
        }
    }

    internal sealed class DevelopmentPageTestWindow : EditorWindow { }
}
