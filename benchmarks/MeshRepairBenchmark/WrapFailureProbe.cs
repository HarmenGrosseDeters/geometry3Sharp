using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using g3;
using gs;

namespace MeshRepairBenchmark
{
    // Diagnostic replay of the real measurement functions, not an alternative definition of fidelity.
    internal static class WrapFailureProbe
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        delegate bool Interpolate(DMesh3 mesh, MeshCurvature curvature, int tid, Vector3d bary, out double h, out double k);
        static readonly Interpolate Interpolator = (Interpolate)Delegate.CreateDelegate(typeof(Interpolate),
            typeof(CurvatureAgreement).GetMethod("interpolate", PrivateStatic));

        internal static Dictionary<string, object> Capture(DMesh3 reference, DMesh3 result,
            int surfaceSamples = 50000, int edgeSamples = 10000, int witnesses = 3)
        {
            if (surfaceSamples <= 0 || edgeSamples <= 0 || witnesses <= 0 || witnesses > 10)
                throw new ArgumentOutOfRangeException();
            Vector3d center;
            double size = MeshFidelityMetrics.SizeOf(reference, out center);
            if (!(size > 0) || !MathUtil.IsFinite(size)) throw new ArgumentException("Invalid reference size");
            var copy = typeof(MeshFidelityMetrics).GetMethod("local_copy", PrivateStatic);
            var r = (DMesh3)copy.Invoke(null, new object[] { reference, center });
            var m = (DMesh3)copy.Invoke(null, new object[] { result, center });
            var f = new MeshFidelityMetrics(reference, result) { SurfaceSamples = surfaceSamples, EdgeSamples = edgeSamples, Parallel = false };
            double area = 1e-12 * size * size;
            var random = new Random(f.Seed);
            var onMesh = MeshFidelityMetrics.sample_surface(m, surfaceSamples, random, area);
            var onRef = MeshFidelityMetrics.sample_surface(r, surfaceSamples, random, area);
            if (onMesh.Length == 0 || onRef.Length == 0) throw new ArgumentException("No nondegenerate samples");
            Type surface = typeof(MeshFidelityMetrics).GetNestedType("Surface", BindingFlags.NonPublic);
            var query = typeof(MeshFidelityMetrics).GetMethod("query", PrivateInstance);
            query.Invoke(f, new object[] { onMesh, Activator.CreateInstance(surface, r, area) });
            query.Invoke(f, new object[] { onRef, Activator.CreateInstance(surface, m, area) });
            var cm = MeshCurvature.Compute(m);
            var cr = MeshCurvature.Compute(r);
            typeof(MeshFidelityMetrics).GetMethod("curvature", PrivateInstance).Invoke(f,
                new object[] { r, m, onRef, onMesh, size, 0.01 * size });
            var refEdges = MeshFidelityMetrics.SharpEdges(r, f.SharpEdgeAngle, f.SharpEdgeMinHeight * size);
            var meshEdges = MeshFidelityMetrics.SharpEdges(m, f.SharpEdgeAngle, f.SharpEdgeMinHeight * size);
            var edgeMethod = typeof(MeshFidelityMetrics).GetMethods(PrivateInstance)
                .Single(method => method.Name == "edge_distances" && method.GetParameters().Length == 5);
            object[] edgeM = { meshEdges, refEdges, random, null, null };
            object[] edgeR = { refEdges, meshEdges, random, null, null };
            double[] dm = (double[])edgeMethod.Invoke(f, edgeM);
            double[] dr = (double[])edgeMethod.Invoke(f, edgeR);
            double em = Mean(dm) / size, er = Mean(dr) / size;
            double ec = meshEdges.Count == 0 && refEdges.Count == 0 ? 0 :
                meshEdges.Count == 0 || refEdges.Count == 0 ? double.PositiveInfinity : 0.5 * (em + er);
            var quality = TriangleQualityStatistics.Compute(m);
            var replay = new Dictionary<string, object> {
                ["fm_size"] = size,
                ["fm_max_fwd"] = onMesh.Max(s => s.Distance) / size,
                ["fm_max_back"] = onRef.Max(s => s.Distance) / size,
                ["fm_reversed_fwd"] = NormalAgreement.Compute(onMesh, 0.01 * size).ReversedFraction,
                ["fm_reversed_back"] = NormalAgreement.Compute(onRef, 0.01 * size).ReversedFraction,
                ["fm_ecd_fwd"] = em, ["fm_ecd_back"] = er, ["fm_ecd"] = ec,
                ["fm_h_err"] = f.MeanCurvatureError, ["fm_k_err"] = f.GaussianCurvatureError,
                ["fm_h_w1"] = f.MeanCurvatureDistance, ["fm_k_w1"] = f.GaussianCurvatureDistance
            };
            return new Dictionary<string, object> {
                ["diagnostic_only"] = true, ["size"] = size, ["coordinate_origin"] = V(center),
                ["coordinate_frame"] = "Coordinates below are relative to coordinate_origin; add it for world coordinates.",
                ["seed"] = f.Seed, ["surface_samples"] = surfaceSamples, ["edge_samples"] = edgeSamples,
                ["replay"] = replay, ["quality"] = Quality(quality),
                ["worst_triangles"] = m.TriangleIndices().Select(tid => new { tid, angle = Angle(m, tid) })
                    .OrderBy(x => x.angle).ThenBy(x => x.tid).Take(witnesses)
                    .Select(x => Patch(m, cm, x.tid)).ToArray(),
                ["mesh_to_reference"] = SurfaceWitnesses(onMesh, m, cm, r, cr, size, witnesses),
                ["reference_to_mesh"] = SurfaceWitnesses(onRef, r, cr, m, cm, size, witnesses),
                ["edge_mesh_to_reference"] = EdgeWitnesses(dm, (Vector3d[])edgeM[3], (int[])edgeM[4], meshEdges, refEdges, size, witnesses),
                ["edge_reference_to_mesh"] = EdgeWitnesses(dr, (Vector3d[])edgeR[3], (int[])edgeR[4], refEdges, meshEdges, size, witnesses),
                ["limits"] = "Top local witnesses, not an edit history. Curvature distribution tails do not individually decompose Wasserstein distance. Exhaustive queries reuse the point-triangle/segment arithmetic and test accelerator completeness only."
            };
        }

        internal static void ValidateReplay(Dictionary<string, object> capture, Dictionary<string, object> recorded)
        {
            var replay = (Dictionary<string, object>)capture["replay"];
            foreach (var kv in replay) {
                if (!recorded.TryGetValue(kv.Key, out object value) || !(value is double expected) || !expected.Equals((double)kv.Value))
                    throw new InvalidOperationException("Diagnostic replay differs from recorded metric: " + kv.Key);
            }
            var expectedQuality = (Dictionary<string, object>)recorded["fm_q_out"];
            var quality = (Dictionary<string, object>)capture["quality"];
            foreach (string key in new[] { "minangle", "maxangle", "sj", "aspect", "cond" })
                if (!((double[])expectedQuality[key]).SequenceEqual((double[])quality[key]))
                    throw new InvalidOperationException("Diagnostic triangle quality differs: " + key);
        }

        internal static void Write(string path, DMesh3 reference, DMesh3 result, Dictionary<string, object> recorded)
        {
            if (File.Exists(path)) throw new IOException("Refusing to overwrite a failure capture");
            var capture = Capture(reference, result);
            ValidateReplay(capture, recorded);
            capture["file"] = recorded["file"];
            capture["policy"] = recorded["robust_wrap_policy"];
            capture["replay_matches_recorded"] = true;
            var options = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(stream, capture, options);
        }

        static object SurfaceWitnesses(MeshFidelityMetrics.SurfaceSample[] samples, DMesh3 own, MeshCurvature co,
            DMesh3 other, MeshCurvature ct, double size, int count)
        {
            var values = new List<(int index, double h, double k, double absK)>();
            for (int i = 0; i < samples.Length; ++i) {
                var s = samples[i];
                if (!Interpolator(own, co, s.Tid, s.Bary, out double h0, out double k0)) continue;
                double h = double.NaN, k = double.NaN;
                if (s.Distance < 0.01 * size && Interpolator(other, ct, s.ClosestTid, s.ClosestBary, out double h1, out double k1)) {
                    h = Math.Abs(h0 * size - h1 * size);
                    k = Math.Abs(k0 * size * size - k1 * size * size);
                }
                values.Add((i, h, k, Math.Abs(k0 * size * size)));
            }
            object[] Describe(IEnumerable<int> ids) => ids.Select(i => Sample(i, samples[i], own, co, other, ct, size)).ToArray();
            return new Dictionary<string, object> {
                ["max_distance"] = Describe(Enumerable.Range(0, samples.Length).OrderByDescending(i => samples[i].Distance).ThenBy(i => i).Take(count)),
                ["reversed_matched"] = Describe(Enumerable.Range(0, samples.Length)
                    .Where(i => samples[i].Distance < 0.01 * size && samples[i].Cos < -NormalAgreement.ReversedCos)
                    .OrderBy(i => samples[i].Cos).ThenBy(i => i).Take(count)),
                ["mean_curvature_error"] = Describe(values.Where(x => MathUtil.IsFinite(x.h)).OrderByDescending(x => x.h).ThenBy(x => x.index).Take(count).Select(x => x.index)),
                ["gaussian_curvature_error"] = Describe(values.Where(x => MathUtil.IsFinite(x.k)).OrderByDescending(x => x.k).ThenBy(x => x.index).Take(count).Select(x => x.index)),
                ["gaussian_distribution_tail"] = Describe(values.Where(x => MathUtil.IsFinite(x.absK)).OrderByDescending(x => x.absK).ThenBy(x => x.index).Take(count).Select(x => x.index))
            };
        }

        static object Sample(int index, MeshFidelityMetrics.SurfaceSample s, DMesh3 own, MeshCurvature co, DMesh3 other, MeshCurvature ct, double size)
        {
            Interpolator(own, co, s.Tid, s.Bary, out double h0, out double k0);
            Interpolator(other, ct, s.ClosestTid, s.ClosestBary, out double h1, out double k1);
            return new Dictionary<string, object> {
                ["sample_index"] = index, ["point"] = V(s.Point), ["normal"] = V(s.Normal),
                ["source_triangle"] = s.Tid, ["barycentric"] = V(s.Bary), ["closest_triangle"] = s.ClosestTid,
                ["closest_barycentric"] = V(s.ClosestBary), ["normal_dot"] = s.Cos,
                ["distance_relative"] = s.Distance / size, ["within_curvature_match"] = s.Distance < 0.01 * size,
                ["source_H_size"] = h0 * size, ["target_H_size"] = h1 * size,
                ["source_K_size2"] = k0 * size * size, ["target_K_size2"] = k1 * size * size,
                ["source_patch"] = Patch(own, co, s.Tid), ["target_patch"] = Patch(other, ct, s.ClosestTid),
                ["nearest_check"] = ExhaustiveNearest(other, s.Point, s.ClosestTid)
            };
        }

        static object ExhaustiveNearest(DMesh3 mesh, Vector3d point, int selected)
        {
            double best = double.PositiveInfinity, chosen = double.PositiveInfinity;
            int bestId = -1;
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i ids = mesh.GetTriangle(tid);
                var tri = new Triangle3d(mesh.GetVertex(ids.a), mesh.GetVertex(ids.b), mesh.GetVertex(ids.c));
                double d = DistPoint3Triangle3.DistanceSqr(ref point, ref tri);
                if (tid == selected) chosen = d;
                if (d < best) { best = d; bestId = tid; }
            }
            return new { exhaustive_triangle = bestId, exhaustive_distance_squared = best,
                selected_distance_squared = chosen, selected_attains_exhaustive_minimum = chosen.Equals(best) };
        }

        static object EdgeWitnesses(double[] distances, Vector3d[] points, int[] segments,
            List<Vector3d> edges, List<Vector3d> other, double size, int count)
        {
            return Enumerable.Range(0, distances.Length).OrderByDescending(i => distances[i]).ThenBy(i => i).Take(count).Select(i => {
                double best = double.PositiveInfinity;
                int nearest = -1;
                Vector3d closest = Vector3d.Zero;
                for (int j = 0; j < other.Count / 2; ++j) {
                    var segment = new Segment3d(other[2 * j], other[2 * j + 1]);
                    Vector3d candidate = segment.NearestPoint(points[i]);
                    double d = candidate.DistanceSquared(points[i]);
                    if (d < best) { best = d; nearest = j; closest = candidate; }
                }
                return new Dictionary<string, object> {
                    ["sample_index"] = i, ["point"] = V(points[i]), ["source_segment_index"] = segments[i],
                    ["source_endpoints"] = new[] { V(edges[2 * segments[i]]), V(edges[2 * segments[i] + 1]) },
                    ["distance_relative"] = distances[i] / size, ["exhaustive_segment_index"] = nearest,
                    ["closest_point"] = nearest >= 0 ? V(closest) : null,
                    ["target_endpoints"] = nearest >= 0 ? new[] { V(other[2 * nearest]), V(other[2 * nearest + 1]) } : null,
                    ["exhaustive_distance_relative"] = Math.Sqrt(best) / size
                };
            }).ToArray();
        }

        static object Patch(DMesh3 mesh, MeshCurvature curvature, int tid)
        {
            if (tid < 0) return null;
            Index3i t = mesh.GetTriangle(tid);
            var ring = new HashSet<int>();
            for (int i = 0; i < 3; ++i)
                foreach (int neighbor in mesh.VtxTrianglesItr(t[i])) ring.Add(neighbor);
            var vids = new SortedSet<int>();
            foreach (int tri in ring) { Index3i v = mesh.GetTriangle(tri); vids.Add(v.a); vids.Add(v.b); vids.Add(v.c); }
            return new Dictionary<string, object> {
                ["triangle"] = tid, ["minimum_angle"] = Angle(mesh, tid),
                ["triangles"] = ring.OrderBy(id => id).Select(id => { Index3i v = mesh.GetTriangle(id); return new[] { id, v.a, v.b, v.c }; }).ToArray(),
                ["vertices"] = vids.Select(id => new { id, point = V(mesh.GetVertex(id)),
                    valid_curvature = curvature.Valid[id], mixed_area = curvature.MixedArea[id],
                    mean_curvature = curvature.Mean[id], gaussian_curvature = curvature.Gaussian[id], angle_deficit = curvature.AngleDeficit[id] }).ToArray()
            };
        }

        static double Angle(DMesh3 mesh, int tid)
        {
            Index3i t = mesh.GetTriangle(tid);
            TriangleQualityStatistics.Measure(mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c),
                out double angle, out _, out _, out _, out _);
            return angle;
        }
        static double Mean(double[] values) { double sum = 0; foreach (double value in values) sum += value; return values.Length > 0 ? sum / values.Length : double.NaN; }
        static double[] V(Vector3d v) => new[] { v.x, v.y, v.z };
    }
}
