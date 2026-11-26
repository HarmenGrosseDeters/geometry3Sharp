using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Edit = g3.MeshRoundingRepair.ScriptEdit;
using Kind = g3.MeshRoundingRepair.ScriptKind;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The joint move of MeshRoundingRepair (#1465, docs/research/ROUNDING_DEGENERATE_LEFT_2026-11.md): the last resort for a degenerate
    /// triangle that no other edit removes. Two of its free corners move together (each by its room over 2^j along one of the 26
    /// directions), accepted by the same exact check as every edit. The reproducer is a plate minus a finer copy of it turned by 2e-9:
    /// the mouth of a thin double layer at the corner of the plate, a triangle with an edge of 3.6e-10 and two of 1e-7, one corner of it
    /// fixed (an input vertex). No single corner can lift it (the area needed is 2e-12, a corner moved by the whole bound, 1.4e-5, gains
    /// 5e-16), two corners moved together can.
    /// </summary>
    public class MeshRoundingRepairTestsJointMove
    {
        const ExactMeshBoolean.Operations Difference = ExactMeshBoolean.Operations.Difference;

        // (correctly rounded sin and cos, the CORE-MATH port: the platform's Math.Sin / Cos differ by an ulp on some arguments on some C
        // runtimes, and the counts these tests pin depend on the bits of the inputs)
        static double Sn(double x) { return g3.CoreMathPort.CoreMath.Sin(x); }
        static double Cs(double x) { return g3.CoreMathPort.CoreMath.Cos(x); }

        static double Wave(double x, double y) { return 0.05 * Sn(3 * x + 1) * Cs(2 * y); }

        static readonly Vector3d Center = new Vector3d(0.5, 0.5, 0);

        // a curved plate: the surface z = h(x, y) over the unit square on an n by n grid, the thickness t along z, closed by side walls
        static DMesh3 Plate(int n, double t, Func<Vector3d, Vector3d> map = null, Func<double, double, double> h = null)
        {
            h = h ?? Wave;
            var m = new DMesh3();
            int N = n + 1;
            var top = new int[N, N];
            var bot = new int[N, N];
            for (int i = 0; i < N; ++i)
                for (int j = 0; j < N; ++j) {
                    double x = (double)i / n, y = (double)j / n, z = h(x, y);
                    Vector3d a = new Vector3d(x, y, z + t / 2), b = new Vector3d(x, y, z - t / 2);
                    top[i, j] = m.AppendVertex(map != null ? map(a) : a);
                    bot[i, j] = m.AppendVertex(map != null ? map(b) : b);
                }
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    m.AppendTriangle(top[i, j], top[i + 1, j], top[i + 1, j + 1]);
                    m.AppendTriangle(top[i, j], top[i + 1, j + 1], top[i, j + 1]);
                    m.AppendTriangle(bot[i, j], bot[i + 1, j + 1], bot[i + 1, j]);
                    m.AppendTriangle(bot[i, j], bot[i, j + 1], bot[i + 1, j + 1]);
                }
            Action<int, int, int, int> wall = (a0, a1, b0, b1) => { m.AppendTriangle(a0, b0, b1); m.AppendTriangle(a0, b1, a1); };
            for (int i = 0; i < n; ++i) {
                wall(top[i, 0], top[i + 1, 0], bot[i, 0], bot[i + 1, 0]);
                wall(top[i + 1, n], top[i, n], bot[i + 1, n], bot[i, n]);
                wall(top[0, i + 1], top[0, i], bot[0, i + 1], bot[0, i]);
                wall(top[n, i], top[n, i + 1], bot[n, i], bot[n, i + 1]);
            }
            return m;
        }

        // a rotation by ax about the x axis, then by ay about the y axis, about Center
        static Vector3d Rotated(Vector3d p, double ax, double ay)
        {
            Vector3d q = p - Center;
            double co = Cs(ax), si = Sn(ax);
            q = new Vector3d(q.x, co * q.y - si * q.z, si * q.y + co * q.z);
            co = Cs(ay);
            si = Sn(ay);
            q = new Vector3d(co * q.x + si * q.z, q.y, -si * q.x + co * q.z);
            return Center + q;
        }

        // the plate on 1 by 1 cells, minus the one on 3 by 3, turned by 2e-9 about x and -2.2e-9 about y (12 + 60 triangles)
        static void Inputs(out DMesh3 a, out DMesh3 b, Similarity s = null)
        {
            const double e = 2e-9;
            a = Plate(1, 0.2);
            b = Plate(3, 0.2, p => Rotated(p, e, -1.1 * e));
            if (s != null) {
                a = Invariance.Transformed(a, s);
                b = Invariance.Transformed(b, s);
            }
        }

        static ExactMeshBoolean Run(bool joint, bool strict = false, Similarity s = null, bool serial = false)
        {
            Inputs(out DMesh3 a, out DMesh3 b, s);
            var op = new ExactMeshBoolean(a, b, Difference) { StrictRounding = strict };
            MeshRoundingRepair.NoJointSearchOnThisThread = joint == false;
            gParallel.SerialOnThisThread = serial;
            try {
                op.Compute();
            } finally {
                MeshRoundingRepair.NoJointSearchOnThisThread = false;
                gParallel.SerialOnThisThread = false;
            }
            return op;
        }

        static void AssertClean(ExactMeshBoolean op, string what = "")
        {
            DMesh3 m = op.Result;
            Assert.NotNull(m);
            // (the result touches itself at one contact, which NotCleanReason reports as "self-intersections": the same with and without the
            // joint search; no other defect)
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m, false));
            Assert.Equal(MeshRobustRepair.Defects.SelfIntersections, MeshRobustRepair.FindDefects(m));
            Assert.Equal(1, op.Contacts);
            Assert.True(m.IsClosed(), what);
            Assert.True(m.CheckValidity(eFailMode: FailMode.ReturnOnly), what);
            Assert.True(Invariance.AllFinite(m), what);
            Assert.True(op.Rounding.IsClean, what);
        }

        [Fact]
        public void Reproducer_WithoutJointSearch_OneDegenerateTriangleLeft()
        {
            // the state of #1465: no edit of the repair removes the triangle (the old behaviour, the joint search switched off)
            ExactMeshBoolean op = Run(joint: false, strict: true);
            Assert.False(op.Status == ExactMeshBoolean.BooleanStatus.Success);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, op.Status);
            MeshRoundingRepair r = op.Rounding;
            Assert.Equal(1, r.RemainingDegenerate);
            Assert.Equal(0, r.RemainingIntersections);
            Assert.Equal(0, r.JointMoves);
            Assert.Equal(1, Run(joint: false).Contacts);
            Assert.Equal(0, r.JointSearchProposals);
        }

        [Fact]
        public void Reproducer_JointSearch_CleanWithinTheBound_TwoVerticesMoved()
        {
            ExactMeshBoolean old = Run(joint: false), op = Run(joint: true, strict: true);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            MeshRoundingRepair r = op.Rounding;
            AssertClean(op);
            Assert.Equal(1, r.JointMoves);
            Assert.True(r.JointSearchProposals >= 1);
            // (counted into Moves; the joint move is the last edit)
            Assert.Equal(old.Rounding.Moves + 1, r.Moves);
            Assert.Equal(old.Rounding.PositionSearchMoves, r.PositionSearchMoves);
            Assert.Equal(old.Rounding.Collapses, r.Collapses);
            Assert.Equal(old.Rounding.Flips, r.Flips);
            Assert.Equal(old.Rounding.RemovedComponents, r.RemovedComponents);
            Assert.True(r.MaxDrift <= r.MaxLength, $"{r.MaxDrift:R} {r.MaxLength:R}");
            // the same mesh as without it, but for the two corners of the triangle that was left: no other vertex moves, no triangle changes
            DMesh3 x = old.Result, y = op.Result;
            Assert.Equal(x.TriangleCount, y.TriangleCount);
            foreach (int t in x.TriangleIndices())
                Assert.Equal(x.GetTriangle(t), y.GetTriangle(t));
            List<int> moved = x.VertexIndices().Where(v => x.GetVertex(v) != y.GetVertex(v)).ToList();
            Assert.Equal(2, moved.Count);
            int degenerate = Assert.Single(x.TriangleIndices().Where(t => x.GetTriArea(t) < 1e-12 * MeshRepairMetrics.TriangleBounds(x).DiagonalLength * MeshRepairMetrics.TriangleBounds(x).DiagonalLength));
            Index3i tv = x.GetTriangle(degenerate);
            foreach (int v in moved) {
                Assert.True(v == tv.a || v == tv.b || v == tv.c);
                double far = x.GetVertex(v).Distance(y.GetVertex(v));
                Assert.True(far > 0 && far <= r.MaxLength, $"moved by {far:R}, bound {r.MaxLength:R}");
            }
            // the sizes: the same j for both, the room over 2^j (the room as the repair without the joint move left it), the first level that
            // lifts the triangle; in the first direction pair that works (3 proposals reached improves, 2 of them refused)
            double[] steps = moved.Select(v => x.GetVertex(v).Distance(y.GetVertex(v)) / old.Rounding.room_at(v)).ToArray();
            Assert.Equal(steps[0], steps[1], 6);
            Assert.Equal(-2, Math.Round(Math.Log(steps[0], 2)));
            Assert.Equal(Math.Round(Math.Log(steps[0], 2)), Math.Log(steps[0], 2), 6);
            Assert.Equal(3, r.JointSearchProposals);
            // the one that is not moved is the fixed corner of the triangle: an input vertex
            Assert.Equal(1, new[] { tv.a, tv.b, tv.c }.Count(v => moved.Contains(v) == false));
            // the same solid within the drift: the volume differs by at most the drift times the area (the probes of BooleanOracle.SameSolid
            // are not used: the result has two faces 4e-10 apart at the corner, nearer than the probe distance)
            double dv = Math.Abs(Invariance.Volume(x) - Invariance.Volume(y));
            Assert.True(dv > 0 && dv <= r.MaxDrift * Invariance.Area(y), $"volume differs by {dv:R}, drift {r.MaxDrift:R}");
        }

        [Fact]
        public void Reproducer_CheckMode_QueriesBoundsAndCanonicalIdsAgree()
        {
            // check mode: every near query against a scan, every bound from the records, the canonical ids from the dictionary (each throws)
            ExactMeshBoolean reference = Run(joint: true);
            MeshRoundingRepair.CheckOnThisThread = true;
            ExactMeshBoolean op;
            try {
                op = Run(joint: true, strict: true);
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            Assert.Equal(1, op.Rounding.JointMoves);
            Assert.Equal("", Invariance.Difference(reference.Result, op.Result));
        }

        [Fact]
        public void Reproducer_NotStrict_SameAsStrict()
        {
            ExactMeshBoolean strict = Run(joint: true, strict: true), plain = Run(joint: true);
            AssertClean(plain);
            Assert.Equal("", Invariance.Difference(strict.Result, plain.Result));
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Reproducer_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            ExactMeshBoolean reference = Run(joint: true), op = Run(joint: true, s: s);
            if (s.IsExact) {
                // a power-of-two scale: the same edits and the same mesh, bit for bit
                AssertClean(op, s.Name);
                Assert.Equal(1, op.Rounding.JointMoves);
                Assert.Equal(reference.Rounding.JointSearchProposals, op.Rounding.JointSearchProposals);
                Assert.Equal(reference.Rounding.MaxDrift * s.Scale, op.Rounding.MaxDrift);
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(op.Result, s)));
                return;
            }
            // another frame rounds the two plates differently: not this state. Whatever it is, a result that is claimed clean is clean, and the
            // joint search made no move that left a defect
            Assert.True(op.Result == null || op.Rounding.IsClean || op.Rounding.RemainingDegenerate + op.Rounding.RemainingIntersections > 0, s.Name);
            if (op.Rounding.IsClean) {
                Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result, false));
                Assert.True(op.Rounding.MaxDrift <= op.Rounding.MaxLength, s.Name);
            }
        }

        [Fact]
        public void Reproducer_ReusedInstance_SameAsFresh()
        {
            Inputs(out DMesh3 a, out DMesh3 b);
            var op = new ExactMeshBoolean(a, b, Difference);
            op.Compute();
            DMesh3 first = new DMesh3(op.Result);
            int joint = op.Rounding.JointMoves;
            op.Compute();
            Assert.Equal(1, joint);
            Assert.Equal(1, op.Rounding.JointMoves);
            Assert.Equal("", Invariance.Difference(first, op.Result));
            Assert.Equal("", Invariance.Difference(Run(joint: true).Result, op.Result));
        }

        [Fact]
        public void Reproducer_SerialEqualsParallel_Repeated()
        {
            ExactMeshBoolean serial = Run(joint: true, serial: true);
            AssertClean(serial);
            for (int i = 0; i < 3; ++i) {
                ExactMeshBoolean parallel = Run(joint: true);
                Assert.Equal("", Invariance.Difference(serial.Result, parallel.Result));
                Assert.Equal(serial.Rounding.JointSearchProposals, parallel.Rounding.JointSearchProposals);
                Assert.Equal(serial.Rounding.MaxDrift, parallel.Rounding.MaxDrift);
            }
        }

        [Fact]
        public void CleanResults_AreNotChangedByTheJointSearch()
        {
            // a result without a degenerate triangle is the same with the search switched on or off: it is the last stage, after everything else
            DMesh3 a = Plate(1, 0.2), b = Plate(3, 0.2, p => Rotated(p, 2e-5, -2.2e-5));
            var on = new ExactMeshBoolean(a, b, Difference);
            Assert.True(on.Compute());
            DMesh3 a2 = Plate(1, 0.2), b2 = Plate(3, 0.2, p => Rotated(p, 2e-5, -2.2e-5));
            var off = new ExactMeshBoolean(a2, b2, Difference);
            MeshRoundingRepair.NoJointSearchOnThisThread = true;
            try {
                Assert.True(off.Compute());
            } finally {
                MeshRoundingRepair.NoJointSearchOnThisThread = false;
            }
            Assert.Equal(0, on.Rounding.JointMoves);
            Assert.Equal(0, on.Rounding.JointSearchProposals);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(on.Result, false));
            Assert.Equal("", Invariance.Difference(on.Result, off.Result));
        }

        [Fact]
        public void JointSearchOff_ByTheProperty_NoJointMove()
        {
            // the public switch of an instance of the repair: with a mesh of one degenerate triangle with two free corners (a cap) nothing
            // is proposed jointly where it is off, and where it is on the single search has repaired it before
            DMesh3 start = SafeRoundingDegenerateInputTests.Make(SafeRoundingDegenerateInputTests.Family.Collinear, 0, 1, 200), m = new DMesh3(start);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, JointSearch = false };
            Assert.True(r.Apply());
            Assert.Equal(0, r.JointMoves);
            Assert.Equal(0, r.JointSearchProposals);
        }

        // ---- plates of the search of benchmarks/BooleanInputOrder: results with two joint moves ----

        static double Bowl(double x, double y) { return 0.3 * ((x - 0.5) * (x - 0.5) + (y - 0.5) * (y - 0.5)); }
        static double Dome(double x, double y) { return Math.Sqrt(4 - (x - 0.5) * (x - 0.5) - (y - 0.5) * (y - 0.5)); }

        // (surface, grid n, extra cells dn, thickness t, turn e, ratio of the second turn, shift of both inputs, which one is cut)
        static ExactMeshBoolean RunPlates(Func<double, double, double> h, int n, int dn, double t, double e, double ratio, double shift, bool coarseMinusFine,
            Similarity s = null, bool joint = true, bool check = false, bool serial = false, int skipRounds = 0)
        {
            Func<Vector3d, Vector3d> sh = p => p + new Vector3d(shift, shift * 0.5, -shift * 0.25);
            DMesh3 coarse = Plate(n, t, sh, h), fine = Plate(n + dn, t, p => sh(Rotated(p, e, ratio * e)), h);
            DMesh3 a = coarseMinusFine ? coarse : fine, b = coarseMinusFine ? fine : coarse;
            if (s != null) {
                a = Invariance.Transformed(a, s);
                b = Invariance.Transformed(b, s);
            }
            var op = new ExactMeshBoolean(a, b, Difference);
            MeshRoundingRepair.NoJointSearchOnThisThread = joint == false;
            MeshRoundingRepair.SkipJointRoundsOnThisThread = skipRounds;
            MeshRoundingRepair.CheckOnThisThread = check;
            gParallel.SerialOnThisThread = serial;
            try {
                op.Compute();
            } finally {
                MeshRoundingRepair.NoJointSearchOnThisThread = false;
                MeshRoundingRepair.SkipJointRoundsOnThisThread = 0;
                MeshRoundingRepair.CheckOnThisThread = false;
                gParallel.SerialOnThisThread = false;
            }
            return op;
        }

        // inputs of the search (BooleanInputOrder search --count 1500 --seed 2, cases 763 and 152; --count 4000 --seed 3 --tiny, case 489), with
        // two or three joint moves each: a later move is proposed on a mesh that an earlier one changed, so a box that the earlier move left
        // stale is read by a later near query (check mode compares every query with a scan). The last one is also mapped by the UTM frame
        public static IEnumerable<object[]> SeveralJointMoves()
        {
            // (name, n, dn, t, e, ratio, shift, coarse minus fine, frame, joint moves, degenerate triangles left)
            yield return new object[] { "bowl", 3, 3, 0.2, 7.1566755079057365E-09, 0.3, 0.0, false, "", 3, 0 };
            yield return new object[] { "dome", 3, 3, 0.02, 8.039487869620447E-09, 0.3, 0.0, false, "", 2, 0 };
            // (with the extension the third triangle is repaired by a fixed corner: 3 moves, none left; FixedCorner_... pins that)
            yield return new object[] { "dome", 3, 3, 0.02, 1.4941511698034265E-08, 0.3, 100.0, false, "", 3, 0 };
            yield return new object[] { "dome", 3, 3, 0.02, 8.039487869620447E-09, 0.3, 0.0, false, "utm", -1, -1 };
        }

        static Func<double, double, double> Surface(string name) { return name == "bowl" ? (Func<double, double, double>)Bowl : name == "wave" ? Wave : Dome; }


        [Theory]
        [MemberData(nameof(SeveralJointMoves))]
        public void SeveralJointMoves_CheckMode_QueriesAgreeWithAScan_ResultUnchanged(string name, int n, int dn, double t, double e, double ratio, double shift, bool diff, string frame, int moves, int degenerateLeft)
        {
            Similarity s = frame == "" ? null : Similarity.Utm;
            ExactMeshBoolean reference = RunPlates(Surface(name), n, dn, t, e, ratio, shift, diff, s);
            if (moves >= 0) {
                Assert.Equal(moves, reference.Rounding.JointMoves);
                Assert.Equal(degenerateLeft, reference.Rounding.RemainingDegenerate);
            }
            // (check mode throws where a near query finds another set of triangles than a scan: a box that a joint move left stale)
            ExactMeshBoolean op = RunPlates(Surface(name), n, dn, t, e, ratio, shift, diff, s, check: true);
            Assert.Equal(reference.Rounding.JointMoves, op.Rounding.JointMoves);
            Assert.Equal("", Invariance.Difference(reference.Result, op.Result));
            Assert.True(op.Rounding.MaxDrift <= op.Rounding.MaxLength);
        }

        // ---- the cost of the stage: its own budget, and the repair's effort limit is not reached by it ----

        static ExactMeshBoolean RunCluster(int k, bool joint, long pairTests = 5000000, int skipRounds = 0)
        {
            DMesh3 m = DegenerateFamilies.Make(DegenerateFamilies.Family.Cluster, k, 1, 8);
            var op = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union) { RoundingMaxPairTests = pairTests };
            MeshRoundingRepair.NoJointSearchOnThisThread = joint == false;
            MeshRoundingRepair.SkipJointRoundsOnThisThread = skipRounds;
            try {
                op.Compute();
            } finally {
                MeshRoundingRepair.NoJointSearchOnThisThread = false;
                MeshRoundingRepair.SkipJointRoundsOnThisThread = 0;
            }
            return op;
        }

        [Fact]
        public void Cluster_TwoUnrepairableTriangles_TheStageStopsAtItsOwnBudget()
        {
            // (cluster k30, 8 poked triangles, seed 1: 2 degenerate triangles that no pair repairs; without a budget 5 pairs of 5 408 proposals
            // reached improves, 27 040 evaluations; the budget is 1 024 evaluations per triangle the stage starts with)
            ExactMeshBoolean off = RunCluster(30, false), first = RunCluster(30, true, skipRounds: SkipExtension), on = RunCluster(30, true);
            MeshRoundingRepair r = first.Rounding;
            Assert.Equal(2, r.RemainingDegenerate);
            Assert.Equal(off.Rounding.RemainingDegenerate, r.RemainingDegenerate);
            Assert.Equal(0, r.JointMoves);
            Assert.Equal(2 * 1024, r.JointSearchProposals);
            Assert.False(r.EvaluationLimitReached);
            Assert.Equal("", Invariance.Difference(off.Result, first.Result));
            // with the extension: the rounds of the pairs with a fixed corner and of two levels have a budget of their own (4 096 evaluations
            // per triangle the stage starts with, after the 1 024 of the first rounds), which these triangles use up; no move either
            r = on.Rounding;
            Assert.Equal(2, r.RemainingDegenerate);
            Assert.Equal(0, r.JointMoves);
            Assert.Equal(2 * (1024 + 4096), r.JointSearchProposals);
            Assert.False(r.EvaluationLimitReached);
            Assert.Equal("", Invariance.Difference(off.Result, on.Result));
        }

        [Fact]
        public void Cluster_ManyUnrepairableTriangles_TheStageDoesNotReachTheEffortLimit()
        {
            // (cluster k36: 13 degenerate triangles and 8 intersecting pairs left; the stage's evaluations turned EvaluationLimitReached from
            // False to True before it had its own budget: the error text of the unrepaired result then said so)
            ExactMeshBoolean off = RunCluster(36, false), on = RunCluster(36, true);
            Assert.False(off.Rounding.EvaluationLimitReached);
            Assert.False(on.Rounding.EvaluationLimitReached);
            Assert.Equal(off.Rounding.RemainingDegenerate, on.Rounding.RemainingDegenerate);
            Assert.Equal(off.Rounding.RemainingIntersections, on.Rounding.RemainingIntersections);
            Assert.Equal(0, on.Rounding.JointMoves);
            Assert.InRange(on.Rounding.JointSearchProposals, 1, 14 * (1024 + 4096));
        }

        [Fact]
        public void Cluster_EvaluationLimitNearlyUsedUp_TheStageStopsBeforeTheLimit()
        {
            // the repair alone, on the 13 degenerate triangles that cluster k36 is left with, with the smallest effort limit (10 000 evaluations):
            // the stage's own budget (1 024 per triangle, 13 312) is more than what is left, so it ends at the repair's limit and does not reach it
            DMesh3 left = RunCluster(36, false).Result;
            DMesh3 m = new DMesh3(left);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, MaxEvaluationsPerDefect = 0 };
            r.Apply();
            Assert.False(r.EvaluationLimitReached);
            Assert.True(r.RemainingDegenerate > 0);
            Assert.True(r.JointSearchProposals > 0);
            Assert.True(r.Evaluations <= r.MaxEvaluations);
            Assert.True(r.MaxEvaluations - r.Evaluations < 1024);
        }

        [Fact]
        public void Cluster_PairTestBudgetNearlyUsedUp_TheStageStopsBeforeTheLimit()
        {
            // 175 000 pair tests: the repair without the stage ends at 94 % of the budget, the stage would use it up (proposals 2 048 at 250 000)
            ExactMeshBoolean off = RunCluster(30, false, 175000), on = RunCluster(30, true, 175000);
            Assert.False(off.Rounding.EvaluationLimitReached);
            Assert.False(on.Rounding.EvaluationLimitReached);
            Assert.False(on.Rounding.StoppedByPairTests);
            Assert.InRange(on.Rounding.JointSearchProposals, 1, 2 * 1024 - 1);
        }

        [Fact]
        public void FailedPairs_AreNotSearchedAgainInTheNextRound()
        {
            // search case 152 of seed 2 (dome, shifted by 100): two joint moves in two rounds, and one triangle that no pair repairs. Its pair
            // fails in the first round and stays failed in the next ones, where the moves changed no star of its vertices: the proposals are
            // those of the moves and of one search of the failing pair (a stage that cleared the failed pairs every round proposed them
            // again, once per round: more than 2 149)
            ExactMeshBoolean op = RunPlates(Dome, 3, 3, 0.02, 1.4941511698034265E-08, 0.3, 100.0, false, skipRounds: SkipExtension);
            MeshRoundingRepair r = op.Rounding;
            Assert.Equal(2, r.JointMoves);
            Assert.Equal(1, r.RemainingDegenerate);
            Assert.False(r.EvaluationLimitReached);
            Assert.Equal(2149, r.JointSearchProposals);
        }

        [Fact]
        public void StageEndedByItsBudgetAfterMoves_ReportsTheStateOfTheMesh()
        {
            // the state the repair leaves without the stage (search case 763 of seed 2: three degenerate triangles, three joint moves), as a mesh for
            // a repair of its own with a stage budget of k evaluations per degenerate triangle: where a round makes moves and the budget then ends
            // the stage, the defect lists are those of the mesh as it is, not before the moves (RemainingDegenerate, RemainingIntersections, IsClean; RemainingDefectAt is taken from the same lists)
            DMesh3 left = RunPlates(Bowl, 3, 3, 0.2, 7.1566755079057365E-09, 0.3, 0.0, false, joint: false).Result;
            int endedAfterMoves = 0;
            foreach (int k in new[] { 1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233, 377, 610 }) {
                DMesh3 m = new DMesh3(left);
                var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, JointStageEvaluationsPerDefect = k };
                bool clean = r.Apply();
                // (what the mesh has: a repair that makes no edit counts its defects at the start)
                var check = new MeshRoundingRepair(new DMesh3(m)) { SharedPositionsTouch = true, MaxPasses = 0, JointSearch = false };
                check.Apply();
                Assert.Equal(check.InitialDegenerate, r.RemainingDegenerate);
                Assert.Equal(check.InitialIntersections, r.RemainingIntersections);
                Assert.Equal(r.RemainingDegenerate == 0 && r.RemainingIntersections == 0, clean);
                Assert.Equal(clean, r.IsClean);
                if (r.JointMoves > 0 && r.RemainingDegenerate == 0)
                    endedAfterMoves++;
            }
            Assert.True(endedAfterMoves > 0);
        }

        [Fact]
        public void ClosedInsideOutComponent_IsNotSearched_TheSameMeshTheRightWayRoundIs()
        {
            // the state the repair leaves without the stage (the plates of FailedPairs_...), as a mesh for a repair of its own: the same
            // degenerate triangles are repaired jointly where the solid is right way round, and left where it is inside out (the volume is
            // negative: a move there makes it no better, and the stage would use its budget on a result that is not clean anyway)
            DMesh3 left = RunPlates(Dome, 3, 3, 0.02, 1.4941511698034265E-08, 0.3, 100.0, false, joint: false).Result;
            Assert.True(left.IsClosed());
            DMesh3 outside = new DMesh3(left), inside = new DMesh3(left);
            inside.ReverseOrientation();
            Assert.True(Invariance.Volume(outside) > 0 && Invariance.Volume(inside) < 0);
            var rightWay = new MeshRoundingRepair(outside) { SharedPositionsTouch = true };
            rightWay.Apply();
            Assert.True(rightWay.JointMoves > 0);
            Assert.Equal(0, rightWay.RemainingDegenerate);
            var insideOut = new MeshRoundingRepair(inside) { SharedPositionsTouch = true };
            Assert.False(insideOut.Apply());
            Assert.Equal(0, insideOut.JointMoves);
            Assert.Equal(0, insideOut.JointSearchProposals);
            Assert.True(insideOut.RemainingDegenerate > 0);
            Assert.True(Invariance.Volume(inside) < 0);
        }

        // on the state the repair leaves without the stage (the reproducer), the pair of the joint move and proposals that put one of its
        // vertices onto the position of the other, onto the position the other leaves, or onto the position of a vertex next to it; each
        // goes through improves (not the script), which refuses it, and nothing moves. The positions of the vertices are those that
        // SharedPositionsTouch registers (the rounded mesh of a Boolean). That the refusal comes from the shared-position rules and not from
        // the counts is not shown here: in this state a vertex that is within the room of the pair is next to it, and moving onto it
        // makes the triangles at their edge degenerate (the rules are the guard of a pair whose vertices are not neighbours, which the
        // search does not propose)
        [Fact]
        public void Pair_OntoOnePositionOrAnotherVertex_Refused_NothingMoves_TheSuccessPairIsAccepted()
        {
            ExactMeshBoolean good = Run(joint: true), probe = Run(joint: false);
            DMesh3 x = probe.Result, y = good.Result;
            List<int> moved = x.VertexIndices().Where(v => x.GetVertex(v) != y.GetVertex(v)).ToList();
            Assert.Equal(2, moved.Count);
            int u = moved[0], w = moved[1];
            Vector3d cu = x.GetVertex(u), cw = x.GetVertex(w), pu = y.GetVertex(u), pw = y.GetVertex(w);
            int neighbour = x.VtxVerticesItr(u).Where(v => v != w).OrderBy(v => x.GetVertex(v).Distance(cu)).First();
            Vector3d cn = x.GetVertex(neighbour);
            var refused = new (string, Vector3d, Vector3d)[] {
                ("both onto the position u moves to", pu, pu),
                ("both onto the position w moves to", pw, pw),
                ("u onto the position w leaves", cw, pw),
                ("w onto the position u leaves", pu, cu),
                ("u onto the position of a neighbour", cn, pw),
                ("w onto the position of a neighbour", pu, cn),
                ("both onto the position of a neighbour", cn, cn),
            };
            foreach (var (what, a, b) in refused) {
                ExactMeshBoolean op = Run(joint: false);
                MeshRoundingRepair r = op.Rounding;
                DMesh3 before = new DMesh3(op.Result);
                Assert.False(r.try_move_pair(u, a, w, b), what);
                Assert.Equal("", Invariance.Difference(before, op.Result));
                Assert.Equal(0, r.JointMoves);
            }
            ExactMeshBoolean accepted = Run(joint: false);
            Assert.True(accepted.Rounding.try_move_pair(u, pu, w, pw));
            Assert.Equal(1, accepted.Rounding.JointMoves);
            Assert.Equal("", Invariance.Difference(y, accepted.Result));
        }

        // the displacement that exempts a triangle that turns over is that of its own moved corners
        [Fact]
        public void OwnDisplacement_IsThatOfTheMovedCornersOfTheTriangle()
        {
            DMesh3 m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(0, 1, 0)), d = m.AppendVertex(new Vector3d(1, 1, 0));
            var r = new MeshRoundingRepair(m);
            Vector3d pb = new Vector3d(1, 0, 0.25), pc = new Vector3d(0, 1, 1.5);
            Assert.Equal(0.25, r.own_displacement(new Index3i(a, b, d), b, pb, c, pc));
            Assert.Equal(1.5, r.own_displacement(new Index3i(a, d, c), b, pb, c, pc));
            Assert.Equal(1.5, r.own_displacement(new Index3i(a, b, c), b, pb, c, pc));
            Assert.Equal(0, r.own_displacement(new Index3i(a, a, d), b, pb, c, pc));
        }

        // ---- the edit itself, by script: the bound of two moved vertices ----

        static DMesh3 Sphere()
        {
            return new Sphere3Generator_NormalizedCube() { EdgeVertices = 4, Radius = 1 }.Generate().MakeDMesh();
        }

        static MeshRoundingRepair Script(DMesh3 m, List<Edit> script, double maxDisplacement, bool check = true)
        {
            var r = new MeshRoundingRepair(m) { MaxDisplacement = maxDisplacement, SharedPositionsTouch = true, RemoveTinyComponents = false, Script = script };
            MeshRoundingRepair.CheckOnThisThread = check;
            try {
                r.Apply();
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
            return r;
        }

        static Edit MovePair(int u, Vector3d pu, int w, Vector3d pw) => new Edit { Kind = Kind.MovePair, A = u, P = pu, B = w, Q = pw };

        // two vertices of one edge, and a third at distance 2 from them on the sphere
        static void Edge(DMesh3 m, out int u, out int w)
        {
            Index2i ev = m.GetEdgeV(m.EdgeIndices().First());
            u = ev.a; w = ev.b;
        }

        [Fact]
        public void Script_MovePair_WithinTheBound_BothMoved_DriftIsTheLargerMove()
        {
            DMesh3 start = Sphere(), m = new DMesh3(start);
            Edge(start, out int u, out int w);
            double diagonal = MeshRepairMetrics.TriangleBounds(start).DiagonalLength;
            double bound = 0.05 * diagonal;
            Vector3d pu = start.GetVertex(u) * 1.02, pw = start.GetVertex(w) * 0.96;
            MeshRoundingRepair r = Script(m, new List<Edit> { MovePair(u, pu, w, pw) }, 0.05);
            Assert.Equal(new[] { true }, r.ScriptAccepted);
            Assert.Equal(pu, m.GetVertex(u));
            Assert.Equal(pw, m.GetVertex(w));
            double larger = Math.Max(pu.Distance(start.GetVertex(u)), pw.Distance(start.GetVertex(w)));
            Assert.Equal(larger, r.MaxDrift, 12);
            Assert.True(r.MaxDrift <= bound);
            Assert.Equal(1, r.JointMoves);
            Assert.Equal(1, r.Moves);
            Assert.True(m.IsClosed() && Invariance.AllFinite(m));
        }

        [Fact]
        public void Script_MovePair_SecondVertexBeyondTheBound_RefusedAndNothingMoved()
        {
            DMesh3 start = Sphere(), m = new DMesh3(start);
            Edge(start, out int u, out int w);
            Vector3d pu = start.GetVertex(u) * 1.01, pw = start.GetVertex(w) * 1.5;
            MeshRoundingRepair r = Script(m, new List<Edit> { MovePair(u, pu, w, pw), MovePair(w, pw, u, pu) }, 0.05);
            Assert.Equal(new[] { false, false }, r.ScriptAccepted);
            Assert.Equal(0, r.JointMoves);
            Assert.Equal("", Invariance.Difference(start, m));
            // (the first vertex alone would have been within the bound)
            var single = Script(new DMesh3(start), new List<Edit> { new Edit { Kind = Kind.Move, A = u, P = pu } }, 0.05);
            Assert.Equal(new[] { true }, single.ScriptAccepted);
        }

        [Fact]
        public void Script_MovePair_BoundCountsFromTheStart_NotFromTheLastPosition()
        {
            // the bound of a triangle is its carried bound plus the farthest corner from where it started: a second pair that shares a vertex
            // with the first (the step from the last position within the bound, the distance from the start beyond it) is refused
            DMesh3 start = Sphere(), m = new DMesh3(start);
            Edge(start, out int u, out int w);
            double bound = 0.05 * MeshRepairMetrics.TriangleBounds(start).DiagonalLength;
            Vector3d dir = start.GetVertex(u).Normalized;
            Vector3d pu1 = start.GetVertex(u) + 0.6 * bound * dir, pw1 = start.GetVertex(w) + 0.6 * bound * start.GetVertex(w).Normalized;
            Vector3d pu2 = start.GetVertex(u) + 1.2 * bound * dir;
            MeshRoundingRepair r = Script(m, new List<Edit> { MovePair(u, pu1, w, pw1), MovePair(u, pu2, w, pw1 + 0.1 * bound * dir) }, 0.05);
            Assert.Equal(new[] { true, false }, r.ScriptAccepted);
            Assert.Equal(pu1, m.GetVertex(u));
            Assert.Equal(pw1, m.GetVertex(w));
        }

        [Fact]
        public void Script_MovePair_VerticesOfTwoComponents_Refused()
        {
            // the bound of an edit is that of the component of its first removed triangle: a pair of two components is refused
            DMesh3 m = Sphere();
            DMesh3 other = Sphere();
            foreach (int v in other.VertexIndices())
                other.SetVertex(v, other.GetVertex(v) + new Vector3d(10, 0, 0));
            int offset = m.MaxVertexID;
            var map = new Dictionary<int, int>();
            foreach (int v in other.VertexIndices())
                map[v] = m.AppendVertex(other.GetVertex(v));
            foreach (int t in other.TriangleIndices()) {
                Index3i tv = other.GetTriangle(t);
                m.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]);
            }
            DMesh3 start = new DMesh3(m);
            int u = m.VertexIndices().First(), w = map[other.VertexIndices().First()];
            MeshRoundingRepair r = Script(m, new List<Edit> { MovePair(u, start.GetVertex(u) * 1.001, w, start.GetVertex(w) + new Vector3d(0, 0, 0.001)) }, 0.05);
            Assert.Equal(new[] { false }, r.ScriptAccepted);
            Assert.Equal("", Invariance.Difference(start, m));
            Assert.True(offset > 0);
        }

        [Fact]
        public void Script_MovePair_SameVertexTwiceUnmovedOrBeyondTheDriftBound_Refused()
        {
            DMesh3 start = Sphere(), m = new DMesh3(start);
            Edge(start, out int u, out int w);
            Vector3d pu = start.GetVertex(u) * 1.01;
            MeshRoundingRepair r = Script(m, new List<Edit> {
                MovePair(u, pu, u, pu),                                            // the same vertex twice
                MovePair(u, start.GetVertex(u), w, start.GetVertex(w) * 1.01),     // the first stays where it is
                MovePair(u, pu, w, start.GetVertex(w)),                            // the second stays where it is
                MovePair(u, pu, w, pu),                                            // w moves beyond the drift bound (not the shared-position rule: a script skips it, see Pair_OntoOnePosition...)
            }, 0.05);
            Assert.Equal(new[] { false, false, false, false }, r.ScriptAccepted);
            Assert.Equal("", Invariance.Difference(start, m));
        }

        // ---- the extension of the stage (#1465): pairs with a corner that FixedVertexF marks, free pairs with two levels ----
        // The states are inputs of the search (benchmarks/BooleanInputOrder search --count 1500 --seed 2, --count 4000 --seed 3 --tiny) that the stage
        // of free pairs leaves with degenerate triangles, each with a corner of an input vertex (FixedVertexF, a preference in the boolean). Skipping
        // the rounds 1 and 2 (6) is the stage of before.

        const int SkipExtension = 6, SkipFixedPairs = 2, SkipFreeLevels = 4;

        // (name, n, dn, t, e, ratio, shift, coarse minus fine, degenerate triangles left without the extension, joint moves with it in all, of them with a fixed corner)
        public static IEnumerable<object[]> FixedCornerStates()
        {
            // s3_16: two triangles at one fixed corner, one pair removes both
            yield return new object[] { "wave", 2, 2, 0.02, 1.4895744956295086E-09, -1.1, 0.0, false, 2, 1, 1 };
            // s3_?: one triangle, the fixed corner moves with a free one
            yield return new object[] { "wave", 3, 3, 0.002, 1.1612562144082896E-09, -1.1, 0.0, false, 1, 1, 1 };
            // s3_17
            yield return new object[] { "wave", 2, 2, 0.02, 5.758742836765129E-07, 0.3, 100.0, false, 2, 1, 1 };
            // s3_18: three triangles, two moves of fixed corners (the oracle needed three vertices for the three at once)
            yield return new object[] { "wave", 2, 2, 0.002, 2.4351079875960173E-09, 0.3, 10000.0, true, 3, 2, 2 };
            // search case 152 of seed 2: two free pairs of the first round, then a fixed corner for the third triangle
            yield return new object[] { "dome", 3, 3, 0.02, 1.4941511698034265E-08, 0.3, 100.0, false, 1, 3, 1 };
        }

        static ExactMeshBoolean StrictBoolean(string name, int n, int dn, double t, double e, double ratio, double shift, bool diff, Similarity s)
        {
            Func<Vector3d, Vector3d> sh = p => p + new Vector3d(shift, shift * 0.5, -shift * 0.25);
            Func<double, double, double> h = Surface(name);
            DMesh3 coarse = Plate(n, t, sh, h), fine = Plate(n + dn, t, p => sh(Rotated(p, e, ratio * e)), h);
            DMesh3 a = diff ? coarse : fine, b = diff ? fine : coarse;
            if (s != null) {
                a = Invariance.Transformed(a, s);
                b = Invariance.Transformed(b, s);
            }
            return new ExactMeshBoolean(a, b, Difference) { StrictRounding = true };
        }

        static ExactMeshBoolean RunPlatesStrict(string name, int n, int dn, double t, double e, double ratio, double shift, bool diff, int skipRounds, Similarity s = null, bool serial = false)
        {
            ExactMeshBoolean op = StrictBoolean(name, n, dn, t, e, ratio, shift, diff, s);
            MeshRoundingRepair.SkipJointRoundsOnThisThread = skipRounds;
            gParallel.SerialOnThisThread = serial;
            try {
                op.Compute();
            } finally {
                MeshRoundingRepair.SkipJointRoundsOnThisThread = 0;
                gParallel.SerialOnThisThread = false;
            }
            return op;
        }

        [Theory]
        [MemberData(nameof(FixedCornerStates))]
        public void FixedCorner_StateLeftByTheFreePairs_IsCleanWithThePairsWithAFixedCorner(string name, int n, int dn, double t, double e, double ratio, double shift, bool diff, int left, int moves, int withFixed)
        {
            // without the extension (the stage of before): degenerate triangles left, the status of a strict run is RoundingNotClean
            ExactMeshBoolean old = RunPlatesStrict(name, n, dn, t, e, ratio, shift, diff, SkipExtension);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, old.Status);
            Assert.Equal(left, old.Rounding.RemainingDegenerate);
            Assert.Equal(0, old.Rounding.JointMovesWithFixedCorner);
            // with it: clean, within the bound, closed and manifold; the moves of fixed corners are counted
            ExactMeshBoolean op = RunPlatesStrict(name, n, dn, t, e, ratio, shift, diff, 0);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            MeshRoundingRepair r = op.Rounding;
            Assert.True(r.IsClean);
            Assert.Equal(0, r.RemainingDegenerate);
            Assert.Equal(0, r.RemainingIntersections);
            Assert.Equal(moves, r.JointMoves);
            Assert.Equal(withFixed, r.JointMovesWithFixedCorner);
            Assert.True(r.MaxDrift <= r.MaxLength, $"{r.MaxDrift:R} {r.MaxLength:R}");
            DMesh3 m = op.Result;
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m, false));
            Assert.True(m.IsClosed());
            Assert.True(m.CheckValidity(eFailMode: FailMode.ReturnOnly));
            Assert.True(Invariance.AllFinite(m));
            // the same mesh as without the extension but for the corners of the moves: the same triangles, and a fixed corner is among the moved vertices
            ExactMeshBoolean lenient = RunPlates(Surface(name), n, dn, t, e, ratio, shift, diff, skipRounds: SkipExtension);
            DMesh3 y = lenient.Result;
            Assert.Equal(y.TriangleCount, m.TriangleCount);
            Func<int, bool> isFixed = lenient.Rounding.FixedVertexF;
            List<int> moved = y.VertexIndices().Where(v => y.GetVertex(v) != m.GetVertex(v)).ToList();
            Assert.True(moved.Count >= 2);
            Assert.Equal(withFixed > 0, moved.Any(v => isFixed(v)));
            foreach (int v in moved)
                Assert.True(y.GetVertex(v).Distance(m.GetVertex(v)) <= r.MaxLength);
        }

        [Fact]
        public void FixedCorner_Reproducer_FreePairsFirst_BitIdenticalWhereTheFreePairsSucceed()
        {
            // the reproducer of #1465 (a free pair moves, the triangle's third corner is fixed): the extension never runs, the rounds
            // of the free pairs make the same move as without it (a stage that tried the fixed corner first moved another pair)
            ExactMeshBoolean with = Run(joint: true), without = RunWithoutExtension();
            Assert.Equal(1, with.Rounding.JointMoves);
            Assert.Equal(0, with.Rounding.JointMovesWithFixedCorner);
            Assert.Equal(without.Rounding.JointSearchProposals, with.Rounding.JointSearchProposals);
            Assert.Equal(without.Rounding.MaxDrift, with.Rounding.MaxDrift);
            Assert.Equal("", Invariance.Difference(without.Result, with.Result));
        }

        static ExactMeshBoolean RunWithoutExtension()
        {
            MeshRoundingRepair.SkipJointRoundsOnThisThread = SkipExtension;
            try {
                return Run(joint: true);
            } finally {
                MeshRoundingRepair.SkipJointRoundsOnThisThread = 0;
            }
        }

        // (name, n, dn, t, e, ratio, shift, coarse minus fine, joint moves, degenerate left): states that the free pairs repair (or leave) as they are
        public static IEnumerable<object[]> FreePairStates()
        {
            yield return new object[] { "bowl", 3, 3, 0.2, 7.1566755079057365E-09, 0.3, 0.0, false, 3, 0 };
            yield return new object[] { "dome", 3, 3, 0.02, 8.039487869620447E-09, 0.3, 0.0, false, 2, 0 };
        }

        [Theory]
        [MemberData(nameof(FreePairStates))]
        public void FixedCorner_FreePairsRepairEverything_SameResultAsWithoutTheExtension(string name, int n, int dn, double t, double e, double ratio, double shift, bool diff, int moves, int left)
        {
            ExactMeshBoolean with = RunPlates(Surface(name), n, dn, t, e, ratio, shift, diff), without = RunPlates(Surface(name), n, dn, t, e, ratio, shift, diff, skipRounds: SkipExtension);
            Assert.Equal(moves, with.Rounding.JointMoves);
            Assert.Equal(left, with.Rounding.RemainingDegenerate);
            Assert.Equal(0, with.Rounding.JointMovesWithFixedCorner);
            Assert.Equal(without.Rounding.JointSearchProposals, with.Rounding.JointSearchProposals);
            Assert.Equal(without.Rounding.MaxDrift, with.Rounding.MaxDrift);
            Assert.Equal("", Invariance.Difference(without.Result, with.Result));
        }

        [Fact]
        public void FixedCorner_JointExtensionOff_ByTheProperty_TheStageOfBefore()
        {
            // the state the repair leaves without the stage, as a mesh for a repair of its own with the fixed corners of the boolean
            DMesh3 left = StateWithoutTheStage(out Func<int, bool> isFixed, 3, 0.002, 1.1612562144082896E-09, -1.1, 0.0, false);
            var on = Repair(new DMesh3(left), isFixed, hard: false, extension: true);
            on.Apply();
            Assert.True(on.IsClean);
            Assert.Equal(1, on.JointMovesWithFixedCorner);
            var off = Repair(new DMesh3(left), isFixed, hard: false, extension: false);
            off.Apply();
            Assert.False(off.IsClean);
            Assert.Equal(1, off.RemainingDegenerate);
            Assert.Equal(0, off.JointMoves);
        }

        static DMesh3 StateWithoutTheStage(out Func<int, bool> isFixed, int n, double t, double e, double ratio, double shift, bool diff)
        {
            ExactMeshBoolean op = RunPlates(Wave, n, n, t, e, ratio, shift, diff, joint: false);
            isFixed = op.Rounding.FixedVertexF;
            return new DMesh3(op.Result);
        }

        static MeshRoundingRepair Repair(DMesh3 m, Func<int, bool> isFixed, bool hard, bool extension)
        {
            return new MeshRoundingRepair(m) {
                SharedPositionsTouch = true, FixedVertexF = isFixed, FixedWhereCoarse = false, ExactVertexF = isFixed, UlpFactor = 0, FixedHard = hard, JointExtension = extension
            };
        }

        [Fact]
        public void FixedCorner_HardFixed_NeverMoved_TheDefectIsLeft()
        {
            DMesh3 left = StateWithoutTheStage(out Func<int, bool> isFixed, 3, 0.002, 1.1612562144082896E-09, -1.1, 0.0, false);
            // (soft: the extension moves a fixed corner and repairs the triangle)
            DMesh3 soft = new DMesh3(left);
            var s = Repair(soft, isFixed, hard: false, extension: true);
            s.Apply();
            Assert.True(s.IsClean);
            Assert.Equal(1, s.JointMovesWithFixedCorner);
            Assert.Contains(left.VertexIndices(), v => isFixed(v) && left.GetVertex(v) != soft.GetVertex(v));
            // (hard: every fixed vertex keeps its position bit for bit, the triangle stays degenerate, as without the extension)
            DMesh3 hardM = new DMesh3(left);
            var h = Repair(hardM, isFixed, hard: true, extension: true);
            h.Apply();
            Assert.False(h.IsClean);
            Assert.Equal(1, h.RemainingDegenerate);
            Assert.Equal(0, h.JointMovesWithFixedCorner);
            foreach (int v in left.VertexIndices().Where(isFixed))
                Assert.Equal(left.GetVertex(v), hardM.GetVertex(v));
            DMesh3 base0 = new DMesh3(left);
            var b = Repair(base0, isFixed, hard: true, extension: false);
            b.Apply();
            Assert.Equal("", Invariance.Difference(base0, hardM));
            // (no pair with a hard-fixed corner is proposed either: the proposals are those of the free pairs)
            Assert.Equal(b.JointSearchProposals, h.JointSearchProposals);
        }

        [Fact]
        public void FixedCorner_TwoLevels_FreePairWithDifferentSizes_RepairsATriangleTheSameLevelsDoNot()
        {
            // s3_18 without the round of the fixed corners: the free pair (128, 130) of the third triangle moves by 1 and 1/8 of the rooms of
            // its corners (the oracle's witness; one level for both has no move). The two other triangles stay
            ExactMeshBoolean before = RunPlates(Wave, 2, 2, 0.002, 2.4351079875960173E-09, 0.3, 10000.0, true, skipRounds: SkipExtension);
            ExactMeshBoolean op = RunPlates(Wave, 2, 2, 0.002, 2.4351079875960173E-09, 0.3, 10000.0, true, skipRounds: SkipFixedPairs);
            Assert.Equal(3, before.Rounding.RemainingDegenerate);
            Assert.Equal(0, before.Rounding.JointMoves);
            Assert.Equal(2, op.Rounding.RemainingDegenerate);
            Assert.Equal(1, op.Rounding.JointMoves);
            Assert.Equal(0, op.Rounding.JointMovesWithFixedCorner);
            DMesh3 x = before.Result, y = op.Result;
            List<int> moved = x.VertexIndices().Where(v => x.GetVertex(v) != y.GetVertex(v)).ToList();
            Assert.Equal(2, moved.Count);
            double[] steps = moved.Select(v => x.GetVertex(v).Distance(y.GetVertex(v)) / before.Rounding.room_at(v)).ToArray();
            // (the room over 2^j: two exact powers of two that differ, by at most 2^3)
            foreach (double step in steps)
                Assert.Equal(Math.Round(Math.Log(step, 2)), Math.Log(step, 2), 4);
            double ratioOfSteps = Math.Abs(Math.Log(steps[0] / steps[1], 2));
            Assert.InRange(Math.Round(ratioOfSteps), 1, 3);
            Assert.Equal(Math.Round(ratioOfSteps), ratioOfSteps, 4);
            Assert.True(op.Rounding.MaxDrift <= op.Rounding.MaxLength);
        }

        [Fact]
        public void FixedCorner_ReusedInstance_CountersAreThoseOfTheLastApply()
        {
            DMesh3 left = StateWithoutTheStage(out Func<int, bool> isFixed, 3, 0.002, 1.1612562144082896E-09, -1.1, 0.0, false);
            var r = Repair(new DMesh3(left), isFixed, hard: false, extension: true);
            r.Apply();
            Assert.Equal(1, r.JointMoves);
            Assert.Equal(1, r.JointMovesWithFixedCorner);
            // (the mesh is clean now: a second Apply makes no move and resets the counters)
            r.Apply();
            Assert.Equal(0, r.JointMoves);
            Assert.Equal(0, r.JointMovesWithFixedCorner);
            Assert.True(r.IsClean);
            // (a fresh instance on the same mesh as the first one had gives the same counts as the first)
            var again = Repair(new DMesh3(left), isFixed, hard: false, extension: true);
            again.Apply();
            Assert.Equal(1, again.JointMovesWithFixedCorner);
        }

        [Fact]
        public void FixedCorner_ReusedBoolean_SameAsFresh()
        {
            ExactMeshBoolean fresh = RunPlatesStrict("wave", 2, 2, 0.002, 2.4351079875960173E-09, 0.3, 10000.0, true, 0);
            ExactMeshBoolean op = StrictBoolean("wave", 2, 2, 0.002, 2.4351079875960173E-09, 0.3, 10000.0, true, null);
            Assert.True(op.Compute());
            DMesh3 first = new DMesh3(op.Result);
            Assert.True(op.Compute());
            Assert.Equal(2, op.Rounding.JointMovesWithFixedCorner);
            Assert.Equal(fresh.Rounding.JointSearchProposals, op.Rounding.JointSearchProposals);
            Assert.Equal("", Invariance.Difference(first, op.Result));
            Assert.Equal("", Invariance.Difference(fresh.Result, op.Result));
        }

        [Fact]
        public void FixedCorner_SecondTier_LeavesOutTheProposalsThatLeaveNoBadTriangle()
        {
            // s3_17: the pair of the extension's one move leaves no degenerate triangle at the two stars (two were there): the cheap test
            // says yes to it with no lower bound (the first tier), and no with a lower bound of 1 (the second tier: it left out what the
            // first one evaluated)
            ExactMeshBoolean left = RunPlates(Wave, 2, 2, 0.02, 5.758742836765129E-07, 0.3, 100.0, false, joint: false);
            ExactMeshBoolean good = RunPlates(Wave, 2, 2, 0.02, 5.758742836765129E-07, 0.3, 100.0, false);
            DMesh3 x = left.Result, y = good.Result;
            Assert.Equal(2, left.Rounding.RemainingDegenerate);
            List<int> moved = x.VertexIndices().Where(v => x.GetVertex(v) != y.GetVertex(v)).ToList();
            Assert.Equal(2, moved.Count);
            int u = moved[0], w = moved[1];
            var star = new List<int>(x.VtxTrianglesItr(u));
            star.AddRange(x.VtxTrianglesItr(w).Where(t => star.Contains(t) == false));
            Assert.True(left.Rounding.pair_removes_bad(u, y.GetVertex(u), w, y.GetVertex(w), star, 2));
            Assert.True(left.Rounding.pair_removes_bad(u, y.GetVertex(u), w, y.GetVertex(w), star, 2, 0));
            Assert.False(left.Rounding.pair_removes_bad(u, y.GetVertex(u), w, y.GetVertex(w), star, 2, 1));
            // (no move at all: both still there, no answer for any bound)
            Assert.False(left.Rounding.pair_removes_bad(u, x.GetVertex(u), w, x.GetVertex(w), star, 2));
        }

        [Fact]
        public void FixedCorner_BothTiersRun_TheProposalsAreThoseOfTheRoundsAndTheBudget()
        {
            // s3_17 with its fixed corner 108 hard-fixed: no pair with it, the free pairs of rounds 0 and 2 find no move (round 2 has two
            // bad triangles at the star of its pair: both tiers run). The count is the budget of the stage (1 024 per triangle) and of the
            // extension (4 096 per triangle) together with the evaluations of the first round 0: pinned as measured (6 144)
            DMesh3 left = StateWithoutTheStage(out Func<int, bool> isFixed, 2, 0.02, 5.758742836765129E-07, 0.3, 100.0, false);
            var m = new DMesh3(left);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, FixedVertexF = v => v == 108, ExactVertexF = isFixed, FixedWhereCoarse = false, UlpFactor = 0, FixedHard = true };
            r.Apply();
            Assert.Equal(1, r.RemainingDegenerate);
            Assert.Equal(1, r.JointMoves);
            Assert.Equal(6144, r.JointSearchProposals);
            Assert.False(r.EvaluationLimitReached);
        }

        public static IEnumerable<object[]> FixedCornerFrames() { return Similarity.Names(); }

        [Theory]
        [MemberData(nameof(FixedCornerFrames))]
        public void FixedCorner_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            ExactMeshBoolean reference = RunPlatesStrict("wave", 2, 2, 0.002, 2.4351079875960173E-09, 0.3, 10000.0, true, 0);
            ExactMeshBoolean op = RunPlatesStrict("wave", 2, 2, 0.002, 2.4351079875960173E-09, 0.3, 10000.0, true, 0, s);
            Assert.NotNull(reference.Result);
            if (s.IsExact) {
                // a power-of-two scale: rooms and steps scale, the same edits and the same mesh, bit for bit
                Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
                Assert.Equal(2, op.Rounding.JointMoves);
                Assert.Equal(2, op.Rounding.JointMovesWithFixedCorner);
                Assert.Equal(reference.Rounding.JointSearchProposals, op.Rounding.JointSearchProposals);
                Assert.Equal(reference.Rounding.MaxDrift * s.Scale, op.Rounding.MaxDrift);
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(op.Result, s)));
                return;
            }
            // another frame rounds the plates differently: not this state. A result that is claimed is clean
            Assert.True(op.Status == ExactMeshBoolean.BooleanStatus.Success || op.Status == ExactMeshBoolean.BooleanStatus.RoundingNotClean, s.Name);
            if (op.Status == ExactMeshBoolean.BooleanStatus.Success) {
                Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result, false));
                Assert.True(op.Rounding.MaxDrift <= op.Rounding.MaxLength, s.Name);
            }
        }

        [Fact]
        public void FixedCorner_SerialEqualsParallel_Repeated()
        {
            ExactMeshBoolean serial = RunPlatesStrict("wave", 2, 2, 0.002, 2.4351079875960173E-09, 0.3, 10000.0, true, 0, serial: true);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, serial.Status);
            for (int i = 0; i < 3; ++i) {
                ExactMeshBoolean parallel = RunPlatesStrict("wave", 2, 2, 0.002, 2.4351079875960173E-09, 0.3, 10000.0, true, 0);
                Assert.Equal("", Invariance.Difference(serial.Result, parallel.Result));
                Assert.Equal(serial.Rounding.JointSearchProposals, parallel.Rounding.JointSearchProposals);
                Assert.Equal(serial.Rounding.JointMovesWithFixedCorner, parallel.Rounding.JointMovesWithFixedCorner);
                Assert.Equal(serial.Rounding.MaxDrift, parallel.Rounding.MaxDrift);
            }
        }
    }
}
