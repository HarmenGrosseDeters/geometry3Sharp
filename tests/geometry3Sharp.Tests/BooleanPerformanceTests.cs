using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The helpers that the performance work on the exact booleans changed or added (benchmarks/BooleanComparison,
    /// profile): they must give the same answers as the code they replace.
    /// </summary>
    public class BooleanPerformanceTests
    {
        static List<AxisAlignedBox3d> RandomBoxes(Random rand, int n, double size)
        {
            var boxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < n; ++i) {
                var c = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * 10;
                var e = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * size;
                // (some boxes touch exactly: corners on a coarse grid)
                if (i % 5 == 0) {
                    c = new Vector3d(Math.Round(c.x), Math.Round(c.y), Math.Round(c.z));
                    e = new Vector3d(0.5, 0.5, 0.5);
                }
                boxes.Add(new AxisAlignedBox3d(c - e, c + e));
            }
            return boxes;
        }

        static bool Overlap(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return !(a.Max.x < b.Min.x || a.Min.x > b.Max.x || a.Max.y < b.Min.y || a.Min.y > b.Max.y || a.Max.z < b.Min.z || a.Min.z > b.Max.z);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(9)]
        [InlineData(1000)]
        public void TriangleBoxTree_Query_FindsExactlyTheOverlappingBoxes(int n)
        {
            var rand = new Random(n);
            var boxes = RandomBoxes(rand, n, 0.3);
            var tree = new TriangleBoxTree(boxes);
            var result = new List<int>();
            for (int q = 0; q < 200; ++q) {
                AxisAlignedBox3d query = (q % 4 == 0 && n > 0) ? boxes[rand.Next(n)] : RandomBoxes(rand, 1, 1.5)[0];
                result.Clear();
                tree.Query(query, result);
                var expected = Enumerable.Range(0, n).Where(i => Overlap(boxes[i], query)).ToList();
                Assert.Equal(expected, result.OrderBy(i => i).ToList());
                Assert.Equal(result.Count, result.Distinct().Count());
                // the callback version reports the same boxes in the same order
                var called = new List<int>();
                tree.Query(query, (i) => called.Add(i));
                Assert.Equal(result, called);
            }
            // empty boxes (a mesh without triangles) overlap nothing
            var withEmpty = new List<AxisAlignedBox3d>(boxes) { AxisAlignedBox3d.Empty };
            result.Clear();
            new TriangleBoxTree(withEmpty).Query(new AxisAlignedBox3d(new Vector3d(-100, -100, -100), new Vector3d(100, 100, 100)), result);
            Assert.Equal(Enumerable.Range(0, n).ToList(), result.OrderBy(i => i).ToList());
        }

        [Fact]
        public void TriangleBoxTree_QuerySegment_FindsAllHitBoxes()
        {
            var rand = new Random(5);
            var boxes = RandomBoxes(rand, 500, 0.3);
            var tree = new TriangleBoxTree(boxes);
            for (int q = 0; q < 200; ++q) {
                Vector3d p = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * 10;
                Vector3d d = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5) * 20;
                if (q % 3 == 0) d.y = 0;     // axis-parallel segments
                var hits = new List<int>();
                tree.QuerySegment(p, p + d, 1e-9, (i) => hits.Add(i));
                Assert.Equal(hits.Count, hits.Distinct().Count());
                // every box the segment passes through (sampled densely) is reported
                for (int i = 0; i < boxes.Count; ++i) {
                    bool hit = false;
                    for (int s = 0; s <= 2000 && hit == false; ++s)
                        hit = boxes[i].Contains(p + (s / 2000.0) * d);
                    if (hit)
                        Assert.Contains(i, hits);
                }
            }
        }

        [Fact]
        public void SmallListSet_FindByKey_MatchesTheIterator()
        {
            // lists of up to 30 values (spilled to the linked list beyond 8), keys with repeats: the first match in
            // the order of ValueItr
            var rand = new Random(3);
            var set = new SmallListSet();
            set.Resize(50);
            var values = new DVector<int>();
            for (int v = 0; v < 400; ++v)
                for (int k = 0; k < 4; ++k)
                    values.Add(k == 1 ? rand.Next(20) : -7);
            for (int l = 0; l < 50; ++l) {
                set.AllocateAt(l);
                int count = rand.Next(31);
                for (int k = 0; k < count; ++k)
                    set.Insert(l, rand.Next(400));
            }
            for (int l = 0; l < 50; ++l)
                for (int key = -1; key < 21; ++key) {
                    int expected = -1;
                    foreach (int v in set.ValueItr(l))
                        if (values[4 * v + 1] == key) { expected = v; break; }
                    Assert.Equal(expected, set.FindByKey(l, values, key, -1));
                }
        }

        [Fact]
        public void DMesh3_FindEdge_HighValence()
        {
            // a fan of 40 triangles around one vertex (more than the 8 edges of a list block)
            var mesh = new DMesh3();
            int c = mesh.AppendVertex(Vector3d.Zero);
            var ring = new List<int>();
            for (int k = 0; k < 40; ++k)
                ring.Add(mesh.AppendVertex(new Vector3d(Math.Cos(k * Math.PI / 20), Math.Sin(k * Math.PI / 20), 0)));
            for (int k = 0; k < 40; ++k)
                mesh.AppendTriangle(c, ring[k], ring[(k + 1) % 40]);
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(eid);
                Assert.Equal(eid, mesh.FindEdge(ev.a, ev.b));
                Assert.Equal(eid, mesh.FindEdge(ev.b, ev.a));
            }
            Assert.Equal(DMesh3.InvalidID, mesh.FindEdge(ring[0], ring[2]));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(1000, 1)]
        [InlineData(1000, 2)]
        [InlineData(100000, 3)]
        public void SortByKey_SortsStably(int n, int kind)
        {
            // edge keys (a << 32 | b): small and large ids, many equal keys; items in increasing order, so a stable
            // sort keeps equal keys' items increasing (with one, two, three and four digit passes)
            var rand = new Random(n + kind);
            var keys = new long[n];
            for (int i = 0; i < n; ++i) {
                long a = rand.Next(kind == 1 ? 50 : kind == 2 ? 70000 : int.MaxValue), b = rand.Next(kind == 1 ? 200 : 1 << 20);
                keys[i] = (kind == 3 && i % 3 == 0) ? keys[rand.Next(Math.Max(1, i))] : ((a << 32) | b);
            }
            var items = Enumerable.Range(0, n).ToArray();
            var expected = keys.Select((k, i) => (k, i)).OrderBy(p => p.k).ThenBy(p => p.i).ToArray();
            ExactMeshBoolean.sort_by_key(keys, items);
            Assert.Equal(expected.Select(p => p.k).ToArray(), keys);
            Assert.Equal(expected.Select(p => p.i).ToArray(), items);
        }

        [Fact]
        public void RefCountVector_Indices_SameAsEnumerator()
        {
            // the valid indices in increasing order, with gaps (freed indices, also at the start and the end)
            var rand = new Random(9);
            var v = new RefCountVector();
            for (int i = 0; i < 5000; ++i)
                v.allocate();
            for (int i = 0; i < 5000; ++i)
                if (rand.Next(3) == 0 || i < 7 || i > 4990)
                    v.decrement(i);
            var expected = new List<int>();
            foreach (int i in v)
                expected.Add(i);
            Assert.Equal(expected, v.Indices().ToList());
            Assert.True(expected.Count > 1000 && expected[0] == 7);
            // a mesh's ids enumerate the same way after removals
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { EdgeVertices = 8 }.Generate().MakeDMesh();
            foreach (int t in mesh.TriangleIndices().Where(t => t % 5 == 0).ToList())
                mesh.RemoveTriangle(t, true, false);
            Assert.Equal(Enumerable.Range(0, mesh.MaxTriangleID).Where(mesh.IsTriangle), mesh.TriangleIndices());
            Assert.Equal(Enumerable.Range(0, mesh.MaxVertexID).Where(mesh.IsVertex), mesh.VertexIndices());
            Assert.Equal(Enumerable.Range(0, mesh.MaxEdgeID).Where(mesh.IsEdge), mesh.EdgeIndices());
        }

        [Fact]
        public void InputTriangles_CornersAndCoordinatesOfTheirMeshTriangles()
        {
            // two meshes with removed triangles (ids with gaps), a degenerate triangle and signed zeros (the input
            // coordinates, not those of the merged point); a second Compute appends the triangles again (as before
            // the coordinates moved out of the triangles), the first ones keep their coordinates
            DMesh3 a = new Sphere3Generator_NormalizedCube() { EdgeVertices = 5 }.Generate().MakeDMesh();
            foreach (int t in a.TriangleIndices().Where(t => t % 7 == 3).ToList())
                a.RemoveTriangle(t, false, false);
            DMesh3 b = new DMesh3();
            int v0 = b.AppendVertex(new Vector3d(-0.0, 0.25, 3)), v1 = b.AppendVertex(new Vector3d(1, -0.0, 3)), v2 = b.AppendVertex(new Vector3d(2, 0.5, 3));
            int v3 = b.AppendVertex(new Vector3d(0.1, 0.2, -0.0));
            b.AppendTriangle(v0, v1, v3);
            b.AppendTriangle(v0, v1, v2);
            b.AppendTriangle(v1, v2, v3);
            // collinear corners
            Assert.True(b.AppendTriangle(b.AppendVertex(new Vector3d(5, 1, -0.0)), b.AppendVertex(new Vector3d(5.5, 1.5, 0)), b.AppendVertex(new Vector3d(6, 2, 0))) >= 0);
            var meshes = new List<DMesh3>() { a, b };
            var arr = new ExactMeshArrangement(meshes);
            arr.Compute();
            var firstCorners = arr.Triangles.Select(t => t.Corners).ToList();
            arr.Compute();
            Assert.Equal(firstCorners, arr.Triangles.Take(firstCorners.Count).Select(t => t.Corners));
            int expected = 2 * (a.TriangleCount + b.TriangleCount);
            Assert.Equal(expected, arr.Triangles.Count);
            Assert.Contains(arr.Triangles, t => t.Degenerate);
            for (int i = 0; i < arr.Triangles.Count; ++i) {
                ExactMeshArrangement.InputTriangle t = arr.Triangles[i];
                DMesh3 m = meshes[t.Mesh];
                Index3i tv = m.GetTriangle(t.Tid);
                if (i >= expected / 2)
                    Assert.Equal(i, arr.MeshTriangles[t.Mesh][i - expected / 2 - (t.Mesh == 0 ? 0 : a.TriangleCount)]);
                Vector3d[] got = { t.V0, t.V1, t.V2 };
                for (int k = 0; k < 3; ++k) {
                    if (i >= expected / 2)     // (the point ids of the second Compute)
                        Assert.Equal(arr.VertexPoints[t.Mesh][tv[k]], t.Corners[k]);
                    Vector3d want = m.GetVertex(tv[k]);
                    for (int c = 0; c < 3; ++c)
                        Assert.Equal(BitConverter.DoubleToInt64Bits(want[c]), BitConverter.DoubleToInt64Bits(got[k][c]));
                }
            }
        }

        [Fact]
        public void EdgePlanes_ComputedWhenUsed_MatchTheirApproximations()
        {
            // two overlapping spheres (triangles near the intersection have planes); the exact edge planes are not
            // kept with the plane, they are computed on first use (also by several threads at once): the same planes
            // as a direct construction from the exact corners, the same object on later uses, and the approximations
            // (computed with the plane) are their values rounded
            DMesh3 a = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Translate(b, new Vector3d(0.31, 0.27, 0.05));
            var arr = new ExactMeshArrangement(new List<DMesh3>() { a, b });
            arr.Compute();
            var tris = arr.Triangles.Where(t => t.Segments.Count > 0).ToList();
            Assert.True(tris.Count > 20);
            var first = new ExactMeshArrangement.Plane[tris.Count][];
            gParallel.ForRange(tris.Count, (i) => first[i] = tris[i].EdgePlanes);
            for (int i = 0; i < tris.Count; ++i) {
                ExactMeshArrangement.InputTriangle t = tris[i];
                Assert.Same(first[i], t.EdgePlanes);
                ExactPoint3[] c = { arr.Exact(t.Corners[0]), arr.Exact(t.Corners[1]), arr.Exact(t.Corners[2]) };
                ExactMeshArrangement.Plane P = t.P;
                for (int k = 0; k < 3; ++k) {
                    ExactPoint3 p = c[k], q = c[(k + 1) % 3];
                    // normal of the edge plane: (q - p) x n, with n the triangle's normal (perpendicular to both)
                    System.Numerics.BigInteger dx = q.X - p.X, dy = q.Y - p.Y, dz = q.Z - p.Z;
                    var e = t.EdgePlanes[k];
                    Assert.Equal(dy * P.C - dz * P.B, e.A);
                    Assert.Equal(dz * P.A - dx * P.C, e.B);
                    Assert.Equal(dx * P.B - dy * P.A, e.C);
                    Assert.Equal(e.A * p.X + e.B * p.Y + e.C * p.Z, e.D);
                    Assert.Equal(e.A * q.X + e.B * q.Y + e.C * q.Z, e.D);
                    double[] pa = t.PlaneApprox;
                    Assert.Equal(new[] { (double)e.A, (double)e.B, (double)e.C, (double)e.D }, pa.Skip(4 * (k + 1)).Take(4).ToArray());
                }
            }
        }

        [Fact]
        public void ForRange_ThrowsTheExceptionOfTheBody()
        {
            // (not an AggregateException: as the serial loops before, eg ExactMeshBoolean's "no non-degenerate ray")
            Assert.Throws<InvalidOperationException>(() => gParallel.ForRange(1000, (i) => { if (i == 500) throw new InvalidOperationException("x"); }));
            Assert.Throws<InvalidOperationException>(() => gParallel.ForRange(1000, () => 0, (i, local) => { if (i == 7) throw new InvalidOperationException("x"); }));
        }

        [Fact]
        public void ForRange_VisitsEveryIndexOnce_WithItsOwnLocalState()
        {
            foreach (int n in new[] { 0, 1, 17, 100000 }) {
                var count = new int[n];
                gParallel.ForRange(n, (i) => System.Threading.Interlocked.Increment(ref count[i]));
                Assert.All(count, c => Assert.Equal(1, c));
                var local = new int[n];
                gParallel.ForRange(n, () => new List<int>(), (i, buffer) => {
                    // (a buffer is never used by two threads at once: it holds only this index while the body runs)
                    buffer.Clear();
                    buffer.Add(i);
                    System.Threading.Thread.SpinWait(10);
                    local[i] = buffer.Single();
                });
                Assert.Equal(Enumerable.Range(0, n).ToArray(), local);
            }
            // serial on this thread: in order, on the calling thread, one local
            gParallel.SerialOnThisThread = true;
            try {
                var order = new List<int>();
                int thread = Environment.CurrentManagedThreadId;
                var locals = new HashSet<List<int>>();
                gParallel.ForRange(100, () => new List<int>(), (i, buffer) => {
                    Assert.Equal(thread, Environment.CurrentManagedThreadId);
                    locals.Add(buffer);
                    order.Add(i);
                });
                gParallel.ForRange(100, (i) => order.Add(100 + i));
                Assert.Equal(Enumerable.Range(0, 200).ToList(), order);
                Assert.Single(locals);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }
    }
}
