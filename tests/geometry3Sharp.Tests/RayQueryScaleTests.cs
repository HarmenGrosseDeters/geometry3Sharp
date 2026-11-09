using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Ray queries at every binary scale (found by tools/MeshFuzzPilot, the tree-ray target): the ray parameter of
    /// IntrRay3Triangle3 (cubes of the coordinates left the double range at 1e-103 and 1e102, so t was 0 or lost and the
    /// traversal's boxes, which are exact, contradicted it) and the centroid of a triangle above 6e307 (the sum of three
    /// corners overflowed, and the tree build skipped the triangle).
    /// </summary>
    public class RayQueryScaleTests
    {
        static double Pow2(int e) => Math.Pow(2, e);

        static DMesh3 Scaled(DMesh3 m, double s)
        {
            var n = new DMesh3(m);
            foreach (int v in n.VertexIndices())
                n.SetVertex(v, s * n.GetVertex(v));
            return n;
        }

        static DMesh3 Octahedron()
        {
            var m = new DMesh3();
            var v = new[] { new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, 1), new Vector3d(0, 0, -1) };
            foreach (var p in v) m.AppendVertex(p);
            int[] t = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            for (int i = 0; i < t.Length; i += 3) m.AppendTriangle(t[i], t[i + 1], t[i + 2]);
            return m;
        }

        // the ray parameter of a hit is the same double, scaled, at every power of two (the cubes of the coordinates
        // underflowed to 0 below 1e-103 and overflowed above 1e102: t was 0 or lost)
        [Theory]
        [InlineData(-1000)]
        [InlineData(-700)]
        [InlineData(-400)]
        [InlineData(-340)]
        [InlineData(-60)]
        [InlineData(0)]
        [InlineData(60)]
        [InlineData(340)]
        [InlineData(400)]
        [InlineData(700)]
        [InlineData(1000)]
        public void RayParameter_ScalesWithTheTriangle_BitForBit(int e)
        {
            Vector3d a = new Vector3d(0, 0, 0), b = new Vector3d(4, 0, 0), c = new Vector3d(0, 4, 0);
            var dir = new Vector3d(0.1, 0.2, -0.75).Normalized;
            Vector3d origin = new Vector3d(1, 1, 3);
            var ray0 = new Ray3d(origin, dir);
            Assert.True(IntrRay3Triangle3.Intersects(ref ray0, ref a, ref b, ref c, out double t0));
            Assert.InRange(t0, 1, 10);
            double s = Pow2(e);
            Vector3d sa = s * a, sb = s * b, sc = s * c;
            var ray = new Ray3d(s * origin, dir);
            Assert.True(IntrRay3Triangle3.Intersects(ref ray, ref sa, ref sb, ref sc, out double t));
            Assert.Equal(s * t0, t);
            // the barycentric coordinates (they are ratios) are the same too
            var tri0 = new IntrRay3Triangle3(ray0, new Triangle3d(a, b, c));
            Assert.True(tri0.Find());
            var tri = new IntrRay3Triangle3(ray, new Triangle3d(sa, sb, sc));
            Assert.True(tri.Find());
            Assert.Equal(tri0.TriangleBaryCoords, tri.TriangleBaryCoords);
            Assert.Equal(s * tri0.RayParameter, tri.RayParameter);
        }

        // the nearest and all hits of the tree are those of the brute force at every scale: rays through nested octahedra
        [Theory]
        [InlineData(1e-150)]
        [InlineData(1e-30)]
        [InlineData(1)]
        [InlineData(1e30)]
        [InlineData(1e100)]
        [InlineData(1e111)]
        [InlineData(1e150)]
        [InlineData(1e250)]
        [InlineData(1e290)]
        public void NearestAndAllHits_MatchTheBruteForce_AtEveryScale(double scale)
        {
            DMesh3 m = Scaled(Octahedron(), scale);
            // refined, so the tree has several levels
            for (int k = 0; k < 2; ++k)
                new MeshEditor(m).AppendMesh(Scaled(Octahedron(), scale * (0.3 + 0.1 * k)));
            var tree = new DMeshAABBTree3(m, true);
            var rnd = new Random(7);
            int hits = 0;
            for (int i = 0; i < 300; ++i) {
                Vector3d origin = scale * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                var dir = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
                var ray = new Ray3d(origin, dir);
                // a tie of two triangles (a hit on a shared edge) is the lowest id in both
                int expected = MeshQueries.FindHitTriangle_LinearSearch(m, ray);
                int actual = tree.FindNearestHitTriangle(ray);
                Assert.Equal(expected, actual);
                var all = new List<int>();
                int n = tree.FindAllHitTriangles(ray, all);
                var brute = m.TriangleIndices().Where(t => {
                    Vector3d p = default, q = default, r = default;
                    m.GetTriVertices(t, ref p, ref q, ref r);
                    return IntrRay3Triangle3.Intersects(ref ray, ref p, ref q, ref r, out _);
                }).ToList();
                Assert.Equal(brute.OrderBy(x => x), all.OrderBy(x => x));
                Assert.Equal(brute.Count, n);
                if (expected != DMesh3.InvalidID) ++hits;
            }
            Assert.True(hits > 250);
        }

        // a far origin against a small triangle: the cubes are fine, scaling by the origin's distance would underflow the normal
        [Fact]
        public void RayParameter_FarOriginSmallTriangle_IsRight()
        {
            double s = 1e-60;
            Vector3d a = s * new Vector3d(0, 0, 0), b = s * new Vector3d(4, 0, 0), c = s * new Vector3d(0, 4, 0);
            var ray = new Ray3d(new Vector3d(1e-60, 1e-60, 1e150), new Vector3d(0, 0, -1));
            Assert.True(IntrRay3Triangle3.Intersects(ref ray, ref a, ref b, ref c, out double t));
            Assert.Equal(1e150, t, 3);
        }

        [Fact]
        public void NoLimit_InfinityAndNaN_AreNoLimit()
        {
            var tree = new DMeshAABBTree3(Octahedron(), true);
            var ray = new Ray3d(Vector3d.Zero, new Vector3d(0.6, 0, 0.8));
            int hit = tree.FindNearestHitTriangle(ray);
            Assert.NotEqual(DMesh3.InvalidID, hit);
            Assert.Equal(hit, tree.FindNearestHitTriangle(ray, double.PositiveInfinity));
            Assert.Equal(hit, tree.FindNearestHitTriangle(ray, double.NaN));
        }

        // above 6e307 the sum of the three corners overflows: the build left such a triangle out, and no query found it
        [Fact]
        public void Tree_TriangleAbove6e307_IsInTheTree()
        {
            double x = 8.0e307;
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(x, 0, 0)), m.AppendVertex(new Vector3d(x, 1e300, 0)), m.AppendVertex(new Vector3d(x, 0, 1e300)));
            var tree = new DMeshAABBTree3(m, true);
            var seen = new List<int>();
            tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() { NextTriangleF = t => seen.Add(t) });
            Assert.Equal(new[] { 0 }, seen);
            var ray = new Ray3d(new Vector3d(x - 1e300, 1e299, 1e299), Vector3d.AxisX);
            Assert.Equal(0, tree.FindNearestHitTriangle(ray));
        }

        // nearest hit, all hits and IsInside under every similarity (the same triangles; for the powers of two also the same
        // boxes and so the same answers bit for bit)
        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void RayQueries_AreInvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 m = Octahedron();
            new MeshEditor(m).AppendMesh(Scaled(Octahedron(), 0.4));
            DMesh3 t = Invariance.Transformed(m, sim);
            var tree = new DMeshAABBTree3(m, true);
            var treeT = new DMeshAABBTree3(t, true);
            var rnd = new Random(11);
            int hits = 0;
            for (int i = 0; i < 100; ++i) {
                Vector3d origin = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                var dir = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
                var ray = new Ray3d(origin, dir);
                var rayT = new Ray3d(sim.Apply(origin), sim.ApplyDirection(dir).Normalized);
                int a = tree.FindNearestHitTriangle(ray), b = treeT.FindNearestHitTriangle(rayT);
                Assert.Equal(a, b);
                if (a != DMesh3.InvalidID) ++hits;
                var all = new List<int>(); var allT = new List<int>();
                tree.FindAllHitTriangles(ray, all); treeT.FindAllHitTriangles(rayT, allT);
                Assert.Equal(all.OrderBy(x => x), allT.OrderBy(x => x));
                Assert.Equal(tree.IsInside(origin), treeT.IsInside(sim.Apply(origin)));
            }
            Assert.True(hits > 80);
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();
    }
}
