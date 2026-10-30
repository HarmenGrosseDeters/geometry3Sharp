using System;
using System.Collections.Generic;
using g3;
using gs;

namespace MeshRepairBenchmark
{
    internal static class WrapCandidateProbe
    {
        internal static Dictionary<string, object> CreateRecord(Dictionary<string, object> actual)
        {
            return new Dictionary<string, object> {
                ["file"] = actual["file"],
                ["record_type"] = "wrap_candidate_probe",
                ["diagnostic_only"] = true,
                ["actual_repair_ms"] = actual["repair_ms"],
                ["repair_ms_scope"] = "actual policy including fallback and capture; not candidate acceptance timing"
            };
        }

        internal static void MeasureEnvelope(DMesh3 originalWrap, DMesh3 candidate, double offset,
            Dictionary<string, object> result, Action<string> progress = null)
        {
            if (!(offset > 0) || double.IsInfinity(offset)) throw new ArgumentOutOfRangeException(nameof(offset));
            double budget = 0.5 * offset;
            progress?.Invoke("candidate.triangle_certificate.begin");
            var reference = new DMeshAABBTree3(originalWrap, true);
            var bound = new TriangleDeviationBounds(originalWrap);
            int passed = 0, unproven = 0;
            foreach (int tid in candidate.TriangleIndices()) {
                Index3i ids = candidate.GetTriangle(tid);
                if (bound.IsWithin(reference, candidate.GetVertex(ids.a), candidate.GetVertex(ids.b), candidate.GetVertex(ids.c), budget)) passed++;
                else unproven++;
            }
            result["candidate_triangle_cert_passed"] = passed;
            result["candidate_triangle_cert_unproven"] = unproven;
            result["candidate_triangle_cert_all"] = unproven == 0 && passed == candidate.TriangleCount && passed > 0;
            progress?.Invoke("candidate.triangle_certificate.end");
            progress?.Invoke("candidate.fine_certificate.begin");
            // This fixed finer diagnostic never changes the policy's output or certificate rule.
            var hd = new MeshHausdorffDistance(candidate, originalWrap) {
                Symmetric = false, SpatialB = reference, Tolerance = 0.001 * budget
            };
            bool computed = hd.Compute();
            result["candidate_fine_computed"] = computed;
            result["candidate_fine_converged"] = hd.Converged;
            result["candidate_fine_lower"] = hd.LowerBound;
            result["candidate_fine_upper"] = hd.UpperBound;
            result["candidate_distance_budget"] = budget;
            result["candidate_fine_tolerance"] = hd.Tolerance;
            result["candidate_fine_status"] = computed && hd.LowerBound > budget ? "exceeded"
                : computed && hd.UpperBound <= budget ? "passed" : "incomplete";
            result["candidate_fine_hi_over_offset"] = hd.UpperBound / offset;
            progress?.Invoke("candidate.fine_certificate.end");
        }
    }
}
