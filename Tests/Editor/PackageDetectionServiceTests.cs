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
        public void RegistryInstalledPackageUsesStableChannel()
        {
            PackageDefinition packageDefinition = CreatePackage();

            using (PackageDetectionService detectionService = new PackageDetectionService())
            {
                detectionService.ReplaceInstalledPackageForTests(
                    packageDefinition.PackageId,
                    "1.2.3",
                    PackageInstallSourceType.Registry,
                    "1.2.3");

                bool found = detectionService.TryGetInstalledPackageChannel(
                    packageDefinition,
                    out PackageChannel channel,
                    out string packageReference);

                Assert.IsTrue(found);
                Assert.AreEqual(PackageChannel.Stable, channel);
                Assert.AreEqual("1.2.3", packageReference);
                Assert.IsTrue(detectionService.TryGetInstalledPackageVersion(
                    packageDefinition.PackageId,
                    out string version));
                Assert.AreEqual("1.2.3", version);
            }
        }

        [Test]
        public void RegistryInstalledDevVersionUsesDevelopmentChannel()
        {
            PackageDefinition packageDefinition = CreatePackage();

            using (PackageDetectionService detectionService = new PackageDetectionService())
            {
                detectionService.ReplaceInstalledPackageForTests(
                    packageDefinition.PackageId,
                    "1.2.4-dev.7",
                    PackageInstallSourceType.Registry,
                    "1.2.4-dev.7");

                bool found = detectionService.TryGetInstalledPackageChannel(
                    packageDefinition,
                    out PackageChannel channel,
                    out string packageReference);

                Assert.IsTrue(found);
                Assert.AreEqual(PackageChannel.Development, channel);
                Assert.AreEqual("1.2.4-dev.7", packageReference);
            }
        }

        [Test]
        public void SourceDetectorClassifiesKnownUnitySources()
        {
            Assert.AreEqual(
                PackageInstallSourceType.Git,
                PackageInstallSourceUtility.Detect("Git", string.Empty, string.Empty, string.Empty));
            Assert.AreEqual(
                PackageInstallSourceType.Registry,
                PackageInstallSourceUtility.Detect("Registry", string.Empty, string.Empty, string.Empty));
            Assert.AreEqual(
                PackageInstallSourceType.Local,
                PackageInstallSourceUtility.Detect("Local", string.Empty, string.Empty, string.Empty));
            Assert.AreEqual(
                PackageInstallSourceType.Embedded,
                PackageInstallSourceUtility.Detect("Embedded", string.Empty, string.Empty, string.Empty));
        }

        [Test]
        public void SourceDetectorInfersRegistryFromVersionReference()
        {
            Assert.AreEqual(
                PackageInstallSourceType.Registry,
                PackageInstallSourceUtility.Detect(
                    string.Empty,
                    "com.deucarian.editor@1.0.0",
                    string.Empty,
                    string.Empty));
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
