// Exact Boolean 2.0 final run: the three operations of one #385 case (A.off, B.off, info.json from BooleanComparison
// export), each timed on its own instance, then checked. One JSON line per operation.
//   FinalRunner <case dir> <id> <class> [--ref <rows.jsonl>] [--no-eval]
//   --ref: rows of a reference build for this id (one per operation, with "hash" and "reason"): where the result's hash
//          equals the reference's, its reason is taken over ("reason_from": "ref") instead of evaluated again
//   --no-eval: no NotCleanReason at all (timing runs)
// Compiles against the library of every measured build (master and the #385 baseline cd7fb8ab): fields that the
// baseline lacks are read by reflection.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using g3;
using gs;

static class Program
{
    static DMesh3 Load(string path)
    {
        DMesh3Builder builder = new DMesh3Builder();
        IOReadResult r = new StandardMeshReader() { MeshBuilder = builder }.Read(path, ReadOptions.Defaults);
        if (r.code != IOCode.Ok || builder.Meshes.Count == 0)
            throw new IOException("load failed: " + path + " " + r.message);
        DMesh3 m = builder.Meshes[0];
        for (int i = 1; i < builder.Meshes.Count; ++i)
            MeshEditor.Append(m, builder.Meshes[i]);
        return m;
    }

    static object Field(object o, string name)
    {
        var f = o.GetType().GetField(name);
        if (f != null) return f.GetValue(o);
        var p = o.GetType().GetProperty(name);
        return p?.GetValue(o);
    }

    // JIT and thread pool warm-up on two small overlapping spheres (not measured)
    static void WarmUp()
    {
        var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6, Radius = 1 };
        DMesh3 a = g.Generate().MakeDMesh();
        DMesh3 b = new DMesh3(a);
        MeshTransforms.Translate(b, new Vector3d(0.5, 0.3, 0.2));
        foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference }) {
            var w = new ExactMeshBoolean(a, b, op);
            w.Compute();
            if (w.Result != null) MeshRobustRepair.NotCleanReason(w.Result);
        }
    }

    // FNV-1a over the vertex coordinates (bits, in vertex id order) and the triangles: equal results, equal hashes
    // the evaluator's self-intersecting pairs (by vertex index) and how many of them still cross with the vertices
    // welded by position: pairs that only touch where a position has several vertices (Contacts, where solids touch)
    // do not. verdict "contacts": every pair is such a touch (owner 2026-11-10: counted apart from unclean;
    // NotCleanReason unchanged)
    static void contact_check(DMesh3 m, Dictionary<string, object> row)
    {
        var first = new Dictionary<Vector3d, int>();
        var weld = new int[m.MaxVertexID];
        var shared = new HashSet<int>();
        foreach (int v in m.VertexIndices()) {
            Vector3d p = m.GetVertex(v);
            if (first.TryGetValue(p, out int w) == false)
                first[p] = w = v;
            else
                shared.Add(w);
            weld[v] = w;
        }
        List<Index2i> pairs = MeshRepairMetrics.SelfIntersectionPairs(m);
        int crossing = 0;
        foreach (Index2i pr in pairs) {
            Index3i ta = m.GetTriangle(pr.a), tb = m.GetTriangle(pr.b);
            var A = new Triangle3d(m.GetVertex(ta.a), m.GetVertex(ta.b), m.GetVertex(ta.c));
            var B = new Triangle3d(m.GetVertex(tb.a), m.GetVertex(tb.b), m.GetVertex(tb.c));
            if (ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(weld[ta.a], weld[ta.b], weld[ta.c]), ref A,
                    new Index3i(weld[tb.a], weld[tb.b], weld[tb.c]), ref B))
                crossing++;
        }
        // (the positions with several vertices counted here, not the boolean's Contacts: older builds lack it)
        row["si_pairs"] = pairs.Count;
        row["si_crossing"] = crossing;
        row["shared_positions"] = shared.Count;
        row["verdict"] = pairs.Count > 0 && crossing == 0 && shared.Count > 0 ? "contacts" : "self-intersections";
    }

    static string Hash(DMesh3 m)
    {
        ulong h = 14695981039346656037UL;
        void add(ulong x) { for (int k = 0; k < 8; ++k) { h ^= (x >> (8 * k)) & 0xFF; h *= 1099511628211UL; } }
        add((ulong)m.MaxVertexID); add((ulong)m.MaxTriangleID);
        for (int v = 0; v < m.MaxVertexID; ++v) {
            if (m.IsVertex(v) == false) { add(ulong.MaxValue); continue; }
            Vector3d p = m.GetVertex(v);
            add((ulong)BitConverter.DoubleToInt64Bits(p.x)); add((ulong)BitConverter.DoubleToInt64Bits(p.y)); add((ulong)BitConverter.DoubleToInt64Bits(p.z));
        }
        for (int t = 0; t < m.MaxTriangleID; ++t) {
            if (m.IsTriangle(t) == false) { add(ulong.MaxValue); continue; }
            Index3i tv = m.GetTriangle(t);
            add((ulong)(uint)tv.a | ((ulong)(uint)tv.b << 32)); add((ulong)(uint)tv.c);
        }
        return h.ToString("X16");
    }

    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string dir = args[0], id = args[1], cls = args[2];
        bool noEval = args.Contains("--no-eval");
        var reference = new Dictionary<string, JsonElement>();
        int ri = Array.IndexOf(args, "--ref");
        if (ri >= 0 && ri + 1 < args.Length && File.Exists(args[ri + 1]))
            foreach (string line in File.ReadLines(args[ri + 1])) {
                if (line.Trim().Length == 0) continue;
                JsonElement e = JsonDocument.Parse(line).RootElement;
                if (e.TryGetProperty("op", out JsonElement o) && e.TryGetProperty("hash", out _) && e.TryGetProperty("reason", out _))
                    reference[o.GetString()] = e;
            }
        DMesh3 A = Load(Path.Combine(dir, "A.off")), B = Load(Path.Combine(dir, "B.off"));
        WarmUp();
        foreach (var (name, op) in new[] { ("union", ExactMeshBoolean.Operations.Union), ("intersection", ExactMeshBoolean.Operations.Intersection),
            ("difference", ExactMeshBoolean.Operations.Difference) }) {
            var row = new Dictionary<string, object> { ["id"] = id, ["class"] = cls, ["op"] = name, ["cores"] = Environment.ProcessorCount };
            try {
                var b = new ExactMeshBoolean(A, B, op);
                var sw = Stopwatch.StartNew();
                bool ok = b.Compute();
                sw.Stop();
                row["ms"] = sw.Elapsed.TotalMilliseconds;
                row["ok"] = ok;
                row["status"] = b.Status.ToString();
                row["pieces"] = b.Counts.TryGetValue("pieces", out long pieces) ? pieces : -1;
                row["rounding_ms"] = b.PhaseMilliseconds.TryGetValue("rounding", out long rms) ? rms : -1;
                row["phases_ms"] = b.PhaseMilliseconds;
                row["rounding_clean"] = b.RoundingClean;
                row["contacts"] = Field(b, "Contacts") ?? -1;
                if (b.Rounding != null) row["rounding_limit"] = b.Rounding.EvaluationLimitReached;
                if (ok && b.Result != null) {
                    row["triangles"] = b.Result.TriangleCount;
                    string hash = Hash(b.Result);
                    row["hash"] = hash;
                    if (reference.TryGetValue(name, out JsonElement refRow) && refRow.GetProperty("hash").GetString() == hash) {
                        row["reason"] = refRow.GetProperty("reason").GetString();
                        row["reason_from"] = "ref";
                        foreach (string k in new[] { "si_pairs", "si_crossing", "shared_positions", "verdict" })
                            if (refRow.TryGetProperty(k, out JsonElement x))
                                row[k] = x.ValueKind == JsonValueKind.Number ? (object)x.GetInt64() : x.GetString();
                    } else if (noEval == false) {
                        var ev = Stopwatch.StartNew();
                        // the #385 evaluator's verdict: "" (clean), "empty", or the first defect of NotCleanReason
                        string reason = MeshRobustRepair.NotCleanReason(b.Result);
                        row["reason"] = reason;
                        if (reason == "self-intersections")
                            contact_check(b.Result, row);
                        row["eval_ms"] = ev.ElapsedMilliseconds;
                    }
                    row["finite"] = b.Result.Vertices().All(v => Math.Abs(v.x) <= double.MaxValue && Math.Abs(v.y) <= double.MaxValue && Math.Abs(v.z) <= double.MaxValue);
                }
            } catch (Exception e) {
                row["exception"] = e.GetType().Name + ": " + e.Message;
            }
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        return 0;
    }
}
