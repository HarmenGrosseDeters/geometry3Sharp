// The imprint comparison (README.md, "Imprint: ExactMeshImprint against MeshMeshCut"): one method on one case directory
// (A.off, B.off, info.json as written by thingi-pair / thingi-contact), one JSON line with the status, the time and the checks
// of the result. compare.py --imprint runs it in one process per (method, operation), with a timeout, like the booleans.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using g3;
using gs;

static class ImprintCompare
{
    public static readonly string[] Methods = { "exact_imprint", "float_cut", "exact_union", "float_union" };

    // imprint <A.off> <B.off> <method> <op>
    //   method exact_imprint (ExactMeshImprint) or float_cut (MeshMeshCut.Compute, default settings): op a_by_b cuts the curves of B
    //   into A, b_by_a the curves of A into B;
    //   method exact_union (ExactMeshBoolean) or float_union (MeshBoolean, the float union built on MeshMeshCut): op union.
    public static int Run(string[] args)
    {
        DMesh3 a = StandardMeshReader.ReadMesh(args[0]), b = StandardMeshReader.ReadMesh(args[1]);
        string method = args[2], op = args[3];
        if (a == null || b == null) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["ok"] = false, ["error"] = "read failed" }));
            return 0;
        }
        Vector3d center = a.CachedBounds.Center;
        DMesh3 target = op == "b_by_a" ? b : a, cutter = op == "b_by_a" ? a : b;
        // warm-up on a small problem, so that the time does not include the JIT compilation (as Program.Run)
        DMesh3 w1 = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        DMesh3 w2 = new DMesh3(w1);
        MeshTransforms.Translate(w2, new Vector3d(0.3, 0.2, 0.1));
        Measure(method, w1, w2, w1.CachedBounds.Center);
        var r = Measure(method, target, cutter, center);
        Console.WriteLine(JsonSerializer.Serialize(r));
        return 0;
    }

    internal static Dictionary<string, object> Measure(string method, DMesh3 target, DMesh3 cutter, Vector3d center)
    {
        var r = new Dictionary<string, object> { ["target_tris"] = target.TriangleCount };
        bool union = method == "exact_union" || method == "float_union";
        if (!union) {
            r["target_closed"] = target.IsClosed();
            r["target_reason"] = MeshRobustRepair.NotCleanReason(target);
        }
        DMesh3 result = null;
        var sw = Stopwatch.StartNew();
        try {
            switch (method) {
                case "exact_imprint": {
                    var imprint = new ExactMeshImprint() { Target = target, Cutter = cutter };
                    bool ok = imprint.Compute();
                    r["ms"] = sw.ElapsedMilliseconds;
                    r["ok"] = ok;
                    r["result_status"] = imprint.Status.ToString();
                    if (!ok) r["error"] = imprint.Error ?? imprint.Status.ToString();
                    result = imprint.Result;
                    if (ok) {
                        r["curve_edges"] = imprint.CurveEdges.Count;
                        r["curve_length"] = imprint.CurveEdges.Sum(k => result.GetVertex((int)(k >> 32)).Distance(result.GetVertex((int)(k & 0xFFFFFFFF))));
                        r["cracks"] = imprint.HasCracks;
                        r["rounding_clean"] = imprint.Rounding == null || imprint.RoundingClean;
                    }
                    break;
                }
                case "float_cut": {
                    var cut = new MeshMeshCut() { Target = new DMesh3(target), CutMesh = cutter };
                    cut.Compute();
                    r["ms"] = sw.ElapsedMilliseconds;
                    r["ok"] = true;
                    r["result_status"] = cut.Status.ToString();
                    result = cut.Target;
                    r["failed_insertions"] = cut.FailedSegmentInsertions;
                    // the intersection segments the cut found (private; read for the comparison only): their length is the
                    // float counterpart of the exact curve length
                    var segs = FloatSegments(cut);
                    r["segments"] = segs.Count;
                    r["curve_length"] = segs.Sum(s => s.a.Distance(s.b));
                    break;
                }
                case "exact_union": {
                    var boolean = Program.Configure(new ExactMeshBoolean(target, cutter, ExactMeshBoolean.Operations.Union));
                    bool ok = boolean.Compute();
                    r["ms"] = sw.ElapsedMilliseconds;
                    r["ok"] = ok;
                    if (!ok) r["error"] = boolean.Error ?? "failed";
                    result = boolean.Result;
                    break;
                }
                case "float_union": {
                    var boolean = new MeshBoolean() { Target = target, Tool = cutter };
                    bool closed = boolean.Compute();
                    r["ms"] = sw.ElapsedMilliseconds;
                    r["ok"] = true;          // Compute() always produces a Result; false only means not closed (checked below)
                    r["returned"] = closed;
                    result = boolean.Result;
                    break;
                }
                default:
                    throw new ArgumentException("unknown method " + method);
            }
        } catch (Exception ex) {
            r["ms"] = sw.ElapsedMilliseconds;
            r["ok"] = false;
            r["exception"] = ex.GetType().Name;
            string msg = ex.Message ?? "";
            r["error"] = "exception " + ex.GetType().Name + ": " + (msg.Length > 160 ? msg.Substring(0, 160) : msg);
            return r;
        }
        if (result != null) {
            r["result_tris"] = result.TriangleCount;
            if (!union)
                r["new_vertices"] = result.VertexCount - target.VertexCount;
            r["result_closed"] = result.IsClosed();
            r["result_reason"] = MeshRobustRepair.NotCleanReason(result);
            if (union)
                r["volume"] = Program.Volume(result, center);
        }
        return r;
    }

    internal static List<(Vector3d a, Vector3d b)> FloatSegments(MeshMeshCut cut)
    {
        var list = new List<(Vector3d, Vector3d)>();
        FieldInfo f = typeof(MeshMeshCut).GetField("Segments", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f?.GetValue(cut) is Array segs) {
            foreach (object s in segs) {
                if (s == null) continue;
                Type t = s.GetType();
                object v0 = t.GetField("v0").GetValue(s), v1 = t.GetField("v1").GetValue(s);
                if (v0 == null || v1 == null) continue;
                FieldInfo pos = v0.GetType().GetField("v");
                list.Add(((Vector3d)pos.GetValue(v0), (Vector3d)pos.GetValue(v1)));
            }
        }
        return list;
    }
}
