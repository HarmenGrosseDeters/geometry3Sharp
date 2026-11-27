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
    /// Drift: the rounding of the input moves a point by at most half a pixel per axis. Vertices that refinement adds are hot pixels of the
    /// mesher's output (MESHING2D_HULL_GRID.md, Q17) and can reroute other chains, so at a sub-pixel wedge a chain may end up to about 2 pixels
    /// from its input edge (owner decision 2026-11-10).
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
        /// <summary> per input segment, the index of its first piece in Result.Segments (one more entry: the end of the last chain) </summary>
        internal int[] SegmentChainStart;
        /// <summary> the largest distance by which an input point moved, per axis (at most half a pixel) </summary>
        public double MaxDisplacement;
        public int Rounds, Crossings, Spread, Widened, ForcedSpread, CollapsedSegments, CollapsedRings, DroppedSeeds;
        /// <summary> the input had outer rings and snapping collapsed every one of them: the domain is empty, not the hull (the result
        /// has no outer ring left, which alone would read as a hull domain, D13) </summary>
        public bool OuterRingsCollapsed;
        /// <summary> the pixel grid, for refinement on the grid (all output vertices on it, owner decision 2026-11-01) </summary>
        public SnapGrid Grid;
        /// <summary> the input segments each output chain edge comes from (by the edge's end coordinates, either order), for O12 splits;
        /// several when the chains of several input segments share the edge </summary>
        // (private: a Dictionary has no specified enumeration order, so nothing outside this class may enumerate it; the members below only
        // look entries up, and Build adds them in chain order. DeterminismStage1ObligationTests.SnapRounding2_Origin_IsNotVisible)
        readonly Dictionary<(Vector2d, Vector2d), List<(Vector2d a, Vector2d b)>> Origin = new Dictionary<(Vector2d, Vector2d), List<(Vector2d, Vector2d)>>();

        /// <summary> the first input segment an output chain edge comes from, or false (a hull edge, or an edge the mesher made) </summary>
        public bool TryOrigin(Vector2d p, Vector2d q, out (Vector2d a, Vector2d b) origin)
        {
            origin = default((Vector2d, Vector2d));
            if (!Origin.TryGetValue(Ordered(p, q), out var list)) return false;
            origin = list[0];
            return true;
        }

        /// <summary> all input segments an output chain edge comes from (empty if none), in chain order; read only: it is the stored list </summary>
        public IReadOnlyList<(Vector2d a, Vector2d b)> Origins(Vector2d p, Vector2d q) => Origin.TryGetValue(Ordered(p, q), out var list) ? (IReadOnlyList<(Vector2d a, Vector2d b)>)list : new List<(Vector2d, Vector2d)>();

        static (Vector2d, Vector2d) Ordered(Vector2d p, Vector2d q) => p.x < q.x || (p.x == q.x && p.y <= q.y) ? (p, q) : (q, p);
        public readonly List<string> Diagnostics = new List<string>();

        const int RoundLimit = 1000;

        readonly ConstrainedMesh2Input input;
        /// <summary> the number of points of the input; the snapped input's points from here on are hot pixel centres it made </summary>
        internal int InputCount => input.Points.Count;
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

        /// <summary> culture-invariant text of an integer or a pixel (the minus sign of a culture is not the same everywhere; O1 of DETERMINISM_PROOF.md 5.4) </summary>
        static string I(long v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        static string I((long, long) p) => "(" + I(p.Item1) + ", " + I(p.Item2) + ")";

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

        // flat 2D k-d tree (BVH) over sorted hot pixels, rebuilt each round at the hot pixels' own density
        struct HotNode
        {
            public long MinX, MaxX, MinY, MaxY;
            public int Start, Count, Left, Right;
        }

        (long x, long y)[] hotPts;
        HotNode[] hotNodes;
        int hotNodeCount;
        // Shared scratch buffers for QueryHot and Restore/Free; neither method may be called re-entrantly.
        readonly List<(long, long)> queryHits = new List<(long, long)>();
        readonly int[] queryStack = new int[128];

        struct EdgeBox
        {
            public long AX, AY, BX, BY;
            public long MinX, MaxX, MinY, MaxY;
        }

        struct EdgeNode
        {
            public long MinX, MaxX, MinY, MaxY;
            public int Start, Count, Left, Right;
        }

        /// <summary> segment pq against the pixel: its open interior (interior), or Hobby's half-open square (contact) </summary>
        bool Meets(BigInteger px, BigInteger py, BigInteger qx, BigInteger qy, (long, long) k, bool interior)
        {
            BigInteger x0 = (2 * k.Item1 - 1) * W, x1 = (2 * k.Item1 + 1) * W, y0 = (2 * k.Item2 - 1) * W, y1 = (2 * k.Item2 + 1) * W;
            BigInteger lx = BigInteger.Min(px, qx), hx = BigInteger.Max(px, qx), ly = BigInteger.Min(py, qy), hy = BigInteger.Max(py, qy);
            if (hx < x0 || lx > x1 || hy < y0 || ly > y1) return false;
            int pos = 0, neg = 0;
            bool point = px == qx && py == qy;
            if (!point) {
                BigInteger dx = qx - px, dy = qy - py;
                int o0 = (dx * (y0 - py) - dy * (x0 - px)).Sign;
                if (o0 > 0) ++pos; else if (o0 < 0) ++neg;
                int o1 = (dx * (y0 - py) - dy * (x1 - px)).Sign;
                if (o1 > 0) ++pos; else if (o1 < 0) ++neg;
                int o2 = (dx * (y1 - py) - dy * (x1 - px)).Sign;
                if (o2 > 0) ++pos; else if (o2 < 0) ++neg;
                int o3 = (dx * (y1 - py) - dy * (x0 - px)).Sign;
                if (o3 > 0) ++pos; else if (o3 < 0) ++neg;
            }
            bool open = point ? (px > x0 && px < x1 && py > y0 && py < y1) : (hx > x0 && lx < x1 && hy > y0 && ly < y1 && pos > 0 && neg > 0);
            if (open) return true;
            if (interior) return false;
            if (!point && (pos == 4 || neg == 4)) return false;
            // the segment meets the closed square only on its boundary: count the left and bottom sides without the top-left and
            // bottom-right corners
            if (py == qy && (py == y0 || py == y1)) return py == y0 && hx >= x0 && lx < x1;
            if (px == qx && (px == x0 || px == x1)) return px == x0 && hy >= y0 && ly < y1;
            if (px >= x0 && px <= x1 && py >= y0 && py <= y1) return px < x1 && py < y1;
            if (qx >= x0 && qx <= x1 && qy >= y0 && qy <= y1) return qx < x1 && qy < y1;
            // through a corner: only the bottom-left one belongs to the pixel
            return Orient(px, py, qx, qy, x0, y0) == 0 && lx <= x0 && x0 <= hx && ly <= y0 && y0 <= hy;
        }

        /// <summary>
        /// Exact test whether the segment between pixel centres p and q crosses the open interior of pixel k:
        /// equivalent to Meets(Cx(p), Cy(p), Cx(q), Cy(q), k, true), with the common factor W &gt; 0 divided out.
        /// Uses exact 64-bit integer arithmetic when |dx|, |dy| &lt;= 2^29 (|det| &lt;= 2^61 + 2^31 &lt; 2^63), else BigInteger.
        /// </summary>
        internal static bool MeetsPixelCentreInterior((long, long) p, (long, long) q, (long, long) k)
        {
            long px = p.Item1, py = p.Item2, qx = q.Item1, qy = q.Item2, kx = k.Item1, ky = k.Item2;
            long lx = px < qx ? px : qx, hx = px > qx ? px : qx;
            long ly = py < qy ? py : qy, hy = py > qy ? py : qy;
            if (hx < kx || lx > kx || hy < ky || ly > ky) return false;
            if (px == qx && py == qy) return px == kx && py == ky;
            long dpx = qx - px, dpy = qy - py;
            const long SafeSpan = 1L << 29;
            if (Math.Abs(dpx) <= SafeSpan && Math.Abs(dpy) <= SafeSpan) {
                long dx = 2 * dpx, dy = 2 * dpy;
                long ux0 = 2 * (kx - px) - 1, ux1 = ux0 + 2;
                long uy0 = 2 * (ky - py) - 1, uy1 = uy0 + 2;
                int pos = 0, neg = 0;
                long o0 = dx * uy0 - dy * ux0; if (o0 > 0) ++pos; else if (o0 < 0) ++neg;
                long o1 = dx * uy0 - dy * ux1; if (o1 > 0) ++pos; else if (o1 < 0) ++neg;
                long o2 = dx * uy1 - dy * ux1; if (o2 > 0) ++pos; else if (o2 < 0) ++neg;
                long o3 = dx * uy1 - dy * ux0; if (o3 > 0) ++pos; else if (o3 < 0) ++neg;
                return pos > 0 && neg > 0;
            }
            BigInteger bdx = (BigInteger)dpx * 2, bdy = (BigInteger)dpy * 2;
            BigInteger bux0 = (BigInteger)(kx - px) * 2 - 1, bux1 = bux0 + 2;
            BigInteger buy0 = (BigInteger)(ky - py) * 2 - 1, buy1 = buy0 + 2;
            int bpos = 0, bneg = 0;
            int bo0 = (bdx * buy0 - bdy * bux0).Sign; if (bo0 > 0) ++bpos; else if (bo0 < 0) ++bneg;
            int bo1 = (bdx * buy0 - bdy * bux1).Sign; if (bo1 > 0) ++bpos; else if (bo1 < 0) ++bneg;
            int bo2 = (bdx * buy1 - bdy * bux1).Sign; if (bo2 > 0) ++bpos; else if (bo2 < 0) ++bneg;
            int bo3 = (bdx * buy1 - bdy * bux0).Sign; if (bo3 > 0) ++bpos; else if (bo3 < 0) ++bneg;
            return bpos > 0 && bneg > 0;
        }

        /// <summary>
        /// Exact test of segment pq against Hobby's half-open pixel k (contact) when all coordinates and 2*W fit safely in
        /// 64-bit signed integers (|dx| * (|dy| + 2*W + 4) &lt; 2^60). Equivalent to Meets(px, py, qx, qy, k, false).
        /// </summary>
        static bool MeetsContactLong(long px, long py, long qx, long qy, long dx, long dy, long lx, long hx, long ly, long hy, long wL, (long, long) k)
        {
            long x0 = (2 * k.Item1 - 1) * wL, x1 = (2 * k.Item1 + 1) * wL;
            if (hx < x0 || lx > x1) return false;
            long y0 = (2 * k.Item2 - 1) * wL, y1 = (2 * k.Item2 + 1) * wL;
            if (hy < y0 || ly > y1) return false;
            int pos = 0, neg = 0;
            bool point = px == qx && py == qy;
            if (!point) {
                long o0 = dx * (y0 - py) - dy * (x0 - px); if (o0 > 0) ++pos; else if (o0 < 0) ++neg;
                long o1 = dx * (y0 - py) - dy * (x1 - px); if (o1 > 0) ++pos; else if (o1 < 0) ++neg;
                long o2 = dx * (y1 - py) - dy * (x1 - px); if (o2 > 0) ++pos; else if (o2 < 0) ++neg;
                long o3 = dx * (y1 - py) - dy * (x0 - px); if (o3 > 0) ++pos; else if (o3 < 0) ++neg;
            }
            bool open = point ? (px > x0 && px < x1 && py > y0 && py < y1) : (hx > x0 && lx < x1 && hy > y0 && ly < y1 && pos > 0 && neg > 0);
            if (open) return true;
            if (!point && (pos == 4 || neg == 4)) return false;
            if (py == qy && (py == y0 || py == y1)) return py == y0 && hx >= x0 && lx < x1;
            if (px == qx && (px == x0 || px == x1)) return px == x0 && hy >= y0 && ly < y1;
            if (px >= x0 && px <= x1 && py >= y0 && py <= y1) return px < x1 && py < y1;
            if (qx >= x0 && qx <= x1 && qy >= y0 && qy <= y1) return qx < x1 && qy < y1;
            return dx * (y0 - py) - dy * (x0 - px) == 0 && lx <= x0 && x0 <= hx && ly <= y0 && y0 <= hy;
        }

        /// <summary>
        /// where segment pq enters and leaves the closed pixel, as fractions of its length (num / den, den > 0); chains are ordered by
        /// entry, then by exit, so that a pixel the segment only touches where it starts comes before the one it then runs through
        /// (a segment starting on the side two pixels share; found by the fuzz test)
        /// </summary>
        ((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) Span(
            BigInteger px, BigInteger py, BigInteger dx, BigInteger dy, BigInteger absDx, BigInteger absDy, (long, long) k)
        {
            BigInteger x0 = (2 * k.Item1 - 1) * W, x1 = (2 * k.Item1 + 1) * W, y0 = (2 * k.Item2 - 1) * W, y1 = (2 * k.Item2 + 1) * W;
            (BigInteger, BigInteger) enter = (BigInteger.Zero, BigInteger.One), leave = (BigInteger.One, BigInteger.One);
            void Slab(BigInteger p, BigInteger d, BigInteger den, BigInteger lo, BigInteger hi)
            {
                if (d.IsZero) return;
                BigInteger nIn, nOut;
                if (d.Sign > 0) { nIn = lo - p; nOut = hi - p; }
                else { nIn = p - hi; nOut = p - lo; }
                if (nIn * enter.Item2 > enter.Item1 * den) enter = (nIn, den);
                if (nOut * leave.Item2 < leave.Item1 * den) leave = (nOut, den);
            }
            Slab(px, dx, absDx, x0, x1);
            Slab(py, dy, absDy, y0, y1);
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
                if (++Rounds > RoundLimit) throw new InvalidOperationException("snap rounding did not settle within " + I(RoundLimit) + " rounds");
                RouteAll();
                var critical = Critical();
                if (critical.Count == 0) break;
                int before = hot.Count;
                Restore(critical);
                if (hot.Count == before) throw new InvalidOperationException("snap rounding: a round added no hot pixel");
            }
            Build();
        }

        /// <summary>
        /// Conservative double orientation filter (returns 0 when uncertain, so caller falls back to exact BigInteger Orient).
        /// Margin 1e-12 * detsum is > 3000x Shewchuk's (3 + 16*eps)*eps bound; detsum > 1e-290 excludes subnormal underflow.
        /// </summary>
        static int OrientFilter(Vector2d a, Vector2d b, Vector2d c)
        {
            double dl = (b.x - a.x) * (c.y - a.y);
            double dr = (b.y - a.y) * (c.x - a.x);
            double det = dl - dr;
            double sum = Math.Abs(dl) + Math.Abs(dr);
            if (sum > 1e-290 && !double.IsInfinity(sum) && !double.IsNaN(det) && Math.Abs(det) > 1e-12 * sum)
                return det > 0 ? 1 : -1;
            return 0;
        }

        static void MergeSortSegsByMinX(int[] order, int[] tmp, double[] minX, int lo, int hi)
        {
            int len = hi - lo;
            if (len <= 16) {
                for (int i = lo + 1; i < hi; ++i) {
                    int key = order[i];
                    double kx = minX[key];
                    int j = i - 1;
                    while (j >= lo && (minX[order[j]] > kx || (minX[order[j]] == kx && order[j] > key))) { order[j + 1] = order[j]; --j; }
                    order[j + 1] = key;
                }
                return;
            }
            int mid = lo + (len >> 1);
            MergeSortSegsByMinX(order, tmp, minX, lo, mid);
            MergeSortSegsByMinX(order, tmp, minX, mid, hi);
            int p = lo, q = mid, dst = lo;
            while (p < mid && q < hi) {
                int ip = order[p], iq = order[q];
                if (minX[ip] > minX[iq] || (minX[ip] == minX[iq] && ip > iq)) tmp[dst++] = order[q++];
                else tmp[dst++] = order[p++];
            }
            while (p < mid) tmp[dst++] = order[p++];
            while (q < hi) tmp[dst++] = order[q++];
            for (int i = lo; i < hi; ++i) order[i] = tmp[i];
        }

        void TestCrossingPair(int i, int j)
        {
            var (a, b) = routed[i]; var (c, d) = routed[j];
            var pa = input.Points[a]; var pb = input.Points[b]; var pc = input.Points[c]; var pd = input.Points[d];
            int o1 = OrientFilter(pa, pb, pc); if (o1 == 0) o1 = Orient(X[a], Y[a], X[b], Y[b], X[c], Y[c]);
            int o2 = OrientFilter(pa, pb, pd); if (o2 == 0) o2 = Orient(X[a], Y[a], X[b], Y[b], X[d], Y[d]);
            if (o1 * o2 >= 0) return;
            int o3 = OrientFilter(pc, pd, pa); if (o3 == 0) o3 = Orient(X[c], Y[c], X[d], Y[d], X[a], Y[a]);
            int o4 = OrientFilter(pc, pd, pb); if (o4 == 0) o4 = Orient(X[c], Y[c], X[d], Y[d], X[b], Y[b]);
            if (o3 * o4 >= 0) return;
            // a + t (b - a), t = cross(c - a, d - c) / cross(b - a, d - c)
            BigInteger den = (X[b] - X[a]) * (Y[d] - Y[c]) - (Y[b] - Y[a]) * (X[d] - X[c]);
            BigInteger num = (X[c] - X[a]) * (Y[d] - Y[c]) - (Y[c] - Y[a]) * (X[d] - X[c]);
            if (den.Sign < 0) { den = -den; num = -num; }
            hot.Add((PixelOf(X[a] * den + num * (X[b] - X[a]), den), PixelOf(Y[a] * den + num * (Y[b] - Y[a]), den)));
            ++Crossings;
        }

        /// <summary> the pixels of the proper crossings of the input constraints (touching ends are input points, already hot) </summary>
        void AddCrossings()
        {
            int m = routed.Count;
            if (m > 64) {
                // Sweep along the axis with shorter typical segment span (Y for mostly horizontal bundles, X otherwise).
                var minX = new double[m]; var maxX = new double[m]; var minY = new double[m]; var maxY = new double[m];
                var order = new int[m];
                int moreHorizontal = 0;
                for (int i = 0; i < m; ++i) {
                    var (a, b) = routed[i];
                    var pa = input.Points[a]; var pb = input.Points[b];
                    double lx = Math.Min(pa.x, pb.x), hx = Math.Max(pa.x, pb.x);
                    double ly = Math.Min(pa.y, pb.y), hy = Math.Max(pa.y, pb.y);
                    minX[i] = lx; maxX[i] = hx; minY[i] = ly; maxY[i] = hy;
                    if (hx - lx > hy - ly) ++moreHorizontal;
                    order[i] = i;
                }
                bool sweepY = moreHorizontal > (m >> 1);
                var sweepLo = sweepY ? minY : minX;
                var sweepHi = sweepY ? maxY : maxX;
                var otherLo = sweepY ? minX : minY;
                var otherHi = sweepY ? maxX : maxY;
                MergeSortSegsByMinX(order, new int[m], sweepLo, 0, m);
                for (int u = 0; u < m; ++u) {
                    if ((u & 63) == 0) cancel.ThrowIfCancellationRequested();
                    int i = order[u];
                    double hiS = sweepHi[i], loO = otherLo[i], hiO = otherHi[i];
                    for (int v = u + 1; v < m; ++v) {
                        int j = order[v];
                        if (sweepLo[j] > hiS) break;
                        if (otherHi[j] < loO || otherLo[j] > hiO) continue;
                        TestCrossingPair(i, j);
                    }
                }
                return;
            }
            var byBucket = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < m; ++i)
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
                        TestCrossingPair(i, j);
                    }
            }
        }

        // deterministic merge sort of pixel slice [lo, hi) by axis (0: (x, y), 1: (y, x)), pure integer comparisons
        static void MergeSortPixels((long x, long y)[] arr, (long x, long y)[] tmp, int lo, int hi, int axis)
        {
            int len = hi - lo;
            if (len <= 16) {
                for (int i = lo + 1; i < hi; ++i) {
                    var key = arr[i];
                    int j = i - 1;
                    while (j >= lo && PixelGreater(arr[j], key, axis)) { arr[j + 1] = arr[j]; --j; }
                    arr[j + 1] = key;
                }
                return;
            }
            int mid = lo + (len >> 1);
            MergeSortPixels(arr, tmp, lo, mid, axis);
            MergeSortPixels(arr, tmp, mid, hi, axis);
            int p = lo, q = mid, dst = lo;
            while (p < mid && q < hi) {
                if (PixelGreater(arr[p], arr[q], axis)) tmp[dst++] = arr[q++];
                else tmp[dst++] = arr[p++];
            }
            while (p < mid) tmp[dst++] = arr[p++];
            while (q < hi) tmp[dst++] = arr[q++];
            for (int i = lo; i < hi; ++i) arr[i] = tmp[i];
        }

        static bool PixelGreater((long x, long y) a, (long x, long y) b, int axis)
            => axis == 0 ? (a.x > b.x || (a.x == b.x && a.y > b.y)) : (a.y > b.y || (a.y == b.y && a.x > b.x));

        /// <summary> snap rounding: builds a flat balanced 2D k-d tree over the hot pixels at their own density </summary>
        void IndexHot()
        {
            int count = hot.Count;
            hotPts = new (long, long)[count];
            int idx = 0;
            foreach (var p in hot.OrderBy(p => p.Item1).ThenBy(p => p.Item2)) hotPts[idx++] = p;
            if (count == 0) { hotNodes = new HotNode[0]; hotNodeCount = 0; return; }
            hotNodes = new HotNode[Math.Max(1, 2 * count)];
            hotNodeCount = 0;
            var tmp = new (long, long)[count];
            BuildHotNode(tmp, 0, count);
        }

        int BuildHotNode((long, long)[] tmp, int lo, int hi)
        {
            int id = hotNodeCount++;
            long minX = hotPts[lo].x, maxX = minX, minY = hotPts[lo].y, maxY = minY;
            for (int i = lo + 1; i < hi; ++i) {
                long x = hotPts[i].x, y = hotPts[i].y;
                if (x < minX) minX = x; else if (x > maxX) maxX = x;
                if (y < minY) minY = y; else if (y > maxY) maxY = y;
            }
            int len = hi - lo;
            if (len <= 16) {
                hotNodes[id] = new HotNode { MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY, Start = lo, Count = len, Left = -1, Right = -1 };
                return id;
            }
            int axis = (maxX - minX) >= (maxY - minY) ? 0 : 1;
            MergeSortPixels(hotPts, tmp, lo, hi, axis);
            int mid = lo + (len >> 1);
            int left = BuildHotNode(tmp, lo, mid);
            int right = BuildHotNode(tmp, mid, hi);
            hotNodes[id] = new HotNode { MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY, Start = lo, Count = 0, Left = left, Right = right };
            return id;
        }

        /// <summary>
        /// Returns all hot pixels in [lx, hx] x [ly, hy] that pass a conservative separating-axis corridor test around
        /// segment (ax, ay)-(bx, by) in pixel coordinates (1.5-pixel half-width + 1e-9 relative margin).
        /// </summary>
        IEnumerable<(long, long)> QueryHot(double ax, double ay, double bx, double by, long lx, long hx, long ly, long hy)
        {
            queryHits.Clear();
            if (hotNodeCount == 0) return queryHits;
            double dx = bx - ax, dy = by - ay;
            double adx = Math.Abs(dx), ady = Math.Abs(dy);
            bool useFilter = !double.IsNaN(dx) && !double.IsNaN(dy) && !double.IsInfinity(dx) && !double.IsInfinity(dy)
                && Math.Abs(ax) <= 1e13 && Math.Abs(ay) <= 1e13 && Math.Abs(bx) <= 1e13 && Math.Abs(by) <= 1e13;
            int top = 0;
            queryStack[top++] = 0;
            while (top > 0) {
                var node = hotNodes[queryStack[--top]];
                if (node.MaxX < lx || node.MinX > hx || node.MaxY < ly || node.MinY > hy) continue;
                if (useFilter) {
                    double cx = 0.5 * ((double)node.MinX + (double)node.MaxX);
                    double cy = 0.5 * ((double)node.MinY + (double)node.MaxY);
                    double halfX = 0.5 * ((double)node.MaxX - (double)node.MinX) + 1.5;
                    double halfY = 0.5 * ((double)node.MaxY - (double)node.MinY) + 1.5;
                    double rx = cx - ax, ry = cy - ay;
                    double val = dx * ry - dy * rx;
                    double rad = adx * halfY + ady * halfX;
                    double err = 1e-9 * (adx * (Math.Abs(ry) + halfY) + ady * (Math.Abs(rx) + halfX) + adx + ady + 1.0);
                    double lim = rad + err;
                    if (!double.IsNaN(val) && !double.IsInfinity(val) && !double.IsNaN(lim) && !double.IsInfinity(lim) && Math.Abs(val) > lim)
                        continue;
                }
                if (node.Count > 0) {
                    int end = node.Start + node.Count;
                    for (int i = node.Start; i < end; ++i) {
                        var p = hotPts[i];
                        if (p.x < lx || p.x > hx || p.y < ly || p.y > hy) continue;
                        if (useFilter) {
                            double rx = p.x - ax, ry = p.y - ay;
                            double val = dx * ry - dy * rx;
                            double lim = (adx + ady) * 1.5 + 1e-9 * (adx * (Math.Abs(ry) + 1.5) + ady * (Math.Abs(rx) + 1.5) + adx + ady + 1.0);
                            if (!double.IsNaN(val) && !double.IsInfinity(val) && !double.IsNaN(lim) && !double.IsInfinity(lim) && Math.Abs(val) > lim)
                                continue;
                        }
                        queryHits.Add((p.x, p.y));
                    }
                } else {
                    queryStack[top++] = node.Right;
                    queryStack[top++] = node.Left;
                }
            }
            return queryHits;
        }

        void RouteAll()
        {
            IndexHot();
            chains = new List<List<(long, long)>>(routed.Count);
            bool wFits = W <= (BigInteger)(1L << 40);
            long wL = wFits ? (long)W : 0;
            foreach (var (a, b) in routed) {
                cancel.ThrowIfCancellationRequested();
                long lx = Math.Min(KX[a], KX[b]) - 1, hx = Math.Max(KX[a], KX[b]) + 1, ly = Math.Min(KY[a], KY[b]) - 1, hy = Math.Max(KY[a], KY[b]) + 1;
                var pa = input.Points[a]; var pb = input.Points[b];
                BigInteger pxB = X[a], pyB = Y[a], qxB = X[b], qyB = Y[b];
                BigInteger dxB = qxB - pxB, dyB = qyB - pyB;
                BigInteger absDxB = BigInteger.Abs(dxB), absDyB = BigInteger.Abs(dyB);
                // fast exact 64-bit long contact test when all segment & pixel coordinates and cross products stay strictly below 2^60
                bool useLong = false;
                long pxL = 0, pyL = 0, qxL = 0, qyL = 0, dxL = 0, dyL = 0, lxL = 0, hxL = 0, lyL = 0, hyL = 0;
                if (wFits) {
                    long maxAbsK = Math.Max(Math.Max(Math.Abs(lx), Math.Abs(hx)), Math.Max(Math.Abs(ly), Math.Abs(hy))) + 2;
                    if ((double)maxAbsK * (2.0 * wL) < 1e18 && BigInteger.Abs(pxB) <= (BigInteger)1000000000000000000L
                        && BigInteger.Abs(pyB) <= (BigInteger)1000000000000000000L
                        && BigInteger.Abs(qxB) <= (BigInteger)1000000000000000000L
                        && BigInteger.Abs(qyB) <= (BigInteger)1000000000000000000L) {
                        pxL = (long)pxB; pyL = (long)pyB; qxL = (long)qxB; qyL = (long)qyB;
                        dxL = qxL - pxL; dyL = qyL - pyL;
                        lxL = pxL < qxL ? pxL : qxL; hxL = pxL > qxL ? pxL : qxL;
                        lyL = pyL < qyL ? pyL : qyL; hyL = pyL > qyL ? pyL : qyL;
                        double adx = Math.Abs((double)dxL), ady = Math.Abs((double)dyL);
                        double pad = 2.0 * wL + 4.0;
                        if (adx * (ady + pad) < 1e18 && ady * (adx + pad) < 1e18) useLong = true;
                    }
                }
                var met = new List<(((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) t, (long, long) p)>();
                foreach (var p in QueryHot(pa.x / w, pa.y / w, pb.x / w, pb.y / w, lx, hx, ly, hy)) {
                    bool hit = useLong
                        ? MeetsContactLong(pxL, pyL, qxL, qyL, dxL, dyL, lxL, hxL, lyL, hyL, wL, p)
                        : Meets(pxB, pyB, qxB, qyB, p, false);
                    if (hit) met.Add((Span(pxB, pyB, dxB, dyB, absDxB, absDyB, p), p));
                }
                met.Sort(CompareMet);
                var chain = new List<(long, long)>();
                foreach (var m in met) if (chain.Count == 0 || !chain[chain.Count - 1].Equals(m.p)) chain.Add(m.p);
                chains.Add(chain);
            }
        }

        /// <summary>
        /// the order in which the hot pixels a segment meets lie along it: by the entry parameter, then the exit parameter (exact fractions with
        /// positive denominators, compared by cross-multiplication), then the pixel itself: 0 only for the same entry, exit and pixel, so for
        /// distinct pixels a total order, and the sorted list does not depend on the order of the unstable sort (determinism proof, R11)
        /// </summary>
        internal static int CompareMet((((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) t, (long, long) p) u,
            (((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) t, (long, long) p) v)
        {
            int c = u.t.entry.den == v.t.entry.den
                ? u.t.entry.num.CompareTo(v.t.entry.num)
                : (u.t.entry.num * v.t.entry.den).CompareTo(v.t.entry.num * u.t.entry.den);
            if (c != 0) return c;
            c = u.t.exit.den == v.t.exit.den
                ? u.t.exit.num.CompareTo(v.t.exit.num)
                : (u.t.exit.num * v.t.exit.den).CompareTo(v.t.exit.num * u.t.exit.den);
            if (c != 0) return c;
            c = u.p.Item1.CompareTo(v.p.Item1);
            return c != 0 ? c : u.p.Item2.CompareTo(v.p.Item2);
        }

        static ((long, long), (long, long)) OrderedPixel((long, long) p, (long, long) q)
            => p.Item1 < q.Item1 || (p.Item1 == q.Item1 && p.Item2 <= q.Item2) ? (p, q) : (q, p);

        /// <summary> critical pixels: a hot pixel whose interior an output edge crosses, the edge's ends outside it </summary>
        List<((long, long) cp, int chain, int edge)> Critical()
        {
            var list = new List<((long, long), int, int)>();
            // cache critical pixels per unique undirected chain edge (lookup-only: never enumerated)
            var edgeCache = new Dictionary<((long, long), (long, long)), List<(long, long)>>();
            for (int c = 0; c < chains.Count; ++c) {
                var ch = chains[c];
                if ((c & 255) == 0) cancel.ThrowIfCancellationRequested();
                for (int e = 0; e + 1 < ch.Count; ++e) {
                    var p = ch[e]; var q = ch[e + 1];
                    var key = OrderedPixel(p, q);
                    if (!edgeCache.TryGetValue(key, out var hits)) {
                        hits = null;
                        long lx = Math.Min(p.Item1, q.Item1), hx = Math.Max(p.Item1, q.Item1), ly = Math.Min(p.Item2, q.Item2), hy = Math.Max(p.Item2, q.Item2);
                        foreach (var h in QueryHot(p.Item1, p.Item2, q.Item1, q.Item2, lx, hx, ly, hy)) {
                            if (!h.Equals(p) && !h.Equals(q) && MeetsPixelCentreInterior(p, q, h)) {
                                if (hits == null) hits = new List<(long, long)>();
                                hits.Add(h);
                            }
                        }
                        if (hits != null && hits.Count > 1) {
                            // deterministic total order by (x, y)
                            for (int i = 1; i < hits.Count; ++i) {
                                var cur = hits[i];
                                int j = i - 1;
                                while (j >= 0 && (hits[j].Item1 > cur.Item1 || (hits[j].Item1 == cur.Item1 && hits[j].Item2 > cur.Item2))) {
                                    hits[j + 1] = hits[j];
                                    --j;
                                }
                                hits[j + 1] = cur;
                            }
                        }
                        edgeCache.Add(key, hits);
                    }
                    if (hits != null) {
                        for (int i = 0; i < hits.Count; ++i) list.Add((hits[i], c, e));
                    }
                }
            }
            return list;
        }

        static void MergeSortEdges(EdgeBox[] arr, EdgeBox[] tmp, int lo, int hi, int axis)
        {
            int len = hi - lo;
            if (len <= 16) {
                for (int i = lo + 1; i < hi; ++i) {
                    var key = arr[i];
                    int j = i - 1;
                    while (j >= lo && EdgeGreater(arr[j], key, axis)) { arr[j + 1] = arr[j]; --j; }
                    arr[j + 1] = key;
                }
                return;
            }
            int mid = lo + (len >> 1);
            MergeSortEdges(arr, tmp, lo, mid, axis);
            MergeSortEdges(arr, tmp, mid, hi, axis);
            int p = lo, q = mid, dst = lo;
            while (p < mid && q < hi) {
                if (EdgeGreater(arr[p], arr[q], axis)) tmp[dst++] = arr[q++];
                else tmp[dst++] = arr[p++];
            }
            while (p < mid) tmp[dst++] = arr[p++];
            while (q < hi) tmp[dst++] = arr[q++];
            for (int i = lo; i < hi; ++i) arr[i] = tmp[i];
        }

        static bool EdgeGreater(EdgeBox a, EdgeBox b, int axis)
        {
            long ka = axis == 0 ? a.MinX + a.MaxX : a.MinY + a.MaxY;
            long kb = axis == 0 ? b.MinX + b.MaxX : b.MinY + b.MaxY;
            if (ka != kb) return ka > kb;
            if (a.AX != b.AX) return a.AX > b.AX;
            if (a.AY != b.AY) return a.AY > b.AY;
            if (a.BX != b.BX) return a.BX > b.BX;
            return a.BY > b.BY;
        }

        static int BuildEdgeNode(EdgeBox[] edges, EdgeBox[] tmp, EdgeNode[] nodes, ref int nodeCount, int lo, int hi)
        {
            int id = nodeCount++;
            long minX = edges[lo].MinX, maxX = edges[lo].MaxX, minY = edges[lo].MinY, maxY = edges[lo].MaxY;
            for (int i = lo + 1; i < hi; ++i) {
                if (edges[i].MinX < minX) minX = edges[i].MinX;
                if (edges[i].MaxX > maxX) maxX = edges[i].MaxX;
                if (edges[i].MinY < minY) minY = edges[i].MinY;
                if (edges[i].MaxY > maxY) maxY = edges[i].MaxY;
            }
            int len = hi - lo;
            if (len <= 16) {
                nodes[id] = new EdgeNode { MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY, Start = lo, Count = len, Left = -1, Right = -1 };
                return id;
            }
            int axis = (maxX - minX) >= (maxY - minY) ? 0 : 1;
            MergeSortEdges(edges, tmp, lo, hi, axis);
            int mid = lo + (len >> 1);
            int left = BuildEdgeNode(edges, tmp, nodes, ref nodeCount, lo, mid);
            int right = BuildEdgeNode(edges, tmp, nodes, ref nodeCount, mid, hi);
            nodes[id] = new EdgeNode { MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY, Start = lo, Count = 0, Left = left, Right = right };
            return id;
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
            // index unique undirected chain edges in a flat 2D BVH (lookup-only HashSet, never enumerated)
            var seenEdges = new HashSet<((long, long), (long, long))>();
            var edgeList = new List<EdgeBox>();
            for (int c = 0; c < chains.Count; ++c) {
                var ch = chains[c];
                for (int e = 0; e + 1 < ch.Count; ++e) {
                    var key = OrderedPixel(ch[e], ch[e + 1]);
                    if (!seenEdges.Add(key)) continue;
                    var a = key.Item1; var b = key.Item2;
                    edgeList.Add(new EdgeBox {
                        AX = a.Item1, AY = a.Item2, BX = b.Item1, BY = b.Item2,
                        MinX = a.Item1 < b.Item1 ? a.Item1 : b.Item1,
                        MaxX = a.Item1 > b.Item1 ? a.Item1 : b.Item1,
                        MinY = a.Item2 < b.Item2 ? a.Item2 : b.Item2,
                        MaxY = a.Item2 > b.Item2 ? a.Item2 : b.Item2
                    });
                }
            }
            var edgeArr = edgeList.ToArray();
            var edgeNodes = new EdgeNode[Math.Max(1, 2 * edgeArr.Length)];
            int edgeNodeCount = 0;
            if (edgeArr.Length > 0) BuildEdgeNode(edgeArr, new EdgeBox[edgeArr.Length], edgeNodes, ref edgeNodeCount, 0, edgeArr.Length);

            var freeCache = new Dictionary<(long, long), bool>();
            bool Free((long, long) p)
            {
                if (freeCache.TryGetValue(p, out bool cached)) return cached;
                bool result = true;
                if (edgeNodeCount > 0) {
                    long px = p.Item1, py = p.Item2;
                    int top = 0;
                    queryStack[top++] = 0;
                    while (top > 0) {
                        var node = edgeNodes[queryStack[--top]];
                        if (px < node.MinX || px > node.MaxX || py < node.MinY || py > node.MaxY) continue;
                        if (node.Count > 0) {
                            int end = node.Start + node.Count;
                            for (int i = node.Start; i < end; ++i) {
                                var eb = edgeArr[i];
                                if (px < eb.MinX || px > eb.MaxX || py < eb.MinY || py > eb.MaxY) continue;
                                if (MeetsPixelCentreInterior((eb.AX, eb.AY), (eb.BX, eb.BY), p)) { result = false; top = 0; break; }
                            }
                        } else {
                            queryStack[top++] = node.Right;
                            queryStack[top++] = node.Left;
                        }
                    }
                }
                freeCache.Add(p, result);
                return result;
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
                    if (MeetsPixelCentreInterior(a, p, cp) || MeetsPixelCentreInterior(p, b, cp)) continue;
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
                    if (wider.Count == 0) throw new InvalidOperationException("snap rounding: no pixel to restore critical pixel " + I(cp) + " on the chain of segment "
                        + input.Points[sa] + " - " + input.Points[sb] + " (SRR, Proposition 4.9)");
                    chosen = Nearest(wider, sa, sb); ++Widened;
                }
                added.Add(chosen);
            }
            foreach (var p in (IEnumerable<(long, long)>)added) hot.Add(p);
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
            SegmentChainStart = new int[input.Segments.Count + 1];
            for (int si = 0; si < input.Segments.Count; ++si) {
                var s = input.Segments[si];
                SegmentChainStart[si] = result.Segments.Count;
                if (s.A == s.B) { result.Segments.Add(new Mesh2Segment(rep[(KX[s.A], KY[s.A])], rep[(KX[s.A], KY[s.A])], s.Marker, s.Locked)); continue; }
                var chain = ChainOf(s.A, s.B);
                if (chain.Count < 2) { ++CollapsedSegments; continue; }
                for (int i = 0; i + 1 < chain.Count; ++i) result.Segments.Add(new Mesh2Segment(chain[i], chain[i + 1], s.Marker, s.Locked));
            }
            SegmentChainStart[input.Segments.Count] = result.Segments.Count;
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
                Diagnostics.Add("snap: ring " + I(r) + " collapsed below the tolerance" + (list.Distinct().Count() >= 2 ? "; its edges stay as segments" : ""));
                for (int i = 0; i < list.Count && list.Count >= 2; ++i)
                    if (list[i] != list[(i + 1) % list.Count]) result.Segments.Add(new Mesh2Segment(list[i], list[(i + 1) % list.Count], ring.Marker, false));
            }
            OuterRingsCollapsed = input.Rings.Any(r => !r.Hole) && !result.Rings.Any(r => !r.Hole);
            if (OuterRingsCollapsed) Diagnostics.Add("snap: every outer ring collapsed below the tolerance; the domain is empty (not the convex hull: D13 is for input without outer rings)");
            // A hole or region seed that snapping swept (constraint geometry moved across it or onto it, eg a contour that collapsed around
            // it) is dropped: left on the other side, it would remove or label the cell there, eg the whole domain (clean-room answer 6,
            // MESHING2D_SNAP_QA.md; review of #277: a hole collapsing to a line, a seed next to an edge that moved over it). Any other seed,
            // also one close to moved geometry, keeps its meaning (second review of #277).
            foreach (var seed in input.HoleSeeds) {
                if (Swept(seed)) { ++DroppedSeeds; Diagnostics.Add("snap: hole seed " + seed + " dropped: snapping moved constraints across it"); continue; }
                result.HoleSeeds.Add(seed);
            }
            foreach (var g in input.Regions) {
                if (g.Ring == null) {
                    if (Swept(g.Seed)) { ++DroppedSeeds; Diagnostics.Add("snap: region seed " + g.Seed + " with label " + I(g.Label) + " dropped: snapping moved constraints across it"); continue; }
                    result.Regions.Add(g);
                    continue;
                }
                var list = Expand(g.Ring);
                if (!Collapsed(list)) result.Regions.Add(Mesh2Region.ByRing(list.ToArray(), g.Label, g.MaxArea));
                else Diagnostics.Add("snap: region ring with label " + I(g.Label) + " collapsed below the tolerance");
            }
            if (input.Heights != null) {
                var heights = new List<double>(input.Heights);
                foreach (var p in source.Keys.OrderBy(p => p.Item2).ThenBy(p => p.Item1)) {
                    var (a, b) = routed[source[p]];
                    double ax = input.Points[a].x, ay = input.Points[a].y, dx = input.Points[b].x - ax, dy = input.Points[b].y - ay;
                    double t = MathUtil.MaxExact(0, MathUtil.MinExact(1, ((p.Item1 * w - ax) * dx + (p.Item2 * w - ay) * dy) / (dx * dx + dy * dy)));
                    heights.Add(input.Heights[a] + t * (input.Heights[b] - input.Heights[a]));
                }
                result.Heights = heights;
            }
            if (CollapsedSegments > 0) Diagnostics.Add("snap: " + I(CollapsedSegments) + " segments collapsed below the tolerance");
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

        /// <summary>
        /// Snapping swept the seed: it lies in the region between an input constraint and its snapped chain (the polygon of the input
        /// segment and the chain back), or on its boundary, so the geometry moved across it or onto it and its side is not that of the
        /// input any more; a contour that collapsed sweeps everything inside it. Exact, by the winding number in the doubled units.
        /// </summary>
        bool Swept(Vector2d seed)
        {
            // the seed in units of 2^emin, doubled like the other coordinates; den > 1 if its exponent is below emin
            ExactPoint3.Decompose(seed.x, out var mx, out int ex);
            ExactPoint3.Decompose(seed.y, out var my, out int ey);
            int lo = Math.Min(emin, Math.Min(mx.IsZero ? emin : ex, my.IsZero ? emin : ey));
            BigInteger den = BigInteger.One << (emin - lo);
            BigInteger sx = mx.IsZero ? BigInteger.Zero : 2 * (mx << (ex - lo)), sy = my.IsZero ? BigInteger.Zero : 2 * (my << (ey - lo));
            var px = new List<BigInteger>(); var py = new List<BigInteger>();
            for (int c = 0; c < chains.Count; ++c) {
                var (oa, ob) = routed[c];
                var ch = chains[c];
                px.Clear(); py.Clear();
                px.Add(X[oa] * den); py.Add(Y[oa] * den);
                px.Add(X[ob] * den); py.Add(Y[ob] * den);
                for (int k = ch.Count - 1; k >= 0; --k) { px.Add(Cx(ch[k]) * den); py.Add(Cy(ch[k]) * den); }
                // a chain on its segment sweeps nothing but the segment itself (every point is on the boundary then: not a sweep)
                bool still = true;
                for (int k = 2; k < px.Count && still; ++k) still = Orient(px[0], py[0], px[1], py[1], px[k], py[k]) == 0;
                if (still) continue;
                BigInteger x0 = px.Min(), x1 = px.Max(), y0 = py.Min(), y1 = py.Max();
                if (sx < x0 || sx > x1 || sy < y0 || sy > y1) continue;
                int winding = 0;
                bool boundary = false;
                for (int k = 0; k < px.Count && !boundary; ++k) {
                    int n = (k + 1) % px.Count;
                    int o = Orient(px[k], py[k], px[n], py[n], sx, sy);
                    if (o == 0 && sx >= BigInteger.Min(px[k], px[n]) && sx <= BigInteger.Max(px[k], px[n]) && sy >= BigInteger.Min(py[k], py[n]) && sy <= BigInteger.Max(py[k], py[n])) boundary = true;
                    else if (py[k] <= sy) { if (py[n] > sy && o > 0) ++winding; }
                    else if (py[n] <= sy && o < 0) --winding;
                }
                if (boundary || winding != 0) return true;
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

        /// <summary>
        /// the pixel index of x: floor(x / w + 1/2), exactly. A filter first, else the exact quotient (<see cref="PixelExact"/>).
        /// Refinement on the grid rounds every candidate point (profiled: the BigInteger quotient was a tenth of the split-point choice).
        ///
        /// The filter needs no error bound, only that rounding is monotone (w &gt; 0; for any other w the exact path alone, as before).
        /// Let q = x / w exactly, r = fl(fl(q) + 1/2) and f = floor(r), and suppose f &lt; r (r is no integer; then |r| &lt; 2^52, beyond
        /// which every double is one, and f + 1/2 is a double). If q + 1/2 &lt; f, then q &lt; f - 1/2, so fl(q) &lt;= fl(f - 1/2) and
        /// r &lt;= fl(fl(f - 1/2) + 1/2) = f (at f = -2^52, f - 1/2 is no double, but q &lt; f - 1/2 then forces fl(q) &lt;= f - 1, and
        /// fl(f - 1 + 1/2) = f, the tie rounding to the even f), a contradiction; if q + 1/2 &gt;= f + 1, then fl(q) &gt;= f + 1/2 and r &gt;= f + 1, again. So f &lt;= q + 1/2 &lt; f + 1: the index is
        /// f. Where r is an integer (q + 1/2 may lie just below it), infinite or NaN, the exact quotient decides
        /// </summary>
        public long Pixel(double x)
        {
            if (W > 0) {
                double r = x / W + 0.5, f = Math.Floor(r);
                if (f < r) return (long)f;
            }
            return PixelExact(x);
        }

        /// <summary> the pixel index of x: floor(x / w + 1/2), from the exact quotient </summary>
        internal long PixelExact(double x)
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

        /// <summary>
        /// does segment ab cross the open interior of pixel (kx, ky), exactly. A filter first (<see cref="CrossesInteriorFilter"/>);
        /// where it cannot decide, the exact test (<see cref="CrossesInteriorExact"/>). Refinement on the grid asks this for every
        /// split point it tries (profiled: the BigInteger test was a tenth of the mesher's time on the Herne tiles at snap 0.01)
        /// </summary>
        public bool CrossesInterior(Vector2d a, Vector2d b, long kx, long ky)
        {
            int f = CrossesInteriorFilter(a, b, kx, ky);
            return f != 0 ? f > 0 : CrossesInteriorExact(a, b, kx, ky);
        }

        /// <summary>
        /// The filter of <see cref="CrossesInterior"/>: 1 crosses, -1 does not, 0 undecided (the exact test decides). u = 2^-53.
        /// - The pixel's sides: (k -+ 1/2) is exact for |k| &lt; 2^52, and the product with w rounds once, s = fl(S) of the true side S.
        /// - The box test compares exact coordinates v (doubles) with the sides. Rounding is monotone, so v &lt; s implies v &lt; S (if
        ///   S &lt;= v, then s = fl(S) &lt;= fl(v) = v), and v &gt; s implies v &gt; S; where v = s, undecided. No error bound is needed.
        /// - A corner's orientation against ab, O = DX EY - DY EX (DX = bx - ax, EY = CY - ay for the true corner (CX, CY)), in
        ///   doubles: dx = DX (1 + d1); ey = (CY (1 + d0) - ay)(1 + d2) = EY (1 + d2) + CY d0 (1 + d2); l = dx ey (1 + d3), and r
        ///   alike; o = (l - r)(1 + d4). With |di| &lt;= u: |o - O| &lt;= (3u + O(u^2)) (|DX| |EY| + |DY| |EX|) + (u + O(u^2)) (|DX| |CY|
        ///   + |DY| |CX|) + u (|l| + |r|) &lt;= 5u (1 + 4u) (|dx| (|ey| + |cy|) + |dy| (|ex| + |cx|)), the computed values standing in
        ///   for the true ones within (1 + 2u). That 5u (1 + 4u) ≈ 5.551e-16 is a coarser majorant (bounding |o - O|); for a sign
        ///   test only, the final rounding of o drops out and tools/ErrorBoundAudit/bounds.py (SN-5) proves the sharper 3u
        ///   requirement (~3.331e-16). The bound used is 6e-16 times that sum (~5.4 u, 1.80x margin over the proved 3u requirement): an orientation
        ///   beyond it has its true sign; within it, undecided. Valid where no rounding is absolute: every input, side, product and the
        ///   bound normal and far from overflow, in [1e-140, 1e140] or zero (Ok; else undecided), and a difference of doubles that is
        ///   subnormal is exact.
        /// The decisions then equal the exact test's, step for step (the box test, then the four signs; a degenerate segment, a == b,
        /// has every orientation 0 and goes to the exact test)
        /// </summary>
        internal int CrossesInteriorFilter(Vector2d a, Vector2d b, long kx, long ky)
        {
            // (w &gt; 0 only: the exact test alone otherwise, as before; |k| &lt; 2^52 by comparisons: Math.Abs(long.MinValue) throws)
            const long big = 1L << 52;
            if (!(W > 0) || !(kx > -big && kx < big && ky > -big && ky < big)) return 0;
            double x0 = (kx - 0.5) * W, x1 = (kx + 0.5) * W, y0 = (ky - 0.5) * W, y1 = (ky + 0.5) * W;
            bool Ok(double v) => v == 0 || (Math.Abs(v) > 1e-140 && Math.Abs(v) < 1e140);
            if (!(Ok(a.x) && Ok(a.y) && Ok(b.x) && Ok(b.y) && Ok(x0) && Ok(x1) && Ok(y0) && Ok(y1) && Ok(W))) return 0;
            // the box test: outside where the segment's extent ends at or before a side (max &lt;= x0 or min &gt;= x1, per axis)
            double ax = Math.Max(a.x, b.x), ix = Math.Min(a.x, b.x), ay = Math.Max(a.y, b.y), iy = Math.Min(a.y, b.y);
            if (ax < x0 || ix > x1 || ay < y0 || iy > y1) return -1;
            if (ax == x0 || ix == x1 || ay == y0 || iy == y1) return 0;
            double dx = b.x - a.x, dy = b.y - a.y;
            int pos = 0, neg = 0;
            foreach (var (cx, cy) in new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }) {
                double ex = cx - a.x, ey = cy - a.y, l = dx * ey, r = dy * ex, o = l - r;
                double bound = 6e-16 * (Math.Abs(dx) * (Math.Abs(ey) + Math.Abs(cy)) + Math.Abs(dy) * (Math.Abs(ex) + Math.Abs(cx)));
                if (!(Ok(l) && Ok(r) && Ok(bound))) return 0;
                if (o > bound) ++pos; else if (o < -bound) ++neg;
                else return 0;
            }
            return pos > 0 && neg > 0 ? 1 : -1;
        }

        /// <summary> does segment ab cross the open interior of pixel (kx, ky), from exact integers </summary>
        internal bool CrossesInteriorExact(Vector2d a, Vector2d b, long kx, long ky)
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
