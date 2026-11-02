using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Refinement on the snap grid: a split point that rounds onto a vertex already there (the vertex of another constraint) reroutes
    /// the piece through that vertex without rewriting the triangles between the old piece and the new chain, and they change sides
    /// of the boundary. Their tags must change with them. Found with the grid fuzz with holes in M3: a hole edge rerouted through a
    /// segment's vertex inside the hole left a domain triangle tagged as hole (SegmentMissing, BoundaryNotConstrained), and the other
    /// way round a hole triangle tagged as domain.
    /// </summary>
    public class QualityMesh2GridRerouteTests
    {
        /// <summary> a 16 square with a triangular hole and the segments of the reduced fuzz cases, on a grid of the pixel </summary>
        static Mesh2Input Case(int which, double? minAngle)
        {
            var x = new List<double> { 0, 16, 16, 0, 6, 9, 7 }; var y = new List<double> { 0, 0, 16, 16, 6, 6.5, 9 };
            var input = new Mesh2Input();
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
            var segments = which == 30
                ? new[] { (3.328125, 3.90625, 8.96875, 7.515625) }
                : new[] { (7.234375, 13.734375, 6.078125, 4.453125), (2.78125, 4.375, 8.75, 11.5) };
            foreach (var (ax, ay, bx, by) in segments) {
                x.Add(ax); y.Add(ay); x.Add(bx); y.Add(by);
                input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
            }
            input.X = x.ToArray(); input.Y = y.ToArray();
            input.SnapTolerance = which == 30 ? 0.5 : 0.25;
            input.MinAngleDeg = minAngle; input.SteinerLimit = 50000;
            return input;
        }

        static Mesh2Output RunChecked(Mesh2Input input)
        {
            QualityMesh2.Refiner.CheckTagsForTests = true;
            try { return new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.CheckTagsForTests = false; }
        }

        [Theory]
        [InlineData(30, 0.0)]
        [InlineData(30, 20.0)]
        [InlineData(30, 25.0)]
        [InlineData(31, 0.0)]
        [InlineData(31, 20.0)]
        [InlineData(31, 25.0)]
        public void SplitOntoAVertexAlreadyThere_RetagsTheTrianglesBetween(int which, double angle)
        {
            var input = Case(which, angle);
            // the tags against domain membership from scratch after every operation (throws on the first difference)
            QualityMesh2.Refiner.ReroutesForTests = 0;
            var o = RunChecked(input);
            Assert.True(QualityMesh2.Refiner.ReroutesForTests > 0, "the case no longer reroutes a piece through a vertex already there");
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> the same under every similarity: 2^k scales give the same mesh bit for bit, the others pass the oracle </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void SplitOntoAVertexAlreadyThere_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            foreach (int which in new[] { 30, 31 }) {
                var plain = Case(which, 20);
                var moved = Case(which, 20);
                for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
                moved.SnapTolerance = plain.SnapTolerance * s.Scale;
                var b = new QualityMesh2Candidate().Run(moved);
                Assert.Equal(Mesh2Status.Success, b.Status);
                var report = Oracle.Check(moved, b, new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(report.Ok, name + ", case " + which + ": " + report);
                if (s.IsExact) {
                    var a = new QualityMesh2Candidate().Run(plain);
                    Assert.Equal(a.VX.Select(v => v * s.Scale), b.VX);
                    Assert.Equal(a.VY.Select(v => v * s.Scale), b.VY);
                    Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
                }
            }
        }

        /// <summary> one mesher, two computes: the same mesh </summary>
        [Fact]
        public void SplitOntoAVertexAlreadyThere_Reuse()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Case(31, 20));
            var mesher = new QualityMesh2 { MinAngle = 20, SnapTolerance = 0.25, SteinerLimit = 50000 };
            var first = mesher.Compute(c).Mesh; var second = mesher.Compute(c).Mesh;
            Assert.Equal(first.Vertices, second.Vertices);
            Assert.Equal(first.Triangles, second.Triangles);
        }

        /// <summary>
        /// the sweep on a kernel triangulation with a constrained chain from u to w: it marks every triangle between the line u w and
        /// the chain (centroid inside that polygon), and only triangles inside the chain's convex hull. Variant 1 has points inside the
        /// region (triangles with no corner on the chain), variant 2 a chain with a vertex off the triangle u q w (a piece split again
        /// at a nearby vertex), variant 3 a vertex on the line u w
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void MarkSwept_MarksTheRegionBetweenPieceAndChain(int variant)
        {
            // (the sweep alone, on a wide region: in refinement the region is thinner than a pixel, which changes nothing here)
            var chainPts = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(5, 3), new Vector2d(10, 0) };
            var pts = new List<Vector2d> { new Vector2d(5, -9), new Vector2d(5, 14), new Vector2d(19, 1), new Vector2d(-9, 1) };
            if (variant == 1) pts.AddRange(new[] { new Vector2d(3.5, 1), new Vector2d(5, 1.75), new Vector2d(6.5, 1), new Vector2d(5, 0.5) });
            if (variant == 2) chainPts.Insert(1, new Vector2d(2, 2));
            if (variant == 3) pts.Add(new Vector2d(4, 0));
            var all = chainPts.Concat(pts).ToList();
            var k = new DelaunayKernel2(all.Select(p => p.x).ToArray(), all.Select(p => p.y).ToArray(), Enumerable.Range(0, all.Count).ToArray());
            Assert.True(k.Build(System.Threading.CancellationToken.None));
            k.BeginConstraints();
            int Id(Vector2d p) => Enumerable.Range(0, all.Count).Single(v => k.X[v] == p.x && k.Y[v] == p.y);
            var chain = chainPts.Select(Id).ToList();
            var sources = new List<int> { 7 };
            for (int i = 0; i + 1 < chain.Count; ++i) {
                int h = k.FindEdge(chain[i], chain[i + 1]);
                Assert.True(h >= 0, "the chain edge " + i + " is an edge of the triangulation");
                k.SetConstraint(h, k.NewConstraint(new List<int>(sources)));
            }
            k.Dirty = new List<int>();
            Assert.True(QualityMesh2.Refiner.MarkSwept(k, chain[0], chain[chain.Count - 1], sources), "chain found");
            var marked = new HashSet<int>(k.Dirty);
            // the polygon between the line u w and the chain, and the chain's convex hull
            var polygon = chainPts;
            bool InPolygon(Vector2d c)
            {
                bool inside = false;
                for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                    if ((polygon[i].y > c.y) != (polygon[j].y > c.y) && c.x < (polygon[j].x - polygon[i].x) * (c.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x) inside = !inside;
                return inside;
            }
            int region = 0;
            for (int t = 0; t < k.TriangleSlots; ++t) {
                if (k.IsGhost(t)) continue;
                var c = (k.Point(k.V[3 * t]) + k.Point(k.V[3 * t + 1]) + k.Point(k.V[3 * t + 2])) / 3;
                if (InPolygon(c)) { ++region; Assert.Contains(t, marked); }
            }
            Assert.True(region > 0, "a region: " + string.Join(" ", Enumerable.Range(0, k.TriangleSlots).Where(t => !k.IsGhost(t)).Select(t => k.V[3 * t] + "," + k.V[3 * t + 1] + "," + k.V[3 * t + 2])));
            foreach (int t in marked)
                for (int j = 0; j < 3; ++j) {
                    var x = k.Point(k.V[3 * t + j]);
                    Assert.True(x.y >= 0 && x.y <= chainPts.Max(p => p.y) && x.x >= 0 && x.x <= 10, "marked triangle " + t + " reaches outside the hull");
                }
            if (variant == 1)
                Assert.Contains(marked, t => Enumerable.Range(0, 3).All(j => !chain.Contains(k.V[3 * t + j])));
            // a chain that does not carry the sources is not found: the caller then marks everything
            Assert.False(QualityMesh2.Refiner.MarkSwept(k, chain[0], chain[chain.Count - 1], new List<int> { 99 }));
        }
    }
}
