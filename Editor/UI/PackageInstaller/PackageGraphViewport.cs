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
    internal sealed class PackageGraphViewport : VisualElement
    {
        private const float OverviewMinZoom = 0.28f;
        private const float FocusMinZoom = 0.42f;
        private const float AbsoluteMinZoom = 0.10f;
        private const float MaxZoom = 1.75f;
        private const float FitMarginRatio = 0.10f;
        private const float MinFitPadding = 28f;
        private const float MaxFitPadding = 88f;
        private const float PanMargin = 220f;

        private readonly PackageGraphSpotlightLayer _spotlightLayer;
        private readonly VisualElement _contentRoot;
        private readonly Action _selectionCleared;
        private Vector2 _pan;
        private readonly PackageGraphPanGesture _panGesture = new PackageGraphPanGesture();

        private float _zoom = 1f;
        private float _contentWidth = PackageGraphLayout.CanvasWidth;
        private float _contentHeight = PackageGraphLayout.CanvasHeight;
        private Vector2 _lastReportedViewportSize;
        private VisualElement _mouseDownTarget;
        private PackageGraphLayoutMode _layoutMode = PackageGraphLayoutMode.Overview;
        private Rect _initialBounds;
        private Rect _activeBounds = default(Rect);
        private bool _initialized;
        private bool _hasInitialBounds;
        private bool _cameraTransitionActive;

        private float _requiredFitZoom = 1f;
        private bool _suppressNextClick;
        private double _cameraTransitionStartedAt;
        private IVisualElementScheduledItem _cameraTransitionItem;
        private PackageGraphCameraState _cameraTransitionSource;
        private PackageGraphCameraState _cameraTransitionTarget;
        private Vector2 _cameraTransitionSourceAnchorWorld;
        private Vector2 _cameraTransitionTargetAnchorWorld;
        private Vector2 _cameraTransitionSourceAnchorScreen;
        private Vector2 _cameraTransitionTargetAnchorScreen;
        private bool _cameraTransitionUsesDirectTarget;
        private Vector2 _spotlightWorldCenter;
        private PackageGraphSpotlightKind _spotlightKind = PackageGraphSpotlightKind.None;
        private bool _spotlightActive;

        public PackageGraphViewport(Action selectionCleared)
        {
            _selectionCleared = selectionCleared;
            name = "ecosystem-graph-viewport";
            AddToClassList("dpi-ecosystem-graph__viewport");
            focusable = true;

            _spotlightLayer = new PackageGraphSpotlightLayer();
            Add(_spotlightLayer);

            _contentRoot = new VisualElement { name = "ecosystem-graph-content" };
            _contentRoot.AddToClassList("dpi-ecosystem-graph__content");
            _contentRoot.style.position = Position.Absolute;
            _contentRoot.style.left = 0f;
            _contentRoot.style.top = 0f;
            _contentRoot.style.width = _contentWidth;
            _contentRoot.style.height = _contentHeight;
            Add(_contentRoot);

            RegisterCallback<WheelEvent>(HandleWheel);
            RegisterCallback<MouseDownEvent>(HandleMouseDown, TrickleDown.TrickleDown);
            RegisterCallback<MouseMoveEvent>(HandleMouseMove);
            RegisterCallback<MouseUpEvent>(HandleMouseUp);
            RegisterCallback<DetachFromPanelEvent>(_ => CancelPan());
            RegisterCallback<BlurEvent>(_ => CancelPan());
            RegisterCallback<ClickEvent>(HandleClickCapture, TrickleDown.TrickleDown);
            RegisterCallback<ClickEvent>(HandleClick);
            RegisterCallback<KeyDownEvent>(HandleKeyDown);
            RegisterCallback<MouseCaptureOutEvent>(_ => ResetPanState());
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                ReportViewportSizeIfNeeded();

                if (!_initialized && _hasInitialBounds)
                {
                    FitToContent(_initialBounds);
                    return;
                }

                ClampAndApplyTransform();
            });

            ApplyTransform();
        }

        public event Action<Vector2> ViewportSizeChanged;

        public event Action<float> ZoomChanged;

        public event Action<float> CameraTransitionCompleted;

        public event Action<PackageGraphContextMenuRequest> ContextMenuRequested;

        public float Zoom => _zoom;

        public Vector2 Pan => _pan;

        public bool IsCameraTransitionActive => _cameraTransitionActive;

        public Vector2 ViewportSize => HasViewportSize()
            ? new Vector2(contentRect.width, contentRect.height)
            : Vector2.zero;

        internal float EffectiveMinZoomForTests => GetMinZoom();

        internal float RequiredFitZoomForTests => _requiredFitZoom;

        internal PackageGraphSpotlightLayer SpotlightLayerForTests => _spotlightLayer;

        internal VisualElement ContentRootForTests => _contentRoot;

        public void SetLayoutMode(PackageGraphLayoutMode layoutMode, bool clampZoom = true)
        {
            _layoutMode = layoutMode;
            UpdateRequiredFitZoom(_activeBounds);

            if (clampZoom && _zoom < GetMinZoom())
            {
                _zoom = GetMinZoom();
                ClampAndApplyTransform();
            }
        }

        public void SetContent(VisualElement content)
        {
            _contentRoot.Clear();

            if (content != null)
            {
                _contentRoot.Add(content);
            }
        }

        public void SetSpotlightWorldCenter(Vector2 worldCenter, PackageGraphSpotlightKind kind)
        {
            _spotlightWorldCenter = worldCenter;
            _spotlightKind = kind;
            _spotlightActive = kind != PackageGraphSpotlightKind.None;
            UpdateSpotlight();
        }

        public void SetContentSize(float width, float height, bool clamp = true)
        {
            _contentWidth = Mathf.Max(1f, width);
            _contentHeight = Mathf.Max(1f, height);
            _contentRoot.style.width = _contentWidth;
            _contentRoot.style.height = _contentHeight;
            UpdateRequiredFitZoom(_activeBounds);

            if (clamp)
            {
                ClampAndApplyTransform();
            }
            else
            {
                ApplyTransform();
            }
        }

        public void SetActiveBounds(Rect worldBounds)
        {
            _activeBounds = NormalizeBounds(worldBounds);
            UpdateRequiredFitZoom(_activeBounds);
        }

        public void EnsureInitialFrame(Rect worldBounds, bool force = false)
        {
            _initialBounds = worldBounds;
            _hasInitialBounds = true;
            SetActiveBounds(worldBounds);

            if (force)
            {
                _initialized = false;
            }

            if (_initialized || !HasViewportSize())
            {
                return;
            }

            FitToContent(worldBounds);
        }

        public void FitToContent(Rect worldBounds)
        {
            if (!HasViewportSize())
            {
                return;
            }

            StopCameraTransition();
            Rect bounds = NormalizeBounds(worldBounds);
            SetActiveBounds(bounds);
            PackageGraphCameraState camera = CalculateFitCamera(bounds);
            _zoom = camera.Zoom;
            _pan = camera.Pan;
            _initialized = true;
            ClampAndApplyTransform();
            ZoomChanged?.Invoke(_zoom);
        }

        public void AnimateToContent(
            Rect worldBounds,
            Vector2 sourceAnchorWorld,
            Vector2 targetAnchorWorld,
            Vector2 sourceAnchorScreen)
        {
            if (!HasViewportSize())
            {
                return;
            }

            Rect bounds = NormalizeBounds(worldBounds);
            SetActiveBounds(bounds);
            PackageGraphCameraState targetCamera = CalculateFitCamera(bounds);
            PackageGraphCameraState sourceCamera = new PackageGraphCameraState(_pan, _zoom);
            _cameraTransitionSource = sourceCamera;
            _cameraTransitionTarget = targetCamera;
            _cameraTransitionSourceAnchorWorld = sourceAnchorWorld;
            _cameraTransitionTargetAnchorWorld = targetAnchorWorld;
            _cameraTransitionSourceAnchorScreen = sourceAnchorScreen;
            _cameraTransitionTargetAnchorScreen = targetCamera.WorldToViewport(targetAnchorWorld);
            _cameraTransitionUsesDirectTarget = false;
            _cameraTransitionStartedAt = EditorApplication.timeSinceStartup;
            _cameraTransitionActive = true;
            _initialized = true;

            StartCameraTransitionSchedule();
        }

        public PackageGraphCameraState GetCameraState()
        {
            return new PackageGraphCameraState(_pan, _zoom);
        }

        public void ApplyPreviewCamera(PackageGraphCameraState camera)
        {
            _zoom = Mathf.Clamp(camera.Zoom, AbsoluteMinZoom, MaxZoom);
            _pan = ClampPan(camera.Pan, _zoom);
            _initialized = true;
            ApplyTransform();
        }

        public void AnimateToCamera(PackageGraphCameraState targetCamera)
        {
            if (!HasViewportSize())
            {
                ApplyPreviewCamera(targetCamera);
                return;
            }

            float targetZoom = Mathf.Clamp(targetCamera.Zoom, AbsoluteMinZoom, MaxZoom);
            _cameraTransitionSource = new PackageGraphCameraState(_pan, _zoom);
            _cameraTransitionTarget = new PackageGraphCameraState(
                ClampPan(targetCamera.Pan, targetZoom),
                targetZoom);
            _cameraTransitionUsesDirectTarget = true;
            _cameraTransitionStartedAt = EditorApplication.timeSinceStartup;
            _cameraTransitionActive = true;
            _initialized = true;
            StartCameraTransitionSchedule();
        }

        public PackageGraphCameraState CalculateFitCamera(Rect worldBounds)
        {
            return CalculateFitCamera(worldBounds, _layoutMode);
        }

        public PackageGraphCameraState CalculateFitCamera(Rect worldBounds, PackageGraphLayoutMode layoutMode)
        {
            Rect bounds = NormalizeBounds(worldBounds);
            float fitZoom = CalculateFitZoom(bounds);
            float zoom = Mathf.Clamp(fitZoom, GetMinZoom(layoutMode), MaxZoom);
            Vector2 pan = GetViewportCenter() - bounds.center * zoom;
            return new PackageGraphCameraState(ClampPan(pan, zoom), zoom);
        }

        public void ResetZoom()
        {
            ResetZoom(ViewportToWorld(GetViewportCenter()));
        }

        public void ResetZoom(Vector2 worldCenter)
        {
            if (!HasViewportSize())
            {
                return;
            }

            StopCameraTransition();
            Vector2 viewportCenter = GetViewportCenter();
            _zoom = 1f;
            _pan = viewportCenter - worldCenter * _zoom;
            _initialized = true;
            ClampAndApplyTransform();
            ZoomChanged?.Invoke(_zoom);
        }

        public void CenterOn(Vector2 worldPoint)
        {
            if (!HasViewportSize())
            {
                return;
            }

            StopCameraTransition();
            _pan = GetViewportCenter() - worldPoint * _zoom;
            _initialized = true;
            ClampAndApplyTransform();
        }

        public Vector2 WorldToViewport(Vector2 worldPoint)
        {
            return worldPoint * _zoom + _pan;
        }

        public Vector2 ViewportToWorld(Vector2 viewportPoint)
        {
            return (viewportPoint - _pan) / Mathf.Max(0.001f, _zoom);
        }

        private void HandleWheel(WheelEvent evt)
        {
            if (!HasViewportSize() || Mathf.Abs(evt.delta.y) <= 0.01f)
            {
                return;
            }

            float zoomMultiplier = evt.delta.y > 0f ? 0.90f : 1.10f;
            ZoomAround(evt.localMousePosition, _zoom * zoomMultiplier);
            evt.StopPropagation();
        }

        private void HandleMouseDown(MouseDownEvent evt)
        {
            if (!ShouldConsiderPan(evt))
            {
                return;
            }

            _panGesture.Begin(evt.button, evt.localMousePosition);
            _mouseDownTarget = evt.target as VisualElement;
            MouseCaptureController.CaptureMouse(this);
            Focus();
            evt.StopPropagation();
        }

        private void HandleMouseMove(MouseMoveEvent evt)
        {
            if (!_panGesture.IsCandidate || !MouseCaptureController.HasMouseCapture(this))
            {
                return;
            }

            _pan += _panGesture.Move(evt.localMousePosition);
            _initialized = true;
            ClampAndApplyTransform();
            evt.StopPropagation();
        }

        private void HandleMouseUp(MouseUpEvent evt)
        {
            if (!_panGesture.IsCandidate)
            {
                return;
            }

            bool openedMenu = false;
            if (_panGesture.Button == 1 && !_panGesture.HasMoved)
            {
                ContextMenuRequested?.Invoke(new PackageGraphContextMenuRequest(
                    _mouseDownTarget,
                    evt.localMousePosition,
                    ViewportToWorld(evt.localMousePosition)));
                openedMenu = true;
            }

            _suppressNextClick = _panGesture.HasMoved || openedMenu;
            ResetPanState();

            if (MouseCaptureController.HasMouseCapture(this))
            {
                MouseCaptureController.ReleaseMouse(this);
            }

            evt.StopPropagation();
        }

        private void HandleClickCapture(ClickEvent evt)
        {
            if (!_suppressNextClick)
            {
                return;
            }

            _suppressNextClick = false;
            evt.StopImmediatePropagation();
        }

        private void HandleClick(ClickEvent evt)
        {
            Focus();

            if (evt.button != 0)
            {
                return;
            }

            evt.StopPropagation();
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            _selectionCleared?.Invoke();
            evt.StopPropagation();
        }

        private void ZoomAround(Vector2 viewportPoint, float targetZoom)
        {
            float nextZoom = Mathf.Clamp(targetZoom, GetMinZoom(), MaxZoom);

            if (Mathf.Approximately(nextZoom, _zoom))
            {
                return;
            }

            Vector2 worldPoint = ViewportToWorld(viewportPoint);
            StopCameraTransition();
            _zoom = nextZoom;
            _pan = viewportPoint - worldPoint * _zoom;
            _initialized = true;
            ClampAndApplyTransform();
            ZoomChanged?.Invoke(_zoom);
        }

        private void UpdateCameraTransition()
        {
            if (!_cameraTransitionActive)
            {
                return;
            }

            float elapsed = (float)(EditorApplication.timeSinceStartup - _cameraTransitionStartedAt);
            float t = Mathf.Clamp01(elapsed / PackageGraphTransition.DefaultDurationSeconds);
            PackageGraphCameraState camera = _cameraTransitionUsesDirectTarget
                ? PackageGraphTransition.EvaluateCamera(
                    _cameraTransitionSource,
                    _cameraTransitionTarget,
                    t)
                : PackageGraphTransition.EvaluateAnchoredCamera(
                    _cameraTransitionSource,
                    _cameraTransitionTarget,
                    _cameraTransitionSourceAnchorWorld,
                    _cameraTransitionTargetAnchorWorld,
                    _cameraTransitionSourceAnchorScreen,
                    _cameraTransitionTargetAnchorScreen,
                    t);
            _zoom = camera.Zoom;
            _pan = ClampPan(camera.Pan, _zoom);
            _initialized = true;
            ApplyTransform();

            if (t < 1f)
            {
                return;
            }

            _cameraTransitionActive = false;
            _cameraTransitionItem?.Pause();
            _zoom = _cameraTransitionTarget.Zoom;
            _pan = _cameraTransitionTarget.Pan;
            ClampAndApplyTransform();
            CameraTransitionCompleted?.Invoke(_zoom);
        }

        private void StartCameraTransitionSchedule()
        {
            if (_cameraTransitionItem == null)
            {
                _cameraTransitionItem = schedule.Execute(UpdateCameraTransition)
                    .Every((long)(PackageGraphTransition.DefaultDurationSeconds * 1000f / 18f));
            }

            _cameraTransitionItem.Resume();
            UpdateCameraTransition();
        }

        private void StopCameraTransition()
        {
            if (!_cameraTransitionActive)
            {
                return;
            }

            _cameraTransitionActive = false;
            _cameraTransitionItem?.Pause();
        }

        private void ClampAndApplyTransform()
        {
            ClampPan();
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            _contentRoot.style.translate = new Translate(_pan.x, _pan.y, 0f);
            _contentRoot.style.scale = new Scale(new Vector3(_zoom, _zoom, 1f));
            _contentRoot.EnableInClassList("dpi-ecosystem-graph__content--low-zoom", _zoom < 0.58f);
            _contentRoot.EnableInClassList("dpi-ecosystem-graph__content--medium-zoom", _zoom >= 0.58f && _zoom < 1.05f);
            _contentRoot.EnableInClassList("dpi-ecosystem-graph__content--high-zoom", _zoom >= 1.05f);
            UpdateSpotlight();
            MarkDirtyRepaint();
            _contentRoot.MarkDirtyRepaint();
        }

        private void UpdateSpotlight()
        {
            if (_spotlightLayer == null)
            {
                return;
            }

            if (!_spotlightActive || !HasViewportSize())
            {
                _spotlightLayer.SetSpotlight(Vector2.zero, 0f, PackageGraphSpotlightKind.None, false);
                return;
            }

            _spotlightLayer.SetSpotlight(
                WorldToViewport(_spotlightWorldCenter),
                GetSpotlightRadius(_spotlightKind),
                _spotlightKind,
                true);
        }

        private static float GetSpotlightRadius(PackageGraphSpotlightKind kind)
        {
            switch (kind)
            {
                case PackageGraphSpotlightKind.Root:
                    return 360f;
                case PackageGraphSpotlightKind.Category:
                    return 300f;
                case PackageGraphSpotlightKind.Attention:
                    return 220f;
                case PackageGraphSpotlightKind.Package:
                    return 205f;
                default:
                    return 0f;
            }
        }

        private void ClampPan()
        {
            if (!HasViewportSize())
            {
                return;
            }

            _pan = ClampPan(_pan, _zoom);
        }

        private Vector2 ClampPan(Vector2 pan, float zoom)
        {
            if (!HasViewportSize())
            {
                return pan;
            }

            pan.x = ClampAxis(pan.x, _contentWidth * zoom, contentRect.width);
            pan.y = ClampAxis(pan.y, _contentHeight * zoom, contentRect.height);
            return pan;
        }

        private static float ClampAxis(float pan, float scaledContentSize, float viewportSize)
        {
            if (scaledContentSize <= viewportSize - PanMargin * 2f)
            {
                return (viewportSize - scaledContentSize) * 0.5f;
            }

            float min = viewportSize - scaledContentSize - PanMargin;
            float max = PanMargin;
            return Mathf.Clamp(pan, min, max);
        }

        private bool HasViewportSize()
        {
            return contentRect.width > 1f && contentRect.height > 1f;
        }

        private Vector2 GetViewportCenter()
        {
            return new Vector2(contentRect.width * 0.5f, contentRect.height * 0.5f);
        }

        private float GetMinZoom()
        {
            return GetMinZoom(_layoutMode);
        }

        private float GetMinZoom(PackageGraphLayoutMode layoutMode)
        {
            float configuredMinimum =
                layoutMode == PackageGraphLayoutMode.Overview
                    ? OverviewMinZoom
                    : FocusMinZoom;
            return CalculateEffectiveMinZoom(configuredMinimum, _requiredFitZoom);
        }

        private float GetFitPadding()
        {
            float smallestViewportAxis = Mathf.Min(contentRect.width, contentRect.height);
            return Mathf.Clamp(smallestViewportAxis * FitMarginRatio, MinFitPadding, MaxFitPadding);
        }

        private void UpdateRequiredFitZoom(Rect worldBounds)
        {
            _requiredFitZoom = HasViewportSize()
                ? Mathf.Max(AbsoluteMinZoom, CalculateFitZoom(NormalizeBounds(worldBounds)) * 0.96f)
                : 1f;
        }

        private float CalculateFitZoom(Rect worldBounds)
        {
            Rect bounds = NormalizeBounds(worldBounds);
            float fitPadding = GetFitPadding();
            float availableWidth = Mathf.Max(1f, contentRect.width - fitPadding * 2f);
            float availableHeight = Mathf.Max(1f, contentRect.height - fitPadding * 2f);
            return Mathf.Min(availableWidth / bounds.width, availableHeight / bounds.height);
        }

        private void ReportViewportSizeIfNeeded()
        {
            if (!HasViewportSize())
            {
                return;
            }

            Vector2 viewportSize = new Vector2(contentRect.width, contentRect.height);

            if (Mathf.Abs(viewportSize.x - _lastReportedViewportSize.x) < 1f &&
                Mathf.Abs(viewportSize.y - _lastReportedViewportSize.y) < 1f)
            {
                return;
            }

            _lastReportedViewportSize = viewportSize;
            ViewportSizeChanged?.Invoke(viewportSize);
        }

        private static Rect NormalizeBounds(Rect bounds)
        {
            if (bounds.width <= 0.01f || bounds.height <= 0.01f)
            {
                return new Rect(
                    PackageGraphLayout.GraphCenter.x - 1f,
                    PackageGraphLayout.GraphCenter.y - 1f,
                    2f,
                    2f);
            }

            return bounds;
        }

        private void CancelPan()
        {
            if (MouseCaptureController.HasMouseCapture(this))
            {
                MouseCaptureController.ReleaseMouse(this);
            }

            ResetPanState();
        }

        private void ResetPanState()
        {
            _panGesture.Reset();
            _mouseDownTarget = null;
        }

        private static bool ShouldConsiderPan(MouseDownEvent evt)
        {
            return evt != null &&
                   ShouldConsiderPan(evt.target as VisualElement, evt.button, evt.altKey);
        }

        private static bool ShouldConsiderPan(VisualElement target, int button, bool altKey)
        {
            if (PackageGraphVisualQueries.HasAncestorClass(target, "dpi-ecosystem-graph__empty-state"))
            {
                return false;
            }

            return button == 2 ||
                   button == 1 ||
                   (button == 0 && (altKey || IsLeftPanTarget(target)));
        }

        internal static bool IsLeftPanTargetForTests(VisualElement target)
        {
            return IsLeftPanTarget(target);
        }

        internal static bool ShouldConsiderPanForTests(VisualElement target, int button, bool altKey = false)
        {
            return ShouldConsiderPan(target, button, altKey);
        }

        internal static float CalculateEffectiveMinZoomForTests(float configuredMinimum, float requiredFitZoom)
        {
            return CalculateEffectiveMinZoom(configuredMinimum, requiredFitZoom);
        }

        private static float CalculateEffectiveMinZoom(float configuredMinimum, float requiredFitZoom)
        {
            return Mathf.Max(AbsoluteMinZoom, Mathf.Min(configuredMinimum, requiredFitZoom));
        }

        private static bool IsLeftPanTarget(VisualElement target)
        {
            if (target == null)
            {
                return true;
            }

            if (PackageGraphVisualQueries.HasAncestorClass(target, "dpi-graph-hub"))
            {
                return true;
            }

            return !PackageGraphVisualQueries.HasAncestorClass(target, "dpi-graph-node") &&
                   !PackageGraphVisualQueries.HasAncestorClass(target, "dpi-graph-group") &&
                   !PackageGraphVisualQueries.HasAncestorClass(target, "dpi-ecosystem-graph__empty-state") &&
                   !PackageGraphVisualQueries.HasAncestorClass(target, "dpi-ecosystem-graph__toolbar") &&
                   !PackageGraphVisualQueries.HasAncestorClass(target, "dpi-ecosystem-graph__breadcrumbs") &&
                   !PackageGraphVisualQueries.HasAncestorClass(target, "dpi-ecosystem-graph__legend");
        }

    }
}
