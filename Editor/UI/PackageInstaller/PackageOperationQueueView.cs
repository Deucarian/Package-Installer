using System;
using System.Collections.Generic;
using Deucarian.Editor;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageOperationQueueView
    {
        internal readonly VisualElement Root = DeucarianEditorWorkspaceControls.Panel("installer-operation-queue");
        private readonly Label title = DeucarianEditorWorkspaceControls.Label("", "dw-section-title");
        private readonly Label summary = DeucarianEditorWorkspaceControls.Label("", "dw-muted");
        private readonly Label recovery = DeucarianEditorWorkspaceControls.Label(
            "Restoring the saved queue after Unity reload. Completed packages are kept.", "dw-muted");
        private readonly VisualElement fill = DeucarianEditorWorkspaceControls.Region(null, "dw-progress-fill");
        private readonly ScrollView scroll = DeucarianEditorWorkspaceControls.Scroll("installer-operation-queue-items");
        private readonly List<Label> rows = new List<Label>();

        internal PackageOperationQueueView()
        {
            title.name = "installer-operation-queue-title";
            summary.name = "installer-operation-queue-summary";
            Root.Add(title); Root.Add(summary); Root.Add(recovery);
            var track = DeucarianEditorWorkspaceControls.Region(null, "dw-progress");
            track.Add(fill); Root.Add(track); Root.Add(scroll);
            // Keep collection and package actions usable, including in a narrow dock.
            scroll.style.maxHeight = 144;
            scroll.style.minHeight = 36;
            Root.style.flexShrink = 0;
            DeucarianEditorWorkspaceControls.Show(Root, false);
        }

        internal void Refresh(PackageOperationQueueSnapshot snapshot)
        {
            var items = snapshot?.items ?? Array.Empty<PackageOperationQueueItem>();
            DeucarianEditorWorkspaceControls.Show(Root, items.Length > 0);
            if (items.Length == 0) return;
            title.text = (snapshot.operationName ?? "Package operation") + " · " + items.Length + " packages";
            summary.text = snapshot.Summary;
            DeucarianEditorWorkspaceControls.Show(recovery, snapshot.waitingForRecovery);
            fill.style.width = Length.Percent(snapshot.Progress * 100f);
            fill.parent.tooltip = "Finished package steps: " + (int)(snapshot.Progress * items.Length) + " / " + items.Length;
            while (rows.Count > items.Length)
            {
                rows[rows.Count - 1].RemoveFromHierarchy();
                rows.RemoveAt(rows.Count - 1);
            }
            for (int i = 0; i < items.Length; i++)
            {
                if (rows.Count <= i)
                {
                    var row = DeucarianEditorWorkspaceControls.Label("", "dw-label");
                    rows.Add(row); scroll.Add(row);
                }
                var item = items[i];
                rows[i].name = "installer-operation-step-" + item.packageId;
                rows[i].text = (i + 1) + ". " + item.displayName + " · " + item.Status;
                rows[i].tooltip = item.packageId;
            }
        }
    }
}
