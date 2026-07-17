using NUnit.Framework;
using System;
using Deucarian.Editor;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageInstallerActivityServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            PackageInstallerActivityService.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PackageInstallerActivityService.ClearForTests();
        }

        [Test]
        public void LatestActivityUsesSequenceInsteadOfServicePrecedence()
        {
            PackageInstallerActivityService.Record(
                "Samples",
                PackageInstallerActivitySeverity.Success,
                "Old sample import completed.");
            PackageInstallerActivityEntry latest = PackageInstallerActivityService.Record(
                "Update Check",
                PackageInstallerActivitySeverity.Error,
                "New update check failed.",
                retryKind: PackageInstallerRetryKind.CheckUpdates);

            Assert.AreSame(latest, PackageInstallerActivityService.Latest);
            Assert.AreEqual("New update check failed.", PackageInstallerActivityService.Latest.Summary);
            Assert.AreEqual(PackageInstallerRetryKind.CheckUpdates, PackageInstallerActivityService.Latest.RetryKind);
        }

        [Test]
        public void ActivityReportPreservesChronologicalOrder()
        {
            PackageInstallerActivityService.Record(
                "Packages",
                PackageInstallerActivitySeverity.Success,
                "Install completed.",
                "Completed: Core");
            PackageInstallerActivityService.Record(
                "Samples",
                PackageInstallerActivitySeverity.Error,
                "Sample failed.",
                "Synthetic copy failure.",
                retryKind: PackageInstallerRetryKind.ImportSample);

            string report = PackageInstallerWindow.FormatActivityReportForTests(
                PackageInstallerActivityService.Recent);

            Assert.That(
                report.IndexOf("Install completed.", StringComparison.Ordinal),
                Is.LessThan(report.IndexOf("Sample failed.", StringComparison.Ordinal)));
            StringAssert.Contains("Completed: Core", report);
            StringAssert.Contains("Synthetic copy failure.", report);
        }

        [Test]
        public void ActivityReportKeepsLiveOperationAheadOfChronologicalHistory()
        {
            PackageInstallerActivityService.Record(
                "Packages",
                PackageInstallerActivitySeverity.Error,
                "Earlier install failed.",
                "Failed: Earlier package");

            string report = PackageInstallerWindow.MergeLiveOperationWithActivityForTests(
                "Installing Current package...\nActive: Current package",
                PackageInstallerActivityService.Recent);

            int currentHeading = report.IndexOf("Current", StringComparison.Ordinal);
            int liveProgress = report.IndexOf("Active: Current package", StringComparison.Ordinal);
            int historyHeading = report.IndexOf("History", StringComparison.Ordinal);
            int earlierActivity = report.IndexOf("Earlier install failed.", StringComparison.Ordinal);
            Assert.That(currentHeading, Is.GreaterThanOrEqualTo(0));
            Assert.That(currentHeading, Is.LessThan(liveProgress));
            Assert.That(liveProgress, Is.LessThan(historyHeading));
            Assert.That(historyHeading, Is.LessThan(earlierActivity));
        }

        [Test]
        public void TerminalOperationRetryRemainsContextualAfterLaterRefreshActivity()
        {
            PackageInstallerActivityService.Record(
                "Packages",
                PackageInstallerActivitySeverity.Error,
                "Install failed.",
                retryKind: PackageInstallerRetryKind.RestartOperation);
            PackageInstallerActivityEntry refresh = PackageInstallerActivityService.Record(
                "Installed Packages",
                PackageInstallerActivitySeverity.Error,
                "Refresh failed.",
                retryKind: PackageInstallerRetryKind.Refresh);
            PackageOperationTerminalSnapshot snapshot = new PackageOperationTerminalSnapshot(
                "operation-id",
                "Install Example",
                PackageOperationTerminalOutcome.Failed,
                "Install failed.",
                "Synthetic failure.",
                new[]
                {
                    new PackageOperationRootRequest(
                        "com.deucarian.example",
                        PackageChannel.Stable)
                },
                new[]
                {
                    new PackageOperationStepSnapshot(
                        "com.deucarian.example",
                        "Example",
                        PackageChannel.Stable,
                        "https://example.com/Example.git#main",
                        isDependency: false,
                        rootPackageIds: new[] { "com.deucarian.example" },
                        state: PackageInstallProgressItemState.Failed,
                        message: "Synthetic failure.")
                },
                Array.Empty<string>(),
                DateTime.UtcNow);

            Assert.AreSame(refresh, PackageInstallerActivityService.Latest);
            Assert.AreEqual(
                PackageInstallerRetryKind.RestartOperation,
                PackageInstallerWindow.ResolveContextualRetryKindForTests(refresh, snapshot));
            Assert.AreEqual(
                PackageInstallerRetryKind.Refresh,
                PackageInstallerWindow.ResolveContextualRetryKindForTests(refresh, null));

            Button retryButton = DeucarianEditorWorkbenchSurfaces.CreateDrawerAction(
                DeucarianEditorIconIds.Refresh,
                "Retry",
                null,
                "Retry the latest failed or canceled activity.");
            PackageInstallerWindow.ApplyContextualRetryButtonStateForTests(
                retryButton,
                PackageInstallerRetryKind.RestartOperation,
                isBusy: false);
            Assert.AreEqual(DisplayStyle.Flex, retryButton.style.display.value);
            Assert.AreEqual(
                "Retry package operation",
                retryButton.Q<Label>(className: DeucarianEditorIconTextButton.LabelClass)?.text);
            StringAssert.Contains("replan", retryButton.tooltip);
        }

        [Test]
        public void CompletedPackageOperationIsRecordedOnce()
        {
            using (PackageInstallService service = new PackageInstallService())
            {
                service.RecordCompletedOperation(
                    "Install Example",
                    "Everything is already installed.",
                    new[] { "No changes required." });

                Assert.AreEqual(1, PackageInstallerActivityService.Recent.Count);
                Assert.AreEqual("Packages", PackageInstallerActivityService.Latest.Source);
                Assert.AreEqual(
                    "Everything is already installed.",
                    PackageInstallerActivityService.Latest.Summary);
            }
        }
    }
}
