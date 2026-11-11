using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Example tests for lines of MeshRobustRepair, MeshRobustRepairPassThrough, MeshRepairMetrics and MeshWindingNumberSolidify that no other test executed
    /// (coverage run on 1938caa): the error paths of the pass through stage, the frame scale of the lengths a caller sets, the guards of the scale
    /// helpers, and FinishPostProcess of a scaled mesh.
    /// </summary>
    public class RepairCoverageTests
    {
        static DMesh3 Sphere(double x, double radius, int edgeVertices = 6) => RobustRepairFrameTests.Sphere(new Vector3d(x, 0, 0), radius, edgeVertices);

        static DMesh3 WithHole(DMesh3 m)
        {
            foreach (int t in m.TriangleIndices().Take(8).ToList())
                m.RemoveTriangle(t, true, false);
            return m;
        }

        static DMesh3 Union(params DMesh3[] parts)
        {
            var m = new DMesh3();
            foreach (var p in parts) MeshEditor.Append(m, p);
            return m;
        }

        static DMesh3 Scaled(DMesh3 m, int shift)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, Math.Pow(2, shift) * c.GetVertex(v));
            return c;
        }

        static HashSet<Vector3d> Vertices(DMesh3 m) => new HashSet<Vector3d>(m.VertexIndices().Select(v => m.GetVertex(v)));

        // a clean sphere and an open one apart: the pass through stage splits them
        static DMesh3 CleanAndOpen() => Union(Sphere(0, 1), WithHole(Sphere(8, 0.5, 5)));

        // ---- MeshRobustRepair.cs:463: an exception in the repair of the others leaves the instance with the mesh it was given

        [Fact]
        public void PassThrough_ExceptionInTheRepairOfTheOthers_MeshIsTheInput()
        {
            DMesh3 input = CleanAndOpen();
            DMesh3 before = new DMesh3(input);
            // (a callback that throws on the probe (an empty mesh) is not framed; it throws on the real repair of the others too)
            var r = new MeshRobustRepair(input) { SolidifyResolution = 32, ConfigureRepair = (rep) => { throw new InvalidOperationException("boom"); } };
            var e = Assert.Throws<InvalidOperationException>(() => r.Apply());
            Assert.Equal("boom", e.Message);
            Assert.Same(input, r.Mesh);                       // (not the mesh of the others, which the stage had put there)
            Assert.Equal("", Invariance.Difference(before, r.Mesh));
            Assert.Equal(0, r.PassedThroughTriangles);
        }

        // ---- MeshRobustRepair.cs:476: the repair of the others is cancelled: Apply() is false and Mesh is the input, not the mesh of the others

        [Fact]
        public void PassThrough_CancelledRepairOfTheOthers_MeshIsTheInput()
        {
            DMesh3 input = CleanAndOpen();
            DMesh3 before = new DMesh3(input);
            var r = new MeshRobustRepair(input) { SolidifyResolution = 32, CancelF = () => true };
            Assert.False(r.Apply());
            Assert.Same(input, r.Mesh);
            Assert.Equal("", Invariance.Difference(before, r.Mesh));
            Assert.Equal(0, r.PassedThroughTriangles);
        }

        // ---- MeshRobustRepairPassThrough.cs:26 and :74: the guards for an empty mesh (the caller checks the input first, so they are called directly)

        [Fact]
        public void PassThroughSplit_EmptyInput_NothingToDo()
        {
            DMesh3 passed, rest;
            List<AxisAlignedBox3d> boxes;
            Assert.False(MeshRobustRepairPassThrough.Split(new DMesh3(), 64, true, out passed, out rest, out boxes));
            Assert.Null(passed);
            Assert.Null(rest);
            Assert.Null(boxes);
        }

        [Fact]
        public void PassThroughMeetsAny_EmptyMesh_MeetsNothing()
        {
            var box = new AxisAlignedBox3d(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            Assert.False(MeshRobustRepairPassThrough.MeetsAny(new DMesh3(), new List<AxisAlignedBox3d> { box }));
            // (and the same mesh with a triangle inside the box does meet it)
            DMesh3 m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(Vector3d.Zero), m.AppendVertex(Vector3d.AxisX), m.AppendVertex(Vector3d.AxisY));
            Assert.True(MeshRobustRepairPassThrough.MeetsAny(m, new List<AxisAlignedBox3d> { box }));
        }

        // the result of the others is empty (injected through the test hook): nothing of it meets a passed part, the union is the passed parts
        [Fact]
        public void PassThrough_EmptyResultOfTheOthers_UnionIsThePassedParts()
        {
            DMesh3 clean = Sphere(0, 1);
            var r = new MeshRobustRepair(Union(clean, WithHole(Sphere(8, 0.5, 5)))) { SolidifyResolution = 32 };
            r.PassThroughTestHook = (m) => {
                foreach (int t in m.TriangleIndices().ToList()) m.RemoveTriangle(t, true, false);
            };
            Assert.True(r.Apply());
            Assert.False(r.PassThroughFellBack);
            Assert.Equal(clean.TriangleCount, r.PassedThroughTriangles);
            Assert.Equal(clean.TriangleCount, r.Mesh.TriangleCount);
            Assert.True(Vertices(clean).SetEquals(Vertices(r.Mesh)));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
        }

        // ---- MeshRepairMetrics.cs:372: an unused vertex far away keeps the mesh out of the canonical frame (its scaled coordinate would overflow)

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnitScale_UnusedVertexThatOverflowsWhenScaled_NoScale(bool readOnly)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(Vector3d.Zero), m.AppendVertex(new Vector3d(0.25, 0, 0)), m.AppendVertex(new Vector3d(0, 0.125, 0)));
            Assert.Equal(4.0, MeshRepairMetrics.unit_scale_for_mesh(m, readOnly, 0, 0));
            m.AppendVertex(new Vector3d(1e300, 0, 0));          // (the box of the triangles does not see it; 1e300 * 4 > 1e300)
            Assert.Equal(1.0, MeshRepairMetrics.unit_scale_for_mesh(m, readOnly, 0, 0));
        }

        // ---- MeshWindingNumberSolidify.cs:283 and :286: FinishPostProcess of a stopped run on a mesh that Compute() scaled into its frame

        static MeshWindingNumberSolidify Solidify(DMesh3 m, bool stop)
        {
            var so = new MeshWindingNumberSolidify(m) { GridResolution = 40, Adaptive = false, StopIfSelfIntersecting = stop };
            Assert.True(so.Compute());
            return so;
        }

        [Theory]
        [InlineData(-23)]
        [InlineData(77)]
        public void Solidify_FinishPostProcessAfterAStopOnAScaledMesh_SameAsTheRunWithoutStopping(int shift)
        {
            int finished = 0;
            for (int seed = 1; seed <= 6 && finished < 2; ++seed) {
                DMesh3 plates = MeshRobustRepairTests.ThinPlates(seed);
                DMesh3 scaled = Scaled(plates, shift);
                var stopped = Solidify(scaled, true);
                if (!stopped.StoppedOnSelfIntersections) continue;
                Assert.NotEqual(1.0, stopped.NormalizationScale);                  // (the mesh was scaled into the frame, so the finish scales the result there and back)
                stopped.FinishPostProcess();
                Assert.False(stopped.StoppedOnSelfIntersections);
                var full = Solidify(scaled, false);
                Assert.Equal("", Invariance.Difference(full.Result, stopped.Result));
                var unit = Solidify(new DMesh3(plates), false);
                Assert.Equal("", Invariance.Difference(unit.Result, Scaled(stopped.Result, -shift)));
                Assert.True(stopped.Result.TriangleCount > 0);
                finished++;
            }
            Assert.True(finished >= 2, $"{finished} stopped runs finished");
        }

        // ---- MeshRobustRepair.cs:602 and :604: lengths the caller sets in ConfigureRepair are in its units, the frame scales them

        // a unit cube with a short edge (a vertex split off one edge by 1e-3)
        static DMesh3 CubeWithShortEdge()
        {
            DMesh3 cube = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            int e = cube.EdgeIndices().First();
            DMesh3.EdgeSplitInfo info;
            Assert.Equal(MeshResult.Ok, cube.SplitEdge(e, out info, 0.001953125));      // 2^-9
            cube.SetVertex(info.vNew, cube.GetVertex(info.vNew) + new Vector3d(0.0009765625, 0.0009765625, 0.0009765625));
            return cube;
        }

        // MinEdgeLengthTol in the caller's units: the short edge collapses with a tolerance above its length, at any scale of the mesh
        [Theory]
        [InlineData(-95)]
        [InlineData(100)]
        public void Frame_MinEdgeLengthTol_IsInTheCallersUnits(int shift)
        {
            DMesh3 cube = CubeWithShortEdge();
            Func<DMesh3, double, MeshRobustRepair> run = (m, tol) => {
                var r = new MeshRobustRepair(m) { SolidifyResolution = 32, ConfigureRepair = (rep) => rep.MinEdgeLengthTol = tol };
                Assert.True(r.Apply());
                Assert.Equal("", r.ResultReason);
                return r;
            };
            double s = Math.Pow(2, shift);
            MeshRobustRepair kept = run(Scaled(cube, shift), 0.0001 * s);          // (shorter than the edge: it stays)
            Assert.Equal(cube.TriangleCount, kept.Mesh.TriangleCount);
            MeshRobustRepair r0 = run(new DMesh3(cube), 0.01);
            Assert.True(r0.Mesh.TriangleCount < cube.TriangleCount);
            MeshRobustRepair r = run(Scaled(cube, shift), 0.01 * s);
            Assert.NotEqual(1.0, r.LastFrameScale);
            Assert.Equal(r0.Mesh.TriangleCount, r.Mesh.TriangleCount);
            Assert.Equal("", Invariance.Difference(r0.Mesh, Scaled(r.Mesh, -shift)));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            Assert.Equal(1.0, r.Mesh.GetBounds().MaxDim / s, 2);
        }

        // CleanupReferenceDiagonal in the caller's units: a diagonal of 1e7 makes every triangle of the unit cube degenerate (the repair is then
        // rejected for its volume, UsedInput), a diagonal of 1 does not; the same at any scale of the mesh
        [Theory]
        [InlineData(-95)]
        [InlineData(100)]
        public void Frame_CleanupReferenceDiagonal_IsInTheCallersUnits(int shift)
        {
            DMesh3 cube = CubeWithShortEdge();
            Func<DMesh3, double, MeshRobustRepair> run = (m, diagonal) => {
                var r = new MeshRobustRepair(m) { SolidifyResolution = 32, ConfigureRepair = (rep) => rep.CleanupReferenceDiagonal = diagonal };
                Assert.True(r.Apply());
                Assert.Equal("", r.ResultReason);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
                return r;
            };
            double s = Math.Pow(2, shift);
            MeshRobustRepair small0 = run(new DMesh3(cube), 1), large0 = run(new DMesh3(cube), 1e7);
            Assert.False(small0.UsedInput);
            Assert.True(large0.UsedInput);
            MeshRobustRepair small = run(Scaled(cube, shift), 1 * s), large = run(Scaled(cube, shift), 1e7 * s);
            Assert.NotEqual(1.0, large.LastFrameScale);
            Assert.False(small.UsedInput);
            Assert.True(large.UsedInput);
            Assert.Equal(large0.FallbackReason, large.FallbackReason);
            Assert.Equal("", Invariance.Difference(large0.Mesh, Scaled(large.Mesh, -shift)));
            Assert.Equal("", Invariance.Difference(small0.Mesh, Scaled(small.Mesh, -shift)));
        }
    }
}
