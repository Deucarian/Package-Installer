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
    internal static class PackageGraphMembershipRoutePlanner
    {
        internal static PackageGraphStructuralMembershipRoute CreateStructuralMembershipRoute(
            string groupId,
            Rect groupPortRect,
            Rect groupContentRect,
            IReadOnlyList<KeyValuePair<string, Rect>> packageRects)
        {
            Vector2 packageAverage = PackageGraphMembershipSegments.CalculateAverageRectCenter(packageRects.Select(pair => pair.Value));
            Vector2 direction = packageAverage - groupPortRect.center;
            bool horizontalBus = Mathf.Abs(direction.y) >= Mathf.Abs(direction.x);
            List<PackageGraphStructuralMembershipSegment> segments = new List<PackageGraphStructuralMembershipSegment>();
            string[] packageIds = packageRects.Select(pair => pair.Key).ToArray();

            if (packageRects.Count == 1)
            {
                AddDirectStructuralSegments(
                    segments,
                    groupPortRect,
                    groupContentRect,
                    packageRects[0].Value,
                    packageRects[0].Key);
                return new PackageGraphStructuralMembershipRoute(groupId, packageIds, segments, usesBus: false);
            }

            if (horizontalBus)
            {
                bool packagesBelow = direction.y >= 0f;
                KeyValuePair<string, Rect>[] ordered = packageRects
                    .OrderBy(pair => pair.Value.center.x)
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                float facingPackageEdge = packagesBelow
                    ? ordered.Min(pair => pair.Value.yMin)
                    : ordered.Max(pair => pair.Value.yMax);
                float categoryBoundary = packagesBelow
                    ? Mathf.Max(groupContentRect.yMax, groupPortRect.yMax)
                    : groupPortRect.yMin;
                float busY = PackageGraphMembershipSegments.GetClearBusAxis(
                    facingPackageEdge,
                    categoryBoundary,
                    PackageGraphMembershipMetrics.StructuralMembershipBusClearance);
                float categoryJoinX;

                if (packagesBelow)
                {
                    bool exitRight = packageAverage.x >= groupPortRect.center.x;
                    Vector2 categoryAnchor = new Vector2(
                        exitRight ? groupPortRect.xMax : groupPortRect.xMin,
                        groupPortRect.center.y);
                    categoryJoinX = exitRight
                        ? Mathf.Max(groupContentRect.xMax, groupPortRect.xMax) + PackageGraphMembershipMetrics.StructuralMembershipBusClearance
                        : Mathf.Min(groupContentRect.xMin, groupPortRect.xMin) - PackageGraphMembershipMetrics.StructuralMembershipBusClearance;
                    Vector2 escapedCategory = new Vector2(categoryJoinX, categoryAnchor.y);
                    segments.Add(new PackageGraphStructuralMembershipSegment(
                        categoryAnchor,
                        escapedCategory,
                        packageIds));
                    segments.Add(new PackageGraphStructuralMembershipSegment(
                        escapedCategory,
                        new Vector2(categoryJoinX, busY),
                        packageIds));
                }
                else
                {
                    Vector2 categoryAnchor = new Vector2(
                        groupPortRect.center.x,
                        groupPortRect.yMin);
                    categoryJoinX = categoryAnchor.x;
                    PackageGraphMembershipSegments.AddCategoryToHorizontalBusSegments(
                        segments,
                        categoryAnchor,
                        busY,
                        packageIds);
                }

                PackageGraphMembershipSegments.AddSplitStructuralBusSegments(
                    segments,
                    ordered
                        .Select(pair => new KeyValuePair<string, float>(pair.Key, pair.Value.center.x))
                        .ToArray(),
                    categoryJoinX,
                    axis => new Vector2(axis, busY));

                foreach (KeyValuePair<string, Rect> package in ordered)
                {
                    Vector2 branch = new Vector2(package.Value.center.x, busY);
                    Vector2 endpoint = new Vector2(
                        package.Value.center.x,
                        packagesBelow ? package.Value.yMin : package.Value.yMax);
                    segments.Add(new PackageGraphStructuralMembershipSegment(
                        branch,
                        endpoint,
                        package.Key));
                }

                return new PackageGraphStructuralMembershipRoute(groupId, packageIds, segments, usesBus: true);
            }

            KeyValuePair<string, Rect>[] verticalOrdered = packageRects
                .OrderBy(pair => pair.Value.center.y)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            bool packagesOnRight = direction.x >= 0f;
            Vector2 verticalCategoryAnchor = new Vector2(
                packagesOnRight ? groupPortRect.xMax : groupPortRect.xMin,
                groupPortRect.center.y);
            float facingVerticalPackageEdge = packagesOnRight
                ? verticalOrdered.Min(pair => pair.Value.xMin)
                : verticalOrdered.Max(pair => pair.Value.xMax);
            float busX = PackageGraphMembershipSegments.GetClearBusAxis(
                facingVerticalPackageEdge,
                verticalCategoryAnchor.x,
                PackageGraphMembershipMetrics.StructuralMembershipBusClearance);
            Vector2 trunk = new Vector2(busX, verticalCategoryAnchor.y);
            segments.Add(new PackageGraphStructuralMembershipSegment(verticalCategoryAnchor, trunk, packageIds));
            PackageGraphMembershipSegments.AddSplitStructuralBusSegments(
                segments,
                verticalOrdered
                    .Select(pair => new KeyValuePair<string, float>(pair.Key, pair.Value.center.y))
                    .ToArray(),
                verticalCategoryAnchor.y,
                axis => new Vector2(busX, axis));

            foreach (KeyValuePair<string, Rect> package in verticalOrdered)
            {
                Vector2 branch = new Vector2(busX, package.Value.center.y);
                Vector2 endpoint = new Vector2(
                    packagesOnRight ? package.Value.xMin : package.Value.xMax,
                    package.Value.center.y);
                segments.Add(new PackageGraphStructuralMembershipSegment(
                    branch,
                    endpoint,
                    package.Key));
            }

            return new PackageGraphStructuralMembershipRoute(groupId, packageIds, segments, usesBus: true);
        }

        internal static void AddDirectStructuralSegments(
            ICollection<PackageGraphStructuralMembershipSegment> segments,
            Rect groupPortRect,
            Rect groupContentRect,
            Rect packageRect,
            string packageId)
        {
            Vector2 delta = packageRect.center - groupPortRect.center;

            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                bool packageOnRight = delta.x >= 0f;
                Vector2 from = new Vector2(
                    packageOnRight ? groupPortRect.xMax : groupPortRect.xMin,
                    groupPortRect.center.y);
                Vector2 to = new Vector2(
                    packageOnRight ? packageRect.xMin : packageRect.xMax,
                    packageRect.center.y);

                if (Mathf.Abs(from.y - to.y) <= 0.01f)
                {
                    PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(segments, from, to, packageId);
                    return;
                }

                float elbowX = (from.x + to.x) * 0.5f;
                PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(segments, from, new Vector2(elbowX, from.y), packageId);
                PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(segments, new Vector2(elbowX, from.y), new Vector2(elbowX, to.y), packageId);
                PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(segments, new Vector2(elbowX, to.y), to, packageId);
                return;
            }

            bool packageBelowGroup = delta.y >= 0f;

            if (packageBelowGroup)
            {
                AddCaptionAvoidingDownwardStructuralSegments(
                    segments,
                    groupPortRect,
                    groupContentRect,
                    packageRect,
                    packageId);
                return;
            }

            Vector2 verticalFrom = new Vector2(
                groupPortRect.center.x,
                groupPortRect.yMin);
            Vector2 verticalTo = new Vector2(
                packageRect.center.x,
                packageRect.yMax);

            if (Mathf.Abs(verticalFrom.x - verticalTo.x) <= 0.01f)
            {
                PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(segments, verticalFrom, verticalTo, packageId);
                return;
            }

            float elbowY = (verticalFrom.y + verticalTo.y) * 0.5f;
            PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(
                segments,
                verticalFrom,
                new Vector2(verticalFrom.x, elbowY),
                packageId);
            PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(
                segments,
                new Vector2(verticalFrom.x, elbowY),
                new Vector2(verticalTo.x, elbowY),
                packageId);
            PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(
                segments,
                new Vector2(verticalTo.x, elbowY),
                verticalTo,
                packageId);
        }

        internal static void AddCaptionAvoidingDownwardStructuralSegments(
            ICollection<PackageGraphStructuralMembershipSegment> segments,
            Rect groupPortRect,
            Rect groupContentRect,
            Rect packageRect,
            string packageId)
        {
            bool exitRight = packageRect.center.x >= groupPortRect.center.x;
            Vector2 categoryAnchor = new Vector2(
                exitRight ? groupPortRect.xMax : groupPortRect.xMin,
                groupPortRect.center.y);
            float escapeX = exitRight
                ? Mathf.Max(groupContentRect.xMax, groupPortRect.xMax) + PackageGraphMembershipMetrics.StructuralMembershipBusClearance
                : Mathf.Min(groupContentRect.xMin, groupPortRect.xMin) - PackageGraphMembershipMetrics.StructuralMembershipBusClearance;
            float clearY = PackageGraphMembershipSegments.GetClearBusAxis(
                packageRect.yMin,
                Mathf.Max(groupContentRect.yMax, groupPortRect.yMax),
                PackageGraphMembershipMetrics.StructuralMembershipBusClearance);
            Vector2 escapedCategory = new Vector2(escapeX, categoryAnchor.y);
            Vector2 lowerCorner = new Vector2(escapeX, clearY);
            Vector2 packageApproach = new Vector2(packageRect.center.x, clearY);
            Vector2 packagePort = new Vector2(packageRect.center.x, packageRect.yMin);

            PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(segments, categoryAnchor, escapedCategory, packageId);
            PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(segments, escapedCategory, lowerCorner, packageId);
            PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(segments, lowerCorner, packageApproach, packageId);
            PackageGraphMembershipSegments.AddOrthogonalStructuralSegment(segments, packageApproach, packagePort, packageId);
        }
    }
}
