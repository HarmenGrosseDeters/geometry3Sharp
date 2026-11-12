// Micro benchmarks of the exact-number prototypes against today's fallbacks (exact-number study; not library code).
//   micro rec <rec.bin> [reps]       the recorded *_bigint arguments of a run (PathStats.Record)
//   micro synth [n] [reps]            synthetic classes for all four predicates
// Per method: ns per call, bytes allocated per call, and the number of signs that differ from the BigInteger
// reference (must be 0); for a filter, the share it decides.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using g3;

static unsafe class Micro
{
    delegate int Pred(double* v);

    public static int Run(string[] args)
    {
        if (args.Length >= 2 && args[0] == "rec") {
            int reps = args.Length > 2 ? int.Parse(args[2]) : 3;
            var sets = LoadRec(args[1]);
            foreach (var kv in sets) Bench("rec:" + Path.GetFileName(args[1]), kv.Key, kv.Value, reps);
            return 0;
        }
        if (args.Length >= 1 && args[0] == "construct") return Construct.Run(args.Skip(1).ToArray());
        if (args.Length >= 1 && args[0] == "norm") return NormBench.Run(args.Skip(1).ToArray());
        if (args.Length >= 1 && args[0] == "implicit") return Implicit.Run(args.Skip(1).ToArray());
        if (args.Length >= 1 && args[0] == "synth") {
            int n = args.Length > 1 ? int.Parse(args[1]) : 20000;
            int reps = args.Length > 2 ? int.Parse(args[2]) : 3;
            foreach (string cls in new[] { "inrange-degenerate", "inrange-general", "tiny-uniform", "wide-3scales", "wide-full", "extreme-carry" })
                for (int kind = 1; kind <= 4; ++kind)
                    Bench(cls, kind, Synth(cls, kind, n, 12345 + kind), reps);
            return 0;
        }
        return 2;
    }

    static readonly int[] Arity = { 0, 12, 6, 8, 15 };
    static readonly string[] KindName = { "", "orient3d", "orient2d", "incircle", "insphere" };

    static Dictionary<int, double[]> LoadRec(string path)
    {
        var lists = new Dictionary<int, List<double>>();
        using (var r = new BinaryReader(File.OpenRead(path))) {
            int n = r.ReadInt32();
            double[] row = new double[16];
            for (int i = 0; i < n; ++i) {
                for (int k = 0; k < 16; ++k) row[k] = r.ReadDouble();
                int kind = (int)row[0];
                if (!lists.TryGetValue(kind, out var l)) lists[kind] = l = new List<double>();
                for (int k = 0; k < Arity[kind]; ++k) l.Add(row[1 + k]);
            }
        }
        return lists.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    // ---------------------------------------------------------------- synthetic classes

    static double[] Synth(string cls, int kind, int n, int seed)
    {
        var rnd = new Random(seed);
        int ar = Arity[kind];
        int dim = (kind == 1 || kind == 4) ? 3 : 2;
        int pts = ar / dim;
        double[] v = new double[n * ar];
        // lattice points on a circle / sphere (exactly cocircular / cospherical quintuples)
        var circle = new List<(int, int)>(); var sphere = new List<(int, int, int)>();
        for (int x = -180; x <= 180; ++x) for (int y = -180; y <= 180; ++y) if (x * x + y * y == 32045) circle.Add((x, y));
        for (int x = -34; x <= 34; ++x) for (int y = -34; y <= 34; ++y) for (int z = -34; z <= 34; ++z) if (x * x + y * y + z * z == 1125) sphere.Add((x, y, z));
        for (int i = 0; i < n; ++i) {
            double* p = stackalloc double[16];
            if (cls == "inrange-degenerate" || cls == "tiny-uniform") {
                // exactly degenerate (or one ulp off): small integer combinations, offset and scaled into [2^-20, 2^20]
                double s = Math.ScaleB(1.0, -rnd.Next(4, 14));
                double ox = rnd.Next(-1000, 1000), oy = rnd.Next(-1000, 1000), oz = rnd.Next(-1000, 1000);
                if (kind == 1 || kind == 2) {
                    // points of a lattice plane / line through random integer directions
                    int[] d1 = { rnd.Next(-9, 10), rnd.Next(-9, 10), rnd.Next(-9, 10) }, d2 = { rnd.Next(-9, 10), rnd.Next(-9, 10), rnd.Next(-9, 10) };
                    for (int q = 0; q < pts; ++q) {
                        int a = rnd.Next(-50, 51), b = (kind == 1) ? rnd.Next(-50, 51) : 0;
                        double x = ox + a * d1[0] + b * d2[0], y = oy + a * d1[1] + b * d2[1], z = oz + a * d1[2] + b * d2[2];
                        p[q * dim] = x * s; p[q * dim + 1] = y * s; if (dim == 3) p[q * dim + 2] = z * s;
                    }
                } else if (kind == 3) {
                    for (int q = 0; q < pts; ++q) { var c = circle[rnd.Next(circle.Count)]; p[q * 2] = (ox + c.Item1) * s; p[q * 2 + 1] = (oy + c.Item2) * s; }
                } else {
                    for (int q = 0; q < pts; ++q) { var c = sphere[rnd.Next(sphere.Count)]; p[q * 3] = (ox + c.Item1) * s; p[q * 3 + 1] = (oy + c.Item2) * s; p[q * 3 + 2] = (oz + c.Item3) * s; }
                }
                // a third of the cases one ulp off in one coordinate (nearly degenerate, sign != 0)
                if (rnd.Next(3) == 0) { int k = rnd.Next(ar); p[k] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p[k]) + 1); }
                if (cls == "tiny-uniform") for (int k = 0; k < ar; ++k) p[k] = Math.ScaleB(p[k], -600);
            } else if (cls == "inrange-general") {
                for (int k = 0; k < ar; ++k) p[k] = (rnd.NextDouble() - 0.5) * 200;
            } else if (cls == "wide-3scales") {
                // the #711 pattern: coordinates k * 2^e with a small k (exact degeneracies) or a random mantissa, e from three scales
                int[] scales = { -943, -106, -100 };
                for (int k = 0; k < ar; ++k) {
                    int e = scales[rnd.Next(3)];
                    double m = (rnd.Next(2) == 0) ? rnd.Next(-3, 4) : (rnd.NextDouble() + 1) * (rnd.Next(2) * 2 - 1);
                    p[k] = Math.ScaleB(m, e);
                }
            } else if (cls == "extreme-carry") {
                // the largest mantissas at the ends of the exponent range (full carries, maximal span, many cancellations)
                double[] ext = { double.MaxValue, -double.MaxValue, BitConverter.Int64BitsToDouble(0x001FFFFFFFFFFFFFL), -BitConverter.Int64BitsToDouble(0x001FFFFFFFFFFFFFL),
                    BitConverter.Int64BitsToDouble(0x000FFFFFFFFFFFFFL), -BitConverter.Int64BitsToDouble(0x000FFFFFFFFFFFFFL), double.Epsilon, -double.Epsilon, 0.0, 1.0, -1.0,
                    BitConverter.Int64BitsToDouble(0x3FFFFFFFFFFFFFFFL), BitConverter.Int64BitsToDouble(0x7FE0000000000000L) };
                for (int k = 0; k < ar; ++k) p[k] = ext[rnd.Next(ext.Length)];
            } else {
                // wide-full: any exponent of the double range, subnormals included; small or random mantissas
                for (int k = 0; k < ar; ++k) {
                    int e = rnd.Next(-1074, 1020);
                    double m = (rnd.Next(2) == 0) ? rnd.Next(-3, 4) : (rnd.NextDouble() + 1) * (rnd.Next(2) * 2 - 1);
                    p[k] = Math.ScaleB(m, e);
                    if (rnd.Next(8) == 0 && k >= dim) p[k] = p[k - dim];     // repeated coordinates: structured zeros
                }
            }
            for (int k = 0; k < ar; ++k) v[i * ar + k] = p[k];
        }
        return v;
    }

    // ---------------------------------------------------------------- the methods

    static int BigRef(int kind, double* v)
    {
        switch (kind) {
            case 1: return ExactPredicates.orient3d_bigint(new Vector3d(v[0], v[1], v[2]), new Vector3d(v[3], v[4], v[5]), new Vector3d(v[6], v[7], v[8]), new Vector3d(v[9], v[10], v[11]));
            case 2: return ExactPredicates.orient2d_bigint(new Vector2d(v[0], v[1]), new Vector2d(v[2], v[3]), new Vector2d(v[4], v[5]));
            case 3: return ExactPredicates.incircle_bigint(new Vector2d(v[0], v[1]), new Vector2d(v[2], v[3]), new Vector2d(v[4], v[5]), new Vector2d(v[6], v[7]));
            default: return ExactPredicates.insphere_bigint(new Vector3d(v[0], v[1], v[2]), new Vector3d(v[3], v[4], v[5]), new Vector3d(v[6], v[7], v[8]), new Vector3d(v[9], v[10], v[11]), new Vector3d(v[12], v[13], v[14]));
        }
    }

    // today's exact stage as the library runs it (int path, adaptive expansions or BigInteger, chosen by range)
    static int Today(int kind, double* v)
    {
        switch (kind) {
            case 1: return ExactPredicates.Orient3dExact(new Vector3d(v[0], v[1], v[2]), new Vector3d(v[3], v[4], v[5]), new Vector3d(v[6], v[7], v[8]), new Vector3d(v[9], v[10], v[11]));
            case 2: return ExactPredicates.Orient2dExact(new Vector2d(v[0], v[1]), new Vector2d(v[2], v[3]), new Vector2d(v[4], v[5]));
            case 3: return ExactPredicates.InCircleExact(new Vector2d(v[0], v[1]), new Vector2d(v[2], v[3]), new Vector2d(v[4], v[5]), new Vector2d(v[6], v[7]));
            default: return ExactPredicates.InSphereExact(new Vector3d(v[0], v[1], v[2]), new Vector3d(v[3], v[4], v[5]), new Vector3d(v[6], v[7], v[8]), new Vector3d(v[9], v[10], v[11]), new Vector3d(v[12], v[13], v[14]));
        }
    }

    // the sign factors of the table kernels against the library's conventions (checked by the sign comparison)
    static readonly int[] TableSign = { 0, -1, 1, 1, -1 };

    static int Table(int kind, double* v)
    {
        switch (kind) {
            case 1: return TableSign[1] * Dyadic.Sign(v, 12, Dyadic.Orient3dTerms, 3);
            case 2: return TableSign[2] * Dyadic.Sign(v, 6, Dyadic.Orient2dTerms, 2);
            case 3: return TableSign[3] * Dyadic.Sign(v, 8, Dyadic.InCircleTerms, 4);
            default: return TableSign[4] * Dyadic.Sign(v, 15, Dyadic.InSphereTerms, 5);
        }
    }

    static void Bench(string cls, int kind, double[] data, int reps)
    {
        int ar = Arity[kind], n = data.Length / ar;
        if (n == 0) return;
        int[] reference = new int[n];
        fixed (double* d = data) {
            for (int i = 0; i < n; ++i) reference[i] = BigRef(kind, d + i * ar);
            int zeros = reference.Count(s => s == 0);
            Console.WriteLine($"## {cls} {KindName[kind]}: n = {n}, sign 0 in {100.0 * zeros / n:F1} %");
            Console.WriteLine("| method | ns / call | B / call | wrong signs | decided |");
            Console.WriteLine("|---|---|---|---|---|");
            var methods = new List<(string, Pred, bool)> {
                ("BigInteger (*_bigint, today's fallback)", v => BigRef(kind, v), false),
                ("today's exact stage (int / adaptive / BigInteger by range)", v => Today(kind, v), false),
                ("P2 monomial accumulator, table-driven", v => Table(kind, v), false),
            };
            if (kind == 1) {
                methods.Add(("P2 monomial accumulator, unrolled", v => Dyadic.Orient3d(v), false));
                methods.Add(("P3 FixedArith, variable width on the stack", v => Dense.Orient3d(v), false));
                methods.Add(("P1 extended-exponent expansion", v => ExtExp.Orient3d(v), false));
                methods.Add(("P4 row-scaled double filter", v => ScaledFilter.Orient3d(v), true));
            }
            if (kind == 2) {
                methods.Add(("P2 monomial accumulator, unrolled", v => Dyadic.Orient2d(v), false));
                methods.Add(("P3 FixedArith, variable width on the stack", v => Dense.Orient2d(v), false));
                methods.Add(("P1 extended-exponent expansion", v => ExtExp.Orient2d(v), false));
                methods.Add(("P4 row-scaled double filter", v => ScaledFilter.Orient2d(v), true));
            }
            if (kind == 3) methods.Add(("P3 FixedArith, variable width on the stack", v => Dense.InCircle(v), false));
            if (kind == 4) methods.Add(("P3 FixedArith, variable width on the stack", v => Dense.InSphere(v), false));
            foreach (var (name, f, isFilter) in methods) {
                int wrong = 0, decided = 0;
                bool failed = false;
                try {
                    for (int i = 0; i < n; ++i) {
                        int s = f(d + i * ar);
                        if (isFilter) { if (s != 0) { decided++; if (s != reference[i]) wrong++; } }
                        else if (s != reference[i]) wrong++;
                    }
                } catch (Exception e) { Console.WriteLine($"| {name} | failed: {e.GetType().Name} {e.Message} | | | |"); failed = true; }
                if (failed) continue;
                double best = double.MaxValue; long bytes = 0;
                for (int r = 0; r < reps; ++r) {
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    long t0 = Stopwatch.GetTimestamp();
                    int acc = 0;
                    for (int i = 0; i < n; ++i) acc += f(d + i * ar);
                    long t1 = Stopwatch.GetTimestamp();
                    bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
                    best = Math.Min(best, (t1 - t0) * 1e9 / Stopwatch.Frequency / n);
                    GC.KeepAlive(acc);
                }
                Console.WriteLine($"| {name} | {best:F0} | {bytes / (double)n:F0} | {wrong} | {(isFilter ? (100.0 * decided / n).ToString("F1") + " %" : "")} |");
            }
            Console.WriteLine();
        }
    }
}

// P3: the library's FixedArith (sign-magnitude limbs, any length) on the stack, coordinates aligned to the smallest
// exponent: the BigInteger algorithm of orient3d_bigint without the heap.
static unsafe class Dense
{
    [SkipLocalsInit]
    static void Load(double* v, int n, ulong* buf, FixedInt* x, int w, out bool any)
    {
        ulong* mag = stackalloc ulong[16]; int* ex = stackalloc int[16]; bool* ng = stackalloc bool[16];
        int emin = int.MaxValue;
        for (int i = 0; i < n; ++i) {
            long bits = BitConverter.DoubleToInt64Bits(v[i]);
            int e = (int)((bits >> 52) & 0x7FF);
            ulong f = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (e == 0x7FF) throw new ArgumentException("ExactPredicates: coordinates must be finite");
            if (e == 0) e = 1; else f |= 1UL << 52;
            mag[i] = f; ex[i] = e; ng[i] = bits < 0;
            if (f != 0 && e < emin) emin = e;
        }
        any = emin != int.MaxValue;
        for (int i = 0; i < n; ++i) {
            ulong* p = buf + i * w;
            int len = 0;
            if (mag[i] != 0) {
                int off = ex[i] - emin, k = off >> 6, s = off & 63;
                for (int j = 0; j < k; ++j) p[j] = 0;
                p[k] = mag[i] << s;
                len = k + 1;
                if (s != 0) { ulong hi = mag[i] >> (64 - s); if (hi != 0) { p[k + 1] = hi; len = k + 2; } }
            }
            x[i] = new FixedInt(p, len, ng[i]);
        }
    }

    static int Width(double* v, int n)
    {
        int emin = int.MaxValue, emax = int.MinValue;
        for (int i = 0; i < n; ++i) {
            long bits = BitConverter.DoubleToInt64Bits(v[i]);
            int e = (int)((bits >> 52) & 0x7FF);
            if (e == 0) e = 1;
            if ((bits & 0x7FFFFFFFFFFFFFFFL) != 0) { if (e < emin) emin = e; if (e > emax) emax = e; }
        }
        return (emin == int.MaxValue) ? 1 : ((emax - emin + 53) >> 6) + 2;
    }

    [SkipLocalsInit]
    public static int Orient3d(double* v)
    {
        int w = Width(v, 12);
        ulong* buf = stackalloc ulong[12 * w];
        FixedInt* p = stackalloc FixedInt[12];
        Load(v, 12, buf, p, w, out bool any);
        if (!any) return 0;
        int scratch = 48 * w + 96;
        ulong* sc = stackalloc ulong[scratch];
        var ar = new FixedArena(sc, scratch);
        FixedInt ux = FixedArith.Sub(p[3], p[0], ref ar), uy = FixedArith.Sub(p[4], p[1], ref ar), uz = FixedArith.Sub(p[5], p[2], ref ar);
        FixedInt vx = FixedArith.Sub(p[6], p[0], ref ar), vy = FixedArith.Sub(p[7], p[1], ref ar), vz = FixedArith.Sub(p[8], p[2], ref ar);
        FixedInt wx = FixedArith.Sub(p[9], p[0], ref ar), wy = FixedArith.Sub(p[10], p[1], ref ar), wz = FixedArith.Sub(p[11], p[2], ref ar);
        FixedInt m1 = FixedArith.Sub(FixedArith.Mul(uy, vz, ref ar), FixedArith.Mul(uz, vy, ref ar), ref ar);
        FixedInt m2 = FixedArith.Sub(FixedArith.Mul(uz, vx, ref ar), FixedArith.Mul(ux, vz, ref ar), ref ar);
        FixedInt m3 = FixedArith.Sub(FixedArith.Mul(ux, vy, ref ar), FixedArith.Mul(uy, vx, ref ar), ref ar);
        FixedInt d = FixedArith.Add(FixedArith.Add(FixedArith.Mul(wx, m1, ref ar), FixedArith.Mul(wy, m2, ref ar), ref ar), FixedArith.Mul(wz, m3, ref ar), ref ar);
        return d.Sign;
    }

    static FixedInt M(FixedInt a, FixedInt b, ref FixedArena ar) { return FixedArith.Mul(a, b, ref ar); }
    static FixedInt S(FixedInt a, FixedInt b, ref FixedArena ar) { return FixedArith.Sub(a, b, ref ar); }
    static FixedInt A(FixedInt a, FixedInt b, ref FixedArena ar) { return FixedArith.Add(a, b, ref ar); }

    // as ExactPredicates.incircle_bigint
    [SkipLocalsInit]
    public static int InCircle(double* v)
    {
        int w = Width(v, 8);
        ulong* buf = stackalloc ulong[8 * w];
        FixedInt* p = stackalloc FixedInt[8];
        Load(v, 8, buf, p, w, out bool any);
        if (!any) return 0;
        int scratch = 96 * w + 256;
        ulong* sc = stackalloc ulong[scratch];
        var ar = new FixedArena(sc, scratch);
        FixedInt adx = S(p[0], p[6], ref ar), ady = S(p[1], p[7], ref ar), bdx = S(p[2], p[6], ref ar), bdy = S(p[3], p[7], ref ar), cdx = S(p[4], p[6], ref ar), cdy = S(p[5], p[7], ref ar);
        FixedInt al = A(M(adx, adx, ref ar), M(ady, ady, ref ar), ref ar), bl = A(M(bdx, bdx, ref ar), M(bdy, bdy, ref ar), ref ar), cl = A(M(cdx, cdx, ref ar), M(cdy, cdy, ref ar), ref ar);
        FixedInt d = A(A(M(al, S(M(bdx, cdy, ref ar), M(cdx, bdy, ref ar), ref ar), ref ar),
            M(bl, S(M(cdx, ady, ref ar), M(adx, cdy, ref ar), ref ar), ref ar), ref ar),
            M(cl, S(M(adx, bdy, ref ar), M(bdx, ady, ref ar), ref ar), ref ar), ref ar);
        return d.Sign;
    }

    // as ExactPredicates.insphere_bigint
    [SkipLocalsInit]
    public static int InSphere(double* v)
    {
        int w = Width(v, 15);
        ulong* buf = stackalloc ulong[15 * w];
        FixedInt* p = stackalloc FixedInt[15];
        Load(v, 15, buf, p, w, out bool any);
        if (!any) return 0;
        int scratch = 256 * w + 512;
        ulong* sc = stackalloc ulong[scratch];
        var ar = new FixedArena(sc, scratch);
        FixedInt aex = S(p[0], p[12], ref ar), aey = S(p[1], p[13], ref ar), aez = S(p[2], p[14], ref ar);
        FixedInt bex = S(p[3], p[12], ref ar), bey = S(p[4], p[13], ref ar), bez = S(p[5], p[14], ref ar);
        FixedInt cex = S(p[6], p[12], ref ar), cey = S(p[7], p[13], ref ar), cez = S(p[8], p[14], ref ar);
        FixedInt dex = S(p[9], p[12], ref ar), dey = S(p[10], p[13], ref ar), dez = S(p[11], p[14], ref ar);
        FixedInt ab = S(M(aex, bey, ref ar), M(bex, aey, ref ar), ref ar), bc = S(M(bex, cey, ref ar), M(cex, bey, ref ar), ref ar), cd = S(M(cex, dey, ref ar), M(dex, cey, ref ar), ref ar);
        FixedInt da = S(M(dex, aey, ref ar), M(aex, dey, ref ar), ref ar), ac = S(M(aex, cey, ref ar), M(cex, aey, ref ar), ref ar), bd = S(M(bex, dey, ref ar), M(dex, bey, ref ar), ref ar);
        FixedInt abc = A(S(M(aez, bc, ref ar), M(bez, ac, ref ar), ref ar), M(cez, ab, ref ar), ref ar);
        FixedInt bcd = A(S(M(bez, cd, ref ar), M(cez, bd, ref ar), ref ar), M(dez, bc, ref ar), ref ar);
        FixedInt cda = A(A(M(cez, da, ref ar), M(dez, ac, ref ar), ref ar), M(aez, cd, ref ar), ref ar);
        FixedInt dab = A(A(M(dez, ab, ref ar), M(aez, bd, ref ar), ref ar), M(bez, da, ref ar), ref ar);
        FixedInt al = A(A(M(aex, aex, ref ar), M(aey, aey, ref ar), ref ar), M(aez, aez, ref ar), ref ar), bl = A(A(M(bex, bex, ref ar), M(bey, bey, ref ar), ref ar), M(bez, bez, ref ar), ref ar);
        FixedInt cl = A(A(M(cex, cex, ref ar), M(cey, cey, ref ar), ref ar), M(cez, cez, ref ar), ref ar), dl = A(A(M(dex, dex, ref ar), M(dey, dey, ref ar), ref ar), M(dez, dez, ref ar), ref ar);
        FixedInt det = A(S(M(dl, abc, ref ar), M(cl, dab, ref ar), ref ar), S(M(bl, cda, ref ar), M(al, bcd, ref ar), ref ar), ref ar);
        return -det.Sign;
    }

    [SkipLocalsInit]
    public static int Orient2d(double* v)
    {
        int w = Width(v, 6);
        ulong* buf = stackalloc ulong[6 * w];
        FixedInt* p = stackalloc FixedInt[6];
        Load(v, 6, buf, p, w, out bool any);
        if (!any) return 0;
        int scratch = 12 * w + 32;
        ulong* sc = stackalloc ulong[scratch];
        var ar = new FixedArena(sc, scratch);
        FixedInt ux = FixedArith.Sub(p[2], p[0], ref ar), uy = FixedArith.Sub(p[3], p[1], ref ar);
        FixedInt vx = FixedArith.Sub(p[4], p[0], ref ar), vy = FixedArith.Sub(p[5], p[1], ref ar);
        return FixedArith.Sub(FixedArith.Mul(ux, vy, ref ar), FixedArith.Mul(uy, vx, ref ar), ref ar).Sign;
    }
}

// P4: the double filter of Orient3d / Orient2d after scaling each difference vector by a power of two so that its
// largest component is in [1, 2): the determinant is multiplied by a positive power of two (exact scaling), and the
// generated filter rule of tools/PredicateCompiler (filter_bounds) applies to the scaled differences as leaves inside
// its gate: every scaled difference is 0 or of magnitude in [2^-GateLow, 2], GateLow = 960 / degree - 52, so no
// product underflows. K by the rule (scripts/check_bounds.py). Returns 0 where it does not decide.
static unsafe class ScaledFilter
{
    const double K3 = 8.881784197001258e-16, K2 = 4.440892098500628e-16;
    const double Gate3 = 2.1084395886461046e-81;    // 2^-268
    const double Gate2 = 1.4426529090290212e-129;   // 2^-428

    // 2^-k for the exponent k of m (m > 0 finite); 0 if m is subnormal or not finite, or 2^-k is not a normal double
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double InvPow(double m)
    {
        int e = (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF);
        if (e == 0 || e == 0x7FF) return 0;
        int k = e - 1023;                           // m in [2^k, 2^(k+1))
        if (-k < -1022 || -k > 1023) return 0;
        return BitConverter.Int64BitsToDouble((long)(1023 - k) << 52);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Gate(double x, double g) { double a = Math.Abs(x); return a == 0 || a >= g; }

    public static int Orient3d(double* p)
    {
        double ux = p[3] - p[0], uy = p[4] - p[1], uz = p[5] - p[2];
        double vx = p[6] - p[0], vy = p[7] - p[1], vz = p[8] - p[2];
        double wx = p[9] - p[0], wy = p[10] - p[1], wz = p[11] - p[2];
        double su = InvPow(Math.Max(Math.Abs(ux), Math.Max(Math.Abs(uy), Math.Abs(uz))));
        double sv = InvPow(Math.Max(Math.Abs(vx), Math.Max(Math.Abs(vy), Math.Abs(vz))));
        double sw = InvPow(Math.Max(Math.Abs(wx), Math.Max(Math.Abs(wy), Math.Abs(wz))));
        if (su == 0 || sv == 0 || sw == 0) return 0;
        ux *= su; uy *= su; uz *= su; vx *= sv; vy *= sv; vz *= sv; wx *= sw; wy *= sw; wz *= sw;
        // (a scaled component at or above 2^-268 is a normal double, so its scaling was exact)
        if (!(Gate(ux, Gate3) && Gate(uy, Gate3) && Gate(uz, Gate3) && Gate(vx, Gate3) && Gate(vy, Gate3) && Gate(vz, Gate3)
            && Gate(wx, Gate3) && Gate(wy, Gate3) && Gate(wz, Gate3))) return 0;
        double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;
        double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);
        double permanent = Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy)) + Math.Abs(wy) * (Math.Abs(uzvx) + Math.Abs(uxvz))
            + Math.Abs(wz) * (Math.Abs(uxvy) + Math.Abs(uyvx));
        double bound = K3 * permanent;
        if (det > bound) return 1;
        if (-det > bound) return -1;
        return 0;
    }

    public static int Orient2d(double* p)
    {
        double ux = p[2] - p[0], uy = p[3] - p[1], vx = p[4] - p[0], vy = p[5] - p[1];
        double su = InvPow(Math.Max(Math.Abs(ux), Math.Abs(uy))), sv = InvPow(Math.Max(Math.Abs(vx), Math.Abs(vy)));
        if (su == 0 || sv == 0) return 0;
        ux *= su; uy *= su; vx *= sv; vy *= sv;
        if (!(Gate(ux, Gate2) && Gate(uy, Gate2) && Gate(vx, Gate2) && Gate(vy, Gate2))) return 0;
        double l = ux * vy, r = uy * vx, det = l - r, sum = Math.Abs(l) + Math.Abs(r);
        double bound = K2 * sum;
        if (det > bound) return 1;
        if (-det > bound) return -1;
        return 0;
    }
}
