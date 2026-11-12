using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 inventory cell K (spatial structures): seeded differential tests against brute force loops for the
    /// structures the gap list calls thin: PointAABBTree3 (nearest point), MeshBatchQueries (every batch result equals the
    /// single call bit for bit, for every option and query order) and WindingNumberOctree (node values against the
    /// brute force winding number). Point sets and rays are random, clustered, collinear, coplanar and duplicated, at
    /// binary scales from 2^-100 to 2^200 (inside the supported range of QA.md 2.5a: the ray queries are good to 2^1000,
    /// nearest queries to a squared distance that is representable). Plain System.Random with fixed seeds, no CsCheck, so
    /// the tests also run on net48; each test takes well under a second.
    /// </summary>
    public class SpatialStructuresDifferentialTests
    {
        static readonly int[] Exponents = { -100, -24, 0, 20, 200 };

        static Vector3d Scale(Vector3d v, double s) => new Vector3d(v.x * s, v.y * s, v.z * s);

        // ---- point sets -------------------------------------------------------------------------------------------

        enum Family { Random, Clustered, Collinear, Coplanar, Duplicates, Grid }

        static List<Vector3d> Points(Family f, int n, Random rng)
        {
            var p = new List<Vector3d>();
            for (int i = 0; i < n; ++i) {
                switch (f) {
                    case Family.Random:
                        p.Add(new Vector3d(rng.NextDouble(), rng.NextDouble(), rng.NextDouble())); break;
                    case Family.Clustered: {
                        double c = (i % 3) * 8;    // three tight clusters
                        p.Add(new Vector3d(c + rng.NextDouble() * 1e-3, rng.NextDouble() * 1e-3, c + rng.NextDouble() * 1e-3)); break;
                    }
                    case Family.Collinear: {
                        double t = rng.NextDouble();
                        p.Add(new Vector3d(t, 2 * t, -3 * t)); break;
                    }
                    case Family.Coplanar:
                        p.Add(new Vector3d(rng.NextDouble(), rng.NextDouble(), 0.5)); break;
                    case Family.Duplicates: {
                        int k = rng.Next(6);
                        p.Add(new Vector3d(k * 0.25, (k * 7 % 5) * 0.25, 0)); break;
                    }
                    default:
                        p.Add(new Vector3d(i % 5, (i / 5) % 5, i / 25)); break;
                }
            }
            return p;
        }

        static DMesh3 PointMesh(List<Vector3d> pts, double scale)
        {
            var m = new DMesh3(false, false, false);
            foreach (Vector3d v in pts) m.AppendVertex(Scale(v, scale));
            return m;
        }

        static PointAABBTree3 BuiltTree(DMesh3 m, Func<int, bool> filter = null)
        {
            // (a property initializer runs after the constructor's build: build explicitly with the leaf size set)
            var tree = new PointAABBTree3(m, false) { LeafMaxPointCount = 4, PointFilterF = filter };
            tree.Build();
            return tree;
        }

        static int BruteNearest(DMesh3 m, Vector3d q, double maxDist, Func<int, bool> filter, out double bestSqr)
        {
            bestSqr = maxDist < double.MaxValue ? maxDist * maxDist : double.MaxValue;
            int best = DMesh3.InvalidID;
            foreach (int v in m.VertexIndices()) {
                if (filter != null && filter(v) == false) continue;
                double d = m.GetVertex(v).DistanceSquared(q);
                if (d < bestSqr) { bestSqr = d; best = v; }
            }
            return best;
        }

        [Theory]
        [InlineData(PointAABBTree3.BuildStrategy.TopDownMidpoint)]
        [InlineData(PointAABBTree3.BuildStrategy.TopDownMedian)]
        public void PointTree_NearestPoint_EqualsBruteForce(PointAABBTree3.BuildStrategy strategy)
        {
            var rng = new Random(2026111201);
            foreach (Family fam in Enum.GetValues(typeof(Family)))
                foreach (int e in Exponents) {
                    double s = Math.Pow(2, e);
                    List<Vector3d> pts = Points(fam, 150, rng);
                    DMesh3 m = PointMesh(pts, s);
                    var tree = new PointAABBTree3(m, false) { LeafMaxPointCount = 4 };
                    tree.Build(strategy);
                    for (int qi = 0; qi < 25; ++qi) {
                        // queries on a point of the set, near it, and far away
                        Vector3d q = qi % 3 == 0 ? m.GetVertex(rng.Next(pts.Count))
                            : qi % 3 == 1 ? Scale(new Vector3d(rng.NextDouble() * 2 - 0.5, rng.NextDouble() * 2 - 0.5, rng.NextDouble() - 0.25), s)
                            : Scale(new Vector3d(rng.NextDouble() * 50 - 25, rng.NextDouble() * 50 - 25, rng.NextDouble() * 50 - 25), s);
                        int found = tree.FindNearestPoint(q);
                        int brute = BruteNearest(m, q, double.MaxValue, null, out double bestSqr);
                        Assert.True(found != DMesh3.InvalidID, $"{fam} 2^{e} query {qi}: no point found");
                        Assert.Equal(bestSqr, m.GetVertex(found).DistanceSquared(q));

                        // twice the nearest distance, half of it, and a hair above and below it (the limit is strict; within an ulp of it the
                        // box distances of the tree and the point distance of the brute force may round either way, so not exactly at it)
                        foreach (double maxDist in new[] { Math.Sqrt(bestSqr) * 2, Math.Sqrt(bestSqr) * 0.5, Math.Sqrt(bestSqr) * (1 + 1e-9), Math.Sqrt(bestSqr) * (1 - 1e-9) }) {
                            int foundMax = tree.FindNearestPoint(q, maxDist);
                            int bruteMax = BruteNearest(m, q, maxDist, null, out double bestMaxSqr);
                            Assert.True(bruteMax == DMesh3.InvalidID == (foundMax == DMesh3.InvalidID), $"{fam} 2^{e} query {qi} limit {maxDist} (nearest {Math.Sqrt(bestSqr)}): brute {bruteMax}, tree {foundMax}");
                            if (foundMax != DMesh3.InvalidID)
                                Assert.Equal(bestMaxSqr, m.GetVertex(foundMax).DistanceSquared(q));
                        }
                    }
                }
        }

        [Fact]
        public void PointTree_PointFilter_IsHonoured()
        {
            var rng = new Random(2026111202);
            foreach (Family fam in new[] { Family.Random, Family.Duplicates, Family.Clustered }) {
                List<Vector3d> pts = Points(fam, 120, rng);
                DMesh3 m = PointMesh(pts, 1);
                Func<int, bool> filter = v => v % 3 != 0;
                PointAABBTree3 tree = BuiltTree(m, filter);
                for (int qi = 0; qi < 30; ++qi) {
                    Vector3d q = new Vector3d(rng.NextDouble() * 3, rng.NextDouble() * 3, rng.NextDouble());
                    int found = tree.FindNearestPoint(q);
                    BruteNearest(m, q, double.MaxValue, filter, out double bestSqr);
                    Assert.True(filter(found), $"{fam}: the filtered point {found} was returned");
                    Assert.Equal(bestSqr, m.GetVertex(found).DistanceSquared(q));
                }
            }
        }

        [Fact]
        public void PointTree_PowerOfTwoScale_GivesTheSamePosition()
        {
            var rng = new Random(2026111203);
            foreach (Family fam in Enum.GetValues(typeof(Family))) {
                List<Vector3d> pts = Points(fam, 150, rng);
                var queries = new List<Vector3d>();
                for (int i = 0; i < 30; ++i) queries.Add(new Vector3d(rng.NextDouble() * 6 - 1, rng.NextDouble() * 6 - 1, rng.NextDouble() * 2 - 0.5));
                // the point found (not its id: among identical points the tree may return another one)
                var reference = new List<Vector3d>();
                DMesh3 m0 = PointMesh(pts, 1);
                PointAABBTree3 t0 = BuiltTree(m0);
                foreach (Vector3d q in queries) reference.Add(m0.GetVertex(t0.FindNearestPoint(q)));
                foreach (int e in Exponents) {
                    double s = Math.Pow(2, e);
                    DMesh3 ms = PointMesh(pts, s);
                    PointAABBTree3 tree = BuiltTree(ms);
                    for (int i = 0; i < queries.Count; ++i)
                        Assert.True(Scale(reference[i], s) == ms.GetVertex(tree.FindNearestPoint(Scale(queries[i], s))), $"{fam} 2^{e} query {i}: another point");
                }
            }
        }

        // ---- batch queries ----------------------------------------------------------------------------------------

        static DMesh3 BatchMesh(int kind, double scale)
        {
            DMesh3 m;
            switch (kind) {
                case 0: m = Defects.Sphere(Vector3d.Zero, 1, 5); break;
                case 1: m = Defects.Sphere(new Vector3d(0.3, -0.2, 0.1), 0.6, 4); break;
                default: {
                    // two spheres and a flat sheet: ties, coplanar triangles and several components
                    m = Defects.Sphere(new Vector3d(-2, 0, 0), 1, 3);
                    MeshEditor.Append(m, Defects.Sphere(new Vector3d(2, 0, 0), 1, 3));
                    // a flat sheet in the plane z = 0 through both centres (coplanar triangles, rays in its plane)
                    MeshEditor.Append(m, new TrivialRectGenerator { Width = 6, Height = 3, IndicesMap = new Index2i(1, 2) }.Generate().MakeDMesh());
                    break;
                }
            }
            MeshTransforms.Scale(m, scale);
            return m;
        }

        static Ray3d[] Rays(DMesh3 m, double scale, Random rng, int n)
        {
            var rays = new Ray3d[n];
            for (int i = 0; i < n; ++i) {
                Vector3d o;
                if (i % 4 == 0) o = m.GetVertex(rng.Next(m.MaxVertexID));       // on the surface
                else o = Scale(new Vector3d(rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3), scale);
                Vector3d d = i % 5 == 0 ? Vector3d.AxisY * (rng.Next(2) == 0 ? 1 : -1)          // axis aligned (edges, ties)
                    : new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
                if (d.LengthSquared < 1e-6) d = Vector3d.AxisX;
                d.Normalize();
                rays[i] = new Ray3d(o, d);
            }
            return rays;
        }

        static Vector3d[] QueryPoints(double scale, Random rng, int n)
        {
            var p = new Vector3d[n];
            for (int i = 0; i < n; ++i)
                p[i] = Scale(new Vector3d(rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3), scale);
            return p;
        }

        public static IEnumerable<object[]> BatchCases()
        {
            foreach (int kind in new[] { 0, 1, 2 })
                foreach (int e in new[] { -24, 0, 20, 200 })
                    yield return new object[] { kind, e };
        }

        [Theory]
        [MemberData(nameof(BatchCases))]
        public void IntersectBatch_EqualsTheSingleQueries_ForEveryOptionAndOrder(int kind, int e)
        {
            double s = Math.Pow(2, e);
            DMesh3 m = BatchMesh(kind, s);
            var tree = new DMeshAABBTree3(m, true);
            var rng = new Random(2026111210 + kind * 100 + e);
            Ray3d[] rays = Rays(m, s, rng, 2100);   // above MinSortCount and BlockSize (1024): the sorted and the parallel path

            var single = new RayHit[rays.Length];
            var singleAny = new bool[rays.Length];
            for (int i = 0; i < rays.Length; ++i) {
                int tid = tree.FindNearestHitTriangle(rays[i]);
                single[i] = new RayHit { Triangle = tid, T = tid == DMesh3.InvalidID ? double.MaxValue : MeshQueries.TriangleIntersection(m, tid, rays[i]).RayParameter };
                singleAny[i] = tid != DMesh3.InvalidID;
            }
            foreach (bool parallel in new[] { false, true })
                foreach (bool sort in new[] { false, true }) {
                    var opt = new BatchOptions { Parallel = parallel, SortForCoherence = sort };
                    var hits = new RayHit[rays.Length];
                    var status = new RayStatus[rays.Length];
                    Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IntersectBatch(tree, rays, hits, status, opt));
                    for (int i = 0; i < rays.Length; ++i) {
                        Assert.Equal(RayStatus.Ok, status[i]);
                        Assert.True(single[i].Triangle == hits[i].Triangle, $"2^{e} kind {kind} parallel {parallel} sort {sort} ray {i}: triangle {hits[i].Triangle}, single {single[i].Triangle}");
                        Assert.Equal(BitConverter.DoubleToInt64Bits(single[i].T), BitConverter.DoubleToInt64Bits(hits[i].T));
                    }
                    var any = new bool[rays.Length];
                    Assert.Equal(BatchStatus.Ok, MeshBatchQueries.AnyHitBatch(tree, rays, any, null, opt));
                    for (int i = 0; i < rays.Length; ++i) Assert.Equal(singleAny[i], any[i]);

                    // reversed order of the queries: every result at the reversed index is the same
                    Ray3d[] rev = (Ray3d[])rays.Clone();
                    Array.Reverse(rev);
                    var revHits = new RayHit[rays.Length];
                    MeshBatchQueries.IntersectBatch(tree, rev, revHits, null, opt);
                    for (int i = 0; i < rays.Length; ++i) {
                        Assert.Equal(hits[i].Triangle, revHits[rays.Length - 1 - i].Triangle);
                        Assert.Equal(BitConverter.DoubleToInt64Bits(hits[i].T), BitConverter.DoubleToInt64Bits(revHits[rays.Length - 1 - i].T));
                    }
                }
        }

        [Theory]
        [MemberData(nameof(BatchCases))]
        public void BatchQueries_MaxDistance_IsTheSingleQueriesLimit(int kind, int e)
        {
            double s = Math.Pow(2, e);
            DMesh3 m = BatchMesh(kind, s);
            var tree = new DMeshAABBTree3(m, true);
            var rng = new Random(2026111410 + kind * 100 + e);
            Ray3d[] rays = Rays(m, s, rng, 2100);
            Vector3d[] pts = QueryPoints(s, rng, 2100);
            foreach (double limit in new[] { 0.25 * s, 1.5 * s }) {
                var opt = new BatchOptions { Parallel = true, MaxDistance = limit };
                var hits = new RayHit[rays.Length];
                var any = new bool[rays.Length];
                MeshBatchQueries.IntersectBatch(tree, rays, hits, null, opt);
                MeshBatchQueries.AnyHitBatch(tree, rays, any, null, opt);
                int limited = 0;
                for (int i = 0; i < rays.Length; ++i) {
                    int tid = tree.FindNearestHitTriangle(rays[i], limit);
                    Assert.True(tid == hits[i].Triangle, $"2^{e} kind {kind} limit {limit} ray {i}: {hits[i].Triangle}, single {tid}");
                    Assert.Equal(tid != DMesh3.InvalidID, any[i]);
                    if (tid != tree.FindNearestHitTriangle(rays[i])) ++limited;
                }
                var res = new NearestPointResult[pts.Length];
                MeshBatchQueries.NearestPointBatch(tree, pts, res, null, opt);
                for (int i = 0; i < pts.Length; ++i)
                    Assert.True(tree.FindNearestTriangle(pts[i], limit) == res[i].Triangle, $"2^{e} kind {kind} limit {limit} point {i}");
                // the limit must cut something in a case (else the test is vacuous)
                if (kind == 0 && limit < s) Assert.True(limited > 0, "vacuous: the limit changed no ray");
            }
        }

        [Theory]
        [MemberData(nameof(BatchCases))]
        public void NearestPointAndInsideBatch_EqualTheSingleQueries(int kind, int e)
        {
            double s = Math.Pow(2, e);
            DMesh3 m = BatchMesh(kind, s);
            var tree = new DMeshAABBTree3(m, true);
            var rng = new Random(2026111310 + kind * 100 + e);
            Vector3d[] pts = QueryPoints(s, rng, 2100);   // see above

            var singleNearest = new int[pts.Length];
            var singleDist = new double[pts.Length];
            for (int i = 0; i < pts.Length; ++i) {
                singleNearest[i] = tree.FindNearestTriangle(pts[i], out double dsqr);
                singleDist[i] = dsqr;
            }
            foreach (bool parallel in new[] { false, true }) {
                var opt = new BatchOptions { Parallel = parallel, SortForCoherence = parallel };
                var res = new NearestPointResult[pts.Length];
                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.NearestPointBatch(tree, pts, res, null, opt));
                for (int i = 0; i < pts.Length; ++i) {
                    Assert.True(singleNearest[i] == res[i].Triangle, $"2^{e} kind {kind} point {i}: triangle {res[i].Triangle}, single {singleNearest[i]}");
                    // the distance is the square root of the tree's squared distance
                    Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Sqrt(singleDist[i])), BitConverter.DoubleToInt64Bits(res[i].Distance));
                }
                var inside = new InsideStatus[pts.Length];
                Assert.Equal(BatchStatus.Ok, MeshBatchQueries.IsInsideBatch(tree, pts, inside, InsideMethod.Crossings, opt));
                for (int i = 0; i < pts.Length; ++i)
                    Assert.Equal(tree.IsInside(pts[i]) ? InsideStatus.Inside : InsideStatus.Outside, inside[i]);
            }
        }

        // ---- winding number octree --------------------------------------------------------------------------------

        public static IEnumerable<object[]> OctreeCases()
        {
            foreach (int kind in new[] { 0, 1 })
                foreach (int e in new[] { -24, 0, 20 })
                    yield return new object[] { kind, e };
        }

        [Theory]
        [MemberData(nameof(OctreeCases))]
        public void Octree_NodeValues_ClassifyLikeTheBruteForceWindingNumber(int kind, int e)
        {
            double s = Math.Pow(2, e);
            DMesh3 m = BatchMesh(kind, s);
            var octree = new WindingNumberOctree(m, new DMeshAABBTree3(m, true)) { MaxDepth = 6 };
            octree.Build();
            int checkedNodes = 0, inside = 0;
            int n = octree.LeafCount;
            for (int k = 0; k <= n; k += 3)
                for (int j = 0; j <= n; j += 3)
                    for (int i = 0; i <= n; i += 3) {
                        double w = octree.NodeValue(new Vector3i(i, j, k));
                        double brute = m.WindingNumber(octree.Origin + octree.LeafCellSize * new Vector3d(i, j, k));
                        if (Math.Abs(brute - 0.5) < 0.3) continue;      // too close to the surface to classify
                        Assert.True((w > 0.5) == (brute > 0.5), $"2^{e} kind {kind} node ({i},{j},{k}): octree {w}, brute force {brute}");
                        ++checkedNodes;
                        if (brute > 0.5) ++inside;
                    }
            Assert.True(checkedNodes > 500 && inside > 10, $"vacuous: {checkedNodes} nodes, {inside} inside");
        }

        [Theory]
        [MemberData(nameof(OctreeCases))]
        public void Octree_PropagationAndRebuildOnTheSameInstance_DoNotChangeTheNodeValues(int kind, int e)
        {
            double s = Math.Pow(2, e);
            DMesh3 m = BatchMesh(kind, s);
            var tree = new DMeshAABBTree3(m, true);
            var with = new WindingNumberOctree(m, tree) { MaxDepth = 5, PropagateValues = true };
            var without = new WindingNumberOctree(m, tree) { MaxDepth = 5, PropagateValues = false };
            with.Build(); without.Build();
            Assert.Equal(with.SurfaceCells.Count, without.SurfaceCells.Count);
            int n = with.LeafCount;
            var first = new List<double>();
            for (int k = 0; k <= n; k += 2)
                for (int j = 0; j <= n; j += 2)
                    for (int i = 0; i <= n; i += 2) {
                        var node = new Vector3i(i, j, k);
                        double a = with.NodeValue(node), b = without.NodeValue(node);
                        first.Add(a);
                        // away from the surface the propagated value is the rounded integer, the other the fast approximation
                        if (Math.Abs(b - 0.5) > 0.3) Assert.True((a > 0.5) == (b > 0.5), $"node ({i},{j},{k}): {a} against {b}");
                    }
            // reuse: Build() again on the same instance gives the same surface cells and node values (by value: -0 and +0)
            List<Vector3i> cells = with.SurfaceCells.ToList();
            with.Build();
            Assert.True(cells.SequenceEqual(with.SurfaceCells), "the surface cells changed on the second Build()");
            int idx = 0;
            for (int k = 0; k <= n; k += 2)
                for (int j = 0; j <= n; j += 2)
                    for (int i = 0; i <= n; i += 2)
                        Assert.Equal(first[idx++], with.NodeValue(new Vector3i(i, j, k)));
        }
    }
}
