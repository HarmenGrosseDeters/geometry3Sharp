// PROTOTYPE (exact-number study, runs/exact-numbers; not library code).
// P2: the sign of a predicate polynomial over doubles by exact accumulation of its monomials.
// Every finite double is m * 2^e (|m| < 2^53). The predicate is a homogeneous polynomial of degree d in the input
// doubles (the determinant with a column of ones, expanded: no differences), so it is a sum of T signed monomials,
// each an integer below 2^(53 d) times 2^(sum of d exponents). With emin the smallest exponent of a nonzero input the
// monomials are integers times 2^(d emin); they are added into two magnitude accumulators (positive and negative
// monomials) of ceil((d (emax - emin) + 53 d + log2 T) / 64) + 1 limbs on the stack, and the sign is their comparison.
// No allocation, no range restriction: the worst case (emin = -1074, emax = 971) is 101 limbs for orient3d,
// 134 for incircle, 167 for insphere.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

static unsafe class Dyadic
{
    public struct Term { public sbyte Sign; public byte I0, I1, I2, I3, I4; }

    public static readonly Term[] Orient2dTerms = Build(2, false, 1);
    public static readonly Term[] Orient3dTerms = Build(3, false, 1);
    public static readonly Term[] InCircleTerms = Build(2, true, 1);
    public static readonly Term[] InSphereTerms = Build(3, true, 1);

    /// <summary>
    /// The monomials of det [ p_i.x .. p_i.(dim) (lift) 1 ] over the dim + 1 (+ 1 with a lift) points, coordinates
    /// indexed as point * dim + axis; the lift column is the sum of the squared coordinates.
    /// </summary>
    public static Term[] Build(int dim, bool lift, int overallSign)
    {
        int n = dim + (lift ? 2 : 1);
        var terms = new List<Term>();
        int[] perm = new int[n];
        for (int i = 0; i < n; ++i) perm[i] = i;
        Permute(perm, 0, () => {
            // row r takes column perm[r]; columns 0..dim-1 coordinates, dim = lift (if any), last = ones
            int sign = overallSign * Parity(perm);
            var idx = new List<byte>();
            int liftRow = -1;
            for (int r = 0; r < n; ++r) {
                int c = perm[r];
                if (c < dim) idx.Add((byte)(r * dim + c));
                else if (lift && c == dim) liftRow = r;
            }
            if (liftRow < 0) terms.Add(Make(sign, idx));
            else
                for (int k = 0; k < dim; ++k) {
                    var j = new List<byte>(idx) { (byte)(liftRow * dim + k), (byte)(liftRow * dim + k) };
                    terms.Add(Make(sign, j));
                }
        });
        return terms.ToArray();
    }

    static Term Make(int sign, List<byte> idx)
    {
        var t = new Term { Sign = (sbyte)sign, I0 = 255, I1 = 255, I2 = 255, I3 = 255, I4 = 255 };
        if (idx.Count > 0) t.I0 = idx[0];
        if (idx.Count > 1) t.I1 = idx[1];
        if (idx.Count > 2) t.I2 = idx[2];
        if (idx.Count > 3) t.I3 = idx[3];
        if (idx.Count > 4) t.I4 = idx[4];
        return t;
    }

    static void Permute(int[] p, int k, Action visit)
    {
        if (k == p.Length) { visit(); return; }
        for (int i = k; i < p.Length; ++i) {
            (p[k], p[i]) = (p[i], p[k]);
            Permute(p, k + 1, visit);
            (p[k], p[i]) = (p[i], p[k]);
        }
    }

    static int Parity(int[] p)
    {
        int s = 1;
        for (int i = 0; i < p.Length; ++i)
            for (int j = i + 1; j < p.Length; ++j)
                if (p[i] > p[j]) s = -s;
        return s;
    }

    /// <summary> the sign of the polynomial `terms` (degree `degree`) at the n doubles v; table-driven (a generator would unroll it) </summary>
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Sign(double* v, int n, Term[] terms, int degree)
    {
        ulong* mag = stackalloc ulong[16];
        int* ex = stackalloc int[16];
        ulong negMask = 0;
        int emin = int.MaxValue, emax = int.MinValue;
        for (int i = 0; i < n; ++i) {
            long bits = BitConverter.DoubleToInt64Bits(v[i]);
            int e = (int)((bits >> 52) & 0x7FF);
            ulong f = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (e == 0x7FF) throw new ArgumentException("ExactPredicates: coordinates must be finite");
            if (e == 0) e = 1; else f |= 1UL << 52;
            mag[i] = f; ex[i] = e;
            if (bits < 0) negMask |= 1UL << i;
            if (f != 0) { if (e < emin) emin = e; if (e > emax) emax = e; }
        }
        if (emin == int.MaxValue) return 0;
        // limbs: the largest shift degree (emax - emin), a monomial below 2^(53 degree), at most 512 monomials
        int limbs = ((degree * (emax - emin)) >> 6) + ((53 * degree + 9) >> 6) + 2;
        ulong* pos = stackalloc ulong[2 * limbs];
        ulong* neg = pos + limbs;
        new Span<ulong>(pos, 2 * limbs).Clear();
        int baseExp = degree * emin;
        ulong* r = stackalloc ulong[8];
        fixed (Term* tp = terms) {
            for (int t = 0; t < terms.Length; ++t) {
                Term* q = tp + t;
                int i0 = q->I0;
                ulong m = mag[i0];
                if (m == 0) continue;
                int e = ex[i0];
                ulong sgn = negMask >> i0;
                r[0] = m;
                int len = 1;
                byte* idx = &q->I1;
                bool zero = false;
                for (int k = 1; k < degree; ++k) {
                    int ik = idx[k - 1];
                    ulong c = mag[ik];
                    if (c == 0) { zero = true; break; }
                    e += ex[ik];
                    sgn ^= negMask >> ik;
                    ulong carry = 0;
                    for (int j = 0; j < len; ++j) {
                        ulong lo, hi = Math.BigMul(r[j], c, out lo);
                        lo += carry;
                        hi += (lo < carry) ? 1UL : 0UL;
                        r[j] = lo;
                        carry = hi;
                    }
                    r[len++] = carry;
                }
                if (zero) continue;
                bool negative = ((sgn & 1) != 0) != (q->Sign < 0);
                int off = e - baseExp;
                ulong* a = (negative ? neg : pos) + (off >> 6);
                int s = off & 63;
                ulong cy = 0, prev = 0;
                for (int j = 0; j < len; ++j) {
                    ulong x = r[j];
                    ulong y = (s == 0) ? x : ((x << s) | (prev >> (64 - s)));
                    prev = x;
                    ulong sum = a[j] + y;
                    ulong c1 = (sum < y) ? 1UL : 0UL;
                    ulong sum2 = sum + cy;
                    c1 += (sum2 < cy) ? 1UL : 0UL;
                    a[j] = sum2;
                    cy = c1;
                }
                {
                    ulong y = (s == 0) ? 0 : (prev >> (64 - s));
                    int j = len;
                    ulong sum = a[j] + y;
                    ulong c1 = (sum < y) ? 1UL : 0UL;
                    ulong sum2 = sum + cy;
                    c1 += (sum2 < cy) ? 1UL : 0UL;
                    a[j] = sum2;
                    cy = c1;
                    while (cy != 0) {
                        ++j;
                        a[j] += 1;
                        cy = (a[j] == 0) ? 1UL : 0UL;
                    }
                }
            }
        }
        for (int i = limbs - 1; i >= 0; --i)
            if (pos[i] != neg[i])
                return (pos[i] > neg[i]) ? 1 : -1;
        return 0;
    }

    // ---- unrolled orient3d (what a generator would emit): 24 monomials of degree 3 ----

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Add3(ulong* pos, ulong* neg, ulong* mag, int* ex, ulong negMask, int baseExp, int i, int j, int k, bool minus)
    {
        ulong a = mag[i], b = mag[j], c = mag[k];
        if (a == 0 || b == 0 || c == 0) return;
        ulong lo, hi = Math.BigMul(a, b, out lo);
        ulong l1, h1 = Math.BigMul(lo, c, out l1);
        ulong l2, h2 = Math.BigMul(hi, c, out l2);
        ulong r0 = l1, r1 = h1 + l2, r2 = h2 + ((r1 < l2) ? 1UL : 0UL);
        bool negative = ((((negMask >> i) ^ (negMask >> j) ^ (negMask >> k)) & 1) != 0) != minus;
        int off = ex[i] + ex[j] + ex[k] - baseExp;
        ulong* p = (negative ? neg : pos) + (off >> 6);
        int s = off & 63;
        ulong t0, t1, t2, t3;
        if (s == 0) { t0 = r0; t1 = r1; t2 = r2; t3 = 0; }
        else { t0 = r0 << s; t1 = (r1 << s) | (r0 >> (64 - s)); t2 = (r2 << s) | (r1 >> (64 - s)); t3 = r2 >> (64 - s); }
        ulong x = p[0] + t0; ulong cy = (x < t0) ? 1UL : 0UL; p[0] = x;
        x = p[1] + t1; ulong c2 = (x < t1) ? 1UL : 0UL; x += cy; c2 += (x < cy) ? 1UL : 0UL; p[1] = x; cy = c2;
        x = p[2] + t2; c2 = (x < t2) ? 1UL : 0UL; x += cy; c2 += (x < cy) ? 1UL : 0UL; p[2] = x; cy = c2;
        x = p[3] + t3; c2 = (x < t3) ? 1UL : 0UL; x += cy; c2 += (x < cy) ? 1UL : 0UL; p[3] = x; cy = c2;
        int q = 3;
        while (cy != 0) { ++q; p[q] += 1; cy = (p[q] == 0) ? 1UL : 0UL; }
    }

    // p . (q x r) with the sign `minus`: six monomials
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Det3(ulong* pos, ulong* neg, ulong* mag, int* ex, ulong negMask, int baseExp, int p, int q, int r, bool minus)
    {
        Add3(pos, neg, mag, ex, negMask, baseExp, p, q + 1, r + 2, minus);
        Add3(pos, neg, mag, ex, negMask, baseExp, p, q + 2, r + 1, !minus);
        Add3(pos, neg, mag, ex, negMask, baseExp, p + 1, q + 2, r, minus);
        Add3(pos, neg, mag, ex, negMask, baseExp, p + 1, q, r + 2, !minus);
        Add3(pos, neg, mag, ex, negMask, baseExp, p + 2, q, r + 1, minus);
        Add3(pos, neg, mag, ex, negMask, baseExp, p + 2, q + 1, r, !minus);
    }

    /// <summary> sign of ((b - a) x (c - a)) . (d - a) for v = (a, b, c, d), as ExactPredicates.orient3d_bigint </summary>
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Orient3d(double* v)
    {
        ulong* mag = stackalloc ulong[12];
        int* ex = stackalloc int[12];
        ulong negMask = 0;
        int emin = int.MaxValue, emax = int.MinValue;
        for (int i = 0; i < 12; ++i) {
            long bits = BitConverter.DoubleToInt64Bits(v[i]);
            int e = (int)((bits >> 52) & 0x7FF);
            ulong f = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (e == 0x7FF) throw new ArgumentException("ExactPredicates: coordinates must be finite");
            if (e == 0) e = 1; else f |= 1UL << 52;
            mag[i] = f; ex[i] = e;
            if (bits < 0) negMask |= 1UL << i;
            if (f != 0) { if (e < emin) emin = e; if (e > emax) emax = e; }
        }
        if (emin == int.MaxValue) return 0;
        int limbs = ((3 * (emax - emin)) >> 6) + 5;
        ulong* pos = stackalloc ulong[2 * limbs];
        ulong* neg = pos + limbs;
        new Span<ulong>(pos, 2 * limbs).Clear();
        int baseExp = 3 * emin;
        // det(b - a, c - a, d - a) = det(b, c, d) - det(a, c, d) + det(a, b, d) - det(a, b, c)
        Det3(pos, neg, mag, ex, negMask, baseExp, 3, 6, 9, false);
        Det3(pos, neg, mag, ex, negMask, baseExp, 0, 6, 9, true);
        Det3(pos, neg, mag, ex, negMask, baseExp, 0, 3, 9, false);
        Det3(pos, neg, mag, ex, negMask, baseExp, 0, 3, 6, true);
        for (int i = limbs - 1; i >= 0; --i)
            if (pos[i] != neg[i])
                return (pos[i] > neg[i]) ? 1 : -1;
        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Add2(ulong* pos, ulong* neg, ulong* mag, int* ex, ulong negMask, int baseExp, int i, int j, bool minus)
    {
        ulong a = mag[i], b = mag[j];
        if (a == 0 || b == 0) return;
        ulong r0, r1 = Math.BigMul(a, b, out r0);
        bool negative = ((((negMask >> i) ^ (negMask >> j)) & 1) != 0) != minus;
        int off = ex[i] + ex[j] - baseExp;
        ulong* p = (negative ? neg : pos) + (off >> 6);
        int s = off & 63;
        ulong t0, t1, t2;
        if (s == 0) { t0 = r0; t1 = r1; t2 = 0; }
        else { t0 = r0 << s; t1 = (r1 << s) | (r0 >> (64 - s)); t2 = r1 >> (64 - s); }
        ulong x = p[0] + t0; ulong cy = (x < t0) ? 1UL : 0UL; p[0] = x;
        x = p[1] + t1; ulong c2 = (x < t1) ? 1UL : 0UL; x += cy; c2 += (x < cy) ? 1UL : 0UL; p[1] = x; cy = c2;
        x = p[2] + t2; c2 = (x < t2) ? 1UL : 0UL; x += cy; c2 += (x < cy) ? 1UL : 0UL; p[2] = x; cy = c2;
        int q = 2;
        while (cy != 0) { ++q; p[q] += 1; cy = (p[q] == 0) ? 1UL : 0UL; }
    }

    /// <summary> sign of (b - a) x (c - a) for v = (a, b, c), as ExactPredicates.orient2d_bigint </summary>
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Orient2d(double* v)
    {
        ulong* mag = stackalloc ulong[6];
        int* ex = stackalloc int[6];
        ulong negMask = 0;
        int emin = int.MaxValue, emax = int.MinValue;
        for (int i = 0; i < 6; ++i) {
            long bits = BitConverter.DoubleToInt64Bits(v[i]);
            int e = (int)((bits >> 52) & 0x7FF);
            ulong f = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (e == 0x7FF) throw new ArgumentException("ExactPredicates: coordinates must be finite");
            if (e == 0) e = 1; else f |= 1UL << 52;
            mag[i] = f; ex[i] = e;
            if (bits < 0) negMask |= 1UL << i;
            if (f != 0) { if (e < emin) emin = e; if (e > emax) emax = e; }
        }
        if (emin == int.MaxValue) return 0;
        int limbs = ((2 * (emax - emin)) >> 6) + 4;
        ulong* pos = stackalloc ulong[2 * limbs];
        ulong* neg = pos + limbs;
        new Span<ulong>(pos, 2 * limbs).Clear();
        int baseExp = 2 * emin;
        // det2(b, c) - det2(a, c) + det2(a, b), det2(p, q) = px qy - py qx
        Add2(pos, neg, mag, ex, negMask, baseExp, 2, 5, false);
        Add2(pos, neg, mag, ex, negMask, baseExp, 3, 4, true);
        Add2(pos, neg, mag, ex, negMask, baseExp, 0, 5, true);
        Add2(pos, neg, mag, ex, negMask, baseExp, 1, 4, false);
        Add2(pos, neg, mag, ex, negMask, baseExp, 0, 3, false);
        Add2(pos, neg, mag, ex, negMask, baseExp, 1, 2, true);
        for (int i = limbs - 1; i >= 0; --i)
            if (pos[i] != neg[i])
                return (pos[i] > neg[i]) ? 1 : -1;
        return 0;
    }
}
