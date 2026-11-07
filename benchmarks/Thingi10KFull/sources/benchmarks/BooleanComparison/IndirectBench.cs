// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Per-call times of the generated indirect predicates (docs/design/INDIRECT_PREDICATES.md):
//   BooleanComparison indirect [rounds]
// Orient3d of a line-plane point (LPI) and of a three-plane point (TPI) against three points, on integer inputs of
// 30 bits: general ones (random points; the filter decides) and degenerate ones (the point on the plane a, b, c: the sign is 0, the filter fails and the exact stage decides):
//   construct: the point as a reduced rational (BigInteger, gcd, as the arrangement constructs its points), then
//              orient3d in BigInteger
//   exact:     the generated exact stage on FixedArith, from the doubles
//   filtered:  the generated entry: filter, then the exact stage
// The median of the rounds, ns per call; every contender's sign is checked against the others.
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using g3;

static class IndirectBench
{
    public static int Run(string[] args)
    {
        int rounds = args.Length > 0 ? int.Parse(args[0]) : 7;
        var rand = new Random(2026);
        const int N = 2000;
        foreach (var kind in new[] { (false, false), (true, false), (false, true), (true, true) }) {
            bool degenerate = kind.Item1, general = kind.Item2;
            var L = new Vector3d[N][];
            var T = new Vector3d[N][];
            for (int k = 0; k < N; ++k) {
                // integer points of 30 bits, or general doubles in [-1, 1] (full mantissas, as the envelope's)
                var P = Enumerable.Range(0, 12).Select(_ => general ? new Vector3d(rand.NextDouble() * 2 - 1, rand.NextDouble() * 2 - 1, rand.NextDouble() * 2 - 1)
                    : new Vector3d(r30(rand), r30(rand), r30(rand))).ToArray();
                L[k] = P.Take(8).ToArray();
                T[k] = (Vector3d[])P.Clone();
                if (degenerate) {
                    // a, b, c = the plane r, s, t (LPI) / the plane of the first triangle (TPI): the point lies on it
                    L[k][5] = P[2]; L[k][6] = P[3]; L[k][7] = P[4];
                    T[k][9] = P[0]; T[k][10] = P[1]; T[k][11] = P[2];
                }
            }
            // signs agree
            for (int k = 0; k < N; ++k) {
                var l = L[k]; var t = T[k];
                int a = IndirectPredicates.Orient3dLPI(l[0], l[1], l[2], l[3], l[4], l[5], l[6], l[7]);
                if (a != IndirectPredicates.Orient3dLPIExact(l[0], l[1], l[2], l[3], l[4], l[5], l[6], l[7]) || a != ConstructLPI(l))
                    throw new Exception("LPI signs differ");
                int b = IndirectPredicates.Orient3dTPI(t[0], t[1], t[2], t[3], t[4], t[5], t[6], t[7], t[8], t[9], t[10], t[11]);
                if (b != IndirectPredicates.Orient3dTPIExact(t[0], t[1], t[2], t[3], t[4], t[5], t[6], t[7], t[8], t[9], t[10], t[11]) || b != ConstructTPI(t))
                    throw new Exception("TPI signs differ");
            }
            string kindName = (general ? "doubles, " : "30 bits, ") + (degenerate ? "degenerate" : "general");
            int fl = 0, ft = 0;
            for (int k = 0; k < N; ++k) {
                var l = L[k]; var t = T[k];
                if (IndirectPredicates.Orient3dLPIFilter(l[0], l[1], l[2], l[3], l[4], l[5], l[6], l[7]) != 2) fl++;
                if (IndirectPredicates.Orient3dTPIFilter(t[0], t[1], t[2], t[3], t[4], t[5], t[6], t[7], t[8], t[9], t[10], t[11]) != 2) ft++;
            }
            Console.WriteLine($"{kindName}: the filter decides {100.0 * fl / N:F1} % (LPI), {100.0 * ft / N:F1} % (TPI)");
            Report($"LPI {kindName}", rounds,
                ("construct", () => { long s = 0; foreach (var l in L) s += ConstructLPI(l); return s; }),
                ("exact", () => { long s = 0; foreach (var l in L) s += IndirectPredicates.Orient3dLPIExact(l[0], l[1], l[2], l[3], l[4], l[5], l[6], l[7]); return s; }),
                ("filtered", () => { long s = 0; foreach (var l in L) s += IndirectPredicates.Orient3dLPI(l[0], l[1], l[2], l[3], l[4], l[5], l[6], l[7]); return s; }), N);
            Report($"TPI {kindName}", rounds,
                ("construct", () => { long s = 0; foreach (var t in T) s += ConstructTPI(t); return s; }),
                ("exact", () => { long s = 0; foreach (var t in T) s += IndirectPredicates.Orient3dTPIExact(t[0], t[1], t[2], t[3], t[4], t[5], t[6], t[7], t[8], t[9], t[10], t[11]); return s; }),
                ("filtered", () => { long s = 0; foreach (var t in T) s += IndirectPredicates.Orient3dTPI(t[0], t[1], t[2], t[3], t[4], t[5], t[6], t[7], t[8], t[9], t[10], t[11]); return s; }), N);
        }
        return 0;
    }

    static double r30(Random r) => r.Next(-(1 << 29), 1 << 29) * 2.0 + r.Next(2);

    static void Report(string name, int rounds, (string, Func<long>) a, (string, Func<long>) b, (string, Func<long>) c, int n)
    {
        var all = new[] { a, b, c };
        var times = all.Select(_ => new double[rounds]).ToArray();
        foreach (var x in all) x.Item2();       // warm-up (JIT)
        for (int r = 0; r < rounds; ++r)
            for (int i = 0; i < all.Length; ++i) {
                var sw = Stopwatch.StartNew();
                int reps = 0;
                while (sw.ElapsedMilliseconds < 200) { all[i].Item2(); reps++; }
                times[i][r] = sw.Elapsed.TotalMilliseconds * 1e6 / (reps * (double)n);
            }
        Console.WriteLine($"{name,-32} " + string.Join("  ", all.Select((x, i) => $"{x.Item1} {median(times[i]),8:F0} ns")));
    }

    static double median(double[] v) { var s = v.OrderBy(x => x).ToArray(); return s[s.Length / 2]; }

    // the coordinates as integers: times 2^60 (exact for the general rows: fl(2 NextDouble() - 1) is a multiple of
    // 2^-53, so every exponent e >= -53 > -60; the integer rows stay integers; the sign cross-check would fail otherwise)
    static BigInteger[] B(Vector3d p) => new[] { I(p.x), I(p.y), I(p.z) };
    static BigInteger I(double v)
    {
        BigInteger m; int e;
        ExactPoint3.Decompose(v, out m, out e);
        return (e + 60 >= 0) ? m << (e + 60) : m >> -(e + 60);
    }
    static BigInteger[] Sub(BigInteger[] a, BigInteger[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    static BigInteger[] Cross(BigInteger[] u, BigInteger[] v) => new[] { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
    static BigInteger Dot(BigInteger[] u, BigInteger[] v) => u[0] * v[0] + u[1] * v[1] + u[2] * v[2];

    // orient3d(a, b, c, x) of the reduced rational point x = N / den, in BigInteger
    static int Orient(BigInteger[] N, BigInteger den, Vector3d a, Vector3d b, Vector3d c)
    {
        if (den.IsZero) return 0;
        var x = ExactPoint3.Create(N[0], N[1], N[2], den);
        BigInteger[] C = B(c), row = { x.X - C[0] * x.W, x.Y - C[1] * x.W, x.Z - C[2] * x.W };
        BigInteger[] u = Sub(B(a), C), v = Sub(B(b), C);
        return (row[0] * (u[1] * v[2] - u[2] * v[1]) - row[1] * (u[0] * v[2] - u[2] * v[0]) + row[2] * (u[0] * v[1] - u[1] * v[0])).Sign;
    }

    static int ConstructLPI(Vector3d[] P)
    {
        BigInteger[] p = B(P[0]), q = B(P[1]), r = B(P[2]);
        var n = Cross(Sub(B(P[3]), r), Sub(B(P[4]), r));
        BigInteger lp = Dot(n, Sub(p, r)), lq = Dot(n, Sub(q, r)), d = lq - lp;
        return Orient(new[] { p[0] * lq - q[0] * lp, p[1] * lq - q[1] * lp, p[2] * lq - q[2] * lp }, d, P[5], P[6], P[7]);
    }

    static int ConstructTPI(Vector3d[] P)
    {
        var n = new BigInteger[3][]; var D = new BigInteger[3];
        for (int k = 0; k < 3; ++k) { var a = B(P[3 * k]); n[k] = Cross(Sub(B(P[3 * k + 1]), a), Sub(B(P[3 * k + 2]), a)); D[k] = Dot(n[k], a); }
        BigInteger[] e23 = Cross(n[1], n[2]), e31 = Cross(n[2], n[0]), e12 = Cross(n[0], n[1]);
        BigInteger d = Dot(n[0], e23);
        return Orient(new[] { D[0] * e23[0] + D[1] * e31[0] + D[2] * e12[0], D[0] * e23[1] + D[1] * e31[1] + D[2] * e12[1],
            D[0] * e23[2] + D[1] * e31[2] + D[2] * e12[2] }, d, P[9], P[10], P[11]);
    }
}
