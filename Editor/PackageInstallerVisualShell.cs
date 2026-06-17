using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageInstallerVisualShell
    {
        public static Color DeepBackground
        {
            get { return EditorGUIUtility.isProSkin ? FromRgb(20, 27, 32) : FromRgb(222, 228, 232); }
        }

        public static Color MainPanel
        {
            get { return EditorGUIUtility.isProSkin ? FromRgb(29, 38, 44) : FromRgb(238, 242, 244); }
        }

        public static Color NestedSurface
        {
            get { return EditorGUIUtility.isProSkin ? FromRgb(34, 45, 52) : FromRgb(248, 250, 251); }
        }

        public static Color HeaderPanel
        {
            get { return EditorGUIUtility.isProSkin ? FromRgb(39, 51, 59) : FromRgb(232, 238, 241); }
        }

        public static Color Border
        {
            get { return EditorGUIUtility.isProSkin ? FromRgb(57, 73, 83) : FromRgb(184, 198, 206); }
        }

        public static Color InteractiveBorder
        {
            get { return DeucarianEditorColors.WithAlpha(DeucarianEditorColors.Teal, EditorGUIUtility.isProSkin ? 0.72f : 0.86f); }
        }

        public static Color SubtleBorder
        {
            get { return EditorGUIUtility.isProSkin ? FromRgb(47, 61, 70) : FromRgb(202, 213, 219); }
        }

        public static Color Text
        {
            get { return DeucarianEditorColors.BodyText; }
        }

        public static Color MutedText
        {
            get { return DeucarianEditorColors.MutedText; }
        }

        public static VisualElement CreateWindowShell(VisualElement root)
        {
            if (root == null)
            {
                return null;
            }

            root.Clear();
            DeucarianEditorUIResources.TryAddSharedStyleSheet(root);
            root.style.flexGrow = 1f;
            root.style.backgroundColor = DeepBackground;

            VisualElement content = new VisualElement();
            content.style.flexGrow = 1f;
            content.style.flexDirection = FlexDirection.Column;
            content.style.paddingLeft = 12f;
            content.style.paddingRight = 12f;
            content.style.paddingTop = 10f;
            content.style.paddingBottom = 10f;
            root.Add(content);
            return content;
        }

        public static VisualElement CreateToolbarRow()
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.flexShrink = 0f;
            toolbar.style.minHeight = 32f;
            toolbar.style.marginBottom = 8f;
            return toolbar;
        }

        public static void DrawWindowBackground(Rect rect, Color backgroundColor)
        {
            DrawRect(rect, backgroundColor);
        }

        public static void DrawFrostedSurface(Rect rect, Color backgroundColor, Color borderColor)
        {
            DrawSurface(rect, backgroundColor, borderColor);
        }

        public static void DrawInsetSurface(Rect rect, Color backgroundColor, Color borderColor, float radius)
        {
            DrawSurface(rect, backgroundColor, borderColor);
        }

        private static void DrawSurface(Rect rect, Color backgroundColor, Color borderColor)
        {
            if (!CanDraw(rect))
            {
                return;
            }

            Rect pixelRect = PixelAlign(rect);
            EditorGUI.DrawRect(pixelRect, backgroundColor);
            DrawBorder(pixelRect, borderColor);
        }

        private static void DrawRect(Rect rect, Color color)
        {
            if (!CanDraw(rect))
            {
                return;
            }

            EditorGUI.DrawRect(PixelAlign(rect), color);
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            if (rect.width < 1f || rect.height < 1f)
            {
                return;
            }

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        private static bool CanDraw(Rect rect)
        {
            return Event.current != null &&
                   Event.current.type == EventType.Repaint &&
                   rect.width > 0f &&
                   rect.height > 0f;
        }

        private static Rect PixelAlign(Rect rect)
        {
            return new Rect(
                Mathf.Round(rect.x),
                Mathf.Round(rect.y),
                Mathf.Round(rect.width),
                Mathf.Round(rect.height));
        }

        private static Color FromRgb(byte red, byte green, byte blue)
        {
            return new Color32(red, green, blue, 255);
        }
    }
}
