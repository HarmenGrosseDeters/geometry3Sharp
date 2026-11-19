using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The opt-in work cap over all improvement operations (MeshTetrahedralization.MaxImproveVisitsPerVertex, #1154, TET_MESHING.md 17.16): a count of visits of
    /// a bad tet, never wall-clock; off = bit for bit as before (pinned by golden digests).
    /// </summary>
    public class TetImproveVisitCapTests
    {
        static DMesh3 Thin() => TetMeshingStage2Tests.Input("torus thin");

        // FNV-1a over the bits of the vertices, the tets and the sorted counts
        static ulong Digest(MeshTetrahedralization tm)
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v) { for (int k = 0; k < 8; ++k) { h ^= (ulong)((v >> (8 * k)) & 0xFF); h *= 1099511628211UL; } }
            foreach (Vector3d p in tm.Result.Vertices) { Mix(BitConverter.DoubleToInt64Bits(p.x)); Mix(BitConverter.DoubleToInt64Bits(p.y)); Mix(BitConverter.DoubleToInt64Bits(p.z)); }
            foreach (Index4i t in tm.Result.Tets) { Mix(t.a); Mix(t.b); Mix(t.c); Mix(t.d); }
            foreach (var kv in tm.Counts.OrderBy(k => k.Key, StringComparer.Ordinal)) { foreach (char c in kv.Key) Mix(c); Mix(kv.Value); }
            return h;
        }

        // the default output, pinned: the option must not change it (digests taken on master before the option)
        [Theory]
        [InlineData("sphere", 6708374383569038591UL)]
        [InlineData("torus thin", 581502297912579406UL)]
        public void Default_OutputIsPinned(string shape, ulong golden)
        {
            var tm = new MeshTetrahedralization(TetMeshingStage2Tests.Input(shape)) { RelativeAlpha = TetMeshingStage2SplitTests.MechanismAlpha };
            Assert.True(tm.Compute());
            Assert.False(tm.Counts.ContainsKey("improve_work_cap_reached"));
            Assert.Equal(golden, Digest(tm));
        }

        static MeshTetrahedralization MakeV(DMesh3 input, double cap) => new MeshTetrahedralization(input) { RelativeAlpha = TetMeshingStage2SplitTests.MechanismAlpha, MaxImproveVisitsPerVertex = cap };

        static TetMeshImprover ImproverV(long visits)
        {
            var tm = new MeshTetrahedralization(Thin()) { Improve = false, RelativeAlpha = TetMeshingStage2SplitTests.MechanismAlpha };
            Assert.True(tm.Compute());
            var imp = new TetMeshImprover(tm.Result) { TargetDegrees = 10, MaxPasses = 50, RescuePasses = 150, Insertion = true, Contraction = true, MaxVisits = visits,
                InputTree = new DMeshAABBTree3(Thin(), true), BoundarySlideLimit = tm.BoundarySlideFactor * tm.UsedOffset };
            imp.Run();
            return imp;
        }

        [Fact]
        public void Visits_BudgetIsExact_AndARoomyBudgetChangesNothing()
        {
            TetMeshImprover free = ImproverV(0);
            long used = free.Visits;
            Assert.True(used > 20, used.ToString());
            Assert.False(free.Counts.ContainsKey("work_cap_reached"));
            foreach (long k in new[] { 1L, 7L, used / 2 }) {
                TetMeshImprover c = ImproverV(k);
                Assert.Equal(k, c.Visits);
                Assert.Equal(1, c.Counts["work_cap_reached"]);
            }
            // a budget exactly as large as the run needs is not "reached" (the cap trips only when a further visit would exceed it)
            TetMeshImprover exact = ImproverV(used);
            Assert.False(exact.Counts.ContainsKey("work_cap_reached"));
            Assert.Equal(free.Result().Tets, exact.Result().Tets);
            Assert.Equal(free.Counts, exact.Counts);
            TetMeshImprover roomy = ImproverV(used + 1);
            Assert.False(roomy.Counts.ContainsKey("work_cap_reached"));
            Assert.Equal(used, roomy.Visits);
            Assert.Equal(free.Result().Tets, roomy.Result().Tets);
            Assert.Equal(free.Result().Vertices, roomy.Result().Vertices);
            Assert.Equal(free.Counts, roomy.Counts);
        }

        [Fact]
        public void Visits_SpentInTheMainPhase_SkipsTheRescue()
        {
            var tm = new MeshTetrahedralization(Thin()) { Improve = false, RelativeAlpha = TetMeshingStage2SplitTests.MechanismAlpha };
            Assert.True(tm.Compute());
            var imp = new TetMeshImprover(tm.Result) { TargetDegrees = 89, MaxPasses = 3, RescuePasses = 50, MaxVisits = 5 };
            imp.Run();
            Assert.Equal(5, imp.Visits);
            Assert.Equal(1, imp.Counts["work_cap_reached"]);
            Assert.False(imp.Counts.ContainsKey("rescue_passes"));
            Assert.Null(TetMeshQuality.NotValidReason(imp.Result()));
        }

        [Fact]
        public void Visits_SpentInTheFirstStagedPhase_SkipsTheSecond()
        {
            var tm = new MeshTetrahedralization(Thin()) { Improve = false, RelativeAlpha = TetMeshingStage2SplitTests.MechanismAlpha };
            Assert.True(tm.Compute());
            TetMeshImprover Run(bool staged)
            {
                var imp = new TetMeshImprover(tm.Result) { TargetDegrees = 89, MaxPasses = 3, RescuePasses = 0, MaxVisits = 5, MaxDihedralDegrees = staged ? 150 : 0, MaxDihedralStaged = staged };
                imp.Run();
                return imp;
            }
            TetMeshImprover one = Run(false), two = Run(true);
            Assert.Equal(5, two.Visits);
            Assert.Equal(1, two.Counts["work_cap_reached"]);
            Assert.Equal(one.Counts["passes"], two.Counts["passes"]);                       // the second phase did not start a pass
        }

        [Fact]
        public void Visits_OffAndOptionEdges_AreBitForBit()
        {
            var off = MakeV(Thin(), 0); Assert.True(off.Compute());
            var plain = new MeshTetrahedralization(Thin()) { RelativeAlpha = TetMeshingStage2SplitTests.MechanismAlpha }; Assert.True(plain.Compute());
            Assert.Equal(plain.Result.Tets, off.Result.Tets);
            Assert.Equal(plain.Counts, off.Counts);
            foreach (double v in new[] { -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e308, double.MaxValue, 1e6 }) {
                var tm = MakeV(Thin(), v); Assert.True(tm.Compute());
                Assert.Equal(off.Result.Tets, tm.Result.Tets);
                Assert.Equal(off.Result.Vertices, tm.Result.Vertices);
                Assert.False(tm.Counts.ContainsKey("improve_work_cap_reached"), v.ToString());
            }
            var tiny = MakeV(Thin(), 1e-300); Assert.True(tiny.Compute());
            Assert.Equal(1, tiny.Counts["improve_work_cap_reached"]);
        }

        [Fact]
        public void Visits_TheCapActs_IsReported_AndKeepsTheMeshValid_AndReuses()
        {
            var off = MakeV(Thin(), 0); Assert.True(off.Compute());
            var capped = MakeV(Thin(), 0.05);
            Assert.True(capped.Compute(), capped.Status.ToString());
            Assert.Equal(1, capped.Counts["improve_work_cap_reached"]);
            TetMeshingStage2Tests.Invariants(capped);
            Assert.True(capped.Counts["improve_passes"] <= off.Counts["improve_passes"]);
            Assert.NotEqual(off.Result.Tets, capped.Result.Tets);
            TetMesh3 first = capped.Result; var counts = new Dictionary<string, long>(capped.Counts);
            Assert.True(capped.Compute());
            Assert.Equal(first.Tets, capped.Result.Tets); Assert.Equal(first.Vertices, capped.Result.Vertices); Assert.Equal(counts, capped.Counts);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Visits_TheCap_IsInvariantUnderEverySimilarity(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = Thin();
            var a = MakeV(input, 0.05); Assert.True(a.Compute());
            var b = MakeV(Invariance.Transformed(input, s), 0.05); Assert.True(b.Compute());
            Assert.Equal(1, a.Counts["improve_work_cap_reached"]);
            TetMeshingStage2Tests.Invariants(b);
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            }
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();
    }
}
