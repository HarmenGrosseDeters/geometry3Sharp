// Exact Boolean 2.0 final run: the three operations of one #385 case (A.off, B.off, info.json from BooleanComparison
// export), each timed on its own instance, then checked. One JSON line per operation.
//   FinalRunner <case dir> <id> <class>
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

    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string dir = args[0], id = args[1], cls = args[2];
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
                    var ev = Stopwatch.StartNew();
                    // the #385 evaluator's verdict: "" (clean), "empty", or the first defect of NotCleanReason
                    row["reason"] = MeshRobustRepair.NotCleanReason(b.Result);
                    row["eval_ms"] = ev.ElapsedMilliseconds;
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
