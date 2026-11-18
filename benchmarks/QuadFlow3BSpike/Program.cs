// Throwaway spike (QUAD_REMESHING.md 4.3): second stage 3B upper-bound measurement, QuadriFlow position-field repair from the paper.
//   dotnet run -c Release --project benchmarks/QuadFlow3BSpike -- [only=<regex>] [ratio=0.5] [greedy=1] [sat=1] [strict=0]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using gs;

static class Program
{
    class Fn : BoundedImplicitFunction3d
    {
        public Func<Vector3d, double> F; public AxisAlignedBox3d B;
        public double Value(ref Vector3d p) { return F(p); }
        public AxisAlignedBox3d Bounds() { return B; }
    }
    static string F(double x, string fmt) { return x.ToString(fmt, CultureInfo.InvariantCulture); }

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "dpllselftest") { DpllSelfTest(); return 0; }
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        double ratio = A.ContainsKey("ratio") ? double.Parse(A["ratio"], CultureInfo.InvariantCulture) : 0.5;
        bool greedy = !(A.ContainsKey("greedy") && A["greedy"] == "0");
        bool sat = !(A.ContainsKey("sat") && A["sat"] == "0");
        bool strict = A.ContainsKey("strict") && A["strict"] == "1";
        double scaleMul = A.ContainsKey("scale") ? double.Parse(A["scale"], CultureInfo.InvariantCulture) : 1.0;
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
            ("sphere", sphere, 0.1, 0.0, 0.0),
            ("torus", torus, 0.1, 0.0, 0.0),
            ("torus-align10", torus, 0.1, 10.0, 0.0),
            ("bumpy", bumpy, 0.06, 0.0, 0.0),
            ("filleted-box", rbox, 0.08, 0.0, 0.0),
            ("noisy-sphere", sphere, 0.1, 0.0, 0.2),
            ("capsule", capsule, 0.08, 0.0, 0.0),
            ("genus2", genus2, 0.06, 0.0, 0.0),
            ("capped-cylinder", cylinder, 0.08, 0.0, 0.0),
        };
        bool gate = A.ContainsKey("gate");
        foreach (var (name, fn, s0, align, noise) in shapes) {
            if (only != null && !only.IsMatch(name)) continue;
            double s = s0 * scaleMul;
            var swAll = Stopwatch.StartNew();
            DMesh3 input = MakeInput(fn, s * ratio, noise);
            var cf = new CrossField(input) { CurvatureAlignment = align }; cf.Compute();
            if (!cf.Success) { Console.WriteLine(name + ": cross field failed " + cf.Status); continue; }
            var qr = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s };
            qr.Compute();
            PolyMeshMetrics m2 = PolyMeshMetrics.Compute(qr.Result);
            var pf = new PositionField(input, cf) { TargetEdgeLength = s };
            var swp = Stopwatch.StartNew(); pf.Compute(); double tpf = swp.Elapsed.TotalSeconds;
            int rounds = A.ContainsKey("rounds") ? int.Parse(A["rounds"]) : 1; double pen = A.ContainsKey("pen") ? double.Parse(A["pen"], CultureInfo.InvariantCulture) : 3;
            double tRun = 0, tRunTotal = 0; var pens = new Dictionary<int, double>(); FlowResult R = null; int bestInv = int.MaxValue; var Rbest = (FlowResult)null; QuadFlowSpike spike = null;
            for (int round = 0; round < rounds; ++round) {
            spike = new QuadFlowSpike(input, cf, pf, s) { SatStrict = strict, Cancel = A.ContainsKey("cancel") && A["cancel"] == "1", Project = A.ContainsKey("proj") && A["proj"] == "1", ExcessMcf = A.ContainsKey("excess") && A["excess"] == "1", SatTotalBudget = A.ContainsKey("sattotal") ? long.Parse(A["sattotal"]) : long.MaxValue, SatStopOnFail = A.ContainsKey("satstop") && A["satstop"] == "1", FastMcf = A.ContainsKey("fastmcf") && A["fastmcf"] == "1", FastSubdiv = A.ContainsKey("fastsub") && A["fastsub"] == "1", PosMode = A.ContainsKey("posmode") ? int.Parse(A["posmode"]) : 1, PosAnchor = A.ContainsKey("posanchor") ? double.Parse(A["posanchor"], CultureInfo.InvariantCulture) : 0.5, FastSat = A.ContainsKey("fastsat") && A["fastsat"] == "1", FastPos = A.ContainsKey("fastpos") && A["fastpos"] == "1", CostMode = A.ContainsKey("cost") ? int.Parse(A["cost"]) : 0, HStart = A.ContainsKey("h") ? int.Parse(A["h"]) : 2, SmartSplit = A.ContainsKey("smartsplit") && A["smartsplit"] == "1", SatMaxRing = A.ContainsKey("satring") ? int.Parse(A["satring"]) : 6, SatBudget = A.ContainsKey("satbudget") ? long.Parse(A["satbudget"]) : 100000, SignPen = A.ContainsKey("signpen") ? double.Parse(A["signpen"], CultureInfo.InvariantCulture) : 10, SignTol = A.ContainsKey("signtol") ? double.Parse(A["signtol"], CultureInfo.InvariantCulture) : 0.25 };
            spike.EdgePen = pens;
            var swRun = Stopwatch.StartNew();
            R = spike.Run(name, greedy, sat);
            tRun = swRun.Elapsed.TotalSeconds;
            Console.WriteLine($"  round {round}: inverted left {R.InvAfterSat}, irregular {(R.Metrics == null ? -1 : R.Metrics.IrregularVertices)}");
            tRunTotal += tRun;
            if (R.InvAfterSat < bestInv) { bestInv = R.InvAfterSat; Rbest = R; }
            if (R.InvAfterSat == 0) break;
            foreach (int e in spike.BadEdges) { pens.TryGetValue(e, out double pv); pens[e] = pv + pen; }
            }
            R = Rbest;
            if (gate) { GateLine(name, input, cf, qr, m2, R, s, tpf, tRunTotal, scaleMul, ratio); continue; }
            Console.WriteLine($"## {name}: input V {input.VertexCount}, singular faces {cf.SingularFaces.Count}, s {s}");
            Console.WriteLine($"stage 2 : irregular {m2.IrregularVertices}, quads {F(100 * m2.QuadFraction, "F1")} %, tris {m2.Triangles}, manifold {m2.IsManifold}, corner p95 {F(PolyMeshMetrics.Percentile(m2.CornerDeviations, 0.95), "F1")}, t pos {F(qr.PositionSeconds, "F2")} s, t extract {F(qr.ExtractSeconds, "F2")} s");
            Console.WriteLine($"position singular triangles: before flow {R.PosSingBefore}, after flow {R.PosSingAfter}; vars balanced {R.Balanced}, fixed {R.Fixed} (boundary {R.FixedBoundary}), network components {R.Components}, clamped {R.Clamped}{R.Note}");
            Console.WriteLine($"orientation: before flow inverted {R.InvBefore} (det>0: {R.DetPosBefore} of {R.Triangles}); after flow, before subdivision inverted {R.InvAfterFlowPreSplit} (det>0: {R.DetPosAfterFlow})");
            Console.WriteLine($"flow: units {R.FlowUnits}, cost {R.FlowCost}, capacity H {R.Hcap}, augmentations {R.Augments}, t flow {F(R.TFlow, "F2")} s (setup {F(R.TSetup, "F2")} s)");
            Console.WriteLine($"subdivision: splits {R.Splits}, long edges left {R.LongEdgesAfter}; inverted triangles: after flow {R.InvAfterFlow}, after greedy {R.InvAfterGreedy} ({R.GreedyMoves} moves, {F(R.TGreedy, "F2")} s), after SAT {R.InvAfterSat} (solved {R.SatSolved}, failed {R.SatFailed}, attempts {R.SatAttempts} [dbg viol {R.SatCurViol} inv-in-region {R.SatInvInRegion}] (unsat {R.SatUnsat}, budget {R.SatBudget}), {F(R.TSat, "F2")} s, {(strict ? "det<=0 forbidden" : "det<0 forbidden")})");
            var m = R.Metrics;
            if (m == null) { Console.WriteLine("result: no faces"); continue; }
            Console.WriteLine($"result: faces {m.Faces}, quads {F(100 * m.QuadFraction, "F1")} %, tris {m.Triangles}, 5-6 {m.Pentagons + m.Hexagons}, irregular {m.IrregularVertices}, manifold {(m.IsManifold ? "yes" : "NO (" + m.NonManifoldEdges + "e " + m.InconsistentEdges + "i " + m.NonManifoldVertices + "v)")}, corner p50/p95 {F(PolyMeshMetrics.Percentile(m.CornerDeviations, 0.5), "F1")} / {F(PolyMeshMetrics.Percentile(m.CornerDeviations, 0.95), "F1")}, unpaired tris {R.Unpaired}, cancelled pillow pairs {R.Cancelled}, geometrically flipped quads {R.FlippedQuads}/{R.Quads}, bad tris {R.BadQuads}, inverted nondegenerate {R.InvertedTris}; t pos solve {F(R.TPos, "F2")} s, t extract {F(R.TExtract, "F2")} s");
            Console.WriteLine();
        }
        return 0;
    }

    static double Haus(DPolyMesh pm, DMesh3 input, double s)
    {
        if (pm == null || pm.FaceCount == 0) return double.NaN;
        DMesh3 tri = pm.ToDMesh3(out int[] map, new List<int>());
        if (tri.TriangleCount == 0) return double.NaN;
        var hd = new MeshHausdorffDistance(tri, input) { Symmetric = false, RelativeTolerance = 1e-3 };
        hd.Compute(); return hd.UpperBound / s;
    }

    static void GateLine(string name, DMesh3 input, CrossField cf, QuadRemesher qr, PolyMeshMetrics m2, FlowResult R, double s, double tpf, double tRun, double scaleMul, double ratio)
    {
        var m = R.Metrics; int sing = cf.SingularFaces.Count;
        double t2 = qr.PositionSeconds + qr.ExtractSeconds, t3 = tpf + tRun;
        double h2 = Haus(qr.Result, input, s), h3 = Haus(R.Out, input, s);
        int gateIrr = sing + (int)Math.Floor(0.1 * m2.IrregularVertices);
        bool detected = R.InvAfterSat > 0 || R.PosSingAfter > 0;
        bool bad = m == null || !m.IsManifold || R.FlippedQuads > 0 || m.QuadFraction < 1 || R.BadQuads > 0 || R.Unpaired > 0 || m.Pentagons + m.Hexagons > 0;
        string cls = R.PosSingAfter > 0 ? "FALLBACK(flow infeasible)" : detected ? "FALLBACK" : bad ? "UNDETECTED-BAD" : "clean";
        string p95(PolyMeshMetrics x) => F(PolyMeshMetrics.Percentile(x.CornerDeviations, 0.95), "F1");
        Console.WriteLine($"| {name} | {F(scaleMul, "G3")} / {F(ratio, "G3")} | {input.VertexCount} | {sing} | {m2.IrregularVertices} | {F(100 * m2.QuadFraction, "F1")} | {p95(m2)} | {F(h2, "F2")} | {F(t2, "F2")} || {(m == null ? -1 : m.IrregularVertices)} ({gateIrr}) | {(m == null ? "-" : F(100 * m.QuadFraction, "F1"))} | {(m == null ? "-" : p95(m))} | {F(h3, "F2")} | {(m != null && m.IsManifold ? "yes" : "NO")} | {R.InvAfterSat} | {R.FlippedQuads} | {F(R.TFlow, "F2")} | {F(R.TSat, "F2")} | {F(t3, "F2")} | {F(t3 / t2, "F1")} | {cls} |");
        Console.WriteLine($"PROF {name} {F(scaleMul, "G3")}/{F(ratio, "G3")} {cls} | t2 {F(t2, "F3")} tpf {F(tpf, "F3")} setup {F(R.TSetup, "F3")} flowprep {F(R.TFlowPrep, "F3")} netbuild+mcf {F(R.TNet, "F3")} (mcf {F(R.TMcf, "F3")}, H iters {R.HIters}, arcs {R.NArcs}, nodes {R.NNodes}, aug {R.Augments}, settled {R.Settled} phases {R.Phases}, units {R.FlowUnits}) subdiv {F(R.TSub, "F3")} greedy {F(R.TGreedy, "F3")} sat {F(R.TSat, "F3")} (att {R.SatAttempts} solved {R.SatSolved} failed {R.SatFailed} dec {R.SatDecisions} clauses {R.SatClauses}) pos {F(R.TPos, "F3")} extract {F(R.TExtract, "F3")} tri {R.Triangles} splits {R.Splits} inv {R.InvAfterSat}");
    }

    static void DpllSelfTest()
    {
        var rnd = new Random(5); int bad = 0, sat = 0, total = 0;
        for (int it = 0; it < 3000; ++it) {
            int nv = 4 + rnd.Next(8), g = nv / 3; if (g < 1) g = 1;
            // groups of 3 variables (exactly one), plus random clauses
            int ng = Math.Max(1, nv / 3); nv = ng * 3;
            var d = new Dpll(nv);
            var groups = new int[ng][];
            for (int k = 0; k < ng; ++k) {
                groups[k] = new[] { 3 * k + 1, 3 * k + 2, 3 * k + 3 };
                d.AddClause(3 * k + 1, 3 * k + 2, 3 * k + 3);
                d.AddClause(-(3 * k + 1), -(3 * k + 2)); d.AddClause(-(3 * k + 1), -(3 * k + 3)); d.AddClause(-(3 * k + 2), -(3 * k + 3));
            }
            int nc = rnd.Next(1, 3 * ng);
            var extra = new List<int[]>();
            for (int c = 0; c < nc; ++c) {
                int len = 1 + rnd.Next(3); var lits = new int[len];
                for (int i = 0; i < len; ++i) lits[i] = (rnd.Next(nv) + 1) * (rnd.Next(2) == 0 ? 1 : -1);
                extra.Add(lits); d.AddClause(lits);
            }
            // brute force over the assignments (one value per group)
            bool exp = false; int[] pick = new int[ng];
            for (int code = 0; code < (int)Math.Pow(3, ng) && !exp; ++code) {
                int cc = code; var asg = new bool[nv];
                for (int k = 0; k < ng; ++k) { asg[3 * k + cc % 3] = true; cc /= 3; }
                bool ok = true;
                foreach (var c in extra) { bool any = false; foreach (int l in c) if (l > 0 ? asg[l - 1] : !asg[-l - 1]) any = true; if (!any) { ok = false; break; } }
                if (ok) exp = true;
            }
            int r = d.Solve(groups, out bool[] model);
            total++; if (exp) sat++;
            if ((r == 1) != exp) bad++;
            if (r == 1) { foreach (var c in extra) { bool any = false; foreach (int l in c) if (l > 0 ? model[l - 1] : !model[-l - 1]) any = true; if (!any) bad++; } }
        }
        Console.WriteLine($"dpll self test: {total} instances, {sat} satisfiable, {bad} wrong");
    }

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
}
