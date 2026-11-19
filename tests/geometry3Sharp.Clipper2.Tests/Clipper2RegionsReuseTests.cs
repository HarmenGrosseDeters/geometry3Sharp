using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using g3.Clipper2;
using geometry3Sharp.Tests;
using Xunit;
using V = g3.Clipper2.Vendored;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// Clipper2Polygons.Regions uses one engine for all polygons of a call (Clear() between them, so its pools of vertices, out points
    /// and out records are reused) instead of one engine per polygon. The result must be the same bit for bit: these tests compare
    /// the shared engine with fresh engines on many polygons (holes, empty regions, a polygon without outer ring, different sizes),
    /// every operation under every similarity, and a call after a call that ran a tree Execute.
    /// </summary>
    [Collection("Clipper2RegionsHook")]   // the tests flip Clipper2Polygons.ReuseRegionEngine: not in parallel with each other
    public class Clipper2RegionsReuseTests
    {
        /// <summary> rotated hexagons of different sizes; every third has a triangular hole, every seventh a hole that is its own outer ring (an empty region) </summary>
        static List<GeneralPolygon2d> Hexagons(int g, double shift, Func<Vector2d, Vector2d> map = null)
        {
            map = map ?? (v => v);
            var list = new List<GeneralPolygon2d>();
            for (int i = 0; i < g; i++)
                for (int j = 0; j < g; j++) {
                    double r = 0.3 + 0.35 * ((i * 7 + j * 3) % 5) / 4.0;
                    var c = new Vector2d(i + shift, j + shift);
                    var v = new Vector2d[6];
                    for (int k = 0; k < 6; k++) {
                        double t = 0.26 + k * Math.PI / 3;
                        v[k] = map(new Vector2d(c.x + r * Math.Cos(t), c.y + r * Math.Sin(t)));
                    }
                    var gp = new GeneralPolygon2d(new Polygon2d(v));
                    int n = i * g + j;
                    if (n % 7 == 6)
                        gp.AddHole(new Polygon2d(v.Reverse()), false, false);
                    else if (n % 3 == 0) {
                        var h = new Vector2d[3];
                        for (int k = 0; k < 3; k++) {
                            double t = 0.26 - k * 2 * Math.PI / 3;
                            h[k] = map(new Vector2d(c.x + 0.4 * r * Math.Cos(t), c.y + 0.4 * r * Math.Sin(t)));
                        }
                        gp.AddHole(new Polygon2d(h), false, false);
                    }
                    list.Add(gp);
                    if (n % 11 == 5)
                        list.Add(new GeneralPolygon2d());   // no outer ring: skipped
                }
            return list;
        }

        static string Dump(V.Paths64 paths)
            => string.Join("|", paths.Select(p => string.Join(";", p.Select(q => q.X + "," + q.Y))));

        static string Dump(Clipper2Result r)
        {
            Assert.True(r.Ok, r.Error);
            string Ring(Polygon2d p) => string.Join(";", Enumerable.Range(0, p.VertexCount)
                .Select(i => BitConverter.DoubleToInt64Bits(p[i].x) + "," + BitConverter.DoubleToInt64Bits(p[i].y)));
            return string.Join("|", r.Polygons.Select(g => Ring(g.Outer) + "/" + string.Join("/", g.Holes.Select(Ring))))
                + "#" + string.Join("|", r.PolyLines.Select(l => string.Join(";", Enumerable.Range(0, l.VertexCount).Select(i => l[i].x + "," + l[i].y))));
        }

        static string WithReuse(bool reuse, Func<string> compute)
        {
            bool old = Clipper2Polygons.ReuseRegionEngine;
            Clipper2Polygons.ReuseRegionEngine = reuse;
            try { return compute(); }
            finally { Clipper2Polygons.ReuseRegionEngine = old; }
        }

        static IEnumerable<(string name, Func<List<GeneralPolygon2d>, List<GeneralPolygon2d>, string> run)> Operations()
        {
            var round = new Clipper2Options();
            var miter = new Clipper2Options { JoinType = JoinType.Miter, MiterLimit = 3 };
            yield return ("union", (a, b) => Dump(Clipper2Polygons.Union(a, b)));
            yield return ("intersection", (a, b) => Dump(Clipper2Polygons.Intersection(a, b)));
            yield return ("difference", (a, b) => Dump(Clipper2Polygons.Difference(a, b)));
            yield return ("xor", (a, b) => Dump(Clipper2Polygons.Xor(a, b)));
            yield return ("offset round", (a, b) => Dump(Clipper2Polygons.Offset(a, 0.05, round)));
            yield return ("offset miter", (a, b) => Dump(Clipper2Polygons.Offset(a, 0.05, miter)));
            yield return ("offset in", (a, b) => Dump(Clipper2Polygons.Offset(a, -0.03, miter)));
            yield return ("simplify", (a, b) => Dump(Clipper2Polygons.Simplify(a, 0.01)));
        }

        [Fact]
        public void Regions_SharedEngineEqualsFreshEngines_ManyPolygons()
        {
            var polygons = Hexagons(14, 0);
            Assert.True(polygons.Count > 200);
            ClipperFrame frame = Clipper2Polygons.Union(polygons).Frame;
            string fresh = Dump(Clipper2Polygons.RegionsOf(frame, polygons, false));
            string shared = Dump(Clipper2Polygons.RegionsOf(frame, polygons, true));
            Assert.Equal(fresh, shared);
            Assert.NotEqual("", shared);
            // twice on the same instances (nothing of the first call stays in the polygons)
            Assert.Equal(shared, Dump(Clipper2Polygons.RegionsOf(frame, polygons, true)));
        }

        [Fact]
        public void Regions_EmptyRegionBetweenNonEmptyOnes_LeavesNothingBehind()
        {
            // polygon 1's hole is its own outer ring: its region is empty, and polygon 2 must not see anything of 0 or 1
            var sq = new Polygon2d(new[] { new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(4, 4), new Vector2d(0, 4) });
            var big = new GeneralPolygon2d(sq);
            var empty = new GeneralPolygon2d(new Polygon2d(sq.Vertices));
            empty.AddHole(new Polygon2d(sq.Vertices.Reverse()), false, false);
            var small = new GeneralPolygon2d(new Polygon2d(new[] { new Vector2d(10, 10), new Vector2d(11, 10), new Vector2d(10, 11) }));
            var list = new List<GeneralPolygon2d> { big, empty, small, big };
            ClipperFrame frame = Clipper2Polygons.Union(list).Frame;
            V.Paths64 shared = Clipper2Polygons.RegionsOf(frame, list, true);
            Assert.Equal(Dump(Clipper2Polygons.RegionsOf(frame, list, false)), Dump(shared));
            Assert.Equal(3, shared.Count);
        }

        [Fact]
        public void Operations_SharedEngineEqualsFreshEngines()
        {
            var a = Hexagons(12, 0);
            var b = Hexagons(12, 0.4);
            foreach (var (name, run) in Operations()) {
                string fresh = WithReuse(false, () => run(a, b));
                string shared = WithReuse(true, () => run(a, b));
                Assert.True(fresh == shared, name + ": result differs between a shared engine and fresh engines");
                Assert.NotEqual("#", shared);
            }
        }

        [Fact]
        public void Operations_AfterATreeExecute_EqualFreshEngines()
        {
            // Union runs Execute with a PolyTree (the engine then has _using_polytree set): the calls after it must not see that
            var a = Hexagons(10, 0);
            var b = Hexagons(10, 0.4);
            string expected = WithReuse(false, () => Dump(Clipper2Polygons.Difference(a, b)));
            Dump(Clipper2Polygons.Union(a, b));
            Dump(Clipper2Polygons.Offset(a, 0.05));
            Assert.Equal(expected, WithReuse(true, () => Dump(Clipper2Polygons.Difference(a, b))));
            Dump(Clipper2Polygons.Union(a, b));
            Assert.Equal(expected, WithReuse(true, () => Dump(Clipper2Polygons.Difference(a, b))));
        }

        [Fact]
        public void Operations_ComputedTwice_GiveTheSameResult()
        {
            var a = Hexagons(10, 0);
            var b = Hexagons(10, 0.4);
            foreach (var (name, run) in Operations())
                Assert.True(run(a, b) == run(a, b), name);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names2D), MemberType = typeof(Similarity))]
        public void Operations_SharedEngineEqualsFreshEngines_UnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            Vector2d Map(Vector2d v) { Vector3d w = s.Apply(new Vector3d(v.x, v.y, 0)); return new Vector2d(w.x, w.y); }
            var a = Hexagons(9, 0, Map);
            var b = Hexagons(9, 0.4, Map);
            foreach (var (op, run) in Operations()) {
                string fresh = WithReuse(false, () => run(a, b));
                string shared = WithReuse(true, () => run(a, b));
                Assert.True(fresh == shared, name + " " + op);
            }
        }
    }
}
