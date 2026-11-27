using System;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// A conservative "apart" test for two triangles with double corners, from axes computed once per triangle: the
    /// filter in front of the pair test of MeshRoundingRepair's full pair search, where a query triangle has hundreds of
    /// box neighbours on results with long thin triangles on tilted faces and none of them intersects
    /// (docs/research/ROUNDING_COST_2026-11.md section 5, docs/design/SNAP_ROUNDING_3D.md section 5.3).
    ///
    /// Rule. A triangle T has four axes a_0 .. a_3 with double components: its normal n = (T1 - T0) x (T2 - T0) and the
    /// in-plane normals of its edges, n x (T1 - T0), n x (T2 - T1), n x (T0 - T2), all computed in doubles. For an axis
    /// a and a point x the projection and its magnitude are computed as
    ///     p~(x) = fl(fl(fl(a_x x_x) + fl(a_y x_y)) + fl(a_z x_z))
    ///     s~(x) = fl(fl(|fl(a_x x_x)| + |fl(a_y x_y)|) + |fl(a_z x_z)|)
    /// and for the three corners of a triangle U
    ///     m~   = fl(fl(Gamma max_k s~(U_k)) + Tiny),            Gamma = 8u, u = 2^-53, Tiny = 2^-1072
    ///     lo~  = fl(min_k p~(U_k) - m~),   hi~ = fl(max_k p~(U_k) + m~).
    /// T and U are apart along a if lo~(U) > hi~(T) or lo~(T) > hi~(U), and the pair is apart if it is apart along one
    /// of the four axes of T or of the four of U. lo~(T) and hi~(T) on T's own axes are kept with the axes.
    ///
    /// Claim. If the pair is apart, the two triangles have no common point. Any vector is a valid axis (a common point
    /// c has a.c between the smallest and the largest corner projection of each triangle, as a.x is linear), so the
    /// axes need no error bound; only the comparison of the projections does.
    ///
    /// The bound, operation by operation, with every rounding to nearest within a factor (1 + d), |d| &lt;= u, of the
    /// exact result (no underflow, see below), p = a.x the exact projection and S = |a_x x_x| + |a_y x_y| + |a_z x_z|:
    ///   1. three products, one rounding each:                 |fl(a_i x_i) - a_i x_i| &lt;= u |a_i x_i|
    ///   2. the first sum, one rounding of a sum of two rounded products:
    ///                                                         error &lt;= ((1 + u)^2 - 1) (|a_x x_x| + |a_y x_y|)
    ///   3. the second sum, one rounding more on the first two terms and on the third product:
    ///                                                         |p~ - p| &lt;= ((1 + u)^3 - 1) S = (3u + 3u^2 + u^3) S &lt; 4u S
    ///   4. s~ is the same three products (|fl(a_i x_i)| = fl(|a_i x_i|), no rounding in an absolute value) and two
    ///      sums of non-negative terms:                        s~ &gt;= (1 - u)^3 S
    ///   5. the margin: Gamma s~ is a multiplication by a power of two, exact:
    ///                                                         m~ = 8u s~ &gt;= 8u (1 - u)^3 S &gt; 4u S &gt; |p~ - p|
    ///      (8 (1 - u)^3 &gt; 3 + 3u + u^2 for u = 2^-53), for each corner with its own S, and so for the maximum over
    ///      the corners that m~ uses
    ///   6. the interval ends are one rounding each, and rounding is monotonic: fl(y) &gt; fl(z) implies y &gt; z. So
    ///      lo~(U) &gt; hi~(T) implies min_k p~(U_k) - m~(U) &gt; max_k p~(T_k) + m~(T) in exact arithmetic, and with 5
    ///      min_k a.U_k &gt; max_k a.T_k: the triangles are strictly apart along a.
    /// Gamma = 8u is twice what 3 and 4 need (4u (1 - u)^-3 would do). The relative part of the bound (steps 1 to 6 without
    /// underflow) corresponds to the lemma of the Z3 pilot (tools/Z3Pilot/separating_axis.py) for its prototype
    /// (benchmarks/SnapRoundingExperiment/CandidateCulling.cs); this file has no source contract with the pilot, and the pilot
    /// has no Tiny term and assumes no underflow. The full bound (with Tiny and the underflow case split below) is derived
    /// here and in docs/design/SNAP_ROUNDING_3D.md section 5.3, and checked by the value tests of TriangleSeparatingAxesTests
    /// and by review.
    ///
    /// Size. MeshRoundingRepair keeps the axes (Stride doubles per triangle) in one array and uses none above
    /// (int.MaxValue / 16) / Stride = 6 710 886 triangles (a 1 GiB array: below the limit of one object on every runtime,
    /// 2 GB on .NET Framework); the search then tests every box pair, as without the axes.
    ///
    /// Underflow. The relative model of a rounding fails for a product that is subnormal or underflows to zero: its
    /// error is then at most 2^-1075 in absolute terms (sums and differences of doubles are exact when their result is
    /// subnormal). So in general |p~ - p| &lt;= (3u + 3u^2 + u^3) S + 3 (1 + u)^2 2^-1075 and s~ &gt;= (1 - u)^3 S -
    /// 3 2^-1075, and fl(Gamma s~) may lose another 2^-1075 where it is subnormal. Tiny = 2^-1072 = 8 2^-1075 covers
    /// these. With g = fl(Gamma s~): where g + Tiny is below 2^-1021 the sum is exact (every double below 2^-1021 is a
    /// multiple of 2^-1074), and m~ &gt;= 8u (1 - u)^3 S - 8u 3 2^-1075 - 2^-1075 + 8 2^-1075 &gt; 4u S + 4 2^-1075. Where
    /// g + Tiny is 2^-1021 or more, g is at least 2^-1021 - 2^-1072 and normal (so exact), the rounded sum is not below
    /// g (rounding is monotonic) but may round Tiny away, and then the part of the margin beyond the relative bound,
    /// (8 (1 - u)^3 - 3 - 3u - u^2) u S &gt; (4 / 8) g &gt; 2^-1023, covers the 4 2^-1075 by itself. With Tiny a triangle
    /// in a plane through the origin (all projections on its normal exactly zero) has the interval (-Tiny, Tiny), not
    /// none.
    ///
    /// The model. Every operation is rounded once to a double (SSE2 and NEON code: x64, arm64, and x86 on .NET Core and
    /// later). The x87 code of a 32-bit .NET Framework process keeps intermediates with a wider exponent range and
    /// rounds a subnormal result a second time when it is stored: the relative steps 1 to 6 are unchanged there (the
    /// precision control is 53 bits), the underflow paragraph is not proved for it.
    ///
    /// Overflow, values that are not finite. With finite magnitudes the projections are finite (|p~| &lt;= s~), and an
    /// interval end that overflows is -inf below or +inf above: still an enclosure. A coordinate, an axis component or a
    /// product that is not finite makes a magnitude s~ infinite or NaN (the maximum is taken with Math.Max, which
    /// returns NaN if one of its arguments is), so the margin is infinite or NaN and each end of the interval is
    /// infinite on its own side or NaN; a comparison with a NaN is false. Either way the pair is not apart along that
    /// axis. No FMA: a fused product would change steps 1 to 3 (it has one rounding less, so the bound would still
    /// hold, but the results would differ between machines).
    ///
    /// Shared corners need no special case: a corner of both triangles has one p~ on an axis, which lies in both
    /// intervals, so two triangles that share a position are never apart.
    /// </summary>
    internal static class TriangleSeparatingAxes
    {
        /// <summary> the doubles kept per triangle: four axes (12) and its own interval on each (8) </summary>
        internal const int Stride = 20;

        // the rounding margin of a projection, relative to the magnitude s~ (see the class comment, steps 3 to 5)
        internal const double Gamma = 8 * 1.1102230246251565e-16;
        // the absolute part of the margin, for products that are subnormal (see the class comment): 2^-1072
        internal const double Tiny = 1.9762625833649862e-323;

        /// <summary> the four axes of triangle (v0, v1, v2) and its interval on each, into data from offset o </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void Compute(Vector3d v0, Vector3d v1, Vector3d v2, double[] data, int o)
        {
            double e0x = v1.x - v0.x, e0y = v1.y - v0.y, e0z = v1.z - v0.z;
            double e1x = v2.x - v1.x, e1y = v2.y - v1.y, e1z = v2.z - v1.z;
            double e2x = v0.x - v2.x, e2y = v0.y - v2.y, e2z = v0.z - v2.z;
            // (any vectors do: the normal, as e0 x (-e2), and its cross products with the edges)
            double nx = e2y * e0z - e2z * e0y, ny = e2z * e0x - e2x * e0z, nz = e2x * e0y - e2y * e0x;
            data[o] = nx; data[o + 1] = ny; data[o + 2] = nz;
            data[o + 3] = ny * e0z - nz * e0y; data[o + 4] = nz * e0x - nx * e0z; data[o + 5] = nx * e0y - ny * e0x;
            data[o + 6] = ny * e1z - nz * e1y; data[o + 7] = nz * e1x - nx * e1z; data[o + 8] = nx * e1y - ny * e1x;
            data[o + 9] = ny * e2z - nz * e2y; data[o + 10] = nz * e2x - nx * e2z; data[o + 11] = nx * e2y - ny * e2x;
            for (int k = 0; k < 4; ++k)
                SetOwnInterval(data, o, k, v0, v1, v2);
        }

        /// <summary> the interval of the triangle (v0, v1, v2) on axis k of data at offset o, as its own </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetOwnInterval(double[] data, int o, int k, Vector3d v0, Vector3d v1, Vector3d v2)
        {
            double lo, hi;
            Interval(data[o + 3 * k], data[o + 3 * k + 1], data[o + 3 * k + 2], v0, v1, v2, out lo, out hi);
            data[o + 12 + 2 * k] = lo; data[o + 13 + 2 * k] = hi;
        }

        /// <summary>
        /// the triangle (u0, u1, u2) and the triangle whose axes are in data at offset o are strictly apart along one
        /// of those four axes: they have no common point (see the class comment)
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static bool Apart(double[] data, int o, Vector3d u0, Vector3d u1, Vector3d u2)
        {
            for (int k = 0; k < 4; ++k)
                if (ApartAlong(data, o, k, u0, u1, u2))
                    return true;
            return false;
        }

        /// <summary> Apart along axis k only </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ApartAlong(double[] data, int o, int k, Vector3d u0, Vector3d u1, Vector3d u2)
        {
            double lo, hi;
            Interval(data[o + 3 * k], data[o + 3 * k + 1], data[o + 3 * k + 2], u0, u1, u2, out lo, out hi);
            return lo > data[o + 13 + 2 * k] || data[o + 12 + 2 * k] > hi;
        }

        // lo~ and hi~ of the three points on the axis (ax, ay, az) (see the class comment; ends that are infinite or NaN
        // where a value is not finite)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Interval(double ax, double ay, double az, Vector3d x0, Vector3d x1, Vector3d x2, out double lo, out double hi)
        {
            double t0x = ax * x0.x, t0y = ay * x0.y, t0z = az * x0.z;
            double t1x = ax * x1.x, t1y = ay * x1.y, t1z = az * x1.z;
            double t2x = ax * x2.x, t2y = ay * x2.y, t2z = az * x2.z;
            double p0 = (t0x + t0y) + t0z, p1 = (t1x + t1y) + t1z, p2 = (t2x + t2y) + t2z;
            double s0 = (Math.Abs(t0x) + Math.Abs(t0y)) + Math.Abs(t0z), s1 = (Math.Abs(t1x) + Math.Abs(t1y)) + Math.Abs(t1z),
                s2 = (Math.Abs(t2x) + Math.Abs(t2y)) + Math.Abs(t2z);
            // (Math.Max: NaN if a magnitude is NaN)
            double m = Gamma * Math.Max(Math.Max(s0, s1), s2) + Tiny;
            double pmin = p0 < p1 ? p0 : p1, pmax = p0 > p1 ? p0 : p1;
            if (p2 < pmin) pmin = p2;
            if (p2 > pmax) pmax = p2;
            lo = pmin - m;
            hi = pmax + m;
        }
    }
}
