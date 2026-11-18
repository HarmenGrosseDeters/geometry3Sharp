using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The work bound of the tet refinement (docs/design/TET_MESHING.md section 17.10, issue #1138): MaxTrialInsertionsPerWrapVertex stops a
    /// refinement that does not end (MaxRadiusEdgeRatio near 1) with status PointLimitReached and a valid result, never on the default
    /// path. Every run here has a stopwatch as CancelF, so that the old code (no bound) fails these tests by status Cancelled instead of
    /// running for minutes.
    /// </summary>
    public class TetRefinementWorkBoundTests
    {
        static MeshTetrahedralization Make(string shape, double alpha, double rho, double seconds = 6)
        {
            var sw = Stopwatch.StartNew();
            return new MeshTetrahedralization(TetMeshingStage2Tests.Input(shape)) {
                RelativeAlpha = alpha, MaxRadiusEdgeRatio = rho, CancelF = () => sw.Elapsed.TotalSeconds > seconds };
        }

        static int WrapVertices(MeshTetrahedralization tm) => tm.Wrap.Triangulation.VertexCount;

        // the runs that do not end: 700 to 1400 trial insertions per wrap vertex and rising
        public static IEnumerable<object[]> Runaways() => new[] { new object[] { "sphere", 12.0, 1.0 }, new object[] { "box", 12.0, 1.2 }, new object[] { "cylinder", 12.0, 1.3 } };

        [Theory]
        [MemberData(nameof(Runaways))]
        public void RunawayRefinement_StopsAtTheBound_WithAValidResultAndTheTargetNotMet(string shape, double alpha, double rho)
        {
            MeshTetrahedralization tm = Make(shape, alpha, rho);
            tm.Compute();
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, tm.Status);
            Assert.Equal(1, tm.Counts["work_limit_reached"]);
            Assert.InRange(tm.Counts["trial_insertions"], 48 * WrapVertices(tm) * 0.95, 48 * WrapVertices(tm) + 2000 + 40);   // the last split may overshoot by its candidates
            Assert.NotNull(tm.Result);
            Assert.Null(TetMeshQuality.NotValidReason(tm.Result));
            Assert.NotNull(tm.Targets);
            Assert.False(tm.Targets.RadiusEdgeMet);                                     // the report says the target was not reached
            Assert.True(tm.Targets.TetsAboveRadiusEdge > 0);
            Assert.Equal(rho, tm.Targets.MaxRadiusEdgeRatio);
        }

        [Fact]
        public void TerminatingRuns_AreNotCut()
        {
            // 1.1 on the sphere ends with 7 trial insertions per wrap vertex: below the bound, so the same result as without it
            MeshTetrahedralization a = Make("sphere", 12, 1.1), off = Make("sphere", 12, 1.1);
            off.MaxTrialInsertionsPerWrapVertex = 0;
            Assert.True(a.Compute()); Assert.True(off.Compute());
            Assert.False(a.Counts.ContainsKey("work_limit_reached"));
            Assert.Equal(off.Result.Vertices, a.Result.Vertices);
            Assert.Equal(off.Result.Tets, a.Result.Tets);
            Assert.Equal(off.Counts, a.Counts);
            Assert.True(a.Counts["trial_insertions"] > 5 * WrapVertices(a));                  // and it did real work
        }

        [Theory]
        [InlineData("box"), InlineData("sphere"), InlineData("torus thin"), InlineData("cylinder")]
        public void DefaultPath_NeverComesNearTheBound(string shape)
        {
            foreach (double alpha in new[] { 12.0, 40.0 }) {
                MeshTetrahedralization tm = Make(shape, alpha, 2);
                Assert.True(tm.Compute());
                Assert.False(tm.Counts.ContainsKey("work_limit_reached"));
                Assert.True(tm.Counts["trial_insertions"] < 3 * WrapVertices(tm), shape + " " + tm.Counts["trial_insertions"] + " of " + WrapVertices(tm));   // 16 times below the bound
            }
        }

        [Fact]
        public void TheParameter_TurnsTheBoundOffOrTightensIt()
        {
            MeshTetrahedralization one = Make("sphere", 12, 1.0);
            one.MaxTrialInsertionsPerWrapVertex = 1;
            one.Compute();
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, one.Status);
            Assert.InRange(one.Counts["trial_insertions"], WrapVertices(one) * 0.9, WrapVertices(one) + 2000 + 40);
            // off: only MaxSteinerPoints stops it, and that is the user's own limit, not reported as the work bound
            MeshTetrahedralization off = Make("sphere", 12, 1.0);
            off.MaxTrialInsertionsPerWrapVertex = 0; off.MaxSteinerPoints = 3000;
            off.Compute();
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, off.Status);
            Assert.Equal(3000, off.Counts["tet_splits"]);
            Assert.False(off.Counts.ContainsKey("work_limit_reached"));
            // the user's lower limit wins over the bound, also unreported
            MeshTetrahedralization user = Make("sphere", 12, 1.0);
            user.MaxSteinerPoints = 500;
            user.Compute();
            Assert.Equal(500, user.Counts["tet_splits"]);
            Assert.False(user.Counts.ContainsKey("work_limit_reached"));
        }

        [Fact]
        public void WithASizeBound_TheBoundIsNotUsed()
        {
            // a size bound makes the point count follow the volume, not the wrap: far more than the bound allows, and legitimate
            MeshTetrahedralization tm = Make("box", 12, 2);
            tm.MaxCircumradius = 0.025;
            Assert.True(tm.Compute());
            Assert.Equal(MeshTetrahedralization.TetStatus.Success, tm.Status);
            Assert.True(tm.Counts["trial_insertions"] > 48 * WrapVertices(tm) + 2000, tm.Counts["trial_insertions"] + " trials");
            Assert.False(tm.Counts.ContainsKey("work_limit_reached"));
        }

        [Fact]
        public void ReuseAndMaxThreads_GiveTheSameLimitedResult()
        {
            MeshTetrahedralization tm = Make("box", 12, 1.2);
            tm.Compute();
            List<Vector3d> v = tm.Result.Vertices.ToList(); List<Index4i> t = tm.Result.Tets.ToList();
            var counts = new Dictionary<string, long>(tm.Counts);
            tm.Compute();                                                                // the same instance again
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, tm.Status);
            Assert.Equal(v, tm.Result.Vertices); Assert.Equal(t, tm.Result.Tets); Assert.Equal(counts, tm.Counts);
            MeshTetrahedralization serial = Make("box", 12, 1.2);
            serial.MaxThreads = 1;
            serial.Compute();
            Assert.Equal(v, serial.Result.Vertices); Assert.Equal(t, serial.Result.Tets); Assert.Equal(counts, serial.Counts);
            // a run without the limit after a limited one on the same instance: no stale count
            tm.MaxRadiusEdgeRatio = 2;
            Assert.True(tm.Compute());
            Assert.False(tm.Counts.ContainsKey("work_limit_reached"));
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void LimitedRun_Similarity_PowerOfTwoScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            MeshTetrahedralization a = Reference.Value;
            var sw = Stopwatch.StartNew();
            var b = new MeshTetrahedralization(Invariance.Transformed(TetMeshingStage2Tests.Input("box"), s)) {
                RelativeAlpha = 12, MaxRadiusEdgeRatio = 1.2, CancelF = () => sw.Elapsed.TotalSeconds > 6 };
            b.Compute();
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, b.Status);
            Assert.Null(TetMeshQuality.NotValidReason(b.Result));
            Assert.False(b.Targets.RadiusEdgeMet);
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            }
        }

        // fingerprints (FNV-1a over the bits of vertices, tets, boundary triangles and counts) computed on master before the work bound:
        // the default path, and radius-edge 1.5, give the same result bit for bit. Pinned on linux / .NET (libm differs in the last bit elsewhere)
        static readonly (string shape, double alpha, double rho, ulong fingerprint, int tets)[] Pinned = {
            ("box", 12, 2, 9245319534554302762UL, 1527),
            ("box", 12, 1.5, 13204798993680902105UL, 3264),
            ("box", 40, 2, 2703540905802959250UL, 8013),
            ("box", 40, 1.5, 11587509505194079974UL, 13741),
            ("sphere", 12, 2, 5602859837465130074UL, 3122),
            ("sphere", 12, 1.5, 17555507881777574014UL, 4348),
            ("sphere", 40, 2, 17776482651336125280UL, 4402),
            ("sphere", 40, 1.5, 697528970051731743UL, 5981),
            ("torus thin", 12, 2, 9763736904603033807UL, 4783),
            ("torus thin", 12, 1.5, 5651169865745693525UL, 6095),
            ("torus thin", 40, 2, 3221333998988497085UL, 5181),
            ("torus thin", 40, 1.5, 8192362481484667581UL, 6726),
            ("cylinder", 12, 2, 13371677604216505104UL, 3733),
            ("cylinder", 12, 1.5, 15683790641454705102UL, 6553),
            ("cylinder", 40, 2, 8661470643332112124UL, 7934),
            ("cylinder", 40, 1.5, 9464946764192240814UL, 11263)
        };

        static ulong Fingerprint(MeshTetrahedralization tm)
        {
            ulong h = 14695981039346656037UL;
            Action<ulong> add = x => { for (int i = 0; i < 8; ++i) { h ^= (x >> (8 * i)) & 0xFF; h *= 1099511628211UL; } };
            TetMesh3 t = tm.Result;
            foreach (Vector3d v in t.Vertices) { add((ulong)BitConverter.DoubleToInt64Bits(v.x)); add((ulong)BitConverter.DoubleToInt64Bits(v.y)); add((ulong)BitConverter.DoubleToInt64Bits(v.z)); }
            foreach (Index4i q in t.Tets) { add((ulong)q.a); add((ulong)q.b); add((ulong)q.c); add((ulong)q.d); }
            foreach (Index3i f in t.BoundaryTriangles) { add((ulong)f.a); add((ulong)f.b); add((ulong)f.c); }
            foreach (var kv in tm.Counts.OrderBy(k => k.Key, StringComparer.Ordinal)) { foreach (char ch in kv.Key) add(ch); add((ulong)kv.Value); }
            return h;
        }

        [Fact]
        public void DefaultPathAndRadiusEdge15_AreBitForBitTheSameAsBeforeTheBound()
        {
            var fw = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) ||
                !fw.StartsWith(".NET ", StringComparison.Ordinal) || fw.StartsWith(".NET Framework", StringComparison.Ordinal)) return;
            foreach (var p in Pinned) {
                var tm = new MeshTetrahedralization(TetMeshingStage2Tests.Input(p.shape)) { RelativeAlpha = p.alpha, MaxRadiusEdgeRatio = p.rho };
                Assert.True(tm.Compute());
                Assert.False(tm.Counts.ContainsKey("work_limit_reached"));
                Assert.Equal(p.tets, tm.Result.Tets.Count);
                Assert.Equal(p.fingerprint, Fingerprint(tm));
            }
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        static readonly Lazy<MeshTetrahedralization> Reference = new Lazy<MeshTetrahedralization>(() => {
            MeshTetrahedralization tm = Make("box", 12, 1.2); tm.Compute(); return tm;
        });
    }
}
