// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Probe for the allow list of the exact kernel (docs/design/DETERMINISM_PROOF.md, stage 0, issue 859): the conversions
// BigInteger -> double (explicit operator) and double -> BigInteger (constructor), which the kernel uses on values
// beyond 2^53. No library code.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DeterminismHarness
{
    static class ProbesBigInteger
    {
        public static void AddTo(List<Case> list)
        {
            list.Add(new Case("probe.bigint-double", serial => ToDoubleProbe()));
            list.Add(new Case("probe.double-bigint", serial => FromDoubleProbe()));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static double Convert(BigInteger v) { return (double)v; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static BigInteger Construct(double d) { return new BigInteger(d); }

        static int BitLength(BigInteger m)
        {
            // m > 0 (net48 has no GetBitLength)
            byte[] b = m.ToByteArray();
            int top = b.Length - 1;
            while (top > 0 && b[top] == 0) top--;
            int n = 8 * top, x = b[top];
            while (x != 0) { n++; x >>= 1; }
            return n;
        }

        // the two candidate rules, written out on the exact value: round to nearest even and truncation toward zero
        static double Reference(BigInteger v, bool nearest)
        {
            if (v.IsZero) return 0;
            bool neg = v.Sign < 0;
            BigInteger m = BigInteger.Abs(v);
            int bits = BitLength(m);
            double r;
            if (bits <= 53) {
                r = (double)(long)m;
            } else {
                int drop = bits - 53;
                BigInteger mant = m >> drop;
                if (nearest) {
                    BigInteger low = m - (mant << drop), half = BigInteger.One << (drop - 1);
                    int c = low.CompareTo(half);
                    if (c > 0 || (c == 0 && mant.IsEven == false)) mant += 1;
                    if (mant == (BigInteger.One << 53)) { mant >>= 1; drop++; }
                }
                int exp = drop + 52;   // value = mant * 2^drop, 2^52 <= mant < 2^53
                if (exp > 1023)
                    r = double.PositiveInfinity;
                else
                    r = BitConverter.Int64BitsToDouble(((long)(exp + 1023) << 52) | ((long)mant & 0xFFFFFFFFFFFFFL));
            }
            return neg ? -r : r;
        }

        static IEnumerable<BigInteger> Values()
        {
            var rng = new Rng(859);
            int[] lengths = { 53, 54, 55, 56, 63, 64, 65, 66, 96, 127, 128, 129, 200, 511, 512, 1000, 1023, 1024, 1025 };
            foreach (int b in lengths) {
                BigInteger top = BigInteger.One << (b - 1);
                int drop = b - 53;
                // a random 53-bit mantissa with its last bit even / odd, at length b
                for (int parity = 0; parity < 2; ++parity) {
                    BigInteger mant = (BigInteger.One << 52) + new BigInteger((long)(rng.Next() >> 12) & ~1L) + parity;
                    BigInteger baseV = mant << drop;
                    yield return baseV;                                         // exact
                    if (drop >= 1) {
                        BigInteger half = BigInteger.One << (drop - 1);
                        yield return baseV + half;                              // tie
                        yield return baseV + half - 1;                          // just below the tie
                        yield return baseV + half + 1;                          // just above the tie (sticky bit at bit 0)
                        yield return baseV + (BigInteger.One << drop) - 1;      // all ones below the mantissa
                        yield return baseV + 1;                                 // just above the exact value
                    }
                    if (drop >= 12) {
                        // the sticky bit beyond the top 64 bits: tie plus a bit far below
                        BigInteger half = BigInteger.One << (drop - 1);
                        yield return baseV + half + (BigInteger.One << (drop - 12));
                        yield return baseV + half - (BigInteger.One << (drop - 12));
                    }
                }
                yield return top;                                               // a power of two
                yield return (BigInteger.One << b) - 1;                         // all ones (rounds up to 2^b)
                // random values of this length
                for (int k = 0; k < 16; ++k) {
                    BigInteger v = top;
                    for (int s = 0; s < b - 1; s += 60)
                        v += new BigInteger((long)(rng.Next() >> 4)) << s;
                    v &= (BigInteger.One << b) - 1;
                    v |= top;
                    yield return v;
                }
            }
            // the overflow boundary: the largest finite double, the tie to 2^1024 and above
            BigInteger max = ((BigInteger.One << 53) - 1) << 971;
            yield return max;
            yield return max + (BigInteger.One << 969);
            yield return max + (BigInteger.One << 970) - 1;
            yield return max + (BigInteger.One << 970);
            yield return max + (BigInteger.One << 970) + 1;
            yield return BigInteger.One << 1024;
            yield return BigInteger.One << 2000;
        }

        static Trace ToDoubleProbe()
        {
            var t = new Trace();
            int n = 0, asNearest = 0, asTrunc = 0, neither = 0, inexact = 0;
            string firstNeither = null;
            foreach (BigInteger p in Values())
                foreach (BigInteger v in new[] { p, -p }) {
                    double got = Convert(v);
                    t.Section("convert"); t.Add(got);
                    double rn = Reference(v, true), rz = Reference(v, false);
                    n++;
                    bool eqN = BitConverter.DoubleToInt64Bits(got) == BitConverter.DoubleToInt64Bits(rn);
                    bool eqZ = BitConverter.DoubleToInt64Bits(got) == BitConverter.DoubleToInt64Bits(rz);
                    if (eqN) asNearest++;
                    if (eqZ) asTrunc++;
                    if (eqN == false && eqZ == false) { neither++; if (firstNeither == null) firstNeither = "bits " + BitLength(BigInteger.Abs(v)) + " sign " + v.Sign; }
                    if (BitConverter.DoubleToInt64Bits(rn) != BitConverter.DoubleToInt64Bits(rz)) inexact++;
                    t.Section("rule"); t.Add((eqN ? 1 : 0) + (eqZ ? 2 : 0));
                }
            t.Notes.Add("(double)BigInteger on " + n + " values, " + inexact + " where the two rules differ: equal to round-to-nearest-even "
                + asNearest + ", equal to truncation " + asTrunc + ", neither " + neither + (firstNeither != null ? " (first: " + firstNeither + ")" : ""));
            return t;
        }

        static Trace FromDoubleProbe()
        {
            var t = new Trace();
            var rng = new Rng(860);
            var ds = new List<double> { 0.0, -0.0, 0.5, -0.5, 0.999999, 1.0, 1.5, -1.5, 2.5, -2.5, 4503599627370495.5, -4503599627370495.5,
                9007199254740991.0, 9007199254740992.0, 9007199254740994.0, 1e19, 1.8446744073709552e19, 1e30, -1e30, 1e300, -1e300,
                double.MaxValue, -double.MaxValue, double.Epsilon, -double.Epsilon, 2.2250738585072014e-308 };
            for (int e = 53; e <= 1023; e += 7)
                for (int k = 0; k < 3; ++k) {
                    long bits = ((long)(e + 1023) << 52) | (long)(rng.Next() >> 12);
                    ds.Add(BitConverter.Int64BitsToDouble(bits));
                    ds.Add(-BitConverter.Int64BitsToDouble(bits));
                }
            for (int k = 0; k < 64; ++k)
                ds.Add(rng.Range(-1e17, 1e17));
            int mismatch = 0;
            foreach (double d in ds) {
                BigInteger v = Construct(d);
                t.Section("construct");
                byte[] b = v.ToByteArray();
                t.Add(b.Length);
                for (int i = 0; i < b.Length; i += 8) {
                    long w = 0;
                    for (int j = 0; j < 8 && i + j < b.Length; ++j) w |= (long)b[i + j] << (8 * j);
                    t.Add(w);
                }
                // reference: the value truncated toward zero, from the bits
                long raw = BitConverter.DoubleToInt64Bits(d);
                int ex = (int)((raw >> 52) & 0x7FF);
                BigInteger want;
                if (ex < 1023) want = BigInteger.Zero;
                else {
                    BigInteger m = new BigInteger((raw & 0xFFFFFFFFFFFFFL) | (1L << 52));
                    int sh = ex - 1075;
                    want = sh >= 0 ? m << sh : m >> -sh;
                    if (raw < 0) want = -want;
                }
                if (v != want) mismatch++;
            }
            string[] special = new string[3];
            double[] sp = { double.NaN, double.PositiveInfinity, double.NegativeInfinity };
            t.Section("special");
            for (int i = 0; i < 3; ++i) {
                try { BigInteger v = Construct(sp[i]); special[i] = "value " + v; t.Add(0); }
                catch (Exception ex) { special[i] = ex.GetType().Name; t.Add(ex is OverflowException ? 1 : 2); }
            }
            t.Notes.Add("new BigInteger(double) on " + ds.Count + " values: " + mismatch + " differ from truncation toward zero; NaN: " + special[0]
                + ", +Inf: " + special[1] + ", -Inf: " + special[2]);
            return t;
        }
    }
}
