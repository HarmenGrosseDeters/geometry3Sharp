// QuadFlow3BGate: the gate measurement of stage 3B (pure quads, QuadRemesher.PureQuads; docs/design/QUAD_REMESHING.md).
// 9 procedural shapes x 6 variations (scale 1, 0.8, 1.25, 1.5 at ratio 0.5; ratio 0.35 and 0.7 at scale 1) = 54 runs. Every run is classified
//   clean            PureQuadStatus.Success and the independent checks on the result pass (manifold, only quads, finite, no quad facing away
//                    from the input surface at its nearest point)
//   fallback         PureQuadStatus is not Success and Result is the stage 2 mesh face for face (checked here)
//   undetected bad   Success, but an independent check fails (must be 0), or a fallback whose Result is not the stage 2 mesh
// plus the time gate (clean rows: PureQuads Compute() at most max(3 x stage 2 Compute(), 1 s)) and the quality gates of stage 3.
//   dotnet run -c Release --project benchmarks/QuadFlow3BGate -- [only=<regex>] [profile=1] [repeat=<n>]
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

    // independent of the library's own flipped-quad count: the normal of every quad (cross of the diagonals) against the normal of the
    // input triangle nearest to the quad's centre
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

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        bool profile = A.ContainsKey("profile") && A["profile"] == "1";
        int repeat = A.ContainsKey("repeat") ? int.Parse(A["repeat"]) : 1;
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
        // warm-up (JIT) on a small run, not part of the table
        { var w = MakeInput(sphere, 0.1, 0); var wc = new CrossField(w); wc.Compute(); new QuadRemesher(w) { Field = wc, TargetEdgeLength = 0.2, PureQuads = true }.Compute(); new QuadRemesher(w) { Field = wc, TargetEdgeLength = 0.2 }.Compute(); }
        Console.WriteLine("| shape | scale / ratio | input V | sing. | irr. stage 2 | irr. 3B (gate) | quads % 3B | corner p95 2 / 3B | Hausdorff 2 / 3B (s) | status | inv. | flipped | t stage 2 (s) | t pure (s) | ratio | time gate | class |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        int clean = 0, fallback = 0, undetected = 0, gateTime = 0, total = 0, gateIrr = 0, gateHaus = 0, gateP95 = 0, gateP95Stage2 = 0;
        var ratios = new List<double>(); var cleanTimes = new List<double>(); var fallbackTimes = new List<double>();
        var stageSum = new double[8]; var stageSumFb = new double[8]; string[] stageNames = { "setup", "flow prep", "flow (net + MCF)", "subdivision", "greedy", "SAT", "position solve", "extract" };
        var statusCount = new SortedDictionary<string, int>();
        foreach (var (name, fn, s0, align, noise) in shapes) {
            if (only != null && !only.IsMatch(name)) continue;
            foreach (var (scale, ratio) in variations) {
                double s = s0 * scale;
                DMesh3 input = MakeInput(fn, s * ratio, noise);
                var cf = new CrossField(input) { CurvatureAlignment = align }; cf.Compute();
                if (!cf.Success) { Console.WriteLine($"| {name} | {scale}/{ratio} | cross field failed {cf.Status} |"); continue; }
                double t2 = double.MaxValue, t3 = double.MaxValue; QuadRemesher q2 = null, q3 = null;
                for (int rep = 0; rep < repeat; ++rep) {
                    var a = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s };
                    var sw = Stopwatch.StartNew(); a.Compute(); double ta = sw.Elapsed.TotalSeconds;
                    var b = new QuadRemesher(input) { Field = cf, TargetEdgeLength = s, PureQuads = true };
                    sw.Restart(); b.Compute(); double tb = sw.Elapsed.TotalSeconds;
                    if (ta < t2) { t2 = ta; q2 = a; }
                    if (tb < t3) { t3 = tb; q3 = b; }
                }
                total++;
                PolyMeshMetrics m2 = PolyMeshMetrics.Compute(q2.Result);
                bool success = q3.PureQuadStatus == PureQuadStatus.Success;
                statusCount.TryGetValue(q3.PureQuadStatus.ToString(), out int sc); statusCount[q3.PureQuadStatus.ToString()] = sc + 1;
                string cls; PolyMeshMetrics m3 = PolyMeshMetrics.Compute(q3.Result);
                int sing = cf.SingularFaces.Count, irrGate = sing + (int)Math.Floor(0.1 * m2.IrregularVertices);
                double h2 = Haus(q2.Result, input, s), h3 = Haus(q3.Result, input, s);
                string p2 = F(PolyMeshMetrics.Percentile(m2.CornerDeviations, 0.95), "F1"), p3 = F(PolyMeshMetrics.Percentile(m3.CornerDeviations, 0.95), "F1");
                bool timeOk = t3 <= Math.Max(3 * t2, 1.0);
                var prof = q3.PureQuadRepair;
                if (success) {
                    using (var tree = new DMeshAABBTreeHolder(input)) {
                        int away = FacingAway(q3.Result, input, tree.Tree);
                        bool finite = Enumerable.Range(0, q3.Result.VertexCount).All(v => q3.Result.GetVertex(v).IsFinite);
                        bool bad = !m3.IsManifold || m3.QuadFraction < 1 || away > 0 || !finite || !q3.Result.Validate().IsValid;
                        cls = bad ? "UNDETECTED-BAD" : "clean";
                    }
                } else cls = SameMesh(q2.Result, q3.Result) ? "fallback" : "UNDETECTED-BAD (Result is not the stage 2 mesh)";
                if (cls == "clean") {
                    clean++; ratios.Add(t3 / t2); cleanTimes.Add(t3); if (timeOk) gateTime++;
                    if (m3.IrregularVertices <= irrGate) gateIrr++;
                    if (h3 <= 0.5) gateHaus++;
                    if (PolyMeshMetrics.Percentile(m3.CornerDeviations, 0.95) <= 15) gateP95++;
                    if (PolyMeshMetrics.Percentile(m2.CornerDeviations, 0.95) <= 15) gateP95Stage2++;
                    if (prof != null) { stageSum[0] += prof.SetupSeconds; stageSum[1] += prof.FlowPrepSeconds; stageSum[2] += prof.FlowNetSeconds; stageSum[3] += prof.SubdivisionSeconds; stageSum[4] += prof.GreedySeconds; stageSum[5] += prof.SatSeconds; stageSum[6] += prof.PositionSeconds; stageSum[7] += prof.ExtractSeconds; }
                } else if (cls == "fallback") {
                    fallback++; fallbackTimes.Add(t3);
                    if (prof != null) { stageSumFb[0] += prof.SetupSeconds; stageSumFb[1] += prof.FlowPrepSeconds; stageSumFb[2] += prof.FlowNetSeconds; stageSumFb[3] += prof.SubdivisionSeconds; stageSumFb[4] += prof.GreedySeconds; stageSumFb[5] += prof.SatSeconds; stageSumFb[6] += prof.PositionSeconds; stageSumFb[7] += prof.ExtractSeconds; }
                } else undetected++;
                Console.WriteLine($"| {name} | {F(scale, "G3")} / {F(ratio, "G3")} | {input.VertexCount} | {sing} | {m2.IrregularVertices} | {(success ? m3.IrregularVertices + " (" + irrGate + ")" : "-")} | {(success ? F(100 * m3.QuadFraction, "F1") : "-")} | {p2} / {(success ? p3 : "-")} | {F(h2, "F2")} / {(success ? F(h3, "F2") : "-")} | {q3.PureQuadStatus} | {q3.InvertedTriangles} | {q3.FlippedQuads} | {F(t2, "F2")} | {F(t3, "F2")} | {F(t3 / t2, "F1")} | {(timeOk ? "ok" : "over")} | {cls} |");
                if (profile && prof != null)
                    Console.WriteLine($"PROF {name} {F(scale, "G3")}/{F(ratio, "G3")} {q3.PureQuadStatus} | t2 {F(t2, "F3")} pure {F(t3, "F3")} | setup {F(prof.SetupSeconds, "F3")} flowprep {F(prof.FlowPrepSeconds, "F3")} flownet {F(prof.FlowNetSeconds, "F3")} (arcs {prof.FlowArcs} nodes {prof.FlowNodes} H {prof.FlowCapacity} searches {prof.FlowSearches} settled {prof.FlowSettled}) subdiv {F(prof.SubdivisionSeconds, "F3")} (splits {prof.Splits}) greedy {F(prof.GreedySeconds, "F3")} sat {F(prof.SatSeconds, "F3")} (attempts {prof.SatAttempts} solved {prof.SatSolved} failed {prof.SatFailed} decisions {prof.SatDecisions}) pos {F(prof.PositionSeconds, "F3")} extract {F(prof.ExtractSeconds, "F3")} tri {prof.Triangles}");
            }
        }
        double Med(List<double> l) { if (l.Count == 0) return double.NaN; var c = l.OrderBy(x => x).ToList(); return c[c.Count / 2]; }
        double Max(List<double> l) { return l.Count == 0 ? double.NaN : l.Max(); }
        Console.WriteLine();
        Console.WriteLine($"runs {total}: clean {clean}, fallback {fallback}, undetected bad {undetected}; status counts: {string.Join(", ", statusCount.Select(kv => kv.Key + " " + kv.Value))}");
        Console.WriteLine($"clean rows: time gate (max(3 x stage 2, 1 s)) {gateTime}/{clean}; irregular gate {gateIrr}/{clean}; Hausdorff <= 0.5 s {gateHaus}/{clean}; corner p95 <= 15: {gateP95}/{clean} (stage 2 on the same rows {gateP95Stage2}/{clean})");
        Console.WriteLine($"ratio pure/stage 2 on clean rows: median {F(Med(ratios), "F1")}, max {F(Max(ratios), "F1")}; pure time clean rows: median {F(Med(cleanTimes), "F2")} s, max {F(Max(cleanTimes), "F2")} s; fallback rows: median {F(Med(fallbackTimes), "F2")} s, max {F(Max(fallbackTimes), "F2")} s");
        Console.WriteLine("stage times of the pure-quad attempt, sum over clean rows (s): " + string.Join(", ", Enumerable.Range(0, 8).Select(i => stageNames[i] + " " + F(stageSum[i], "F2"))));
        Console.WriteLine("stage times of the pure-quad attempt, sum over fallback rows (s): " + string.Join(", ", Enumerable.Range(0, 8).Select(i => stageNames[i] + " " + F(stageSumFb[i], "F2"))));
        return undetected == 0 ? 0 : 1;
    }

    sealed class DMeshAABBTreeHolder : IDisposable
    {
        public DMeshAABBTree3 Tree;
        public DMeshAABBTreeHolder(DMesh3 m) { Tree = new DMeshAABBTree3(m, true); }
        public void Dispose() { }
    }
}
