// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;
#if NET7_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif

namespace g3
{
    /// <summary>
    /// Dense inner kernels of SparseCholesky: dot products of rows of the supernode blocks.
    ///
    /// Every kernel returns, bit for bit, what DotScalar returns for the same operands: four partial sums (element k
    /// goes to sum k mod 4), the tail added to the first, combined as (s0 + s1) + (s2 + s3), with a separate multiply
    /// and add (no fused multiply-add: it would round differently). The vector versions are the same sums with
    /// the four partial sums in the four lanes of a Vector256, and the "4" versions run four independent dot products
    /// at once, which hides the latency of the add chain (one chain per product). So the factor and the solutions of
    /// SparseCholesky do not depend on the machine, the target framework or whether the vector path is taken.
    /// </summary>
    internal static unsafe class SparseCholeskyKernels
    {
        /// <summary> the vector kernels are used on this machine (net7.0 or later with 256-bit vectors) </summary>
        internal static bool VectorPath
        {
            get
            {
#if NET7_0_OR_GREATER
                return Vector256.IsHardwareAccelerated;
#else
                return false;
#endif
            }
        }

        /// <summary> the reference: four partial sums, the tail into the first, (s0 + s1) + (s2 + s3) </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static double DotScalar(double* a, double* b, int n)
        {
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            int k = 0;
            for (; k + 4 <= n; k += 4) {
                s0 += a[k] * b[k]; s1 += a[k + 1] * b[k + 1];
                s2 += a[k + 2] * b[k + 2]; s3 += a[k + 3] * b[k + 3];
            }
            for (; k < n; ++k) s0 += a[k] * b[k];
            return (s0 + s1) + (s2 + s3);
        }

        /// <summary> a . b, the same bits as DotScalar </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static double Dot(double* a, double* b, int n, bool vector)
        {
#if NET7_0_OR_GREATER
            if (vector) {
                Vector256<double> acc = Vector256<double>.Zero;
                int k = 0;
                for (; k + 4 <= n; k += 4)
                    acc = acc + Vector256.Load(a + k) * Vector256.Load(b + k);
                double s0 = acc.GetElement(0), s1 = acc.GetElement(1), s2 = acc.GetElement(2), s3 = acc.GetElement(3);
                for (; k < n; ++k) s0 += a[k] * b[k];
                return (s0 + s1) + (s2 + s3);
            }
#endif
            return DotScalar(a, b, n);
        }

        /// <summary> r[j] = a . (b + j * stride) of length n, j = 0..3; each the same bits as DotScalar </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void Dot4Cols(double* a, double* b, int stride, int n, double* r, bool vector)
        {
#if NET7_0_OR_GREATER
            if (vector) {
                Vector256<double> c0 = Vector256<double>.Zero, c1 = c0, c2 = c0, c3 = c0;
                double* b1 = b + stride, b2 = b + 2 * stride, b3 = b + 3 * stride;
                int k = 0;
                for (; k + 4 <= n; k += 4) {
                    Vector256<double> va = Vector256.Load(a + k);
                    c0 = c0 + va * Vector256.Load(b + k);
                    c1 = c1 + va * Vector256.Load(b1 + k);
                    c2 = c2 + va * Vector256.Load(b2 + k);
                    c3 = c3 + va * Vector256.Load(b3 + k);
                }
                r[0] = Finish(c0, a, b, k, n);
                r[1] = Finish(c1, a, b1, k, n);
                r[2] = Finish(c2, a, b2, k, n);
                r[3] = Finish(c3, a, b3, k, n);
                return;
            }
#endif
            for (int j = 0; j < 4; ++j) r[j] = DotScalar(a, b + j * stride, n);
        }

        /// <summary> r[j] = (a + j * stride) . b of length n, j = 0..3; each the same bits as DotScalar </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void Dot4Rows(double* a, int stride, double* b, int n, double* r, bool vector)
        {
#if NET7_0_OR_GREATER
            if (vector) {
                Vector256<double> c0 = Vector256<double>.Zero, c1 = c0, c2 = c0, c3 = c0;
                double* a1 = a + stride, a2 = a + 2 * stride, a3 = a + 3 * stride;
                int k = 0;
                for (; k + 4 <= n; k += 4) {
                    Vector256<double> vb = Vector256.Load(b + k);
                    c0 = c0 + Vector256.Load(a + k) * vb;
                    c1 = c1 + Vector256.Load(a1 + k) * vb;
                    c2 = c2 + Vector256.Load(a2 + k) * vb;
                    c3 = c3 + Vector256.Load(a3 + k) * vb;
                }
                r[0] = Finish(c0, a, b, k, n);
                r[1] = Finish(c1, a1, b, k, n);
                r[2] = Finish(c2, a2, b, k, n);
                r[3] = Finish(c3, a3, b, k, n);
                return;
            }
#endif
            for (int j = 0; j < 4; ++j) r[j] = DotScalar(a + j * stride, b, n);
        }

#if NET7_0_OR_GREATER
        // the four partial sums of a vector accumulator, the tail from index k, combined as in DotScalar
        [MethodImpl(JitHints.Optimize)]
        static double Finish(Vector256<double> acc, double* a, double* b, int k, int n)
        {
            double s0 = acc.GetElement(0), s1 = acc.GetElement(1), s2 = acc.GetElement(2), s3 = acc.GetElement(3);
            for (; k < n; ++k) s0 += a[k] * b[k];
            return (s0 + s1) + (s2 + s3);
        }
#endif
    }
}
