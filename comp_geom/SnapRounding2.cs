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
        public int Rounds, Crossings, Spread, Widened, ForcedSpread, CollapsedSegments, CollapsedRings, DroppedSeeds;
        /// <summary> the pixel grid, for refinement on the grid (all output vertices on it, owner decision 2026-11-01) </summary>
        public SnapGrid Grid;
        /// <summary> the input segments each output chain edge comes from (by the edge's end coordinates, either order), for O12 splits;
        /// several when the chains of several input segments share the edge </summary>
        public readonly Dictionary<(Vector2d, Vector2d), List<(Vector2d a, Vector2d b)>> Origin = new Dictionary<(Vector2d, Vector2d), List<(Vector2d, Vector2d)>>();

        /// <summary> the first input segment an output chain edge comes from, or false (a hull edge, or an edge the mesher made) </summary>
        public bool TryOrigin(Vector2d p, Vector2d q, out (Vector2d a, Vector2d b) origin)
        {
            origin = default((Vector2d, Vector2d));
            if (!Origin.TryGetValue(Ordered(p, q), out var list)) return false;
            origin = list[0];
            return true;
        }

        /// <summary> all input segments an output chain edge comes from (empty if none) </summary>
        public List<(Vector2d a, Vector2d b)> Origins(Vector2d p, Vector2d q) => Origin.TryGetValue(Ordered(p, q), out var list) ? list : new List<(Vector2d, Vector2d)>();

        static (Vector2d, Vector2d) Ordered(Vector2d p, Vector2d q) => p.x < q.x || (p.x == q.x && p.y <= q.y) ? (p, q) : (q, p);
        public readonly List<string> Diagnostics = new List<string>();

        const int RoundLimit = 1000;

        readonly ConstrainedMesh2Input input;
        readonly System.Threading.CancellationToken cancel;
        readonly int emin;                  // the common exponent: coordinates and pixel width are integers times 2^emin
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

        SnapRounding2(ConstrainedMesh2Input input, double tolerance, System.Threading.CancellationToken cancel)
        {
            this.input = input; this.cancel = cancel;
            w = tolerance;
            int n = input.Points.Count;
            // the common exponent of all coordinates and the pixel width
            var mant = new BigInteger[2 * n + 1];
            var exps = new int[2 * n + 1];
            emin = int.MaxValue;
            for (int i = 0; i <= 2 * n; ++i) {
                double v = i == 2 * n ? w : (i % 2 == 0 ? input.Points[i / 2].x : input.Points[i / 2].y);
                ExactPoint3.Decompose(v, out mant[i], out exps[i]);
                if (!mant[i].IsZero) emin = Math.Min(emin, exps[i]);
            }
            int e0 = emin;
            BigInteger Units(int i) => mant[i].IsZero ? BigInteger.Zero : mant[i] << (exps[i] - e0);
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
            // (at most 2^40 pixels from the origin: the centres k w then round to doubles within 2^-12 pixels, so the half-pixel separation
            // holds on the doubles up to that; a smaller tolerance is below the automatic one, 2^-41 of the magnitude, anyway)
            if (BigInteger.Abs(q) >= (BigInteger.One << 40)) throw new ArgumentException("SnapTolerance is too small for the coordinates: it must be at least 2^-40 of the largest coordinate");
            return (long)q;
        }

        static BigInteger FloorDiv(BigInteger a, BigInteger b)
        {
            var q = BigInteger.DivRem(a, b, out var r);
            if (!r.IsZero && (r.Sign < 0) != (b.Sign < 0)) q -= 1;
            return q;
        }

        /// <summary> Snap-rounds the input; throws ArgumentException for a tolerance that is not positive and finite or too small </summary>
        public static SnapRounding2 Apply(ConstrainedMesh2Input input, double tolerance, System.Threading.CancellationToken cancel = default(System.Threading.CancellationToken))
        {
            if (!(tolerance > 0) || double.IsInfinity(tolerance)) throw new ArgumentException("SnapTolerance must be positive and finite");
            var s = new SnapRounding2(input, tolerance, cancel);
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
                cancel.ThrowIfCancellationRequested();
                var list = byBucket[key];
                for (int u = 0; u < list.Count; ++u)
                    for (int v = u + 1; v < list.Count; ++v) {
                        int i = Math.Min(list[u], list[v]), j = Math.Max(list[u], list[v]);
                        if (!tested.Add((i, j))) continue;
                        var (a, b) = routed[i]; var (c, d) = routed[j];
                        // (a filter in doubles: boxes that do not meet cannot cross; the box test on doubles is exact)
                        var pa = input.Points[a]; var pb = input.Points[b]; var pc = input.Points[c]; var pd = input.Points[d];
                        if (Math.Max(pa.x, pb.x) < Math.Min(pc.x, pd.x) || Math.Max(pc.x, pd.x) < Math.Min(pa.x, pb.x)
                            || Math.Max(pa.y, pb.y) < Math.Min(pc.y, pd.y) || Math.Max(pc.y, pd.y) < Math.Min(pa.y, pb.y)) continue;
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
                cancel.ThrowIfCancellationRequested();
                long lx = Math.Min(KX[a], KX[b]) - 1, hx = Math.Max(KX[a], KX[b]) + 1, ly = Math.Min(KY[a], KY[b]) - 1, hy = Math.Max(KY[a], KY[b]) + 1;
                var met = new List<(((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) t, (long, long) p)>();
                var seen = new HashSet<(long, long)>();
                foreach (var key in Buckets((KX[a], KY[a]), (KX[b], KY[b])))
                    if (hotByBucket.TryGetValue(key, out var list))
                        foreach (var p in list)
                            if (p.Item1 >= lx && p.Item1 <= hx && p.Item2 >= ly && p.Item2 <= hy && seen.Add(p) && Meets(X[a], Y[a], X[b], Y[b], p, false))
                                met.Add((Span(X[a], Y[a], X[b], Y[b], p), p));
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
                if ((c & 255) == 0) cancel.ThrowIfCancellationRequested();
                for (int e = 0; e + 1 < ch.Count; ++e) {
                    var p = ch[e]; var q = ch[e + 1];
                    long lx = Math.Min(p.Item1, q.Item1), hx = Math.Max(p.Item1, q.Item1), ly = Math.Min(p.Item2, q.Item2), hy = Math.Max(p.Item2, q.Item2);
                    foreach (var key in Buckets(p, q))
                        if (hotByBucket.TryGetValue(key, out var pixels))
                            foreach (var h in pixels)
                                if (h.Item1 >= lx && h.Item1 <= hx && h.Item2 >= ly && h.Item2 <= hy && !h.Equals(p) && !h.Equals(q) && Meets(Cx(p), Cy(p), Cx(q), Cy(q), h, true))
                                    list.Add((h, c, e));
                }
            }
            return list;
        }

        /// <summary>
        /// SRR's Restore with ChoosePixel (their Table II), for each critical pixel and edge, among pixels not hot yet: a free pixel next to
        /// it in the envelope of the edge's input segment that routes the edge around it, nearest to the segment (R1, spread); else the
        /// nearest such pixel even if not free (R3, spread). R2 (the critical pixel itself) cannot apply here: a segment crossing it would
        /// have been routed through it. If no pixel next to it qualifies, the nearest pixel of the envelope within two pixels on the edge's
        /// span is taken (Widened). All choices of a round are made on the same arrangement, so the result does not depend on the order
        /// (their Proposition 4.17).
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
                    chosen = Nearest(wider, sa, sb); ++Widened;
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
            var rep = new Dictionary<(long, long), int>();
            for (int i = 0; i < n; ++i) {
                var p = input.Points[i];
                var c = new Vector2d(KX[i] * w, KY[i] * w);
                points.Add(c);
                MaxDisplacement = Math.Max(MaxDisplacement, Math.Max(Math.Abs(c.x - p.x), Math.Abs(c.y - p.y)));
                if (!rep.ContainsKey((KX[i], KY[i]))) rep.Add((KX[i], KY[i]), i);
            }
            // extra hot pixels (crossings and restore) as new points, in a deterministic order
            var source = new Dictionary<(long, long), int>();
            for (int c = 0; c < chains.Count; ++c) foreach (var p in chains[c]) if (!rep.ContainsKey(p) && !source.ContainsKey(p)) source.Add(p, c);
            foreach (var p in source.Keys.OrderBy(p => p.Item2).ThenBy(p => p.Item1)) { rep.Add(p, points.Count); points.Add(new Vector2d(p.Item1 * w, p.Item2 * w)); }

            List<int> ChainOf(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                var chain = chains[routedIndex[key]].Select(p => rep[p]).ToList();
                if (key.Item1 != a) chain.Reverse();
                return chain;
            }
            for (int c = 0; c < chains.Count; ++c) {
                var (oa, ob) = routed[c];
                for (int e = 0; e + 1 < chains[c].Count; ++e) {
                    var key = Ordered(points[rep[chains[c][e]]], points[rep[chains[c][e + 1]]]);
                    if (!Origin.TryGetValue(key, out var list)) Origin.Add(key, list = new List<(Vector2d, Vector2d)>());
                    var o = (input.Points[oa], input.Points[ob]);
                    if (!list.Contains(o)) list.Add(o);
                }
            }
            Grid = new SnapGrid(w);
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
                if (!Collapsed(list)) { result.Rings.Add(new Mesh2Ring(list.ToArray(), ring.Hole, ring.Marker)); continue; }
                // a ring that collapsed to a line or a point removes and adds nothing; its pieces stay as segments
                ++CollapsedRings;
                Diagnostics.Add("snap: ring " + r + " collapsed below the tolerance" + (list.Distinct().Count() >= 2 ? "; its edges stay as segments" : ""));
                for (int i = 0; i < list.Count && list.Count >= 2; ++i)
                    if (list[i] != list[(i + 1) % list.Count]) result.Segments.Add(new Mesh2Segment(list[i], list[(i + 1) % list.Count], ring.Marker, false));
            }
            // A hole or region seed within one pixel (per axis) of constraint geometry that snapping moved is dropped: the geometry moved by
            // up to half a pixel, so the seed's side of it is not reliable, and a seed left on the wrong side, or inside a contour that
            // collapsed, would remove or label the cell around it, eg the whole domain (clean-room answer 6, MESHING2D_SNAP_QA.md; review of
            // #277: a hole collapsing to a line). A seed next to geometry that did not move keeps its meaning.
            MovedGeometry();
            foreach (var seed in input.HoleSeeds) {
                if (NearMoved(seed)) { ++DroppedSeeds; Diagnostics.Add("snap: hole seed " + seed + " dropped: within a pixel of constraints that snapping moved"); continue; }
                result.HoleSeeds.Add(seed);
            }
            foreach (var g in input.Regions) {
                if (g.Ring == null) {
                    if (NearMoved(g.Seed)) { ++DroppedSeeds; Diagnostics.Add("snap: region seed " + g.Seed + " with label " + g.Label + " dropped: within a pixel of constraints that snapping moved"); continue; }
                    result.Regions.Add(g);
                    continue;
                }
                var list = Expand(g.Ring);
                if (!Collapsed(list)) result.Regions.Add(Mesh2Region.ByRing(list.ToArray(), g.Label, g.MaxArea));
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

        /// <summary>
        /// a ring of output points encloses nothing by the even-odd rule: every edge is passed an even number of times (a ring collapsed to
        /// a line or a point); a self-intersecting ring whose signed area is zero, eg a bowtie, still encloses its odd parts (review of #277)
        /// </summary>
        static bool Collapsed(List<int> ring)
        {
            if (ring.Count < 2) return true;
            var count = new Dictionary<(int, int), int>();
            for (int i = 0; i < ring.Count; ++i) {
                int a = ring[i], b = ring[(i + 1) % ring.Count];
                if (a == b) continue;
                var key = a < b ? (a, b) : (b, a);
                count.TryGetValue(key, out int c);
                count[key] = c + 1;
            }
            return count.Values.All(c => c % 2 == 0);
        }

        readonly List<((long, long) a, (long, long) b)> movedEdges = new List<((long, long), (long, long))>();
        readonly List<(long, long)> movedPoints = new List<(long, long)>();

        /// <summary> the chain edges that do not lie on their input segment, and the points of constraints that collapsed to a point </summary>
        void MovedGeometry()
        {
            for (int c = 0; c < chains.Count; ++c) {
                var (oa, ob) = routed[c];
                var ch = chains[c];
                bool On((long, long) p)
                {
                    BigInteger px = Cx(p), py = Cy(p);
                    return Orient(X[oa], Y[oa], X[ob], Y[ob], px, py) == 0 && px >= BigInteger.Min(X[oa], X[ob]) && px <= BigInteger.Max(X[oa], X[ob])
                        && py >= BigInteger.Min(Y[oa], Y[ob]) && py <= BigInteger.Max(Y[oa], Y[ob]);
                }
                if (ch.Count == 1) { movedPoints.Add(ch[0]); continue; }
                for (int e = 0; e + 1 < ch.Count; ++e) if (!On(ch[e]) || !On(ch[e + 1])) movedEdges.Add((ch[e], ch[e + 1]));
            }
        }

        /// <summary> the closed square of half-width one pixel around the seed meets moved constraint geometry, exactly </summary>
        bool NearMoved(Vector2d seed)
        {
            if (movedEdges.Count == 0 && movedPoints.Count == 0) return false;
            // the seed in units of 2^emin, doubled like the other coordinates; den > 1 if its exponent is below emin
            ExactPoint3.Decompose(seed.x, out var mx, out int ex);
            ExactPoint3.Decompose(seed.y, out var my, out int ey);
            int lo = Math.Min(emin, Math.Min(mx.IsZero ? emin : ex, my.IsZero ? emin : ey));
            BigInteger den = BigInteger.One << (emin - lo);
            BigInteger sx = mx.IsZero ? BigInteger.Zero : 2 * (mx << (ex - lo)), sy = my.IsZero ? BigInteger.Zero : 2 * (my << (ey - lo));
            BigInteger r = 2 * W * den, x0 = sx - r, x1 = sx + r, y0 = sy - r, y1 = sy + r;
            foreach (var p in movedPoints) {
                BigInteger px = Cx(p) * den, py = Cy(p) * den;
                if (px >= x0 && px <= x1 && py >= y0 && py <= y1) return true;
            }
            foreach (var (a, b) in movedEdges) {
                BigInteger ax = Cx(a) * den, ay = Cy(a) * den, bx = Cx(b) * den, by = Cy(b) * den;
                if (BigInteger.Max(ax, bx) < x0 || BigInteger.Min(ax, bx) > x1 || BigInteger.Max(ay, by) < y0 || BigInteger.Min(ay, by) > y1) continue;
                int pos = 0, neg = 0;
                foreach (var (cx, cy) in new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }) {
                    int o = Orient(ax, ay, bx, by, cx, cy);
                    if (o > 0) ++pos; else if (o < 0) ++neg;
                }
                if (pos < 4 && neg < 4) return true;
            }
            return false;
        }

        /// <summary> the chain of the constraint between input points a and b, as output coordinates from a to b (for tests) </summary>
        public List<Vector2d> ChainPoints(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            var chain = chains[routedIndex[key]].Select(p => new Vector2d(p.Item1 * w, p.Item2 * w)).ToList();
            if (key.Item1 != a) chain.Reverse();
            return chain;
        }

    }

    /// <summary>
    /// The pixel grid of a snap tolerance (D14): pixels of width w centred on the points k w, half-open (closed left and bottom), anchored
    /// at the origin. Every decision is exact: the coordinates and w as integers on their common power of two.
    /// </summary>
    internal sealed class SnapGrid
    {
        public readonly double W;
        public SnapGrid(double w) { W = w; }

        /// <summary> the pixel index of x: floor(x / w + 1/2), exactly </summary>
        public long Pixel(double x)
        {
            ExactPoint3.Decompose(x, out var mx, out int ex);
            ExactPoint3.Decompose(W, out var mw, out int ew);
            int lo = mx.IsZero ? ew : Math.Min(ex, ew);
            BigInteger num = mx.IsZero ? BigInteger.Zero : mx << (ex - lo), den = mw << (ew - lo);
            var q = BigInteger.DivRem(2 * num + den, 2 * den, out var r);
            if (!r.IsZero && r.Sign < 0) q -= 1;
            return (long)q;
        }

        public double Centre(long k) => k * W;

        /// <summary> the centre of the pixel holding the circumcentre of triangle abc, exactly (false for a degenerate triangle) </summary>
        public bool CircumcentrePixelCentre(Vector2d a, Vector2d b, Vector2d c, out Vector2d centre)
        {
            centre = default(Vector2d);
            var v = new[] { a.x, a.y, b.x, b.y, c.x, c.y, W };
            var m = new BigInteger[7]; var e = new int[7];
            int lo = int.MaxValue;
            for (int i = 0; i < 7; ++i) { ExactPoint3.Decompose(v[i], out m[i], out e[i]); if (!m[i].IsZero) lo = Math.Min(lo, e[i]); }
            BigInteger U(int i) => m[i].IsZero ? BigInteger.Zero : m[i] << (e[i] - lo);
            BigInteger ax = U(0), ay = U(1), bx = U(2) - ax, by = U(3) - ay, cx = U(4) - ax, cy = U(5) - ay, w = U(6);
            BigInteger d = 2 * (bx * cy - by * cx);
            if (d.IsZero) return false;
            BigInteger b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
            // circumcentre = a + (ux, uy) / d; pixel = floor((ax + ux / d) / w + 1/2) = floor((2 (ax d + ux) + w d) / (2 w d))
            BigInteger ux = cy * b2 - by * c2, uy = bx * c2 - cx * b2;
            if (d.Sign < 0) { d = -d; ux = -ux; uy = -uy; }
            BigInteger Floor(BigInteger n, BigInteger q) { var f = BigInteger.DivRem(n, q, out var r); if (!r.IsZero && r.Sign < 0) f -= 1; return f; }
            BigInteger kx = Floor(2 * (ax * d + ux) + w * d, 2 * w * d), ky = Floor(2 * (ay * d + uy) + w * d, 2 * w * d);
            if (BigInteger.Abs(kx) >= (BigInteger.One << 52) || BigInteger.Abs(ky) >= (BigInteger.One << 52)) return false;
            centre = new Vector2d(Centre((long)kx), Centre((long)ky));
            return true;
        }
        public Vector2d Round(Vector2d p) => new Vector2d(Centre(Pixel(p.x)), Centre(Pixel(p.y)));
        public bool OnGrid(Vector2d p) => Round(p) == p;

        /// <summary> does segment ab cross the open interior of pixel (kx, ky), exactly </summary>
        public bool CrossesInterior(Vector2d a, Vector2d b, long kx, long ky)
        {
            var v = new[] { a.x, a.y, b.x, b.y, W };
            var m = new BigInteger[5]; var e = new int[5];
            int lo = int.MaxValue;
            for (int i = 0; i < 5; ++i) { ExactPoint3.Decompose(v[i], out m[i], out e[i]); if (!m[i].IsZero) lo = Math.Min(lo, e[i]); }
            BigInteger U(int i) => m[i].IsZero ? BigInteger.Zero : 2 * (m[i] << (e[i] - lo));
            BigInteger px = U(0), py = U(1), qx = U(2), qy = U(3), w = U(4) / 2;
            BigInteger x0 = (2 * kx - 1) * w, x1 = (2 * kx + 1) * w, y0 = (2 * ky - 1) * w, y1 = (2 * ky + 1) * w;
            if (BigInteger.Max(px, qx) <= x0 || BigInteger.Min(px, qx) >= x1 || BigInteger.Max(py, qy) <= y0 || BigInteger.Min(py, qy) >= y1) return false;
            if (px == qx && py == qy) return true;
            int pos = 0, neg = 0;
            foreach (var (cx, cy) in new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }) {
                int o = ((qx - px) * (cy - py) - (qy - py) * (cx - px)).Sign;
                if (o > 0) ++pos; else if (o < 0) ++neg;
            }
            return pos > 0 && neg > 0;
        }
    }
}
