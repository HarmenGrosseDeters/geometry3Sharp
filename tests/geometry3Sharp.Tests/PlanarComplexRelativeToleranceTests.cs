using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// FindSolidsOptions.SimplifyRelativeTolerance (#628): the absolute SimplifyDeviationTolerance of 0.1 of the default options
    /// simplifies close loops past each other and loses their nesting (PlanarComplexNestingProbeTests, skipped by design); the
    /// optional tolerance relative to each loop's bounding box keeps it. Off by default: the results then are those of before.
    /// </summary>
    public class PlanarComplexRelativeToleranceTests
    {
        // the cases of the probe (PlanarComplexNestingProbeTests): two near-concentric circles over Similarity.All(); the relative
        // tolerance is a hundredth of the gap, so the simplification stays well inside the gap
        public static IEnumerable<object[]> Cases()
        {
            foreach (double gap in new[] { 1e-1, 1e-2, 1e-3, 1e-6 })
            foreach (bool offset in new[] { false, true })
            foreach (Similarity s in Similarity.All())
                yield return new object[] { gap, offset, s.Name };
        }

        static PlanarComplex Circles(double gap, bool offset, Similarity s, out PlanarComplex.Element outer, out PlanarComplex.Element inner)
        {
            Vector3d origin = s.Apply(Vector3d.Zero);
            Vector3d ex = s.ApplyDirection(Vector3d.AxisX), ey = s.ApplyDirection(Vector3d.AxisY);
            var center = new Vector2d(origin.Dot(ex), origin.Dot(ey));
            double radius = s.Scale;
            var complex = new PlanarComplex { DistanceAccuracy = 0.1 * radius };
            outer = complex.Add(new Circle2d(center, radius * (1 + gap)) { IsReversed = true });
            inner = complex.Add(new Circle2d(center + new Vector2d(offset ? 0.9 * gap * radius : 0, 0), radius));
            return complex;
        }

        static PlanarComplex.FindSolidsOptions Relative(double rel)
        {
            var o = PlanarComplex.FindSolidsOptions.Default;
            o.SimplifyRelativeTolerance = rel;
            return o;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void CloseCircles_RelativeToleranceKeepsTheNesting_OverEverySimilarity(double gap, bool offset, string similarity)
        {
            // (the gap of 1e-6 at the rotated UTM millimetre similarity is about 1 ulp of the coordinates: not representable, #628)
            if (gap == 1e-6 && similarity == Similarity.RotatedUtmMillimetre.Name)
                return;
            Similarity s = Similarity.ByName(similarity);
            PlanarComplex complex = Circles(gap, offset, s, out var outer, out var inner);
            var result = complex.FindSolidRegions(Relative(gap / 100));
            var polygon = Assert.Single(result.Polygons);
            Assert.Single(polygon.Holes);
            var sources = Assert.Single(result.PolygonsSources);
            Assert.Same(outer, sources.Outer);
            Assert.Same(inner, Assert.Single(sources.Holes));
            double area = result.HolesArea / (s.Scale * s.Scale);
            Assert.True(Math.Abs(area - Math.PI) < 0.006, $"normalized hole area {area:G17} for {similarity}");
        }

        [Fact]
        public void CloseCircles_Exact2kScales_SameVerticesBitForBit()
        {
            // the tolerance is relative to the loop, so a 2^k scale gives the same simplification: the vertices are those of the unit
            // case times the scale, bit for bit
            var unit = Circles(1e-2, true, Similarity.Identity, out _, out _).FindSolidRegions(Relative(1e-4));
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact && x.Name != Similarity.Identity.Name)) {
                var r = Circles(1e-2, true, s, out _, out _).FindSolidRegions(Relative(1e-4));
                Assert.Equal(unit.Polygons[0].Outer.VertexCount, r.Polygons[0].Outer.VertexCount);
                Assert.Equal(unit.Polygons[0].Holes[0].VertexCount, r.Polygons[0].Holes[0].VertexCount);
                Assert.True(unit.Polygons[0].Holes[0].Vertices.Zip(r.Polygons[0].Holes[0].Vertices, (u, v) => v == u * s.Scale).All(x => x), s.Name);
                Assert.True(unit.Polygons[0].Outer.Vertices.Zip(r.Polygons[0].Outer.Vertices, (u, v) => v == u * s.Scale).All(x => x), s.Name);
            }
        }

        [Fact]
        public void CloseCircles_DefaultOptions_DocumentTheUnchangedDefault_TwoSolidsNoHole()
        {
            // NOT the expected-correct result: it pins what the default options do (#628), the absolute tolerance 0.1 simplifies the
            // circles of radius 1.01 and 1 (gap 0.01) past each other. The correct nesting is the relative tolerance's job above.
            var complex = new PlanarComplex { DistanceAccuracy = 0.1 };
            complex.Add(new Circle2d(Vector2d.Zero, 1.01) { IsReversed = true });
            complex.Add(new Circle2d(Vector2d.Zero, 1.0));
            var result = complex.FindSolidRegions(PlanarComplex.FindSolidsOptions.Default);
            Assert.Equal(2, result.Polygons.Count);
            Assert.All(result.Polygons, p => Assert.Empty(p.Holes));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.NegativeInfinity)]
        public void ZeroNegativeAndNaN_AreOff_ResultsBitIdenticalToTheOldOverload(double rel)
        {
            var complex = new PlanarComplex { DistanceAccuracy = 0.1 };
            complex.Add(new Circle2d(Vector2d.Zero, 1.01) { IsReversed = true });
            complex.Add(new Circle2d(Vector2d.Zero, 1.0));
            complex.Add(new Circle2d(new Vector2d(5, 0), 0.5));
            var old = complex.FindSolidRegions(0.1, true);
            var with = complex.FindSolidRegions(Relative(rel));
            AssertSame(old, with);
            // and the struct without the field set is the old Default
            AssertSame(old, complex.FindSolidRegions(PlanarComplex.FindSolidsOptions.Default));
            Assert.Equal(0.0, PlanarComplex.FindSolidsOptions.Default.SimplifyRelativeTolerance);
            Assert.Equal(0.0, PlanarComplex.FindSolidsOptions.SortPolygons.SimplifyRelativeTolerance);
        }

        static void AssertSame(PlanarComplex.SolidRegionInfo a, PlanarComplex.SolidRegionInfo b)
        {
            Assert.Equal(a.Polygons.Count, b.Polygons.Count);
            for (int i = 0; i < a.Polygons.Count; ++i) {
                Assert.True(a.Polygons[i].Outer.Vertices.SequenceEqual(b.Polygons[i].Outer.Vertices));
                Assert.Equal(a.Polygons[i].Holes.Count, b.Polygons[i].Holes.Count);
                for (int h = 0; h < a.Polygons[i].Holes.Count; ++h)
                    Assert.True(a.Polygons[i].Holes[h].Vertices.SequenceEqual(b.Polygons[i].Holes[h].Vertices));
            }
        }

        [Fact]
        public void RelativeAlone_WithoutAbsoluteTolerance_Simplifies()
        {
            // SortPolygons has SimplifyDeviationTolerance 0 (no simplification); the relative tolerance alone simplifies
            var complex = new PlanarComplex { DistanceAccuracy = 0.001 };
            complex.Add(new Circle2d(Vector2d.Zero, 1.0));
            var plain = complex.FindSolidRegions(PlanarComplex.FindSolidsOptions.SortPolygons);
            var o = PlanarComplex.FindSolidsOptions.SortPolygons;
            o.SimplifyRelativeTolerance = 0.01;
            var simplified = complex.FindSolidRegions(o);
            int n0 = plain.Polygons[0].Outer.VertexCount, n1 = simplified.Polygons[0].Outer.VertexCount;
            Assert.True(n1 < n0 / 2 && n1 >= 3, $"{n0} -> {n1} vertices");
            // (the deviation stays within the tolerance: 0.01 times the diagonal 2.83)
            Assert.True(Math.Abs(simplified.Area - plain.Area) < 0.03 * plain.Area, $"area {plain.Area} -> {simplified.Area}");
        }

        [Fact]
        public void BothTolerances_TheSmallerOneIsUsedForEveryLoop()
        {
            var complex = new PlanarComplex { DistanceAccuracy = 0.001 };
            complex.Add(new Circle2d(Vector2d.Zero, 1.0));
            int Count(double abs, double rel)
            {
                var o = PlanarComplex.FindSolidsOptions.SortPolygons;
                o.SimplifyDeviationTolerance = abs;
                o.SimplifyRelativeTolerance = rel;
                return complex.FindSolidRegions(o).Polygons[0].Outer.VertexCount;
            }
            int absOnly = Count(0.001, 0), relOnly = Count(0, 0.1), both1 = Count(0.001, 0.1), both2 = Count(1.0, 0.0001);
            Assert.Equal(absOnly, both1);                 // 0.001 < 0.1 * 2.83: the absolute one is smaller
            Assert.Equal(Count(0, 0.0001), both2);        // 0.0001 * 2.83 < 1: the relative one is smaller
            Assert.True(relOnly < absOnly);
        }

        static Polygon2d Ellipse(Vector2d center, double a, double b, int n)
        {
            var v = new Vector2d[n];
            for (int i = 0; i < n; ++i) {
                double t = 2 * Math.PI * i / n;
                v[i] = center + new Vector2d(a * Math.Cos(t), b * Math.Sin(t));
            }
            return new Polygon2d(v);
        }

        [Fact]
        public void RelativeTolerance_IsPerLoop_TimesTheDiagonalOfThatLoopsBoundingBox()
        {
            // a tall thin ellipse (width 0.04, height 20: the diagonal is about 20, the width is 0.04) and a small round loop far
            // from it. The relative tolerance of each loop is the same as an absolute one of rel times that loop's own diagonal.
            Polygon2d tall = Ellipse(Vector2d.Zero, 0.02, 10, 400), small = Ellipse(new Vector2d(50, 0), 0.5, 0.5, 400);
            const double rel = 0.002;
            double diagTall = Math.Sqrt(tall.GetBounds().Width * tall.GetBounds().Width + tall.GetBounds().Height * tall.GetBounds().Height);
            double diagSmall = Math.Sqrt(small.GetBounds().Width * small.GetBounds().Width + small.GetBounds().Height * small.GetBounds().Height);
            var both = new PlanarComplex();
            both.Add(tall);
            both.Add(small);
            var o = PlanarComplex.FindSolidsOptions.Default;
            o.SimplifyDeviationTolerance = 0;
            o.SimplifyRelativeTolerance = rel;
            var relative = both.FindSolidRegions(o);
            int Count(PlanarComplex.SolidRegionInfo r, double cx) => r.Polygons.Single(p => Math.Abs(p.Outer.GetBounds().Center.x - cx) < 1).Outer.VertexCount;
            int Absolute(Polygon2d poly, double tol, double cx)
            {
                var c = new PlanarComplex();
                c.Add(poly);
                var ao = PlanarComplex.FindSolidsOptions.Default;
                ao.SimplifyDeviationTolerance = tol;
                return Count(c.FindSolidRegions(ao), cx);
            }
            Assert.Equal(Absolute(tall, rel * diagTall, 0), Count(relative, 0));
            Assert.Equal(Absolute(small, rel * diagSmall, 50), Count(relative, 50));
            // (the two loops are simplified differently: one tolerance for both would not give these counts)
            Assert.NotEqual(Absolute(tall, rel * diagSmall, 0), Count(relative, 0));
        }

        [Fact]
        public void Reuse_FindSolidRegionsTwiceOnOneComplex_SameResult()
        {
            var complex = Circles(1e-2, true, Similarity.Identity, out _, out _);
            var first = complex.FindSolidRegions(Relative(1e-4));
            var second = complex.FindSolidRegions(Relative(1e-4));
            AssertSame(first, second);
        }
    }
}
