// QuadFlow3BSat: a research spike, not a gate (docs/research/QUAD_3B_CLAUSE_LEARNING_2026-11.md). It asks how many fallbacks of
// QuadRemesher.PureQuads on the 54 runs of benchmarks/QuadFlow3BGate are caused by the search of the local repair (a DPLL without
// learning under a decision budget) and how many by the problem (local instances with no solution).
//
// Per fallback run:
//   1. which budget ended the library's attempt (splits, SAT decisions, SAT attempts, the round limit, or none);
//   2. the state after the greedy moves (a second QuadFlowRepair with SatTotalBudget = 0, read by reflection: no library change);
//   3. "library trajectory": the repair loop of QuadFlowRepair.RepairSat replayed here with the library's own GroupSatSolver and budgets.
//      Its counters must equal the library's (the check that the instances built here are the library's). Every instance the DPLL
//      leaves at its budget is decided by the clause-learning solver (CdclSolver.cs) and written as DIMACS;
//   4. "learning trajectory": the same loop with the clause-learning solver and no budget (no decision, attempt or round limit). A seed
//      whose instances of all rings are unsatisfiable is a local obstruction; its instances are written as DIMACS. Every Satisfiable
//      answer is checked against the clauses, every Unsatisfiable answer by reverse unit propagation (RupChecker) and, where the
//      library's DPLL decides within `crosscheck` decisions, against that;
//   5. if no fold is left, the library's own remaining steps (MapCloses, SolvePositions, Extract) run on the repaired map (by
//      reflection) and the result gets the independent checks of the gate.
// Effort is counted in conflicts and decisions, never in time.
//
//   dotnet run -c Release --project benchmarks/QuadFlow3BSat -- [only=<regex>] [dump=<dir>] [maxring=<n>] [conflicts=<limit>] [crosscheck=<decisions>] [rup=0|1] [list=0|1] [selftest=1]
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using g3;
using gs;

static class Program
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    class Fn : BoundedImplicitFunction3d
    {
        public Func<Vector3d, double> F; public AxisAlignedBox3d B;
        public double Value(ref Vector3d p) { return F(p); }
        public AxisAlignedBox3d Bounds() { return B; }
    }
    static string F(double x, string fmt) { return x.ToString(fmt, CultureInfo.InvariantCulture); }

    // the inputs of benchmarks/QuadFlow3BGate (a copy: the same shapes, variations and construction)
    static DMesh3 MakeInput(Func<Vector3d, double> fn, double h, double noise)
    {
        var B = new AxisAlignedBox3d(new Vector3d(-1.6, -1.6, -1.6), new Vector3d(1.6, 1.6, 1.6));
        var mc = new MarchingCubes { Implicit = new Fn { F = fn, B = B }, Bounds = B, CubeSize = h * 0.5, ParallelCompute = false, RootMode = MarchingCubes.RootfindingModes.Bisection };
        mc.Generate();
        DMesh3 m = mc.Mesh;
        DMesh3 src = new DMesh3(m);
        var r = new Remesher(m) { SmoothSpeedT = 0.5 };
        r.SetProjectionTarget(MeshProjectionTarget.Auto(src));
        r.SetTargetEdgeLength(h);
        for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
        m = new DMesh3(m, true);
        if (noise > 0) {
            var rnd = new Random(1);
            MeshNormals.QuickCompute(m);
            foreach (int v in m.VertexIndices()) { Vector3d n = (Vector3d)m.GetVertexNormal(v); m.SetVertex(v, m.GetVertex(v) + n * ((rnd.NextDouble() * 2 - 1) * noise * h)); }
        }
        return m;
    }

    static int FacingAway(DPolyMesh r, DMesh3 input, DMeshAABBTree3 tree)
    {
        int bad = 0;
        for (int f = 0; f < r.FaceCount; ++f) {
            var fv = r.GetFace(f);
            if (fv.Length != 4) { bad++; continue; }
            Vector3d p0 = r.GetVertex(fv[0]), p1 = r.GetVertex(fv[1]), p2 = r.GetVertex(fv[2]), p3 = r.GetVertex(fv[3]);
            Vector3d n = (p2 - p0).Cross(p3 - p1), c = 0.25 * (p0 + p1 + p2 + p3);
            int t = tree.FindNearestTriangle(c);
            if (t < 0 || n.Dot(input.GetTriNormal(t)) <= 0) bad++;
        }
        return bad;
    }

    // ------------------------------------------------------------------------------------------ the map of the repair (a copy)

    /// <summary> the integer map of QuadFlowRepair after the subdivision: per triangle slot the offset, the neighbour, its slot and the frame rotation </summary>
    sealed class Map
    {
        public int N;
        public int[] V, Nb, NbS, Rot, X, Y;
        public Map Clone() { return new Map { N = N, V = V, Nb = Nb, NbS = NbS, Rot = Rot, X = (int[])X.Clone(), Y = (int[])Y.Clone() }; }
        public int Det(int f) { return X[3 * f] * Y[3 * f + 1] - Y[3 * f] * X[3 * f + 1]; }
        public int Inverted() { int n = 0; for (int f = 0; f < N; ++f) if (Det(f) < 0) n++; return n; }
    }

    static object Get(object o, string name)
    {
        FieldInfo fi = o.GetType().GetField(name, Any);
        if (fi == null) throw new MissingFieldException(o.GetType().Name, name);
        return fi.GetValue(o);
    }
    static void Set(object o, string name, object v)
    {
        FieldInfo fi = o.GetType().GetField(name, Any);
        if (fi == null) throw new MissingFieldException(o.GetType().Name, name);
        fi.SetValue(o, v);
    }
    static object Call(object o, string name, params object[] args)
    {
        MethodInfo mi = o.GetType().GetMethod(name, Any);
        if (mi == null) throw new MissingMethodException(o.GetType().Name, name);
        return mi.Invoke(o, args);
    }

    static Map ReadMap(object rep)
    {
        var tris = (IList)Get(rep, "tris");
        var m = new Map { N = tris.Count };
        m.V = new int[3 * m.N]; m.Nb = new int[3 * m.N]; m.NbS = new int[3 * m.N]; m.Rot = new int[3 * m.N]; m.X = new int[3 * m.N]; m.Y = new int[3 * m.N];
        for (int f = 0; f < m.N; ++f) {
            object t = tris[f];
            var v = (int[])Get(t, "V"); var nb = (int[])Get(t, "Nb"); var nbs = (int[])Get(t, "NbS"); var rot = (int[])Get(t, "Rot"); var d = (Array)Get(t, "D");
            for (int s = 0; s < 3; ++s) {
                m.V[3 * f + s] = v[s]; m.Nb[3 * f + s] = nb[s]; m.NbS[3 * f + s] = nbs[s]; m.Rot[3 * f + s] = rot[s];
                object o = d.GetValue(s); m.X[3 * f + s] = (int)Get(o, "X"); m.Y[3 * f + s] = (int)Get(o, "Y");
            }
        }
        return m;
    }

    static void WriteMap(object rep, Map m)
    {
        var tris = (IList)Get(rep, "tris");
        if (tris.Count != m.N) throw new InvalidOperationException("the triangle count changed");
        for (int f = 0; f < m.N; ++f) {
            var d = (Array)Get(tris[f], "D");
            for (int s = 0; s < 3; ++s) {
                object o = d.GetValue(s); Set(o, "X", m.X[3 * f + s]); Set(o, "Y", m.Y[3 * f + s]);
                d.SetValue(o, s);
            }
        }
    }

    // ------------------------------------------------------------------------------------------ one local instance (QuadFlowRepair.TrySat)

    static (int x, int y) RotOff((int x, int y) v, int k) { k = ((k % 4) + 4) % 4; for (int i = 0; i < k; ++i) v = (-v.y, v.x); return v; }
    static readonly (int x, int y)[] Dom = BuildDom();
    static (int x, int y)[] BuildDom() { var l = new List<(int, int)>(); for (int y = -1; y <= 1; ++y) for (int x = -1; x <= 1; ++x) l.Add((x, y)); return l.ToArray(); }
    static int DomIdx((int x, int y) d) { return (d.y + 1) * 3 + (d.x + 1); }

    sealed class Instance
    {
        public int Seed, Ring, M;
        public List<int> Region;
        public (int f, int s)[] Masters;
        public int[][] Groups;
        public List<int[]> Clauses = new List<int[]>();
        /// <summary> the build ended before a solver was called: no mutable edge, a triangle with only fixed edges that is open or inverted, or a triangle with one mutable edge and no value for it </summary>
        public bool Trivial;
        public int NVars { get { return 9 * M; } }
    }

    /// <summary>
    /// the instance of QuadFlowRepair.TrySat(seed, ring): the offsets of the edges between two triangles of the region (the triangles within
    /// `ring` steps of the seed) are the variables (nine values each, exactly one); every triangle of the region must have sum 0 and
    /// det >= 0. Same variable numbering, group order and clauses as the library.
    /// </summary>
    static Instance Build(Map m, int seed, int ring)
    {
        var inst = new Instance { Seed = seed, Ring = ring };
        var dist = new Dictionary<int, int> { { seed, 0 } };
        var region = new List<int> { seed };
        for (int qi = 0; qi < region.Count; ++qi) {
            int f = region[qi]; if (dist[f] >= ring) continue;
            for (int sl = 0; sl < 3; ++sl) {
                int nb = m.Nb[3 * f + sl];
                if (nb >= 0 && !dist.ContainsKey(nb)) { dist[nb] = dist[f] + 1; region.Add(nb); }
            }
        }
        inst.Region = region;
        var sorted = region.ToArray(); Array.Sort(sorted);
        var masters = new List<(int f, int s)>();
        var slotVar = new Dictionary<int, int>();
        foreach (int f in sorted)
            for (int sl = 0; sl < 3; ++sl) {
                int nb = m.Nb[3 * f + sl];
                if (nb >= 0 && dist.ContainsKey(nb) && nb > f) {
                    int id = masters.Count; masters.Add((f, sl));
                    slotVar[3 * f + sl] = id; slotVar[3 * nb + m.NbS[3 * f + sl]] = -1 - id;
                }
            }
        int M = masters.Count; inst.M = M; inst.Masters = masters.ToArray();
        if (M == 0) { inst.Trivial = true; return inst; }
        inst.Groups = new int[M][];
        for (int e = 0; e < M; ++e) {
            var (f, sl) = masters[e];
            int cur = DomIdx((m.X[3 * f + sl], m.Y[3 * f + sl]));
            var order = new List<int>();
            for (int k = 0; k < 9; ++k) if (k != cur) order.Add(k);
            var cv = Dom[cur];
            int L1(int a) { return Math.Abs(Dom[a].x - cv.x) + Math.Abs(Dom[a].y - cv.y); }
            order.Sort((a, b) => { int c = L1(a).CompareTo(L1(b)); return c != 0 ? c : a.CompareTo(b); });
            inst.Groups[e] = new int[9]; inst.Groups[e][0] = 9 * e + cur + 1;
            for (int k = 0; k < 8; ++k) inst.Groups[e][k + 1] = 9 * e + order[k] + 1;
            var all = new int[9]; for (int k = 0; k < 9; ++k) all[k] = 9 * e + k + 1;
            inst.Clauses.Add(all);
            for (int a = 0; a < 9; ++a) for (int b = a + 1; b < 9; ++b) inst.Clauses.Add(new[] { -(9 * e + a + 1), -(9 * e + b + 1) });
        }
        var varOf = new int[3]; var rotOf = new int[3]; var fixedVal = new (int x, int y)[3]; var mutSlots = new int[3];
        foreach (int f in region) {
            int mk = 0;
            for (int sl = 0; sl < 3; ++sl) {
                varOf[sl] = -1; fixedVal[sl] = (m.X[3 * f + sl], m.Y[3 * f + sl]);
                if (slotVar.TryGetValue(3 * f + sl, out int id)) {
                    if (id >= 0) { varOf[sl] = id; rotOf[sl] = 0; }
                    else { varOf[sl] = -1 - id; var mm = masters[-1 - id]; rotOf[sl] = mm.f == f ? 0 : (m.Rot[3 * mm.f + mm.s] + 2) % 4; }
                    mutSlots[mk++] = sl;
                }
            }
            if (mk == 0) {
                bool open = fixedVal[0].x + fixedVal[1].x + fixedVal[2].x != 0 || fixedVal[0].y + fixedVal[1].y + fixedVal[2].y != 0;
                if (open || fixedVal[0].x * fixedVal[1].y - fixedVal[0].y * fixedVal[1].x < 0) { inst.Trivial = true; return inst; }
                continue;
            }
            int last = mutSlots[mk - 1]; int free = mk - 1; int combos = 1; for (int i = 0; i < free; ++i) combos *= 9;
            var picks = new int[free];
            for (int c = 0; c < combos; ++c) {
                int cc = c; var val = new[] { fixedVal[0], fixedVal[1], fixedVal[2] };
                for (int i = 0; i < free; ++i) { int k = cc % 9; cc /= 9; picks[i] = k; int sl = mutSlots[i]; val[sl] = RotOff(Dom[k], rotOf[sl]); }
                (int x, int y) need = (-(val[0].x + val[1].x + val[2].x - val[last].x), -(val[0].y + val[1].y + val[2].y - val[last].y));
                var masterVal = RotOff(need, -rotOf[last]);
                bool okv = Math.Max(Math.Abs(masterVal.x), Math.Abs(masterVal.y)) <= 1;
                if (okv) { val[last] = need; if (val[0].x * val[1].y - val[0].y * val[1].x < 0) okv = false; }
                var lits = new int[free + (okv ? 1 : 0)];
                for (int i = 0; i < free; ++i) lits[i] = -(9 * varOf[mutSlots[i]] + picks[i] + 1);
                if (okv) lits[free] = 9 * varOf[last] + DomIdx(masterVal) + 1;
                if (lits.Length == 0) { inst.Trivial = true; return inst; }
                inst.Clauses.Add(lits);
            }
        }
        return inst;
    }

    /// <summary> the smallest ring from `from` to `to` whose region around the seed has a sum of det >= 0 (a necessary condition for a fold-free region without a singular vertex); to + 1 if none </summary>
    static int FirstRingWithArea(Map m, int seed, int from, int to)
    {
        var dist = new Dictionary<int, int> { { seed, 0 } };
        var region = new List<int> { seed };
        long sum = m.Det(seed); int qi = 0;
        for (int ring = 1; ring <= to; ++ring) {
            for (; qi < region.Count && dist[region[qi]] < ring; ++qi) {
                int f = region[qi];
                for (int sl = 0; sl < 3; ++sl) {
                    int nb = m.Nb[3 * f + sl];
                    if (nb >= 0 && !dist.ContainsKey(nb)) { dist[nb] = dist[f] + 1; region.Add(nb); sum += m.Det(nb); }
                }
            }
            if (ring >= from && sum >= 0) return ring;
        }
        return to + 1;
    }

    static void Apply(Map m, Instance inst, bool[] model)
    {
        for (int e = 0; e < inst.M; ++e) {
            var (f, sl) = inst.Masters[e];
            (int x, int y) nv = (0, 0); int n = 0;
            for (int k = 0; k < 9; ++k) if (model[9 * e + k]) { nv = Dom[k]; n++; }
            if (n != 1) throw new InvalidOperationException("the model has not exactly one value for an edge");
            m.X[3 * f + sl] = nv.x; m.Y[3 * f + sl] = nv.y;
            int nb = m.Nb[3 * f + sl], ns = m.NbS[3 * f + sl];
            var o = RotOff(nv, m.Rot[3 * f + sl]);
            m.X[3 * nb + ns] = -o.x; m.Y[3 * nb + ns] = -o.y;
        }
    }

    static bool ModelHolds(Instance inst, bool[] model)
    {
        foreach (var c in inst.Clauses) {
            bool sat = false;
            foreach (int l in c) if (l > 0 ? model[l - 1] : !model[-l - 1]) { sat = true; break; }
            if (!sat) return false;
        }
        return true;
    }

    /// <summary> after a model was applied: every triangle of the region closes and has det >= 0 (the meaning of the clauses, checked on the map) </summary>
    static bool RegionValid(Map m, Instance inst)
    {
        foreach (int f in inst.Region) {
            if (m.X[3 * f] + m.X[3 * f + 1] + m.X[3 * f + 2] != 0 || m.Y[3 * f] + m.Y[3 * f + 1] + m.Y[3 * f + 2] != 0 || m.Det(f) < 0) return false;
            for (int s = 0; s < 3; ++s) if (Math.Max(Math.Abs(m.X[3 * f + s]), Math.Abs(m.Y[3 * f + s])) > 1 && inst.Masters.Contains((f, s))) return false;
        }
        return true;
    }

    static void WriteDimacs(string path, Instance inst, string verdict, string comment)
    {
        var sb = new StringBuilder();
        sb.Append("c QuadFlow3BSat: local repair instance of QuadFlowRepair.TrySat\n");
        sb.Append("c ").Append(comment).Append('\n');
        sb.Append("c seed triangle ").Append(inst.Seed).Append(", ring ").Append(inst.Ring).Append(", region triangles ").Append(inst.Region.Count).Append(", mutable edges ").Append(inst.M).Append(", verdict ").Append(verdict).Append('\n');
        sb.Append("c variable 9 e + k + 1: edge e has the offset Dom[k] = (k % 3 - 1, k / 3 - 1) in the frame of its master triangle\n");
        for (int e = 0; e < inst.M; ++e) sb.Append("c edge ").Append(e).Append(": triangle ").Append(inst.Masters[e].f).Append(" slot ").Append(inst.Masters[e].s).Append(", preference ").Append(string.Join(" ", inst.Groups[e])).Append('\n');
        sb.Append("p cnf ").Append(inst.NVars).Append(' ').Append(inst.Clauses.Count).Append('\n');
        foreach (var c in inst.Clauses) { foreach (int l in c) sb.Append(l).Append(' '); sb.Append("0\n"); }
        File.WriteAllText(path, sb.ToString());
    }

    // ------------------------------------------------------------------------------------------ the two solvers

    static Type dpllType;
    /// <summary> the library's GroupSatSolver (by reflection); 1 satisfiable, 0 unsatisfiable, -1 budget </summary>
    static int Dpll(Instance inst, long budget, out bool[] model, out long decisions)
    {
        if (dpllType == null) dpllType = typeof(QuadRemesher).Assembly.GetType("gs.GroupSatSolver", true);
        object s = Activator.CreateInstance(dpllType, Any, null, new object[] { inst.NVars }, null);
        Set(s, "DecisionBudget", budget);
        var add = (Action<int[]>)Delegate.CreateDelegate(typeof(Action<int[]>), s, dpllType.GetMethod("AddClause", Any));
        foreach (var c in inst.Clauses) add((int[])c.Clone());      // the solver reorders its clauses in place
        var args = new object[] { inst.Groups.Select(g => (int[])g.Clone()).ToArray(), null };
        int r = (int)dpllType.GetMethod("Solve", Any).Invoke(s, args);
        model = (bool[])args[1]; decisions = (long)Get(s, "Decisions");
        return r;
    }

    sealed class Tally
    {
        public long Sat, Unsat, Undecided, Trivial, Conflicts, Decisions, MaxConflicts, RupOk, RupFail, ModelFail, CrossAgree, CrossDisagree, CrossOpen;
    }

    static long conflictLimit, crossBudget; static bool rup = true;

    /// <summary> decides an instance with the clause-learning solver and checks the answer </summary>
    static int Cdcl(Instance inst, Tally t, out bool[] model)
    {
        var s = new CdclSolver(inst.NVars) { ConflictLimit = conflictLimit };
        foreach (var c in inst.Clauses) s.AddClause(c);
        int r = s.Solve(inst.Groups, out model);
        t.Conflicts += s.Conflicts; t.Decisions += s.Decisions; t.MaxConflicts = Math.Max(t.MaxConflicts, s.Conflicts);
        if (r == CdclSolver.Satisfiable) { t.Sat++; if (!ModelHolds(inst, model)) t.ModelFail++; }
        else if (r == CdclSolver.Unsatisfiable) {
            t.Unsat++;
            if (rup) { if (RupChecker.Check(inst.NVars, inst.Clauses, s.Learned)) t.RupOk++; else t.RupFail++; }
        } else t.Undecided++;
        if (crossBudget > 0 && r != CdclSolver.Undecided) {
            int d = Dpll(inst, crossBudget, out _, out _);
            if (d < 0) t.CrossOpen++; else if (d == r) t.CrossAgree++; else t.CrossDisagree++;
        }
        return r;
    }

    // ------------------------------------------------------------------------------------------ vertex fans (singular vertices)

    /// <summary> per vertex of the map: true if the frame rotations around its fan do not sum to 0 mod 4 (a singular vertex) or the fan is open </summary>
    static bool[] SingularVertices(Map m)
    {
        int nv = 0; foreach (int v in m.V) nv = Math.Max(nv, v + 1);
        var cornerF = new int[nv]; var cornerI = new int[nv]; var res = new bool[nv];
        for (int i = 0; i < nv; ++i) cornerF[i] = -1;
        for (int f = 0; f < m.N; ++f) for (int c = 0; c < 3; ++c) if (cornerF[m.V[3 * f + c]] < 0) { cornerF[m.V[3 * f + c]] = f; cornerI[m.V[3 * f + c]] = c; }
        for (int u = 0; u < nv; ++u) {
            if (cornerF[u] < 0) continue;
            int f = cornerF[u], i = cornerI[u], c = 0; bool closed = false;
            for (int guard = 0; guard < 200; ++guard) {
                int sl = (i + 2) % 3, nf = m.Nb[3 * f + sl]; if (nf < 0) break;
                c = (c + m.Rot[3 * f + sl]) % 4;
                i = m.NbS[3 * f + sl]; f = nf;
                if (f == cornerF[u] && i == cornerI[u]) { closed = true; break; }
            }
            res[u] = !closed || c % 4 != 0;
        }
        return res;
    }

    // ------------------------------------------------------------------------------------------ main

    static int SelfTest()
    {
        // random small formulas with groups of three: the clause-learning solver against the library's DPLL without a budget
        var rnd = new Random(7); int agree = 0, bad = 0, sat = 0;
        for (int it = 0; it < 4000; ++it) {
            int G = 4 + rnd.Next(10), n = 3 * G;
            var inst = new Instance { Seed = 0, Ring = 0, M = 0, Region = new List<int>() };
            var groups = new int[G][];
            for (int g = 0; g < G; ++g) {
                groups[g] = new[] { 3 * g + 1, 3 * g + 2, 3 * g + 3 };
                inst.Clauses.Add((int[])groups[g].Clone());
                for (int a = 0; a < 3; ++a) for (int b = a + 1; b < 3; ++b) inst.Clauses.Add(new[] { -(3 * g + a + 1), -(3 * g + b + 1) });
            }
            int extra = (int)(G * (2.0 + 3.0 * rnd.NextDouble()));
            for (int k = 0; k < extra; ++k) { int len = 2 + rnd.Next(2); var c = new int[len]; for (int i = 0; i < len; ++i) c[i] = (rnd.Next(2) == 0 ? 1 : -1) * (1 + rnd.Next(n)); inst.Clauses.Add(c); }
            var s = new CdclSolver(n) { RestartUnit = 2 };
            foreach (var c in inst.Clauses) s.AddClause(c);
            int r = s.Solve(groups, out bool[] model);
            inst.Groups = groups;
            // Dpll needs NVars = 9 M; build it directly
            if (dpllType == null) dpllType = typeof(QuadRemesher).Assembly.GetType("gs.GroupSatSolver", true);
            object d = Activator.CreateInstance(dpllType, Any, null, new object[] { n }, null);
            Set(d, "DecisionBudget", long.MaxValue);
            foreach (var c in inst.Clauses) dpllType.GetMethod("AddClause", Any).Invoke(d, new object[] { c.Clone() });
            var args = new object[] { groups, null };
            int rd = (int)dpllType.GetMethod("Solve", Any).Invoke(d, args);
            bool ok = r == rd;
            if (r == 1) { sat++; ok &= ModelHolds(inst, model); }
            if (r == 0) ok &= RupChecker.Check(n, inst.Clauses, s.Learned);
            // a wrong proof must be refused: a satisfiable formula has no refutation, neither with the solver's learned clauses nor with
            // three units that exclude every value of the first group
            if (r == 1) ok &= !RupChecker.Check(n, inst.Clauses, s.Learned) && !RupChecker.Check(n, inst.Clauses, new List<int[]> { new[] { -1 }, new[] { -2 }, new[] { -3 } });
            if (ok) agree++; else bad++;
        }
        Console.WriteLine($"selftest: 4000 random formulas, {sat} satisfiable, agree with the library's DPLL and pass the checks: {agree}, failures: {bad}");
        return bad == 0 ? 0 : 1;
    }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        if (A.ContainsKey("selftest")) return SelfTest();
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        string dump = A.ContainsKey("dump") ? A["dump"] : null;
        int maxRingArg = A.ContainsKey("maxring") ? int.Parse(A["maxring"], CultureInfo.InvariantCulture) : 0;
        conflictLimit = A.ContainsKey("conflicts") ? long.Parse(A["conflicts"], CultureInfo.InvariantCulture) : 0;
        crossBudget = A.ContainsKey("crosscheck") ? long.Parse(A["crosscheck"], CultureInfo.InvariantCulture) : 20000;
        rup = !A.ContainsKey("rup") || A["rup"] != "0";
        bool list = !A.ContainsKey("list") || A["list"] != "0";
        Func<Vector3d, double> sphere = p => p.Length - 1;
        Func<Vector3d, double> torus = p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 1; return Math.Sqrt(q * q + p.z * p.z) - 0.35; };
        Func<Vector3d, double> bumpy = p => p.Length - 1 - 0.08 * Math.Sin(4 * p.x) * Math.Sin(4 * p.y) * Math.Sin(4 * p.z);
        Vector3d ext = new Vector3d(1, 0.7, 0.5); double fr = 0.15;
        Func<Vector3d, double> rbox = p => {
            Vector3d q = new Vector3d(Math.Abs(p.x) - (ext.x - fr), Math.Abs(p.y) - (ext.y - fr), Math.Abs(p.z) - (ext.z - fr));
            Vector3d qp = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return qp.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0) - fr;
        };
        Func<Vector3d, double> capsule = p => { double x = Math.Max(-0.6, Math.Min(0.6, p.x)); return new Vector3d(p.x - x, p.y, p.z).Length - 0.5; };
        Func<Vector3d, double> genus2 = p => {
            Func<double, double> tor = cx => { double q = Math.Sqrt((p.x - cx) * (p.x - cx) + p.y * p.y) - 0.6; return Math.Sqrt(q * q + p.z * p.z) - 0.22; };
            double a = tor(-0.62), b = tor(0.62), k = 0.1, h = Math.Max(k - Math.Abs(a - b), 0) / k;
            return Math.Min(a, b) - h * h * k * 0.25;
        };
        Func<Vector3d, double> cylinder = p => { double dx = Math.Sqrt(p.x * p.x + p.y * p.y) - 0.6, dz = Math.Abs(p.z) - 0.8; return Math.Min(Math.Max(dx, dz), 0) + new Vector2d(Math.Max(dx, 0), Math.Max(dz, 0)).Length; };
        var shapes = new List<(string, Func<Vector3d, double>, double, double, double)> {
            ("sphere", sphere, 0.1, 0.0, 0.0), ("torus", torus, 0.1, 0.0, 0.0), ("torus-align10", torus, 0.1, 10.0, 0.0),
            ("bumpy", bumpy, 0.06, 0.0, 0.0), ("filleted-box", rbox, 0.08, 0.0, 0.0), ("noisy-sphere", sphere, 0.1, 0.0, 0.2),
            ("capsule", capsule, 0.08, 0.0, 0.0), ("genus2", genus2, 0.06, 0.0, 0.0), ("capped-cylinder", cylinder, 0.08, 0.0, 0.0),
        };
        var variations = new (double scale, double ratio)[] { (1, 0.5), (0.8, 0.5), (1.25, 0.5), (1.5, 0.5), (1, 0.35), (1, 0.7) };
        Type repType = typeof(QuadRemesher).Assembly.GetType("gs.QuadFlowRepair", true);

        Console.WriteLine("| shape | scale / ratio | library status | ended by | folds after greedy | library: attempts / sat / unsat / budget (decisions) | replay = library | budget instances: sat / unsat / undecided | learning: instances sat / unsat / undecided | seeds solved / obstructed | folds left | conflicts sum / max | completed status | class |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        int total = 0, clean = 0, fallbacks = 0, foldFree = 0, completedClean = 0, replayMismatch = 0, checkFailures = 0;
        var endedBy = new SortedDictionary<string, int>(); var libStatus = new SortedDictionary<string, int>(); var classes = new SortedDictionary<string, int>();
        var all = new Tally(); var budgetAll = new Tally();
        long obstructedSeeds = 0, solvedSeeds = 0, obsSingular = 0, obsSeedSingular = 0, obsAlone = 0, obsFew = 0, obsMany = 0, obsAreaNeg = 0, obsAreaZero = 0, obsAreaPos = 0;
        var ringHist = new long[64]; var areaRing = new long[5]; var obstructionLines = new List<string>();
        foreach (var (name, fn, s0, align, noise) in shapes) {
            if (only != null && !only.IsMatch(name)) continue;
            foreach (var (scale, ratio) in variations) {
                double s = s0 * scale;
                string runId = $"{name}_{F(scale, "G3")}_{F(ratio, "G3")}";
                DMesh3 input = MakeInput(fn, s * ratio, noise);
                var cf = new CrossField(input) { CurvatureAlignment = align }; cf.Compute();
                if (!cf.Success) { Console.WriteLine($"| {name} | {scale}/{ratio} | cross field failed {cf.Status} |"); continue; }
                var q3 = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PureQuads = true };
                q3.Compute();
                total++;
                libStatus.TryGetValue(q3.PureQuadStatus.ToString(), out int lc); libStatus[q3.PureQuadStatus.ToString()] = lc + 1;
                if (q3.PureQuadStatus == PureQuadStatus.Success) { clean++; Console.WriteLine($"| {name} | {F(scale, "G3")} / {F(ratio, "G3")} | Success | - | | | | | | | | | | clean |"); continue; }
                fallbacks++;
                object lib = Get(q3, "PureQuadRepair");
                int splits = (int)Get(lib, "Splits"), maxSplits = (int)Get(lib, "MaxSplits"), libAttempts = (int)Get(lib, "SatAttempts"), maxAttempts = (int)Get(lib, "SatMaxAttempts"), maxRing = (int)Get(lib, "SatMaxRing");
                int libSolved = (int)Get(lib, "SatSolved"), libFailed = (int)Get(lib, "SatFailed");
                long libDecisions = (long)Get(lib, "SatDecisions"), totalBudget = (long)Get(lib, "SatTotalBudget"), attemptBudget = (long)Get(lib, "SatAttemptBudget");
                bool exhausted = (bool)Get(lib, "SatBudgetExhausted");
                string ended = q3.PureQuadStatus != PureQuadStatus.BudgetExceeded && q3.PureQuadStatus != PureQuadStatus.OrientationFolds ? "not the repair"
                    : splits > maxSplits ? "splits" : libDecisions >= totalBudget ? "SAT decisions" : libAttempts >= maxAttempts ? "SAT attempts" : exhausted ? "SAT rounds" : "none (every seed failed)";
                endedBy.TryGetValue(ended, out int ec); endedBy[ended] = ec + 1;
                if (ended == "not the repair" || ended == "splits") {
                    classes.TryGetValue(ended, out int cc0); classes[ended] = cc0 + 1;
                    Console.WriteLine($"| {name} | {F(scale, "G3")} / {F(ratio, "G3")} | {q3.PureQuadStatus} | {ended} | {q3.InvertedTriangles} | | | | | | | | | {ended} |"); continue;
                }
                // the state after the greedy moves: the same repair with no SAT budget (RepairSat then changes nothing)
                object rep = Activator.CreateInstance(repType, Any, null, new object[] { input, cf, q3.Positions, s }, null);
                Set(rep, "SatTotalBudget", 0L);
                Call(rep, "Run");
                Map start = ReadMap(rep);
                int folds0 = start.Inverted();
                string dir = null;
                if (dump != null) { dir = Path.Combine(dump, runId); Directory.CreateDirectory(dir); }

                // ---- the library trajectory (the library's DPLL and budgets)
                var mL = start.Clone();
                int attempts = 0, solved = 0, failedN = 0, nSat = 0, nUnsat = 0, nBudget = 0; long decisions = 0;
                var bt = new Tally();
                {
                    var failed = new bool[mL.N]; int scan = 0;
                    for (int round = 0; round < 10000; ++round) {
                        if (attempts >= maxAttempts) break;
                        int seed = -1;
                        for (; scan < mL.N; ++scan) if (mL.Det(scan) < 0 && !failed[scan]) { seed = scan; break; }
                        if (seed < 0) break;
                        bool ok = false;
                        for (int ring = 1; ring <= maxRing && !ok && decisions < totalBudget; ++ring) {
                            Instance inst = Build(mL, seed, ring);
                            if (inst.Trivial) continue;
                            attempts++;
                            int r = Dpll(inst, Math.Min(attemptBudget, Math.Max(1, totalBudget - decisions)), out bool[] model, out long dec);
                            decisions += dec;
                            if (r == 1) { nSat++; Apply(mL, inst, model); ok = true; }
                            else if (r == 0) nUnsat++;
                            else {
                                nBudget++;
                                int rc = Cdcl(inst, bt, out _);
                                string verdict = rc == 1 ? "SAT" : rc == 0 ? "UNSAT" : "UNDECIDED";
                                if (dir != null) WriteDimacs(Path.Combine(dir, $"budget_a{attempts}_seed{seed}_r{ring}_{verdict}.cnf"), inst, verdict, $"{runId}: the library's DPLL ended at its budget on this instance (library trajectory, attempt {attempts})");
                            }
                        }
                        if (ok) solved++; else { failedN++; failed[seed] = true; }
                        if (decisions >= totalBudget) break;
                    }
                }
                bool same = attempts == libAttempts && solved == libSolved && failedN == libFailed && decisions == libDecisions && mL.Inverted() == q3.InvertedTriangles;
                if (!same) replayMismatch++;
                budgetAll.Sat += bt.Sat; budgetAll.Unsat += bt.Unsat; budgetAll.Undecided += bt.Undecided;

                // ---- the learning trajectory (no budget)
                var mC = start.Clone();
                var t = new Tally(); int cSolved = 0, cObstructed = 0;
                int ringCap = maxRingArg > 0 ? maxRingArg : maxRing;
                bool[] singular = SingularVertices(mC);
                {
                    var failed = new bool[mC.N]; int scan = 0;
                    while (true) {
                        int seed = -1;
                        for (; scan < mC.N; ++scan) if (mC.Det(scan) < 0 && !failed[scan]) { seed = scan; break; }
                        if (seed < 0) break;
                        bool ok = false; var tried = new List<(Instance inst, int r)>();
                        for (int ring = 1; ring <= ringCap && !ok; ++ring) {
                            Instance inst = Build(mC, seed, ring);
                            if (inst.Trivial) { t.Trivial++; tried.Add((inst, 0)); continue; }
                            int r = Cdcl(inst, t, out bool[] model);
                            tried.Add((inst, r));
                            if (r == 1) {
                                Apply(mC, inst, model); ok = true; ringHist[ring]++;
                                if (!RegionValid(mC, inst) || mC.Det(seed) < 0) t.ModelFail++;
                            }
                        }
                        if (ok) { cSolved++; continue; }
                        failed[seed] = true; cObstructed++;
                        Instance big = tried[tried.Count - 1].inst;
                        int inv = big.Region.Count(f => mC.Det(f) < 0);
                        var verts = new HashSet<int>(); foreach (int f in big.Region) for (int c = 0; c < 3; ++c) verts.Add(mC.V[3 * f + c]);
                        int sing = verts.Count(v => singular[v]);
                        bool seedSing = Enumerable.Range(0, 3).Any(c => singular[mC.V[3 * seed + c]]);
                        int area = big.Region.Sum(f => mC.Det(f));      // twice the lattice area of the region
                        if (area < 0) obsAreaNeg++; else if (area == 0) obsAreaZero++; else obsAreaPos++;
                        if (area < 0) { int first = FirstRingWithArea(mC, seed, big.Ring + 1, 20); areaRing[first <= big.Ring + 2 ? 0 : first <= big.Ring + 6 ? 1 : first <= 20 ? 2 : 3]++; }
                        if (sing > 0) obsSingular++; if (seedSing) obsSeedSingular++;
                        if (inv == 1) obsAlone++; else if (inv <= 5) obsFew++; else obsMany++;
                        string verdicts = string.Join(" ", tried.Select(x => x.inst.Trivial ? "T" : x.r == 0 ? "U" : "?"));
                        obstructionLines.Add($"| {name} | {F(scale, "G3")} / {F(ratio, "G3")} | {seed} | {mC.Det(seed)} | {verdicts} | {big.Region.Count} | {big.M} | {inv} | {area} | {sing} | {(seedSing ? "yes" : "no")} |");
                        if (dir != null)
                            foreach (var (inst, r) in tried) {
                                if (inst.Trivial) continue;
                                string verdict = r == 0 ? "UNSAT" : "UNDECIDED";
                                WriteDimacs(Path.Combine(dir, $"obstruction_seed{seed}_r{inst.Ring}_{verdict}.cnf"), inst, verdict, $"{runId}: learning trajectory, a seed with no satisfiable instance up to ring {ringCap}");
                            }
                    }
                }
                int foldsLeft = mC.Inverted();
                string completed = "-";
                if (foldsLeft == 0) {
                    foldFree++;
                    WriteMap(rep, mC);
                    if (!(bool)Call(rep, "MapCloses")) completed = "FlowInfeasible (map open)";
                    else {
                        object pos = Call(rep, "SolvePositions");
                        if (pos == null) completed = "FlowInfeasible (positions)";
                        else {
                            Call(rep, "Extract", pos);
                            var st = (PureQuadStatus)Get(rep, "Status"); completed = st.ToString();
                            if (st == PureQuadStatus.Success) {
                                var res = (DPolyMesh)Get(rep, "Result");
                                PolyMeshMetrics pm = PolyMeshMetrics.Compute(res);
                                bool finite = Enumerable.Range(0, res.VertexCount).All(v => res.GetVertex(v).IsFinite);
                                bool bad = !pm.IsManifold || pm.QuadFraction < 1 || !finite || !res.Validate().IsValid || FacingAway(res, input, new DMeshAABBTree3(input, true)) > 0;
                                if (bad) completed = "Success, independent check FAILED"; else completedClean++;
                            }
                        }
                    }
                }
                string cls = foldsLeft == 0 ? "search (fold-free with learning)" : t.Undecided > 0 ? "undecided" : "problem (obstructed seeds)";
                classes.TryGetValue(cls, out int cc); classes[cls] = cc + 1;
                all.Sat += t.Sat; all.Unsat += t.Unsat; all.Undecided += t.Undecided; all.Trivial += t.Trivial; all.Conflicts += t.Conflicts + bt.Conflicts; all.Decisions += t.Decisions + bt.Decisions;
                all.MaxConflicts = Math.Max(all.MaxConflicts, Math.Max(t.MaxConflicts, bt.MaxConflicts));
                all.RupOk += t.RupOk + bt.RupOk; all.RupFail += t.RupFail + bt.RupFail; all.ModelFail += t.ModelFail + bt.ModelFail;
                all.CrossAgree += t.CrossAgree + bt.CrossAgree; all.CrossDisagree += t.CrossDisagree + bt.CrossDisagree; all.CrossOpen += t.CrossOpen + bt.CrossOpen;
                checkFailures += (int)(t.RupFail + bt.RupFail + t.ModelFail + bt.ModelFail + t.CrossDisagree + bt.CrossDisagree);
                solvedSeeds += cSolved; obstructedSeeds += cObstructed;
                Console.WriteLine($"| {name} | {F(scale, "G3")} / {F(ratio, "G3")} | {q3.PureQuadStatus} | {ended} | {folds0} | {attempts} / {nSat} / {nUnsat} / {nBudget} ({decisions}) | {(same ? "yes" : "NO")} | {bt.Sat} / {bt.Unsat} / {bt.Undecided} | {t.Sat} / {t.Unsat + t.Trivial} / {t.Undecided} | {cSolved} / {cObstructed} | {foldsLeft} | {t.Conflicts} / {t.MaxConflicts} | {completed} | {cls} |");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"runs {total}: library clean {clean}, fallback {fallbacks}; library status: {string.Join(", ", libStatus.Select(kv => kv.Key + " " + kv.Value))}");
        Console.WriteLine($"fallbacks ended by: {string.Join(", ", endedBy.Select(kv => kv.Key + " " + kv.Value))}");
        Console.WriteLine($"library trajectory replayed with the library's counters: {fallbacks - replayMismatch - classes.Where(kv => kv.Key == "splits" || kv.Key == "not the repair").Sum(kv => kv.Value)} runs, mismatches {replayMismatch}");
        Console.WriteLine($"instances the library's DPLL left at its budget: satisfiable {budgetAll.Sat}, unsatisfiable {budgetAll.Unsat}, undecided {budgetAll.Undecided}");
        Console.WriteLine($"learning trajectory: instances satisfiable {all.Sat}, unsatisfiable {all.Unsat} (+ {all.Trivial} refuted while built), undecided {all.Undecided}; seeds solved {solvedSeeds}, obstructed {obstructedSeeds}");
        Console.WriteLine($"seeds solved at ring: {string.Join(", ", Enumerable.Range(1, 63).Where(r => ringHist[r] > 0).Select(r => r + ": " + ringHist[r]))}");
        Console.WriteLine($"fallback classes: {string.Join(", ", classes.Select(kv => kv.Key + " " + kv.Value))}");
        Console.WriteLine($"best case: {foldFree} of {fallbacks} fallbacks become fold-free; {completedClean} of them complete to Success with the independent checks (clean would be {clean + completedClean} of {total})");
        Console.WriteLine($"effort of the clause-learning solver (both trajectories): conflicts {all.Conflicts}, decisions {all.Decisions}, most conflicts on one instance {all.MaxConflicts}");
        Console.WriteLine($"checks: unsatisfiable answers with a valid RUP proof {all.RupOk}, invalid {all.RupFail}; models or repaired regions that fail {all.ModelFail}; against the library's DPLL within {crossBudget} decisions: agree {all.CrossAgree}, disagree {all.CrossDisagree}, DPLL undecided {all.CrossOpen}");
        Console.WriteLine($"obstructed seeds: {obstructedSeeds}; with a singular vertex in the region of the largest ring {obsSingular}, at a singular vertex {obsSeedSingular}; folds in that region: 1 (the seed alone) {obsAlone}, 2 to 5 {obsFew}, 6 or more {obsMany}; sum of det over that region: negative {obsAreaNeg}, zero {obsAreaZero}, positive {obsAreaPos}");
        Console.WriteLine($"obstructed seeds with a negative sum: the first larger ring whose region has a sum of det >= 0 is 1 or 2 rings larger {areaRing[0]}, 3 to 6 rings larger {areaRing[1]}, larger up to ring 20 {areaRing[2]}, none up to ring 20 {areaRing[3]}");
        if (list && obstructionLines.Count > 0) {
            Console.WriteLine();
            Console.WriteLine("obstructed seeds (learning trajectory): verdict per ring from 1 (U unsatisfiable, T refuted while built, ? undecided); the region is that of the largest ring");
            Console.WriteLine("| shape | scale / ratio | seed triangle | det | rings | region triangles | mutable edges | folds in region | sum of det | singular vertices in region | seed at a singular vertex |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (string l in obstructionLines) Console.WriteLine(l);
        }
        return replayMismatch == 0 && checkFailures == 0 ? 0 : 1;
    }
}
