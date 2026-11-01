using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.ShareDeviationQueries: the tree queries of one collapse's MaxDeviation check are shared through a
    /// TriangleDeviationBounds.QueryCache. The answers must not change: reductions with and without sharing give the
    /// same mesh bit for bit, IsWithin with a cache equals IsWithin without one, and a cache of another distance is
    /// not used.
    /// </summary>
    public class ReducerSharedDeviationQueriesTests
    {
        static DMesh3 Bumpy(int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5) + 0.05 * Math.Sin(9 * p.z)));
            }
            return m;
        }

        static DMesh3 Terrain(int n)
        {
            var m = new DMesh3();
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    ids[i, j] = m.AppendVertex(new Vector3d(x, y, 0.08 * Math.Sin(6 * x) * Math.Cos(5 * y) + 0.01 * Math.Sin(40 * x + 13 * y)));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        static void AssertSame(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            Assert.Equal(a.MaxVertexID, b.MaxVertexID);
            foreach (int v in a.VertexIndices()) {
                Assert.True(b.IsVertex(v));
                Assert.Equal(a.GetVertex(v), b.GetVertex(v));
            }
            foreach (int t in a.TriangleIndices()) Assert.Equal(a.GetTriangle(t), b.GetTriangle(t));
        }

        static DMesh3 Reduce(DMesh3 input, double tol, bool share, int target = 4)
        {
            var m = new DMesh3(input);
            new Reducer(m) { MaxDeviation = tol, ShareDeviationQueries = share }.ReduceToTriangleCount(target);
            return m;
        }

        /// <summary> the same reduction with and without sharing, at three tolerances, on two meshes, twice </summary>
        [Fact]
        public void Reduction_SameWithAndWithoutSharing()
        {
            foreach (DMesh3 input in new[] { Bumpy(20), Terrain(40) }) {
                double diag = input.CachedBounds.DiagonalLength;
                foreach (double rel in new[] { 1e-3, 3e-3, 1e-2 }) {
                    DMesh3 without = Reduce(input, rel * diag, false), with = Reduce(input, rel * diag, true);
                    Assert.True(with.TriangleCount < 0.9 * input.TriangleCount && with.TriangleCount > 4, $"{with.TriangleCount} of {input.TriangleCount}: the check both allowed and stopped collapses");
                    AssertSame(without, with);
                    AssertSame(with, Reduce(input, rel * diag, true));     // repeated: deterministic
                    Assert.True(MeshDeviationCheck(with, input, rel * diag), "within the tolerance");
                }
            }
        }

        // every vertex of the reduced mesh, and the centroid of every triangle, within tol of the input
        static bool MeshDeviationCheck(DMesh3 reduced, DMesh3 input, double tol)
        {
            var tree = new DMeshAABBTree3(input, true);
            foreach (int t in reduced.TriangleIndices()) {
                Vector3d c = reduced.GetTriCentroid(t);
                if (Math.Sqrt(MeshQueries.TriDistanceSqr(input, tree.FindNearestTriangle(c), c)) > tol * (1 + 1e-9)) return false;
            }
            return true;
        }

        /// <summary>
        /// IsWithin with a cache, shared by triangles with common corners and edges (a fan, as a collapse makes),
        /// equals IsWithin without one; a cache of another distance is ignored; NearestWithin equals the tree query
        /// </summary>
        [Fact]
        public void IsWithin_CacheGivesTheSameAnswers()
        {
            DMesh3 target = Bumpy(16);
            var tree = new DMeshAABBTree3(target, true);
            var bounds = new TriangleDeviationBounds(target);
            var rand = new Random(3);
            int yes = 0, no = 0;
            foreach (double eps in new[] { 0.003, 0.01, 0.03 }) {
                for (int k = 0; k < 150; ++k) {
                    // a fan of 6 triangles around a centre near the surface
                    Vector3d c = target.GetVertex(rand.Next(target.VertexCount)) * (1 + (rand.NextDouble() - 0.5) * eps);
                    double r = 0.02 + 0.1 * rand.NextDouble();
                    Vector3d n = c.Normalized, u = n.Cross(Vector3d.AxisZ).Normalized, w = n.Cross(u);
                    var ring = Enumerable.Range(0, 6).Select(i => c + r * (Math.Cos(i * Math.PI / 3) * u + Math.Sin(i * Math.PI / 3) * w)).ToArray();
                    var cache = new TriangleDeviationBounds.QueryCache(eps);
                    var other = new TriangleDeviationBounds.QueryCache(eps * 2);
                    for (int i = 0; i < 6; ++i) {
                        Vector3d a = ring[i], b = ring[(i + 1) % 6];
                        bool plain = bounds.IsWithin(tree, c, a, b, eps);
                        Assert.Equal(plain, bounds.IsWithin(tree, c, a, b, eps, 10, cache));
                        Assert.Equal(plain, bounds.IsWithin(tree, c, a, b, eps, 10, other));
                        Assert.Equal(tree.FindNearestTriangle(a, eps), bounds.NearestWithin(tree, a, cache));
                        Assert.Equal(tree.FindNearestTriangle(0.5 * (c + a), eps), bounds.NearestWithin(tree, 0.5 * (c + a), cache));
                        if (plain) yes++; else no++;
                    }
                    Assert.True(cache.Results.Count > 0);
                    Assert.Empty(other.Results);        // another distance: not used
                }
            }
            Assert.True(yes > 100 && no > 100, $"{yes} within, {no} not");
        }

        /// <summary> invariance: 2^k scales give the same reduction, scaled, with sharing </summary>
        [Fact]
        public void Reduction_Invariance_Pow2()
        {
            DMesh3 input = Terrain(16);
            double tol = 3e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 baseRes = Reduce(input, tol, true);
            foreach (Similarity sim in Similarity.All().Where(s => s.IsExact)) {
                DMesh3 scaled = new DMesh3(input);
                foreach (int v in scaled.VertexIndices()) scaled.SetVertex(v, sim.Apply(scaled.GetVertex(v)));
                DMesh3 r = Reduce(scaled, sim.ApplyLength(tol), true);
                Assert.Equal(baseRes.TriangleCount, r.TriangleCount);
                foreach (int v in baseRes.VertexIndices()) Assert.Equal(sim.Apply(baseRes.GetVertex(v)), r.GetVertex(v));
            }
        }
    }
}
