using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageUpdateCheckTests
    {
        [SetUp]
        public void SetUp()
        {
            PackageUpdateCheckService.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PackageUpdateCheckService.ResetForTests();
        }

        [Test]
        public void WindowOpenThrottleAllowsFirstCheck()
        {
            Assert.IsTrue(PackageUpdateCheckPreferences.ShouldRunThrottledCheck(
                new DateTime(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc),
                null,
                PackageUpdateCheckPreferences.WindowOpenThrottle));
        }

        [Test]
        public void WindowOpenThrottleSkipsRecentCheck()
        {
            DateTime now = new DateTime(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc);
            DateTime lastChecked = now.AddMinutes(-10);

            Assert.IsFalse(PackageUpdateCheckPreferences.ShouldRunThrottledCheck(
                now,
                lastChecked,
                PackageUpdateCheckPreferences.WindowOpenThrottle));
        }

        [Test]
        public void WindowOpenThrottleAllowsExpiredCheck()
        {
            DateTime now = new DateTime(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc);
            DateTime lastChecked = now.AddMinutes(-31);

            Assert.IsTrue(PackageUpdateCheckPreferences.ShouldRunThrottledCheck(
                now,
                lastChecked,
                PackageUpdateCheckPreferences.WindowOpenThrottle));
        }

        [Test]
        public void RevisionsMatchAcceptsExactAndShortShaMatches()
        {
            const string fullRevision = "0123456789abcdef0123456789abcdef01234567";

            Assert.IsTrue(PackageUpdateCheckService.RevisionsMatch(fullRevision, fullRevision));
            Assert.IsTrue(PackageUpdateCheckService.RevisionsMatch(fullRevision.Substring(0, 7), fullRevision));
            Assert.IsTrue(PackageUpdateCheckService.RevisionsMatch(fullRevision, fullRevision.Substring(0, 7)));
        }

        [Test]
        public void RevisionsMatchRejectsDifferentShas()
        {
            Assert.IsFalse(PackageUpdateCheckService.RevisionsMatch(
                "0123456789abcdef0123456789abcdef01234567",
                "fedcba9876543210fedcba9876543210fedcba98"));
        }

        [Test]
        public void OwnedGitProcessSeamResolvesRemoteRevision()
        {
            const string revision = "0123456789abcdef0123456789abcdef01234567";
            string receivedArguments = string.Empty;
            int receivedTimeout = 0;
            PackageUpdateCheckService.GitProcessRunnerForTests =
                (arguments, cancellationToken, timeoutMilliseconds) =>
                {
                    receivedArguments = arguments;
                    receivedTimeout = timeoutMilliseconds;
                    Assert.IsFalse(cancellationToken.IsCancellationRequested);
                    return PackageUpdateCheckService.GitProcessResult.Ok(
                        revision + "\trefs/heads/main\n");
                };

            bool found = PackageUpdateCheckService.TryGetRemoteRevisionForTests(
                "https://github.com/Deucarian/Object-Loading.git",
                "main",
                CancellationToken.None,
                out string actualRevision,
                out string message);

            Assert.IsTrue(found, message);
            Assert.AreEqual(revision, actualRevision);
            StringAssert.StartsWith("ls-remote ", receivedArguments);
            StringAssert.Contains("Object-Loading.git", receivedArguments);
            StringAssert.Contains("main", receivedArguments);
            Assert.Greater(receivedTimeout, 0);
        }

        [Test]
        public void OwnedGitProcessSeamSurfacesFailure()
        {
            PackageUpdateCheckService.GitProcessRunnerForTests =
                (_, __, ___) => PackageUpdateCheckService.GitProcessResult.Fail(
                    "Synthetic git failure.");

            bool found = PackageUpdateCheckService.TryGetRemoteRevisionForTests(
                "https://github.com/Deucarian/Object-Loading.git",
                "main",
                CancellationToken.None,
                out string revision,
                out string message);

            Assert.IsFalse(found);
            Assert.IsEmpty(revision);
            Assert.AreEqual("Synthetic git failure.", message);
        }

        [Test]
        public void OwnedGitProcessSeamPropagatesCancellation()
        {
            using (ManualResetEventSlim runnerStarted = new ManualResetEventSlim(false))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Exception observedException = null;
                PackageUpdateCheckService.GitProcessRunnerForTests =
                    (_, cancellationToken, __) =>
                    {
                        runnerStarted.Set();
                        cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                        cancellationToken.ThrowIfCancellationRequested();
                        return PackageUpdateCheckService.GitProcessResult.Ok(string.Empty);
                    };

                Task invocation = Task.Run(() =>
                {
                    try
                    {
                        PackageUpdateCheckService.TryGetRemoteRevisionForTests(
                            "https://github.com/Deucarian/Object-Loading.git",
                            "main",
                            cancellation.Token,
                            out _,
                            out _);
                    }
                    catch (Exception exception)
                    {
                        observedException = exception;
                    }
                });

                Assert.IsTrue(runnerStarted.Wait(TimeSpan.FromSeconds(2)));
                cancellation.Cancel();
                Assert.IsTrue(invocation.Wait(TimeSpan.FromSeconds(2)));
                Assert.IsInstanceOf<OperationCanceledException>(observedException);
            }
        }

        [Test]
        public void InstalledPackageWithUnknownRevisionReturnsCannotDetermineStatus()
        {
            PackageDefinition packageDefinition = CreatePackage();

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                string.Empty,
                string.Empty,
                string.Empty,
                Array.Empty<string>());

            Assert.AreEqual(PackageUpdateStatusKind.CannotDetermine, status.Kind);
            Assert.AreEqual("Cannot determine update", status.Label);
            Assert.IsFalse(status.IsUpdateAvailable);
        }

        [Test]
        public void GitPackageWithMissingRevisionReturnsNeutralCannotDetermineStatus()
        {
            PackageDefinition packageDefinition = CreatePackage();

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                string.Empty,
                string.Empty,
                string.Empty,
                PackageInstallSourceType.Git,
                string.Empty,
                Array.Empty<string>());

            Assert.AreEqual(PackageUpdateStatusKind.CannotDetermine, status.Kind);
            Assert.AreEqual(LogType.Log, PackageUpdateCheckService.GetLogType(status));
            Assert.IsFalse(PackageUpdateCheckService.TryCreateLogMessage(status, out _, out _));
        }

        [Test]
        public void InstalledPackageReferenceRevisionIsUsedForUpdateCheck()
        {
            const string revision = "0123456789abcdef0123456789abcdef01234567";
            PackageDefinition packageDefinition = CreatePackage(
                stableUrl: "https://github.com/Deucarian/Object-Loading.git#" + revision);

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                string.Empty,
                string.Empty,
                "https://github.com/Deucarian/Object-Loading.git#" + revision,
                Array.Empty<string>());

            Assert.AreEqual(PackageUpdateStatusKind.UpToDate, status.Kind);
            Assert.AreEqual(revision, status.InstalledRevision);
            Assert.AreEqual(revision, status.LatestRevision);
        }

        [Test]
        public void PackageLockRevisionReadsHashAfterNestedDependencies()
        {
            const string revision = "fedcba9876543210fedcba9876543210fedcba98";
            string packageLockPath = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + ".json");

            try
            {
                File.WriteAllText(
                    packageLockPath,
                    "{ \"dependencies\": {" +
                    "\"com.deucarian.loading-consumer\": {" +
                    "\"version\": \"https://github.com/Deucarian/Loading-Consumer.git#main\"," +
                    "\"dependencies\": { \"com.deucarian.object-loading\": \"https://github.com/Deucarian/Object-Loading.git#main\" }" +
                    "}," +
                    "\"com.deucarian.object-loading\": {" +
                    "\"version\": \"https://github.com/Deucarian/Object-Loading.git#main\"," +
                    "\"depth\": 0," +
                    "\"source\": \"git\"," +
                    "\"dependencies\": { \"com.deucarian.api\": \"https://github.com/Deucarian/API.git#main\" }," +
                    "\"hash\": \"" + revision + "\"" +
                    "}" +
                    "} }");

                bool found = PackageUpdateCheckService.TryReadPackageLockRevision(
                    packageLockPath,
                    "com.deucarian.object-loading",
                    out string actualRevision);

                Assert.IsTrue(found);
                Assert.AreEqual(revision, actualRevision);
            }
            finally
            {
                if (File.Exists(packageLockPath))
                {
                    File.Delete(packageLockPath);
                }
            }
        }

        [Test]
        public void FailedUpdateCheckStatusLogsAsError()
        {
            PackageDefinition packageDefinition = CreatePackage();
            PackageUpdateStatus status = PackageUpdateStatus.Failed(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                string.Empty,
                "Update check failed: remote revision could not be resolved.");

            Assert.AreEqual(LogType.Error, PackageUpdateCheckService.GetLogType(status));
        }

        [Test]
        public void CannotDetermineUpdateStatusLogsAsNeutral()
        {
            PackageDefinition packageDefinition = CreatePackage();
            PackageUpdateStatus status = PackageUpdateStatus.CannotDetermine(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                string.Empty,
                "The package is installed, but Unity did not expose a Git revision for this package.");

            Assert.AreEqual(LogType.Log, PackageUpdateCheckService.GetLogType(status));
            Assert.IsFalse(PackageUpdateCheckService.TryCreateLogMessage(status, out _, out _));
        }

        [Test]
        public void UpToDateStatusDoesNotLogAsError()
        {
            const string revision = "0123456789abcdef0123456789abcdef01234567";
            PackageDefinition packageDefinition = CreatePackage();
            PackageUpdateStatus status = PackageUpdateStatus.UpToDate(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                revision,
                revision);

            Assert.AreEqual(LogType.Log, PackageUpdateCheckService.GetLogType(status));
        }

        [Test]
        public void UpdateAvailableStatusDoesNotLogAsError()
        {
            PackageDefinition packageDefinition = CreatePackage();
            PackageUpdateStatus status = PackageUpdateStatus.UpdateAvailable(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                "0123456789abcdef0123456789abcdef01234567",
                "fedcba9876543210fedcba9876543210fedcba98");

            Assert.AreEqual(LogType.Log, PackageUpdateCheckService.GetLogType(status));
            Assert.IsTrue(PackageUpdateCheckService.ShouldLogStatusForTests(status));
        }

        [Test]
        public void RegistryInstalledPackageReturnsGitSourceMigration()
        {
            const string latestRevision = "fedcba9876543210fedcba9876543210fedcba98";
            PackageDefinition packageDefinition = CreatePackage(
                stableUrl: "https://github.com/Deucarian/Object-Loading.git#" + latestRevision);
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, __, revision) =>
                {
                    Assert.AreEqual(latestRevision, revision);
                    return PackageUpdateCheckService.PackageVersionResult.Ok("1.2.4");
                };

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                packageDefinition.PackageId + "@1.2.3",
                string.Empty,
                "1.2.3",
                PackageInstallSourceType.Registry,
                "1.2.3",
                Array.Empty<string>());

            Assert.AreEqual(PackageUpdateStatusKind.SourceMigrationAvailable, status.Kind);
            Assert.IsTrue(status.IsSourceMigrationAvailable);
            Assert.IsFalse(status.IsUpdateAvailable);
            Assert.AreEqual("1.2.3", status.InstalledVersion);
            Assert.AreEqual("1.2.4", status.LatestVersion);
            Assert.AreEqual(latestRevision, status.LatestRevision);
        }

        [Test]
        public void SelfGitPackageWithResolvedTargetAndOlderRunningAssemblyReturnsReloadPending()
        {
            const string revision = "0123456789abcdef0123456789abcdef01234567";
            PackageDefinition packageDefinition = CreateInstallerPackage();
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, __, ___) => PackageUpdateCheckService.PackageVersionResult.Ok("1.1.61");

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl.Replace("#main", "#" + revision),
                string.Empty,
                string.Empty,
                packageDefinition.StableUrl.Replace("#main", "#" + revision),
                PackageInstallSourceType.Git,
                "1.1.61",
                hasInstalledChannel: true,
                installedChannel: PackageChannel.Stable,
                packageLockPaths: Array.Empty<string>(),
                runningInstallerVersion: "1.1.60",
                selfUpdateSnapshot: PackageInstallerSelfUpdateSnapshot.None);

            Assert.AreEqual(PackageUpdateStatusKind.ReloadPending, status.Kind);
            Assert.IsTrue(status.IsReloadPending);
            Assert.IsFalse(status.IsUpdateAvailable);
            Assert.AreEqual("1.1.60", status.RunningVersion);
            Assert.AreEqual("1.1.61", status.InstalledVersion);
        }

        [Test]
        public void SelfGitPackageWithSameVersionAndPendingMarkerReturnsReloadPending()
        {
            const string revision = "0123456789abcdef0123456789abcdef01234567";
            PackageDefinition packageDefinition = CreateInstallerPackage();
            string selectedUrl = packageDefinition.StableUrl.Replace("#main", "#" + revision);
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, __, ___) => PackageUpdateCheckService.PackageVersionResult.Ok("1.1.61");

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Stable,
                selectedUrl,
                string.Empty,
                string.Empty,
                selectedUrl,
                PackageInstallSourceType.Git,
                "1.1.61",
                hasInstalledChannel: true,
                installedChannel: PackageChannel.Stable,
                packageLockPaths: Array.Empty<string>(),
                runningInstallerVersion: "1.1.61",
                selfUpdateSnapshot: new PackageInstallerSelfUpdateSnapshot(
                    true,
                    "1.1.61",
                    "11111111111111111111111111111111",
                    "1.1.61",
                    selectedUrl));

            Assert.AreEqual(PackageUpdateStatusKind.ReloadPending, status.Kind);
        }

        [Test]
        public void PendingSelfMarkerDoesNotHideANewerRemoteRevision()
        {
            const string installedRevision = "0123456789abcdef0123456789abcdef01234567";
            const string latestRevision = "fedcba9876543210fedcba9876543210fedcba98";
            PackageDefinition packageDefinition = CreateInstallerPackage();
            string installedUrl = packageDefinition.StableUrl.Replace("#main", "#" + installedRevision);
            string selectedUrl = packageDefinition.StableUrl.Replace("#main", "#" + latestRevision);
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, __, ___) => PackageUpdateCheckService.PackageVersionResult.Ok("1.1.62");

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Stable,
                selectedUrl,
                string.Empty,
                string.Empty,
                installedUrl,
                PackageInstallSourceType.Git,
                "1.1.61",
                hasInstalledChannel: true,
                installedChannel: PackageChannel.Stable,
                packageLockPaths: Array.Empty<string>(),
                runningInstallerVersion: "1.1.60",
                selfUpdateSnapshot: new PackageInstallerSelfUpdateSnapshot(
                    true,
                    "1.1.60",
                    "11111111111111111111111111111111",
                    "1.1.61",
                    installedUrl));

            Assert.AreEqual(PackageUpdateStatusKind.UpdateAvailable, status.Kind);
        }

        [Test]
        public void RegistryInstalledSelfReturnsGitSourceMigrationWithoutQueryingNpm()
        {
            const string latestRevision = "fedcba9876543210fedcba9876543210fedcba98";
            PackageDefinition packageDefinition = CreateInstallerPackage();
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, __, ___) => PackageUpdateCheckService.PackageVersionResult.Ok("1.1.61");

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl.Replace("#main", "#" + latestRevision),
                packageDefinition.PackageId + "@1.1.12",
                string.Empty,
                "1.1.12",
                PackageInstallSourceType.Registry,
                "1.1.12",
                hasInstalledChannel: true,
                installedChannel: PackageChannel.Stable,
                packageLockPaths: Array.Empty<string>(),
                runningInstallerVersion: "1.1.12",
                selfUpdateSnapshot: PackageInstallerSelfUpdateSnapshot.None);

            Assert.AreEqual(PackageUpdateStatusKind.SourceMigrationAvailable, status.Kind);
            Assert.IsTrue(status.IsSourceMigrationAvailable);
            Assert.IsFalse(status.IsUpdateAvailable);
            Assert.AreEqual("1.1.12", status.InstalledVersion);
            Assert.AreEqual("1.1.61", status.LatestVersion);
            Assert.AreEqual(latestRevision, status.LatestRevision);
        }

        [Test]
        public void SelfSourceMigrationHandsOffToBootstrapWithChannelFallbackUrls()
        {
            PackageDefinition installer = CreateInstallerPackage();
            PackageDefinition otherPackage = CreatePackage();

            Assert.AreEqual(
                PackageSourceMigrationAction.OpenBootstrap,
                PackageInstallerWindow.GetSourceMigrationActionForTests(installer));
            Assert.AreEqual(
                PackageSourceMigrationAction.InstallSelectedGitUrl,
                PackageInstallerWindow.GetSourceMigrationActionForTests(otherPackage));
            Assert.AreEqual(
                "Tools/Deucarian/Bootstrap/Open Bootstrapper",
                PackageInstallerWindow.BootstrapMenuPathForTests);
            Assert.AreEqual(
                "https://github.com/Deucarian/Bootstrap.git#main",
                PackageInstallerWindow.GetBootstrapGitUrlForTests(PackageChannel.Stable));
            Assert.AreEqual(
                "https://github.com/Deucarian/Bootstrap.git#develop",
                PackageInstallerWindow.GetBootstrapGitUrlForTests(PackageChannel.Development));
        }

        [Test]
        public void CompletionSummaryIncludesMigrationAndReloadPendingCounts()
        {
            PackageDefinition packageDefinition = CreateInstallerPackage();
            PackageUpdateStatus migration = PackageUpdateStatus.SourceMigrationAvailable(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                "fedcba9876543210fedcba9876543210fedcba98",
                "1.1.12",
                "1.1.61",
                "Use Bootstrap.");
            PackageUpdateStatus reloadPending = PackageUpdateStatus.ReloadPending(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                "fedcba9876543210fedcba9876543210fedcba98",
                "fedcba9876543210fedcba9876543210fedcba98",
                "1.1.61",
                "1.1.61",
                "1.1.60",
                "Retry reload.");

            Assert.AreEqual(
                "Checked for updates. 0 updates available, 1 source migration available, 1 reload pending.",
                PackageUpdateCheckService.GetCompletionSummaryForTests(new[] { migration, reloadPending }));
        }

        [Test]
        public void RegistryMigrationRemainsActionableWhenTargetMetadataFails()
        {
            PackageDefinition packageDefinition = CreatePackage();
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, __, ___) => PackageUpdateCheckService.PackageVersionResult.Fail(
                    "Target package.json was unavailable.");

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Stable,
                "not-a-git-package-reference",
                packageDefinition.PackageId + "@1.2.3",
                string.Empty,
                "1.2.3",
                PackageInstallSourceType.Registry,
                "1.2.3",
                Array.Empty<string>());

            Assert.AreEqual(PackageUpdateStatusKind.SourceMigrationAvailable, status.Kind);
            Assert.IsTrue(status.NeedsAttention);
            Assert.IsFalse(status.IsUpdateAvailable);
            StringAssert.Contains("Target metadata was unavailable", status.Message);
            StringAssert.Contains("Target package.json was unavailable", status.Message);
        }

        [Test]
        public void GitPackageSwitchResolvesTargetPackageVersion()
        {
            const string installedRevision = "0123456789abcdef0123456789abcdef01234567";
            const string latestRevision = "fedcba9876543210fedcba9876543210fedcba98";
            PackageDefinition packageDefinition = CreatePackage();
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, channel, revision) =>
                {
                    Assert.AreEqual(PackageChannel.Development, channel);
                    Assert.AreEqual(latestRevision, revision);
                    return PackageUpdateCheckService.PackageVersionResult.Ok("1.1.15");
                };

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Development,
                "https://github.com/Deucarian/Object-Loading.git#" + latestRevision,
                string.Empty,
                string.Empty,
                "https://github.com/Deucarian/Object-Loading.git#" + installedRevision,
                PackageInstallSourceType.Git,
                "1.1.14",
                hasInstalledChannel: true,
                installedChannel: PackageChannel.Stable,
                packageLockPaths: Array.Empty<string>());

            Assert.AreEqual(PackageUpdateStatusKind.SwitchAvailable, status.Kind);
            Assert.AreEqual(installedRevision, status.InstalledRevision);
            Assert.AreEqual(latestRevision, status.LatestRevision);
            Assert.AreEqual("1.1.14", status.InstalledVersion);
            Assert.AreEqual("1.1.15", status.LatestVersion);
            Assert.IsTrue(status.HasPackageVersionTransition);
            Assert.IsFalse(status.HasUnbumpedPackageVersionWarning);
        }

        [Test]
        public void GitPackageSwitchWarnsWhenTargetContentChangedWithoutVersionBump()
        {
            const string installedRevision = "0123456789abcdef0123456789abcdef01234567";
            const string latestRevision = "fedcba9876543210fedcba9876543210fedcba98";
            PackageDefinition packageDefinition = CreatePackage();
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, __, ___) => PackageUpdateCheckService.PackageVersionResult.Ok("1.1.14");

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Development,
                "https://github.com/Deucarian/Object-Loading.git#" + latestRevision,
                string.Empty,
                string.Empty,
                "https://github.com/Deucarian/Object-Loading.git#" + installedRevision,
                PackageInstallSourceType.Git,
                "1.1.14",
                hasInstalledChannel: true,
                installedChannel: PackageChannel.Stable,
                packageLockPaths: Array.Empty<string>());

            Assert.AreEqual(PackageUpdateStatusKind.SwitchAvailable, status.Kind);
            Assert.AreEqual("1.1.14", status.InstalledVersion);
            Assert.AreEqual("1.1.14", status.LatestVersion);
            Assert.IsFalse(status.HasPackageVersionTransition);
            Assert.IsTrue(status.HasUnbumpedPackageVersionWarning);
            Assert.AreEqual(
                "Switch available, but package version was not bumped.",
                status.PackageVersionWarningMessage);
        }

        [Test]
        public void RegistryMigrationReadsInstalledVersionFromPackageIdMetadata()
        {
            const string latestRevision = "fedcba9876543210fedcba9876543210fedcba98";
            PackageDefinition packageDefinition = CreatePackage(
                stableUrl: "https://github.com/Deucarian/Object-Loading.git#" + latestRevision);
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, __, ___) => PackageUpdateCheckService.PackageVersionResult.Fail("No package version metadata.");

            PackageUpdateStatus status = PackageUpdateCheckService.CheckItemForTests(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                packageDefinition.PackageId + "@1.2.3",
                string.Empty,
                string.Empty,
                PackageInstallSourceType.Registry,
                string.Empty,
                Array.Empty<string>());

            Assert.AreEqual(PackageUpdateStatusKind.SourceMigrationAvailable, status.Kind);
            Assert.AreEqual("1.2.3", status.InstalledVersion);
            Assert.AreEqual(latestRevision, status.LatestRevision);
        }

        [Test]
        public void EmptyUpdateCheckIsSilentAndDoesNotRecordFailure()
        {
            using (PackageDetectionService detectionService = new PackageDetectionService())
            using (PackageUpdateCheckService updateCheckService = new PackageUpdateCheckService(detectionService))
            {
                updateCheckService.CheckForUpdates(new[] { CreatePackage() }, _ => PackageChannel.Stable);

                Assert.IsFalse(updateCheckService.IsChecking);
                Assert.AreEqual(string.Empty, updateCheckService.LastFailureMessage);
                Assert.AreEqual(PackageUpdateStatusKind.NotInstalled, updateCheckService.GetStatus(CreatePackage(), PackageChannel.Stable).Kind);
            }
        }

        [Test]
        public void CancelCurrentUpdateCheckClearsCheckingStatusAndIgnoresStaleResult()
        {
            PackageDefinition packageDefinition = CreatePackageWithRevisionChannels();
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, __, ___) =>
                {
                    Thread.Sleep(100);
                    return PackageUpdateCheckService.PackageVersionResult.Ok("1.2.3");
                };

            using (PackageDetectionService detectionService = new PackageDetectionService())
            using (PackageUpdateCheckService updateCheckService = new PackageUpdateCheckService(detectionService))
            {
                detectionService.ReplaceInstalledPackageForTests(
                    packageDefinition.PackageId,
                    "1.2.0",
                    PackageInstallSourceType.Registry,
                    "1.2.0");

                updateCheckService.CheckForUpdates(new[] { packageDefinition }, _ => PackageChannel.Stable);

                Assert.IsTrue(updateCheckService.IsChecking);
                Assert.AreEqual(
                    PackageUpdateStatusKind.Checking,
                    updateCheckService.GetStatus(packageDefinition, PackageChannel.Stable).Kind);

                Assert.IsTrue(updateCheckService.CancelCurrentCheck());

                Assert.IsFalse(updateCheckService.IsChecking);
                Assert.AreEqual(string.Empty, updateCheckService.LastFailureMessage);
                Assert.AreEqual("Update check canceled.", updateCheckService.LastStatusMessage);
                Assert.AreEqual(
                    PackageUpdateStatusKind.Unknown,
                    updateCheckService.GetStatus(packageDefinition, PackageChannel.Stable).Kind);

                Thread.Sleep(150);
                PackageUpdateCheckService.UpdateSharedForTests();

                Assert.AreEqual("Update check canceled.", updateCheckService.LastStatusMessage);
                Assert.AreEqual(
                    PackageUpdateStatusKind.Unknown,
                    updateCheckService.GetStatus(packageDefinition, PackageChannel.Stable).Kind);
            }
        }

        [Test]
        public void FullUpdateCheckUsesAtMostFourWorkers()
        {
            int activeWorkers = 0;
            int maximumWorkers = 0;
            int resolverCalls = 0;
            PackageDefinition[] packages = Enumerable.Range(0, 8)
                .Select(index => new PackageDefinition(
                    "Package " + index,
                    "com.deucarian.test-" + index,
                    "https://github.com/Deucarian/Test-" + index + ".git#" + StableRevision,
                    "Test package."))
                .ToArray();

            PackageUpdateCheckService.GitPackageVersionResolverForTests = (_, __, ___) =>
            {
                int current = Interlocked.Increment(ref activeWorkers);
                Interlocked.Increment(ref resolverCalls);
                int observed;
                do
                {
                    observed = maximumWorkers;
                }
                while (current > observed &&
                       Interlocked.CompareExchange(ref maximumWorkers, current, observed) != observed);

                Thread.Sleep(40);
                Interlocked.Decrement(ref activeWorkers);
                return PackageUpdateCheckService.PackageVersionResult.Ok("1.2.3");
            };

            using (PackageDetectionService detectionService = new PackageDetectionService())
            using (PackageUpdateCheckService updateCheckService = new PackageUpdateCheckService(detectionService))
            {
                foreach (PackageDefinition package in packages)
                {
                    detectionService.ReplaceInstalledPackageForTests(
                        package.PackageId,
                        "1.2.0",
                        PackageInstallSourceType.Registry,
                        "1.2.0");
                }

                updateCheckService.CheckForUpdates(packages, _ => PackageChannel.Stable);
                PumpFullCheckUntilIdle(updateCheckService);
            }

            Assert.AreEqual(packages.Length, resolverCalls);
            Assert.LessOrEqual(maximumWorkers, 4);
        }

        [Test]
        public void BulkAndTargetedChecksShareOneFourWorkerBudget()
        {
            int activeWorkers = 0;
            int maximumWorkers = 0;
            int resolverCalls = 0;
            PackageDefinition[] bulkPackages = CreateConcurrentPackages("bulk", 6);
            PackageDefinition[] targetedPackages = CreateConcurrentPackages("targeted", 6);

            PackageUpdateCheckService.GitPackageVersionResolverForTests = (_, __, ___) =>
            {
                int current = Interlocked.Increment(ref activeWorkers);
                Interlocked.Increment(ref resolverCalls);
                int observed;
                do
                {
                    observed = maximumWorkers;
                }
                while (current > observed &&
                       Interlocked.CompareExchange(ref maximumWorkers, current, observed) != observed);

                Thread.Sleep(40);
                Interlocked.Decrement(ref activeWorkers);
                return PackageUpdateCheckService.PackageVersionResult.Ok("1.2.3");
            };

            using (PackageDetectionService detectionService = new PackageDetectionService())
            using (PackageUpdateCheckService updateCheckService = new PackageUpdateCheckService(detectionService))
            {
                foreach (PackageDefinition package in bulkPackages.Concat(targetedPackages))
                {
                    detectionService.ReplaceInstalledPackageForTests(
                        package.PackageId,
                        "1.2.0",
                        PackageInstallSourceType.Registry,
                        "1.2.0");
                }

                updateCheckService.CheckForUpdates(bulkPackages, _ => PackageChannel.Stable);
                foreach (PackageDefinition package in targetedPackages)
                {
                    updateCheckService.CheckForUpdate(package, PackageChannel.Stable);
                }
                PackageUpdateCheckService.UpdateTargetedChecksForTests(forceStartPending: true);

                Assert.IsTrue(updateCheckService.IsChecking);
                Assert.IsTrue(PackageUpdateCheckService.HasTargetedChecksForTests);
                PumpAllChecksUntilIdle(updateCheckService);
            }

            Assert.AreEqual(bulkPackages.Length + targetedPackages.Length, resolverCalls);
            Assert.LessOrEqual(maximumWorkers, 4);
        }

        [Test]
        public void CancelCurrentCheckCancelsBulkAndTargetedAndSuppressesLateResults()
        {
            PackageDefinition bulkPackage = CreateConcurrentPackages("cancel-bulk", 1).Single();
            PackageDefinition targetedPackage = CreateConcurrentPackages("cancel-targeted", 1).Single();
            using (ManualResetEventSlim bulkStarted = new ManualResetEventSlim(false))
            using (ManualResetEventSlim targetedStarted = new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseOldChecks = new ManualResetEventSlim(false))
            {
                PackageUpdateCheckService.GitPackageVersionResolverForTests =
                    (package, channel, _) =>
                    {
                        if (channel == PackageChannel.Development)
                        {
                            return PackageUpdateCheckService.PackageVersionResult.Ok("2.0.0-dev.1");
                        }

                        if (package.PackageId == bulkPackage.PackageId)
                        {
                            bulkStarted.Set();
                        }
                        else if (package.PackageId == targetedPackage.PackageId)
                        {
                            targetedStarted.Set();
                        }

                        releaseOldChecks.Wait(TimeSpan.FromSeconds(3));
                        return PackageUpdateCheckService.PackageVersionResult.Ok("9.9.9");
                    };

                try
                {
                    using (PackageDetectionService detectionService = new PackageDetectionService())
                    using (PackageUpdateCheckService updateCheckService =
                           new PackageUpdateCheckService(detectionService))
                    {
                        foreach (PackageDefinition package in new[] { bulkPackage, targetedPackage })
                        {
                            detectionService.ReplaceInstalledPackageForTests(
                                package.PackageId,
                                "1.2.0",
                                PackageInstallSourceType.Registry,
                                "1.2.0");
                        }

                        updateCheckService.CheckForUpdates(
                            new[] { bulkPackage },
                            _ => PackageChannel.Stable);
                        updateCheckService.CheckForUpdate(targetedPackage, PackageChannel.Stable);
                        PackageUpdateCheckService.UpdateTargetedChecksForTests(forceStartPending: true);
                        Assert.IsTrue(bulkStarted.Wait(TimeSpan.FromSeconds(2)));
                        Assert.IsTrue(targetedStarted.Wait(TimeSpan.FromSeconds(2)));

                        Assert.IsTrue(updateCheckService.CancelCurrentCheck());
                        Assert.IsFalse(updateCheckService.IsChecking);
                        Assert.IsFalse(PackageUpdateCheckService.HasTargetedChecksForTests);
                        Assert.AreEqual(
                            PackageUpdateStatusKind.Unknown,
                            updateCheckService.GetStatus(bulkPackage, PackageChannel.Stable).Kind);
                        Assert.AreEqual(
                            PackageUpdateStatusKind.Unknown,
                            updateCheckService.GetStatus(targetedPackage, PackageChannel.Stable).Kind);

                        updateCheckService.CheckForUpdate(
                            targetedPackage,
                            PackageChannel.Development);
                        PumpTargetedChecksUntilIdle();
                        PackageUpdateStatus newerStatus = updateCheckService.GetStatus(
                            targetedPackage,
                            PackageChannel.Development);
                        Assert.AreEqual(
                            PackageUpdateStatusKind.SourceMigrationAvailable,
                            newerStatus.Kind);
                        Assert.AreEqual("2.0.0-dev.1", newerStatus.LatestVersion);

                        releaseOldChecks.Set();
                        Thread.Sleep(75);
                        PackageUpdateCheckService.UpdateSharedForTests();
                        PackageUpdateCheckService.UpdateTargetedChecksForTests(forceStartPending: true);

                        PackageUpdateStatus finalStatus = updateCheckService.GetStatus(
                            targetedPackage,
                            PackageChannel.Development);
                        Assert.AreEqual(
                            PackageUpdateStatusKind.SourceMigrationAvailable,
                            finalStatus.Kind);
                        Assert.AreEqual("2.0.0-dev.1", finalStatus.LatestVersion);
                    }
                }
                finally
                {
                    releaseOldChecks.Set();
                }
            }
        }

        [Test]
        public void InvalidateAllDetachesBulkCheckAndSuppressesItsLateCompletion()
        {
            PackageDefinition package = CreatePackageWithRevisionChannels();
            DateTime? previousLastCheckedUtc = PackageUpdateCheckPreferences.LastCheckedUtc;
            DateTime sentinelLastCheckedUtc = new DateTime(
                2026,
                1,
                2,
                3,
                4,
                5,
                DateTimeKind.Utc);
            using (ManualResetEventSlim oldCheckStarted = new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseOldCheck = new ManualResetEventSlim(false))
            using (ManualResetEventSlim oldCheckCompleted = new ManualResetEventSlim(false))
            {
                PackageUpdateCheckPreferences.LastCheckedUtc = sentinelLastCheckedUtc;
                PackageInstallerActivityService.ClearForTests();
                PackageUpdateCheckService.GitPackageVersionResolverForTests = (_, channel, __) =>
                {
                    if (channel == PackageChannel.Stable)
                    {
                        oldCheckStarted.Set();
                        releaseOldCheck.Wait(TimeSpan.FromSeconds(3));
                        oldCheckCompleted.Set();
                        return PackageUpdateCheckService.PackageVersionResult.Ok("9.9.9");
                    }

                    return PackageUpdateCheckService.PackageVersionResult.Ok("1.2.4-dev.7");
                };

                try
                {
                    using (PackageDetectionService detectionService = new PackageDetectionService())
                    using (PackageUpdateCheckService updateCheckService =
                           new PackageUpdateCheckService(detectionService))
                    {
                        detectionService.ReplaceInstalledPackageForTests(
                            package.PackageId,
                            "1.2.3",
                            PackageInstallSourceType.Registry,
                            "1.2.3");

                        updateCheckService.CheckForUpdates(
                            new[] { package },
                            _ => PackageChannel.Stable);
                        Assert.IsTrue(oldCheckStarted.Wait(TimeSpan.FromSeconds(2)));

                        updateCheckService.InvalidateAll();

                        Assert.IsFalse(updateCheckService.IsChecking);
                        Assert.AreEqual(0, PackageInstallerActivityService.Recent.Count);
                        Assert.AreEqual(
                            sentinelLastCheckedUtc,
                            PackageUpdateCheckPreferences.LastCheckedUtc);

                        updateCheckService.CheckForUpdates(
                            new[] { package },
                            _ => PackageChannel.Development);
                        Assert.IsTrue(updateCheckService.IsChecking);
                        PumpFullCheckUntilIdle(updateCheckService);

                        Assert.AreEqual(1, PackageInstallerActivityService.Recent.Count);
                        DateTime? acceptedLastCheckedUtc =
                            PackageUpdateCheckPreferences.LastCheckedUtc;
                        Assert.IsTrue(acceptedLastCheckedUtc.HasValue);
                        Assert.AreNotEqual(sentinelLastCheckedUtc, acceptedLastCheckedUtc.Value);

                        releaseOldCheck.Set();
                        Assert.IsTrue(oldCheckCompleted.Wait(TimeSpan.FromSeconds(2)));
                        Thread.Sleep(50);
                        PackageUpdateCheckService.UpdateSharedForTests();

                        Assert.AreEqual(1, PackageInstallerActivityService.Recent.Count);
                        Assert.AreEqual(
                            acceptedLastCheckedUtc,
                            PackageUpdateCheckPreferences.LastCheckedUtc);
                        PackageUpdateStatus finalStatus = updateCheckService.GetStatus(
                            package,
                            PackageChannel.Development);
                        Assert.AreEqual(
                            PackageUpdateStatusKind.SourceMigrationAvailable,
                            finalStatus.Kind);
                        Assert.AreEqual("1.2.4-dev.7", finalStatus.LatestVersion);
                    }
                }
                finally
                {
                    releaseOldCheck.Set();
                    PackageUpdateCheckPreferences.LastCheckedUtc = previousLastCheckedUtc;
                    PackageInstallerActivityService.ClearForTests();
                }
            }
        }

        [Test]
        public void NewTargetedIntentIsNotOverwrittenByOlderBulkFinalization()
        {
            PackageDefinition package = CreatePackageWithRevisionChannels();
            using (ManualResetEventSlim stableStarted = new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseStable = new ManualResetEventSlim(false))
            {
                PackageUpdateCheckService.GitPackageVersionResolverForTests = (_, channel, __) =>
                {
                    if (channel == PackageChannel.Stable)
                    {
                        stableStarted.Set();
                        releaseStable.Wait(TimeSpan.FromSeconds(3));
                        return PackageUpdateCheckService.PackageVersionResult.Ok("9.9.9");
                    }

                    return PackageUpdateCheckService.PackageVersionResult.Ok("1.2.4-dev.7");
                };

                try
                {
                    using (PackageDetectionService detectionService = new PackageDetectionService())
                    using (PackageUpdateCheckService updateCheckService =
                           new PackageUpdateCheckService(detectionService))
                    {
                        detectionService.ReplaceInstalledPackageForTests(
                            package.PackageId,
                            "1.2.0",
                            PackageInstallSourceType.Registry,
                            "1.2.0");

                        updateCheckService.CheckForUpdates(
                            new[] { package },
                            _ => PackageChannel.Stable);
                        Assert.IsTrue(stableStarted.Wait(TimeSpan.FromSeconds(2)));

                        updateCheckService.CheckForUpdate(package, PackageChannel.Development);
                        PumpTargetedChecksUntilIdle();
                        Assert.AreEqual(
                            "1.2.4-dev.7",
                            updateCheckService.GetStatus(
                                package,
                                PackageChannel.Development).LatestVersion);

                        releaseStable.Set();
                        PumpFullCheckUntilIdle(updateCheckService);

                        PackageUpdateStatus status = updateCheckService.GetStatus(
                            package,
                            PackageChannel.Development);
                        Assert.AreEqual(PackageUpdateStatusKind.SourceMigrationAvailable, status.Kind);
                        Assert.AreEqual("1.2.4-dev.7", status.LatestVersion);
                    }
                }
                finally
                {
                    releaseStable.Set();
                }
            }
        }

        [Test]
        public void NewBulkIntentIsNotOverwrittenByOlderTargetedResult()
        {
            PackageDefinition package = CreatePackageWithRevisionChannels();
            using (ManualResetEventSlim stableStarted = new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseStable = new ManualResetEventSlim(false))
            {
                PackageUpdateCheckService.GitPackageVersionResolverForTests = (_, channel, __) =>
                {
                    if (channel == PackageChannel.Stable)
                    {
                        stableStarted.Set();
                        releaseStable.Wait(TimeSpan.FromSeconds(3));
                        return PackageUpdateCheckService.PackageVersionResult.Ok("9.9.9");
                    }

                    return PackageUpdateCheckService.PackageVersionResult.Ok("1.2.4-dev.7");
                };

                try
                {
                    using (PackageDetectionService detectionService = new PackageDetectionService())
                    using (PackageUpdateCheckService updateCheckService =
                           new PackageUpdateCheckService(detectionService))
                    {
                        detectionService.ReplaceInstalledPackageForTests(
                            package.PackageId,
                            "1.2.0",
                            PackageInstallSourceType.Registry,
                            "1.2.0");

                        updateCheckService.CheckForUpdate(package, PackageChannel.Stable);
                        PackageUpdateCheckService.UpdateTargetedChecksForTests(forceStartPending: true);
                        Assert.IsTrue(stableStarted.Wait(TimeSpan.FromSeconds(2)));

                        updateCheckService.CheckForUpdates(
                            new[] { package },
                            _ => PackageChannel.Development);
                        PumpFullCheckUntilIdle(updateCheckService);
                        Assert.AreEqual(
                            "1.2.4-dev.7",
                            updateCheckService.GetStatus(
                                package,
                                PackageChannel.Development).LatestVersion);

                        releaseStable.Set();
                        PumpTargetedChecksUntilIdle();

                        PackageUpdateStatus status = updateCheckService.GetStatus(
                            package,
                            PackageChannel.Development);
                        Assert.AreEqual(PackageUpdateStatusKind.SourceMigrationAvailable, status.Kind);
                        Assert.AreEqual("1.2.4-dev.7", status.LatestVersion);
                    }
                }
                finally
                {
                    releaseStable.Set();
                }
            }
        }

        [Test]
        public void SequentialBulkRefreshDoesNotReuseCompletedManifestResult()
        {
            PackageDefinition package = CreatePackageWithRevisionChannels();
            int resolverCalls = 0;
            PackageUpdateCheckService.GitPackageVersionResolverForTests = (_, __, ___) =>
                PackageUpdateCheckService.PackageVersionResult.Ok(
                    Interlocked.Increment(ref resolverCalls) == 1 ? "1.2.3" : "1.2.4");

            using (PackageDetectionService detectionService = new PackageDetectionService())
            using (PackageUpdateCheckService updateCheckService = new PackageUpdateCheckService(detectionService))
            {
                detectionService.ReplaceInstalledPackageForTests(
                    package.PackageId,
                    "1.2.0",
                    PackageInstallSourceType.Registry,
                    "1.2.0");

                updateCheckService.CheckForUpdates(new[] { package }, _ => PackageChannel.Stable);
                PumpFullCheckUntilIdle(updateCheckService);
                Assert.AreEqual("1.2.3", updateCheckService.GetStatus(package, PackageChannel.Stable).LatestVersion);

                updateCheckService.CheckForUpdates(new[] { package }, _ => PackageChannel.Stable);
                PumpFullCheckUntilIdle(updateCheckService);

                Assert.AreEqual(2, resolverCalls);
                Assert.AreEqual("1.2.4", updateCheckService.GetStatus(package, PackageChannel.Stable).LatestVersion);
            }
        }

        [Test]
        public void FullUpdateCheckDeduplicatesEquivalentManifestProbe()
        {
            int resolverCalls = 0;
            PackageDefinition first = new PackageDefinition(
                "First",
                "com.deucarian.first",
                "https://github.com/Deucarian/Shared.git#" + StableRevision,
                "First package.");
            PackageDefinition second = new PackageDefinition(
                "Second",
                "com.deucarian.second",
                first.StableUrl,
                "Second package.");
            PackageUpdateCheckService.GitPackageVersionResolverForTests = (_, __, ___) =>
            {
                Interlocked.Increment(ref resolverCalls);
                Thread.Sleep(25);
                return PackageUpdateCheckService.PackageVersionResult.Ok("1.2.3");
            };

            using (PackageDetectionService detectionService = new PackageDetectionService())
            using (PackageUpdateCheckService updateCheckService = new PackageUpdateCheckService(detectionService))
            {
                detectionService.ReplaceInstalledPackageForTests(
                    first.PackageId,
                    "1.2.0",
                    PackageInstallSourceType.Registry,
                    "1.2.0");
                detectionService.ReplaceInstalledPackageForTests(
                    second.PackageId,
                    "1.2.0",
                    PackageInstallSourceType.Registry,
                    "1.2.0");

                updateCheckService.CheckForUpdates(new[] { first, second }, _ => PackageChannel.Stable);
                PumpFullCheckUntilIdle(updateCheckService);
            }

            Assert.AreEqual(1, resolverCalls);
        }

        [Test]
        public void TargetedStableToDevelopmentCheckMarksCheckingThenMigrationAvailable()
        {
            PackageDefinition packageDefinition = CreatePackageWithRevisionChannels();
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, channel, __) => PackageUpdateCheckService.PackageVersionResult.Ok(
                    channel == PackageChannel.Development ? "1.2.4-dev.7" : "1.2.3");

            using (PackageDetectionService detectionService = new PackageDetectionService())
            using (PackageUpdateCheckService updateCheckService = new PackageUpdateCheckService(detectionService))
            {
                detectionService.ReplaceInstalledPackageForTests(
                    packageDefinition.PackageId,
                    "1.2.3",
                    PackageInstallSourceType.Registry,
                    "1.2.3");

                updateCheckService.CheckForUpdate(packageDefinition, PackageChannel.Development);

                Assert.AreEqual(
                    PackageUpdateStatusKind.Checking,
                    updateCheckService.GetStatus(packageDefinition, PackageChannel.Development).Kind);

                PumpTargetedChecksUntilIdle();

                PackageUpdateStatus status = updateCheckService.GetStatus(
                    packageDefinition,
                    PackageChannel.Development);
                Assert.AreEqual(PackageUpdateStatusKind.SourceMigrationAvailable, status.Kind);
                Assert.AreEqual(DevelopmentRevision, status.LatestRevision);
                Assert.AreEqual("1.2.4-dev.7", status.LatestVersion);
            }
        }

        [Test]
        public void TargetedDevelopmentToStableCheckMarksCheckingThenMigrationAvailable()
        {
            PackageDefinition packageDefinition = CreatePackageWithRevisionChannels();
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, channel, __) => PackageUpdateCheckService.PackageVersionResult.Ok(
                    channel == PackageChannel.Stable ? "1.2.3" : "1.2.4-dev.7");

            using (PackageDetectionService detectionService = new PackageDetectionService())
            using (PackageUpdateCheckService updateCheckService = new PackageUpdateCheckService(detectionService))
            {
                detectionService.ReplaceInstalledPackageForTests(
                    packageDefinition.PackageId,
                    "1.2.4-dev.7",
                    PackageInstallSourceType.Registry,
                    "1.2.4-dev.7");

                updateCheckService.CheckForUpdate(packageDefinition, PackageChannel.Stable);

                Assert.AreEqual(
                    PackageUpdateStatusKind.Checking,
                    updateCheckService.GetStatus(packageDefinition, PackageChannel.Stable).Kind);

                PumpTargetedChecksUntilIdle();

                PackageUpdateStatus status = updateCheckService.GetStatus(
                    packageDefinition,
                    PackageChannel.Stable);
                Assert.AreEqual(PackageUpdateStatusKind.SourceMigrationAvailable, status.Kind);
                Assert.AreEqual(StableRevision, status.LatestRevision);
                Assert.AreEqual("1.2.3", status.LatestVersion);
            }
        }

        [Test]
        public void TargetedChannelCheckForNotInstalledPackageDoesNotStartAsyncCheck()
        {
            PackageDefinition packageDefinition = CreatePackage();

            using (PackageDetectionService detectionService = new PackageDetectionService())
            using (PackageUpdateCheckService updateCheckService = new PackageUpdateCheckService(detectionService))
            {
                updateCheckService.CheckForUpdate(packageDefinition, PackageChannel.Development);

                Assert.IsFalse(PackageUpdateCheckService.HasTargetedChecksForTests);
                Assert.AreEqual(
                    PackageUpdateStatusKind.NotInstalled,
                    updateCheckService.GetStatus(packageDefinition, PackageChannel.Development).Kind);
            }
        }

        [Test]
        public void StaleTargetedCheckResultDoesNotOverwriteNewerChannelSelection()
        {
            PackageDefinition packageDefinition = CreatePackageWithRevisionChannels();
            PackageUpdateCheckService.GitPackageVersionResolverForTests =
                (_, channel, __) =>
                {
                    if (channel == PackageChannel.Stable)
                    {
                        Thread.Sleep(100);
                        return PackageUpdateCheckService.PackageVersionResult.Ok("1.2.3");
                    }

                    return PackageUpdateCheckService.PackageVersionResult.Ok("1.2.4-dev.7");
                };

            using (PackageDetectionService detectionService = new PackageDetectionService())
            using (PackageUpdateCheckService updateCheckService = new PackageUpdateCheckService(detectionService))
            {
                detectionService.ReplaceInstalledPackageForTests(
                    packageDefinition.PackageId,
                    "1.2.0",
                    PackageInstallSourceType.Registry,
                    "1.2.0");

                updateCheckService.CheckForUpdate(packageDefinition, PackageChannel.Stable);
                PackageUpdateCheckService.UpdateTargetedChecksForTests(forceStartPending: true);
                updateCheckService.CheckForUpdate(packageDefinition, PackageChannel.Development);

                PumpTargetedChecksUntilIdle();

                PackageUpdateStatus status = updateCheckService.GetStatus(
                    packageDefinition,
                    PackageChannel.Development);
                Assert.AreEqual(PackageUpdateStatusKind.SourceMigrationAvailable, status.Kind);
                Assert.AreEqual(DevelopmentRevision, status.LatestRevision);
                Assert.AreEqual("1.2.4-dev.7", status.LatestVersion);
            }
        }

        private const string StableRevision = "0123456789abcdef0123456789abcdef01234567";
        private const string DevelopmentRevision = "fedcba9876543210fedcba9876543210fedcba98";

        private static PackageDefinition CreatePackage(
            string stableUrl = "https://github.com/Deucarian/Object-Loading.git#main")
        {
            return new PackageDefinition(
                "Deucarian Object Loading",
                "com.deucarian.object-loading",
                stableUrl,
                "Reusable runtime loading pipeline.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://github.com/Deucarian/Object-Loading.git#develop",
                category: "Core");
        }

        private static PackageDefinition CreateInstallerPackage()
        {
            return new PackageDefinition(
                "Deucarian Package Installer",
                PackageInstallerRuntimeIdentity.PackageId,
                "https://github.com/Deucarian/Package-Installer.git#main",
                "Package management.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://github.com/Deucarian/Package-Installer.git#develop",
                category: "Tools");
        }

        private static PackageDefinition CreatePackageWithRevisionChannels()
        {
            return new PackageDefinition(
                "Deucarian Object Loading",
                "com.deucarian.object-loading",
                "https://github.com/Deucarian/Object-Loading.git#" + StableRevision,
                "Reusable runtime loading pipeline.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://github.com/Deucarian/Object-Loading.git#" + DevelopmentRevision,
                category: "Core");
        }

        private static PackageDefinition[] CreateConcurrentPackages(string prefix, int count)
        {
            return Enumerable.Range(0, count)
                .Select(index => new PackageDefinition(
                    prefix + " " + index,
                    "com.deucarian." + prefix + "-" + index,
                    "https://github.com/Deucarian/" + prefix + "-" + index +
                    ".git#" + StableRevision,
                    "Concurrent update-check package.",
                    Array.Empty<string>(),
                    PackageType.Core,
                    "https://github.com/Deucarian/" + prefix + "-" + index +
                    ".git#" + DevelopmentRevision,
                    category: "Core"))
                .ToArray();
        }

        private static void PumpTargetedChecksUntilIdle()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (PackageUpdateCheckService.HasTargetedChecksForTests && DateTime.UtcNow < deadline)
            {
                PackageUpdateCheckService.UpdateTargetedChecksForTests(forceStartPending: true);
                Thread.Sleep(10);
            }

            Assert.IsFalse(PackageUpdateCheckService.HasTargetedChecksForTests);
        }

        private static void PumpFullCheckUntilIdle(PackageUpdateCheckService service)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (service.IsChecking && DateTime.UtcNow < deadline)
            {
                PackageUpdateCheckService.UpdateSharedForTests();
                Thread.Sleep(10);
            }

            PackageUpdateCheckService.UpdateSharedForTests();
            Assert.IsFalse(service.IsChecking);
        }

        private static void PumpAllChecksUntilIdle(PackageUpdateCheckService service)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while ((service.IsChecking || PackageUpdateCheckService.HasTargetedChecksForTests) &&
                   DateTime.UtcNow < deadline)
            {
                PackageUpdateCheckService.UpdateSharedForTests();
                PackageUpdateCheckService.UpdateTargetedChecksForTests(forceStartPending: true);
                Thread.Sleep(10);
            }

            PackageUpdateCheckService.UpdateSharedForTests();
            PackageUpdateCheckService.UpdateTargetedChecksForTests(forceStartPending: true);
            Assert.IsFalse(service.IsChecking);
            Assert.IsFalse(PackageUpdateCheckService.HasTargetedChecksForTests);
        }
    }
}
