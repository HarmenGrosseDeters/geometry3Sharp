using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The options of Reducer.PreventFoldOver (BoundaryTriangleMinNormalDot, ReferenceNormalMinLength, off). The
    /// behaviour with the defaults is in ReducerFoldOverTests, which also compiles against upstream.
    /// </summary>
    public class ReducerFoldOverOptionsTests
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

        [Fact]
        public void BoundaryTriangleMinNormalDot_ControlsTheWalls()
        {
            // the walls: with only the sign test (BoundaryTriangleMinNormalDot 0) some stay, the default removes them;
            // without PreventFoldOver also folds stay
            int walls(DMesh3 m) => m.TriangleIndices().Count(t => m.GetTriNormal(t).z == 0);
            int signOnly = 0, byDefault = 0, off = 0;
            for (int seed = 1; seed <= 3; ++seed) {
                DMesh3 input = NoisyTerrain(seed);
                DMesh3 a = new DMesh3(input), b = new DMesh3(input), c = new DMesh3(input);
                new Reducer(a) { BoundaryTriangleMinNormalDot = 0 }.ReduceToTriangleCount(input.TriangleCount / 5);
                new Reducer(b).ReduceToTriangleCount(input.TriangleCount / 5);
                new Reducer(c) { PreventFoldOver = false }.ReduceToTriangleCount(input.TriangleCount / 5);
                signOnly += walls(a); byDefault += NotUp(b); off += NotUp(c);
            }
            Assert.True(signOnly > 0, "the sign test alone leaves walls on these inputs");
            Assert.Equal(0, byDefault);
            Assert.True(off > byDefault, "without PreventFoldOver the folds stay");
        }

        [Theory]
        [InlineData(0.005, 30)]
        [InlineData(0.0001, 40)]
        public void ThinPlate_ReachesTheTarget(double thickness, int edgeVertices)
        {
            // on the rim of a thin plate the absorbed normals of both sides cancel: the reference says nothing there and
            // the collapse is not rejected for it (otherwise the plate stops far above its target)
            DMesh3 plate = new GridBox3Generator() {
                Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, thickness)), EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            DMesh3 off = new DMesh3(plate);
            new Reducer(plate).ReduceToTriangleCount(12);
            new Reducer(off) { PreventFoldOver = false }.ReduceToTriangleCount(12);
            AssertValid(plate);
            Assert.True(plate.IsClosed());
            Assert.Equal(off.TriangleCount, plate.TriangleCount);
        }

        [Fact]
        public void BoxesTouchingAtAFace_CancellingNormalsAreNoReference()
        {
            // two closed boxes sharing the region of a face (PreventSelfIntersections keeps it): at the shared face the
            // absorbed normals of both boxes cancel. Taken as a reference anyway (ReferenceNormalMinLength 0), it blocks
            // collapses there (558 triangles instead of 540, the count without PreventFoldOver)
            DMesh3 a = new GridBox3Generator() { EdgeVertices = 10, Box = new Box3d(Vector3d.Zero, new Vector3d(10, 10, 10)) }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Translate(b, new Vector3d(20, 0, 0));
            new MeshEditor(a).AppendMesh(b);
            DMesh3 guarded = new DMesh3(a), unguarded = new DMesh3(a), off = new DMesh3(a);
            new Reducer(guarded) { PreventSelfIntersections = true }.ReduceToTriangleCount(194);
            new Reducer(unguarded) { PreventSelfIntersections = true, ReferenceNormalMinLength = 0 }.ReduceToTriangleCount(194);
            new Reducer(off) { PreventSelfIntersections = true, PreventFoldOver = false }.ReduceToTriangleCount(194);
            Assert.Equal(off.TriangleCount, guarded.TriangleCount);
            Assert.True(unguarded.TriangleCount > guarded.TriangleCount, $"{unguarded.TriangleCount} vs {guarded.TriangleCount}");
        }
    }
}
