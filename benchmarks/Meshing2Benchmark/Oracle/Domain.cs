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
        public readonly Q Area;
        public readonly Q Perimeter2Bound; // an upper bound of the summed ring lengths, for area tolerances

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
            inside = new bool[Rings.Count, Rings.Count];
            for (int a = 0; a < Rings.Count; ++a)
                for (int b = 0; b < Rings.Count; ++b)
                    if (a != b) inside[a, b] = Nested(Rings[a].ring, Rings[b].ring, Filter);
            Area = ComputeArea();
            Q length = Q.Zero;
            foreach (var ring in Outers.Concat(Holes))
                for (int i = 0; i < ring.Length; ++i) {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                    length += Q.Abs(a.Qx - b.Qx) + Q.Abs(a.Qy - b.Qy); // L1 length is at least the Euclidean length
                }
            Perimeter2Bound = length;
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
        sealed class EdgeBuckets
        {
            public double Y0, Y1, Step;
            public int[][] Buckets;
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
            return new EdgeBuckets { Y0 = y0, Y1 = y1, Step = step, Buckets = lists.Select(l => l.ToArray()).ToArray() };
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
            bool inside = false;
            Q py = p.Qy;
            var vb = VertexBounds(ring);
            // the edges to look at: one bucket if p's y interval lies inside it (with a bucket of margin on each side of every edge,
            // so that rounding in the bucket index cannot drop an edge), else all
            int[] only = null;
            if (filter && ring.Length > 16) {
                var bk = Buckets(ring);
                if (bk != null) {
                    // beyond every vertex's y interval: outside, not on it (compared directly: the bucket index is rounded, and a point on
                    // the top edge could get the index past the last bucket; found by the bounds review)
                    if (pyHi < bk.Y0 || pyLo > bk.Y1) return Where.Outside;
                    int last = bk.Buckets.Length - 1;
                    int b0 = Math.Max(0, Math.Min(last, (int)Math.Floor((pyLo - bk.Y0) / bk.Step)));
                    int b1 = Math.Max(0, Math.Min(last, (int)Math.Floor((pyHi - bk.Y0) / bk.Step)));
                    // (clamped: an edge at either end is in the end bucket too, it was added with a bucket of margin, clamped the same way)
                    if (b0 == b1) only = bk.Buckets[b0];
                }
            }
            int count = only?.Length ?? ring.Length;
            for (int q = 0; q < count; ++q) {
                int i = only != null ? only[q] : q;
                int j = (i + 1) % ring.Length;
                if (filter && !double.IsNaN(vb[4 * i]) && !double.IsNaN(vb[4 * j])) {
                    double eyl = Math.Min(vb[4 * i + 2], vb[4 * j + 2]), eyh = Math.Max(vb[4 * i + 3], vb[4 * j + 3]);
                    if (pyHi < eyl || pyLo > eyh) continue;
                    double exl = Math.Min(vb[4 * i], vb[4 * j]), exh = Math.Max(vb[4 * i + 1], vb[4 * j + 1]);
                    if (exh < pxLo) continue;
                    if (exl > pxHi) {
                        // entirely right of p: a crossing if the ends lie on both sides of p's y (decided in doubles where possible)
                        int sa = vb[4 * i + 2] > pyHi ? 1 : vb[4 * i + 3] < pyLo ? -1 : 0, sb = vb[4 * j + 2] > pyHi ? 1 : vb[4 * j + 3] < pyLo ? -1 : 0;
                        bool aUp = sa != 0 ? sa > 0 : ring[i].Qy > py, bUp = sb != 0 ? sb > 0 : ring[j].Qy > py;
                        if (aUp != bUp) inside = !inside;
                        continue;
                    }
                }
                Pt a = ring[i], b = ring[j];
                if (Exact.OnClosedSegment(a, b, p)) return Where.Boundary;
                bool aAbove = a.Qy > py, bAbove = b.Qy > py;
                if (aAbove == bAbove) continue;
                // half-open rule on y: the edge counts if it crosses the ray to the right of p
                int side = Exact.Orient(a, b, p);
                if (bAbove ? side > 0 : side < 0) inside = !inside;
            }
            return inside ? Where.Inside : Where.Outside;
        }

        /// <summary> per ring: its box in doubles around the exact one (NaN: unknown) </summary>
        readonly Dictionary<Pt[], double[]> ringBoxes = new Dictionary<Pt[], double[]>();

        /// <summary> InRing, with the ring's box first: a point certainly outside it is outside the ring and not on it </summary>
        Where InRingBoxed(Pt[] ring, Pt p, bool known, double pxl, double pxh, double pyl, double pyh)
        {
            if (!Filter || !known) return InRing(ring, p, false);
            if (!ringBoxes.TryGetValue(ring, out var box)) {
                box = new[] { double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity };
                foreach (var q in ring) {
                    if (!q.Bounds(false, out double xl, out double xh) || !q.Bounds(true, out double yl, out double yh)) { box = null; break; }
                    box[0] = Math.Min(box[0], xl); box[1] = Math.Max(box[1], xh); box[2] = Math.Min(box[2], yl); box[3] = Math.Max(box[3], yh);
                }
                lock (ringBoxes) ringBoxes[ring] = box;
            }
            if (box != null && (pxh < box[0] || pxl > box[1] || pyh < box[2] || pyl > box[3])) return Where.Outside;
            return InRing(ring, p, true, pxl, pxh, pyl, pyh);
        }

        public Where Classify(Pt p)
        {
            // the rings p is strictly inside of, and those it lies on; on a ring's boundary its two sides differ by that ring
            // (p's interval once for all rings)
            double pxl = 0, pxh = 0, pyl = 0, pyh = 0;
            bool known = Filter && p.Bounds(false, out pxl, out pxh) && p.Bounds(true, out pyl, out pyh);
            var strictly = new List<int>(); var on = new List<int>();
            for (int k = 0; k < Rings.Count; ++k) {
                var w = InRingBoxed(Rings[k].ring, p, known, pxl, pxh, pyl, pyh);
                if (w == Where.Inside) strictly.Add(k); else if (w == Where.Boundary) on.Add(k);
            }
            bool without = Decide(strictly);
            if (on.Count == 0) return without ? Where.Inside : Where.Outside;
            bool with = Decide(strictly.Concat(on).ToList());
            return with && without ? Where.Inside : !with && !without ? Where.Outside : Where.Boundary;
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
            var edges = new List<(Q ax, Q ay, Q bx, Q by, int ring)>();
            for (int k = 0; k < rings.Count; ++k) {
                var r = rings[k].ring;
                for (int i = 0; i < r.Length; ++i) {
                    Pt a = r[i], b = r[(i + 1) % r.Length];
                    Q ax = a.Qx, bx = b.Qx;
                    if (ax == bx) continue; // a vertical edge spans no slab
                    if (ax < bx) edges.Add((ax, a.Qy, bx, b.Qy, k)); else edges.Add((bx, b.Qy, ax, a.Qy, k));
                }
            }
            var xs = new List<Q>();
            foreach (var e in edges) { xs.Add(e.ax); xs.Add(e.bx); }
            for (int i = 0; i < edges.Count; ++i)
                for (int j = i + 1; j < edges.Count; ++j)
                    if (TryCrossX(edges[i], edges[j], out Q x)) xs.Add(x);
            xs.Sort();
            Q area = Q.Zero;
            var parity = new bool[rings.Count];
            for (int s = 0; s + 1 < xs.Count; ++s) {
                Q x0 = xs[s], x1 = xs[s + 1];
                if (x0 == x1) continue;
                Q xm = (x0 + x1) * new Q(1, 2);
                var spanning = new List<(Q y, int ring)>();
                foreach (var e in edges)
                    if (e.ax <= x0 && x1 <= e.bx)
                        spanning.Add((e.ay + (e.by - e.ay) * (xm - e.ax) / (e.bx - e.ax), e.ring));
                spanning.Sort((p, q) => p.y.CompareTo(q.y));
                Array.Clear(parity, 0, parity.Length);
                for (int i = 0; i + 1 < spanning.Count; ++i) {
                    parity[spanning[i].ring] = !parity[spanning[i].ring];
                    var containing = new List<int>();
                    for (int k = 0; k < rings.Count; ++k) if (parity[k]) containing.Add(rings[k].k);
                    if (Decide(containing)) area += (spanning[i + 1].y - spanning[i].y) * (x1 - x0);
                }
            }
            return area;
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
