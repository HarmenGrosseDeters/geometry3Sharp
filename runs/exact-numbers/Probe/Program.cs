// Probe of the exact-number study (runs/exact-numbers): runs ExactMeshBoolean on a MeshFuzzPilot dump (text) and
// prints phase times, allocations, GC and the PredicateStats counters as one JSON line per run.
//   dump <dump.txt> [--op n] [--repeat r] [--zero-below x]   (zero-below: coordinates with |v| < x become 0)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;

static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length >= 2 && args[0] == "dump") return Dump(args);
        if (args.Length >= 1 && args[0] == "micro") return Micro.Run(args.Skip(1).ToArray());
#if PATHSTATS
        string proto = Environment.GetEnvironmentVariable("EXACT_PROTO") ?? "";
        ExactPredicates.UseDyadic = proto.Contains("dyadic");
        ExactPredicates.UseScaledFilter = proto.Contains("filter");
#endif
        if (args.Length >= 3 && args[0] == "list") return RunList(args[1], args[2]);
        Console.Error.WriteLine("usage: dump <file> | micro ...");
        return 2;
    }

    public static void ParseDump(string path, out DMesh3 a, out DMesh3 b, out int op)
    {
        a = new DMesh3(); b = new DMesh3(); op = 0;
        DMesh3 cur = null;
        foreach (string raw in File.ReadAllLines(path)) {
            string l = raw.Trim();
            if (l.StartsWith("op ")) op = int.Parse(l.Split(' ')[1]);
            else if (l == "A vertices:") cur = a;
            else if (l == "B vertices:") cur = b;
            else if (l.StartsWith("A triangles:") || l.StartsWith("B triangles:")) {
                DMesh3 m = l[0] == 'A' ? a : b;
                foreach (string t in l.Substring(l.IndexOf(':') + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                    var p = t.Trim('(', ')').Split(',').Select(int.Parse).ToArray();
                    m.AppendTriangle(p[0], p[1], p[2]);
                }
                cur = null;
            } else if (cur != null && l.Contains(':')) {
                var p = l.Substring(l.IndexOf(':') + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                cur.AppendVertex(new Vector3d(p[0], p[1], p[2]));
            }
        }
    }

    static int Dump(string[] args)
    {
        int repeat = 1, opOverride = -1; double zeroBelow = 0; string record = null; int recN = 0, recEvery = 1;
        for (int i = 2; i < args.Length; ++i) {
            if (args[i] == "--repeat") repeat = int.Parse(args[++i]);
            else if (args[i] == "--op") opOverride = int.Parse(args[++i]);
            else if (args[i] == "--record") { record = args[++i]; recN = int.Parse(args[++i]); recEvery = int.Parse(args[++i]); }
            else if (args[i] == "--zero-below") zeroBelow = double.Parse(args[++i], CultureInfo.InvariantCulture);
        }
        ParseDump(args[1], out DMesh3 a, out DMesh3 b, out int op);
        if (opOverride >= 0) op = opOverride;
        if (zeroBelow > 0)
            foreach (DMesh3 m in new[] { a, b })
                foreach (int v in m.VertexIndices()) {
                    Vector3d p = m.GetVertex(v);
                    for (int k = 0; k < 3; ++k) if (Math.Abs(p[k]) < zeroBelow) p[k] = 0;
                    m.SetVertex(v, p);
                }
        Warm();
#if PATHSTATS
        if (record != null) PathStats.StartRecord(recN, recEvery);
#endif
        for (int r = 0; r < repeat; ++r)
            Console.WriteLine(JsonSerializer.Serialize(RunBoolean(Path.GetFileName(args[1]), a, b, op)));
#if PATHSTATS
        if (record != null) {
            int n = PathStats.RecCount;
            using (var w = new BinaryWriter(File.Create(record))) {
                w.Write(n);
                for (int i = 0; i < n * PathStats.RecStride; ++i) w.Write(PathStats.Rec[i]);
            }
            Console.Error.WriteLine("recorded " + n);
        }
#endif
        return 0;
    }

    static DMesh3 Load(string meshFile)
    {
        DMesh3Builder builder = new DMesh3Builder();
        IOReadResult rr = StandardMeshReader.ReadFile(meshFile, ReadOptions.Defaults, builder);
        if (rr.code != IOCode.Ok || builder.Meshes.Count == 0) return null;
        DMesh3 a = builder.Meshes[0];
        for (int i = 1; i < builder.Meshes.Count; ++i) MeshEditor.Append(a, builder.Meshes[i]);
        return a;
    }

    static void Normalize(DMesh3 m, Quaterniond rotation, Vector3d translation)
    {
        AxisAlignedBox3d bounds = m.CachedBounds;
        Vector3d c = bounds.Center;
        double s = 1.0 / bounds.MaxDim;
        foreach (int vid in m.VertexIndices())
            m.SetVertex(vid, rotation * ((m.GetVertex(vid) - c) * s) + translation);
    }

    // list <cases.txt> <out.jsonl>: one case per line, resumable (rows already in out are skipped):
    //   copy <name> <mesh> <ops>                         B = A rotated 20 degrees about (1,1,0) and shifted by a quarter box (class thingi10k)
    //   pair <name> <meshA> <meshB> qx qy qz qw tx ty tz <ops>   the thingi-pairs protocol (unit box, B rotated and shifted)
    //   dump <name> <dump.txt> <ops>                     a MeshFuzzPilot dump
    // <ops>: comma separated 0 = union, 1 = intersection, 2 = difference
    static int RunList(string listFile, string outFile)
    {
        var done = new HashSet<string>();
        if (File.Exists(outFile))
            foreach (string l in File.ReadLines(outFile)) {
                try { var d = JsonDocument.Parse(l).RootElement; done.Add(d.GetProperty("case").GetString() + "/" + d.GetProperty("op").GetInt32()); } catch { }
            }
        Warm();
        foreach (string raw in File.ReadAllLines(listFile)) {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string name = f[1];
            int[] ops = f[f.Length - 1].Split(',').Select(int.Parse).ToArray();
            if (ops.All(o => done.Contains(name + "/" + o))) continue;
            DMesh3 a = null, b = null;
            try {
                if (f[0] == "copy") {
                    a = Load(f[2]);
                    if (a != null) {
                        b = new DMesh3(a);
                        AxisAlignedBox3d bounds = a.CachedBounds;
                        MeshTransforms.Rotate(b, bounds.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
                        MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
                    }
                } else if (f[0] == "pair") {
                    a = Load(f[2]); b = Load(f[3]);
                    if (a != null && b != null) {
                        double[] p = f.Skip(4).Take(7).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                        Quaterniond q = new Quaterniond(p[0], p[1], p[2], p[3]);
                        q.Normalize();
                        Normalize(a, Quaterniond.Identity, Vector3d.Zero);
                        Normalize(b, q, new Vector3d(p[4], p[5], p[6]));
                    }
                } else if (f[0] == "dump") {
                    ParseDump(f[2], out a, out b, out int _);
                }
            } catch (Exception e) { Console.Error.WriteLine(name + ": " + e.Message); }
            if (a == null || b == null) { File.AppendAllText(outFile, JsonSerializer.Serialize(new Dictionary<string, object> { ["case"] = name, ["op"] = -1, ["error"] = "load failed" }) + "\n"); continue; }
            foreach (int op in ops) {
                if (done.Contains(name + "/" + op)) continue;
                Console.Error.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + name + " op " + op);
                Dictionary<string, object> row;
                try { row = RunBoolean(name, a, b, op); row["kind"] = f[0]; }
                catch (Exception e) { row = new Dictionary<string, object> { ["case"] = name, ["op"] = op, ["error"] = e.GetType().Name + ": " + e.Message }; }
                File.AppendAllText(outFile, JsonSerializer.Serialize(row) + "\n");
            }
        }
        return 0;
    }

    // a hash of the result mesh: vertices (bits of the doubles) and triangles in id order
    static string Hash(DMesh3 m)
    {
        if (m == null) return "";
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var ms = new MemoryStream()) {
            var w = new BinaryWriter(ms);
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); w.Write(p.x); w.Write(p.y); w.Write(p.z); }
            foreach (int t in m.TriangleIndices()) { Index3i x = m.GetTriangle(t); w.Write(x.a); w.Write(x.b); w.Write(x.c); }
            w.Flush();
            return Convert.ToHexString(sha.ComputeHash(ms.ToArray())).Substring(0, 16);
        }
    }

    public static void Warm()
    {
        DMesh3 w1 = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        DMesh3 w2 = new DMesh3(w1);
        MeshTransforms.Translate(w2, new Vector3d(0.3, 0.2, 0.1));
        for (int k = 0; k < 3; ++k)
            for (int o = 0; o < 3; ++o)
                new ExactMeshBoolean(w1, w2, (ExactMeshBoolean.Operations)o).Compute();
    }

    public static Dictionary<string, object> RunBoolean(string name, DMesh3 a, DMesh3 b, int op)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        PredicateStats.Reset();
#if PATHSTATS
        PathStats.Reset();
#endif
        long alloc0 = GC.GetTotalAllocatedBytes(true);
        TimeSpan cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
        TimeSpan gc0 = GC.GetTotalPauseDuration();
        int g0 = GC.CollectionCount(0), g2 = GC.CollectionCount(2);
        var sw = Stopwatch.StartNew();
        var bo = new ExactMeshBoolean(a, b, (ExactMeshBoolean.Operations)op);
        bool ok = bo.Compute();
        sw.Stop();
        var row = new Dictionary<string, object> {
            ["case"] = name, ["op"] = op, ["ok"] = ok, ["status"] = bo.Status.ToString(), ["ms"] = sw.ElapsedMilliseconds,
            ["cpu_ms"] = (long)(Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds,
            ["tris_a"] = a.TriangleCount, ["tris_b"] = b.TriangleCount,
            ["peak_ws_mb"] = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576,
            ["alloc_mb"] = (GC.GetTotalAllocatedBytes(true) - alloc0) / 1048576.0,
            ["gc_pause_ms"] = (GC.GetTotalPauseDuration() - gc0).TotalMilliseconds,
            ["gen0"] = GC.CollectionCount(0) - g0, ["gen2"] = GC.CollectionCount(2) - g2,
            ["rounding_clean"] = bo.RoundingClean,
            ["hash"] = Hash(bo.Result),
            ["proto"] = Environment.GetEnvironmentVariable("EXACT_PROTO") ?? "",
            ["result_tris"] = bo.Result != null ? bo.Result.TriangleCount : -1,
            ["phases_ms"] = bo.PhaseMilliseconds.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value),
            ["counts"] = bo.Counts,
        };
        var st = new Dictionary<string, long[]>();
        for (int k = 0; k < (int)PredicateStats.Kind.Count; ++k)
            if (PredicateStats.Calls(k) != 0 || PredicateStats.ExactStages(k) != 0)
                st[PredicateStats.Names[k]] = new[] { PredicateStats.Calls(k), PredicateStats.ExactStages(k) };
        row["predicates"] = st;
#if PATHSTATS
        var ps = new Dictionary<string, double[]>();
        for (int k = 0; k < (int)PathStats.P.Count; ++k)
            if (PathStats.Calls(k) != 0)
                ps[PathStats.Names[k]] = new[] { PathStats.Calls(k), Math.Round(PathStats.Ms(k), 1), Math.Round(PathStats.Bytes(k) / 1048576.0, 2) };
        row["paths"] = ps;      // calls, ms summed over threads (includes the timer overhead: calls * pair_ns), MB allocated inside
        row["pair_ns"] = Math.Round(PathStats.PairOverheadNs(), 1);
#endif
        return row;
    }
}
