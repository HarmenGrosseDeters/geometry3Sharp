// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Measurement for docs/research/GEOGRAM_MEASUREMENTS_2026-11.md, item 3: true restricted Voronoi cells (RVCs) of the samples
// on S, in floating point, against the (C') / R2 proxy of Rdr.region_violations (input vertices labelled by their nearest
// sample). Written from Yan, Lévy, Liu, Sun, Wang, "Isotropic remeshing with fast and exact computation of restricted Voronoi
// diagram", SGP 2009 (T14 of CERTIFIED_REMESHING.md): each input triangle is clipped by the bisector planes of a sample and
// its Delaunay neighbours (nearest first, stopping by the security radius: a neighbour farther than twice the largest distance
// of the polygon's vertices from the sample cannot cut it), and the samples whose cells meet the triangle are found by
// propagation over the bisector edges of the clipped polygons. Not a certificate (floating point, no symbolic perturbation).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

namespace RestrictedDelaunaySpike
{
    public sealed class RvcCheck
    {
        /// <summary> per sample whose cell meets S: one disk (connected, Euler characteristic 1), and the farthest point of the cell </summary>
        public sealed class Cell
        {
            public int Pieces, Components, Chi;
            public double Far, Area;
            public bool Disk => Components == 1 && Chi == 1;
        }

        public readonly Dictionary<int, Cell> Cells = new Dictionary<int, Cell>();
        /// <summary> per sample: the input triangles its cell meets (RvcIncremental's seed) </summary>
        public readonly Dictionary<int, List<int>> TriLists = new Dictionary<int, List<int>>();
        public Dictionary<int, List<int>> ComputeTriangleLists() { Compute(); return TriLists; }
        public double Seconds;
        public long Clips, ClipsSkippedBySecurityRadius;

        // a polygon vertex and the label of the edge from it to the next vertex: >= 0 a sample (bisector of the cell's
        // sample and it), < 0 the input edge -1 - label
        struct PV { public Vector3d P; public int Label; }

        readonly DMesh3 S;
        readonly Delaunay3 dt;
        readonly List<int>[] nbr;

        public RvcCheck(DMesh3 s, Delaunay3 dt)
        {
            S = s; this.dt = dt;
            int nv = dt.VertexCount;
            var sets = new HashSet<int>[nv];
            for (int v = 0; v < nv; ++v) sets[v] = new HashSet<int>();
            foreach (int c in dt.CellIndices()) {
                Index4i t = dt.GetCell(c);
                for (int a = 0; a < 4; ++a) for (int b = a + 1; b < 4; ++b) { sets[t[a]].Add(t[b]); sets[t[b]].Add(t[a]); }
            }
            nbr = new List<int>[nv];
            for (int v = 0; v < nv; ++v) {
                Vector3d p = dt.GetVertex(v);
                nbr[v] = sets[v].OrderBy(u => dt.GetVertex(u).DistanceSquared(p)).ThenBy(u => u).ToList();
            }
        }

        int nearest(Vector3d q, ref int hint)
        {
            int c = dt.Locate(q, hint);
            hint = c;
            Index4i t = dt.GetCell(c);
            int best = t.a; double bd = dt.GetVertex(best).DistanceSquared(q);
            for (int k = 1; k < 4; ++k) { double d = dt.GetVertex(t[k]).DistanceSquared(q); if (d < bd) { bd = d; best = t[k]; } }
            // greedy walk on the Delaunay graph ends at the nearest vertex
            for (bool moved = true; moved;) {
                moved = false;
                foreach (int u in nbr[best]) { double d = dt.GetVertex(u).DistanceSquared(q); if (d < bd) { bd = d; best = u; moved = true; } }
            }
            return best;
        }

        // clip the polygon by the half-space closer to sample i than to sample j; returns false if nothing is cut
        static bool clip(List<PV> poly, List<PV> tmp, Vector3d pi, Vector3d pj, int j)
        {
            Vector3d n = pj - pi, m = (pi + pj) * 0.5;
            int count = poly.Count;
            bool anyOut = false;
            var s = new double[count];
            for (int k = 0; k < count; ++k) { s[k] = (poly[k].P - m).Dot(n); if (s[k] > 0) anyOut = true; }
            if (!anyOut) return false;
            tmp.Clear();
            for (int k = 0; k < count; ++k) {
                int k1 = (k + 1) % count;
                bool inK = s[k] <= 0, inK1 = s[k1] <= 0;
                if (inK) tmp.Add(poly[k]);
                if (inK != inK1) {
                    double t = s[k] / (s[k] - s[k1]);
                    Vector3d x = poly[k].P + t * (poly[k1].P - poly[k].P);
                    // entering the cell: the new vertex continues the old edge; leaving it: the new edge lies on the bisector
                    if (inK) tmp.Add(new PV { P = x, Label = j });
                    else tmp.Add(new PV { P = x, Label = poly[k].Label });
                }
            }
            poly.Clear(); poly.AddRange(tmp);
            return true;
        }

        public void Compute()
        {
            var sw = Stopwatch.StartNew();
            // pieces per sample: vertex keys, edge keys of the input-edge segments, bisector edge count, union-find over pieces
            var vkeys = new Dictionary<int, HashSet<(long, long, long)>>();
            var ekeys = new Dictionary<int, Dictionary<((long, long, long), (long, long, long)), int>>();   // edge key -> first piece
            var bisectorEdges = new Dictionary<int, int>();
            var pieceParent = new Dictionary<int, List<int>>();       // per sample: union-find parents of its pieces
            var poly = new List<PV>(); var tmp = new List<PV>();
            var queue = new Queue<int>(); var seen = new HashSet<int>();
            int hint = -1;
            foreach (int t in S.TriangleIndices()) {
                Index3i tv = S.GetTriangle(t);
                Index3i te = S.GetTriEdges(t);   // te[k] is the edge (tv[k], tv[k+1])
                Vector3d centroid = (S.GetVertex(tv.a) + S.GetVertex(tv.b) + S.GetVertex(tv.c)) / 3;
                int start = nearest(centroid, ref hint);
                queue.Clear(); seen.Clear();
                queue.Enqueue(start); seen.Add(start);
                while (queue.Count > 0) {
                    int i = queue.Dequeue();
                    Vector3d pi = dt.GetVertex(i);
                    poly.Clear();
                    for (int k = 0; k < 3; ++k) poly.Add(new PV { P = S.GetVertex(tv[k]), Label = -1 - te[k] });
                    var ni = nbr[i];
                    for (int x = 0; x < ni.Count; ++x) {
                        int j = ni[x];
                        double R2 = 0;
                        foreach (var q in poly) R2 = Math.Max(R2, q.P.DistanceSquared(pi));
                        if (dt.GetVertex(j).DistanceSquared(pi) > 4 * R2) { ClipsSkippedBySecurityRadius += ni.Count - x; break; }
                        ++Clips;
                        clip(poly, tmp, pi, dt.GetVertex(j), j);
                        if (poly.Count < 3) break;
                    }
                    if (poly.Count < 3) continue;
                    double area = 0;
                    for (int k = 1; k + 1 < poly.Count; ++k) area += 0.5 * (poly[k].P - poly[0].P).Cross(poly[k + 1].P - poly[0].P).Length;
                    if (!(area > 0)) continue;
                    // propagate to the samples across the bisector edges
                    foreach (var q in poly) if (q.Label >= 0 && seen.Add(q.Label)) queue.Enqueue(q.Label);
                    record(i, t, tv, poly, area, vkeys, ekeys, bisectorEdges, pieceParent);
                }
            }
            foreach (var kv in Cells) {
                int i = kv.Key; Cell c = kv.Value;
                c.Chi = vkeys[i].Count - (ekeys[i].Count + bisectorEdges.GetValueOrDefault(i)) + c.Pieces;
                var par = pieceParent[i];
                int roots = 0;
                for (int k = 0; k < par.Count; ++k) if (find(par, k) == k) roots++;
                c.Components = roots;
            }
            Seconds = sw.Elapsed.TotalSeconds;
        }

        static int find(List<int> par, int k) { while (par[k] != k) k = par[k] = par[par[k]]; return k; }

        void record(int i, int t, Index3i tv, List<PV> poly, double area, Dictionary<int, HashSet<(long, long, long)>> vkeys,
            Dictionary<int, Dictionary<((long, long, long), (long, long, long)), int>> ekeys, Dictionary<int, int> bisectorEdges,
            Dictionary<int, List<int>> pieceParent)
        {
            if (!Cells.TryGetValue(i, out Cell cell)) {
                Cells[i] = cell = new Cell();
                vkeys[i] = new HashSet<(long, long, long)>();
                ekeys[i] = new Dictionary<((long, long, long), (long, long, long)), int>();
                pieceParent[i] = new List<int>();
            }
            int piece = cell.Pieces++;
            if (!TriLists.TryGetValue(i, out var tl)) TriLists[i] = tl = new List<int>();
            tl.Add(t);
            var par = pieceParent[i];
            par.Add(piece);
            cell.Area += area;
            Vector3d pi = dt.GetVertex(i);
            int n = poly.Count;
            var keys = new (long, long, long)[n];
            for (int k = 0; k < n; ++k) {
                int lin = poly[(k + n - 1) % n].Label, lout = poly[k].Label;   // the edges into and out of vertex k
                (long, long, long) key;
                if (lin < 0 && lout < 0) {
                    // an input vertex: the common vertex of the two input edges
                    Index2i a = S.GetEdgeV(-1 - lin), b = S.GetEdgeV(-1 - lout);
                    int v = (a.a == b.a || a.a == b.b) ? a.a : a.b;
                    key = (0, v, 0);
                } else if (lin < 0 || lout < 0) {
                    int e = lin < 0 ? -1 - lin : -1 - lout, j = lin < 0 ? lout : lin;
                    key = (1, e, j);
                } else {
                    int j = Math.Min(lin, lout), l = Math.Max(lin, lout);
                    key = (2 + (long)t, j, l);
                }
                keys[k] = key;
                vkeys[i].Add(key);
                cell.Far = Math.Max(cell.Far, poly[k].P.Distance(pi));
            }
            for (int k = 0; k < n; ++k) {
                if (poly[k].Label >= 0) { bisectorEdges[i] = bisectorEdges.GetValueOrDefault(i) + 1; continue; }
                var a = keys[k]; var b = keys[(k + 1) % n];
                var ek = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                if (ekeys[i].TryGetValue(ek, out int other)) { int ra = find(par, piece), rb = find(par, other); if (ra != rb) par[ra] = rb; }
                else ekeys[i][ek] = piece;
            }
        }
    }
}
