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
    /// The grading levers of <see cref="QualityMesh2"/> (MESHING2D_GRADING.md) through their switches: the worst-first queue and the
    /// choice of the split point among candidates (in the petal of the shortest edge, or step 1's on its bisector), with its floor (no chosen point nearer to the mesh than the default
    /// point); on the snap grid; invariance, reuse and determinism.
    /// </summary>
    public class QualityMesh2GradingLibraryTests
    {
        static Mesh2Output Run(Mesh2Input input, bool? worstFirst = null, bool? best = null, Action<double, double, bool, Vector2d> picked = null)
        {
            QualityMesh2.Refiner.WorstFirstForBenchmarks = worstFirst;
            QualityMesh2.Refiner.BestCandidateForBenchmarks = best;
            QualityMesh2.Refiner.PickedForTests = picked;
            // (the reused angle tests of a choice checked against fresh ones, throws)
            QualityMesh2.Refiner.CheckScoresForTests = true;
            try { return new QualityMesh2Candidate().Run(input); }
            finally {
                QualityMesh2.Refiner.WorstFirstForBenchmarks = null; QualityMesh2.Refiner.BestCandidateForBenchmarks = null; QualityMesh2.Refiner.PickedForTests = null;
                QualityMesh2.Refiner.CheckScoresForTests = false;
            }
        }

        static OracleReport Judge(Mesh2Input input, Mesh2Output output) => Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });

        static bool Same(Mesh2Output a, Mesh2Output b) => a.VX.SequenceEqual(b.VX) && a.VY.SequenceEqual(b.VY) && a.Triangles.SelectMany(t => t).SequenceEqual(b.Triangles.SelectMany(t => t));

        static Mesh2Input Fuzz(int seed, double angle, bool constrainedQuality, double snap = 0)
        {
            var input = QualityMesh2LensDeletionTests.Fuzz(seed, angle, snap);
            input.ConstrainedQuality = constrainedQuality;
            return input;
        }

        /// <summary>
        /// each lever alone and both: every mesh passes the oracle, and each saves triangles summed over the seeds against neither (the
        /// defaults are both)
        /// </summary>
        [Theory]
        [InlineData(20.0, false)]
        [InlineData(25.0, true)]
        [InlineData(30.0, false)]
        public void Fuzz_EachLeverSavesTriangles_AndEveryMeshPasses(double angle, bool constrainedQuality)
        {
            var totals = new long[4];
            for (int seed = 0; seed < 24; ++seed) {
                var input = Fuzz(6000 + seed, angle, constrainedQuality);
                var variants = new[] { Run(input, false, false), Run(input, true, false), Run(input, false, true), Run(input) };
                for (int i = 0; i < 4; ++i) {
                    Assert.Equal(Mesh2Status.Success, variants[i].Status);
                    Assert.False(variants[i].SteinerLimitReached, "seed " + seed);
                    var report = Judge(input, variants[i]);
                    Assert.True(report.Ok, "seed " + seed + " variant " + i + ": " + report);
                    totals[i] += variants[i].Triangles.Length;
                }
                Assert.True(Same(variants[3], Run(input, true, true)), "the defaults are not both levers");
            }
            Assert.True(totals[1] < totals[0], "worst first: " + totals[1] + " against " + totals[0]);
            Assert.True(totals[2] < totals[0], "best candidate: " + totals[2] + " against " + totals[0]);
            Assert.True(totals[3] < totals[0], "both: " + totals[3] + " against " + totals[0]);
        }

        /// <summary>
        /// the petal's candidates (grading step 2, the default) against step 1's on the bisector: every mesh passes the oracle, in
        /// both modes and on the snap grid, and the petal saves triangles summed over the seeds
        /// </summary>
        [Theory]
        [InlineData(20.0, false, 0.0)]
        [InlineData(30.0, true, 0.0)]
        [InlineData(25.0, false, 0.25)]
        public void Petal_SavesTriangles_AndEveryMeshPasses(double angle, bool constrainedQuality, double snap)
        {
            long bisector = 0, petal = 0;
            for (int seed = 0; seed < 24; ++seed) {
                var input = Fuzz(6300 + seed, angle, constrainedQuality, snap);
                Mesh2Output withPetal = Run(input), onBisector;
                QualityMesh2.Refiner.PetalForBenchmarks = false;
                try { onBisector = Run(input); } finally { QualityMesh2.Refiner.PetalForBenchmarks = null; }
                foreach (var o in new[] { withPetal, onBisector }) {
                    Assert.Equal(Mesh2Status.Success, o.Status);
                    Assert.False(o.SteinerLimitReached, "seed " + seed);
                    var report = Judge(input, o);
                    Assert.True(report.Ok, "seed " + seed + ": " + report);
                }
                bisector += onBisector.Triangles.Length; petal += withPetal.Triangles.Length;
            }
            Assert.True(petal < bisector, "the petal: " + petal + " against " + bisector + " on the bisector");
        }

        /// <summary>
        /// the bound in Score (a candidate that removes fewer bad triangles than the best score so far is refused without its fan) is
        /// exact: with it switched off every mesh and every pick are the same, over the fuzz in both modes and on the grid (a bound
        /// that also refused an equal score would lose the tie to the larger radius: such ties occur in these seeds)
        /// </summary>
        [Theory]
        [InlineData(20.0, false, 0.0)]
        [InlineData(30.0, true, 0.0)]
        [InlineData(25.0, false, 0.25)]
        [InlineData(30.0, false, 0.0)]
        public void ScoreBound_LeavesEveryChoiceUnchanged(double angle, bool constrainedQuality, double snap)
        {
            for (int seed = 0; seed < 24; ++seed) {
                var input = Fuzz(6400 + seed, angle, constrainedQuality, snap);
                var withBound = new List<(double, double, bool, Vector2d)>();
                var o = Run(input, picked: (f, r, other, p) => withBound.Add((f, r, other, p)));
                var withoutBound = new List<(double, double, bool, Vector2d)>();
                QualityMesh2.Refiner.ScoreBoundOffForTests = true;
                Mesh2Output u;
                try { u = Run(input, picked: (f, r, other, p) => withoutBound.Add((f, r, other, p))); }
                finally { QualityMesh2.Refiner.ScoreBoundOffForTests = false; }
                Assert.True(Same(o, u), "seed " + seed + ": the meshes differ");
                Assert.Equal(withoutBound, withBound);
            }
        }

        /// <summary>
        /// the floor: a chosen point is never nearer to the mesh than the default point (its insertion radius at least the default's),
        /// a choice is made only where the default point would be inserted (its radius is positive), points other than the default
        /// are chosen, and the chosen points are the points inserted (on the grid already rounded); on the grid as well
        /// </summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(0.25)]
        public void Picks_KeepTheFloor(double snap)
        {
            int picks = 0, others = 0, inOutput = 0;
            for (int seed = 0; seed < 40; ++seed) {
                var input = Fuzz(6100 + seed, new[] { 20.0, 25, 30 }[seed % 3], seed % 2 == 0, snap);
                var chosen = new List<Vector2d>();
                var o = Run(input, picked: (floor, radius, other, p) => {
                    Assert.True(floor > 0, "the default point does not count: " + floor);
                    Assert.True(radius >= floor, radius + " below the floor " + floor);
                    ++picks; if (other) ++others;
                    chosen.Add(p);
                });
                Assert.Equal(Mesh2Status.Success, o.Status);
                // the point chosen is the point inserted (on the grid: scored where it is inserted, at its pixel centre, and refused
                // where a constraint crosses its pixel). Without lenses nothing is deleted, so every chosen point is in the output; in
                // the constrained quality mode lens deletion may remove some again
                var vertices = new HashSet<Vector2d>(Enumerable.Range(0, o.VX.Length).Select(i => new Vector2d(o.VX[i], o.VY[i])));
                int found = chosen.Count(vertices.Contains);
                if (!input.ConstrainedQuality) Assert.True(found == chosen.Count, "seed " + seed + ": " + (chosen.Count - found) + " chosen points not inserted");
                inOutput += found;
            }
            Assert.True(picks > 0 && others > 0, picks + " choices, " + others + " not the default point");
            Assert.True(2 * inOutput > picks, inOutput + " of " + picks + " chosen points in the output");
        }

        /// <summary>
        /// on the grid a candidate whose pixel a constraint crosses does not count (the insertion would refuse it and fall back to the
        /// circumcentre): every chosen point is inserted. Found by a search over 27 000 fuzz runs for a choice that is refused; this is
        /// the one input where it happens when the pixel check is left out
        /// </summary>
        [Theory]
        [InlineData(97, 0.25, 30.0)]
        public void Grid_NoChoiceInAConstrainedPixel(int seed, double snap, double angle)
        {
            var input = QualityMesh2LensDeletionTests.Fuzz(seed, angle, snap);
            input.ConstrainedQuality = false;
            var chosen = new List<Vector2d>();
            var o = Run(input, picked: (f, r, other, p) => chosen.Add(p));
            Assert.Equal(Mesh2Status.Success, o.Status);
            var vertices = new HashSet<Vector2d>(Enumerable.Range(0, o.VX.Length).Select(i => new Vector2d(o.VX[i], o.VY[i])));
            Assert.True(chosen.Count > 0);
            Assert.All(chosen, p => Assert.Contains(p, vertices));
            Assert.True(Judge(input, o).Ok);
        }

        /// <summary>
        /// on the snap grid: every mesh passes the oracle (every vertex of refinement on a pixel centre, no chains coinciding or pinched:
        /// the oracle's OffGrid, RefinementCoincidence and RefinementPinch), and the levers change meshes there
        /// </summary>
        [Fact]
        public void GridFuzz_PassesTheOracle()
        {
            int differ = 0;
            for (int seed = 0; seed < 40; ++seed) {
                var input = Fuzz(6200 + seed, new[] { 20.0, 25, 30 }[seed % 3], seed % 2 == 0, new[] { 0.25, 0.5, 0.3, 1.0 / 3, 0.125 }[seed % 5]);
                var o = Run(input);
                Assert.Equal(Mesh2Status.Success, o.Status);
                var report = Judge(input, o);
                Assert.True(report.Ok, "seed " + seed + ": " + report);
                if (!Same(o, Run(input, false, false))) ++differ;
            }
            Assert.True(differ > 0, "the levers changed no mesh on the grid");
        }

        /// <summary> the star polygon fuzz on the grid in both modes at 20 and 25 degrees: no seed rejected that is accepted without the levers </summary>
        [Fact]
        public void StarFuzz_NoNewRejections()
        {
            var worse = new List<int>();
            for (int seed = 0; seed < 300; ++seed) {
                var input = StarPolygonFuzz.Make(seed);
                input.ConstrainedQuality = seed % 3 == 0;
                input.MinAngleDeg = seed % 2 == 0 ? 20 : 25;
                var o = Run(input); var without = Run(input, false, false);
                bool ok = (o.Status == Mesh2Status.Success || o.Status == Mesh2Status.Empty) && Judge(input, o).Ok;
                bool okWithout = (without.Status == Mesh2Status.Success || without.Status == Mesh2Status.Empty) && Judge(input, without).Ok;
                if (okWithout && !ok) worse.Add(seed);
            }
            Assert.Empty(worse);
        }

        /// <summary> without an angle bound (size refinement only) neither lever is used: the meshes are those of the queue in order and the
        /// circumcentres (no triangle is below a bound of 0, so none is ordered or bad by its angle) </summary>
        [Fact]
        public void SizeOnly_Unchanged()
        {
            for (int seed = 0; seed < 12; ++seed) {
                var input = Fuzz(6300 + seed, 0, seed % 2 == 0);
                input.MinAngleDeg = 0; input.MaxArea = 0.5;
                var o = Run(input);
                Assert.Equal(Mesh2Status.Success, o.Status);
                Assert.True(o.Triangles.Length > 200, "seed " + seed + ": no size refinement");
                Assert.True(Same(o, Run(input, false, false)), "seed " + seed);
                Assert.True(Same(o, Run(input, true, true)), "seed " + seed);
            }
        }

        /// <summary> a candidate is rounded to the grid only when its pixel index fits (|coordinate| / W below 2^52, the bound of the exact
        /// circumcentre's pixel): the grid's rounding throws far beyond it (review of #375: a sliver's far bisector point) </summary>
        [Fact]
        public void RoundableOnGrid_GuardsTheRounding()
        {
            var grid = new SnapGrid(0.25);
            Assert.True(QualityMesh2.Refiner.RoundableOnGrid(grid, new Vector2d(1e6, -3)));
            Assert.True(QualityMesh2.Refiner.RoundableOnGrid(null, new Vector2d(1e30, 0)));
            Assert.False(QualityMesh2.Refiner.RoundableOnGrid(grid, new Vector2d(1e30, 0)));
            Assert.False(QualityMesh2.Refiner.RoundableOnGrid(grid, new Vector2d(0, -Math.Pow(2, 51))));   // 2^53 pixels of 1/4
            Assert.True(QualityMesh2.Refiner.RoundableOnGrid(grid, new Vector2d(0, Math.Pow(2, 49))));     // 2^51 pixels
            Assert.False(QualityMesh2.Refiner.RoundableOnGrid(grid, new Vector2d(double.NaN, 0)));
            Assert.False(QualityMesh2.Refiner.RoundableOnGrid(null, new Vector2d(2e70, 0)));
            Assert.ThrowsAny<Exception>(() => grid.Round(new Vector2d(1e30, 0)));   // what the guard keeps away
        }

        /// <summary> the benchmark counts chosen points apart from off-centres and circumcentres: as many as the choices of a point other
        /// than the default </summary>
        [Fact]
        public void Counts_ChosenPointsApart()
        {
            var input = Fuzz(6003, 25, false);
            var counts = new long[8];
            int others = 0;
            QualityMesh2.Refiner.CountsForBenchmarks = counts;
            try { Run(input, picked: (f, r, other, p) => { if (other) ++others; }); }
            finally { QualityMesh2.Refiner.CountsForBenchmarks = null; }
            Assert.True(others > 0);
            Assert.Equal(others, counts[7]);
        }

        // ------------------------------------------------------------ invariance, reuse, determinism

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        static Mesh2Input Scaled(Mesh2Input input, Func<double, double, Vector2d> map, double tolerance)
        {
            var moved = new Mesh2Input {
                X = (double[])input.X.Clone(), Y = (double[])input.Y.Clone(), MinAngleDeg = input.MinAngleDeg, SteinerLimit = input.SteinerLimit,
                ConstrainedQuality = input.ConstrainedQuality, SnapTolerance = tolerance,
            };
            moved.Rings.AddRange(input.Rings); moved.Segments.AddRange(input.Segments);
            for (int i = 0; i < input.Count; ++i) { var q = map(input.X[i], input.Y[i]); moved.X[i] = q.x; moved.Y[i] = q.y; }
            return moved;
        }

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Similarity_WithTheLevers(string name)
        {
            var s = Similarity.ByName(name);
            foreach (bool cq in new[] { false, true }) {
                var plain = Fuzz(6003, 25, cq);
                var moved = Scaled(plain, (x, y) => { var q = s.Apply(new Vector3d(x, y, 0)); return new Vector2d(q.x, q.y); }, 0);
                var b = Run(moved);
                var report = Judge(moved, b);
                Assert.True(report.Ok, name + ": " + report);
                if (s.IsExact) {
                    int others = 0;
                    var a = Run(plain, picked: (f, r, other, p) => { if (other) ++others; });
                    Assert.True(others > 0, "no point other than the default chosen");
                    Assert.Equal(a.VX.Select(v => v * s.Scale), b.VX);
                    Assert.Equal(a.VY.Select(v => v * s.Scale), b.VY);
                    Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
                }
            }
        }

        /// <summary> scaled by 2^-500 and 2^500, with and without the grid: the same mesh bit for bit (the scores are computed in the
        /// triangle's frame scaled to unit size, so they neither underflow nor overflow) </summary>
        [Theory]
        [InlineData(-500, 0.0, false)]
        [InlineData(200, 0.0, false)]
        [InlineData(-500, 0.25, false)]
        [InlineData(200, 0.25, false)]
        [InlineData(-500, 0.0, true)]
        [InlineData(200, 0.25, true)]
        public void PowerOfTwoScale_IsBitIdentical(int exponent, double snap, bool constrainedQuality)
        {
            double f = Math.Pow(2, exponent);
            var plain = Fuzz(6004, 25, constrainedQuality, snap);
            var moved = Scaled(plain, (x, y) => new Vector2d(x * f, y * f), snap * f);
            var a = Run(plain); var b = Run(moved);
            Assert.Equal(Mesh2Status.Success, a.Status);
            Assert.Equal(a.VX.Select(v => v * f), b.VX);
            Assert.Equal(a.VY.Select(v => v * f), b.VY);
            Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
        }

        [Fact]
        public void Reuse_And_Parallel_RunsAreIdentical()
        {
            var mesher = new QualityMesh2 { MinAngle = 25, Conforming = false };
            var input = ConstrainedDelaunayMesh2Candidate.Convert(Fuzz(6003, 25, true));
            var first = mesher.Compute(input).Mesh;
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
