// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Exact slope predicate of a triangle given by double coordinates (docs/design/TERRAIN.md 5.4, oracle (i)).
    /// With n = (b - a) x (c - a), the slope (magnitude of the gradient over XY) is sqrt(nx^2 + ny^2) / |nz|. The
    /// comparison against a limit is a polynomial sign, decided by interval arithmetic (ExactInterval) and, if the
    /// interval contains 0, exactly (ExactDyadic). The orientation does not matter (both sides are squares).
    /// </summary>
    internal static class ExactSlopePredicates
    {
        /// <summary>
        /// sign of K2 * nz^2 - K1 * (nx^2 + ny^2) for n = (b - a) x (c - a): &gt;= 0 iff slope^2 * K1 &lt;= K2.
        /// Also returns, in degenerate, whether nz is exactly 0 (the triangle is degenerate in XY).
        /// K1 = k1a * k1b and K2 = k2a * k2b, each a product of two doubles (eg n * n for a ratio 1:n).
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        public static int SlopeSign(Vector3d a, Vector3d b, Vector3d c, double k1a, double k1b, double k2a, double k2b, out bool degenerate)
        {
            ExactInterval ux = ExactInterval.Of(b.x) - ExactInterval.Of(a.x), uy = ExactInterval.Of(b.y) - ExactInterval.Of(a.y),
                uz = ExactInterval.Of(b.z) - ExactInterval.Of(a.z);
            ExactInterval vx = ExactInterval.Of(c.x) - ExactInterval.Of(a.x), vy = ExactInterval.Of(c.y) - ExactInterval.Of(a.y),
                vz = ExactInterval.Of(c.z) - ExactInterval.Of(a.z);
            ExactInterval nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            int snz = nz.Sign();
            if (snz != 2) {
                degenerate = snz == 0;
                ExactInterval q = ExactInterval.Square(nx) + ExactInterval.Square(ny);
                ExactInterval d = ExactInterval.Of(k2a) * ExactInterval.Of(k2b) * ExactInterval.Square(nz)
                                - ExactInterval.Of(k1a) * ExactInterval.Of(k1b) * q;
                int sd = d.Sign();
                if (sd != 2 && snz != 0)
                    return sd;
            }
            return slope_sign_exact(a, b, c, k1a, k1b, k2a, k2b, out degenerate);
        }

        static int slope_sign_exact(Vector3d a, Vector3d b, Vector3d c, double k1a, double k1b, double k2a, double k2b, out bool degenerate)
        {
            ExactDyadic ax = ExactDyadic.Of(a.x), ay = ExactDyadic.Of(a.y), az = ExactDyadic.Of(a.z);
            ExactDyadic ux = ExactDyadic.Of(b.x) - ax, uy = ExactDyadic.Of(b.y) - ay, uz = ExactDyadic.Of(b.z) - az;
            ExactDyadic vx = ExactDyadic.Of(c.x) - ax, vy = ExactDyadic.Of(c.y) - ay, vz = ExactDyadic.Of(c.z) - az;
            ExactDyadic nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            degenerate = nz.Sign == 0;
            ExactDyadic d = ExactDyadic.Of(k2a) * ExactDyadic.Of(k2b) * (nz * nz)
                          - ExactDyadic.Of(k1a) * ExactDyadic.Of(k1b) * (nx * nx + ny * ny);
            return d.Sign;
        }

        /// <summary> sign of a * b - c (all finite doubles), exactly </summary>
        public static int ProductMinus(double a, double b, double c)
        {
            int s = (ExactInterval.Of(a) * ExactInterval.Of(b) - ExactInterval.Of(c)).Sign();
            if (s != 2) return s;
            return (ExactDyadic.Of(a) * ExactDyadic.Of(b) - ExactDyadic.Of(c)).Sign;
        }
    }
}
