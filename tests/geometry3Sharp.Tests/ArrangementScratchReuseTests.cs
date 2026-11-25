using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The allocation cuts in ExactMeshArrangement (lever 4 of the boolean study): TriangleBoxTree.Query with a callback
    /// reuses its result list per thread and nesting depth, BoxGrid2 keeps its boxes as structs, merge_cluster numbers
    /// its groups, ExactTriangulation2 is sized from its point count. Same results, in the same order, as before.
    /// </summary>
    public class ArrangementScratchReuseTests
    {
        static List<AxisAlignedBox3d> RandomBoxes(Random rand, int n, double size)
        {
            var boxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < n; ++i) {
                var c = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                var e = size * new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                boxes.Add(new AxisAlignedBox3d(c - e, c + e));
            }
            return boxes;
        }

        static List<int> ListQuery(TriangleBoxTree tree, AxisAlignedBox3d q)
        {
            var r = new List<int>();
            tree.Query(q, r);
            return r;
        }

        [Fact]
        public void QueryCallback_NestedInTheCallback_SameAsTheListQuery()
        {
            // f queries the same tree and another one again (deeper than the pool of lists): every level reports the
            // boxes of its own query, in the order of the list version
            var rand = new Random(3);
            var boxesA = RandomBoxes(rand, 500, 0.08);
            var boxesB = RandomBoxes(rand, 300, 0.1);
            var a = new TriangleBoxTree(boxesA);
            var b = new TriangleBoxTree(boxesB);
            int levels = 12;
            Action<int, AxisAlignedBox3d> nested = null;
            int checkedQueries = 0;
            nested = (level, q) => {
                TriangleBoxTree tree = (level % 2 == 0) ? a : b;
                List<AxisAlignedBox3d> boxes = (level % 2 == 0) ? boxesA : boxesB;
                var called = new List<int>();
                tree.Query(q, (k) => {
                    called.Add(k);
                    // (one nested query per level, from inside the callback)
                    if (called.Count == 1 && level + 1 < levels)
                        nested(level + 1, boxes[k]);
                });
                Assert.Equal(ListQuery(tree, q), called);
                checkedQueries++;
            };
            for (int i = 0; i < 20; ++i)
                nested(0, boxesA[rand.Next(boxesA.Count)]);
            Assert.True(checkedQueries > 20 * 4, checkedQueries.ToString());
        }

        [Fact]
        public void QueryCallback_AfterAnExceptionInTheCallback_AndWithLargeResults()
        {
            var rand = new Random(4);
            var boxes = RandomBoxes(rand, 70000, 0.5);       // (a query over all: a result above the kept list size)
            var tree = new TriangleBoxTree(boxes);
            var all = new AxisAlignedBox3d(new Vector3d(-10, -10, -10), new Vector3d(10, 10, 10));
            for (int round = 0; round < 3; ++round) {
                Assert.Throws<InvalidOperationException>(() => tree.Query(all, (k) => {
                    if (k == boxes.Count / 2) throw new InvalidOperationException("stop");
                }));
                var called = new List<int>();
                tree.Query(all, called.Add);
                Assert.Equal(ListQuery(tree, all), called);
                Assert.Equal(boxes.Count, called.Count);
                var small = boxes[rand.Next(boxes.Count)];
                var c2 = new List<int>();
                tree.Query(small, c2.Add);
                Assert.Equal(ListQuery(tree, small), c2);
            }
        }

        [Fact]
        public void QueryCallback_ConcurrentThreads_SameAsTheListQuery()
        {
            var rand = new Random(5);
            var boxes = RandomBoxes(rand, 2000, 0.05);
            var tree = new TriangleBoxTree(boxes);
            var queries = RandomBoxes(rand, 400, 0.1);
            var expected = queries.Select(q => ListQuery(tree, q)).ToArray();
            var got = new List<int>[queries.Count];
            System.Threading.Tasks.Parallel.For(0, queries.Count, (i) => {
                var r = new List<int>();
                tree.Query(queries[i], (k) => r.Add(k));
                got[i] = r;
            });
            for (int i = 0; i < queries.Count; ++i)
                Assert.Equal(expected[i], got[i]);
        }

        // boxes on a grid with coincident corners and faces, and a rotated one through them: many coincident
        // constructions (merge_cluster's hashed groups) and segments crossing in the triangles (BoxGrid2)
        static List<DMesh3> CoincidentBoxes(int seed)
        {
            var meshes = new List<DMesh3>();
            Func<Vector3d, double, Quaterniond, DMesh3> box = (c, s, r) => {
                DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, s * 0.5 * Vector3d.One) }.Generate().MakeDMesh();
                MeshTransforms.Rotate(m, Vector3d.Zero, r);
                MeshTransforms.Translate(m, c);
                return m;
            };
            for (int i = 0; i < 2; ++i)
                for (int j = 0; j < 2; ++j)
                    meshes.Add(box(new Vector3d(0.9 * i, 0.9 * j, 0.45 * (i + j)), 1, Quaterniond.Identity));
            var rand = new Random(seed);
            var axis = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble() + 0.1).Normalized;
            meshes.Add(box(new Vector3d(0.4, 0.5, 0.3), 1.1, Quaterniond.AxisAngleD(axis, 37 + rand.Next(300))));
            return meshes;
        }

        static string Fingerprint(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                sb.Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append(';');
            }
            return sb.ToString();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        public void UnionOfCoincidentBoxes_Invariant_Reusable_Deterministic(int seed)
        {
            List<DMesh3> meshes = CoincidentBoxes(seed);
            // the arrangement merges coincident constructions (hashed clusters) and crosses segments
            var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true };
            arr.Compute();
            Assert.True(arr.MergedPoints > 3, arr.MergedPoints.ToString());
            Assert.True(arr.SegmentCrossings > 0, arr.SegmentCrossings.ToString());

            var reference = new ExactMeshBoolean(meshes, Ops.Union);
            Assert.True(reference.Compute(), reference.Error);
            ResultChecks.AssertClosed(reference.Result);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(reference.Result));
            string print = Fingerprint(reference.Result);
            // reuse: Compute twice on one instance
            Assert.True(reference.Compute(), reference.Error);
            Assert.Equal(print, Fingerprint(reference.Result));
            // determinism: serial equals parallel, repeated runs agree
            gParallel.SerialOnThisThread = true;
            try {
                var serial = new ExactMeshBoolean(meshes, Ops.Union);
                Assert.True(serial.Compute(), serial.Error);
                Assert.Equal(print, Fingerprint(serial.Result));
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            for (int run = 0; run < 3; ++run) {
                var again = new ExactMeshBoolean(meshes, Ops.Union);
                Assert.True(again.Compute(), again.Error);
                Assert.Equal(print, Fingerprint(again.Result));
            }
            // invariance: every similarity gives a clean result; the 2^k scales the same one, scaled, bit for bit
            foreach (Similarity sim in Similarity.All()) {
                var b = new ExactMeshBoolean(meshes.Select(m => Invariance.Transformed(m, sim)), Ops.Union);
                Assert.True(b.Compute(), $"{sim}: {b.Error}");
                Assert.True(MeshRobustRepair.NotCleanReason(b.Result) == "", sim.ToString());
                if (sim.IsExact)
                    Assert.True(Invariance.Difference(reference.Result, Invariance.MappedBack(b.Result, sim)) == "", sim.ToString());
            }
        }

        // counts, then each output triangle by its source and the exact coordinates of its corners (X, Y, Z, W of the
        // exact point: point ids are assigned in parallel, so they are not compared)
        static string ArrangementPrint(ExactMeshArrangement arr)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(arr.Triangles.Count).Append('/').Append(arr.Output.Count).Append('/').Append(arr.MergedPoints)
              .Append('/').Append(arr.ConstraintEdges.Count).Append('/').Append(arr.IntersectingPairs).Append('/').Append(arr.SegmentCrossings)
              .Append('/').Append(arr.PreDecidedPairs).Append('|');
            foreach (var o in arr.Output) {
                sb.Append(o.Mesh).Append(':').Append(o.Source);
                foreach (int c in new[] { o.A, o.B, o.C }) {
                    ExactPoint3 x = arr.Exact(arr.Canonical(c));
                    sb.Append(':').Append(x.X).Append(',').Append(x.Y).Append(',').Append(x.Z).Append(',').Append(x.W);
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        static string Computed(List<DMesh3> meshes)
        {
            var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true };
            arr.Compute();
            return ArrangementPrint(arr);
        }

        // issue 1512: a second Compute() on one instance threw "point coincides with a vertex" when the first run merged points
        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        public void Arrangement_SecondComputeWithMerges_EqualsFirst(int seed)
        {
            List<DMesh3> meshes = CoincidentBoxes(seed);
            var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true };
            arr.Compute();
            Assert.True(arr.MergedPoints > 3, arr.MergedPoints.ToString());
            string first = ArrangementPrint(arr);
            arr.Compute();
            Assert.Equal(first, ArrangementPrint(arr));
            arr.Compute();
            Assert.Equal(first, ArrangementPrint(arr));
        }

        // face-adjacent boxes: coplanar triangle pairs that meet only in a corner or an edge are decided before clipping
        // (PreDecidedPairs), which a second run counts again from zero
        [Fact]
        public void Arrangement_SecondComputeWithPreDecidedPairs_EqualsFirst()
        {
            var meshes = new List<DMesh3>();
            for (int i = 0; i < 3; ++i) {
                DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, 0.5 * Vector3d.One) }.Generate().MakeDMesh();
                MeshTransforms.Translate(m, new Vector3d(i, 0, 0));
                meshes.Add(m);
            }
            var arr = new ExactMeshArrangement(meshes) { SelfIntersections = true };
            arr.Compute();
            Assert.True(arr.PreDecidedPairs > 0, arr.PreDecidedPairs.ToString());
            string first = ArrangementPrint(arr);
            arr.Compute();
            Assert.Equal(first, ArrangementPrint(arr));
        }

        // the 2^k scales give the same arrangement, a reused instance included (grid coordinates are those of the input
        // divided by 2^ScaleExp); the others a reused one equal to their first run
        [Fact]
        public void Arrangement_Reuse_InvariantOverSimilarities()
        {
            List<DMesh3> meshes = CoincidentBoxes(0);
            string reference = Computed(meshes);
            foreach (Similarity sim in Similarity.All()) {
                var b = new ExactMeshArrangement(meshes.Select(m => Invariance.Transformed(m, sim)).ToList()) { SelfIntersections = true };
                b.Compute();
                string once = ArrangementPrint(b);
                b.Compute();
                Assert.Equal(once, ArrangementPrint(b));
                if (sim.IsExact)
                    Assert.Equal(reference, once);
            }
        }

        // Meshes changed between two runs: the second equals a new instance on the new meshes (also fewer meshes)
        [Fact]
        public void Arrangement_MeshesChangedBetweenRuns_EqualsFreshInstance()
        {
            List<DMesh3> a = CoincidentBoxes(0), b = CoincidentBoxes(5), c = a.Take(3).ToList();
            var arr = new ExactMeshArrangement(a) { SelfIntersections = true };
            arr.Compute();
            foreach (List<DMesh3> next in new[] { b, c, a }) {
                arr.Meshes = next;
                arr.Compute();
                Assert.Equal(Computed(next), ArrangementPrint(arr));
            }
        }

        // points added after a Compute (the classification's centroids) in a reused instance: valid ids of the second run,
        // whatever the first run left in the point indexes (integer, rational and beyond fixed width)
        [Fact]
        public void Arrangement_AddPointAfterReuse_IdsOfTheSecondRun()
        {
            List<DMesh3> a = CoincidentBoxes(0), b = CoincidentBoxes(5);
            Func<int, ExactPoint3>[] points = {
                (i) => ExactPoint3.Create(new System.Numerics.BigInteger(3 + i), 5, 7, 1),
                (i) => ExactPoint3.Create(new System.Numerics.BigInteger(3 + i), 5, 7, 11),
                (i) => ExactPoint3.Create(System.Numerics.BigInteger.Pow(2, 3000) + i, 5, 7, 13),
            };
            var arr = new ExactMeshArrangement(a) { SelfIntersections = true };
            arr.Compute();
            for (int i = 0; i < 40; ++i)
                foreach (var f in points)
                    arr.AddPoint(f(i));
            arr.Meshes = b;
            arr.Compute();
            var fresh = new ExactMeshArrangement(b) { SelfIntersections = true };
            fresh.Compute();
            Assert.Equal(fresh.PointCount, arr.PointCount);
            for (int i = 0; i < 40; ++i)
                foreach (var f in points) {
                    int id = arr.AddPoint(f(i));
                    Assert.True(id >= 0 && id < arr.PointCount);
                    Assert.True(id >= fresh.PointCount, "a new point, not an id of the first run");
                    Assert.Equal(f(i), arr.Exact(id));
                    Assert.Equal(id, arr.AddPoint(f(i)));
                }
            Assert.Equal(ArrangementPrint(fresh), ArrangementPrint(arr));
        }
    }
}
