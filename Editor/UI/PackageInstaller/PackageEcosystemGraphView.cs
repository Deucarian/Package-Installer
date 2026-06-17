using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphView : VisualElement
    {
        private readonly PackageGraphCanvas _canvas;
        private readonly PackageGraphViewport _viewport;

        public PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction)
            : this(packageSelected, packageAction, null)
        {
        }

        public PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared)
        {
            AddToClassList("dpi-ecosystem-graph");
            focusable = true;

            _canvas = new PackageGraphCanvas(packageSelected, packageAction, selectionCleared);
            _viewport = new PackageGraphViewport(selectionCleared);
            _viewport.SetContent(_canvas);

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-ecosystem-graph__header");
            Add(header);

            VisualElement legend = new VisualElement();
            legend.AddToClassList("dpi-ecosystem-graph__legend");
            legend.Add(CreateLegendItem(
                "Flow",
                "Dependency flow",
                "dpi-graph-legend__line--solid",
                "Required package → dependent package. Animated flow markers show direction."));
            legend.Add(CreateLegendItem(
                "Cable",
                "Integration connection",
                "dpi-graph-legend__line--integration",
                "Integration package connects systems. Animated markers show direction."));
            legend.Add(CreateLegendItem(
                "Dotted",
                "Optional companion",
                "dpi-graph-legend__line--optional",
                "Enhances, not required. Subtle animated markers show direction."));
            legend.Add(CreateLegendItem(
                "Halo",
                "Suite membership",
                "dpi-graph-legend__line--suite",
                "Grouped package bundle"));
            legend.Add(CreateLegendItem("Attention", "Update / missing dependency", "dpi-graph-legend__line--warning"));
            header.Add(legend);

            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("dpi-ecosystem-graph__toolbar");
            toolbar.Add(CreateToolbarButton("Fit", () => _viewport.FitToContent(_canvas.GetContentBounds())));
            toolbar.Add(CreateToolbarButton("100%", _viewport.ResetZoom));
            toolbar.Add(CreateToolbarButton("Center", () => _viewport.CenterOn(_canvas.GetHubCenter())));
            header.Add(toolbar);

            Add(_viewport);
        }

        public void SetGraph(PackageGraphModel graph, string selectedPackageId, bool actionsEnabled)
        {
            SetGraph(graph, selectedPackageId, selectedPackageId, actionsEnabled);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled)
        {
            _canvas.SetGraph(graph, selectedPackageId, focusedPackageId, actionsEnabled);
            _viewport.SetContentSize(_canvas.ContentSize.x, _canvas.ContentSize.y);
            _viewport.EnsureInitialFrame(_canvas.GetContentBounds());
        }

        private static VisualElement CreateLegendItem(
            string marker,
            string label,
            string markerClass,
            string tooltip = null)
        {
            VisualElement item = new VisualElement();
            item.AddToClassList("dpi-graph-legend__item");
            item.tooltip = tooltip ?? label;

            Label markerLabel = new Label(marker);
            markerLabel.AddToClassList("dpi-graph-legend__line");
            markerLabel.AddToClassList(markerClass);
            item.Add(markerLabel);

            Label labelElement = new Label(label);
            labelElement.AddToClassList("dpi-graph-legend__label");
            item.Add(labelElement);

            return item;
        }

        private static Button CreateToolbarButton(string label, Action action)
        {
            Button button = new Button(action)
            {
                text = label,
                tooltip = label
            };
            button.AddToClassList("dpi-ecosystem-graph__toolbar-button");
            return button;
        }
    }

    internal sealed class PackageGraphViewport : VisualElement
    {
        private const float MinZoom = 0.35f;
        private const float MaxZoom = 1.75f;
        private const float FitPadding = 72f;
        private const float PanMargin = 220f;
        private const float PanClickToleranceSqr = 4f;

        private readonly VisualElement _contentRoot;
        private readonly Action _selectionCleared;
        private Vector2 _pan;
        private Vector2 _lastMousePosition;
        private Vector2 _mouseDownPosition;
        private float _zoom = 1f;
        private float _contentWidth = PackageGraphLayout.CanvasWidth;
        private float _contentHeight = PackageGraphLayout.CanvasHeight;
        private Rect _initialBounds;
        private bool _initialized;
        private bool _hasInitialBounds;
        private bool _panning;
        private bool _panMoved;

        public PackageGraphViewport(Action selectionCleared)
        {
            _selectionCleared = selectionCleared;
            name = "ecosystem-graph-viewport";
            AddToClassList("dpi-ecosystem-graph__viewport");
            focusable = true;

            _contentRoot = new VisualElement { name = "ecosystem-graph-content" };
            _contentRoot.AddToClassList("dpi-ecosystem-graph__content");
            _contentRoot.style.position = Position.Absolute;
            _contentRoot.style.left = 0f;
            _contentRoot.style.top = 0f;
            _contentRoot.style.width = _contentWidth;
            _contentRoot.style.height = _contentHeight;
            Add(_contentRoot);

            RegisterCallback<WheelEvent>(HandleWheel);
            RegisterCallback<MouseDownEvent>(HandleMouseDown);
            RegisterCallback<MouseMoveEvent>(HandleMouseMove);
            RegisterCallback<MouseUpEvent>(HandleMouseUp);
            RegisterCallback<ClickEvent>(HandleClick);
            RegisterCallback<KeyDownEvent>(HandleKeyDown);
            RegisterCallback<MouseCaptureOutEvent>(_ => _panning = false);
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!_initialized && _hasInitialBounds)
                {
                    FitToContent(_initialBounds);
                    return;
                }

                ClampAndApplyTransform();
            });

            ApplyTransform();
        }

        public float Zoom => _zoom;

        public Vector2 Pan => _pan;

        public void SetContent(VisualElement content)
        {
            _contentRoot.Clear();

            if (content != null)
            {
                _contentRoot.Add(content);
            }
        }

        public void SetContentSize(float width, float height)
        {
            _contentWidth = Mathf.Max(1f, width);
            _contentHeight = Mathf.Max(1f, height);
            _contentRoot.style.width = _contentWidth;
            _contentRoot.style.height = _contentHeight;
            ClampAndApplyTransform();
        }

        public void EnsureInitialFrame(Rect worldBounds)
        {
            _initialBounds = worldBounds;
            _hasInitialBounds = true;

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

            Rect bounds = NormalizeBounds(worldBounds);
            float availableWidth = Mathf.Max(1f, contentRect.width - FitPadding * 2f);
            float availableHeight = Mathf.Max(1f, contentRect.height - FitPadding * 2f);
            float fitZoom = Mathf.Min(availableWidth / bounds.width, availableHeight / bounds.height);
            _zoom = Mathf.Clamp(fitZoom, MinZoom, MaxZoom);
            _pan = GetViewportCenter() - bounds.center * _zoom;
            _initialized = true;
            ClampAndApplyTransform();
        }

        public void ResetZoom()
        {
            if (!HasViewportSize())
            {
                return;
            }

            Vector2 viewportCenter = GetViewportCenter();
            Vector2 worldCenter = ViewportToWorld(viewportCenter);
            _zoom = 1f;
            _pan = viewportCenter - worldCenter * _zoom;
            _initialized = true;
            ClampAndApplyTransform();
        }

        public void CenterOn(Vector2 worldPoint)
        {
            if (!HasViewportSize())
            {
                return;
            }

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
            if (!ShouldStartPan(evt))
            {
                return;
            }

            _panning = true;
            _lastMousePosition = evt.localMousePosition;
            _mouseDownPosition = evt.localMousePosition;
            _panMoved = false;
            MouseCaptureController.CaptureMouse(this);
            Focus();
            evt.StopPropagation();
        }

        private void HandleMouseMove(MouseMoveEvent evt)
        {
            if (!_panning || !MouseCaptureController.HasMouseCapture(this))
            {
                return;
            }

            Vector2 nextMousePosition = evt.localMousePosition;
            if ((nextMousePosition - _mouseDownPosition).sqrMagnitude > PanClickToleranceSqr)
            {
                _panMoved = true;
            }

            _pan += nextMousePosition - _lastMousePosition;
            _lastMousePosition = nextMousePosition;
            _initialized = true;
            ClampAndApplyTransform();
            evt.StopPropagation();
        }

        private void HandleMouseUp(MouseUpEvent evt)
        {
            if (!_panning)
            {
                return;
            }

            _panning = false;

            if (MouseCaptureController.HasMouseCapture(this))
            {
                MouseCaptureController.ReleaseMouse(this);
            }

            evt.StopPropagation();
        }

        private void HandleClick(ClickEvent evt)
        {
            Focus();

            if (evt.button != 0 || _panMoved)
            {
                _panMoved = false;
                return;
            }

            _selectionCleared?.Invoke();
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
            float nextZoom = Mathf.Clamp(targetZoom, MinZoom, MaxZoom);

            if (Mathf.Approximately(nextZoom, _zoom))
            {
                return;
            }

            Vector2 worldPoint = ViewportToWorld(viewportPoint);
            _zoom = nextZoom;
            _pan = viewportPoint - worldPoint * _zoom;
            _initialized = true;
            ClampAndApplyTransform();
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
            _contentRoot.EnableInClassList("dpi-ecosystem-graph__content--low-zoom", _zoom < 0.55f);
            MarkDirtyRepaint();
            _contentRoot.MarkDirtyRepaint();
        }

        private void ClampPan()
        {
            if (!HasViewportSize())
            {
                return;
            }

            _pan.x = ClampAxis(_pan.x, _contentWidth * _zoom, contentRect.width);
            _pan.y = ClampAxis(_pan.y, _contentHeight * _zoom, contentRect.height);
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

        private static bool ShouldStartPan(MouseDownEvent evt)
        {
            return evt.button == 2 ||
                   evt.button == 1 ||
                   (evt.button == 0 && evt.altKey);
        }
    }

    internal sealed class PackageGraphCanvas : VisualElement
    {
        private readonly Action<PackageDefinition> _packageSelected;
        private readonly Action<PackageDefinition, PackageGraphNodeAction> _packageAction;
        private readonly Action _selectionCleared;
        private readonly PackageGraphLayout _layout = new PackageGraphLayout();
        private readonly HashSet<string> _expandedSuiteIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly VisualElement _guideLayer;
        private readonly VisualElement _suiteLayer;
        private readonly PackageGraphEdgeLayer _edgeLayer;
        private readonly VisualElement _nodeLayer;

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphLayoutResult _layoutResult;
        private string _selectedPackageId = string.Empty;
        private string _focusedPackageId = string.Empty;
        private string _hoveredPackageId = string.Empty;
        private bool _actionsEnabled;

        public PackageGraphCanvas(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared)
        {
            _packageSelected = packageSelected;
            _packageAction = packageAction;
            _selectionCleared = selectionCleared;
            name = "ecosystem-graph-canvas";
            AddToClassList("dpi-ecosystem-graph__canvas");
            style.width = PackageGraphLayout.CanvasWidth;
            style.height = PackageGraphLayout.CanvasHeight;

            _guideLayer = new VisualElement { name = "ecosystem-graph-guide-layer" };
            _guideLayer.AddToClassList("dpi-ecosystem-graph__guide-layer");
            _guideLayer.pickingMode = PickingMode.Ignore;
            StretchToCanvas(_guideLayer);
            Add(_guideLayer);

            _suiteLayer = new VisualElement { name = "ecosystem-graph-suite-layer" };
            _suiteLayer.AddToClassList("dpi-ecosystem-graph__suite-layer");
            _suiteLayer.pickingMode = PickingMode.Ignore;
            StretchToCanvas(_suiteLayer);
            Add(_suiteLayer);

            _edgeLayer = new PackageGraphEdgeLayer { name = "ecosystem-graph-edge-layer" };
            _edgeLayer.AddToClassList("dpi-ecosystem-graph__edge-layer");
            _edgeLayer.pickingMode = PickingMode.Ignore;
            StretchToCanvas(_edgeLayer);
            Add(_edgeLayer);

            _nodeLayer = new VisualElement { name = "ecosystem-graph-node-layer" };
            _nodeLayer.AddToClassList("dpi-ecosystem-graph__node-layer");
            StretchToCanvas(_nodeLayer);
            Add(_nodeLayer);
        }

        public Vector2 ContentSize
        {
            get
            {
                return _layoutResult != null
                    ? new Vector2(_layoutResult.CanvasWidth, _layoutResult.CanvasHeight)
                    : new Vector2(PackageGraphLayout.CanvasWidth, PackageGraphLayout.CanvasHeight);
            }
        }

        public Rect GetContentBounds()
        {
            if (_layoutResult == null)
            {
                return new Rect(0f, 0f, PackageGraphLayout.CanvasWidth, PackageGraphLayout.CanvasHeight);
            }

            Rect bounds = _layoutResult.HubRect;
            bool hasBounds = bounds.width > 0.01f && bounds.height > 0.01f;

            foreach (Rect nodeRect in _layoutResult.NodeRects.Values)
            {
                bounds = hasBounds
                    ? Union(bounds, nodeRect)
                    : nodeRect;
                hasBounds = true;
            }

            if (!hasBounds)
            {
                bounds = new Rect(
                    PackageGraphLayout.GraphCenter.x - 1f,
                    PackageGraphLayout.GraphCenter.y - 1f,
                    2f,
                    2f);
            }

            bounds.x -= 36f;
            bounds.y -= 36f;
            bounds.width += 72f;
            bounds.height += 72f;
            return bounds;
        }

        public Vector2 GetHubCenter()
        {
            return _layoutResult != null
                ? _layoutResult.HubRect.center
                : PackageGraphLayout.GraphCenter;
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled)
        {
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _selectedPackageId = selectedPackageId ?? string.Empty;
            _focusedPackageId = focusedPackageId ?? string.Empty;
            _actionsEnabled = actionsEnabled;
            Rebuild();
        }

        private void Rebuild()
        {
            _guideLayer.Clear();
            _suiteLayer.Clear();
            _nodeLayer.Clear();

            _layoutResult = _layout.Calculate(_graph);
            style.width = _layoutResult.CanvasWidth;
            style.height = _layoutResult.CanvasHeight;
            _guideLayer.style.width = _layoutResult.CanvasWidth;
            _guideLayer.style.height = _layoutResult.CanvasHeight;
            _suiteLayer.style.width = _layoutResult.CanvasWidth;
            _suiteLayer.style.height = _layoutResult.CanvasHeight;
            _edgeLayer.style.width = _layoutResult.CanvasWidth;
            _edgeLayer.style.height = _layoutResult.CanvasHeight;
            _nodeLayer.style.width = _layoutResult.CanvasWidth;
            _nodeLayer.style.height = _layoutResult.CanvasHeight;

            PackageGraphFocus focus = PackageGraphFocus.Create(
                _graph,
                GetActiveFocusPackageId(),
                _expandedSuiteIds);

            DrawGuides();
            DrawSuiteRegions(focus);
            DrawNodes(focus);
            _edgeLayer.SetGraph(_graph, _layoutResult.NodeRects, _layoutResult.CanvasHeight, focus);
        }

        private string GetActiveFocusPackageId()
        {
            if (!string.IsNullOrWhiteSpace(_hoveredPackageId) &&
                _graph.TryGetNode(_hoveredPackageId, out _))
            {
                return _hoveredPackageId;
            }

            return !string.IsNullOrWhiteSpace(_focusedPackageId) &&
                   _graph.TryGetNode(_focusedPackageId, out _)
                ? _focusedPackageId
                : string.Empty;
        }

        private void DrawGuides()
        {
            foreach (PackageGraphRingGuide guide in _layoutResult.RingGuides)
            {
                VisualElement ringGuide = new VisualElement();
                ringGuide.AddToClassList("dpi-graph-ring-guide");
                ringGuide.AddToClassList("dpi-graph-ring-guide--" + GetRingClass(guide.Ring));
                ringGuide.style.left = guide.Center.x - guide.RadiusX;
                ringGuide.style.top = guide.Center.y - guide.RadiusY;
                ringGuide.style.width = guide.RadiusX * 2f;
                ringGuide.style.height = guide.RadiusY * 2f;

                Label label = new Label(guide.Label);
                label.AddToClassList("dpi-graph-ring-guide__label");
                ringGuide.Add(label);
                _guideLayer.Add(ringGuide);
            }

            VisualElement hub = new VisualElement();
            hub.AddToClassList("dpi-graph-hub");
            hub.style.left = _layoutResult.HubRect.x;
            hub.style.top = _layoutResult.HubRect.y;
            hub.style.width = _layoutResult.HubRect.width;
            hub.style.height = _layoutResult.HubRect.height;

            Image hubIcon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon("package-installer"),
                scaleMode = ScaleMode.ScaleToFit
            };
            hubIcon.AddToClassList("dpi-graph-hub__icon");
            hub.Add(hubIcon);

            Label title = new Label("Deucarian");
            title.AddToClassList("dpi-graph-hub__title");
            hub.Add(title);

            Label subtitle = new Label("Unity Package System");
            subtitle.AddToClassList("dpi-graph-hub__subtitle");
            hub.Add(subtitle);
            _guideLayer.Add(hub);
        }

        private void DrawSuiteRegions(PackageGraphFocus focus)
        {
            foreach (PackageGraphSuiteRegion region in _graph.SuiteRegions)
            {
                if (!focus.IsSuiteRegionVisible(region))
                {
                    continue;
                }

                List<Rect> memberRects = new List<Rect>();

                foreach (string memberPackageId in region.MemberPackageIds)
                {
                    if (_layoutResult.NodeRects.TryGetValue(memberPackageId, out Rect memberRect))
                    {
                        memberRects.Add(memberRect);
                    }
                }

                if (_layoutResult.NodeRects.TryGetValue(region.SuitePackageId, out Rect suiteRect))
                {
                    memberRects.Add(suiteRect);
                }

                if (memberRects.Count == 0)
                {
                    continue;
                }

                Rect bounds = memberRects[0];

                for (int index = 1; index < memberRects.Count; index++)
                {
                    Rect rect = memberRects[index];
                    bounds = Rect.MinMaxRect(
                        Mathf.Min(bounds.xMin, rect.xMin),
                        Mathf.Min(bounds.yMin, rect.yMin),
                        Mathf.Max(bounds.xMax, rect.xMax),
                        Mathf.Max(bounds.yMax, rect.yMax));
                }

                bounds.x -= 28f;
                bounds.y -= 30f;
                bounds.width += 56f;
                bounds.height += 60f;

                VisualElement suiteRegion = new VisualElement();
                suiteRegion.AddToClassList("dpi-graph-suite-region");
                suiteRegion.EnableInClassList(
                    "dpi-graph-suite-region--expanded",
                    _expandedSuiteIds.Contains(region.SuitePackageId));
                suiteRegion.style.left = bounds.x;
                suiteRegion.style.top = bounds.y;
                suiteRegion.style.width = bounds.width;
                suiteRegion.style.height = bounds.height;

                Label label = new Label(GetSuiteRegionLabel(region.SuitePackageId));
                label.AddToClassList("dpi-graph-suite-region__label");
                suiteRegion.Add(label);
                _suiteLayer.Add(suiteRegion);
            }
        }

        private void DrawNodes(PackageGraphFocus focus)
        {
            foreach (PackageGraphNode node in _graph.Nodes)
            {
                if (!_layoutResult.NodeRects.TryGetValue(node.PackageId, out Rect rect))
                {
                    continue;
                }

                PackageGraphLayoutRing ring = _layoutResult.NodeRings.TryGetValue(
                    node.PackageId,
                    out PackageGraphLayoutRing nodeRing)
                    ? nodeRing
                    : PackageGraphLayoutRing.Foundation;
                bool selected = string.Equals(
                    node.PackageId,
                    _selectedPackageId,
                    StringComparison.OrdinalIgnoreCase);
                bool previewed = string.Equals(
                    node.PackageId,
                    _hoveredPackageId,
                    StringComparison.OrdinalIgnoreCase);
                bool related = focus.IsPackageRelated(node.PackageId);
                bool dimmed = focus.HasFocus && !related;
                PackageGraphNodeElement nodeElement = new PackageGraphNodeElement(
                    node,
                    ring,
                    selected,
                    related,
                    dimmed,
                    previewed,
                    _expandedSuiteIds.Contains(node.PackageId),
                    _actionsEnabled,
                    _packageSelected,
                    _packageAction,
                    _selectionCleared,
                    ToggleSuiteExpanded,
                    SetPreviewPackage,
                    ClearPreviewPackage);
                nodeElement.style.left = rect.x;
                nodeElement.style.top = rect.y;
                nodeElement.style.width = rect.width;
                nodeElement.style.height = rect.height;
                _nodeLayer.Add(nodeElement);
            }
        }

        private string GetSuiteRegionLabel(string suitePackageId)
        {
            PackageGraphNode suiteNode = _graph.Nodes.FirstOrDefault(node =>
                string.Equals(node.PackageId, suitePackageId, StringComparison.OrdinalIgnoreCase));
            return suiteNode != null ? suiteNode.DisplayName + " composition" : "Suite composition";
        }

        private void ToggleSuiteExpanded(string suitePackageId)
        {
            if (string.IsNullOrWhiteSpace(suitePackageId))
            {
                return;
            }

            if (!_expandedSuiteIds.Add(suitePackageId.Trim()))
            {
                _expandedSuiteIds.Remove(suitePackageId.Trim());
            }

            Rebuild();
        }

        private void SetPreviewPackage(string packageId)
        {
            if (string.Equals(_hoveredPackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredPackageId = packageId ?? string.Empty;
            Rebuild();
        }

        private void ClearPreviewPackage(string packageId)
        {
            if (!string.Equals(_hoveredPackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hoveredPackageId = string.Empty;
            Rebuild();
        }

        private static Rect Union(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        private static void StretchToCanvas(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.top = 0f;
            element.style.width = PackageGraphLayout.CanvasWidth;
            element.style.height = PackageGraphLayout.CanvasHeight;
        }

        private static string GetRingClass(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return "runtime";
                case PackageGraphLayoutRing.Integration:
                    return "integration";
                case PackageGraphLayoutRing.Suite:
                    return "suite";
                default:
                    return "foundation";
            }
        }
    }

    internal sealed class PackageGraphEdgeLayer : VisualElement
    {
        private const int CurveSamples = 32;
        private const float AnimationFrameMs = 33f;
        private const float AnimatedDashLength = 14f;
        private const float AnimatedDashGap = 10f;
        private const float EdgeEndpointPadding = 8f;
        private const float MarkerTravelStart = 0.045f;
        private const float MarkerTravelEnd = 0.955f;

        private readonly Dictionary<string, Rect> _nodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphFocus _focus = PackageGraphFocus.Create(null, string.Empty, null);
        private IVisualElementScheduledItem _animationItem;
        private float _animationPhase;
        private bool _animationEnabled;

        public PackageGraphEdgeLayer()
        {
            generateVisualContent += GenerateEdges;
            RegisterCallback<AttachToPanelEvent>(_ => UpdateAnimationSchedule());
            RegisterCallback<DetachFromPanelEvent>(_ => PauseAnimation());
        }

        public void SetGraph(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            float canvasHeight,
            PackageGraphFocus focus)
        {
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _focus = focus ?? PackageGraphFocus.Create(_graph, string.Empty, null);
            _nodeRects.Clear();

            if (nodeRects != null)
            {
                foreach (KeyValuePair<string, Rect> nodeRect in nodeRects)
                {
                    _nodeRects[nodeRect.Key] = nodeRect.Value;
                }
            }

            style.height = canvasHeight;
            _animationEnabled = HasAnimatedEdges();
            UpdateAnimationSchedule();
            MarkDirtyRepaint();
        }

        private bool HasAnimatedEdges()
        {
            if (_graph == null || _graph.Edges.Count == 0 || _focus == null || !_focus.HasFocus)
            {
                return false;
            }

            return _graph.Edges.Any(edge =>
                ShouldAnimate(edge.Kind) &&
                _focus.IsEdgeVisible(edge) &&
                _focus.IsEdgeEmphasized(edge));
        }

        private void UpdateAnimationSchedule()
        {
            if (_animationItem == null)
            {
                _animationItem = schedule.Execute(UpdateAnimation).Every((long)AnimationFrameMs);
            }

            if (_animationEnabled)
            {
                _animationItem.Resume();
            }
            else
            {
                PauseAnimation();
            }
        }

        private void PauseAnimation()
        {
            _animationItem?.Pause();
        }

        private void UpdateAnimation()
        {
            if (!_animationEnabled || !IsVisibleInPanel())
            {
                return;
            }

            _animationPhase = Mathf.Repeat((float)(EditorApplication.timeSinceStartup * 0.46d), 1f);
            MarkDirtyRepaint();
        }

        private bool IsVisibleInPanel()
        {
            if (panel == null)
            {
                return false;
            }

            VisualElement element = this;

            while (element != null)
            {
                if (element.resolvedStyle.display == DisplayStyle.None ||
                    element.resolvedStyle.visibility == Visibility.Hidden)
                {
                    return false;
                }

                element = element.parent;
            }

            return true;
        }

        private void GenerateEdges(MeshGenerationContext context)
        {
            if (_graph == null || _graph.Edges.Count == 0)
            {
                return;
            }

            Painter2D painter = context.painter2D;

            foreach (PackageGraphEdge edge in _graph.Edges)
            {
                if (!_focus.IsEdgeVisible(edge) ||
                    !_nodeRects.TryGetValue(edge.FromPackageId, out Rect fromRect) ||
                    !_nodeRects.TryGetValue(edge.ToPackageId, out Rect toRect))
                {
                    continue;
                }

                DrawEdge(
                    painter,
                    edge,
                    fromRect,
                    toRect,
                    _focus.IsEdgeEmphasized(edge),
                    _focus.HasFocus,
                    _animationPhase);
            }
        }

        private static void DrawEdge(
            Painter2D painter,
            PackageGraphEdge edge,
            Rect fromRect,
            Rect toRect,
            bool emphasized,
            bool focusMode,
            float animationPhase)
        {
            Vector2 start = GetPort(fromRect, toRect, EdgeEndpointPadding);
            Vector2 end = GetPort(toRect, fromRect, EdgeEndpointPadding);
            Vector2 control = GetArcControlPoint(start, end, edge, emphasized);
            Vector2 controlA = Vector2.Lerp(start, control, 0.72f);
            Vector2 controlB = Vector2.Lerp(end, control, 0.72f);
            Color color = GetEdgeColor(edge, emphasized, focusMode);
            float width = GetEdgeWidth(edge, emphasized, focusMode);
            bool animate = emphasized &&
                           focusMode &&
                           ShouldAnimate(edge.Kind);

            if (color.a <= 0.01f || width <= 0.01f)
            {
                return;
            }

            if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
            {
                DrawIntegrationCableBezier(
                    painter,
                    start,
                    controlA,
                    controlB,
                    end,
                    color,
                    width,
                    emphasized);
            }
            else if (IsDotted(edge.Kind))
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                DrawDashedBezier(
                    painter,
                    start,
                    controlA,
                    controlB,
                    end,
                    2.5f,
                    7.5f,
                    animate ? (1f - animationPhase) * 10f : 0f);
            }
            else if (IsDashed(edge.Kind))
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                DrawDashedBezier(
                    painter,
                    start,
                    controlA,
                    controlB,
                    end,
                    AnimatedDashLength,
                    AnimatedDashGap,
                    animate ? (1f - animationPhase) * (AnimatedDashLength + AnimatedDashGap) : 0f);
            }
            else
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                painter.BeginPath();
                painter.MoveTo(start);
                painter.BezierCurveTo(controlA, controlB, end);
                painter.Stroke();
            }

            if (animate)
            {
                Color pulseColor = new Color(color.r, color.g, color.b, Mathf.Min(0.86f, color.a + 0.10f));
                DrawFlowMarkers(
                    painter,
                    edge.Kind,
                    start,
                    controlA,
                    controlB,
                    end,
                    pulseColor,
                    animationPhase);
            }

            if (edge.State == PackageGraphEdgeState.Warning)
            {
                DrawWarningMarker(painter, GetBezierPoint(start, controlA, controlB, end, 0.5f));
            }
        }

        private static Vector2 GetPort(Rect fromRect, Rect toRect, float padding)
        {
            Vector2 delta = toRect.center - fromRect.center;

            if (delta.sqrMagnitude < 0.01f)
            {
                return fromRect.center;
            }

            float scaleX = Mathf.Abs(delta.x) > 0.01f
                ? (fromRect.width * 0.5f) / Mathf.Abs(delta.x)
                : float.PositiveInfinity;
            float scaleY = Mathf.Abs(delta.y) > 0.01f
                ? (fromRect.height * 0.5f) / Mathf.Abs(delta.y)
                : float.PositiveInfinity;
            float scale = Mathf.Min(scaleX, scaleY);
            return fromRect.center +
                   delta.normalized * ((delta.magnitude * Mathf.Clamp01(scale)) + Mathf.Max(0f, padding));
        }

        private static Vector2 GetArcControlPoint(
            Vector2 start,
            Vector2 end,
            PackageGraphEdge edge,
            bool emphasized)
        {
            Vector2 midpoint = (start + end) * 0.5f;
            Vector2 outward = midpoint - PackageGraphLayout.GraphCenter;

            if (outward.sqrMagnitude < 1f)
            {
                Vector2 delta = end - start;
                outward = new Vector2(-delta.y, delta.x);
            }

            if (outward.sqrMagnitude < 1f)
            {
                outward = Vector2.up;
            }

            float distance = Mathf.Max(70f, Vector2.Distance(start, end) * 0.16f);

            if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
            {
                distance += 24f;
            }
            else if (edge.Kind == PackageGraphEdgeKind.SuiteMembership)
            {
                distance += 36f;
            }
            else if (!emphasized)
            {
                distance *= 0.72f;
            }

            return midpoint + outward.normalized * distance;
        }

        private static bool IsDashed(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.Recommended ||
                   kind == PackageGraphEdgeKind.SuiteMembership;
        }

        private static bool IsDotted(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.OptionalCompanion;
        }

        private static bool ShouldAnimate(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.HardDependency ||
                   kind == PackageGraphEdgeKind.IntegrationConnection ||
                   kind == PackageGraphEdgeKind.OptionalCompanion ||
                   kind == PackageGraphEdgeKind.Recommended ||
                   kind == PackageGraphEdgeKind.SuiteMembership;
        }

        private static Color GetEdgeColor(PackageGraphEdge edge, bool emphasized, bool focusMode)
        {
            if (edge.State == PackageGraphEdgeState.Warning)
            {
                return new Color(0.94f, 0.64f, 0.27f, emphasized ? 0.94f : 0.62f);
            }

            if (emphasized)
            {
                if (edge.Kind == PackageGraphEdgeKind.HardDependency)
                {
                    return new Color(0.34f, 0.70f, 0.98f, 0.90f);
                }

                if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
                {
                    return new Color(0.24f, 0.82f, 0.75f, 0.92f);
                }

                if (edge.Kind == PackageGraphEdgeKind.OptionalCompanion)
                {
                    return new Color(0.62f, 0.75f, 0.84f, 0.58f);
                }

                return new Color(0.48f, 0.74f, 0.78f, 0.64f);
            }

            if (focusMode)
            {
                return edge.Kind == PackageGraphEdgeKind.OptionalCompanion
                    ? new Color(0.42f, 0.54f, 0.70f, 0.12f)
                    : new Color(0.42f, 0.54f, 0.70f, 0.22f);
            }

            switch (edge.Kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                    return new Color(0.42f, 0.54f, 0.72f, 0.24f);
                case PackageGraphEdgeKind.IntegrationConnection:
                    return new Color(0.20f, 0.70f, 0.66f, 0.18f);
                case PackageGraphEdgeKind.OptionalCompanion:
                    return new Color(0.50f, 0.62f, 0.72f, 0.12f);
                default:
                    return new Color(0.28f, 0.58f, 0.76f, 0.14f);
            }
        }

        private static float GetEdgeWidth(PackageGraphEdge edge, bool emphasized, bool focusMode)
        {
            if (emphasized)
            {
                if (edge.State == PackageGraphEdgeState.Warning)
                {
                    return 2.7f;
                }

                return edge.Kind == PackageGraphEdgeKind.OptionalCompanion ? 1.8f : 3f;
            }

            if (focusMode)
            {
                return edge.Kind == PackageGraphEdgeKind.OptionalCompanion ? 0.95f : 1.2f;
            }

            switch (edge.Kind)
            {
                case PackageGraphEdgeKind.HardDependency:
                    return 1.45f;
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 1.2f;
                case PackageGraphEdgeKind.OptionalCompanion:
                    return 0.9f;
                default:
                    return 1f;
            }
        }

        private static void DrawIntegrationCableBezier(
            Painter2D painter,
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end,
            Color color,
            float width,
            bool emphasized)
        {
            Vector2 tangent = GetBezierTangent(start, controlA, controlB, end, 0.5f);

            if (tangent.sqrMagnitude < 0.01f)
            {
                tangent = end - start;
            }

            Vector2 side = tangent.sqrMagnitude < 0.01f
                ? Vector2.up
                : new Vector2(-tangent.y, tangent.x).normalized;
            float offset = emphasized ? 4.2f : 3.2f;
            Color underlay = new Color(0.04f, 0.12f, 0.14f, Mathf.Min(0.58f, color.a * 0.68f));

            painter.strokeColor = underlay;
            painter.lineWidth = Mathf.Max(2f, width + 1.9f);
            DrawBezierStroke(painter, start, controlA, controlB, end);

            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(1f, width * 0.62f);
            DrawBezierStroke(
                painter,
                start + side * offset,
                controlA + side * offset,
                controlB + side * offset,
                end + side * offset);
            DrawBezierStroke(
                painter,
                start - side * offset,
                controlA - side * offset,
                controlB - side * offset,
                end - side * offset);

        }

        private static void DrawBezierStroke(
            Painter2D painter,
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end)
        {
            painter.BeginPath();
            painter.MoveTo(start);
            painter.BezierCurveTo(controlA, controlB, end);
            painter.Stroke();
        }

        private static void DrawFlowMarkers(
            Painter2D painter,
            PackageGraphEdgeKind kind,
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end,
            Color color,
            float animationPhase)
        {
            int markerCount = GetFlowMarkerCount(kind);
            float markerSize = GetFlowMarkerSize(kind);
            float markerWidth = GetFlowMarkerWidth(kind);
            float markerAlpha = GetFlowMarkerAlpha(kind);
            Color markerColor = new Color(color.r, color.g, color.b, Mathf.Min(1f, color.a * markerAlpha));

            for (int index = 0; index < markerCount; index++)
            {
                float phase = Mathf.Repeat(animationPhase + (index / (float)markerCount), 1f);
                float markerT = Mathf.Lerp(MarkerTravelStart, MarkerTravelEnd, phase);
                DrawFlowChevron(
                    painter,
                    GetBezierPoint(start, controlA, controlB, end, markerT),
                    GetBezierTangent(start, controlA, controlB, end, markerT),
                    markerColor,
                    markerSize,
                    markerWidth);
            }
        }

        private static void DrawFlowChevron(
            Painter2D painter,
            Vector2 center,
            Vector2 tangent,
            Color color,
            float size,
            float width)
        {
            if (tangent.sqrMagnitude < 0.01f)
            {
                return;
            }

            Vector2 forward = tangent.normalized;
            Vector2 side = new Vector2(-forward.y, forward.x);
            Vector2 tip = center + forward * size * 0.62f;
            Vector2 back = center - forward * size * 0.58f;
            Vector2 left = back + side * size * 0.48f;
            Vector2 right = back - side * size * 0.48f;

            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(left);
            painter.LineTo(tip);
            painter.LineTo(right);
            painter.Stroke();
        }

        private static int GetFlowMarkerCount(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.OptionalCompanion ||
                   kind == PackageGraphEdgeKind.SuiteMembership
                ? 1
                : 2;
        }

        private static float GetFlowMarkerSize(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.OptionalCompanion:
                    return 4.6f;
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 5.8f;
                case PackageGraphEdgeKind.SuiteMembership:
                    return 4.8f;
                default:
                    return 6.2f;
            }
        }

        private static float GetFlowMarkerWidth(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.OptionalCompanion:
                    return 1.0f;
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 1.35f;
                default:
                    return 1.25f;
            }
        }

        private static float GetFlowMarkerAlpha(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.OptionalCompanion:
                    return 0.68f;
                case PackageGraphEdgeKind.SuiteMembership:
                    return 0.58f;
                default:
                    return 0.95f;
            }
        }

        private static void DrawDashedBezier(
            Painter2D painter,
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end,
            float dashLength,
            float gapLength,
            float dashOffset)
        {
            float patternLength = dashLength + gapLength;
            float normalizedOffset = Mathf.Repeat(dashOffset, patternLength);

            bool draw = normalizedOffset < dashLength;
            float segmentCursor = draw ? normalizedOffset : normalizedOffset - dashLength;
            Vector2 previous = start;

            for (int index = 1; index <= CurveSamples; index++)
            {
                float t = index / (float)CurveSamples;
                Vector2 current = GetBezierPoint(start, controlA, controlB, end, t);
                Vector2 delta = current - previous;
                float length = delta.magnitude;

                if (length <= 0.1f)
                {
                    previous = current;
                    continue;
                }

                Vector2 direction = delta / length;
                float consumed = 0f;

                while (consumed < length)
                {
                    float targetLength = draw ? dashLength : gapLength;
                    float step = Mathf.Min(targetLength - segmentCursor, length - consumed);
                    Vector2 segmentStart = previous + direction * consumed;
                    Vector2 segmentEnd = previous + direction * (consumed + step);

                    if (draw)
                    {
                        painter.BeginPath();
                        painter.MoveTo(segmentStart);
                        painter.LineTo(segmentEnd);
                        painter.Stroke();
                    }

                    consumed += step;
                    segmentCursor += step;

                    if (segmentCursor >= targetLength - 0.01f)
                    {
                        segmentCursor = 0f;
                        draw = !draw;
                    }
                }

                previous = current;
            }
        }

        private static Vector2 GetBezierPoint(
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end,
            float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * inverse * start +
                   3f * inverse * inverse * t * controlA +
                   3f * inverse * t * t * controlB +
                   t * t * t * end;
        }

        private static Vector2 GetBezierTangent(
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end,
            float t)
        {
            float inverse = 1f - t;
            return 3f * inverse * inverse * (controlA - start) +
                   6f * inverse * t * (controlB - controlA) +
                   3f * t * t * (end - controlB);
        }

        private static void DrawWarningMarker(Painter2D painter, Vector2 center)
        {
            painter.fillColor = new Color(0.94f, 0.64f, 0.27f, 0.90f);
            painter.strokeColor = new Color(0.16f, 0.12f, 0.06f, 0.86f);
            painter.lineWidth = 1.2f;

            painter.BeginPath();
            painter.MoveTo(center + new Vector2(0f, -7f));
            painter.LineTo(center + new Vector2(7f, 6f));
            painter.LineTo(center + new Vector2(-7f, 6f));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }
    }

    internal sealed class PackageGraphNodeElement : VisualElement
    {
        private readonly PackageGraphNode _node;

        public PackageGraphNodeElement(
            PackageGraphNode node,
            PackageGraphLayoutRing ring,
            bool selected,
            bool related,
            bool dimmed,
            bool previewed,
            bool suiteExpanded,
            bool actionsEnabled,
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            Action<string> suiteToggled,
            Action<string> previewPackage,
            Action<string> clearPreviewPackage)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));

            AddToClassList("dpi-graph-node");
            AddToClassList("dpi-graph-node--" + GetNodeClass(node.NodeType));
            AddToClassList("dpi-graph-node--status-" + GetStatusClass(node.Status));
            AddToClassList("dpi-graph-node--ring-" + GetRingClass(ring));
            EnableInClassList("dpi-graph-node--installed", node.IsInstalled);
            EnableInClassList("dpi-graph-node--selected", selected);
            EnableInClassList("dpi-graph-node--related", related && !selected);
            EnableInClassList("dpi-graph-node--dimmed", dimmed);
            EnableInClassList("dpi-graph-node--previewed", previewed);
            EnableInClassList("dpi-graph-node--missing", !node.IsRegistered);
            tooltip = GetTooltip(node);

            RegisterCallback<MouseEnterEvent>(_ => previewPackage?.Invoke(node.PackageId));
            RegisterCallback<MouseLeaveEvent>(_ => clearPreviewPackage?.Invoke(node.PackageId));

            if (node.PackageDefinition != null && packageSelected != null)
            {
                RegisterCallback<ClickEvent>(evt =>
                {
                    if (selected)
                    {
                        selectionCleared?.Invoke();
                    }
                    else
                    {
                        packageSelected(node.PackageDefinition);
                    }

                    evt.StopPropagation();
                });
            }

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-graph-node__header");
            Add(header);

            Image icon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon(node.IconKey),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList("dpi-graph-node__icon");
            header.Add(icon);

            VisualElement titleBlock = new VisualElement();
            titleBlock.AddToClassList("dpi-graph-node__title-block");
            header.Add(titleBlock);

            Label title = new Label(node.DisplayName);
            title.AddToClassList("dpi-graph-node__title");
            titleBlock.Add(title);

            Label packageId = new Label(node.PackageId);
            packageId.AddToClassList("dpi-graph-node__package-id");
            titleBlock.Add(packageId);

            if (node.NodeType == PackageGraphNodeType.Suite)
            {
                Button expandButton = new Button(() => suiteToggled?.Invoke(node.PackageId))
                {
                    text = suiteExpanded ? "-" : "+",
                    tooltip = suiteExpanded ? "Collapse suite composition" : "Expand suite composition"
                };
                expandButton.AddToClassList("dpi-graph-node__suite-toggle");
                expandButton.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                header.Add(expandButton);
            }

            VisualElement badges = new VisualElement();
            badges.AddToClassList("dpi-graph-node__badges");
            badges.Add(CreateBadge(GetNodeTypeLabel(node.NodeType), "dpi-graph-node__badge--type"));
            badges.Add(CreateBadge(GetStatusLabel(node), GetStatusBadgeClass(node.Status)));
            Add(badges);

            VisualElement footer = new VisualElement();
            footer.AddToClassList("dpi-graph-node__footer");
            Add(footer);

            Label channel = new Label(node.SelectedChannel.ToString());
            channel.AddToClassList("dpi-graph-node__channel");
            footer.Add(channel);

            if (node.PrimaryAction != PackageGraphNodeAction.None)
            {
                Button actionButton = new Button(() =>
                {
                    packageAction?.Invoke(node.PackageDefinition, node.PrimaryAction);
                })
                {
                    text = node.PrimaryActionLabel
                };
                actionButton.AddToClassList("dpi-graph-node__action");
                actionButton.SetEnabled(actionsEnabled);
                actionButton.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                footer.Add(actionButton);
            }
        }

        private static Label CreateBadge(string text, string className)
        {
            Label badge = new Label(text);
            badge.AddToClassList("deucarian-badge");
            badge.AddToClassList("dpi-graph-node__badge");
            badge.AddToClassList(className);
            return badge;
        }

        private static string GetNodeClass(PackageGraphNodeType nodeType)
        {
            switch (nodeType)
            {
                case PackageGraphNodeType.Tool:
                    return "tool";
                case PackageGraphNodeType.Companion:
                    return "companion";
                case PackageGraphNodeType.Suite:
                    return "suite";
                case PackageGraphNodeType.Integration:
                    return "integration";
                default:
                    return "core";
            }
        }

        private static string GetRingClass(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return "runtime";
                case PackageGraphLayoutRing.Integration:
                    return "integration";
                case PackageGraphLayoutRing.Suite:
                    return "suite";
                default:
                    return "foundation";
            }
        }

        private static string GetNodeTypeLabel(PackageGraphNodeType nodeType)
        {
            switch (nodeType)
            {
                case PackageGraphNodeType.Tool:
                    return "Tool";
                case PackageGraphNodeType.Companion:
                    return "Companion";
                case PackageGraphNodeType.Suite:
                    return "Suite";
                case PackageGraphNodeType.Integration:
                    return "Integration";
                default:
                    return "Core";
            }
        }

        private static string GetStatusLabel(PackageGraphNode node)
        {
            switch (node.Status)
            {
                case PackageGraphNodeStatus.Missing:
                    return "Missing dependency";
                case PackageGraphNodeStatus.NotInstalled:
                    return "Not installed";
                case PackageGraphNodeStatus.UpdateAvailable:
                    return "Update available";
                case PackageGraphNodeStatus.Checking:
                    return "Checking";
                case PackageGraphNodeStatus.Warning:
                    return string.IsNullOrWhiteSpace(node.UpdateStatusLabel)
                        ? "Attention"
                        : node.UpdateStatusLabel;
                default:
                    return "Installed";
            }
        }

        private static string GetStatusClass(PackageGraphNodeStatus status)
        {
            switch (status)
            {
                case PackageGraphNodeStatus.Missing:
                    return "missing";
                case PackageGraphNodeStatus.NotInstalled:
                    return "available";
                case PackageGraphNodeStatus.UpdateAvailable:
                    return "update";
                case PackageGraphNodeStatus.Checking:
                    return "checking";
                case PackageGraphNodeStatus.Warning:
                    return "warning";
                default:
                    return "installed";
            }
        }

        private static string GetStatusBadgeClass(PackageGraphNodeStatus status)
        {
            return "dpi-graph-node__badge--" + GetStatusClass(status);
        }

        private static string GetTooltip(PackageGraphNode node)
        {
            string status = string.IsNullOrWhiteSpace(node.UpdateStatusLabel)
                ? GetStatusLabel(node)
                : node.UpdateStatusLabel;
            string description = string.IsNullOrWhiteSpace(node.Description)
                ? node.PackageId
                : node.Description;

            return node.DisplayName + "\n" +
                   description + "\n" +
                   node.PackageId + "\n" +
                   "Status: " + status;
        }
    }
}
