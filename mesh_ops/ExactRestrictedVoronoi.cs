// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// The restricted Voronoi diagram of the vertices of a Delaunay3 (the sites, under its symbolic perturbation) on a clean
    /// closed mesh S, by exact clipping (docs/design/CERTIFIED_REMESHING.md 14.4 deviation, 14.8 and 14.9). Each triangle t of S
    /// is clipped by the power bisectors of a site and its Delaunay neighbours. The polygon t ∩ V(p) has vertices of three
    /// kinds with symbolic keys: a mesh vertex (kind 1, key v), the crossing of a mesh edge with a bisector (kind 2, key the
    /// edge and the other site b), and a point of a Voronoi edge in the plane of t (kind 3, key t and the other two sites).
    /// Every clipping decision is ExactBisectorSoS.Side1/2/3 at such a point, never 0. Kept up to date during refinement
    /// after each insertion of p (the cut lemma of 14.9): an old cell is cut by bisector(j, p) only, and the cell of p is
    /// clipped on the triangles that were cut. The (C') and (F') summaries of the changed cells are computed on demand
    /// (Flush), the (E') counts and the R2 flags of the points when the points are made.
    ///
    /// Sources: D.-M. Yan, B. Lévy, Y. Liu, F. Sun, W. Wang, "Isotropic remeshing with fast and exact computation of restricted
    /// Voronoi diagram", SGP 2009 (clipping, security radius); J.-D. Boissonnat, S. Oudot, "Provably good sampling and meshing
    /// of surfaces", Graphical Models 67, 2005; H. Edelsbrunner, N. Shah, "Triangulating topological spaces", IJCGA 7, 1997
    /// (the topological ball property); papers only.
    /// </summary>
    internal sealed class ExactRestrictedVoronoi
    {
        /// <summary> a polygon vertex of t ∩ V(i) </summary>
        internal struct Point
        {
            /// <summary> 1 a mesh vertex, 2 a mesh edge on bisector(i, B), 3 the point of bisector(i, A), bisector(i, B) and plane(t) </summary>
            internal int Kind;
            /// <summary> kind 1: the mesh vertex; kind 2: the mesh edge; kind 3: the smaller of the two other sites </summary>
            internal int A;
            /// <summary> kind 2: the other site; kind 3: the larger of the two other sites </summary>
            internal int B;
            /// <summary> the polygon edge from this vertex to the next: &gt;= 0 on bisector(i, Label), &lt; 0 on the mesh edge -1 - Label </summary>
            internal int Label;
            /// <summary> R2: the (unweighted) point is farther than r_max from the cell's site (DistanceAbove1-3 &gt; 0) </summary>
            internal bool Far;
            /// <summary> the point in floating point, from its defining elements (queue keys and insertion points only) </summary>
            internal Vector3d P;
        }

        /// <summary> t ∩ V(i) for one triangle t </summary>
        internal struct Piece
        {
            internal int T;
            internal Point[] V;
        }

        /// <summary> the (C') and R2 summary of a cell </summary>
        internal struct CellSummary
        {
            internal int Pieces, Components, Chi;
            /// <summary> pairs (i, q) whose facet meets S in something other than one arc (F') </summary>
            internal int BadArcs;
            internal bool AnyFar;
            internal bool Disk => Components == 1 && Chi == 1;
        }

        readonly DMesh3 S;
        readonly Delaunay3 dt;
        readonly Vector3d r;
        readonly int[] triEdges;        // 3 per triangle: the edge (v_k, v_k+1)
        readonly int[] minTriOfVertex;  // the smallest triangle id at each vertex
        readonly int[] edgeTris;        // 2 per edge
        readonly List<int> tris;        // the triangle ids in increasing order
        readonly int[] triStamp;        // per triangle: the stamp of its entry in triSlot (Summary, is_arc)
        int[] triSlot = new int[0];
        int stamp;

        List<Piece>[] cells = new List<Piece>[0];
        readonly Dictionary<long, int> facetPoints = new Dictionary<long, int>();   // (E'): kind-3 points per site triple, counted in the smallest site's cell
        readonly List<long> changedFacets = new List<long>();
        readonly List<int> dirty = new List<int>();
        bool[] isDirty = new bool[0];
        List<int>[] nbrCache = new List<int>[0];

        /// <summary> the number of calls of Side2 and Side3 (each at a point that exists: they never return 0, or this throws) </summary>
        internal long Side2Calls, Side3Calls;

        /// <summary> S must be clean and closed, dt built with SymbolicPerturbation and RefinementSupport; r_max &gt; 0 </summary>
        internal ExactRestrictedVoronoi(DMesh3 s, Delaunay3 dt, double rmax)
        {
            if (!dt.SymbolicPerturbation || !dt.RefinementSupport)
                throw new ArgumentException("ExactRestrictedVoronoi: the triangulation needs SymbolicPerturbation and RefinementSupport");
            S = s; this.dt = dt; r = new Vector3d(rmax, 0, 0);
            int nt = s.MaxTriangleID, maxEdge = 0;
            triEdges = new int[3 * nt];
            minTriOfVertex = new int[s.MaxVertexID];
            for (int v = 0; v < minTriOfVertex.Length; ++v) minTriOfVertex[v] = int.MaxValue;
            tris = new List<int>();
            foreach (int t in s.TriangleIndices()) {
                tris.Add(t);
                Index3i tv = s.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    int e = s.FindEdge(tv[k], tv[(k + 1) % 3]);
                    triEdges[3 * t + k] = e;
                    if (e >= maxEdge) maxEdge = e + 1;
                    if (t < minTriOfVertex[tv[k]]) minTriOfVertex[tv[k]] = t;
                }
            }
            edgeTris = new int[2 * maxEdge];
            for (int e = 0; e < edgeTris.Length; ++e) edgeTris[e] = -1;
            for (int k = 0; k < tris.Count; ++k)
                for (int m = 0; m < 3; ++m) {
                    int e = triEdges[3 * tris[k] + m];
                    if (edgeTris[2 * e] < 0) edgeTris[2 * e] = tris[k]; else edgeTris[2 * e + 1] = tris[k];
                }
            triStamp = new int[nt];
        }

        internal int SiteCount => dt.VertexCount;
        internal Delaunay3 Triangulation => dt;
        internal DMesh3 Surface => S;

        /// <summary> the pieces of site i (empty if its cell misses S); do not modify </summary>
        internal List<Piece> Cell(int i) { return i < cells.Length && cells[i] != null ? cells[i] : empty; }
        static readonly List<Piece> empty = new List<Piece>();

        List<Piece> cellOf(int i)
        {
            if (i >= cells.Length) {
                int n = Math.Max(2 * cells.Length, i + 16);
                Array.Resize(ref cells, n); Array.Resize(ref isDirty, n); Array.Resize(ref nbrCache, n);
            }
            return cells[i] ?? (cells[i] = new List<Piece>());
        }

        /// <summary> the number of kind-3 points of the site triple a &lt; b &lt; c (0: the facet is not restricted) </summary>
        internal int FacetPointCount(int a, int b, int c) { return facetPoints.TryGetValue(TripleKey(a, b, c), out int n) ? n : 0; }

        internal static long TripleKey(int a, int b, int c)
        {
            if (c >= 1 << 21) throw new InvalidOperationException("ExactRestrictedVoronoi: more than 2^21 sites");
            return a | ((long)b << 21) | ((long)c << 42);
        }

        internal static void TripleOf(long key, out int a, out int b, out int c)
        {
            a = (int)(key & 0x1FFFFF); b = (int)((key >> 21) & 0x1FFFFF); c = (int)(key >> 42);
        }

        /// <summary> the triples whose kind-3 points changed since the last call (in the order of the changes; may repeat) </summary>
        internal void TakeChangedFacets(List<long> into) { for (int k = 0; k < changedFacets.Count; ++k) into.Add(changedFacets[k]); changedFacets.Clear(); }

        /// <summary> the sites whose cells changed since the last call (each once, in the order of the changes) </summary>
        internal void TakeDirtySites(List<int> into)
        {
            for (int k = 0; k < dirty.Count; ++k) { into.Add(dirty[k]); isDirty[dirty[k]] = false; }
            dirty.Clear();
        }

        void mark_dirty(int i) { cellOf(i); if (!isDirty[i]) { isDirty[i] = true; dirty.Add(i); } }

        // ---- the decisions ----

        /// <summary> the side of bisector(i, j) at q in the cell of i (triangle t): -1 inside (closer to i), +1 outside </summary>
        int side(in Point q, int t, int i, int j)
        {
            Vector3d pi = dt.GetVertex(i), pj = dt.GetVertex(j);
            int s;
            if (q.Kind == 1) {
                s = ExactBisectorSoS.Side1(S.GetVertex(q.A), pi, i, pj, j);
            } else if (q.Kind == 2) {
                Index2i ev = S.GetEdgeV(q.A);
                Side2Calls++;
                s = ExactBisectorSoS.Side2(S.GetVertex(ev.a), S.GetVertex(ev.b), pi, i, dt.GetVertex(q.B), q.B, pj, j);
            } else {
                Index3i tv = S.GetTriangle(t);
                Side3Calls++;
                s = ExactBisectorSoS.Side3(pi, i, dt.GetVertex(q.A), q.A, dt.GetVertex(q.B), q.B, S.GetVertex(tv.a), S.GetVertex(tv.b), S.GetVertex(tv.c), pj, j);
            }
            if (s == 0)
                throw new InvalidOperationException("ExactRestrictedVoronoi: a side test returned 0 at a point that exists (14.8 (a))");
            return s;
        }

        /// <summary>
        /// The skip radius (14.9 Lemma R): bisector(i, j) cuts nothing of t ∩ V(i) if |p_j - p_i|^2 &gt; 4 max_v |v - p_i|^2 over the
        /// vertices v of t. True only if the float test proves it (the relative margin 2^-40 covers the rounding of both sums
        /// of squares, 14.9; both in the normal range)
        /// </summary>
        bool skip(int t, Vector3d pi, Vector3d pj)
        {
            Index3i tv = S.GetTriangle(t);
            return SkipRadius(S.GetVertex(tv.a), S.GetVertex(tv.b), S.GetVertex(tv.c), pi, pj);
        }

        /// <summary> Lemma R's float test for the triangle t0 t1 t2, the cell's site pi and the neighbour pj </summary>
        internal static bool SkipRadius(Vector3d t0, Vector3d t1, Vector3d t2, Vector3d pi, Vector3d pj)
        {
            double R2 = D2(t0, pi), rb = D2(t1, pi), rc = D2(t2, pi);
            if (rb > R2) R2 = rb;
            if (rc > R2) R2 = rc;
            double d2 = D2(pj, pi);
            const double minNormal = 1.0261342003245941e-289;   // 2^-960
            if (!(R2 >= minNormal) || !(d2 <= double.MaxValue)) return false;
            return d2 > 4 * R2 * (1 + 1.0 / (1L << 40));
        }

        /// <summary> |a - b|^2: three differences, three squares, two additions, left to right (Lemma R's rounding bound) </summary>
        internal static double D2(Vector3d a, Vector3d b)
        {
            double x = a.x - b.x, y = a.y - b.y, z = a.z - b.z;
            return x * x + y * y + z * z;
        }

        // ---- the points ----

        Point make_kind2(int e, int i, int b, int label)
        {
            Index2i ev = S.GetEdgeV(e);
            Vector3d q0 = S.GetVertex(ev.a), q1 = S.GetVertex(ev.b), a = dt.GetVertex(i), pb = dt.GetVertex(b);
            Vector3d bp = pb - a;
            double bb = bp.LengthSquared;
            double l0 = 2 * bp.Dot(q0 - a) - bb, l1 = 2 * bp.Dot(q1 - a) - bb;
            double s = l0 != l1 ? l0 / (l0 - l1) : 0.5;
            if (!(s >= 0)) s = 0; else if (s > 1) s = 1;
            var q = new Point { Kind = 2, A = e, B = b, Label = label, P = q0 + s * (q1 - q0) };
            q.Far = BisectorPredicates.DistanceAbove2(q0, q1, a, pb, r) > 0;
            return q;
        }

        Point make_kind3(int t, int i, int x, int y, int label)
        {
            int A = Math.Min(x, y), B = Math.Max(x, y);
            Index3i tv = S.GetTriangle(t);
            Vector3d t0 = S.GetVertex(tv.a), t1 = S.GetVertex(tv.b), t2 = S.GetVertex(tv.c);
            var q = new Point { Kind = 3, A = A, B = B, Label = label, P = btpi(t, i, A, B) };
            q.Far = BisectorPredicates.DistanceAbove3(dt.GetVertex(i), dt.GetVertex(A), dt.GetVertex(B), t0, t1, t2, r) > 0;
            return q;
        }

        /// <summary> the point of a Voronoi edge in the plane of t, in floating point (the same for each of its three cells:
        /// computed from the smallest site) </summary>
        Vector3d btpi(int t, int i, int A, int B)
        {
            int a = Math.Min(i, A), b = i < A ? A : Math.Min(i, B), c = Math.Max(i, B);
            Index3i tv = S.GetTriangle(t);
            Vector3d t0 = S.GetVertex(tv.a), t1 = S.GetVertex(tv.b), t2 = S.GetVertex(tv.c);
            Vector3d pa = dt.GetVertex(a), bp = dt.GetVertex(b) - pa, cp = dt.GetVertex(c) - pa;
            Vector3d n = (t1 - t0).Cross(t2 - t0);
            double h = n.Dot(t0 - pa);
            double det = bp.Dot(cp.Cross(n));
            Vector3d N = bp.LengthSquared * cp.Cross(n) + cp.LengthSquared * n.Cross(bp) + 2 * h * bp.Cross(cp);
            Vector3d x = pa + N / (2 * det);
            if (!(x.x - x.x == 0 && x.y - x.y == 0 && x.z - x.z == 0)) x = (t0 + t1 + t2) / 3;
            return x;
        }

        // ---- clipping ----

        readonly List<Point> work = new List<Point>(), tmp = new List<Point>();
        int[] sideBuf = new int[16];

        /// <summary> clips work (a polygon of t ∩ V(i)) by bisector(i, j); false if nothing is outside </summary>
        bool clip(int t, int i, int j)
        {
            int n = work.Count;
            if (sideBuf.Length < n) sideBuf = new int[2 * n];
            bool anyOut = false;
            for (int k = 0; k < n; ++k) { sideBuf[k] = side(work[k], t, i, j); if (sideBuf[k] > 0) anyOut = true; }
            if (!anyOut) return false;
            tmp.Clear();
            for (int k = 0; k < n; ++k) {
                int k1 = k + 1 == n ? 0 : k + 1;
                bool inK = sideBuf[k] < 0, inK1 = sideBuf[k1] < 0;
                if (inK) tmp.Add(work[k]);
                if (inK != inK1) {
                    // the new point lies on the polygon edge k -> k1; leaving the cell, the next edge is on bisector(i, j)
                    int lbl = work[k].Label;
                    int nextLabel = inK ? j : lbl;
                    tmp.Add(lbl < 0 ? make_kind2(-1 - lbl, i, j, nextLabel) : make_kind3(t, i, lbl, j, nextLabel));
                }
            }
            work.Clear();
            for (int k = 0; k < tmp.Count; ++k) work.Add(tmp[k]);
            return true;
        }

        readonly List<int> cellsTmp = new List<int>();

        /// <summary> the Delaunay neighbours of i in increasing id order </summary>
        List<int> neighbours(int i)
        {
            cellOf(i);
            List<int> l = nbrCache[i];
            if (l != null) return l;
            l = new List<int>();
            dt.IncidentCells(i, cellsTmp);
            for (int k = 0; k < cellsTmp.Count; ++k)
                for (int m = 0; m < 4; ++m) {
                    int u = dt.CellVertex(cellsTmp[k], m);
                    if (u != i && !l.Contains(u)) l.Add(u);
                }
            l.Sort();
            nbrCache[i] = l;
            return l;
        }

        /// <summary> t ∩ V(i) into work, clipped by every neighbour of i that the skip radius does not exclude; false if empty </summary>
        bool clip_triangle(int t, int i)
        {
            Index3i tv = S.GetTriangle(t);
            work.Clear();
            for (int k = 0; k < 3; ++k)
                work.Add(new Point { Kind = 1, A = tv[k], Label = -1 - triEdges[3 * t + k], P = S.GetVertex(tv[k]),
                    Far = BisectorPredicates.DistanceAbove1(S.GetVertex(tv[k]), dt.GetVertex(i), r) > 0 });
            List<int> nb = neighbours(i);
            Vector3d pi = dt.GetVertex(i);
            for (int x = 0; x < nb.Count && work.Count > 0; ++x) {
                if (skip(t, pi, dt.GetVertex(nb[x]))) continue;
                clip(t, i, nb[x]);
            }
            return work.Count > 0;
        }

        // ---- storage and the (E') counts ----

        void count_facets(int i, Point[] v, int delta)
        {
            for (int k = 0; k < v.Length; ++k) {
                if (v[k].Kind != 3 || i > v[k].A) continue;
                long key = TripleKey(i, v[k].A, v[k].B);
                facetPoints.TryGetValue(key, out int c);
                c += delta;
                if (c == 0) facetPoints.Remove(key); else facetPoints[key] = c;
                changedFacets.Add(key);
            }
        }

        void add_piece(int i, int t, Point[] v)
        {
            cellOf(i).Add(new Piece { T = t, V = v });
            count_facets(i, v, +1);
            mark_dirty(i);
        }

        // ---- the full computation and the update ----

        /// <summary> computes every cell from scratch (by propagation over the bisector edges inside each triangle) </summary>
        internal void ComputeAll()
        {
            for (int i = 0; i < cells.Length; ++i) {
                if (cells[i] == null) continue;
                for (int k = 0; k < cells[i].Count; ++k) count_facets(i, cells[i][k].V, -1);
                if (cells[i].Count > 0) mark_dirty(i);
                cells[i].Clear();
                nbrCache[i] = null;
            }
            int owner = dt.VertexCount > 8 ? 8 : 0;
            var queue = new List<int>();
            var seen = new int[dt.VertexCount];
            for (int x = 0; x < tris.Count; ++x) {
                int t = tris[x];
                Index3i tv = S.GetTriangle(t);
                owner = owner_of(S.GetVertex(tv.a), owner);
                queue.Clear();
                queue.Add(owner); seen[owner] = x + 1;
                for (int q = 0; q < queue.Count; ++q) {
                    int i = queue[q];
                    if (!clip_triangle(t, i)) continue;
                    Point[] v = work.ToArray();
                    add_piece(i, t, v);
                    for (int k = 0; k < v.Length; ++k)
                        if (v[k].Label >= 0 && seen[v[k].Label] != x + 1) { seen[v[k].Label] = x + 1; queue.Add(v[k].Label); }
                }
            }
        }

        /// <summary> the site whose cell contains the mesh vertex x: a greedy walk over the Delaunay graph (power distance
        /// decreases strictly; it ends where no neighbour is closer, so at the owner: V(i) is the intersection of the
        /// half-spaces of its neighbours) </summary>
        int owner_of(Vector3d x, int start)
        {
            int cur = start;
            for (bool moved = true; moved;) {
                moved = false;
                List<int> nb = neighbours(cur);
                for (int k = 0; k < nb.Count; ++k)
                    if (ExactBisectorSoS.Side1(x, dt.GetVertex(cur), cur, dt.GetVertex(nb[k]), nb[k]) > 0) { cur = nb[k]; moved = true; break; }
            }
            return cur;
        }

        readonly List<int> affected = new List<int>(), cand = new List<int>();
        /// <summary> the sites whose cells the last Update changed (the new site included) </summary>
        internal readonly List<int> LastChanged = new List<int>();
        bool[] candMark = new bool[0];

        /// <summary>
        /// The update after Delaunay3.Insert accepted the vertex p with the new cells added (14.9 Lemma U): the cells of p's
        /// neighbours are cut by bisector(j, p), and p's cell is clipped on the triangles that were cut.
        /// </summary>
        internal void Update(int p, List<int> added)
        {
            affected.Clear(); LastChanged.Clear();
            for (int k = 0; k < added.Count; ++k) {
                int c = added[k];
                if (!dt.IsCell(c)) continue;
                for (int m = 0; m < 4; ++m) { int u = dt.CellVertex(c, m); if (u != p && !affected.Contains(u)) affected.Add(u); }
            }
            affected.Sort();
            cellOf(p); nbrCache[p] = null;
            for (int k = 0; k < affected.Count; ++k) { cellOf(affected[k]); nbrCache[affected[k]] = null; }
            if (candMark.Length < S.MaxTriangleID) candMark = new bool[S.MaxTriangleID];
            cand.Clear();
            Vector3d pp = dt.GetVertex(p);
            for (int k = 0; k < affected.Count; ++k) {
                int j = affected[k];
                List<Piece> list = cellOf(j);
                Vector3d pj = dt.GetVertex(j);
                bool changed = false;
                for (int m = list.Count - 1; m >= 0; --m) {
                    Piece pc = list[m];
                    if (skip(pc.T, pj, pp)) continue;
                    work.Clear();
                    for (int w = 0; w < pc.V.Length; ++w) work.Add(pc.V[w]);
                    if (!clip(pc.T, j, p)) continue;
                    changed = true;
                    if (!candMark[pc.T]) { candMark[pc.T] = true; cand.Add(pc.T); }
                    count_facets(j, pc.V, -1);
                    if (work.Count == 0) list.RemoveAt(m);
                    else { pc.V = work.ToArray(); list[m] = pc; count_facets(j, pc.V, +1); }
                }
                if (changed) { mark_dirty(j); LastChanged.Add(j); }
            }
            LastChanged.Add(p);
            cand.Sort();
            for (int k = 0; k < cand.Count; ++k) {
                int t = cand[k];
                candMark[t] = false;
                if (clip_triangle(t, p)) add_piece(p, t, work.ToArray());
            }
        }

        /// <summary>
        /// tests: the number of sites whose cells differ between x and y (the same pieces, each the same cyclic sequence of
        /// symbolic points with the same labels), plus the triples whose (E') counts differ
        /// </summary>
        internal static int Differences(ExactRestrictedVoronoi x, ExactRestrictedVoronoi y)
        {
            int n = Math.Max(x.SiteCount, y.SiteCount), diff = 0;
            for (int i = 0; i < n; ++i)
                if (!same_cell(x.Cell(i), y.Cell(i))) diff++;
            for (int i = 0; i < n; ++i)
                foreach (Piece pc in x.Cell(i))
                    foreach (Point q in pc.V)
                        if (q.Kind == 3 && q.A > i && x.FacetPointCount(i, q.A, q.B) != y.FacetPointCount(i, q.A, q.B)) diff++;
            return diff;
        }

        /// <summary> tests: a diagram with the cell of site i only (the same surface and triangulation; no (E') counts) </summary>
        internal ExactRestrictedVoronoi OnlySite(int i)
        {
            var d = new ExactRestrictedVoronoi(S, dt, r.x);
            List<Piece> c = d.cellOf(i);
            List<Piece> mine = Cell(i);
            for (int k = 0; k < mine.Count; ++k) c.Add(mine[k]);
            return d;
        }

        static bool same_cell(List<Piece> a, List<Piece> b)
        {
            if (a.Count != b.Count) return false;
            for (int k = 0; k < a.Count; ++k) {
                int m = 0;
                while (m < b.Count && b[m].T != a[k].T) m++;
                if (m == b.Count || !same_polygon(a[k].V, b[m].V)) return false;
            }
            return true;
        }

        static bool same_polygon(Point[] u, Point[] v)
        {
            if (u.Length != v.Length) return false;
            for (int s = 0; s < v.Length; ++s) {
                bool ok = true;
                for (int k = 0; k < u.Length && ok; ++k) {
                    Point p = u[k], q = v[(k + s) % v.Length];
                    ok = p.Kind == q.Kind && p.A == q.A && p.B == q.B && p.Label == q.Label && p.Far == q.Far;
                }
                if (ok) return true;
            }
            return false;
        }

        // ---- the summaries ----

        readonly List<int> par = new List<int>();

        // the slot of triangle t under the current stamp, or -1
        int slot(int t) { return triStamp[t] == stamp ? triSlot[t] : -1; }
        void set_slot(int t, int v) { if (triSlot.Length < triStamp.Length) triSlot = new int[triStamp.Length]; triStamp[t] = stamp; triSlot[t] = v; }
        void new_stamp() { if (++stamp == int.MaxValue) { for (int k = 0; k < triStamp.Length; ++k) triStamp[k] = 0; stamp = 1; } }

        static int find(List<int> par, int k) { while (par[k] != k) { par[k] = par[par[k]]; k = par[k]; } return k; }

        int other_tri(int e, int t) { return edgeTris[2 * e] == t ? edgeTris[2 * e + 1] : edgeTris[2 * e]; }
        int min_tri(int e) { return Math.Min(edgeTris[2 * e], edgeTris[2 * e + 1]); }

        /// <summary>
        /// (C'), (F') and R2 of the cell of i (Lemma 1): chi = V - E + F of the pieces (each point and each mesh-edge segment
        /// counted once: in the piece of the smallest triangle that contains it), the components through shared mesh-edge
        /// segments, and for each pair (i, q) the arc S ∩ F_iq: one component of the bisector segments, nodes - segments = 1
        /// </summary>
        internal CellSummary Summary(int i)
        {
            var s = new CellSummary();
            List<Piece> list = Cell(i);
            s.Pieces = list.Count;
            if (list.Count == 0) return s;
            new_stamp(); par.Clear();
            for (int k = 0; k < list.Count; ++k) { set_slot(list[k].T, k); par.Add(k); }
            int V = 0, E = 0;
            for (int k = 0; k < list.Count; ++k) {
                Piece pc = list[k];
                Point[] v = pc.V;
                for (int m = 0; m < v.Length; ++m) {
                    if (v[m].Far) s.AnyFar = true;
                    if (v[m].Kind == 1) { if (minTriOfVertex[v[m].A] == pc.T) V++; }
                    else if (v[m].Kind == 2) { if (min_tri(v[m].A) == pc.T) V++; }
                    else V++;
                    int lbl = v[m].Label;
                    if (lbl >= 0) { E++; continue; }
                    int e = -1 - lbl;
                    if (min_tri(e) == pc.T) E++;
                    int o = slot(other_tri(e, pc.T));
                    if (o >= 0) { int ra = find(par, k), rb = find(par, o); if (ra != rb) par[ra] = rb; }
                }
            }
            s.Chi = V - E + list.Count;
            for (int k = 0; k < par.Count; ++k) if (find(par, k) == k) s.Components++;
            s.BadArcs = bad_arcs(i, list);
            return s;
        }

        readonly List<int> pairs = new List<int>();
        readonly List<int> arcPar = new List<int>();

        // the number of pairs (i, q) whose restricted facet is not one arc
        int bad_arcs(int i, List<Piece> list)
        {
            pairs.Clear();
            for (int k = 0; k < list.Count; ++k)
                foreach (Point q in list[k].V) if (q.Label >= 0 && !pairs.Contains(q.Label)) pairs.Add(q.Label);
            int bad = 0;
            for (int x = 0; x < pairs.Count; ++x)
                if (!is_arc(i, pairs[x], list)) bad++;
            return bad;
        }

        /// <summary> (F') for the pair (i, q), from i's pieces: one component and nodes - segments = 1 </summary>
        internal bool IsArc(int i, int q) { return is_arc(i, q, Cell(i)); }

        bool is_arc(int i, int q, List<Piece> list)
        {
            new_stamp(); arcPar.Clear();
            int nodes = 0, segs = 0;
            for (int k = 0; k < list.Count; ++k) {
                Point[] v = list[k].V;
                for (int m = 0; m < v.Length; ++m) {
                    if (v[m].Label != q) continue;
                    // the segment from v[m] to v[m + 1] lies on bisector(i, q); at most one per triangle (a convex polygon)
                    set_slot(list[k].T, segs); arcPar.Add(segs); segs++;
                    Point a = v[m], b = v[m + 1 == v.Length ? 0 : m + 1];
                    nodes += node_weight2(a, list[k].T) + node_weight2(b, list[k].T);
                }
            }
            // a kind-2 end is shared with the segment of the other triangle of its edge (counted once there), a kind-3 end is an end of the arc
            for (int k = 0; k < list.Count; ++k) {
                Point[] v = list[k].V;
                for (int m = 0; m < v.Length; ++m) {
                    if (v[m].Label != q) continue;
                    int me = slot(list[k].T);
                    Point a = v[m], b = v[m + 1 == v.Length ? 0 : m + 1];
                    for (int w = 0; w < 2; ++w) {
                        Point end = w == 0 ? a : b;
                        if (end.Kind != 2) continue;
                        int o = slot(other_tri(end.A, list[k].T));
                        if (o >= 0) { int ra = find(arcPar, me), rb = find(arcPar, o); if (ra != rb) arcPar[ra] = rb; }
                    }
                }
            }
            int comps = 0;
            for (int k = 0; k < arcPar.Count; ++k) if (find(arcPar, k) == k) comps++;
            // nodes counted twice over (2 for a kind-3 end, 1 for each side of a kind-2 end)
            return comps == 1 && nodes == 2 * (segs + 1);
        }

        int node_weight2(in Point a, int t) { return a.Kind == 3 ? 2 : a.Kind == 2 ? 1 : 0; }
    }
}
