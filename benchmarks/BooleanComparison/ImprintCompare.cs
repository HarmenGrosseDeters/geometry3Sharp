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
        Measure(method, w1, w2, w1.CachedBounds.Center, false);
        var r = Measure(method, target, cutter, center, true);
        Console.WriteLine(JsonSerializer.Serialize(r));
        return 0;
    }

    // printOp: print a first JSON line (stage "op": ms, ok) as soon as the operation has ended, before the checks of the input and
    // the result (NotCleanReason with its self-intersection test can take long on large meshes): compare.py reads it when the run
    // times out, so that a timeout in the checks is not charged to the method as a timeout of the operation (status check_timeout)
    internal static Dictionary<string, object> Measure(string method, DMesh3 target, DMesh3 cutter, Vector3d center, bool printOp)
    {
        var r = new Dictionary<string, object> { ["target_tris"] = target.TriangleCount };
        bool union = method == "exact_union" || method == "float_union";
        DMesh3 result = null;
        // the float cut works in place: its copy of the target is made before the clock starts
        DMesh3 copy = method == "float_cut" ? new DMesh3(target) : null;
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
                        // (#1034: the sliver cleanup stage, the split edges rerouted through a vertex, the drift used against the bound)
                        foreach (string k in new[] { "sliver_cleanup_stage", "split_edges_through_target_vertex", "split_edges_through_new_vertex",
                                "curve_edges_contracted", "target_edge_parts_contracted" })
                            if (imprint.Counts.TryGetValue(k, out long n)) r[k] = n;
                        if (imprint.Rounding != null) {
                            r["rounding_max_drift"] = imprint.Rounding.MaxDrift;
                            r["rounding_bound"] = imprint.Rounding.MaxLength;
                            r["rounding_remaining_degenerate"] = imprint.Rounding.RemainingDegenerate;
                            r["rounding_remaining_intersections"] = imprint.Rounding.RemainingIntersections;
                            r["rounding_remaining_false_contacts"] = imprint.Rounding.RemainingFalseContacts;
                            r["rounding_limit"] = imprint.Rounding.EvaluationLimitReached;
                        }
                    }
                    break;
                }
                case "float_cut": {
                    var cut = new MeshMeshCut() { Target = copy, CutMesh = cutter };
                    cut.Compute();
                    r["ms"] = sw.ElapsedMilliseconds;
                    r["ok"] = true;
                    r["result_status"] = cut.Status.ToString();
                    result = cut.Target;
                    r["failed_insertions"] = cut.FailedSegmentInsertions;
                    // the intersection segments the cut found (private; read for the comparison only): their length is the
                    // float counterpart of the exact curve length. MeshMeshCut has one segment per pair of triangles, so a piece
                    // of the curve on an edge shared by two triangles comes twice: deduplicated (unordered endpoint pairs, at a
                    // grid of 1e-9 of the target's box). Coplanar overlaps give no segments (MeshMeshCut does not handle them).
                    var segs = Dedup(FloatSegments(cut), target.CachedBounds.Min, 1e-9 * Math.Max(target.CachedBounds.MaxDim, 1e-300));
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
            if (!union) AddTargetChecks(r, target);
            r["ok"] = false;
            r["exception"] = ex.GetType().Name;
            string msg = ex.Message ?? "";
            r["error"] = "exception " + ex.GetType().Name + ": " + (msg.Length > 160 ? msg.Substring(0, 160) : msg);
            return r;
        }
        if (printOp) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["stage"] = "op", ["ms"] = r["ms"], ["ok"] = r["ok"] }));
            Console.Out.Flush();
        }
        if (!union)
            AddTargetChecks(r, target);
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

    static void AddTargetChecks(Dictionary<string, object> r, DMesh3 target)
    {
        r["target_closed"] = target.IsClosed();
        r["target_reason"] = MeshRobustRepair.NotCleanReason(target);
    }

    // segments with the same endpoints (in either order) after snapping to a grid of cell size h relative to origin (the box
    // minimum, so that coordinates far from the zero point do not overflow the keys) are counted once; segments of zero length
    // (point contacts) are dropped, so "segments" counts the pieces of curve
    internal static List<(Vector3d a, Vector3d b)> Dedup(List<(Vector3d a, Vector3d b)> segs, Vector3d origin, double h)
    {
        (long, long, long) key(Vector3d p) => ((long)Math.Round((p.x - origin.x) / h), (long)Math.Round((p.y - origin.y) / h), (long)Math.Round((p.z - origin.z) / h));
        var seen = new HashSet<((long, long, long), (long, long, long))>();
        var list = new List<(Vector3d, Vector3d)>();
        foreach (var s in segs) {
            var ka = key(s.a); var kb = key(s.b);
            var k = ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka);
            if (ka.Equals(kb) == false && seen.Add(k))
                list.Add(s);
        }
        return list;
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
