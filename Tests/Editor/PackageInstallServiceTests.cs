using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageInstallServiceTests
    {
        private string _temporaryProjectRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryProjectRoot = Path.Combine(
                Path.GetTempPath(),
                "Deucarian.PackageInstaller.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryProjectRoot);
            PackageInstallerSelfUpdateState.Clear();
            PackageInstallerActivityService.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PackageInstallerSelfUpdateState.Clear();
            PackageInstallerActivityService.ClearForTests();

            if (Directory.Exists(_temporaryProjectRoot))
            {
                Directory.Delete(_temporaryProjectRoot, true);
            }
        }

        [Test]
        public void FailedPrerequisiteBlocksDependentAndContinuesIndependentRoot()
        {
            PackageDefinition prerequisite = CreatePackage("Prerequisite", "com.deucarian.prerequisite");
            PackageDefinition dependent = CreatePackage(
                "Dependent",
                "com.deucarian.dependent",
                new[] { prerequisite.PackageId });
            PackageDefinition independent = CreatePackage("Independent", "com.deucarian.independent");
            PackageDefinition[] packages = { prerequisite, dependent, independent };
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            using (PackageDetectionService detection = new PackageDetectionService())
            {
                PackageDependencyInstaller planner = new PackageDependencyInstaller(
                    service,
                    detection,
                    () => packages);
                PackageDependencyInstallPlan plan = planner.CreateInstallPlan(
                    new[] { dependent, independent },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                Assert.IsTrue(service.InstallPlan(plan, "Install independent roots"));
                CollectionAssert.AreEqual(
                    new[] { prerequisite.StableUrl },
                    client.AddedUrls);

                LogAssert.Expect(
                    LogType.Error,
                    "[PackageInstaller.Install] Failed to install Prerequisite: prerequisite failed");
                client.Requests[0].CompleteFailure("prerequisite failed");
                service.UpdateForTests();

                CollectionAssert.AreEqual(
                    new[] { prerequisite.StableUrl, independent.StableUrl },
                    client.AddedUrls);
                Assert.AreEqual(
                    PackageInstallProgressItemState.Blocked,
                    GetProgress(service, dependent.PackageId).State);
                Assert.AreEqual(
                    PackageInstallProgressItemState.Active,
                    GetProgress(service, independent.PackageId).State);

                client.Requests[1].CompleteSuccess(independent.PackageId, "1.0.0");
                service.UpdateForTests();

                Assert.IsFalse(service.IsBusy);
                Assert.AreEqual(1, service.FailedSteps);
                Assert.AreEqual(1, service.BlockedSteps);
                Assert.AreEqual(1, service.SuccessfulSteps);
                Assert.IsFalse(client.AddedUrls.Contains(dependent.StableUrl));
                StringAssert.Contains("1 failed", service.LastStatusMessage);
                StringAssert.Contains("1 blocked", service.LastStatusMessage);
            }
        }

        [Test]
        public void FailedOperationSnapshotRestartsOnlyAffectedRoots()
        {
            PackageDefinition prerequisite = CreatePackage("Prerequisite", "com.deucarian.prerequisite");
            PackageDefinition dependent = CreatePackage(
                "Dependent",
                "com.deucarian.dependent",
                new[] { prerequisite.PackageId });
            PackageDefinition independent = CreatePackage("Independent", "com.deucarian.independent");
            PackageDefinition[] packages = { prerequisite, dependent, independent };
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            using (PackageDetectionService detection = new PackageDetectionService())
            {
                PackageDependencyInstaller planner = new PackageDependencyInstaller(
                    service,
                    detection,
                    () => packages);
                PackageDependencyInstallPlan plan = planner.CreateInstallPlan(
                    new[] { dependent, independent },
                    package => package.PackageId == dependent.PackageId
                        ? PackageChannel.Development
                        : PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(service.InstallPlan(plan, "Install independent roots"));
                LogAssert.Expect(
                    LogType.Error,
                    "[PackageInstaller.Install] Failed to install Prerequisite: prerequisite failed");
                client.Requests[0].CompleteFailure("prerequisite failed");
                service.UpdateForTests();
                client.Requests[1].CompleteSuccess(independent.PackageId, "1.0.0");
                service.UpdateForTests();

                PackageOperationTerminalSnapshot snapshot = service.TerminalOperationSnapshot;
                Assert.IsNotNull(snapshot);
                Assert.AreEqual(PackageOperationTerminalOutcome.Failed, snapshot.Outcome);
                Assert.IsTrue(snapshot.CanRestart);
                CollectionAssert.AreEqual(
                    new[] { dependent.PackageId },
                    snapshot.RestartRoots.Select(root => root.PackageId).ToArray());
                Assert.AreEqual(
                    PackageChannel.Development,
                    snapshot.RestartRoots.Single().Channel);
                Assert.AreEqual(
                    prerequisite.DevelopmentUrl,
                    snapshot.Steps.Single(step => step.PackageId == prerequisite.PackageId).TargetUrl);
                Assert.AreEqual(
                    PackageInstallProgressItemState.Blocked,
                    snapshot.Steps.Single(step => step.PackageId == dependent.PackageId).State);
                Assert.AreEqual(
                    PackageInstallProgressItemState.Completed,
                    snapshot.Steps.Single(step => step.PackageId == independent.PackageId).State);
                Assert.AreEqual(
                    PackageInstallerRetryKind.RestartOperation,
                    PackageInstallerActivityService.Latest.RetryKind);
            }
        }

        [Test]
        public void FailedFallbackDependencyRestartsSkippedRootOnItsRequestedChannel()
        {
            PackageDefinition dependency = new PackageDefinition(
                "Stable Dependency",
                "com.deucarian.stable-dependency",
                "https://example.com/Stable-Dependency.git#main",
                "Stable-only dependency.",
                Array.Empty<string>(),
                PackageType.Core,
                developmentUrl: string.Empty,
                category: "Core");
            PackageDefinition root = CreatePackage(
                "Development Root",
                "com.deucarian.development-root",
                new[] { dependency.PackageId });
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            using (PackageDetectionService detection = new PackageDetectionService())
            {
                detection.ReplaceInstalledPackageReferenceForTests(
                    root.PackageId,
                    root.DevelopmentUrl);
                PackageDependencyInstaller planner = new PackageDependencyInstaller(
                    service,
                    detection,
                    () => new[] { root, dependency });
                PackageDependencyInstallPlan plan = planner.CreateInstallPlan(
                    new[] { root },
                    _ => PackageChannel.Development,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                Assert.AreEqual(1, plan.Steps.Count);
                Assert.AreEqual(dependency.PackageId, plan.Steps.Single().PackageDefinition.PackageId);
                Assert.AreEqual(PackageChannel.Stable, plan.Steps.Single().Channel);
                Assert.AreEqual(PackageChannel.Development, plan.RootRequests.Single().Channel);
                Assert.IsTrue(service.InstallPlan(plan, "Install development root"));

                LogAssert.Expect(
                    LogType.Error,
                    "[PackageInstaller.Install] Failed to install Stable Dependency: dependency failed");
                client.Requests.Single().CompleteFailure("dependency failed");
                service.UpdateForTests();

                PackageOperationRootRequest restartRoot =
                    service.TerminalOperationSnapshot.RestartRoots.Single();
                Assert.AreEqual(root.PackageId, restartRoot.PackageId);
                Assert.AreEqual(PackageChannel.Development, restartRoot.Channel);
            }
        }

        [Test]
        public void CancelActiveOperationMarksPendingCanceledAndStartsNothingElse()
        {
            PackageDefinition first = CreatePackage("First", "com.deucarian.first");
            PackageDefinition second = CreatePackage("Second", "com.deucarian.second");
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            {
                PackageDependencyInstallPlan plan = CreateIndependentPlan(first, second);

                Assert.IsTrue(service.InstallPlan(plan, "Cancel test"));
                Assert.IsTrue(service.CancelCurrentOperation());
                Assert.AreEqual(
                    PackageInstallProgressItemState.Canceled,
                    GetProgress(service, second.PackageId).State);

                client.Requests[0].CompleteSuccess(first.PackageId, "1.0.0");
                service.UpdateForTests();

                CollectionAssert.AreEqual(new[] { first.StableUrl }, client.AddedUrls);
                Assert.IsFalse(service.IsBusy);
                Assert.AreEqual(1, service.SuccessfulSteps);
                Assert.AreEqual(1, service.CanceledSteps);
                Assert.AreEqual(
                    "Cancel test canceled with 1 succeeded, 1 canceled.",
                    service.LastStatusMessage);
            }
        }

        [Test]
        public void CanceledOperationSnapshotRestartsOnlyStepsThatNeverStarted()
        {
            PackageDefinition first = CreatePackage("First", "com.deucarian.first");
            PackageDefinition second = CreatePackage("Second", "com.deucarian.second");
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            {
                Assert.IsTrue(service.InstallPlan(CreateIndependentPlan(first, second), "Cancel test"));
                Assert.IsTrue(service.CancelCurrentOperation());
                client.Requests[0].CompleteSuccess(first.PackageId, "1.0.0");
                service.UpdateForTests();

                PackageOperationTerminalSnapshot snapshot = service.TerminalOperationSnapshot;
                Assert.IsNotNull(snapshot);
                Assert.AreEqual(PackageOperationTerminalOutcome.Canceled, snapshot.Outcome);
                Assert.IsTrue(snapshot.CanRestart);
                CollectionAssert.AreEqual(
                    new[] { second.PackageId },
                    snapshot.RestartRoots.Select(root => root.PackageId).ToArray());
                Assert.AreEqual(
                    PackageInstallProgressItemState.Completed,
                    snapshot.Steps.Single(step => step.PackageId == first.PackageId).State);
                Assert.AreEqual(
                    PackageInstallProgressItemState.Canceled,
                    snapshot.Steps.Single(step => step.PackageId == second.PackageId).State);
                Assert.AreEqual(
                    PackageInstallerRetryKind.RestartOperation,
                    PackageInstallerActivityService.Latest.RetryKind);
            }
        }

        [Test]
        public void SuccessfulOperationSnapshotDoesNotExposeRestart()
        {
            PackageDefinition package = CreatePackage("Success", "com.deucarian.success");
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            {
                Assert.IsTrue(service.InstallPlan(CreateIndependentPlan(package), "Success test"));
                client.Requests[0].CompleteSuccess(package.PackageId, "1.0.0");
                service.UpdateForTests();

                Assert.IsNotNull(service.TerminalOperationSnapshot);
                Assert.AreEqual(
                    PackageOperationTerminalOutcome.Succeeded,
                    service.TerminalOperationSnapshot.Outcome);
                Assert.IsFalse(service.TerminalOperationSnapshot.CanRestart);
                Assert.AreEqual(
                    PackageInstallerRetryKind.None,
                    PackageInstallerActivityService.Latest.RetryKind);
            }
        }

        [Test]
        public void FailedRemoveDoesNotOfferInstallRestart()
        {
            PackageDefinition package = CreatePackage("Removed", "com.deucarian.removed");
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            {
                Assert.IsTrue(service.Remove(package));
                LogAssert.Expect(
                    LogType.Error,
                    "[PackageInstaller.Install] Failed to remove Removed: removal failed");
                client.Requests[0].CompleteFailure("removal failed");
                service.UpdateForTests();

                Assert.IsNotNull(service.TerminalOperationSnapshot);
                Assert.AreEqual(
                    PackageOperationTerminalOutcome.Failed,
                    service.TerminalOperationSnapshot.Outcome);
                Assert.IsFalse(service.TerminalOperationSnapshot.CanRestart);
                Assert.AreEqual(
                    PackageInstallerRetryKind.None,
                    PackageInstallerActivityService.Latest.RetryKind);
            }
        }

        [Test]
        public void TerminalRetryReplansFromCurrentRegistryInsteadOfSavedTarget()
        {
            const string packageId = "com.deucarian.retry";
            const string previousTarget = "https://example.com/Retry.git#old-ref";
            const string currentTarget = "https://example.com/Retry.git#new-ref";
            PackageDefinition currentPackage = new PackageDefinition(
                "Retry",
                packageId,
                currentTarget,
                "Retry package.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://example.com/Retry.git#develop",
                category: "Core");
            PackageOperationTerminalSnapshot snapshot = new PackageOperationTerminalSnapshot(
                "operation-id",
                "Install Retry",
                PackageOperationTerminalOutcome.Failed,
                "Install Retry failed.",
                "Synthetic failure.",
                new[] { new PackageOperationRootRequest(packageId, PackageChannel.Stable) },
                new[]
                {
                    new PackageOperationStepSnapshot(
                        packageId,
                        currentPackage.DisplayName,
                        PackageChannel.Stable,
                        previousTarget,
                        isDependency: false,
                        rootPackageIds: new[] { packageId },
                        state: PackageInstallProgressItemState.Failed,
                        message: "Synthetic failure.")
                },
                Array.Empty<string>(),
                DateTime.UtcNow);
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(
                       new ControlledPackageInstallClient(),
                       repository))
            using (PackageDetectionService detection = new PackageDetectionService())
            {
                PackageDependencyInstaller installer = new PackageDependencyInstaller(
                    service,
                    detection,
                    () => new[] { currentPackage });

                PackageDependencyInstallPlan freshPlan =
                    PackageInstallerWindow.CreateFreshTerminalRetryPlanForTests(
                        snapshot,
                        installer,
                        new[] { currentPackage });

                Assert.IsNotNull(freshPlan);
                Assert.IsTrue(freshPlan.IsValid, freshPlan.ErrorMessage);
                Assert.AreEqual(currentTarget, freshPlan.Steps.Single().TargetUrl);
                Assert.AreNotEqual(previousTarget, freshPlan.Steps.Single().TargetUrl);
                string delta = PackageInstallerWindow.FormatTerminalRetryPlanDeltaForTests(
                    snapshot,
                    freshPlan);
                StringAssert.Contains("Changed: Retry", delta);
                StringAssert.Contains(previousTarget, delta);
                StringAssert.Contains(currentTarget, delta);
            }
        }

        [Test]
        public void ResumeUsesPersistedExactUrlWithoutRegistryResolution()
        {
            PackageDefinition package = CreatePackage("Recovery", "com.deucarian.recovery");
            const string exactTarget =
                "https://github.com/Deucarian/Recovery.git?path=/Packages/Recovery#0123456789abcdef";
            PackageDependencyInstallStep step = new PackageDependencyInstallStep(
                package,
                PackageChannel.Stable,
                isDependency: false,
                targetUrl: exactTarget,
                rootPackageIds: new[] { package.PackageId },
                rootPaths: new[] { package.DisplayName });
            PackageDependencyInstallPlan plan = PackageDependencyInstallPlan.Success(
                new[] { step },
                Array.Empty<string>(),
                registryFingerprint: "fingerprint-before-registry-change");
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);
            ControlledPackageInstallClient writerClient = new ControlledPackageInstallClient();

            using (PackageInstallService writer = new PackageInstallService(writerClient, repository))
            {
                Assert.IsTrue(writer.InstallPlan(plan, "Exact target recovery"));
                writer.SavePendingOperationForTests();
                CollectionAssert.AreEqual(new[] { exactTarget }, writerClient.AddedUrls);
            }

            ControlledPackageInstallClient readerClient = new ControlledPackageInstallClient();
            using (PackageInstallService reader = new PackageInstallService(readerClient, repository))
            {
                LogAssert.Expect(
                    LogType.Warning,
                    "[PackageInstaller.Install] The registry fingerprint changed; the saved operation must be replanned before its exact URLs can be reused.");
                Assert.IsFalse(reader.ResumeSavedOperation("different-registry-fingerprint"));
                CollectionAssert.IsEmpty(readerClient.AddedUrls);
                Assert.IsTrue(File.Exists(repository.StatePathForTests));

                Assert.IsTrue(reader.ResumeSavedOperation("fingerprint-before-registry-change"));
                CollectionAssert.AreEqual(new[] { exactTarget }, readerClient.AddedUrls);
                Assert.AreEqual(exactTarget, reader.CurrentUrl);

                readerClient.Requests[0].CompleteSuccess(package.PackageId, "1.0.0");
                reader.UpdateForTests();
                Assert.IsFalse(File.Exists(repository.StatePathForTests));
            }
        }

        [Test]
        public void ResumeSkipsRequestThatRefreshAlreadyFoundAtExactTarget()
        {
            PackageDefinition package = CreatePackage("Recovered", "com.deucarian.recovered");
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService writer = new PackageInstallService(
                       new ControlledPackageInstallClient(),
                       repository))
            {
                Assert.IsTrue(writer.InstallPlan(
                    CreateIndependentPlan(package),
                    "Already correct recovery"));
                writer.SavePendingOperationForTests();
            }

            ControlledPackageInstallClient readerClient = new ControlledPackageInstallClient();
            using (PackageInstallService reader = new PackageInstallService(readerClient, repository))
            {
                reader.ExactTargetAlreadyInstalled = (packageId, targetUrl, previousIdentity) =>
                    packageId == package.PackageId && targetUrl == package.StableUrl;

                Assert.IsTrue(reader.ResumeSavedOperation("registry-fingerprint"));
                CollectionAssert.IsEmpty(readerClient.AddedUrls);
                Assert.AreEqual(
                    PackageInstallProgressItemState.AlreadyCorrect,
                    GetProgress(reader, package.PackageId).State);
                Assert.IsFalse(reader.IsBusy);
                Assert.IsFalse(File.Exists(repository.StatePathForTests));
            }
        }

        [Test]
        public void RestartSavedOperationReplaysCompletedStepsAndDiscardClearsState()
        {
            PackageDefinition first = CreatePackage("Restart First", "com.deucarian.restart-first");
            PackageDefinition second = CreatePackage("Restart Second", "com.deucarian.restart-second");
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);
            ControlledPackageInstallClient writerClient = new ControlledPackageInstallClient();

            using (PackageInstallService writer = new PackageInstallService(writerClient, repository))
            {
                Assert.IsTrue(writer.InstallPlan(CreateIndependentPlan(first, second), "Restart recovery"));
                writerClient.Requests[0].CompleteSuccess(first.PackageId, "1.0.0");
                writer.UpdateForTests();
                Assert.AreEqual(second.StableUrl, writer.CurrentUrl);
                Assert.IsTrue(writer.TryGetSavedOperation(
                    out PackageOperationRecoveryRecord saved,
                    out string savedError), savedError);
                Assert.IsTrue(saved.CanResume);
                Assert.IsTrue(saved.CanRestart);
                Assert.IsTrue(saved.HasCompletedSteps);
            }

            ControlledPackageInstallClient restartClient = new ControlledPackageInstallClient();
            using (PackageInstallService restarted = new PackageInstallService(restartClient, repository))
            {
                LogAssert.Expect(
                    LogType.Warning,
                    "[PackageInstaller.Install] The registry fingerprint changed; the saved operation must be replanned before its exact URLs can be reused.");
                Assert.IsFalse(restarted.RestartSavedOperation("different-registry-fingerprint"));
                CollectionAssert.IsEmpty(restartClient.AddedUrls);
                Assert.IsTrue(File.Exists(repository.StatePathForTests));

                Assert.IsTrue(restarted.RestartSavedOperation("registry-fingerprint"));
                CollectionAssert.AreEqual(new[] { first.StableUrl }, restartClient.AddedUrls);
                Assert.IsTrue(restarted.CancelCurrentOperation());
                restartClient.Requests[0].CompleteSuccess(first.PackageId, "1.0.0");
                restarted.UpdateForTests();
            }

            Assert.IsFalse(File.Exists(repository.StatePathForTests));

            ControlledPackageInstallClient discardWriterClient = new ControlledPackageInstallClient();
            using (PackageInstallService discardWriter =
                   new PackageInstallService(discardWriterClient, repository))
            {
                Assert.IsTrue(discardWriter.InstallPlan(CreateIndependentPlan(first), "Discard recovery"));
                discardWriter.SavePendingOperationForTests();
            }

            using (PackageInstallService discarder = new PackageInstallService(
                       new ControlledPackageInstallClient(),
                       repository))
            {
                Assert.IsTrue(discarder.DiscardSavedOperation());
                Assert.IsFalse(discarder.HasSavedOperation);
            }
        }

        [Test]
        public void ReinstallPreflightDeclineStartsNoRequest()
        {
            PackageDefinition package = CreatePackage("Reinstall", "com.deucarian.reinstall");
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            using (PackageDetectionService detection = new PackageDetectionService())
            {
                PackageDependencyInstaller installer = new PackageDependencyInstaller(
                    service,
                    detection,
                    () => new[] { package });
                int confirmationCount = 0;
                PackageDependencyInstallPlan confirmedPlan = null;
                installer.PreflightConfirmation = (plan, operationName) =>
                {
                    confirmationCount++;
                    confirmedPlan = plan;
                    Assert.AreEqual("Reinstall Reinstall", operationName);
                    return false;
                };

                installer.ReinstallWithDependencies(package, _ => PackageChannel.Stable);

                Assert.AreEqual(1, confirmationCount);
                Assert.IsNotNull(confirmedPlan);
                Assert.IsTrue(confirmedPlan.HasDestructiveRisk);
                Assert.IsTrue(confirmedPlan.RequiresPreflight);
                CollectionAssert.IsEmpty(client.AddedUrls);
                Assert.IsFalse(service.IsBusy);
            }
        }

        [Test]
        public void OrdinarySingleInstallDoesNotInvokePreflight()
        {
            PackageDefinition package = CreatePackage("Ordinary", "com.deucarian.ordinary");
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            using (PackageDetectionService detection = new PackageDetectionService())
            {
                PackageDependencyInstaller installer = new PackageDependencyInstaller(
                    service,
                    detection,
                    () => new[] { package });
                int confirmationCount = 0;
                installer.PreflightConfirmation = (_, __) =>
                {
                    confirmationCount++;
                    return true;
                };

                installer.InstallWithDependencies(package, _ => PackageChannel.Stable);

                Assert.AreEqual(0, confirmationCount);
                CollectionAssert.AreEqual(new[] { package.StableUrl }, client.AddedUrls);
                client.Requests.Single().CompleteSuccess(package.PackageId, "1.0.0");
                service.UpdateForTests();
                Assert.IsFalse(service.IsBusy);
            }
        }

        [Test]
        public void BulkRootsWithOnePendingSharedDependencyRequireAcceptedPreflight()
        {
            PackageDefinition shared = CreatePackage(
                "Bulk Shared",
                "com.deucarian.bulk-shared");
            PackageDefinition firstRoot = CreatePackage(
                "Bulk First",
                "com.deucarian.bulk-first",
                new[] { shared.PackageId });
            PackageDefinition secondRoot = CreatePackage(
                "Bulk Second",
                "com.deucarian.bulk-second",
                new[] { shared.PackageId });
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            using (PackageDetectionService detection = new PackageDetectionService())
            {
                detection.ReplaceInstalledPackageNamesForTests(
                    new[] { firstRoot.PackageId, secondRoot.PackageId });
                PackageDependencyInstaller installer = new PackageDependencyInstaller(
                    service,
                    detection,
                    () => new[] { shared, firstRoot, secondRoot });
                int confirmationCount = 0;
                installer.PreflightConfirmation = (plan, _) =>
                {
                    confirmationCount++;
                    Assert.AreEqual(1, plan.Steps.Count);
                    Assert.IsTrue(plan.IsBulk);
                    Assert.IsFalse(plan.IsMultiStep);
                    return false;
                };

                installer.InstallManyWithDependencies(
                    new[] { firstRoot, secondRoot },
                    _ => PackageChannel.Stable,
                    "Bulk roots");

                Assert.AreEqual(1, confirmationCount);
                CollectionAssert.IsEmpty(client.AddedUrls);
                Assert.IsFalse(service.IsBusy);
            }
        }

        [Test]
        public void PlannerFailureOffersRetryAndReplansFromCurrentRegistry()
        {
            PackageDefinition dependency = CreatePackage(
                "Retry Dependency",
                "com.deucarian.retry-dependency");
            PackageDefinition root = CreatePackage(
                "Retry Root",
                "com.deucarian.retry-root",
                new[] { dependency.PackageId });
            List<PackageDefinition> registeredPackages = new List<PackageDefinition> { root };
            ControlledPackageInstallClient client = new ControlledPackageInstallClient();
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);

            using (PackageInstallService service = new PackageInstallService(client, repository))
            using (PackageDetectionService detection = new PackageDetectionService())
            {
                PackageDependencyInstaller installer = new PackageDependencyInstaller(
                    service,
                    detection,
                    () => registeredPackages);
                LogAssert.Expect(
                    LogType.Error,
                    "[PackageInstaller.Install] Cannot install Retry Root because dependency " +
                    dependency.PackageId + " is unavailable.");

                installer.InstallWithDependencies(root, _ => PackageChannel.Development);

                Assert.IsTrue(installer.CanRetryLastPlannerFailure);
                Assert.AreEqual(
                    PackageInstallerRetryKind.ReplanOperation,
                    PackageInstallerActivityService.Latest.RetryKind);
                CollectionAssert.IsEmpty(client.AddedUrls);

                registeredPackages.Add(dependency);
                detection.ReplaceInstalledPackageForTests(
                    dependency.PackageId,
                    dependency.DevelopmentUrl,
                    PackageInstallSourceType.Git);
                Assert.IsTrue(installer.RetryLastPlannerFailure());

                Assert.IsFalse(installer.CanRetryLastPlannerFailure);
                CollectionAssert.AreEqual(new[] { root.DevelopmentUrl }, client.AddedUrls);
            }
        }

        [TestCase(false, false, false, false)]
        [TestCase(true, true, false, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, false, false, true)]
        public void PlannerRetryRefreshReadinessWaitsForBothRefreshes(
            bool pending,
            bool registryRefreshing,
            bool detectionRefreshing,
            bool expected)
        {
            Assert.AreEqual(
                expected,
                PackageInstallerWindow.IsPlannerRetryRefreshReadyForTests(
                    pending,
                    registryRefreshing,
                    detectionRefreshing));
        }

        [Test]
        public void RecoveryRepositoryAtomicallyReplacesVersionedRecord()
        {
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);
            PackageOperationRecoveryRecord first = CreateRecoveryRecord("https://example.com/first.git#main");
            PackageOperationRecoveryRecord second = CreateRecoveryRecord("https://example.com/second.git#main");

            Assert.IsTrue(repository.Save(first, out string firstError), firstError);
            Assert.IsTrue(repository.Save(second, out string secondError), secondError);
            Assert.IsTrue(repository.TryLoad(out PackageOperationRecoveryRecord loaded, out string loadError), loadError);

            Assert.AreEqual(3, PackageOperationStateRepository.CurrentSchemaVersion);
            Assert.AreEqual("https://example.com/second.git#main", loaded.Steps.Single().TargetUrl);
            Assert.IsTrue(loaded.CanResume);
            Assert.IsTrue(loaded.CanRestart);
            StringAssert.Contains("\"schemaVersion\": 3", File.ReadAllText(repository.StatePathForTests));
            CollectionAssert.IsEmpty(
                Directory.GetFiles(Path.GetDirectoryName(repository.StatePathForTests), "*.tmp"));
        }

        [Test]
        public void RecoveryRepositoryPersistsRequestedRootChannelIndependentlyFromStepChannel()
        {
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);
            PackageOperationRecoveryRecord source = CreateRecoveryRecord(
                "https://example.com/dependency.git#main",
                PackageChannel.Development);
            Assert.AreEqual(PackageChannel.Development, source.RootRequests.Single().Channel);
            PackageOperationRecoveryRecord record = new PackageOperationRecoveryRecord(
                source.OperationId,
                source.OperationName,
                source.RegistryFingerprint,
                source.CreatedAtUtcTicks,
                source.UpdatedAtUtcTicks,
                source.Steps,
                source.Messages,
                new[]
                {
                    new PackageOperationRootRequest(
                        source.Steps.Single().PackageId,
                        PackageChannel.Development)
                });

            Assert.IsTrue(repository.Save(record, out string saveError), saveError);
            Assert.IsTrue(repository.TryLoad(out PackageOperationRecoveryRecord loaded, out string loadError), loadError);

            Assert.AreEqual(PackageChannel.Stable, loaded.Steps.Single().Channel);
            Assert.AreEqual(PackageChannel.Development, loaded.Steps.Single().RequestedChannel);
            Assert.AreEqual(PackageChannel.Development, loaded.RootRequests.Single().Channel);
            StringAssert.Contains(
                "\"requestedChannel\": 1",
                File.ReadAllText(repository.StatePathForTests));
            Assert.AreEqual(
                PackageChannel.Development,
                PackageInstallerWindow.GetRecoveryRequestedChannelForTests(
                    loaded,
                    loaded.RootRequests.Single().PackageId,
                    PackageChannel.Stable));
        }

        [Test]
        public void RecoveryRepositorySchemaTwoInfersRequestedChannelFromRootRequest()
        {
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);
            WriteRecoveryJson(
                repository,
                SingleRecoveryStepJson(0, 0),
                rootChannel: 1);

            Assert.IsTrue(repository.TryLoad(
                out PackageOperationRecoveryRecord loaded,
                out string errorMessage), errorMessage);
            Assert.AreEqual(PackageChannel.Stable, loaded.Steps.Single().Channel);
            Assert.AreEqual(PackageChannel.Development, loaded.Steps.Single().RequestedChannel);
        }

        [TestCase(99, 0)]
        [TestCase(0, 99)]
        public void RecoveryRepositoryRejectsInvalidEnumValues(int channel, int state)
        {
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);
            WriteRecoveryJson(repository, SingleRecoveryStepJson(channel, state));

            Assert.IsFalse(repository.TryLoad(out _, out string errorMessage));
            StringAssert.Contains("invalid", errorMessage.ToLowerInvariant());
        }

        [Test]
        public void RecoveryRepositoryRejectsInvalidRequestedChannelInCurrentSchema()
        {
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);
            WriteRecoveryJson(
                repository,
                SingleRecoveryStepJson(0, 0, requestedChannel: 99),
                schemaVersion: PackageOperationStateRepository.CurrentSchemaVersion);

            Assert.IsFalse(repository.TryLoad(out _, out string errorMessage));
            StringAssert.Contains("invalid requested channel", errorMessage);
        }

        [Test]
        public void RecoveryRepositoryRejectsDuplicatePackageSteps()
        {
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);
            string step = SingleRecoveryStepJson(0, 0);
            WriteRecoveryJson(repository, step + "," + step);

            Assert.IsFalse(repository.TryLoad(out _, out string errorMessage));
            StringAssert.Contains("duplicate steps", errorMessage);
        }

        [Test]
        public void RecoveryRepositoryRejectsMissingPrerequisiteStep()
        {
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);
            WriteRecoveryJson(
                repository,
                SingleRecoveryStepJson(
                    0,
                    0,
                    "[\"com.deucarian.missing-prerequisite\"]"));

            Assert.IsFalse(repository.TryLoad(out _, out string errorMessage));
            StringAssert.Contains("invalid prerequisite", errorMessage);
        }

        [Test]
        public void RecoveryRepositoryRejectsUnsupportedSchema()
        {
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryProjectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(repository.StatePathForTests));
            File.WriteAllText(repository.StatePathForTests, "{ \"schemaVersion\": 99, \"steps\": [] }");

            Assert.IsFalse(repository.TryLoad(out _, out string errorMessage));
            StringAssert.Contains("Unsupported pending package operation schemaVersion", errorMessage);
        }

        private static PackageInstallProgressItem GetProgress(
            PackageInstallService service,
            string packageId)
        {
            return service.ProgressItems.Single(item => item.PackageId == packageId);
        }

        [Test]
        public void Recovery_ReusesExactTargetsOnlyForMatchingValidRegistryFingerprint()
        {
            PackageOperationRecoveryRecord recovery = CreateRecoveryRecord(
                "https://github.com/Deucarian/Recovery-Test.git#main");
            PackageDefinition package = CreatePackage(
                "Recovery Test",
                "com.deucarian.recovery-test");
            PackageDependencyInstallStep step = new PackageDependencyInstallStep(
                package,
                PackageChannel.Stable,
                isDependency: false,
                targetUrl: package.StableUrl,
                rootPackageIds: new[] { package.PackageId },
                rootPaths: new[] { package.DisplayName });
            PackageDependencyInstallPlan matching = PackageDependencyInstallPlan.Success(
                new[] { step },
                Array.Empty<string>(),
                registryFingerprint: "registry-fingerprint");
            PackageDependencyInstallPlan drifted = PackageDependencyInstallPlan.Success(
                new[] { step },
                Array.Empty<string>(),
                registryFingerprint: "new-registry-fingerprint");
            PackageDependencyInstallPlan invalid = PackageDependencyInstallPlan.Failure(
                "Root is no longer registered.",
                Array.Empty<string>());

            Assert.IsTrue(PackageInstallerWindow.CanReuseSavedExactTargetsForTests(recovery, matching));
            Assert.IsFalse(PackageInstallerWindow.CanReuseSavedExactTargetsForTests(recovery, drifted));
            Assert.IsFalse(PackageInstallerWindow.CanReuseSavedExactTargetsForTests(recovery, invalid));
            Assert.IsFalse(PackageInstallerWindow.CanReuseSavedExactTargetsForTests(recovery, null));
        }

        [Test]
        public void Recovery_RegistryDriftDeltaShowsOldAndFreshExactTargets()
        {
            const string oldTarget = "https://github.com/Deucarian/Recovery-Test.git#old-ref";
            const string freshTarget = "https://github.com/Deucarian/Recovery-Test.git#fresh-ref";
            PackageOperationRecoveryRecord recovery = CreateRecoveryRecord(oldTarget);
            PackageDefinition package = new PackageDefinition(
                "Recovery Test",
                "com.deucarian.recovery-test",
                freshTarget,
                "Recovery test package.",
                Array.Empty<string>());
            PackageDependencyInstallPlan freshPlan = PackageDependencyInstallPlan.Success(
                new[]
                {
                    new PackageDependencyInstallStep(
                        package,
                        PackageChannel.Stable,
                        isDependency: false,
                        targetUrl: freshTarget,
                        rootPackageIds: new[] { package.PackageId },
                        rootPaths: new[] { package.DisplayName })
                },
                Array.Empty<string>(),
                registryFingerprint: "fresh-registry-fingerprint");

            string delta = PackageInstallerWindow.FormatRecoveryPlanDeltaForTests(
                recovery,
                freshPlan);

            StringAssert.Contains("Changed: Recovery Test", delta);
            StringAssert.Contains(oldTarget, delta);
            StringAssert.Contains(freshTarget, delta);
        }

        [Test]
        public void Recovery_RegistryDriftDeltaShowsDependencyGraphChangesWithoutTargetChange()
        {
            PackageDefinition package = CreatePackage(
                "Recovery Test",
                "com.deucarian.recovery-test");
            PackageOperationRecoveryRecord recovery = new PackageOperationRecoveryRecord(
                Guid.NewGuid().ToString("N"),
                "Recovery test",
                "old-registry-fingerprint",
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.Ticks,
                new[]
                {
                    new PackageOperationRecoveryStep(
                        package.PackageId,
                        package.DisplayName,
                        PackageChannel.Stable,
                        package.StableUrl,
                        isDependency: false,
                        prerequisitePackageIds: Array.Empty<string>(),
                        rootPackageIds: new[] { package.PackageId },
                        rootPaths: new[] { package.DisplayName },
                        dependencyReason: string.Empty,
                        state: PackageInstallProgressItemState.Pending,
                        message: string.Empty,
                        requestedChannel: PackageChannel.Stable)
                },
                Array.Empty<string>());
            PackageDependencyInstallPlan freshPlan = PackageDependencyInstallPlan.Success(
                new[]
                {
                    new PackageDependencyInstallStep(
                        package,
                        PackageChannel.Stable,
                        isDependency: true,
                        targetUrl: package.StableUrl,
                        prerequisitePackageIds: new[] { "com.deucarian.new-prerequisite" },
                        rootPackageIds: new[] { "com.deucarian.new-root" },
                        rootPaths: new[] { "New Root -> Recovery Test" },
                        dependencyReason: "Required by New Root.",
                        requestedChannel: PackageChannel.Development)
                },
                Array.Empty<string>(),
                registryFingerprint: "fresh-registry-fingerprint");

            string delta = PackageInstallerWindow.FormatRecoveryPlanDeltaForTests(
                recovery,
                freshPlan);

            StringAssert.Contains("Changed: Recovery Test", delta);
            StringAssert.Contains("requested channel: Stable -> Development", delta);
            StringAssert.Contains("role: requested root -> dependency", delta);
            StringAssert.Contains("prerequisites: (none) -> com.deucarian.new-prerequisite", delta);
            StringAssert.Contains(
                "root packages: com.deucarian.recovery-test -> com.deucarian.new-root",
                delta);
            StringAssert.Contains(
                "root paths: Recovery Test -> New Root -> Recovery Test",
                delta);
            StringAssert.Contains("dependency reason: (none) -> Required by New Root.", delta);
            StringAssert.DoesNotContain("target:", delta);
        }

        [Test]
        public void Recovery_RegistryDriftDeltaShowsDetectedInstalledStateChangesWithoutTargetChange()
        {
            PackageDefinition package = CreatePackage(
                "Recovery Test",
                "com.deucarian.recovery-test");
            PackageOperationRecoveryRecord recovery = new PackageOperationRecoveryRecord(
                Guid.NewGuid().ToString("N"),
                "Recovery test",
                "old-registry-fingerprint",
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.Ticks,
                new[]
                {
                    new PackageOperationRecoveryStep(
                        package.PackageId,
                        package.DisplayName,
                        PackageChannel.Stable,
                        package.StableUrl,
                        isDependency: false,
                        prerequisitePackageIds: Array.Empty<string>(),
                        rootPackageIds: new[] { package.PackageId },
                        rootPaths: new[] { package.DisplayName },
                        dependencyReason: string.Empty,
                        state: PackageInstallProgressItemState.Pending,
                        message: string.Empty,
                        detectedCurrentSource: "Git",
                        detectedCurrentVersion: "1.1.61",
                        detectedCurrentIdentity: "git-old",
                        requestedChannel: PackageChannel.Stable)
                },
                Array.Empty<string>());
            PackageDependencyInstallPlan freshPlan = PackageDependencyInstallPlan.Success(
                new[]
                {
                    new PackageDependencyInstallStep(
                        package,
                        PackageChannel.Stable,
                        isDependency: false,
                        targetUrl: package.StableUrl,
                        rootPackageIds: new[] { package.PackageId },
                        rootPaths: new[] { package.DisplayName },
                        detectedCurrentSource: "Registry",
                        detectedCurrentVersion: "1.1.62",
                        detectedCurrentIdentity: "registry-new",
                        requestedChannel: PackageChannel.Stable)
                },
                Array.Empty<string>(),
                registryFingerprint: "fresh-registry-fingerprint");

            string delta = PackageInstallerWindow.FormatRecoveryPlanDeltaForTests(
                recovery,
                freshPlan);

            StringAssert.Contains("Changed: Recovery Test", delta);
            StringAssert.Contains("detected source: Git -> Registry", delta);
            StringAssert.Contains("detected version: 1.1.61 -> 1.1.62", delta);
            StringAssert.Contains("detected identity: git-old -> registry-new", delta);
            StringAssert.DoesNotContain("target:", delta);
        }

        [Test]
        public void RecoveryModelsExposeImmutableSnapshotCollections()
        {
            string[] prerequisiteIds = { "com.deucarian.prerequisite" };
            string[] rootIds = { "com.deucarian.root" };
            string[] rootPaths = { "Root -> Recovery" };
            PackageOperationRecoveryStep step = new PackageOperationRecoveryStep(
                "com.deucarian.recovery-test",
                "Recovery Test",
                PackageChannel.Stable,
                "https://github.com/Deucarian/Recovery-Test.git#main",
                isDependency: true,
                prerequisitePackageIds: prerequisiteIds,
                rootPackageIds: rootIds,
                rootPaths: rootPaths,
                dependencyReason: "Required by Root.",
                state: PackageInstallProgressItemState.Pending,
                message: string.Empty);
            PackageOperationRecoveryStep[] steps = { step };
            string[] messages = { "Original message" };
            PackageOperationRecoveryRecord record = new PackageOperationRecoveryRecord(
                Guid.NewGuid().ToString("N"),
                "Recovery test",
                "registry-fingerprint",
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.Ticks,
                steps,
                messages);

            prerequisiteIds[0] = "com.deucarian.changed";
            rootIds[0] = "com.deucarian.changed-root";
            rootPaths[0] = "Changed Path";
            steps[0] = null;
            messages[0] = "Changed message";

            CollectionAssert.AreEqual(
                new[] { "com.deucarian.prerequisite" },
                step.PrerequisitePackageIds);
            CollectionAssert.AreEqual(new[] { "com.deucarian.root" }, step.RootPackageIds);
            CollectionAssert.AreEqual(new[] { "Root -> Recovery" }, step.RootPaths);
            Assert.AreSame(step, record.Steps.Single());
            CollectionAssert.AreEqual(new[] { "Original message" }, record.Messages);
            Assert.IsFalse(step.PrerequisitePackageIds is string[]);
            Assert.IsFalse(record.Steps is PackageOperationRecoveryStep[]);
            Assert.IsFalse(record.Messages is string[]);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)step.PrerequisitePackageIds)[0] = "mutated");
            Assert.Throws<NotSupportedException>(() =>
                ((IList<PackageOperationRecoveryStep>)record.Steps)[0] = null);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)record.Messages)[0] = "mutated");
        }

        private static PackageDependencyInstallPlan CreateIndependentPlan(
            params PackageDefinition[] packages)
        {
            return PackageDependencyInstallPlan.Success(
                packages.Select(package => new PackageDependencyInstallStep(
                    package,
                    PackageChannel.Stable,
                    isDependency: false,
                    targetUrl: package.StableUrl,
                    rootPackageIds: new[] { package.PackageId },
                    rootPaths: new[] { package.DisplayName })),
                Array.Empty<string>(),
                registryFingerprint: "registry-fingerprint");
        }

        private static PackageOperationRecoveryRecord CreateRecoveryRecord(
            string targetUrl,
            PackageChannel? requestedChannel = null)
        {
            return new PackageOperationRecoveryRecord(
                Guid.NewGuid().ToString("N"),
                "Recovery test",
                "registry-fingerprint",
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.Ticks,
                new[]
                {
                    new PackageOperationRecoveryStep(
                        "com.deucarian.recovery-test",
                        "Recovery Test",
                        PackageChannel.Stable,
                        targetUrl,
                        isDependency: false,
                        prerequisitePackageIds: Array.Empty<string>(),
                        rootPackageIds: new[] { "com.deucarian.recovery-test" },
                        rootPaths: new[] { "Recovery Test" },
                        dependencyReason: string.Empty,
                        state: PackageInstallProgressItemState.Pending,
                        message: string.Empty,
                        requestedChannel: requestedChannel)
                },
                Array.Empty<string>());
        }

        private static string SingleRecoveryStepJson(
            int channel,
            int state,
            string prerequisitePackageIds = "[]",
            int? requestedChannel = null)
        {
            return "{" +
                   "\"packageId\":\"com.deucarian.recovery-test\"," +
                   "\"displayName\":\"Recovery Test\"," +
                   "\"channel\":" + channel + "," +
                   (requestedChannel.HasValue
                       ? "\"requestedChannel\":" + requestedChannel.Value + ","
                       : string.Empty) +
                   "\"targetUrl\":\"https://example.com/recovery.git#main\"," +
                   "\"prerequisitePackageIds\":" + prerequisitePackageIds + "," +
                   "\"rootPackageIds\":[\"com.deucarian.recovery-test\"]," +
                   "\"state\":" + state +
                   "}";
        }

        private static void WriteRecoveryJson(
            PackageOperationStateRepository repository,
            string stepsJson,
            int rootChannel = 0,
            int schemaVersion = 2)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(repository.StatePathForTests));
            File.WriteAllText(
                repository.StatePathForTests,
                "{" +
                "\"schemaVersion\":" + schemaVersion + "," +
                "\"operationId\":\"operation-id\"," +
                "\"operationName\":\"Recovery test\"," +
                "\"registryFingerprint\":\"registry-fingerprint\"," +
                "\"rootRequests\":[{" +
                "\"packageId\":\"com.deucarian.recovery-test\"," +
                "\"channel\":" + rootChannel + "}]," +
                "\"steps\":[" + stepsJson + "]" +
                "}");
        }

        private static PackageDefinition CreatePackage(
            string displayName,
            string packageId,
            string[] dependencies = null)
        {
            return new PackageDefinition(
                displayName,
                packageId,
                "https://github.com/Deucarian/" + displayName.Replace(" ", "-") + ".git#main",
                displayName + " package.",
                dependencies ?? Array.Empty<string>(),
                PackageType.Core,
                "https://github.com/Deucarian/" + displayName.Replace(" ", "-") + ".git#develop",
                category: "Core");
        }

        private sealed class ControlledPackageInstallClient : IPackageInstallClient
        {
            public List<string> AddedUrls { get; } = new List<string>();

            public List<ControlledRequest> Requests { get; } = new List<ControlledRequest>();

            public IPackageInstallRequest Add(string packageUrl)
            {
                AddedUrls.Add(packageUrl);
                ControlledRequest request = new ControlledRequest();
                Requests.Add(request);
                return request;
            }

            public IPackageInstallRequest Remove(string packageId)
            {
                ControlledRequest request = new ControlledRequest();
                Requests.Add(request);
                return request;
            }
        }

        private sealed class ControlledRequest : IPackageInstallRequest
        {
            public bool IsCompleted { get; private set; }

            public bool IsSuccess { get; private set; }

            public string ErrorMessage { get; private set; } = string.Empty;

            public string PackageName { get; private set; } = string.Empty;

            public string PackageVersion { get; private set; } = string.Empty;

            public void CompleteSuccess(string packageName, string packageVersion)
            {
                PackageName = packageName;
                PackageVersion = packageVersion;
                IsSuccess = true;
                IsCompleted = true;
            }

            public void CompleteFailure(string errorMessage)
            {
                ErrorMessage = errorMessage;
                IsSuccess = false;
                IsCompleted = true;
            }
        }
    }
}
