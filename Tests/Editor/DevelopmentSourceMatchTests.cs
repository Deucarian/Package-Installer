using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentSourceMatchTests
    {
        private const string Package = "com.deucarian.source-fixture";
        private const string Local = "D:/Codex-storage/validation/package-development-20260910/source-fixture";

        [Test]
        public void RestoringSamePreexistingLocalCheckoutRecognizesExactOriginalPath()
        {
            var session = new PackageDevelopmentSession { PackageId = Package, RepositoryRoot = Local,
                OriginalResolvedPath = Local, OriginalReference = "file:" + Local, WasDirectDependency = true, Restoring = true };
            Assert.IsTrue(DevelopmentSourceMatchPolicy.Matches(Package + "@file:" + Local, "Local", Local, "1",
                new DevelopmentSourceExpectation(session)));
            Assert.IsFalse(DevelopmentSourceMatchPolicy.Matches(Package + "@file:" + Local, "Local", Local + "/other", "1",
                new DevelopmentSourceExpectation(session)));
        }

        [Test]
        public void ConnectingRequiresLocalSourceAndExactResolvedPath()
        {
            var expected = new DevelopmentSourceExpectation(new PackageDevelopmentSession { PackageId = Package, RepositoryRoot = Local });
            Assert.IsTrue(DevelopmentSourceMatchPolicy.Matches(Package, "Local", Local, "1", expected));
            Assert.IsFalse(DevelopmentSourceMatchPolicy.Matches(Package, "Git", Local, "1", expected));
            Assert.IsFalse(DevelopmentSourceMatchPolicy.Matches(Package, "Local", Local + "/other", "1", expected));
        }

        [Test]
        public void RestoringGitRequiresOriginalReferenceAndGitSource()
        {
            string original = "https://github.com/Deucarian/Source-Fixture.git#develop";
            var expected = new DevelopmentSourceExpectation(new PackageDevelopmentSession { PackageId = Package,
                RepositoryRoot = Local, OriginalReference = original, WasDirectDependency = true, Restoring = true });
            Assert.IsTrue(DevelopmentSourceMatchPolicy.Matches(Package + "@" + original, "Git", Local + "-cache", "1", expected));
            Assert.IsFalse(DevelopmentSourceMatchPolicy.Matches(Package + "@" + original.Replace("develop", "main"), "Git", Local + "-cache", "1", expected));
            Assert.IsFalse(DevelopmentSourceMatchPolicy.Matches(Package + "@" + original, "Local", Local, "1", expected));
        }
    }
}
