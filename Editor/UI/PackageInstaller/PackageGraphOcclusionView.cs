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
    internal sealed class PackageGraphOcclusionView
    {
        private readonly Dictionary<string, VisualElement> _nodeOcclusionElements =
            new Dictionary<string, VisualElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VisualElement> _groupSymbolOcclusionElements =
            new Dictionary<string, VisualElement>(StringComparer.OrdinalIgnoreCase);
        private VisualElement _rootHubOcclusionElement;
        private readonly VisualElement _occlusionLayer;

        internal PackageGraphOcclusionView(VisualElement layer)
        {
            _occlusionLayer = layer ?? throw new ArgumentNullException(nameof(layer));
        }

        internal void Clear()
        {
            _occlusionLayer.Clear();
            _nodeOcclusionElements.Clear();
            _groupSymbolOcclusionElements.Clear();
            _rootHubOcclusionElement = null;
        }

        internal void Build(PackageGraphLayoutResult layout, PackageGraphLayoutAnimation animation)
        {
            VisualElement rootHubOcclusion = new VisualElement
            {
                name = "ecosystem-graph-root-hub-occlusion",
                pickingMode = PickingMode.Ignore
            };
            rootHubOcclusion.AddToClassList("dpi-graph-occlusion");
            rootHubOcclusion.AddToClassList("dpi-graph-occlusion--hub");
            PackageGraphCanvasVisuals.SetElementRect(rootHubOcclusion, layout.HubRect);
            _occlusionLayer.Add(rootHubOcclusion);
            _rootHubOcclusionElement = rootHubOcclusion;

            foreach (PackageGraphGroupLayoutNode groupNode in layout.GroupNodes)
            {
                if (groupNode == null)
                {
                    continue;
                }

                VisualElement symbolOcclusion = new VisualElement
                {
                    name = "group-symbol-occlusion-" + groupNode.GroupId,
                    pickingMode = PickingMode.Ignore
                };
                symbolOcclusion.AddToClassList("dpi-graph-occlusion");
                symbolOcclusion.AddToClassList("dpi-graph-occlusion--group-symbol");
                _occlusionLayer.Add(symbolOcclusion);
                _groupSymbolOcclusionElements[groupNode.GroupId] = symbolOcclusion;
            }

            foreach (KeyValuePair<string, Rect> nodeRect in animation.Nodes)
            {
                VisualElement nodeOcclusion = new VisualElement
                {
                    name = "package-occlusion-" + nodeRect.Key,
                    pickingMode = PickingMode.Ignore
                };
                nodeOcclusion.AddToClassList("dpi-graph-occlusion");
                nodeOcclusion.AddToClassList("dpi-graph-occlusion--package");
                _occlusionLayer.Add(nodeOcclusion);
                _nodeOcclusionElements[nodeRect.Key] = nodeOcclusion;
            }

        }

        internal void Update(PackageGraphLayoutResult layout, PackageGraphLayoutAnimation animation,
            VisualElement rootHub, IReadOnlyDictionary<string, PackageGraphNodeElement> nodes,
            IReadOnlyDictionary<string, PackageGraphGroupElement> groups)
        {
            if (layout == null)
            {
                return;
            }

            if (rootHub != null && _rootHubOcclusionElement != null)
            {
                float rootOpacity = PackageGraphCanvasVisuals.ResolveRootHubOpacity(rootHub);
                rootHub.style.opacity = rootOpacity;
                _rootHubOcclusionElement.style.opacity = rootOpacity;
            }

            foreach (KeyValuePair<string, VisualElement> occlusion in _nodeOcclusionElements)
            {
                float interactionOpacity = nodes.TryGetValue(
                    occlusion.Key,
                    out PackageGraphNodeElement nodeElement)
                    ? PackageGraphCanvasVisuals.ResolvePackageElementOpacity(nodeElement)
                    : 1f;

                if (animation.NodeStates.TryGetValue(occlusion.Key, out PackageGraphNodeVisualState state))
                {
                    float effectiveOpacity = state.Opacity * interactionOpacity;
                    PackageGraphCanvasVisuals.SetElementRect(occlusion.Value, state.Rect);
                    occlusion.Value.style.opacity = effectiveOpacity;
                    occlusion.Value.style.scale = new Scale(new Vector3(state.Scale, state.Scale, 1f));
                    occlusion.Value.style.transformOrigin = new TransformOrigin(
                        new Length(50f, LengthUnit.Percent),
                        new Length(50f, LengthUnit.Percent),
                        0f);

                    if (nodeElement != null)
                    {
                        nodeElement.style.opacity = effectiveOpacity;
                    }
                }
                else if (animation.Nodes.TryGetValue(occlusion.Key, out Rect rect))
                {
                    PackageGraphCanvasVisuals.SetElementRect(occlusion.Value, rect);
                    occlusion.Value.style.opacity = interactionOpacity;
                    occlusion.Value.style.scale = new Scale(Vector3.one);

                    if (nodeElement != null)
                    {
                        nodeElement.style.opacity = interactionOpacity;
                    }
                }
            }

            foreach (PackageGraphGroupLayoutNode groupNode in layout.GroupNodes)
            {
                if (groupNode == null ||
                    !animation.Groups.TryGetValue(groupNode.GroupId, out Rect groupRect))
                {
                    continue;
                }

                Rect hubRect = PackageGraphTransitionGeometry.GetGroupHubRect(groupNode, groupRect);

                if (_groupSymbolOcclusionElements.TryGetValue(groupNode.GroupId, out VisualElement symbol))
                {
                    PackageGraphCanvasVisuals.SetElementRect(symbol, hubRect);

                    if (groups.TryGetValue(groupNode.GroupId, out PackageGraphGroupElement groupElement))
                    {
                        float groupOpacity = PackageGraphCanvasVisuals.ResolveGroupElementOpacity(groupElement);
                        groupElement.style.opacity = groupOpacity;
                        symbol.style.opacity = groupOpacity;
                    }
                }
            }
        }
    }
}
