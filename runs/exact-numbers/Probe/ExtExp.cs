// PROTOTYPE P1 (exact-number study; not library code): floating-point expansions with an extended exponent.
// Each monomial of the predicate polynomial (see Dyadic.cs) is evaluated on the integer mantissas as a Shewchuk
// expansion (TwoProduct, Scale-Expansion: 2 components at degree 2, 4 at degree 3; exact, no overflow or underflow
// because the mantissas are integers below 2^53) and carries its binary exponent E as an int. The monomials are
// sorted by E and cut into clusters wherever two neighbours are more than 170 binary orders apart. A cluster's
// components are scaled to its smallest E (exact powers of two) and summed with Fast-Expansion-Sum (Shewchuk 1997,
// Theorem 13). A nonzero cluster sum S is an integer multiple of 2^Elow, and everything below the cut is smaller than
// 24 * 2^159 * 2^(Elow - 171) < 2^Elow, so the first nonzero cluster decides the sign. A cluster wider than 800
// binary orders (possible only for adversarial staircases of exponents) falls back to the accumulator (counted).
using System;
using System.Runtime.CompilerServices;
using g3;

static unsafe class ExtExp
{
    public static long Fallbacks;

    public static int Orient3d(double* v) { return -Sign(v, 12, Dyadic.Orient3dTerms, 3); }
    public static int Orient2d(double* v) { return Sign(v, 6, Dyadic.Orient2dTerms, 2); }

    const int MaxTerms = 24, Comps = 4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void TwoSum(double a, double b, out double x, out double y)
    {
        x = a + b;
        double bv = x - a, av = x - bv;
        y = (a - av) + (b - bv);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int Sign(double* v, int n, Dyadic.Term[] terms, int degree)
    {
        double* m = stackalloc double[16];
        int* ex = stackalloc int[16];
        for (int i = 0; i < n; ++i) {
            long bits = BitConverter.DoubleToInt64Bits(v[i]);
            int e = (int)((bits >> 52) & 0x7FF);
            long f = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0x7FF) throw new ArgumentException("ExactPredicates: coordinates must be finite");
            if (e == 0) e = 1; else f |= 1L << 52;
            m[i] = (bits < 0) ? -(double)f : (double)f;
            ex[i] = e;
        }
        double* c = stackalloc double[MaxTerms * Comps];
        int* E = stackalloc int[MaxTerms];
        int* order = stackalloc int[MaxTerms];
        int nt = 0;
        for (int t = 0; t < terms.Length; ++t) {
            Dyadic.Term q = terms[t];
            double a = m[q.I0], b = m[q.I1];
            if (a == 0 || b == 0) continue;
            if (q.Sign < 0) a = -a;
            double hi, lo;
            Expansion.TwoProduct(a, b, out hi, out lo);
            double* h = c + nt * Comps;
            int e = ex[q.I0] + ex[q.I1];
            if (degree == 2) {
                h[0] = lo; h[1] = hi; h[2] = 0; h[3] = 0;
            } else {
                double s = m[q.I2];
                if (s == 0) continue;
                e += ex[q.I2];
                // Scale-Expansion of (lo, hi) by s
                double Q, T, t0, t1;
                Expansion.TwoProduct(lo, s, out Q, out h[0]);
                Expansion.TwoProduct(hi, s, out T, out t0);
                TwoSum(Q, t0, out Q, out h[1]);
                t1 = T + Q;                 // Fast-Two-Sum (|T| >= |Q|)
                h[2] = Q - (t1 - T);
                h[3] = t1;
            }
            E[nt] = e;
            // insertion sort by E, descending
            int k = nt;
            while (k > 0 && E[order[k - 1]] < e) { order[k] = order[k - 1]; --k; }
            order[k] = nt;
            ++nt;
        }
        double* acc = stackalloc double[MaxTerms * Comps + 8];
        double* tmp = stackalloc double[MaxTerms * Comps + 8];
        double* sc = stackalloc double[Comps];
        int i0 = 0;
        while (i0 < nt) {
            int i1 = i0 + 1;
            while (i1 < nt && E[order[i1 - 1]] - E[order[i1]] <= 170) ++i1;
            int elow = E[order[i1 - 1]], ehigh = E[order[i0]];
            if (ehigh - elow > 800) {
                Fallbacks++;
                return Dyadic.Sign(v, n, terms, degree);
            }
            int alen = 0;
            for (int k = i0; k < i1; ++k) {
                int t = order[k];
                double p2 = BitConverter.Int64BitsToDouble((long)(1023 + E[t] - elow) << 52);
                double* h = c + t * Comps;
                int sl = 0;
                for (int j = 0; j < Comps; ++j) if (h[j] != 0) sc[sl++] = h[j] * p2;
                alen = FastExpansionSum(acc, alen, sc, sl, tmp);
                double* sw = acc; acc = tmp; tmp = sw;
            }
            if (alen > 0)
                return (acc[alen - 1] > 0) ? 1 : -1;
            i0 = i1;
        }
        return 0;
    }

    // h = e + f (Shewchuk 1997, Fast-Expansion-Sum with zero elimination); e and f sorted by increasing magnitude
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int FastExpansionSum(double* e, int elen, double* f, int flen, double* h)
    {
        if (elen == 0) { for (int i = 0; i < flen; ++i) h[i] = f[i]; return flen; }
        if (flen == 0) { for (int i = 0; i < elen; ++i) h[i] = e[i]; return elen; }
        int ei = 0, fi = 0, hl = 0;
        double g0 = (Math.Abs(f[fi]) > Math.Abs(e[ei])) ? e[ei++] : f[fi++];
        double g1;
        if (ei < elen && (fi >= flen || Math.Abs(f[fi]) > Math.Abs(e[ei]))) g1 = e[ei++]; else g1 = f[fi++];
        // Fast-Two-Sum(g1, g0)
        double Q = g1 + g0;
        double q = g0 - (Q - g1);
        if (q != 0) h[hl++] = q;
        while (ei < elen || fi < flen) {
            double g;
            if (ei < elen && (fi >= flen || Math.Abs(f[fi]) > Math.Abs(e[ei]))) g = e[ei++]; else g = f[fi++];
            TwoSum(Q, g, out Q, out q);
            if (q != 0) h[hl++] = q;
        }
        if (Q != 0) h[hl++] = Q;
        return hl;
    }
}
