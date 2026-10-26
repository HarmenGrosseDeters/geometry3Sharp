// Analysis building blocks on real meshes (BENCH_REPAIR=analysis): MeshWallThickness, MeshDeviation and
// PrimitiveFit3 on the input as loaded (often open, non-manifold or self-intersecting). Thingi10K has no
// reference values, so the run records robustness (exceptions, NaN, time) and checks invariants that
// hold for any input. All lengths are relative to the bounding box diagonal, so that runs with the
// meters / geo transforms can be compared (compare_analysis in analyze_analysis.py).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

namespace MeshRepairBenchmark
{
    static class AnalysisBench
    {
        public static void Run(DMesh3 mesh, Dictionary<string, object> r)
        {
            double diag = mesh.CachedBounds.DiagonalLength;
            r["diag"] = diag;
            r["closed"] = mesh.IsClosed();
            // BENCH_ANALYSIS: comma separated subset of thickness,deviation,fitting (default all)
            string parts = Environment.GetEnvironmentVariable("BENCH_ANALYSIS") ?? "thickness,deviation,fitting";
            if (parts.Contains("thickness")) Thickness(mesh, diag, r);
            if (parts.Contains("deviation")) Deviation(mesh, diag, r);
            if (parts.Contains("fitting")) Fitting(mesh, diag, r);
        }

        static double Rel(double v, double diag) => double.IsNaN(v) ? double.NaN : v / diag;

        // --- wall thickness: per triangle (vertex values are derived), all three methods
        static void Thickness(DMesh3 mesh, double diag, Dictionary<string, object> r)
        {
            // no tree passed in: the library builds its own, on a copy at the origin for geo coordinates; one
            // instance for all methods, so the tree (and the copy) are built once
            var wt = new MeshWallThickness(mesh) { ComputeVertices = false };
            var values = new Dictionary<MeshWallThickness.Methods, double[]>();
            int sdfMax = int.Parse(Environment.GetEnvironmentVariable("BENCH_SDF_MAXTRIS") ?? "50000");
            foreach (var method in new[] { MeshWallThickness.Methods.Ray, MeshWallThickness.Methods.InscribedSphere, MeshWallThickness.Methods.ShapeDiameter }) {
                string key = "wt_" + method.ToString().ToLower();
                if (method == MeshWallThickness.Methods.ShapeDiameter && mesh.TriangleCount > sdfMax) {
                    r[key + "_skipped"] = true;
                    continue;
                }
                var sw = Stopwatch.StartNew();
                wt.Method = method;
                bool ok = wt.Compute();
                r[key + "_ms"] = sw.ElapsedMilliseconds;
                r[key + "_ok"] = ok;
                if (!ok) continue;
                values[method] = wt.TriangleThickness;
                int nan = mesh.TriangleIndices().Count(t => double.IsNaN(wt.TriangleThickness[t]));
                r[key + "_nan_frac"] = (double)nan / mesh.TriangleCount;
                var s = wt.Statistics;
                if (s.Count > 0) {
                    r[key + "_median_rel"] = Rel(s.Median, diag);
                    r[key + "_p05_rel"] = Rel(s.Quantile(0.05), diag);
                    r[key + "_min_rel"] = Rel(s.Min, diag);
                    r[key + "_max_rel"] = Rel(s.Max, diag);
                }
                int negative = mesh.TriangleIndices().Count(t => wt.TriangleThickness[t] < 0);
                r[key + "_negative"] = negative;
            }
            // invariant: the inscribed ball never exceeds the ray (same NaN pattern too)
            double[] ray, ins;
            if (values.TryGetValue(MeshWallThickness.Methods.Ray, out ray) && values.TryGetValue(MeshWallThickness.Methods.InscribedSphere, out ins)) {
                int above = 0, nanMismatch = 0;
                foreach (int t in mesh.TriangleIndices()) {
                    if (double.IsNaN(ray[t]) != double.IsNaN(ins[t])) { nanMismatch++; continue; }
                    if (ins[t] > ray[t] * (1 + 1e-9) + 1e-12 * diag) above++;
                }
                r["wt_inscribed_above_ray"] = above;
                r["wt_nan_mismatch"] = nanMismatch;
            }
        }

        // --- deviation: self deviation, sign agreement of the two sign modes, certified bound
        static void Deviation(DMesh3 mesh, double diag, Dictionary<string, object> r)
        {
            // no tree passed in, as above
            var sw = Stopwatch.StartNew();
            var self = new MeshDeviation(mesh, mesh);
            if (self.Compute())
                r["dev_self_max_rel"] = Rel(self.Statistics.MaxAbsolute, diag);
            r["dev_self_ms"] = sw.ElapsedMilliseconds;

            // the two sign modes on random points of the (expanded) bounding box, away from the surface
            var pn = new MeshDeviation(mesh, mesh) { Sign = MeshDeviation.Signs.PseudoNormal };
            var wn = new MeshDeviation(mesh, mesh) { Sign = MeshDeviation.Signs.WindingNumber };
            AxisAlignedBox3d box = mesh.CachedBounds;
            box.Expand(0.1 * diag);
            var rnd = new Random(1);
            int samples = 2000, disagree = 0, used = 0;
            for (int i = 0; i < samples; ++i) {
                Vector3d p = box.Min + new Vector3d(rnd.NextDouble() * box.Width, rnd.NextDouble() * box.Height, rnd.NextDouble() * box.Depth);
                Vector3d c; int tid;
                double a = pn.SignedDistance(p, out c, out tid);
                if (!(Math.Abs(a) > 1e-6 * diag)) continue;
                double b = wn.SignedDistance(p, out c, out tid);
                used++;
                if (Math.Sign(a) != Math.Sign(b)) disagree++;
            }
            r["dev_sign_samples"] = used;
            r["dev_sign_disagree_frac"] = used > 0 ? (double)disagree / used : 0;

            // against a simplified copy: the largest vertex deviation must lie below the certified bound
            try {
                DMesh3 reduced = new DMesh3(mesh);
                var reducer = new Reducer(reduced);
                reducer.ReduceToTriangleCount(Math.Max(4, mesh.TriangleCount / 2));
                sw.Restart();
                var dev = new MeshDeviation(mesh, reduced) { ComputeCertifiedBound = true, CertifiedTolerance = 1e-3 * diag };
                bool ok = dev.Compute();
                r["dev_reduced_ms"] = sw.ElapsedMilliseconds;
                r["dev_reduced_ok"] = ok;
                if (ok) {
                    r["dev_reduced_max_rel"] = Rel(dev.Statistics.MaxAbsolute, diag);
                    r["dev_reduced_rms_rel"] = Rel(dev.Statistics.RMS, diag);
                    r["dev_reduced_hd_lo_rel"] = Rel(dev.HausdorffLowerBound, diag);
                    r["dev_reduced_hd_hi_rel"] = Rel(dev.HausdorffUpperBound, diag);
                    r["dev_reduced_bound_violated"] = dev.Statistics.MaxAbsolute > dev.HausdorffLowerBound * (1 + 1e-9) + 1e-12 * diag;
                }
            } catch (Exception e) {
                r["dev_reduced_error"] = e.GetType().Name + ": " + e.Message;
            }

            TriangleBounds(mesh, diag, r);
        }

        // --- certified deviation per triangle (MeshDeviation.ComputeTriangleBounds), against the vertex values.
        //   tri_red:  the copy reduced to half against the input (big triangles over the input's details; the
        //             Reducer depends on rounding, so this one is not compared across transforms)
        //   tri_pert: the input against a copy whose vertices are moved along their normals by up to 2e-3 of
        //             the diagonal, a fixed pattern per vertex ID: the same case in every transform
        static void TriangleBounds(DMesh3 mesh, double diag, Dictionary<string, object> r)
        {
            try {
                DMesh3 reduced = new DMesh3(mesh);
                new Reducer(reduced).ReduceToTriangleCount(Math.Max(4, mesh.TriangleCount / 2));
                TriangleBoundsCase(reduced, mesh, diag, "dev_tri_red", true, r);
            } catch (Exception e) {
                r["dev_tri_red_error"] = e.GetType().Name + ": " + e.Message;
            }
            try {
                DMesh3 perturbed = new DMesh3(mesh);
                foreach (int vid in mesh.VertexIndices()) {
                    if (mesh.GetVtxEdgeCount(vid) == 0) continue;
                    // hash of the ID in [-1, 1]
                    uint h = (uint)vid * 2654435761u; h ^= h >> 16; h *= 2246822519u; h ^= h >> 13;
                    double s = (h & 0xFFFFFF) / (double)0xFFFFFF * 2 - 1;
                    perturbed.SetVertex(vid, mesh.GetVertex(vid) + (2e-3 * diag * s) * MeshAnalysisNormals.VertexNormal(mesh, vid));
                }
                TriangleBoundsCase(mesh, perturbed, diag, "dev_tri_pert", false, r);
            } catch (Exception e) {
                r["dev_tri_pert_error"] = e.GetType().Name + ": " + e.Message;
            }
        }

        static void TriangleBoundsCase(DMesh3 from, DMesh3 to, double diag, string key, bool withHausdorff, Dictionary<string, object> r)
        {
            double tol = 1e-4 * diag;
            // vertex values only, then with the triangle bounds (and the certified Hausdorff interval)
            var sw = Stopwatch.StartNew();
            var vtx = new MeshDeviation(from, to) { Sign = MeshDeviation.Signs.Unsigned };
            if (vtx.Compute() == false) { r[key + "_ok"] = false; return; }
            r[key + "_vertex_ms"] = sw.ElapsedMilliseconds;
            sw.Restart();
            var dev = new MeshDeviation(from, to) { Sign = MeshDeviation.Signs.Unsigned, ComputeTriangleBounds = true, CertifiedTolerance = tol };
            bool ok = dev.Compute();
            r[key + "_ms"] = sw.ElapsedMilliseconds;
            r[key + "_ok"] = ok;
            if (!ok) return;
            r[key + "_unconverged"] = dev.TriangleBoundsUnconverged;

            // per triangle: largest corner value, the interval, and where the interior exceeds the corners
            double maxLo = 0, maxHi = 0, maxAbove = 0, areaAbove = 0, area = 0;
            int lowBelowCorner = 0, inverted = 0, above = 0, nan = 0, n = 0;
            foreach (int tid in from.TriangleIndices()) {
                double lo = dev.TriangleDeviationLower[tid], hi = dev.TriangleDeviationUpper[tid];
                if (double.IsNaN(lo) || double.IsNaN(hi)) { nan++; continue; }
                n++;
                Index3i t = from.GetTriangle(tid);
                double corner = Math.Max(dev.VertexDeviation[t.a], Math.Max(dev.VertexDeviation[t.b], dev.VertexDeviation[t.c]));
                if (lo < corner * (1 - 1e-12) - 1e-15 * diag) lowBelowCorner++;
                if (hi < lo) inverted++;
                maxLo = Math.Max(maxLo, lo); maxHi = Math.Max(maxHi, hi);
                double a = from.GetTriArea(tid);
                area += a;
                // the interior is certainly (lower bound) farther than the corners by more than the tolerance
                if (lo > corner + tol) { above++; areaAbove += a; }
                maxAbove = Math.Max(maxAbove, lo - corner);
            }
            r[key + "_nan"] = nan;
            r[key + "_lower_below_corner"] = lowBelowCorner;
            r[key + "_inverted"] = inverted;
            r[key + "_vertex_max_rel"] = Rel(dev.Statistics.MaxAbsolute, diag);
            r[key + "_lo_max_rel"] = Rel(maxLo, diag);
            r[key + "_hi_max_rel"] = Rel(maxHi, diag);
            r[key + "_above_frac"] = n > 0 ? (double)above / n : 0;
            r[key + "_above_area_frac"] = area > 0 ? areaAbove / area : 0;
            r[key + "_above_max_rel"] = Rel(maxAbove, diag);
            // certified area fractions within 1e-3 / 1e-2 of the diagonal, and from the vertex values (not certified)
            r[key + "_within3_area_frac"] = dev.TriangleUpperStatistics.FractionWithin(1e-3 * diag);
            r[key + "_within2_area_frac"] = dev.TriangleUpperStatistics.FractionWithin(1e-2 * diag);
            r[key + "_vertex_within3_frac"] = dev.Statistics.FractionWithin(1e-3 * diag);
            r[key + "_vertex_within2_frac"] = dev.Statistics.FractionWithin(1e-2 * diag);
            r[key + "_hi_p95_rel"] = Rel(dev.TriangleUpperStatistics.AbsolutePercentile(0.95), diag);
            r[key + "_vertex_p95_rel"] = Rel(dev.Statistics.AbsolutePercentile(0.95), diag);

            if (withHausdorff) {
                sw.Restart();
                var h = new MeshDeviation(from, to) { Sign = MeshDeviation.Signs.Unsigned, ComputeCertifiedBound = true, CertifiedTolerance = tol };
                if (h.Compute()) {
                    r[key + "_hd_ms"] = sw.ElapsedMilliseconds;
                    r[key + "_hd_lo_rel"] = Rel(h.HausdorffLowerBound, diag);
                    r[key + "_hd_hi_rel"] = Rel(h.HausdorffUpperBound, diag);
                    // both are intervals of the same maximum: they must overlap
                    double eps = 1e-12 * diag;
                    r[key + "_hd_disjoint"] = maxLo > h.HausdorffUpperBound + eps || h.HausdorffLowerBound > maxHi + eps;
                }
            }
        }

        // --- fitting on the vertices: success, inlier fraction, determinism and invariance under a rotation
        static void Fitting(DMesh3 mesh, double diag, Dictionary<string, object> r)
        {
            int maxPoints = 20000;
            var vids = mesh.VertexIndices().Where(v => mesh.GetVtxEdgeCount(v) > 0).ToList();
            int step = Math.Max(1, vids.Count / maxPoints);
            var pts = new List<Vector3d>();
            var nrm = new List<Vector3d>();
            for (int i = 0; i < vids.Count; i += step) {
                pts.Add(mesh.GetVertex(vids[i]));
                nrm.Add(MeshAnalysisNormals.VertexNormal(mesh, vids[i]));
            }
            Vector3d center = mesh.CachedBounds.Center;
            Quaterniond rot = Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 37);
            // BENCH_FIT_LO=0: plain RANSAC (PrimitiveFit3.LocalOptimization = false)
            bool lo = Environment.GetEnvironmentVariable("BENCH_FIT_LO") != "0";
            foreach (var prim in new[] { PrimitiveFit3.Primitives.Plane, PrimitiveFit3.Primitives.Sphere, PrimitiveFit3.Primitives.Cylinder, PrimitiveFit3.Primitives.Cone }) {
                string key = "fit_" + prim.ToString().ToLower();
                var sw = Stopwatch.StartNew();
                var fit = new PrimitiveFit3(pts, nrm) { Primitive = prim, InlierDistance = 1e-3 * diag, LocalOptimization = lo };
                bool ok = fit.Compute();
                r[key + "_ms"] = sw.ElapsedMilliseconds;
                r[key + "_result"] = fit.Result.ToString();
                if (!ok) continue;
                r[key + "_inlier_frac"] = (double)fit.InlierCount / pts.Count;
                r[key + "_rms_rel"] = Rel(fit.RMSError, diag);
                r[key + "_max_err_rel"] = Rel(fit.MaxError, diag);
                // every inlier must be within the threshold
                r[key + "_max_err_ok"] = fit.MaxError <= 1e-3 * diag * (1 + 1e-9);

                // same input again: identical
                var again = new PrimitiveFit3(pts, nrm) { Primitive = prim, InlierDistance = 1e-3 * diag, LocalOptimization = lo };
                again.Compute();
                r[key + "_deterministic"] = again.InlierCount == fit.InlierCount && again.RMSError.Equals(fit.RMSError);

                // rotated input (same point order and seed): the same inliers up to points near the threshold
                var rp = pts.Select(p => center + rot * (p - center)).ToList();
                var rn = nrm.Select(n => rot * n).ToList();
                var rf = new PrimitiveFit3(rp, rn) { Primitive = prim, InlierDistance = 1e-3 * diag, LocalOptimization = lo };
                if (rf.Compute()) {
                    int both = 0, any = 0;
                    for (int i = 0; i < pts.Count; ++i) {
                        if (fit.IsInlier[i] && rf.IsInlier[i]) both++;
                        if (fit.IsInlier[i] || rf.IsInlier[i]) any++;
                    }
                    r[key + "_rot_jaccard"] = any > 0 ? (double)both / any : 1.0;
                    r[key + "_rot_inlier_frac"] = (double)rf.InlierCount / pts.Count;
                    r[key + "_rot_rms_rel"] = Rel(rf.RMSError, diag);
                    r[key + "_iterations"] = fit.Iterations;
                    r[key + "_rot_iterations"] = rf.Iterations;
                } else {
                    r[key + "_rot_jaccard"] = 0.0;
                }
            }
        }
    }
}
