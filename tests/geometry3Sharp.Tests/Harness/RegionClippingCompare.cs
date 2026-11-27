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
    /// whether the certifying checker certifies the result with the switch on (where it applies), and the times. The certificate is checked on a third run (the checker's class:
    /// Intersection without the early-out, the certificate recorded; not the timed run). Verdict: "same" (everything agrees), "not compared (not clean)" (same status and reason, but the
    /// results are not clean, so their surfaces are not compared; the reason is in the row and the summary counts these apart), or the first thing that
    /// differs: "failed differently", "status differs", "reason differs", "surface differs", "certificate rejected". No mesh is written or returned. Shared by the Nightly test
    /// (RegionClippingCorpusTests) and benchmarks/BooleanComparison (region-compare: Thingi10K, the fuzz families, the audit corpus).
    /// </summary>
    public static class RegionClippingCompare
    {
        /// <summary>
        /// The predicates of the exact-winding rows: they read the winding numbers themselves (not only their sign), so a crossing counted twice shows. Two meshes: "only A" (inside A,
        /// winding exactly 0 in B) and "only B".
        /// </summary>
        public static readonly (string Name, Func<int[], bool> Predicate)[] ExactPredicates = {
            ("only-first", w => w[0] > 0 && w[1] == 0), ("only-second", w => w[0] == 0 && w[1] > 0), ("both-once", w => w[0] == 1 && w[1] == 1) };

        public static Dictionary<string, object> Row(IList<DMesh3> meshes, Ops op, bool certificate = true, Action<ExactMeshBoolean> configureOn = null, Func<int[], bool> predicate = null, string predicateName = null)
        {
            var r = new Dictionary<string, object> { ["op"] = predicate != null ? "pred-" + predicateName : op.ToString().ToLowerInvariant(), ["meshes"] = meshes.Count, ["tris"] = meshes.Sum(m => m.TriangleCount) };
            ExactMeshBoolean off = new ExactMeshBoolean(meshes.ToList(), op) { AllowOpenMeshes = true, WindingPredicate = predicate }, on = new ExactMeshBoolean(meshes.ToList(), op) { AllowOpenMeshes = true, RegionClipping = true, WindingPredicate = predicate };
            configureOn?.Invoke(on);        // (the positive control of the tests: a change that must be reported)
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
            bool compared = reasonOff == "" && off.Result.TriangleCount > 0;
            if (compared) {
                foreach (var pair in new[] { new[] { off.Result, on.Result }, new[] { on.Result, off.Result } }) {
                    var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference) { AllowOpenMeshes = true };
                    bool ok;
                    try { ok = d.Compute(); } catch (Exception e) { return Verdict(r, "difference failed", e.GetType().Name + ": " + e.Message); }
                    if (ok == false) return Verdict(r, "difference failed", d.Status + ": " + d.Error);
                    if (d.Result.TriangleCount != 0) return Verdict(r, "surface differs", "the exact difference has " + d.Result.TriangleCount + " triangles");
                }
                r["diff"] = "empty";
            } else r["diff"] = off.Result.TriangleCount == 0 ? "both empty" : "not compared";
            if (certificate) {
                // (the checker's class: Intersection without the early-out; the same inputs with the switch on)
                var cb = new ExactMeshBoolean(meshes.ToList(), op) { AllowOpenMeshes = true, RegionClipping = true, RecordCertificate = true, ComputeExactVertices = true, WindingPredicate = predicate };
                if (op == Ops.Intersection && predicate == null) cb.CullOutsideA = false;
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
            if (off.Result.TriangleCount > 0 && reasonOff != "") return Verdict(r, "not compared (not clean)", reasonOff);
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
