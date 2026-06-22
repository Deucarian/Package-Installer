using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageInstallerAmbientMotionMode
    {
        On,
        Reduced,
        Off
    }

    internal static class PackageInstallerAmbientMotionSettings
    {
        private const string PreferenceKey = "Deucarian.PackageInstaller.AmbientUIMotion";
        private static PackageInstallerAmbientMotionMode? _overrideMode;

        public static PackageInstallerAmbientMotionMode CurrentMode
        {
            get
            {
                if (_overrideMode.HasValue)
                {
                    return _overrideMode.Value;
                }

                string stored = EditorPrefs.GetString(PreferenceKey, PackageInstallerAmbientMotionMode.On.ToString());
                return Enum.TryParse(stored, out PackageInstallerAmbientMotionMode mode)
                    ? mode
                    : PackageInstallerAmbientMotionMode.On;
            }
        }

        public static float MotionScale
        {
            get
            {
                switch (CurrentMode)
                {
                    case PackageInstallerAmbientMotionMode.Off:
                        return 0f;
                    case PackageInstallerAmbientMotionMode.Reduced:
                        return 0.25f;
                    default:
                        return 1f;
                }
            }
        }

        internal static void SetModeForTests(PackageInstallerAmbientMotionMode? mode)
        {
            _overrideMode = mode;
        }
    }

    internal static class PackageInstallerAmbientGlass
    {
        public const string AmbientLayerName = "package-installer-ambient-lighting-layer";
        public const string GrainLayerName = "package-installer-grain-layer";
        public const string VignetteLayerName = "package-installer-vignette-layer";
        public const string AmbientGlowAName = "package-installer-ambient-glow-a";
        public const string AmbientGlowBName = "package-installer-ambient-glow-b";
        public const string AmbientGlowCName = "package-installer-ambient-glow-c";

        private static Texture2D _tealGlowTexture;
        private static Texture2D _blueGlowTexture;
        private static Texture2D _indigoGlowTexture;
        private static Texture2D _grainTexture;
        private static Texture2D _vignetteTexture;

        public static void Install(VisualElement host)
        {
            if (host == null || host.Q<VisualElement>(AmbientLayerName) != null)
            {
                return;
            }

            PackageInstallerAmbientLayer ambientLayer = new PackageInstallerAmbientLayer
            {
                name = AmbientLayerName
            };
            ambientLayer.AddToClassList("dpi-ambient-lighting-layer");
            ConfigureFixedDecorativeLayer(ambientLayer);
            ambientLayer.AddGlow(
                AmbientGlowAName,
                "dpi-ambient-glow--teal",
                GetTealGlowTexture());
            ambientLayer.AddGlow(
                AmbientGlowBName,
                "dpi-ambient-glow--blue",
                GetBlueGlowTexture());
            ambientLayer.AddGlow(
                AmbientGlowCName,
                "dpi-ambient-glow--indigo",
                GetIndigoGlowTexture());

            VisualElement grain = new VisualElement
            {
                name = GrainLayerName
            };
            grain.AddToClassList("dpi-grain-layer");
            ConfigureFixedDecorativeLayer(grain);
            grain.style.backgroundImage = new StyleBackground(GetGrainTexture());

            VisualElement vignette = new VisualElement
            {
                name = VignetteLayerName
            };
            vignette.AddToClassList("dpi-vignette-layer");
            ConfigureFixedDecorativeLayer(vignette);
            vignette.style.backgroundImage = new StyleBackground(GetVignetteTexture());

            InsertAt(host, ambientLayer, 1);
            InsertAt(host, grain, 2);
            InsertAt(host, vignette, 3);
        }

        internal static void ConfigureFixedDecorativeLayer(VisualElement layer)
        {
            if (layer == null)
            {
                return;
            }

            layer.pickingMode = PickingMode.Ignore;
            layer.style.position = Position.Absolute;
            layer.style.left = 0f;
            layer.style.right = 0f;
            layer.style.top = 0f;
            layer.style.bottom = 0f;
        }

        internal static Texture2D CreateRadialGlowTextureForTests(Color center, Color edge)
        {
            return CreateRadialTexture(32, center, edge, 1.35f);
        }

        private static void InsertAt(VisualElement host, VisualElement layer, int index)
        {
            host.Insert(Mathf.Clamp(index, 0, host.childCount), layer);
        }

        private static Texture2D GetTealGlowTexture()
        {
            return _tealGlowTexture ?? (_tealGlowTexture = CreateRadialTexture(
                128,
                new Color(0.10f, 0.78f, 0.72f, 0.28f),
                new Color(0.02f, 0.06f, 0.08f, 0.00f),
                1.45f));
        }

        private static Texture2D GetBlueGlowTexture()
        {
            return _blueGlowTexture ?? (_blueGlowTexture = CreateRadialTexture(
                128,
                new Color(0.16f, 0.46f, 0.88f, 0.24f),
                new Color(0.01f, 0.04f, 0.08f, 0.00f),
                1.55f));
        }

        private static Texture2D GetIndigoGlowTexture()
        {
            return _indigoGlowTexture ?? (_indigoGlowTexture = CreateRadialTexture(
                128,
                new Color(0.30f, 0.25f, 0.78f, 0.16f),
                new Color(0.02f, 0.02f, 0.08f, 0.00f),
                1.65f));
        }

        private static Texture2D GetGrainTexture()
        {
            if (_grainTexture != null)
            {
                return _grainTexture;
            }

            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PackageInstallerAmbientGrain",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    uint hash = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ 0x9E3779B9u;
                    hash ^= hash >> 13;
                    hash *= 1274126177u;
                    float alpha = ((hash & 0xFFu) / 255f) * 0.045f;
                    texture.SetPixel(x, y, new Color(0.72f, 0.86f, 0.92f, alpha));
                }
            }

            texture.Apply(false, true);
            _grainTexture = texture;
            return _grainTexture;
        }

        private static Texture2D GetVignetteTexture()
        {
            return _vignetteTexture ?? (_vignetteTexture = CreateVignetteTexture());
        }

        private static Texture2D CreateRadialTexture(int size, Color center, Color edge, float falloff)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PackageInstallerAmbientGlow",
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
                    float t = Mathf.Clamp01(Mathf.Pow(distance, falloff));
                    texture.SetPixel(x, y, Color.Lerp(center, edge, t));
                }
            }

            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateVignetteTexture()
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PackageInstallerAmbientVignette",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 midpoint = new Vector2((size - 1) * 0.48f, (size - 1) * 0.45f);
            float maxDistance = Mathf.Max(1f, midpoint.magnitude);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2(x, y);
                    float distance = Vector2.Distance(point, midpoint) / maxDistance;
                    float edge = Mathf.SmoothStep(0.45f, 1.18f, distance);
                    float rightBias = Mathf.InverseLerp(size * 0.42f, size, x) * 0.045f;
                    float bottomBias = Mathf.InverseLerp(size * 0.48f, size, y) * 0.055f;
                    float alpha = Mathf.Clamp01(edge * 0.24f + rightBias + bottomBias);
                    texture.SetPixel(x, y, new Color(0.00f, 0.02f, 0.04f, alpha));
                }
            }

            texture.Apply(false, true);
            return texture;
        }
    }

    internal sealed class PackageInstallerAmbientLayer : VisualElement
    {
        private const long UpdateIntervalMs = 120;

        private readonly VisualElement[] _glows = new VisualElement[3];
        private IVisualElementScheduledItem _animationItem;

        public PackageInstallerAmbientLayer()
        {
            pickingMode = PickingMode.Ignore;
            RegisterCallback<AttachToPanelEvent>(_ => StartAnimation());
            RegisterCallback<DetachFromPanelEvent>(_ => PauseAnimation());
        }

        public void AddGlow(string glowName, string className, Texture2D texture)
        {
            int index = Mathf.Clamp(childCount, 0, _glows.Length - 1);
            VisualElement glow = new VisualElement
            {
                name = glowName
            };
            glow.AddToClassList("dpi-ambient-glow");
            glow.AddToClassList(className);
            glow.pickingMode = PickingMode.Ignore;
            glow.style.backgroundImage = new StyleBackground(texture);
            Add(glow);
            _glows[index] = glow;
        }

        internal bool HasScheduledAnimationForTests => _animationItem != null;

        private void StartAnimation()
        {
            if (PackageInstallerAmbientMotionSettings.CurrentMode == PackageInstallerAmbientMotionMode.Off)
            {
                ApplyStaticFrame(0f);
                return;
            }

            if (_animationItem == null)
            {
                _animationItem = schedule.Execute(UpdateAnimation).Every(UpdateIntervalMs);
            }

            _animationItem.Resume();
            UpdateAnimation();
        }

        private void PauseAnimation()
        {
            _animationItem?.Pause();
        }

        private void UpdateAnimation()
        {
            if (panel == null || !IsVisibleInPanel())
            {
                PauseAnimation();
                return;
            }

            float motionScale = PackageInstallerAmbientMotionSettings.MotionScale;
            ApplyStaticFrame((float)EditorApplication.timeSinceStartup * motionScale);
            MarkDirtyRepaint();
        }

        private void ApplyStaticFrame(float time)
        {
            ApplyGlow(
                _glows[0],
                Mathf.Sin(time * 0.18f) * 22f,
                Mathf.Cos(time * 0.13f) * 18f,
                1.00f + Mathf.Sin(time * 0.11f) * 0.025f,
                0.52f + Mathf.Sin(time * 0.15f) * 0.035f);
            ApplyGlow(
                _glows[1],
                Mathf.Cos(time * 0.14f + 1.7f) * 20f,
                Mathf.Sin(time * 0.10f + 0.6f) * 16f,
                1.02f + Mathf.Cos(time * 0.09f) * 0.02f,
                0.42f + Mathf.Sin(time * 0.12f + 2.1f) * 0.025f);
            ApplyGlow(
                _glows[2],
                Mathf.Sin(time * 0.09f + 2.4f) * 14f,
                Mathf.Cos(time * 0.16f + 1.2f) * 12f,
                0.98f + Mathf.Sin(time * 0.08f) * 0.02f,
                0.34f + Mathf.Cos(time * 0.11f + 0.4f) * 0.02f);
        }

        private static void ApplyGlow(VisualElement glow, float x, float y, float scale, float opacity)
        {
            if (glow == null)
            {
                return;
            }

            glow.style.translate = new Translate(x, y, 0f);
            glow.style.scale = new Scale(new Vector3(scale, scale, 1f));
            glow.style.opacity = Mathf.Clamp01(opacity);
        }

        private bool IsVisibleInPanel()
        {
            VisualElement current = this;

            while (current != null)
            {
                if (current.resolvedStyle.display == DisplayStyle.None ||
                    current.resolvedStyle.visibility == Visibility.Hidden)
                {
                    return false;
                }

                current = current.parent;
            }

            return true;
        }
    }

    internal static class PackageInstallerGlassSheen
    {
        private const float DurationSeconds = 0.34f;
        private const long FrameIntervalMs = 24;

        public static VisualElement Create()
        {
            VisualElement sheen = new VisualElement();
            sheen.AddToClassList("dpi-glass-sheen");
            sheen.pickingMode = PickingMode.Ignore;
            sheen.style.opacity = 0f;
            sheen.style.rotate = new Rotate(new Angle(-18f, AngleUnit.Degree));
            return sheen;
        }

        public static void Play(VisualElement sheen)
        {
            if (sheen == null ||
                sheen.panel == null ||
                PackageInstallerAmbientMotionSettings.CurrentMode != PackageInstallerAmbientMotionMode.On)
            {
                return;
            }

            double startedAt = EditorApplication.timeSinceStartup;
            IVisualElementScheduledItem item = null;
            item = sheen.schedule.Execute(() =>
            {
                float elapsed = (float)(EditorApplication.timeSinceStartup - startedAt);
                float t = Mathf.Clamp01(elapsed / DurationSeconds);
                float eased = t * t * (3f - 2f * t);
                sheen.style.translate = new Translate(Mathf.Lerp(-58f, 180f, eased), 0f, 0f);
                sheen.style.opacity = Mathf.Sin(t * Mathf.PI) * 0.16f;

                if (t >= 1f)
                {
                    sheen.style.opacity = 0f;
                    item?.Pause();
                }
            }).Every(FrameIntervalMs);
        }
    }
}
