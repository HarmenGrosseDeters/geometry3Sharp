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
            var spatial = new DMeshAABBTree3(mesh, true);
            var values = new Dictionary<MeshWallThickness.Methods, double[]>();
            int sdfMax = int.Parse(Environment.GetEnvironmentVariable("BENCH_SDF_MAXTRIS") ?? "50000");
            foreach (var method in new[] { MeshWallThickness.Methods.Ray, MeshWallThickness.Methods.InscribedSphere, MeshWallThickness.Methods.ShapeDiameter }) {
                string key = "wt_" + method.ToString().ToLower();
                if (method == MeshWallThickness.Methods.ShapeDiameter && mesh.TriangleCount > sdfMax) {
                    r[key + "_skipped"] = true;
                    continue;
                }
                var sw = Stopwatch.StartNew();
                var wt = new MeshWallThickness(mesh) { Spatial = spatial, Method = method, ComputeVertices = false };
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
            var spatial = new DMeshAABBTree3(mesh, true);
            var sw = Stopwatch.StartNew();
            var self = new MeshDeviation(mesh, mesh) { ReferenceSpatial = spatial };
            if (self.Compute())
                r["dev_self_max_rel"] = Rel(self.Statistics.MaxAbsolute, diag);
            r["dev_self_ms"] = sw.ElapsedMilliseconds;

            // the two sign modes on random points of the (expanded) bounding box, away from the surface
            var pn = new MeshDeviation(mesh, mesh) { ReferenceSpatial = spatial, Sign = MeshDeviation.Signs.PseudoNormal };
            var wn = new MeshDeviation(mesh, mesh) { ReferenceSpatial = spatial, Sign = MeshDeviation.Signs.WindingNumber };
            spatial.FastWindingNumber(Vector3d.Zero);
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
