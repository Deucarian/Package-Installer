using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageSampleImportServiceTests
    {
        private string _root;
        private string _source;
        private string _destination;
        private string _staging;

        [SetUp]
        public void SetUp()
        {
            PackageInstallerActivityService.ClearForTests();
            _root = Path.Combine(
                Path.GetTempPath(),
                "Deucarian-PackageInstaller-SampleTests-" + Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "source");
            _destination = Path.Combine(_root, "Assets", "Samples", "Example");
            _staging = Path.Combine(_root, "Library", "Deucarian", "PackageInstaller", "SampleImports");
            Directory.CreateDirectory(Path.Combine(_source, "Nested"));
            File.WriteAllText(Path.Combine(_source, "root.txt"), "root-content");
            File.WriteAllText(Path.Combine(_source, "Nested", "child.txt"), "child-content");
        }

        [TearDown]
        public void TearDown()
        {
            PackageInstallerActivityService.ClearForTests();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Test]
        public void StagedImportCommitsCompleteVerifiedTree()
        {
            PackageSampleStageResult result = new PackageSampleStagingImporter().Import(
                _source,
                _destination,
                _staging,
                CancellationToken.None);

            Assert.AreEqual(PackageSampleStageResultState.Imported, result.State);
            Assert.AreEqual("root-content", File.ReadAllText(Path.Combine(_destination, "root.txt")));
            Assert.AreEqual(
                "child-content",
                File.ReadAllText(Path.Combine(_destination, "Nested", "child.txt")));
            AssertNoStagedOperations();
        }

        [Test]
        public void CanceledImportLeavesNoDestinationOrStagedContent()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                PackageSampleStageResult result = new PackageSampleStagingImporter().Import(
                    _source,
                    _destination,
                    _staging,
                    cancellation.Token);

                Assert.AreEqual(PackageSampleStageResultState.Canceled, result.State);
            }

            Assert.IsFalse(Directory.Exists(_destination));
            AssertNoStagedOperations();
        }

        [Test]
        public void MidCopyCancellationLeavesNoDestinationOrStagedContent()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                PackageSampleStageResult result = new PackageSampleStagingImporter(
                        new CancelAfterCopyFileOperations(cancellation))
                    .Import(_source, _destination, _staging, cancellation.Token);

                Assert.AreEqual(PackageSampleStageResultState.Canceled, result.State);
            }

            Assert.IsFalse(Directory.Exists(_destination));
            AssertNoStagedOperations();
        }

        [Test]
        public void CopyFailureLeavesNoDestinationAndCleansStaging()
        {
            PackageSampleStageResult result = new PackageSampleStagingImporter(
                    new ThrowingCopyFileOperations(throwOnCopyNumber: 2))
                .Import(_source, _destination, _staging, CancellationToken.None);

            Assert.AreEqual(PackageSampleStageResultState.Failed, result.State);
            Assert.IsFalse(Directory.Exists(_destination));
            AssertNoStagedOperations();
        }

        [Test]
        public void ExistingDestinationIsNeverOverwritten()
        {
            Directory.CreateDirectory(_destination);
            string sentinelPath = Path.Combine(_destination, "sentinel.txt");
            File.WriteAllText(sentinelPath, "keep-me");

            PackageSampleStageResult result = new PackageSampleStagingImporter().Import(
                _source,
                _destination,
                _staging,
                CancellationToken.None);

            Assert.AreEqual(PackageSampleStageResultState.AlreadyExists, result.State);
            Assert.AreEqual("keep-me", File.ReadAllText(sentinelPath));
            AssertNoStagedOperations();
        }

        [Test]
        public void DestinationRacePreservesWinnerAndCleansStaging()
        {
            PackageSampleStageResult result = new PackageSampleStagingImporter(
                    new DestinationRaceFileOperations(_destination))
                .Import(_source, _destination, _staging, CancellationToken.None);

            Assert.AreEqual(PackageSampleStageResultState.Failed, result.State);
            Assert.AreEqual(
                "race-winner",
                File.ReadAllText(Path.Combine(_destination, "sentinel.txt")));
            AssertNoStagedOperations();
        }

        [Test]
        public void UnitySampleImportFalseSignalsStagedFallbackInsteadOfSuccess()
        {
            bool imported = PackageSampleImportService.TryInvokeUnitySampleImportForTests(
                new FalseReturningUnitySample(),
                out string message);

            Assert.IsFalse(imported);
            StringAssert.Contains("returned false", message);
            StringAssert.Contains("staged fallback", message);
        }

        [Test]
        public void RetryRefreshesCurrentPackageInfoOnEveryAttempt()
        {
            PackageDefinition package = new PackageDefinition(
                "Retry Package",
                "com.deucarian.retry-package",
                "https://github.com/Deucarian/Retry-Package.git#main",
                "Retry package.",
                Array.Empty<string>());
            PackageExtraDefinition sample = new PackageExtraDefinition(
                "Retry Sample",
                "Retry sample.",
                samplePath: "Samples~/Retry",
                destinationPath: "Assets/Samples/Retry",
                requiresPackageInstalled: true);
            PackageManagerPackageInfo refreshedPackageInfo =
                (PackageManagerPackageInfo)Activator.CreateInstance(
                    typeof(PackageManagerPackageInfo),
                    nonPublic: true);
            int resolverCalls = 0;
            string resolvedPackageId = string.Empty;

            using (PackageSampleImportService service = new PackageSampleImportService(
                       packageInfoResolver: packageId =>
                       {
                           resolverCalls++;
                           resolvedPackageId = packageId;
                           return resolverCalls == 1 ? refreshedPackageInfo : null;
                       }))
            {
                service.ImportSample(package, sample, null);
                Assert.AreEqual(
                    "Install the package before importing this sample.",
                    service.LastStatusMessage);

                Assert.IsTrue(service.RetryLastImport());
                Assert.AreEqual(1, resolverCalls);
                Assert.AreEqual(package.PackageId, resolvedPackageId);
                Assert.AreNotEqual(
                    "Install the package before importing this sample.",
                    service.LastStatusMessage);

                Assert.IsTrue(service.RetryLastImport());
                Assert.AreEqual(2, resolverCalls);
                Assert.AreEqual(
                    "Install the package before importing this sample.",
                    service.LastStatusMessage);
            }

        }

        private void AssertNoStagedOperations()
        {
            if (!Directory.Exists(_staging))
            {
                return;
            }

            Assert.IsEmpty(Directory.GetDirectories(_staging));
            Assert.IsEmpty(Directory.GetFiles(_staging));
        }

        private sealed class ThrowingCopyFileOperations : IPackageFileOperations
        {
            private readonly SystemPackageFileOperations _inner = new SystemPackageFileOperations();
            private readonly int _throwOnCopyNumber;
            private int _copyCount;

            public ThrowingCopyFileOperations(int throwOnCopyNumber)
            {
                _throwOnCopyNumber = throwOnCopyNumber;
            }

            public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
            public bool FileExists(string path) => _inner.FileExists(path);
            public void CreateDirectory(string path) => _inner.CreateDirectory(path);
            public string[] GetFiles(string path) => _inner.GetFiles(path);
            public string[] GetDirectories(string path) => _inner.GetDirectories(path);

            public void CopyFile(string sourcePath, string destinationPath)
            {
                _copyCount++;
                if (_copyCount == _throwOnCopyNumber)
                {
                    throw new IOException("Synthetic copy failure.");
                }
                _inner.CopyFile(sourcePath, destinationPath);
            }

            public void MoveDirectory(string sourcePath, string destinationPath) =>
                _inner.MoveDirectory(sourcePath, destinationPath);
            public void DeleteDirectory(string path, bool recursive) =>
                _inner.DeleteDirectory(path, recursive);
            public long GetFileLength(string path) => _inner.GetFileLength(path);
            public byte[] ComputeSha256(string path) => _inner.ComputeSha256(path);
        }

        private sealed class FalseReturningUnitySample
        {
            public bool Import()
            {
                return false;
            }
        }

        private sealed class CancelAfterCopyFileOperations : IPackageFileOperations
        {
            private readonly SystemPackageFileOperations _inner = new SystemPackageFileOperations();
            private readonly CancellationTokenSource _cancellation;

            public CancelAfterCopyFileOperations(CancellationTokenSource cancellation)
            {
                _cancellation = cancellation;
            }

            public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
            public bool FileExists(string path) => _inner.FileExists(path);
            public void CreateDirectory(string path) => _inner.CreateDirectory(path);
            public string[] GetFiles(string path) => _inner.GetFiles(path);
            public string[] GetDirectories(string path) => _inner.GetDirectories(path);
            public void CopyFile(string sourcePath, string destinationPath)
            {
                _inner.CopyFile(sourcePath, destinationPath);
                _cancellation.Cancel();
            }
            public void MoveDirectory(string sourcePath, string destinationPath) =>
                _inner.MoveDirectory(sourcePath, destinationPath);
            public void DeleteDirectory(string path, bool recursive) =>
                _inner.DeleteDirectory(path, recursive);
            public long GetFileLength(string path) => _inner.GetFileLength(path);
            public byte[] ComputeSha256(string path) => _inner.ComputeSha256(path);
        }

        private sealed class DestinationRaceFileOperations : IPackageFileOperations
        {
            private readonly SystemPackageFileOperations _inner = new SystemPackageFileOperations();
            private readonly string _destination;

            public DestinationRaceFileOperations(string destination)
            {
                _destination = destination;
            }

            public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
            public bool FileExists(string path) => _inner.FileExists(path);
            public void CreateDirectory(string path) => _inner.CreateDirectory(path);
            public string[] GetFiles(string path) => _inner.GetFiles(path);
            public string[] GetDirectories(string path) => _inner.GetDirectories(path);
            public void CopyFile(string sourcePath, string destinationPath) =>
                _inner.CopyFile(sourcePath, destinationPath);
            public void MoveDirectory(string sourcePath, string destinationPath)
            {
                Directory.CreateDirectory(_destination);
                File.WriteAllText(Path.Combine(_destination, "sentinel.txt"), "race-winner");
                throw new IOException("Synthetic destination race.");
            }
            public void DeleteDirectory(string path, bool recursive) =>
                _inner.DeleteDirectory(path, recursive);
            public long GetFileLength(string path) => _inner.GetFileLength(path);
            public byte[] ComputeSha256(string path) => _inner.ComputeSha256(path);
        }
    }
}
