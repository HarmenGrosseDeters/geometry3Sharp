// Throwaway spike (QUAD_REMESHING.md 4.3, second measurement): the QuadriFlow position-field repair as the coordinator's summary of
// Huang et al. 2018 states it (sections 3.2-3.6). No QuadriFlow / Instant Meshes code read. Not a library change.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

struct V2
{
    public int X, Y;
    public V2(int x, int y) { X = x; Y = y; }
    public static V2 operator +(V2 a, V2 b) { return new V2(a.X + b.X, a.Y + b.Y); }
    public static V2 operator -(V2 a, V2 b) { return new V2(a.X - b.X, a.Y - b.Y); }
    public static V2 operator -(V2 a) { return new V2(-a.X, -a.Y); }
    public static bool operator ==(V2 a, V2 b) { return a.X == b.X && a.Y == b.Y; }
    public static bool operator !=(V2 a, V2 b) { return !(a == b); }
    public override bool Equals(object o) { return o is V2 v && v == this; }
    public override int GetHashCode() { return X * 31 + Y; }
    public static V2 Rot(V2 v, int k) { k = ((k % 4) + 4) % 4; for (int i = 0; i < k; ++i) v = new V2(-v.Y, v.X); return v; }
    public static int Det(V2 a, V2 b) { return a.X * b.Y - a.Y * b.X; }
    public static readonly V2 Zero = new V2(0, 0);
    public int Inf { get { return Math.Max(Math.Abs(X), Math.Abs(Y)); } }
    public int L1 { get { return Math.Abs(X) + Math.Abs(Y); } }
    public bool IsZero { get { return X == 0 && Y == 0; } }
    public override string ToString() { return "(" + X + "," + Y + ")"; }
}

class Tri
{
    public int Orig;
    public (double, double)[] Dr = new (double, double)[3];
    public int[] V = new int[3];
    public V2[] D = new V2[3];          // slot s: vertex s -> s+1, in the frame of this triangle
    public int[] Nb = { -1, -1, -1 };   // neighbour across slot s
    public int[] NbS = new int[3];      // its slot (the reversed edge)
    public int[] Rot = new int[3];      // D_neighbour = - R^Rot D_self
    public int Fa;                      // frame vertex (an original mesh vertex)
}

class FlowResult
{
    public string Name;
    public int Vertices, Triangles;
    public int PosSingBefore, PosSingAfter;
    public int Balanced, Fixed, FixedBoundary, FlowUnits, FlowCost, Hcap, Augments, Clamped, Components;
    public double TFlow;
    public int Splits;
    public int Quads, FlippedQuads, InvAfterFlow, InvAfterGreedy, InvAfterSat, GreedyMoves, Cancelled, SatCurViol, SatInvInRegion, SatUnsat, SatBudget, SatSolved, SatFailed, SatAttempts;
    public double TGreedy, TSat, TPos, TExtract, TSetup, TMcf, TNet, TSub, TFlowPrep; public int HIters, NArcs, NNodes; public int Phases; public long Settled, SatDecisions, SatClauses;
    public int LongEdgesAfter, InvBefore, DetPosBefore, InvAfterFlowPreSplit, DetPosAfterFlow;
    public PolyMeshMetrics Metrics; public DPolyMesh Out;
    public int Unpaired, BadQuads, InvertedTris;
    public string Note = "";
}

partial class QuadFlowSpike
{
    DMesh3 mesh; CrossField cf; PositionField pf; double s;
    int maxV;
    Vector3d[] P, Nrm, Q, T;
    // per edge (u<v) data
    Dictionary<long, int> gMap = new Dictionary<long, int>();
    Dictionary<long, V2> dMap = new Dictionary<long, V2>();
    Dictionary<long, (double, double)> rMap = new Dictionary<long, (double, double)>();
    public int CostMode = 0;      // 0 flat cost 1 per unit, 1 geometry aware (marginal misfit of the real offset, two tiers)
    public int HStart = 2; public int PosMode = 1; public double PosAnchor = 0.02;
    public bool ExcessMcf = false; public bool FastMcf = false, FastSubdiv = false, FastPos = false, FastSat = false;
    public Dictionary<int, double> EdgePen = new Dictionary<int, double>();     // edge index -> extra cost multiplier
    public HashSet<int> BadEdges = new HashSet<int>();                         // output: edges of original triangles that end up inverted
    int[] slotEdge;
    static (double, double) RotD((double X, double Y) v, int k) { k = ((k % 4) + 4) % 4; for (int i = 0; i < k; ++i) v = (-v.Y, v.X); return v; }
    (double, double) DirR(int u, int w)
    {
        if (u < w) return rMap[Key(u, w)];
        var d = rMap[Key(w, u)];
        var r = RotD(d, -(gMap[Key(w, u)]));
        return (-r.Item1, -r.Item2);
    }
    public bool SatStrict = false;
    public bool SmartSplit = false;
    public bool Cancel = false, Project = false;
    public int SatMaxRing = 6;
    public long SatBudget = 100000;

    long Key(int u, int v) { return (long)u * maxV + v; }
    int G(int u, int w) { if (u == w) return 0; return u < w ? gMap[Key(u, w)] : (4 - gMap[Key(w, u)]) % 4; }       // coords_u = R^G coords_w
    V2 Dir(int u, int w)
    {   // offset of w's origin from u's, in u's frame
        if (u < w) return dMap[Key(u, w)];
        V2 d = dMap[Key(w, u)];                        // in w's frame (w<u): offset u from w
        return -V2.Rot(d, -(gMap[Key(w, u)]));          // coords_u = R^{-g_wu} coords_w ... see below
    }

    public QuadFlowSpike(DMesh3 m, CrossField c, PositionField p, double scale) { mesh = m; cf = c; pf = p; s = scale; }

    static Vector3d MiddlePoint(Vector3d p0, Vector3d n0, Vector3d p1, Vector3d n1, double eps)
    {
        Vector3d m = 0.5 * (p0 + p1);
        double c = n0.Dot(n1), r0 = n0.Dot(p0 - m), r1 = n1.Dot(p1 - m);
        double d0 = 1 + eps, det = d0 * d0 - c * c;
        double alpha = (d0 * r0 - c * r1) / det, beta = (d0 * r1 - c * r0) / det;
        return m + alpha * n0 + beta * n1;
    }

    // ------------------------------------------------------------------------------------------ step 1

    void BuildOffsets()
    {
        maxV = mesh.MaxVertexID;
        P = new Vector3d[maxV]; Nrm = new Vector3d[maxV]; Q = new Vector3d[maxV]; T = new Vector3d[maxV];
        foreach (int v in mesh.VertexIndices()) {
            P[v] = mesh.GetVertex(v); Nrm[v] = cf.VertexNormal(v); Q[v] = pf.Orientation(v); T[v] = Nrm[v].Cross(Q[v]);
        }
        foreach (int e in mesh.EdgeIndices()) {
            Index2i ev = mesh.GetEdgeV(e);
            int u = Math.Min(ev.a, ev.b), v = Math.Max(ev.a, ev.b);
            Vector3d x = MiddlePoint(P[u], Nrm[u], P[v], Nrm[v], 0.05);
            // compatible representative of v's cross: b = R^m q_v
            Vector3d tv = T[v];
            double dq = Q[u].Dot(Q[v]), dt = Q[u].Dot(tv);
            Vector3d b; int m;
            if (Math.Abs(dq) >= Math.Abs(dt)) { if (dq >= 0) { b = Q[v]; m = 0; } else { b = -Q[v]; m = 2; } }
            else { if (dt >= 0) { b = tv; m = 1; } else { b = -tv; m = 3; } }
            Vector3d tb = Nrm[v].Cross(b);
            Vector3d qi = Q[u], ti = T[u], oi = pf.Origin(u), oj = pf.Origin(v);
            Vector3d d = x - oi;
            double cq0 = Math.Round(qi.Dot(d) / s), ct0 = Math.Round(ti.Dot(d) / s);
            int kq0 = (int)cq0, kt0 = (int)ct0;
            Vector3d a = oi + s * (kq0 * qi + kt0 * ti);
            Vector3d d2 = a - oj;
            int kq1 = (int)Math.Round(b.Dot(d2) / s), kt1 = (int)Math.Round(tb.Dot(d2) / s);
            gMap[Key(u, v)] = (4 - m) % 4;
            dMap[Key(u, v)] = new V2(kq0 - kq1, kt0 - kt1);
            {   // real offset: coordinates of x relative to o_u minus coordinates of x relative to o_v (in the compatible basis)
                Vector3d dv = x - oj;
                rMap[Key(u, v)] = (qi.Dot(d) / s - b.Dot(dv) / s, ti.Dot(d) / s - tb.Dot(dv) / s);
            }
        }
    }

    // ------------------------------------------------------------------------------------------ data

    List<Tri> tris = new List<Tri>();
    List<Vector3d> VP = new List<Vector3d>(), VN = new List<Vector3d>();
    List<int> VFrame = new List<int>();

    // balancing machinery
    class Edge { public int F0, S0, F1 = -1, S1, M; public V2 Dv; public (double, double) Rr; }
    List<Edge> edges = new List<Edge>();

    // coefficient (row, sign) of component j of a variable with exponent x
    static void RowOf(int x, int j, out int row, out int sign)
    {
        x = ((x % 4) + 4) % 4;
        // row0: x=0: dx+, 1: dy-, 2: dx-, 3: dy+ ; row1: x=0: dy+, 1: dx+, 2: dy-, 3: dx-
        switch (x) {
            case 0: row = j == 0 ? 0 : 1; sign = 1; break;
            case 1: if (j == 0) { row = 1; sign = 1; } else { row = 0; sign = -1; } break;
            case 2: row = j == 0 ? 0 : 1; sign = -1; break;
            default: if (j == 0) { row = 1; sign = -1; } else { row = 0; sign = 1; } break;
        }
    }

    void BuildTriangles()
    {
        foreach (int t in mesh.TriangleIndices()) {
            Index3i tv = mesh.GetTriangle(t);
            int a = tv.a, b = tv.b, c = tv.c;
            var tr = new Tri { Fa = a, Orig = tris.Count };
            tr.V[0] = a; tr.V[1] = b; tr.V[2] = c;
            tr.D[0] = Dir(a, b);
            tr.D[1] = V2.Rot(Dir(b, c), G(a, b));
            tr.D[2] = V2.Rot(Dir(c, a), G(a, c));
            tr.Dr[0] = DirR(a, b); tr.Dr[1] = RotD(DirR(b, c), G(a, b)); tr.Dr[2] = RotD(DirR(c, a), G(a, c));
            tris.Add(tr);
        }
        foreach (int v in mesh.VertexIndices()) { }
        var dir = new Dictionary<long, (int, int)>();
        for (int f = 0; f < tris.Count; ++f)
            for (int sl = 0; sl < 3; ++sl) dir[(long)tris[f].V[sl] * maxV + tris[f].V[(sl + 1) % 3]] = (f, sl);
        for (int f = 0; f < tris.Count; ++f)
            for (int sl = 0; sl < 3; ++sl) {
                int p = tris[f].V[sl], q = tris[f].V[(sl + 1) % 3];
                if (!dir.TryGetValue((long)q * maxV + p, out var nb)) continue;
                tris[f].Nb[sl] = nb.Item1; tris[f].NbS[sl] = nb.Item2;
                int fa = tris[f].V[0], fb = tris[nb.Item1].V[0];
                tris[f].Rot[sl] = (((G(fb, q) - G(p, q) - G(fa, p)) % 4) + 4) % 4;
            }
    }

    // ------------------------------------------------------------------------------------------ steps 2-5: the flow

    public FlowResult Run(string name, bool doGreedy, bool doSat)
    {
        var R = new FlowResult { Name = name };
        var sw = Stopwatch.StartNew();
        BuildOffsets();
        BuildTriangles();
        R.TSetup = sw.Elapsed.TotalSeconds;
        R.Vertices = mesh.VertexCount; R.Triangles = tris.Count;
        foreach (var t in tris) if (!(t.D[0] + t.D[1] + t.D[2]).IsZero) R.PosSingBefore++;
        R.InvBefore = Inverted(); R.DetPosBefore = tris.Count(t => Det(t) > 0);
        // check the transition relation on the initial data
        for (int f = 0; f < tris.Count; ++f)
            for (int sl = 0; sl < 3; ++sl) {
                int nb = tris[f].Nb[sl]; if (nb < 0) continue;
                V2 expect = -V2.Rot(tris[f].D[sl], tris[f].Rot[sl]);
                if (expect != tris[nb].D[tris[f].NbS[sl]]) { R.Note += " TRANSITION MISMATCH"; goto done; }
            }
        done:
        sw.Restart();
        SolveFlow(R);
        R.TFlow = sw.Elapsed.TotalSeconds;
        foreach (var t in tris) if (!(t.D[0] + t.D[1] + t.D[2]).IsZero) R.PosSingAfter++;
        R.InvAfterFlowPreSplit = Inverted(); R.DetPosAfterFlow = tris.Count(t => Det(t) > 0);
        // step 7: subdivision
        sw.Restart(); R.Splits = Subdivide(); R.TSub = sw.Elapsed.TotalSeconds;
        R.LongEdgesAfter = tris.Count(t => t.D.Any(d => d.Inf > 1));
        R.InvAfterFlow = Inverted();
        if (doGreedy) { sw.Restart(); Greedy(R); R.TGreedy = sw.Elapsed.TotalSeconds; }
        R.InvAfterGreedy = Inverted();
        if (doSat) { sw.Restart(); RepairSat(R); R.TSat = sw.Elapsed.TotalSeconds; }
        R.InvAfterSat = Inverted();
        foreach (var t in tris) if (Det(t) < 0) for (int sl = 0; sl < 3; ++sl) BadEdges.Add(slotEdge[t.Orig * 3 + sl]);
        if (Environment.GetEnvironmentVariable("SPIKE_DBG") == "1") {
            BuildCorners(); var ft = new List<int>(); var fi = new List<int>(); var cum = new List<int>();
            int shown = 0;
            // distance (triangle rings) from every triangle to the nearest triangle with an S (holonomy) vertex
            var dist = new int[tris.Count]; for (int i = 0; i < dist.Length; ++i) dist[i] = -1;
            var qq = new Queue<int>();
            var isS = new bool[VP.Count]; for (int v = 0; v < VP.Count; ++v) isS[v] = !Fan(v, ft, fi, cum);
            int nS = isS.Count(b => b);
            for (int f = 0; f < tris.Count; ++f) if (tris[f].V.Any(v => isS[v])) { dist[f] = 0; qq.Enqueue(f); }
            while (qq.Count > 0) { int f = qq.Dequeue(); for (int sl = 0; sl < 3; ++sl) { int nb = tris[f].Nb[sl]; if (nb >= 0 && dist[nb] < 0) { dist[nb] = dist[f] + 1; qq.Enqueue(nb); } } }
            var hist = new SortedDictionary<int, int>();
            for (int f = 0; f < tris.Count; ++f) if (Det(tris[f]) < 0) { hist.TryGetValue(dist[f], out int c); hist[dist[f]] = c + 1; }
            Console.WriteLine("  [dbg] vertices with fan holonomy/open: " + nS + "; remaining inverted by ring distance to them: " + string.Join(" ", hist.Select(kv => kv.Key + ":" + kv.Value)));
            // connected components of inverted triangles
            var comp = new int[tris.Count]; int nc = 0; var sizes = new List<int>();
            for (int f = 0; f < tris.Count; ++f) if (Det(tris[f]) < 0 && comp[f] == 0) {
                nc++; int sz = 0; var st = new Stack<int>(); st.Push(f); comp[f] = nc;
                while (st.Count > 0) { int g = st.Pop(); sz++; for (int sl = 0; sl < 3; ++sl) { int nb = tris[g].Nb[sl]; if (nb >= 0 && Det(tris[nb]) < 0 && comp[nb] == 0) { comp[nb] = nc; st.Push(nb); } } }
                sizes.Add(sz);
            }
            Console.WriteLine("  [dbg] inverted components (edge-connected): " + nc + " sizes " + string.Join(" ", sizes.OrderByDescending(x => x)));
            for (int f = 0; f < tris.Count && shown < 12; ++f) if (Det(tris[f]) < 0) {
                string hol = string.Join("", tris[f].V.Select(v => Fan(v, ft, fi, cum) ? "r" : "S"));
                var nbDet = string.Join(",", Enumerable.Range(0, 3).Select(sl => tris[f].Nb[sl] >= 0 ? Det(tris[tris[f].Nb[sl]]).ToString() : "x"));
                Console.WriteLine($"  inv tri {f} D {tris[f].D[0]} {tris[f].D[1]} {tris[f].D[2]} det {Det(tris[f])} fans {hol} nbdets {nbDet} verts {string.Join(",", tris[f].V)} (orig<{maxV}: {tris[f].V.Count(v => v < maxV)})");
                shown++;
            }
        }
        if (Environment.GetEnvironmentVariable("SPIKE_DBG2") == "1") Diag2();
        sw.Restart();
        Vector3d[] pos = SolvePositions();
        R.TPos = sw.Elapsed.TotalSeconds;
        sw.Restart();
        Extract(R, pos);
        R.TExtract = sw.Elapsed.TotalSeconds;
        return R;
    }

    // integer cost of moving the value from a to b (a unit step) for the real value r: 100 * (|b-r| - |a-r| + 1), >= 0
    static int MarginalCost(int a, int b, double r) { return (int)Math.Round(100 * (Math.Abs(b - r) - Math.Abs(a - r) + 1)); }
    // cost mode 2: as mode 1, times SignPen when the step moves the value to the side opposite the real offset (an axis reversal)
    public double SignPen = 10, SignTol = 0.25;
    bool Against(int a, int b, double r) { return Math.Abs(b) > Math.Abs(a) && b * r < 0 && Math.Abs(r) > SignTol; }
    // cost mode 3: hard form (the step arcs against the sign are not created; the [C1] analogue of Protais et al. 2022)
    int StepCost(int a, int b, double r)
    {
        int c = MarginalCost(a, b, r);
        if (CostMode == 2 && Math.Abs(b) > Math.Abs(a) && b * r < 0 && Math.Abs(r) > SignTol) c = (int)Math.Round(c * SignPen);
        return c;
    }

    int Pen(int k, int c) { return EdgePen.TryGetValue(k, out double m) ? (int)Math.Round(c * (1 + m)) : c; }

    void SolveFlow(FlowResult R)
    {
        var swFlow = Stopwatch.StartNew();
        int F = tris.Count;
        // edges
        slotEdge = new int[tris.Count * 3];
        for (int f = 0; f < F; ++f)
            for (int sl = 0; sl < 3; ++sl) {
                int nb = tris[f].Nb[sl];
                if (nb >= 0 && nb < f) continue;
                var e = new Edge { F0 = f, S0 = sl, Dv = tris[f].D[sl], Rr = tris[f].Dr[sl] };
                if (nb >= 0) { e.F1 = nb; e.S1 = tris[f].NbS[sl]; e.M = tris[f].Rot[sl]; }
                slotEdge[f * 3 + sl] = edges.Count; if (nb >= 0) slotEdge[nb * 3 + tris[f].NbS[sl]] = edges.Count;
                edges.Add(e);
            }
        // exponent of edge e in triangle f before the equation rotation: 0 in F0, M+2 in F1
        // BFS over triangles: rho
        var rho = new int[F]; var vis = new bool[F];
        var adjE = new List<int>[F]; for (int f = 0; f < F; ++f) adjE[f] = new List<int>();
        for (int k = 0; k < edges.Count; ++k) { adjE[edges[k].F0].Add(k); if (edges[k].F1 >= 0) adjE[edges[k].F1].Add(k); }
        var q = new Queue<int>(); vis[0] = true; q.Enqueue(0);
        while (q.Count > 0) {
            int f = q.Dequeue();
            foreach (int k in adjE[f]) {
                var e = edges[k]; if (e.F1 < 0) continue;
                int c = e.F0 == f ? e.F1 : e.F0;
                if (vis[c]) continue;
                vis[c] = true;
                rho[c] = e.F0 == f ? ((rho[f] - e.M) % 4 + 4) % 4 : ((rho[f] + e.M) % 4 + 4) % 4;
                q.Enqueue(c);
            }
        }
        // exponent x of slot in the rotated equation
        int[][] ex = new int[F][]; for (int f = 0; f < F; ++f) ex[f] = new int[3];
        foreach (var e in edges) {
            ex[e.F0][e.S0] = (0 + rho[e.F0]) % 4;
            if (e.F1 >= 0) ex[e.F1][e.S1] = (e.M + 2 + rho[e.F1]) % 4;
        }
        var balanced = new bool[edges.Count];
        for (int k = 0; k < edges.Count; ++k) {
            var e = edges[k];
            balanced[k] = e.F1 >= 0 && (((ex[e.F0][e.S0] - ex[e.F1][e.S1] - 2) % 4) + 4) % 4 == 0;
            if (balanced[k]) R.Balanced++; else { R.Fixed++; if (e.F1 < 0) R.FixedBoundary++; }
        }
        var initDv = edges.Select(e => e.Dv).ToArray();
        // clamp initial values to [-2,2]
        int H0 = HStart;
        foreach (var e in edges) {
            V2 d = e.Dv;
            V2 c = new V2(Math.Max(-H0, Math.Min(H0, d.X)), Math.Max(-H0, Math.Min(H0, d.Y)));
            if (c != d) R.Clamped++;
            e.Dv = c;
        }
        // components of the network graph (balanced arcs connect rows)
        int NN = 2 * F;
        var uf = new int[NN]; for (int i = 0; i < NN; ++i) uf[i] = i;
        Func<int, int> find = null; find = x => { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; };
        for (int k = 0; k < edges.Count; ++k) {
            if (!balanced[k]) continue;
            var e = edges[k];
            for (int j = 0; j < 2; ++j) {
                RowOf(ex[e.F0][e.S0], j, out int row, out int sg);
                int a = find(2 * e.F0 + row), b = find(2 * e.F1 + row);
                if (a != b) uf[Math.Max(a, b)] = Math.Min(a, b);
            }
        }
        // residual per node
        long[] res = new long[NN];
        Action<Edge, int, V2> addEdge = null;
        Func<long[], long[]> compSums = r => {
            var cs = new Dictionary<int, long>();
            for (int i = 0; i < NN; ++i) { int c = find(i); cs.TryGetValue(c, out long v); cs[c] = v + r[i]; }
            var arr = new long[NN]; foreach (var kv in cs) arr[kv.Key] = kv.Value; return arr;
        };
        Action recompute = () => {
            Array.Clear(res, 0, NN);
            foreach (var e in edges) {
                V2 d0 = e.Dv;
                V2 v0 = V2.Rot(d0, ex[e.F0][e.S0]);
                res[2 * e.F0] += v0.X; res[2 * e.F0 + 1] += v0.Y;
                if (e.F1 >= 0) { V2 v1 = V2.Rot(d0, ex[e.F1][e.S1]); res[2 * e.F1] += v1.X; res[2 * e.F1 + 1] += v1.Y; }
            }
        };
        // ex[F0][S0] exponent applies to d (the master); ex[F1][S1] = M+2+rho applies to d as well (master expressed in F1 via -R^M)
        recompute();
        var Bc = compSums(res);
        var comps = new HashSet<int>(); for (int i = 0; i < NN; ++i) comps.Add(find(i)); R.Components = comps.Count;
        // feasibility: B = 0 per component, change fixed variables greedily
        long Tot(long[] b) { long t = 0; foreach (var c in comps) t += Math.Abs(b[c]); return t; }
        for (int round = 0; round < 100000 && Tot(Bc) > 0; ++round) {
            bool improved = false;
            for (int k = 0; k < edges.Count && !improved; ++k) {
                if (balanced[k]) continue;
                var e = edges[k];
                for (int j = 0; j < 2 && !improved; ++j)
                    for (int dir = -1; dir <= 1 && !improved; dir += 2) {
                        // effect of d_j += dir on component sums
                        var delta = new Dictionary<int, long>();
                        void Add(int f, int sl)
                        {
                            RowOf(ex[f][sl], j, out int row, out int sg);
                            int c = find(2 * f + row); delta.TryGetValue(c, out long v); delta[c] = v + sg * dir;
                        }
                        Add(e.F0, e.S0); if (e.F1 >= 0) Add(e.F1, e.S1);
                        long before = 0, after = 0;
                        foreach (var kv in delta) { before += Math.Abs(Bc[kv.Key]); after += Math.Abs(Bc[kv.Key] + kv.Value); }
                        if (after < before) {
                            foreach (var kv in delta) Bc[kv.Key] += kv.Value;
                            e.Dv = j == 0 ? new V2(e.Dv.X + dir, e.Dv.Y) : new V2(e.Dv.X, e.Dv.Y + dir);
                            improved = true;
                        }
                    }
            }
            if (!improved) break;
        }
        if (Tot(Bc) > 0) {
            // d = 0 on every fixed variable always achieves B = 0
            for (int k = 0; k < edges.Count; ++k) if (!balanced[k]) edges[k].Dv = new V2(0, 0);
            recompute(); Bc = compSums(res);
            R.Note += " B-fallback";
        }
        var preFlow = edges.Select(e => e.Dv).ToArray();
        R.TFlowPrep = swFlow.Elapsed.TotalSeconds; var swNet = Stopwatch.StartNew(); double tMcf = 0;
        // network, with capacities growing until full flow
        for (int H = H0; H <= 40; H += 2) {
            recompute();
            Mcf net0 = null; McfFast net1 = null;
            if (FastMcf) net1 = new McfFast(NN + 2, edges.Count * 6); else net0 = new Mcf(NN + 2);
            int AddA(int u, int v, int c, int w) { return FastMcf ? net1.Add(u, v, c, w) : net0.Add(u, v, c, w); }
            int FlowA(int a) { return FastMcf ? net1.Flow(a) : net0.Flow(a); }
            int S = NN, Tn = NN + 1;
            long supply = 0;
            for (int i = 0; i < NN; ++i) {
                if (res[i] > 0) supply += res[i];
                if (ExcessMcf) continue;
                if (res[i] > 0) AddA(S, i, (int)res[i], 0);
                else if (res[i] < 0) AddA(i, Tn, (int)(-res[i]), 0);
            }
            var arcF = new int[edges.Count * 2]; var arcB = new int[edges.Count * 2]; var arcLF = new List<int>[edges.Count * 2]; var arcLB = new List<int>[edges.Count * 2];
            for (int k = 0; k < edges.Count; ++k) {
                arcF[2 * k] = arcF[2 * k + 1] = arcB[2 * k] = arcB[2 * k + 1] = -1;
                if (!balanced[k]) continue;
                var e = edges[k];
                for (int j = 0; j < 2; ++j) {
                    RowOf(ex[e.F0][e.S0], j, out int row, out int sg);
                    int n0 = 2 * e.F0 + row, n1 = 2 * e.F1 + row;
                    int head = sg > 0 ? n0 : n1, tail = sg > 0 ? n1 : n0;
                    int v = j == 0 ? e.Dv.X : e.Dv.Y;
                    int capF = Math.Max(0, H - v), capB = Math.Max(0, H + v);
                    if (CostMode == 0) {
                        arcF[2 * k + j] = AddA(tail, head, capF, 1);
                        arcB[2 * k + j] = AddA(head, tail, capB, 1);
                    } else {
                        double r = j == 0 ? e.Rr.Item1 : e.Rr.Item2;
                        var lf = new List<int>(); var lb = new List<int>();
                        if (FastMcf) {
                            int run = 0, pc = 0;
                            for (int st = 1; st <= capF; ++st) { int c = Pen(k, StepCost(v + st - 1, v + st, r)); if (run > 0 && c == pc) run++; else { if (run > 0) lf.Add(AddA(tail, head, run, pc)); pc = c; run = 1; } }
                            if (run > 0) lf.Add(AddA(tail, head, run, pc));
                            run = 0;
                            for (int st = 1; st <= capB; ++st) { int c = Pen(k, StepCost(v - st + 1, v - st, r)); if (run > 0 && c == pc) run++; else { if (run > 0) lb.Add(AddA(head, tail, run, pc)); pc = c; run = 1; } }
                            if (run > 0) lb.Add(AddA(head, tail, run, pc));
                        } else {
                            for (int st = 1; st <= capF; ++st) { if (CostMode == 3 && Against(v + st - 1, v + st, r)) break; lf.Add(AddA(tail, head, 1, Pen(k, StepCost(v + st - 1, v + st, r)))); }
                            for (int st = 1; st <= capB; ++st) { if (CostMode == 3 && Against(v - st + 1, v - st, r)) break; lb.Add(AddA(head, tail, 1, Pen(k, StepCost(v - st + 1, v - st, r)))); }
                        }
                        arcLF[2 * k + j] = lf; arcLB[2 * k + j] = lb;
                    }
                }
            }
            R.HIters++; R.NArcs = FastMcf ? net1.ArcCount : net0.To.Count / 2; R.NNodes = NN + 2;
            var swM = Stopwatch.StartNew();
            long cost; int flow;
            if (FastMcf && ExcessMcf) { var exc = new int[NN + 2]; for (int i = 0; i < NN; ++i) exc[i] = (int)res[i]; flow = (int)net1.SolveExcess(exc, out cost); R.Augments = net1.Augmentations; R.Settled = net1.Settled; R.Phases = net1.Phases; }
            else if (FastMcf) { flow = net1.Solve(S, Tn, out cost); R.Augments = net1.Augmentations; R.Settled = net1.Settled; R.Phases = net1.Phases; } else { flow = net0.Solve(S, Tn, out cost); R.Augments = net0.Augmentations; }
            tMcf += swM.Elapsed.TotalSeconds; R.TMcf = tMcf; R.TNet = swNet.Elapsed.TotalSeconds;
            R.Hcap = H;
            if (flow < supply) continue;           // raise the capacity, solve again
            R.FlowUnits = (int)supply; R.FlowCost = (int)cost;
            for (int k = 0; k < edges.Count; ++k) {
                if (!balanced[k]) continue;
                var e = edges[k];
                Func<int, int> fl = jj => CostMode == 0 ? FlowA(arcF[2 * k + jj]) - FlowA(arcB[2 * k + jj]) : arcLF[2 * k + jj].Sum(a => FlowA(a)) - arcLB[2 * k + jj].Sum(a => FlowA(a));
                int dx = fl(0), dy = fl(1);
                e.Dv = new V2(e.Dv.X + dx, e.Dv.Y + dy);
            }
            recompute();
            long bad = 0; for (int i = 0; i < NN; ++i) bad += Math.Abs(res[i]);
            if (bad != 0) R.Note += " RESIDUAL " + bad;
            break;
        }
        DiagRecord(F, balanced, preFlow, initDv);
        // write the edge values back to the triangles
        foreach (var e in edges) {
            tris[e.F0].D[e.S0] = e.Dv;
            if (e.F1 >= 0) tris[e.F1].D[e.S1] = -V2.Rot(e.Dv, e.M);
        }
    }

    // ------------------------------------------------------------------------------------------ step 7

    void Link(int a, int sa, int b, int sb, int rot)
    {
        tris[a].Nb[sa] = b; tris[a].NbS[sa] = sb; tris[a].Rot[sa] = ((rot % 4) + 4) % 4;
        tris[b].Nb[sb] = a; tris[b].NbS[sb] = sa; tris[b].Rot[sb] = (((-rot) % 4) + 4) % 4;
    }

    static V2 Half(V2 d) { return new V2(d.X / 2, d.Y / 2); }

    int Subdivide()
    {
        for (int v = 0; v < maxV; ++v) { VP.Add(mesh.IsVertex(v) ? P[v] : Vector3d.Zero); VN.Add(mesh.IsVertex(v) ? Nrm[v] : Vector3d.Zero); VFrame.Add(v); }
        int splits = 0;
        if (FastSubdiv) {
            // same order as the scan below (largest |d|inf first, then lowest triangle, then lowest slot), by a priority queue with lazy invalidation
            var pq = new PriorityQueue<(int f, int sl, int inf), long>();
            long Key(int f, int sl, int inf) { return ((long)(100000 - Math.Min(inf, 99999)) << 40) | ((long)f << 2) | (long)sl; }
            void PushTri(int f) { for (int sl = 0; sl < 3; ++sl) { int inf = tris[f].D[sl].Inf; if (inf > 1) pq.Enqueue((f, sl, inf), Key(f, sl, inf)); } }
            for (int f = 0; f < tris.Count; ++f) PushTri(f);
            while (pq.Count > 0) {
                var (f, sl, inf) = pq.Dequeue();
                if (tris[f].D[sl].Inf != inf) continue;
                if (++splits > 400000) break;
                int nf = tris[f].Nb[sl];
                int n0 = tris.Count; SplitEdge(f, sl);
                PushTri(f); PushTri(n0); if (nf >= 0) { PushTri(nf); PushTri(n0 + 1); }
            }
            return splits;
        }
        while (true) {
            int bf = -1, bs = -1, best = 1;
            for (int f = 0; f < tris.Count; ++f)
                for (int sl = 0; sl < 3; ++sl) if (tris[f].D[sl].Inf > best) { best = tris[f].D[sl].Inf; bf = f; bs = sl; }
            if (bf < 0) break;
            if (++splits > 400000) break;
            SplitEdge(bf, bs);
        }
        return splits;
    }

    void SplitEdge(int f, int sl)
    {
        Tri F = tris[f];
        int p = F.V[sl], q = F.V[(sl + 1) % 3], x = F.V[(sl + 2) % 3];
        int nf = F.Nb[sl], ns = F.NbS[sl], m = F.Rot[sl];
        int w = VP.Count;
        VP.Add(0.5 * (VP[p] + VP[q]));
        Vector3d nn = VN[p] + VN[q]; VN.Add(nn.Length > 0 ? nn / nn.Length : VN[p]); VFrame.Add(VFrame[p]);
        V2 D = F.D[sl], Dp = Half(D), Dq = D - Dp;
        V2 Dqx = F.D[(sl + 1) % 3], Dxp = F.D[(sl + 2) % 3];
        if (SmartSplit) {
            // choose which half gets the larger part: fewer inverted new triangles (own heuristic, not in the paper)
            int bestNeg = int.MaxValue; V2 bestDp = Dp;
            for (int opt = 0; opt < 2; ++opt) {
                V2 dp = opt == 0 ? Half(D) : D - Half(D), dq = D - dp;
                int neg = 0;
                V2 e = -dp - Dxp;
                if (V2.Det(dp, e) < 0) neg++; if (V2.Det(dq, Dqx) < 0) neg++;
                if (nf >= 0) {
                    Tri N0 = tris[nf]; V2 hqw = -V2.Rot(dq, m), hwp = -V2.Rot(dp, m);
                    V2 dpy = N0.D[(ns + 1) % 3], dyq = N0.D[(ns + 2) % 3]; V2 e2 = -hqw - dyq;
                    if (V2.Det(hqw, e2) < 0) neg++; if (V2.Det(hwp, dpy) < 0) neg++;
                }
                if (neg < bestNeg) { bestNeg = neg; bestDp = dp; }
            }
            Dp = bestDp; Dq = D - Dp;
        }
        V2 E = -Dp - Dxp;
        int oq = F.Nb[(sl + 1) % 3], oqs = F.NbS[(sl + 1) % 3], oqr = F.Rot[(sl + 1) % 3];
        int ox = F.Nb[(sl + 2) % 3], oxs = F.NbS[(sl + 2) % 3], oxr = F.Rot[(sl + 2) % 3];
        // tA = (p, w, x) reuses f; tB = (w, q, x) is new
        var tB = new Tri { Fa = F.Fa, Orig = F.Orig }; int ib = tris.Count; tris.Add(tB);
        F = tris[f];
        F.V[0] = p; F.V[1] = w; F.V[2] = x; F.D[0] = Dp; F.D[1] = E; F.D[2] = Dxp;
        tB.V[0] = w; tB.V[1] = q; tB.V[2] = x; tB.D[0] = Dq; tB.D[1] = Dqx; tB.D[2] = -E;
        // neighbours across the outer edges
        if (oq >= 0) Link(ib, 1, oq, oqs, oqr); else tB.Nb[1] = -1;
        if (ox >= 0) Link(f, 2, ox, oxs, oxr); else F.Nb[2] = -1;
        Link(f, 1, ib, 2, 0);
        int fd = -1, fc = -1;
        if (nf >= 0) {
            Tri N = tris[nf];
            // N has slot ns: q -> p. its third vertex y at slot ns+2 -> ns : (y -> q) is slot ns+2? slots: ns: q->p, ns+1: p->y, ns+2: y->q
            int y = N.V[(ns + 2) % 3];
            V2 Hqw = -V2.Rot(Dq, m), Hwp = -V2.Rot(Dp, m);        // q -> w and w -> p in N's frame
            V2 Dpy = N.D[(ns + 1) % 3], Dyq = N.D[(ns + 2) % 3];
            V2 E2 = -Hqw - Dyq;                                   // w -> y
            int opn = N.Nb[(ns + 1) % 3], opns = N.NbS[(ns + 1) % 3], opr = N.Rot[(ns + 1) % 3];
            int oyn = N.Nb[(ns + 2) % 3], oyns = N.NbS[(ns + 2) % 3], oyr = N.Rot[(ns + 2) % 3];
            // tC = (q, w, y) reuses nf; tD = (w, p, y) is new
            var tD = new Tri { Fa = N.Fa, Orig = N.Orig }; fd = tris.Count; tris.Add(tD);
            N = tris[nf];
            N.V[0] = q; N.V[1] = w; N.V[2] = y; N.D[0] = Hqw; N.D[1] = E2; N.D[2] = Dyq;
            tD.V[0] = w; tD.V[1] = p; tD.V[2] = y; tD.D[0] = Hwp; tD.D[1] = Dpy; tD.D[2] = -E2;
            fc = nf;
            if (opn >= 0) Link(fd, 1, opn, opns, opr); else tD.Nb[1] = -1;
            if (oyn >= 0) Link(fc, 2, oyn, oyns, oyr); else N.Nb[2] = -1;
            Link(fc, 1, fd, 2, 0);
            // across the two halves of the old edge
            Link(f, 0, fd, 0, m);        // (p -> w) with (w -> p)
            Link(ib, 0, fc, 0, m);       // (w -> q) with (q -> w)
        }
    }

    // ------------------------------------------------------------------------------------------ step 8

    static int Det(Tri t) { return V2.Det(t.D[0], t.D[1]); }

    int Inverted() { int n = 0; foreach (var t in tris) if (Det(t) < 0) n++; return n; }

    double InvArea() { double a = 0; foreach (var t in tris) { int d = Det(t); if (d < 0) a -= d; } return a; }

    int[] cornerTri, cornerIdx;

    void BuildCorners()
    {
        int nv = VP.Count;
        cornerTri = new int[nv]; cornerIdx = new int[nv];
        for (int i = 0; i < nv; ++i) cornerTri[i] = -1;
        for (int f = 0; f < tris.Count; ++f)
            for (int c = 0; c < 3; ++c) if (cornerTri[tris[f].V[c]] < 0) { cornerTri[tris[f].V[c]] = f; cornerIdx[tris[f].V[c]] = c; }
    }

    // the fan of vertex u in ccw order (triangles and corner indices) and the cumulative frame rotation to each; false if holonomy
    bool Fan(int u, List<int> ft, List<int> fi, List<int> cum)
    {
        ft.Clear(); fi.Clear(); cum.Clear();
        int f0 = cornerTri[u], i0 = cornerIdx[u];
        if (f0 < 0) return false;
        int f = f0, i = i0, c = 0;
        for (int guard = 0; guard < 200; ++guard) {
            ft.Add(f); fi.Add(i); cum.Add(c);
            int sl = (i + 2) % 3;
            int nf = tris[f].Nb[sl]; if (nf < 0) return false;
            c = (c + tris[f].Rot[sl]) % 4;
            int ni = tris[f].NbS[sl];
            f = nf; i = ni;
            if (f == f0 && i == i0) return c % 4 == 0;
        }
        return false;
    }

    void Greedy(FlowResult R)
    {
        BuildCorners();
        var ft = new List<int>(); var fi = new List<int>(); var cum = new List<int>();
        for (int pass = 0; pass < 100; ++pass) {
            int moves = 0;
            var cand = new HashSet<int>();
            for (int f = 0; f < tris.Count; ++f) if (Det(tris[f]) < 0) foreach (int v in tris[f].V) cand.Add(v);
            var order = cand.ToList(); order.Sort();
            if (pass == 0) { int okFan = 0, longBlock = 0, noGain = 0; foreach (int u in order) { if (Fan(u, ft, fi, cum)) okFan++; } Console.WriteLine($"  [greedy dbg] candidate vertices {order.Count}, valid fans {okFan}"); }
            foreach (int u in order) {
                if (!Fan(u, ft, fi, cum)) continue;
                int n = ft.Count;
                long before = 0;
                for (int k = 0; k < n; ++k) { int d = Det(tris[ft[k]]); if (d < 0) before -= d; }
                if (before == 0) continue;
                long bestAfter = before; int bestJ = -1;
                for (int j = 0; j < n; ++j) {
                    V2 Vj = tris[ft[j]].D[fi[j]];
                    long after = 0; bool ok = true;
                    for (int k = 0; k < n && ok; ++k) {
                        V2 Vk = V2.Rot(Vj, cum[k] - cum[j]);
                        Tri t = tris[ft[k]]; int i = fi[k];
                        V2 o = t.D[i] - Vk, inn = t.D[(i + 2) % 3] + Vk;
                        if (o.Inf > 1 || inn.Inf > 1) { ok = false; break; }
                        V2[] nd = { t.D[0], t.D[1], t.D[2] }; nd[i] = o; nd[(i + 2) % 3] = inn;
                        int d = V2.Det(nd[0], nd[1]); if (d < 0) after -= d;
                    }
                    if (ok && after < bestAfter) { bestAfter = after; bestJ = j; }
                }
                if (bestJ < 0) continue;
                {
                    V2 Vj = tris[ft[bestJ]].D[fi[bestJ]];
                    for (int k = 0; k < n; ++k) {
                        V2 Vk = V2.Rot(Vj, cum[k] - cum[bestJ]);
                        Tri t = tris[ft[k]]; int i = fi[k];
                        t.D[i] = t.D[i] - Vk; t.D[(i + 2) % 3] = t.D[(i + 2) % 3] + Vk;
                    }
                    moves++;
                }
            }
            R.GreedyMoves += moves;
            if (moves == 0) break;
        }
    }

    // ------------------------------------------------------------------------------------------ step 8b: local SAT

    static readonly V2[] Dom = BuildDom();
    static V2[] BuildDom() { var l = new List<V2>(); for (int y = -1; y <= 1; ++y) for (int x = -1; x <= 1; ++x) l.Add(new V2(x, y)); return l.ToArray(); }
    static int DomIdx(V2 d) { return (d.Y + 1) * 3 + (d.X + 1); }

    bool TrySat(int seed, int ring, FlowResult R)
    {
        // region: triangles within `ring` adjacency steps of the seed
        var inR = new Dictionary<int, int>(); var q = new Queue<int>();
        inR[seed] = 0; q.Enqueue(seed);
        while (q.Count > 0) {
            int f = q.Dequeue(); if (inR[f] >= ring) continue;
            for (int sl = 0; sl < 3; ++sl) { int nb = tris[f].Nb[sl]; if (nb >= 0 && !inR.ContainsKey(nb)) { inR[nb] = inR[f] + 1; q.Enqueue(nb); } }
        }
        // mutable edges: both triangles in the region; variable = master in the lower triangle id
        var edgeId = new Dictionary<(int, int), int>();     // (f, slot) of master -> edge index
        var masters = new List<(int f, int s)>();
        foreach (int f in inR.Keys.OrderBy(x => x))
            for (int sl = 0; sl < 3; ++sl) {
                int nb = tris[f].Nb[sl];
                if (nb >= 0 && inR.ContainsKey(nb) && nb > f) { edgeId[(f, sl)] = masters.Count; masters.Add((f, sl)); edgeId[(nb, tris[f].NbS[sl])] = -1 - (masters.Count - 1); }
            }
        int M = masters.Count; if (M == 0) return false;
        var sat = new Dpll(9 * M) { Budget = SatBudget };
        var groups = new int[M][];
        for (int e = 0; e < M; ++e) {
            var (f, sl) = masters[e];
            V2 cur = tris[f].D[sl];
            var order = new List<int> { DomIdx(cur) };
            for (int k = 0; k < 9; ++k) if (k != DomIdx(cur)) order.Add(k);
            // prefer small changes: order the rest by distance to the current value
            order = new[] { order[0] }.Concat(order.Skip(1).OrderBy(k => (Dom[k] - cur).L1).ThenBy(k => k)).ToList();
            groups[e] = order.Select(k => 9 * e + k + 1).ToArray();
            sat.AddClause(Enumerable.Range(0, 9).Select(k => 9 * e + k + 1).ToArray());
            for (int a = 0; a < 9; ++a) for (int b = a + 1; b < 9; ++b) sat.AddClause(-(9 * e + a + 1), -(9 * e + b + 1));
        }
        // triangle clauses
        foreach (int f in inR.Keys) {
            Tri t = tris[f];
            var mutIdx = new List<int>(); var slotInfo = new List<(int e, int rot, bool master)>();   // per slot: edge index or -1 (fixed)
            var fixedVal = new V2[3]; var varOf = new int[3]; var rotOf = new int[3];
            for (int sl = 0; sl < 3; ++sl) {
                varOf[sl] = -1; fixedVal[sl] = t.D[sl];
                if (edgeId.TryGetValue((f, sl), out int id)) {
                    if (id >= 0) { varOf[sl] = id; rotOf[sl] = 0; }
                    else { varOf[sl] = -1 - id; rotOf[sl] = (masters[-1 - id].f == f) ? 0 : (tris[masters[-1 - id].f].Rot[masters[-1 - id].s] + 2) % 4; }
                }
            }
            // enumerate the combinations of the mutable slots
            var mutSlots = Enumerable.Range(0, 3).Where(sl => varOf[sl] >= 0).ToArray();
            if (FastSat) {
                // implication encoding: the last mutable slot is determined by the other values (sum = 0): (-a -b c) instead of the 9^k - valid forbidden triples
                int mk = mutSlots.Length;
                if (mk == 0) { if (!(fixedVal[0] + fixedVal[1] + fixedVal[2]).IsZero || V2.Det(fixedVal[0], fixedVal[1]) < 0) return false; continue; }
                int last = mutSlots[mk - 1]; int free = mk - 1; int combosF = 1; for (int i = 0; i < free; ++i) combosF *= 9;
                for (int c = 0; c < combosF; ++c) {
                    int cc = c; V2[] val = { fixedVal[0], fixedVal[1], fixedVal[2] }; var picks = new int[free];
                    for (int i = 0; i < free; ++i) { int k = cc % 9; cc /= 9; picks[i] = k; int sl = mutSlots[i]; val[sl] = V2.Rot(Dom[k], rotOf[sl]); }
                    V2 need = -(val[0] + val[1] + val[2] - val[last]);   // val[last] was the (unset) fixed value of the slot, remove it
                    V2 masterVal = V2.Rot(need, -rotOf[last]);
                    bool okv = masterVal.Inf <= 1;
                    if (okv) { val[last] = need; if (V2.Det(val[0], val[1]) < 0) okv = false; }
                    var lits = new int[free + (okv ? 1 : 0)];
                    for (int i = 0; i < free; ++i) lits[i] = -(9 * varOf[mutSlots[i]] + picks[i] + 1);
                    if (okv) lits[free] = 9 * varOf[last] + DomIdx(masterVal) + 1;
                    if (lits.Length == 0) return false;
                    sat.AddClause(lits);
                }
                continue;
            }
            int combos = 1; for (int i = 0; i < mutSlots.Length; ++i) combos *= 9;
            for (int c = 0; c < combos; ++c) {
                int cc = c; V2[] val = { fixedVal[0], fixedVal[1], fixedVal[2] }; var picks = new int[mutSlots.Length];
                for (int i = 0; i < mutSlots.Length; ++i) {
                    int k = cc % 9; cc /= 9; picks[i] = k;
                    int sl = mutSlots[i];
                    // master value Dom[k] -> slot value: master frame is F0; in the other triangle it is -R^M Dom[k] = R^(M+2)
                    val[sl] = V2.Rot(Dom[k], rotOf[sl]);
                }
                bool bad = !(val[0] + val[1] + val[2]).IsZero;
                int det = V2.Det(val[0], val[1]);
                if (!bad) bad = SatStrict ? det <= 0 : det < 0;
                if (!bad) continue;
                if (mutSlots.Length == 0) return false;    // a fixed triangle that is violated: this region cannot fix it
                var lits = new int[mutSlots.Length];
                for (int i = 0; i < mutSlots.Length; ++i) lits[i] = -(9 * varOf[mutSlots[i]] + picks[i] + 1);
                // identical edges in one triangle cannot happen
                sat.AddClause(lits);
                bool curMatch = true; for (int i = 0; i < mutSlots.Length; ++i) { var (mf, ms) = masters[varOf[mutSlots[i]]]; if (DomIdx(tris[mf].D[ms]) != picks[i]) curMatch = false; }
                if (curMatch) R.SatCurViol++;
            }
        }
        R.SatAttempts++;
        foreach (int f in inR.Keys) if (Det(tris[f]) < 0) R.SatInvInRegion++;
        int res = sat.Solve(groups, out bool[] model); R.SatDecisions += sat.Decisions; R.SatClauses += sat.Clauses.Count;
        if (res == 0) R.SatUnsat++; else if (res < 0) R.SatBudget++;
        if (res != 1) return false;
        for (int e = 0; e < M; ++e) {
            var (f, sl) = masters[e];
            V2 nv = V2.Zero;
            for (int k = 0; k < 9; ++k) if (model[9 * e + k]) nv = Dom[k];
            tris[f].D[sl] = nv;
            int nb = tris[f].Nb[sl];
            tris[nb].D[tris[f].NbS[sl]] = -V2.Rot(nv, tris[f].Rot[sl]);
        }
        return true;
    }

    void RepairSat(FlowResult R)
    {
        var failed = new HashSet<int>();
        for (int round = 0; round < 10000; ++round) {
            int seed = -1;
            for (int f = 0; f < tris.Count; ++f) if (Det(tris[f]) < 0 && !failed.Contains(f)) { seed = f; break; }
            if (seed < 0) break;
            bool ok = false;
            for (int ring = 1; ring <= SatMaxRing && !ok; ++ring) ok = TrySat(seed, ring, R);
            if (ok) R.SatSolved++; else { R.SatFailed++; failed.Add(seed); }
        }
    }

    // ------------------------------------------------------------------------------------------ step 9: positions

    Vector3d[] SolvePositions()
    {
        var swPos = Stopwatch.StartNew();
        if (PosMode == 2) {
            // contracted solve: one unknown (3D point) per cluster of the d = 0 edges, residual P_b - P_a - target on every other edge, weak anchor to the cluster mean
            int nv0 = VP.Count; var ufc = new int[nv0]; for (int i = 0; i < nv0; ++i) ufc[i] = i;
            int Find(int x) { while (ufc[x] != x) { ufc[x] = ufc[ufc[x]]; x = ufc[x]; } return x; }
            foreach (var t in tris) for (int sl = 0; sl < 3; ++sl) if (t.D[sl].IsZero) { int a = Find(t.V[sl]), b = Find(t.V[(sl + 1) % 3]); if (a != b) ufc[Math.Max(a, b)] = Math.Min(a, b); }
            var cid = new int[nv0]; for (int i = 0; i < nv0; ++i) cid[i] = -1;
            int nc = 0; var mean = new List<Vector3d>(); var cnt0 = new List<int>();
            for (int i = 0; i < nv0; ++i) { if (i < maxV && !mesh.IsVertex(i)) continue; int r = Find(i); if (cid[r] < 0) { cid[r] = nc++; mean.Add(Vector3d.Zero); cnt0.Add(0); } mean[cid[r]] += VP[i]; cnt0[cid[r]]++; }
            for (int c = 0; c < nc; ++c) mean[c] /= cnt0[c];
            var ch = new SparseCholesky(nc, 1); var rx = new double[nc]; var ry = new double[nc]; var rz = new double[nc];
            double wA = PosAnchor;
            for (int c = 0; c < nc; ++c) { ch.Add(c, c, wA); rx[c] += wA * mean[c].x; ry[c] += wA * mean[c].y; rz[c] += wA * mean[c].z; }
            foreach (var t in tris) for (int sl = 0; sl < 3; ++sl) {
                int a = cid[Find(t.V[sl])], b = cid[Find(t.V[(sl + 1) % 3])]; if (a == b) continue;
                Vector3d tg = s * (t.D[sl].X * Q[t.Fa] + t.D[sl].Y * T[t.Fa]);
                ch.Add(a, a, 1); ch.Add(b, b, 1); if (a > b) ch.Add(a, b, -1); else ch.Add(b, a, -1);
                rx[b] += tg.x; ry[b] += tg.y; rz[b] += tg.z; rx[a] -= tg.x; ry[a] -= tg.y; rz[a] -= tg.z;
            }
            var pos2 = new Vector3d[nv0];
            if (ch.Factorize()) {
                var X = new double[nc]; var Y = new double[nc]; var Z = new double[nc];
                ch.Solve(rx, X); ch.Solve(ry, Y); ch.Solve(rz, Z);
                for (int i = 0; i < nv0; ++i) { if (i < maxV && !mesh.IsVertex(i)) continue; int c = cid[Find(i)]; pos2[i] = new Vector3d(X[c], Y[c], Z[c]); }
            } else for (int i = 0; i < nv0; ++i) pos2[i] = VP[i];
            if (Environment.GetEnvironmentVariable("SPIKE_POS") == "1") Console.Error.WriteLine($"  [pos2] nv {nv0} clusters {nc} t {swPos.Elapsed.TotalSeconds:F3}");
            return pos2;
        }
        int nv = VP.Count;
        // tangent frames per vertex
        var E1 = new Vector3d[nv]; var E2 = new Vector3d[nv];
        for (int i = 0; i < nv; ++i) {
            if (i < maxV) { if (!mesh.IsVertex(i)) continue; E1[i] = Q[i]; E2[i] = T[i]; }
            else {
                Vector3d n = VN[i]; Vector3d e = Q[VFrame[i]]; e -= n.Dot(e) * n; double l = e.Length; e = l > 0 ? e / l : e;
                E1[i] = e; E2[i] = n.Cross(e);
            }
        }
        var A = new Dictionary<long, double>(); var rhs = new double[2 * nv];
        double lam = 1e-6 * s * s;
        var cholF = FastPos ? new SparseCholesky(2 * nv, 2) : null;
        Action<int, int, double> addA = (r, c, v) => { if (r < c) return; if (cholF != null) { cholF.Add(r, c, v); return; } long k = (long)r * (2 * nv) + c; A.TryGetValue(k, out double o); A[k] = o + v; };
        for (int i = 0; i < nv; ++i) { if (i < maxV && !mesh.IsVertex(i)) { for (int k = 0; k < 2; ++k) addA(2 * i + k, 2 * i + k, 1); continue; } for (int k = 0; k < 2; ++k) addA(2 * i + k, 2 * i + k, lam / (s * s)); }
        foreach (var t in tris)
            for (int sl = 0; sl < 3; ++sl) {
                int p = t.V[sl], q = t.V[(sl + 1) % 3];
                Vector3d target = s * (t.D[sl].X * Q[t.Fa] + t.D[sl].Y * T[t.Fa]);
                Vector3d c = (VP[q] - VP[p] - target) / s;
                // residual c + Xq xq - Xp xp, with x in units of s
                Vector3d[] cols = { -E1[p], -E2[p], E1[q], E2[q] }; int[] idx = { 2 * p, 2 * p + 1, 2 * q, 2 * q + 1 };
                for (int a = 0; a < 4; ++a) {
                    rhs[idx[a]] -= cols[a].Dot(c);
                    for (int b = 0; b <= a; ++b) { int r = Math.Max(idx[a], idx[b]), cc = Math.Min(idx[a], idx[b]); addA(r, cc, cols[a].Dot(cols[b])); }
                }
            }
        double tAsm = swPos.Elapsed.TotalSeconds;
        var chol = cholF ?? new SparseCholesky(2 * nv, 2);
        if (cholF == null) foreach (var kv in A) chol.Add((int)(kv.Key / (2 * nv)), (int)(kv.Key % (2 * nv)), kv.Value);
        var pos = new Vector3d[nv];
        if (!chol.Factorize()) { for (int i = 0; i < nv; ++i) pos[i] = VP[i]; return pos; }
        double tFac = swPos.Elapsed.TotalSeconds - tAsm;
        var x = new double[2 * nv]; chol.Solve(rhs, x);
        if (Environment.GetEnvironmentVariable("SPIKE_POS") == "1") Console.Error.WriteLine($"  [pos] nv {nv} assemble {tAsm:F3} factor {tFac:F3} solve {swPos.Elapsed.TotalSeconds - tAsm - tFac:F3}");
        for (int i = 0; i < nv; ++i) pos[i] = VP[i] + s * (x[2 * i] * E1[i] + x[2 * i + 1] * E2[i]);
        return pos;
    }

    // ------------------------------------------------------------------------------------------ step 10: extraction

    void Extract(FlowResult R, Vector3d[] pos)
    {
        int nv = VP.Count;
        var uf = new int[nv]; for (int i = 0; i < nv; ++i) uf[i] = i;
        Func<int, int> find = x => { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; };
        foreach (var t in tris)
            for (int sl = 0; sl < 3; ++sl) if (t.D[sl].IsZero) { int a = find(t.V[sl]), b = find(t.V[(sl + 1) % 3]); if (a != b) uf[Math.Max(a, b)] = Math.Min(a, b); }
        var sum = new Dictionary<int, Vector3d>(); var cnt = new Dictionary<int, int>(); var nsum = new Dictionary<int, Vector3d>();
        for (int i = 0; i < nv; ++i) {
            if (i < maxV && !mesh.IsVertex(i)) continue;
            int r = find(i); sum.TryGetValue(r, out Vector3d sv); sum[r] = sv + pos[i]; cnt.TryGetValue(r, out int c); cnt[r] = c + 1; nsum.TryGetValue(r, out Vector3d nv0); nsum[r] = nv0 + VN[i];
        }
        // nondegenerate triangles: group by the hypotenuse cluster pair
        var hyp = new Dictionary<(int, int), List<(int a, int b, int c)>>();
        var leftover = new List<int[]>();
        var used = new bool[tris.Count];
        foreach (var t in tris) {
            int[] cl = { find(t.V[0]), find(t.V[1]), find(t.V[2]) };
            if (cl[0] == cl[1] || cl[1] == cl[2] || cl[0] == cl[2]) continue;
            int det = Det(t);
            if (det < 0) R.InvertedTris++;
            int h = -1;
            for (int sl = 0; sl < 3; ++sl) if (t.D[sl].L1 == 2 && t.D[sl].Inf == 1) h = sl;
            int ok = 0; for (int sl = 0; sl < 3; ++sl) if (t.D[sl].L1 == 1) ok++;
            if (h < 0 || ok != 2) { leftover.Add(cl); R.BadQuads++; continue; }
            int A = cl[h], B = cl[(h + 1) % 3], C = cl[(h + 2) % 3];
            var key = A < B ? (A, B) : (B, A);
            if (!hyp.TryGetValue(key, out var lst)) hyp[key] = lst = new List<(int, int, int)>();
            lst.Add((A, B, C));
        }
        var outMesh = new DPolyMesh();
        var vmap = new Dictionary<int, int>();
        DMeshAABBTree3 tree = null; if (Project) { tree = new DMeshAABBTree3(mesh, true); }
        Func<int, int> vid = c => { if (!vmap.TryGetValue(c, out int id)) { Vector3d p = sum[c] / cnt[c]; if (tree != null) { int nt = tree.FindNearestTriangle(p); if (nt >= 0) { var dq = MeshQueries.TriangleDistance(mesh, nt, p); p = dq.TriangleClosest; } } id = outMesh.AppendVertex(p.x, p.y, p.z); vmap[c] = id; } return id; };
        foreach (var kv in hyp) {
            var l = new List<(int a, int b, int c)>(kv.Value);
            if (Cancel) for (int i = 0; i < l.Count; ++i) for (int j = i + 1; j < l.Count; ++j)
                if (l[i].a == l[j].b && l[i].b == l[j].a && l[i].c == l[j].c) { l.RemoveAt(j); l.RemoveAt(i); R.Cancelled++; i = -1; break; }
            if (l.Count == 2 && l[0].a == l[1].b && l[0].b == l[1].a && l[0].c != l[1].c) {
                int A = l[0].a, B = l[0].b, C1 = l[0].c, C2 = l[1].c;
                outMesh.AppendFace(new List<int> { vid(A), vid(C2), vid(B), vid(C1) }, 0);
                {   // geometric check: the quad normal against the mean surface normal of its clusters
                    Vector3d pa = sum[A] / cnt[A], pc2 = sum[C2] / cnt[C2], pb = sum[B] / cnt[B], pc1 = sum[C1] / cnt[C1];
                    Vector3d fn = (pb - pa).Cross(pc1 - pc2); Vector3d sn = nsum[A] + nsum[B] + nsum[C1] + nsum[C2];
                    R.Quads++; if (fn.Dot(sn) <= 0) R.FlippedQuads++;
                }
            } else {
                if (Environment.GetEnvironmentVariable("SPIKE_DBG") == "1") Console.WriteLine($"  [dbg] unpaired key {kv.Key}: count {l.Count} " + string.Join(" ", l.Select(t => $"({t.a},{t.b},{t.c})")));
                foreach (var tr in l) leftover.Add(new[] { tr.a, tr.b, tr.c });
                R.Unpaired += l.Count;
            }
        }
        foreach (var tr in leftover) outMesh.AppendFace(new List<int> { vid(tr[0]), vid(tr[1]), vid(tr[2]) }, 0);
        R.Out = outMesh;
        if (outMesh.FaceCount > 0) R.Metrics = PolyMeshMetrics.Compute(outMesh);
    }
}
