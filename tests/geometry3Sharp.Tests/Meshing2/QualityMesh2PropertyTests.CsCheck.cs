using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using CsCheck;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// CsCheck campaign of QualityMesh2 against the independent oracle (QA 2.0 T5; the model is ExactBooleanPropertyTests). Cases are
    /// printable <see cref="QualityMesh2PropertyCase"/>s built from integers (lattice points, a 2^-d lattice unit, a 2^k scale, a snap
    /// pixel), valid by construction: every generator proposal is checked with exact integer geometry (simple rings, holes inside the
    /// outer ring and apart from each other) and replaced by a plain fallback when it fails, so a shrunk case is always a valid input.
    /// Properties (<see cref="QualityMesh2Properties.Check"/>): no exception or hang; status Success or Empty; the oracle's verdict is
    /// valid (segments are unions of output edges, angle bound outside the documented exemptions, constrained or conforming Delaunay,
    /// snap rules); input edges are chains of output edges (own exact check where it applies); the Steiner limit only with status
    /// Success and a mesh the oracle accepts; the same mesh on a repeated and on parallel runs; the 2^k scaled input gives the same
    /// mesh bit for bit.
    ///
    /// The class carries the trait Category=Campaign, which no Stryker test filter selects (they all list test classes by name). CI runs
    /// a small seeded campaign per family. Opt-in, as for the boolean:
    ///   G3_PROPERTY_SAMPLES=2000 dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~QualityMesh2PropertyTests
    ///   G3_PROPERTY_CAMPAIGN="polygon" G3_PROPERTY_REPLAY=&lt;seed&gt; dotnet test ... --filter FullyQualifiedName~QualityMesh2PropertyTests
    /// A failing seed becomes a fixed example (QualityMesh2PropertyExamplesTests, QA.md 2.4).
    /// </summary>
    [Trait("Category", "Campaign")]
    public class QualityMesh2PropertyTests
    {
        readonly ITestOutputHelper log;
        public QualityMesh2PropertyTests(ITestOutputHelper log) { this.log = log; }

        const int ShrinkIterations = 200;

        // ---------------------------------------------------------------- generators

        sealed class Builder
        {
            public readonly List<int> X = new List<int>(), Y = new List<int>();
            public readonly List<int[]> Rings = new List<int[]>();
            public readonly List<bool> Holes = new List<bool>();
            public readonly List<int[]> Segments = new List<int[]>();
            readonly Dictionary<(int, int), int> index = new Dictionary<(int, int), int>();
            public int Point(int x, int y)
            {
                if (!index.TryGetValue((x, y), out int i)) { i = X.Count; X.Add(x); Y.Add(y); index[(x, y)] = i; }
                return i;
            }
            public int[] Ring(IEnumerable<(int x, int y)> pts) => pts.Select(p => Point(p.x, p.y)).ToArray();
            public QualityMesh2PropertyCase Done(string family) => new QualityMesh2PropertyCase {
                Family = family, X = X.ToArray(), Y = Y.ToArray(), Rings = Rings.ToArray(), Holes = Holes.ToArray(), Segments = Segments.ToArray()
            };
        }

        static Gen<T> Pick<T>(params T[] items) => Gen.Int[0, items.Length - 1].Select(i => items[i]);

        static (int x, int y) Perimeter(int s, int t)
        {
            int side = t / (2 * s), u = t % (2 * s);
            switch (side) {
                case 0: return (s, -s + u);
                case 1: return (s - u, s);
                case 2: return (-s, s - u);
                default: return (-s + u, -s);
            }
        }

        static int Quarter(int v, int r) { long p = (long)v * r; return (int)((p + (p >= 0 ? 2 : -2)) / 4); }

        /// <summary> a polygon around (cx, cy): points on the square of half-size s at increasing perimeter positions, each pulled to r/4 of its radius </summary>
        static List<(int x, int y)> StarPoints(int cx, int cy, int s, int[] ts, int[] rs)
        {
            var order = Enumerable.Range(0, ts.Length).Select(i => (t: ts[i], r: rs[i])).OrderBy(p => p.t).ToList();
            var pts = new List<(int, int)>();
            int last = -1;
            foreach (var (t, r) in order) {
                if (t == last) continue;
                last = t;
                var p = Perimeter(s, t);
                pts.Add((cx + Quarter(p.x, r), cy + Quarter(p.y, r)));
            }
            return pts;
        }

        static List<(int x, int y)> Square(int cx, int cy, int s) => new List<(int, int)> { (cx - s, cy - s), (cx + s, cy - s), (cx + s, cy + s), (cx - s, cy + s) };

        static Gen<List<(int x, int y)>> GenStar(int cx, int cy, int s, int nmin, int nmax) =>
            Gen.Int[nmin, nmax].SelectMany(n => Gen.Select(Gen.Int[0, 8 * s - 1].Array[n], Gen.Int[2, 4].Array[n], (ts, rs) => StarPoints(cx, cy, s, ts, rs)));

        // a ring is kept if it is simple; the builder decides the rest
        static bool AddRing(Builder b, bool hole, List<(int x, int y)> pts, List<(int x, int y)> fallback)
        {
            var tmp = Candidate(b, pts);
            int[] ring = tmp.Item1;
            if (ring.Length < 3 || !LatticeGeometry.RingIsSimple(tmp.Item2, ring)) return false;
            foreach (int k in Enumerable.Range(0, b.Rings.Count)) {
                // (checked on the temporary case, which holds the ring's own points too)
                int[] other = b.Rings[k];
                if (!LatticeGeometry.RingsApart(tmp.Item2, other, ring)) return false;
                if (hole && k == 0 && !LatticeGeometry.Inside(tmp.Item2, other, tmp.Item2.X[ring[0]], tmp.Item2.Y[ring[0]])) return false;
                if (hole && k > 0 && (LatticeGeometry.Inside(tmp.Item2, other, tmp.Item2.X[ring[0]], tmp.Item2.Y[ring[0]]) ||
                                      LatticeGeometry.Inside(tmp.Item2, ring, tmp.Item2.X[other[0]], tmp.Item2.Y[other[0]]))) return false;
            }
            int[] real = b.Ring(pts);
            b.Rings.Add(real); b.Holes.Add(hole);
            return true;
        }

        // the ring's points as indices into a copy of the builder's table (so the checks see the ring before it is added)
        static (int[], QualityMesh2PropertyCase) Candidate(Builder b, List<(int x, int y)> pts)
        {
            var copy = new Builder();
            for (int i = 0; i < b.X.Count; ++i) copy.Point(b.X[i], b.Y[i]);
            foreach (var r in b.Rings) copy.Rings.Add(r);
            int[] ring = copy.Ring(pts);
            var c = copy.Done("");
            return (ring, c);
        }

        static void AddOuter(Builder b, List<(int x, int y)> pts, List<(int x, int y)> fallback)
        {
            if (!AddRing(b, false, pts, fallback)) AddRing(b, false, fallback, fallback);
        }

        /// <summary> star outer ring with up to three star or rectangle holes in the corners of an inner box, margins down to one unit </summary>
        static Gen<QualityMesh2PropertyCase> GenPolygon() =>
            Gen.Int[12, 40].SelectMany(so => {
                int h = Math.Max(4, so * 2 / 5);
                return Gen.Select(GenStar(0, 0, so, 3, 9), Gen.Int[0, 3], Gen.Int[0, 2],
                    GenStar(-h / 2, -h / 2, Math.Max(2, h / 2), 3, 7), GenStar(h / 2, -h / 2, Math.Max(2, h / 2), 3, 7),
                    GenStar(h / 2, h / 2, Math.Max(2, h / 2), 3, 7), GenStar(-h / 2, h / 2, Math.Max(2, h / 2), 3, 7),
                    (outer, nh, margin, h0, h1, h2, h3) => {
                        var b = new Builder();
                        AddOuter(b, outer, Square(0, 0, so));
                        var holes = new[] { h0, h1, h2, h3 };
                        for (int i = 0; i < nh; ++i) AddRing(b, true, holes[i], null);
                        return b.Done("polygon");
                    });
            });

        /// <summary> a rectangle with 1 to 4 holes in the cells of a 2 x 2 grid, one or two units from each other and from the outer ring </summary>
        static Gen<QualityMesh2PropertyCase> GenTouching() =>
            Gen.Select(Gen.Int[8, 40], Gen.Int[1, 2], Gen.Int[1, 2], Gen.Int[1, 4], Gen.Int[0, 15], Gen.Int[0, 15].Array[4], (size, margin, gap, nh, shapes, picks) => {
                var b = new Builder();
                AddRing(b, false, new List<(int, int)> { (0, 0), (size, 0), (size, size), (0, size) }, null);
                int w = (size - 2 * margin - gap) / 2;
                if (w >= 2)
                    for (int i = 0; i < nh; ++i) {
                        int x0 = margin + (i % 2) * (w + gap), y0 = margin + (i / 2) * (w + gap);
                        bool rect = (shapes >> i & 1) == 0 || w < 4;
                        var ring = rect
                            ? new List<(int, int)> { (x0, y0), (x0 + w, y0), (x0 + w, y0 + w), (x0, y0 + w) }
                            : StarPoints(x0 + w / 2, y0 + w / 2, w / 2, new[] { picks[0] % (4 * w), picks[1] % (4 * w), picks[2] % (4 * w), picks[3] % (4 * w) }, new[] { 4, 4, 4, 4 });
                        // (a star around the cell centre with half-size w/2 touches the cell's middle lines at most; rectangles fill the cell)
                        AddRing(b, true, ring, null);
                    }
                return b.Done("touching");
            });

        /// <summary> a fan polygon: an apex and 2 to 4 far points in increasing slope; twin points one unit apart give angles of 1/L radian </summary>
        static Gen<QualityMesh2PropertyCase> GenWedge() =>
            Gen.Select(Gen.Int[2, 4], Gen.Int[8, 300].Array[4], Gen.Int[-4, 4].Array[4], Gen.Int[0, 2], (n, ls, ds, mode) => {
                var pts = new List<(int x, int y)>();
                for (int i = 0; i < n; ++i) pts.Add((ls[i], ds[i]));
                if (mode == 1 && n >= 2) pts[1] = (pts[0].x, pts[0].y + 1);                      // twin: one unit above
                if (mode == 2 && n >= 2) pts[1] = (pts[0].x + 1, pts[0].y);                      // shifted: one unit further
                // increasing slope d/L about the apex (0, 0): compare d1 L2 with d2 L1, all L positive
                pts = pts.OrderBy(p => p, Comparer<(int x, int y)>.Create((p, q) => ((long)p.y * q.x).CompareTo((long)q.y * p.x))).ToList();
                var ring = new List<(int, int)> { (0, 0) };
                foreach (var p in pts) if (ring.Count == 1 || (long)ring[ring.Count - 1].Item2 * p.x != (long)p.y * ring[ring.Count - 1].Item1) ring.Add(p);
                var b = new Builder();
                if (!AddRing(b, false, ring, null)) AddRing(b, false, new List<(int, int)> { (0, 0), (100, 0), (100, 1) }, null);
                return b.Done("wedge");
            });

        /// <summary> 2 to 6 segments between lattice points (no ring: the domain is the hull), crossing, touching and nearly parallel </summary>
        static Gen<QualityMesh2PropertyCase> GenSoup() =>
            Gen.Select(Pick(6, 12, 40), Gen.Int[2, 6], Gen.Int[0, 40].Array[12], Gen.Int[0, 40].Array[12], (range, n, xs, ys) => {
                var b = new Builder();
                for (int i = 0; i < n; ++i) {
                    int a = b.Point(xs[2 * i] % (range + 1), ys[2 * i] % (range + 1)), c = b.Point(xs[2 * i + 1] % (range + 1), ys[2 * i + 1] % (range + 1));
                    if (a != c) b.Segments.Add(new[] { a, c });
                }
                if (b.X.Count == 0) { b.Point(0, 0); b.Point(1, 0); b.Point(0, 1); }
                return b.Done("soup");
            });

        static Gen<QualityMesh2PropertyCase> GenAnyFamily() =>
            Gen.Int[0, 3].SelectMany(f => f == 0 ? GenPolygon() : f == 1 ? GenTouching() : f == 2 ? GenWedge() : GenSoup());

        static readonly Gen<double?> GenAngle = Pick<double?>(0, 15, 20, 20, 25, 30, 30, 33);

        /// <summary> adds the frame: lattice unit, scale, snap pixel and the options </summary>
        static Gen<QualityMesh2PropertyCase> WithFrame(Gen<QualityMesh2PropertyCase> gen, Gen<int> exponent, Gen<int?> snap) =>
            Gen.Select(gen, exponent, snap, Gen.Int[0, 2], GenAngle, Gen.Bool, Gen.Bool, (c, k, s, den, angle, constrained, conforming) => {
                var r = c.Clone();
                r.Exponent = k; r.SnapLog2 = s; r.Denominator = den; r.Angle = angle; r.Constrained = constrained; r.Conforming = conforming && !constrained;
                return r;
            });

        static readonly Gen<int?> NoSnap = Gen.Int[0, 0].Select(_ => (int?)null);
        static readonly Gen<int?> GridSnap = Gen.Int[-3, 2].Select(v => (int?)v);

        // ---------------------------------------------------------------- campaign

        static int Samples(int normal)
        {
            string env = Environment.GetEnvironmentVariable("G3_PROPERTY_SAMPLES");
            return int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : normal;
        }

        void Campaign(string name, Gen<QualityMesh2PropertyCase> gen, int[] shifts, int samples, uint campaign)
        {
            Func<QualityMesh2PropertyCase, string> property = c => QualityMesh2Properties.Check(c, shifts);
            void Check(QualityMesh2PropertyCase t) { string r = property(t); if (r != "") throw new Exception(r); }
            var openByClass = new Dictionary<string, int>();
            var watch = Stopwatch.StartNew();
            string replay = Environment.GetEnvironmentVariable("G3_PROPERTY_CAMPAIGN") == name ? Environment.GetEnvironmentVariable("G3_PROPERTY_REPLAY") : null;
            string[] seeds = string.IsNullOrEmpty(replay)
                ? Enumerable.Range(0, Samples(samples)).Select(i => new PCG(campaign, (ulong)i).ToString()).ToArray()
                : new[] { replay };
            log.WriteLine($"{name}: CsCheck campaign {campaign}, {seeds.Length} samples");
            int failed = 0;
            foreach (string seed in seeds) {
                string failure = null;
                QualityMesh2PropertyCase original = null;
                try { gen.Sample(t => { original = t; Check(t); if (QualityMesh2Properties.LastOpenClass != null) { openByClass.TryGetValue(QualityMesh2Properties.LastOpenClass, out int n); openByClass[QualityMesh2Properties.LastOpenClass] = n + 1; log.WriteLine($"{name}: open grid finding {QualityMesh2Properties.LastOpenClass}, seed {seed}"); } }, seed: seed, iter: 1, threads: 1, print: t => t.ToString()); }
                catch (CsCheckException e) { failure = e.InnerException?.Message ?? e.Message; }
                if (failure == null) continue;
                ++failed;
                string shrunk = ExactBooleanPropertyTests.Shrink(gen, property, seed, ShrinkIterations).Report;
                string message = $"{name} fails for seed {seed} (campaign {campaign}):\n  {original}\n  {failure}\n" +
                    $"Shrunk ({ShrinkIterations} tries, not necessarily minimal): {shrunk}\n" +
                    $"Replay: G3_PROPERTY_CAMPAIGN=\"{name}\" G3_PROPERTY_REPLAY=<seed> dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter \"FullyQualifiedName~QualityMesh2PropertyTests.{name}\"";
                // a large campaign reports every failing seed, a small one stops at the first
                if (seeds.Length <= samples) throw new Xunit.Sdk.XunitException(message);
                log.WriteLine(message);
            }
            log.WriteLine($"{name}: {failed} failing seeds, {openByClass.Sum(kv => kv.Value)} open grid findings ({string.Join(", ", openByClass.Select(kv => kv.Key + " " + kv.Value))}), {watch.ElapsedMilliseconds} ms");
            Assert.True(failed == 0, name + ": " + failed + " failing seeds (see the output)");
        }

        static readonly int[] Shifts = { -11, 13 };
        static readonly int[] ExtremeShifts = { -200, 90 };

        [Fact] public void Polygon() => Campaign("polygon", WithFrame(GenPolygon(), Gen.Int[-6, 6], NoSnap), Shifts, 50, 101u);
        [Fact] public void Touching() => Campaign("touching", WithFrame(GenTouching(), Gen.Int[-6, 6], NoSnap), Shifts, 50, 102u);
        [Fact] public void Wedge() => Campaign("wedge", WithFrame(GenWedge(), Gen.Int[-6, 6], NoSnap), Shifts, 12, 103u);
        [Fact] public void Soup() => Campaign("soup", WithFrame(GenSoup(), Gen.Int[-6, 6], NoSnap), Shifts, 50, 104u);
        [Fact] public void Grid() => Campaign("grid", WithFrame(GenAnyFamily(), Gen.Int[-6, 6], GridSnap), Shifts, 50, 105u);
        [Fact] public void Extreme() => Campaign("extreme", WithFrame(GenAnyFamily(), Gen.Int[-300, 100], Gen.OneOf(NoSnap, GridSnap)), ExtremeShifts, 50, 106u);
    }
}
