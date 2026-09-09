using System;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageInstallerStatusPresentation
    {
        internal static Color GetStatusColor(PackageInstallerVisualStatusKind statusKind)
        {
            return DeucarianEditorStatusBadge.GetColor(ToEditorStatus(statusKind));
        }

        internal static string GetStatusIconId(PackageInstallerVisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case PackageInstallerVisualStatusKind.Installed:
                    return DeucarianEditorIconIds.Success;
                case PackageInstallerVisualStatusKind.NotInstalled:
                    return DeucarianEditorIconIds.Optional;
                case PackageInstallerVisualStatusKind.UpdateAvailable:
                    return DeucarianEditorIconIds.Update;
                case PackageInstallerVisualStatusKind.Failed:
                    return DeucarianEditorIconIds.Error;
                case PackageInstallerVisualStatusKind.Busy:
                    return DeucarianEditorIconIds.Busy;
                case PackageInstallerVisualStatusKind.Integration:
                    return DeucarianEditorIconIds.Integration;
                case PackageInstallerVisualStatusKind.Info:
                default:
                    return DeucarianEditorIconIds.Info;
            }
        }

        internal static DeucarianEditorStatus ToEditorStatus(PackageInstallerVisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case PackageInstallerVisualStatusKind.Installed:
                    return DeucarianEditorStatus.Success;
                case PackageInstallerVisualStatusKind.UpdateAvailable:
                    return DeucarianEditorStatus.Warning;
                case PackageInstallerVisualStatusKind.Failed:
                    return DeucarianEditorStatus.Error;
                case PackageInstallerVisualStatusKind.NotInstalled:
                    return DeucarianEditorStatus.Disabled;
                case PackageInstallerVisualStatusKind.Busy:
                case PackageInstallerVisualStatusKind.Info:
                case PackageInstallerVisualStatusKind.Integration:
                default:
                    return DeucarianEditorStatus.Info;
            }
        }

        internal static MessageType ToMessageType(PackageInstallerVisualStatusKind statusKind)
        {
            switch (statusKind)
            {
                case PackageInstallerVisualStatusKind.Failed:
                    return MessageType.Error;
                case PackageInstallerVisualStatusKind.UpdateAvailable:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }
    }
}
