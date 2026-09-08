using Deucarian.Editor;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    public sealed class PackageInstallerWindowStylesTests
    {
        [Test]
        public void EachWindowOwnsItsStylesWithoutMutatingSharedDefaults()
        {
            var first = new PackageInstallerWindowStyles();
            var second = new PackageInstallerWindowStyles();
            first.Ensure();
            second.Ensure();
            Assert.That(first.RowStatusStyle, Is.Not.SameAs(second.RowStatusStyle));
            Assert.That(first.RowStatusStyle, Is.Not.SameAs(DeucarianEditorWorkbenchGUI.RowStatusStyle));
            int originalSize = second.RowStatusStyle.fontSize;
            first.RowStatusStyle.fontSize = originalSize + 5;
            Assert.That(second.RowStatusStyle.fontSize, Is.EqualTo(originalSize));
            Assert.That(DeucarianEditorWorkbenchGUI.RowStatusStyle.fontSize, Is.EqualTo(originalSize));
            first.Ensure();
            Assert.That(first.RowStatusStyle.fontSize, Is.EqualTo(originalSize + 5));
        }
    }
}
