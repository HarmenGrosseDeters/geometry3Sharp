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

        [Theory]
        [InlineData(2)]     // two triangles per face: the ball has to find the side faces between their vertices
        [InlineData(21)]
        public void Cube_InscribedSphere_IsMedialAxisDistance(int edgeVertices)
        {
            // cube [-1,1]^3: the largest ball tangent to the top face at (x, y) has radius min(1-|x|, 1-|y|)
            // (it touches the side faces), while the inward ray always has length 2
            DMesh3 cube = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One), EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            var ins = new MeshWallThickness(cube) { Method = MeshWallThickness.Methods.InscribedSphere };
            var ray = new MeshWallThickness(cube) { Method = MeshWallThickness.Methods.Ray };
            Assert.True(ins.Compute() && ray.Compute());
            foreach (int tid in cube.TriangleIndices()) {
                Vector3d c = cube.GetTriCentroid(tid);
                Assert.Equal(2, ray.TriangleThickness[tid], 9);
                Vector3d nrm = cube.GetTriNormal(tid);
                // by symmetry, the two coordinates across the face's normal
                Vector3d a = new Vector3d(Math.Abs(c.x), Math.Abs(c.y), Math.Abs(c.z));
                double u = (Math.Abs(nrm.x) > 0.5) ? a.y : a.x, v = (Math.Abs(nrm.z) > 0.5) ? a.y : a.z;
                double expected = 2 * Math.Min(1 - u, 1 - v);
                Assert.Equal(expected, ins.TriangleThickness[tid], 9);
            }
            foreach (int vid in cube.VertexIndices())
                Assert.True(ins.VertexThickness[vid] <= 2 + 1e-9);
        }

        [Fact]
        public void Ray_ThroughOppositeVertices_IsWatertight()
        {
            // plate whose top and bottom grids are aligned: every vertex ray of the top face passes exactly
            // through a vertex or an edge of the bottom face
            DMesh3 slab = Box(Vector3d.Zero, new Vector3d(1, 1, 0.25));
            var wt = new MeshWallThickness(slab) { Method = MeshWallThickness.Methods.Ray };
            Assert.True(wt.Compute());
            int n = 0;
            foreach (int vid in slab.VertexIndices()) {
                Vector3d p = slab.GetVertex(vid);
                if (Math.Abs(p.x) > 0.99 || Math.Abs(p.y) > 0.99) continue;      // rim: averaged normals
                Assert.Equal(0.5, wt.VertexThickness[vid], 9);
                n++;
            }
            Assert.True(n >= 16);
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
        public void Results_DoNotDependOnTheUnit()
        {
            // the same shell in a small unit (eg meters for a mm part): the library's triangle normals treat
            // triangles below an absolute size as degenerate; the analysis must not (found on Thingi10K)
            const double s = 1e-4;
            DMesh3 a = SphereShell(1, 0.8, 16);
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Scale(b, s);
            foreach (var method in new[] { MeshWallThickness.Methods.Ray, MeshWallThickness.Methods.InscribedSphere, MeshWallThickness.Methods.ShapeDiameter }) {
                var wa = new MeshWallThickness(a) { Method = method };
                var wb = new MeshWallThickness(b) { Method = method };
                Assert.True(wa.Compute() && wb.Compute());
                foreach (int tid in a.TriangleIndices())
                    Assert.Equal(wa.TriangleThickness[tid], wb.TriangleThickness[tid] / s, 6);
                foreach (int vid in a.VertexIndices())
                    Assert.Equal(wa.VertexThickness[vid], wb.VertexThickness[vid] / s, 6);
            }
        }

        [Theory]
        [InlineData(1.0, 0.0)]
        [InlineData(0.001, 0.0)]        // mm part in meters: the scale is not exact in binary
        [InlineData(0.1, 0.0)]
        [InlineData(1.0, 5000000.0)]    // geo coordinates
        public void TouchingParts_RayStopsAtOwnWall(double scale, double offset)
        {
            // two cubes sharing the face x = 1 (coincident, opposite orientation): the ray from the left cube
            // hits both at the same t up to rounding and must end at the left cube's own wall, not NaN
            DMesh3 mesh = Box(new Vector3d(0.5, 0.5, 0.5), 0.5 * Vector3d.One);
            new MeshEditor(mesh).AppendMesh(Box(new Vector3d(1.5, 0.5, 0.5), 0.5 * Vector3d.One));
            MeshTransforms.Scale(mesh, scale);
            MeshTransforms.Translate(mesh, new Vector3d(offset, offset, 0));
            var wt = new MeshWallThickness(mesh) { Method = MeshWallThickness.Methods.Ray, ComputeVertices = false };
            Assert.True(wt.Compute());
            foreach (int tid in mesh.TriangleIndices()) {
                Vector3d n = mesh.GetTriNormal(tid);
                if (Math.Abs(n.x) < 0.99) continue;
                Assert.False(double.IsNaN(wt.TriangleThickness[tid]), "triangle " + tid);
                Assert.Equal(1.0, wt.TriangleThickness[tid] / scale, 6);
            }
        }

        [Fact]
        public void InsideOutParts_AreTakenFlipped()
        {
            // Thingi10K has closed meshes that are inside out as a whole: their rays all leave through front
            // faces. With OrientInvertedParts (default) they are measured as if oriented outward.
            DMesh3 sphere = Sphere(1, 10, Vector3d.Zero);
            sphere.ReverseOrientation();
            var raw = new MeshWallThickness(sphere) { Method = MeshWallThickness.Methods.Ray, OrientInvertedParts = false };
            Assert.True(raw.Compute());
            Assert.True(sphere.TriangleIndices().All(tid => double.IsNaN(raw.TriangleThickness[tid])));
            Assert.Equal(0, raw.Statistics.Count);
            var wt = new MeshWallThickness(sphere) { Method = MeshWallThickness.Methods.Ray };
            Assert.True(wt.Compute());
            Assert.True(sphere.TriangleIndices().All(tid => wt.TriangleThickness[tid] > 1.9 && wt.TriangleThickness[tid] < 2));

            // a hollow ball inside out as a whole (outer shell inverted, cavity outward): same as the correct one;
            // a correctly oriented cavity is not flipped
            foreach (bool invert in new[] { false, true }) {
                DMesh3 shell = SphereShell(1, 0.8, 16);
                if (invert) shell.ReverseOrientation();
                var s = new MeshWallThickness(shell) { Method = MeshWallThickness.Methods.InscribedSphere };
                Assert.True(s.Compute());
                foreach (int tid in shell.TriangleIndices())
                    Assert.InRange(s.TriangleThickness[tid], 0.19, 0.21);
            }

            // an inverted part next to a correct one: only that part is flipped
            DMesh3 two = Sphere(1, 10, Vector3d.Zero);
            DMesh3 bad = Sphere(0.5, 10, new Vector3d(3, 0, 0));
            bad.ReverseOrientation();
            new MeshEditor(two).AppendMesh(bad);
            var wt2 = new MeshWallThickness(two) { Method = MeshWallThickness.Methods.Ray };
            Assert.True(wt2.Compute());
            foreach (int tid in two.TriangleIndices()) {
                double expected = (two.GetTriCentroid(tid).x > 1.5) ? 1 : 2;
                Assert.InRange(wt2.TriangleThickness[tid], expected * 0.95, expected);
            }
        }
    }
}
