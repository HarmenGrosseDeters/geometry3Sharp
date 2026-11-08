// Directed, sampled result -> reference distances in the fixed Z-up frame.
// Sampling follows MeshFidelityMetrics.sample_surface: stratified cumulative area,
// sqrt barycentric sampling (uniform on a triangle), System.Random with a fixed seed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

namespace TerrainBenchmark
{
    public sealed class ClosestFields
    {
        readonly DMesh3 reference = new DMesh3(MeshComponents.None);
        readonly DMeshAABBTree3 tree;
        public readonly int InvalidReferenceTriangles;

        public ClosestFields(DMesh3 source)
        {
            foreach (int tid in source.TriangleIndices()) {
                Triangle3d t = Triangle(source, tid);
                if (!Valid(t, out _)) { InvalidReferenceTriangles++; continue; }
                int a = reference.AppendVertex(t.V0), b = reference.AppendVertex(t.V1), c = reference.AppendVertex(t.V2);
                reference.AppendTriangle(a, b, c);
            }
            if (reference.TriangleCount > 0) tree = new DMeshAABBTree3(reference, true);
        }

        static Triangle3d Triangle(DMesh3 mesh, int tid)
        {
            var t = new Triangle3d();
            mesh.GetTriVertices(tid, ref t.V0, ref t.V1, ref t.V2);
            return t;
        }

        static bool Finite(Vector3d p) => double.IsFinite(p.x) && double.IsFinite(p.y) && double.IsFinite(p.z);
        static bool Valid(Triangle3d t, out double area)
        {
            area = (t.V1 - t.V0).Cross(t.V2 - t.V0).Length;
            return Finite(t.V0) && Finite(t.V1) && Finite(t.V2) && double.IsFinite(area) && area > 0;
        }

        // No XY window or distance cap. Equal-distance ties follow the fixed tree's traversal.
        public bool TryClosest(Vector3d p, out Vector3d q)
        {
            q = Vector3d.Zero;
            if (tree == null || !Finite(p)) return false;
            int tid = tree.FindNearestTriangle(p);
            if (tid < 0) return false;
            var query = new DistPoint3Triangle3(p, Triangle(reference, tid)).Compute();
            q = query.TriangleClosest;
            return Finite(q) && double.IsFinite((p - q).Length);
        }

        public void Measure(IEnumerable<Vector3d> samples, int requested, int seed, Dictionary<string, object> row)
        {
            var d3 = new List<double>(); var dh = new List<double>(); var dv = new List<double>();
            int seen = 0;
            foreach (Vector3d p in samples) {
                seen++;
                if (!TryClosest(p, out Vector3d q)) continue;
                Vector3d d = p - q;
                double h = new Vector2d(d.x, d.y).Length, v = Math.Abs(d.z), length = d.Length;
                if (!double.IsFinite(h) || !double.IsFinite(v) || !double.IsFinite(length)) continue;
                d3.Add(length); dh.Add(h); dv.Add(v);
            }
            row["closest_direction"] = "result_to_reference";
            row["closest_units"] = "m";
            row["closest_sampling"] = "area_weighted_stratified";
            row["closest_seed"] = seed;
            row["closest_requested"] = requested;
            row["closest_valid"] = d3.Count;
            row["closest_invalid"] = seen - d3.Count;
            row["closest_missing"] = Math.Max(0, requested - seen);
            row["closest_invalid_reference_triangles"] = InvalidReferenceTriangles;
            row["closest_status"] = d3.Count == 0 ? "unavailable" :
                d3.Count != requested || InvalidReferenceTriangles > 0 ? "partial" : "ok";
            Stats(d3, "closest_d3", row); Stats(dh, "closest_dh", row); Stats(dv, "closest_dv", row);
        }

        static void Stats(List<double> values, string prefix, Dictionary<string, object> row)
        {
            row[prefix + "_n"] = values.Count;
            foreach (string suffix in new[] { "min", "p01", "p50", "p99", "max", "mean", "rms" })
                row.Remove(prefix + "_" + suffix);
            if (values.Count == 0) return;
            values.Sort();
            double max = values[values.Count - 1], mean = 0, squared = 0;
            // Normalize first so that finite input statistics cannot overflow in the sum of squares.
            foreach (double v in values) { double x = max == 0 ? 0 : v / max; mean += x / values.Count; squared += x * x / values.Count; }
            row[prefix + "_min"] = values[0];
            row[prefix + "_p01"] = values[(int)(0.01 * (values.Count - 1))];
            row[prefix + "_p50"] = values[(int)(0.5 * (values.Count - 1))];
            row[prefix + "_p99"] = values[(int)(0.99 * (values.Count - 1))];
            row[prefix + "_max"] = max;
            row[prefix + "_mean"] = Math.Min(1, mean) * max;
            row[prefix + "_rms"] = Math.Sqrt(Math.Min(1, squared)) * max;
        }

        public static IEnumerable<Vector3d> Samples(DMesh3 result, int count, int seed, Dictionary<string, object> row)
        {
            var triangles = new List<Triangle3d>(); var cumulative = new List<double>();
            double total = 0; int invalid = 0;
            foreach (int tid in result.TriangleIndices()) {
                Triangle3d t = Triangle(result, tid);
                if (!Valid(t, out double area)) { invalid++; continue; }
                total += area; triangles.Add(t); cumulative.Add(total);
            }
            row["closest_invalid_result_triangles"] = invalid;
            if (triangles.Count == 0 || !double.IsFinite(total)) yield break;
            var random = new Random(seed);
            for (int i = 0; i < count; i++) {
                double u = (i + random.NextDouble()) / count * total;
                int k = cumulative.BinarySearch(u); k = k < 0 ? ~k : k + 1;
                Triangle3d t = triangles[Math.Min(k, triangles.Count - 1)];
                double s = Math.Sqrt(random.NextDouble()), v = random.NextDouble();
                yield return (1 - s) * t.V0 + s * (1 - v) * t.V1 + s * v * t.V2;
            }
        }

        public static void Add(DMesh3 reference, DMesh3 result, Dictionary<string, object> row, int count = 10000, int seed = 0)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            var sw = Stopwatch.StartNew();
            var metric = new ClosestFields(reference);
            row["closest_tree_s"] = sw.Elapsed.TotalSeconds;
            sw.Restart();
            metric.Measure(Samples(result, count, seed, row), count, seed, row);
            if ((int)row["closest_invalid_result_triangles"] > 0 && (string)row["closest_status"] == "ok") row["closest_status"] = "partial";
            row["closest_measure_s"] = sw.Elapsed.TotalSeconds;
        }
    }
}
