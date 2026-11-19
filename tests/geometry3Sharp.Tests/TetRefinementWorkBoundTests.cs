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
    /// The work bound of the tet refinement (docs/design/TET_MESHING.md section 17.10, issue #1138): a refinement that does not end
    /// (MaxRadiusEdgeRatio near 1) stops with status PointLimitReached, a valid result and Counts["work_limit_reached"], never on the
    /// default path. This file only uses what the library had before the bound (the default value of the bound applies), and every run has
    /// its own stopwatch as CancelF, so the old code, which does not stop, fails these tests as status Cancelled instead of hanging.
    /// The tests that need the new field are in TetRefinementWorkBoundParameterTests.
    /// </summary>
    public class TetRefinementWorkBoundTests : IClassFixture<TetRefinementWorkBoundTests.ReferenceRun>
    {
        readonly ReferenceRun fixture;
        public TetRefinementWorkBoundTests(ReferenceRun f) { fixture = f; }

        /// <summary> the untransformed limited run, built once before the tests (deterministic) </summary>
        public sealed class ReferenceRun
        {
            public readonly MeshTetrahedralization Tm = Make("sphere", 12, 1.0);
            public ReferenceRun() { Go(Tm); }
        }

        internal static MeshTetrahedralization Make(string shape, double alpha, double rho)
        {
            // the work-bound tests target the refinement; the rescue phase of stage 2 (#1072) would add seconds to a run that does not converge
            return new MeshTetrahedralization(TetMeshingStage2Tests.Input(shape)) { RelativeAlpha = alpha, MaxRadiusEdgeRatio = rho, RescuePasses = 0 };
        }

        // Compute with a stopwatch of its own (15 s per call: the new code needs 1 to 2 s, also under the load of parallel test classes; the old code runs into it)
        internal static bool Go(MeshTetrahedralization tm)
        {
            var sw = Stopwatch.StartNew();
            tm.CancelF = () => sw.Elapsed.TotalSeconds > 15;
            return tm.Compute();
        }

        internal static int WrapVertices(MeshTetrahedralization tm) => tm.Wrap.Triangulation.VertexCount;

        // the runs that do not end: 370 to 1400 trial insertions per wrap vertex within 25 s and rising
        public static IEnumerable<object[]> Runaways() => new[] { new object[] { "sphere", 12.0, 1.0 }, new object[] { "box", 12.0, 1.2 }, new object[] { "cylinder", 12.0, 1.3 } };

        [Theory]
        [MemberData(nameof(Runaways))]
        public void RunawayRefinement_StopsAtTheBound_WithAValidResultAndTheTargetNotMet(string shape, double alpha, double rho)
        {
            MeshTetrahedralization tm = Make(shape, alpha, rho);
            Go(tm);
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, tm.Status);
            Assert.Equal(1, tm.Counts["work_limit_reached"]);
            // the default bound is 48 trial insertions per wrap vertex + 2000, checked before each split (one split adds at most 30)
            Assert.InRange(tm.Counts["trial_insertions"], 48 * WrapVertices(tm) + 2000, 48 * WrapVertices(tm) + 2000 + 30);
            Assert.NotNull(tm.Result);
            Assert.Null(TetMeshQuality.NotValidReason(tm.Result));
            Assert.NotNull(tm.Targets);
            Assert.False(tm.Targets.RadiusEdgeMet);                                     // the report says the target was not reached
            Assert.True(tm.Targets.TetsAboveRadiusEdge > 0);
            Assert.Equal(rho, tm.Targets.MaxRadiusEdgeRatio);
        }

        // the default path and ratio 1.5 on every input at two resolutions: not near the bound, and bit for bit the result of master before
        // the bound (fingerprints computed there; pinned on linux x64 / .NET, where libm gives the same last bits)
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

        static bool PinnedPlatform()
        {
                        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64
                && System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET ", StringComparison.Ordinal) && !System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("box"), InlineData("sphere"), InlineData("torus thin"), InlineData("cylinder")]
        public void DefaultPathAndRadiusEdge15_AreFarFromTheBound_AndBitForBitTheSameAsBeforeIt(string shape)
        {
            foreach (var p in Pinned.Where(x => x.shape == shape)) {
                var tm = Make(shape, p.alpha, p.rho);
                Assert.True(Go(tm));
                Assert.False(tm.Counts.ContainsKey("work_limit_reached"));
                if (p.rho == 2) Assert.True(tm.Counts["trial_insertions"] < 3 * WrapVertices(tm), shape + ": " + tm.Counts["trial_insertions"] + " of " + WrapVertices(tm));   // 16 times below the bound
                if (!PinnedPlatform()) continue;
                Assert.Equal(p.tets, tm.Result.Tets.Count);
                Assert.Equal(p.fingerprint, Fingerprint(tm));
            }
        }

        [Fact]
        public void WithASizeBound_TheBoundIsNotUsed()
        {
            // a size bound makes the point count follow the volume, not the wrap: far more than the bound allows, and legitimate
            MeshTetrahedralization tm = Make("box", 12, 2);
            tm.MaxCircumradius = 0.025;
            Assert.True(Go(tm));
            Assert.Equal(MeshTetrahedralization.TetStatus.Success, tm.Status);
            Assert.True(tm.Counts["trial_insertions"] > 48 * WrapVertices(tm) + 2000, tm.Counts["trial_insertions"] + " trials");
            Assert.False(tm.Counts.ContainsKey("work_limit_reached"));
        }

        [Fact]
        public void Reuse_GivesTheSameLimitedResult()
        {
            MeshTetrahedralization tm = Make("sphere", 12, 1.0);
            Go(tm);
            List<Vector3d> v = tm.Result.Vertices.ToList(); List<Index4i> t = tm.Result.Tets.ToList();
            var counts = new Dictionary<string, long>(tm.Counts);
            Go(tm);                                                                      // the same instance again
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, tm.Status);
            Assert.Equal(v, tm.Result.Vertices); Assert.Equal(t, tm.Result.Tets); Assert.Equal(counts, tm.Counts);
            // a run that ends after a limited one on the same instance: no stale count, status or targets
            tm.MaxRadiusEdgeRatio = 2;
            Assert.True(Go(tm));
            Assert.Equal(MeshTetrahedralization.TetStatus.Success, tm.Status);
            Assert.False(tm.Counts.ContainsKey("work_limit_reached"));
        }

        [Fact]
        public void EveryMaxThreads_GivesTheSameLimitedResult()
        {
            MeshTetrahedralization serial = Make("sphere", 12, 1.0);
            serial.MaxThreads = 1;
            Go(serial);
            MeshTetrahedralization a = fixture.Tm;
            Assert.Equal(a.Result.Vertices, serial.Result.Vertices); Assert.Equal(a.Result.Tets, serial.Result.Tets); Assert.Equal(a.Counts, serial.Counts);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void LimitedRun_Similarity_PowerOfTwoScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            MeshTetrahedralization a = fixture.Tm;
            var b = new MeshTetrahedralization(Invariance.Transformed(TetMeshingStage2Tests.Input("sphere"), s)) { RelativeAlpha = 12, MaxRadiusEdgeRatio = 1.0, RescuePasses = 0 };
            Go(b);
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, b.Status);
            Assert.Null(TetMeshQuality.NotValidReason(b.Result));
            Assert.False(b.Targets.RadiusEdgeMet);
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            }
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();
    }
}
