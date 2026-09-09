using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_2022_1_OR_NEWER
using PackageGraphPainter = UnityEngine.UIElements.Painter2D;
#else
using PackageGraphPainter = Deucarian.PackageInstaller.Editor.PackageGraphMeshPainter;
#endif

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphSpotlightLayer : VisualElement
    {
        public const string LayerName = "ecosystem-graph-spotlight-layer";
        private const string SpotlightName = "ecosystem-graph-spotlight";

        private static Texture2D _rootTexture;
        private static Texture2D _categoryTexture;
        private static Texture2D _packageTexture;
        private static Texture2D _attentionTexture;

        private readonly VisualElement _spotlight;

        public PackageGraphSpotlightLayer()
        {
            name = LayerName;
            AddToClassList("dpi-graph-spotlight-layer");
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0f;
            style.right = 0f;
            style.top = 0f;
            style.bottom = 0f;
            style.overflow = Overflow.Hidden;

            _spotlight = new VisualElement { name = SpotlightName };
            _spotlight.AddToClassList("dpi-graph-spotlight");
            _spotlight.pickingMode = PickingMode.Ignore;
            _spotlight.style.position = Position.Absolute;
            _spotlight.style.display = DisplayStyle.None;
            Add(_spotlight);
        }

        internal VisualElement SpotlightForTests => _spotlight;

        public void SetSpotlight(
            Vector2 viewportCenter,
            float radius,
            PackageGraphSpotlightKind kind,
            bool visible)
        {
            if (!visible || kind == PackageGraphSpotlightKind.None || radius <= 1f)
            {
                _spotlight.style.display = DisplayStyle.None;
                return;
            }

            float diameter = radius * 2f;
            _spotlight.style.display = DisplayStyle.Flex;
            _spotlight.style.left = viewportCenter.x - radius;
            _spotlight.style.top = viewportCenter.y - radius;
            _spotlight.style.width = diameter;
            _spotlight.style.height = diameter;
            _spotlight.style.backgroundImage = new StyleBackground(GetTexture(kind));
            _spotlight.style.opacity = GetOpacity(kind);
            _spotlight.EnableInClassList("dpi-graph-spotlight--root", kind == PackageGraphSpotlightKind.Root);
            _spotlight.EnableInClassList("dpi-graph-spotlight--category", kind == PackageGraphSpotlightKind.Category);
            _spotlight.EnableInClassList("dpi-graph-spotlight--package", kind == PackageGraphSpotlightKind.Package);
            _spotlight.EnableInClassList("dpi-graph-spotlight--attention", kind == PackageGraphSpotlightKind.Attention);
            MarkDirtyRepaint();
        }

        private static float GetOpacity(PackageGraphSpotlightKind kind)
        {
            switch (kind)
            {
                case PackageGraphSpotlightKind.Root:
                    return 0.28f;
                case PackageGraphSpotlightKind.Category:
                    return 0.38f;
                case PackageGraphSpotlightKind.Attention:
                    return 0.44f;
                case PackageGraphSpotlightKind.Package:
                    return 0.42f;
                default:
                    return 0f;
            }
        }

        private static Texture2D GetTexture(PackageGraphSpotlightKind kind)
        {
            switch (kind)
            {
                case PackageGraphSpotlightKind.Root:
                    return _rootTexture ?? (_rootTexture = CreateSpotlightTexture(
                        DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorPalette.Cobalt, 0.24f),
                        DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Canvas, 0f)));
                case PackageGraphSpotlightKind.Category:
                    return _categoryTexture ?? (_categoryTexture = CreateSpotlightTexture(
                        DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorPalette.Grove, 0.24f),
                        DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Canvas, 0f)));
                case PackageGraphSpotlightKind.Attention:
                    return _attentionTexture ?? (_attentionTexture = CreateSpotlightTexture(
                        DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Update, 0.26f),
                        DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Canvas, 0f)));
                case PackageGraphSpotlightKind.Package:
                default:
                    return _packageTexture ?? (_packageTexture = CreateSpotlightTexture(
                        DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorPalette.Tideline, 0.28f),
                        DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Canvas, 0f)));
            }
        }

        private static Texture2D CreateSpotlightTexture(Color center, Color edge)
        {
            const int size = 96;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PackageGraphSpotlight",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 midpoint = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float maxDistance = Mathf.Max(1f, midpoint.magnitude);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), midpoint) / maxDistance;
                    float t = Mathf.SmoothStep(0f, 1f, distance);
                    texture.SetPixel(x, y, Color.Lerp(center, edge, t));
                }
            }

            texture.Apply(false, true);
            return texture;
        }
    }
}
