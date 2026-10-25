using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Repair with the solidify fallback: clean inputs keep their triangles, unclean results (also only
    /// self-intersecting ones) become a clean solid.
    /// </summary>
    public class MeshRobustRepairTests
    {
        static DMesh3 Sphere(double r, Vector3d c)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = 12 }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        [Fact]
        public void CleanInput_KeepsItsTriangles()
        {
            DMesh3 m = Sphere(1, Vector3d.Zero);
            int n = m.TriangleCount;
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.False(r.UsedSolidify);
            Assert.Equal("", r.FallbackReason);
            Assert.Equal(n, r.Mesh.TriangleCount);
        }

        [Fact]
        public void SelfIntersectingInput_SolidifiedClean()
        {
            // two overlapping spheres: closed and manifold, but self-intersecting (the repair keeps that)
            DMesh3 m = Sphere(1, Vector3d.Zero);
            MeshEditor.Append(m, Sphere(1, new Vector3d(1.2, 0, 0)));
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.True(r.UsedSolidify);
            Assert.Equal("self-intersections", r.FallbackReason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            // the union of the spheres
            double v = MeshMeasurements.VolumeArea(r.Mesh, r.Mesh.TriangleIndices(), r.Mesh.GetVertex).x;
            double h = (2 - 1.2) / 2, cap = Math.PI * h * h * (3 - h) / 3;   // each sphere's cap inside the other
            double expected = 2 * 4.0 / 3 * Math.PI - 2 * cap;
            Assert.InRange(v, 0.97 * expected, 1.03 * expected);
        }

        [Fact]
        public void WithoutTheSelfIntersectionCheck_KeptAsRepaired()
        {
            DMesh3 m = Sphere(1, Vector3d.Zero);
            MeshEditor.Append(m, Sphere(1, new Vector3d(1.2, 0, 0)));
            var r = new MeshRobustRepair(m) { CheckSelfIntersections = false };
            Assert.True(r.Apply());
            Assert.False(r.UsedSolidify);
        }

        [Fact]
        public void OpenMesh_ClosedBySolidifyOrRepair()
        {
            DMesh3 m = Sphere(1, Vector3d.Zero);
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).z > 0.9) m.RemoveTriangle(t);
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
        }

        [Fact]
        public void CleanButVolumeLost_InputSolidified()
        {
            // input: two overlapping boxes (a region enclosed twice); a repair stage that returns only one of
            // them gives a clean mesh that lost a third of the solid: the input is solidified instead
            DMesh3 a = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            DMesh3 b = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(1, 0, 0), Vector3d.One) }.Generate().MakeDMesh();
            DMesh3 input = new DMesh3(a);
            MeshEditor.Append(input, b);
            var r = new MeshRobustRepair(new DMesh3(input)) { RepairF = (m) => new DMesh3(a) };
            Assert.True(r.Apply());
            Assert.True(r.UsedSolidify);
            Assert.Equal("volume differs from the input", r.FallbackReason);
            Assert.InRange(r.RepairVolumeIoU, 0.6, 0.7);
            double missing, extra;
            Assert.True(MeshRepairMetrics.VolumeIoU(input, r.Mesh, out missing, out extra) > 0.99);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            // the same repair returning the whole solid is kept
            DMesh3 union = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1)) }.Generate().MakeDMesh();
            var r2 = new MeshRobustRepair(new DMesh3(input)) { RepairF = (m) => new DMesh3(union) };
            Assert.True(r2.Apply());
            Assert.False(r2.UsedSolidify);
            Assert.True(r2.RepairVolumeIoU > 0.99);
        }
    
        [Fact]
        public void InsideOutInput_RepairKeptNotSolidified()
        {
            // two disjoint spheres, one inside out: the repair flips it, the result is the solid of both
            // (with the raw winding number as reference only one sphere was the input solid: IoU 0.5, and
            // the good repair was replaced by a solidify)
            DMesh3 m = Sphere(1, Vector3d.Zero);
            DMesh3 inv = Sphere(1, new Vector3d(3, 0, 0));
            inv.ReverseOrientation(false);
            MeshEditor.Append(m, inv);
            int n = m.TriangleCount;
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.False(r.UsedSolidify, r.FallbackReason);
            Assert.True(r.RepairVolumeIoU > 0.99, $"IoU {r.RepairVolumeIoU}");
            Assert.Equal(n, r.Mesh.TriangleCount);
            Assert.True(MeshMeasurements.VolumeArea(r.Mesh, r.Mesh.TriangleIndices(), r.Mesh.GetVertex).x > 0);
        }
}
}
