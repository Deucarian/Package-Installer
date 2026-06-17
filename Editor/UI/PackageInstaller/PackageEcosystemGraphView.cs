using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageEcosystemGraphView : VisualElement
    {
        private const float CanvasWidth = 1160f;
        private const float MinimumCanvasHeight = 660f;
        private const float NodeWidth = 220f;
        private const float NodeHeight = 76f;
        private const float NodeSpacing = 22f;

        private readonly Action<PackageDefinition> _packageSelected;
        private readonly ScrollView _scrollView;
        private readonly VisualElement _canvas;
        private readonly VisualElement _suiteLayer;
        private readonly IMGUIContainer _edgeLayer;
        private readonly VisualElement _nodeLayer;
        private readonly Dictionary<string, Rect> _nodeRects =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

        private PackageEcosystemGraph _graph =
            new PackageEcosystemGraph(Array.Empty<PackageEcosystemNode>(), Array.Empty<PackageEcosystemEdge>(), Array.Empty<PackageEcosystemSuiteRegion>());
        private string _selectedPackageId = string.Empty;
        private float _canvasHeight = MinimumCanvasHeight;

        public PackageEcosystemGraphView(Action<PackageDefinition> packageSelected)
        {
            _packageSelected = packageSelected;
            AddToClassList("dpi-ecosystem-graph");

            _scrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _scrollView.AddToClassList("dpi-ecosystem-graph__scroll");
            Add(_scrollView);

            _canvas = new VisualElement { name = "ecosystem-graph-canvas" };
            _canvas.AddToClassList("dpi-ecosystem-graph__canvas");
            _canvas.style.width = CanvasWidth;
            _canvas.style.height = MinimumCanvasHeight;
            _scrollView.Add(_canvas);

            _suiteLayer = new VisualElement { name = "ecosystem-graph-suite-layer" };
            _suiteLayer.AddToClassList("dpi-ecosystem-graph__suite-layer");
            _suiteLayer.pickingMode = PickingMode.Ignore;
            StretchToCanvas(_suiteLayer);
            _canvas.Add(_suiteLayer);

            _edgeLayer = new IMGUIContainer(DrawEdges) { name = "ecosystem-graph-edge-layer" };
            _edgeLayer.AddToClassList("dpi-ecosystem-graph__edge-layer");
            _edgeLayer.pickingMode = PickingMode.Ignore;
            StretchToCanvas(_edgeLayer);
            _canvas.Add(_edgeLayer);

            _nodeLayer = new VisualElement { name = "ecosystem-graph-node-layer" };
            _nodeLayer.AddToClassList("dpi-ecosystem-graph__node-layer");
            StretchToCanvas(_nodeLayer);
            _canvas.Add(_nodeLayer);
        }

        public void SetGraph(PackageEcosystemGraph graph, string selectedPackageId)
        {
            _graph = graph ?? new PackageEcosystemGraph(
                Array.Empty<PackageEcosystemNode>(),
                Array.Empty<PackageEcosystemEdge>(),
                Array.Empty<PackageEcosystemSuiteRegion>());
            _selectedPackageId = selectedPackageId ?? string.Empty;
            Rebuild();
        }

        private void Rebuild()
        {
            _nodeRects.Clear();
            _suiteLayer.Clear();
            _nodeLayer.Clear();

            IReadOnlyList<PackageEcosystemNode> nodes = _graph.Nodes;
            CalculateNodeLayout(nodes);
            _canvas.style.height = _canvasHeight;

            DrawSuiteRegions();

            foreach (PackageEcosystemNode node in nodes)
            {
                if (!_nodeRects.TryGetValue(node.PackageId, out Rect rect))
                {
                    continue;
                }

                bool selected = string.Equals(node.PackageId, _selectedPackageId, StringComparison.OrdinalIgnoreCase);
                GraphNodeElement nodeElement = new GraphNodeElement(node, selected, _packageSelected);
                nodeElement.style.left = rect.x;
                nodeElement.style.top = rect.y;
                nodeElement.style.width = rect.width;
                nodeElement.style.height = rect.height;
                _nodeLayer.Add(nodeElement);
            }

            _edgeLayer.MarkDirtyRepaint();
        }

        private void CalculateNodeLayout(IReadOnlyList<PackageEcosystemNode> nodes)
        {
            float maxHeight = MinimumCanvasHeight;

            PlaceGroup(
                nodes.Where(node => node.NodeType == PackageGraphNodeType.Core),
                28f,
                58f,
                ref maxHeight);
            PlaceGroup(
                nodes.Where(node => node.NodeType == PackageGraphNodeType.Tool),
                346f,
                42f,
                ref maxHeight);
            PlaceGroup(
                nodes.Where(node => node.NodeType == PackageGraphNodeType.OptionalIntegration),
                346f,
                178f,
                ref maxHeight);
            PlaceGroup(
                nodes.Where(node => node.NodeType == PackageGraphNodeType.Bridge),
                664f,
                120f,
                ref maxHeight);
            PlaceGroup(
                nodes.Where(node => node.NodeType == PackageGraphNodeType.Suite),
                924f,
                272f,
                ref maxHeight);

            _canvasHeight = Mathf.Max(MinimumCanvasHeight, maxHeight + 52f);
        }

        private void PlaceGroup(
            IEnumerable<PackageEcosystemNode> nodes,
            float x,
            float startY,
            ref float maxHeight)
        {
            float y = startY;

            foreach (PackageEcosystemNode node in nodes.OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                Rect rect = new Rect(x, y, NodeWidth, NodeHeight);
                _nodeRects[node.PackageId] = rect;
                y += NodeHeight + NodeSpacing;
                maxHeight = Mathf.Max(maxHeight, rect.yMax);
            }
        }

        private void DrawSuiteRegions()
        {
            foreach (PackageEcosystemSuiteRegion region in _graph.SuiteRegions)
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

                bounds.x -= 18f;
                bounds.y -= 18f;
                bounds.width += 36f;
                bounds.height += 36f;

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
            PackageEcosystemNode suiteNode = _graph.Nodes.FirstOrDefault(node =>
                string.Equals(node.PackageId, suitePackageId, StringComparison.OrdinalIgnoreCase));
            return suiteNode != null ? suiteNode.DisplayName : "Suite";
        }

        private void DrawEdges()
        {
            if (_graph == null || _graph.Edges.Count == 0)
            {
                return;
            }

            Handles.BeginGUI();
            Color previousHandlesColor = Handles.color;

            foreach (PackageEcosystemEdge edge in _graph.Edges)
            {
                if (!_nodeRects.TryGetValue(edge.FromPackageId, out Rect fromRect) ||
                    !_nodeRects.TryGetValue(edge.ToPackageId, out Rect toRect))
                {
                    continue;
                }

                Vector2 start = GetPort(fromRect, toRect);
                Vector2 end = GetPort(toRect, fromRect);
                Vector2 midA = new Vector2((start.x + end.x) * 0.5f, start.y);
                Vector2 midB = new Vector2((start.x + end.x) * 0.5f, end.y);
                Color color = GetEdgeColor(edge);
                float width = GetEdgeWidth(edge);

                if (IsDashed(edge.Kind))
                {
                    DrawDashedPolyline(color, width, start, midA, midB, end);
                }
                else
                {
                    Handles.color = color;
                    Handles.DrawAAPolyLine(
                        width,
                        new Vector3(start.x, start.y),
                        new Vector3(midA.x, midA.y),
                        new Vector3(midB.x, midB.y),
                        new Vector3(end.x, end.y));
                }
            }

            Handles.color = previousHandlesColor;
            Handles.EndGUI();
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

        private static Color GetEdgeColor(PackageEcosystemEdge edge)
        {
            if (edge.State == PackageGraphEdgeState.Warning)
            {
                return new Color(0.86f, 0.61f, 0.28f, 0.92f);
            }

            if (edge.State == PackageGraphEdgeState.Active)
            {
                return new Color(0.23f, 0.65f, 0.60f, 0.95f);
            }

            return edge.Kind == PackageGraphEdgeKind.HardDependency
                ? new Color(0.42f, 0.50f, 0.66f, 0.48f)
                : new Color(0.34f, 0.56f, 0.72f, 0.36f);
        }

        private static float GetEdgeWidth(PackageEcosystemEdge edge)
        {
            if (edge.State == PackageGraphEdgeState.Active)
            {
                return 3f;
            }

            if (edge.State == PackageGraphEdgeState.Warning)
            {
                return 2.4f;
            }

            return edge.Kind == PackageGraphEdgeKind.HardDependency ? 1.8f : 1.4f;
        }

        private static void DrawDashedPolyline(Color color, float width, params Vector2[] points)
        {
            for (int index = 0; index < points.Length - 1; index++)
            {
                DrawDashedLine(points[index], points[index + 1], color, width);
            }
        }

        private static void DrawDashedLine(Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;

            if (length <= 0.1f)
            {
                return;
            }

            Vector2 direction = delta / length;
            const float dashLength = 8f;
            const float gapLength = 5f;
            float cursor = 0f;

            Handles.color = color;

            while (cursor < length)
            {
                float segmentEnd = Mathf.Min(cursor + dashLength, length);
                Vector2 segmentStart = start + direction * cursor;
                Vector2 segmentStop = start + direction * segmentEnd;
                Handles.DrawAAPolyLine(
                    width,
                    new Vector3(segmentStart.x, segmentStart.y),
                    new Vector3(segmentStop.x, segmentStop.y));
                cursor += dashLength + gapLength;
            }
        }

        private static void StretchToCanvas(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.top = 0f;
            element.style.width = CanvasWidth;
            element.style.height = Length.Percent(100f);
        }

        private sealed class GraphNodeElement : VisualElement
        {
            public GraphNodeElement(
                PackageEcosystemNode node,
                bool selected,
                Action<PackageDefinition> packageSelected)
            {
                AddToClassList("dpi-graph-node");
                AddToClassList("dpi-graph-node--" + GetNodeClass(node.NodeType));
                EnableInClassList("dpi-graph-node--installed", node.IsInstalled);
                EnableInClassList("dpi-graph-node--selected", selected);
                EnableInClassList("dpi-graph-node--missing", !node.IsRegistered);
                tooltip = GetTooltip(node);

                if (node.PackageDefinition != null && packageSelected != null)
                {
                    RegisterCallback<ClickEvent>(_ => packageSelected(node.PackageDefinition));
                }

                Label title = new Label(node.DisplayName);
                title.AddToClassList("dpi-graph-node__title");
                Add(title);

                VisualElement badges = new VisualElement();
                badges.AddToClassList("dpi-graph-node__badges");
                badges.Add(CreateBadge(GetNodeTypeLabel(node.NodeType), "dpi-graph-node__badge--type"));
                badges.Add(CreateBadge(GetStatusLabel(node), GetStatusClass(node)));
                Add(badges);

                Label packageId = new Label(node.PackageId);
                packageId.AddToClassList("dpi-graph-node__package-id");
                Add(packageId);
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
                    case PackageGraphNodeType.OptionalIntegration:
                        return "optional";
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
                    case PackageGraphNodeType.OptionalIntegration:
                        return "Optional";
                    default:
                        return "Core";
                }
            }

            private static string GetStatusLabel(PackageEcosystemNode node)
            {
                if (!node.IsRegistered)
                {
                    return "Missing";
                }

                if (node.IsInstalled)
                {
                    return "Installed";
                }

                return node.HasPackageReference ? "Available" : "No URL";
            }

            private static string GetStatusClass(PackageEcosystemNode node)
            {
                if (!node.IsRegistered)
                {
                    return "dpi-graph-node__badge--warning";
                }

                if (node.IsInstalled)
                {
                    return "dpi-graph-node__badge--installed";
                }

                return node.HasPackageReference
                    ? "dpi-graph-node__badge--available"
                    : "dpi-graph-node__badge--warning";
            }

            private static string GetTooltip(PackageEcosystemNode node)
            {
                if (string.IsNullOrWhiteSpace(node.Description))
                {
                    return node.PackageId;
                }

                return node.DisplayName + "\n" + node.Description + "\n" + node.PackageId;
            }
        }
    }
}
