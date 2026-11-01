using System;
using System.Collections.Generic;
using System.Linq;

namespace Meshing2Acceptance
{
    public enum Where { Outside, Boundary, Inside }

    /// <summary>
    /// The region to mesh, computed from the rings alone, exactly: the union of the even-odd interiors of the outer rings
    /// (the convex hull of the input points if there is no outer ring) minus the union of the even-odd interiors of the hole
    /// rings. Spec: Q7 (a hole removes everything inside its ring), F-7 (bowtie), F-44 (a nested ring does not fill back).
    /// Changing the rule (decision D5 of docs/design/MESHING2D_DECISIONS.md) means changing <see cref="Combine"/> only.
    /// </summary>
    public sealed class Domain
    {
        public readonly List<Pt[]> Outers = new List<Pt[]>(), Holes = new List<Pt[]>();
        public readonly bool IsHull;
        /// <summary> false: no floating-point filter in the ring tests (the oracle's reference path) </summary>
        public bool Filter = true;
        public readonly Q Area;
        public readonly Q Perimeter2Bound; // an upper bound of the summed ring lengths, for area tolerances

        public Domain(Mesh2Input input)
        {
            var pts = new Pt[input.Count];
            for (int i = 0; i < pts.Length; ++i) pts[i] = Pt.FromDoubles(input.X[i], input.Y[i]);
            foreach (var r in input.Rings) {
                var ring = OpenRing(r.Vertices.Select(i => pts[i]).ToArray());
                (r.Hole ? Holes : Outers).Add(ring);
            }
            if (Outers.Count == 0) {
                Outers.Add(Exact.ConvexHull(pts).ToArray());
                IsHull = true;
            }
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

        /// <summary>The one place that says how the rings combine.</summary>
        static bool Combine(bool anyOuter, bool anyHole) => anyOuter && !anyHole;

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
            bool inOuter = false, onOuter = false, inHole = false, onHole = false;
            // p's interval once for all rings
            double pxl = 0, pxh = 0, pyl = 0, pyh = 0;
            bool known = Filter && p.Bounds(false, out pxl, out pxh) && p.Bounds(true, out pyl, out pyh);
            foreach (var r in Outers) { var w = InRingBoxed(r, p, known, pxl, pxh, pyl, pyh); inOuter |= w == Where.Inside; onOuter |= w == Where.Boundary; }
            foreach (var r in Holes) { var w = InRingBoxed(r, p, known, pxl, pxh, pyl, pyh); inHole |= w == Where.Inside; onHole |= w == Where.Boundary; }
            if (Combine(inOuter, inHole)) return onHole ? Where.Boundary : Where.Inside;
            if (inHole) return Where.Outside;
            if (Combine(inOuter || onOuter, false)) return Where.Boundary;
            return Where.Outside;
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
            var rings = Outers.Select(r => (r, false)).Concat(Holes.Select(r => (r, true))).Where(x => x.r.Length >= 3).ToList();
            var edges = new List<(Q ax, Q ay, Q bx, Q by, int ring)>();
            for (int k = 0; k < rings.Count; ++k) {
                var r = rings[k].r;
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
                    bool anyOuter = false, anyHole = false;
                    for (int k = 0; k < rings.Count; ++k) {
                        if (!parity[k]) continue;
                        if (rings[k].Item2) anyHole = true; else anyOuter = true;
                    }
                    if (Combine(anyOuter, anyHole)) area += (spanning[i + 1].y - spanning[i].y) * (x1 - x0);
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
