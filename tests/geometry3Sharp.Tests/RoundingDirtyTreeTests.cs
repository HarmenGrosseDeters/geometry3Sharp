using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SafeRounding's box query over the changed triangles (MeshRoundingRepair.near): the tree of the changed triangles
    /// is rebuilt only when the triangles changed since its build outnumber 256, those being scanned linearly, instead of
    /// after every edit (which made a repair with many edits quadratic: Thingi10K 119829/119830 union, 13000
    /// contractions of input slivers, rounding 22.9 -> 4.1 s). With MeshRoundingRepair.CheckOnThisThread every query is
    /// compared with a scan of all triangles (it throws if they differ). The meshes are grid boxes with many edges split
    /// next to an end: two slivers per split, which the repair contracts.
    /// </summary>
    public class RoundingDirtyTreeTests
    {
        // a grid box with every k-th edge split at a fraction f from its first end (two slivers per split)
        internal static DMesh3 SliverBox(int n = 24, int every = 9, double f = 1e-10)
        {
            DMesh3 m = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int e in m.EdgeIndices().Where(e => e % every == 0).ToList()) {
                if (m.IsEdge(e) == false)
                    continue;
                Index2i ev = m.GetEdgeV(e);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                DMesh3.EdgeSplitInfo info;
                if (m.SplitEdge(e, out info) == MeshResult.Ok)
                    m.SetVertex(info.vNew, a + f * (b - a));
            }
            return m;
        }

        static MeshRoundingRepair Repair(DMesh3 m, bool check = false, bool serial = false)
        {
            MeshRoundingRepair.CheckOnThisThread = check;
            gParallel.SerialOnThisThread = serial;
            try {
                var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true };
                r.Apply();
                return r;
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
                gParallel.SerialOnThisThread = false;
            }
        }

        static string Print(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices())
                sb.Append(t).Append(':').Append(m.GetTriangle(t)).Append(';');
            return sb.ToString();
        }

        [Fact]
        public void ManySlivers_QueriesEqualAScan_TreeBuiltRarely()
        {
            DMesh3 m = SliverBox();
            DMesh3 plainMesh = new DMesh3(m), checkedMesh = new DMesh3(m);
            MeshRoundingRepair plain = Repair(plainMesh);
            MeshRoundingRepair checkedRepair = Repair(checkedMesh, check: true);
            Assert.True(checkedRepair.InitialDegenerate > 1000, $"{checkedRepair.InitialDegenerate} degenerate");
            Assert.True(checkedRepair.IsClean, $"{checkedRepair.RemainingDegenerate} degenerate left");
            Assert.True(checkedRepair.Collapses > 500, $"{checkedRepair.Collapses} contractions");
            Assert.True(checkedRepair.NearChecks > 1000, $"{checkedRepair.NearChecks} queries checked");
            // (before: a build after every edit; now one per 256 changed triangles)
            Assert.True(checkedRepair.DirtyTreeBuilds > 1 && checkedRepair.DirtyTreeBuilds * 20 < checkedRepair.Collapses,
                $"{checkedRepair.DirtyTreeBuilds} builds for {checkedRepair.Collapses} contractions");
            Assert.Equal(Print(plainMesh), Print(checkedMesh));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(checkedMesh));
        }

        [Fact]
        public void DegenerateFamilies_QueriesEqualAScan()
        {
            long checks = 0;
            foreach (SafeRoundingDegenerateInputTests.Family f in Enum.GetValues(typeof(SafeRoundingDegenerateInputTests.Family)))
                foreach (int seed in new[] { 1, 2 }) {
                    DMesh3 m = SafeRoundingDegenerateInputTests.Make(f, 30, seed);
                    DMesh3 a = new DMesh3(m), b = new DMesh3(m);
                    Repair(a);
                    checks += Repair(b, check: true).NearChecks;
                    Assert.True(Print(a) == Print(b), f.ToString());
                }
            Assert.True(checks > 0);
        }

        [Fact]
        public void ManySlivers_Similarities_Pow2BitIdentical()
        {
            DMesh3 m = SliverBox(16, 7);
            DMesh3 m0 = new DMesh3(m);
            MeshRoundingRepair r0 = Repair(m0);
            Assert.True(r0.IsClean);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 ms = Invariance.Transformed(m, sim);
                MeshRoundingRepair rs = Repair(ms, check: true);
                Assert.True(rs.IsClean, sim.ToString());
                Assert.True(ms.IsClosed() && Invariance.AllFinite(ms), sim.ToString());
                if (sim.IsExact) {
                    Assert.True(Invariance.Difference(m0, Invariance.MappedBack(ms, sim)) == "", sim.ToString());
                    Assert.Equal(r0.Collapses, rs.Collapses);
                }
            }
        }

        [Fact]
        public void ManySlivers_SerialEqualsParallel_Repeated()
        {
            DMesh3 m = SliverBox();
            DMesh3 serial = new DMesh3(m);
            Repair(serial, serial: true);
            string s = Print(serial);
            for (int run = 0; run < 3; ++run) {
                DMesh3 p = new DMesh3(m);
                Repair(p);
                Assert.True(s == Print(p), $"parallel run {run} differs from the serial run");
            }
        }
    }
}
