using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed partial class PackageInstallServiceTests
    {
        [Test]
        public void TestSessionScopeRestoresActivitySequenceAndRecoveryState()
        {
            var original = PackageInstallerActivityService.Record("Test", PackageInstallerActivitySeverity.Info, "Preserved");
            PackageOperationAutoResumeState.TrackActiveOperation("prior-operation", "prior-registry", true);
            PackageOperationAutoResumeState.SimulateBeforeAssemblyReloadForTests();
            PackageInstallerSelfUpdateState.BeginForTests("https://example.com/Installer.git#main",
                new PackageInstallerAssemblyIdentity("0.0.1", "prior-module"));
            const string selfUpdateKey = "Deucarian.PackageInstaller.SelfUpdateState";
            string selfUpdate = SessionState.GetString(selfUpdateKey, string.Empty);
            int activityChanges = 0;
            Action onChanged = () => activityChanges++;
            PackageInstallerActivityService.Changed += onChanged;
            try
            {
                using (new PackageInstallerTestSessionScope())
                {
                    PackageInstallerActivityService.ClearForTests();
                    PackageInstallerActivityService.Record("Test", PackageInstallerActivitySeverity.Info, "Synthetic");
                    PackageInstallerSelfUpdateState.Clear();
                    PackageOperationAutoResumeState.ResetForTests();
                    Assert.IsEmpty(SessionState.GetString(selfUpdateKey, string.Empty));
                }
                Assert.AreSame(original, PackageInstallerActivityService.Latest);
                Assert.AreEqual(1, PackageInstallerActivityService.Recent.Count);
                Assert.AreEqual(0, activityChanges, "Synthetic activity must not notify the preserved window.");
                Assert.AreEqual(selfUpdate, SessionState.GetString(selfUpdateKey, string.Empty));
                Assert.IsTrue(PackageOperationAutoResumeState.IsAssemblyReloading);
                Assert.IsTrue(PackageOperationAutoResumeState.HasMatchingReloadMarker("prior-operation", "prior-registry"));
                PackageOperationAutoResumeState.ClearReloadMarker();
                PackageOperationAutoResumeState.SimulateBeforeAssemblyReloadForTests();
                Assert.IsTrue(PackageOperationAutoResumeState.HasMatchingReloadMarker("prior-operation", "prior-registry"),
                    "The active operation fields must also survive the scope.");
                var next = PackageInstallerActivityService.Record("Test", PackageInstallerActivitySeverity.Info, "Next");
                Assert.AreEqual(original.Sequence + 1, next.Sequence);
                Assert.AreEqual(1, activityChanges, "The preserved activity subscriber must be reattached.");
            }
            finally
            {
                PackageInstallerActivityService.Changed -= onChanged;
            }
        }

        [Test]
        public void FirstDependencyDispatchPublishesPersistsAndDisplaysEntireQueue()
        {
            var prerequisite = CreatePackage("Prerequisite", "com.deucarian.prerequisite");
            var root = CreatePackage("Root", "com.deucarian.root", new[] { prerequisite.PackageId });
            var client = new ControlledPackageInstallClient();
            var repository = new PackageOperationStateRepository(_temporaryProjectRoot);
            var view = new PackageOperationQueueView();
            using (var service = new PackageInstallService(client, repository))
            {
                bool published = false;
                bool inspectedFirstDispatch = false;
                service.StateChanged += () =>
                {
                    published = true;
                    view.Refresh(PackageOperationQueueSnapshot.Capture(service));
                };
                client.BeforeAdd = () =>
                {
                    Assert.IsTrue(published, "The first request must not begin before queue publication.");
                    Assert.AreEqual(2, service.TotalSteps);
                    Assert.IsTrue(repository.TryLoad(out var record, out var error), error);
                    CollectionAssert.AreEqual(new[] { prerequisite.PackageId, root.PackageId },
                        record.Steps.Select(step => step.PackageId));
                    Assert.AreEqual(PackageInstallProgressItemState.Active, record.Steps[0].State);
                    Assert.AreEqual(PackageInstallProgressItemState.Pending, record.Steps[1].State);
                    Assert.AreEqual(DisplayStyle.Flex, view.Root.style.display.value);
                    StringAssert.Contains("0 completed · 1 current · 1 remaining",
                        view.Root.Q<Label>("installer-operation-queue-summary").text);
                    StringAssert.Contains("Root · Remaining",
                        view.Root.Q<Label>("installer-operation-step-" + root.PackageId).text);
                    inspectedFirstDispatch = true;
                };
                Assert.IsTrue(service.InstallPlan(CreateSingleRootDependencyPlan(prerequisite, root), "Install Root"));
                Assert.IsTrue(inspectedFirstDispatch);
                Assert.AreEqual(1, client.AddedUrls.Count);
            }
        }

        [Test]
        public void ReloadKeepsCompletedCurrentAndRemainingQueueRowsUntilCompletion()
        {
            var first = CreatePackage("First", "com.deucarian.first");
            var second = CreatePackage("Second", "com.deucarian.second");
            var third = CreatePackage("Third", "com.deucarian.third");
            var repository = new PackageOperationStateRepository(_temporaryProjectRoot);
            var client = new ControlledPackageInstallClient();
            using (var writer = new PackageInstallService(client, repository))
            {
                writer.InstallPlan(CreateIndependentPlan(first, second, third), "Install three packages");
                client.Requests[0].CompleteSuccess(first.PackageId, "1.0.0");
                writer.UpdateForTests();
                Assert.IsTrue(repository.TryLoad(out var saved, out var error), error);
                var waiting = PackageOperationQueueSnapshot.Capture(saved);
                Assert.IsTrue(waiting.waitingForRecovery);
                Assert.AreEqual("1 completed · 1 current · 1 remaining", waiting.Summary);
            }

            var resumedClient = new ControlledPackageInstallClient();
            var view = new PackageOperationQueueView();
            using (var resumed = new PackageInstallService(resumedClient, repository))
            {
                resumed.StateChanged += () => view.Refresh(PackageOperationQueueSnapshot.Capture(resumed));
                Assert.IsTrue(resumed.ResumeSavedOperation("registry-fingerprint"));
                Assert.AreEqual(3, resumed.TotalSteps);
                Assert.AreEqual("1 completed · 1 current · 1 remaining",
                    PackageOperationQueueSnapshot.Capture(resumed).Summary);
                Assert.AreEqual(PackageInstallProgressItemState.Completed, resumed.ProgressItems[0].State);
                resumedClient.Requests[0].CompleteSuccess(second.PackageId, "1.0.0");
                resumed.UpdateForTests();
                resumedClient.Requests[1].CompleteSuccess(third.PackageId, "1.0.0");
                resumed.UpdateForTests();
                Assert.IsFalse(resumed.IsBusy);
                Assert.AreEqual("3 completed · 0 current · 0 remaining",
                    view.Root.Q<Label>("installer-operation-queue-summary").text);
                Assert.AreEqual(DisplayStyle.Flex, view.Root.style.display.value);
                CollectionAssert.AreEqual(new[] { second.StableUrl, third.StableUrl }, resumedClient.AddedUrls);
            }
        }

        [Test]
        public void AppliedInstallerRemainsInCompletedQueueAfterFinalReload()
        {
            var dependency = CreatePackage("Dependency", "com.deucarian.dependency");
            var installer = CreatePackage("Installer", PackageInstallerRuntimeIdentity.PackageId);
            var repository = new PackageOperationStateRepository(_temporaryProjectRoot);
            var client = new ControlledPackageInstallClient();
            using (var writer = new PackageInstallService(client, repository))
            {
                writer.InstallPlan(CreateIndependentPlan(dependency, installer), "Update packages");
                client.Requests[0].CompleteSuccess(dependency.PackageId, "1.0.0");
                writer.UpdateForTests();
            }
            PackageInstallerSelfUpdateState.BeginForTests(installer.StableUrl,
                new PackageInstallerAssemblyIdentity("0.0.1", "previous-module"));
            PackageInstallerSelfUpdateState.MarkResolved(PackageInstallerRuntimeIdentity.Version);
            Assert.AreEqual(PackageInstallerSelfUpdateReconcileResult.AppliedOnReload,
                PackageInstallerSelfUpdateState.ReconcileForTests(PackageInstallerRuntimeIdentity.Current));
            var resumedClient = new ControlledPackageInstallClient();
            using (var resumed = new PackageInstallService(resumedClient, repository))
            {
                Assert.IsFalse(resumed.ResumeSavedOperation("registry-fingerprint"));
                Assert.AreEqual(2, resumed.TotalSteps);
                Assert.AreEqual(2, resumed.CompletedSteps);
                Assert.AreEqual("2 completed · 0 current · 0 remaining",
                    PackageOperationQueueSnapshot.Capture(resumed).Summary);
                Assert.AreEqual(PackageInstallProgressItemState.AlreadyCorrect, resumed.ProgressItems[1].State);
                Assert.IsFalse(resumed.HasSavedOperation);
                Assert.IsEmpty(resumedClient.AddedUrls);
            }
        }
    }
}
