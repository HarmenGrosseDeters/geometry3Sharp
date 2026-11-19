// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace gs
{
    /// <summary>
    /// A small DPLL solver for CNF with "exactly one" variable groups: unit propagation with two watched literals per clause
    /// (Davis, Putnam, Logemann, Loveland 1962; the watching scheme of Moskewicz et al., "Chaff", DAC 2001, written from the
    /// description), chronological backtracking, no learning. The decisions pick a value of the next group that has no true variable, in the
    /// order given for the group, so the first model is the one nearest to the preferred values. The effort is counted in
    /// decisions (a value tried for a group), never in time: a run is repeatable bit for bit. Literals: +(v+1) is variable v true,
    /// -(v+1) false. Internal: the local repair of the pure-quad extraction (QuadFlowRepair).
    /// </summary>
    internal sealed class GroupSatSolver
    {
        public readonly int NVars;
        public readonly List<int[]> Clauses = new List<int[]>();
        /// <summary> the decisions Solve may make before it gives up (Solve returns Budget) </summary>
        public long DecisionBudget = 200000;
        /// <summary> the decisions of the last Solve </summary>
        public long Decisions;

        public const int Satisfiable = 1, Unsatisfiable = 0, Budget = -1;

        sbyte[] val; int[] trail; int trailN, qhead;
        List<int>[] watch;

        public GroupSatSolver(int nvars) { NVars = nvars; }

        public void AddClause(params int[] lits)
        {
            foreach (int l in lits) if (l == 0 || Math.Abs(l) > NVars) throw new ArgumentOutOfRangeException(nameof(lits));
            Clauses.Add(lits);
        }

        static int Idx(int lit) { return lit > 0 ? 2 * (lit - 1) : 2 * (-lit - 1) + 1; }
        sbyte Value(int lit) { sbyte v = val[Math.Abs(lit) - 1]; return lit > 0 ? v : (sbyte)-v; }

        bool Enqueue(int lit)
        {
            sbyte v = Value(lit);
            if (v > 0) return true;
            if (v < 0) return false;
            val[Math.Abs(lit) - 1] = (sbyte)(lit > 0 ? 1 : -1);
            trail[trailN++] = lit;
            return true;
        }

        bool Propagate()
        {
            while (qhead < trailN) {
                int lit = trail[qhead++];
                int fl = -lit;                       // the literal that became false
                var wl = watch[Idx(fl)];
                int i = 0, j = 0;
                for (; i < wl.Count; ++i) {
                    int[] c = Clauses[wl[i]];
                    if (c[0] == fl) { c[0] = c[1]; c[1] = fl; }
                    if (Value(c[0]) > 0) { wl[j++] = wl[i]; continue; }
                    bool found = false;
                    for (int k = 2; k < c.Length; ++k)
                        if (Value(c[k]) >= 0) { int t = c[k]; c[k] = c[1]; c[1] = t; watch[Idx(t)].Add(wl[i]); found = true; break; }
                    if (found) continue;
                    wl[j++] = wl[i];
                    if (Value(c[0]) < 0 || !Enqueue(c[0])) { for (i++; i < wl.Count; ++i) wl[j++] = wl[i]; wl.RemoveRange(j, wl.Count - j); return false; }
                }
                wl.RemoveRange(j, wl.Count - j);
            }
            return true;
        }

        void Undo(int to) { for (int k = trailN - 1; k >= to; --k) val[Math.Abs(trail[k]) - 1] = 0; trailN = to; qhead = to; }

        bool HasTrue(int[] grp) { foreach (int l in grp) if (Value(l) > 0) return true; return false; }

        /// <summary>
        /// groups: the literals of each group in the order of the values to try (every group is decided in the order of the array).
        /// Returns Satisfiable (model: one entry per variable), Unsatisfiable, or Budget (DecisionBudget exceeded). The clauses
        /// are reordered in place (the watched literals first), the solver can be used once.
        /// </summary>
        public int Solve(int[][] groups, out bool[] model)
        {
            model = null;
            val = new sbyte[NVars]; trail = new int[NVars + 1]; trailN = 0; qhead = 0; Decisions = 0;
            watch = new List<int>[2 * NVars];
            for (int i = 0; i < watch.Length; ++i) watch[i] = new List<int>();
            var units = new List<int>();
            for (int ci = 0; ci < Clauses.Count; ++ci) {
                int[] c = Clauses[ci];
                if (c.Length == 0) return Unsatisfiable;
                if (c.Length == 1) { units.Add(c[0]); continue; }
                watch[Idx(c[0])].Add(ci); watch[Idx(c[1])].Add(ci);
            }
            foreach (int u in units) if (!Enqueue(u)) return Unsatisfiable;
            if (!Propagate()) return Unsatisfiable;
            // decision stack: group, next index in the group's order, trail position before the decision
            var sg = new List<int>(); var si = new List<int>(); var st = new List<int>();
            int g = 0;
            while (true) {
                while (g < groups.Length && HasTrue(groups[g])) g++;
                if (g == groups.Length) { model = new bool[NVars]; for (int v = 0; v < NVars; ++v) model[v] = val[v] > 0; return Satisfiable; }
                sg.Add(g); si.Add(0); st.Add(trailN);
                while (true) {
                    int top = sg.Count - 1;
                    int[] grp = groups[sg[top]];
                    Undo(st[top]);
                    bool ok = false;
                    while (si[top] < grp.Length) {
                        int cand = grp[si[top]++];
                        if (Value(cand) < 0) continue;
                        Decisions++;
                        if (Decisions > DecisionBudget) return Budget;
                        if (Enqueue(cand) && Propagate()) { ok = true; break; }
                        Undo(st[top]);
                    }
                    if (ok) { g = sg[top] + 1; break; }
                    sg.RemoveAt(top); si.RemoveAt(top); st.RemoveAt(top);
                    if (sg.Count == 0) return Unsatisfiable;
                }
            }
        }
    }
}
