using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

// Upper-bound measurement for local self-intersection untangling as a repair step
// (docs/research/SELF_INTERSECTION_UNTANGLING_2026-11.md). For every procedural self-intersecting solid and frame:
// the untangling prototype (Untangler.cs, four variants) against MeshRobustRepair as it is, the exact resolve alone,
// the solidify alone and the alpha wrap alone (with the repair's defaults). "clean" is always
// MeshRobustRepair.NotCleanReason == "" with the exact self-intersection check. Distances are MeshHausdorffDistance upper
// bounds in units of the input's diagonal, to 1e-4: h(result->input) is how far the result's surface lies from the input's,
// h(input->result) how far input surface lies from the result (surface that was removed or moved); "max move" is the
// largest vertex displacement of an untangled mesh (it bounds both). vol/intent is the volume against the intent's.
//   dotnet run --project benchmarks/SelfIntersectionUntangle -c Release            (all cases, table on stdout)
//   dotnet run --project benchmarks/SelfIntersectionUntangle -c Release -- check   (the corpus self-check only)
//   dotnet run --project benchmarks/SelfIntersectionUntangle -c Release -- open    (the same solids with a hole: which path the
//                                                                                    repair takes, with and without untangling first)
//   dotnet run --project benchmarks/SelfIntersectionUntangle -c Release -- pipeline (SolidBooleanPipeline, defaults: the solid united
//                                                                                    with a box far away; Status and NotCleanReason)
//   ... -- only <substring>                                                         (cases whose name contains it)
static class Program
{
    static string F(double v, string fmt = "0.####") { return double.IsNaN(v) ? "-" : v.ToString(fmt, CultureInfo.InvariantCulture); }

    static double Volume(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

    // the upper bound of the distance from a to b (one-sided) or between them, to 1e-4 of the diagonal
    static double Hausdorff(DMesh3 a, DMesh3 b, bool symmetric, double diag)
    {
        if (a == null || b == null || a.TriangleCount == 0 || b.TriangleCount == 0) return double.NaN;
        var h = new MeshHausdorffDistance(a, b) { Symmetric = symmetric, Tolerance = 1e-4 * diag, MaxSubdivisions = 1000000 };
        return h.Compute() ? h.UpperBound : double.NaN;
    }

    // components and Euler characteristic V - E + F (2 per sphere-like component, 0 per torus-like one)
    static string Topology(DMesh3 m)
    {
        if (m == null) return "-";
        var c = new MeshConnectedComponents(m); c.FindConnectedT();
        return c.Count + "c/" + (m.VertexCount - m.EdgeCount + m.TriangleCount);
    }

    sealed class Row
    {
        public string Method, Status = "";
        public bool Clean;
        public DMesh3 Mesh;
        public long Ms;
        public string Detail = "";
    }

    static Row Timed(string method, Func<Row> f)
    {
        var sw = Stopwatch.StartNew();
        Row r;
        try { r = f(); } catch (Exception e) { r = new Row { Status = "threw " + e.GetType().Name }; }
        r.Method = method; r.Ms = sw.ElapsedMilliseconds;
        if (r.Mesh != null && r.Status == "") {
            r.Status = MeshRobustRepair.NotCleanReason(r.Mesh);
            r.Clean = r.Status == "";
            if (r.Clean) r.Status = "clean";
        }
        return r;
    }

    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        string only = null; bool checkOnly = args.Contains("check"), openMode = args.Contains("open");
        for (int i = 0; i + 1 < args.Length; ++i) if (args[i] == "only") only = args[i + 1];

        var frames = new (string name, Func<Vector3d, Vector3d> map)[] { ("id", p => p), ("rot", Corpus.Rotate) };
        var variants = new[] {
            new UntangleOptions { Signed = false, Diffusion = 0 }, new UntangleOptions { Signed = false, Diffusion = 4 },
            new UntangleOptions { Signed = true, Diffusion = 0 }, new UntangleOptions { Signed = true, Diffusion = 4 } };

        // corpus self-check: every given mesh is closed, consistently oriented and exactly self-intersecting; every intent is clean
        int bad = 0;
        var cases = UCases.Cases().Where(c => only == null || c.Name.Contains(only)).ToList();
        foreach (UCase c in cases) {
            DMesh3 g = c.Given();
            var d = MeshRobustRepair.FindDefects(g);
            bool givenOk = (d & ~(MeshRobustRepair.Defects.SelfIntersections | MeshRobustRepair.Defects.InsideOutComponents)) == 0
                && (d & MeshRobustRepair.Defects.SelfIntersections) != 0;
            string intent = c.Intent == null ? "" : MeshRobustRepair.NotCleanReason(c.Intent());
            if (!givenOk || intent != "") {
                bad++;
                Console.WriteLine("CORPUS DEFECT: " + c.Name + ": given = " + MeshRobustRepair.DescribeDefects(d) + "; intent = '" + intent + "'");
            }
        }
        Console.WriteLine("corpus: " + cases.Count + " cases, " + bad + " defects");
        if (bad > 0) return 2;
        if (checkOnly) return 0;
        if (openMode) return Open(cases, frames[1].map);
        if (args.Contains("pipeline")) {
            Console.WriteLine("class | case | Status | BooleanStatus | NotCleanReason | inside-out cells");
            var count = new Dictionary<string, int>();
            foreach (UCase c in cases) {
                DMesh3 g = c.Given();
                AxisAlignedBox3d b = g.CachedBounds;
                var pipe = new SolidBooleanPipeline(g, Corpus.Box(b.Max.x + 5, 0, 0, b.Max.x + 6, 1, 1), ExactMeshBoolean.Operations.Union);
                pipe.Compute();
                Console.WriteLine(string.Join(" | ", c.Class, c.Name, pipe.Status, pipe.BooleanStatus, pipe.Result == null ? "-" : "'" + pipe.Report.NotCleanReason + "'", pipe.Report.InsideOutCells));
                string k = pipe.Status.ToString(); count[k] = count.TryGetValue(k, out int v) ? v + 1 : 1;
            }
            foreach (var kv in count) Console.WriteLine(kv.Key + " | " + kv.Value);
            return 0;
        }

        Console.WriteLine("class | case | frame | tris | pairs | method | status | ms | h(result->input) | h(input->result) | H(intent) | vol/intent | topology (intent) | detail");
        var tally = new Dictionary<string, int[]>();
        foreach (UCase c in cases)
            foreach (var fr in frames) {
                DMesh3 given = Map(c.Given(), fr.map), intent = c.Intent == null ? null : Map(c.Intent(), fr.map);
                int pairs = MeshRepairMetrics.SelfIntersections(given);
                double diag = given.CachedBounds.DiagonalLength, intentVol = intent == null ? double.NaN : Volume(intent);
                var rows = new List<Row>();
                foreach (UntangleOptions o in variants)
                    rows.Add(Timed("untangle " + o.Name, () => {
                        UntangleResult u = Untangler.Run(given, o);
                        return new Row { Mesh = u.Mesh, Detail = "rounds " + u.Rounds + ", pairs left " + u.PairsEnd + " (best " + u.PairsBest + "), pull-back t " + F(u.PullBackT) };
                    }));
                rows.Add(Timed("robust repair", () => {
                    var rr = new MeshRobustRepair(new DMesh3(given, false, MeshComponents.None));
                    bool ok = rr.Apply();
                    return new Row { Mesh = rr.Mesh, Detail = "Apply " + ok + ", path " + rr.ResultPath + (rr.UsedInput ? ", used input" : "") };
                }));
                rows.Add(Timed("exact resolve", () => new Row { Mesh = ExactMeshBoolean.Resolve(new DMesh3(given, false, MeshComponents.None)), Status = "" }));
                rows.Add(Timed("solidify", () => {
                    var s = new MeshWindingNumberSolidify(new DMesh3(given, false, MeshComponents.None)) { GridResolution = 128 };
                    return s.Compute() ? new Row { Mesh = s.Result } : new Row { Status = "failed" };
                }));
                rows.Add(Timed("alpha wrap", () => {
                    var w = new MeshAlphaWrap(new DMesh3(given, false, MeshComponents.None)) { RelativeAlpha = 300, RelativeOffset = 3000 };
                    w.Compute();
                    return w.Result != null && w.Result.TriangleCount > 0 ? new Row { Mesh = w.Result, Detail = w.Status.ToString() } : new Row { Status = "failed " + w.Status };
                }));
                foreach (Row r in rows) {
                    if (r.Mesh == null && r.Status == "") r.Status = "no result";
                    double hTo = r.Clean ? Hausdorff(r.Mesh, given, false, diag) / diag : double.NaN;
                    double hFrom = r.Clean ? Hausdorff(given, r.Mesh, false, diag) / diag : double.NaN;
                    double hIntent = r.Clean && intent != null ? Hausdorff(r.Mesh, intent, true, diag) / diag : double.NaN;
                    double vol = r.Clean && intent != null ? Volume(r.Mesh) / intentVol : double.NaN;
                    Console.WriteLine(string.Join(" | ", c.Class, c.Name, fr.name, given.TriangleCount, pairs, r.Method, r.Status, r.Ms, F(hTo, "0.#####"), F(hFrom, "0.#####"), F(hIntent, "0.#####"), F(vol),
                        (r.Clean ? Topology(r.Mesh) : "-") + " (" + Topology(intent) + ")", r.Detail));
                    string key = c.Class + " | " + r.Method;
                    if (!tally.ContainsKey(key)) tally[key] = new int[2];
                    tally[key][0]++; if (r.Clean) tally[key][1]++;
                }
                bool any = rows.Take(variants.Length).Any(r => r.Clean);
                string k2 = c.Class + " | untangle (any variant)";
                if (!tally.ContainsKey(k2)) tally[k2] = new int[2];
                tally[k2][0]++; if (any) tally[k2][1]++;
                Console.Out.Flush();
            }
        Console.WriteLine();
        Console.WriteLine("class | method | clean / runs");
        foreach (var kv in tally.OrderBy(k => k.Key, StringComparer.Ordinal))
            Console.WriteLine(kv.Key + " | " + kv.Value[1] + " / " + kv.Value[0]);
        return 0;
    }

    // The same solids with a hole (the rotated frame): MeshRobustRepair as it is, and after the untangling prototype (signed+d0,
    // success = no exact self-intersection left; the hole stays). Two holes per case: one triangle of an intersecting pair
    // removed, and the triangle farthest from every intersecting pair removed.
    static int Open(List<UCase> cases, Func<Vector3d, Vector3d> map)
    {
        Console.WriteLine("class | case | hole | tris | pairs | untangled | repair path | repair clean | h(result->input) | repair path after untangling | clean | h(result->input)");
        var paths = new Dictionary<string, int>();
        foreach (UCase c in cases)
            foreach (string hole in new[] { "at the intersection", "far from it" }) {
                DMesh3 closed = Map(c.Given(), map);
                List<Index2i> pairs = MeshRepairMetrics.SelfIntersectionPairs(closed);
                int remove = pairs[0].a;
                if (hole == "far from it") {
                    var centers = pairs.Select(p => closed.GetTriCentroid(p.a)).ToList();
                    double far = -1;
                    foreach (int t in closed.TriangleIndices()) {
                        Vector3d ct = closed.GetTriCentroid(t);
                        double d = centers.Min(x => x.DistanceSquared(ct));
                        if (d > far) { far = d; remove = t; }
                    }
                }
                var given = new DMesh3(closed, false, MeshComponents.None);
                given.RemoveTriangle(remove, true, false);
                double diag = given.CachedBounds.DiagonalLength;
                int n = MeshRepairMetrics.SelfIntersections(given);
                Func<DMesh3, (string path, bool clean, double h)> repair = m => {
                    var rr = new MeshRobustRepair(new DMesh3(m, false, MeshComponents.None));
                    bool ok = rr.Apply();
                    bool clean = rr.Mesh != null && MeshRobustRepair.NotCleanReason(rr.Mesh) == "";
                    return (rr.ResultPath.ToString() + (ok ? "" : " (Apply false)"), clean, clean ? Hausdorff(rr.Mesh, given, false, diag) / diag : double.NaN);
                };
                var a = repair(given);
                UntangleResult u = n == 0 ? null : Untangler.Run(given, new UntangleOptions { Signed = true, Diffusion = 0 });
                var b = u != null && u.Clean ? repair(u.Mesh) : ("-", false, double.NaN);
                Console.WriteLine(string.Join(" | ", c.Class, c.Name, hole, given.TriangleCount, n, u == null ? "no intersection left" : u.Clean ? "yes" : "no", a.path, a.clean, F(a.h, "0.#####"),
                    b.Item1, b.Item2, F(b.Item3, "0.#####")));
                Console.Out.Flush();
                string k = "as is: " + a.path; paths[k] = paths.TryGetValue(k, out int v1) ? v1 + 1 : 1;
                k = "after untangling: " + b.Item1; paths[k] = paths.TryGetValue(k, out int v2) ? v2 + 1 : 1;
            }
        Console.WriteLine();
        foreach (var kv in paths.OrderBy(x => x.Key, StringComparer.Ordinal)) Console.WriteLine(kv.Key + " | " + kv.Value);
        return 0;
    }

    static DMesh3 Map(DMesh3 m, Func<Vector3d, Vector3d> f)
    {
        var c = new DMesh3(m, false, MeshComponents.None);
        foreach (int v in c.VertexIndices()) c.SetVertex(v, f(c.GetVertex(v)));
        return c;
    }
}
