using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class DelaunayMesh2ContractTests
    {
        static Vector2d P(double x, double y) => new Vector2d(x, y);

        static TriangulationMesh2 Mesh(Triangulation2Result result, Triangulation2Status expected)
        {
            Assert.Equal(expected, result.Status);
            Assert.Equal(Triangulation2FailureKind.None, result.FailureKind);
            Assert.Null(result.ProblemCoordinate);
            Assert.Null(result.ProblemInputIndex);
            Assert.NotNull(result.Mesh);
            Assert.True(result.Mesh.IsValid(out string reason), reason);
            return result.Mesh;
        }

        [Fact]
        public void EmptyAndCollinearInputsPreserveSlotsAndHaveNoEdges()
        {
            var mesher = new DelaunayMesh2();
            foreach (var input in new[] {
                new Vector2d[0],
                new[] { P(2, 3) },
                new[] { P(0, 0), P(1, 1) },
                new[] { P(0, 0), P(1, 1), P(2, 2), P(1, 1) }
            }) {
                var mesh = Mesh(mesher.Compute(input), Triangulation2Status.Empty);
                Assert.Equal(input.Length, mesh.Vertices.Count);
                int[] expectedMapping = input.Select((point, i) =>
                    Enumerable.Range(0, i + 1).First(j => input[j] == point)).ToArray();
                Assert.Equal(expectedMapping, mesh.InputToVertex.ToArray());
                Assert.Empty(mesh.Triangles);
                Assert.Empty(mesh.Neighbours);
                Assert.Empty(mesh.Edges());
                Assert.False(mesh.TryLocate(P(0, 0), out int triangle));
                Assert.Equal(-1, triangle);
                for (int i = 0; i < input.Length; ++i)
                    Assert.Empty(mesh.IncidentTriangles(i));
            }
        }

        [Fact]
        public void DuplicatesKeepSlotsMapToFirstAndCanonicalizeSignedZero()
        {
            var input = new[] { P(-0.0, 0), P(2, 0), P(0, 2), P(0, -0.0), P(2, 0) };
            var mesh = Mesh(new DelaunayMesh2().Compute(input), Triangulation2Status.Success);
            Assert.Equal(new[] { 0, 1, 2, 0, 1 }, mesh.InputToVertex.ToArray());
            Assert.Equal(input.Length, mesh.Vertices.Count);
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(mesh.Vertices[0].x));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(mesh.Vertices[3].y));
            Assert.Single(mesh.Triangles);
            Assert.Empty(mesh.IncidentTriangles(3));
            Assert.Empty(mesh.IncidentTriangles(4));
            Assert.Equal(new[] { 0, 1, 2 }, new[] { mesh.Triangles[0].a, mesh.Triangles[0].b, mesh.Triangles[0].c }.OrderBy(i => i).ToArray());
        }

        [Fact]
        public void OneTriangleHasClosedLocationAndThreeDistinctBoundaryEdges()
        {
            var mesh = Mesh(new DelaunayMesh2().Compute(new[] { P(0, 0), P(4, 0), P(0, 4) }), Triangulation2Status.Success);
            Assert.Single(mesh.Triangles);
            Assert.Single(mesh.Neighbours);
            for (int i = 0; i < 3; ++i)
                Assert.Equal(-1, mesh.Neighbours[0][i]);
            var edges = mesh.Edges().Select(e => (e.a, e.b)).ToArray();
            Assert.Equal(3, edges.Length);
            Assert.Equal(3, edges.Distinct().Count());
            Assert.All(edges, e => Assert.True(e.a < e.b));
            foreach (var point in new[] { P(0, 0), P(2, 0), P(1, 1), P(2, 2) }) {
                Assert.True(mesh.TryLocate(point, out int hit));
                Assert.Equal(0, hit);
            }
            Assert.False(mesh.TryLocate(P(3, 3), out int outside));
            Assert.Equal(-1, outside);
            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.IncidentTriangles(-1).ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.IncidentTriangles(3).ToArray());
        }

        [Fact]
        public void InteriorEdgesHaveReciprocalNeighboursAndLowestLocationId()
        {
            var points = new[] { P(0, 0), P(8, 0), P(8, 8), P(0, 8), P(3, 4) };
            var mesh = Mesh(new DelaunayMesh2().Compute(points), Triangulation2Status.Success);
            Assert.Equal(4, mesh.Triangles.Count);
            Assert.Equal(mesh.Triangles.Count, mesh.Neighbours.Count);
            var expectedEdges = new HashSet<(int, int)>();
            for (int t = 0; t < mesh.Triangles.Count; ++t) {
                Index3i face = mesh.Triangles[t];
                for (int k = 0; k < 3; ++k) {
                    int a = face[(k + 1) % 3], b = face[(k + 2) % 3];
                    var edge = (Math.Min(a, b), Math.Max(a, b));
                    expectedEdges.Add(edge);
                    int neighbour = mesh.Neighbours[t][k];
                    if (neighbour == -1)
                        continue;
                    Assert.InRange(neighbour, 0, mesh.Triangles.Count - 1);
                    Assert.NotEqual(t, neighbour);
                    var other = mesh.Triangles[neighbour];
                    int opposite = Enumerable.Range(0, 3).Single(j =>
                        Math.Min(other[(j + 1) % 3], other[(j + 2) % 3]) == edge.Item1 &&
                        Math.Max(other[(j + 1) % 3], other[(j + 2) % 3]) == edge.Item2);
                    Assert.Equal(t, mesh.Neighbours[neighbour][opposite]);
                    var midpoint = P((points[a].x + points[b].x) / 2, (points[a].y + points[b].y) / 2);
                    Assert.True(mesh.TryLocate(midpoint, out int hit));
                    Assert.Equal(Math.Min(t, neighbour), hit);
                }
            }
            var edges = mesh.Edges().Select(e => (e.a, e.b)).ToArray();
            Assert.Equal(expectedEdges.Count, edges.Length);
            Assert.True(expectedEdges.SetEquals(edges));
            for (int vertex = 0; vertex < points.Length; ++vertex) {
                int id = vertex;
                var expected = Enumerable.Range(0, mesh.Triangles.Count).Where(t => {
                    var face = mesh.Triangles[t];
                    return face.a == id || face.b == id || face.c == id;
                }).OrderBy(i => i).ToArray();
                Assert.Equal(expected, mesh.IncidentTriangles(vertex).OrderBy(i => i).ToArray());
                Assert.True(mesh.TryLocate(points[vertex], out int hit));
                Assert.Equal(expected[0], hit);
            }
            Assert.False(mesh.TryLocate(P(-double.Epsilon, 4), out int outside));
            Assert.Equal(-1, outside);
        }

        [Fact]
        public void ReuseAndCallerMutationCannotChangePreviousResult()
        {
            var input = new[] { P(0, 0), P(4, 0), P(0, 4) };
            var mesher = new DelaunayMesh2();
            var first = Mesh(mesher.Compute(input), Triangulation2Status.Success);
            var triangles = first.Triangles.ToArray();
            input[0] = P(100, 100);
            Mesh(mesher.Compute(new Vector2d[0]), Triangulation2Status.Empty);
            Assert.Equal(P(0, 0), first.Vertices[0]);
            Assert.Equal(triangles, first.Triangles.ToArray());
            Assert.True(first.TryLocate(P(1, 1), out int hit));
            Assert.Equal(0, hit);
            Assert.False(first.Vertices is Vector2d[]);
            Assert.False(first.Triangles is Index3i[]);
            if (first.Vertices is IList<Vector2d> vertices)
                Assert.Throws<NotSupportedException>(() => vertices[0] = P(9, 9));
            if (first.Triangles is IList<Index3i> faces)
                Assert.Throws<NotSupportedException>(() => faces[0] = default(Index3i));
        }

        [Fact]
        public void GridPermutationsCoverHullAndHaveEmptyCircumcircles()
        {
            var grid = (from x in Enumerable.Range(0, 7) from y in Enumerable.Range(0, 7)
                        select P(x, y)).ToArray();
            foreach (var points in new[] { grid, grid.Reverse().ToArray(), grid.OrderBy(p => ((int)p.x * 19 + (int)p.y * 23) % 49).ToArray() }) {
                var mesh = Mesh(new DelaunayMesh2().Compute(points), Triangulation2Status.Success);
                long area = 0;
                var used = new HashSet<int>();
                foreach (var triangle in mesh.Triangles) {
                    var a = points[triangle.a]; var b = points[triangle.b]; var c = points[triangle.c];
                    long twiceArea = (long)(b.x - a.x) * (long)(c.y - a.y) - (long)(b.y - a.y) * (long)(c.x - a.x);
                    Assert.True(twiceArea > 0);
                    area += twiceArea;
                    used.Add(triangle.a); used.Add(triangle.b); used.Add(triangle.c);
                    for (int i = 0; i < points.Length; ++i) {
                        if (i == triangle.a || i == triangle.b || i == triangle.c) continue;
                        BigInteger ax = (long)(a.x - points[i].x), ay = (long)(a.y - points[i].y);
                        BigInteger bx = (long)(b.x - points[i].x), by = (long)(b.y - points[i].y);
                        BigInteger cx = (long)(c.x - points[i].x), cy = (long)(c.y - points[i].y);
                        BigInteger determinant = (ax * ax + ay * ay) * (bx * cy - by * cx)
                            + (bx * bx + by * by) * (cx * ay - cy * ax)
                            + (cx * cx + cy * cy) * (ax * by - ay * bx);
                        Assert.True(determinant.Sign <= 0);
                    }
                }
                Assert.Equal(72, area);
                Assert.Equal(points.Length, used.Count);
            }
        }

        [Fact]
        public void SimilaritiesPreserveTopologyInTheTransformedPlane()
        {
            var input = new[] { P(0, 0), P(8, 0), P(0, 8), P(2, 3) };
            var original = Mesh(new DelaunayMesh2().Compute(input), Triangulation2Status.Success);
            var expected = FaceKeys(original);
            foreach (var similarity in Similarity.All()) {
                var origin = similarity.Apply(Vector3d.Zero);
                var axisX = similarity.ApplyDirection(Vector3d.AxisX);
                var axisY = similarity.ApplyDirection(Vector3d.AxisY);
                var transformed = input.Select(p => {
                    var offset = similarity.Apply(new Vector3d(p.x, p.y, 0)) - origin;
                    return P(offset.Dot(axisX), offset.Dot(axisY));
                }).ToArray();
                var mesh = Mesh(new DelaunayMesh2().Compute(transformed), Triangulation2Status.Success);
                Assert.Equal(expected, FaceKeys(mesh));
                if (similarity.IsExact) {
                    for (int i = 0; i < input.Length; ++i) {
                        Assert.Equal(BitConverter.DoubleToInt64Bits(input[i].x), BitConverter.DoubleToInt64Bits(mesh.Vertices[i].x / similarity.Scale));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(input[i].y), BitConverter.DoubleToInt64Bits(mesh.Vertices[i].y / similarity.Scale));
                    }
                }
            }
        }

        [Fact]
        public void PlanarReflectionsPreserveFacesAndRestoreCounterClockwiseOrientation()
        {
            var points = new[] { P(0, 0), P(8, 0), P(8, 8), P(0, 8), P(3, 4), P(4, 0) };
            var expected = FaceKeys(Mesh(new DelaunayMesh2().Compute(points), Triangulation2Status.Success));
            foreach (var reflected in new[] {
                points.Select(p => P(-p.x, p.y)).ToArray(),
                points.Select(p => P(p.x, -p.y)).ToArray(),
                points.Select(p => P(p.y, p.x)).ToArray()
            }) {
                var mesh = Mesh(new DelaunayMesh2().Compute(reflected), Triangulation2Status.Success);
                Assert.Equal(expected, FaceKeys(mesh));
                foreach (var t in mesh.Triangles) {
                    var a = reflected[t.a]; var b = reflected[t.b]; var c = reflected[t.c];
                    Assert.True((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) > 0);
                }
                for (int i = 0; i < reflected.Length; ++i) {
                    Assert.True(mesh.TryLocate(reflected[i], out int hit));
                    Assert.Equal(mesh.IncidentTriangles(i).Min(), hit);
                }
            }
        }

        static string[] FaceKeys(TriangulationMesh2 mesh) => mesh.Triangles
            .Select(t => string.Join(",", new[] { t.a, t.b, t.c }.OrderBy(i => i)))
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();

        sealed class CancelDuringSnapshot : IReadOnlyList<Vector2d>
        {
            readonly Vector2d[] points;
            readonly CancellationTokenSource source;
            public CancelDuringSnapshot(Vector2d[] points, CancellationTokenSource source) { this.points = points; this.source = source; }
            public int Count => points.Length;
            public Vector2d this[int index] {
                get { if (index == 1) source.Cancel(); return points[index]; }
            }
            public IEnumerator<Vector2d> GetEnumerator() => ((IEnumerable<Vector2d>)points).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Fact]
        public void CancellationDuringSnapshotPreservesCallerAndPriorResult()
        {
            var points = new[] { P(0, 0), P(4, 0), P(0, 4), P(1, 1) };
            var original = points.ToArray();
            var mesher = new DelaunayMesh2();
            var previous = Mesh(mesher.Compute(points), Triangulation2Status.Success);
            var triangles = previous.Triangles.ToArray();
            using (var source = new CancellationTokenSource()) {
                var cancelInput = new CancelDuringSnapshot(points, source);
                Assert.ThrowsAny<OperationCanceledException>(() => mesher.Compute(cancelInput, source.Token));
            }
            Assert.Equal(original, points);
            Assert.Equal(triangles, previous.Triangles.ToArray());
            Assert.True(previous.IsValid(out string reason), reason);
        }

        sealed class CancelAfterSnapshot : IReadOnlyList<Vector2d>
        {
            readonly Vector2d[] points;
            readonly CancellationTokenSource source;
            public int Reads { get; private set; }
            public CancelAfterSnapshot(Vector2d[] points, CancellationTokenSource source)
            { this.points = points; this.source = source; }
            public int Count => points.Length;
            public Vector2d this[int index] {
                get {
                    ++Reads;
                    if (index == points.Length - 1) source.CancelAfter(50);
                    return points[index];
                }
            }
            public IEnumerator<Vector2d> GetEnumerator() => ((IEnumerable<Vector2d>)points).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Fact]
        public void CancellationAfterSnapshotInterruptsLargeComputationAndAllowsReuse()
        {
            var points = (from x in Enumerable.Range(0, 64) from y in Enumerable.Range(0, 64)
                          select P(x, y)).ToArray();
            var original = points.ToArray();
            var mesher = new DelaunayMesh2();
            var small = new[] { P(0, 0), P(4, 0), P(0, 4), P(1, 1) };
            var previous = Mesh(mesher.Compute(small), Triangulation2Status.Success);
            var faces = previous.Triangles.ToArray();
            using (var source = new CancellationTokenSource()) {
                var input = new CancelAfterSnapshot(points, source);
                Assert.ThrowsAny<OperationCanceledException>(() => mesher.Compute(input, source.Token));
                Assert.Equal(points.Length, input.Reads);
            }
            Assert.Equal(original, points);
            Assert.Equal(faces, previous.Triangles.ToArray());
            Assert.True(previous.IsValid(out string reason), reason);
            Assert.Equal(faces, Mesh(mesher.Compute(small), Triangulation2Status.Success).Triangles.ToArray());
        }

        [Fact]
        public void InvalidCoordinatesAndCancellationAreNotSuccessfulResults()
        {
            var mesher = new DelaunayMesh2();
            Assert.Throws<ArgumentNullException>(() => mesher.Compute(null));
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.1e70, -1.1e70 }) {
                Assert.ThrowsAny<ArgumentException>(() => mesher.Compute(new[] { P(value, 0) }));
                Assert.ThrowsAny<ArgumentException>(() => mesher.Compute(new[] { P(0, value) }));
            }
            foreach (var invalid in new[] { (point: P(double.NaN, 0), component: "x"), (point: P(0, double.NaN), component: "y") }) {
                var error = Assert.ThrowsAny<ArgumentException>(() => mesher.Compute(new[] { P(0, 0), P(1, 0), invalid.point }));
                Assert.Contains("2", error.Message);
                Assert.Contains(invalid.component, error.Message.ToLowerInvariant());
            }
            var valid = Mesh(mesher.Compute(new[] { P(0, 0), P(1, 0), P(0, 1) }), Triangulation2Status.Success);
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.1e70 }) {
                Assert.ThrowsAny<ArgumentException>(() => valid.TryLocate(P(value, 0), out _));
                Assert.ThrowsAny<ArgumentException>(() => valid.TryLocate(P(0, value), out _));
            }
            using (var cancelled = new CancellationTokenSource()) {
                cancelled.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => mesher.Compute(new[] { P(0, 0), P(1, 0), P(0, 1) }, cancelled.Token));
            }
        }
    }
}
