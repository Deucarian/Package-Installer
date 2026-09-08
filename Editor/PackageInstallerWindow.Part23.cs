using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {


        private void PromptForSavedOperationRecovery()
        {
            new PackageOperationRecoveryWorkflow(
                _packageInstallService, _packageDependencyInstaller, GetSelectedChannel,
                () => _confirmationState != null && _confirmationState.IsPending,
                () => this != null,
                () => _promptSavedOperationAfterDetectionRefresh = true,
                UpdateOperationFooter, TryShowManagedDialog,
                (title, message, icon) => ShowInformationDialog(title, message, icon)).Start();
        }









        internal static PackageChannel GetRecoveryRequestedChannelForTests(
            PackageOperationRecoveryRecord recovery,
            string rootPackageId,
            PackageChannel fallback)
        {
            return PackageOperationRecoveryPolicy.GetRecoveryRequestedChannel(recovery, rootPackageId, fallback);
        }



        internal static string FormatRecoveryPlanDeltaForTests(
            PackageOperationRecoveryRecord recovery,
            PackageDependencyInstallPlan freshPlan)
        {
            return PackageOperationPlanReview.FormatRecoveryPlanDelta(recovery, freshPlan);
        }
    }
}
