using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageInstallerAtomicFileCommitterTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "Deucarian.PackageInstaller.AtomicFileCommitterTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_temporaryRoot))
            {
                DeleteDirectoryWithRetries(_temporaryRoot);
            }
        }

        [Test]
        public void Commit_TransientReplaceFailureSucceedsOnSecondAttempt()
        {
            int replaceCalls = 0;
            List<int> delays = new List<int>();
            PackageInstallerAtomicFileCommitter committer =
                new PackageInstallerAtomicFileCommitter(
                    path => true,
                    (source, destination) =>
                    {
                        replaceCalls++;
                        if (replaceCalls == 1)
                        {
                            throw new IOException("Transient replace failure.");
                        }
                    },
                    (source, destination) => Assert.Fail("Move should not be selected."),
                    delays.Add);

            committer.Commit("temporary", "destination");

            Assert.AreEqual(2, replaceCalls);
            CollectionAssert.AreEqual(new[] { 10 }, delays);
        }

        [Test]
        public void Commit_PersistentReplaceFailureExhaustsRetriesAndRethrows()
        {
            int replaceCalls = 0;
            List<int> delays = new List<int>();
            PackageInstallerAtomicFileCommitter committer =
                new PackageInstallerAtomicFileCommitter(
                    path => true,
                    (source, destination) =>
                    {
                        replaceCalls++;
                        throw new IOException("Persistent replace failure.");
                    },
                    (source, destination) => Assert.Fail("Move should not be selected."),
                    delays.Add);

            IOException exception = Assert.Throws<IOException>(() =>
                committer.Commit("temporary", "destination"));

            Assert.AreEqual("Persistent replace failure.", exception.Message);
            Assert.AreEqual(4, replaceCalls);
            CollectionAssert.AreEqual(new[] { 10, 25, 50 }, delays);
        }

        [Test]
        public void Commit_MoveFailureRechecksDestinationAndUsesReplaceOnRetry()
        {
            bool destinationExists = false;
            int moveCalls = 0;
            int replaceCalls = 0;
            List<int> delays = new List<int>();
            PackageInstallerAtomicFileCommitter committer =
                new PackageInstallerAtomicFileCommitter(
                    path => destinationExists,
                    (source, destination) => replaceCalls++,
                    (source, destination) =>
                    {
                        moveCalls++;
                        destinationExists = true;
                        throw new IOException("Destination appeared during move.");
                    },
                    delays.Add);

            committer.Commit("temporary", "destination");

            Assert.AreEqual(1, moveCalls);
            Assert.AreEqual(1, replaceCalls);
            CollectionAssert.AreEqual(new[] { 10 }, delays);
        }

        [Test]
        public void Commit_UnauthorizedAccessFailsFastWithoutDelay()
        {
            int replaceCalls = 0;
            List<int> delays = new List<int>();
            PackageInstallerAtomicFileCommitter committer =
                new PackageInstallerAtomicFileCommitter(
                    path => true,
                    (source, destination) =>
                    {
                        replaceCalls++;
                        throw new UnauthorizedAccessException("Denied.");
                    },
                    (source, destination) => Assert.Fail("Move should not be selected."),
                    delays.Add);

            UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
                committer.Commit("temporary", "destination"));

            Assert.AreEqual("Denied.", exception.Message);
            Assert.AreEqual(1, replaceCalls);
            CollectionAssert.IsEmpty(delays);
        }

        [Test]
        public void Commit_CancellationBeforeRetryPreventsSecondReplace()
        {
            bool cancellationRequested = false;
            int replaceCalls = 0;
            List<int> delays = new List<int>();
            PackageInstallerAtomicFileCommitter committer =
                new PackageInstallerAtomicFileCommitter(
                    path => true,
                    (source, destination) =>
                    {
                        replaceCalls++;
                        cancellationRequested = true;
                        throw new IOException("Transient replace failure.");
                    },
                    (source, destination) => Assert.Fail("Move should not be selected."),
                    delays.Add);

            Assert.Throws<OperationCanceledException>(() => committer.Commit(
                "temporary",
                "destination",
                () =>
                {
                    if (cancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }
                }));

            Assert.AreEqual(1, replaceCalls);
            CollectionAssert.IsEmpty(delays);
        }

        [Test]
        public void Delete_TransientFailureSucceedsOnSecondAttempt()
        {
            int deleteCalls = 0;
            List<int> delays = new List<int>();
            PackageInstallerAtomicFileCommitter committer =
                new PackageInstallerAtomicFileCommitter(
                    path => true,
                    (source, destination) => Assert.Fail("Replace should not be selected."),
                    (source, destination) => Assert.Fail("Move should not be selected."),
                    path =>
                    {
                        deleteCalls++;
                        if (deleteCalls == 1)
                        {
                            throw new IOException("Transient delete failure.");
                        }
                    },
                    delays.Add);

            committer.Delete("destination");

            Assert.AreEqual(2, deleteCalls);
            CollectionAssert.AreEqual(new[] { 10 }, delays);
        }

        [Test]
        public void Delete_PersistentFailureExhaustsRetriesAndRethrows()
        {
            int deleteCalls = 0;
            List<int> delays = new List<int>();
            PackageInstallerAtomicFileCommitter committer =
                new PackageInstallerAtomicFileCommitter(
                    path => true,
                    (source, destination) => Assert.Fail("Replace should not be selected."),
                    (source, destination) => Assert.Fail("Move should not be selected."),
                    path =>
                    {
                        deleteCalls++;
                        throw new IOException("Persistent delete failure.");
                    },
                    delays.Add);

            IOException exception = Assert.Throws<IOException>(() =>
                committer.Delete("destination"));

            Assert.AreEqual("Persistent delete failure.", exception.Message);
            Assert.AreEqual(4, deleteCalls);
            CollectionAssert.AreEqual(new[] { 10, 25, 50 }, delays);
        }

        [Test]
        public void RecoveryRepository_PersistentCommitFailurePreservesRecordAndCleansTempFile()
        {
            PackageOperationStateRepository seedRepository =
                new PackageOperationStateRepository(_temporaryRoot);
            Assert.IsTrue(
                seedRepository.Save(
                    CreateRecoveryRecord("https://example.com/seed.git#main"),
                    out string seedError),
                seedError);
            string statePath = seedRepository.StatePathForTests;
            string seededBytes = File.ReadAllText(statePath);
            List<int> delays = new List<int>();
            PackageInstallerAtomicFileCommitter failingCommitter =
                new PackageInstallerAtomicFileCommitter(
                    File.Exists,
                    (source, destination) =>
                    {
                        throw new IOException("Destination is temporarily locked.");
                    },
                    File.Move,
                    delays.Add);
            PackageOperationStateRepository repository =
                new PackageOperationStateRepository(_temporaryRoot, failingCommitter);

            Assert.IsFalse(repository.Save(
                CreateRecoveryRecord("https://example.com/replacement.git#main"),
                out string errorMessage));

            StringAssert.Contains("temporarily locked", errorMessage);
            Assert.AreEqual(seededBytes, File.ReadAllText(statePath));
            CollectionAssert.AreEqual(new[] { 10, 25, 50 }, delays);
            CollectionAssert.IsEmpty(
                Directory.GetFiles(Path.GetDirectoryName(statePath), "*.tmp"));
        }

        [Test]
        public void RegistryCache_CancellationDuringRetryPreservesCacheAndCleansTempFile()
        {
            string cachePath = Path.Combine(_temporaryRoot, PackageRegistryCache.CacheFileName);
            PackageRegistryCache seedCache = new PackageRegistryCache(cachePath);
            Assert.IsTrue(seedCache.TryWrite(
                "{\"schemaVersion\":1,\"packages\":[]}",
                "https://example.com/registry.json",
                "seed-etag",
                DateTimeOffset.UtcNow,
                "2026-07-14T00:00:00Z",
                out string seedError), seedError);
            string seededBytes = File.ReadAllText(cachePath);
            CancellationTokenSource cancellation = new CancellationTokenSource();
            List<int> delays = new List<int>();
            PackageInstallerAtomicFileCommitter cancelingCommitter =
                new PackageInstallerAtomicFileCommitter(
                    File.Exists,
                    (source, destination) =>
                    {
                        cancellation.Cancel();
                        throw new IOException("Destination is temporarily locked.");
                    },
                    File.Move,
                    delays.Add);
            PackageRegistryCache cache = new PackageRegistryCache(cachePath, cancelingCommitter);

            try
            {
                Assert.Throws<OperationCanceledException>(() => cache.TryWrite(
                    "{\"schemaVersion\":1,\"packages\":[{\"id\":\"replacement\"}]}",
                    "https://example.com/registry.json",
                    "replacement-etag",
                    DateTimeOffset.UtcNow,
                    "2026-07-14T00:01:00Z",
                    out string ignoredError,
                    cancellation.Token));
            }
            finally
            {
                cancellation.Dispose();
            }

            Assert.AreEqual(seededBytes, File.ReadAllText(cachePath));
            CollectionAssert.IsEmpty(delays);
            CollectionAssert.IsEmpty(
                Directory.GetFiles(Path.GetDirectoryName(cachePath), "*.tmp"));
        }

        private static PackageOperationRecoveryRecord CreateRecoveryRecord(string targetUrl)
        {
            const string PackageId = "com.deucarian.atomic-test";
            return new PackageOperationRecoveryRecord(
                Guid.NewGuid().ToString("N"),
                "Atomic commit test",
                "registry-fingerprint",
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.Ticks,
                new[]
                {
                    new PackageOperationRecoveryStep(
                        PackageId,
                        "Atomic Test",
                        PackageChannel.Stable,
                        targetUrl,
                        false,
                        Array.Empty<string>(),
                        new[] { PackageId },
                        new[] { "Atomic Test" },
                        string.Empty,
                        PackageInstallProgressItemState.Pending,
                        string.Empty)
                },
                Array.Empty<string>());
        }

        private static void DeleteDirectoryWithRetries(string path)
        {
            const int AttemptCount = 3;

            for (int attempt = 0; attempt < AttemptCount; attempt++)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }

                    return;
                }
                catch (IOException) when (attempt + 1 < AttemptCount)
                {
                    Thread.Sleep(20 * (attempt + 1));
                }
                catch (UnauthorizedAccessException) when (attempt + 1 < AttemptCount)
                {
                    Thread.Sleep(20 * (attempt + 1));
                }
            }
        }
    }
}
