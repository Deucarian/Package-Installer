using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageInstallerLoggingPreferences
    {
        internal const string VerboseConsoleLoggingKeyPrefix =
            "Deucarian.PackageInstaller.VerboseConsoleLogging.";

        public static bool VerboseConsoleLogging
        {
            get => EditorPrefs.GetBool(VerboseConsoleLoggingKey, false);
            set => EditorPrefs.SetBool(VerboseConsoleLoggingKey, value);
        }

        internal static string VerboseConsoleLoggingKey =>
            VerboseConsoleLoggingKeyPrefix + Application.dataPath.Replace("\\", "/");
    }
}
