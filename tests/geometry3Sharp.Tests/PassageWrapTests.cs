using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// PassageWrap: an alpha wrap driven by "this passage must stay open" (test pins: polyline axis and radius), docs/design/PASSAGE_WRAP.md.
    /// Procedural parts: a block with a cross bore, an angled channel (two bores meeting) and a blind hole (marching cubes of the
    /// signed distance of the block minus a capsule chain around the pin axis), and inputs that are not solids (a triangle soup, an open
    /// tube, random clutter with many pinches).
    /// </summary>
    public class PassageWrapTests
    {
        const double H = 12, Margin = 4, BoreR = 3;

        internal static List<Vector3d> Axis(string part)
        {
            double e = H + Margin;
            switch (part) {
                case "cross": return new List<Vector3d> { new Vector3d(0, 0, -e), new Vector3d(0, 0, e) };
                case "angled": return new List<Vector3d> { new Vector3d(0, 0, e), Vector3d.Zero, new Vector3d(e, 0, -0.5 * e) };
                case "blind": return new List<Vector3d> { new Vector3d(0, 0, e), Vector3d.Zero };
                default: throw new ArgumentException(part);
            }
        }

        sealed class PartSdf : ImplicitFunction3d
        {
            public List<Vector3d> Axis;
            public double R;
            public double Value(ref Vector3d p)
            {
                double box = Math.Max(Math.Abs(p.x) - H, Math.Max(Math.Abs(p.y) - H, Math.Abs(p.z) - H));
                double tube = double.MaxValue;
                for (int i = 0; i + 1 < Axis.Count; ++i) {
                    Vector3d a = Axis[i], ab = Axis[i + 1] - a;
                    double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / ab.LengthSquared));
                    tube = Math.Min(tube, (p - (a + t * ab)).Length - R);
                }
                return Math.Max(box, -tube);
            }
        }

        // the block (side 2 H) minus the capsule chain of the axis, radius BoreR: marching cubes, cell BoreR / 4 (about 4000 triangles)
        internal static DMesh3 Part(string part)
        {
            var mc = new MarchingCubes {
                Implicit = new PartSdf { Axis = Axis(part), R = BoreR }, CubeSize = BoreR / 4, IsoValue = 0,
                RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 10,
                Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.8 * (H + Margin)), ParallelCompute = false
            };
            mc.Generate();
            return mc.Mesh;
        }

        // the same surface as a soup: every triangle with its own three vertices
        internal static DMesh3 Soup(DMesh3 m)
        {
            var s = new DMesh3();
            foreach (int t in m.TriangleIndices()) {
                Index3i tri = m.GetTriangle(t);
                s.AppendTriangle(s.AppendVertex(m.GetVertex(tri.a)), s.AppendVertex(m.GetVertex(tri.b)), s.AppendVertex(m.GetVertex(tri.c)));
            }
            return s;
        }

        // an open tube (no end caps) of radius r and length 2 * half around the z axis
        static DMesh3 OpenTube(double r, double half, int n)
        {
            var m = new DMesh3();
            for (int k = 0; k < n; ++k) {
                double a = 2 * Math.PI * k / n;
                m.AppendVertex(new Vector3d(r * Math.Cos(a), r * Math.Sin(a), -half));
                m.AppendVertex(new Vector3d(r * Math.Cos(a), r * Math.Sin(a), half));
            }
            for (int k = 0; k < n; ++k) {
                int k1 = (k + 1) % n;
                m.AppendTriangle(2 * k, 2 * k1, 2 * k + 1);
                m.AppendTriangle(2 * k1, 2 * k1 + 1, 2 * k + 1);
            }
            return m;
        }

        static PassageWrap Run(DMesh3 mesh, List<Vector3d> axis, double radius, Action<PassageWrap> setup = null)
        {
            var w = new PassageWrap(mesh) { Pins = { new PassagePin(axis, radius) }, MaxThreads = 1 };
            setup?.Invoke(w);
            w.Compute();
            return w;
        }

        // independent of PolylineClearance: the smallest of the nearest-triangle distances at fine samples of the axis (an upper bound of
        // the clearance: a pin reported as passed must not be refuted by it)
        static double SampledClearance(DMesh3 m, List<Vector3d> axis, double step)
        {
            var tree = new DMeshAABBTree3(m, true);
            double best = double.MaxValue;
            for (int i = 0; i + 1 < axis.Count; ++i) {
                int n = Math.Max(1, (int)Math.Ceiling((axis[i + 1] - axis[i]).Length / step));
                for (int j = 0; j <= n; ++j) {
                    tree.FindNearestTriangle(Vector3d.Lerp(axis[i], axis[i + 1], (double)j / n), out double dsq);
                    best = Math.Min(best, Math.Sqrt(dsq));
                }
            }
            return best;
        }

        public static IEnumerable<object[]> Parts() { foreach (string p in new[] { "cross", "angled", "blind" }) yield return new object[] { p }; }

        // ---- the a priori bound ----

        [Theory]
        [InlineData(3.0, 1.0, 30.0, false)]
        [InlineData(3.0, 0.0001, 30.0, false)]
        [InlineData(3.0, 2.5, 10.0, false)]
        [InlineData(5.0, 2.0, 3.0, false)]
        [InlineData(3.0, 1.0, 30.0, true)]
        [InlineData(7.0, 3.5, 10.0, true)]
        public void MaxAlpha_IsTheRootOfTheBound(double r, double p, double ratio, bool pull)
        {
            double a = PassageWrap.MaxAlpha(r, p, ratio, pull);
            double d = (pull ? 2.0 : 1.0) * a / ratio;
            // sqrt((r - d)^2 - a^2) = p: the carved wrap's clearance from the axis is exactly the pin radius at the largest alpha
            Assert.True(r - d > a);
            Assert.True(Math.Abs((r - d) * (r - d) - a * a - p * p) <= 1e-12 * r * r, $"{a}");
            // and a larger alpha does not keep the pin (strictly below p)
            double a2 = a * 1.001, d2 = (pull ? 2.0 : 1.0) * a2 / ratio;
            Assert.True(!(r - d2 > a2) || Math.Sqrt((r - d2) * (r - d2) - a2 * a2) < p);
            // open passage (p -> 0): alpha + offset = r
            double open = PassageWrap.MaxAlpha(r, 1e-9 * r, ratio, pull);
            Assert.True(Math.Abs(open + (pull ? 2.0 : 1.0) * open / ratio - r) <= 1e-6 * r);
        }

        [Fact]
        public void MaxAlpha_NotPositiveWhereNoAlphaKeepsThePin()
        {
            Assert.Equal(0.0, PassageWrap.MaxAlpha(3, 3, 30));
            Assert.Equal(0.0, PassageWrap.MaxAlpha(3, 4, 30));
            Assert.Equal(0.0, PassageWrap.MaxAlpha(double.NaN, 1, 30));
            Assert.Equal(0.0, PassageWrap.MaxAlpha(3, 1, 1));      // offset not below alpha
            Assert.True(PassageWrap.MaxAlpha(3, 1, 30) > PassageWrap.MaxAlpha(3, 2, 30));
            Assert.True(PassageWrap.MaxAlpha(3, 1, 30) > PassageWrap.MaxAlpha(3, 1, 10));
            Assert.True(PassageWrap.MaxAlpha(3, 1, 30, true) < PassageWrap.MaxAlpha(3, 1, 30, false));
        }

        // ---- the helper on the procedural parts ----

        // the bound holds with no fallback: the first wrap, at the largest alpha, keeps the pin, certified, and the measured clearance is
        // at least the bound's sqrt((r - d)^2 - a^2) (the data of the study: measured / bound 1.01 to 1.9, 68 wraps, 0 violations)
        [Theory]
        [MemberData(nameof(Parts))]
        public void Part_PassesAtTheBound_WithoutFallback(string part)
        {
            DMesh3 mesh = Part(part);
            double pin = 0.5 * BoreR;
            PassageWrap w = Run(mesh, Axis(part), pin);
            Assert.True(w.Status == PassageWrap.PassageStatus.Success, w.Status + " " + w.Error);
            Assert.Equal(0, w.FallbackSteps);
            Assert.True(w.BoundPremisesHeld);
            PassagePinResult r = w.PinResults[0];
            Assert.True(r.Passed && r.AxisOutside);
            double a = w.Alpha, d = w.Offset;
            double expected = Math.Sqrt((r.InputClearance - d) * (r.InputClearance - d) - a * a);
            // (the simplified wrap may lose half an offset)
            Assert.True(r.ClearanceLow >= expected - (w.Simplified ? 0.5 * d : 0) - 1e-9, $"{part}: {r.ClearanceLow} vs bound {expected}");
            Assert.True(r.ClearanceLow >= pin);
            Assert.True(r.ClearanceHigh >= r.ClearanceLow);
            // the answer is the wrap, closed and clean, and the sampled clearance agrees
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            Assert.True(SampledClearance(w.Result, Axis(part), 0.05) >= r.ClearanceLow - 1e-9);
            // the alpha is the bound's (0.999 of it): MaxAlpha at the measured input clearance
            Assert.Equal(0.999 * PassageWrap.MaxAlpha(r.InputClearance, pin, 30), w.BoundAlpha, 12);
            Assert.True(Math.Abs(w.Alpha - w.BoundAlpha) <= 0, "no fallback: alpha is the bound");
            // the input clearance is the bore's inscribed radius, within the marching cubes' accuracy
            Assert.InRange(r.InputClearance, 0.95 * BoreR, 1.01 * BoreR);
        }

        // a pin as wide as the bore allows at the offset: the open passage (the radius just above 0) gives alpha + offset = r
        [Fact]
        public void OpenPassage_AlphaPlusOffsetIsTheClearance()
        {
            DMesh3 mesh = Part("cross");
            PassageWrap w = Run(mesh, Axis("cross"), 0.05 * BoreR);
            Assert.Equal(PassageWrap.PassageStatus.Success, w.Status);
            double r = w.PinResults[0].InputClearance;
            Assert.True(Math.Abs((w.BoundAlpha / 0.999 + w.BoundAlpha / 0.999 / 30) - r) <= 5e-3 * r);
            Assert.Equal(0, w.FallbackSteps);
        }

        // the bound is tight: 10 % above it the passage closes (study: closed from 1.02), so the first attempt fails the check and the
        // fallback finds a wrap that passes; every step is measured, none is claimed. Kills a mutant that raises the bound.
        [Fact]
        public void AboveTheBound_TheCheckFailsAndTheFallbackRecovers()
        {
            DMesh3 mesh = Part("cross");
            PassageWrap w = Run(mesh, Axis("cross"), 0.05 * BoreR, x => x.FirstAlphaScale = 1.1 / 0.999);
            Assert.Equal(PassageWrap.PassageStatus.Success, w.Status);
            Assert.True(w.FallbackSteps >= 1, $"steps {w.FallbackSteps}");
            Assert.True(w.PinResults[0].Passed);
            Assert.True(w.Alpha < w.BoundAlpha * 1.1);
        }

        [Fact]
        public void FarAboveTheBound_NoFallbackLeft_ReportsNotKept_NeverAPass()
        {
            DMesh3 mesh = Part("cross");
            PassageWrap w = Run(mesh, Axis("cross"), 0.05 * BoreR, x => { x.FirstAlphaScale = 3; x.MaxFallbackSteps = 0; });
            Assert.Equal(PassageWrap.PassageStatus.PassageNotKept, w.Status);
            Assert.NotNull(w.Result);
            PassagePinResult r = w.PinResults[0];
            Assert.False(r.Passed);
            Assert.True(r.ClearanceLow < 0.05 * BoreR || !r.AxisOutside);
            Assert.False(w.AllPassed);
        }

        // the angled channel with two pins (the two legs) and a mixed pin radius
        [Fact]
        public void TwoPins_TheSmallerAlphaWins()
        {
            DMesh3 mesh = Part("angled");
            List<Vector3d> axis = Axis("angled");
            var w = new PassageWrap(mesh) { MaxThreads = 1 };
            w.Pins.Add(new PassagePin(axis.Take(2), 0.4 * BoreR));
            w.Pins.Add(new PassagePin(axis.Skip(1), 0.7 * BoreR));
            Assert.True(w.Compute(), w.Status + w.Error);
            Assert.True(w.PinResults.All(p => p.Passed));
            double a0 = PassageWrap.MaxAlpha(w.PinResults[0].InputClearance, 0.4 * BoreR, 30);
            double a1 = PassageWrap.MaxAlpha(w.PinResults[1].InputClearance, 0.7 * BoreR, 30);
            Assert.True(a1 < a0);
            Assert.Equal(0.999 * Math.Min(a0, a1), w.BoundAlpha, 12);
        }

        [Fact]
        public void PinAsWideAsTheBore_IsBlockedByTheInput()
        {
            DMesh3 mesh = Part("cross");
            PassageWrap w = Run(mesh, Axis("cross"), 1.2 * BoreR);
            Assert.Equal(PassageWrap.PassageStatus.PinBlockedByInput, w.Status);
            Assert.Null(w.Result);
            Assert.Equal(1, w.PinResults.Length);
        }

        [Fact]
        public void Budget_IsReportedNotCoarsenedPast()
        {
            DMesh3 mesh = Part("cross");
            PassageWrap tight = Run(mesh, Axis("cross"), 0.5 * BoreR, x => x.MaxTriangles = 10);
            Assert.Equal(PassageWrap.PassageStatus.BudgetExceeded, tight.Status);
            Assert.Null(tight.Result);
            Assert.True(tight.EstimatedTriangles > 10);
            PassageWrap loose = Run(mesh, Axis("cross"), 0.5 * BoreR, x => x.MaxTriangles = (int)Math.Ceiling(tight.EstimatedTriangles) + 1);
            Assert.Equal(PassageWrap.PassageStatus.Success, loose.Status);
            Assert.Equal(tight.BoundAlpha, loose.BoundAlpha);
            // the estimate is the size of the wrap, up to the factor of MeshAlphaWrap.EstimateDensity's comment (the raw wrap's)
            Assert.InRange(loose.EstimatedTriangles / (loose.Result.TriangleCount * (loose.Simplified ? 1.0 : 1.0)), 0.5, 20);
        }

        [Fact]
        public void MaxAlphaLimit_GivesAFinerWrap()
        {
            DMesh3 mesh = Part("cross");
            PassageWrap coarse = Run(mesh, Axis("cross"), 0.5 * BoreR);
            PassageWrap fine = Run(mesh, Axis("cross"), 0.5 * BoreR, x => x.MaxAlphaLimit = 0.5 * coarse.BoundAlpha);
            Assert.Equal(PassageWrap.PassageStatus.Success, fine.Status);
            Assert.Equal(0.5 * coarse.BoundAlpha, fine.BoundAlpha);
            Assert.True(fine.Result.TriangleCount > coarse.Result.TriangleCount);
            Assert.True(fine.PinResults[0].ClearanceLow > coarse.PinResults[0].ClearanceLow);
        }

        [Fact]
        public void InvalidArguments_AreReported()
        {
            DMesh3 mesh = Part("cross");
            List<Vector3d> axis = Axis("cross");
            Assert.Equal(PassageWrap.PassageStatus.InvalidInput, new PassageWrap(new DMesh3()) { Pins = { new PassagePin(axis, 1) } }.Run());
            Assert.Equal(PassageWrap.PassageStatus.InvalidPins, new PassageWrap(mesh).Run());
            Assert.Equal(PassageWrap.PassageStatus.InvalidPins, Run(mesh, axis, 0).Status);
            Assert.Equal(PassageWrap.PassageStatus.InvalidPins, Run(mesh, axis, double.NaN).Status);
            Assert.Equal(PassageWrap.PassageStatus.InvalidPins, Run(mesh, new List<Vector3d>(), 1).Status);
            Assert.Equal(PassageWrap.PassageStatus.InvalidPins, Run(mesh, new List<Vector3d> { new Vector3d(double.NaN, 0, 0) }, 1).Status);
            Assert.Equal(PassageWrap.PassageStatus.InvalidPins, Run(mesh, axis, 1, x => x.OffsetRatio = 2).Status);
            Assert.Equal(PassageWrap.PassageStatus.InvalidPins, Run(mesh, axis, 1, x => x.FallbackFactor = 1).Status);
        }

        // reuse: Compute() twice on one instance gives the same result, bit for bit; threads: the wrap does not depend on MaxThreads
        [Fact]
        public void Compute_Reuse_AndThreads_SameResult()
        {
            DMesh3 mesh = Part("blind");
            PassageWrap w = Run(mesh, Axis("blind"), 0.5 * BoreR);
            string h1 = AlphaWrapManifoldStepTests.Hash(w.Result);
            double lo1 = w.PinResults[0].ClearanceLow;
            Assert.True(w.Compute());
            Assert.Equal(h1, AlphaWrapManifoldStepTests.Hash(w.Result));
            Assert.Equal(lo1, w.PinResults[0].ClearanceLow);
            PassageWrap par = Run(mesh, Axis("blind"), 0.5 * BoreR, x => x.MaxThreads = 4);
            Assert.Equal(h1, AlphaWrapManifoldStepTests.Hash(par.Result));
            Assert.Equal(lo1, par.PinResults[0].ClearanceLow);
        }

        // invariance over Similarity.All(): the pin and the part in every frame; the 2^k scales give the same alpha, status and result
        // scaled, bit for bit; the other frames the same status, the same fallback steps and a pass
        [Fact]
        public void Invariance_OverSimilarityAll()
        {
            DMesh3 mesh = Part("angled");
            List<Vector3d> axis = Axis("angled");
            double pin = 0.5 * BoreR;
            PassageWrap w0 = Run(mesh, axis, pin);
            Assert.Equal(PassageWrap.PassageStatus.Success, w0.Status);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(mesh, s);
                PassageWrap w = Run(m, axis.Select(p => s.Apply(p)).ToList(), s.ApplyLength(pin));
                Assert.True(w.Status == PassageWrap.PassageStatus.Success, s.Name + " " + w.Status + w.Error);
                Assert.Equal(0, w.FallbackSteps);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
                // (the frames that round the coordinates: the certified clearance is a lower bound within 1e-4 of the part's size, so alpha differs by that much)
                Assert.True(Math.Abs(w.BoundAlpha / s.Scale - w0.BoundAlpha) <= 5e-3 * w0.BoundAlpha, $"{s.Name}: alpha {w.BoundAlpha / s.Scale} vs {w0.BoundAlpha}");
                if (s.IsExact) {
                    Assert.Equal(w0.BoundAlpha * s.Scale, w.BoundAlpha);
                    Assert.Equal(w0.PinResults[0].InputClearance * s.Scale, w.PinResults[0].InputClearance);
                    Assert.Equal(w0.PinResults[0].ClearanceLow * s.Scale, w.PinResults[0].ClearanceLow);
                    Assert.Equal(w0.Result.TriangleCount, w.Result.TriangleCount);
                    Assert.True(w0.Result.VertexIndices().All(v => w.Result.GetVertex(v) == s.Apply(w0.Result.GetVertex(v))), s.Name);
                }
            }
        }

        // PolylineClearance brackets the true minimum, also where the distance dips in a short stretch (a small triangle near a long axis:
        // sampling would miss it, the Lipschitz bound does not), and a crossing gives 0
        [Fact]
        public void PolylineClearance_BracketsTheTrueMinimum()
        {
            var m = new DMesh3();
            // a tiny triangle near x = 37.3, 0.5 above the axis; a big far one
            m.AppendTriangle(m.AppendVertex(new Vector3d(37.29, -0.003, 0.5)), m.AppendVertex(new Vector3d(37.31, -0.003, 0.5)), m.AppendVertex(new Vector3d(37.30, 0.004, 0.5)));
            m.AppendTriangle(m.AppendVertex(new Vector3d(-50, -50, 20)), m.AppendVertex(new Vector3d(50, -50, 20)), m.AppendVertex(new Vector3d(0, 50, 20)));
            var tree = new DMeshAABBTree3(m, true);
            var axis = new List<Vector3d> { new Vector3d(-40, 0, 0), new Vector3d(40, 0, 0) };
            PassageWrap.PolylineClearance(tree, axis, 1e-3, double.MaxValue, out double lo, out double hi);
            Assert.True(lo <= 0.5 && hi >= 0.5 - 1e-9, $"[{lo}, {hi}]");
            Assert.True(hi - lo <= 2e-2, $"[{lo}, {hi}]");  // (the floor 1e-4 of the size, about 0.01 here)
            // a polyline of two legs, the second crossing the big triangle's plane at its interior: the clearance is 0
            var bent = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(0, 0, 30) };
            PassageWrap.PolylineClearance(tree, bent, 1e-3, double.MaxValue, out lo, out hi);
            Assert.True(lo == 0 && hi <= 2e-2, $"[{lo}, {hi}]");
            // a single point is a ball
            PassageWrap.PolylineClearance(tree, new List<Vector3d> { new Vector3d(0, 0, 15) }, 1e-3, double.MaxValue, out lo, out hi);
            Assert.True(lo <= 5 && hi >= 5 - 1e-9 && hi - lo <= 1e-6, $"[{lo}, {hi}]");
        }

        // a pin inside the material: far from every triangle of the wrap too, but not outside it: never a pass (the winding number, the
        // second half of the check; the clearance alone would accept it)
        [Fact]
        public void PinInsideTheMaterial_IsNeverPassed()
        {
            var axis = new List<Vector3d> { new Vector3d(-5, 6, 6), new Vector3d(5, 6, 6) };
            PassageWrap w = Run(Part("cross"), axis, 1.0);
            Assert.Equal(PassageWrap.PassageStatus.PassageNotKept, w.Status);
            Assert.False(w.PinResults[0].AxisOutside);
            Assert.True(w.PinResults[0].ClearanceLow >= 1.0);
            Assert.False(w.PinResults[0].Passed);
        }

        // ---- inputs that are not solids, pinches, the premises ----

        // a triangle soup (no shared vertices) of the cross bore: the same bound
        [Fact]
        public void Soup_PassesAtTheBound()
        {
            PassageWrap w = Run(Soup(Part("cross")), Axis("cross"), 0.5 * BoreR);
            Assert.Equal(PassageWrap.PassageStatus.Success, w.Status);
            Assert.Equal(0, w.FallbackSteps);
            Assert.True(w.BoundPremisesHeld);
        }

        // an open input: the wall of a bore alone, an open tube without caps (the wrap covers both sides of the sheet; the pin is inside)
        [Fact]
        public void OpenTube_PassesAtTheBound_AndTheOutsideIsNotTheInside()
        {
            DMesh3 tube = OpenTube(BoreR, 10, 48);
            var axis = new List<Vector3d> { new Vector3d(0, 0, -14), new Vector3d(0, 0, 14) };
            PassageWrap w = Run(tube, axis, 0.5 * BoreR);
            Assert.True(w.Status == PassageWrap.PassageStatus.Success, w.Status + " " + w.Error);
            Assert.Equal(0, w.FallbackSteps);
            Assert.True(w.PinResults[0].AxisOutside);
            // a pin of the tube's own radius cannot pass: the wrap is a layer around the sheet, the sheet's inside is the tube
            Assert.Equal(PassageWrap.PassageStatus.PinBlockedByInput, Run(tube, axis, 1.01 * BoreR).Status);
        }

        // clutter with many pinches (random soups of triangles around a pin): whatever the manifold step does, a pass is never claimed
        // that the independent sampling refutes, and where the premises held the first wrap at the bound passes (the proof's claim);
        // with and without PullPinchPoints (offset up to twice: the bound uses 2 offsets)
        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(4, true)]
        [InlineData(5, true)]
        public void Clutter_Pinches_NoFalsePass_AndTheBoundHoldsWhereItsPremisesDo(int seed, bool pull)
        {
            DMesh3 clutter = Clutter(seed, out List<Vector3d> axis, out double clearance);
            double pin = 0.4 * clearance;
            PassageWrap w = Run(clutter, axis, pin, x => x.PullPinchPoints = pull);
            Assert.True(w.Status == PassageWrap.PassageStatus.Success || w.Status == PassageWrap.PassageStatus.PassageNotKept, w.Status + " " + w.Error);
            Assert.NotNull(w.Result);
            if (w.AllPassed)
                Assert.True(SampledClearance(w.Result, axis, pin / 20) >= pin - 1e-9, "a claimed pass that the sampling refutes");
            if (w.BoundPremisesHeld && w.FallbackSteps == 0 && w.Status == PassageWrap.PassageStatus.Success)
                Assert.True(w.PinResults[0].ClearanceLow >= Math.Sqrt(Math.Pow(w.PinResults[0].InputClearance - (pull ? 2 : 1) * w.Offset, 2) - w.Alpha * w.Alpha) - 0.5 * w.Offset - 1e-9);
            // (the premises: no rejected points; a failure of the check with the premises held would be a counterexample to the proof)
            Assert.True(!(w.BoundPremisesHeld && w.FirstAttemptFailedCheck), $"the proof's premises held and the check failed (seed {seed}, pull {pull})");
        }

        // random triangles in a box of side 10, and the straight line through it with the largest clearance of 60 tried
        internal static DMesh3 Clutter(int seed, out List<Vector3d> axis, out double clearance)
        {
            var rnd = new Random(1000 + seed);
            var m = new DMesh3();
            for (int t = 0; t < 50; ++t) {
                Vector3d c = new Vector3d(10 * rnd.NextDouble() - 5, 10 * rnd.NextDouble() - 5, 10 * rnd.NextDouble() - 5);
                double sz = 0.8 + 1.5 * rnd.NextDouble();
                Func<Vector3d> j = () => new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                m.AppendTriangle(m.AppendVertex(c + sz * j()), m.AppendVertex(c + sz * j()), m.AppendVertex(c + sz * j()));
            }
            var tree = new DMeshAABBTree3(m, true);
            clearance = -1; axis = null;
            for (int k = 0; k < 60; ++k) {
                Vector3d a = new Vector3d(8 * rnd.NextDouble() - 4, 8 * rnd.NextDouble() - 4, -9), b = new Vector3d(8 * rnd.NextDouble() - 4, 8 * rnd.NextDouble() - 4, 9);
                var line = new List<Vector3d> { a, b };
                PassageWrap.PolylineClearance(tree, line, 1e-3, double.MaxValue, out double lo, out double hi);
                if (lo > clearance) { clearance = lo; axis = line; }
            }
            return m;
        }
    }

    static class PassageWrapTestExtensions
    {
        public static PassageWrap.PassageStatus Run(this PassageWrap w)
        {
            w.Compute();
            return w.Status;
        }
    }
}
