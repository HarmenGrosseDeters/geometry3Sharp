// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The local alpha (docs/design/TET_MESHING.md section 17.13): MeshAlphaWrap.AlphaField and MeshTetrahedralization.MaxSurfaceDeviation.
    /// Both default off, and then the output is as before (ExistingResult_IsBitForBitTheSameAsBeforeP2 pins the default). The wrap's promises that do not read alpha
    /// (closed, manifold, no self-intersection, the input inside) are re-checked with a field.
    /// </summary>
    public class TetMeshLocalAlphaTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
            new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5 * (x0 + x1), 0.5 * (y0 + y1), 0.5 * (z0 + z1)), new Vector3d(0.5 * (x1 - x0), 0.5 * (y1 - y0), 0.5 * (z1 - z0))) }.Generate().MakeDMesh();

        /// <summary> a base plate with four teeth: gaps of 0.05 between the teeth (a little more than 1 alpha at diagonal / 20), exact union </summary>
        // the comb and the reference run are shared by the tests (deterministic, read only): the union and the run are computed once
        static readonly Lazy<DMesh3> CombShared = new Lazy<DMesh3>(MakeComb);
        internal static DMesh3 Comb() => CombShared.Value;
        static readonly Lazy<MeshTetrahedralization> Ref150 = new Lazy<MeshTetrahedralization>(() => { var tm = Local(Comb()); Run(tm); return tm; });

        static DMesh3 MakeComb()
        {
            var parts = new List<DMesh3> { Box(0, 0, 0, 0.6, 0.12, 0.12) };
            for (int i = 0; i < 4; ++i) parts.Add(Box(0.01 + i * 0.15, 0.08, 0, 0.01 + i * 0.15 + 0.10, 0.5, 0.12));
            return ExactMeshBoolean.Union(parts);
        }

        // the options of the tests: a coarse base alpha keeps the runs short (the comb's gaps are bridged at it)
        const double BaseAlpha = 20;
        static MeshTetrahedralization Uniform(DMesh3 m, double alpha = BaseAlpha) => new MeshTetrahedralization(m) { RelativeAlpha = alpha };
        static MeshTetrahedralization Local(DMesh3 m, double devFraction = 150, int passes = 1) =>
            new MeshTetrahedralization(m) { RelativeAlpha = BaseAlpha, MaxSurfaceDeviation = m.CachedBounds.DiagonalLength / devFraction, LocalAlphaPasses = passes };

        // the largest distance of area-weighted input samples to the boundary of a result
        static double MaxDeviation(MeshTetrahedralization tm)
        {
            var tree = new DMeshAABBTree3(tm.Result.ToSurfaceMesh(), true);
            double max = 0;
            foreach (Vector3d p in AlphaSizeField.Samples(tm.Mesh, 4000)) { tree.FindNearestTriangle(p, out double d2); max = Math.Max(max, Math.Sqrt(d2)); }
            return max;
        }

        static void Run(MeshTetrahedralization tm) { Assert.True(tm.Compute(), tm.Status.ToString()); Assert.Equal(MeshTetrahedralization.TetStatus.Success, tm.Status); }

        // ------------------------------------------------------------------------------------------------ off by default

        [Fact]
        public void Default_IsOff_AndSwitchedOffValuesGiveTheDefaultResult()
        {
            Assert.Equal(0.0, new MeshTetrahedralization(new DMesh3()).MaxSurfaceDeviation);
            Assert.Equal(1, new MeshTetrahedralization(new DMesh3()).LocalAlphaPasses);
            Assert.Null(new MeshAlphaWrap(new DMesh3()).AlphaField);
            DMesh3 input = Comb();
            var def = Uniform(input); Run(def);
            foreach (double off in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity }) {
                var tm = Uniform(input); tm.MaxSurfaceDeviation = off; Run(tm);
                Assert.Equal(def.Result.Vertices, tm.Result.Vertices);
                Assert.Equal(def.Result.Tets, tm.Result.Tets);
                Assert.Equal(def.Result.BoundaryTriangles, tm.Result.BoundaryTriangles);
                Assert.Equal(def.Counts, tm.Counts);
            }
            Assert.Equal(new MeshTetrahedralization(input).SurfaceMinAngleDegrees, def.Wrap.RefineMinAngleDegrees);   // the default keeps its surface refinement
            Assert.False(def.Counts.ContainsKey("local_alpha_passes"));
            Assert.False(def.Counts.ContainsKey("local_alpha_fine_samples"));
        }

        [Fact]
        public void InputWithoutTriangles_IgnoresTheOption()
        {
            var pts = new DMesh3();
            var rnd = new Random(5);
            for (int i = 0; i < 60; ++i) pts.AppendVertex(new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()));
            var a = Uniform(pts); Run(a);
            var b = Uniform(pts); b.MaxSurfaceDeviation = 0.01; Run(b);
            Assert.Equal(a.Result.Tets, b.Result.Tets);
            Assert.Equal(a.Counts, b.Counts);
        }

        // ------------------------------------------------------------------------------------------------ what it does

        [Fact]
        public void Deviation_IsAsLowAsAUniformAlphaThreeTimesFiner_WithFewerTets()
        {
            // measured (comb, alpha / 20): uniform alpha / 20 .. / 60: distance 0.078 .. 0.0146 of the diagonal at 4.7k .. 17k tets; local at diagonal / 100: 0.0141 at 9.9k
            DMesh3 input = Comb();
            var uni = Uniform(input); Run(uni);
            var loc = Local(input, 100); Run(loc);
            Assert.True(loc.Counts["local_alpha_fine_samples"] > 0);
            double du = MaxDeviation(uni), dl = MaxDeviation(loc);
            Assert.True(dl < 0.3 * du, "the local alpha did not follow the input closer: " + dl + " against " + du);
            var fine = Uniform(input, 3 * BaseAlpha); Run(fine);
            // (bound 1.11: 1.103 times since #1464 changed the ids of the boolean's result that the input is built with; 1.1 at most before)
            Assert.True(dl < 1.11 * MaxDeviation(fine), "the local alpha is farther than the uniform alpha / 3: " + dl + " against " + MaxDeviation(fine));
            Assert.True(loc.Result.Tets.Count < 0.75 * fine.Result.Tets.Count, loc.Result.Tets.Count + " tets against " + fine.Result.Tets.Count);
        }

        [Fact]
        public void ResultIsValid_TheInputInside_AndTheFloorIsMet()
        {
            DMesh3 input = Comb();
            {          // (one pass: the identity case of the similarity test below checks the same invariants)
                int passes = 2;
                var tm = Local(input, 150, passes); Run(tm);
                Assert.Equal(0.0, tm.Wrap.RefineMinAngleDegrees);       // the wrap's surface angle refinement is not used with a local alpha (17.13)
                TetMeshingStage2Tests.Invariants(tm);
                Assert.True(tm.Targets.MinDihedralMet, "worst dihedral " + tm.Targets.WorstMinDihedral);
                Assert.Equal(passes, tm.Counts["local_alpha_passes"]);
            }
        }

        // a dihedral target that one pass of stage 2 does not reach (20 degrees): the field is smoothed once, the target is still reported as missed
        static MeshTetrahedralization Hard(DMesh3 input) { var tm = Local(input); tm.SliverDihedralDegrees = 20; tm.ImprovePasses = 1; tm.RescuePasses = 0; return tm; }

        [Fact]
        [Trait("Category", "Slow")]       // two full runs of the retry (the second with half the slope), 4 to 6 s
        public void MissedDihedralTarget_RunsOnceMoreWithASmootherField_AndKeepsTheBetterResult()
        {
            DMesh3 input = Comb();
            var met = Ref150.Value;
            Assert.True(met.Targets.MinDihedralMet);
            Assert.False(met.Counts.ContainsKey("local_alpha_retry"));            // the target is met: no second run
            Assert.Equal(0.0, met.GradationRetry);
            var hard = Hard(input);
            hard.Compute(); Assert.NotNull(hard.Result);
            Assert.Equal(1, hard.Counts["local_alpha_retry"]);
            Assert.False(hard.Targets.MinDihedralMet);
            Assert.Null(TetMeshQuality.NotValidReason(hard.Result));
            // the second run used a different, smoother field: half the slope
            Assert.Equal(0.25, hard.GradationFirst);
            Assert.Equal(0.125, hard.GradationRetry);
            // the kept result is the better one: kept <=> the smoother field gave a larger smallest angle than the first run
            bool kept = hard.Counts.ContainsKey("local_alpha_retry_kept");
            if (kept) Assert.True(hard.Targets.WorstMinDihedral > hard.RetryFirstWorstMinDihedral);
            else Assert.Equal(hard.RetryFirstWorstMinDihedral, hard.Targets.WorstMinDihedral);
            Assert.False(double.IsNaN(hard.RetryFirstWorstMinDihedral));
            // the result, counts and targets belong to each other: the targets are those of the result
            var check = TetMeshQuality.CheckTargets(hard.Result, 20, hard.MaxRadiusEdgeRatio);
            Assert.Equal(check.WorstMinDihedral, hard.Targets.WorstMinDihedral);
            // reuse with the retry: the same instance again gives the same result and counts, and the options are reset
            var verts = hard.Result.Vertices.ToList(); var tets = hard.Result.Tets.ToList(); var counts = new Dictionary<string, long>(hard.Counts);
            hard.Compute();
            Assert.Equal(verts, hard.Result.Vertices); Assert.Equal(tets, hard.Result.Tets); Assert.Equal(counts, hard.Counts);
            Assert.Equal(0.25, hard.GradationFirst); Assert.Equal(0.125, hard.GradationRetry);
        }

        [Fact]
        public void CancelledDuringTheRetry_IsCancelled_NotTheFirstResult()
        {
            // the retry starts when Compute() resets Result after the first run's last check saw it set
            MeshTetrahedralization tm = Local(Comb());
            tm.SliverDihedralDegrees = 20; tm.ImprovePasses = 1; tm.RescuePasses = 0;
            bool sawResult = false;
            tm.CancelF = () => { if (tm.Result != null) sawResult = true; return sawResult && tm.Result == null; };
            Assert.False(tm.Compute());
            Assert.True(sawResult);
            Assert.Equal(MeshTetrahedralization.TetStatus.Cancelled, tm.Status);
            Assert.Null(tm.Result);
        }

        [Fact]
        public void SphereAndBox_WhichHaveNoDetail_AreNotRefined_AndNoSecondWrapRuns()
        {
            // the tolerance is not below the distance of the uniform wrap (about 1 offset): no sample is farther, no field, the second wrap is not run
            foreach (string name in new[] { "sphere", "box" }) {
                DMesh3 input = TetMeshingStage2Tests.Input(name);
                var tm = new MeshTetrahedralization(input) { RelativeAlpha = 12, MaxSurfaceDeviation = input.CachedBounds.DiagonalLength / 100 }; Run(tm);
                Assert.Equal(0, tm.Counts["local_alpha_fine_samples"]);
                Assert.Equal(0, tm.Counts["local_alpha_passes"]);               // no refined wrap ran
                Assert.Null(tm.Wrap.AlphaField);                                // the wrap of the tets is the uniform one
                Assert.Equal(MeshAlphaWrap.WrapStatus.Success, tm.WrapStatus);
            }
        }

        [Fact]
        public void FailedLocalWrap_KeepsTheLastSuccessfulWrap_AndSaysSo()
        {
            DMesh3 input = Comb();
            var tm = Local(input); tm.LocalWrapMaxSteinerPoints = 30;     // the refined wrap needs many more points: its point limit is reached
            Run(tm);                                                      // the run still succeeds, as it does without the option
            Assert.Equal(1, tm.Counts["local_alpha_wrap_failed"]);
            Assert.Equal((long)MeshAlphaWrap.WrapStatus.PointLimitReached, tm.Counts["local_alpha_wrap_failed_status"]);
            Assert.Equal(0, tm.Counts["local_alpha_passes"]);
            Assert.Null(tm.Wrap.AlphaField);                              // the uniform wrap is the one of the result
            Assert.Null(TetMeshQuality.NotValidReason(tm.Result));
            Assert.False(Ref150.Value.Counts.ContainsKey("local_alpha_wrap_failed"));
            // the same result as the uniform wrap (without the surface angle refinement, which the option switches off)
            var uni = Uniform(input); uni.SurfaceMinAngleDegrees = 0; Run(uni);
            Assert.Equal(uni.Result.Tets, tm.Result.Tets);
        }

        [Fact]
        public void ToleranceBelowTwoOffsets_IsRaisedToTwoOffsets()
        {
            DMesh3 input = Comb();
            var tiny = new MeshTetrahedralization(input) { RelativeAlpha = BaseAlpha, MaxSurfaceDeviation = 1e-9 }; Run(tiny);
            var two = new MeshTetrahedralization(input) { RelativeAlpha = BaseAlpha, MaxSurfaceDeviation = 2 * input.CachedBounds.DiagonalLength / 600 }; Run(two);
            Assert.Equal(two.Result.Tets, tiny.Result.Tets);
            Assert.Equal(two.Counts, tiny.Counts);
        }

        [Fact]
        [Trait("Category", "Slow")]       // three full runs, 3 to 5 s
        public void MorePasses_NeverMakeTheDeviationLarger_AndTheCountsSaySo()
        {
            DMesh3 input = Comb();
            var one = Ref150.Value;
            var two = Local(input, 150, 2); Run(two);
            Assert.True(MaxDeviation(two) <= MaxDeviation(one) * 1.05, MaxDeviation(two) + " against " + MaxDeviation(one));
            Assert.True(two.Counts["local_alpha_fine_samples"] >= one.Counts["local_alpha_fine_samples"]);
            Assert.Equal(1, one.Counts["local_alpha_passes"]);
            Assert.Equal(2, two.Counts["local_alpha_passes"]);       // the executed passes
            Assert.NotEqual(one.Result.Tets, two.Result.Tets);       // the second pass changed the field
            var outOfRange = Local(input, 150, 4); Run(outOfRange);
            Assert.Equal(one.Result.Tets, outOfRange.Result.Tets);   // out of range counts as 1
        }

        // ------------------------------------------------------------------------------------------------ determinism, reuse, invariance

        [Fact]
        public void EveryMaxThreads_AndRepeatedRuns_GiveTheSameResult()
        {
            DMesh3 input = Comb();
            var reference = Ref150.Value;                      // (the default threads)
            foreach (int threads in new[] { 1, 3 }) {
                var tm = Local(input); tm.MaxThreads = threads; Run(tm);
                Assert.Equal(reference.Result.Vertices, tm.Result.Vertices);
                Assert.Equal(reference.Result.Tets, tm.Result.Tets);
                Assert.Equal(reference.Result.BoundaryTriangles, tm.Result.BoundaryTriangles);
                Assert.Equal(reference.Counts, tm.Counts);
            }
        }

        [Fact]
        public void Compute_OnTheSameInstanceTwice_GivesTheSameResult()
        {
            var tm = Local(Comb()); Run(tm);
            var verts = tm.Result.Vertices.ToList(); var tets = tm.Result.Tets.ToList(); var counts = new Dictionary<string, long>(tm.Counts);
            Run(tm);
            Assert.Equal(verts, tm.Result.Vertices);
            Assert.Equal(tets, tm.Result.Tets);
            Assert.Equal(counts, tm.Counts);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Similarity_ValidAndPow2ScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = Comb();
            var a = Ref150.Value;
            DMesh3 moved = Invariance.Transformed(input, s);
            var b = new MeshTetrahedralization(moved) { RelativeAlpha = BaseAlpha, MaxSurfaceDeviation = s.ApplyLength(a.MaxSurfaceDeviation) };
            if (!s.IsExact) {
                // the relative alpha and offset are fractions of the bounding box diagonal, which a rotation changes: give the moved input the same absolute alpha and offset
                double ratio = moved.CachedBounds.DiagonalLength / (s.Scale * input.CachedBounds.DiagonalLength);
                b.RelativeAlpha = a.RelativeAlpha * ratio; b.RelativeOffset = a.RelativeOffset * ratio;
            }
            Run(b);
            TetMeshingStage2Tests.Invariants(b);
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            } else {
                // not bit for bit (the rounding of the moved coordinates and the grid differ), but the same refinement: the fine samples and the tets
                // within 25 %, the distance (in the input's units, scaled back) within a factor of 1.6
                double fa = a.Counts["local_alpha_fine_samples"], fb = b.Counts["local_alpha_fine_samples"];
                Assert.InRange(fb, 0.8 * fa, 1.25 * fa);
                double da = MaxDeviation(a), db = MaxDeviation(b) / s.Scale;
                Assert.InRange(db, da / 1.6, da * 1.6);
                Assert.InRange((double)b.Result.Tets.Count, 0.8 * a.Result.Tets.Count, 1.25 * a.Result.Tets.Count);
                Assert.True(b.Targets.MinDihedralMet);
            }
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        // ------------------------------------------------------------------------------------------------ cancellation

        [Fact]
        public void Cancelled_InTheLocalAlphaPasses_IsReportedAsCancelled()
        {
            DMesh3 input = Comb();
            // tm.Wrap is set when the first (uniform) wrap has run: cancelled from then on, so in the passes
            MeshTetrahedralization tm = null;
            tm = Local(input);
            tm.CancelF = () => tm.Wrap != null;
            Assert.False(tm.Compute());
            Assert.Equal(MeshTetrahedralization.TetStatus.Cancelled, tm.Status);
            Assert.Null(tm.Result);
        }

        // ------------------------------------------------------------------------------------------------ the wrap with a field

        static MeshAlphaWrap Wrap(DMesh3 input, Func<Vector3d, double> field, double ra = 12)
        {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = ra, RelativeOffset = 300, AlphaField = field };
            Assert.True(w.Compute(), w.Status + " " + w.Error);
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, w.Status);
            return w;
        }

        static void WrapPromises(MeshAlphaWrap w, DMesh3 input)
        {
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result, true));
            Assert.True(w.Result.IsClosed());
            Assert.False(MeshRepairMetrics.HasSelfIntersections(w.Result));
            Assert.Equal(0, AlphaWrapNoContactTests.Crossings(w.Result, input));
            var tree = new DMeshAABBTree3(input, true);
            foreach (int v in w.Result.VertexIndices()) {          // the vertices keep the offset from the input
                tree.FindNearestTriangle(w.Result.GetVertex(v), out double d2);
                Assert.True(Math.Sqrt(d2) >= 0.99 * w.UsedOffset);
            }
            foreach (int v in input.VertexIndices())               // the input is inside: the winding number of the result at its vertices is 1
                Assert.True(WindingInside(w.Result, input.GetVertex(v)));
        }

        static bool WindingInside(DMesh3 closed, Vector3d p)
        {
            double sum = 0;
            foreach (int t in closed.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero; closed.GetTriVertices(t, ref a, ref b, ref c);
                Vector3d A = a - p, B = b - p, C = c - p;
                double la = A.Length, lb = B.Length, lc = C.Length;
                sum += 2 * Math.Atan2(A.Dot(B.Cross(C)), la * lb * lc + A.Dot(B) * lc + B.Dot(C) * la + C.Dot(A) * lb);
            }
            return Math.Abs(sum / (4 * Math.PI) - 1) < 0.1;
        }

        [Fact]
        public void WrapWithAField_KeepsItsPromises_OnTheCombAndASphere()
        {
            foreach (DMesh3 input in new[] { Comb(), TetMeshingStage2Tests.Input("sphere") }) {
                double diag = input.CachedBounds.DiagonalLength, alpha = diag / 12;
                // a field that is fine in one half of the space and coarse in the other, with a slope of 0.25
                Func<Vector3d, double> field = p => Math.Max(alpha / 4, alpha / 4 + 0.25 * (p.x - input.CachedBounds.Center.x + 0.02 * diag) * 2);
                MeshAlphaWrap w = Wrap(input, field);
                WrapPromises(w, input);
                Assert.True(w.Result.TriangleCount > Wrap(input, null).Result.TriangleCount, "a finer alpha somewhere gives more triangles");
            }
        }

        [Fact]
        public void AField_AtLeastTheUniformAlpha_NaN_OrBelowTheFloor_IsClampedAsDocumented()
        {
            DMesh3 input = Comb();
            var uniform = Wrap(input, null);
            foreach (Func<Vector3d, double> f in new Func<Vector3d, double>[] { p => 1e9, p => double.NaN, p => double.PositiveInfinity, p => uniform.UsedAlpha }) {
                var w = Wrap(input, f);
                Assert.Equal(uniform.Result.TriangleCount, w.Result.TriangleCount);
                for (int v = 0; v < uniform.Result.MaxVertexID; ++v) Assert.Equal(uniform.Result.GetVertex(v), w.Result.GetVertex(v));
            }
            // below the floor (3 offsets) it is the floor, whatever the value
            var a = Wrap(input, p => 1e-12); var b = Wrap(input, p => 3 * uniform.UsedOffset);
            Assert.Equal(a.Result.TriangleCount, b.Result.TriangleCount);
            for (int v = 0; v < a.Result.MaxVertexID; ++v) Assert.Equal(a.Result.GetVertex(v), b.Result.GetVertex(v));
            WrapPromises(a, input);
        }

        [Fact]
        public void WrapField_EveryMaxThreads_GivesTheSameResult()
        {
            DMesh3 input = Comb();
            double alpha = input.CachedBounds.DiagonalLength / 12;
            Func<Vector3d, double> field = p => p.y > 0.3 ? alpha : alpha / 3;
            var serial = new MeshAlphaWrap(input) { RelativeAlpha = 12, RelativeOffset = 300, AlphaField = field, MaxThreads = 1 }; Assert.True(serial.Compute());
            foreach (int t in new[] { 0, 3 }) {
                var w = new MeshAlphaWrap(input) { RelativeAlpha = 12, RelativeOffset = 300, AlphaField = field, MaxThreads = t }; Assert.True(w.Compute());
                Assert.Equal(serial.Result.TriangleCount, w.Result.TriangleCount);
                for (int v = 0; v < serial.Result.MaxVertexID; ++v) Assert.Equal(serial.Result.GetVertex(v), w.Result.GetVertex(v));
            }
        }

        // ------------------------------------------------------------------------------------------------ the field

        [Fact]
        public void SizeField_IsTheGradedEnvelope()
        {
            var pos = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(5, 5, 5) };
            var aj = new[] { 0.25, 1.0, 0.5 };                // the second is not finer than alpha0 = 1: not a sample of the field
            var f = new AlphaSizeField(pos, aj, 1.0, 0.25, 0.25);
            Assert.Equal(2, f.FineSamples);
            Assert.Equal(0.25, f.At(pos[0]));
            Assert.Equal(0.25 + 0.25 * 0.5, f.At(new Vector3d(0.5, 0, 0)), 12);          // the cone of the first sample
            Assert.Equal(0.5, f.At(new Vector3d(1, 0, 0)), 12);                          // 0.25 + 0.25 * 1: below alpha0, the cone counts
        }

        [Fact]
        public void SizeField_QueryCost_IsBounded_OnAClusteredInput()
        {
            // 20000 samples packed on a small patch (a gear on a long axle: nearly all the area in one corner), all with the finest alpha
            const double a0 = 1.0, aMin = 0.25, L = 0.25;
            var rnd = new Random(11);
            var pos = new Vector3d[20000]; var aj = new double[pos.Length];
            for (int i = 0; i < pos.Length; ++i) { pos[i] = new Vector3d(rnd.NextDouble() * 4, rnd.NextDouble() * 4, 0); aj[i] = aMin; }
            var f = new AlphaSizeField(pos, aj, a0, aMin, L);
            Assert.True(f.FineSamples < 20000);                         // thinned to one per cube of aMin / 2 (16 x 16 cells of the patch at most: 64 x 64 = 4096)
            Assert.True(f.FineSamples <= 4096 + 64);
            double worstVisited = 0;
            for (int i = 0; i < 400; ++i) {
                Vector3d q = new Vector3d(rnd.NextDouble() * 6 - 1, rnd.NextDouble() * 6 - 1, rnd.NextDouble() * 2 - 1);
                double v = f.At(q, out int visited);
                worstVisited = Math.Max(worstVisited, visited);
                // against the brute force over all the samples: the thinning moves the field by at most L * (aMin / 2) * sqrt(3)
                double brute = a0;
                foreach (Vector3d p in pos) brute = Math.Min(brute, aMin + L * (p - q).Length);
                Assert.InRange(v, brute - 1e-12, brute + L * aMin / 2 * Math.Sqrt(3) + 1e-12);
            }
            Assert.True(worstVisited <= 4160, "visited " + worstVisited + " of " + f.FineSamples);
            // far from the patch nothing is visited
            f.At(new Vector3d(100, 100, 100), out int far); Assert.Equal(0, far);
            // a cell that cannot lower the best is skipped: a coarse cluster next to a fine one
            var pos2 = new[] { new Vector3d(0, 0, 0), new Vector3d(0.9, 0, 0) }; var aj2 = new[] { 0.25, 0.9 };
            var g = new AlphaSizeField(pos2, aj2, 1.0, 0.25, 0.25);
            Assert.Equal(0.25, g.At(new Vector3d(0, 0, 0), out int vis2), 12);
            Assert.True(vis2 <= 2);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        public void SizeField_WideAxis_PreservesDistinctSamplesAndQueries(int sign)
        {
            // x / (alphaMin / 2) = +-2^63 overflows long on the old code, dropping the second sample and breaking At()
            double x = sign * ExactPoint3.ScaleB(1.0, 58);
            var pos = new[] { new Vector3d(x, 0, 0), new Vector3d(x + 128, 0, 0) };
            var aj = new[] { 0.0625, 0.0625 };
            var f = new AlphaSizeField(pos, aj, 1.0, 0.0625, 0.25);
            Assert.Equal(2, f.FineSamples);
            Assert.Equal(0.0625, f.At(pos[0]));
            Assert.Equal(0.0625, f.At(pos[1]));
            Assert.Equal(1.0, f.At(new Vector3d(x + 64, 0, 0)));
            // Signed zero on an unsaturated axis welds with +0.0 even when x is wide.
            double negZero = BitConverter.Int64BitsToDouble(long.MinValue);
            var posNeg = new[] { new Vector3d(x, 0, 0), new Vector3d(x, negZero, 0) };
            var fNeg = new AlphaSizeField(posNeg, aj, 1.0, 0.0625, 0.25);
            Assert.Equal(1, fNeg.FineSamples);
        }

        [Fact]
        public void SizeField_SubnormalRadiusSquared_DoesNotSkipInsideSample()
        {
            // r = sqrt(1.499) * 2^-537: r^2 rounds down to 2^-1074 while dx^2 + dy^2 = 2 * 2^-1074 on the old code
            double r = Math.Sqrt(1.499) * ExactPoint3.ScaleB(1.0, -537);
            Assert.True(r * r > 0 && r * r < ExactPoint3.ScaleB(1.0, -1022));
            double aMin = r, a0 = 2 * r, L = 1.0;
            var f = new AlphaSizeField(new[] { Vector3d.Zero }, new[] { aMin }, a0, aMin, L);
            Vector3d q = new Vector3d(0.6 * r, 0.6 * r, 0);
            double expected = aMin + L * (0.6 * r * Math.Sqrt(2.0));
            Assert.True(expected < a0);
            Assert.True(Math.Abs(f.At(q) - expected) <= 1e-12 * expected, $"{f.At(q)} vs {expected}");
        }

        [Fact]
        public void SizeField_HugeRadius_DoesNotOverflowDistanceSquared()
        {
            double r = ExactPoint3.ScaleB(1.0, 600);
            Assert.True(double.IsPositiveInfinity(r * r));
            double aMin = 1.0, a0 = 2.0, L = 1.0 / r;
            var f = new AlphaSizeField(new[] { Vector3d.Zero }, new[] { aMin }, a0, aMin, L);
            Vector3d q = new Vector3d(0.5 * r, 0, 0);
            Assert.Equal(1.5, f.At(q), 12);
            Assert.Equal(2.0, f.At(new Vector3d(2 * r, 0, 0)));
        }

        [Fact]
        public void SizeField_Invariance_OverSimilarityAll()
        {
            var basePos = new[] { new Vector3d(0, 0, 0), new Vector3d(0.4, 0.2, 0.1), new Vector3d(2.5, 1.0, 0.5) };
            var baseAj = new[] { 0.25, 0.4, 0.5 };
            var baseQueries = new[] { new Vector3d(0.1, 0.05, 0), new Vector3d(1.0, 0.5, 0.2), new Vector3d(5.0, 5.0, 5.0) };
            var f0 = new AlphaSizeField(basePos, baseAj, 1.0, 0.25, 0.25);
            double[] refVals = baseQueries.Select(q => f0.At(q)).ToArray();
            foreach (Similarity s in Similarity.All()) {
                var pos = basePos.Select(s.Apply).ToArray();
                var aj = baseAj.Select(s.ApplyLength).ToArray();
                var f = new AlphaSizeField(pos, aj, s.ApplyLength(1.0), s.ApplyLength(0.25), 0.25);
                for (int i = 0; i < baseQueries.Length; ++i) {
                    double v = f.At(s.Apply(baseQueries[i]));
                    if (s.IsExact)
                        Assert.Equal(s.ApplyLength(refVals[i]), v);
                    else {
                        double tol = Math.Max(1e-9 * refVals[i], 4 * s.Resolution(5.0));
                        Assert.True(Math.Abs(v / s.Scale - refVals[i]) <= tol, $"{s.Name}: {v / s.Scale} vs {refVals[i]} (tol {tol})");
                    }
                }
            }
        }

        [Fact]
        public void NextAlpha_RefinesWhereTheDistanceIsAboveTheTolerance_AndNeverRelaxes()
        {
            // floor 0.25, tolerance 0.1; the alpha the sample was wrapped with is the first argument
            Assert.Equal(1.0, MeshTetrahedralization.NextAlpha(1.0, 0.05, 0.1, 0.25));          // within: unchanged
            Assert.Equal(1.0, MeshTetrahedralization.NextAlpha(1.0, 0.1, 0.1, 0.25));           // at the tolerance: unchanged
            Assert.Equal(0.5, MeshTetrahedralization.NextAlpha(1.0, 0.2, 0.1, 0.25), 12);       // twice the tolerance: half
            Assert.Equal(0.25, MeshTetrahedralization.NextAlpha(1.0, 10, 0.1, 0.25));           // far: the floor
            Assert.Equal(0.5, MeshTetrahedralization.NextAlpha(0.5, 0.05, 0.1, 0.25));          // the pass before got it within the tolerance: stays fine
            // a later pass refines a sample that is still above the tolerance, in proportion to the alpha it was wrapped with
            Assert.Equal(0.5 * (0.1 / 0.15), MeshTetrahedralization.NextAlpha(0.5, 0.15, 0.1, 0.25), 12);
            Assert.Equal(0.25, MeshTetrahedralization.NextAlpha(0.3, 0.2, 0.1, 0.25));          // 0.15 is below the floor
            Assert.Equal(0.25, MeshTetrahedralization.NextAlpha(0.25, double.PositiveInfinity, 0.1, 0.25));
            // no overflow of a product at extreme scales: alphaJ * (tol / d)
            Assert.Equal(5e199, MeshTetrahedralization.NextAlpha(1e200, 2e300, 1e300, 1.0), 1e187);
        }

        [Fact]
        public void Samples_AreWeightedByArea()
        {
            // two separate triangles of area 50 and 0.5: about 99 % of the samples on the large one
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(10, 0, 0)), c = m.AppendVertex(new Vector3d(0, 10, 0));
            m.AppendTriangle(a, b, c);
            int d = m.AppendVertex(new Vector3d(20, 0, 0)), e = m.AppendVertex(new Vector3d(21, 0, 0)), f = m.AppendVertex(new Vector3d(20, 1, 0));
            m.AppendTriangle(d, e, f);
            Vector3d[] s = AlphaSizeField.Samples(m, 4000);
            int small = s.Count(p => p.x >= 20);
            Assert.InRange(small, 15, 70);              // 1 % of 4000 is 40
        }

        [Fact]
        public void Samples_AreDeterministicAreaWeighted_AndScaleBitForBit()
        {
            DMesh3 input = Comb();
            Vector3d[] a = AlphaSizeField.Samples(input, 500), b = AlphaSizeField.Samples(input, 500);
            Assert.Equal(a, b);
            DMesh3 big = Invariance.Transformed(input, Similarity.Pow2Large);
            Vector3d[] c = AlphaSizeField.Samples(big, 500);
            for (int i = 0; i < a.Length; ++i) Assert.Equal(1024 * a[i], c[i]);
            Assert.Empty(AlphaSizeField.Samples(new DMesh3(), 10));
            // every sample lies on the input
            var tree = new DMeshAABBTree3(input, true);
            foreach (Vector3d p in a) { tree.FindNearestTriangle(p, out double d2); Assert.True(Math.Sqrt(d2) < 1e-6 * input.CachedBounds.DiagonalLength); }   // (the exact union has sliver triangles: their points are only as exact as the barycentric sum)
        }
    }
}
