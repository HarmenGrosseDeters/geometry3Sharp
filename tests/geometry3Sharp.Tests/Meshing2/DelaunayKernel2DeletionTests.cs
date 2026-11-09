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
    /// Vertex deletion in the constrained Delaunay kernel (DelaunayKernel2.DeleteVertex): after every deletion the triangulation is
    /// consistent and constrained Delaunay (every unconstrained edge locally Delaunay, exactly), it equals the triangulation built
    /// afresh from the remaining points up to cocircular ties, the 2^k scales give the same triangulation bit for bit, and a vertex
    /// that may not be deleted (an input vertex, one on the hull, one on a constraint) leaves everything as it was.
    /// </summary>
    public class DelaunayKernel2DeletionTests
    {
        static int Next(int e) => e % 3 == 2 ? e - 2 : e + 1;

        static DelaunayKernel2 Kernel(IList<Vector2d> pts)
        {
            var k = new DelaunayKernel2(pts.Select(p => p.x).ToArray(), pts.Select(p => p.y).ToArray(), Enumerable.Range(0, pts.Count).ToArray());
            Assert.True(k.Build(CancellationToken.None));
            k.BeginConstraints();
            return k;
        }

        /// <summary> twins reversed and linked both ways, constraints equal on both half-edges, real triangles counter-clockwise </summary>
        static void AssertConsistent(DelaunayKernel2 k, string where)
        {
            for (int e = 0; e < 3 * k.TriangleSlots; ++e) {
                int f = k.H[e];
                Assert.True(f >= 0 && f < 3 * k.TriangleSlots, where + ": twin of " + e + " out of range");
                Assert.Equal(e, k.H[f]);
                Assert.Equal(k.V[e], k.V[Next(f)]);
                Assert.Equal(k.V[Next(e)], k.V[f]);
                Assert.Equal(k.Seg[e], k.Seg[f]);
            }
            for (int t = 0; t < k.TriangleSlots; ++t)
                if (!k.IsGhost(t)) Assert.True(k.OrientV(k.V[3 * t], k.V[3 * t + 1], k.V[3 * t + 2]) > 0, where + ": triangle " + t + " not counter-clockwise");
            Assert.Empty(k.NonDelaunayEdges());
        }

        /// <summary> the real triangles' edges, by coordinates </summary>
        static HashSet<(Vector2d, Vector2d)> Edges(DelaunayKernel2 k)
        {
            var edges = new HashSet<(Vector2d, Vector2d)>();
            for (int e = 0; e < 3 * k.TriangleSlots; ++e) {
                if (k.IsGhost(e / 3)) continue;
                Vector2d a = k.Point(k.V[e]), b = k.Point(k.V[Next(e)]);
                edges.Add(a.x < b.x || a.x == b.x && a.y < b.y ? (a, b) : (b, a));
            }
            return edges;
        }

        /// <summary> every edge of k that the other lacks is a cocircular tie: its two triangles share their circumcircle </summary>
        static void AssertSameUpToTies(DelaunayKernel2 k, DelaunayKernel2 other, string where)
        {
            var mine = Edges(k); var theirs = Edges(other);
            Assert.Equal(theirs.Count, mine.Count);
            for (int e = 0; e < 3 * k.TriangleSlots; ++e) {
                int f = k.H[e];
                if (f < e || k.IsGhost(e / 3) || k.IsGhost(f / 3)) continue;
                Vector2d a = k.Point(k.V[e]), b = k.Point(k.V[Next(e)]);
                if (theirs.Contains(a.x < b.x || a.x == b.x && a.y < b.y ? (a, b) : (b, a))) continue;
                Vector2d c = k.Point(k.V[Next(Next(e))]), d = k.Point(k.V[Next(Next(f))]);
                Assert.True(ExactPredicates.InCircle(a, b, c, d) == 0, where + ": edge " + a + " " + b + " differs from the rebuilt triangulation and is no tie");
            }
        }

        /// <summary>
        /// points on a coarse grid (many cocircular quadruples), the square's corners on the hull; Steiner points strictly inside,
        /// then deleted in a random order, each step checked and compared with the triangulation built afresh
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void InsertAndDelete_MatchesTheRebuiltTriangulation(int seed)
        {
            var r = new Random(seed);
            var pts = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(16, 0), new Vector2d(16, 16), new Vector2d(0, 16) };
            while (pts.Count < 24) { var p = new Vector2d(1 + r.Next(15), 1 + r.Next(15)); if (!pts.Contains(p)) pts.Add(p); }
            var k = Kernel(pts);
            var steiner = new List<int>();
            for (int i = 0; i < 40; ++i) {
                var p = new Vector2d(1 + r.Next(57) * 0.25, 1 + r.Next(57) * 0.25);
                int before = k.VertexCount, v = k.InsertPoint(p.x, p.y);
                if (k.VertexCount > before) steiner.Add(v);
            }
            AssertConsistent(k, "after the insertions");
            var alive = new HashSet<int>(steiner);
            foreach (int v in steiner.OrderBy(_ => r.Next()).ToList()) {
                Assert.True(k.DeleteVertex(v), "vertex " + v + " could not be deleted");
                alive.Remove(v);
                AssertConsistent(k, "after deleting " + v);
                var left = pts.Concat(alive.OrderBy(a => a).Select(a => k.Point(a))).ToList();
                AssertSameUpToTies(k, Kernel(left), "after deleting " + v);
                foreach (int a in alive) Assert.Contains(a, Enumerable.Range(0, 3 * k.TriangleSlots).Select(e => k.V[e]));
                Assert.DoesNotContain(v, Enumerable.Range(0, 3 * k.TriangleSlots).Select(e => k.V[e]));
            }
            Assert.Equal(2 * pts.Count - 2 - 4, Enumerable.Range(0, k.TriangleSlots).Count(t => !k.IsGhost(t)));
        }

        /// <summary>
        /// with constraints: edges of the triangulation made constrained, Steiner points around them deleted again; the constraints stay,
        /// the triangulation stays constrained Delaunay, and a vertex on a constraint (inserted onto it) is refused
        /// </summary>
        [Theory]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        public void InsertAndDelete_WithConstraints_StaysConstrainedDelaunay(int seed)
        {
            var r = new Random(seed);
            var pts = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(16, 0), new Vector2d(16, 16), new Vector2d(0, 16) };
            while (pts.Count < 20) { var p = new Vector2d(1 + r.Next(15), 1 + r.Next(15)); if (!pts.Contains(p)) pts.Add(p); }
            var k = Kernel(pts);
            // constrain some edges between input points
            var constrained = new List<(Vector2d, Vector2d)>();
            for (int e = 0; e < 3 * k.TriangleSlots && constrained.Count < 6; ++e) {
                int f = k.H[e];
                if (f < e || k.IsGhost(e / 3) || k.IsGhost(f / 3) || k.Seg[e] >= 0 || r.Next(4) != 0) continue;
                k.SetConstraint(e, k.NewConstraint(new List<int> { constrained.Count }));
                constrained.Add((k.Point(k.V[e]), k.Point(k.V[Next(e)])));
            }
            var steiner = new List<int>();
            // the midpoint of every constraint (exact: half-integers), which splits it: a vertex on a constraint, to be refused
            foreach (var (a, b) in constrained) {
                int before = k.VertexCount, v = k.InsertPoint((a.x + b.x) / 2, (a.y + b.y) / 2);
                Assert.True(k.VertexCount > before);
                steiner.Add(v);
            }
            for (int i = 0; i < 30; ++i) {
                var p = new Vector2d(1 + r.Next(57) * 0.25, 1 + r.Next(57) * 0.25);
                int before = k.VertexCount, v = k.InsertPoint(p.x, p.y);
                if (k.VertexCount > before) steiner.Add(v);
            }
            AssertConsistent(k, "after the insertions");
            int refused = 0;
            foreach (int v in steiner.OrderBy(_ => r.Next()).ToList()) {
                bool onConstraint = k.Star(v).Any(e => k.Seg[e] >= 0);
                var before = (k.V.Take(3 * k.TriangleSlots).ToArray(), k.H.Take(3 * k.TriangleSlots).ToArray());
                bool deleted = k.DeleteVertex(v);
                Assert.Equal(!onConstraint, deleted);
                if (!deleted) {
                    ++refused;
                    Assert.Equal(before.Item1, k.V.Take(3 * k.TriangleSlots).ToArray());
                    Assert.Equal(before.Item2, k.H.Take(3 * k.TriangleSlots).ToArray());
                }
                AssertConsistent(k, "after deleting " + v);
            }
            Assert.True(refused >= constrained.Count, "the midpoints of the constraints were refused");
            // every constraint is still there, as one edge or as a chain of constrained edges along it
            foreach (var (a, b) in constrained) {
                int u = Enumerable.Range(0, k.VertexCount).First(v => v != k.Infinite && k.Point(v) == a);
                Assert.True(k.Star(u).Any(e => k.Seg[e] >= 0 && ExactPredicates.Orient2d(a, b, k.Point(k.V[Next(e)])) == 0), "constraint at " + a + " lost");
            }
        }

        [Fact]
        public void Refused_InputVertex_HullVertex_LeaveEverythingAsItWas()
        {
            var pts = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(8, 0), new Vector2d(8, 8), new Vector2d(0, 8), new Vector2d(3, 4) };
            var k = Kernel(pts);
            int hull = k.InsertPoint(4, 0);   // on the hull edge (0, 0)-(8, 0)
            int inner = k.InsertPoint(5, 5);
            var v = k.V.Take(3 * k.TriangleSlots).ToArray(); var h = k.H.Take(3 * k.TriangleSlots).ToArray(); int slots = k.TriangleSlots;
            for (int input = 0; input < pts.Count; ++input) Assert.False(k.DeleteVertex(input));
            Assert.False(k.DeleteVertex(hull));
            Assert.False(k.DeleteVertex(k.Infinite));
            Assert.Equal(slots, k.TriangleSlots);
            Assert.Equal(v, k.V.Take(3 * k.TriangleSlots).ToArray());
            Assert.Equal(h, k.H.Take(3 * k.TriangleSlots).ToArray());
            Assert.True(k.DeleteVertex(inner));
            Assert.False(k.DeleteVertex(inner));   // deleted already
            Assert.Equal(slots - 2, k.TriangleSlots);
            AssertConsistent(k, "after deleting the inner point");
        }

        /// <summary>
        /// a hole that reaches beyond the outer ring (D5: rings that overlap without nesting) has ring edges with the outside on both
        /// sides; the carve leaves rings alone (it lifts only pieces of free segments), so the tag check from scratch, which finds the
        /// domain by ring parity, still agrees with the tags after every operation (review of T15: lifting them made it leak)
        /// </summary>
        [Theory]
        [InlineData(0.0, false)]
        [InlineData(0.25, false)]
        [InlineData(1.0 / 3, true)]
        public void Carve_KeepsRingEdges_TheTagCheckHolds(double snap, bool constrainedQuality)
        {
            var input = new Mesh2Input {
                X = new[] { 0, 16, 16, 0, 12, 20, 12, 9.5, 18.25 },
                Y = new[] { 0, 0, 16, 16, 6, 8, 10, 3.75, 9.5 },
                MinAngleDeg = 25, SteinerLimit = 50000, SnapTolerance = snap, ConstrainedQuality = constrainedQuality };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
            input.Segments.Add(new SegmentInput(7, 8));
            QualityMesh2.Refiner.LiftedForTests = 0;
            QualityMesh2.Refiner.CheckTagsForTests = true;
            Mesh2Output o;
            try { o = new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.CheckTagsForTests = false; }
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.True(QualityMesh2.Refiner.LiftedForTests > 0, "the segment's piece inside the hole beyond the square is lifted");
        }

        /// <summary> a star whose triangles carry two tags (a region boundary that is no constraint) is refused, unchanged </summary>
        [Fact]
        public void Refused_TwoTagsInTheStar()
        {
            var pts = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(8, 0), new Vector2d(8, 8), new Vector2d(0, 8), new Vector2d(3, 4) };
            var k = Kernel(pts);
            int inner = k.InsertPoint(5, 5);
            int one = k.Star(inner).First() / 3;
            k.Tag[one] = 1;
            var v = k.V.Take(3 * k.TriangleSlots).ToArray(); int slots = k.TriangleSlots;
            Assert.False(k.DeleteVertex(inner));
            Assert.Equal(slots, k.TriangleSlots);
            Assert.Equal(v, k.V.Take(3 * k.TriangleSlots).ToArray());
            foreach (int e in k.Star(inner)) k.Tag[e / 3] = 1;
            Assert.True(k.DeleteVertex(inner));
            AssertConsistent(k, "after deleting the inner point");
        }

        /// <summary>
        /// a made input vertex (on the grid, a hot pixel centre snap rounding appended) can be deleted when its input index is at least
        /// firstMadeInput, an input vertex below it cannot
        /// </summary>
        [Fact]
        public void MadeInputVertex_IsDeletedAboveTheThreshold()
        {
            var pts = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(8, 0), new Vector2d(8, 8), new Vector2d(0, 8), new Vector2d(3, 4), new Vector2d(5, 5) };
            var k = Kernel(pts);
            int Id(Vector2d p) => Enumerable.Range(0, pts.Count).Single(v => k.X[v] == p.x && k.Y[v] == p.y);
            int a = Id(pts[4]), b = Id(pts[5]);
            Assert.False(k.DeleteVertex(b));
            Assert.False(k.DeleteVertex(a, firstMadeInput: 5));
            Assert.True(k.DeleteVertex(b, firstMadeInput: 5));
            AssertConsistent(k, "after deleting the made input vertex");
            AssertSameUpToTies(k, Kernel(pts.Take(5).ToList()), "after deleting the made input vertex");
        }

        /// <summary> a point deleted and inserted again: a valid triangulation, the same as before the first insertion up to ties </summary>
        [Fact]
        public void DeleteThenInsertAgain()
        {
            var pts = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(8, 0), new Vector2d(8, 8), new Vector2d(0, 8), new Vector2d(3, 4), new Vector2d(5, 2) };
            var k = Kernel(pts);
            int v = k.InsertPoint(4.5, 5.25);
            Assert.True(k.DeleteVertex(v));
            AssertSameUpToTies(k, Kernel(pts), "deleted");
            k.InsertPoint(4.5, 5.25);
            AssertConsistent(k, "inserted again");
            AssertSameUpToTies(k, Kernel(pts.Concat(new[] { new Vector2d(4.5, 5.25) }).ToList()), "inserted again");
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> the same insertions and deletions under every similarity: 2^k scales give the same triangulation bit for bit,
        /// the others a valid constrained Delaunay triangulation </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void InsertAndDelete_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            DelaunayKernel2 Run(Func<Vector2d, Vector2d> map)
            {
                var r = new Random(7);
                var pts = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(16, 0), new Vector2d(16, 16), new Vector2d(0, 16) };
                while (pts.Count < 20) { var p = new Vector2d(1 + r.Next(15), 1 + r.Next(15)); if (!pts.Contains(p)) pts.Add(p); }
                var k = Kernel(pts.Select(map).ToList());
                var steiner = new List<int>();
                for (int i = 0; i < 25; ++i) {
                    var p = map(new Vector2d(1 + r.Next(57) * 0.25, 1 + r.Next(57) * 0.25));
                    int before = k.VertexCount, v = k.InsertPoint(p.x, p.y);
                    if (k.VertexCount > before) steiner.Add(v);
                }
                foreach (int v in steiner.Where((_, i) => i % 3 != 0)) Assert.True(k.DeleteVertex(v), name + ": vertex " + v + " not deleted");
                AssertConsistent(k, name);
                return k;
            }
            var moved = Run(p => { var q = s.Apply(new Vector3d(p.x, p.y, 0)); return new Vector2d(q.x, q.y); });
            if (s.IsExact) {
                var plain = Run(p => p);
                Assert.Equal(plain.TriangleSlots, moved.TriangleSlots);
                Assert.Equal(plain.V.Take(3 * plain.TriangleSlots), moved.V.Take(3 * moved.TriangleSlots));
                for (int v = 0; v < plain.VertexCount; ++v) {
                    if (v == plain.Infinite) continue;
                    var q = s.Apply(new Vector3d(plain.X[v], plain.Y[v], 0));
                    Assert.Equal(q.x, moved.X[v]); Assert.Equal(q.y, moved.Y[v]);
                }
            }
        }

        /// <summary>
        /// the carve counts: a crossing made inside a hole is deleted before refinement (the hole case of the grid tests), off the grid
        /// (a vertex of the constrained phase) and on it (a hot pixel centre that snap rounding appended to the input)
        /// </summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(0.25)]
        public void Carve_DeletesACrossingInsideAHole(double snap)
        {
            var input = new Mesh2Input {
                X = new[] { 0, 16, 16, 0, 6, 9, 7, 8.28125, 5.078125, 13.671875, 3.625 },
                Y = new[] { 0, 0, 16, 16, 6, 6.5, 9, 4.140625, 14.59375, 9.75, 6.71875 },
                MinAngleDeg = 20, SteinerLimit = 50000, SnapTolerance = snap };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
            input.Segments.Add(new SegmentInput(7, 8)); input.Segments.Add(new SegmentInput(9, 10));
            QualityMesh2.Refiner.LiftedForTests = 0; QualityMesh2.Refiner.CarvedForTests = 0;
            QualityMesh2.Refiner.CheckTagsForTests = true;
            Mesh2Output o;
            try { o = new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.CheckTagsForTests = false; }
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            // the two pieces inside the hole on each side of their crossing, and the crossing itself
            Assert.Equal(4, QualityMesh2.Refiner.LiftedForTests);
            Assert.Equal(1, QualityMesh2.Refiner.CarvedForTests);
        }
    }
}
