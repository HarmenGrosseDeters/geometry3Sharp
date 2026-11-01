using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    /// <summary>
    /// Fit minimal bounding-circle to a set of 2D points.
    /// Incremental construction in random order (Welzl 1991, written as three nested loops), expected O(N), on the points
    /// translated to the first one and scaled by a power of two of their extent. So the decisions depend on neither the
    /// position nor the size of the set, and any finite coordinates work. One limit: with a coordinate at or above 2^1021,
    /// all coordinates are first divided by 4, which rounds away the last two units of a subnormal coordinate in that set.
    /// The radius is up to about 16 eps of the extent above the minimal one, plus the rounding of the center to a coordinate.
    /// Circle2d.Contains is true for every input point, as long as the radius is above about 1e-150: below that, squared
    /// distances lose bits as subnormal numbers (and from about 1e-162 they are 0, so Contains is simply true).
    /// A radius above the double range is +Infinity.
    /// The same input gives the same bits (fixed seed). Scaling the input by a power of two scales the result by it, bit
    /// for bit, unless a subnormal coordinate rounds in that scaling.
    /// </summary>
    public class ContMinCircle2
    {
        // how far outside a point counts as inside, in the scaled coordinates (the largest is in [1, 2)): only the rounding of a circle
        const double RoundingSlack = 16 * MathUtil.Epsilon;
        const double DefaultEpsilon = 1e-05;
        // 2^1021: below this, the difference of two coordinates does not overflow
        const double LargeCoordinate = 2.2471164185778949e307;

        double mEpsilon;
        internal long ContainsTests;    // for the complexity test

        public Circle2d Result;

        /// <param name="epsilon">Dimensionless (it was an absolute squared length in upstream): three points are treated as
        /// collinear if the sine of their triangle's largest angle is at most this, and the circle over their longest edge
        /// is used. Below about 0.866 this takes only obtuse triangles, for which that circle is the minimal one. From about
        /// 0.866 (the sine of 60 degrees, the least that the largest angle of an acute triangle can have) it also takes acute
        /// triangles: the result still contains every point but is not minimal, with no error or status, which is what an
        /// upstream caller gets who passes a large absolute epsilon. Negative or NaN mean the default.</param>
        public ContMinCircle2(IList<Vector2d> pointsIn, double epsilon = 1e-05) {
            mEpsilon = (epsilon >= 0) ? epsilon : DefaultEpsilon;
            int numPoints = pointsIn.Count;
            if (numPoints < 1)
                throw new Exception("ContMinCircle2: Input must contain points\n");

            double maxCoord = 0;
            for (int i = 0; i < numPoints; ++i) {
                Vector2d point = pointsIn[i];
                if (MathUtil.IsFinite(point.x) == false || MathUtil.IsFinite(point.y) == false)
                    throw new ArgumentException("ContMinCircle2: non-finite point " + i);
                maxCoord = Math.Max(maxCoord, Math.Max(Math.Abs(point.x), Math.Abs(point.y)));
            }

            // Relative to the first point (the difference of the largest coordinates would overflow, so those go down by
            // 4 first), and scaled by a power of two of the extent. A difference rounds by at most eps of itself.
            int preShift = (maxCoord >= LargeCoordinate) ? -2 : 0;
            Vector2d origin = pointsIn[0];
            Vector2d[] p = new Vector2d[numPoints];
            double extent = 0;
            for (int i = 0; i < numPoints; ++i) {
                p[i] = Difference(pointsIn[i], origin, preShift, 0);
                extent = Math.Max(extent, Math.Max(Math.Abs(p[i].x), Math.Abs(p[i].y)));
            }
            if (extent == 0) {
                Result = new Circle2d(origin, 0);
                return;
            }
            int shift = Exponent(extent);
            for (int i = 0; i < numPoints; ++i)
                p[i] = new Vector2d(ExactPoint3.ScaleB(p[i].x, -shift), ExactPoint3.ScaleB(p[i].y, -shift));

            // in random order (fixed seed: the rounding of the result depends on the order, and it must not vary between runs)
            Random r = new Random(31337);
            for (int i = numPoints - 1; i > 0; --i) {
                int j = r.Next() % (i + 1);
                Vector2d save = p[i];
                p[i] = p[j];
                p[j] = save;
            }

            Vector2d center = p[0];
            double radiusSqr = 0;
            for (int i = 1; i < numPoints; ++i) {
                if (Contains(p[i], center, radiusSqr))
                    continue;
                // p[i] is on the boundary of the minimal circle of p[0..i]
                center = p[i];
                radiusSqr = 0;
                for (int j = 0; j < i; ++j) {
                    if (Contains(p[j], center, radiusSqr) || p[j] == p[i])
                        continue;
                    // p[i] and p[j] are on the boundary of the minimal circle of p[0..j] and p[i]
                    CircleOverEdge(p[i], p[j], out center, out radiusSqr);
                    for (int k = 0; k < j; ++k) {
                        // A copy of p[i] or p[j] is on this circle, so "outside" is only rounding. With it the three points
                        // are collinear (det = 0), and the circle over their edge would drop the third point of the circle.
                        if (Contains(p[k], center, radiusSqr) == false && p[k] != p[i] && p[k] != p[j])
                            CircleThrough(p[i], p[j], p[k], out center, out radiusSqr);
                    }
                }
            }

            Vector2d resultCenter = new Vector2d(
                ExactPoint3.ScaleB(ExactPoint3.ScaleB(origin.x, preShift) + ExactPoint3.ScaleB(center.x, shift), -preShift),
                ExactPoint3.ScaleB(ExactPoint3.ScaleB(origin.y, preShift) + ExactPoint3.ScaleB(center.y, shift), -preShift));

            // No point stays outside by the rounding of the circle, or of its center to a coordinate. The root of d^2 can
            // square to below d^2, and Circle2d.Contains compares the squares. Those are the same bits as in the input's
            // coordinates, scaled, as long as they neither overflow nor are subnormal there.
            double farthest = 0;
            for (int i = 0; i < numPoints; ++i)
                farthest = Math.Max(farthest, Difference(pointsIn[i], resultCenter, preShift, shift).LengthSquared);
            double radius = Math.Sqrt(farthest);
            while (radius * radius < farthest)
                radius = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(radius) + 1);
            Result = new Circle2d(resultCenter, ExactPoint3.ScaleB(radius, shift - preShift));
        }

        static Vector2d Difference(Vector2d a, Vector2d b, int preShift, int shift)
        {
            return new Vector2d(
                ExactPoint3.ScaleB(ExactPoint3.ScaleB(a.x, preShift) - ExactPoint3.ScaleB(b.x, preShift), -shift),
                ExactPoint3.ScaleB(ExactPoint3.ScaleB(a.y, preShift) - ExactPoint3.ScaleB(b.y, preShift), -shift));
        }

        /// <summary> e with x in [2^e, 2^(e+1)), for finite x greater than 0 </summary>
        static int Exponent(double x)
        {
            if (x >= 2.2250738585072014e-308)
                return (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF) - 1023;
            return (int)((BitConverter.DoubleToInt64Bits(x * 18446744073709551616.0) >> 52) & 0x7FF) - 1023 - 64;    // subnormal: times 2^64
        }

        bool Contains(Vector2d point, Vector2d center, double radiusSqr)
        {
            ++ContainsTests;
            double allowed = Math.Sqrt(radiusSqr) + RoundingSlack;
            return point.DistanceSquared(center) <= allowed * allowed;
        }

        static void CircleOverEdge(Vector2d a, Vector2d b, out Vector2d center, out double radiusSqr)
        {
            center = 0.5 * (a + b);
            radiusSqr = 0.25 * a.DistanceSquared(b);
        }

        void CircleThrough(Vector2d a, Vector2d b, Vector2d c, out Vector2d center, out double radiusSqr)
        {
            double lab = a.DistanceSquared(b), lac = a.DistanceSquared(c), lbc = b.DistanceSquared(c);
            // Based at the vertex opposite the longest edge: at the far end of a needle, the two edges are nearly parallel,
            // and the center's error is eps * L / angle. That made the next point falsely "outside", which moved the circle
            // to the wrong side (a radius 5% too large on tight clusters at the support points).
            double longest = Math.Max(lab, Math.Max(lac, lbc));
            Vector2d origin, u, v, edgeA, edgeB;
            double lu, lv;
            if (longest == lab) {
                origin = c; u = a - c; v = b - c; lu = lac; lv = lbc; edgeA = a; edgeB = b;
            } else if (longest == lac) {
                origin = b; u = a - b; v = c - b; lu = lab; lv = lbc; edgeA = a; edgeB = c;
            } else {
                origin = a; u = b - a; v = c - a; lu = lab; lv = lac; edgeA = b; edgeB = c;
            }
            double det = u.x * v.y - u.y * v.x;
            // |det| = sin(angle) * (the two edges at that angle), and the angle at this vertex is the largest.
            // (mEpsilon >= 0, so det = 0 ends here and never in the division)
            if (Math.Abs(det) <= mEpsilon * (Math.Sqrt(lu) * Math.Sqrt(lv))) {
                CircleOverEdge(edgeA, edgeB, out center, out radiusSqr);
                return;
            }
            double invDet = 0.5 / det;
            Vector2d q = new Vector2d((v.y * lu - u.y * lv) * invDet, (u.x * lv - v.x * lu) * invDet);
            center = origin + q;
            radiusSqr = q.LengthSquared;
        }


        /// <summary> not used any more; kept for API compatibility with subclasses </summary>
        protected class Support
        {
            public bool Contains(int index, IList<Vector2d> Points, int[] permutation, double epsilon)
            {
                for (int i = 0; i < Quantity; ++i) {
                    Vector2d diff = Points[permutation[index]] - Points[permutation[Index[i]]];
                    if (diff.LengthSquared < epsilon) {
                        return true;
                    }
                }
                return false;
            }

            public int Quantity;
            public Index3i Index;
        };

    }
}
