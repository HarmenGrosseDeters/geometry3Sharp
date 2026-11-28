// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Upper-bound probe of stage 1b (docs/design/CERTIFIED_REMESHING.md 14.4 deviation, 14.8): the restricted Voronoi cells kept
// up to date during the refinement, in floating point. After each accepted insertion only the cells of the cavity's vertices
// (the vertices of Delaunay3's added cells) are recomputed: an old site's new cell is inside its old one, and the new site's
// cell inside the union of its new neighbours' old cells, so the candidate triangles are those lists. The clipping is
// RvcCheck's (Yan, Lévy et al. 2009, T14), with the polygon vertices tagged by the kind of point the exact clipping would
// test: 1 a mesh vertex (BisectorSide1), 2 a mesh edge on a bisector (BisectorSide2), 3 a Voronoi edge in the plane of a
// triangle (BisectorSide3, prepared once per point). The counts of the side tests and of the R2 tests at the final polygon
// vertices (DistanceAbove1-3), and a sample of their arguments, give the exact cost on top (Program.Probe). Not a certificate.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

namespace RestrictedDelaunaySpike
{
    public sealed class RvcIncremental
    {
        public sealed class Cell { public int Pieces, Components, Chi; public double Far; public bool Disk => Components == 1 && Chi == 1; }

        struct PV { public Vector3d P; public int Label, Kind, A, B; }   // Label: the edge to the next vertex (>= 0 a site, < 0 the input edge -1 - label)

        readonly DMesh3 S;
        readonly Delaunay3 dt;
        readonly double rmax;
        List<int>[] tris = new List<int>[0];
        public readonly Dictionary<int, Cell> Cells = new Dictionary<int, Cell>();

        public long Side1, Side2, Side3, Prepare3, Dist1, Dist2, Dist3, Recomputes, Insertions, CandidateTris, Clips;
        public double SeedSeconds, UpdateSeconds;
        public long[] SideByKind => new[] { Side1, Side2, Side3 };

        // sampled arguments of the exact calls (every 8th call, up to Cap per kind)
        public const int Cap = 200000;
        public readonly List<(Vector3d v, Vector3d a, Vector3d e)> S1 = new();
        public readonly List<(Vector3d q0, Vector3d q1, Vector3d a, Vector3d b, Vector3d c)> S2 = new();
        public readonly List<(Vector3d a, Vector3d b, Vector3d c, Vector3d t0, Vector3d t1, Vector3d t2, Vector3d e)> S3 = new();
        public readonly List<(Vector3d v, Vector3d a)> D1 = new();
        public readonly List<(Vector3d q0, Vector3d q1, Vector3d a, Vector3d b)> D2 = new();
        public readonly List<(Vector3d a, Vector3d b, Vector3d c, Vector3d t0, Vector3d t1, Vector3d t2)> D3 = new();

        public RvcIncremental(DMesh3 s, Delaunay3 dt, double rmax) { S = s; this.dt = dt; this.rmax = rmax; }

        /// <summary> candidate A: keep the clipped polygons per site; an old site's cell is cut by the new bisector only
        /// (V_new(j) = V_old(j) ∩ H(j, p)), and the new site's cell is clipped on the triangles that were cut </summary>
        public bool Cut;
        readonly Dictionary<int, List<(int t, List<PV> poly)>> pieces = new Dictionary<int, List<(int, List<PV>)>>();
        public long CutTests, CutPieces;
        /// <summary> candidate A': the (C') summaries of the cut cells only at Flush (once per region round), not per insertion </summary>
        public bool Lazy, Prune, PruneByTriangle;
        public long Pruned;
        readonly HashSet<int> dirty = new HashSet<int>();
        public double FlushSeconds;
        public void Flush()
        {
            var sw = Stopwatch.StartNew();
            foreach (int j in dirty) if (pieces.ContainsKey(j)) summary(j); else Cells.Remove(j);
            dirty.Clear();
            FlushSeconds += sw.Elapsed.TotalSeconds;
        }

        List<int> trisOf(int v)
        {
            if (v >= tris.Length) Array.Resize(ref tris, Math.Max(2 * tris.Length, v + 16));
            return tris[v] ??= new List<int>();
        }

        readonly List<int> cellsTmp = new List<int>();
        readonly HashSet<int> nset = new HashSet<int>();
        readonly List<int> nbr = new List<int>();

        void neighbours(int i)
        {
            dt.IncidentCells(i, cellsTmp);
            nset.Clear(); nbr.Clear();
            foreach (int c in cellsTmp) for (int k = 0; k < 4; ++k) { int u = dt.CellVertex(c, k); if (u != i && nset.Add(u)) nbr.Add(u); }
            Vector3d p = dt.GetVertex(i);
            nbr.Sort((x, y) => { int r = dt.GetVertex(x).DistanceSquared(p).CompareTo(dt.GetVertex(y).DistanceSquared(p)); return r != 0 ? r : x.CompareTo(y); });
        }

        /// <summary> the full computation on the current triangulation (by propagation over the bisector edges, as RvcCheck) </summary>
        public void Seed()
        {
            var sw = Stopwatch.StartNew();
            var full = new RvcCheckLists(S, dt);
            var lists = full.TrianglesPerSite();
            foreach (var kv in lists) trisOf(kv.Key).AddRange(kv.Value);
            foreach (var kv in lists) recompute(kv.Key, kv.Value);
            SeedSeconds = sw.Elapsed.TotalSeconds;
        }

        readonly HashSet<int> affected = new HashSet<int>();
        readonly HashSet<int> union = new HashSet<int>();
        readonly List<int> cand = new List<int>();

        public void Update(int nv, List<int> removed, List<int> added)
        {
            var sw = Stopwatch.StartNew();
            Insertions++;
            affected.Clear();
            foreach (int c in added)
                if (dt.IsCell(c)) for (int k = 0; k < 4; ++k) affected.Add(dt.CellVertex(c, k));
            union.Clear();
            if (Cut) { update_cut(nv); UpdateSeconds += sw.Elapsed.TotalSeconds; return; }
            foreach (int j in affected) if (j != nv) foreach (int t in trisOf(j)) union.Add(t);
            foreach (int j in affected) {
                if (j == nv) continue;
                cand.Clear(); cand.AddRange(trisOf(j));
                recompute(j, cand);
            }
            cand.Clear(); cand.AddRange(union); cand.Sort();
            recompute(nv, cand);
            UpdateSeconds += sw.Elapsed.TotalSeconds;
        }

        public double CutSeconds, NewCellSeconds;
        void update_cut(int nv)
        {
            var swc = Stopwatch.StartNew();
            foreach (int j in affected) {
                if (j == nv || !pieces.TryGetValue(j, out var list)) continue;
                bool changed = false;
                for (int k = list.Count - 1; k >= 0; --k) {
                    var (t, pl) = list[k];
                    curT = t;
                    if (Prune) {
                        // security radius: bisector(j, nv) cuts nothing if |nv - j| > 2 max |x - j| over the piece (margin for rounding)
                        double R2 = 0; Vector3d pj = dt.GetVertex(j);
                        if (PruneByTriangle) { Index3i tv = S.GetTriangle(t); for (int a = 0; a < 3; ++a) R2 = Math.Max(R2, S.GetVertex(tv[a]).DistanceSquared(pj)); }
                        else foreach (var q in pl) R2 = Math.Max(R2, q.P.DistanceSquared(pj));
                        if (dt.GetVertex(nv).DistanceSquared(pj) > 4 * R2 * (1 + 1e-9)) { Pruned++; continue; }
                    }
                    poly.Clear(); poly.AddRange(pl);
                    int before = poly.Count;
                    CutPieces++; CutTests += before;
                    if (!clip(j, nv)) continue;
                    changed = true;
                    union.Add(t);
                    // R2 at the new vertices only
                    foreach (var q in poly) if (q.Kind != 1 && q.B == nv) count_dist(q, j, t);
                    if (poly.Count < 3) list.RemoveAt(k);
                    else list[k] = (t, new List<PV>(poly));
                }
                if (changed) { if (Lazy) dirty.Add(j); else summary(j); }
            }
            CutSeconds += swc.Elapsed.TotalSeconds; swc.Restart();
            cand.Clear(); cand.AddRange(union); cand.Sort();
            recompute(nv, cand);
            NewCellSeconds += swc.Elapsed.TotalSeconds;
        }

        void count_dist(in PV q, int i, int t)
        {
            Vector3d pi = dt.GetVertex(i);
            Index3i tv = S.GetTriangle(t);
            if (q.Kind == 1) { if (Dist1++ % 8 == 0 && D1.Count < Cap) D1.Add((q.P, pi)); }
            else if (q.Kind == 2) { if (Dist2++ % 8 == 0 && D2.Count < Cap) { Index2i ev = S.GetEdgeV(q.A); D2.Add((S.GetVertex(ev.a), S.GetVertex(ev.b), pi, dt.GetVertex(q.B))); } }
            else { if (Dist3++ % 8 == 0 && D3.Count < Cap) D3.Add((pi, dt.GetVertex(q.A), dt.GetVertex(q.B), S.GetVertex(tv.a), S.GetVertex(tv.b), S.GetVertex(tv.c))); }
        }

        // the (C') summary of site i from its stored pieces
        void summary(int i)
        {
            var list = pieces[i];
            trisOf(i).Clear();
            if (list.Count == 0) { Cells.Remove(i); pieces.Remove(i); return; }
            var cell = new Cell();
            vkeys.Clear(); ekeys.Clear(); par.Clear();
            int bisectorEdges = 0; double far2 = 0;
            Vector3d pi = dt.GetVertex(i);
            foreach (var (t, pl) in list) {
                trisOf(i).Add(t);
                int piece = cell.Pieces++;
                par.Add(piece);
                bisectorEdges += add_keys(pl, t, piece, pi, ref far2);
            }
            finish(i, cell, bisectorEdges, far2);
        }

        int add_keys(List<PV> pl, int t, int piece, Vector3d pi, ref double far2)
        {
            int n = pl.Count, be = 0;
            Span<(long, long, long)> keys = n <= 64 ? stackalloc (long, long, long)[n] : new (long, long, long)[n];
            for (int k = 0; k < n; ++k) {
                var q = pl[k];
                far2 = Math.Max(far2, q.P.DistanceSquared(pi));
                keys[k] = q.Kind == 1 ? (0L, (long)q.A, 0L) : q.Kind == 2 ? (1L, (long)q.A, (long)q.B) : (2L + t, Math.Min(q.A, q.B), Math.Max(q.A, q.B));
                vkeys[keys[k]] = 1;
            }
            for (int k = 0; k < n; ++k) {
                if (pl[k].Label >= 0) { be++; continue; }
                var a = keys[k]; var b = keys[(k + 1) % n];
                var ek = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                if (ekeys.TryGetValue(ek, out int other)) { int ra = find(par, piece), rb = find(par, other); if (ra != rb) par[ra] = rb; }
                else ekeys[ek] = piece;
            }
            return be;
        }

        void finish(int i, Cell cell, int bisectorEdges, double far2)
        {
            cell.Chi = vkeys.Count - (ekeys.Count + bisectorEdges) + cell.Pieces;
            int roots = 0;
            for (int k = 0; k < par.Count; ++k) if (find(par, k) == k) roots++;
            cell.Components = roots;
            cell.Far = Math.Sqrt(far2);
            Cells[i] = cell;
        }

        readonly List<PV> poly = new List<PV>(), tmp = new List<PV>();
        readonly List<double> sd = new List<double>();
        readonly Dictionary<(long, long, long), int> vkeys = new Dictionary<(long, long, long), int>();
        readonly Dictionary<((long, long, long), (long, long, long)), int> ekeys = new Dictionary<((long, long, long), (long, long, long)), int>();
        readonly List<int> par = new List<int>();

        void count_side(in PV q, int i, int e)
        {
            Vector3d pi = dt.GetVertex(i), pe = dt.GetVertex(e);
            if (q.Kind == 1) { if (Side1++ % 8 == 0 && S1.Count < Cap) S1.Add((q.P, pi, pe)); }
            else if (q.Kind == 2) {
                if (Side2++ % 8 == 0 && S2.Count < Cap) { Index2i ev = S.GetEdgeV(q.A); S2.Add((S.GetVertex(ev.a), S.GetVertex(ev.b), pi, dt.GetVertex(q.B), pe)); }
            } else {
                if (Side3++ % 8 == 0 && S3.Count < Cap) { Index3i tv = S.GetTriangle(curT); S3.Add((pi, dt.GetVertex(q.A), dt.GetVertex(q.B), S.GetVertex(tv.a), S.GetVertex(tv.b), S.GetVertex(tv.c), pe)); }
            }
        }

        int curT;

        // clip by the half-space closer to site i than to j; false if nothing is cut
        bool clip(int i, int j)
        {
            Vector3d pi = dt.GetVertex(i), pj = dt.GetVertex(j);
            Vector3d n = pj - pi, m = (pi + pj) * 0.5;
            int count = poly.Count;
            bool anyOut = false;
            sd.Clear();
            for (int k = 0; k < count; ++k) { count_side(poly[k], i, j); double s = (poly[k].P - m).Dot(n); sd.Add(s); if (s > 0) anyOut = true; }
            if (!anyOut) return false;
            tmp.Clear();
            for (int k = 0; k < count; ++k) {
                int k1 = (k + 1) % count;
                bool inK = sd[k] <= 0, inK1 = sd[k1] <= 0;
                if (inK) tmp.Add(poly[k]);
                if (inK != inK1) {
                    double t = sd[k] / (sd[k] - sd[k1]);
                    Vector3d x = poly[k].P + t * (poly[k1].P - poly[k].P);
                    int lbl = poly[k].Label;
                    var nvx = lbl < 0 ? new PV { P = x, Kind = 2, A = -1 - lbl, B = j } : new PV { P = x, Kind = 3, A = lbl, B = j };
                    if (nvx.Kind == 3) Prepare3++;
                    nvx.Label = inK ? j : lbl;
                    tmp.Add(nvx);
                }
            }
            poly.Clear(); poly.AddRange(tmp);
            return true;
        }

        static int find(List<int> par, int k) { while (par[k] != k) k = par[k] = par[par[k]]; return k; }

        void recompute(int i, List<int> candidates)
        {
            Recomputes++;
            CandidateTris += candidates.Count;
            neighbours(i);
            Vector3d pi = dt.GetVertex(i);
            var keep = new List<int>();
            var cell = new Cell();
            vkeys.Clear(); ekeys.Clear(); par.Clear();
            int bisectorEdges = 0;
            double far2 = 0;
            foreach (int t in candidates) {
                curT = t;
                Index3i tv = S.GetTriangle(t), te = S.GetTriEdges(t);
                poly.Clear();
                for (int k = 0; k < 3; ++k) poly.Add(new PV { P = S.GetVertex(tv[k]), Label = -1 - te[k], Kind = 1, A = tv[k] });
                for (int x = 0; x < nbr.Count; ++x) {
                    int j = nbr[x];
                    double R2 = 0;
                    foreach (var q in poly) R2 = Math.Max(R2, q.P.DistanceSquared(pi));
                    if (dt.GetVertex(j).DistanceSquared(pi) > 4 * R2) break;
                    ++Clips;
                    clip(i, j);
                    if (poly.Count < 3) break;
                }
                if (poly.Count < 3) continue;
                keep.Add(t);
                if (Cut) {
                    if (!pieces.TryGetValue(i, out var pl)) pieces[i] = pl = new List<(int, List<PV>)>();
                    if (keep.Count == 1) pl.Clear();
                    pl.Add((t, new List<PV>(poly)));
                }
                int piece = cell.Pieces++;
                par.Add(piece);
                int n = poly.Count;
                Span<(long, long, long)> keys = n <= 64 ? stackalloc (long, long, long)[n] : new (long, long, long)[n];
                for (int k = 0; k < n; ++k) {
                    var q = poly[k];
                    // R2 at the polygon vertex
                    if (q.Kind == 1) { if (Dist1++ % 8 == 0 && D1.Count < Cap) D1.Add((q.P, pi)); }
                    else if (q.Kind == 2) { if (Dist2++ % 8 == 0 && D2.Count < Cap) { Index2i ev = S.GetEdgeV(q.A); D2.Add((S.GetVertex(ev.a), S.GetVertex(ev.b), pi, dt.GetVertex(q.B))); } }
                    else { if (Dist3++ % 8 == 0 && D3.Count < Cap) D3.Add((pi, dt.GetVertex(q.A), dt.GetVertex(q.B), S.GetVertex(tv.a), S.GetVertex(tv.b), S.GetVertex(tv.c))); }
                    far2 = Math.Max(far2, q.P.DistanceSquared(pi));
                    keys[k] = q.Kind == 1 ? (0L, (long)q.A, 0L) : q.Kind == 2 ? (1L, (long)q.A, (long)q.B) : (2L + t, Math.Min(q.A, q.B), Math.Max(q.A, q.B));
                    vkeys[keys[k]] = 1;
                }
                for (int k = 0; k < n; ++k) {
                    if (poly[k].Label >= 0) { bisectorEdges++; continue; }
                    var a = keys[k]; var b = keys[(k + 1) % n];
                    var ek = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                    if (ekeys.TryGetValue(ek, out int other)) { int ra = find(par, piece), rb = find(par, other); if (ra != rb) par[ra] = rb; }
                    else ekeys[ek] = piece;
                }
            }
            trisOf(i).Clear(); trisOf(i).AddRange(keep);
            if (cell.Pieces == 0) { Cells.Remove(i); pieces.Remove(i); return; }
            cell.Chi = vkeys.Count - (ekeys.Count + bisectorEdges) + cell.Pieces;
            int roots = 0;
            for (int k = 0; k < par.Count; ++k) if (find(par, k) == k) roots++;
            cell.Components = roots;
            cell.Far = Math.Sqrt(far2);
            Cells[i] = cell;
        }
    }

    /// <summary> RvcCheck's propagation, returning the triangles per site (for the seed) </summary>
    sealed class RvcCheckLists
    {
        readonly DMesh3 S; readonly Delaunay3 dt;
        public RvcCheckLists(DMesh3 s, Delaunay3 dt) { S = s; this.dt = dt; }
        public Dictionary<int, List<int>> TrianglesPerSite()
        {
            var rc = new RvcCheck(S, dt);
            return rc.ComputeTriangleLists();
        }
    }
}
