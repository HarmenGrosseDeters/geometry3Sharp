// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// IntrTriangle3Triangle3 on coplanar triangles (`dotnet run -c Release -- triangles`): false negatives and false
// positives of Test(), Intersects() and Find() (ReportCoplanarIntersection) against ExactTriangleIntersection, Find()
// results without a contact point, and the time per pair (best of 5). Families as in the tests
// (IntrTriangle3Triangle3CoplanarTests): coplanar slivers at a shared vertex and at a shared edge in three axis planes
// and the plane y = 2 z; the half-integer lattice of #193 in five planes; exactly coplanar pairs in
// oblique planes with full-mantissa coordinates; triangles tilted within 1e-8 of another's plane, touching its edge; random triangles in general position.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

static class CoplanarTriangles
{
    static Vector3d Lift(int plane, double u, double v, double c) =>
        plane == 0 ? new Vector3d(c, u, v) : plane == 1 ? new Vector3d(v, c, u) : plane == 2 ? new Vector3d(u, v, c) : new Vector3d(u, 2 * v, v);

    // the generator of IntrTriangle3Triangle3CoplanarTests.Slivers
    static List<(Triangle3d, Triangle3d)> Slivers(int count, int seed, bool sharedEdge)
    {
        var rnd = new Random(seed);
        var list = new List<(Triangle3d, Triangle3d)>();
        for (int i = 0; i < count; ++i) {
            int plane = i % 4;
            double c = (i % 2 == 0) ? 0 : rnd.NextDouble() * 4 - 2;
            double su = rnd.NextDouble() * 2 - 1, sv = rnd.NextDouble() * 2 - 1;
            double angle = rnd.NextDouble() * 2 * Math.PI, len = 0.05 + rnd.NextDouble() * 0.3;
            double du = Math.Cos(angle), dv = Math.Sin(angle);
            double t = 0.2 + 0.6 * rnd.NextDouble(), h = Math.Pow(2, -(rnd.Next(2) == 0 ? rnd.Next(20, 50) : rnd.Next(50, 58))) * (rnd.Next(2) == 0 ? 1 : -1);
            Vector3d s = Lift(plane, su, sv, c), p = Lift(plane, su + len * du, sv + len * dv, c);
            Vector3d q = Lift(plane, su + t * len * du - h * dv, sv + t * len * dv + h * du, c);
            double off = (rnd.Next(3) == 0 ? Math.Pow(2, -rnd.Next(10, 50)) : rnd.NextDouble() * 0.5) * (rnd.Next(2) == 0 ? 1 : -1);
            double open = (rnd.Next(3) == 0 ? Math.Pow(2, -rnd.Next(20, 50)) : 0.05 + rnd.NextDouble()) * (rnd.Next(2) == 0 ? 1 : -1);
            double a1 = angle + off, a2 = a1 + open, l1 = 0.05 + rnd.NextDouble() * 0.3, l2 = 0.05 + rnd.NextDouble() * 0.3;
            Vector3d u = Lift(plane, su + l1 * Math.Cos(a1), sv + l1 * Math.Sin(a1), c);
            Vector3d w = Lift(plane, su + l2 * Math.Cos(a2), sv + l2 * Math.Sin(a2), c);
            var A = new Triangle3d(s, p, q);
            var B = sharedEdge ? new Triangle3d(p, s, u) : new Triangle3d(u, s, w);
            if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B))
                continue;
            list.Add((A, B));
        }
        return list;
    }

    static List<(Triangle3d, Triangle3d)> Lattice(int count, int seed)
    {
        var rnd = new Random(seed);
        var list = new List<(Triangle3d, Triangle3d)>();
        for (int i = 0; i < count; ++i) {
            int plane = i % 5;
            Func<Vector3d, Vector3d> map = p => plane == 0 ? p : plane == 1 ? new Vector3d(p.x, 0, p.y) : plane == 2 ? new Vector3d(0, p.x, p.y)
                : plane == 3 ? new Vector3d(p.x, p.y, (p.x + p.y) / 2) : new Vector3d(p.x, p.y, p.x + p.y);
            Func<Triangle3d> next = () => {
                Triangle3d t;
                do {
                    double[] c = Enumerable.Range(0, 6).Select(_ => rnd.Next(-16, 17) / 2.0).ToArray();
                    t = new Triangle3d(new Vector3d(c[0], c[1], 0), new Vector3d(c[2], c[3], 0), new Vector3d(c[4], c[5], 0));
                } while (ExactPredicates.Orient2d(t.V0.xy, t.V1.xy, t.V2.xy) == 0);
                return new Triangle3d(map(t.V0), map(t.V1), map(t.V2));
            };
            list.Add((next(), next()));
        }
        return list;
    }

    // the generators of IntrTriangle3Triangle3CoplanarTests.Oblique and TiltedWithinTheEpsilon_TouchingAnEdge
    static List<(Triangle3d, Triangle3d)> Oblique(int count, int seed)
    {
        var rnd = new Random(seed);
        var list = new List<(Triangle3d, Triangle3d)>();
        Func<double> g = () => Math.Round((rnd.NextDouble() * 2 - 1) * (1 << 30)) / (1 << 30);
        for (int i = 0; i < count; ++i) {
            bool first = i % 2 == 0;
            Func<Vector3d> P = () => { double x = g(), y = g(); return first ? new Vector3d(x, y, x + y) : new Vector3d(x, 3 * x - y, y); };
            var A = new Triangle3d(P(), P(), P());
            Vector3d c = A[rnd.Next(3)];
            Func<Vector3d> Q = () => {
                double dx = g() * 0.5, dy = g() * 0.5;
                double x = c.x + dx, y = first ? c.y + dy : c.z + dy;
                return first ? new Vector3d(x, y, x + y) : new Vector3d(x, 3 * x - y, y);
            };
            var B = new Triangle3d(Q(), Q(), Q());
            if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B))
                continue;
            if (Enumerable.Range(0, 3).Any(k => ExactPredicates.Orient3d(A.V0, A.V1, A.V2, B[k]) != 0))
                continue;
            list.Add((A, B));
        }
        return list;
    }

    static List<(Triangle3d, Triangle3d)> Tilted(int count, int seed)
    {
        var rnd = new Random(seed);
        var list = new List<(Triangle3d, Triangle3d)>();
        for (int i = 0; i < count; ++i) {
            double h = Math.Pow(2, -rnd.Next(28, 60));
            Func<double> g = () => rnd.Next(-16, 17) / 8.0;
            double x0 = g(), x1 = g();
            if (x0 == x1) continue;
            var A = new Triangle3d(new Vector3d(x0, 0, 0), new Vector3d(x1, 0, 0), new Vector3d(g(), rnd.Next(1, 9) / 8.0 * (rnd.Next(2) == 0 ? 1 : -1), 0));
            var B = new Triangle3d(new Vector3d(g(), -0.25, -h), new Vector3d(g(), -0.25, -h), new Vector3d(g(), 0.25, h));
            if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B)) continue;
            list.Add((A, B));
        }
        return list;
    }

    static List<(Triangle3d, Triangle3d)> General(int count, int seed)
    {
        var rnd = new Random(seed);
        Func<Vector3d> P = () => new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
        return Enumerable.Range(0, count).Select(_ => (new Triangle3d(P(), P(), P()), new Triangle3d(P(), P(), P()))).ToList();
    }

    static bool Query(int what, Triangle3d a, Triangle3d b, out int quantity)
    {
        quantity = -1;
        if (what == 0) return new IntrTriangle3Triangle3(a, b).Test();
        if (what == 1) return IntrTriangle3Triangle3.Intersects(ref a, ref b);
        var q = new IntrTriangle3Triangle3(a, b) { ReportCoplanarIntersection = true };
        bool r = q.Find();
        quantity = q.Quantity;
        return r;
    }

    static void Report(string name, List<(Triangle3d a, Triangle3d b)> pairs, bool counts)
    {
        string line = $"{name,-24} {pairs.Count,6} pairs";
        if (counts) {
            int[] fn = new int[3], fp = new int[3];
            int noContact = 0;
            foreach (var (a0, b0) in pairs) {
                Triangle3d a = a0, b = b0;
                bool exact = ExactTriangleIntersection.Intersects(ref a, ref b);
                for (int k = 0; k < 3; ++k) {
                    bool got = Query(k, a, b, out int quantity);
                    if (got != exact) { if (exact) fn[k]++; else fp[k]++; }
                    if (k == 2 && got && quantity == 0) noContact++;
                }
            }
            line += $" | false neg. {fn[0]} / {fn[1]} / {fn[2]} | false pos. {fp[0]} / {fp[1]} / {fp[2]} | Find without contact {noContact}";
        }
        line += " | ns per pair";
        for (int k = 0; k < 3; ++k) {
            long best = long.MaxValue;
            for (int r = 0; r < 5; ++r) {
                var sw = Stopwatch.StartNew();
                foreach (var (a, b) in pairs)
                    Query(k, a, b, out int quantity);
                best = Math.Min(best, sw.ElapsedTicks);
            }
            line += (k == 0 ? " " : " / ") + (best * 1e9 / Stopwatch.Frequency / pairs.Count).ToString("F0");
        }
        Console.WriteLine(line);
    }

    public static void Run()
    {
        Console.WriteLine("Test / Intersects / Find (ReportCoplanarIntersection) against ExactTriangleIntersection.Intersects");
        Report("slivers, shared vertex", Slivers(20000, 21, false), true);
        Report("slivers, shared edge", Slivers(20000, 22, true), true);
        Report("half-integer lattice", Lattice(20000, 7), true);
        Report("oblique planes", Oblique(40000, 25), true);
        Report("tilted within 1e-8", Tilted(20000, 26), true);
        Report("general position", General(20000, 3), false);
    }
}
