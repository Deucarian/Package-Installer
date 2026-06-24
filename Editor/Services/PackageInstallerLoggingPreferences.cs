using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageInstallerLoggingPreferences
    {
        internal const string VerboseConsoleLoggingKeyPrefix =
            "Deucarian.PackageInstaller.VerboseConsoleLogging.";
        internal const string GraphOpenDiagnosticsLoggingKeyPrefix =
            "Deucarian.PackageInstaller.GraphOpenDiagnosticsLogging.";

        public static bool VerboseConsoleLogging
        {
            get => EditorPrefs.GetBool(VerboseConsoleLoggingKey, false);
            set => EditorPrefs.SetBool(VerboseConsoleLoggingKey, value);
        }

        public static bool GraphOpenDiagnosticsLogging
        {
            get => EditorPrefs.GetBool(GraphOpenDiagnosticsLoggingKey, false);
            set => EditorPrefs.SetBool(GraphOpenDiagnosticsLoggingKey, value);
        }

        internal static string VerboseConsoleLoggingKey =>
            VerboseConsoleLoggingKeyPrefix + Application.dataPath.Replace("\\", "/");

        internal static string GraphOpenDiagnosticsLoggingKey =>
            GraphOpenDiagnosticsLoggingKeyPrefix + Application.dataPath.Replace("\\", "/");
    }
}
