using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index;
using NetTopologySuite.Index.Strtree;

namespace Meshing2Acceptance
{
    /// <summary>Certified broad phase for the oracle's unchanged proper-crossing predicate.
    /// Each node hull contains every descendant segment, since it contains their endpoints.
    /// A closed-half-plane separation excludes proper crossings, including boundary-only contacts.
    /// Point/triangle and duplicate-coordinate checks retain responsibility for such contacts.
    /// The cost per visited node is bounded: a node keeps a containing polygon of at most <see cref="HullLimit"/> vertices (the
    /// exact hull where it is that small, else the corners of its closed box), so the walk costs a constant per node that a box
    /// query visits (on a convex boundary the root's exact hull holds nearly every point, and testing all of it per node made the
    /// search O(E·h), review of #411).</summary>
    public sealed class StrtreeEdgeSearch
    {
        readonly double[] x, y;
        readonly Pt?[] exact;
        readonly long[] keys;
        /// <summary>The largest polygon a node tests; a node whose hull would be larger tests its four box corners instead.</summary>
        public const int HullLimit = 16;
        static int A(long key) => (int)(key >> 32);
        static int B(long key) => (int)(uint)key;
        public static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        public StrtreeEdgeSearch(Mesh2Output mesh, long[] edges)
        {
            // Self-checks, not verdicts (see StrtreePointSearch's constructor): Conformity calls this only on the fast path
            // (ExactVertices == null), after Run.Vertices() rejected non-finite coordinates and after EdgesAndNeighbours() built the
            // edge keys from the triangles' own indices; Execute returns after Orientation() when !r.Ok. A throw means that coupling broke.
            if (mesh.ExactVertices != null) throw new ArgumentException("Binary64 pilot only");
            x = mesh.VX; y = mesh.VY;
            if (x.Length != y.Length) throw new ArgumentException("Finite matching coordinates required");
            for (int i = 0; i < x.Length; ++i)
                if (double.IsNaN(x[i]) || double.IsInfinity(x[i]) || double.IsNaN(y[i]) || double.IsInfinity(y[i]))
                    throw new ArgumentException("Finite matching coordinates required");
            exact = new Pt?[x.Length];
            var copy = new long[edges.Length];
            Array.Copy(edges, copy, edges.Length);
            Array.Sort(copy);
            int uniq = 0;
            for (int i = 0; i < copy.Length; ++i) {
                if (i > 0 && copy[i] == copy[i - 1]) continue;
                int a = A(copy[i]), b = B(copy[i]);
                if (a < 0 || b < 0 || a >= x.Length || b >= x.Length) throw new ArgumentException("Invalid edge index");
                copy[uniq++] = copy[i];
            }
            if (uniq == copy.Length) keys = copy;
            else { keys = new long[uniq]; Array.Copy(copy, keys, uniq); }
        }
        Pt ExactPt(int i) => exact[i] ?? (exact[i] = Pt.FromDoubles(x[i], y[i])).Value;
        // Existing oracle OrientV filter, unchanged; no new error bound or epsilon.
        int Orient(int a, int b, int c)
        {
            double ax = x[a], ay = y[a];
            double l = (x[b] - ax) * (y[c] - ay), rr = (y[b] - ay) * (x[c] - ax);
            double det = l - rr, sum = Math.Abs(l) + Math.Abs(rr), bound = 1e-15 * sum;
            if (sum > 1e-200 && sum < 1e200 && Math.Abs(det) > bound) return det > 0 ? 1 : -1;
            return Exact.Orient(ExactPt(a), ExactPt(b), ExactPt(c));
        }
        bool Proper(int a, int b, int c, int d) => Orient(a, b, c) * Orient(a, b, d) < 0 && Orient(c, d, a) * Orient(c, d, b) < 0;
        Envelope Box(int index)
        {
            int a = A(keys[index]), b = B(keys[index]);
            return new Envelope(x[a], x[b], y[a], y[b]);
        }
        sealed class Node
        {
            public double MinX, MaxX, MinY, MaxY;
            public Node[] Children;
            public int Item, MaxItem;
            /// <summary>A convex polygon (CCW) of at most max(HullLimit, 4) vertices that contains every descendant endpoint, as point
            /// handles (see Tree): the exact endpoint hull where that is small, else a coarser one (see Tree.Wrap).</summary>
            public int[] Hull;
        }
        /// <summary>The certified hierarchy of one run. A point handle h is vertex h if h ≥ 0, else corner -1-h of a child's box, whose
        /// exact point is made only if the filter needs it. Built per run, so concurrent runs share nothing mutable.</summary>
        sealed class Tree
        {
            readonly StrtreeEdgeSearch s;
            readonly List<double> cx = new List<double>(), cy = new List<double>();
            readonly List<Pt?> cp = new List<Pt?>();
            readonly List<int> hullUnique = new List<int>(), hullLower = new List<int>(), hullUpper = new List<int>();
            readonly Comparison<int> hullCmp;
            public Tree(StrtreeEdgeSearch search)
            {
                s = search;
                hullCmp = (p, q) => {
                    double px = X(p), qx = X(q);
                    if (px != qx) return px.CompareTo(qx);
                    double py = Y(p), qy = Y(q);
                    return py != qy ? py.CompareTo(qy) : p.CompareTo(q);
                };
            }
            /// <summary>The nodes whose polygon is their box (their hull had more than HullLimit vertices).</summary>
            public long Coarse;
            double X(int h) => h >= 0 ? s.x[h] : cx[-1 - h];
            double Y(int h) => h >= 0 ? s.y[h] : cy[-1 - h];
            Pt P(int h)
            {
                if (h >= 0) return s.ExactPt(h);
                // (an NTS envelope's bounds are the input doubles themselves: the corner is exact)
                return cp[-1 - h] ?? (cp[-1 - h] = Pt.FromDoubles(cx[-1 - h], cy[-1 - h])).Value;
            }
            int Corner(double px, double py) { cx.Add(px); cy.Add(py); cp.Add(null); return -cx.Count; }
            /// <summary>The oracle's OrientV filter, unchanged, on point handles; no new error bound or epsilon.</summary>
            public int Orient(int u, int v, int c)
            {
                double ux = X(u), uy = Y(u), vx = X(v), vy = Y(v), qx = X(c), qy = Y(c);
                // three points on one vertical or horizontal line: exactly collinear (box corners often are; spares the exact fallback)
                if ((ux == vx && vx == qx) || (uy == vy && vy == qy)) return 0;
                double l = (vx - ux) * (qy - uy), rr = (vy - uy) * (qx - ux);
                double det = l - rr, sum = Math.Abs(l) + Math.Abs(rr), bound = 1e-15 * sum;
                if (sum > 1e-200 && sum < 1e200 && Math.Abs(det) > bound) return det > 0 ? 1 : -1;
                return Exact.Orient(P(u), P(v), P(c));
            }
            /// <summary>The convex hull (CCW, Andrew's monotone chain) of the given points, without repeated coordinates.</summary>
            int[] Hull(List<int> source)
            {
                source.Sort(hullCmp);
                hullUnique.Clear();
                for (int i = 0; i < source.Count; ++i) {
                    int p = source[i];
                    if (hullUnique.Count == 0 || X(p) != X(hullUnique[hullUnique.Count - 1]) || Y(p) != Y(hullUnique[hullUnique.Count - 1]))
                        hullUnique.Add(p);
                }
                if (hullUnique.Count <= 2) return hullUnique.ToArray();
                hullLower.Clear(); hullUpper.Clear();
                for (int i = 0; i < hullUnique.Count; ++i) {
                    int p = hullUnique[i];
                    while (hullLower.Count >= 2 && Orient(hullLower[hullLower.Count - 2], hullLower[hullLower.Count - 1], p) <= 0)
                        hullLower.RemoveAt(hullLower.Count - 1);
                    hullLower.Add(p);
                }
                for (int i = hullUnique.Count - 1; i >= 0; i--) {
                    int p = hullUnique[i];
                    while (hullUpper.Count >= 2 && Orient(hullUpper[hullUpper.Count - 2], hullUpper[hullUpper.Count - 1], p) <= 0)
                        hullUpper.RemoveAt(hullUpper.Count - 1);
                    hullUpper.Add(p);
                }
                int lc = hullLower.Count - 1, uc = hullUpper.Count - 1;
                var result = new int[lc + uc];
                for (int i = 0; i < lc; ++i) result[i] = hullLower[i];
                for (int i = 0; i < uc; ++i) result[lc + i] = hullUpper[i];
                return result;
            }
            /// <summary>Wraps an NTS node. Its polygon is the convex hull of its children's polygons if that has at most HullLimit
            /// vertices, else the four corners of its closed box (which contains that hull). By induction every polygon contains the node's
            /// endpoints and has at most max(HullLimit, 4) vertices: the test per visited node is O(HullLimit), and the build is linear
            /// in the tree size (an exact endpoint hull at every level was O(n log n) per level, and O(h) per visit).</summary>
            public Node Wrap(IBoundable<Envelope, int> source)
            {
                var e = source.Bounds;
                var node = new Node { MinX = e.MinX, MaxX = e.MaxX, MinY = e.MinY, MaxY = e.MaxY };
                if (source is AbstractNode<Envelope, int> branch) {
                    var kids = branch.ChildBoundables;
                    var children = new Node[kids.Count];
                    int totalPts = 0;
                    for (int c = 0; c < children.Length; c++) { children[c] = Wrap(kids[c]); totalPts += children[c].Hull.Length; }
                    var points = new List<int>(totalPts);
                    int maxItem = -1;
                    for (int c = 0; c < children.Length; c++) {
                        if (children[c].MaxItem > maxItem) maxItem = children[c].MaxItem;
                        var h = children[c].Hull;
                        for (int k = 0; k < h.Length; ++k) points.Add(h[k]);
                    }
                    node.Children = children;
                    node.MaxItem = maxItem;
                    node.Hull = Hull(points);
                } else {
                    node.Item = source.Item; node.MaxItem = node.Item;
                    int a = A(s.keys[node.Item]), b = B(s.keys[node.Item]);
                    node.Hull = s.x[a] == s.x[b] && s.y[a] == s.y[b] ? new[] { a } : new[] { a, b };
                }
                // the closed box contains the polygon (and so every descendant endpoint); its corners in CCW order. Only a box of
                // positive width and height: a flat box has repeated corners, and a zero-length edge, whose orientation 0 "separates"
                // everything, would prune unsoundly (a hull of more than HullLimit ≥ 3 vertices is never flat; the guard keeps it so
                // if the limit changes)
                if (node.Hull.Length > HullLimit && e.Width > 0 && e.Height > 0) {
                    Coarse++;
                    node.Hull = new[] { Corner(e.MinX, e.MinY), Corner(e.MaxX, e.MinY), Corner(e.MaxX, e.MaxY), Corner(e.MinX, e.MaxY) };
                }
                return node;
            }
            public bool Separated(int a, int b, Node node, ref long pruneTests, out bool line)
            {
                bool positive = false, negative = false;
                var hull = node.Hull;
                for (int i = 0; i < hull.Length; ++i) {
                    int p = hull[i];
                    pruneTests++;
                    int sign = p == a || p == b ? 0 : Orient(a, b, p);
                    positive |= sign > 0; negative |= sign < 0;
                    if (positive && negative) break;
                }
                // Every descendant endpoint is in the polygon, so on one closed side of the query line (a half-plane is convex).
                // A proper crossing needs strict opposite signs; zero-only contacts are safe here.
                line = !(positive && negative);
                if (line) return true;
                int len = hull.Length;
                for (int i = 0; i < len; i++) {
                    int u = hull[i], v = hull[i + 1 < len ? i + 1 : 0];
                    pruneTests += 2;
                    // Polygon edges are CCW; its entire content is in their closed left half-planes.
                    // The query segment in a closed right half-plane cannot cross a node segment properly.
                    if (Orient(u, v, a) <= 0 && Orient(u, v, b) <= 0) return true;
                }
                return false;
            }
        }
        sealed class Visitor : IItemVisitor<int>
        {
            public Action<int> Visit;
            public void VisitItem(int id) => Visit(id);
        }
        static int[] Ranks(double[] values)
        {
            var order = Enumerable.Range(0, values.Length).OrderBy(i => values[i]).ToArray();
            var ranks = new int[values.Length]; int rank = 0;
            for (int i = 0; i < order.Length; i++) {
                if (i > 0 && values[order[i]] != values[order[i - 1]]) rank++;
                ranks[order[i]] = rank;
            }
            return ranks;
        }
        public EdgeIndexResult Run(string mode, int queryCount = 0) => RunCore(mode, queryCount, true, out _);

        struct EdgeBvhNode
        {
            public double MinX, MaxX, MinY, MaxY;
            public int Left, Right, MaxItem; // Left < 0 means leaf at start = ~Left, count = Right
        }

        public IReadOnlyList<(long, long)> FindCrossings()
        {
            var hits = new List<(long, long)>();
            int n = keys.Length;
            if (n < 2) return hits;
            var ex0 = new double[n]; var ex1 = new double[n]; var ey0 = new double[n]; var ey1 = new double[n];
            var ea = new int[n]; var eb = new int[n];
            var order = new int[n];
            for (int i = 0; i < n; ++i) {
                int a = A(keys[i]), b = B(keys[i]);
                ea[i] = a; eb[i] = b; order[i] = i;
                double xa = x[a], xb = x[b], ya = y[a], yb = y[b];
                ex0[i] = xa < xb ? xa : xb; ex1[i] = xa < xb ? xb : xa;
                ey0[i] = ya < yb ? ya : yb; ey1[i] = ya < yb ? yb : ya;
            }
            var bvh = new EdgeBvhNode[Math.Max(1, (n / 4) * 2 + 4)];
            int nodeCount = 0;

            int Build(int start, int count)
            {
                double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
                double cx0 = double.PositiveInfinity, cx1 = double.NegativeInfinity, cy0 = double.PositiveInfinity, cy1 = double.NegativeInfinity;
                int maxItem = -1;
                for (int k = start; k < start + count; ++k) {
                    int idx = order[k];
                    if (idx > maxItem) maxItem = idx;
                    double ix0 = ex0[idx], ix1 = ex1[idx], iy0 = ey0[idx], iy1 = ey1[idx];
                    if (ix0 < x0) x0 = ix0; if (ix1 > x1) x1 = ix1;
                    if (iy0 < y0) y0 = iy0; if (iy1 > y1) y1 = iy1;
                    double cx = ix0 + ix1, cy = iy0 + iy1;
                    if (cx < cx0) cx0 = cx; if (cx > cx1) cx1 = cx;
                    if (cy < cy0) cy0 = cy; if (cy > cy1) cy1 = cy;
                }
                int ni = nodeCount++;
                if (ni >= bvh.Length) Array.Resize(ref bvh, bvh.Length * 2);
                bvh[ni].MinX = x0; bvh[ni].MaxX = x1; bvh[ni].MinY = y0; bvh[ni].MaxY = y1; bvh[ni].MaxItem = maxItem;
                if (count <= 8) {
                    bvh[ni].Left = ~start; bvh[ni].Right = count;
                    return ni;
                }
                bool splitX = (cx1 - cx0) >= (cy1 - cy0);
                double mid = splitX ? (cx0 + cx1) * 0.5 : (cy0 + cy1) * 0.5;
                int lo = start, hi = start + count - 1;
                while (lo <= hi) {
                    int idx = order[lo];
                    double v = splitX ? (ex0[idx] + ex1[idx]) : (ey0[idx] + ey1[idx]);
                    if (v < mid) ++lo;
                    else { int tmp = order[lo]; order[lo] = order[hi]; order[hi] = tmp; --hi; }
                }
                int leftCount = lo - start;
                if (leftCount == 0 || leftCount == count) leftCount = count >> 1;
                int lc = Build(start, leftCount);
                int rc = Build(start + leftCount, count - leftCount);
                bvh[ni].Left = lc; bvh[ni].Right = rc;
                return ni;
            }

            int root = Build(0, n);
            var stack = new int[128];
            for (int i = 0; i < n; ++i) {
                int a = ea[i], b = eb[i];
                double qx0 = ex0[i], qx1 = ex1[i], qy0 = ey0[i], qy1 = ey1[i];
                double ax = x[a], ay = y[a], dx = x[b] - ax, dy = y[b] - ay;
                // Extreme corners of any box for orient(a, b, p) = dx*(py - ay) - dy*(px - ax):
                // max at (dy <= 0 ? MaxX : MinX, dx >= 0 ? MaxY : MinY); min at opposite corner.
                bool maxR = dy <= 0, maxT = dx >= 0;
                int sp = 0;
                stack[sp++] = root;
                while (sp > 0) {
                    int ni = stack[--sp];
                    ref var node = ref bvh[ni];
                    if (node.MaxItem <= i || node.MaxX < qx0 || node.MinX > qx1 || node.MaxY < qy0 || node.MinY > qy1) continue;
                    if (node.Left < 0) {
                        int s = ~node.Left, end = s + node.Right;
                        for (int k = s; k < end; ++k) {
                            int j = order[k];
                            if (j <= i) continue;
                            if (ex1[j] < qx0 || ex0[j] > qx1 || ey1[j] < qy0 || ey0[j] > qy1) continue;
                            int c = ea[j], d = eb[j];
                            if (a == c || a == d || b == c || b == d) continue;
                            if (Proper(a, b, c, d)) hits.Add((keys[i], keys[j]));
                        }
                        continue;
                    }
                    // Fast line separation of the node's box by query edge a-b (sound: if max < 0 or min > 0, all points lie strictly on one side).
                    double px = maxR ? node.MaxX : node.MinX, py = maxT ? node.MaxY : node.MinY;
                    double l = dx * (py - ay), rr = dy * (px - ax), det = l - rr, sum = Math.Abs(l) + Math.Abs(rr);
                    if (sum > 1e-200 && sum < 1e200 && det < -1e-15 * sum) continue;
                    px = maxR ? node.MinX : node.MaxX; py = maxT ? node.MinY : node.MaxY;
                    l = dx * (py - ay); rr = dy * (px - ax); det = l - rr; sum = Math.Abs(l) + Math.Abs(rr);
                    if (sum > 1e-200 && sum < 1e200 && det > 1e-15 * sum) continue;
                    if (sp + 2 > stack.Length) Array.Resize(ref stack, stack.Length * 2);
                    stack[sp++] = node.Right;
                    stack[sp++] = node.Left;
                }
            }
            hits.Sort();
            return hits;
        }
        EdgeIndexResult RunCore(string mode, int queryCount, bool report, out List<(long, long)> found)
        {
            if (mode != "boxes" && mode != "nts" && mode != "hulls") throw new ArgumentException("Unknown edge mode");
            int count = queryCount <= 0 ? keys.Length : Math.Min(queryCount, keys.Length);
            var queries = count == keys.Length ? null : Enumerable.Range(0, count).Select(i => (int)((long)i * keys.Length / count)).ToArray();
            var hits = new List<(long, long)>(); long candidates = 0, nodes = 0, linePruned = 0, hullPruned = 0, pruneTests = 0, coarse = 0;
            void Check(int i, int j) {
                if (j <= i) return;
                int a = A(keys[i]), b = B(keys[i]), c = A(keys[j]), d = B(keys[j]);
                if (a == c || a == d || b == c || b == d) return;
                candidates++;
                if (Proper(a, b, c, d)) hits.Add((keys[i], keys[j]));
            }
            Process process = report ? Process.GetCurrentProcess() : null;
            TimeSpan cpu = report ? process.TotalProcessorTime : default;
            Stopwatch watch = report ? Stopwatch.StartNew() : null;
            try {
                if (mode == "boxes") {
                    var rx = Ranks(x); var ry = Ranks(y);
                    var boxes = keys.Select(k => new[] { Math.Min(rx[A(k)], rx[B(k)]), Math.Max(rx[A(k)], rx[B(k)]), Math.Min(ry[A(k)], ry[B(k)]), Math.Max(ry[A(k)], ry[B(k)]) }).ToArray();
                    if (count == keys.Length) BoxPairs.Overlaps(boxes, boxes, true, (i, j) => Check(Math.Min(i, j), Math.Max(i, j)));
                    else BoxPairs.Overlaps(queries.Select(i => boxes[i]).ToArray(), boxes, false, (qi, j) => Check(queries[qi], j));
                } else if (count > 0) {
                    var tree = new STRtree<int>();
                    for (int i = 0; i < keys.Length; i++) tree.Insert(Box(i), i);
                    tree.Build();
                    if (mode == "nts") {
                        var visitor = new Visitor();
                        for (int qi = 0; qi < count; ++qi) {
                            int i = queries != null ? queries[qi] : qi;
                            visitor.Visit = j => Check(i, j);
                            tree.Query(Box(i), visitor);
                        }
                    } else {
                        var hierarchy = new Tree(this); var root = hierarchy.Wrap(tree.Root); coarse = hierarchy.Coarse;
                        void Walk(Node node, int i, int a, int b, double ex0, double ex1, double ey0, double ey1) {
                            nodes++;
                            if (node.MaxItem <= i || node.MaxX < ex0 || node.MinX > ex1 || node.MaxY < ey0 || node.MinY > ey1) return;
                            if (node.Children == null) { Check(i, node.Item); return; }
                            if (hierarchy.Separated(a, b, node, ref pruneTests, out bool line)) {
                                if (line) linePruned++; else hullPruned++;
                                return;
                            }
                            var kids = node.Children;
                            for (int c = 0; c < kids.Length; ++c) Walk(kids[c], i, a, b, ex0, ex1, ey0, ey1);
                        }
                        for (int qi = 0; qi < count; ++qi) {
                            int i = queries != null ? queries[qi] : qi;
                            int a = A(keys[i]), b = B(keys[i]);
                            double xa = x[a], xb = x[b], ya = y[a], yb = y[b];
                            Walk(root, i, a, b, xa < xb ? xa : xb, xa < xb ? xb : xa, ya < yb ? ya : yb, ya < yb ? yb : ya);
                        }
                    }
                }
                hits.Sort(); found = hits;
                if (!report) return null;
                watch.Stop(); process.Refresh();
                return new EdgeIndexResult(mode, count, watch.Elapsed.TotalSeconds, (process.TotalProcessorTime - cpu).TotalSeconds, candidates, nodes, linePruned, hullPruned, hits.Count, Fingerprint(hits)) { PruneTests = pruneTests, CoarseNodes = coarse };
            } finally {
                process?.Dispose();
            }
        }
        static string Fingerprint(List<(long, long)> hits)
        {
            string text = string.Join("\n", hits.Select(p => p.Item1 + " " + p.Item2));
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        public EdgeIndexResult Exhaustive()
        {
            var hits = new List<(long, long)>();
            for (int i = 0; i < keys.Length; i++) for (int j = i + 1; j < keys.Length; j++) {
                int a = A(keys[i]), b = B(keys[i]), c = A(keys[j]), d = B(keys[j]);
                if (a == c || a == d || b == c || b == d) continue;
                int O(int u, int v, int p) => Exact.Orient(ExactPt(u), ExactPt(v), ExactPt(p));
                if (O(a, b, c) * O(a, b, d) < 0 && O(c, d, a) * O(c, d, b) < 0) hits.Add((keys[i], keys[j]));
            }
            hits.Sort(); return new EdgeIndexResult("exhaustive", keys.Length, 0, 0, 0, 0, 0, 0, hits.Count, Fingerprint(hits));
        }
    }
    public sealed class EdgeIndexResult
    {
        public string Mode { get; }
        public int Queries { get; }
        public double Seconds { get; }
        public double CpuSeconds { get; }
        public long Candidates { get; }
        public long Nodes { get; }
        public long LinePruned { get; }
        public long HullPruned { get; }
        public int Hits { get; }
        public string Fingerprint { get; }
        /// <summary>Orientation tests made by the pruning certificates (hull vertices, box corners, hull edges): with Nodes the walk's work.</summary>
        public long PruneTests { get; set; }
        /// <summary>Inner nodes that test their box corners because their hull exceeded the hull limit.</summary>
        public long CoarseNodes { get; set; }
        public EdgeIndexResult(string mode,int queries,double seconds,double cpuSeconds,long candidates,long nodes,long linePruned,long hullPruned,int hits,string fingerprint)
        { Mode=mode;Queries=queries;Seconds=seconds;CpuSeconds=cpuSeconds;Candidates=candidates;Nodes=nodes;LinePruned=linePruned;HullPruned=hullPruned;Hits=hits;Fingerprint=fingerprint; }
    }
}
