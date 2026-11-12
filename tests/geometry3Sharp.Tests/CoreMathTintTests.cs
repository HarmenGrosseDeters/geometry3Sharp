// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The 192-bit helper type of the vendored atan2 (Tint: sign, exponent, h:m:l significand) against BigInteger. The first
// version of the port had lost bits in Lshift for shifts of 64 to 127; the hard cases of atan2 exposed it, these tests
// pin the operations themselves.
using System;
using System.Numerics;
using g3.CoreMathPort;
using Xunit;

namespace g3.Tests
{
    public class CoreMathTintTests
    {
        static ulong SplitMix(ref ulong s)
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static BigInteger Sig(CoreMath.Tint t) { return ((BigInteger)t.h << 128) | ((BigInteger)t.m << 64) | t.l; }

        static CoreMath.Tint Make(BigInteger sig, long ex, ulong sgn)
        {
            ulong mask = ulong.MaxValue;
            return new CoreMath.Tint((ulong)((sig >> 64) & mask), (ulong)((sig >> 128) & mask), (ulong)(sig & mask), ex, sgn);
        }

        static CoreMath.Tint Random(ref ulong s, long exLo, long exHi)
        {
            ulong h = SplitMix(ref s) | (1UL << 63), m = SplitMix(ref s), l = SplitMix(ref s);
            long ex = exLo + (long)(SplitMix(ref s) % (ulong)(exHi - exLo + 1));
            return new CoreMath.Tint(m, h, l, ex, SplitMix(ref s) & 1);
        }

        static BigInteger Mod192(BigInteger v) { return v & ((BigInteger.One << 192) - 1); }

        [Fact]
        public void Lshift_And_Rshift_AreExactOnTheSignificand()
        {
            ulong s = 11;
            for (int i = 0; i < 400; ++i) {
                var b = Random(ref s, -50, 50);
                for (int k = 0; k <= 200; ++k) {
                    var left = CoreMath.Tint.Lshift(new CoreMath.Tint(), b, k);
                    var right = CoreMath.Tint.Rshift(new CoreMath.Tint(), b, k);
                    BigInteger wantLeft = k >= 192 ? BigInteger.Zero : Mod192(Sig(b) << k);
                    BigInteger wantRight = k >= 192 ? BigInteger.Zero : Sig(b) >> k;
                    Assert.True(Sig(left) == wantLeft, $"Lshift by {k}");
                    Assert.True(Sig(right) == wantRight, $"Rshift by {k}");
                }
            }
        }

        // value = sign * sig * 2^(ex - 192); difference in units of 2^(exR - 192) of the result
        static BigInteger ScaledValue(CoreMath.Tint t, long ex0)
        {
            // t as an integer multiple of 2^(ex0 - 192 - 400): scale by shifting
            int sh = (int)(t.ex - ex0) + 400;
            BigInteger v = sh >= 0 ? Sig(t) << sh : Sig(t) >> -sh;
            return t.sgn != 0 ? -v : v;
        }

        [Fact]
        public void Add_ErrorIsBelowTwoUlps()
        {
            ulong s = 12;
            for (int i = 0; i < 3000; ++i) {
                var a = Random(ref s, 0, 6);
                var b = Random(ref s, -3, 6);
                if (i % 3 == 0) b = new CoreMath.Tint(a.m, a.h, a.l ^ (SplitMix(ref s) & 0xFFFF), a.ex - (long)(SplitMix(ref s) % 3), 1 - a.sgn);    // cancellation
                var r = CoreMath.Tint.Add(a, b);
                BigInteger exact = ScaledValue(a, 0) + ScaledValue(b, 0);
                BigInteger got = r.h == 0 ? BigInteger.Zero : ScaledValue(r, 0);
                BigInteger err = BigInteger.Abs(got - exact);
                int ulpShift = (int)r.ex + 400;                          // 1 ulp (the least significant bit of the 192-bit significand) in the same units
                BigInteger ulp = ulpShift >= 0 ? BigInteger.One << ulpShift : BigInteger.One;
                if (r.h == 0) { Assert.True(exact.IsZero || BigInteger.Abs(exact) < (BigInteger.One << (400 - 100)), "zero result"); continue; }
                Assert.True(err <= 2 * ulp, $"Add case {i}: error {err} > 2 ulp ({ulp})");
            }
        }

        [Fact]
        public void Mul_ErrorIsBelowTenUlps()
        {
            ulong s = 13;
            for (int i = 0; i < 3000; ++i) {
                var a = Random(ref s, -4, 4);
                var b = Random(ref s, -4, 4);
                var r = CoreMath.Tint.Mul(a, b);
                // exact product: sig_a * sig_b * 2^(ea + eb - 384), compared with sig_r * 2^(er - 192)
                BigInteger exactSig = Sig(a) * Sig(b);                    // scale 2^(ea + eb - 384)
                long shift = (a.ex + b.ex - 384) - (r.ex - 192);          // exact = exactSig * 2^shift in units of the result significand
                BigInteger exactInResultUnits = shift >= 0 ? exactSig << (int)shift : exactSig >> (int)-shift;
                BigInteger err = BigInteger.Abs(exactInResultUnits - Sig(r));
                Assert.True(err <= 10, $"Mul case {i}: error {err} ulp");
                Assert.True(r.sgn == (a.sgn ^ b.sgn));
            }
        }

        [Fact]
        public void Div_RelativeErrorIsBelow2PowMinus180()
        {
            ulong s = 14;
            for (int i = 0; i < 1500; ++i) {
                var a = Random(ref s, 0, 1);
                var b = Random(ref s, 0, 1);
                var q = CoreMath.Tint.Div(b, a);
                // q * a vs b, as exact rationals scaled by 2^(2 * 192 + 600)
                BigInteger qa = Sig(q) * Sig(a);                          // 2^(q.ex + a.ex - 384)
                BigInteger bb = Sig(b);                                   // 2^(b.ex - 192)
                int e1 = (int)(q.ex + a.ex - 384), e2 = (int)(b.ex - 192);
                int lo = Math.Min(e1, e2);
                BigInteger lhs = qa << (e1 - lo), rhs = bb << (e2 - lo);
                BigInteger diff = BigInteger.Abs(lhs - rhs);
                // relative error: diff / rhs < 2^-180
                Assert.True((diff << 180) < rhs, $"Div case {i}: relative error too large");
            }
        }
    }
}
