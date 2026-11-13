using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The [SkipLocalsInit] members of the exact kernel (registered in DeterministicApis.txt, G3D0008): their stack buffers
    /// are not zeroed on .NET (and are on .NET Framework, where the attribute is compiled out: the #if NET blocks around
    /// it). A result that read an element before writing it would depend on what an earlier call left on the stack. The
    /// stack below the caller is filled with a pattern first, then the member runs in its place; every pattern must give
    /// the results of the zeroed stack, which is what the netstandard2.0 branch runs on.
    /// </summary>
    public unsafe class SkipLocalsInitKernelTests
    {
        const int DirtyBytes = 64 * 1024;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Dirty(byte pattern)
        {
            byte* b = stackalloc byte[DirtyBytes];
            for (int i = 0; i < DirtyBytes; ++i)
                b[i] = (byte)(pattern ^ (pattern == 0 ? 0 : (i * 131) & 0x3F));
            Keep(b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Keep(byte* b) { GC.KeepAlive(b[DirtyBytes - 1]); }

        static BigInteger Random(Random r, int bits)
        {
            if (bits <= 0) return BigInteger.Zero;
            var bytes = new byte[(bits + 7) / 8 + 1];
            r.NextBytes(bytes);
            bytes[bytes.Length - 1] = 0;
            BigInteger v = new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
            return (r.Next(2) == 0) ? v : -v;
        }

        static FixedPoint3 Fixed(ExactPoint3 p)
        {
            FixedPoint3 f;
            Assert.True(FixedPoint3.TryCreate(p, out f));
            return f;
        }

        sealed class Input
        {
            public FixedPoint3[] F = new FixedPoint3[12];
            public Vector3d[] D = new Vector3d[12];
            public ulong[] Dir = new ulong[24];
            public int[] DirLen = new int[3];
            public bool[] DirNeg = new bool[3];
            public int I, J;
        }

        static List<Input> Inputs()
        {
            var r = new Random(859);
            var list = new List<Input>();
            for (int k = 0; k < 400; ++k) {
                var inp = new Input();
                int bits = new[] { 3, 20, 63, 64, 100, 200, 255 }[k % 7];
                for (int n = 0; n < 12; ++n) {
                    ExactPoint3 p = (k % 3 == 0)
                        ? ExactPoint3.Create(Random(r, bits), Random(r, bits), Random(r, bits), BigInteger.Abs(Random(r, 1 + bits / 2)) + 1)
                        : ExactPoint3.FromInteger(Random(r, bits), Random(r, bits), Random(r, bits));
                    if (n > 0 && k % 5 == 0) p = (n % 2 == 0) ? ExactPoint3.FromInteger(Random(r, 2), Random(r, 2), 0) : p;     // shared planes, degenerate cases
                    inp.F[n] = Fixed(p);
                    // (small integers: the double entries of the indirect predicates reach their exact stages on degenerate inputs)
                    inp.D[n] = new Vector3d(r.Next(-3, 4), r.Next(-3, 4), (k % 2 == 0) ? 0 : r.Next(-3, 4));
                }
                fixed (ulong* l = inp.Dir)
                    for (int c = 0; c < 3; ++c) {
                        Assert.True(FixedPoint3.LoadN(Random(r, new[] { 1, 64, 200, 450, 511 }[(k + c) % 5]), l + 8 * c, 8, out inp.DirLen[c], out inp.DirNeg[c]));
                    }
                inp.I = k % 3;
                inp.J = (inp.I + 1 + (k / 3) % 2) % 3;
                list.Add(inp);
            }
            return list;
        }

        // the nine members, one wrapper each, results as integers (doubles as their bits)
        delegate void Member(Input x, List<long> trace);

        static readonly Member[] Members = {
            Orient, InCircle, ComparePoints, CompareAlong, TrianglePlanes, LPI, TPI, LPIExact, TPIExact };

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Orient(Input x, List<long> t) { FixedPoint3[] f = x.F; t.Add(FixedArith.Orient(ref f[0], ref f[1], ref f[2], x.I, x.J)); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void InCircle(Input x, List<long> t) { FixedPoint3[] f = x.F; t.Add(FixedArith.InCircle(ref f[0], ref f[1], ref f[2], ref f[3], x.I, x.J)); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ComparePoints(Input x, List<long> t)
        {
            FixedPoint3[] f = x.F;
            t.Add(FixedArith.ComparePoints(ref f[0], ref f[1], 0x9E3779B97F4A7C15UL, 0xBF58476D1CE4E5B9UL, 0x94D049BB133111EBUL));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void CompareAlong(Input x, List<long> t)
        {
            FixedPoint3[] f = x.F;
            fixed (ulong* l = x.Dir)
                t.Add(FixedArith.CompareAlong(ref f[0], ref f[1], new FixedInt(l, x.DirLen[0], x.DirNeg[0]),
                    new FixedInt(l + 8, x.DirLen[1], x.DirNeg[1]), new FixedInt(l + 16, x.DirLen[2], x.DirNeg[2])));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void TrianglePlanes(Input x, List<long> t)
        {
            FixedPoint3[] f = x.F;
            if (!Integer(f, 3)) return;
            var approx = new double[16];
            BigInteger nx, ny, nz, dd;
            int axis, sign;
            FixedArith.TrianglePlanes(ref f[0], ref f[1], ref f[2], approx, out nx, out ny, out nz, out dd, out axis, out sign);
            foreach (double a in approx) t.Add(BitConverter.DoubleToInt64Bits(a));
            Add(t, nx); Add(t, ny); Add(t, nz); Add(t, dd);
            t.Add(axis); t.Add(sign);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void LPI(Input x, List<long> t)
        {
            FixedPoint3[] f = x.F;
            if (Integer(f, 8)) t.Add(IndirectPredicates.Orient3dLPI(ref f[0], ref f[1], ref f[2], ref f[3], ref f[4], ref f[5], ref f[6], ref f[7]));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void TPI(Input x, List<long> t)
        {
            FixedPoint3[] f = x.F;
            if (Integer(f, 12))
                t.Add(IndirectPredicates.Orient3dTPI(ref f[0], ref f[1], ref f[2], ref f[3], ref f[4], ref f[5], ref f[6], ref f[7],
                    ref f[8], ref f[9], ref f[10], ref f[11]));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void LPIExact(Input x, List<long> t)
        {
            Vector3d[] d = x.D;
            t.Add(IndirectPredicates.Orient3dLPIExact(d[0], d[1], d[2], d[3], d[4], d[5], d[6], d[7]));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void TPIExact(Input x, List<long> t)
        {
            Vector3d[] d = x.D;
            t.Add(IndirectPredicates.Orient3dTPIExact(d[0], d[1], d[2], d[3], d[4], d[5], d[6], d[7], d[8], d[9], d[10], d[11]));
        }

        static void Add(List<long> trace, BigInteger v)
        {
            foreach (byte b in v.ToByteArray()) trace.Add(b);
        }

        static bool Integer(FixedPoint3[] f, int n)
        {
            for (int i = 0; i < n; ++i)
                if (!f[i].WIsOne) return false;
            return true;
        }

        [Fact]
        public void SkipLocalsInitMembers_SameResults_OnADirtyStack()
        {
            List<Input> inputs = Inputs();
            List<long> clean = null;
            foreach (byte pattern in new byte[] { 0x00, 0xFF, 0xA5, 0x5A, 0x80, 0x00 }) {
                var trace = new List<long>();
                foreach (Input x in inputs)
                    foreach (Member m in Members) {
                        // (Dirty and the member's wrapper are called from this frame, one after the other: the
                        // wrapper's frame and the member's stack buffers lie in the memory that Dirty just filled)
                        Dirty(pattern);
                        m(x, trace);
                    }
                if (clean == null) { clean = trace; continue; }
                Assert.Equal(clean.Count, trace.Count);
                for (int i = 0; i < clean.Count; ++i)
                    Assert.True(clean[i] == trace[i], "pattern 0x" + pattern.ToString("x2") + ", value " + i);
            }
            Assert.True(clean.Count > 3000, clean.Count.ToString());
        }

        // the results themselves: the indirect predicates against their BigInteger forms (the FixedArith kernels have
        // their oracles in ExactPoint3FixedWidthTests and ExactPlanesFixedWidthTests)
        [Fact]
        public void IndirectPredicates_OnADirtyStack_MatchTheBigIntegerForms()
        {
            foreach (Input x in Inputs()) {
                FixedPoint3[] f = x.F;
                if (!Integer(f, 12)) continue;
                ExactPoint3[] p = new ExactPoint3[12];
                for (int i = 0; i < 12; ++i) p[i] = f[i].ToExact();
                var t = new List<long>();
                Dirty(0xA5);
                LPI(x, t);
                Dirty(0xA5);
                TPI(x, t);
                Assert.Equal(IndirectPredicates.Orient3dLPIBig(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]), (int)t[0]);
                Assert.Equal(IndirectPredicates.Orient3dTPIBig(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8], p[9], p[10], p[11]), (int)t[1]);
            }
        }
    }
}
