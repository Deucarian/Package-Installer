using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphView : VisualElement
    {
        private readonly PackageGraphCanvas _canvas;

        public PackageGraphView(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction)
        {
            AddToClassList("dpi-ecosystem-graph");

            VisualElement legend = new VisualElement();
            legend.AddToClassList("dpi-ecosystem-graph__legend");
            legend.Add(CreateLegendItem("Solid", "Hard dependency", "dpi-graph-legend__line--solid"));
            legend.Add(CreateLegendItem("Dashed", "Bridge / integration", "dpi-graph-legend__line--dashed"));
            legend.Add(CreateLegendItem("Bright", "Installed / active", "dpi-graph-legend__line--active"));
            legend.Add(CreateLegendItem("Dim", "Available path", "dpi-graph-legend__line--possible"));
            Add(legend);

            ScrollView scrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scrollView.AddToClassList("dpi-ecosystem-graph__scroll");
            Add(scrollView);

            _canvas = new PackageGraphCanvas(packageSelected, packageAction);
            scrollView.Add(_canvas);
        }

        public void SetGraph(PackageGraphModel graph, string selectedPackageId, bool actionsEnabled)
        {
            _canvas.SetGraph(graph, selectedPackageId, actionsEnabled);
        }

        private static VisualElement CreateLegendItem(string marker, string label, string markerClass)
        {
            VisualElement item = new VisualElement();
            item.AddToClassList("dpi-graph-legend__item");

            Label markerLabel = new Label(marker);
            markerLabel.AddToClassList("dpi-graph-legend__line");
            markerLabel.AddToClassList(markerClass);
            item.Add(markerLabel);

            Label labelElement = new Label(label);
            labelElement.AddToClassList("dpi-graph-legend__label");
            item.Add(labelElement);

            return item;
        }
    }

    internal sealed class PackageGraphCanvas : VisualElement
    {
        private const float CanvasWidth = 1380f;
        private const float MinimumCanvasHeight = 760f;
        private const float NodeWidth = 258f;
        private const float NodeHeight = 118f;
        private const float NodeSpacing = 24f;
        private const float ToolLaneX = 36f;
        private const float CoreLaneX = 36f;
        private const float RuntimeLaneX = 372f;
        private const float BridgeLaneX = 704f;
        private const float SuiteLaneX = 1038f;

        private readonly Action<PackageDefinition> _packageSelected;
        private readonly Action<PackageDefinition, PackageGraphNodeAction> _packageAction;
        private readonly VisualElement _suiteLayer;
        private readonly PackageGraphEdgeLayer _edgeLayer;
        private readonly VisualElement _nodeLayer;
        private readonly Dictionary<string, Rect> _nodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        private PackageGraphModel _graph =
            new PackageGraphModel(Array.Empty<PackageGraphNode>(), Array.Empty<PackageGraphEdge>(), Array.Empty<PackageGraphSuiteRegion>());
        private string _selectedPackageId = string.Empty;
        private bool _actionsEnabled;
        private float _canvasHeight = MinimumCanvasHeight;

        public PackageGraphCanvas(
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction)
        {
            _packageSelected = packageSelected;
            _packageAction = packageAction;
            name = "ecosystem-graph-canvas";
            AddToClassList("dpi-ecosystem-graph__canvas");
            style.width = CanvasWidth;
            style.height = MinimumCanvasHeight;

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

        public void SetGraph(PackageGraphModel graph, string selectedPackageId, bool actionsEnabled)
        {
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _selectedPackageId = selectedPackageId ?? string.Empty;
            _actionsEnabled = actionsEnabled;
            Rebuild();
        }

        private void Rebuild()
        {
            _nodeRects.Clear();
            _suiteLayer.Clear();
            _nodeLayer.Clear();

            IReadOnlyList<PackageGraphNode> nodes = _graph.Nodes;
            CalculateNodeLayout(nodes);
            style.height = _canvasHeight;
            _suiteLayer.style.height = _canvasHeight;
            _edgeLayer.style.height = _canvasHeight;
            _nodeLayer.style.height = _canvasHeight;

            DrawSuiteRegions();

            foreach (PackageGraphNode node in nodes)
            {
                if (!_nodeRects.TryGetValue(node.PackageId, out Rect rect))
                {
                    continue;
                }

                bool selected = string.Equals(node.PackageId, _selectedPackageId, StringComparison.OrdinalIgnoreCase);
                PackageGraphNodeElement nodeElement = new PackageGraphNodeElement(
                    node,
                    selected,
                    _actionsEnabled,
                    _packageSelected,
                    _packageAction);
                nodeElement.style.left = rect.x;
                nodeElement.style.top = rect.y;
                nodeElement.style.width = rect.width;
                nodeElement.style.height = rect.height;
                _nodeLayer.Add(nodeElement);
            }

            _edgeLayer.SetGraph(_graph, _nodeRects, _canvasHeight);
        }

        private void CalculateNodeLayout(IReadOnlyList<PackageGraphNode> nodes)
        {
            float maxHeight = MinimumCanvasHeight;

            PlaceGroup(
                nodes.Where(node => node.NodeType == PackageGraphNodeType.Tool),
                ToolLaneX,
                34f,
                ref maxHeight);
            PlaceGroup(
                nodes.Where(node => node.NodeType == PackageGraphNodeType.Core),
                CoreLaneX,
                232f,
                ref maxHeight);
            PlaceGroup(
                nodes.Where(node => node.NodeType == PackageGraphNodeType.Integration),
                RuntimeLaneX,
                154f,
                ref maxHeight);
            PlaceBridgeGroup(
                nodes.Where(node => node.NodeType == PackageGraphNodeType.Bridge),
                BridgeLaneX,
                154f,
                ref maxHeight);
            PlaceGroup(
                nodes.Where(node => node.NodeType == PackageGraphNodeType.Suite),
                SuiteLaneX,
                270f,
                ref maxHeight);

            _canvasHeight = Mathf.Max(MinimumCanvasHeight, maxHeight + 64f);
        }

        private void PlaceGroup(
            IEnumerable<PackageGraphNode> nodes,
            float x,
            float startY,
            ref float maxHeight)
        {
            float y = startY;

            foreach (PackageGraphNode node in nodes.OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                Rect rect = new Rect(x, y, NodeWidth, NodeHeight);
                _nodeRects[node.PackageId] = rect;
                y += NodeHeight + NodeSpacing;
                maxHeight = Mathf.Max(maxHeight, rect.yMax);
            }
        }

        private void PlaceBridgeGroup(
            IEnumerable<PackageGraphNode> nodes,
            float x,
            float startY,
            ref float maxHeight)
        {
            float y = startY;

            foreach (PackageGraphNode node in nodes.OrderBy(node => GetBridgeAnchorY(node.PackageId)))
            {
                float desiredY = GetBridgeAnchorY(node.PackageId);

                if (desiredY <= 0f)
                {
                    desiredY = y + NodeHeight * 0.5f;
                }

                y = Mathf.Max(y, desiredY - NodeHeight * 0.5f);
                Rect rect = new Rect(x, y, NodeWidth, NodeHeight);
                _nodeRects[node.PackageId] = rect;
                y += NodeHeight + NodeSpacing;
                maxHeight = Mathf.Max(maxHeight, rect.yMax);
            }
        }

        private float GetBridgeAnchorY(string bridgePackageId)
        {
            PackageGraphEdge[] bridgeEdges = _graph.Edges
                .Where(edge =>
                    edge.Kind == PackageGraphEdgeKind.Bridge &&
                    string.Equals(edge.ToPackageId, bridgePackageId, StringComparison.OrdinalIgnoreCase) &&
                    _nodeRects.ContainsKey(edge.FromPackageId))
                .ToArray();

            if (bridgeEdges.Length == 0)
            {
                return 0f;
            }

            return bridgeEdges.Average(edge => _nodeRects[edge.FromPackageId].center.y);
        }

        private void DrawSuiteRegions()
        {
            foreach (PackageGraphSuiteRegion region in _graph.SuiteRegions)
            {
                List<Rect> memberRects = new List<Rect>();

                foreach (string memberPackageId in region.MemberPackageIds)
                {
                    if (_nodeRects.TryGetValue(memberPackageId, out Rect memberRect))
                    {
                        memberRects.Add(memberRect);
                    }
                }

                if (_nodeRects.TryGetValue(region.SuitePackageId, out Rect suiteRect))
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

                bounds.x -= 22f;
                bounds.y -= 26f;
                bounds.width += 44f;
                bounds.height += 52f;

                VisualElement suiteRegion = new VisualElement();
                suiteRegion.AddToClassList("dpi-graph-suite-region");
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

        private string GetSuiteRegionLabel(string suitePackageId)
        {
            PackageGraphNode suiteNode = _graph.Nodes.FirstOrDefault(node =>
                string.Equals(node.PackageId, suitePackageId, StringComparison.OrdinalIgnoreCase));
            return suiteNode != null ? suiteNode.DisplayName + " membership" : "Suite membership";
        }

        private static void StretchToCanvas(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.top = 0f;
            element.style.width = CanvasWidth;
            element.style.height = Length.Percent(100f);
        }
    }

    internal sealed class PackageGraphEdgeLayer : VisualElement
    {
        private const int CurveSamples = 30;

        private readonly Dictionary<string, Rect> _nodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        private PackageGraphModel _graph =
            new PackageGraphModel(Array.Empty<PackageGraphNode>(), Array.Empty<PackageGraphEdge>(), Array.Empty<PackageGraphSuiteRegion>());

        public PackageGraphEdgeLayer()
        {
            generateVisualContent += GenerateEdges;
        }

        public void SetGraph(
            PackageGraphModel graph,
            IReadOnlyDictionary<string, Rect> nodeRects,
            float canvasHeight)
        {
            _graph = graph ?? new PackageGraphModel(
                Array.Empty<PackageGraphNode>(),
                Array.Empty<PackageGraphEdge>(),
                Array.Empty<PackageGraphSuiteRegion>());
            _nodeRects.Clear();

            if (nodeRects != null)
            {
                foreach (KeyValuePair<string, Rect> nodeRect in nodeRects)
                {
                    _nodeRects[nodeRect.Key] = nodeRect.Value;
                }
            }

            style.height = canvasHeight;
            MarkDirtyRepaint();
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
                if (!_nodeRects.TryGetValue(edge.FromPackageId, out Rect fromRect) ||
                    !_nodeRects.TryGetValue(edge.ToPackageId, out Rect toRect))
                {
                    continue;
                }

                DrawEdge(painter, edge, fromRect, toRect);
            }
        }

        private static void DrawEdge(Painter2D painter, PackageGraphEdge edge, Rect fromRect, Rect toRect)
        {
            Vector2 start = GetPort(fromRect, toRect);
            Vector2 end = GetPort(toRect, fromRect);
            float direction = end.x >= start.x ? 1f : -1f;
            float handleLength = Mathf.Max(76f, Mathf.Abs(end.x - start.x) * 0.42f);
            Vector2 controlA = start + new Vector2(handleLength * direction, 0f);
            Vector2 controlB = end - new Vector2(handleLength * direction, 0f);
            Color color = GetEdgeColor(edge);
            float width = GetEdgeWidth(edge);

            painter.strokeColor = color;
            painter.lineWidth = width;

            if (IsDashed(edge.Kind))
            {
                DrawDashedBezier(painter, start, controlA, controlB, end);
            }
            else
            {
                painter.BeginPath();
                painter.MoveTo(start);
                painter.BezierCurveTo(controlA, controlB, end);
                painter.Stroke();
            }

            DrawPortMarker(painter, start, color, width);

            if (edge.State == PackageGraphEdgeState.Warning)
            {
                DrawWarningMarker(painter, GetBezierPoint(start, controlA, controlB, end, 0.5f));
            }
        }

        private static Vector2 GetPort(Rect fromRect, Rect toRect)
        {
            if (toRect.xMin >= fromRect.xMax)
            {
                return new Vector2(fromRect.xMax, fromRect.center.y);
            }

            if (toRect.xMax <= fromRect.xMin)
            {
                return new Vector2(fromRect.xMin, fromRect.center.y);
            }

            if (toRect.center.y >= fromRect.center.y)
            {
                return new Vector2(fromRect.center.x, fromRect.yMax);
            }

            return new Vector2(fromRect.center.x, fromRect.yMin);
        }

        private static bool IsDashed(PackageGraphEdgeKind kind)
        {
            return kind == PackageGraphEdgeKind.Bridge ||
                   kind == PackageGraphEdgeKind.OptionalIntegration ||
                   kind == PackageGraphEdgeKind.Recommended ||
                   kind == PackageGraphEdgeKind.SuiteMembership;
        }

        private static Color GetEdgeColor(PackageGraphEdge edge)
        {
            if (edge.State == PackageGraphEdgeState.Warning)
            {
                return new Color(0.94f, 0.64f, 0.27f, 0.92f);
            }

            if (edge.State == PackageGraphEdgeState.Active)
            {
                return new Color(0.24f, 0.82f, 0.75f, 0.96f);
            }

            if (edge.Kind == PackageGraphEdgeKind.HardDependency)
            {
                return new Color(0.44f, 0.55f, 0.76f, 0.54f);
            }

            return new Color(0.28f, 0.62f, 0.82f, 0.40f);
        }

        private static float GetEdgeWidth(PackageGraphEdge edge)
        {
            if (edge.State == PackageGraphEdgeState.Active)
            {
                return 3.1f;
            }

            if (edge.State == PackageGraphEdgeState.Warning)
            {
                return 2.6f;
            }

            return edge.Kind == PackageGraphEdgeKind.HardDependency ? 2f : 1.55f;
        }

        private static void DrawDashedBezier(
            Painter2D painter,
            Vector2 start,
            Vector2 controlA,
            Vector2 controlB,
            Vector2 end)
        {
            const float dashLength = 12f;
            const float gapLength = 7f;

            bool draw = true;
            float segmentCursor = 0f;
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

        private static void DrawPortMarker(Painter2D painter, Vector2 position, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(1.4f, width);
            painter.BeginPath();
            painter.MoveTo(position + new Vector2(-4f, 0f));
            painter.LineTo(position + new Vector2(4f, 0f));
            painter.Stroke();
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
            bool selected,
            bool actionsEnabled,
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));

            AddToClassList("dpi-graph-node");
            AddToClassList("dpi-graph-node--" + GetNodeClass(node.NodeType));
            AddToClassList("dpi-graph-node--status-" + GetStatusClass(node.Status));
            EnableInClassList("dpi-graph-node--installed", node.IsInstalled);
            EnableInClassList("dpi-graph-node--selected", selected);
            EnableInClassList("dpi-graph-node--missing", !node.IsRegistered);
            tooltip = GetTooltip(node);

            if (node.PackageDefinition != null && packageSelected != null)
            {
                RegisterCallback<ClickEvent>(_ => packageSelected(node.PackageDefinition));
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
                case PackageGraphNodeType.Bridge:
                    return "bridge";
                case PackageGraphNodeType.Suite:
                    return "suite";
                case PackageGraphNodeType.Integration:
                    return "integration";
                default:
                    return "core";
            }
        }

        private static string GetNodeTypeLabel(PackageGraphNodeType nodeType)
        {
            switch (nodeType)
            {
                case PackageGraphNodeType.Tool:
                    return "Tool";
                case PackageGraphNodeType.Bridge:
                    return "Bridge";
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
                    return "Missing";
                case PackageGraphNodeStatus.NotInstalled:
                    return "Not installed";
                case PackageGraphNodeStatus.UpdateAvailable:
                    return "Update";
                case PackageGraphNodeStatus.Checking:
                    return "Checking";
                case PackageGraphNodeStatus.Warning:
                    return "Warning";
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
