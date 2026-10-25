using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Special cases of the winding number solidify / booleans and the post-repair cleanup: open meshes
    /// away from the origin, empty inputs, UTM coordinates, too small cells, identical inputs, inverted
    /// hollow solids.
    /// </summary>
    public class WindingEdgeCaseTests
    {
        static DMesh3 Box(double size, Vector3d center)
        {
            var box = new Box3d(center, 0.5 * size * Vector3d.One);
            return new TrivialBox3Generator() { Box = box }.Generate().MakeDMesh();
        }

        static double Volume(DMesh3 m)
        {
            Vector3d c = m.CachedBounds.Center;
            return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), (v) => m.GetVertex(v) - c).x;
        }

        static void RemoveTriangles(DMesh3 m, Func<Vector3d, bool> pred)
        {
            foreach (int tid in m.TriangleIndices().ToArray())
                if (pred(m.GetTriCentroid(tid))) m.RemoveTriangle(tid);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Solidify_OpenBoxAwayFromTheOrigin_IsNotTurnedInsideOut(bool adaptive)
        {
            // open in +x, centred at x = 10: its volume relative to the origin is negative
            DMesh3 box = Box(10, new Vector3d(10, 0, 0));
            RemoveTriangles(box, (c) => c.x > 14.99);
            var s = new MeshWindingNumberSolidify(box) { Adaptive = adaptive, GridResolution = 64 };
            Assert.True(s.Compute());
            Assert.True(s.Result.IsClosed());
            Assert.InRange(Volume(s.Result), 950, 1050);
        }

        [Fact]
        public void Solidify_Dense_UtmCoordinates_IsAccurate()
        {
            Vector3d utm = new Vector3d(500000, 5500000, 120);
            DMesh3 box = Box(10, utm);
            var s = new MeshWindingNumberSolidify(box) { CellSize = 0.25 };
            Assert.True(s.Compute());
            Assert.InRange(Volume(s.Result), 995, 1005);
            // the corners are reproduced (sharp features from the Hermite data)
            foreach (Vector3d corner in new[] { utm + new Vector3d(5, 5, 5), utm - new Vector3d(5, 5, 5) })
                Assert.True(s.Result.VertexIndices().Min(v => s.Result.GetVertex(v).Distance(corner)) < 0.01, $"corner {corner}");
        }

        [Theory]
        [InlineData(MeshWindingNumberBoolean.Operations.Union, 1000)]
        [InlineData(MeshWindingNumberBoolean.Operations.Difference, 1000)]
        [InlineData(MeshWindingNumberBoolean.Operations.Intersection, 0)]
        public void Boolean_WithEmptyMesh_AtUtm(MeshWindingNumberBoolean.Operations op, double expected)
        {
            DMesh3 a = Box(10, new Vector3d(500000, 5000000, 100));
            var b = new MeshWindingNumberBoolean(a, new DMesh3(), op) { GridResolution = 64 };
            Assert.True(b.Compute());
            if (expected == 0)
                Assert.Equal(0, b.Result.TriangleCount);
            else
                Assert.InRange(Volume(b.Result), 0.99 * expected, 1.01 * expected);
            // empty A
            var c = new MeshWindingNumberBoolean(new DMesh3(), a, op) { GridResolution = 64 };
            Assert.True(c.Compute());
            if (op == MeshWindingNumberBoolean.Operations.Union)
                Assert.InRange(Volume(c.Result), 990, 1010);
            else
                Assert.Equal(0, c.Result.TriangleCount);
        }

        [Fact]
        public void TooSmallCells_FailInsteadOfThrowing()
        {
            DMesh3 box = Box(1000, Vector3d.Zero);
            Assert.False(new MeshWindingNumberSolidify(box) { CellSize = 1e-6, Adaptive = true }.Compute());
            Assert.False(new MeshWindingNumberSolidify(box) { CellSize = 1e-3 }.Compute());
            Assert.False(new MeshWindingNumberBoolean(box, Box(10, Vector3d.Zero), MeshWindingNumberBoolean.Operations.Union) { CellSize = 1e-6 }.Compute());
            var octree = new WindingNumberOctree(box, new DMeshAABBTree3(box, true)) { MaxDepth = 2 };
            Assert.Throws<ArgumentException>(() => octree.Build());
        }

        [Fact]
        public void Boolean_IdenticalInputs()
        {
            DMesh3 a = Box(10, new Vector3d(3, 4, 5));
            var diff = new MeshWindingNumberBoolean(a, new DMesh3(a), MeshWindingNumberBoolean.Operations.Difference) { GridResolution = 64 };
            Assert.True(diff.Compute());
            Assert.Equal(0, diff.Result.TriangleCount);
            foreach (var op in new[] { MeshWindingNumberBoolean.Operations.Union, MeshWindingNumberBoolean.Operations.Intersection }) {
                var b = new MeshWindingNumberBoolean(a, new DMesh3(a), op) { GridResolution = 64 };
                Assert.True(b.Compute());
                Assert.True(b.Result.IsClosed());
                Assert.InRange(Volume(b.Result), 995, 1005);
            }
        }

        [Fact]
        public void Boolean_StackedOpenLayers_IsTheIsoSurface()
        {
            // two parallel same-oriented squares (an open soup): w > 0.5 also beside and around them, so the
            // sparse grid grows past the octree padding (node keys must stay distinct there)
            DMesh3 soup = new DMesh3();
            foreach (double z in new[] { 0.0, 1.0 }) {
                int a = soup.AppendVertex(new Vector3d(0, 0, z)), b = soup.AppendVertex(new Vector3d(10, 0, z));
                int c = soup.AppendVertex(new Vector3d(10, 10, z)), d = soup.AppendVertex(new Vector3d(0, 10, z));
                soup.AppendTriangle(a, b, c); soup.AppendTriangle(a, c, d);
            }
            DMesh3 small = Box(2, new Vector3d(20, 5, 0.5));
            var u = new MeshWindingNumberBoolean(soup, small, MeshWindingNumberBoolean.Operations.Union) { GridResolution = 64 };
            Assert.True(u.Compute());
            Assert.True(u.Result.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(u.Result.TriangleCount > 0);
            var ts = new DMeshAABBTree3(soup, true);
            var tb = new DMeshAABBTree3(small, true);
            foreach (int v in u.Result.VertexIndices()) {
                Vector3d p = u.Result.GetVertex(v);
                // on an input surface (its corners have w = 1/8), or on the iso-surface w = 0.5
                double d = Math.Min(Math.Sqrt(MeshQueries.TriDistanceSqr(soup, ts.FindNearestTriangle(p), p)),
                                    Math.Sqrt(MeshQueries.TriDistanceSqr(small, tb.FindNearestTriangle(p), p)));
                double w = Math.Max(ts.WindingNumber(p), tb.WindingNumber(p));
                Assert.True(d < 0.01 * u.UsedCellSize || Math.Abs(w - 0.5) < 0.15, $"{p}: w = {w}, distance {d}");
            }
        }

        static DMesh3 Sphere(double radius)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = 10 }.Generate().MakeDMesh();
            return m;
        }

        [Fact]
        public void Cleanup_InvertedHollowSolid_IsFixedCompletely()
        {
            DMesh3 outer = Sphere(2);
            DMesh3 cavity = Sphere(1);
            cavity.ReverseOrientation();
            MeshEditor.Append(outer, cavity);
            double expected = Volume(outer);
            Assert.True(expected > 0);
            outer.ReverseOrientation();       // the whole hollow solid inside out
            Assert.Equal(2, MeshPostRepairCleanup.FindInvertedComponents(outer).Count);
            var cleanup = new MeshPostRepairCleanup(outer);
            cleanup.Apply();
            Assert.InRange(Volume(outer), 0.999 * expected, 1.001 * expected);
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(outer));
        }

        [Fact]
        public void Cleanup_InvertedCubeTouchingAnotherOne_IsFlipped()
        {
            // [0,1]^3 outwards and [1,2]x[0,1]^2 inverted, sharing the face x = 1 (not its vertices)
            DMesh3 a = Box(1, new Vector3d(0.5, 0.5, 0.5));
            DMesh3 b = Box(1, new Vector3d(1.5, 0.5, 0.5));
            b.ReverseOrientation();
            MeshEditor.Append(a, b);
            var inverted = MeshPostRepairCleanup.FindInvertedComponents(a);
            Assert.Single(inverted);
        }
    }
}
