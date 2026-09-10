using System;
using System.Collections.Generic;
using System.IO;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentProtectionTests
    {
        private string root;
        private string project;
        private PackageDevelopmentSession session;
        private PackageDevelopmentSessionStore store;
        private const string Id = "com.deucarian.protection-fixture";

        [SetUp]
        public void SetUp()
        {
            string parent = Directory.Exists("D:/Codex-storage") ? "D:/Codex-storage/validation/package-development-20260910" : Path.GetTempPath();
            root = Path.GetFullPath(Path.Combine(parent, "protection-tests", Guid.NewGuid().ToString("N")));
            project = Path.Combine(root, "consumer");
            string checkout = Path.Combine(root, "package");
            Directory.CreateDirectory(Path.Combine(project, "Packages"));
            Directory.CreateDirectory(Path.Combine(checkout, ".git"));
            store = new PackageDevelopmentSessionStore(project, PackageInstallerAtomicFileCommitter.Shared);
            session = new PackageDevelopmentSession {
                Id = Guid.NewGuid().ToString("N"), PackageId = Id, ProjectRoot = project,
                RepositoryRoot = checkout, CommonDirectory = Path.Combine(checkout, ".git"),
                LocalReference = "file:" + checkout.Replace('\\', '/'), State = DevelopmentSourceState.ResolvingLocal,
                InstalledReference = "https://github.com/Deucarian/Protection-Fixture.git#develop"
            };
            store.Save(session);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null && Path.GetFileName(Path.GetDirectoryName(root)) == "protection-tests" && Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void PersistedSessionBlocksFinalInstallAndRemoveBeforeClientSideEffects()
        {
            var client = new RecordingClient();
            Assert.Throws<InvalidOperationException>(() => DevelopmentPackageProtection.Add(client, Id, "https://github.com/Deucarian/Protection-Fixture.git#main", project));
            Assert.Throws<InvalidOperationException>(() => DevelopmentPackageProtection.Remove(client, Id, project));
            Assert.That(client.Calls, Is.Zero);
            Assert.That(DevelopmentPackageProtection.IsProtected("com.deucarian.other", project), Is.False);
        }

        [Test]
        public void ManagedSourceWinsOverCachedMigrationAndIsExcludedFromUpdateAllDiscovery()
        {
            var package = new PackageDefinition("Fixture", Id, "https://github.com/Deucarian/Protection-Fixture.git#main", "Fixture");
            var statuses = new Dictionary<string, PackageUpdateStatus> {
                [Id] = PackageUpdateStatus.SourceMigrationAvailable(package, PackageChannel.Stable, package.StableUrl, "revision", "1.0.0", "1.1.0", "Migration")
            };
            var status = DevelopmentPackageProtection.ResolveStatus(package, PackageChannel.Stable, statuses, _ => true, project);
            Assert.That(status.IsUpdateAvailable, Is.False);
            Assert.That(status.IsSourceMigrationAvailable, Is.False);
            StringAssert.Contains("Local development", status.Message);
        }

        [Test]
        public void RestoredSourceReleasesOnlyItsOwnProtection()
        {
            session.State = DevelopmentSourceState.Restored;
            store.Save(session);
            var client = new RecordingClient();
            DevelopmentPackageProtection.Remove(client, Id, project);
            Assert.That(client.Calls, Is.EqualTo(1));
        }

        [Test]
        public void CorruptJournalFailsClosedWithoutSendingAnyPackageRequest()
        {
            string journal = Path.Combine(project, "Library", "Deucarian", "PackageInstaller", "Development", "session-" + Id + ".json");
            File.WriteAllText(journal, "invalid fixture journal");
            var client = new RecordingClient();
            var error = Assert.Throws<InvalidOperationException>(() => DevelopmentPackageProtection.Remove(client, Id, project));
            Assert.That(client.Calls, Is.Zero);
            StringAssert.DoesNotContain("invalid fixture journal", error.Message);
        }

        private sealed class RecordingClient : IPackageInstallClient
        {
            internal int Calls;
            public IPackageInstallRequest Add(string reference) { Calls++; return null; }
            public IPackageInstallRequest Remove(string id) { Calls++; return null; }
        }
    }
}
