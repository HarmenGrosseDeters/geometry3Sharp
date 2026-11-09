using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SafeRounding's degenerate area with several components side by side (found by the Exact Boolean 2.0 final run:
    /// 432 of 16239 Thingi10K operations, mostly intersections, came out RoundingClean but "degenerate triangles" by
    /// MeshRobustRepair.NotCleanReason, against 81 unclean in #385). Since the far-component tolerances the degenerate
    /// area was relative to the largest component's diagonal; NotCleanReason's is relative to the diagonal of all
    /// triangles. With pieces close together (thingi 100606's intersection: 3 pieces, box 114, largest 72) slivers
    /// between the two areas stayed. Now the box's diagonal, unless the components are far apart (box above 100 times the
    /// largest component's; ExactBooleanFarComponentTests).
    /// </summary>
    public class RoundingAdjacentComponentsTests
    {
        // three unit spheres in a row, spacing apart (2.5: box diagonal about 7.6, largest component 3.5), the first with
        // needles: every 9th edge split at 3e-10 from its first end (area about 0.5 * 3e-10 * the opposite height)
        static DMesh3 Row(double spacing = 2.5)
        {
            var m = new DMesh3();
            for (int i = 0; i < 3; ++i) {
                DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
                if (i == 0)
                    foreach (int eid in s.EdgeIndices().ToArray()) {
                        if (eid % 9 != 0)
                            continue;
                        Index2i ev = s.GetEdgeV(eid);
                        Vector3d p = s.GetVertex(ev.a), q = s.GetVertex(ev.b);
                        Assert.Equal(MeshResult.Ok, s.SplitEdge(eid, out DMesh3.EdgeSplitInfo info));
                        s.SetVertex(info.vNew, p + 3e-10 * (q - p).Normalized);
                    }
                MeshTransforms.Translate(s, new Vector3d(spacing * i, 0, 0));
                new MeshEditor(m).AppendMesh(s);
            }
            return m;
        }

        static MeshRoundingRepair Repair(DMesh3 m)
        {
            var r = new MeshRoundingRepair(m);
            r.Apply();
            return r;
        }

        [Fact]
        public void SideBySide_NeedlesBetweenTheTwoAreas_Repaired()
        {
            DMesh3 m = Row();
            // the setup: needles below the box's degenerate area but above the largest component's
            double box = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength, largest = gs.MeshRepairMetrics.LargestComponentDiagonal(m);
            Assert.True(box < 8 * largest && box > 2 * largest);
            int between = m.TriangleIndices().Count(t => m.GetTriArea(t) >= 1e-12 * largest * largest && m.GetTriArea(t) < 1e-12 * box * box);
            Assert.True(between > 0);
            MeshRoundingRepair r = Repair(m);
            Assert.True(r.IsClean);
            Assert.True(m.IsClosed() && Invariance.AllFinite(m));
            // clean by the evaluator too (before: RoundingClean, but "degenerate triangles")
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            var comps = new MeshConnectedComponents(m);
            comps.FindConnectedT();
            Assert.Equal(3, comps.Count);
        }

        [Theory]
        [InlineData(20, 12.0, false)]
        [InlineData(345, 200.0, true)]
        public void FarApartThreshold_BoxBelow100TimesTheLargest(double spacing, double ratio, bool far)
        {
            // the box's diagonal up to 100 times the largest component's: the box's degenerate area (the needles are
            // repaired); above: the largest component's (they stay, as for parts far apart: the rounding calls the result
            // clean, NotCleanReason degenerate; ExactBooleanFarComponentTests)
            DMesh3 m = Row(spacing);
            double box = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength, largest = gs.MeshRepairMetrics.LargestComponentDiagonal(m);
            Assert.InRange(box / largest, 0.95 * ratio, 1.05 * ratio);
            Assert.Contains(m.TriangleIndices(), t => m.GetTriArea(t) >= 1e-12 * largest * largest && m.GetTriArea(t) < 1e-12 * box * box);
            MeshRoundingRepair r = Repair(m);
            Assert.True(r.IsClean && m.IsClosed());
            Assert.Equal(far ? "degenerate triangles" : "", MeshRobustRepair.NotCleanReason(m));
        }

        [Fact]
        public void Fragments_BoxSixteenTimesTheLargest_Repaired()
        {
            // a result in many pieces (the Thingi10K intersections and differences the threshold 8 left degenerate: 29 to
            // 785 fragments, box / largest 8 to 16): 16 unit spheres on a 4 x 4 grid, 13 apart, the first with needles
            DMesh3 m = Row();
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).x > 1.5)
                    m.RemoveTriangle(t, true, false);
            m = new DMesh3(m, true);
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j) {
                    if (i == 0 && j == 0)
                        continue;
                    DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
                    MeshTransforms.Translate(s, new Vector3d(13 * i, 13 * j, 0));
                    new MeshEditor(m).AppendMesh(s);
                }
            double box = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength, largest = gs.MeshRepairMetrics.LargestComponentDiagonal(m);
            Assert.InRange(box / largest, 15, 18);
            Assert.Contains(m.TriangleIndices(), t => m.GetTriArea(t) >= 1e-12 * largest * largest && m.GetTriArea(t) < 1e-12 * box * box);
            MeshRoundingRepair r = Repair(m);
            Assert.True(r.IsClean && m.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            var comps = new MeshConnectedComponents(m);
            comps.FindConnectedT();
            Assert.Equal(16, comps.Count);
        }

        [Fact]
        public void SideBySide_SmallRealPart_NotRemovedAsTiny()
        {
            // the tiny test keeps the largest component's diagonal: a corner tetrahedron with legs 4e-4 between the
            // spheres (area 3.8e-7: above 1e4 DegenerateArea largest^2 = 1.2e-7, below the box's 5.7e-7; not thin) with
            // a needle (so that the rounding works on it) stays
            DMesh3 m = Row();
            double a = 4e-4;
            var p = new Vector3d(1.25, 0, 0);
            int v0 = m.AppendVertex(p), v1 = m.AppendVertex(p + a * Vector3d.AxisX), v2 = m.AppendVertex(p + a * Vector3d.AxisY), v3 = m.AppendVertex(p + a * Vector3d.AxisZ);
            m.AppendTriangle(v0, v2, v1); m.AppendTriangle(v0, v1, v3); m.AppendTriangle(v0, v3, v2); m.AppendTriangle(v1, v2, v3);
            int e = m.FindEdge(v1, v2);
            Assert.Equal(MeshResult.Ok, m.SplitEdge(e, out DMesh3.EdgeSplitInfo info));
            m.SetVertex(info.vNew, m.GetVertex(v1) + 1e-8 * (m.GetVertex(v2) - m.GetVertex(v1)).Normalized);
            double box = gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength, largest = gs.MeshRepairMetrics.LargestComponentDiagonal(m);
            double tetArea = 1.5 * a * a + 0.5 * Math.Sqrt(3) * a * a;
            Assert.InRange(tetArea, 1e-8 * largest * largest, 1e-8 * box * box);
            MeshRoundingRepair r = Repair(m);
            Assert.Equal(0, r.RemovedComponents);
            var comps = new MeshConnectedComponents(m);
            comps.FindConnectedT();
            Assert.Equal(4, comps.Count);
            Assert.True(r.IsClean && m.IsClosed());
        }

        [Fact]
        public void SideBySide_Similarities_CleanAndPow2BitIdentical()
        {
            DMesh3 start = Row(), m0 = new DMesh3(start);
            MeshRoundingRepair r0 = Repair(m0);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(start, sim);
                MeshRoundingRepair r = Repair(m);
                Assert.True(r.IsClean && Invariance.AllFinite(m) && m.IsClosed(), sim.ToString());
                Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
                if (sim.IsExact) {
                    Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m, sim)));
                    Assert.Equal(r0.Collapses, r.Collapses);
                }
            }
        }

        [Fact]
        public void SideBySide_SerialEqualsParallel_ApplyTwice()
        {
            DMesh3 start = Row(), parallel = new DMesh3(start), serial = new DMesh3(start);
            Repair(parallel);
            gParallel.SerialOnThisThread = true;
            try {
                Repair(serial);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            Assert.Equal("", Invariance.Difference(serial, parallel));
            var r = new MeshRoundingRepair(new DMesh3(start));
            Assert.True(r.Apply());
            r.Mesh = new DMesh3(start);
            Assert.True(r.Apply());
            Assert.Equal("", Invariance.Difference(serial, r.Mesh));
        }
    }
}
