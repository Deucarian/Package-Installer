using System;
using NUnit.Framework;

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
    }
}
