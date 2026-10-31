using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    /// <summary>
    /// Fit minimal bounding-circle to a set of 2D points.
    /// Incremental construction in random order (Welzl 1991, in the iterative form of Seidel), expected O(N). Every point
    /// lies inside the result: the radius is the distance to the farthest point from the center.
    /// The same input gives the same bits (fixed seed), and a scale by a power of two scales the result exactly.
    /// </summary>
    public class ContMinCircle2
    {
        // how far outside a point counts as inside, in units of the largest coordinate: only the rounding of a circle
        const double RoundingSlack = 16 * MathUtil.Epsilon;

        double mEpsilon;
        double slack;

        public Circle2d Result;

        /// <param name="epsilon">Dimensionless (it was an absolute squared length in upstream): three points are treated as
        /// collinear if the sine of their triangle's largest angle is at most this, and the circle over their longest edge
        /// is used, which is the minimal one for such an obtuse triangle.</param>
        public ContMinCircle2(IList<Vector2d> pointsIn, double epsilon = 1e-05) {
            mEpsilon = epsilon;
            int numPoints = pointsIn.Count;
            if (numPoints < 1)
                throw new Exception("ContMinCircle2: Input must contain points\n");

            // in random order (fixed seed: the rounding of the result depends on the order, and it must not vary between runs)
            Vector2d[] p = new Vector2d[numPoints];
            double maxCoord = 0;
            for (int i = 0; i < numPoints; ++i) {
                p[i] = pointsIn[i];
                if (MathUtil.IsFinite(p[i].x) == false || MathUtil.IsFinite(p[i].y) == false)
                    throw new ArgumentException("ContMinCircle2: non-finite point " + i);
                maxCoord = Math.Max(maxCoord, Math.Max(Math.Abs(p[i].x), Math.Abs(p[i].y)));
            }
            slack = RoundingSlack * maxCoord;
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
                    if (Contains(p[j], center, radiusSqr))
                        continue;
                    // p[i] and p[j] are on the boundary of the minimal circle of p[0..j] and p[i]
                    CircleOverEdge(p[i], p[j], out center, out radiusSqr);
                    for (int k = 0; k < j; ++k) {
                        if (Contains(p[k], center, radiusSqr) == false)
                            CircleThrough(p[i], p[j], p[k], out center, out radiusSqr);
                    }
                }
            }

            // no point stays outside by the rounding of the circle
            double farthest = 0;
            for (int i = 0; i < numPoints; ++i)
                farthest = Math.Max(farthest, p[i].DistanceSquared(center));
            Result = new Circle2d(center, Math.Sqrt(farthest));
        }

        bool Contains(Vector2d point, Vector2d center, double radiusSqr)
        {
            double allowed = Math.Sqrt(radiusSqr) + slack;
            return point.DistanceSquared(center) <= allowed * allowed;
        }

        static void CircleOverEdge(Vector2d a, Vector2d b, out Vector2d center, out double radiusSqr)
        {
            center = 0.5 * (a + b);
            radiusSqr = 0.25 * a.DistanceSquared(b);
        }

        void CircleThrough(Vector2d a, Vector2d b, Vector2d c, out Vector2d center, out double radiusSqr)
        {
            Vector2d ab = b - a, ac = c - a;
            double lab = ab.LengthSquared, lac = ac.LengthSquared, lbc = b.DistanceSquared(c);
            double det = ab.x * ac.y - ab.y * ac.x;
            // |det| = sin(angle) * (the two edges at that angle), for the largest angle those are the two shorter edges.
            // (one root per edge: their product would overflow far before the circle does)
            double longest = Math.Max(lab, Math.Max(lac, lbc));
            double shorterEdges = (lab == longest) ? Math.Sqrt(lac) * Math.Sqrt(lbc)
                : (lac == longest) ? Math.Sqrt(lab) * Math.Sqrt(lbc) : Math.Sqrt(lab) * Math.Sqrt(lac);
            if (Math.Abs(det) <= mEpsilon * shorterEdges) {
                if (lab == longest)
                    CircleOverEdge(a, b, out center, out radiusSqr);
                else if (lac == longest)
                    CircleOverEdge(a, c, out center, out radiusSqr);
                else
                    CircleOverEdge(b, c, out center, out radiusSqr);
                return;
            }
            double invDet = 0.5 / det;
            Vector2d q = new Vector2d((ac.y * lab - ab.y * lac) * invDet, (ab.x * lac - ac.x * lab) * invDet);
            center = a + q;
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
