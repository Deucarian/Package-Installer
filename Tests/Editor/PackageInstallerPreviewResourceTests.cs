using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageInstallerPreviewResourceTests
    {
        [Test]
        public void PreviewResourcePaths_AreCentralizedAndStable()
        {
            Assert.AreEqual(
                "Packages/com.deucarian.package-installer/Editor/UI/PackageInstaller/PackageInstallerPreviewWindow.uxml",
                PackageInstallerPreviewResources.UxmlPath);
            Assert.AreEqual(
                "Packages/com.deucarian.package-installer/Editor/UI/PackageInstaller/PackageInstallerPreviewWindow.uss",
                PackageInstallerPreviewResources.UssPath);
            Assert.AreEqual(
                "Packages/com.deucarian.editor/Editor/Assets/Logos/DeucarianPlaceholderLogo.png",
                PackageInstallerPreviewResources.PlaceholderLogoPath);
            Assert.AreEqual(
                "Packages/com.deucarian.editor/Editor/Assets/Images/DeucarianPackageInstallerPlaceholderHero.png",
                PackageInstallerPreviewResources.PackageInstallerPlaceholderHeroPath);
            Assert.AreEqual(
                "Packages/com.deucarian.editor/Editor/Assets/Icons/DeucarianPackagePlaceholderIcon.png",
                PackageInstallerPreviewResources.PackagePlaceholderIconPath);
        }
    }
}
