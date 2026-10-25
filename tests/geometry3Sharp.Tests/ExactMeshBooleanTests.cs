using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    public class ExactMeshBooleanTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var box = new Box3d(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2));
            DMesh3 mesh = new TrivialBox3Generator() { Box = box }.Generate().MakeDMesh();
            // exact corners (center +- extent can round)
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d v = mesh.GetVertex(vid);
                mesh.SetVertex(vid, new Vector3d(v.x < box.Center.x ? x0 : x1, v.y < box.Center.y ? y0 : y1, v.z < box.Center.z ? z0 : z1));
            }
            Assert.True(Volume(mesh) > 0);
            return mesh;
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices = 12)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, center);
            return mesh;
        }

        static double Volume(DMesh3 mesh)
        {
            return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
        }

        static DMesh3 Run(DMesh3 a, DMesh3 b, Ops op, bool expectClosed = true)
        {
            var boolean = new ExactMeshBoolean(a, b, op);
            Assert.True(boolean.Compute(), boolean.Error);
            DMesh3 r = boolean.Result;
            Assert.True(r.CheckValidity(true, FailMode.ReturnOnly), "invalid mesh");
            // (DMesh3.IsClosed() is false for an empty mesh)
            if (expectClosed && r.TriangleCount > 0) {
                Assert.True(r.IsClosed(), $"{op}: {MeshIterators.BoundaryEdges(r).Count()} boundary edges");
                Assert.Equal(0, boolean.NonManifoldTriangles);
            }
            return r;
        }


        [Fact]
        public void Boxes_GeneralPosition_ExactVolumes()
        {
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), b = Box(0.5, 0.25, -0.5, 1.5, 0.75, 0.5);
            // overlap 0.5 x 0.5 x 0.5
            Assert.Equal(1.0 + 0.5 - 0.125, Volume(Run(a, b, Ops.Union)), 12);
            Assert.Equal(0.125, Volume(Run(a, b, Ops.Intersection)), 12);
            Assert.Equal(1.0 - 0.125, Volume(Run(a, b, Ops.Difference)), 12);
            Assert.Equal(0.5 - 0.125, Volume(Run(b, a, Ops.Difference)), 12);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(Run(a, b, Ops.Union)));
        }

        [Fact]
        public void Boxes_CoplanarFaces()
        {
            // b shares the planes y = 0, y = 1, z = 0, z = 1 with a
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), b = Box(0.5, 0, 0, 1.5, 1, 1);
            DMesh3 union = Run(a, b, Ops.Union);
            Assert.Equal(1.5, Volume(union), 12);
            Assert.Equal(0.5, Volume(Run(a, b, Ops.Intersection)), 12);
            Assert.Equal(0.5, Volume(Run(a, b, Ops.Difference)), 12);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(union));
            // the union is a box: all faces are axis-aligned and on its boundary
            foreach (int vid in union.VertexIndices()) {
                Vector3d v = union.GetVertex(vid);
                Assert.True(v.x == 0 || v.x == 1.5 || v.y == 0 || v.y == 1 || v.z == 0 || v.z == 1, v.ToString());
            }
        }

        [Fact]
        public void Boxes_PartlyCoplanar_Staircase()
        {
            // touching and coplanar in several planes at once
            DMesh3 a = Box(0, 0, 0, 2, 2, 1), b = Box(1, 1, 0, 3, 3, 2);
            Assert.Equal(4 + 8 - 1, Volume(Run(a, b, Ops.Union)), 12);
            Assert.Equal(1, Volume(Run(a, b, Ops.Intersection)), 12);
            Assert.Equal(3, Volume(Run(a, b, Ops.Difference)), 12);
            Assert.Equal(7, Volume(Run(b, a, Ops.Difference)), 12);
        }

        [Fact]
        public void IdenticalMeshes()
        {
            DMesh3 a = Sphere(Vector3d.Zero, 1);
            double v = Volume(a);
            Assert.Equal(v, Volume(Run(a, new DMesh3(a), Ops.Union)), 12);
            Assert.Equal(v, Volume(Run(a, new DMesh3(a), Ops.Intersection)), 12);
            DMesh3 diff = Run(a, new DMesh3(a), Ops.Difference);
            Assert.Equal(0, diff.TriangleCount);
        }

        [Fact]
        public void TouchingFaces()
        {
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), b = Box(1, 0, 0, 2, 1, 1);
            DMesh3 union = Run(a, b, Ops.Union);
            Assert.Equal(2.0, Volume(union), 12);
            Assert.Equal(0, Run(a, b, Ops.Intersection).TriangleCount);
            Assert.Equal(1.0, Volume(Run(a, b, Ops.Difference)), 12);
        }

        [Fact]
        public void DisjointAndNested()
        {
            DMesh3 a = Sphere(Vector3d.Zero, 2), far = Sphere(new Vector3d(10, 0, 0), 1), inner = Sphere(new Vector3d(0.1, 0.2, 0.3), 1);
            double va = Volume(a), vf = Volume(far), vi = Volume(inner);
            Assert.Equal(va + vf, Volume(Run(a, far, Ops.Union)), 9);
            Assert.Equal(0, Run(a, far, Ops.Intersection).TriangleCount);
            Assert.Equal(va, Volume(Run(a, far, Ops.Difference)), 9);
            Assert.Equal(va, Volume(Run(a, inner, Ops.Union)), 9);
            Assert.Equal(vi, Volume(Run(a, inner, Ops.Intersection)), 9);
            DMesh3 shell = Run(a, inner, Ops.Difference);
            Assert.Equal(va - vi, Volume(shell), 9);
            Assert.Equal(a.TriangleCount + inner.TriangleCount, shell.TriangleCount);
        }

        static Quaterniond RandomRotation(Random rand)
        {
            var axis = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5).Normalized;
            return new Quaterniond(axis, rand.NextDouble() * 360);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void RotatedShapes_VolumeIdentities(int seed)
        {
            var rand = new Random(seed);
            DMesh3 a = (seed % 2 == 0) ? Sphere(Vector3d.Zero, 1, 10) : Box(-1, -1, -1, 1, 1, 1);
            DMesh3 b = Sphere(new Vector3d(0.6, 0.3, -0.2), 0.9, 8);
            MeshTransforms.Rotate(a, Vector3d.Zero, RandomRotation(rand));
            MeshTransforms.Rotate(b, new Vector3d(0.6, 0.3, -0.2), RandomRotation(rand));
            DMesh3 union = Run(a, b, Ops.Union), inter = Run(a, b, Ops.Intersection), diff = Run(a, b, Ops.Difference);
            double va = Volume(a), vb = Volume(b);
            // inclusion-exclusion holds exactly for the exact result, here up to the rounding of new vertices
            Assert.Equal(va + vb, Volume(union) + Volume(inter), 9);
            Assert.Equal(va, Volume(diff) + Volume(inter), 9);
            Assert.True(Volume(inter) > 0.1);
            foreach (DMesh3 r in new[] { union, inter, diff })
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(r));
        }

        [Fact]
        public void NearlyCoincident_ShiftedByOneUlp()
        {
            DMesh3 a = Sphere(Vector3d.Zero, 1, 8);
            DMesh3 b = new DMesh3(a);
            foreach (int vid in b.VertexIndices().ToArray()) {
                Vector3d v = b.GetVertex(vid);
                b.SetVertex(vid, new Vector3d(v.x + 1e-15, v.y, v.z));
            }
            double va = Volume(a);
            DMesh3 union = Run(a, b, Ops.Union), inter = Run(a, b, Ops.Intersection);
            Assert.Equal(va, Volume(union), 9);
            Assert.Equal(va, Volume(inter), 9);
        }

        [Fact]
        public void LargeCoordinates()
        {
            double x = 500000.123, y = 5700000.456;
            DMesh3 a = Box(x, y, 100, x + 10, y + 10, 110), b = Box(x + 5, y + 2.5, 105, x + 15, y + 7.5, 115);
            Assert.Equal(1000 + 500 - 125, Volume(Run(a, b, Ops.Union)), 6);
            Assert.Equal(125, Volume(Run(a, b, Ops.Intersection)), 6);
        }

        [Fact]
        public void SharedVerticesAndEdges()
        {
            // b is a corner of a, cut out by a box with the same corner: shared vertex and coplanar faces
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), b = Box(0, 0, 0, 0.5, 0.5, 0.5);
            Assert.Equal(1.0, Volume(Run(a, b, Ops.Union)), 12);
            Assert.Equal(0.125, Volume(Run(a, b, Ops.Intersection)), 12);
            Assert.Equal(0.875, Volume(Run(a, b, Ops.Difference)), 12);
        }

        static DMesh3 Tetra(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            DMesh3 m = new DMesh3();
            foreach (Vector3d v in new[] { a, b, c, d }) m.AppendVertex(v);
            m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 1, 3); m.AppendTriangle(1, 2, 3); m.AppendTriangle(0, 3, 2);
            if (Volume(m) < 0) {
                m.ReverseOrientation();
            }
            return m;
        }

        [Fact]
        public void SameBox_DifferentTriangulation()
        {
            // mirrored copy: the same box, with the other face diagonals (coplanar crossing triangles everywhere)
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), b = new DMesh3(a);
            foreach (int vid in b.VertexIndices().ToArray()) {
                Vector3d v = b.GetVertex(vid);
                b.SetVertex(vid, new Vector3d(1 - v.x, v.y, v.z));
            }
            b.ReverseOrientation();
            Assert.Equal(1.0, Volume(b), 12);
            Assert.Equal(1.0, Volume(Run(a, b, Ops.Union)), 12);
            Assert.Equal(1.0, Volume(Run(a, b, Ops.Intersection)), 12);
            Assert.Equal(0, Run(a, b, Ops.Difference).TriangleCount);
        }

        [Fact]
        public void TetraVertexOnFaceAndEdge()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            // one vertex exactly on the top face, one exactly on a top edge, penetrating
            DMesh3 t = Tetra(new Vector3d(0.5, 0.5, 1), new Vector3d(0.25, 0, 1), new Vector3d(0.75, 0.25, 0.5), new Vector3d(0.5, 0.5, 2));
            double vc = Volume(cube), vt = Volume(t);
            DMesh3 union = Run(cube, t, Ops.Union), inter = Run(cube, t, Ops.Intersection), diff = Run(cube, t, Ops.Difference);
            Assert.Equal(vc + vt, Volume(union) + Volume(inter), 12);
            Assert.Equal(vc, Volume(diff) + Volume(inter), 12);
            Assert.True(Volume(inter) > 0);
            foreach (DMesh3 r in new[] { union, inter, diff })
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(r));
        }

        [Fact]
        public void TouchingAtVertexOnly()
        {
            DMesh3 cube = Box(0, 0, 0, 1, 1, 1);
            DMesh3 t = Tetra(new Vector3d(0.5, 0.5, 1), new Vector3d(0, 0, 2), new Vector3d(1, 0, 2), new Vector3d(0.5, 1, 2));
            DMesh3 union = Run(cube, t, Ops.Union);
            Assert.Equal(Volume(cube) + Volume(t), Volume(union), 12);
            Assert.Equal(cube.TriangleCount + 2 + t.TriangleCount, union.TriangleCount);    // the top face gets the touching vertex
            Assert.Equal(0, Run(cube, t, Ops.Intersection).TriangleCount);
        }

        [Fact]
        public void CubesTouchingAlongAnEdge_Union()
        {
            // the union is non-manifold along the shared edge: the result keeps all faces, with duplicated vertices
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), b = Box(1, 1, 0, 2, 2, 1);
            var boolean = new ExactMeshBoolean(a, b, Ops.Union);
            Assert.True(boolean.Compute());
            Assert.Equal(2.0, Volume(boolean.Result), 12);
            Assert.Equal(24, boolean.Result.TriangleCount);
        }

        [Fact]
        public void OpenMesh_ReturnsError()
        {
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), b = Box(0.5, 0.5, 0.5, 2, 2, 2);
            b.RemoveTriangle(b.TriangleIndices().First(), true, false);
            var boolean = new ExactMeshBoolean(a, b, Ops.Union);
            Assert.False(boolean.Compute());
            Assert.Contains("closed", boolean.Error);
        }
    }
}
