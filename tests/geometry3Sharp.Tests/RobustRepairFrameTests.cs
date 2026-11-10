using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRobustRepair.Apply() works in one exact unit frame (the mesh scaled by a power of two to an extent in [1, 2), the result scaled back):
    /// the result for a scene scaled by 2^k is the unit-scale result times 2^k, bit for bit, at every scale of the supported range, also in the
    /// windows of scales where single stages were not invariant (design note ROBUST_REPAIR_SCALE_FREE.md, 3.3).
    /// </summary>
    public class RobustRepairFrameTests
    {
        static double Round(double x) => Math.Round(x * 4096) / 4096;      // multiples of 2^-12

        // a unit sphere (coordinates rounded to multiples of 2^-12, so that the scenes are dyadic) and a small part of half size 0.25 at distance 8 along x
        public static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices = 6)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = radius * m.GetVertex(v);
                m.SetVertex(v, new Vector3d(Round(p.x), Round(p.y), Round(p.z)) + center);
            }
            return m;
        }

        public static DMesh3 CleanTwoParts()
        {
            DMesh3 m = Sphere(Vector3d.Zero, 1);
            var box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(8, 0, 0), new Vector3d(0.25, 0.25, 0.25)) }.Generate().MakeDMesh();
            new MeshEditor(m).AppendMesh(box);
            return m;
        }

        public static DMesh3 OpenSmallSphere()
        {
            DMesh3 m = Sphere(Vector3d.Zero, 1);
            DMesh3 small = Sphere(new Vector3d(8, 0, 0), 0.25, 5);
            foreach (int t in small.TriangleIndices().Take(8).ToList())
                small.RemoveTriangle(t, true, false);
            new MeshEditor(m).AppendMesh(small);
            return m;
        }

        static DMesh3 Scaled(DMesh3 m, int shift)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, Math.Pow(2, shift) * c.GetVertex(v));
            return c;
        }

        static MeshRobustRepair Run(DMesh3 m)
        {
            var r = new MeshRobustRepair(m) { SolidifyResolution = 32, WrapRelativeAlpha = 16, WrapRelativeOffset = 160 };
            r.Apply();
            return r;
        }

        public static readonly int[] Shifts = { -190, -150, -102, -95, -90, -80, -70, -50, -48, -44, -20, 20, 60, 100, 200, 300, 345, 380 };

        [Fact]
        public void CleanTwoParts_ShiftSweep_SameResult()
        {
            DMesh3 scene = CleanTwoParts();
            MeshRobustRepair r0 = Run(new DMesh3(scene));
            foreach (int shift in Shifts) {
                MeshRobustRepair r = Run(Scaled(scene, shift));
                Assert.Equal(r0.ResultReason, r.ResultReason);
                string d = Invariance.Difference(r0.Mesh, Scaled(r.Mesh, -shift));
                Assert.True(d == "", $"shift {shift}: {d}");
            }
        }

        [Fact]
        public void OpenSmallSphere_ShiftSweep_SameResult()
        {
            DMesh3 scene = OpenSmallSphere();
            MeshRobustRepair r0 = Run(new DMesh3(scene));
            foreach (int shift in Shifts) {
                MeshRobustRepair r = Run(Scaled(scene, shift));
                Assert.Equal(r0.ResultReason, r.ResultReason);
                string d = Invariance.Difference(r0.Mesh, Scaled(r.Mesh, -shift));
                Assert.True(d == "", $"shift {shift}: {d}");
            }
        }

        [Fact]
        public void Similarities_SameResult()
        {
            foreach (DMesh3 scene in new[] { CleanTwoParts(), OpenSmallSphere() }) {
                MeshRobustRepair r0 = Run(new DMesh3(scene));
                foreach (Similarity sim in Similarity.All()) {
                    if (!sim.IsExact) continue;
                    MeshRobustRepair r = Run(Invariance.Transformed(scene, sim));
                    Assert.Equal(r0.ResultReason, r.ResultReason);
                    Assert.Equal("", Invariance.Difference(r0.Mesh, Invariance.MappedBack(r.Mesh, sim)));
                }
            }
        }

        // the mesh object the caller handed over is in its own units after Apply(), also when Mesh is a new object
        [Fact]
        public void CallersMeshObject_IsBackInItsUnits()
        {
            DMesh3 input = Scaled(CleanTwoParts(), -95);
            DMesh3 before = new DMesh3(input);
            var r = new MeshRobustRepair(input) { SolidifyResolution = 32, WrapRelativeAlpha = 16, WrapRelativeOffset = 160 };
            r.Apply();
            Assert.True(r.LastFrameScale != 1);
            Assert.InRange(input.GetBounds().MaxDim / before.GetBounds().MaxDim, 0.99, 1.01);      // (not in the frame: its extent is 1 .. 2 there)
            Assert.InRange(r.Mesh.GetBounds().MaxDim / before.GetBounds().MaxDim, 0.99, 1.01);
        }

        [Fact]
        public void Reuse_ApplyTwiceOnTheResult_Unchanged()
        {
            var r = Run(Scaled(OpenSmallSphere(), -95));
            Assert.Equal("", r.ResultReason);
            DMesh3 first = new DMesh3(r.Mesh);
            Assert.True(r.Apply());
            Assert.Equal("", Invariance.Difference(first, r.Mesh));
        }

        // lengths the caller sets are in its units: the frame scales them (a crack of 1e-4 in a unit cube, repaired with an absolute tolerance of 1e-3)
        [Theory]
        [InlineData(-40)]
        [InlineData(-10)]
        [InlineData(10)]
        [InlineData(40)]
        public void AbsoluteTolerance_IsScaledWithTheMesh(int shift)
        {
            DMesh3 cube = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            // a crack: one corner's vertex used by one of its triangles is a copy, moved by 1e-4 (a dyadic 2^-13)
            int corner = cube.VertexIndices().First();
            int tri = cube.VtxTrianglesItr(corner).First();
            Index3i t = cube.GetTriangle(tri);
            int copy = cube.AppendVertex(cube.GetVertex(corner) + new Vector3d(Math.Pow(2, -13), 0, 0));
            cube.SetTriangle(tri, new Index3i(t.a == corner ? copy : t.a, t.b == corner ? copy : t.b, t.c == corner ? copy : t.c));
            Func<DMesh3, double, MeshRobustRepair> run = (m, tolerance) => {
                var r = new MeshRobustRepair(m) { SolidifyResolution = 32, ConfigureRepair = (rep) => rep.RepairTolerance = tolerance };
                r.Apply();
                return r;
            };
            MeshRobustRepair r0 = run(new DMesh3(cube), 1e-3);
            MeshRobustRepair r = run(Scaled(cube, shift), 1e-3 * Math.Pow(2, shift));
            Assert.Equal(r0.ResultReason, r.ResultReason);
            Assert.Equal("", Invariance.Difference(r0.Mesh, Scaled(r.Mesh, -shift)));
        }

        // the public results of the stages are in the caller's frame after Apply(): the wrap's alpha and offset, its result, the solidify's cell size
        [Fact]
        public void PublicStageResults_AreInTheCallersFrame()
        {
            DMesh3 plates = MeshRobustRepairTests.ThinPlates(6);
            Func<DMesh3, MeshRobustRepair> run = (m) => {
                var r = new MeshRobustRepair(m) { RepairF = null, ExactResolve = false, SolidifyResolution = 24, WrapRelativeAlpha = 70, WrapRelativeOffset = 700 };
                r.Apply();
                return r;
            };
            MeshRobustRepair r0 = run(new DMesh3(plates));
            Assert.NotNull(r0.AlphaWrap);
            foreach (int shift in new[] { -40, 40, 100 }) {
                MeshRobustRepair r = run(Scaled(plates, shift));
                Assert.True(r.LastFrameScale != 1);
                Assert.NotNull(r.AlphaWrap);
                double s = Math.Pow(2, shift);
                Assert.Equal(r0.AlphaWrap.UsedAlpha * s, r.AlphaWrap.UsedAlpha);
                Assert.Equal(r0.AlphaWrap.UsedOffset * s, r.AlphaWrap.UsedOffset);
                Assert.Equal("", Invariance.Difference(r0.AlphaWrap.Result, Scaled(r.AlphaWrap.Result, -shift)));
                Assert.NotNull(r.Solidify);
                Assert.Equal(r0.Solidify.UsedCellSize * s, r.Solidify.UsedCellSize);
                Assert.Equal("", Invariance.Difference(r0.Mesh, Scaled(r.Mesh, -shift)));
                // the box of the wrap is the scene's, not the frame's
                Assert.InRange(r.AlphaWrap.Result.GetBounds().MaxDim / s, 0.5, 5);
            }
        }

        // the caller's RoundingAbsoluteTolerance is scaled into the frame and restored, also on the rounding objects the Apply() leaves
        [Fact]
        public void RoundingAbsoluteTolerance_ScaledInRestoredAfter()
        {
            DMesh3 m = Scaled(MeshRobustRepairTests.OverlappingSpheres(), -70);
            var r = new MeshRobustRepair(m) { RoundingAbsoluteTolerance = 1e-30 };
            Assert.True(r.Apply());
            Assert.True(r.LastFrameScale != 1);
            Assert.Equal(1e-30, r.RoundingAbsoluteTolerance);
            Assert.NotNull(r.ExactResolveRounding);
            Assert.Equal(1e-30, r.ExactResolveRounding.AbsoluteTolerance);
            // the same scene at unit scale with the tolerance scaled the same way: the same result
            var r0 = new MeshRobustRepair(MeshRobustRepairTests.OverlappingSpheres()) { RoundingAbsoluteTolerance = 1e-30 * Math.Pow(2, 70) };
            Assert.True(r0.Apply());
            Assert.Equal("", Invariance.Difference(r0.Mesh, Scaled(r.Mesh, 70)));
        }

        // ToleranceMode.Absolute (the 1.0.324 values, some constants not scalable from here) and a callback that needs a mesh: no frame
        [Fact]
        public void AbsoluteMode_And_ThrowingProbe_NoFrame()
        {
            var a = new MeshRobustRepair(Scaled(CleanTwoParts(), -95)) { SolidifyResolution = 32, ConfigureRepair = (rep) => rep.ToleranceMode = MeshAutoRepair.ToleranceModes.Absolute };
            a.Apply();
            Assert.Equal(1.0, a.LastFrameScale);
            var t = new MeshRobustRepair(Scaled(CleanTwoParts(), -95)) { SolidifyResolution = 32, ConfigureRepair = (rep) => { if (rep.Mesh.TriangleCount == 0) throw new InvalidOperationException("needs a mesh"); } };
            t.Apply();
            Assert.Equal(1.0, t.LastFrameScale);
        }

        // RepairF is a function on the caller's coordinates: the frame is not used
        [Fact]
        public void RepairF_NoFrame()
        {
            var r = new MeshRobustRepair(Scaled(OpenSmallSphere(), -95)) { RepairF = (m) => new DMesh3(m), SolidifyResolution = 32, WrapRelativeAlpha = 16, WrapRelativeOffset = 160 };
            r.Apply();
            Assert.Equal(1.0, r.LastFrameScale);
        }

        // G: components far apart compared to their size: the alpha wrap is one global alpha, and a result in blobs of that size is not taken
        [Theory]
        [InlineData(22, 2)]
        [InlineData(22, 10)]
        [InlineData(30, 6)]
        public void FarOpenPart_WrapBlobs_AreNotAccepted(int f, int r)
        {
            DMesh3 scene = RobustRepairFrameTests.Sphere(Vector3d.Zero, 1);
            DMesh3 small = RobustRepairFrameTests.Sphere(Vector3d.Zero, 1, 5);
            foreach (int v in small.VertexIndices()) small.SetVertex(v, Math.Pow(2, -r) * small.GetVertex(v) + new Vector3d(Math.Pow(2, f), 0, 0));
            foreach (int t in small.TriangleIndices().Take(8).ToList()) small.RemoveTriangle(t, true, false);
            new MeshEditor(scene).AppendMesh(small);
            var rep = new MeshRobustRepair(scene);
            bool ok = rep.Apply();
            // before: true, with one octahedron of the size of alpha (1400) for each part (the sphere of radius 1 and the part)
            Assert.False(ok && rep.UsedAlphaWrap, $"{ok}, wrap {rep.UsedAlphaWrap}, {rep.Mesh.TriangleCount} triangles");
            Assert.Equal(MeshRobustRepair.WrapTooCoarse, rep.WrapReason);
        }

        [Fact]
        public void WrapGuard_Thresholds()
        {
            // two unit spheres at distance 8: the largest component has a diagonal of about 3.4; alpha + offset at the defaults is 0.0037 of the diagonal of all
            DMesh3 scene = RobustRepairFrameTests.Sphere(Vector3d.Zero, 1);
            new MeshEditor(scene).AppendMesh(RobustRepairFrameTests.Sphere(new Vector3d(8, 0, 0), 1));
            double all = scene.GetBounds().DiagonalLength, largest = MeshRepairMetrics.LargestComponentDiagonal(scene);
            Assert.False(MeshRobustRepair.wrap_too_coarse(scene, all * (1.0 / 300 + 1.0 / 3000)));
            Assert.True(MeshRobustRepair.wrap_too_coarse(scene, 0.06 * largest));
            Assert.False(MeshRobustRepair.wrap_too_coarse(scene, 0.04 * largest));
        }
    }
}
