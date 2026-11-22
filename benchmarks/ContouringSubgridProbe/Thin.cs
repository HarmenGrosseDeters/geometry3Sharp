// Part "thin": walls thinner than a grid cell.
// SubgridMT is a restricted prototype of Subgrid Marching Tetrahedra (Baktash, Gillespie, Crane 2026; from the paper text):
// every cube is split into five tetrahedra, every edge of the tetrahedra stores ALL its crossings with the surface (not one
// sign change), and the surface in a tetrahedron is rebuilt from the crossing counts as normal curves on its faces.
// Where a face violates the triangle inequality of the counts (the surface leaves and re-enters through one edge), the
// surplus crossings of that edge are joined in pairs ("scoops"), as in the paper. Simplifications against the paper: a loop
// with a scoop is filled by a fan from a point half way to the centre of the tetrahedron and a longer normal loop by a fan
// from its centroid (the paper has dedicated cases and proves the result free of intersections; this prototype only measures
// it), several scoops on one edge of a face are paired as neighbours or nested (NestedScoops; the counts alone do not decide), and the crossings come from sampling or sphere tracing.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using gs;

sealed class SubgridMT
{
    public Func<Vector3d, double> F;
    public Vector3d Origin; public double H; public int N;      // N cells per axis
    public int SubSamples = 8;                                     // intervals per edge for the sign-change search; 0: sphere tracing (needs Lipschitz)
    public double TraceTolerance = 1.0 / 64;                       // sphere tracing: smallest step, in cells (a wall or gap thinner than this can be missed)
    public bool NestedScoops;                                       // several scoops on one edge of a face: nested pairs instead of adjacent ones
    public double Lipschitz = 0;                                   // > 0: skip an edge that cannot reach the level (|f| > L * length at both ends)
    // two-level mode (no evaluations on the edges): the field is max(|G| - W, D) and the crossings are those of the same expression
    // with G and D interpolated linearly along the edge, so the two sides of the wall are two levels of one linear function
    public Func<Vector3d, double> G, D; public double W;
    public long Evaluations;
    public DMesh3 Mesh;
    public int Scoops, Tips, FailedTriangles, MultiEdges, CrossedEdges, SampledEdges;
    public int[] LoopLengths = new int[4];                         // normal loops of 3, 4, more crossings; loops with a scoop

    double[] node, nodeG, nodeD;
    readonly Dictionary<long, List<double>> crossings = new Dictionary<long, List<double>>();
    readonly Dictionary<long, int[]> vids = new Dictionary<long, int[]>();

    static readonly int[][] EvenTets = { new[] { 0, 3, 5, 6 }, new[] { 1, 0, 3, 5 }, new[] { 2, 0, 3, 6 }, new[] { 4, 0, 5, 6 }, new[] { 7, 3, 5, 6 } };
    static readonly int[][] OddTets = { new[] { 1, 2, 4, 7 }, new[] { 0, 1, 2, 4 }, new[] { 3, 1, 2, 7 }, new[] { 5, 1, 4, 7 }, new[] { 6, 2, 4, 7 } };
    static readonly int[][] TetEdges = { new[] { 0, 1 }, new[] { 0, 2 }, new[] { 0, 3 }, new[] { 1, 2 }, new[] { 1, 3 }, new[] { 2, 3 } };
    static readonly int[][] TetFaces = { new[] { 0, 1, 2 }, new[] { 0, 1, 3 }, new[] { 0, 2, 3 }, new[] { 1, 2, 3 } };
    static int EdgeOf(int a, int b) { if (a > b) { int t = a; a = b; b = t; } return a == 0 ? b - 1 : a + b; }   // (0,1)..(2,3) -> 0..5

    int NodeId(int i, int j, int k) { return i + (N + 1) * (j + (N + 1) * k); }
    Vector3d NodePos(int id) { int n1 = N + 1; return new Vector3d(Origin.x + H * (id % n1), Origin.y + H * (id / n1 % n1), Origin.z + H * (id / (n1 * n1))); }
    long Key(int a, int b) { return a < b ? a * 2654435761L + b : b * 2654435761L + a; }     // unique for b < 2^31; mixes the low word (the hash of a long is low ^ high)
    double Eval(Vector3d p) { ++Evaluations; return F(p); }

    public void Generate()
    {
        int n1 = N + 1;
        node = new double[n1 * n1 * n1];
        if (G != null) {
            nodeG = new double[node.Length]; nodeD = new double[node.Length];
            for (int id = 0; id < node.Length; ++id) {
                Vector3d p = NodePos(id);
                nodeG[id] = G(p); nodeD[id] = D(p); ++Evaluations;
                node[id] = Math.Max(Math.Abs(nodeG[id]) - W, nodeD[id]);
            }
        } else
            for (int id = 0; id < node.Length; ++id) node[id] = Eval(NodePos(id));
        var tets = new int[5][];
        for (int t = 0; t < 5; ++t) tets[t] = new int[4];
        // 1. crossings of every edge of the tetrahedra (each edge once)
        ForEachTet(tets, tet => {
            foreach (int[] e in TetEdges) {
                int a = Math.Min(tet[e[0]], tet[e[1]]), b = Math.Max(tet[e[0]], tet[e[1]]);
                long key = Key(a, b);
                if (crossings.ContainsKey(key)) continue;
                crossings[key] = FindCrossings(a, b);
            }
        });
        foreach (var kv in crossings) { if (kv.Value != null) { ++CrossedEdges; if (kv.Value.Count > 1) ++MultiEdges; } }
        // 2. the surface in each tetrahedron
        Mesh = new DMesh3();
        ForEachTet(tets, MeshTet);
    }

    void ForEachTet(int[][] tets, Action<int[]> action)
    {
        var corner = new int[8];
        for (int k = 0; k < N; ++k) for (int j = 0; j < N; ++j) for (int i = 0; i < N; ++i) {
            for (int c = 0; c < 8; ++c) corner[c] = NodeId(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1));
            int[][] split = ((i + j + k) & 1) == 0 ? EvenTets : OddTets;
            for (int t = 0; t < 5; ++t) {
                for (int q = 0; q < 4; ++q) tets[t][q] = corner[split[t][q]];
                action(tets[t]);
            }
        }
    }

    int Count(int a, int b) { List<double> l = crossings[Key(a, b)]; return l == null ? 0 : l.Count; }

    // parameters (from node a, a < b) of the sign changes of F on the edge, by SubSamples intervals and bisection
    List<double> FindCrossings(int a, int b)
    {
        if (G != null) return LinearCrossings(a, b);
        Vector3d pa = NodePos(a), pb = NodePos(b);
        double fa = node[a], fb = node[b];
        if (Lipschitz > 0 && Math.Min(Math.Abs(fa), Math.Abs(fb)) > Lipschitz * pa.Distance(pb)) return null;
        ++SampledEdges;
        List<double> res = null;
        double len = pa.Distance(pb);
        if (SubSamples == 0) {
            // sphere tracing along the edge: a step of |f| / L cannot pass a crossing; within Tol of the level, step over it
            double tol = TraceTolerance * H / len;       // as a parameter step
            double t = 0, f = fa;
            while (t < 1) {
                double step = Math.Max(Math.Abs(f) / (Lipschitz * len), tol);
                double t1 = Math.Min(1, t + step);
                double f1 = t1 == 1 ? fb : Eval(pa + t1 * (pb - pa));
                if ((f < 0) != (f1 < 0)) Bisect(pa, pb, t, t1, f < 0, ref res);
                t = t1; f = f1;
            }
            return res;
        }
        double t0 = 0, f0 = fa;
        for (int s = 1; s <= SubSamples; ++s) {
            double t1 = (double)s / SubSamples;
            double f1 = s == SubSamples ? fb : Eval(pa + t1 * (pb - pa));
            if ((f0 < 0) != (f1 < 0)) Bisect(pa, pb, t0, t1, f0 < 0, ref res);
            t0 = t1; f0 = f1;
        }
        return res;
    }

    List<double> LinearCrossings(int a, int b)
    {
        double ga = nodeG[a], gb = nodeG[b], da = nodeD[a], db = nodeD[b];
        var cand = new List<double>(3);
        if (ga != gb) { cand.Add((W - ga) / (gb - ga)); cand.Add((-W - ga) / (gb - ga)); }
        if (da != db) cand.Add(-da / (db - da));
        cand.RemoveAll(t => !(t > 0 && t < 1));
        if (cand.Count == 0) return null;
        cand.Sort();
        List<double> res = null;
        for (int i = 0; i < cand.Count; ++i) {
            double before = 0.5 * ((i == 0 ? 0 : cand[i - 1]) + cand[i]), after = 0.5 * (cand[i] + (i + 1 == cand.Count ? 1 : cand[i + 1]));
            bool insideBefore = Math.Max(Math.Abs(ga + before * (gb - ga)) - W, da + before * (db - da)) < 0;
            bool insideAfter = Math.Max(Math.Abs(ga + after * (gb - ga)) - W, da + after * (db - da)) < 0;
            if (insideBefore == insideAfter) continue;
            if (res == null) res = new List<double>();
            res.Add(cand[i]);
        }
        return res;
    }

    void Bisect(Vector3d pa, Vector3d pb, double lo, double hi, bool loInside, ref List<double> res)
    {
        for (int it = 0; it < 40; ++it) {
            double mid = 0.5 * (lo + hi);
            if ((Eval(pa + mid * (pb - pa)) < 0) == loInside) lo = mid; else hi = mid;
        }
        if (res == null) res = new List<double>();
        res.Add(0.5 * (lo + hi));
    }

    int[] VertexIds(int a, int b)
    {
        long key = Key(a, b);
        int[] ids;
        if (vids.TryGetValue(key, out ids)) return ids;
        List<double> l = crossings[key];
        Vector3d pa = NodePos(Math.Min(a, b)), pb = NodePos(Math.Max(a, b));
        ids = new int[l.Count];
        for (int i = 0; i < ids.Length; ++i) ids[i] = Mesh.AppendVertex(pa + l[i] * (pb - pa));
        vids[key] = ids;
        return ids;
    }

    struct LocalPoint { public int Vid, MinNode, Index, Edge; }      // Index: position on its edge, counted from the edge's smaller node

    void MeshTet(int[] tet)
    {
        var cnt = new int[6]; var offset = new int[7];
        int total = 0;
        for (int e = 0; e < 6; ++e) { cnt[e] = Count(tet[TetEdges[e][0]], tet[TetEdges[e][1]]); offset[e] = total; total += cnt[e]; }
        if (total == 0) return;
        var pts = new LocalPoint[total];
        for (int e = 0; e < 6; ++e) {
            if (cnt[e] == 0) continue;
            int a = tet[TetEdges[e][0]], b = tet[TetEdges[e][1]];
            int[] ids = VertexIds(a, b);
            for (int i = 0; i < cnt[e]; ++i) pts[offset[e] + i] = new LocalPoint { Vid = ids[i], MinNode = Math.Min(a, b), Index = i, Edge = e };
        }
        // arcs on the four faces: the i-th crossings from a corner on its two edges are joined
        var plist = new List<LocalPoint>(pts);
        var next = new List<int[]>(total + 4);
        for (int i = 0; i < total; ++i) next.Add(new[] { -1, -1 });
        Action<int, int> join = (p, q) => { next[p][next[p][0] < 0 ? 0 : 1] = q; next[q][next[q][0] < 0 ? 0 : 1] = p; };
        foreach (int[] f in TetFaces)
            for (int r = 0; r < 3; ++r) {
                int a = f[r], b = f[(r + 1) % 3], c = f[(r + 2) % 3];
                int eab = EdgeOf(a, b), eac = EdgeOf(a, c), ebc = EdgeOf(b, c);
                int x = cnt[eab], y = cnt[eac], z = cnt[ebc];
                int arcs = Math.Max(0, Math.Min((x + y - z) / 2, Math.Min(x, y)));
                for (int i = 0; i < arcs; ++i)
                    join(offset[eab] + (tet[a] < tet[b] ? i : x - 1 - i), offset[eac] + (tet[a] < tet[c] ? i : y - 1 - i));
                // scoops: the crossings of ab beyond those that the corners a and b take, joined in pairs through a vertex in the face
                // (a straight arc would lie in the edge and coincide with the scoops of the other faces at that edge)
                int surplus = x - z - y;
                for (int s = 0; s + 1 < surplus; s += 2) {
                    int i = NestedScoops ? y + s / 2 : y + s, j = NestedScoops ? x - z - 1 - s / 2 : y + s + 1;
                    int p = offset[eab] + (tet[a] < tet[b] ? i : x - 1 - i);
                    int q = offset[eab] + (tet[a] < tet[b] ? j : x - 1 - j);
                    int vp = plist[p].Vid, vq = plist[q].Vid;
                    var key = new ValueTuple<int, int, int>(Math.Min(vp, vq), Math.Max(vp, vq), tet[c]);
                    int vs;
                    if (!scoopVids.TryGetValue(key, out vs)) {
                        Vector3d pp = Mesh.GetVertex(vp), pq = Mesh.GetVertex(vq), mid = 0.5 * (pp + pq);
                        vs = Mesh.AppendVertex(mid + (NodePos(tet[c]) - mid).Normalized * (0.5 * pp.Distance(pq)));
                        scoopVids[key] = vs;
                    }
                    int sp = plist.Count;
                    plist.Add(new LocalPoint { Vid = vs, MinNode = -1, Index = 0, Edge = -1 });
                    next.Add(new[] { -1, -1 });
                    join(p, sp); join(sp, q);
                    ++Scoops;
                }
            }
        Vector3d centre = 0.25 * (NodePos(tet[0]) + NodePos(tet[1]) + NodePos(tet[2]) + NodePos(tet[3]));
        pts = plist.ToArray();
        var done = new bool[pts.Length];
        var loop = new List<int>();
        for (int s = 0; s < pts.Length; ++s) {
            if (done[s]) continue;
            loop.Clear();
            int prev = -1, cur = s;
            while (!done[cur]) {
                done[cur] = true; loop.Add(cur);
                int nx = (next[cur][0] != prev || loop.Count == 1) ? next[cur][0] : next[cur][1];
                prev = cur; cur = nx;
            }
            Fill(loop, pts, centre);
        }
    }

    readonly Dictionary<ValueTuple<int, int, int>, int> scoopVids = new Dictionary<ValueTuple<int, int, int>, int>();

    readonly List<Index3i> fillTris = new List<Index3i>();

    void Fill(List<int> loop, LocalPoint[] pts, Vector3d tetCentre)
    {
        int n = loop.Count;
        int scoops = 0;
        for (int i = 0; i < n; ++i) if (pts[loop[i]].Edge < 0) ++scoops;
        bool scoop = scoops > 0;
        if (n == 4 && scoops == 2) ++Tips;
        ++LoopLengths[scoop ? 3 : n == 3 ? 0 : n == 4 ? 1 : 2];
        var p = new Vector3d[n + 1]; var v = new int[n + 1];
        Vector3d centroid = Vector3d.Zero;
        for (int i = 0; i < n; ++i) { v[i] = pts[loop[i]].Vid; p[i] = Mesh.GetVertex(v[i]); centroid += p[i] / n; }
        fillTris.Clear();
        if (!scoop && n == 3) fillTris.Add(new Index3i(0, 1, 2));
        else if (!scoop && n == 4) {
            int s = 0;
            for (int i = 1; i < 4; ++i) if (v[i] < v[s]) s = i;
            fillTris.Add(new Index3i(s, (s + 1) % 4, (s + 2) % 4)); fillTris.Add(new Index3i(s, (s + 2) % 4, (s + 3) % 4));
        } else {
            p[n] = scoop ? 0.5 * (centroid + tetCentre) : centroid;
            v[n] = Mesh.AppendVertex(p[n]);
            for (int i = 0; i < n; ++i) fillTris.Add(new Index3i(n, i, (i + 1) % n));
        }
        // orientation: the side of crossing r of an edge towards the edge's smaller node m is inside if m is inside and r is even;
        // decided at the triangle corner where the triangle is most transversal to that edge
        double bestDot = 0; bool flip = false;
        foreach (Index3i t in fillTris) {
            Vector3d normal = (p[t.b] - p[t.a]).Cross(p[t.c] - p[t.a]);
            if (normal.LengthSquared == 0) continue;
            normal.Normalize();
            for (int c = 0; c < 3; ++c) {
                if (t[c] == n) continue;
                LocalPoint lp = pts[loop[t[c]]];
                if (lp.MinNode < 0) continue;
                Vector3d d = NodePos(lp.MinNode) - p[t[c]];
                double dot = normal.Dot(d) / d.Length;
                if (Math.Abs(dot) <= Math.Abs(bestDot)) continue;
                bestDot = dot;
                bool towardsInside = (node[lp.MinNode] < 0) == ((lp.Index & 1) == 0);
                flip = towardsInside == (dot > 0);          // the normal must point away from the inside
            }
        }
        foreach (Index3i t in fillTris) { if (flip) Add(v[t.a], v[t.c], v[t.b]); else Add(v[t.a], v[t.b], v[t.c]); }
    }

    void Add(int a, int b, int c) { if (Mesh.AppendTriangle(a, b, c) < 0) ++FailedTriangles; }
}

static class ThinProbe
{
    static double Box(Vector3d p, Vector3d half)
    {
        Vector3d q = new Vector3d(Math.Abs(p.x) - half.x, Math.Abs(p.y) - half.y, Math.Abs(p.z) - half.z);
        double outside = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0)).Length;
        return outside + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
    }

    // inside where < 0, all in [-1, 1]^3; t = wall thickness; lip = a Lipschitz bound of the field
    static Func<Vector3d, double> Make(string shape, double t, double h, int seed, out string truth, out double lip, out Func<Vector3d, double> g, out Func<Vector3d, double> d, out double w)
    {
        Matrix3d R = Program.Rotation(seed);
        lip = 1; g = null; d = null; w = t / 2;
        switch (shape) {
            case "plate": truth = "c=1;g=0"; return p => Box(R * p, new Vector3d(0.6, 0.5, t / 2));
            case "shell": truth = "c=2;g=0,0"; g = p => p.Length - 0.7; d = p => -1; return p => Math.Abs(p.Length - 0.7) - t / 2;
            // gyroid sheet, 16 cells per period (the default of TpmsLattice), clipped to a ball; |grad g| / k is between 1 and sqrt 3
            // on the surface, so the wall is between t / sqrt 3 and t thick
            case "gyroid": truth = null; lip = Math.Sqrt(6); {
                double k = 2 * Math.PI / (16 * h);
                g = p => { Vector3d q = R * p; return (Math.Sin(k * q.x) * Math.Cos(k * q.y) + Math.Sin(k * q.y) * Math.Cos(k * q.z) + Math.Sin(k * q.z) * Math.Cos(k * q.x)) / k; };
                d = p => p.Length - 0.8;
                return p => { Vector3d q = R * p; double g = Math.Sin(k * q.x) * Math.Cos(k * q.y) + Math.Sin(k * q.y) * Math.Cos(k * q.z) + Math.Sin(k * q.z) * Math.Cos(k * q.x);
                    return Math.Max(Math.Abs(g) / k - t / 2, p.Length - 0.8); }; }
        }
        throw new ArgumentException(shape);
    }

    static Vector3d Grad(Func<Vector3d, double> f, Vector3d p, double e)
    {
        return new Vector3d(f(new Vector3d(p.x + e, p.y, p.z)) - f(new Vector3d(p.x - e, p.y, p.z)), f(new Vector3d(p.x, p.y + e, p.z)) - f(new Vector3d(p.x, p.y - e, p.z)),
            f(new Vector3d(p.x, p.y, p.z + e)) - f(new Vector3d(p.x, p.y, p.z - e))) / (2 * e);
    }

    // points on the level set with their normals: random points near it, projected by Newton steps along the gradient
    static void TruthSamples(Func<Vector3d, double> f, double h, int count, List<Vector3d> pts, List<Vector3d> normals)
    {
        var rnd = new Random(7);
        int tries = 0;
        while (pts.Count < count && tries++ < count * 2000) {
            Vector3d p = new Vector3d(2 * rnd.NextDouble() - 1, 2 * rnd.NextDouble() - 1, 2 * rnd.NextDouble() - 1);
            if (Math.Abs(f(p)) > h) continue;
            bool ok = false;
            for (int it = 0; it < 30; ++it) {
                double v = f(p); Vector3d g = Grad(f, p, 1e-7);
                if (Math.Abs(v) < 1e-10) { ok = true; break; }
                if (g.LengthSquared < 1e-12) break;
                p -= g * (v / g.LengthSquared);
            }
            Vector3d n = Grad(f, p, 1e-7);
            if (!ok || n.Length < 0.2) continue;       // not converged, or on a crease of the field
            pts.Add(p); normals.Add(n.Normalized);
        }
    }

    static string Signature(DMesh3 m)
    {
        MeshTopologySummary s = MeshTopologySummary.Compute(m);
        var g = new List<double>();
        foreach (var c in s.ComponentList) g.Add(c.Genus);
        g.Sort();
        if (g.Count > 4) return "c=" + s.Components + ";genus sum " + s.Genus;
        return "c=" + s.Components + ";g=" + string.Join(",", g);
    }

    public static void Run(int seeds, int res)
    {
        Console.WriteLine("# thin: walls of k cells; coverage = share of the true surface with a result triangle within 0.25 cells and a normal within 60 degrees");
        Console.WriteLine("shape, k, seed, variant, triangles, clean, topology, truth, coverage, maxErr, evals, bandEvals, ms, notes");
        var agg = new Dictionary<string, double[]>();
        foreach (string shape in new[] { "plate", "shell", "gyroid" })
            foreach (double k in new[] { 0.25, 0.5, 0.75, 1.0, 1.5 })
                for (int seed = 0; seed < seeds; ++seed) {
                    double h = 2.0 / res;
                    string truth; double lip;
                    Func<Vector3d, double> gF, dF; double wF;
                    Func<Vector3d, double> f = Make(shape, k * h, h, seed, out truth, out lip, out gF, out dF, out wF);
                    Vector3d org = -Vector3d.One - h * (2 * Vector3d.One + Program.Shift(seed));
                    int n = res + 5;
                    var bounds = new AxisAlignedBox3d(org, org + n * h * Vector3d.One);
                    var pts = new List<Vector3d>(); var nrm = new List<Vector3d>();
                    TruthSamples(f, h, 4000, pts, nrm);
                    if (truth == null) {       // the gyroid: a marching cubes run on a grid 8 times finer (2 fine cells per wall at k = 0.25)
                        var fine = new MarchingCubes { Implicit = new FuncImplicit(f), Bounds = bounds, CubeSize = h / 8, RootMode = MarchingCubes.RootfindingModes.Bisection };
                        fine.Generate();
                        truth = Signature(fine.Mesh) + (MeshRobustRepair.NotCleanReason(fine.Mesh, false) == "" ? " (x8)" : " (x8, not clean)");
                    }
                    // band: the coarse cells that can hold the surface by the Lipschitz bound; a refinement inside them costs r^3 evaluations each
                    long bandCells = 0;
                    for (int kk = 0; kk < n; ++kk) for (int jj = 0; jj < n; ++jj) for (int ii = 0; ii < n; ++ii)
                        if (Math.Abs(f(org + h * new Vector3d(ii + 0.5, jj + 0.5, kk + 0.5))) <= lip * h * Math.Sqrt(3) / 2) ++bandCells;
                    long coarseNodes = (long)(n + 1) * (n + 1) * (n + 1);

                    foreach (string variant in new[] { "mc", "dc", "mc/2", "mc/4", "sub8", "subtrace", "subtrace-n", "sub2level" }) {
                        var impl = new FuncImplicit(f);
                        DMesh3 m; string notes = ""; long band;
                        var sw = Stopwatch.StartNew();
                        if (variant.StartsWith("mc")) {
                            int r = variant == "mc" ? 1 : int.Parse(variant.Substring(3));
                            var mc = new MarchingCubes { Implicit = impl, Bounds = bounds, CubeSize = h / r, RootMode = MarchingCubes.RootfindingModes.Bisection };
                            mc.Generate(); m = mc.Mesh;
                            band = r == 1 ? coarseNodes : coarseNodes + bandCells * r * r * r;
                        } else if (variant == "dc") {
                            var dc = new DualContouring { Implicit = impl, InsideIsLess = true, Bounds = bounds, CellSize = h };
                            dc.Generate(); m = dc.Mesh; band = coarseNodes;
                        } else {
                            if (variant == "sub2level" && gF == null) continue;
                            bool trace = variant.StartsWith("subtrace");
                            var sub = new SubgridMT { F = f, Origin = org, H = h, N = n, SubSamples = trace ? 0 : variant == "sub8" ? 8 : 1, Lipschitz = trace ? lip : 0, NestedScoops = variant.EndsWith("-n") };
                            if (variant == "sub2level") { sub.G = gF; sub.D = dF; sub.W = wF; }
                            sub.Generate(); m = sub.Mesh; impl.Evaluations = sub.Evaluations; band = sub.Evaluations;
                            notes = "multi-crossing edges " + sub.MultiEdges + "/" + sub.CrossedEdges + "; scoops " + sub.Scoops + "; loops 3/4/longer/with scoop/tips " + string.Join("/", sub.LoopLengths) + "/" + sub.Tips
                                + "; failed triangles " + sub.FailedTriangles;
                        }
                        double ms = sw.Elapsed.TotalMilliseconds;
                        string clean = m.TriangleCount == 0 ? "empty" : MeshRobustRepair.NotCleanReason(m);
                        string topo = m.TriangleCount == 0 ? "c=0" : Signature(m);
                        double covered = 0, maxErr = 0;
                        if (m.TriangleCount > 0) {
                            var tree = new DMeshAABBTree3(m, true);
                            for (int i = 0; i < pts.Count; ++i) {
                                double d2; int tid = tree.FindNearestTriangle(pts[i], out d2);
                                if (Math.Sqrt(d2) <= 0.25 * h && m.GetTriNormal(tid).Dot(nrm[i]) > 0.5) ++covered;
                            }
                            foreach (int vid in m.VertexIndices()) {
                                Vector3d p = m.GetVertex(vid); Vector3d g = Grad(f, p, 1e-7);
                                if (g.Length > 0.2) maxErr = Math.Max(maxErr, Math.Abs(f(p)) / g.Length / h);
                            }
                        }
                        double coverage = covered / pts.Count;
                        bool topoOk = clean == "" && truth.StartsWith(topo);
                        Console.WriteLine(string.Format("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8:F3}, {9:F3}, {10}, {11}, {12:F0}, {13}",
                            shape, k, seed, variant, m.TriangleCount, clean == "" ? "yes" : clean, topo, truth, coverage, maxErr, impl.Evaluations, band, ms, notes));
                        string key = shape + ", " + k + ", " + variant;
                        double[] a;
                        if (!agg.TryGetValue(key, out a)) agg[key] = a = new double[8];
                        a[0] += 1; a[1] += clean == "" ? 1 : 0; a[2] += topoOk ? 1 : 0; a[3] += coverage; a[4] += m.TriangleCount; a[5] += impl.Evaluations; a[6] += band; a[7] += ms;
                    }
                }
        Console.WriteLine("# thin, per shape, k and variant: sums of runs, means of the rest");
        Console.WriteLine("shape, k, variant, runs, clean, topology right, coverage, triangles, evals, bandEvals, ms");
        foreach (var kv in agg) {
            double[] a = kv.Value;
            Console.WriteLine(string.Format("{0}, {1}, {2}, {3}, {4:F3}, {5:F0}, {6:F0}, {7:F0}, {8:F0}", kv.Key, a[0], a[1], a[2], a[3] / a[0], a[4] / a[0], a[5] / a[0], a[6] / a[0], a[7] / a[0]));
        }
    }
}
