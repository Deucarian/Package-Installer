using System;
using System.Linq;
using NUnit.Framework;
using Deucarian.PackageInstaller.Editor.Development;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentGitPolicyTests
    {
        [TestCase("main")]
        [TestCase("develop")]
        [TestCase("release/1.0")]
        [TestCase("feature/x..y")]
        [TestCase("feature/a.lock")]
        [TestCase("feature/a//b")]
        [TestCase("feature/x\n--force")]
        public void ProtectedAndHostileBranchesAreRejected(string branch) =>
            Assert.Throws<DevelopmentGitException>(() => DevelopmentGitPolicy.RequireFeatureBranch(branch));

        [TestCase("feature/package-work")]
        [TestCase("codex/first-slice")]
        [TestCase("fix/meta-pair")]
        public void FeatureBranchesAreAccepted(string branch) =>
            Assert.DoesNotThrow(() => DevelopmentGitPolicy.RequireFeatureBranch(branch));

        [TestCase("../consumer/file")]
        [TestCase(".git/config")]
        [TestCase("source/.GIT/config")]
        [TestCase("Library/PackageCache/file")]
        [TestCase(":(top)../consumer")]
        [TestCase("file\nname")]
        [TestCase("file\\name")]
        [TestCase("file/../other")]
        [TestCase("file./asset")]
        public void UnsafeRelativePathsFailClosed(string path) =>
            Assert.Throws<DevelopmentGitException>(() => DevelopmentGitPolicy.RequireRelativePath(path));

        [Test]
        public void ShellCharactersAndWildcardsRemainLiteralFileNames()
        {
            Assert.AreEqual("a [1] $(literal); & name.cs", DevelopmentGitPolicy.RequireRelativePath("a [1] $(literal); & name.cs"));
            Assert.AreEqual("\"a \\\"quote\\\"\\\\\"", DevelopmentGitProcessRunner.QuoteArgument("a \"quote\"\\"));
        }

        [TestCase("https://github.com/Deucarian/Logging.git")]
        [TestCase("git@github.com:Deucarian/Logging.git")]
        [TestCase("ssh://git@github.com/Deucarian/Logging.git")]
        public void HttpsAndSshShareCredentialFreeRemoteIdentity(string remote) =>
            Assert.AreEqual("github.com/deucarian/logging", DevelopmentGitPolicy.RemoteIdentity(remote));

        [TestCase("https://username:credential@github.com/Deucarian/Logging.git")]
        [TestCase("https://github.com/Deucarian/Logging.git?private=value")]
        [TestCase("https://github.com/Deucarian/Logging.git#develop")]
        [TestCase("ext::untrusted")]
        [TestCase("file:///temporary/repo")]
        [TestCase("https://private-host.invalid/Deucarian/Logging.git")]
        public void CredentialBearingAndUnsupportedRemotesAreRejected(string remote) =>
            Assert.Throws<DevelopmentGitException>(() => DevelopmentGitPolicy.RemoteIdentity(remote));

        [Test]
        public void StatusParserPreservesRenameDeletionUntrackedConflictAndSpaces()
        {
            var files = DevelopmentGitStatusReader.ParseStatus("R  New name.cs\0Old name.cs\0 D deleted.txt\0?? New [2].txt\0UU conflict.txt\0");
            Assert.AreEqual("Old name.cs", files[0].OriginalPath);
            Assert.IsTrue(files[0].IsStaged);
            Assert.AreEqual('D', files[1].WorktreeStatus);
            Assert.IsTrue(files[2].IsUntracked);
            Assert.IsTrue(files[3].IsConflict);
        }

        [Test]
        public void MetaExpansionIsExplicitAndIncludesRenameOrigins()
        {
            var files = DevelopmentGitStatusReader.ParseStatus("R  New.cs\0Old.cs\0R  New.cs.meta\0Old.cs.meta\0");
            CollectionAssert.AreEquivalent(new[] { "New.cs", "Old.cs", "New.cs.meta", "Old.cs.meta" },
                DevelopmentGitPolicy.IncludeMeta(new[] { "New.cs" }, files).ToArray());
        }

        [Test]
        public void CredentialLinesAreHiddenAndDisplayIsBounded()
        {
            string display = DevelopmentGitInspection.SafeDisplay("safe\nAuthorization: bearer placeholder\nhttps://user:value@github.com/org/repo\nend");
            StringAssert.Contains("safe", display);
            StringAssert.DoesNotContain("placeholder", display);
            StringAssert.DoesNotContain("user:value", display);
            Assert.Less(DevelopmentGitInspection.SafeDisplay(new string('x', 200000)).Length, 132000);
        }
    }
}
