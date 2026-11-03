using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.PreventFoldOver (finding S1, docs/ISSUE_CASE_FINDINGS.md): the new triangles of a collapse are checked
    /// against the input normals their corners have absorbed, not only against the current triangles, so chains of
    /// collapses do not fold a height field over, and no triangle stands up as a wall along a straight border.
    /// </summary>
    public class ReducerFoldOverTests
    {
        /// <summary> an open height field over [0,nx] x [0,ny] (unit cells, two triangles each, normals +z) </summary>
        static DMesh3 Grid(int nx, int ny, Func<int, int, double> z)
        {
            var m = new DMesh3();
            int[,] vid = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    vid[i, j] = m.AppendVertex(new Vector3d(i, j, z(i, j)));
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    m.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    m.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return m;
        }

        /// <summary> x rounded to a multiple of 2^-30 (the same input on net48 and net10 despite Math.Sin) </summary>
        static double R(double x) => Math.Round(x * (1 << 30)) / (1 << 30);

        // a 40 x 40 hill (amplitude 3) with noise of +-noise/2, all normals up
        static DMesh3 NoisyTerrain(int seed, double noise = 0.8)
        {
            var rnd = new Random(seed);
            return Grid(40, 40, (i, j) => R(3 * Math.Sin(0.2 * i) * Math.Cos(0.15 * j) + noise * (rnd.NextDouble() - 0.5)));
        }

        /// <summary> triangles whose normal does not point up: folded over, or standing on edge (a wall) </summary>
        static int NotUp(DMesh3 m) => m.TriangleIndices().Count(t => !(m.GetTriNormal(t).z > 0));

        static void AssertValid(DMesh3 m)
        {
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(Invariance.AllFinite(m));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void NoisyTerrain_ReducedTo20Percent_NoTriangleTurnsDown(int seed)
        {
            // https://github.com/zeux/meshoptimizer/issues/346: a chain of collapses, each passing the 90 degree flip test
            // against the current triangle, turned triangles of a terrain over (1, 4 and 2 of them for these seeds), and
            // collapses of interior vertices onto the border left vertical walls along it
            DMesh3 input = NoisyTerrain(seed);
            Assert.Equal(0, NotUp(input));
            DMesh3 m = new DMesh3(input);
            new Reducer(m).ReduceToTriangleCount(input.TriangleCount / 5);
            AssertValid(m);
            Assert.True(m.TriangleCount <= input.TriangleCount / 5 + 2, $"{m.TriangleCount} triangles");
            Assert.Equal(0, NotUp(m));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void SmallPatch_ReducedTo10_NoWallOnTheBorder(int seed)
        {
            // https://github.com/zeux/meshoptimizer/issues/699 setting: a 4 x 3 grid with random heights (24 triangles)
            // reduced to 10. With seed 2, a triangle on three consecutive border vertices of y = 0 stood vertical
            // (the reference of the border vertices tilts towards the border, so a sign test passes it)
            var rnd = new Random(seed);
            DMesh3 input = Grid(4, 3, (i, j) => 0.3 * rnd.NextDouble());
            DMesh3 m = new DMesh3(input);
            new Reducer(m).ReduceToTriangleCount(10);
            AssertValid(m);
            Assert.True(m.TriangleCount <= 10, $"{m.TriangleCount} triangles");
            Assert.Equal(0, NotUp(m));
            foreach (int corner in new[] { 0, 4, 15, 19 })
                Assert.True(m.IsVertex(corner) && m.GetVertex(corner) == input.GetVertex(corner), $"corner {corner}");
        }

        [Fact]
        public void SharpShapes_ReachTheTarget()
        {
            // creases of 90 degrees (box, cylinder caps) and a smooth closed surface lose no reach
            DMesh3 box = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 2, 3)), EdgeVertices = 20 }.Generate().MakeDMesh();
            DMesh3 rod = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.01, 0.01)), EdgeVertices = 30 }.Generate().MakeDMesh();
            DMesh3 cyl = new CappedCylinderGenerator() { BaseRadius = 1, TopRadius = 1, Height = 3, Slices = 64 }.Generate().MakeDMesh();
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { EdgeVertices = 30, Radius = 1 }.Generate().MakeDMesh();
            foreach (var (mesh, target) in new[] { (box, 12), (rod, 12), (cyl, 20), (sphere, 20) }) {
                new Reducer(mesh).ReduceToTriangleCount(target);
                AssertValid(mesh);
                Assert.True(mesh.IsClosed());
                Assert.Equal(target, mesh.TriangleCount);
            }
        }

        [Fact]
        public void NoisyTerrain_InEveryFrame()
        {
            // the test compares unit vectors (scaled by their largest coordinate first): the 2^k scales and exact
            // translations give the same reduction bit for bit, the others no fold either (in the mesh mapped back)
            DMesh3 input = NoisyTerrain(2);
            DMesh3 reference = new DMesh3(input);
            new Reducer(reference).ReduceToTriangleCount(input.TriangleCount / 5);
            Assert.Equal(0, NotUp(reference));
            foreach (Similarity s in Similarity.All()) {
                DMesh3 mesh = Invariance.Transformed(input, s);
                new Reducer(mesh).ReduceToTriangleCount(input.TriangleCount / 5);
                DMesh3 back = Invariance.MappedBack(mesh, s);
                AssertValid(back);
                Assert.True(NotUp(back) == 0, $"{s.Name}: {NotUp(back)} triangles not up");
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference, back));
            }
        }

        [Fact]
        public void RepeatedReductions_NoFold_AndRepeatedRunsAgree()
        {
            // reduced in steps with one instance (each step takes the references from its start): no fold either, and
            // two runs give the same mesh
            DMesh3 first = null;
            for (int run = 0; run < 2; ++run) {
                DMesh3 m = NoisyTerrain(2);
                var reducer = new Reducer(m);
                foreach (int target in new[] { 2400, 1200, 640 }) {
                    reducer.ReduceToTriangleCount(target);
                    Assert.Equal(0, NotUp(m));
                }
                AssertValid(m);
                if (first == null) first = m;
                else Assert.Equal("", Invariance.Difference(first, m));
            }
        }

        [Fact]
        public void MeshChangedBetweenReductions_ReferenceTakenAgain()
        {
            // a reducer used on a mesh that was changed in between (here: flipped upside down) uses the references of
            // the changed mesh: the old ones would point the wrong way and block every collapse
            DMesh3 m = NoisyTerrain(1);
            var reducer = new Reducer(m);
            reducer.ReduceToTriangleCount(2400);
            m.ReverseOrientation();
            reducer.ReduceToTriangleCount(640);
            AssertValid(m);
            Assert.True(m.TriangleCount <= 642, $"{m.TriangleCount} triangles");
            Assert.Equal(0, m.TriangleIndices().Count(t => !(m.GetTriNormal(t).z < 0)));
        }
    }
}
