using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Invariance over Similarity.All() of the opt-in exact winding paths (ExactWindingAdoptionTests): the decisions of
    /// IsInsideBatch / the BoundaryTree threshold queries equal those of the identity frame in every frame, the 2^k frames
    /// bit for bit (results and, mapped back, solid meshes).
    /// </summary>
    public class ExactWindingAdoptionInvarianceTests
    {
        static Vector3d[] Points(int n, int seed)
        {
            var rnd = new Random(seed);
            var pts = new Vector3d[n];
            for (int i = 0; i < n; ++i)
                pts[i] = new Vector3d(rnd.NextDouble() * 3.0 - 1.5, rnd.NextDouble() * 3.0 - 1.5, rnd.NextDouble() * 3.0 - 1.5);
            return pts;
        }

        static DMesh3[] Meshes()
        {
            DMesh3 closed = ExactWindingAdoptionTests.Sphere(10, 1.0);
            return new[] { closed, ExactWindingAdoptionTests.Unweld(closed), ExactWindingAdoptionTests.LargeBoundaryOpenGrid(160) };
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void IsInsideBatch_ExactWindingNumber_InvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            Vector3d[] pts = Points(400, 5);
            Vector3d[] ptsT = pts.Select(sim.Apply).ToArray();
            foreach (DMesh3 m in Meshes()) {
                var tree = new DMeshAABBTree3(m, true);
                var treeT = new DMeshAABBTree3(Invariance.Transformed(m, sim), true);
                var r = new InsideStatus[pts.Length];
                var rT = new InsideStatus[pts.Length];
                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(tree, pts, r, InsideMethod.ExactWindingNumber));
                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(treeT, ptsT, rT, InsideMethod.ExactWindingNumber));
                Assert.Contains(InsideStatus.Inside, r);
                Assert.Contains(InsideStatus.Outside, r);
                for (int i = 0; i < pts.Length; ++i)
                    Assert.True(r[i] == rT[i], $"{name}, point {i} {pts[i]}: {r[i]} vs {rT[i]}");
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void BoundaryTreeThresholdQueries_InvariantUnderSimilarities(string name)
        {
            // the open grid has 640 boundary edges, above BoundaryTreeMinEdges: EvaluateAbove / EvaluateAbsAbove take the tree
            Similarity sim = Similarity.ByName(name);
            DMesh3 m = ExactWindingAdoptionTests.LargeBoundaryOpenGrid(160);
            var cache = new DMeshAABBTree3(m, true).GetExactWindingCache();
            var cacheT = new DMeshAABBTree3(Invariance.Transformed(m, sim), true).GetExactWindingCache();
            Assert.True(cache.BoundaryCount >= ExactWindingCache.BoundaryTreeMinEdges);
            cache.PrepareThresholdQueries();
            cacheT.PrepareThresholdQueries();
            Assert.True(cache.HasBoundaryTree && cacheT.HasBoundaryTree);
            int above = 0;
            foreach (Vector3d p in Points(500, 9)) {
                Vector3d pT = sim.Apply(p);
                foreach (double iso in new[] { 0.5, 0.0, -0.25 }) {
                    bool a = cache.EvaluateAbove(p, iso);
                    Assert.True(a == cacheT.EvaluateAbove(pT, iso), $"{name}, EvaluateAbove, point {p}, iso {iso}");
                    if (a) ++above;
                }
                Assert.True(cache.EvaluateAbsAbove(p, 0.5) == cacheT.EvaluateAbsAbove(pT, 0.5), $"{name}, EvaluateAbsAbove, point {p}");
            }
            Assert.True(above > 0);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void MeshDeviation_ExactWindingNumber_InvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 reference = ExactWindingAdoptionTests.Sphere(10, 1.0);
            DMesh3 query = ExactWindingAdoptionTests.Sphere(8, 0.85, new Vector3d(0.25, 0.0, 0.0));
            foreach (DMesh3 refMesh in new[] { reference, ExactWindingAdoptionTests.LargeBoundaryOpenGrid(160) }) {
                var dev = new MeshDeviation(query, refMesh) { Sign = MeshDeviation.Signs.ExactWindingNumber };
                var devT = new MeshDeviation(Invariance.Transformed(query, sim), Invariance.Transformed(refMesh, sim)) { Sign = MeshDeviation.Signs.ExactWindingNumber };
                Assert.True(dev.Compute());
                Assert.True(devT.Compute());
                int negative = 0;
                foreach (int vid in query.VertexIndices()) {
                    double d = dev.VertexDeviation[vid], dT = devT.VertexDeviation[vid] / sim.Scale;
                    Assert.True(d < 0 == dT < 0 || Math.Abs(d) < 1e-9, $"{name}, vertex {vid}: sign of {d} vs {dT}");
                    if (sim.IsExact)
                        Assert.Equal(d, dT);        // bit for bit
                    else
                        Assert.True(Math.Abs(d - dT) <= sim.DistanceResolution(2), $"{name}, vertex {vid}: {d} vs {dT}");
                    if (d < 0) ++negative;
                }
                if (ReferenceEquals(refMesh, reference))
                    Assert.True(negative > 0 && negative < query.VertexCount);
            }
        }

        static bool SameScaled(DMesh3 a, DMesh3 b, double scale)
        {
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return false;
            foreach (int v in a.VertexIndices()) {
                Vector3d p = a.GetVertex(v) * scale, q = b.GetVertex(v);
                if (p.x != q.x || p.y != q.y || p.z != q.z) return false;
            }
            return true;
        }

        [Theory]
        [InlineData(-10)]
        [InlineData(10)]
        public void SolidifyAndBoolean_UseExactWinding_PowerOfTwoScaleGivesTheSameMeshBitForBit(int exp)
        {
            double sc = Math.Pow(2.0, exp);
            DMesh3 box = ExactWindingAdoptionTests.Box(2.0);
            DMesh3 boxT = new DMesh3(box); MeshTransforms.Scale(boxT, sc);
            var s0 = new MeshWindingNumberSolidify(box) { Adaptive = true, GridResolution = 20, UseExactWinding = true, CleanupResult = false, ReduceTolerance = 0 };
            var s1 = new MeshWindingNumberSolidify(boxT) { Adaptive = true, GridResolution = 20, UseExactWinding = true, CleanupResult = false, ReduceTolerance = 0 };
            Assert.True(s0.Compute());
            Assert.True(s1.Compute());
            Assert.True(SameScaled(s0.Result, s1.Result, sc), "solidify, exact winding: not the same mesh under 2^k");

            DMesh3 a = ExactWindingAdoptionTests.Box(2.0, new Vector3d(-0.5, 0, 0)), b = ExactWindingAdoptionTests.Box(2.0, new Vector3d(0.5, 0, 0));
            DMesh3 aT = new DMesh3(a), bT = new DMesh3(b);
            MeshTransforms.Scale(aT, sc); MeshTransforms.Scale(bT, sc);
            var b0 = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24, UseExactWinding = true };
            var b1 = new MeshWindingNumberBoolean(aT, bT, MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24, UseExactWinding = true };
            Assert.True(b0.Compute());
            Assert.True(b1.Compute());
            Assert.True(SameScaled(b0.Result, b1.Result, sc), "boolean, exact winding: not the same mesh under 2^k");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SolidifyAndBoolean_UseExactWinding_InvariantUnderSimilarities(string name)
        {
            // the other frames: the same volume up to the cell size, closed and finite
            Similarity sim = Similarity.ByName(name);
            DMesh3 box = ExactWindingAdoptionTests.Box(2.0);
            var s = new MeshWindingNumberSolidify(Invariance.Transformed(box, sim)) { Adaptive = true, GridResolution = 20, UseExactWinding = true };
            Assert.True(s.Compute());
            Assert.True(s.Result.IsClosed());
            double vol = Invariance.Volume(s.Result) / (sim.Scale * sim.Scale * sim.Scale);
            Assert.True(Math.Abs(vol - 8.0) / 8.0 < 0.05, $"{name}: solidify volume {vol}");

            DMesh3 a = ExactWindingAdoptionTests.Box(2.0, new Vector3d(-0.5, 0, 0)), b = ExactWindingAdoptionTests.Box(2.0, new Vector3d(0.5, 0, 0));
            var u = new MeshWindingNumberBoolean(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24, UseExactWinding = true };
            Assert.True(u.Compute());
            Assert.True(u.Result.IsClosed());
            double uvol = Invariance.Volume(u.Result) / (sim.Scale * sim.Scale * sim.Scale);
            Assert.True(Math.Abs(uvol - 12.0) / 12.0 < 0.05, $"{name}: union volume {uvol}");
        }

        [Fact]
        public void WindingContouring_KeepsItsThreeArgumentConstructor()
        {
            // an optional parameter on the 4-argument constructor would drop this signature (binary break against 1.0.324)
            var ctor = typeof(WindingContouring).GetConstructor(new[] { typeof(IEnumerable<DMesh3>), typeof(Func<double[], double>), typeof(double) });
            Assert.NotNull(ctor);
            var four = typeof(WindingContouring).GetConstructor(new[] { typeof(IEnumerable<DMesh3>), typeof(Func<double[], double>), typeof(double), typeof(bool) });
            Assert.NotNull(four);
            Assert.False(four.GetParameters()[3].IsOptional);
            var wc = (WindingContouring)ctor.Invoke(new object[] { new DMesh3[] { ExactWindingAdoptionTests.Box(2.0) }, null, 1.5 });
            Assert.False(wc.UseExactWinding);
        }
    }
}
