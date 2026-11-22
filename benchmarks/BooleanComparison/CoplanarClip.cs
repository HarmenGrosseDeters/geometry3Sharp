// Research spike (docs/research/BOOLEAN_CERTIFIED_MODE_2026-11.md, part C): the intersection polygon of two coplanar
// triangles. An exact clip (rational coordinates, a prototype) is the reference; against it the floating-point clip of
// IntrTriangle2Triangle2 (unchanged from WildMagic5) and the coplanar route of IntrTriangle3Triangle3.Find (exact
// decisions, crossing points rounded from exact determinants) are compared on procedural pairs: the decision, the
// number of corners, the coordinates, and the time.
//
// Usage: BooleanComparison coplanar-clip [pairs per family]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using g3;

static class CoplanarClip
{
    // a point of the plane with rational coordinates X / W, Y / W (W > 0), reduced: equal points are equal structs
    struct Q2 : IEquatable<Q2>
    {
        public BigInteger X, Y, W;
        public Q2(BigInteger x, BigInteger y, BigInteger w)
        {
            if (w.Sign < 0) { x = -x; y = -y; w = -w; }
            BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(x, y), w);
            if (g.IsOne == false) { x /= g; y /= g; w /= g; }
            X = x; Y = y; W = w;
        }
        public bool Equals(Q2 o) => X == o.X && Y == o.Y && W == o.W;
        public override bool Equals(object o) => o is Q2 q && Equals(q);
        public override int GetHashCode() => X.GetHashCode() * 31 + Y.GetHashCode();
        public static Q2 From(Vector2d v)
        {
            ExactPoint3.Decompose(v.x, out BigInteger mx, out int ex); ExactPoint3.Decompose(v.y, out BigInteger my, out int ey);
            int lo = Math.Min(0, Math.Min(ex, ey));
            return new Q2(mx << (ex - lo), my << (ey - lo), BigInteger.One << -lo);
        }
        public Vector2d Rounded() => new Vector2d(ExactPoint3.ToDouble(X, W, 0), ExactPoint3.ToDouble(Y, W, 0));
        public long Bits => Math.Max(ExactPoint3.BitLength(X), Math.Max(ExactPoint3.BitLength(Y), ExactPoint3.BitLength(W)));
    }

    // the sign of (b - a) x (c - a)
    static BigInteger Cross(Q2 a, Q2 b, Q2 c) =>
        (b.X * a.W - a.X * b.W) * (c.Y * a.W - a.Y * c.W) - (b.Y * a.W - a.Y * b.W) * (c.X * a.W - a.X * c.W);

    /// <summary>
    /// The intersection of the triangles t0 and t1 (counter-clockwise, not degenerate) as a convex polygon with exact
    /// corners, in order: t0 clipped by the three half-planes of t1 (Sutherland and Hodgman 1974), every sign and every
    /// crossing point exact. 0 corners: disjoint; 1: a point; 2: a segment.
    /// </summary>
    static List<Q2> ExactClip(Q2[] t0, Q2[] t1)
    {
        var poly = new List<Q2>(t0);
        for (int e = 0; e < 3 && poly.Count > 0; ++e) {
            Q2 c = t1[e], d = t1[(e + 1) % 3];
            var next = new List<Q2>();
            for (int i = 0; i < poly.Count; ++i) {
                Q2 p = poly[i], q = poly[(i + 1) % poly.Count];
                BigInteger sp = Cross(c, d, p), sq = Cross(c, d, q);
                if (sp.Sign >= 0) next.Add(p);
                if (sp.Sign * sq.Sign < 0) {
                    // p + sp / (sp - sq) (q - p): Cross is scaled by c.W^2 d.W times the point's W, so the weights are
                    // sp q.W and sq p.W
                    BigInteger a = sp * q.W, b = sq * p.W, den = a - b;
                    next.Add(new Q2((a * q.X * p.W - b * p.X * q.W), (a * q.Y * p.W - b * p.Y * q.W), den * p.W * q.W));
                }
            }
            // equal neighbours (a corner on the clipping line is kept once)
            poly = new List<Q2>();
            foreach (Q2 x in next) if (poly.Count == 0 || poly[poly.Count - 1].Equals(x) == false) poly.Add(x);
            if (poly.Count > 1 && poly[0].Equals(poly[poly.Count - 1])) poly.RemoveAt(poly.Count - 1);
            // a polygon that collapsed onto the clipping line: its two extreme points
            if (poly.Count > 2 && poly.All(x => Cross(c, d, x).IsZero)) {
                var distinct = poly.Distinct().ToList();
                Q2 lo = distinct[0], hi = distinct[0];
                foreach (Q2 x in distinct) { if (Less(x, lo)) lo = x; if (Less(hi, x)) hi = x; }
                poly = lo.Equals(hi) ? new List<Q2> { lo } : new List<Q2> { lo, hi };
            }
        }
        return poly;
    }

    static bool Less(Q2 a, Q2 b)
    {
        int c = (a.X * b.W).CompareTo(b.X * a.W);
        return c != 0 ? c < 0 : (a.Y * b.W).CompareTo(b.Y * a.W) < 0;
    }

    sealed class Family { public string Name; public Func<Random, Vector2d> Point; public Func<Random, Triangle2d[]> Pair; }

    static Triangle2d Ccw(Vector2d a, Vector2d b, Vector2d c) => ExactPredicates.Orient2d(a, b, c) > 0 ? new Triangle2d(a, b, c) : new Triangle2d(a, c, b);

    static List<Family> Families()
    {
        Func<Func<Random, Vector2d>, Func<Random, Triangle2d[]>> independent = pt => rng => new[] { Ccw(pt(rng), pt(rng), pt(rng)), Ccw(pt(rng), pt(rng), pt(rng)) };
        return new List<Family> {
            new Family { Name = "lattice 0..4 (shared corners, collinear edges)", Pair = independent(r => new Vector2d(r.Next(5), r.Next(5))) },
            new Family { Name = "lattice 0..16", Pair = independent(r => new Vector2d(r.Next(17), r.Next(17))) },
            new Family { Name = "uniform doubles in [0, 1)", Pair = independent(r => new Vector2d(r.NextDouble(), r.NextDouble())) },
            new Family { Name = "uniform doubles in [0, 1) + 1e6", Pair = independent(r => new Vector2d(1e6 + r.NextDouble(), 1e6 + r.NextDouble())) },
            // a triangle and a copy turned by a small angle about a corner: nearly parallel edges
            new Family { Name = "a triangle and its copy turned by 1e-9 about a corner", Pair = r => {
                Vector2d a = new Vector2d(r.NextDouble(), r.NextDouble()), b = new Vector2d(r.NextDouble(), r.NextDouble()), c = new Vector2d(r.NextDouble(), r.NextDouble());
                double s = 1e-9 * (r.NextDouble() + 0.5);
                Func<Vector2d, Vector2d> turn = p => a + new Vector2d((p.x - a.x) - s * (p.y - a.y), s * (p.x - a.x) + (p.y - a.y));
                return new[] { Ccw(a, b, c), Ccw(a, turn(b), turn(c)) };
            } },
            // two triangles with a common edge line: one corner of the second on an edge of the first (in doubles: nearly)
            new Family { Name = "a corner at the midpoint of the other's edge (T-junction)", Pair = r => {
                Vector2d a = new Vector2d(r.NextDouble(), r.NextDouble()), b = new Vector2d(r.NextDouble(), r.NextDouble()), c = new Vector2d(r.NextDouble(), r.NextDouble());
                Vector2d m = 0.5 * (a + b);
                return new[] { Ccw(a, b, c), Ccw(m, new Vector2d(r.NextDouble(), r.NextDouble()), new Vector2d(r.NextDouble(), r.NextDouble())) };
            } },
        };
    }

    // the largest distance from a point of a to the nearest point of b, in units of ulp(scale)
    static double Deviation(IList<Vector2d> a, IList<Vector2d> b, double scale)
    {
        // (the next double above scale, by its bits: Math.BitIncrement is not in net48)
        double ulp = scale > 0 ? BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(scale) + 1) - scale : double.Epsilon, worst = 0;
        foreach (Vector2d p in a) {
            double best = double.MaxValue;
            foreach (Vector2d q in b) best = Math.Min(best, Math.Max(Math.Abs(p.x - q.x), Math.Abs(p.y - q.y)));
            worst = Math.Max(worst, best);
        }
        return worst / ulp;
    }

    sealed class Stats
    {
        public int Decision, Count, Beyond1, NotEqual; public double MaxUlps; public double Ms;
        public void Add(bool found, IList<Vector2d> pts, List<Vector2d> exact, double scale)
        {
            if (found != exact.Count > 0) { Decision++; return; }
            if (found == false) return;
            var distinct = pts.Distinct().ToList();
            if (distinct.Count != exact.Count) Count++;
            double d = Math.Max(Deviation(distinct, exact, scale), Deviation(exact, distinct, scale));
            if (d > 0) NotEqual++;
            if (d > 1) Beyond1++;
            MaxUlps = Math.Max(MaxUlps, d);
        }
        public override string ToString() => $"{Decision} | {Count} | {NotEqual} | {Beyond1} | {MaxUlps.ToString("G3", CultureInfo.InvariantCulture)} | {Ms.ToString("0.0", CultureInfo.InvariantCulture)}";
    }

    public static int Run(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        int n = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 20000;
        Console.WriteLine($"# {n} pairs per family; per method: wrong decisions | wrong corner counts | results not equal to the rounded exact corners | farther than 1 ulp | largest deviation (ulps of the largest coordinate) | ms for all pairs");
        Console.WriteLine("family | intersecting | point or segment | exact: ms, max bits | IntrTriangle2Triangle2.Find | IntrTriangle3Triangle3.Find (coplanar)");
        foreach (Family f in Families()) {
            var rng = new Random(20261122);
            var pairs = new List<Triangle2d[]>();
            while (pairs.Count < n) {
                Triangle2d[] p = f.Pair(rng);
                if (ExactPredicates.Orient2d(p[0].V0, p[0].V1, p[0].V2) != 0 && ExactPredicates.Orient2d(p[1].V0, p[1].V1, p[1].V2) != 0) pairs.Add(p);
            }
            // warm-up, then one timed pass per method
            var exact = new List<List<Vector2d>>(); long maxBits = 0; double exactMs = 0;
            for (int pass = 0; pass < 2; ++pass) {
                exact.Clear();
                var sw = Stopwatch.StartNew();
                foreach (Triangle2d[] p in pairs) {
                    List<Q2> e = ExactClip(new[] { Q2.From(p[0].V0), Q2.From(p[0].V1), Q2.From(p[0].V2) }, new[] { Q2.From(p[1].V0), Q2.From(p[1].V1), Q2.From(p[1].V2) });
                    foreach (Q2 x in e) maxBits = Math.Max(maxBits, x.Bits);
                    exact.Add(e.Select(x => x.Rounded()).Distinct().ToList());
                }
                exactMs = sw.Elapsed.TotalMilliseconds;
            }
            Stats s2 = new Stats(), s3 = new Stats();
            for (int pass = 0; pass < 2; ++pass) {
                s2 = new Stats(); s3 = new Stats();
                var found2 = new bool[n]; var pts2 = new Vector2d[n][]; var found3 = new bool[n]; var pts3 = new Vector2d[n][];
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < n; ++i) {
                    var intr = new IntrTriangle2Triangle2(pairs[i][0], pairs[i][1]);
                    found2[i] = intr.Find();
                    pts2[i] = found2[i] ? intr.Points.Take(intr.Quantity).ToArray() : null;
                }
                s2.Ms = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                for (int i = 0; i < n; ++i) {
                    Triangle2d a = pairs[i][0], b = pairs[i][1];
                    var intr = new IntrTriangle3Triangle3(new Triangle3d(new Vector3d(a.V0.x, a.V0.y, 0), new Vector3d(a.V1.x, a.V1.y, 0), new Vector3d(a.V2.x, a.V2.y, 0)),
                        new Triangle3d(new Vector3d(b.V0.x, b.V0.y, 0), new Vector3d(b.V1.x, b.V1.y, 0), new Vector3d(b.V2.x, b.V2.y, 0))) { ReportCoplanarIntersection = true };
                    found3[i] = intr.Find();
                    if (found3[i]) {
                        Vector3d[] src = intr.Type == IntersectionType.Polygon ? intr.PolygonPoints : new[] { intr.Points[0], intr.Points[1], intr.Points[2] };
                        pts3[i] = src.Take(intr.Quantity).Select(v => new Vector2d(v.x, v.y)).ToArray();
                    }
                }
                s3.Ms = sw.Elapsed.TotalMilliseconds;
                for (int i = 0; i < n; ++i) {
                    Triangle2d a = pairs[i][0], b = pairs[i][1];
                    double scale = new[] { a.V0, a.V1, a.V2, b.V0, b.V1, b.V2 }.Max(v => Math.Max(Math.Abs(v.x), Math.Abs(v.y)));
                    s2.Add(found2[i], pts2[i], exact[i], scale);
                    s3.Add(found3[i], pts3[i], exact[i], scale);
                }
            }
            Console.WriteLine($"{f.Name} | {exact.Count(e => e.Count > 0)} | {exact.Count(e => e.Count == 1 || e.Count == 2)} | {exactMs.ToString("0.0", CultureInfo.InvariantCulture)}, {maxBits} | {s2} | {s3}");
        }
        return 0;
    }
}
