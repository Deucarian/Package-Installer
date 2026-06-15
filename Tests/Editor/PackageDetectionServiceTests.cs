using System;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageDetectionServiceTests
    {
        [Test]
        public void InstalledReferenceWithRefsHeadsMainUsesStableChannel()
        {
            PackageDefinition packageDefinition = CreatePackage();

            using (PackageDetectionService detectionService = new PackageDetectionService())
            {
                detectionService.ReplaceInstalledPackageReferenceForTests(
                    packageDefinition.PackageId,
                    "https://github.com/Deucarian/Object-Loading.git#refs/heads/main");

                bool found = detectionService.TryGetInstalledPackageChannel(
                    packageDefinition,
                    out PackageChannel channel,
                    out _);

                Assert.IsTrue(found);
                Assert.AreEqual(PackageChannel.Stable, channel);
            }
        }

        [Test]
        public void InstalledReferenceWithOriginDevelopUsesDevelopmentChannel()
        {
            PackageDefinition packageDefinition = CreatePackage();

            using (PackageDetectionService detectionService = new PackageDetectionService())
            {
                detectionService.ReplaceInstalledPackageReferenceForTests(
                    packageDefinition.PackageId,
                    "https://github.com/Deucarian/Object-Loading.git#origin/develop");

                bool found = detectionService.TryGetInstalledPackageChannel(
                    packageDefinition,
                    out PackageChannel channel,
                    out _);

                Assert.IsTrue(found);
                Assert.AreEqual(PackageChannel.Development, channel);
            }
        }

        [Test]
        public void InstalledChannelSelectionDoesNotOverrideManualSelection()
        {
            Assert.IsFalse(PackageInstallerWindow.ShouldApplyInstalledChannelSelection(
                hasSelectedChannel: true,
                hasStoredChannel: true,
                wasAutoSelectedChannel: false));
        }

        [Test]
        public void InstalledChannelSelectionCanInitializeUnselectedPackage()
        {
            Assert.IsTrue(PackageInstallerWindow.ShouldApplyInstalledChannelSelection(
                hasSelectedChannel: false,
                hasStoredChannel: false,
                wasAutoSelectedChannel: false));
        }

        [Test]
        public void InstalledChannelSelectionCanRefreshAutoSelection()
        {
            Assert.IsTrue(PackageInstallerWindow.ShouldApplyInstalledChannelSelection(
                hasSelectedChannel: true,
                hasStoredChannel: false,
                wasAutoSelectedChannel: true));
        }

        private static PackageDefinition CreatePackage()
        {
            return new PackageDefinition(
                "Deucarian Object Loading",
                "com.deucarian.object-loading",
                "https://github.com/Deucarian/Object-Loading.git#main",
                "Reusable runtime loading pipeline.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://github.com/Deucarian/Object-Loading.git#develop",
                category: "Core");
        }
    }
}
