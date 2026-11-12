using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #738: at coordinates far beyond the meshes' extent a subdivision midpoint is not representable. At z = 1.3 2^52 (an ulp is 1)
    /// two spheres of radius 1 and 1.01 are folded onto the levels z = -1, 0, 1, and the midpoint of z = -1 and 0 rounded onto a corner:
    /// a child triangle was its parent, refinement ran to MaxSubdivisions (minutes, GBs), and the rounded points, off the triangles by
    /// up to 0.5, gave a "certified" lower bound of 0.18 for a distance of 0.0099. Now (a) such meshes are measured on copies moved
    /// near 0 by an exact shift, (b) a triangle that cannot be split, or whose points may be off by more than the tolerance, is not
    /// refined further (its bound is kept), and (c) the bounds account for the rounding of subdivision points, so a certified lower
    /// bound never exceeds the distance. The reference is the same meshes shifted by the offset, exactly (Sterbenz: every z lies in
    /// [offset / 2, 2 offset]), measured near 0.
    /// </summary>
    public class HausdorffRoundedMidpointTests
    {
        static DMesh3 Sphere(double r, double z)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = 6, Radius = 1 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, r * m.GetVertex(v) + new Vector3d(0, 0, z));
            return m;
        }

        static DMesh3 Shifted(DMesh3 m, double z)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices().ToList()) c.SetVertex(v, c.GetVertex(v) - new Vector3d(0, 0, z));
            return c;
        }

        // the test hook (absent on the base, where the meshes are always measured as given)
        static void AsGiven(MeshHausdorffDistance h)
        {
            FieldInfo f = typeof(MeshHausdorffDistance).GetField("AsGivenForTests", BindingFlags.NonPublic | BindingFlags.Instance);
            f?.SetValue(h, true);
        }

        static void AssertValid(MeshHausdorffDistance h, MeshHausdorffDistance reference, string what)
        {
            Assert.True(reference.Converged, what + ": reference");
            Assert.False(double.IsNaN(h.LowerBound) || double.IsNaN(h.UpperBound), what);
            // both intervals contain the distance: the certified lower bound is not above the reference's upper bound, the upper
            // bound not below its lower bound
            Assert.True(h.LowerBound <= reference.UpperBound, what + ": lower " + h.LowerBound + " above the distance " + reference.UpperBound);
            Assert.True(h.UpperBound >= reference.LowerBound, what + ": upper " + h.UpperBound + " below the distance " + reference.LowerBound);
            Assert.True(h.LowerBound >= 0 && h.LowerBound <= h.UpperBound, what);
        }

        public static TheoryData<double> Offsets() => new TheoryData<double> {
            1.3 * Math.Pow(2, 52), 1.7 * Math.Pow(2, 50), 1.1 * Math.Pow(2, 40), 1.5 * Math.Pow(2, 26), 1.5 * Math.Pow(2, 24), -1.3 * Math.Pow(2, 52) };

        /// <summary> the folded spheres converge, fast, to the result of the exactly shifted meshes (base: 0.18 .. 0.31 after
        /// minutes at 1.3 2^52) </summary>
        [Theory]
        [MemberData(nameof(Offsets))]
        public void FoldedSpheres_AtLargeOffsets_ConvergeToTheShiftedResult(double z)
        {
            DMesh3 a = Sphere(1, z), b = Sphere(1.01, z);
            var reference = new MeshHausdorffDistance(Shifted(a, z), Shifted(b, z));
            Assert.True(reference.Compute());
            var sw = Stopwatch.StartNew();
            var h = new MeshHausdorffDistance(a, b) { MaxSubdivisions = 2000000 };
            Assert.True(h.Compute());
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 3000, "took " + sw.ElapsedMilliseconds + " ms, " + h.Subdivisions + " subdivisions");
            Assert.True(h.Converged, "not converged: " + h.LowerBound + " .. " + h.UpperBound);
            AssertValid(h, reference, "z = " + z);
            Assert.True(h.UpperBound - h.LowerBound <= 1e-4 * 2.01 * Math.Sqrt(3) * 1.0001, "gap " + (h.UpperBound - h.LowerBound));
            // (the worst point is back at the meshes' coordinates, in their bounding box up to a rounding)
            Assert.InRange(h.WorstPoint.z, z - 1.01 - 2 * Math.Abs(z) * 1.2e-16, z + 1.01 + 2 * Math.Abs(z) * 1.2e-16);
        }

        /// <summary> the certified lower bound never exceeds the distance, also measured as given (the bounds' own accounting of
        /// rounded subdivision points, without the shifted copies), and that run ends after few subdivisions, unconverged:
        /// the folded points are off the triangles by up to 0.5, beyond the tolerance </summary>
        [Theory]
        [MemberData(nameof(Offsets))]
        public void CertifiedLowerBound_NeverAboveTheDistance(double z)
        {
            DMesh3 a = Sphere(1, z), b = Sphere(1.01, z);
            var reference = new MeshHausdorffDistance(Shifted(a, z), Shifted(b, z));
            Assert.True(reference.Compute());
            foreach (bool symmetric in new[] { true, false }) {
                var given = new MeshHausdorffDistance(a, b) { Symmetric = symmetric, MaxSubdivisions = 200000 };
                AsGiven(given);
                var sw = Stopwatch.StartNew();
                Assert.True(given.Compute());
                Assert.True(sw.ElapsedMilliseconds < 3000, "as given took " + sw.ElapsedMilliseconds + " ms");
                Assert.True(given.Subdivisions < 50000, "as given: " + given.Subdivisions + " subdivisions");
                var oneSidedRef = symmetric ? reference : new MeshHausdorffDistance(Shifted(a, z), Shifted(b, z)) { Symmetric = false };
                if (!symmetric) Assert.True(oneSidedRef.Compute());
                AssertValid(given, oneSidedRef, "as given, z = " + z + ", symmetric " + symmetric);
            }
        }

        /// <summary> under every similarity (folded spheres at 1.5 2^26, on the shifted copies): the result of the plain input mapped by
        /// it, bit for bit where the similarity is exact; otherwise the intervals agree up to the coordinates' rounding </summary>
        [Fact]
        public void FoldedSpheres_UnderEverySimilarity()
        {
            double z = 1.5 * Math.Pow(2, 26);
            var h0 = new MeshHausdorffDistance(Sphere(1, z), Sphere(1.01, z));
            Assert.True(h0.Compute() && h0.Converged);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 a = Sphere(1, z), b = Sphere(1.01, z);
                MeshTransforms.PerVertexTransform(a, s.Apply);
                MeshTransforms.PerVertexTransform(b, s.Apply);
                var h = new MeshHausdorffDistance(a, b);
                Assert.True(h.Compute(), s.Name);
                Assert.True(h.Converged, s.Name);
                if (s.IsExact) {
                    Assert.Equal(h0.LowerBound, h.LowerBound / s.Scale);
                    Assert.Equal(h0.UpperBound, h.UpperBound / s.Scale);
                    Assert.Equal(h0.Subdivisions, h.Subdivisions);
                } else {
                    // (the coordinates near 1e8 round by up to 1.5e-8 in the mapped frame)
                    double eps = 1e-6;
                    Assert.True(h.LowerBound / s.Scale <= h0.UpperBound + eps && h0.LowerBound <= h.UpperBound / s.Scale + eps, s.Name);
                }
            }
        }

        /// <summary> on the shifted path: computing twice on one instance, a fresh instance and a serial run give the same result </summary>
        [Fact]
        public void ShiftedPath_Reuse_Serial_Same()
        {
            double z = 1.3 * Math.Pow(2, 52);
            DMesh3 a = Sphere(1, z), b = Sphere(1.01, z);
            var h = new MeshHausdorffDistance(a, b);
            Assert.True(h.Compute());
            double lo = h.LowerBound, up = h.UpperBound; int sub = h.Subdivisions; Vector3d w = h.WorstPoint;
            Assert.True(h.Compute());
            Assert.Equal(lo, h.LowerBound); Assert.Equal(up, h.UpperBound); Assert.Equal(sub, h.Subdivisions); Assert.Equal(w, h.WorstPoint);
            var fresh = new MeshHausdorffDistance(a, b);
            Assert.True(fresh.Compute());
            Assert.Equal(lo, fresh.LowerBound); Assert.Equal(up, fresh.UpperBound);
            gParallel.SerialOnThisThread = true;
            try {
                var serial = new MeshHausdorffDistance(a, b);
                Assert.True(serial.Compute());
                Assert.Equal(lo, serial.LowerBound); Assert.Equal(up, serial.UpperBound); Assert.Equal(sub, serial.Subdivisions);
            } finally { gParallel.SerialOnThisThread = false; }
        }
    }
}
