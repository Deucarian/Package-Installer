using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageInstallerPreviewResources
    {
        private const string SharedResourceTypeName =
            "Deucarian.Editor.DeucarianEditorUIResources, Deucarian.Editor";

        public const string UxmlPath =
            "Packages/com.deucarian.package-installer/Editor/UI/PackageInstaller/PackageInstallerPreviewWindow.uxml";
        public const string UssPath =
            "Packages/com.deucarian.package-installer/Editor/UI/PackageInstaller/PackageInstallerPreviewWindow.uss";

        public const string SharedStyleSheetPath =
            "Packages/com.deucarian.editor/Editor/Assets/Styles/DeucarianEditor.uss";
        public const string PlaceholderLogoPath =
            "Packages/com.deucarian.editor/Editor/Assets/Logos/DeucarianPlaceholderLogo.png";
        public const string PackageInstallerPlaceholderHeroPath =
            "Packages/com.deucarian.editor/Editor/Assets/Images/DeucarianPackageInstallerPlaceholderHero.png";
        public const string PackagePlaceholderIconPath =
            "Packages/com.deucarian.editor/Editor/Assets/Icons/DeucarianPackagePlaceholderIcon.png";

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
            return LoadSharedAsset<StyleSheet>("SharedStyleSheetPath", SharedStyleSheetPath);
        }

        public static Texture2D LoadPlaceholderLogo()
        {
            return LoadSharedAsset<Texture2D>("PlaceholderLogoPath", PlaceholderLogoPath);
        }

        public static Texture2D LoadPackageInstallerPlaceholderHero()
        {
            return LoadSharedAsset<Texture2D>(
                "PackageInstallerPlaceholderHeroPath",
                PackageInstallerPlaceholderHeroPath);
        }

        public static Texture2D LoadPackagePlaceholderIcon()
        {
            return LoadSharedAsset<Texture2D>("PackagePlaceholderIconPath", PackagePlaceholderIconPath);
        }

        public static bool TryAddSharedStyleSheet(VisualElement root)
        {
            if (root == null)
            {
                return false;
            }

            Type sharedType = GetSharedResourceType();
            if (sharedType != null)
            {
                MethodInfo method = sharedType.GetMethod(
                    "TryAddSharedStyleSheet",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(VisualElement) },
                    null);

                if (method != null)
                {
                    try
                    {
                        return method.Invoke(null, new object[] { root }) is bool result && result;
                    }
                    catch
                    {
                    }
                }
            }

            StyleSheet styleSheet = LoadSharedStyleSheet();
            if (styleSheet == null)
            {
                return false;
            }

            root.styleSheets.Add(styleSheet);
            return true;
        }

        private static T LoadSharedAsset<T>(string pathFieldName, string fallbackPath)
            where T : UnityEngine.Object
        {
            string path = GetSharedPath(pathFieldName, fallbackPath);

            Type sharedType = GetSharedResourceType();
            if (sharedType != null)
            {
                MethodInfo loadAssetMethod = sharedType.GetMethod(
                    "LoadAsset",
                    BindingFlags.Public | BindingFlags.Static);

                if (loadAssetMethod != null && loadAssetMethod.IsGenericMethodDefinition)
                {
                    try
                    {
                        object asset = loadAssetMethod
                            .MakeGenericMethod(typeof(T))
                            .Invoke(null, new object[] { path });

                        if (asset is T typedAsset)
                        {
                            return typedAsset;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static string GetSharedPath(string fieldName, string fallbackPath)
        {
            Type sharedType = GetSharedResourceType();
            FieldInfo field = sharedType != null
                ? sharedType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)
                : null;

            return field != null && field.GetValue(null) is string path && !string.IsNullOrWhiteSpace(path)
                ? path
                : fallbackPath;
        }

        private static Type GetSharedResourceType()
        {
            return Type.GetType(SharedResourceTypeName, false);
        }
    }
}
