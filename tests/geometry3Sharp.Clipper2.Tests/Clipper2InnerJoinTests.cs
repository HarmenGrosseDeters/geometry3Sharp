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
    /// #1169: a concave join whose two offset edges intersect inside both gets one point (<c>SinglePointInnerJoin</c>) instead
    /// of the 3-point detour. The detour made the offset of a dense smooth ring quadratic (about delta / spacing edges active
    /// in the sweep at once; 100k vertices: 20 s). Checked by counts (the raw offset's vertices), not by time, and against the
    /// 3-point path by region (area of the symmetric difference, vertex distance, orientation structure).
    /// </summary>
    public class Clipper2InnerJoinTests
    {
        const double Grid = 1 << 24;

        static V.Path64 Star(int n, double r, double amplitude, int waves, double phase = 0)
        {
            var p = new V.Path64(n);
            for (int i = 0; i < n; i++) {
                double t = 2 * Math.PI * i / n, rr = r * (1 + amplitude * Math.Sin(waves * t + phase));
                p.Add(new V.Point64((long)Math.Round(rr * Math.Cos(t)), (long)Math.Round(rr * Math.Sin(t))));
            }
            return p;
        }

        static V.Paths64 Offset(V.Paths64 paths, V.JoinType join, double delta, bool inner, out int raw, double arcTol = 0, V.EndType end = V.EndType.Polygon)
        {
            var o = new V.ClipperOffset(2.0, arcTol) { SinglePointInnerJoin = inner };
            o.AddPaths(paths, join, end);
            var sol = new V.Paths64();
            o.Execute(delta, sol);
            raw = o.RawVertexCount;
            return sol;
        }

        static double Dist(V.Point64 v, V.Paths64 b)
        {
            double best = double.MaxValue;
            foreach (V.Path64 q in b)
                for (int i = 0; i < q.Count; i++) {
                    V.Point64 p1 = q[i], p2 = q[(i + 1) % q.Count];
                    double dx = p2.X - p1.X, dy = p2.Y - p1.Y, l = dx * dx + dy * dy;
                    double t = l == 0 ? 0 : Math.Min(1, Math.Max(0, ((v.X - p1.X) * dx + (v.Y - p1.Y) * dy) / l));
                    double ex = p1.X + t * dx - v.X, ey = p1.Y + t * dy - v.Y;
                    best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
                }
            return best;
        }

        static double Perimeter(V.Paths64 paths)
        {
            double s = 0;
            foreach (V.Path64 q in paths)
                for (int i = 0; i < q.Count; i++)
                    s += Math.Sqrt(V.Clipper.DistanceSqr(q[i], q[(i + 1) % q.Count]));
            return s;
        }

        /// <summary> the single-point result is the 3-point region: same ring structure, area XOR within the rounding, vertices within 3 grid units (two roundings of up to one unit per coordinate) </summary>
        static void AssertSameRegion(V.Paths64 old, V.Paths64 inner, string what)
        {
            Assert.True(old.Count == inner.Count, what + ": " + old.Count + " rings against " + inner.Count);
            Assert.Equal(old.Select(q => V.Clipper.Area(q) > 0).OrderBy(x => x), inner.Select(q => V.Clipper.Area(q) > 0).OrderBy(x => x));
            var xor = new V.Paths64();
            var c = new V.Clipper64();
            c.AddSubject(old);
            c.AddClip(inner);
            c.Execute(V.ClipType.Xor, V.FillRule.NonZero, xor);
            double xorArea = Math.Abs(xor.Sum(q => V.Clipper.Area(q)));   // outer rings positive, holes negative
            Assert.True(xorArea <= 2 * Perimeter(old) + 4, what + ": area of the difference " + xorArea + " against perimeter " + Perimeter(old));
            // 3 grid units, more at the tip of a thin spike of the result (angle alpha between its edges): both results round the
            // two edges of the tip, and the tip moves by about 1 / sin(alpha / 2) times that along its axis
            double worst = 0;
            foreach (var (from, to) in new[] { (inner, old), (old, inner) })
                foreach (V.Path64 q in from)
                    for (int i = 0; i < q.Count; i++) {
                        V.Point64 p0 = q[(i + q.Count - 1) % q.Count], p1 = q[i], p2 = q[(i + 1) % q.Count];
                        double ax = p0.X - p1.X, ay = p0.Y - p1.Y, bx = p2.X - p1.X, by = p2.Y - p1.Y;
                        double cos = (ax * bx + ay * by) / Math.Sqrt((ax * ax + ay * ay) * (bx * bx + by * by));
                        double allowed = 3.0 / Math.Max(Math.Sqrt(Math.Max(0, (1 - cos) / 2)), 0.01);
                        worst = Math.Max(worst, Dist(p1, to) / allowed);
                    }
            Assert.True(worst <= 1.0, what + ": a vertex is " + worst + " times the allowed distance (3 grid units, more at a thin tip) off the 3-point result");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DenseRing_GetsOnePointPerConcaveVertex(bool round)
        {
            V.JoinType join = round ? V.JoinType.Round : V.JoinType.Miter;
            // spacing about 1e-3 of the radius, delta 0.04 r: the offset edges of a concave vertex always meet inside both
            // (no wall clock: the raw offset of the 3-point path has 1.8 to 2.4 n vertices, with the single point at most 1.7 n,
            // which is what keeps the sweep short)
            int n = 4000;
            V.Paths64 ring = new V.Paths64 { Star(n, Grid, 0.1, 12) };
            double delta = 0.04 * Grid;
            V.Paths64 inner = Offset(ring, join, delta, true, out int rawInner, delta / 1000);
            V.Paths64 old = Offset(ring, join, delta, false, out int rawOld, delta / 1000);
            Assert.InRange(rawOld, (int)(1.75 * n), 3 * n);
            Assert.True(rawInner <= 1.7 * n, "raw offset has " + rawInner + " vertices for " + n);
            Assert.True(rawInner < 0.8 * rawOld);
            AssertSameRegion(old, inner, join.ToString());
        }

        [Fact]
        public void Raw_VerticesPerInputVertex_DoNotGrowWithDensity()
        {
            // the superlinear cost came from the number of edges active at once; the count that causes it, per input vertex,
            // is constant for the single-point join
            var perVertex = new List<double>();
            foreach (int n in new[] { 1000, 2000, 4000 }) {
                Offset(new V.Paths64 { Star(n, Grid, 0.1, 12) }, V.JoinType.Miter, 0.04 * Grid, true, out int raw);
                perVertex.Add((double)raw / n);
            }
            Assert.All(perVertex, x => Assert.InRange(x, 1.0, 1.1));
        }

        [Fact]
        public void ThreePointJoin_IsKept_WhereTheOffsetEdgesDoNotMeetInsideBoth()
        {
            // a narrow notch with edges shorter than delta: the offset edges of the concave vertices overlap, so the detour stays
            V.Path64 notch = new V.Path64 {
                new V.Point64(0, 0), new V.Point64(1000, 0), new V.Point64(1000, 1000), new V.Point64(520, 1000),
                new V.Point64(500, 100), new V.Point64(480, 1000), new V.Point64(0, 1000) };
            foreach (V.JoinType join in new[] { V.JoinType.Miter, V.JoinType.Round, V.JoinType.Bevel, V.JoinType.Square }) {
                V.Paths64 a = Offset(new V.Paths64 { notch }, join, 80, true, out int rawInner);
                V.Paths64 b = Offset(new V.Paths64 { notch }, join, 80, false, out int rawOld);
                Assert.Equal(rawOld, rawInner);
                Assert.Equal(b.Select(q => q.ToArray()), a.Select(q => q.ToArray()));
            }
        }

        [Fact]
        public void InnerJoin_MatchesTheThreePointRegion_OnRandomAndSharpRings()
        {
            var rnd = new Random(1169);
            int checkedRings = 0, withSingle = 0;
            for (int trial = 0; trial < 60; trial++) {
                int n = 3 + rnd.Next(trial < 30 ? 14 : 300);
                double a = trial % 3 == 0 ? 0.9 : 0.15, r = 100000 + rnd.Next(100000);
                V.Path64 ring = new V.Path64();
                for (int i = 0; i < n; i++) {
                    double t = 2 * Math.PI * (i + (trial % 2 == 0 ? 0 : 0.4 * rnd.NextDouble())) / n;   // uneven spacing
                    double rr = r * (1 + a * (2 * rnd.NextDouble() - 1) * (trial % 4 == 3 ? 1 : 0.2) + (trial % 4 == 3 ? 0 : a * Math.Sin(5 * t)));
                    ring.Add(new V.Point64((long)Math.Round(rr * Math.Cos(t)), (long)Math.Round(rr * Math.Sin(t))));
                }
                if (V.Clipper.Area(ring) == 0) continue;
                foreach (V.JoinType join in new[] { V.JoinType.Round, V.JoinType.Miter, V.JoinType.Square, V.JoinType.Bevel })
                    foreach (double delta in new[] { 30.0, 3000, -3000, 25000, -25000 }) {
                        var paths = new V.Paths64 { ring };
                        V.Paths64 old = Offset(paths, join, delta, false, out int rawOld, 20);
                        V.Paths64 inner = Offset(paths, join, delta, true, out int rawInner, 20);
                        AssertSameRegion(old, inner, $"trial {trial} n={n} {join} delta={delta}");
                        checkedRings++;
                        if (rawInner < rawOld) withSingle++;
                    }
            }
            Assert.True(checkedRings > 300);
            Assert.True(withSingle > 50, "the single-point join was taken in only " + withSingle + " cases");
        }

        [Fact]
        public void InnerJoin_OpenPaths_BothSidesWithUnevenEdges()
        {
            // the way back of an open path walks the vertices backwards: the next vertex of a join is the previous one of the way there
            var rnd = new Random(1170);
            int single = 0;
            for (int trial = 0; trial < 40; trial++) {
                var line = new V.Path64();
                double x = 0, y = 0, dir = 0;
                for (int i = 0; i < 6 + rnd.Next(40); i++) {
                    double len = rnd.Next(4) == 0 ? 300 + rnd.Next(300) : 20000 + rnd.Next(30000);
                    dir += (rnd.NextDouble() - 0.5) * (trial % 2 == 0 ? 1.0 : 0.3);
                    x += len * Math.Cos(dir); y += len * Math.Sin(dir);
                    line.Add(new V.Point64((long)Math.Round(x), (long)Math.Round(y)));
                }
                foreach (V.EndType end in new[] { V.EndType.Butt, V.EndType.Round, V.EndType.Joined })
                    foreach (V.JoinType join in new[] { V.JoinType.Round, V.JoinType.Miter })
                        foreach (double delta in new[] { 100.0, 4000, 12000 }) {
                            var paths = new V.Paths64 { line };
                            V.Paths64 old = Offset(paths, join, delta, false, out int rawOld, 20, end);
                            V.Paths64 inner = Offset(paths, join, delta, true, out int rawInner, 20, end);
                            AssertSameRegion(old, inner, $"trial {trial} {end} {join} {delta}");
                            if (rawInner < rawOld) single++;
                        }
            }
            Assert.True(single > 100, "the single-point join was taken in only " + single + " cases");
        }

        [Fact]
        public void InnerJoin_NearCollinearAndTouchingRings()
        {
            // near collinear (turn angles of 1e-6), a ring touching itself at a vertex, and a pinched ring
            var rings = new List<V.Path64>();
            var wavy = new V.Path64();
            for (int i = 0; i < 400; i++) wavy.Add(new V.Point64(i * 5000, (long)(2 + 3 * Math.Sin(i * 0.01))));
            for (int i = 400; i > 0; i--) wavy.Add(new V.Point64(i * 5000, 60000));
            rings.Add(wavy);
            rings.Add(new V.Path64 { new V.Point64(0, 0), new V.Point64(4000, 0), new V.Point64(2000, 2000), new V.Point64(4000, 4000),
                new V.Point64(0, 4000), new V.Point64(2000, 2000) });
            rings.Add(new V.Path64 { new V.Point64(0, 0), new V.Point64(4000, 0), new V.Point64(4000, 1990), new V.Point64(2000, 2000),
                new V.Point64(4000, 2010), new V.Point64(4000, 4000), new V.Point64(0, 4000) });
            foreach (V.Path64 ring in rings)
                foreach (V.JoinType join in new[] { V.JoinType.Round, V.JoinType.Miter })
                    foreach (double delta in new[] { 100.0, -100, 1500, -1500 }) {
                        var paths = new V.Paths64 { ring };
                        AssertSameRegion(Offset(paths, join, delta, false, out _, 10), Offset(paths, join, delta, true, out _, 10), $"{join} {delta}");
                    }
        }

        [Fact]
        public void SelfCrossingPolylines_ThroughTheAdapter_GiveTheSameArea()
        {
            // the adapter resolves overlapping polygons into disjoint regions before the offset, but the offset of a polyline that
            // crosses itself overlaps itself: the area still agrees with the 3-point join to the rounding (the raw rings of
            // the vendored class with self-intersections do not, see the README of the vendored sources)
            var rnd = new Random(5);
            for (int trial = 0; trial < 40; trial++) {
                var pts = new Vector2d[5 + rnd.Next(60)];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2d(rnd.NextDouble() * 10, rnd.NextDouble() * 10);
                foreach (JoinType join in new[] { JoinType.Round, JoinType.Miter })
                    foreach (double d in new[] { 0.05, 0.5, 2.0 }) {
                        var line = new[] { new PolyLine2d(pts) };
                        Clipper2Result on = Clipper2Polygons.OffsetPolyLines(line, d, new Clipper2Options { JoinType = join });
                        Clipper2Result off = Clipper2Polygons.OffsetPolyLines(line, d, new Clipper2Options { JoinType = join, SinglePointInnerJoin = false });
                        Assert.True(on.Ok && off.Ok);
                        Assert.InRange(on.Area, off.Area * (1 - 1e-4), off.Area * (1 + 1e-4));
                    }
            }
        }

        [Fact]
        public void Option_TurnsTheSinglePointJoinOff_AndItIsOnByDefault()
        {
            Polygon2d ring = new Polygon2d(Enumerable.Range(0, 1200).Select(i => {
                double t = 2 * Math.PI * i / 1200, rr = 10 * (1 + 0.1 * Math.Sin(12 * t));
                return new Vector2d(rr * Math.Cos(t), rr * Math.Sin(t));
            }));
            var input = new[] { new GeneralPolygon2d(ring) };
            Assert.True(new Clipper2Options().SinglePointInnerJoin);
            Clipper2Result on = Clipper2Polygons.Offset(input, 0.4, new Clipper2Options { JoinType = JoinType.Miter });
            Clipper2Result def = Clipper2Polygons.Offset(input, 0.4, new Clipper2Options { JoinType = JoinType.Miter, SinglePointInnerJoin = true });
            Clipper2Result off = Clipper2Polygons.Offset(input, 0.4, new Clipper2Options { JoinType = JoinType.Miter, SinglePointInnerJoin = false });
            Assert.True(on.Ok && off.Ok);
            Assert.Equal(def.Polygons[0].Outer.Vertices, on.Polygons[0].Outer.Vertices);
            Assert.NotEqual(off.Polygons[0].Outer.Vertices, on.Polygons[0].Outer.Vertices);   // the rounding of the miter points differs
            Assert.InRange(on.Area, off.Area * (1 - 1e-9), off.Area * (1 + 1e-9));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names2D), MemberType = typeof(Similarity))]
        public void DenseOffset_UnderSimilarity(string name)
        {
            // the addon takes the single-point join: a power-of-two scale gives the same grid integers, so the same result bit for
            // bit; any other similarity another grid, so the claim is the area
            Similarity s = Similarity.ByName(name);
            Polygon2d ring = new Polygon2d(Enumerable.Range(0, 1500).Select(i => {
                double t = 2 * Math.PI * i / 1500, rr = 10 * (1 + 0.1 * Math.Sin(12 * t));
                return new Vector2d(rr * Math.Cos(t), rr * Math.Sin(t));
            }));
            Polygon2d Map(Polygon2d p) => new Polygon2d(p.Vertices.Select(s.Apply2d));
            foreach (JoinType join in new[] { JoinType.Round, JoinType.Miter }) {
                var options = new Clipper2Options { JoinType = join };
                Clipper2Result plain = Clipper2Polygons.Offset(new[] { new GeneralPolygon2d(ring) }, 0.4, options);
                Clipper2Result mapped = Clipper2Polygons.Offset(new[] { new GeneralPolygon2d(Map(ring)) }, s.Scale * 0.4, options);
                Assert.True(plain.Ok);
                Assert.True(mapped.Ok);
                Assert.Equal(1, mapped.Polygons.Count);
                Assert.InRange(mapped.Area, s.Scale * s.Scale * plain.Area * (1 - 1e-6), s.Scale * s.Scale * plain.Area * (1 + 1e-6));
                if (s.Angle2d == 0 && s.Translation.x == 0 && s.Translation.y == 0 && Math.Abs(Math.Log(s.Scale, 2) - Math.Round(Math.Log(s.Scale, 2))) < 1e-12)
                    Assert.Equal(plain.Polygons[0].Outer.Vertices.Select(v => s.Scale * v), mapped.Polygons[0].Outer.Vertices);
            }
        }
    }
}
