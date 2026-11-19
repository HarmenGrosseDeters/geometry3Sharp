// QuadFoldDiagnosis: where the folds of the pure-quad flow (QuadRemesher.PureQuads, stage 3B) come from. Measurement only (data branch runs/quad-folds).
// Same 54 runs as benchmarks/QuadFlow3BGate. Runs a benchmark-only copy of QuadFlowRepair (QuadFlowDiag.cs) with hooks:
//   (a) budget x10 / x100 on the fallbacks, (b) single take-back of a flow correction on the folds right after the flow,
//   (c) where the folds sit (singularities, cap seam, recurrence across the 6 variants), (d) at most two flow re-solves with the implicated corrections penalized.
//   dotnet run -c Release --project benchmarks/QuadFoldDiagnosis -- [only=<regex>] [d=0|1] [x100=0|1] [pen=<factor>] [rounds=<n>]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using g3;
using gs;
using gsx;

static class Program
{
    class Fn : BoundedImplicitFunction3d
    {
        public Func<Vector3d, double> F; public AxisAlignedBox3d B;
        public double Value(ref Vector3d p) { return F(p); }
        public AxisAlignedBox3d Bounds() { return B; }
    }
    static string F(double x, string fmt) { return x.ToString(fmt, CultureInfo.InvariantCulture); }

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

    // the independent check of the gate: Success and manifold, only quads, finite, valid, no quad facing away
    static bool Clean(QuadFlowDiag d, DMesh3 input)
    {
        if (d.Status != PureQuadStatus.Success || d.Result == null) return false;
        PolyMeshMetrics m = PolyMeshMetrics.Compute(d.Result);
        var tree = new DMeshAABBTree3(input, true);
        int away = FacingAway(d.Result, input, tree);
        bool finite = Enumerable.Range(0, d.Result.VertexCount).All(v => d.Result.GetVertex(v).IsFinite);
        return m.IsManifold && m.QuadFraction >= 1 && away == 0 && finite && d.Result.Validate().IsValid;
    }

    sealed class Rec
    {
        public string Name; double _s; public double Scale, Ratio, S;
        public int Tris;
        public PureQuadStatus Lib; public int LibInv; public double TLib;
        public QuadFlowDiag D0; public double T0; public bool Clean0;
        public bool Same;
        public Vector3d[] SingC; public Vector3d[] AllC;
        public Func<Vector3d, double> Seam;
        public List<Vector3d> FoldC = new List<Vector3d>();
        public List<Vector3d> FinalFoldC = new List<Vector3d>(); public List<Vector3d> PreSatC = new List<Vector3d>();
        // (a)
        public PureQuadStatus X10; public double TX10; public bool CleanX10; public long DecX10; public int SolvedX10, FailedX10, FoldsX10;
        public PureQuadStatus X10a; public double TX10a; public bool CleanX10a;
        public PureQuadStatus X100; public double TX100; public bool CleanX100; public long DecX100; public bool RanX100;
        // (d)
        public bool RanD; public int[] FoldsFlowRound = new int[3]; public int FoldsSatD, InvD; public PureQuadStatus StD; public double TD; public bool CleanD; public long DecD; public int PenSteps;
        // (d2): the penalties from the folds that survive subdivision and greedy moves; two re-solves, each through the greedy moves
        public bool RanD2; public int D2Pre0, D2Pre1, D2Pre2, D2Pens; public PureQuadStatus StD2; public double TD2; public bool CleanD2; public long DecD2;
    }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        bool doD = !A.ContainsKey("d") || A["d"] == "1";
        bool doD2 = doD;
        bool doX100 = A.ContainsKey("x100") && A["x100"] == "1";
        int pen = A.ContainsKey("pen") ? int.Parse(A["pen"]) : 10;
        int rounds = A.ContainsKey("rounds") ? int.Parse(A["rounds"]) : 2;
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
        // distance to the cap-cylinder seam, on the surface (capsule: axial arc length; capped cylinder: 3D distance to the two edge circles)
        Func<Vector3d, double> seamCapsule = p => { double xp = Math.Abs(p.x) - 0.6; return xp <= 0 ? -xp : 0.5 * Math.Asin(Math.Min(1.0, xp / 0.5)); };
        Func<Vector3d, double> seamCyl = p => { double rho = Math.Sqrt(p.x * p.x + p.y * p.y); return Math.Sqrt((rho - 0.6) * (rho - 0.6) + (Math.Abs(p.z) - 0.8) * (Math.Abs(p.z) - 0.8)); };
        var shapes = new List<(string, Func<Vector3d, double>, double, double, double, Func<Vector3d, double>)> {
            ("sphere", sphere, 0.1, 0.0, 0.0, null), ("torus", torus, 0.1, 0.0, 0.0, null), ("torus-align10", torus, 0.1, 10.0, 0.0, null),
            ("bumpy", bumpy, 0.06, 0.0, 0.0, null), ("filleted-box", rbox, 0.08, 0.0, 0.0, null), ("noisy-sphere", sphere, 0.1, 0.0, 0.2, null),
            ("capsule", capsule, 0.08, 0.0, 0.0, seamCapsule), ("genus2", genus2, 0.06, 0.0, 0.0, null), ("capped-cylinder", cylinder, 0.08, 0.0, 0.0, seamCyl),
        };
        var variations = new (double scale, double ratio)[] { (1, 0.5), (0.8, 0.5), (1.25, 0.5), (1.5, 0.5), (1, 0.35), (1, 0.7) };
        // warm-up
        { var w = MakeInput(sphere, 0.1, 0); var wc = new CrossField(w); wc.Compute(); var wq = new QuadRemesher(w) { Field = wc, TargetEdgeLength = 0.2, PureQuads = true }; wq.Compute(); new QuadFlowDiag(w, wc, wq.Positions, 0.2).Run(); }
        var all = new List<Rec>();
        var swAll = Stopwatch.StartNew();
        foreach (var (name, fn, s0, align, noise, seam) in shapes) {
            if (only != null && !only.IsMatch(name)) continue;
            var shapeRecs = new List<Rec>();
            foreach (var (scale, ratio) in variations) {
                double s = s0 * scale;
                DMesh3 input = MakeInput(fn, s * ratio, noise);
                var cf = new CrossField(input) { CurvatureAlignment = align }; cf.Compute();
                if (!cf.Success) { Console.WriteLine($"{name} {scale}/{ratio}: cross field failed"); continue; }
                var r = new Rec { Name = name, Scale = scale, Ratio = ratio, S = s, Seam = seam };
                var sw = Stopwatch.StartNew();
                var q = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PureQuads = true };
                q.Compute(); r.TLib = sw.Elapsed.TotalSeconds;
                r.Lib = q.PureQuadStatus; r.LibInv = q.InvertedTriangles;
                var pf = q.Positions; var lib = q.PureQuadRepair;
                r.SingC = cf.SingularFaces.Select(t => input.GetTriCentroid(t)).ToArray();
                r.AllC = input.TriangleIndices().Select(t => input.GetTriCentroid(t)).ToArray();
                // the copy, library settings: must agree with the library
                sw.Restart();
                var d0 = new QuadFlowDiag(input, cf, pf, s); d0.Run(); r.T0 = sw.Elapsed.TotalSeconds; r.D0 = d0; r.Tris = d0.Triangles;
                r.Same = d0.Status == r.Lib && d0.InvertedTriangles == lib.InvertedTriangles && d0.SatDecisions == lib.SatDecisions && d0.Splits == lib.Splits
                    && lib.Offsets().SequenceEqual(d0.Offsets());
                r.Clean0 = Clean(d0, input);
                foreach (var fi in d0.FoldsAfterFlow[0]) r.FoldC.Add(fi.C);
                r.FinalFoldC = d0.FinalFoldPos; r.PreSatC = d0.PreSatFoldPos;
                // (a)
                if (r.Lib != PureQuadStatus.Success) {
                    sw.Restart();
                    var x = new QuadFlowDiag(input, cf, pf, s) { SatTotalBudget = 3000000, SatMaxAttempts = 200000, SatMaxRounds = 100000 }; x.Run(); r.TX10 = sw.Elapsed.TotalSeconds;
                    r.X10 = x.Status; r.CleanX10 = Clean(x, input); r.DecX10 = x.SatDecisions; r.SolvedX10 = x.SatSolved; r.FailedX10 = x.SatFailed; r.FoldsX10 = x.InvertedTriangles;
                    sw.Restart();
                    var xa = new QuadFlowDiag(input, cf, pf, s) { SatTotalBudget = 3000000, SatAttemptBudget = 1000000, SatMaxAttempts = 200000, SatMaxRounds = 100000 }; xa.Run(); r.TX10a = sw.Elapsed.TotalSeconds;
                    r.X10a = xa.Status; r.CleanX10a = Clean(xa, input);
                    if (doX100 && !(r.CleanX10 || r.CleanX10a)) {
                        sw.Restart();
                        var xb = new QuadFlowDiag(input, cf, pf, s) { SatTotalBudget = 30000000, SatAttemptBudget = 10000000, SatMaxAttempts = 2000000, SatMaxRounds = 1000000 }; xb.Run(); r.TX100 = sw.Elapsed.TotalSeconds;
                        r.X100 = xb.Status; r.CleanX100 = Clean(xb, input); r.DecX100 = xb.SatDecisions; r.RanX100 = true;
                    }
                }
                // (d)
                if (doD) {
                    sw.Restart();
                    var dd = new QuadFlowDiag(input, cf, pf, s) { ResolveRounds = rounds, PenaltyFactor = pen }; dd.Run(); r.TD = sw.Elapsed.TotalSeconds;
                    r.RanD = true; r.StD = dd.Status; r.CleanD = Clean(dd, input); r.DecD = dd.SatDecisions; r.InvD = dd.InvertedTriangles; r.PenSteps = dd.PenalizedSteps;
                    for (int k = 0; k < 3; ++k) r.FoldsFlowRound[k] = k < dd.FoldsAfterFlow.Count ? dd.FoldsAfterFlow[k].Count : (dd.FoldsAfterFlow.Count > 0 ? dd.FoldsAfterFlow[dd.FoldsAfterFlow.Count - 1].Count : 0);
                    r.FoldsSatD = dd.FoldsBeforeSat;
                }
                if (doD2) {
                    sw.Restart();
                    var i1 = new QuadFlowDiag(input, cf, pf, s) { StopBeforeSat = true }; i1.Run();
                    var pen1 = new HashSet<(int, int, int)>(i1.ImplicatedPersistent);
                    var i2 = new QuadFlowDiag(input, cf, pf, s) { StopBeforeSat = true, PenaltyIn = pen1, PenaltyFactor = pen }; i2.Run();
                    var pen2 = new HashSet<(int, int, int)>(pen1); foreach (var k in i2.ImplicatedPersistent) pen2.Add(k);
                    var i3 = new QuadFlowDiag(input, cf, pf, s) { PenaltyIn = pen2, PenaltyFactor = pen }; i3.Run();
                    r.TD2 = sw.Elapsed.TotalSeconds; r.RanD2 = true; r.D2Pre0 = i1.FoldsBeforeSat; r.D2Pre1 = i2.FoldsBeforeSat; r.D2Pre2 = i3.FoldsBeforeSat; r.D2Pens = pen2.Count;
                    r.StD2 = i3.Status; r.CleanD2 = Clean(i3, input); r.DecD2 = i3.SatDecisions;
                }
                shapeRecs.Add(r); all.Add(r);
                Console.Error.WriteLine($"{name} {scale}/{ratio} lib {r.Lib} x10 {(r.Lib != PureQuadStatus.Success ? r.X10.ToString() : "-")} d {(r.RanD ? r.StD.ToString() : "-")}  [{swAll.Elapsed.TotalSeconds:F0}s]");
            }
        }
        Report(all, doD, doX100, pen, rounds, swAll.Elapsed.TotalSeconds);
        return 0;
    }

    static double Dist(Vector3d p, Vector3d[] pts) { double b = double.MaxValue; foreach (var c in pts) { double d = p.DistanceSquared(c); if (d < b) b = d; } return Math.Sqrt(b); }

    static double Med(List<double> l) { if (l.Count == 0) return double.NaN; var c = l.OrderBy(x => x).ToList(); return c[c.Count / 2]; }

    static void Report(List<Rec> all, bool doD, bool doX100, int pen, int rounds, double totalSeconds)
    {
        var sb = new StringBuilder();
        void W(string s) { Console.WriteLine(s); }
        W($"# QuadFoldDiagnosis (total harness time {F(totalSeconds, "F0")} s, net10.0 Release)");
        W("");
        W("## 0. The copy agrees with the library");
        int same = all.Count(r => r.Same);
        W($"status, inverted count, SAT decisions, splits and all offsets of the copy (library settings) equal the library's on {same} / {all.Count} runs.");
        foreach (var r in all.Where(r => !r.Same)) W($"MISMATCH {r.Name} {r.Scale}/{r.Ratio}");
        W($"Clean (independent check of the gate) with the copy: {all.Count(r => r.Clean0)}; library status Success: {all.Count(r => r.Lib == PureQuadStatus.Success)}.");
        W("");
        W("## (a) Budget vs feasibility");
        W("SatTotalBudget x10 (3M decisions; SatMaxAttempts and the round limit x10, the per-attempt budget stays 100k) and x10 with the per-attempt budget x10 as well" + (doX100 ? "; x100 (30M, per attempt 10M) on the rows still not clean" : "") + ".");
        W("");
        W("| shape | scale / ratio | tris | library status | folds after flow | folds before SAT | folds at end | x10 status (clean) | x10 s | decisions | SAT solved / failed | x10 + attempt x10 status (clean) | s |" + (doX100 ? " x100 status (clean) | s | decisions |" : ""));
        W("|---|---|---|---|---|---|---|---|---|---|---|---|---|" + (doX100 ? "---|---|---|" : ""));
        foreach (var r in all) {
            if (r.Lib == PureQuadStatus.Success) continue;
            W($"| {r.Name} | {F(r.Scale, "G3")} / {F(r.Ratio, "G3")} | {r.Tris} | {r.Lib} | {r.D0.FoldsAfterFlow[0].Count} | {r.D0.FoldsBeforeSat} | {r.LibInv} | {r.X10} ({(r.CleanX10 ? "clean" : "no")}) | {F(r.TX10, "F2")} | {r.DecX10} | {r.SolvedX10} / {r.FailedX10} | {r.X10a} ({(r.CleanX10a ? "clean" : "no")}) | {F(r.TX10a, "F2")} |" + (doX100 ? (r.RanX100 ? $" {r.X100} ({(r.CleanX100 ? "clean" : "no")}) | {F(r.TX100, "F1")} | {r.DecX100} |" : " - | - | - |") : ""));
        }
        var fb = all.Where(r => r.Lib != PureQuadStatus.Success).ToList();
        var bx = fb.Where(r => r.Lib == PureQuadStatus.BudgetExceeded).ToList();
        W("");
        W($"Fallbacks {fb.Count} (BudgetExceeded {bx.Count}, OrientationFolds {fb.Count(r => r.Lib == PureQuadStatus.OrientationFolds)}, other {fb.Count(r => r.Lib != PureQuadStatus.BudgetExceeded && r.Lib != PureQuadStatus.OrientationFolds)}).");
        W($"BudgetExceeded rows clean with x10: {bx.Count(r => r.CleanX10)} / {bx.Count} (time of those: median {F(Med(bx.Where(r => r.CleanX10).Select(r => r.TX10).ToList()), "F2")} s, max {F(bx.Where(r => r.CleanX10).Select(r => r.TX10).DefaultIfEmpty(0).Max(), "F2")} s); with the attempt budget x10 too: {bx.Count(r => r.CleanX10a)} / {bx.Count}; either: {bx.Count(r => r.CleanX10 || r.CleanX10a)}.");
        W($"BudgetExceeded rows: x10 total time {F(bx.Sum(r => r.TX10), "F1")} s (library {F(bx.Sum(r => r.TLib), "F1")} s incl. stage 2). x10 outcome: {string.Join(", ", bx.GroupBy(r => r.X10).Select(g => g.Key + " " + g.Count()))}.");
        if (doX100) W($"x100 run on {bx.Count(r => r.RanX100)} BudgetExceeded rows: clean {bx.Count(r => r.RanX100 && r.CleanX100)}; {string.Join(", ", bx.Where(r => r.RanX100).GroupBy(r => r.X100).Select(g => g.Key + " " + g.Count()))}.");
        W($"OrientationFolds rows clean with x10: {fb.Count(r => r.Lib == PureQuadStatus.OrientationFolds && (r.CleanX10 || r.CleanX10a))} / {fb.Count(r => r.Lib == PureQuadStatus.OrientationFolds)}.");
        W("");
        W("clean per shape (library -> x10 either variant" + (doX100 ? " -> x100" : "") + "):");
        foreach (var g in all.GroupBy(r => r.Name)) W($"- {g.Key}: {g.Count(r => r.Lib == PureQuadStatus.Success)} / {g.Count()} -> {g.Count(r => r.Lib == PureQuadStatus.Success || r.CleanX10 || r.CleanX10a)}" + (doX100 ? $" -> {g.Count(r => r.Lib == PureQuadStatus.Success || r.CleanX10 || r.CleanX10a || r.CleanX100)}" : ""));

        // ---- (b)
        W("");
        W("## (b) Single take-back of a flow correction, on the folds right after the flow (original triangles, before subdivision)");
        W("A correction is the value of an edge after the flow (and the greedy balancing of the fixed variables) minus the clamped position-field offset. Edge level: the edge is reset to its raw offset (both components); unit level: one component moves one unit back toward the raw offset. The fold counts as un-inverted if the two determinants of its offset pairs that contain the changed edge are both >= 0 (the sum-zero constraint is not kept: diagnostic only). 'folds' are triangles with det < 0 after the flow (before subdivision, greedy and SAT).");
        W("");
        W("| shape | runs with folds | folds after flow (sum of 6) | folds before SAT (sum, after subdivision + greedy) | fold clusters (3D, 1.5 s) | corrections per fold 0 / 1 / 2 / 3 | un-inverted by one edge take-back | by one unit take-back | by either | fold with 0 corrections |");
        W("|---|---|---|---|---|---|---|---|---|---|");
        long tF = 0, tFe = 0, tFu = 0, tFa = 0, tF0 = 0, tBS = 0; var tHist = new int[4];
        foreach (var g in all.GroupBy(r => r.Name)) {
            long fo = 0, fe = 0, fu = 0, fa = 0, f0 = 0, bs = 0, cl = 0; var hist = new int[4]; int runsWith = 0;
            foreach (var r in g) {
                var list = r.D0.FoldsAfterFlow[0];
                if (list.Count > 0) runsWith++;
                bs += r.D0.FoldsBeforeSat;
                foreach (var fi in list) { fo++; hist[fi.Corrections]++; if (fi.Corrections == 0) f0++; if (fi.FixEdge > 0) fe++; if (fi.FixUnit > 0) fu++; if (fi.FixEdge > 0 || fi.FixUnit > 0) fa++; }
                cl += Cluster(r.FoldC, 1.5 * r.S).Count;
            }
            tF += fo; tFe += fe; tFu += fu; tFa += fa; tF0 += f0; tBS += bs; for (int k = 0; k < 4; ++k) tHist[k] += hist[k];
            W($"| {g.Key} | {runsWith} / {g.Count()} | {fo} | {bs} | {cl} | {hist[0]} / {hist[1]} / {hist[2]} / {hist[3]} | {Pct(fe, fo)} | {Pct(fu, fo)} | {Pct(fa, fo)} | {f0} |");
        }
        W($"| **all** | {all.Count(r => r.D0.FoldsAfterFlow[0].Count > 0)} / {all.Count} | {tF} | {tBS} | {all.Sum(r => Cluster(r.FoldC, 1.5 * r.S).Count)} | {tHist[0]} / {tHist[1]} / {tHist[2]} / {tHist[3]} | {Pct(tFe, tF)} | {Pct(tFu, tF)} | {Pct(tFa, tF)} | {tF0} |");
        W("");
        {
            long sc = 0, se = 0, su = 0; var hh = new Dictionary<(int, int), int>();
            foreach (var r in all) foreach (var fi in r.D0.FoldsAfterFlow[0]) { sc += fi.Corrections; se += fi.FixEdge; su += fi.FixUnit; hh.TryGetValue((fi.Corrections, fi.FixEdge), out int c); hh[(fi.Corrections, fi.FixEdge)] = c + 1; }
            W("");
            W($"How selective is the take-back? Over all folds: {sc} corrections, of which {Pct(se, sc)} are individually implicating at edge level and {Pct(su, sc)} (sum of corrected components: units counted per fold) at unit level. Folds by (corrections, implicating edge corrections): " + string.Join(", ", hh.OrderBy(kv => kv.Key).Select(kv => $"({kv.Key.Item1},{kv.Key.Item2}): {kv.Value}")) + ".");
        }
        W("");
        W("Persistent folds: a fold after the flow is persistent if a triangle that came out of its subdivision is still inverted before the SAT (after subdivision and greedy moves). Take-back statistics for the persistent ones only:");
        W("");
        W("| shape | folds after flow | persistent | persistent un-inverted by an edge take-back | by a unit take-back | by either | persistent with 0 corrections | corrections per persistent fold 0 / 1 / 2 / 3 | folds before SAT (subdivided triangles) | of those with a parent that was not a fold after the flow |");
        W("|---|---|---|---|---|---|---|---|---|---|");
        {
            long aF = 0, aP = 0, aE = 0, aU = 0, aA = 0, aZ = 0, aB = 0, aN = 0; var aH = new int[4];
            foreach (var g in all.GroupBy(r => r.Name)) {
                long fo = 0, pe = 0, fe = 0, fu = 0, fa = 0, z = 0, bs = 0, nw = 0; var h = new int[4];
                foreach (var r in g) {
                    bs += r.D0.FoldsBeforeSat; nw += r.D0.NewFoldTris;
                    foreach (var fi in r.D0.FoldsAfterFlow[0]) { fo++; if (!fi.Persistent) continue; pe++; h[fi.Corrections]++; if (fi.Corrections == 0) z++; if (fi.FixEdge > 0) fe++; if (fi.FixUnit > 0) fu++; if (fi.FixEdge > 0 || fi.FixUnit > 0) fa++; }
                }
                aF += fo; aP += pe; aE += fe; aU += fu; aA += fa; aZ += z; aB += bs; aN += nw; for (int k = 0; k < 4; ++k) aH[k] += h[k];
                W($"| {g.Key} | {fo} | {pe} | {Pct(fe, pe)} | {Pct(fu, pe)} | {Pct(fa, pe)} | {z} | {h[0]} / {h[1]} / {h[2]} / {h[3]} | {bs} | {nw} |");
            }
            W($"| **all** | {aF} | {aP} | {Pct(aE, aP)} | {Pct(aU, aP)} | {Pct(aA, aP)} | {aZ} | {aH[0]} / {aH[1]} / {aH[2]} / {aH[3]} | {aB} | {aN} |");
        }
        W("");
        W("Same, by whether the run ends clean in the library (folds after flow of the 25 clean / 29 fallback rows):");
        foreach (var grp in new[] { ("clean", all.Where(r => r.Lib == PureQuadStatus.Success).ToList()), ("fallback", fb) }) {
            long fo = 0, fa = 0, fe = 0, fu = 0; foreach (var r in grp.Item2) foreach (var fi in r.D0.FoldsAfterFlow[0]) { fo++; if (fi.FixEdge > 0) fe++; if (fi.FixUnit > 0) fu++; if (fi.FixEdge > 0 || fi.FixUnit > 0) fa++; }
            W($"- {grp.Item1} rows ({grp.Item2.Count}): {fo} folds after flow, {Pct(fe, fo)} by an edge take-back, {Pct(fu, fo)} by a unit take-back, {Pct(fa, fo)} by either; folds before SAT {grp.Item2.Sum(r => r.D0.FoldsBeforeSat)}.");
        }

        // ---- (c)
        W("");
        W("## (c) Where the folds sit");
        W("Distance in units of the target edge length s, 3D, from the fold triangle's centroid to the nearest singular face of the cross field (centroid), and on capsule / capped cylinder to the cap-cylinder seam (capsule: arc length on the surface; capped cylinder: 3D distance to the two edge circles). 'all' is the same measure over all triangles of the input (the share of the surface that lies within the radius), so the ratio fold / all shows concentration.");
        W("");
        W("| shape | folds | singularities per run (median) | median dist to singularity | folds within 1 s / 2 s / 3 s | all triangles within 1 s / 2 s / 3 s | final folds of fallbacks within 2 s (n) | seam: folds within 1 s / 2 s | seam: all triangles within 1 s / 2 s |");
        W("|---|---|---|---|---|---|---|---|---|");
        foreach (var g in all.GroupBy(r => r.Name)) {
            var dS = new List<double>(); var dSall = new List<double>(); var dSeam = new List<double>(); var dSeamAll = new List<double>(); var dFin = new List<double>();
            foreach (var r in g) {
                foreach (var c in r.FoldC) { if (r.SingC.Length > 0) dS.Add(Dist(c, r.SingC) / r.S); if (r.Seam != null) dSeam.Add(r.Seam(c) / r.S); }
                foreach (var c in r.FinalFoldC) if (r.SingC.Length > 0) dFin.Add(Dist(c, r.SingC) / r.S);
                // the baseline: a stride sample of the triangles
                int step = Math.Max(1, r.AllC.Length / 1500);
                for (int i = 0; i < r.AllC.Length; i += step) { if (r.SingC.Length > 0) dSall.Add(Dist(r.AllC[i], r.SingC) / r.S); if (r.Seam != null) dSeamAll.Add(r.Seam(r.AllC[i]) / r.S); }
            }
            string Within(List<double> l, params double[] rr) { return l.Count == 0 ? "-" : string.Join(" / ", rr.Select(x => Pct(l.Count(v => v <= x), l.Count))); }
            string seamF = dSeam.Count == 0 ? "-" : Within(dSeam, 1, 2), seamA = dSeamAll.Count == 0 ? "-" : Within(dSeamAll, 1, 2);
            W($"| {g.Key} | {g.Sum(r => r.FoldC.Count)} | {F(Med(g.Select(r => (double)r.SingC.Length).ToList()), "F0")} | {(dS.Count == 0 ? "-" : F(Med(dS), "F1"))} | {Within(dS, 1, 2, 3)} | {Within(dSall, 1, 2, 3)} | {(dFin.Count == 0 ? "-" : Pct(dFin.Count(v => v <= 2), dFin.Count) + " (" + dFin.Count + ")")} | {seamF} | {seamA} |");
        }
        W("");
        W("");
        W("The same for the folds that survive subdivision and greedy moves (triangles with det < 0 just before the SAT; centroids of the subdivided triangles):");
        W("");
        W("| shape | folds before SAT | within 1 s / 2 s / 3 s of a singularity | seam: within 1 s / 2 s | clusters (1.5 s) |");
        W("|---|---|---|---|---|");
        foreach (var g in all.GroupBy(r => r.Name)) {
            var dS = new List<double>(); var dSeam = new List<double>(); int cl = 0, n = 0;
            foreach (var r in g) { n += r.PreSatC.Count; cl += Cluster(r.PreSatC, 1.5 * r.S).Count; foreach (var c in r.PreSatC) { if (r.SingC.Length > 0) dS.Add(Dist(c, r.SingC) / r.S); if (r.Seam != null) dSeam.Add(r.Seam(c) / r.S); } }
            string Within(List<double> l, params double[] rr) { return l.Count == 0 ? "-" : string.Join(" / ", rr.Select(x => Pct(l.Count(v => v <= x), l.Count))); }
            W($"| {g.Key} | {n} | {Within(dS, 1, 2, 3)} | {(dSeam.Count == 0 ? "-" : Within(dSeam, 1, 2))} | {cl} |");
        }
        W("");
        W("Recurrence across the 6 variants: a fold cluster (3D single linkage 1.5 s) of one variant 'recurs' in another if that variant has a fold triangle within 2 x s0 (the base target length) of the cluster's centroid. Expected = share of the surface (triangle centroids of the cluster's own mesh) within 2 s0 of any fold of the other variant, i.e. what a random place would give. The cross field and the input mesh differ per variant.");
        W("");
        W("| shape | fold clusters | observed recurrences per cluster (of 5 others) | expected by chance | clusters recurring in >= 1 | in >= 3 | variants with folds |");
        W("|---|---|---|---|---|---|---|");
        foreach (var g in all.GroupBy(r => r.Name)) {
            var rs = g.ToList(); double rad = 2 * g.First().S / g.First().Scale; int nCl = 0; double obs = 0, expd = 0; int ge1 = 0, ge3 = 0;
            for (int i = 0; i < rs.Count; ++i) {
                foreach (var cl in Cluster(rs[i].FoldC, 1.5 * rs[i].S)) {
                    nCl++; int hits = 0; double ex = 0;
                    for (int j = 0; j < rs.Count; ++j) {
                        if (j == i) continue;
                        if (rs[j].FoldC.Count > 0 && Dist(cl, rs[j].FoldC.ToArray()) <= rad) hits++;
                        if (rs[j].FoldC.Count > 0) {
                            int step = Math.Max(1, rs[i].AllC.Length / 1500), n = 0, c = 0; var fj = rs[j].FoldC.ToArray();
                            for (int t = 0; t < rs[i].AllC.Length; t += step) { n++; if (Dist(rs[i].AllC[t], fj) <= rad) c++; }
                            ex += (double)c / n;
                        }
                    }
                    obs += hits; expd += ex; if (hits >= 1) ge1++; if (hits >= 3) ge3++;
                }
            }
            W($"| {g.Key} | {nCl} | {(nCl == 0 ? "-" : F(obs / nCl, "F2"))} | {(nCl == 0 ? "-" : F(expd / nCl, "F2"))} | {Pct(ge1, nCl)} | {Pct(ge3, nCl)} | {rs.Count(r => r.FoldC.Count > 0)} / {rs.Count} |");
        }

        // ---- (d)
        if (doD) {
            W("");
            W($"## (d) Flow re-solve with the implicated corrections penalized (at most {rounds} re-solves, step cost of an implicated (edge, component, direction) max(c, 100) x {pen})");
            W("Implicated: on every fold after the flow, the balanced edges whose single take-back (edge level, or unit level where that works) un-inverts it; the same flow is solved again from the same start values (capacity loop from 2) with those steps dearer; the penalties accumulate over the re-solves. Same SAT budget (300k decisions) as the library.");
            W("");
            W("| shape | scale / ratio | library status | folds after flow: solve 1 / 2 / 3 | folds before SAT: library -> penalized | status penalized (clean) | t library copy (s) | t penalized (s) | decisions lib -> pen | penalized (edge, comp, dir) |");
            W("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in all) {
                if (r.D0.FoldsAfterFlow[0].Count == 0 && r.Lib == PureQuadStatus.Success) continue;
                W($"| {r.Name} | {F(r.Scale, "G3")} / {F(r.Ratio, "G3")} | {r.Lib} | {r.FoldsFlowRound[0]} / {r.FoldsFlowRound[1]} / {r.FoldsFlowRound[2]} | {r.D0.FoldsBeforeSat} -> {r.FoldsSatD} | {r.StD} ({(r.CleanD ? "clean" : "no")}) | {F(r.T0, "F2")} | {F(r.TD, "F2")} | {r.D0.SatDecisions} -> {r.DecD} | {r.PenSteps} |");
            }
            W("");
            int c0 = all.Count(r => r.Clean0), cd = all.Count(r => r.CleanD);
            W($"Clean: library {c0} / {all.Count} -> penalized {cd} / {all.Count}; newly clean {all.Count(r => !r.Clean0 && r.CleanD)}, lost {all.Count(r => r.Clean0 && !r.CleanD)}.");
            W($"Folds after the flow, sum over the 54 runs: solve 1 {all.Sum(r => r.FoldsFlowRound[0])}, solve 2 {all.Sum(r => r.FoldsFlowRound[1])}, solve 3 {all.Sum(r => r.FoldsFlowRound[2])}. Folds before SAT: {all.Sum(r => r.D0.FoldsBeforeSat)} -> {all.Sum(r => r.FoldsSatD)}.");
            W($"Time (sum of Run() of the copy over the 54 runs): library settings {F(all.Sum(r => r.T0), "F1")} s, penalized {F(all.Sum(r => r.TD), "F1")} s; fallback rows {F(fb.Sum(r => r.T0), "F1")} -> {F(fb.Sum(r => r.TD), "F1")} s; clean rows (library) {F(all.Where(r => r.Lib == PureQuadStatus.Success).Sum(r => r.T0), "F1")} -> {F(all.Where(r => r.Lib == PureQuadStatus.Success).Sum(r => r.TD), "F1")} s.");
            W("clean per shape (library -> penalized):");
            foreach (var g in all.GroupBy(r => r.Name)) W($"- {g.Key}: {g.Count(r => r.Clean0)} / {g.Count()} -> {g.Count(r => r.CleanD)}");
            W("");
            W($"## (d2) Same, with the penalties taken from the persistent folds only (folds that survive subdivision and greedy moves); two re-solves, each carried through the greedy moves (the penalties accumulate); the SAT runs on the last one only");
            W("");
            W("| shape | scale / ratio | library status | folds before SAT: library -> re-solve 1 -> re-solve 2 | penalized steps | status (clean) | decisions lib -> pen | t library copy (s) | t all three solves + SAT (s) |");
            W("|---|---|---|---|---|---|---|---|---|");
            foreach (var r in all) {
                if (!r.RanD2 || (r.D2Pre0 == 0 && r.Lib == PureQuadStatus.Success)) continue;
                W($"| {r.Name} | {F(r.Scale, "G3")} / {F(r.Ratio, "G3")} | {r.Lib} | {r.D2Pre0} -> {r.D2Pre1} -> {r.D2Pre2} | {r.D2Pens} | {r.StD2} ({(r.CleanD2 ? "clean" : "no")}) | {r.D0.SatDecisions} -> {r.DecD2} | {F(r.T0, "F2")} | {F(r.TD2, "F2")} |");
            }
            W("");
            W($"Clean: library {all.Count(r => r.Clean0)} / {all.Count} -> {all.Count(r => r.CleanD2)} / {all.Count}; newly clean {all.Count(r => !r.Clean0 && r.CleanD2)}, lost {all.Count(r => r.Clean0 && !r.CleanD2)}. Folds before SAT, sum: {all.Sum(r => r.D2Pre0)} -> {all.Sum(r => r.D2Pre1)} -> {all.Sum(r => r.D2Pre2)}.");
            W($"Time (sum): library copy {F(all.Sum(r => r.T0), "F1")} s -> {F(all.Sum(r => r.TD2), "F1")} s; fallback rows {F(fb.Sum(r => r.T0), "F1")} -> {F(fb.Sum(r => r.TD2), "F1")} s; clean rows (library) {F(all.Where(r => r.Lib == PureQuadStatus.Success).Sum(r => r.T0), "F1")} -> {F(all.Where(r => r.Lib == PureQuadStatus.Success).Sum(r => r.TD2), "F1")} s.");
            foreach (var g in all.GroupBy(r => r.Name)) W($"- {g.Key}: {g.Count(r => r.Clean0)} / {g.Count()} -> {g.Count(r => r.CleanD2)}");
        }
    }

    static string Pct(long a, long n) { return n == 0 ? "-" : F(100.0 * a / n, "F0") + " % (" + a + ")"; }

    static List<Vector3d> Cluster(List<Vector3d> pts, double link)
    {
        int n = pts.Count; var uf = new int[n]; for (int i = 0; i < n; ++i) uf[i] = i;
        int Find(int x) { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; }
        double l2 = link * link;
        for (int i = 0; i < n; ++i) for (int j = i + 1; j < n; ++j) if (pts[i].DistanceSquared(pts[j]) <= l2) { int a = Find(i), b = Find(j); if (a != b) uf[Math.Max(a, b)] = Math.Min(a, b); }
        var sum = new Dictionary<int, (Vector3d, int)>();
        for (int i = 0; i < n; ++i) { int r = Find(i); sum.TryGetValue(r, out var v); sum[r] = (v.Item1 + pts[i], v.Item2 + 1); }
        return sum.OrderBy(kv => kv.Key).Select(kv => kv.Value.Item1 / kv.Value.Item2).ToList();
    }
}
