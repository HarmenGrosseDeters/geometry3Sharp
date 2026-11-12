using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #731: MeshHausdorffDistance squared lengths at the meshes' own scale. For two spheres of radii s and 1.01 s (distance about
    /// 0.01 s) the upper bound was double.MaxValue from s of about 1e120, the queries threw from about 1e155 (#727 refused that), and at
    /// s = 1e-200 both bounds were 0, below the distance. Meshes with an extent outside [2^-32, 2^65) are now measured on copies scaled by
    /// an exact power of two: at 2^k times the meshes the result is 2^k times the result, bit for bit, at every k the doubles reach.
    /// </summary>
    public class HausdorffScaleTests
    {
        static DMesh3 Sphere(double scale, Vector3d offset = default)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = 6, Radius = 1 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, scale * m.GetVertex(v) + offset);
            return m;
        }

        static MeshHausdorffDistance Measure(double s, Vector3d offset = default, bool symmetric = true)
        {
            var h = new MeshHausdorffDistance(Sphere(s, offset), Sphere(1.01 * s, offset)) { Symmetric = symmetric };
            Assert.True(h.Compute(), "s = " + s);
            Assert.True(h.Converged, "s = " + s);
            Assert.False(double.IsNaN(h.LowerBound) || double.IsInfinity(h.LowerBound) || double.IsNaN(h.UpperBound) || double.IsInfinity(h.UpperBound),
                "s = " + s + ": " + h.LowerBound + " " + h.UpperBound);
            Assert.True(0 <= h.LowerBound && h.LowerBound <= h.UpperBound, "s = " + s + ": " + h.LowerBound + " " + h.UpperBound);
            return h;
        }

        /// <summary> every 2^k scale gives the unit result times 2^k, bit for bit: bounds, worst point, subdivisions (k = 1020: radius
        /// 1.01 2^1020 is about 1.1e307) </summary>
        [Theory]
        [InlineData(-1000)] [InlineData(-700)] [InlineData(-500)] [InlineData(-300)] [InlineData(-100)] [InlineData(-40)]
        [InlineData(-34)] [InlineData(-33)] [InlineData(-31)] [InlineData(63)] [InlineData(64)] [InlineData(65)] [InlineData(100)]
        [InlineData(300)] [InlineData(500)] [InlineData(700)] [InlineData(1000)] [InlineData(1020)]
        public void Spheres_AtEveryPowerOfTwoScale_AreTheUnitResultScaled(int k)
        {
            double s = Math.Pow(2, k);
            foreach (bool symmetric in new[] { true, false }) {
                MeshHausdorffDistance unit = Measure(1, default, symmetric), h = Measure(s, default, symmetric);
                Assert.Equal(unit.LowerBound * s, h.LowerBound);
                Assert.Equal(unit.UpperBound * s, h.UpperBound);
                Assert.Equal(unit.WorstPoint * s, h.WorstPoint);
                Assert.Equal(unit.WorstPointClosest * s, h.WorstPointClosest);
                Assert.Equal(unit.WorstPointOnB, h.WorstPointOnB);
                Assert.Equal(unit.Subdivisions, h.Subdivisions);
            }
        }

        /// <summary> decimal scales (the coordinates round differently): the distance 0.01 s within the default relative tolerance and
        /// the faces' discretization; 1e-200 gave 0 and 0, 1e130 an upper bound of double.MaxValue </summary>
        [Theory]
        [InlineData(1e-300)] [InlineData(1e-200)] [InlineData(1e-160)] [InlineData(1e-12)] [InlineData(1e130)] [InlineData(1e150)]
        [InlineData(1e200)] [InlineData(1e300)]
        public void Spheres_AtExtremeScales_Measure(double s)
        {
            var h = Measure(s);
            Assert.InRange(h.LowerBound, 0.0099 * s, 0.0101 * s);
            Assert.InRange(h.UpperBound, 0.0099 * s, 0.0105 * s);
        }

        /// <summary> coordinates far larger than the extent: a sphere of radius 1e-100 at 1e-90 is scaled up (2^332) and measured </summary>
        [Fact]
        public void LargeOffsets_SmallExtent_Measured()
        {
            var h = Measure(1e-100, new Vector3d(1e-90, -1e-90, 1e-90));
            Assert.InRange(h.LowerBound, 0.0099e-100, 0.0101e-100);
            Assert.InRange(h.UpperBound, 0.0099e-100, 0.0105e-100);
        }

        /// <summary> spheres of radius r at z = 1e300 are flat (an ulp of 1e300 is 1.5e284): two discs, whose distance is 0.01 r in the
        /// plane. Scaled to extent 1, z would overflow; the flat axis is moved to 0 on the copies and back for the worst points
        /// (review of #735: a refusal there was a regression for 2^-35 .. 2^-45, which the old code measured) </summary>
        [Theory]
        [InlineData(-35)] [InlineData(-40)] [InlineData(-45)] [InlineData(-830)]
        public void FlatAtHugeCoordinates_Measured(int k)
        {
            double r = Math.Pow(2, k);
            var off = new Vector3d(0, 0, 1e300);
            var h = Measure(r, off);
            Assert.InRange(h.LowerBound, 0.0099 * r, 0.0101 * r);
            Assert.InRange(h.UpperBound, 0.0099 * r, 0.0105 * r);
            Assert.Equal(1e300, h.WorstPoint.z);
            Assert.Equal(1e300, h.WorstPointClosest.z);
        }

        /// <summary> a vertex no triangle uses does not set the scale: with one far away (1e100 gave an upper bound of 23 times the
        /// distance after 0 subdivisions, the relative tolerance taken from it; from 1e160 the scaled triangles underflowed and the
        /// certified bounds were 0 and 0) the result is the one without it, bit for bit (review of #735) </summary>
        [Theory]
        [InlineData(1e100)] [InlineData(1e200)] [InlineData(1e300)] [InlineData(-1.7e308)]
        public void FarUnusedVertex_DoesNotSetTheScale(double far)
        {
            foreach (double s in new[] { 1.0, Math.Pow(2, -300) }) {
                MeshHausdorffDistance plain = Measure(s);
                DMesh3 a = Sphere(s), b = Sphere(1.01 * s);
                a.AppendVertex(new Vector3d(far, 0, 0));
                b.AppendVertex(new Vector3d(0, far, -far));
                var h = new MeshHausdorffDistance(a, b);
                Assert.True(h.Compute());
                Assert.Equal(plain.LowerBound, h.LowerBound);
                Assert.Equal(plain.UpperBound, h.UpperBound);
                Assert.Equal(plain.Subdivisions, h.Subdivisions);
                Assert.Equal(plain.WorstPoint, h.WorstPoint);
            }
        }

        /// <summary> meshes near opposite ends of the double range: their distance (3.4e308) exceeds double.MaxValue and has no bounds
        /// in doubles; refused, the outputs NaN (scaled back, the lower bound was +infinity above an upper bound MaxValue). Just
        /// below MaxValue it is measured </summary>
        [Fact]
        public void DistanceBeyondMaxValue_Refuses()
        {
            var far = new MeshHausdorffDistance(Sphere(1e306, new Vector3d(-1.7e308, 0, 0)), Sphere(1e306, new Vector3d(1.7e308, 0, 0)));
            Assert.False(far.Compute());
            Assert.True(double.IsNaN(far.LowerBound) && double.IsNaN(far.UpperBound));
            Assert.False(far.Converged);
            var near = new MeshHausdorffDistance(Sphere(1e306, new Vector3d(-0.8e308, 0, 0)), Sphere(1e306, new Vector3d(0.8e308, 0, 0)));
            Assert.True(near.Compute());
            Assert.InRange(near.LowerBound, 1.59e308, 1.61e308);
            Assert.True(near.UpperBound >= near.LowerBound && !double.IsInfinity(near.UpperBound));
        }

        /// <summary> the scaled path neither uses nor sets SpatialA / SpatialB (trees of the unscaled meshes); one instance computed on
        /// a plain extent, then on a scaled one, then plain again gives the results of fresh instances </summary>
        [Fact]
        public void ScaledPath_SpatialUntouched_ReuseAcrossPaths()
        {
            double s = Math.Pow(2, -400);
            MeshHausdorffDistance plain = Measure(1), small = Measure(s);
            Assert.Null(small.SpatialA);
            Assert.Null(small.SpatialB);
            DMesh3 ua = Sphere(1), ub = Sphere(1.01);
            var h = new MeshHausdorffDistance(ua, ub);
            Assert.True(h.Compute());
            DMeshAABBTree3 ta = h.SpatialA, tb = h.SpatialB;
            Assert.NotNull(ta);
            h.MeshA = Sphere(s); h.MeshB = Sphere(1.01 * s);
            Assert.True(h.Compute());
            Assert.Equal(small.LowerBound, h.LowerBound); Assert.Equal(small.UpperBound, h.UpperBound); Assert.Equal(small.Subdivisions, h.Subdivisions);
            Assert.Same(ta, h.SpatialA); Assert.Same(tb, h.SpatialB);
            h.MeshA = ua; h.MeshB = ub;
            Assert.True(h.Compute());
            Assert.Equal(plain.LowerBound, h.LowerBound); Assert.Equal(plain.UpperBound, h.UpperBound); Assert.Equal(plain.Subdivisions, h.Subdivisions);
        }

        /// <summary> spheres of radius 2^k with subnormal coordinates (multiples of 2^-1074): the bounds, scaled back into the
        /// subnormal range, round outward, so they contain the result of the same meshes scaled up exactly (rounded to nearest,
        /// a lower bound could round up past it; review of #735) </summary>
        [Theory]
        [InlineData(-1056)] [InlineData(-1060)] [InlineData(-1063)] [InlineData(-1066)]
        public void SubnormalExtremes_BoundsRoundOutward(int k)
        {
            double s = Math.Pow(2, k), back = Math.Pow(2, -k - 600);
            DMesh3 a = Sphere(s), b = Sphere(1.01 * s);
            DMesh3 ua = new DMesh3(a), ub = new DMesh3(b);
            // (exact: every coordinate is a multiple of 2^-1074; in two factors, 2^-k alone overflows)
            foreach (var m in new[] { ua, ub }) foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, m.GetVertex(v) * back * Math.Pow(2, 600));
            foreach (bool symmetric in new[] { true, false }) {
                var reference = new MeshHausdorffDistance(ua, ub) { Symmetric = symmetric };
                var h = new MeshHausdorffDistance(a, b) { Symmetric = symmetric };
                Assert.True(reference.Compute() && h.Compute());
                Assert.True(h.LowerBound * back * Math.Pow(2, 600) <= reference.LowerBound, k + ": lower " + h.LowerBound + " above " + reference.LowerBound);
                Assert.True(h.UpperBound * back * Math.Pow(2, 600) >= reference.UpperBound, k + ": upper " + h.UpperBound + " below " + reference.UpperBound);
                Assert.True(h.LowerBound > 0 && h.LowerBound <= h.UpperBound);
            }
        }

        /// <summary> two parallel triangles one smallest subnormal (2^-1074) apart, whose spans are 2^-1074: their distance is
        /// 2^-1074 (half the span rounded to 0, the meshes stayed unscaled, and both bounds were 0; review of #735) </summary>
        [Fact]
        public void SpanOfTheSmallestSubnormal_IsMeasured()
        {
            double u = double.Epsilon;
            var a = new DMesh3(); a.AppendTriangle(a.AppendVertex(new Vector3d(0, 0, 0)), a.AppendVertex(new Vector3d(u, 0, 0)), a.AppendVertex(new Vector3d(0, u, 0)));
            var b = new DMesh3(); b.AppendTriangle(b.AppendVertex(new Vector3d(0, 0, u)), b.AppendVertex(new Vector3d(u, 0, u)), b.AppendVertex(new Vector3d(0, u, u)));
            var h = new MeshHausdorffDistance(a, b);
            Assert.True(h.Compute());
            Assert.True(h.LowerBound <= u && h.UpperBound >= u, h.LowerBound + " .. " + h.UpperBound);
            Assert.True(h.UpperBound > 0);
        }

        /// <summary> an absolute Tolerance is scaled with the meshes: the same result as the unit spheres with the unit tolerance </summary>
        [Fact]
        public void AbsoluteTolerance_ScalesWithTheMeshes()
        {
            double s = Math.Pow(2, -600);
            var unit = new MeshHausdorffDistance(Sphere(1), Sphere(1.01)) { Tolerance = 1e-3 };
            var h = new MeshHausdorffDistance(Sphere(s), Sphere(1.01 * s)) { Tolerance = 1e-3 * s };
            Assert.True(unit.Compute() && h.Compute());
            Assert.Equal(unit.LowerBound * s, h.LowerBound);
            Assert.Equal(unit.UpperBound * s, h.UpperBound);
            Assert.Equal(unit.Subdivisions, h.Subdivisions);
            Assert.True(h.UpperBound - h.LowerBound <= 1e-3 * s);
        }

        /// <summary> on the scaled path: computing twice on one instance, a fresh instance, a serial run and repeated runs give the same
        /// result (the parallel queries write per index) </summary>
        [Fact]
        public void ScaledPath_Reuse_Serial_Repeated_Same()
        {
            double s = Math.Pow(2, 300);
            var h = new MeshHausdorffDistance(Sphere(s), Sphere(1.01 * s));
            Assert.True(h.Compute());
            double lo = h.LowerBound, up = h.UpperBound; int sub = h.Subdivisions; Vector3d w = h.WorstPoint;
            Assert.True(h.Compute());
            Assert.Equal(lo, h.LowerBound); Assert.Equal(up, h.UpperBound); Assert.Equal(sub, h.Subdivisions); Assert.Equal(w, h.WorstPoint);
            for (int r = 0; r < 3; ++r) {
                var f = Measure(s);
                Assert.Equal(lo, f.LowerBound); Assert.Equal(up, f.UpperBound); Assert.Equal(sub, f.Subdivisions);
            }
            gParallel.SerialOnThisThread = true;
            try {
                var serial = Measure(s);
                Assert.Equal(lo, serial.LowerBound); Assert.Equal(up, serial.UpperBound); Assert.Equal(sub, serial.Subdivisions);
                Assert.Equal(w, serial.WorstPoint);
            } finally { gParallel.SerialOnThisThread = false; }
        }

        /// <summary> under every similarity at a scaled extent (spheres of radius 2^-200): the result of the unscaled spheres mapped by
        /// it, bit for bit where it is exact, otherwise both intervals contain the distance </summary>
        [Fact]
        public void ScaledPath_UnderEverySimilarity()
        {
            double t = Math.Pow(2, -200);
            var h0 = Measure(t);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 a = Sphere(t), b = Sphere(1.01 * t);
                MeshTransforms.PerVertexTransform(a, p => s.Apply(p / t) * t);
                MeshTransforms.PerVertexTransform(b, p => s.Apply(p / t) * t);
                var h = new MeshHausdorffDistance(a, b);
                Assert.True(h.Compute(), s.Name);
                if (s.IsExact) {
                    Assert.Equal(h0.LowerBound, h.LowerBound / s.Scale);
                    Assert.Equal(h0.UpperBound, h.UpperBound / s.Scale);
                } else {
                    double eps = 100 * s.Resolution(2) * t;
                    Assert.True(h.LowerBound / s.Scale <= h0.UpperBound + eps && h0.LowerBound <= h.UpperBound / s.Scale + eps, s.Name);
                }
            }
        }
    }
}
