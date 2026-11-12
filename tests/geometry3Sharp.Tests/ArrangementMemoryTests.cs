using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Exact Boolean 3.0 T2 (memory of the arrangement): what the arrangement keeps alive after a phase is over. The
    /// results do not change; these tests pin that the memory is released and that the structures still answer.
    /// </summary>
    public class ArrangementMemoryTests
    {
        static List<AxisAlignedBox3d> Boxes(int count, int seed)
        {
            var rand = new Random(seed);
            var boxes = new List<AxisAlignedBox3d>(count);
            for (int i = 0; i < count; ++i) {
                var c = new Vector3d(rand.NextDouble() * 100, rand.NextDouble() * 100, rand.NextDouble() * 100);
                var b = new AxisAlignedBox3d(c, c);
                b.Contain(c + new Vector3d(rand.NextDouble() * 3, rand.NextDouble() * 3, rand.NextDouble() * 3));
                boxes.Add(b);
            }
            return boxes;
        }

        // (the tree's overlap is closed: touching boxes overlap)
        static bool Overlap(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return a.Min.x <= b.Max.x && b.Min.x <= a.Max.x && a.Min.y <= b.Max.y && b.Min.y <= a.Max.y && a.Min.z <= b.Max.z && b.Min.z <= a.Max.z;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static TriangleBoxTree Build(List<AxisAlignedBox3d> boxes, out WeakReference listRef)
        {
            listRef = new WeakReference(boxes);
            return new TriangleBoxTree(boxes);
        }

        // (in a method of its own: the list must be unreachable from this frame, and the lambda's closure would hold it)
        [MethodImpl(MethodImplOptions.NoInlining)]
        static TriangleBoxTree BuildWithExpected(int count, out WeakReference listRef, out List<AxisAlignedBox3d> queries, out List<int>[] expected)
        {
            List<AxisAlignedBox3d> boxes = Boxes(count, 7);
            queries = Boxes(40, 8);
            var exp = new List<int>[queries.Count];
            for (int k = 0; k < exp.Length; ++k) {
                exp[k] = new List<int>();
                for (int i = 0; i < boxes.Count; ++i)
                    if (Overlap(boxes[i], queries[k])) exp[k].Add(i);
            }
            expected = exp;
            return Build(boxes, out listRef);
        }

        // below MedianSplitBelow (4096 boxes) the tree takes the median split (sort keys), above it the Morton order
        [Theory]
        [InlineData(300)]
        [InlineData(6000)]
        public void BoxTree_ReleasesItsInputListAfterTheBuild_AndStillAnswers(int count)
        {
            WeakReference listRef;
            List<AxisAlignedBox3d> queries;
            List<int>[] expected;
            TriangleBoxTree tree = BuildWithExpected(count, out listRef, out queries, out expected);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.False(listRef.IsAlive, "the tree keeps the list of boxes it was built from");
            for (int k = 0; k < queries.Count; ++k) {
                var found = new List<int>();
                tree.Query(queries[k], (i) => found.Add(i));
                found.Sort();
                Assert.Equal(expected[k], found);
            }
            GC.KeepAlive(tree);
        }

        static DMesh3 Sphere(Vector3d shift, int edgeVertices = 12)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices())
                m.SetVertex(v, m.GetVertex(v) + shift);
            return m;
        }

        [Fact]
        public void Boolean_ReleasesTheBoxTreesAfterTheClassification_AndComputesTwiceTheSame()
        {
            DMesh3 a = Sphere(Vector3d.Zero), b = Sphere(new Vector3d(0.6, 0.2, 0.1));
            var boolean = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.True(boolean.Compute());
            Assert.Null(boolean.Arrangement.Trees);
            DMesh3 first = boolean.Result;
            Assert.True(boolean.Compute());
            Assert.Null(boolean.Arrangement.Trees);
            Assert.Equal("", Invariance.GeometricDifference(first, boolean.Result) ?? "");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(boolean.Result) ?? "");
        }

        static ExactMeshArrangement Arranged(Vector3d shift, int edgeVertices = 12)
        {
            var arr = new ExactMeshArrangement(new List<DMesh3> { Sphere(Vector3d.Zero, edgeVertices), Sphere(shift, edgeVertices) }) { SelfIntersections = true };
            arr.Compute();
            return arr;
        }

        // InputTriangle is a handle on arrays of the arrangement (T2 B): a copy is the same triangle, untouched triangles
        // share the empty lists, the data are those of the mesh triangles
        [Fact]
        public void InputTriangle_IsAHandle_WithTheDataOfItsMeshTriangle()
        {
            Assert.True(typeof(ExactMeshArrangement.InputTriangle).IsValueType);
            ExactMeshArrangement arr = Arranged(new Vector3d(0.6, 0.2, 0.1));
            int touched = 0, untouched = 0;
            ExactMeshArrangement.InputTriangle firstEmpty = default(ExactMeshArrangement.InputTriangle);
            for (int ti = 0; ti < arr.Triangles.Count; ++ti) {
                ExactMeshArrangement.InputTriangle t = arr.Triangles[ti], copy = t;
                Assert.Equal(ti, t.Index);
                Assert.Same(arr, copy.arr);
                DMesh3 mesh = arr.Meshes[t.Mesh];
                Assert.True(mesh.IsTriangle(t.Tid));
                Index3i m = mesh.GetTriangle(t.Tid);
                Assert.Equal(arr.VertexPoints[t.Mesh][m.a], copy.Corners.a);
                Assert.Equal(mesh.GetVertex(m.b), copy.V1);
                Assert.Contains(ti, arr.MeshTriangles[t.Mesh]);
                if (t.Segments.Count > 0 || t.Points.Count > 0) {
                    touched++;
                    // (a segment added through the copy is seen through the original)
                    int before = t.Segments.Count;
                    copy.AddSegment(new ExactMeshArrangement.Segment(0, 1, 0));
                    Assert.Equal(before + 1, t.Segments.Count);
                } else {
                    untouched++;
                    if (untouched == 1) firstEmpty = t;
                    else Assert.Same(firstEmpty.Segments, t.Segments);        // (the shared empty list)
                }
            }
            Assert.True(touched > 0 && untouched > touched);
        }

        // threads adding to the same triangle (the lock is a stripe of the arrangement, as a handle cannot be locked)
        [Fact]
        public void InputTriangle_ParallelAdds_AreNotLost()
        {
            ExactMeshArrangement arr = Arranged(new Vector3d(5, 5, 5));         // (far apart: nothing is touched)
            ExactMeshArrangement.InputTriangle a = arr.Triangles[0], b = arr.Triangles[1];
            Assert.Empty(a.Coplanar);
            const int threads = 8, per = 2000;
            var tasks = new System.Threading.Tasks.Task[threads];
            for (int k = 0; k < threads; ++k) {
                int kk = k;
                tasks[k] = System.Threading.Tasks.Task.Run(() => {
                    for (int i = 0; i < per; ++i) {
                        var t = (i % 2 == 0) ? a : b;
                        lock (t.Lock) t.AddCoplanar(kk * per + i);
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);
            Assert.Equal(threads * per / 2, a.Coplanar.Count);
            Assert.Equal(threads * per / 2, b.Coplanar.Count);
        }

        static DMesh3 Box(Vector3d min, Vector3d max, bool negativeZeros)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d((min + max) * 0.5, (max - min) * 0.5) }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                // (box corners are computed from centre and extent: snap to the requested ones, with -0.0 for zeros)
                for (int k = 0; k < 3; ++k) {
                    double c = p[k] < (min[k] + max[k]) / 2 ? min[k] : max[k];
                    p[k] = (c == 0 && negativeZeros) ? -0.0 : c;
                }
                m.SetVertex(v, p);
            }
            return m;
        }

        // T2 C: the corners of an input triangle are read from the input points (-0.0 is +0.0 there) instead of the
        // mesh coordinates: -0.0 in the input changes no result (overlapping, face-touching and edge-touching boxes at zero)
        [Theory]
        [InlineData(0.5, 0.0, 0.0)]       // overlapping
        [InlineData(1.0, 0.0, 0.0)]       // sharing a face (coplanar, touching)
        [InlineData(1.0, 1.0, 0.0)]       // sharing an edge
        [InlineData(0.0, 0.0, 0.0)]       // equal (everything coplanar)
        public void NegativeZeroCoordinates_GiveTheSameResult(double dx, double dy, double dz)
        {
            foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference }) {
                DMesh3[] results = new DMesh3[2];
                bool[] ok = new bool[2];
                for (int variant = 0; variant < 2; ++variant) {
                    bool neg = variant == 1;
                    DMesh3 a = Box(new Vector3d(-1, -1, -1) + Vector3d.One, new Vector3d(1, 1, 1) + Vector3d.One, neg);   // [0,2]^3
                    DMesh3 b = Box(new Vector3d(dx, dy, dz), new Vector3d(dx, dy, dz) + new Vector3d(2, 2, 2), neg);
                    var boolean = new ExactMeshBoolean(a, b, op);
                    ok[variant] = boolean.Compute();
                    results[variant] = boolean.Result;
                }
                Assert.True(ok[0] && ok[1]);
                {
                    Assert.Equal("", Invariance.GeometricDifference(results[0], results[1]) ?? "");
                    Assert.Equal(Invariance.Volume(results[0]), Invariance.Volume(results[1]));
                }
            }
        }

        // T2 C, the consumers of the corners directly: the exact predicates and the triangle intersection test, and the box,
        // give the same answers when the zeros of the coordinates have either sign (small integer grids: many touching and
        // coplanar configurations, and many zeros)
        [Fact]
        public void CornerConsumers_AreBlindToTheSignOfAZero()
        {
            var rand = new Random(11);
            Func<Vector3d> corner = () => new Vector3d(rand.Next(-1, 2), rand.Next(-1, 2), rand.Next(-1, 2));
            Func<Vector3d, Vector3d> flipped = (v) => new Vector3d(v.x == 0 ? -0.0 : v.x, v.y == 0 ? -0.0 : v.y, v.z == 0 ? -0.0 : v.z);
            int zeros = 0, touching = 0;
            for (int n = 0; n < 4000; ++n) {
                Vector3d[] p = { corner(), corner(), corner(), corner(), corner(), corner() };
                Vector3d[] q = p.Select(flipped).ToArray();
                Assert.Equal(ExactPredicates.Orient3d(p[0], p[1], p[2], p[3]), ExactPredicates.Orient3d(q[0], q[1], q[2], q[3]));
                var ia = new Index3i(0, 1, 2); var ib = new Index3i(3, 4, 5);
                var ta = new Triangle3d(p[0], p[1], p[2]); var tb = new Triangle3d(p[3], p[4], p[5]);
                var qa = new Triangle3d(q[0], q[1], q[2]); var qb = new Triangle3d(q[3], q[4], q[5]);
                bool r = ExactTriangleIntersection.IntersectsBeyondShared(ia, ref ta, ib, ref tb);
                Assert.Equal(r, ExactTriangleIntersection.IntersectsBeyondShared(ia, ref qa, ib, ref qb));
                var ja = new Index3i(0, 1, 2); var jb = new Index3i(0, 4, 2);
                Assert.Equal(ExactTriangleIntersection.IntersectsBeyondShared(ja, ref ta, jb, ref tb),
                    ExactTriangleIntersection.IntersectsBeyondShared(ja, ref qa, jb, ref qb));
                var bp = new AxisAlignedBox3d(p[0]); bp.Contain(p[1]); bp.Contain(p[2]);
                var bq = new AxisAlignedBox3d(q[0]); bq.Contain(q[1]); bq.Contain(q[2]);
                Assert.True(bp.Min == bq.Min && bp.Max == bq.Max);
                if (p.Any(v => v.x == 0 || v.y == 0 || v.z == 0)) zeros++;
                if (r) touching++;
            }
            Assert.True(zeros > 3000 && touching > 100, $"zeros {zeros}, intersecting {touching}");
        }

        // two triangles that share a lock stripe (Index and Index + stripes) and one that does not: adds from many threads to
        // the triangles are not lost, and the stripe of a handle is the one of its index
        [Fact]
        public void InputTriangle_LockStripes_FollowTheIndex_AndSharedStripesStayCorrect()
        {
            ExactMeshArrangement arr = Arranged(new Vector3d(5, 5, 5), 24);        // (about 12,700 triangles: more than the 4096 stripes)
            int count = arr.Triangles.Count;
            // (the stripes are a power of two, at most 4096, at least the triangle count up to that)
            ExactMeshArrangement.InputTriangle t0 = arr.Triangles[0], t1 = arr.Triangles[1];
            Assert.NotSame(t0.Lock, t1.Lock);
            int stripes = 1;
            while (stripes < 4096 && stripes < count) stripes <<= 1;
            Assert.True(count > stripes, "needs more triangles than stripes to share one");
            ExactMeshArrangement.InputTriangle far = arr.Triangles[stripes];
            Assert.Same(t0.Lock, far.Lock);                 // index and index + stripes: one lock
            Assert.NotSame(t1.Lock, far.Lock);
            const int threads = 8, per = 2000;
            var tasks = new System.Threading.Tasks.Task[threads];
            for (int k = 0; k < threads; ++k) {
                int kk = k;
                tasks[k] = System.Threading.Tasks.Task.Run(() => {
                    for (int i = 0; i < per; ++i) {
                        var t = (i % 2 == 0) ? t0 : far;
                        lock (t.Lock) t.AddCoplanar(kk * per + i);
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);
            Assert.Equal(threads * per / 2, t0.Coplanar.Count);
            Assert.Equal(threads * per / 2, far.Coplanar.Count);
        }
    }
}
