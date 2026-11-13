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

        // the nine members, each result as integers (doubles as their bits)
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Run(Input x, List<long> trace)
        {
            FixedPoint3[] f = x.F;
            Vector3d[] d = x.D;
            trace.Add(FixedArith.Orient(ref f[0], ref f[1], ref f[2], x.I, x.J));
            trace.Add(FixedArith.InCircle(ref f[0], ref f[1], ref f[2], ref f[3], x.I, x.J));
            trace.Add(FixedArith.ComparePoints(ref f[0], ref f[1], 0x9E3779B97F4A7C15UL, 0xBF58476D1CE4E5B9UL, 0x94D049BB133111EBUL));
            fixed (ulong* l = x.Dir)
                trace.Add(FixedArith.CompareAlong(ref f[0], ref f[1], new FixedInt(l, x.DirLen[0], x.DirNeg[0]),
                    new FixedInt(l + 8, x.DirLen[1], x.DirNeg[1]), new FixedInt(l + 16, x.DirLen[2], x.DirNeg[2])));
            if (f[0].WIsOne && f[1].WIsOne && f[2].WIsOne) {
                var approx = new double[16];
                BigInteger nx, ny, nz, dd;
                int axis, sign;
                FixedArith.TrianglePlanes(ref f[0], ref f[1], ref f[2], approx, out nx, out ny, out nz, out dd, out axis, out sign);
                foreach (double a in approx) trace.Add(BitConverter.DoubleToInt64Bits(a));
                Add(trace, nx); Add(trace, ny); Add(trace, nz); Add(trace, dd);
                trace.Add(axis); trace.Add(sign);
            }
            if (Integer(f, 8)) {
                trace.Add(IndirectPredicates.Orient3dLPI(ref f[0], ref f[1], ref f[2], ref f[3], ref f[4], ref f[5], ref f[6], ref f[7]));
                if (Integer(f, 12))
                    trace.Add(IndirectPredicates.Orient3dTPI(ref f[0], ref f[1], ref f[2], ref f[3], ref f[4], ref f[5], ref f[6], ref f[7],
                        ref f[8], ref f[9], ref f[10], ref f[11]));
            }
            trace.Add(IndirectPredicates.Orient3dLPIExact(d[0], d[1], d[2], d[3], d[4], d[5], d[6], d[7]));
            trace.Add(IndirectPredicates.Orient3dTPIExact(d[0], d[1], d[2], d[3], d[4], d[5], d[6], d[7], d[8], d[9], d[10], d[11]));
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
                foreach (Input x in inputs) {
                    // (Dirty and Run are called from the same frame: Run's frame and its callees' stack buffers lie in
                    // the memory that Dirty filled)
                    Dirty(pattern);
                    Run(x, trace);
                }
                if (clean == null) { clean = trace; continue; }
                Assert.Equal(clean.Count, trace.Count);
                for (int i = 0; i < clean.Count; ++i)
                    Assert.True(clean[i] == trace[i], "pattern 0x" + pattern.ToString("x2") + ", value " + i);
            }
            Assert.True(clean.Count > 3000, clean.Count.ToString());
        }
    }
}
