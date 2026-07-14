using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageGraphPainterCompatibility
    {
#if UNITY_2022_1_OR_NEWER
        public static Painter2D Create(MeshGenerationContext context)
        {
            return context.painter2D;
        }

        public static void Complete(Painter2D painter)
        {
        }
#else
        public static PackageGraphMeshPainter Create(MeshGenerationContext context)
        {
            return new PackageGraphMeshPainter(context);
        }

        public static void Complete(PackageGraphMeshPainter painter)
        {
            painter.Complete();
        }
#endif
    }

    internal struct PackageGraphMeshData
    {
        private static readonly Vector2[] EmptyPositions = new Vector2[0];
        private static readonly ushort[] EmptyIndices = new ushort[0];

        public PackageGraphMeshData(Vector2[] positions, ushort[] indices)
        {
            Positions = positions ?? EmptyPositions;
            Indices = indices ?? EmptyIndices;
        }

        public Vector2[] Positions { get; }
        public ushort[] Indices { get; }

        public static PackageGraphMeshData Empty =>
            new PackageGraphMeshData(EmptyPositions, EmptyIndices);
    }

    internal static class PackageGraphMeshBuilder
    {
        private const float PointEpsilonSquared = 0.000001f;
        private const float TriangleEpsilon = 0.000001f;
        private const float MaximumMiterScale = 4f;
        private const int CubicSteps = 12;
        private const int MaximumVertexCount = ushort.MaxValue;

        public static PackageGraphMeshData BuildFill(IReadOnlyList<Vector2> points)
        {
            List<Vector2> normalized = Normalize(points, true);

            if (normalized.Count < 3 || normalized.Count > MaximumVertexCount)
            {
                return PackageGraphMeshData.Empty;
            }

            Vector2[] positions = normalized.ToArray();
            List<ushort> indices = new List<ushort>((positions.Length - 2) * 3);

            for (int index = 1; index < positions.Length - 1; index++)
            {
                AddPositiveTriangle(indices, positions, 0, index, index + 1);
            }

            return indices.Count == 0
                ? PackageGraphMeshData.Empty
                : new PackageGraphMeshData(positions, indices.ToArray());
        }

        public static PackageGraphMeshData BuildStroke(
            IReadOnlyList<Vector2> points,
            bool closed,
            float width)
        {
            List<Vector2> normalized = Normalize(points, closed);
            bool shouldClose = closed && normalized.Count > 2;
            int segmentCount = shouldClose ? normalized.Count : normalized.Count - 1;

            if (segmentCount < 1 ||
                width <= 0.0001f ||
                normalized.Count > MaximumVertexCount / 2)
            {
                return PackageGraphMeshData.Empty;
            }

            float halfWidth = Mathf.Max(0.0001f, width * 0.5f);
            Vector2[] positions = new Vector2[normalized.Count * 2];

            for (int index = 0; index < normalized.Count; index++)
            {
                Vector2 offset = GetStrokeOffset(normalized, index, shouldClose, halfWidth);
                positions[index * 2] = normalized[index] + offset;
                positions[index * 2 + 1] = normalized[index] - offset;
            }

            List<ushort> indices = new List<ushort>(segmentCount * 6);

            for (int index = 0; index < segmentCount; index++)
            {
                int next = (index + 1) % normalized.Count;
                int firstPositive = index * 2;
                int firstNegative = firstPositive + 1;
                int nextPositive = next * 2;
                int nextNegative = nextPositive + 1;

                AddPositiveTriangle(
                    indices,
                    positions,
                    firstPositive,
                    firstNegative,
                    nextNegative);
                AddPositiveTriangle(
                    indices,
                    positions,
                    firstPositive,
                    nextNegative,
                    nextPositive);
            }

            return indices.Count == 0
                ? PackageGraphMeshData.Empty
                : new PackageGraphMeshData(positions, indices.ToArray());
        }

        public static void AppendCubic(
            List<Vector2> path,
            Vector2 control1,
            Vector2 control2,
            Vector2 end)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (path.Count == 0)
            {
                path.Add(end);
                return;
            }

            Vector2 start = path[path.Count - 1];

            for (int step = 1; step <= CubicSteps; step++)
            {
                if (step == CubicSteps)
                {
                    path.Add(end);
                    continue;
                }

                float t = step / (float)CubicSteps;
                float inverse = 1f - t;
                path.Add(
                    inverse * inverse * inverse * start +
                    3f * inverse * inverse * t * control1 +
                    3f * inverse * t * t * control2 +
                    t * t * t * end);
            }
        }

        private static List<Vector2> Normalize(IReadOnlyList<Vector2> points, bool removeClosingPoint)
        {
            List<Vector2> normalized = new List<Vector2>();

            if (points == null)
            {
                return normalized;
            }

            for (int index = 0; index < points.Count; index++)
            {
                Vector2 point = points[index];

                if (!IsFinite(point) ||
                    (normalized.Count > 0 &&
                     (normalized[normalized.Count - 1] - point).sqrMagnitude <= PointEpsilonSquared))
                {
                    continue;
                }

                normalized.Add(point);
            }

            if (removeClosingPoint &&
                normalized.Count > 1 &&
                (normalized[0] - normalized[normalized.Count - 1]).sqrMagnitude <= PointEpsilonSquared)
            {
                normalized.RemoveAt(normalized.Count - 1);
            }

            return normalized;
        }

        private static Vector2 GetStrokeOffset(
            IReadOnlyList<Vector2> points,
            int index,
            bool closed,
            float halfWidth)
        {
            if (!closed && index == 0)
            {
                return GetSegmentNormal(points[0], points[1]) * halfWidth;
            }

            if (!closed && index == points.Count - 1)
            {
                return GetSegmentNormal(points[index - 1], points[index]) * halfWidth;
            }

            int previousIndex = index == 0 ? points.Count - 1 : index - 1;
            int nextIndex = index == points.Count - 1 ? 0 : index + 1;
            Vector2 previousNormal = GetSegmentNormal(points[previousIndex], points[index]);
            Vector2 nextNormal = GetSegmentNormal(points[index], points[nextIndex]);
            Vector2 combined = previousNormal + nextNormal;

            if (combined.sqrMagnitude <= PointEpsilonSquared)
            {
                return nextNormal * halfWidth;
            }

            Vector2 miter = combined.normalized;
            float denominator = Vector2.Dot(miter, nextNormal);

            if (Mathf.Abs(denominator) <= 0.25f)
            {
                return nextNormal * halfWidth;
            }

            float miterLength = halfWidth / denominator;

            if (Mathf.Abs(miterLength) > halfWidth * MaximumMiterScale)
            {
                return nextNormal * halfWidth;
            }

            return miter * miterLength;
        }

        private static Vector2 GetSegmentNormal(Vector2 start, Vector2 end)
        {
            Vector2 direction = end - start;
            return new Vector2(-direction.y, direction.x).normalized;
        }

        private static void AddPositiveTriangle(
            ICollection<ushort> indices,
            IReadOnlyList<Vector2> positions,
            int first,
            int second,
            int third)
        {
            float area = Cross(
                positions[second] - positions[first],
                positions[third] - positions[first]);

            if (Mathf.Abs(area) <= TriangleEpsilon)
            {
                return;
            }

            indices.Add((ushort)first);
            indices.Add((ushort)(area > 0f ? second : third));
            indices.Add((ushort)(area > 0f ? third : second));
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static bool IsFinite(Vector2 point)
        {
            return !float.IsNaN(point.x) &&
                   !float.IsInfinity(point.x) &&
                   !float.IsNaN(point.y) &&
                   !float.IsInfinity(point.y);
        }
    }

#if !UNITY_2022_1_OR_NEWER
    internal sealed class PackageGraphMeshPainter
    {
        private const int MaximumVertexCount = ushort.MaxValue;

        private readonly MeshGenerationContext _context;
        private readonly List<Vector2> _path = new List<Vector2>();
        private readonly List<Vertex> _vertices = new List<Vertex>();
        private readonly List<ushort> _indices = new List<ushort>();
        private bool _closed;

        public PackageGraphMeshPainter(MeshGenerationContext context)
        {
            _context = context;
            strokeColor = Color.white;
            fillColor = Color.white;
            lineWidth = 1f;
        }

        public Color strokeColor { get; set; }
        public Color fillColor { get; set; }
        public float lineWidth { get; set; }

        public void BeginPath()
        {
            _path.Clear();
            _closed = false;
        }

        public void MoveTo(Vector2 point)
        {
            _path.Clear();
            _path.Add(point);
            _closed = false;
        }

        public void LineTo(Vector2 point)
        {
            _path.Add(point);
        }

        public void BezierCurveTo(Vector2 control1, Vector2 control2, Vector2 end)
        {
            PackageGraphMeshBuilder.AppendCubic(_path, control1, control2, end);
        }

        public void ClosePath()
        {
            _closed = true;
        }

        public void Fill()
        {
            Append(PackageGraphMeshBuilder.BuildFill(_path), fillColor);
        }

        public void Stroke()
        {
            Append(PackageGraphMeshBuilder.BuildStroke(_path, _closed, lineWidth), strokeColor);
        }

        public void Complete()
        {
            Flush();
        }

        private void Append(PackageGraphMeshData mesh, Color color)
        {
            if (mesh.Positions.Length == 0 || mesh.Indices.Length == 0)
            {
                return;
            }

            if (_vertices.Count + mesh.Positions.Length > MaximumVertexCount)
            {
                Flush();
            }

            int vertexOffset = _vertices.Count;
            Color32 tint = color;

            for (int index = 0; index < mesh.Positions.Length; index++)
            {
                Vector2 position = mesh.Positions[index];
                _vertices.Add(new Vertex
                {
                    position = new Vector3(position.x, position.y, Vertex.nearZ),
                    tint = tint,
                    uv = Vector2.zero
                });
            }

            for (int index = 0; index < mesh.Indices.Length; index++)
            {
                _indices.Add((ushort)(vertexOffset + mesh.Indices[index]));
            }
        }

        private void Flush()
        {
            if (_vertices.Count == 0 || _indices.Count == 0)
            {
                _vertices.Clear();
                _indices.Clear();
                return;
            }

            MeshWriteData mesh = _context.Allocate(_vertices.Count, _indices.Count, null);
            mesh.SetAllVertices(_vertices.ToArray());
            mesh.SetAllIndices(_indices.ToArray());
            _vertices.Clear();
            _indices.Clear();
        }
    }
#endif
}
