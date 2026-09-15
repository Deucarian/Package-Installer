using System;
using Deucarian.Editor;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {
        private static VisualElement CreateWorkspaceOperationStatus(Action toggle, Action cancel)
        {
            var root = DeucarianEditorWorkspaceControls.Region(OperationFooterRowName, "dw-operation-status");
            var icon = new Image { name = OperationFooterStatusIconName, pickingMode = PickingMode.Ignore };
            icon.AddToClassList("dw-icon"); root.Add(icon);
            var summary = DeucarianEditorWorkspaceControls.Label(string.Empty, "dw-muted");
            summary.name = OperationFooterSummaryName; root.Add(summary);
            var details = DeucarianEditorWorkspaceControls.Button("Show details", toggle);
            details.name = OperationFooterDetailsButtonName; root.Add(details);
            var cancelButton = DeucarianEditorWorkspaceControls.Button("Cancel", cancel);
            cancelButton.name = OperationFooterCancelButtonName; root.Add(cancelButton);
            return root;
        }
    }
}
