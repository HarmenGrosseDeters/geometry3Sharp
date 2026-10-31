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
        public static Where InRing(Pt[] ring, Pt p)
        {
            if (ring.Length == 0) return Where.Outside;
            bool inside = false;
            Q py = p.Qy;
            for (int i = 0; i < ring.Length; ++i) {
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
            bool inOuter = false, onOuter = false, inHole = false, onHole = false;
            foreach (var r in Outers) { var w = InRing(r, p); inOuter |= w == Where.Inside; onOuter |= w == Where.Boundary; }
            foreach (var r in Holes) { var w = InRing(r, p); inHole |= w == Where.Inside; onHole |= w == Where.Boundary; }
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
