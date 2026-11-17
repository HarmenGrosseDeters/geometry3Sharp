using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The tests that attack the arguments of the determinism proof, stage 1 (docs/design/DETERMINISM_PROOF.md section 5.4):
    /// the exact building blocks ExactTriangulation2, SnapRounding2, ExactPlanarSimplify and TriangleBoxTree. The registrations
    /// of DeterministicApis.txt name these tests (DeterminismScopeAnalyzerTests checks that they exist); the box tree and the
    /// planar simplification are attacked by the tests of their own classes that the registrations name.
    /// </summary>
    public class DeterminismStage1Tests
    {
        // ---- SnapRounding2: P3 (hash containers are enumerated through sorted unique keys; the sort of the pixels along a segment is total)

        static Vector2d Pt(int i, Random rnd) { return new Vector2d(rnd.Next(0, 64) / 8.0, rnd.Next(0, 64) / 8.0); }

        static ConstrainedMesh2Input SnapInput(int seed, int n, int segments)
        {
            var rnd = new Random(seed);
            var input = new ConstrainedMesh2Input();
            for (int i = 0; i < n; ++i)
                input.Points.Add(Pt(i, rnd));
            for (int k = 0; k < segments; ++k) {
                int a = rnd.Next(n), b = rnd.Next(n);
                if (a != b)
                    input.Segments.Add(new Mesh2Segment(a, b));
            }
            return input;
        }

        // the same input with the points renumbered and the segments in another order: what the hash containers of the snap
        // see differs in the order of every insertion
        static ConstrainedMesh2Input Permuted(ConstrainedMesh2Input input, int seed)
        {
            var rnd = new Random(seed);
            int n = input.Points.Count;
            int[] map = Enumerable.Range(0, n).OrderBy(i => rnd.Next()).ToArray();      // old index -> new index
            var result = new ConstrainedMesh2Input();
            for (int i = 0; i < n; ++i)
                result.Points.Add(default(Vector2d));
            for (int i = 0; i < n; ++i)
                result.Points[map[i]] = input.Points[i];
            foreach (Mesh2Segment s in input.Segments.OrderBy(s => rnd.Next()))
                result.Segments.Add(new Mesh2Segment(map[s.A], map[s.B]));
            return result;
        }

        static string Bits(Vector2d p) { return BitConverter.DoubleToInt64Bits(p.x) + "," + BitConverter.DoubleToInt64Bits(p.y); }

        static string EdgeKey(SnapRounding2 s, Mesh2Segment e)
        {
            string a = Bits(s.Result.Points[e.A]), b = Bits(s.Result.Points[e.B]);
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        [Theory]
        [InlineData(1, 24, 14)]
        [InlineData(2, 24, 14)]
        [InlineData(3, 40, 30)]
        [InlineData(4, 40, 30)]
        [InlineData(5, 12, 20)]
        [InlineData(6, 60, 45)]
        public void SnapRounding2_InsertionOrder_DoesNotChangeTheHotPixels(int seed, int n, int segments)
        {
            ConstrainedMesh2Input input = SnapInput(seed, n, segments);
            SnapRounding2 reference = SnapRounding2.Apply(input, 0.5);
            // the hot pixels the snap added after the input points come in the order of their sorted pixels (the keys of a
            // dictionary, sorted by OrderBy), not in the order of the dictionary's enumeration, which is unspecified
            string[] extra = reference.Result.Points.Skip(reference.InputCount).Select(Bits).ToArray();
            string[] edges = reference.Result.Segments.Select(e => EdgeKey(reference, e)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            for (int variant = 1; variant <= 4; ++variant) {
                SnapRounding2 other = SnapRounding2.Apply(Permuted(input, 100 * seed + variant), 0.5);
                Assert.Equal(extra, other.Result.Points.Skip(other.InputCount).Select(Bits).ToArray());
                Assert.Equal(edges, other.Result.Segments.Select(e => EdgeKey(other, e)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray());
                Assert.Equal(reference.Crossings, other.Crossings);
            }
            // the same input again gives the same bits (no state left behind), also from two threads at once
            SnapRounding2 again = SnapRounding2.Apply(input, 0.5);
            Assert.Equal(reference.Result.Points.Select(Bits), again.Result.Points.Select(Bits));
            Assert.Equal(reference.Result.Segments.Select(e => e.A + "-" + e.B), again.Result.Segments.Select(e => e.A + "-" + e.B));
            var results = new SnapRounding2[4];
            System.Threading.Tasks.Parallel.For(0, results.Length, i => results[i] = SnapRounding2.Apply(input, 0.5));
            foreach (SnapRounding2 r in results)
                Assert.Equal(reference.Result.Points.Select(Bits), r.Result.Points.Select(Bits));
        }

        static (((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) t, (long, long) p) Met(long en, long ed, long xn, long xd, long px, long py)
        {
            return (((new BigInteger(en), new BigInteger(ed)), (new BigInteger(xn), new BigInteger(xd))), (px, py));
        }

        [Fact]
        public void SnapRounding2_RouteOrder_IsATotalOrder()
        {
            // elements built from small fractions (equal values in different forms, 1/2 and 2/4) and a few pixels, so that
            // equal entries, equal exits and equal pairs of both occur often
            var all = new List<(((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) t, (long, long) p)>();
            foreach (long en in new long[] { 0, 1, 2 })
                foreach (long ed in new long[] { 2, 4 })
                    foreach (long xn in new long[] { 2, 3 })
                        foreach (long xd in new long[] { 4, 2 })
                            foreach (long px in new long[] { -1, 0 })
                                foreach (long py in new long[] { 0, 2 })
                                    all.Add(Met(en, ed, xn, xd, px, py));
            // zero exactly for the same entry value, exit value and pixel; antisymmetric
            foreach (var u in all)
                foreach (var v in all) {
                    int c = SnapRounding2.CompareMet(u, v);
                    Assert.Equal(-Math.Sign(c), Math.Sign(SnapRounding2.CompareMet(v, u)));
                    bool same = u.t.entry.num * v.t.entry.den == v.t.entry.num * u.t.entry.den
                        && u.t.exit.num * v.t.exit.den == v.t.exit.num * u.t.exit.den && u.p.Equals(v.p);
                    Assert.Equal(same, c == 0);
                }
            // transitive: every order of the list sorts to the same sequence (equal elements are identical in value, so
            // compare the sorted values; the unstable sort cannot put two different pixels in either order)
            string Key(((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) t, (long, long) p) =>
                (double)t.entry.num / (double)t.entry.den + "/" + (double)t.exit.num / (double)t.exit.den + "/" + p.Item1 + "," + p.Item2;
            string[] expected = null;
            for (int seed = 1; seed <= 6; ++seed) {
                var rnd = new Random(seed);
                var list = all.OrderBy(x => rnd.Next()).ToList();
                list.Sort(SnapRounding2.CompareMet);
                for (int i = 1; i < list.Count; ++i)
                    Assert.True(SnapRounding2.CompareMet(list[i - 1], list[i]) <= 0);
                string[] keys = list.Select(x => Key(x.t, x.p)).ToArray();
                if (expected == null) expected = keys;
                else Assert.Equal(expected, keys);
            }
        }

        // ---- ExactTriangulation2: P4 (the callbacks give signs that the points define) and the capacity hint

        sealed class Cloud
        {
            public readonly List<(long x, long y)> P = new List<(long, long)>();
            public int Calls;

            // the signs from long arithmetic
            public int Orient(int a, int b, int c)
            {
                ++Calls;
                return Math.Sign((P[b].x - P[a].x) * (P[c].y - P[a].y) - (P[b].y - P[a].y) * (P[c].x - P[a].x));
            }

            public int InCircle(int a, int b, int c, int d)
            {
                ++Calls;
                BigInteger[] r = new[] { a, b, c }.SelectMany(i => {
                    BigInteger x = P[i].x - P[d].x, y = P[i].y - P[d].y;
                    return new[] { x, y, x * x + y * y };
                }).ToArray();
                return (r[0] * (r[4] * r[8] - r[5] * r[7]) - r[1] * (r[3] * r[8] - r[5] * r[6]) + r[2] * (r[3] * r[7] - r[4] * r[6])).Sign;
            }

            // the same signs from other formulas: the orientation as the sign of a BigInteger area about the point c (not a),
            // the incircle about the point a (not d) with the rows in another order, and with a pause in between
            public int Orient2(int a, int b, int c)
            {
                BigInteger s = (BigInteger)(P[a].x - P[c].x) * (P[b].y - P[c].y) - (BigInteger)(P[a].y - P[c].y) * (P[b].x - P[c].x);
                return s.Sign;
            }

            public int InCircle2(int a, int b, int c, int d)
            {
                // incircle(a, b, c, d) = incircle(b, c, a, d) (a cyclic shift keeps the sign), about the origin of a
                var rows = new[] { b, c, d }.Select(i => {
                    BigInteger x = P[i].x - P[a].x, y = P[i].y - P[a].y;
                    return new[] { x, y, x * x + y * y };
                }).ToArray();
                BigInteger det = rows[0][0] * (rows[1][1] * rows[2][2] - rows[1][2] * rows[2][1])
                    - rows[0][1] * (rows[1][0] * rows[2][2] - rows[1][2] * rows[2][0])
                    + rows[0][2] * (rows[1][0] * rows[2][1] - rows[1][1] * rows[2][0]);
                return -det.Sign;     // the rows (b - a, c - a, d - a) give the opposite sign to the rows (a - d, b - d, c - d) of the lifted incircle determinant
            }
        }

        static (Cloud, int[], List<int>, List<(int, int)>) Cloud2(int seed, int n, int count, int constraintCount)
        {
            var rnd = new Random(seed);
            var c = new Cloud();
            int[] outer = new int[3];
            c.P.Add((0, 0)); outer[0] = 0;
            c.P.Add((n, 0)); outer[1] = 1;
            c.P.Add((0, n)); outer[2] = 2;
            var seen = new HashSet<(long, long)> { (0, 0), (n, 0), (0, n) };
            var inner = new List<int>();
            while (inner.Count < count) {
                long x = rnd.Next(n + 1), y = rnd.Next(n + 1 - (int)x);
                if (seen.Add((x, y))) { c.P.Add((x, y)); inner.Add(c.P.Count - 1); }
            }
            // constraints: the segments between consecutive inner points of a fixed order that cross nothing (checked by the exact
            // triangulation itself: a crossing constraint would throw, so only take those that pass)
            var constraints = new List<(int, int)>();
            for (int k = 0; k + 1 < inner.Count && constraints.Count < constraintCount; k += 2)
                constraints.Add((inner[k], inner[k + 1]));
            return (c, outer, inner, constraints);
        }

        static List<Index3i> Triangulate(Cloud c, int[] outer, List<int> inner, List<(int, int)> constraints, Func<int, int, int, int> orient,
            Func<int, int, int, int, int> incircle, int hint)
        {
            var tr = new ExactTriangulation2(outer[0], outer[1], outer[2], orient, incircle, hint);
            foreach (int p in inner)
                tr.InsertPoint(p);
            foreach (var (u, v) in constraints) {
                try { tr.InsertConstraint(u, v); }
                catch (InvalidOperationException) { }      // a crossing or a point on it (the random constraints are not screened): the same on every run
            }
            tr.MakeDelaunay();
            return tr.Triangles().ToList();
        }

        static string Show(List<Index3i> t) { return string.Join(";", t.Select(x => x.a + "," + x.b + "," + x.c)); }

        [Theory]
        [InlineData(1, 12, 20, 4)]
        [InlineData(2, 30, 80, 10)]
        [InlineData(3, 8, 25, 6)]
        [InlineData(4, 1000, 200, 12)]
        public void ExactTriangulation2_EquivalentCallbacks_GiveTheSameTriangulation(int seed, int n, int count, int constraintCount)
        {
            var (c, outer, inner, constraints) = Cloud2(seed, n, count, constraintCount);
            string reference = Show(Triangulate(c, outer, inner, constraints, c.Orient, c.InCircle, 0));
            // other formulas for the same signs, and a second instance on the same input: the same triangle list, in the same order
            Assert.Equal(reference, Show(Triangulate(c, outer, inner, constraints, c.Orient2, c.InCircle2, 0)));
            Assert.Equal(reference, Show(Triangulate(c, outer, inner, constraints, c.Orient, c.InCircle, 0)));
            // callbacks that count and delay their answer: no state of the triangulation depends on when or how often they run
            int ticks = 0;
            Func<int, int, int, int> slowOrient = (a, b, d) => { ticks++; System.Threading.Thread.Yield(); return c.Orient(a, b, d); };
            Assert.Equal(reference, Show(Triangulate(c, outer, inner, constraints, slowOrient, c.InCircle, 0)));
            Assert.True(ticks > 0);
        }

        [Theory]
        [InlineData(1, 12, 20, 4)]
        [InlineData(4, 1000, 200, 12)]
        public void ExactTriangulation2_CapacityHint_ChangesNothing(int seed, int n, int count, int constraintCount)
        {
            var (c, outer, inner, constraints) = Cloud2(seed, n, count, constraintCount);
            string reference = Show(Triangulate(c, outer, inner, constraints, c.Orient, c.InCircle, 0));
            // the hint is a size only: none, the exact number of points, too few and far too many
            foreach (int hint in new[] { count, 3, 100 * count + 7 })
                Assert.Equal(reference, Show(Triangulate(c, outer, inner, constraints, c.Orient, c.InCircle, hint)));
        }
    }
}
