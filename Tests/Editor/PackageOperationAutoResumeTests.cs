using System;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageOperationAutoResumeTests
    {
        private const string OperationId = "bulk-operation-id";
        private const string RegistryFingerprint = "registry-fingerprint";

        [SetUp]
        public void SetUp()
        {
            PackageOperationAutoResumeState.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PackageOperationAutoResumeState.ResetForTests();
        }

        [Test]
        public void MatchingReloadMarkerExistsOnlyAfterTrackedBulkReload()
        {
            PackageOperationAutoResumeState.TrackActiveOperation(
                OperationId,
                RegistryFingerprint,
                isBulk: true);

            Assert.IsFalse(PackageOperationAutoResumeState.HasMatchingReloadMarker(
                OperationId,
                RegistryFingerprint));

            PackageOperationAutoResumeState.SimulateBeforeAssemblyReloadForTests();

            Assert.IsTrue(PackageOperationAutoResumeState.HasMatchingReloadMarker(
                OperationId,
                RegistryFingerprint));
            Assert.IsFalse(PackageOperationAutoResumeState.HasMatchingReloadMarker(
                "different-operation",
                RegistryFingerprint));
            Assert.IsFalse(PackageOperationAutoResumeState.HasMatchingReloadMarker(
                OperationId,
                "different-fingerprint"));
        }

        [Test]
        public void SingleOperationAndNormalWindowReopenDoNotCreateReloadMarker()
        {
            PackageOperationAutoResumeState.TrackActiveOperation(
                OperationId,
                RegistryFingerprint,
                isBulk: false);
            PackageOperationAutoResumeState.SimulateBeforeAssemblyReloadForTests();
            Assert.IsFalse(PackageOperationAutoResumeState.HasMatchingReloadMarker(
                OperationId,
                RegistryFingerprint));

            PackageOperationAutoResumeState.ResetForTests();
            PackageOperationAutoResumeState.TrackActiveOperation(
                OperationId,
                RegistryFingerprint,
                isBulk: true);
            PackageOperationAutoResumeState.DetachOperation(OperationId);
            PackageOperationAutoResumeState.SimulateBeforeAssemblyReloadForTests();

            Assert.IsFalse(PackageOperationAutoResumeState.HasMatchingReloadMarker(
                OperationId,
                RegistryFingerprint));
        }

        [Test]
        public void ReloadMarkerSurvivesReloadDisposalAndIsOneShot()
        {
            PackageOperationAutoResumeState.TrackActiveOperation(
                OperationId,
                RegistryFingerprint,
                isBulk: true);
            PackageOperationAutoResumeState.SimulateBeforeAssemblyReloadForTests();
            PackageOperationAutoResumeState.DetachOperation(OperationId);
            PackageOperationAutoResumeState.SimulateNewDomainForTests();

            Assert.IsTrue(PackageOperationAutoResumeState.HasMatchingReloadMarker(
                OperationId,
                RegistryFingerprint));

            PackageOperationAutoResumeState.AcknowledgeReloadMarker(OperationId);

            Assert.IsFalse(PackageOperationAutoResumeState.HasMatchingReloadMarker(
                OperationId,
                RegistryFingerprint));
        }

        [Test]
        public void CancelFailureCompletionAndEditorRestartClearEligibility()
        {
            AssertEligibilityCleared(PackageOperationAutoResumeState.DisqualifyOperation);
            AssertEligibilityCleared(_ => PackageOperationAutoResumeState.Clear());
            AssertEligibilityCleared(_ =>
                PackageOperationAutoResumeState.SimulateEditorRestartForTests());
        }

        [Test]
        public void SafeMatchingRecoveryAutoResumes()
        {
            PackageOperationRecoveryRecord recovery = CreateRecovery(
                PackageInstallProgressItemState.Completed,
                PackageInstallProgressItemState.Pending);
            PackageDependencyInstallPlan freshPlan = CreateFreshPlan(recovery.RegistryFingerprint);

            Assert.AreEqual(
                PackageOperationRecoveryDisposition.AutoResume,
                PackageInstallerWindow.GetRecoveryDispositionForTests(
                    recovery,
                    freshPlan,
                    hasMatchingReloadMarker: true));
        }

        [TestCase(PackageInstallProgressItemState.Failed)]
        [TestCase(PackageInstallProgressItemState.Blocked)]
        [TestCase(PackageInstallProgressItemState.Canceled)]
        public void UnsafeRecoveryStateRequiresPrompt(PackageInstallProgressItemState unsafeState)
        {
            PackageOperationRecoveryRecord recovery = CreateRecovery(
                unsafeState,
                PackageInstallProgressItemState.Pending);

            Assert.AreEqual(
                PackageOperationRecoveryDisposition.Prompt,
                PackageInstallerWindow.GetRecoveryDispositionForTests(
                    recovery,
                    CreateFreshPlan(recovery.RegistryFingerprint),
                    hasMatchingReloadMarker: true));
        }

        [Test]
        public void MissingMarkerRegistryDriftAndNoPendingWorkRequirePrompt()
        {
            PackageOperationRecoveryRecord pending = CreateRecovery(
                PackageInstallProgressItemState.Completed,
                PackageInstallProgressItemState.Pending);
            PackageOperationRecoveryRecord completed = CreateRecovery(
                PackageInstallProgressItemState.Completed,
                PackageInstallProgressItemState.AlreadyCorrect);

            Assert.AreEqual(
                PackageOperationRecoveryDisposition.Prompt,
                PackageInstallerWindow.GetRecoveryDispositionForTests(
                    pending,
                    CreateFreshPlan(pending.RegistryFingerprint),
                    hasMatchingReloadMarker: false));
            Assert.AreEqual(
                PackageOperationRecoveryDisposition.Prompt,
                PackageInstallerWindow.GetRecoveryDispositionForTests(
                    pending,
                    CreateFreshPlan("different-fingerprint"),
                    hasMatchingReloadMarker: true));
            Assert.AreEqual(
                PackageOperationRecoveryDisposition.Prompt,
                PackageInstallerWindow.GetRecoveryDispositionForTests(
                    completed,
                    CreateFreshPlan(completed.RegistryFingerprint),
                    hasMatchingReloadMarker: true));
        }

        private static void AssertEligibilityCleared(Action<string> clearAction)
        {
            PackageOperationAutoResumeState.ResetForTests();
            PackageOperationAutoResumeState.TrackActiveOperation(
                OperationId,
                RegistryFingerprint,
                isBulk: true);
            PackageOperationAutoResumeState.SimulateBeforeAssemblyReloadForTests();
            PackageOperationAutoResumeState.SimulateNewDomainForTests();

            clearAction(OperationId);

            Assert.IsFalse(PackageOperationAutoResumeState.HasMatchingReloadMarker(
                OperationId,
                RegistryFingerprint));
        }

        private static PackageOperationRecoveryRecord CreateRecovery(
            PackageInstallProgressItemState firstState,
            PackageInstallProgressItemState secondState)
        {
            PackageOperationRecoveryStep first = CreateRecoveryStep(
                "com.deucarian.first",
                "First",
                firstState);
            PackageOperationRecoveryStep second = CreateRecoveryStep(
                "com.deucarian.second",
                "Second",
                secondState);
            return new PackageOperationRecoveryRecord(
                OperationId,
                "Update All Packages",
                RegistryFingerprint,
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.Ticks,
                new[] { first, second },
                Array.Empty<string>(),
                new[]
                {
                    new PackageOperationRootRequest(first.PackageId, PackageChannel.Stable),
                    new PackageOperationRootRequest(second.PackageId, PackageChannel.Stable)
                });
        }

        private static PackageOperationRecoveryStep CreateRecoveryStep(
            string packageId,
            string displayName,
            PackageInstallProgressItemState state)
        {
            return new PackageOperationRecoveryStep(
                packageId,
                displayName,
                PackageChannel.Stable,
                "https://github.com/Deucarian/" + displayName + ".git#main",
                isDependency: false,
                prerequisitePackageIds: Array.Empty<string>(),
                rootPackageIds: new[] { packageId },
                rootPaths: new[] { displayName },
                dependencyReason: string.Empty,
                state: state,
                message: string.Empty);
        }

        private static PackageDependencyInstallPlan CreateFreshPlan(string registryFingerprint)
        {
            PackageDefinition first = CreatePackage("First", "com.deucarian.first");
            PackageDefinition second = CreatePackage("Second", "com.deucarian.second");
            return PackageDependencyInstallPlan.Success(
                new[]
                {
                    CreateStep(first),
                    CreateStep(second)
                },
                Array.Empty<string>(),
                registryFingerprint,
                rootRequests: new[]
                {
                    new PackageOperationRootRequest(first.PackageId, PackageChannel.Stable),
                    new PackageOperationRootRequest(second.PackageId, PackageChannel.Stable)
                });
        }

        private static PackageDependencyInstallStep CreateStep(PackageDefinition package)
        {
            return new PackageDependencyInstallStep(
                package,
                PackageChannel.Stable,
                isDependency: false,
                targetUrl: package.StableUrl,
                rootPackageIds: new[] { package.PackageId },
                rootPaths: new[] { package.DisplayName });
        }

        private static PackageDefinition CreatePackage(string displayName, string packageId)
        {
            return new PackageDefinition(
                displayName,
                packageId,
                "https://github.com/Deucarian/" + displayName + ".git#main",
                displayName + " package.");
        }
    }
}
