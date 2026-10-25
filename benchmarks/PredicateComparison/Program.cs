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
            foreach (string scenario in new[] { "nearly degenerate", "exactly degenerate (grid)" }) {
                foreach (double s in scales) {
                    var r = new Random(1234 + kind);
                    var cases = Enumerable.Range(0, Count).Select(_ => scaled(scenario[0] == 'n' ? near[kind](r) : grid[kind](r), s)).ToList();
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
        foreach (var im in impls) {
            Console.WriteLine($"{im.Name}: orient2d general {time(im.Orient2d, general)}, nearly collinear {time(im.Orient2d, nearCol)}; "
                + $"incircle general {time(im.InCircle, general)}, nearly cocircular {time(im.InCircle, nearCo)}; "
                + $"orient3d general {time(im.Orient3d, general)}, nearly coplanar {time(im.Orient3d, nearPl)}");
        }
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
