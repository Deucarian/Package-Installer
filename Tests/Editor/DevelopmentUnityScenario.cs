using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    // Explicit disposable validation entry only. No test attributes; no mutations unless
    // both the exact command-line flag and host-specific sentinel are present.
    public static class DevelopmentUnityScenario
    {
        internal const string Root = "D:/Codex-storage/validation/package-development-20260910/scenario";
        internal const string Consumer = "D:/Codex-storage/validation/package-development-20260910/Consumer";
        private static bool _running;

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (Allowed()) EditorApplication.delayCall += Start;
        }

        public static async void Start()
        {
            if (_running || !Allowed()) return;
            _running = true;
            try
            {
                var runner = new DevelopmentUnityScenarioRunner(Root, Consumer);
                await runner.RunAsync();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                // Fixture-only errors are retained locally for validation review; no application data.
                File.WriteAllText(Path.Combine(Root, "failure.txt"), exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        internal static bool Allowed()
        {
            if (!Application.isBatchMode || !Environment.GetCommandLineArgs().Contains("--deucarian-development-scenario")) return false;
            string actual = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!string.Equals(actual.TrimEnd('/'), Consumer, StringComparison.OrdinalIgnoreCase)) return false;
            string sentinel = Path.Combine(Root, "ALLOW");
            try { Development.DevelopmentSourceStoragePaths.RequireRegular(Path.GetDirectoryName(Root), sentinel); }
            catch (InvalidOperationException) { return false; }
            return File.Exists(sentinel) && File.ReadAllText(sentinel).Trim() == Consumer;
        }
    }

    [Serializable]
    internal sealed class DevelopmentUnityScenarioState
    {
        public string Phase;
        public string OriginalManifestHash;
        public string ConsumerHead;
        public string ConsumerIndexHash;
        public string InstalledReference;
        public string InstalledPath;
        public string CloneHead;
        public string Commit;
        public int Entries;
        public bool CloneVerified;
        public bool ReuseVerified;
        public bool LocalCompilationVerified;
        public bool EditedCompilationVerified;
        public bool SelectedCommitVerified;
        public bool FixturePushVerified;
        public bool OriginalSourceRestored;
        public bool ExactManifestRestored;
        public bool LocalWorkPreserved;
        public bool ConsumerGitUnchanged;
    }
}
