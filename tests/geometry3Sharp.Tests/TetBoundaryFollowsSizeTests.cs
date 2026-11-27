using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshTetrahedralization.BoundaryFollowsSize (docs/research/TET_RADIUS_EDGE_AT_SURFACE_2026-11.md, option B): with a size option the wrap's
    /// surface follows the wanted size, so the fixed boundary is not coarser than the interior next to it and the tets above the radius-edge bound
    /// there (which the refinement cannot split, the surface is fixed) mostly go. Off, or without a size option: the output as before, bit for bit.
    /// </summary>
    public class TetBoundaryFollowsSizeTests
    {
        static double Diagonal(DMesh3 m) => m.GetBounds().DiagonalLength;

        static MeshTetrahedralization Make(string shape, double hPerDiagonal, bool follow, bool improve = false)
        {
            DMesh3 input = TetMeshingStage2Tests.Input(shape);
            return new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = improve, BoundaryFollowsSize = follow,
                TargetEdgeLength = hPerDiagonal > 0 ? Diagonal(input) / hPerDiagonal : 0 };
        }

        static bool Go(MeshTetrahedralization tm)
        {
            var budget = new CallBudget(CallBudget.Standard);
            tm.CancelF = budget.Exceeded;
            return tm.Compute();
        }

        // the result itself: exactly valid (closed 2-manifold boundary, positive tets), finite, the oracle agrees, the targets were measured
        static void AssertGood(MeshTetrahedralization tm)
        {
            Assert.Equal(MeshTetrahedralization.TetStatus.Success, tm.Status);
            Assert.NotNull(tm.Result);
            Assert.Null(TetMeshQuality.NotValidReason(tm.Result));
            Assert.Null(TetOracle.Check(tm.Result, null));
            Assert.All(tm.Result.Vertices, v => Assert.True(v.IsFinite));
            Assert.NotNull(tm.Targets);
            Assert.Equal(tm.Result.Tets.Count, tm.Targets.Tets);
        }

        // an L of two overlapping boxes (a soup; x and y in [0, 1]): its concave corner is bridged by the wrap, so MaxSurfaceDeviation has work there
        static DMesh3 LShape()
        {
            DMesh3 a = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.125, 0.125), new Vector3d(0.5, 0.125, 0.125)) }.Generate().MakeDMesh();
            DMesh3 b = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.125, 0.5, 0.125), new Vector3d(0.125, 0.5, 0.125)) }.Generate().MakeDMesh();
            new MeshEditor(a).AppendMesh(b);
            return a;
        }

        static double MeanBoundaryEdge(TetMesh3 t)
        {
            double sum = 0; int n = 0;
            foreach (Index3i f in t.BoundaryTriangles) {
                sum += t.Vertices[f.a].Distance(t.Vertices[f.b]) + t.Vertices[f.b].Distance(t.Vertices[f.c]) + t.Vertices[f.c].Distance(t.Vertices[f.a]); n += 3;
            }
            return sum / n;
        }

        // ---- the effect: fewer tets above the radius-edge bound, a boundary at the wanted size
        // (measured at alpha 12: sphere h = diagonal / 32 312 -> 3 of 39k tets, box h = diagonal / 24 992 -> 95 of 14-17k)
        [Theory]
        [InlineData("sphere", 32), InlineData("box", 24)]
        public void SizeFinerThanTheWrap_FollowingTheSize_RemovesMostTetsAboveTheRadiusEdgeBound(string shape, double hPerDiagonal)
        {
            MeshTetrahedralization off = Make(shape, hPerDiagonal, false); Assert.True(Go(off)); AssertGood(off);
            MeshTetrahedralization on = Make(shape, hPerDiagonal, true); Assert.True(Go(on)); AssertGood(on);
            int before = off.Targets.TetsAboveRadiusEdge, after = on.Targets.TetsAboveRadiusEdge;
            Assert.True(before >= 20, "the case must have bad tets without the option: " + before);
            Assert.True(after <= 0.3 * before, after + " of " + on.Result.Tets.Count + " against " + before + " of " + off.Result.Tets.Count);
            Assert.True(on.Counts["boundary_size_fine_samples"] > 0);
            // the boundary follows the size: its mean edge is finer than without and not coarser than about the size
            double h = on.TargetEdgeLength;
            Assert.True(MeanBoundaryEdge(on.Result) < 0.9 * MeanBoundaryEdge(off.Result), MeanBoundaryEdge(on.Result) + " vs " + MeanBoundaryEdge(off.Result));
            Assert.True(MeanBoundaryEdge(on.Result) <= 1.2 * h, MeanBoundaryEdge(on.Result) + " vs h " + h);
            // the uniform alpha is unchanged (the field only makes the wrap finer)
            Assert.Equal(off.UsedAlpha, on.UsedAlpha);
        }

        [Fact]
        public void WithStageTwo_TheResultMeetsTheDihedralTargetAndHasFewerTetsAboveTheBound()
        {
            MeshTetrahedralization off = Make("box", 16, false, improve: true); Assert.True(Go(off)); AssertGood(off);
            MeshTetrahedralization on = Make("box", 16, true, improve: true); Assert.True(Go(on)); AssertGood(on);
            Assert.True(on.Targets.MinDihedralMet, on.Targets.WorstMinDihedral.ToString());
            Assert.True(on.Targets.TetsAboveRadiusEdge <= 0.3 * off.Targets.TetsAboveRadiusEdge, on.Targets.TetsAboveRadiusEdge + " vs " + off.Targets.TetsAboveRadiusEdge);
            TetMeshingStage2Tests.Invariants(on);
        }

        [Fact]
        public void SizeFunction_IsFollowedWhereItIsFine_AndOnlyThere()
        {
            DMesh3 input = TetMeshingStage2Tests.Input("box");     // x in [0, 1]
            double h = Diagonal(input) / 24;
            // fine in the half x > 0.5, no size (NaN) in the other half
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, BoundaryFollowsSize = true,
                SizeFunction = p => p.x > 0.5 ? h : double.NaN };
            Assert.True(Go(tm)); AssertGood(tm);
            var plain = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, SizeFunction = p => p.x > 0.5 ? h : double.NaN };
            Assert.True(Go(plain));
            Func<TetMesh3, bool, double> half = (t, pos) => {
                double sum = 0; int n = 0;
                foreach (Index3i f in t.BoundaryTriangles) {
                    Vector3d c = (t.Vertices[f.a] + t.Vertices[f.b] + t.Vertices[f.c]) / 3;
                    if ((c.x > 0.5) != pos || Math.Abs(c.x - 0.5) < 0.3) continue;     // away from the step, which the field grades over
                    sum += t.Vertices[f.a].Distance(t.Vertices[f.b]); n++;
                }
                return sum / n;
            };
            Assert.True(half(tm.Result, true) < 0.8 * half(plain.Result, true), half(tm.Result, true) + " vs " + half(plain.Result, true));
            // the side without a size keeps (about) the wrap's spacing: the graded field only reaches a little past x = 0.5
            Assert.InRange(half(tm.Result, false), 0.85 * half(plain.Result, false), 1.15 * half(plain.Result, false));
            Assert.True(tm.Targets.TetsAboveRadiusEdge < plain.Targets.TetsAboveRadiusEdge);
        }

        // ---- off values: bit for bit the output without the option
        [Theory]
        [InlineData("none"), InlineData("grading"), InlineData("nan-function"), InlineData("zero-function"), InlineData("coarse")]
        public void WithoutAFinerSize_TheOptionChangesNothing(string kind)
        {
            Func<bool, MeshTetrahedralization> make = follow => {
                MeshTetrahedralization tm = Make("box", 0, follow);
                if (kind == "grading") tm.GradingFactor = 0.5;              // the graded size starts at the wrap's own spacing: not followed
                if (kind == "nan-function") tm.SizeFunction = p => double.NaN;
                if (kind == "zero-function") tm.SizeFunction = p => 0;         // not a size (invalid): must not become the finest alpha
                if (kind == "coarse") tm.TargetEdgeLength = 10 * Diagonal(tm.Mesh);   // coarser than the wrap: alpha stays
                return tm;
            };
            MeshTetrahedralization a = make(false); Assert.True(Go(a));
            MeshTetrahedralization b = make(true); Assert.True(Go(b)); AssertGood(b);
            Assert.Equal(a.Result.Vertices, b.Result.Vertices);
            Assert.Equal(a.Result.Tets, b.Result.Tets);
            Assert.Equal(a.Result.BoundaryTriangles, b.Result.BoundaryTriangles);
            var expected = new Dictionary<string, long>(a.Counts);
            if (kind != "none" && kind != "grading") expected["boundary_size_fine_samples"] = 0;   // the option ran and found no finer size
            Assert.Equal(expected, b.Counts);
        }

        [Fact]
        public void SizesBelowTheAlphaFloor_GiveTheFloor_BitForBit()
        {
            // a size only on the input's face x = 0 (where the samples lie; no tet centroid is there): the wrap's local alpha is clamped to
            // max(alpha / 4, 3 offsets), so a size far below it gives the same result as the size whose alpha is exactly the floor
            DMesh3 input = TetMeshingStage2Tests.Input("box");
            MeshTetrahedralization probe = Make("box", 0, false); Assert.True(Go(probe));
            double aMin = Math.Max(probe.UsedAlpha / 4, MeshAlphaWrap.AlphaFieldFloorOffsets * probe.UsedOffset);
            Func<double, MeshTetrahedralization> run = hFace => {
                var tm = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, BoundaryFollowsSize = true,
                    SizeFunction = p => Math.Abs(p.x) < 1e-12 ? hFace : double.NaN };
                Assert.True(Go(tm)); AssertGood(tm);
                return tm;
            };
            MeshTetrahedralization atFloor = run(aMin * (1 - 1e-12) / MeshTetrahedralization.BoundaryAlphaPerSize);   // (just below: 0.6 * (aMin / 0.6) need not round back to aMin)
            MeshTetrahedralization tiny = run(1e-6 * aMin);
            Assert.True(atFloor.Counts["boundary_size_fine_samples"] > 0);
            Assert.Equal(atFloor.Result.Vertices, tiny.Result.Vertices);
            Assert.Equal(atFloor.Result.Tets, tiny.Result.Tets);
        }

        [Fact]
        public void AFailedFollowingWrap_MeshesTheUniformWrap_AndSaysSo()
        {
            MeshTetrahedralization a = Make("sphere", 32, false); Assert.True(Go(a));
            MeshTetrahedralization b = Make("sphere", 32, true); b.LocalWrapMaxSteinerPoints = 5; Assert.True(Go(b)); AssertGood(b);
            Assert.Equal(1, b.Counts["boundary_size_wrap_failed"]);
            Assert.Equal(a.Result.Vertices, b.Result.Vertices);
            Assert.Equal(a.Result.Tets, b.Result.Tets);
        }

        [Theory]
        [InlineData(256), InlineData(20000)]
        public void Cancelled_WhileFollowingTheSize_IsCancelled(int afterSizeCalls)
        {
            // the size function is evaluated first at the 20000 input samples of the option: cancel during the sampling (at its check after 256 samples) or right after it,
            // inside the following wrap (20000)
            DMesh3 input = TetMeshingStage2Tests.Input("sphere");
            double h = Diagonal(input) / 16;
            long sizeCalls = 0;
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, BoundaryFollowsSize = true, MaxThreads = 1,
                SizeFunction = p => { sizeCalls++; return h; } };
            tm.CancelF = () => sizeCalls >= afterSizeCalls;
            Assert.False(tm.Compute());
            Assert.Equal(MeshTetrahedralization.TetStatus.Cancelled, tm.Status);
            Assert.Null(tm.Result);
            Assert.Equal(afterSizeCalls, sizeCalls);     // stopped where it was cancelled: the refinement (which also calls it) never started
        }

        [Fact]
        public void WithMaxSurfaceDeviation_BothFieldsApply_AndTheResultIsGood()
        {
            // the size covers only x > 0.5 (one arm of the L); the deviation (tolerance raised to 2 offsets) must refine beyond the size alphas at the
            // concave corner (measured: 1468 samples, 678 without the size)
            DMesh3 input = LShape();
            double h = Diagonal(input) / 24;
            Func<Vector3d, double> f = p => p.x > 0.5 ? h : double.NaN;
            var dev = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, SizeFunction = f, MaxSurfaceDeviation = 1e-9 };
            Assert.True(Go(dev)); AssertGood(dev);
            var both = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, BoundaryFollowsSize = true, SizeFunction = f,
                MaxSurfaceDeviation = 1e-9 };
            Assert.True(Go(both)); AssertGood(both);
            Assert.True(both.Counts["boundary_size_fine_samples"] > 0);
            Assert.True(both.Counts["local_alpha_fine_samples"] > 0 && both.Counts["local_alpha_passes"] >= 1,
                both.Counts["local_alpha_fine_samples"] + " samples refined beyond the size, " + both.Counts["local_alpha_passes"] + " passes");
            // only the refinement beyond the size counts (the corner, which the size does not cover): about what the deviation alone refines there
            Assert.InRange(both.Counts["local_alpha_fine_samples"], 1, (long)(1.2 * dev.Counts["local_alpha_fine_samples"]));
            // the size's half is finer than with the deviation alone, and fewer tets are above the bound
            Func<TetMesh3, double> sizedHalf = t => {
                double sum = 0; int n = 0;
                foreach (Index3i q in t.BoundaryTriangles) {
                    Vector3d c = (t.Vertices[q.a] + t.Vertices[q.b] + t.Vertices[q.c]) / 3;
                    if (c.x < 0.8) continue;
                    sum += t.Vertices[q.a].Distance(t.Vertices[q.b]); n++;
                }
                return sum / n;
            };
            Assert.True(sizedHalf(both.Result) < 0.9 * sizedHalf(dev.Result), sizedHalf(both.Result) + " vs " + sizedHalf(dev.Result));
            Assert.True(both.Targets.TetsAboveRadiusEdge < dev.Targets.TetsAboveRadiusEdge, both.Targets.TetsAboveRadiusEdge + " vs " + dev.Targets.TetsAboveRadiusEdge);
        }

        [Fact]
        public void WithMaxSurfaceDeviation_ThatTheSizeAlreadyMeets_NoDeviationWrapRuns()
        {
            // a loose tolerance (half the diagonal) that the size's wrap already meets: no sample is refined beyond the size alphas, so the
            // deviation pass neither wraps again nor reports work
            DMesh3 input = TetMeshingStage2Tests.Input("box");
            double h = Diagonal(input) / 24;
            var both = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, BoundaryFollowsSize = true, TargetEdgeLength = h,
                MaxSurfaceDeviation = 0.5 * Diagonal(input) };
            Assert.True(Go(both)); AssertGood(both);
            Assert.Equal(0, both.Counts["local_alpha_fine_samples"]);
            Assert.Equal(0, both.Counts["local_alpha_passes"]);
        }

        [Fact]
        public void AFailedFollowingWrap_WithMaxSurfaceDeviation_TheDeviationStartsFromTheUniformWrap()
        {
            // the size's wrap fails (its own point limit), the deviation wraps do not: the result is that of MaxSurfaceDeviation without the option
            DMesh3 input = LShape();      // (the deviation has work here, so seeding it with the failed wrap's alphas would change the result)
            double h = Diagonal(input) / 24;
            var dev = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, TargetEdgeLength = h, MaxSurfaceDeviation = 1e-9 };
            Assert.True(Go(dev)); AssertGood(dev);
            var both = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, BoundaryFollowsSize = true, TargetEdgeLength = h,
                MaxSurfaceDeviation = 1e-9, SizeWrapMaxSteinerPoints = 5 };
            Assert.True(Go(both)); AssertGood(both);
            Assert.Equal(1, both.Counts["boundary_size_wrap_failed"]);
            Assert.Equal(dev.Result.Vertices, both.Result.Vertices);
            Assert.Equal(dev.Result.Tets, both.Result.Tets);
            Assert.True(dev.Counts["local_alpha_fine_samples"] > 0);
            Assert.Equal(dev.Counts["local_alpha_fine_samples"], both.Counts["local_alpha_fine_samples"]);
        }

        [Fact]
        public void SteppedSizeFunction_WithStageTwo_MeetsTheDihedralTarget()
        {
            // a size that jumps by 3x across x = 0.5 (a graded local alpha with steps): the improved result meets the dihedral target, with the
            // half-gradation retry of Compute() if the first run misses it
            DMesh3 input = TetMeshingStage2Tests.Input("box");
            double h = Diagonal(input) / 30;
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, BoundaryFollowsSize = true,
                SizeFunction = p => p.x > 0.5 ? h : 3 * h };
            Assert.True(Go(tm)); AssertGood(tm);
            Assert.True(tm.Counts["boundary_size_fine_samples"] > 0);
            Assert.True(tm.Targets.MinDihedralMet, tm.Targets.WorstMinDihedral + " degrees, retry " + (tm.Counts.ContainsKey("local_alpha_retry") ? "ran" : "not run"));
            TetMeshingStage2Tests.Invariants(tm);
        }

        [Fact]
        public void MissedDihedralTarget_TheSizePathRunsOnceMoreWithHalfTheGradation()
        {
            // a 20 degree target that one improve pass cannot reach (as the retry tests of MaxSurfaceDeviation): with the option the run is repeated
            // with half the gradation of the size field, the target stays reported as missed; without it (no local alpha) there is no second run
            DMesh3 input = TetMeshingStage2Tests.Input("box");
            double h = Diagonal(input) / 24;
            Func<bool, MeshTetrahedralization> make = follow => new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, ImprovePasses = 1,
                SliverDihedralDegrees = 20, BoundaryFollowsSize = follow, SizeFunction = p => p.x > 0.5 ? h : 3 * h };
            MeshTetrahedralization plain = make(false); Go(plain); Assert.NotNull(plain.Result);
            Assert.False(plain.Targets.MinDihedralMet);
            Assert.False(plain.Counts.ContainsKey("local_alpha_retry"));
            MeshTetrahedralization tm = make(true); Go(tm); Assert.NotNull(tm.Result);
            Assert.Equal(1, tm.Counts["local_alpha_retry"]);
            Assert.Equal(0.25, tm.GradationFirst); Assert.Equal(0.125, tm.GradationRetry);
            Assert.False(double.IsNaN(tm.RetryFirstWorstMinDihedral));
            Assert.False(tm.Targets.MinDihedralMet);                         // reported, never silent
            Assert.True(tm.Targets.WorstMinDihedral >= tm.RetryFirstWorstMinDihedral);   // the better of the two runs is kept
            Assert.Null(TetMeshQuality.NotValidReason(tm.Result));
        }

        // ---- reuse, determinism, similarity
        [Fact]
        public void Reuse_ComputingTwiceOnOneInstance_GivesTheSameResult_AndSwitchingOffLeavesNothingBehind()
        {
            MeshTetrahedralization tm = Make("box", 24, true);
            Assert.True(Go(tm));
            List<Vector3d> v = tm.Result.Vertices.ToList(); List<Index4i> t = tm.Result.Tets.ToList();
            var counts = new Dictionary<string, long>(tm.Counts);
            Assert.True(Go(tm));
            Assert.Equal(v, tm.Result.Vertices); Assert.Equal(t, tm.Result.Tets); Assert.Equal(counts, tm.Counts);
            tm.BoundaryFollowsSize = false; Assert.True(Go(tm));
            MeshTetrahedralization fresh = Make("box", 24, false); Assert.True(Go(fresh));
            Assert.Equal(fresh.Result.Tets, tm.Result.Tets); Assert.Equal(fresh.Counts, tm.Counts);
        }

        [Fact]
        public void EveryMaxThreads_AndRepeatedRuns_GiveTheSameResult()
        {
            MeshTetrahedralization serial = Make("box", 24, true); serial.MaxThreads = 1; Assert.True(Go(serial));
            foreach (int threads in new[] { 0, 4 }) {
                MeshTetrahedralization tm = Make("box", 24, true); tm.MaxThreads = threads; Assert.True(Go(tm));
                Assert.Equal(serial.Result.Vertices, tm.Result.Vertices); Assert.Equal(serial.Result.Tets, tm.Result.Tets); Assert.Equal(serial.Counts, tm.Counts);
            }
            MeshTetrahedralization again = Make("box", 24, true); again.MaxThreads = 1; Assert.True(Go(again));
            Assert.Equal(serial.Result.Tets, again.Result.Tets);
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Similarity_TheFollowedSurfaceScalesWithTheInput(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = TetMeshingStage2Tests.Input("box");
            double h = Diagonal(input) / 24;
            Func<Vector3d, double> f = p => h * (1 + 0.5 * Math.Abs(p.x));
            var a = new MeshTetrahedralization(input) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, BoundaryFollowsSize = true, TargetEdgeLength = 1.2 * h, SizeFunction = f };
            Assert.True(Go(a)); AssertGood(a);
            var b = new MeshTetrahedralization(Invariance.Transformed(input, s)) { RelativeAlpha = 12, RescuePasses = 0, Improve = false, BoundaryFollowsSize = true,
                TargetEdgeLength = s.Scale * 1.2 * h, SizeFunction = p => s.Scale * f(s.Inverse(p)) };
            Assert.True(Go(b)); AssertGood(b);
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            } else {
                Assert.InRange((double)b.Result.Tets.Count, 0.8 * a.Result.Tets.Count, 1.25 * a.Result.Tets.Count);
                Assert.True(b.Counts["boundary_size_fine_samples"] > 0);
            }
        }
    }
}
