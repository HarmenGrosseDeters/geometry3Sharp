using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// Snap rounding at an explicit tolerance (spec O11, decision D14 of 2026-11-01): snap rounding on a grid of pixel width tol anchored
    /// at the origin (J. D. Hobby, Practical segment intersection with finite precision output, CGTA 1999), followed by the restore step of
    /// Snap Rounding with Restore (A. Belussi, S. Migliorini, M. Negri, G. Pelagatti, ACM TSAS 2016), so that every vertex ends at least
    /// half a pixel from every edge not incident to it.
    ///
    /// Pixels are half-open, closed on the left and bottom (Hobby). A pixel is hot if it holds an input point or a crossing of two input
    /// segments. Every input point moves to the centre of its pixel; every input segment becomes the chain through the centres of the hot
    /// pixels it touches, in order along it. A hot pixel whose interior an output edge crosses, its ends outside the pixel, is critical;
    /// SRR's ChoosePixel picks a pixel next to it in the envelope of the edge's input segment (rules R1 to R3) and makes it hot; by SRR's
    /// Proposition 4.14 the result is snap rounding on the grown set of hot pixels, so every round routes all segments again, which keeps
    /// SR's guarantees (no crossings, topology up to collapse) at every step. Splitting only the critical edge, as SRR's listing does,
    /// can make the new edge cross another chain away from any hot pixel (found by the fuzz test). Only pixels that are not hot yet are
    /// chosen, so every round adds one; all lie in the envelopes of the input segments, so the rounds end.
    ///
    /// Arithmetic: every double is an integer times a power of two; with the common exponent of all input coordinates and the pixel width,
    /// twice every coordinate is an integer, and so are the pixel corners (2k ± 1) W and centres 2 k W. Every decision is exact.
    /// </summary>
    internal sealed class SnapRounding2
    {
        /// <summary> the snapped input: input points at their pixel centres, extra hot pixel centres appended, segments and rings as chains </summary>
        public ConstrainedMesh2Input Result;
        /// <summary> the largest distance by which an input point moved, per axis (at most half a pixel) </summary>
        public double MaxDisplacement;
        public int Rounds, Crossings, Spread, Snapped, ForcedSpread, CollapsedSegments, CollapsedRings, DroppedSeeds;
        public readonly List<string> Diagnostics = new List<string>();

        const int RoundLimit = 1000;

        readonly ConstrainedMesh2Input input;
        readonly double w;
        readonly BigInteger W;              // the pixel width in units
        readonly BigInteger[] X, Y;         // twice the input coordinates, in units
        readonly long[] KX, KY;             // the pixel of each input point
        readonly List<(int a, int b)> routed = new List<(int, int)>();   // input point pairs of every constraint edge, routed once each
        readonly Dictionary<(int, int), int> routedIndex = new Dictionary<(int, int), int>();
        readonly HashSet<(long, long)> hot = new HashSet<(long, long)>();
        readonly long bucket;                // side of the spatial buckets, in pixels
        Dictionary<(long, long), List<(long, long)>> hotByBucket;
        List<List<(long, long)>> chains;

        SnapRounding2(ConstrainedMesh2Input input, double tolerance)
        {
            this.input = input;
            w = tolerance;
            int n = input.Points.Count;
            // the common exponent of all coordinates and the pixel width
            var mant = new BigInteger[2 * n + 1];
            var exps = new int[2 * n + 1];
            int emin = int.MaxValue;
            for (int i = 0; i <= 2 * n; ++i) {
                double v = i == 2 * n ? w : (i % 2 == 0 ? input.Points[i / 2].x : input.Points[i / 2].y);
                ExactPoint3.Decompose(v, out mant[i], out exps[i]);
                if (!mant[i].IsZero) emin = Math.Min(emin, exps[i]);
            }
            BigInteger Units(int i) => mant[i].IsZero ? BigInteger.Zero : mant[i] << (exps[i] - emin);
            W = Units(2 * n);
            X = new BigInteger[n]; Y = new BigInteger[n]; KX = new long[n]; KY = new long[n];
            for (int i = 0; i < n; ++i) {
                X[i] = 2 * Units(2 * i); Y[i] = 2 * Units(2 * i + 1);
                KX[i] = PixelOf(X[i], BigInteger.One); KY[i] = PixelOf(Y[i], BigInteger.One);
            }
            void Route(int a, int b)
            {
                if (a == b) return;
                var key = a < b ? (a, b) : (b, a);
                if (routedIndex.ContainsKey(key)) return;
                routedIndex.Add(key, routed.Count);
                routed.Add(key);
            }
            foreach (var s in input.Segments) Route(s.A, s.B);
            foreach (var r in input.Rings) for (int i = 0; i < r.Vertices.Length; ++i) Route(r.Vertices[i], r.Vertices[(i + 1) % r.Vertices.Length]);
            foreach (var g in input.Regions) if (g.Ring != null) for (int i = 0; i < g.Ring.Length; ++i) Route(g.Ring[i], g.Ring[(i + 1) % g.Ring.Length]);
            // buckets of about the mean constraint length, at least 4 pixels, a power of two
            double length = 0;
            foreach (var (a, b) in routed) length += Math.Abs(KX[a] - KX[b]) + Math.Abs(KY[a] - KY[b]);
            double mean = routed.Count == 0 ? 4 : length / routed.Count;
            bucket = 4;
            while (bucket < mean && bucket < (1L << 40)) bucket *= 2;
        }

        /// <summary> the pixel holding the point num / den (twice the coordinate, in units; den > 0): floor((num / den + W) / (2 W)) </summary>
        long PixelOf(BigInteger num, BigInteger den)
        {
            BigInteger q = FloorDiv(num + W * den, 2 * W * den);
            if (BigInteger.Abs(q) >= (BigInteger.One << 52)) throw new ArgumentException("SnapTolerance is too small for the coordinates: more than 2^52 pixels across");
            return (long)q;
        }

        static BigInteger FloorDiv(BigInteger a, BigInteger b)
        {
            var q = BigInteger.DivRem(a, b, out var r);
            if (!r.IsZero && (r.Sign < 0) != (b.Sign < 0)) q -= 1;
            return q;
        }

        /// <summary> Snap-rounds the input; throws ArgumentException for a tolerance that is not positive and finite or too small </summary>
        public static SnapRounding2 Apply(ConstrainedMesh2Input input, double tolerance)
        {
            if (!(tolerance > 0) || double.IsInfinity(tolerance)) throw new ArgumentException("SnapTolerance must be positive and finite");
            var s = new SnapRounding2(input, tolerance);
            s.Compute();
            return s;
        }

        // ------------------------------------------------------------ exact geometry on twice the coordinates

        BigInteger Cx((long, long) p) => 2 * p.Item1 * W;
        BigInteger Cy((long, long) p) => 2 * p.Item2 * W;

        static int Orient(BigInteger px, BigInteger py, BigInteger qx, BigInteger qy, BigInteger cx, BigInteger cy)
            => ((qx - px) * (cy - py) - (qy - py) * (cx - px)).Sign;

        /// <summary> segment pq against the pixel: its open interior (interior), or Hobby's half-open square (contact) </summary>
        bool Meets(BigInteger px, BigInteger py, BigInteger qx, BigInteger qy, (long, long) k, bool interior)
        {
            BigInteger x0 = (2 * k.Item1 - 1) * W, x1 = (2 * k.Item1 + 1) * W, y0 = (2 * k.Item2 - 1) * W, y1 = (2 * k.Item2 + 1) * W;
            BigInteger lx = BigInteger.Min(px, qx), hx = BigInteger.Max(px, qx), ly = BigInteger.Min(py, qy), hy = BigInteger.Max(py, qy);
            if (hx < x0 || lx > x1 || hy < y0 || ly > y1) return false;
            int pos = 0, neg = 0;
            bool point = px == qx && py == qy;
            if (!point)
                foreach (var (cx, cy) in new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }) {
                    int o = Orient(px, py, qx, qy, cx, cy);
                    if (o > 0) ++pos; else if (o < 0) ++neg;
                }
            bool open = point ? (px > x0 && px < x1 && py > y0 && py < y1) : (hx > x0 && lx < x1 && hy > y0 && ly < y1 && pos > 0 && neg > 0);
            if (open) return true;
            if (interior) return false;
            if (!point && (pos == 4 || neg == 4)) return false;
            // the segment meets the closed square only on its boundary: count the left and bottom sides without the top-left and
            // bottom-right corners
            if (py == qy && (py == y0 || py == y1)) return py == y0 && hx >= x0 && lx < x1;
            if (px == qx && (px == x0 || px == x1)) return px == x0 && hy >= y0 && ly < y1;
            foreach (var (ex, ey) in new[] { (px, py), (qx, qy) })
                if (ex >= x0 && ex <= x1 && ey >= y0 && ey <= y1) return ex < x1 && ey < y1;
            // through a corner: only the bottom-left one belongs to the pixel
            return Orient(px, py, qx, qy, x0, y0) == 0 && lx <= x0 && x0 <= hx && ly <= y0 && y0 <= hy;
        }

        /// <summary>
        /// where segment pq enters and leaves the closed pixel, as fractions of its length (num / den, den > 0); chains are ordered by
        /// entry, then by exit, so that a pixel the segment only touches where it starts comes before the one it then runs through
        /// (a segment starting on the side two pixels share; found by the fuzz test)
        /// </summary>
        ((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) Span(BigInteger px, BigInteger py, BigInteger qx, BigInteger qy, (long, long) k)
        {
            BigInteger x0 = (2 * k.Item1 - 1) * W, x1 = (2 * k.Item1 + 1) * W, y0 = (2 * k.Item2 - 1) * W, y1 = (2 * k.Item2 + 1) * W;
            (BigInteger, BigInteger) enter = (BigInteger.Zero, BigInteger.One), leave = (BigInteger.One, BigInteger.One);
            void Slab(BigInteger p, BigInteger d, BigInteger lo, BigInteger hi)
            {
                if (d.IsZero) return;
                BigInteger nIn = d.Sign > 0 ? lo - p : hi - p, nOut = d.Sign > 0 ? hi - p : lo - p, den = d;
                if (den.Sign < 0) { nIn = -nIn; nOut = -nOut; den = -den; }
                if (nIn * enter.Item2 > enter.Item1 * den) enter = (nIn, den);
                if (nOut * leave.Item2 < leave.Item1 * den) leave = (nOut, den);
            }
            Slab(px, qx - px, x0, x1);
            Slab(py, qy - py, y0, y1);
            return (enter, leave);
        }

        long BucketOf(long k) => k >= 0 ? k / bucket : -((-k + bucket - 1) / bucket);

        /// <summary> the buckets a segment between two pixels may touch: sampled at a quarter bucket, with all neighbours </summary>
        IEnumerable<(long, long)> Buckets((long, long) a, (long, long) b)
        {
            var seen = new HashSet<(long, long)>();
            long dx = b.Item1 - a.Item1, dy = b.Item2 - a.Item2;
            long steps = Math.Max(1, 4 * Math.Max(Math.Abs(dx), Math.Abs(dy)) / bucket + 1);
            for (long i = 0; i <= steps; ++i) {
                long x = a.Item1 + (long)Math.Round((double)dx * i / steps), y = a.Item2 + (long)Math.Round((double)dy * i / steps);
                long gx = BucketOf(x), gy = BucketOf(y);
                for (long ox = -1; ox <= 1; ++ox) for (long oy = -1; oy <= 1; ++oy) if (seen.Add((gx + ox, gy + oy))) yield return (gx + ox, gy + oy);
            }
        }

        // ------------------------------------------------------------ the algorithm

        void Compute()
        {
            for (int i = 0; i < input.Points.Count; ++i) hot.Add((KX[i], KY[i]));
            AddCrossings();
            while (true) {
                if (++Rounds > RoundLimit) throw new InvalidOperationException("snap rounding did not settle within " + RoundLimit + " rounds");
                RouteAll();
                var critical = Critical();
                if (critical.Count == 0) break;
                int before = hot.Count;
                Restore(critical);
                if (hot.Count == before) throw new InvalidOperationException("snap rounding: a round added no hot pixel");
            }
            Build();
        }

        /// <summary> the pixels of the proper crossings of the input constraints (touching ends are input points, already hot) </summary>
        void AddCrossings()
        {
            var byBucket = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < routed.Count; ++i)
                foreach (var key in Buckets((KX[routed[i].a], KY[routed[i].a]), (KX[routed[i].b], KY[routed[i].b]))) {
                    if (!byBucket.TryGetValue(key, out var list)) byBucket[key] = list = new List<int>();
                    list.Add(i);
                }
            var tested = new HashSet<(int, int)>();
            foreach (var key in byBucket.Keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2)) {
                var list = byBucket[key];
                for (int u = 0; u < list.Count; ++u)
                    for (int v = u + 1; v < list.Count; ++v) {
                        int i = Math.Min(list[u], list[v]), j = Math.Max(list[u], list[v]);
                        if (!tested.Add((i, j))) continue;
                        var (a, b) = routed[i]; var (c, d) = routed[j];
                        int o1 = Orient(X[a], Y[a], X[b], Y[b], X[c], Y[c]), o2 = Orient(X[a], Y[a], X[b], Y[b], X[d], Y[d]);
                        int o3 = Orient(X[c], Y[c], X[d], Y[d], X[a], Y[a]), o4 = Orient(X[c], Y[c], X[d], Y[d], X[b], Y[b]);
                        if (o1 * o2 >= 0 || o3 * o4 >= 0) continue;
                        // a + t (b - a), t = cross(c - a, d - c) / cross(b - a, d - c)
                        BigInteger den = (X[b] - X[a]) * (Y[d] - Y[c]) - (Y[b] - Y[a]) * (X[d] - X[c]);
                        BigInteger num = (X[c] - X[a]) * (Y[d] - Y[c]) - (Y[c] - Y[a]) * (X[d] - X[c]);
                        if (den.Sign < 0) { den = -den; num = -num; }
                        hot.Add((PixelOf(X[a] * den + num * (X[b] - X[a]), den), PixelOf(Y[a] * den + num * (Y[b] - Y[a]), den)));
                        ++Crossings;
                    }
            }
        }

        /// <summary> snap rounding: every constraint becomes the chain through the centres of the hot pixels it touches, in order </summary>
        void IndexHot()
        {
            hotByBucket = new Dictionary<(long, long), List<(long, long)>>();
            foreach (var p in hot.OrderBy(p => p.Item1).ThenBy(p => p.Item2)) {
                var key = (BucketOf(p.Item1), BucketOf(p.Item2));
                if (!hotByBucket.TryGetValue(key, out var list)) hotByBucket[key] = list = new List<(long, long)>();
                list.Add(p);
            }
        }

        void RouteAll()
        {
            IndexHot();
            chains = new List<List<(long, long)>>(routed.Count);
            foreach (var (a, b) in routed) {
                var met = new List<(((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) t, (long, long) p)>();
                var seen = new HashSet<(long, long)>();
                foreach (var key in Buckets((KX[a], KY[a]), (KX[b], KY[b])))
                    if (hotByBucket.TryGetValue(key, out var list))
                        foreach (var p in list)
                            if (seen.Add(p) && Meets(X[a], Y[a], X[b], Y[b], p, false)) met.Add((Span(X[a], Y[a], X[b], Y[b], p), p));
                met.Sort((u, v) => {
                    int c = (u.t.entry.num * v.t.entry.den).CompareTo(v.t.entry.num * u.t.entry.den);
                    if (c != 0) return c;
                    c = (u.t.exit.num * v.t.exit.den).CompareTo(v.t.exit.num * u.t.exit.den);
                    if (c != 0) return c;
                    c = u.p.Item1.CompareTo(v.p.Item1);
                    return c != 0 ? c : u.p.Item2.CompareTo(v.p.Item2);
                });
                var chain = new List<(long, long)>();
                foreach (var m in met) if (chain.Count == 0 || !chain[chain.Count - 1].Equals(m.p)) chain.Add(m.p);
                chains.Add(chain);
            }
        }

        /// <summary> critical pixels: a hot pixel whose interior an output edge crosses, the edge's ends outside it </summary>
        List<((long, long) cp, int chain, int edge)> Critical()
        {
            var list = new List<((long, long), int, int)>();
            for (int c = 0; c < chains.Count; ++c) {
                var ch = chains[c];
                for (int e = 0; e + 1 < ch.Count; ++e) {
                    var p = ch[e]; var q = ch[e + 1];
                    foreach (var key in Buckets(p, q))
                        if (hotByBucket.TryGetValue(key, out var pixels))
                            foreach (var h in pixels)
                                if (!h.Equals(p) && !h.Equals(q) && Meets(Cx(p), Cy(p), Cx(q), Cy(q), h, true)) list.Add((h, c, e));
                }
            }
            return list;
        }

        /// <summary>
        /// SRR's Restore with ChoosePixel (their Table II), for each critical pixel and edge: a free pixel next to it in the envelope of the
        /// edge's input segment that routes the edge around it, nearest to the segment (R1, spread); else the critical pixel itself if the
        /// input segment crosses it (R2, snap); else the nearest such pixel even if not free (R3, spread). All choices of a round are made
        /// on the same arrangement, so the result does not depend on the order (their Proposition 4.17).
        /// </summary>
        void Restore(List<((long, long) cp, int chain, int edge)> critical)
        {
            var edgesByBucket = new Dictionary<(long, long), List<(int, int)>>();
            for (int c = 0; c < chains.Count; ++c)
                for (int e = 0; e + 1 < chains[c].Count; ++e)
                    foreach (var key in Buckets(chains[c][e], chains[c][e + 1])) {
                        if (!edgesByBucket.TryGetValue(key, out var list)) edgesByBucket[key] = list = new List<(int, int)>();
                        list.Add((c, e));
                    }
            bool Free((long, long) p)
            {
                if (!edgesByBucket.TryGetValue((BucketOf(p.Item1), BucketOf(p.Item2)), out var list)) return true;
                foreach (var (c, e) in list) {
                    var a = chains[c][e]; var b = chains[c][e + 1];
                    if (Meets(Cx(a), Cy(a), Cx(b), Cy(b), p, true)) return false;
                }
                return true;
            }
            var added = new List<(long, long)>();
            foreach (var (cp, c, e) in critical) {
                var (sa, sb) = routed[c];
                var a = chains[c][e]; var b = chains[c][e + 1];
                // the critical pixel itself is never in the envelope here: a segment that crossed it would have been routed through it
                var useful = new List<(long, long)>();
                for (long dx = -1; dx <= 1; ++dx) for (long dy = -1; dy <= 1; ++dy) {
                    if (dx == 0 && dy == 0) continue;
                    var p = (cp.Item1 + dx, cp.Item2 + dy);
                    if (hot.Contains(p) || !Meets(X[sa], Y[sa], X[sb], Y[sb], p, true)) continue;
                    if (Meets(Cx(a), Cy(a), Cx(p), Cy(p), cp, true) || Meets(Cx(p), Cy(p), Cx(b), Cy(b), cp, true)) continue;
                    useful.Add(p);
                }
                var free = useful.Where(Free).ToList();
                (long, long) chosen;
                if (free.Count > 0) { chosen = Nearest(free, sa, sb); ++Spread; }
                else if (useful.Count > 0) { chosen = Nearest(useful, sa, sb); ++ForcedSpread; }
                else {
                    // no useful pixel left next to it: the nearest pixel of the envelope that is not hot yet, within two pixels, on the
                    // part of the segment between the edge's ends
                    var wider = new List<(long, long)>();
                    for (long dx = -2; dx <= 2; ++dx) for (long dy = -2; dy <= 2; ++dy) {
                        var p = (cp.Item1 + dx, cp.Item2 + dy);
                        if (!hot.Contains(p) && Meets(X[sa], Y[sa], X[sb], Y[sb], p, true) && Between(sa, sb, a, b, p)) wider.Add(p);
                    }
                    if (wider.Count == 0) throw new InvalidOperationException("snap rounding: no pixel to restore critical pixel " + cp + " on the chain of segment "
                        + input.Points[sa] + " - " + input.Points[sb] + " (SRR, Proposition 4.9)");
                    chosen = Nearest(wider, sa, sb); ++Snapped;
                }
                added.Add(chosen);
            }
            foreach (var p in added) hot.Add(p);
        }

        /// <summary> does pixel p's centre project onto segment s strictly between the centres of a and b (in the order of s) </summary>
        bool Between(int sa, int sb, (long, long) a, (long, long) b, (long, long) p)
        {
            BigInteger dx = X[sb] - X[sa], dy = Y[sb] - Y[sa];
            BigInteger Along((long, long) q) => (Cx(q) - X[sa]) * dx + (Cy(q) - Y[sa]) * dy;
            BigInteger ta = Along(a), tb = Along(b), tp = Along(p);
            return BigInteger.Min(ta, tb) < tp && tp < BigInteger.Max(ta, tb);
        }

        /// <summary> the pixel whose centre is nearest to segment ab, exactly; ties by pixel coordinates </summary>
        (long, long) Nearest(List<(long, long)> pixels, int a, int b)
        {
            BigInteger dx = X[b] - X[a], dy = Y[b] - Y[a], len2 = dx * dx + dy * dy;
            (BigInteger num, BigInteger den) Dist2((long, long) p)
            {
                BigInteger ux = Cx(p) - X[a], uy = Cy(p) - Y[a], dot = ux * dx + uy * dy;
                if (dot.Sign <= 0 || len2.IsZero) return (ux * ux + uy * uy, BigInteger.One);
                if (dot >= len2) { BigInteger vx = Cx(p) - X[b], vy = Cy(p) - Y[b]; return (vx * vx + vy * vy, BigInteger.One); }
                BigInteger cr = ux * dy - uy * dx;
                return (cr * cr, len2);
            }
            var best = pixels[0]; var bd = Dist2(best);
            foreach (var p in pixels.Skip(1)) {
                var d = Dist2(p);
                int c = (d.num * bd.den).CompareTo(bd.num * d.den);
                if (c < 0 || (c == 0 && (p.Item1 < best.Item1 || (p.Item1 == best.Item1 && p.Item2 < best.Item2)))) { best = p; bd = d; }
            }
            return best;
        }

        // ------------------------------------------------------------ the snapped input

        void Build()
        {
            int n = input.Points.Count;
            var points = new List<Vector2d>(n);
            var pixels = new List<(long, long)>(n);
            var rep = new Dictionary<(long, long), int>();
            var mergedInto = new Dictionary<int, int>();
            for (int i = 0; i < n; ++i) {
                var p = input.Points[i];
                var c = new Vector2d(KX[i] * w, KY[i] * w);
                points.Add(c); pixels.Add((KX[i], KY[i]));
                MaxDisplacement = Math.Max(MaxDisplacement, Math.Max(Math.Abs(c.x - p.x), Math.Abs(c.y - p.y)));
                if (rep.TryGetValue((KX[i], KY[i]), out int first)) { mergedInto.TryGetValue(first, out int m); mergedInto[first] = m + 1; }
                else rep.Add((KX[i], KY[i]), i);
            }
            // extra hot pixels (crossings and restore) as new points, in a deterministic order
            var source = new Dictionary<(long, long), int>();
            for (int c = 0; c < chains.Count; ++c) foreach (var p in chains[c]) if (!rep.ContainsKey(p) && !source.ContainsKey(p)) source.Add(p, c);
            foreach (var p in source.Keys.OrderBy(p => p.Item2).ThenBy(p => p.Item1)) { rep.Add(p, points.Count); points.Add(new Vector2d(p.Item1 * w, p.Item2 * w)); pixels.Add(p); }

            List<int> ChainOf(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                var chain = chains[routedIndex[key]].Select(p => rep[p]).ToList();
                if (key.Item1 != a) chain.Reverse();
                return chain;
            }
            var result = new ConstrainedMesh2Input { Points = points, HoleSeeds = new List<Vector2d>(), Regions = new List<Mesh2Region>() };
            foreach (var s in input.Segments) {
                if (s.A == s.B) { result.Segments.Add(new Mesh2Segment(rep[(KX[s.A], KY[s.A])], rep[(KX[s.A], KY[s.A])], s.Marker, s.Locked)); continue; }
                var chain = ChainOf(s.A, s.B);
                if (chain.Count < 2) { ++CollapsedSegments; continue; }
                for (int i = 0; i + 1 < chain.Count; ++i) result.Segments.Add(new Mesh2Segment(chain[i], chain[i + 1], s.Marker, s.Locked));
            }
            List<int> Expand(IReadOnlyList<int> ring)
            {
                var list = new List<int>();
                for (int i = 0; i < ring.Count; ++i) {
                    int a = ring[i], b = ring[(i + 1) % ring.Count];
                    var chain = a == b ? new List<int> { rep[(KX[a], KY[a])] } : ChainOf(a, b);
                    foreach (int v in chain) if (list.Count == 0 || list[list.Count - 1] != v) list.Add(v);
                }
                while (list.Count > 1 && list[0] == list[list.Count - 1]) list.RemoveAt(list.Count - 1);
                return list;
            }
            for (int r = 0; r < input.Rings.Count; ++r) {
                var ring = input.Rings[r];
                var list = Expand(ring.Vertices);
                if (list.Distinct().Count() >= 3 && !Area2(list, pixels).IsZero) { result.Rings.Add(new Mesh2Ring(list.ToArray(), ring.Hole, ring.Marker)); continue; }
                // a ring that collapsed to a line or a point removes and adds nothing; its pieces stay as segments
                ++CollapsedRings;
                Diagnostics.Add("snap: ring " + r + " collapsed below the tolerance" + (list.Distinct().Count() >= 2 ? "; its edges stay as segments" : ""));
                for (int i = 0; i < list.Count && list.Count >= 2; ++i)
                    if (list[i] != list[(i + 1) % list.Count]) result.Segments.Add(new Mesh2Segment(list[i], list[(i + 1) % list.Count], ring.Marker, false));
            }
            // a hole marker in the pixel of a vertex into which a contour (at least three input points) merged is dropped: the contour
            // collapsed, and the marker would remove the cell around it (clean-room answer 6, MESHING2D_SNAP_QA.md)
            var collapsedPixels = new HashSet<(long, long)>(mergedInto.Where(m => m.Value >= 2).Select(m => (KX[m.Key], KY[m.Key])));
            foreach (var seed in input.HoleSeeds) {
                if (collapsedPixels.Contains(SeedPixel(seed))) { ++DroppedSeeds; Diagnostics.Add("snap: hole seed " + seed + " dropped, its contour collapsed"); continue; }
                result.HoleSeeds.Add(seed);
            }
            foreach (var g in input.Regions) {
                if (g.Ring == null) { result.Regions.Add(g); continue; }
                var list = Expand(g.Ring);
                if (list.Distinct().Count() >= 3 && !Area2(list, pixels).IsZero) result.Regions.Add(Mesh2Region.ByRing(list.ToArray(), g.Label, g.MaxArea));
                else Diagnostics.Add("snap: region ring with label " + g.Label + " collapsed below the tolerance");
            }
            if (input.Heights != null) {
                var heights = new List<double>(input.Heights);
                foreach (var p in source.Keys.OrderBy(p => p.Item2).ThenBy(p => p.Item1)) {
                    var (a, b) = routed[source[p]];
                    double ax = input.Points[a].x, ay = input.Points[a].y, dx = input.Points[b].x - ax, dy = input.Points[b].y - ay;
                    double t = Math.Max(0, Math.Min(1, ((p.Item1 * w - ax) * dx + (p.Item2 * w - ay) * dy) / (dx * dx + dy * dy)));
                    heights.Add(input.Heights[a] + t * (input.Heights[b] - input.Heights[a]));
                }
                result.Heights = heights;
            }
            if (CollapsedSegments > 0) Diagnostics.Add("snap: " + CollapsedSegments + " segments collapsed below the tolerance");
            Result = result;
        }

        (long, long) SeedPixel(Vector2d seed)
        {
            ExactPoint3.Decompose(seed.x, out var mx, out int ex);
            ExactPoint3.Decompose(seed.y, out var my, out int ey);
            ExactPoint3.Decompose(w, out var mw, out int ew);
            // floor((x / w) + 1/2) with x and w as integers times powers of two
            long Pixel(BigInteger m, int e)
            {
                int lo = Math.Min(e, ew);
                BigInteger num = m.IsZero ? BigInteger.Zero : m << (e - lo), den = mw << (ew - lo);
                return (long)FloorDiv(2 * num + den, 2 * den);
            }
            return (Pixel(mx, ex), Pixel(my, ey));
        }

        /// <summary> twice the signed area of a ring of pixel centres, in pixel units, exactly </summary>
        static BigInteger Area2(List<int> ring, List<(long, long)> pixels)
        {
            BigInteger a = BigInteger.Zero;
            for (int i = 0; i < ring.Count; ++i) {
                var p = pixels[ring[i]]; var q = pixels[ring[(i + 1) % ring.Count]];
                a += new BigInteger(p.Item1) * q.Item2 - new BigInteger(q.Item1) * p.Item2;
            }
            return a;
        }
    }
}
