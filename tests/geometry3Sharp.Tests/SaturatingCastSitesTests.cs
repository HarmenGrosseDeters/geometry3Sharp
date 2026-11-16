using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue 804, the casts of a double to an integer type outside the exact scope that the audit
    /// (docs/design/DETERMINISM_PROOF.md section 5.3) moved to MathUtil.ToIntSaturating / FloorToIntSaturating /
    /// FloorToLongSaturating: NaN gives 0 and an out-of-range value saturates, the result of .NET 9 and later, on every
    /// runtime. Each test feeds NaN, an infinity or 1e300 to a site and checks the stated result. On .NET 9 and later the
    /// plain casts give these results already, so the tests decide on .NET Framework (the net48 test job), where the old
    /// casts gave int.MinValue / long.MinValue.
    /// </summary>
    public class SaturatingCastSitesTests
    {
        [Fact]
        public void SnapToIncrement_HugeValue_SaturatesTheIncrementCount()
        {
            // (nInc = (int)(1e300 / 1): int.MinValue on net48, giving -2147483648 instead of +2147483647)
            Assert.Equal(2147483647.0, Snapping.SnapToIncrement(1e300, 1.0));
            Assert.Equal(-2147483647.0, Snapping.SnapToIncrement(-1e300, 1.0));
            Assert.Equal(0.0, Snapping.SnapToIncrement(double.NaN, 1.0));
            Assert.Equal(0.0, Snapping.SnapToIncrement(double.PositiveInfinity, 1.0));
        }

        [Fact]
        public void Vector3i_FromVector3d_NaNInfinityAndHuge()
        {
            Vector3i v = (Vector3i)new Vector3d(double.NaN, double.PositiveInfinity, -1e300);
            Assert.Equal(new Vector3i(0, int.MaxValue, int.MinValue), v);
            Vector3i f = (Vector3i)new Vector3f(float.NaN, float.NegativeInfinity, 1e30f);
            Assert.Equal(new Vector3i(0, int.MinValue, int.MaxValue), f);
        }

        [Fact]
        public void IntegerVectors_LengthOfAnOverflowedSquare_IsTheSameOnEveryRuntime()
        {
            // (LengthSquared wraps to a negative int, its square root is NaN: the length is 0, not int.MinValue)
            var v3 = new Index3i(46341, 0, 0);
            Assert.True(v3.LengthSquared < 0);
            Assert.Equal(0, v3.Length);
            var v2 = new Index2i(46341, 0);
            Assert.True(v2.LengthSquared < 0);
            Assert.Equal(0, v2.Length);
        }

        [Fact]
        public void Colorb_FromFloats_NaNAndInfinityClampToTheByteRange()
        {
            // (+Inf: 255 on net10, but 0 on net48 with the plain cast, int.MinValue clamped to 0)
            Colorb c = new Colorb(float.PositiveInfinity, float.NaN, float.NegativeInfinity, 1e30f);
            Assert.Equal(255, c.r);
            Assert.Equal(0, c.g);
            Assert.Equal(0, c.b);
            Assert.Equal(255, c.a);
        }

        [Fact]
        public void ColorHSV_NaNAndInfiniteHue_DoNotThrow()
        {
            foreach (float h in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1e30f }) {
                Colorf c = new ColorHSV(h, 1.0f, 1.0f).ConvertToRGB();
                Assert.False(float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b));
            }
        }

        [Fact]
        public void PolyLineAndPolygonCurves_SampleT_NaNInfinityAndHuge()
        {
            var line = new PolyLine2DCurve() { Polyline = new PolyLine2d(new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(1, 1) }) };
            Assert.Equal(new Vector2d(1, 1), line.SampleT(1e300));
            Assert.Equal(new Vector2d(1, 1), line.SampleT(double.PositiveInfinity));
            Assert.Equal(new Vector2d(0, 0), line.SampleT(-1e300));
            Assert.Equal(new Vector2d(0, 0), line.SampleT(double.NegativeInfinity));
            // (NaN: edge 0 on every runtime, a NaN point; net48 gave the first vertex)
            Vector2d p = line.SampleT(double.NaN);
            Assert.True(double.IsNaN(p.x) && double.IsNaN(p.y));
            Vector2d t = line.TangentT(double.NaN);
            Assert.Equal(new Vector2d(1, 0), t);
            Assert.Equal(new Vector2d(0, 1), line.TangentT(1e300));
            Assert.Equal(new Vector2d(1, 0), line.TangentT(-1e300));

            var poly = new Polygon2DCurve() { Polygon = new Polygon2d(new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(1, 1) }) };
            Assert.Equal(new Vector2d(0, 0), poly.SampleT(1e300));
            Assert.Equal(new Vector2d(0, 0), poly.SampleT(-1e300));
            Vector2d q = poly.SampleT(double.NaN);
            Assert.True(double.IsNaN(q.x) && double.IsNaN(q.y));
            Assert.Equal(new Vector2d(1, 0), poly.TangentT(double.NaN));
            Assert.Equal(new Vector2d(-1, -1).Normalized, poly.TangentT(1e300));
        }

        [Fact]
        public void LineGenerator_InfiniteLength_ClampsTheStepCount()
        {
            // (the step count (int)(Inf / step): int.MinValue on net48, clamped to 2 instead of 10000)
            var gen = new LineGenerator() { Start = Vector3d.Zero, End = new Vector3d(double.PositiveInfinity, 0, 0), StepSize = 1.0, Subdivisions = 0 };
            gen.Generate();
            Assert.Equal(10001, gen.vertices.Count);
        }


        [Fact]
        public void DenseGridTrilinearImplicit_NaNPoint_GivesNaNNotAnIndexError()
        {
            // (a NaN passes the range test; its cell is 0 on every runtime, int.MinValue on net48 with the plain cast: an index error)
            var grid = new DenseGrid3f(3, 3, 3, 0.0f);
            var f = new DenseGridTrilinearImplicit(grid, Vector3d.Zero, 1.0);
            Vector3d nan = new Vector3d(double.NaN, 1.0, 1.0);
            Assert.True(double.IsNaN(f.Value(ref nan)));
            Vector3d inf = new Vector3d(1.0, double.PositiveInfinity, 1.0);
            Assert.Equal(f.Outside, f.Value(ref inf));
            Vector3d huge = new Vector3d(1e300, 1.0, 1.0);
            Assert.Equal(f.Outside, f.Value(ref huge));
        }

        [Fact]
        public void CachingDenseGridTrilinearImplicit_NaNPoint_DoesNotThrow()
        {
            var f = new CachingDenseGridTrilinearImplicit(Vector3d.Zero, 1.0, new Vector3i(3, 3, 3)) { AnalyticF = new ImplicitSphere3d() { Origin = new Vector3d(1, 1, 1), Radius = 5 } };
            Vector3d nan = new Vector3d(double.NaN, 1.0, 1.0);
            Assert.True(double.IsNaN(f.Value(ref nan)));
            Vector3d huge = new Vector3d(1e300, 1.0, 1.0);
            Assert.Equal(f.Outside, f.Value(ref huge));
        }

        [Fact]
        public void DijkstraGraphDistance_NaNSeedId_IsNodeZero()
        {
            // (the seed ids are the x components of Vector2d pairs: NaN gives node 0 on every runtime, int.MinValue on net48)
            var seeds = new[] { new Vector2d(double.NaN, 0.0) };
            var d = new DijkstraGraphDistance(4, false, (id) => true, (a, b) => 1.0f, (id) => new int[0], seeds);
            Assert.True(d.IsSeed(0));
        }
    }
}
