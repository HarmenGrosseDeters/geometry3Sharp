// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Tet mesh improvement (docs/design/TET_MESHING.md, stage 2), after J. Klingner and J. R. Shewchuk, "Aggressive tetrahedral mesh
    /// improvement", IMR 2007 (paper only): hill climbing on the smallest dihedral angle with
    ///   - edge removal (the m tets around an interior edge replaced by the 2 (m - 2) tets of the best triangulation of the ring,
    ///     found by dynamic programming over the ring polygon; m = 3 is the 3-2 flip),
    ///   - face removal (the 2-3 flip of an interior facet),
    ///   - smoothing of interior vertices (moves towards a better position of the worst tet of the star).
    /// An operation is kept only if every new tet is exactly positively oriented (ExactPredicates.Orient3d) and the smallest
    /// dihedral angle of the tets it replaces strictly improves. The boundary facets are never changed by these operations.
    /// Serial and deterministic; scale-free steps, so scaling by 2^k gives the scaled result bit for bit.
    /// </summary>
    internal sealed class TetMeshImprover
    {
        readonly List<Vector3d> pos;
        readonly List<Index4i> tets = new List<Index4i>();
        readonly List<bool> alive = new List<bool>();
        readonly List<List<int>> vtets;
        readonly HashSet<(int, int, int)> boundaryFaces = new HashSet<(int, int, int)>();
        readonly bool[] boundaryVertex;
        readonly Stack<int> free = new Stack<int>();
        internal readonly Dictionary<string, long> Counts = new Dictionary<string, long>();
        /// <summary> tets with a smallest dihedral angle below this are worked on </summary>
        internal double TargetDegrees = 10;
        internal int MaxPasses = 20;
        internal int MaxRing = 8;
        internal Func<bool> CancelF = () => false;
        /// <summary>
        /// boundary smoothing: if set, boundary vertices may move (in any direction, or in their tangent plane with TangentOnly), at
        /// most BoundarySlideLimit from their first position, if the swept region touches no input triangle and the moved boundary
        /// triangles cut no other one (exact)
        /// </summary>
        internal DMeshAABBTree3 InputTree;
        internal double BoundarySlideLimit = 0;
        /// <summary> boundary moves only in the tangent plane of the vertex's boundary fan; false: any direction (still within the limit) </summary>
        internal bool TangentOnly = false;

        internal TetMeshImprover(TetMesh3 m)
        {
            pos = new List<Vector3d>(m.Vertices);
            vtets = new List<List<int>>(pos.Count);
            for (int i = 0; i < pos.Count; ++i) vtets.Add(new List<int>());
            boundaryVertex = new bool[pos.Count];
            foreach (Index3i f in m.BoundaryTriangles) {
                boundaryFaces.Add(key(f.a, f.b, f.c));
                boundaryVertex[f.a] = boundaryVertex[f.b] = boundaryVertex[f.c] = true;
            }
            foreach (Index4i t in m.Tets) add_tet(t);
            foreach (string k in new[] { "edge_removals", "face_removals", "smooth_moves", "boundary_moves", "boundary_refused_input", "boundary_refused_self", "passes" })
                Counts[k] = 0;
            orig = new List<Vector3d>(pos);
            bfaces = new List<Index3i>(m.BoundaryTriangles);
            vfaces = new List<List<int>>(pos.Count);
            for (int i = 0; i < pos.Count; ++i) vfaces.Add(new List<int>());
            for (int f = 0; f < bfaces.Count; ++f) for (int j = 0; j < 3; ++j) vfaces[bfaces[f][j]].Add(f);
        }

        readonly List<Vector3d> orig;
        readonly List<Index3i> bfaces;                    // boundary triangles (outward), fixed topology
        readonly List<List<int>> vfaces;
        // a uniform grid of the boundary triangles' boxes, for the exact self-intersection test of boundary moves
        double cell;
        readonly Dictionary<(long, long, long), List<int>> grid = new Dictionary<(long, long, long), List<int>>();

        void build_grid()
        {
            var lens = new List<double>();
            foreach (Index3i f in bfaces) lens.Add(pos[f.a].Distance(pos[f.b]));
            lens.Sort();
            cell = lens.Count > 0 ? 2 * lens[lens.Count / 2] : 1;
            if (!(cell > 0)) cell = 1;
            for (int f = 0; f < bfaces.Count; ++f) grid_add(f);
        }

        AxisAlignedBox3d face_box(int f) { Index3i t = bfaces[f]; var b = new AxisAlignedBox3d(pos[t.a]); b.Contain(pos[t.b]); b.Contain(pos[t.c]); return b; }
        IEnumerable<(long, long, long)> cells(AxisAlignedBox3d b)
        {
            long x0 = (long)Math.Floor(b.Min.x / cell), x1 = (long)Math.Floor(b.Max.x / cell), y0 = (long)Math.Floor(b.Min.y / cell),
                y1 = (long)Math.Floor(b.Max.y / cell), z0 = (long)Math.Floor(b.Min.z / cell), z1 = (long)Math.Floor(b.Max.z / cell);
            for (long x = x0; x <= x1; ++x) for (long y = y0; y <= y1; ++y) for (long z = z0; z <= z1; ++z) yield return (x, y, z);
        }
        void grid_add(int f) { foreach (var k in cells(face_box(f))) { if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(); l.Add(f); } }
        void grid_remove(int f) { foreach (var k in cells(face_box(f))) if (grid.TryGetValue(k, out var l)) l.Remove(f); }

        static (int, int, int) key(int a, int b, int c)
        {
            if (a > b) { int x = a; a = b; b = x; }
            if (b > c) { int x = b; b = c; c = x; }
            if (a > b) { int x = a; a = b; b = x; }
            return (a, b, c);
        }

        int add_tet(Index4i t)
        {
            int id;
            if (free.Count > 0) { id = free.Pop(); tets[id] = t; alive[id] = true; }
            else { id = tets.Count; tets.Add(t); alive.Add(true); }
            for (int i = 0; i < 4; ++i) vtets[t[i]].Add(id);
            return id;
        }

        void remove_tet(int id)
        {
            Index4i t = tets[id];
            for (int i = 0; i < 4; ++i) vtets[t[i]].Remove(id);
            alive[id] = false;
            free.Push(id);
        }

        /// <summary> smallest dihedral angle in degrees; -1 if not exactly positively oriented </summary>
        internal static double Quality(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            if (ExactPredicates.Orient3d(a, b, c, d) <= 0) return -1;
            // outward facet normals; dihedral at the edge of facets i, j is pi - angle(n_i, n_j)
            Vector3d n0 = (c - b).Cross(d - b), n1 = (d - a).Cross(c - a), n2 = (b - a).Cross(d - a), n3 = (c - a).Cross(b - a);
            double mn = 180;
            mn = Math.Min(mn, dih(n0, n1)); mn = Math.Min(mn, dih(n0, n2)); mn = Math.Min(mn, dih(n0, n3));
            mn = Math.Min(mn, dih(n1, n2)); mn = Math.Min(mn, dih(n1, n3)); mn = Math.Min(mn, dih(n2, n3));
            return mn;
        }
        static double dih(Vector3d ni, Vector3d nj) { return 180 - Math.Atan2(ni.Cross(nj).Length, ni.Dot(nj)) * (180 / Math.PI); }

        double quality(int id) { Index4i t = tets[id]; return Quality(pos[t.a], pos[t.b], pos[t.c], pos[t.d]); }
        double quality(int a, int b, int c, int d) { return Quality(pos[a], pos[b], pos[c], pos[d]); }

        internal void Run()
        {
            for (int pass = 0; pass < MaxPasses && !CancelF(); ++pass) {
                Counts["passes"]++;
                long before = Counts["edge_removals"] + Counts["face_removals"] + Counts["smooth_moves"] + Counts["boundary_moves"];
                // the bad tets, worst first (ids for ties: deterministic)
                var bad = new List<(double q, int id)>();
                for (int id = 0; id < tets.Count; ++id)
                    if (alive[id]) { double q = quality(id); if (q < TargetDegrees) bad.Add((q, id)); }
                bad.Sort();
                foreach (var (q0, id) in bad) {
                    if (CancelF()) break;
                    if (!alive[id] || quality(id) >= TargetDegrees) continue;
                    improve(id);
                }
                if (Counts["edge_removals"] + Counts["face_removals"] + Counts["smooth_moves"] + Counts["boundary_moves"] == before) break;
            }
            // what is left, by the number of boundary vertices and boundary facets
            for (int id = 0; id < tets.Count; ++id) {
                if (!alive[id] || quality(id) >= TargetDegrees) continue;
                Index4i t = tets[id];
                int nv = 0, nf = 0;
                for (int i = 0; i < 4; ++i) {
                    if (boundaryVertex[t[i]]) nv++;
                    if (boundaryFaces.Contains(key(t[Delaunay3.FacetIndices[i, 0]], t[Delaunay3.FacetIndices[i, 1]], t[Delaunay3.FacetIndices[i, 2]]))) nf++;
                }
                string k = "left_v" + nv + "_f" + nf;
                Counts[k] = (Counts.TryGetValue(k, out long x) ? x : 0) + 1;
            }
        }

        void improve(int id)
        {
            Index4i t = tets[id];
            // edges, face removals, then smoothing of its interior vertices
            for (int i = 0; i < 4; ++i)
                for (int j = i + 1; j < 4; ++j)
                    if (alive[id] && tets[id] == t && try_edge_removal(t[i], t[j])) return;
            for (int i = 0; i < 4; ++i)
                if (alive[id] && tets[id] == t && try_face_removal(id, i)) return;
            for (int i = 0; i < 4; ++i)
                if (alive[id] && tets[id] == t && !boundaryVertex[t[i]] && try_smooth(t[i])) return;
            if (InputTree != null && BoundarySlideLimit > 0)
                for (int i = 0; i < 4; ++i)
                    if (alive[id] && tets[id] == t && boundaryVertex[t[i]] && try_smooth_boundary(t[i])) return;
        }

        // ---- boundary smoothing ----

        bool try_smooth_boundary(int v)
        {
            if (cell == 0) build_grid();
            List<int> star = vtets[v];
            double old = 180; int worst = -1;
            foreach (int id in star) { double q = quality(id); if (q < old) { old = q; worst = id; } }
            if (worst < 0) return false;
            Vector3d p0 = pos[v];
            Vector3d n = Vector3d.Zero;
            foreach (int f in vfaces[v]) { Index3i t = bfaces[f]; n += (pos[t.b] - pos[t.a]).Cross(pos[t.c] - pos[t.a]); }
            double nl = n.Length;
            if (!(nl > 0)) return false;
            n /= nl;
            double h = 0; int ne = 0;
            foreach (int id in star) { Index4i t = tets[id]; for (int i = 0; i < 4; ++i) if (t[i] != v) { h += p0.Distance(pos[t[i]]); ne++; } }
            h /= ne;
            double eps = h * (1.0 / 1024);
            Vector3d g = Vector3d.Zero;
            for (int axis = 0; axis < 3; ++axis) {
                Vector3d dv = Vector3d.Zero; dv[axis] = eps;
                pos[v] = p0 + dv; double qp = quality(worst);
                pos[v] = p0 - dv; double qm = quality(worst);
                g[axis] = qp - qm;
            }
            pos[v] = p0;
            if (TangentOnly) g -= g.Dot(n) * n;           // in the tangent plane
            double gl = g.Length;
            if (!(gl > 0)) return false;
            // steps of the star's scale, but within the slide budget left (moves can be far below the edge length)
            double budget = BoundarySlideLimit - p0.Distance(orig[v]);
            if (!(budget > 0)) return false;
            foreach (double sc in new[] { 0.25, 0.125, 0.0625, 0.03125 }) {
                Vector3d p = p0 + (Math.Min(sc * h, sc * 4 * budget) / gl) * g;
                if (p.Distance(orig[v]) > BoundarySlideLimit) continue;
                pos[v] = p;
                double q = 180;
                foreach (int id in star) { q = Math.Min(q, quality(id)); if (q <= old) break; }
                if (q > old && boundary_move_ok(v, p0, p)) {
                    pos[v] = p0;
                    foreach (int f in vfaces[v]) grid_remove(f);
                    pos[v] = p;
                    foreach (int f in vfaces[v]) grid_add(f);
                    Counts["boundary_moves"]++;
                    return true;
                }
                pos[v] = p0;
            }
            return false;
        }

        /// <summary> (tests) would moving boundary vertex v to p pass the exact checks (input and self-intersection)? </summary>
        internal bool BoundaryMoveAllowed(int v, Vector3d p)
        {
            if (cell == 0) build_grid();
            Vector3d p0 = pos[v];
            pos[v] = p;
            bool ok = boundary_move_ok(v, p0, p);
            pos[v] = p0;
            return ok;
        }

        // pos[v] is at p (the candidate); the old position is p0
        bool boundary_move_ok(int v, Vector3d p0, Vector3d p)
        {
            // the swept region: tets (p0, p, a, b) for the boundary link edges (a, b) of v; its faces include the moved triangles
            foreach (int f in vfaces[v]) {
                Index3i t = bfaces[f];
                int j = t.a == v ? 0 : t.b == v ? 1 : 2;
                Vector3d a = pos[t[(j + 1) % 3]], b = pos[t[(j + 2) % 3]];
                if (TetTouchesInput(p0, p, a, b)) { Counts["boundary_refused_input"]++; return false; }
            }
            // the moved triangles against the other boundary triangles near them (exact, beyond shared corners)
            foreach (int f in vfaces[v]) {
                Index3i ia = bfaces[f];
                var A = new Triangle3d(pos[ia.a], pos[ia.b], pos[ia.c]);
                var box = new AxisAlignedBox3d(A.V0); box.Contain(A.V1); box.Contain(A.V2);
                var seen = new HashSet<int>();
                foreach (var k in cells(box)) {
                    if (!grid.TryGetValue(k, out var l)) continue;
                    foreach (int o in l) {
                        if (o == f || !seen.Add(o)) continue;
                        Index3i ib = bfaces[o];
                        var B = new Triangle3d(pos[ib.a], pos[ib.b], pos[ib.c]);
                        if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B)) { Counts["boundary_refused_self"]++; return false; }
                    }
                }
            }
            return true;
        }

        bool TetTouchesInput(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            return MeshTetrahedralization.Refiner.TetTouchesMesh(InputTree, a, b, c, d);
        }

        // ---- edge removal ----

        readonly List<int> ring = new List<int>(), ringTets = new List<int>();

        // the closed ring of vertices around edge (a, b), in order; false if the edge is on the boundary (open ring) or too long
        bool edge_ring(int a, int b)
        {
            ring.Clear(); ringTets.Clear();
            foreach (int id in vtets[a])
                if (tets[id].a == b || tets[id].b == b || tets[id].c == b || tets[id].d == b) ringTets.Add(id);
            int m = ringTets.Count;
            if (m < 3 || m > MaxRing) return false;
            // orient: in the first tet (a, b, c, d) positively oriented with a, b first means the ring goes c -> d around a -> b
            int first = ringTets[0];
            others(tets[first], a, b, out int c, out int d);
            ring.Add(c);
            int cur = first, next = d;
            var used = new HashSet<int> { first };
            for (int k = 1; k < m; ++k) {
                ring.Add(next);
                int found = -1;
                foreach (int id in ringTets) {
                    if (used.Contains(id)) continue;
                    Index4i q = tets[id];
                    if (q.a == next || q.b == next || q.c == next || q.d == next) { found = id; break; }
                }
                if (found < 0) return false;            // open ring: a boundary edge
                used.Add(found);
                others(tets[found], a, b, out int x, out int y);
                next = x == next ? y : x;
                cur = found;
            }
            if (next != c) return false;                // not closed
            // the ring's tets: replace ringTets by the walk order is not needed (all are removed)
            return true;
        }

        // the two other vertices of tet t (besides a, b), ordered so that (a, b, c, d) is positively oriented as a permutation
        void others(Index4i t, int a, int b, out int c, out int d)
        {
            c = d = -1;
            for (int i = 0; i < 4; ++i) {
                int v = t[i];
                if (v == a || v == b) continue;
                if (c < 0) c = v; else d = v;
            }
            // parity: (a, b, c, d) has the orientation of t if it is an even permutation of t
            if (!even_permutation(t, a, b, c, d)) { int x = c; c = d; d = x; }
        }

        static bool even_permutation(Index4i t, int a, int b, int c, int d)
        {
            int[] p = { idx(t, a), idx(t, b), idx(t, c), idx(t, d) };
            int inv = 0;
            for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) if (p[i] > p[j]) inv++;
            return (inv & 1) == 0;
        }
        static int idx(Index4i t, int v) { return t.a == v ? 0 : t.b == v ? 1 : t.c == v ? 2 : 3; }

        bool try_edge_removal(int a, int b)
        {
            if (!edge_ring(a, b)) return false;
            int m = ring.Count;
            double old = 180;
            foreach (int id in ringTets) old = Math.Min(old, quality(id));
            // DP over the ring polygon r0..r(m-1): best[i, k] = best min quality triangulating r_i..r_k (k - i >= 2)
            var best = new double[m, m];
            var split = new int[m, m];
            for (int len = 2; len < m; ++len)
                for (int i = 0; i + len < m; ++i) {
                    int k = i + len;
                    best[i, k] = -1; split[i, k] = -1;
                    for (int j = i + 1; j < k; ++j) {
                        // triangle (r_i, r_j, r_k): the ring runs c -> d with Orient3d(a, b, c, d) > 0, which is clockwise seen from
                        // a, so (r_i, r_j, r_k, b) and (r_k, r_j, r_i, a) are the positive orientations
                        double qa = quality(ring[i], ring[j], ring[k], b), qb = quality(ring[k], ring[j], ring[i], a);
                        double q = Math.Min(qa, qb);
                        if (j - i >= 2) q = Math.Min(q, best[i, j]);
                        if (k - j >= 2) q = Math.Min(q, best[j, k]);
                        if (q > best[i, k]) { best[i, k] = q; split[i, k] = j; }
                    }
                }
            if (!(best[0, m - 1] > old)) return false;
            foreach (int id in ringTets) remove_tet(id);
            emit(0, m - 1, split, a, b);
            Counts["edge_removals"]++;
            return true;
        }

        void emit(int i, int k, int[,] split, int a, int b)
        {
            if (k - i < 2) return;
            int j = split[i, k];
            add_tet(new Index4i(ring[i], ring[j], ring[k], b));
            add_tet(new Index4i(ring[k], ring[j], ring[i], a));
            emit(i, j, split, a, b);
            emit(j, k, split, a, b);
        }

        // ---- face removal (2-3) ----

        bool try_face_removal(int id, int i)
        {
            Index4i t = tets[id];
            int e = t[i];
            int f0 = t[Delaunay3.FacetIndices[i, 0]], f1 = t[Delaunay3.FacetIndices[i, 1]], f2 = t[Delaunay3.FacetIndices[i, 2]];
            // the other tet on facet (f0, f1, f2); none for a boundary facet
            int other = -1, d = -1;
            foreach (int k in vtets[f0]) {
                if (k == id) continue;
                Index4i ot = tets[k];
                if (idx2(ot, f1) && idx2(ot, f2)) {
                    other = k;
                    for (int s = 0; s < 4; ++s) if (ot[s] != f0 && ot[s] != f1 && ot[s] != f2) d = ot[s];
                    break;
                }
            }
            if (other < 0) return false;
            double old = Math.Min(quality(id), quality(other));
            // the outward facet (f0, f1, f2) of t: e is behind it, d in front (Orient3d(f0, f1, f2, d) > 0). New tets around edge
            // (d, e), one per facet edge, oriented as t: (f1, f0, d, e) etc.; all three positive exactly iff the segment de
            // crosses the facet's interior (the 2-3 flip is valid)
            double q0 = quality(f1, f0, d, e), q1 = quality(f2, f1, d, e), q2 = quality(f0, f2, d, e);
            double q = Math.Min(q0, Math.Min(q1, q2));
            if (!(q > old)) return false;
            remove_tet(id); remove_tet(other);
            add_tet(new Index4i(f1, f0, d, e)); add_tet(new Index4i(f2, f1, d, e)); add_tet(new Index4i(f0, f2, d, e));
            Counts["face_removals"]++;
            return true;
        }
        static bool idx2(Index4i q, int v) { return q.a == v || q.b == v || q.c == v || q.d == v; }

        // ---- smoothing ----

        bool try_smooth(int v)
        {
            List<int> star = vtets[v];
            if (star.Count == 0) return false;
            double old = 180;
            int worst = -1;
            foreach (int id in star) { double q = quality(id); if (q < old) { old = q; worst = id; } }
            Vector3d p0 = pos[v];
            // the star's mean edge length sets the step scale
            double h = 0; int ne = 0;
            foreach (int id in star) { Index4i t = tets[id]; for (int i = 0; i < 4; ++i) if (t[i] != v) { h += p0.Distance(pos[t[i]]); ne++; } }
            h /= ne;
            // direction: numerical gradient of the worst tet's quality (central differences, step h / 1000)
            double eps = h * (1.0 / 1024);
            Vector3d g = Vector3d.Zero;
            for (int axis = 0; axis < 3; ++axis) {
                Vector3d dv = Vector3d.Zero; dv[axis] = eps;
                pos[v] = p0 + dv; double qp = quality(worst);
                pos[v] = p0 - dv; double qm = quality(worst);
                g[axis] = (qp - qm);
            }
            pos[v] = p0;
            double gl = g.Length;
            var candidates = new List<Vector3d>();
            if (gl > 0) foreach (double s in new[] { 0.25, 0.125, 0.0625, 0.03125 }) candidates.Add(p0 + (s * h / gl) * g);
            // and the centroid of the star's other vertices (Laplacian)
            Vector3d cen = Vector3d.Zero; int nc = 0;
            foreach (int id in star) { Index4i t = tets[id]; for (int i = 0; i < 4; ++i) if (t[i] != v) { cen += pos[t[i]]; nc++; } }
            candidates.Add(cen / nc);
            foreach (Vector3d p in candidates) {
                pos[v] = p;
                double q = 180;
                foreach (int id in star) { q = Math.Min(q, quality(id)); if (q <= old) break; }
                if (q > old) { Counts["smooth_moves"]++; return true; }
            }
            pos[v] = p0;
            return false;
        }

        internal TetMesh3 Result()
        {
            var m = new TetMesh3();
            var map = new int[pos.Count];
            for (int i = 0; i < map.Length; ++i) map[i] = -1;
            Func<int, int> vid = (v) => { if (map[v] < 0) { map[v] = m.Vertices.Count; m.Vertices.Add(pos[v]); } return map[v]; };
            for (int id = 0; id < tets.Count; ++id) {
                if (!alive[id]) continue;
                Index4i t = tets[id];
                m.Tets.Add(new Index4i(vid(t.a), vid(t.b), vid(t.c), vid(t.d)));
            }
            foreach (var f in boundary_oriented())
                m.BoundaryTriangles.Add(new Index3i(vid(f.a), vid(f.b), vid(f.c)));
            return m;
        }

        // boundary facets with the orientation of their tet (outward)
        IEnumerable<Index3i> boundary_oriented()
        {
            for (int id = 0; id < tets.Count; ++id) {
                if (!alive[id]) continue;
                Index4i t = tets[id];
                for (int i = 0; i < 4; ++i) {
                    int f0 = t[Delaunay3.FacetIndices[i, 0]], f1 = t[Delaunay3.FacetIndices[i, 1]], f2 = t[Delaunay3.FacetIndices[i, 2]];
                    if (boundaryFaces.Contains(key(f0, f1, f2))) yield return new Index3i(f0, f1, f2);
                }
            }
        }
    }
}
