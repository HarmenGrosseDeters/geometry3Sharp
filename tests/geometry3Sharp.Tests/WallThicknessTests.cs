using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Wall thickness (ray, shape diameter function, inscribed sphere) on solids with known thickness.
    /// </summary>
    public class WallThicknessTests
    {
        static DMesh3 Sphere(double r, int n, Vector3d center)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            return m;
        }

        // hollow ball: outer sphere R (outward normals) and inner sphere r (normals towards the center)
        static DMesh3 SphereShell(double R, double r, int n)
        {
            DMesh3 shell = Sphere(R, n, Vector3d.Zero);
            DMesh3 inner = Sphere(r, n, Vector3d.Zero);
            inner.ReverseOrientation();
            new MeshEditor(shell).AppendMesh(inner);
            return shell;
        }

        static DMesh3 Box(Vector3d center, Vector3d extent)
        {
            var gen = new GridBox3Generator() { Box = new Box3d(center, extent), EdgeVertices = 6 };
            return gen.Generate().MakeDMesh();
        }

        [Fact]
        public void SphereShell_RayAndInscribedSphere_GiveShellThickness()
        {
            const double R = 1, r = 0.8, T = R - r;
            DMesh3 shell = SphereShell(R, r, 24);
            Assert.Equal(0, MeshTestUtil.BoundaryEdgeCount(shell));
            foreach (var method in new[] { MeshWallThickness.Methods.Ray, MeshWallThickness.Methods.InscribedSphere }) {
                var wt = new MeshWallThickness(shell) { Method = method };
                Assert.True(wt.Compute());
                foreach (int vid in shell.VertexIndices()) {
                    double t = wt.VertexThickness[vid];
                    Assert.False(double.IsNaN(t), method + " vertex " + vid);
                    Assert.InRange(t, T - 0.01, T + 0.01);
                }
                foreach (int tid in shell.TriangleIndices())
                    Assert.InRange(wt.TriangleThickness[tid], T - 0.01, T + 0.01);
                Assert.InRange(wt.Statistics.Mean, T - 0.005, T + 0.005);
                Assert.True(wt.Statistics.StdDev < 0.005);
            }
        }

        [Fact]
        public void Cube_InscribedSphere_IsMedialAxisDistance()
        {
            // cube [-1,1]^3: the largest ball tangent to the top face at (x, y) has radius min(1-|x|, 1-|y|)
            // (it touches the side faces), while the inward ray always has length 2
            DMesh3 cube = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One), EdgeVertices = 21 }.Generate().MakeDMesh();
            var ins = new MeshWallThickness(cube) { Method = MeshWallThickness.Methods.InscribedSphere };
            var ray = new MeshWallThickness(cube) { Method = MeshWallThickness.Methods.Ray };
            Assert.True(ins.Compute() && ray.Compute());
            int checkedCount = 0;
            foreach (int tid in cube.TriangleIndices()) {
                Vector3d c = cube.GetTriCentroid(tid);
                Assert.Equal(2, ray.TriangleThickness[tid], 9);
                if (cube.GetTriNormal(tid).z < 0.99) continue;
                double expected = 2 * Math.Min(1 - Math.Abs(c.x), 1 - Math.Abs(c.y));
                if (expected < 0.6) continue;       // near the rim the ball reaches between the side face vertices
                Assert.InRange(ins.TriangleThickness[tid], expected - 0.02, expected + 0.02);
                checkedCount++;
            }
            Assert.True(checkedCount > 100);
            // never more than the ray
            foreach (int tid in cube.TriangleIndices())
                Assert.True(ins.TriangleThickness[tid] <= ray.TriangleThickness[tid] + 1e-9);
            foreach (int vid in cube.VertexIndices())
                Assert.True(ins.VertexThickness[vid] <= 2 + 1e-9);
        }

        [Fact]
        public void Slab_ShapeDiameter_BetweenThicknessAndConeBound()
        {
            // plate 20 x 20 x 1: away from the rim every SDF ray has length T / cos(angle), angle <= 60 deg
            const double T = 1;
            DMesh3 slab = Box(Vector3d.Zero, new Vector3d(10, 10, 0.5 * T));
            var wt = new MeshWallThickness(slab) { Method = MeshWallThickness.Methods.ShapeDiameter, ComputeVertices = false };
            Assert.True(wt.Compute());
            int checkedCount = 0;
            foreach (int tid in slab.TriangleIndices()) {
                Vector3d c = slab.GetTriCentroid(tid);
                if (Math.Abs(slab.GetTriNormal(tid).z) < 0.99 || Math.Abs(c.x) > 6 || Math.Abs(c.y) > 6) continue;
                double t = wt.TriangleThickness[tid];
                Assert.InRange(t, T, 2 * T);
                checkedCount++;
            }
            Assert.True(checkedCount > 4);

            // the inscribed sphere is exact there
            var ins = new MeshWallThickness(slab) { Method = MeshWallThickness.Methods.InscribedSphere, ComputeVertices = false };
            Assert.True(ins.Compute());
            foreach (int tid in slab.TriangleIndices()) {
                Vector3d c = slab.GetTriCentroid(tid);
                if (Math.Abs(slab.GetTriNormal(tid).z) < 0.99 || Math.Abs(c.x) > 6 || Math.Abs(c.y) > 6) continue;
                Assert.Equal(T, ins.TriangleThickness[tid], 6);
            }
        }

        [Fact]
        public void ShapeDiameter_ScalesWithMesh_AndIsUniformOnShell()
        {
            // inner radius above sin(60 deg): every cone ray hits the inner sphere (none grazes it)
            DMesh3 a = SphereShell(1, 0.9, 20);
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Scale(b, 2);     // exact in floating point
            var wa = new MeshWallThickness(a);
            var wb = new MeshWallThickness(b);
            Assert.True(wa.Compute() && wb.Compute());
            foreach (int tid in a.TriangleIndices())
                Assert.Equal(2 * wa.TriangleThickness[tid], wb.TriangleThickness[tid], 9);
            // rotationally symmetric part: (nearly) the same value everywhere, and above the wall thickness
            Assert.True(wa.Statistics.Min >= 0.1 - 0.005);
            Assert.True(wa.Statistics.StdDev < 0.05 * wa.Statistics.Mean);
        }

        [Fact]
        public void ThinFin_IsDetected()
        {
            // block 4 x 4 x 4 with a fin 0.1 thick sticking out of the top (separate closed parts are fine:
            // each ray only sees its own part)
            DMesh3 mesh = Box(Vector3d.Zero, new Vector3d(2, 2, 2));
            new MeshEditor(mesh).AppendMesh(Box(new Vector3d(0, 0, 3), new Vector3d(1.5, 0.05, 1)));
            foreach (var method in new[] { MeshWallThickness.Methods.Ray, MeshWallThickness.Methods.ShapeDiameter, MeshWallThickness.Methods.InscribedSphere }) {
                var wt = new MeshWallThickness(mesh) { Method = method };
                Assert.True(wt.Compute());
                var thin = wt.ThinTriangles(0.5);
                Assert.NotEmpty(thin);
                foreach (int tid in thin)
                    Assert.True(mesh.GetTriCentroid(tid).z > 1.99, method + ": only fin triangles are thin");
                // the large faces of the fin are thin
                foreach (int tid in mesh.TriangleIndices()) {
                    Vector3d c = mesh.GetTriCentroid(tid);
                    if (c.z > 2.5 && Math.Abs(mesh.GetTriNormal(tid).y) > 0.99 && Math.Abs(c.x) < 1)
                        Assert.True(wt.TriangleThickness[tid] < 0.5, method.ToString());
                }
            }
        }

        [Fact]
        public void OpenOrFlippedParts_AreNaN()
        {
            // a single open hemisphere-like sheet: rays either miss or hit front faces
            DMesh3 sphere = Sphere(1, 10, Vector3d.Zero);
            sphere.ReverseOrientation();     // inside-out: every inward ray leaves through a front face
            var wt = new MeshWallThickness(sphere) { Method = MeshWallThickness.Methods.Ray };
            Assert.True(wt.Compute());
            Assert.True(sphere.TriangleIndices().All(tid => double.IsNaN(wt.TriangleThickness[tid])));
            Assert.Equal(0, wt.Statistics.Count);
        }
    }
}
