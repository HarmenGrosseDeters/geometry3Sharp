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
        var shapes = new[] {
            ("sphere", sphere, 0.1, 0.0),
            ("torus", torus, 0.1, 0.0),
            ("torus-align10", torus, 0.1, 10.0),
        };
        foreach (var (name, fn, s0, align) in shapes) {
            if (only != null && !only.IsMatch(name)) continue;
            double s = s0 * scaleMul;
            DMesh3 input = MakeInput(fn, s * ratio);
            var cf = new CrossField(input) { CurvatureAlignment = align }; cf.Compute();
            if (!cf.Success) { Console.WriteLine(name + ": cross field failed " + cf.Status); continue; }
            var qr = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s };
            qr.Compute();
            PolyMeshMetrics m2 = PolyMeshMetrics.Compute(qr.Result);
            var pf = new PositionField(input, cf) { TargetEdgeLength = s };
            var swp = Stopwatch.StartNew(); pf.Compute(); double tpf = swp.Elapsed.TotalSeconds;
            int rounds = A.ContainsKey("rounds") ? int.Parse(A["rounds"]) : 1; double pen = A.ContainsKey("pen") ? double.Parse(A["pen"], CultureInfo.InvariantCulture) : 3;
            var pens = new Dictionary<int, double>(); FlowResult R = null; int bestInv = int.MaxValue; var Rbest = (FlowResult)null; QuadFlowSpike spike = null;
            for (int round = 0; round < rounds; ++round) {
            spike = new QuadFlowSpike(input, cf, pf, s) { SatStrict = strict, Cancel = A.ContainsKey("cancel") && A["cancel"] == "1", CostMode = A.ContainsKey("cost") ? int.Parse(A["cost"]) : 0, HStart = A.ContainsKey("h") ? int.Parse(A["h"]) : 2, SmartSplit = A.ContainsKey("smartsplit") && A["smartsplit"] == "1", SatMaxRing = A.ContainsKey("satring") ? int.Parse(A["satring"]) : 6, SatBudget = A.ContainsKey("satbudget") ? long.Parse(A["satbudget"]) : 100000 };
            spike.EdgePen = pens;
            R = spike.Run(name, greedy, sat);
            Console.WriteLine($"  round {round}: inverted left {R.InvAfterSat}, irregular {(R.Metrics == null ? -1 : R.Metrics.IrregularVertices)}");
            if (R.InvAfterSat < bestInv) { bestInv = R.InvAfterSat; Rbest = R; }
            if (R.InvAfterSat == 0) break;
            foreach (int e in spike.BadEdges) { pens.TryGetValue(e, out double pv); pens[e] = pv + pen; }
            }
            R = Rbest;
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

    static DMesh3 MakeInput(Func<Vector3d, double> fn, double h)
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
        return new DMesh3(m, true);
    }
}
