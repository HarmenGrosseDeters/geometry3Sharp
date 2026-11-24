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

        static double Wave(double x, double y) { return 0.05 * Math.Sin(3 * x + 1) * Math.Cos(2 * y); }

        static readonly Vector3d Center = new Vector3d(0.5, 0.5, 0);

        // a curved plate: the surface z = h(x, y) over the unit square on an n by n grid, the thickness t along z, closed by side walls
        static DMesh3 Plate(int n, double t, Func<Vector3d, Vector3d> map = null)
        {
            var m = new DMesh3();
            int N = n + 1;
            var top = new int[N, N];
            var bot = new int[N, N];
            for (int i = 0; i < N; ++i)
                for (int j = 0; j < N; ++j) {
                    double x = (double)i / n, y = (double)j / n, z = Wave(x, y);
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
            double co = Math.Cos(ax), si = Math.Sin(ax);
            q = new Vector3d(q.x, co * q.y - si * q.z, si * q.y + co * q.z);
            co = Math.Cos(ay);
            si = Math.Sin(ay);
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
        public void Script_MovePair_SameVertexTwiceOrUnmoved_Refused()
        {
            DMesh3 start = Sphere(), m = new DMesh3(start);
            Edge(start, out int u, out int w);
            Vector3d pu = start.GetVertex(u) * 1.01;
            MeshRoundingRepair r = Script(m, new List<Edit> {
                MovePair(u, pu, u, pu),                                            // the same vertex twice
                MovePair(u, start.GetVertex(u), w, start.GetVertex(w) * 1.01),     // the first stays where it is
                MovePair(u, pu, w, start.GetVertex(w)),                            // the second stays where it is
                MovePair(u, pu, w, pu),                                            // both onto one position
            }, 0.05);
            Assert.Equal(new[] { false, false, false, false }, r.ScriptAccepted);
            Assert.Equal("", Invariance.Difference(start, m));
        }
    }
}
