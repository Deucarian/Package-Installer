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

        private void TryPromptForSavedOperationRecovery()
        {
            if (!_promptSavedOperationAfterDetectionRefresh ||
                (_packageDetectionService != null && _packageDetectionService.IsRefreshing) ||
                (_packageDetectionService != null && !_packageDetectionService.HasSuccessfulRefresh) ||
                PackageRegistryProvider.IsRemoteRefreshing)
            {
                return;
            }

            _promptSavedOperationAfterDetectionRefresh = false;
            PromptForSavedOperationRecovery();
        }
    }
}
