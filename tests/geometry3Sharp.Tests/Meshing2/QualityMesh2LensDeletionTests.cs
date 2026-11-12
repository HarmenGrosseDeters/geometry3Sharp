using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Chew's deletion with diametral lenses (Chew 1993; Shewchuk 2002, section 6.4, "Terminator Chew"): before a subsegment is bisected,
    /// the free vertices (made by refinement, on no constraint) in its open diametral circle that see its midpoint are deleted.
    /// </summary>
    public class QualityMesh2LensDeletionTests
    {
        static Mesh2Output Run(Mesh2Input input, bool? deletion = null, bool check = false)
        {
            QualityMesh2.Refiner.DeleteInCircleForBenchmarks = deletion;
            // (the checks: the tags after every operation against domain membership from scratch, and every deletion pass against a
            // brute force over all triangles; both throw)
            QualityMesh2.Refiner.CheckTagsForTests = check; QualityMesh2.Refiner.CheckDeletionForTests = check;
            try { return new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.DeleteInCircleForBenchmarks = null; QualityMesh2.Refiner.CheckTagsForTests = false; QualityMesh2.Refiner.CheckDeletionForTests = false; }
        }

        static long Deleted(Func<Mesh2Output> run, out Mesh2Output output)
        {
            long before = QualityMesh2.Refiner.DeletedForTests;
            output = run();
            return QualityMesh2.Refiner.DeletedForTests - before;
        }

        static OracleReport Judge(Mesh2Input input, Mesh2Output output) => Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });

        static bool Same(Mesh2Output a, Mesh2Output b) => a.VX.SequenceEqual(b.VX) && a.VY.SequenceEqual(b.VY) && a.Triangles.SelectMany(t => t).SequenceEqual(b.Triangles.SelectMany(t => t));

        // ------------------------------------------------------------ the predicate

        /// <summary>
        /// the subsegment (0, 0)-(8, 0) and the constrained edge (2, 1)-(6, 1) above it: a vertex is deleted only in the open diametral
        /// circle (not only the lens) and where the segment to the midpoint (4, 0) meets no constrained edge, closed
        /// </summary>
        [Theory]
        [InlineData(1, -2, true)]       // below, in the circle
        [InlineData(7, 1.2, true)]      // above, past the blocking edge's end
        [InlineData(7.9, 0.5, true)]    // in the circle but outside the lens
        [InlineData(4, 2, false)]       // behind the blocking edge
        [InlineData(7, 1.5, false)]     // its sight line touches the blocking edge's end (6, 1)
        [InlineData(4, 4, false)]       // on the circle (open)
        [InlineData(4, 5, false)]       // outside
        public void InCircleAndVisible(double x, double y, bool expected)
        {
            var blockers = new List<(Vector2d, Vector2d)> { (new Vector2d(2, 1), new Vector2d(6, 1)) };
            Assert.Equal(expected, QualityMesh2.Refiner.InCircleAndVisible(new Vector2d(0, 0), new Vector2d(8, 0), new Vector2d(x, y), blockers));
            Assert.Equal(expected, QualityMesh2.Refiner.InCircleAndVisible(new Vector2d(8, 0), new Vector2d(0, 0), new Vector2d(x, y), blockers));
        }

        [Fact]
        public void SegmentsMeet_ClosedAndCollinear()
        {
            Assert.True(QualityMesh2.Refiner.SegmentsMeet(new Vector2d(0, 0), new Vector2d(2, 2), new Vector2d(0, 2), new Vector2d(2, 0)));
            Assert.True(QualityMesh2.Refiner.SegmentsMeet(new Vector2d(0, 0), new Vector2d(2, 2), new Vector2d(2, 2), new Vector2d(3, 0)));   // an end
            Assert.True(QualityMesh2.Refiner.SegmentsMeet(new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(1, 0), new Vector2d(3, 0)));   // overlap
            Assert.False(QualityMesh2.Refiner.SegmentsMeet(new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(2, 0), new Vector2d(3, 0)));  // collinear apart
            Assert.False(QualityMesh2.Refiner.SegmentsMeet(new Vector2d(0, 0), new Vector2d(2, 2), new Vector2d(0, 3), new Vector2d(3, 3)));
        }

        // ------------------------------------------------------------ the meshes

        /// <summary> a square with a triangular hole and random segments (the lens tests' fuzz), in the constrained quality mode </summary>
        internal static Mesh2Input Fuzz(int seed, double angle, double snap = 0, bool region = false)
        {
            var r = new Random(seed);
            var x = new List<double> { 0, 16, 16, 0, 6, 9, 7 }; var y = new List<double> { 0, 0, 16, 16, 6, 6.5, 9 };
            var input = new Mesh2Input();
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            if (!(snap > 0)) input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
            int m = 3 + r.Next(6);
            for (int i = 0; i < m; ++i) {
                for (int k = 0; k < 2; ++k) {
                    double px, py;
                    do { px = 1 + Math.Round(r.NextDouble() * 14 * 64) / 64; py = 1 + Math.Round(r.NextDouble() * 14 * 64) / 64; }
                    while (px > 5 && px < 10 && py > 5 && py < 10);
                    x.Add(px); y.Add(py);
                }
                input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
            }
            // a region of one small triangle among the segments, its own label (a tag that only constraints enclose)
            if (region) { x.AddRange(new[] { 12.0, 12.75, 12.25 }); y.AddRange(new[] { 2.0, 2.0, 2.625 }); input.Regions.Add(new RegionInput { Ring = new[] { x.Count - 3, x.Count - 2, x.Count - 1 }, Label = 1 }); }
            input.X = x.ToArray(); input.Y = y.ToArray();
            input.MinAngleDeg = angle; input.SteinerLimit = 50000; input.ConstrainedQuality = true;
            if (snap > 0) input.SnapTolerance = snap;
            return input;
        }

        /// <summary>
        /// deletions happen, every mesh passes the oracle (constrained Delaunay after them, the angle bound, the domain), and with the
        /// checks on: the tags after every operation, and every deletion pass against a brute force (no free vertex left in the circle
        /// that sees the midpoint, none deleted that does not)
        /// </summary>
        [Theory]
        [InlineData(20.0, false)]
        [InlineData(25.0, false)]
        [InlineData(25.65, false)]
        [InlineData(20.0, true)]
        [InlineData(25.0, true)]
        public void Fuzz_DeletesAndPassesTheOracle(double angle, bool region)
        {
            long deleted = 0; int differ = 0;
            for (int seed = 0; seed < 40; ++seed) {
                var input = Fuzz(3000 + seed, angle, 0, region);
                deleted += Deleted(() => Run(input, null, true), out var o);
                Assert.Equal(Mesh2Status.Success, o.Status);
                Assert.False(o.SteinerLimitReached, "seed " + seed);
                var report = Judge(input, o);
                Assert.True(report.Ok, "seed " + seed + ": " + report);
                if (!Same(o, Run(input, false))) ++differ;
            }
            Assert.True(deleted > 0, "no vertex deleted");
            Assert.True(differ > 0, "the deletion changed no mesh");
        }

        /// <summary> no deletion without lenses: above their bound, and with conforming (circles) </summary>
        [Theory]
        [InlineData(30.0, true)]
        [InlineData(20.0, false)]
        public void NoLenses_NoDeletion(double angle, bool constrained)
        {
            for (int seed = 0; seed < 10; ++seed) {
                var input = Fuzz(3000 + seed, angle);
                input.ConstrainedQuality = constrained;
                Assert.Equal(0, Deleted(() => Run(input), out _));
            }
        }

        /// <summary>
        /// on the snap grid: deletions happen, the meshes pass the oracle, and the chains stay apart (the oracle's O-H5 and the rule of
        /// #360: a deleted vertex is on no constraint, so no chain loses or gains a vertex by it)
        /// </summary>
        [Fact]
        public void GridFuzz_DeletesAndPassesTheOracle()
        {
            long deleted = 0;
            for (int seed = 0; seed < 40; ++seed) {
                var input = Fuzz(4000 + seed, new[] { 20.0, 25 }[seed % 2], new[] { 0.25, 0.5, 0.3, 1.0 / 3, 0.125 }[seed % 5]);
                deleted += Deleted(() => Run(input, null, true), out var o);
                Assert.Equal(Mesh2Status.Success, o.Status);
                var report = Judge(input, o);
                Assert.True(report.Ok, "seed " + seed + ": " + report);
            }
            Assert.True(deleted > 0, "no vertex deleted on the grid");
        }

        /// <summary>
        /// star polygons on the grid (the chain-coincidence fuzz of #360) in the constrained quality mode at 20 and 25 degrees: every
        /// verdict is the one without the deletion, and every mesh is accepted (seeds 37 and 41 were rejected until #372: their only ring
        /// collapses, and the domain became the hull; it is empty now, and empty outputs are judged too)
        /// </summary>
        [Fact]
        public void StarFuzz_ConstrainedQuality_VerdictsAsWithoutDeletion()
        {
            long deleted = 0;
            var rejected = new List<int>();
            for (int seed = 0; seed < 300; ++seed) {
                var input = StarPolygonFuzz.Make(seed);
                input.ConstrainedQuality = true;
                input.MinAngleDeg = seed % 2 == 0 ? 20 : 25;
                deleted += Deleted(() => Run(input), out var o);
                var without = Run(input, false);
                Assert.Equal(without.Status, o.Status);
                if (o.Status != Mesh2Status.Success && o.Status != Mesh2Status.Empty) continue;
                var report = Judge(input, o);
                var reportWithout = Judge(input, without);
                Assert.Equal(reportWithout.Violations.Select(v => v.Kind).Distinct().OrderBy(x => x), report.Violations.Select(v => v.Kind).Distinct().OrderBy(x => x));
                if (!report.Ok) rejected.Add(seed);
            }
            Assert.Empty(rejected);
            Assert.True(deleted > 0, "no vertex deleted");
        }

        // ------------------------------------------------------------ invariance, reuse, determinism

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Similarity_WithDeletions(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Fuzz(3003, 25);
            var moved = Fuzz(3003, 25);
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            Deleted(() => Run(moved), out var b);
            var report = Judge(moved, b);
            Assert.True(report.Ok, name + ": " + report);
            if (s.IsExact) {
                Assert.True(Deleted(() => Run(plain), out var a) > 0, "the plain input deletes nothing");
                Assert.Equal(a.VX.Select(v => v * s.Scale), b.VX);
                Assert.Equal(a.VY.Select(v => v * s.Scale), b.VY);
                Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
            }
        }

        [Fact]
        public void Reuse_And_Parallel_RunsAreIdentical()
        {
            var mesher = new QualityMesh2 { MinAngle = 25, Conforming = false };
            var input = ConstrainedDelaunayMesh2Candidate.Convert(Fuzz(3003, 25));
            long before = QualityMesh2.Refiner.DeletedForTests;
            var first = mesher.Compute(input).Mesh;
            Assert.True(QualityMesh2.Refiner.DeletedForTests > before, "nothing deleted");
            var results = new ConstrainedMesh2[8];
            Parallel.For(0, results.Length, i => results[i] = mesher.Compute(input).Mesh);
            foreach (var m in results.Concat(new[] { mesher.Compute(input).Mesh })) {
                Assert.Equal(first.Vertices, m.Vertices);
                Assert.Equal(first.Triangles, m.Triangles);
                Assert.Equal(first.BadTriangles, m.BadTriangles);
            }
        }
    }
}
