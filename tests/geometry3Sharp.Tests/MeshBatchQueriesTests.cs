using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshBatchQueries (docs/design/BATCH_QUERIES.md): every batch result equals the single call for the same tree,
    /// bit for bit, whatever the options, thread count or order; the statuses of bad input; the 2^k scale invariance.
    /// </summary>
    public class MeshBatchQueriesTests
    {
        static DMesh3 Sphere(int edgeVertices = 20)
        {
            return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        static Vector3d RandDir(Random r)
        {
            Vector3d d;
            do d = new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);
            while (d.LengthSquared > 1 || d.LengthSquared < 1e-3);
            return d.Normalized;
        }

        // rays at random targets of the ball, from the sphere of radius 3, plus rays at mesh vertices and edge midpoints
        // (ties between triangles), plus a few that start on the surface and rays that miss
        static Ray3d[] Rays(DMesh3 mesh, int n, int seed)
        {
            var r = new Random(seed);
            var rays = new List<Ray3d>();
            for (int i = 0; i < n; ++i) {
                var o = 3 * RandDir(r);
                var target = 1.1 * RandDir(r) * Math.Pow(r.NextDouble(), 1 / 3.0);
                rays.Add(new Ray3d(o, (target - o).Normalized));
            }
            foreach (int v in mesh.VertexIndices().Where(v => v % 7 == 0)) {
                var o = 3 * RandDir(r);
                rays.Add(new Ray3d(o, (mesh.GetVertex(v) - o).Normalized));
            }
            foreach (int e in mesh.EdgeIndices().Where(e => e % 11 == 0)) {
                var ev = mesh.GetEdgeV(e);
                var o = 3 * RandDir(r);
                rays.Add(new Ray3d(o, (0.5 * (mesh.GetVertex(ev.a) + mesh.GetVertex(ev.b)) - o).Normalized));
            }
            for (int i = 0; i < 20; ++i)       // from the surface, outwards and inwards
                rays.Add(new Ray3d(mesh.GetVertex(i * 5), (i % 2 == 0 ? 1 : -1) * RandDir(r)));
            for (int i = 0; i < 20; ++i)       // misses
                rays.Add(new Ray3d(new Vector3d(5, 5, 5) + RandDir(r), RandDir(r)));
            return rays.ToArray();
        }

        static Vector3d[] Points(DMesh3 mesh, int n, int seed)
        {
            var r = new Random(seed);
            var pts = new List<Vector3d>();
            for (int i = 0; i < n; ++i)
                pts.Add(new Vector3d(r.NextDouble() * 3 - 1.5, r.NextDouble() * 3 - 1.5, r.NextDouble() * 3 - 1.5));
            foreach (int v in mesh.VertexIndices().Where(v => v % 13 == 0))
                pts.Add(mesh.GetVertex(v));                                    // on the surface
            foreach (int t in mesh.TriangleIndices().Where(t => t % 17 == 0))
                pts.Add(mesh.GetTriCentroid(t) + 0.01 * mesh.GetTriNormal(t)); // near it
            return pts.ToArray();
        }

        static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);

        // the single calls the batch must equal
        static RayHit SingleHit(DMeshAABBTree3 tree, Ray3d ray, double maxDist)
        {
            int tid = tree.FindNearestHitTriangle(ray, maxDist);
            return new RayHit() {
                Triangle = tid,
                T = tid == DMesh3.InvalidID ? double.MaxValue : MeshQueries.TriangleIntersection(tree.Mesh, tid, ray).RayParameter
            };
        }

        static NearestPointResult SingleNearest(DMeshAABBTree3 tree, Vector3d p, double maxDist)
        {
            double dSqr;
            int tid = tree.FindNearestTriangle(p, out dSqr, maxDist);
            if (tid == DMesh3.InvalidID)
                return new NearestPointResult() { Triangle = tid, Distance = double.MaxValue };
            var dt = MeshQueries.TriangleDistance(tree.Mesh, tid, p);
            return new NearestPointResult() { Triangle = tid, Point = dt.TriangleClosest, Bary = dt.TriangleBaryCoords, Distance = Math.Sqrt(dSqr) };
        }

        static void AssertSame(RayHit expected, RayHit actual, int i)
        {
            Assert.True(expected.Triangle == actual.Triangle && Bits(expected.T) == Bits(actual.T),
                $"ray {i}: single {expected.Triangle} t={expected.T:R}, batch {actual.Triangle} t={actual.T:R}");
        }

        static void AssertSame(NearestPointResult e, NearestPointResult a, int i)
        {
            Assert.True(e.Triangle == a.Triangle && Bits(e.Distance) == Bits(a.Distance) && e.Point == a.Point && e.Bary == a.Bary,
                $"point {i}: single {e.Triangle} d={e.Distance:R} {e.Point}, batch {a.Triangle} d={a.Distance:R} {a.Point}");
        }

        public static IEnumerable<object[]> OptionCases()
        {
            foreach (bool parallel in new[] { false, true })
                foreach (bool sort in new[] { false, true })
                    yield return new object[] { parallel, sort };
        }

        // ---- bit identity with the single calls ----

        [Theory, MemberData(nameof(OptionCases))]
        public void IntersectBatch_EqualsSingleCalls(bool parallel, bool sort)
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 3000, 1);
            Assert.True(rays.Length > 1024);       // sorting and threads are in play
            var hits = new RayHit[rays.Length]; var st = new RayStatus[rays.Length];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IntersectBatch(tree, rays, hits, st, new BatchOptions() { Parallel = parallel, SortForCoherence = sort }));
            int nhit = 0;
            for (int i = 0; i < rays.Length; ++i) {
                AssertSame(SingleHit(tree, rays[i], double.MaxValue), hits[i], i);
                Assert.Equal(RayStatus.Ok, st[i]);
                if (hits[i].Triangle != DMesh3.InvalidID) nhit++;
            }
            Assert.InRange(nhit, rays.Length / 4, rays.Length - 10);      // hits and misses both occur
        }

        [Theory]
        [InlineData(2.0)]
        [InlineData(2.5)]
        [InlineData(0.0)]
        public void IntersectBatch_MaxDistance_EqualsSingleCalls(double maxDistance)
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 2000, 2);
            var hits = new RayHit[rays.Length];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IntersectBatch(tree, rays, hits, null, new BatchOptions() { MaxDistance = maxDistance }));
            for (int i = 0; i < rays.Length; ++i)
                AssertSame(SingleHit(tree, rays[i], maxDistance), hits[i], i);
        }

        [Fact]
        public void IntersectBatch_ReportsParameterOfTheHit()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 100, 3);
            var hits = new RayHit[rays.Length];
            MeshBatchQueries.IntersectBatch(tree, rays, hits);
            for (int i = 0; i < rays.Length; ++i) {
                if (hits[i].Triangle == DMesh3.InvalidID) { Assert.Equal(double.MaxValue, hits[i].T); continue; }
                Assert.True(hits[i].T >= 0);
                Vector3d p = rays[i].PointAt(hits[i].T);
                Assert.True(Math.Abs(p.Length - 1) < 0.01);       // on the unit sphere (a triangle surface lies inside it)
            }
        }

        [Theory, MemberData(nameof(OptionCases))]
        public void AnyHitBatch_EqualsSingleCalls(bool parallel, bool sort)
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 3000, 4);
            var hit = new bool[rays.Length];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.AnyHitBatch(tree, rays, hit, null, new BatchOptions() { Parallel = parallel, SortForCoherence = sort, MaxDistance = 2.5 }));
            for (int i = 0; i < rays.Length; ++i)
                Assert.Equal(tree.FindNearestHitTriangle(rays[i], 2.5) != DMesh3.InvalidID, hit[i]);
            Assert.Contains(true, hit); Assert.Contains(false, hit);
        }

        [Theory, MemberData(nameof(OptionCases))]
        public void NearestPointBatch_EqualsSingleCalls(bool parallel, bool sort)
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var pts = Points(mesh, 3000, 5);
            var res = new NearestPointResult[pts.Length];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.NearestPointBatch(tree, pts, res, null, new BatchOptions() { Parallel = parallel, SortForCoherence = sort }));
            for (int i = 0; i < pts.Length; ++i)
                AssertSame(SingleNearest(tree, pts[i], double.MaxValue), res[i], i);
        }

        [Theory]
        [InlineData(0.05)]
        [InlineData(0.5)]
        public void NearestPointBatch_MaxDistance_EqualsSingleCalls(double maxDistance)
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var pts = Points(mesh, 2000, 6);
            var res = new NearestPointResult[pts.Length];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.NearestPointBatch(tree, pts, res, null, new BatchOptions() { MaxDistance = maxDistance }));
            int none = 0;
            for (int i = 0; i < pts.Length; ++i) {
                AssertSame(SingleNearest(tree, pts[i], maxDistance), res[i], i);
                if (res[i].Triangle == DMesh3.InvalidID) { none++; Assert.Equal(double.MaxValue, res[i].Distance); Assert.Equal(Vector3d.Zero, res[i].Point); }
            }
            Assert.True(none > 0 && none < pts.Length);
        }

        [Fact]
        public void TriangleFilter_IsHonoured()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true) { TriangleFilterF = t => t % 3 != 0 };
            var rays = Rays(mesh, 2000, 7); var pts = Points(mesh, 2000, 8);
            var hits = new RayHit[rays.Length]; var res = new NearestPointResult[pts.Length];
            MeshBatchQueries.IntersectBatch(tree, rays, hits);
            MeshBatchQueries.NearestPointBatch(tree, pts, res);
            for (int i = 0; i < rays.Length; ++i) AssertSame(SingleHit(tree, rays[i], double.MaxValue), hits[i], i);
            for (int i = 0; i < pts.Length; ++i) AssertSame(SingleNearest(tree, pts[i], double.MaxValue), res[i], i);
            Assert.DoesNotContain(hits, h => h.Triangle != DMesh3.InvalidID && h.Triangle % 3 == 0);
        }

        // ---- determinism, reuse ----

        [Fact]
        public void Results_DoNotDependOnOptionsThreadsOrRepeats()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 4000, 9); var pts = Points(mesh, 4000, 10);
            RayHit[] refHits = null; NearestPointResult[] refRes = null;
            for (int run = 0; run < 3; ++run)
                foreach (var oc in OptionCases()) {
                    var hits = new RayHit[rays.Length]; var res = new NearestPointResult[pts.Length];
                    var opt = new BatchOptions() { Parallel = (bool)oc[0], SortForCoherence = (bool)oc[1] };
                    MeshBatchQueries.IntersectBatch(tree, rays, hits, null, opt);
                    MeshBatchQueries.NearestPointBatch(tree, pts, res, null, opt);
                    if (refHits == null) { refHits = hits; refRes = res; continue; }
                    for (int i = 0; i < hits.Length; ++i) AssertSame(refHits[i], hits[i], i);
                    for (int i = 0; i < res.Length; ++i) AssertSame(refRes[i], res[i], i);
                }
        }

        [Fact]
        public void SerialOnThisThread_EqualsParallel()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 4000, 11);
            var a = new RayHit[rays.Length]; var b = new RayHit[rays.Length];
            MeshBatchQueries.IntersectBatch(tree, rays, a);
            gParallel.SerialOnThisThread = true;
            try { MeshBatchQueries.IntersectBatch(tree, rays, b); }
            finally { gParallel.SerialOnThisThread = false; }
            for (int i = 0; i < a.Length; ++i) AssertSame(a[i], b[i], i);
        }

        [Fact]
        public void Reuse_SameBuffersTwice_AndStaleBuffersAreOverwritten()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 2000, 12); var pts = Points(mesh, 2000, 13);
            var hits = new RayHit[rays.Length]; var res = new NearestPointResult[pts.Length];
            var st = new RayStatus[rays.Length]; var pst = new PointStatus[pts.Length];
            for (int i = 0; i < hits.Length; ++i) { hits[i] = new RayHit() { Triangle = 12345, T = -1 }; st[i] = RayStatus.NonFinite; }
            for (int i = 0; i < res.Length; ++i) { res[i] = new NearestPointResult() { Triangle = 12345, Distance = -1 }; pst[i] = PointStatus.NonFinite; }
            for (int run = 0; run < 2; ++run) {
                MeshBatchQueries.IntersectBatch(tree, rays, hits, st);
                MeshBatchQueries.NearestPointBatch(tree, pts, res, pst);
                for (int i = 0; i < rays.Length; ++i) { AssertSame(SingleHit(tree, rays[i], double.MaxValue), hits[i], i); Assert.Equal(RayStatus.Ok, st[i]); }
                for (int i = 0; i < pts.Length; ++i) { AssertSame(SingleNearest(tree, pts[i], double.MaxValue), res[i], i); Assert.Equal(PointStatus.Ok, pst[i]); }
            }
        }

        [Fact]
        public void TwoTreesOneBatchEach_RunConcurrently()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 3000, 14);
            var expected = rays.Select(r => SingleHit(tree, r, double.MaxValue)).ToArray();
            var results = new RayHit[4][];
            System.Threading.Tasks.Parallel.For(0, 4, k => {
                results[k] = new RayHit[rays.Length];
                MeshBatchQueries.IntersectBatch(tree, rays, results[k], null, new BatchOptions() { SortForCoherence = k % 2 == 0 });
            });
            foreach (var r in results)
                for (int i = 0; i < r.Length; ++i) AssertSame(expected[i], r[i], i);
        }

        // ---- invariance over similarities ----

        [Fact]
        public void Batches_AreExactForEverySimilarity_AndPow2ScalesAreBitIdentical()
        {
            var mesh = Sphere(12);
            var rays = Rays(mesh, 1500, 15); var pts = Points(mesh, 1500, 16);
            var refTree = new DMeshAABBTree3(mesh, true);
            var refHits = new RayHit[rays.Length]; var refRes = new NearestPointResult[pts.Length];
            MeshBatchQueries.IntersectBatch(refTree, rays, refHits);
            MeshBatchQueries.NearestPointBatch(refTree, pts, refRes);
            foreach (var sim in Similarity.All()) {
                var m = Invariance.Transformed(mesh, sim); var tree = new DMeshAABBTree3(m, true);
                var tr = rays.Select(r => new Ray3d(sim.Apply(r.Origin), sim.IsExact ? r.Direction : sim.ApplyDirection(r.Direction).Normalized, true)).ToArray();
                var tp = pts.Select(sim.Apply).ToArray();
                var hits = new RayHit[tr.Length]; var res = new NearestPointResult[tp.Length];
                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IntersectBatch(tree, tr, hits));
                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.NearestPointBatch(tree, tp, res));
                // (1) the batch equals the single calls on the transformed input
                for (int i = 0; i < tr.Length; ++i) AssertSame(SingleHit(tree, tr[i], double.MaxValue), hits[i], i);
                for (int i = 0; i < tp.Length; ++i) AssertSame(SingleNearest(tree, tp[i], double.MaxValue), res[i], i);
                if (!sim.IsExact) continue;
                // (2) scaling by 2^k: the same triangles, parameters and distances scaled by 2^k, bit for bit
                for (int i = 0; i < tr.Length; ++i) {
                    Assert.Equal(refHits[i].Triangle, hits[i].Triangle);
                    double expected = refHits[i].Triangle == DMesh3.InvalidID ? double.MaxValue : sim.Scale * refHits[i].T;
                    Assert.Equal(Bits(expected), Bits(hits[i].T));
                }
                for (int i = 0; i < tp.Length; ++i) {
                    // (FindNearestTriangle's choice among equidistant triangles, eg for a point on a vertex of the mesh, follows
                    // the traversal order, which the absolute box padding changes with the scale: that is the single call's
                    // property, not the batch's. The distance is the same; the point too where the triangle is.)
                    Assert.Equal(Bits(sim.Scale * refRes[i].Distance), Bits(res[i].Distance));
                    if (refRes[i].Triangle == res[i].Triangle)
                        Assert.Equal(sim.Apply(refRes[i].Point), res[i].Point);
                }
            }
        }

        // ---- statuses and arguments ----

        [Fact]
        public void BadRays_GetStatusAndNoHit_OthersAreAnswered()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var good = new Ray3d(new Vector3d(0, 0, 3), -Vector3d.AxisZ);
            var rays = new[] {
                good,
                new Ray3d(new Vector3d(0, 0, 3), new Vector3d(0, 0, -2), true),                      // not normalized
                new Ray3d(new Vector3d(double.NaN, 0, 3), -Vector3d.AxisZ, true),                    // non-finite origin
                new Ray3d(new Vector3d(0, 0, 3), new Vector3d(0, double.PositiveInfinity, 0), true), // non-finite direction
                good };
            var hits = new RayHit[rays.Length]; var st = new RayStatus[rays.Length]; var any = new bool[rays.Length];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IntersectBatch(tree, rays, hits, st));
            Assert.Equal(new[] { RayStatus.Ok, RayStatus.NotNormalized, RayStatus.NonFinite, RayStatus.NonFinite, RayStatus.Ok }, st);
            Assert.Throws<Exception>(() => tree.FindNearestHitTriangle(rays[1]));          // what the single call does
            AssertSame(SingleHit(tree, good, double.MaxValue), hits[0], 0);
            AssertSame(hits[0], hits[4], 4);
            Assert.NotEqual(DMesh3.InvalidID, hits[0].Triangle);
            foreach (int i in new[] { 1, 2, 3 }) { Assert.Equal(DMesh3.InvalidID, hits[i].Triangle); Assert.Equal(double.MaxValue, hits[i].T); }
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.AnyHitBatch(tree, rays, any, st));
            Assert.Equal(new[] { true, false, false, false, true }, any);
        }

        [Fact]
        public void NonFinitePoints_GetStatusAndNoResult()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var pts = new[] { new Vector3d(0, 0, 2), new Vector3d(double.NaN, 0, 0), new Vector3d(0, double.NegativeInfinity, 0), new Vector3d(0.5, 0, 0) };
            var res = new NearestPointResult[4]; var st = new PointStatus[4];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.NearestPointBatch(tree, pts, res, st));
            Assert.Equal(new[] { PointStatus.Ok, PointStatus.NonFinite, PointStatus.NonFinite, PointStatus.Ok }, st);
            AssertSame(SingleNearest(tree, pts[0], double.MaxValue), res[0], 0);
            AssertSame(SingleNearest(tree, pts[3], double.MaxValue), res[3], 3);
            Assert.Equal(DMesh3.InvalidID, res[1].Triangle); Assert.Equal(double.MaxValue, res[2].Distance);
        }

        [Fact]
        public void NonFiniteQueriesInALargeBatch_DoNotDisturbTheOthers()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 3000, 17); var pts = Points(mesh, 3000, 18);
            for (int i = 0; i < rays.Length; i += 97) rays[i] = new Ray3d(new Vector3d(double.NaN, 1, 1), Vector3d.AxisX, true);
            for (int i = 0; i < pts.Length; i += 89) pts[i] = new Vector3d(1, double.PositiveInfinity, 1);
            var hits = new RayHit[rays.Length]; var res = new NearestPointResult[pts.Length];
            var st = new RayStatus[rays.Length]; var pst = new PointStatus[pts.Length];
            MeshBatchQueries.IntersectBatch(tree, rays, hits, st);
            MeshBatchQueries.NearestPointBatch(tree, pts, res, pst);
            for (int i = 0; i < rays.Length; ++i) {
                if (i % 97 == 0) { Assert.Equal(RayStatus.NonFinite, st[i]); Assert.Equal(DMesh3.InvalidID, hits[i].Triangle); }
                else AssertSame(SingleHit(tree, rays[i], double.MaxValue), hits[i], i);
            }
            for (int i = 0; i < pts.Length; ++i) {
                if (i % 89 == 0) { Assert.Equal(PointStatus.NonFinite, pst[i]); Assert.Equal(DMesh3.InvalidID, res[i].Triangle); }
                else AssertSame(SingleNearest(tree, pts[i], double.MaxValue), res[i], i);
            }
        }

        [Fact]
        public void ArgumentsAndTreeState_AreReportedAsStatus()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 10, 19); var hits = new RayHit[rays.Length]; var pts = Points(mesh, 10, 20); var res = new NearestPointResult[pts.Length];
            Assert.Equal(BatchStatus.InvalidArguments, MeshBatchQueries.IntersectBatch(null, rays, hits));
            Assert.Equal(BatchStatus.InvalidArguments, MeshBatchQueries.IntersectBatch(tree, null, hits));
            Assert.Equal(BatchStatus.InvalidArguments, MeshBatchQueries.IntersectBatch(tree, rays, null));
            Assert.Equal(BatchStatus.InvalidArguments, MeshBatchQueries.AnyHitBatch(tree, rays, null));
            Assert.Equal(BatchStatus.InvalidArguments, MeshBatchQueries.NearestPointBatch(tree, null, res));
            Assert.Equal(BatchStatus.InvalidArguments, MeshBatchQueries.IntersectBatch(tree, rays, hits, null, new BatchOptions() { MaxDistance = -1 }));
            Assert.Equal(BatchStatus.InvalidArguments, MeshBatchQueries.NearestPointBatch(tree, pts, res, null, new BatchOptions() { MaxDistance = double.NaN }));
            Assert.Equal(BatchStatus.BufferTooSmall, MeshBatchQueries.IntersectBatch(tree, rays, new RayHit[rays.Length - 1]));
            Assert.Equal(BatchStatus.BufferTooSmall, MeshBatchQueries.IntersectBatch(tree, rays, hits, new RayStatus[1]));
            Assert.Equal(BatchStatus.BufferTooSmall, MeshBatchQueries.AnyHitBatch(tree, rays, new bool[3]));
            Assert.Equal(BatchStatus.BufferTooSmall, MeshBatchQueries.NearestPointBatch(tree, pts, new NearestPointResult[2]));
            Assert.Equal(BatchStatus.BufferTooSmall, MeshBatchQueries.NearestPointBatch(tree, pts, res, new PointStatus[2]));
            // a larger buffer is fine and its tail is left alone
            var big = new RayHit[rays.Length + 3]; big[rays.Length] = new RayHit() { Triangle = 77, T = 1 };
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IntersectBatch(tree, rays, big));
            Assert.Equal(77, big[rays.Length].Triangle);
        }

        [Fact]
        public void StaleOrUnbuiltTree_IsMeshModified()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh);          // not built
            var rays = Rays(mesh, 10, 21); var hits = new RayHit[rays.Length]; var pts = Points(mesh, 10, 22); var res = new NearestPointResult[pts.Length];
            Assert.Equal(BatchStatus.MeshModified, MeshBatchQueries.IntersectBatch(tree, rays, hits));
            tree.Build();
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IntersectBatch(tree, rays, hits));
            mesh.SetVertex(0, mesh.GetVertex(0) + new Vector3d(0.01, 0, 0));
            Assert.Equal(BatchStatus.MeshModified, MeshBatchQueries.IntersectBatch(tree, rays, hits));
            Assert.Equal(BatchStatus.MeshModified, MeshBatchQueries.AnyHitBatch(tree, rays, new bool[rays.Length]));
            Assert.Equal(BatchStatus.MeshModified, MeshBatchQueries.NearestPointBatch(tree, pts, res));
            Assert.Throws<Exception>(() => tree.FindNearestTriangle(pts[0]));  // the single call throws
        }

        [Fact]
        public void Cancellation_ReturnsCancelled()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            var rays = Rays(mesh, 3000, 23); var hits = new RayHit[rays.Length];
            using (var cts = new CancellationTokenSource()) {
                cts.Cancel();
                Assert.Equal(BatchStatus.Cancelled, MeshBatchQueries.IntersectBatch(tree, rays, hits, null, new BatchOptions() { Cancel = cts.Token }));
                Assert.Equal(BatchStatus.Cancelled, MeshBatchQueries.NearestPointBatch(tree, Points(mesh, 3000, 24), new NearestPointResult[3010 + mesh.TriangleCount], null, new BatchOptions() { Cancel = cts.Token, Parallel = false }));
            }
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IntersectBatch(tree, rays, hits, null, new BatchOptions() { Cancel = CancellationToken.None }));
        }

        [Fact]
        public void EmptyInput_AndEmptyMesh()
        {
            var mesh = Sphere(); var tree = new DMeshAABBTree3(mesh, true);
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IntersectBatch(tree, new Ray3d[0], new RayHit[0]));
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.NearestPointBatch(tree, new Vector3d[0], new NearestPointResult[0]));
            var empty = new DMesh3(); var etree = new DMeshAABBTree3(empty, true);
            var rays = new[] { new Ray3d(Vector3d.Zero, Vector3d.AxisX) }; var hits = new RayHit[1]; var pts = new[] { Vector3d.Zero }; var res = new NearestPointResult[1];
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IntersectBatch(etree, rays, hits));
            Assert.Equal(BatchStatus.Ok, MeshBatchQueries.NearestPointBatch(etree, pts, res));
            Assert.Equal(DMesh3.InvalidID, hits[0].Triangle); Assert.Equal(DMesh3.InvalidID, res[0].Triangle);
            Assert.Equal(double.MaxValue, res[0].Distance);
        }
    }
}
