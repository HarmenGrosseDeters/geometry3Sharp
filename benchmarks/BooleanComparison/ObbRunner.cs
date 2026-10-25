// Runner for the Open Boolean Benchmark protocol (https://github.com/Open-Boolean-Benchmark/boolean-benchmark-runners,
// docs/Runner IO Contract.md): reads request.json, runs the SSA operation lists of all runs with
// ExactMeshBoolean, writes result.json (flushed every 10 s through a temporary file and a rename).
//   obb --request <request.json> --result <result.json> [--write-last-only]
// --write-last-only writes only the result of the last operation of each run (the protocol writes all of them),
// for long sequences. Timings: io_ms = file to DMesh3 (StandardMeshReader, which builds the native mesh
// directly, so import_ms = 0); operation_ms = ExactMeshBoolean.Compute(), whose result is a DMesh3 already
// (export_ms = 0); the OBJ write is only in debug_total_ms.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using g3;

static class ObbRunner
{
    public static int Run(string[] args)
    {
        string request = null, result = null;
        bool lastOnly = false;
        for (int i = 0; i < args.Length; ++i) {
            if (args[i] == "--request") request = args[++i];
            else if (args[i] == "--result") result = args[++i];
            else if (args[i] == "--write-last-only") lastOnly = true;
        }
        if (request == null || result == null) {
            Console.Error.WriteLine("usage: obb --request <request.json> --result <result.json> [--write-last-only]");
            return 2;
        }
        JsonElement req = JsonDocument.Parse(File.ReadAllText(request)).RootElement;
        Warmup();
        var runs = new List<Dictionary<string, object>>();
        var output = new Dictionary<string, object> {
            ["kind"] = req.GetProperty("kind").GetString(), ["version"] = req.GetProperty("version").GetInt32(),
            ["id"] = req.GetProperty("id").GetString(), ["runs"] = runs };
        bool allOk = true;
        var sinceFlush = Stopwatch.StartNew();
        foreach (JsonElement run in req.GetProperty("runs").EnumerateArray()) {
            var r = RunOne(run, lastOnly, () => {
                if (sinceFlush.ElapsedMilliseconds > 10000) { Flush(output, result); sinceFlush.Restart(); }
            }, runs);
            allOk &= (string)r["status"] == "success";
        }
        Flush(output, result);
        return allOk ? 0 : 1;
    }

    // the result of one run; it is added to runs before the operations execute, so that a flush during a long
    // run already contains the operations done so far
    static Dictionary<string, object> RunOne(JsonElement run, bool lastOnly, Action flush, List<Dictionary<string, object>> runs)
    {
        var sw = Stopwatch.StartNew();
        string outDir = run.TryGetProperty("out_dir", out JsonElement od) ? od.GetString() : null;
        JsonElement[] ops = run.GetProperty("operations").EnumerateArray().ToArray();
        var results = new List<Dictionary<string, object>>();
        var r = new Dictionary<string, object> { ["case_id"] = run.GetProperty("case_id").GetString(), ["status"] = "crash", ["duration_ms"] = 0.0, ["ops"] = results };
        lock (runs) runs.Add(r);
        // free each slot after its last use: long sequences keep only the meshes that are still needed
        var lastUse = new int[ops.Length];
        for (int i = 0; i < ops.Length; ++i) {
            lastUse[i] = i;
            if (ops[i].TryGetProperty("args", out JsonElement a))
                foreach (JsonElement x in a.EnumerateArray())
                    lastUse[x.GetInt32()] = i;
        }
        var slots = new DMesh3[ops.Length];
        string status = "success";
        for (int i = 0; i < ops.Length && status == "success"; ++i) {
            var o = new Dictionary<string, object>();
            results.Add(o);
            var total = Stopwatch.StartNew();
            string op = ops[i].GetProperty("op").GetString();
            try {
                if (op == "load-mesh") {
                    var t = Stopwatch.StartNew();
                    DMesh3 m = StandardMeshReader.ReadMesh(ops[i].GetProperty("path").GetString());
                    o["io_ms"] = t.Elapsed.TotalMilliseconds;
                    o["import_ms"] = 0.0;
                    if (m == null) { status = "invalid_input"; o["error"] = "could not read the mesh"; }
                    slots[i] = m;
                } else if (op.StartsWith("boolean-")) {
                    int[] a = ops[i].GetProperty("args").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                    var kind = op == "boolean-union" ? ExactMeshBoolean.Operations.Union
                        : op == "boolean-intersection" ? ExactMeshBoolean.Operations.Intersection
                        : op == "boolean-difference" ? ExactMeshBoolean.Operations.Difference : (ExactMeshBoolean.Operations)(-1);
                    if ((int)kind < 0 || a.Length < 1 || a.Length > 2) {
                        status = "unsupported"; o["error"] = a.Length > 2 ? "variadic booleans are not supported" : "unknown operation " + op;
                    } else {
                        // one argument: the self-operation (regularization), with an empty second mesh
                        DMesh3 second = a.Length == 2 ? slots[a[1]] : (kind == ExactMeshBoolean.Operations.Difference ? slots[a[0]] : new DMesh3());
                        var t = Stopwatch.StartNew();
                        var b = new ExactMeshBoolean(slots[a[0]], second, kind);
                        bool ok = b.Compute();
                        o["operation_ms"] = t.Elapsed.TotalMilliseconds;
                        o["export_ms"] = 0.0;
                        if (ok) slots[i] = b.Result;
                        else { status = b.Error != null && b.Error.Contains("input") ? "invalid_input" : "crash"; o["error"] = b.Error; }
                    }
                } else {
                    status = "unsupported"; o["error"] = "unknown operation " + op;
                }
            } catch (Exception ex) {
                status = "crash"; o["error"] = ex.GetType().Name + ": " + ex.Message;
            }
            if (status == "success" && string.IsNullOrEmpty(outDir) == false && (lastOnly == false || i == ops.Length - 1)) {
                string file = Path.Combine(outDir, $"op_{i}.obj");
                WriteOBJ(slots[i], file);
                o["file"] = file;
            }
            o["status"] = status;
            o["debug_total_ms"] = total.Elapsed.TotalMilliseconds;
            if (ops[i].TryGetProperty("args", out JsonElement used))
                foreach (JsonElement x in used.EnumerateArray())
                    if (lastUse[x.GetInt32()] == i) slots[x.GetInt32()] = null;
            flush();
        }
        r["status"] = status;
        r["duration_ms"] = sw.Elapsed.TotalMilliseconds;
        return r;
    }

    // a small problem first, so that the times do not include the JIT compilation
    static void Warmup()
    {
        DMesh3 w1 = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        DMesh3 w2 = new DMesh3(w1);
        MeshTransforms.Translate(w2, new Vector3d(0.3, 0.2, 0.1));
        foreach (ExactMeshBoolean.Operations o in Enum.GetValues(typeof(ExactMeshBoolean.Operations)))
            new ExactMeshBoolean(w1, w2, o).Compute();
    }

    static void Flush(Dictionary<string, object> output, string path)
    {
        string json;
        lock (output["runs"]) json = JsonSerializer.Serialize(output);
        File.WriteAllText(path + ".tmp", json);
        File.Move(path + ".tmp", path, true);
    }

    static void WriteOBJ(DMesh3 mesh, string path)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var map = new Dictionary<int, int>();
        foreach (int vid in mesh.VertexIndices()) {
            map[vid] = map.Count + 1;
            Vector3d v = mesh.GetVertex(vid);
            sb.Append("v ").Append(v.x.ToString("R", ci)).Append(' ').Append(v.y.ToString("R", ci)).Append(' ').Append(v.z.ToString("R", ci)).Append('\n');
        }
        foreach (int tid in mesh.TriangleIndices()) {
            Index3i t = mesh.GetTriangle(tid);
            sb.Append("f ").Append(map[t.a]).Append(' ').Append(map[t.b]).Append(' ').Append(map[t.c]).Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }
}
