using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    // ExactMeshBoolean.SimplifyFlatRegions: new vertices inside flat regions and on straight edges are removed
    public class ExactPlanarSimplifyTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }

        static double Volume(DMesh3 mesh)
        {
            return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
        }

        static DMesh3 Run(DMesh3 a, DMesh3 b, Ops op, bool simplify = true)
        {
            var boolean = new ExactMeshBoolean(a, b, op) { SimplifyFlatRegions = simplify };
            Assert.True(boolean.Compute(), boolean.Error);
            DMesh3 r = boolean.Result;
            Assert.True(r.CheckValidity(true, FailMode.ReturnOnly), "invalid mesh");
            if (r.TriangleCount > 0)
                Assert.True(r.IsClosed(), $"{op}: {MeshIterators.BoundaryEdges(r).Count()} boundary edges");
            Assert.Equal(0, boolean.NonManifoldTriangles);
            return r;
        }

        // every vertex coordinate is one of the input coordinates (no rounded intersection points)
        static void AssertInputCoordinates(DMesh3 m, IEnumerable<double> values)
        {
            var set = new HashSet<double>(values);
            foreach (Vector3d v in m.Vertices())
                for (int k = 0; k < 3; ++k)
                    Assert.True(set.Contains(v[k]), $"vertex {v.x:R} {v.y:R} {v.z:R} is not an input coordinate");
        }

        [Fact]
        public void CubeColumn_CoplanarOverlap_OnlyInputCoordinates()
        {
            // the side faces overlap in the same planes: their diagonals cross at points that are not needed
            DMesh3 cur = Box(0, 0, 0, 1, 1, 1);
            foreach (double z in new[] { 0.9, 1.8 })
                cur = Run(cur, Box(0, 0, z, 1, 1, z + 1), Ops.Union);
            AssertInputCoordinates(cur, new[] { 0, 1, 0.9, 1.9, 1.8, 2.8 });
            Assert.Equal(2.8, Volume(cur), 12);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(cur));
            // the box with the input corners on its vertical edges: 4 x 6 vertices on the side edges
            Assert.True(cur.VertexCount <= 24, $"{cur.VertexCount} vertices");
        }

        [Fact]
        public void CubeGrid_IteratedUnions_DoNotGrow()
        {
            // 3 x 3 x 3 overlapping cubes united one after the other: without the simplification the rounded
            // crossing points are split again by every following union and the triangle count explodes
            int n = 3;
            var coords = new List<double>();
            for (int i = 0; i < n; ++i) {
                coords.Add(0.9 * i);
                coords.Add(0.9 * i + 1);
            }
            DMesh3 cur = null;
            var counts = new List<int>();
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j)
                    for (int k = 0; k < n; ++k) {
                        DMesh3 c = Box(0.9 * i, 0.9 * j, 0.9 * k, 0.9 * i + 1, 0.9 * j + 1, 0.9 * k + 1);
                        cur = (cur == null) ? c : Run(cur, c, Ops.Union);
                        counts.Add(cur.TriangleCount);
                    }
            AssertInputCoordinates(cur, coords);
            double side = 0.9 * (n - 1) + 1;
            Assert.Equal(side * side * side, Volume(cur), 9);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(cur));
            // at most the input corners on the faces of the box (2n - 1 values per coordinate)
            int perSide = 2 * n;
            Assert.True(cur.VertexCount <= 6 * perSide * perSide, $"{cur.VertexCount} vertices");
            Assert.True(cur.TriangleCount <= 2 * cur.VertexCount, $"{cur.TriangleCount} triangles");
        }

        [Fact]
        public void StraightEdge_CrossingPointRemoved()
        {
            // the boxes' front faces (y = 0) overlap, the diagonal of one crosses the bottom edge region of the
            // other: crossing points on the straight edges of the union are removed, the input corners stay
            DMesh3 a = Box(0, 0, 0, 2, 1, 1), b = Box(1, 0, 0.5, 3, 1, 1.5);
            DMesh3 off = Run(a, b, Ops.Union, simplify: false), on = Run(a, b, Ops.Union);
            Assert.Equal(Volume(off), Volume(on), 12);
            AssertInputCoordinates(on, new[] { 0, 1, 2, 3, 0.5, 1.5 });
            Assert.True(on.TriangleCount < off.TriangleCount);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(on));
            Assert.Equal(1 * 2 * 1 + 2 * 1 * 1 - 1 * 1 * 0.5, Volume(on), 12);
        }

        [Fact]
        public void AllOperations_SameVolumeAsWithoutSimplification()
        {
            var rand = new Random(5);
            for (int iter = 0; iter < 20; ++iter) {
                // boxes on a coarse grid: many coplanar faces and collinear edges, some touching
                Func<double> r = () => rand.Next(0, 5) * 0.3;
                DMesh3 a = Box(r(), r(), r(), 1.5 + r(), 1.5 + r(), 1.5 + r());
                DMesh3 b = Box(r(), r(), r(), 1.5 + r(), 1.5 + r(), 1.5 + r());
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                    var boolean = new ExactMeshBoolean(a, b, op);
                    Assert.True(boolean.Compute());
                    var plain = new ExactMeshBoolean(a, b, op) { SimplifyFlatRegions = false };
                    Assert.True(plain.Compute());
                    Assert.Equal(Volume(plain.Result), Volume(boolean.Result), 9);
                    Assert.True(boolean.Result.TriangleCount <= plain.Result.TriangleCount);
                    if (boolean.Result.TriangleCount > 0)
                        Assert.Equal(plain.Result.IsClosed(), boolean.Result.IsClosed());
                    Assert.Equal(0, MeshRepairMetrics.SelfIntersections(boolean.Result));
                }
            }
        }

        [Fact]
        public void CurvedMeshes_InputVerticesKept()
        {
            // no flat regions with new points: the input vertices that are on the result are all kept
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            MeshTransforms.Translate(b, new Vector3d(0.7, 0.2, 0.1));
            DMesh3 off = Run(a, b, Ops.Union, simplify: false), on = Run(a, b, Ops.Union);
            Assert.Equal(Volume(off), Volume(on), 12);
            var verts = new HashSet<Vector3d>(on.Vertices());
            foreach (DMesh3 m in new[] { a, b })
                foreach (Vector3d v in m.Vertices())
                    if (off.Vertices().Contains(v))
                        Assert.Contains(v, verts);
        }
    }
}
