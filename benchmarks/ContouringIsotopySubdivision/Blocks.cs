// A measurement for Theorem 8 (docs/design/CONTOURING_BLOCK_CERTIFICATE.md): how many of the surface cells that the cell
// test (S0)-(S3) refuses lie in box-shaped blocks that pass (B0)-(B3)? Level-set side only; the contour's count in a block
// (Lemma 6.4) is not measured. Not a certificate: a prototype to decide whether a build is worth it.
//
// Blocks: every refused cell is a seed. Its box starts as the cell and grows by one layer on the sides that carry a failing
// node, edge or face, until the boundary passes or the box exceeds MaxSide cells or reaches the outer layer. A box that
// overlaps an accepted block takes the bounding box of both. (B3) is tested as in the cell test: on the sub-boxes that can
// hold a zero, one direction d with d . g > 0 (an axis, or the mean gradient), and at most one circle on the boundary,
// counted from the node signs and the contouring's face rule.
using System;
using System.Collections.Generic;
using g3;

sealed class BlockCoverage
{
    public GridCertificate G;
    public int MaxSide = 6, T = 2;

    public int Refused, Covered, Blocks, LargestBlock, CellsInBlocks, ZeroFreeBlocks;
    public int[] FailBy = new int[8];       // per seed left uncovered: 1 node, 2 edge, 3 face (at the size limit), 4 no direction, 5 several circles, 6 outer layer
    public int[] SizeHistogram = new int[8]; // blocks by largest side 1..7
    public long Evaluations;

    int N;
    int[] owner;
    readonly Dictionary<long, bool> edgeOk = new Dictionary<long, bool>(), faceOk = new Dictionary<long, bool>();
    readonly Dictionary<int, List<Enc>> cellPieces = new Dictionary<int, List<Enc>>();
    readonly List<int[]> boxes = new List<int[]>();       // lo x, y, z, hi x, y, z (hi exclusive); null if absorbed

    public void Run()
    {
        N = G.N;
        owner = new int[N * N * N];
        for (int i = 0; i < owner.Length; ++i) owner[i] = -1;
        long before = G.Evaluations;
        for (int idx = 0; idx < G.Cell.Length; ++idx) {
            if ((G.Cell[idx] & 1) == 0 || (G.Cell[idx] & 4) != 0) continue;
            ++Refused;
            if (owner[idx] >= 0) continue;
            int i = idx % N, j = idx / N % N, k = idx / (N * N);
            int fail = grow(new[] { i, j, k, i + 1, j + 1, k + 1 });
            if (fail != 0) ++FailBy[fail];
        }
        foreach (int[] b in boxes) {
            if (b == null) continue;
            ++Blocks;
            int side = Math.Max(b[3] - b[0], Math.Max(b[4] - b[1], b[5] - b[2]));
            ++SizeHistogram[Math.Min(side, 7)];
            int cells = (b[3] - b[0]) * (b[4] - b[1]) * (b[5] - b[2]);
            CellsInBlocks += cells; LargestBlock = Math.Max(LargestBlock, cells);
        }
        for (int idx = 0; idx < G.Cell.Length; ++idx)
            if ((G.Cell[idx] & 1) != 0 && (G.Cell[idx] & 4) == 0 && owner[idx] >= 0) ++Covered;
        Evaluations = G.Evaluations - before;
    }

    int grow(int[] b)
    {
        var absorbed = new List<int>();
        lastFail = 0;
        for (int step = 0; step < 64; ++step) {
            // take in every accepted block the box overlaps
            bool changed = true;
            while (changed) {
                changed = false;
                for (int k = b[2]; k < b[5]; ++k) for (int j = b[1]; j < b[4]; ++j) for (int i = b[0]; i < b[3]; ++i) {
                    int o = owner[i + N * (j + N * k)];
                    if (o < 0 || absorbed.Contains(o)) continue;
                    absorbed.Add(o);
                    int[] other = boxes[o];
                    for (int a = 0; a < 3; ++a) { b[a] = Math.Min(b[a], other[a]); b[a + 3] = Math.Max(b[a + 3], other[a + 3]); }
                    changed = true;
                }
            }
            for (int a = 0; a < 3; ++a) {
                if (b[a] < 1 || b[a + 3] > N - 1) return 6;
                if (b[a + 3] - b[a] > MaxSide) return lastFail == 0 ? 3 : lastFail;
            }
            bool[] sides = new bool[6];
            int fail = test(b, sides);
            if (fail == 0) {
                foreach (int o in absorbed) boxes[o] = null;
                int id = boxes.Count; boxes.Add((int[])b.Clone());
                for (int k = b[2]; k < b[5]; ++k) for (int j = b[1]; j < b[4]; ++j) for (int i = b[0]; i < b[3]; ++i) owner[i + N * (j + N * k)] = id;
                return 0;
            }
            if (fail >= 4) return fail;
            lastFail = fail;
            for (int a = 0; a < 3; ++a) { if (sides[a]) --b[a]; if (sides[a + 3]) ++b[a + 3]; }
        }
        return 3;
    }
    int lastFail;

    // 0 if the box passes (B0)-(B3); else the failing condition, and for 1-3 the sides of the box that carry the failing element
    int test(int[] b, bool[] sides)
    {
        // (B0) nodes on the boundary
        for (int k = b[2]; k <= b[5]; ++k) for (int j = b[1]; j <= b[4]; ++j) for (int i = b[0]; i <= b[3]; ++i) {
            int[] p = { i, j, k };
            if (!onBoundary(b, p, -1)) continue;
            if (G.NodeSign(i, j, k) == 2) { mark(b, p, -1, sides); return 1; }
        }
        // (B1) edges on the boundary
        for (int axis = 0; axis < 3; ++axis)
            for (int k = b[2]; k <= b[5] - (axis == 2 ? 1 : 0); ++k) for (int j = b[1]; j <= b[4] - (axis == 1 ? 1 : 0); ++j) for (int i = b[0]; i <= b[3] - (axis == 0 ? 1 : 0); ++i) {
                int[] p = { i, j, k };
                if (!onBoundary(b, p, axis)) continue;
                if (!edge(axis, i, j, k)) { mark(b, p, axis, sides); return 2; }
            }
        // (B2) faces on the boundary
        for (int axis = 0; axis < 3; ++axis) {
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            for (int side = 0; side < 2; ++side) {
                int[] p = new int[3];
                p[axis] = side == 0 ? b[axis] : b[axis + 3];
                for (p[u] = b[u]; p[u] < b[u + 3]; ++p[u]) for (p[v] = b[v]; p[v] < b[v + 3]; ++p[v])
                    if (!face(axis, p[0], p[1], p[2])) { sides[axis + 3 * side] = true; return 3; }
            }
        }
        // (B3) one direction on the pieces that can hold a zero
        var list = new List<Enc>();
        for (int k = b[2]; k < b[5]; ++k) for (int j = b[1]; j < b[4]; ++j) for (int i = b[0]; i < b[3]; ++i) list.AddRange(pieces(i, j, k));
        if (list.Count > 0) {
            Vector3d mean = Vector3d.Zero;
            foreach (Enc e in list) mean += new Vector3d(e.Gx.Mid, e.Gy.Mid, e.Gz.Mid);
            bool ok = GridCertificate.oneSign(list, mean);
            for (int a = 0; a < 3 && !ok; ++a) for (int s = -1; s <= 1 && !ok; s += 2) { Vector3d d = Vector3d.Zero; d[a] = s; ok = GridCertificate.oneSign(list, d); }
            if (!ok) return 4;
        }
        int m = circles(b);
        if (m > 1) return 5;
        if (m == 0) ++ZeroFreeBlocks;
        return 0;
    }

    // is the node p (axis < 0) or the edge from node p along axis on the boundary of the box?
    static bool onBoundary(int[] b, int[] p, int axis)
    {
        for (int a = 0; a < 3; ++a) if (a != axis && (p[a] == b[a] || p[a] == b[a + 3])) return true;
        return false;
    }

    static void mark(int[] b, int[] p, int axis, bool[] sides)
    {
        for (int a = 0; a < 3; ++a) {
            if (a == axis) continue;
            if (p[a] == b[a]) sides[a] = true;
            if (p[a] == b[a + 3]) sides[a + 3] = true;
        }
    }

    static long key(int axis, int i, int j, int k) { return axis + 4L * (i + 4096L * (j + 4096L * k)); }

    // (S1) for the grid edge from node (i, j, k) along axis, at some tightening t <= T
    bool edge(int axis, int i, int j, int k)
    {
        bool r;
        long key = BlockCoverage.key(axis, i, j, k);
        if (edgeOk.TryGetValue(key, out r)) return r;
        r = false;
        int[] n = { i, j, k };
        for (int t = 0; t <= T && !r; ++t) {
            int m = 1 << t;
            int sign = 0; bool ok = true; int state = 0;
            for (int a = 0; a < m && ok; ++a) {
                Iv[] x = new Iv[3];
                for (int ax = 0; ax < 3; ++ax) x[ax] = ax == axis ? G.Span(ax, (long)n[ax] * m + a, t, 1) : Iv.Of(G.X(ax, (long)n[ax] * m, t));
                Enc e = G.Eval(x[0], x[1], x[2]);
                if (!e.F.HasZero) { if (state == 1) state = 2; continue; }
                if (state == 2) { ok = false; break; }
                state = 1;
                Iv d = axis == 0 ? e.Gx : axis == 1 ? e.Gy : e.Gz;
                int s = d.Lo > 0 ? 1 : d.Hi < 0 ? -1 : 0;
                if (s == 0 || (sign != 0 && s != sign)) ok = false;
                sign = s;
            }
            r = ok;
        }
        edgeOk[key] = r;
        return r;
    }

    // (S2) for the grid face with normal axis and lowest node (i, j, k), at some tightening t <= T: a direction in the plane,
    // and on a face with alternating corner signs f > 0 on the diagonal between the outside corners (the contouring's rule)
    bool face(int axis, int i, int j, int k)
    {
        bool r;
        long key = BlockCoverage.key(axis, i, j, k);
        if (faceOk.TryGetValue(key, out r)) return r;
        r = false;
        int u = (axis + 1) % 3, v = (axis + 2) % 3;
        int[] n = { i, j, k };
        for (int t = 0; t <= T && !r; ++t) {
            int m = 1 << t;
            var list = new List<Enc>();
            for (int a = 0; a < m; ++a) for (int c = 0; c < m; ++c) {
                Iv[] x = new Iv[3];
                x[axis] = Iv.Of(G.X(axis, (long)n[axis] * m, t));
                x[u] = G.Span(u, (long)n[u] * m + a, t, 1); x[v] = G.Span(v, (long)n[v] * m + c, t, 1);
                Enc e = G.Eval(x[0], x[1], x[2]);
                if (e.F.HasZero) list.Add(e);
            }
            bool ok = true;
            if (list.Count > 0) {
                Vector3d mid = Vector3d.Zero;
                mid[axis] = G.X(axis, (long)n[axis] * m, t); mid[u] = G.X(u, 2L * n[u] * m + m, t + 1); mid[v] = G.X(v, 2L * n[v] * m + m, t + 1);
                Enc ec = G.F.At(mid);
                Vector3d d = new Vector3d(ec.Gx.Mid, ec.Gy.Mid, ec.Gz.Mid); d[axis] = 0;
                Vector3d du = Vector3d.Zero, dv = Vector3d.Zero; du[u] = 1; dv[v] = 1;
                ok = GridCertificate.oneSign(list, du) || GridCertificate.oneSign(list, dv) || GridCertificate.oneSign(list, d);
            }
            if (ok) {
                int[] q = (int[])n.Clone();
                int c0 = G.NodeSign(q[0], q[1], q[2]); q[u]++; int c1 = G.NodeSign(q[0], q[1], q[2]); q[v]++; int c2 = G.NodeSign(q[0], q[1], q[2]); q[u]--; int c3 = G.NodeSign(q[0], q[1], q[2]);
                if (c0 == 2 || c1 == 2 || c2 == 2 || c3 == 2) ok = false;
                else if (c0 == c2 && c1 == c3 && c0 != c1) {
                    // the diagonal between the two outside corners, in 8 m boxes
                    bool fromLow = c0 > 0;      // corners 0 and 2 (the diagonal u = v), else corners 1 and 3
                    int pieces = 8 * m, tt = t + 3;
                    for (int p = 0; p < pieces && ok; ++p) {
                        Iv[] x = new Iv[3];
                        x[axis] = Iv.Of(G.X(axis, (long)n[axis] * pieces, tt));
                        x[u] = G.Span(u, (long)n[u] * pieces + p, tt, 1);
                        x[v] = G.Span(v, (long)n[v] * pieces + (fromLow ? p : pieces - 1 - p), tt, 1);
                        if (!(G.Eval(x[0], x[1], x[2]).F.Lo > 0)) ok = false;
                    }
                }
            }
            r = ok;
        }
        faceOk[key] = r;
        return r;
    }

    // the enclosures over the 2^T sub-boxes per axis of a cell that can hold a zero
    List<Enc> pieces(int i, int j, int k)
    {
        List<Enc> list;
        int idx = i + N * (j + N * k);
        if (cellPieces.TryGetValue(idx, out list)) return list;
        list = new List<Enc>();
        if ((G.Cell[idx] & 1) != 0) {
            int m = 1 << T;
            for (int a = 0; a < m; ++a) for (int c = 0; c < m; ++c) for (int e2 = 0; e2 < m; ++e2) {
                Enc e = G.Eval(G.Span(0, (long)i * m + a, T, 1), G.Span(1, (long)j * m + c, T, 1), G.Span(2, (long)k * m + e2, T, 1));
                if (e.F.HasZero) list.Add(e);
            }
        }
        cellPieces[idx] = list;
        return list;
    }

    // the circles of the pattern on the boundary of the box: classes of the sign-changing edges on it, joined across each
    // boundary face (two crossings: joined; four: the two edges at each inside corner)
    int circles(int[] b)
    {
        var parent = new Dictionary<long, long>();
        Func<long, long> find = null;
        find = a => { long p; if (!parent.TryGetValue(a, out p)) { parent[a] = a; return a; } return p == a ? a : (parent[a] = find(p)); };
        for (int axis = 0; axis < 3; ++axis) {
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            for (int side = 0; side < 2; ++side) {
                int[] p = new int[3];
                p[axis] = side == 0 ? b[axis] : b[axis + 3];
                for (p[u] = b[u]; p[u] < b[u + 3]; ++p[u]) for (p[v] = b[v]; p[v] < b[v + 3]; ++p[v]) {
                    int[] q = (int[])p.Clone();
                    int[] c = new int[4]; long[] e = new long[4];
                    // corners in cyclic order 0 (lowest), 1 (+u), 2 (+u +v), 3 (+v); edge a joins corner a and a + 1
                    c[0] = G.NodeSign(q[0], q[1], q[2]); e[0] = key(u, q[0], q[1], q[2]);
                    e[3] = key(v, q[0], q[1], q[2]);
                    q[u]++; c[1] = G.NodeSign(q[0], q[1], q[2]); e[1] = key(v, q[0], q[1], q[2]);
                    q[v]++; c[2] = G.NodeSign(q[0], q[1], q[2]);
                    q[u]--; c[3] = G.NodeSign(q[0], q[1], q[2]); e[2] = key(u, q[0], q[1], q[2]);
                    var crossed = new List<int>();
                    for (int a = 0; a < 4; ++a) if (c[a] != c[(a + 1) % 4]) crossed.Add(a);
                    if (crossed.Count == 2) parent[find(e[crossed[0]])] = find(e[crossed[1]]);
                    else if (crossed.Count == 4) {
                        // the two edges at an inside corner are joined: corner a has the edges a - 1 and a
                        int first = c[0] < 0 ? 0 : 1;
                        parent[find(e[(first + 3) % 4])] = find(e[first]);
                        parent[find(e[(first + 1) % 4])] = find(e[(first + 2) % 4]);
                    }
                }
            }
        }
        var roots = new HashSet<long>();
        foreach (long a in new List<long>(parent.Keys)) roots.Add(find(a));
        return roots.Count;
    }
}
