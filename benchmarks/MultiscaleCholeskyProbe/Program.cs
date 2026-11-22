// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Research probe (docs/research/MULTISCALE_SPARSE_CHOLESKY_2026-11.md): can a multiscale approximate (inverse) Cholesky factor,
// used as the preconditioner of a fixed-order conjugate gradient iteration, beat the direct SparseCholesky on the systems of
// the library? Prototype code, not a library candidate: serial, scalar, no tuning. It measures
//   - the direct solver (minimum degree and nested dissection): Factorize, nnz(L), Solve;
//   - PCG with Jacobi, IC(0), a multiscale incomplete Cholesky (coarse-to-fine sampling, eliminated fine to coarse, pattern
//     dist(i, j) <= rho * min(l_i, l_j), after Chen, Schaefer, Huang, Desbrun 2021) and a factorized sparse approximate
//     inverse on the same pattern (one independent dense solve per row: the closed form of Schaefer, Katzfuss, Owhadi
//     2021 applied to the entries of A, which is FSAI, Kolotilina and Yeremin 1993): setup time, entries, iterations to a
//     relative residual of 1e-6 / 1e-8 / 1e-12 and the time of the iteration;
//   - the floor of any PCG: iterations times one product with A.
// on procedural systems shaped like those of the library: cotan Poisson (one pinned vertex), heat (M / t + L), bi-Laplacian
// with fixed caps (BiharmonicMeshDeformer), a connection Laplacian with two unknowns per vertex (CrossField, real form) and a
// 3D grid Laplacian.
//
//   dotnet run -c Release --project benchmarks/MultiscaleCholeskyProbe -- [only=<regex>] [rho=2,3,4] [k=96,192] [k3=30,48] [direct=1]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using g3;

// symmetric matrix, both triangles, columns sorted in each row
sealed class Csr
{
    public int N; public int[] P, J; public double[] V;

    public static Csr FromTriplets(int n, long[] key, double[] val, int count)
    {
        Array.Sort(key, val, 0, count);
        var c = new Csr { N = n, P = new int[n + 1] };
        int m = 0;
        for (int i = 0; i < count; ++i) {
            if (m > 0 && key[m - 1] == key[i]) val[m - 1] += val[i];
            else { key[m] = key[i]; val[m] = val[i]; ++m; }
        }
        c.J = new int[m]; c.V = new double[m];
        for (int i = 0; i < m; ++i) { int r = (int)(key[i] >> 32); c.P[r + 1]++; c.J[i] = (int)(key[i] & 0xffffffffL); c.V[i] = val[i]; }
        for (int r = 0; r < n; ++r) c.P[r + 1] += c.P[r];
        return c;
    }

    public void Mul(double[] x, double[] y)
    {
        for (int r = 0; r < N; ++r) {
            double s = 0;
            for (int q = P[r]; q < P[r + 1]; ++q) s += V[q] * x[J[q]];
            y[r] = s;
        }
    }

    // rows and columns of the kept unknowns
    public Csr Restrict(bool[] drop, out int[] kept)
    {
        var map = new int[N]; var k = new List<int>();
        for (int i = 0; i < N; ++i) { map[i] = drop[i] ? -1 : k.Count; if (!drop[i]) k.Add(i); }
        kept = k.ToArray();
        var c = new Csr { N = kept.Length, P = new int[kept.Length + 1] };
        var j = new List<int>(); var v = new List<double>();
        for (int a = 0; a < kept.Length; ++a) {
            int r = kept[a];
            for (int q = P[r]; q < P[r + 1]; ++q) if (map[J[q]] >= 0) { j.Add(map[J[q]]); v.Add(V[q]); }
            c.P[a + 1] = j.Count;
        }
        c.J = j.ToArray(); c.V = v.ToArray();
        return c;
    }
}

sealed class TriMesh
{
    public Vector3d[] V; public int[] T;
    public int MaxVertexID { get { return V.Length; } }
    public IEnumerable<int> TriangleIndices() { return Enumerable.Range(0, T.Length / 3); }
    public Index3i GetTriangle(int t) { return new Index3i(T[3 * t], T[3 * t + 1], T[3 * t + 2]); }
    public Vector3d GetVertex(int i) { return V[i]; }
}

sealed class TripletList
{
    public long[] Key = new long[1 << 16]; public double[] Val = new double[1 << 16]; public int Count;
    public void Add(int r, int c, double v)
    {
        if (Count == Key.Length) { Array.Resize(ref Key, 2 * Count); Array.Resize(ref Val, 2 * Count); }
        Key[Count] = ((long)r << 32) | (uint)c; Val[Count++] = v;
    }
    public void AddSym(int r, int c, double v) { Add(r, c, v); Add(c, r, v); }
    public Csr ToCsr(int n) { return Csr.FromTriplets(n, Key, Val, Count); }
}

sealed class Problem
{
    public string Name; public Csr A; public Vector3d[] Pos; public int Bs = 1;    // Pos: one point per block of Bs unknowns
}

// uniform hash grid over points, for "points within r" queries with r <= cell
sealed class HashGrid
{
    readonly Dictionary<long, int> head; readonly int[] next; readonly Vector3d[] pos; readonly Vector3d min; readonly double inv;
    public HashGrid(Vector3d[] pos, Vector3d min, double cell, int capacity)
    {
        this.pos = pos; this.min = min; inv = 1.0 / cell; next = new int[pos.Length]; head = new Dictionary<long, int>(capacity);
    }
    public static Vector3d Lo(Vector3d a, Vector3d b) { return new Vector3d(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z)); }
    public static Vector3d Hi(Vector3d a, Vector3d b) { return new Vector3d(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z)); }
    static long Pack(long x, long y, long z) { return (x << 42) | (y << 21) | z; }
    void Cell(Vector3d p, out long x, out long y, out long z)
    {
        x = (long)((p.x - min.x) * inv) + 1; y = (long)((p.y - min.y) * inv) + 1; z = (long)((p.z - min.z) * inv) + 1;
    }
    public void Add(int i)
    {
        Cell(pos[i], out long x, out long y, out long z); long k = Pack(x, y, z);
        next[i] = head.TryGetValue(k, out int h) ? h : -1; head[k] = i;
    }
    public bool AnyWithin(Vector3d p, double r)
    {
        Cell(p, out long x, out long y, out long z); double r2 = r * r;
        for (int o = 0; o < 27; ++o) {
            // the own cell first: it rejects most candidates
            int oo = o == 0 ? 13 : (o <= 13 ? o - 1 : o);
            if (!head.TryGetValue(Pack(x + oo % 3 - 1, y + oo / 3 % 3 - 1, z + oo / 9 - 1), out int i)) continue;
            for (; i >= 0; i = next[i]) if (pos[i].DistanceSquared(p) <= r2) return true;
        }
        return false;
    }
    public void Within(Vector3d p, double r, List<int> result)
    {
        Cell(p, out long x, out long y, out long z); double r2 = r * r;
        for (int o = 0; o < 27; ++o) {
            if (!head.TryGetValue(Pack(x + o % 3 - 1, y + o / 3 % 3 - 1, z + o / 9 - 1), out int i)) continue;
            for (; i >= 0; i = next[i]) if (pos[i].DistanceSquared(p) <= r2) result.Add(i);
        }
    }
}

// Coarse-to-fine hierarchy of the points: level k takes, in index order, every point farther than l_k = diameter / 2^k
// from all points taken so far. The selection order is the coarse-to-fine order; l of a point is the l_k of its level.
// (A level-wise stand-in for the maximin ordering: the same nested scales, without the heap.)
sealed class Hierarchy
{
    public int[] Order;       // selection order: Order[s] = point
    public int[] Sel;         // Sel[point] = s
    public double[] Scale;    // l of a point
    public int[] LevelStart;  // first s of each level
    public double[] LevelScale;

    public Hierarchy(Vector3d[] pos)
    {
        int n = pos.Length;
        Vector3d min = pos[0], max = pos[0];
        foreach (var p in pos) { min = HashGrid.Lo(min, p); max = HashGrid.Hi(max, p); }
        double l = (max - min).Length;
        Order = new int[n]; Sel = new int[n]; Scale = new double[n];
        for (int i = 0; i < n; ++i) Sel[i] = -1;
        var starts = new List<int>(); var scales = new List<double>();
        int taken = 0;
        var left = new List<int>(n); for (int i = 0; i < n; ++i) left.Add(i);
        for (int level = 0; taken < n; ++level, l *= 0.5) {
            starts.Add(taken); scales.Add(l);
            bool last = level >= 19;                        // 2^20 cells per axis at most: the rest is one level
            var grid = last ? null : new HashGrid(pos, min, l, 2 * taken + 16);
            if (!last) for (int s = 0; s < taken; ++s) grid.Add(Order[s]);
            var rest = new List<int>(left.Count);
            foreach (int i in left) {
                if (!last && grid.AnyWithin(pos[i], l)) { rest.Add(i); continue; }
                Sel[i] = taken; Order[taken++] = i; Scale[i] = l;
                if (!last) grid.Add(i);
            }
            left = rest;
        }
        starts.Add(n);
        LevelStart = starts.ToArray(); LevelScale = scales.ToArray();
    }
}

// lower triangular sparse matrix in the elimination order, by columns, the diagonal first in each column
sealed class Lower
{
    public int N; public int[] Perm;      // Perm[position] = unknown
    public int[] ColPtr, Row; public double[] Val;
    public long Entries { get { return ColPtr[N]; } }

    // pattern of the points: column of point j holds the earlier selected (coarser) points i with dist <= rho * l_j, and the
    // neighbours of j in A that are coarser; eliminated fine to coarse; every point is a block of bs unknowns
    public static Lower MultiscalePattern(Problem pr, Hierarchy h, double rho)
    {
        int nv = pr.Pos.Length, bs = pr.Bs; Csr A = pr.A;
        Vector3d min = pr.Pos[0]; foreach (var p in pr.Pos) min = HashGrid.Lo(min, p);
        // elimination position of a point: reverse of the selection
        var colStart = new int[nv + 1]; var rows = new List<int>(nv * 16);   // by elimination position of the column
        var near = new List<int>(); var cand = new List<int>();
        var colsOf = new List<int>[h.LevelScale.Length];
        // columns are needed in elimination order (fine first), the grid of a level needs all coarser points: collect per level
        for (int level = 0; level < h.LevelScale.Length; ++level) {
            int s0 = h.LevelStart[level], s1 = h.LevelStart[level + 1];
            double r = rho * h.LevelScale[level];
            var lst = new List<int>((s1 - s0) * 16);
            bool all = level >= 19 || r >= h.LevelScale[0];
            HashGrid grid = null;
            if (!all) { grid = new HashGrid(pr.Pos, min, r, 2 * s1 + 16); for (int s = 0; s < s1; ++s) grid.Add(h.Order[s]); }
            for (int s = s0; s < s1; ++s) {
                int j = h.Order[s]; near.Clear(); cand.Clear();
                if (all && level < 19) { for (int t = 0; t < s; ++t) near.Add(t); }
                else if (grid != null) { grid.Within(pr.Pos[j], r, cand); foreach (int i in cand) if (h.Sel[i] < s) near.Add(h.Sel[i]); }
                for (int c = 0; c < bs; ++c)
                    for (int q = A.P[j * bs + c]; q < A.P[j * bs + c + 1]; ++q) { int i = A.J[q] / bs; if (h.Sel[i] < s) near.Add(h.Sel[i]); }
                near.Sort();
                lst.Add(-1 - s);                                  // column marker
                for (int t = near.Count - 1; t >= 0; --t) if (t == near.Count - 1 || near[t] != near[t + 1]) lst.Add(near[t]);   // descending selection = ascending position
            }
            colsOf[level] = lst;
        }
        // point level pattern in elimination order: position = nv - 1 - selection
        var vPtr = new int[nv + 1]; var vRow = new List<int>();
        for (int level = h.LevelScale.Length - 1; level >= 0; --level) {
            var lst = colsOf[level];
            // the columns of a level were appended in ascending selection; elimination needs descending selection
            var begin = new List<int>();
            for (int t = 0; t < lst.Count; ++t) if (lst[t] < 0) begin.Add(t);
            for (int b = begin.Count - 1; b >= 0; --b) {
                int t0 = begin[b], t1 = b + 1 < begin.Count ? begin[b + 1] : lst.Count;
                int s = -1 - lst[t0]; int p = nv - 1 - s;
                for (int t = t0 + 1; t < t1; ++t) vRow.Add(nv - 1 - lst[t]);
                vPtr[p + 1] = vRow.Count;
            }
            colsOf[level] = null;
        }
        var perm = new int[nv]; for (int s = 0; s < nv; ++s) perm[nv - 1 - s] = h.Order[s];
        return Expand(pr, perm, vPtr, vRow);
    }

    // IC(0): the pattern of A in the given order of the points
    public static Lower PatternOfA(Problem pr)
    {
        int nv = pr.Pos.Length, bs = pr.Bs; var vPtr = new int[nv + 1]; var vRow = new List<int>(); var seen = new int[nv];
        for (int i = 0; i < nv; ++i) seen[i] = -1;
        var perm = new int[nv];
        for (int j = 0; j < nv; ++j) {
            perm[j] = j; int start = vRow.Count;
            for (int c = 0; c < bs; ++c)
                for (int q = pr.A.P[j * bs + c]; q < pr.A.P[j * bs + c + 1]; ++q) { int i = pr.A.J[q] / bs; if (i > j && seen[i] != j) { seen[i] = j; vRow.Add(i); } }
            vRow.Sort(start, vRow.Count - start, null);
            vPtr[j + 1] = vRow.Count;
        }
        return Expand(pr, perm, vPtr, vRow);
    }

    // unknown level pattern from the point level pattern (rows below the diagonal point, ascending), values of A scattered in
    static Lower Expand(Problem pr, int[] vPerm, int[] vPtr, List<int> vRow)
    {
        int nv = vPerm.Length, bs = pr.Bs, n = nv * bs; Csr A = pr.A;
        var L = new Lower { N = n, Perm = new int[n], ColPtr = new int[n + 1] };
        var pos = new int[n];
        for (int p = 0; p < nv; ++p) for (int c = 0; c < bs; ++c) { L.Perm[p * bs + c] = vPerm[p] * bs + c; pos[vPerm[p] * bs + c] = p * bs + c; }
        long total = 0;
        for (int p = 0; p < nv; ++p) for (int c = 0; c < bs; ++c) total += (bs - c) + (long)(vPtr[p + 1] - vPtr[p]) * bs;
        if (total > int.MaxValue) throw new Exception("pattern too large");
        L.Row = new int[total]; L.Val = new double[total];
        var where = new int[n]; for (int i = 0; i < n; ++i) where[i] = -1;
        int m = 0;
        for (int p = 0; p < nv; ++p)
            for (int c = 0; c < bs; ++c) {
                int col = p * bs + c, start = m;
                for (int d = c; d < bs; ++d) L.Row[m++] = p * bs + d;
                for (int q = vPtr[p]; q < vPtr[p + 1]; ++q) for (int d = 0; d < bs; ++d) L.Row[m++] = vRow[q] * bs + d;
                L.ColPtr[col + 1] = m;
                for (int q = start; q < m; ++q) where[L.Row[q]] = q;
                int u = L.Perm[col];
                for (int q = A.P[u]; q < A.P[u + 1]; ++q) { int r = pos[A.J[q]]; if (r >= col) { if (where[r] < start) throw new Exception("entry of A outside the pattern"); L.Val[where[r]] = A.V[q]; } }
            }
        return L;
    }

    // incomplete Cholesky on the pattern (left looking, entries outside the pattern dropped); false on a non-positive pivot
    public bool IncompleteCholesky(double shift)
    {
        int n = N; var w = new double[n]; var mark = new int[n]; var head = new int[n]; var link = new int[n]; var posn = new int[n];
        for (int i = 0; i < n; ++i) { mark[i] = -1; head[i] = -1; }
        for (int j = 0; j < n; ++j) {
            int c0 = ColPtr[j], c1 = ColPtr[j + 1];
            for (int q = c0; q < c1; ++q) { w[Row[q]] = Val[q]; mark[Row[q]] = j; }
            double diagA = Val[c0];
            w[j] += shift * diagA;
            for (int k = head[j]; k >= 0;) {
                int nextK = link[k], p = posn[k], e = ColPtr[k + 1];
                double ljk = Val[p];
                for (int q = p; q < e; ++q) { int r = Row[q]; if (mark[r] == j) w[r] -= ljk * Val[q]; }
                if (++p < e) { posn[k] = p; int r2 = Row[p]; link[k] = head[r2]; head[r2] = k; }
                k = nextK;
            }
            double d = w[j];
            if (!(d > 1e-12 * Math.Abs(diagA))) return false;
            double ljj = Math.Sqrt(d), inv = 1.0 / ljj;
            Val[c0] = ljj;
            for (int q = c0 + 1; q < c1; ++q) Val[q] = w[Row[q]] * inv;
            if (c0 + 1 < c1) { posn[j] = c0 + 1; int r = Row[c0 + 1]; link[j] = head[r]; head[r] = j; }
        }
        return true;
    }

    // z = (L L^T)^-1 r, in the order of the unknowns of A
    public void SolveLLt(double[] r, double[] z, double[] t)
    {
        int n = N;
        for (int p = 0; p < n; ++p) t[p] = r[Perm[p]];
        for (int j = 0; j < n; ++j) {
            int c0 = ColPtr[j], c1 = ColPtr[j + 1];
            double y = t[j] / Val[c0]; t[j] = y;
            for (int q = c0 + 1; q < c1; ++q) t[Row[q]] -= Val[q] * y;
        }
        for (int j = n - 1; j >= 0; --j) {
            int c0 = ColPtr[j], c1 = ColPtr[j + 1];
            double s = t[j];
            for (int q = c0 + 1; q < c1; ++q) s -= Val[q] * t[Row[q]];
            t[j] = s / Val[c0];
        }
        for (int p = 0; p < n; ++p) z[Perm[p]] = t[p];
    }
}

// Factorized sparse approximate inverse G (lower triangular, G A G^T ~ I) on the transpose of a lower pattern: row p of G has
// the columns q <= p with (p, q) in the pattern, and is C^-T e_last for the dense Cholesky factor C of A restricted to them.
// Every row is independent of the others (the loop is parallel; a row is the same bits in any schedule).
sealed class Fsai
{
    public int N; public int[] Perm; public int[] RowPtr, Col; public double[] Val; public int MaxRow; public long DenseFlops;

    public Fsai(Csr A, Lower pat)
    {
        int n = pat.N; N = n; Perm = pat.Perm;
        RowPtr = new int[n + 1];
        for (int j = 0; j < n; ++j) for (int q = pat.ColPtr[j]; q < pat.ColPtr[j + 1]; ++q) RowPtr[pat.Row[q] + 1]++;
        for (int r = 0; r < n; ++r) { MaxRow = Math.Max(MaxRow, RowPtr[r + 1]); RowPtr[r + 1] += RowPtr[r]; }
        Col = new int[RowPtr[n]]; Val = new double[RowPtr[n]];
        var fill = (int[])RowPtr.Clone();
        for (int j = 0; j < n; ++j) for (int q = pat.ColPtr[j]; q < pat.ColPtr[j + 1]; ++q) Col[fill[pat.Row[q]]++] = j;   // ascending, the diagonal last
        var posOf = new int[n]; for (int p = 0; p < n; ++p) posOf[Perm[p]] = p;
        long flops = 0;
        Parallel.For(0, n, () => (new int[n], new double[MaxRow * MaxRow], 0L), (p, _, loc) => {
            var (local, C, fl) = loc;
            int r0 = RowPtr[p], m = RowPtr[p + 1] - r0;
            for (int a = 0; a < m; ++a) local[Col[r0 + a]] = a + 1;
            Array.Clear(C, 0, m * m);
            for (int a = 0; a < m; ++a) {
                int u = Perm[Col[r0 + a]];
                for (int q = A.P[u]; q < A.P[u + 1]; ++q) { int b = local[posOf[A.J[q]]] - 1; if (b >= 0 && b <= a) C[a * m + b] = A.V[q]; }
            }
            for (int a = 0; a < m; ++a) local[Col[r0 + a]] = 0;
            // dense Cholesky, lower, row by row
            for (int a = 0; a < m; ++a) {
                for (int b = 0; b <= a; ++b) {
                    double s = C[a * m + b];
                    for (int k = 0; k < b; ++k) s -= C[a * m + k] * C[b * m + k];
                    if (a == b) { if (!(s > 0)) throw new Exception("FSAI: a principal block of A is not positive definite"); C[a * m + a] = Math.Sqrt(s); }
                    else C[a * m + b] = s / C[b * m + b];
                }
            }
            // g = C^-T e_last
            for (int a = m - 1; a >= 0; --a) {
                double s = a == m - 1 ? 1.0 : 0.0;
                for (int k = a + 1; k < m; ++k) s -= C[k * m + a] * Val[r0 + k];
                Val[r0 + a] = s / C[a * m + a];
            }
            return (local, C, fl + (long)m * m * m / 3);
        }, loc => { lock (Perm) flops += loc.Item3; });
        DenseFlops = flops;
    }

    // z = G^T G r
    public void Apply(double[] r, double[] z, double[] t, double[] u)
    {
        int n = N;
        for (int p = 0; p < n; ++p) u[p] = r[Perm[p]];
        for (int p = 0; p < n; ++p) {
            double s = 0;
            for (int q = RowPtr[p]; q < RowPtr[p + 1]; ++q) s += Val[q] * u[Col[q]];
            t[p] = s;
        }
        Array.Clear(u, 0, n);
        for (int p = 0; p < n; ++p) {
            double s = t[p];
            for (int q = RowPtr[p]; q < RowPtr[p + 1]; ++q) u[Col[q]] += Val[q] * s;
        }
        for (int p = 0; p < n; ++p) z[Perm[p]] = u[p];
    }
}

// Approximate elimination of a symmetric diagonally dominant matrix with non-positive off-diagonals (a graph Laplacian plus a
// non-negative diagonal, held as an edge of each vertex to a ground vertex), after Kyng and Sachdeva 2016 and Gao, Kyng and
// Spielman 2023: the eliminated vertex's clique w_i w_j / W is replaced by one sampled edge per neighbour (neighbours in
// ascending weight; neighbour i gets an edge of weight w_i S_i / W to a later neighbour j drawn with probability w_j / S_i,
// S_i the weight after i), which has the clique as its mean. Vertices are taken by smallest current list length. The
// generator is seeded and the elimination serial, so the factor is the same on every run. A ~ C D C^T, C unit lower.
sealed class ApproxElimination
{
    public int N; public int[] Order, ColPtr, Row; public double[] Val, D; public long Entries { get { return ColPtr[N] + N; } }
    public int Clamped;

    public ApproxElimination(Csr A, ulong seed)
    {
        int n = A.N; N = n;
        var adj = new List<(int v, double w)>[n]; var ground = new double[n];
        for (int r = 0; r < n; ++r) {
            adj[r] = new List<(int, double)>(8); double sum = 0, diag = 0;
            for (int q = A.P[r]; q < A.P[r + 1]; ++q) {
                if (A.J[q] == r) { diag = A.V[q]; continue; }
                if (A.V[q] >= 0) { if (A.V[q] > 0) ++Clamped; continue; }      // a positive off-diagonal is left out of the preconditioner
                adj[r].Add((A.J[q], -A.V[q])); sum -= A.V[q];
            }
            ground[r] = Math.Max(0, diag - sum);
        }
        Clamped /= 2;
        var done = new bool[n]; Order = new int[n]; D = new double[n]; ColPtr = new int[n + 1];
        var rows = new List<int>(8 * n); var vals = new List<double>(8 * n);
        var pq = new PriorityQueue<int, long>(n);
        for (int i = 0; i < n; ++i) pq.Enqueue(i, ((long)adj[i].Count << 32) | (uint)i);
        var nb = new List<(int v, double w)>(); var cum = new List<double>();
        for (int p = 0; p < n;) {
            pq.TryDequeue(out int v, out long key);
            if (done[v] || (int)(key >> 32) != adj[v].Count) continue;
            // the live neighbours, equal ones merged; the ground is vertex n
            nb.Clear();
            adj[v].Sort((a, b) => a.v.CompareTo(b.v));
            foreach (var e in adj[v]) {
                if (done[e.v]) continue;
                if (nb.Count > 0 && nb[nb.Count - 1].v == e.v) nb[nb.Count - 1] = (e.v, nb[nb.Count - 1].w + e.w); else nb.Add(e);
            }
            if (ground[v] > 0) nb.Add((n, ground[v]));
            adj[v] = null; done[v] = true; Order[p] = v;
            nb.Sort((a, b) => a.w != b.w ? a.w.CompareTo(b.w) : a.v.CompareTo(b.v));
            double W = 0; cum.Clear();
            foreach (var e in nb) { W += e.w; cum.Add(W); }
            if (!(W > 0)) throw new Exception("approximate elimination: a vertex without weight (singular matrix)");
            D[p] = W;
            foreach (var e in nb) if (e.v < n) { rows.Add(e.v); vals.Add(-e.w / W); }
            ColPtr[++p] = rows.Count;
            for (int i = 0; i + 1 < nb.Count; ++i) {
                double S = W - cum[i];
                if (!(S > 0)) break;
                seed = seed * 6364136223846793005UL + 1442695040888963407UL;
                double t = cum[i] + S * ((seed >> 11) * (1.0 / 9007199254740992.0));
                int lo = i + 1, hi = nb.Count - 1;
                while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] > t) hi = mid; else lo = mid + 1; }
                int a = nb[i].v, b = nb[lo].v; double w = nb[i].w * S / W;
                if (a == n) ground[b] += w;
                else if (b == n) ground[a] += w;
                else {
                    adj[a].Add((b, w)); adj[b].Add((a, w));
                    pq.Enqueue(a, ((long)adj[a].Count << 32) | (uint)a); pq.Enqueue(b, ((long)adj[b].Count << 32) | (uint)b);
                }
            }
        }
        Row = rows.ToArray(); Val = vals.ToArray();
    }

    // z = (C D C^T)^-1 r
    public void Apply(double[] r, double[] z, double[] t)
    {
        int n = N; Array.Copy(r, t, n);
        for (int p = 0; p < n; ++p) {
            double y = t[Order[p]];
            for (int q = ColPtr[p]; q < ColPtr[p + 1]; ++q) t[Row[q]] -= Val[q] * y;
        }
        for (int p = n - 1; p >= 0; --p) {
            int v = Order[p]; double s = t[v] / D[p];
            for (int q = ColPtr[p]; q < ColPtr[p + 1]; ++q) s -= Val[q] * z[Row[q]];
            z[v] = s;
        }
    }
}

static class Program
{
    static ulong Next(ref ulong s) { s = s * 6364136223846793005UL + 1442695040888963407UL; return s >> 11; }

    // ---------------------------------------------------------------- inputs

    // cube sphere with k vertices per cube edge (6 (k - 1)^2 + 2 vertices), every quad split along its shorter diagonal, with a
    // smooth radial bump (Sphere3Generator_NormalizedCube takes minutes at 200k vertices)
    static TriMesh BumpySphere(int k)
    {
        int m = k - 1; var id = new Dictionary<long, int>(); var V = new List<Vector3d>(); var T = new List<int>();
        int vertex(int x, int y, int z)
        {
            long key = ((long)x << 42) | ((long)y << 21) | (long)z;
            if (id.TryGetValue(key, out int v)) return v;
            var p = new Vector3d(2.0 * x / m - 1, 2.0 * y / m - 1, 2.0 * z / m - 1).Normalized;
            p *= 1.0 + 0.15 * Math.Sin(3 * p.x + 0.3) * Math.Sin(4 * p.y + 0.7) * Math.Cos(5 * p.z);
            id[key] = V.Count; V.Add(p); return V.Count - 1;
        }
        void tri(int a, int b, int c)
        {
            if ((V[b] - V[a]).Cross(V[c] - V[a]).Dot(V[a] + V[b] + V[c]) < 0) { int t = b; b = c; c = t; }
            T.Add(a); T.Add(b); T.Add(c);
        }
        for (int axis = 0; axis < 3; ++axis)
            for (int side = 0; side <= m; side += m)
                for (int u = 0; u < m; ++u)
                    for (int v = 0; v < m; ++v) {
                        var q = new int[4];
                        for (int c = 0; c < 4; ++c) {
                            int uu = u + (c == 1 || c == 2 ? 1 : 0), vv = v + (c >= 2 ? 1 : 0);
                            q[c] = axis == 0 ? vertex(side, uu, vv) : axis == 1 ? vertex(vv, side, uu) : vertex(uu, vv, side);
                        }
                        if (V[q[0]].DistanceSquared(V[q[2]]) <= V[q[1]].DistanceSquared(V[q[3]])) { tri(q[0], q[1], q[2]); tri(q[0], q[2], q[3]); }
                        else { tri(q[0], q[1], q[3]); tri(q[1], q[2], q[3]); }
                    }
        return new TriMesh { V = V.ToArray(), T = T.ToArray() };
    }

    // cotan Laplacian (positive semidefinite, rows sum to zero) and lumped mass
    static Csr Cotan(TriMesh m, out double[] mass, out double meanEdge, bool clamp)
    {
        int n = m.MaxVertexID; var t = new TripletList(); mass = new double[n];
        var w = new Dictionary<long, double>();
        foreach (int tid in m.TriangleIndices()) {
            Index3i tri = m.GetTriangle(tid);
            Vector3d a = m.GetVertex(tri.a), b = m.GetVertex(tri.b), c = m.GetVertex(tri.c);
            double area2 = (b - a).Cross(c - a).Length;
            for (int k = 0; k < 3; ++k) {
                int i = tri[(k + 1) % 3], j = tri[(k + 2) % 3];
                Vector3d o = m.GetVertex(tri[k]), e1 = m.GetVertex(i) - o, e2 = m.GetVertex(j) - o;
                double cot = e1.Dot(e2) / area2;
                long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
                w.TryGetValue(key, out double old); w[key] = old + 0.5 * cot;
                mass[tri[k]] += area2 / 6;
            }
        }
        double es = 0;
        foreach (var kv in w.OrderBy(kv => kv.Key)) {
            int i = (int)(kv.Key >> 32), j = (int)(kv.Key & 0xffffffffL); double wij = kv.Value;
            if (clamp && wij < 1e-6) wij = 1e-6;
            t.AddSym(i, j, -wij); t.Add(i, i, wij); t.Add(j, j, wij);
            es += m.GetVertex(i).Distance(m.GetVertex(j));
        }
        meanEdge = es / w.Count;
        return t.ToCsr(n);
    }

    static Vector3d[] Positions(TriMesh m, int[] kept)
    {
        var p = new Vector3d[kept.Length];
        for (int i = 0; i < kept.Length; ++i) p[i] = m.GetVertex(kept[i]);
        return p;
    }

    static Problem Poisson(TriMesh m)
    {
        Csr L = Cotan(m, out _, out _, false);
        var drop = new bool[L.N]; drop[0] = true;
        Csr A = L.Restrict(drop, out int[] kept);
        return new Problem { Name = "poisson", A = A, Pos = Positions(m, kept) };
    }

    static Problem Heat(TriMesh m)
    {
        Csr L = Cotan(m, out double[] mass, out double h, false);
        var t = new TripletList(); double time = h * h;
        for (int r = 0; r < L.N; ++r) { t.Add(r, r, mass[r] / time); for (int q = L.P[r]; q < L.P[r + 1]; ++q) t.Add(r, L.J[q], L.V[q]); }
        return new Problem { Name = "heat", A = t.ToCsr(L.N), Pos = Positions(m, Enumerable.Range(0, L.N).ToArray()) };
    }

    // Q = L M^-1 L, the caps |z| > 0.9 fixed (handle and anchor regions of BiharmonicMeshDeformer)
    static Problem Bilaplacian(TriMesh m)
    {
        Csr L = Cotan(m, out double[] mass, out _, false);
        int n = L.N; var t = new TripletList();
        for (int k = 0; k < n; ++k) {
            double inv = 1.0 / mass[k];
            for (int a = L.P[k]; a < L.P[k + 1]; ++a) for (int b = L.P[k]; b < L.P[k + 1]; ++b) t.Add(L.J[a], L.J[b], L.V[a] * inv * L.V[b]);
        }
        Csr Q = t.ToCsr(n);
        var drop = new bool[n];
        for (int i = 0; i < n; ++i) { Vector3d p = m.GetVertex(i); drop[i] = Math.Abs(p.z) > 0.9 * p.Length; }
        Csr A = Q.Restrict(drop, out int[] kept);
        return new Problem { Name = "bilaplacian", A = A, Pos = Positions(m, kept) };
    }

    // connection Laplacian of a 4-direction field in real form (two unknowns per vertex) plus the 1e-8 mass shift of CrossField
    static Problem Connection(TriMesh m)
    {
        Csr L = Cotan(m, out double[] mass, out _, true);
        int n = L.N; var normal = new Vector3d[n];
        foreach (int tid in m.TriangleIndices()) {
            Index3i tri = m.GetTriangle(tid);
            Vector3d fn = (m.GetVertex(tri.b) - m.GetVertex(tri.a)).Cross(m.GetVertex(tri.c) - m.GetVertex(tri.a));
            normal[tri.a] += fn; normal[tri.b] += fn; normal[tri.c] += fn;
        }
        var e1 = new Vector3d[n]; var e2 = new Vector3d[n];
        for (int i = 0; i < n; ++i) {
            Vector3d nn = normal[i].Normalized;
            Vector3d ax = Math.Abs(nn.x) < 0.8 ? Vector3d.AxisX : Vector3d.AxisY;
            e1[i] = nn.Cross(ax).Normalized; e2[i] = nn.Cross(e1[i]);
        }
        double meanMass = mass.Sum() / n;
        var t = new TripletList();
        for (int i = 0; i < n; ++i) {
            double sh = 1e-8 * mass[i] / meanMass;
            for (int q = L.P[i]; q < L.P[i + 1]; ++q) {
                int j = L.J[q];
                if (j == i) { t.Add(2 * i, 2 * i, L.V[q] + sh); t.Add(2 * i + 1, 2 * i + 1, L.V[q] + sh); continue; }
                Vector3d d = m.GetVertex(j) - m.GetVertex(i);
                double rho = 4 * (Math.Atan2(d.Dot(e2[j]), d.Dot(e1[j])) - Math.Atan2(d.Dot(e2[i]), d.Dot(e1[i])));
                double a = L.V[q] * Math.Cos(rho), b = L.V[q] * Math.Sin(rho);   // block (j, i) = w R(rho); block (i, j) is its transpose
                t.Add(2 * j, 2 * i, a); t.Add(2 * j, 2 * i + 1, -b); t.Add(2 * j + 1, 2 * i, b); t.Add(2 * j + 1, 2 * i + 1, a);
            }
        }
        return new Problem { Name = "connection bs=2", A = t.ToCsr(2 * n), Pos = Positions(m, Enumerable.Range(0, n).ToArray()), Bs = 2 };
    }

    // 7-point Laplacian of a k^3 grid with Dirichlet walls and jittered edge weights
    static Problem Grid3(int k)
    {
        ulong s = 1; int n = k * k * k; var t = new TripletList(); var pos = new Vector3d[n]; var diag = new double[n];
        for (int z = 0; z < k; ++z) for (int y = 0; y < k; ++y) for (int x = 0; x < k; ++x) {
            int a = (z * k + y) * k + x; pos[a] = new Vector3d(x, y, z);
            int[] nb = { x + 1 < k ? a + 1 : -1, y + 1 < k ? a + k : -1, z + 1 < k ? a + k * k : -1 };
            foreach (int b in nb) {
                double w = 0.5 + (Next(ref s) % 64) / 64.0;
                if (b < 0) { diag[a] += w; continue; }
                t.AddSym(a, b, -w); diag[a] += w; diag[b] += w;
            }
            if (x == 0) diag[a] += 1; if (y == 0) diag[a] += 1; if (z == 0) diag[a] += 1;
        }
        for (int a = 0; a < n; ++a) t.Add(a, a, diag[a]);
        return new Problem { Name = "grid3d laplacian", A = t.ToCsr(n), Pos = pos };
    }

    // ---------------------------------------------------------------- solvers

    struct PcgResult { public int It6, It8, It12; public double Ms8, Ms12, TrueRes; public bool Converged; }

    // conjugate gradients with fixed-order sums; iterations and time at relative residuals 1e-6, 1e-8, 1e-12
    static PcgResult Pcg(Csr A, double[] b, Action<double[], double[]> precond, int maxIt)
    {
        int n = A.N; var x = new double[n]; var r = (double[])b.Clone(); var z = new double[n]; var p = new double[n]; var ap = new double[n];
        var res = new PcgResult();
        double bn = Math.Sqrt(Dot(b, b));
        var sw = Stopwatch.StartNew();
        precond(r, z); Array.Copy(z, p, n);
        double rz = Dot(r, z);
        for (int it = 1; it <= maxIt; ++it) {
            A.Mul(p, ap);
            double alpha = rz / Dot(p, ap);
            for (int i = 0; i < n; ++i) { x[i] += alpha * p[i]; r[i] -= alpha * ap[i]; }
            double rn = Math.Sqrt(Dot(r, r)) / bn;
            if (res.It6 == 0 && rn <= 1e-6) res.It6 = it;
            if (res.It8 == 0 && rn <= 1e-8) { res.It8 = it; res.Ms8 = sw.Elapsed.TotalMilliseconds; }
            if (rn <= 1e-12) { res.It12 = it; res.Ms12 = sw.Elapsed.TotalMilliseconds; res.Converged = true; break; }
            if (!(rn < 1e30)) break;
            precond(r, z);
            double rz2 = Dot(r, z), beta = rz2 / rz; rz = rz2;
            for (int i = 0; i < n; ++i) p[i] = z[i] + beta * p[i];
        }
        A.Mul(x, ap); double e = 0;
        for (int i = 0; i < n; ++i) { double d = ap[i] - b[i]; e += d * d; }
        res.TrueRes = Math.Sqrt(e) / bn;
        return res;
    }

    static double Dot(double[] a, double[] b) { double s = 0; for (int i = 0; i < a.Length; ++i) s += a[i] * b[i]; return s; }

    static string Fmt(PcgResult r)
    {
        string it(int v) { return v == 0 ? "-" : v.ToString(); }
        return $"{it(r.It6)} / {it(r.It8)} / {it(r.It12)} | {(r.It8 == 0 ? "-" : r.Ms8.ToString("F0"))} / {(r.It12 == 0 ? "-" : r.Ms12.ToString("F0"))}";
    }

    static void Direct(Problem pr, CholeskyOrdering ord, double[] b)
    {
        Csr A = pr.A; GC.Collect();
        var c = new SparseCholesky(A.N, pr.Bs) { Ordering = ord };
        for (int r = 0; r < A.N; ++r) for (int q = A.P[r]; q < A.P[r + 1]; ++q) if (A.J[q] <= r) c.Add(r, A.J[q], A.V[q]);
        var sw = Stopwatch.StartNew();
        if (!c.Factorize()) { Console.WriteLine($"| direct {ord} | failed: {c.Error} |"); return; }
        double f = sw.Elapsed.TotalSeconds;
        var x = new double[A.N]; double best = double.MaxValue;
        for (int k = 0; k < 3; ++k) { sw.Restart(); c.Solve(b, x); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); }
        var ax = new double[A.N]; A.Mul(x, ax); double e = 0, bn = 0;
        for (int i = 0; i < A.N; ++i) { double d = ax[i] - b[i]; e += d * d; bn += b[i] * b[i]; }
        Console.WriteLine($"| direct {(ord == CholeskyOrdering.MinimumDegree ? "AMD" : "ND")} | {f:F2} | {c.FactorNonZeros / 1e6:F2}M | 1 | {best:F1} | {Math.Sqrt(e / bn):E1} |");
    }

    static void Run(Problem pr, double[] rhos, bool direct, bool fsai)
    {
        Csr A = pr.A; int n = A.N;
        ulong s = 99; var b = new double[n]; for (int i = 0; i < n; ++i) b[i] = (Next(ref s) % 2048) / 1024.0 - 1.0;
        var t1 = new double[n]; var t2 = new double[n];
        double spmv = double.MaxValue; var swm = new Stopwatch();
        for (int k = 0; k < 5; ++k) { swm.Restart(); A.Mul(b, t1); spmv = Math.Min(spmv, swm.Elapsed.TotalMilliseconds); }
        Console.WriteLine();
        Console.WriteLine($"### {pr.Name}, n = {n}, nnz(A) = {A.P[n] / 1e6:F2}M, one product with A {spmv:F2} ms");
        Console.WriteLine();
        Console.WriteLine("| method | setup (s) | entries | iterations 1e-6 / 1e-8 / 1e-12 | solve to 1e-8 / 1e-12 (ms) | true residual |");
        Console.WriteLine("|---|---|---|---|---|---|");
        if (direct) {
            Direct(pr, CholeskyOrdering.MinimumDegree, b);
            Direct(pr, CholeskyOrdering.NestedDissection, b);
        }
        int maxIt = 3000;
        {
            var dinv = new double[n];
            for (int r = 0; r < n; ++r) for (int q = A.P[r]; q < A.P[r + 1]; ++q) if (A.J[q] == r) dinv[r] = 1.0 / A.V[q];
            PcgResult r0 = Pcg(A, b, (r, z) => { for (int i = 0; i < n; ++i) z[i] = dinv[i] * r[i]; }, maxIt);
            Console.WriteLine($"| PCG Jacobi | 0 | {n / 1e6:F2}M | {Fmt(r0)} | {r0.TrueRes:E1} |");
        }
        {
            var sw = Stopwatch.StartNew();
            Lower L = Lower.PatternOfA(pr); var keep = (double[])L.Val.Clone(); double shift = 0;
            while (!L.IncompleteCholesky(shift)) { shift = shift == 0 ? 1e-3 : 4 * shift; Array.Copy(keep, L.Val, keep.Length); }
            double setup = sw.Elapsed.TotalSeconds;
            PcgResult r0 = Pcg(A, b, (r, z) => L.SolveLLt(r, z, t1), maxIt);
            Console.WriteLine($"| PCG IC(0){(shift > 0 ? $" shift {shift:G2}" : "")} | {setup:F2} | {L.Entries / 1e6:F2}M | {Fmt(r0)} | {r0.TrueRes:E1} |");
        }
        if (pr.Bs == 1) {
            // share of the positive off-diagonals in the sum of the absolute off-diagonals: the method needs it to be (near) zero
            double pos = 0, off = 0;
            for (int r = 0; r < n; ++r) for (int q = A.P[r]; q < A.P[r + 1]; ++q) if (A.J[q] != r) { off += Math.Abs(A.V[q]); if (A.V[q] > 0) pos += A.V[q]; }
            if (pos > 0.05 * off) Console.WriteLine($"| PCG approximate elimination | not applicable: positive off-diagonals hold {100.0 * pos / off:F0} % of the off-diagonal weight | | | | |");
            else {
                var sw = Stopwatch.StartNew();
                var ae = new ApproxElimination(A, 1);
                double setup = sw.Elapsed.TotalSeconds;
                PcgResult r0 = Pcg(A, b, (r, z) => ae.Apply(r, z, t1), maxIt);
                Console.WriteLine($"| PCG approximate elimination seed 1{(ae.Clamped > 0 ? $" ({ae.Clamped} positive off-diagonals left out)" : "")} | {setup:F2} | {ae.Entries / 1e6:F2}M | {Fmt(r0)} | {r0.TrueRes:E1} |");
            }
        }
        var swh = Stopwatch.StartNew();
        var h = new Hierarchy(pr.Pos);
        double order = swh.Elapsed.TotalSeconds;
        foreach (double rho in rhos) {
            GC.Collect();
            var sw = Stopwatch.StartNew();
            Lower L = Lower.MultiscalePattern(pr, h, rho);
            double pattern = sw.Elapsed.TotalSeconds;
            Fsai G = null; double fsaiS = 0;
            if (fsai) { sw.Restart(); G = new Fsai(A, L); fsaiS = sw.Elapsed.TotalSeconds; }
            sw.Restart();
            var keep = (double[])L.Val.Clone(); double shift = 0;
            while (!L.IncompleteCholesky(shift)) { shift = shift == 0 ? 1e-3 : 4 * shift; Array.Copy(keep, L.Val, keep.Length); }
            keep = null;
            double numeric = sw.Elapsed.TotalSeconds;
            PcgResult r0 = Pcg(A, b, (r, z) => L.SolveLLt(r, z, t1), maxIt);
            Console.WriteLine($"| PCG multiscale IC rho={rho:G3}{(shift > 0 ? $" shift {shift:G2}" : "")} | {order:F2} + {pattern:F2} + {numeric:F2} | {L.Entries / 1e6:F2}M | {Fmt(r0)} | {r0.TrueRes:E1} |");
            if (fsai) {
                PcgResult r1 = Pcg(A, b, (r, z) => G.Apply(r, z, t1, t2), maxIt);
                Console.WriteLine($"| PCG multiscale FSAI rho={rho:G3} (max row {G.MaxRow}, {G.DenseFlops / 1e9:F2}G dense flops, {Environment.ProcessorCount} threads) | {order:F2} + {pattern:F2} + {fsaiS:F2} | {G.RowPtr[n] / 1e6:F2}M | {Fmt(r1)} | {r1.TrueRes:E1} |");
            }
        }
    }

    static int Main(string[] args)
    {
        var a = args.Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => x.Length > 1 ? x[1] : "");
        string only = a.ContainsKey("only") ? a["only"] : ".*";
        double[] rhos = (a.ContainsKey("rho") ? a["rho"] : "2,3,4").Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        int[] ks = (a.ContainsKey("k") ? a["k"] : "96,192").Split(',').Where(x => x.Length > 0).Select(int.Parse).ToArray();
        int[] k3s = (a.ContainsKey("k3") ? a["k3"] : "30,48").Split(',').Where(x => x.Length > 0).Select(int.Parse).ToArray();
        bool direct = !a.ContainsKey("direct") || a["direct"] != "0";
        bool fsai = !a.ContainsKey("fsai") || a["fsai"] != "0";
        Console.WriteLine($"CPUs: {Environment.ProcessorCount} (the direct solver and the FSAI setup use them; everything else in the probe is serial)");
        {   // warm up the JIT
            var w = Poisson(BumpySphere(12)); var q = Console.Out; Console.SetOut(System.IO.TextWriter.Null);
            Run(w, new[] { 2.0 }, true, true); Console.SetOut(q);
        }
        var makers = new (string name, Func<TriMesh, Problem> make)[] { ("poisson", Poisson), ("heat", Heat), ("bilaplacian", Bilaplacian), ("connection", Connection) };
        foreach (int k in ks) {
            TriMesh m = null;
            foreach (var mk in makers) {
                if (!Regex.IsMatch($"sphere k={k} {mk.name}", only)) continue;
                if (m == null) m = BumpySphere(k);
                Problem pr = mk.make(m); pr.Name = $"sphere k={k} {pr.Name}";
                Run(pr, rhos, direct, fsai);
            }
        }
        foreach (int k in k3s) {
            if (!Regex.IsMatch($"grid3d k={k}", only)) continue;
            Problem pr = Grid3(k); pr.Name = $"grid3d k={k}";
            Run(pr, rhos, direct, fsai);
        }
        return 0;
    }
}
