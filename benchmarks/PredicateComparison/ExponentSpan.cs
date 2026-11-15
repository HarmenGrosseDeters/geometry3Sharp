// The exact stage of ExactPredicates beyond the range of the expansions (docs/design/EXACT_FALLBACK_MONOMIALS.md):
// inputs whose filter does not decide and whose coordinates are outside [2^-100, 2^100], so that the stage after the
// adaptive predicates runs. Per class and predicate: ns per call, bytes allocated per call, and the signs that differ
// from a BigInteger reference of this file (must be 0).
//   PredicateComparison span [cases per class]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using g3;

static class ExponentSpan
{
    // v as an integer times 2^-1074
    static BigInteger I(double v)
    {
        long bits = BitConverter.DoubleToInt64Bits(v);
        int e = (int)((bits >> 52) & 0x7FF);
        BigInteger m = bits & 0xFFFFFFFFFFFFFL;
        if (e == 0) e = 1; else m += BigInteger.One << 52;
        m <<= e - 1;
        return bits < 0 ? -m : m;
    }

    static int Orient2dRef(double[] p)
    {
        BigInteger ux = I(p[2]) - I(p[0]), uy = I(p[3]) - I(p[1]), vx = I(p[4]) - I(p[0]), vy = I(p[5]) - I(p[1]);
        return (ux * vy - uy * vx).Sign;
    }

    static int Orient3dRef(double[] p)
    {
        BigInteger[] u = { I(p[3]) - I(p[0]), I(p[4]) - I(p[1]), I(p[5]) - I(p[2]) }, v = { I(p[6]) - I(p[0]), I(p[7]) - I(p[1]), I(p[8]) - I(p[2]) },
            w = { I(p[9]) - I(p[0]), I(p[10]) - I(p[1]), I(p[11]) - I(p[2]) };
        return (u[0] * v[1] * w[2] + u[1] * v[2] * w[0] + u[2] * v[0] * w[1] - u[2] * v[1] * w[0] - u[1] * v[0] * w[2] - u[0] * v[2] * w[1]).Sign;
    }

    static int InCircleRef(double[] p)
    {
        BigInteger ax = I(p[0]) - I(p[6]), ay = I(p[1]) - I(p[7]), bx = I(p[2]) - I(p[6]), by = I(p[3]) - I(p[7]), cx = I(p[4]) - I(p[6]), cy = I(p[5]) - I(p[7]);
        BigInteger al = ax * ax + ay * ay, bl = bx * bx + by * by, cl = cx * cx + cy * cy;
        return (ax * by * cl + ay * bl * cx + al * bx * cy - al * by * cx - ay * bx * cl - ax * bl * cy).Sign;
    }

    static double Scale(double m, int e)
    {
        while (e > 1000) { m *= BitConverter.Int64BitsToDouble((long)(1000 + 1023) << 52); e -= 1000; }
        while (e < -1000) { m *= BitConverter.Int64BitsToDouble((long)(-1000 + 1023) << 52); e += 1000; }
        return m * BitConverter.Int64BitsToDouble((long)(e + 1023) << 52);
    }

    static double[] Case(string cls, int n, Random r)
    {
        double[] v = new double[n];
        if (cls == "uniformly tiny, nearly degenerate") {
            // multiples of a lattice direction, one ulp off, times 2^-600
            int dim = n == 12 ? 3 : 2;
            int[] o = { r.Next(-1000, 1000), r.Next(-1000, 1000), r.Next(-1000, 1000) }, d1 = { r.Next(1, 10), r.Next(-9, 10), r.Next(-9, 10) }, d2 = { r.Next(-9, 10), r.Next(1, 10), r.Next(-9, 10) };
            for (int q = 0; q < n / dim; ++q) {
                int a = r.Next(-50, 51), b = dim == 3 || n == 8 ? r.Next(-50, 51) : 0;
                for (int j = 0; j < dim; ++j) v[q * dim + j] = Scale(o[j] + a * d1[j] + b * d2[j], -600);
            }
            int k = r.Next(n);
            v[k] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v[k]) + 1);
        } else if (cls == "three scales (issue #711)") {
            int[] scales = { -943, -106, -100 };
            for (int k = 0; k < n; ++k) v[k] = Scale((r.Next(2) == 0) ? r.Next(-3, 4) : (r.NextDouble() + 1) * (r.Next(2) * 2 - 1), scales[r.Next(3)]);
        } else {
            for (int k = 0; k < n; ++k) v[k] = Scale((r.Next(2) == 0) ? r.Next(-3, 4) : (r.NextDouble() + 1) * (r.Next(2) * 2 - 1), r.Next(-1074, 1020));
        }
        return v;
    }

    public static void Run(int count)
    {
        var preds = new (string name, int n, Func<double[], int> f, Func<double[], int> reference)[] {
            ("orient2d", 6, p => ExactPredicates.Orient2d(new Vector2d(p[0], p[1]), new Vector2d(p[2], p[3]), new Vector2d(p[4], p[5])), Orient2dRef),
            ("orient3d", 12, p => ExactPredicates.Orient3d(new Vector3d(p[0], p[1], p[2]), new Vector3d(p[3], p[4], p[5]), new Vector3d(p[6], p[7], p[8]), new Vector3d(p[9], p[10], p[11])), Orient3dRef),
            ("incircle", 8, p => ExactPredicates.InCircle(new Vector2d(p[0], p[1]), new Vector2d(p[2], p[3]), new Vector2d(p[4], p[5]), new Vector2d(p[6], p[7])), InCircleRef),
        };
        Console.WriteLine($"ExactPredicates beyond the range of the expansions, {count} cases per row");
        Console.WriteLine("| class | predicate | ns / call | B / call | wrong signs | sign 0 |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (string cls in new[] { "uniformly tiny, nearly degenerate", "three scales (issue #711)", "any exponent" })
            foreach (var pr in preds) {
                var rnd = new Random(77);
                var cases = Enumerable.Range(0, count).Select(_ => Case(cls, pr.n, rnd)).ToList();
                int wrong = 0, zeros = 0;
                foreach (var c in cases) { int e = pr.reference(c); if (pr.f(c) != e) wrong++; if (e == 0) zeros++; }
                double best = double.MaxValue; long bytes = 0;
                for (int rep = 0; rep < 7; ++rep) {
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    var sw = Stopwatch.StartNew();
                    int sink = 0;
                    foreach (var c in cases) sink += pr.f(c);
                    sw.Stop();
                    bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
                    if (rep > 0) best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / count);
                    GC.KeepAlive(sink);
                }
                Console.WriteLine($"| {cls} | {pr.name} | {best:F0} | {bytes / (double)count:F0} | {wrong} | {100.0 * zeros / count:F1} % |");
            }
    }
}
