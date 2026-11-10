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
//   - t_ray: a ray from the query along +x against the mesh's DMeshAABBTree3 (FindAllHitTriangles; a double ray, a lower
//     bound of an exact ray);
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

            // the boundary sum for a few sampled queries (all segments each)
            int edgeQueries = Math.Max(3, Math.Min(200, (int)(2e7 / Math.Max(1, nb))));
            int estride = Math.Max(1, pts.Length / edgeQueries);
            Vector3d x1 = new Vector3d(-1, 0, 0);
            sw.Restart();
            long terms = 0;
            for (int q = 0; q < pts.Length && terms < 2e7 + nb; q += estride) {
                Vector3d p = pts[q];
                double sum = 0;
                for (int k = 0; k < nb; ++k) {
                    Vector3d A = sa[k] - p, B = sb[k] - p;
                    double la = A.Length, lb = B.Length;
                    A /= la; B /= lb;
                    sum += 2 * Math.Atan2(A.Dot(B.Cross(x1)), 1 + A.Dot(B) + A.Dot(x1) + B.Dot(x1));
                }
                chk += sum;
                terms += nb;
            }
            double edgeNs = nb > 0 ? sw.Elapsed.TotalMilliseconds * 1e6 / terms : 0;

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
}
