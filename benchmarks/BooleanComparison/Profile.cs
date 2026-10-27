// Profile of ExactMeshBoolean: phase times, allocations, GC, counts, filter failures of the predicates and a hash of
// the result, per case and operation (one JSON line each). For before/after comparisons of optimizations: the
// hashes must stay the same (bit-identical results).
//   profile [options] <case>...
//     <case>: a directory with A.off and B.off (export / pair), a text file listing such directories (one per
//             line), or grid:<n> / spheres:<k> (the one-pass n-ary boolean of `nary`)
//     --ops union,intersection,difference   operations of the pair cases (default all three)
//     --repeat <r>                           timed runs per case and operation (default 3); reports all times,
//                                            the phases of the fastest run
//     --exact                                also hash the exact result vertices (ComputeExactVertices; slower)
//     --safe-rounding                        SafeRounding = true
// Filter failures are counted only by a library built with -p:G3PredicateStats=true (PredicateStats); in a normal
// build they are absent. The process uses all cores; run it under `taskset -c 0` (and DOTNET_PROCESSOR_COUNT=1)
// for one core.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using g3;

static class Profile
{
    public static int Run(string[] args)
    {
        var ops = new List<string> { "union", "intersection", "difference" };
        int repeat = 3;
        bool exact = false, safe = false;
        var cases = new List<string>();
        for (int i = 0; i < args.Length; ++i) {
            if (args[i] == "--ops") ops = args[++i].Split(',').ToList();
            else if (args[i] == "--repeat") repeat = int.Parse(args[++i]);
            else if (args[i] == "--exact") exact = true;
            else if (args[i] == "--safe-rounding") safe = true;
            else if (File.Exists(args[i]) && Directory.Exists(args[i]) == false)
                cases.AddRange(File.ReadAllLines(args[i]).Select(l => l.Trim()).Where(l => l.Length > 0 && l.StartsWith("#") == false));
            else cases.Add(args[i]);
        }
        var opOf = new Dictionary<string, ExactMeshBoolean.Operations> {
            ["union"] = ExactMeshBoolean.Operations.Union, ["intersection"] = ExactMeshBoolean.Operations.Intersection,
            ["difference"] = ExactMeshBoolean.Operations.Difference };

        // warm-up (JIT), as in `run`
        DMesh3 w1 = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        DMesh3 w2 = new DMesh3(w1);
        MeshTransforms.Translate(w2, new Vector3d(0.3, 0.2, 0.1));
        for (int k = 0; k < 3; ++k)
            foreach (var o in opOf.Values)
                new ExactMeshBoolean(w1, w2, o) { ComputeExactVertices = exact, SafeRounding = safe }.Compute();

        foreach (string c in cases) {
            List<KeyValuePair<string, Func<ExactMeshBoolean>>> runs = new List<KeyValuePair<string, Func<ExactMeshBoolean>>>();
            if (c.StartsWith("grid:") || c.StartsWith("spheres:")) {
                string kind = c.Substring(0, c.IndexOf(':'));
                int n = int.Parse(c.Substring(c.IndexOf(':') + 1));
                List<DMesh3> cubes, voids; Func<int[], bool> predicate; double expected;
                Nary.Inputs(kind, n, out cubes, out voids, out predicate, out expected);
                var all = cubes.Concat(voids).ToList();
                runs.Add(new KeyValuePair<string, Func<ExactMeshBoolean>>("onepass", () => new ExactMeshBoolean(all, predicate)));
            } else {
                DMesh3 a = StandardMeshReader.ReadMesh(Path.Combine(c, "A.off")), b = StandardMeshReader.ReadMesh(Path.Combine(c, "B.off"));
                if (a == null || b == null) {
                    Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["case"] = c, ["error"] = "load failed" }));
                    continue;
                }
                foreach (string op in ops) {
                    var o = opOf[op];
                    runs.Add(new KeyValuePair<string, Func<ExactMeshBoolean>>(op, () => new ExactMeshBoolean(a, b, o)));
                }
            }
            foreach (var run in runs) {
                var times = new List<long>();
                Dictionary<string, object> best = null;
                long bestMs = long.MaxValue;
                var hashes = new HashSet<string>();
                for (int r = 0; r < repeat; ++r) {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    PredicateStats.Reset();
                    int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                    TimeSpan pause0 = GC.GetTotalPauseDuration();
                    long alloc0 = GC.GetTotalAllocatedBytes(true);
                    var boolean = run.Value();
                    boolean.ComputeExactVertices = exact;
                    boolean.SafeRounding = safe;
                    var sw = Stopwatch.StartNew();
                    bool ok;
                    string error = null;
                    try {
                        ok = boolean.Compute();
                        error = boolean.Error;
                    } catch (Exception e) {
                        ok = false;
                        error = e.GetType().Name + ": " + e.Message;
                    }
                    long ms = sw.ElapsedMilliseconds;
                    times.Add(ms);
                    string hash = ok ? Hash(boolean.Result) : "-";
                    if (ok && exact)
                        hash += "/" + HashExact(boolean.ResultExactVertices);
                    hashes.Add(hash);
                    if (ms >= bestMs)
                        continue;
                    bestMs = ms;
                    var stats = new Dictionary<string, object>();
                    if (PredicateStats.Enabled)
                        for (int k = 0; k < (int)PredicateStats.Kind.Count; ++k)
                            if (PredicateStats.Calls(k) > 0 || PredicateStats.ExactStages(k) > 0)
                                stats[PredicateStats.Names[k]] = new[] { PredicateStats.Calls(k), PredicateStats.ExactStages(k) };
                    best = new Dictionary<string, object> {
                        ["case"] = c, ["op"] = run.Key, ["ok"] = ok, ["status"] = boolean.Status.ToString(),
                        ["cores"] = Environment.ProcessorCount,
                        ["phases_ms"] = boolean.PhaseMilliseconds,
                        ["phases_alloc_mb"] = boolean.PhaseAllocatedBytes.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / 1048576.0, 1)),
                        ["alloc_mb"] = Math.Round((GC.GetTotalAllocatedBytes(true) - alloc0) / 1048576.0, 1),
                        ["gc"] = new Dictionary<string, object> { ["gen0"] = GC.CollectionCount(0) - gc0, ["gen1"] = GC.CollectionCount(1) - gc1,
                            ["gen2"] = GC.CollectionCount(2) - gc2, ["pause_ms"] = (long)(GC.GetTotalPauseDuration() - pause0).TotalMilliseconds },
                        ["counts"] = boolean.Counts,
                        ["triangles"] = ok ? boolean.Result.TriangleCount : -1,
                    };
                    if (stats.Count > 0)
                        best["predicates"] = stats;
                    if (error != null)
                        best["error"] = error;
                }
                best["ms"] = times;
                best["hash"] = string.Join(" ", hashes);
                best["deterministic"] = hashes.Count == 1;
                Console.WriteLine(JsonSerializer.Serialize(best));
            }
        }
        return 0;
    }

    // vertex coordinates (bits) and triangles, in the order of the mesh
    static string Hash(DMesh3 mesh)
    {
        var sb = new StringBuilder();
        foreach (int vid in mesh.VertexIndices()) {
            Vector3d v = mesh.GetVertex(vid);
            sb.Append(vid).Append(':').Append(BitConverter.DoubleToInt64Bits(v.x)).Append(',')
                .Append(BitConverter.DoubleToInt64Bits(v.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(v.z)).Append(';');
        }
        foreach (int tid in mesh.TriangleIndices()) {
            Index3i t = mesh.GetTriangle(tid);
            sb.Append(tid).Append(':').Append(t.a).Append(',').Append(t.b).Append(',').Append(t.c).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).Substring(0, 16);
    }

    static string HashExact(ExactPoint3[] v)
    {
        if (v == null)
            return "-";
        var sb = new StringBuilder();
        foreach (ExactPoint3 p in v)
            sb.Append(p.X).Append(',').Append(p.Y).Append(',').Append(p.Z).Append(',').Append(p.W).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).Substring(0, 16);
    }
}
