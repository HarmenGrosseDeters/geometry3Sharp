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
        public readonly Q Area;
        public readonly Q Perimeter2Bound; // an upper bound of the summed ring lengths, for area tolerances

        public Domain(Mesh2Input input)
        {
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
                    if (a != b) inside[a, b] = Nested(Rings[a].ring, Rings[b].ring);
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
        static bool Nested(Pt[] a, Pt[] b)
        {
            if (a.Length < 3 || b.Length < 3) return false;
            foreach (var p in a) if (InRing(b, p) == Where.Outside) return false;
            for (int i = 0; i < a.Length; ++i) {
                Pt p = a[i], q = a[(i + 1) % a.Length];
                var m = new Pt((p.Qx + q.Qx) * new Q(1, 2), (p.Qy + q.Qy) * new Q(1, 2));
                if (InRing(b, m) == Where.Outside) return false;
            }
            for (int i = 0; i < a.Length; ++i)
                for (int j = 0; j < b.Length; ++j)
                    if (Exact.ProperCross(a[i], a[(i + 1) % a.Length], b[j], b[(j + 1) % b.Length])) return false;
            return true;
        }

        /// <summary>Even-odd position against one ring, by exact crossing counts.</summary>
        /// <summary> per ring: the y range of each edge in doubles (an interval around the exact range), NaN where unknown </summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pt[], double[]> edgeRanges = new System.Runtime.CompilerServices.ConditionalWeakTable<Pt[], double[]>();

        static double[] EdgeRanges(Pt[] ring) => edgeRanges.GetValue(ring, r => {
            var lo = new double[r.Length]; var hi = new double[r.Length]; var known = new bool[r.Length];
            for (int i = 0; i < r.Length; ++i) known[i] = r[i].Bounds(true, out lo[i], out hi[i]);
            var ranges = new double[2 * r.Length];
            for (int i = 0; i < r.Length; ++i) {
                int j = (i + 1) % r.Length;
                bool k = known[i] && known[j];
                ranges[2 * i] = k ? Math.Min(lo[i], lo[j]) : double.NaN;
                ranges[2 * i + 1] = k ? Math.Max(hi[i], hi[j]) : double.NaN;
            }
            return ranges;
        });

        public static Where InRing(Pt[] ring, Pt p)
        {
            if (ring.Length == 0) return Where.Outside;
            bool inside = false;
            Q py = p.Qy;
            // a floating-point filter: an edge whose y range certainly excludes p's y neither contains p nor crosses its ray
            var ranges = EdgeRanges(ring);
            bool filter = p.Bounds(true, out double pyLo, out double pyHi);
            for (int i = 0; i < ring.Length; ++i) {
                if (filter && (pyHi < ranges[2 * i] || pyLo > ranges[2 * i + 1])) continue;   // (false for NaN: no filter)
                Pt a = ring[i], b = ring[(i + 1) % ring.Length];
                if (Exact.OnClosedSegment(a, b, p)) return Where.Boundary;
                bool aAbove = a.Qy > py, bAbove = b.Qy > py;
                if (aAbove == bAbove) continue;
                // half-open rule on y: the edge counts if it crosses the ray to the right of p
                int side = Exact.Orient(a, b, p);
                if (bAbove ? side > 0 : side < 0) inside = !inside;
            }
            return inside ? Where.Inside : Where.Outside;
        }

        public Where Classify(Pt p)
        {
            // the rings p is strictly inside of, and those it lies on; on a ring's boundary its two sides differ by that ring
            var strictly = new List<int>(); var on = new List<int>();
            for (int k = 0; k < Rings.Count; ++k) {
                var w = InRing(Rings[k].ring, p);
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
