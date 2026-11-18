// Throwaway spike: a small DPLL solver (two watched literals, chronological backtracking, a decision budget) for the local
// repair of the paper's step 8b. Literals: +(v+1) true, -(v+1) false.
using System;
using System.Collections.Generic;

class Dpll
{
    public int NVars;
    public List<int[]> Clauses = new List<int[]>();
    public long Budget = 200000;
    public long Decisions;
    sbyte[] val; int[] trail; int trailN;
    List<int>[] watch;

    public Dpll(int nvars) { NVars = nvars; }
    public void AddClause(params int[] lits) { Clauses.Add(lits); }

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

    int qhead;
    bool Propagate()
    {
        while (qhead < trailN) {
            int lit = trail[qhead++];
            int fl = -lit;                       // literal that became false
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
                if (Value(c[0]) < 0) { for (i++; i < wl.Count; ++i) wl[j++] = wl[i]; wl.RemoveRange(j, wl.Count - j); return false; }
                if (!Enqueue(c[0])) { for (i++; i < wl.Count; ++i) wl[j++] = wl[i]; wl.RemoveRange(j, wl.Count - j); return false; }
            }
            wl.RemoveRange(j, wl.Count - j);
        }
        return true;
    }

    /// <summary>groups: exactly-one groups (the variables of one edge), decided in order, value preference order as given.
    /// returns 1 SAT, 0 UNSAT, -1 budget exhausted</summary>
    public int Solve(int[][] groups, out bool[] model)
    {
        model = null;
        val = new sbyte[NVars]; trail = new int[NVars + 1]; trailN = 0; qhead = 0; Decisions = 0;
        watch = new List<int>[2 * NVars];
        for (int i = 0; i < watch.Length; ++i) watch[i] = new List<int>();
        var units = new List<int>();
        for (int ci = 0; ci < Clauses.Count; ++ci) {
            int[] c = Clauses[ci];
            if (c.Length == 0) return 0;
            if (c.Length == 1) { units.Add(c[0]); continue; }
            watch[Idx(c[0])].Add(ci); watch[Idx(c[1])].Add(ci);
        }
        foreach (int u in units) if (!Enqueue(u)) return 0;
        if (!Propagate()) return 0;
        // decision stack: group, index into the group order, trail position before the decision
        var sg = new List<int>(); var si = new List<int>(); var st = new List<int>();
        int g = 0;
        while (true) {
            while (g < groups.Length && HasTrue(groups[g])) g++;
            if (g == groups.Length) { model = new bool[NVars]; for (int v = 0; v < NVars; ++v) model[v] = val[v] > 0; return 1; }
            sg.Add(g); si.Add(0); st.Add(trailN);
            while (true) {
                int top = sg.Count - 1;
                int[] grp = groups[sg[top]];
                // undo to the decision point
                for (int k = trailN - 1; k >= st[top]; --k) val[Math.Abs(trail[k]) - 1] = 0;
                trailN = st[top]; qhead = trailN;
                bool ok = false;
                while (si[top] < grp.Length) {
                    int cand = grp[si[top]++];
                    if (Value(cand) < 0) continue;
                    Decisions++;
                    if (Decisions > Budget) return -1;
                    if (Enqueue(cand) && Propagate()) { ok = true; break; }
                    for (int k = trailN - 1; k >= st[top]; --k) val[Math.Abs(trail[k]) - 1] = 0;
                    trailN = st[top]; qhead = trailN;
                }
                if (ok) { g = sg[top] + 1; break; }
                sg.RemoveAt(top); si.RemoveAt(top); st.RemoveAt(top);
                if (sg.Count == 0) return 0;
            }
        }
    }

    bool HasTrue(int[] grp) { foreach (int l in grp) if (Value(l) > 0) return true; return false; }
}

