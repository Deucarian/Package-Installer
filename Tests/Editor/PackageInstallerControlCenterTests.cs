using System;
using Deucarian.Editor;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    public sealed class PackageInstallerControlCenterTests
    {
        [Test]
        public void CardUsesSanitizedPackageOwnedSnapshot()
        {
            PackageInstallerEditorSnapshot snapshot = new PackageInstallerEditorSnapshot(
                true, 40, false, true, 12, false, 3, 0, false,
                new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc),
                false, 0, 0, 0);

            DeucarianControlCenterCard card =
                PackageInstallerCardProvider.CreateCard(snapshot);

            Assert.AreEqual("package-installer.catalog", card.Id);
            Assert.AreEqual(DeucarianControlCenterArea.BuildAndPackages, card.Area);
            Assert.AreEqual(DeucarianControlCenterStatus.Warning, card.Status);
            Assert.AreEqual("3 update(s) available", card.StatusText);
            Assert.That(card.Details, Has.Some.Contains("12 catalog package(s) installed"));
            Assert.AreEqual("package-installer.open", card.Actions[0].Id);
        }

        [Test]
        public void OperationFailureTakesPriorityWithoutLeakingOperationPayload()
        {
            PackageInstallerEditorSnapshot snapshot = new PackageInstallerEditorSnapshot(
                true, 20, false, true, 10, false, 0, 0, false,
                null, false, 4, 5, 1);

            DeucarianControlCenterCard card =
                PackageInstallerCardProvider.CreateCard(snapshot);

            Assert.AreEqual(DeucarianControlCenterStatus.Error, card.Status);
            Assert.AreEqual("1 operation failure(s)", card.StatusText);
            StringAssert.DoesNotContain("http", string.Join(" ", card.Details));
        }
    }
}