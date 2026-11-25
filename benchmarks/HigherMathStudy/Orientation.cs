// Idea 3: orientation of triangle soups as Z2 synchronization (a signed graph over the triangles, its leading
// eigenvector), against the library's orientation (MeshRepairOrientation's patch spanning forest and ray voting,
// MeshAutoRepair end to end, WindingContouring.OrientedCopy). Procedural soups with known flips. Research probe.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class Orientation
{
    class Soup
    {
        public List<Vector3d[]> T = new List<Vector3d[]>();
        public List<bool> Flipped = new List<bool>();        // the truth: reversed against the outward orientation
        public double Area(int i) => MathUtil.Area(T[i][0], T[i][1], T[i][2]);
    }

    // solid unit cells, every boundary cell face as two outward triangles (as AlphaWrapFeaturePassTests.PolyCube)
    static List<Vector3d[]> PolyCube(int nx, int ny, int nz, Func<int, int, int, bool> solid, Vector3d o)
    {
        Func<int, int, int, bool> s = (x, y, z) => x >= 0 && y >= 0 && z >= 0 && x < nx && y < ny && z < nz && solid(x, y, z);
        var tris = new List<Vector3d[]>();
        int[][] dirs = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        for (int x = 0; x < nx; ++x) for (int y = 0; y < ny; ++y) for (int z = 0; z < nz; ++z) {
            if (!s(x, y, z)) continue;
            foreach (int[] d in dirs) {
                if (s(x + d[0], y + d[1], z + d[2])) continue;
                int ax = d[0] != 0 ? 0 : (d[1] != 0 ? 1 : 2), u = (ax + 1) % 3, v = (ax + 2) % 3;
                int[] c = { x, y, z };
                if (d[ax] > 0) c[ax] += 1;
                var q = new Vector3d[4];
                for (int k = 0; k < 4; ++k) {
                    int[] p = { c[0], c[1], c[2] };
                    if (k == 1 || k == 2) p[u] += 1;
                    if (k == 2 || k == 3) p[v] += 1;
                    q[k] = new Vector3d(p[0], p[1], p[2]) + o;
                }
                if (d[ax] > 0) { tris.Add(new[] { q[0], q[1], q[2] }); tris.Add(new[] { q[0], q[2], q[3] }); }
                else { tris.Add(new[] { q[0], q[2], q[1] }); tris.Add(new[] { q[0], q[3], q[2] }); }
            }
        }
        return tris;
    }

    static List<Vector3d[]> SphereTris(int n)
    {
        var m = new Sphere3Generator_NormalizedCube() { Radius = 2, EdgeVertices = n }.Generate().MakeDMesh();
        return m.TriangleIndices().Select(t => { var q = m.GetTriangle(t); return new[] { m.GetVertex(q.a), m.GetVertex(q.b), m.GetVertex(q.c) }; }).ToList();
    }

    // unwelded soup: every triangle its own corners; flip(i) reverses it; jitter moves every corner copy by up to delta
    static Soup MakeSoup(List<Vector3d[]> tris, Func<int, Random, bool> flip, double jitter, int seed, double drop = 0)
    {
        var rnd = new Random(seed);
        var s = new Soup();
        for (int i = 0; i < tris.Count; ++i) {
            if (drop > 0 && rnd.NextDouble() < drop) continue;
            var t = tris[i].Select(p => jitter == 0 ? p : p + jitter * new Vector3d(2 * rnd.NextDouble() - 1, 2 * rnd.NextDouble() - 1, 2 * rnd.NextDouble() - 1)).ToArray();
            bool f = flip(s.T.Count, rnd);
            if (f) t = new[] { t[0], t[2], t[1] };
            s.T.Add(t); s.Flipped.Add(f);
        }
        return s;
    }

    // ---- the signed triangle graph at weld tolerance eps ----
    class Graph
    {
        public int N; public List<(int j, int s)>[] Adj; public int[] Cluster; public int[][] Corner; public int Links, Frustrated;
    }

    static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }

    static Graph Build(Soup s, double eps, bool radial = false)
    {
        int n = s.T.Count, nv = 3 * n;
        var parent = Enumerable.Range(0, nv).ToArray();
        var grid = new Dictionary<(long, long, long), List<int>>();
        Func<Vector3d, (long, long, long)> cell = p => ((long)Math.Floor(p.x / eps), (long)Math.Floor(p.y / eps), (long)Math.Floor(p.z / eps));
        for (int v = 0; v < nv; ++v) {
            Vector3d p = s.T[v / 3][v % 3];
            var c = cell(p);
            for (long dx = -1; dx <= 1; ++dx) for (long dy = -1; dy <= 1; ++dy) for (long dz = -1; dz <= 1; ++dz)
                if (grid.TryGetValue((c.Item1 + dx, c.Item2 + dy, c.Item3 + dz), out var list))
                    foreach (int w in list)
                        if (s.T[w / 3][w % 3].Distance(p) <= eps) parent[Find(parent, v)] = Find(parent, w);
            if (!grid.TryGetValue(c, out var l)) grid[c] = l = new List<int>();
            l.Add(v);
        }
        var g = new Graph { N = n, Adj = new List<(int, int)>[n], Cluster = new int[nv], Corner = new int[n][] };
        for (int v = 0; v < nv; ++v) g.Cluster[v] = Find(parent, v);
        for (int i = 0; i < n; ++i) { g.Adj[i] = new List<(int, int)>(); g.Corner[i] = new[] { g.Cluster[3 * i], g.Cluster[3 * i + 1], g.Cluster[3 * i + 2] }; }
        var edges = new Dictionary<(int, int), List<(int t, bool fwd, int k)>>();
        for (int i = 0; i < n; ++i)
            for (int k = 0; k < 3; ++k) {
                int a = g.Corner[i][k], b = g.Corner[i][(k + 1) % 3];
                if (a == b) continue;
                var key = (Math.Min(a, b), Math.Max(a, b));
                if (!edges.TryGetValue(key, out var l)) edges[key] = l = new List<(int, bool, int)>();
                l.Add((i, a < b, k));
            }
        foreach (var l0 in edges.Values) {
            var pairs = new List<(int, int)>();
            var l = l0;
            if (radial && l.Count > 2) {
                // radial order of the triangles around the edge (the angle of their third corner about the edge axis);
                // link only neighbours in that order: each pair bounds one wedge, solid or empty, and is consistent there
                var e0 = l[0];
                Vector3d pa = s.T[e0.t][e0.k], pb = s.T[e0.t][(e0.k + 1) % 3];
                if (!e0.fwd) { var tmp = pa; pa = pb; pb = tmp; }      // the axis from the smaller cluster id to the larger
                Vector3d ax = (pb - pa).Normalized, u = Vector3d.Zero;
                Vector3d c0 = s.T[e0.t][(e0.k + 2) % 3] - pa; u = (c0 - c0.Dot(ax) * ax).Normalized; Vector3d v = ax.Cross(u);
                l = l.OrderBy(e => { Vector3d c = s.T[e.t][(e.k + 2) % 3] - pa; return Math.Atan2(c.Dot(v), c.Dot(u)); }).ToList();
                for (int x = 0; x < l.Count; ++x) pairs.Add((x, (x + 1) % l.Count));
            } else {
                for (int x = 0; x < l.Count; ++x) for (int y = x + 1; y < l.Count; ++y) pairs.Add((x, y));
            }
            foreach (var (x, y) in pairs) {
                if (l[x].t == l[y].t) continue;
                int sg = l[x].fwd != l[y].fwd ? 1 : -1;      // opposite directions: consistent
                g.Adj[l[x].t].Add((l[y].t, sg)); g.Adj[l[y].t].Add((l[x].t, sg));
                g.Links++;
                // is the measured relation wrong against the truth? (true relation: equal flips <=> consistent)
                int truth = s.Flipped[l[x].t] == s.Flipped[l[y].t] ? 1 : -1;
                if (truth != sg) g.Frustrated++;
            }
        }
        return g;
    }

    static List<int[]> Components(Graph g)
    {
        var seen = new bool[g.N]; var comps = new List<int[]>();
        for (int r = 0; r < g.N; ++r) {
            if (seen[r]) continue;
            var c = new List<int> { r }; seen[r] = true;
            for (int h = 0; h < c.Count; ++h) foreach (var (j, _) in g.Adj[c[h]]) if (!seen[j]) { seen[j] = true; c.Add(j); }
            comps.Add(c.ToArray());
        }
        return comps;
    }

    // per component: flip all when its signed volume (about its centroid) is negative
    static void GlobalSign(Soup s, int[] comp, int[] x)
    {
        Vector3d c = Vector3d.Zero; foreach (int i in comp) c += (s.T[i][0] + s.T[i][1] + s.T[i][2]) / 3; c /= comp.Length;
        double vol = 0;
        foreach (int i in comp) {
            Vector3d a = s.T[i][0] - c, b = s.T[i][1] - c, d = s.T[i][2] - c;
            if (x[i] < 0) { var t = b; b = d; d = t; }
            vol += a.Dot(b.Cross(d));
        }
        if (vol < 0) foreach (int i in comp) x[i] = -x[i];
    }

    static int[] Bfs(Soup s, Graph g)
    {
        var x = new int[g.N];
        foreach (int[] comp in Components(g)) {
            x[comp[0]] = 1;
            var q = new Queue<int>(); q.Enqueue(comp[0]);
            while (q.Count > 0) { int i = q.Dequeue(); foreach (var (j, sg) in g.Adj[i]) if (x[j] == 0) { x[j] = sg * x[i]; q.Enqueue(j); } }
            GlobalSign(s, comp, x);
        }
        return x;
    }

    // Z2 synchronization, spectral relaxation (Singer 2011; Cucuringu 2015): the leading eigenvector of the
    // normalized signed adjacency D^-1/2 A D^-1/2 (shifted by I so the power iteration converges to the top of the
    // spectrum), per connected component; the signs of its entries are the orientations
    public static int Iterations = int.TryParse(Environment.GetEnvironmentVariable("G3_Z2_ITERATIONS"), out int it) ? it : 300;
    static int[] Spectral(Soup s, Graph g)
    {
        var x = new int[g.N];
        var rnd = new Random(7);
        foreach (int[] comp in Components(g)) {
            if (comp.Length == 1) { x[comp[0]] = 1; GlobalSign(s, comp, x); continue; }
            var v = new double[g.N]; var w = new double[g.N]; var dinv = new double[g.N];
            foreach (int i in comp) { v[i] = rnd.NextDouble() - 0.5; dinv[i] = 1 / Math.Sqrt(Math.Max(1, g.Adj[i].Count)); }
            for (int it = 0; it < Iterations; ++it) {
                double norm = 0;
                foreach (int i in comp) {
                    double acc = v[i];
                    foreach (var (j, sg) in g.Adj[i]) acc += sg * dinv[i] * dinv[j] * v[j];
                    w[i] = acc; norm += acc * acc;
                }
                norm = Math.Sqrt(norm);
                foreach (int i in comp) v[i] = w[i] / norm;
            }
            foreach (int i in comp) x[i] = v[i] >= 0 ? 1 : -1;
            GlobalSign(s, comp, x);
        }
        return x;
    }

    // the library: weld by the clusters into a DMesh3, MeshRepairOrientation.OrientComponents + SolveGlobalOrientation
    static int[] Library(Soup s, Graph g, out int notAppended)
    {
        var m = new DMesh3();
        var vid = new Dictionary<int, int>();
        int V(int c, Vector3d p) { if (!vid.TryGetValue(c, out int id)) vid[c] = id = m.AppendVertex(p); return id; }
        var tidOf = new int[g.N]; notAppended = 0;
        for (int i = 0; i < g.N; ++i) {
            int a = V(g.Corner[i][0], s.T[i][0]), b = V(g.Corner[i][1], s.T[i][1]), c = V(g.Corner[i][2], s.T[i][2]);
            tidOf[i] = (a == b || b == c || a == c) ? DMesh3.InvalidID : m.AppendTriangle(a, b, c);
            if (tidOf[i] < 0) notAppended++;
        }
        var before = new Index3i[g.N];
        for (int i = 0; i < g.N; ++i) if (tidOf[i] >= 0) before[i] = m.GetTriangle(tidOf[i]);
        var o = new MeshRepairOrientation(m); o.OrientComponents(); o.SolveGlobalOrientation();
        var x = new int[g.N];
        for (int i = 0; i < g.N; ++i) {
            if (tidOf[i] < 0) { x[i] = 1; continue; }     // not in the mesh: left as it is
            Index3i t = m.GetTriangle(tidOf[i]), b0 = before[i];
            bool same = (t.a == b0.a && t.b == b0.b) || (t.a == b0.b && t.b == b0.c) || (t.a == b0.c && t.b == b0.a);
            x[i] = same ? 1 : -1;
        }
        return x;
    }

    static DMesh3 SoupMesh(Soup s)
    {
        var m = new DMesh3();
        foreach (var t in s.T) m.AppendTriangle(m.AppendVertex(t[0]), m.AppendVertex(t[1]), m.AppendVertex(t[2]));
        return m;
    }

    // a repaired mesh's orientation, measured on the soup: per soup triangle, the nearest triangle of the output at its
    // centroid; correct when its normal agrees with the true outward normal
    static double OutputShare(Soup s, DMesh3 outMesh)
    {
        if (outMesh == null || outMesh.TriangleCount == 0) return 0;
        var tree = new DMeshAABBTree3(outMesh, true);
        double ok = 0, all = 0;
        for (int i = 0; i < s.T.Count; ++i) {
            var t = s.T[i];
            Vector3d n = MathUtil.Normal(t[0], t[1], t[2]); if (s.Flipped[i]) n = -n;
            int near = tree.FindNearestTriangle((t[0] + t[1] + t[2]) / 3);
            double a = s.Area(i); all += a;
            if (near >= 0 && outMesh.GetTriNormal(near).Dot(n) > 0) ok += a;
        }
        return ok / all;
    }

    // the steps of WindingContouring.OrientedCopy (internal): inverted components reversed, then the whole by its signed volume
    static DMesh3 OrientedCopy(DMesh3 m)
    {
        DMesh3 copy = new DMesh3(m, true);
        foreach (int[] comp in MeshPostRepairCleanup.FindInvertedComponents(copy))
            foreach (int tid in comp) copy.ReverseTriOrientation(tid);
        Vector3d c = copy.CachedBounds.Center;
        if (MeshMeasurements.VolumeArea(copy, copy.TriangleIndices(), (vid) => copy.GetVertex(vid) - c).x < 0)
            copy.ReverseOrientation(false);
        return copy;
    }

    static double Share(Soup s, int[] x)
    {
        double ok = 0, all = 0;
        for (int i = 0; i < s.T.Count; ++i) {
            double a = s.Area(i); all += a;
            bool finalFlipped = s.Flipped[i] ^ (x[i] < 0);
            if (!finalFlipped) ok += a;
        }
        return ok / all;
    }

    public static void Run()
    {
        var cube = PolyCube(4, 3, 2, (x, y, z) => true, Vector3d.Zero);
        var checker = PolyCube(4, 4, 2, (x, y, z) => (x + y + z) % 2 == 0, Vector3d.Zero);
        var twoBlocks = PolyCube(2, 2, 2, (x, y, z) => true, Vector3d.Zero).Concat(PolyCube(2, 2, 2, (x, y, z) => true, new Vector3d(2 + 5e-4, 0, 0))).ToList();
        var sphere = SphereTris(16);
        Func<int, Func<int, Random, bool>> everyK = k => (i, r) => i % k == 0;
        Func<double, Func<int, Random, bool>> rate = p => (i, r) => r.NextDouble() < p;
        var cases = new List<(string name, Soup soup, double eps)> {
            ("#1548 PolyCube(4,3,2), every 5th", MakeSoup(cube, everyK(5), 0, 1), 1e-9),
            ("#1548 PolyCube(4,3,2), every 3rd", MakeSoup(cube, everyK(3), 0, 1), 1e-9),
            ("#1548 PolyCube(4,3,2), every 2nd", MakeSoup(cube, everyK(2), 0, 1), 1e-9),
            ("cube, jitter 1e-4, 30 % random, eps 1e-3", MakeSoup(cube, rate(0.3), 1e-4, 2), 1e-3),
            ("sphere n=16, jitter 1e-4, 50 % random, eps 1e-3", MakeSoup(sphere, rate(0.5), 1e-4, 3), 1e-3),
            ("cube, 10 % dropped, every 3rd", MakeSoup(cube, everyK(3), 0, 4, 0.10), 1e-9),
            ("checkerboard (non-manifold edges), every 3rd", MakeSoup(checker, everyK(3), 0, 5), 1e-9),
            ("two blocks, gap 5e-4 < eps 1e-3, every 3rd", MakeSoup(twoBlocks, everyK(3), 0, 6), 1e-3),
            ("cube, jitter 1e-3 = eps (welds fail), every 3rd", MakeSoup(cube, everyK(3), 1e-3, 7), 1e-3),
        };
        Console.WriteLine("| case | tris | links | wrong links | components | BFS | Z2 spectral | library weld + MeshRepairOrientation | MeshAutoRepair | OrientedCopy (soup) | ms: BFS / spectral / library / auto | radial links: wrong links / BFS / spectral |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (name, soup, eps) in cases) {
            var g = Build(soup, eps);
            var gr = Build(soup, eps, radial: true);
            int comps = Components(g).Count;
            var sw = Stopwatch.StartNew(); var xb = Bfs(soup, g); long tb = sw.ElapsedMilliseconds;
            sw.Restart(); var xs = Spectral(soup, g); long ts = sw.ElapsedMilliseconds;
            sw.Restart(); var xl = Library(soup, g, out int na); long tl = sw.ElapsedMilliseconds;
            DMesh3 auto = SoupMesh(soup);
            sw.Restart(); var ar = new MeshAutoRepair(auto); bool ok = ar.Apply(); long ta = sw.ElapsedMilliseconds;
            DMesh3 oc = OrientedCopy(SoupMesh(soup));
            string F(double v) => v == 1 ? "1" : v.ToString("F3");
            Console.WriteLine($"| {name} | {soup.T.Count} | {g.Links} | {g.Frustrated} | {comps} | {F(Share(soup, xb))} | {F(Share(soup, xs))} | {F(Share(soup, xl))}{(na > 0 ? $" ({na} not appended)" : "")} | {F(OutputShare(soup, ar.Mesh))} ({ar.Mesh.TriangleCount} tris{(ok ? "" : ", Apply false")}) | {F(OutputShare(soup, oc))} | {tb} / {ts} / {tl} / {ta} | {gr.Frustrated} / {F(Share(soup, Bfs(soup, gr)))} / {F(Share(soup, Spectral(soup, gr)))} |");
        }
    }
}
