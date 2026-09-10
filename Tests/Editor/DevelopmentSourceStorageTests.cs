using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentSourceStorageTests
    {
        private string _root;
        private string _project;
        private string _checkout;
        private string _common;
        private PackageInstallerAtomicFileCommitter _committer;
        private readonly List<string> _links = new List<string>();

        [SetUp]
        public void SetUp()
        {
            string storage = Directory.Exists("D:/Codex-storage") ?
                "D:/Codex-storage/validation/package-development-20260910" : Path.GetTempPath();
            _root = Path.GetFullPath(Path.Combine(storage, "source-tests", Guid.NewGuid().ToString("N")));
            _project = Path.Combine(_root, "consumer");
            _checkout = Path.Combine(_root, "checkout");
            _common = Path.Combine(_checkout, ".git");
            Directory.CreateDirectory(Path.Combine(_project, "Packages"));
            Directory.CreateDirectory(_common);
            _committer = PackageInstallerAtomicFileCommitter.Shared;
            File.WriteAllText(Path.Combine(_project, "Packages", "manifest.json"), "{\"dependencies\":{}}", new UTF8Encoding(false));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string link in _links) if (Directory.Exists(link)) Directory.Delete(link);
            _links.Clear();
            if (!string.IsNullOrEmpty(_root) && Path.GetFileName(Path.GetDirectoryName(_root)) == "source-tests" && Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void ClaimDirectoryJunctionCannotRedirectMetadataWrites()
        {
            string outside = Path.Combine(_root, "outside-claims");
            CreateDirectoryLink(Path.Combine(_common, "deucarian-package-development"), outside);
            var claims = new DevelopmentCheckoutClaims(_committer);
            Assert.Throws<InvalidOperationException>(() => claims.Acquire(Session()));
            Assert.IsEmpty(Directory.GetFileSystemEntries(outside));
        }

        [Test]
        public void JournalDirectoryJunctionCannotRedirectStateWrites()
        {
            string outside = Path.Combine(_root, "outside-journal");
            Directory.CreateDirectory(Path.Combine(_project, "Library", "Deucarian", "PackageInstaller"));
            CreateDirectoryLink(Path.Combine(_project, "Library", "Deucarian", "PackageInstaller", "Development"), outside);
            Assert.Throws<InvalidOperationException>(() => new PackageDevelopmentSessionStore(_project, _committer).Save(Session()));
            Assert.Throws<InvalidOperationException>(() => new DevelopmentSourceFileSystem(_project, _committer).AcquireProjectLock());
            Assert.IsEmpty(Directory.GetFileSystemEntries(outside));
        }

        [Test]
        public void ManifestDirectoryJunctionCannotRedirectConsumerWrites()
        {
            string outside = Path.Combine(_root, "outside-manifest");
            string packages = Path.Combine(_project, "Packages");
            File.Delete(Path.Combine(packages, "manifest.json"));
            Directory.Delete(packages);
            CreateDirectoryLink(packages, outside);
            File.WriteAllText(Path.Combine(outside, "manifest.json"), "{\"dependencies\":{}}");
            var files = new DevelopmentSourceFileSystem(_project, _committer);
            Assert.Throws<InvalidOperationException>(() => files.ReadManifest());
            Assert.Throws<InvalidOperationException>(() => files.CompareExchangeManifest(Encoding.UTF8.GetBytes("{\"dependencies\":{}}"), Encoding.UTF8.GetBytes("{}")));
            Assert.AreEqual("{\"dependencies\":{}}", File.ReadAllText(Path.Combine(outside, "manifest.json")));
        }

        [Test]
        public void AtomicManifestWritePreservesCurrentBytesWhenPreconditionFails()
        {
            var files = new DevelopmentSourceFileSystem(_project, _committer);
            byte[] original = files.ReadManifest();
            byte[] concurrent = Encoding.UTF8.GetBytes("{\"dependencies\":{},\"testables\":[]}");
            File.WriteAllBytes(Path.Combine(_project, "Packages", "manifest.json"), concurrent);
            Assert.Throws<InvalidOperationException>(() => files.CompareExchangeManifest(original, Encoding.UTF8.GetBytes("{}")));
            CollectionAssert.AreEqual(concurrent, files.ReadManifest());
            Assert.IsEmpty(Directory.GetFiles(Path.Combine(_project, "Packages"), "*.tmp"));
        }

        [Test]
        public void AtomicManifestWriteSucceedsAndLeavesNoTemporaryFile()
        {
            var files = new DevelopmentSourceFileSystem(_project, _committer);
            byte[] original = files.ReadManifest();
            byte[] connected = Encoding.UTF8.GetBytes("{\"dependencies\":{\"com.deucarian.source-fixture\":\"file:/fixture\"}}");
            files.CompareExchangeManifest(original, connected);
            CollectionAssert.AreEqual(connected, files.ReadManifest());
            Assert.IsEmpty(Directory.GetFiles(Path.Combine(_project, "Packages"), "*.tmp"));
        }

        [Test]
        public void ProjectLockCoordinatesIndependentServiceInstances()
        {
            var first = new DevelopmentSourceFileSystem(_project, _committer);
            var second = new DevelopmentSourceFileSystem(_project, _committer);
            using (first.AcquireProjectLock())
                Assert.Throws<InvalidOperationException>(() => second.AcquireProjectLock());
            using (second.AcquireProjectLock()) { }
        }

        [Test]
        public void JournalRoundTripsAcrossInstancesWithoutCopyingUnrelatedManifestData()
        {
            var store = new PackageDevelopmentSessionStore(_project, _committer);
            PackageDevelopmentSession session = Session();
            session.OriginalReference = "https://github.com/Deucarian/Source-Fixture.git#develop";
            session.OriginalValueJson = SourceManifestJson.Quote(session.OriginalReference);
            session.WasDirectDependency = true;
            session.OriginalManifestHash = "original-hash";
            store.Save(session);
            var restored = new PackageDevelopmentSessionStore(_project, _committer).LoadAll().Single();
            Assert.AreEqual(session.Id, restored.Id);
            Assert.AreEqual(session.OriginalValueJson, restored.OriginalValueJson);
            Assert.AreEqual(session.State, restored.State);
            Assert.IsEmpty(Directory.GetFiles(Path.Combine(_project, "Library", "Deucarian", "PackageInstaller", "Development"), "*.tmp"));
        }

        [Test]
        public void CorruptJournalFailsClosedWithoutEchoingContents()
        {
            var store = new PackageDevelopmentSessionStore(_project, _committer);
            store.Save(Session());
            string file = Directory.GetFiles(Path.Combine(_project, "Library", "Deucarian", "PackageInstaller", "Development"), "session-*.json").Single();
            File.WriteAllText(file, "invalid journal contents");
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => store.LoadAll());
            StringAssert.Contains("blocked", error.Message);
            StringAssert.DoesNotContain("invalid journal contents", error.Message);
        }

        [Test]
        public void JournalCannotBeReusedInAnotherProject()
        {
            var store = new PackageDevelopmentSessionStore(_project, _committer);
            PackageDevelopmentSession session = Session();
            session.ProjectRoot = Path.Combine(_root, "different-project");
            Assert.Throws<InvalidOperationException>(() => store.Save(session));
        }

        [Test]
        public void JournalRejectsTamperedOriginalReferenceEdit()
        {
            var store = new PackageDevelopmentSessionStore(_project, _committer);
            PackageDevelopmentSession session = Session();
            session.WasDirectDependency = true;
            session.OriginalReference = "1.0.0";
            session.OriginalValueJson = "\"1.0.0\",\"another\":\"2.0.0\"";
            Assert.Throws<InvalidOperationException>(() => store.Save(session));
        }

        [Test]
        public void SameCheckoutCannotBeClaimedByTwoProjects()
        {
            var claims = new DevelopmentCheckoutClaims(_committer);
            PackageDevelopmentSession first = Session();
            PackageDevelopmentSession second = Session();
            second.ProjectRoot = Path.Combine(_root, "other-project");
            claims.Acquire(first);
            Assert.Throws<InvalidOperationException>(() => claims.Acquire(second));
            claims.AssertOwned(first);
            Assert.Throws<InvalidOperationException>(() => claims.AssertOwned(second));
            Assert.Throws<InvalidOperationException>(() => claims.Release(second));
            claims.Release(first);
            claims.Acquire(second);
            claims.AssertOwned(second);
        }

        [Test]
        public void SeparateWorktreesCanHaveIndependentClaimsInOneCommonDirectory()
        {
            var claims = new DevelopmentCheckoutClaims(_committer);
            PackageDevelopmentSession first = Session();
            PackageDevelopmentSession second = Session();
            second.RepositoryRoot = Path.Combine(_root, "another-worktree");
            second.ProjectRoot = Path.Combine(_root, "other-project");
            claims.Acquire(first);
            claims.Acquire(second);
            claims.AssertOwned(first);
            claims.AssertOwned(second);
            claims.Release(first);
            claims.AssertOwned(second);
        }

        [Test]
        public void DeletedCheckoutDoesNotBlockClaimReleaseDuringRestoration()
        {
            var claims = new DevelopmentCheckoutClaims(_committer);
            PackageDevelopmentSession session = Session();
            claims.Acquire(session);
            Assert.That(Path.GetFullPath(_checkout).StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            Directory.Delete(_checkout, true);
            Assert.DoesNotThrow(() => claims.Release(session));
            Assert.Throws<InvalidOperationException>(() => claims.AssertOwned(session));
        }

        private PackageDevelopmentSession Session() => new PackageDevelopmentSession
        {
            Id = Guid.NewGuid().ToString("N"), PackageId = "com.deucarian.source-fixture", ProjectRoot = _project,
            RepositoryRoot = _checkout, CommonDirectory = _common, LocalReference = "file:" + _checkout.Replace('\\', '/'),
            State = DevelopmentSourceState.ResolvingLocal, InstalledReference = "https://github.com/Deucarian/Source-Fixture.git#develop"
        };

        private void CreateDirectoryLink(string link, string target)
        {
            Directory.CreateDirectory(target);
            var info = new ProcessStartInfo
            {
                FileName = Path.DirectorySeparatorChar == '\\' ? "cmd.exe" : "/bin/ln",
                Arguments = Path.DirectorySeparatorChar == '\\' ? "/c mklink /J \"" + link + "\" \"" + target + "\"" :
                    "-s \"" + target + "\" \"" + link + "\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (Process process = Process.Start(info))
            {
                Assert.IsTrue(process.WaitForExit(10000), "Fixture link creation timed out.");
                Assert.AreEqual(0, process.ExitCode, "Fixture link creation failed.");
            }
            _links.Add(link);
        }
    }
}
