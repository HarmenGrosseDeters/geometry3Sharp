// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Remesher.MinTriangleQuality (opt-in quality safeguards) on and off, with the settings of `g3cli remesh` (20 passes,
// smoothing 0.5, projection onto the input, boundary and 30 degree creases kept): per case the poor and flat
// triangles, self-intersecting triangle pairs, inside-out components, Hausdorff distance and time, and how many cases
// get worse. Procedural primitives, gears and thin-walled tubes (targets far above the wall thickness); optionally the
// Thingi10K selection of benchmarks/RemeshBenchmark (fetch_data.py).
//
//   dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- [thingi=<dir>] [only=<regex>] [quality=0.2] [runs=1]
//       [base=0] [si=1]
// base=<q>: the quality of the "off" run (default 0); si=1: Remesher.PreventSelfIntersections in the "on" runs,
// si_from=<k>: only from pass k on (0-based; an alternative that skips the check in the first passes);
// pin=<deg>: MeshConstraintUtil.PinSharpVertices with this angle deficit in the "on" runs;
// sizing=<f>: Remesher.TargetEdgeLengthF = RemeshSizing.FromWallThickness(input, f) in the "on" runs
// spheres=<seeds>: instead of the cases, the jittered spheres of issue #1441 (triangles facing inward; see Spheres)
// sweep=1: every buffered move pass (smoothing, projection) is also checked with MeshSweepCollision over its motion
//   (outside the reported time; Sweep* fields of the JSON, a stderr line per pass that starts and ends without a pair)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using g3;
using gs;

static class Program
{
    class Result
    {
        public int Tris, Poor, Flat, SelfPairs, Inverted, RejectedPasses, Degenerate, SelfIntersectionsRejected;
        // band=<f>: refusals of the band guard, and the RemeshCertificate of the result (band f x diagonal)
        public int DeviationRejected; public bool CertPassed, CertWithinBand, CertNesting; public double CertFwd = double.NaN, CertBwd = double.NaN; public string CertTopology;
        // sweep=1: the buffered move passes (smoothing, projection) checked by a MeshSweepCollision over each pass's motion
        // (outside Seconds): passes with moves, passes whose path touches a primitive (sweep status Collision / IterationLimit),
        // of those the ones that end intersection-free after starting so (a move through a wall that a static end check
        // accepts), the sweep seconds and the candidate pairs
        public int SweepPasses, SweepCollisions, SweepTunnelled; public long SweepCandidates; public double SweepSeconds, SweepComputeSeconds;
        public double Hausdorff, Seconds;
        public double VolumeRatio = double.NaN;     // signed volume of the result / of the input (closed inputs)
        // MeshFidelityMetrics against the input (relative thresholds of its size): F-score at 0.1 % and 0.5 %, normal
        // consistency, Edge Chamfer distance / size and edge recall at 0.5 % (30 degree sharp edges), min angle of
        // the 1 % worst triangles (degrees, TriangleQualityStatistics)
        public double F0001 = double.NaN, F0005 = double.NaN, NC = double.NaN, ECD = double.NaN, ER0005 = double.NaN, MinAngle1 = double.NaN;
        public string NotClean;
    }

    // spheres=<seeds>: the family of issue #1441. The normalized cube sphere (8 vertices per edge, h = 2/7), every vertex
    // moved by up to jit * h / 2 per axis (jit 0.3 and 0.45, seeds 1..<seeds>) and put back onto the unit sphere; the inputs
    // with every triangle facing outward are remeshed to 0.8 h with 6 x BasicRemeshPass (serial, projection onto the
    // sphere). Per setting: the inputs with a triangle facing inward (area > 1e-12, normal . centroid < 0) after some
    // pass and after the last one, the number of those triangles, poor triangles and the time.
    sealed class UnitSphere : IProjectionTarget
    {
        public Vector3d Project(Vector3d p, int id = -1) => p.Normalized;
    }

    static DMesh3 JitteredSphere(int seed, double jit)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = 8, Radius = 1 }.Generate().MakeDMesh();
        var rnd = new Random(seed);
        foreach (int v in m.VertexIndices()) {
            var d = new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
            m.SetVertex(v, (m.GetVertex(v) + d * (jit * (2.0 / 7) / 2)).Normalized);
        }
        return m;
    }

    static int InwardTriangles(DMesh3 m)
    {
        int n = 0;
        Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
        foreach (int t in m.TriangleIndices()) {
            m.GetTriVertices(t, ref a, ref b, ref c);
            Vector3d normal = (b - a).Cross(c - a);
            if (normal.Length * 0.5 > 1e-12 && normal.Dot((a + b + c) / 3) < 0)
                n++;
        }
        return n;
    }

    static int Spheres(int seeds)
    {
        var inputs = new List<(int seed, double jit)>();
        foreach (double jit in new[] { 0.3, 0.45 })
            for (int s = 1; s <= seeds; ++s)
                if (InwardTriangles(JitteredSphere(s, jit)) == 0) inputs.Add((s, jit));
        var settings = new (string name, Action<Remesher> set)[] {
            ("MinTriangleQuality = 0.2", r => r.MinTriangleQuality = 0.2),
            ("+ PreventNormalFlips = true", r => { r.MinTriangleQuality = 0.2; r.PreventNormalFlips = true; }),
            ("EnableSmoothing = false", r => { r.MinTriangleQuality = 0.2; r.EnableSmoothing = false; }),
            ("no projection target", r => { r.MinTriangleQuality = 0.2; r.SetProjectionTarget(null); }),
            ("EnableFlips = false", r => { r.MinTriangleQuality = 0.2; r.EnableFlips = false; }),
            ("EnableCollapses = false", r => { r.MinTriangleQuality = 0.2; r.EnableCollapses = false; }),
            ("EnableSplits = false", r => { r.MinTriangleQuality = 0.2; r.EnableSplits = false; }),
            ("PreventNormalFlips = true alone", r => r.PreventNormalFlips = true),
            ("neither (opt-out)", r => { }),
        };
        Console.WriteLine($"{inputs.Count} inputs (of {2 * seeds})");
        Console.WriteLine("| setting | inputs with an inward triangle after some pass | after pass 6 | inward triangles after pass 6 | q<0.1 | triangles | not closed or invalid | time s |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var (name, set) in settings) {
            int some = 0, last = 0, lastTriangles = 0, poor = 0, triangles = 0, invalid = 0;
            var sw = Stopwatch.StartNew();
            foreach (var (seed, jit) in inputs) {
                DMesh3 m = JitteredSphere(seed, jit);
                var r = new Remesher(m) { EnableParallelSmooth = false, EnableParallelProjection = false };
                r.SetTargetEdgeLength(0.8 * 2.0 / 7);
                r.SetProjectionTarget(new UnitSphere());
                set(r);
                bool any = false;
                int n = 0;
                for (int pass = 0; pass < 6; ++pass) {
                    r.BasicRemeshPass();
                    n = InwardTriangles(m);
                    any |= n > 0;
                }
                if (any) some++;
                if (n > 0) last++;
                lastTriangles += n;
                triangles += m.TriangleCount;
                if (m.IsClosed() == false || m.CheckValidity(false, FailMode.ReturnOnly) == false) invalid++;
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                foreach (int t in m.TriangleIndices()) {
                    m.GetTriVertices(t, ref a, ref b, ref c);
                    if (MeshRefinerBase.TriangleQuality(ref a, ref b, ref c) < 0.1) poor++;
                }
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7:F1} |",
                name, some, last, lastTriangles, poor, triangles, invalid, sw.Elapsed.TotalSeconds));
        }
        return 0;
    }

    static int Main(string[] args)
    {
        var A = args.Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        if (A.ContainsKey("spheres"))
            return Spheres(int.Parse(A["spheres"]));
        var only = A.ContainsKey("only") ? new Regex(A["only"]) : null;
        // quality=<q> or qualities=<q1>,<q2>,..: the values compared with 0 (off)
        double[] qualities = (A.ContainsKey("qualities") ? A["qualities"] : A.ContainsKey("quality") ? A["quality"] : "0.2")
            .Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        fidelity = A.ContainsKey("fidelity") == false || A["fidelity"] != "0";
        double baseQuality = A.ContainsKey("base") ? double.Parse(A["base"], CultureInfo.InvariantCulture) : 0;
        preventOn = A.ContainsKey("si") && A["si"] == "1";
        preventFrom = A.ContainsKey("si_from") ? int.Parse(A["si_from"]) : 0;
        double pinAngle = A.ContainsKey("pin") ? double.Parse(A["pin"], CultureInfo.InvariantCulture) : 0;
        sweep = A.ContainsKey("sweep") && A["sweep"] == "1";
        sizing = A.ContainsKey("sizing") ? double.Parse(A["sizing"], CultureInfo.InvariantCulture) : 0;
        int runs = A.ContainsKey("runs") ? int.Parse(A["runs"]) : 1;
        remeshBand = A.ContainsKey("band") ? double.Parse(A["band"], CultureInfo.InvariantCulture) : 0;
        postBand = A.ContainsKey("post") ? double.Parse(A["post"], CultureInfo.InvariantCulture) : 0;
        postVariant = A.ContainsKey("post_variant") ? A["post_variant"] : "";

        var cases = new List<(string group, string name, DMesh3 input, double target)>();
        foreach (int s in new[] { 6, 8, 10, 12, 16, 24, 32 })
            foreach (var (kind, m) in new[] { ("cylinder", Cylinder(s, 1, 2)), ("cone", Cone(s)), ("taper", Cylinder(s, 0.3, 1)) })
                foreach (double f in Targets) cases.Add(("primitives", $"{kind}{s}_x{f}", m, f * MeanEdge(m)));
        foreach (int e in new[] { 2, 3, 4, 6 }) {
            DMesh3 m = Box(e);
            foreach (double f in Targets) cases.Add(("primitives", $"box{e}_x{f}", m, f * MeanEdge(m)));
        }
        foreach (var (teeth, h, sub) in new[] { (12, 0.3, 1), (24, 0.2, 1), (40, 0.25, 1), (12, 0.3, 3), (24, 0.2, 3), (16, 0.25, 4) }) {
            DMesh3 m = Gear(teeth, h, sub);
            foreach (double f in new[] { 0.5, 1.0, 2.0 }) cases.Add(("gears", $"gear{teeth}_{sub}_x{f}", m, f * MeanEdge(m)));
        }
        foreach (int s in new[] { 16, 32, 64 })
            foreach (double w in new[] { 0.05, 0.1 }) {
                DMesh3 m = Tube(s, 4, 1, w, 3);
                foreach (double t in new[] { 0.5, 0.8, 1.2, 1.6 }) cases.Add(("tubes", $"tube{s}_w{w}_t{t}", m, t));
            }
        if (A.ContainsKey("thingi"))
            foreach (string f in System.IO.Directory.GetFiles(A["thingi"]).OrderBy(f => f, StringComparer.Ordinal)) {
                DMesh3 m = StandardMeshReader.ReadMesh(f);
                if (m != null && m.TriangleCount > 0)
                    cases.Add(("thingi", System.IO.Path.GetFileNameWithoutExtension(f), m, MeanEdge(m)));
            }

        Remesh(cases[0].input, cases[0].target, qualities[0], preventOn, pinAngle, sizing, out _, out _);      // warm-up (JIT)
        if (postBand > 0)
            return Post(cases.Where(c => only == null || only.IsMatch(c.name)).ToList(), qualities[0], runs);
        var json = new JsonSerializerOptions() { IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var rows = new List<(string group, string name, Result off, Result[] on)>();
        foreach (var c in cases) {
            if (only != null && !only.IsMatch(c.name)) continue;
            Result off = Measure(c.input, c.target, baseQuality, false, 0, 0, runs);
            bandOn = true;
            Result[] on = qualities.Select(q => Measure(c.input, c.target, q, preventOn, pinAngle, sizing, runs)).ToArray();
            bandOn = false;
            rows.Add((c.group, c.name, off, on));
            var line = new Dictionary<string, object>() {
                ["group"] = c.group, ["case"] = c.name, ["input_tris"] = c.input.TriangleCount, ["input_self_pairs"] = MeshRepairMetrics.SelfIntersections(c.input),
                ["off"] = off };
            for (int k = 0; k < qualities.Length; ++k)
                line[qualities.Length == 1 ? "on" : "on_" + qualities[k].ToString(CultureInfo.InvariantCulture)] = on[k];
            Console.WriteLine(JsonSerializer.Serialize(line, json));
        }
        for (int k = 0; k < qualities.Length; ++k) {
            Console.WriteLine();
            Console.WriteLine($"## MinTriangleQuality = {qualities[k].ToString(CultureInfo.InvariantCulture)}{(preventOn ? " with PreventSelfIntersections" + (preventFrom > 0 ? $" from pass {preventFrom}" : "") : "")}{(pinAngle > 0 ? $" with PinSharpVertices({pinAngle})" : "")}{(sizing > 0 ? $" with RemeshSizing.FromWallThickness({sizing})" : "")} (on) " +
                $"vs {baseQuality.ToString(CultureInfo.InvariantCulture)} (off)");
            Summary(rows.Select(r => (r.group, r.name, r.off, r.on[k])).ToList());
        }
        return 0;
    }

    // post=<f>: RemeshQualityPass after the "on" remesh, band f x the input's diagonal (one-sided; post_variant: a
    // comma list of twosided, free (corners not pinned), merge (strips between creases collapsed), c60 / c180 (crease angle),
    // nocrease (crease operations off)). Per case the min angle (worst and at the 1 % rank) before and after, the time of
    // the remesh and of the pass, the Hausdorff distance to the input, self-intersecting pairs and NotCleanReason before and
    // after, the operation counts, and the crease-sector bound (the smallest angle at the 1 % rank between two crease edges
    // of one vertex, the most any triangulation that keeps those creases can reach there)
    static double postBand = 0;
    // band=<f>: Remesher.MaxDeviation = f x the input's diagonal in the "on" runs (the band guard, CERTIFIED_REMESHING.md section 10);
    // the JSON then has the RemeshCertificate of the result (Cert*) and DeviationRejected
    static double remeshBand = 0;
    static int lastDeviationRejected;
    static bool bandOn;     // (the "on" runs only)
    static string postVariant = "";

    class PostResult
    {
        public int TrisBefore, TrisAfter, PairsBefore, PairsAfter;
        public double RemeshSeconds, PassSeconds, MinBefore, MinAfter, P01Before, P01After, HdBefore, HdAfter, SectorBound;
        public string CleanBefore, CleanAfter, Status;
        public bool StartWithinBand, ResultWithinBand;
        public double CoverageInit, CoverageCheck; public long CoverageCertified; public int Uncovered;
        public int Flips, Collapses, Splits, Relocations, CreaseInsertions, CreaseRemovals, CreaseRelocations, RejectedByBand, RejectedByIntersection, RejectedByAngle, Uncertified, StartUncertified;
    }

    static RemeshQualityPass MakePass(DMesh3 mesh, DMesh3 input, double band)
    {
        var v = new HashSet<string>(postVariant.Split(',', StringSplitOptions.RemoveEmptyEntries));
        return new RemeshQualityPass(mesh, input) {
            MaxDeviation = band, TwoSided = v.Contains("twosided"), PinCreaseCorners = !v.Contains("free"), MergeCreases = v.Contains("merge"),
            CreaseAngleDegrees = v.Contains("c60") ? 60 : v.Contains("c180") ? 180 : 30, EnableCreaseOperations = !v.Contains("nocrease"),
            CoverageDepth = v.Contains("cd6") ? 6 : v.Contains("cd4") ? 4 : 10,
            CoveragePieces = v.Contains("mp64") ? 64 : v.Contains("mp1k") ? 1024 : v.Contains("mpinf") ? int.MaxValue : 256 };
    }

    static int Post(List<(string group, string name, DMesh3 input, double target)> cases, double quality, int runs)
    {
        var json = new JsonSerializerOptions() { IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var rows = new List<(string group, string name, PostResult r)>();
        foreach (var c in cases) {
            var r = new PostResult();
            double diag = c.input.CachedBounds.DiagonalLength, band = postBand * diag;
            var remeshTimes = new List<double>(); var passTimes = new List<double>();
            DMesh3 remeshed = null, m = null; RemeshQualityResult q = null; RemeshQualityPass pass = null;
            for (int k = 0; k < runs; ++k) {
                GC.Collect(); GC.WaitForPendingFinalizers();
                var sw = Stopwatch.StartNew();
                remeshed = Remesh(c.input, c.target, quality, preventOn, 0, 0, out _, out _);
                remeshTimes.Add(sw.Elapsed.TotalSeconds);
                m = new DMesh3(remeshed);
                GC.Collect(); GC.WaitForPendingFinalizers();
                sw.Restart();
                pass = MakePass(m, c.input, band);
                q = pass.Compute();
                passTimes.Add(sw.Elapsed.TotalSeconds);
            }
            remeshTimes.Sort(); passTimes.Sort();
            r.RemeshSeconds = remeshTimes[runs / 2]; r.PassSeconds = passTimes[runs / 2];
            r.TrisBefore = remeshed.TriangleCount; r.TrisAfter = m.TriangleCount;
            r.MinBefore = q.MinAngleBefore; r.MinAfter = q.MinAngleAfter; r.P01Before = q.Worst1PercentBefore; r.P01After = q.Worst1PercentAfter;
            r.PairsBefore = MeshRepairMetrics.SelfIntersections(remeshed); r.PairsAfter = MeshRepairMetrics.SelfIntersections(m);
            r.CleanBefore = MeshRobustRepair.NotCleanReason(remeshed); r.CleanAfter = MeshRobustRepair.NotCleanReason(m);
            var h0 = new MeshHausdorffDistance(c.input, remeshed) { Tolerance = 0.001 * diag }; h0.Compute();
            var h1 = new MeshHausdorffDistance(c.input, m) { Tolerance = 0.001 * diag }; h1.Compute();
            r.HdBefore = h0.UpperBound / diag; r.HdAfter = h1.UpperBound / diag;
            r.Status = q.Status.ToString(); r.StartWithinBand = q.StartWithinBand; r.ResultWithinBand = q.ResultWithinBand;
            r.Flips = q.Flips; r.Collapses = q.Collapses; r.Splits = q.Splits; r.Relocations = q.Relocations; r.CreaseInsertions = q.CreaseInsertions;
            r.CreaseRemovals = q.CreaseRemovals; r.CreaseRelocations = q.CreaseRelocations; r.RejectedByBand = q.RejectedByBand;
            r.RejectedByIntersection = q.RejectedByIntersection; r.RejectedByAngle = q.RejectedByAngle;
            r.Uncertified = q.UncertifiedTriangles; r.StartUncertified = q.StartUncertifiedTriangles;
            r.SectorBound = SectorBound(m, pass);
            if (pass.Coverage != null) { r.CoverageInit = pass.Coverage.InitSeconds; r.CoverageCheck = pass.Coverage.CheckSeconds; r.CoverageCertified = pass.Coverage.Certified; r.Uncovered = pass.Coverage.Uncovered; }
            rows.Add((c.group, c.name, r));
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object>() { ["group"] = c.group, ["case"] = c.name, ["post"] = r }, json));
        }
        Console.WriteLine();
        Console.WriteLine($"## RemeshQualityPass after MinTriangleQuality = {quality.ToString(CultureInfo.InvariantCulture)}, band {postBand.ToString(CultureInfo.InvariantCulture)} x diagonal{(postVariant.Length > 0 ? ", " + postVariant : "")}");
        Console.WriteLine("| group | cases | p01 mean before / after | p01 median before / after | min angle lower after | new self-int. pairs (cases) | not clean before / after | result in band | sector bound < 20: cases, p01 mean of the others before / after | remesh s / pass s |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var g in rows.GroupBy(x => x.group)) {
            var l = g.Select(x => x.r).ToList();
            double med(IEnumerable<double> xs) { var a = xs.OrderBy(x => x).ToList(); return a.Count % 2 == 1 ? a[a.Count / 2] : 0.5 * (a[a.Count / 2 - 1] + a[a.Count / 2]); }
            var wide = l.Where(x => !(x.SectorBound < 20)).ToList();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "| {0} | {1} | {2:F2} / {3:F2} | {4:F2} / {5:F2} | {6} | {7} | {8} / {9} | {10} | {11}, {12:F2} / {13:F2} | {14:F1} / {15:F1} |",
                g.Key, l.Count, l.Average(x => x.P01Before), l.Average(x => x.P01After), med(l.Select(x => x.P01Before)), med(l.Select(x => x.P01After)),
                l.Count(x => x.MinAfter < x.MinBefore), l.Count(x => x.PairsAfter > x.PairsBefore), l.Count(x => x.CleanBefore != ""), l.Count(x => x.CleanAfter != ""),
                l.Count(x => x.ResultWithinBand), l.Count - wide.Count, wide.Count > 0 ? wide.Average(x => x.P01Before) : double.NaN, wide.Count > 0 ? wide.Average(x => x.P01After) : double.NaN,
                l.Sum(x => x.RemeshSeconds), l.Sum(x => x.PassSeconds)));
        }
        return 0;
    }

    // the smallest angle at the 1 % rank of the sectors at the vertices with two or more crease edges (per triangle the
    // smallest sector one of its corners lies in; 180 for a triangle at no such vertex)
    static double SectorBound(DMesh3 m, RemeshQualityPass pass)
    {
        double angle_at(int t, int v)
        {
            Index3i tv = m.GetTriangle(t);
            int o1 = tv.a == v ? tv.b : tv.a, o2 = tv.c == v ? tv.b : tv.c;
            Vector3d p = m.GetVertex(v);
            return Vector3d.AngleD((m.GetVertex(o1) - p).Normalized, (m.GetVertex(o2) - p).Normalized);
        }
        var bounds = new List<double>();
        foreach (int t in m.TriangleIndices()) {
            double b = 180;
            Index3i tv = m.GetTriangle(t);
            foreach (int v in new[] { tv.a, tv.b, tv.c }) {
                int n = 0;
                foreach (int x in m.VtxVerticesItr(v)) if (pass.IsCrease(v, x)) n++;
                if (n < 2) continue;
                var seen = new HashSet<int> { t };
                var stack = new Stack<int>(); stack.Push(t);
                double sum = 0;
                while (stack.Count > 0) {
                    int u = stack.Pop();
                    sum += angle_at(u, v);
                    Index3i uv = m.GetTriangle(u);
                    foreach (int x in new[] { uv.a, uv.b, uv.c }) {
                        if (x == v || pass.IsCrease(v, x)) continue;
                        Index2i et = m.GetEdgeT(m.FindEdge(v, x));
                        foreach (int w in new[] { et.a, et.b }) if (w >= 0 && seen.Add(w)) stack.Push(w);
                    }
                }
                b = Math.Min(b, sum);
            }
            bounds.Add(b);
        }
        bounds.Sort();
        return bounds.Count > 0 ? bounds[bounds.Count / 100] : double.NaN;
    }

    static bool fidelity = true, preventOn = false, sweep = false;
    static int sweepPasses, sweepCollisions, sweepTunnelled; static long sweepCandidates; static double sweepSeconds, sweepComputeSeconds;

    // sweep=1: checks the motion of each buffered move pass (the vertices go linearly from the old to the new position)
    class SweepRemesher : Remesher
    {
        public SweepRemesher(DMesh3 m) : base(m) { }
        protected override void ApplyVertexBuffer(bool bParallel)
        {
            var sw = Stopwatch.StartNew();
            int nv = Mesh.MaxVertexID;
            var before = new Vector3d[nv];
            for (int v = 0; v < nv; ++v) before[v] = Mesh.IsVertex(v) ? Mesh.GetVertex(v) : Vector3d.Zero;
            sweepSeconds += sw.Elapsed.TotalSeconds;
            base.ApplyVertexBuffer(bParallel);
            sw.Restart();
            var after = new Vector3d[nv];
            bool moved = false;
            for (int v = 0; v < nv; ++v) {
                after[v] = Mesh.IsVertex(v) ? Mesh.GetVertex(v) : Vector3d.Zero;
                if (Mesh.IsVertex(v) && !(after[v] == before[v])) moved = true;
            }
            if (moved) {
                DMesh3 start = new DMesh3(Mesh, false, false, false, false);
                for (int v = 0; v < nv; ++v) if (start.IsVertex(v)) start.SetVertex(v, before[v]);
                var sc = new MeshSweepCollision(start, after);
                var swc = Stopwatch.StartNew();
                sc.Compute();
                sweepComputeSeconds += swc.Elapsed.TotalSeconds;
                sweepPasses++; sweepCandidates += sc.CandidatePairCount;
                if (sc.Status != MeshSweepStatus.NoCollision) {
                    sweepCollisions++;
                    if (MeshRepairMetrics.SelfIntersections(start) == 0 && MeshRepairMetrics.SelfIntersections(Mesh) == 0)
                    {
                        sweepTunnelled++;
                        var c0 = sc.Contacts.Count > 0 ? sc.Contacts[0] : default;
                        Console.Error.WriteLine($"# tunnelled pass: status {sc.Status}, earliest ToI {sc.EarliestTimeOfImpact:G6}, contacts {sc.Contacts.Count}, first {c0.Kind} {c0.First}/{c0.Second} ToI {c0.Result.TimeOfImpact:G6}, tris {Mesh.TriangleCount}");
                    }
                }
            }
            sweepSeconds += sw.Elapsed.TotalSeconds;
        }
    }
    static double sizing = 0;
    static int preventFrom = 0;

    static readonly double[] Targets = { 0.1, 0.15, 0.2, 0.3, 0.45, 0.6 };

    static Result Measure(DMesh3 input, double target, double quality, bool prevent, double pinAngle, double sizingFraction, int runs)
    {
        var times = new List<double>();
        DMesh3 m = null;
        int rejected = 0, siRejected = 0;
        for (int k = 0; k < runs; ++k) {
            sweepPasses = sweepCollisions = sweepTunnelled = 0; sweepCandidates = 0; sweepSeconds = 0; sweepComputeSeconds = 0;
            GC.Collect(); GC.WaitForPendingFinalizers();
            var sw = Stopwatch.StartNew();
            m = Remesh(input, target, quality, prevent, pinAngle, sizingFraction, out rejected, out siRejected);
            times.Add(sw.Elapsed.TotalSeconds - sweepSeconds);
        }
        times.Sort();
        var r = new Result() { Tris = m.TriangleCount, Seconds = times[times.Count / 2], RejectedPasses = rejected, SelfIntersectionsRejected = siRejected,
            SweepPasses = sweepPasses, SweepCollisions = sweepCollisions, SweepTunnelled = sweepTunnelled, SweepCandidates = sweepCandidates, SweepSeconds = sweepSeconds, SweepComputeSeconds = sweepComputeSeconds };
        foreach (int t in m.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            double q = MeshRefinerBase.TriangleQuality(ref a, ref b, ref c);
            if (q < 0.1) r.Poor++;
            if (q < 1e-3) r.Flat++;
        }
        r.SelfPairs = MeshRepairMetrics.SelfIntersections(m);
        double v0 = signed_volume(input);
        if (input.IsClosed() && v0 != 0)
            r.VolumeRatio = signed_volume(m) / v0;
        var hd = new MeshHausdorffDistance(input, m) { Tolerance = 0.01 * target };
        hd.Compute();
        r.Hausdorff = hd.UpperBound / target;
        // directly: NotCleanReason stops at its first failed check (degenerate triangles come before inside-out)
        r.Inverted = MeshPostRepairCleanup.FindInvertedComponents(m).Count;
        r.NotClean = MeshRobustRepair.NotCleanReasons(m, true);
        if (remeshBand > 0) {
            double diag = input.CachedBounds.DiagonalLength;
            var cert = RemeshCertificate.Compute(input, m, remeshBand * diag);
            r.DeviationRejected = lastDeviationRejected;
            r.CertPassed = cert.Passed; r.CertWithinBand = cert.WithinBand; r.CertNesting = cert.NestingPreserved;
            r.CertFwd = cert.HausdorffResultToInput / diag; r.CertBwd = cert.HausdorffInputToResult / diag; r.CertTopology = cert.TopologyChanges.ToString();
        }
        if (fidelity) {
            var f = new MeshFidelityMetrics(input, m) { SurfaceSamples = 20000, VolumeSamples = 0, Curvature = false, RelativeThresholds = new[] { 0.001, 0.005 } };
            if (f.Compute()) {
                r.F0001 = f.FScore[0]; r.F0005 = f.FScore[1]; r.NC = f.NormalConsistency; r.ECD = f.EdgeChamfer;
                r.ER0005 = f.EdgeRecall[1]; r.MinAngle1 = f.MeshQuality.MinAngle.Worst1Percent; r.Degenerate = f.MeshQuality.Degenerate;
            }
        }
        return r;
    }

    // tools/g3cli/Commands.cs, remesh with its defaults
    static DMesh3 Remesh(DMesh3 input, double target, double quality, bool prevent, double pinAngle, double sizingFraction, out int rejectedPasses, out int siRejected)
    {
        DMesh3 mesh = new DMesh3(input);
        Remesher r = sweep ? new SweepRemesher(mesh) : new Remesher(mesh);
        r.SmoothSpeedT = 0.5; r.MinTriangleQuality = quality;
        r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
        MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
        if (pinAngle > 0)
            MeshConstraintUtil.PinSharpVertices(r, pinAngle);
        if (sizingFraction > 0)
            r.TargetEdgeLengthF = RemeshSizing.FromWallThickness(input, sizingFraction);     // (included in the time)
        if (remeshBand > 0 && bandOn)
            r.MaxDeviation = remeshBand * input.CachedBounds.DiagonalLength;
        r.SetTargetEdgeLength(target);
        for (int k = 0; k < 20; ++k) {
            r.PreventSelfIntersections = prevent && k >= preventFrom;
            r.BasicRemeshPass();
        }
        rejectedPasses = r.InvertingPassesRejected;      // RejectInvertingPasses (default on)
        lastDeviationRejected = r.DeviationRejected;
        siRejected = r.SelfIntersectionsRejected;
        return mesh;
    }

    static double signed_volume(DMesh3 m)
    {
        double v = 0;
        foreach (int t in m.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            v += a.Dot(b.Cross(c));
        }
        return v / 6;
    }

    static void Summary(List<(string group, string name, Result off, Result on)> rows)
    {
        Console.WriteLine();
        Console.WriteLine($"| group | cases | q<0.1 off / on | q<1e-3 off / on | cases with q<1e-3 off / on | self-intersecting pairs off / on | cases with self-int. off / on | cases inside-out off / on | cases with rejected passes off / on | mean hausdorff/target off / on | time s off / on | worse: more pairs / more q<1e-3 / hausdorff / inside-out |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var g in rows.GroupBy(r => r.group)) {
            var l = g.ToList();
            int morePairs = l.Count(r => r.on.SelfPairs > r.off.SelfPairs), moreFlat = l.Count(r => r.on.Flat > r.off.Flat);
            int worseHd = l.Count(r => r.on.Hausdorff > r.off.Hausdorff + 0.02);       // above the two tolerances of 0.01 target
            int newInverted = l.Count(r => r.on.Inverted > 0 && r.off.Inverted == 0);
            Console.WriteLine($"| {g.Key} | {l.Count} | {l.Sum(r => r.off.Poor)} / {l.Sum(r => r.on.Poor)} | {l.Sum(r => r.off.Flat)} / {l.Sum(r => r.on.Flat)} | " +
                $"{l.Count(r => r.off.Flat > 0)} / {l.Count(r => r.on.Flat > 0)} | {l.Sum(r => r.off.SelfPairs)} / {l.Sum(r => r.on.SelfPairs)} | " +
                $"{l.Count(r => r.off.SelfPairs > 0)} / {l.Count(r => r.on.SelfPairs > 0)} | {l.Count(r => r.off.Inverted > 0)} / {l.Count(r => r.on.Inverted > 0)} | " +
                $"{l.Count(r => r.off.RejectedPasses > 0)} / {l.Count(r => r.on.RejectedPasses > 0)} | " +
                $"{l.Average(r => r.off.Hausdorff):F3} / {l.Average(r => r.on.Hausdorff):F3} | " +
                $"{l.Sum(r => r.off.Seconds):F1} / {l.Sum(r => r.on.Seconds):F1} | {morePairs} / {moreFlat} / {worseHd} / {newInverted} |");
        }
        if (fidelity) {
            // means over the cases where both values are finite (Edge Chamfer is infinite and edge recall NaN when only
            // one or neither mesh has sharp edges): off and on over the same cases
            Console.WriteLine();
            Console.WriteLine("| group | F 0.1% off / on | F 0.5% off / on | normal consistency off / on | Edge Chamfer off / on | edge recall 0.5% off / on | min angle 1% worst off / on | cases with degenerate tris off / on | cases not clean off / on | worse: F 0.1% by > 0.01 / edge recall by > 0.02 |");
            Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (var g in rows.GroupBy(r => r.group)) {
                var l = g.ToList();
                string mean(Func<Result, double> f) {
                    var both = l.Where(r => double.IsFinite(f(r.off)) && double.IsFinite(f(r.on))).ToList();
                    var off = both.Select(r => f(r.off)).ToList();
                    var on = both.Select(r => f(r.on)).ToList();
                    return (off.Count > 0 ? off.Average().ToString("F4", CultureInfo.InvariantCulture) : "-") + " / " + (on.Count > 0 ? on.Average().ToString("F4", CultureInfo.InvariantCulture) : "-");
                }
                Console.WriteLine($"| {g.Key} | {mean(r => r.F0001)} | {mean(r => r.F0005)} | {mean(r => r.NC)} | {mean(r => r.ECD)} | {mean(r => r.ER0005)} | {mean(r => r.MinAngle1)} | " +
                    $"{l.Count(r => r.off.Degenerate > 0)} / {l.Count(r => r.on.Degenerate > 0)} | {l.Count(r => r.off.NotClean != "")} / {l.Count(r => r.on.NotClean != "")} | " +
                    $"{l.Count(r => r.on.F0001 < r.off.F0001 - 0.01)} / {l.Count(r => r.on.ER0005 < r.off.ER0005 - 0.02)} |");
            }
        }
        Console.WriteLine();
        Console.WriteLine("Cases that get worse with the option (more self-intersecting pairs, more q<1e-3, a Hausdorff distance more than 0.02 target larger, or inside-out only with it):");
        foreach (var r in rows.Where(r => r.on.SelfPairs > r.off.SelfPairs || r.on.Flat > r.off.Flat || r.on.Hausdorff > r.off.Hausdorff + 0.02 || (r.on.Inverted > 0 && r.off.Inverted == 0)))
            Console.WriteLine($"- {r.name}: pairs {r.off.SelfPairs} -> {r.on.SelfPairs}, q<1e-3 {r.off.Flat} -> {r.on.Flat}, hausdorff/target {r.off.Hausdorff:F3} -> {r.on.Hausdorff:F3}, inside-out components {r.off.Inverted} -> {r.on.Inverted}");
    }

    // ---- inputs ----

    static double MeanEdge(DMesh3 m)
    {
        return m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });
    }

    // closed thin-walled tube (outer radius R, wall w): outer and inner wall joined by annular rims at both ends
    static DMesh3 Tube(int slices, int rings, double R, double w, double H)
    {
        var m = new DMesh3();
        int[,] o = new int[rings + 1, slices], i = new int[rings + 1, slices];
        for (int r = 0; r <= rings; ++r)
            for (int s = 0; s < slices; ++s) {
                double a = 2 * Math.PI * s / slices, z = H * r / rings;
                o[r, s] = m.AppendVertex(new Vector3d(R * Math.Cos(a), R * Math.Sin(a), z));
                i[r, s] = m.AppendVertex(new Vector3d((R - w) * Math.Cos(a), (R - w) * Math.Sin(a), z));
            }
        for (int s = 0; s < slices; ++s) {
            int s1 = (s + 1) % slices;
            for (int r = 0; r < rings; ++r) {
                m.AppendTriangle(o[r, s], o[r, s1], o[r + 1, s1]); m.AppendTriangle(o[r, s], o[r + 1, s1], o[r + 1, s]);
                m.AppendTriangle(i[r, s], i[r + 1, s1], i[r, s1]); m.AppendTriangle(i[r, s], i[r + 1, s], i[r + 1, s1]);
            }
            m.AppendTriangle(o[rings, s], o[rings, s1], i[rings, s1]); m.AppendTriangle(o[rings, s], i[rings, s1], i[rings, s]);
            m.AppendTriangle(o[0, s], i[0, s1], o[0, s1]); m.AppendTriangle(o[0, s], i[0, s], i[0, s1]);
        }
        return m;
    }

    static DMesh3 Cylinder(int slices, double topRadius, double height) =>
        new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = (float)topRadius, Height = (float)height, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
    static DMesh3 Cone(int slices) =>
        new ConeGenerator() { BaseRadius = 1f, Height = 2f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
    static DMesh3 Box(int edgeVertices) =>
        new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.3)), EdgeVertices = edgeVertices, NoSharedVertices = false }.Generate().MakeDMesh();

    // an extruded gear: a CAD-like part with many feature curves and junctions, extra (collinear) points on the
    // flanks, and caps as fans around their centres (long thin triangles)
    static DMesh3 Gear(int teeth, double height, int pointsPerSide)
    {
        var poly = new List<Vector2d>();
        for (int k = 0; k < teeth; ++k) {
            double a0 = 2 * Math.PI * k / teeth, da = 2 * Math.PI / teeth;
            foreach (var (f, r) in new[] { (0.0, 0.8), (0.2, 1.0), (0.5, 1.0), (0.7, 0.8) })
                poly.Add(r * new Vector2d(Math.Cos(a0 + f * da), Math.Sin(a0 + f * da)));
        }
        var outline = new List<Vector2d>();
        for (int i = 0; i < poly.Count; ++i)
            for (int k = 0; k < pointsPerSide; ++k)
                outline.Add(Vector2d.Lerp(poly[i], poly[(i + 1) % poly.Count], (double)k / pointsPerSide));
        var m = new DMesh3();
        int n = outline.Count;
        int cb = m.AppendVertex(new Vector3d(0, 0, 0)), ct = m.AppendVertex(new Vector3d(0, 0, height));
        int[] b = outline.Select(p => m.AppendVertex(new Vector3d(p.x, p.y, 0))).ToArray();
        int[] t = outline.Select(p => m.AppendVertex(new Vector3d(p.x, p.y, height))).ToArray();
        for (int i = 0; i < n; ++i) {
            int j = (i + 1) % n;
            m.AppendTriangle(cb, b[j], b[i]);
            m.AppendTriangle(ct, t[i], t[j]);
            m.AppendTriangle(b[i], b[j], t[j]);
            m.AppendTriangle(b[i], t[j], t[i]);
        }
        return m;
    }
}
