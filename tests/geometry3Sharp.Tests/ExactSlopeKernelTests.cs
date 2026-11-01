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
            int lp = K.PointLine(new Vector2d(0, 3), new Vector2d(2, 4));
            Assert.Equal(-1, K.Crossing(l, lp));
            Assert.Equal(-1, K.Crossing(lp, l));      // cached, both ways round
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
            // a vertex with the x (or the y) of a defining point only: no shortcut, decided by the heights
            int e = K.AddPlane(new Vector3d(3, 0, 0), new Vector3d(4, 0, 0), new Vector3d(3, 1, 0));    // z = 0
            int f = K.AddPlane(new Vector3d(3, 2, 0), new Vector3d(5, 2, 0), new Vector3d(3, 3, 1));    // z = y - 2
            Assert.Equal(1, K.Side(K.PointVertex(new Vector2d(3, 1)), K.PlaneLine(e, f)));     // e = 0 > f = -1
            Assert.Equal(-1, K.Side(K.PointVertex(new Vector2d(3, 3)), K.PlaneLine(e, f)));    // (the R of f)
            Assert.Equal(-1, K.Side(K.PointVertex(new Vector2d(4, 3)), K.PlaneLine(e, f)));
            Assert.Equal(0, K.Side(K.PointVertex(new Vector2d(5, 2)), K.PlaneLine(e, f)));      // the Q of f, height 0 in both
            Assert.Equal(0, K.Side(K.PointVertex(new Vector2d(4, 2)), K.PlaneLine(e, f)));
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

        // ---- survivors of the kernel mutation run (follow-up of #267) ----

        // Approx is within 1e-12 (relative) of the exact point, also for nearly parallel lines, where the intervals are
        // wide and it must fall back to the exact rounding; checked before the vertex is rounded (Rounded caches)
        [Fact]
        public void Approx_IsAccurate_AlsoForNearlyParallelLines()
        {
            var rnd = new Random(31);
            for (int i = 0; i < 2000; ++i) {
                double t = (i % 2 == 0) ? 1e-14 * rnd.NextDouble() : rnd.NextDouble();      // the angle between the lines
                Vector2d c = new Vector2d(1000 * rnd.NextDouble(), 1000 * rnd.NextDouble());
                Vector2d u = new Vector2d(Math.Cos(rnd.NextDouble() * 6), Math.Sin(rnd.NextDouble() * 6));
                Vector2d w = new Vector2d(u.x * Math.Cos(t) - u.y * Math.Sin(t), u.x * Math.Sin(t) + u.y * Math.Cos(t));
                var K = new ExactSlopeKernel();
                var K2 = new ExactSlopeKernel();
                Func<ExactSlopeKernel, int> make = (k) => k.Crossing(k.PointLine(c - 3 * u, c + 5 * u), k.PointLine(c - 4 * w + new Vector2d(1e-3, 0), c + 2 * w + new Vector2d(1e-3, 0)));
                int v = make(K), v2 = make(K2);
                if (v < 0) { Assert.True(v2 < 0); continue; }
                Vector2d a = K.Approx(v), r = K2.Rounded(v2);
                Assert.True(a.Distance(r) <= 2e-12 * (Math.Abs(r.x) + Math.Abs(r.y)) + 1e-300, $"{a} {r} angle {t}");
            }
        }

        // ApproxHeight is within 1e-12 (relative to the magnitudes involved) of the exact height
        [Fact]
        public void ApproxHeight_IsAccurate()
        {
            var rnd = new Random(37);
            var K = new ExactSlopeKernel();
            for (int i = 0; i < 2000; ++i) {
                Vector3d p = R(rnd, 100), q = R(rnd, 100), r = R(rnd, 100);
                if (Math.Abs((q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x)) < 1) continue;
                int pl = K.AddPlane(p, q, r);
                double x = 200 * (rnd.NextDouble() - 0.5), y = 200 * (rnd.NextDouble() - 0.5);
                double h = K.Height(pl, x, y), ah = K.ApproxHeight(pl, x, y);
                Assert.True(Math.Abs(h - ah) <= 1e-9 * (1 + Math.Abs(h)), $"{h} {ah}");
            }
        }

        // a plane whose Nz interval contains 0 (nearly vertical, but not vertical) is still oriented and evaluated
        // exactly
        [Fact]
        public void NearlyVerticalPlane_TakesTheExactPath()
        {
            var K = new ExactSlopeKernel();
            Vector3d p = new Vector3d(0, 0, 0), q = new Vector3d(1, 1, 0), r = new Vector3d(1 + Math.Pow(2, -52), 1, 1);
            int a = K.AddPlane(p, q, r);       // Nz = -2^-52: clockwise, flipped
            int b = K.AddPlane(p, r, q);
            Assert.Equal(Nearest(Height(p, q, r, Q.Of(0.5), Q.Of(0.25))), K.Height(a, 0.5, 0.25));
            Assert.Equal(K.Height(a, 0.5, 0.25), K.Height(b, 0.5, 0.25));
            Assert.Equal(2, K.PlaneCount);
        }

        // the counters the benchmark reports: a decided side, an undecided one (exact stage), a construction
        [Fact]
        public void Counters()
        {
            var K = new ExactSlopeKernel();
            int l = K.PointLine(new Vector2d(0, 0), new Vector2d(2, 1));
            Assert.Equal(1, K.LineCount);
            int v = K.PointVertex(new Vector2d(0, 1));
            Assert.Equal(1, K.VertexCount);
            Assert.Equal(1, K.Side(v, l));
            Assert.Equal(1, K.PredicateCalls);
            Assert.Equal(0, K.ExactPredicates);
            // a double point exactly on the line: the interval contains 0, the exact stage decides
            Assert.Equal(0, K.Side(K.PointVertex(new Vector2d(4, 2)), l));
            Assert.Equal(2, K.PredicateCalls);
            Assert.Equal(1, K.ExactPredicates);
            Assert.Equal(0, K.Orient(K.PointVertex(new Vector2d(0, 0)), K.PointVertex(new Vector2d(1, 0.5)), K.PointVertex(new Vector2d(4, 2))));
            Assert.Equal(3, K.PredicateCalls);
            Assert.Equal(2, K.ExactPredicates);
            Assert.Equal(1, K.Orient(K.PointVertex(new Vector2d(0, 0)), K.PointVertex(new Vector2d(1, 0)), K.PointVertex(new Vector2d(0, 1))));
            Assert.Equal(4, K.PredicateCalls);
            Assert.Equal(2, K.ExactPredicates);
            Assert.Equal(8, K.VertexCount);
            // a double point is its own rounding (no construction); a crossing and a height are constructions
            Assert.Equal(new Vector2d(0, 1), K.Rounded(v));
            Assert.Equal(0, K.ExactConstructions);
            int x = K.Crossing(l, K.PointLine(new Vector2d(1, -3), new Vector2d(1, 7)));
            K.Rounded(x); K.Rounded(x);
            Assert.Equal(1, K.ExactConstructions);
            K.Height(K.AddHorizontalPlane(3), 1, 1);
            Assert.Equal(2, K.ExactConstructions);
            // a vertex on one of its own lines needs no predicate
            Assert.Equal(0, K.Side(x, l));
            Assert.Equal(4, K.PredicateCalls);
        }
    }
}
