// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The exact kernel's operations and the members of its allow list (DeterministicApis.txt, the G3D0009 ledger of
// docs/design/DETERMINISM_PROOF.md 5.2) on procedural inputs, and Booleans whose rounding meets near-degenerate
// contacts: the net48 / net10.0 measurement of DETERMINISM_PROOF.md section 5.3. The inputs come from a splitmix64
// stream (System.Random is a probe of its own), so every runtime sees the same bits.
using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

namespace DeterminismHarness
{
    static class Kernel
    {
        public static void AddTo(List<Case> list)
        {
            list.Add(new Case("kernel.predicates.double", serial => Predicates()));
            list.Add(new Case("kernel.predicates.adaptive", serial => Adaptive()));
            list.Add(new Case("kernel.exactpoint", serial => ExactPoints()));
            list.Add(new Case("kernel.fixedpoint", serial => FixedPoints()));
            list.Add(new Case("kernel.limbsigns", serial => LimbSigns()));
            list.Add(new Case("kernel.triangles", serial => Triangles()));
            list.Add(new Case("kernel.ray", serial => Rays()));
            list.Add(new Case("kernel.slope", serial => Slopes()));
            list.Add(new Case("kernel.intervals", serial => Intervals()));
            list.Add(new Case("ledger.math", serial => LedgerMath()));
            list.Add(new Case("ledger.biginteger", serial => LedgerBigInteger()));
            foreach (int k in new[] { -30, 0, 30 }) {
                int s = k;
                list.Add(new Case("boolean.rounding.coplanar.2^" + k, serial => CoplanarBoxes(s, true)));
                list.Add(new Case("boolean.rounding.coplanar.2^" + k + ".exact", serial => CoplanarBoxes(s, false)));
            }
            list.Add(new Case("boolean.rounding.nearcontact", serial => NearContact()));
        }

        // ---------------------------------------------------------------- inputs

        sealed class Rng
        {
            ulong s;
            public Rng(ulong seed) { s = seed; }
            public ulong Next() { ulong z = (s += 0x9E3779B97F4A7C15UL); z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
            public double Unit() { return (Next() >> 11) * (1.0 / 9007199254740992.0); }
            public double Range(double lo, double hi) { return lo + (hi - lo) * Unit(); }
            public int Int(int n) { return (int)(Next() % (ulong)n); }
            public BigInteger Big(int bits)
            {
                BigInteger v = BigInteger.Zero;
                for (int b = 0; b < bits; b += 64) v = (v << 64) + new BigInteger(Next());
                v >>= ((bits + 63) / 64) * 64 - bits;
                return (Next() & 1) == 0 ? v : -v;
            }
        }

        // a double moved by k ulps (k may be negative)
        static double Ulps(double x, int k)
        {
            long b = BitConverter.DoubleToInt64Bits(x);
            if (x == 0) return k * double.Epsilon;
            return BitConverter.Int64BitsToDouble(x > 0 ? b + k : b - k);
        }

        static double ScaleB(double x, int e) { return ExactPoint3.ScaleB(x, e); }

        // point sets for the predicates: random, nearly collinear / coplanar / cocircular (a point on the exact
        // object moved by a few ulps), with exponents from subnormal to huge
        static IEnumerable<Vector3d[]> Points3(Rng r, int n, int count)
        {
            int[] exps = { 0, -60, 60, -1000, 900, -1060 };
            for (int i = 0; i < count; ++i) {
                int e = exps[i % exps.Length];
                var p = new Vector3d[n];
                for (int k = 0; k < n; ++k) p[k] = new Vector3d(ScaleB(r.Range(-1, 1), e), ScaleB(r.Range(-1, 1), e), ScaleB(r.Range(-1, 1), e));
                switch (i % 4) {
                    case 1:     // the last point on the line / plane of the first ones, then moved by ulps
                        double t = r.Unit(), u = r.Unit();
                        Vector3d q = p[0] + t * (p[1] - p[0]) + (n > 3 ? u * (p[2] - p[0]) : Vector3d.Zero);
                        p[n - 1] = new Vector3d(Ulps(q.x, r.Int(5) - 2), Ulps(q.y, r.Int(5) - 2), Ulps(q.z, r.Int(5) - 2));
                        break;
                    case 2:     // small integers: exact degeneracies
                        for (int k = 0; k < n; ++k) p[k] = new Vector3d(r.Int(5) - 2, r.Int(5) - 2, r.Int(5) - 2);
                        break;
                    case 3:     // a repeated point
                        p[n - 1] = p[0];
                        break;
                }
                yield return p;
            }
        }

        static Vector2d V2(Vector3d v) { return new Vector2d(v.x, v.y); }

        // a sign, or the kind of exception (a domain error of the input, eg an overflow to infinity or NaN, is a
        // result too: it must be the same on every runtime)
        static long S(Func<int> f)
        {
            try { return f(); }
            catch (ArithmeticException) { return 1001; }
            catch (ArgumentException) { return 1002; }
            catch (InvalidOperationException) { return 1003; }
        }
        static long S(Func<bool> f) { return S(() => f() ? 1 : 0); }

        // ---------------------------------------------------------------- kernel

        static Trace Predicates()
        {
            var t = new Trace(); var r = new Rng(1);
            t.Section("orient2d");
            foreach (var p in Points3(r, 3, 3000)) t.Add(S(() => ExactPredicates.Orient2d(V2(p[0]), V2(p[1]), V2(p[2]))));
            t.Section("orient3d");
            foreach (var p in Points3(r, 4, 3000)) t.Add(S(() => ExactPredicates.Orient3d(p[0], p[1], p[2], p[3])));
            t.Section("incircle");
            foreach (var p in Points3(r, 4, 3000)) t.Add(S(() => ExactPredicates.InCircle(V2(p[0]), V2(p[1]), V2(p[2]), V2(p[3]))));
            t.Section("insphere");
            foreach (var p in Points3(r, 5, 2000)) t.Add(S(() => ExactPredicates.InSphere(p[0], p[1], p[2], p[3], p[4])));
            t.Section("exact-stage");
            foreach (var p in Points3(r, 5, 500)) {
                t.Add(S(() => ExactPredicates.Orient3dExact(p[0], p[1], p[2], p[3])));
                t.Add(S(() => ExactPredicates.Orient2dExact(V2(p[0]), V2(p[1]), V2(p[2]))));
                t.Add(S(() => ExactPredicates.InCircleExact(V2(p[0]), V2(p[1]), V2(p[2]), V2(p[3]))));
                t.Add(S(() => ExactPredicates.InSphereExact(p[0], p[1], p[2], p[3], p[4])));
            }
            return t;
        }

        static Trace Adaptive()
        {
            var t = new Trace(); var r = new Rng(2);
            t.Section("orient2d");
            foreach (var p in Points3(r, 3, 3000)) t.Add(S(() => AdaptivePredicates.Orient2d(p[0].x, p[0].y, p[1].x, p[1].y, p[2].x, p[2].y)));
            t.Section("orient3d");
            foreach (var p in Points3(r, 4, 3000)) t.Add(S(() => AdaptivePredicates.Orient3d(p[0].x, p[0].y, p[0].z, p[1].x, p[1].y, p[1].z, p[2].x, p[2].y, p[2].z, p[3].x, p[3].y, p[3].z)));
            t.Section("incircle");
            foreach (var p in Points3(r, 4, 3000)) t.Add(S(() => AdaptivePredicates.InCircle(p[0].x, p[0].y, p[1].x, p[1].y, p[2].x, p[2].y, p[3].x, p[3].y)));
            return t;
        }

        static ExactPoint3 Point(Rng r, int bits, int wbits)
        {
            BigInteger w = BigInteger.Abs(r.Big(wbits)) + 1;
            return ExactPoint3.Create(r.Big(bits), r.Big(bits), r.Big(bits), w);
        }

        static Trace ExactPoints()
        {
            var t = new Trace(); var r = new Rng(3);
            foreach (int bits in new[] { 8, 53, 54, 64, 120, 255, 600, 1100 }) {
                t.Section("bits-" + bits);
                for (int i = 0; i < 200; ++i) {
                    ExactPoint3 p = Point(r, bits, i % 3 == 0 ? 1 : bits / 2 + 1);
                    t.Add(p.ToVector3d()); t.Add(p.ToVector3d(-40)); t.Add(p.ToVector3d(40)); t.Add(p.ApproxVector3d());
                    t.Add(ExactPoint3.BitLength(p.X)); t.Add(ExactPoint3.ToDouble(p.X, p.W, i % 7 - 3));
                    BigInteger m; int e; double x0 = p.ToVector3d().x;
                    t.Add(MathUtil.IsFinite(x0));
                    if (MathUtil.IsFinite(x0)) { ExactPoint3.Decompose(x0, out m, out e); t.Add((long)(m % long.MaxValue)); t.Add(e); }
                    ExactPoint3 q = Point(r, bits, 1), s = Point(r, bits, 1);
                    BigInteger d = ExactPoint3.Det3(p.X, p.Y, p.Z, q.X, q.Y, q.Z, s.X, s.Y, s.Z);
                    t.Add(d.Sign); t.Add((long)(d % long.MaxValue));
                    t.Add(p.GetHashCode() == ExactPoint3.Create(p.X, p.Y, p.Z, p.W).GetHashCode());
                    t.Add(p.Equals(ExactPoint3.CreateUnreduced(p.X * 3, p.Y * 3, p.Z * 3, p.W * 3)));
                }
            }
            t.Section("scaleb");
            foreach (double x in new[] { 1.0, -1.5, double.Epsilon, 4.9e-300, 1.7e308, 0.0, -0.0 })
                foreach (int e in new[] { -1100, -1074, -1023, -1, 0, 1, 1023, 1100 })
                    t.Add(ExactPoint3.ScaleB(x, e));
            return t;
        }

        static Trace FixedPoints()
        {
            var t = new Trace(); var r = new Rng(4);
            foreach (int bits in new[] { 30, 53, 64, 128, 250, 300 }) {
                t.Section("bits-" + bits);
                for (int i = 0; i < 150; ++i) {
                    ExactPoint3 a = Point(r, bits, i % 2 == 0 ? 1 : 40), b = Point(r, bits, 1), c = Point(r, bits, 1), d = Point(r, bits, i % 3 == 0 ? 1 : 20);
                    if (i % 5 == 0) d = ExactPoint3.CreateUnreduced(a.X + b.X - c.X, a.Y + b.Y - c.Y, a.Z + b.Z - c.Z, BigInteger.One);
                    FixedPoint3 fa, fb, fc, fd;
                    bool ok = FixedPoint3.TryCreate(a, out fa) & FixedPoint3.TryCreate(b, out fb) & FixedPoint3.TryCreate(c, out fc) & FixedPoint3.TryCreate(d, out fd);
                    t.Add(ok);
                    if (!ok) continue;
                    t.Add(fa.IsDouble); t.Add(fa.D);
                    for (int ij = 0; ij < 3; ++ij) {
                        int ii = ij, jj = (ij + 1) % 3;
                        t.Add(FixedArith.Orient(ref fa, ref fb, ref fc, ii, jj));
                        t.Add(FixedArith.InCircle(ref fa, ref fb, ref fc, ref fd, ii, jj));
                    }
                    t.Add(FixedPoint3.SameValue(ref fa, ref fb));
                    t.Add(FixedArith.ComparePoints(ref fa, ref fb, r.Next(), r.Next(), r.Next()));
                    t.Add(fa.ToExact().Equals(a));
                }
            }
            t.Section("mulhigh");
            for (int i = 0; i < 2000; ++i) { ulong lo, x = r.Next(), y = r.Next() >> (i % 64); t.Add((long)FixedArith.MulHigh(x, y, out lo)); t.Add((long)lo); }
            return t;
        }

        static Trace LimbSigns()
        {
            var t = new Trace(); var r = new Rng(5);
            foreach (int bits in new[] { 20, 64, 130, 255 }) {
                t.Section("bits-" + bits);
                for (int i = 0; i < 150; ++i) {
                    BigInteger a = r.Big(bits), b = r.Big(bits), c = r.Big(bits), x = r.Big(bits), y = r.Big(bits), z = r.Big(bits), w = BigInteger.Abs(r.Big(bits / 2)) + 1;
                    // d chosen so that the side is zero, then moved by one
                    BigInteger d = -(a * x + b * y + c * z) / w + (i % 3 - 1);
                    int used;
                    t.Add(ExactLimbSigns.PlaneSide(a, b, c, d, x, y, z, w, out used)); t.Add(used >= 0);
                    t.Add(ExactLimbSigns.PlaneSide(a, b, c, d, x, y, z, out used)); t.Add(used >= 0);
                    t.Add(ExactLimbSigns.Dot(a, b, c, x, y, z, out used)); t.Add(used >= 0);
                    ExactPoint3 p = Point(r, bits, 1), q = Point(r, bits, 9), s = Point(r, bits, 1), u = Point(r, bits, 3);
                    if (i % 4 == 0) u = ExactPoint3.CreateUnreduced(p.X + q.X - s.X, p.Y + q.Y - s.Y, p.Z + q.Z - s.Z, BigInteger.One);
                    t.Add(ExactLimbSigns.Det4(p, q, s, u, out used)); t.Add(ExactLimbSigns.Det4Big(p, q, s, u));
                    t.Add(ExactLimbSigns.Orient2(p, q, s, 0, 1, out used)); t.Add(ExactLimbSigns.Orient2Big(p, q, s, 2, 0));
                    t.Add(ExactLimbSigns.InCircle(p, q, s, u, 1, 2, out used)); t.Add(ExactLimbSigns.InCircleBig(p, q, s, u, 1, 2));
                }
            }
            return t;
        }

        static Triangle3d Tri(Vector3d[] p, int o) { return new Triangle3d(p[o], p[o + 1], p[o + 2]); }

        static Trace Triangles()
        {
            var t = new Trace(); var r = new Rng(6);
            foreach (var p in Points3(r, 6, 3000)) {
                Triangle3d a = Tri(p, 0), b = Tri(p, 3);
                t.Add(S(() => ExactTriangleIntersection.Intersects(ref a, ref b)));
                t.Add(S(() => ExactTriangleIntersection.IntersectsByEdges(ref a, ref b)));
                t.Add(S(() => ExactTriangleIntersection.SegmentIntersectsTriangle(p[3], p[4], ref a)));
                t.Add(S(() => ExactTriangleIntersection.SegmentsIntersect(p[0], p[1], p[3], p[4])));
                t.Add(S(() => ExactTriangleIntersection.IsDegenerate(ref b)));
                // a shared vertex and a shared edge
                Triangle3d bv = new Triangle3d(p[0], p[4], p[5]), be = new Triangle3d(p[0], p[1], p[5]);
                t.Add(S(() => ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref a, new Index3i(0, 4, 5), ref bv)));
                t.Add(S(() => ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref a, new Index3i(0, 1, 5), ref be)));
                t.Add(S(() => TriangleTriangleFilter.Intersects(ref a, ref b)));
                t.Add(S(() => TriangleTriangleFilter.BeyondShared(new Index3i(0, 1, 2), ref a, new Index3i(0, 4, 5), ref bv)));
            }
            return t;
        }

        static Trace Rays()
        {
            var t = new Trace(); var r = new Rng(7);
            foreach (var p in Points3(r, 5, 3000)) {
                Vector3d o = p[3], q = ExactRayTriangle.SecondPoint(o, p[4] - p[3]);
                t.Add(q);
                t.Add(S(() => ExactRayTriangle.EdgeSign(o, q, p[0], p[1])));
                t.Add(S(() => ExactRayTriangle.LineCrossing(o, q, p[0], p[1], p[2])));
                t.Add(S(() => ExactRayTriangle.ClosedCrossing(o, q, p[0], p[1], p[2])));
                t.Add(S(() => ExactRayTriangle.RayHitClosed(o, q, p[0], p[1], p[2])));
                t.Add(S(() => ExactRayTriangle.RayHitPerturbed(o, q, p[0], p[1], p[2])));
            }
            return t;
        }

        static Trace Slopes()
        {
            var t = new Trace(); var r = new Rng(8);
            foreach (var p in Points3(r, 3, 3000)) {
                double k1a = r.Range(-2, 2), k1b = r.Range(0.1, 2), k2a = (r.Int(3) == 0) ? k1a : r.Range(-2, 2), k2b = (r.Int(3) == 0) ? k1b : r.Range(0.1, 2);
                bool deg;
                t.Add(ExactSlopePredicates.SlopeSign(p[0], p[1], p[2], k1a, k1b, k2a, k2b, out deg)); t.Add(deg);
                t.Add(ExactSlopePredicates.ProductMinus(p[0].x, p[0].y, p[0].x * p[0].y));
            }
            return t;
        }

        static Trace Intervals()
        {
            var t = new Trace(); var r = new Rng(9);
            for (int i = 0; i < 3000; ++i) {
                double x = ScaleB(r.Range(-1, 1), r.Int(200) - 100), y = ScaleB(r.Range(-1, 1), r.Int(200) - 100);
                if (i % 10 == 0) x = i % 20 == 0 ? double.Epsilon : -0.0;
                t.Add(ExactInterval.Up(x)); t.Add(ExactInterval.Down(x));
                ExactInterval a = ExactInterval.Of(x), b = new ExactInterval(Math.Min(x, y), Math.Max(x, y));
                ExactInterval s = a + b, d = a - b, m = a * b, q = ExactInterval.Square(b);
                t.Add(s.Lo); t.Add(s.Hi); t.Add(d.Lo); t.Add(d.Hi); t.Add(m.Lo); t.Add(m.Hi); t.Add(q.Lo); t.Add(q.Hi);
                ExactDyadic dx = ExactDyadic.Of(x), dy = ExactDyadic.Of(y);
                t.Add(ExactDyadic.Compare(dx, dy)); t.Add((dx * dy - dx + dy).Sign);
                if (dy.Sign != 0) t.Add(ExactDyadic.Quotient(dx + dy, dy));
            }
            return t;
        }

        // ---------------------------------------------------------------- the allow list's BCL members

        static readonly double[] Edge = {
            0.0, -0.0, double.Epsilon, -double.Epsilon, 2.2250738585072009e-308, 2.2250738585072014e-308, 1.0, -1.0, 0.5,
            1.0000000000000002, 0.9999999999999999, 4503599627370495.5, 4503599627370496.0, 9007199254740993.0,
            2147483647.0, 2147483648.0, -2147483648.0, -2147483649.0, 9.2233720368547758e18, -9.2233720368547758e18,
            1.7976931348623157e308, double.PositiveInfinity, double.NegativeInfinity, double.NaN };

        static Trace LedgerMath()
        {
            var t = new Trace(); var r = new Rng(10);
            var xs = new List<double>(Edge);
            for (int i = 0; i < 2000; ++i) xs.Add(ScaleB(r.Range(-1, 1), r.Int(2100) - 1075));
            t.Section("sqrt"); foreach (double x in xs) t.Add(Math.Sqrt(x));
            t.Section("abs-sign"); foreach (double x in xs) { t.Add(Math.Abs(x)); if (!double.IsNaN(x)) t.Add(Math.Sign(x)); }
            t.Section("isnan-isinf-finite"); foreach (double x in xs) { t.Add(double.IsNaN(x)); t.Add(double.IsInfinity(x)); t.Add(MathUtil.IsFinite(x)); }
            t.Section("min-max");
            foreach (double x in Edge) foreach (double y in Edge) { t.Add(Math.Min(x, y)); t.Add(Math.Max(x, y)); }
            t.Section("min-max-int");
            foreach (long x in new long[] { long.MinValue, -1, 0, 1, long.MaxValue, int.MinValue, int.MaxValue })
                foreach (long y in new long[] { long.MinValue, -1, 0, 7 }) { t.Add(Math.Min(x, y)); t.Add(Math.Max(x, y)); t.Add(Math.Abs(Math.Max(x, long.MinValue + 1))); }
            t.Section("bigmul");
            for (int i = 0; i < 500; ++i) { long a = (long)r.Next(), b = (long)r.Next(); t.Add(Math.BigMul((int)a, (int)b)); }
            t.Section("bits");
            foreach (double x in xs) t.Add(BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x))));
            t.Section("arith");     // the IEEE operations the kernel's filters are made of
            for (int i = 0; i + 2 < xs.Count; ++i) { double a = xs[i], b = xs[i + 1], c = xs[i + 2]; t.Add(a * b - c); t.Add((a + b) * c); t.Add(a / (b == 0 ? 1 : b)); }
            return t;
        }

        static Trace LedgerBigInteger()
        {
            var t = new Trace(); var r = new Rng(11);
            var vs = new List<BigInteger> { BigInteger.Zero, BigInteger.One, BigInteger.MinusOne, new BigInteger(long.MinValue), new BigInteger(ulong.MaxValue) };
            foreach (int bits in new[] { 1, 31, 32, 33, 52, 53, 54, 63, 64, 65, 127, 128, 129, 1023, 1024, 1025, 3000 }) {
                vs.Add(r.Big(bits)); vs.Add(r.Big(bits)); vs.Add(BigInteger.One << (bits - 1)); vs.Add(-(BigInteger.One << bits) + 1);
            }
            t.Section("to-double"); foreach (BigInteger v in vs) t.Add((double)v);
            t.Section("from-double"); foreach (double x in Edge) if (!double.IsNaN(x) && !double.IsInfinity(x)) { var b = new BigInteger(x); t.Add(b.Sign); t.Add((double)b); t.Add(b.IsZero); }
            for (int i = 0; i < 500; ++i) { double x = ScaleB(r.Range(-1, 1), r.Int(1024)); t.Add((double)new BigInteger(x)); }
            t.Section("to-long"); foreach (BigInteger v in vs) if (ExactPoint3.BitLength(v) < 63) t.Add((long)v);
            t.Section("ops");
            for (int i = 0; i < vs.Count; ++i)
                for (int j = 0; j < vs.Count; j += 3) {
                    BigInteger a = vs[i], b = vs[j];
                    t.Add(a.CompareTo(b)); t.Add(a == b); t.Add((a + b).Sign); t.Add(ExactPoint3.BitLength(a - b)); t.Add(ExactPoint3.BitLength(a * b));
                    if (!b.IsZero) {
                        BigInteger rem, q = BigInteger.DivRem(a, b, out rem);
                        t.Add(q.Sign); t.Add(rem.Sign); t.Add((double)q); t.Add((double)rem); t.Add((a / b).Equals(q));
                    }
                    t.Add((double)BigInteger.GreatestCommonDivisor(a, b));
                    t.Add((double)(a >> (j % 70))); t.Add((double)(a << (j % 70)));
                }
            t.Section("bytes");
            foreach (BigInteger v in vs) {
                byte[] bs = v.ToByteArray(); t.Add(bs.Length); foreach (byte b in bs) t.Add(b);
                t.Add(v.IsEven); t.Add(v.IsOne); t.Add(BigInteger.Abs(v).Sign); t.Add((-v).Sign);
            }
            return t;
        }

        // ---------------------------------------------------------------- Booleans with rounding

        // two boxes that share a face plane, the second rotated about the z axis by a Pythagorean angle (3, 4, 5:
        // its vertices are not on the grid of the first), scaled by 2^k: the contacts are exactly coplanar and the
        // rounding of the crossing points to doubles is the step under test
        static Trace CoplanarBoxes(int k, bool safe)
        {
            double s = ScaleB(1, k);
            DMesh3 a = Inputs.Box(Vector3d.Zero, new Vector3d(s, s, s));
            var t = new Trace();
            // on the top face from outside (a face contact), and inside up to the top face (overlapping coplanar faces)
            foreach (double zc in new[] { 1.5, 0.7 }) {
                double hz = zc > 1 ? 0.5 : 0.3;
                DMesh3 b = Inputs.Box(new Vector3d(0.3 * s, 0.2 * s, zc * s), new Vector3d(0.9 * s, 0.6 * s, hz * s));
                foreach (int v in b.VertexIndices()) {
                    Vector3d p = b.GetVertex(v), c = new Vector3d(0.3 * s, 0.2 * s, 0);
                    double x = p.x - c.x, y = p.y - c.y;
                    b.SetVertex(v, new Vector3d(c.x + (3 * x - 4 * y) / 5, c.y + (4 * x + 3 * y) / 5, p.z));
                }
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                    var boolean = new ExactMeshBoolean(a, b, op) { SafeRounding = safe };
                    string name = op.ToString() + "@" + zc.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    t.Section(name);
                    t.Add(boolean.Compute());
                    if (boolean.Result != null) t.Add(boolean.Result, name);
                }
            }
            return t;
        }

        // a sphere whose vertices lie a few ulps off the faces of a box: crossing points that round to the face
        static Trace NearContact()
        {
            DMesh3 a = Inputs.Box(Vector3d.Zero, new Vector3d(1, 1, 1)), b = Inputs.Sphere(new Vector3d(0, 0, 1.6), 0.6, 8);
            var r = new Rng(12);
            foreach (int v in b.VertexIndices()) {
                Vector3d p = b.GetVertex(v);
                if (p.z < 1.05) b.SetVertex(v, new Vector3d(p.x, p.y, Ulps(1.0, r.Int(7) - 3)));
            }
            var t = new Trace();
            foreach (bool safe in new[] { true, false }) {
                var boolean = new ExactMeshBoolean(a, b, Ops.Union) { SafeRounding = safe };
                t.Section("union-" + safe);
                t.Add(boolean.Compute());
                if (boolean.Result != null) t.Add(boolean.Result, "union-" + safe);
            }
            return t;
        }
    }
}
