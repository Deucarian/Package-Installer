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
    internal static class PackageGraphRouteCacheIdentity
    {
        internal static string BuildRouteLayoutSignature(
            IReadOnlyDictionary<string, Rect> nodeRects,
            IReadOnlyDictionary<string, Rect> groupRects)
        {
            StringBuilder builder = new StringBuilder(512);
            AppendRectDictionarySignature(builder, "n", nodeRects);
            AppendRectDictionarySignature(builder, "g", groupRects);
            return builder.ToString();
        }

        internal static void AppendRectDictionarySignature(
            StringBuilder builder,
            string prefix,
            IReadOnlyDictionary<string, Rect> rects)
        {
            if (rects == null || rects.Count == 0)
            {
                builder.Append(prefix);
                builder.Append(":0|");
                return;
            }

            List<string> keys = new List<string>(rects.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            builder.Append(prefix);
            builder.Append(':');
            builder.Append(keys.Count);
            builder.Append('|');

            foreach (string key in keys)
            {
                if (!rects.TryGetValue(key, out Rect rect))
                {
                    continue;
                }

                builder.Append(key);
                builder.Append('=');
                AppendRectKey(builder, rect);
                builder.Append(';');
            }
        }

        internal static string BuildRouteFocusGraphSignature(
            PackageGraphModel graph,
            PackageGraphFocus focus)
        {
            StringBuilder builder = new StringBuilder(128);
            builder.Append("hasFocus=");
            builder.Append(focus != null && focus.HasFocus ? '1' : '0');
            builder.Append("|focus=");
            builder.Append(focus != null ? focus.FocusPackageId ?? string.Empty : string.Empty);
            builder.Append("|nodes=");
            builder.Append(graph != null ? graph.Nodes.Count : 0);
            builder.Append("|edges=");
            builder.Append(graph != null ? graph.Edges.Count : 0);
            builder.Append("|visibleEdges=");
            builder.Append(focus != null && focus.VisibleEdgeKeys != null ? focus.VisibleEdgeKeys.Count : 0);
            return builder.ToString();
        }

        internal static PackageGraphEdgeRouteCacheKey CreateRouteCacheKey(
            string layoutSignature,
            string focusGraphSignature,
            PackageGraphConnectionBundle bundle,
            PackageGraphEdgeRoutePort sourcePort,
            PackageGraphEdgeRoutePort targetPort,
            PackageGraphEdgeRouteZone zone,
            string sharedTrunkId,
            int branchIndex,
            int branchCount,
            IReadOnlyList<Vector2> preferredPoints)
        {
            string identityKey = bundle.Key;
            StringBuilder styleBuilder = new StringBuilder(96);
            styleBuilder.Append("sp=");
            styleBuilder.Append((int)sourcePort);
            styleBuilder.Append("|tp=");
            styleBuilder.Append((int)targetPort);
            styleBuilder.Append("|z=");
            styleBuilder.Append((int)zone);
            styleBuilder.Append("|tr=");
            styleBuilder.Append(sharedTrunkId ?? string.Empty);
            styleBuilder.Append("|bi=");
            styleBuilder.Append(branchIndex);
            styleBuilder.Append("|bc=");
            styleBuilder.Append(branchCount);

            StringBuilder endpointBuilder = new StringBuilder(128);

            if (preferredPoints != null)
            {
                foreach (Vector2 point in preferredPoints)
                {
                    AppendPointKey(endpointBuilder, point);
                    endpointBuilder.Append(';');
                }
            }

            return new PackageGraphEdgeRouteCacheKey(
                identityKey,
                focusGraphSignature,
                layoutSignature,
                endpointBuilder.ToString(),
                styleBuilder.ToString());
        }

        internal static void AppendRectKey(StringBuilder builder, Rect rect)
        {
            AppendRoundedFloat(builder, rect.x);
            builder.Append(',');
            AppendRoundedFloat(builder, rect.y);
            builder.Append(',');
            AppendRoundedFloat(builder, rect.width);
            builder.Append(',');
            AppendRoundedFloat(builder, rect.height);
        }

        internal static void AppendPointKey(StringBuilder builder, Vector2 point)
        {
            AppendRoundedFloat(builder, point.x);
            builder.Append(',');
            AppendRoundedFloat(builder, point.y);
        }

        internal static void AppendRoundedFloat(StringBuilder builder, float value)
        {
            builder.Append(Mathf.RoundToInt(value));
        }
    }
}
