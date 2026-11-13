using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// Exact area of a region given by winding numbers of integer rings, by definition and with no code shared with
    /// Clipper2: the plane is cut into vertical slabs at every vertex x and every x of a crossing of two edges (rational).
    /// Inside a slab no two edges cross, so the edges spanning it are ordered by their y at the slab's middle, and the
    /// winding number is constant between consecutive ones; each such trapezoid adds its exact area (BigInteger
    /// fractions) if the predicate holds for the winding numbers there.
    /// </summary>
    static class ExactRegionArea
    {
        /// <summary> a rational n / d with d &gt; 0, not reduced except on demand </summary>
        readonly struct Q : IComparable<Q>
        {
            public readonly BigInteger N, D;
            public Q(BigInteger n, BigInteger d) { if (d.Sign < 0) { n = -n; d = -d; } N = n; D = d; }
            public static implicit operator Q(long v) => new Q(v, BigInteger.One);
            public static Q operator +(Q a, Q b) => new Q(a.N * b.D + b.N * a.D, a.D * b.D).Reduce();
            public static Q operator -(Q a, Q b) => new Q(a.N * b.D - b.N * a.D, a.D * b.D).Reduce();
            public static Q operator *(Q a, Q b) => new Q(a.N * b.N, a.D * b.D).Reduce();
            public static Q operator /(Q a, Q b) => new Q(a.N * b.D, a.D * b.N).Reduce();
            public Q Reduce() { BigInteger g = BigInteger.GreatestCommonDivisor(N, D); return g.IsOne || g.IsZero ? this : new Q(N / g, D / g); }
            public int CompareTo(Q o) => (N * o.D).CompareTo(o.N * D);
            public override bool Equals(object o) => o is Q q && CompareTo(q) == 0;
            public override int GetHashCode() { Q r = Reduce(); return r.N.GetHashCode() ^ r.D.GetHashCode(); }
            /// <summary> rounded to double, also when N and D exceed the double range (scaled to 64 bits first) </summary>
            public double ToDouble()
            {
                if (N.IsZero) return 0;
                int k = 64 - (Bits(BigInteger.Abs(N)) - Bits(D));
                BigInteger q = k >= 0 ? (N << k) / D : N / (D << -k);
                return (double)q * Math.Pow(2, -k);
            }

            static int Bits(BigInteger v) => v.ToByteArray().Length * 8;
        }

        struct Edge { public long X0, Y0, X1, Y1; public int Operand; public int Dir; }   // X0 < X1; Dir +1 if the ring goes +x

        /// <summary>
        /// The exact area of { p : inside(w_0(p), w_1(p), ...) } for the winding numbers w_k of operand k (each operand a
        /// list of closed rings with integer vertices), as a double rounded from the exact rational.
        /// </summary>
        public static double Area(IReadOnlyList<IReadOnlyList<(long x, long y)[]>> operands, Func<int[], bool> inside)
        {
            var edges = new List<Edge>();
            var xs = new List<Q>();
            for (int k = 0; k < operands.Count; ++k)
                foreach (var ring in operands[k])
                    for (int i = 0; i < ring.Length; ++i) {
                        var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                        xs.Add(a.x);
                        if (a.x == b.x) continue;   // vertical edges bound no slab
                        edges.Add(a.x < b.x ? new Edge { X0 = a.x, Y0 = a.y, X1 = b.x, Y1 = b.y, Operand = k, Dir = 1 }
                                            : new Edge { X0 = b.x, Y0 = b.y, X1 = a.x, Y1 = a.y, Operand = k, Dir = -1 });
                    }
            // x of every proper crossing of two edges
            for (int i = 0; i < edges.Count; ++i)
                for (int j = i + 1; j < edges.Count; ++j) {
                    Edge e = edges[i], f = edges[j];
                    if (e.X1 <= f.X0 || f.X1 <= e.X0) continue;
                    // e(t) = (X0 + t dx, Y0 + t dy); solve cross((f1 - f0), e(t) - f0) = 0 for t
                    BigInteger edx = e.X1 - e.X0, edy = e.Y1 - e.Y0, fdx = f.X1 - f.X0, fdy = f.Y1 - f.Y0;
                    BigInteger den = fdx * edy - fdy * edx;
                    if (den.IsZero) continue;   // parallel: no crossing inside a slab
                    BigInteger num = fdy * (e.X0 - f.X0) - fdx * (e.Y0 - f.Y0);   // t = num / den
                    var t = new Q(num, den);
                    if (t.CompareTo(0) <= 0 || t.CompareTo(1) >= 0) continue;
                    xs.Add(new Q(e.X0, 1) + t * new Q(edx, 1));
                }
            List<Q> cuts = xs.Distinct().OrderBy(q => q).ToList();
            Q total = 0;
            var w = new int[operands.Count];
            for (int s = 0; s + 1 < cuts.Count; ++s) {
                Q x0 = cuts[s], x1 = cuts[s + 1], xm = (x0 + x1) / 2;
                var spanning = new List<(Q ym, Q y0, Q y1, Edge e)>();
                foreach (Edge e in edges) {
                    if (new Q(e.X0, 1).CompareTo(x0) > 0 || new Q(e.X1, 1).CompareTo(x1) < 0) continue;
                    spanning.Add((Y(e, xm), Y(e, x0), Y(e, x1), e));
                }
                spanning.Sort((a, b) => a.ym.CompareTo(b.ym));
                Array.Clear(w, 0, w.Length);
                // from below: crossing an edge upward adds its Dir (the bottom edge of a counterclockwise ring goes +x,
                // and its inside, winding +1, is above it)
                for (int i = 0; i + 1 < spanning.Count; ++i) {
                    w[spanning[i].e.Operand] += spanning[i].e.Dir;
                    if (!inside(w)) continue;
                    Q h0 = spanning[i + 1].y0 - spanning[i].y0, h1 = spanning[i + 1].y1 - spanning[i].y1;
                    total = total + (x1 - x0) * (h0 + h1) / 2;
                }
            }
            return total.ToDouble();
        }

        static Q Y(Edge e, Q x) => new Q(e.Y0, 1) + new Q(e.Y1 - e.Y0, e.X1 - e.X0) * (x - new Q(e.X0, 1));
    }
}
