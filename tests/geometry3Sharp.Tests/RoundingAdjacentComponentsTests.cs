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
    /// between the two areas stayed. Now the box's diagonal, unless the components are far apart (box above 8 times the
    /// largest component's; ExactBooleanFarComponentTests).
    /// </summary>
    public class RoundingAdjacentComponentsTests
    {
        // three unit spheres in a row, 2.5 apart (box diagonal about 7.6, largest component 3.5), the first with needles:
        // every 9th edge split at 3e-10 from its first end (area about 0.5 * 3e-10 * the opposite height)
        static DMesh3 Row()
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
                MeshTransforms.Translate(s, new Vector3d(2.5 * i, 0, 0));
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
            double box = m.CachedBounds.DiagonalLength, largest = gs.MeshRepairMetrics.LargestComponentDiagonal(m);
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
