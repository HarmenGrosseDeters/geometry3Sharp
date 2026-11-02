using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;

namespace Meshing2Acceptance
{
    /// <summary>
    /// Cost measurement before building m2.snap (docs/design/MESHING2D_SNAP.md, section 4; coordinator's ask): a prototype of snap
    /// rounding on a grid (Hobby 1999) with the restore step of Belussi, Migliorini, Negri, Pelagatti 2016 (SRR), run on the corpus's
    /// segment sets. It uses SRR's Proposition 4.14: the result equals SR on the hot pixels of SR plus the pixels the restore step
    /// adds, so each round routes every segment through the current hot pixels, finds the critical pixels (a hot pixel whose interior
    /// an output edge crosses) and adds the pixels ChoosePixel picks (rules R1 to R3), until no critical pixel is left.
    /// A prototype for timing, not the library routine: the pixel width is a power of two (pixel corners and centres are exact
    /// doubles, decided with the exact Orient2d), contact with a hot pixel uses the closed square (D14 asks for Hobby's half-open
    /// pixel), and a crossing's pixel comes from its double coordinates.
    /// </summary>
    static class SnapCost
    {
        readonly struct Pix : IEquatable<Pix>
        {
            public readonly long X, Y;
            public Pix(long x, long y) { X = x; Y = y; }
            public bool Equals(Pix o) => X == o.X && Y == o.Y;
            public override bool Equals(object o) => o is Pix p && Equals(p);
            public override int GetHashCode() => unchecked((int)(X * 0x9E3779B1L) ^ (int)(Y * 0x85EBCA77L) ^ (int)((X ^ Y) >> 32));
        }

        sealed class Rounder
        {
            readonly double w, h;                       // pixel width (a power of two) and half of it
            readonly List<(double ax, double ay, double bx, double by)> segs;
            readonly double bucket;                     // side of the spatial buckets, a multiple of w
            public readonly HashSet<Pix> Hot = new HashSet<Pix>();
            public int InitialHot, Crossings, Rounds, Spread, Snapped, ForcedSpread, CriticalTotal;
            public double MsCross, MsRoute, MsCritical, MsRestore;
            List<List<Pix>> chains;

            public Rounder(List<(double, double, double, double)> segments, double pixel)
            {
                segs = segments; w = pixel; h = pixel / 2;
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, length = 0;
                foreach (var s in segs) {
                    minX = Math.Min(minX, Math.Min(s.ax, s.bx)); maxX = Math.Max(maxX, Math.Max(s.ax, s.bx));
                    minY = Math.Min(minY, Math.Min(s.ay, s.by)); maxY = Math.Max(maxY, Math.Max(s.ay, s.by));
                    length += Math.Sqrt((s.bx - s.ax) * (s.bx - s.ax) + (s.by - s.ay) * (s.by - s.ay));
                }
                double mean = length / Math.Max(1, segs.Count);
                bucket = Math.Max(4 * w, Math.Pow(2, Math.Ceiling(Math.Log(Math.Max(mean, 4 * w), 2))));
            }

            Pix PixelOf(double x, double y) => new Pix((long)Math.Floor(x / w + 0.5), (long)Math.Floor(y / w + 0.5));
            double Cx(Pix p) => p.X * w;
            double Cy(Pix p) => p.Y * w;
            long B(double v) => (long)Math.Floor(v / bucket);

            /// <summary> the buckets a segment may touch, widened by one pixel: sampled at a quarter bucket, with the neighbours </summary>
            IEnumerable<(long, long)> Buckets(double ax, double ay, double bx, double by)
            {
                var seen = new HashSet<(long, long)>();
                double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                int steps = Math.Max(1, (int)Math.Ceiling(4 * len / bucket));
                for (int i = 0; i <= steps; ++i) {
                    double t = (double)i / steps, x = ax + t * (bx - ax), y = ay + t * (by - ay);
                    long gx = B(x), gy = B(y);
                    for (long dx = -1; dx <= 1; ++dx) for (long dy = -1; dy <= 1; ++dy) if (seen.Add((gx + dx, gy + dy))) yield return (gx + dx, gy + dy);
                }
            }

            static int O(double ax, double ay, double bx, double by, double cx, double cy) => ExactPredicates.Orient2d(new Vector2d(ax, ay), new Vector2d(bx, by), new Vector2d(cx, cy));

            /// <summary> does segment ab meet the pixel: the closed square (contact), or its open interior (crossing); separating axes </summary>
            bool Meets(double ax, double ay, double bx, double by, Pix p, bool interior)
            {
                double x0 = Cx(p) - h, x1 = Cx(p) + h, y0 = Cy(p) - h, y1 = Cy(p) + h;
                double lx = Math.Min(ax, bx), hx = Math.Max(ax, bx), ly = Math.Min(ay, by), hy = Math.Max(ay, by);
                if (interior ? (hx <= x0 || lx >= x1 || hy <= y0 || ly >= y1) : (hx < x0 || lx > x1 || hy < y0 || ly > y1)) return false;
                int pos = 0, neg = 0;
                foreach (var (cx, cy) in new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }) {
                    int o = O(ax, ay, bx, by, cx, cy);
                    if (o > 0) ++pos; else if (o < 0) ++neg;
                }
                if (ax == bx && ay == by) return interior ? (ax > x0 && ax < x1 && ay > y0 && ay < y1) : true;
                return interior ? (pos > 0 && neg > 0) : !(pos == 4 || neg == 4);
            }

            public void Compute()
            {
                var sw = Stopwatch.StartNew();
                foreach (var s in segs) { Hot.Add(PixelOf(s.ax, s.ay)); Hot.Add(PixelOf(s.bx, s.by)); }
                // crossings: candidate pairs from shared buckets, decided with Orient2d
                var byBucket = new Dictionary<(long, long), List<int>>();
                for (int i = 0; i < segs.Count; ++i) {
                    var s = segs[i];
                    foreach (var k in Buckets(s.ax, s.ay, s.bx, s.by)) { if (!byBucket.TryGetValue(k, out var l)) byBucket[k] = l = new List<int>(); l.Add(i); }
                }
                var pairs = new HashSet<(int, int)>();
                foreach (var l in byBucket.Values)
                    for (int a = 0; a < l.Count; ++a) for (int b = a + 1; b < l.Count; ++b) {
                        int i = Math.Min(l[a], l[b]), j = Math.Max(l[a], l[b]);
                        if (i == j || !pairs.Add((i, j))) continue;
                        var s = segs[i]; var t = segs[j];
                        int o1 = O(s.ax, s.ay, s.bx, s.by, t.ax, t.ay), o2 = O(s.ax, s.ay, s.bx, s.by, t.bx, t.by);
                        int o3 = O(t.ax, t.ay, t.bx, t.by, s.ax, s.ay), o4 = O(t.ax, t.ay, t.bx, t.by, s.bx, s.by);
                        if (o1 * o2 < 0 && o3 * o4 < 0) {
                            double d = (s.bx - s.ax) * (t.by - t.ay) - (s.by - s.ay) * (t.bx - t.ax);
                            double u = ((t.ax - s.ax) * (t.by - t.ay) - (t.ay - s.ay) * (t.bx - t.ax)) / d;
                            Hot.Add(PixelOf(s.ax + u * (s.bx - s.ax), s.ay + u * (s.by - s.ay)));
                            ++Crossings;
                        }
                    }
                MsCross = sw.Elapsed.TotalMilliseconds;
                InitialHot = Hot.Count;
                while (true) {
                    ++Rounds;
                    var t0 = Stopwatch.StartNew();
                    Route();
                    MsRoute += t0.Elapsed.TotalMilliseconds;
                    t0.Restart();
                    var critical = Critical();
                    MsCritical += t0.Elapsed.TotalMilliseconds;
                    CriticalTotal += critical.Count;
                    if (critical.Count == 0 || Rounds > 200) break;
                    t0.Restart();
                    Restore(critical);
                    MsRestore += t0.Elapsed.TotalMilliseconds;
                }
            }

            Dictionary<(long, long), List<Pix>> hotByBucket;

            /// <summary> SR: every segment becomes the chain through the centres of the hot pixels it meets, in order along it </summary>
            void Route()
            {
                hotByBucket = new Dictionary<(long, long), List<Pix>>();
                foreach (var p in Hot) { var k = (B(Cx(p)), B(Cy(p))); if (!hotByBucket.TryGetValue(k, out var l)) hotByBucket[k] = l = new List<Pix>(); l.Add(p); }
                chains = new List<List<Pix>>(segs.Count);
                foreach (var s in segs) {
                    var met = new List<(double t, Pix p)>();
                    var seen = new HashSet<Pix>();
                    double dx = s.bx - s.ax, dy = s.by - s.ay;
                    foreach (var k in Buckets(s.ax, s.ay, s.bx, s.by))
                        if (hotByBucket.TryGetValue(k, out var l))
                            foreach (var p in l) if (seen.Add(p) && Meets(s.ax, s.ay, s.bx, s.by, p, false)) met.Add(((Cx(p) - s.ax) * dx + (Cy(p) - s.ay) * dy, p));
                    met.Sort((a, b) => a.t.CompareTo(b.t));
                    var chain = new List<Pix>();
                    foreach (var m in met) if (chain.Count == 0 || !chain[chain.Count - 1].Equals(m.p)) chain.Add(m.p);
                    chains.Add(chain);
                }
            }

            /// <summary> a critical pixel: a hot pixel whose interior an output edge crosses, the edge's ends lying outside it </summary>
            List<(Pix cp, int chain, int edge)> Critical()
            {
                var list = new List<(Pix, int, int)>();
                for (int c = 0; c < chains.Count; ++c) {
                    var ch = chains[c];
                    for (int e = 0; e + 1 < ch.Count; ++e) {
                        Pix p = ch[e], q = ch[e + 1];
                        double ax = Cx(p), ay = Cy(p), bx = Cx(q), by = Cy(q);
                        foreach (var k in Buckets(ax, ay, bx, by))
                            if (hotByBucket.TryGetValue(k, out var l))
                                foreach (var hp in l) if (!hp.Equals(p) && !hp.Equals(q) && Meets(ax, ay, bx, by, hp, true)) list.Add((hp, c, e));
                    }
                }
                return list;
            }

            /// <summary> SRR's Restore with ChoosePixel (Table II): spread to a free useful pixel of the original segment's envelope next to the
            /// critical pixel (R1), else snap if the original segment crosses it (R2), else spread to a useful pixel anyway (R3) </summary>
            void Restore(List<(Pix cp, int chain, int edge)> critical)
            {
                // edges of the arrangement by bucket, for the freedom test
                var edgesByBucket = new Dictionary<(long, long), List<(int, int)>>();
                for (int c = 0; c < chains.Count; ++c)
                    for (int e = 0; e + 1 < chains[c].Count; ++e)
                        foreach (var k in Buckets(Cx(chains[c][e]), Cy(chains[c][e]), Cx(chains[c][e + 1]), Cy(chains[c][e + 1]))) {
                            if (!edgesByBucket.TryGetValue(k, out var l)) edgesByBucket[k] = l = new List<(int, int)>(); l.Add((c, e));
                        }
                bool Free(Pix p)
                {
                    if (!edgesByBucket.TryGetValue((B(Cx(p)), B(Cy(p))), out var l)) return true;
                    foreach (var (c, e) in l) {
                        Pix a = chains[c][e], b = chains[c][e + 1];
                        if (Meets(Cx(a), Cy(a), Cx(b), Cy(b), p, true)) return false;
                    }
                    return true;
                }
                var added = new List<Pix>();
                foreach (var (cp, c, e) in critical) {
                    var s = segs[c];
                    Pix a = chains[c][e], b = chains[c][e + 1];
                    var cvs = new List<(double d, Pix p)>();
                    for (long dx = -1; dx <= 1; ++dx) for (long dy = -1; dy <= 1; ++dy) {
                        if (dx == 0 && dy == 0) continue;
                        var p = new Pix(cp.X + dx, cp.Y + dy);
                        if (!Meets(s.ax, s.ay, s.bx, s.by, p, true)) continue;                                  // the envelope of the original segment
                        if (Meets(Cx(a), Cy(a), Cx(p), Cy(p), cp, true) || Meets(Cx(p), Cy(p), Cx(b), Cy(b), cp, true)) continue;   // useful
                        double sx = s.bx - s.ax, sy = s.by - s.ay, u = Math.Max(0, Math.Min(1, ((Cx(p) - s.ax) * sx + (Cy(p) - s.ay) * sy) / (sx * sx + sy * sy)));
                        double ex = s.ax + u * sx - Cx(p), ey = s.ay + u * sy - Cy(p);
                        cvs.Add((ex * ex + ey * ey, p));
                    }
                    var free = cvs.Where(x => Free(x.p)).OrderBy(x => x.d).ToList();
                    if (free.Count > 0) { added.Add(free[0].p); ++Spread; }
                    else if (Meets(s.ax, s.ay, s.bx, s.by, cp, true)) { added.Add(cp); ++Snapped; }
                    else if (cvs.Count > 0) { added.Add(cvs.OrderBy(x => x.d).First().p); ++ForcedSpread; }
                }
                foreach (var p in added) Hot.Add(p);
            }
        }

        public static int Run(string[] args, Func<string[], string, string> arg)
        {
            string dir = arg(args, "--dir") ?? Corpus.DefaultDir();
            string filter = arg(args, "--filter");
            var exponents = (arg(args, "--grid") ?? "12,16,20").Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            Console.WriteLine("input | segments | grid (extent / 2^k) | crossings | hot pixels: SR, final | rounds | critical | spread, snap, forced | ms: crossings, routing, critical, restore, total");
            foreach (var (name, segments) in Corpus.SegmentSets(dir)) {
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                double extent = segments.Max(s => Math.Max(Math.Max(Math.Abs(s.Item1), Math.Abs(s.Item2)), Math.Max(Math.Abs(s.Item3), Math.Abs(s.Item4))));
                foreach (int k in exponents) {
                    double w = Math.Pow(2, Math.Floor(Math.Log(extent, 2)) - k);
                    var run = new Rounder(segments, w);
                    var sw = Stopwatch.StartNew();
                    run.Compute();
                    double total = sw.Elapsed.TotalMilliseconds;
                    Console.WriteLine(string.Join(" | ", name, segments.Count, "2^" + k, run.Crossings, run.InitialHot + " -> " + run.Hot.Count,
                        run.Rounds, run.CriticalTotal, run.Spread + ", " + run.Snapped + ", " + run.ForcedSpread,
                        string.Join(", ", new[] { run.MsCross, run.MsRoute, run.MsCritical, run.MsRestore, total }.Select(x => x.ToString("F0", CultureInfo.InvariantCulture)))));
                }
            }
            return 0;
        }
    }
}
