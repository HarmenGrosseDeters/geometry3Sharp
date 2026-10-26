// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Shape quality of the triangles of a mesh, with the triangle metrics of the Verdict library (Stimpson,
    /// Ernst, Knupp, Pebay, Thompson, "The Verdict Geometric Quality Library", Sandia SAND2007-1751; Knupp,
    /// "Algebraic mesh quality metrics", SIAM J. Sci. Comput. 2001), implemented from their definitions. For a
    /// triangle with edge lengths L0, L1, L2 and area A:
    ///   - minimum and maximum angle (degrees; equilateral 60);
    ///   - aspect ratio  Lmax (L0 + L1 + L2) / (4 sqrt(3) A)  (1 for equilateral, up to infinity);
    ///   - scaled Jacobian  2 / sqrt(3) * min over the corners of sin(corner angle)  (1 for equilateral, 0 for
    ///     degenerate; unsigned: a surface mesh has no reference normal for Verdict's sign);
    ///   - condition number  (L0^2 + L1^2 + L2^2) / (4 sqrt(3) A)  (1 for equilateral, up to infinity).
    /// Each metric as a distribution over the triangles, from the bad side (QualityDistribution), and the number of
    /// triangles outside Verdict's acceptable ranges (min angle 30-60, max angle 60-90, aspect ratio and condition
    /// 1-1.3, scaled Jacobian at least 0.5). Triangles with zero area have aspect ratio and condition infinity,
    /// scaled Jacobian 0; with a zero length edge also min angle 0, max angle 180. All metrics are scale invariant
    /// (bit for bit under scaling by powers of two). Unweighted: every triangle counts once.
    /// </summary>
    public class TriangleQualityStatistics
    {
        /// <summary> number of triangles </summary>
        public int Triangles;
        /// <summary> triangles with area at most 1e-12 times the squared diagonal of the triangles' bounds </summary>
        public int Degenerate;
        /// <summary> distributions of the metrics (Worst is the smallest min angle and scaled Jacobian, the largest of the others) </summary>
        public QualityDistribution MinAngle, MaxAngle, AspectRatio, ScaledJacobian, Condition;
        /// <summary> triangles with min angle below 30 degrees (Verdict's acceptable range), below 10 and below 1 (slivers) </summary>
        public int MinAngleBelow30, MinAngleBelow10, MinAngleBelow1;
        /// <summary>
        /// triangles with max angle above 90 degrees (Verdict's acceptable range; right angles, as in grids, count as
        /// 90 up to 1e-3 degrees, so that rounding, eg of coordinates far from the origin, does not count them) and above 160 (needles, caps)
        /// </summary>
        public int MaxAngleAbove90, MaxAngleAbove160;
        /// <summary> triangles outside Verdict's acceptable range: aspect ratio above 1.3, scaled Jacobian below 0.5, condition above 1.3 </summary>
        public int AspectRatioAbove13, ScaledJacobianBelow05, ConditionAbove13;

        /// <summary> the Verdict metrics of the triangle (a, b, c), see the class </summary>
        public static void Measure(Vector3d a, Vector3d b, Vector3d c, out double minAngle, out double maxAngle,
            out double aspectRatio, out double scaledJacobian, out double condition)
        {
            Vector3d e0 = b - a, e1 = c - b, e2 = a - c;
            double l0 = e0.Length, l1 = e1.Length, l2 = e2.Length;
            double twiceArea = e0.Cross(-e2).Length;
            double area = 0.5 * twiceArea;
            if (l0 > 0 && l1 > 0 && l2 > 0) {
                // corner angles from atan2 of sine and cosine (accurate also for slivers)
                double angA = Math.Atan2(twiceArea, e0.Dot(-e2));
                double angB = Math.Atan2(twiceArea, e1.Dot(-e0));
                double angC = Math.Atan2(twiceArea, e2.Dot(-e1));
                minAngle = Math.Min(angA, Math.Min(angB, angC)) * MathUtil.Rad2Deg;
                maxAngle = Math.Max(angA, Math.Max(angB, angC)) * MathUtil.Rad2Deg;
                // sin of the corner angles, twice the area over the products of the adjacent edges
                double sinMin = Math.Min(twiceArea / (l0 * l2), Math.Min(twiceArea / (l0 * l1), twiceArea / (l1 * l2)));
                scaledJacobian = 2 / Math.Sqrt(3) * sinMin;
            } else {
                minAngle = 0; maxAngle = 180;
                scaledJacobian = 0;
            }
            if (area > 0) {
                double lmax = Math.Max(l0, Math.Max(l1, l2));
                aspectRatio = lmax * (l0 + l1 + l2) / (4 * Math.Sqrt(3) * area);
                condition = (l0 * l0 + l1 * l1 + l2 * l2) / (4 * Math.Sqrt(3) * area);
            } else {
                aspectRatio = condition = double.PositiveInfinity;
            }
        }

        /// <summary> the statistics of the triangles of mesh (all fields NaN / 0 for a mesh without triangles) </summary>
        public static TriangleQualityStatistics Compute(DMesh3 mesh)
        {
            var r = new TriangleQualityStatistics();
            int n = mesh.TriangleCount;
            double[] minA = new double[n], maxA = new double[n], aspect = new double[n], sj = new double[n], cond = new double[n];
            double diag = MeshRepairMetrics.TriangleBounds(mesh).DiagonalLength;
            double degenerateArea = n > 0 ? 1e-12 * diag * diag : 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            int i = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                mesh.GetTriVertices(tid, ref a, ref b, ref c);
                Measure(a, b, c, out minA[i], out maxA[i], out aspect[i], out sj[i], out cond[i]);
                if (!(0.5 * (b - a).Cross(c - a).Length > degenerateArea)) r.Degenerate++;
                if (minA[i] < 30) r.MinAngleBelow30++;
                if (minA[i] < 10) r.MinAngleBelow10++;
                if (minA[i] < 1) r.MinAngleBelow1++;
                if (maxA[i] > 90 + 1e-3) r.MaxAngleAbove90++;
                if (maxA[i] > 160) r.MaxAngleAbove160++;
                if (aspect[i] > 1.3) r.AspectRatioAbove13++;
                if (sj[i] < 0.5) r.ScaledJacobianBelow05++;
                if (cond[i] > 1.3) r.ConditionAbove13++;
                i++;
            }
            r.Triangles = n;
            r.MinAngle = QualityDistribution.Compute(minA, false);
            r.MaxAngle = QualityDistribution.Compute(maxA, true);
            r.AspectRatio = QualityDistribution.Compute(aspect, true);
            r.ScaledJacobian = QualityDistribution.Compute(sj, false);
            r.Condition = QualityDistribution.Compute(cond, true);
            return r;
        }
    }


    /// <summary>
    /// Distribution of a quality metric over the elements, read from the bad side: Worst, the value that 1% and 5%
    /// of the elements are worse than (nearest rank), and the median. NaN for no elements.
    /// </summary>
    public struct QualityDistribution
    {
        public double Worst, Worst1Percent, Worst5Percent, Median;

        /// <summary> highIsBad: large values are bad (aspect ratio), else small values (min angle) </summary>
        public static QualityDistribution Compute(double[] values, bool highIsBad)
        {
            var d = new QualityDistribution() { Worst = double.NaN, Worst1Percent = double.NaN, Worst5Percent = double.NaN, Median = double.NaN };
            int n = values.Length;
            if (n == 0)
                return d;
            double[] sorted = (double[])values.Clone();
            Array.Sort(sorted);
            Func<double, double> rank = (q) => {
                int k = (int)Math.Floor(q * (n - 1));
                return highIsBad ? sorted[n - 1 - k] : sorted[k];
            };
            d.Worst = rank(0);
            d.Worst1Percent = rank(0.01);
            d.Worst5Percent = rank(0.05);
            d.Median = rank(0.5);
            return d;
        }
    }
}
