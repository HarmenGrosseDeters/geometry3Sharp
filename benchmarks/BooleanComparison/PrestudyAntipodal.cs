// Exact Boolean 3.0 pre-study, T5 counting step (docs/design/EXACT_BOOLEAN_30_PRESTUDY.md section 5.1): the cost
// model of the Antipodal Method (Martens, Trettner, Bessmeltsev 2026, arXiv:2605.01536; paper only, nothing ported)
// against today's per-piece fast winding numbers, on an open-cohort case.
//   prestudy-antipodal <case dir>
// Per input mesh (A, and B = A moved, which has the same counts):
//   - boundary segments: edges welded by exact position, each counted |n(a->b) - n(b->a)| times (the segments whose
//     terms do not cancel; a soup of unwelded triangles has all its edges here);
//   - the queries: the double centroids of the arrangement pieces (as the per-piece path);
//   - t_fwn: DMeshAABBTree3.FastWindingNumber per query (serial, on a fixed subsample);
//   - t_edge: the per-segment term of the method (Van Oosterom-Strackee, 2 atan2(A.(B x x1), 1 + A.B + A.x1 + B.x1) with
//     A, B the unit vectors to the segment ends), summed over this mesh's boundary segments for sampled queries;
//   - t_ray: a ray from the query along +x against the mesh's DMeshAABBTree3 (FindAllHitTriangles, whose hit test uses
//     exact predicates: a stand-in for the boolean's exact ray);
//   prestudy-antipodal <case dir> --unit-only: the per-segment unit cost alone (no arrangement), per mesh, after a long
//     warm-up (run with DOTNET_TieredCompilation=0 for fully optimized code from the start)
//   - the model: antipodal per query = B t_edge + t_ray, against t_fwn.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;

static class PrestudyAntipodal
{
    public static int Run(string[] args)
    {
        string dir = args[0];
        if (args.Contains("--unit-only"))
            return UnitOnly(dir);
        DMesh3 a = StandardMeshReader.ReadMesh(Path.Combine(dir, "A.off")), b = StandardMeshReader.ReadMesh(Path.Combine(dir, "B.off"));
        var sw = Stopwatch.StartNew();
        var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true };
        arr.Compute();
        long arrMs = sw.ElapsedMilliseconds;
        var pts = new Vector3d[arr.Output.Count];
        for (int i = 0; i < pts.Length; ++i) {
            var ot = arr.Output[i];
            pts[i] = (arr.PointToVector(ot.A) + arr.PointToVector(ot.B) + arr.PointToVector(ot.C)) / 3;
        }
        arr = null;
        var row = new Dictionary<string, object> { ["case"] = Path.GetFileName(dir.TrimEnd('/')), ["tris"] = a.TriangleCount,
            ["queries"] = pts.Length, ["arr_ms"] = arrMs };
        int mi = 0;
        foreach (DMesh3 m in new[] { a, b }) {
            // boundary segments by exact position
            var id = new Dictionary<Vector3d, int>();
            var pos = new List<Vector3d>();
            int vid(int v) {
                Vector3d p = m.GetVertex(v);
                if (id.TryGetValue(p, out int k) == false) { k = pos.Count; id[p] = k; pos.Add(p); }
                return k;
            }
            var count = new Dictionary<long, int>();   // key (min, max), value n(min->max) - n(max->min)
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                int[] c = { vid(f.a), vid(f.b), vid(f.c) };
                for (int e = 0; e < 3; ++e) {
                    int u = c[e], w = c[(e + 1) % 3];
                    if (u == w) continue;
                    long key = (long)Math.Min(u, w) << 32 | (uint)Math.Max(u, w);
                    count[key] = (count.TryGetValue(key, out int s) ? s : 0) + (u < w ? 1 : -1);
                }
            }
            var segA = new List<Vector3d>(); var segB = new List<Vector3d>();
            foreach (var kv in count) {
                int u = (int)(kv.Key >> 32), w = (int)(kv.Key & 0xFFFFFFFF);
                for (int r = 0; r < Math.Abs(kv.Value); ++r) {
                    if (kv.Value > 0) { segA.Add(pos[u]); segB.Add(pos[w]); } else { segA.Add(pos[w]); segB.Add(pos[u]); }
                }
            }
            Vector3d[] sa = segA.ToArray(), sb = segB.ToArray();
            int nb = sa.Length;

            var tree = new DMeshAABBTree3(m, true);
            tree.FastWindingNumber(Vector3d.Zero);
            int stride = Math.Max(1, pts.Length / 20000);
            sw.Restart();
            double chk = 0; int nq = 0;
            for (int q = 0; q < pts.Length; q += stride) { chk += tree.FastWindingNumber(pts[q]); nq++; }
            double fwnUs = sw.Elapsed.TotalMilliseconds * 1000 / nq;

            // the boundary sum (all segments) for sampled queries: a warm-up pass, then passes over the sampled queries
            // until at least 2e6 terms are timed (JIT and caches warm; the time per term does not depend on the query)
            Vector3d x1 = new Vector3d(-1, 0, 0);
            Func<Vector3d, double> boundarySum = (p) => {
                double sum = 0;
                for (int k = 0; k < nb; ++k) {
                    Vector3d A = sa[k] - p, B = sb[k] - p;
                    A /= A.Length; B /= B.Length;
                    sum += 2 * Math.Atan2(A.Dot(B.Cross(x1)), 1 + A.Dot(B) + A.Dot(x1) + B.Dot(x1));
                }
                return sum;
            };
            double edgeNs = 0;
            if (nb > 0) {
                int estride = Math.Max(1, pts.Length / 64);
                for (int q = 0; q < pts.Length && q < 3 * estride; q += estride) chk += boundarySum(pts[q]);
                long terms = 0;
                sw.Restart();
                while (terms < 2000000)
                    for (int q = 0; q < pts.Length && terms < 2000000; q += estride) { chk += boundarySum(pts[q]); terms += nb; }
                edgeNs = sw.Elapsed.TotalMilliseconds * 1e6 / terms;
            }

            // the ray (double, all hits) for sampled queries
            int rstride = Math.Max(1, pts.Length / 20000);
            var hits = new List<int>();
            sw.Restart();
            int nr = 0; long nh = 0;
            for (int q = 0; q < pts.Length; q += rstride) {
                hits.Clear();
                tree.FindAllHitTriangles(new Ray3d(pts[q], Vector3d.AxisX), hits);
                nh += hits.Count; nr++;
            }
            double rayUs = sw.Elapsed.TotalMilliseconds * 1000 / nr;

            double antipodalUs = nb * edgeNs / 1000 + rayUs;
            row["mesh" + mi] = new Dictionary<string, object> {
                ["boundary_segments"] = nb, ["welded_vertices"] = pos.Count, ["fwn_us_per_query"] = Math.Round(fwnUs, 2),
                ["ns_per_segment"] = Math.Round(edgeNs, 2), ["ray_us"] = Math.Round(rayUs, 2), ["ray_hits_mean"] = Math.Round(nh / (double)nr, 1),
                ["antipodal_us_per_query"] = Math.Round(antipodalUs, 2), ["ratio_antipodal_over_fwn"] = Math.Round(antipodalUs / fwnUs, 3),
                ["fwn_s_all_queries"] = Math.Round(fwnUs * pts.Length / 1e6, 1), ["antipodal_s_all_queries"] = Math.Round(antipodalUs * pts.Length / 1e6, 1),
            };
            GC.KeepAlive(chk);
            mi++;
        }
        Console.WriteLine(JsonSerializer.Serialize(row));
        return 0;
    }

    // exact equality, a hash that mixes all bits (Vector3d.GetHashCode collides heavily on grid-like coordinates:
    // 2.3 s for the 68k triangles of 107554 against about 0.02 s with this one)
    sealed class PositionComparer : IEqualityComparer<Vector3d>
    {
        public bool Equals(Vector3d a, Vector3d b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public int GetHashCode(Vector3d v)
        {
            ulong h = (ulong)BitConverter.DoubleToInt64Bits(v.x + 0.0) * 0x9E3779B97F4A7C15UL;
            h = (h ^ (h >> 29) ^ (ulong)BitConverter.DoubleToInt64Bits(v.y + 0.0)) * 0xBF58476D1CE4E5B9UL;
            h = (h ^ (h >> 31) ^ (ulong)BitConverter.DoubleToInt64Bits(v.z + 0.0)) * 0x94D049BB133111EBUL;
            return (int)(h ^ (h >> 32));
        }
    }

    // boundary segments welded by exact position (as in Run), as pairs of points
    static Vector3d[] Segments(DMesh3 m)
    {
        var id = new Dictionary<Vector3d, int>(new PositionComparer()); var pos = new List<Vector3d>();
        var count = new Dictionary<long, int>(EdgeKeyComparer.Instance); var order = new List<long>();
        int vid(int v) { Vector3d p = m.GetVertex(v); if (id.TryGetValue(p, out int k) == false) { k = pos.Count; id[p] = k; pos.Add(p); } return k; }
        foreach (int t in m.TriangleIndices()) {
            Index3i f = m.GetTriangle(t);
            int[] c = { vid(f.a), vid(f.b), vid(f.c) };
            for (int e = 0; e < 3; ++e) {
                int u = c[e], w = c[(e + 1) % 3];
                if (u == w) continue;
                long key = (long)Math.Min(u, w) << 32 | (uint)Math.Max(u, w);
                if (count.TryGetValue(key, out int s0) == false) { s0 = 0; order.Add(key); }
                count[key] = s0 + (u < w ? 1 : -1);
            }
        }
        var seg = new List<Vector3d>();
        foreach (long key in order) {
            int u = (int)(key >> 32), w = (int)(key & 0xFFFFFFFF), n = count[key];
            for (int r = 0; r < Math.Abs(n); ++r) { seg.Add(pos[n > 0 ? u : w]); seg.Add(pos[n > 0 ? w : u]); }
        }
        return seg.ToArray();
    }

    // the per-segment unit cost per mesh: boundary segments as above, queries at triangle centroids, 20 untimed passes,
    // then at least 2e7 terms timed (both meshes alike)
    static int UnitOnly(string dir)
    {
        var row = new Dictionary<string, object> { ["case"] = Path.GetFileName(dir.TrimEnd('/')) };
        int mi = 0;
        foreach (string f in new[] { "A.off", "B.off" }) {
            var swr = Stopwatch.StartNew();
            DMesh3 m = StandardMeshReader.ReadMesh(Path.Combine(dir, f));
            long readMs = swr.ElapsedMilliseconds; swr.Restart();
            Vector3d[] seg = Segments(m);
            Console.Error.WriteLine($"{f}: read {readMs} ms, segments {swr.ElapsedMilliseconds} ms");
            int nb = seg.Length / 2;
            int stride = Math.Max(1, m.TriangleCount / 64);
            var q = m.TriangleIndices().Where((t, k) => k % stride == 0).Select(t => m.GetTriCentroid(t)).ToArray();
            Vector3d x1 = new Vector3d(-1, 0, 0);
            double chk = 0;
            Func<Vector3d, double> sum = (p) => {
                double s2 = 0;
                for (int k = 0; k < seg.Length; k += 2) {
                    Vector3d A = seg[k] - p, B = seg[k + 1] - p;
                    A /= A.Length; B /= B.Length;
                    s2 += 2 * Math.Atan2(A.Dot(B.Cross(x1)), 1 + A.Dot(B) + A.Dot(x1) + B.Dot(x1));
                }
                return s2;
            };
            double ns = 0;
            if (nb > 0) {
                for (int w = 0; w < 20; ++w) foreach (var p in q) chk += sum(p);
                long terms = 0;
                var sw = Stopwatch.StartNew();
                while (terms < 20000000) foreach (var p in q) { chk += sum(p); terms += nb; }
                ns = sw.Elapsed.TotalMilliseconds * 1e6 / terms;
            }
            row["mesh" + mi] = new Dictionary<string, object> { ["boundary_segments"] = nb, ["ns_per_segment"] = Math.Round(ns, 2) };
            GC.KeepAlive(chk);
            mi++;
        }
        Console.WriteLine(JsonSerializer.Serialize(row));
        return 0;
    }
}
