using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageUpdateCheckTests
    {
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
        }

        [Test]
        public void RegistryPackageInstalledAtLatestVersionReturnsUpToDate()
        {
            PackageDefinition packageDefinition = CreatePackage();

            PackageUpdateStatus status = CheckRegistryPackageForTests(
                packageDefinition,
                installedVersion: "1.2.3",
                latestVersion: "1.2.3");

            Assert.AreEqual(PackageUpdateStatusKind.UpToDate, status.Kind);
            Assert.AreEqual("1.2.3", status.InstalledRevision);
            Assert.AreEqual("1.2.3", status.LatestRevision);
            Assert.IsFalse(status.IsUpdateAvailable);
        }

        [Test]
        public void RegistryPackageInstalledBelowLatestVersionReturnsUpdateAvailable()
        {
            PackageDefinition packageDefinition = CreatePackage();

            PackageUpdateStatus status = CheckRegistryPackageForTests(
                packageDefinition,
                installedVersion: "1.2.2",
                latestVersion: "1.2.3");

            Assert.AreEqual(PackageUpdateStatusKind.UpdateAvailable, status.Kind);
            Assert.AreEqual("1.2.2", status.InstalledRevision);
            Assert.AreEqual("1.2.3", status.LatestRevision);
            Assert.IsTrue(status.IsUpdateAvailable);
        }

        [Test]
        public void RegistryPackageWithoutLatestVersionReturnsCannotDetermine()
        {
            PackageDefinition packageDefinition = CreatePackage();

            PackageUpdateStatus status = CheckRegistryPackageForTests(
                packageDefinition,
                installedVersion: "1.2.3",
                latestVersion: string.Empty);

            Assert.AreEqual(PackageUpdateStatusKind.CannotDetermine, status.Kind);
            Assert.AreEqual(LogType.Log, PackageUpdateCheckService.GetLogType(status));
            Assert.IsFalse(status.IsUpdateAvailable);
        }

        [Test]
        public void RegistryPackageVersionCanBeReadFromPackageIdMetadata()
        {
            PackageDefinition packageDefinition = CreatePackage();
            PackageUpdateCheckService.RegistryLatestVersionResolverForTests =
                _ => PackageUpdateCheckService.RegistryLatestVersionResult.Ok("1.2.3");

            try
            {
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

                Assert.AreEqual(PackageUpdateStatusKind.UpToDate, status.Kind);
                Assert.AreEqual("1.2.3", status.InstalledRevision);
            }
            finally
            {
                PackageUpdateCheckService.RegistryLatestVersionResolverForTests = null;
            }
        }

        [Test]
        public void NpmLatestDistTagCanBeReadFromRegistryMetadata()
        {
            bool found = PackageUpdateCheckService.TryReadNpmLatestVersion(
                "{\"dist-tags\":{\"dev\":\"1.2.4-dev.7\",\"latest\":\"1.2.3\"}}",
                out string latestVersion);

            Assert.IsTrue(found);
            Assert.AreEqual("1.2.3", latestVersion);
        }

        [Test]
        public void SemanticVersionComparisonDetectsOlderInstalledVersion()
        {
            bool compared = PackageUpdateCheckService.TryCompareSemanticVersions(
                "1.2.2",
                "1.2.3",
                out int comparison,
                out string message);

            Assert.IsTrue(compared, message);
            Assert.Less(comparison, 0);
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

        private static PackageUpdateStatus CheckRegistryPackageForTests(
            PackageDefinition packageDefinition,
            string installedVersion,
            string latestVersion)
        {
            PackageUpdateCheckService.RegistryLatestVersionResolverForTests =
                string.IsNullOrWhiteSpace(latestVersion)
                    ? _ => PackageUpdateCheckService.RegistryLatestVersionResult.Fail("Could not fetch latest npmjs version.")
                    : _ => PackageUpdateCheckService.RegistryLatestVersionResult.Ok(latestVersion);

            try
            {
                return PackageUpdateCheckService.CheckItemForTests(
                    packageDefinition,
                    PackageChannel.Stable,
                    packageDefinition.StableUrl,
                    packageDefinition.PackageId + "@" + installedVersion,
                    string.Empty,
                    installedVersion,
                    PackageInstallSourceType.Registry,
                    installedVersion,
                    Array.Empty<string>());
            }
            finally
            {
                PackageUpdateCheckService.RegistryLatestVersionResolverForTests = null;
            }
        }
    }
}
