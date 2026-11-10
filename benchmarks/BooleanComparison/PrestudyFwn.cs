// Exact Boolean 3.0 pre-study, topic 5 (docs/design/EXACT_BOOLEAN_30_PRESTUDY.md): an upper bound for a batched /
// dual-tree fast winding number (variant B) against the per-piece path of open operands (one DMeshAABBTree3
// FastWindingNumber query per representative piece and open mesh, branch claude/open-operand-per-piece).
//   prestudy-fwn <case dir> [--leaf <points per target leaf, default 16>]
// For each input mesh (both are treated as open: the per-piece path queries every open mesh at every piece):
//   - the queries: the double centroid of every arrangement piece (ExactMeshArrangement of A and B, as the boolean);
//   - t_lib: the library's FastWindingNumber over all queries, serial (the per-piece cost, one thread);
//   - counts of the same single-query traversal (Barill 2018 criterion, beta = FWNBeta = 2, order 2): leaf triangle
//     terms (near) and node expansions (far) per query, from a copy of the traversal over the same tree;
//   - a dual-tree count (Barnes-Hut / FMM style): a target tree over the queries, interaction of target node t and
//     source node s far when |c_t - c_s| > beta (R_s + r_t); far pairs become one multipole-to-local translation each,
//     near target leaves fall back to the single-query traversal per point;
//   - unit costs of a triangle term and an order-2 expansion (micro benchmark), and the bound: the B work with
//     M2L and L2P counted at 0 (the near work alone, a strict lower bound of any B with this criterion) and with
//     M2L = 10 and L2P = 1 order-2 evaluations (an estimate).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;

static class PrestudyFwn
{
    // the library tree with its fast winding coefficients recomputed per node (same function as the library's cache)
    // and the library's single-query traversal copied with counters
    sealed class CountingTree : DMeshAABBTree3
    {
        public Vector3d[] C; public double[] R; public Vector3d[] O1; public Matrix3d[] O2; public bool[] Has;
        public double Beta;
        public long Near, Far, Visits;

        public CountingTree(DMesh3 m) : base(m, true) { }

        public int Root { get { return root_index; } }
        public bool IsLeaf(int box) { return box_to_index[box] < triangles_end; }
        public IEnumerable<int> Children(int box)
        {
            int idx = box_to_index[box];
            int c1 = index_list[idx];
            if (c1 < 0) { yield return -c1 - 1; yield break; }
            yield return c1 - 1;
            yield return index_list[idx + 1] - 1;
        }
        public int LeafCount(int box) { return index_list[box_to_index[box]]; }

        public void BuildCoefficients()
        {
            int n = box_to_index.size;
            C = new Vector3d[n]; R = new double[n]; O1 = new Vector3d[n]; O2 = new Matrix3d[n]; Has = new bool[n];
            Beta = FWNBeta;
            var cache = new MeshTriInfoCache(Mesh);
            collect(root_index, 0, cache);
        }

        List<int> collect(int box, int depth, MeshTriInfoCache cache)
        {
            var tris = new List<int>();
            int idx = box_to_index[box];
            if (idx < triangles_end) {
                int k = index_list[idx];
                for (int i = 1; i <= k; ++i) tris.Add(index_list[idx + i]);
                return tris;
            }
            foreach (int c in Children(box).ToList())
                tris.AddRange(collect(c, depth + 1, cache));
            // (as the library: no cache at the root, and a node with one child shares its child's triangles)
            if (depth > 0 && index_list[idx] >= 0) {
                FastTriWinding.ComputeCoeffs(Mesh, tris, ref C[box], ref R[box], ref O1[box], ref O2[box], cache);
                Has[box] = true;
            }
            return tris;
        }

        public bool Contains(int box, Vector3d p) { return box_contains(box, p); }

        // the single-query traversal (branch_fast_winding_num) with counters, from node box
        public double Single(int box, Vector3d p)
        {
            Visits++;
            double sum = 0;
            int idx = box_to_index[box];
            if (idx < triangles_end) {
                int k = index_list[idx];
                for (int i = 1; i <= k; ++i) {
                    Index3i t = Mesh.GetTriangle(index_list[idx + i]);
                    Vector3d a = Mesh.GetVertex(t.a), b = Mesh.GetVertex(t.b), c = Mesh.GetVertex(t.c);
                    sum += MathUtil.TriSolidAngle(a, b, c, ref p) / MathUtil.FourPI;
                }
                Near += k;
                return sum;
            }
            foreach (int c in Children(box)) {
                if (box_contains(c, p) == false && Has[c] && C[c].Distance(p) > Beta * R[c]) {
                    Far++;
                    sum += FastTriWinding.EvaluateOrder2Approx(ref C[c], ref O1[c], ref O2[c], ref p);
                } else
                    sum += Single(c, p);
            }
            return sum;
        }
    }

    // target tree over the query points: median splits on the longest axis
    sealed class Target
    {
        public Vector3d C; public double R; public int Lo, Hi; public Target A, B;
    }

    static Target BuildTarget(Vector3d[] pts, int[] order, int lo, int hi, int leaf)
    {
        var t = new Target { Lo = lo, Hi = hi };
        var box = AxisAlignedBox3d.Empty;
        for (int i = lo; i < hi; ++i) box.Contain(pts[order[i]]);
        t.C = box.Center;
        double r = 0;
        for (int i = lo; i < hi; ++i) r = Math.Max(r, t.C.Distance(pts[order[i]]));
        t.R = r;
        if (hi - lo > leaf) {
            int axis = box.Width >= box.Height && box.Width >= box.Depth ? 0 : box.Height >= box.Depth ? 1 : 2;
            Array.Sort(order, lo, hi - lo, Comparer<int>.Create((x, y) => pts[x][axis].CompareTo(pts[y][axis])));
            int mid = (lo + hi) / 2;
            t.A = BuildTarget(pts, order, lo, mid, leaf);
            t.B = BuildTarget(pts, order, mid, hi, leaf);
        }
        return t;
    }

    sealed class DualCounts { public long M2L, Near, Far, Points; }

    // dual traversal: target t against source node s (whose contribution has not been accounted yet)
    static void Dual(CountingTree src, Target t, int s, Vector3d[] pts, int[] order, DualCounts dc)
    {
        if (src.Has[s] && t.C.Distance(src.C[s]) > src.Beta * (src.R[s] + t.R)) {
            dc.M2L++;
            return;
        }
        if (t.A == null) {
            // target leaf: the single-query traversal from s per point (its own near and far terms)
            long n0 = src.Near, f0 = src.Far;
            for (int i = t.Lo; i < t.Hi; ++i) {
                Vector3d p = pts[order[i]];
                if (src.Contains(s, p) == false && src.Has[s] && src.C[s].Distance(p) > src.Beta * src.R[s])
                    src.Far++;
                else
                    src.Single(s, p);
            }
            dc.Near += src.Near - n0;
            dc.Far += src.Far - f0;
            src.Near = n0; src.Far = f0;
            return;
        }
        if (src.IsLeaf(s) || t.R > src.R[s]) {
            Dual(src, t.A, s, pts, order, dc);
            Dual(src, t.B, s, pts, order, dc);
        } else {
            foreach (int c in src.Children(s))
                Dual(src, t, c, pts, order, dc);
        }
    }

    public static int Run(string[] args)
    {
        string dir = args[0];
        int leaf = 16;
        for (int i = 1; i < args.Length; ++i)
            if (args[i] == "--leaf") leaf = int.Parse(args[++i]);
        DMesh3 a = StandardMeshReader.ReadMesh(Path.Combine(dir, "A.off")), b = StandardMeshReader.ReadMesh(Path.Combine(dir, "B.off"));
        var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true };
        var sw = Stopwatch.StartNew();
        arr.Compute();
        long arrMs = sw.ElapsedMilliseconds;
        var pts = new Vector3d[arr.Output.Count];
        for (int i = 0; i < pts.Length; ++i) {
            var ot = arr.Output[i];
            pts[i] = (arr.PointToVector(ot.A) + arr.PointToVector(ot.B) + arr.PointToVector(ot.C)) / 3;
        }
        var row = new Dictionary<string, object> { ["case"] = Path.GetFileName(dir.TrimEnd('/')), ["tris_a"] = a.TriangleCount,
            ["tris_b"] = b.TriangleCount, ["pieces"] = pts.Length, ["arr_ms"] = arrMs };

        // unit costs (serial micro benchmark on this machine)
        double tTri, tFar;
        {
            var rnd = new Random(1);
            var v = Enumerable.Range(0, 3000).Select(_ => new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble())).ToArray();
            double s = 0;
            int reps = 3000000;
            sw.Restart();
            for (int i = 0; i < reps; ++i) {
                Vector3d q = v[(i * 7) % 3000];
                s += MathUtil.TriSolidAngle(v[i % 3000], v[(i + 1) % 3000], v[(i + 2) % 3000], ref q);
            }
            tTri = sw.Elapsed.TotalMilliseconds * 1e6 / reps;
            Vector3d c = new Vector3d(0.5, 0.5, 0.5), o1 = new Vector3d(0.1, 0.2, 0.3);
            Matrix3d o2 = new Matrix3d(0.1, 0.0, 0.2, 0.0, 0.3, 0.1, 0.2, 0.1, 0.05);
            sw.Restart();
            for (int i = 0; i < reps; ++i) {
                Vector3d q = v[i % 3000] * 4;
                s += FastTriWinding.EvaluateOrder2Approx(ref c, ref o1, ref o2, ref q);
            }
            tFar = sw.Elapsed.TotalMilliseconds * 1e6 / reps;
            row["ns_triangle_term"] = Math.Round(tTri, 1);
            row["ns_order2_eval"] = Math.Round(tFar, 1);
            GC.KeepAlive(s);
        }

        int mi = 0;
        foreach (DMesh3 m in new[] { a, b }) {
            var lib = new DMeshAABBTree3(m, true);
            sw.Restart();
            lib.FastWindingNumber(Vector3d.Zero);
            long buildMs = sw.ElapsedMilliseconds;
            sw.Restart();
            double check = 0;
            foreach (Vector3d p in pts) check += lib.FastWindingNumber(p);
            double libMs = sw.Elapsed.TotalMilliseconds;

            var ct = new CountingTree(m);
            ct.BuildCoefficients();
            sw.Restart();
            double check2 = 0;
            foreach (Vector3d p in pts) check2 += ct.Single(ct.Root, p);
            double copyMs = sw.Elapsed.TotalMilliseconds;
            long near = ct.Near, far = ct.Far, visits = ct.Visits;

            int[] order = Enumerable.Range(0, pts.Length).ToArray();
            var target = BuildTarget(pts, order, 0, pts.Length, leaf);
            var dc = new DualCounts();
            Dual(ct, target, ct.Root, pts, order, dc);

            double modelSingle = (near * tTri + far * tFar) / 1e6;     // ms
            double bLower = (dc.Near * tTri + dc.Far * tFar) / 1e6;   // M2L and L2P free
            double bEstimate = bLower + (dc.M2L * 10 * tFar + pts.Length * tFar) / 1e6;
            row["mesh" + mi] = new Dictionary<string, object> {
                ["tree_build_ms"] = buildMs, ["lib_queries_ms"] = Math.Round(libMs, 1), ["copy_queries_ms"] = Math.Round(copyMs, 1),
                ["sums_equal"] = Math.Abs(check - check2) <= 1e-9 * Math.Max(1, Math.Abs(check)),
                ["near_per_query"] = Math.Round(near / (double)pts.Length, 1), ["far_per_query"] = Math.Round(far / (double)pts.Length, 1),
                ["visits_per_query"] = Math.Round(visits / (double)pts.Length, 1),
                ["model_single_ms"] = Math.Round(modelSingle, 1),
                ["dual_m2l"] = dc.M2L, ["dual_near_per_query"] = Math.Round(dc.Near / (double)pts.Length, 1),
                ["dual_far_per_query"] = Math.Round(dc.Far / (double)pts.Length, 1),
                ["b_lower_ms"] = Math.Round(bLower, 1), ["b_estimate_ms"] = Math.Round(bEstimate, 1),
                ["bound_speedup_lower"] = Math.Round(libMs / Math.Max(bEstimate, 1e-3), 2),
                ["bound_speedup_upper"] = Math.Round(libMs / Math.Max(bLower, 1e-3), 2),
            };
            mi++;
        }
        Console.WriteLine(JsonSerializer.Serialize(row));
        return 0;
    }
}
