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
            d.RequiredCatchLineAccuracy = 0.15;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
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
    }
}
