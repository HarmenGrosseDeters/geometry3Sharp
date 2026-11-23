using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// IntrLine3Box3, IntrRay3Box3, IntrSegment3Box3, IntrLine3AxisAlignedBox3, IntrRay3AxisAlignedBox3: the decision is the exact closed
    /// contact of the component with the box (the slabs |dot(x - Center, Axis_i)| &lt;= Extent_i, or lo &lt;= x &lt;= hi), compared with an exact
    /// rational oracle that does not use the library's own arithmetic.
    /// </summary>
    public class IntrBox3ExactTests
    {
        // ---- the oracle: fractions of BigIntegers ----
        readonly struct Q
        {
            public readonly BigInteger N, D;
            public Q(BigInteger n, BigInteger d) { if (d.Sign < 0) { n = -n; d = -d; } N = n; D = d; }
            public static Q From(double x)
            {
                if (x == 0) return new Q(0, 1);
                long bits = BitConverter.DoubleToInt64Bits(x);
                bool neg = bits < 0; int e = (int)((bits >> 52) & 0x7FF); long m = bits & 0xFFFFFFFFFFFFFL;
                if (e == 0) e = 1; else m |= 1L << 52;
                e -= 1075;
                BigInteger n = m; if (neg) n = -n;
                return e >= 0 ? new Q(n << e, 1) : new Q(n, BigInteger.One << -e);
            }
            public static Q operator +(Q a, Q b) => new Q(a.N * b.D + b.N * a.D, a.D * b.D);
            public static Q operator -(Q a, Q b) => new Q(a.N * b.D - b.N * a.D, a.D * b.D);
            public static Q operator *(Q a, Q b) => new Q(a.N * b.N, a.D * b.D);
            public static Q operator /(Q a, Q b) => new Q(a.N * b.D, a.D * b.N);
            public int Sign => N.Sign;
            public int CompareTo(Q o) => (N * o.D).CompareTo(o.N * D);
            public double ToDouble()
            {
                if (N.IsZero) return 0;
                // 70 bits of the quotient are enough for an error measure
                BigInteger a = BigInteger.Abs(N);
                int shift = 70 + (int)(BigInteger.Abs(D).GetBitLengthPortable()) - (int)a.GetBitLengthPortable();
                BigInteger q = shift >= 0 ? (a << shift) / D : a / (D << -shift);
                double r = (double)q * Math.Pow(2, -shift);
                return N.Sign < 0 ? -r : r;
            }
        }

        /// <summary> L &lt;= P + t Q &lt;= H </summary>
        readonly struct SlabQ { public readonly Q P, Qd, L, H; public SlabQ(Q p, Q q, Q l, Q h) { P = p; Qd = q; L = l; H = h; } }

        static Q Dot(Vector3d u, Vector3d v) => Q.From(u.x) * Q.From(v.x) + Q.From(u.y) * Q.From(v.y) + Q.From(u.z) * Q.From(v.z);

        static SlabQ[] Slabs(AxisAlignedBox3d b, Vector3d o, Vector3d d, double expand = 0)
        {
            var r = new SlabQ[3];
            for (int i = 0; i < 3; i++)
                r[i] = new SlabQ(Q.From(o[i]), Q.From(d[i]), Q.From(b.Min[i] - expand), Q.From(b.Max[i] + expand));
            return r;
        }

        static SlabQ[] Slabs(Box3d b, Vector3d o, Vector3d d, double expand = 0)
        {
            var r = new SlabQ[3];
            for (int i = 0; i < 3; i++) {
                Vector3d u = b.Axis(i);
                Q p = Q.From(u.x) * (Q.From(o.x) - Q.From(b.Center.x)) + Q.From(u.y) * (Q.From(o.y) - Q.From(b.Center.y)) + Q.From(u.z) * (Q.From(o.z) - Q.From(b.Center.z));
                Q e = Q.From(b.Extent[i] + expand);
                r[i] = new SlabQ(p, Dot(u, d), new Q(-e.N, e.D), e);
            }
            return r;
        }

        // the exact contact: kind 0 none, 1 point, 2 segment, with the ends as t fractions
        static (int kind, Q t0, Q t1) Contact(SlabQ[] s, Q? tmin, Q? tmax)
        {
            Q? lo = tmin, hi = tmax;
            foreach (var sl in s) {
                if (sl.Qd.Sign == 0) {
                    if (sl.P.CompareTo(sl.L) < 0 || sl.P.CompareTo(sl.H) > 0) return (0, default, default);
                    continue;
                }
                Q a = (sl.L - sl.P) / sl.Qd, b = (sl.H - sl.P) / sl.Qd;
                if (a.CompareTo(b) > 0) { var t = a; a = b; b = t; }
                if (lo == null || a.CompareTo(lo.Value) > 0) lo = a;
                if (hi == null || b.CompareTo(hi.Value) < 0) hi = b;
            }
            int c = lo.Value.CompareTo(hi.Value);
            if (c > 0) return (0, default, default);
            return (c == 0 ? 1 : 2, lo.Value, hi.Value);
        }

        static double Up(double x) => x == 0 ? double.Epsilon : BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x > 0 ? 1 : -1));
        static double Down(double x) => -Up(-x);
        static double Ulp(double x) { x = Math.Abs(x); return Math.Max(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1) - x, double.Epsilon); }

        // a point is within a few ulp of the exact o + t d (an error measured against the size of the coordinates involved)
        static void CheckPoint(Vector3d got, Vector3d o, Vector3d d, Q t, double ulps, string what)
        {
            for (int k = 0; k < 3; k++) {
                Q exact = Q.From(o[k]) + t * Q.From(d[k]);
                double ex = exact.ToDouble();
                double err = Math.Abs((Q.From(got[k]) - exact).ToDouble());
                double unit = Ulp(Math.Max(Math.Abs(ex), Math.Max(Math.Abs(o[k]), Math.Abs(got[k]))));
                Assert.True(err <= ulps * unit, $"{what}: coordinate {k} {got[k]:R} against {ex:R} ({err / unit:F2} ulp)");
            }
        }

        // ---- the inputs: lattice points (touching contacts) and random doubles ----
        static IEnumerable<(Vector3d o, Vector3d d, Vector3d lo, Vector3d hi)> Cases(int count, int seed, double scale = 1, double offset = 0)
        {
            var r = new Random(seed);
            int L(int g) => r.Next(-g, g + 1);
            for (int i = 0; i < count; i++) {
                Vector3d lo = new Vector3d(L(2) - 1, L(2) - 1, L(2) - 1), size = new Vector3d(r.Next(1, 4), r.Next(1, 4), r.Next(1, 4));
                Vector3d hi = lo + size;
                Vector3d o, q;
                int flavor = i % 4;
                if (flavor == 0) { o = new Vector3d(r.NextDouble() * 10 - 5, r.NextDouble() * 10 - 5, r.NextDouble() * 10 - 5); q = new Vector3d(r.NextDouble() * 10 - 5, r.NextDouble() * 10 - 5, r.NextDouble() * 10 - 5); }
                else {
                    o = new Vector3d(L(4), L(4), L(4)); q = new Vector3d(L(4), L(4), L(4));
                    Vector3d corner() => new Vector3d(r.Next(2) == 0 ? lo.x : hi.x, r.Next(2) == 0 ? lo.y : hi.y, r.Next(2) == 0 ? lo.z : hi.z);
                    if (flavor >= 2) {
                        int m = r.Next(4);
                        if (m == 0) q = corner(); else if (m == 1) o = corner();
                        else if (m == 2) q = new Vector3d(lo.x + size.x * (r.Next(0, 5) / 4.0), hi.y, lo.z + size.z * (r.Next(0, 5) / 4.0));
                        else { o = corner(); q = corner(); }
                    }
                    if (r.Next(3) == 0) { int ax = r.Next(3); q[ax] = Up(q[ax]); }       // one ulp off the lattice
                }
                if ((q - o).LengthSquared == 0) continue;
                Vector3d d = (q - o).Normalized;
                Vector3d sh = new Vector3d(offset, offset * 0.5, -offset * 0.25);
                yield return (o * scale + sh, d, lo * scale + sh, hi * scale + sh);
            }
        }

        static bool Normalized(Vector3d d) => d.IsNormalized;

        // ---- Line / Ray against AxisAlignedBox3d and Box3d ----
        public static IEnumerable<object[]> Frames() => new[] { new object[] { 1.0, 0.0 }, new object[] { 1.0 / 1024, 0.0 }, new object[] { 1024.0, 0.0 }, new object[] { 1.0, 1e6 }, new object[] { 1e-3, 0.0 } };

        [Theory]
        [MemberData(nameof(Frames))]
        public void LineAndRay_AxisAligned_AgreeWithTheOracle(double scale, double offset)
        {
            int kinds = 0;
            foreach (var (o, d, lo, hi) in Cases(1500, 11, scale, offset)) {
                if (Normalized(d) == false) continue;
                var box = new AxisAlignedBox3d(lo, hi);
                var line = new Line3d(o, d); var ray = new Ray3d(o, d);
                var s = Slabs(box, o, d);
                var el = Contact(s, null, null); var er = Contact(s, new Q(0, 1), null);
                var il = new IntrLine3AxisAlignedBox3(line, box); bool hl = il.Find();
                var ir = new IntrRay3AxisAlignedBox3(ray, box); bool hr = ir.Find();
                Assert.Equal(el.kind != 0, hl); Assert.Equal(er.kind != 0, hr);
                Assert.Equal(hl, il.Test()); Assert.Equal(hr, ir.Test());
                Assert.Equal(hr, IntrRay3AxisAlignedBox3.Intersects(ref ray, ref box));
                double t; Assert.Equal(hr, IntrRay3AxisAlignedBox3.FindRayIntersectT(ref ray, ref box, out t));
                if (hr) { Assert.True(Math.Abs((Q.From(t) - er.t0).ToDouble()) <= 4 * Ulp(t) || t == er.t0.ToDouble(), "entry t " + t); }
                if (hl) { Check(il.Type, il.Quantity, il.Point0, il.Point1, el, o, d, "line"); kinds |= el.kind; }
                if (hr) Check(ir.Type, ir.Quantity, ir.Point0, ir.Point1, er, o, d, "ray");
            }
            Assert.Equal(3, kinds);
        }

        static void Check(IntersectionType type, int quantity, Vector3d p0, Vector3d p1, (int kind, Q t0, Q t1) e, Vector3d o, Vector3d d, string what)
        {
            Assert.Equal(e.kind == 1 ? IntersectionType.Point : IntersectionType.Segment, type);
            Assert.Equal(e.kind == 1 ? 1 : 2, quantity);
            CheckPoint(p0, o, d, e.t0, 3, what + " p0");
            if (e.kind == 2) CheckPoint(p1, o, d, e.t1, 3, what + " p1");
        }

        // the axes of an oriented box: the identity, or permutations and sign flips (exactly orthonormal), or a rotation about z by the 3-4-5 triangle
        static Box3d Oriented(Vector3d lo, Vector3d hi, int variant)
        {
            Vector3d c = 0.5 * (lo + hi), e = 0.5 * (hi - lo);
            switch (variant % 3) {
                case 0: return new Box3d(c, e);
                case 1: return new Box3d(c, new Vector3d(0, 1, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 0, 1), new Vector3d(e.y, e.x, e.z));
                default: return new Box3d(c, new Vector3d(0.6, 0.8, 0), new Vector3d(-0.8, 0.6, 0), new Vector3d(0, 0, 1), e);
            }
        }

        [Theory]
        [MemberData(nameof(Frames))]
        public void LineAndRay_Oriented_AgreeWithTheOracle(double scale, double offset)
        {
            int n = 0;
            foreach (var (o, d, lo, hi) in Cases(1200, 12, scale, offset)) {
                if (Normalized(d) == false) continue;
                var box = Oriented(lo, hi, n++);
                var line = new Line3d(o, d); var ray = new Ray3d(o, d);
                var s = Slabs(box, o, d);
                var el = Contact(s, null, null); var er = Contact(s, new Q(0, 1), null);
                var il = new IntrLine3Box3(line, box); bool hl = il.Find();
                var ir = new IntrRay3Box3(ray, box); bool hr = ir.Find();
                Assert.Equal(el.kind != 0, hl); Assert.Equal(er.kind != 0, hr);
                Assert.Equal(hl, il.Test()); Assert.Equal(hr, ir.Test());
                Assert.Equal(hr, IntrRay3Box3.Intersects(ref ray, ref box));
                if (hl) Check(il.Type, il.Quantity, il.Point0, il.Point1, el, o, d, "line");
                if (hr) Check(ir.Type, ir.Quantity, ir.Point0, ir.Point1, er, o, d, "ray");
            }
        }

        [Fact]
        public void Segment_Oriented_AgreesWithTheOracle_OnItsRoundedEnds()
        {
            int n = 0, hits = 0;
            foreach (var (o, d, lo, hi) in Cases(2000, 13)) {
                if (Normalized(d) == false) continue;
                var seg = new Segment3d(o, o + d * 3.25);
                Vector3d p0 = seg.P0, p1 = seg.P1;
                var box = Oriented(lo, hi, n++);
                var s = Slabs(box, p0, p1 - p0);
                // (the exact difference: the slab constants use the double p1 - p0 only through Q.From of an exact subtraction, so use rationals)
                var sq = new SlabQ[3];
                for (int i = 0; i < 3; i++) {
                    Vector3d u = box.Axis(i);
                    Q dq = Q.From(u.x) * (Q.From(p1.x) - Q.From(p0.x)) + Q.From(u.y) * (Q.From(p1.y) - Q.From(p0.y)) + Q.From(u.z) * (Q.From(p1.z) - Q.From(p0.z));
                    sq[i] = new SlabQ(s[i].P, dq, s[i].L, s[i].H);
                }
                var e = Contact(sq, new Q(0, 1), new Q(1, 1));
                var it = new IntrSegment3Box3(seg, box, true);
                bool hit = it.Find();
                Assert.True(e.kind != 0 == hit, $"case {n - 1}: oracle {e.kind}, library {hit} | p0 {p0.x:R},{p0.y:R},{p0.z:R} p1 {p1.x:R},{p1.y:R},{p1.z:R} box {box.Center} {box.Extent} variant {(n - 1) % 3}");
                Assert.Equal(hit, it.Test());
                if (hit) {
                    hits++;
                    Assert.Equal(e.kind == 1 ? IntersectionType.Point : IntersectionType.Segment, it.Type);
                    Vector3d dd = p1 - p0;
                    // the point from the exact fraction (the difference p1 - p0 rounded here: allow the few ulp)
                    CheckPointOnSegment(it.Point0, p0, p1, e.t0, "p0");
                    if (e.kind == 2) CheckPointOnSegment(it.Point1, p0, p1, e.t1, "p1");
                    Assert.InRange(it.SegmentParam0, -seg.Extent, seg.Extent);
                    Assert.InRange(it.SegmentParam1, -seg.Extent, seg.Extent);
                    Assert.True(it.SegmentParam0 <= it.SegmentParam1);
                }
            }
            Assert.True(hits > 300, "hits " + hits);
        }

        static void CheckPointOnSegment(Vector3d got, Vector3d p0, Vector3d p1, Q t, string what)
        {
            for (int k = 0; k < 3; k++) {
                Q exact = Q.From(p0[k]) + t * (Q.From(p1[k]) - Q.From(p0[k]));
                double ex = exact.ToDouble();
                double err = Math.Abs((Q.From(got[k]) - exact).ToDouble());
                double unit = Ulp(Math.Max(Math.Abs(ex), Math.Max(Math.Abs(p0[k]), Math.Abs(p1[k]))));
                Assert.True(err <= 3 * unit, $"{what}: coordinate {k} {got[k]:R} against {ex:R} ({err / unit:F2} ulp)");
            }
        }

        // ---- the semantics ----
        [Fact]
        public void TouchingContacts_AreHits_WithExactPoints()
        {
            var box = new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1));
            // a ray along an edge's extension that touches only the corner (1, 1, 1)
            var ray = new Ray3d(new Vector3d(2, 2, 2), new Vector3d(-1, -1, -1).Normalized);
            var f = new IntrRay3AxisAlignedBox3(ray, box);
            Assert.True(f.Find());
            // the diagonal enters at the corner (1,1,1) and leaves at (0,0,0): a segment
            Assert.Equal(IntersectionType.Segment, f.Type);
            Assert.Equal(new Vector3d(1, 1, 1), f.Point0);
            Assert.Equal(new Vector3d(0, 0, 0), f.Point1);
            // a line that passes exactly through the edge (1,1,z): a single point? no: through the corner only
            var line = new Line3d(new Vector3d(2, 0, 1), new Vector3d(-1, 1, 0).Normalized);
            var g = new IntrLine3AxisAlignedBox3(line, box);
            Assert.True(g.Find());
            Assert.Equal(IntersectionType.Point, g.Type);
            Assert.Equal(1, g.Quantity);
            Assert.Equal(new Vector3d(1, 1, 1), g.Point0);
            // a line one ulp outside the touching position misses
            var line2 = new Line3d(new Vector3d(Up(2.0), 0, 1), new Vector3d(-1, 1, 0).Normalized);
            Assert.False(new IntrLine3AxisAlignedBox3(line2, box).Find());
            // no tolerance: 1e-9 away misses (the old routine counted it as a hit)
            var line3 = new Line3d(new Vector3d(0, 0, 1 + 1e-9), new Vector3d(1, 0, 0));
            Assert.False(new IntrLine3AxisAlignedBox3(line3, box).Find());
        }

        [Fact]
        public void EdgeContact_IsAPoint_RoundedOnce()
        {
            // a line that meets the box only at a point of the edge x = 1, y = 0 (it passes the corner of the square in x and y): the two slabs end at the same t exactly (a tie), the exact route
            // decides, and the coordinate along the edge is the correctly rounded value
            var r = new Random(23);
            var box = new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1));
            var obox = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5));
            int points = 0;
            for (int i = 0; i < 400; i++) {
                double z0 = r.NextDouble() * 0.9 + 0.05, slope = (r.NextDouble() - 0.5) * 0.2;
                Vector3d o = new Vector3d(2, 1, z0), d = new Vector3d(-1, -1, slope).Normalized;
                var f = new IntrLine3AxisAlignedBox3(new Line3d(o, d), box);
                var g = new IntrLine3Box3(new Line3d(o, d), obox);
                var e = Contact(Slabs(box, o, d), null, null);
                Assert.Equal(e.kind != 0, f.Find());
                Assert.Equal(e.kind != 0, g.Find());
                if (e.kind != 1) continue;
                points++;
                Assert.Equal(IntersectionType.Point, f.Type); Assert.Equal(IntersectionType.Point, g.Type);
                CheckPoint(f.Point0, o, d, e.t0, 0.5, "aabb"); CheckPoint(g.Point0, o, d, e.t0, 0.5, "oriented");
                Assert.Equal(1.0, f.Point0.x); Assert.Equal(0.0, f.Point0.y);
            }
            Assert.True(points > 100, "points " + points);
        }

        [Fact]
        public void ParallelRay_JustOutsideAFace_Misses_AndOnItOrInside_Hits()
        {
            var aab = new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1));
            var obox = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5));
            foreach (var dir in new[] { Vector3d.AxisZ, -Vector3d.AxisZ, Vector3d.AxisX, -Vector3d.AxisY }) {
                // the origin in the plane of the axis the ray is parallel to, at the ends of the slab
                foreach (double off in new[] { 1.0, 0.0 }) {
                    int axis = dir.x != 0 ? 1 : 0;                       // a slab the ray is parallel to
                    // the origin on the face (hit), one ulp outside (miss), 1e-12 outside (miss)
                    double[] coords = off == 1.0 ? new[] { 1.0, Up(1.0), 1.0 + 1e-12 } : new[] { 0.0, Down(0.0), -1e-12 };
                    for (int ci = 0; ci < 3; ci++) {
                        double delta = ci;
                        Vector3d o = new Vector3d(0.5, 0.5, 0.5);
                        if (dir.z != 0) o.z = dir.z > 0 ? -2 : 3;
                        else if (dir.x != 0) o.x = dir.x > 0 ? -2 : 3;
                        else o.y = dir.y > 0 ? -2 : 3;
                        o[axis] = coords[ci];
                        var ray = new Ray3d(o, dir);
                        bool expected = ci == 0;
                        Assert.Equal(expected, IntrRay3AxisAlignedBox3.Intersects(ref ray, ref aab));
                        Assert.Equal(expected, new IntrRay3AxisAlignedBox3(ray, aab).Find());
                        Assert.Equal(expected, IntrRay3Box3.Intersects(ref ray, ref obox));
                        Assert.Equal(expected, new IntrRay3Box3(ray, obox).Find());
                        Assert.Equal(expected, new IntrLine3Box3(new Line3d(o, dir), obox).Find());
                    }
                }
            }
        }

        [Fact]
        public void RayPointingAway_Misses_RayPointingToward_Hits_ForOrientedBoxes()
        {
            var obox = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            var rot = new Box3d(Vector3d.Zero, new Vector3d(0.6, 0.8, 0), new Vector3d(-0.8, 0.6, 0), new Vector3d(0, 0, 1), new Vector3d(1, 1, 1));
            foreach (var box in new[] { obox, rot }) {
                var b = box;
                var towards = new Ray3d(new Vector3d(5, 0, 0), new Vector3d(-1, 0, 0));
                var away = new Ray3d(new Vector3d(5, 0, 0), new Vector3d(1, 0, 0));
                var towardsY = new Ray3d(new Vector3d(0, -5, 0), new Vector3d(0, 1, 0));
                var awayY = new Ray3d(new Vector3d(0, -5, 0), new Vector3d(0, -1, 0));
                Assert.True(IntrRay3Box3.Intersects(ref towards, ref b)); Assert.False(IntrRay3Box3.Intersects(ref away, ref b));
                Assert.True(IntrRay3Box3.Intersects(ref towardsY, ref b)); Assert.False(IntrRay3Box3.Intersects(ref awayY, ref b));
                var behind = new Ray3d(new Vector3d(-5, 0, 0), new Vector3d(-1, 0, 0));
                Assert.False(IntrRay3Box3.Intersects(ref behind, ref b));
            }
        }

        [Fact]
        public void ExpandExtents_AtATouchingCorner_DecidesExactly()
        {
            // the ray passes the corner (1.25, 1.25) of the expanded box in x and y exactly: a tie, decided on the expanded faces
            var aab = new AxisAlignedBox3d(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            var obox = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            Vector3d dir = new Vector3d(-1, 1, 0).Normalized;
            var touching = new Ray3d(new Vector3d(2.25, 0.25, 0), dir);
            Assert.True(IntrRay3AxisAlignedBox3.Intersects(ref touching, ref aab, 0.25));
            Assert.True(IntrRay3Box3.Intersects(ref touching, ref obox, 0.25));
            var beside = new Ray3d(new Vector3d(Up(2.25), 0.25, 0), dir);
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref beside, ref aab, 0.25));
            Assert.False(IntrRay3Box3.Intersects(ref beside, ref obox, 0.25));
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref touching, ref aab, 0.25 - 1e-9));
        }

        [Fact]
        public void NearlyParallelDirections_WithHugeParameters_AreDecidedExactly()
        {
            // a direction component of 1e-300: the slab bounds in t are beyond 1e290, where the double route does not decide
            var aab = new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1));
            var obox = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5));
            Vector3d d = new Vector3d(1, 0, 1e-300);
            var inside = new Ray3d(new Vector3d(-1, 0.5, 0.5), d);     // z stays in the slab for 1e300: a hit
            var outside = new Ray3d(new Vector3d(-1, 0.5, 5), d);       // z = 5 for any t < 1e300: far beyond the box: a miss
            var rising = new Ray3d(new Vector3d(-1, 0.5, 5), new Vector3d(1, 0, -1e-300));       // reaches z = 1 only at t = 4e300
            Assert.True(new IntrRay3AxisAlignedBox3(inside, aab).Find());
            Assert.True(new IntrRay3Box3(inside, obox).Find());
            Assert.False(new IntrRay3AxisAlignedBox3(outside, aab).Find());
            Assert.False(new IntrRay3Box3(outside, obox).Find());
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref outside, ref aab));
            Assert.False(IntrRay3Box3.Intersects(ref outside, ref obox));
            // the slab of z is reached at t = 4e300 and left at 5e300, while x leaves at t = 2: no contact
            Assert.False(new IntrRay3AxisAlignedBox3(rising, aab).Find());
            Assert.False(IntrRay3Box3.Intersects(ref rising, ref obox));
        }

        [Fact]
        public void HugeExpansion_StillSeparatesAnOriginBeyondTheSlab()
        {
            // an extent expanded by 1e308 (DCurve3BoxTree starts its search with a distance of double.MaxValue): the bounds in t are beyond
            // reach, and an origin beyond the slab that moves away from it must still miss
            var obox = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5));
            var away = new Ray3d(new Vector3d(0, 0, 1.5e308), Vector3d.AxisZ);
            var toward = new Ray3d(new Vector3d(0, 0, 1.5e308), -Vector3d.AxisZ);
            var inside = new Ray3d(new Vector3d(0, 0, 0), Vector3d.AxisZ);
            Assert.False(IntrRay3Box3.Intersects(ref away, ref obox, 1e308));
            Assert.True(IntrRay3Box3.Intersects(ref toward, ref obox, 1e308));
            Assert.True(IntrRay3Box3.Intersects(ref inside, ref obox, 1e308));
            Assert.True(IntrRay3Box3.Intersects(ref inside, ref obox, double.MaxValue));
            // a line whose origin is beyond a slab of half-width 1e308 (its t interval there is about 1e308, the other slabs end near t = 1)
            var tall = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.5, 1e308));
            var line = new Line3d(new Vector3d(-1, 0, 1.5e308), new Vector3d(0.6, 0, 0.8));
            Assert.False(new IntrLine3Box3(line, tall).Find());
            var tallAabb = new AxisAlignedBox3d(new Vector3d(-0.5, -0.5, -1e308), new Vector3d(0.5, 0.5, 1e308));
            Assert.False(new IntrLine3AxisAlignedBox3(line, tallAabb).Find());
            var aab = new AxisAlignedBox3d(new Vector3d(-0.5, -0.5, -0.5), new Vector3d(0.5, 0.5, 0.5));
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref away, ref aab, 1e308));
            Assert.True(IntrRay3AxisAlignedBox3.Intersects(ref toward, ref aab, 1e308));
        }

        [Fact]
        public void SegmentLongerThanTheRange_IsDecidedExactly()
        {
            // the span p1 - p0 overflows (3.4e308: not reachable through Segment3d, whose center, direction and extent overflow first, but the
            // shared decision takes two points): the filter's direction would be wrong; the exact route decides
            var box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            var spec = IntrExactBox3.BoxSpec.Of(ref box, 0);
            Vector3d p0 = new Vector3d(-1.7e308, 0, 0), p1 = new Vector3d(1.7e308, 0, 0);
            IntrExactBox3.Hit hit;
            Assert.True(IntrExactBox3.Find(ref spec, ref p0, ref p0, ref p1, true, 0, true, 1, true, out hit));
            Assert.Equal(2, hit.Kind);
            Assert.Equal(-1.0, hit.P0.x); Assert.Equal(1.0, hit.P1.x);
            Vector3d q0 = new Vector3d(-1.7e308, 2, 0), q1 = new Vector3d(1.7e308, 2, 0);
            Assert.True(IntrExactBox3.Find(ref spec, ref q0, ref q0, ref q1, true, 0, true, 1, true, out hit));
            Assert.Equal(0, hit.Kind);
            var aab = new AxisAlignedBox3d(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            var aspec = IntrExactBox3.BoxSpec.Of(ref aab, 0);
            Assert.True(IntrExactBox3.Find(ref aspec, ref p0, ref p0, ref p1, true, 0, true, 1, true, out hit));
            Assert.Equal(2, hit.Kind);
        }

        [Fact]
        public void FaceSnap_UsesTheEndsOfTheSegment_NotARoundedDifference()
        {
            // x spans more than the range (the difference overflows, only its halves are used), y runs over two subnormal values whose
            // halves round to the same number (3 and 4 times 2^-1074 give 2 times 2^-1074 both): the sign of the halved difference is 0, and
            // the point where the segment reaches the lower face of y at its end must still lie on that face
            double tiny = double.Epsilon;
            var aab = new AxisAlignedBox3d(new Vector3d(-1.7e308, 4 * tiny, -1), new Vector3d(1.7e308, 5 * tiny, 1));
            var aspec = IntrExactBox3.BoxSpec.Of(ref aab, 0);
            Vector3d p0 = new Vector3d(-1.7e308, 3 * tiny, 0), p1 = new Vector3d(1.7e308, 4 * tiny, 0);
            IntrExactBox3.Hit hit;
            Assert.True(IntrExactBox3.Find(ref aspec, ref p0, ref p0, ref p1, true, 0, true, 1, true, out hit));
            Assert.NotEqual(0, hit.Kind);
            Assert.Equal(4 * tiny, hit.P0.y);
        }

        [Fact]
        public void AxisParallelSegments_AxisAlignedAndOrientedBoxAgree()
        {
            // a segment parallel to an axis has d1[i] == d0[i] in two slabs: exact zero, no error bound, decided by comparisons; the answer is
            // the one of the same box as an oriented one (exact dot products)
            int hits = 0, n = 0;
            foreach (var (o, d, lo, hi) in Cases(800, 41)) {
                for (int ax = 0; ax < 3; ax++) {
                    Vector3d p0 = o, p1 = o; p1[ax] = o[ax] + 3.5 * (ax == 1 ? -1 : 1);
                    var aab = new AxisAlignedBox3d(lo, hi);
                    var ob = new Box3d(0.5 * (lo + hi), 0.5 * (hi - lo));
                    var sa = IntrExactBox3.BoxSpec.Of(ref aab, 0); var so = IntrExactBox3.BoxSpec.Of(ref ob, 0);
                    IntrExactBox3.Hit ha, ho;
                    Assert.True(IntrExactBox3.Find(ref sa, ref p0, ref p0, ref p1, true, 0, true, 1, true, out ha));
                    Assert.True(IntrExactBox3.Find(ref so, ref p0, ref p0, ref p1, true, 0, true, 1, true, out ho));
                    Assert.Equal(ho.Kind, ha.Kind);
                    if (ha.Kind != 0) { hits++; Assert.Equal(ho.T0, ha.T0); Assert.Equal(ho.T1, ha.T1); }
                    n++;
                }
            }
            Assert.True(hits > 100 && n > 1000);
        }

        [Fact]
        public void DoClipping_OnAMiss_LeavesTheParametersAsGiven()
        {
            var box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            double t0 = -5, t1 = 7; int quantity = 9; Vector3d p0 = new Vector3d(4, 5, 6), p1 = p0; var type = IntersectionType.Segment;
            Assert.False(IntrLine3Box3.DoClipping(ref t0, ref t1, new Vector3d(0, 3, 0), Vector3d.AxisX, box, true, ref quantity, ref p0, ref p1, ref type));
            Assert.Equal(-5.0, t0); Assert.Equal(7.0, t1); Assert.Equal(0, quantity); Assert.Equal(IntersectionType.Empty, type);
            var seg = new IntrSegment3Box3(new Segment3d(new Vector3d(-3, 3, 0), new Vector3d(3, 3, 0)), box, true);
            Assert.False(seg.Find());
            Assert.Equal(-seg.Segment.Extent, seg.SegmentParam0); Assert.Equal(seg.Segment.Extent, seg.SegmentParam1);
        }

        [Fact]
        public void OverflowingNumerator_FaceMinusOrigin_IsAMiss_NotAnInfiniteBound()
        {
            // L - P overflows to an infinity while L, P and Q are finite: the slab bound is a finite t (here 2.7e18, below the 5e18 where the
            // other slab starts), not an infinite one; certifying the infinity gave a hit with t = 5e18 and points at x = -Infinity (review of #1432)
            var box = new AxisAlignedBox3d(new Vector3d(-1.7e308, 5e18, -1), new Vector3d(1.7e308, 1e19, 1));
            var ray = new Ray3d(new Vector3d(1e308, 0, 0), new Vector3d(-1e290, 1, 0), true);
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref box));
            double t; Assert.False(IntrRay3AxisAlignedBox3.FindRayIntersectT(ref ray, ref box, out t));
            Assert.False(new IntrRay3AxisAlignedBox3(ray, box).Find());
            Assert.False(new IntrRay3AxisAlignedBox3(ray, box).Test());
            var line = new Line3d(ray.Origin, ray.Direction);
            Assert.False(new IntrLine3AxisAlignedBox3(line, box).Find());
            Assert.False(new IntrLine3AxisAlignedBox3(line, box).Test());
            double t0 = 0, t1 = double.MaxValue; int quantity = 0; Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero; var type = IntersectionType.Empty;
            Assert.False(IntrLine3AxisAlignedBox3.DoClipping(ref t0, ref t1, ref ray.Origin, ref ray.Direction, ref box, true, ref quantity, ref p0, ref p1, ref type));
            // the conservative form may say "may hit" but never culls an exact hit: the box moved so that the ray does hit
            var hitBox = new AxisAlignedBox3d(new Vector3d(-1.7e308, 1e18, -1), new Vector3d(1.7e308, 2e18, 1));
            Assert.True(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref hitBox));
            double m; Assert.True(IntrExactBox3.RayAabbMay(ref ray.Origin, ref ray.Direction, ref hitBox, 0, out m));
        }

        [Fact]
        public void ExtremeValues_AxisAlignedAndOriented_AgreeWithTheOracle_ForEveryEntry()
        {
            // large extents, origins near the range limits and unnormalized directions: the differences face - origin overflow while the
            // inputs and the exact answer are finite; every entry against the exact rational oracle
            double[] c = { 1.7e308, -1.7e308, 1e308, -1e308, 5e18, -5e18, 1e19, 1e154, -1e154, 3, -3, 1, -1, 0, 0.5 };
            double[] dd = { 1e290, -1e290, 1e300, -1e300, 1e200, 1, -1, 0.5, 0, 1e-200, 3 };
            var r = new Random(97);
            int hits = 0, misses = 0, n = 0;
            for (int it = 0; it < 25000; it++) {
                Vector3d lo = new Vector3d(c[r.Next(c.Length)], c[r.Next(c.Length)], c[r.Next(c.Length)]), hi = new Vector3d(c[r.Next(c.Length)], c[r.Next(c.Length)], c[r.Next(c.Length)]);
                for (int k = 0; k < 3; k++) if (lo[k] > hi[k]) { double tmp = lo[k]; lo[k] = hi[k]; hi[k] = tmp; }
                Vector3d o = new Vector3d(c[r.Next(c.Length)], c[r.Next(c.Length)], c[r.Next(c.Length)]), d = new Vector3d(dd[r.Next(dd.Length)], dd[r.Next(dd.Length)], dd[r.Next(dd.Length)]);
                if (r.Next(2) == 0) d = -d;
                if (d.x == 0 && d.y == 0 && d.z == 0) continue;
                var aab = new AxisAlignedBox3d(lo, hi);
                Vector3d ctr = 0.5 * lo + 0.5 * hi, ext = 0.5 * hi - 0.5 * lo;
                var ob = new Box3d(ctr, ext);       // (the half sums may round: the oracle uses the box as given)
                bool er = false;
                // the direction as given (Test, Intersects, FindRayIntersectT and the conservative form do not need it normalized: Find does)
                Vector3d dn = d.Normalized;
                for (int pass = 0; pass < 2; pass++) {
                    Vector3d dir = pass == 0 ? d : dn;
                    if (dir.x == 0 && dir.y == 0 && dir.z == 0) continue;      // (a direction that normalizes to nothing)
                    bool finds = dir.IsNormalized;
                    var ray = new Ray3d(o, dir, true); var line = new Line3d(o, dir);
                    var s = Slabs(aab, o, dir);
                    bool xr = Contact(s, new Q(0, 1), null).kind != 0, xl = Contact(s, null, null).kind != 0;
                    if (pass == 0) er = xr;
                    Assert.Equal(xr, IntrRay3AxisAlignedBox3.Intersects(ref ray, ref aab));
                    Assert.Equal(xr, new IntrRay3AxisAlignedBox3(ray, aab).Test());
                    double t; Assert.Equal(xr, IntrRay3AxisAlignedBox3.FindRayIntersectT(ref ray, ref aab, out t));
                    Assert.Equal(xl, new IntrLine3AxisAlignedBox3(line, aab).Test());
                    if (finds) { Assert.Equal(xr, new IntrRay3AxisAlignedBox3(ray, aab).Find()); Assert.Equal(xl, new IntrLine3AxisAlignedBox3(line, aab).Find()); }
                    double m; if (xr) Assert.True(IntrExactBox3.RayAabbMay(ref ray.Origin, ref ray.Direction, ref aab, 0, out m), "the conservative form culled an exact hit");
                    var so = Slabs(ob, o, dir);
                    bool eor = Contact(so, new Q(0, 1), null).kind != 0, eol = Contact(so, null, null).kind != 0;
                    Assert.Equal(eor, IntrRay3Box3.Intersects(ref ray, ref ob));
                    Assert.Equal(eor, new IntrRay3Box3(ray, ob).Test());
                    Assert.Equal(eol, new IntrLine3Box3(line, ob).Test());
                    if (finds) { Assert.Equal(eor, new IntrRay3Box3(ray, ob).Find()); Assert.Equal(eol, new IntrLine3Box3(line, ob).Find()); }
                }
                if (er) hits++; else misses++;
                n++;
            }
            Assert.True(hits > 200 && misses > 200 && n > 8000, $"hits {hits} misses {misses} n {n}");
        }

        [Fact]
        public void DoClipping_WithExtremeCallerLimits_AgreesWithTheOracle()
        {
            // the t limits are the caller's: beyond 1e290 they are no infinite bound of the filter but numbers (review of #1432: the box below with
            // t in [-1e300, -1e298] was a Segment, and with [-1e300, 1e300] t0 was -1e300, a point outside the box)
            double[] lim = { -1e300, -1e298, -1e295, -1e291, -1e289, -1e100, -5, 0, 2, 1e100, 1e289, 1e291, 1e295, 1e298, 1e300, -double.MaxValue, double.MaxValue };
            double[] c = { 1, -1, 0.5, 0, 1e295, -1e295, 1e290, -1e290, 3 };
            var r = new Random(5);
            int hits = 0, n = 0;
            for (int it = 0; it < 6000; it++) {
                Vector3d lo = new Vector3d(-c[r.Next(c.Length)] - 1, -c[r.Next(c.Length)] - 1, -c[r.Next(c.Length)] - 1), hi = new Vector3d(c[r.Next(c.Length)] + 1, c[r.Next(c.Length)] + 1, c[r.Next(c.Length)] + 1);
                for (int k = 0; k < 3; k++) if (lo[k] > hi[k]) { double tmp = lo[k]; lo[k] = hi[k]; hi[k] = tmp; }
                Vector3d o = new Vector3d(c[r.Next(c.Length)] * (r.Next(2) * 2 - 1), 0, c[r.Next(c.Length)] * (r.Next(2) * 2 - 1) * 0.5);
                Vector3d d = r.Next(3) == 0 ? new Vector3d(1, 0, 0) : new Vector3d(r.Next(-2, 3), r.Next(-2, 3), r.Next(-2, 3)).Normalized;
                if (d.x == 0 && d.y == 0 && d.z == 0) continue;
                double a = lim[r.Next(lim.Length)], b = lim[r.Next(lim.Length)];
                double t0 = Math.Min(a, b), t1 = Math.Max(a, b);
                bool hasMin = t0 > -double.MaxValue, hasMax = t1 < double.MaxValue;
                var aab = new AxisAlignedBox3d(lo, hi); var ob = new Box3d(0.5 * lo + 0.5 * hi, 0.5 * hi - 0.5 * lo);
                for (int kind = 0; kind < 2; kind++) {
                    var s = kind == 0 ? Slabs(aab, o, d) : Slabs(ob, o, d);
                    var e = Contact(s, hasMin ? Q.From(t0) : (Q?)null, hasMax ? Q.From(t1) : (Q?)null);
                    double g0 = t0, g1 = t1; int quantity = 0; Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero; var type = IntersectionType.Empty;
                    bool got = kind == 0
                        ? IntrLine3AxisAlignedBox3.DoClipping(ref g0, ref g1, ref o, ref d, ref aab, true, ref quantity, ref p0, ref p1, ref type)
                        : IntrLine3Box3.DoClipping(ref g0, ref g1, o, d, ob, true, ref quantity, ref p0, ref p1, ref type);
                    Assert.Equal(e.kind != 0, got);
                    if (got) {
                        hits++;
                        // (the route is not visible here: rounded once from the exact contact, or within the certified bound of the double filter; both in rationals)
                        Assert.True((CorrectlyRounded(g0, e.t0) || WithinFilterBound(g0, e.t0)) && (CorrectlyRounded(g1, e.t1) || WithinFilterBound(g1, e.t1)),
                            $"t {g0:R} {g1:R} against {e.t0.ToDouble():R} {e.t1.ToDouble():R}, limits {t0:R} {t1:R}");
                        Assert.True(InBoxUpToRounding(ob, p0) && (quantity == 1 || InBoxUpToRounding(ob, p1)), "a point of the contact outside the box");
                        Assert.Equal(e.kind == 1 ? IntersectionType.Point : IntersectionType.Segment, type);
                    }
                    n++;
                }
            }
            Assert.True(hits > 300 && n > 10000, $"hits {hits} n {n}");
        }

        [Fact]
        public void CancellingSlab_BoundBeyondHugeWithAnErrorThatReachesInside_IsNotDroppedAsInfinite()
        {
            // oriented slab: L - P cancels, so the computed bound lies beyond 1e290 while its exact value is about 2.6e289 and binds (review of #1432)
            var box = new Box3d(Vector3d.Zero, new Vector3d(0.6, 0.8, 0), new Vector3d(-0.8, 0.6, 0), new Vector3d(0, 0, 1),
                new Vector3d(1.2628850212607981E+303, 7.505022788804454E+306, 7.884470018569269E+284));
            Vector3d o = new Vector3d(4.0034365517084655E+306, -3.000998807504773E+306, -1.3416535123677303E+286);
            Vector3d d = new Vector3d(-0.14142135447902154, 0.9899494813531506, 0.00015768940037138535);
            var s = Slabs(box, o, d);
            Assert.Equal(0, Contact(s, null, null).kind);
            Assert.Equal(0, Contact(s, new Q(0, 1), null).kind);
            var ray = new Ray3d(o, d, true);
            Assert.False(new IntrLine3Box3(new Line3d(o, d), box).Test());
            Assert.False(IntrRay3Box3.Intersects(ref ray, ref box));
            Assert.False(new IntrRay3Box3(ray, box).Test());
            Assert.False(new IntrLine3Box3(new Line3d(o, d), box).Find());
            // the limits case
            var box2 = new Box3d(Vector3d.Zero, new Vector3d(0.6, 0.8, 0), new Vector3d(-0.8, 0.6, 0), new Vector3d(0, 0, 1),
                new Vector3d(2.0586688118345252E+102, 1.0007421359214291E+107, 1));
            Vector3d o2 = new Vector3d(4.002845023557006E+106, -3.0023911012692337E+106, 0), d2 = new Vector3d(-3.40707396967666E-201, 2.3849517787736615E-200, 0);
            double t0 = -8.806194077388475E+289, t1 = -8.545073569773265E+289;
            var s2 = Slabs(box2, o2, d2);
            Assert.Equal(0, Contact(s2, Q.From(t0), Q.From(t1)).kind);
            int quantity = 0; Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero; var type = IntersectionType.Empty;
            Assert.False(IntrLine3Box3.DoClipping(ref t0, ref t1, o2, d2, box2, true, ref quantity, ref p0, ref p1, ref type));
        }

        // g is the double nearest to the rational e (a tie either way); beyond the range an infinity
        static bool CorrectlyRounded(double g, Q e)
        {
            Q maxHalf = Q.From(double.MaxValue) + new Q(BigInteger.One << 970, 1);
            if (double.IsInfinity(g))
                return g > 0 ? e.CompareTo(maxHalf) >= 0 : e.CompareTo(new Q(-maxHalf.N, maxHalf.D)) <= 0;
            Q half = new Q(1, 2);
            Q qg = Q.From(g);
            double dn = Down(g), up = Up(g);
            Q lo = double.IsInfinity(dn) ? new Q(-maxHalf.N, maxHalf.D) : (qg + Q.From(dn)) * half;
            Q hi = double.IsInfinity(up) ? maxHalf : (qg + Q.From(up)) * half;
            return e.CompareTo(lo) >= 0 && e.CompareTo(hi) <= 0;
        }

        // |g - e| <= 6 u |e| + 1e-300, in rationals
        static bool WithinFilterBound(double g, Q e)
        {
            if (double.IsInfinity(g)) return false;
            Q diff = Q.From(g) - e; if (diff.Sign < 0) diff = new Q(-diff.N, diff.D);
            Q ae = e; if (ae.Sign < 0) ae = new Q(-ae.N, ae.D);
            Q bound = ae * Q.From(6 * 1.1102230246251565e-16) + Q.From(1e-300);
            return diff.CompareTo(bound) <= 0;
        }

        // the point lies in the oriented box up to the rounding of its coordinates (a few u of the sums that make the slab values)
        static bool InBoxUpToRounding(Box3d b, Vector3d p)
        {
            for (int i = 0; i < 3; i++) {
                Vector3d u = b.Axis(i);
                Q dot = Dot(u, p - b.Center);
                double scale = Math.Abs(u.x) * (Math.Abs(p.x) + Math.Abs(b.Center.x)) + Math.Abs(u.y) * (Math.Abs(p.y) + Math.Abs(b.Center.y)) + Math.Abs(u.z) * (Math.Abs(p.z) + Math.Abs(b.Center.z)) + b.Extent[i];       // (each coordinate of the point is rounded relative to itself, not to its distance from the center)
                Q lim = Q.From(b.Extent[i]) + Q.From(64 * 1.1102230246251565e-16 * scale + 1e-300);
                Q ad = dot; if (ad.Sign < 0) ad = new Q(-ad.N, ad.D);
                if (ad.CompareTo(lim) > 0) return false;
            }
            return true;
        }

        [Fact]
        public void OrientedBoxes_CancellingSlabs_BoundsNearHuge_AgreeWithTheOracle_ForEveryEntry()
        {
            // the standing property test of the infinite-bound rule (docs/design/BOX3_EXACT_FILTER.md "The infinite bounds"): oriented boxes whose
            // slab value P cancels against the face (the origin placed so that the bound face - P over Q is about 1e285 to 1e292), extreme
            // scales and limits of the caller within +-1e291; every entry against the exact rational oracle
            var r = new Random(20261123);
            Func<double, double> pow10 = x => Math.Pow(10, x);
            int hits = 0, misses = 0, exactEnds = 0, filterEnds = 0, n = 0;
            for (int it = 0; it < 9000; it++) {
                double ang = r.NextDouble() * 6.283185307179586;
                Vector3d ux, uy, uz = Vector3d.AxisZ;
                if (r.Next(3) == 0) { ux = new Vector3d(0.6, 0.8, 0); uy = new Vector3d(-0.8, 0.6, 0); }
                else if (r.Next(2) == 0) { ux = Vector3d.AxisX; uy = Vector3d.AxisY; }
                else { ux = new Vector3d(Math.Cos(ang), Math.Sin(ang), 0); uy = new Vector3d(-Math.Sin(ang), Math.Cos(ang), 0); }
                double scale = pow10(r.Next(-5, 307) + r.NextDouble());
                Vector3d ext = new Vector3d(scale * pow10(-6 * r.NextDouble()), scale * pow10(-6 * r.NextDouble()), scale * pow10(-12 * r.NextDouble()));
                Vector3d ctr = r.Next(2) == 0 ? Vector3d.Zero : new Vector3d((r.NextDouble() - 0.5) * scale, (r.NextDouble() - 0.5) * scale, 0);
                Box3d box = new Box3d(ctr, ux, uy, uz, ext);
                // the direction: random, some components tiny
                Vector3d d = new Vector3d((r.NextDouble() - 0.5) * pow10(-r.Next(0, 17)), (r.NextDouble() - 0.5) * pow10(-r.Next(0, 17)), (r.NextDouble() - 0.5) * pow10(-r.Next(0, 17)));
                d = d.Normalized; if (d.IsNormalized == false || (d.x == 0 && d.y == 0 && d.z == 0)) continue;
                // the origin: on the slab k at the distance B * Q_k from the face (the bound about B), plus a random part along the others
                int k = r.Next(2);
                Vector3d uk = k == 0 ? ux : uy, uo = k == 0 ? uy : ux;
                double qk = uk.Dot(d);
                if (qk == 0) continue;
                double B = (r.Next(2) == 0 ? 1 : -1) * pow10(285 + 7 * r.NextDouble());
                double Pk = (r.Next(2) == 0 ? ext[k] : -ext[k]) - B * qk;
                if (double.IsInfinity(Pk) || Math.Abs(Pk) > 1.7e306) Pk = Math.Sign(Pk) * ext[k] * (1 + pow10(-r.Next(0, 17)));
                double Po = (r.NextDouble() * 2 - 1) * ext[1 - k] * (r.Next(3) == 0 ? 3 : 1);
                Vector3d o = ctr + uk * Pk + uo * Po + uz * ((r.NextDouble() * 2 - 1) * ext.z);
                if (double.IsInfinity(o.x) || double.IsInfinity(o.y) || double.IsInfinity(o.z)) continue;
                if (r.Next(2) == 0) {
                    // aimed at a point of the box (a contact, with the face bounds still far)
                    Vector3d aim = ctr + ux * ((r.NextDouble() * 2 - 1) * ext.x) + uy * ((r.NextDouble() * 2 - 1) * ext.y) + uz * ((r.NextDouble() * 2 - 1) * ext.z);
                    Vector3d dd2 = (aim - o).Normalized;
                    if (dd2.IsNormalized && (dd2.x != 0 || dd2.y != 0 || dd2.z != 0)) d = dd2;
                }
                bool limits = r.Next(3) == 0;
                double t0 = limits ? -pow10(r.Next(0, 291)) * r.NextDouble() : -double.MaxValue, t1 = limits ? pow10(r.Next(0, 291)) * r.NextDouble() : double.MaxValue;
                if (limits && r.Next(2) == 0) { t0 = -pow10(r.Next(285, 291)) * (0.5 + r.NextDouble()); t1 = t0 * 0.5; if (t0 > t1) { double tmp = t0; t0 = t1; t1 = tmp; } }
                bool hasMin = t0 > -double.MaxValue, hasMax = t1 < double.MaxValue;
                var slabs = Slabs(box, o, d);
                var e = Contact(slabs, hasMin ? Q.From(t0) : (Q?)null, hasMax ? Q.From(t1) : (Q?)null);
                // internal Find: the kind, the ends by the route, the points
                var spec = IntrExactBox3.BoxSpec.Of(ref box, 0);
                Vector3d zero = Vector3d.Zero; IntrExactBox3.Hit hit;
                Assert.True(IntrExactBox3.Find(ref spec, ref o, ref zero, ref d, hasMin, t0, hasMax, t1, true, out hit));
                Assert.True(hit.Kind == (e.kind == 0 ? 0 : (e.kind == 1 ? 1 : 2)) || (hit.Kind == 1 && e.kind == 2),
                    $"kind {hit.Kind} against {e.kind}: box {box.Extent.x:R} {box.Extent.y:R} {box.Extent.z:R} o {o.x:R} {o.y:R} {o.z:R} d {d.x:R} {d.y:R} {d.z:R} t [{t0:R}, {t1:R}]");
                if (e.kind != 0) {
                    hits++;
                    if (hit.Exact) { exactEnds++; Assert.True(CorrectlyRounded(hit.T0, e.t0) && CorrectlyRounded(hit.T1, e.t1), $"exact ends {hit.T0:R} {hit.T1:R} against {e.t0.ToDouble():R} {e.t1.ToDouble():R}"); }
                    else { filterEnds++; Assert.True(WithinFilterBound(hit.T0, e.t0) && WithinFilterBound(hit.T1, e.t1), $"filter ends {hit.T0:R} {hit.T1:R} against {e.t0.ToDouble():R} {e.t1.ToDouble():R}"); }
                    if (double.IsInfinity(hit.P0.x) == false && double.IsInfinity(hit.P0.y) == false && double.IsInfinity(hit.P0.z) == false)
                        Assert.True(InBoxUpToRounding(box, hit.P0), "P0 outside the box");
                    if (hit.Kind == 2 && double.IsInfinity(hit.P1.x) == false && double.IsInfinity(hit.P1.y) == false && double.IsInfinity(hit.P1.z) == false)
                        Assert.True(InBoxUpToRounding(box, hit.P1), $"P1 outside the box: P1 {hit.P1.x:R} {hit.P1.y:R} {hit.P1.z:R} T {hit.T0:R} {hit.T1:R} exact {hit.Exact} box c {ctr.x:R} {ctr.y:R} ext {ext.x:R} {ext.y:R} {ext.z:R} ux {ux.x:R} {ux.y:R} o {o.x:R} {o.y:R} {o.z:R} d {d.x:R} {d.y:R} {d.z:R} t [{t0:R}, {t1:R}]");
                } else misses++;
                // the public entries
                bool el = Contact(slabs, null, null).kind != 0, er = Contact(slabs, new Q(0, 1), null).kind != 0;
                var ray = new Ray3d(o, d, true); var line = new Line3d(o, d);
                Assert.Equal(er, IntrRay3Box3.Intersects(ref ray, ref box));
                Assert.Equal(er, new IntrRay3Box3(ray, box).Test());
                Assert.Equal(er, new IntrRay3Box3(ray, box).Find());
                Assert.Equal(el, new IntrLine3Box3(line, box).Test());
                Assert.Equal(el, new IntrLine3Box3(line, box).Find());
                double g0 = t0, g1 = t1; int quantity = 0; Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero; var type = IntersectionType.Empty;
                Assert.Equal(e.kind != 0, IntrLine3Box3.DoClipping(ref g0, ref g1, o, d, box, true, ref quantity, ref p0, ref p1, ref type));
                n++;
            }
            Assert.True(n > 4000 && hits > 200 && misses > 200 && exactEnds > 50 && filterEnds > 20, $"n {n} hits {hits} misses {misses} exact {exactEnds} filter {filterEnds}");
        }

        [Fact]
        public void ContactBeyondTheDoubleRange_HasInfiniteT_AndFinitePointsInTheBox()
        {
            // documented: a t beyond +-double.MaxValue is +-Infinity (it is the result, not a limit: the limits +-MaxValue mean "none")
            var box = new AxisAlignedBox3d(new Vector3d(1.7e308, -1, -1), new Vector3d(1.79e308, 1, 1));
            Vector3d o = new Vector3d(-1.7e308, 0, 0), d = new Vector3d(1, 0, 0);
            double t0 = -double.MaxValue, t1 = double.MaxValue; int quantity = 0; Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero; var type = IntersectionType.Empty;
            Assert.True(IntrLine3AxisAlignedBox3.DoClipping(ref t0, ref t1, ref o, ref d, ref box, true, ref quantity, ref p0, ref p1, ref type));
            Assert.Equal(IntersectionType.Segment, type);
            Assert.True(double.IsPositiveInfinity(t0) && double.IsPositiveInfinity(t1));
            Assert.Equal(1.7e308, p0.x); Assert.Equal(1.79e308, p1.x);
            Assert.True(p0.y == 0 && p0.z == 0 && p1.y == 0 && p1.z == 0);
        }

        [Fact]
        public void ZeroDirection_IsNotARay_TestAndFindAgree()
        {
            var aab = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            var obox = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5));
            var zero = new Ray3d(new Vector3d(0.5, 0.5, 0.5), Vector3d.Zero);
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref zero, ref aab));
            Assert.False(IntrRay3Box3.Intersects(ref zero, ref obox));
            double t; Assert.False(IntrRay3AxisAlignedBox3.FindRayIntersectT(ref zero, ref aab, out t));
            Assert.False(new IntrRay3AxisAlignedBox3(zero, aab).Test());
            Assert.False(new IntrRay3Box3(zero, obox).Test());
            Assert.False(new IntrRay3AxisAlignedBox3(zero, aab).Find());
        }

        [Fact]
        public void FacesThatOverflowWhenExpanded_AreAnInvalidQuery()
        {
            var aab = new AxisAlignedBox3d(new Vector3d(-1.7e308, 0, 0), new Vector3d(1.7e308, 1, 1));
            var ray = new Ray3d(new Vector3d(0, 0.5, 0.5), Vector3d.AxisX);
            Assert.True(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref aab));
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref aab, 1.7e308));
        }

        [Theory]
        [MemberData(nameof(Frames))]
        public void ConservativeRayForm_NeverMissesABoxThatTheExactTestHits(double scale, double offset)
        {
            int hits = 0, culled = 0, extra = 0;
            foreach (var (o, d, lo, hi) in Cases(2500, 19, scale, offset)) {
                if (Normalized(d) == false) continue;
                var box = new AxisAlignedBox3d(lo, hi);
                var ray = new Ray3d(o, d);
                var e = Contact(Slabs(box, o, d), new Q(0, 1), null);
                double t;
                bool may = IntrExactBox3.RayAabbMay(ref ray.Origin, ref ray.Direction, ref box, 0, out t);
                if (e.kind != 0) {
                    hits++;
                    Assert.True(may, "an exact hit was culled");
                    Assert.True(t >= 0 && t <= e.t0.ToDouble(), $"entry {t:R} is not a lower bound of {e.t0.ToDouble():R}");
                } else if (may) extra++;
                else culled++;
                // the expanded box too
                double ex = 0.125 * scale;
                var s2 = Slabs(box, o, d, ex);
                var e2 = Contact(s2, new Q(0, 1), null);
                bool may2 = IntrExactBox3.RayAabbMay(ref ray.Origin, ref ray.Direction, ref box, ex, out t);
                if (e2.kind != 0) Assert.True(may2, "an exact hit of the expanded box was culled");
            }
            Assert.True(hits > 200 && culled > 200, $"hits {hits}, culled {culled}, may-hit without an exact hit {extra}");
            // the touching and near-touching rays are answered "may hit": a ray through the corner, and one ulp beside it
            var b = new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1));
            var corner = new Ray3d(new Vector3d(2, 2, 2), new Vector3d(-1, -1, -1).Normalized);
            double tt;
            Assert.True(IntrExactBox3.RayAabbMay(ref corner.Origin, ref corner.Direction, ref b, 0, out tt));
        }

        [Fact]
        public void ZeroLengthSegment_IsAPoint()
        {
            var box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            var seg = new Segment3d(Vector3d.Zero, Vector3d.AxisX, 0);
            var f = new IntrSegment3Box3(seg, box, true);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Point, f.Type);
            Assert.Equal(1, f.Quantity);
            var out1 = new Segment3d(new Vector3d(2, 0, 0), Vector3d.AxisX, 0);
            Assert.False(new IntrSegment3Box3(out1, box, true).Find());
        }

        [Fact]
        public void NonSolidSegment_NeedsTheSurface()
        {
            var box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            var inside = new Segment3d(new Vector3d(-0.5, 0, 0), new Vector3d(0.5, 0, 0));
            Assert.True(new IntrSegment3Box3(inside, box, true).Find());
            Assert.False(new IntrSegment3Box3(inside, box, false).Find());           // fully inside the open box: no surface contact
            var touching = new Segment3d(new Vector3d(-0.5, 0, 0), new Vector3d(1, 0, 0));
            Assert.True(new IntrSegment3Box3(touching, box, false).Find());          // an end on a face
            var crossing = new Segment3d(new Vector3d(-2, 0, 0), new Vector3d(0, 0, 0));
            var c = new IntrSegment3Box3(crossing, box, false);
            Assert.True(c.Find());
            Assert.Equal(IntersectionType.Segment, c.Type);
        }

        [Fact]
        public void NonFiniteInput_IsAnInvalidQuery_NeverAHit()
        {
            var box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                var ray = new Ray3d(new Vector3d(bad, 0.5, 0.5), Vector3d.AxisX);
                var f = new IntrRay3AxisAlignedBox3(ray, box);
                Assert.False(f.Find()); Assert.Equal(IntersectionResult.InvalidQuery, f.Result);
                Assert.False(f.Test());
                double t; Assert.False(IntrRay3AxisAlignedBox3.FindRayIntersectT(ref ray, ref box, out t));
                var bbox = new AxisAlignedBox3d(new Vector3d(0, bad, 0), new Vector3d(1, 1, 1));
                var ray2 = new Ray3d(new Vector3d(-1, 0.5, 0.5), Vector3d.AxisX);
                Assert.False(new IntrRay3AxisAlignedBox3(ray2, bbox).Find());
                Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref ray2, ref bbox));
                var ob = new Box3d(new Vector3d(bad, 0, 0), new Vector3d(1, 1, 1));
                var ray3 = new Ray3d(new Vector3d(-1, 0.5, 0.5), Vector3d.AxisX);
                Assert.False(IntrRay3Box3.Intersects(ref ray3, ref ob));
                Assert.False(new IntrLine3Box3(new Line3d(new Vector3d(-1, 0, 0), Vector3d.AxisX), ob).Find());
                Assert.False(new IntrSegment3Box3(new Segment3d(new Vector3d(bad, 0, 0), new Vector3d(1, 0, 0)), new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)), true).Find());
            }
        }

        [Fact]
        public void ExpandExtents_MovesTheFacesOut_ExactlyAtTheBoundary()
        {
            var box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            var ray = new Ray3d(new Vector3d(-3, 1.25, 0), Vector3d.AxisX);
            Assert.False(IntrRay3Box3.Intersects(ref ray, ref box));
            Assert.True(IntrRay3Box3.Intersects(ref ray, ref box, 0.25));           // exactly on the expanded face
            Assert.False(IntrRay3Box3.Intersects(ref ray, ref box, 0.25 - 1e-12));
            var aab = new AxisAlignedBox3d(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            Assert.True(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref aab, 0.25));
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref aab, 0.25 - 1e-12));
        }

        [Fact]
        public void ReusedInstances_FollowTheirSetters()
        {
            var box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            var f = new IntrRay3AxisAlignedBox3(new Ray3d(new Vector3d(-1, 0.5, 0.5), Vector3d.AxisX), box);
            Assert.True(f.Find());
            Assert.Equal(new Vector3d(0, 0.5, 0.5), f.Point0);
            f.Ray = new Ray3d(new Vector3d(-1, 2, 0.5), Vector3d.AxisX);
            Assert.False(f.Find());
            f.Box = new AxisAlignedBox3d(new Vector3d(0, 1, 0), new Vector3d(1, 3, 1));
            Assert.True(f.Find());
            var ob = new IntrRay3Box3(new Ray3d(new Vector3d(-2, 0, 0), Vector3d.AxisX), new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)));
            Assert.True(ob.Find()); Assert.Equal(new Vector3d(-1, 0, 0), ob.Point0); Assert.Equal(new Vector3d(1, 0, 0), ob.Point1);
            ob.Ray = new Ray3d(new Vector3d(-2, 5, 0), Vector3d.AxisX);
            Assert.False(ob.Find());
            var lb = new IntrLine3Box3(new Line3d(new Vector3d(0, 5, 0), Vector3d.AxisX), new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)));
            Assert.False(lb.Find());
            lb.Line = new Line3d(new Vector3d(0, 1, 0), Vector3d.AxisX);
            Assert.True(lb.Find());                                  // on the face
            var sb = new IntrSegment3Box3(new Segment3d(new Vector3d(-3, 0, 0), new Vector3d(-2, 0, 0)), new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)), true);
            Assert.False(sb.Find());
            sb.Segment = new Segment3d(new Vector3d(-3, 0, 0), new Vector3d(0, 0, 0));
            Assert.True(sb.Find());
            sb.Solid = false;
            Assert.True(sb.Find());
        }

        [Fact]
        public void PublicClippingEntryPoints_KeepTheirShape()
        {
            var box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            double t0 = 0, t1 = double.MaxValue; int quantity = 0; Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero; var type = IntersectionType.Empty;
            Assert.True(IntrLine3Box3.DoClipping(ref t0, ref t1, new Vector3d(-3, 0, 0), Vector3d.AxisX, box, true, ref quantity, ref p0, ref p1, ref type));
            Assert.Equal(2.0, t0); Assert.Equal(4.0, t1); Assert.Equal(2, quantity); Assert.Equal(IntersectionType.Segment, type);
            Assert.Equal(new Vector3d(-1, 0, 0), p0); Assert.Equal(new Vector3d(1, 0, 0), p1);
            t0 = -double.MaxValue; t1 = double.MaxValue; var aab = new AxisAlignedBox3d(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            Vector3d o = new Vector3d(0, 0, 0), dir = Vector3d.AxisY;
            Assert.True(IntrLine3AxisAlignedBox3.DoClipping(ref t0, ref t1, ref o, ref dir, ref aab, true, ref quantity, ref p0, ref p1, ref type));
            Assert.Equal(-1.0, t0); Assert.Equal(1.0, t1);
        }

        // ---- invariance: scaling by a power of two gives the same result bit for bit ----
        // ---- invariance over Similarity.All(): the decisions of every entry point map through the similarity ----
        public static IEnumerable<object[]> SimilarityNames() { return Similarity.Names(); }

        /// <summary> the decisions of the ray, the line and the segment do not change when the extents of the box move by +-eps (no touching case) </summary>
        static bool HasMargin(Vector3d o, Vector3d d, Box3d ob, double eps)
        {
            Vector3d far = o + d * 6.5;
            bool r0 = false, l0 = false, s0 = false;
            for (int k = 0; k < 3; k++) {
                double e = k == 0 ? 0 : (k == 1 ? eps : -eps);
                Box3d b = new Box3d(ob.Center, ob.AxisX, ob.AxisY, ob.AxisZ, ob.Extent + new Vector3d(e, e, e));
                bool r = new IntrRay3Box3(new Ray3d(o, d), b).Find(), l = new IntrLine3Box3(new Line3d(o, d), b).Find(), sg = new IntrSegment3Box3(new Segment3d(o, far), b, true).Find();
                if (k == 0) { r0 = r; l0 = l; s0 = sg; }
                else if (r != r0 || l != l0 || sg != s0) return false;
            }
            return true;
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Similarities_EveryEntryPointGivesTheMappedResult(string name)
        {
            Similarity sim = Similarity.ByName(name);
            bool exact = sim.IsExact;
            int compared = 0, n = 0;
            // (a similarity with a rotation or a translation rounds the input, so only the cases with a margin keep their decision, see HasMargin;
            // the scales by 2^k change nothing in the rounding: every case, the touching ones too, and the results bit for bit)
            foreach (var (o, d, lo, hi) in Cases(exact ? 600 : 1200, 31)) {
                if (Normalized(d) == false) continue;
                // (a coordinate in the subnormal range, from Up(0): the rounded ends of a Segment3d, center -+ extent * direction, lose their
                // scale invariance there; not a property of the box decision)
                if (new[] { o.x, o.y, o.z, lo.x, lo.y, lo.z, hi.x, hi.y, hi.z, o.x + d.x * 6.5, o.y + d.y * 6.5, o.z + d.z * 6.5 }.Any(v => v != 0 && Math.Abs(v) < 1e-200)) continue;
                n++;
                Box3d ob = Oriented(lo, hi, n);
                if (exact == false && (HasMargin(o, d, ob, 1e-5) == false || HasMargin(o, d, Oriented(lo, hi, 0), 1e-5) == false)) continue;
                Vector3d ax = sim.ApplyDirection(ob.AxisX), ay = sim.ApplyDirection(ob.AxisY), az = sim.ApplyDirection(ob.AxisZ);
                Box3d ob2 = new Box3d(sim.Apply(ob.Center), ax, ay, az, ob.Extent * sim.Scale);
                Vector3d o2 = sim.Apply(o), d2 = exact ? d : sim.ApplyDirection(d).Normalized;       // (the same bits of the direction for the scales)
                Vector3d far = o + d * 6.5, far2 = sim.Apply(far);
                // oriented box, all of the components
                Ray3d ray = new Ray3d(o, d), ray2 = new Ray3d(o2, d2);
                bool rf = new IntrRay3Box3(ray, ob).Find(), rf2 = new IntrRay3Box3(ray2, ob2).Find();
                bool rt = new IntrRay3Box3(ray, ob).Test(), ri = IntrRay3Box3.Intersects(ref ray, ref ob);
                bool rt2 = new IntrRay3Box3(ray2, ob2).Test(), ri2 = IntrRay3Box3.Intersects(ref ray2, ref ob2);
                bool lf = new IntrLine3Box3(new Line3d(o, d), ob).Find(), lf2 = new IntrLine3Box3(new Line3d(o2, d2), ob2).Find();
                bool lt = new IntrLine3Box3(new Line3d(o, d), ob).Test(), lt2 = new IntrLine3Box3(new Line3d(o2, d2), ob2).Test();
                Assert.Equal(rf, rf2); Assert.Equal(rt, rt2); Assert.Equal(ri, ri2); Assert.Equal(lf, lf2); Assert.Equal(lt, lt2);
                Assert.Equal(rf, rt); Assert.Equal(rf, ri); Assert.Equal(lf, lt);
                foreach (bool solid in new[] { true, false }) {
                    var sg = new IntrSegment3Box3(new Segment3d(o, far), ob, solid); var sg2 = new IntrSegment3Box3(new Segment3d(o2, far2), ob2, solid);
                    bool sf = sg.Find(), sf2 = sg2.Find();
                    Assert.Equal(sf, sf2); Assert.Equal(sg.Type, sg2.Type); Assert.Equal(sg.Quantity, sg2.Quantity);
                    Assert.Equal(sf, new IntrSegment3Box3(new Segment3d(o, far), ob, solid).Test());
                    Assert.Equal(sf2, new IntrSegment3Box3(new Segment3d(o2, far2), ob2, solid).Test());
                    if (exact && sf) { Assert.Equal(sg.Point0 * sim.Scale, sg2.Point0); if (sg.Quantity == 2) Assert.Equal(sg.Point1 * sim.Scale, sg2.Point1); }
                }
                if (exact && rf) {
                    var a = new IntrRay3Box3(ray, ob); a.Find(); var b = new IntrRay3Box3(ray2, ob2); b.Find();
                    Assert.Equal(a.RayParam0 * sim.Scale, b.RayParam0); Assert.Equal(a.Point0 * sim.Scale, b.Point0);
                }
                // axis-aligned box (a rotation turns it into an oriented one: the scales and translations only)
                if (sim.IsRotated == false) {
                    var aab = new AxisAlignedBox3d(lo, hi); var aab2 = new AxisAlignedBox3d(sim.Apply(lo), sim.Apply(hi));
                    var ra = new IntrRay3AxisAlignedBox3(ray, aab); var ra2 = new IntrRay3AxisAlignedBox3(ray2, aab2);
                    bool raf = ra.Find(), raf2 = ra2.Find();
                    Assert.Equal(raf, raf2); Assert.Equal(raf, ra.Test()); Assert.Equal(raf2, ra2.Test());
                    Assert.Equal(raf, IntrRay3AxisAlignedBox3.Intersects(ref ray, ref aab)); Assert.Equal(raf2, IntrRay3AxisAlignedBox3.Intersects(ref ray2, ref aab2));
                    double t, t2;
                    bool ft = IntrRay3AxisAlignedBox3.FindRayIntersectT(ref ray, ref aab, out t), ft2 = IntrRay3AxisAlignedBox3.FindRayIntersectT(ref ray2, ref aab2, out t2);
                    Assert.Equal(ft, ft2); Assert.Equal(raf, ft);
                    if (exact && ft) Assert.Equal(t * sim.Scale, t2);
                    var la = new IntrLine3AxisAlignedBox3(new Line3d(o, d), aab); var la2 = new IntrLine3AxisAlignedBox3(new Line3d(o2, d2), aab2);
                    Assert.Equal(la.Find(), la2.Find()); Assert.Equal(la.Test(), la2.Test());
                    // the culling form: never a miss where the box is hit; the same decision and the entry bound scaled bit for bit
                    double m1, m2;
                    bool may = IntrExactBox3.RayAabbMay(ref ray.Origin, ref ray.Direction, ref aab, 0, out m1);
                    bool may2 = IntrExactBox3.RayAabbMay(ref ray2.Origin, ref ray2.Direction, ref aab2, 0, out m2);
                    if (raf) { Assert.True(may); Assert.True(may2); }
                    if (exact) { Assert.Equal(may, may2); if (may) Assert.Equal(m1 * sim.Scale, m2); }
                    if (may && ft) Assert.True(m1 <= t);
                }
                compared++;
            }
            Assert.True(compared > 100);
        }

        [Theory]
        [InlineData(-20)]
        [InlineData(-3)]
        [InlineData(10)]
        [InlineData(40)]
        public void PowerOfTwoScales_GiveTheSameResult(int k)
        {
            double sc = Math.Pow(2, k);
            int compared = 0;
            int n = 0;
            foreach (var (o, d, lo, hi) in Cases(600, 17)) {
                if (Normalized(d) == false) continue;
                var aab = new AxisAlignedBox3d(lo, hi); var aab2 = new AxisAlignedBox3d(lo * sc, hi * sc);
                var ray = new Ray3d(o, d); var ray2 = new Ray3d(o * sc, d);
                var a = new IntrRay3AxisAlignedBox3(ray, aab); var b = new IntrRay3AxisAlignedBox3(ray2, aab2);
                Assert.Equal(a.Find(), b.Find());
                Assert.Equal(a.Type, b.Type); Assert.Equal(a.Quantity, b.Quantity); Assert.Equal(a.Result, b.Result);
                if (a.Result == IntersectionResult.Intersects) {
                    Assert.Equal(a.RayParam0 * sc, b.RayParam0); Assert.Equal(a.RayParam1 * sc, b.RayParam1);
                    Assert.Equal(a.Point0 * sc, b.Point0); if (a.Quantity == 2) Assert.Equal(a.Point1 * sc, b.Point1);
                    compared++;
                }
                var ob = Oriented(lo, hi, n++); var ob2 = Oriented(lo * sc, hi * sc, n - 1);
                var c = new IntrLine3Box3(new Line3d(o, d), ob); var e = new IntrLine3Box3(new Line3d(o * sc, d), ob2);
                Assert.Equal(c.Find(), e.Find()); Assert.Equal(c.Type, e.Type);
                if (c.Result == IntersectionResult.Intersects) { Assert.Equal(c.Point0 * sc, e.Point0); Assert.Equal(c.LineParam0 * sc, e.LineParam0); }
            }
            Assert.True(compared > 100);
        }
    }

    static class BigIntegerExt
    {
        public static long GetBitLengthPortable(this BigInteger v)
        {
            v = BigInteger.Abs(v);
            long n = 0; while (!v.IsZero) { v >>= 1; n++; }
            return n;
        }
    }
}
