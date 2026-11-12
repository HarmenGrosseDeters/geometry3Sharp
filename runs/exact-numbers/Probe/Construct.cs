// Micro benchmark (exact-number study): where the time of an exact point construction goes
// (ExactMeshArrangement.exact_ref -> compute_exact -> ExactPoint3.Create -> FixedPoint3.TryCreate), on synthetic
// inputs of the sizes of EXACT_BOOLEAN_TAIL_55928.md: grid points of 56 bits (W = 1), plane normals of 101 bits,
// D of 156 bits. micro construct [n] [reps]
using System;
using System.Diagnostics;
using System.Numerics;
using g3;

static unsafe class Construct
{
    // the same construction with FixedArith on the stack, from limbs (grid points 1 limb, plane 2-3 limbs); the result's
    // lengths are returned (a library version would store the limbs in the FixedPoint3 after the reduction)
    [System.Runtime.CompilerServices.SkipLocalsInit]
    static int ConstructLimbs(ulong* v, bool* neg, int* len)
    {
        ulong* sc = stackalloc ulong[96];
        var ar = new FixedArena(sc, 96);
        FixedInt px = new FixedInt(v, len[0], neg[0]), py = new FixedInt(v + 1, len[1], neg[1]), pz = new FixedInt(v + 2, len[2], neg[2]);
        FixedInt qx = new FixedInt(v + 3, len[3], neg[3]), qy = new FixedInt(v + 4, len[4], neg[4]), qz = new FixedInt(v + 5, len[5], neg[5]);
        FixedInt a = new FixedInt(v + 6, len[6], neg[6]), b = new FixedInt(v + 9, len[7], neg[7]), c = new FixedInt(v + 12, len[8], neg[8]), d = new FixedInt(v + 15, len[9], neg[9]);
        FixedInt sp = FixedArith.Sub(FixedArith.Add(FixedArith.Add(FixedArith.Mul(a, px, ref ar), FixedArith.Mul(b, py, ref ar), ref ar), FixedArith.Mul(c, pz, ref ar), ref ar), d, ref ar);
        FixedInt sq = FixedArith.Sub(FixedArith.Add(FixedArith.Add(FixedArith.Mul(a, qx, ref ar), FixedArith.Mul(b, qy, ref ar), ref ar), FixedArith.Mul(c, qz, ref ar), ref ar), d, ref ar);
        FixedInt x = FixedArith.Sub(FixedArith.Mul(sp, qx, ref ar), FixedArith.Mul(sq, px, ref ar), ref ar);
        FixedInt y = FixedArith.Sub(FixedArith.Mul(sp, qy, ref ar), FixedArith.Mul(sq, py, ref ar), ref ar);
        FixedInt z = FixedArith.Sub(FixedArith.Mul(sp, qz, ref ar), FixedArith.Mul(sq, pz, ref ar), ref ar);
        FixedInt w = FixedArith.Sub(sp, sq, ref ar);
        return x.Len + y.Len + z.Len + w.Len;
    }

    static void LoadMag(BigInteger v, ulong* dst, int cap, out int len, out bool neg)
    {
        neg = v.Sign < 0;
        byte[] b = BigInteger.Abs(v).ToByteArray(true, false);
        for (int k = 0; k < cap; ++k) dst[k] = 0;
        for (int k = 0; k < b.Length; ++k) dst[k / 8] |= (ulong)b[k] << (8 * (k % 8));
        len = cap;
        while (len > 0 && dst[len - 1] == 0) len--;
    }

    static BigInteger Rand(Random r, int bits)
    {
        byte[] b = new byte[(bits + 7) / 8 + 1];
        r.NextBytes(b);
        b[b.Length - 1] = 0;
        int extra = 8 * (b.Length - 1) - bits;
        b[b.Length - 2] &= (byte)(0xFF >> extra);
        b[b.Length - 2] |= (byte)(0x80 >> extra);
        var v = new BigInteger(b);
        return r.Next(2) == 0 ? v : -v;
    }

    public static int Run(string[] args)
    {
        int n = args.Length > 0 ? int.Parse(args[0]) : 20000, reps = args.Length > 1 ? int.Parse(args[1]) : 5;
        var rnd = new Random(11);
        var P = new BigInteger[n][]; var Q = new BigInteger[n][]; var pl = new BigInteger[n][];
        for (int i = 0; i < n; ++i) {
            P[i] = new[] { Rand(rnd, 56), Rand(rnd, 56), Rand(rnd, 56) };
            Q[i] = new[] { Rand(rnd, 56), Rand(rnd, 56), Rand(rnd, 56) };
            // a plane through three grid points (so the construction has the common factors real ones have)
            BigInteger[] a = { Rand(rnd, 56), Rand(rnd, 56), Rand(rnd, 56) }, b = { Rand(rnd, 50), Rand(rnd, 50), Rand(rnd, 50) }, c = { Rand(rnd, 50), Rand(rnd, 50), Rand(rnd, 50) };
            BigInteger nx = b[1] * c[2] - b[2] * c[1], ny = b[2] * c[0] - b[0] * c[2], nz = b[0] * c[1] - b[1] * c[0];
            pl[i] = new[] { nx, ny, nz, nx * a[0] + ny * a[1] + nz * a[2] };
        }
        var x = new BigInteger[n]; var y = new BigInteger[n]; var z = new BigInteger[n]; var w = new BigInteger[n];
        Action<int> construct = i => {
            BigInteger sp = pl[i][0] * P[i][0] + pl[i][1] * P[i][1] + pl[i][2] * P[i][2] - pl[i][3];
            BigInteger sq = pl[i][0] * Q[i][0] + pl[i][1] * Q[i][1] + pl[i][2] * Q[i][2] - pl[i][3];
            x[i] = sp * Q[i][0] - sq * P[i][0]; y[i] = sp * Q[i][1] - sq * P[i][1]; z[i] = sp * Q[i][2] - sq * P[i][2]; w[i] = sp - sq;
        };
        for (int i = 0; i < n; ++i) construct(i);
        long bitsX = 0, bitsW = 0, bitsG = 0, fixedOk = 0;
        for (int i = 0; i < n; ++i) {
            bitsX += (long)x[i].GetBitLength(); bitsW += (long)w[i].GetBitLength();
            bitsG += (long)BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(x[i], y[i]), BigInteger.GreatestCommonDivisor(z[i], w[i])).GetBitLength();
        }
        Console.WriteLine($"edge-plane point: X {bitsX / n} bits, W {bitsW / n} bits, gcd {bitsG / n} bits on average, n = {n}");
        Console.WriteLine("| step | ns / point | B / point |");
        Console.WriteLine("|---|---|---|");
        var pts = new ExactPoint3[n];
        ulong[] limbs = new ulong[18 * n]; bool[] negs = new bool[10 * n]; int[] lens = new int[10 * n];
        fixed (ulong* L = limbs) fixed (bool* N = negs) fixed (int* Ln = lens)
            for (int i = 0; i < n; ++i) {
                for (int k = 0; k < 3; ++k) { LoadMag(P[i][k], L + 18 * i + k, 1, out Ln[10 * i + k], out N[10 * i + k]); LoadMag(Q[i][k], L + 18 * i + 3 + k, 1, out Ln[10 * i + 3 + k], out N[10 * i + 3 + k]); }
                for (int k = 0; k < 4; ++k) LoadMag(pl[i][k], L + 18 * i + 6 + 3 * k, 3, out Ln[10 * i + 6 + k], out N[10 * i + 6 + k]);
            }
        long lensum = 0;
        Row("construction with FixedArith on the stack (from limbs)", n, reps, i => { fixed (ulong* L = limbs) fixed (bool* N = negs) fixed (int* Ln = lens) lensum += ConstructLimbs(L + 18 * i, N + 10 * i, Ln + 10 * i); });
        Row("construction (BigInteger products and sums)", n, reps, construct);
        Row("ExactPoint3.CreateUnreduced", n, reps, i => pts[i] = ExactPoint3.CreateUnreduced(x[i], y[i], z[i], w[i]));
        Row("ExactPoint3.Create (three gcds, four divisions)", n, reps, i => pts[i] = ExactPoint3.Create(x[i], y[i], z[i], w[i]));
        Row("  of it: the three gcds", n, reps, i => { var g = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(x[i], y[i]), BigInteger.GreatestCommonDivisor(z[i], w[i])); if (g.IsZero) throw new Exception(); });
        Row("FixedPoint3.TryCreate of the reduced point", n, reps, i => { FixedPoint3 f; if (FixedPoint3.TryCreate(pts[i], out f)) fixedOk++; });
        Console.WriteLine($"fixed width admits {100.0 * fixedOk / ((reps + 1.0) * n):F1} % of the reduced points");
        return 0;
    }

    static void Row(string name, int n, int reps, Action<int> f)
    {
        double best = double.MaxValue; long bytes = 0;
        for (int r = 0; r < reps + 1; ++r) {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; ++i) f(i);
            long t1 = Stopwatch.GetTimestamp();
            bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            if (r > 0) best = Math.Min(best, (t1 - t0) * 1e9 / Stopwatch.Frequency / n);
        }
        Console.WriteLine($"| {name} | {best:F0} | {bytes / (double)n:F0} |");
    }
}
