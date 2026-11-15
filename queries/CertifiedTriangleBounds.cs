// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;

namespace g3
{
    /// <summary>
    /// Certified upper bounds for TriangleDeviationBounds in its certified mode (MeshHausdorffDistance.CertifiedUpper, #742): never
    /// below the exact value, rounding included. Every bound is an outward-rounded (ExactInterval) evaluation, or a single rounded
    /// operation followed by one step up; a NaN anywhere gives +infinity. Derivations: docs/design/HAUSDORFF_CERTIFIED_UPPER.md.
    /// </summary>
    internal static class CertifiedTriangleBounds
    {
        /// <summary> 1/sqrt(3) rounded up: the nearest double, 3 K^2 >= 1 exactly (tested) </summary>
        internal const double K = 0.5773502691896258;

        /// <summary>
        /// B1: an upper bound of the distance from x to the triangle (v0, v1, v2): the distance to its point v0 + s (v1 - v0) + t (v2 - v0),
        /// with s = w1 and t = w2 (the barycentric coordinates of a float closest point) clamped so that s, t >= 0 and s + t <= 1 hold
        /// exactly, evaluated with outward rounding. Any point of the triangle gives a valid bound; the float closest point a tight one.
        /// </summary>
        internal static double PointTriangleUpper(Vector3d x, Vector3d v0, Vector3d v1, Vector3d v2, double w1, double w2)
        {
            Clamp(w1, w2, out double s, out double t);
            ExactInterval si = ExactInterval.Of(s), ti = ExactInterval.Of(t), sum = ExactInterval.Of(0);
            for (int k = 0; k < 3; ++k) {
                ExactInterval a = ExactInterval.Of(v0[k]);
                ExactInterval p = a + si * (ExactInterval.Of(v1[k]) - a) + ti * (ExactInterval.Of(v2[k]) - a);
                sum = sum + ExactInterval.Square(ExactInterval.Of(x[k]) - p);
            }
            return sqrt_up(sum.Hi);
        }

        /// <summary> barycentric coordinates s, t of v1, v2 with s, t >= 0 and s + t <= 1 exactly (a NaN counts as 0) </summary>
        internal static void Clamp(double w1, double w2, out double s, out double t)
        {
            s = Math.Min(w1, 1); t = w2;
            if (!(s >= 0)) s = 0;           // (NaN too: the corner v0)
            if (!(t >= 0)) t = 0;
            // fl(s + t) < 1 implies s + t <= 1 (rounding is monotone, 1 is a double). Otherwise t <= 1 - s: exact for s >= 1/2
            // (Sterbenz), else one step below the rounded difference (which is within half a step of it)
            if (!(s + t < 1))
                t = Math.Min(t, (s >= 0.5) ? 1 - s : ExactInterval.Down(1 - s));
            if (!(t >= 0)) t = 0;
        }

        /// <summary> an upper bound of |a - b| </summary>
        internal static double LengthUpper(Vector3d a, Vector3d b)
        {
            return sqrt_up(SquaredLength(a, b).Hi);
        }

        /// <summary> an enclosure of |a - b|^2 </summary>
        internal static ExactInterval SquaredLength(Vector3d a, Vector3d b)
        {
            ExactInterval sum = ExactInterval.Of(0);
            for (int k = 0; k < 3; ++k)
                sum = sum + ExactInterval.Square(ExactInterval.Of(b[k]) - ExactInterval.Of(a[k]));
            return sum;
        }

        /// <summary>
        /// B5: an upper bound of the covering radius of the triangle (the largest distance from one of its points to its nearest corner),
        /// given upper bounds e01, e12, e20 of its edges: half the longest edge if the triangle is certainly not acute, else the smaller
        /// of l/sqrt(3) and the circumradius (both bound it for any triangle)
        /// </summary>
        internal static double CoveringUpper(Vector3d p0, Vector3d p1, Vector3d p2, double e01, double e12, double e20)
        {
            ExactInterval q01 = SquaredLength(p0, p1), q12 = SquaredLength(p1, p2), q20 = SquaredLength(p2, p0);
            // (the angle opposite an edge is at least 90 degrees if its square is at least the sum of the other two; that edge is then
            // the longest, and the covering radius is at most half of it)
            if ((q12 + q20 - q01).Hi <= 0) return up(0.5 * e01);
            if ((q01 + q20 - q12).Hi <= 0) return up(0.5 * e12);
            if ((q01 + q12 - q20).Hi <= 0) return up(0.5 * e20);
            double l = max(e01, max(e12, e20));
            double c = up(l * K);
            // circumradius e01 e12 e20 / (2 |e1 x e2|): the numerator's upper end, one division by twice a lower bound of the cross
            // product's length, one step up
            ExactInterval x = cross_squared(p0, p1, p2);
            double den = ExactInterval.Down(Math.Sqrt(x.Lo));
            // (den below MaxValue / 2: twice it is exact)
            if (den > 0 && den < 8.9884656743115785E+307) {
                double num = (ExactInterval.Of(e01) * ExactInterval.Of(e12) * ExactInterval.Of(e20)).Hi;
                double r = up(num / (2 * den));
                if (r < c) c = r;
            }
            return c;
        }

        // an enclosure of |(p1 - p0) x (p2 - p0)|^2
        static ExactInterval cross_squared(Vector3d p0, Vector3d p1, Vector3d p2)
        {
            ExactInterval ax = ExactInterval.Of(p1.x) - ExactInterval.Of(p0.x), ay = ExactInterval.Of(p1.y) - ExactInterval.Of(p0.y),
                az = ExactInterval.Of(p1.z) - ExactInterval.Of(p0.z);
            ExactInterval bx = ExactInterval.Of(p2.x) - ExactInterval.Of(p0.x), by = ExactInterval.Of(p2.y) - ExactInterval.Of(p0.y),
                bz = ExactInterval.Of(p2.z) - ExactInterval.Of(p0.z);
            ExactInterval cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
            ExactInterval sum = ExactInterval.Square(cx) + ExactInterval.Square(cy) + ExactInterval.Square(cz);
            return new ExactInterval(Math.Max(0, sum.Lo), sum.Hi);
        }

        /// <summary>
        /// B3: an upper bound of the distance between the stored cut point c = fl(a + fl(lambda fl(b - a))) (per component, the order of
        /// Vector3d's operators) and the exact a + lambda (b - a): the L1 sum of the enclosure's distances from c, each step rounded up
        /// </summary>
        internal static double CutRounding(Vector3d a, Vector3d b, double lambda, Vector3d c)
        {
            double rho = 0;
            ExactInterval li = ExactInterval.Of(lambda);
            for (int k = 0; k < 3; ++k) {
                ExactInterval ai = ExactInterval.Of(a[k]);
                ExactInterval e = ai + li * (ExactInterval.Of(b[k]) - ai);
                double dk = max(up(e.Hi - c[k]), up(c[k] - e.Lo));
                rho = up(rho + max(dk, 0));
            }
            return double.IsNaN(rho) ? double.PositiveInfinity : rho;
        }

        /// <summary> the next double above x (one rounded operation's result covers its exact value); NaN gives +infinity </summary>
        internal static double up(double x) => double.IsNaN(x) ? double.PositiveInfinity : ExactInterval.Up(x);

        /// <summary> Math.Max that propagates NaN as +infinity </summary>
        internal static double max(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.PositiveInfinity;
            return (a > b) ? a : b;
        }

        // an upper bound of sqrt(s) for an upper bound s of a square (Math.Sqrt rounds to nearest: one step up covers it)
        static double sqrt_up(double s)
        {
            if (double.IsNaN(s) || double.IsPositiveInfinity(s)) return double.PositiveInfinity;
            if (!(s > 0)) return 0;
            return ExactInterval.Up(Math.Sqrt(s));
        }
    }
}
