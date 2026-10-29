using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class CoplanarTriangleDifferentialTests
    {
        sealed class Case
        {
            public string Name;
            public Triangle3d A, B;
            public Vector3d[] Outline;
            public Case(string name, double[] a, double[] b, params double[] outline)
            {
                Name = name;
                A = Triangle(a); B = Triangle(b);
                Outline = Enumerable.Range(0, outline.Length / 2)
                    .Select(i => new Vector3d(outline[2 * i], outline[2 * i + 1], 0)).ToArray();
            }
        }

        static Triangle3d Triangle(double[] p) => new Triangle3d(
            new Vector3d(p[0], p[1], 0), new Vector3d(p[2], p[3], 0), new Vector3d(p[4], p[5], 0));

        static IEnumerable<Case> Cases()
        {
            var right = new double[] { 0, 0, 4, 0, 0, 4 };
            yield return new Case("disjoint", right, new double[] { 5, 5, 7, 5, 5, 7 });
            yield return new Case("containment", right, new double[] { .5, .5, 1.5, .5, .5, 1.5 }, .5, .5, 1.5, .5, .5, 1.5);
            yield return new Case("equality", right, right, 0, 0, 4, 0, 0, 4);
            yield return new Case("quadrilateral", right, new double[] { -1, 1, 3, 1, 1, 3 }, 0, 1, 3, 1, 1, 3, 0, 2);
            yield return new Case("pentagon", new double[] { 0, 0, 6, 0, 3, 6 },
                new double[] { 0, 4, 6, 4, 3, 0 }, 3, 0, 24.0 / 5, 12.0 / 5, 4, 4, 2, 4, 6.0 / 5, 12.0 / 5);
            yield return new Case("hexagon", new double[] { 0, 0, 6, 0, 3, 6 },
                new double[] { 0, 4, 6, 4, 3, -2 }, 2, 0, 4, 0, 5, 2, 4, 4, 2, 4, 1, 2);
            yield return new Case("vertex", right, new double[] { 4, 0, 6, 0, 4, -2 }, 4, 0);
            yield return new Case("vertex-on-edge", new double[] { 6, -10, 12, 9, 14, -5 },
                new double[] { 16, -10, 4, -11, 12, -7 }, 6, -10);
            yield return new Case("full-edge", right, new double[] { 0, 0, 0, -4, 4, 0 }, 0, 0, 4, 0);
            yield return new Case("partial-edge", right, new double[] { 1, 0, 1, -2, 3, 0 }, 1, 0, 3, 0);
            yield return new Case("collinear-overlap", right, new double[] { 2, 0, 2, -2, 6, 0 }, 2, 0, 4, 0);
        }

        static readonly int[][] Permutations = {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
        };

        public static IEnumerable<object[]> NamedCases() => Cases().Select(c => new object[] { c.Name });
        public static IEnumerable<object[]> Seeds() => new[] { 7, 19, 43, 101, 307, 701, 1237, 4093 }
            .Select(s => new object[] { s });

        static Triangle3d Map(Triangle3d t, Func<Vector3d, Vector3d> f) => new Triangle3d(f(t.V0), f(t.V1), f(t.V2));
        static Triangle3d Permute(Triangle3d t, int[] p) => new Triangle3d(t[p[0]], t[p[1]], t[p[2]]);

        static Vector3d ProjectFixture(Vector3d p, int plane)
        {
            switch (plane) {
                case 0: return p;
                case 1: return new Vector3d(p.x, 0, p.y);
                case 2: return new Vector3d(0, p.x, p.y);
                case 3: return new Vector3d(p.x, p.y, (p.x + p.y) / 2);
                default: return new Vector3d(p.x, p.y, p.x + p.y);
            }
        }

        static double Resolution(Triangle3d a, Triangle3d b)
        {
            double magnitude = 1;
            for (int i = 0; i < 3; ++i) {
                magnitude = Math.Max(magnitude, a[i].Length);
                magnitude = Math.Max(magnitude, b[i].Length);
            }
            return 128 * MathUtil.Epsilon * magnitude;
        }

        static bool Coplanar(Triangle3d a, Triangle3d b) => Enumerable.Range(0, 3)
            .All(i => ExactPredicates.Orient3d(a.V0, a.V1, a.V2, b[i]) == 0);

        static int DropAxis(Triangle3d t)
        {
            Vector3d n = (t.V1 - t.V0).Cross(t.V2 - t.V0);
            double x = Math.Abs(n.x), y = Math.Abs(n.y), z = Math.Abs(n.z);
            return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
        }

        static Vector2d Drop(Vector3d p, int axis) => axis == 0 ? new Vector2d(p.y, p.z)
            : axis == 1 ? new Vector2d(p.x, p.z) : new Vector2d(p.x, p.y);

        static double Cross(Vector2d a, Vector2d b) => a.x * b.y - a.y * b.x;

        static void Contains(Triangle3d t, Vector3d p, double tolerance, string context)
        {
            int axis = DropAxis(t);
            Vector2d a = Drop(t.V0, axis), b = Drop(t.V1, axis), c = Drop(t.V2, axis), q = Drop(p, axis);
            int winding = ExactPredicates.Orient2d(a, b, c);
            Assert.NotEqual(0, winding);
            for (int i = 0; i < 3; ++i) {
                Vector2d v = Drop(t[i], axis), w = Drop(t[(i + 1) % 3], axis);
                double side = winding * Cross(w - v, q - v);
                Assert.True(side >= -tolerance * (w - v).Length, context + " point outside input triangle");
            }
            Vector3d normal = (t.V1 - t.V0).Cross(t.V2 - t.V0).Normalized;
            Assert.True(Math.Abs(normal.Dot(p - t.V0)) <= tolerance, context + " point off input plane");
        }

        static double Area(Vector3d[] p)
        {
            if (p.Length < 3) return 0;
            Vector3d sum = Vector3d.Zero;
            for (int i = 1; i + 1 < p.Length; ++i)
                sum += (p[i] - p[0]).Cross(p[i + 1] - p[0]);
            return sum.Length / 2;
        }

        static Vector3d[] Geometry(IntrTriangle3Triangle3 query, string context)
        {
            Assert.Equal(IntersectionResult.Intersects, query.Result);
            Assert.True(query.Quantity > 0, context + " empty contact geometry");
            Assert.Equal(IntersectionType.Polygon, query.Type);
            Assert.NotNull(query.PolygonPoints);
            Assert.True(query.PolygonPoints.Length >= query.Quantity, context);
            return query.PolygonPoints.Take(query.Quantity).ToArray();
        }

        static Vector3d[] DistinctPoints(Vector3d[] p, double tolerance)
        {
            var result = new List<Vector3d>();
            foreach (Vector3d v in p)
                if (!result.Any(w => v.Distance(w) <= tolerance)) result.Add(v);
            return result.ToArray();
        }

        static void SameSet(Vector3d[] expected, Vector3d[] actual, double tolerance, string context)
        {
            Vector3d[] unique = DistinctPoints(actual, tolerance);
            Assert.True(expected.Length == unique.Length, context + " outline count expected=" + expected.Length + " actual=" + unique.Length);
            foreach (Vector3d p in expected)
                Assert.True(unique.Any(q => p.Distance(q) <= tolerance), context + " missing outline vertex " + Format(p));
        }

        static void AssertPositiveOutline(IntrTriangle3Triangle3 query, Vector3d[] expected,
            double resolution, string context)
        {
            Vector3d[] actual = Geometry(query, context);
            Assert.Equal(expected.Length, actual.Length);
            double shortestEdge = Enumerable.Range(0, expected.Length)
                .Min(i => expected[i].Distance(expected[(i + 1) % expected.Length]));
            double tolerance = Math.Min(resolution, shortestEdge / 64);
            Assert.Equal(actual.Length, DistinctPoints(actual, 0).Length);
            SameSet(expected, actual, tolerance, context);
            double expectedArea = Area(expected), actualArea = Area(actual);
            Assert.True(expectedArea > 0 && actualArea > 0, context + " collapsed positive-area outline");
            double perimeter = Enumerable.Range(0, expected.Length)
                .Sum(i => expected[i].Distance(expected[(i + 1) % expected.Length]));
            double areaTolerance = tolerance * perimeter + 64 * MathUtil.Epsilon * expectedArea;
            Assert.True(Math.Abs(expectedArea - actualArea) <= areaTolerance, context + " analytic area mismatch");
        }

        static string Format(Vector3d p) => string.Join(",", new[] { p.x, p.y, p.z }
            .Select(x => x.ToString("R", CultureInfo.InvariantCulture)));
        static string Describe(Triangle3d a, Triangle3d b) => " A=" + string.Join(";", Enumerable.Range(0, 3).Select(i => Format(a[i])))
            + " B=" + string.Join(";", Enumerable.Range(0, 3).Select(i => Format(b[i])));

        static IntrTriangle3Triangle3 Run(Triangle3d a, Triangle3d b, bool expected, string context,
            bool exactContract = true, double extraTolerance = 0)
        {
            context += Describe(a, b);
            if (exactContract) {
                Assert.True(Coplanar(a, b), context + " noncoplanar reference input");
                Assert.Equal(expected, ExactTriangleIntersection.Intersects(ref a, ref b));
            }
            var query = new IntrTriangle3Triangle3(a, b) { ReportCoplanarIntersection = true };
            bool actual = query.Find();
            Assert.True(actual == expected, context + " Boolean mismatch; expected=" + expected + "; actual=" + actual);
            if (!expected) {
                Assert.Equal(IntersectionResult.NoIntersection, query.Result);
                Assert.Equal(IntersectionType.Empty, query.Type);
                Assert.Equal(0, query.Quantity);
                return query;
            }
            Vector3d[] p = Geometry(query, context);
            double tolerance = Resolution(a, b) + extraTolerance;
            foreach (Vector3d v in p) {
                Assert.True(v.IsFinite, context + " nonfinite output");
                Contains(a, v, tolerance, context);
                Contains(b, v, tolerance, context);
            }
            if (Area(p) > tolerance * tolerance) {
                Assert.Equal(p.Length, DistinctPoints(p, tolerance).Length);
                int axis = DropAxis(a), sign = 0;
                for (int i = 0; i < p.Length; ++i) {
                    Vector2d x = Drop(p[i], axis), y = Drop(p[(i + 1) % p.Length], axis), z = Drop(p[(i + 2) % p.Length], axis);
                    double turn = Cross(y - x, z - y);
                    if (Math.Abs(turn) <= tolerance * ((y - x).Length + (z - y).Length)) continue;
                    int s = Math.Sign(turn);
                    if (sign == 0) sign = s;
                    Assert.Equal(sign, s);
                }
            }
            return query;
        }

        [Theory]
        [MemberData(nameof(NamedCases))]
        public void NamedOutlines_AllProjectionsAndCornerPermutations(string name)
        {
            Case fixture = Cases().Single(c => c.Name == name);
            for (int plane = 0; plane < 5; ++plane) {
                Func<Vector3d, Vector3d> map = p => ProjectFixture(p, plane);
                Triangle3d a = Map(fixture.A, map), b = Map(fixture.B, map);
                bool expected = fixture.Outline.Length > 0;
                Assert.Equal(expected, ExactTriangleIntersection.IntersectsByEdges(ref a, ref b));
                Vector3d[] outline = fixture.Outline.Select(map).ToArray();
                foreach (int[] pa in Permutations)
                    foreach (int[] pb in Permutations)
                        for (int swap = 0; swap < 2; ++swap) {
                            Triangle3d x = Permute(a, pa), y = Permute(b, pb);
                            if (swap != 0) { Triangle3d temp = x; x = y; y = temp; }
                            string context = name + " plane=" + plane + " pa=" + string.Join(",", pa) + " pb=" + string.Join(",", pb) + " swap=" + swap;
                            var query = Run(x, y, expected, context);
                            if (!expected) continue;
                            Vector3d[] result = Geometry(query, context);
                            double tolerance = Resolution(x, y);
                            SameSet(outline, result, tolerance, context);
                            Assert.True(Math.Abs(Area(outline) - Area(result)) <= tolerance * 64, context + " area mismatch");
                        }
            }
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void SeededExactlyCoplanarPairs_AgreeWithExactClassificationAndSwap(int seed)
        {
            var random = new Random(seed);
            for (int i = 0; i < 256; ++i) {
                Func<Triangle3d> next = () => {
                    Triangle3d t;
                    do {
                        t = Triangle(Enumerable.Range(0, 6).Select(_ => random.Next(-16, 17) / 2.0).ToArray());
                    } while (ExactPredicates.Orient2d(Drop(t.V0, 2), Drop(t.V1, 2), Drop(t.V2, 2)) == 0);
                    return t;
                };
                int plane = i % 5;
                Triangle3d a = Map(next(), p => ProjectFixture(p, plane)), b = Map(next(), p => ProjectFixture(p, plane));
                bool expected = ExactTriangleIntersection.Intersects(ref a, ref b);
                string context = "seed=" + seed + " pair=" + i + " plane=" + plane;
                var q = Run(a, b, expected, context);
                var swapped = Run(b, a, expected, context + " swapped");
                if (expected) {
                    double tolerance = Resolution(a, b) * 4;
                    SameSet(DistinctPoints(Geometry(q, context), tolerance), Geometry(swapped, context), tolerance, context + Describe(a, b));
                    Assert.True(Math.Abs(Area(Geometry(q, context)) - Area(Geometry(swapped, context))) <= tolerance * 128, context + " swap area");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities_ExactAndRoundedPlaneContractsRemainSeparate(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (Case fixture in Cases().Where(c => c.Name == "containment" || c.Name == "hexagon")) {
                var baseline = Run(fixture.A, fixture.B, true, fixture.Name);
                Triangle3d a = Map(fixture.A, s.Apply), b = Map(fixture.B, s.Apply);
                bool exact = Coplanar(a, b);
                var query = Run(a, b, true, name + " " + fixture.Name + " exactCoplanar=" + exact,
                    exact, s.Resolution(8) * 128);
                Vector3d[] mapped = Geometry(query, name).Select(s.Inverse).ToArray();
                Vector3d[] expected = Geometry(baseline, name);
                if (s.IsExact) {
                    Assert.Equal(expected.Length, mapped.Length);
                    for (int i = 0; i < mapped.Length; ++i)
                        for (int k = 0; k < 3; ++k)
                            Assert.Equal(BitConverter.DoubleToInt64Bits(expected[i][k]), BitConverter.DoubleToInt64Bits(mapped[i][k]));
                } else {
                    SameSet(DistinctPoints(expected, Resolution(fixture.A, fixture.B)), mapped,
                        256 * s.Resolution(8) + Resolution(fixture.A, fixture.B), name);
                }
            }
        }

        [Fact]
        public void Reuse_HitMissHitAndRepeatedComputeMatchFreshQueries()
        {
            Case hit = Cases().Single(c => c.Name == "hexagon"), miss = Cases().Single(c => c.Name == "disjoint");
            var reused = new IntrTriangle3Triangle3(hit.A, hit.B) { ReportCoplanarIntersection = true };
            foreach (Case fixture in new[] { hit, miss, hit }) {
                reused.Triangle0 = fixture.A; reused.Triangle1 = fixture.B;
                bool expected = fixture.Outline.Length > 0;
                var fresh = Run(fixture.A, fixture.B, expected, fixture.Name);
                Assert.Equal(expected, reused.Find());
                Assert.Equal(fresh.Result, reused.Result); Assert.Equal(fresh.Type, reused.Type); Assert.Equal(fresh.Quantity, reused.Quantity);
                if (expected) SameSet(Geometry(fresh, fixture.Name), Geometry(reused, fixture.Name), Resolution(fixture.A, fixture.B), fixture.Name);
                Assert.Same(reused, reused.Compute());
                Assert.Equal(fresh.Result, reused.Result); Assert.Equal(fresh.Type, reused.Type); Assert.Equal(fresh.Quantity, reused.Quantity);
            }
            var disabled = new IntrTriangle3Triangle3(hit.A, hit.B);
            Assert.False(disabled.ReportCoplanarIntersection);
            Assert.False(disabled.Find());
        }

        [Theory]
        [InlineData(.5, true)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        public void EpsilonThickPlanes_CharacterizeLegacyRatherThanExactContract(double factor, bool legacy)
        {
            Case fixture = Cases().Single(c => c.Name == "containment");
            Triangle3d a = fixture.A, b = Map(fixture.B, p => p + new Vector3d(0, 0, factor * MathUtil.ZeroTolerance));
            Assert.False(ExactTriangleIntersection.Intersects(ref a, ref b));
            var query = new IntrTriangle3Triangle3(a, b) { ReportCoplanarIntersection = true };
            Assert.Equal(legacy, query.Find());
            if (legacy) {
                Vector3d[] p = Geometry(query, "epsilon");
                Assert.All(p, v => Assert.True(v.IsFinite));
                SameSet(fixture.Outline, p, Resolution(a, b), "epsilon projected to first plane");
            } else Assert.Equal(0, query.Quantity);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        public void DegenerateControls_UseBooleanOnlyRoute(int kind, bool expected)
        {
            Triangle3d a = Triangle(new double[] { 0, 0, 4, 0, 0, 4 });
            Triangle3d b = kind == 0 ? Triangle(new double[] { 1, 1, 1, 1, 1, 1 })
                : kind == 1 ? Triangle(new double[] { -1, 1, 1, 1, 3, 1 }) : Triangle(new double[] { 5, 5, 6, 5, 7, 5 });
            Assert.Equal(expected, ExactTriangleIntersection.Intersects(ref a, ref b));
            foreach (bool swap in new[] { false, true }) {
                var query = new IntrTriangle3Triangle3(swap ? b : a, swap ? a : b) { ReportCoplanarIntersection = true };
                Assert.Equal(expected, query.Find());
                Assert.Equal(expected ? IntersectionType.Unknown : IntersectionType.Empty, query.Type);
                Assert.Equal(0, query.Quantity);
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        public void TinyDyadicGapOrOverlap_IsNotRoundedIntoContact(int direction)
        {
            double delta = direction * Math.Pow(2, -30);
            Triangle3d a = Triangle(new double[] { 0, 0, 4, 0, 0, 4 });
            Triangle3d b = Triangle(new double[] { 4 + delta, 0, 6 + delta, 0, 4 + delta, 2 });
            string context = "tiny offset direction=" + direction;
            var query = Run(a, b, direction < 0, context);
            if (direction < 0) {
                Vector3d[] expected = { new Vector3d(4 + delta, 0, 0), new Vector3d(4, 0, 0), new Vector3d(4 + delta, -delta, 0) };
                AssertPositiveOutline(query, expected, Resolution(a, b), context);
                Assert.Equal(delta * delta / 2, Area(expected));
            }
        }

        [Fact]
        public void ThinDyadicTrianglesAndLargeTranslation_AgreeWithExactOracle()
        {
            double height = Math.Pow(2, -20);
            Triangle3d a = Triangle(new double[] { 0, 0, 4, 0, 0, height });
            Triangle3d b = Triangle(new double[] { .5, height / 8, 1, height / 8, .5, height / 4 });
            foreach (Vector3d translation in new[] { Vector3d.Zero, new Vector3d(1048576, 1048576, 1048576) }) {
                Triangle3d translatedA = Map(a, p => p + translation), translatedB = Map(b, p => p + translation);
                string context = "thin translation=" + Format(translation);
                var query = Run(translatedA, translatedB, true, context);
                Vector3d[] expected = { translatedB.V0, translatedB.V1, translatedB.V2 };
                AssertPositiveOutline(query, expected, Resolution(translatedA, translatedB), context);
                Assert.Equal(height / 32, Area(expected));
            }
        }
    }
}
