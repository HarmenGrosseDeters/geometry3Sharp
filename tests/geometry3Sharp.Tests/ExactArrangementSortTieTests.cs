using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Stage 2 obligation O-SORTS of docs/design/DETERMINISM_PROOF.md: the sorts of ExactMeshArrangement and
    /// ExactMeshBoolean that only normalize an order, and the order among equal keys in merge_points. Each is argued
    /// in the proof (a sort makes a schedule dependent list unique; no reader depends on the order of equal keys); these
    /// tests make a mutant fail that removes the sort or lets a reader depend on the order it leaves.
    /// The test of a normalizing sort feeds the unsorted list in a fixed hostile order (the loops of the arrangement run
    /// backwards and in seeded permutations, as in <see cref="OrderAdversaryTests"/>): a parallel run is the same by
    /// chance on some schedules, these orders are not.
    /// </summary>
    public class ExactArrangementSortTieTests
    {
        static readonly int[] Orders = { -1, 1, 2, 3 };

        static T Serial<T>(int order, Func<T> f)
        {
            bool serial = gParallel.SerialOnThisThread;
            gParallel.SerialOnThisThread = true;
            gParallel.SerialOrderOnThisThread = order;
            try {
                return f();
            } finally {
                gParallel.SerialOrderOnThisThread = 0;
                gParallel.SerialOnThisThread = serial;
            }
        }

        static DMesh3 Box(Vector3d center, double size, Quaterniond rotation)
        {
            DMesh3 mesh = new TrivialBox3Generator() { Box = new Box3d(center, new Vector3d(size, size, size)) }.Generate().MakeDMesh();
            MeshTransforms.Rotate(mesh, center, rotation);
            return mesh;
        }

        // everything the arrangement computed that a later stage can read, with the point ids (the ids are handed out in
        // the order the loops run, so they are the same only for the same loop order): per id its kind and canonical id,
        // the pieces with their ids, and the merges (the choice of a representative is part of the result)
        static string PrintIds(ExactMeshArrangement arr)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(arr.PointCount).Append('/').Append(arr.MergedPoints).Append('|');
            for (int id = 0; id < arr.PointCount; ++id)
                sb.Append(arr.KindOf(id)).Append(':').Append(arr.Canonical(id)).Append(',');
            sb.Append('|');
            foreach (var o in arr.Output)
                sb.Append(o.Mesh).Append(':').Append(o.A).Append(',').Append(o.B).Append(',').Append(o.C).Append(';');
            return sb.ToString();
        }

        // the same for every loop order: the pieces in their order, each corner as its exact point (ids by name are not
        // a result, DETERMINISM_PROOF.md P5), and the number of merges
        static string PrintPoints(ExactMeshArrangement arr)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(arr.MergedPoints).Append('|');
            foreach (var o in arr.Output) {
                sb.Append(o.Mesh).Append(':');
                foreach (int id in new[] { o.A, o.B, o.C }) {
                    ExactPoint3 x = arr.Exact(id);
                    sb.Append(x.X).Append(',').Append(x.Y).Append(',').Append(x.Z).Append(',').Append(x.W).Append('/');
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        // boxes with coplanar faces (an n x n grid) and a rotated box: many triangles with segments from many pairs (appended
        // by the pair loop under the lock of the triangle, so in the order of the schedule), and many coincident points
        // of different constructions
        static List<DMesh3> CoplanarGrid(int n = 3, int seed = 1)
        {
            var meshes = new List<DMesh3>();
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j)
                    meshes.Add(Box(new Vector3d(0.9 * i, 0.9 * j, 0), 0.5, Quaterniond.Identity));
            var rand = new Random(seed);
            var axis = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5).Normalized;
            meshes.Add(Box(new Vector3d(0.4, 0.45, 0.2), 0.8, new Quaterniond(axis, rand.NextDouble() * 360)));
            meshes.Add(Box(new Vector3d(0.45 * n, 0.4, 0.1), 0.6, Quaterniond.Identity));
            return meshes;
        }

        static string Run(List<DMesh3> meshes, int order, int tieSeed = 0, int maxHashed = 64, bool ids = false)
        {
            return Serial(order, () => {
                var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true, TieOrderSeed = tieSeed, MaxHashedCluster = maxHashed };
                arr.Compute();
                return ids ? PrintIds(arr) : PrintPoints(arr);
            });
        }

        // ---------------------------------------------------------------- merge_points: the order of equal lo

        public static IEnumerable<object[]> Grids()
        {
            foreach (int n in new[] { 2, 3 })
                foreach (int seed in new[] { 0, 1, 2 })
                    foreach (int maxHashed in new[] { 64, -1 })
                        yield return new object[] { n, seed, maxHashed };
        }

        [Theory]
        [MemberData(nameof(Grids))]
        public void MergePoints_OrderOfEqualLowerEnds_DoesNotChangeTheResult(int n, int gridSeed, int maxHashed)
        {
            // the coplanar grid has 56 runs of equal lo (up to 3 points); the hook reverses or permutes every run after the
            // sort. merge_cluster reads its cluster as a set: the two point case, the grouping by exact point and the
            // sorted path (MaxHashedCluster -1) give the same ids and the same representatives (the point with the
            // smallest key, not the first of the cluster). The loop order is fixed, so the ids can be compared
            List<DMesh3> meshes = CoplanarGrid(n, gridSeed);
            string reference = Run(meshes, 0, 0, maxHashed, ids: true);
            foreach (int seed in new[] { -1, 1, 2, 3, 4 })
                Assert.Equal(reference, Run(meshes, 0, seed, maxHashed, ids: true));
        }

        [Fact]
        public void MergePoints_TieOrderHook_ActuallyMovesEqualLowerEnds()
        {
            // the hook is no no-op: the runs of copies are longer than one, and they are reordered (the order of the sort
            // for equal lo is the order of the position numbers only for small arrays)
            var lo = new double[] { 1, 1, 1, 1, 2, 3, 3, 3, double.NegativeInfinity, double.NegativeInfinity };
            var order = Enumerable.Range(0, lo.Length).ToArray();
            Array.Sort(lo, order);
            int[] sorted = (int[])order.Clone();
            var rev = (int[])sorted.Clone();
            ExactMeshArrangement.permute_ties(lo, rev, -1);
            Assert.Equal(sorted.Take(2).Reverse(), rev.Take(2));
            Assert.Equal(sorted.Skip(2).Take(4).Reverse(), rev.Skip(2).Take(4));
            Assert.Equal(sorted[6], rev[6]);
            Assert.Equal(sorted.Skip(7).Take(3).Reverse(), rev.Skip(7).Take(3));
            var shuffled = (int[])sorted.Clone();
            ExactMeshArrangement.permute_ties(lo, shuffled, 7);
            Assert.NotEqual(sorted, shuffled);
            // a permutation within each run: every run keeps its elements
            for (int a = 0, b; a < lo.Length; a = b) {
                b = a + 1;
                while (b < lo.Length && lo[b] == lo[a]) b++;
                Assert.Equal(sorted.Skip(a).Take(b - a).OrderBy(x => x), shuffled.Skip(a).Take(b - a).OrderBy(x => x));
            }
        }

        // ---------------------------------------------------------------- merge_sorted

        [Fact]
        public void MergeSorted_SortsTheClusterByPoint_BeforeItReadsRunsOfEqualPoints()
        {
            // MaxHashedCluster below 0 sorts every cluster of more than two points: equal points that are not adjacent in
            // the order of lo are merged only after the sort by ComparePoints (without it, the runs of equal points that
            // it reads are not runs, and merges are missed). The same merges as the hashed path, with the same ids
            List<DMesh3> meshes = CoplanarGrid();
            string hashed = Run(meshes, 0, 0, 64, ids: true);
            Assert.Equal(hashed, Run(meshes, 0, 0, -1, ids: true));
            foreach (int seed in new[] { -1, 3 })
                Assert.Equal(hashed, Run(meshes, 0, seed, -1, ids: true));
            Assert.Equal(Run(meshes, 0, 0, 64), Run(meshes, 1, 0, -1));
        }

        // ---------------------------------------------------------------- Segments.Sort in prepare_touched

        [Fact]
        public void PrepareTouched_SortsSegments_WhateverTheOrderThePairsWereFound()
        {
            // the segments of a triangle are appended by the parallel pair loop (schedule order); the sort makes the list,
            // and so the first of equal edges that the dedupe keeps and the order of the constraints, unique
            List<DMesh3> meshes = CoplanarGrid();
            string reference = Run(meshes, 0);
            foreach (int order in Orders)
                Assert.Equal(reference, Run(meshes, order));
        }

        // ---------------------------------------------------------------- fan.Sort in split_bowties

        // vertex 0 with two fans of three triangles each: the first (triangles 0-2) keeps the vertex, the second is moved to
        // a copy by SetTriangle, one triangle at a time. The second fan's triangles take the ids that earlier removals freed
        // (the most recently freed first), so the order in which the vertex lists them (VtxTrianglesItr) is descending
        static DMesh3 BowtieWithDescendingFan(out List<int> fanOrder)
        {
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0));
            for (int i = 1; i <= 8; ++i)
                m.AppendVertex(new Vector3d(Math.Cos(i), Math.Sin(i), 1 + 0.25 * i));
            m.AppendTriangle(0, 1, 2); m.AppendTriangle(0, 2, 3); m.AppendTriangle(0, 3, 4);
            // three triangles away from vertex 0, removed again: their ids (3, 4, 5) are free for the second fan
            for (int k = 0; k < 3; ++k) {
                int a = m.AppendVertex(new Vector3d(10 + k, 0, 0)), b = m.AppendVertex(new Vector3d(10 + k, 1, 0)), c = m.AppendVertex(new Vector3d(10 + k, 0, 1));
                m.AppendTriangle(a, b, c);
            }
            for (int tid = 3; tid <= 5; ++tid)
                m.RemoveTriangle(tid, true);
            m.AppendTriangle(0, 5, 6); m.AppendTriangle(0, 6, 7); m.AppendTriangle(0, 7, 8);
            fanOrder = m.VtxTrianglesItr(0).Where(tid => tid >= 3).ToList();
            return m;
        }

        // the mesh as its edges (in edge id order, with their triangles) and its triangles: the bookkeeping of SetTriangle
        // (new edges take ids in the order of the calls) is part of it
        static string Structure(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int e in m.EdgeIndices()) {
                Index4i ev = m.GetEdge(e);
                sb.Append(e).Append('=').Append(ev.a).Append('-').Append(ev.b).Append(':').Append(ev.c).Append(',').Append(ev.d).Append(' ');
            }
            sb.Append('|');
            foreach (int t in m.TriangleIndices())
                sb.Append(t).Append('=').Append(m.GetTriangle(t).a).Append(',').Append(m.GetTriangle(t).b).Append(',').Append(m.GetTriangle(t).c).Append(' ');
            return sb.ToString();
        }

        [Fact]
        public void SplitBowties_FanListedDescending_IsRedirectedInTriangleIdOrder()
        {
            List<int> fanOrder;
            DMesh3 m = BowtieWithDescendingFan(out fanOrder);
            // the fan reaches split_bowties in descending order (without the sort of fan.Sort it is redirected so)
            Assert.Equal(new[] { 5, 4, 3 }, fanOrder);
            Assert.True(m.IsBowtieVertex(0));
            var vertexPoint = Enumerable.Range(0, m.MaxVertexID).ToList();
            Assert.Equal(1, ExactMeshBoolean.split_bowties(m, vertexPoint));
            Assert.Empty(MeshIterators.BowtieVertices(m));
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            // the new vertex 17 takes the triangles of the fan in the order 3, 4, 5, so SetTriangle hands out the edge ids 8 to 14 in
            // that order (listed as they come, the order 5, 4, 3 gives other ids and other adjacency), pinned by hand
            Assert.Equal("0=0-1:0,-1 1=1-2:0,-1 2=0-2:0,1 3=2-3:1,-1 4=0-3:1,2 5=3-4:2,-1 6=0-4:2,-1 "
                + "8=8-17:3,-1 9=7-17:3,4 10=7-8:3,-1 11=6-17:4,5 12=6-7:4,-1 13=5-17:5,-1 14=5-6:5,-1 "
                + "|0=0,1,2 1=0,2,3 2=0,3,4 3=17,7,8 4=17,6,7 5=17,5,6 ", Structure(m));
        }
    }
}
