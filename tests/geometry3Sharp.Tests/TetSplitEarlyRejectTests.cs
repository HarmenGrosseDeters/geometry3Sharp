using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact early rejection of split attempts (#1154, TetMeshImprover.try_split): an attempt whose neighbourhood's worst tet is not
    /// at the edge cannot succeed and is skipped. It must change nothing: the same result bits, counts (including split_refused_input),
    /// and the same internal ids and free lists as the run that tries every split to its end (SplitEarlyReject = false).
    /// </summary>
    public class TetSplitEarlyRejectTests
    {
        static MeshTetrahedralization Stage1(DMesh3 input, double floor, double alpha, double offset)
        {
            var tm = new MeshTetrahedralization(input) { Improve = false, RelativeAlpha = alpha, RelativeOffset = offset, SliverDihedralDegrees = floor };
            Assert.True(tm.Compute());
            return tm;
        }

        // the improver as MeshTetrahedralization sets it up, with the switch
        static TetMeshImprover Improve(MeshTetrahedralization tm, DMesh3 input, double floor, bool early)
        {
            var imp = new TetMeshImprover(tm.Result) {
                TargetDegrees = floor, CancelF = () => false, InputTree = new DMeshAABBTree3(input, true),
                BoundarySlideLimit = tm.BoundarySlideFactor * tm.UsedOffset, TangentOnly = tm.BoundaryMovesTangentOnly,
                MaxPasses = tm.ImprovePasses, RescuePasses = tm.RescuePasses, Contraction = tm.ContractEdges, Insertion = tm.SplitEdges,
                SplitEarlyReject = early };
            imp.Run();
            return imp;
        }

        public static IEnumerable<object[]> Cases() => new[] {
            new object[] { "torus48", 20.0, 12.0, 200.0 }, new object[] { "torus48", 25.0, 8.0, 100.0 }, new object[] { "torus48", 20.0, 8.0, 300.0 },
            new object[] { "torus thin", 10.0, 20.0, 600.0 }, new object[] { "torus thin", 20.0, 20.0, 600.0 } };

        static DMesh3 InputOf(string name) => name == "torus48" ? TetMeshingStage2Tests.Torus(48, 12, 1, 0.15) : TetMeshingStage2Tests.Input(name);

        [Theory]
        [MemberData(nameof(Cases))]
        public void EarlyRejection_ChangesNothing_ResultCountsAndIds(string name, double floor, double alpha, double offset)
        {
            DMesh3 input = InputOf(name);
            var tm = Stage1(input, floor, alpha, offset);
            var full = Improve(tm, input, floor, false);
            var fast = Improve(tm, input, floor, true);
            Assert.Equal(0, full.SplitEarlyRejects);
            Assert.True(fast.SplitEarlyRejects > 0, "the early rejection never acted: " + name);
            TetMesh3 a = full.Result(), b = fast.Result();
            Assert.Equal(a.Vertices, b.Vertices); Assert.Equal(a.Tets, b.Tets); Assert.Equal(a.BoundaryTriangles, b.BoundaryTriangles);
            Assert.Equal(full.Counts.OrderBy(k => k.Key), fast.Counts.OrderBy(k => k.Key));
            Assert.Equal(full.StateForTest(), fast.StateForTest());
            Assert.Equal(full.VertexCount, fast.VertexCount);
            Assert.True(full.Counts["insertions"] > 0 || floor == 10, name);
        }

        [Fact]
        public void EarlyRejection_KeepsTheRefusedInputCount_OfSkippedBoundarySplits()
        {
            // an input triangle through the midpoint of a boundary edge (as BoundarySplit_IsRefused_WhereTheSplitRegionTouchesTheInput):
            // the input refuses the split. Where the worst tet of the neighbourhood is not at the edge, the early rejection skips the attempt
            // and must still count the refusal; compared edge by edge with the full attempt
            DMesh3 torus = TetMeshingStage2Tests.Torus(48, 12, 1, 0.15);
            var tm = Stage1(torus, 20, 12, 200);
            TetMesh3 src = tm.Result;
            int refusedSkipped = 0, skipped = 0;
            foreach (Index3i g in src.BoundaryTriangles.Take(150)) {
                int a = g.a, b = g.b;
                Vector3d pa = src.Vertices[a], pb = src.Vertices[b], m = 0.5 * (pa + pb);
                Vector3d d = (pb - pa).Normalized, n = Vector3d.AxisZ.Cross(d);
                if (n.Length < 0.1) n = Vector3d.AxisX.Cross(d);
                n.Normalize();
                Vector3d k = d.Cross(n);
                double h = 1e-3 * pa.Distance(pb);
                DMesh3 input = new DMesh3(tm.Mesh);
                int v0 = input.AppendVertex(m + h * n), v1 = input.AppendVertex(m - h * n + h * k), v2 = input.AppendVertex(m - h * n - h * k);
                input.AppendTriangle(v0, v1, v2);
                TetMeshImprover Make(bool early) => new TetMeshImprover(src) { InputTree = new DMeshAABBTree3(input, true), BoundarySlideLimit = tm.BoundarySlideFactor * tm.UsedOffset,
                    TargetDegrees = 20, SplitEarlyReject = early };
                var full = Make(false); var fast = Make(true);
                if (!full.IsBoundaryEdge(a, b)) continue;
                bool rf = full.TrySplit(a, b), rq = fast.TrySplit(a, b);
                Assert.Equal(rf, rq);
                Assert.Equal(full.Counts.OrderBy(x => x.Key), fast.Counts.OrderBy(x => x.Key));
                Assert.Equal(full.StateForTest(), fast.StateForTest());
                if (fast.SplitEarlyRejects > 0) { skipped++; if (full.Counts["split_refused_input"] > 0) refusedSkipped++; }
            }
            Assert.True(skipped > 5, "attempts skipped: " + skipped);
            Assert.True(refusedSkipped > 5, "skipped attempts that the input refused: " + refusedSkipped);
        }

        [Fact]
        public void EarlyRejection_DoesNotCountARefusal_WhereAHalfOfTheSplitIsInverted()
        {
            // the split point moved off the edge, out of and into the mesh (SplitPointForTest), with an input triangle at it: where a half of
            // a ring tet is inverted the attempt is not refused by the input (no count); where not, it is. The skipped attempts must
            // give the same counts as the full ones in both cases
            DMesh3 torus = TetMeshingStage2Tests.Torus(48, 12, 1, 0.15);
            var tm = Stage1(torus, 20, 12, 200);
            TetMesh3 src = tm.Result;
            int skippedInverted = 0, skippedRefused = 0;
            foreach (double sign in new[] { 1.0, -1.0 })
                foreach (Index3i g in src.BoundaryTriangles.Take(120)) {
                    int a = g.a, b = g.b;
                    Vector3d pa = src.Vertices[a], pb = src.Vertices[b];
                    Vector3d nrm = (src.Vertices[g.b] - src.Vertices[g.a]).Cross(src.Vertices[g.c] - src.Vertices[g.a]).Normalized;
                    Vector3d m = 0.5 * (pa + pb) + sign * 0.8 * pa.Distance(pb) * nrm;
                    Vector3d u = (pb - pa).Normalized, w = nrm.Cross(u);
                    double h = 1e-3 * pa.Distance(pb);
                    DMesh3 input = new DMesh3(tm.Mesh);
                    int v0 = input.AppendVertex(m + h * nrm), v1 = input.AppendVertex(m - h * nrm + h * w), v2 = input.AppendVertex(m - h * nrm - h * w);
                    input.AppendTriangle(v0, v1, v2);
                    TetMeshImprover Make(bool early) => new TetMeshImprover(src) { InputTree = new DMeshAABBTree3(input, true), BoundarySlideLimit = 1e9,
                        TargetDegrees = 20, SplitEarlyReject = early, SplitPointForTest = (x, y) => m };
                    var full = Make(false); var fast = Make(true);
                    if (!full.IsBoundaryEdge(a, b)) continue;
                    Assert.Equal(full.TrySplit(a, b), fast.TrySplit(a, b));
                    Assert.Equal(full.Counts.OrderBy(x => x.Key), fast.Counts.OrderBy(x => x.Key));
                    Assert.Equal(full.StateForTest(), fast.StateForTest());
                    if (fast.SplitEarlyRejects > 0) { if (full.Counts["split_refused_input"] > 0) skippedRefused++; else skippedInverted++; }
                }
            Assert.True(skippedInverted > 5, "skipped, not refused by the input (a half inverted or the input not touched): " + skippedInverted);
            Assert.True(skippedRefused > 5, "skipped and refused: " + skippedRefused);
        }

        [Fact]
        public void EarlyRejection_SkipsMostAttempts_AndNeverTheOnesThatSucceed()
        {
            // every kept split of the full run is also kept by the fast run (equal insertions), and many attempts are skipped
            DMesh3 input = TetMeshingStage2Tests.Torus(48, 12, 1, 0.15);
            var tm = Stage1(input, 20, 12, 200);
            var full = Improve(tm, input, 20, false);
            var fast = Improve(tm, input, 20, true);
            Assert.Equal(full.Counts["insertions"], fast.Counts["insertions"]);
            Assert.True(fast.SplitEarlyRejects > 100, fast.SplitEarlyRejects.ToString());
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void FloorTwenty_Pow2ScalesBitForBit_AndReuse(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = TetMeshingStage2Tests.Torus(48, 12, 1, 0.15);
            var a = new MeshTetrahedralization(input) { RelativeAlpha = 12, RelativeOffset = 200, SliverDihedralDegrees = 20 }; Assert.True(a.Compute());
            var b = new MeshTetrahedralization(Invariance.Transformed(input, s)) { RelativeAlpha = 12, RelativeOffset = 200, SliverDihedralDegrees = 20 }; Assert.True(b.Compute());
            TetMeshingStage2Tests.Invariants(b);
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            }
            if (simName == "identity") {
                TetMesh3 first = a.Result;
                Assert.True(a.Compute());                                                // reuse: the same instance again
                Assert.Equal(first.Vertices, a.Result.Vertices); Assert.Equal(first.Tets, a.Result.Tets);
            }
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();
    }
}
