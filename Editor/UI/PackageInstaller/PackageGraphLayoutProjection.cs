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
    internal sealed class PackageGraphLayoutProjection
    {
        private readonly PackageGraphModel _visibleGraph;
        private readonly ISet<string> _visiblePackageIds;

        internal PackageGraphLayoutProjection(PackageGraphModel visibleGraph, ISet<string> visiblePackageIds)
        {
            _visibleGraph = visibleGraph;
            _visiblePackageIds = visiblePackageIds;
        }

        internal PackageGraphLayoutResult CreateProjectedLayoutResult(
            PackageGraphLayoutResult fullLayoutResult,
            PackageGraphFocus fullFocus)
        {
            if (fullLayoutResult == null)
            {
                return null;
            }

            bool packageFocus = fullLayoutResult.Mode == PackageGraphLayoutMode.Focus;
            Dictionary<string, Rect> projectedNodeRects = fullLayoutResult.NodeRects
                .Where(pair => packageFocus || _visiblePackageIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphLayoutRing> projectedNodeRings = fullLayoutResult.NodeRings
                .Where(pair => packageFocus || _visiblePackageIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageGraphNodePresentationLevel> projectedNodePresentations =
                fullLayoutResult.NodePresentationLevels
                    .Where(pair => packageFocus || _visiblePackageIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            int visibleUnrelatedCount = packageFocus && fullFocus != null
                ? _visibleGraph.Nodes.Count(node => !fullFocus.IsPackageRelated(node.PackageId))
                : 0;
            Rect unrelatedSummaryRect = visibleUnrelatedCount > 0
                ? fullLayoutResult.UnrelatedSummaryRect
                : default(Rect);
            PackageGraphGroupLayoutNode[] projectedGroupNodes = fullLayoutResult.GroupNodes
                .Where(groupNode => groupNode != null)
                .Select(ProjectGroupLayoutNode)
                .ToArray();

            return new PackageGraphLayoutResult(
                fullLayoutResult.Mode,
                fullLayoutResult.FocusPackageId,
                fullLayoutResult.CanvasWidth,
                fullLayoutResult.CanvasHeight,
                fullLayoutResult.HubRect,
                fullLayoutResult.ActiveCenter,
                projectedNodeRects,
                projectedNodeRings,
                fullLayoutResult.RingGuides,
                fullLayoutResult.SectorLabels,
                visibleUnrelatedCount,
                unrelatedSummaryRect,
                projectedGroupNodes,
                fullLayoutResult.FocusGroupId,
                projectedNodePresentations,
                fullLayoutResult.OverflowSummaries);
        }

        internal PackageGraphGroupLayoutNode ProjectGroupLayoutNode(PackageGraphGroupLayoutNode groupNode)
        {
            List<PackageGraphNode> shownPackageList = new List<PackageGraphNode>();

            foreach (string packageId in groupNode.RepresentedPackageIds)
            {
                if (!string.IsNullOrWhiteSpace(packageId) &&
                    _visibleGraph.TryGetNode(packageId, out PackageGraphNode node))
                {
                    shownPackageList.Add(node);
                }
            }

            PackageGraphNode[] shownPackages = shownPackageList.ToArray();
            PackageGraphCategoryStatusSummary statusSummary =
                PackageGraphCategoryStatusSummary.Create(shownPackages);

            return new PackageGraphGroupLayoutNode(
                groupNode.Group,
                groupNode.Rect,
                groupNode.HubRect,
                groupNode.Ring,
                shownPackages.Length,
                statusSummary.InstalledCount,
                statusSummary.NotInstalledCount,
                statusSummary.AttentionCount,
                statusSummary.UnknownCount,
                shownPackages.Count(node => node.Status == PackageGraphNodeStatus.UpdateAvailable),
                groupNode.Focused,
                groupNode.Collapsed,
                groupNode.OrbitRadius,
                groupNode.SummaryLabel,
                groupNode.RepresentedPackageIds);
        }
    }
}
