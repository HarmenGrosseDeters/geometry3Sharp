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

        /// <summary> coordinates far larger than the extent: a sphere of radius 1e-100 at 1e-90 is scaled up (2^332) and measured; the
        /// same at radius 1e-250 is flat along z (an ulp of 1e300 is 1e284) and would overflow when scaled to extent 1, so it is
        /// refused, with the outputs NaN </summary>
        [Fact]
        public void LargeOffsets_SmallExtent_MeasuredOrRefused()
        {
            var h = Measure(1e-100, new Vector3d(1e-90, -1e-90, 1e-90));
            Assert.InRange(h.LowerBound, 0.0099e-100, 0.0101e-100);
            var flat = new MeshHausdorffDistance(Sphere(1e-250, new Vector3d(0, 0, 1e300)), Sphere(1.01e-250, new Vector3d(0, 0, 1e300)));
            Assert.False(flat.Compute());
            Assert.True(double.IsNaN(flat.LowerBound) && double.IsNaN(flat.UpperBound));
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
