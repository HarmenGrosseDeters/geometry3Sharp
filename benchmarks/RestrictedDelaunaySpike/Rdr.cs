// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Stage 1a spike of docs/design/CERTIFIED_REMESHING.md: restricted Delaunay refinement of a clean closed mesh S with
// floating-point surface tests. Not library code. The Delaunay triangulation is the exact Delaunay3; the dual
// Voronoi edge of a facet is tested against S with DMeshAABBTree3.FindAllHitTriangles (exact ray-triangle decisions),
// the crossing points (surface Delaunay ball centres) are constructed in floating point. The TBP is checked by a
// proxy: (E') every dual edge meets S at most once, and every sample's restricted facets form one closed umbrella
// (its link a single cycle). The exact counting checks of (F') / (C') (section 14.1) are stage 1b.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

namespace RestrictedDelaunaySpike
{
    public class Rdr
    {
        public double RMax, HMin, Rho = 1;
        public int MaxInsertions = 2000000;

        readonly DMesh3 S;
        readonly DMeshAABBTree3 tree;
        Delaunay3 dt;

        struct Facet
        {
            public int Cell, Stamp, Face;   // one cell containing it (validity: the cell is alive with this stamp)
            public int Hits;
            public Vector3d Centre;         // the farthest crossing from the vertices (largest surface Delaunay ball)
            public double R2;
            public Vector3d SurfaceNormal;  // of the input triangle at the centre
        }
        readonly Dictionary<(int, int, int), Facet> facets = new Dictionary<(int, int, int), Facet>();
        readonly Dictionary<int, HashSet<(int, int, int)>> star = new Dictionary<int, HashSet<(int, int, int)>>();

        // queue: facets (kind 0) and vertices (kind 1), largest squared radius first, ties by key
        readonly PriorityQueue<(int kind, (int, int, int) key), (double, (int, int, int))> queue =
            new PriorityQueue<(int, (int, int, int)), (double, (int, int, int))>(Comparer<(double, (int, int, int))>.Create((x, y) => {
                int c = y.Item1.CompareTo(x.Item1);
                return c != 0 ? c : x.Item2.CompareTo(y.Item2);
            }));
        readonly HashSet<(int, (int, int, int), int)> refused = new HashSet<(int, (int, int, int), int)>();

        public int Inserted, Refused, Failed;
        /// <summary> probe hooks (stage 1b upper bound): called once the initial sample is in (with RefinementSupport on), and
        /// after every accepted insertion with the new vertex and the cavity's removed / added cells </summary>
        public Action Seeded;
        public Action<int, List<int>, List<int>> AfterInsert;
        public double Seconds;
        readonly List<int> removed = new List<int>(), added = new List<int>(), hits = new List<int>();

        public Rdr(DMesh3 s)
        {
            S = s;
            tree = new DMeshAABBTree3(S, true);
        }

        static (int, int, int) Key(Index3i t)
        {
            int a = t.a, b = t.b, c = t.c;
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }

        bool valid(in Facet f) => dt.IsCell(f.Cell) && dt.CellStamp(f.Cell) == f.Stamp;

        // the dual Voronoi edge of facet (c, i) against S
        public double FacetSeconds;
        readonly Stopwatch facetWatch = new Stopwatch();
        void compute_facet(int c, int i)
        {
            facetWatch.Start();
            try { compute_facet_(c, i); } finally { facetWatch.Stop(); FacetSeconds = facetWatch.Elapsed.TotalSeconds; }
        }

        void compute_facet_(int c, int i)
        {
            int n = dt.Neighbour(c, i);
            Index3i fv = dt.FacetVertices(c, i);
            var key = Key(fv);
            if (n < 0) { facets.Remove(key); return; }       // on the box (S is far inside it)
            Vector3d c1 = dt.Circumcenter(c), c2 = dt.Circumcenter(n);
            Vector3d d = c2 - c1;
            double len = d.Length;
            hits.Clear();
            if (len > 0 && double.IsFinite(len)) {
                var ray = new Ray3d(c1, d / len);
                tree.FindAllHitTriangles(ray, hits, len);
            }
            if (hits.Count == 0) { facets.Remove(key); return; }
            var f = new Facet { Cell = c, Stamp = dt.CellStamp(c), Face = i, Hits = hits.Count, R2 = -1 };
            Vector3d v0 = dt.GetVertex(fv.a);
            foreach (int t in hits) {
                var ray = new Ray3d(c1, d / len);
                Vector3d ta = Vector3d.Zero, tb = Vector3d.Zero, tc = Vector3d.Zero;
                S.GetTriVertices(t, ref ta, ref tb, ref tc);
                var intr = new IntrRay3Triangle3(ray, new Triangle3d(ta, tb, tc));
                Vector3d x = intr.Find() ? ray.PointAt(intr.RayParameter) : (c1 + c2) * 0.5;
                double r2 = x.DistanceSquared(v0);
                if (r2 > f.R2) { f.R2 = r2; f.Centre = x; f.SurfaceNormal = S.GetTriNormal(t); }
            }
            facets[key] = f;
            for (int k = 0; k < 3; ++k) {
                int v = k == 0 ? key.Item1 : k == 1 ? key.Item2 : key.Item3;
                if (!star.TryGetValue(v, out var set)) star[v] = set = new HashSet<(int, int, int)>();
                set.Add(key);
            }
            queue.Enqueue((0, key), (f.R2, key));
            queue.Enqueue((1, (key.Item1, -1, -1)), (f.R2, (key.Item1, -1, -1)));
            queue.Enqueue((1, (key.Item2, -1, -1)), (f.R2, (key.Item2, -1, -1)));
            queue.Enqueue((1, (key.Item3, -1, -1)), (f.R2, (key.Item3, -1, -1)));
        }

        bool get(in (int, int, int) key, out Facet f) => facets.TryGetValue(key, out f) && valid(f);

        // the violation of a restricted facet (0 none): 1 a corner vertex (R0), 2 two or more crossings (E'),
        // 3 ball larger than RMax (R2), 4 radius-edge above Rho (R3)
        int facet_violation(in (int, int, int) key, in Facet f)
        {
            if (key.Item1 < 8) return 1;
            if (f.Hits > 1) return 2;
            if (f.R2 > RMax * RMax) return 3;
            Vector3d a = dt.GetVertex(key.Item1), b = dt.GetVertex(key.Item2), c = dt.GetVertex(key.Item3);
            double lmin2 = Math.Min(a.DistanceSquared(b), Math.Min(b.DistanceSquared(c), c.DistanceSquared(a)));
            double R = circumradius(a, b, c);
            if (R * R > Rho * Rho * lmin2) return 4;
            return 0;
        }

        static double circumradius(Vector3d a, Vector3d b, Vector3d c)
        {
            double la = b.Distance(c), lb = a.Distance(c), lc = a.Distance(b);
            double area2 = (b - a).Cross(c - a).Length;      // twice the area
            return area2 > 0 ? la * lb * lc / (2 * area2) : double.PositiveInfinity;
        }

        // the umbrella of vertex v: its valid restricted facets form one cycle around it. Returns false if not; then
        // centre is the largest ball centre among them
        bool umbrella_ok(int v, out Vector3d centre, out double r2)
        {
            centre = Vector3d.Zero; r2 = -1;
            if (v < 8 || !star.TryGetValue(v, out var set)) return true;
            var link = new Dictionary<int, List<int>>();
            int nf = 0;
            var stale = new List<(int, int, int)>();
            foreach (var key in set) {
                if (!get(key, out Facet f)) { stale.Add(key); continue; }
                nf++;
                if (f.R2 > r2) { r2 = f.R2; centre = f.Centre; }
                int a, b;
                if (key.Item1 == v) { a = key.Item2; b = key.Item3; } else if (key.Item2 == v) { a = key.Item1; b = key.Item3; } else { a = key.Item1; b = key.Item2; }
                if (!link.TryGetValue(a, out var la)) link[a] = la = new List<int>();
                if (!link.TryGetValue(b, out var lb)) link[b] = lb = new List<int>();
                la.Add(b); lb.Add(a);
            }
            foreach (var k in stale) set.Remove(k);
            if (nf == 0) return true;
            if (nf < 3 || link.Values.Any(l => l.Count != 2)) return false;
            // one cycle: walk it
            int start = link.Keys.Min(), prev = -1, cur = start, steps = 0;
            do {
                var l = link[cur];
                int next = (l[0] != prev) ? l[0] : l[1];
                prev = cur; cur = next; steps++;
            } while (cur != start && steps <= link.Count);
            return steps == link.Count;
        }

        public bool Run(IEnumerable<Vector3d> initial)
        {
            var sw = Stopwatch.StartNew();
            var box = S.CachedBounds;
            double margin = Math.Max(box.Diagonal.Length, 8 * HMin);
            box.Expand(margin);
            dt = new Delaunay3(box);
            if (Seeded != null) dt.RefinementSupport = true;
            int hint = -1;
            foreach (var p in initial) {
                if (dt.Insert(p, hint, HMin, out int v) == Delaunay3.InsertResult.Inserted) Inserted++;
            }
            foreach (int c in dt.CellIndices().ToList())
                for (int i = 0; i < 4; ++i) {
                    int n = dt.Neighbour(c, i);
                    if (n < 0 || c < n) compute_facet(c, i);
                }
            Seeded?.Invoke();
            refine();
            for (Rounds = 0; Rounds < 50; ++Rounds) {
                BeforeRegionRound?.Invoke(Rounds);
                var swr = Stopwatch.StartNew();
                FarViolations = 0;
                var fix = region_violations(out int nViol);
                RegionSeconds += swr.Elapsed.TotalSeconds;
                RegionViolations = nViol;
                if (nViol == 0 && FarViolations == 0) break;
                int before = Inserted;
                foreach (var x in fix) {
                    removed.Clear(); added.Clear();
                    if (dt.Insert(x, hint, HMin, out int nv, removed, added) != Delaunay3.InsertResult.Inserted) { Refused++; continue; }
                    Inserted++;
                    AfterInsert?.Invoke(nv, removed, added);
                    foreach (int c in added)
                        if (dt.IsCell(c))
                            for (int i = 0; i < 4; ++i) compute_facet(c, i);
                }
                refine();
                if (Inserted == before) break;      // every fix refused by HMin
            }
            Seconds = sw.Elapsed.TotalSeconds;
            return queue.Count == 0;
        }

        public int Rounds, RegionViolations, FarViolations;
        public Delaunay3 Dt => dt;
        /// <summary> the proxy's verdict per sample (Delaunay vertex id) of the last region check: one disk, farthest input vertex within RMax </summary>
        public readonly Dictionary<int, (bool disk, bool farOk)> ProxyVerdict = new Dictionary<int, (bool, bool)>();
        /// <summary> called before each region round's check, with the round (measurement hook, item 3 of GEOGRAM_MEASUREMENTS_2026-11.md) </summary>
        public Action<int> BeforeRegionRound;
        /// <summary> the proxy check on the current state (fills ProxyVerdict) </summary>
        public int ProxyCheck() { var fix = region_violations(out int c); return c; }
        public double RegionSeconds;

        // proxy for (C'): every sample's restricted Voronoi region, approximated by the input vertices nearest to it,
        // must be one disk (connected through input edges, Euler characteristic 1 of the input triangles all of whose
        // vertices are labelled with it). Returns, per violating sample, its region's input vertex farthest from it
        List<Vector3d> region_violations(out int count)
        {
            int nv = dt.VertexCount;
            var pts = new List<(int id, Vector3d p)>();
            for (int v = 8; v < nv; ++v) pts.Add((v, dt.GetVertex(v)));
            double h = RMax;
            var grid = new Dictionary<Vector3i, List<int>>();
            Vector3i cell(Vector3d p) => new Vector3i((int)Math.Floor(p.x / h), (int)Math.Floor(p.y / h), (int)Math.Floor(p.z / h));
            for (int k = 0; k < pts.Count; ++k) {
                var c = cell(pts[k].p);
                if (!grid.TryGetValue(c, out var l)) grid[c] = l = new List<int>();
                l.Add(k);
            }
            int[] label = new int[S.MaxVertexID];
            foreach (int v in S.VertexIndices()) {
                Vector3d q = S.GetVertex(v);
                var c = cell(q);
                int best = -1; double bd = double.MaxValue;
                for (int ring = 1; ring <= 64 && (best < 0 || (ring - 1) * h < Math.Sqrt(bd)); ++ring)
                    for (int x = -ring; x <= ring; ++x) for (int y = -ring; y <= ring; ++y) for (int z = -ring; z <= ring; ++z) {
                        if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) != ring && ring > 1) continue;
                        if (!grid.TryGetValue(new Vector3i(c.x + x, c.y + y, c.z + z), out var l)) continue;
                        foreach (int k in l) { double d = pts[k].p.DistanceSquared(q); if (d < bd || (d == bd && k < best)) { bd = d; best = k; } }
                    }
                label[v] = best;
            }
            // per label: vertices, edges and triangles inside; components through inside edges
            var V = new Dictionary<int, int>(); var E = new Dictionary<int, int>(); var T = new Dictionary<int, int>();
            var parent = new int[S.MaxVertexID];
            foreach (int v in S.VertexIndices()) { parent[v] = v; V[label[v]] = V.GetValueOrDefault(label[v]) + 1; }
            int find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            foreach (int e in S.EdgeIndices()) {
                Index2i ev = S.GetEdgeV(e);
                if (label[ev.a] != label[ev.b]) continue;
                E[label[ev.a]] = E.GetValueOrDefault(label[ev.a]) + 1;
                parent[find(ev.a)] = find(ev.b);
            }
            foreach (int t in S.TriangleIndices()) {
                Index3i tv = S.GetTriangle(t);
                if (label[tv.a] == label[tv.b] && label[tv.b] == label[tv.c]) T[label[tv.a]] = T.GetValueOrDefault(label[tv.a]) + 1;
            }
            var roots = new Dictionary<int, HashSet<int>>();
            foreach (int v in S.VertexIndices()) {
                if (!roots.TryGetValue(label[v], out var r)) roots[label[v]] = r = new HashSet<int>();
                r.Add(find(v));
            }
            var fix = new List<Vector3d>();
            count = 0;
            ProxyVerdict.Clear();
            foreach (var kv in V) {
                int k = kv.Key;
                int chi = kv.Value - E.GetValueOrDefault(k) + T.GetValueOrDefault(k);
                Vector3d p = pts[k].p, far = p; double fd = -1;
                foreach (int v in S.VertexIndices())
                    if (label[v] == k) { double d = S.GetVertex(v).DistanceSquared(p); if (d > fd) { fd = d; far = S.GetVertex(v); } }
                bool disk = roots[k].Count == 1 && chi == 1;
                ProxyVerdict[pts[k].id] = (disk, fd <= RMax * RMax);
                // R2 for S -> D: the region's farthest point (here: input vertex) within RMax of its sample
                if (disk && fd <= RMax * RMax) continue;
                if (disk) FarViolations++; else count++;
                fix.Add(far);
            }
            return fix;
        }

        void refine()
        {
            int hint = -1;
            while (queue.Count > 0 && Inserted < MaxInsertions) {
                queue.TryDequeue(out var item, out var prio);
                Vector3d x;
                int stampKey;
                if (item.kind == 0) {
                    if (!get(item.key, out Facet f) || facet_violation(item.key, f) == 0) continue;
                    x = f.Centre; stampKey = f.Stamp;
                } else {
                    int v = item.key.Item1;
                    if (umbrella_ok(v, out x, out double r2)) continue;
                    stampKey = (int)(BitConverter.DoubleToInt64Bits(r2) & 0x7fffffff);
                }
                if (refused.Contains((item.kind, item.key, stampKey))) continue;
                removed.Clear(); added.Clear();
                var res = dt.Insert(x, hint, HMin, out int nv, removed, added);
                if (res != Delaunay3.InsertResult.Inserted) {
                    refused.Add((item.kind, item.key, stampKey));
                    if (res == Delaunay3.InsertResult.TooClose) Refused++; else Failed++;
                    continue;
                }
                Inserted++;
                AfterInsert?.Invoke(nv, removed, added);
                foreach (int c in added)
                    if (dt.IsCell(c))
                        for (int i = 0; i < 4; ++i) compute_facet(c, i);
            }
        }

        public struct Report
        {
            public int Restricted, Corner, MultiHit, TooLarge, BadRatio, BadUmbrella, Samples;
        }

        // the remaining violations (all refused by HMin or a failed insertion)
        public Report Check()
        {
            var r = new Report();
            var verts = new HashSet<int>();
            foreach (var kv in facets) {
                if (!valid(kv.Value)) continue;
                r.Restricted++;
                verts.Add(kv.Key.Item1); verts.Add(kv.Key.Item2); verts.Add(kv.Key.Item3);
                switch (facet_violation(kv.Key, kv.Value)) {
                    case 1: r.Corner++; break;
                    case 2: r.MultiHit++; break;
                    case 3: r.TooLarge++; break;
                    case 4: r.BadRatio++; break;
                }
            }
            foreach (int v in verts)
                if (!umbrella_ok(v, out _, out _)) r.BadUmbrella++;
            r.Samples = verts.Count;
            return r;
        }

        /// <summary> the restricted facets as a mesh, oriented by the input's normal at their ball centres; failed appends counted </summary>
        public DMesh3 Output(out int appendFailed)
        {
            var m = new DMesh3();
            var map = new Dictionary<int, int>();
            appendFailed = 0;
            int vid(int v) { if (!map.TryGetValue(v, out int id)) map[v] = id = m.AppendVertex(dt.GetVertex(v)); return id; }
            foreach (var kv in facets.OrderBy(k => k.Key)) {
                if (!valid(kv.Value)) continue;
                var k = kv.Key;
                Vector3d a = dt.GetVertex(k.Item1), b = dt.GetVertex(k.Item2), c = dt.GetVertex(k.Item3);
                bool flip = (b - a).Cross(c - a).Dot(kv.Value.SurfaceNormal) < 0;
                int ta = vid(k.Item1), tb = vid(k.Item2), tc = vid(k.Item3);
                int t = flip ? m.AppendTriangle(ta, tc, tb) : m.AppendTriangle(ta, tb, tc);
                if (t < 0) appendFailed++;
            }
            return m;
        }
    }
}
