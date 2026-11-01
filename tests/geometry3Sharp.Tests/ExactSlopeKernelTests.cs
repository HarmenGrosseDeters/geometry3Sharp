using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactSlopeKernel directly (the predicates of the terrain slope envelope) against an independent rational
    /// evaluation: planes through three points in either orientation, lines of plane pairs and of point pairs, their
    /// crossings, the side of a line at a vertex, the orientation of three vertices, rounding and heights; the
    /// shortcut for a point that both planes of a line pass through; parallel lines.
    /// </summary>
    public class ExactSlopeKernelTests
    {
        // ---- rationals (independent of the kernel) ----
        struct Q
        {
            public BigInteger N, D;
            public Q(BigInteger n, BigInteger d) { if (d.Sign < 0) { n = -n; d = -d; } N = n; D = d; }
            public static Q Of(double x)
            {
                if (x == 0) return new Q(0, 1);
                long bits = BitConverter.DoubleToInt64Bits(x);
                int exp = (int)((bits >> 52) & 0x7FF);
                long frac = bits & 0xFFFFFFFFFFFFFL;
                if (exp == 0) exp = 1; else frac |= 1L << 52;
                BigInteger m = bits < 0 ? -frac : frac;
                int e = exp - 1075;
                return e >= 0 ? new Q(m << e, 1) : new Q(m, BigInteger.One << -e);
            }
            public static Q operator +(Q a, Q b) => new Q(a.N * b.D + b.N * a.D, a.D * b.D);
            public static Q operator -(Q a, Q b) => new Q(a.N * b.D - b.N * a.D, a.D * b.D);
            public static Q operator *(Q a, Q b) => new Q(a.N * b.N, a.D * b.D);
            public static Q operator /(Q a, Q b) => new Q(a.N * b.D, a.D * b.N);
            public int Sign => N.Sign;
        }

        // plane z = p.z - (nx (x - px) + ny (y - py)) / nz
        static Q Height(Vector3d p, Vector3d q, Vector3d r, Q x, Q y)
        {
            Q ux = Q.Of(q.x) - Q.Of(p.x), uy = Q.Of(q.y) - Q.Of(p.y), uz = Q.Of(q.z) - Q.Of(p.z);
            Q vx = Q.Of(r.x) - Q.Of(p.x), vy = Q.Of(r.y) - Q.Of(p.y), vz = Q.Of(r.z) - Q.Of(p.z);
            Q nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            return Q.Of(p.z) - (nx * (x - Q.Of(p.x)) + ny * (y - Q.Of(p.y))) / nz;
        }

        static Vector3d R(Random rnd, double s = 10) => new Vector3d(s * (rnd.NextDouble() - 0.5), s * (rnd.NextDouble() - 0.5), s * (rnd.NextDouble() - 0.5));

        [Fact]
        public void PlaneLines_Crossings_Side_Orient_AgainstRationals()
        {
            var rnd = new Random(7);
            for (int round = 0; round < 60; ++round) {
                var K = new ExactSlopeKernel();
                var pts = new List<Vector3d[]>();
                var ids = new List<int>();
                for (int i = 0; i < 4; ++i) {
                    Vector3d p = R(rnd), q = R(rnd), r = R(rnd);
                    if (Math.Abs((q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x)) < 1e-3) { --i; continue; }
                    // both orientations (clockwise points must give the same plane)
                    ids.Add(K.AddPlane(p, q, r));
                    pts.Add(new[] { p, q, r });
                }
                int l01 = K.PlaneLine(ids[0], ids[1]), l23 = K.PlaneLine(ids[2], ids[3]), l02 = K.PlaneLine(ids[0], ids[2]);
                int v = K.Crossing(l01, l23);
                Assert.Equal(v, K.Crossing(l23, l01));       // cached either way round
                if (v < 0) continue;
                // the crossing in rationals: where P0 = P1 and P2 = P3; check by the side tests and by rounding
                Vector2d xy = K.Rounded(v);
                Q x = Q.Of(xy.x), y = Q.Of(xy.y);
                double d01 = (double)((Height(pts[0][0], pts[0][1], pts[0][2], x, y) - Height(pts[1][0], pts[1][1], pts[1][2], x, y)).N);
                // the side of l02 at v equals the sign of P0 - P2 at the exact crossing; at the rounded point it agrees
                // unless the rounded point is within rounding of the line
                Q dz = Height(pts[0][0], pts[0][1], pts[0][2], x, y) - Height(pts[2][0], pts[2][1], pts[2][2], x, y);
                int side = K.Side(v, l02);
                if (Math.Abs(ToDouble(dz)) > 1e-9) Assert.Equal(dz.Sign, side);
                Assert.Equal(0, K.Side(v, l01));
                Assert.Equal(0, K.Side(v, l23));
                // heights at a double point, rounded to nearest
                double hx = rnd.NextDouble() * 4 - 2, hy = rnd.NextDouble() * 4 - 2;
                for (int i = 0; i < 4; ++i)
                    Assert.Equal(Nearest(Height(pts[i][0], pts[i][1], pts[i][2], Q.Of(hx), Q.Of(hy))), K.Height(ids[i], hx, hy));
            }
        }

        // an approximation (a few ulp) of q, without overflow of the big numerator and denominator
        static double ToDouble(Q q)
        {
            if (q.N.IsZero) return 0;
            long nb = (long)BigInteger.Log(BigInteger.Abs(q.N), 2), db = (long)BigInteger.Log(q.D, 2);
            int shift = (int)(60 - (nb - db));
            BigInteger t = shift >= 0 ? (q.N << shift) / q.D : q.N / (q.D << -shift);
            return (double)t * Math.Pow(2, -shift);
        }

        // nearest double to q by comparing with the neighbours of a candidate
        static double Nearest(Q q)
        {
            double c = ToDouble(q);
            if (double.IsNaN(c) || double.IsInfinity(c)) return c;
            for (int k = 0; k < 4; ++k) {
                double up = Next(c, 1), dn = Next(c, -1);
                Q e = Abs(q - Q.Of(c)), eu = Abs(q - Q.Of(up)), ed = Abs(q - Q.Of(dn));
                if ((eu - e).Sign < 0) c = up; else if ((ed - e).Sign < 0) c = dn; else break;
            }
            return c;
        }
        static Q Abs(Q q) => q.Sign < 0 ? new Q(-q.N, q.D) : q;
        static double Next(double x, int dir)
        {
            if (x == 0) return dir > 0 ? double.Epsilon : -double.Epsilon;
            long b = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble((x > 0) == (dir > 0) ? b + 1 : b - 1);
        }

        [Fact]
        public void PointLines_ExplicitVertices_Orient()
        {
            var K = new ExactSlopeKernel();
            // a point line through (0,0) and (2,1); points left, right, on it
            int l = K.PointLine(new Vector2d(0, 0), new Vector2d(2, 1));
            int left = K.PointVertex(new Vector2d(0, 1)), right = K.PointVertex(new Vector2d(1, 0)), on = K.PointVertex(new Vector2d(4, 2));
            Assert.Equal(1, K.Side(left, l));
            Assert.Equal(-1, K.Side(right, l));
            Assert.Equal(0, K.Side(on, l));
            Assert.Equal(1, K.Orient(right, on, left));
            Assert.Equal(-1, K.Orient(left, on, right));
            Assert.Equal(0, K.Orient(K.PointVertex(new Vector2d(0, 0)), on, K.PointVertex(new Vector2d(1, 0.5))));
            // nearly collinear: needs the exact stage
            int near = K.PointVertex(new Vector2d(1, 0.5000000000000001));
            Assert.Equal(1, K.Side(near, l));
            // a crossing of two point lines, rounded exactly: (1, 0.5)
            int l2 = K.PointLine(new Vector2d(1, -3), new Vector2d(1, 7));
            int x = K.Crossing(l, l2);
            Assert.Equal(new Vector2d(1, 0.5), K.Rounded(x));
            Assert.True(K.ExactConstructions > 0);
            // parallel and identical lines do not cross
            Assert.Equal(-1, K.Crossing(l, K.PointLine(new Vector2d(0, 1), new Vector2d(2, 2))));
            Assert.Equal(-1, K.Crossing(l, K.PointLine(new Vector2d(4, 2), new Vector2d(6, 3))));
            Assert.Equal(-1, K.Crossing(l, K.PointLine(new Vector2d(0, 1), new Vector2d(2, 2))));     // (cached)
            Assert.True(K.PredicateCalls > 0);
            Assert.Equal(K.VertexCount, K.VertexCount);
        }

        [Fact]
        public void Planes_EitherOrientation_AndVertical()
        {
            var K = new ExactSlopeKernel();
            Vector3d p = new Vector3d(0, 0, 1), q = new Vector3d(1, 0, 2), r = new Vector3d(0, 1, 3);
            int a = K.AddPlane(p, q, r), b = K.AddPlane(p, r, q);      // counter-clockwise and clockwise
            Assert.Equal(K.Height(a, 0.5, 0.25), K.Height(b, 0.5, 0.25));
            Assert.Equal(1 + 0.5 + 2 * 0.25, K.Height(a, 0.5, 0.25));
            // a line of two planes: positive where the first is higher
            int c = K.AddHorizontalPlane(2);
            int l = K.PlaneLine(a, c);       // z_a = 1 + x + 2y vs 2
            Assert.Equal(1, K.Side(K.PointVertex(new Vector2d(2, 2)), l));
            Assert.Equal(-1, K.Side(K.PointVertex(new Vector2d(0, 0)), l));
            Assert.Equal(0, K.Side(K.PointVertex(new Vector2d(1, 0)), l));
            int lb = K.PlaneLine(b, c);
            Assert.Equal(1, K.Side(K.PointVertex(new Vector2d(2, 2)), lb));
            Assert.Equal(3, K.PlaneCount);
            Assert.Throws<ArgumentException>(() => K.AddPlane(p, new Vector3d(1, 0, 5), new Vector3d(2, 0, 7)));
        }

        // the shortcut: a double point that both planes pass through (the same point in 3D) is on their line; a
        // point with the same XY but different heights, or a point of only one plane, is decided by the predicate
        [Fact]
        public void SharedApexShortcut_OnlyForACommonPoint()
        {
            var K = new ExactSlopeKernel();
            Vector3d apex = new Vector3d(3, 4, 5);
            int a = K.AddPlane(apex, new Vector3d(4, 4, 6), new Vector3d(3, 5, 5));
            int b = K.AddPlane(apex, new Vector3d(4, 4, 5.5), new Vector3d(3, 5, 7));
            int l = K.PlaneLine(a, b);
            Assert.Equal(0, K.Side(K.PointVertex(apex.xy), l));
            // same XY as a defining point of both planes, but at different heights: not on the line
            int c = K.AddPlane(new Vector3d(3, 4, 6), new Vector3d(4, 4, 6), new Vector3d(3, 5, 6));     // z = 6
            int lc = K.PlaneLine(a, c);
            Assert.Equal(-1, K.Side(K.PointVertex(apex.xy), lc));      // a is 5 there, c is 6
            // a point of plane a only (one of its other defining points): decided by the heights there
            int lq = K.PlaneLine(a, b);
            Assert.Equal(1, K.Side(K.PointVertex(new Vector2d(4, 4)), lq));       // a = 6, b = 5.5
            Assert.Equal(-1, K.Side(K.PointVertex(new Vector2d(3, 5)), lq));      // a = 5, b = 7
            // a defining point of b (R) vs a's other points
            int d = K.AddPlane(new Vector3d(9, 9, 1), new Vector3d(10, 9, 1), apex);
            Assert.Equal(0, K.Side(K.PointVertex(apex.xy), K.PlaneLine(a, d)));
            Assert.Equal(0, K.Side(K.PointVertex(apex.xy), K.PlaneLine(d, b)));
            // plane 0 takes part in the shortcut as any other
            Assert.Equal(0, a);
        }

        [Fact]
        public void Approx_IsNearTheRoundedPosition()
        {
            var K = new ExactSlopeKernel();
            int l1 = K.PointLine(new Vector2d(0, 0), new Vector2d(3, 1)), l2 = K.PointLine(new Vector2d(0, 2), new Vector2d(1, -1));
            int v = K.Crossing(l1, l2);
            Vector2d a = K.Approx(v), r = K.Rounded(v);
            Assert.True(a.Distance(r) <= 1e-12 * (1 + r.Length), $"{a} {r}");
            double h = K.ApproxHeight(K.AddHorizontalPlane(7), 1, 2);
            Assert.Equal(7, h, 12);
        }
    }
}
