// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Compares implementations of orient2d / incircle / orient3d against an exact integer reference
// (every double is m * 2^e: all coordinates of a case as integers on a common exponent) on nearly
// and exactly degenerate inputs at several scales, and measures their speed. See README.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using g3;

static class Program
{
    // an implementation: returns the sign (+1, -1, 0); exceptions and NaN count as wrong
    class Impl
    {
        public string Name;
        public Func<double[], int> Orient2d, InCircle, Orient3d;
    }

    static List<Impl> implementations()
    {
        var list = new List<Impl>();
        list.Add(new Impl {
            Name = "g3 ExactPredicates",
            Orient2d = p => ExactPredicates.Orient2d(new Vector2d(p[0], p[1]), new Vector2d(p[2], p[3]), new Vector2d(p[4], p[5])),
            InCircle = p => ExactPredicates.InCircle(new Vector2d(p[0], p[1]), new Vector2d(p[2], p[3]), new Vector2d(p[4], p[5]), new Vector2d(p[6], p[7])),
            Orient3d = p => ExactPredicates.Orient3d(new Vector3d(p[0], p[1], p[2]), new Vector3d(p[3], p[4], p[5]),
                                                     new Vector3d(p[6], p[7], p[8]), new Vector3d(p[9], p[10], p[11])),
        });
        list.Add(new Impl {
            Name = "generated (experiment)",
            Orient2d = p => GeneratedPredicates.Orient2d(p[0], p[1], p[2], p[3], p[4], p[5]),
            InCircle = p => GeneratedPredicates.InCircle(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]),
        });
        list.Add(new Impl {
            Name = "naive double",
            Orient2d = p => Math.Sign((p[2] - p[0]) * (p[5] - p[1]) - (p[3] - p[1]) * (p[4] - p[0])),
            InCircle = p => Math.Sign(incircle_double(p)),
            Orient3d = p => Math.Sign(orient3d_double(p)),
        });
#if TRIANGLENET
        var rp = TriangleNet.RobustPredicates.Default;
        list.Add(new Impl {
            Name = "Triangle.NET RobustPredicates",
            Orient2d = p => Math.Sign(rp.CounterClockwise(new TriangleNet.Geometry.Point(p[0], p[1]),
                new TriangleNet.Geometry.Point(p[2], p[3]), new TriangleNet.Geometry.Point(p[4], p[5]))),
            InCircle = p => Math.Sign(rp.InCircle(new TriangleNet.Geometry.Point(p[0], p[1]), new TriangleNet.Geometry.Point(p[2], p[3]),
                new TriangleNet.Geometry.Point(p[4], p[5]), new TriangleNet.Geometry.Point(p[6], p[7]))),
        });
#endif
        return list;
    }

    static double incircle_double(double[] p)
    {
        double adx = p[0] - p[6], ady = p[1] - p[7], bdx = p[2] - p[6], bdy = p[3] - p[7], cdx = p[4] - p[6], cdy = p[5] - p[7];
        return (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy) + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
            + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
    }

    static double orient3d_double(double[] p)
    {
        double ux = p[3] - p[0], uy = p[4] - p[1], uz = p[5] - p[2], vx = p[6] - p[0], vy = p[7] - p[1], vz = p[8] - p[2];
        double wx = p[9] - p[0], wy = p[10] - p[1], wz = p[11] - p[2];
        return wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);
    }


    // --- exact reference

    static BigInteger[] to_integers(double[] v)
    {
        var mant = new long[v.Length];
        var exp = new int[v.Length];
        int emin = int.MaxValue;
        for (int i = 0; i < v.Length; ++i) {
            long bits = BitConverter.DoubleToInt64Bits(v[i]);
            int e = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = -1074; else { frac |= 1L << 52; e -= 1075; }
            mant[i] = bits < 0 ? -frac : frac;
            exp[i] = e;
            if (frac != 0) emin = Math.Min(emin, e);
        }
        if (emin == int.MaxValue) emin = 0;
        return Enumerable.Range(0, v.Length).Select(i => mant[i] == 0 ? BigInteger.Zero : new BigInteger(mant[i]) << (exp[i] - emin)).ToArray();
    }

    static int orient2d_ref(double[] p)
    {
        var v = to_integers(p);
        return ((v[2] - v[0]) * (v[5] - v[1]) - (v[3] - v[1]) * (v[4] - v[0])).Sign;
    }

    static int incircle_ref(double[] p)
    {
        var v = to_integers(p);
        BigInteger adx = v[0] - v[6], ady = v[1] - v[7], bdx = v[2] - v[6], bdy = v[3] - v[7], cdx = v[4] - v[6], cdy = v[5] - v[7];
        return ((adx * adx + ady * ady) * (bdx * cdy - cdx * bdy) + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
            + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady)).Sign;
    }

    // g3 convention: sign of ((b - a) x (c - a)) . (d - a)
    static int orient3d_ref(double[] p)
    {
        var v = to_integers(p);
        BigInteger ux = v[3] - v[0], uy = v[4] - v[1], uz = v[5] - v[2], vx = v[6] - v[0], vy = v[7] - v[1], vz = v[8] - v[2];
        BigInteger wx = v[9] - v[0], wy = v[10] - v[1], wz = v[11] - v[2];
        return (wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx)).Sign;
    }


    // --- inputs

    static double ulps(double x, int k)
    {
        for (; k > 0; --k) x = Math.BitIncrement(x);
        for (; k < 0; ++k) x = Math.BitDecrement(x);
        return x;
    }

    static double[] perturb(Random r, double[] p, int maxUlps)
    {
        return p.Select(x => ulps(x, r.Next(-maxUlps, maxUlps + 1))).ToArray();
    }

    static double[] scaled(double[] p, double s) { return p.Select(x => x * s).ToArray(); }

    static double U(Random r) { return 2 * r.NextDouble() - 1; }

    // c = a + t (b - a), every coordinate +-4 ulp
    static double[] near_collinear(Random r)
    {
        double ax = U(r), ay = U(r), bx = U(r), by = U(r), t = r.NextDouble();
        return perturb(r, new[] { ax, ay, bx, by, ax + t * (bx - ax), ay + t * (by - ay) }, 4);
    }

    // four points on a circle, +-4 ulp, a, b, c counter-clockwise
    static double[] near_cocircular(Random r)
    {
        double cx = U(r), cy = U(r), rad = 0.5 + 1.5 * r.NextDouble();
        var ang = Enumerable.Range(0, 3).Select(_ => 2 * Math.PI * r.NextDouble()).OrderBy(x => x).ToList();
        ang.Add(2 * Math.PI * r.NextDouble());
        var p = new List<double>();
        foreach (double a in ang) { p.Add(cx + rad * Math.Cos(a)); p.Add(cy + rad * Math.Sin(a)); }
        return perturb(r, p.ToArray(), 4);
    }

    // d = a + u (b - a) + v (c - a), +-4 ulp
    static double[] near_coplanar(Random r)
    {
        var a = new[] { U(r), U(r), U(r) };
        var b = new[] { U(r), U(r), U(r) };
        var c = new[] { U(r), U(r), U(r) };
        double u = r.NextDouble(), v = r.NextDouble();
        var d = Enumerable.Range(0, 3).Select(k => a[k] + u * (b[k] - a[k]) + v * (c[k] - a[k])).ToArray();
        return perturb(r, a.Concat(b).Concat(c).Concat(d).ToArray(), 4);
    }

    // exactly degenerate on an integer grid
    static double[] grid_collinear(Random r)
    {
        int ax = r.Next(-1000, 1000), ay = r.Next(-1000, 1000), dx = r.Next(-20, 21), dy = r.Next(-20, 21);
        int m = r.Next(-50, 50), n = r.Next(-50, 50);
        return new double[] { ax, ay, ax + m * dx, ay + m * dy, ax + n * dx, ay + n * dy };
    }

    static readonly int[,] Circle5 = { { 5, 0 }, { 4, 3 }, { 3, 4 }, { 0, 5 }, { -3, 4 }, { -4, 3 }, { -5, 0 }, { -4, -3 }, { -3, -4 }, { 0, -5 }, { 3, -4 }, { 4, -3 } };

    static double[] grid_cocircular(Random r)
    {
        // indices increase counter-clockwise: three sorted ones are counter-clockwise
        var idx = Enumerable.Range(0, 12).OrderBy(_ => r.Next()).Take(4).ToList();
        var abc = idx.Take(3).OrderBy(i => i).ToList();
        abc.Add(idx[3]);
        int cx = r.Next(-1000, 1000), cy = r.Next(-1000, 1000), k = r.Next(1, 100);
        return abc.SelectMany(i => new double[] { cx + k * Circle5[i, 0], cy + k * Circle5[i, 1] }).ToArray();
    }

    static double[] grid_coplanar(Random r)
    {
        int px = r.Next(-5, 6), py = r.Next(-5, 6), q = r.Next(-1000, 1000);
        var p = new List<double>();
        for (int k = 0; k < 4; ++k) {
            int x = r.Next(-1000, 1000), y = r.Next(-1000, 1000);
            p.Add(x); p.Add(y); p.Add(px * x + py * y + q);
        }
        return p.ToArray();
    }

    // exactly degenerate, but the differences are not exact doubles: multiples 2^k u of one vector are
    // collinear (and cocircular: lifted, they lie on a parabola in a vertical plane), multiples of u and v coplanar
    static double[] pow2_degenerate(Random r, int kind)
    {
        var u = new[] { U(r), U(r), U(r) };
        var v = new[] { U(r), U(r), U(r) };
        int dim = kind == 2 ? 3 : 2, n = kind == 0 ? 3 : 4;
        var p = new List<double>();
        for (int k = 0; k < n; ++k) {
            double s = Math.Pow(2, r.Next(-40, 41));
            var w = (kind == 2 && k % 2 == 1) ? v : u;
            for (int c = 0; c < dim; ++c) p.Add(s * w[c]);
        }
        return p.ToArray();
    }

    // Kettner et al., "Classroom examples of robustness problems in geometric computations": (0.5 + i ulp, 0.5 + j ulp)
    // against the line (12, 12) - (24, 24), against the circle through (23.5, 0.5), (23.5, 23.5), (0.5, 23.5) and
    // against the plane x = y through (12, 12, 12), (24, 24, 24), (0, 0, 1)
    static IEnumerable<double[]> kettner(int kind, int n)
    {
        double u = Math.BitIncrement(0.5) - 0.5;
        for (int i = 0; i < n; ++i)
            for (int j = 0; j < n; ++j) {
                double x = 0.5 + i * u, y = 0.5 + j * u;
                if (kind == 0) yield return new[] { x, y, 12, 12, 24, 24 };
                else if (kind == 1) yield return new[] { 23.5, 0.5, 23.5, 23.5, 0.5, 23.5, x, y };
                else yield return new[] { 12, 12, 12, 24, 24, 24, 0, 0, 1, x, y, 0.5 };
            }
    }


    // --- runs

    static int Count = 20000;

    static void Main(string[] args)
    {
        if (args.Length > 0) Count = int.Parse(args[0]);
        var impls = implementations();
        var scales = new[] { 1.0, 1e6, 1e-10, 1e-160, 1e-300, 1e150, 1e200 };
        Func<double[], int>[] refs = { orient2d_ref, incircle_ref, orient3d_ref };
        string[] names = { "orient2d", "incircle", "orient3d" };
        Func<Random, double[]>[] near = { near_collinear, near_cocircular, near_coplanar };
        Func<Random, double[]>[] grid = { grid_collinear, grid_cocircular, grid_coplanar };

        Console.WriteLine($"wrong signs out of {Count} cases per row (exceptions and NaN count as wrong)");
        Console.WriteLine();
        for (int kind = 0; kind < 3; ++kind) {
            var header = string.Join(" | ", impls.Select(im => im.Name));
            Console.WriteLine($"## {names[kind]}: {header}");
            foreach (string scenario in new[] { "nearly degenerate", "exactly degenerate (grid)", "exactly degenerate (2^k)" }) {
                foreach (double s in scales) {
                    var r = new Random(1234 + kind);
                    Func<Random, double[]> gen = scenario[0] == 'n' ? near[kind] : scenario.EndsWith("(grid)") ? grid[kind] : rr => pow2_degenerate(rr, kind);
                    var cases = Enumerable.Range(0, Count).Select(_ => scaled(gen(r), s)).ToList();
                    report($"{scenario}, scale {s:g3}", cases, refs[kind], impls, kind);
                }
            }
            int side = (int)Math.Sqrt(Count);
            foreach (double s in new[] { 1.0, 1e-160, 1e200 })
                report($"Kettner grid {side}x{side}, scale {s:g3}", kettner(kind, side).Select(p => scaled(p, s)).ToList(), refs[kind], impls, kind);
            Console.WriteLine();
        }

        Console.WriteLine("## time for 200000 calls (ms)");
        var rnd = new Random(99);
        var general = Enumerable.Range(0, 1000).Select(_ => Enumerable.Range(0, 12).Select(__ => U(rnd)).ToArray()).ToList();
        var nearCo = Enumerable.Range(0, 1000).Select(_ => near_cocircular(rnd)).ToList();
        var nearCol = Enumerable.Range(0, 1000).Select(_ => near_collinear(rnd)).ToList();
        var nearPl = Enumerable.Range(0, 1000).Select(_ => near_coplanar(rnd)).ToList();
        var deg = Enumerable.Range(0, 3).Select(k => Enumerable.Range(0, 1000).Select(_ => pow2_degenerate(rnd, k)).ToList()).ToArray();
        foreach (var im in impls) {
            Console.WriteLine($"{im.Name}: orient2d general {time(im.Orient2d, general)}, nearly collinear {time(im.Orient2d, nearCol)}, "
                + $"degenerate (2^k) {time(im.Orient2d, deg[0])}; "
                + $"incircle general {time(im.InCircle, general)}, nearly cocircular {time(im.InCircle, nearCo)}, degenerate (2^k) {time(im.InCircle, deg[1])}; "
                + $"orient3d general {time(im.Orient3d, general)}, nearly coplanar {time(im.Orient3d, nearPl)}, degenerate (2^k) {time(im.Orient3d, deg[2])}");
        }
        direct_timing(general, nearCol, nearCo, deg);
    }

    // hand-written (ExactPredicates) against generated (GeneratedPredicates), called directly in a loop (no delegate,
    // no array per call): ns per call, the median of 7 rounds of 1M calls
    static void direct_timing(List<double[]> general, List<double[]> nearCol, List<double[]> nearCo, List<double[]>[] deg)
    {
        Console.WriteLine();
        Console.WriteLine("## direct calls: ns per call, hand-written ExactPredicates / generated (median of 7 x 1M calls)");
        string row(string label, double[] p, int stride, int hand, int gen) => $"{label,-34} {ns(p, stride, hand):F1} / {ns(p, stride, gen):F1}";
        double[] flat(List<double[]> l, int k) => l.SelectMany(p => p.Take(k)).ToArray();
        double[] g6 = flat(general, 6), g8 = flat(general, 8), c6 = flat(nearCol, 6), c8 = flat(nearCo, 8), d6 = flat(deg[0], 6), d8 = flat(deg[1], 8);
        Console.WriteLine(row("orient2d general", g6, 6, O2H, O2G));
        Console.WriteLine(row("orient2d nearly collinear", c6, 6, O2H, O2G));
        Console.WriteLine(row("orient2d degenerate (2^k)", d6, 6, O2H, O2G));
        Console.WriteLine(row("incircle general", g8, 8, ICH, ICG));
        Console.WriteLine(row("incircle nearly cocircular", c8, 8, ICH, ICG));
        Console.WriteLine(row("incircle degenerate (2^k)", d8, 8, ICH, ICG));
    }

    const int O2H = 0, O2G = 1, ICH = 2, ICG = 3;

    static double ns(double[] p, int stride, int which)
    {
        int n = p.Length / stride;
        const int calls = 1000000;
        var times = new List<double>();
        int sink = 0;
        for (int round = 0; round < 8; ++round) {
            var sw = Stopwatch.StartNew();
            switch (which) {
                case O2H: for (int i = 0, j = 0; i < calls; ++i, j = j + 1 == n ? 0 : j + 1) { int o = j * 6;
                    sink += ExactPredicates.Orient2d(new Vector2d(p[o], p[o + 1]), new Vector2d(p[o + 2], p[o + 3]), new Vector2d(p[o + 4], p[o + 5])); } break;
                case O2G: for (int i = 0, j = 0; i < calls; ++i, j = j + 1 == n ? 0 : j + 1) { int o = j * 6;
                    sink += GeneratedPredicates.Orient2d(p[o], p[o + 1], p[o + 2], p[o + 3], p[o + 4], p[o + 5]); } break;
                case ICH: for (int i = 0, j = 0; i < calls; ++i, j = j + 1 == n ? 0 : j + 1) { int o = j * 8;
                    sink += ExactPredicates.InCircle(new Vector2d(p[o], p[o + 1]), new Vector2d(p[o + 2], p[o + 3]), new Vector2d(p[o + 4], p[o + 5]), new Vector2d(p[o + 6], p[o + 7])); } break;
                default: for (int i = 0, j = 0; i < calls; ++i, j = j + 1 == n ? 0 : j + 1) { int o = j * 8;
                    sink += GeneratedPredicates.InCircle(p[o], p[o + 1], p[o + 2], p[o + 3], p[o + 4], p[o + 5], p[o + 6], p[o + 7]); } break;
            }
            sw.Stop();
            if (round > 0) times.Add(sw.Elapsed.TotalMilliseconds * 1e6 / calls);    // (round 0: warm-up)
        }
        times.Sort();
        return times[times.Count / 2] + (sink == int.MinValue ? 1 : 0);
    }

    static void report(string label, List<double[]> cases, Func<double[], int> reference, List<Impl> impls, int kind)
    {
        var expected = cases.Select(reference).ToArray();
        var cells = new List<string>();
        foreach (var im in impls) {
            var f = kind == 0 ? im.Orient2d : kind == 1 ? im.InCircle : im.Orient3d;
            if (f == null) { cells.Add("-"); continue; }
            int wrong = 0;
            for (int i = 0; i < cases.Count; ++i) {
                int s;
                try { s = f(cases[i]); } catch (Exception) { s = 99; }
                if (s != expected[i]) wrong++;
            }
            cells.Add(wrong.ToString());
        }
        Console.WriteLine($"{label,-42} {string.Join(" | ", cells)}");
    }

    static string time(Func<double[], int> f, List<double[]> cases)
    {
        if (f == null) return "-";
        int sink = 0;
        for (int i = 0; i < 20000; ++i) sink += f(cases[i % cases.Count]);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 200000; ++i) sink += f(cases[i % cases.Count]);
        sw.Stop();
        return sw.ElapsedMilliseconds + (sink == int.MinValue ? "!" : "");
    }
}
