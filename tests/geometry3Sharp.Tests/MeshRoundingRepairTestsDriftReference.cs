using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Edit = g3.MeshRoundingRepair.ScriptEdit;
using Kind = g3.MeshRoundingRepair.ScriptKind;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The reference rule of SafeRounding's drift (docs/design/SAFE_ROUNDING_D1_DRIFT.md), on scripted edits: the repair
    /// applies the given edits, each checked by the drift bound only (not by the defects), so that a sequence can be
    /// chosen that the repair would not choose by itself. Each case is a counterexample of the Z3 check
    /// (tools/Z3Pilot/test_drift_reference.py, PINNED) built as a mesh: with the matching wrong rule in the code the
    /// reported drift falls below the measured distance between the start and the result, by 10 % or more; with the
    /// rule it bounds it, and the measured distance is a large share of it.
    /// The mesh: a sphere, one edge split at 1/32 and 2/32 of its length (a short edge a-n1, then n1-n2), and the
    /// triangle (a, n1, x) poked, its new vertex u raised by h along the triangle's normal: a tent of three triangles.
    /// Removing u makes the triangle (a, n1, x) again with the carried bound h (a generic edit); contracting n1 into a
    /// then collapses it, a ghost on the edge a-x that carries h, while the triangles around carry nothing. The ghost is
    /// the only record of the tent: a rule that drops it, does not move it with its vertex, or does not evaluate it,
    /// reports less than the surface moved.
    /// </summary>
    public class MeshRoundingRepairTestsDriftReference
    {
        const double H = 0.2, Fraction = 1.0 / 32, Move = 0.1;

        sealed class Tent
        {
            public DMesh3 Mesh;
            public int A, N1, N2, X, U, Q;
            public Vector3d Normal;
            public Vector3d P(int v) => Mesh.GetVertex(v);
        }

        static Tent MakeTent()
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { EdgeVertices = 4, Radius = 1 }.Generate().MakeDMesh();
            Vector3d dir = new Vector3d(1, 0.3, 0.2).Normalized;
            int e0 = m.EdgeIndices().OrderBy(e => (m.GetEdgePoint(e, 0.5).Normalized - dir).Length).First();
            Index2i ev = m.GetEdgeV(e0);
            int a = ev.a;
            Vector3d pa = m.GetVertex(a), pb = m.GetVertex(ev.b);
            Assert.Equal(MeshResult.Ok, m.SplitEdge(e0, out DMesh3.EdgeSplitInfo s2));
            int n2 = s2.vNew;
            m.SetVertex(n2, pa + 2 * Fraction * (pb - pa));
            Assert.Equal(MeshResult.Ok, m.SplitEdge(m.FindEdge(a, n2), out DMesh3.EdgeSplitInfo s1));
            int n1 = s1.vNew;
            m.SetVertex(n1, pa + Fraction * (pb - pa));
            int x = m.GetEdgeOpposingV(m.FindEdge(a, n1)).a;
            int t0 = m.FindTriangle(a, n1, x);
            Index3i T = m.GetTriangle(t0);
            Vector3d p0 = m.GetVertex(T.a), p1 = m.GetVertex(T.b), p2 = m.GetVertex(T.c);
            Vector3d normal = (p1 - p0).Cross(p2 - p0).Normalized;
            Assert.Equal(MeshResult.Ok, m.PokeTriangle(t0, out DMesh3.PokeTriangleInfo poke));
            int u = poke.new_vid;
            m.SetVertex(u, (p0 + p1 + p2) / 3 + H * normal);
            // q: across the edge n1-x from the tent; after the contraction of n1 into a, the edge a-x has q on one side
            Index2i o = m.GetEdgeOpposingV(m.FindEdge(n1, x));
            int q = o.a == u ? o.b : o.a;
            Assert.True(m.IsClosed() && m.CheckValidity(eFailMode: FailMode.ReturnOnly));
            return new Tent { Mesh = m, A = a, N1 = n1, N2 = n2, X = x, U = u, Q = q, Normal = normal };
        }

        static Edit Remove(int v) => new Edit { Kind = Kind.RemoveVertex, A = v };
        static Edit Collapse(int a, int b, Vector3d p) => new Edit { Kind = Kind.Collapse, A = a, B = b, P = p };
        static Edit MoveTo(int v, Vector3d p) => new Edit { Kind = Kind.Move, A = v, P = p };
        static Edit Flip(int a, int b) => new Edit { Kind = Kind.Flip, A = a, B = b };

        // the cases: the mutant of the Z3 check each one exposes (PINNED there), and the edits on the tent t, with the
        // positions mapped by f
        static readonly Dictionary<string, Func<Tent, Func<Vector3d, Vector3d>, List<Edit>>> Cases =
            new Dictionary<string, Func<Tent, Func<Vector3d, Vector3d>, List<Edit>>> {
                // generic_without_delta: the removal moves the tent's tip by h onto the triangle below; without its own
                // bound the edit reports 0
                ["remove the tip"] = (t, f) => new List<Edit> { Remove(t.U) },
                // generic_carry_only: the tip moved by h / 4 towards the base, then removed: N from the carried bound
                // alone (0) misses the move, 3/4 h is reported for a distance h
                ["move the tip, remove it"] = (t, f) => new List<Edit> {
                    MoveTo(t.U, f(t.P(t.U) - (H / 4) * t.Normal)), Remove(t.U) },
                // kept_vertex_anchor: the tip contracted into a; a renamed corner that took the kept vertex's position
                // as its anchor would report 0
                ["contract the tip"] = (t, f) => new List<Edit> { Collapse(t.U, t.A, f(t.P(t.A))) },
                // no_ghosts, ghost_dies_with_its_vertex, move_skips_ghosts: the ghost on a-x (carrying h) goes with a
                // to n2, and n2 moves away from the tent
                ["remove, contract twice, move"] = (t, f) => new List<Edit> {
                    Remove(t.U), Collapse(t.N1, t.A, f(t.P(t.A))), Collapse(t.A, t.N2, f(t.P(t.N2))),
                    MoveTo(t.N2, f(t.P(t.N2) - Move * t.Normal)) },
                // generic_ignores_lost_ghosts, and a ghost on an edge lost: the flip of a-x takes the ghost's edge away,
                // and q, a vertex of the flipped triangles, moves away from the tent
                ["remove, contract, flip the ghost's edge, move"] = (t, f) => new List<Edit> {
                    Remove(t.U), Collapse(t.N1, t.A, f(t.P(t.A))), Flip(t.A, t.X),
                    MoveTo(t.Q, f(t.P(t.Q) - Move * t.Normal)) },
            };

        // the largest distance of the corners, edge midpoints and centroids of from's triangles to the surface to: a
        // lower estimate of the one-sided Hausdorff distance (as RoundingDriftClusterTests)
        static double Measured(DMesh3 from, DMesh3 to)
        {
            var tree = new DMeshAABBTree3(to, true);
            double worst = 0;
            foreach (int t in from.TriangleIndices()) {
                Index3i tv = from.GetTriangle(t);
                Vector3d a = from.GetVertex(tv.a), b = from.GetVertex(tv.b), c = from.GetVertex(tv.c);
                foreach (Vector3d p in new[] { a, b, c, (a + b) / 2, (b + c) / 2, (c + a) / 2, (a + b + c) / 3 }) {
                    Index3i nv = to.GetTriangle(tree.FindNearestTriangle(p));
                    var d = new DistPoint3Triangle3(p, new Triangle3d(to.GetVertex(nv.a), to.GetVertex(nv.b), to.GetVertex(nv.c)));
                    d.GetSquared();
                    worst = Math.Max(worst, p.Distance(d.TriangleClosest));
                }
            }
            return worst;
        }

        static double Measured2(DMesh3 x, DMesh3 y) => Math.Max(Measured(x, y), Measured(y, x));

        // the repair of start by the script (bound: the diagonal, so that every edit passes it and it scales with a
        // similarity); vertex removals need SharedPositionsTouch
        static MeshRoundingRepair Run(DMesh3 m, List<Edit> script, bool check = false)
        {
            var r = new MeshRoundingRepair(m) { MaxDisplacement = 1, SharedPositionsTouch = true, RemoveTinyComponents = false, Script = script };
            MeshRoundingRepair.CheckOnThisThread = check;
            try {
                r.Apply();
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
            return r;
        }

        public static IEnumerable<object[]> CaseNames() => Cases.Keys.Select(k => new object[] { k });

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Script_ReportedDriftBoundsTheMeasuredDistance(string name)
        {
            Tent t = MakeTent();
            DMesh3 m = new DMesh3(t.Mesh);
            List<Edit> script = Cases[name](t, p => p);
            MeshRoundingRepair r = Run(m, script);
            Assert.True(r.ScriptAccepted.Count == script.Count && r.ScriptAccepted.All(ok => ok), name);
            Assert.True(m.IsClosed() && Invariance.AllFinite(m), name);
            double distance = Measured2(m, t.Mesh);
            // (a wrong rule reports at most 0.9 of the distance here; the slack is the rounding of the measurement)
            Assert.True(distance <= r.MaxDrift + 1e-12, $"{name}: measured {distance:R}, reported {r.MaxDrift:R}");
            Assert.True(distance >= 0.5 * r.MaxDrift, $"{name}: measured {distance:R}, reported {r.MaxDrift:R}");
            Assert.True(distance >= 0.9 * H, $"{name}: measured {distance:R}");
            // the check mode (every bound from the records, the ghosts on vertices or edges) agrees
            DMesh3 mc = new DMesh3(t.Mesh);
            MeshRoundingRepair rc = Run(mc, Cases[name](t, p => p), check: true);
            Assert.Equal("", Invariance.Difference(m, mc));
            Assert.Equal(r.MaxDrift, rc.MaxDrift);
        }

        [Fact]
        public void Script_GhostsMadeAndLost()
        {
            // the contraction of n1 into a collapses two triangles (two ghosts), the flip of a-x loses the one on a-x
            Tent t = MakeTent();
            MeshRoundingRepair r = Run(new DMesh3(t.Mesh), Cases["remove, contract, flip the ghost's edge, move"](t, p => p), check: true);
            Assert.Equal(2, r.GhostsMade);
            Assert.Equal(1, r.GhostsLost);
            // the second contraction (a into n2) collapses two more, the move loses none
            r = Run(new DMesh3(t.Mesh), Cases["remove, contract twice, move"](t, p => p), check: true);
            Assert.Equal(4, r.GhostsMade);
            Assert.Equal(0, r.GhostsLost);
        }

        [Fact]
        public void Script_Similarities_BoundHolds_Pow2BitIdentical()
        {
            Tent t = MakeTent();
            foreach (string name in Cases.Keys) {
                DMesh3 m0 = new DMesh3(t.Mesh);
                MeshRoundingRepair r0 = Run(m0, Cases[name](t, p => p));
                foreach (Similarity sim in Similarity.All()) {
                    DMesh3 s = Invariance.Transformed(t.Mesh, sim), m = new DMesh3(s);
                    MeshRoundingRepair r = Run(m, Cases[name](t, sim.Apply));
                    Assert.True(r.ScriptAccepted.All(ok => ok), $"{name}, {sim}");
                    double slack = 16 * sim.ApplyLength(sim.Resolution(2));
                    Assert.True(Measured2(m, s) <= r.MaxDrift + slack, $"{name}, {sim}");
                    if (sim.IsExact) {
                        Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m, sim)));
                        Assert.Equal(r0.MaxDrift * sim.Scale, r.MaxDrift);
                    }
                }
            }
        }

        [Fact]
        public void Script_ReusedInstance_SameAsFresh()
        {
            // the records (anchors, carried bounds, ghosts) start again on the next mesh
            Tent t = MakeTent();
            const string name = "remove, contract twice, move";
            DMesh3 fresh = new DMesh3(t.Mesh);
            MeshRoundingRepair rf = Run(fresh, Cases[name](t, p => p));
            DMesh3 first = new DMesh3(t.Mesh), second = new DMesh3(t.Mesh);
            var r = new MeshRoundingRepair(first) { MaxDisplacement = 1, SharedPositionsTouch = true, RemoveTinyComponents = false, Script = Cases[name](t, p => p) };
            r.Apply();
            r.Mesh = second;
            r.Apply();
            Assert.Equal("", Invariance.Difference(fresh, second));
            Assert.Equal(rf.MaxDrift, r.MaxDrift);
            Assert.Equal(rf.GhostsMade, r.GhostsMade);
        }
    }
}
