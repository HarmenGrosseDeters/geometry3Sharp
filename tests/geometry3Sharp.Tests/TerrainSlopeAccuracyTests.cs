using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TERRAIN.md 5.5: the horizontal uncertainty of the catch line. Per segment the gradient of S - T on its face and
    /// (Tolerance + TerrainVerticalUncertainty) / gradient + MinFeatureSize; the warning threshold
    /// (IllConditionedCatchLine), tangential contacts, and RequiredCatchLineAccuracy (a smaller Tolerance, or
    /// CatchLineAccuracyNotReached).
    /// </summary>
    public class TerrainSlopeAccuracyTests
    {
        readonly ITestOutputHelper output;
        public TerrainSlopeAccuracyTests(ITestOutputHelper output) { this.output = output; }

        // a square pit at z 0 (side 16), slope 1:ratio, terrain z = h + g x (g = 0: flat)
        static TerrainSlopeDesign Pit(double ratio, double h, double g, double tol = 0.05)
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-80, -80, 80, 80), 64, 64, (x, y) => h + g * x);
            var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(ratio));
            return new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = tol, HeightLimit = 80 };
        }

        [Fact]
        public void FlatTerrain_GradientIsTheSlope()
        {
            // flat terrain, slope 1:1: |grad (S - T)| = s on every segment (the facets are tangent planes of the cone, the
            // built slope s (1 - δ)); the uncertainty (Tolerance) / s + MinFeatureSize
            var d = Pit(1, 6, 0);
            Assert.True(d.Compute(), d.Message);
            var l = Assert.Single(d.CatchLines);
            int segs = l.Closed ? l.Points.Count : l.Points.Count - 1;
            Assert.Equal(segs, l.Gradient.Count);
            Assert.Equal(segs, l.Uncertainty.Count);
            Assert.Equal(segs, l.Tangential.Count);
            foreach (double g in l.Gradient) Assert.InRange(g, 1 - 1e-4, 1 + 1e-9);
            foreach (double u in l.Uncertainty) Assert.InRange(u, d.Tolerance, d.Tolerance / (1 - 1e-4) + d.UsedMinFeatureSize * (1 + 1e-9));
            Assert.Empty(d.IllConditionedCatchLine);
            Assert.Empty(d.TangentialContact);
            Assert.Equal(d.Tolerance, d.UsedTolerance);
        }

        [Fact]
        public void NearlyParallel_IsIllConditioned()
        {
            // the terrain rises with x at 0.8, the slope at 1 (1:1): on the +x side the gradient of S - T is 0.2, five times
            // the uncertainty there; on the -x side 1.8. A threshold between reports the +x segments only
            var d = Pit(1, 3, 0.8);
            d.CatchLineWarningThreshold = 0.2;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            var l = Assert.Single(d.CatchLines);
            Assert.NotEmpty(d.IllConditionedCatchLine);
            foreach (var seg in d.IllConditionedCatchLine) {
                Assert.True(l.Uncertainty[seg.b] > 0.2);
                Assert.True(l.Points[seg.b].x > 8, $"{l.Points[seg.b]}");
            }
            double umax = l.Uncertainty.Max(), umin = l.Uncertainty.Min();
            output.WriteLine($"uncertainty {umin:G3} .. {umax:G3}, ill-conditioned {d.IllConditionedCatchLine.Count} of {l.Uncertainty.Count}");
            Assert.InRange(umax, 0.05 / 0.2 * 0.99, 0.05 / 0.2 * 1.2 + d.UsedMinFeatureSize);
            // (the gradient is at most |grad S| + |grad T| = 1.8; on the y sides sqrt(1 + 0.8^2): well conditioned there)
            Assert.InRange(umin, 0.05 / 1.8 * 0.99, 0.05 / Math.Sqrt(1.64) * 1.2 + d.UsedMinFeatureSize);
        }

        [Fact]
        public void RequiredAccuracy_SmallerTolerance_OrNotReached()
        {
            // nearly parallel on +x (gradient 0.2): Tolerance 0.05 gives 0.25 m there. Required 0.15 m: Tolerance ~ 0.027
            var d = Pit(1, 3, 0.8);
            d.RequiredCatchLineAccuracy = 0.15; d.BuildComposite = true;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            // (the volume of the last round only, as the composite's)
            TerrainSlopeVolumeTests.AssertVolumesMatchComposite(d, false);
            Assert.True(d.UsedTolerance < 0.03 && d.UsedTolerance > 0.01, $"{d.UsedTolerance}");
            Assert.Equal(0.05, d.Tolerance);    // (the caller's input is kept)
            Assert.All(d.CatchLines.SelectMany(l => l.Uncertainty), u => Assert.True(u <= 0.15));
            Assert.NotNull(d.SlopeSurface);
            // the terrain's own uncertainty 0.04 m alone gives 0.2 m on +x: not reachable
            var e = Pit(1, 3, 0.8);
            e.RequiredCatchLineAccuracy = 0.15; e.TerrainVerticalUncertainty = 0.04;
            Assert.False(e.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.CatchLineAccuracyNotReached, e.Status);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.NotCertified, e.StatusKind);
            Assert.Null(e.SlopeSurface);
            // (the catch lines are kept, with their uncertainties)
            Assert.Single(e.CatchLines);
            Assert.Contains(e.CatchLines[0].Uncertainty, u => u > 0.15);
            Assert.Equal(0.05, e.Tolerance);
        }

        [Fact]
        public void FaceGradient_CoplanarIsTangential()
        {
            // a slope triangle and a terrain triangle in one plane: a tangential contact (gradient 0); tilted: the gradient
            Func<double, DMesh3> tri = (tilt) => {
                var m = new DMesh3(false, false, false, true);
                Func<double, double, Vector3d> p = (x, y) => new Vector3d(x, y, 0.5 * x + tilt * y);
                m.AppendTriangle(m.AppendVertex(p(0, 0)), m.AppendVertex(p(10, 0)), m.AppendVertex(p(0, 10)));
                m.SetTriangleGroup(0, 0);
                return m;
            };
            var a = tri(0);
            var b = new DMesh3(false, false, false, true);
            Func<double, double, Vector3d> q = (x, y) => new Vector3d(x, y, 0.5 * x);
            b.AppendTriangle(b.AppendVertex(q(1, 1)), b.AppendVertex(q(8, 1)), b.AppendVertex(q(1, 8)));
            var o = new SlopeTerrainOverlay(a, b, false, new Dictionary<int, int>(), null);
            o.Compute();
            Assert.NotEmpty(o.Faces);
            double g; bool coplanar;
            o.FaceGradient(0, out g, out coplanar);
            Assert.True(coplanar);
            Assert.Equal(0, g);
            var o2 = new SlopeTerrainOverlay(tri(0.25), b, false, new Dictionary<int, int>(), null);
            o2.Compute();
            o2.FaceGradient(0, out g, out coplanar);
            Assert.False(coplanar);
            Assert.Equal(0.25, g, 12);
        }

        [Theory]
        [InlineData(0.0, true)]
        [InlineData(0.01, false)]
        public void CatchLineAlongAPlanarContact_IsTangential(double lift, bool tangential)
        {
            // a slope surface that lies in the terrain plane z = 0 for x <= 5 (lifted by 'lift') and falls to -5 at the toe
            // x = 10: the region is x > 5, and its catch line runs along x = 5, the border of the planar contact (D = 0
            // there counts as no work). Lifted, there is no contact: the segments are not tangential
            var a = new DMesh3(false, false, false, true);
            Func<double, double, double, int> v = (x, y, z) => a.AppendVertex(new Vector3d(x, y, z));
            int p0 = v(0, 0, lift), p1 = v(5, 0, 0), p2 = v(5, 10, 0), p3 = v(0, 10, lift), p4 = v(10, 0, -5), p5 = v(10, 10, -5);
            foreach (var t in new[] { new Index3i(p0, p1, p2), new Index3i(p0, p2, p3), new Index3i(p1, p4, p5), new Index3i(p1, p5, p2) })
                a.AppendTriangle(t, 0);
            var kinds = new Dictionary<int, int>();
            foreach (int e in a.BoundaryEdgeIndices()) {
                Index2i ev = a.GetEdgeV(e);
                kinds[e] = (a.GetVertex(ev.a).x == 10 && a.GetVertex(ev.b).x == 10) ? SlopeTerrainOverlay.KindToe : SlopeTerrainOverlay.KindOther;
            }
            var b = new DMesh3(false, false, false, true);
            int q0 = b.AppendVertex(new Vector3d(-1, -1, 0)), q1 = b.AppendVertex(new Vector3d(11, -1, 0));
            int q2 = b.AppendVertex(new Vector3d(11, 11, 0)), q3 = b.AppendVertex(new Vector3d(-1, 11, 0));
            b.AppendTriangle(q0, q1, q2); b.AppendTriangle(q0, q2, q3);
            var o = new SlopeTerrainOverlay(a, b, false, kinds, null);
            Assert.True(o.Compute(), o.Problem);
            Assert.NotEmpty(o.CatchLines);
            int segs = 0, marked = 0;
            for (int li = 0; li < o.CatchLines.Count; ++li)
                foreach (int f in o.CatchFaces[li]) {
                    double g; bool tg;
                    o.SegmentContact(f, out g, out tg);
                    segs++;
                    if (tg) marked++;
                    Assert.True(g > 0);
                }
            Assert.True(segs > 0);
            if (tangential) Assert.Equal(segs, marked); else Assert.Equal(0, marked);
        }

        [Fact]
        public void RequiredAccuracy_ASmallerToleranceThatFails_IsNotReached()
        {
            // required 1e-7 m: the next Tolerance (about 1e-8) is below the numerical margins and its build fails; that is
            // the accuracy not reached (NotCertified, the catch lines of the previous round kept), not an input error
            var d = Pit(1, 3, 0.8);
            d.RequiredCatchLineAccuracy = 1e-7;
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.CatchLineAccuracyNotReached, d.Status);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.NotCertified, d.StatusKind);
            Assert.NotEmpty(d.CatchLines);
            Assert.All(d.CatchLines, l => Assert.Equal(l.Gradient.Count, l.Uncertainty.Count));
            Assert.Equal(0, d.Volume);
            Assert.Null(d.SlopeSurface);
            Assert.Equal(0.05, d.Tolerance);
        }

        [Fact]
        public void AccuracyInputs_AreValidated()
        {
            foreach (Action<TerrainSlopeDesign> bad in new Action<TerrainSlopeDesign>[] {
                x => x.TerrainVerticalUncertainty = -1, x => x.TerrainVerticalUncertainty = double.NaN,
                x => x.RequiredCatchLineAccuracy = 0, x => x.RequiredCatchLineAccuracy = double.PositiveInfinity,
                x => x.CatchLineWarningThreshold = 0, x => x.CatchLineWarningThreshold = double.NaN }) {
                var d = Pit(1, 6, 0);
                bad(d);
                Assert.False(d.Compute());
                Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            }
        }
    }
}
