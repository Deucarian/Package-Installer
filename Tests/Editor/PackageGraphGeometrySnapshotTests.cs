using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    public sealed class PackageGraphGeometrySnapshotTests
    {
        [Test]
        public void GeometryIsCopiedAndComparedCaseInsensitively()
        {
            var source = new Dictionary<string, Rect> { ["Package"] = new Rect(1, 2, 3, 4) };
            var snapshot = new PackageGraphGeometrySnapshot();
            snapshot.Update(source, null);
            source["Package"] = new Rect(9, 9, 9, 9);

            Assert.AreEqual(new Rect(1, 2, 3, 4), snapshot.Nodes["package"]);
            Assert.IsEmpty(snapshot.Groups);
        }

        [Test]
        public void MissingInputsClearPreviousGeometryAndRadiiStayNonnegative()
        {
            var snapshot = new PackageGraphGeometrySnapshot();
            snapshot.Update(null, null, new Dictionary<string, Vector2> { ["group"] = Vector2.one },
                new Dictionary<string, float> { ["group"] = -2f });
            Assert.AreEqual(0f, snapshot.Radii["GROUP"]);
            Assert.AreEqual(Vector2.one, snapshot.Centers["GROUP"]);

            snapshot.Update(null, null);
            Assert.IsEmpty(snapshot.Nodes);
            Assert.IsEmpty(snapshot.Groups);
            Assert.IsEmpty(snapshot.Centers);
            Assert.IsEmpty(snapshot.Radii);
        }

        [Test]
        public void RouteGeometryCanBeEvaluatedWithoutAVisualLayer()
        {
            var points = new[] { Vector2.zero, new Vector2(10, 0), new Vector2(10, 10) };
            Assert.AreEqual(20f, PackageGraphRouteGeometry.GetRouteLength(points));
            Assert.AreEqual(new Vector2(10, 0), PackageGraphRouteGeometry.GetPointOnRoute(points, 0.5f, out _));
            Assert.IsTrue(PackageGraphRouteGeometry.LineIntersectsRectInterior(
                Vector2.zero, new Vector2(10, 0), new Rect(4, -1, 2, 2)));
        }
    }
}
