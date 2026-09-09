using System;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageInstallerVisualStatusKind
    {
        Installed,
        NotInstalled,
        UpdateAvailable,
        Failed,
        Busy,
        Info,
        Integration
    }
}
