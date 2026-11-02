// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    // Prototype of option A of the snap-rounding experiment (docs/design/SAFE_ROUNDING.md sections 3, 6 and 7): the
    // expansion stage of Milenkovic and Sacks ("Geometric rounding and feature separation in meshes", CAD 2019, section
    // 4), extended to the defects SafeRounding leaves. Their expansion separates disjoint features (vertex-triangle,
    // edge-edge pairs that share no vertex) of an intersection-free mesh by linear programs over the vertex
    // displacements; what SafeRounding leaves are degenerate triangles (a corner within about 1e-18 of the diagonal of the
    // opposite edge: no disjoint features) and intersecting pairs. So the linear program here also keeps every triangle
    // around a defect above the degenerate area, along a reference normal (the untangling of Freitag and Plassmann,
    // "Local optimization-based simplicial mesh untangling and improvement", IJNME 49, 2000, which maximizes the
    // smallest signed area by a linear program; Milenkovic and Sacks name it as a basis), and separates the
    // vertex-disjoint pairs near it by a slab (all corners of one triangle on one side of a plane, those of the other on
    // the other side, at least dmin apart: exact for a fixed direction). Sequential: the area constraints are
    // linearized, re-linearized at the new positions up to a few times, in a box of displacements that grows towards the
    // drift left. The result is proposed as one edit (all moved vertices at once) through improves(): applied only if it
    // adds no intersection and no degenerate triangle and removes at least one, checked exactly; its bound is the
    // largest distance a vertex moves (every point of a triangle moves by at most the largest move of its corners).
    // Internal and off unless Separate() is called: nothing changes for the existing results.
    public partial class MeshRoundingRepair
    {
        /// <summary> of the edits: separations applied (Separate) </summary>
        internal int Separations;
        /// <summary> linear programs solved by Separate, and those that reached their target </summary>
        internal int SeparationPrograms, SeparationTargetsReached;
        /// <summary> diagnostics of Separate (the experiment), or null </summary>
        internal Action<string> SeparationLog;

        // the area a triangle around a defect must reach, in units of the degenerate area (a margin for the
        // linearization and the rounding of the new positions)
        const double SeparationAreaMargin = 4;
        // trust regions, as fractions of the drift left
        static readonly double[] SeparationBoxes = { 1.0 / 1024, 1.0 / 64, 1.0 / 8, 1.0 / 2 };
        // linearizations per box
        const int SeparationIterations = 6;
        /// <summary> passes of Separate over what is left (each followed by the passes of Apply, MaxPasses) </summary>
        internal int SeparationPasses = 4;

        /// <summary>
        /// after Apply(): separate what is left (see the comment above), then the passes again; updates the counts and
        /// returns IsClean
        /// </summary>
        internal bool Separate()
        {
            Separations = SeparationPrograms = SeparationTargetsReached = 0;
            // (without Apply there is no state to continue from: not a silent success, independent review)
            if (drift == null)
                throw new InvalidOperationException("MeshRoundingRepair.Separate: call Apply() first");
            if (Mesh.TriangleCount == 0 || !(maxLength > 0))
                return IsClean;
            List<Index2i> pairs = intersecting_pairs();
            List<int> bad = degenerate_triangles();
            for (int pass = 0; pass < SeparationPasses && (pairs.Count > 0 || bad.Count > 0); ++pass) {
                int before = Separations;
                foreach (List<int> cluster in defect_clusters(pairs, bad)) {
                    bool live = false;
                    foreach (int t in cluster)
                        live |= Mesh.IsTriangle(t);
                    if (live)
                        try_separate(cluster);
                }
                pairs = intersecting_pairs();
                bad = degenerate_triangles();
                if (Separations == before)
                    break;
                // (what the separation made possible: contractions of what is now short, flips)
                repair_passes(ref pairs, ref bad);
            }
            RemainingIntersections = pairs.Count;
            RemainingDegenerate = bad.Count;
            int at = bad.Count > 0 ? bad[0] : pairs.Count > 0 ? pairs[0].a : -1;
            RemainingDefectAt = (at >= 0 && Mesh.IsTriangle(at)) ? Mesh.GetTriCentroid(at) : Vector3d.Zero;
            return IsClean;
        }

        // the defect triangles (degenerate, or in an intersecting pair) grouped by shared vertices and pairs
        List<List<int>> defect_clusters(List<Index2i> pairs, List<int> bad)
        {
            var parent = new Dictionary<int, int>();
            Func<int, int> find = null;
            find = (x) => {
                int p = parent[x];
                if (p == x) return x;
                int r = find(p);
                parent[x] = r;
                return r;
            };
            Action<int, int> join = (x, y) => {
                int a = find(x), b = find(y);
                if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
            };
            foreach (int t in bad) parent[t] = t;
            foreach (Index2i p in pairs) { parent[p.a] = p.a; parent[p.b] = p.b; }
            foreach (Index2i p in pairs) join(p.a, p.b);
            // (triangles sharing a vertex)
            var byVertex = new Dictionary<int, int>();
            foreach (int t in new List<int>(parent.Keys)) {
                Index3i tv = Mesh.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    int other;
                    if (byVertex.TryGetValue(tv[k], out other)) join(t, other);
                    else byVertex[tv[k]] = t;
                }
            }
            var groups = new SortedDictionary<int, List<int>>();
            var keys = new List<int>(parent.Keys);
            keys.Sort();
            foreach (int t in keys) {
                int r = find(t);
                List<int> g;
                if (groups.TryGetValue(r, out g) == false) groups[r] = g = new List<int>();
                g.Add(t);
            }
            return new List<List<int>>(groups.Values);
        }

        // may v move in a separation (not on the boundary, no bowtie, not at a position shared with another vertex)
        bool separation_movable(int v)
        {
            if (Mesh.IsBoundaryVertex(v) || Mesh.IsBowtieVertex(v))
                return false;
            List<int> there;
            if (atPosition != null && atPosition.TryGetValue(key(Mesh.GetVertex(v)), out there) && there.Count > 1)
                return false;
            return true;
        }

        bool try_separate(List<int> cluster)
        {
            // the vertices of the cluster, then with their one-ring
            var verts = new List<int>();
            var inV = new HashSet<int>();
            foreach (int t in cluster) {
                if (Mesh.IsTriangle(t) == false) continue;
                Index3i tv = Mesh.GetTriangle(t);
                for (int k = 0; k < 3; ++k)
                    if (inV.Add(tv[k]) && separation_movable(tv[k]))
                        verts.Add(tv[k]);
            }
            for (int ring = 0; ring < 2; ++ring) {
                if (ring == 1) {
                    foreach (int v in new List<int>(verts))
                        foreach (int u in Mesh.VtxVerticesItr(v))
                            if (inV.Add(u) && separation_movable(u))
                                verts.Add(u);
                }
                if (verts.Count == 0 || verts.Count > 3 * MaxStripTriangles)
                    return false;
                verts.Sort();
                if (separate(verts))
                    return true;
            }
            return false;
        }

        // one constraint of the program: g0 + a · (displacements of the variable vertices) >= s h
        struct SepRow
        {
            public int[] V;           // the vertices (ids)
            public Vector3d[] A;      // their gradients
            public double G0, H;
        }

        bool separate(List<int> verts)
        {
            var index = new Dictionary<int, int>();
            for (int i = 0; i < verts.Count; ++i) index[verts[i]] = i;
            // the triangles that move, and the largest drift among their vertices
            var tris = new List<int>();
            var inT = new HashSet<int>();
            double most = 0;
            foreach (int v in verts)
                foreach (int t in Mesh.VtxTrianglesItr(v))
                    if (inT.Add(t)) {
                        tris.Add(t);
                        Index3i tv = Mesh.GetTriangle(t);
                        for (int k = 0; k < 3; ++k) most = Math.Max(most, drift[tv[k]]);
                    }
            tris.Sort();
            double left = maxLength - most;
            if (!(left > 0))
                return false;
            // reference normals: a triangle's own (unless tiny), else the sum of those of the larger triangles at its
            // corners, else of all larger triangles that move, else the area-weighted sum of all that move (in a dense
            // cluster every neighbour of a sliver can be a sliver)
            var normal = new Dictionary<int, Vector3d>();
            Vector3d around = Vector3d.Zero, weighted = Vector3d.Zero;
            foreach (int t in tris) {
                Vector3d cr = cross_of(t);
                weighted += cr;
                if (area(t) >= OrientedArea * areaEps)
                    around += unit(cr);
            }
            Vector3d fallback = unit(around) != Vector3d.Zero ? unit(around) : unit(weighted);
            foreach (int t in tris) {
                Vector3d n = unit(cross_of(t));
                if (area(t) < OrientedArea * areaEps) {
                    Vector3d sum = Vector3d.Zero;
                    Index3i tv = Mesh.GetTriangle(t);
                    for (int k = 0; k < 3; ++k)
                        foreach (int u in Mesh.VtxTrianglesItr(tv[k]))
                            if (area(u) >= OrientedArea * areaEps)
                                sum += unit(cross_of(u));
                    n = unit(sum) != Vector3d.Zero ? unit(sum) : fallback;
                }
                if (n == Vector3d.Zero)
                    return false;
                normal[t] = n;
            }
            double largest = 0;
            foreach (int v in verts) largest = Math.Max(largest, Mesh.GetVertex(v).MaxAbs);
            // the separation of the slabs: a few ulps, so that rounding the new positions keeps them apart
            double dmin = 8 * ulp(Math.Max(largest, 1e-300));
            Vector3d[] x0 = new Vector3d[verts.Count];
            for (int i = 0; i < verts.Count; ++i) x0[i] = Mesh.GetVertex(verts[i]);
            foreach (double fraction in SeparationBoxes) {
                // (each coordinate within R: a vertex moves at most R √3)
                double R = fraction * left / Math.Sqrt(3);
                var x = (Vector3d[])x0.Clone();
                bool reached = false;
                for (int it = 0; it < SeparationIterations && reached == false; ++it) {
                    moveSet = new Dictionary<int, Vector3d>();
                    for (int i = 0; i < verts.Count; ++i) moveSet[verts[i]] = x[i];
                    List<SepRow> rows = separation_rows(verts, index, tris, normal, R, dmin);
                    if (rows == null) { moveSet = null; return false; }
                    double[] d = solve_separation(rows, index, x0, x, R, out double s);
                    moveSet = null;
                    if (d == null) { SeparationLog?.Invoke($"verts {verts.Count} rows {rows.Count}: LP failed"); break; }
                    for (int i = 0; i < verts.Count; ++i)
                        x[i] = x0[i] + new Vector3d(d[3 * i], d[3 * i + 1], d[3 * i + 2]);
                    // (the constraints at the new positions)
                    moveSet = new Dictionary<int, Vector3d>();
                    for (int i = 0; i < verts.Count; ++i) moveSet[verts[i]] = x[i];
                    List<SepRow> after = separation_rows(verts, index, tris, normal, R, dmin);
                    reached = s >= 1 && satisfied(after);
                    moveSet = null;
                    if (SeparationLog != null) {
                        double worst = double.PositiveInfinity; int nr = after == null ? -1 : after.Count;
                        if (after != null) foreach (SepRow r in after) worst = Math.Min(worst, r.G0 / r.H);
                        SeparationLog($"verts {verts.Count} tris {tris.Count} rows {rows.Count}/{nr} R/left {R * Math.Sqrt(3) / left:G2} it {it} s {s:G4} worst g/h after {worst:G4}");
                    }
                }
                if (reached)
                    SeparationTargetsReached++;
                if (try_move_all(verts, x, tris)) {
                    SeparationLog?.Invoke("  applied");
                    return true;
                }
                SeparationLog?.Invoke("  rejected by improves");
            }
            return false;
        }

        static bool satisfied(List<SepRow> rows)
        {
            if (rows == null) return false;
            foreach (SepRow r in rows)
                if (!(r.G0 >= 0.9 * r.H)) return false;
            return true;
        }

        Vector3d cross_of(int t)
        {
            Index3i tv = Mesh.GetTriangle(t);
            Vector3d a = pos(tv.a);
            return (pos(tv.b) - a).Cross(pos(tv.c) - a);
        }

        // v / |v|, or 0 (not Normalized: its threshold is absolute)
        static Vector3d unit(Vector3d v)
        {
            double l = v.Length;
            return (l > 0 && l < double.PositiveInfinity) ? v / l : Vector3d.Zero;
        }

        // the constraints at the current positions (pos(), with moveSet): areas of the moving triangles along their
        // reference normals, and slabs between the moving triangles and the vertex-disjoint triangles near them
        List<SepRow> separation_rows(List<int> verts, Dictionary<int, int> index, List<int> tris, Dictionary<int, Vector3d> normal, double R, double dmin)
        {
            var rows = new List<SepRow>();
            double target = 2 * SeparationAreaMargin * areaEps;       // (of the cross product: twice the area)
            foreach (int t in tris) {
                Index3i tv = Mesh.GetTriangle(t);
                Vector3d a = pos(tv.a), b = pos(tv.b), c = pos(tv.c), n = normal[t];
                Vector3d e1 = b - a, e2 = c - a;
                // d(n · (e1 × e2)) = Δb · (e2 × n) + Δc · (n × e1) - Δa · (e2 × n + n × e1)
                Vector3d gb = e2.Cross(n), gc = n.Cross(e1);
                rows.Add(new SepRow { V = new[] { tv.a, tv.b, tv.c }, A = new[] { -(gb + gc), gb, gc }, G0 = n.Dot(e1.Cross(e2)), H = target });
            }
            // the pairs: within 2 √3 R + dmin of a moving triangle (farther ones cannot come that close)
            double reach = 2 * Math.Sqrt(3) * R + dmin;
            var seen = new HashSet<long>();
            foreach (int t in tris) {
                Index3i tv = Mesh.GetTriangle(t);
                AxisAlignedBox3d bx = box(tv);
                bx.Expand(reach);
                near(bx, scratch);
                foreach (int u in new List<int>(scratch)) {
                    if (u == t) continue;
                    long k = ((long)Math.Min(t, u) << 32) | (uint)Math.Max(t, u);
                    if (seen.Add(k) == false) continue;
                    Index3i uv = Mesh.GetTriangle(u);
                    if (shares_position(tv, uv)) continue;
                    // (an exactly degenerate triangle: its intersections do not count, the area rows lift it)
                    Triangle3d T0 = triangle(tv), U0 = triangle(uv);
                    if (ExactTriangleIntersection.IsDegenerate(ref T0) || ExactTriangleIntersection.IsDegenerate(ref U0)) continue;
                    Vector3d dir;
                    if (separation_direction(tv, uv, reach, out dir) == false) continue;
                    if (dir == Vector3d.Zero) return null;
                    for (int i = 0; i < 3; ++i)
                        for (int j = 0; j < 3; ++j)
                            rows.Add(new SepRow { V = new[] { tv[i], uv[j] }, A = new[] { dir, -dir }, G0 = dir.Dot(pos(tv[i]) - pos(uv[j])), H = dmin });
                }
            }
            // rows that cannot be violated within the box are left out (a vertex moves by at most 2 √3 R from the current
            // positions): |a·Δ| <= Σ |a_k| 2 √3 R; this bounds the linear model, the exact check decides anyway
            var kept = new List<SepRow>(rows.Count);
            double reachMove = 2 * Math.Sqrt(3) * R;
            foreach (SepRow r in rows) {
                double most = 0;
                for (int k = 0; k < r.V.Length; ++k)
                    if (index.ContainsKey(r.V[k]))
                        most += r.A[k].Length * reachMove;
                if (!(r.G0 - most >= r.H))
                    kept.Add(r);
            }
            return kept;
        }

        // do two triangles share a vertex, or a position (SharedPositionsTouch)
        bool shares_position(Index3i t, Index3i u)
        {
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    if (t[i] == u[j] || pos(t[i]) == pos(u[j]))
                        return true;
            return false;
        }

        // the direction from u to t along which to separate them, if they are closer than reach (false: farther). For
        // an intersecting pair (exact), the normal of the larger triangle, towards the side where the smaller one
        // reaches farthest; else from the nearest points of u to those of t
        bool separation_direction(Index3i t, Index3i u, double reach, out Vector3d dir)
        {
            dir = Vector3d.Zero;
            Triangle3d T = triangle(t), U = triangle(u);
            bool degT = ExactTriangleIntersection.IsDegenerate(ref T), degU = ExactTriangleIntersection.IsDegenerate(ref U);
            if (degT == false && degU == false && intersects(t, ref T, u, ref U)) {
                bool tLarger = area(t) >= area(u);
                Triangle3d big = tLarger ? T : U, small = tLarger ? U : T;
                Vector3d n = unit((big.V1 - big.V0).Cross(big.V2 - big.V0));
                double far = 0;
                for (int k = 0; k < 3; ++k) {
                    double h = n.Dot(small[k] - big.V0);
                    if (Math.Abs(h) > Math.Abs(far)) far = h;
                }
                // (n points from big to small: from u to t if t is the small one)
                n = far >= 0 ? n : -n;
                dir = tLarger ? -n : n;
                return true;
            }
            // nearest points (in double: only the direction is taken from them, the result is checked exactly)
            Vector3d p, q;
            double dist = triangles_nearest(T, U, out p, out q);
            if (!(dist <= reach))
                return false;
            dir = unit(p - q);
            // (nearest points that coincide in double: the normal of the larger triangle, towards t)
            if (dir == Vector3d.Zero) {
                bool tLarger = area(t) >= area(u);
                Triangle3d big = tLarger ? T : U;
                Vector3d n = unit((big.V1 - big.V0).Cross(big.V2 - big.V0));
                Triangle3d other = tLarger ? U : T;
                double far = 0;
                for (int k = 0; k < 3; ++k) {
                    double h = n.Dot(other[k] - big.V0);
                    if (Math.Abs(h) > Math.Abs(far)) far = h;
                }
                n = far >= 0 ? n : -n;
                dir = tLarger ? -n : n;
            }
            return true;
        }

        // the nearest points of two triangles (in double; only a direction is taken from them). DistTriangle3Triangle3 has
        // absolute tolerances: it runs on the triangles moved to T.V0 and scaled by a power of two to a size of about 1,
        // so that 2^k scales give the same direction bit for bit
        static double triangles_nearest(Triangle3d T, Triangle3d U, out Vector3d p, out Vector3d q)
        {
            Vector3d o = T.V0;
            double extent = 0;
            for (int k = 0; k < 3; ++k) {
                extent = Math.Max(extent, (T[k] - o).MaxAbs);
                extent = Math.Max(extent, (U[k] - o).MaxAbs);
            }
            double s = (extent > 0 && extent < double.PositiveInfinity) ? 1 / ulp(extent) * 2.220446049250313e-16 : 1;
            if (!(s > 0 && s < double.PositiveInfinity)) s = 1;
            var Tn = new Triangle3d((T.V0 - o) * s, (T.V1 - o) * s, (T.V2 - o) * s);
            var Un = new Triangle3d((U.V0 - o) * s, (U.V1 - o) * s, (U.V2 - o) * s);
            var d = new DistTriangle3Triangle3(Tn, Un).Compute();
            p = d.Triangle0Closest / s + o; q = d.Triangle1Closest / s + o;
            return p.Distance(q);
        }

        // the program: maximize Σ_k s_k - beta Σ|Δ| subject to g0_k + a_k · (Δ - Δcur) >= s_k h_k for every row k,
        // s_k <= 1, |Δ| <= R per coordinate (Δ the total displacement from x0, Δcur the current one). Each row has its
        // own s_k, so that a row that cannot reach its target (eg a pair that cannot be separated within the box) does
        // not hold back the others. s = the smallest s_k; null if it fails
        double[] solve_separation(List<SepRow> rows, Dictionary<int, int> index, Vector3d[] x0, Vector3d[] x, double R, out double s)
        {
            s = 0;
            int nv = x0.Length, K = rows.Count, nd = 6 * nv;
            int nvar = nd + K;                  // p, q (Δ = R (p - q)) per coordinate, then s'_k = s_k + S_k per row
            var cur = new double[3 * nv];
            for (int i = 0; i < nv; ++i) {
                Vector3d dc = x[i] - x0[i];
                cur[3 * i] = dc.x; cur[3 * i + 1] = dc.y; cur[3 * i + 2] = dc.z;
            }
            int m = K + nd + K;
            var A = new double[m, nvar];
            var b = new double[m];
            var c = new double[nvar];
            var shift = new double[K];
            for (int k = 0; k < K; ++k) {
                SepRow r = rows[k];
                // the linearized constraint at Δ = 0 (x0): g0 - a·Δcur; the shift S_k makes Δ = 0, s'_k = 0 feasible
                double g = r.G0, scale = r.H;
                for (int j = 0; j < r.V.Length; ++j) {
                    int i;
                    if (index.TryGetValue(r.V[j], out i) == false) continue;
                    g -= r.A[j].x * cur[3 * i] + r.A[j].y * cur[3 * i + 1] + r.A[j].z * cur[3 * i + 2];
                    for (int d = 0; d < 3; ++d)
                        scale = Math.Max(scale, Math.Abs(r.A[j][d] * R));
                }
                double S = Math.Max(0, -g / r.H);
                if (!(S < 1e300) || !(scale > 0) || !(scale < double.PositiveInfinity)) {
                    SeparationLog?.Invoke($"row {k}: g {g:G3} h {r.H:G3} scale {scale:G3}");
                    return null;
                }
                shift[k] = S;
                // -R a·(p - q) + h s'_k <= g0 - a·Δcur + S_k h, scaled to coefficients of about 1
                for (int j = 0; j < r.V.Length; ++j) {
                    int i;
                    if (index.TryGetValue(r.V[j], out i) == false) continue;
                    for (int d = 0; d < 3; ++d) {
                        double a = r.A[j][d] * R / scale;
                        A[k, 2 * (3 * i + d)] -= a;
                        A[k, 2 * (3 * i + d) + 1] += a;
                    }
                }
                A[k, nd + k] = r.H / scale;
                // (>= 0 by the choice of S_k, up to rounding)
                b[k] = Math.Max(0, (g + S * r.H) / scale);
                A[K + nd + k, nd + k] = 1;
                b[K + nd + k] = 1 + S;
                c[nd + k] = 1 / (1 + S);
            }
            for (int j = 0; j < nd; ++j) {
                A[K + j, j] = 1; b[K + j] = 1;
                c[j] = -1e-3 / nd;
            }
            // (a program that does not converge within a few times its size is given up: the dense tableau stalls on large
            // programs with rows of very different scales, see docs/design/SAFE_ROUNDING.md section 8)
            var lp = new DenseSimplex() { MaxPivots = 2 * (m + nvar) };
            SeparationPrograms++;
            DenseSimplex.Status status = lp.Solve(A, b, c);
            if (status != DenseSimplex.Status.Optimal) {
                SeparationLog?.Invoke($"LP {status} after {lp.Pivots} pivots, {K} rows");
                return null;
            }
            s = double.PositiveInfinity;
            for (int k = 0; k < K; ++k)
                s = Math.Min(s, lp.X[nd + k] - shift[k]);
            var delta = new double[3 * nv];
            for (int j = 0; j < 3 * nv; ++j)
                delta[j] = R * (lp.X[2 * j] - lp.X[2 * j + 1]);
            return delta;
        }

        // (for the tests: propose the vertices at the given positions as one separation edit, after Apply)
        internal bool propose_move(List<int> verts, Vector3d[] x)
        {
            var tris = new List<int>();
            var inT = new HashSet<int>();
            foreach (int v in verts)
                foreach (int t in Mesh.VtxTrianglesItr(v))
                    if (inT.Add(t)) tris.Add(t);
            tris.Sort();
            return try_move_all(verts, x, tris);
        }

        // propose all vertices at their new positions as one edit (see the comment at the top)
        bool try_move_all(List<int> verts, Vector3d[] x, List<int> tris)
        {
            double step = 0;
            bool any = false;
            for (int i = 0; i < verts.Count; ++i) {
                Vector3d old = Mesh.GetVertex(verts[i]);
                if (double.IsNaN(x[i].x + x[i].y + x[i].z) || double.IsInfinity(x[i].x + x[i].y + x[i].z))
                    return false;
                if (x[i] != old) any = true;
                step = Math.Max(step, x[i].Distance(old));
                // (SharedPositionsTouch: not onto the position of another vertex)
                List<int> there;
                if (atPosition != null && x[i] != old && atPosition.TryGetValue(key(x[i]), out there) && there.Count > 0)
                    return false;
            }
            if (any == false)
                return false;
            // (two moved vertices at one position)
            var positions = new HashSet<Vector3d>();
            foreach (Vector3d p in x)
                if (positions.Add(key(p)) == false)
                    return false;
            begin_edit();
            foreach (int t in tris) {
                removed.Add(t);
                added.Add(Mesh.GetTriangle(t));
                addedFrom.Add(removed.Count - 1);
            }
            moveSet = new Dictionary<int, Vector3d>();
            for (int i = 0; i < verts.Count; ++i) moveSet[verts[i]] = x[i];
            bool ok = improves(step);
            moveSet = null;
            if (ok == false)
                return false;
            for (int i = 0; i < verts.Count; ++i) {
                set_vertex(verts[i], x[i]);
                touched.Add(verts[i]);
            }
            foreach (int t in tris)
                mark_dirty(t);
            commit_drift();
            Separations++;
            return true;
        }
    }
}
