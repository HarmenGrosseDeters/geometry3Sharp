// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

namespace gs
{
    /// <summary>
    /// Pure quads from the position field, opt-in stage 3B of QuadRemesher (docs/design/QUAD_REMESHING.md): the repair of the integer
    /// lattice offsets of the position field after Huang, Zhou, Niessner, Shewchuk, Guibas, "QuadriFlow: A Scalable and Robust Method
    /// for Quadrangulation", CGF 37(5) 2018 (sections 3.2-3.6; written from the paper), then the extraction of a mesh of quads only.
    ///
    /// Every triangle carries the three integer offsets of its edges in the lattice frame of its first vertex; the frames of
    /// neighbours differ by a quarter turn (the transition). A lattice that is regular needs the three offsets of every triangle to
    /// sum to zero. The steps: (1) the offsets from the position field; (2) the equations "sum = 0" per triangle and component, with
    /// the frame rotations absorbed by a breadth-first rotation of the equations, which makes every edge between two triangles a
    /// +1 / -1 variable of a network (the edges whose cycle has a holonomy stay fixed variables, adjusted greedily so that every
    /// connected component of the network is balanced); (3) the min-cost flow of the integer corrections (MinCostFlow, successive
    /// shortest paths per source with early termination; Ahuja, Magnanti, Orlin, Network Flows, 9.7), with the capacity of the
    /// variables growing from 2 by 2 until the flow is full; the unit cost of a step is the increase of |d - r| over the real
    /// offset r of the position field (two tiers, +1), times 100 when the step grows |d| against the sign of r (the sign-aware
    /// cost, it softens the orientation constraints of Bommes et al., "Integer-Grid Maps for Reliable Quad Meshing", TOG 32(4)
    /// 2013); (4) the subdivision of every edge with |d|inf above 1 (largest first, by a priority queue with lazy invalidation, key
    /// (|d|inf descending, triangle, slot)); (5) the greedy move of vertices that lowers the area of the inverted triangles; (6) the
    /// local repair of the remaining inverted triangles by SAT over rings 1 to 6 of triangles around a seed (GroupSatSolver, the
    /// implication encoding of the triangle constraints, a decision budget for the whole repair: counted, never timed); (7) the
    /// position of one point per cluster of the d = 0 edges (least squares of the offsets, SparseCholesky) projected onto the
    /// input; (8) the extraction: d = 0 edges collapse, the triangles are paired over their hypotenuses, pairs of triangles that
    /// have the same three clusters (2-gons) cancel.
    ///
    /// The detection is exact: after the repair every offset is a small integer, det(D0, D1) of a triangle is an int, and the
    /// number of triangles with det &lt; 0 is InvertedTriangles; a map with such a triangle is not used. Deterministic: everything
    /// is serial, integer where it counts, with no hash order and no tie of a priority queue in a result.
    /// </summary>
    internal sealed class QuadFlowRepair
    {
        // ------------------------------------------------------------------------------------------ settings and results

        /// <summary> the sign-aware flow cost (cost mode 2); false: the plain marginal cost (cost mode 1, the test hook that makes the sphere fold) </summary>
        public bool SignPenalty = true;
        /// <summary> decisions of the SAT over the whole repair; exhausted: BudgetExceeded </summary>
        public long SatTotalBudget = 300000;
        /// <summary> decisions of one SAT attempt </summary>
        public long SatAttemptBudget = 100000;
        public int SatMaxRing = 6;
        /// <summary> most edge splits of the subdivision (a budget of the same kind) </summary>
        public int MaxSplits = 400000;
        /// <summary> the largest capacity of the flow variables (from 2, by 2); a flow that is not full by then is infeasible </summary>
        public int MaxFlowCapacity = 40;
        /// <summary> SAT attempts (also those that fail at once and cost no decision) and seeds tried; exhausted: the SAT budget is exhausted </summary>
        public int SatMaxAttempts = 20000;
        /// <summary> edge visits of the greedy balancing of the fixed variables (a count, not a clock); then every fixed variable is set to 0 </summary>
        public long GreedyWorkLimit = 400000000;

        public PureQuadStatus Status = PureQuadStatus.NotRequested;
        public int InvertedTriangles, FlippedQuads;
        public DPolyMesh Result;
        /// <summary> the setup and the flow, the subdivision, the greedy moves and the SAT </summary>
        public double FlowSeconds, RepairSeconds;

        // the profile and counters of the last run (benchmarks, tests)
        public double SetupSeconds, FlowPrepSeconds, FlowNetSeconds, SubdivisionSeconds, GreedySeconds, SatSeconds, PositionSeconds, ExtractSeconds;
        public int Triangles, Splits, FlowArcs, FlowNodes, FlowCapacity, FlowSearches, SatAttempts, SatSolved, SatFailed, GreedyMoves, Cancelled, UnpairedTriangles, BadTriangles, Quads;
        public long FlowSettled, SatDecisions;
        public bool SatBudgetExhausted;

        // ------------------------------------------------------------------------------------------ data

        const double SignTolerance = 0;     // |r| above which a step against the sign of r is penalized
        const int SignPenaltyFactor = 100;


        readonly DMesh3 mesh; readonly CrossField cf; readonly PositionField pf; readonly double s;
        int maxV;
        Vector3d[] P, Nrm, Q, T;
        int[] gA; Off[] dA; (double, double)[] rA;
        bool setupFailed;

        struct Off
        {
            public int X, Y;
            public Off(int x, int y) { X = x; Y = y; }
            public static Off operator +(Off a, Off b) { return new Off(a.X + b.X, a.Y + b.Y); }
            public static Off operator -(Off a, Off b) { return new Off(a.X - b.X, a.Y - b.Y); }
            public static Off operator -(Off a) { return new Off(-a.X, -a.Y); }
            public static bool operator ==(Off a, Off b) { return a.X == b.X && a.Y == b.Y; }
            public static bool operator !=(Off a, Off b) { return !(a == b); }
            public override bool Equals(object o) { return o is Off v && v == this; }
            public override int GetHashCode() { return X * 31 + Y; }
            public static Off Rot(Off v, int k) { k = ((k % 4) + 4) % 4; for (int i = 0; i < k; ++i) v = new Off(-v.Y, v.X); return v; }
            public static int Det(Off a, Off b) { long d = (long)a.X * b.Y - (long)a.Y * b.X; return d > int.MaxValue ? int.MaxValue : d < int.MinValue ? int.MinValue : (int)d; }
            public int Inf { get { return Math.Max(Math.Abs(X), Math.Abs(Y)); } }
            public int L1 { get { return Math.Abs(X) + Math.Abs(Y); } }
            public bool IsZero { get { return X == 0 && Y == 0; } }
        }

        sealed class Tri
        {
            public (double, double)[] Dr = new (double, double)[3];
            public int[] V = new int[3];
            public Off[] D = new Off[3];        // slot s: vertex s -> s+1, in the frame of this triangle
            public int[] Nb = { -1, -1, -1 };   // neighbour across slot s
            public int[] NbS = new int[3];      // its slot (the reversed edge)
            public int[] Rot = new int[3];      // D_neighbour = - R^Rot D_self
            public int[] E = new int[3];        // mesh edge ids (original triangles only)
            public int Fa;                      // frame vertex (an original mesh vertex)
        }

        sealed class Edge { public int F0, S0, F1 = -1, S1, M; public Off Dv; public (double, double) Rr; }

        readonly List<Tri> tris = new List<Tri>();
        readonly List<Vector3d> VP = new List<Vector3d>(), VN = new List<Vector3d>();

        public QuadFlowRepair(DMesh3 mesh, CrossField cf, PositionField pf, double scale)
        {
            this.mesh = mesh; this.cf = cf; this.pf = pf; s = scale;
        }

        /// <summary> the offsets of every triangle slot after Run (three per triangle, X then Y; the triangles after the subdivision) </summary>
        public int[] Offsets()
        {
            var r = new int[tris.Count * 6];
            for (int f = 0; f < tris.Count; ++f)
                for (int sl = 0; sl < 3; ++sl) { r[6 * f + 2 * sl] = tris[f].D[sl].X; r[6 * f + 2 * sl + 1] = tris[f].D[sl].Y; }
            return r;
        }

        // ------------------------------------------------------------------------------------------ run

        bool ran;

        /// <summary> runs the repair; once per instance (the data of the run stays for inspection) </summary>
        public PureQuadStatus Run()
        {
            if (ran) throw new InvalidOperationException("QuadFlowRepair.Run can be called once");
            ran = true;
            Status = PureQuadStatus.NotRequested; Result = null; InvertedTriangles = FlippedQuads = 0;
            var sw = Stopwatch.StartNew();
            BuildOffsets();
            if (setupFailed) { FlowSeconds = sw.Elapsed.TotalSeconds; return Status = PureQuadStatus.FlowInfeasible; }
            BuildTriangles();
            SetupSeconds = sw.Elapsed.TotalSeconds; Triangles = tris.Count;
            // a boundary is not supported: its edges would be free variables of the flow, and the offsets that the position field leaves
            // there are not small (the measurement covers closed meshes only)
            foreach (var tr in tris) if (tr.Nb[0] < 0 || tr.Nb[1] < 0 || tr.Nb[2] < 0) { FlowSeconds = sw.Elapsed.TotalSeconds; return Status = PureQuadStatus.FlowInfeasible; }
            bool feasible = SolveFlow();
            FlowSeconds = sw.Elapsed.TotalSeconds;
            if (!feasible) { InvertedTriangles = Inverted(); return Status = PureQuadStatus.FlowInfeasible; }
            sw.Restart();
            bool splitOk = Subdivide(); SubdivisionSeconds = sw.Elapsed.TotalSeconds; sw.Restart();
            Greedy(); GreedySeconds = sw.Elapsed.TotalSeconds; sw.Restart();
            if (splitOk) RepairSat();
            SatSeconds = sw.Elapsed.TotalSeconds;
            RepairSeconds = SubdivisionSeconds + GreedySeconds + SatSeconds;
            InvertedTriangles = Inverted();
            if (!splitOk) return Status = PureQuadStatus.BudgetExceeded;
            if (InvertedTriangles > 0) return Status = SatBudgetExhausted ? PureQuadStatus.BudgetExceeded : PureQuadStatus.OrientationFolds;
            if (!MapCloses()) return Status = PureQuadStatus.FlowInfeasible;
            sw.Restart();
            Vector3d[] pos = SolvePositions();
            PositionSeconds = sw.Elapsed.TotalSeconds; sw.Restart();
            if (pos == null) return Status = PureQuadStatus.FlowInfeasible;
            Extract(pos);
            ExtractSeconds = sw.Elapsed.TotalSeconds;
            return Status;
        }

        // ------------------------------------------------------------------------------------------ step 1: offsets

        static (double, double) RotD((double X, double Y) v, int k) { k = ((k % 4) + 4) % 4; for (int i = 0; i < k; ++i) v = (-v.Y, v.X); return v; }

        void BuildOffsets()
        {
            maxV = mesh.MaxVertexID; gA = new int[mesh.MaxEdgeID]; dA = new Off[mesh.MaxEdgeID]; rA = new (double, double)[mesh.MaxEdgeID];
            P = new Vector3d[maxV]; Nrm = new Vector3d[maxV]; Q = new Vector3d[maxV]; T = new Vector3d[maxV];
            foreach (int v in mesh.VertexIndices()) {
                if (!pf.HasPosition(v)) continue;
                P[v] = mesh.GetVertex(v); Nrm[v] = cf.VertexNormal(v); Q[v] = pf.Orientation(v); T[v] = Nrm[v].Cross(Q[v]);
            }
            foreach (int e in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(e);
                int u = Math.Min(ev.a, ev.b), v = Math.Max(ev.a, ev.b);
                if (!pf.HasPosition(u) || !pf.HasPosition(v)) { setupFailed = true; return; }
                Vector3d x = PositionField.MiddlePoint(P[u], Nrm[u], P[v], Nrm[v], pf.MiddleRegularization);
                // the compatible representative of v's cross: b = R^m q_v
                Vector3d tv = T[v];
                double dq = Q[u].Dot(Q[v]), dt = Q[u].Dot(tv);
                Vector3d b; int m;
                if (Math.Abs(dq) >= Math.Abs(dt)) { if (dq >= 0) { b = Q[v]; m = 0; } else { b = -Q[v]; m = 2; } }
                else { if (dt >= 0) { b = tv; m = 1; } else { b = -tv; m = 3; } }
                Vector3d tb = Nrm[v].Cross(b);
                Vector3d qi = Q[u], ti = T[u], oi = pf.Origin(u), oj = pf.Origin(v);
                Vector3d d = x - oi;
                double cq0 = Math.Round(qi.Dot(d) / s), ct0 = Math.Round(ti.Dot(d) / s);
                if (!(Math.Abs(cq0) < 1e9) || !(Math.Abs(ct0) < 1e9)) { setupFailed = true; return; }
                int kq0 = (int)cq0, kt0 = (int)ct0;
                Vector3d a = oi + s * (kq0 * qi + kt0 * ti);
                Vector3d d2 = a - oj;
                double cq1 = Math.Round(b.Dot(d2) / s), ct1 = Math.Round(tb.Dot(d2) / s);
                if (!(Math.Abs(cq1) < 1e9) || !(Math.Abs(ct1) < 1e9)) { setupFailed = true; return; }
                int kq1 = (int)cq1, kt1 = (int)ct1;
                gA[e] = (4 - m) % 4;
                dA[e] = new Off(kq0 - kq1, kt0 - kt1);
                // the real offset: the coordinates of x relative to o_u minus those relative to o_v (in the compatible basis)
                Vector3d dv = x - oj;
                rA[e] = (qi.Dot(d) / s - b.Dot(dv) / s, ti.Dot(d) / s - tb.Dot(dv) / s);
            }
        }

        // edge-id based lookups: the data of the edge (u < w) as seen from u or from w
        (double, double) DirRe(int u, int w, int e)
        {
            if (u < w) return rA[e];
            var d = rA[e]; var r = RotD(d, -gA[e]); return (-r.Item1, -r.Item2);
        }
        int Ge(int u, int w, int e) { if (u == w) return 0; return u < w ? gA[e] : (4 - gA[e]) % 4; }
        Off Dire(int u, int w, int e) { if (u < w) return dA[e]; return -Off.Rot(dA[e], -gA[e]); }

        void BuildTriangles()
        {
            var tidToF = new int[mesh.MaxTriangleID];
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                int a = tv.a, b = tv.b, c = tv.c;
                var tr = new Tri { Fa = a };
                tr.V[0] = a; tr.V[1] = b; tr.V[2] = c;
                Index3i te = mesh.GetTriEdges(t); tr.E[0] = te.a; tr.E[1] = te.b; tr.E[2] = te.c;
                int gab = Ge(a, b, te.a), gac = Ge(a, c, te.c);
                tr.D[0] = Dire(a, b, te.a);
                tr.D[1] = Off.Rot(Dire(b, c, te.b), gab);
                tr.D[2] = Off.Rot(Dire(c, a, te.c), gac);
                tr.Dr[0] = DirRe(a, b, te.a); tr.Dr[1] = RotD(DirRe(b, c, te.b), gab); tr.Dr[2] = RotD(DirRe(c, a, te.c), gac);
                tidToF[t] = tris.Count;
                tris.Add(tr);
            }
            // G of two vertices of a triangle (the frame vertex with another vertex)
            int GT(Tri tt, int u, int w)
            {
                if (u == w) return 0;
                int iu = tt.V[0] == u ? 0 : tt.V[1] == u ? 1 : 2, iw = tt.V[0] == w ? 0 : tt.V[1] == w ? 1 : 2;
                int sl = (iu + 1) % 3 == iw ? iu : iw;      // the slot of the edge {u, w}
                return Ge(u, w, tt.E[sl]);
            }
            for (int f = 0; f < tris.Count; ++f)
                for (int sl = 0; sl < 3; ++sl) {
                    int e = tris[f].E[sl]; Index2i et = mesh.GetEdgeT(e);
                    int ntid = et.a == -1 ? -1 : et.b == -1 ? -1 : (tidToF[et.a] == f ? et.b : et.a);
                    if (ntid < 0) continue;
                    int nb = tidToF[ntid];
                    int ns = tris[nb].E[0] == e ? 0 : tris[nb].E[1] == e ? 1 : 2;
                    int p = tris[f].V[sl], q = tris[f].V[(sl + 1) % 3];
                    tris[f].Nb[sl] = nb; tris[f].NbS[sl] = ns;
                    Tri Tf = tris[f], Tn = tris[nb];
                    int fa = Tf.V[0], fb = Tn.V[0];
                    tris[f].Rot[sl] = (((GT(Tn, fb, q) - Ge(p, q, e) - GT(Tf, fa, p)) % 4) + 4) % 4;
                }
        }

        // ------------------------------------------------------------------------------------------ steps 2-3: the equations and the flow

        // coefficient (row, sign) of component j of a variable with exponent x
        static void RowOf(int x, int j, out int row, out int sign)
        {
            x = ((x % 4) + 4) % 4;
            switch (x) {
                case 0: row = j == 0 ? 0 : 1; sign = 1; break;
                case 1: if (j == 0) { row = 1; sign = 1; } else { row = 0; sign = -1; } break;
                case 2: row = j == 0 ? 0 : 1; sign = -1; break;
                default: if (j == 0) { row = 1; sign = -1; } else { row = 0; sign = 1; } break;
            }
        }

        // integer cost of a unit step from a to b for the real value r: 100 * (|b - r| - |a - r| + 1), >= 0
        static int MarginalCost(int a, int b, double r) { return (int)Math.Round(100 * (Math.Abs(b - r) - Math.Abs(a - r) + 1)); }

        int StepCost(int a, int b, double r)
        {
            int c = MarginalCost(a, b, r);
            if (SignPenalty && Math.Abs(b) > Math.Abs(a) && b * r < 0 && Math.Abs(r) > SignTolerance) c = (int)Math.Round((double)c * SignPenaltyFactor);
            return c;
        }

        static int FindRoot(int[] uf, int x) { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; }

        /// <summary> the corrections of the offsets by the flow; false if the flow cannot be completed (no triangle data is changed then, except the clamping) </summary>
        bool SolveFlow()
        {
            var sw = Stopwatch.StartNew();
            int F = tris.Count;
            var edges = new List<Edge>();
            for (int f = 0; f < F; ++f)
                for (int sl = 0; sl < 3; ++sl) {
                    int nb = tris[f].Nb[sl];
                    if (nb >= 0 && nb < f) continue;
                    var e = new Edge { F0 = f, S0 = sl, Dv = tris[f].D[sl], Rr = tris[f].Dr[sl] };
                    if (nb >= 0) { e.F1 = nb; e.S1 = tris[f].NbS[sl]; e.M = tris[f].Rot[sl]; }
                    edges.Add(e);
                }
            int E = edges.Count;
            // the rotation of the equation of every triangle: a breadth-first search over the triangles (from every component)
            var rho = new int[F]; var vis = new bool[F];
            var adjStart = new int[F + 1];
            for (int k = 0; k < E; ++k) { adjStart[edges[k].F0 + 1]++; if (edges[k].F1 >= 0) adjStart[edges[k].F1 + 1]++; }
            for (int f = 0; f < F; ++f) adjStart[f + 1] += adjStart[f];
            var adjList = new int[adjStart[F]]; var fill = new int[F];
            for (int k = 0; k < E; ++k) {
                adjList[adjStart[edges[k].F0] + fill[edges[k].F0]++] = k;
                if (edges[k].F1 >= 0) adjList[adjStart[edges[k].F1] + fill[edges[k].F1]++] = k;
            }
            var queue = new int[F];
            for (int root = 0; root < F; ++root) {
                if (vis[root]) continue;
                int qh = 0, qt = 0; vis[root] = true; queue[qt++] = root;
                while (qh < qt) {
                    int f = queue[qh++];
                    for (int a = adjStart[f]; a < adjStart[f + 1]; ++a) {
                        var e = edges[adjList[a]]; if (e.F1 < 0) continue;
                        int c = e.F0 == f ? e.F1 : e.F0;
                        if (vis[c]) continue;
                        vis[c] = true;
                        rho[c] = e.F0 == f ? ((rho[f] - e.M) % 4 + 4) % 4 : ((rho[f] + e.M) % 4 + 4) % 4;
                        queue[qt++] = c;
                    }
                }
            }
            // the exponent of the slot in the rotated equation
            var ex = new int[F * 3];
            foreach (var e in edges) {
                ex[e.F0 * 3 + e.S0] = (0 + rho[e.F0]) % 4;
                if (e.F1 >= 0) ex[e.F1 * 3 + e.S1] = (e.M + 2 + rho[e.F1]) % 4;
            }
            var balanced = new bool[E];
            for (int k = 0; k < E; ++k) {
                var e = edges[k];
                balanced[k] = e.F1 >= 0 && (((ex[e.F0 * 3 + e.S0] - ex[e.F1 * 3 + e.S1] - 2) % 4) + 4) % 4 == 0;
            }
            // clamp the initial values to [-2, 2]
            const int H0 = 2;
            foreach (var e in edges) {
                Off d = e.Dv;
                e.Dv = new Off(Math.Max(-H0, Math.Min(H0, d.X)), Math.Max(-H0, Math.Min(H0, d.Y)));
            }
            // the components of the network (the balanced edges connect the rows of two triangles)
            int NN = 2 * F;
            var uf = new int[NN]; for (int i = 0; i < NN; ++i) uf[i] = i;
            for (int k = 0; k < E; ++k) {
                if (!balanced[k]) continue;
                var e = edges[k];
                for (int j = 0; j < 2; ++j) {
                    RowOf(ex[e.F0 * 3 + e.S0], j, out int row, out int sg);
                    int a = FindRoot(uf, 2 * e.F0 + row), b = FindRoot(uf, 2 * e.F1 + row);
                    if (a != b) uf[Math.Max(a, b)] = Math.Min(a, b);
                }
            }
            var res = new long[NN];
            void Recompute()
            {
                Array.Clear(res, 0, NN);
                foreach (var e in edges) {
                    Off v0 = Off.Rot(e.Dv, ex[e.F0 * 3 + e.S0]);
                    res[2 * e.F0] += v0.X; res[2 * e.F0 + 1] += v0.Y;
                    if (e.F1 >= 0) { Off v1 = Off.Rot(e.Dv, ex[e.F1 * 3 + e.S1]); res[2 * e.F1] += v1.X; res[2 * e.F1 + 1] += v1.Y; }
                }
            }
            long[] CompSums()
            {
                var arr = new long[NN];
                for (int i = 0; i < NN; ++i) arr[FindRoot(uf, i)] += res[i];
                return arr;
            }
            Recompute();
            long[] Bc = CompSums();
            long tot = 0; for (int i = 0; i < NN; ++i) tot += Math.Abs(Bc[i]);
            // feasibility: B = 0 for every component; the fixed variables change greedily (the first that lowers the total imbalance)
            long work = 0;
            for (int round = 0; round < 100000 && tot > 0 && work < GreedyWorkLimit; ++round) {
                bool improved = false;
                work += E;
                for (int k = 0; k < E && !improved; ++k) {
                    if (balanced[k]) continue;
                    var e = edges[k];
                    for (int j = 0; j < 2 && !improved; ++j)
                        for (int dir = -1; dir <= 1 && !improved; dir += 2) {
                            RowOf(ex[e.F0 * 3 + e.S0], j, out int row0, out int sg0);
                            int c0 = FindRoot(uf, 2 * e.F0 + row0); long d0 = sg0 * dir;
                            int c1 = -1; long d1 = 0;
                            if (e.F1 >= 0) { RowOf(ex[e.F1 * 3 + e.S1], j, out int row1, out int sg1); c1 = FindRoot(uf, 2 * e.F1 + row1); d1 = sg1 * dir; }
                            long before, after;
                            if (c1 == c0) { d0 += d1; c1 = -1; }
                            before = Math.Abs(Bc[c0]); after = Math.Abs(Bc[c0] + d0);
                            if (c1 >= 0) { before += Math.Abs(Bc[c1]); after += Math.Abs(Bc[c1] + d1); }
                            if (after < before) {
                                Bc[c0] += d0; if (c1 >= 0) Bc[c1] += d1;
                                tot -= before - after;
                                e.Dv = j == 0 ? new Off(e.Dv.X + dir, e.Dv.Y) : new Off(e.Dv.X, e.Dv.Y + dir);
                                improved = true;
                            }
                        }
                }
                if (!improved) break;
            }
            if (tot > 0) {
                // d = 0 on every fixed variable
                for (int k = 0; k < E; ++k) if (!balanced[k]) edges[k].Dv = new Off(0, 0);
                Recompute();
            }
            FlowPrepSeconds = sw.Elapsed.TotalSeconds; sw.Restart();
            // the network, with the capacity growing until the flow is full
            bool full = false;
            for (int H = H0; H <= MaxFlowCapacity; H += 2) {
                Recompute();
                var net = new MinCostFlow(NN, E * 6);
                long supply = 0;
                var excess = new int[NN];
                for (int i = 0; i < NN; ++i) { if (res[i] > 0) supply += res[i]; excess[i] = (int)res[i]; }
                var fF = new int[E * 2]; var nF = new int[E * 2]; var fB = new int[E * 2]; var nB = new int[E * 2];
                for (int k = 0; k < E; ++k) {
                    if (!balanced[k]) continue;
                    var e = edges[k];
                    for (int j = 0; j < 2; ++j) {
                        RowOf(ex[e.F0 * 3 + e.S0], j, out int row, out int sg);
                        int n0 = 2 * e.F0 + row, n1 = 2 * e.F1 + row;
                        int head = sg > 0 ? n0 : n1, tail = sg > 0 ? n1 : n0;
                        int v = j == 0 ? e.Dv.X : e.Dv.Y;
                        int capF = Math.Max(0, H - v), capB = Math.Max(0, H + v);
                        double r = j == 0 ? e.Rr.Item1 : e.Rr.Item2;
                        int idx = 2 * k + j;
                        // one arc per run of unit steps of the same cost
                        int run = 0, pc = 0;
                        fF[idx] = net.ArcCount * 2; nF[idx] = 0;
                        for (int st = 1; st <= capF; ++st) {
                            int c = StepCost(v + st - 1, v + st, r);
                            if (run > 0 && c == pc) run++; else { if (run > 0) { net.AddArc(tail, head, run, pc); nF[idx]++; } pc = c; run = 1; }
                        }
                        if (run > 0) { net.AddArc(tail, head, run, pc); nF[idx]++; }
                        run = 0;
                        fB[idx] = net.ArcCount * 2; nB[idx] = 0;
                        for (int st = 1; st <= capB; ++st) {
                            int c = StepCost(v - st + 1, v - st, r);
                            if (run > 0 && c == pc) run++; else { if (run > 0) { net.AddArc(head, tail, run, pc); nB[idx]++; } pc = c; run = 1; }
                        }
                        if (run > 0) { net.AddArc(head, tail, run, pc); nB[idx]++; }
                    }
                }
                FlowArcs = net.ArcCount; FlowNodes = NN; FlowCapacity = H;
                long flow = net.Solve(excess, out long cost);
                FlowSearches = net.Searches; FlowSettled = net.Settled;
                if (flow < supply) continue;           // raise the capacity, solve again
                int Sum(int first, int cnt) { int t = 0; for (int i = 0; i < cnt; ++i) t += net.ArcFlow(first + 2 * i); return t; }
                for (int k = 0; k < E; ++k) {
                    if (!balanced[k]) continue;
                    var e = edges[k];
                    int dx = Sum(fF[2 * k], nF[2 * k]) - Sum(fB[2 * k], nB[2 * k]);
                    int dy = Sum(fF[2 * k + 1], nF[2 * k + 1]) - Sum(fB[2 * k + 1], nB[2 * k + 1]);
                    e.Dv = new Off(e.Dv.X + dx, e.Dv.Y + dy);
                }
                Recompute();
                long bad = 0; for (int i = 0; i < NN; ++i) bad += Math.Abs(res[i]);
                full = bad == 0;
                break;
            }
            FlowNetSeconds = sw.Elapsed.TotalSeconds;
            if (!full) return false;
            // write the edge values back to the triangles
            foreach (var e in edges) {
                tris[e.F0].D[e.S0] = e.Dv;
                if (e.F1 >= 0) tris[e.F1].D[e.S1] = -Off.Rot(e.Dv, e.M);
            }
            return true;
        }

        // ------------------------------------------------------------------------------------------ step 4: subdivision

        void Link(int a, int sa, int b, int sb, int rot)
        {
            tris[a].Nb[sa] = b; tris[a].NbS[sa] = sb; tris[a].Rot[sa] = ((rot % 4) + 4) % 4;
            tris[b].Nb[sb] = a; tris[b].NbS[sb] = sa; tris[b].Rot[sb] = (((-rot) % 4) + 4) % 4;
        }

        static Off Half(Off d) { return new Off(d.X / 2, d.Y / 2); }

        /// <summary> a binary min-heap of longs (the keys are unique per entry: the order does not depend on a tie) </summary>
        sealed class LongHeap
        {
            long[] a = new long[256]; int n;
            public int Count { get { return n; } }
            public void Push(long k)
            {
                if (n == a.Length) Array.Resize(ref a, n * 2);
                int i = n++;
                while (i > 0) { int p = (i - 1) >> 1; if (a[p] <= k) break; a[i] = a[p]; i = p; }
                a[i] = k;
            }
            public long Pop()
            {
                long top = a[0], last = a[--n];
                int i = 0;
                while (true) {
                    int c = 2 * i + 1; if (c >= n) break;
                    if (c + 1 < n && a[c + 1] < a[c]) c++;
                    if (a[c] >= last) break;
                    a[i] = a[c]; i = c;
                }
                if (n > 0) a[i] = last;
                return top;
            }
        }

        /// <summary> splits every edge with |d|inf above 1, the largest first (then the lowest triangle, then the lowest slot); false if MaxSplits is exceeded </summary>
        bool Subdivide()
        {
            for (int v = 0; v < maxV; ++v) { VP.Add(mesh.IsVertex(v) ? P[v] : Vector3d.Zero); VN.Add(mesh.IsVertex(v) ? Nrm[v] : Vector3d.Zero); }
            var heap = new LongHeap();
            long Key(int f, int sl, int inf) { return ((long)(100000 - Math.Min(inf, 99999)) << 40) | ((long)f << 2) | (long)sl; }
            void PushTri(int f) { for (int sl = 0; sl < 3; ++sl) { int inf = tris[f].D[sl].Inf; if (inf > 1) heap.Push(Key(f, sl, inf)); } }
            for (int f = 0; f < tris.Count; ++f) PushTri(f);
            Splits = 0;
            while (heap.Count > 0) {
                long key = heap.Pop();
                int sl = (int)(key & 3), f = (int)((key >> 2) & ((1L << 38) - 1)), inf = 100000 - (int)(key >> 40);
                if (Math.Min(tris[f].D[sl].Inf, 99999) != inf) continue;
                if (++Splits > MaxSplits) return false;
                int nf = tris[f].Nb[sl];
                int n0 = tris.Count; SplitEdge(f, sl);
                PushTri(f); PushTri(n0); if (nf >= 0) { PushTri(nf); PushTri(n0 + 1); }
            }
            return true;
        }

        void SplitEdge(int f, int sl)
        {
            Tri F = tris[f];
            int p = F.V[sl], q = F.V[(sl + 1) % 3], x = F.V[(sl + 2) % 3];
            int nf = F.Nb[sl], ns = F.NbS[sl], m = F.Rot[sl];
            int w = VP.Count;
            VP.Add(0.5 * (VP[p] + VP[q]));
            Vector3d nn = VN[p] + VN[q]; VN.Add(nn.Length > 0 ? nn / nn.Length : VN[p]);
            Off D = F.D[sl], Dp = Half(D), Dq = D - Dp;
            Off Dqx = F.D[(sl + 1) % 3], Dxp = F.D[(sl + 2) % 3];
            Off E = -Dp - Dxp;
            int oq = F.Nb[(sl + 1) % 3], oqs = F.NbS[(sl + 1) % 3], oqr = F.Rot[(sl + 1) % 3];
            int ox = F.Nb[(sl + 2) % 3], oxs = F.NbS[(sl + 2) % 3], oxr = F.Rot[(sl + 2) % 3];
            // tA = (p, w, x) reuses f; tB = (w, q, x) is new
            var tB = new Tri { Fa = F.Fa }; int ib = tris.Count; tris.Add(tB);
            F = tris[f];
            F.V[0] = p; F.V[1] = w; F.V[2] = x; F.D[0] = Dp; F.D[1] = E; F.D[2] = Dxp;
            tB.V[0] = w; tB.V[1] = q; tB.V[2] = x; tB.D[0] = Dq; tB.D[1] = Dqx; tB.D[2] = -E;
            // the neighbours across the outer edges
            if (oq >= 0) Link(ib, 1, oq, oqs, oqr); else tB.Nb[1] = -1;
            if (ox >= 0) Link(f, 2, ox, oxs, oxr); else F.Nb[2] = -1;
            Link(f, 1, ib, 2, 0);
            if (nf >= 0) {
                Tri N = tris[nf];
                // N has the slot ns: q -> p; its third vertex y is at slot ns + 2 -> ns (y -> q is slot ns + 2)
                int y = N.V[(ns + 2) % 3];
                Off Hqw = -Off.Rot(Dq, m), Hwp = -Off.Rot(Dp, m);        // q -> w and w -> p in the frame of N
                Off Dpy = N.D[(ns + 1) % 3], Dyq = N.D[(ns + 2) % 3];
                Off E2 = -Hqw - Dyq;                                     // w -> y
                int opn = N.Nb[(ns + 1) % 3], opns = N.NbS[(ns + 1) % 3], opr = N.Rot[(ns + 1) % 3];
                int oyn = N.Nb[(ns + 2) % 3], oyns = N.NbS[(ns + 2) % 3], oyr = N.Rot[(ns + 2) % 3];
                // tC = (q, w, y) reuses nf; tD = (w, p, y) is new
                var tD = new Tri { Fa = N.Fa }; int fd = tris.Count; tris.Add(tD);
                N = tris[nf];
                N.V[0] = q; N.V[1] = w; N.V[2] = y; N.D[0] = Hqw; N.D[1] = E2; N.D[2] = Dyq;
                tD.V[0] = w; tD.V[1] = p; tD.V[2] = y; tD.D[0] = Hwp; tD.D[1] = Dpy; tD.D[2] = -E2;
                int fc = nf;
                if (opn >= 0) Link(fd, 1, opn, opns, opr); else tD.Nb[1] = -1;
                if (oyn >= 0) Link(fc, 2, oyn, oyns, oyr); else N.Nb[2] = -1;
                Link(fc, 1, fd, 2, 0);
                // across the two halves of the old edge
                Link(f, 0, fd, 0, m);        // (p -> w) with (w -> p)
                Link(ib, 0, fc, 0, m);       // (w -> q) with (q -> w)
            }
        }

        // ------------------------------------------------------------------------------------------ steps 5-6: greedy and SAT

        static int Det(Tri t) { return Off.Det(t.D[0], t.D[1]); }

        int Inverted() { int n = 0; foreach (var t in tris) if (Det(t) < 0) n++; return n; }

        int[] cornerTri, cornerIdx;

        void BuildCorners()
        {
            int nv = VP.Count;
            cornerTri = new int[nv]; cornerIdx = new int[nv];
            for (int i = 0; i < nv; ++i) cornerTri[i] = -1;
            for (int f = 0; f < tris.Count; ++f)
                for (int c = 0; c < 3; ++c) if (cornerTri[tris[f].V[c]] < 0) { cornerTri[tris[f].V[c]] = f; cornerIdx[tris[f].V[c]] = c; }
        }

        // the fan of vertex u in counter-clockwise order (triangles and corner indices) and the cumulative frame rotation to each; false if the fan is open or has a holonomy
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

        /// <summary> moves the lattice point of a vertex by one unit offset of one of its triangles where that lowers the area of the inverted triangles around it </summary>
        void Greedy()
        {
            BuildCorners();
            var ft = new List<int>(); var fi = new List<int>(); var cum = new List<int>();
            var isCand = new bool[VP.Count];
            for (int pass = 0; pass < 100; ++pass) {
                int moves = 0;
                Array.Clear(isCand, 0, isCand.Length);
                for (int f = 0; f < tris.Count; ++f) if (Det(tris[f]) < 0) foreach (int v in tris[f].V) isCand[v] = true;
                for (int u = 0; u < isCand.Length; ++u) {         // the candidates in vertex order
                    if (!isCand[u] || !Fan(u, ft, fi, cum)) continue;
                    int n = ft.Count;
                    long before = 0;
                    for (int k = 0; k < n; ++k) { int d = Det(tris[ft[k]]); if (d < 0) before -= d; }
                    if (before == 0) continue;
                    long bestAfter = before; int bestJ = -1;
                    for (int j = 0; j < n; ++j) {
                        Off Vj = tris[ft[j]].D[fi[j]];
                        long after = 0; bool ok = true;
                        for (int k = 0; k < n && ok; ++k) {
                            Off Vk = Off.Rot(Vj, cum[k] - cum[j]);
                            Tri t = tris[ft[k]]; int i = fi[k];
                            Off o = t.D[i] - Vk, inn = t.D[(i + 2) % 3] + Vk;
                            if (o.Inf > 1 || inn.Inf > 1) { ok = false; break; }
                            Off[] nd = { t.D[0], t.D[1], t.D[2] }; nd[i] = o; nd[(i + 2) % 3] = inn;
                            int d = Off.Det(nd[0], nd[1]); if (d < 0) after -= d;
                        }
                        if (ok && after < bestAfter) { bestAfter = after; bestJ = j; }
                    }
                    if (bestJ < 0) continue;
                    Off Vb = tris[ft[bestJ]].D[fi[bestJ]];
                    for (int k = 0; k < n; ++k) {
                        Off Vk = Off.Rot(Vb, cum[k] - cum[bestJ]);
                        Tri t = tris[ft[k]]; int i = fi[k];
                        t.D[i] = t.D[i] - Vk; t.D[(i + 2) % 3] = t.D[(i + 2) % 3] + Vk;
                    }
                    moves++;
                }
                GreedyMoves += moves;
                if (moves == 0) break;
            }
        }

        static readonly Off[] Dom = BuildDom();
        static Off[] BuildDom() { var l = new List<Off>(); for (int y = -1; y <= 1; ++y) for (int x = -1; x <= 1; ++x) l.Add(new Off(x, y)); return l.ToArray(); }
        static int DomIdx(Off d) { return (d.Y + 1) * 3 + (d.X + 1); }

        int[] regionMark, regionDist, slotMark, slotVar; int regionStamp;

        // one SAT attempt: the offsets of the edges between two triangles of the region (the triangles within `ring` steps of the
        // seed) are the variables (nine values each, exactly one), every triangle of the region must have sum 0 and det >= 0
        bool TrySat(int seed, int ring)
        {
            int nt = tris.Count;
            if (regionMark == null) { regionMark = new int[nt]; regionDist = new int[nt]; slotMark = new int[3 * nt]; slotVar = new int[3 * nt]; }
            int stamp = ++regionStamp;
            var region = new List<int> { seed };               // in breadth-first order
            regionMark[seed] = stamp; regionDist[seed] = 0;
            for (int qi = 0; qi < region.Count; ++qi) {
                int f = region[qi]; if (regionDist[f] >= ring) continue;
                for (int sl = 0; sl < 3; ++sl) {
                    int nb = tris[f].Nb[sl];
                    if (nb >= 0 && regionMark[nb] != stamp) { regionMark[nb] = stamp; regionDist[nb] = regionDist[f] + 1; region.Add(nb); }
                }
            }
            // the mutable edges: both triangles in the region, the variable is the master in the lower triangle id
            var sorted = region.ToArray(); Array.Sort(sorted);
            var masters = new List<(int f, int s)>();
            foreach (int f in sorted)
                for (int sl = 0; sl < 3; ++sl) {
                    int nb = tris[f].Nb[sl];
                    if (nb >= 0 && regionMark[nb] == stamp && nb > f) {
                        int id = masters.Count; masters.Add((f, sl));
                        slotMark[3 * f + sl] = stamp; slotVar[3 * f + sl] = id;
                        int ns = tris[f].NbS[sl]; slotMark[3 * nb + ns] = stamp; slotVar[3 * nb + ns] = -1 - id;
                    }
                }
            int M = masters.Count; if (M == 0) return false;
            long remaining = Math.Max(1, SatTotalBudget - SatDecisions);
            var sat = new GroupSatSolver(9 * M) { DecisionBudget = Math.Min(SatAttemptBudget, remaining) };
            var groups = new int[M][];
            for (int e = 0; e < M; ++e) {
                var (f, sl) = masters[e];
                int cur = DomIdx(tris[f].D[sl]);
                var order = new List<int>();
                for (int k = 0; k < 9; ++k) if (k != cur) order.Add(k);
                Off curV = Dom[cur];
                // the current value first, then the others by distance to it (then by index)
                order.Sort((a, b) => { int c = (Dom[a] - curV).L1.CompareTo((Dom[b] - curV).L1); return c != 0 ? c : a.CompareTo(b); });
                groups[e] = new int[9]; groups[e][0] = 9 * e + cur + 1;
                for (int k = 0; k < 8; ++k) groups[e][k + 1] = 9 * e + order[k] + 1;
                var all = new int[9]; for (int k = 0; k < 9; ++k) all[k] = 9 * e + k + 1;
                sat.AddClause(all);
                for (int a = 0; a < 9; ++a) for (int b = a + 1; b < 9; ++b) sat.AddClause(-(9 * e + a + 1), -(9 * e + b + 1));
            }
            // the triangle clauses, in the implication encoding: the last mutable slot is determined by the others (sum = 0), so
            // (not a, not b, c) replaces the 9^k forbidden triples
            var varOf = new int[3]; var rotOf = new int[3]; var fixedVal = new Off[3]; var mutSlots = new int[3];
            foreach (int f in region) {
                Tri t = tris[f];
                int mk = 0;
                for (int sl = 0; sl < 3; ++sl) {
                    varOf[sl] = -1; fixedVal[sl] = t.D[sl];
                    if (slotMark[3 * f + sl] == stamp) {
                        int id = slotVar[3 * f + sl];
                        if (id >= 0) { varOf[sl] = id; rotOf[sl] = 0; }
                        else { varOf[sl] = -1 - id; var mm = masters[-1 - id]; rotOf[sl] = mm.f == f ? 0 : (tris[mm.f].Rot[mm.s] + 2) % 4; }
                        mutSlots[mk++] = sl;
                    }
                }
                if (mk == 0) { if (!(fixedVal[0] + fixedVal[1] + fixedVal[2]).IsZero || Off.Det(fixedVal[0], fixedVal[1]) < 0) return false; continue; }
                int last = mutSlots[mk - 1]; int free = mk - 1; int combos = 1; for (int i = 0; i < free; ++i) combos *= 9;
                var picks = new int[free];
                for (int c = 0; c < combos; ++c) {
                    int cc = c; var val = new Off[] { fixedVal[0], fixedVal[1], fixedVal[2] };
                    for (int i = 0; i < free; ++i) { int k = cc % 9; cc /= 9; picks[i] = k; int sl = mutSlots[i]; val[sl] = Off.Rot(Dom[k], rotOf[sl]); }
                    Off need = -(val[0] + val[1] + val[2] - val[last]);       // val[last] is still the current value of that slot
                    Off masterVal = Off.Rot(need, -rotOf[last]);
                    bool okv = masterVal.Inf <= 1;
                    if (okv) { val[last] = need; if (Off.Det(val[0], val[1]) < 0) okv = false; }
                    var lits = new int[free + (okv ? 1 : 0)];
                    for (int i = 0; i < free; ++i) lits[i] = -(9 * varOf[mutSlots[i]] + picks[i] + 1);
                    if (okv) lits[free] = 9 * varOf[last] + DomIdx(masterVal) + 1;
                    if (lits.Length == 0) return false;
                    sat.AddClause(lits);
                }
            }
            SatAttempts++;
            int rs = sat.Solve(groups, out bool[] model);
            SatDecisions += sat.Decisions;
            if (rs != GroupSatSolver.Satisfiable) return false;
            for (int e = 0; e < M; ++e) {
                var (f, sl) = masters[e];
                Off nv = new Off(0, 0);
                for (int k = 0; k < 9; ++k) if (model[9 * e + k]) nv = Dom[k];
                tris[f].D[sl] = nv;
                int nb = tris[f].Nb[sl];
                tris[nb].D[tris[f].NbS[sl]] = -Off.Rot(nv, tris[f].Rot[sl]);
            }
            return true;
        }

        // the first inverted triangle (not failed before) is the seed of a region of growing size; the decisions of all attempts share one budget
        void RepairSat()
        {
            var failed = new bool[tris.Count];
            int scan = 0;       // an inverted triangle only becomes fixed or stays (the triangles outside a region never change): the scan never goes back
            for (int round = 0; round < 10000; ++round) {
                if (SatAttempts >= SatMaxAttempts) { SatBudgetExhausted = true; break; }
                int seed = -1;
                for (; scan < tris.Count; ++scan) if (Det(tris[scan]) < 0 && !failed[scan]) { seed = scan; break; }
                if (seed < 0) break;
                bool ok = false;
                for (int ring = 1; ring <= SatMaxRing && !ok && SatDecisions < SatTotalBudget; ++ring) ok = TrySat(seed, ring);
                if (ok) SatSolved++; else { SatFailed++; failed[seed] = true; }
                if (SatDecisions >= SatTotalBudget) { SatBudgetExhausted = true; break; }
                if (round == 9999) SatBudgetExhausted = true;      // the round limit stops the repair with folds left
            }
        }

        // every triangle of the repaired map closes (the three offsets sum to 0) and the two sides of every edge agree (D_nb = -R^rot D)
        bool MapCloses()
        {
            for (int f = 0; f < tris.Count; ++f) {
                Tri t = tris[f];
                if (!(t.D[0] + t.D[1] + t.D[2]).IsZero) return false;
                for (int sl = 0; sl < 3; ++sl) {
                    int nb = t.Nb[sl]; if (nb < 0) continue;
                    if (tris[nb].D[t.NbS[sl]] != -Off.Rot(t.D[sl], t.Rot[sl])) return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------------------------------ step 7: positions

        /// <summary>
        /// one 3D point per cluster of the d = 0 edges: least squares of P_b - P_a = s (dx q + dy t) over the other edges (the frame of
        /// the first vertex of the triangle), with a weak anchor to the mean of the cluster's vertices
        /// </summary>
        Vector3d[] SolvePositions()
        {
            const double anchor = 0.5;
            int nv = VP.Count;
            var ufc = new int[nv]; for (int i = 0; i < nv; ++i) ufc[i] = i;
            foreach (var t in tris) for (int sl = 0; sl < 3; ++sl) if (t.D[sl].IsZero) { int a = FindRoot(ufc, t.V[sl]), b = FindRoot(ufc, t.V[(sl + 1) % 3]); if (a != b) ufc[Math.Max(a, b)] = Math.Min(a, b); }
            var cid = new int[nv]; for (int i = 0; i < nv; ++i) cid[i] = -1;
            int nc = 0; var mean = new List<Vector3d>(); var cnt = new List<int>();
            for (int i = 0; i < nv; ++i) {
                if (i < maxV && !mesh.IsVertex(i)) continue;
                int r = FindRoot(ufc, i);
                if (cid[r] < 0) { cid[r] = nc++; mean.Add(Vector3d.Zero); cnt.Add(0); }
                mean[cid[r]] += VP[i]; cnt[cid[r]]++;
            }
            for (int c = 0; c < nc; ++c) mean[c] /= cnt[c];
            var ch = new SparseCholesky(nc, 1); var rx = new double[nc]; var ry = new double[nc]; var rz = new double[nc];
            for (int c = 0; c < nc; ++c) { ch.Add(c, c, anchor); rx[c] += anchor * mean[c].x; ry[c] += anchor * mean[c].y; rz[c] += anchor * mean[c].z; }
            foreach (var t in tris)
                for (int sl = 0; sl < 3; ++sl) {
                    int a = cid[FindRoot(ufc, t.V[sl])], b = cid[FindRoot(ufc, t.V[(sl + 1) % 3])]; if (a == b) continue;
                    Vector3d tg = s * (t.D[sl].X * Q[t.Fa] + t.D[sl].Y * T[t.Fa]);
                    ch.Add(a, a, 1); ch.Add(b, b, 1); if (a > b) ch.Add(a, b, -1); else ch.Add(b, a, -1);
                    rx[b] += tg.x; ry[b] += tg.y; rz[b] += tg.z; rx[a] -= tg.x; ry[a] -= tg.y; rz[a] -= tg.z;
                }
            var pos = new Vector3d[nv];
            if (ch.Factorize()) {
                var X = new double[nc]; var Y = new double[nc]; var Z = new double[nc];
                ch.Solve(rx, X); ch.Solve(ry, Y); ch.Solve(rz, Z);
                for (int i = 0; i < nv; ++i) { if (i < maxV && !mesh.IsVertex(i)) continue; int c = cid[FindRoot(ufc, i)]; pos[i] = new Vector3d(X[c], Y[c], Z[c]); }
            } else return null;       // not expected (the matrix is positive definite)
            return pos;
        }

        // ------------------------------------------------------------------------------------------ step 8: extraction

        void Extract(Vector3d[] pos)
        {
            int nv = VP.Count;
            var uf = new int[nv]; for (int i = 0; i < nv; ++i) uf[i] = i;
            foreach (var t in tris)
                for (int sl = 0; sl < 3; ++sl) if (t.D[sl].IsZero) { int a = FindRoot(uf, t.V[sl]), b = FindRoot(uf, t.V[(sl + 1) % 3]); if (a != b) uf[Math.Max(a, b)] = Math.Min(a, b); }
            var sum = new Vector3d[nv]; var cnt = new int[nv]; var nsum = new Vector3d[nv];
            for (int i = 0; i < nv; ++i) {
                if (i < maxV && !mesh.IsVertex(i)) continue;
                int r = FindRoot(uf, i); sum[r] += pos[i]; cnt[r]++; nsum[r] += VN[i];
            }
            // the non-degenerate triangles, grouped by the pair of clusters of the hypotenuse (in the order of first appearance)
            var hyp = new Dictionary<(int, int), List<(int a, int b, int c)>>();
            var hypKeys = new List<(int, int)>();
            var leftover = new List<int[]>();
            foreach (var t in tris) {
                int[] cl = { FindRoot(uf, t.V[0]), FindRoot(uf, t.V[1]), FindRoot(uf, t.V[2]) };
                if (cl[0] == cl[1] || cl[1] == cl[2] || cl[0] == cl[2]) continue;
                int h = -1;
                for (int sl = 0; sl < 3; ++sl) if (t.D[sl].L1 == 2 && t.D[sl].Inf == 1) h = sl;
                int ok = 0; for (int sl = 0; sl < 3; ++sl) if (t.D[sl].L1 == 1) ok++;
                if (h < 0 || ok != 2) { leftover.Add(cl); BadTriangles++; continue; }
                int A = cl[h], B = cl[(h + 1) % 3], C = cl[(h + 2) % 3];
                var key = A < B ? (A, B) : (B, A);
                if (!hyp.TryGetValue(key, out var lst)) { hyp[key] = lst = new List<(int, int, int)>(); hypKeys.Add(key); }
                lst.Add((A, B, C));
            }
            var outMesh = new DPolyMesh();
            var vmap = new Dictionary<int, int>();
            var tree = new DMeshAABBTree3(mesh, true);
            int Vid(int c)
            {
                if (!vmap.TryGetValue(c, out int id)) {
                    Vector3d p = sum[c] / cnt[c];
                    int nt = tree.FindNearestTriangle(p);
                    if (nt >= 0) p = MeshQueries.TriangleDistance(mesh, nt, p).TriangleClosest;     // projection onto the input
                    id = outMesh.AppendVertex(p.x, p.y, p.z); vmap[c] = id;
                }
                return id;
            }
            foreach (var key in hypKeys) {
                var l = new List<(int a, int b, int c)>(hyp[key]);
                if (PairQuad(l, ref Cancelled, out int A, out int B, out int C1, out int C2)) {
                    outMesh.AppendFace(new List<int> { Vid(A), Vid(C2), Vid(B), Vid(C1) }, 0);
                    // geometric check: the normal of the quad against the sum of the normals of its clusters
                    Vector3d pa = sum[A] / cnt[A], pc2 = sum[C2] / cnt[C2], pb = sum[B] / cnt[B], pc1 = sum[C1] / cnt[C1];
                    Vector3d fn = (pb - pa).Cross(pc1 - pc2); Vector3d sn = nsum[A] + nsum[B] + nsum[C1] + nsum[C2];
                    Quads++; if (fn.Dot(sn) <= 0) FlippedQuads++;
                } else {
                    foreach (var tr in l) leftover.Add(new[] { tr.a, tr.b, tr.c });
                    UnpairedTriangles += l.Count;
                }
            }
            foreach (var tr in leftover) outMesh.AppendFace(new List<int> { Vid(tr[0]), Vid(tr[1]), Vid(tr[2]) }, 0);
            // the second gate, on the extracted mesh
            Status = CheckExtracted(outMesh, BadTriangles, UnpairedTriangles, FlippedQuads);
            if (Status == PureQuadStatus.Success) Result = outMesh;
        }

        /// <summary>
        /// the triangles (A, B, C) over one hypotenuse {A, B} of the clusters: pairs of triangles with the same three clusters, one on each
        /// side (a 2-gon), cancel (the list is changed, cancelled counts the pairs); then exactly two triangles on the two sides with
        /// different third clusters make the quad (A, C2, B, C1). False (the list holds the triangles left, none of them is a quad) otherwise.
        /// </summary>
        internal static bool PairQuad(List<(int a, int b, int c)> l, ref int cancelled, out int A, out int B, out int C1, out int C2)
        {
            for (int i = 0; i < l.Count; ++i)
                for (int j = i + 1; j < l.Count; ++j)
                    if (l[i].a == l[j].b && l[i].b == l[j].a && l[i].c == l[j].c) { l.RemoveAt(j); l.RemoveAt(i); cancelled++; i = -1; break; }
            A = B = C1 = C2 = -1;
            if (l.Count == 2 && l[0].a == l[1].b && l[0].b == l[1].a && l[0].c != l[1].c) { A = l[0].a; B = l[0].b; C1 = l[0].c; C2 = l[1].c; return true; }
            return false;
        }

        /// <summary>
        /// the checks of the extracted mesh, in order: NotManifold (no face, an unpaired or malformed triangle, a face that is not a quad, a
        /// non-manifold or inconsistently oriented edge or vertex), then FlippedFaces (a quad facing away from the surface); else Success
        /// </summary>
        internal static PureQuadStatus CheckExtracted(DPolyMesh mesh, int badTriangles, int unpairedTriangles, int flippedQuads)
        {
            if (mesh == null || mesh.FaceCount == 0 || badTriangles > 0 || unpairedTriangles > 0) return PureQuadStatus.NotManifold;
            PolyMeshMetrics m = PolyMeshMetrics.Compute(mesh);
            if (!m.IsManifold || m.QuadFraction < 1 || m.BoundaryVertices > 0) return PureQuadStatus.NotManifold;   // closed meshes only
            return flippedQuads > 0 ? PureQuadStatus.FlippedFaces : PureQuadStatus.Success;
        }
    }
}
