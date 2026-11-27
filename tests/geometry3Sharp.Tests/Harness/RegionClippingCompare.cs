using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
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
            if (reasonOff != reasonOn) return Differs(r, "reason differs", "off '" + reasonOff + "', on '" + reasonOn + "'", meshes, op, predicate);
            if ((off.Result.TriangleCount == 0) != (on.Result.TriangleCount == 0)) return Differs(r, "surface differs", "one result is empty", meshes, op, predicate);
            bool compared = reasonOff == "" && off.Result.TriangleCount > 0;
            if (compared) {
                foreach (var pair in new[] { new[] { off.Result, on.Result }, new[] { on.Result, off.Result } }) {
                    var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference) { AllowOpenMeshes = true };
                    bool ok;
                    try { ok = d.Compute(); } catch (Exception e) { return Verdict(r, "difference failed", e.GetType().Name + ": " + e.Message); }
                    if (ok == false) return Verdict(r, "difference failed", d.Status + ": " + d.Error);
                    if (d.Result.TriangleCount != 0) return Differs(r, "surface differs", "the exact difference has " + d.Result.TriangleCount + " triangles", meshes, op, predicate);
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

        // A difference of the rounded results, and the informational column "exact" for it (the verdict and the exit code stay those of the rounded results): the two UNROUNDED results (the
        // assembled mesh with the exact vertices, ComputeExactVertices) as sets of exact oriented triangles, "equal" if the sets are the same (the same exact surface, only the
        // rounding differs), else "differs" (a difference of the exact surfaces, or of their triangulations: the planar simplification makes the faces canonical, so only a face with other
        // boundary vertices shows). Also the smallest edge of the unrounded result against the rounding resolution of its coordinates (max |coordinate| 2^-52) and the displacement the
        // rounding repair may use (RoundingUlpFactor times that): a feature near or below the second is what the repair may collapse.
        static Dictionary<string, object> Differs(Dictionary<string, object> r, string verdict, string message, IList<DMesh3> meshes, Ops op, Func<int[], bool> predicate)
        {
            try {
                var keys = new List<string>[2];
                var faces = new List<ExactPoint3[]>[2];
                for (int k = 0; k < 2; ++k) {
                    faces[k] = new List<ExactPoint3[]>();
                    var b = new ExactMeshBoolean(meshes.ToList(), op) { AllowOpenMeshes = true, RegionClipping = k == 1, RecordCertificate = true, ComputeExactVertices = true, WindingPredicate = predicate };
                    if (op == Ops.Intersection && predicate == null) b.CullOutsideA = false;
                    if (b.Compute() == false) { r["exact"] = "run failed"; return Verdict(r, verdict, message); }
                    DMesh3 m = b.CertificateAssembled ?? b.Result;
                    ExactPoint3[] ex = b.ResultExactVertices;
                    keys[k] = new List<string>();
                    foreach (int t in m.TriangleIndices()) {
                        Index3i f = m.GetTriangle(t);
                        string[] p = { PointKey(ex[f.a]), PointKey(ex[f.b]), PointKey(ex[f.c]) };
                        faces[k].Add(new[] { ex[f.a], ex[f.b], ex[f.c] });
                        int lo = 0;
                        for (int i = 1; i < 3; ++i) if (string.CompareOrdinal(p[i], p[lo]) < 0) lo = i;
                        keys[k].Add(p[lo] + "|" + p[(lo + 1) % 3] + "|" + p[(lo + 2) % 3]);
                    }
                    keys[k].Sort(string.CompareOrdinal);
                    if (k == 1) {
                        double minEdge = double.MaxValue, maxAbs = 0;
                        foreach (int v in m.VertexIndices()) { Vector3d q = m.GetVertex(v); maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(q.x), Math.Max(Math.Abs(q.y), Math.Abs(q.z)))); }
                        foreach (int t in m.TriangleIndices()) {
                            Index3i f = m.GetTriangle(t);
                            for (int i = 0; i < 3; ++i) { double len = m.GetVertex(f[i]).Distance(m.GetVertex(f[(i + 1) % 3])); if (len > 0 && len < minEdge) minEdge = len; }
                        }
                        r["min_edge"] = minEdge == double.MaxValue ? 0 : minEdge;
                        r["round_res"] = maxAbs * Math.Pow(2, -52);
                        r["repair_tol"] = maxAbs * Math.Pow(2, -52) * new ExactMeshBoolean(meshes.ToList(), op).RoundingUlpFactor;      // (the displacement the rounding repair may use: RoundingUlpFactor ulps)
                    }
                }
                r["exact_tris"] = keys[0].Count + "/" + keys[1].Count;
                if (keys[0].SequenceEqual(keys[1])) r["exact"] = "equal";
                else {
                    // (another triangulation of the same faces is no difference of the surface: the outlines of the faces, per oriented plane, must be the same)
                    r["exact"] = SameFaces(faces[0], faces[1]) ? "equal" : "differs";
                    if ((string)r["exact"] == "equal") r["exact_note"] = "same faces, another triangulation";
                }
            } catch (Exception e) { r["exact"] = "error: " + e.GetType().Name; }
            return Verdict(r, verdict, message);
        }

        /// <summary> two triangle soups have the same faces: the same outlines per oriented plane, whatever the triangulation </summary>
        internal static bool SameFaces(List<ExactPoint3[]> a, List<ExactPoint3[]> b) => FaceOutlines(a).SequenceEqual(FaceOutlines(b));

        static string PointKey(ExactPoint3 p) => p.X + "," + p.Y + "," + p.Z + "," + p.W;

        // The outlines of the planar faces of a triangle soup: the triangles grouped by their oriented exact plane, the directed edges of a group with the opposite pairs cancelled (the
        // inner edges of a face), the chains of collinear edges joined where they meet in a point with one edge in and one out. The same faces give the same sorted list whatever
        // their triangulation.
        static List<string> FaceOutlines(List<ExactPoint3[]> triangles)
        {
            var byPlane = new Dictionary<string, List<ExactPoint3[]>>();
            foreach (ExactPoint3[] t in triangles) {
                BigInteger[] d1 = Diff(t[1], t[0]), d2 = Diff(t[2], t[0]);
                BigInteger[] n = { d1[1] * d2[2] - d1[2] * d2[1], d1[2] * d2[0] - d1[0] * d2[2], d1[0] * d2[1] - d1[1] * d2[0] };
                BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(n[0], n[1]), n[2]);
                if (g.IsZero) continue;                       // (a triangle of zero area: no face)
                for (int k = 0; k < 3; ++k) n[k] /= g;
                BigInteger num = n[0] * t[0].X + n[1] * t[0].Y + n[2] * t[0].Z, den = t[0].W, h = BigInteger.GreatestCommonDivisor(num, den);
                if (h.IsZero) h = BigInteger.One;
                string key = n[0] + "," + n[1] + "," + n[2] + ":" + (num / h) + "/" + (den / h);
                if (byPlane.TryGetValue(key, out List<ExactPoint3[]> l) == false) byPlane[key] = l = new List<ExactPoint3[]>();
                l.Add(t);
            }
            var result = new List<string>();
            foreach (var kv in byPlane) {
                var edges = new Dictionary<string, (ExactPoint3, ExactPoint3)>();
                foreach (ExactPoint3[] t in kv.Value)
                    for (int k = 0; k < 3; ++k) {
                        ExactPoint3 a = t[k], b = t[(k + 1) % 3];
                        string ab = PointKey(a) + ">" + PointKey(b), ba = PointKey(b) + ">" + PointKey(a);
                        if (edges.Remove(ba) == false) edges[ab] = (a, b);
                    }
                // the edges out of and into each point
                var outOf = new Dictionary<string, List<string>>(); var into = new Dictionary<string, List<string>>();
                foreach (var e in edges) {
                    string pa = PointKey(e.Value.Item1), pb = PointKey(e.Value.Item2);
                    if (outOf.TryGetValue(pa, out List<string> lo) == false) outOf[pa] = lo = new List<string>();
                    lo.Add(e.Key);
                    if (into.TryGetValue(pb, out List<string> li) == false) into[pb] = li = new List<string>();
                    li.Add(e.Key);
                }
                var merged = new List<(ExactPoint3, ExactPoint3)>();
                var used = new HashSet<string>();
                foreach (var e in edges.OrderBy(x => x.Key, StringComparer.Ordinal)) {
                    if (used.Contains(e.Key)) continue;
                    // walk back to the start of the collinear chain, then forward to its end
                    ExactPoint3 a = e.Value.Item1, b = e.Value.Item2;
                    used.Add(e.Key);
                    while (true) {
                        string pa = PointKey(a);
                        if (into.TryGetValue(pa, out List<string> li) == false || li.Count != 1 || outOf[pa].Count != 1 || used.Contains(li[0])) break;
                        var prev = edges[li[0]];
                        if (Collinear(prev.Item1, a, b) == false) break;
                        used.Add(li[0]); a = prev.Item1;
                    }
                    while (true) {
                        string pb = PointKey(b);
                        if (outOf.TryGetValue(pb, out List<string> lo) == false || lo.Count != 1 || into[pb].Count != 1 || used.Contains(lo[0])) break;
                        var next = edges[lo[0]];
                        if (Collinear(a, b, next.Item2) == false) break;
                        used.Add(lo[0]); b = next.Item2;
                    }
                    merged.Add((a, b));
                }
                result.Add(kv.Key + "#" + string.Join(";", merged.Select(m => PointKey(m.Item1) + ">" + PointKey(m.Item2)).OrderBy(x => x, StringComparer.Ordinal)));
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        static BigInteger[] Diff(ExactPoint3 a, ExactPoint3 b) => new[] { a.X * b.W - b.X * a.W, a.Y * b.W - b.Y * a.W, a.Z * b.W - b.Z * a.W };

        static bool Collinear(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c)
        {
            BigInteger[] u = Diff(b, a), v = Diff(c, b);
            return (u[1] * v[2] - u[2] * v[1]).IsZero && (u[2] * v[0] - u[0] * v[2]).IsZero && (u[0] * v[1] - u[1] * v[0]).IsZero;
        }

        static Dictionary<string, object> Verdict(Dictionary<string, object> row, string verdict, string message)
        {
            row["verdict"] = verdict;
            if (message != "") row["message"] = message.Length > 400 ? message.Substring(0, 400) : message;
            return row;
        }
    }
}
