using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Bugs found (and fixed) in forks of geometry3Sharp, reproduced against this code base.
    /// </summary>
    public class ForkFixesTests
    {
        static DMesh3 Sphere() { return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh(); }

        // --- DMesh3.CompactInPlace (SoftSmile-Inc) ---

        [Fact]
        public void CompactInPlace_EmptyMesh()
        {
            DMesh3 mesh = Sphere();
            foreach (int tid in mesh.TriangleIndices().ToArray())
                mesh.RemoveTriangle(tid, true, false);
            Assert.Equal(0, mesh.VertexCount);
            mesh.CompactInPlace();
            Assert.Equal(0, mesh.TriangleCount);
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void CompactInPlace_AlreadyCompactMesh()
        {
            DMesh3 mesh = Sphere();
            Assert.True(mesh.IsCompact);
            int nt = mesh.TriangleCount;
            mesh.CompactInPlace();
            Assert.Equal(nt, mesh.TriangleCount);
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void CompactInPlace_WithIsolatedVertex()
        {
            DMesh3 mesh = Sphere();
            foreach (int tid in mesh.TriangleIndices().Take(10).ToArray())
                mesh.RemoveTriangle(tid, true, false);
            mesh.AppendVertex(new Vector3d(5, 5, 5));      // no edges
            mesh.CompactInPlace();
            Assert.True(mesh.IsCompact);
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
        }

        // --- DMesh3.RemoveVertex at a bowtie vertex (SoftSmile-Inc) ---

        [Fact]
        public void RemoveVertex_Bowtie_RemovesAllTriangles()
        {
            DMesh3 mesh = new DMesh3();
            int c = mesh.AppendVertex(Vector3d.Zero);
            int a0 = mesh.AppendVertex(new Vector3d(1, 0, 0)), a1 = mesh.AppendVertex(new Vector3d(1, 1, 0)), a2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
            int b0 = mesh.AppendVertex(new Vector3d(-1, 0, 0)), b1 = mesh.AppendVertex(new Vector3d(-1, -1, 0)), b2 = mesh.AppendVertex(new Vector3d(0, -1, 0));
            mesh.AppendTriangle(c, a0, a1); mesh.AppendTriangle(c, a1, a2);
            mesh.AppendTriangle(c, b0, b1); mesh.AppendTriangle(c, b1, b2);
            Assert.True(mesh.IsBowtieVertex(c));
            Assert.Equal(MeshResult.Ok, mesh.RemoveVertex(c, true, false));
            Assert.False(mesh.IsVertex(c));
            Assert.Equal(0, mesh.TriangleCount);
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
        }

        // --- DynamicPriorityQueue.Contains after Clear(false) (SoftSmile-Inc), Dijkstra reuse (rarora7777) ---

        class Node : DynamicPriorityQueueNode { }

        [Fact]
        public void PriorityQueue_ContainsAfterClearWithoutFreeingMemory()
        {
            var queue = new DynamicPriorityQueue<Node>();
            var nodes = Enumerable.Range(0, 5).Select(i => new Node()).ToArray();
            for (int i = 0; i < 5; ++i)
                queue.Enqueue(nodes[i], i);
            queue.Clear(false);
            var other = new Node();
            queue.Enqueue(other, 1);
            Assert.True(queue.Contains(other));
            Assert.All(nodes, n => Assert.False(queue.Contains(n)));
        }

        [Fact]
        public void Dijkstra_SparseReset_SameResultTwice()
        {
            DMesh3 mesh = Sphere();
            var dijkstra = DijkstraGraphDistance.MeshVertices(mesh, true);
            dijkstra.AddSeed(0, 0);
            dijkstra.Compute();
            var first = mesh.VertexIndices().Select(v => dijkstra.GetDistance(v)).ToArray();
            dijkstra.Reset();
            dijkstra.AddSeed(0, 0);
            dijkstra.Compute();
            var second = mesh.VertexIndices().Select(v => dijkstra.GetDistance(v)).ToArray();
            Assert.Equal(first, second);
        }

        // --- DijkstraGraphDistance.ComputeDense distances (SoftSmile-Inc) ---

        [Fact]
        public void Dijkstra_Dense_DistancesOnAChain()
        {
            // chain 0 - 1 - 2 - 3 with unit edges
            var dijkstra = new DijkstraGraphDistance(4, false, (id) => true, (a, b) => 1.0f,
                (a) => new[] { a - 1, a + 1 }.Where(n => n >= 0 && n < 4));
            dijkstra.AddSeed(0, 0);
            dijkstra.Compute();
            for (int i = 0; i < 4; ++i)
                Assert.Equal(i, dijkstra.GetDistance(i), 5);
        }

        // --- LaplacianMeshSmoother (archformco, SoftSmile-Inc) ---

        [Fact]
        public void LaplacianSmoother_NonCompactMesh_SameResultAsCompacted()
        {
            DMesh3 mesh = Sphere();
            foreach (int tid in mesh.TriangleIndices().Where(t => mesh.GetTriCentroid(t).z > 0.7).ToArray())
                mesh.RemoveTriangle(tid, true, false);
            Assert.False(mesh.IsCompact);
            DMesh3 compact = new DMesh3(mesh, true);
            Func<DMesh3, Vector3d[]> smooth = (m) => {
                var smoother = new LaplacianMeshSmoother(m);
                foreach (int vid in m.VertexIndices())
                    if (m.IsBoundaryVertex(vid))
                        smoother.SetConstraint(vid, m.GetVertex(vid), 10, true);
                Assert.True(smoother.SolveAndUpdateMesh());
                return m.VertexIndices().Select(v => m.GetVertex(v)).ToArray();
            };
            var a = smooth(mesh);
            var b = smooth(compact);
            for (int i = 0; i < a.Length; ++i)
                Assert.True(a[i].Distance(b[i]) < 1e-6, $"vertex {i}: {a[i]} vs {b[i]}");
        }

        [Fact]
        public void LaplacianSmoother_RegionSmooth_WholeClosedMesh()
        {
            DMesh3 mesh = Sphere();
            // a region whose exterior rings cover the rest of the closed mesh: no border vertices
            var tris = mesh.TriangleIndices().Where(t => mesh.GetTriCentroid(t).z > -0.9).ToArray();
            LaplacianMeshSmoother.RegionSmooth(mesh, tris, 2, 3, false);
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
        }

        // --- math (SoftSmile-Inc, nsmela) ---

        [Fact]
        public void Matrix3_FromFunction()
        {
            var m = new Matrix3d((r, c) => 10 * r + c);
            var f = new Matrix3f((r, c) => 10 * r + c);
            for (int r = 0; r < 3; ++r)
                for (int c = 0; c < 3; ++c) {
                    Assert.Equal(10 * r + c, m[r, c]);
                    Assert.Equal(10 * r + c, f[r, c]);
                }
        }

        [Fact]
        public void DistPoint2Circle2_PointAtCenter_ClosestIsOnCircle()
        {
            var dist = new DistPoint2Circle2(new Vector2d(1, 2), new Circle2d(new Vector2d(1, 2), 3)).Compute();
            Assert.Equal(3, dist.CircleClosest.Distance(new Vector2d(1, 2)), 9);
        }

        [Fact]
        public void MeshTransforms_RotateQuaternionf_RotatesNormals()
        {
            DMesh3 mesh = Sphere();
            if (mesh.HasVertexNormals == false)
                MeshNormals.QuickCompute(mesh);
            var q = new Quaternionf(Vector3f.AxisZ, 90);
            MeshTransforms.Rotate(mesh, Vector3d.Zero, q);
            foreach (int vid in mesh.VertexIndices())
                Assert.True(((Vector3d)mesh.GetVertexNormal(vid)).Dot(mesh.GetVertex(vid).Normalized) > 0.95);
        }

        // --- DenseGridTrilinearImplicit bounds (SoftSmile-Inc) ---

        [Fact]
        public void DenseGridTrilinearImplicit_BoundsAreTheSampledRegion()
        {
            var grid = new DenseGrid3f(4, 4, 4, 1);
            var iso = new DenseGridTrilinearImplicit(grid, Vector3d.Zero, 0.5) { Outside = 100 };
            AxisAlignedBox3d b = iso.Bounds();
            Assert.Equal(1.5, b.Max.x, 9);      // 4 nodes, 3 cells of 0.5
            Vector3d onMaxFace = new Vector3d(1.5, 0.7, 0.7);
            Assert.Equal(1.0, iso.Value(ref onMaxFace), 6);
            Vector3d outside = new Vector3d(1.6, 0.7, 0.7);
            Assert.Equal(100, iso.Value(ref outside));
        }

        // --- prime-modulo edge iteration (upstream dotnet8 branch) ---

        [Theory]
        [InlineData(10)]
        [InlineData(31337)]
        [InlineData(62674)]
        public void ModuloIteration_VisitsEveryIndex(int n)
        {
            var seen = new HashSet<int>(MathUtil.ModuloIteration(n));
            Assert.Equal(n, seen.Count);
            Assert.All(seen, i => Assert.InRange(i, 0, n - 1));
        }
    }
}
