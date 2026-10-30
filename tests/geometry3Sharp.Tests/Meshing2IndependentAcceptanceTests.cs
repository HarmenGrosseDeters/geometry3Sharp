using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class Meshing2IndependentAcceptanceTests
    {
        private static Vector2d P(double x, double y) { return new Vector2d(x, y); }

        [Fact]
        public void DegenerateInputsRetainSlotsAndHaveNoTopology()
        {
            var cases = new[]
            {
                new Vector2d[0],
                new[] { P(3, 4) },
                new[] { P(3, 4), P(8, 9) },
                new[] { P(1, 1), P(1, 1), P(1, 1), P(1, 1), P(1, 1) },
                new[] { P(-2, -4), P(0, 0), P(2, 4), P(1, 2), P(-2, -4) }
            };
            foreach (var points in cases)
            {
                var result = new DelaunayMesh2().Compute(points);
                Assert.Equal(Triangulation2Status.Empty, result.Status);
                Assert.Equal(Triangulation2FailureKind.None, result.FailureKind);
                Assert.Null(result.ProblemCoordinate);
                Assert.Null(result.ProblemInputIndex);
                Assert.NotNull(result.Mesh);
                CheckIdentity(points, result.Mesh);
                Assert.Empty(result.Mesh.Triangles);
                Assert.Empty(result.Mesh.Neighbours);
                Assert.Empty(result.Mesh.Edges());
                int triangle;
                Assert.False(result.Mesh.TryLocate(P(0, 0), out triangle));
                Assert.Equal(-1, triangle);
                string reason;
                Assert.True(result.Mesh.IsValid(out reason), reason);
            }
        }

        [Fact]
        public void ExactOracleChecksHullDelaunayTopologyAndIdentity()
        {
            var cases = new[]
            {
                new[] { P(0, 0), P(8, 0), P(0, 7) },
                new[] { P(0, 0), P(8, 0), P(8, 8), P(0, 8), P(4, 4), P(4, 0), P(0, 3), P(8, 5) },
                new[] { P(5, 0), P(3, 4), P(0, 5), P(-3, 4), P(-5, 0), P(-3, -4), P(0, -5), P(3, -4), P(0, 0) },
                new[] { P(0, 0), P(7, 0), P(9, 4), P(7, 9), P(0, 8), P(2, 2), P(5, 3), P(4, 7), P(2, 2), P(7, 0) },
                Enumerable.Range(0, 16).Select(i => P(i % 4, i / 4)).ToArray()
            };
            foreach (var points in cases)
                CheckSuccess(points, new DelaunayMesh2().Compute(points));
        }

        [Fact]
        public void SignedZeroAndDuplicatesUseFirstInputWithoutCompacting()
        {
            double negativeZero = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000UL));
            var points = new[] { P(negativeZero, 0), P(4, 0), P(0, 4), P(0, negativeZero), P(4, 0), P(1, 1) };
            var result = new DelaunayMesh2().Compute(points);
            CheckSuccess(points, result);
            Assert.Equal(new[] { 0, 1, 2, 0, 1, 5 }, result.Mesh.InputToVertex.ToArray());
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(result.Mesh.Vertices[0].x));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(result.Mesh.Vertices[3].y));
            Assert.Empty(result.Mesh.IncidentTriangles(3));
            Assert.Empty(result.Mesh.IncidentTriangles(4));
        }

        [Fact]
        public void ExactOracleCoversNearDegeneracyAndMixedMagnitude()
        {
            double tiny = BitConverter.Int64BitsToDouble(1);
            double large = Math.Pow(2, 50);
            var cases = new[]
            {
                new[] { P(0, 0), P(large, large - 1), P(large + 1, large), P(0, large), P(large, 0) },
                new[] { P(0, 0), P(8 * tiny, 0), P(0, 8 * tiny), P(2 * tiny, 2 * tiny) },
                new[] { P(-1e70, 0), P(1e70, 0), P(0, 1e70), P(0, tiny), P(tiny, 0) },
                new[] { P(1e9, 1e9), P(1e9 + 10, 1e9), P(1e9 + 10, 1e9 + 10), P(1e9, 1e9 + 10), P(1e9 + 0.001, 1e9 + 0.001) }
            };
            foreach (var points in cases)
                CheckSuccess(points, new DelaunayMesh2().Compute(points));
        }

        [Fact]
        public void ClosedLocationReturnsLowestContainingTriangle()
        {
            var points = new[] { P(0, 0), P(12, 0), P(12, 12), P(0, 12), P(6, 6), P(3, 3) };
            var result = new DelaunayMesh2().Compute(points);
            CheckSuccess(points, result);
            var queries = new List<Vector2d>(points) { P(1, 2), P(13, 6), P(-double.Epsilon, 0), P(6, 12) };
            foreach (var edge in result.Mesh.Edges())
            {
                var a = points[edge[0]];
                var b = points[edge[1]];
                queries.Add(P((a.x + b.x) / 2, (a.y + b.y) / 2));
            }
            foreach (var query in queries)
            {
                int expected = -1;
                for (int t = 0; t < result.Mesh.Triangles.Count; ++t)
                {
                    var face = result.Mesh.Triangles[t];
                    if (Orient(points[face[0]], points[face[1]], query) >= 0 &&
                        Orient(points[face[1]], points[face[2]], query) >= 0 &&
                        Orient(points[face[2]], points[face[0]], query) >= 0)
                    {
                        expected = t;
                        break;
                    }
                }
                int actual;
                Assert.Equal(expected >= 0, result.Mesh.TryLocate(query, out actual));
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void InvalidCoordinatesAndIndicesAreRejected()
        {
            var operation = new DelaunayMesh2();
            Assert.ThrowsAny<ArgumentException>(() => operation.Compute(null));
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.01e70, -1.01e70 })
            {
                Assert.ThrowsAny<ArgumentException>(() => operation.Compute(new[] { P(0, 0), P(bad, 0), P(0, 1) }));
                Assert.ThrowsAny<ArgumentException>(() => operation.Compute(new[] { P(0, 0), P(0, bad), P(1, 0) }));
            }
            var mesh = operation.Compute(new[] { P(0, 0), P(2, 0), P(0, 2) }).Mesh;
            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.IncidentTriangles(-1).ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => mesh.IncidentTriangles(3).ToArray());
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.01e70 })
            {
                Assert.ThrowsAny<ArgumentException>(() => { int id; mesh.TryLocate(P(bad, 0), out id); });
                Assert.ThrowsAny<ArgumentException>(() => { int id; mesh.TryLocate(P(0, bad), out id); });
            }
        }

        [Fact]
        public void ReuseCancellationAndOwnedCollectionsPreservePriorResults()
        {
            var points = new[] { P(0, 0), P(8, 0), P(8, 8), P(0, 8), P(2, 3), P(5, 4) };
            var operation = new DelaunayMesh2();
            var first = operation.Compute(points);
            CheckSuccess(points, first);
            string before = Fingerprint(first.Mesh);
            CheckReadOnly(first.Mesh.Vertices);
            CheckReadOnly(first.Mesh.InputToVertex);
            CheckReadOnly(first.Mesh.Triangles);
            CheckReadOnly(first.Mesh.Neighbours);
            var second = operation.Compute(points);
            Assert.Equal(before, Fingerprint(second.Mesh));
            operation.Compute(new[] { P(1, 1) });
            Assert.ThrowsAny<ArgumentException>(() => operation.Compute(new[] { P(double.NaN, 0) }));
            var cancelled = new CancellationToken(true);
            Assert.ThrowsAny<OperationCanceledException>(() => operation.Compute(points, cancelled));
            points[0] = P(100, 100);
            Assert.Equal(before, Fingerprint(first.Mesh));
            Assert.Equal(before, Fingerprint(second.Mesh));
        }

        [Fact]
        public void IndependentParallelInstancesAndRepeatedRunsAreDeterministic()
        {
            var points = Enumerable.Range(0, 25).Select(i => P(i % 5, i / 5)).ToArray();
            var baseline = new DelaunayMesh2().Compute(points);
            CheckSuccess(points, baseline);
            string expected = Fingerprint(baseline.Mesh);
            var fingerprints = new string[12];
            Parallel.For(0, fingerprints.Length, i =>
            {
                var result = new DelaunayMesh2().Compute(points);
                CheckSuccess(points, result);
                fingerprints[i] = Fingerprint(result.Mesh);
            });
            foreach (string actual in fingerprints)
                Assert.Equal(expected, actual);
        }

        [Fact]
        public void ExactPowerOfTwoScalesPreserveCanonicalTopology()
        {
            var original = new[] { P(0, 0), P(10, 0), P(11, 7), P(7, 12), P(0, 10), P(3, 2), P(8, 4), P(4, 8) };
            var baseline = new DelaunayMesh2().Compute(original);
            CheckSuccess(original, baseline);
            string topology = Topology(baseline.Mesh);
            foreach (int exponent in new[] { -200, -20, 20, 200 })
            {
                double scale = Math.Pow(2, exponent);
                var transformed = original.Select(p => P(p.x * scale, p.y * scale)).ToArray();
                var result = new DelaunayMesh2().Compute(transformed);
                CheckSuccess(transformed, result);
                Assert.Equal(topology, Topology(result.Mesh));
            }
        }

        private static void CheckReadOnly<T>(IReadOnlyList<T> items)
        {
            var list = items as IList<T>;
            if (list != null && list.Count != 0)
                Assert.Throws<NotSupportedException>(() => list[0] = list[0]);
        }

        private static void CheckIdentity(Vector2d[] points, TriangulationMesh2 mesh)
        {
            Assert.Equal(points.Length, mesh.Vertices.Count);
            Assert.Equal(points.Length, mesh.InputToVertex.Count);
            for (int i = 0; i < points.Length; ++i)
            {
                int representative = 0;
                while (points[representative].x != points[i].x || points[representative].y != points[i].y)
                    ++representative;
                Assert.Equal(representative, mesh.InputToVertex[i]);
                Assert.Equal(points[i].x == 0 ? 0L : BitConverter.DoubleToInt64Bits(points[i].x), BitConverter.DoubleToInt64Bits(mesh.Vertices[i].x));
                Assert.Equal(points[i].y == 0 ? 0L : BitConverter.DoubleToInt64Bits(points[i].y), BitConverter.DoubleToInt64Bits(mesh.Vertices[i].y));
                if (representative != i)
                    Assert.Empty(mesh.IncidentTriangles(i));
            }
        }

        private static void CheckSuccess(Vector2d[] points, Triangulation2Result result)
        {
            Assert.Equal(Triangulation2Status.Success, result.Status);
            Assert.Equal(Triangulation2FailureKind.None, result.FailureKind);
            Assert.Null(result.ProblemCoordinate);
            Assert.Null(result.ProblemInputIndex);
            Assert.NotNull(result.Mesh);
            var mesh = result.Mesh;
            CheckIdentity(points, mesh);
            string reason;
            Assert.True(mesh.IsValid(out reason), reason);
            Assert.NotEmpty(mesh.Triangles);
            Assert.Equal(mesh.Triangles.Count, mesh.Neighbours.Count);
            var edgeFaces = new Dictionary<Tuple<int, int>, List<int>>();
            var used = new HashSet<int>();
            BigInteger area = BigInteger.Zero;
            for (int t = 0; t < mesh.Triangles.Count; ++t)
            {
                var face = mesh.Triangles[t];
                for (int k = 0; k < 3; ++k)
                {
                    int v = face[k];
                    Assert.InRange(v, 0, points.Length - 1);
                    Assert.Equal(v, mesh.InputToVertex[v]);
                    used.Add(v);
                    var key = Edge(face[(k + 1) % 3], face[(k + 2) % 3]);
                    if (!edgeFaces.ContainsKey(key)) edgeFaces.Add(key, new List<int>());
                    edgeFaces[key].Add(t);
                }
                BigInteger orientation = Orient(points[face[0]], points[face[1]], points[face[2]]);
                Assert.True(orientation > 0);
                area += orientation;
                for (int i = 0; i < points.Length; ++i)
                    if (i != face[0] && i != face[1] && i != face[2])
                        Assert.True(InCircle(points[face[0]], points[face[1]], points[face[2]], points[i]) <= 0);
            }
            for (int i = 0; i < points.Length; ++i)
            {
                if (mesh.InputToVertex[i] == i) Assert.Contains(i, used);
                var expected = Enumerable.Range(0, mesh.Triangles.Count)
                    .Where(t => mesh.Triangles[t][0] == i || mesh.Triangles[t][1] == i || mesh.Triangles[t][2] == i)
                    .OrderBy(t => t).ToArray();
                Assert.Equal(expected, mesh.IncidentTriangles(i).OrderBy(t => t).ToArray());
            }
            var emitted = new HashSet<Tuple<int, int>>();
            foreach (var edge in mesh.Edges())
            {
                Assert.True(edge[0] < edge[1]);
                Assert.True(emitted.Add(Edge(edge[0], edge[1])));
            }
            Assert.Equal(edgeFaces.Count, emitted.Count);
            Assert.True(emitted.SetEquals(edgeFaces.Keys));
            for (int t = 0; t < mesh.Triangles.Count; ++t)
            {
                var face = mesh.Triangles[t];
                for (int k = 0; k < 3; ++k)
                {
                    var incident = edgeFaces[Edge(face[(k + 1) % 3], face[(k + 2) % 3])];
                    Assert.InRange(incident.Count, 1, 2);
                    int expected = incident.Count == 1 ? -1 : incident.Single(x => x != t);
                    Assert.Equal(expected, mesh.Neighbours[t][k]);
                }
            }
            var edges = emitted.ToArray();
            for (int i = 0; i < edges.Length; ++i)
                for (int j = i + 1; j < edges.Length; ++j)
                {
                    var a = edges[i]; var b = edges[j];
                    if (a.Item1 == b.Item1 || a.Item1 == b.Item2 || a.Item2 == b.Item1 || a.Item2 == b.Item2) continue;
                    Assert.False(Intersects(points[a.Item1], points[a.Item2], points[b.Item1], points[b.Item2]));
                }
            Assert.Equal(HullArea(points, mesh.InputToVertex), area);
        }

        private static Tuple<int, int> Edge(int a, int b)
        {
            return Tuple.Create(Math.Min(a, b), Math.Max(a, b));
        }

        // Every finite binary64 coordinate is an integer multiple of 2^-1074.
        private static BigInteger Exact(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7ffL);
            long mantissa = bits & 0x000fffffffffffffL;
            if (exponent != 0) mantissa |= 0x0010000000000000L;
            BigInteger integer = new BigInteger(mantissa) << (exponent == 0 ? 0 : exponent - 1);
            return bits < 0 ? -integer : integer;
        }

        private static BigInteger Orient(Vector2d a, Vector2d b, Vector2d c)
        {
            return (Exact(b.x) - Exact(a.x)) * (Exact(c.y) - Exact(a.y)) -
                   (Exact(b.y) - Exact(a.y)) * (Exact(c.x) - Exact(a.x));
        }

        private static BigInteger InCircle(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            BigInteger ax = Exact(a.x) - Exact(d.x), ay = Exact(a.y) - Exact(d.y);
            BigInteger bx = Exact(b.x) - Exact(d.x), by = Exact(b.y) - Exact(d.y);
            BigInteger cx = Exact(c.x) - Exact(d.x), cy = Exact(c.y) - Exact(d.y);
            return (ax * ax + ay * ay) * (bx * cy - by * cx) -
                   (bx * bx + by * by) * (ax * cy - ay * cx) +
                   (cx * cx + cy * cy) * (ax * by - ay * bx);
        }

        private static bool Intersects(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            int abC = Orient(a, b, c).Sign, abD = Orient(a, b, d).Sign;
            int cdA = Orient(c, d, a).Sign, cdB = Orient(c, d, b).Sign;
            if (abC == 0 && OnSegment(a, b, c)) return true;
            if (abD == 0 && OnSegment(a, b, d)) return true;
            if (cdA == 0 && OnSegment(c, d, a)) return true;
            if (cdB == 0 && OnSegment(c, d, b)) return true;
            return abC * abD < 0 && cdA * cdB < 0;
        }

        private static bool OnSegment(Vector2d a, Vector2d b, Vector2d p)
        {
            return p.x >= Math.Min(a.x, b.x) && p.x <= Math.Max(a.x, b.x) &&
                   p.y >= Math.Min(a.y, b.y) && p.y <= Math.Max(a.y, b.y);
        }

        private static BigInteger HullArea(Vector2d[] points, IReadOnlyList<int> mapping)
        {
            var sorted = Enumerable.Range(0, points.Length).Where(i => mapping[i] == i)
                .OrderBy(i => points[i].x).ThenBy(i => points[i].y).ToArray();
            var lower = new List<int>();
            foreach (int i in sorted)
            {
                while (lower.Count >= 2 && Orient(points[lower[lower.Count - 2]], points[lower[lower.Count - 1]], points[i]) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(i);
            }
            var upper = new List<int>();
            foreach (int i in sorted.Reverse())
            {
                while (upper.Count >= 2 && Orient(points[upper[upper.Count - 2]], points[upper[upper.Count - 1]], points[i]) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(i);
            }
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            BigInteger sum = BigInteger.Zero;
            for (int i = 0; i < lower.Count; ++i)
            {
                var a = points[lower[i]];
                var b = points[lower[(i + 1) % lower.Count]];
                sum += Exact(a.x) * Exact(b.y) - Exact(a.y) * Exact(b.x);
            }
            return sum;
        }

        private static string Topology(TriangulationMesh2 mesh)
        {
            var result = new StringBuilder();
            foreach (int id in mesh.InputToVertex) result.Append(id).Append(',');
            result.Append('|');
            foreach (var face in mesh.Triangles)
                for (int k = 0; k < 3; ++k) result.Append(face[k]).Append(',');
            result.Append('|');
            foreach (var neighbours in mesh.Neighbours)
                for (int k = 0; k < 3; ++k) result.Append(neighbours[k]).Append(',');
            result.Append('|');
            foreach (var edge in mesh.Edges()) result.Append(edge[0]).Append(',').Append(edge[1]).Append(';');
            return result.ToString();
        }

        private static string Fingerprint(TriangulationMesh2 mesh)
        {
            var result = new StringBuilder();
            foreach (var vertex in mesh.Vertices)
                result.Append(BitConverter.DoubleToInt64Bits(vertex.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(vertex.y)).Append(';');
            return result.Append('|').Append(Topology(mesh)).ToString();
        }
    }
}
