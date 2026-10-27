using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactTriangulation2 on its own, with exact integer predicates: random points (many on the edges of the outer
    /// triangle and on common lines) and non-crossing constraint segments, checked against the definition of a
    /// constrained Delaunay triangulation; and the documented exceptions for input that breaks the preconditions.
    /// Found with the mutation tests (tests/mutation, stryker-mesh-planar.json): the class was only tested through
    /// the boolean, where the arrangement never hands it collinear points or invalid input.
    /// </summary>
    public class ExactTriangulation2Tests
    {
        sealed class Points
        {
            public readonly List<(long x, long y)> P = new List<(long, long)>();
            public int Add(long x, long y) { P.Add((x, y)); return P.Count - 1; }

            public int Orient(int a, int b, int c)
            {
                var (ax, ay) = P[a]; var (bx, by) = P[b]; var (cx, cy) = P[c];
                return Math.Sign((bx - ax) * (cy - ay) - (by - ay) * (cx - ax));
            }

            // +1 if d is inside the circle through counter-clockwise a, b, c
            public int InCircle(int a, int b, int c, int d)
            {
                var (dx, dy) = P[d];
                BigInteger[] r = new[] { a, b, c }.Select(i => {
                    BigInteger x = P[i].x - dx, y = P[i].y - dy;
                    return new[] { x, y, x * x + y * y };
                }).SelectMany(v => v).ToArray();
                BigInteger det = r[0] * (r[4] * r[8] - r[5] * r[7]) - r[1] * (r[3] * r[8] - r[5] * r[6]) + r[2] * (r[3] * r[7] - r[4] * r[6]);
                return det.Sign;
            }

            // p on the closed segment (a, b)
            public bool OnSegment(int p, int a, int b)
            {
                if (Orient(a, b, p) != 0) return false;
                return Math.Min(P[a].x, P[b].x) <= P[p].x && P[p].x <= Math.Max(P[a].x, P[b].x)
                    && Math.Min(P[a].y, P[b].y) <= P[p].y && P[p].y <= Math.Max(P[a].y, P[b].y);
            }

            // the open segments (a, b) and (c, d) cross or overlap
            public bool Intersect(int a, int b, int c, int d)
            {
                if (a == c || a == d || b == c || b == d) {
                    // sharing an end: only an overlap along a common line counts
                    int s = (a == c || a == d) ? a : b, e1 = (s == a) ? b : a, e2 = (s == c) ? d : c;
                    return Orient(s, e1, e2) == 0 && (OnSegment(e1, s, e2) || OnSegment(e2, s, e1));
                }
                int o1 = Orient(a, b, c), o2 = Orient(a, b, d), o3 = Orient(c, d, a), o4 = Orient(c, d, b);
                if (o1 * o2 < 0 && o3 * o4 < 0) return true;
                return OnSegment(c, a, b) || OnSegment(d, a, b) || OnSegment(a, c, d) || OnSegment(b, c, d);
            }

            public ExactTriangulation2 Triangulation(int c0, int c1, int c2) =>
                new ExactTriangulation2(c0, c1, c2, Orient, InCircle);
        }

        static void AssertConstrainedDelaunay(Points pts, ExactTriangulation2 tr, IEnumerable<int> vertices, int[] outer,
            List<(int, int)> constraints, bool delaunay)
        {
            var tris = tr.Triangles().ToList();
            var directed = new Dictionary<(int, int), int>();
            foreach (Index3i t in tris) {
                // counter-clockwise, never flat
                Assert.Equal(1, pts.Orient(t.a, t.b, t.c));
                for (int k = 0; k < 3; ++k) {
                    var e = (t[k], t[(k + 1) % 3]);
                    Assert.False(directed.ContainsKey(e), $"edge {e} twice");
                    directed[e] = t[(k + 2) % 3];
                }
            }
            // the triangles tile the outer triangle: twice the areas add up, and every edge is an inner edge (both
            // directions) or on the outer boundary
            Func<int, int, int, long> area2 = (a, b, c) =>
                (pts.P[b].x - pts.P[a].x) * (pts.P[c].y - pts.P[a].y) - (pts.P[b].y - pts.P[a].y) * (pts.P[c].x - pts.P[a].x);
            Assert.Equal(area2(outer[0], outer[1], outer[2]), tris.Sum(t => area2(t.a, t.b, t.c)));
            foreach (var e in directed.Keys)
                if (directed.ContainsKey((e.Item2, e.Item1)) == false)
                    Assert.True(pts.OnSegment(e.Item1, outer[0], outer[1]) && pts.OnSegment(e.Item2, outer[0], outer[1])
                        || pts.OnSegment(e.Item1, outer[1], outer[2]) && pts.OnSegment(e.Item2, outer[1], outer[2])
                        || pts.OnSegment(e.Item1, outer[2], outer[0]) && pts.OnSegment(e.Item2, outer[2], outer[0]), $"boundary edge {e} inside");
            // exactly the inserted points are vertices
            var used = new HashSet<int>(tris.SelectMany(t => new[] { t.a, t.b, t.c }));
            Assert.True(used.SetEquals(vertices), "vertices");
            // the constraints are edges
            var cset = new HashSet<(int, int)>();
            foreach (var (u, v) in constraints) {
                Assert.True(directed.ContainsKey((u, v)) || directed.ContainsKey((v, u)), $"constraint ({u}, {v}) missing");
                cset.Add((Math.Min(u, v), Math.Max(u, v)));
            }
            if (delaunay == false)
                return;
            // every inner edge that is not a constraint is locally Delaunay
            foreach (var kv in directed) {
                var (a, b) = kv.Key;
                int d;
                if (directed.TryGetValue((b, a), out d) == false || cset.Contains((Math.Min(a, b), Math.Max(a, b))))
                    continue;
                Assert.True(pts.InCircle(a, b, kv.Value, d) <= 0, $"edge ({a}, {b}) not Delaunay");
            }
        }

        // the outer triangle (0, 0), (n, 0), (0, n) and random grid points in it (closed: some on its edges, many on
        // common lines), random non-crossing constraints between them that pass through no other point
        static (Points, int[], List<int>, List<(int, int)>) RandomInput(int seed, int n, int count, int constraintCount)
        {
            var rnd = new Random(seed);
            var pts = new Points();
            int[] outer = { pts.Add(0, 0), pts.Add(n, 0), pts.Add(0, n) };
            var seen = new HashSet<(long, long)> { (0, 0), (n, 0), (0, n) };
            var inner = new List<int>();
            while (inner.Count < count) {
                long x = rnd.Next(n + 1), y = rnd.Next(n + 1 - (int)x);
                // a third on the outer edges
                switch (rnd.Next(9)) {
                    case 0: y = 0; break;
                    case 1: x = 0; break;
                    case 2: y = n - x; break;
                }
                if (seen.Add((x, y)))
                    inner.Add(pts.Add(x, y));
            }
            var all = outer.Concat(inner).ToList();
            var constraints = new List<(int, int)>();
            for (int tries = 0; constraints.Count < constraintCount && tries < 50 * constraintCount; ++tries) {
                int u = inner[rnd.Next(inner.Count)], v = inner[rnd.Next(inner.Count)];
                if (u == v) continue;
                if (all.Any(p => p != u && p != v && pts.OnSegment(p, u, v))) continue;
                if (constraints.Any(c => pts.Intersect(u, v, c.Item1, c.Item2))) continue;
                constraints.Add((u, v));
            }
            return (pts, outer, inner, constraints);
        }

        [Theory]
        [InlineData(1, 12, 20, 6)]
        [InlineData(2, 30, 80, 25)]
        [InlineData(3, 8, 25, 10)]          // dense: most points on common lines
        [InlineData(4, 1000, 200, 40)]
        public void RandomPointsAndConstraints_ConstrainedDelaunay(int seed, int n, int count, int constraintCount)
        {
            var (pts, outer, inner, constraints) = RandomInput(seed, n, count, constraintCount);
            Assert.True(constraints.Count >= constraintCount / 2, "too few constraints generated");
            var tr = pts.Triangulation(outer[0], outer[1], outer[2]);
            foreach (int p in inner)
                tr.InsertPoint(p);
            // inserting a point again changes nothing
            tr.InsertPoint(inner[0]);
            AssertConstrainedDelaunay(pts, tr, outer.Concat(inner), outer, new List<(int, int)>(), true);
            foreach (var (u, v) in constraints)
                tr.InsertConstraint(u, v);
            // a constraint twice, or of length zero, changes nothing
            tr.InsertConstraint(constraints[0].Item1, constraints[0].Item2);
            tr.InsertConstraint(inner[1], inner[1]);
            AssertConstrainedDelaunay(pts, tr, outer.Concat(inner), outer, constraints, false);
            tr.MakeDelaunay();
            AssertConstrainedDelaunay(pts, tr, outer.Concat(inner), outer, constraints, true);
        }

        [Fact]
        public void ConstraintAcrossManyEdges_Inserted()
        {
            // a fan of points above and below a long segment, which crosses all the edges between them
            var pts = new Points();
            int[] outer = { pts.Add(-100, -100), pts.Add(200, -100), pts.Add(-100, 200) };
            int u = pts.Add(-50, 0), v = pts.Add(50, 0);
            var inner = new List<int> { u, v };
            for (int i = -9; i <= 9; i += 2) {
                inner.Add(pts.Add(5 * i, 1 + (i + 9) % 3));
                inner.Add(pts.Add(5 * i + 1, -1 - (i + 9) % 4));
            }
            var tr = pts.Triangulation(outer[0], outer[1], outer[2]);
            foreach (int p in inner)
                tr.InsertPoint(p);
            var c = new List<(int, int)> { (u, v) };
            tr.InsertConstraint(u, v);
            tr.MakeDelaunay();
            AssertConstrainedDelaunay(pts, tr, outer.Concat(inner), outer, c, true);
            Assert.Equal(0, tr.LinearSearches);
        }

        [Fact]
        public void InvalidInput_Throws()
        {
            var pts = new Points();
            int[] outer = { pts.Add(0, 0), pts.Add(10, 0), pts.Add(0, 10) };
            int p = pts.Add(2, 2), q = pts.Add(6, 2), r = pts.Add(4, 2), s = pts.Add(4, 0), t = pts.Add(4, 5);
            int outside = pts.Add(8, 8), copy = pts.Add(2, 2), notInserted = pts.Add(1, 1);

            var tr = pts.Triangulation(outer[0], outer[1], outer[2]);
            Assert.Throws<InvalidOperationException>(() => tr.InsertPoint(outside));
            tr.InsertPoint(p);
            Assert.Throws<InvalidOperationException>(() => tr.InsertPoint(copy));
            // (a corner of the outer triangle at another index)
            Assert.Throws<InvalidOperationException>(() => tr.InsertPoint(pts.Add(10, 0)));
            tr.InsertPoint(q); tr.InsertPoint(r); tr.InsertPoint(s); tr.InsertPoint(t);
            Assert.Throws<InvalidOperationException>(() => tr.InsertConstraint(p, notInserted));
            Assert.Throws<InvalidOperationException>(() => tr.InsertConstraint(notInserted, p));
            // through the point r
            Assert.Throws<InvalidOperationException>(() => tr.InsertConstraint(p, q));
            // crossing a constraint (without r, which (s, t) would pass through)
            var tr2 = pts.Triangulation(outer[0], outer[1], outer[2]);
            foreach (int k in new[] { p, q, s, t }) tr2.InsertPoint(k);
            tr2.InsertConstraint(s, t);
            Assert.Throws<InvalidOperationException>(() => tr2.InsertConstraint(p, q));
        }
    }
}
