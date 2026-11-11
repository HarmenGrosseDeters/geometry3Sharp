using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The squared distance from a point to a triangle (MeshQueries.TriDistanceSqr, DistPoint3Triangle3.DistanceSqr) at extreme sizes:
    /// the products of the algorithm (squared edge lengths, their determinant) under- or overflowed, and the division by a subnormal
    /// determinant gave NaN (found by the MeshFuzzPilot's libFuzzer run of the nearest-triangle query on a mesh of edge 2.4e-35 with a
    /// sliver). For tiny or huge regular triangles the degenerate branch answered with the distance to the edges.
    /// </summary>
    public class TriangleDistanceScaleTests
    {
        static readonly Vector3d V0 = new Vector3d(0, 0, 0), V1 = new Vector3d(1, 0.1, 0), V2 = new Vector3d(0.2, 1, 0.3);
        static readonly Vector3d[] Points = {
            new Vector3d(0.3, 0.3, 0.5), new Vector3d(-0.4, 0.2, 0.1), new Vector3d(0.6, 0.7, -0.2), new Vector3d(2, 2, 2), new Vector3d(0.5, 0.05, 0) };

        static DMesh3 Mesh(Vector3d a, Vector3d b, Vector3d c)
        {
            var m = new DMesh3();
            m.AppendVertex(a); m.AppendVertex(b); m.AppendVertex(c);
            m.AppendTriangle(0, 1, 2);
            return m;
        }

        [Fact]
        public void Distance_IsScaleInvariantBitForBit_AcrossTheRangeOfDoubles()
        {
            foreach (Vector3d p in Points) {
                DMesh3 baseMesh = Mesh(V0, V1, V2);
                double expected = MeshQueries.TriDistanceSqr(baseMesh, 0, p);
                var baseTri = new Triangle3d(V0, V1, V2);
                Vector3d basePoint = p;
                double baseD2 = DistPoint3Triangle3.DistanceSqr(ref basePoint, ref baseTri, out Vector3d baseClosest, out Vector3d baseBary);
                Assert.True(expected >= 0 && !double.IsNaN(expected));
                for (int k = -340; k <= 340; k += 17) {
                    double s = Math.Pow(2, k);
                    DMesh3 m = Mesh(V0 * s, V1 * s, V2 * s);
                    double d2 = MeshQueries.TriDistanceSqr(m, 0, p * s);
                    double back = d2 / s / s;
                    Assert.True(back == expected, "scale 2^" + k + ", point " + p + ": " + back.ToString("R") + " instead of " + expected.ToString("R"));
                    var tri = new Triangle3d(V0 * s, V1 * s, V2 * s);
                    Vector3d ps = p * s;
                    Assert.True(DistPoint3Triangle3.DistanceSqr(ref ps, ref tri, out Vector3d cp, out Vector3d bc) / s / s == expected, "DistPoint3Triangle3 at 2^" + k);
                    Assert.True(baseD2 == expected);
                    Assert.True(cp / s == baseClosest, "closest point at 2^" + k + ": " + cp / s + " instead of " + baseClosest);
                    Assert.True(bc == baseBary, "barycentric coordinates at 2^" + k + ": " + bc + " instead of " + baseBary);
                }
            }
        }

        [Fact]
        public void Sliver_GivesTheDistanceToItsSegment_NotNaN()
        {
            // an edge 1e-160 times the other: a segment to the precision of a double (the products a00 and det are subnormal)
            Vector3d a = Vector3d.Zero, b = new Vector3d(3.6e-160, 3.8e-285, 0), c = new Vector3d(1, 1, 0);
            Vector3d p = new Vector3d(0.3, 0.8, 0.4);
            double expected = Math.Sqrt(SegmentDistanceSqr(a, c, p));
            for (int k = -60; k <= 400; k += 20) {
                double s = Math.Pow(2, k);
                double d2 = MeshQueries.TriDistanceSqr(Mesh(a, b * s, c * s), 0, p * s);
                Assert.False(double.IsNaN(d2), "NaN at scale 2^" + k);
                Assert.True(Math.Abs(Math.Sqrt(d2) / s - expected) <= 1e-12, "scale 2^" + k + ": " + Math.Sqrt(d2) / s + " instead of " + expected);
                var tri = new Triangle3d(a, b * s, c * s);
                Vector3d ps = p * s;
                double t2 = DistPoint3Triangle3.DistanceSqr(ref ps, ref tri, out _, out _);
                Assert.False(double.IsNaN(t2), "DistPoint3Triangle3: NaN at scale 2^" + k);
            }
        }

        static double SegmentDistanceSqr(Vector3d a, Vector3d b, Vector3d p)
        {
            Vector3d ab = b - a;
            double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / ab.LengthSquared));
            return (a + t * ab - p).LengthSquared;
        }

        /// <summary>
        /// The nearest-triangle query on two cubes of edge 2.4e-35 and a flat sliver at the origin (the 20-byte pilot input), at the
        /// scales that gave NaN: the tree finds a triangle at the brute force minimum, and no triangle's distance is NaN
        /// </summary>
        [Theory]
        [InlineData(1.0)]
        [InlineData(1.2676506002282294E+30)]    // 2^100
        [InlineData(8.307674973655724E+34)]     // the normalising scale of the pilot input
        [InlineData(1.329227995784916E+36)]     // 2^120
        public void NearestTriangle_OnTinyCubesWithASliver_FindsTheMinimum(double scale)
        {
            double a = 2.407412430484045E-35, h = 1.2037062152420224E-35, k = 3.611118645726067E-35;
            var v = new[] {
                new Vector3d(0, 0, 0), new Vector3d(4.383619285969676E-193, 4.635E-320, 0), new Vector3d(a, a, 0), new Vector3d(0, a, 0),
                new Vector3d(0, 0, a), new Vector3d(a, 0, a), new Vector3d(a, a, a), new Vector3d(0, a, a),
                new Vector3d(h, h, h), new Vector3d(k, h, h), new Vector3d(k, k, h), new Vector3d(h, k, h),
                new Vector3d(h, h, k), new Vector3d(k, h, k), new Vector3d(k, k, k), new Vector3d(h, k, k) };
            int[][] t = {
                new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 }, new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 1, 2, 6 }, new[] { 1, 6, 5 },
                new[] { 2, 3, 7 }, new[] { 2, 7, 6 }, new[] { 3, 0, 4 }, new[] { 3, 4, 7 }, new[] { 8, 10, 9 }, new[] { 8, 11, 10 }, new[] { 12, 13, 14 }, new[] { 12, 14, 15 },
                new[] { 8, 9, 13 }, new[] { 8, 13, 12 }, new[] { 9, 10, 14 }, new[] { 9, 14, 13 }, new[] { 10, 11, 15 }, new[] { 10, 15, 14 }, new[] { 11, 8, 12 }, new[] { 11, 12, 15 } };
            var mesh = new DMesh3();
            foreach (Vector3d p in v) mesh.AppendVertex(p * scale);
            foreach (int[] f in t) mesh.AppendTriangle(f[0], f[1], f[2]);
            Vector3d q = Vector3d.Zero;
            double[] d = Enumerable.Range(0, t.Length).Select(i => MeshQueries.TriDistanceSqr(mesh, i, q)).ToArray();
            Assert.All(d, x => Assert.False(double.IsNaN(x)));
            var tree = new DMeshAABBTree3(mesh, true);
            int id = tree.FindNearestTriangle(q, out double treeD2);
            Assert.NotEqual(DMesh3.InvalidID, id);
            Assert.Equal(d.Min(), d[id]);
            Assert.Equal(d.Min(), treeD2);
        }

        [Fact]
        public void FarPoint_BesideATinyTriangle_IsNotInfinity()
        {
            // 1e56 is 1e156 edge lengths away: the scaled square overflows, the answer 1e112 does not
            var mesh = Mesh(Vector3d.Zero, new Vector3d(1e-100, 0, 0), new Vector3d(0, 1e-100, 0));
            double d2 = MeshQueries.TriDistanceSqr(mesh, 0, new Vector3d(0, 0, 1e56));
            Assert.True(Math.Abs(d2 / 1e112 - 1) < 1e-12, d2.ToString("R"));
            var tri = new Triangle3d(Vector3d.Zero, new Vector3d(1e-100, 0, 0), new Vector3d(0, 1e-100, 0));
            Vector3d p = new Vector3d(0, 0, 1e56);
            Assert.True(Math.Abs(DistPoint3Triangle3.DistanceSqr(ref p, ref tri, out _, out _) / 1e112 - 1) < 1e-12);
        }

        [Fact]
        public void TinyTriangleFarFromTheOrigin_IsMeasuredFromItsOwnVertex()
        {
            // the edges (1e-250) are tiny beside the coordinates (1e100): scaling the absolute coordinates overflowed (NaN)
            Vector3d a = new Vector3d(1e100, 0, 0), b = new Vector3d(1e100, 1e-250, 0), c = new Vector3d(1e100, 0, 1e-250);
            var mesh = Mesh(a, b, c);
            Assert.Equal(0, MeshQueries.TriDistanceSqr(mesh, 0, a));
            var tri = new Triangle3d(a, b, c);
            Vector3d p = a;
            Assert.Equal(0, DistPoint3Triangle3.DistanceSqr(ref p, ref tri, out Vector3d closest, out Vector3d bary));
            Assert.Equal(a, closest);
            Assert.False(double.IsNaN(bary.x + bary.y + bary.z));
        }

        [Fact]
        public void TinyRegularTriangle_PointAboveItsInterior_IsTheHeightSquared()
        {
            // 1e-100 edges: det underflowed to 0 and the degenerate branch gave the distance to the nearest edge
            var tri = new Triangle3d(Vector3d.Zero, new Vector3d(1e-100, 0, 0), new Vector3d(0, 1e-100, 0));
            Vector3d p = new Vector3d(0.2e-100, 0.2e-100, 0.5e-100);
            double d2 = DistPoint3Triangle3.DistanceSqr(ref p, ref tri, out Vector3d closest, out _);
            Assert.True(Math.Abs(d2 / 2.5e-201 - 1) < 1e-12, d2.ToString("R"));
            Assert.True(Math.Abs(closest.x / 0.2e-100 - 1) < 1e-12 && Math.Abs(closest.y / 0.2e-100 - 1) < 1e-12 && closest.z == 0);
        }

        [Fact]
        public void ZeroAreaTriangle_IsThePointDistance()
        {
            Vector3d a = new Vector3d(1e-200, 2e-200, 0);
            var mesh = Mesh(a, a, a);
            double d2 = MeshQueries.TriDistanceSqr(mesh, 0, new Vector3d(0, 0, 3e-200));
            Assert.False(double.IsNaN(d2));
            Assert.True(d2 >= 0);
        }

        [Fact]
        public void HugeNeedlesAndCoincidentVertices_GiveTheDistanceToTheirSegment()
        {
            // edges of 1e155 and more (squared 1e310: not a double), found by the second review
            var cases = new (Vector3d a, Vector3d b, Vector3d c, Vector3d p, double expected)[] {
                (Vector3d.Zero, new Vector3d(1e155, 0, 0), new Vector3d(0, 1e90, 0), new Vector3d(5e154, 0, 1e100), 1e100),
                (Vector3d.Zero, new Vector3d(1e160, 0, 0), new Vector3d(0, 1e99, 0), new Vector3d(5e159, 1e50, 1e130), 1e130),
                (Vector3d.Zero, Vector3d.Zero, new Vector3d(1e160, 0, 0), new Vector3d(5e159, 1e130, 0), 1e130) };
            foreach (var (a, b, c, p, expected) in cases) {
                double d2 = MeshQueries.TriDistanceSqr(Mesh(a, b, c), 0, p);
                Assert.True(!double.IsNaN(d2) && !double.IsInfinity(d2) && d2 < double.MaxValue, d2.ToString("R"));
                Assert.True(Math.Abs(Math.Sqrt(d2) / expected - 1) < 1e-9, "distance " + Math.Sqrt(d2) + " instead of " + expected);
                var tri = new Triangle3d(a, b, c);
                Vector3d q = p;
                double t2 = DistPoint3Triangle3.DistanceSqr(ref q, ref tri, out _, out _);
                Assert.True(Math.Abs(Math.Sqrt(t2) / expected - 1) < 1e-9);
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Distance_InvariantUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (Vector3d p in Points) {
                double expected = MeshQueries.TriDistanceSqr(Mesh(V0, V1, V2), 0, p);
                double d2 = MeshQueries.TriDistanceSqr(Mesh(s.Apply(V0), s.Apply(V1), s.Apply(V2)), 0, s.Apply(p));
                double back = d2 / (s.Scale * s.Scale);
                if (s.IsExact)
                    Assert.True(back == expected, name + ": " + back.ToString("R") + " instead of " + expected.ToString("R"));
                else
                    Assert.True(Math.Abs(back - expected) <= 1e-9 + 8 * s.Resolution(4) * 4, name + ": " + back + " instead of " + expected);
            }
        }
    }
}
