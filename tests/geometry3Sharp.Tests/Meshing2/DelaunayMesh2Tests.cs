using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The M1 mesher <see cref="DelaunayMesh2"/> and its kernel. The gates (oracle per case, invariance, reuse, determinism)
    /// run in Meshing2CandidateTests through the registered candidate; these tests add fuzzing against the oracle, the tie
    /// rule, the API of <see cref="TriangulationMesh2"/> and the verification of a corrupted mesh.
    /// </summary>
    public class DelaunayMesh2Tests
    {
        static Triangulation2Result Mesh(IReadOnlyList<Vector2d> points) => new DelaunayMesh2().Compute(points);

        static Mesh2Input Input(IReadOnlyList<Vector2d> p) => new Mesh2Input { X = p.Select(q => q.x).ToArray(), Y = p.Select(q => q.y).ToArray() };

        /// <summary> small inputs full of what breaks kernels: ties on grids, duplicates, extreme scales, last-bit nudges </summary>
        static Vector2d[] FuzzInput(int seed)
        {
            var r = new Random(seed);
            int n = 3 + r.Next(60);
            var p = new Vector2d[n];
            int style = seed % 6;
            for (int i = 0; i < n; ++i) {
                switch (style) {
                    case 0: p[i] = new Vector2d(r.Next(6), r.Next(6)); break;                       // grid ties and duplicates
                    case 1: p[i] = new Vector2d(r.NextDouble(), r.NextDouble()); break;
                    case 2: { double a = r.Next(8); p[i] = new Vector2d(a, 2 * a + (r.Next(3) == 0 ? Math.Pow(2, -50) : 0)); break; } // nearly collinear
                    case 3: { var c = new[] { (5, 0), (3, 4), (0, 5), (-3, 4), (-5, 0), (-4, -3), (0, -5), (4, 3), (4, -3), (-4, 3) }[r.Next(10)]; p[i] = new Vector2d(c.Item1, c.Item2); break; } // cocircular
                    case 4: p[i] = new Vector2d(r.Next(1 << 20) * Math.Pow(2, -1060), r.Next(1 << 20) * Math.Pow(2, -1060)); break; // subnormal
                    default: p[i] = new Vector2d(5.7e6 + r.Next(16) * Math.Pow(2, -20), 3e5 + r.Next(16) * Math.Pow(2, -20)); break; // UTM, fine grid
                }
                if (r.Next(10) == 0 && i > 0) p[i] = p[r.Next(i)];                                   // exact duplicate
            }
            if (seed % 7 == 0) { double s = Math.Pow(2, 200); for (int i = 0; i < n; ++i) p[i] = new Vector2d(p[i].x * s, p[i].y * s); }
            return p;
        }

        [Fact]
        public void Fuzz_EveryResultPassesTheOracle_AndItsOwnCheck()
        {
            for (int seed = 0; seed < 600; ++seed) {
                var p = FuzzInput(seed);
                var result = Mesh(p);
                Assert.NotEqual(Triangulation2Status.Failed, result.Status);
                Assert.True(result.Mesh.IsValid(out string reason), "seed " + seed + ": " + reason);
                var output = new DelaunayMesh2Candidate().Run(Input(p));
                var report = Oracle.Check(Input(p), output, new OracleExpect { Milestone = Milestone.M1 });
                Assert.True(report.Ok, "seed " + seed + ": " + report);
            }
        }

        static string[] TriangleSet(TriangulationMesh2 m) => m.Triangles.Select(t => t.a + " " + t.b + " " + t.c).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        static string[] KernelTriangles(Vector2d[] p, int[] order)
        {
            var k = new DelaunayKernel2(order.Select(i => p[i].x).ToArray(), order.Select(i => p[i].y).ToArray(), order);
            Assert.True(k.Build(CancellationToken.None));
            Assert.True(k.CanonicalizeTies(out string failure) >= 0, failure);
            DelaunayMesh2.Extract(k, out var tris, out _);
            return tris.Select(t => t.a + " " + t.b + " " + t.c).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }

        [Theory]
        [InlineData(0)]   // 6x6 grid with duplicates: every cell a tie
        [InlineData(3)]   // ten points on one circle
        [InlineData(6)]
        [InlineData(12)]
        public void TieRule_ResultDoesNotDependOnTheInsertionOrder(int seed)
        {
            var p = FuzzInput(seed);
            var natural = Enumerable.Range(0, p.Length).ToArray();
            var expected = KernelTriangles(p, natural);
            var r = new Random(seed);
            for (int round = 0; round < 20; ++round) {
                var order = natural.OrderBy(_ => r.Next()).ToArray();
                Assert.Equal(expected, KernelTriangles(p, order));
            }
        }

        [Fact]
        public void TieRule_IsAFanFromTheSmallestIndex()
        {
            // a square: both diagonals are Delaunay; the fan from vertex 0 takes the diagonal 0-2
            var p = new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(1, 1), new Vector2d(0, 1) };
            Assert.Equal(new[] { "0 1 2", "0 2 3" }, TriangleSet(Mesh(p).Mesh));
            // the same square, numbered from another corner: vertex 0 is now (1, 0), so the diagonal is the other one
            var q = new[] { new Vector2d(1, 0), new Vector2d(1, 1), new Vector2d(0, 1), new Vector2d(0, 0) };
            Assert.Equal(new[] { "0 1 2", "0 2 3" }, TriangleSet(Mesh(q).Mesh));
            // and a regular octagon on integer points of the circle of radius 5 (cocircular): six triangles, all at vertex 0
            var o = new[] { new Vector2d(5, 0), new Vector2d(4, 3), new Vector2d(0, 5), new Vector2d(-3, 4), new Vector2d(-5, 0), new Vector2d(-4, -3), new Vector2d(0, -5), new Vector2d(3, -4) };
            var m = Mesh(o).Mesh;
            Assert.Equal(6, m.TriangleCount);
            Assert.All(m.Triangles, t => Assert.Equal(0, t.a));
        }

        [Theory]
        [InlineData(double.NaN, 0)]
        [InlineData(0, double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity, 0)]
        [InlineData(1.0000000001e70, 0)]
        [InlineData(0, -2e70)]
        public void InvalidCoordinates_AreArgumentErrors(double x, double y)
        {
            var p = new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(x, y), new Vector2d(0, 1) };
            var e = Assert.Throws<ArgumentException>(() => Mesh(p));
            Assert.Contains("points[2]", e.Message);
            Assert.Throws<ArgumentNullException>(() => Mesh(null));
        }

        [Fact]
        public void CoordinatesAtTheLimit_Work()
        {
            var p = new[] { new Vector2d(-1e70, -1e70), new Vector2d(1e70, -1e70), new Vector2d(1e70, 1e70), new Vector2d(0, 3) };
            var result = Mesh(p);
            Assert.Equal(Triangulation2Status.Success, result.Status);
            Assert.True(result.Mesh.IsValid(out string reason), reason);
        }

        public static IEnumerable<object[]> DegenerateInputs()
        {
            yield return new object[] { new Vector2d[0], new int[0] };
            yield return new object[] { new[] { new Vector2d(1, 1) }, new[] { 0 } };
            yield return new object[] { new[] { new Vector2d(1, 1), new Vector2d(2, 1) }, new[] { 0, 1 } };
            yield return new object[] { Enumerable.Repeat(new Vector2d(1, 1), 5).ToArray(), new[] { 0, 0, 0, 0, 0 } };      // F-57
            yield return new object[] { new[] { new Vector2d(0, 0), new Vector2d(-0.0, 0), new Vector2d(3, 3), new Vector2d(1, 1), new Vector2d(3, 3) }, new[] { 0, 0, 2, 3, 2 } }; // collinear, -0
        }

        [Theory]
        [MemberData(nameof(DegenerateInputs))]
        public void NoArea_GivesAnEmptyMesh_WithTheMapping(Vector2d[] p, int[] mapping)
        {
            var result = Mesh(p);
            Assert.Equal(Triangulation2Status.Empty, result.Status);
            Assert.Equal(0, result.Mesh.TriangleCount);
            Assert.Equal(mapping, result.Mesh.InputToVertex);
            Assert.True(result.Mesh.IsValid(out string reason), reason);
        }

        [Fact]
        public void Duplicates_MapToTheFirstIndex_EvenWhenInsertedLater()
        {
            // many duplicates in random order: whichever copy the kernel inserts first, the first input index is reported
            var r = new Random(5);
            var p = Enumerable.Range(0, 2000).Select(_ => new Vector2d(r.Next(20), r.Next(20))).ToArray();
            var m = Mesh(p).Mesh;
            for (int i = 0; i < p.Length; ++i) {
                int first = Array.FindIndex(p, q => q.x == p[i].x && q.y == p[i].y);
                Assert.Equal(first, m.InputToVertex[i]);
                if (first != i) Assert.Empty(m.IncidentTriangles(i));
            }
            Assert.True(m.IsValid(out string reason), reason);
            Assert.Equal(p.Distinct().Count(), m.Triangles.SelectMany(t => new[] { t.a, t.b, t.c }).Distinct().Count());
        }

        [Fact]
        public void Cancellation_Throws()
        {
            var p = Enumerable.Range(0, 5000).Select(i => new Vector2d(i % 71, i / 71 + 0.25 * (i % 3))).ToArray();
            var cancelled = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() => new DelaunayMesh2().Compute(p, cancelled));
            // inside the insertion loop too
            var k = new DelaunayKernel2(p.Select(q => q.x).ToArray(), p.Select(q => q.y).ToArray(), Enumerable.Range(0, p.Length).ToArray());
            Assert.Throws<OperationCanceledException>(() => k.Build(cancelled));
            // and the instance is usable afterwards
            Assert.Equal(Triangulation2Status.Success, new DelaunayMesh2().Compute(p).Status);
        }

        static bool InClosed(TriangulationMesh2 m, int t, Vector2d q)
        {
            var tri = m.Triangles[t];
            var a = m.Vertices[tri.a]; var b = m.Vertices[tri.b]; var c = m.Vertices[tri.c];
            return ExactPredicates.Orient2d(a, b, q) >= 0 && ExactPredicates.Orient2d(b, c, q) >= 0 && ExactPredicates.Orient2d(c, a, q) >= 0;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        public void TryLocate_FindsTheSmallestContainingTriangle(int seed)
        {
            var p = FuzzInput(seed * 6 + (seed == 5 ? 5 : 0));
            if (seed == 1) p = Enumerable.Range(0, 400).Select(i => new Vector2d(i % 20, i / 20)).ToArray();
            var m = Mesh(p).Mesh;
            var probes = new List<Vector2d>();
            foreach (var t in m.Triangles) {
                var a = m.Vertices[t.a]; var b = m.Vertices[t.b]; var c = m.Vertices[t.c];
                probes.Add(a); probes.Add((a + b) * 0.5); probes.Add((a + b + c) / 3);
                probes.Add(new Vector2d(2 * a.x - c.x, 2 * a.y - c.y));   // often outside
            }
            foreach (var q in probes) {
                int expected = -1;
                for (int t = 0; t < m.TriangleCount; ++t) if (InClosed(m, t, q)) { expected = t; break; }
                bool found = m.TryLocate(q, out int got);
                Assert.Equal(expected >= 0, found);
                Assert.Equal(expected, got);
            }
            Assert.Throws<ArgumentException>(() => m.TryLocate(new Vector2d(double.NaN, 0), out _));
            Assert.Throws<ArgumentException>(() => m.TryLocate(new Vector2d(0, 2e70), out _));
            Assert.False(Mesh(new Vector2d[0]).Mesh.TryLocate(Vector2d.Zero, out int none));
            Assert.Equal(-1, none);
        }

        [Fact]
        public void TryLocate_ScalesToLargeMeshes()
        {
            var r = new Random(3);
            var p = Enumerable.Range(0, 100000).Select(_ => new Vector2d(r.NextDouble(), r.NextDouble())).ToArray();
            var m = Mesh(p).Mesh;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 2000; ++i) {
                var q = new Vector2d(r.NextDouble(), r.NextDouble());
                if (m.TryLocate(q, out int t)) Assert.True(InClosed(m, t, q));
            }
            // a linear scan per query would take seconds; the walk takes well under one
            Assert.True(watch.Elapsed.TotalSeconds < 10, watch.Elapsed.ToString());
        }

        [Fact]
        public void IncidentTriangles_And_Edges_MatchTheTriangles()
        {
            var r = new Random(11);
            var p = Enumerable.Range(0, 300).Select(_ => new Vector2d(r.Next(30), r.Next(30))).ToArray();
            var m = Mesh(p).Mesh;
            for (int v = 0; v < p.Length; ++v) {
                var expected = Enumerable.Range(0, m.TriangleCount).Where(t => m.Triangles[t].a == v || m.Triangles[t].b == v || m.Triangles[t].c == v).ToList();
                Assert.Equal(expected, m.IncidentTriangles(v));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => m.IncidentTriangles(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => m.IncidentTriangles(p.Length));
            var edges = m.Edges().ToList();
            var expectedEdges = m.Triangles.SelectMany(t => new[] { (t.a, t.b), (t.b, t.c), (t.c, t.a) })
                .Select(e => new Index2i(Math.Min(e.Item1, e.Item2), Math.Max(e.Item1, e.Item2))).Distinct()
                .OrderBy(e => e.a).ThenBy(e => e.b).ToList();
            Assert.Equal(expectedEdges, edges);
            for (int t = 0; t < m.TriangleCount; ++t)
                for (int k = 0; k < 3; ++k) {
                    int nb = m.Neighbours[t][k];
                    if (nb < 0) continue;
                    var tri = m.Triangles[t];
                    int u = tri[(k + 1) % 3], w = tri[(k + 2) % 3];
                    var o = m.Triangles[nb];
                    Assert.True((o.a == u || o.b == u || o.c == u) && (o.a == w || o.b == w || o.c == w));
                }
        }

        static TriangulationMesh2 Corrupt(TriangulationMesh2 m, Action<Vector2d[], int[], Index3i[], Index3i[]> change)
        {
            var v = m.Vertices.ToArray(); var map = m.InputToVertex.ToArray(); var t = m.Triangles.ToArray(); var n = m.Neighbours.ToArray();
            change(v, map, t, n);
            return new TriangulationMesh2(v, map, t, n);
        }

        [Fact]
        public void IsValid_RejectsEveryKindOfDefect()
        {
            // a 3x3 grid plus a centre point off the grid (no ties) and one duplicate
            var p = new List<Vector2d>();
            for (int i = 0; i < 9; ++i) p.Add(new Vector2d(i % 3, i / 3 + 0.125 * (i % 3) * (i % 2)));
            p.Add(new Vector2d(0.75, 0.5)); p.Add(p[4]);
            var m = Mesh(p).Mesh;
            Assert.True(m.IsValid(out string ok), ok);
            int interior = Enumerable.Range(0, m.TriangleCount).First(t => m.Neighbours[t].a >= 0);
            var cases = new Dictionary<string, Action<Vector2d[], int[], Index3i[], Index3i[]>> {
                { "not counter-clockwise", (v, map, t, n) => { var x = t[0]; t[0] = new Index3i(x.a, x.c, x.b); } },
                { "not mutual neighbours", (v, map, t, n) => { var x = n[interior]; n[interior] = new Index3i(-1, x.b, x.c); } },
                { "is mapped to", (v, map, t, n) => map[10] = 9 },
                { "duplicate slot", (v, map, t, n) => { for (int i = 0; i < t.Length; ++i) t[i] = Replace(t[i], 4, 10); } },
                { "invalid vertex", (v, map, t, n) => t[0] = new Index3i(t[0].a, t[0].b, 99) },
            };
            foreach (var c in cases) {
                var bad = Corrupt(m, c.Value);
                Assert.False(bad.IsValid(out string reason), c.Key);
                Assert.Contains(c.Key, reason);
            }
            // a point inside the hull that is in no triangle: remove nothing, but declare a new distinct point
            var v2 = m.Vertices.Concat(new[] { new Vector2d(0.5, 0.25) }).ToArray();
            var map2 = m.InputToVertex.Concat(new[] { m.Vertices.Count }).ToArray();
            Assert.False(new TriangulationMesh2(v2, map2, m.Triangles.ToArray(), m.Neighbours.ToArray()).IsValid(out string missing));
            Assert.Contains("in no triangle", missing);
            // a flipped interior edge: valid triangles, not Delaunay
            Assert.False(FlipAnyEdge(m).IsValid(out string flipped));
            Assert.Contains("not locally Delaunay", flipped);
        }

        static Index3i Replace(Index3i t, int from, int to) => new Index3i(t.a == from ? to : t.a, t.b == from ? to : t.b, t.c == from ? to : t.c);

        /// <summary> flips the first interior edge whose quadrilateral is convex: a valid triangulation that is not Delaunay </summary>
        static TriangulationMesh2 FlipAnyEdge(TriangulationMesh2 m)
        {
            for (int t = 0; t < m.TriangleCount; ++t)
                for (int k = 0; k < 3; ++k) {
                    int s = m.Neighbours[t][k];
                    if (s < t) continue;
                    var T = m.Triangles[t];
                    int a = T[k], b = T[(k + 1) % 3], c = T[(k + 2) % 3];
                    var S = m.Triangles[s];
                    int j = S.a == c ? 0 : S.b == c ? 1 : 2;
                    int d = S[(j + 2) % 3];
                    var P = m.Vertices;
                    if (ExactPredicates.Orient2d(P[a], P[b], P[d]) <= 0 || ExactPredicates.Orient2d(P[a], P[d], P[c]) <= 0) continue;
                    var tris = m.Triangles.ToArray();
                    tris[t] = new Index3i(a, b, d); tris[s] = new Index3i(a, d, c);
                    return new TriangulationMesh2(m.Vertices.ToArray(), m.InputToVertex.ToArray(), tris, Neighbours(tris));
                }
            throw new InvalidOperationException("no convex quadrilateral");
        }

        static Index3i[] Neighbours(Index3i[] tris)
        {
            var map = new Dictionary<(int, int), int>();
            for (int t = 0; t < tris.Length; ++t) for (int k = 0; k < 3; ++k) map[(tris[t][(k + 1) % 3], tris[t][(k + 2) % 3])] = t;
            return tris.Select(t => {
                var n = new Index3i();
                for (int k = 0; k < 3; ++k) n[k] = map.TryGetValue((t[(k + 2) % 3], t[(k + 1) % 3]), out int o) ? o : -1;
                return n;
            }).ToArray();
        }

        [Fact]
        public void KernelVerification_DetectsACorruptedTriangulation()
        {
            var p = Enumerable.Range(0, 50).Select(i => new Vector2d(i * 7 % 13, i * 5 % 11 + 0.5 * (i % 2))).ToArray();
            var k = new DelaunayKernel2(p.Select(q => q.x).ToArray(), p.Select(q => q.y).ToArray(), Enumerable.Range(0, p.Length).ToArray());
            Assert.True(k.Build(CancellationToken.None));
            int real = Enumerable.Range(0, k.TriangleSlots).First(t => !k.IsGhost(t));
            int x = k.V[3 * real]; k.V[3 * real] = k.V[3 * real + 1]; k.V[3 * real + 1] = x;
            Assert.Equal(-1, k.CanonicalizeTies(out string failure));
            Assert.NotNull(failure);
            Assert.True(k.FailureVertex >= 0);
        }

        [Theory]
        [InlineData(100000, false)]
        [InlineData(90000, true)]
        public void LargeInputs_AreValid(int n, bool grid)
        {
            var r = new Random(n);
            int side = (int)Math.Sqrt(n);
            var p = Enumerable.Range(0, n).Select(i => grid ? new Vector2d(i % side, i / side) : new Vector2d(r.NextDouble(), r.NextDouble())).ToArray();
            var result = Mesh(p);
            Assert.Equal(Triangulation2Status.Success, result.Status);
            Assert.True(result.Mesh.IsValid(out string reason), reason);
            if (grid) Assert.Equal(2 * (side - 1) * (side - 1), result.Mesh.TriangleCount);
        }

        [Fact]
        public void Repeated_And_Parallel_RunsAreIdentical()
        {
            var r = new Random(17);
            var p = Enumerable.Range(0, 3000).Select(_ => new Vector2d(r.Next(60), r.Next(60))).ToArray();
            var mesher = new DelaunayMesh2();
            var first = mesher.Compute(p).Mesh;
            var results = new TriangulationMesh2[8];
            System.Threading.Tasks.Parallel.For(0, results.Length, i => results[i] = mesher.Compute(p).Mesh);
            foreach (var m in results.Concat(new[] { mesher.Compute(p).Mesh })) {
                Assert.Equal(first.Triangles, m.Triangles);   // same order, not only the same set
                Assert.Equal(first.Neighbours, m.Neighbours);
                Assert.Equal(first.InputToVertex, m.InputToVertex);
            }
        }
    }
}
