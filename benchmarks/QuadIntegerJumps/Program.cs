// QuadIntegerJumps: prototype of option C of docs/research/QUAD_SHEAF_OBSTRUCTION_2026-11.md ("integer jumps up front") for stage 3B
// (QuadRemesher.PureQuads, QuadFlowRepair). Research spike, no library change: the library is read and driven by reflection.
//
// The library fixes the offsets of the edges whose cycle has a holonomy (the "frozen" edges) before its min-cost flow. With the rows
// of the rotated equations as nodes, a frozen edge is a column with two entries of the same sign, so the problem with all edges free
// is a min-cost flow in a bi-directed graph. Its linear relaxation is an ordinary min-cost flow on the doubled network (a node and its
// mirror, an arc and its mirror), solved here with the library's MinCostFlow and the library's step costs; the solution is
// half-integral. The prototype rounds the values of the frozen edges only, writes them into the repair, and then runs the library's
// own steps (greedy balancing, flow, subdivision, greedy moves, SAT, positions, extraction). The result is judged with the independent
// checks of benchmarks/QuadFlow3BGate, never with the solver's cost.
//
//   dotnet run -c Release --project benchmarks/QuadIntegerJumps -- [only=<regex>] [var=<regex on "scale/ratio">] [adapt=0|1|2]
//                                                                    [align=<x>] [mingain=<x>] [round=real|keep|down]
// Per run: the stage 2 mesh, the library's pure-quad attempt, a replay of the library's steps through reflection without the jumps
// (must reproduce the library's status and mesh: "replay ok"), and the replay with the jumps.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
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

    // the inputs of QuadFlow3BGate (same construction, same seeds)
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

    static double Haus(DPolyMesh pm, DMesh3 input, double s)
    {
        if (pm == null || pm.FaceCount == 0) return double.NaN;
        DMesh3 tri = pm.ToDMesh3(out int[] map, new List<int>());
        if (tri.TriangleCount == 0) return double.NaN;
        var hd = new MeshHausdorffDistance(tri, input) { Symmetric = false, RelativeTolerance = 1e-3 };
        hd.Compute(); return hd.UpperBound / s;
    }

    static bool SameMesh(DPolyMesh a, DPolyMesh b)
    {
        if (a == null || b == null) return a == b;
        if (a.VertexCount != b.VertexCount || a.FaceCount != b.FaceCount) return false;
        if (!a.FaceOffsets.ToArray().SequenceEqual(b.FaceOffsets.ToArray()) || !a.FaceIndices.ToArray().SequenceEqual(b.FaceIndices.ToArray())) return false;
        for (int v = 0; v < a.VertexCount; ++v) if (a.GetVertex(v) != b.GetVertex(v)) return false;
        return true;
    }

    // the independent check of the gate: the normal of every quad (cross of the diagonals) against the input triangle nearest to its centre
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

    /// <summary> the gate's checks on a mesh that claims Success: manifold, quads only, finite, valid, no quad facing away </summary>
    static bool GateClean(DPolyMesh r, PolyMeshMetrics m, DMesh3 input, DMeshAABBTree3 tree)
    {
        if (r == null) return false;
        int away = FacingAway(r, input, tree);
        bool finite = Enumerable.Range(0, r.VertexCount).All(v => r.GetVertex(v).IsFinite);
        return m.IsManifold && m.QuadFraction >= 1 && away == 0 && finite && r.Validate().IsValid;
    }

    // ---------------------------------------------------------------------------------------------------- the library by reflection

    static readonly Assembly Lib = typeof(QuadRemesher).Assembly;
    static readonly Type RepT = Lib.GetType("gs.QuadFlowRepair", true);
    static readonly Type FlowT = Lib.GetType("gs.MinCostFlow", true);
    delegate long SolveFn(int[] excess, out long cost);

    /// <summary> the library's MinCostFlow (internal), through delegates </summary>
    sealed class Flow
    {
        readonly Func<int, int, int, int, int> add; readonly Func<int, int> flow; readonly SolveFn solve; readonly object o;
        public Flow(int n, int arcHint)
        {
            o = Activator.CreateInstance(FlowT, Any, null, new object[] { n, arcHint }, null);
            add = (Func<int, int, int, int, int>)Delegate.CreateDelegate(typeof(Func<int, int, int, int, int>), o, FlowT.GetMethod("AddArc", Any));
            flow = (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), o, FlowT.GetMethod("ArcFlow", Any));
            solve = (SolveFn)Delegate.CreateDelegate(typeof(SolveFn), o, FlowT.GetMethod("Solve", Any));
        }
        public int AddArc(int u, int v, int c, int w) { return add(u, v, c, w); }
        public int ArcFlow(int a) { return flow(a); }
        public long Solve(int[] excess, out long cost) { return solve(excess, out cost); }
    }

    /// <summary> one QuadFlowRepair, driven step by step (the body of QuadFlowRepair.Run) </summary>
    sealed class Rep
    {
        public readonly object O;
        public IList Tris; Type offT; FieldInfo fD, fNb, fNbS, fRot, fDr, fDg, fX, fY;
        public Func<int, int, double, double, int> StepCost;
        public PureQuadStatus Status = PureQuadStatus.NotRequested; public DPolyMesh Result;
        public double FlowSeconds, RestSeconds, JumpSeconds;
        public int InvertedAfterFlow, Inverted, MaxInfAfterFlow, BaseTriangles, MovedByBalancing; public long AreaAfterFlow2, Area2; public long FlowCost = -1;
        public Jumps J;

        public Rep(DMesh3 mesh, CrossField cf, PositionField pf, double s, bool signPenalty)
        {
            O = Activator.CreateInstance(RepT, Any, null, new object[] { mesh, cf, pf, s }, null);
            RepT.GetField("SignPenalty", Any).SetValue(O, signPenalty);
            StepCost = (Func<int, int, double, double, int>)Delegate.CreateDelegate(typeof(Func<int, int, double, double, int>), O, RepT.GetMethod("StepCost", Any));
        }
        object Call(string name, params object[] args) { return RepT.GetMethod(name, Any).Invoke(O, args); }
        public T Get<T>(string name) { return (T)RepT.GetField(name, Any).GetValue(O); }

        void Bind()
        {
            Tris = (IList)RepT.GetField("tris", Any).GetValue(O);
            Type triT = Tris[0].GetType();
            fD = triT.GetField("D", Any); fNb = triT.GetField("Nb", Any); fNbS = triT.GetField("NbS", Any); fRot = triT.GetField("Rot", Any);
            fDr = triT.GetField("Dr", Any); fDg = triT.GetField("Dg", Any);
            offT = fD.FieldType.GetElementType(); fX = offT.GetField("X", Any); fY = offT.GetField("Y", Any);
        }
        public int[] Nb(int f) { return (int[])fNb.GetValue(Tris[f]); }
        public int[] NbS(int f) { return (int[])fNbS.GetValue(Tris[f]); }
        public int[] Rot(int f) { return (int[])fRot.GetValue(Tris[f]); }
        public (double, double)[] Dr(int f) { return ((double, double)[])fDr.GetValue(Tris[f]); }
        public (double, double)[] Dg(int f) { return ((double, double)[])fDg.GetValue(Tris[f]); }
        public (int, int) D(int f, int sl) { object o = ((Array)fD.GetValue(Tris[f])).GetValue(sl); return ((int)fX.GetValue(o), (int)fY.GetValue(o)); }
        public void SetD(int f, int sl, int x, int y) { ((Array)fD.GetValue(Tris[f])).SetValue(Activator.CreateInstance(offT, Any, null, new object[] { x, y }, null), sl); }

        /// <summary> the steps of QuadFlowRepair.Run in its order; jumps: the hook between the triangles and the flow </summary>
        public PureQuadStatus Run(string round)
        {
            var sw = Stopwatch.StartNew();
            Call("BuildOffsets");
            if (Get<bool>("setupFailed")) return Status = PureQuadStatus.FlowInfeasible;
            Call("BuildTriangles");
            Bind(); BaseTriangles = Tris.Count;
            for (int f = 0; f < Tris.Count; ++f) { int[] nb = Nb(f); if (nb[0] < 0 || nb[1] < 0 || nb[2] < 0) return Status = PureQuadStatus.FlowInfeasible; }
            double t0 = sw.Elapsed.TotalSeconds;
            if (round != null) { J = new Jumps(this); J.Solve(round); }
            else { J = new Jumps(this); }       // the edges only, for the cost of the library's own flow
            JumpSeconds = sw.Elapsed.TotalSeconds - t0;
            bool feasible = (bool)Call("SolveFlow");
            FlowSeconds = sw.Elapsed.TotalSeconds - JumpSeconds;
            if (round == null) JumpSeconds = 0;
            if (!feasible) { Inverted = (int)Call("Inverted"); return Status = PureQuadStatus.FlowInfeasible; }
            FlowCost = J.CostOfCurrent(); MovedByBalancing = J.MovedAfterFlow();
            MapStats(out InvertedAfterFlow, out AreaAfterFlow2, out MaxInfAfterFlow);
            sw.Restart();
            try {
                bool splitOk = (bool)Call("Subdivide");
                Call("Greedy");
                if (splitOk) Call("RepairSat");
                Inverted = (int)Call("Inverted");
                MapStats(out int inv2, out Area2, out int mi2);
                if (!splitOk) return Status = PureQuadStatus.BudgetExceeded;
                if (Inverted > 0) return Status = Get<bool>("SatBudgetExhausted") ? PureQuadStatus.BudgetExceeded : PureQuadStatus.OrientationFolds;
                if (!(bool)Call("MapCloses")) return Status = PureQuadStatus.FlowInfeasible;
                object pos = Call("SolvePositions");
                if (pos == null) return Status = PureQuadStatus.FlowInfeasible;
                Call("Extract", pos);
                Status = Get<PureQuadStatus>("Status"); Result = Get<DPolyMesh>("Result");
                return Status;
            } finally { RestSeconds = sw.Elapsed.TotalSeconds; }
        }

        void MapStats(out int inverted, out long area2, out int maxInf)
        {
            int[] o = (int[])Call("Offsets");
            inverted = 0; area2 = 0; maxInf = 0;
            for (int f = 0; 6 * f < o.Length; ++f) {
                long det = (long)o[6 * f] * o[6 * f + 3] - (long)o[6 * f + 1] * o[6 * f + 2];
                area2 += det; if (det < 0) inverted++;
                for (int i = 0; i < 6; ++i) maxInf = Math.Max(maxInf, Math.Abs(o[6 * f + i]));
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------- the jumps

    /// <summary>
    /// The edges and the rotated equations exactly as QuadFlowRepair.SolveFlow builds them (same edge order, same breadth-first rotation,
    /// so the same edges are frozen), the relaxation with all edges free on the doubled network, and the rounded values of the frozen
    /// edges written back to the triangles.
    /// </summary>
    sealed class Jumps
    {
        readonly Rep rep; readonly int F, E;
        readonly int[] f0, s0, f1, s1, mm; readonly int[] vx, vy; readonly double[] rr, rg;      // per edge; rr, rg: 2 per edge
        readonly int[] ex; readonly bool[] balanced;
        public int Frozen, FrozenChanged, FrozenComponentsChanged, FrozenHalf, AllHalf, Clamped, MaxJump, Capacity; public long RelaxedCost2 = -1, Supply;
        public bool Full;

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
        static (int, int) RotI((int X, int Y) v, int k) { k = ((k % 4) + 4) % 4; for (int i = 0; i < k; ++i) v = (-v.Y, v.X); return v; }

        public Jumps(Rep rep)
        {
            this.rep = rep; F = rep.Tris.Count;
            var lf0 = new List<int>(); var ls0 = new List<int>(); var lf1 = new List<int>(); var ls1 = new List<int>(); var lm = new List<int>();
            var lvx = new List<int>(); var lvy = new List<int>(); var lr = new List<double>(); var lg = new List<double>();
            for (int f = 0; f < F; ++f) {
                int[] nb = rep.Nb(f), nbs = rep.NbS(f), rot = rep.Rot(f); var dr = rep.Dr(f); var dg = rep.Dg(f);
                for (int sl = 0; sl < 3; ++sl) {
                    if (nb[sl] < f) continue;      // closed meshes only (checked by the caller): every edge once, from its lower triangle
                    var (x, y) = rep.D(f, sl);
                    lf0.Add(f); ls0.Add(sl); lf1.Add(nb[sl]); ls1.Add(nbs[sl]); lm.Add(rot[sl]);
                    lvx.Add(Math.Max(-2, Math.Min(2, x))); lvy.Add(Math.Max(-2, Math.Min(2, y)));      // the clamp of SolveFlow
                    lr.Add(dr[sl].Item1); lr.Add(dr[sl].Item2); lg.Add(dg[sl].Item1); lg.Add(dg[sl].Item2);
                }
            }
            f0 = lf0.ToArray(); s0 = ls0.ToArray(); f1 = lf1.ToArray(); s1 = ls1.ToArray(); mm = lm.ToArray(); vx = lvx.ToArray(); vy = lvy.ToArray();
            rr = lr.ToArray(); rg = lg.ToArray(); E = f0.Length;
            // the rotation of the equation of every triangle (breadth-first, as in SolveFlow)
            var rho = new int[F]; var vis = new bool[F]; var adjStart = new int[F + 1];
            for (int k = 0; k < E; ++k) { adjStart[f0[k] + 1]++; adjStart[f1[k] + 1]++; }
            for (int f = 0; f < F; ++f) adjStart[f + 1] += adjStart[f];
            var adjList = new int[adjStart[F]]; var fill = new int[F];
            for (int k = 0; k < E; ++k) { adjList[adjStart[f0[k]] + fill[f0[k]]++] = k; adjList[adjStart[f1[k]] + fill[f1[k]]++] = k; }
            var queue = new int[F];
            for (int root = 0; root < F; ++root) {
                if (vis[root]) continue;
                int qh = 0, qt = 0; vis[root] = true; queue[qt++] = root;
                while (qh < qt) {
                    int f = queue[qh++];
                    for (int a = adjStart[f]; a < adjStart[f + 1]; ++a) {
                        int k = adjList[a]; int c = f0[k] == f ? f1[k] : f0[k];
                        if (vis[c]) continue;
                        vis[c] = true;
                        rho[c] = f0[k] == f ? ((rho[f] - mm[k]) % 4 + 4) % 4 : ((rho[f] + mm[k]) % 4 + 4) % 4;
                        queue[qt++] = c;
                    }
                }
            }
            ex = new int[F * 3]; balanced = new bool[E];
            for (int k = 0; k < E; ++k) { ex[f0[k] * 3 + s0[k]] = rho[f0[k]] % 4; ex[f1[k] * 3 + s1[k]] = (mm[k] + 2 + rho[f1[k]]) % 4; }
            for (int k = 0; k < E; ++k) { balanced[k] = (((ex[f0[k] * 3 + s0[k]] - ex[f1[k] * 3 + s1[k]] - 2) % 4) + 4) % 4 == 0; if (!balanced[k]) Frozen++; }
        }

        /// <summary> the cost of the offsets the triangles hold now, in the library's step costs from the clamped start values (all edges) </summary>
        public long CostOfCurrent()
        {
            long c = 0;
            for (int k = 0; k < E; ++k) {
                var (x, y) = rep.D(f0[k], s0[k]);
                c += PathCost(vx[k], x, rr[2 * k], rg[2 * k]) + PathCost(vy[k], y, rr[2 * k + 1], rg[2 * k + 1]);
            }
            return c;
        }
        long PathCost(int from, int to, double r, double g)
        {
            long c = 0; int d = to > from ? 1 : -1;
            for (int a = from; a != to; a += d) c += rep.StepCost(a, a + d, r, g);
            return c;
        }

        public void Solve(string round)
        {
            int NN = 2 * F;
            var res = new long[NN];
            for (int k = 0; k < E; ++k) {
                var a = RotI((vx[k], vy[k]), ex[f0[k] * 3 + s0[k]]); res[2 * f0[k]] += a.Item1; res[2 * f0[k] + 1] += a.Item2;
                var b = RotI((vx[k], vy[k]), ex[f1[k] * 3 + s1[k]]); res[2 * f1[k]] += b.Item1; res[2 * f1[k] + 1] += b.Item2;
            }
            int[] x2 = null;
            for (int H = 2; H <= rep.Get<int>("MaxFlowCapacity") && !Full; H += 2) {
                // node n and its mirror n + NN; a unit of flow u -> v lowers the excess of u and raises that of v
                var net = new Flow(2 * NN, E * 12);
                var excess = new int[2 * NN]; Supply = 0;
                for (int i = 0; i < NN; ++i) { excess[i] = (int)res[i]; excess[i + NN] = -(int)res[i]; Supply += Math.Abs(res[i]); }
                var first = new int[E * 2 * 4]; var count = new int[E * 2 * 4];
                for (int k = 0; k < E; ++k)
                    for (int j = 0; j < 2; ++j) {
                        RowOf(ex[f0[k] * 3 + s0[k]], j, out int ra, out int sa); RowOf(ex[f1[k] * 3 + s1[k]], j, out int rb, out int sb);
                        int a = 2 * f0[k] + ra, b = 2 * f1[k] + rb;
                        int aP = sa > 0 ? a : a + NN, aM = sa > 0 ? a + NN : a, bP = sb > 0 ? b : b + NN, bM = sb > 0 ? b + NN : b;
                        // a step of +1 adds sa to the residual of a and sb to that of b: the arc bM -> aP and its mirror aM -> bP
                        int v = j == 0 ? vx[k] : vy[k]; double r = rr[2 * k + j], g = rg[2 * k + j];
                        int capF = Math.Max(0, H - v), capB = Math.Max(0, H + v);
                        for (int copy = 0; copy < 2; ++copy) {
                            int tail = copy == 0 ? bM : aM, head = copy == 0 ? aP : bP;
                            int idx = (2 * k + j) * 4 + copy * 2;
                            int run = 0, pc = 0;
                            first[idx] = -1; count[idx] = 0;
                            for (int st = 1; st <= capF; ++st) {
                                int c = rep.StepCost(v + st - 1, v + st, r, g);
                                if (run > 0 && c == pc) run++; else { if (run > 0) { int id = net.AddArc(tail, head, run, pc); if (count[idx]++ == 0) first[idx] = id; } pc = c; run = 1; }
                            }
                            if (run > 0) { int id = net.AddArc(tail, head, run, pc); if (count[idx]++ == 0) first[idx] = id; }
                            run = 0; first[idx + 1] = -1; count[idx + 1] = 0;
                            for (int st = 1; st <= capB; ++st) {
                                int c = rep.StepCost(v - st + 1, v - st, r, g);
                                if (run > 0 && c == pc) run++; else { if (run > 0) { int id = net.AddArc(head, tail, run, pc); if (count[idx + 1]++ == 0) first[idx + 1] = id; } pc = c; run = 1; }
                            }
                            if (run > 0) { int id = net.AddArc(head, tail, run, pc); if (count[idx + 1]++ == 0) first[idx + 1] = id; }
                        }
                    }
                long flow = net.Solve(excess, out long cost);
                Capacity = H;
                if (flow < Supply) continue;
                Full = true; RelaxedCost2 = cost;
                x2 = new int[E * 2];
                for (int k = 0; k < E; ++k)
                    for (int j = 0; j < 2; ++j) {
                        int t = 0;
                        for (int copy = 0; copy < 2; ++copy)
                            for (int dir = 0; dir < 2; ++dir) {
                                int idx = (2 * k + j) * 4 + copy * 2 + dir;
                                for (int i = 0; i < count[idx]; ++i) t += (dir == 0 ? 1 : -1) * net.ArcFlow(first[idx] + 2 * i);
                            }
                        x2[2 * k + j] = t;
                        if ((t & 1) != 0) AllHalf++;
                    }
                // the relaxed solution closes every equation (twice the residual plus the signed steps is zero)
                var chk = new long[NN];
                for (int i = 0; i < NN; ++i) chk[i] = 2 * res[i];
                for (int k = 0; k < E; ++k)
                    for (int j = 0; j < 2; ++j) {
                        RowOf(ex[f0[k] * 3 + s0[k]], j, out int ra, out int sa); RowOf(ex[f1[k] * 3 + s1[k]], j, out int rb, out int sb);
                        chk[2 * f0[k] + ra] += sa * x2[2 * k + j]; chk[2 * f1[k] + rb] += sb * x2[2 * k + j];
                    }
                for (int i = 0; i < NN; ++i) if (chk[i] != 0) throw new InvalidOperationException("the relaxed solution does not close equation " + i);
            }
            if (!Full) return;
            // the frozen edges take the rounded relaxed value; the library's greedy balancing then repairs what the rounding leaves
            written = new int[2 * E];
            for (int k = 0; k < E; ++k) {
                if (balanced[k]) continue;
                int nx = Round(vx[k], x2[2 * k], rr[2 * k], round), ny = Round(vy[k], x2[2 * k + 1], rr[2 * k + 1], round);
                if ((x2[2 * k] & 1) != 0) FrozenHalf++;
                if ((x2[2 * k + 1] & 1) != 0) FrozenHalf++;
                if (nx != vx[k]) FrozenComponentsChanged++;
                if (ny != vy[k]) FrozenComponentsChanged++;
                if (nx != vx[k] || ny != vy[k]) FrozenChanged++;
                MaxJump = Math.Max(MaxJump, Math.Max(Math.Abs(nx - vx[k]), Math.Abs(ny - vy[k])));
                if (Math.Abs(nx) > 2 || Math.Abs(ny) > 2) Clamped++;
                rep.SetD(f0[k], s0[k], nx, ny); written[2 * k] = nx; written[2 * k + 1] = ny;
                var o = RotI((nx, ny), mm[k]);
                rep.SetD(f1[k], s1[k], -o.Item1, -o.Item2);
            }
        }

        /// <summary> the frozen edges whose value after SolveFlow is not the one the jumps wrote (the greedy balancing moved it, or reset all of them to 0) </summary>
        public int MovedAfterFlow()
        {
            if (written == null) return 0;
            int n = 0;
            for (int k = 0; k < E; ++k) { if (balanced[k]) continue; var (x, y) = rep.D(f0[k], s0[k]); if (x != written[2 * k] || y != written[2 * k + 1]) n++; }
            return n;
        }
        int[] written;

        // v + x2 / 2; a half is rounded to the neighbour nearer to the real offset ("real"), towards the start value ("keep") or down ("down")
        static int Round(int v, int x2, double r, string round)
        {
            if ((x2 & 1) == 0) return v + x2 / 2;
            int lo = v + (int)Math.Floor(x2 / 2.0), hi = lo + 1;
            if (round == "down") return lo;
            if (round == "keep") return x2 > 0 ? lo : hi;
            double dl = Math.Abs(lo - r), dh = Math.Abs(hi - r);
            return dl < dh ? lo : dh < dl ? hi : (x2 > 0 ? lo : hi);
        }
    }

    // ---------------------------------------------------------------------------------------------------- the runs

    sealed class Quality
    {
        public PolyMeshMetrics M; public double P95, Haus, EdgeMin, EdgeP05, EdgeP95, EdgeMax; public int Faces;
        public Quality(DPolyMesh r, DMesh3 input, double s)
        {
            M = PolyMeshMetrics.Compute(r); Faces = r.FaceCount;
            P95 = PolyMeshMetrics.Percentile(M.CornerDeviations, 0.95); Haus = Program.Haus(r, input, s);
            var len = new List<double>();
            for (int f = 0; f < r.FaceCount; ++f) { var fv = r.GetFace(f); for (int i = 0; i < fv.Length; ++i) len.Add((r.GetVertex(fv[i]) - r.GetVertex(fv[(i + 1) % fv.Length])).Length / s); }
            len.Sort();
            EdgeMin = len[0]; EdgeMax = len[len.Count - 1]; EdgeP05 = len[(int)(0.05 * (len.Count - 1))]; EdgeP95 = len[(int)(0.95 * (len.Count - 1))];
        }
    }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        var varRe = A.ContainsKey("var") ? new Regex(A["var"]) : null;
        int adaptArg = A.ContainsKey("adapt") ? int.Parse(A["adapt"], CultureInfo.InvariantCulture) : 0;
        SizeAdaptationMode adaptMode = adaptArg == 1 ? SizeAdaptationMode.Scale : adaptArg == 2 ? SizeAdaptationMode.ScaleAndRotation : SizeAdaptationMode.Off;
        double minGain = A.ContainsKey("mingain") ? double.Parse(A["mingain"], CultureInfo.InvariantCulture) : 0.2;
        double? alignOverride = A.ContainsKey("align") ? double.Parse(A["align"], CultureInfo.InvariantCulture) : (double?)null;
        string round = A.ContainsKey("round") ? A["round"] : "real";
        if (round != "real" && round != "keep" && round != "down") throw new ArgumentException("round must be real, keep or down");
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
        { var w = MakeInput(sphere, 0.1, 0); var wc = new CrossField(w); wc.Compute(); var wq = new QuadRemesher(w) { Field = wc, TargetEdgeLength = 0.2, PureQuads = true }; wq.Compute(); new Rep(w, wc, wq.Positions, 0.2, true).Run(round); }

        Console.WriteLine($"adapt={adaptArg} align={(alignOverride.HasValue ? F(alignOverride.Value, "G3") : "per shape")} round={round}");
        Console.WriteLine("| shape | scale / ratio | tri | library | lib area | jumps | jumps area | class change | frozen / changed / half (all edges) / clamped / max / moved by the balancing | cost lib / relaxed / jumps | folds after flow lib / jumps | H lib / relaxed / jumps | quads 2 / lib / jumps | irr 2 / lib / jumps | corner p95 2 / lib / jumps | Hausdorff 2 / lib / jumps | edge p05-p95 (max) lib ; jumps | t lib repair / jumps repair (of it relaxation) (s) | t stage 2 / pure lib / pure jumps est. (s) |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        int total = 0, libClean = 0, jClean = 0, gained = 0, lost = 0, undetected = 0, replayBad = 0, either = 0, libTimeOk = 0, jTimeOk = 0, integral = 0, sized = 0;
        double sumLib = 0, sumJ = 0, sumRelax = 0;
        var notes = new List<string>();
        foreach (var (name, fn, s0, align, noise) in shapes) {
            if (only != null && !only.IsMatch(name)) continue;
            foreach (var (scale, ratio) in variations) {
                string vname = F(scale, "G3") + "/" + F(ratio, "G3");
                if (varRe != null && !varRe.IsMatch(vname)) continue;
                double s = s0 * scale;
                DMesh3 input = MakeInput(fn, s * ratio, noise);
                var cf = new CrossField(input) { CurvatureAlignment = alignOverride ?? align }; cf.Compute();
                if (!cf.Success) { Console.WriteLine($"| {name} | {vname} | cross field failed {cf.Status} |"); continue; }
                var sw = Stopwatch.StartNew();
                var q2 = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, SizeAdaptation = adaptMode, SizeAdaptationMinGain = minGain }; q2.Compute();
                double t2 = sw.Elapsed.TotalSeconds; sw.Restart();
                var q3 = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PureQuads = true, SizeAdaptation = adaptMode, SizeAdaptationMinGain = minGain }; q3.Compute();
                double t3 = sw.Elapsed.TotalSeconds;
                bool signPenalty = (bool)typeof(QuadRemesher).GetField("SignPenalty", Any).GetValue(q3);
                PositionField pf = q3.Positions;
                // the replay without the jumps is the library
                var lib = new Rep(input, cf, pf, s, signPenalty); lib.Run(null);
                bool replayOk = lib.Status == q3.PureQuadStatus && (lib.Status != PureQuadStatus.Success || SameMesh(lib.Result, q3.Result));
                if (!replayOk) { replayBad++; notes.Add($"REPLAY MISMATCH {name} {vname}: library {q3.PureQuadStatus}, replay {lib.Status}"); }
                var jmp = new Rep(input, cf, pf, s, signPenalty); jmp.Run(round);
                total++;
                var tree = new DMeshAABBTree3(input, true);
                double geoCells = 0; double[] spc = pf.SpacingFactors;
                foreach (int t in input.TriangleIndices()) {
                    Index3i tv = input.GetTriangle(t);
                    double sp = spc == null ? s : s * (spc[tv.a] + spc[tv.b] + spc[tv.c]) / 3.0;
                    geoCells += input.GetTriArea(t) / (sp * sp);
                }
                var Q2 = new Quality(q2.Result, input, s);
                Quality QL = lib.Status == PureQuadStatus.Success ? new Quality(lib.Result, input, s) : null, QJ = jmp.Status == PureQuadStatus.Success ? new Quality(jmp.Result, input, s) : null;
                string Cls(Rep r, Quality q) { if (r.Status != PureQuadStatus.Success) return "fallback"; return GateClean(r.Result, q.M, input, tree) ? "clean" : "UNDETECTED-BAD"; }
                string cl = Cls(lib, QL), cj = Cls(jmp, QJ);
                if (cl == "clean") libClean++;
                if (cj == "clean") jClean++;
                if (cl == "clean" || cj == "clean") either++;
                if (cl == "UNDETECTED-BAD" || cj == "UNDETECTED-BAD") undetected++;
                string change = cl == cj ? "" : cl != "clean" && cj == "clean" ? "GAINED" : cl == "clean" && cj != "clean" ? "LOST" : cl + " -> " + cj;
                if (change == "GAINED") gained++;
                if (change == "LOST") lost++;
                string Q3(Func<Quality, string> f) { return f(Q2) + " / " + (QL == null ? "-" : f(QL)) + " / " + (QJ == null ? "-" : f(QJ)); }
                string Edge(Quality q) { return q == null ? "-" : F(q.EdgeP05, "F2") + "-" + F(q.EdgeP95, "F2") + " (" + F(q.EdgeMax, "F2") + ")"; }
                string Area(Rep r) { return r.FlowCost < 0 ? "-" : F(0.5 * r.Area2 / geoCells, "F3"); }
                var J = jmp.J;
                // the time of Compute() with the jumps, estimated: the library's attempt with its repair replaced by the replay with the jumps
                double tLib = lib.FlowSeconds + lib.RestSeconds, tJ = jmp.JumpSeconds + jmp.FlowSeconds + jmp.RestSeconds, t3j = Math.Max(0, t3 - tLib) + tJ;
                if (cl == "clean" && t3 <= Math.Max(3 * t2, 1.0)) libTimeOk++;
                if (cj == "clean" && t3j <= Math.Max(3 * t2, 1.0)) jTimeOk++;
                if (J.AllHalf == 0) integral++;
                if (q3.SizeAdaptationApplied) sized++;
                if (J.Frozen > 0 && jmp.MovedByBalancing == J.Frozen) notes.Add($"JUMPS DISCARDED {name} {vname}: the balancing moved every frozen edge");
                sumLib += tLib; sumJ += tJ; sumRelax += jmp.JumpSeconds;
                Console.WriteLine($"| {name} | {vname}{(q3.SizeAdaptationApplied ? " (sized)" : "")} | {lib.BaseTriangles} | {lib.Status} | {Area(lib)} | {jmp.Status} | {Area(jmp)} | {change} | {J.Frozen} / {J.FrozenChanged} / {J.FrozenHalf} ({J.AllHalf}) / {J.Clamped} / {J.MaxJump} / {jmp.MovedByBalancing} | {lib.FlowCost} / {(J.RelaxedCost2 < 0 ? "-" : F(0.5 * J.RelaxedCost2, "F1"))} / {jmp.FlowCost} | {lib.InvertedAfterFlow} / {jmp.InvertedAfterFlow} | {lib.Get<int>("FlowCapacity")} / {J.Capacity} / {jmp.Get<int>("FlowCapacity")} | {Q3(q => q.Faces.ToString())} | {Q3(q => q.M.IrregularVertices.ToString())} | {Q3(q => F(q.P95, "F1"))} | {Q3(q => F(q.Haus, "F2"))} | {Edge(QL)} ; {Edge(QJ)} | {F(tLib, "F2")} / {F(tJ, "F2")} ({F(jmp.JumpSeconds, "F2")}) | {F(t2, "F2")} / {F(t3, "F2")} / {F(t3j, "F2")} |");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"runs {total}: library clean {libClean}, jumps clean {jClean}, clean with either {either}; gained {gained}, lost {lost}; undetected bad {undetected}; replay mismatches {replayBad}");
        Console.WriteLine($"time gate max(3 x stage 2, 1 s) on clean rows: library {libTimeOk}/{libClean}, jumps (estimated) {jTimeOk}/{jClean}; repair time summed over the runs: library {F(sumLib, "F1")} s, jumps {F(sumJ, "F1")} s (of it the relaxation {F(sumRelax, "F1")} s); runs whose relaxed solution is integral on every edge: {integral}/{total}; runs with the size field applied: {sized}/{total}");
        foreach (string n in notes) Console.WriteLine(n);
        return undetected == 0 && replayBad == 0 ? 0 : 1;
    }
}
