using System;
using System.Collections.Generic;
using Deucarian.Logging;
using NUnit.Framework;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageDetectionServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            PackageInstallerActivityService.ClearForTests();
            DeucarianLog.ClearSinks();
        }

        [TearDown]
        public void TearDown()
        {
            PackageInstallerActivityService.ClearForTests();
            DeucarianLog.ResetSinksToDefault();
        }

        [Test]
        public void RefreshUsesOfflineListIncludingIndirectDependenciesAndDoesNotStartSecondPendingRequest()
        {
            FakePackageListRequest request = new FakePackageListRequest(isCompleted: false);
            FakePackageListClient client = new FakePackageListClient(_ => request);

            using (PackageDetectionService detectionService = new PackageDetectionService(client))
            {
                detectionService.Refresh();
                detectionService.Refresh();

                Assert.AreEqual(1, client.ListCalls);
                Assert.IsTrue(client.LastOfflineMode);
                Assert.IsTrue(client.LastIncludeIndirectDependencies);
                Assert.IsTrue(detectionService.IsRefreshing);
            }
        }

        [Test]
        public void CompletedButUnprocessedListRequestIsNotReplaced()
        {
            FakePackageListRequest request = new FakePackageListRequest(
                isCompleted: true,
                isSuccess: true);
            FakePackageListClient client = new FakePackageListClient(_ => request);

            using (PackageDetectionService detectionService = new PackageDetectionService(client))
            {
                detectionService.Refresh();
                detectionService.Refresh();

                Assert.AreEqual(1, client.ListCalls);
                Assert.IsTrue(detectionService.IsRefreshing);

                detectionService.UpdateForTests();

                Assert.IsFalse(detectionService.IsRefreshing);
                Assert.IsTrue(detectionService.HasSuccessfulRefresh);
            }
        }

        [Test]
        public void SuccessfulEmptyListCompletesAndRaisesRefreshCompleted()
        {
            FakePackageListClient client = new FakePackageListClient(
                _ => new FakePackageListRequest(isCompleted: true, isSuccess: true));
            int completedEvents = 0;

            using (PackageDetectionService detectionService = new PackageDetectionService(client))
            {
                detectionService.RefreshCompleted += () => completedEvents++;
                detectionService.ReplaceInstalledPackageNamesForTests(
                    new[] { "com.deucarian.previously-installed" });

                detectionService.Refresh();
                detectionService.UpdateForTests();

                Assert.IsTrue(detectionService.HasSuccessfulRefresh);
                Assert.IsFalse(detectionService.IsRefreshing);
                Assert.AreEqual(1, completedEvents);
                Assert.IsEmpty(detectionService.InstalledPackageIds);
                Assert.IsFalse(detectionService.IsInstalled("com.deucarian.previously-installed"));
            }
        }

        [Test]
        public void FailedListRequestClearsRefreshingAndReportsFailure()
        {
            FakePackageListClient client = new FakePackageListClient(
                _ => new FakePackageListRequest(
                    isCompleted: true,
                    isSuccess: false,
                    errorMessage: "Synthetic list failure."));
            int completedEvents = 0;

            using (PackageDetectionService detectionService = new PackageDetectionService(client))
            {
                detectionService.RefreshCompleted += () => completedEvents++;
                detectionService.ReplaceInstalledPackageNamesForTests(
                    new[] { "com.deucarian.previously-installed" });

                detectionService.Refresh();
                detectionService.UpdateForTests();

                Assert.IsFalse(detectionService.HasSuccessfulRefresh);
                Assert.IsFalse(detectionService.IsRefreshing);
                Assert.IsTrue(detectionService.IsInstalled("com.deucarian.previously-installed"));
                Assert.AreEqual(1, completedEvents);
                Assert.IsNotNull(PackageInstallerActivityService.Latest);
                Assert.AreEqual("Installed Packages", PackageInstallerActivityService.Latest.Source);
                Assert.AreEqual(
                    PackageInstallerActivitySeverity.Error,
                    PackageInstallerActivityService.Latest.Severity);
                Assert.AreEqual("Synthetic list failure.", PackageInstallerActivityService.Latest.Details);
                Assert.AreEqual(
                    PackageInstallerRetryKind.Refresh,
                    PackageInstallerActivityService.Latest.RetryKind);
            }
        }

        [Test]
        public void ListStartExceptionDoesNotEscapeAndLeavesRefreshRetryable()
        {
            FakePackageListRequest retryRequest = new FakePackageListRequest(isCompleted: false);
            FakePackageListClient client = new FakePackageListClient(call =>
            {
                if (call == 1)
                {
                    throw new InvalidOperationException("Synthetic start failure.");
                }

                return retryRequest;
            });

            using (PackageDetectionService detectionService = new PackageDetectionService(client))
            {
                Assert.DoesNotThrow(detectionService.Refresh);
                Assert.IsFalse(detectionService.IsRefreshing);
                Assert.IsNotNull(PackageInstallerActivityService.Latest);
                StringAssert.Contains(
                    "Synthetic start failure.",
                    PackageInstallerActivityService.Latest.Summary);

                detectionService.Refresh();

                Assert.AreEqual(2, client.ListCalls);
                Assert.IsTrue(detectionService.IsRefreshing);
            }
        }

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

        private sealed class FakePackageListClient : IPackageListClient
        {
            private readonly Func<int, IPackageListRequest> _requestFactory;

            public FakePackageListClient(Func<int, IPackageListRequest> requestFactory)
            {
                _requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
            }

            public int ListCalls { get; private set; }

            public bool LastOfflineMode { get; private set; }

            public bool LastIncludeIndirectDependencies { get; private set; }

            public IPackageListRequest List(bool offlineMode, bool includeIndirectDependencies)
            {
                ListCalls++;
                LastOfflineMode = offlineMode;
                LastIncludeIndirectDependencies = includeIndirectDependencies;
                return _requestFactory(ListCalls);
            }
        }

        private sealed class FakePackageListRequest : IPackageListRequest
        {
            public FakePackageListRequest(
                bool isCompleted,
                bool isSuccess = false,
                string errorMessage = "",
                IEnumerable<PackageManagerPackageInfo> packages = null)
            {
                IsCompleted = isCompleted;
                IsSuccess = isSuccess;
                ErrorMessage = errorMessage ?? string.Empty;
                Packages = packages ?? Array.Empty<PackageManagerPackageInfo>();
            }

            public bool IsCompleted { get; }

            public bool IsSuccess { get; }

            public string ErrorMessage { get; }

            public IEnumerable<PackageManagerPackageInfo> Packages { get; }
        }
    }
}
