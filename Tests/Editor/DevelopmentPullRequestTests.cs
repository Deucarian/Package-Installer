using System;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentPullRequestTests
    {
        private const string Head = "1111111111111111111111111111111111111111";
        private const string TargetHead = "2222222222222222222222222222222222222222";

        [TestCase("https://github.com/Deucarian/Package.git", "github.com/deucarian/package")]
        [TestCase("ssh://git@github.com/Deucarian/Package.git", "github.com/deucarian/package")]
        [TestCase("git@github.com:Deucarian/Package.git", "github.com/deucarian/package")]
        [TestCase("https://bitbucket.org/Deucarian/Package.git", "bitbucket.org/deucarian/package")]
        [TestCase("ssh://git@bitbucket.org/Deucarian/Package.git", "bitbucket.org/deucarian/package")]
        [TestCase("git@bitbucket.org:Deucarian/Package.git", "bitbucket.org/deucarian/package")]
        public void CanonicalProviderRemotesProduceCredentialFreeHttpsHandoffs(string remote, string identity)
        {
            var snapshot = Snapshot();
            string url = DevelopmentPullRequest.CreateUrl(Repository(remote), snapshot, "integration/next");
            string expected = identity.StartsWith("github.com/", StringComparison.Ordinal)
                ? "https://" + identity + "/compare/integration%2Fnext...feature%2Fpackage%2Fextension?quick_pull=1"
                : "https://" + identity + "/pull-requests/new?source=feature%2Fpackage%2Fextension&t=1";
            Assert.That(url, Is.EqualTo(expected));
            var uri = new Uri(url);
            Assert.That(uri.Scheme, Is.EqualTo("https"));
            Assert.That(uri.UserInfo, Is.Empty);
            Assert.That(uri.Fragment, Is.Empty);
        }

        [TestCase("https://github.com/Deucarian/Package.git?access_token=fixture")]
        [TestCase("https://user:fixture@github.com/Deucarian/Package.git")]
        [TestCase("https://user@bitbucket.org/Deucarian/Package.git")]
        [TestCase("https://bitbucket.org/Deucarian/Package.git#develop")]
        [TestCase("https://bitbucket.org:8443/Deucarian/Package.git")]
        [TestCase("https://github.com.invalid/Deucarian/Package.git")]
        [TestCase("https://bitbucket.example/Deucarian/Package.git")]
        [TestCase("https://bitbucket.org/Deucarian/Nested/Package.git")]
        [TestCase("http://github.com/Deucarian/Package.git")]
        [TestCase("file:///D:/private/package")]
        [TestCase("javascript:alert(1)")]
        [TestCase("https://github.com/Deucarian/Package.git\n")]
        public void UnsafeOrUnsupportedRemotesNeverProduceABrowserUrl(string remote)
        {
            Assert.That(DevelopmentPullRequest.UnavailableReason(Repository(remote), Snapshot(), "develop"), Is.Not.Empty);
            Assert.Throws<DevelopmentGitException>(() => DevelopmentPullRequest.CreateUrl(Repository(remote), Snapshot(), "develop"));
        }

        [Test]
        public void RepositoryPathsAndChangeDataDoNotEnterTheHandoff()
        {
            var repository = Repository();
            repository.Root = "D:/private/consumer-and-checkout";
            repository.ConsumerRoot = "D:/private/application";
            var snapshot = Snapshot();
            snapshot.IndexRecords = "private index contents";
            snapshot.Files = new[] { new DevelopmentChangedFile { Path = "PrivateDraft.txt", WorktreeFingerprint = "private data" } };
            string url = DevelopmentPullRequest.CreateUrl(repository, snapshot, "develop");
            Assert.That(url, Is.EqualTo("https://github.com/deucarian/package/compare/develop...feature%2Fpackage%2Fextension?quick_pull=1"));
        }

        [Test]
        public void UninspectedRepositoriesCannotOpenPullRequests()
        {
            Assert.That(DevelopmentPullRequest.UnavailableReason(null, Snapshot(), "develop"), Is.Not.Empty);
            Assert.That(DevelopmentPullRequest.UnavailableReason(Repository(), null, "develop"), Is.Not.Empty);
        }

        [TestCase("")]
        [TestCase("main")]
        [TestCase("develop")]
        [TestCase("production")]
        [TestCase("release/1.4")]
        [TestCase("feature/../main")]
        [TestCase("feature/extension?title=unreviewed")]
        public void DetachedProtectedAndInvalidSourceBranchesAreUnavailable(string branch)
        {
            var snapshot = Snapshot(); snapshot.Branch = branch; snapshot.Upstream = "origin/" + branch;
            Assert.That(DevelopmentPullRequest.UnavailableReason(Repository(), snapshot, "develop"), Is.Not.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("release/../develop")]
        [TestCase("develop&body=draft")]
        [TestCase("feature/package/extension")]
        public void InvalidOrSameBranchTargetsAreUnavailable(string target)
        {
            Assert.That(DevelopmentPullRequest.UnavailableReason(Repository(), Snapshot(), target), Is.Not.Empty);
        }

        [TestCase("", 0, 0)]
        [TestCase("upstream/feature/package/extension", 0, 0)]
        [TestCase("origin/different", 0, 0)]
        [TestCase("origin/feature/package/extension", 1, 0)]
        [TestCase("origin/feature/package/extension", 0, 1)]
        [TestCase("origin/feature/package/extension", 2, 3)]
        public void UnpushedWrongUpstreamAndDivergedBranchesAreUnavailable(string upstream, int ahead, int behind)
        {
            var snapshot = Snapshot(); snapshot.Upstream = upstream; snapshot.Ahead = ahead; snapshot.Behind = behind;
            Assert.That(DevelopmentPullRequest.UnavailableReason(Repository(), snapshot, "develop"), Is.Not.Empty);
        }

        [Test]
        public void ExactPushedSourceAndExistingTargetAcceptEitherRemoteRecordOrder()
        {
            string source = Head + "\trefs/heads/feature/package/extension";
            string target = TargetHead + "\trefs/heads/integration/next";
            Assert.DoesNotThrow(() => RequireRemote(source + "\n" + target + "\n"));
            Assert.DoesNotThrow(() => RequireRemote(target + "\r\n" + source + "\r\n"));
            // The development branch may advance normally; it need not point to the source commit.
            Assert.DoesNotThrow(() => RequireRemote(source + "\n" + Head + "\trefs/heads/integration/next\n"));
        }

        [TestCase("")]
        [TestCase(Head + "\trefs/heads/feature/package/extension\n")]
        [TestCase(TargetHead + "\trefs/heads/integration/next\n")]
        [TestCase(TargetHead + "\trefs/heads/feature/package/extension\n" + TargetHead + "\trefs/heads/integration/next\n")]
        [TestCase(Head + "\trefs/heads/feature/other\n" + TargetHead + "\trefs/heads/integration/next\n")]
        [TestCase(Head + "\trefs/heads/feature/package/extension\n" + TargetHead + "\trefs/heads/main\n")]
        [TestCase(Head + "\trefs/heads/feature/package/extension\n\trefs/heads/integration/next\n")]
        [TestCase(Head + "\trefs/heads/feature/package/extension\nabc1234\trefs/heads/integration/next\n")]
        [TestCase(Head + "\trefs/heads/feature/package/extension\nnot-an-object-id\trefs/heads/integration/next\n")]
        [TestCase(Head + "\trefs/heads/feature/package/extension\n" + Head + "\trefs/heads/feature/package/extension\n")]
        [TestCase(Head + "\trefs/heads/feature/package/extension\n" + TargetHead + "\trefs/heads/integration/next\n" + TargetHead + "\trefs/heads/extra\n")]
        [TestCase(Head + " refs/heads/feature/package/extension\n" + TargetHead + "\trefs/heads/integration/next\n")]
        public void MissingChangedOrAmbiguousRemoteBranchEvidenceIsRejected(string output)
        {
            Assert.Throws<DevelopmentGitException>(() => RequireRemote(output));
        }

        [Test]
        public void FullSha256RemoteObjectIdsAreSupported()
        {
            string sourceHead = new string('a', 64), targetHead = new string('b', 64);
            Assert.DoesNotThrow(() => DevelopmentPullRequest.RequireRemoteBranches(
                sourceHead + "\trefs/heads/feature/extension\n" + targetHead + "\trefs/heads/develop\n",
                "feature/extension", "develop", sourceHead));
        }

        [Test]
        public void LocalBranchOrCommitDriftRequiresFreshReview()
        {
            var repository = Repository(); var snapshot = Snapshot();
            Assert.DoesNotThrow(() => DevelopmentPullRequest.RequireReviewedHead(repository, snapshot));
            repository.Head = TargetHead;
            Assert.Throws<DevelopmentGitException>(() => DevelopmentPullRequest.RequireReviewedHead(repository, snapshot));
            repository.Head = Head; repository.Branch = "feature/other";
            Assert.Throws<DevelopmentGitException>(() => DevelopmentPullRequest.RequireReviewedHead(repository, snapshot));
        }

        private static void RequireRemote(string output) => DevelopmentPullRequest.RequireRemoteBranches(output,
            "feature/package/extension", "integration/next", Head);
        private static DevelopmentRepository Repository(string remote = "https://github.com/Deucarian/Package.git")
            => new DevelopmentRepository { ExpectedRemote = remote, Branch = "feature/package/extension", Head = Head };
        private static DevelopmentGitSnapshot Snapshot() => new DevelopmentGitSnapshot {
            Head = Head, Branch = "feature/package/extension", Upstream = "origin/feature/package/extension",
            Files = Array.Empty<DevelopmentChangedFile>()
        };
    }
}
