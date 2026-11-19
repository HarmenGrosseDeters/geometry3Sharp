using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshTetrahedralization.MaxDihedralDegrees (stage 2, opt-in objective on both ends, Klingner's thesis ch. 3-5): off (0) is the
    /// stage as it was, bit for bit; on, the improver also works on tets with a largest dihedral above the value, keeps every
    /// invariant of stage 2, and no operation trades the max side for a tet below the floor (the guard).
    /// </summary>
    public class TetMeshingMaxDihedralTests
    {
        const double G = 150;

        static int CountAbove(TetMesh3 m, double g)
        {
            int n = 0;
            foreach (Index4i t in m.Tets) {
                bool ok = TetMeshQuality.Measure(m.Vertices[t.a], m.Vertices[t.b], m.Vertices[t.c], m.Vertices[t.d], out _, out _, out double mx);
                if (!ok || mx > g) n++;
            }
            return n;
        }
        static double WorstMin(TetMesh3 m) => TetMeshQuality.Measure(m).MinDihedral.Worst;
        static int CountBelow(TetMesh3 m, double f) => m.Tets.Count(t => {
            bool ok = TetMeshQuality.Measure(m.Vertices[t.a], m.Vertices[t.b], m.Vertices[t.c], m.Vertices[t.d], out _, out double mn, out _);
            return !ok || mn < f;
        });

        static MeshTetrahedralization Run(DMesh3 input, double maxDihedral, bool guard = true, bool staged = true, double alpha = 40, double offset = 600, bool improve = true)
        {
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = alpha, RelativeOffset = offset, MaxDihedralDegrees = maxDihedral, MaxDihedralGuard = guard, MaxDihedralStaged = staged, Improve = improve };
            Assert.True(tm.Compute(), tm.Status.ToString());
            return tm;
        }

        static void AssertSame(TetMesh3 a, TetMesh3 b)
        {
            Assert.Equal(a.Vertices, b.Vertices); Assert.Equal(a.Tets, b.Tets); Assert.Equal(a.BoundaryTriangles, b.BoundaryTriangles);
        }

        // ---- the objective in the improver, on a mesh small enough to read ----

        // three tets around the edge (d, e) through an equilateral triangle: 38.7 degrees smallest, the three dihedral angles at the
        // shared edge are 120 each
        static TetMesh3 ThreeAroundAnEdge()
        {
            double s = Math.Sqrt(3) / 2;
            var v = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, s, 0),
                                         new Vector3d(0.5, s / 3, -0.6), new Vector3d(0.5, s / 3, 0.6) };
            var t = new List<Index4i> { new Index4i(1, 0, 4, 3), new Index4i(2, 1, 4, 3), new Index4i(0, 2, 4, 3) };
            return TetMeshingStage2Tests.FromTets(v, t);
        }

        [Fact]
        public void Objective_RemovesAnEdgeWhoseTetsAreTooOpen_WhichTheSmallestAngleAloneLeaves()
        {
            TetMesh3 m = ThreeAroundAnEdge();
            Assert.Equal(3, CountAbove(m, 110));
            Assert.True(WorstMin(m) > 38);
            // off: every tet is above the floor, nothing to do
            var off = new TetMeshImprover(m) { TargetDegrees = 10 };
            off.Run();
            Assert.Equal(0, off.Counts["edge_removals"]);
            Assert.Equal(3, off.Result().Tets.Count);
            // on: the 120 degree edge goes (the 3-2 flip), two tets of 64 degrees smallest remain
            var on = new TetMeshImprover(m) { TargetDegrees = 10, MaxDihedralDegrees = 110 };
            on.Run();
            TetMesh3 r = on.Result();
            Assert.Null(TetMeshQuality.NotValidReason(r));
            Assert.Equal(1, on.Counts["edge_removals"]);
            Assert.Equal(2, r.Tets.Count);
            Assert.Equal(0, CountAbove(r, 110));
            Assert.Equal(m.BoundaryTriangles.Count, r.BoundaryTriangles.Count);
        }

        [Fact]
        public void Objective_BelowZero_StillImproves()
        {
            // with a maximum this low the objective of valid tets is negative (10 + 71 - 120 = -39 before, higher after: the flip's two
            // tets have a smaller largest dihedral); a negative value is not the quality of an inverted tet
            TetMesh3 m = ThreeAroundAnEdge();
            var on = new TetMeshImprover(m) { TargetDegrees = 10, MaxDihedralDegrees = 71 };
            on.Run();
            TetMesh3 r = on.Result();
            Assert.Null(TetMeshQuality.NotValidReason(r));
            Assert.Equal(1, on.Counts["edge_removals"]);
            Assert.Equal(2, r.Tets.Count);
            double after = TetMeshQuality.Measure(r).MaxDihedral.Worst;
            Assert.True(after < 119, after.ToString());
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.Epsilon)]
        [InlineData(60.0)]
        [InlineData(70.5)]
        [InlineData(180.0)]
        [InlineData(1e9)]
        public void ObjectiveOff_ForZeroNegativeNonFiniteImpossibleAndTooLarge_IsTheImproverAsItWas(double g)
        {
            TetMesh3 m = ThreeAroundAnEdge();
            var a = new TetMeshImprover(m) { TargetDegrees = 60 }; a.Run();
            var b = new TetMeshImprover(m) { TargetDegrees = 60, MaxDihedralDegrees = g }; b.Run();
            AssertSame(a.Result(), b.Result());
            Assert.Equal(a.Counts, b.Counts);
        }

        // ---- the pipeline ----

        [Theory]
        [InlineData(0.0, false)] [InlineData(-1.0, false)] [InlineData(double.NaN, false)] [InlineData(double.PositiveInfinity, false)]
        [InlineData(double.NegativeInfinity, false)] [InlineData(double.Epsilon, false)] [InlineData(60.0, false)] [InlineData(70.5, false)]
        [InlineData(179.99, true)] [InlineData(180.0, false)] [InlineData(1e300, false)] [InlineData(70.6, true)] [InlineData(150.0, true)]
        public void ObjectiveActive_IsTheRangeWhereATetCanMissAndMeetTheMaximum(double g, bool on)
        {
            Assert.Equal(on, TetMeshImprover.ObjectiveActive(g));
        }

        [Fact]
        public void ImpossibleMaximum_IsOffAndFast_NotMinutesOfWorkOnEveryTet()
        {
            // below 70.53 degrees (the regular tet's largest dihedral) every tet would miss the target on every pass: off, so the run
            // takes as long as without the option (a Compute() of the sphere did not finish in 120 s before)
            DMesh3 input = TetMeshingStage2Tests.Input("sphere");
            var plain = Run(input, 0);
            foreach (double g in new[] { 60.0, double.Epsilon, 70.5 }) {
                var tm = Run(input, g);
                AssertSame(plain.Result, tm.Result);
                Assert.Equal(plain.Counts, tm.Counts);
            }
        }

        [Theory]
        [MemberData(nameof(TetMeshingStage2Tests.Inputs), MemberType = typeof(TetMeshingStage2Tests))]
        public void Off_IsBitForBitTheDefault_ForEveryOffValue(string name)
        {
            DMesh3 input = TetMeshingStage2Tests.Input(name);
            var plain = new MeshTetrahedralization(input) { RelativeAlpha = 40, RelativeOffset = 600 }; Assert.True(plain.Compute());
            foreach (double off in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 60, 180, 1e9 }) {
                var tm = Run(input, off);
                AssertSame(plain.Result, tm.Result);
                Assert.Equal(plain.Counts, tm.Counts);
            }
        }

        [Theory]
        [MemberData(nameof(TetMeshingStage2Tests.Inputs), MemberType = typeof(TetMeshingStage2Tests))]
        public void On_KeepsTheInvariants_AndLeavesFewerTetsAboveTheMaximum(string name)
        {
            DMesh3 input = TetMeshingStage2Tests.Input(name);
            var plain = Run(input, 0);
            var on = Run(input, G);
            TetMeshingStage2Tests.Invariants(on);                      // valid, closed, no boundary self-intersection, the input inside, the slide cap
            int before = CountAbove(plain.Result, G), after = CountAbove(on.Result, G);
            // the sample measured (benchmarks/TetMeshBenchmark/README.md): master leaves hundreds above 150 on every mesh, the option nearly none
            Assert.True(before > 20, name + ": master leaves " + before);
            Assert.True(after * 4 < before, name + ": " + before + " -> " + after);
            Assert.True(on.Counts["improve_edge_removals"] > plain.Counts["improve_edge_removals"]);
            // the guard: the smallest dihedral does not fall below min(floor, what the pipeline without the option reached)
            Assert.True(WorstMin(on.Result) >= Math.Min(10, WorstMin(plain.Result)) - 1e-9, name + ": " + WorstMin(on.Result) + " vs " + WorstMin(plain.Result));
            Assert.True(CountBelow(on.Result, 10) <= CountBelow(plain.Result, 10), name);
        }

        [Fact]
        public void Guard_StopsTradesOfTheMaxSideForTetsBelowTheFloor()
        {
            // the thin torus is where the measurement lost meshes on the min side: without the guard a tet falls below the floor that the
            // master pipeline reaches
            DMesh3 input = TetMeshingStage2Tests.Input("torus thin");
            var plain = Run(input, 0);
            var guarded = Run(input, G, staged: false);
            var unguarded = Run(input, G, guard: false, staged: false);
            double floor = Math.Min(10, WorstMin(plain.Result));
            Assert.True(WorstMin(guarded.Result) >= floor - 1e-9, WorstMin(guarded.Result) + " vs " + floor);
            Assert.True(WorstMin(unguarded.Result) < floor - 1e-9, "the unguarded objective keeps the floor here: " + WorstMin(unguarded.Result));
            // every operation's guard fires on this mesh (the counts of candidates it turned down); none without the guard, or without the option
            foreach (string op in new[] { "smooth", "boundary_move", "contract", "edge_removal", "face_removal", "split" }) {
                Assert.True(guarded.Counts.TryGetValue("improve_guard_trips_" + op, out long n) && n > 0, op);
                Assert.False(unguarded.Counts.ContainsKey("improve_guard_trips_" + op), op);
                Assert.False(plain.Counts.ContainsKey("improve_guard_trips_" + op), op);
            }
        }

        [Theory]
        [MemberData(nameof(TetMeshingStage2Tests.Inputs), MemberType = typeof(TetMeshingStage2Tests))]
        public void Staged_RunsThePlainPhaseFirst_SoTheMinSideIsAsWithoutTheOption(string name)
        {
            DMesh3 input = TetMeshingStage2Tests.Input(name);
            var plain = Run(input, 0);
            var staged = Run(input, G);
            var direct = Run(input, G, staged: false);
            Assert.True(staged.Counts["improve_passes"] > direct.Counts["improve_passes"] || staged.Counts["improve_edge_removals"] != direct.Counts["improve_edge_removals"], name);
            Assert.True(WorstMin(staged.Result) >= Math.Min(10, WorstMin(plain.Result)) - 1e-9, name);
        }

        [Fact]
        public void LowAttainableMaximum_StopsThroughTheStallStop_AndKeepsTheInvariantsAndTheMinSide()
        {
            // a maximum the shape cannot meet keeps every tet bad; the passes then stop when they stall (not on time), Success is kept
            DMesh3 input = TetMeshingStage2Tests.Torus(48, 12, 1, 0.15);
            var plain = Run(input, 0, alpha: 12, offset: 200);
            var low = Run(input, 100, alpha: 12, offset: 200);
            Assert.Equal(1, low.Counts["improve_maxdih_stall_stop"]);
            TetMeshingStage2Tests.Invariants(low);
            Assert.True(WorstMin(low.Result) >= Math.Min(10, WorstMin(plain.Result)) - 1e-9, WorstMin(low.Result) + " vs " + WorstMin(plain.Result));
            double maxLow = TetMeshQuality.Measure(low.Result).MaxDihedral.Worst, maxPlain = TetMeshQuality.Measure(plain.Result).MaxDihedral.Worst;
            Assert.True(maxLow < maxPlain, maxLow + " vs " + maxPlain);           // it did work before stalling: the worst largest angle fell
            Assert.True(low.Counts["improve_passes"] < 40, low.Counts["improve_passes"].ToString());
            // deterministic: the same stop twice
            AssertSame(low.Result, Run(input, 100, alpha: 12, offset: 200).Result);
        }

        [Fact]
        public void StallStop_IsNotTakenWhereTheTargetIsMet()
        {
            foreach (string name in new[] { "sphere", "box", "cylinder", "torus thin" })
                Assert.False(Run(TetMeshingStage2Tests.Input(name), G).Counts.ContainsKey("improve_maxdih_stall_stop"), name);
        }

        [Fact]
        public void On_ReuseAndRepeatedRuns_GiveTheSameTets()
        {
            DMesh3 input = TetMeshingStage2Tests.Input("torus thin");
            var a = Run(input, G);
            TetMesh3 r1 = a.Result;
            Assert.True(a.Compute());
            var b = Run(input, G);
            Assert.True(a.Counts["improve_edge_removals"] > 0);
            foreach (TetMesh3 r in new[] { a.Result, b.Result }) AssertSame(r1, r);
        }

        [Fact]
        public void On_IsTheSameForEveryThreadCount()
        {
            // no parallel path in stage 2; the carving of the wrap before it is parallel and must stay without effect
            DMesh3 input = TetMeshingStage2Tests.Input("cylinder");
            var one = new MeshTetrahedralization(input) { RelativeAlpha = 40, MaxDihedralDegrees = G, MaxThreads = 1 }; Assert.True(one.Compute());
            var all = new MeshTetrahedralization(input) { RelativeAlpha = 40, MaxDihedralDegrees = G, MaxThreads = 0 }; Assert.True(all.Compute());
            AssertSame(one.Result, all.Result);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void On_Similarity_ValidAndPow2ScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = TetMeshingStage2Tests.Torus(48, 12, 1, 0.15);
            var (a, plain) = SimilarityBase.Value;
            var b = new MeshTetrahedralization(Invariance.Transformed(input, s)) { RelativeAlpha = 12, RelativeOffset = 200, MaxDihedralDegrees = G }; Assert.True(b.Compute());
            TetMeshingStage2Tests.Invariants(b);
            Assert.True(CountAbove(plain.Result, G) > CountAbove(a.Result, G), CountAbove(plain.Result, G) + " -> " + CountAbove(a.Result, G));   // the option does work here
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            } else {
                Assert.InRange(TetMeshQuality.Measure(b.Result).MinDihedral.Worst, 0.5 * TetMeshQuality.Measure(a.Result).MinDihedral.Worst, 90);
                // and the max side improves under the transform too (the stage-1 mesh is transformed with the input; tets above G afterwards)
                var plainB = new MeshTetrahedralization(Invariance.Transformed(input, s)) { RelativeAlpha = 12, RelativeOffset = 200 }; Assert.True(plainB.Compute());
                Assert.True(CountAbove(b.Result, G) * 2 < CountAbove(plainB.Result, G), CountAbove(plainB.Result, G) + " -> " + CountAbove(b.Result, G));
            }
        }
        // the untransformed runs, once for every similarity
        static readonly Lazy<(MeshTetrahedralization on, MeshTetrahedralization plain)> SimilarityBase = new Lazy<(MeshTetrahedralization, MeshTetrahedralization)>(() => {
            DMesh3 input = TetMeshingStage2Tests.Torus(48, 12, 1, 0.15);
            var a = new MeshTetrahedralization(input) { RelativeAlpha = 12, RelativeOffset = 200, MaxDihedralDegrees = G }; Assert.True(a.Compute());
            var plain = new MeshTetrahedralization(input) { RelativeAlpha = 12, RelativeOffset = 200 }; Assert.True(plain.Compute());
            return (a, plain);
        });
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();
    }
}
