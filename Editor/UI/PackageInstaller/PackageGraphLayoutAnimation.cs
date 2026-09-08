using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphLayoutAnimation
    {
        internal readonly Dictionary<string, Rect> Nodes =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, PackageGraphNodeVisualState> NodeStates =
            new Dictionary<string, PackageGraphNodeVisualState>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, Rect> Groups =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, Vector2> GroupCenters =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, float> GroupOrbitRadii =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, Rect> StartNodes =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, PackageGraphNodeVisualState> StartNodeStates =
            new Dictionary<string, PackageGraphNodeVisualState>(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> EnteringNodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, Rect> StartGroups =
            new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, Vector2> StartGroupCenters =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, float> StartGroupOrbitRadii =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private PackageGraphLayoutResult _layoutResult;

        internal bool Begin(
            PackageGraphLayoutResult targetLayout, PackageGraphTransitionOrigins origins,
            IReadOnlyDictionary<string, Rect> previousRects,
            IReadOnlyDictionary<string, PackageGraphNodeVisualState> previousNodeVisualStates,
            IReadOnlyDictionary<string, Rect> previousGroupRects,
            IReadOnlyDictionary<string, Vector2> previousGroupCenters,
            IReadOnlyDictionary<string, float> previousGroupOrbitRadii)
        {
            _layoutResult = targetLayout;

            Nodes.Clear();
            NodeStates.Clear();
            Groups.Clear();
            GroupCenters.Clear();
            GroupOrbitRadii.Clear();
            StartNodes.Clear();
            StartNodeStates.Clear();
            EnteringNodes.Clear();
            StartGroups.Clear();
            StartGroupCenters.Clear();
            StartGroupOrbitRadii.Clear();
            bool shouldAnimate = false;
            bool hasPreviousNodeFrame = previousRects != null && previousRects.Count > 0;
            bool hasPreviousGroupFrame = previousGroupRects != null && previousGroupRects.Count > 0;

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                Rect previous = default(Rect);
                bool hasPreviousNodeRect = hasPreviousNodeFrame &&
                                           previousRects.TryGetValue(target.Key, out previous);
                bool entering = !hasPreviousNodeRect && (hasPreviousNodeFrame || hasPreviousGroupFrame);
                Rect start = !entering
                    ? hasPreviousNodeRect ? previous : target.Value
                    : origins.CreateEnteringNodeStartRect(target.Key, target.Value, previousGroupRects);
                PackageGraphNodePresentationLevel presentationLevel = GetPresentationLevel(target.Key);
                PackageGraphNodeVisualState startState =
                    !entering &&
                    previousNodeVisualStates != null &&
                    previousNodeVisualStates.TryGetValue(target.Key, out PackageGraphNodeVisualState previousVisualState)
                        ? previousVisualState.WithRect(start)
                        : new PackageGraphNodeVisualState(
                            start,
                            entering ? 0f : 1f,
                            entering ? 0.24f : 1f,
                            presentationLevel,
                            true,
                            entering,
                            false);
                StartNodes[target.Key] = start;
                Nodes[target.Key] = start;
                StartNodeStates[target.Key] = startState;
                NodeStates[target.Key] = startState;

                if (entering)
                {
                    EnteringNodes.Add(target.Key);
                }

                if (!PackageGraphTransitionGeometry.AreRectsClose(start, target.Value) || entering)
                {
                    shouldAnimate = true;
                }
            }

            foreach (PackageGraphGroupLayoutNode target in _layoutResult.GroupNodes)
            {
                if (target == null)
                {
                    continue;
                }

                Rect previous = default(Rect);
                bool hasPreviousGroupRect = hasPreviousGroupFrame &&
                                            previousGroupRects != null &&
                                            previousGroupRects.TryGetValue(target.GroupId, out previous);
                Rect start = hasPreviousGroupRect
                    ? previous
                    : hasPreviousGroupFrame
                        ? origins.CreateEnteringGroupStartRect(target, previousGroupRects)
                        : target.Rect;
                Vector2 startCenter = previousGroupCenters != null &&
                                      previousGroupCenters.TryGetValue(target.GroupId, out Vector2 previousCenter)
                    ? previousCenter
                    : PackageGraphTransitionGeometry.GetGroupHubRect(target, start).center;
                float previousRadius = 0f;
                bool hasPreviousRadius = hasPreviousGroupFrame &&
                                         previousGroupOrbitRadii != null &&
                                         previousGroupOrbitRadii.TryGetValue(target.GroupId, out previousRadius);
                bool enteringGroup = hasPreviousGroupFrame && !hasPreviousRadius;
                float startRadius = !hasPreviousGroupFrame
                    ? target.OrbitRadius
                    : enteringGroup
                        ? target.OrbitRadius * 0.24f
                        : previousRadius;
                StartGroups[target.GroupId] = start;
                Groups[target.GroupId] = start;
                StartGroupCenters[target.GroupId] = startCenter;
                GroupCenters[target.GroupId] = startCenter;
                StartGroupOrbitRadii[target.GroupId] = startRadius;
                GroupOrbitRadii[target.GroupId] = startRadius;

                if (!PackageGraphTransitionGeometry.AreRectsClose(start, target.Rect) ||
                    Vector2.Distance(startCenter, target.HubCenter) > 0.25f ||
                    Mathf.Abs(startRadius - target.OrbitRadius) > 0.25f)
                {
                    shouldAnimate = true;
                }
            }

            return shouldAnimate;
        }

        internal void Evaluate(float linearProgress)
        {
            if (_layoutResult == null)
            {
                return;
            }

            float eased = PackageGraphTransitionGeometry.SmoothStep(Mathf.Clamp01(linearProgress));

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                Rect start = StartNodes.TryGetValue(target.Key, out Rect startRect)
                    ? startRect
                    : target.Value;
                Nodes[target.Key] = PackageGraphTransitionGeometry.LerpRect(start, target.Value, eased);
                PackageGraphNodePresentationLevel presentationLevel = GetPresentationLevel(target.Key);
                bool entering = EnteringNodes.Contains(target.Key);
                float opacity = entering ? PackageGraphTransitionGeometry.EvaluateEnteringOpacity(eased) : 1f;
                float scale = entering ? PackageGraphTransitionGeometry.EvaluateEnteringScale(eased) : 1f;
                NodeStates[target.Key] = new PackageGraphNodeVisualState(
                    Nodes[target.Key],
                    opacity,
                    scale,
                    presentationLevel,
                    true,
                    entering,
                    false);
            }

            foreach (PackageGraphGroupLayoutNode target in _layoutResult.GroupNodes)
            {
                if (target == null)
                {
                    continue;
                }

                Rect start = StartGroups.TryGetValue(target.GroupId, out Rect startRect)
                    ? startRect
                    : target.Rect;
                Vector2 startCenter = StartGroupCenters.TryGetValue(
                    target.GroupId,
                    out Vector2 transitionStartCenter)
                    ? transitionStartCenter
                    : PackageGraphTransitionGeometry.GetGroupHubRect(target, start).center;
                float startRadius = StartGroupOrbitRadii.TryGetValue(
                    target.GroupId,
                    out float transitionStartRadius)
                    ? transitionStartRadius
                    : target.OrbitRadius;
                Vector2 center = Vector2.Lerp(startCenter, target.HubCenter, eased);
                Rect lerpedRect = PackageGraphTransitionGeometry.LerpRect(start, target.Rect, eased);
                GroupCenters[target.GroupId] = center;
                Groups[target.GroupId] = PackageGraphTransitionGeometry.PositionGroupRectFromHubCenter(target, lerpedRect, center);
                GroupOrbitRadii[target.GroupId] = Mathf.Lerp(startRadius, target.OrbitRadius, eased);
            }

        }

        internal void Snap(PackageGraphLayoutResult targetLayout)
        {
            _layoutResult = targetLayout;
            Nodes.Clear();
            NodeStates.Clear();
            Groups.Clear();
            GroupCenters.Clear();
            GroupOrbitRadii.Clear();

            if (_layoutResult == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> target in _layoutResult.NodeRects)
            {
                Nodes[target.Key] = target.Value;
                NodeStates[target.Key] = PackageGraphNodeVisualState.Stable(
                    target.Value,
                    GetPresentationLevel(target.Key));
            }

            foreach (PackageGraphGroupLayoutNode target in _layoutResult.GroupNodes)
            {
                if (target != null)
                {
                    Groups[target.GroupId] = target.Rect;
                    GroupCenters[target.GroupId] = target.HubCenter;
                    GroupOrbitRadii[target.GroupId] = target.OrbitRadius;
                }
            }

        }

        internal PackageGraphNodePresentationLevel GetPresentationLevel(string packageId)
        {
            return _layoutResult != null &&
                   !string.IsNullOrWhiteSpace(packageId) &&
                   _layoutResult.NodePresentationLevels.TryGetValue(
                       packageId,
                       out PackageGraphNodePresentationLevel presentationLevel)
                ? presentationLevel
                : PackageGraphNodePresentationLevel.Compact;
        }

        internal void SyncNodeVisualStateRects()
        {
            foreach (KeyValuePair<string, Rect> rect in Nodes)
            {
                if (NodeStates.TryGetValue(rect.Key, out PackageGraphNodeVisualState state))
                {
                    NodeStates[rect.Key] = state.WithRect(rect.Value);
                    continue;
                }

                NodeStates[rect.Key] = PackageGraphNodeVisualState.Stable(
                    rect.Value,
                    GetPresentationLevel(rect.Key));
            }
        }

        internal void RebuildStableNodeVisualStates(IReadOnlyDictionary<string, Rect> rects)
        {
            NodeStates.Clear();

            if (rects == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Rect> rect in rects)
            {
                NodeStates[rect.Key] = PackageGraphNodeVisualState.Stable(
                    rect.Value,
                    GetPresentationLevel(rect.Key));
            }
        }
    }
}
