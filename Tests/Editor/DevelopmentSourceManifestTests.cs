using System;
using System.Linq;
using System.Text;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentSourceManifestTests
    {
        private const string Package = "com.deucarian.source-fixture";
        private const string Original = "https://github.com/Deucarian/Source-Fixture.git#develop";

        [TestCase(false)]
        [TestCase(true)]
        public void DirectSourceRoundTripPreservesExactBytesAndUnrelatedJson(bool withBom)
        {
            string text = "{\r\n  \"dependencies\" : {\r\n    \"" + Package + "\" : \"" + Original +
                "\",\r\n    \"com.example.other\": \"1.2.3\"\r\n  }, \"nested\": {\"dependencies\": {\"a\":\"b\"}}, \"note\":\"€\"\r\n}\r\n";
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (withBom) bytes = new byte[] { 239, 187, 191 }.Concat(bytes).ToArray();
            PackageDevelopmentSession session = Session();
            byte[] connected = new SourceManifestEdit(bytes).Prepare(session);
            session.ConnectedManifestHash = SourceManifestEdit.Hash(connected);
            Assert.IsTrue(session.WasDirectDependency);
            Assert.AreEqual(Original, session.OriginalReference);
            Assert.AreEqual(session.LocalReference, new SourceManifestEdit(connected).GetReference(Package));
            CollectionAssert.AreEqual(bytes, new SourceManifestEdit(connected).Restore(session, connected));
        }

        [TestCase("{}")]
        [TestCase("{\r\n  }")]
        [TestCase("{\"com.example.other\":\"1.0.0\"}")]
        public void TransitiveSourceRemovesOnlyInsertedDirectOverrideAndRestoresExactBytes(string dependencies)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"dependencies\":" + dependencies + ",\"testables\":[]}");
            PackageDevelopmentSession session = Session();
            byte[] connected = new SourceManifestEdit(bytes).Prepare(session);
            session.ConnectedManifestHash = SourceManifestEdit.Hash(connected);
            Assert.IsFalse(session.WasDirectDependency);
            Assert.IsNull(session.OriginalReference);
            CollectionAssert.AreEqual(bytes, new SourceManifestEdit(connected).Restore(session, connected));
        }

        [Test]
        public void RestorePreservesConcurrentUnrelatedDependencyAndTopLevelChanges()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"dependencies\":{\"" + Package + "\":\"" + Original + "\"}}");
            PackageDevelopmentSession session = Session();
            byte[] connected = new SourceManifestEdit(bytes).Prepare(session);
            session.ConnectedManifestHash = SourceManifestEdit.Hash(connected);
            string modified = Encoding.UTF8.GetString(connected).Replace("}}", ",\"com.example.added\":\"3.0.0\"},\"testables\":[\"keep\"]}");
            byte[] current = Encoding.UTF8.GetBytes(modified);
            string restored = Encoding.UTF8.GetString(new SourceManifestEdit(current).Restore(session, current));
            Assert.AreEqual(modified.Replace(session.LocalReference, Original), restored);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TransitiveRestorePreservesDependenciesAddedBeforeOrAfterLocalOverride(bool before)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"dependencies\":{\"com.example.original\":\"1\"}}");
            PackageDevelopmentSession session = Session();
            byte[] connected = new SourceManifestEdit(bytes).Prepare(session);
            session.ConnectedManifestHash = SourceManifestEdit.Hash(connected);
            string text = Encoding.UTF8.GetString(connected);
            text = before ? text.Replace("{\"com.example.original", "{\"com.example.added\":\"2\",\"com.example.original") :
                text.Replace("}}", ",\"com.example.added\":\"2\"}}");
            byte[] current = Encoding.UTF8.GetBytes(text);
            SourceManifestEdit restored = new SourceManifestEdit(new SourceManifestEdit(current).Restore(session, current));
            Assert.IsNull(restored.GetReference(Package));
            Assert.AreEqual("1", restored.GetReference("com.example.original"));
            Assert.AreEqual("2", restored.GetReference("com.example.added"));
        }

        [Test]
        public void SelectedReferenceChangedExternallyIsNeverOverwritten()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"dependencies\":{\"" + Package + "\":\"" + Original + "\"}}");
            PackageDevelopmentSession session = Session();
            byte[] connected = new SourceManifestEdit(bytes).Prepare(session);
            byte[] modified = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(connected).Replace(session.LocalReference, "3.0.0"));
            Assert.Throws<InvalidOperationException>(() => new SourceManifestEdit(modified).Restore(session, modified));
            Assert.AreEqual("3.0.0", new SourceManifestEdit(modified).GetReference(Package));
        }

        [TestCase("{\"dependencies\":{},\"dependencies\":{}}")]
        [TestCase("{\"dependencies\":{\"a\":\"1\",\"a\":\"2\"}}")]
        [TestCase("{\"dependencies\":{\"a\":\"1\",}}")]
        [TestCase("{\"dependencies\":{\"a\":null}}")]
        [TestCase("{\"dependencies\":[],\"n\":1}")]
        [TestCase("{\"dependencies\":{},\"n\":01}")]
        [TestCase("{\"dependencies\":{},\"n\":1.}")]
        [TestCase("{\"dependencies\":{}} trailing")]
        public void InvalidOrAmbiguousManifestFailsClosed(string manifest)
        {
            Assert.Throws<InvalidOperationException>(() => new SourceManifestEdit(Encoding.UTF8.GetBytes(manifest)));
        }

        [Test]
        public void EscapedPropertyNameAndValueRoundTripWithoutReformatting()
        {
            string text = "{\"dependencies\":{\"com.deucarian.source-\\u0066ixture\":\"https:\\/\\/github.com/Deucarian/Source-Fixture.git#develop\"}}";
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            PackageDevelopmentSession session = Session();
            byte[] connected = new SourceManifestEdit(bytes).Prepare(session);
            CollectionAssert.AreEqual(bytes, new SourceManifestEdit(connected).Restore(session, connected));
        }

        [TestCase("https://user:credential@example.invalid/repository.git#branch")]
        [TestCase("com.deucarian.source-fixture@https://user:credential@example.invalid/repository.git#branch")]
        [TestCase("https://example.invalid/repository.git?token=credential#branch")]
        [TestCase("https://example.invalid/repository.git?path=/Package&token=credential#branch")]
        public void CredentialBearingReferenceIsRejectedWithoutEchoingIt(string reference)
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => SourceManifestEdit.ValidateReference(reference));
            StringAssert.DoesNotContain("credential@example", error.Message);
            StringAssert.DoesNotContain("token=", error.Message);
        }

        [Test]
        public void CanonicalSshGitUsernameIsAcceptedWithoutCredentials()
        {
            Assert.DoesNotThrow(() => SourceManifestEdit.ValidateReference("ssh://git@github.com/Deucarian/Source-Fixture.git#develop"));
            Assert.Throws<InvalidOperationException>(() => SourceManifestEdit.ValidateReference("ssh://git:credential@github.com/Deucarian/Source-Fixture.git#develop"));
        }

        private static PackageDevelopmentSession Session() => new PackageDevelopmentSession
        {
            PackageId = Package, LocalReference = "file:D:/Codex-storage/validation/package-development-20260910/source fixture"
        };
    }
}
