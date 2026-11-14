using System;
using System.Linq;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #742: MeshHausdorffDistance.CertifiedUpper. Without it UpperBound is the floating-point model of the distance queries, whose
    /// squared-distance form cancels: near the surface (a distance d much below the size E of the nearest triangle) it is off by
    /// about 2^-53 E^2 / d, up to 2^-26 E, and can be below the exact distance. With it every bound holds under rounding (outward-rounded
    /// distances to points of the triangles; the float cascade's pieces re-evaluated so). Checked against exact integer distances
    /// (CertifiedPointMeshDistance.AtLeastDistance). docs/design/HAUSDORFF_CERTIFIED_UPPER.md
    /// </summary>
    public class HausdorffCertifiedUpperTests
    {
        static DMesh3 Tri(Vector3d a, Vector3d b, Vector3d c)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(b), m.AppendVertex(c));
            return m;
        }

        static DMesh3 Sphere(int ev, double r, Vector3d off)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = ev, Radius = 1 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, r * m.GetVertex(v) + off);
            return m;
        }

        static DMesh3 Bumpy(int seed, int ev)
        {
            var rnd = new Random(seed);
            DMesh3 m = Sphere(ev, 1, Vector3d.Zero);
            foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, m.GetVertex(v) * (1 + 0.05 * rnd.NextDouble()));
            return m;
        }

        // every vertex of from is within u of to, exactly
        static bool VerticesWithin(DMesh3 from, DMesh3 to, double u)
            => from.VertexIndices().All(v => CertifiedPointMeshDistance.AtLeastDistance(to, from.GetVertex(v), u));

        /// <summary> a small triangle 1e-9 above the interior of a tilted unit triangle: the default upper bound is below the exact
        /// distance of a corner in many cases (the float distance is 0 or 1e-8 off there), the certified one never; at offsets
        /// 0, 1e3, UTM coordinates </summary>
        [Fact]
        public void NearSurface_CertifiedUpper_NeverBelowTheExactDistance()
        {
            var rnd = new Random(1);
            int below0 = 0, n = 0;
            foreach (double off in new[] { 0.0, 1e3, 5.7e6 })
                for (int it = 0; it < 60; ++it) {
                    Vector3d v0 = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()) + off;
                    Vector3d v1 = v0 + new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                    Vector3d v2 = v0 + new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                    DMesh3 b = Tri(v0, v1, v2);
                    Vector3d nrm = (v1 - v0).Cross(v2 - v0).Normalized;
                    Vector3d q(double s, double t) => v0 + s * (v1 - v0) + t * (v2 - v0) + 1e-9 * nrm;
                    DMesh3 a = Tri(q(0.3, 0.3), q(0.32, 0.3), q(0.3, 0.32));
                    var plain = new MeshHausdorffDistance(a, b) { Symmetric = false };
                    var cert = new MeshHausdorffDistance(a, b) { Symmetric = false, CertifiedUpper = true };
                    Assert.True(plain.Compute() && cert.Compute());
                    Assert.True(VerticesWithin(a, b, cert.UpperBound), $"off {off} it {it}: certified upper {cert.UpperBound:R} below a corner's distance");
                    Assert.True(cert.LowerBound <= cert.UpperBound && cert.Converged);
                    Assert.Equal(plain.LowerBound, cert.LowerBound);
                    if (!VerticesWithin(a, b, plain.UpperBound)) below0++;
                    n++;
                }
            // (why the option exists: the default is below the exact distance in a good part of these cases; 76 of 180 measured)
            Assert.True(below0 > n / 10, $"{below0} of {n}");
        }

        /// <summary> dense exact sampling: a grid patch in the plane z = c (its vertices and points on its triangles are doubles, exactly
        /// on it) against a bumpy sphere: the certified upper bound is at least the exact distance of every sample </summary>
        [Fact]
        public void DenseSamples_UnderTheCertifiedUpperBound()
        {
            DMesh3 b = Bumpy(3, 6);
            var a = new DMesh3();
            const int N = 6;
            double z = 0.875, s0 = -0.5, h = 1.0 / N;
            var id = new int[N + 1, N + 1];
            for (int i = 0; i <= N; ++i)
                for (int j = 0; j <= N; ++j)
                    id[i, j] = a.AppendVertex(new Vector3d(s0 + i * 0.125, s0 + j * 0.125, z));
            for (int i = 0; i < N; ++i)
                for (int j = 0; j < N; ++j) {
                    a.AppendTriangle(id[i, j], id[i + 1, j], id[i + 1, j + 1]);
                    a.AppendTriangle(id[i, j], id[i + 1, j + 1], id[i, j + 1]);
                }
            var hd = new MeshHausdorffDistance(a, b) { Symmetric = false, CertifiedUpper = true, Tolerance = 1e-6 };
            Assert.True(hd.Compute() && hd.Converged);
            int samples = 0;
            // (points on the patch at a 1/64 spacing: dyadic, exactly in its plane and on its triangles)
            for (int i = 0; i <= 48; i += 3)
                for (int j = 0; j <= 48; j += 3) {
                    Vector3d p = new Vector3d(s0 + i / 64.0, s0 + j / 64.0, z);
                    Assert.True(CertifiedPointMeshDistance.AtLeastDistance(b, p, hd.UpperBound), $"sample {p}: {hd.UpperBound:R}");
                    samples++;
                }
            Assert.True(samples > 250);
            Assert.True(hd.LowerBound > 0 && hd.UpperBound - hd.LowerBound <= 1e-6 && !double.IsInfinity(hd.UpperBound));
        }

        /// <summary> folded spheres at 1.3 2^52 (#738, the shifted copies): certified, converged, every vertex of both meshes within the
        /// upper bound, exactly (the distance is 0.0099, as for the exactly shifted spheres) </summary>
        [Fact]
        public void FoldedSpheres_AtALargeOffset_Certified()
        {
            Vector3d off = new Vector3d(0, 0, 1.3 * Math.Pow(2, 52));
            DMesh3 a = Sphere(6, 1, off), b = Sphere(6, 1.01, off);
            var hd = new MeshHausdorffDistance(a, b) { CertifiedUpper = true, MaxSubdivisions = 100000 };
            Assert.True(hd.Compute());
            Assert.True(hd.Converged, hd.Subdivisions + " subdivisions");
            Assert.True(VerticesWithin(a, b, hd.UpperBound) && VerticesWithin(b, a, hd.UpperBound));
            Assert.True(hd.LowerBound > 0.009 && hd.LowerBound <= hd.UpperBound && hd.UpperBound < 0.011, hd.LowerBound + " " + hd.UpperBound);
        }

        /// <summary> ordinary inputs: the certified result is the default one up to a few ulps of the coordinates (the same lower bound,
        /// the same or nearly the same subdivisions) </summary>
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void OrdinaryInputs_CloseToTheDefault(int which)
        {
            DMesh3 a, b; double scale;
            if (which == 0) { a = Sphere(6, 1, Vector3d.Zero); b = Sphere(6, 1.01, Vector3d.Zero); scale = 1; }
            else if (which == 1) { a = Bumpy(5, 12); b = Bumpy(6, 8); scale = 1; }
            else {
                Vector3d utm = new Vector3d(632117.25, 5712043.5, 113.75);
                a = Sphere(8, 50, utm); b = Sphere(10, 50.3, utm); scale = 5.7e6;
            }
            foreach (bool sym in new[] { true, false }) {
                var plain = new MeshHausdorffDistance(a, b) { Symmetric = sym };
                var cert = new MeshHausdorffDistance(a, b) { Symmetric = sym, CertifiedUpper = true };
                Assert.True(plain.Compute() && cert.Compute() && plain.Converged && cert.Converged);
                Assert.Equal(plain.LowerBound, cert.LowerBound);
                double ulp = Math.Pow(2, -52) * scale;
                Assert.True(Math.Abs(cert.UpperBound - plain.UpperBound) <= 1e-13 * plain.UpperBound + 64 * ulp,
                    $"{which} {sym}: {plain.UpperBound:R} vs {cert.UpperBound:R}");
                Assert.True(Math.Abs(cert.Subdivisions - plain.Subdivisions) <= plain.Subdivisions / 100 + 4, $"{plain.Subdivisions} vs {cert.Subdivisions}");
            }
        }

        /// <summary> under every similarity: bit for bit at the exact ones (2^k scales), the intervals overlapping otherwise </summary>
        [Fact]
        public void CertifiedUpper_UnderEverySimilarity()
        {
            DMesh3 a0 = Bumpy(5, 8), b0 = Bumpy(6, 6);
            var h0 = new MeshHausdorffDistance(a0, b0) { CertifiedUpper = true, Tolerance = 1e-7 };
            Assert.True(h0.Compute() && h0.Converged);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 a = new DMesh3(a0), b = new DMesh3(b0);
                MeshTransforms.PerVertexTransform(a, s.Apply);
                MeshTransforms.PerVertexTransform(b, s.Apply);
                var h = new MeshHausdorffDistance(a, b) { CertifiedUpper = true, Tolerance = 1e-7 * s.Scale };
                Assert.True(h.Compute(), s.Name);
                if (s.IsExact) {
                    Assert.Equal(h0.LowerBound, h.LowerBound / s.Scale);
                    Assert.Equal(h0.UpperBound, h.UpperBound / s.Scale);
                    Assert.Equal(h0.Subdivisions, h.Subdivisions);
                } else {
                    double eps = 100 * s.Resolution(2);
                    Assert.True(h.LowerBound / s.Scale <= h0.UpperBound + eps && h0.LowerBound <= h.UpperBound / s.Scale + eps, s.Name);
                }
            }
        }

        /// <summary> computing twice on one instance, a fresh instance and a serial run give the same result </summary>
        [Fact]
        public void CertifiedUpper_Reuse_Serial_Same()
        {
            DMesh3 a = Bumpy(7, 10), b = Bumpy(8, 8);
            var h = new MeshHausdorffDistance(a, b) { CertifiedUpper = true };
            Assert.True(h.Compute() && h.Converged);
            Assert.True(h.LowerBound > 0 && h.LowerBound <= h.UpperBound && !double.IsInfinity(h.UpperBound));
            double lo = h.LowerBound, up = h.UpperBound; int sub = h.Subdivisions;
            Assert.True(h.Compute());
            Assert.Equal(lo, h.LowerBound); Assert.Equal(up, h.UpperBound); Assert.Equal(sub, h.Subdivisions);
            var fresh = new MeshHausdorffDistance(a, b) { CertifiedUpper = true };
            Assert.True(fresh.Compute());
            Assert.Equal(lo, fresh.LowerBound); Assert.Equal(up, fresh.UpperBound);
            gParallel.SerialOnThisThread = true;
            try {
                var serial = new MeshHausdorffDistance(a, b) { CertifiedUpper = true };
                Assert.True(serial.Compute());
                Assert.Equal(lo, serial.LowerBound); Assert.Equal(up, serial.UpperBound); Assert.Equal(sub, serial.Subdivisions);
            } finally { gParallel.SerialOnThisThread = false; }
        }

        /// <summary> the patch bound (estimated slack) is not used for a certified bound: with PatchBound the result is the same </summary>
        [Fact]
        public void CertifiedUpper_IgnoresThePatchBound()
        {
            DMesh3 big = Bumpy(9, 16), red = new DMesh3(big);
            new Reducer(red).ReduceToTriangleCount(big.TriangleCount / 8);
            FieldInfo patch = typeof(MeshHausdorffDistance).GetField("PatchBound", BindingFlags.NonPublic | BindingFlags.Instance);
            var without = new MeshHausdorffDistance(red, big) { Symmetric = false, CertifiedUpper = true };
            var with = new MeshHausdorffDistance(red, big) { Symmetric = false, CertifiedUpper = true };
            patch.SetValue(with, true);
            var plainWith = new MeshHausdorffDistance(red, big) { Symmetric = false };
            patch.SetValue(plainWith, true);
            var plainWithout = new MeshHausdorffDistance(red, big) { Symmetric = false };
            Assert.True(without.Compute() && with.Compute() && plainWith.Compute() && plainWithout.Compute());
            Assert.Equal(without.UpperBound, with.UpperBound);
            Assert.Equal(without.Subdivisions, with.Subdivisions);
            // (the patch bound is active on this input without the option: it saves subdivisions)
            Assert.True(plainWith.Subdivisions < plainWithout.Subdivisions, $"{plainWith.Subdivisions} vs {plainWithout.Subdivisions}");
        }
    }
}
