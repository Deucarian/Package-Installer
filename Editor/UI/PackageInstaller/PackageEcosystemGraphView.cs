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
        private readonly PackageVisibilityFilterState _filterState;
        private readonly Action _filterChanged;
        private readonly Action _rootFocused;
        private readonly Action<PackageGraphGroup> _groupFocused;
        private readonly TextField _searchField;
        private readonly VisualElement _breadcrumbRow;
        private readonly Button _installedFilterButton;
        private readonly Button _notInstalledFilterButton;
        private readonly Button _clearFiltersButton;
        private readonly Label _visibleCountLabel;
        private readonly Label _hiddenRelatedLabel;
        private readonly VisualElement _emptyState;
        private readonly Label _emptyStateTitle;
        private readonly Button _emptyStateActionButton;
        private PackageVisibilityFilterCounts _filterCounts =
            new PackageVisibilityFilterCounts(0, 0, 0, 0);
        private int _hiddenRelatedCount;
        private bool _hasAppliedGraphFrame;

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
            : this(packageSelected, packageAction, selectionCleared, null, null)
        {
        }

        public PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            Action rootFocused,
            Action<PackageGraphGroup> groupFocused,
            PackageVisibilityFilterState filterState,
            Action filterChanged)
            : this(packageSelected, packageAction, selectionCleared, filterState, filterChanged, rootFocused, groupFocused)
        {
        }

        public PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            PackageVisibilityFilterState filterState,
            Action filterChanged)
            : this(packageSelected, packageAction, selectionCleared, filterState, filterChanged, null, null)
        {
        }

        private PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            PackageVisibilityFilterState filterState,
            Action filterChanged,
            Action rootFocused,
            Action<PackageGraphGroup> groupFocused)
        {
            AddToClassList("dpi-ecosystem-graph");
            focusable = true;
            _filterState = filterState ?? new PackageVisibilityFilterState();
            _filterChanged = filterChanged;
            _rootFocused = rootFocused;
            _groupFocused = groupFocused;

            _canvas = new PackageGraphCanvas(packageSelected, packageAction, selectionCleared, _rootFocused, _groupFocused);
            _viewport = new PackageGraphViewport(selectionCleared);
            _viewport.ViewportSizeChanged += HandleViewportSizeChanged;
            _viewport.SetContent(_canvas);

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-ecosystem-graph__header");
            Add(header);

            VisualElement filterRow = new VisualElement();
            filterRow.AddToClassList("dpi-ecosystem-graph__filter-row");
            header.Add(filterRow);

            _breadcrumbRow = new VisualElement();
            _breadcrumbRow.AddToClassList("dpi-ecosystem-graph__breadcrumbs");
            header.Add(_breadcrumbRow);

            _searchField = new TextField
            {
                name = "ecosystem-graph-search"
            };
            _searchField.AddToClassList("dpi-ecosystem-graph__search");
            _searchField.tooltip = "Search package names, package IDs, categories, package types, descriptions, URLs, versions, and dependencies.";
            _searchField.SetValueWithoutNotify(_filterState.SearchText);
            _searchField.RegisterValueChangedCallback(evt =>
            {
                if (_filterState.SetSearchText(evt.newValue))
                {
                    _filterChanged?.Invoke();
                    UpdateFilterControls();
                }
            });
            _searchField.RegisterCallback<KeyDownEvent>(HandleSearchKeyDown);
            filterRow.Add(_searchField);

            _installedFilterButton = CreateFilterToggleButton(() =>
            {
                if (_filterState.SetShowInstalled(!_filterState.ShowInstalled))
                {
                    _filterChanged?.Invoke();
                    UpdateFilterControls();
                }
            });
            filterRow.Add(_installedFilterButton);

            _notInstalledFilterButton = CreateFilterToggleButton(() =>
            {
                if (_filterState.SetShowNotInstalled(!_filterState.ShowNotInstalled))
                {
                    _filterChanged?.Invoke();
                    UpdateFilterControls();
                }
            });
            filterRow.Add(_notInstalledFilterButton);

            _clearFiltersButton = new Button(ClearFilters)
            {
                text = "Clear",
                tooltip = "Clear search and show all package visibility states."
            };
            _clearFiltersButton.AddToClassList("dpi-ecosystem-graph__filter-clear");
            filterRow.Add(_clearFiltersButton);

            _visibleCountLabel = new Label("0 visible");
            _visibleCountLabel.AddToClassList("dpi-ecosystem-graph__visible-count");
            filterRow.Add(_visibleCountLabel);

            _hiddenRelatedLabel = new Label();
            _hiddenRelatedLabel.AddToClassList("dpi-ecosystem-graph__hidden-related");
            filterRow.Add(_hiddenRelatedLabel);

            VisualElement filterSpacer = new VisualElement();
            filterSpacer.AddToClassList("deucarian-toolbar-spacer");
            filterRow.Add(filterSpacer);

            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("dpi-ecosystem-graph__toolbar");
            toolbar.Add(CreateToolbarButton("Fit", () => _viewport.FitToContent(_canvas.GetContentBounds())));
            toolbar.Add(CreateToolbarButton("100%", _viewport.ResetZoom));
            toolbar.Add(CreateToolbarButton("Center", () => _viewport.CenterOn(_canvas.GetActiveCenter())));
            filterRow.Add(toolbar);

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
                "Recommended alongside, not required"));
            legend.Add(CreateLegendItem(
                "Halo",
                "Suite membership",
                "dpi-graph-legend__line--suite",
                "Grouped package bundle"));
            legend.Add(CreateLegendItem("Attention", "Update / missing dependency", "dpi-graph-legend__line--warning"));
            header.Add(legend);

            Add(_viewport);

            _emptyState = new VisualElement();
            _emptyState.AddToClassList("dpi-ecosystem-graph__empty-state");
            _emptyStateTitle = new Label();
            _emptyStateTitle.AddToClassList("dpi-ecosystem-graph__empty-title");
            _emptyState.Add(_emptyStateTitle);
            _emptyStateActionButton = new Button(ClearFilters);
            _emptyStateActionButton.AddToClassList("dpi-ecosystem-graph__empty-action");
            _emptyState.Add(_emptyStateActionButton);
            _viewport.Add(_emptyState);

            UpdateFilterControls();
        }

        public void SetGraph(PackageGraphModel graph, string selectedPackageId, bool actionsEnabled)
        {
            SetGraph(
                graph,
                selectedPackageId,
                selectedPackageId,
                string.Empty,
                actionsEnabled,
                null,
                null,
                0);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled)
        {
            SetGraph(
                graph,
                selectedPackageId,
                focusedPackageId,
                string.Empty,
                actionsEnabled,
                null,
                null,
                0);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds,
            PackageVisibilityFilterCounts filterCounts,
            int hiddenRelatedCount)
        {
            SetGraph(
                graph,
                selectedPackageId,
                focusedPackageId,
                string.Empty,
                actionsEnabled,
                visiblePackageIds,
                filterCounts,
                hiddenRelatedCount);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            string focusedGroupId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds,
            PackageVisibilityFilterCounts filterCounts,
            int hiddenRelatedCount)
        {
            string previousLayoutFocusPackageId = _canvas.LayoutFocusPackageId;
            string previousLayoutFocusGroupId = _canvas.LayoutFocusGroupId;
            bool shouldForceInitialFrame = !_hasAppliedGraphFrame && graph != null && graph.Nodes.Count > 0;
            _canvas.SetGraph(graph, selectedPackageId, focusedPackageId, focusedGroupId, actionsEnabled, visiblePackageIds);
            _viewport.SetLayoutMode(_canvas.LayoutMode);
            _viewport.SetContentSize(_canvas.ContentSize.x, _canvas.ContentSize.y);
            _viewport.EnsureInitialFrame(_canvas.GetContentBounds(), shouldForceInitialFrame);
            _hasAppliedGraphFrame = _hasAppliedGraphFrame || shouldForceInitialFrame;
            _filterCounts = filterCounts ?? PackageVisibilityFilter.CalculateCounts(graph, _filterState);
            _hiddenRelatedCount = Math.Max(0, hiddenRelatedCount);
            UpdateFilterControls();
            UpdateBreadcrumbs(graph, focusedGroupId, focusedPackageId);

            if (!string.Equals(
                    previousLayoutFocusPackageId,
                    _canvas.LayoutFocusPackageId,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(_canvas.LayoutFocusPackageId) ||
                !string.Equals(
                    previousLayoutFocusGroupId,
                    _canvas.LayoutFocusGroupId,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(_canvas.LayoutFocusGroupId))
            {
                _viewport.CenterOn(_canvas.GetActiveCenter());
            }
        }

        private void HandleViewportSizeChanged(Vector2 viewportSize)
        {
            if (!_canvas.SetViewportSize(viewportSize))
            {
                return;
            }

            _viewport.SetLayoutMode(_canvas.LayoutMode);
            _viewport.SetContentSize(_canvas.ContentSize.x, _canvas.ContentSize.y);
            _viewport.EnsureInitialFrame(_canvas.GetContentBounds(), force: true);
        }

        private void HandleSearchKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || string.IsNullOrWhiteSpace(_filterState.SearchText))
            {
                return;
            }

            if (_filterState.SetSearchText(string.Empty))
            {
                _searchField.SetValueWithoutNotify(string.Empty);
                _filterChanged?.Invoke();
                UpdateFilterControls();
            }

            evt.StopPropagation();
        }

        private void ClearFilters()
        {
            if (_filterState.Reset())
            {
                _searchField.SetValueWithoutNotify(string.Empty);
                _filterChanged?.Invoke();
            }

            UpdateFilterControls();
        }

        private void UpdateBreadcrumbs(
            PackageGraphModel graph,
            string focusedGroupId,
            string focusedPackageId)
        {
            _breadcrumbRow.Clear();

            Button rootButton = CreateBreadcrumbButton("Deucarian", () =>
            {
                _rootFocused?.Invoke();
            });
            _breadcrumbRow.Add(rootButton);

            PackageGraphGroup[] groupPath = CreateGroupPath(graph, focusedGroupId, focusedPackageId);

            foreach (PackageGraphGroup group in groupPath)
            {
                _breadcrumbRow.Add(CreateBreadcrumbSeparator());
                _breadcrumbRow.Add(CreateBreadcrumbButton(group.DisplayName, () =>
                {
                    _groupFocused?.Invoke(group);
                }));
            }

            PackageGraphNode focusedPackage = null;

            if (graph != null &&
                !string.IsNullOrWhiteSpace(focusedPackageId) &&
                graph.TryGetNode(focusedPackageId, out PackageGraphNode packageNode))
            {
                focusedPackage = packageNode;
            }

            if (focusedPackage != null)
            {
                _breadcrumbRow.Add(CreateBreadcrumbSeparator());
                Label packageLabel = new Label(focusedPackage.DisplayName);
                packageLabel.AddToClassList("dpi-ecosystem-graph__breadcrumb-current");
                _breadcrumbRow.Add(packageLabel);
            }

            _breadcrumbRow.EnableInClassList(
                "dpi-ecosystem-graph__breadcrumbs--root",
                groupPath.Length == 0 && focusedPackage == null);
        }

        private static PackageGraphGroup[] CreateGroupPath(
            PackageGraphModel graph,
            string focusedGroupId,
            string focusedPackageId)
        {
            if (graph == null || graph.Groups.Count == 0)
            {
                return Array.Empty<PackageGraphGroup>();
            }

            string groupId = focusedGroupId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(groupId) &&
                !string.IsNullOrWhiteSpace(focusedPackageId) &&
                graph.TryGetNode(focusedPackageId, out PackageGraphNode focusedPackage))
            {
                groupId = focusedPackage.GroupId;
            }

            List<PackageGraphGroup> path = new List<PackageGraphGroup>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(groupId) &&
                   visited.Add(groupId) &&
                   graph.TryGetGroup(groupId, out PackageGraphGroup group))
            {
                path.Add(group);
                groupId = group.ParentGroupId;
            }

            path.Reverse();
            return path.ToArray();
        }

        private static Button CreateBreadcrumbButton(string text, Action clicked)
        {
            Button button = new Button(clicked)
            {
                text = text,
                tooltip = "Navigate to " + text
            };
            button.AddToClassList("dpi-ecosystem-graph__breadcrumb");
            return button;
        }

        private static Label CreateBreadcrumbSeparator()
        {
            Label separator = new Label("/");
            separator.AddToClassList("dpi-ecosystem-graph__breadcrumb-separator");
            return separator;
        }

        private void ShowAllPackages()
        {
            if (_filterState.Set(string.Empty, showInstalled: true, showNotInstalled: true))
            {
                _searchField.SetValueWithoutNotify(string.Empty);
                _filterChanged?.Invoke();
            }

            UpdateFilterControls();
        }

        private void UpdateFilterControls()
        {
            PackageVisibilityFilterCounts counts = _filterCounts ?? new PackageVisibilityFilterCounts(0, 0, 0, 0);
            _searchField.SetValueWithoutNotify(_filterState.SearchText);
            UpdateFilterToggleButton(
                _installedFilterButton,
                _filterState.ShowInstalled,
                "Installed",
                counts.InstalledCount);
            UpdateFilterToggleButton(
                _notInstalledFilterButton,
                _filterState.ShowNotInstalled,
                "Not installed",
                counts.NotInstalledCount);

            _clearFiltersButton.SetEnabled(!_filterState.IsDefault);
            _visibleCountLabel.text = counts.VisibleCount + " visible";
            _hiddenRelatedLabel.text = _hiddenRelatedCount > 0
                ? _hiddenRelatedCount + " related hidden by filters"
                : string.Empty;
            _hiddenRelatedLabel.style.display = _hiddenRelatedCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateEmptyState(counts);
        }

        private void UpdateEmptyState(PackageVisibilityFilterCounts counts)
        {
            bool showEmptyState = counts != null && counts.TotalCount > 0 && counts.VisibleCount == 0;
            _emptyState.style.display = showEmptyState ? DisplayStyle.Flex : DisplayStyle.None;

            if (!showEmptyState)
            {
                return;
            }

            if (!_filterState.HasAnyVisibilityEnabled)
            {
                _emptyStateTitle.text = "No package visibility filters selected.";
                _emptyStateActionButton.text = "Show all packages";
                _emptyStateActionButton.tooltip = "Enable Installed and Not installed packages.";
                _emptyStateActionButton.clicked -= ClearFilters;
                _emptyStateActionButton.clicked -= ShowAllPackages;
                _emptyStateActionButton.clicked += ShowAllPackages;
                return;
            }

            _emptyStateTitle.text = "No packages match the current filters.";
            _emptyStateActionButton.text = "Clear filters";
            _emptyStateActionButton.tooltip = "Clear search and show all package visibility states.";
            _emptyStateActionButton.clicked -= ClearFilters;
            _emptyStateActionButton.clicked -= ShowAllPackages;
            _emptyStateActionButton.clicked += ClearFilters;
        }

        private static Button CreateFilterToggleButton(Action action)
        {
            Button button = new Button(action);
            button.AddToClassList("dpi-ecosystem-graph__filter-toggle");
            return button;
        }

        private static void UpdateFilterToggleButton(
            Button button,
            bool active,
            string label,
            int count)
        {
            if (button == null)
            {
                return;
            }

            button.text = (active ? "[x] " : "[ ] ") + label + " " + count;
            button.tooltip = (active ? "Hide " : "Show ") + label.ToLowerInvariant() + " packages.";
            button.EnableInClassList("dpi-ecosystem-graph__filter-toggle--active", active);
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
        private const float OverviewMinZoom = 0.35f;
        private const float FocusMinZoom = 0.68f;
        private const float MaxZoom = 1.75f;
        private const float FitMarginRatio = 0.10f;
        private const float MinFitPadding = 28f;
        private const float MaxFitPadding = 88f;
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
        private Vector2 _lastReportedViewportSize;
        private PackageGraphLayoutMode _layoutMode = PackageGraphLayoutMode.Overview;
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

        public float Zoom => _zoom;

        public Vector2 Pan => _pan;

        public void SetLayoutMode(PackageGraphLayoutMode layoutMode)
        {
            _layoutMode = layoutMode;

            if (_zoom < GetMinZoom())
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

        public void SetContentSize(float width, float height)
        {
            _contentWidth = Mathf.Max(1f, width);
            _contentHeight = Mathf.Max(1f, height);
            _contentRoot.style.width = _contentWidth;
            _contentRoot.style.height = _contentHeight;
            ClampAndApplyTransform();
        }

        public void EnsureInitialFrame(Rect worldBounds, bool force = false)
        {
            _initialBounds = worldBounds;
            _hasInitialBounds = true;

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

            Rect bounds = NormalizeBounds(worldBounds);
            float fitPadding = GetFitPadding();
            float availableWidth = Mathf.Max(1f, contentRect.width - fitPadding * 2f);
            float availableHeight = Mathf.Max(1f, contentRect.height - fitPadding * 2f);
            float fitZoom = Mathf.Min(availableWidth / bounds.width, availableHeight / bounds.height);
            _zoom = Mathf.Clamp(fitZoom, GetMinZoom(), MaxZoom);
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
            float nextZoom = Mathf.Clamp(targetZoom, GetMinZoom(), MaxZoom);

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

        private float GetMinZoom()
        {
            return _layoutMode == PackageGraphLayoutMode.Overview ? OverviewMinZoom : FocusMinZoom;
        }

        private float GetFitPadding()
        {
            float smallestViewportAxis = Mathf.Min(contentRect.width, contentRect.height);
            return Mathf.Clamp(smallestViewportAxis * FitMarginRatio, MinFitPadding, MaxFitPadding);
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

        private static bool ShouldStartPan(MouseDownEvent evt)
        {
            return evt.button == 2 ||
                   evt.button == 1 ||
                   (evt.button == 0 && evt.altKey);
        }
    }

    internal sealed class PackageGraphCanvas : VisualElement
    {
        private const float LayoutTransitionSeconds = 0.24f;
        private const float LayoutAnimationFrameMs = 16f;

        private readonly Action<PackageDefinition> _packageSelected;
        private readonly Action<PackageDefinition, PackageGraphNodeAction> _packageAction;
        private readonly Action _selectionCleared;
        private readonly Action _rootFocused;
        private readonly Action<PackageGraphGroup> _groupFocused;
        private readonly PackageGraphLayout _layout = new PackageGraphLayout();
        private readonly Dictionary<string, Rect> _animatedNodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rect> _transitionStartRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageGraphNodeElement> _nodeElements =
            new Dictionary<string, PackageGraphNodeElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageGraphGroupElement> _groupElements =
            new Dictionary<string, PackageGraphGroupElement>(StringComparer.OrdinalIgnoreCase);
        private readonly VisualElement _guideLayer;
        private readonly PackageGraphEdgeLayer _edgeLayer;
        private readonly VisualElement _nodeLayer;

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphModel _visibleGraph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private HashSet<string> _visiblePackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private PackageGraphLayoutResult _layoutResult;
        private string _selectedPackageId = string.Empty;
        private string _focusedPackageId = string.Empty;
        private string _focusedGroupId = string.Empty;
        private string _hoveredPackageId = string.Empty;
        private string _layoutFocusPackageId = string.Empty;
        private string _layoutFocusGroupId = string.Empty;
        private PackageGraphFocus _currentFocus = PackageGraphFocus.Create(null, string.Empty);
        private PackageGraphFocus _actionFocus = PackageGraphFocus.Create(null, string.Empty);
        private Vector2 _viewportSize;
        private IVisualElementScheduledItem _layoutAnimationItem;
        private double _layoutAnimationStartedAt;
        private bool _layoutAnimationActive;
        private bool _interactionsLocked;
        private bool _actionsEnabled;

        public PackageGraphCanvas(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared)
            : this(packageSelected, packageAction, selectionCleared, null, null)
        {
        }

        public PackageGraphCanvas(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            Action rootFocused,
            Action<PackageGraphGroup> groupFocused)
        {
            _packageSelected = packageSelected;
            _packageAction = packageAction;
            _selectionCleared = selectionCleared;
            _rootFocused = rootFocused;
            _groupFocused = groupFocused;
            name = "ecosystem-graph-canvas";
            AddToClassList("dpi-ecosystem-graph__canvas");
            style.width = PackageGraphLayout.CanvasWidth;
            style.height = PackageGraphLayout.CanvasHeight;

            _guideLayer = new VisualElement { name = "ecosystem-graph-guide-layer" };
            _guideLayer.AddToClassList("dpi-ecosystem-graph__guide-layer");
            _guideLayer.pickingMode = PickingMode.Ignore;
            StretchToCanvas(_guideLayer);
            Add(_guideLayer);

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

            foreach (PackageGraphGroupLayoutNode groupNode in _layoutResult.GroupNodes)
            {
                bounds = hasBounds
                    ? Union(bounds, groupNode.Rect)
                    : groupNode.Rect;
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

            bounds.x -= 16f;
            bounds.y -= 16f;
            bounds.width += 32f;
            bounds.height += 32f;
            return bounds;
        }

        public Vector2 GetActiveCenter()
        {
            return _layoutResult != null
                ? _layoutResult.ActiveCenter
                : PackageGraphLayout.GraphCenter;
        }

        public string LayoutFocusPackageId => _layoutFocusPackageId;

        public string LayoutFocusGroupId => _layoutFocusGroupId;

        public PackageGraphLayoutMode LayoutMode => _layoutResult != null ? _layoutResult.Mode : PackageGraphLayoutMode.Overview;

        internal IReadOnlyDictionary<string, Rect> NodeRectsForTests => _layoutResult != null
            ? _layoutResult.NodeRects
            : new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        public bool SetViewportSize(Vector2 viewportSize)
        {
            if (viewportSize.x <= 1f || viewportSize.y <= 1f)
            {
                return false;
            }

            if (Mathf.Abs(viewportSize.x - _viewportSize.x) < 1f &&
                Mathf.Abs(viewportSize.y - _viewportSize.y) < 1f)
            {
                return false;
            }

            _viewportSize = viewportSize;

            if (_layoutResult != null)
            {
                Rebuild();
            }

            return true;
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled)
        {
            SetGraph(graph, selectedPackageId, focusedPackageId, string.Empty, actionsEnabled, null);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds)
        {
            SetGraph(graph, selectedPackageId, focusedPackageId, string.Empty, actionsEnabled, visiblePackageIds);
        }

        public void SetGraph(
            PackageGraphModel graph,
            string selectedPackageId,
            string focusedPackageId,
            string focusedGroupId,
            bool actionsEnabled,
            IReadOnlyCollection<string> visiblePackageIds)
        {
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _visiblePackageIds = visiblePackageIds == null
                ? new HashSet<string>(_graph.Nodes.Select(node => node.PackageId), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(visiblePackageIds, StringComparer.OrdinalIgnoreCase);
            _visibleGraph = PackageVisibilityFilter.CreateVisibleGraph(_graph, _visiblePackageIds);
            _selectedPackageId = selectedPackageId ?? string.Empty;
            _focusedPackageId = focusedPackageId ?? string.Empty;
            _focusedGroupId = focusedGroupId ?? string.Empty;
            _actionsEnabled = actionsEnabled;
            Rebuild();
        }

        private void Rebuild()
        {
            Dictionary<string, Rect> previousRects = CaptureCurrentNodeRects();
            _guideLayer.Clear();
            _nodeLayer.Clear();
            _nodeElements.Clear();
            _groupElements.Clear();

            _layoutFocusPackageId = GetLayoutFocusPackageId();
            _layoutFocusGroupId = string.IsNullOrWhiteSpace(_layoutFocusPackageId)
                ? GetLayoutFocusGroupId()
                : string.Empty;
            PackageGraphLayoutMode layoutMode = !string.IsNullOrWhiteSpace(_layoutFocusPackageId)
                ? PackageGraphLayoutMode.Focus
                : (!string.IsNullOrWhiteSpace(_layoutFocusGroupId)
                    ? PackageGraphLayoutMode.GroupFocus
                    : PackageGraphLayoutMode.Overview);
            PackageGraphLayoutResult fullLayoutResult = _layout.Calculate(
                _visibleGraph,
                layoutMode,
                _layoutFocusPackageId,
                _layoutFocusGroupId,
                _viewportSize);
            _currentFocus = PackageGraphFocus.Create(
                _visibleGraph,
                _layoutFocusPackageId);
            _actionFocus = PackageGraphFocus.Create(
                _visibleGraph,
                _layoutFocusPackageId);
            PackageGraphFocus edgeFocus = PackageGraphFocus.Create(
                _visibleGraph,
                _layoutFocusPackageId);
            _layoutResult = CreateVisibleLayoutResult(fullLayoutResult, _currentFocus);
            style.width = _layoutResult.CanvasWidth;
            style.height = _layoutResult.CanvasHeight;
            _guideLayer.style.width = _layoutResult.CanvasWidth;
            _guideLayer.style.height = _layoutResult.CanvasHeight;
            _edgeLayer.style.width = _layoutResult.CanvasWidth;
            _edgeLayer.style.height = _layoutResult.CanvasHeight;
            _nodeLayer.style.width = _layoutResult.CanvasWidth;
            _nodeLayer.style.height = _layoutResult.CanvasHeight;

            DrawGuides();
            StartLayoutTransition(previousRects);
            DrawGroups();
            DrawNodes(_currentFocus);
            DrawUnrelatedSummary();
            ApplyAnimatedLayout();
            _edgeLayer.SetGraph(_visibleGraph, _animatedNodeRects, _layoutResult.CanvasHeight, edgeFocus);
        }

        private string GetLayoutFocusPackageId()
        {
            return !string.IsNullOrWhiteSpace(_focusedPackageId) &&
                   _graph.TryGetNode(_focusedPackageId, out _) &&
                   _visiblePackageIds.Contains(_focusedPackageId)
                ? _focusedPackageId
                : string.Empty;
        }

        private string GetLayoutFocusGroupId()
        {
            return !string.IsNullOrWhiteSpace(_focusedGroupId) &&
                   _graph.TryGetGroup(_focusedGroupId, out _)
                ? _focusedGroupId
                : string.Empty;
        }

        private PackageGraphLayoutResult CreateVisibleLayoutResult(
            PackageGraphLayoutResult fullLayoutResult,
            PackageGraphFocus fullFocus)
        {
            if (fullLayoutResult == null)
            {
                return null;
            }

            Dictionary<string, Rect> visibleNodeRects = fullLayoutResult.NodeRects
                .Where(pair => _visiblePackageIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphLayoutRing> visibleNodeRings = fullLayoutResult.NodeRings
                .Where(pair => _visiblePackageIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            int visibleUnrelatedCount = fullLayoutResult.Mode == PackageGraphLayoutMode.Focus && fullFocus != null
                ? _visibleGraph.Nodes.Count(node => !fullFocus.IsPackageRelated(node.PackageId))
                : 0;
            Rect unrelatedSummaryRect = visibleUnrelatedCount > 0
                ? fullLayoutResult.UnrelatedSummaryRect
                : default(Rect);
            PackageGraphGroupLayoutNode[] visibleGroupNodes = fullLayoutResult.GroupNodes
                .Where(groupNode => groupNode != null &&
                                    (groupNode.PackageCount > 0 ||
                                     fullLayoutResult.Mode == PackageGraphLayoutMode.GroupFocus ||
                                     fullLayoutResult.Mode == PackageGraphLayoutMode.Overview))
                .ToArray();

            return new PackageGraphLayoutResult(
                fullLayoutResult.Mode,
                fullLayoutResult.FocusPackageId,
                fullLayoutResult.CanvasWidth,
                fullLayoutResult.CanvasHeight,
                fullLayoutResult.HubRect,
                fullLayoutResult.ActiveCenter,
                visibleNodeRects,
                visibleNodeRings,
                fullLayoutResult.RingGuides,
                fullLayoutResult.SectorLabels,
                visibleUnrelatedCount,
                unrelatedSummaryRect,
                visibleGroupNodes,
                fullLayoutResult.FocusGroupId);
        }

        private Dictionary<string, Rect> CaptureCurrentNodeRects()
        {
            if (_animatedNodeRects.Count > 0)
            {
                return new Dictionary<string, Rect>(_animatedNodeRects, StringComparer.OrdinalIgnoreCase);
            }

            return _layoutResult != null
                ? new Dictionary<string, Rect>(_layoutResult.NodeRects, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        }

        private void StartLayoutTransition(IReadOnlyDictionary<string, Rect> previousRects)
        {
            _animatedNodeRects.Clear();
            _transitionStartRects.Clear();
            bool shouldAnimate = false;

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                Rect start = previousRects != null && previousRects.TryGetValue(target.Key, out Rect previous)
                    ? previous
                    : target.Value;
                _transitionStartRects[target.Key] = start;
                _animatedNodeRects[target.Key] = start;

                if (!AreRectsClose(start, target.Value))
                {
                    shouldAnimate = true;
                }
            }

            if (!shouldAnimate)
            {
                _layoutAnimationActive = false;
                _interactionsLocked = false;
                _layoutAnimationItem?.Pause();
                CopyTargetRectsToAnimatedRects();
                return;
            }

            _layoutAnimationStartedAt = EditorApplication.timeSinceStartup;
            _layoutAnimationActive = true;
            _interactionsLocked = true;

            if (_layoutAnimationItem == null)
            {
                _layoutAnimationItem = schedule.Execute(UpdateLayoutAnimation).Every((long)LayoutAnimationFrameMs);
            }

            _layoutAnimationItem.Resume();
        }

        private void UpdateLayoutAnimation()
        {
            if (!_layoutAnimationActive || _layoutResult == null)
            {
                return;
            }

            float elapsed = (float)(EditorApplication.timeSinceStartup - _layoutAnimationStartedAt);
            float t = Mathf.Clamp01(elapsed / LayoutTransitionSeconds);
            float eased = SmoothStep(t);

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                Rect start = _transitionStartRects.TryGetValue(target.Key, out Rect startRect)
                    ? startRect
                    : target.Value;
                _animatedNodeRects[target.Key] = LerpRect(start, target.Value, eased);
            }

            ApplyAnimatedLayout();

            if (t >= 1f)
            {
                _layoutAnimationActive = false;
                _interactionsLocked = false;
                _layoutAnimationItem?.Pause();
                CopyTargetRectsToAnimatedRects();
                ApplyAnimatedLayout();
                Rebuild();
            }
        }

        private void CopyTargetRectsToAnimatedRects()
        {
            _animatedNodeRects.Clear();

            if (_layoutResult == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                _animatedNodeRects[target.Key] = target.Value;
            }
        }

        private void ApplyAnimatedLayout()
        {
            foreach (KeyValuePair<string, PackageGraphNodeElement> nodeElement in _nodeElements)
            {
                if (_animatedNodeRects.TryGetValue(nodeElement.Key, out Rect rect))
                {
                    SetElementRect(nodeElement.Value, rect);
                }
            }

            _edgeLayer.UpdateNodeRects(_animatedNodeRects);
            _edgeLayer.MarkDirtyRepaint();
        }

        private void DrawGuides()
        {
            foreach (PackageGraphRingGuide guide in _layoutResult.RingGuides)
            {
                VisualElement ringGuide = new VisualElement();
                ringGuide.AddToClassList("dpi-graph-ring-guide");
                ringGuide.AddToClassList("dpi-graph-ring-guide--" + GetRingClass(guide.Ring));
                ringGuide.pickingMode = PickingMode.Ignore;
                ringGuide.style.left = guide.Center.x - guide.RadiusX;
                ringGuide.style.top = guide.Center.y - guide.RadiusY;
                ringGuide.style.width = guide.RadiusX * 2f;
                ringGuide.style.height = guide.RadiusY * 2f;

                if (!string.IsNullOrWhiteSpace(guide.Label))
                {
                    Label label = new Label(guide.Label);
                    label.AddToClassList("dpi-graph-ring-guide__label");
                    ringGuide.Add(label);
                }

                _guideLayer.Add(ringGuide);
            }

            foreach (PackageGraphSectorLabel sectorLabel in _layoutResult.SectorLabels)
            {
                Label label = new Label(sectorLabel.Label);
                label.AddToClassList("dpi-graph-sector-label");
                label.AddToClassList("dpi-graph-sector-label--" + GetSectorClass(sectorLabel));
                label.style.left = sectorLabel.Position.x - 92f;
                label.style.top = sectorLabel.Position.y - 12f;
                label.style.width = 184f;
                label.style.height = 24f;
                _guideLayer.Add(label);
            }

            VisualElement hub = new VisualElement();
            hub.AddToClassList("dpi-graph-hub");
            hub.EnableInClassList("dpi-graph-hub--focus", _layoutResult.Mode == PackageGraphLayoutMode.Focus);
            hub.EnableInClassList("dpi-graph-hub--group-focus", _layoutResult.Mode == PackageGraphLayoutMode.GroupFocus);
            hub.style.left = _layoutResult.HubRect.x;
            hub.style.top = _layoutResult.HubRect.y;
            hub.style.width = _layoutResult.HubRect.width;
            hub.style.height = _layoutResult.HubRect.height;
            hub.tooltip = "Return to Deucarian overview";
            hub.RegisterCallback<ClickEvent>(evt =>
            {
                if (_interactionsLocked)
                {
                    evt.StopPropagation();
                    return;
                }

                _rootFocused?.Invoke();
                evt.StopPropagation();
            });

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

        private void DrawGroups()
        {
            foreach (PackageGraphGroupLayoutNode groupNode in _layoutResult.GroupNodes)
            {
                PackageGraphGroupElement groupElement = new PackageGraphGroupElement(
                    groupNode,
                    _layoutResult.Mode,
                    !_interactionsLocked,
                    _groupFocused);
                SetElementRect(groupElement, groupNode.Rect);
                _nodeLayer.Add(groupElement);
                _groupElements[groupNode.GroupId] = groupElement;
            }
        }

        private void DrawNodes(PackageGraphFocus focus)
        {
            foreach (PackageGraphNode node in _graph.Nodes)
            {
                if (!_animatedNodeRects.TryGetValue(node.PackageId, out Rect rect))
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
                PackageGraphNodeVisualMode visualMode = GetNodeVisualMode(dimmed);
                bool showNodeAction = ShouldShowNodeAction(
                    node,
                    _actionFocus,
                    _actionFocus.IsPackageRelated(node.PackageId),
                    _layoutResult.Mode);
                bool nodeActionsEnabled = showNodeAction && _actionsEnabled && !_interactionsLocked;
                PackageGraphNodeElement nodeElement = new PackageGraphNodeElement(
                    node,
                    ring,
                    visualMode,
                    selected,
                    related,
                    dimmed,
                    previewed,
                    showNodeAction,
                    nodeActionsEnabled,
                    !_interactionsLocked,
                    _packageSelected,
                    _packageAction,
                    _selectionCleared,
                    SetPreviewPackage,
                    ClearPreviewPackage);
                nodeElement.style.left = rect.x;
                nodeElement.style.top = rect.y;
                nodeElement.style.width = rect.width;
                nodeElement.style.height = rect.height;
                _nodeLayer.Add(nodeElement);
                _nodeElements[node.PackageId] = nodeElement;
            }
        }

        private void DrawUnrelatedSummary()
        {
            if (_layoutResult == null || !_layoutResult.HasUnrelatedSummary)
            {
                return;
            }

            Label summary = new Label("+" + _layoutResult.UnrelatedPackageCount + " unrelated packages");
            summary.name = "ecosystem-graph-unrelated-summary";
            summary.AddToClassList("dpi-graph-unrelated-summary");
            summary.tooltip = "Return to overview";
            summary.SetEnabled(!_interactionsLocked);
            summary.RegisterCallback<ClickEvent>(evt =>
            {
                if (_interactionsLocked)
                {
                    evt.StopPropagation();
                    return;
                }

                _selectionCleared?.Invoke();
                evt.StopPropagation();
            });
            SetElementRect(summary, _layoutResult.UnrelatedSummaryRect);
            _nodeLayer.Add(summary);
        }

        private PackageGraphNodeVisualMode GetNodeVisualMode(bool dimmed)
        {
            if (_layoutResult != null && _layoutResult.Mode != PackageGraphLayoutMode.Focus)
            {
                return PackageGraphNodeVisualMode.Overview;
            }

            return dimmed ? PackageGraphNodeVisualMode.Stack : PackageGraphNodeVisualMode.Focus;
        }

        private static bool ShouldShowNodeAction(
            PackageGraphNode node,
            PackageGraphFocus focus,
            bool related,
            PackageGraphLayoutMode layoutMode)
        {
            return node != null &&
                   focus != null &&
                   layoutMode == PackageGraphLayoutMode.Focus &&
                   focus.HasFocus &&
                   related;
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

        private static void SetElementRect(VisualElement element, Rect rect)
        {
            if (element == null)
            {
                return;
            }

            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.width = rect.width;
            element.style.height = rect.height;
        }

        private static Rect LerpRect(Rect start, Rect end, float t)
        {
            return new Rect(
                Mathf.Lerp(start.x, end.x, t),
                Mathf.Lerp(start.y, end.y, t),
                Mathf.Lerp(start.width, end.width, t),
                Mathf.Lerp(start.height, end.height, t));
        }

        private static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static bool AreRectsClose(Rect first, Rect second)
        {
            return Mathf.Abs(first.x - second.x) < 0.5f &&
                   Mathf.Abs(first.y - second.y) < 0.5f &&
                   Mathf.Abs(first.width - second.width) < 0.5f &&
                   Mathf.Abs(first.height - second.height) < 0.5f;
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

        private static string GetSectorClass(PackageGraphSectorLabel label)
        {
            return label != null && !string.IsNullOrWhiteSpace(label.ClassName)
                ? label.ClassName
                : GetRingClass(label != null ? label.Ring : PackageGraphLayoutRing.Foundation);
        }
    }

    internal sealed class PackageGraphEdgeLayer : VisualElement
    {
        private const int CurveSamples = 32;
        private const float AnimationFrameMs = 40f;
        private const float AnimatedDashLength = 12f;
        private const float AnimatedDashGap = 12f;
        private const float EdgeEndpointPadding = 6f;
        private const float MarkerTravelStart = 0.045f;
        private const float MarkerTravelEnd = 0.955f;

        private readonly Dictionary<string, Rect> _nodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        private PackageGraphModel _graph =
            new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
        private PackageGraphFocus _focus = PackageGraphFocus.Create(null, string.Empty);
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
            _focus = focus ?? PackageGraphFocus.Create(_graph, string.Empty);
            CopyNodeRects(nodeRects);
            style.height = canvasHeight;
            _animationEnabled = HasAnimatedEdges();
            UpdateAnimationSchedule();
            MarkDirtyRepaint();
        }

        public void UpdateNodeRects(IReadOnlyDictionary<string, Rect> nodeRects)
        {
            CopyNodeRects(nodeRects);
            MarkDirtyRepaint();
        }

        private void CopyNodeRects(IReadOnlyDictionary<string, Rect> nodeRects)
        {
            _nodeRects.Clear();

            if (nodeRects == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> nodeRect in nodeRects)
            {
                _nodeRects[nodeRect.Key] = nodeRect.Value;
            }
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

            _animationPhase = Mathf.Repeat((float)(EditorApplication.timeSinceStartup * 0.30d), 1f);
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
                if (SupportsDirectionalFlowMarkers(edge.Kind))
                {
                    Color pulseColor = new Color(color.r, color.g, color.b, Mathf.Min(0.64f, color.a + 0.06f));
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

            float distance = Mathf.Max(46f, Vector2.Distance(start, end) * 0.10f);

            if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
            {
                distance += 10f;
            }
            else if (edge.Kind == PackageGraphEdgeKind.SuiteMembership)
            {
                distance += 18f;
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
            return SupportsDirectionalFlowMarkers(kind) ||
                   kind == PackageGraphEdgeKind.Recommended ||
                   kind == PackageGraphEdgeKind.SuiteMembership;
        }

        internal static bool UsesDirectionalFlowMarkersForTests(PackageGraphEdgeKind kind)
        {
            return SupportsDirectionalFlowMarkers(kind);
        }

        internal static bool AnimatesEdgeForTests(PackageGraphEdgeKind kind)
        {
            return ShouldAnimate(kind);
        }

        private static bool SupportsDirectionalFlowMarkers(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.HardDependency ||
                   kind == PackageGraphEdgeKind.IntegrationConnection;
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
                    return new Color(0.34f, 0.70f, 0.98f, 0.76f);
                }

                if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
                {
                    return new Color(0.24f, 0.82f, 0.75f, 0.72f);
                }

                if (edge.Kind == PackageGraphEdgeKind.OptionalCompanion)
                {
                    return new Color(0.62f, 0.75f, 0.84f, 0.44f);
                }

                return new Color(0.48f, 0.74f, 0.78f, 0.50f);
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
                    return 2.1f;
                }

                if (edge.Kind == PackageGraphEdgeKind.IntegrationConnection)
                {
                    return 1.9f;
                }

                return edge.Kind == PackageGraphEdgeKind.OptionalCompanion ? 1.15f : 2.15f;
            }

            if (focusMode)
            {
                return edge.Kind == PackageGraphEdgeKind.OptionalCompanion ? 0.75f : 0.95f;
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
            float offset = emphasized ? 3.0f : 2.2f;
            Color underlay = new Color(0.04f, 0.12f, 0.14f, Mathf.Min(0.34f, color.a * 0.50f));

            painter.strokeColor = underlay;
            painter.lineWidth = Mathf.Max(1.4f, width + 1.1f);
            DrawBezierStroke(painter, start, controlA, controlB, end);

            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(0.85f, width * 0.54f);
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
            return kind == PackageGraphEdgeKind.SuiteMembership
                ? 1
                : 2;
        }

        private static float GetFlowMarkerSize(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 4.8f;
                case PackageGraphEdgeKind.SuiteMembership:
                    return 4.2f;
                default:
                    return 5.0f;
            }
        }

        private static float GetFlowMarkerWidth(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.IntegrationConnection:
                    return 1.0f;
                default:
                    return 1.0f;
            }
        }

        private static float GetFlowMarkerAlpha(PackageGraphEdgeKind kind)
        {
            switch (kind)
            {
                case PackageGraphEdgeKind.SuiteMembership:
                    return 0.48f;
                default:
                    return 0.74f;
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

    internal enum PackageGraphNodeVisualMode
    {
        Overview,
        Focus,
        Stack
    }

    internal sealed class PackageGraphGroupElement : VisualElement
    {
        public PackageGraphGroupElement(
            PackageGraphGroupLayoutNode groupNode,
            PackageGraphLayoutMode layoutMode,
            bool interactionsEnabled,
            Action<PackageGraphGroup> groupFocused)
        {
            if (groupNode == null || groupNode.Group == null)
            {
                throw new ArgumentNullException(nameof(groupNode));
            }

            name = "group-" + groupNode.GroupId;
            AddToClassList("dpi-graph-group");
            AddToClassList("dpi-graph-group--" + GetRingClass(groupNode.Ring));
            EnableInClassList("dpi-graph-group--focused", groupNode.Focused);
            EnableInClassList("dpi-graph-group--collapsed", groupNode.Collapsed);
            EnableInClassList("dpi-graph-group--overview", layoutMode == PackageGraphLayoutMode.Overview);
            EnableInClassList("dpi-graph-group--locked", !interactionsEnabled);
            EnableInClassList("dpi-graph-group--attention", groupNode.UpdateCount > 0 || groupNode.MissingCount > 0);
            tooltip = GetTooltip(groupNode);

            if (interactionsEnabled)
            {
                RegisterCallback<ClickEvent>(evt =>
                {
                    groupFocused?.Invoke(groupNode.Group);
                    evt.StopPropagation();
                });
            }

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-graph-group__header");
            Add(header);

            Image icon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon(groupNode.Group.IconKey),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList("dpi-graph-group__icon");
            header.Add(icon);

            VisualElement titleBlock = new VisualElement();
            titleBlock.AddToClassList("dpi-graph-group__title-block");
            header.Add(titleBlock);

            Label title = new Label(GetTitle(groupNode));
            title.AddToClassList("dpi-graph-group__title");
            titleBlock.Add(title);

            string subtitleText = groupNode.Collapsed && !string.IsNullOrWhiteSpace(groupNode.SummaryLabel)
                ? groupNode.SummaryLabel
                : groupNode.PackageCount + " packages";
            Label subtitle = new Label(subtitleText);
            subtitle.AddToClassList("dpi-graph-group__subtitle");
            titleBlock.Add(subtitle);

            VisualElement stats = new VisualElement();
            stats.AddToClassList("dpi-graph-group__stats");
            stats.Add(CreateStat("Installed", groupNode.InstalledCount, "installed"));
            stats.Add(CreateStat("Missing", groupNode.MissingCount, "missing"));

            if (groupNode.UpdateCount > 0)
            {
                stats.Add(CreateStat("Updates", groupNode.UpdateCount, "update"));
            }

            Add(stats);
        }

        private static string GetTitle(PackageGraphGroupLayoutNode groupNode)
        {
            return groupNode.Group.DisplayName;
        }

        private static Label CreateStat(string label, int count, string className)
        {
            Label stat = new Label(label + " " + count);
            stat.AddToClassList("dpi-graph-group__stat");
            stat.AddToClassList("dpi-graph-group__stat--" + className);
            return stat;
        }

        private static string GetTooltip(PackageGraphGroupLayoutNode groupNode)
        {
            string description = string.IsNullOrWhiteSpace(groupNode.Group.Description)
                ? "Structural package group."
                : groupNode.Group.Description;
            return groupNode.Group.DisplayName + "\n" +
                   description + "\n" +
                   groupNode.PackageCount + " packages, " +
                   groupNode.InstalledCount + " installed, " +
                   groupNode.UpdateCount + " updates";
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

    internal sealed class PackageGraphNodeElement : VisualElement
    {
        private readonly PackageGraphNode _node;

        public PackageGraphNodeElement(
            PackageGraphNode node,
            PackageGraphLayoutRing ring,
            PackageGraphNodeVisualMode visualMode,
            bool selected,
            bool related,
            bool dimmed,
            bool previewed,
            bool showActions,
            bool actionsEnabled,
            bool interactionsEnabled,
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            Action<string> previewPackage,
            Action<string> clearPreviewPackage)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));

            name = node.PackageId;
            AddToClassList("dpi-graph-node");
            AddToClassList("dpi-graph-node--" + GetNodeClass(node.NodeType));
            AddToClassList("dpi-graph-node--" + GetVisualModeClass(visualMode));
            AddToClassList("dpi-graph-node--status-" + GetStatusClass(node.Status));
            AddToClassList("dpi-graph-node--ring-" + GetRingClass(ring));
            EnableInClassList("dpi-graph-node--installed", node.IsInstalled);
            EnableInClassList("dpi-graph-node--actionable", showActions && IsGraphShortcutAction(node.PrimaryAction));
            EnableInClassList("dpi-graph-node--selected", selected);
            EnableInClassList("dpi-graph-node--related", related && !selected);
            EnableInClassList("dpi-graph-node--dimmed", dimmed);
            EnableInClassList("dpi-graph-node--locked", !interactionsEnabled);
            EnableInClassList("dpi-graph-node--previewed", previewed);
            EnableInClassList("dpi-graph-node--missing", !node.IsRegistered);
            tooltip = GetTooltip(node);

            VisualElement statusRail = new VisualElement();
            statusRail.AddToClassList("dpi-graph-node__status-rail");
            statusRail.AddToClassList("dpi-graph-node__status-rail--" + GetStatusClass(node.Status));
            Add(statusRail);

            if (interactionsEnabled)
            {
                RegisterCallback<MouseEnterEvent>(_ => previewPackage?.Invoke(node.PackageId));
                RegisterCallback<MouseLeaveEvent>(_ => clearPreviewPackage?.Invoke(node.PackageId));
            }

            if (node.PackageDefinition != null && packageSelected != null)
            {
                RegisterCallback<ClickEvent>(evt =>
                {
                    if (!interactionsEnabled)
                    {
                        evt.StopPropagation();
                        return;
                    }

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

            Label statusMarker = new Label(GetStatusMarker(node.Status));
            statusMarker.AddToClassList("dpi-graph-node__status-icon");
            statusMarker.AddToClassList("dpi-graph-node__status-icon--" + GetStatusClass(node.Status));
            header.Add(statusMarker);

            VisualElement titleBlock = new VisualElement();
            titleBlock.AddToClassList("dpi-graph-node__title-block");
            header.Add(titleBlock);

            Label title = new Label(node.DisplayName);
            title.AddToClassList("dpi-graph-node__title");
            titleBlock.Add(title);

            bool fullCard = visualMode == PackageGraphNodeVisualMode.Focus;

            if (fullCard)
            {
                Label packageId = new Label(node.PackageId);
                packageId.AddToClassList("dpi-graph-node__package-id");
                titleBlock.Add(packageId);
            }

            VisualElement badges = new VisualElement();
            badges.AddToClassList("dpi-graph-node__badges");
            if (fullCard)
            {
                badges.Add(CreateBadge(GetNodeTypeLabel(node.NodeType), "dpi-graph-node__badge--type"));
            }
            badges.Add(CreateBadge(GetStatusLabel(node), GetStatusBadgeClass(node.Status)));
            Add(badges);

            if (!fullCard)
            {
                return;
            }

            VisualElement footer = new VisualElement();
            footer.AddToClassList("dpi-graph-node__footer");
            Add(footer);
            Label channel = new Label(node.SelectedChannel.ToString());
            channel.AddToClassList("dpi-graph-node__channel");
            footer.Add(channel);

            if (showActions && IsGraphShortcutAction(node.PrimaryAction))
            {
                Button actionButton = new Button(() =>
                {
                    if (actionsEnabled && interactionsEnabled)
                    {
                        packageAction?.Invoke(node.PackageDefinition, node.PrimaryAction);
                    }
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

        private static string GetVisualModeClass(PackageGraphNodeVisualMode visualMode)
        {
            switch (visualMode)
            {
                case PackageGraphNodeVisualMode.Overview:
                    return "overview";
                case PackageGraphNodeVisualMode.Stack:
                    return "stack";
                default:
                    return "focus";
            }
        }

        private static bool IsGraphShortcutAction(PackageGraphNodeAction action)
        {
            switch (action)
            {
                case PackageGraphNodeAction.Install:
                case PackageGraphNodeAction.Update:
                case PackageGraphNodeAction.Reinstall:
                    return true;
                default:
                    return false;
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

        private static string GetStatusMarker(PackageGraphNodeStatus status)
        {
            switch (status)
            {
                case PackageGraphNodeStatus.Missing:
                    return "!";
                case PackageGraphNodeStatus.NotInstalled:
                    return "PKG";
                case PackageGraphNodeStatus.UpdateAvailable:
                    return "UP";
                case PackageGraphNodeStatus.Checking:
                    return "...";
                case PackageGraphNodeStatus.Warning:
                    return "!";
                default:
                    return "OK";
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
