using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Meshing2Acceptance;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index;
using NetTopologySuite.Index.Strtree;

namespace Meshing2Acceptance
{
public sealed class StrtreePointSearch
{
    readonly Mesh2Output mesh;
    readonly Pt?[] exact;
    readonly int[] used;
    public StrtreePointSearch(Mesh2Output mesh)
    {
        // These throws are self-checks, not verdicts: inside the oracle they cannot fire. Conformity calls this only on the fast path
        // (ExactVertices == null, Oracle.UseConformityIndex); Run.Vertices() has already rejected non-finite coordinates (Execute
        // returns before here), and Execute returns after Orientation() when !r.Ok, so every triangle is positive by then. A throw
        // here therefore means that coupling broke, and it must surface rather than become a silent verdict.
        if (mesh.ExactVertices != null) throw new ArgumentException("Pilot accepts binary64 output only");
        var vx = mesh.VX; var vy = mesh.VY;
        for (int i = 0; i < vx.Length; ++i) if (double.IsNaN(vx[i]) || double.IsInfinity(vx[i])) throw new ArgumentException("Non-finite vertex");
        for (int i = 0; i < vy.Length; ++i) if (double.IsNaN(vy[i]) || double.IsInfinity(vy[i])) throw new ArgumentException("Non-finite vertex");
        this.mesh = mesh;
        exact = new Pt?[vx.Length];
        var seen = new bool[vx.Length];
        int usedCount = 0;
        var tris = mesh.Triangles;
        for (int i = 0; i < tris.Length; ++i) {
            var t = tris[i];
            int a = t[0], b = t[1], c = t[2];
            if (!seen[a]) { seen[a] = true; ++usedCount; }
            if (!seen[b]) { seen[b] = true; ++usedCount; }
            if (!seen[c]) { seen[c] = true; ++usedCount; }
            if (Orient(a, b, c) <= 0) throw new ArgumentException("Pilot requires positive triangles, as Conformity does after Orientation");
        }
        used = new int[usedCount];
        int u = 0;
        for (int i = 0; i < seen.Length; ++i) if (seen[i]) used[u++] = i;
    }

    Pt ExactPt(int i) => exact[i] ?? (exact[i] = Pt.FromDoubles(mesh.VX[i], mesh.VY[i])).Value;

    // The existing oracle's OrientV filter, unchanged; uncertain cases use its exact predicate.
    int Orient(int a, int b, double cx, double cy, Func<Pt> getC)
    {
        double ax = mesh.VX[a], ay = mesh.VY[a];
        double l = (mesh.VX[b] - ax) * (cy - ay), rr = (mesh.VY[b] - ay) * (cx - ax);
        double det = l - rr, sum = Math.Abs(l) + Math.Abs(rr), bound = 1e-15 * sum;
        if (sum > 1e-200 && sum < 1e200 && Math.Abs(det) > bound) return det > 0 ? 1 : -1;
        return Exact.Orient(ExactPt(a), ExactPt(b), getC());
    }
    int Orient(int a, int b, int c)
    {
        double ax = mesh.VX[a], ay = mesh.VY[a];
        double l = (mesh.VX[b] - ax) * (mesh.VY[c] - ay), rr = (mesh.VY[b] - ay) * (mesh.VX[c] - ax);
        double det = l - rr, sum = Math.Abs(l) + Math.Abs(rr), bound = 1e-15 * sum;
        if (sum > 1e-200 && sum < 1e200 && Math.Abs(det) > bound) return det > 0 ? 1 : -1;
        return Exact.Orient(ExactPt(a), ExactPt(b), ExactPt(c));
    }
    Envelope TriangleBox(int ti)
    {
        var t = mesh.Triangles[ti];
        double x0 = mesh.VX[t[0]], x1 = mesh.VX[t[1]], x2 = mesh.VX[t[2]];
        double y0 = mesh.VY[t[0]], y1 = mesh.VY[t[1]], y2 = mesh.VY[t[2]];
        return new Envelope(Math.Min(x0, Math.Min(x1, x2)), Math.Max(x0, Math.Max(x1, x2)), Math.Min(y0, Math.Min(y1, y2)), Math.Max(y0, Math.Max(y1, y2)));
    }

    sealed class Node
    {
        public double MinX, MaxX, MinY, MaxY;
        public Node[] Children;
        public int Item;
        public Pt?[] Corners;
        public Pt Corner(int corner, double x, double y)
        {
            if (Corners == null) Corners = new Pt?[4];
            return Corners[corner] ?? (Corners[corner] = Pt.FromDoubles(x, y)).Value;
        }
    }
    static Node Wrap(NetTopologySuite.Geometries.IBoundable<Envelope, int> source)
    {
        var e = source.Bounds;
        var node = new Node { MinX = e.MinX, MaxX = e.MaxX, MinY = e.MinY, MaxY = e.MaxY };
        if (source is AbstractNode<Envelope, int> branch) {
            var kids = branch.ChildBoundables;
            var arr = new Node[kids.Count];
            for (int i = 0; i < arr.Length; ++i) arr[i] = Wrap(kids[i]);
            node.Children = arr;
        } else node.Item = source.Item;
        return node;
    }
    bool Outside(int[] t, Node node)
    {
        var vx = mesh.VX; var vy = mesh.VY;
        for (int edge = 0; edge < 3; edge++) {
            int a = t[edge], b = t[(edge + 1) % 3];
            // orient(a,b,p) is affine in p: maximize it over the closed box.
            // Its x coefficient is -(by-ay), its y coefficient is bx-ax.
            bool right = vy[b] <= vy[a], top = vx[b] >= vx[a];
            double x = right ? node.MaxX : node.MinX, y = top ? node.MaxY : node.MinY;
            double ax = vx[a], ay = vy[a];
            double l = (vx[b] - ax) * (y - ay), rr = (vy[b] - ay) * (x - ax);
            double det = l - rr, sum = Math.Abs(l) + Math.Abs(rr), bound = 1e-15 * sum;
            int sign;
            if (sum > 1e-200 && sum < 1e200 && Math.Abs(det) > bound) sign = det > 0 ? 1 : -1;
            else {
                int corner = (right ? 1 : 0) + (top ? 2 : 0);
                sign = Exact.Orient(ExactPt(a), ExactPt(b), node.Corner(corner, x, y));
            }
            // Strictly outside only: points on edges must still be reported.
            if (sign < 0) return true;
        }
        return false;
    }
    sealed class Visitor : IItemVisitor<int>
    {
        public Action<int> Visit;
        public void VisitItem(int item) => Visit(item);
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
    PointIndexResult RunCore(string mode, int queryCount, bool report, out List<(int, int)> found)
    {
        if (mode != "boxes" && mode != "nts" && mode != "pruned") throw new ArgumentException("Unknown mode");
        int count = queryCount <= 0 ? mesh.Triangles.Length : Math.Min(queryCount, mesh.Triangles.Length);
        var queries = count == mesh.Triangles.Length ? null : Enumerable.Range(0, count).Select(i => (int)((long)i * mesh.Triangles.Length / count)).ToArray();
        var hits = new List<(int, int)>(); long candidates = 0, nodes = 0, pruned = 0;
        var tris = mesh.Triangles; var vx = mesh.VX; var vy = mesh.VY;
        void Check(int ti, int id) {
            var t = tris[ti];
            if (id == t[0] || id == t[1] || id == t[2]) return;
            candidates++;
            if (Orient(t[0], t[1], id) >= 0 && Orient(t[1], t[2], id) >= 0 && Orient(t[2], t[0], id) >= 0) hits.Add((ti, id));
        }
        Process process = report ? Process.GetCurrentProcess() : null;
        TimeSpan cpuStart = report ? process.TotalProcessorTime : default;
        Stopwatch watch = report ? Stopwatch.StartNew() : null;
        try {
            if (mode == "boxes") {
                var rx = Ranks(vx); var ry = Ranks(vy);
                var qIdx = queries ?? Enumerable.Range(0, count).ToArray();
                var tb = qIdx.Select(i => tris[i]).Select(t => new[] { t.Min(i => rx[i]), t.Max(i => rx[i]), t.Min(i => ry[i]), t.Max(i => ry[i]) }).ToArray();
                var vb = used.Select(i => new[] { rx[i], rx[i], ry[i], ry[i] }).ToArray();
                BoxPairs.Overlaps(tb, vb, false, (ti, vi) => Check(qIdx[ti], used[vi]));
            } else if (count > 0 && used.Length > 0) {
                var tree = new STRtree<int>();
                for (int u = 0; u < used.Length; ++u) { int id = used[u]; tree.Insert(new Envelope(vx[id], vx[id], vy[id], vy[id]), id); }
                tree.Build();
                if (mode == "nts") {
                    var visitor = new Visitor();
                    for (int qi = 0; qi < count; ++qi) {
                        int ti = queries != null ? queries[qi] : qi;
                        visitor.Visit = id => Check(ti, id);
                        tree.Query(TriangleBox(ti), visitor);
                    }
                } else {
                    var root = Wrap(tree.Root);
                    void Walk(Node node, int ti, int[] t, double tx0, double tx1, double ty0, double ty1) {
                        nodes++;
                        if (node.MaxX < tx0 || node.MinX > tx1 || node.MaxY < ty0 || node.MinY > ty1) return;
                        if (node.Children == null) { Check(ti, node.Item); return; }
                        if (Outside(t, node)) { pruned++; return; }
                        var kids = node.Children;
                        for (int c = 0; c < kids.Length; ++c) Walk(kids[c], ti, t, tx0, tx1, ty0, ty1);
                    }
                    for (int qi = 0; qi < count; ++qi) {
                        int ti = queries != null ? queries[qi] : qi;
                        var t = tris[ti];
                        double x0 = vx[t[0]], x1 = vx[t[1]], x2 = vx[t[2]];
                        double y0 = vy[t[0]], y1 = vy[t[1]], y2 = vy[t[2]];
                        Walk(root, ti, t, Math.Min(x0, Math.Min(x1, x2)), Math.Max(x0, Math.Max(x1, x2)), Math.Min(y0, Math.Min(y1, y2)), Math.Max(y0, Math.Max(y1, y2)));
                    }
                }
            }
            hits.Sort();
            found = hits;
            if (!report) return null;
            watch.Stop();
            process.Refresh();
            double cpu = (process.TotalProcessorTime - cpuStart).TotalSeconds;
            string canonical = string.Join("\n", hits.Select(p => p.Item1 + " " + p.Item2));
            string hash = Fingerprint(canonical);
            return new PointIndexResult(mode, count, watch.Elapsed.TotalSeconds, cpu, candidates, nodes, pruned, hits.Count, hash);
        } finally {
            process?.Dispose();
        }
    }
    public PointIndexResult Run(string mode, int queryCount = 0) => RunCore(mode, queryCount, true, out _);

    struct PtBvhNode
    {
        public double MinX, MaxX, MinY, MaxY;
        public int Left, Right; // Left < 0 means leaf at start = ~Left, count = Right
    }

    public IReadOnlyList<(int, int)> FindTouches()
    {
        var hits = new List<(int, int)>();
        var tris = mesh.Triangles;
        if (tris.Length == 0 || used.Length == 0) return hits;
        var vx = mesh.VX; var vy = mesh.VY;
        int n = used.Length;
        var pts = new int[n];
        Array.Copy(used, pts, n);
        var bvh = new PtBvhNode[Math.Max(1, (n / 4) * 2 + 4)];
        int nodeCount = 0;

        int Build(int start, int count)
        {
            double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
            for (int i = start; i < start + count; ++i) {
                int id = pts[i];
                double px = vx[id], py = vy[id];
                if (px < x0) x0 = px; if (px > x1) x1 = px;
                if (py < y0) y0 = py; if (py > y1) y1 = py;
            }
            int ni = nodeCount++;
            if (ni >= bvh.Length) Array.Resize(ref bvh, bvh.Length * 2);
            bvh[ni].MinX = x0; bvh[ni].MaxX = x1; bvh[ni].MinY = y0; bvh[ni].MaxY = y1;
            if (count <= 8) {
                bvh[ni].Left = ~start; bvh[ni].Right = count;
                return ni;
            }
            bool splitX = (x1 - x0) >= (y1 - y0);
            double mid = splitX ? (x0 + x1) * 0.5 : (y0 + y1) * 0.5;
            int lo = start, hi = start + count - 1;
            while (lo <= hi) {
                double v = splitX ? vx[pts[lo]] : vy[pts[lo]];
                if (v < mid) ++lo;
                else { int tmp = pts[lo]; pts[lo] = pts[hi]; pts[hi] = tmp; --hi; }
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
        for (int ti = 0; ti < tris.Length; ++ti) {
            var t = tris[ti];
            int a = t[0], b = t[1], c = t[2];
            double ax = vx[a], ay = vy[a], bx = vx[b], by = vy[b], cx = vx[c], cy = vy[c];
            double tx0 = ax < bx ? (ax < cx ? ax : cx) : (bx < cx ? bx : cx);
            double tx1 = ax > bx ? (ax > cx ? ax : cx) : (bx > cx ? bx : cx);
            double ty0 = ay < by ? (ay < cy ? ay : cy) : (by < cy ? by : cy);
            double ty1 = ay > by ? (ay > cy ? ay : cy) : (by > cy ? by : cy);
            // Precompute edge vectors & corner-selection signs once per triangle!
            double e0x = bx - ax, e0y = by - ay; bool r0 = e0y <= 0, t0 = e0x >= 0;
            double e1x = cx - bx, e1y = cy - by; bool r1 = e1y <= 0, t1 = e1x >= 0;
            double e2x = ax - cx, e2y = ay - cy; bool r2 = e2y <= 0, t2 = e2x >= 0;
            int sp = 0;
            stack[sp++] = root;
            while (sp > 0) {
                int ni = stack[--sp];
                ref var node = ref bvh[ni];
                if (node.MaxX < tx0 || node.MinX > tx1 || node.MaxY < ty0 || node.MinY > ty1) continue;
                if (node.Left < 0) {
                    int s = ~node.Left, end = s + node.Right;
                    for (int k = s; k < end; ++k) {
                        int id = pts[k];
                        if (id == a || id == b || id == c) continue;
                        double px = vx[id], py = vy[id];
                        if (px < tx0 || px > tx1 || py < ty0 || py > ty1) continue;
                        if (Orient(a, b, id) >= 0 && Orient(b, c, id) >= 0 && Orient(c, a, id) >= 0) hits.Add((ti, id));
                    }
                    continue;
                }
                // Half-plane separation test against the 3 triangle edges (exact same certificate as Outside).
                double qx = r0 ? node.MaxX : node.MinX, qy = t0 ? node.MaxY : node.MinY;
                double l = e0x * (qy - ay), rr = e0y * (qx - ax), det = l - rr, sum = Math.Abs(l) + Math.Abs(rr);
                if (sum > 1e-200 && sum < 1e200 && det < -1e-15 * sum) continue;
                qx = r1 ? node.MaxX : node.MinX; qy = t1 ? node.MaxY : node.MinY;
                l = e1x * (qy - by); rr = e1y * (qx - bx); det = l - rr; sum = Math.Abs(l) + Math.Abs(rr);
                if (sum > 1e-200 && sum < 1e200 && det < -1e-15 * sum) continue;
                qx = r2 ? node.MaxX : node.MinX; qy = t2 ? node.MaxY : node.MinY;
                l = e2x * (qy - cy); rr = e2y * (qx - cx); det = l - rr; sum = Math.Abs(l) + Math.Abs(rr);
                if (sum > 1e-200 && sum < 1e200 && det < -1e-15 * sum) continue;
                if (sp + 2 > stack.Length) Array.Resize(ref stack, stack.Length * 2);
                stack[sp++] = node.Right;
                stack[sp++] = node.Left;
            }
        }
        hits.Sort();
        return hits;
    }
    static string Fingerprint(string canonical)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
    }
    public PointIndexResult Exhaustive()
    {
        var hits = new List<(int, int)>();
        for (int ti = 0; ti < mesh.Triangles.Length; ti++) foreach (int id in used) {
            var t = mesh.Triangles[ti];
            if (t.Contains(id)) continue;
            // Independent exact leaf test: no floating filter or spatial index.
            if (Exact.Orient(ExactPt(t[0]), ExactPt(t[1]), ExactPt(id)) >= 0 && Exact.Orient(ExactPt(t[1]), ExactPt(t[2]), ExactPt(id)) >= 0 && Exact.Orient(ExactPt(t[2]), ExactPt(t[0]), ExactPt(id)) >= 0) hits.Add((ti, id));
        }
        hits.Sort();
        string canonical = string.Join("\n", hits.Select(p => p.Item1 + " " + p.Item2));
        return new PointIndexResult("exhaustive", mesh.Triangles.Length, 0, 0, 0, 0, 0, hits.Count, Fingerprint(canonical));
    }
}
public sealed class PointIndexResult
{
    public string Mode { get; }
    public int Queries { get; }
    public double Seconds { get; }
    public double CpuSeconds { get; }
    public long Candidates { get; }
    public long Nodes { get; }
    public long Pruned { get; }
    public int Hits { get; }
    public string Fingerprint { get; }
    public PointIndexResult(string mode,int queries,double seconds,double cpuSeconds,long candidates,long nodes,long pruned,int hits,string fingerprint)
    { Mode=mode; Queries=queries; Seconds=seconds; CpuSeconds=cpuSeconds; Candidates=candidates; Nodes=nodes; Pruned=pruned; Hits=hits; Fingerprint=fingerprint; }
}
}
