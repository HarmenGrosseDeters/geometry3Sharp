// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif

namespace g3
{
    public static partial class ExactPredicates
    {
        /// <summary>
        /// Orient3d(a, b, c, d) for the three points d0, d1, d2 (the corners of a triangle against the plane of another
        /// one), for the points selected by the bits 1, 2, 4 of lanes (the others give 0). Each sign is that of
        /// Orient3d: the floating-point filter evaluates the same IEEE operations in the same order for each point
        /// (the terms of a, b, c once instead of once per point), so it decides the same points with the same signs;
        /// the points it does not decide go through the same exact stage. With hardware SIMD (Vector256, .NET 8
        /// and later; not with ScalarOnly) the three points are filtered in the lanes of one vector: lane-wise
        /// operations only, no fused multiply-add, no reduction across lanes.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void Orient3d3(Vector3d a, Vector3d b, Vector3d c, Vector3d d0, Vector3d d1, Vector3d d2, int lanes,
            out int s0, out int s1, out int s2)
        {
            double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
            double vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
            double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;
            // the factors of wx, wy, wz in the determinant and in the permanent (as in Orient3d)
            double cx = uyvz - uzvy, cy = uzvx - uxvz, cz = uxvy - uyvx;
            double px = Math.Abs(uyvz) + Math.Abs(uzvy), py = Math.Abs(uzvx) + Math.Abs(uxvz), pz = Math.Abs(uxvy) + Math.Abs(uyvx);
            int f0 = 0, f1 = 0, f2 = 0;
#if NET8_0_OR_GREATER
            if (ScalarOnly == false && Vector256.IsHardwareAccelerated) {
                filter3_vector(a, cx, cy, cz, px, py, pz, d0, d1, d2, out f0, out f1, out f2);
            } else
#endif
            {
                f0 = filter3(a, cx, cy, cz, px, py, pz, d0);
                f1 = filter3(a, cx, cy, cz, px, py, pz, d1);
                f2 = filter3(a, cx, cy, cz, px, py, pz, d2);
            }
            s0 = finish(lanes & 1, f0, a, b, c, d0);
            s1 = finish(lanes & 2, f1, a, b, c, d1);
            s2 = finish(lanes & 4, f2, a, b, c, d2);
        }

        /// <summary>
        /// for tests and for a deterministic mode: Orient3d3 filters with scalar operations even where SIMD is
        /// available (the signs are the same either way)
        /// </summary>
        internal static bool ScalarOnly = false;

        // the sign of a lane: 0 if not selected, the filter's sign, or the exact stage's
        static int finish(int selected, int filtered, Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            if (selected == 0)
                return 0;
            PredicateStats.Call(PredicateStats.Kind.Orient3d);
            if (filtered != 0)
                return filtered;
            PredicateStats.ExactStage(PredicateStats.Kind.Orient3d);
            return orient3d_exact(a, b, c, d);
        }

        // Orient3d's filter for one point with the terms of a, b, c given: +1, -1, or 0 if it does not decide
        static int filter3(Vector3d a, double cx, double cy, double cz, double px, double py, double pz, Vector3d d)
        {
            double wx = d.x - a.x, wy = d.y - a.y, wz = d.z - a.z;
            double det = wx * cx + wy * cy + wz * cz;
            double permanent = Math.Abs(wx) * px + Math.Abs(wy) * py + Math.Abs(wz) * pz;
            double bound = Orient3dBound * permanent + (Math.Abs(wx) + Math.Abs(wy) + Math.Abs(wz) + 1) * UnderflowUnit;
            if (det > bound && permanent > UnderflowLimit) return 1;
            if (-det > bound && permanent > UnderflowLimit) return -1;
            return 0;
        }

#if NET8_0_OR_GREATER
        // filter3 for three points in the lanes 0, 1, 2 of 256-bit vectors (lane 3 repeats d2): the same operations in
        // the same order per lane
        static void filter3_vector(Vector3d a, double cx, double cy, double cz, double px, double py, double pz,
            Vector3d d0, Vector3d d1, Vector3d d2, out int f0, out int f1, out int f2)
        {
            Vector256<double> wx = Vector256.Create(d0.x, d1.x, d2.x, d2.x) - Vector256.Create(a.x);
            Vector256<double> wy = Vector256.Create(d0.y, d1.y, d2.y, d2.y) - Vector256.Create(a.y);
            Vector256<double> wz = Vector256.Create(d0.z, d1.z, d2.z, d2.z) - Vector256.Create(a.z);
            Vector256<double> det = (wx * Vector256.Create(cx) + wy * Vector256.Create(cy)) + wz * Vector256.Create(cz);
            Vector256<double> ax = Vector256.Abs(wx), ay = Vector256.Abs(wy), az = Vector256.Abs(wz);
            Vector256<double> permanent = (ax * Vector256.Create(px) + ay * Vector256.Create(py)) + az * Vector256.Create(pz);
            Vector256<double> bound = Vector256.Create(Orient3dBound) * permanent
                + (((ax + ay) + az) + Vector256.Create(1.0)) * Vector256.Create(UnderflowUnit);
            Vector256<double> normal = Vector256.GreaterThan(permanent, Vector256.Create(UnderflowLimit));
            uint pos = (Vector256.GreaterThan(det, bound) & normal).ExtractMostSignificantBits();
            uint neg = (Vector256.GreaterThan(-det, bound) & normal).ExtractMostSignificantBits();
            f0 = ((pos & 1) != 0) ? 1 : ((neg & 1) != 0) ? -1 : 0;
            f1 = ((pos & 2) != 0) ? 1 : ((neg & 2) != 0) ? -1 : 0;
            f2 = ((pos & 4) != 0) ? 1 : ((neg & 4) != 0) ? -1 : 0;
        }
#endif
    }
}
