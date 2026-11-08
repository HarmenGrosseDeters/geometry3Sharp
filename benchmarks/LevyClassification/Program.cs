// Phase-1 probe for classification by propagation (docs/design/LEVY_CLASSIFICATION.md): runs ExactMeshBoolean on
// BooleanComparison case directories (A.off, B.off, as written by `BooleanComparison export`) and writes one JSON
// line per operation with the phase times, the counters, the patch count and the input facts that bound how many
// exact rays a propagating classification would still need (connected components of A and B).
// Uses the public API only; builds against any library commit that has ExactMeshBoolean.Patches.
//
// usage: dotnet run -c Release -- [--ops union,intersection,difference] [--no-safe-rounding] <case dir>...
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;

static class Program
{
    static int Main(string[] args)
    {
        var ops = new List<string> { "union", "intersection", "difference" };
        bool safe = true;
        var cases = new List<string>();
        for (int i = 0; i < args.Length; ++i) {
            if (args[i] == "--ops") ops = args[++i].Split(',').ToList();
            else if (args[i] == "--no-safe-rounding") safe = false;
            else cases.Add(args[i]);
        }
        var opOf = new Dictionary<string, ExactMeshBoolean.Operations> {
            ["union"] = ExactMeshBoolean.Operations.Union, ["intersection"] = ExactMeshBoolean.Operations.Intersection,
            ["difference"] = ExactMeshBoolean.Operations.Difference };
        foreach (string c in cases) {
            DMesh3 a = StandardMeshReader.ReadMesh(Path.Combine(c, "A.off")), b = StandardMeshReader.ReadMesh(Path.Combine(c, "B.off"));
            if (a == null || b == null) {
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["case"] = c, ["error"] = "load failed" }));
                continue;
            }
            int compA = new MeshConnectedComponents(a).Let(m => { m.FindConnectedT(); return m.Count; });
            int compB = new MeshConnectedComponents(b).Let(m => { m.FindConnectedT(); return m.Count; });
            foreach (string op in ops) {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var boolean = new ExactMeshBoolean(a, b, opOf[op]) { SafeRounding = safe };
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
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> {
                    ["case"] = Path.GetFileName(c.TrimEnd('/')), ["op"] = op, ["ok"] = ok, ["error"] = error,
                    ["status"] = boolean.Status.ToString(), ["ms"] = ms, ["cores"] = Environment.ProcessorCount,
                    ["tris_a"] = a.TriangleCount, ["tris_b"] = b.TriangleCount, ["components_a"] = compA, ["components_b"] = compB,
                    ["exact_winding_a"] = boolean.ExactWindingA, ["exact_winding_b"] = boolean.ExactWindingB,
                    ["patches"] = boolean.Patches, ["non_manifold_edges"] = boolean.NonManifoldEdges,
                    ["conflict_edges"] = boolean.ConflictEdges,
                    ["phases_ms"] = boolean.PhaseMilliseconds, ["counts"] = boolean.Counts,
                    ["triangles"] = ok ? boolean.Result.TriangleCount : -1,
                }));
            }
        }
        return 0;
    }

    static R Let<T, R>(this T x, Func<T, R> f) => f(x);
}
