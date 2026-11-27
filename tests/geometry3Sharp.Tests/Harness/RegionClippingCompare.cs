using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The corpus gate of E' stage 4 (docs/design/COPLANAR_REGION_CLIPPING.md, #1630): one boolean with and without ExactMeshBoolean.RegionClipping. A row says, per operation:
    /// the Status of both, the NotCleanReason of both results, whether the exact difference of the two results is empty both ways (only where both are clean and not empty),
    /// whether the certifying checker certifies the result with the switch on (where it applies), and the times. Verdict: "same" (everything agrees), or the first thing that
    /// differs: "failed differently", "status differs", "reason differs", "surface differs", "certificate rejected". No mesh is written or returned. Shared by the Nightly test
    /// (RegionClippingCorpusTests) and benchmarks/BooleanComparison (region-compare: Thingi10K, the fuzz families, the audit corpus).
    /// </summary>
    public static class RegionClippingCompare
    {
        public static Dictionary<string, object> Row(IList<DMesh3> meshes, Ops op, bool certificate = true)
        {
            var r = new Dictionary<string, object> { ["op"] = op.ToString().ToLowerInvariant(), ["meshes"] = meshes.Count, ["tris"] = meshes.Sum(m => m.TriangleCount) };
            ExactMeshBoolean off = new ExactMeshBoolean(meshes.ToList(), op) { AllowOpenMeshes = true }, on = new ExactMeshBoolean(meshes.ToList(), op) { AllowOpenMeshes = true, RegionClipping = true };
            bool okOff, okOn;
            var sw = Stopwatch.StartNew();
            try { okOff = off.Compute(); } catch (Exception e) { return Verdict(r, "exception off", e.GetType().Name + ": " + e.Message); }
            r["t_off_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            sw.Restart();
            try { okOn = on.Compute(); } catch (Exception e) { return Verdict(r, "exception on", e.GetType().Name + ": " + e.Message); }
            r["t_on_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            r["status_off"] = off.Status.ToString(); r["status_on"] = on.Status.ToString();
            if (okOff != okOn) return Verdict(r, "failed differently", "off " + off.Status + " (" + off.Error + "), on " + on.Status + " (" + on.Error + ")");
            if (off.Status != on.Status) return Verdict(r, "status differs", off.Status + " / " + on.Status);
            if (okOff == false) return Verdict(r, "same", "both fail: " + off.Status);
            r["tris_off"] = off.Result.TriangleCount; r["tris_on"] = on.Result.TriangleCount;
            string reasonOff = MeshRobustRepair.NotCleanReason(off.Result) ?? "", reasonOn = MeshRobustRepair.NotCleanReason(on.Result) ?? "";
            r["reason"] = reasonOff == "" ? "clean" : reasonOff;
            if (reasonOff != reasonOn) return Verdict(r, "reason differs", "off '" + reasonOff + "', on '" + reasonOn + "'");
            if ((off.Result.TriangleCount == 0) != (on.Result.TriangleCount == 0)) return Verdict(r, "surface differs", "one result is empty");
            if (reasonOff == "" && off.Result.TriangleCount > 0) {
                foreach (var pair in new[] { new[] { off.Result, on.Result }, new[] { on.Result, off.Result } }) {
                    var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference) { AllowOpenMeshes = true };
                    bool ok;
                    try { ok = d.Compute(); } catch (Exception e) { return Verdict(r, "difference failed", e.GetType().Name + ": " + e.Message); }
                    if (ok == false) return Verdict(r, "difference failed", d.Status + ": " + d.Error);
                    if (d.Result.TriangleCount != 0) return Verdict(r, "surface differs", "the exact difference has " + d.Result.TriangleCount + " triangles");
                }
                r["diff"] = "empty";
            } else r["diff"] = "skipped";
            if (certificate) {
                // (the checker's class: Intersection without the early-out; the same inputs with the switch on)
                var cb = new ExactMeshBoolean(meshes.ToList(), op) { AllowOpenMeshes = true, RegionClipping = true, RecordCertificate = true, ComputeExactVertices = true };
                if (op == Ops.Intersection) cb.CullOutsideA = false;
                string v;
                try {
                    if (cb.Compute() == false) return Verdict(r, "certificate run failed", cb.Status + ": " + cb.Error);
                    v = BooleanCertificate.From(cb).Check();
                } catch (Exception e) { return Verdict(r, "certificate exception", e.GetType().Name + ": " + e.Message); }
                if (v.StartsWith("n/a")) r["cert"] = "not applicable";
                else if (v.StartsWith("not clean")) r["cert"] = "removed parts";
                else if (v != "") return Verdict(r, "certificate rejected", v);
                else r["cert"] = "certified";
            }
            return Verdict(r, "same", "");
        }

        static Dictionary<string, object> Verdict(Dictionary<string, object> row, string verdict, string message)
        {
            row["verdict"] = verdict;
            if (message != "") row["message"] = message.Length > 400 ? message.Substring(0, 400) : message;
            return row;
        }
    }
}
