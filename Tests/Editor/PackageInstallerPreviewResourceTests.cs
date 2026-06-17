using Deucarian.Editor;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageInstallerPreviewResourceTests
    {
        [Test]
        public void PreviewResourcePaths_KeepSharedAssetsInEditorPackage()
        {
            Assert.AreEqual(
                "Packages/com.deucarian.package-installer/Editor/UI/PackageInstaller/PackageInstallerPreviewWindow.uxml",
                PackageInstallerPreviewResources.UxmlPath);
            Assert.AreEqual(
                "Packages/com.deucarian.package-installer/Editor/UI/PackageInstaller/PackageInstallerPreviewWindow.uss",
                PackageInstallerPreviewResources.UssPath);
            Assert.AreEqual(
                "Packages/com.deucarian.editor/Editor/Assets/Logos/DeucarianPlaceholderLogo.png",
                DeucarianEditorUIResources.PlaceholderLogoPath);
            Assert.AreEqual(
                "Packages/com.deucarian.editor/Editor/Assets/Images/DeucarianPackageInstallerPlaceholderHero.png",
                DeucarianEditorUIResources.PackageInstallerPlaceholderHeroPath);
            Assert.AreEqual(
                "Packages/com.deucarian.editor/Editor/Assets/Icons/DeucarianPackagePlaceholderIcon.png",
                DeucarianEditorUIResources.PackagePlaceholderIconPath);
        }
    }
}
