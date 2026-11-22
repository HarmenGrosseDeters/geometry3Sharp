// The two level-set certificates on the uniform grid that DualContouring uses (node n at origin + h * n):
//
//   PV   : every cell passes C0 (the enclosure of f excludes 0) or C1 (no two gradients in the cell are orthogonal: the
//          lower bound of <g, g'> over all pairs of gradient enclosures is positive), after Plantinga and Vegter. A cell
//          that fails as one box is cut into 2^t sub-boxes per axis, t <= maxT, only to tighten the enclosures: C0 needs all
//          sub-boxes zero-free, C1 needs the pairwise test for all pairs of sub-boxes and f to be C1 on the whole cell.
//   S    : the cell test (S0)-(S3) of Theorem 5 (docs/design/CONTOURING_TOPOLOGY.md 7.8) with the same enclosures, in the
//          segment form (the direction tests are made on the sub-pieces that can hold a zero only).
//
// Both need the sign of f at the nodes of the cell certified. Every decision is a sign of an outward-rounded interval.
using System;
using System.Collections.Generic;
using g3;

sealed class GridCertificate
{
    public Field F;
    public Vector3d Origin;
    public double H;
    public int N;                 // cells per axis, a power of two
    public int MaxT = 2;

    // per cell: 0 zero-free, else a surface cell; bit 1 = PV certified, bit 2 = S certified
    public byte[] Cell;
    public int SurfaceCells, PvCells, SCells, BothCells, NeitherCells, NotSmoothCells, UncertainNodeCells, OuterSurfaceCells;
    public int AmbiguousFacesInPv, MultiPatchInPv, PvRefuted;
    public int MaxTS = 2;
    public int[] PvAtT = new int[8], SAtT = new int[8];
    public int[] SFailBy = new int[5];          // first failing condition of S at the last t: S0, S1, S2, S3 (no graph direction), S3 (several circles)
    public bool DoS = true;
    public long Evaluations;
    sbyte[] node;                 // 0 not computed, 1 f > 0, -1 f < 0, 2 not certified

    public bool PvCertified { get { return SurfaceCells > 0 && PvCells == SurfaceCells && OuterSurfaceCells == 0; } }
    public bool SCertified { get { return SurfaceCells > 0 && SCells == SurfaceCells && OuterSurfaceCells == 0; } }

    double X(int axis, long i, int t) { return Origin[axis] + (H / (1 << t)) * i; }      // node i of the grid refined t times; equals DC's origin + h * n at t = 0
    Iv Span(int axis, long i, int t, int cells) { return new Iv(X(axis, i, t), X(axis, i + cells, t)); }
    Enc Eval(Iv x, Iv y, Iv z) { ++Evaluations; return F.Eval(x, y, z); }

    public int NodeSign(int i, int j, int k)
    {
        int idx = i + (N + 1) * (j + (N + 1) * k);
        if (node[idx] != 0) return node[idx];
        Iv f = Eval(Iv.Of(X(0, i, 0)), Iv.Of(X(1, j, 0)), Iv.Of(X(2, k, 0))).F;
        // the contouring keeps node values as float: a certified sign must survive that cast
        sbyte s = f.Lo > 1e-30 ? (sbyte)1 : f.Hi < -1e-30 ? (sbyte)-1 : (sbyte)2;
        node[idx] = s;
        return s;
    }

    public void Run()
    {
        Cell = new byte[N * N * N];
        node = new sbyte[(N + 1) * (N + 1) * (N + 1)];
        descend(0, 0, 0, N);
    }

    void descend(int i, int j, int k, int size)
    {
        Enc e = Eval(Span(0, i, 0, size), Span(1, j, 0, size), Span(2, k, 0, size));
        if (!e.F.HasZero) return;
        if (size > 1) {
            int s = size / 2;
            for (int c = 0; c < 8; ++c) descend(i + ((c & 1) != 0 ? s : 0), j + ((c & 2) != 0 ? s : 0), k + ((c & 4) != 0 ? s : 0), s);
            return;
        }
        testCell(i, j, k);
    }

    void testCell(int i, int j, int k)
    {
        // corner signs, local corner c = bits (x, y, z)
        int[] sg = new int[8];
        bool nodesOk = true;
        for (int c = 0; c < 8; ++c) { sg[c] = NodeSign(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1)); if (sg[c] == 2) nodesOk = false; }

        int pvT = -1; bool zeroFree = false, smoothAll = true, refuted = false;
        bool signChange = false;
        for (int c = 1; c < 8; ++c) if (sg[c] != 2 && sg[0] != 2 && sg[c] != sg[0]) signChange = true;
        // after a refutation of C1 only zero-freeness is still tried, to t = 2, and not at all if two corners have different certified signs
        for (int t = 0; t <= MaxT && pvT < 0 && !zeroFree && !(refuted && (signChange || t > 2)); ++t) {
            int m = 1 << t;
            var enc = new Enc[m * m * m];
            bool allFree = true, smooth = true;
            for (int a = 0; a < m; ++a) for (int b = 0; b < m; ++b) for (int c = 0; c < m; ++c) {
                Enc e = Eval(Span(0, (long)i * m + a, t, 1), Span(1, (long)j * m + b, t, 1), Span(2, (long)k * m + c, t, 1));
                enc[a + m * (b + m * c)] = e;
                if (e.F.HasZero) allFree = false;
                if (!e.Smooth || !e.Gx.IsFinite || !e.Gy.IsFinite || !e.Gz.IsFinite) smooth = false;
            }
            if (allFree) { zeroFree = true; break; }
            smoothAll = smooth;
            if (!refuted && smooth && (HullOnly ? hullPositive(enc) : t <= 3 ? pairwisePositive(enc) : inCone(enc))) pvT = t;
            else if (t == 1 && !refuted) {
                // a proof that C1 is false ends the deepening: two points of the cell whose gradients have a certified
                // inner product <= 0 (the nine points are the corners of the cell and its centre)
                var pg = new List<Enc>();
                for (int c = 0; c < 8; ++c) pg.Add(Eval(Iv.Of(X(0, i + (c & 1), 0)), Iv.Of(X(1, j + ((c >> 1) & 1), 0)), Iv.Of(X(2, k + ((c >> 2) & 1), 0))));
                pg.Add(Eval(Iv.Of(X(0, 2L * i + 1, 1)), Iv.Of(X(1, 2L * j + 1, 1)), Iv.Of(X(2, 2L * k + 1, 1))));
                for (int a = 0; a < pg.Count && !refuted; ++a) for (int b = a + 1; b < pg.Count; ++b) {
                    if (!pg[a].Smooth || !pg[b].Smooth) continue;
                    if (((pg[a].Gx * pg[b].Gx) + (pg[a].Gy * pg[b].Gy) + (pg[a].Gz * pg[b].Gz)).Hi <= 0) { refuted = true; break; }
                }
            }

        }
        if (zeroFree) return;

        ++SurfaceCells;
        bool outer = i == 0 || j == 0 || k == 0 || i == N - 1 || j == N - 1 || k == N - 1;
        if (outer) ++OuterSurfaceCells;
        if (!nodesOk) ++UncertainNodeCells;
        if (!smoothAll) ++NotSmoothCells;
        if (refuted) ++PvRefuted;
        bool pv = pvT >= 0 && nodesOk;
        int sT = -1;
        if (!DoS) { }
        else if (nodesOk) {
            int fail = 0;
            for (int t = 0; t <= MaxTS && sT < 0; ++t) { fail = sTest(i, j, k, sg, t); if (fail < 0) sT = t; }
            if (sT < 0) ++SFailBy[fail];
        } else ++SFailBy[0];
        bool s = sT >= 0;
        byte flags = 1;
        if (pv) { flags |= 2; ++PvCells; ++PvAtT[pvT]; int amb; if (patches(sg, out amb) > 1) ++MultiPatchInPv; AmbiguousFacesInPv += amb; }
        if (s) { flags |= 4; ++SCells; ++SAtT[sT]; }
        if (pv && s) ++BothCells;
        if (!pv && !s) ++NeitherCells;
        Cell[i + N * (j + N * k)] = flags;
    }

    // The published form of C1 with a tighter box function: the hull of the sub-boxes' gradient enclosures is an enclosure of the
    // gradient over the cell, and the interval inner product of that one box with itself must exclude 0. Stronger than the
    // pairwise test (it implies that one partial derivative keeps its sign on the cell).
    public bool HullOnly;
    static bool hullPositive(Enc[] enc)
    {
        Enc h = enc[0];
        foreach (Enc e in enc) { h.Gx = Iv.Hull(h.Gx, e.Gx); h.Gy = Iv.Hull(h.Gy, e.Gy); h.Gz = Iv.Hull(h.Gz, e.Gz); }
        return lowerDot(h, h) > 0;
    }

    static double lowerDot(Enc p, Enc q) { return ((p.Gx * q.Gx) + (p.Gy * q.Gy) + (p.Gz * q.Gz)).Lo; }

    // <g, g'> > 0 for every g in the enclosure of one sub-box and g' in that of another (or the same): any two points of
    // the cell lie in two such sub-boxes
    // hand mutant for the truth check (README): G3_CIS_MUTANT=1 tests each sub-box against itself only, which is the classic
    // mistake of squaring the gradient interval: it proves a gradient that does not vanish, not a small normal variation
    static readonly bool mutant = Environment.GetEnvironmentVariable("G3_CIS_MUTANT") == "1";
    static bool pairwisePositive(Enc[] enc)
    {
        if (mutant) {
            foreach (Enc e in enc) if (!((Iv.Sqr(e.Gx) + Iv.Sqr(e.Gy) + Iv.Sqr(e.Gz)).Lo > 0)) return false;
            return true;
        }
        for (int a = 0; a < enc.Length; ++a)
            for (int b = a; b < enc.Length; ++b)
                if (!(lowerDot(enc[a], enc[b]) > 0)) return false;
        return true;
    }

    // all gradients of all sub-boxes within 45 degrees of one direction d (so any two are less than 90 degrees apart):
    // d . g > 0 and (d . g)^2 > |d|^2 |g|^2 / 2 on every enclosure. Linear in the number of sub-boxes; implies the pairwise test.
    static bool inCone(Enc[] enc)
    {
        Vector3d d = Vector3d.Zero;
        foreach (Enc e in enc) d += new Vector3d(e.Gx.Mid, e.Gy.Mid, e.Gz.Mid);
        if (!(d.LengthSquared > 0) || double.IsInfinity(d.LengthSquared)) return false;
        d = d.Normalized;
        Iv dd = Iv.Sqr(Iv.Of(d.x)) + Iv.Sqr(Iv.Of(d.y)) + Iv.Sqr(Iv.Of(d.z));
        foreach (Enc e in enc) {
            Iv dot = d.x * e.Gx + d.y * e.Gy + d.z * e.Gz;
            if (!(dot.Lo > 0)) return false;
            Iv g2 = Iv.Sqr(e.Gx) + Iv.Sqr(e.Gy) + Iv.Sqr(e.Gz);
            if (!((Iv.Sqr(dot) - Iv.Of(0.5) * (dd * g2)).Lo > 0)) return false;
        }
        return true;
    }

    // ---- the cell test of Theorem 5; returns -1 if it passes, else the index of the first failing condition (1, 2, 3)

    static readonly int[,] faceCorners = {        // per face (axis, side): 4 corners in cyclic order
        { 0, 2, 6, 4 }, { 1, 3, 7, 5 }, { 0, 1, 5, 4 }, { 2, 3, 7, 6 }, { 0, 1, 3, 2 }, { 4, 5, 7, 6 } };

    int sTest(int i, int j, int k, int[] sg, int t)
    {
        int m = 1 << t;
        long[] b = { (long)i * m, (long)j * m, (long)k * m };
        // (S1) the 12 edges
        for (int axis = 0; axis < 3; ++axis) {
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            for (int su = 0; su < 2; ++su) for (int sv = 0; sv < 2; ++sv) {
                int sign = 0; bool ok = true; int state = 0;       // state: 0 before the run of pieces with a possible zero, 1 in it, 2 after
                for (int a = 0; a < m && ok; ++a) {
                    Iv[] x = new Iv[3];
                    x[axis] = Span(axis, b[axis] + a, t, 1);
                    x[u] = Iv.Of(X(u, b[u] + su * m, t)); x[v] = Iv.Of(X(v, b[v] + sv * m, t));
                    Enc e = Eval(x[0], x[1], x[2]);
                    if (!e.F.HasZero) { if (state == 1) state = 2; continue; }
                    if (state == 2) { ok = false; break; }
                    state = 1;
                    Iv d = axis == 0 ? e.Gx : axis == 1 ? e.Gy : e.Gz;
                    int s = d.Lo > 0 ? 1 : d.Hi < 0 ? -1 : 0;
                    if (s == 0 || (sign != 0 && s != sign)) ok = false;
                    sign = s;
                }
                if (!ok) return 1;
            }
        }
        // (S2) the 6 faces, with any direction in the face's plane ((S2')): the two axes and the in-plane gradient at the centre
        for (int axis = 0; axis < 3; ++axis) {
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            for (int side = 0; side < 2; ++side) {
                var list = new List<Enc>();
                for (int a = 0; a < m; ++a) for (int c = 0; c < m; ++c) {
                    Iv[] x = new Iv[3];
                    x[axis] = Iv.Of(X(axis, b[axis] + side * m, t));
                    x[u] = Span(u, b[u] + a, t, 1); x[v] = Span(v, b[v] + c, t, 1);
                    Enc e = Eval(x[0], x[1], x[2]);
                    if (e.F.HasZero) list.Add(e);
                }
                if (list.Count > 0) {
                    Vector3d mid = Vector3d.Zero;
                    mid[axis] = X(axis, b[axis] + side * m, t); mid[u] = X(u, 2 * b[u] + m, t + 1); mid[v] = X(v, 2 * b[v] + m, t + 1);
                    Enc ec = F.At(mid);
                    Vector3d d = new Vector3d(ec.Gx.Mid, ec.Gy.Mid, ec.Gz.Mid); d[axis] = 0;
                    Vector3d du = Vector3d.Zero, dv = Vector3d.Zero; du[u] = 1; dv[v] = 1;
                    if (!oneSign(list, du) && !oneSign(list, dv) && !oneSign(list, d)) return 2;
                }
                // four crossings: the contouring's face rule keeps the inside corners apart, so the outside corners must be
                // joined: f > 0 on the diagonal between them (covered by 8 m boxes around its pieces)
                int f = axis * 2 + side;
                int c0 = sg[faceCorners[f, 0]], c1 = sg[faceCorners[f, 1]], c2 = sg[faceCorners[f, 2]], c3 = sg[faceCorners[f, 3]];
                if (c0 == c2 && c1 == c3 && c0 != c1) {
                    int o = c0 > 0 ? faceCorners[f, 0] : faceCorners[f, 1], p = c0 > 0 ? faceCorners[f, 2] : faceCorners[f, 3];
                    int pieces = 8 * m, tt = t + 3;
                    long[] bb = { b[0] * 8, b[1] * 8, b[2] * 8 };
                    for (int q = 0; q < pieces; ++q) {
                        Iv[] x = new Iv[3];
                        for (int ax = 0; ax < 3; ++ax) {
                            int from = (o >> ax) & 1, to = (p >> ax) & 1;
                            if (from == to) { x[ax] = Iv.Of(X(ax, bb[ax] + (long)from * pieces, tt)); continue; }
                            long lo = from < to ? q : pieces - 1 - q;
                            x[ax] = Span(ax, bb[ax] + lo, tt, 1);
                        }
                        if (!(Eval(x[0], x[1], x[2]).F.Lo > 0)) return 2;
                    }
                }
            }
        }
        // (S3) a graph direction on the sub-boxes that can hold a zero, and one circle on the cell's boundary
        {
            var list = new List<Enc>();
            for (int a = 0; a < m; ++a) for (int c = 0; c < m; ++c) for (int e2 = 0; e2 < m; ++e2) {
                Enc e = Eval(Span(0, b[0] + a, t, 1), Span(1, b[1] + c, t, 1), Span(2, b[2] + e2, t, 1));
                if (e.F.HasZero) list.Add(e);
            }
            Enc ec = F.At(new Vector3d(X(0, 2 * b[0] + m, t + 1), X(1, 2 * b[1] + m, t + 1), X(2, 2 * b[2] + m, t + 1)));
            Vector3d d = new Vector3d(ec.Gx.Mid, ec.Gy.Mid, ec.Gz.Mid);
            if (list.Count > 0 && !oneSign(list, d)) {
                // second try: the mean of the midpoints of the enclosures
                Vector3d d2 = Vector3d.Zero;
                foreach (Enc e in list) d2 += new Vector3d(e.Gx.Mid, e.Gy.Mid, e.Gz.Mid);
                if (!oneSign(list, d2)) return 3;
            }
            // No circle: with (S1) and (S2) passed and no sign change at the corners, L does not meet the cell's boundary, and a
            // closed surface is not a graph, so the cell is zero-free. One circle: one disk (Theorem 4). More: not tested here
            // (it needs a certified sign on a separating surface).
            int amb;
            if (patches(sg, out amb) > 1) return 4;
        }
        return -1;
    }

    // d . g > 0 for all g of all enclosures (d as doubles; a zero or not finite d fails)
    static bool oneSign(List<Enc> list, Vector3d d)
    {
        if (!(d.LengthSquared > 0) || double.IsInfinity(d.LengthSquared)) return false;
        foreach (Enc e in list)
            if (!((d.x * e.Gx + d.y * e.Gy + d.z * e.Gz).Lo > 0)) return false;
        return true;
    }

    // the patches of the contouring's face rule (DualContouring.cell_patches): inside corners joined along cube edges,
    // outside corners along cube edges and across the diagonal of ambiguous faces; a patch is a pair of an inside and an
    // outside component with a sign-changing edge between them
    static int patches(int[] sg, out int ambiguousFaces)
    {
        int[] parent = { 0, 1, 2, 3, 4, 5, 6, 7 };
        Func<int, int> find = null;
        find = a => parent[a] == a ? a : (parent[a] = find(parent[a]));
        for (int c = 0; c < 8; ++c) for (int ax = 0; ax < 3; ++ax) {
            int d = c | (1 << ax);
            if (d != c && sg[c] == sg[d]) parent[find(c)] = find(d);
        }
        ambiguousFaces = 0;
        for (int f = 0; f < 6; ++f) {
            int a = faceCorners[f, 0], b = faceCorners[f, 1], c = faceCorners[f, 2], d = faceCorners[f, 3];
            if (sg[a] == sg[c] && sg[b] == sg[d] && sg[a] != sg[b]) {
                ++ambiguousFaces;
                if (sg[a] > 0) parent[find(a)] = find(c); else parent[find(b)] = find(d);
            }
        }
        var keys = new HashSet<int>();
        for (int c = 0; c < 8; ++c) for (int ax = 0; ax < 3; ++ax) {
            int d = c | (1 << ax);
            if (d != c && sg[c] != sg[d]) keys.Add(sg[c] < 0 ? find(c) * 8 + find(d) : find(d) * 8 + find(c));
        }
        return keys.Count;
    }
}

// The subdivision itself (Plantinga and Vegter's first phase): an octree over the grid's box, split until C0 or C1 holds
// for the box as one enclosure. Counts only: no balancing, no mesh. Depth is counted from the grid's cell (0 = a grid cell,
// negative = coarser, positive = finer than the grid).
sealed class Subdivision
{
    public Field F;
    public Vector3d Origin;
    public double H;
    public int N, MaxFiner = 4;
    public long MaxLeaves = 4000000;
    public long Leaves, ByC0, ByC1, Unresolved, UnresolvedNotSmooth;
    public int DeepestC1 = int.MinValue;
    public long[] C1ByDepth = new long[64];           // index depth + 32
    public bool Aborted;

    public void Run() { visit(0, 0, 0, N, 0); }

    // box = cells [i, i + size) of the grid refined t times
    void visit(long i, long j, long k, long size, int t)
    {
        if (Aborted) return;
        double w = H / (1 << t);
        Enc e = F.Eval(new Iv(Origin.x + w * i, Origin.x + w * (i + size)), new Iv(Origin.y + w * j, Origin.y + w * (j + size)), new Iv(Origin.z + w * k, Origin.z + w * (k + size)));
        int depth = t - (size > 1 ? (int)Math.Round(Math.Log(size, 2)) : 0);
        if (!e.F.HasZero) { ++Leaves; ++ByC0; return; }
        bool smooth = e.Smooth && e.Gx.IsFinite && e.Gy.IsFinite && e.Gz.IsFinite;
        if (smooth && ((e.Gx * e.Gx) + (e.Gy * e.Gy) + (e.Gz * e.Gz)).Lo > 0) {
            ++Leaves; ++ByC1; ++C1ByDepth[depth + 32]; DeepestC1 = Math.Max(DeepestC1, depth); return;
        }
        if (size == 1 && t == MaxFiner) { ++Leaves; ++Unresolved; if (!smooth) ++UnresolvedNotSmooth; return; }
        if (Leaves > MaxLeaves) { Aborted = true; return; }
        if (size > 1) {
            long s = size / 2;
            for (int c = 0; c < 8; ++c) visit(i + ((c & 1) != 0 ? s : 0), j + ((c & 2) != 0 ? s : 0), k + ((c & 4) != 0 ? s : 0), s, t);
        } else {
            for (int c = 0; c < 8; ++c) visit(2 * i + (c & 1), 2 * j + ((c >> 1) & 1), 2 * k + ((c >> 2) & 1), 1, t + 1);
        }
    }
}
