using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using g3.Clipper2;
using geometry3Sharp.Tests;
using Xunit;
using Xunit.Abstractions;
using V = g3.Clipper2.Vendored;

namespace geometry3Sharp.Clipper2.Tests
{
    // #955: small procedural reductions of the #949 transform campaign.
    public class Clipper2FrameClampLimitTests
    {
        readonly ITestOutputHelper log;
        public Clipper2FrameClampLimitTests(ITestOutputHelper log) { this.log = log; }

        static Polygon2d Ring(params int[] xy) => new Polygon2d(Enumerable.Range(0, xy.Length / 2)
            .Select(i => new Vector2d(xy[2 * i], xy[2 * i + 1])));
        static (Polygon2d a, Polygon2d b) Scene(int kind)
        {
            if (kind == 0) return (Ring(-2,0, 2,2, -2,-1, 2,1, 2,-2, 2,0), Ring(1,-1, 2,0, 0,-2, -2,1, 2,-1, 0,2));
            if (kind == 1) return (Ring(1,-2, -1,-1, -1,0), Ring(-2,0, 0,-2, 2,-2, -1,-1, 2,2));
            if (kind == 3) return (Ring(-1,4, -4,-4, -3,-4, -3,2, 1,4, -2,-4), Ring(0,4, -3,-3, -2,2));
            return (Ring(-2,-2, -2,2, -2,1, 2,1), Ring(0,0, -2,2, -2,1));
        }
        static BigInteger TwiceArea(V.Path64 p)
        {
            BigInteger sum = 0;
            for (int i = 0; i < p.Count; ++i) {
                var a = p[i]; var b = p[(i + 1) % p.Count];
                sum += (BigInteger)a.X * b.Y - (BigInteger)a.Y * b.X;
            }
            return sum;
        }
        static IEnumerable<V.PolyPath64> Nodes(V.PolyPath64 p)
        {
            for (int i = 0; i < p.Count; ++i) {
                var child = p.Child(i); yield return child;
                foreach (var descendant in Nodes(child)) yield return descendant;
            }
        }
        static V.PolyTree64 Execute(ClipperFrame f, Polygon2d a, Polygon2d b, ClipType op, FillRule rule)
        {
            var c = new V.Clipper64 { PreserveCollinear = false }; c.AddSubject(f.ToPath(a)); c.AddClip(f.ToPath(b));
            var tree = new V.PolyTree64();
            Assert.True(c.Execute((V.ClipType)(int)op, (V.FillRule)(int)rule, tree));
            return tree;
        }
        void Print(string label, V.Path64 p) => log.WriteLine(label + ": " + string.Join(" ", p.Select(v => $"({v.X},{v.Y})")));

        static string Canonical(V.Path64 p)
        {
            var candidates = new List<string>();
            for (int start = 0; start < p.Count; ++start)
                foreach (int direction in new[] { -1, 1 })
                    candidates.Add(string.Join(" ", Enumerable.Range(0, p.Count).Select(i => {
                        var v = p[(start + direction * i + p.Count) % p.Count]; return v.X + "," + v.Y;
                    })));
            return candidates.OrderBy(x => x, StringComparer.Ordinal).First();
        }
        static V.Path64 Path(params long[] xy) => new V.Path64(Enumerable.Range(0, xy.Length / 2).Select(i => new V.Point64(xy[2*i], xy[2*i+1])));
        static void AssertInputs(int kind, V.Path64 a, V.Path64 b)
        {
            V.Path64 ea, eb;
            if (kind == 0) { ea = Path(-542019001050, -400953352814, 117301236966, 636569706332, -400446413022, -636569706332, 258873824994, 400953352814, 683591589077, -305895707739, 400446413022, 165336999297); eb = Path(306402647532, -211851942249, 400446413022, 165336999297, 212358882042, -589040883795, -683591589077, -165336999297, 542019001050, -70279354221, -353931470069, 353424530277); }
            else if (kind == 1) { ea = Path(281474976711, -562949953421, -281474976711, -281474976711, -281474976711, 0); eb = Path(-562949953421, 0, 0, -562949953421, 562949953421, -562949953421, -281474976711, -281474976711, 562949953421, 562949953421); }
            else if (kind == 2) { ea = Path(-508364, -2321666, -2186542, 1631872, -1766998, 643488, 2186541, 2321665); eb = Path(629316, 494192, -2186542, 1631872, -1766998, 643488); }
            else if (kind == 3) { ea = Path(-615145, 4163310, -223944, -5002400, 764441, -4582855, -1752825, 1347452, 1361624, 5002399, 1752825, -4163311); eb = Path(373240, 4582854, 344896, -3594471, -764441, 1766996); }
            else throw new ArgumentOutOfRangeException(nameof(kind));
            Assert.Equal(ea, a); Assert.Equal(eb, b);
        }
        static (long x, long y)[] Grid(V.Path64 p) => p.Select(q => (q.X, q.Y)).ToArray();
        static double Perimeter(V.Path64 p) => Enumerable.Range(0, p.Count).Sum(i => {
            var a = p[i]; var b = p[(i + 1) % p.Count]; return Math.Abs((double)a.X - b.X) + Math.Abs((double)a.Y - b.Y);
        });
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void IntegerEngineAreaLimits(int kind)
        {
            var (a, b) = Scene(kind); var s = Similarity.RotatedZUtmMillimetre;
            a = new Polygon2d(a.Vertices.Select(s.Apply2d)); b = new Polygon2d(b.Vertices.Select(s.Apply2d));
            var op = kind == 2 ? ClipType.Intersection : ClipType.Union;
            var rule = kind == 2 ? FillRule.Positive : FillRule.Negative;
            var r = Clipper2Polygons.BooleanPaths(op, new[] { a }, new[] { b }, new Clipper2Options { FillRule = rule });
            Assert.True(r.Ok);
            var pa = r.Frame.ToPath(a); var pb = r.Frame.ToPath(b);
            AssertInputs(kind, pa, pb);
            Print("subject", pa); Print("clip", pb);
            var tree = Execute(r.Frame, a, b, op, rule);
            foreach (var n in Nodes(tree)) Print("output", n.Polygon);
            double reference = ExactRegionArea.Area(new[] { new[] { Grid(pa) }, new[] { Grid(pb) } }, Clipper2DegenerateUnionTests.Predicate(op, rule));
            log.WriteLine($"exact reference {reference:R}; engine area {tree.Area():R}");
            if (kind == 2) {
                Assert.Empty(r.Polygons); Assert.Equal(0, tree.Count);
                Assert.True(reference > 1 && reference < Perimeter(pa) + Perimeter(pb));
            } else {
                Assert.Equal(12523546880661d, tree.Area());
                Assert.True(Math.Abs(tree.Area() - reference) > 100 * (Perimeter(pa) + Perimeter(pb)));
                Assert.Equal(tree.Area(), r.Area * r.Frame.Scale * r.Frame.Scale);
                // Independent regular-grid winding integration checks the rational-slab reference.
                var bounds = new AxisAlignedBox2d(a.Bounds); bounds.Contain(b.Bounds);
                int inside = 0, missing = 0; const int n = 160;
                for (int ix = 0; ix < n; ++ix)
                    for (int iy = 0; iy < n; ++iy) {
                        var q = new Vector2d(bounds.Min.x + bounds.Width * (ix + 0.5) / n, bounds.Min.y + bounds.Height * (iy + 0.5) / n);
                        bool expected = Oracle.Winding(a, q) < 0 || Oracle.Winding(b, q) < 0;
                        bool actual = Oracle.ResultCount(r.Polygons, q) != 0;
                        if (expected) ++inside;
                        if (expected && !actual) ++missing;
                    }
                double sampled = bounds.Width * bounds.Height * inside / (n * n) * r.Frame.Scale * r.Frame.Scale;
                Assert.True(Math.Abs(sampled - reference) < 0.02 * reference);
                Assert.True(missing > 100);

            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void EngineEmitsZeroSignedAreaRing(int kind)
        {
            Similarity s = kind == 0 ? Similarity.RotatedZ : Similarity.Millimetre;
            var (a, b) = Scene(kind);
            a = new Polygon2d(a.Vertices.Select(s.Apply2d)); b = new Polygon2d(b.Vertices.Select(s.Apply2d));
            var r = Clipper2Polygons.BooleanPaths(ClipType.Xor, new[] { a }, new[] { b }, new Clipper2Options { FillRule = FillRule.EvenOdd });
            Assert.True(r.Ok);
            var tree = Execute(r.Frame, a, b, ClipType.Xor, FillRule.EvenOdd);
            AssertInputs(kind, r.Frame.ToPath(a), r.Frame.ToPath(b));
            Print("subject", r.Frame.ToPath(a)); Print("clip", r.Frame.ToPath(b));
            foreach (var node in Nodes(tree)) Print("engine output", node.Polygon);
            var zero = Nodes(tree).Where(n => TwiceArea(n.Polygon).IsZero).ToList();
            Assert.Single(zero);
            double filledArea = ExactRegionArea.Area(new[] { new[] { Grid(zero[0].Polygon) } }, w => w[0] != 0);
            Assert.True(filledArea > 0); // opposite-winding lobes: zero signed area does not mean an empty region
            log.WriteLine($"zero-signed-area ring filled area {filledArea:R}");
            Assert.False(zero[0].IsHole);
            Print("engine zero-signed-area output", zero[0].Polygon);
            Assert.Equal(kind == 0 ? -0.5 : 0, V.Clipper.Area(zero[0].Polygon));
            // The adapter keeps the non-collinear figure eight, as documented; converting it cannot restore orientation.
            Assert.Contains(r.Polygons, p => TwiceArea(r.Frame.ToPath(p.Outer)).IsZero);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names2D), MemberType = typeof(Similarity))]
        public void ReducedCases_UnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            for (int kind = 0; kind < 4; ++kind) {
                var (a, b) = Scene(kind);
                var op = kind == 2 ? ClipType.Intersection : kind == 3 ? ClipType.Union : ClipType.Xor;
                var rule = kind == 2 ? FillRule.Positive : kind == 3 ? FillRule.Negative : FillRule.EvenOdd;
                Polygon2d Map(Polygon2d p) => new Polygon2d(p.Vertices.Select(s.Apply2d));
                var r = Clipper2Polygons.BooleanPaths(op, new[] { Map(a) }, new[] { Map(b) }, new Clipper2Options { FillRule = rule });
                Assert.True(r.Ok);
                var tree = Execute(r.Frame, Map(a), Map(b), op, rule);
                Assert.Equal(Nodes(tree).Select(n => Canonical(n.Polygon)).OrderBy(x => x, StringComparer.Ordinal),
                    r.Polygons.SelectMany(p => new[] { p.Outer }.Concat(p.Holes)).Select(p => Canonical(r.Frame.ToPath(p))).OrderBy(x => x, StringComparer.Ordinal));
                BigInteger engineArea = Nodes(tree).Aggregate(BigInteger.Zero, (sum, n) => sum + TwiceArea(n.Polygon));
                BigInteger adapterArea = r.Polygons.Aggregate(BigInteger.Zero, (sum, p) => sum + BigInteger.Abs(TwiceArea(r.Frame.ToPath(p.Outer)))
                    - p.Holes.Aggregate(BigInteger.Zero, (holes, h) => holes + BigInteger.Abs(TwiceArea(r.Frame.ToPath(h)))));
                Assert.Equal(engineArea, adapterArea);
                double reference = ExactRegionArea.Area(new[] { new[] { Grid(r.Frame.ToPath(Map(a))) }, new[] { Grid(r.Frame.ToPath(Map(b))) } }, Clipper2DegenerateUnionTests.Predicate(op, rule));
                double inputPerimeter = Perimeter(r.Frame.ToPath(Map(a))) + Perimeter(r.Frame.ToPath(Map(b)));
                if (reference > inputPerimeter) Assert.NotEmpty(r.Polygons);
                foreach (var n in Nodes(tree)) Assert.All(n.Polygon, q => Assert.Equal(q, r.Frame.ToPoint(r.Frame.ToVector2d(q))));
                foreach (var p in r.Polygons) Assert.All(p.Outer.Vertices, v => Assert.True(v.IsFinite));
                if (s.Angle2d == 0 && s.Translation.x == 0 && s.Translation.y == 0 && Clipper2DegenerateUnionTests.IsPowerOfTwo(s.Scale)) {
                    var plain = Clipper2Polygons.BooleanPaths(op, new[] { a }, new[] { b }, new Clipper2Options { FillRule = rule });
                    Assert.Equal(plain.Polygons.Count, r.Polygons.Count);
                    for (int i = 0; i < plain.Polygons.Count; ++i) {
                        Assert.Equal(plain.Polygons[i].Outer.Vertices.Select(v => s.Scale * v), r.Polygons[i].Outer.Vertices);
                        Assert.Equal(plain.Polygons[i].Holes.Count, r.Polygons[i].Holes.Count);
                        for (int j = 0; j < plain.Polygons[i].Holes.Count; ++j)
                            Assert.Equal(plain.Polygons[i].Holes[j].Vertices.Select(v => s.Scale * v), r.Polygons[i].Holes[j].Vertices);
                    }
                }
            }
        }
        // Campaign evidence rows.
        public static IEnumerable<object[]> CampaignFailures()
        {
            yield return new object[] { 0, 36, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 0, 103, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 0, 137, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 0, 160, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 0, 164, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 0, 178, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 0, 226, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 0, 230, ClipType.Union, FillRule.Negative, false };
            yield return new object[] { 0, 230, ClipType.Difference, FillRule.Negative, false };
            yield return new object[] { 0, 230, ClipType.Xor, FillRule.Negative, false };
            yield return new object[] { 0, 233, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 0, 233, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 0, 234, ClipType.Difference, FillRule.Negative, false };
            yield return new object[] { 0, 241, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 0, 248, ClipType.Difference, FillRule.NonZero, false };
            yield return new object[] { 0, 258, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 0, 262, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 0, 268, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 0, 285, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 0, 285, ClipType.Difference, FillRule.Negative, false };
            yield return new object[] { 0, 330, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 0, 352, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 0, 399, ClipType.Union, FillRule.Negative, false };
            yield return new object[] { 0, 411, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 0, 411, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 0, 436, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 0, 494, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 0, 498, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 1, 22, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 1, 22, ClipType.Difference, FillRule.Negative, false };
            yield return new object[] { 1, 44, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 1, 68, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 1, 68, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 1, 84, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 1, 137, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 139, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 154, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 1, 164, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 178, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 1, 214, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 226, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 233, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 233, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 1, 234, ClipType.Difference, FillRule.Negative, false };
            yield return new object[] { 1, 238, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 1, 241, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 248, ClipType.Difference, FillRule.NonZero, false };
            yield return new object[] { 1, 254, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 254, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 1, 268, ClipType.Difference, FillRule.EvenOdd, true };
            yield return new object[] { 1, 268, ClipType.Difference, FillRule.NonZero, true };
            yield return new object[] { 1, 268, ClipType.Xor, FillRule.EvenOdd, true };
            yield return new object[] { 1, 268, ClipType.Xor, FillRule.NonZero, true };
            yield return new object[] { 1, 294, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 330, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 1, 352, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 382, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 411, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 411, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 1, 414, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 1, 420, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 438, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 1, 481, ClipType.Intersection, FillRule.EvenOdd, false };
            yield return new object[] { 1, 481, ClipType.Intersection, FillRule.NonZero, false };
            yield return new object[] { 1, 494, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 1, 496, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 1, 498, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 2, 154, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 2, 294, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 2, 296, ClipType.Difference, FillRule.EvenOdd, true };
            yield return new object[] { 2, 296, ClipType.Difference, FillRule.NonZero, true };
            yield return new object[] { 2, 296, ClipType.Xor, FillRule.EvenOdd, true };
            yield return new object[] { 2, 296, ClipType.Xor, FillRule.NonZero, true };
            yield return new object[] { 2, 370, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 2, 382, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 2, 414, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 2, 420, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 2, 436, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 2, 454, ClipType.Intersection, FillRule.Positive, false };
            yield return new object[] { 2, 454, ClipType.Difference, FillRule.Positive, false };
            yield return new object[] { 2, 461, ClipType.Intersection, FillRule.Negative, false };
            yield return new object[] { 2, 479, ClipType.Intersection, FillRule.Negative, false };
        }
        [Theory]
        [MemberData(nameof(CampaignFailures))]
        public void CampaignFailure_IsAlreadyInIntegerEngine(int transform, int pair, ClipType op, FillRule rule, bool structural)
        {
            Similarity s = transform == 0 ? Similarity.RotatedZUtmMillimetre : transform == 1 ? Similarity.RotatedZ : Similarity.Millimetre;
            var random = new Random(17);
            Polygon2d Next(int range) {
                int count = 3 + random.Next(22);
                return new Polygon2d(Enumerable.Range(0, count).Select(_ => s.Apply2d(new Vector2d(random.Next(-range, range + 1), random.Next(-range, range + 1)))));
            }
            Polygon2d a = null, b = null;
            for (int i = 0; i <= pair; ++i) { int range = i % 2 == 0 ? 2 : 4; a = Next(range); b = Next(range); }
            var r = Clipper2Polygons.BooleanPaths(op, new[] { a }, new[] { b }, new Clipper2Options { FillRule = rule });
            Assert.True(r.Ok);
            var pa = r.Frame.ToPath(a); var pb = r.Frame.ToPath(b);
            var tree = Execute(r.Frame, a, b, op, rule);
            Print("subject", pa); Print("clip", pb);
            foreach (var node in Nodes(tree)) Print("output", node.Polygon);
            Assert.Equal(Nodes(tree).Select(n => Canonical(n.Polygon)).OrderBy(x => x, StringComparer.Ordinal),
                r.Polygons.SelectMany(p => new[] { p.Outer }.Concat(p.Holes)).Select(p => Canonical(r.Frame.ToPath(p))).OrderBy(x => x, StringComparer.Ordinal));
            if (structural) {
                Assert.Contains(Nodes(tree), n => TwiceArea(n.Polygon).IsZero);
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Oracle.CheckWellFormed(r));
            } else {
                double reference = ExactRegionArea.Area(new[] { new[] { Grid(pa) }, new[] { Grid(pb) } }, Clipper2DegenerateUnionTests.Predicate(op, rule));
                log.WriteLine($"reference {reference:R}; engine {tree.Area():R}; adapter {r.Area * r.Frame.Scale * r.Frame.Scale:R}");
                if (transform == 0 && pair == 399) {
                    Assert.Equal(23171051747518d, tree.Area());
                    Assert.True(Math.Abs(tree.Area() - reference) > 100 * (Perimeter(pa) + Perimeter(pb)));
                } else {
                    Assert.Empty(r.Polygons); Assert.Equal(0, tree.Count);
                    Assert.True(reference > 1 && reference < Perimeter(pa) + Perimeter(pb));
                }
            }
        }
    }
}
