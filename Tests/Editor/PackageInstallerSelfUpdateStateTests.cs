using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageInstallerSelfUpdateStateTests
    {
        private static readonly PackageInstallerAssemblyIdentity OldIdentity =
            new PackageInstallerAssemblyIdentity("1.1.60", "11111111111111111111111111111111");

        private static readonly PackageInstallerAssemblyIdentity NewIdentity =
            new PackageInstallerAssemblyIdentity("1.1.61", "22222222222222222222222222222222");

        [SetUp]
        public void SetUp()
        {
            PackageInstallerSelfUpdateState.Clear();
            PackageInstallService.ClearPendingOperationForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PackageInstallerSelfUpdateState.Clear();
            PackageInstallService.ClearPendingOperationForTests();
        }

        [Test]
        public void ResolvedMarkerSurvivesDomainReloadStateAndRemainsPendingForSameAssembly()
        {
            const string selectedUrl = "https://github.com/Deucarian/Package-Installer.git#main";
            PackageInstallerSelfUpdateState.BeginForTests(selectedUrl, OldIdentity);
            PackageInstallerSelfUpdateState.MarkResolved("1.1.61");

            PackageInstallerSelfUpdateSnapshot snapshot =
                PackageInstallerSelfUpdateState.CaptureSnapshotForTests(OldIdentity);

            Assert.IsTrue(snapshot.IsAwaitingReload);
            Assert.AreEqual("1.1.60", snapshot.SourceVersion);
            Assert.AreEqual("1.1.60", snapshot.ResolvedVersionBeforeAdd);
            Assert.AreEqual("1.1.61", snapshot.ResolvedVersionAfterAdd);
            Assert.AreEqual("1.1.61", snapshot.ResolvedVersion);
            Assert.AreEqual(selectedUrl, snapshot.SelectedUrl);
        }

        [Test]
        public void AppliedMarkerIsIdempotentUntilExplicitlyAcknowledged()
        {
            PackageInstallerSelfUpdateState.BeginForTests("https://example.com/installer.git#main", OldIdentity);
            PackageInstallerSelfUpdateState.MarkResolved("1.1.61");

            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload,
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity));
            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload,
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity));
            Assert.IsTrue(PackageInstallerSelfUpdateState.AcknowledgeApplied());
            Assert.IsFalse(PackageInstallerSelfUpdateState.AcknowledgeApplied());
            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.None,
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity));
        }

        [Test]
        public void BeginPersistsDistinctResolvedVersionsBeforeAndAfterAdd()
        {
            PackageInstallerSelfUpdateState.BeginForTests(
                "https://example.com/installer.git#main",
                OldIdentity,
                "1.1.59");

            PackageInstallerSelfUpdateSnapshot beforeAdd =
                PackageInstallerSelfUpdateState.CapturePersistedSnapshotForTests();

            Assert.AreEqual("1.1.59", beforeAdd.ResolvedVersionBeforeAdd);
            Assert.AreEqual(string.Empty, beforeAdd.ResolvedVersionAfterAdd);
            Assert.AreEqual("1.1.59", beforeAdd.ResolvedVersion);

            PackageInstallerSelfUpdateState.MarkResolved("1.1.61");
            PackageInstallerSelfUpdateSnapshot afterAdd =
                PackageInstallerSelfUpdateState.CapturePersistedSnapshotForTests();

            Assert.AreEqual("1.1.59", afterAdd.ResolvedVersionBeforeAdd);
            Assert.AreEqual("1.1.61", afterAdd.ResolvedVersionAfterAdd);
            Assert.AreEqual("1.1.61", afterAdd.ResolvedVersion);
        }

        [Test]
        public void BeginCapturesCurrentlyResolvedPackageVersionBeforeAdd()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(
                typeof(PackageInstallerRuntimeIdentity).Assembly);
            string expectedVersion = packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.version)
                ? packageInfo.version
                : PackageInstallerRuntimeIdentity.Version;

            PackageInstallerSelfUpdateState.Begin("https://example.com/installer.git#main");

            Assert.AreEqual(
                expectedVersion,
                PackageInstallerSelfUpdateState
                    .CapturePersistedSnapshotForTests()
                    .ResolvedVersionBeforeAdd);
        }

        [Test]
        public void VersionChangeWithoutMvidChangeRemainsReloadPending()
        {
            PackageInstallerSelfUpdateState.BeginForTests(
                "https://example.com/installer.git#main",
                OldIdentity);
            PackageInstallerSelfUpdateState.MarkResolved("1.1.61");
            PackageInstallerAssemblyIdentity changedVersionSameMvid =
                new PackageInstallerAssemblyIdentity("1.1.61", OldIdentity.ModuleVersionId);

            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.Pending,
                PackageInstallerSelfUpdateState.ReconcileForTests(changedVersionSameMvid));
            Assert.IsTrue(
                PackageInstallerSelfUpdateState
                    .CaptureSnapshotForTests(changedVersionSameMvid)
                    .IsAwaitingReload);
        }

        [Test]
        public void ReadOnlySnapshotDoesNotConsumeAppliedReloadReconciliation()
        {
            PackageInstallerSelfUpdateState.BeginForTests("https://example.com/installer.git#main", OldIdentity);
            PackageInstallerSelfUpdateState.MarkResolved("1.1.61");

            PackageInstallerSelfUpdateSnapshot snapshot =
                PackageInstallerSelfUpdateState.CaptureSnapshotForTests(NewIdentity);

            Assert.IsFalse(snapshot.IsAwaitingReload);
            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload,
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity));
        }

        [Test]
        public void InstallingMarkerIdentityChangeHandlesReloadBeforeAddCompletion()
        {
            PackageInstallerSelfUpdateState.BeginForTests("https://example.com/installer.git#main", OldIdentity);

            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload,
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity));
            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload,
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity));
            Assert.AreEqual(
                NewIdentity.Version,
                PackageInstallerSelfUpdateState
                    .CapturePersistedSnapshotForTests()
                    .ResolvedVersionAfterAdd);
        }

        [Test]
        public void InstallingMarkerIdentityChangeDropsAppliedSelfFromResumeQueue()
        {
            PackageDefinition installer = CreatePackage(PackageInstallerRuntimeIdentity.PackageId);
            PackageDefinition dependency = CreatePackage("com.deucarian.logging");
            PackageInstallerSelfUpdateState.BeginForTests("https://example.com/installer.git#main", OldIdentity);

            PackageInstallerSelfUpdateReconcileResult reconcileResult =
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity);
            PackageDefinition[] resumedPackages = reconcileResult ==
                                                   PackageInstallerSelfUpdateReconcileResult.AppliedOnReload
                ? PackageInstallService.FilterAppliedSelfUpdateForTests(new[] { dependency, installer })
                : new[] { dependency, installer };

            Assert.AreEqual(PackageInstallerSelfUpdateReconcileResult.AppliedOnReload, reconcileResult);
            Assert.IsFalse(resumedPackages.Any(package =>
                PackageInstallerRuntimeIdentity.IsSelf(package.PackageId)));
            CollectionAssert.AreEqual(
                new[] { dependency.PackageId },
                resumedPackages.Select(package => package.PackageId).ToArray());
        }

        [Test]
        public void MultiPackageQueueOrdersInstallerLast()
        {
            PackageDefinition installer = CreatePackage(PackageInstallerRuntimeIdentity.PackageId);
            PackageDefinition editor = CreatePackage("com.deucarian.editor");
            PackageDefinition logging = CreatePackage("com.deucarian.logging");

            PackageDefinition[] ordered = PackageInstallService.OrderSelfUpdateLastForTests(
                new[] { installer, editor, logging });

            CollectionAssert.AreEqual(
                new[] { editor.PackageId, logging.PackageId, installer.PackageId },
                ordered.Select(package => package.PackageId).ToArray());
        }

        [Test]
        public void AppliedSelfUpdateIsRemovedFromResumeQueue()
        {
            PackageDefinition installer = CreatePackage(PackageInstallerRuntimeIdentity.PackageId);
            PackageDefinition logging = CreatePackage("com.deucarian.logging");

            PackageDefinition[] remaining = PackageInstallService.FilterAppliedSelfUpdateForTests(
                new[] { installer, logging });

            CollectionAssert.AreEqual(
                new[] { logging.PackageId },
                remaining.Select(package => package.PackageId).ToArray());
        }

        [Test]
        public void FailedInstallCleanupRemovesPendingMarker()
        {
            PackageDefinition installer = CreatePackage(PackageInstallerRuntimeIdentity.PackageId);
            PackageInstallerSelfUpdateState.BeginForTests("https://example.com/installer.git#main", OldIdentity);
            PackageInstallerSelfUpdateState.MarkResolved("1.1.61");

            PackageInstallService.ReconcileSelfUpdateAfterInstallForTests(
                installer,
                success: false);

            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.None,
                PackageInstallerSelfUpdateState.ReconcileForTests(OldIdentity));
        }

        [Test]
        public void PersistedQueueRestorationDropsAppliedInstallerAndKeepsRemainingWork()
        {
            PackageDefinition logging = CreatePackage("com.deucarian.logging");
            PackageDefinition installer = CreatePackage(PackageInstallerRuntimeIdentity.PackageId);

            using (PackageInstallService service = new PackageInstallService())
            {
                service.QueuePendingOperationForTests(
                    "Resume after Installer reload",
                    new[] { logging, installer });
                service.SavePendingOperationForTests();
            }

            string[] restored = PackageInstallService.RestorePendingPackageIdsForTests(
                selfUpdateAppliedOnReload: true,
                out string operationName);

            Assert.AreEqual("Resume after Installer reload", operationName);
            CollectionAssert.AreEqual(new[] { logging.PackageId }, restored);
        }

        [Test]
        public void RepeatedServiceConstructionPreservesAppliedMarkerUntilResumeAcknowledgesIt()
        {
            PackageDefinition installer = CreatePackage(PackageInstallerRuntimeIdentity.PackageId);

            using (PackageInstallService queueWriter = new PackageInstallService())
            {
                queueWriter.QueuePendingOperationForTests(
                    "Resume after repeated construction",
                    new[] { installer });
                queueWriter.SavePendingOperationForTests();
            }

            PackageInstallerSelfUpdateState.BeginForTests(
                "https://example.com/installer.git#main",
                OldIdentity);
            PackageInstallerSelfUpdateState.MarkResolved("1.1.61");

            using (new PackageInstallService())
            {
            }

            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload,
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity));

            using (PackageInstallService resumedService = new PackageInstallService())
            {
                Assert.IsFalse(resumedService.ResumeSavedOperation());
            }

            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.None,
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity));
            CollectionAssert.IsEmpty(
                PackageInstallService.RestorePendingPackageIdsForTests(
                    selfUpdateAppliedOnReload: false,
                    out _));
        }

        [Test]
        public void ResumeFilteringIsPersistedBeforeAppliedMarkerIsAcknowledged()
        {
            PackageDefinition logging = CreatePackage("com.deucarian.logging");
            PackageDefinition installer = CreatePackage(PackageInstallerRuntimeIdentity.PackageId);

            using (PackageInstallService queueWriter = new PackageInstallService())
            {
                queueWriter.QueuePendingOperationForTests(
                    "Durable filtered resume",
                    new[] { logging, installer });
                queueWriter.SavePendingOperationForTests();
            }

            PackageInstallerSelfUpdateState.BeginForTests(
                "https://example.com/installer.git#main",
                OldIdentity);
            PackageInstallerSelfUpdateState.MarkResolved("1.1.61");
            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload,
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity));

            string[] prepared = PackageInstallService.PreparePendingPackageIdsForResumeForTests(
                selfUpdateAppliedOnReload: true,
                out string operationName);

            Assert.AreEqual("Durable filtered resume", operationName);
            CollectionAssert.AreEqual(new[] { logging.PackageId }, prepared);
            Assert.AreEqual(
                PackageInstallerSelfUpdateReconcileResult.None,
                PackageInstallerSelfUpdateState.ReconcileForTests(NewIdentity));

            string[] restoredAfterAcknowledgement =
                PackageInstallService.RestorePendingPackageIdsForTests(
                    selfUpdateAppliedOnReload: false,
                    out string persistedOperationName);

            Assert.AreEqual("Durable filtered resume", persistedOperationName);
            CollectionAssert.AreEqual(new[] { logging.PackageId }, restoredAfterAcknowledgement);
        }

        [Test]
        public void EmbeddedRuntimeVersionMatchesPackageManifest()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(PackageInstallerRuntimeIdentity).Assembly);
            Assert.IsNotNull(packageInfo);

            string packageJson = File.ReadAllText(Path.Combine(packageInfo.resolvedPath, "package.json"));
            Assert.IsTrue(PackageRegistryPackageNameValidator.TryReadPackageVersion(
                packageJson,
                out string packageVersion));
            Assert.AreEqual(packageVersion, PackageInstallerRuntimeIdentity.Version);
        }

        private static PackageDefinition CreatePackage(string packageId)
        {
            return new PackageDefinition(
                packageId,
                packageId,
                "https://example.com/" + packageId + ".git#main",
                "Test package.");
        }
    }
}
