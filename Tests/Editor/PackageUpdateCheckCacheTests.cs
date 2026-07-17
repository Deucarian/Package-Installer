using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageUpdateCheckCacheTests
    {
        private string _temporaryDirectory;
        private string _cachePath;

        [SetUp]
        public void SetUp()
        {
            PackageUpdateCheckService.ResetForTests();
            PackageUpdateCheckService.SetDefaultCacheEnabledForTests(false);
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "Deucarian-PackageUpdateCheckCacheTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
            _cachePath = Path.Combine(_temporaryDirectory, PackageUpdateCheckCache.CacheFileName);
        }

        [TearDown]
        public void TearDown()
        {
            PackageUpdateCheckService.ResetForTests();
            PackageUpdateCheckService.SetDefaultCacheEnabledForTests(true);

            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, true);
            }
        }

        [Test]
        public void CacheRoundTripsTerminalStatusAndProjectSummary()
        {
            PackageDefinition package = CreatePackage();
            DateTime checkedUtc = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);
            PackageUpdateStatus status = PackageUpdateStatus.ReloadPending(
                package,
                PackageChannel.Development,
                package.DevelopmentUrl,
                "installed-revision",
                "latest-revision",
                "1.1.67",
                "1.1.68",
                "1.1.67",
                "Reload the editor to finish updating.");
            PackageUpdateCheckCache cache = new PackageUpdateCheckCache(_cachePath);

            Assert.IsTrue(cache.TryWrite(
                "manifest-signature",
                checkedUtc,
                "Checked 1 installed package; 1 needs attention.",
                "One check failed.",
                new[]
                {
                    status,
                    PackageUpdateStatus.Unknown(package, PackageChannel.Stable),
                    PackageUpdateStatus.Checking(package, PackageChannel.Stable, package.StableUrl)
                },
                out string writeError), writeError);
            Assert.IsTrue(cache.TryRead(
                "manifest-signature",
                out PackageUpdateCheckCacheSnapshot snapshot,
                out string readError), readError);

            Assert.AreEqual(checkedUtc, snapshot.LastCheckedUtc);
            Assert.AreEqual("Checked 1 installed package; 1 needs attention.", snapshot.LastStatusMessage);
            Assert.AreEqual("One check failed.", snapshot.LastFailureMessage);
            Assert.AreEqual(1, snapshot.Statuses.Count);
            PackageUpdateStatus restored = snapshot.Statuses[0];
            Assert.AreEqual(PackageUpdateStatusKind.ReloadPending, restored.Kind);
            Assert.AreEqual(package.PackageId, restored.PackageId);
            Assert.AreEqual(package.DisplayName, restored.DisplayName);
            Assert.AreEqual(PackageChannel.Development, restored.Channel);
            Assert.AreEqual(package.DevelopmentUrl, restored.SelectedUrl);
            Assert.AreEqual("installed-revision", restored.InstalledRevision);
            Assert.AreEqual("latest-revision", restored.LatestRevision);
            Assert.AreEqual("1.1.67", restored.InstalledVersion);
            Assert.AreEqual("1.1.68", restored.LatestVersion);
            Assert.AreEqual("1.1.67", restored.RunningVersion);
            Assert.AreEqual("Reload the editor to finish updating.", restored.Message);
        }

        [Test]
        public void ManifestMismatchDiscardsCache()
        {
            PackageUpdateCheckCache cache = new PackageUpdateCheckCache(_cachePath);
            Assert.IsTrue(cache.TryWrite(
                "old-signature",
                DateTime.UtcNow,
                "Complete",
                string.Empty,
                new[] { CreateUpdateAvailableStatus(CreatePackage()) },
                out string writeError), writeError);

            Assert.IsFalse(cache.TryRead(
                "new-signature",
                out PackageUpdateCheckCacheSnapshot snapshot,
                out string readError));
            Assert.IsNull(snapshot);
            Assert.IsEmpty(readError);
            Assert.IsFalse(File.Exists(_cachePath));
        }

        [TestCase("{not-json")]
        [TestCase("{\"schemaVersion\":99,\"manifestSignature\":\"manifest-signature\"}")]
        public void InvalidCacheIsIgnoredAndDeleted(string json)
        {
            File.WriteAllText(_cachePath, json);
            PackageUpdateCheckCache cache = new PackageUpdateCheckCache(_cachePath);

            Assert.IsFalse(cache.TryRead(
                "manifest-signature",
                out PackageUpdateCheckCacheSnapshot snapshot,
                out string errorMessage));
            Assert.IsNull(snapshot);
            Assert.IsNotEmpty(errorMessage);
            Assert.IsFalse(File.Exists(_cachePath));
        }

        [Test]
        public void AtomicWriteFailureLeavesCacheNonFatal()
        {
            PackageInstallerAtomicFileCommitter failingCommitter =
                new PackageInstallerAtomicFileCommitter(
                    _ => false,
                    (_, __) => throw new IOException("replace failed"),
                    (_, __) => throw new IOException("move failed"),
                    _ => { },
                    _ => { });
            PackageUpdateCheckCache cache =
                new PackageUpdateCheckCache(_cachePath, failingCommitter);

            Assert.IsFalse(cache.TryWrite(
                "manifest-signature",
                DateTime.UtcNow,
                "Complete",
                string.Empty,
                new[] { CreateUpdateAvailableStatus(CreatePackage()) },
                out string errorMessage));
            StringAssert.Contains("move failed", errorMessage);
            Assert.IsFalse(File.Exists(_cachePath));
        }

        [Test]
        public void ServiceRestoresCacheAfterStaticStateReset()
        {
            PackageDefinition package = CreatePackage();
            PackageUpdateCheckCache cache = new PackageUpdateCheckCache(_cachePath);
            PackageInstallerStateRepository stateRepository = new PackageInstallerStateRepository();
            string manifestSignature = stateRepository.GetManifestStateSignature();
            DateTime checkedUtc = new DateTime(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc);
            Assert.IsTrue(cache.TryWrite(
                manifestSignature,
                checkedUtc,
                "One update is available.",
                string.Empty,
                new[] { CreateUpdateAvailableStatus(package) },
                out string writeError), writeError);

            AssertRestoredServiceState(cache, stateRepository, package, checkedUtc);
            PackageUpdateCheckService.ResetForTests();
            AssertRestoredServiceState(cache, stateRepository, package, checkedUtc);
        }

        [Test]
        public void PreparingNewCheckPreservesLastCompletedDiskSnapshot()
        {
            PackageDefinition package = CreatePackage();
            PackageUpdateCheckCache cache = new PackageUpdateCheckCache(_cachePath);
            PackageInstallerStateRepository stateRepository = new PackageInstallerStateRepository();
            DateTime checkedUtc = new DateTime(2026, 7, 17, 9, 15, 0, DateTimeKind.Utc);
            Assert.IsTrue(cache.TryWrite(
                stateRepository.GetManifestStateSignature(),
                checkedUtc,
                "One update is available.",
                string.Empty,
                new[] { CreateUpdateAvailableStatus(package) },
                out string writeError), writeError);

            using (PackageDetectionService detection = new PackageDetectionService())
            using (PackageUpdateCheckService service = CreateService(detection, cache, stateRepository))
            {
                service.PrepareForUpdateCheck();
                Assert.IsFalse(service.HasStatuses);
            }

            PackageUpdateCheckService.ResetForTests();
            AssertRestoredServiceState(cache, stateRepository, package, checkedUtc);
        }

        [Test]
        public void ReconcilePreservesMatchingTargetAndPrunesChangedOrRemovedTargets()
        {
            PackageDefinition kept = CreatePackage("com.deucarian.kept", "Kept", "kept");
            PackageDefinition changed = CreatePackage("com.deucarian.changed", "Changed", "changed-old");
            PackageDefinition removed = CreatePackage("com.deucarian.removed", "Removed", "removed");
            PackageUpdateCheckCache cache = new PackageUpdateCheckCache(_cachePath);
            PackageInstallerStateRepository stateRepository = new PackageInstallerStateRepository();
            Assert.IsTrue(cache.TryWrite(
                stateRepository.GetManifestStateSignature(),
                DateTime.UtcNow,
                "Three checks complete.",
                string.Empty,
                new[]
                {
                    CreateUpdateAvailableStatus(kept),
                    CreateUpdateAvailableStatus(changed),
                    CreateUpdateAvailableStatus(removed)
                },
                out string writeError), writeError);

            PackageDefinition renamedKept =
                CreatePackage(kept.PackageId, "Kept Renamed", "kept");
            PackageDefinition retargetedChanged =
                CreatePackage(changed.PackageId, changed.DisplayName, "changed-new");

            using (PackageDetectionService detection = new PackageDetectionService())
            using (PackageUpdateCheckService service = CreateService(detection, cache, stateRepository))
            {
                service.ReconcileCachedStatusesForTests(
                    new[] { renamedKept, retargetedChanged },
                    _ => PackageChannel.Stable);
            }

            Assert.IsTrue(cache.TryRead(
                stateRepository.GetManifestStateSignature(),
                out PackageUpdateCheckCacheSnapshot snapshot,
                out string readError), readError);
            Assert.AreEqual(1, snapshot.Statuses.Count);
            Assert.AreEqual(kept.PackageId, snapshot.Statuses.Single().PackageId);
            Assert.AreEqual("Kept Renamed", snapshot.Statuses.Single().DisplayName);
        }

        [Test]
        public void PackageInvalidationRemovesOnlyAffectedCachedEntry()
        {
            PackageDefinition first = CreatePackage("com.deucarian.first", "First", "first");
            PackageDefinition second = CreatePackage("com.deucarian.second", "Second", "second");
            PackageUpdateCheckCache cache = new PackageUpdateCheckCache(_cachePath);
            PackageInstallerStateRepository stateRepository = new PackageInstallerStateRepository();
            Assert.IsTrue(cache.TryWrite(
                stateRepository.GetManifestStateSignature(),
                DateTime.UtcNow,
                "Two checks complete.",
                string.Empty,
                new[]
                {
                    CreateUpdateAvailableStatus(first),
                    CreateUpdateAvailableStatus(second)
                },
                out string writeError), writeError);

            using (PackageDetectionService detection = new PackageDetectionService())
            using (PackageUpdateCheckService service = CreateService(detection, cache, stateRepository))
            {
                service.Invalidate(first.PackageId);
            }

            Assert.IsTrue(cache.TryRead(
                stateRepository.GetManifestStateSignature(),
                out PackageUpdateCheckCacheSnapshot snapshot,
                out string readError), readError);
            Assert.AreEqual(1, snapshot.Statuses.Count);
            Assert.AreEqual(second.PackageId, snapshot.Statuses.Single().PackageId);
        }

        private static void AssertRestoredServiceState(
            PackageUpdateCheckCache cache,
            PackageInstallerStateRepository stateRepository,
            PackageDefinition package,
            DateTime checkedUtc)
        {
            using (PackageDetectionService detection = new PackageDetectionService())
            using (PackageUpdateCheckService service = CreateService(detection, cache, stateRepository))
            {
                PackageUpdateStatus restored = service.GetStatus(package, PackageChannel.Stable);
                Assert.AreEqual(PackageUpdateStatusKind.UpdateAvailable, restored.Kind);
                Assert.AreEqual(checkedUtc, service.LastCheckedUtc);
                Assert.AreEqual("One update is available.", service.LastStatusMessage);
            }
        }

        private static PackageUpdateCheckService CreateService(
            PackageDetectionService detection,
            PackageUpdateCheckCache cache,
            PackageInstallerStateRepository stateRepository)
        {
            return new PackageUpdateCheckService(
                detection,
                PackageRegistryRemoteFetch.FetchAsync,
                TimeSpan.FromSeconds(1),
                cache,
                stateRepository);
        }

        private static PackageUpdateStatus CreateUpdateAvailableStatus(PackageDefinition package)
        {
            return PackageUpdateStatus.UpdateAvailable(
                    package,
                    PackageChannel.Stable,
                    package.StableUrl,
                    "installed-revision",
                    "latest-revision",
                    "A newer revision is available.")
                .WithPackageVersions("1.0.0", "1.1.0");
        }

        private static PackageDefinition CreatePackage(
            string packageId = "com.deucarian.cache-test",
            string displayName = "Cache Test",
            string repositoryName = "cache-test")
        {
            return new PackageDefinition(
                displayName,
                packageId,
                "https://github.com/Deucarian/" + repositoryName + ".git#main",
                "Cache test package.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://github.com/Deucarian/" + repositoryName + ".git#develop");
        }
    }
}
