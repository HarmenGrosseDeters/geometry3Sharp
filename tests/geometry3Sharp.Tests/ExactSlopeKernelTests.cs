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
        public void CompareXY_IsTheExactLexicographicOrder()
        {
            var K = new ExactSlopeKernel();
            // P = (1, 5) on the vertical x = 1; Q on a line that leans by 2^-52 over 1e6: x = 1 + 3 * 2^-52 / 1e6, y = 3.
            // The same rounded x (1), so the rounded y (5 > 3) would order them the wrong way: x decides, exactly
            int vert = K.PointLine(new Vector2d(1, 0), new Vector2d(1, 1));
            int lean = K.PointLine(new Vector2d(1, 0), new Vector2d(1 + Math.Pow(2, -52), 1e6));
            int y5 = K.PointLine(new Vector2d(0, 5), new Vector2d(1, 5));
            int y3 = K.PointLine(new Vector2d(0, 3), new Vector2d(1, 3));
            int P = K.Crossing(vert, y5), Q = K.Crossing(lean, y3);
            Assert.Equal(K.Rounded(P).x, K.Rounded(Q).x);
            Assert.Equal(-1, K.CompareXY(P, Q));
            Assert.Equal(1, K.CompareXY(Q, P));
            // the same with the lines the other way round (the sign of W flips)
            int y3r = K.PointLine(new Vector2d(1, 3), new Vector2d(0, 3));
            int Qr = K.Crossing(y3r, lean);
            Assert.Equal(0, K.CompareXY(Q, Qr));
            Assert.Equal(-1, K.CompareXY(P, Qr));
            // the same exact x: y decides, exactly (Q2 = (1 + 4 * 2^-52 / 1e6, 4) against Q)
            int y4 = K.PointLine(new Vector2d(0, 4), new Vector2d(1, 4));
            int Q2 = K.Crossing(lean, y4);
            Assert.Equal(-1, K.CompareXY(Q, Q2));
            int R = K.Crossing(vert, y3);
            Assert.Equal(1, K.CompareXY(P, R));
            Assert.Equal(-1, K.CompareXY(R, Q));
            // one position, two vertices
            Assert.Equal(0, K.CompareXY(P, K.PointVertex(new Vector2d(1, 5))));
            // IsAt: exact, also where the rounded position is the point
            Assert.True(K.IsAt(P, new Vector2d(1, 5)));
            Assert.False(K.IsAt(Q, new Vector2d(1, 3)));
            Assert.Equal(new Vector2d(1, 3), K.Rounded(Q));
            Assert.True(K.IsAt(R, new Vector2d(1, 3)));
            Assert.True(K.IsAt(K.PointVertex(new Vector2d(2, 7)), new Vector2d(2, 7)));
            Assert.False(K.IsAt(K.PointVertex(new Vector2d(2, 7)), new Vector2d(2, 7.000000000000001)));
            Assert.Equal(0, K.CompareXY(P, P));
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

        // Approx is within 2e-12 (relative) of the exact point, also for nearly parallel lines, where the intervals are
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

        // ApproxHeight is within 1e-9 (relative to the magnitudes involved) of the exact height
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
            // a tight crossing is approximated from its intervals, without a construction
            var K2 = new ExactSlopeKernel();
            int t = K2.Crossing(K2.PointLine(new Vector2d(0, 0), new Vector2d(3, 1)), K2.PointLine(new Vector2d(1, -3), new Vector2d(2, 7)));
            K2.Approx(t);
            Assert.Equal(0, K2.ExactConstructions);
        }

        // the shared-apex shortcut decides without a predicate call, for a common point as P, Q or R of the planes, and
        // for plane 0
        [Fact]
        public void SharedApexShortcut_NeedsNoPredicate()
        {
            Vector3d o = new Vector3d(0, 0, 0);
            var orders = new[] {
                (new[] { o, new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) }, new[] { o, new Vector3d(1, 0, 1), new Vector3d(0, 1, 0) }),
                // (counter-clockwise, so that AddPlane keeps o as Q)
                (new[] { new Vector3d(0, 1, 0), o, new Vector3d(1, 0, 0) }, new[] { new Vector3d(0, 1, 0), o, new Vector3d(1, 0, 1) }),
                (new[] { new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), o }, new[] { new Vector3d(1, 0, 1), new Vector3d(0, 1, 0), o }),
            };
            foreach (var (pa, pb) in orders) {
                var K = new ExactSlopeKernel();
                int a = K.AddPlane(pa[0], pa[1], pa[2]), b = K.AddPlane(pb[0], pb[1], pb[2]);
                Assert.Equal(0, a);
                Assert.Equal(0, K.Side(K.PointVertex(new Vector2d(0, 0)), K.PlaneLine(a, b)));
                Assert.Equal(0, K.PredicateCalls);
            }
        }

        // the shortcut matches a defining point by both coordinates: a vertex that shares only x or only y with the Q
        // or R of both planes (with equal z there) is decided by the heights, not taken as on the line
        [Fact]
        public void SharedApexShortcut_MatchesBothCoordinates_OfQAndR()
        {
            var K = new ExactSlopeKernel();
            // Q.y = 0 and R.y = 5 in both planes, at equal z (1 and 0), but different x
            int a = K.AddPlane(new Vector3d(0, 0, 0), new Vector3d(5, 0, 1), new Vector3d(0, 5, 0));
            int b = K.AddPlane(new Vector3d(1, 1, 7), new Vector3d(6, 0, 1), new Vector3d(2, 5, 0));
            int l = K.PlaneLine(a, b);
            foreach (var p in new[] { new Vector2d(9, 0), new Vector2d(11, 5) }) {
                // the reference: the sign of the height difference in rationals
                int expect = (Height(new Vector3d(0, 0, 0), new Vector3d(5, 0, 1), new Vector3d(0, 5, 0), Q.Of(p.x), Q.Of(p.y))
                            - Height(new Vector3d(1, 1, 7), new Vector3d(6, 0, 1), new Vector3d(2, 5, 0), Q.Of(p.x), Q.Of(p.y))).Sign;
                Assert.NotEqual(0, expect);
                Assert.Equal(expect, K.Side(K.PointVertex(p), l));
            }
            // Q.x = 5 and R.x = 0 in both planes, at equal z, but different y
            int c = K.AddPlane(new Vector3d(1, 0, 0), new Vector3d(5, 1, 1), new Vector3d(0, 4, 0));
            int d = K.AddPlane(new Vector3d(2, 6, 7), new Vector3d(5, 9, 1), new Vector3d(0, 8, 0));
            int l2 = K.PlaneLine(c, d);
            foreach (var p in new[] { new Vector2d(5, 20), new Vector2d(0, 20) }) {
                int expect = (Height(new Vector3d(1, 0, 0), new Vector3d(5, 1, 1), new Vector3d(0, 4, 0), Q.Of(p.x), Q.Of(p.y))
                            - Height(new Vector3d(2, 6, 7), new Vector3d(5, 9, 1), new Vector3d(0, 8, 0), Q.Of(p.x), Q.Of(p.y))).Sign;
                Assert.NotEqual(0, expect);
                Assert.Equal(expect, K.Side(K.PointVertex(p), l2));
            }
        }

        // a nearly vertical plane given clockwise (its Nz interval contains 0, so it is oriented by the exact stage):
        // the same lines and sides as the counter-clockwise one, also where the side needs the exact stage
        [Fact]
        public void NearlyVerticalClockwisePlane_SameSidesAsCounterClockwise()
        {
            var K = new ExactSlopeKernel();
            Vector3d p = new Vector3d(0, 0, 0), q = new Vector3d(1, 1, 0), r = new Vector3d(1 + Math.Pow(2, -52), 1, 1);
            int cw = K.AddPlane(p, q, r), ccw = K.AddPlane(p, r, q), h = K.AddHorizontalPlane(0.5);
            int lcw = K.PlaneLine(cw, h), lccw = K.PlaneLine(ccw, h);
            var rnd = new Random(41);
            int nonzero = 0;
            for (int i = 0; i < 200; ++i) {
                // near the trace x = y of the nearly vertical plane, where its heights are moderate
                double x = 4 * rnd.NextDouble() - 2;
                int v = K.PointVertex(new Vector2d(x, x + (rnd.NextDouble() - 0.5) * 1e-15));
                int s1 = K.Side(v, lcw), s2 = K.Side(v, lccw);
                Assert.Equal(s2, s1);
                // the reference in rationals
                Vector2d xy = K.Rounded(v);
                Q d = Height(p, r, q, Q.Of(xy.x), Q.Of(xy.y)) - Q.Of(0.5);
                Assert.Equal(d.Sign, s1);
                if (s1 != 0) nonzero++;
            }
            // the plane is x - y = 2^-52 z, so its line at z = 0.5 is y = x - 2^-53: points one 2^-54 off it (x in [0.25, 0.5),
            // where these are doubles) need the exact stage on both lines, and must agree
            for (int i = 0; i < 50; ++i) {
                double x = 0.25 + 0.25 * rnd.NextDouble();
                foreach (double off in new[] { -Math.Pow(2, -54), 0, Math.Pow(2, -54) }) {
                    double y = x - Math.Pow(2, -53) + off;
                    int v = K.PointVertex(new Vector2d(x, y));
                    long before = K.ExactPredicates;
                    int s1 = K.Side(v, lcw), s2 = K.Side(v, lccw);
                    Assert.Equal(s2, s1);
                    Assert.Equal((Height(p, r, q, Q.Of(x), Q.Of(y)) - Q.Of(0.5)).Sign, s1);
                    Assert.Equal(before + 2, K.ExactPredicates);
                }
            }
            Assert.True(nonzero > 100, nonzero.ToString());
            Assert.True(K.ExactPredicates > 0, K.ExactPredicates.ToString());
        }

        // lines so nearly parallel that the W interval of their crossing contains 0: the exact stage decides that they
        // cross (far away), and the position is exact
        [Fact]
        public void NearlyParallelLines_CrossExactly()
        {
            var K = new ExactSlopeKernel();
            int l1 = K.PointLine(new Vector2d(0, 0), new Vector2d(1, 1));
            int l2 = K.PointLine(new Vector2d(0, 1), new Vector2d(1 + Math.Pow(2, -52), 2));
            int v = K.Crossing(l1, l2);
            Assert.True(v >= 0);
            // the crossing: y = x and y = 1 + x / (1 + 2^-52): x = 1 / (1 - 1 / (1 + 2^-52)) = 2^52 + 1
            double expect = Math.Pow(2, 52) + 1;
            Vector2d a = K.Approx(v);
            Assert.True(Math.Abs(a.x - expect) <= 1e-12 * expect, a.ToString());
            Assert.Equal(new Vector2d(expect, expect), K.Rounded(v));
            // and truly parallel ones do not cross
            Assert.Equal(-1, K.Crossing(l1, K.PointLine(new Vector2d(0, 1), new Vector2d(1, 2))));
        }
    }
}
