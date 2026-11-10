using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Clipper2Lib;
using CsCheck;
using g3;
using g3.Clipper2;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// QA 2.0 T10: CsCheck campaign for Clipper2Polygons against Oracle.cs (winding numbers, point-segment distances, shoelace areas;
    /// no code shared with Clipper2). Random polygon sets A and B (stars, rectangles, a star with a hole, two disjoint stars) placed
    /// so that they overlap, touch, nest or are apart, at scales 2^-10 .. 2^10 and far from the origin.
    ///   - Union, Intersection, Difference, Xor: well formed (Oracle.CheckWellFormed) and, at sample points farther than a few grid
    ///     resolutions from every input boundary, the membership of the result is the Boolean operation on the memberships of A and B;
    ///     the areas satisfy area(A u B) + area(A n B) = area A + area B and the partition identities, up to the rounding of the grid;
    ///   - Offset (round joins) both ways: membership by the distance to the input, up to the arc tolerance and the grid;
    ///   - Simplify: every result vertex within epsilon (and the grid) of an input boundary, the membership kept away from it;
    ///   - a repeated call equals the first bit for bit, and the input scaled by 2^k gives the result scaled by 2^k, bit for bit.
    /// The runner here is small (no shrinking: a failure reports the whole scene, ten integers). 20 samples in CI; G3_PROPERTY_SAMPLES=n, G3_PROPERTY_CAMPAIGN=clipper2 with G3_PROPERTY_REPLAY=seed to replay (the same
    /// variables as the campaigns of geometry3Sharp.Tests).
    /// </summary>
    [Trait("Category", "Campaign")]
    public class Clipper2CampaignTests
    {
        readonly ITestOutputHelper log;
        public Clipper2CampaignTests(ITestOutputHelper log) { this.log = log; }

        public sealed class Scene
        {
            public int KindA, KindB;    // 0 star, 1 rectangle, 2 star with a hole, 3 two disjoint stars, 4 a finely sampled circle
            public int Exp;             // size 2^Exp, -10 .. 10
            public int Far;             // moved by 2^Far sizes along (1, 0.7), -1: not moved (at most 2^10: the oracle's own rounding stays far below the margin)
            public int Dx, Dy;          // B moved against A by (Dx, Dy) / 4 sizes, -6 .. 6 (touching and nested cases included)
            public int Rel;             // placement of B: 0 free (Dx, Dy), 1 an identical copy of A, 2 nested (A's shape kind at 0.3 size, same center), 3 rectangles sharing an edge (B at (2, Dy / 8))
            public int Op;              // 0 union, 1 intersection, 2 difference, 3 xor
            public int Round;           // 0: Offset by +0.15 size; 1: by -0.1 size
            public int Seed;
            public override string ToString() => $"A kind {KindA}, B kind {KindB}, size 2^{Exp}, far {Far}, B moved ({Dx}, {Dy})/4, placement {new[] { "free", "identical", "nested", "shared edge" }[Rel]}, op {new[] { "union", "intersection", "difference", "xor" }[Op]}, offset case {Round}, seed {Seed}";
        }

        static List<GeneralPolygon2d> Shapes(int kind, Random rnd, Vector2d center, double h)
        {
            var l = new List<GeneralPolygon2d>();
            switch (kind) {
                case 0: l.Add(new GeneralPolygon2d(Oracle.Star(rnd, center, 5 + rnd.Next(8), 0.4 * h, h))); break;
                case 1: l.Add(Oracle.Rect(center.x - h, center.y - 0.6 * h, center.x + h, center.y + 0.6 * h)); break;
                case 2: l.Add(Oracle.StarWithHole(rnd, center, 8 + rnd.Next(6), h)); break;
                case 4: {
                    // a finely sampled circle (160 vertices, wobbling by 1e-4): what Simplify is for
                    var v = new List<Vector2d>();
                    for (int i = 0; i < 160; ++i) {
                        double r = h * (1 + 1e-4 * (rnd.NextDouble() - 0.5)), a = 2 * Math.PI * i / 160;
                        v.Add(new Vector2d(center.x + r * Math.Cos(a), center.y + r * Math.Sin(a)));
                    }
                    l.Add(new GeneralPolygon2d(new Polygon2d(v)));
                    break;
                }
                default:
                    l.Add(new GeneralPolygon2d(Oracle.Star(rnd, center + new Vector2d(-1.6 * h, 0), 6, 0.3 * h, 0.7 * h)));
                    l.Add(new GeneralPolygon2d(Oracle.Star(rnd, center + new Vector2d(1.6 * h, 0), 6, 0.3 * h, 0.7 * h)));
                    break;
            }
            return l;
        }

        /// <summary> the inputs at scale 2^shift (the unit shapes are drawn first and multiplied exactly) </summary>
        static (List<GeneralPolygon2d> a, List<GeneralPolygon2d> b) Build(Scene s, int shift)
        {
            double f = Math.ScaleB(1.0, s.Exp + shift);
            Vector2d move = s.Far >= 0 ? Math.ScaleB(1.0, s.Far) * new Vector2d(1, 0.7) : Vector2d.Zero;
            var rnd = new Random(s.Seed);
            int kindA = s.Rel == 3 ? 1 : s.KindA, kindB = s.Rel == 3 ? 1 : s.KindB;
            var a = Shapes(kindA, rnd, Vector2d.Zero, 1);
            List<GeneralPolygon2d> b;
            switch (s.Rel) {
                case 1: b = Shapes(kindA, new Random(s.Seed), Vector2d.Zero, 1); break;
                case 2: b = Shapes(kindA, new Random(s.Seed), Vector2d.Zero, 0.3); break;
                case 3: b = Shapes(1, rnd, new Vector2d(2, s.Dy / 8.0), 1); break;
                default: b = Shapes(kindB, rnd, new Vector2d(s.Dx / 4.0, s.Dy / 4.0), 1); break;
            }
            Func<List<GeneralPolygon2d>, List<GeneralPolygon2d>> scale = l => l.Select(g => {
                var o = new GeneralPolygon2d(new Polygon2d(g.Outer.Vertices.Select(v => f * (v + move)).ToList()));
                foreach (Polygon2d h in g.Holes) o.AddHole(new Polygon2d(h.Vertices.Select(v => f * (v + move)).ToList()), false, false);
                return o;
            }).ToList();
            return (scale(a), scale(b));
        }

        static bool Inside(List<GeneralPolygon2d> set, Vector2d p) => set.Any(g => Oracle.Winding(g, p) != 0);

        static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);

        static string SameResult(Clipper2Result a, Clipper2Result b, double scale)
        {
            if (a.Status != b.Status) return $"status {a.Status} vs {b.Status}";
            if (a.Polygons.Count != b.Polygons.Count) return $"{a.Polygons.Count} vs {b.Polygons.Count} polygons";
            for (int i = 0; i < a.Polygons.Count; ++i) {
                var ra = Oracle.Rings(a.Polygons[i]).ToList(); var rb = Oracle.Rings(b.Polygons[i]).ToList();
                if (ra.Count != rb.Count) return $"polygon {i}: {ra.Count} vs {rb.Count} rings";
                for (int r = 0; r < ra.Count; ++r) {
                    if (ra[r].Count != rb[r].Count) return $"polygon {i} ring {r}: {ra[r].Count} vs {rb[r].Count} vertices";
                    for (int v = 0; v < ra[r].Count; ++v)
                        if (Bits(ra[r][v].x * scale) != Bits(rb[r][v].x) || Bits(ra[r][v].y * scale) != Bits(rb[r][v].y))
                            return $"polygon {i} ring {r} vertex {v}: {ra[r][v]} * {scale} vs {rb[r][v]}";
                }
            }
            return "";
        }

        static IEnumerable<Vector2d> SamplePoints(List<GeneralPolygon2d> a, List<GeneralPolygon2d> b, int seed)
        {
            var all = a.Concat(b).SelectMany(Oracle.Rings).SelectMany(r => r).ToList();
            var box = new AxisAlignedBox2d(all[0]);
            foreach (Vector2d v in all) box.Contain(v);
            double e = 0.25 * Math.Max(box.Width, box.Height);
            box = new AxisAlignedBox2d(box.Min - new Vector2d(e, e), box.Max + new Vector2d(e, e));
            var rnd = new Random(seed ^ 0x5bd1e995);
            foreach (Vector2d p in Oracle.Samples(rnd, box, 250)) yield return p;
            // (near the vertices: a point of a thin sliver or a touching spot)
            foreach (Vector2d v in all.OrderBy(_ => rnd.Next()).Take(60)) yield return v + 0.01 * (box.Width) * new Vector2d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
        }

        static string Property(Scene s, int[] shifts)
        {
            var (a, b) = Build(s, 0);
            ClipType op = new[] { ClipType.Union, ClipType.Intersection, ClipType.Difference, ClipType.Xor }[s.Op];
            Clipper2Result r = Clipper2Polygons.Boolean(op, a, b);
            if (!r.Ok) return "Boolean: " + r;
            string d = BooleanProperty(s, a, b, r);
            if (d != "") return d;
            d = IdentityProperty(a, b);
            if (d != "") return d;
            d = OffsetProperty(s, a);
            if (d != "") return d;
            d = SimplifyProperty(s, a);
            if (d != "") return d;
            // a repeated call, and the 2^k scaled input
            Clipper2Result again = Clipper2Polygons.Boolean(op, a, b);
            d = SameResult(r, again, 1);
            if (d != "") return "a repeated call differs: " + d;
            foreach (int shift in shifts) {
                var (sa, sb) = Build(s, shift);
                Clipper2Result rs = Clipper2Polygons.Boolean(op, sa, sb);
                d = SameResult(r, rs, Math.ScaleB(1.0, shift));
                if (d != "") return "the input scaled by 2^" + shift + " differs: " + d;
            }
            return "";
        }

        static string BooleanProperty(Scene s, List<GeneralPolygon2d> a, List<GeneralPolygon2d> b, Clipper2Result r)
        {
            try { Oracle.CheckWellFormed(r); }
            catch (Xunit.Sdk.XunitException e) { return "not well formed: " + e.Message.Split('\n')[0]; }
            // (the grid, and the rounding of the oracle's own cross products at the coordinates' magnitude)
            double maxCoord = a.Concat(b).SelectMany(Oracle.Rings).SelectMany(q => q).Max(v => Math.Max(Math.Abs(v.x), Math.Abs(v.y)));
            double margin = Math.Max(4 * r.Frame.Resolution, 64 * Math.Abs(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(maxCoord) + 1) - maxCoord));
            foreach (Vector2d p in SamplePoints(a, b, s.Seed)) {
                if (Oracle.BoundaryDistance(a, p) <= margin || Oracle.BoundaryDistance(b, p) <= margin) continue;
                bool ia = Inside(a, p), ib = Inside(b, p);
                bool expect = s.Op == 0 ? ia || ib : s.Op == 1 ? ia && ib : s.Op == 2 ? ia && !ib : ia != ib;
                int count = Oracle.ResultCount(r.Polygons, p);
                if (count > 1) return $"result polygons overlap at {p}";
                if ((count == 1) != expect) return $"at {p}: in A {ia}, in B {ib}, result {(count == 1)}, expected {expect} ({new[] { "union", "intersection", "difference", "xor" }[s.Op]})";
            }
            return "";
        }

        /// <summary> area identities of the four operations </summary>
        static string IdentityProperty(List<GeneralPolygon2d> a, List<GeneralPolygon2d> b)
        {
            Clipper2Result u = Clipper2Polygons.Union(a, b), n = Clipper2Polygons.Intersection(a, b), df = Clipper2Polygons.Difference(a, b), x = Clipper2Polygons.Xor(a, b);
            if (!u.Ok || !n.Ok || !df.Ok || !x.Ok) return $"an operation failed: {u.Status} {n.Status} {df.Status} {x.Status}";
            double areaA = Oracle.Area(Clipper2Polygons.Union(a).Polygons), areaB = Oracle.Area(Clipper2Polygons.Union(b).Polygons);
            // (A and B as sets: the union of each alone; the grid moves a vertex by up to half a resolution, so an area by perimeter x resolution)
            double perimeter = a.Concat(b).SelectMany(Oracle.Rings).Sum(r => Enumerable.Range(0, r.Count).Sum(i => (r[(i + 1) % r.Count] - r[i]).Length));
            double tol = 4 * u.Frame.Resolution * perimeter + 1e-12 * (areaA + areaB);
            double au = Oracle.Area(u.Polygons), an = Oracle.Area(n.Polygons), ad = Oracle.Area(df.Polygons), ax = Oracle.Area(x.Polygons);
            if (Math.Abs(au + an - areaA - areaB) > tol) return $"area(A u B) + area(A n B) = {au + an}, area A + area B = {areaA + areaB} (tolerance {tol})";
            if (Math.Abs(ad - (areaA - an)) > tol) return $"area(A - B) = {ad}, area A - area(A n B) = {areaA - an} (tolerance {tol})";
            if (Math.Abs(ax - (au - an)) > tol) return $"area(A xor B) = {ax}, area(A u B) - area(A n B) = {au - an} (tolerance {tol})";
            return "";
        }

        static string OffsetProperty(Scene s, List<GeneralPolygon2d> a)
        {
            double size = Math.ScaleB(1.0, s.Exp);
            double delta = s.Round == 0 ? 0.15 * size : -0.1 * size;
            Clipper2Result r = Clipper2Polygons.Offset(a, delta, new Clipper2Options { JoinType = JoinType.Round });
            if (!r.Ok) return "Offset: " + r;
            try { Oracle.CheckWellFormed(r); }
            catch (Xunit.Sdk.XunitException e) { return "offset not well formed: " + e.Message.Split('\n')[0]; }
            double tol = 0.01 * Math.Abs(delta) + 4 * r.Frame.Resolution;
            var rnd = new Random(s.Seed ^ 0x2545f491);
            var all = a.SelectMany(Oracle.Rings).SelectMany(q => q).ToList();
            var box = new AxisAlignedBox2d(all[0]);
            foreach (Vector2d v in all) box.Contain(v);
            double pad = Math.Abs(delta) * 1.5 + 0.1 * size;
            box = new AxisAlignedBox2d(box.Min - new Vector2d(pad, pad), box.Max + new Vector2d(pad, pad));
            foreach (Vector2d p in Oracle.Samples(rnd, box, 250)) {
                bool inside = Inside(a, p);
                double dist = Oracle.BoundaryDistance(a, p);
                // dilation: inside the result iff within delta of the input (inside the input counts as distance 0);
                // erosion: inside iff inside the input and at least |delta| from its boundary
                bool inResult = Oracle.ResultCount(r.Polygons, p) > 0;
                if (delta > 0) {
                    double signedDist = inside ? 0 : dist;
                    if (signedDist < delta - tol && !inResult) return $"offset {delta}: {p} at distance {signedDist} from the input is not in the result";
                    if (signedDist > delta + tol && inResult) return $"offset {delta}: {p} at distance {signedDist} from the input is in the result";
                } else {
                    double depth = inside ? dist : -dist;
                    if (depth > -delta + tol && !inResult) return $"offset {delta}: {p} at depth {depth} is not in the result";
                    if (depth < -delta - tol && inResult) return $"offset {delta}: {p} at depth {depth} is in the result";
                }
            }
            return "";
        }

        static string SimplifyProperty(Scene s, List<GeneralPolygon2d> a)
        {
            double size = Math.ScaleB(1.0, s.Exp);
            double eps = 0.03 * size;
            Clipper2Result r = Clipper2Polygons.Simplify(a, eps);
            if (!r.Ok) return "Simplify: " + r;
            try { Oracle.CheckWellFormed(r); }
            catch (Xunit.Sdk.XunitException e) { return "simplified not well formed: " + e.Message.Split('\n')[0]; }
            double tol = eps + 4 * r.Frame.Resolution;
            // (it does simplify: a finely sampled circle loses vertices)
            if (s.KindA == 4 && s.Rel != 3 && r.Polygons.SelectMany(Oracle.Rings).Sum(q => q.Count) >= a.SelectMany(Oracle.Rings).Sum(q => q.Count)) return "a finely sampled circle was not simplified";
            foreach (Vector2d v in r.Polygons.SelectMany(Oracle.Rings).SelectMany(q => q))
                if (Oracle.BoundaryDistance(a, v) > tol) return $"simplified vertex {v} is {Oracle.BoundaryDistance(a, v)} from the input boundary, epsilon {eps}";
            // and the other way: every input vertex near the result boundary. Clipper2's SimplifyPaths measures a vertex against the line
            // through its neighbours, so a run of removed vertices can drift past epsilon (1.04 epsilon seen): a small multiple is the bound
            var resultRings = r.Polygons.SelectMany(Oracle.Rings).ToList();
            foreach (Vector2d v in a.SelectMany(Oracle.Rings).SelectMany(q => q))
                if (resultRings.Min(q => Oracle.RingDistance(q, v)) > 3 * eps + 4 * r.Frame.Resolution) return $"input vertex {v} is {resultRings.Min(q => Oracle.RingDistance(q, v))} from the simplified boundary, epsilon {eps} (bound 3 epsilon)";
            var rnd = new Random(s.Seed ^ 0x7f4a7c15);
            var all = a.SelectMany(Oracle.Rings).SelectMany(q => q).ToList();
            var box = new AxisAlignedBox2d(all[0]);
            foreach (Vector2d v in all) box.Contain(v);
            foreach (Vector2d p in Oracle.Samples(rnd, new AxisAlignedBox2d(box.Min - new Vector2d(size, size), box.Max + new Vector2d(size, size)), 250)) {
                if (Oracle.BoundaryDistance(a, p) <= 2 * tol) continue;
                if (Inside(a, p) != (Oracle.ResultCount(r.Polygons, p) > 0)) return $"simplified: membership of {p} changed (distance {Oracle.BoundaryDistance(a, p)} from the input boundary, epsilon {eps})";
            }
            return "";
        }

        static readonly Gen<Scene> GenScene =
            Gen.Select(Gen.Select(Gen.Int[0, 4], Gen.Int[0, 4], Gen.Int[-10, 10], Gen.Int[-1, 10], (ka, kb, e, f) => (ka, kb, e, f)),
                       Gen.Select(Gen.Int[-6, 6], Gen.Int[-6, 6], Gen.Int[0, 3], Gen.Int[0, 1], Gen.Int[0, 100000], Gen.Int[0, 3], (dx, dy, op, rd, sd, rel) => (dx, dy, op, rd, sd, rel)),
                (x, y) => new Scene { Rel = y.rel, KindA = x.ka, KindB = x.kb, Exp = x.e, Far = x.f, Dx = y.dx, Dy = y.dy, Op = y.op, Round = y.rd, Seed = y.sd });

        static readonly int[] Shifts = { -9, 11 };

        static void Run(ITestOutputHelper log, string name, Gen<Scene> gen, Func<Scene, string> property, int samples, uint campaign)
        {
            string replay = Environment.GetEnvironmentVariable("G3_PROPERTY_CAMPAIGN") == name ? Environment.GetEnvironmentVariable("G3_PROPERTY_REPLAY") : null;
            int n = int.TryParse(Environment.GetEnvironmentVariable("G3_PROPERTY_SAMPLES"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int env) && env > 0 ? env : samples;
            int first = int.TryParse(Environment.GetEnvironmentVariable("G3_PROPERTY_FIRST"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int f0) && f0 > 0 ? f0 : 0;
            string[] seeds = string.IsNullOrEmpty(replay) ? Enumerable.Range(first, n).Select(i => new PCG(campaign, (ulong)i).ToString()).ToArray() : new[] { replay };
            log.WriteLine($"{name}: CsCheck campaign {campaign}, {seeds.Length} samples");
            bool all = Environment.GetEnvironmentVariable("G3_PROPERTY_ALL") == "1";
            var failures = new List<string>();
            var watch = Stopwatch.StartNew();
            foreach (string seed in seeds) {
                string failure = null; Scene original = null;
                try {
                    gen.Sample(sc => {
                        original = sc;
                        string r;
                        try { r = property(sc); } catch (Exception e) { r = "exception " + e.GetType().Name + ": " + e.Message; }
                        if (r != "") throw new Exception(r);
                    }, seed: seed, iter: 1, threads: 1, print: t => t.ToString());
                } catch (CsCheckException e) { failure = e.InnerException?.Message ?? e.Message; }
                if (failure == null) continue;
                string text = $"FAIL {seed}: {original}\n    {failure.Replace("\n", "\n    ")}";
                if (all) { failures.Add(text); continue; }
                throw new Xunit.Sdk.XunitException($"{name} fails for seed {seed} (campaign {campaign}):\n  {original}\n  {failure}\n" +
                    $"Replay: G3_PROPERTY_CAMPAIGN=\"{name}\" G3_PROPERTY_REPLAY={seed} dotnet test tests/geometry3Sharp.Clipper2.Tests -c Release -f net10.0 --filter \"FullyQualifiedName~Clipper2CampaignTests\"");
            }
            if (failures.Count > 0) throw new Xunit.Sdk.XunitException($"{name}: {failures.Count} of {seeds.Length} cases fail:\n" + string.Join("\n", failures));
            log.WriteLine($"{name}: passed in {watch.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void Campaign()
        {
            Run(log, "clipper2", GenScene, s => Property(s, Shifts), 20, 501u);
        }
    }
}
