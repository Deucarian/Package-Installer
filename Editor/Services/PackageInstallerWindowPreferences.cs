using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageInstallerWindowPreferences
    {
        private const string Prefix = "Deucarian.PackageInstaller.";

        internal bool OperationDetailsExpanded
        {
            get => Read("OperationDrawer", "", false);
            set => Write("OperationDrawer", "", value);
        }

        internal bool IsAdvancedExpanded(string packageId) => Read("AdvancedFoldout", packageId, false);
        internal void SetAdvancedExpanded(string packageId, bool value) => Write("AdvancedFoldout", packageId, value);
        internal bool IsCategoryExpanded(string category) => Read("CategoryFoldout", category?.Trim(), true);
        internal void SetCategoryExpanded(string category, bool value) => Write("CategoryFoldout", category?.Trim(), value);

        private static bool Read(string area, string item, bool fallback)
        {
            string key = Prefix + area + "." + item;
            string scoped = DeucarianEditorProjectPreferences.Key(key);
            if (EditorPrefs.HasKey(scoped)) return DeucarianEditorProjectPreferences.GetBool(key, fallback);
            string legacy = Prefix + area + "." + Application.dataPath.Replace('\\', '/');
            if (!string.IsNullOrEmpty(item)) legacy += "." + item;
            return EditorPrefs.GetBool(legacy, fallback);
        }

        private static void Write(string area, string item, bool value)
            => DeucarianEditorProjectPreferences.SetBool(Prefix + area + "." + item, value);
    }
}
