using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Refinement for hidden gaps and thin walls (narrower than a cell): resolved until the result has no
    /// self-intersections and no lost material; creases where surfaces meet are not refined. From the
    /// quality review of the contouring.
    /// </summary>
    public class HiddenGapRefinementTests
    {
        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
        }

        // exact self-intersections between triangles that share no vertex
        static int SelfIntersections(DMesh3 m)
        {
            var tree = new DMeshAABBTree3(m, true);
            int count = 0;
            foreach (int ta in m.TriangleIndices()) {
                Index3i A = m.GetTriangle(ta);
                var TA = new Triangle3d(m.GetVertex(A.a), m.GetVertex(A.b), m.GetVertex(A.c));
                var box = new AxisAlignedBox3d(TA.V0); box.Contain(TA.V1); box.Contain(TA.V2);
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (b, d) => b.Intersects(box),
                    NextTriangleF = (tb) => {
                        if (tb <= ta) return;
                        Index3i B = m.GetTriangle(tb);
                        if (new[] { A.a, A.b, A.c }.Intersect(new[] { B.a, B.b, B.c }).Any()) return;
                        var TB = new Triangle3d(m.GetVertex(B.a), m.GetVertex(B.b), m.GetVertex(B.c));
                        if (ExactTriangleIntersection.Intersects(ref TA, ref TB)) count++;
                    }
                });
            }
            return count;
        }

        // two 5 x 2.5 x 5 boxes with a gap, rotated off the grid axes (as DualContouringTests.BoxesWithGap);
        // optionally the second box is tilted by tiltDeg about the x axis (the gap is then a 2-degree... wedge)
        static DMesh3 BoxesWithGap(double gap, double tiltDeg)
        {
            DMesh3 a = Box(new Vector3d(-2.5, -1.25, -2.5), new Vector3d(2.5, 1.25, 2.5));
            DMesh3 b = Box(new Vector3d(-2.5, 1.25 + gap, -2.5), new Vector3d(2.5, 3.75 + gap, 2.5));
            MeshTransforms.Rotate(b, new Vector3d(0, 1.25 + gap, 0), Quaterniond.AxisAngleD(Vector3d.AxisX, tiltDeg));
            MeshEditor.Append(a, b);
            MeshTransforms.Rotate(a, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 27));
            return a;
        }

        // the hidden gap edge count grows ~4x per level while a gap is still hidden: refinement must go on
        [Theory]
        [InlineData(0.3)]
        [InlineData(0.2)]
        public void NarrowParallelGap_IsRefinedUntilResolved(double gapCells)
        {
            double cell = 0.25;
            var s = new MeshWindingNumberSolidify(BoxesWithGap(gapCells * cell, 0)) { CellSize = cell, Adaptive = true, ReduceTolerance = 0, MaxRefineLevels = 3 };
            Assert.True(s.Compute());
            Assert.Equal(0, s.HiddenGapEdges);
            Assert.Equal(0, SelfIntersections(s.Result));
            Assert.Equal(2, Components(s.Result));
        }

        // nearly parallel faces (0.5 degrees): their planes meet far outside the parts, it is a gap
        [Fact]
        public void NearlyParallelGap_IsDetected()
        {
            double cell = 0.25;
            var s = new MeshWindingNumberSolidify(BoxesWithGap(0.45 * cell, 0.5)) { CellSize = cell, Adaptive = true, ReduceTolerance = 0, MaxRefineLevels = 3 };
            Assert.True(s.Compute());
            Assert.True(s.UsedCellSize < cell);
            Assert.Equal(0, SelfIntersections(s.Result));
            Assert.Equal(2, Components(s.Result));
        }

        static int Components(DMesh3 m)
        {
            var c = new MeshConnectedComponents(m);
            c.FindConnectedT();
            return c.Count;
        }

        static double Volume(DMesh3 m)
        {
            Vector3d c = m.CachedBounds.Center;
            return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), (v) => m.GetVertex(v) - c).x;
        }

        // a plate thinner than a cell (rotated off the grid axes): no grid node inside, without refinement
        // it vanishes or breaks up
        [Theory]
        [InlineData(0.3)]
        [InlineData(0.6)]
        [InlineData(0.9)]
        public void ThinPlate_IsRefinedAndKept(double thicknessCells)
        {
            double cell = 0.25, t = thicknessCells * cell;
            DMesh3 plate = Box(new Vector3d(-2.5, -t / 2, -2.5), new Vector3d(2.5, t / 2, 2.5));
            MeshTransforms.Rotate(plate, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 27));
            var s = new MeshWindingNumberSolidify(plate) { CellSize = cell, Adaptive = true, MaxRefineLevels = 3 };
            Assert.True(s.Compute());
            Assert.True(s.UsedCellSize < cell);
            Assert.Equal(0, s.HiddenWallEdges);
            Assert.Equal(1, Components(s.Result));
            Assert.True(s.Result.IsClosed());
            Assert.InRange(Volume(s.Result), 0.97 * 25 * t, 1.03 * 25 * t);
            Assert.Equal(0, SelfIntersections(s.Result));
        }

        // concentric spheres (a ball in a hollow sphere) with a curved gap of 0.6 cells, differently tessellated
        [Fact]
        public void CurvedGap_IsDetectedAndResolved()
        {
            double cell = 0.2;
            DMesh3 ball = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 16 }.Generate().MakeDMesh();
            DMesh3 shell = new Sphere3Generator_NormalizedCube() { Radius = 1 + 0.6 * cell, EdgeVertices = 21 }.Generate().MakeDMesh();
            DMesh3 outer = new Sphere3Generator_NormalizedCube() { Radius = 1.6, EdgeVertices = 16 }.Generate().MakeDMesh();
            shell.ReverseOrientation();
            MeshEditor.Append(outer, shell);
            MeshEditor.Append(outer, ball);
            var s = new MeshWindingNumberSolidify(outer) { CellSize = cell, Adaptive = true, MaxRefineLevels = 3 };
            Assert.True(s.Compute());
            Assert.True(s.UsedCellSize < cell);
            Assert.Equal(3, Components(s.Result));       // the ball, the cavity and the outside of the hollow sphere
            Assert.Equal(0, SelfIntersections(s.Result));
        }

        // the crease where two inputs meet at a small angle is not a gap (refining would not change it)
        [Fact]
        public void BooleanCrease_NotRefined()
        {
            DMesh3 a = Box(Vector3d.Zero, new Vector3d(20, 10, 2));
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Rotate(b, new Vector3d(10, 5, 1), Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 5));
            MeshTransforms.Translate(b, new Vector3d(5, 2.5, 0.5));
            var u = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { CellSize = 0.2 };
            Assert.True(u.Compute());
            Assert.Equal(0.2, u.UsedCellSize);
            Assert.True(u.Result.IsClosed());
        }
    }
}
