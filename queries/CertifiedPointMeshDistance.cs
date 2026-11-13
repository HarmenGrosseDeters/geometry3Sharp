// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Numerics;
using g3;

namespace gs
{
    /// <summary>
    /// A certified lower bound of the distance from a point (a double vector, taken exactly) to a triangle mesh: never above the
    /// exact distance, rounding included (#738). Every triangle is first bounded by the distance to its bounding box, evaluated with
    /// outward rounding (ExactInterval), so no triangle is dropped by rounding; the triangles that may be the nearest are then
    /// measured exactly, in integers (the coordinates of the point and the triangle scaled to a common power of two), with an
    /// exact region test; the squared distance, converted to double, and its square root are rounded down where they are not exact.
    /// Any finite coordinates (the exact parts have no range limit; a box bound whose square overflows does not prune). In
    /// MeshHausdorffDistance the coordinates are at most about 2^90 (copies beyond), so the doubles stay finite.
    /// </summary>
    internal static class CertifiedPointMeshDistance
    {
        /// <summary> a lower bound of the distance from p to the triangles of mesh (+infinity for no triangle) </summary>
        public static double LowerBound(DMesh3 mesh, Vector3d p)
        {
            // pass 1: an upper bound of the smallest squared distance, from the exact distance to the triangle whose box is nearest
            // (any triangle's exact distance is an upper bound of the minimum)
            int first = -1; double firstBox = double.PositiveInfinity;
            foreach (int tid in mesh.TriangleIndices()) {
                double lb = box_lower_squared(mesh, tid, p);
                if (lb < firstBox) { firstBox = lb; first = tid; }
            }
            if (first < 0)
                return double.PositiveInfinity;
            BigInteger bestNum, bestDen; int bestExp;
            exact_squared(mesh, first, p, out bestNum, out bestDen, out bestExp);
            // pass 2: every triangle whose box may be nearer than the best so far is measured exactly
            foreach (int tid in mesh.TriangleIndices()) {
                if (tid == first) continue;
                double lb = box_lower_squared(mesh, tid, p);
                if (greater(lb, bestNum, bestDen, bestExp)) continue;     // (lb is a lower bound: the triangle is not nearer)
                exact_squared(mesh, tid, p, out BigInteger n, out BigInteger d, out int x);
                if (less(n, d, x, bestNum, bestDen, bestExp)) { bestNum = n; bestDen = d; bestExp = x; }
            }
            // d^2 = num / den * 2^exp; rounded down twice (ToDouble truncates the quotient and then rounds to nearest once, and once
            // more in the subnormal range) unless the conversion is exact, and its root rounded down (sqrt rounds to nearest: one step
            // down is below the root) unless it is exact: exact values stay exact (eg 0.125 for two planes 0.125 apart; numerics review)
            if (bestNum.IsZero)
                return 0;
            double d2 = ExactPoint3.ToDouble(bestNum, bestDen, bestExp);
            if (!equals(d2, bestNum, bestDen, bestExp))
                d2 = ExactInterval.Down(ExactInterval.Down(d2));
            if (!(d2 > 0))
                return 0;
            double r = Math.Sqrt(d2);
            if (!(r > 0) || !square_equals(r, d2))
                r = ExactInterval.Down(r);
            return (r > 0 && !double.IsInfinity(r)) ? r : (r > 0 ? double.MaxValue : 0);
        }

        // (a test helper in the library: the tests compare certified upper bounds with the exact distance through it)
        /// <summary> (tests) is u at least the exact distance from p to the triangles of mesh? Exact, over all triangles (true for no
        /// triangle only if u is +infinity) </summary>
        internal static bool AtLeastDistance(DMesh3 mesh, Vector3d p, double u)
        {
            if (double.IsNaN(u)) return false;
            if (double.IsPositiveInfinity(u)) return true;
            if (u < 0) return false;
            bool any = false; BigInteger bn = 0, bd = 1; int bx = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                exact_squared(mesh, tid, p, out BigInteger n, out BigInteger d, out int x);
                if (!any || less(n, d, x, bn, bd, bx)) { bn = n; bd = d; bx = x; any = true; }
            }
            if (!any) return false;
            if (u == 0) return bn.IsZero;
            // u^2 >= bn / bd 2^bx  <=>  m^2 bd 2^(2e - bx) >= bn
            ExactPoint3.Decompose(u, out BigInteger m, out int e);
            return compare(m * m * bd, 2 * e - bx, bn) >= 0;
        }

        // a lower bound of the squared distance from p to the bounding box of the triangle: per axis the gap below the box or above
        // it, each difference and the sum of squares rounded down
        static double box_lower_squared(DMesh3 mesh, int tid, Vector3d p)
        {
            Index3i t = mesh.GetTriangle(tid);
            Vector3d a = mesh.GetVertex(t.a), b = mesh.GetVertex(t.b), c = mesh.GetVertex(t.c);
            double sum = 0;
            for (int k = 0; k < 3; ++k) {
                double lo = Math.Min(a[k], Math.Min(b[k], c[k])), hi = Math.Max(a[k], Math.Max(b[k], c[k]));
                double g = 0;
                if (p[k] < lo) g = ExactInterval.Down(lo - p[k]);
                else if (p[k] > hi) g = ExactInterval.Down(p[k] - hi);
                if (g > 0) sum = ExactInterval.Down(sum + ExactInterval.Down(g * g));
            }
            return (sum > 0) ? sum : 0;
        }

        // d (a finite double) == num / den * 2^exp, exactly
        static bool equals(double d, BigInteger num, BigInteger den, int exp)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            if (d == 0) return num.IsZero;
            ExactPoint3.Decompose(d, out BigInteger m, out int e);
            return compare(m * den, e - exp, num) == 0;
        }

        // r * r == d exactly (r, d finite and positive)
        static bool square_equals(double r, double d)
        {
            if (double.IsInfinity(r) || double.IsInfinity(d)) return false;
            ExactPoint3.Decompose(r, out BigInteger mr, out int er);
            ExactPoint3.Decompose(d, out BigInteger md, out int ed);
            return compare(mr * mr, 2 * er - ed, md) == 0;
        }

        // lb (a double) > num / den * 2^exp, exactly; false for a non-finite lb (a box gap whose square overflowed proves nothing
        // against an exact value that may be as large: the triangle is measured)
        static bool greater(double lb, BigInteger num, BigInteger den, int exp)
        {
            if (!(lb > 0) || double.IsInfinity(lb)) return false;
            ExactPoint3.Decompose(lb, out BigInteger m, out int e);
            // m 2^e > num / den 2^exp  <=>  m den 2^(e - exp) > num
            return compare(m * den, e - exp, num) > 0;
        }

        // n1 / d1 2^x1 < n2 / d2 2^x2, exactly (dens > 0)
        static bool less(BigInteger n1, BigInteger d1, int x1, BigInteger n2, BigInteger d2, int x2)
        {
            return compare(n1 * d2, x1 - x2, n2 * d1) < 0;
        }

        // sign of a 2^s - b
        static int compare(BigInteger a, int s, BigInteger b)
        {
            if (s >= 0) return (a << s).CompareTo(b);
            return a.CompareTo(b << -s);
        }

        // the exact squared distance from p to the triangle, as num / den * 2^exp (den > 0)
        static void exact_squared(DMesh3 mesh, int tid, Vector3d p, out BigInteger num, out BigInteger den, out int exp)
        {
            Index3i t = mesh.GetTriangle(tid);
            Vector3d[] pts = { mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c), p };
            // every coordinate as an integer times 2^e0, e0 the smallest exponent
            var m = new BigInteger[12]; var e = new int[12];
            int e0 = int.MaxValue;
            for (int i = 0; i < 4; ++i)
                for (int k = 0; k < 3; ++k) {
                    ExactPoint3.Decompose(pts[i][k], out m[3 * i + k], out e[3 * i + k]);
                    if (!m[3 * i + k].IsZero) e0 = Math.Min(e0, e[3 * i + k]);
                }
            if (e0 == int.MaxValue) e0 = 0;
            var v = new BigInteger[4, 3];
            for (int i = 0; i < 4; ++i)
                for (int k = 0; k < 3; ++k)
                    v[i, k] = m[3 * i + k].IsZero ? BigInteger.Zero : m[3 * i + k] << (e[3 * i + k] - e0);
            exp = 2 * e0;
            BigInteger[] V0 = row(v, 0), V1 = row(v, 1), V2 = row(v, 2), P = row(v, 3);
            BigInteger[] e1 = sub(V1, V0), e2 = sub(V2, V0), d = sub(V0, P);
            BigInteger a = dot(e1, e1), b = dot(e1, e2), c = dot(e2, e2), dd = dot(e1, d), ee = dot(e2, d), f = dot(d, d);
            BigInteger det = a * c - b * b;     // (>= 0; 0 for a degenerate triangle)
            if (det.Sign > 0) {
                // the projection of p onto the plane: barycentric (s, t) / det; inside the triangle when s, t >= 0 and s + t <= det
                BigInteger s = b * ee - c * dd, tt = b * dd - a * ee;
                if (s.Sign >= 0 && tt.Sign >= 0 && s + tt <= det) {
                    // |d + S e1 + T e2|^2 with S = s / det, T = t / det, times det^2
                    num = s * (a * s + b * tt + 2 * dd * det) + tt * (b * s + c * tt + 2 * ee * det) + f * det * det;
                    den = det * det;
                    if (num.Sign < 0) num = BigInteger.Zero;
                    return;
                }
            }
            // otherwise the nearest point is on an edge (for a degenerate triangle too: it is the union of its edges)
            segment(V0, V1, P, out num, out den);
            segment(V1, V2, P, out BigInteger n2, out BigInteger d2);
            if (n2 * den < num * d2) { num = n2; den = d2; }
            segment(V2, V0, P, out BigInteger n3, out BigInteger d3);
            if (n3 * den < num * d3) { num = n3; den = d3; }
        }

        // the exact squared distance from p to the segment [u, w], as num / den
        static void segment(BigInteger[] u, BigInteger[] w, BigInteger[] p, out BigInteger num, out BigInteger den)
        {
            BigInteger[] dir = sub(w, u), q = sub(p, u);
            BigInteger ww = dot(dir, dir), t = dot(q, dir), qq = dot(q, q);
            if (ww.IsZero || t.Sign <= 0) { num = qq; den = BigInteger.One; return; }
            if (t >= ww) { BigInteger[] r = sub(p, w); num = dot(r, r); den = BigInteger.One; return; }
            num = qq * ww - t * t;      // |q|^2 - t^2 / |dir|^2, times |dir|^2
            den = ww;
        }

        static BigInteger[] row(BigInteger[,] v, int i) => new[] { v[i, 0], v[i, 1], v[i, 2] };
        static BigInteger[] sub(BigInteger[] x, BigInteger[] y) => new[] { x[0] - y[0], x[1] - y[1], x[2] - y[2] };
        static BigInteger dot(BigInteger[] x, BigInteger[] y) => x[0] * y[0] + x[1] * y[1] + x[2] * y[2];
    }
}
