using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    // ExactPlanarSimplify.ear_clip (#1158): the emptiness test of an ear takes the floating-point filter's signs first. The ears
    // chosen, and so the result, must be those of the plain exact test; only the number of exact orient calls drops.
    public class ExactPlanarSimplifyEarClipTests
    {
        // a flat fan around a hub (point 0 of the mesh, in the plane z = 0): triangles (hub, ring[i], ring[i + 1]), counter-clockwise
        // seen from +z, so the ring is the polygon to retriangulate when the hub is removed
        sealed class Fan
        {
            public readonly ExactMeshArrangement Arr;
            public readonly ExactPlanarSimplify Simplify;
            public readonly List<Index3i> Tris = new List<Index3i>();
            public readonly int[] Ring;         // point ids
            public readonly int Hub;
            public readonly int Group;

            public Fan(Vector2d hub, IList<Vector2d> ring, bool disableFilters = false)
            {
                var m = new DMesh3();
                m.AppendVertex(new Vector3d(hub.x, hub.y, 0));
                foreach (Vector2d p in ring)
                    m.AppendVertex(new Vector3d(p.x, p.y, 0));
                for (int i = 0; i < ring.Count; ++i)
                    m.AppendTriangle(0, 1 + i, 1 + (i + 1) % ring.Count);
                Arr = new ExactMeshArrangement(new List<DMesh3> { m }) { DisableFilters = disableFilters };
                Arr.Compute();
                Hub = Arr.Canonical(Arr.VertexPoints[0][0]);
                Ring = new int[ring.Count];
                for (int i = 0; i < ring.Count; ++i)
                    Ring[i] = Arr.Canonical(Arr.VertexPoints[0][1 + i]);
                var source = new List<int>();
                var flipped = new List<bool>();
                for (int t = 0; t < m.TriangleCount; ++t) {
                    Index3i f = m.GetTriangle(t);
                    Tris.Add(new Index3i(Arr.Canonical(Arr.VertexPoints[0][f.a]), Arr.Canonical(Arr.VertexPoints[0][f.b]), Arr.Canonical(Arr.VertexPoints[0][f.c])));
                    source.Add(Arr.MeshTriangles[0][t]);
                    flipped.Add(false);
                }
                Simplify = new ExactPlanarSimplify(Arr, Tris, source, flipped);
                Group = Simplify.group_of(0);
            }

            public bool[] Keep()
            {
                var keep = new bool[Arr.PointCount];
                for (int i = 0; i < keep.Length; ++i)
                    keep[i] = i != Hub;
                return keep;
            }
        }

        // the ear clipping before #1158, verbatim in its decisions: every orient is the plain exact one
        static bool ReferenceEarClip(ExactPlanarSimplify s, int g, List<int> poly, List<Index3i> add, long[] zeros)
        {
            Func<int, int, int, int> orient = (a, b, c) => {
                int o = s.orient(g, a, b, c);
                if (o == 0) zeros[0]++;
                return o;
            };
            var p = new List<int>(poly);
            while (p.Count > 3) {
                int n = p.Count;
                bool found = false;
                for (int i = 0; i < n && found == false; ++i) {
                    int a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
                    if (orient(a, b, c) <= 0)
                        continue;
                    bool empty = true;
                    for (int j = 0; j < n && empty; ++j) {
                        int x = p[j];
                        if (x == a || x == b || x == c)
                            continue;
                        if (orient(a, b, x) >= 0 && orient(b, c, x) >= 0 && orient(c, a, x) >= 0)
                            empty = false;
                    }
                    if (empty == false)
                        continue;
                    add.Add(new Index3i(a, b, c));
                    p.RemoveAt(i);
                    found = true;
                }
                if (found == false)
                    return false;
            }
            if (orient(p[0], p[1], p[2]) <= 0)
                return false;
            add.Add(new Index3i(p[0], p[1], p[2]));
            return true;
        }

        // a long straight run of N collinear points on y = 0 (the rim of a big flat region), then the two corners above its ends
        static List<Vector2d> ChainRing(int n)
        {
            var r = new List<Vector2d>();
            for (int i = 0; i < n; ++i)
                r.Add(new Vector2d(i, 0));
            r.Add(new Vector2d(n - 1, n));
            r.Add(new Vector2d(0, n));
            return r;
        }

        // the lattice points on the boundary of an a x b rectangle, counter-clockwise: four straight runs and four corners
        static List<Vector2d> RectangleRing(int a, int b)
        {
            var r = new List<Vector2d>();
            for (int i = 0; i < a; ++i) r.Add(new Vector2d(i, 0));
            for (int i = 0; i < b; ++i) r.Add(new Vector2d(a, i));
            for (int i = 0; i < a; ++i) r.Add(new Vector2d(a - i, b));
            for (int i = 0; i < b; ++i) r.Add(new Vector2d(0, b - i));
            return r;
        }

        static List<Vector2d> Rotated(List<Vector2d> r, int k)
        {
            return r.Skip(k % r.Count).Concat(r.Take(k % r.Count)).ToList();
        }

        static void AssertSameEars(Fan f, out long referenceZeros)
        {
            var expected = new List<Index3i>();
            var zeros = new long[1];
            bool okRef = ReferenceEarClip(f.Simplify, f.Group, f.Ring.ToList(), expected, zeros);
            referenceZeros = zeros[0];
            var actual = new List<Index3i>();
            bool ok = f.Simplify.ear_clip(f.Group, f.Ring.ToList(), actual);
            Assert.Equal(okRef, ok);
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; ++i)
                Assert.True(expected[i].Equals(actual[i]), $"ear {i}: {actual[i]} instead of {expected[i]}");
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (bool disable in new[] { false, true }) {
                // the rotation of the ring puts different vertices first: the ear tests meet the long chord (the chain between its
                // two end points, all on the segment that closes the ear) with the apex first
                foreach (int k in new[] { 0, 1, 5, 9, 11, 12 })
                    yield return new object[] { "chain", 12, 0, k, disable };
                foreach (var ab in new[] { (3, 3), (5, 2), (2, 7), (6, 6) })
                    foreach (int k in new[] { 0, 1, 4, 7 })
                        yield return new object[] { "rectangle", ab.Item1, ab.Item2, k, disable };
            }
        }

        static Vector2d HubOf(string kind, int a, int b)
        {
            return kind == "chain" ? new Vector2d(a / 2.0 + 0.5, a / 3.0) : new Vector2d(a / 2.0 + 0.25, b / 2.0 - 0.25);
        }

        [Theory, MemberData(nameof(Cases))]
        public void EarClip_ChoosesTheSameEars_AsThePlainExactTest(string kind, int a, int b, int rotation, bool disableFilters)
        {
            List<Vector2d> ring = kind == "chain" ? ChainRing(a) : RectangleRing(a, b);
            var f = new Fan(HubOf(kind, a, b), Rotated(ring, rotation), disableFilters);
            long zeros;
            AssertSameEars(f, out zeros);
            Assert.True(zeros > 0, "the polygon has collinear points: the plain test meets exact zeros");
        }


        // a deterministic pseudo-random star-shaped ring around the hub (0, 0): corners at multiples of 4 (reflex ones too), straight
        // runs of lattice points on some edges, and with jitter some points moved by one unit, which at scale 2^46 (coordinates near 2^52) are off their
        // lines by less than the floating-point filter can see. Null if a fan triangle is not counter-clockwise.
        static List<Vector2d> RandomRing(int seed, double scale, bool jitter)
        {
            ulong state = (ulong)seed * 0x9E3779B97F4A7C15UL + 12345;
            Func<int, int> next = (n) => {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return (int)((z ^ (z >> 31)) % (ulong)n);
            };
            int m = 5 + next(5);
            var corners = new List<(long x, long y)>();
            for (int i = 0; i < m; ++i) {
                double angle = 2 * Math.PI * (i + 0.15 + 0.7 * next(100) / 100.0) / m;
                double r = 8 + next(40);
                corners.Add((4 * (long)Math.Round(r * Math.Cos(angle) / 4), 4 * (long)Math.Round(r * Math.Sin(angle) / 4)));
            }
            var pts = new List<(long x, long y)>();
            for (int i = 0; i < m; ++i) {
                var u = corners[i]; var w = corners[(i + 1) % m];
                pts.Add(u);
                int k = new[] { 0, 0, 1, 3 }[next(4)];
                for (int j = 1; j <= k; ++j)
                    pts.Add((u.x + (w.x - u.x) * j / (k + 1), u.y + (w.y - u.y) * j / (k + 1)));
            }
            var ring = new List<Vector2d>();
            foreach (var q in pts) {
                long dx = 0, dy = 0;
                if (jitter && next(3) == 0) { dx = next(3) - 1; dy = next(3) - 1; }
                ring.Add(new Vector2d(q.x * scale + dx, q.y * scale + dy));
            }
            // (a hub at (0, 0): every fan triangle must be counter-clockwise, tested exactly)
            for (int i = 0; i < ring.Count; ++i) {
                Vector2d p = ring[i], q = ring[(i + 1) % ring.Count];
                var det = new System.Numerics.BigInteger(p.x) * new System.Numerics.BigInteger(q.y) - new System.Numerics.BigInteger(p.y) * new System.Numerics.BigInteger(q.x);
                if (det.Sign <= 0)
                    return null;
            }
            return ring;
        }

        public static IEnumerable<object[]> RandomCases()
        {
            foreach (double scale in new[] { 1.0, 70368744177664.0 /* 2^46 */ })
                foreach (bool jitter in new[] { false, true })
                    for (int seed = 0; seed < 40; ++seed)
                        yield return new object[] { seed, scale, jitter };
        }

        [Theory, MemberData(nameof(RandomCases))]
        public void EarClip_RandomStarPolygons_ChoosesTheSameEars(int seed, double scale, bool jitter)
        {
            List<Vector2d> ring = RandomRing(seed, scale, jitter);
            if (ring == null)
                return;
            // (the first ring vertices go first: the ear search order is the ring order)
            var f = new Fan(new Vector2d(0, 0), ring);
            long zeros;
            AssertSameEars(f, out zeros);
        }

        [Fact]
        public void RandomStarPolygons_AreMostlyUsable_AndReachTheOpenSigns()
        {
            // the generator gives counter-clockwise fans for most seeds, and the jittered ones at scale 2^46 have points that the filter
            // cannot place (open signs with a nonzero exact sign): the exact branch of the early exits is reached
            int usable = 0, open = 0;
            for (int seed = 0; seed < 40; ++seed) {
                List<Vector2d> ring = RandomRing(seed, 70368744177664.0, true);
                if (ring == null)
                    continue;
                usable++;
                var f = new Fan(new Vector2d(0, 0), ring);
                var add = new List<Index3i>();
                f.Simplify.ear_clip(f.Group, f.Ring.ToList(), add);
                open += (int)Math.Min(f.Simplify.EarExactOrients, 1000);
            }
            Assert.True(usable >= 10, $"{usable} usable polygons");
            Assert.True(open > 0, "no open sign");
        }


        [Theory, InlineData(false), InlineData(true)]
        public void InClosedTriangle_EqualsThePlainAnd_OnNearCollinearPoints(bool disableFilters)
        {
            // points near one line, at coordinates near 2^50, moved by -1, 0 or +1: the filter cannot place most of them against the lines
            // of the triangle, and the exact sign is zero or either sign. The test must give the AND of the three exact signs >= 0.
            ulong state = 20261119;
            Func<int, long> next = (n) => {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return (long)((z ^ (z >> 31)) % (ulong)n);
            };
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(0, 1, 2);
            const int count = 24;
            long ox = 1L << 49, oy = 1L << 48;
            long dx = (1L << 40) + next(1000), dy = (1L << 39) + next(1000);
            for (int k = 0; k < count; ++k) {
                long t = k * 3 % 7 + next(3);
                m.AppendVertex(new Vector3d(ox + t * dx + next(3) - 1, oy + t * dy + next(3) - 1, 0));
            }
            var arr = new ExactMeshArrangement(new List<DMesh3> { m }) { DisableFilters = disableFilters };
            arr.Compute();
            var ids = new int[count];
            for (int k = 0; k < count; ++k)
                ids[k] = arr.Canonical(arr.VertexPoints[0][3 + k]);
            var s = new ExactPlanarSimplify(arr, new List<Index3i> { new Index3i(arr.Canonical(arr.VertexPoints[0][0]), arr.Canonical(arr.VertexPoints[0][1]), arr.Canonical(arr.VertexPoints[0][2])) },
                new List<int> { arr.MeshTriangles[0][0] }, new List<bool> { false });
            int g = s.group_of(0);
            int inside = 0, outside = 0, tested = 0, filtered = 0, open = 0;
            for (int a = 0; a < count; ++a)
                for (int b = 0; b < count; ++b)
                    for (int c = 0; c < count; ++c) {
                        if (a == b || b == c || c == a)
                            continue;
                        for (int xi = 0; xi < count; xi += 3) {
                            int x = (xi + a + c) % count;
                            if (x == a || x == b || x == c)
                                continue;
                            bool plain = s.orient(g, ids[a], ids[b], ids[x]) >= 0 && s.orient(g, ids[b], ids[c], ids[x]) >= 0 && s.orient(g, ids[c], ids[a], ids[x]) >= 0;
                            Assert.Equal(plain, s.in_closed_triangle(g, ids[a], ids[b], ids[c], ids[x]));
                            // the filter's sign, where it has one, is the exact sign (so a negative one decides the AND)
                            var filter = arr.BeginTriangleFilter(arr.Triangles[arr.MeshTriangles[0][0]], ids[a], ids[b], ids[c]);
                            int[][] edges = { new[] { a, b }, new[] { b, c }, new[] { c, a } };
                            for (int k = 0; k < 3; ++k) {
                                int fs = arr.EdgeSign(ref filter, k, ids[x]);
                                int exact = s.orient(g, ids[edges[k][0]], ids[edges[k][1]], ids[x]);
                                if (fs != 0) { filtered++; Assert.Equal(arr.Orient(arr.Triangles[arr.MeshTriangles[0][0]], ids[edges[k][0]], ids[edges[k][1]], ids[x]), fs); }
                                else open++;
                            }
                            tested++;
                            if (plain) inside++; else outside++;
                        }
                    }
            Assert.True(tested > 1000 && inside > 20 && outside > 20, $"{tested} tests, {inside} inside, {outside} outside");
            if (disableFilters)
                Assert.Equal(0, filtered);
            else
                Assert.True(filtered > 100 && open > 100, $"{filtered} filter signs, {open} open");
        }


        [Fact]
        public void InClosedTriangle_EqualsThePlainAnd_OnTheIntersectionPointsOfAnArrangement()
        {
            // implicit points (an edge of B crossing the plane z = 0 of A) have larger approximation errors than the input points; with
            // collinear input points and crossing points the filter must use the largest error of the three points, or it decides a zero
            var a = new DMesh3();
            double[][] corner = { new[] { 0.0, 0, 0 }, new[] { 16.0, 0, 0 }, new[] { 16.0, 16, 0 }, new[] { 0.0, 16, 0 } };
            foreach (double[] c in corner) a.AppendVertex(new Vector3d(c[0], c[1], c[2]));
            a.AppendTriangle(0, 1, 2); a.AppendTriangle(0, 2, 3);
            // B: a fan of triangles from a point above the plane to points below it, the crossings of the edges at rational points
            var b = new DMesh3();
            b.AppendVertex(new Vector3d(7, 5, 3));
            int[][] below = { new[] { 1, 9, -7 }, new[] { 13, 3, -5 }, new[] { 15, 11, -3 }, new[] { 4, 14, -11 }, new[] { 2, 6, -2 }, new[] { 9, 1, -13 } };
            foreach (int[] q in below) b.AppendVertex(new Vector3d(q[0], q[1], q[2]));
            for (int i = 1; i <= below.Length; ++i)
                b.AppendTriangle(0, i, i % below.Length + 1);
            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true };
            arr.Compute();
            var pts = new SortedSet<int>();
            var sources = new List<int>(arr.MeshTriangles[0]);
            int tri0 = sources[0];
            foreach (var ot in arr.Output)
                if (sources.Contains(ot.Source)) { pts.Add(ot.A); pts.Add(ot.B); pts.Add(ot.C); }
            int[] ids = pts.ToArray();
            Assert.True(ids.Length > 10, $"{ids.Length} points in the plane");
            var s = new ExactPlanarSimplify(arr, new List<Index3i> { new Index3i(ids[0], ids[1], ids[2]) }, new List<int> { tri0 }, new List<bool> { false });
            int g = s.group_of(0);
            ulong state = 99;
            int tested = 0, open = 0;
            for (int n = 0; n < 40000; ++n) {
                int[] q = new int[4];
                for (int k = 0; k < 4; ++k) {
                    state += 0x9E3779B97F4A7C15UL;
                    ulong z = state; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    q[k] = ids[(int)((z ^ (z >> 31)) % (ulong)ids.Length)];
                }
                if (q.Distinct().Count() < 4)
                    continue;
                bool plain = s.orient(g, q[0], q[1], q[3]) >= 0 && s.orient(g, q[1], q[2], q[3]) >= 0 && s.orient(g, q[2], q[0], q[3]) >= 0;
                Assert.Equal(plain, s.in_closed_triangle(g, q[0], q[1], q[2], q[3]));
                tested++;
                if (s.orient(g, q[0], q[1], q[3]) == 0) open++;
            }
            Assert.True(tested > 10000 && open > 0, $"{tested} tests, {open} with x on a line of the triangle");
        }

        [Fact]
        public void EarClip_LongChain_NeedsFewExactOrients()
        {
            // 300 collinear points: the plain test calls the exact orient for each of them at every ear test (O(n^2) exact zeros,
            // 13 s for the 715-point hubs of Thingi10K p0084, #1158); the filter's sign of one of the other two edges excludes them
            int n = 300;
            var f = new Fan(new Vector2d(n / 2.0 + 0.5, n / 3.0), ChainRing(n));
            long zeros;
            AssertSameEars(f, out zeros);
            Assert.True(zeros > (long)n * n / 8, $"plain test: {zeros} exact zeros");
            var s = new ExactPlanarSimplify(f.Arr, f.Tris, Enumerable.Range(0, f.Tris.Count).Select(i => f.Arr.MeshTriangles[0][i]).ToList(), f.Tris.Select(t => false).ToList());
            var add = new List<Index3i>();
            Assert.True(s.ear_clip(s.group_of(0), f.Ring.ToList(), add));
            Assert.True(s.EarTests > (long)n * n / 4, $"{s.EarTests} points tested");
            Assert.True(s.EarExactOrients < 8L * n, $"{s.EarExactOrients} exact orients for {s.EarTests} tests (plain: {zeros})");
        }

        [Fact]
        public void EarClip_WithoutFilters_UsesTheExactOrientForEveryOpenSign()
        {
            // DisableFilters: EdgeSign decides nothing, every sign goes to the exact orient (the plain test's calls, up to the
            // early exit), and the ears are the same
            var f = new Fan(new Vector2d(6.5, 4), ChainRing(12), disableFilters: true);
            long zeros;
            AssertSameEars(f, out zeros);
            var s = new ExactPlanarSimplify(f.Arr, f.Tris, Enumerable.Range(0, f.Tris.Count).Select(i => f.Arr.MeshTriangles[0][i]).ToList(), f.Tris.Select(t => false).ToList());
            var add = new List<Index3i>();
            Assert.True(s.ear_clip(s.group_of(0), f.Ring.ToList(), add));
            Assert.True(s.EarExactOrients >= s.EarTests, $"{s.EarExactOrients} exact orients for {s.EarTests} tests");
        }

        [Fact]
        public void Run_RemovesTheHub_AndKeepsThePolygon()
        {
            // the whole simplification (ear clipping, then the flips) on the chain polygon: the hub goes, the ring stays the boundary
            int n = 40;
            var f = new Fan(new Vector2d(n / 2.0 + 0.5, n / 3.0), ChainRing(n));
            f.Simplify.Run(f.Keep());
            Assert.Equal(1, f.Simplify.Removed);
            List<Index3i> tris = f.Simplify.Result();
            Assert.Equal(n, tris.Count);
            // the same polygon area, all triangles counter-clockwise, every ring point used, no hub
            double area = 0;
            var used = new HashSet<int>();
            var pts = new Dictionary<int, Vector2d>();
            var ring = ChainRing(n);
            for (int i = 0; i < ring.Count; ++i)
                pts[f.Ring[i]] = ring[i];
            foreach (Index3i t in tris) {
                Vector2d a = pts[t.a], b = pts[t.b], c = pts[t.c];
                double twice = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                Assert.True(twice > 0, "counter-clockwise");
                area += twice / 2;
                used.Add(t.a); used.Add(t.b); used.Add(t.c);
            }
            Assert.Equal((n - 1.0) * n, area);
            Assert.Equal(ring.Count, used.Count);
            Assert.DoesNotContain(f.Hub, used);
            // each interior edge in two triangles, each boundary edge in one
            var edges = new Dictionary<long, int>();
            foreach (Index3i t in tris)
                for (int k = 0; k < 3; ++k) {
                    long e = ExactMeshArrangement.EdgeKey(t[k], t[(k + 1) % 3]);
                    edges[e] = edges.TryGetValue(e, out int c) ? c + 1 : 1;
                }
            Assert.Equal(ring.Count, edges.Values.Count(c => c == 1));
            Assert.True(edges.Values.All(c => c <= 2));
        }

        // ---------------------------------------------------------------- through the boolean

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }

        // the simplified union of coplanar overlapping boxes under a similarity, mapped back, and its removed vertices
        static (DMesh3 mesh, int removed) Column(Similarity s)
        {
            DMesh3 cur = Invariance.Transformed(Box(0, 0, 0, 1, 1, 1), s);
            int removed = 0;
            foreach (double z in new[] { 0.5, 1.0, 1.5 }) {
                var boolean = new ExactMeshBoolean(cur, Invariance.Transformed(Box(0, 0, z, 1, 1, z + 1), s), Ops.Union) { SimplifyFlatRegions = true };
                Assert.True(boolean.Compute(), boolean.Error);
                cur = boolean.Result;
                removed += boolean.RemovedVertices;
            }
            return (Invariance.MappedBack(cur, s), removed);
        }

        [Theory, MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SimplifiedUnion_IsInvariant_UnderSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            var reference = Column(Similarity.Identity);
            var moved = Column(s);
            // (a rotation or a UTM offset rounds the input: the boxes are no longer exactly coplanar, there is nothing to compare)
            if (s.IsExact == false)
                return;
            Assert.Equal("", MeshRobustRepair.NotCleanReason(moved.mesh));
            Assert.Equal("", Invariance.GeometricDifference(reference.mesh, moved.mesh));
            Assert.Equal(reference.removed, moved.removed);
            Assert.True(reference.removed > 0, "the column has points to remove");
        }

        [Fact]
        public void SimplifiedUnion_ReusedInstance_GivesTheSameResult()
        {
            var boolean = new ExactMeshBoolean(Box(0, 0, 0, 1, 1, 1), Box(0, 0, 0.5, 1, 1, 1.5), Ops.Union) { SimplifyFlatRegions = true };
            Assert.True(boolean.Compute(), boolean.Error);
            DMesh3 first = boolean.Result;
            int removed = boolean.RemovedVertices;
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal("", Invariance.Difference(first, boolean.Result));
            Assert.Equal(removed, boolean.RemovedVertices);
        }
    }
}
