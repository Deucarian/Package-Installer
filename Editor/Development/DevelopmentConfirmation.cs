using System;
using Deucarian.Editor;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal static class DevelopmentConfirmation
    {
        internal static void Show(DevelopmentWorkflow workflow, string title, string details, string action, Action accepted)
        {
            string scope = workflow.ConfirmationScope;
            DeucarianEditorDialog.Show(new DeucarianEditorDialogOptions(title,
                "Review this package operation before continuing.", DeucarianEditorIconIds.Warning,
                new[] {
                    new DeucarianEditorDialogAction("continue", action, DeucarianEditorIconIds.Play, DeucarianEditorDialogActionStyle.Primary),
                    new DeucarianEditorDialogAction("cancel", "Cancel", DeucarianEditorIconIds.Clear)
                }) { Details = details, DefaultActionId = "cancel", CancelActionId = "cancel" },
                result => { if (result.ActionId == "continue") workflow.AcceptConfirmation(scope, accepted); });
        }
    }
}
