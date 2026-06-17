using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageInstallerTheme
    {
        private const string BackgroundAssetPath =
            "Packages/com.deucarian.package-installer/Editor/Assets/deucarian-installer-background.png";
        private const string BackgroundAssetFileName = "deucarian-installer-background.png";
        private const float SurfaceRadius = 8f;
        private const float BackgroundImageAlpha = 0.58f;

        private static bool _backgroundLoadAttempted;
        private static Texture2D _backgroundTexture;

        public static void DrawWindowBackground(Rect rect, Color fallbackColor)
        {
            Event currentEvent = Event.current;

            if (currentEvent == null || currentEvent.type != EventType.Repaint || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            EditorGUI.DrawRect(rect, fallbackColor);

            Texture2D backgroundTexture = GetBackgroundTexture();

            if (backgroundTexture == null)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, BackgroundImageAlpha);
            GUI.DrawTexture(rect, backgroundTexture, ScaleMode.ScaleAndCrop, true);
            GUI.color = previousColor;

            EditorGUI.DrawRect(rect, new Color(0.005f, 0.014f, 0.030f, 0.24f));
        }

        public static void DrawFrostedSurface(Rect rect, Color backgroundColor, Color borderColor)
        {
            DrawRoundedSurface(rect, backgroundColor, borderColor, SurfaceRadius, true);
        }

        public static void DrawInsetSurface(Rect rect, Color backgroundColor, Color borderColor, float radius)
        {
            DrawRoundedSurface(rect, backgroundColor, borderColor, radius, false);
        }

        private static Texture2D GetBackgroundTexture()
        {
            if (_backgroundLoadAttempted)
            {
                return _backgroundTexture;
            }

            _backgroundLoadAttempted = true;
            _backgroundTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundAssetPath);

            if (_backgroundTexture != null)
            {
                return _backgroundTexture;
            }

            string[] guids = AssetDatabase.FindAssets("deucarian-installer-background t:Texture2D");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!string.IsNullOrWhiteSpace(path) &&
                    path.EndsWith("/" + BackgroundAssetFileName, System.StringComparison.OrdinalIgnoreCase))
                {
                    _backgroundTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                    if (_backgroundTexture != null)
                    {
                        break;
                    }
                }
            }

            return _backgroundTexture;
        }

        private static void DrawRoundedSurface(
            Rect rect,
            Color backgroundColor,
            Color borderColor,
            float radius,
            bool drawShadow)
        {
            Event currentEvent = Event.current;

            if (currentEvent == null || currentEvent.type != EventType.Repaint || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Rect alignedRect = AlignToPixels(rect);
            radius = Mathf.Min(radius, Mathf.Min(alignedRect.width, alignedRect.height) * 0.5f);

            if (drawShadow)
            {
                Rect shadowRect = new Rect(
                    alignedRect.x + 1f,
                    alignedRect.y + 2f,
                    alignedRect.width,
                    alignedRect.height);
                DrawRoundedFill(shadowRect, radius, new Color(0f, 0f, 0f, 0.14f));
            }

            DrawRoundedFill(alignedRect, radius, borderColor);

            Rect innerRect = new Rect(
                alignedRect.x + 1f,
                alignedRect.y + 1f,
                Mathf.Max(0f, alignedRect.width - 2f),
                Mathf.Max(0f, alignedRect.height - 2f));
            DrawRoundedFill(innerRect, Mathf.Max(0f, radius - 1f), backgroundColor);

            if (innerRect.width > 8f && innerRect.height > 2f)
            {
                DrawRoundedFill(
                    new Rect(innerRect.x + radius, innerRect.y, innerRect.width - radius * 2f, 1f),
                    0f,
                    new Color(0.75f, 0.94f, 1f, 0.08f));
            }
        }

        private static Rect AlignToPixels(Rect rect)
        {
            return new Rect(
                Mathf.Floor(rect.x),
                Mathf.Floor(rect.y),
                Mathf.Ceil(rect.width),
                Mathf.Ceil(rect.height));
        }

        private static void DrawRoundedFill(Rect rect, float radius, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f || color.a <= 0f)
            {
                return;
            }

            radius = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);

            if (radius < 1f)
            {
                EditorGUI.DrawRect(rect, color);
                return;
            }

            float middleWidth = Mathf.Max(0f, rect.width - radius * 2f);
            float middleHeight = Mathf.Max(0f, rect.height - radius * 2f);

            if (middleWidth > 0f)
            {
                EditorGUI.DrawRect(new Rect(rect.x + radius, rect.y, middleWidth, rect.height), color);
            }

            if (middleHeight > 0f)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + radius, radius, middleHeight), color);
                EditorGUI.DrawRect(new Rect(rect.xMax - radius, rect.y + radius, radius, middleHeight), color);
            }

            int rows = Mathf.CeilToInt(radius);
            float radiusSquared = radius * radius;

            for (int row = 0; row < rows; row++)
            {
                float sample = radius - row - 0.5f;
                float inset = radius - Mathf.Sqrt(Mathf.Max(0f, radiusSquared - sample * sample));
                float width = rect.width - inset * 2f;

                if (width <= 0f)
                {
                    continue;
                }

                EditorGUI.DrawRect(new Rect(rect.x + inset, rect.y + row, width, 1f), color);
                EditorGUI.DrawRect(new Rect(rect.x + inset, rect.yMax - row - 1f, width, 1f), color);
            }
        }
    }
}
