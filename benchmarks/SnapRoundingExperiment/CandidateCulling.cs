// Upper-bound prototype for docs/design/SNAP_ROUNDING_3D.md (section 4): the first full self-intersection search of
// SafeRounding (MeshRoundingRepair.intersecting_pairs_all) on a rounded boolean result, with tighter candidate culling.
// Not library code: it measures what a separating-axis certificate would save, and checks that the set of
// intersecting pairs stays the same.
//
//   cull <case dir with A.off, B.off> <union|intersection|difference> [--serial]
//
// Variants (all search from the triangles with a vertex that is not an input position, as the library's search skips the
// pairs of two exact triangles, docs/design/ROUNDING_EXACT_PAIRS.md; here "exact" is taken from the positions, an
// approximation of the library's flag that is good enough for an upper bound):
//   box       the library's search: TriangleBoxTree.Query with the triangle's box, then the pair test
//             (TriangleTriangleFilter.IntersectsBeyondShared) on every hit
//   box+side  the same hits; before the pair test, the exact side of the hit's three corners to the query triangle's
//             plane (ExactPredicates.Orient3d): all three strictly on one side => apart (no new bound: exact signs)
//   own-box   this file's box tree with box culling only (separates the tree from the certificate)
//   box+axes  the library's hits; before the pair test, the pair is dropped if the two triangles are strictly apart along
//             one of 8 axes computed in doubles: each triangle's normal and the three in-plane normals of its edges. Any
//             vector is a valid separating axis, so only the comparison of the projections needs a rounding margin (the
//             lemma checked in tools/Z3Pilot/separating_axis.py)
//   axes      this file's box tree, a node (and a leaf's box) also culled if it is apart from the query triangle along
//             one of the query's 4 axes; then box+axes' test of the pair
// Output: one JSON line.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using g3;

static class CandidateCulling
{
    public static int Run(string[] args)
    {
        string dir = args[0];
        var op = (ExactMeshBoolean.Operations)Enum.Parse(typeof(ExactMeshBoolean.Operations), args[1], true);
        bool serial = args.Contains("--serial");
        DMesh3 a = StandardMeshReader.ReadMesh(Path.Combine(dir, "A.off")), b = StandardMeshReader.ReadMesh(Path.Combine(dir, "B.off"));
        var boolean = new ExactMeshBoolean(a, b, op) { SafeRounding = false };
        boolean.Compute();
        DMesh3 mesh = boolean.Result;

        // canonical vertex ids: one per position (SharedPositionsTouch), and the input positions
        var inputs = new HashSet<Vector3d>();
        foreach (DMesh3 m in new[] { a, b })
            foreach (int v in m.VertexIndices())
                inputs.Add(m.GetVertex(v));
        var canonOf = new Dictionary<Vector3d, int>();
        var canon = new int[mesh.MaxVertexID];
        var exactV = new bool[mesh.MaxVertexID];
        foreach (int v in mesh.VertexIndices()) {
            Vector3d p = mesh.GetVertex(v);
            if (canonOf.TryGetValue(p, out int c) == false)
                canonOf[p] = c = v;
            canon[v] = c;
            exactV[v] = inputs.Contains(p);
        }
        // the triangles as arrays: corners, canonical ids, boxes; degenerate ones are left out, as in the library's search
        var ids = new List<Index3i>();
        var tris = new List<Triangle3d>();
        var exact = new List<bool>();
        foreach (int t in mesh.TriangleIndices()) {
            Index3i tv = mesh.GetTriangle(t);
            var T = new Triangle3d(mesh.GetVertex(tv.a), mesh.GetVertex(tv.b), mesh.GetVertex(tv.c));
            if (ExactTriangleIntersection.IsDegenerate(ref T))
                continue;
            ids.Add(new Index3i(canon[tv.a], canon[tv.b], canon[tv.c]));
            tris.Add(T);
            exact.Add(exactV[tv.a] && exactV[tv.b] && exactV[tv.c]);
        }
        int n = tris.Count;
        var boxes = new List<AxisAlignedBox3d>(n);
        for (int i = 0; i < n; ++i) {
            var bx = new AxisAlignedBox3d(tris[i].V0);
            bx.Contain(tris[i].V1); bx.Contain(tris[i].V2);
            boxes.Add(bx);
        }
        Index3i[] id = ids.ToArray();
        Triangle3d[] tri = tris.ToArray();
        bool[] ex = exact.ToArray();
        int queries = ex.Count(e => e == false);

        var sw = Stopwatch.StartNew();
        var libTree = new TriangleBoxTree(boxes);
        double libBuild = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var own = new Tree(tri, boxes);
        double ownBuild = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var qs = new Query[n];
        Parallel.For(0, n, (i) => { qs[i] = new Query(ref tri[i]); });
        double axesBuild = sw.Elapsed.TotalMilliseconds;

        var o = new Dictionary<string, object> {
            ["case"] = Path.GetFileName(dir.TrimEnd('/')), ["op"] = args[1].ToLowerInvariant(), ["serial"] = serial,
            ["triangles"] = n, ["queries"] = queries, ["build_ms_lib"] = Math.Round(libBuild, 1), ["build_ms_own"] = Math.Round(ownBuild, 1), ["build_ms_axes"] = Math.Round(axesBuild, 1) };
        long[] reference = null;
        foreach (string variant in new[] { "box", "box+side", "box+axes", "own-box", "axes", "box", "box+side", "box+axes", "own-box", "axes" }) {
            var hits = new long[n]; var tested = new long[n];
            var found = new List<long>[n];
            sw.Restart();
            Action<int> body = (ta) => {
                if (ex[ta])
                    return;
                var list = new List<int>();
                Query q = qs[ta];
                if (variant == "box" || variant == "box+side" || variant == "box+axes")
                    libTree.Query(boxes[ta], list);
                else
                    own.Collect(boxes[ta], variant == "axes" ? q : null, list);
                hits[ta] = list.Count;
                foreach (int tb in list) {
                    // (a pair of two queried triangles once, from the smaller index; one with an exact triangle from the other)
                    if (tb == ta || (tb < ta && ex[tb] == false))
                        continue;
                    if (variant == "box+side" && q.ExactlyApart(ref tri[tb]))
                        continue;
                    if ((variant == "axes" || variant == "box+axes") && (q.ApartFromCorners(ref tri[tb]) || qs[tb].ApartFromCorners(ref tri[ta])))
                        continue;
                    tested[ta]++;
                    bool hit = ta < tb ? TriangleTriangleFilter.IntersectsBeyondShared(id[ta], ref tri[ta], id[tb], ref tri[tb])
                        : TriangleTriangleFilter.IntersectsBeyondShared(id[tb], ref tri[tb], id[ta], ref tri[ta]);
                    if (hit)
                        (found[ta] ?? (found[ta] = new List<long>())).Add(((long)Math.Min(ta, tb) << 32) | (uint)Math.Max(ta, tb));
                }
            };
            if (serial)
                for (int i = 0; i < n; ++i) body(i);
            else
                Parallel.For(0, n, body);
            double ms = sw.Elapsed.TotalMilliseconds;
            long[] pairs = found.Where(l => l != null).SelectMany(l => l).OrderBy(x => x).ToArray();
            if (reference == null)
                reference = pairs;
            // (the second round of each variant is reported: the first warms up)
            o[variant] = new Dictionary<string, object> {
                ["ms"] = Math.Round(ms, 1), ["hits_per_query"] = Math.Round(hits.Sum() / (double)Math.Max(queries, 1), 1),
                ["pair_tests"] = tested.Sum(), ["intersecting"] = pairs.Length, ["same_pairs"] = pairs.SequenceEqual(reference) };
        }
        Console.WriteLine(JsonSerializer.Serialize(o));
        return 0;
    }

    // a query triangle with its 4 axes and its interval on each (doubles; see the file comment)
    sealed class Query
    {
        readonly Vector3d v0, v1, v2;
        readonly Vector3d[] axis = new Vector3d[4];
        readonly double[] lo = new double[4], hi = new double[4], mag = new double[4];
        // relative rounding margin of a projection a.x computed in doubles: |fl(a.x) - a.x| <= Gamma (|a_x x_x| + |a_y x_y| + |a_z x_z|)
        // (3 products and 2 sums, each within one rounding: gamma_3 = 3u / (1 - 3u) < 4u, u = 2^-53; doubled here)
        const double Gamma = 8 * 1.1102230246251565e-16;

        public Query(ref Triangle3d t)
        {
            v0 = t.V0; v1 = t.V1; v2 = t.V2;
            Vector3d nrm = (v1 - v0).Cross(v2 - v0);
            axis[0] = nrm;
            axis[1] = nrm.Cross(v1 - v0); axis[2] = nrm.Cross(v2 - v1); axis[3] = nrm.Cross(v0 - v2);
            for (int k = 0; k < 4; ++k) {
                double p0 = axis[k].Dot(v0), p1 = axis[k].Dot(v1), p2 = axis[k].Dot(v2);
                // (an axis that is not finite never separates)
                bool ok = !(double.IsNaN(p0 + p1 + p2) || double.IsInfinity(p0 + p1 + p2));
                double m = Gamma * Math.Max(absdot(axis[k], v0), Math.Max(absdot(axis[k], v1), absdot(axis[k], v2)));
                lo[k] = ok ? Math.Min(p0, Math.Min(p1, p2)) - m : double.NegativeInfinity;
                hi[k] = ok ? Math.Max(p0, Math.Max(p1, p2)) + m : double.PositiveInfinity;
            }
        }

        static double absdot(Vector3d a, Vector3d x)
        {
            return Math.Abs(a.x * x.x) + Math.Abs(a.y * x.y) + Math.Abs(a.z * x.z);
        }

        // the box [min, max] and the triangle are strictly apart along one of the axes, with the rounding margins
        public bool ApartFromBox(double minx, double miny, double minz, double maxx, double maxy, double maxz)
        {
            for (int k = 0; k < 4; ++k) {
                Vector3d a = axis[k];
                // the box's interval on the axis: per coordinate the corner that gives the smaller and the larger product
                double x0 = a.x * minx, x1 = a.x * maxx, y0 = a.y * miny, y1 = a.y * maxy, z0 = a.z * minz, z1 = a.z * maxz;
                double blo = Math.Min(x0, x1) + Math.Min(y0, y1) + Math.Min(z0, z1);
                double bhi = Math.Max(x0, x1) + Math.Max(y0, y1) + Math.Max(z0, z1);
                double m = Gamma * (Math.Max(Math.Abs(x0), Math.Abs(x1)) + Math.Max(Math.Abs(y0), Math.Abs(y1)) + Math.Max(Math.Abs(z0), Math.Abs(z1)));
                if (blo - m > hi[k] || bhi + m < lo[k])
                    return true;
            }
            return false;
        }

        // the corners of u and the query triangle are strictly apart along one of the query's axes, with the rounding margins
        public bool ApartFromCorners(ref Triangle3d u)
        {
            for (int k = 0; k < 4; ++k) {
                Vector3d a = axis[k];
                double p0 = a.Dot(u.V0), p1 = a.Dot(u.V1), p2 = a.Dot(u.V2);
                double m = Gamma * Math.Max(absdot(a, u.V0), Math.Max(absdot(a, u.V1), absdot(a, u.V2)));
                if (Math.Min(p0, Math.Min(p1, p2)) - m > hi[k] || Math.Max(p0, Math.Max(p1, p2)) + m < lo[k])
                    return true;
            }
            return false;
        }

        // all three corners of u strictly on one side of the query triangle's plane (exact signs)
        public bool ExactlyApart(ref Triangle3d u)
        {
            int s0 = ExactPredicates.Orient3d(v0, v1, v2, u.V0);
            if (s0 == 0) return false;
            int s1 = ExactPredicates.Orient3d(v0, v1, v2, u.V1);
            if (s1 != s0) return false;
            return ExactPredicates.Orient3d(v0, v1, v2, u.V2) == s0;
        }
    }

    // a box tree over the triangles (median split along the longest axis of the centroids, leaves of up to 4)
    sealed class Tree
    {
        readonly double[] nodeBox;      // 6 per node
        readonly int[] left, right, start, count;
        readonly int[] order;
        readonly double[] triBox;       // 6 per triangle, in the order of `order`
        int nodes;

        public Tree(Triangle3d[] tri, List<AxisAlignedBox3d> boxes)
        {
            int n = tri.Length;
            order = Enumerable.Range(0, n).ToArray();
            var cen = new Vector3d[n];
            for (int i = 0; i < n; ++i)
                cen[i] = boxes[i].Center;
            int cap = Math.Max(2 * n, 2);
            nodeBox = new double[6 * cap]; left = new int[cap]; right = new int[cap]; start = new int[cap]; count = new int[cap];
            build(0, n, boxes, cen);
            triBox = new double[6 * n];
            for (int i = 0; i < n; ++i) {
                AxisAlignedBox3d bx = boxes[order[i]];
                triBox[6 * i] = bx.Min.x; triBox[6 * i + 1] = bx.Min.y; triBox[6 * i + 2] = bx.Min.z;
                triBox[6 * i + 3] = bx.Max.x; triBox[6 * i + 4] = bx.Max.y; triBox[6 * i + 5] = bx.Max.z;
            }
        }

        int build(int s, int e, List<AxisAlignedBox3d> boxes, Vector3d[] cen)
        {
            int node = nodes++;
            var bx = AxisAlignedBox3d.Empty; var cb = AxisAlignedBox3d.Empty;
            for (int i = s; i < e; ++i) { bx.Contain(boxes[order[i]]); cb.Contain(cen[order[i]]); }
            nodeBox[6 * node] = bx.Min.x; nodeBox[6 * node + 1] = bx.Min.y; nodeBox[6 * node + 2] = bx.Min.z;
            nodeBox[6 * node + 3] = bx.Max.x; nodeBox[6 * node + 4] = bx.Max.y; nodeBox[6 * node + 5] = bx.Max.z;
            start[node] = s; count[node] = e - s; left[node] = -1;
            if (e - s <= 4)
                return node;
            Vector3d d = cb.Diagonal;
            int ax = d.x >= d.y && d.x >= d.z ? 0 : d.y >= d.z ? 1 : 2;
            int mid = (s + e) / 2;
            Array.Sort(order, s, e - s, Comparer<int>.Create((p, q) => { int c = cen[p][ax].CompareTo(cen[q][ax]); return c != 0 ? c : p.CompareTo(q); }));
            left[node] = build(s, mid, boxes, cen);
            right[node] = build(mid, e, boxes, cen);
            return node;
        }

        // the triangles whose box overlaps the query box (closed, as TriangleBoxTree.Query) and, with q, whose box is not
        // apart from the query triangle along one of its axes
        public void Collect(AxisAlignedBox3d qb, Query q, List<int> result)
        {
            var stack = new int[128];
            int sp = 0;
            stack[sp++] = 0;
            while (sp > 0) {
                int node = stack[--sp];
                if (apart(nodeBox, 6 * node, qb, q))
                    continue;
                if (left[node] < 0) {
                    for (int i = start[node]; i < start[node] + count[node]; ++i)
                        if (apart(triBox, 6 * i, qb, q) == false)
                            result.Add(order[i]);
                    continue;
                }
                stack[sp++] = left[node];
                stack[sp++] = right[node];
            }
        }

        static bool apart(double[] bx, int o, AxisAlignedBox3d qb, Query q)
        {
            if (bx[o + 3] < qb.Min.x || bx[o] > qb.Max.x || bx[o + 4] < qb.Min.y || bx[o + 1] > qb.Max.y || bx[o + 5] < qb.Min.z || bx[o + 2] > qb.Max.z)
                return true;
            return q != null && q.ApartFromBox(bx[o], bx[o + 1], bx[o + 2], bx[o + 3], bx[o + 4], bx[o + 5]);
        }
    }
}
