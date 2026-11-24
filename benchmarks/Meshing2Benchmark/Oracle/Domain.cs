using System;
using System.Collections.Generic;
using System.Linq;

namespace Meshing2Acceptance
{
    public enum Where { Outside, Boundary, Inside }

    /// <summary>
    /// The region to mesh, computed from the rings alone, exactly. Decision D5 (owner, 2026-11-01): a point is in the domain iff
    /// the innermost ring that contains it (each ring by the even-odd rule) is an outer ring; without outer rings the convex hull
    /// of the input points is the outermost outer ring. So an island in a lake is meshed, a lake in it removed again, and a hole
    /// in a hole stays removed (F-44). Rings that overlap without nesting: the later ring decides. Spec: Q7 as clarified, F-7
    /// (bowtie), F-44. Changing the rule means changing <see cref="Decide"/> only.
    /// </summary>
    public sealed class Domain
    {
        public readonly List<Pt[]> Outers = new List<Pt[]>(), Holes = new List<Pt[]>();
        /// <summary> all rings in input order (the hull first if there are no outer rings), with their role </summary>
        public readonly List<(Pt[] ring, bool hole)> Rings = new List<(Pt[], bool)>();
        /// <summary> Inside[a, b]: ring a lies in the closure of ring b (nested, not crossing) </summary>
        bool[,] inside;
        public readonly bool IsHull;
        /// <summary> false: no floating-point filter in the ring tests (the oracle's reference path) </summary>
        public readonly bool Filter = true;
        /// <summary> the area of the domain (D5 parity of its rings), exact; for a snapped domain computed on first use </summary>
        public Q Area => area ?? (area = ComputeArea()).Value;
        Q? area;
        public readonly Q Perimeter2Bound; // an upper bound of the summed ring lengths, for area tolerances

        // Per-Domain fast-path geometry caches (populated only when Filter == true)
        double[] ringBoxFlat;       // 4 doubles per ring: x0, x1, y0, y1 (NaN if unknown)
        double[] ringInnerFlat;     // 4 doubles per ring: min_i x_hi, max_i x_lo, min_i y_hi, max_i y_lo (NaN if unknown)
        double[][] ringVB;
        double[][] ringEB;
        EdgeBuckets[] ringBuckets;
        RingGrid ringGrid;

        public Domain(Mesh2Input input, bool filter = true)
        {
            Filter = filter;
            var pts = new Pt[input.Count];
            for (int i = 0; i < pts.Length; ++i) pts[i] = Pt.FromDoubles(input.X[i], input.Y[i]);
            foreach (var r in input.Rings) {
                var ring = OpenRing(r.Vertices.Select(i => pts[i]).ToArray());
                (r.Hole ? Holes : Outers).Add(ring);
                Rings.Add((ring, r.Hole));
            }
            if (Outers.Count == 0) {
                var hull = Exact.ConvexHull(pts).ToArray();
                Outers.Add(hull);
                Rings.Insert(0, (hull, false));
                IsHull = true;
            }
            InitRingStructures();
            area = ComputeArea();
            Perimeter2Bound = SumPerimeterL1(Outers.Concat(Holes));
        }

        /// <summary>
        /// The domain of the given rings and roles by the same rule (D5), for the snapped domain of O-H5: the images of the input's rings,
        /// and for a hull domain the snapped hull as its outer ring. Its area is computed on first use; no perimeter (not needed there).
        /// </summary>
        internal Domain(IEnumerable<(Pt[] ring, bool hole)> rings, bool filter)
        {
            Filter = filter;
            foreach (var (r, hole) in rings) {
                var ring = OpenRing(r);
                (hole ? Holes : Outers).Add(ring);
                Rings.Add((ring, hole));
            }
            InitRingStructures();
            Perimeter2Bound = Q.Zero;
        }

        static Q SumPerimeterL1(IEnumerable<Pt[]> rings)
        {
            var list = rings as IList<Pt[]> ?? rings.ToList();
            System.Numerics.BigInteger maxW = System.Numerics.BigInteger.One;
            bool dyadic = true;
            foreach (var ring in list)
                for (int i = 0; i < ring.Length; ++i) {
                    var w = ring[i].W;
                    if (!w.IsPowerOfTwo) { dyadic = false; break; }
                    if (w > maxW) maxW = w;
                }
            if (dyadic) {
                System.Numerics.BigInteger sum = System.Numerics.BigInteger.Zero;
                foreach (var ring in list)
                    for (int i = 0; i < ring.Length; ++i) {
                        var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                        var fa = maxW / a.W; var fb = maxW / b.W;
                        sum += System.Numerics.BigInteger.Abs(a.X * fa - b.X * fb) + System.Numerics.BigInteger.Abs(a.Y * fa - b.Y * fb);
                    }
                return new Q(sum, maxW);
            }
            Q length = Q.Zero;
            foreach (var ring in list)
                for (int i = 0; i < ring.Length; ++i) {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                    length += Q.Abs(a.Qx - b.Qx) + Q.Abs(a.Qy - b.Qy); // L1 length is at least the Euclidean length
                }
            return length;
        }

        void InitRingStructures()
        {
            int n = Rings.Count;
            inside = new bool[n, n];
            if (!Filter) {
                for (int a = 0; a < n; ++a)
                    for (int b = 0; b < n; ++b)
                        if (a != b) inside[a, b] = Nested(Rings[a].ring, Rings[b].ring, false);
                return;
            }
            ringBoxFlat = new double[4 * n];
            ringInnerFlat = new double[4 * n];
            ringVB = new double[n][];
            ringEB = new double[n][];
            ringBuckets = new EdgeBuckets[n];
            for (int k = 0; k < n; ++k) {
                var r = Rings[k].ring;
                var vb = VertexBounds(r);
                ringVB[k] = vb;
                ringEB[k] = EdgeBoxes(r);
                if (r.Length == 0) {
                    for (int j = 0; j < 4; ++j) ringBoxFlat[4 * k + j] = ringInnerFlat[4 * k + j] = double.NaN;
                    continue;
                }
                double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
                double ix0 = double.PositiveInfinity, ix1 = double.NegativeInfinity, iy0 = double.PositiveInfinity, iy1 = double.NegativeInfinity;
                bool known = true;
                for (int i = 0; i < r.Length; ++i) {
                    double vxl = vb[4 * i];
                    if (double.IsNaN(vxl)) { known = false; break; }
                    double vxh = vb[4 * i + 1], vyl = vb[4 * i + 2], vyh = vb[4 * i + 3];
                    if (vxl < x0) x0 = vxl;
                    if (vxh > x1) x1 = vxh;
                    if (vyl < y0) y0 = vyl;
                    if (vyh > y1) y1 = vyh;
                    if (vxh < ix0) ix0 = vxh;
                    if (vxl > ix1) ix1 = vxl;
                    if (vyh < iy0) iy0 = vyh;
                    if (vyl > iy1) iy1 = vyl;
                }
                if (known) {
                    ringBoxFlat[4 * k] = x0; ringBoxFlat[4 * k + 1] = x1; ringBoxFlat[4 * k + 2] = y0; ringBoxFlat[4 * k + 3] = y1;
                    ringInnerFlat[4 * k] = ix0; ringInnerFlat[4 * k + 1] = ix1; ringInnerFlat[4 * k + 2] = iy0; ringInnerFlat[4 * k + 3] = iy1;
                } else {
                    for (int j = 0; j < 4; ++j) ringBoxFlat[4 * k + j] = ringInnerFlat[4 * k + j] = double.NaN;
                }
            }
            if (n > 16) ringGrid = new RingGrid(ringBoxFlat, n);
            if (n <= 16) {
                for (int a = 0; a < n; ++a)
                    for (int b = 0; b < n; ++b)
                        if (a != b && RingBoxMayNest(a, b)) inside[a, b] = Nested(Rings[a].ring, Rings[b].ring, true);
                return;
            }
            // Fast path for many rings: ring a can nest in ring b only if their boxes overlap (or either box is unknown).
            var boxList = new List<(double x0, double x1, double y0, double y1)>(n);
            var boxIds = new List<int>(n);
            var unknown = new List<int>();
            for (int k = 0; k < n; ++k) {
                if (double.IsNaN(ringBoxFlat[4 * k])) unknown.Add(k);
                else { boxList.Add((ringBoxFlat[4 * k], ringBoxFlat[4 * k + 1], ringBoxFlat[4 * k + 2], ringBoxFlat[4 * k + 3])); boxIds.Add(k); }
            }
            foreach (var (ia, ib) in BoxPairs(boxList)) {
                int a = boxIds[ia], b = boxIds[ib];
                if (RingBoxMayNest(a, b)) inside[a, b] = Nested(Rings[a].ring, Rings[b].ring, true);
                if (RingBoxMayNest(b, a)) inside[b, a] = Nested(Rings[b].ring, Rings[a].ring, true);
            }
            foreach (int u in unknown)
                for (int k = 0; k < n; ++k) {
                    if (k == u) continue;
                    inside[u, k] = Nested(Rings[u].ring, Rings[k].ring, true);
                    inside[k, u] = Nested(Rings[k].ring, Rings[u].ring, true);
                }
        }

        /// <summary>False only if some vertex of ring a is provably outside ring b's bounding box.</summary>
        bool RingBoxMayNest(int a, int b)
        {
            if (Rings[a].ring.Length < 3 || Rings[b].ring.Length < 3) return false;
            if (double.IsNaN(ringInnerFlat[4 * a]) || double.IsNaN(ringBoxFlat[4 * b])) return true;
            return !(ringInnerFlat[4 * a] < ringBoxFlat[4 * b] || ringInnerFlat[4 * a + 1] > ringBoxFlat[4 * b + 1]
                  || ringInnerFlat[4 * a + 2] < ringBoxFlat[4 * b + 2] || ringInnerFlat[4 * a + 3] > ringBoxFlat[4 * b + 3]);
        }

        static Pt[] OpenRing(Pt[] ring)
        {
            int n = ring.Length;
            while (n > 1 && ring[n - 1].Equals(ring[0])) --n;
            return ring.Take(n).ToArray();
        }

        /// <summary>
        /// The one place that says how the rings combine (D5): the innermost of the rings that contain a point decides by its role.
        /// Innermost: inside every other containing ring. None (rings that overlap without nesting) or several (equal rings): the
        /// latest of the candidates, or of all containing rings, decides. No containing ring: outside.
        /// </summary>
        bool Decide(List<int> containing)
        {
            if (containing.Count == 0) return false;
            int pick = -1;
            foreach (int r in containing) {
                bool innermost = true;
                foreach (int s in containing) if (s != r && !inside[r, s]) { innermost = false; break; }
                if (innermost) pick = Math.Max(pick, r);
            }
            if (pick < 0) pick = containing.Max();
            return !Rings[pick].hole;
        }

        /// <summary>
        /// Ring a lies in the closure of ring b: no vertex of a strictly outside b, no edge midpoint of a outside b, and no edge of a
        /// properly crossing an edge of b (exact).
        /// </summary>
        static bool Nested(Pt[] a, Pt[] b, bool filter)
        {
            if (a.Length < 3 || b.Length < 3) return false;
            // (filter: a's box certainly not within b's box means a vertex of a lies outside b)
            double[] va = filter ? VertexBounds(a) : null, vb = filter ? VertexBounds(b) : null;
            if (filter) {
                double bx0 = double.PositiveInfinity, bx1 = double.NegativeInfinity, by0 = double.PositiveInfinity, by1 = double.NegativeInfinity;
                bool known = true;
                for (int j = 0; j < b.Length && known; ++j) {
                    if (double.IsNaN(vb[4 * j])) { known = false; break; }
                    bx0 = Math.Min(bx0, vb[4 * j]); bx1 = Math.Max(bx1, vb[4 * j + 1]); by0 = Math.Min(by0, vb[4 * j + 2]); by1 = Math.Max(by1, vb[4 * j + 3]);
                }
                for (int i = 0; i < a.Length && known; ++i)
                    if (!double.IsNaN(va[4 * i]) && (va[4 * i + 1] < bx0 || va[4 * i] > bx1 || va[4 * i + 3] < by0 || va[4 * i + 2] > by1)) return false;
            }
            foreach (var p in a) if (InRing(b, p, filter) == Where.Outside) return false;
            for (int i = 0; i < a.Length; ++i) {
                Pt p = a[i], q = a[(i + 1) % a.Length];
                var m = new Pt((p.Qx + q.Qx) * new Q(1, 2), (p.Qy + q.Qy) * new Q(1, 2));
                if (InRing(b, m, filter) == Where.Outside) return false;
            }
            for (int i = 0; i < a.Length; ++i) {
                int i2 = (i + 1) % a.Length;
                for (int j = 0; j < b.Length; ++j) {
                    int j2 = (j + 1) % b.Length;
                    // (filter: edges whose boxes are certainly apart do not cross)
                    if (filter && !double.IsNaN(va[4 * i]) && !double.IsNaN(va[4 * i2]) && !double.IsNaN(vb[4 * j]) && !double.IsNaN(vb[4 * j2])
                        && (Math.Max(va[4 * i + 1], va[4 * i2 + 1]) < Math.Min(vb[4 * j], vb[4 * j2]) || Math.Min(va[4 * i], va[4 * i2]) > Math.Max(vb[4 * j + 1], vb[4 * j2 + 1])
                         || Math.Max(va[4 * i + 3], va[4 * i2 + 3]) < Math.Min(vb[4 * j + 2], vb[4 * j2 + 2]) || Math.Min(va[4 * i + 2], va[4 * i2 + 2]) > Math.Max(vb[4 * j + 3], vb[4 * j2 + 3])))
                        continue;
                    if (Exact.ProperCross(a[i], a[i2], b[j], b[j2])) return false;
                }
            }
            return true;
        }

        /// <summary>Even-odd position against one ring, by exact crossing counts.</summary>
        /// <summary> per ring: each vertex as an interval of doubles around its exact coordinates (x lo, x hi, y lo, y hi; NaN: unknown) </summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pt[], double[]> vertexBounds = new System.Runtime.CompilerServices.ConditionalWeakTable<Pt[], double[]>();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pt[], double[]> edgeBoxes = new System.Runtime.CompilerServices.ConditionalWeakTable<Pt[], double[]>();

        /// <summary> per edge i (from vertex i to the next): its box in doubles, the union of its two ends' boxes, as four values
        /// (x low, x high, y low, y high); NaN where an end has no bounds </summary>
        static double[] EdgeBoxes(Pt[] ring) => edgeBoxes.GetValue(ring, r => {
            var vb = VertexBounds(r);
            var b = new double[4 * r.Length];
            for (int i = 0; i < r.Length; ++i) {
                int j = (i + 1) % r.Length;
                if (double.IsNaN(vb[4 * i]) || double.IsNaN(vb[4 * j])) { for (int k = 0; k < 4; ++k) b[4 * i + k] = double.NaN; continue; }
                b[4 * i] = Math.Min(vb[4 * i], vb[4 * j]); b[4 * i + 1] = Math.Max(vb[4 * i + 1], vb[4 * j + 1]);
                b[4 * i + 2] = Math.Min(vb[4 * i + 2], vb[4 * j + 2]); b[4 * i + 3] = Math.Max(vb[4 * i + 3], vb[4 * j + 3]);
            }
            return b;
        });

        static double[] VertexBounds(Pt[] ring) => vertexBounds.GetValue(ring, r => {
            var b = new double[4 * r.Length];
            for (int i = 0; i < r.Length; ++i)
                if (!r[i].Bounds(false, out b[4 * i], out b[4 * i + 1]) || !r[i].Bounds(true, out b[4 * i + 2], out b[4 * i + 3]))
                    for (int j = 0; j < 4; ++j) b[4 * i + j] = double.NaN;
            return b;
        });

        /// <summary>
        /// Per ring: its edges in buckets of y (an edge in every bucket its y range meets), for the filtered test: a point whose y
        /// interval lies in one bucket needs only that bucket's edges, the others' y ranges exclude it. Edges without bounds are in
        /// every bucket.
        /// </summary>
        sealed class BucketSlice
        {
            public double StripLo, StripHi;
            public int[] SpanEdge;      // non-negative: bAbove == true (a below, b above); bitwise ~i: bAbove == false
            public double[] SpanExl;    // ascending
            public double[] SpanMaxExh; // prefix max of exh
            public int[] Ending;
        }

        sealed class EdgeBuckets
        {
            public double Y0, Y1, Step;
            public int[][] Buckets;
            public BucketSlice[] Slices;

            public BucketSlice GetSlice(int b, int n, double[] vb, double[] eb)
            {
                var sl = System.Threading.Volatile.Read(ref Slices[b]);
                if (sl != null) return sl;
                var all = Buckets[b];
                double sLo = Y0 + b * Step;
                double sHi = b + 1 == Buckets.Length ? Y1 : Y0 + (b + 1) * Step;
                var span = new List<(double exl, double exh, int code)>(all.Length);
                var end = new List<int>(16);
                for (int q = 0; q < all.Length; ++q) {
                    int i = all[q];
                    double exl = eb[4 * i];
                    if (double.IsNaN(exl)) { end.Add(i); continue; }
                    int j = (i + 1) % n;
                    if (vb[4 * i + 3] < sLo && vb[4 * j + 2] > sHi) span.Add((exl, eb[4 * i + 1], i));
                    else if (vb[4 * j + 3] < sLo && vb[4 * i + 2] > sHi) span.Add((exl, eb[4 * i + 1], ~i));
                    else end.Add(i);
                }
                span.Sort((a, c) => a.exl.CompareTo(c.exl));
                int sc = span.Count;
                var spanEdge = new int[sc];
                var spanExl = new double[sc];
                var spanMaxExh = new double[sc];
                double maxExh = double.NegativeInfinity;
                for (int k = 0; k < sc; ++k) {
                    spanEdge[k] = span[k].code;
                    spanExl[k] = span[k].exl;
                    if (span[k].exh > maxExh) maxExh = span[k].exh;
                    spanMaxExh[k] = maxExh;
                }
                sl = new BucketSlice { StripLo = sLo, StripHi = sHi, SpanEdge = spanEdge, SpanExl = spanExl, SpanMaxExh = spanMaxExh, Ending = end.ToArray() };
                System.Threading.Volatile.Write(ref Slices[b], sl);
                return sl;
            }
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pt[], EdgeBuckets> edgeBuckets = new System.Runtime.CompilerServices.ConditionalWeakTable<Pt[], EdgeBuckets>();

        static EdgeBuckets Buckets(Pt[] ring) => edgeBuckets.GetValue(ring, r => {
            var vb = VertexBounds(r);
            int n = r.Length, count = Math.Max(1, n / 4);
            double y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
            for (int i = 0; i < n; ++i) if (!double.IsNaN(vb[4 * i])) { y0 = Math.Min(y0, vb[4 * i + 2]); y1 = Math.Max(y1, vb[4 * i + 3]); }
            double step = (y1 - y0) / count;
            if (!(step > 0) || double.IsInfinity(step)) return null;
            var lists = new List<int>[count];
            for (int b = 0; b < count; ++b) lists[b] = new List<int>();
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                int b0 = 0, b1 = count - 1;
                if (!double.IsNaN(vb[4 * i]) && !double.IsNaN(vb[4 * j])) {
                    b0 = Math.Max(0, Math.Min(count - 1, (int)Math.Floor((Math.Min(vb[4 * i + 2], vb[4 * j + 2]) - y0) / step) - 1));
                    b1 = Math.Max(0, Math.Min(count - 1, (int)Math.Floor((Math.Max(vb[4 * i + 3], vb[4 * j + 3]) - y0) / step) + 1));
                }
                for (int b = b0; b <= b1; ++b) lists[b].Add(i);
            }
            return new EdgeBuckets { Y0 = y0, Y1 = y1, Step = step, Buckets = lists.Select(l => l.ToArray()).ToArray(), Slices = new BucketSlice[count] };
        });

        public static Where InRing(Pt[] ring, Pt p) => InRing(ring, p, true);

        /// <summary>
        /// Even-odd position against one ring, by exact crossing counts, with a floating-point filter in front (useFilter): each
        /// edge's box in doubles around the exact one, and p's. An edge whose y range excludes p's y neither contains p nor crosses its
        /// ray; one entirely left of p neither; one entirely right of p that runs from one side of p's y to the other crosses the
        /// ray (to the right of p) and does not contain p. Only the edges whose box p's box meets, or whose ends cannot be told from
        /// p's y in doubles, go to the exact tests.
        /// </summary>
        public static Where InRing(Pt[] ring, Pt p, bool useFilter)
        {
            double pxLo = 0, pxHi = 0, pyLo = 0, pyHi = 0;
            bool filter = useFilter && p.Bounds(false, out pxLo, out pxHi) && p.Bounds(true, out pyLo, out pyHi);
            return InRing(ring, p, filter, pxLo, pxHi, pyLo, pyHi);
        }

        /// <summary> InRing with p's interval given (filter false: no filter) </summary>
        static Where InRing(Pt[] ring, Pt p, bool filter, double pxLo, double pxHi, double pyLo, double pyHi)
        {
            if (ring.Length == 0) return Where.Outside;
            var vb = filter ? VertexBounds(ring) : null;
            var eb = filter ? EdgeBoxes(ring) : null;
            var bk = filter && ring.Length > 16 ? Buckets(ring) : null;
            Pt? exact = p;
            return InRingCore(ring, vb, eb, bk, ref exact, null, filter, pxLo, pxHi, pyLo, pyHi);
        }

        static Where InRingCore(Pt[] ring, double[] vb, double[] eb, EdgeBuckets bk, ref Pt? pCache, Func<Pt> getPt, bool filter, double pxLo, double pxHi, double pyLo, double pyHi)
        {
            int n = ring.Length;
            if (n == 0) return Where.Outside;
            bool inside = false;
            Q py = default; bool pyKnown = false;   // (p.Qy, on first use: a gcd)
            int[] only = null;
            if (filter && bk != null) {
                if (pyHi < bk.Y0 || pyLo > bk.Y1) return Where.Outside;
                int last = bk.Buckets.Length - 1;
                int b0 = Math.Max(0, Math.Min(last, (int)Math.Floor((pyLo - bk.Y0) / bk.Step)));
                int b1 = Math.Max(0, Math.Min(last, (int)Math.Floor((pyHi - bk.Y0) / bk.Step)));
                if (b0 == b1) {
                    var allB = bk.Buckets[b0];
                    if (allB.Length > 24) {
                        var sl = bk.GetSlice(b0, n, vb, eb);
                        if (pyLo >= sl.StripLo && pyHi <= sl.StripHi) {
                            // 1. Spanning edges: binary search first index with exl > pxHi; all at >= hi are strictly right and cross ray.
                            var spanExl = sl.SpanExl;
                            int lo = 0, hi = spanExl.Length;
                            while (lo < hi) {
                                int mid = lo + ((hi - lo) >> 1);
                                if (spanExl[mid] > pxHi) hi = mid; else lo = mid + 1;
                            }
                            if (((spanExl.Length - hi) & 1) != 0) inside = !inside;
                            var spanMaxExh = sl.SpanMaxExh;
                            var spanEdge = sl.SpanEdge;
                            for (int k = hi - 1; k >= 0; --k) {
                                if (spanMaxExh[k] < pxLo) break;
                                int code = spanEdge[k];
                                bool bAbove = code >= 0;
                                int i = bAbove ? code : ~code;
                                if (eb[4 * i + 1] < pxLo) continue;
                                int j = i + 1 < n ? i + 1 : 0;
                                int sure = SureOrient(vb, i, j, pxLo, pxHi, pyLo, pyHi);
                                if (sure == 0) {
                                    if (!pCache.HasValue) pCache = getPt();
                                    if (Exact.OnClosedSegment(ring[i], ring[j], pCache.Value)) return Where.Boundary;
                                    sure = Exact.Orient(ring[i], ring[j], pCache.Value);
                                }
                                if (bAbove ? sure > 0 : sure < 0) inside = !inside;
                            }
                            // 2. Remaining (ending) edges in this bucket.
                            only = sl.Ending;
                        } else only = allB;
                    } else only = allB;
                }
            }
            int count = only?.Length ?? n;
            for (int q = 0; q < count; ++q) {
                int i = only != null ? only[q] : q;
                int j = i + 1 < n ? i + 1 : 0;
                if (filter && !double.IsNaN(eb[4 * i])) {
                    double eyl = eb[4 * i + 2], eyh = eb[4 * i + 3];
                    if (pyHi < eyl || pyLo > eyh) continue;
                    double exl = eb[4 * i], exh = eb[4 * i + 1];
                    if (exh < pxLo) continue;
                    if (exl > pxHi) {
                        int sa = vb[4 * i + 2] > pyHi ? 1 : vb[4 * i + 3] < pyLo ? -1 : 0;
                        int sb = vb[4 * j + 2] > pyHi ? 1 : vb[4 * j + 3] < pyLo ? -1 : 0;
                        if (sa != 0 && sb != 0) {
                            if ((sa > 0) != (sb > 0)) inside = !inside;
                            continue;
                        }
                        if (!pyKnown) { if (!pCache.HasValue) pCache = getPt(); py = pCache.Value.Qy; pyKnown = true; }
                        bool aUp = sa != 0 ? sa > 0 : ring[i].Qy > py, bUp = sb != 0 ? sb > 0 : ring[j].Qy > py;
                        if (aUp != bUp) inside = !inside;
                        continue;
                    }
                }
                int sure = filter && !double.IsNaN(vb[4 * i]) && !double.IsNaN(vb[4 * j]) ? SureOrient(vb, i, j, pxLo, pxHi, pyLo, pyHi) : 0;
                if (sure == 0) {
                    if (!pCache.HasValue) pCache = getPt();
                    if (Exact.OnClosedSegment(ring[i], ring[j], pCache.Value)) return Where.Boundary;
                }
                bool aAbove, bAbove;
                {
                    int sa = filter && vb[4 * i + 2] > pyHi ? 1 : filter && vb[4 * i + 3] < pyLo ? -1 : 0;
                    int sb = filter && vb[4 * j + 2] > pyHi ? 1 : filter && vb[4 * j + 3] < pyLo ? -1 : 0;
                    if (sa != 0) aAbove = sa > 0; else { if (!pyKnown) { if (!pCache.HasValue) pCache = getPt(); py = pCache.Value.Qy; pyKnown = true; } aAbove = ring[i].Qy > py; }
                    if (sb != 0) bAbove = sb > 0; else { if (!pyKnown) { if (!pCache.HasValue) pCache = getPt(); py = pCache.Value.Qy; pyKnown = true; } bAbove = ring[j].Qy > py; }
                }
                if (aAbove == bAbove) continue;
                int side;
                if (sure != 0) side = sure;
                else {
                    if (!pCache.HasValue) pCache = getPt();
                    side = Exact.Orient(ring[i], ring[j], pCache.Value);
                }
                if (bAbove ? side > 0 : side < 0) inside = !inside;
            }
            return inside ? Where.Inside : Where.Outside;
        }

        /// <summary>
        /// The sign of Orient(a, b, p) for the ring's vertices i and j and the point whose box is given, when doubles decide it (0: unsure).
        /// The boxes are the exact coordinates rounded to within 1e-13 of themselves, so the middles are off by at most that; the bound is
        /// the one of the oracle's SurelyOneSide: delta = max(1e-12 * big, box half-width) times the factors of both products, plus 1e-12
        /// of the products' sizes.
        /// </summary>
        static int SureOrient(double[] vb, int i, int j, double pxl, double pxh, double pyl, double pyh)
        {
            double ax = (vb[4 * i] + vb[4 * i + 1]) / 2, ay = (vb[4 * i + 2] + vb[4 * i + 3]) / 2, bx = (vb[4 * j] + vb[4 * j + 1]) / 2, by = (vb[4 * j + 2] + vb[4 * j + 3]) / 2;
            double px = (pxl + pxh) / 2, py = (pyl + pyh) / 2;
            double ux = bx - ax, uy = by - ay, vx = px - ax, vy = py - ay;
            double l = ux * vy, r = uy * vx, det = l - r;
            double big = Math.Max(Math.Max(Math.Max(Math.Abs(ax), Math.Abs(ay)), Math.Max(Math.Abs(bx), Math.Abs(by))), Math.Max(Math.Abs(px), Math.Abs(py)));
            double pHalf = Math.Max(pxh - pxl, pyh - pyl);
            double delta = Math.Max(big * 1e-12, pHalf);
            double err = delta * (Math.Abs(ux) + Math.Abs(uy) + Math.Abs(vx) + Math.Abs(vy) + delta) + (Math.Abs(l) + Math.Abs(r)) * 1e-12 + double.Epsilon;
            if (double.IsNaN(det) || double.IsInfinity(err) || big > 1e140 || (big < 1e-140 && big != 0)) return 0;
            return det > err ? 1 : det < -err ? -1 : 0;
        }

        EdgeBuckets GetRingBuckets(int k)
        {
            var r = Rings[k].ring;
            if (r.Length <= 16) return null;
            var bk = ringBuckets[k];
            if (bk == null) ringBuckets[k] = bk = Buckets(r);
            return bk;
        }

        RingCellCache[] ringCellCaches;

        /// <summary>
        /// 2D cell grid over a large ring's bounding box: marks every cell that any ring edge may meet as Mixed (1)
        /// using conservative box overlap + 4-corner <see cref="SureOrient"/> separation. Any unmarked cell is provably
        /// disjoint from the ring's boundary, so every point inside it shares the exact <see cref="Where"/> of its center
        /// (classified lazily once and cached).
        /// </summary>
        sealed class RingCellCache
        {
            readonly int gridN;
            readonly double x0, y0, cellX, cellY;
            readonly byte[] state; // 0 = empty-uncomputed, 1 = mixed (boundary may enter), 2 = Inside, 3 = Outside
            public readonly bool Valid;

            public RingCellCache(Pt[] ring, double[] vb, double[] eb, double rx0, double rx1, double ry0, double ry1)
            {
                if (double.IsNaN(rx0) || !(rx1 > rx0) || !(ry1 > ry0)) { Valid = false; return; }
                double w = rx1 - rx0, h = ry1 - ry0;
                if (double.IsInfinity(w) || double.IsInfinity(h)) { Valid = false; return; }
                gridN = 128;
                // Slightly pad outer bounds so interior points near the boundary still map cleanly.
                x0 = rx0; y0 = ry0;
                cellX = w / gridN; cellY = h / gridN;
                if (!(cellX > 0) || !(cellY > 0)) { Valid = false; return; }
                state = new byte[gridN * gridN];
                int n = ring.Length;
                for (int i = 0; i < n; ++i) {
                    double ex0 = eb[4 * i];
                    if (double.IsNaN(ex0)) { Valid = false; return; }
                    double ex1 = eb[4 * i + 1], ey0 = eb[4 * i + 2], ey1 = eb[4 * i + 3];
                    int i0 = Math.Max(0, Math.Min(gridN - 1, (int)Math.Floor((ex0 - x0) / cellX) - 1));
                    int i1 = Math.Max(0, Math.Min(gridN - 1, (int)Math.Floor((ex1 - x0) / cellX) + 1));
                    int j0 = Math.Max(0, Math.Min(gridN - 1, (int)Math.Floor((ey0 - y0) / cellY) - 1));
                    int j1 = Math.Max(0, Math.Min(gridN - 1, (int)Math.Floor((ey1 - y0) / cellY) + 1));
                    int j = (i + 1) % n;
                    for (int ci = i0; ci <= i1; ++ci) {
                        double cx0 = x0 + ci * cellX;
                        double cx1 = ci + 1 == gridN ? rx1 : x0 + (ci + 1) * cellX;
                        if (ex1 < cx0 || ex0 > cx1) continue;
                        for (int cj = j0; cj <= j1; ++cj) {
                            int idx = ci * gridN + cj;
                            if (state[idx] == 1) continue;
                            double cy0 = y0 + cj * cellY;
                            double cy1 = cj + 1 == gridN ? ry1 : y0 + (cj + 1) * cellY;
                            if (ey1 < cy0 || ey0 > cy1) continue;
                            // Cell [cx0, cx1] x [cy0, cy1] is disjoint from edge (i, j) if all 4 corners lie strictly on the same side.
                            int s0 = SureOrient(vb, i, j, cx0, cx0, cy0, cy0);
                            if (s0 != 0
                                && SureOrient(vb, i, j, cx1, cx1, cy0, cy0) == s0
                                && SureOrient(vb, i, j, cx1, cx1, cy1, cy1) == s0
                                && SureOrient(vb, i, j, cx0, cx0, cy1, cy1) == s0)
                                continue;
                            state[idx] = 1;
                        }
                    }
                }
                Valid = true;
            }

            public bool TryClassify(double pxl, double pxh, double pyl, double pyh, Pt[] ring, double[] vb, double[] eb, EdgeBuckets bk, out Where result)
            {
                result = Where.Outside;
                if (!Valid) return false;
                int ci = (int)Math.Floor((pxl - x0) / cellX);
                int cj = (int)Math.Floor((pyl - y0) / cellY);
                if (ci < 0 || ci >= gridN || cj < 0 || cj >= gridN) return false;
                int idx = ci * gridN + cj;
                byte st = state[idx];
                if (st == 1) return false;
                double cx0 = x0 + ci * cellX, cx1 = x0 + (ci + 1) * cellX;
                double cy0 = y0 + cj * cellY, cy1 = y0 + (cj + 1) * cellY;
                // Require the point's full enclosing box to lie strictly inside the empty cell.
                if (!(pxl > cx0 && pxh < cx1 && pyl > cy0 && pyh < cy1)) return false;
                if (st == 0) {
                    double mx = (cx0 + cx1) * 0.5, my = (cy0 + cy1) * 0.5;
                    Pt? center = Pt.FromDoubles(mx, my);
                    var w = InRingCore(ring, vb, eb, bk, ref center, null, true, mx, mx, my, my);
                    if (w == Where.Boundary) { state[idx] = 1; return false; }
                    st = w == Where.Inside ? (byte)2 : (byte)3;
                    state[idx] = st;
                }
                result = st == 2 ? Where.Inside : Where.Outside;
                return true;
            }
        }

        Where InRingByIndex(int k, ref Pt? pCache, Func<Pt> getPt, bool known, double pxl, double pxh, double pyl, double pyh)
        {
            var ring = Rings[k].ring;
            if (!Filter || !known) {
                if (!pCache.HasValue) pCache = getPt();
                return InRing(ring, pCache.Value, false);
            }
            double bx0 = ringBoxFlat[4 * k];
            if (!double.IsNaN(bx0) && (pxh < bx0 || pxl > ringBoxFlat[4 * k + 1] || pyh < ringBoxFlat[4 * k + 2] || pyl > ringBoxFlat[4 * k + 3]))
                return Where.Outside;
            var bk = GetRingBuckets(k);
            if (ring.Length > 64 && !double.IsNaN(bx0)) {
                if (ringCellCaches == null) ringCellCaches = new RingCellCache[Rings.Count];
                var cc = ringCellCaches[k];
                if (cc == null) ringCellCaches[k] = cc = new RingCellCache(ring, ringVB[k], ringEB[k], bx0, ringBoxFlat[4 * k + 1], ringBoxFlat[4 * k + 2], ringBoxFlat[4 * k + 3]);
                if (cc.TryClassify(pxl, pxh, pyl, pyh, ring, ringVB[k], ringEB[k], bk, out Where cached))
                    return cached;
            }
            return InRingCore(ring, ringVB[k], ringEB[k], bk, ref pCache, getPt, true, pxl, pxh, pyl, pyh);
        }

        public Where Classify(Pt p)
        {
            double pxl = 0, pxh = 0, pyl = 0, pyh = 0;
            bool known = Filter && p.Bounds(false, out pxl, out pxh) && p.Bounds(true, out pyl, out pyh);
            Pt? pCache = p;
            return ClassifyCore(ref pCache, null, known, pxl, pxh, pyl, pyh);
        }

        /// <summary>
        /// Fast-path Classify when an enclosing double box [pxl, pxh] x [pyl, pyh] of the exact point is known:
        /// avoids constructing the exact <see cref="Pt"/> unless a ring edge's double filter is undecided.
        /// Falls back to <paramref name="getPt"/> whenever Filter is false or bounds are not finite.
        /// </summary>
        internal Where ClassifyLazy(double pxl, double pxh, double pyl, double pyh, Func<Pt> getPt)
        {
            if (!Filter || double.IsNaN(pxl) || double.IsNaN(pxh) || double.IsNaN(pyl) || double.IsNaN(pyh)
                || double.IsInfinity(pxl) || double.IsInfinity(pxh) || double.IsInfinity(pyl) || double.IsInfinity(pyh))
                return Classify(getPt());
            Pt? pCache = null;
            return ClassifyCore(ref pCache, getPt, true, pxl, pxh, pyl, pyh);
        }

        Where ClassifyCore(ref Pt? pCache, Func<Pt> getPt, bool known, double pxl, double pxh, double pyl, double pyh)
        {
            int n = Rings.Count;
            if (n == 0) return Where.Outside;
            if (n == 1) {
                var w = InRingByIndex(0, ref pCache, getPt, known, pxl, pxh, pyl, pyh);
                if (Rings[0].hole) return Where.Outside;
                return w;
            }
            var strictly = new List<int>();
            var on = new List<int>();
            if (known && ringGrid != null) {
                var candidates = ringGrid.Query(pxl, pxh, pyl, pyh);
                for (int c = 0; c < candidates.Count; ++c) {
                    int k = candidates[c];
                    var w = InRingByIndex(k, ref pCache, getPt, true, pxl, pxh, pyl, pyh);
                    if (w == Where.Inside) strictly.Add(k); else if (w == Where.Boundary) on.Add(k);
                }
            } else {
                for (int k = 0; k < n; ++k) {
                    var w = InRingByIndex(k, ref pCache, getPt, known, pxl, pxh, pyl, pyh);
                    if (w == Where.Inside) strictly.Add(k); else if (w == Where.Boundary) on.Add(k);
                }
            }
            bool without = Decide(strictly);
            if (on.Count == 0) return without ? Where.Inside : Where.Outside;
            strictly.AddRange(on);
            bool with = Decide(strictly);
            return with && without ? Where.Inside : !with && !without ? Where.Outside : Where.Boundary;
        }

        /// <summary>Uniform spatial grid over ring bounding boxes (fast path only). Returns a superset of all rings whose box meets the query box.</summary>
        sealed class RingGrid
        {
            readonly double[] box;
            readonly double x0, y0, cell;
            readonly int nx, ny;
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            readonly List<int> overflow = new List<int>();
            readonly List<int> unknown = new List<int>();
            public RingGrid(double[] box, int count)
            {
                this.box = box;
                double xl = double.PositiveInfinity, xh = double.NegativeInfinity, yl = double.PositiveInfinity, yh = double.NegativeInfinity;
                for (int i = 0; i < count; ++i) {
                    if (double.IsNaN(box[4 * i])) continue;
                    if (box[4 * i] < xl) xl = box[4 * i];
                    if (box[4 * i + 1] > xh) xh = box[4 * i + 1];
                    if (box[4 * i + 2] < yl) yl = box[4 * i + 2];
                    if (box[4 * i + 3] > yh) yh = box[4 * i + 3];
                }
                x0 = xl; y0 = yl;
                double extent = Math.Max(xh - xl, yh - yl);
                cell = extent > 0 && !double.IsInfinity(extent) ? extent / Math.Max(1, Math.Sqrt(count)) : 1;
                nx = (int)Math.Min(1 << 14, Math.Max(1, Math.Ceiling((xh - xl) / cell) + 1));
                ny = (int)Math.Min(1 << 14, Math.Max(1, Math.Ceiling((yh - yl) / cell) + 1));
                for (int i = 0; i < count; ++i) {
                    if (double.IsNaN(box[4 * i])) { unknown.Add(i); continue; }
                    Cells(box[4 * i], box[4 * i + 1], box[4 * i + 2], box[4 * i + 3], out int i0, out int i1, out int j0, out int j1);
                    if ((long)(i1 - i0 + 1) * (j1 - j0 + 1) > 256) { overflow.Add(i); continue; }
                    for (int ci = i0; ci <= i1; ++ci)
                        for (int cj = j0; cj <= j1; ++cj) {
                            long key = (long)ci * ny + cj;
                            if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<int>());
                            list.Add(i);
                        }
                }
            }
            void Cells(double xl, double xh, double yl, double yh, out int i0, out int i1, out int j0, out int j1)
            {
                int Clamp(double v, int n) => double.IsNaN(v) ? 0 : (int)Math.Max(0, Math.Min(n - 1, Math.Floor(v)));
                i0 = Clamp((xl - x0) / cell, nx); i1 = Clamp((xh - x0) / cell, nx);
                j0 = Clamp((yl - y0) / cell, ny); j1 = Clamp((yh - y0) / cell, ny);
            }
            public List<int> Query(double xl, double xh, double yl, double yh)
            {
                var result = new List<int>(unknown.Count + 8);
                for (int u = 0; u < unknown.Count; ++u) result.Add(unknown[u]);
                for (int u = 0; u < overflow.Count; ++u) {
                    int i = overflow[u];
                    if (!(box[4 * i + 1] < xl || box[4 * i] > xh || box[4 * i + 3] < yl || box[4 * i + 2] > yh)) result.Add(i);
                }
                Cells(xl, xh, yl, yh, out int i0, out int i1, out int j0, out int j1);
                if (i0 == i1 && j0 == j1) {
                    if (cells.TryGetValue((long)i0 * ny + j0, out var list)) {
                        for (int k = 0; k < list.Count; ++k) {
                            int i = list[k];
                            if (!(box[4 * i + 1] < xl || box[4 * i] > xh || box[4 * i + 3] < yl || box[4 * i + 2] > yh)) result.Add(i);
                        }
                    }
                    return result;
                }
                HashSet<int> seen = result.Count > 0 ? new HashSet<int>(result) : new HashSet<int>();
                for (int ci = i0; ci <= i1; ++ci)
                    for (int cj = j0; cj <= j1; ++cj)
                        if (cells.TryGetValue((long)ci * ny + cj, out var list))
                            for (int k = 0; k < list.Count; ++k) {
                                int i = list[k];
                                if (!(box[4 * i + 1] < xl || box[4 * i] > xh || box[4 * i + 3] < yl || box[4 * i + 2] > yh) && seen.Add(i))
                                    result.Add(i);
                            }
                return result;
            }
        }

        /// <summary> one connected piece of the domain for the vanishing rule: the rings nested in one top-level ring, as a domain of its own </summary>
        public sealed class Component
        {
            public Domain Region;
            /// <summary> an upper bound of the summed ring lengths (L1) </summary>
            public Q Perimeter2Bound;
        }

        List<Component> components;

        /// <summary>
        /// The parts of the domain that do not touch: the top-level rings (inside no other ring) with the rings nested in each. They are
        /// separate only if no two top-level rings meet (exact); otherwise, or when a ring lies in two top-level rings, the whole domain
        /// is the one part (never a finer split than is certain). A hull domain is its hull alone.
        /// </summary>
        public IReadOnlyList<Component> Components()
        {
            if (components != null) return components;
            int n = Rings.Count;
            var top = new List<int>();
            for (int k = 0; k < n; ++k) {
                bool covered = false;
                for (int j = 0; j < n && !covered; ++j) if (j != k && inside[k, j] && !inside[j, k]) covered = true;
                if (!covered) top.Add(k);
            }
            // equal rings (nested both ways) are one top-level ring
            top = top.Where(k => !top.Any(j => j < k && inside[k, j] && inside[j, k])).ToList();
            var groups = new List<List<int>>();
            bool whole = top.Count <= 1;
            if (!whole) {
                foreach (int t in top) groups.Add(new List<int> { t });
                for (int k = 0; k < n && !whole; ++k) {
                    if (top.Contains(k)) continue;
                    var homes = top.Where(t => inside[k, t]).ToList();
                    if (homes.Count != 1) whole = true; else groups[top.IndexOf(homes[0])].Add(k);
                }
                for (int a = 0; a < top.Count && !whole; ++a)
                    for (int b = a + 1; b < top.Count && !whole; ++b)
                        if (RingsMeet(Rings[top[a]].ring, Rings[top[b]].ring)) whole = true;
            }
            if (whole) return components = new List<Component> { new Component { Region = this, Perimeter2Bound = SumPerimeterL1(Rings.Select(x => x.ring)) } };
            components = new List<Component>();
            foreach (var g in groups)
                {
                // (input order within the part: Decide picks the latest ring when none is innermost)
                var ordered = g.OrderBy(k => k).ToList();
                components.Add(new Component { Region = new Domain(ordered.Select(k => Rings[k]).ToList(), Filter), Perimeter2Bound = SumPerimeterL1(ordered.Select(k => Rings[k].ring)) });
            }
            return components;
        }

        /// <summary> two rings share a point (exact): some edge of one meets some edge of the other </summary>
        static bool RingsMeet(Pt[] r, Pt[] s)
        {
            (Q x0, Q x1, Q y0, Q y1) Box(Pt[] ring) => (ring.Min(p => p.Qx), ring.Max(p => p.Qx), ring.Min(p => p.Qy), ring.Max(p => p.Qy));
            var br = Box(r); var bs = Box(s);
            if (br.x1 < bs.x0 || bs.x1 < br.x0 || br.y1 < bs.y0 || bs.y1 < br.y0) return false;
            for (int i = 0; i < r.Length; ++i) {
                Pt a = r[i], b = r[(i + 1) % r.Length];
                Q ax0 = Q.Min(a.Qx, b.Qx), ax1 = Q.Max(a.Qx, b.Qx), ay0 = Q.Min(a.Qy, b.Qy), ay1 = Q.Max(a.Qy, b.Qy);
                if (ax1 < bs.x0 || ax0 > bs.x1 || ay1 < bs.y0 || ay0 > bs.y1) continue;
                for (int j = 0; j < s.Length; ++j) {
                    Pt c = s[j], d = s[(j + 1) % s.Length];
                    if (Q.Max(c.Qx, d.Qx) < ax0 || Q.Min(c.Qx, d.Qx) > ax1 || Q.Max(c.Qy, d.Qy) < ay0 || Q.Min(c.Qy, d.Qy) > ay1) continue;
                    if (Exact.ProperCross(a, b, c, d) || Exact.OnClosedSegment(a, b, c) || Exact.OnClosedSegment(a, b, d)
                        || Exact.OnClosedSegment(c, d, a) || Exact.OnClosedSegment(c, d, b)) return true;
                }
            }
            return false;
        }

        /// <summary>All ring edges of the domain.</summary>
        public IEnumerable<(Pt a, Pt b)> Edges()
        {
            foreach (var ring in Outers.Concat(Holes))
                for (int i = 0; i < ring.Length; ++i) {
                    Pt a = ring[i], b = ring[(i + 1) % ring.Length];
                    if (!a.Equals(b)) yield return (a, b);
                }
        }

        /// <summary>
        /// Exact area by slab decomposition: vertical lines through all ring vertices and pairwise edge crossings cut the
        /// plane into slabs in which the spanning edges do not cross; between two neighbouring edges the membership is
        /// constant, and the area of a trapezoid is its height at the middle of the slab times the slab's width.
        /// </summary>
        Q ComputeArea()
        {
            var rings = Rings.Select((x, k) => (x.ring, k)).Where(x => x.ring.Length >= 3).ToList();
            if (rings.Count == 0) return Q.Zero;
            // Fast path (Filter == true): when all rings are simple, no edges properly cross, no vertex lies in the open interior
            // of another ring's edge, no two rings are equal, and every pair of rings has either nested or disjoint interiors
            // (a laminar family of simple polygons), D5's area equals the telescoping shoelace sum over the containment tree:
            //   sum_r ((Decide(anc(r) U {r}) ? 1 : 0) - (Decide(anc(r)) ? 1 : 0)) * shoelaceArea(r).
            // Exact (dyadic common-W BigInteger accumulation when all W are powers of two). The reference path keeps the slabs.
            if (Filter && TryComputeLaminarArea(rings, out Q laminarArea)) return laminarArea;
            var edges = new List<(Q ax, Q ay, Q bx, Q by, int ring)>();
            var boxes = new List<(double x0, double x1, double y0, double y1)>();
            bool boxed = Filter;
            for (int k = 0; k < rings.Count; ++k) {
                var r = rings[k].ring;
                var vb = Filter ? VertexBounds(r) : null;
                for (int i = 0; i < r.Length; ++i) {
                    Pt a = r[i], b = r[(i + 1) % r.Length];
                    Q ax = a.Qx, bx = b.Qx;
                    if (ax == bx) continue; // a vertical edge spans no slab
                    if (ax < bx) edges.Add((ax, a.Qy, bx, b.Qy, k)); else edges.Add((bx, b.Qy, ax, a.Qy, k));
                    if (boxed) {
                        int i2 = (i + 1) % r.Length;
                        if (double.IsNaN(vb[4 * i]) || double.IsNaN(vb[4 * i2])) boxed = false;
                        else boxes.Add((Math.Min(vb[4 * i], vb[4 * i2]), Math.Max(vb[4 * i + 1], vb[4 * i2 + 1]), Math.Min(vb[4 * i + 2], vb[4 * i2 + 2]), Math.Max(vb[4 * i + 3], vb[4 * i2 + 3])));
                    }
                }
            }
            var xs = new List<Q>();
            foreach (var e in edges) { xs.Add(e.ax); xs.Add(e.bx); }
            if (boxed) {
                // (filter: edges whose boxes, rounded outwards, are apart do not cross; a sweep in x visits only pairs whose x ranges meet)
                foreach (var (i, j) in BoxPairs(boxes))
                    if (TryCrossX(edges[i], edges[j], out Q x)) xs.Add(x);
            } else
                for (int i = 0; i < edges.Count; ++i)
                    for (int j = i + 1; j < edges.Count; ++j)
                        if (TryCrossX(edges[i], edges[j], out Q x)) xs.Add(x);
            xs.Sort();
            // (the edges in order of their left end; a slab sees the edges begun at or before it that have not ended before it)
            var order = Enumerable.Range(0, edges.Count).OrderBy(i => edges[i].ax).ToList();
            var active = new List<int>();
            int next = 0;
            Q area = Q.Zero;
            var parity = new bool[rings.Count];
            var containing = new List<int>();
            for (int s = 0; s + 1 < xs.Count; ++s) {
                Q x0 = xs[s], x1 = xs[s + 1];
                if (x0 == x1) continue;
                while (next < order.Count && edges[order[next]].ax <= x0) active.Add(order[next++]);
                active.RemoveAll(i => edges[i].bx < x1);
                Q xm = (x0 + x1) * new Q(1, 2);
                var spanning = new List<(Q y, int ring)>();
                foreach (int i in active) {
                    var e = edges[i];
                    spanning.Add((e.ay + (e.by - e.ay) * (xm - e.ax) / (e.bx - e.ax), e.ring));
                }
                spanning.Sort((p, q) => p.y.CompareTo(q.y));
                Array.Clear(parity, 0, parity.Length);
                // (the rings of odd parity, as their ids in ascending order, kept by insertion and removal: the same list as the scan of
                // all rings gave for every gap, at the cost of the toggled ring only)
                containing.Clear();
                for (int i = 0; i + 1 < spanning.Count; ++i) {
                    int ring = spanning[i].ring;
                    parity[ring] = !parity[ring];
                    int at = containing.BinarySearch(rings[ring].k);
                    if (parity[ring]) containing.Insert(~at, rings[ring].k); else containing.RemoveAt(at);
                    if (Decide(containing)) area += (spanning[i + 1].y - spanning[i].y) * (x1 - x0);
                }
            }
            return area;
        }

        /// <summary>
        /// Verifies that the rings form a laminar family of simple polygons (disjoint or strictly nested interiors) and computes
        /// the exact D5 area by shoelace accumulation. Returns false (caller falls back to exact slab decomposition) on any
        /// self-intersection, cross-ring edge crossing, vertex on another ring's open edge, equal rings, or partial interior overlap.
        /// </summary>
        bool TryComputeLaminarArea(List<(Pt[] ring, int k)> rings, out Q result)
        {
            result = Q.Zero;
            int m = rings.Count;
            if (m == 1) {
                if (!IsSimple(rings[0].ring)) return false;
                if (!Decide(new List<int> { rings[0].k })) { result = Q.Zero; return true; }
                result = ShoelaceArea(rings[0].ring);
                return true;
            }
            // Check all ring boxes are known, and collect all edges for one unified simplicity + non-crossing sweep.
            int totalEdges = 0;
            var rBoxes = new List<(double x0, double x1, double y0, double y1)>(m);
            for (int r = 0; r < m; ++r) {
                int rk = rings[r].k;
                if (double.IsNaN(ringBoxFlat[4 * rk])) return false;
                rBoxes.Add((ringBoxFlat[4 * rk], ringBoxFlat[4 * rk + 1], ringBoxFlat[4 * rk + 2], ringBoxFlat[4 * rk + 3]));
                totalEdges += rings[r].ring.Length;
            }
            var eBoxes = new List<(double x0, double x1, double y0, double y1)>(totalEdges);
            var eRing = new int[totalEdges];
            var eIdx = new int[totalEdges];
            int ec = 0;
            for (int r = 0; r < m; ++r) {
                var ring = rings[r].ring;
                var eb = ringEB[rings[r].k];
                for (int i = 0; i < ring.Length; ++i) {
                    int i2 = (i + 1) % ring.Length;
                    if (double.IsNaN(eb[4 * i]) || ring[i].Equals(ring[i2])) return false;
                    eBoxes.Add((eb[4 * i], eb[4 * i + 1], eb[4 * i + 2], eb[4 * i + 3]));
                    eRing[ec] = r;
                    eIdx[ec] = i;
                    ++ec;
                }
            }
            // Single sweep over all edges: validates every ring is simple, no cross-ring proper crossing, and no vertex in another ring's open edge.
            foreach (var (ei, ej) in BoxPairs(eBoxes)) {
                int ra = eRing[ei], rb = eRing[ej];
                var ringA = rings[ra].ring; var ringB = rings[rb].ring;
                int ia = eIdx[ei], ib = eIdx[ej];
                int ia2 = (ia + 1) % ringA.Length, ib2 = (ib + 1) % ringB.Length;
                Pt a = ringA[ia], b = ringA[ia2], c = ringB[ib], d = ringB[ib2];
                if (ra == rb) {
                    int len = ringA.Length;
                    int lo = Math.Min(ia, ib), hi = Math.Max(ia, ib);
                    if (hi == lo + 1 || (lo == 0 && hi == len - 1)) {
                        Pt sv = hi == lo + 1 ? ringA[hi] : ringA[0];
                        Pt p = hi == lo + 1 ? ringA[lo] : ringA[1];
                        Pt q = hi == lo + 1 ? ringA[(hi + 1) % len] : ringA[len - 1];
                        if (Exact.Orient(sv, p, q) == 0 && (p.Qx - sv.Qx) * (q.Qx - sv.Qx) + (p.Qy - sv.Qy) * (q.Qy - sv.Qy) > Q.Zero) return false;
                        continue;
                    }
                    if (Exact.ProperCross(a, b, c, d) || Exact.OnClosedSegment(a, b, c) || Exact.OnClosedSegment(a, b, d)
                        || Exact.OnClosedSegment(c, d, a) || Exact.OnClosedSegment(c, d, b)) return false;
                } else {
                    // Identical edges (shared boundary between touching rings) are allowed.
                    bool ac = a.Equals(c), ad = a.Equals(d), bc = b.Equals(c), bd = b.Equals(d);
                    if ((ac && bd) || (ad && bc)) continue;
                    // Double filter: if one segment's endpoints lie strictly on one side of the other's line, they cannot cross or touch.
                    var vba = ringVB[rings[ra].k]; var vbb = ringVB[rings[rb].k];
                    if (!(ac || ad || bc || bd)) {
                        int oc = SureOrient(vba, ia, ia2, vbb[4 * ib], vbb[4 * ib + 1], vbb[4 * ib + 2], vbb[4 * ib + 3]);
                        int od = SureOrient(vba, ia, ia2, vbb[4 * ib2], vbb[4 * ib2 + 1], vbb[4 * ib2 + 2], vbb[4 * ib2 + 3]);
                        if (oc != 0 && oc == od) continue;
                        int oa = SureOrient(vbb, ib, ib2, vba[4 * ia], vba[4 * ia + 1], vba[4 * ia + 2], vba[4 * ia + 3]);
                        int ob = SureOrient(vbb, ib, ib2, vba[4 * ia2], vba[4 * ia2 + 1], vba[4 * ia2 + 2], vba[4 * ia2 + 3]);
                        if (oa != 0 && oa == ob) continue;
                    }
                    if (Exact.ProperCross(a, b, c, d)) return false;
                    // Reject any vertex lying in the open interior of another ring's edge (including partial collinear overlap).
                    if (!ac && !bc && Exact.OnClosedSegment(a, b, c)) return false;
                    if (!ad && !bd && Exact.OnClosedSegment(a, b, d)) return false;
                    if (!ac && !ad && Exact.OnClosedSegment(c, d, a)) return false;
                    if (!bc && !bd && Exact.OnClosedSegment(c, d, b)) return false;
                }
            }
            // Check pairwise nesting / interior disjointness over overlapping ring-box pairs, and collect ancestors.
            var ancestors = new List<int>[m];
            foreach (var (ra, rb) in BoxPairs(rBoxes)) {
                int ka = rings[ra].k, kb = rings[rb].k;
                bool aInB = inside[ka, kb], bInA = inside[kb, ka];
                if (aInB && bInA) return false; // equal rings: fall back to slabs
                if (aInB) { (ancestors[ra] ??= new List<int>()).Add(kb); continue; }
                if (bInA) { (ancestors[rb] ??= new List<int>()).Add(ka); continue; }
                // Neither is nested in the other; verify their interiors are disjoint (no vertex or open-edge midpoint of either lies strictly inside the other).
                if (HasVertexOrMidpointStrictlyInside(ka, kb) || HasVertexOrMidpointStrictlyInside(kb, ka)) return false;
            }
            // All rings form a laminar family of simple polygons: compute telescoping exact shoelace sum.
            System.Numerics.BigInteger maxW = System.Numerics.BigInteger.One;
            bool dyadic = true;
            for (int r = 0; r < m && dyadic; ++r) {
                var ring = rings[r].ring;
                for (int i = 0; i < ring.Length; ++i) {
                    var w = ring[i].W;
                    if (!w.IsPowerOfTwo) { dyadic = false; break; }
                    if (w > maxW) maxW = w;
                }
            }
            System.Numerics.BigInteger totalTwice = System.Numerics.BigInteger.Zero;
            Q totalQ = Q.Zero;
            var withList = new List<int>();
            for (int r = 0; r < m; ++r) {
                var anc = ancestors[r];
                bool without;
                bool with;
                if (anc == null || anc.Count == 0) {
                    without = false;
                    with = !Rings[rings[r].k].hole;
                } else {
                    anc.Sort();
                    without = Decide(anc);
                    withList.Clear();
                    withList.AddRange(anc);
                    withList.Add(rings[r].k);
                    with = Decide(withList);
                }
                int sign = (with ? 1 : 0) - (without ? 1 : 0);
                if (sign == 0) continue;
                var ring = rings[r].ring;
                if (dyadic) {
                    System.Numerics.BigInteger twice = System.Numerics.BigInteger.Zero;
                    for (int i = 0; i < ring.Length; ++i) {
                        Pt a = ring[i], b = ring[(i + 1) % ring.Length];
                        var fa = maxW / a.W; var fb = maxW / b.W;
                        twice += (a.X * fa) * (b.Y * fb) - (b.X * fb) * (a.Y * fa);
                    }
                    totalTwice += sign * System.Numerics.BigInteger.Abs(twice);
                } else {
                    Q aQ = ShoelaceArea(ring);
                    totalQ = sign > 0 ? totalQ + aQ : totalQ - aQ;
                }
            }
            result = dyadic ? new Q(totalTwice, 2 * maxW * maxW) : totalQ;
            return true;
        }

        bool HasVertexOrMidpointStrictlyInside(int ka, int kb)
        {
            var ra = Rings[ka].ring;
            var vba = ringVB[ka];
            double bx0 = ringBoxFlat[4 * kb], bx1 = ringBoxFlat[4 * kb + 1], by0 = ringBoxFlat[4 * kb + 2], by1 = ringBoxFlat[4 * kb + 3];
            for (int i = 0; i < ra.Length; ++i) {
                double pxl = vba[4 * i], pxh = vba[4 * i + 1], pyl = vba[4 * i + 2], pyh = vba[4 * i + 3];
                if (pxh >= bx0 && pxl <= bx1 && pyh >= by0 && pyl <= by1) {
                    Pt? pc = ra[i];
                    if (InRingByIndex(kb, ref pc, null, true, pxl, pxh, pyl, pyh) == Where.Inside) return true;
                }
                int j = (i + 1) % ra.Length;
                double mxl = (pxl + vba[4 * j]) * 0.5, mxh = (pxh + vba[4 * j + 1]) * 0.5;
                double myl = (pyl + vba[4 * j + 2]) * 0.5, myh = (pyh + vba[4 * j + 3]) * 0.5;
                double padX = Math.Max(Math.Abs(mxl), Math.Abs(mxh)) * 1e-13 + 1e-300;
                double padY = Math.Max(Math.Abs(myl), Math.Abs(myh)) * 1e-13 + 1e-300;
                mxl -= padX; mxh += padX; myl -= padY; myh += padY;
                if (mxh >= bx0 && mxl <= bx1 && myh >= by0 && myl <= by1) {
                    Pt p = ra[i], q = ra[j];
                    Pt? mc = null;
                    if (InRingByIndex(kb, ref mc, () => new Pt((p.Qx + q.Qx) * new Q(1, 2), (p.Qy + q.Qy) * new Q(1, 2)), true, mxl, mxh, myl, myh) == Where.Inside)
                        return true;
                }
            }
            return false;
        }

        static Q ShoelaceArea(Pt[] r)
        {
            System.Numerics.BigInteger maxW = System.Numerics.BigInteger.One;
            bool dyadic = true;
            for (int i = 0; i < r.Length; ++i) {
                var w = r[i].W;
                if (!w.IsPowerOfTwo) { dyadic = false; break; }
                if (w > maxW) maxW = w;
            }
            if (dyadic) {
                System.Numerics.BigInteger twice = System.Numerics.BigInteger.Zero;
                for (int i = 0; i < r.Length; ++i) {
                    Pt a = r[i], b = r[(i + 1) % r.Length];
                    var fa = maxW / a.W; var fb = maxW / b.W;
                    twice += (a.X * fa) * (b.Y * fb) - (b.X * fb) * (a.Y * fa);
                }
                return new Q(System.Numerics.BigInteger.Abs(twice), 2 * maxW * maxW);
            }
            Q twiceQ = Q.Zero;
            for (int i = 0; i < r.Length; ++i) { Pt a = r[i], b = r[(i + 1) % r.Length]; twiceQ += a.Qx * b.Qy - b.Qx * a.Qy; }
            return Q.Abs(twiceQ) * new Q(1, 2);
        }

        /// <summary> the pairs i &lt; j of boxes that meet (closed), by a sweep in x </summary>
        static IEnumerable<(int, int)> BoxPairs(List<(double x0, double x1, double y0, double y1)> boxes)
        {
            var order = Enumerable.Range(0, boxes.Count).OrderBy(i => boxes[i].x0).ToArray();
            for (int a = 0; a < order.Length; ++a) {
                var p = boxes[order[a]];
                for (int b = a + 1; b < order.Length; ++b) {
                    var q = boxes[order[b]];
                    if (q.x0 > p.x1) break;
                    if (q.y1 < p.y0 || q.y0 > p.y1) continue;
                    yield return (Math.Min(order[a], order[b]), Math.Max(order[a], order[b]));
                }
            }
        }

        /// <summary>
        /// The ring is simple: no two edges meet except neighbours at their common vertex, which do not overlap (exact; pairs
        /// prefiltered by boxes). False when a bound is unknown.
        /// </summary>
        static bool IsSimple(Pt[] r)
        {
            int n = r.Length;
            var vb = VertexBounds(r);
            var boxes = new List<(double x0, double x1, double y0, double y1)>(n);
            for (int i = 0; i < n; ++i) {
                int i2 = (i + 1) % n;
                if (double.IsNaN(vb[4 * i]) || double.IsNaN(vb[4 * i2]) || r[i].Equals(r[i2])) return false;
                boxes.Add((Math.Min(vb[4 * i], vb[4 * i2]), Math.Max(vb[4 * i + 1], vb[4 * i2 + 1]), Math.Min(vb[4 * i + 2], vb[4 * i2 + 2]), Math.Max(vb[4 * i + 3], vb[4 * i2 + 3])));
            }
            foreach (var (i, j) in BoxPairs(boxes)) {
                int i2 = (i + 1) % n, j2 = (j + 1) % n;
                if (j == i + 1 || (i == 0 && j == n - 1)) {
                    // neighbours: they share one vertex s; they overlap when the other ends lie on the same ray from s
                    int si = j == i + 1 ? i2 : i, pi = j == i + 1 ? i : i2, qi = j == i + 1 ? j2 : j;
                    if (SureOrient(vb, si, pi, vb[4 * qi], vb[4 * qi + 1], vb[4 * qi + 2], vb[4 * qi + 3]) != 0) continue;
                    Pt sv = r[si], p = r[pi], q = r[qi];
                    if (Exact.Orient(sv, p, q) == 0 && (p.Qx - sv.Qx) * (q.Qx - sv.Qx) + (p.Qy - sv.Qy) * (q.Qy - sv.Qy) > Q.Zero) return false;
                    continue;
                }
                int oc = SureOrient(vb, i, i2, vb[4 * j], vb[4 * j + 1], vb[4 * j + 2], vb[4 * j + 3]);
                int od = SureOrient(vb, i, i2, vb[4 * j2], vb[4 * j2 + 1], vb[4 * j2 + 2], vb[4 * j2 + 3]);
                if (oc != 0 && oc == od) continue;
                int oa = SureOrient(vb, j, j2, vb[4 * i], vb[4 * i + 1], vb[4 * i + 2], vb[4 * i + 3]);
                int ob = SureOrient(vb, j, j2, vb[4 * i2], vb[4 * i2 + 1], vb[4 * i2 + 2], vb[4 * i2 + 3]);
                if (oa != 0 && oa == ob) continue;
                Pt a = r[i], b = r[i2], c = r[j], d = r[j2];
                if (Exact.ProperCross(a, b, c, d) || Exact.OnClosedSegment(a, b, c) || Exact.OnClosedSegment(a, b, d)
                    || Exact.OnClosedSegment(c, d, a) || Exact.OnClosedSegment(c, d, b)) return false;
            }
            return true;
        }

        static bool TryCrossX((Q ax, Q ay, Q bx, Q by, int ring) e, (Q ax, Q ay, Q bx, Q by, int ring) f, out Q x)
        {
            x = Q.Zero;
            Q dex = e.bx - e.ax, dey = e.by - e.ay, dfx = f.bx - f.ax, dfy = f.by - f.ay;
            Q denom = dex * dfy - dey * dfx;
            if (denom.Sign == 0) return false; // parallel: never crossing inside a slab, overlap has zero height
            Q t = ((f.ax - e.ax) * dfy - (f.ay - e.ay) * dfx) / denom;
            Q u = ((f.ax - e.ax) * dey - (f.ay - e.ay) * dex) / denom;
            if (t.Sign < 0 || t > Q.One || u.Sign < 0 || u > Q.One) return false;
            x = e.ax + t * dex;
            return true;
        }
    }
}
