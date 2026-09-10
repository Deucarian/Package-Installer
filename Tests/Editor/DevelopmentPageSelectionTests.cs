using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentPageSelectionTests
    {
        [Test]
        public void RefreshDropsRemovedAspectWithoutBlockingRemainingSelectedFiles()
        {
            var selected = new HashSet<string> { "W:reverted.cs", "W:keep.cs", "S:keep.cs" };
            var rows = new[] { Row("W:keep.cs"), Row("S:keep.cs") };
            string inspected = DevelopmentPage.ReconcileVisibleSelection(rows, selected, "W:reverted.cs");
            CollectionAssert.AreEquivalent(new[] { "W:keep.cs", "S:keep.cs" }, selected);
            Assert.That(inspected, Is.Null, "A removed row must not keep a misleading visible diff.");
            Assert.That(DevelopmentPage.ReconcileVisibleSelection(rows, selected, "S:keep.cs"), Is.EqualTo("S:keep.cs"));
        }

        [Test]
        public void RowsBeyondSharedDisplayLimitCannotRemainSelectedOrInspected()
        {
            int limit = DeucarianEditorChangeReview.MaximumVisibleChanges;
            var rows = Enumerable.Range(0, limit + 2).Select(i => Row("W:file-" + i)).ToArray();
            var selected = new HashSet<string>(rows.Select(row => row.Id));
            string inspected = DevelopmentPage.ReconcileVisibleSelection(rows, selected, rows[limit].Id);
            Assert.That(selected.Count, Is.EqualTo(limit));
            Assert.That(selected.Contains(rows[limit - 1].Id), Is.True);
            Assert.That(selected.Contains(rows[limit].Id), Is.False);
            Assert.That(inspected, Is.Null);
            DevelopmentPage.ReconcileVisibleSelection(rows.Skip(1).ToArray(), selected, null);
            Assert.That(selected.Contains(rows[limit].Id), Is.False, "A newly visible row is never selected automatically.");
        }

        [Test]
        public void EmptySnapshotClearsSelectionAndInspection()
        {
            var selected = new HashSet<string> { "W:file.cs" };
            Assert.That(DevelopmentPage.ReconcileVisibleSelection(new DeucarianEditorChangeItem[0], selected,
                "W:file.cs"), Is.Null);
            Assert.That(selected, Is.Empty);
        }

        private static DeucarianEditorChangeItem Row(string id) => new DeucarianEditorChangeItem(
            id, id.Substring(2), "Modified", id.StartsWith("S:"), false, null, null);
    }
}
