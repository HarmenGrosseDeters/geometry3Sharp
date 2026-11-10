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
            foreach (int v in c.VertexIndices()) c.SetVertex(v, Math.ScaleB(1.0, shift) * c.GetVertex(v));
            return c;
        }

        static DMesh3 Solidify(DMesh3 m)
        {
            var so = new MeshWindingNumberSolidify(m) { GridResolution = 24 };
            Assert.True(so.Compute());
            return so.Result;
        }

        public static readonly int[] Shifts = { -60, -30, -23, -10, 0, 20, 60, 73, 77, 85, 95, 120 };

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
