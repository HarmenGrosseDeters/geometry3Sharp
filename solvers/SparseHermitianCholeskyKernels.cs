// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;
#if NET7_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif

namespace g3
{
    /// <summary>
    /// Dense inner kernels of SparseHermitianCholesky: sum over k of a[k] conj(b[k]) for rows of the supernode blocks (real and
    /// imaginary parts in separate arrays).
    ///
    /// Every kernel returns, bit for bit, what DotcScalar returns for the same operands: two partial sums per component (element
    /// k goes to sum k mod 2, the odd tail element to the first), r += (ar br + ai bi) and i += (ai br - ar bi) with separate
    /// multiplies and adds (no fused multiply-add, which would round differently), combined as r0 + r1. The vector versions are
    /// the same sums with the two partial sums in the two lanes of a Vector128, and the "4" versions run four independent
    /// products at once, which hides the latency of the add chains. So the factor and the solutions do not depend on the machine,
    /// the target framework or whether the vector path is taken.
    /// </summary>
    internal static unsafe class SparseHermitianCholeskyKernels
    {
        /// <summary> the vector kernels are used on this machine (net7.0 or later with 128-bit vectors) </summary>
        internal static bool VectorPath
        {
            get
            {
#if NET7_0_OR_GREATER
                return Vector128.IsHardwareAccelerated;
#else
                return false;
#endif
            }
        }

        /// <summary> the reference: sum over k of a[k] conj(b[k]), two partial sums per component </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void DotcScalar(double* ar, double* ai, double* br, double* bi, int n, out double re, out double im)
        {
            double r0 = 0, r1 = 0, i0 = 0, i1 = 0;
            int k = 0;
            for (; k + 2 <= n; k += 2) {
                r0 += ar[k] * br[k] + ai[k] * bi[k]; i0 += ai[k] * br[k] - ar[k] * bi[k];
                r1 += ar[k + 1] * br[k + 1] + ai[k + 1] * bi[k + 1]; i1 += ai[k + 1] * br[k + 1] - ar[k + 1] * bi[k + 1];
            }
            for (; k < n; ++k) { r0 += ar[k] * br[k] + ai[k] * bi[k]; i0 += ai[k] * br[k] - ar[k] * bi[k]; }
            re = r0 + r1; im = i0 + i1;
        }

        /// <summary> the sum of a[k] conj(b[k]), the same bits as DotcScalar </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void Dotc(double* ar, double* ai, double* br, double* bi, int n, out double re, out double im, bool vector)
        {
#if NET7_0_OR_GREATER
            if (vector) {
                Vector128<double> accR = Vector128<double>.Zero, accI = accR;
                int k = 0;
                for (; k + 2 <= n; k += 2) {
                    Vector128<double> xr = Vector128.Load(ar + k), xi = Vector128.Load(ai + k), yr = Vector128.Load(br + k), yi = Vector128.Load(bi + k);
                    accR = accR + (xr * yr + xi * yi);
                    accI = accI + (xi * yr - xr * yi);
                }
                Finish(accR, accI, ar, ai, br, bi, k, n, out re, out im);
                return;
            }
#endif
            DotcScalar(ar, ai, br, bi, n, out re, out im);
        }

        /// <summary> rr[j], ri[j] = the sum of a[k] conj(b_j[k]) with b_j = b + j * stride (both parts), j = 0..3; each the same bits as DotcScalar </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void Dotc4Cols(double* ar, double* ai, double* br, double* bi, int stride, int n, double* rr, double* ri, bool vector)
        {
#if NET7_0_OR_GREATER
            if (vector) {
                Vector128<double> r0 = Vector128<double>.Zero, r1 = r0, r2 = r0, r3 = r0, i0 = r0, i1 = r0, i2 = r0, i3 = r0;
                double* br1 = br + stride, br2 = br + 2 * stride, br3 = br + 3 * stride;
                double* bi1 = bi + stride, bi2 = bi + 2 * stride, bi3 = bi + 3 * stride;
                int k = 0;
                for (; k + 2 <= n; k += 2) {
                    Vector128<double> xr = Vector128.Load(ar + k), xi = Vector128.Load(ai + k);
                    Vector128<double> yr = Vector128.Load(br + k), yi = Vector128.Load(bi + k);
                    r0 = r0 + (xr * yr + xi * yi); i0 = i0 + (xi * yr - xr * yi);
                    yr = Vector128.Load(br1 + k); yi = Vector128.Load(bi1 + k);
                    r1 = r1 + (xr * yr + xi * yi); i1 = i1 + (xi * yr - xr * yi);
                    yr = Vector128.Load(br2 + k); yi = Vector128.Load(bi2 + k);
                    r2 = r2 + (xr * yr + xi * yi); i2 = i2 + (xi * yr - xr * yi);
                    yr = Vector128.Load(br3 + k); yi = Vector128.Load(bi3 + k);
                    r3 = r3 + (xr * yr + xi * yi); i3 = i3 + (xi * yr - xr * yi);
                }
                Finish(r0, i0, ar, ai, br, bi, k, n, out rr[0], out ri[0]);
                Finish(r1, i1, ar, ai, br1, bi1, k, n, out rr[1], out ri[1]);
                Finish(r2, i2, ar, ai, br2, bi2, k, n, out rr[2], out ri[2]);
                Finish(r3, i3, ar, ai, br3, bi3, k, n, out rr[3], out ri[3]);
                return;
            }
#endif
            for (int j = 0; j < 4; ++j) DotcScalar(ar, ai, br + j * stride, bi + j * stride, n, out rr[j], out ri[j]);
        }

        /// <summary> rr[j], ri[j] = the sum of a_j[k] conj(b[k]) with a_j = a + j * stride (both parts), j = 0..3; each the same bits as DotcScalar </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void Dotc4Rows(double* ar, double* ai, int stride, double* br, double* bi, int n, double* rr, double* ri, bool vector)
        {
#if NET7_0_OR_GREATER
            if (vector) {
                Vector128<double> r0 = Vector128<double>.Zero, r1 = r0, r2 = r0, r3 = r0, i0 = r0, i1 = r0, i2 = r0, i3 = r0;
                double* ar1 = ar + stride, ar2 = ar + 2 * stride, ar3 = ar + 3 * stride;
                double* ai1 = ai + stride, ai2 = ai + 2 * stride, ai3 = ai + 3 * stride;
                int k = 0;
                for (; k + 2 <= n; k += 2) {
                    Vector128<double> yr = Vector128.Load(br + k), yi = Vector128.Load(bi + k);
                    Vector128<double> xr = Vector128.Load(ar + k), xi = Vector128.Load(ai + k);
                    r0 = r0 + (xr * yr + xi * yi); i0 = i0 + (xi * yr - xr * yi);
                    xr = Vector128.Load(ar1 + k); xi = Vector128.Load(ai1 + k);
                    r1 = r1 + (xr * yr + xi * yi); i1 = i1 + (xi * yr - xr * yi);
                    xr = Vector128.Load(ar2 + k); xi = Vector128.Load(ai2 + k);
                    r2 = r2 + (xr * yr + xi * yi); i2 = i2 + (xi * yr - xr * yi);
                    xr = Vector128.Load(ar3 + k); xi = Vector128.Load(ai3 + k);
                    r3 = r3 + (xr * yr + xi * yi); i3 = i3 + (xi * yr - xr * yi);
                }
                Finish(r0, i0, ar, ai, br, bi, k, n, out rr[0], out ri[0]);
                Finish(r1, i1, ar1, ai1, br, bi, k, n, out rr[1], out ri[1]);
                Finish(r2, i2, ar2, ai2, br, bi, k, n, out rr[2], out ri[2]);
                Finish(r3, i3, ar3, ai3, br, bi, k, n, out rr[3], out ri[3]);
                return;
            }
#endif
            for (int j = 0; j < 4; ++j) DotcScalar(ar + j * stride, ai + j * stride, br, bi, n, out rr[j], out ri[j]);
        }

#if NET7_0_OR_GREATER
        // the two partial sums of each component, the odd tail element from index k into the first, combined as in DotcScalar
        [MethodImpl(JitHints.Optimize)]
        static void Finish(Vector128<double> accR, Vector128<double> accI, double* ar, double* ai, double* br, double* bi, int k, int n, out double re, out double im)
        {
            double r0 = accR.GetElement(0), r1 = accR.GetElement(1), i0 = accI.GetElement(0), i1 = accI.GetElement(1);
            for (; k < n; ++k) { r0 += ar[k] * br[k] + ai[k] * bi[k]; i0 += ai[k] * br[k] - ar[k] * bi[k]; }
            re = r0 + r1; im = i0 + i1;
        }
#endif
    }
}
