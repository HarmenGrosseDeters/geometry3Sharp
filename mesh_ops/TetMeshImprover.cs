// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Tet mesh improvement (docs/design/TET_MESHING.md, stage 2), after B. M. Klingner and J. R. Shewchuk, "Aggressive tetrahedral mesh
    /// improvement", IMR 2007 (paper only): hill climbing on the smallest dihedral angle with
    ///   - edge removal (the m tets around an interior edge replaced by the 2 (m - 2) tets of the best triangulation of the ring,
    ///     found by dynamic programming over the ring polygon; m = 3 is the 3-2 flip),
    ///   - face removal (the 2-3 flip of an interior facet),
    ///   - smoothing of interior vertices (moves towards a better position of the worst tet of the star),
    ///   - with InputTree and BoundarySlideLimit set, moves of boundary vertices, capped cumulatively at BoundarySlideLimit from
    ///     their first position and kept only if the swept region touches no input triangle and the moved boundary triangles
    ///     cut no other boundary triangle (both exact).
    /// An operation is kept only if every new tet is exactly positively oriented (ExactPredicates.Orient3d) and the smallest
    /// dihedral angle of the tets it replaces strictly improves. Edge and face removal never change the boundary facets; boundary
    /// moves change only their positions.
    /// Serial and deterministic; scale-free steps, so scaling by 2^k gives the scaled result bit for bit.
    /// </summary>
    internal sealed class TetMeshImprover
    {
        readonly List<Vector3d> pos;
        readonly List<Index4i> tets = new List<Index4i>();
        readonly List<bool> alive = new List<bool>();
        readonly List<List<int>> vtets;
        readonly HashSet<(int, int, int)> boundaryFaces = new HashSet<(int, int, int)>();
        readonly List<bool> boundaryVertex;
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
            boundaryVertex = new List<bool>(new bool[pos.Count]);
            foreach (Index3i f in m.BoundaryTriangles) {
                boundaryFaces.Add(key(f.a, f.b, f.c));
                boundaryVertex[f.a] = boundaryVertex[f.b] = boundaryVertex[f.c] = true;
            }
            foreach (Index4i t in m.Tets) add_tet(t);
            foreach (string k in new[] { "edge_removals", "face_removals", "smooth_moves", "boundary_moves", "boundary_refused_input", "boundary_refused_self", "passes", "contractions", "insertions", "split_refused_input" })
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
        // a uniform grid of the boundary triangles, for the exact self-intersection test of boundary moves. Static: each triangle
        // is registered once with the box of its ORIGINAL corners grown by BoundarySlideLimit, which contains the triangle at
        // every later time (no corner ever gets farther than the limit from its original position), so the grid is never stale.
        // Triangles that would span more than MaxCellsPerFace cells go to a list that every query checks.
        double cell;
        /// <summary> (tests) the grid's cell size (0 = automatic) and the cell count above which a face goes to the always-checked list </summary>
        internal double GridCell = 0;
        internal int MaxCellsPerFace = 64;
        readonly Dictionary<(long, long, long), List<int>> grid = new Dictionary<(long, long, long), List<int>>();
        readonly List<int> bigFaces = new List<int>();

        void build_grid()
        {
            var lens = new List<double>();
            foreach (Index3i f in bfaces) lens.Add(orig[f.a].Distance(orig[f.b]));
            lens.Sort();
            cell = GridCell > 0 ? GridCell : (lens.Count > 0 ? 2 * lens[lens.Count / 2] : 0) + 2 * BoundarySlideLimit;
            if (!(cell > 0)) cell = 1;
            for (int f = 0; f < bfaces.Count; ++f) {
                AxisAlignedBox3d b = reach_box(f);
                if (cell_count(b) > MaxCellsPerFace) { bigFaces.Add(f); continue; }
                foreach (var k in cells(b)) { if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(); l.Add(f); }
            }
        }

        // the box that contains face f at any time: its original corners' box grown by the slide limit
        AxisAlignedBox3d reach_box(int f)
        {
            Index3i t = bfaces[f];
            var b = new AxisAlignedBox3d(orig[t.a]); b.Contain(orig[t.b]); b.Contain(orig[t.c]);
            b.Expand(BoundarySlideLimit);
            return b;
        }
        long cell_count(AxisAlignedBox3d b)
        {
            long nx = MathUtil.FloorToLongSaturating(b.Max.x / cell) - MathUtil.FloorToLongSaturating(b.Min.x / cell) + 1, ny = MathUtil.FloorToLongSaturating(b.Max.y / cell) - MathUtil.FloorToLongSaturating(b.Min.y / cell) + 1,
                nz = MathUtil.FloorToLongSaturating(b.Max.z / cell) - MathUtil.FloorToLongSaturating(b.Min.z / cell) + 1;
            return nx * ny * nz;
        }
        IEnumerable<(long, long, long)> cells(AxisAlignedBox3d b)
        {
            long x0 = MathUtil.FloorToLongSaturating(b.Min.x / cell), x1 = MathUtil.FloorToLongSaturating(b.Max.x / cell), y0 = MathUtil.FloorToLongSaturating(b.Min.y / cell),
                y1 = MathUtil.FloorToLongSaturating(b.Max.y / cell), z0 = MathUtil.FloorToLongSaturating(b.Min.z / cell), z1 = MathUtil.FloorToLongSaturating(b.Max.z / cell);
            for (long x = x0; x <= x1; ++x) for (long y = y0; y <= y1; ++y) for (long z = z0; z <= z1; ++z) yield return (x, y, z);
        }

        static (int, int, int) key(int a, int b, int c)
        {
            if (a > b) { int x = a; a = b; b = x; }
            if (b > c) { int x = b; b = c; c = x; }
            if (a > b) { int x = a; a = b; b = x; }
            return (a, b, c);
        }

        // undo journal of a split: tets added (id; fromFree: popped from the free stack, else appended) and removed (id, tet; its
        // id pushed on the free stack). Undone in reverse order, these stack operations restore the stack exactly
        List<(int id, Index4i t, bool added, bool fromFree)> tetLog;
        // undo journal of a split's grid registrations: the lists appended to (each append is at the list's end)
        List<List<int>> gridLog;

        // per vertex: bumped whenever a tet at the vertex is added or removed, or the vertex moves (memo of failed operations)
        readonly List<int> ver = new List<int>();
        int ver_of(int v) { while (ver.Count <= v) ver.Add(0); return ver[v]; }
        void bump(int v) { while (ver.Count <= v) ver.Add(0); ver[v]++; }

        int add_tet(Index4i t)
        {
            int id;
            bool fromFree = free.Count > 0;
            if (fromFree) { id = free.Pop(); tets[id] = t; alive[id] = true; }
            else { id = tets.Count; tets.Add(t); alive.Add(true); }
            for (int i = 0; i < 4; ++i) { vtets[t[i]].Add(id); bump(t[i]); }
            tetLog?.Add((id, t, true, fromFree));
            return id;
        }

        void remove_tet(int id)
        {
            Index4i t = tets[id];
            for (int i = 0; i < 4; ++i) { vtets[t[i]].Remove(id); bump(t[i]); }
            alive[id] = false;
            free.Push(id);
            tetLog?.Add((id, t, false, false));
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
            hill_climb(MaxPasses);
            // rescue phase (TET_MESHING.md section 17): only if tets below the target are left, so meshes that reach it are unchanged
            if (RescuePasses > 0 && !CancelF() && any_bad()) {
                ActiveSet = true;
                long p = Counts["passes"];
                hill_climb(RescuePasses);
                Counts["rescue_passes"] = Counts["passes"] - p;
                ActiveSet = false;
            }
            classify_left();
        }

        /// <summary> passes of the rescue phase (active-set smoothing) after the main passes, if tets below the target are left </summary>
        internal int RescuePasses = 0;
        /// <summary> smoothing of interior vertices also along the active-set direction (Freitag, Jones, Plassmann 1995) </summary>
        internal bool ActiveSet = false;
        bool any_bad() { for (int id = 0; id < tets.Count; ++id) if (alive[id] && quality(id) < TargetDegrees) return true; return false; }

        /// <summary>
        /// the steepest ascent direction of the smallest dihedral angle of v's star (Freitag, Jones, Plassmann 1995): the point of
        /// least norm in the convex hull of the gradients of the near-worst tets (within ActiveBand degrees of the worst), found by
        /// Gilbert's iteration; zero if there is no direction that raises all of them
        /// </summary>
        Vector3d active_direction(int v, double worstQ, double eps)
        {
            Vector3d p0 = pos[v];
            var gs = new List<Vector3d>();
            foreach (int id in vtets[v]) {
                double q = quality(id);
                if (!(q <= worstQ + ActiveBand)) continue;
                Vector3d g = Vector3d.Zero;
                for (int axis = 0; axis < 3; ++axis) {
                    Vector3d dv = Vector3d.Zero; dv[axis] = eps;
                    pos[v] = p0 + dv; double qp = quality(id);
                    pos[v] = p0 - dv; double qm = quality(id);
                    g[axis] = qp - qm;
                }
                pos[v] = p0;
                gs.Add(g);
            }
            return MinNormInHull(gs);
        }

        /// <summary>
        /// the point of least norm in the convex hull of gs (Gilbert's iteration, 64 steps); zero (or nearly) when no direction raises
        /// all of them to first order. A candidate step only: the smoothing keeps a move only if the star's worst angle rises
        /// </summary>
        internal static Vector3d MinNormInHull(List<Vector3d> gs)
        {
            if (gs.Count == 0) return Vector3d.Zero;
            Vector3d x = gs[0];
            for (int it = 0; it < 64; ++it) {
                int best = 0; double bd = double.MaxValue;
                for (int i = 0; i < gs.Count; ++i) { double d = gs[i].Dot(x); if (d < bd) { bd = d; best = i; } }
                Vector3d e = x - gs[best];
                double ee = e.LengthSquared;
                if (!(ee > 0) || !(x.Dot(e) > 1e-12 * x.LengthSquared)) break;
                double t = Math.Min(1, x.Dot(e) / ee);
                x = x - t * e;
            }
            return x;
        }
        /// <summary> tets within this many degrees of the worst of a star are active in the active-set direction </summary>
        internal double ActiveBand = 1;

        void hill_climb(int maxPasses)
        {
            for (int pass = 0; pass < maxPasses && !CancelF(); ++pass) {
                Counts["passes"]++;
                long before = Counts["edge_removals"] + Counts["face_removals"] + Counts["smooth_moves"] + Counts["boundary_moves"] + ins_count();
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
                if (Counts["edge_removals"] + Counts["face_removals"] + Counts["smooth_moves"] + Counts["boundary_moves"] + ins_count() == before) break;
            }
        }

        long ins_count() { return (Counts.TryGetValue("insertions", out long a) ? a : 0) + (Counts.TryGetValue("contractions", out long b) ? b : 0); }

        void classify_left()
        {
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

        // ---- diagnosis (docs/design/TET_MESHING.md section 16): why the tets left below the target stay there ----

        bool dry;
        double lastDp;
        string lastContract;
        string lastFace, lastBoundary;
        /// <summary> (diagnosis) one line per tet left below TargetDegrees after Diagnose() </summary>
        internal readonly List<string> DiagnosticLines = new List<string>();

        /// <summary>
        /// (diagnosis) for every tet left below TargetDegrees: its shape, how it touches the boundary, and why each operation fails
        /// on it (side-effect free: the operations run with dry set; splits are not tried). Adds "diag_*" counts.
        /// </summary>
        internal void Diagnose()
        {
            var bedges = new HashSet<(int, int)>();
            foreach (Index3i f in bfaces) for (int j = 0; j < 3; ++j) { int a = f[j], b = f[(j + 1) % 3]; bedges.Add(a < b ? (a, b) : (b, a)); }
            if (InputTree != null && BoundarySlideLimit > 0 && cell == 0) build_grid();
            dry = true;
            var snapshot = new Dictionary<string, long>(Counts);
            var add = new Dictionary<string, long>();
            Action<string> inc = (k) => add[k] = (add.TryGetValue(k, out long x) ? x : 0) + 1;
            for (int id = 0; id < tets.Count; ++id) {
                if (!alive[id]) continue;
                double q = quality(id);
                if (q >= TargetDegrees) continue;
                Index4i t = tets[id];
                int nv = 0, nf = 0, ne = 0;
                for (int i = 0; i < 4; ++i) {
                    if (boundaryVertex[t[i]]) nv++;
                    if (boundaryFaces.Contains(key(t[Delaunay3.FacetIndices[i, 0]], t[Delaunay3.FacetIndices[i, 1]], t[Delaunay3.FacetIndices[i, 2]]))) nf++;
                }
                for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) { int a = t[i], b = t[j]; if (bedges.Contains(a < b ? (a, b) : (b, a))) ne++; }
                string touch = nf > 0 ? "facet" + nf : ne > 0 ? "edge" + ne : nv > 0 ? "vertex" + nv : "interior";
                string shape = Shape(pos[t.a], pos[t.b], pos[t.c], pos[t.d]);
                inc("diag_touch_" + touch); inc("diag_shape_" + shape);
                bool can = false;
                // the shortest edge: boundary edge, chord between two boundary vertices, or with an interior end; ratio to the longest
                int sa = -1, sb = -1; double smin = double.MaxValue, smax = 0;
                for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) { double l = pos[t[i]].Distance(pos[t[j]]); smax = Math.Max(smax, l); if (l < smin) { smin = l; sa = t[i]; sb = t[j]; } }
                string sk = bedges.Contains(sa < sb ? (sa, sb) : (sb, sa)) ? "bedge" : boundaryVertex[sa] && boundaryVertex[sb] ? "chord" : boundaryVertex[sa] || boundaryVertex[sb] ? "mixed" : "inner";
                if (shape == "needle" || shape == "wedge") {
                    inc("diag_short_" + sk);
                    if (Contraction) {
                        if (try_contract(sa, sb)) can = true;
                        string r1 = lastContract;
                        if (try_contract(sb, sa)) can = true;
                        string r2 = lastContract;
                        inc("diag_contract_" + sk + "_" + (string.CompareOrdinal(r1, r2) < 0 ? r1 + "_" + r2 : r2 + "_" + r1));
                        if (BoundarySlideLimit > 0) shape += "/short_in_caps:" + (smin / BoundarySlideLimit).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "/long_in_caps:" + (smax / BoundarySlideLimit).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                shape += "/" + sk + ":" + (smin / smax).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                // operations, as improve() would try them (all but the splits, which have no dry run)
                var why = new List<string>();
                for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) {
                    int a = t[i], b = t[j];
                    bool be = bedges.Contains(a < b ? (a, b) : (b, a));
                    lastDp = double.NaN;
                    if (try_edge_removal(a, b)) { can = true; why.Add("E+"); continue; }
                    int m = 0; foreach (int k in vtets[a]) if (idx2(tets[k], b)) m++;
                    string r = be ? "Eb" : m > MaxRing ? "Er" + m : double.IsNaN(lastDp) ? "Eo" : "Ed" + m + ":" + lastDp.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    why.Add(r); inc("diag_edge_" + (r.StartsWith("Ed") ? "dp_not_better" : r.StartsWith("Er") ? "ring_too_large" : r == "Eb" ? "boundary_edge" : "open_ring"));
                }
                for (int i = 0; i < 4; ++i) {
                    lastFace = "boundary";
                    if (try_face_removal(id, i)) { can = true; why.Add("F+"); continue; }
                    why.Add("F" + lastFace[0]); inc("diag_face_" + lastFace);
                }
                for (int i = 0; i < 4; ++i) {
                    int v = t[i];
                    if (!boundaryVertex[v]) {
                        if (try_smooth(v)) { can = true; why.Add("S+"); continue; }
                        why.Add("Si"); inc("diag_smooth_interior_not_better");
                    } else if (InputTree != null && BoundarySlideLimit > 0) {
                        if (try_smooth_boundary(v)) { can = true; why.Add("B+"); continue; }
                        why.Add("B:" + lastBoundary); inc("diag_boundary_" + lastBoundary);
                    }
                }
                if (can) inc("diag_still_improvable");
                DiagnosticLines.Add(q.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " " + shape + " " + touch + " v" + nv + " " + string.Join(" ", why));
            }
            dry = false;
            Counts.Clear();
            foreach (var kv in snapshot) Counts[kv.Key] = kv.Value;
            foreach (var kv in add) Counts[kv.Key] = kv.Value;
        }

        /// <summary>
        /// (diagnosis) the shape class of a bad tet after Cheng, Dey, Edelsbrunner, Facello, Teng 2000 (fig. 1): "sliver" (no short
        /// edge, no vertex over the opposite face's interior), "cap" (a vertex projects into the opposite face), "wedge" / "needle"
        /// (a short edge: shortest / longest below 0.3; wedge with at least two small dihedral angles)
        /// </summary>
        internal static string Shape(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            Vector3d[] p = { a, b, c, d };
            double lmin = double.MaxValue, lmax = 0;
            for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) { double l = p[i].Distance(p[j]); lmin = Math.Min(lmin, l); lmax = Math.Max(lmax, l); }
            Vector3d n0 = (c - b).Cross(d - b), n1 = (d - a).Cross(c - a), n2 = (b - a).Cross(d - a), n3 = (c - a).Cross(b - a);
            Vector3d[] n = { n0, n1, n2, n3 };
            int small = 0;
            for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) if (dih(n[i], n[j]) < 10) small++;
            if (lmin < 0.3 * lmax) return small >= 2 ? "wedge" : "needle";
            for (int i = 0; i < 4; ++i) {
                // project p[i] onto the plane of the opposite face; inside its triangle?
                Vector3d u = p[Delaunay3.FacetIndices[i, 0]], v = p[Delaunay3.FacetIndices[i, 1]], w = p[Delaunay3.FacetIndices[i, 2]];
                Vector3d nn = (v - u).Cross(w - u);
                double nl2 = nn.LengthSquared; if (!(nl2 > 0)) continue;
                Vector3d x = p[i] - ((p[i] - u).Dot(nn) / nl2) * nn;
                double s0 = (v - u).Cross(x - u).Dot(nn), s1 = (w - v).Cross(x - v).Dot(nn), s2 = (u - w).Cross(x - w).Dot(nn);
                if (s0 > 0 && s1 > 0 && s2 > 0) return "cap";
            }
            return "sliver";
        }

        // ---- test hooks ----

        /// <summary> (tests) accept splits and contractions that pass the exact checks, whatever the quality </summary>
        internal bool IgnoreQualityForTest = false;
        /// <summary> (tests) the split point of an edge instead of its midpoint, to reach the exact checks that guard the rounding </summary>
        internal Func<Vector3d, Vector3d, Vector3d> SplitPointForTest;
        /// <summary> (tests) the split of edge (a, b) as the improvement would try it </summary>
        internal bool TrySplit(int a, int b) { if (InputTree != null && BoundarySlideLimit > 0 && cell == 0) build_grid(); return try_split(a, b); }
        /// <summary> (tests) the contraction of u into w as the improvement would try it </summary>
        internal bool TryContract(int u, int w) { return try_contract(u, w); }
        /// <summary> (tests) the link condition of edge (u, w) for an interior u </summary>
        internal bool LinkCondition(int u, int w) { return link_ok(u, w); }
        internal bool IsBoundaryVertex(int v) { return boundaryVertex[v]; }
        internal bool IsBoundaryEdge(int a, int b) { return is_bedge(a, b); }
        internal int VertexCount => pos.Count;
        /// <summary> (tests) the alive tets </summary>
        internal List<Index4i> AliveTets() { var r = new List<Index4i>(); for (int id = 0; id < tets.Count; ++id) if (alive[id]) r.Add(tets[id]); return r; }
        /// <summary> (tests) the improver's internal state, in order: per vertex its tet ids and boundary face ids, the free tet ids </summary>
        internal string StateForTest()
        {
            var sb = new System.Text.StringBuilder();
            for (int v = 0; v < vtets.Count; ++v) { sb.Append(string.Join(",", vtets[v])).Append('|').Append(string.Join(",", vfaces[v])).Append(';'); }
            sb.Append('#').Append(string.Join(",", free)).Append('#').Append(tets.Count);
            return sb.ToString();
        }
        /// <summary> (tests) the neighbours of v </summary>
        internal SortedSet<int> Neighbours(int v) { var r = new SortedSet<int>(); foreach (int id in vtets[v]) { Index4i t = tets[id]; for (int i = 0; i < 4; ++i) if (t[i] != v) r.Add(t[i]); } return r; }

        void improve(int id)
        {
            Index4i t = tets[id];
            // edges, face removals, contractions, splits, then smoothing of its interior vertices and moves of its boundary vertices
            for (int i = 0; i < 4; ++i)
                for (int j = i + 1; j < 4; ++j)
                    if (alive[id] && tets[id] == t && try_edge_removal(t[i], t[j])) return;
            for (int i = 0; i < 4; ++i)
                if (alive[id] && tets[id] == t && try_face_removal(id, i)) return;
            if (Contraction && alive[id] && tets[id] == t && try_contract_tet(id)) return;
            if (Insertion && alive[id] && tets[id] == t && try_split_tet(id)) return;
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
            lastBoundary = "no_star";
            if (worst < 0) return false;
            Vector3d p0 = pos[v];
            Vector3d n = Vector3d.Zero;
            foreach (int f in vfaces[v]) { Index3i t = bfaces[f]; n += (pos[t.b] - pos[t.a]).Cross(pos[t.c] - pos[t.a]); }
            double nl = n.Length;
            if (TangentOnly && !(nl > 0)) return false;
            if (nl > 0) n /= nl;
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
            lastBoundary = "no_gradient";
            if (!(gl > 0)) return false;
            // steps of the star's scale, but within the slide budget left (moves can be far below the edge length)
            double budget = slide_limit(v) - p0.Distance(orig[v]);
            lastBoundary = "budget";
            if (!(budget > 0)) return false;
            lastBoundary = "not_better";
            foreach (double sc in new[] { 0.25, 0.125, 0.0625, 0.03125 }) {
                Vector3d p = p0 + (Math.Min(sc * h, sc * 4 * budget) / gl) * g;
                if (p.Distance(orig[v]) > slide_limit(v)) { lastBoundary = "budget"; continue; }
                pos[v] = p;
                double q = 180;
                foreach (int id in star) { q = Math.Min(q, quality(id)); if (q <= old) break; }
                if (q > old) {
                    long ri = Counts["boundary_refused_input"];
                    if (boundary_move_ok(v, p0, p)) {
                        if (dry) { pos[v] = p0; return true; }
                        Counts["boundary_moves"]++;
                        bump(v);
                        return true;
                    }
                    lastBoundary = Counts["boundary_refused_input"] > ri ? "refused_input" : "refused_self";
                }
                pos[v] = p0;
            }
            return false;
        }

        /// <summary> (tests) the largest distance of a vertex from its first position </summary>
        internal double MaxDisplacement()
        {
            double m = 0;
            for (int v = 0; v < pos.Count; ++v) if (boundaryVertex[v]) m = Math.Max(m, pos[v].Distance(orig[v]));
            return m;
        }

        /// <summary> (tests) moves vertex v to p without any check (to build up displaced states for BoundaryMoveAllowed) </summary>
        internal void SetPosition(int v, Vector3d p) { pos[v] = p; }

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
                // every face that can meet A is registered in a cell of A's box (its reach box contains it), or is a big face;
                // this includes the other faces of v's fan
                List<int> seen = near_faces(box);
                foreach (int o in seen) {
                    if (o == f || o >= bfaces.Count) continue;
                    Index3i ib = bfaces[o];
                    if (!boundaryFaces.Contains(key(ib.a, ib.b, ib.c))) continue;      // removed by a contraction or an undone split
                    var B = new Triangle3d(pos[ib.a], pos[ib.b], pos[ib.c]);
                    if (boxes_meet(ref A, ref B) && TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, ib, ref B)) { Counts["boundary_refused_self"]++; return false; }
                }
            }
            return true;
        }

        // the boundary faces registered in the cells of box (or big), once each (a stamp per face instead of a set), whose current
        // box meets box; reused list
        readonly List<int> nearList = new List<int>();
        int[] faceStamp = new int[0];
        int stamp;
        List<int> near_faces(AxisAlignedBox3d box)
        {
            nearList.Clear();
            if (faceStamp.Length < bfaces.Count) Array.Resize(ref faceStamp, Math.Max(bfaces.Count, 2 * faceStamp.Length));
            if (++stamp == int.MaxValue) { Array.Clear(faceStamp, 0, faceStamp.Length); stamp = 1; }
            foreach (int o in bigFaces) consider(o, box);
            foreach (var k in cells(box))
                if (grid.TryGetValue(k, out var l)) foreach (int o in l) consider(o, box);
            return nearList;
        }
        void consider(int o, AxisAlignedBox3d box)
        {
            if (o >= bfaces.Count || faceStamp[o] == stamp) return;
            faceStamp[o] = stamp;
            Index3i t = bfaces[o];
            Vector3d a = pos[t.a], b = pos[t.b], c = pos[t.c];
            for (int k = 0; k < 3; ++k)
                if (Math.Max(a[k], Math.Max(b[k], c[k])) < box.Min[k] || Math.Min(a[k], Math.Min(b[k], c[k])) > box.Max[k]) return;
            nearList.Add(o);
        }

        // the closed boxes of two triangles meet (a necessary condition for any common point)
        static bool boxes_meet(ref Triangle3d A, ref Triangle3d B)
        {
            for (int k = 0; k < 3; ++k) {
                double amin = Math.Min(A.V0[k], Math.Min(A.V1[k], A.V2[k])), amax = Math.Max(A.V0[k], Math.Max(A.V1[k], A.V2[k]));
                double bmin = Math.Min(B.V0[k], Math.Min(B.V1[k], B.V2[k])), bmax = Math.Max(B.V0[k], Math.Max(B.V1[k], B.V2[k]));
                if (amax < bmin || bmax < amin) return false;
            }
            return true;
        }

        bool TetTouchesInput(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            return MeshTetrahedralization.Refiner.TetTouchesMesh(InputTree, a, b, c, d);
        }

        // ---- vertex insertion: edge split + smoothing of the new vertex, kept only if the neighbourhood improves ----

        /// <summary>
        /// edge splits (interior edges, and boundary edges split in place: the two boundary triangles on the edge become four in their
        /// planes, so the boundary as a point set moves only by the rounding of the midpoint, checked exactly). A split is undone unless
        /// the smallest dihedral angle at the edge's neighbourhood strictly increases after smoothing the new vertex.
        /// </summary>
        internal bool Insertion = false;
        /// <summary> smoothing steps of a new vertex before the split is judged </summary>
        internal int SmoothIterations = 8;

        // failed splits: the tet and the versions of the vertices of its 1-ring at the time (a retry fails again unless one changed)
        readonly Dictionary<Index4i, long> splitFailed = new Dictionary<Index4i, long>();
        long ring_signature(Index4i t)
        {
            long h = 17;
            var seen = new HashSet<int>();
            for (int i = 0; i < 4; ++i) foreach (int id in vtets[t[i]]) { Index4i q = tets[id]; for (int k = 0; k < 4; ++k) if (seen.Add(q[k])) h = h * 31 + ((long)q[k] << 20) + ver_of(q[k]); }
            return h;
        }

        bool try_split_tet(int id)
        {
            Index4i t = tets[id];
            long sig = ring_signature(t);
            if (splitFailed.TryGetValue(t, out long s0) && s0 == sig) return false;
            bool r = try_split_tet_core(t);
            if (!r) splitFailed[t] = ring_signature(t);
            return r;
        }

        bool try_split_tet_core(Index4i t)
        {
            var e = new List<(double, int, int)>();
            for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) e.Add((-pos[t[i]].DistanceSquared(pos[t[j]]), t[i], t[j]));
            e.Sort();                                       // longest first
            foreach (var (_, a, b) in e) if (try_split(a, b)) return true;
            return false;
        }

        bool try_split(int a, int b)
        {
            bool be = is_bedge(a, b);
            if (be && (InputTree == null || !(BoundarySlideLimit > 0))) return false;
            var ringT = new List<int>();
            foreach (int id in vtets[a]) if (idx2(tets[id], b)) ringT.Add(id);
            if (ringT.Count == 0) return false;
            // the neighbourhood: tets at a, b and the ring vertices
            var nbSet = new HashSet<int> { a, b };
            var nb = new List<int> { a, b };
            foreach (int id in ringT) { Index4i t = tets[id]; for (int i = 0; i < 4; ++i) if (nbSet.Add(t[i])) nb.Add(t[i]); }
            double old = nbmin(nb);
            // what the undo restores besides the journal: the order of the tet lists at the neighbourhood (later sums run over
            // them), and the vertices' versions (a refused split must not look like a change to the memo)
            var vtetsSnap = new List<(int, List<int>, int)>();
            foreach (int x in nb) vtetsSnap.Add((x, new List<int>(vtets[x]), ver_of(x)));
            Vector3d m = SplitPointForTest != null ? SplitPointForTest(pos[a], pos[b]) : 0.5 * (pos[a] + pos[b]);
            // the new vertex. Its slide cap is measured from the midpoint of the edge's first positions, which lies on the wrap surface
            // (up to rounding): boundary edges only ever get split, so each lies in one wrap triangle; and the vertex starts within the
            // cap of there (the mean of two vertices within the cap of theirs)
            if (cell == 0 && be) build_grid();
            int v = pos.Count;
            Vector3d om = 0.5 * (orig[a] + orig[b]);
            // the rounding of om (it is on the wrap only up to that): taken off the new vertex's cap, so that its distance to the wrap
            // stays within BoundarySlideLimit; a vertex that starts beyond its cap is not inserted
            double margin = Math.Max(slide_margin(a), slide_margin(b)) + 4 * MathUtil.Epsilon * (orig[a].MaxAbs + orig[b].MaxAbs);
            if (be && !(m.Distance(om) <= BoundarySlideLimit - margin)) return false;
            pos.Add(m); orig.Add(om); vtets.Add(new List<int>()); vfaces.Add(new List<int>());
            while (slideMargin.Count < v) slideMargin.Add(0);
            slideMargin.Add(margin);
            boundaryVertex.Add(be);
            tetLog = new List<(int, Index4i, bool, bool)>();
            gridLog = new List<List<int>>();
            var vfacesSnap = new List<(int, List<int>)>();
            var countsBefore = new Dictionary<string, long>(Counts);
            var faceLog = new List<(int f, Index3i t)>();
            int nfaces0 = bfaces.Count;
            bool ok = true, refusedInput = false;
            foreach (int id in ringT) {
                Index4i t = tets[id];
                remove_tet(id);
                Index4i t1 = t, t2 = t;
                for (int i = 0; i < 4; ++i) { if (t1[i] == a) t1[i] = v; if (t2[i] == b) t2[i] = v; }
                if (quality(t1.a, t1.b, t1.c, t1.d) < 0 || quality(t2.a, t2.b, t2.c, t2.d) < 0) ok = false;
                add_tet(t1); add_tet(t2);
            }
            if (ok && be) {
                // the two boundary triangles on (a, b) become four; the new ones lie in the old ones' planes up to the rounding of m:
                // checked exactly as a move of v from the old triangles
                foreach (int f in new List<int>(vfaces[a])) {
                    Index3i t = bfaces[f];
                    if (!(t.a == b || t.b == b || t.c == b)) continue;
                    faceLog.Add((f, t));
                    for (int j = 0; j < 3; ++j) if (!vfacesSnap.Exists(e => e.Item1 == t[j])) vfacesSnap.Add((t[j], new List<int>(vfaces[t[j]])));
                    boundaryFaces.Remove(key(t.a, t.b, t.c));
                    for (int j = 0; j < 3; ++j) vfaces[t[j]].Remove(f);
                    Index3i f1 = t, f2 = t;
                    for (int j = 0; j < 3; ++j) { if (f1[j] == a) f1[j] = v; if (f2[j] == b) f2[j] = v; }
                    bfaces[f] = f1; add_face(f);
                    bfaces.Add(f2); add_face(bfaces.Count - 1);
                    int c = t.a != a && t.a != b ? t.a : t.b != a && t.b != b ? t.b : t.c;
                    if (ok && TetTouchesInput(pos[a], pos[b], pos[c], m)) { ok = false; refusedInput = true; }
                }
                if (ok) ok = faces_ok(v);
            }
            if (ok) {
                // smooth the new vertex (and its neighbours' tets by edge removal around it)
                for (int it = 0; it < SmoothIterations; ++it) {
                    bool moved = be ? try_smooth_boundary(v) : try_smooth(v);
                    if (!moved) break;
                }
                foreach (int x in nb) try_edge_removal(v, x);
                nb.Add(v);
                double now = nbmin(nb);
                ok = now > old || IgnoreQualityForTest;
            }
            if (ok) {
                Counts["insertions"]++;
                tetLog = null; gridLog = null;
                return true;
            }
            // undo, in reverse: tets, alive and the free stack exactly as before
            var tl = tetLog; tetLog = null;
            for (int k = tl.Count - 1; k >= 0; --k) {
                var (id, t, added, fromFree) = tl[k];
                for (int i = 0; i < 4; ++i) { if (added) vtets[t[i]].Remove(id); else vtets[t[i]].Add(id); }
                if (added) {
                    alive[id] = false;
                    if (fromFree) free.Push(id);
                    else { tets.RemoveAt(tets.Count - 1); alive.RemoveAt(alive.Count - 1); }   // the last appended id
                } else {
                    tets[id] = t; alive[id] = true;
                    free.Pop();                                     // remove_tet pushed id last among what is still on the stack
                }
            }
            foreach (var (x, l, vv) in vtetsSnap) { vtets[x].Clear(); vtets[x].AddRange(l); ver[x] = vv; }
            var gl = gridLog; gridLog = null;
            for (int k = gl.Count - 1; k >= 0; --k) gl[k].RemoveAt(gl[k].Count - 1);
            if (be) {
                for (int f = bfaces.Count - 1; f >= nfaces0; --f) { Index3i t = bfaces[f]; boundaryFaces.Remove(key(t.a, t.b, t.c)); for (int j = 0; j < 3; ++j) vfaces[t[j]].Remove(f); }
                bfaces.RemoveRange(nfaces0, bfaces.Count - nfaces0);
                foreach (var (f, t) in faceLog) {
                    Index3i cur = bfaces[f]; boundaryFaces.Remove(key(cur.a, cur.b, cur.c));
                    bfaces[f] = t; boundaryFaces.Add(key(t.a, t.b, t.c));
                }
                foreach (var (x, l) in vfacesSnap) { vfaces[x].Clear(); vfaces[x].AddRange(l); }
            }
            pos.RemoveAt(v); orig.RemoveAt(v); vtets.RemoveAt(v); vfaces.RemoveAt(v); boundaryVertex.RemoveAt(v); slideMargin.RemoveAt(v);
            // the counts of the undone operations go, the refusal stays
            Counts.Clear(); foreach (var kv in countsBefore) Counts[kv.Key] = kv.Value;
            if (refusedInput) Counts["split_refused_input"]++;
            return false;
        }

        // per vertex: the part of BoundarySlideLimit taken off for the rounding of its first position (0 for wrap vertices)
        readonly List<double> slideMargin = new List<double>();
        double slide_margin(int v) { return v < slideMargin.Count ? slideMargin[v] : 0; }
        double slide_limit(int v) { return BoundarySlideLimit - slide_margin(v); }

        double nbmin(List<int> nb)
        {
            double q = 180;
            var seen = new HashSet<int>();
            foreach (int x in nb) foreach (int id in vtets[x]) if (seen.Add(id)) q = Math.Min(q, quality(id));
            return q;
        }

        void add_face(int f)
        {
            Index3i t = bfaces[f];
            boundaryFaces.Add(key(t.a, t.b, t.c));
            for (int j = 0; j < 3; ++j) vfaces[t[j]].Add(f);
            if (cell == 0) return;            // the grid is built later from orig (which has the new vertex)
            AxisAlignedBox3d b = reach_box(f);
            if (cell_count(b) > MaxCellsPerFace) { bigFaces.Add(f); gridLog?.Add(bigFaces); }
            else foreach (var k in cells(b)) { if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(); l.Add(f); gridLog?.Add(l); }
        }

        // the faces at v against the other boundary faces near them (exact)
        bool faces_ok(int v)
        {
            foreach (int f in vfaces[v]) {
                Index3i ia = bfaces[f];
                var A = new Triangle3d(pos[ia.a], pos[ia.b], pos[ia.c]);
                var box = new AxisAlignedBox3d(A.V0); box.Contain(A.V1); box.Contain(A.V2);
                List<int> seen = near_faces(box);
                foreach (int o in seen) {
                    if (o == f || o >= bfaces.Count) continue;
                    Index3i ib = bfaces[o];
                    if (!boundaryFaces.Contains(key(ib.a, ib.b, ib.c))) continue;
                    var B = new Triangle3d(pos[ib.a], pos[ib.b], pos[ib.c]);
                    if (boxes_meet(ref A, ref B) && TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, ib, ref B)) return false;
                }
            }
            return true;
        }

        // ---- edge contraction ----

        /// <summary> edge contraction (merging an end of an edge of a bad tet into the other end) </summary>
        internal bool Contraction = false;

        // the edges of tet id, shortest first; each tried in both directions
        bool try_contract_tet(int id)
        {
            Index4i t = tets[id];
            var e = new List<(double, int, int)>();
            for (int i = 0; i < 4; ++i) for (int j = i + 1; j < 4; ++j) e.Add((pos[t[i]].DistanceSquared(pos[t[j]]), t[i], t[j]));
            e.Sort();
            foreach (var (_, a, b) in e) {
                if (try_contract(a, b)) return true;
                if (try_contract(b, a)) return true;
            }
            return false;
        }

        bool is_bedge(int a, int b)
        {
            foreach (int f in vfaces[a]) { Index3i t = bfaces[f]; if (t.a == b || t.b == b || t.c == b) return true; }
            return false;
        }

        /// <summary>
        /// merge the interior vertex u into w (w keeps its position; the boundary does not change, u has no boundary facet). The link
        /// condition (Dey, Edelsbrunner, Guha, Nekhayev 1999) keeps the complex a triangulation: every vertex, edge and triangle in the
        /// links of both u and w is in the link of the edge (u, w). Every new tet is exactly positive, and the smallest dihedral angle
        /// of the changed tets (u's star) strictly increases.
        /// </summary>
        bool try_contract(int u, int w)
        {
            lastContract = "boundary";
            if (boundaryVertex[u]) return false;
            lastContract = "link";
            if (!link_ok(u, w)) return false;
            List<int> su = vtets[u];
            lastContract = "not_better";
            double old = 180;
            foreach (int id in su) old = Math.Min(old, quality(id));
            double q = 180;
            foreach (int id in su) {
                Index4i t = tets[id];
                if (idx2(t, w)) continue;
                Index4i nt = t; for (int i = 0; i < 4; ++i) if (nt[i] == u) nt[i] = w;
                q = Math.Min(q, quality(nt.a, nt.b, nt.c, nt.d));
                if (!(q > old) && !IgnoreQualityForTest) return false;
                if (q < 0) return false;
            }
            lastContract = "ok";
            if (dry) return true;
            var changed = new List<Index4i>();
            foreach (int id in new List<int>(su)) {
                Index4i t = tets[id];
                remove_tet(id);
                if (idx2(t, w)) continue;
                for (int i = 0; i < 4; ++i) if (t[i] == u) t[i] = w;
                changed.Add(t);
            }
            foreach (Index4i t in changed) add_tet(t);
            Counts["contractions"]++;
            return true;
        }

        /// <summary>
        /// the link condition of the edge (u, w) for an interior u (Dey, Edelsbrunner, Guha, Nekhayev 1999): every vertex, edge and
        /// triangle in the links of both u and w is in the link of (u, w). False if (u, w) is not an edge.
        /// </summary>
        bool link_ok(int u, int w)
        {
            List<int> su = vtets[u], sw = vtets[w];
            var lkuw = new HashSet<int>();
            var lkuwE = new HashSet<(int, int)>();
            int nEdgeTets = 0;
            foreach (int id in su) {
                if (!idx2(tets[id], w)) continue;
                nEdgeTets++;
                others(tets[id], u, w, out int c, out int d); lkuw.Add(c); lkuw.Add(d); lkuwE.Add(c < d ? (c, d) : (d, c));
            }
            if (nEdgeTets == 0) return false;
            var nw = new HashSet<int>(); var ew = new HashSet<(int, int)>(); var fw = new HashSet<(int, int, int)>();
            foreach (int id in sw) {
                Index4i t = tets[id]; var o = new List<int>(3);
                for (int i = 0; i < 4; ++i) if (t[i] != w) o.Add(t[i]);
                foreach (int x in o) nw.Add(x);
                for (int i = 0; i < 3; ++i) for (int j = i + 1; j < 3; ++j) ew.Add(o[i] < o[j] ? (o[i], o[j]) : (o[j], o[i]));
                fw.Add(key(o[0], o[1], o[2]));
            }
            foreach (int id in su) {
                Index4i t = tets[id];
                if (idx2(t, w)) continue;
                var o = new List<int>(3);
                for (int i = 0; i < 4; ++i) if (t[i] != u) o.Add(t[i]);
                foreach (int x in o) if (nw.Contains(x) && !lkuw.Contains(x)) return false;
                for (int i = 0; i < 3; ++i) for (int j = i + 1; j < 3; ++j) { var k = o[i] < o[j] ? (o[i], o[j]) : (o[j], o[i]); if (ew.Contains(k) && !lkuwE.Contains(k)) return false; }
                if (fw.Contains(key(o[0], o[1], o[2]))) return false;
            }
            return true;
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
            lastDp = best[0, m - 1];
            if (!(best[0, m - 1] > old)) return false;
            if (dry) return true;
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
            if (!(q > old)) { lastFace = q < 0 ? "invalid" : "not_better"; return false; }
            if (dry) return true;
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
            if (ActiveSet) {
                Vector3d d = active_direction(v, old, eps);
                double dl = d.Length;
                if (dl > 0) foreach (double s in new[] { 0.25, 0.125, 0.0625, 0.03125, 0.015625, 0.0078125 }) candidates.Add(p0 + (s * h / dl) * d);
            }
            // and the centroid of the star's other vertices (Laplacian)
            Vector3d cen = Vector3d.Zero; int nc = 0;
            foreach (int id in star) { Index4i t = tets[id]; for (int i = 0; i < 4; ++i) if (t[i] != v) { cen += pos[t[i]]; nc++; } }
            candidates.Add(cen / nc);
            foreach (Vector3d p in candidates) {
                pos[v] = p;
                double q = 180;
                foreach (int id in star) { q = Math.Min(q, quality(id)); if (q <= old) break; }
                if (q > old) { if (dry) { pos[v] = p0; return true; } Counts["smooth_moves"]++; bump(v); return true; }
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
