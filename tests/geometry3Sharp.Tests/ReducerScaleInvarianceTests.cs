using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer under power-of-two scaling (F5 of RepairCampaignFindings, R14 of docs/ROBUSTNESS_SWEEP.md), at every scale from 2^-120 to 2^120:
    /// the edge queue's priorities were floats, which overflowed to infinity for a quadric error above 3.4e38 (an area weighted error is about the
    /// fourth power of the extent: from an extent of 2^32; every edge then had the same priority and the order was that of the ids, erratic in a
    /// window of scales) and underflowed below about 2^-32; and the triangle normals and areas of the quadrics used a length of 2.2e-16 as zero
    /// (triangles of edge length below about 1.5e-8 had no plane). Now the errors are scaled by a power of two for the queue, and the normals have
    /// no absolute threshold. Beyond 2^+-128 the quadric arithmetic itself leaves the double range (products of area weighted planes), which the
    /// Reducer answers by taking an endpoint or the midpoint of the edge.
    /// </summary>
    public class ReducerScaleInvarianceTests
    {
        static DMesh3 Scaled(DMesh3 m, int shift)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, Math.Pow(2, shift) * c.GetVertex(v));
            return c;
        }

        // a bumpy sphere: many different collapse errors
        static DMesh3 Bumpy()
        {
            DMesh3 m = DegenerateFamilies.Sphere(8);
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, p * (1 + 0.05 * Math.Sin(7 * p.x + 3 * p.y) * Math.Cos(5 * p.z)));
            }
            return m;
        }

        public static readonly int[] Shifts = { -120, -100, -60, -30, -24, -20, -10, -1, 1, 10, 30, 32, 33, 40, 63, 64, 66, 70, 80, 90, 100, 120 };

        static DMesh3 ReduceToCount(DMesh3 m, int count)
        {
            var r = new Reducer(m);
            r.ReduceToTriangleCount(count);
            return m;
        }

        [Fact]
        public void TriangleCount_ShiftSweep_BitIdentical()
        {
            DMesh3 unit = Bumpy();
            DMesh3 reference = ReduceToCount(new DMesh3(unit), unit.TriangleCount / 4);
            foreach (int shift in Shifts) {
                DMesh3 r = ReduceToCount(Scaled(unit, shift), unit.TriangleCount / 4);
                string d = Invariance.Difference(reference, Scaled(r, -shift));
                Assert.True(d == "", $"shift {shift}: {d}");
            }
        }

        [Fact]
        public void MaxQuadricError_ShiftSweep_BitIdentical()
        {
            DMesh3 unit = Bumpy();
            Func<DMesh3, double, DMesh3> run = (m, err) => { var r = new Reducer(m); r.ReduceToMaxQuadricError(err); return m; };
            DMesh3 reference = run(new DMesh3(unit), 1e-5);
            Assert.True(reference.TriangleCount < unit.TriangleCount && reference.TriangleCount > 50);
            foreach (int shift in Shifts) {
                // (area weighted quadrics: an error is an area times a squared distance, so it scales with the fourth power)
                DMesh3 r = run(Scaled(unit, shift), 1e-5 * Math.Pow(2, 4 * shift));
                string d = Invariance.Difference(reference, Scaled(r, -shift));
                Assert.True(d == "", $"shift {shift}: {d}");
            }
        }

        // an open mesh (boundary quadrics), without area weights, with a feature angle: other paths of the quadrics
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Options_ShiftSweep_BitIdentical(int variant)
        {
            DMesh3 unit = Bumpy();
            foreach (int k in new[] { 10, 200, 333 }) { int t = unit.TriangleIndices().Skip(k).First(); unit.RemoveTriangle(t, true, false); }
            Func<DMesh3, DMesh3> run = (m) => {
                var r = new Reducer(m);
                if (variant == 0) r.AreaWeightedQuadrics = false;
                if (variant == 1) { r.PreserveBoundaryShape = true; r.BoundaryQuadricWeight = 1; r.FeatureAngle = 20; r.FeatureQuadricWeight = 1; }
                if (variant == 2) { r.AreaWeightedQuadrics = false; r.PreventSelfIntersections = true; }
                r.ReduceToTriangleCount(unit.TriangleCount / 3);
                return m;
            };
            DMesh3 reference = run(new DMesh3(unit));
            foreach (int shift in new[] { -100, -30, -24, 33, 64, 100 }) {
                DMesh3 r = run(Scaled(unit, shift));
                string d = Invariance.Difference(reference, Scaled(r, -shift));
                Assert.True(d == "", $"variant {variant}, shift {shift}: {d}");
            }
        }

        [Fact]
        public void TriangleCount_Similarities_Pow2BitIdentical()
        {
            DMesh3 unit = Bumpy();
            DMesh3 r0 = ReduceToCount(new DMesh3(unit), unit.TriangleCount / 4);
            foreach (Similarity sim in Similarity.All()) {
                if (!sim.IsExact) continue;
                DMesh3 r = ReduceToCount(Invariance.Transformed(unit, sim), unit.TriangleCount / 4);
                Assert.Equal("", Invariance.Difference(r0, Invariance.MappedBack(r, sim)));
            }
        }

        [Fact]
        public void Reuse_TwoReductionsOnOneInstance_SecondContinues()
        {
            DMesh3 m = Scaled(Bumpy(), 80);
            var r = new Reducer(m);
            r.ReduceToTriangleCount(m.TriangleCount / 2);
            int half = m.TriangleCount;
            r.ReduceToTriangleCount(half / 2);
            Assert.True(m.TriangleCount <= half / 2 + 2);
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
        }
    }
}
