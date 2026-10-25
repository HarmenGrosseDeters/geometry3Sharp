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
    }
}
