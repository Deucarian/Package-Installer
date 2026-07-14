using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageGraphPainterTests
    {
        [Test]
        public void BuildFill_ProducesValidConvexMeshWithPositiveScreenSpaceWinding()
        {
            Vector2[] polygon =
            {
                new Vector2(1f, 2f),
                new Vector2(5f, 2f),
                new Vector2(5f, 6f),
                new Vector2(1f, 6f)
            };

            PackageGraphMeshData mesh = PackageGraphMeshBuilder.BuildFill(polygon);

            Assert.AreEqual(4, mesh.Positions.Length);
            Assert.AreEqual(6, mesh.Indices.Length);
            AssertMeshTrianglesAreValid(mesh);
            AssertBounds(mesh.Positions, 1f, 2f, 5f, 6f);
        }

        [Test]
        public void BuildStroke_TwoPointHorizontalLineUsesRequestedHalfWidth()
        {
            Vector2[] path =
            {
                new Vector2(0f, 0f),
                new Vector2(4f, 0f)
            };

            PackageGraphMeshData mesh = PackageGraphMeshBuilder.BuildStroke(path, false, 2f);

            Assert.AreEqual(4, mesh.Positions.Length);
            Assert.AreEqual(6, mesh.Indices.Length);
            AssertMeshTrianglesAreValid(mesh);
            AssertBounds(mesh.Positions, 0f, -1f, 4f, 1f);
        }

        [Test]
        public void BuildStroke_ClosedPolygonConnectsLastPairBackToFirstPair()
        {
            Vector2[] path =
            {
                new Vector2(0f, 0f),
                new Vector2(4f, 0f),
                new Vector2(4f, 4f),
                new Vector2(0f, 4f)
            };

            PackageGraphMeshData mesh = PackageGraphMeshBuilder.BuildStroke(path, true, 2f);

            Assert.AreEqual(8, mesh.Positions.Length);
            Assert.AreEqual(24, mesh.Indices.Length);
            AssertMeshTrianglesAreValid(mesh);
            Assert.IsTrue(HasTriangleJoiningFirstAndLastVertexPairs(mesh.Indices, mesh.Positions.Length));
        }

        [Test]
        public void BuildStroke_IgnoresDuplicateConsecutivePointsWithoutInvalidVertices()
        {
            Vector2[] path =
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(4f, 0f),
                new Vector2(4f, 0f)
            };

            PackageGraphMeshData mesh = PackageGraphMeshBuilder.BuildStroke(path, false, 2f);

            Assert.AreEqual(4, mesh.Positions.Length);
            Assert.AreEqual(6, mesh.Indices.Length);
            AssertMeshTrianglesAreValid(mesh);

            foreach (Vector2 position in mesh.Positions)
            {
                Assert.IsFalse(float.IsNaN(position.x) || float.IsInfinity(position.x));
                Assert.IsFalse(float.IsNaN(position.y) || float.IsInfinity(position.y));
            }
        }

        [Test]
        public void AppendCubic_AddsIntermediatePointsAndEndsExactlyAtEndpoint()
        {
            List<Vector2> path = new List<Vector2> { Vector2.zero };
            Vector2 endpoint = new Vector2(3f, 0f);

            PackageGraphMeshBuilder.AppendCubic(
                path,
                new Vector2(0f, 3f),
                new Vector2(3f, 3f),
                endpoint);

            Assert.That(path.Count, Is.GreaterThan(2));
            Assert.AreEqual(endpoint, path[path.Count - 1]);
            Assert.That(path[1].y, Is.GreaterThan(0f));
            Assert.AreNotEqual(Vector2.zero, path[1]);
            Assert.AreNotEqual(endpoint, path[1]);
        }

        [Test]
        public void CubicCircle_ProducesClosedProductionShapedFillAndStrokeMeshes()
        {
            const float Radius = 10f;
            const float Kappa = 0.55228475f;
            float offset = Radius * Kappa;
            List<Vector2> path = new List<Vector2> { new Vector2(0f, -Radius) };

            PackageGraphMeshBuilder.AppendCubic(
                path,
                new Vector2(offset, -Radius),
                new Vector2(Radius, -offset),
                new Vector2(Radius, 0f));
            PackageGraphMeshBuilder.AppendCubic(
                path,
                new Vector2(Radius, offset),
                new Vector2(offset, Radius),
                new Vector2(0f, Radius));
            PackageGraphMeshBuilder.AppendCubic(
                path,
                new Vector2(-offset, Radius),
                new Vector2(-Radius, offset),
                new Vector2(-Radius, 0f));
            PackageGraphMeshBuilder.AppendCubic(
                path,
                new Vector2(-Radius, -offset),
                new Vector2(-offset, -Radius),
                new Vector2(0f, -Radius));

            Assert.AreEqual(49, path.Count);
            Assert.AreEqual(path[0], path[path.Count - 1]);

            PackageGraphMeshData fill = PackageGraphMeshBuilder.BuildFill(path);
            PackageGraphMeshData stroke = PackageGraphMeshBuilder.BuildStroke(path, true, 2f);

            Assert.AreEqual(48, fill.Positions.Length);
            Assert.AreEqual(138, fill.Indices.Length);
            AssertMeshTrianglesAreValid(fill);
            AssertPositionsAreFinite(fill.Positions);
            AssertBounds(fill.Positions, -10f, -10f, 10f, 10f);

            Assert.AreEqual(96, stroke.Positions.Length);
            Assert.AreEqual(288, stroke.Indices.Length);
            AssertMeshTrianglesAreValid(stroke);
            AssertPositionsAreFinite(stroke.Positions);
            AssertBounds(stroke.Positions, -11f, -11f, 11f, 11f, 0.1f);
        }

        private static void AssertMeshTrianglesAreValid(PackageGraphMeshData mesh)
        {
            Assert.NotNull(mesh.Positions);
            Assert.NotNull(mesh.Indices);
            Assert.AreEqual(0, mesh.Indices.Length % 3);

            for (int index = 0; index < mesh.Indices.Length; index += 3)
            {
                int firstIndex = mesh.Indices[index];
                int secondIndex = mesh.Indices[index + 1];
                int thirdIndex = mesh.Indices[index + 2];

                Assert.That(firstIndex, Is.LessThan(mesh.Positions.Length));
                Assert.That(secondIndex, Is.LessThan(mesh.Positions.Length));
                Assert.That(thirdIndex, Is.LessThan(mesh.Positions.Length));

                Vector2 first = mesh.Positions[firstIndex];
                Vector2 second = mesh.Positions[secondIndex];
                Vector2 third = mesh.Positions[thirdIndex];
                float screenSpaceArea = Cross(second - first, third - first);

                Assert.That(screenSpaceArea, Is.GreaterThan(0.0001f));
            }
        }

        private static void AssertBounds(
            IReadOnlyList<Vector2> positions,
            float expectedMinimumX,
            float expectedMinimumY,
            float expectedMaximumX,
            float expectedMaximumY,
            float tolerance = 0.0001f)
        {
            float minimumX = float.PositiveInfinity;
            float minimumY = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float maximumY = float.NegativeInfinity;

            for (int index = 0; index < positions.Count; index++)
            {
                minimumX = Mathf.Min(minimumX, positions[index].x);
                minimumY = Mathf.Min(minimumY, positions[index].y);
                maximumX = Mathf.Max(maximumX, positions[index].x);
                maximumY = Mathf.Max(maximumY, positions[index].y);
            }

            Assert.That(minimumX, Is.EqualTo(expectedMinimumX).Within(tolerance));
            Assert.That(minimumY, Is.EqualTo(expectedMinimumY).Within(tolerance));
            Assert.That(maximumX, Is.EqualTo(expectedMaximumX).Within(tolerance));
            Assert.That(maximumY, Is.EqualTo(expectedMaximumY).Within(tolerance));
        }

        private static void AssertPositionsAreFinite(IReadOnlyList<Vector2> positions)
        {
            for (int index = 0; index < positions.Count; index++)
            {
                Assert.IsFalse(float.IsNaN(positions[index].x) || float.IsInfinity(positions[index].x));
                Assert.IsFalse(float.IsNaN(positions[index].y) || float.IsInfinity(positions[index].y));
            }
        }

        private static bool HasTriangleJoiningFirstAndLastVertexPairs(
            IReadOnlyList<ushort> indices,
            int positionCount)
        {
            int lastPairStart = positionCount - 2;

            for (int index = 0; index < indices.Count; index += 3)
            {
                bool containsFirstPair = false;
                bool containsLastPair = false;

                for (int corner = 0; corner < 3; corner++)
                {
                    ushort vertexIndex = indices[index + corner];
                    containsFirstPair |= vertexIndex < 2;
                    containsLastPair |= vertexIndex >= lastPairStart;
                }

                if (containsFirstPair && containsLastPair)
                {
                    return true;
                }
            }

            return false;
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }
    }
}
