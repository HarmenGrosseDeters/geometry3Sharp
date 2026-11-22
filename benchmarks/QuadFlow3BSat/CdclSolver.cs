// A throwaway clause-learning SAT solver for the measurement of QuadFlow3BSat (not library code). Written from the papers, no solver
// code was read:
//   J. P. Marques-Silva, K. A. Sakallah, "GRASP: A Search Algorithm for Propositional Satisfiability", IEEE Trans. Computers 48(5), 1999
//     (conflict analysis, learned clauses, non-chronological backtracking; the first unique implication point as the cut);
//   N. Een, N. Sorensson, "An Extensible SAT-solver", SAT 2003, LNCS 2919 (the architecture: a trail with decision levels, a reason
//     clause per implied literal, two watched literals, an activity per variable that is bumped in the analysis and decays);
//   M. Luby, A. Sinclair, D. Zuckerman, "Optimal speedup of Las Vegas algorithms", Information Processing Letters 47, 1993 (the
//     restart sequence 1 1 2 1 1 2 4 ...).
// Deterministic: restarts are counted in conflicts, activity ties are broken by the variable index, nothing is timed. No clause is ever
// deleted (the instances are small). The decision is made on an "exactly one" group: the unassigned variable of the highest activity
// names the group, and the first value of that group (in its preference order) that is not false is set to true, so the preference
// order of the repair (the current value first, then the nearest) is the phase choice.
using System;
using System.Collections.Generic;

sealed class CdclSolver
{
    public const int Satisfiable = 1, Unsatisfiable = 0, Undecided = -1;

    public readonly int NVars;
    /// <summary> conflicts after which Solve gives up (Undecided); 0: no limit </summary>
    public long ConflictLimit;
    public long Conflicts, Decisions, Propagations, Restarts;
    public int RestartUnit = 64;
    /// <summary> the learned clauses in the order they were derived (DIMACS literals); with the input they are the proof that RupChecker checks </summary>
    public readonly List<int[]> Learned = new List<int[]>();

    // a literal is 2 v (v true) or 2 v + 1 (v false)
    readonly List<int[]> clauses = new List<int[]>();
    readonly List<int>[] watch;
    readonly sbyte[] val;           // per variable: 0 unassigned, 1 true, -1 false
    readonly int[] level, reason, trail;
    readonly List<int> trailLim = new List<int>();
    int trailN, qhead;
    readonly double[] activity; double varInc = 1;
    readonly int[] heap, heapPos; int heapN;
    readonly bool[] seen;
    int[][] groups; int[] groupOf;
    bool emptyClause;
    readonly List<int> units = new List<int>();

    public CdclSolver(int nvars)
    {
        NVars = nvars;
        watch = new List<int>[2 * nvars];
        for (int i = 0; i < watch.Length; ++i) watch[i] = new List<int>();
        val = new sbyte[nvars]; level = new int[nvars]; reason = new int[nvars]; trail = new int[nvars];
        activity = new double[nvars]; heap = new int[nvars]; heapPos = new int[nvars]; seen = new bool[nvars];
    }

    static int ToLit(int dimacs) { return dimacs > 0 ? 2 * (dimacs - 1) : 2 * (-dimacs - 1) + 1; }
    static int ToDimacs(int lit) { return (lit & 1) == 0 ? (lit >> 1) + 1 : -((lit >> 1) + 1); }
    int Value(int lit) { int v = val[lit >> 1]; return (lit & 1) == 0 ? v : -v; }

    /// <summary> a clause in DIMACS literals (+(v+1) / -(v+1)); duplicates are removed, a tautology is dropped </summary>
    public void AddClause(int[] dimacs)
    {
        var c = new List<int>();
        foreach (int d in dimacs) {
            if (d == 0 || Math.Abs(d) > NVars) throw new ArgumentOutOfRangeException(nameof(dimacs));
            int l = ToLit(d);
            if (c.Contains(l ^ 1)) return;
            if (!c.Contains(l)) c.Add(l);
        }
        if (c.Count == 0) { emptyClause = true; return; }
        if (c.Count == 1) { units.Add(c[0]); return; }
        Attach(c.ToArray());
    }

    int Attach(int[] c) { int ci = clauses.Count; clauses.Add(c); watch[c[0]].Add(ci); watch[c[1]].Add(ci); return ci; }

    // ------------------------------------------------------------------------------------------ the order heap (activity, then index)

    bool Before(int a, int b) { return activity[a] > activity[b] || (activity[a] == activity[b] && a < b); }
    void Up(int i) { int v = heap[i]; while (i > 0) { int p = (i - 1) >> 1; if (!Before(v, heap[p])) break; heap[i] = heap[p]; heapPos[heap[i]] = i; i = p; } heap[i] = v; heapPos[v] = i; }
    void Down(int i)
    {
        int v = heap[i];
        while (true) {
            int c = 2 * i + 1; if (c >= heapN) break;
            if (c + 1 < heapN && Before(heap[c + 1], heap[c])) c++;
            if (!Before(heap[c], v)) break;
            heap[i] = heap[c]; heapPos[heap[i]] = i; i = c;
        }
        heap[i] = v; heapPos[v] = i;
    }
    void HeapInsert(int v) { if (heapPos[v] >= 0) return; heap[heapN] = v; heapPos[v] = heapN; Up(heapN++); }
    int HeapPop() { int v = heap[0]; heapPos[v] = -1; heapN--; if (heapN > 0) { heap[0] = heap[heapN]; heapPos[heap[0]] = 0; Down(0); } return v; }

    void Bump(int v)
    {
        if ((activity[v] += varInc) > 1e100) {
            for (int i = 0; i < NVars; ++i) activity[i] *= 1e-100;     // the same factor for all: the order is kept
            varInc *= 1e-100;
        }
        if (heapPos[v] >= 0) Up(heapPos[v]);
    }

    // ------------------------------------------------------------------------------------------ trail

    void Enqueue(int lit, int why)
    {
        int v = lit >> 1;
        val[v] = (sbyte)((lit & 1) == 0 ? 1 : -1); level[v] = trailLim.Count; reason[v] = why; trail[trailN++] = lit;
    }

    void CancelUntil(int lvl)
    {
        if (trailLim.Count <= lvl) return;
        int to = trailLim[lvl];
        for (int k = trailN - 1; k >= to; --k) { int v = trail[k] >> 1; val[v] = 0; HeapInsert(v); }
        trailN = to; qhead = to; trailLim.RemoveRange(lvl, trailLim.Count - lvl);
    }

    /// <summary> unit propagation; the index of a clause with all literals false, or -1 </summary>
    int Propagate()
    {
        while (qhead < trailN) {
            int fl = trail[qhead++] ^ 1;            // the literal that became false
            Propagations++;
            var wl = watch[fl];
            int i = 0, j = 0;
            for (; i < wl.Count; ++i) {
                int ci = wl[i]; int[] c = clauses[ci];
                if (c[0] == fl) { c[0] = c[1]; c[1] = fl; }
                if (Value(c[0]) > 0) { wl[j++] = ci; continue; }
                bool moved = false;
                for (int k = 2; k < c.Length; ++k)
                    if (Value(c[k]) >= 0) { int t = c[k]; c[k] = fl; c[1] = t; watch[t].Add(ci); moved = true; break; }
                if (moved) continue;
                wl[j++] = ci;
                if (Value(c[0]) < 0) { for (i++; i < wl.Count; ++i) wl[j++] = wl[i]; wl.RemoveRange(j, wl.Count - j); return ci; }
                Enqueue(c[0], ci);
            }
            wl.RemoveRange(j, wl.Count - j);
        }
        return -1;
    }

    /// <summary> the first unique implication point: the learned clause (the asserting literal first, a literal of the backjump level second) and that level </summary>
    int[] Analyze(int confl, out int backLevel)
    {
        var learnt = new List<int> { 0 };
        int open = 0, p = -1, idx = trailN - 1, cur = trailLim.Count;
        while (true) {
            int[] c = clauses[confl];
            foreach (int q in c) {
                if (q == p) continue;
                int v = q >> 1;
                if (seen[v] || level[v] == 0) continue;
                seen[v] = true; Bump(v);
                if (level[v] == cur) open++; else learnt.Add(q);
            }
            while (!seen[trail[idx] >> 1]) idx--;
            p = trail[idx--]; seen[p >> 1] = false; open--;
            if (open == 0) break;
            confl = reason[p >> 1];
        }
        learnt[0] = p ^ 1;
        backLevel = 0;
        int at = 1;
        for (int k = 1; k < learnt.Count; ++k) {
            int v = learnt[k] >> 1; seen[v] = false;
            if (level[v] > backLevel) { backLevel = level[v]; at = k; }
        }
        if (learnt.Count > 1) { int t = learnt[1]; learnt[1] = learnt[at]; learnt[at] = t; }
        return learnt.ToArray();
    }

    static long Luby(long i)
    {
        // the i-th term (from 1) of 1 1 2 1 1 2 4 1 1 2 1 1 2 4 8 ...
        while (true) {
            long k = 1; while ((1L << (int)k) - 1 < i) k++;
            if ((1L << (int)k) - 1 == i) return 1L << (int)(k - 1);
            i -= (1L << (int)(k - 1)) - 1;
        }
    }

    /// <summary>
    /// groups: the "exactly one" groups (DIMACS literals, all positive, in the order of the values to try); every variable must be in one
    /// group and the clauses must hold the exactly-one constraints. Returns Satisfiable (model per variable), Unsatisfiable or Undecided.
    /// A solver instance is used once (the trail and the learned clauses of a call stay).
    /// </summary>
    public int Solve(int[][] groupLits, out bool[] model)
    {
        model = null;
        if (emptyClause) return Unsatisfiable;
        groups = new int[groupLits.Length][]; groupOf = new int[NVars];
        for (int v = 0; v < NVars; ++v) groupOf[v] = -1;
        for (int g = 0; g < groupLits.Length; ++g) {
            groups[g] = new int[groupLits[g].Length];
            for (int k = 0; k < groupLits[g].Length; ++k) {
                if (groupLits[g][k] <= 0) throw new ArgumentException("group literals must be positive");
                int v = groupLits[g][k] - 1; groups[g][k] = v;
                if (groupOf[v] >= 0) throw new ArgumentException("a variable is in two groups");
                groupOf[v] = g;
            }
        }
        for (int v = 0; v < NVars; ++v) if (groupOf[v] < 0) throw new ArgumentException("a variable is in no group");
        heapN = 0; for (int v = 0; v < NVars; ++v) heapPos[v] = -1;
        for (int v = 0; v < NVars; ++v) HeapInsert(v);
        foreach (int u in units) { if (Value(u) < 0) return Unsatisfiable; if (Value(u) == 0) Enqueue(u, -1); }
        if (Propagate() >= 0) return Unsatisfiable;
        long restartAt = RestartUnit * Luby(1), run = 1, sinceRestart = 0;
        while (true) {
            int confl = Propagate();
            if (confl >= 0) {
                Conflicts++; sinceRestart++;
                if (trailLim.Count == 0) return Unsatisfiable;
                int[] learnt = Analyze(confl, out int back);
                var d = new int[learnt.Length]; for (int k = 0; k < d.Length; ++k) d[k] = ToDimacs(learnt[k]);
                Learned.Add(d);
                CancelUntil(back);
                if (learnt.Length == 1) Enqueue(learnt[0], -1);
                else Enqueue(learnt[0], Attach(learnt));
                varInc /= 0.95;
                if (ConflictLimit > 0 && Conflicts >= ConflictLimit) return Undecided;
                continue;
            }
            if (sinceRestart >= restartAt) { Restarts++; sinceRestart = 0; restartAt = RestartUnit * Luby(++run); CancelUntil(0); continue; }
            int pick = -1;
            while (heapN > 0) { int v = HeapPop(); if (val[v] == 0) { pick = v; break; } }
            if (pick < 0) {
                model = new bool[NVars]; for (int v = 0; v < NVars; ++v) model[v] = val[v] > 0;
                return Satisfiable;
            }
            HeapInsert(pick);       // only the chosen value is assigned below; `pick` may stay unassigned
            int choice = -1;
            foreach (int v in groups[groupOf[pick]]) if (val[v] == 0) { choice = v; break; }
            Decisions++;
            trailLim.Add(trailN);
            Enqueue(2 * choice, -1);
        }
    }
}

/// <summary>
/// An independent check of an Unsatisfiable answer by reverse unit propagation (E. Goldberg, Y. Novikov, "Verification of proofs of
/// unsatisfiability for CNF formulas", DATE 2003; the forward check): every learned clause, in order, must be implied by unit propagation
/// from the input and the earlier learned clauses (assume all its literals false, propagate, reach a conflict), and at the end the
/// propagation alone must reach a conflict. It shares no code with the solver: counters per clause over occurrence lists.
/// </summary>
sealed class RupChecker
{
    readonly int nvars;
    readonly List<int[]> cls = new List<int[]>();
    readonly List<int> falseCount = new List<int>();
    readonly List<int>[] occ;
    readonly sbyte[] val; readonly List<int> trail = new List<int>(); int qhead;
    bool refuted;

    public RupChecker(int nvars)
    {
        this.nvars = nvars; val = new sbyte[nvars]; occ = new List<int>[2 * nvars];
        for (int i = 0; i < occ.Length; ++i) occ[i] = new List<int>();
    }

    static int Idx(int d) { return d > 0 ? 2 * (d - 1) : 2 * (-d - 1) + 1; }
    int Value(int d) { int v = val[Math.Abs(d) - 1]; return d > 0 ? v : -v; }

    // sets d true and counts it in the clauses that hold -d; false on a conflict (a literal already false)
    bool Assign(int d)
    {
        if (Value(d) > 0) return true;
        if (Value(d) < 0) return false;
        val[Math.Abs(d) - 1] = (sbyte)(d > 0 ? 1 : -1); trail.Add(d);
        foreach (int ci in occ[Idx(-d)]) falseCount[ci]++;
        return true;
    }

    bool Propagate()
    {
        while (qhead < trail.Count) {
            int d = trail[qhead++];
            foreach (int ci in occ[Idx(-d)]) {
                int[] c = cls[ci];
                if (falseCount[ci] < c.Length - 1) continue;
                int free = 0; bool sat = false;
                foreach (int l in c) { int v = Value(l); if (v > 0) { sat = true; break; } if (v == 0) free = l; }
                if (sat) continue;
                if (free == 0) return false;
                if (!Assign(free)) return false;
            }
        }
        return true;
    }

    void Undo(int to)
    {
        for (int k = trail.Count - 1; k >= to; --k) {
            int d = trail[k]; val[Math.Abs(d) - 1] = 0;
            foreach (int ci in occ[Idx(-d)]) falseCount[ci]--;
        }
        trail.RemoveRange(to, trail.Count - to); qhead = to;
    }

    /// <summary> adds a clause to the formula (an input clause, or a learned clause after Implied) and propagates at the top level </summary>
    public void Add(int[] dimacs)
    {
        if (refuted) return;
        var c = new List<int>();
        foreach (int d in dimacs) { if (c.Contains(-d)) return; if (!c.Contains(d)) c.Add(d); }
        int ci = cls.Count; cls.Add(c.ToArray());
        int fc = 0; int free = 0; bool sat = false;
        foreach (int l in c) { occ[Idx(l)].Add(ci); int v = Value(l); if (v < 0) fc++; else if (v > 0) sat = true; else free = l; }
        falseCount.Add(fc);
        if (sat) return;
        if (fc == c.Count) { refuted = true; return; }
        if (fc == c.Count - 1) { if (!Assign(free) || !Propagate()) refuted = true; }
    }

    /// <summary> true if the clause follows by unit propagation from the clauses added so far </summary>
    public bool Implied(int[] dimacs)
    {
        if (refuted) return true;
        int mark = trail.Count; bool conflict = false;
        foreach (int d in dimacs) {
            if (Value(d) > 0) { conflict = true; break; }      // a literal is true at the top level: the clause holds
            if (!Assign(-d)) { conflict = true; break; }
        }
        if (!conflict) conflict = !Propagate();
        Undo(mark);
        return conflict;
    }

    /// <summary> true once unit propagation alone has refuted the formula </summary>
    public bool Refuted { get { return refuted; } }

    /// <summary> the whole check: the input, then every learned clause in order; true if the proof is valid and ends in a refutation </summary>
    public static bool Check(int nvars, List<int[]> input, List<int[]> learned)
    {
        var ck = new RupChecker(nvars);
        foreach (var c in input) ck.Add(c);
        foreach (var c in learned) { if (ck.Refuted) return true; if (!ck.Implied(c)) return false; ck.Add(c); }
        return ck.Refuted;
    }
}
