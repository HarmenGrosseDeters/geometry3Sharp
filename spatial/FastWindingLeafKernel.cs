// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif

namespace g3
{
    /// <summary>
    /// The exact terms of DMeshAABBTree3.FastWindingNumber (the triangles of a leaf, summed one by one) for up to four
    /// triangles at once: the square roots, dot products and the triple product of MathUtil.TriSolidAngle in the four
    /// lanes of a Vector256, then Math.Atan2 per triangle.
    ///
    /// Every term is, bit for bit, what MathUtil.TriSolidAngle(a, b, c, ref p) / MathUtil.FourPI returns: the same
    /// operations in the same order (separate multiplies and adds, no fused multiply-add and no reassociation; square root
    /// and the basic operations are correctly rounded in every lane), and the terms are added to the sum in the order
    /// of the triangles. A group in which a triangle is outside the range where TriSolidAngle rescales its coordinates
    /// (largest component of a-p, b-p, c-p, below 2^-100 or above 2^100, zero, not finite) is not computed here
    /// (TrySum returns false, the sum untouched): the caller takes the scalar path for it. So the value of a query
    /// does not depend on the machine, the target framework or whether this kernel is used (DMeshAABBTree3.FastWindingForceScalar
    /// is the switch of the tests).
    /// </summary>
    internal static class FastWindingLeafKernel
    {
        /// <summary> the kernel is used on this machine (net8.0 or later with 256-bit vectors) </summary>
        internal static bool Available
        {
            get
            {
#if NET8_0_OR_GREATER
                return Vector256.IsHardwareAccelerated;
#else
                return false;
#endif
            }
        }

        /// <summary> doubles per group of four triangles: ax ay az bx by bz cx cy cz, four lanes each </summary>
        internal const int GroupSize = 36;

        /// <summary>
        /// Stores the n (1 to 4) triangles of verts[from .. from + 3n - 1] (corners a b c per triangle) as one group at
        /// lanes[o ..]; the lanes after n repeat the first triangle (their terms are not used)
        /// </summary>
        internal static void StoreGroup(double[] lanes, int o, Vector3d[] verts, int from, int n)
        {
            for (int t = 0; t < 4; ++t) {
                int s = from + 3 * (t < n ? t : 0);
                for (int k = 0; k < 3; ++k) {
                    Vector3d v = verts[s + k];
                    lanes[o + 12 * k + t] = v.x;
                    lanes[o + 12 * k + 4 + t] = v.y;
                    lanes[o + 12 * k + 8 + t] = v.z;
                }
            }
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Adds the terms of the first n (1 to 4) triangles of the group at lanes[o ..] at p to sum, one after the other
        /// (sum += term / 4 pi); false, sum unchanged, if a triangle of the group needs the rescaled scalar path
        /// </summary>
        internal static bool TrySum(double[] lanes, int o, ref Vector3d p, int n, ref double sum)
        {
            Vector256<double> px = Vector256.Create(p.x), py = Vector256.Create(p.y), pz = Vector256.Create(p.z);
            Vector256<double> ax = Vector256.LoadUnsafe(ref lanes[o]) - px, ay = Vector256.LoadUnsafe(ref lanes[o + 4]) - py, az = Vector256.LoadUnsafe(ref lanes[o + 8]) - pz;
            Vector256<double> bx = Vector256.LoadUnsafe(ref lanes[o + 12]) - px, by = Vector256.LoadUnsafe(ref lanes[o + 16]) - py, bz = Vector256.LoadUnsafe(ref lanes[o + 20]) - pz;
            Vector256<double> cx = Vector256.LoadUnsafe(ref lanes[o + 24]) - px, cy = Vector256.LoadUnsafe(ref lanes[o + 28]) - py, cz = Vector256.LoadUnsafe(ref lanes[o + 32]) - pz;
            // (the range of TriSolidAngle: inside [lo, hi] it does not rescale; a NaN fails both comparisons, so the scalar path decides)
            Vector256<double> m = Vector256.Max(Vector256.Max(Vector256.Max(Vector256.Abs(ax), Vector256.Abs(ay)), Vector256.Max(Vector256.Abs(az), Vector256.Abs(bx))),
                Vector256.Max(Vector256.Max(Vector256.Abs(by), Vector256.Abs(bz)), Vector256.Max(Vector256.Max(Vector256.Abs(cx), Vector256.Abs(cy)), Vector256.Abs(cz))));
            if (Vector256.GreaterThanOrEqualAll(m, Vector256.Create(7.8886090522101181e-31)) == false || Vector256.LessThanOrEqualAll(m, Vector256.Create(1.2676506002282294e30)) == false)
                return false;
            Vector256<double> la = Vector256.Sqrt(ax * ax + ay * ay + az * az);
            Vector256<double> lb = Vector256.Sqrt(bx * bx + by * by + bz * bz);
            Vector256<double> lc = Vector256.Sqrt(cx * cx + cy * cy + cz * cz);
            Vector256<double> dab = ax * bx + ay * by + az * bz, dbc = bx * cx + by * cy + bz * cz, dca = cx * ax + cy * ay + cz * az;
            Vector256<double> bottom = (la * lb * lc) + dab * lc + dbc * la + dca * lb;
            Vector256<double> top = ax * (by * cz - cy * bz) - ay * (bx * cz - cx * bz) + az * (bx * cy - cx * by);
            // (2 atan2 / 4 pi per lane: the product and the quotient are correctly rounded in a vector as in a scalar)
            Vector256<double> at = Vector256.Create(g3.CoreMathPort.CoreMath.Atan2(top.GetElement(0), bottom.GetElement(0)),
                n > 1 ? g3.CoreMathPort.CoreMath.Atan2(top.GetElement(1), bottom.GetElement(1)) : 0,
                n > 2 ? g3.CoreMathPort.CoreMath.Atan2(top.GetElement(2), bottom.GetElement(2)) : 0,
                n > 3 ? g3.CoreMathPort.CoreMath.Atan2(top.GetElement(3), bottom.GetElement(3)) : 0);
            Vector256<double> term = Vector256.Create(2.0) * at / Vector256.Create(MathUtil.FourPI);
            sum += term.GetElement(0);
            if (n > 1) sum += term.GetElement(1);
            if (n > 2) sum += term.GetElement(2);
            if (n > 3) sum += term.GetElement(3);
            return true;
        }
#endif
    }
}
