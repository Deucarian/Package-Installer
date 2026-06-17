using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageInstallerPreviewResources
    {
        public const string UxmlPath =
            "Packages/com.deucarian.package-installer/Editor/UI/PackageInstaller/PackageInstallerPreviewWindow.uxml";
        public const string UssPath =
            "Packages/com.deucarian.package-installer/Editor/UI/PackageInstaller/PackageInstallerPreviewWindow.uss";

        public static VisualTreeAsset LoadVisualTree()
        {
            return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        }

        public static StyleSheet LoadPreviewStyleSheet()
        {
            return AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
        }

        public static StyleSheet LoadSharedStyleSheet()
        {
            return DeucarianEditorUIResources.LoadSharedStyleSheet();
        }

        public static Texture2D LoadPlaceholderLogo()
        {
            return DeucarianEditorUIResources.LoadPlaceholderLogo();
        }

        public static Texture2D LoadPackageInstallerPlaceholderHero()
        {
            return DeucarianEditorUIResources.LoadPackageInstallerPlaceholderHero();
        }

        public static Texture2D LoadPackagePlaceholderIcon()
        {
            return DeucarianEditorUIResources.LoadPackagePlaceholderIcon();
        }

        public static bool TryAddSharedStyleSheet(VisualElement root)
        {
            return DeucarianEditorUIResources.TryAddSharedStyleSheet(root);
        }
    }
}
