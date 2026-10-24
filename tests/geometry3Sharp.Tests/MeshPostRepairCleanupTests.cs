using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class MeshPostRepairCleanupTests
    {
        static DMesh3 Sphere(double radius = 1, Vector3d center = default(Vector3d))
        {
            var gen = new Sphere3Generator_NormalizedCube() { EdgeVertices = 12, Radius = radius };
            DMesh3 mesh = gen.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, center);
            return mesh;
        }

        static double Volume(DMesh3 mesh)
        {
            return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
        }

        static int DegenerateCount(DMesh3 mesh)
        {
            double diag = mesh.CachedBounds.DiagonalLength;
            return mesh.TriangleIndices().Count(t => mesh.GetTriArea(t) < 1e-12 * diag * diag);
        }

        static int ShortEdgeCount(DMesh3 mesh, double len)
        {
            return mesh.EdgeIndices().Count(e => {
                Index2i ev = mesh.GetEdgeV(e);
                return mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)) < len;
            });
        }

        [Fact]
        public void CleanMesh_IsUnchanged()
        {
            DMesh3 mesh = Sphere();
            int nV = mesh.VertexCount, nT = mesh.TriangleCount;
            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();

            Assert.Equal(nV, mesh.VertexCount);
            Assert.Equal(nT, mesh.TriangleCount);
            Assert.Equal(0, cleanup.RemovedDuplicates + cleanup.FixedDegenerate + cleanup.FixedFolds
                + cleanup.DisconnectedBowties + cleanup.FlippedComponents);
        }

        [Fact]
        public void DuplicateTrianglePair_IsRemoved()
        {
            DMesh3 mesh = Sphere();
            int nT = mesh.TriangleCount;
            int a = mesh.AppendVertex(new Vector3d(5, 0, 0));
            int b = mesh.AppendVertex(new Vector3d(6, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(5, 1, 0));
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, b);

            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();

            Assert.Equal(2, cleanup.RemovedDuplicates);
            Assert.Equal(nT, mesh.TriangleCount);
            Assert.True(mesh.IsClosed());
        }

        [Fact]
        public void CapTriangle_IsFlippedAway()
        {
            DMesh3 mesh = Sphere();
            // poke a triangle with a point almost on one of its edges -> cap triangle with ~zero area
            DMesh3.PokeTriangleInfo info;
            Assert.Equal(MeshResult.Ok, mesh.PokeTriangle(mesh.TriangleIndices().First(), new Vector3d(0.5, 0.5 - 1e-9, 1e-9), out info));
            Assert.True(DegenerateCount(mesh) > 0);

            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();

            Assert.Equal(0, DegenerateCount(mesh));
            Assert.True(mesh.IsClosed());
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void NeedleTriangles_AreCollapsed()
        {
            DMesh3 mesh = Sphere();
            // split an edge very close to one end -> very short edge with two needle triangles
            int eid = mesh.EdgeIndices().First();
            Index2i ev = mesh.GetEdgeV(eid);
            DMesh3.EdgeSplitInfo split;
            Assert.Equal(MeshResult.Ok, mesh.SplitEdge(eid, out split));
            mesh.SetVertex(split.vNew, Vector3d.Lerp(mesh.GetVertex(ev.a), mesh.GetVertex(ev.b), 1e-8));
            Assert.Equal(1, ShortEdgeCount(mesh, 1e-6));

            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();

            Assert.Equal(0, ShortEdgeCount(mesh, 1e-6));
            Assert.True(mesh.IsClosed());
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void InsideOutComponent_IsFlipped_ButCavityIsKept()
        {
            DMesh3 mesh = Sphere(1, Vector3d.Zero);
            // separate inside-out sphere -> should be flipped
            DMesh3 inverted = Sphere(1, new Vector3d(5, 0, 0));
            inverted.ReverseOrientation();
            MeshEditor.Append(mesh, inverted);
            // inside-out sphere inside the first one = cavity -> must stay inside-out
            DMesh3 cavity = Sphere(0.5, Vector3d.Zero);
            cavity.ReverseOrientation();
            MeshEditor.Append(mesh, cavity);
            Assert.Single(MeshPostRepairCleanup.FindInvertedComponents(mesh));

            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();

            Assert.Equal(1, cleanup.FlippedComponents);
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(mesh));
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            var vols = comps.Components.Select(c => MeshMeasurements.VolumeArea(mesh, c.Indices, mesh.GetVertex).x).OrderBy(v => v).ToList();
            Assert.True(vols[0] < 0);     // cavity
            Assert.True(vols[1] > 0 && vols[2] > 0);
        }

        [Fact]
        public void AutoRepair_WithPostRepairCleanup_GivesCleanClosedMesh()
        {
            // sphere with a hole, a separate two-sided triangle pair and a double cone with a bowtie apex
            DMesh3 mesh = Sphere();
            mesh.RemoveTriangle(mesh.TriangleIndices().First(), false, false);
            int a = mesh.AppendVertex(new Vector3d(5, 0, 0));
            int b = mesh.AppendVertex(new Vector3d(6, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(5, 1, 0));
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, b);
            DMesh3 cone = MeshTestUtil.DoubleConeBowtie(8, 1);
            MeshTransforms.Translate(cone, new Vector3d(-5, 0, 0));
            MeshEditor.Append(mesh, cone);
            Assert.False(mesh.IsClosed());

            var repair = new MeshAutoRepair(mesh) { PostRepairCleanup = true };
            Assert.True(repair.Apply());
            DMesh3 result = repair.Mesh;

            Assert.True(result.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(result));
            Assert.Equal(0, DegenerateCount(result));
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(result));
            Assert.True(Volume(result) > 0);
            Assert.True(result.CheckValidity(false, FailMode.ReturnOnly));
        }
    }
}
