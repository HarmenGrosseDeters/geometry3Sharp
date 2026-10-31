// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Hand-written runtime of the generated predicates (GeneratedPredicates.cs, from tools/PredicateCompiler):
    /// expansion arithmetic on stack buffers (Shewchuk, "Adaptive Precision Floating-Point Arithmetic and Fast
    /// Robust Geometric Predicates", 1997). An expansion is a run of doubles, non-overlapping, in order of
    /// increasing magnitude, without zeros (except a single 0 for the value 0). Outputs must not alias inputs.
    ///
    /// Exact only without overflow and underflow: the generated code calls it only when all inputs are 0 or in
    /// [2^-100, 2^100] (InRange) and the polynomial has degree at most 6, so every product of components is a
    /// normal double and its rounding error is exact (see the generator).
    ///
    /// Experimental (docs/design/PREDICATE_COMPILER.md): nothing in the library uses it yet.
    /// </summary>
    internal static unsafe class PredicateRuntime
    {
        const double MinMagnitude = 7.888609052210118e-31;     // 2^-100
        const double MaxMagnitude = 1.2676506002282294e30;     // 2^100

        /// <summary> 0 or a magnitude in [2^-100, 2^100]; NaN throws (as ExactPredicates) </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InRange(double x)
        {
            if (double.IsNaN(x))
                throw new ArgumentException("ExactPredicates: coordinates must be finite");
            double ax = Math.Abs(x);
            return ax == 0 || (ax >= MinMagnitude && ax <= MaxMagnitude);
        }

        /// <summary> x as an expansion of one component </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int One(double x, double* h)
        {
            h[0] = x;
            return 1;
        }

        /// <summary> a + b exactly (up to 2 components) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int TwoSum(double a, double b, double* h)
        {
            double x = a + b;
            double bv = x - a, av = x - bv;
            double y = (a - av) + (b - bv);
            int k = 0;
            if (y != 0) h[k++] = y;
            h[k++] = x;
            return k;
        }

        /// <summary> a - b exactly (up to 2 components) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int TwoDiff(double a, double b, double* h)
        {
            return TwoSum(a, -b, h);
        }

        /// <summary> the rounding error of x = fl(a - b) </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DiffTail(double a, double b, double x)
        {
            double bv = a - x, av = x + bv;
            return (a - av) + (bv - b);
        }

        /// <summary> the rounding error of x = fl(a + b) </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SumTail(double a, double b, double x)
        {
            double bv = x - a, av = x - bv;
            return (a - av) + (b - bv);
        }

        /// <summary> e + f (sign -1: e - f): merge by magnitude, accumulate with Two-Sum (fast-expansion-sum) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int Sum(double* e, int elen, double* f, int flen, double* h, double fsign)
        {
            int i = 0, j = 0, k = 0;
            double q;
            if (j >= flen || (i < elen && Math.Abs(e[i]) < Math.Abs(f[j]))) q = e[i++]; else q = fsign * f[j++];
            while (i < elen || j < flen) {
                double g;
                if (j >= flen || (i < elen && Math.Abs(e[i]) < Math.Abs(f[j]))) g = e[i++]; else g = fsign * f[j++];
                double qn = q + g;
                double bv = qn - q, av = qn - bv;
                double hh = (q - av) + (g - bv);
                q = qn;
                if (hh != 0) h[k++] = hh;
            }
            if (q != 0 || k == 0) h[k++] = q;
            return k;
        }

        /// <summary> e * b (up to 2 elen components) </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int Scale(double* e, int elen, double b, double* h)
        {
            int k = 0;
            double q, hh;
            Expansion.TwoProduct(e[0], b, out q, out hh);
            if (hh != 0) h[k++] = hh;
            for (int i = 1; i < elen; ++i) {
                double p1, p0;
                Expansion.TwoProduct(e[i], b, out p1, out p0);
                double s = q + p0;
                double bv = s - q, av = s - bv;
                hh = (q - av) + (p0 - bv);
                if (hh != 0) h[k++] = hh;
                // fast two-sum (|p1| >= |s|)
                q = p1 + s;
                hh = s - (q - p1);
                if (hh != 0) h[k++] = hh;
            }
            if (q != 0 || k == 0) h[k++] = q;
            return k;
        }

        /// <summary> e * f (up to 2 elen flen components): the longer one scaled by each component of the shorter </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int Mul(double* e, int elen, double* f, int flen, double* h)
        {
            if (elen < flen) {
                double* t = e; e = f; f = t;
                int n = elen; elen = flen; flen = n;
            }
            int k = Scale(e, elen, f[0], h);
            if (flen == 1)
                return k;
            double* s = stackalloc double[2 * elen];
            double* acc = stackalloc double[2 * elen * flen];
            for (int j = 1; j < flen; ++j) {
                int ns = Scale(e, elen, f[j], s);
                int na = Sum(h, k, s, ns, acc, 1);
                for (int i = 0; i < na; ++i) h[i] = acc[i];
                k = na;
            }
            return k;
        }

        /// <summary> the sign of the expansion: its largest component decides </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(double* e, int n)
        {
            return Math.Sign(e[n - 1]);
        }

        /// <summary> the sign of (e - b), exactly </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int CompareTo(double* e, int n, double b)
        {
            double* h = stackalloc double[n + 1];
            return Sign(h, Sum(e, n, &b, 1, h, -1));
        }
    }
}
