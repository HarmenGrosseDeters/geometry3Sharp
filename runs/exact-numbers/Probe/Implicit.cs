// PROTOTYPE P3b (exact-number study; not library code): the arrangement's and the classification's BigInteger stages on
// points beyond the fixed width (ExactBox: homogeneous BigInteger X, Y, Z, W), evaluated with the library's FixedArith
// (sign-magnitude limbs of any length) on the stack instead: the same determinants, so the same signs.
//   "load": the points stay BigInteger (today's storage) and are copied into stack limbs per call (TryWriteBytes)
//   "limbs": the points are stored as limbs (a variable-width point store), no conversion per call
//   micro implicit [n] [reps]
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using g3;

static unsafe class Implicit
{
    struct Pt { public BigInteger X, Y, Z, W; public ulong[] L; public int[] Len; public bool[] Neg; public int Cap; }

    static BigInteger Rand(Random r, int bits, bool positive)
    {
        byte[] b = new byte[(bits + 7) / 8 + 1];
        r.NextBytes(b);
        b[b.Length - 1] = 0;
        int extra = 8 * (b.Length - 1) - bits;
        b[b.Length - 2] &= (byte)(0xFF >> extra);
        b[b.Length - 2] |= (byte)(0x80 >> extra);
        var v = new BigInteger(b);
        return (positive || r.Next(2) == 0) ? v : -v;
    }

    static Pt Make(Random r, int bx, int bw)
    {
        var p = new Pt { X = Rand(r, bx, false), Y = Rand(r, bx, false), Z = Rand(r, bx, false), W = Rand(r, bw, true) };
        p.Cap = (bx + 63) / 64;
        p.L = new ulong[4 * p.Cap]; p.Len = new int[4]; p.Neg = new bool[4];
        fixed (ulong* l = p.L) {
            Load(p.X, l, p.Cap, out p.Len[0], out p.Neg[0]); Load(p.Y, l + p.Cap, p.Cap, out p.Len[1], out p.Neg[1]);
            Load(p.Z, l + 2 * p.Cap, p.Cap, out p.Len[2], out p.Neg[2]); Load(p.W, l + 3 * p.Cap, p.Cap, out p.Len[3], out p.Neg[3]);
        }
        return p;
    }

    // the magnitude of v into at most cap limbs at dst (no allocation)
    [SkipLocalsInit]
    static void Load(BigInteger v, ulong* dst, int cap, out int len, out bool neg)
    {
        neg = v.Sign < 0;
        len = 0;
        if (v.IsZero) return;
        int size = 8 * (cap + 1);
        byte* b = stackalloc byte[size];
        new Span<byte>(b, size).Clear();
        int n;
        if (BigInteger.Abs(v).TryWriteBytes(new Span<byte>(b, size), out n, true, false) == false)
            throw new InvalidOperationException("Implicit.Load: value too wide");
        for (int k = 0; k < cap; ++k) dst[k] = ((ulong*)b)[k];
        len = cap;
        while (len > 0 && dst[len - 1] == 0) len--;
    }

    // (Abs allocates for negative values; a library version would negate the two's complement bytes as FixedPoint3.FromTwos does)
    static FixedInt View(ref Pt p, int k, ulong* l) { return new FixedInt(l + k * p.Cap, p.Len[k], p.Neg[k]); }

    static FixedInt M(FixedInt a, FixedInt b, ref FixedArena ar) { return FixedArith.Mul(a, b, ref ar); }
    static FixedInt S(FixedInt a, FixedInt b, ref FixedArena ar) { return FixedArith.Sub(a, b, ref ar); }
    static FixedInt A(FixedInt a, FixedInt b, ref FixedArena ar) { return FixedArith.Add(a, b, ref ar); }

    // ---- InCircle (ExactMeshArrangement.InCircle's BigInteger stage, projection axes 0 and 1) ----
    static void RowBig(ref Pt v, ref Pt d, out BigInteger x, out BigInteger y, out BigInteger l)
    {
        BigInteger a = v.X * d.W - d.X * v.W, b = v.Y * d.W - d.Y * v.W, s = v.W * d.W;
        x = a * s; y = b * s;
        l = a * a + b * b;
    }
    static int InCircleBig(Pt[] q, int o)
    {
        BigInteger ax, ay, al, bx, by, bl, cx, cy, cl;
        RowBig(ref q[o], ref q[o + 3], out ax, out ay, out al);
        RowBig(ref q[o + 1], ref q[o + 3], out bx, out by, out bl);
        RowBig(ref q[o + 2], ref q[o + 3], out cx, out cy, out cl);
        return ExactPoint3.Det3(ax, ay, al, bx, by, bl, cx, cy, cl).Sign;
    }
    static void RowLimbs(FixedInt px, FixedInt py, FixedInt pw, FixedInt dx, FixedInt dy, FixedInt dw, ref FixedArena ar, out FixedInt x, out FixedInt y, out FixedInt l)
    {
        FixedInt a = S(M(px, dw, ref ar), M(dx, pw, ref ar), ref ar), b = S(M(py, dw, ref ar), M(dy, pw, ref ar), ref ar), s = M(pw, dw, ref ar);
        x = M(a, s, ref ar); y = M(b, s, ref ar);
        l = A(M(a, a, ref ar), M(b, b, ref ar), ref ar);
    }
    [SkipLocalsInit]
    static int InCircleLimbs(Pt[] q, int o, bool load)
    {
        int cap = q[o].Cap;
        ulong* buf = stackalloc ulong[16 * cap];
        int scratch = 320 * cap + 128;
        ulong* sc = stackalloc ulong[scratch];
        var ar = new FixedArena(sc, scratch);
        FixedInt* f = stackalloc FixedInt[12];
        if (load) {
            for (int k = 0; k < 4; ++k) {
                int len; bool neg;
                Load(q[o + k].X, buf + (4 * k) * cap, cap, out len, out neg); f[3 * k] = new FixedInt(buf + (4 * k) * cap, len, neg);
                Load(q[o + k].Y, buf + (4 * k + 1) * cap, cap, out len, out neg); f[3 * k + 1] = new FixedInt(buf + (4 * k + 1) * cap, len, neg);
                Load(q[o + k].W, buf + (4 * k + 3) * cap, cap, out len, out neg); f[3 * k + 2] = new FixedInt(buf + (4 * k + 3) * cap, len, neg);
            }
            return InCircleKernel(f, ref ar);
        }
        fixed (ulong* l0 = q[o].L, l1 = q[o + 1].L, l2 = q[o + 2].L, l3 = q[o + 3].L) {
            f[0] = View(ref q[o], 0, l0); f[1] = View(ref q[o], 1, l0); f[2] = View(ref q[o], 3, l0);
            f[3] = View(ref q[o + 1], 0, l1); f[4] = View(ref q[o + 1], 1, l1); f[5] = View(ref q[o + 1], 3, l1);
            f[6] = View(ref q[o + 2], 0, l2); f[7] = View(ref q[o + 2], 1, l2); f[8] = View(ref q[o + 2], 3, l2);
            f[9] = View(ref q[o + 3], 0, l3); f[10] = View(ref q[o + 3], 1, l3); f[11] = View(ref q[o + 3], 3, l3);
            return InCircleKernel(f, ref ar);
        }
    }
    static int InCircleKernel(FixedInt* f, ref FixedArena ar)
    {
        FixedInt ax, ay, al, bx, by, bl, cx, cy, cl;
        RowLimbs(f[0], f[1], f[2], f[9], f[10], f[11], ref ar, out ax, out ay, out al);
        RowLimbs(f[3], f[4], f[5], f[9], f[10], f[11], ref ar, out bx, out by, out bl);
        RowLimbs(f[6], f[7], f[8], f[9], f[10], f[11], ref ar, out cx, out cy, out cl);
        return FixedArith.Det3Sign(ax, ay, al, bx, by, bl, cx, cy, cl, ref ar);
    }

    // ---- Orient (Det3 of the rows (x, y, W)) ----
    static int OrientBig(Pt[] q, int o)
    {
        return ExactPoint3.Det3(q[o].X, q[o].Y, q[o].W, q[o + 1].X, q[o + 1].Y, q[o + 1].W, q[o + 2].X, q[o + 2].Y, q[o + 2].W).Sign;
    }
    [SkipLocalsInit]
    static int OrientLimbs(Pt[] q, int o, bool load)
    {
        int cap = q[o].Cap;
        ulong* buf = stackalloc ulong[12 * cap];
        int scratch = 40 * cap + 32;
        ulong* sc = stackalloc ulong[scratch];
        var ar = new FixedArena(sc, scratch);
        FixedInt* f = stackalloc FixedInt[9];
        if (load) {
            for (int k = 0; k < 3; ++k) {
                int len; bool neg;
                Load(q[o + k].X, buf + (4 * k) * cap, cap, out len, out neg); f[3 * k] = new FixedInt(buf + (4 * k) * cap, len, neg);
                Load(q[o + k].Y, buf + (4 * k + 1) * cap, cap, out len, out neg); f[3 * k + 1] = new FixedInt(buf + (4 * k + 1) * cap, len, neg);
                Load(q[o + k].W, buf + (4 * k + 3) * cap, cap, out len, out neg); f[3 * k + 2] = new FixedInt(buf + (4 * k + 3) * cap, len, neg);
            }
            return FixedArith.Det3Sign(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], ref ar);
        }
        fixed (ulong* l0 = q[o].L, l1 = q[o + 1].L, l2 = q[o + 2].L) {
            return FixedArith.Det3Sign(View(ref q[o], 0, l0), View(ref q[o], 1, l0), View(ref q[o], 3, l0),
                View(ref q[o + 1], 0, l1), View(ref q[o + 1], 1, l1), View(ref q[o + 1], 3, l1),
                View(ref q[o + 2], 0, l2), View(ref q[o + 2], 1, l2), View(ref q[o + 2], 3, l2), ref ar);
        }
    }

    // ---- plane side: A X + B Y + C Z - D W (the plane in q[o], the point in q[o + 1]) ----
    static int SideBig(Pt[] q, int o)
    {
        return (q[o].X * q[o + 1].X + q[o].Y * q[o + 1].Y + q[o].Z * q[o + 1].Z - q[o].W * q[o + 1].W).Sign;
    }
    [SkipLocalsInit]
    static int SideLimbs(Pt[] q, int o, bool load)
    {
        int cp = q[o].Cap, cq = q[o + 1].Cap;
        ulong* buf = stackalloc ulong[4 * cp + 4 * cq];
        int scratch = 8 * (cp + cq) + 32;
        ulong* sc = stackalloc ulong[scratch];
        var ar = new FixedArena(sc, scratch);
        FixedInt* f = stackalloc FixedInt[8];
        if (load) {
            int len; bool neg;
            Load(q[o].X, buf, cp, out len, out neg); f[0] = new FixedInt(buf, len, neg);
            Load(q[o].Y, buf + cp, cp, out len, out neg); f[1] = new FixedInt(buf + cp, len, neg);
            Load(q[o].Z, buf + 2 * cp, cp, out len, out neg); f[2] = new FixedInt(buf + 2 * cp, len, neg);
            Load(q[o].W, buf + 3 * cp, cp, out len, out neg); f[3] = new FixedInt(buf + 3 * cp, len, neg);
            ulong* b2 = buf + 4 * cp;
            Load(q[o + 1].X, b2, cq, out len, out neg); f[4] = new FixedInt(b2, len, neg);
            Load(q[o + 1].Y, b2 + cq, cq, out len, out neg); f[5] = new FixedInt(b2 + cq, len, neg);
            Load(q[o + 1].Z, b2 + 2 * cq, cq, out len, out neg); f[6] = new FixedInt(b2 + 2 * cq, len, neg);
            Load(q[o + 1].W, b2 + 3 * cq, cq, out len, out neg); f[7] = new FixedInt(b2 + 3 * cq, len, neg);
            return S(A(A(M(f[0], f[4], ref ar), M(f[1], f[5], ref ar), ref ar), M(f[2], f[6], ref ar), ref ar), M(f[3], f[7], ref ar), ref ar).Sign;
        }
        fixed (ulong* l0 = q[o].L, l1 = q[o + 1].L) {
            return S(A(A(M(View(ref q[o], 0, l0), View(ref q[o + 1], 0, l1), ref ar), M(View(ref q[o], 1, l0), View(ref q[o + 1], 1, l1), ref ar), ref ar),
                M(View(ref q[o], 2, l0), View(ref q[o + 1], 2, l1), ref ar), ref ar), M(View(ref q[o], 3, l0), View(ref q[o + 1], 3, l1), ref ar), ref ar).Sign;
        }
    }

    // ---- the unreduced centroid of three points (ExactMeshBoolean.centroid_point): construction; result kept ----
    static BigInteger CentroidBig(Pt[] q, int o)
    {
        BigInteger wbc = q[o + 1].W * q[o + 2].W, wac = q[o].W * q[o + 2].W, wab = q[o].W * q[o + 1].W;
        var p = ExactPoint3.CreateUnreduced(q[o].X * wbc + q[o + 1].X * wac + q[o + 2].X * wab, q[o].Y * wbc + q[o + 1].Y * wac + q[o + 2].Y * wab,
            q[o].Z * wbc + q[o + 1].Z * wac + q[o + 2].Z * wab, 3 * q[o].W * wbc);
        return p.W;
    }
    [SkipLocalsInit]
    static ulong[] CentroidLimbs(Pt[] q, int o)
    {
        int cap = q[o].Cap;
        int scratch = 120 * cap + 64;
        ulong* sc = stackalloc ulong[scratch];
        var ar = new FixedArena(sc, scratch);
        ulong three = 3;
        fixed (ulong* l0 = q[o].L, l1 = q[o + 1].L, l2 = q[o + 2].L) {
            FixedInt aw = View(ref q[o], 3, l0), bw = View(ref q[o + 1], 3, l1), cw = View(ref q[o + 2], 3, l2);
            FixedInt wbc = M(bw, cw, ref ar), wac = M(aw, cw, ref ar), wab = M(aw, bw, ref ar);
            FixedInt x = A(A(M(View(ref q[o], 0, l0), wbc, ref ar), M(View(ref q[o + 1], 0, l1), wac, ref ar), ref ar), M(View(ref q[o + 2], 0, l2), wab, ref ar), ref ar);
            FixedInt y = A(A(M(View(ref q[o], 1, l0), wbc, ref ar), M(View(ref q[o + 1], 1, l1), wac, ref ar), ref ar), M(View(ref q[o + 2], 1, l2), wab, ref ar), ref ar);
            FixedInt z = A(A(M(View(ref q[o], 2, l0), wbc, ref ar), M(View(ref q[o + 1], 2, l1), wac, ref ar), ref ar), M(View(ref q[o + 2], 2, l2), wab, ref ar), ref ar);
            FixedInt w = M(M(new FixedInt(&three, 1, false), aw, ref ar), wbc, ref ar);
            // one array for the four components (a variable-width point: lengths in a header limb)
            ulong[] r = new ulong[1 + x.Len + y.Len + z.Len + w.Len];
            r[0] = (ulong)x.Len | ((ulong)y.Len << 16) | ((ulong)z.Len << 32) | ((ulong)w.Len << 48);
            int k = 1;
            for (int i = 0; i < x.Len; ++i) r[k++] = x.P[i];
            for (int i = 0; i < y.Len; ++i) r[k++] = y.P[i];
            for (int i = 0; i < z.Len; ++i) r[k++] = z.P[i];
            for (int i = 0; i < w.Len; ++i) r[k++] = w.P[i];
            return r;
        }
    }

    public static int Run(string[] args)
    {
        int n = args.Length > 0 ? int.Parse(args[0]) : 4000;
        int reps = args.Length > 1 ? int.Parse(args[1]) : 3;
        // (bits of X / Y / Z, bits of W): the fixed-width limit, three-plane points beyond it, piece centroids (EXACT_BOOLEAN_TAIL_55928.md)
        foreach (var (bx, bw, label) in new[] { (192, 138, "edge-plane points (fixed width today)"), (309, 255, "three-plane points beyond the fixed width"), (807, 753, "piece centroids") }) {
            var rnd = new Random(bx);
            Pt[] q = new Pt[4 * n];
            for (int i = 0; i < q.Length; ++i) q[i] = Make(rnd, bx, bw);
            // planes for the side test: normals up to 101 bits, D up to 156 (stored as X, Y, Z = normal, W = D)
            Pt[] pl = new Pt[2 * n];
            for (int i = 0; i < n; ++i) {
                var p = new Pt { X = Rand(rnd, 101, false), Y = Rand(rnd, 101, false), Z = Rand(rnd, 101, false), W = Rand(rnd, 156, false), Cap = 3, L = new ulong[12], Len = new int[4], Neg = new bool[4] };
                fixed (ulong* l = p.L) {
                    Load(p.X, l, 3, out p.Len[0], out p.Neg[0]); Load(p.Y, l + 3, 3, out p.Len[1], out p.Neg[1]);
                    Load(p.Z, l + 6, 3, out p.Len[2], out p.Neg[2]); Load(p.W, l + 9, 3, out p.Len[3], out p.Neg[3]);
                }
                pl[2 * i] = p; pl[2 * i + 1] = q[4 * i];
            }
            Console.WriteLine($"## {label}: X {bx} bits, W {bw} bits, n = {n}");
            Console.WriteLine("| operation | method | ns / call | B / call | wrong signs |");
            Console.WriteLine("|---|---|---|---|---|");
            Row("InCircle", "BigInteger (today)", n, reps, i => InCircleBig(q, 4 * i), null);
            Row("InCircle", "FixedArith, loaded from BigInteger per call", n, reps, i => InCircleLimbs(q, 4 * i, true), i => InCircleBig(q, 4 * i));
            Row("InCircle", "FixedArith, points stored as limbs", n, reps, i => InCircleLimbs(q, 4 * i, false), i => InCircleBig(q, 4 * i));
            Row("Orient", "BigInteger (today)", n, reps, i => OrientBig(q, 4 * i), null);
            Row("Orient", "FixedArith, loaded from BigInteger per call", n, reps, i => OrientLimbs(q, 4 * i, true), i => OrientBig(q, 4 * i));
            Row("Orient", "FixedArith, points stored as limbs", n, reps, i => OrientLimbs(q, 4 * i, false), i => OrientBig(q, 4 * i));
            Row("plane side", "BigInteger (today)", n, reps, i => SideBig(pl, 2 * i), null);
            Row("plane side", "FixedArith, loaded from BigInteger per call", n, reps, i => SideLimbs(pl, 2 * i, true), i => SideBig(pl, 2 * i));
            Row("plane side", "FixedArith, points stored as limbs", n, reps, i => SideLimbs(pl, 2 * i, false), i => SideBig(pl, 2 * i));
            Row("centroid (construction, result kept)", "BigInteger (today)", n, reps, i => CentroidBig(q, 4 * i).Sign, null);
            Row("centroid (construction, result kept)", "FixedArith, one limb array per point", n, reps, i => (int)(CentroidLimbs(q, 4 * i)[0] & 1) * 0 + 1, null);
            Console.WriteLine();
        }
        return 0;
    }

    static void Row(string op, string method, int n, int reps, Func<int, int> f, Func<int, int> reference)
    {
        int wrong = 0;
        if (reference != null)
            for (int i = 0; i < n; ++i) if (f(i) != reference(i)) wrong++;
        double best = double.MaxValue; long bytes = 0;
        for (int r = 0; r < reps + 1; ++r) {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            int acc = 0;
            for (int i = 0; i < n; ++i) acc += f(i);
            long t1 = Stopwatch.GetTimestamp();
            bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            if (r > 0) best = Math.Min(best, (t1 - t0) * 1e9 / Stopwatch.Frequency / n);
            GC.KeepAlive(acc);
        }
        Console.WriteLine($"| {op} | {method} | {best:F0} | {bytes / (double)n:F0} | {(reference != null ? wrong.ToString() : "")} |");
    }
}
