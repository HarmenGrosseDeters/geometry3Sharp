// Upper bounds (task step 1): for the tets of a MeshTetrahedralization result that miss a target (min dihedral F, max dihedral G),
// what could a perfect local operation reach? The slack of a tet is min(minDih - F, G - maxDih) (>= 0: target met).
//   op1: perfect single-vertex move (nonsmooth max-min of the star's slack; direct random search, so a lower estimate of the optimum),
//        boundary vertices within 0.5 offsets of their current position, no input/self-intersection check (so an upper bound)
//   op2: multi-face removal: all faces between apexes t, b (subsets that form a disc) replaced by the tets around the new edge tb
//   op3: cavity insertion: a new vertex p (candidates in and around the tet, on boundary facets too), Bowyer-Watson cavity, enlarged
//        until star-shaped, p then moved optimally (op1 on the new star)
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

static partial class Spike
{
    internal static double F_ = 20, G_ = 150;

    internal static double Slack(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
    {
        Dihedrals(a, b, c, d, out double mn, out double mx);
        if (mn < 0) return -1000;
        return Math.Min(mn - F_, G_ - mx);
    }

    sealed class Star
    {
        public List<Vector3d[]> T = new List<Vector3d[]>(); public List<int> Slot = new List<int>();
        public void Add(Vector3d[] p, int slot) { T.Add(p); Slot.Add(slot); }
        public double G(Vector3d p)
        {
            double m = double.MaxValue;
            for (int i = 0; i < T.Count; ++i) {
                var q = T[i]; var s = q[Slot[i]]; q[Slot[i]] = p;
                double v = Slack(q[0], q[1], q[2], q[3]);
                q[Slot[i]] = s;
                if (v < m) m = v;
                if (m < -999) break;
            }
            return m;
        }
    }

    static Vector3d RandDir(Random r)
    {
        while (true) {
            var v = new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);
            double l = v.LengthSquared; if (l > 1e-6 && l <= 1) return v / Math.Sqrt(l);
        }
    }

    // maximise star.G over the ball (c0, R) (R = infinity: free); returns the best value, best point
    static double Maximize(Star s, Vector3d start, Vector3d c0, double R, double r0, Random rng, out Vector3d best, int restarts = 10, int iters = 300)
    {
        best = start; double bv = s.G(start);
        for (int rs = 0; rs < restarts; ++rs) {
            Vector3d x = start; double xv = bv;
            if (rs > 0) {
                // restart from a random point near start (feasible only)
                for (int k = 0; k < 50; ++k) {
                    Vector3d y = start + RandDir(rng) * (r0 * 2 * rng.NextDouble());
                    if (R < double.MaxValue && y.Distance(c0) > R) continue;
                    double yv = s.G(y); if (yv > -999) { x = y; xv = yv; break; }
                }
                if (xv < -999) continue;
            }
            double step = r0; int fails = 0;
            for (int it = 0; it < iters && step > r0 * 1e-6; ++it) {
                Vector3d y = x + RandDir(rng) * (step * (0.3 + 0.7 * rng.NextDouble()));
                if (R < double.MaxValue && y.Distance(c0) > R) { if (++fails > 15) { step *= 0.5; fails = 0; } continue; }
                double yv = s.G(y);
                if (yv > xv) { x = y; xv = yv; fails = 0; } else if (++fails > 15) { step *= 0.5; fails = 0; }
            }
            if (xv > bv) { bv = xv; best = x; }
        }
        return bv;
    }

    sealed class TM
    {
        public List<Vector3d> V; public List<Index4i> Tets;
        public List<int>[] VT; public HashSet<int> BV = new HashSet<int>();
        public Dictionary<(int, int, int), List<int>> Faces = new Dictionary<(int, int, int), List<int>>();
        public HashSet<(int, int)> Edges = new HashSet<(int, int)>();
        public double[] Sl;
        public TM(TetMesh3 m)
        {
            V = m.Vertices; Tets = m.Tets;
            VT = new List<int>[V.Count]; for (int i = 0; i < V.Count; ++i) VT[i] = new List<int>();
            Sl = new double[Tets.Count];
            for (int id = 0; id < Tets.Count; ++id) {
                var t = Tets[id];
                for (int i = 0; i < 4; ++i) VT[t[i]].Add(id);
                for (int i = 0; i < 4; ++i) {
                    var f = Key(t[(i + 1) & 3], t[(i + 2) & 3], t[(i + 3) & 3]);
                    if (!Faces.TryGetValue(f, out var l)) Faces[f] = l = new List<int>();
                    l.Add(id);
                    for (int j = i + 1; j < 4; ++j) Edges.Add(t[i] < t[j] ? (t[i], t[j]) : (t[j], t[i]));
                }
                Sl[id] = Slack(V[t.a], V[t.b], V[t.c], V[t.d]);
            }
            foreach (var kv in Faces) if (kv.Value.Count == 1) { BV.Add(kv.Key.Item1); BV.Add(kv.Key.Item2); BV.Add(kv.Key.Item3); }
        }
        public static (int, int, int) Key(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a); if (b > c) (b, c) = (c, b); if (a > b) (a, b) = (b, a); return (a, b, c);
        }
        public bool IsBoundaryFace(int a, int b, int c) => Faces[Key(a, b, c)].Count == 1;
        public bool HasEdge(int a, int b) => Edges.Contains(a < b ? (a, b) : (b, a));
        public Star StarOf(int v, int skipTet = -1)
        {
            var s = new Star();
            foreach (int id in VT[v]) {
                var t = Tets[id]; var p = new Vector3d[4]; int slot = 0;
                for (int i = 0; i < 4; ++i) { p[i] = V[t[i]]; if (t[i] == v) slot = i; }
                s.Add(p, slot);
            }
            return s;
        }
    }

    sealed class BoundRow
    {
        public int Viol, Interior, TouchF, TouchE, TouchV;
        public int Op1Prog, Op1Clear, Op2Prog, Op2Clear, Op2n3Clear, Op3Prog, Op3Clear, Op3bClear, AnyClear, AnyProg, Op23Clear;
        public int Op3IntOnlyClear;
        public Dictionary<string, int> Shape = new Dictionary<string, int>();
        public int NoOp3Cand;
    }

    // ---- op2: multi-face removal around (t, b) containing face f0 ----
    // returns best new min slack over subsets with n >= minN ring vertices (ring size n -> n new tets), -inf if none; also old min slack of removed tets
    static double BestMultiFace(TM m, int t, int b, (int, int, int) f0, int minN, int maxN, out double oldMin, out int bestN)
    {
        oldMin = 0; bestN = 0; double best = double.NegativeInfinity;
        if (m.HasEdge(t, b)) return best;
        // candidate faces: both (face,t) and (face,b) are tets
        var C = new List<(int, int, int)>();
        foreach (int id in m.VT[t]) {
            var tt = m.Tets[id];
            var fs = new List<int>(); for (int i = 0; i < 4; ++i) if (tt[i] != t) fs.Add(tt[i]);
            var k = TM.Key(fs[0], fs[1], fs[2]);
            if (m.Faces[k].Count == 2) { bool hasB = false; foreach (int o in m.Faces[k]) { var to = m.Tets[o]; for (int i = 0; i < 4; ++i) if (to[i] == b) hasB = true; } if (hasB) C.Add(k); }
        }
        if (C.Count == 0 || C.Count > 14) return best;
        int i0 = C.IndexOf(f0); if (i0 < 0) return best;
        int N = C.Count;
        for (int mask = 1; mask < (1 << N); ++mask) {
            if ((mask & (1 << i0)) == 0) continue;
            var S = new List<(int, int, int)>(); for (int i = 0; i < N; ++i) if ((mask & (1 << i)) != 0) S.Add(C[i]);
            if (S.Count + 2 < minN || S.Count + 2 > maxN) continue;   // |S| faces -> ring of |S|+2 vertices, n = |S|+2 new tets
            // edges
            var ec = new Dictionary<(int, int), int>();
            foreach (var f in S) { void E(int x, int y) { var k = x < y ? (x, y) : (y, x); ec[k] = ec.TryGetValue(k, out int c) ? c + 1 : 1; } E(f.Item1, f.Item2); E(f.Item2, f.Item3); E(f.Item1, f.Item3); }
            var bd = ec.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
            var verts = new HashSet<int>(); foreach (var f in S) { verts.Add(f.Item1); verts.Add(f.Item2); verts.Add(f.Item3); }
            if (verts.Count - ec.Count + S.Count != 1) continue;            // disc
            if (ec.Values.Any(c => c > 2)) continue;
            // single cycle
            var adj = new Dictionary<int, List<int>>(); foreach (var e in bd) { if (!adj.ContainsKey(e.Item1)) adj[e.Item1] = new List<int>(); if (!adj.ContainsKey(e.Item2)) adj[e.Item2] = new List<int>(); adj[e.Item1].Add(e.Item2); adj[e.Item2].Add(e.Item1); }
            if (adj.Values.Any(l => l.Count != 2) || bd.Count != S.Count + 2) continue;
            var ring = new List<int>(); int start = bd[0].Item1, prev = -1, cur = start;
            do { ring.Add(cur); int nx = adj[cur][0] == prev ? adj[cur][1] : adj[cur][0]; prev = cur; cur = nx; } while (cur != start && ring.Count <= bd.Count + 1);
            if (ring.Count != bd.Count) continue;
            // new tets (t, b, r_i, r_i+1): all same sign
            int n = ring.Count; int sgn = 0; bool ok = true; var nt = new List<(int, int, int, int)>();
            for (int i = 0; i < n && ok; ++i) {
                int r0 = ring[i], r1 = ring[(i + 1) % n];
                double o = ExactPredicates.Orient3d(m.V[t], m.V[b], m.V[r0], m.V[r1]);
                int s = o > 0 ? 1 : (o < 0 ? -1 : 0);
                if (s == 0 || (sgn != 0 && s != sgn)) ok = false; sgn = s;
            }
            if (!ok) continue;
            double mn = double.MaxValue;
            for (int i = 0; i < n; ++i) {
                int r0 = ring[i], r1 = ring[(i + 1) % n];
                double s = sgn > 0 ? Slack(m.V[t], m.V[b], m.V[r0], m.V[r1]) : Slack(m.V[b], m.V[t], m.V[r0], m.V[r1]);
                if (s < mn) mn = s;
            }
            if (mn > best) {
                best = mn; bestN = n;
                double om = double.MaxValue;
                foreach (var f in S) foreach (int id in m.Faces[f]) om = Math.Min(om, m.Sl[id]);
                oldMin = om;
            }
        }
        return best;
    }

    // ---- op3: cavity insertion of p; returns the new star (tets (a,b,c,p)) or null; oldMin = min slack of the cavity tets ----
    static Star Cavity(TM m, Vector3d p, int startTet, bool pOnBoundaryFace, out double oldMin, out bool touchesBoundary)
    {
        oldMin = double.MaxValue; touchesBoundary = false;
        var cav = new HashSet<int> { startTet };
        var queue = new Queue<int>(); queue.Enqueue(startTet);
        while (queue.Count > 0) {
            int id = queue.Dequeue(); var t = m.Tets[id];
            for (int i = 0; i < 4; ++i) {
                var k = TM.Key(t[(i + 1) & 3], t[(i + 2) & 3], t[(i + 3) & 3]);
                foreach (int o in m.Faces[k]) {
                    if (o == id || cav.Contains(o)) continue;
                    var to = m.Tets[o];
                    double ins = ExactPredicates.InSphere(m.V[to.a], m.V[to.b], m.V[to.c], m.V[to.d], p);
                    if (ins > 0) { cav.Add(o); queue.Enqueue(o); }
                }
            }
            if (cav.Count > 60) return null;
        }
        for (int round = 0; round < 8; ++round) {
            var s = new Star(); bool bad = false; int addTet = -1;
            foreach (int id in cav) {
                var t = m.Tets[id];
                for (int i = 0; i < 4; ++i) {
                    var k = TM.Key(t[(i + 1) & 3], t[(i + 2) & 3], t[(i + 3) & 3]);
                    bool shared = false; int other = -1;
                    foreach (int o in m.Faces[k]) if (o != id) { other = o; if (cav.Contains(o)) shared = true; }
                    if (shared) continue;
                    // new tet (face, p) with the face vertices as in the old tet (i opposite): replace vertex i by p
                    var pts = new Vector3d[4]; for (int j = 0; j < 4; ++j) pts[j] = j == i ? p : m.V[t[j]];
                    double o3 = ExactPredicates.Orient3d(pts[0], pts[1], pts[2], pts[3]);
                    if (o3 <= 0) {
                        // a degenerate new tet is allowed only when p lies on a boundary face of the cavity (insertion on the boundary)
                        if (o3 == 0 && other == -1 && pOnBoundaryFace) continue;
                        bad = true; if (other >= 0) addTet = other; else { return null; } break;
                    }
                    s.Add(pts, i);
                    if (other == -1) touchesBoundary = true;
                }
                if (bad) break;
            }
            if (!bad) { foreach (int id in cav) oldMin = Math.Min(oldMin, m.Sl[id]); return s; }
            cav.Add(addTet); if (cav.Count > 60) return null;
        }
        return null;
    }

    static int Locate(TM m, Vector3d p, IEnumerable<int> cands)
    {
        foreach (int id in cands) {
            var t = m.Tets[id]; bool inside = true;
            for (int i = 0; i < 4 && inside; ++i) {
                var q = new Vector3d[4]; for (int j = 0; j < 4; ++j) q[j] = j == i ? p : m.V[t[j]];
                if (ExactPredicates.Orient3d(q[0], q[1], q[2], q[3]) < 0) inside = false;
            }
            if (inside) return id;
        }
        return -1;
    }

    static Vector3d Circumcenter(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
    {
        Vector3d ba = b - a, ca = c - a, da = d - a;
        Vector3d x = ca.Cross(da) * ba.LengthSquared + da.Cross(ba) * ca.LengthSquared + ba.Cross(ca) * da.LengthSquared;
        double det = 2 * ba.Dot(ca.Cross(da));
        return a + x / det;
    }

    static BoundRow Bounds(TetMesh3 res, double offset, int maxViol, Random rng)
    {
        var m = new TM(res); var row = new BoundRow();
        var viol = Enumerable.Range(0, m.Tets.Count).Where(i => m.Sl[i] < 0).OrderBy(i => m.Sl[i]).ToList();
        row.Viol = viol.Count;
        if (viol.Count > maxViol) { viol = viol.OrderBy(i => (i * 2654435761u) % 1000003u).Take(maxViol).ToList(); }  // deterministic sample
        double scale = Math.Max(1, row.Viol) / (double)Math.Max(1, viol.Count);   // report counts scaled back to all violators
        var cnt = new Dictionary<string, double>();
        void Inc(string k) { cnt[k] = (cnt.TryGetValue(k, out var v) ? v : 0) + 1; }
        foreach (int id in viol) {
            var t = m.Tets[id]; int nb = 0; for (int i = 0; i < 4; ++i) if (m.BV.Contains(t[i])) nb++;
            int nf = 0; for (int i = 0; i < 4; ++i) if (m.IsBoundaryFace(t[(i + 1) & 3], t[(i + 2) & 3], t[(i + 3) & 3])) nf++;
            Inc(nf > 0 ? "touchF" : nb >= 2 ? "touchE" : nb == 1 ? "touchV" : "interior");
            Inc("shape_" + TetMeshImprover.Shape(m.V[t.a], m.V[t.b], m.V[t.c], m.V[t.d]));
            bool clear1 = false, prog1 = false, clear2 = false, prog2 = false, clear3 = false, prog3 = false, clear3b = false, clear3i = false, clear23 = false, clear2n3 = false;
            // --- op1
            for (int i = 0; i < 4; ++i) {
                int v = t[i]; var star = m.StarOf(v);
                double cur = star.G(m.V[v]); double r0 = 0;
                foreach (int tid in m.VT[v]) { var q = m.Tets[tid]; for (int j = 0; j < 4; ++j) if (q[j] != v) r0 += m.V[q[j]].Distance(m.V[v]); r0 /= 3 * m.VT[v].Count; }
                bool bnd = m.BV.Contains(v);
                double R = bnd ? 0.5 * offset : double.MaxValue;
                double bv = Maximize(star, m.V[v], m.V[v], R, bnd ? Math.Min(r0, R) : r0, rng, out _);
                if (bv > cur + 1e-6) prog1 = true;
                if (bv >= 0) clear1 = true;
            }
            // --- op2: faces of the tet shared with a neighbour
            for (int i = 0; i < 4; ++i) {
                var k = TM.Key(t[(i + 1) & 3], t[(i + 2) & 3], t[(i + 3) & 3]);
                if (m.Faces[k].Count != 2) continue;
                int o = m.Faces[k][0] == id ? m.Faces[k][1] : m.Faces[k][0]; var to = m.Tets[o]; int bapex = -1;
                for (int j = 0; j < 4; ++j) if (to[j] != k.Item1 && to[j] != k.Item2 && to[j] != k.Item3) bapex = to[j];
                double best3 = BestMultiFace(m, t[i], bapex, k, 3, 3, out double om3, out _);
                if (best3 >= 0 && best3 > om3 + 1e-6) clear2n3 = true;
                double best = BestMultiFace(m, t[i], bapex, k, 4, 9, out double om, out _);
                if (best > om + 1e-6) prog2 = true;
                if (best >= 0 && best > om + 1e-6) clear2 = true;
            }
            // --- op3: candidate points
            var near = new HashSet<int>(); for (int i = 0; i < 4; ++i) foreach (int x in m.VT[t[i]]) near.Add(x);
            var P = new List<(Vector3d, bool)>();
            Vector3d[] vp = { m.V[t.a], m.V[t.b], m.V[t.c], m.V[t.d] };
            P.Add(((vp[0] + vp[1] + vp[2] + vp[3]) * 0.25, false));
            P.Add((Circumcenter(vp[0], vp[1], vp[2], vp[3]), false));
            for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) P.Add(((vp[i] + vp[j]) * 0.5, false));
            for (int i = 0; i < 4; ++i) {
                var f = new[] { vp[(i + 1) & 3], vp[(i + 2) & 3], vp[(i + 3) & 3] };
                bool bf = m.IsBoundaryFace(t[(i + 1) & 3], t[(i + 2) & 3], t[(i + 3) & 3]);
                P.Add(((f[0] + f[1] + f[2]) / 3, bf));
            }
            var nearL = near.ToList();
            for (int k = 0; k < 40; ++k) {          // random points in the tets of the 1-ring (bary coordinates)
                int tid = nearL[rng.Next(nearL.Count)]; var q = m.Tets[tid];
                double w0 = -Math.Log(rng.NextDouble()), w1 = -Math.Log(rng.NextDouble()), w2 = -Math.Log(rng.NextDouble()), w3 = -Math.Log(rng.NextDouble()); double ws = w0 + w1 + w2 + w3;
                P.Add(((m.V[q.a] * w0 + m.V[q.b] * w1 + m.V[q.c] * w2 + m.V[q.d] * w3) / ws, false));
            }
            for (int k = 0; k < 12; ++k) {          // random points on the boundary faces of the 1-ring tets (boundary insertion)
                int tid = nearL[rng.Next(nearL.Count)]; var q = m.Tets[tid];
                for (int i = 0; i < 4; ++i) if (m.IsBoundaryFace(q[(i + 1) & 3], q[(i + 2) & 3], q[(i + 3) & 3])) {
                    double w0 = -Math.Log(rng.NextDouble()), w1 = -Math.Log(rng.NextDouble()), w2 = -Math.Log(rng.NextDouble()); double ws = w0 + w1 + w2;
                    P.Add(((m.V[q[(i + 1) & 3]] * w0 + m.V[q[(i + 2) & 3]] * w1 + m.V[q[(i + 3) & 3]] * w2) / ws, true)); break;
                }
            }
            double bestC = double.NegativeInfinity, bestCi = double.NegativeInfinity, oldC = 0; bool any = false;
            foreach (var (p, onB) in P) {
                int st = Locate(m, p, onB ? near : near);
                if (onB) {
                    // on a boundary face: the tet owning that face (point is on its face, Locate with >= 0 finds it)
                }
                if (st < 0) continue;
                var star = Cavity(m, p, st, onB, out double om, out bool tb);
                if (star == null || star.T.Count == 0) continue;
                // the cavity must contain T (so T is replaced)
                any = true;
                double r0 = vp[0].Distance(vp[1]);
                double bv = Maximize(star, p, p, double.MaxValue, r0 * 0.5, rng, out _, 3, 120);
                if (bv > bestC) { bestC = bv; oldC = om; }
                if (!tb && bv > bestCi) bestCi = bv;
                if (bv >= 0 && bv > om + 1e-6) { clear3 = true; if (onB) clear3b = true; else clear3i = true; }
                if (bv > om + 1e-6) prog3 = true;
            }
            if (!any) Inc("op3_nocand");
            clear23 = clear2 || clear3;
            if (clear1) Inc("op1_clear"); if (prog1) Inc("op1_prog");
            if (clear2) Inc("op2_clear"); if (prog2) Inc("op2_prog"); if (clear2n3) Inc("op2n3_clear");
            if (clear3) Inc("op3_clear"); if (prog3) Inc("op3_prog"); if (clear3b) Inc("op3b_clear"); if (clear3i) Inc("op3i_clear");
            if (clear1 || clear2 || clear3) Inc("any_clear"); if (prog1 || prog2 || prog3) Inc("any_prog");
        }
        row.Shape = cnt.ToDictionary(kv => kv.Key, kv => (int)Math.Round(kv.Value * scale));
        return row;
    }
}
