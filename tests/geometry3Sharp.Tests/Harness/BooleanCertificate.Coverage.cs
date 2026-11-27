using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The union cover of C5 (#1631). A triangle t of one mesh (a sub-triangle of a result triangle, say) that no single triangle
    /// of the other mesh covers within Delta can still be covered by several of them together: the rounded result has fewer
    /// triangles than the exact one, so a result triangle is tiled by several nearly coplanar exact pieces, and a sub-triangle that
    /// straddles the boundary of two of them is within Delta of the union but of neither alone (the splitting does not converge).
    ///
    /// Take the axis a of the largest component of t's normal and the chart that drops the coordinate a (an affine bijection of
    /// t's plane onto the plane of the other two coordinates). For a triangle G of the other mesh whose chart is not degenerate,
    /// the points of t and of G above a chart point (u, v) differ along a by gap(u, v), affine. Over the region R(G) of the chart
    /// where both exist (the chart of t clipped by the three edges of G's) and |gap| is at most Delta (two more half-planes), every
    /// point x of t is within Delta of the point of G above it (the distance of two points that differ along one axis is that
    /// difference), so x is within Delta of G: the region is a convex polygon, computed in exact rationals. t is covered if the
    /// chart of t minus the union of the regions (convex polygons minus convex polygons, repeated: no assumption that the
    /// triangles of the other mesh are disjoint or coplanar) is empty, or what is left is covered the old way: each left polygon,
    /// lifted back onto the plane of t (exact), is within Delta of a single triangle (a polygon is within Delta of a convex
    /// triangle iff its corners are: the distance to a convex set is convex).
    ///
    /// Sound by construction: only exact sign tests and exact distance tests, no error bound, no property of the other mesh.
    /// Where a rule fails, t is left to the splitting as before. The cost is bounded: a triangle with a corner that is not within Delta
    /// of any triangle is not tried (it is not covered), and a difference that leaves more than MaxLeftover polygons is given up.
    /// </summary>
    internal sealed partial class BooleanCertificate
    {
        /// <summary> for the mutants and the tests of the cover: false takes the single-triangle cover and the splits only (the state before #1631) </summary>
        public bool UnionCover = true;
        /// <summary> triangles (and sub-triangles) of the last coverage test that the union cover decided </summary>
        public int UnionCovered;
        /// <summary> of those, the ones where polygons were left over by the regions and covered by single triangles </summary>
        public int UnionLeftoverCovered;

        // the cap on the polygons left over by the difference: more is given up (the splitting goes on), the cost stays bounded
        const int MaxLeftover = 64;

        static Q Abs(Q q) => q.Sign < 0 ? new Q(-q.N, q.D) : q;

        // an affine function c0 + cu u + cv v of the chart point (u, v)
        struct Affine
        {
            public Q C0, Cu, Cv;
            public Q Eval(Q[] p) => (C0 + Cu * p[0] + Cv * p[1]).Reduce();
            public static Affine operator -(Affine a, Affine b) => new Affine { C0 = (a.C0 - b.C0).Reduce(), Cu = (a.Cu - b.Cu).Reduce(), Cv = (a.Cv - b.Cv).Reduce() };
        }

        // the coordinate along the axis a of the plane of triangle t above the chart point: (n . t0 - n_b u - n_c v) / n_a, with (b, c) = (a + 1, a + 2) mod 3
        static Affine HeightOver(Q[][] t, int a, Q[] n)
        {
            Q d = VDot(n, t[0]);
            return new Affine { C0 = (d / n[a]).Reduce(), Cu = (new Q(-n[(a + 1) % 3].N, n[(a + 1) % 3].D) / n[a]).Reduce(), Cv = (new Q(-n[(a + 2) % 3].N, n[(a + 2) % 3].D) / n[a]).Reduce() };
        }

        static Q[] NormalOfQ(Q[][] t)
        {
            Q[] u = VSub(t[1], t[0]), v = VSub(t[2], t[0]);
            return new[] { (u[1] * v[2] - u[2] * v[1]).Reduce(), (u[2] * v[0] - u[0] * v[2]).Reduce(), (u[0] * v[1] - u[1] * v[0]).Reduce() };
        }

        static int LargestAxis(Q[] n)
        {
            int a = 0;
            for (int k = 1; k < 3; ++k)
                if (Q.Compare(Abs(n[k]), Abs(n[a])) > 0) a = k;
            return a;
        }

        // a point of the chart: two rationals
        static Q[] Chart(Q[] p, int a) => new[] { p[(a + 1) % 3], p[(a + 2) % 3] };

        // twice the signed area of the chart polygon (counterclockwise: positive)
        static Q Area2(List<Q[]> poly)
        {
            Q s = new Q(0, 1);
            for (int i = 0; i < poly.Count; ++i) {
                Q[] p = poly[i], q = poly[(i + 1) % poly.Count];
                s = (s + p[0] * q[1] - p[1] * q[0]).Reduce();
            }
            return s;
        }

        // the side of x of the directed line a -> b (positive: left)
        static Q Side2(Q[] a, Q[] b, Q[] x) => ((b[0] - a[0]) * (x[1] - a[1]) - (b[1] - a[1]) * (x[0] - a[0])).Reduce();

        // the part of a convex polygon where h >= 0 (h affine along every edge: Sutherland-Hodgman in exact rationals)
        static List<Q[]> Clip(List<Q[]> poly, Func<Q[], Q> h)
        {
            var r = new List<Q[]>();
            int n = poly.Count;
            if (n == 0) return r;
            var v = new Q[n];
            for (int i = 0; i < n; ++i) v[i] = h(poly[i]);
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                if (v[i].Sign >= 0) r.Add(poly[i]);
                if ((v[i].Sign > 0 && v[j].Sign < 0) || (v[i].Sign < 0 && v[j].Sign > 0)) {
                    Q t = (v[i] / (v[i] - v[j])).Reduce();
                    r.Add(new[] { (poly[i][0] + (poly[j][0] - poly[i][0]) * t).Reduce(), (poly[i][1] + (poly[j][1] - poly[i][1]) * t).Reduce() });
                }
            }
            return r;
        }

        static List<Q[]> ClipLeft(List<Q[]> poly, Q[] a, Q[] b) => Clip(poly, x => Side2(a, b, x));

        // the convex polygon minus a convex polygon g (counterclockwise): convex polygons of positive area whose union contains the
        // difference (a point of the polygon not in g is strictly outside an edge of g; the first such edge gives its piece)
        static List<List<Q[]>> Difference(List<Q[]> poly, List<Q[]> g)
        {
            var pieces = new List<List<Q[]>>();
            // (disjoint from g, or inside it: nothing to cut)
            bool inside = true;
            for (int e = 0; e < g.Count; ++e) {
                bool allOut = true;
                foreach (Q[] p in poly) {
                    int s = Side2(g[e], g[(e + 1) % g.Count], p).Sign;
                    if (s >= 0) allOut = false;
                    if (s < 0) inside = false;
                }
                if (allOut) { pieces.Add(poly); return pieces; }
            }
            if (inside) return pieces;
            List<Q[]> cur = poly;
            for (int e = 0; e < g.Count && cur.Count > 0; ++e) {
                Q[] a = g[e], b = g[(e + 1) % g.Count];
                List<Q[]> outside = Clip(cur, x => { Q s = Side2(a, b, x); return new Q(-s.N, s.D); });
                if (outside.Count >= 3 && Area2(outside).Sign > 0) pieces.Add(outside);
                cur = ClipLeft(cur, a, b);
            }
            return pieces;
        }

        /// <summary> for tests: whether t is covered within delta by the union of the triangles `to` (all of them candidates); viaLeftover: by polygons left over by the regions, covered one by one </summary>
        public static bool UnionCovers(ExactPoint3[] t, IList<ExactPoint3[]> to, double delta, out bool viaLeftover)
        {
            var c = new BooleanCertificate { Delta = delta };
            int before = c.UnionLeftoverCovered;
            bool r = c.CoveredByUnion(t.Select(Vec).ToArray(), Enumerable.Range(0, to.Count).ToList(), to.Select(x => x.Select(Vec).ToArray()).ToList(), c.DeltaQ(), c.DeltaSquared());
            viaLeftover = c.UnionLeftoverCovered > before;
            return r;
        }

        /// <summary>
        /// whether the triangle t (rational corners) is covered by the union of the candidates within delta (the class comment); delta2 its square
        /// </summary>
        bool CoveredByUnion(Q[][] t, List<int> cand, List<Q[][]> to, Q delta, Q delta2)
        {
            if (UnionCover == false || cand.Count < 2) return false;
            Q[] tn = NormalOfQ(t);
            if (tn[0].Sign == 0 && tn[1].Sign == 0 && tn[2].Sign == 0) return false;
            int a = LargestAxis(tn);
            var poly = new List<Q[]> { Chart(t[0], a), Chart(t[1], a), Chart(t[2], a) };
            if (Area2(poly).Sign < 0) poly.Reverse();
            Affine tz = HeightOver(t, a, tn);
            // the region of each triangle: its chart over the chart of t, within delta along a
            var regions = new List<List<Q[]>>();
            foreach (int j in cand) {
                Q[][] g = to[j];
                Q[] gn = NormalOfQ(g);
                if (gn[a].Sign == 0) continue;              // a chart that collapses (G parallel to the axis a)
                var gc = new List<Q[]> { Chart(g[0], a), Chart(g[1], a), Chart(g[2], a) };
                if (Area2(gc).Sign < 0) gc.Reverse();
                List<Q[]> region = poly;
                for (int e = 0; e < 3 && region.Count > 0; ++e) region = ClipLeft(region, gc[e], gc[(e + 1) % 3]);
                if (region.Count < 3 || Area2(region).Sign <= 0) continue;
                Affine gap = tz - HeightOver(g, a, gn);
                region = Clip(region, x => (delta - gap.Eval(x)).Reduce());
                region = Clip(region, x => (delta + gap.Eval(x)).Reduce());
                if (region.Count < 3 || Area2(region).Sign <= 0) continue;
                regions.Add(region);
            }
            if (regions.Count == 0) return false;
            var left = new List<List<Q[]>> { poly };
            foreach (var region in regions) {
                var next = new List<List<Q[]>>();
                foreach (var piece in left) next.AddRange(Difference(piece, region));
                left = next;
                if (left.Count == 0) { UnionCovered++; return true; }
                if (left.Count > MaxLeftover) return false;
            }
            // what is left, lifted onto the plane of t, is within delta of one triangle of the other mesh (the old test, per polygon)
            foreach (var piece in left) {
                var lifted = new List<Q[]>();
                foreach (Q[] c in piece) {
                    var x = new Q[3];
                    x[(a + 1) % 3] = c[0]; x[(a + 2) % 3] = c[1]; x[a] = tz.Eval(c);
                    lifted.Add(x);
                }
                bool covered = false;
                foreach (int j in cand) {
                    bool all = true;
                    foreach (Q[] x in lifted)
                        if (Near(x, to[j], delta2) == false) { all = false; break; }
                    if (all) { covered = true; break; }
                }
                if (covered == false) return false;
            }
            UnionCovered++; UnionLeftoverCovered++;
            return true;
        }
    }
}
