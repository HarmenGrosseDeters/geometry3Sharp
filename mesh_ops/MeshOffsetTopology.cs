// Copyright (c) geometry3Sharp contributors. Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The level-set side of the offset topology certificate (stage A2, docs/design/MESH_OFFSET_ENCLOSURE.md): every cell of
    /// MeshOffset's grid is proven zero-free (F one sign on the closed cell, by the A1 interval bounds and the 1-Lipschitz
    /// bound, on blocks of cells first), or certified ((S0)-(S3) on the enclosure of Theorem E, as exact signs), or listed.
    /// Also checks (N): the node signs the contouring used (float values, as DualContouring stores them) are the proven ones.
    /// Not here: the mesh side (H) of Theorem O, the face-rule agreement (P) (cells with a face of four crossings are listed),
    /// cells with more than one circle on their boundary (listed), refinement. Serial.
    /// </summary>
    internal class MeshOffsetTopology
    {
        readonly DMesh3 mesh;
        readonly DMeshAABBTree3 tree;
        readonly double d, h;
        readonly Vector3d origin;
        readonly int nx, ny, nz;
        readonly Func<long, double> fieldF;     // the node value the contouring used

        sbyte[] proof;     // per node: 0 not yet, +1 / -1 proven sign of F, 2 not provable

        public int ZeroFreeBlocks, ZeroFreeCells, CertifiedCells, NodeSignMismatches, Evaluations;
        public readonly List<Vector3i> Listed = new List<Vector3i>();
        public readonly Dictionary<string, int> ListedBy = new Dictionary<string, int>();
        public bool LevelSetCertified { get { return Listed.Count == 0 && NodeSignMismatches == 0; } }

        public MeshOffsetTopology(DMesh3 mesh, DMeshAABBTree3 tree, double d, Vector3d origin, double h, int nx, int ny, int nz, Func<long, double> fieldF)
        {
            this.mesh = mesh; this.tree = tree; this.d = d; this.origin = origin; this.h = h;
            this.nx = nx; this.ny = ny; this.nz = nz; this.fieldF = fieldF;
        }

        long idx(int i, int j, int k) { return i + (long)nx * (j + (long)ny * k); }
        Vector3d pos(int i, int j, int k) { return new Vector3d(origin.x + h * i, origin.y + h * j, origin.z + h * k); }

        // the sign DualContouring sees at a node (it stores sparse values as float; inside is > 0)
        int contour_sign(long n)
        {
            float v = (float)MathUtil.Clamp(fieldF(n), -float.MaxValue / 2, float.MaxValue / 2);
            return v > 0 ? 1 : -1;
        }

        public void Run()
        {
            proof = new sbyte[(long)nx * ny * nz];
            var stack = new Stack<(int, int, int, int, int, int)>();
            stack.Push((0, nx - 1, 0, ny - 1, 0, nz - 1));
            while (stack.Count > 0) {
                var (i0, i1, j0, j1, k0, k1) = stack.Pop();
                if (i1 <= i0 || j1 <= j0 || k1 <= k0) continue;
                int sigma = zero_free(pos(i0, j0, k0), pos(i1, j1, k1));
                if (sigma != 0) {
                    ZeroFreeBlocks++;
                    ZeroFreeCells += (i1 - i0) * (j1 - j0) * (k1 - k0);
                    // (N): every node of the block carries that sign in the contouring
                    for (int k = k0; k <= k1; ++k)
                        for (int j = j0; j <= j1; ++j)
                            for (int i = i0; i <= i1; ++i)
                                if (contour_sign(idx(i, j, k)) != sigma) NodeSignMismatches++;
                    continue;
                }
                int di = i1 - i0, dj = j1 - j0, dk = k1 - k0;
                if (di == 1 && dj == 1 && dk == 1) {
                    string why = certify_cell(i0, j0, k0);
                    if (why == null) CertifiedCells++;
                    else { Listed.Add(new Vector3i(i0, j0, k0)); int c; ListedBy[why] = ListedBy.TryGetValue(why, out c) ? c + 1 : 1; }
                    continue;
                }
                if (di >= dj && di >= dk) { int m = i0 + di / 2; stack.Push((i0, m, j0, j1, k0, k1)); stack.Push((m, i1, j0, j1, k0, k1)); }
                else if (dj >= dk) { int m = j0 + dj / 2; stack.Push((i0, i1, j0, m, k0, k1)); stack.Push((i0, i1, m, j1, k0, k1)); }
                else { int m = k0 + dk / 2; stack.Push((i0, i1, j0, j1, k0, m)); stack.Push((i0, i1, j0, j1, m, k1)); }
            }
        }

        // the centre of a box and an upper bound of the distance from it to every point of the box
        static void centre_radius(Vector3d lo, Vector3d hi, out Vector3d c, out double r)
        {
            c = 0.5 * (lo + hi);
            ExactInterval sq = ExactInterval.Of(0);
            for (int a = 0; a < 3; ++a) {
                ExactInterval e1 = ExactInterval.Of(hi[a]) - ExactInterval.Of(c[a]), e0 = ExactInterval.Of(c[a]) - ExactInterval.Of(lo[a]);
                double m = Math.Max(Math.Abs(e1.Hi), Math.Abs(e0.Hi));
                sq = sq + ExactInterval.Square(ExactInterval.Of(m));
            }
            r = sq.Hi <= 0 ? 0 : ExactInterval.Up(Math.Sqrt(sq.Hi));
        }

        // +1 / -1 if F = d - sd provably has that sign on the whole closed box (lo, hi), else 0
        int zero_free(Vector3d lo, Vector3d hi)
        {
            Vector3d c; double r;
            centre_radius(lo, hi, out c, out r);
            double sLo, sHi;
            MeshOffset.signed_distance_bounds(mesh, tree, c, out sLo, out sHi);
            Evaluations++;
            ExactInterval D = ExactInterval.Of(d), R = ExactInterval.Of(r);
            // sd in [sLo - r, sHi + r] on the box, so F in [d - sHi - r, d - sLo + r]
            double fLo = (D - ExactInterval.Of(sHi) - R).Lo, fHi = (D - ExactInterval.Of(sLo) + R).Hi;
            if (fLo > 0) return 1;
            if (fHi < 0) return -1;
            return 0;
        }

        // the proven sign of F at a node (0 if not provable)
        int node_sign(int i, int j, int k)
        {
            long n = idx(i, j, k);
            if (proof[n] == 0) {
                int s = zero_free(pos(i, j, k), pos(i, j, k));
                proof[n] = (sbyte)(s == 0 ? 2 : s);
            }
            return proof[n] == 2 ? 0 : proof[n];
        }

        // the 8 corners of a cell, bit 0 = +x, bit 1 = +y, bit 2 = +z; the 12 edges as corner pairs with their axis
        static readonly int[,] Edges = { { 0, 1, 0 }, { 2, 3, 0 }, { 4, 5, 0 }, { 6, 7, 0 }, { 0, 2, 1 }, { 1, 3, 1 }, { 4, 6, 1 }, { 5, 7, 1 }, { 0, 4, 2 }, { 1, 5, 2 }, { 2, 6, 2 }, { 3, 7, 2 } };

        // null if certified, else the reason it is listed
        string certify_cell(int i, int j, int k)
        {
            Vector3d lo = pos(i, j, k), hi = pos(i + 1, j + 1, k + 1);
            // (S0) and (N) at the corners
            int[] sg = new int[8];
            for (int q = 0; q < 8; ++q) {
                int a = i + (q & 1), b = j + ((q >> 1) & 1), c = k + ((q >> 2) & 1);
                sg[q] = node_sign(a, b, c);
                if (sg[q] == 0) return "S0 node";
                if (contour_sign(idx(a, b, c)) != sg[q]) { NodeSignMismatches++; return "node sign"; }
            }
            // clear, side, the upper bound U of u over the cell
            Vector3d cc; double r;
            centre_radius(lo, hi, out cc, out r);
            double sLo, sHi;
            MeshOffset.signed_distance_bounds(mesh, tree, cc, out sLo, out sHi);
            Evaluations++;
            double uLo = sLo > 0 ? sLo : (sHi < 0 ? -sHi : 0), uHi = Math.Max(Math.Abs(sLo), Math.Abs(sHi));
            if (!((ExactInterval.Of(uLo) - ExactInterval.Of(r)).Lo > 0)) return "not clear";
            double U = (ExactInterval.Of(uHi) + ExactInterval.Of(r)).Hi;
            double U2 = ExactInterval.Up(U * U);

            // candidates C(X): triangles whose box can be within U of the cell; their vertices
            var tris = new List<Vector3d>();   // 3 vertices per candidate triangle
            var trav = new DMeshAABBTree3.TreeTraversal() {
                NextBoxDF = (box, depth) => box_box_lower_sq(box.Min, box.Max, lo, hi) <= U2,
                NextTriangleF = (t) => {
                    Index3i tv = mesh.GetTriangle(t);
                    Vector3d a = mesh.GetVertex(tv.a), b = mesh.GetVertex(tv.b), c = mesh.GetVertex(tv.c);
                    Vector3d bmin = new Vector3d(Math.Min(a.x, Math.Min(b.x, c.x)), Math.Min(a.y, Math.Min(b.y, c.y)), Math.Min(a.z, Math.Min(b.z, c.z)));
                    Vector3d bmax = new Vector3d(Math.Max(a.x, Math.Max(b.x, c.x)), Math.Max(a.y, Math.Max(b.y, c.y)), Math.Max(a.z, Math.Max(b.z, c.z)));
                    if (box_box_lower_sq(bmin, bmax, lo, hi) <= U2) { tris.Add(a); tris.Add(b); tris.Add(c); }
                }
            };
            tree.DoTraversal(trav);
            if (tris.Count == 0) return "no candidates";

            // the crossing pattern from the proven signs: faces with four crossings and boundary circles
            int crossings = 0;
            int[] parent = new int[12];
            for (int e = 0; e < 12; ++e) parent[e] = e;
            Func<int, int> find = null;
            find = (x) => parent[x] == x ? x : (parent[x] = find(parent[x]));
            for (int axis = 0; axis < 3; ++axis)
                for (int side = 0; side < 2; ++side) {
                    // the face normal to axis at side: its 4 edges are those of the other two axes with that corner bit
                    var fe = new List<int>();
                    for (int e = 0; e < 12; ++e) {
                        if (Edges[e, 2] == axis) continue;
                        int c0 = Edges[e, 0], c1 = Edges[e, 1];
                        if (((c0 >> axis) & 1) == side && ((c1 >> axis) & 1) == side && sg[c0] != sg[c1]) fe.Add(e);
                    }
                    if (fe.Count == 4) return "four crossings";
                    if (fe.Count == 2) parent[find(fe[0])] = find(fe[1]);
                }
            var roots = new HashSet<int>();
            for (int e = 0; e < 12; ++e) if (sg[Edges[e, 0]] != sg[Edges[e, 1]]) { crossings++; roots.Add(find(e)); }
            if (roots.Count > 1) return "several circles";

            // (S1): per edge, its axis one-signed on the enclosure over the edge, else the edge is zero-free
            for (int e = 0; e < 12; ++e) {
                int axis = Edges[e, 2];
                Vector3d p0 = corner(lo, hi, Edges[e, 0]), p1 = corner(lo, hi, Edges[e, 1]);
                Vector3d a = Vector3d.Zero; a[axis] = 1;
                if (one_signed(a, tris, new[] { p0, p1 })) continue;
                if (!zero_free_split(p0, p1, 10)) return "S1 edge";
            }
            // (S2) / (S2'): per face an axis of the face, else an in-plane direction, else zero-free
            for (int axis = 0; axis < 3; ++axis)
                for (int side = 0; side < 2; ++side) {
                    int ka = (axis + 1) % 3, kb = (axis + 2) % 3;
                    Vector3d flo = lo, fhi = hi;
                    if (side == 0) fhi[axis] = lo[axis]; else flo[axis] = hi[axis];
                    Vector3d[] fz = { corner(flo, fhi, 0), corner(flo, fhi, 1 << ka), corner(flo, fhi, 1 << kb), corner(flo, fhi, (1 << ka) | (1 << kb)) };
                    Vector3d ea = Vector3d.Zero; ea[ka] = 1;
                    Vector3d eb = Vector3d.Zero; eb[kb] = 1;
                    if (one_signed(ea, tris, fz) || one_signed(eb, tris, fz)) continue;
                    Vector3d g = gradient_at(0.5 * (flo + fhi));
                    g[axis] = 0;
                    if (g.LengthSquared > 0 && one_signed(g, tris, fz)) continue;
                    if (!zero_free_split(flo, fhi, 8)) return "S2 face";
                }
            // (S3): a direction one-signed on the enclosure over the cell
            Vector3d[] cz = new Vector3d[8];
            for (int q = 0; q < 8; ++q) cz[q] = corner(lo, hi, q);
            Vector3d dir = gradient_at(cc);
            if (!(dir.LengthSquared > 0) || !one_signed(dir, tris, cz)) return "S3 cell";
            return null;
        }

        // the unit vector from the nearest input point to x (a direction to try; any double vector is valid)
        Vector3d gradient_at(Vector3d x)
        {
            int t = tree.FindNearestTriangle(x);
            if (t == DMesh3.InvalidID) return Vector3d.Zero;
            Vector3d q = x - MeshQueries.TriangleDistance(mesh, t, x).TriangleClosest;
            double l = q.Length;
            return l > 0 ? q / l : Vector3d.Zero;
        }

        // Theorem E' (tighter than E): for z in X and a candidate t, the nearest point p of t is in t's interior (z - p along
        // +-N), in an edge's interior (z - p = the part of z - A orthogonal to the edge), or a vertex (z - v). A part is left
        // out where its condition provably fails for every z in X (linear tests on the corners). a . g has one sign on the
        // hull of all remaining generators iff it has on each, and each test is linear in z: corners suffice.
        static bool one_signed(Vector3d a, List<Vector3d> tris, Vector3d[] zs)
        {
            int want = 0;
            Func<int, bool> take = (sg) => { if (sg == 0) return false; if (want == 0) want = sg; return sg == want; };
            for (int t = 0; t < tris.Count; t += 3) {
                Vector3d v0 = tris[t], v1 = tris[t + 1], v2 = tris[t + 2];
                Vector3d[] v = { v0, v1, v2 };
                bool interior = true;
                for (int e = 0; e < 3; ++e) {
                    Vector3d A = v[e], B = v[(e + 1) % 3], C = v[(e + 2) % 3];
                    // outer side of the edge line in t's plane: sign of ((B - A) x N) . (z - A) against that of C
                    int sc = Ex.edge_side(v0, v1, v2, A, B, C);
                    bool allInner = true, allOuterOrOn = true;
                    foreach (Vector3d z in zs) {
                        int sz = Ex.edge_side(v0, v1, v2, A, B, z);
                        if (sz != sc) allInner = false;
                        if (sz == sc) allOuterOrOn = false;
                    }
                    if (allOuterOrOn) interior = false;
                    // the edge's interior can hold the nearest point only if some z is on its outer side and projects into it
                    bool edgeRelevant = !allInner;
                    if (edgeRelevant) {
                        bool allBefore = true, allAfter = true;
                        foreach (Vector3d z in zs) {
                            int s0 = Ex.span_sign(A, B, z);     // sign of (z - A) . (B - A)
                            int s1 = Ex.span_sign(B, A, z);     // sign of (z - B) . (A - B)
                            if (s0 > 0) allBefore = false;
                            if (s1 > 0) allAfter = false;
                        }
                        if (allBefore || allAfter) edgeRelevant = false;
                    }
                    if (edgeRelevant)
                        foreach (Vector3d z in zs) if (!take(Ex.edge_dir_sign(a, z, A, B))) return false;
                }
                for (int k = 0; k < 3; ++k) {
                    Vector3d P = v[k], Q = v[(k + 1) % 3], R = v[(k + 2) % 3];
                    // the vertex is nearest only if (z - P) . (Q - P) <= 0 and (z - P) . (R - P) <= 0
                    bool outQ = true, outR = true;
                    foreach (Vector3d z in zs) { if (Ex.span_sign(P, Q, z) <= 0) outQ = false; if (Ex.span_sign(P, R, z) <= 0) outR = false; }
                    if (outQ || outR) continue;
                    foreach (Vector3d z in zs) if (!take(Ex.dot_sign(a, z, P))) return false;
                }
                if (interior) {
                    // which side of t's plane the corners are on; both sides possible if they straddle it
                    bool pos = false, neg = false;
                    foreach (Vector3d z in zs) { int o = Ex.plane_side(v0, v1, v2, z); if (o >= 0) pos = true; if (o <= 0) neg = true; }
                    int an = Ex.normal_dot_sign(a, v0, v1, v2);
                    if (pos && !take(an)) return false;
                    if (neg && !take(-an)) return false;
                }
            }
            return want != 0;
        }

        static Vector3d corner(Vector3d lo, Vector3d hi, int q)
        {
            return new Vector3d((q & 1) != 0 ? hi.x : lo.x, (q & 2) != 0 ? hi.y : lo.y, (q & 4) != 0 ? hi.z : lo.z);
        }

        // F is zero-free on the box (lo, hi) (a segment, a face or a cell), proven by splitting it up to depth times
        bool zero_free_split(Vector3d lo, Vector3d hi, int depth)
        {
            if (zero_free(lo, hi) != 0) return true;
            if (depth == 0) return false;
            int axis = 0;
            for (int a = 1; a < 3; ++a) if (hi[a] - lo[a] > hi[axis] - lo[axis]) axis = a;
            double m = 0.5 * (lo[axis] + hi[axis]);
            Vector3d hi0 = hi, lo1 = lo;
            hi0[axis] = m; lo1[axis] = m;
            // (both halves share the plane at m: the union is the box)
            return zero_free_split(lo, hi0, depth - 1) && zero_free_split(lo1, hi, depth - 1);
        }

        // lower bound of the squared distance between two boxes
        static double box_box_lower_sq(Vector3d amin, Vector3d amax, Vector3d bmin, Vector3d bmax)
        {
            double s = 0;
            for (int k = 0; k < 3; ++k) {
                double mag = Math.Max(Math.Max(Math.Abs(amin[k]), Math.Abs(amax[k])), Math.Max(Math.Abs(bmin[k]), Math.Abs(bmax[k])));
                double slack = 8 * (ExactInterval.Up(mag) - mag);
                double gap = Math.Max((ExactInterval.Of(bmin[k]) - ExactInterval.Of(amax[k])).Lo, (ExactInterval.Of(amin[k]) - ExactInterval.Of(bmax[k])).Lo);
                gap = Math.Max(0, ExactInterval.Down(gap - slack));
                s = ExactInterval.Down(s + ExactInterval.Down(gap * gap));
            }
            return Math.Max(0, s);
        }
    }

    /// <summary> exact signs of small polynomials in doubles: interval filter, then exact dyadic arithmetic </summary>
    internal static class Ex
    {
        interface IRing<T> { T Of(double x); T Add(T a, T b); T Sub(T a, T b); T Mul(T a, T b); }
        struct IR : IRing<ExactInterval> {
            public ExactInterval Of(double x) => ExactInterval.Of(x);
            public ExactInterval Add(ExactInterval a, ExactInterval b) => a + b;
            public ExactInterval Sub(ExactInterval a, ExactInterval b) => a - b;
            public ExactInterval Mul(ExactInterval a, ExactInterval b) => a * b;
        }
        struct DR : IRing<ExactDyadic> {
            public ExactDyadic Of(double x) => ExactDyadic.Of(x);
            public ExactDyadic Add(ExactDyadic a, ExactDyadic b) => a + b;
            public ExactDyadic Sub(ExactDyadic a, ExactDyadic b) => a - b;
            public ExactDyadic Mul(ExactDyadic a, ExactDyadic b) => a * b;
        }

        static T[] V<T, R>(R r, Vector3d p) where R : struct, IRing<T> => new[] { r.Of(p.x), r.Of(p.y), r.Of(p.z) };
        static T[] Sub<T, R>(R r, T[] a, T[] b) where R : struct, IRing<T> => new[] { r.Sub(a[0], b[0]), r.Sub(a[1], b[1]), r.Sub(a[2], b[2]) };
        static T Dot<T, R>(R r, T[] a, T[] b) where R : struct, IRing<T> => r.Add(r.Add(r.Mul(a[0], b[0]), r.Mul(a[1], b[1])), r.Mul(a[2], b[2]));
        static T[] Cross<T, R>(R r, T[] a, T[] b) where R : struct, IRing<T> => new[] {
            r.Sub(r.Mul(a[1], b[2]), r.Mul(a[2], b[1])), r.Sub(r.Mul(a[2], b[0]), r.Mul(a[0], b[2])), r.Sub(r.Mul(a[0], b[1]), r.Mul(a[1], b[0])) };

        static int sign(Func<IR, ExactInterval> fi, Func<DR, ExactDyadic> fd)
        {
            int s = fi(new IR()).Sign();
            if (s == 1 || s == -1) return s;
            return fd(new DR()).Sign;
        }

        /// <summary> a . (z - p) </summary>
        public static int dot_sign(Vector3d a, Vector3d z, Vector3d p) => sign(
            r => Dot<ExactInterval, IR>(r, V<ExactInterval, IR>(r, a), Sub<ExactInterval, IR>(r, V<ExactInterval, IR>(r, z), V<ExactInterval, IR>(r, p))),
            r => Dot<ExactDyadic, DR>(r, V<ExactDyadic, DR>(r, a), Sub<ExactDyadic, DR>(r, V<ExactDyadic, DR>(r, z), V<ExactDyadic, DR>(r, p))));

        /// <summary> (z - A) . (B - A) </summary>
        public static int span_sign(Vector3d A, Vector3d B, Vector3d z) => sign(
            r => { var a = V<ExactInterval, IR>(r, A); return Dot<ExactInterval, IR>(r, Sub<ExactInterval, IR>(r, V<ExactInterval, IR>(r, z), a), Sub<ExactInterval, IR>(r, V<ExactInterval, IR>(r, B), a)); },
            r => { var a = V<ExactDyadic, DR>(r, A); return Dot<ExactDyadic, DR>(r, Sub<ExactDyadic, DR>(r, V<ExactDyadic, DR>(r, z), a), Sub<ExactDyadic, DR>(r, V<ExactDyadic, DR>(r, B), a)); });

        /// <summary> sign of a . w with w = |e|^2 (z - A) - ((z - A) . e) e, e = B - A: a positive multiple of the part of z - A orthogonal to e </summary>
        public static int edge_dir_sign(Vector3d a, Vector3d z, Vector3d A, Vector3d B) => sign(
            r => edge_dir<ExactInterval, IR>(r, a, z, A, B), r => edge_dir<ExactDyadic, DR>(r, a, z, A, B));
        static T edge_dir<T, R>(R r, Vector3d a, Vector3d z, Vector3d A, Vector3d B) where R : struct, IRing<T>
        {
            T[] av = V<T, R>(r, a), e = Sub<T, R>(r, V<T, R>(r, B), V<T, R>(r, A)), w = Sub<T, R>(r, V<T, R>(r, z), V<T, R>(r, A));
            return r.Sub(r.Mul(Dot<T, R>(r, e, e), Dot<T, R>(r, av, w)), r.Mul(Dot<T, R>(r, w, e), Dot<T, R>(r, av, e)));
        }

        /// <summary> N . (z - v0), N = (v1 - v0) x (v2 - v0) </summary>
        public static int plane_side(Vector3d v0, Vector3d v1, Vector3d v2, Vector3d z) => sign(
            r => plane<ExactInterval, IR>(r, v0, v1, v2, z), r => plane<ExactDyadic, DR>(r, v0, v1, v2, z));
        static T plane<T, R>(R r, Vector3d v0, Vector3d v1, Vector3d v2, Vector3d z) where R : struct, IRing<T>
        {
            T[] o = V<T, R>(r, v0);
            return Dot<T, R>(r, Cross<T, R>(r, Sub<T, R>(r, V<T, R>(r, v1), o), Sub<T, R>(r, V<T, R>(r, v2), o)), Sub<T, R>(r, V<T, R>(r, z), o));
        }

        /// <summary> a . N </summary>
        public static int normal_dot_sign(Vector3d a, Vector3d v0, Vector3d v1, Vector3d v2) => sign(
            r => nd<ExactInterval, IR>(r, a, v0, v1, v2), r => nd<ExactDyadic, DR>(r, a, v0, v1, v2));
        static T nd<T, R>(R r, Vector3d a, Vector3d v0, Vector3d v1, Vector3d v2) where R : struct, IRing<T>
        {
            T[] o = V<T, R>(r, v0);
            return Dot<T, R>(r, V<T, R>(r, a), Cross<T, R>(r, Sub<T, R>(r, V<T, R>(r, v1), o), Sub<T, R>(r, V<T, R>(r, v2), o)));
        }

        /// <summary> ((B - A) x N) . (z - A), N the normal of the triangle (v0, v1, v2): the side of z of the edge line AB in t's plane </summary>
        public static int edge_side(Vector3d v0, Vector3d v1, Vector3d v2, Vector3d A, Vector3d B, Vector3d z) => sign(
            r => es<ExactInterval, IR>(r, v0, v1, v2, A, B, z), r => es<ExactDyadic, DR>(r, v0, v1, v2, A, B, z));
        static T es<T, R>(R r, Vector3d v0, Vector3d v1, Vector3d v2, Vector3d A, Vector3d B, Vector3d z) where R : struct, IRing<T>
        {
            T[] o = V<T, R>(r, v0);
            T[] n = Cross<T, R>(r, Sub<T, R>(r, V<T, R>(r, v1), o), Sub<T, R>(r, V<T, R>(r, v2), o));
            T[] a = V<T, R>(r, A);
            return Dot<T, R>(r, Cross<T, R>(r, Sub<T, R>(r, V<T, R>(r, B), a), n), Sub<T, R>(r, V<T, R>(r, z), a));
        }
    }
}
