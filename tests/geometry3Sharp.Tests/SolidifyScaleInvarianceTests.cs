using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshWindingNumberSolidify under power-of-two scaling (QA 2.0 F4 of RepairCampaignFindings): the result for a scene scaled by 2^k is
    /// the unit-scale result times 2^k, bit for bit, at every scale of the supported range. Before, the solidify ran on the mesh as it was
    /// for an extent within [2^-100, 2^100], and the result differed from the unit-scale one at extents about 2^-25 and below
    /// (absolute tolerances of the spatial queries) and at coordinates of about 2^72 to 2^98 (a window of magnitudes, from float
    /// intermediates of the grid and the octree); it is now always computed on a copy scaled to an extent in [1, 2).
    /// </summary>
    public class SolidifyScaleInvarianceTests
    {
        // an open sphere (three triangles removed) overlapping a box: needs the solidify
        static DMesh3 Scene(int variant)
        {
            var a = DegenerateFamilies.Sphere(3 + variant % 2);
            var box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.9, 0.3, 0.2), new Vector3d(0.4, 0.3, 0.35)) }.Generate().MakeDMesh();
            new MeshEditor(a).AppendMesh(box);
            foreach (int k in new[] { 2, 11, 23 }) {
                int t = a.TriangleIndices().Skip(k + 5 * variant).First();
                a.RemoveTriangle(t, true, false);
            }
            return a;
        }

        static DMesh3 Scaled(DMesh3 m, int shift)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, Math.Pow(2, shift) * c.GetVertex(v));
            return c;
        }

        static DMesh3 Solidify(DMesh3 m)
        {
            var so = new MeshWindingNumberSolidify(m) { GridResolution = 24 };
            Assert.True(so.Compute());
            return so.Result;
        }

        public static readonly int[] Shifts = { -60, -30, -23, -10, -1, 0, 1, 2, 20, 60, 73, 77, 85, 95, 120 };

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Solidify_ShiftSweep_BitIdentical(int variant)
        {
            DMesh3 scene = Scene(variant);
            DMesh3 reference = Solidify(new DMesh3(scene));
            foreach (int shift in Shifts) {
                DMesh3 r = Solidify(Scaled(scene, shift));
                Assert.True(Invariance.Difference(reference, Scaled(r, -shift)) == "", $"variant {variant}, shift {shift}: " + Invariance.Difference(reference, Scaled(r, -shift)));
            }
        }

        [Fact]
        public void Solidify_Similarities_Pow2BitIdentical()
        {
            DMesh3 scene = Scene(1);
            DMesh3 r0 = Solidify(new DMesh3(scene));
            foreach (Similarity sim in Similarity.All()) {
                if (!sim.IsExact) continue;
                DMesh3 r = Solidify(Invariance.Transformed(scene, sim));
                Assert.Equal("", Invariance.Difference(r0, Invariance.MappedBack(r, sim)));
            }
        }

        // the same through the robust repair (exact resolve and wrap off: the solidify result is the repair's result)
        [Theory]
        [InlineData(-23)]
        [InlineData(77)]
        [InlineData(90)]
        public void RobustRepair_SolidifyPath_ShiftBitIdentical(int shift)
        {
            DMesh3 scene = Scene(1);
            Func<DMesh3, MeshRobustRepair> run = (m) => {
                var r = new MeshRobustRepair(m) { ExactResolve = false, AlphaWrapFallback = false, SolidifyResolution = 24 };
                r.Apply();
                return r;
            };
            MeshRobustRepair r0 = run(new DMesh3(scene)), r1 = run(Scaled(scene, shift));
            Assert.Equal(r0.ResultReason, r1.ResultReason);
            Assert.Equal("", Invariance.Difference(r0.Mesh, Scaled(r1.Mesh, -shift)));
        }

        // a mesh whose extent is exactly a power of two (the boundary of the canonical window [1, 2)): 1 and 2 must be computed in the same frame
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(0.5)]
        public void Solidify_ExtentExactlyAPowerOfTwo_SameFrame(double extent)
        {
            Func<double, DMesh3> box = (e) => new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5 * e, 0.5 * e, 0.5 * e), new Vector3d(0.5 * e, 0.5 * e, 0.5 * e)) }.Generate().MakeDMesh();
            DMesh3 m = box(extent);
            foreach (int k in new[] { 3, 17 }) { int t = m.TriangleIndices().Skip(k % m.TriangleCount).First(); m.RemoveTriangle(t, true, false); }
            DMesh3 one = new DMesh3(m);
            foreach (int v in one.VertexIndices()) one.SetVertex(v, one.GetVertex(v) / extent);
            // (the window is [1, 2): an extent of exactly 2 is scaled to 1, not left as it is)
            var so = new MeshWindingNumberSolidify(new DMesh3(m)) { GridResolution = 24 };
            Assert.True(so.Compute());
            Assert.Equal(1 / extent, so.NormalizationScale);
            DMesh3 r = Solidify(m), r1 = Solidify(one);
            foreach (int v in r.VertexIndices()) r.SetVertex(v, r.GetVertex(v) / extent);
            Assert.Equal("", Invariance.Difference(r1, r));
        }

        // the canonical frame of the solidify: every extent is brought into [1, 2) (an extent of exactly 2 is scaled, not left as it is)
        [Theory]
        [InlineData(1.0, 1.0)]
        [InlineData(1.5, 1.0)]
        [InlineData(1.9999999999999998, 1.0)]
        [InlineData(2.0, 0.5)]
        [InlineData(4.0, 0.25)]
        [InlineData(0.5, 2.0)]
        [InlineData(0.75, 2.0)]
        [InlineData(1e-30, 0.0)]       // (the expected scale is computed in the test)
        public void UnitScale_CanonicalWindow_IsOneToTwo(double extent, double expectedScale)
        {
            if (extent == 1e-30) { expectedScale = Math.Pow(2, -Math.Floor(Math.Log(extent, 2))); }
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(Vector3d.Zero), m.AppendVertex(new Vector3d(extent, 0, 0)), m.AppendVertex(new Vector3d(0, extent / 2, 0)));
            double s = MeshRepairMetrics.unit_scale_for_mesh(m, true, 0, 0);
            Assert.Equal(expectedScale, s);
            Assert.InRange(extent * s, 1.0, 1.9999999999999998);
        }

        // StopIfSelfIntersecting + FinishPostProcess run in the frame of Compute(): the same mesh as a Compute() without stopping, at any scale
        [Theory]
        [InlineData(0)]
        [InlineData(-23)]
        [InlineData(77)]
        public void FinishPostProcess_SameAsFullCompute_AtAnyScale(int shift)
        {
            DMesh3 plates = MeshRobustRepairTests.ThinPlates(6);
            Func<bool, MeshWindingNumberSolidify> run = (stop) => {
                var so = new MeshWindingNumberSolidify(Scaled(plates, shift)) { GridResolution = 32, Adaptive = true, StopIfSelfIntersecting = stop };
                Assert.True(so.Compute());
                return so;
            };
            var full = run(false);
            var stopped = run(true);
            if (!stopped.StoppedOnSelfIntersections) return;      // (nothing to finish: the contour has no self-intersection at this resolution)
            stopped.FinishPostProcess();
            Assert.Equal("", Invariance.Difference(full.Result, stopped.Result));
            var unit = new MeshWindingNumberSolidify(new DMesh3(plates)) { GridResolution = 32, Adaptive = true };
            Assert.True(unit.Compute());
            Assert.Equal("", Invariance.Difference(unit.Result, Scaled(stopped.Result, -shift)));
        }

        // an unused vertex far away is no reason to leave the canonical frame, and must not overflow when scaled
        [Fact]
        public void Solidify_UnusedFarVertex_SameAsWithout()
        {
            DMesh3 scene = Scaled(Scene(1), -1);
            DMesh3 with = new DMesh3(scene);
            with.AppendVertex(new Vector3d(1e308, 0, 0));
            DMesh3 a = Solidify(scene), b = Solidify(with);
            Assert.Equal("", Invariance.Difference(a, b));
        }

        // CellSize and UsedCellSize follow the scale
        [Fact]
        public void Solidify_UserCellSize_UsedCellSizeScalesWithTheMesh()
        {
            DMesh3 scene = Scene(0);
            var a = new MeshWindingNumberSolidify(new DMesh3(scene)) { CellSize = 0.05 };
            var b = new MeshWindingNumberSolidify(Scaled(scene, 40)) { CellSize = 0.05 * Math.Pow(2, 40) };
            Assert.True(a.Compute());
            Assert.True(b.Compute());
            Assert.Equal(a.UsedCellSize, b.UsedCellSize * Math.Pow(2, -40));
            Assert.Equal("", Invariance.Difference(a.Result, Scaled(b.Result, -40)));
        }

        [Fact]
        public void Solidify_Reuse_TwoComputesOnOneInstance()
        {
            var so = new MeshWindingNumberSolidify(Scaled(Scene(1), 77)) { GridResolution = 24 };
            Assert.True(so.Compute());
            var first = new DMesh3(so.Result);
            Assert.True(so.Compute());
            Assert.Equal("", Invariance.Difference(first, so.Result));
        }
    }
}
