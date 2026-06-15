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
        public void CannotDetermineUpdateStatusLogsAsError()
        {
            PackageDefinition packageDefinition = CreatePackage();
            PackageUpdateStatus status = PackageUpdateStatus.CannotDetermine(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                string.Empty,
                "The package is installed, but Unity did not expose a Git revision for this package.");

            Assert.AreEqual(LogType.Error, PackageUpdateCheckService.GetLogType(status));
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
    }
}
