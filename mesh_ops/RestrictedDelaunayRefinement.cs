// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Certified restricted Delaunay refinement of a clean closed mesh S, stage 1b (docs/design/CERTIFIED_REMESHING.md sections 4,
    /// 14 and 14.9). Internal; the public API comes later. Delaunay3 with SymbolicPerturbation; the restricted Voronoi diagram
    /// by exact clipping (ExactRestrictedVoronoi); the rules, worst first by a floating-point key:
    ///   R0 a box corner's cell meets S; R1 the topological ball property: (E') a Voronoi edge meets S more than once, (F') a
    ///   Voronoi facet meets S in something other than one arc, (C') a cell meets S in something other than one disk;
    ///   R2 a surface Delaunay ball or a restricted cell with a point farther than r_max (DistanceAbove1-3); R3 a restricted
    ///   facet with circumradius / shortest edge above rho (RadiusEdgeAbove).
    /// An insertion closer than h_min to a sample is refused and the element is not retried until it changes. The output D is
    /// the set of restricted facets; Certified means (TBP), R0 and R2 hold at the end: D is then homeomorphic to S (Theorem A)
    /// and within r_max of it both ways (Theorem D). The facets that R3 could not fix are counted as exempt (Theorem E).
    ///
    /// Sources: J.-D. Boissonnat, S. Oudot, "Provably good sampling and meshing of surfaces", Graphical Models 67, 2005;
    /// S.-W. Cheng, T. K. Dey, E. A. Ramos, T. Ray, "Sampling and meshing a surface with guaranteed topology and geometry",
    /// SoCG 2004; T. K. Dey, J. A. Levine, "Delaunay meshing of piecewise smooth complexes without expensive predicates",
    /// Algorithms 2(4), 2009; H. Edelsbrunner, N. Shah, "Triangulating topological spaces", IJCGA 7, 1997 (papers only).
    /// </summary>
    internal sealed class RestrictedDelaunayRefinement
    {
        internal enum ResultStatus { NotComputed, Certified, Uncertified, InvalidInput, LimitReached }

        /// <summary> the largest surface Delaunay ball radius and band width (required, &gt; 0) </summary>
        internal double RMax;
        /// <summary> the radius-edge bound (default 1: angles of at least 30 degrees) </summary>
        internal double Rho = 1;
        /// <summary> the separation floor (0: r_max / 16) </summary>
        internal double HMin;
        /// <summary> the largest number of samples (the result is LimitReached beyond) </summary>
        internal int MaxSamples = 1 << 20;
        /// <summary> tests: after every accepted insertion, compare the updated cells with a full exact recompute (slow) </summary>
        internal bool CheckEachInsertion;
        /// <summary> tests: only the facet rules (R0, (E'), R2 of the balls, R3), no rounds of the cell rules </summary>
        internal bool FacetRulesOnly;

        internal ResultStatus Status;
        internal DMesh3 Output;
        internal ExactRestrictedVoronoi Diagram;
        internal int Inserted, Refused, CheckedInsertions, CheckMismatches;
        /// <summary> the violations left at the end </summary>
        internal int BadEdges, BadArcs, BadCells, CornerCells, FarBalls, FarCells, ExemptFacets, RestrictedFacets;

        Delaunay3 dt;
        ExactRestrictedVoronoi rvd;
        double hmin;
        readonly List<int> removed = new List<int>(), added = new List<int>();
        readonly Heap facetQueue = new Heap();
        Dictionary<long, int> facetVersion = new Dictionary<long, int>();
        Dictionary<long, int> refusedFacet = new Dictionary<long, int>();
        int[] siteVersion = new int[0], refusedSite = new int[0];
        int hint = -1;

        /// <summary> refines the closed, manifold mesh S (the caller guarantees it is clean: MeshRobustRepair.NotCleanReason empty) </summary>
        internal void Compute(DMesh3 S)
        {
            Status = ResultStatus.NotComputed; Output = null; Diagram = null;
            Inserted = Refused = CheckedInsertions = CheckMismatches = 0;
            BadEdges = BadArcs = BadCells = CornerCells = FarBalls = FarCells = ExemptFacets = RestrictedFacets = 0;
            facetQueue.Clear(); facetVersion = new Dictionary<long, int>(); refusedFacet = new Dictionary<long, int>();
            siteVersion = new int[0]; refusedSite = new int[0]; hint = -1;
            if (!(RMax > 0) || !(Rho > 0) || S.TriangleCount == 0 || !closed_manifold(S)) { Status = ResultStatus.InvalidInput; return; }
            hmin = HMin > 0 ? HMin : RMax / 16;

            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            foreach (int v in S.VertexIndices()) box.Contain(S.GetVertex(v));
            double margin = box.Diagonal.Length;
            if (!(margin >= 8 * hmin)) margin = 8 * hmin;
            box = new AxisAlignedBox3d(box.Min - new Vector3d(margin, margin, margin), box.Max + new Vector3d(margin, margin, margin));
            dt = new Delaunay3(box);
            dt.SymbolicPerturbation = true;
            dt.RefinementSupport = true;
            // the initial sample: input vertices in id order, each one not within 4 r_max of an earlier choice along the mesh
            // (a breadth-first search over the edges, through vertices within that distance), then Insert's separation
            // (4 r_max) for the choices that are close across the surface
            var marked = new bool[S.MaxVertexID];
            var bfs = new List<int>();
            var verts = new List<int>();
            foreach (int v in S.VertexIndices()) verts.Add(v);
            // the vertex neighbours from the triangles (each edge twice: harmless)
            var nbrs = new List<int>[S.MaxVertexID];
            foreach (int t in S.TriangleIndices()) {
                Index3i tv = S.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    int a = tv[k], b = tv[(k + 1) % 3];
                    (nbrs[a] ?? (nbrs[a] = new List<int>())).Add(b);
                    (nbrs[b] ?? (nbrs[b] = new List<int>())).Add(a);
                }
            }
            double sep2 = 16 * RMax * RMax;
            for (int x = 0; x < verts.Count; ++x) {
                int v = verts[x];
                if (marked[v]) continue;
                Vector3d pv = S.GetVertex(v);
                bfs.Clear(); bfs.Add(v); marked[v] = true;
                for (int k = 0; k < bfs.Count; ++k) {
                    List<int> nb = nbrs[bfs[k]];
                    for (int m = 0; m < nb.Count; ++m) {
                        int u = nb[m];
                        if (!marked[u] && ExactRestrictedVoronoi.D2(S.GetVertex(u), pv) < sep2) { marked[u] = true; bfs.Add(u); }
                    }
                }
                if (dt.Insert(pv, hint, 4 * RMax, out int nv) == Delaunay3.InsertResult.Inserted) Inserted++;
            }
            rvd = new ExactRestrictedVoronoi(S, dt, RMax);
            Diagram = rvd;
            rvd.ComputeAll();
            bool limit = false;
            var sites = new List<int>();
            var fixes = new Heap();
            var fixPoint = new Vector3d[0];
            while (true) {
                if (!drain_facets()) { limit = true; break; }
                if (FacetRulesOnly) break;
                sites.Clear();
                rvd.TakeDirtySites(sites);
                sites.Sort();
                fixes.Clear();
                if (fixPoint.Length < dt.VertexCount) fixPoint = new Vector3d[2 * dt.VertexCount];
                for (int k = 0; k < sites.Count; ++k) {
                    int i = sites[k];
                    grow(i);
                    if (refusedSite[i] == siteVersion[i] + 1) continue;
                    if (site_violation(i, out Vector3d x, out double key)) { fixes.Push(key, i, siteVersion[i]); fixPoint[i] = x; }
                }
                if (fixes.Count == 0) break;
                int accepted = 0;
                while (fixes.Count > 0) {
                    fixes.Pop(out long id, out int ver);
                    int i = (int)id;
                    if (siteVersion[i] != ver) continue;          // changed by an earlier fix: re-evaluated next round
                    if (insert(fixPoint[i])) accepted++;
                    else refusedSite[i] = siteVersion[i] + 1;
                    if (dt.VertexCount >= MaxSamples) { limit = true; break; }
                }
                if (limit) break;
                if (accepted == 0) break;
            }
            final_check();
            Output = build_output();
            Status = limit ? ResultStatus.LimitReached
                : BadEdges + BadArcs + BadCells + CornerCells + FarBalls + FarCells == 0 ? ResultStatus.Certified : ResultStatus.Uncertified;
        }

        static bool closed_manifold(DMesh3 S)
        {
            foreach (int e in S.EdgeIndices()) if (S.IsBoundaryEdge(e)) return false;
            foreach (int v in S.VertexIndices()) if (S.IsBowtieVertex(v)) return false;
            return true;
        }

        void grow(int i)
        {
            if (i < siteVersion.Length) return;
            int n = Math.Max(2 * siteVersion.Length, i + 16);
            Array.Resize(ref siteVersion, n); Array.Resize(ref refusedSite, n);
        }

        bool insert(Vector3d x)
        {
            removed.Clear(); added.Clear();
            var res = dt.Insert(x, hint, hmin, out int nv, removed, added);
            if (res != Delaunay3.InsertResult.Inserted) { Refused++; return false; }
            Inserted++;
            hint = -1;
            rvd.Update(nv, added);
            if (CheckEachInsertion) {
                var full = new ExactRestrictedVoronoi(rvd.Surface, dt, RMax);
                full.ComputeAll();
                CheckedInsertions++;
                CheckMismatches += ExactRestrictedVoronoi.Differences(rvd, full);
            }
            queue_changed_facets();
            // the sites whose cells changed get a new version (a refused fix is retried only after a change)
            List<int> ch = rvd.LastChanged;
            for (int k = 0; k < ch.Count; ++k) { grow(ch[k]); siteVersion[ch[k]]++; }
            return true;
        }

        readonly List<long> changed = new List<long>();
        readonly List<ExactRestrictedVoronoi.Point> pts = new List<ExactRestrictedVoronoi.Point>();
        readonly List<int> ptTris = new List<int>();

        void queue_changed_facets()
        {
            changed.Clear();
            rvd.TakeChangedFacets(changed);
            for (int k = 0; k < changed.Count; ++k) {
                long key = changed[k];
                facetVersion.TryGetValue(key, out int ver);
                facetVersion[key] = ++ver;
                if (!facet_points(key)) continue;
                ExactRestrictedVoronoi.TripleOf(key, out int a, out _, out _);
                facetQueue.Push(farthest(a, out _), key, ver);
            }
        }

        // the kind-3 points of the triple, from the cell of its smallest site; false if none
        bool facet_points(long key)
        {
            ExactRestrictedVoronoi.TripleOf(key, out int a, out int b, out int c);
            pts.Clear(); ptTris.Clear();
            List<ExactRestrictedVoronoi.Piece> cell = rvd.Cell(a);
            for (int k = 0; k < cell.Count; ++k)
                foreach (var q in cell[k].V)
                    if (q.Kind == 3 && q.A == b && q.B == c) { pts.Add(q); ptTris.Add(cell[k].T); }
            return pts.Count > 0;
        }

        double farthest(int a, out int which)
        {
            Vector3d pa = dt.GetVertex(a);
            double best = -1; which = 0;
            for (int k = 0; k < pts.Count; ++k) { double d = ExactRestrictedVoronoi.D2(pts[k].P, pa); if (d > best) { best = d; which = k; } }
            return best;
        }

        // the facet rules R0, (E'), R2, R3 in queue order; false if the sample limit was reached
        bool drain_facets()
        {
            queue_changed_facets();
            while (facetQueue.Count > 0) {
                facetQueue.Pop(out long key, out int ver);
                if (facetVersion[key] != ver) continue;
                if (refusedFacet.TryGetValue(key, out int rv) && rv == ver) continue;
                if (!facet_points(key)) continue;
                if (facet_violation(key) == 0) continue;
                ExactRestrictedVoronoi.TripleOf(key, out int a, out _, out _);
                farthest(a, out int w);
                if (!insert(pts[w].P)) refusedFacet[key] = ver;
                if (dt.VertexCount >= MaxSamples) return false;
            }
            return true;
        }

        // 0 none, 1 a corner (R0), 2 (E'), 3 a ball above r_max (R2), 4 radius-edge above rho (R3); on the points in pts
        int facet_violation(long key)
        {
            ExactRestrictedVoronoi.TripleOf(key, out int a, out int b, out int c);
            if (a < 8) return 1;
            if (pts.Count > 1) return 2;
            if (pts[0].Far) return 3;
            return radius_edge_above(a, b, c) ? 4 : 0;
        }

        bool radius_edge_above(int a, int b, int c)
        {
            Vector3d pa = dt.GetVertex(a), pb = dt.GetVertex(b), pc = dt.GetVertex(c);
            var k = new Vector3d(Rho * Rho, 1, 0);
            return BisectorPredicates.RadiusEdgeAbove(pa, pb, pc, pa, pb, k) > 0 || BisectorPredicates.RadiusEdgeAbove(pa, pb, pc, pb, pc, k) > 0
                || BisectorPredicates.RadiusEdgeAbove(pa, pb, pc, pc, pa, k) > 0;
        }

        // the cell rules R0, (C'), (F'), R2 of site i: the point to insert is the cell's polygon vertex farthest from i (for
        // R2 the farthest of the points beyond r_max)
        bool site_violation(int i, out Vector3d x, out double key)
        {
            x = Vector3d.Zero; key = 0;
            List<ExactRestrictedVoronoi.Piece> cell = rvd.Cell(i);
            if (cell.Count == 0) return false;
            var s = rvd.Summary(i);
            bool farOnly = i >= 8 && s.Disk && s.BadArcs == 0;
            if (farOnly && !s.AnyFar) return false;
            Vector3d pi = dt.GetVertex(i);
            double best = -1;
            for (int k = 0; k < cell.Count; ++k)
                foreach (var q in cell[k].V) {
                    if (farOnly && !q.Far) continue;
                    double d = ExactRestrictedVoronoi.D2(q.P, pi);
                    if (d > best) { best = d; x = q.P; }
                }
            key = best;
            return true;
        }

        void final_check()
        {
            int n = dt.VertexCount;
            var seen = new Dictionary<long, bool>();
            for (int i = 0; i < n; ++i) {
                List<ExactRestrictedVoronoi.Piece> cell = rvd.Cell(i);
                if (cell.Count == 0) continue;
                if (i < 8) { CornerCells++; continue; }
                var s = rvd.Summary(i);
                if (!s.Disk) BadCells++;
                BadArcs += s.BadArcs;
                if (s.AnyFar) FarCells++;
                for (int k = 0; k < cell.Count; ++k)
                    foreach (var q in cell[k].V) {
                        if (q.Kind != 3 || q.A < i) continue;
                        long key = ExactRestrictedVoronoi.TripleKey(i, q.A, q.B);
                        if (seen.ContainsKey(key)) continue;
                        seen[key] = true;
                        int cnt = rvd.FacetPointCount(i, q.A, q.B);
                        RestrictedFacets++;
                        if (cnt > 1) BadEdges++;
                        if (q.Far) FarBalls++;
                        if (radius_edge_above(i, q.A, q.B)) ExemptFacets++;
                    }
            }
            // each bad arc is seen from both of its sites
            BadArcs /= 2;
        }

        // the restricted facets, oriented like the triangle of S whose clipping made their point (DetSitesNormal, never 0 there)
        DMesh3 build_output()
        {
            var m = new DMesh3();
            var map = new Dictionary<int, int>();
            int n = dt.VertexCount;
            var tris = new List<long>();
            var triOf = new Dictionary<long, int>();
            for (int i = 8; i < n; ++i) {
                List<ExactRestrictedVoronoi.Piece> cell = rvd.Cell(i);
                for (int k = 0; k < cell.Count; ++k)
                    foreach (var q in cell[k].V) {
                        if (q.Kind != 3 || q.A < i) continue;
                        long key = ExactRestrictedVoronoi.TripleKey(i, q.A, q.B);
                        if (triOf.ContainsKey(key)) continue;
                        triOf[key] = cell[k].T;
                        tris.Add(key);
                    }
            }
            tris.Sort();
            var used = new List<int>();
            foreach (long key in tris) {
                ExactRestrictedVoronoi.TripleOf(key, out int a, out int b, out int c);
                if (!map.ContainsKey(a)) { map[a] = -1; used.Add(a); }
                if (!map.ContainsKey(b)) { map[b] = -1; used.Add(b); }
                if (!map.ContainsKey(c)) { map[c] = -1; used.Add(c); }
            }
            used.Sort();
            foreach (int v in used) map[v] = m.AppendVertex(dt.GetVertex(v));
            DMesh3 S = rvd.Surface;
            foreach (long key in tris) {
                ExactRestrictedVoronoi.TripleOf(key, out int a, out int b, out int c);
                Index3i tv = S.GetTriangle(triOf[key]);
                int sgn = BisectorPredicates.DetSitesNormal(dt.GetVertex(a), dt.GetVertex(b), dt.GetVertex(c), S.GetVertex(tv.a), S.GetVertex(tv.b), S.GetVertex(tv.c));
                if (sgn > 0) m.AppendTriangle(map[a], map[b], map[c]);
                else m.AppendTriangle(map[a], map[c], map[b]);
            }
            return m;
        }

        /// <summary> a binary max-heap of (key, id, version): the largest key first, ties by the smaller id </summary>
        sealed class Heap
        {
            struct E { public double Key; public long Id; public int Ver; }
            readonly List<E> h = new List<E>();
            public int Count => h.Count;
            public void Clear() { h.Clear(); }
            static bool before(in E x, in E y) { return x.Key != y.Key ? x.Key > y.Key : x.Id < y.Id; }
            public void Push(double key, long id, int ver)
            {
                h.Add(new E { Key = key, Id = id, Ver = ver });
                int k = h.Count - 1;
                while (k > 0) {
                    int p = (k - 1) / 2;
                    if (!before(h[k], h[p])) break;
                    E t = h[k]; h[k] = h[p]; h[p] = t; k = p;
                }
            }
            public void Pop(out long id, out int ver)
            {
                id = h[0].Id; ver = h[0].Ver;
                E last = h[h.Count - 1];
                h.RemoveAt(h.Count - 1);
                if (h.Count == 0) return;
                h[0] = last;
                int k = 0;
                while (true) {
                    int l = 2 * k + 1, r = l + 1, best = k;
                    if (l < h.Count && before(h[l], h[best])) best = l;
                    if (r < h.Count && before(h[r], h[best])) best = r;
                    if (best == k) break;
                    E t = h[k]; h[k] = h[best]; h[best] = t; k = best;
                }
            }
        }
    }
}
