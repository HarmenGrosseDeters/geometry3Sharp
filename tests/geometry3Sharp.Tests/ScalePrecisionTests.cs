using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// docs/ROBUSTNESS_SWEEP.md S14 (ContMinCircle2), S19 (QuadricError.OptimalPoint) and S8 (PlaneIntersectionTarget):
    /// decisions relative to the input instead of absolute constants, and double instead of float arithmetic.
    /// </summary>
    public class ScalePrecisionTests
    {
        // ---- S14: ContMinCircle2 ----

        /// <summary>The in-plane part of a similarity: its scale, its turn of the x axis, and its xy translation.</summary>
        sealed class Similarity2
        {
            public readonly Similarity S;
            readonly double cos, sin;

            public Similarity2(Similarity s)
            {
                S = s;
                Vector3d x = s.ApplyDirection(Vector3d.AxisX);
                double len = Math.Sqrt(x.x * x.x + x.y * x.y);
                cos = s.IsRotated ? x.x / len : 1;
                sin = s.IsRotated ? x.y / len : 0;
            }

            public Vector2d Apply(Vector2d p) => new Vector2d(
                S.Scale * (cos * p.x - sin * p.y) + S.Translation.x,
                S.Scale * (sin * p.x + cos * p.y) + S.Translation.y);

            public Vector2d Inverse(Vector2d q)
            {
                double x = (q.x - S.Translation.x) / S.Scale, y = (q.y - S.Translation.y) / S.Scale;
                return new Vector2d(cos * x + sin * y, -sin * x + cos * y);
            }
        }

        /// <summary>Smallest circle through 2 or 3 of the points that contains all of them: O(n^4), no shared code with ContMinCircle2.</summary>
        static Circle2d BruteForceCircle(IList<Vector2d> points, double size)
        {
            double slack = 8 * MathUtil.Epsilon * size;
            double best = double.MaxValue;
            Vector2d center = Vector2d.Zero;
            Action<Vector2d, double> consider = (c, r) => {
                if (r >= best) return;
                foreach (Vector2d p in points)
                    if (p.Distance(c) > r + slack) return;
                best = r;
                center = c;
            };
            consider(points[0], 0);
            for (int i = 0; i < points.Count; ++i) {
                for (int j = i + 1; j < points.Count; ++j) {
                    consider(0.5 * (points[i] + points[j]), 0.5 * points[i].Distance(points[j]));
                    Vector2d u = points[j] - points[i];
                    for (int k = j + 1; k < points.Count; ++k) {
                        Vector2d v = points[k] - points[i];
                        double d = 2 * (u.x * v.y - u.y * v.x);
                        if (d == 0) continue;
                        Vector2d q = new Vector2d(
                            (v.y * u.LengthSquared - u.y * v.LengthSquared) / d,
                            (u.x * v.LengthSquared - v.x * u.LengthSquared) / d);
                        consider(points[i] + q, q.Length);
                    }
                }
            }
            return new Circle2d(center, best);
        }

        static Vector2d[] RandomPoints(int seed, int n)
        {
            var rand = new Random(seed);
            return Enumerable.Range(0, n).Select(i => new Vector2d(rand.NextDouble(), rand.NextDouble())).ToArray();
        }

        /// <summary>Point sets of size about 1: at 1e-3 scale, their determinants are below the old 1e-5.</summary>
        public static IEnumerable<object[]> PointSetNames()
        {
            foreach (string set in PointSets().Keys)
                yield return new object[] { set };
        }

        static Dictionary<string, Vector2d[]> PointSets()
        {
            var sets = new Dictionary<string, Vector2d[]>();
            sets["acute triangle"] = new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(0.5, 1) };
            sets["obtuse triangle"] = new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(0.5, 0.1) };
            sets["random 10"] = RandomPoints(1, 10);
            sets["random 30"] = RandomPoints(2, 30);
            sets["cocircular"] = Enumerable.Range(0, 16).Select(i => new Vector2d(
                0.5 + 0.5 * Math.Cos(i * Math.PI / 8 + 0.1), 0.5 + 0.5 * Math.Sin(i * Math.PI / 8 + 0.1))).ToArray();
            sets["grid"] = Enumerable.Range(0, 25).Select(i => new Vector2d((i % 5) / 4.0, (i / 5) / 4.0)).ToArray();
            sets["collinear"] = Enumerable.Range(0, 12).Select(i => new Vector2d(i / 11.0, 0.5 * i / 11.0)).ToArray();
            sets["duplicates"] = RandomPoints(3, 8).Concat(RandomPoints(3, 8)).ToArray();
            // a needle: the smallest angle is below 1e-5, the circle through all three is still the minimal one
            sets["needle"] = new[] { new Vector2d(0, 0), new Vector2d(1, 0.5e-6), new Vector2d(1, -0.5e-6) };
            // close to the largest extent of the set, and much closer to each other than the old tolerance (3e-3)
            Vector2d[] near = RandomPoints(4, 12);
            sets["near duplicates"] = near.Concat(near.Select(p => p + new Vector2d(1e-4, -2e-4))).Concat(new[] {
                new Vector2d(-0.2, -0.2), new Vector2d(-0.2 - 1e-4, -0.2 - 1e-4), new Vector2d(1.2, 1.2), new Vector2d(1.2 + 1e-4, 1.2 + 1e-4)
            }).ToArray();
            return sets;
        }

        public static IEnumerable<object[]> SimilarityAndPointSet()
        {
            foreach (Similarity s in Similarity.All())
                foreach (string set in PointSets().Keys)
                    yield return new object[] { s.Name, set };
        }

        [Theory]
        [MemberData(nameof(SimilarityAndPointSet))]
        public void MinCircle_EqualsBruteForceUnderSimilarity(string name, string set)
        {
            Similarity s = Similarity.ByName(name);
            var s2 = new Similarity2(s);
            Vector2d[] input = PointSets()[set];
            Vector2d[] points = input.Select(s2.Apply).ToArray();
            Circle2d expected = BruteForceCircle(input, 2);

            Circle2d result = new ContMinCircle2(points).Result;

            double tol = 64 * s.Resolution(2);
            Assert.True(Math.Abs(result.Radius / s.Scale - expected.Radius) <= tol,
                $"radius {result.Radius / s.Scale} expected {expected.Radius}, tol {tol}");
            // (the brute force's center of a needle is not the reference: it is based at the far end, see AssertMinimal for the exact one)
            if (set != "needle")
                Assert.True(s2.Inverse(result.Center).Distance(expected.Center) <= tol,
                    $"center {s2.Inverse(result.Center)} expected {expected.Center}, tol {tol}");
            else
                AssertMinimal(points, result, tol * s.Scale, tol * s.Scale, "needle");
            foreach (Vector2d p in points)
                Assert.True(p.Distance(result.Center) / s.Scale <= result.Radius / s.Scale + tol, $"{p} is outside");
        }

        [Theory]
        [MemberData(nameof(PointSetNames))]
        public void MinCircle_PowerOfTwoScales_BitIdentical(string set)
        {
            Vector2d[] input = PointSets()[set];
            Circle2d reference = new ContMinCircle2(input).Result;
            foreach (int exponent in new[] { -40, -24, -10, 10, 30 }) {
                double scale = Math.Pow(2, exponent);
                Circle2d result = new ContMinCircle2(input.Select(p => scale * p).ToArray()).Result;
                Assert.Equal(reference.Center.x * scale, result.Center.x);
                Assert.Equal(reference.Center.y * scale, result.Center.y);
                Assert.Equal(reference.Radius * scale, result.Radius);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(1e-3)]
        [InlineData(1e-6)]
        [InlineData(1e3)]
        public void MinCircle_AcuteTriangle_IsItsCircumcircle(double scale)
        {
            // center (0.5, 0.375), radius 0.625 at scale 1: 0.5^2 + y^2 = (1 - y)^2
            Vector2d[] points = { scale * new Vector2d(0, 0), scale * new Vector2d(1, 0), scale * new Vector2d(0.5, 1) };
            Circle2d result = new ContMinCircle2(points).Result;
            double tol = 8 * MathUtil.Epsilon;
            Assert.True(Math.Abs(result.Radius / scale - 0.625) <= tol, $"radius {result.Radius / scale}");
            Assert.True((result.Center / scale).Distance(new Vector2d(0.5, 0.375)) <= tol, $"center {result.Center / scale}");
        }

        [Fact]
        public void MinCircle_Needle_InEveryOrder_IsItsCircumcircle()
        {
            // An acute needle: only its smallest angle is tiny, and the circle through all three points is the minimal
            // one. Center (c, 0) with c^2 = (1 - c)^2 + (d/2)^2, so radius c = 1/2 + d^2/8. The two-point circles leave
            // a point outside by about d^2/2, far above this tolerance, whichever order the points come in.
            const double d = 1e-6;
            Vector2d a = new Vector2d(0, 0), b = new Vector2d(1, d / 2), c = new Vector2d(1, -d / 2);
            foreach (Vector2d[] points in new[] {
                new[] { a, b, c }, new[] { a, c, b }, new[] { b, a, c }, new[] { b, c, a }, new[] { c, a, b }, new[] { c, b, a } }) {
                Circle2d result = new ContMinCircle2(points).Result;
                Assert.True(Math.Abs(result.Radius - (0.5 + d * d / 8)) <= 16 * MathUtil.Epsilon, $"radius {result.Radius}");
            }
        }

        [Fact]
        public void MinCircle_RepeatedRuns_BitIdentical()
        {
            Vector2d[] points = RandomPoints(5, 200);
            Circle2d first = new ContMinCircle2(points).Result;
            for (int k = 0; k < 20; ++k) {
                Circle2d again = new ContMinCircle2(points).Result;
                Assert.Equal(first.Center.x, again.Center.x);
                Assert.Equal(first.Center.y, again.Center.y);
                Assert.Equal(first.Radius, again.Radius);
            }
        }

        [Fact]
        public void MinCircle_ManyRandomSets_EqualBruteForce()
        {
            double tol = 64 * Similarity.Identity.Resolution(2);
            // (the old algorithm passed some of these seeds, and failed at 1194 and on 9 of 5000 lattice sets)
            for (int seed = 100; seed < 3100; ++seed) {
                Vector2d[] points = RandomPoints(seed, 3 + seed % 12);
                if (seed % 2 == 0)      // on a lattice: many ties, cocircular and collinear points
                    points = points.Select(p => new Vector2d(Math.Round(p.x * 6) / 6, Math.Round(p.y * 6) / 6)).ToArray();
                Circle2d expected = BruteForceCircle(points, 2);
                Circle2d result = new ContMinCircle2(points).Result;
                Assert.True(Math.Abs(result.Radius - expected.Radius) <= tol,
                    $"seed {seed}: radius {result.Radius} expected {expected.Radius}");
                foreach (Vector2d p in points)
                    Assert.True(p.Distance(result.Center) <= result.Radius + tol, $"seed {seed}: {p} is outside");
            }
        }

        [Fact]
        public void MinCircle_LatticeSetThatTheOldAlgorithmGotWrong()
        {
            // the old result had radius 0.2635, with a point 0.16 outside
            Vector2d[] points = { new Vector2d(1, 2.0 / 3), new Vector2d(2.0 / 3, 5.0 / 6), new Vector2d(1.0 / 3, 0.5),
                new Vector2d(0.5, 0.5), new Vector2d(2.0 / 3, 1.0 / 3) };
            Circle2d expected = BruteForceCircle(points, 2);
            Circle2d result = new ContMinCircle2(points).Result;
            Assert.True(Math.Abs(result.Radius - expected.Radius) <= 1e-14, $"radius {result.Radius} expected {expected.Radius}");
            Assert.True(result.Radius > 0.34, $"radius {result.Radius}");
        }

        [Theory]
        [InlineData(1e60)]
        [InlineData(1e-60)]
        [InlineData(1e80)]
        [InlineData(1e-80)]
        public void MinCircle_ExtremeScale_IsStillTheCircumcircle(double scale)
        {
            // a product of three squared edges would overflow or underflow here
            Vector2d[] points = { scale * new Vector2d(0, 0), scale * new Vector2d(1, 0), scale * new Vector2d(0.5, 1) };
            Circle2d result = new ContMinCircle2(points).Result;
            Assert.True(Math.Abs(result.Radius / scale - 0.625) <= 8 * MathUtil.Epsilon, $"radius {result.Radius / scale}");
        }

        [Fact]
        public void MinCircle_EveryPointIsInside_WithoutAnyTolerance()
        {
            foreach (KeyValuePair<string, Vector2d[]> set in PointSets()) {
                foreach (Similarity s in Similarity.All()) {
                    var s2 = new Similarity2(s);
                    Vector2d[] points = set.Value.Select(s2.Apply).ToArray();
                    Circle2d result = new ContMinCircle2(points).Result;
                    foreach (Vector2d p in points)
                        Assert.True(p.Distance(result.Center) <= result.Radius, $"{set.Key}, {s.Name}: {p} is outside");
                }
            }
        }

        [Theory]
        [InlineData(0.8660, true)]      // just below the sine of 60 degrees, 0.86602...
        [InlineData(0.8661, false)]     // just above: even the equilateral triangle counts as collinear
        public void MinCircle_EquilateralTriangle_MinimalUpToTheSineOf60Degrees(double epsilon, bool minimal)
        {
            double h = Math.Sqrt(3) / 2;
            Vector2d a = new Vector2d(0, 0), b = new Vector2d(1, 0), c = new Vector2d(0.5, h);
            foreach (Vector2d[] points in new[] {
                new[] { a, b, c }, new[] { a, c, b }, new[] { b, a, c }, new[] { b, c, a }, new[] { c, a, b }, new[] { c, b, a } }) {
                Circle2d result = new ContMinCircle2(points, epsilon).Result;
                // the circumradius 1/sqrt(3), or the center on an edge and the radius grown to reach the opposite vertex
                Assert.True(Math.Abs(result.Radius - (minimal ? 1 / Math.Sqrt(3) : h)) <= 8 * MathUtil.Epsilon, $"radius {result.Radius}");
                foreach (Vector2d p in points)
                    Assert.True(result.Contains(p), $"{p} is outside");
            }
        }

        [Fact]
        public void MinCircle_LargeEpsilon_TreatsTrianglesAsCollinear_InEveryOrder()
        {
            // An acute triangle with a unique longest edge. With epsilon above any sine, its three points "define no circle":
            // the circle over the longest edge is used, and the radius grows to reach the apex.
            Vector2d a = new Vector2d(0, 0), b = new Vector2d(2, 0), c = new Vector2d(1, 1.5);
            foreach (Vector2d[] points in new[] {
                new[] { a, b, c }, new[] { a, c, b }, new[] { b, a, c }, new[] { b, c, a }, new[] { c, a, b }, new[] { c, b, a } }) {
                Circle2d result = new ContMinCircle2(points, 2).Result;
                Assert.Equal(new Vector2d(1, 0), result.Center);
                Assert.Equal(1.5, result.Radius);
            }
        }

        [Theory]
        [InlineData(double.NaN, 0.0)]
        [InlineData(0.0, double.PositiveInfinity)]
        public void MinCircle_NonFinitePoint_IsRejected(double x, double y)
        {
            Assert.Throws<ArgumentException>(() => new ContMinCircle2(new[] { new Vector2d(0, 0), new Vector2d(x, y), new Vector2d(1, 1) }));
        }

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(0.001, 0.002)]
        public void MinCircle_SinglePointAndCoincidentPoints_RadiusZero(double x, double y)
        {
            Vector2d p = new Vector2d(x, y);
            foreach (Vector2d[] points in new[] { new[] { p }, new[] { p, p, p, p } }) {
                Circle2d result = new ContMinCircle2(points).Result;
                Assert.Equal(0, result.Radius);
                Assert.Equal(p, result.Center);
            }
        }

        /// <summary>
        /// The minimal circle in exact arithmetic: every double as an integer times a common power of two, the circle
        /// as center (Ux, Uy) / D and squared radius R2 / D^2. It proves its own result (<see cref="Check"/>), so it does
        /// not rest on the incremental loop it shares with the code under test, or on any of that code's rounding.
        /// </summary>
        sealed class ExactCircle
        {
            BigInteger[] xs, ys;
            int exponent;
            BigInteger ux, uy, d = BigInteger.One, r2;
            int[] support;

            public ExactCircle(IList<Vector2d> points)
            {
                int n = points.Count, min = int.MaxValue;
                var parts = new (BigInteger m, int e)[2 * n];
                for (int i = 0; i < n; ++i) {
                    parts[2 * i] = Split(points[i].x);
                    parts[2 * i + 1] = Split(points[i].y);
                }
                foreach (var part in parts)
                    if (part.m.IsZero == false) min = Math.Min(min, part.e);
                exponent = min;
                xs = new BigInteger[n];
                ys = new BigInteger[n];
                for (int i = 0; i < n; ++i) {
                    xs[i] = parts[2 * i].m << Math.Max(parts[2 * i].e - min, 0);
                    ys[i] = parts[2 * i + 1].m << Math.Max(parts[2 * i + 1].e - min, 0);
                }

                int[] order = Enumerable.Range(0, n).ToArray();
                var rand = new Random(4711);
                for (int i = n - 1; i > 0; --i) {
                    int j = rand.Next(i + 1), t = order[i];
                    order[i] = order[j];
                    order[j] = t;
                }
                SetPoint(order[0]);
                for (int i = 1; i < n; ++i) {
                    if (Inside(order[i])) continue;
                    SetPoint(order[i]);
                    for (int j = 0; j < i; ++j) {
                        if (Inside(order[j])) continue;
                        SetEdge(order[i], order[j]);
                        for (int k = 0; k < j; ++k)
                            if (Inside(order[k]) == false) SetTriangle(order[i], order[j], order[k]);
                    }
                }
                Check();
            }

            static (BigInteger m, int e) Split(double v)
            {
                long bits = BitConverter.DoubleToInt64Bits(v);
                int e = (int)((bits >> 52) & 0x7FF);
                long m = bits & 0xFFFFFFFFFFFFFL;
                if (e == 0) e = 1; else m |= 1L << 52;
                return (bits < 0 ? -new BigInteger(m) : new BigInteger(m), e - 1075);
            }

            BigInteger DistSqr(int i)
            {
                BigInteger dx = xs[i] * d - ux, dy = ys[i] * d - uy;
                return dx * dx + dy * dy;
            }

            bool Inside(int i) => DistSqr(i) <= r2;

            void SetPoint(int a)
            {
                ux = xs[a]; uy = ys[a]; d = BigInteger.One; r2 = BigInteger.Zero;
                support = new[] { a };
            }

            void SetEdge(int a, int b)
            {
                ux = xs[a] + xs[b]; uy = ys[a] + ys[b]; d = 2;
                r2 = BigInteger.Zero;
                r2 = DistSqr(a);
                support = new[] { a, b };
            }

            void SetTriangle(int a, int b, int c)
            {
                BigInteger bx = xs[b] - xs[a], by = ys[b] - ys[a], cx = xs[c] - xs[a], cy = ys[c] - ys[a];
                BigInteger det = 2 * (bx * cy - by * cx), lb = bx * bx + by * by, lc = cx * cx + cy * cy;
                Assert.False(det.IsZero, "exact solver: collinear support");
                BigInteger qx = cy * lb - by * lc, qy = bx * lc - cx * lb;
                if (det.Sign < 0) { det = -det; qx = -qx; qy = -qy; }
                d = det;
                ux = xs[a] * d + qx;
                uy = ys[a] * d + qy;
                r2 = DistSqr(a);
                support = new[] { a, b, c };
            }

            // all points are inside and the center is in the convex hull of the support: only then the circle is minimal
            void Check()
            {
                for (int i = 0; i < xs.Length; ++i)
                    Assert.True(Inside(i), "exact solver: a point is outside");
                foreach (int i in support)
                    Assert.Equal(r2, DistSqr(i));
                if (support.Length == 3) {
                    for (int i = 0; i < 3; ++i) {
                        int a = support[i], b = support[(i + 1) % 3], c = support[(i + 2) % 3];
                        Assert.True((xs[b] - xs[a]) * (xs[c] - xs[a]) + (ys[b] - ys[a]) * (ys[c] - ys[a]) >= 0,
                            "exact solver: an obtuse support");
                    }
                }
            }

            static int BitLength(BigInteger v) => v.IsZero ? 0 : 8 * BigInteger.Abs(v).ToByteArray().Length;

            /// <summary>num / den * 2^shift, within about one rounding (den > 0).</summary>
            static double Ratio(BigInteger num, BigInteger den, int shift)
            {
                if (num.IsZero) return 0;
                int s = 128 - (BitLength(num) - BitLength(den));
                BigInteger q = (s >= 0) ? (num << s) / den : num / (den << -s);
                return ExactPoint3.ScaleB((double)q, shift - s);
            }

            public double Radius
            {
                get {
                    if (r2.IsZero) return 0;
                    // an even shift, so the root of the power of two is exact
                    int s = 256 - (BitLength(r2) - 2 * BitLength(d));
                    s += s & 1;
                    BigInteger den = d * d;
                    BigInteger q = (s >= 0) ? (r2 << s) / den : r2 / (den << -s);
                    return ExactPoint3.ScaleB(Math.Sqrt((double)q), exponent - s / 2);
                }
            }

            public Vector2d Center => new Vector2d(Ratio(ux, d, exponent), Ratio(uy, d, exponent));
        }

        // The sets of the review: a0 + 0, 2.1, 4.2 on the unit circle, 2 to 6 points in each arc, Gaussian noise.
        static Vector2d[] ClusterSet(Random rand, double width, double noise)
        {
            double a0 = rand.NextDouble() * 6.28;
            var points = new List<Vector2d>();
            foreach (double arc in new[] { 0, 2.1, 4.2 }) {
                int count = rand.Next(2, 7);
                for (int i = 0; i < count; ++i) {
                    double a = a0 + arc + (2 * rand.NextDouble() - 1) * width;
                    points.Add(new Vector2d(Math.Cos(a) + Gauss(rand) * noise, Math.Sin(a) + Gauss(rand) * noise));
                }
            }
            for (int i = points.Count - 1; i > 0; --i) {
                int j = rand.Next(i + 1);
                Vector2d t = points[i];
                points[i] = points[j];
                points[j] = t;
            }
            return points.ToArray();
        }

        static double Gauss(Random rand) => Math.Sqrt(-2 * Math.Log(1 - rand.NextDouble())) * Math.Cos(2 * Math.PI * rand.NextDouble());

        static double MaxCoord(IEnumerable<Vector2d> points) => points.Max(p => Math.Max(Math.Abs(p.x), Math.Abs(p.y)));

        /// <summary>The result against the exact circle: radius and center within tol, no point outside through Contains.</summary>
        static void AssertMinimal(IList<Vector2d> points, Circle2d result, double tol, double centerTol, string what)
        {
            var exact = new ExactCircle(points);
            double expected = exact.Radius;
            Assert.True(Math.Abs(result.Radius - expected) <= tol, $"{what}: radius {result.Radius:R} expected {expected:R}, tol {tol}");
            Assert.True(result.Center.Distance(exact.Center) <= centerTol, $"{what}: center {result.Center} expected {exact.Center}");
            foreach (Vector2d p in points)
                Assert.True(result.Contains(p), $"{what}: {p} is outside");
        }

        static readonly Vector2d[] ReviewCluster = {
            new Vector2d(-0.29263477811403177, 0.9562242867481332),
            new Vector2d(0.9731573095962865, -0.23014093769501476),
            new Vector2d(-0.6776862793410731, -0.7353511470568985),
            new Vector2d(0.9731573169016159, -0.23014090613838026),
            new Vector2d(0.9731573125062671, -0.23014092747955794) };

        [Fact]
        public void MinCircle_TightClusterAtASupportPoint_IsMinimal()
        {
            // The radius used to be 1.02828: a circumcenter based at the far end of a needle has an error of eps * L / angle,
            // so the next point was falsely outside, and the circle moved to the wrong side.
            Circle2d result = new ContMinCircle2(ReviewCluster).Result;
            Assert.True(Math.Abs(result.Radius - 1.00000000066) <= 1e-10, $"radius {result.Radius}");
            Assert.True(result.Center.Length <= 1e-8, $"center {result.Center}");
            AssertMinimal(ReviewCluster, result, 8 * MathUtil.Epsilon, 1e-13, "review input");
        }

        [Theory]
        [InlineData(1e-3, 1e-9)]
        [InlineData(1e-4, 1e-12)]
        [InlineData(1e-5, 1e-12)]
        [InlineData(1e-6, 0.0)]
        [InlineData(1e-7, 1e-9)]
        [InlineData(1e-7, 1e-12)]
        public void MinCircle_ThreeTightClusters_EqualExactSolver(double width, double noise)
        {
            var rand = new Random(1000 + (int)(-Math.Log10(width)) * 10 + (noise == 0 ? 0 : (int)(-Math.Log10(noise))));
            for (int n = 0; n < 3000; ++n) {
                Vector2d[] points = ClusterSet(rand, width, noise);
                Circle2d result = new ContMinCircle2(points).Result;
                AssertMinimal(points, result, 32 * MathUtil.Epsilon, 1e-13, $"set {n}");
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void MinCircle_ThreeTightClusters_UnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            var s2 = new Similarity2(s);
            var rand = new Random(77);
            foreach (double width in new[] { 1e-4, 1e-5, 1e-7 }) {
                for (int n = 0; n < 200; ++n) {
                    Vector2d[] points = ClusterSet(rand, width, 1e-12).Select(s2.Apply).ToArray();
                    Circle2d result = new ContMinCircle2(points).Result;
                    // (exact on the mapped points: their rounding is the input here; the center cannot be better than a coordinate)
                    double tol = 32 * s.Scale * s.Resolution(2);
                    AssertMinimal(points, result, tol, tol + 1e-13 * s.Scale, $"{name}, width {width}, set {n}");
                }
            }
        }

        [Fact]
        public void MinCircle_ThreeTightClusters_PowerOfTwoScales_BitIdentical()
        {
            var rand = new Random(78);
            for (int n = 0; n < 200; ++n) {
                Vector2d[] points = ClusterSet(rand, 1e-7, 1e-9);
                Circle2d reference = new ContMinCircle2(points).Result;
                foreach (int exponent in FarExponents) {
                    double scale = Math.Pow(2, exponent);
                    Circle2d result = new ContMinCircle2(points.Select(p => scale * p).ToArray()).Result;
                    Assert.Equal(reference.Center.x * scale, result.Center.x);
                    Assert.Equal(reference.Center.y * scale, result.Center.y);
                    Assert.Equal(reference.Radius * scale, result.Radius);
                }
            }
        }

        static readonly int[] FarExponents = { -900, -600, -345, -10, 10, 345, 900 };

        [Theory]
        [MemberData(nameof(PointSetNames))]
        public void MinCircle_FarPowerOfTwoScales_BitIdentical(string set)
        {
            Vector2d[] input = PointSets()[set];
            Circle2d reference = new ContMinCircle2(input).Result;
            foreach (int exponent in FarExponents) {
                double scale = Math.Pow(2, exponent);
                Circle2d result = new ContMinCircle2(input.Select(p => scale * p).ToArray()).Result;
                Assert.Equal(reference.Center.x * scale, result.Center.x);
                Assert.Equal(reference.Center.y * scale, result.Center.y);
                Assert.Equal(reference.Radius * scale, result.Radius);
            }
        }

        [Theory]
        [InlineData(1e103)]
        [InlineData(1e-103)]
        [InlineData(1e-108)]
        [InlineData(1e-155)]
        [InlineData(1e-162)]
        [InlineData(1e155)]
        [InlineData(1e200)]
        [InlineData(1e-200)]
        [InlineData(1e300)]
        [InlineData(1e-300)]
        public void MinCircle_FarRanges_AreStillTheCircumcircle(double scale)
        {
            // silent garbage in the old code outside about 1e+-103: L^3 and L^2 overflowed or underflowed
            Vector2d[] points = { scale * new Vector2d(0, 0), scale * new Vector2d(1, 0), scale * new Vector2d(0.5, 1) };
            Circle2d result = new ContMinCircle2(points).Result;
            double tol = 8 * MathUtil.Epsilon;
            Assert.True(Math.Abs(result.Radius / scale - 0.625) <= tol, $"radius {result.Radius / scale}");
            Assert.True(Math.Abs(result.Center.x / scale - 0.5) <= tol && Math.Abs(result.Center.y / scale - 0.375) <= tol,
                $"center {result.Center.x / scale}, {result.Center.y / scale}");
        }

        [Theory]
        [InlineData(1e155, 0, -1e155, 0, 0, 0, 1e155)]
        [InlineData(1e-170, 0, 2e-170, 0, 1.5e-170, 0, 0.5e-170)]
        [InlineData(1.2e308, 0, 1.5e308, 0, 1.35e308, 0, 0.15e308)]
        [InlineData(1.2e308, 1e308, 1.5e308, 1e308, 1.35e308, 1e308, 0.15e308)]
        [InlineData(1e308, 1e308, -1e308, -1e308, 0, 0, 1.4142135623730951e308)]
        [InlineData(1e300, 0, 1e300, 1e-20, 1e300, 0.5e-20, 0.5e-20)]
        public void MinCircle_TwoFarPoints(double ax, double ay, double bx, double by, double cx, double cy, double radius)
        {
            foreach (Vector2d[] points in new[] {
                new[] { new Vector2d(ax, ay), new Vector2d(bx, by) }, new[] { new Vector2d(bx, by), new Vector2d(ax, ay) } }) {
                Circle2d result = new ContMinCircle2(points).Result;
                double tol = 4 * MathUtil.Epsilon;
                Assert.True(Math.Abs(result.Radius / radius - 1) <= tol, $"radius {result.Radius}");
                Assert.True(cx == 0 ? result.Center.x == 0 : Math.Abs(result.Center.x / cx - 1) <= tol, $"center {result.Center}");
                Assert.True(cy == 0 ? result.Center.y == 0 : Math.Abs(result.Center.y / cy - 1) <= tol, $"center {result.Center}");
            }
        }

        [Fact]
        public void MinCircle_MixedMagnitudes()
        {
            Vector2d[] points = { new Vector2d(1e300, 0), new Vector2d(-1e300, 0), new Vector2d(0, 1e-300), new Vector2d(1e-300, -1e-300) };
            Circle2d result = new ContMinCircle2(points).Result;
            Assert.Equal(1e300, result.Radius);
            Assert.Equal(new Vector2d(0, 0), result.Center);
        }

        [Fact]
        public void MinCircle_RadiusBeyondTheDoubleRange_IsInfinityAndNotNaN()
        {
            Vector2d[] points = { new Vector2d(1.7e308, 1.7e308), new Vector2d(-1.7e308, -1.7e308) };
            Circle2d result = new ContMinCircle2(points).Result;
            Assert.Equal(new Vector2d(0, 0), result.Center);
            Assert.True(double.IsPositiveInfinity(result.Radius), $"radius {result.Radius}");
        }

        [Fact]
        public void MinCircle_SubnormalPoints_FiniteAndInRange()
        {
            Vector2d[] points = { new Vector2d(1e-320, 0), new Vector2d(3e-320, 0), new Vector2d(2e-320, 1e-320) };
            Circle2d result = new ContMinCircle2(points).Result;
            Assert.True(Math.Abs(result.Radius / 1e-320 - 1) <= 1e-2, $"radius {result.Radius}");
            Assert.True(Math.Abs(result.Center.x / 2e-320 - 1) <= 1e-2, $"center {result.Center}");
        }

        [Fact]
        public void MinCircle_ExtentOfAFewSmallestSubnormals_IsResolved()
        {
            // 2^-1074 is 1 here, after the scaling by the exponent of the extent: nothing falls inside the slack
            double e = double.Epsilon;
            Circle2d result = new ContMinCircle2(new[] { new Vector2d(0, 0), new Vector2d(4 * e, 0) }).Result;
            Assert.Equal(2 * e, result.Center.x);
            Assert.Equal(2 * e, result.Radius);
        }

        [Fact]
        public void MinCircle_ContainsEveryInputPoint_Example()
        {
            // d^2 = 0.18676334276479373 of the farthest point, and sqrt of that, squared, was 0.1867633427647937
            Vector2d[] points = { new Vector2d(0.13436424411240122, 0.8474337369372327),
                new Vector2d(0.763774618976614, 0.2550690257394217), new Vector2d(0.49543508709194095, 0.4494910647887381) };
            Circle2d result = new ContMinCircle2(points).Result;
            foreach (Vector2d p in points)
                Assert.True(result.Contains(p), $"{p} is outside");
        }

        [Fact]
        public void MinCircle_ContainsEveryInputPoint_RandomSetsAndAtMostAFewUlpsMore()
        {
            var rand = new Random(79);
            for (int n = 0; n < 20000; ++n) {
                Vector2d[] points = Enumerable.Range(0, rand.Next(3, 13)).Select(i => new Vector2d(rand.NextDouble(), rand.NextDouble())).ToArray();
                Circle2d result = new ContMinCircle2(points).Result;
                double farthest = points.Max(p => p.Distance(result.Center));
                foreach (Vector2d p in points)
                    Assert.True(result.Contains(p), $"set {n}: {p} is outside");
                Assert.True(result.Radius <= farthest * (1 + 4 * MathUtil.Epsilon), $"set {n}: radius {result.Radius:R}, farthest {farthest:R}");
            }
        }

        [Theory]
        [MemberData(nameof(SimilarityAndPointSet))]
        public void MinCircle_ContainsEveryInputPoint_UnderSimilarity(string name, string set)
        {
            var s2 = new Similarity2(Similarity.ByName(name));
            Vector2d[] points = PointSets()[set].Select(s2.Apply).ToArray();
            Circle2d result = new ContMinCircle2(points).Result;
            foreach (Vector2d p in points)
                Assert.True(result.Contains(p), $"{p} is outside");
        }

        [Theory]
        [InlineData(1e-140)]
        [InlineData(1e-100)]
        [InlineData(1e100)]
        [InlineData(1e154)]
        [InlineData(1e200)]
        public void MinCircle_ContainsEveryInputPoint_FarRanges(double scale)
        {
            var rand = new Random(80);
            for (int n = 0; n < 500; ++n) {
                Vector2d[] points = Enumerable.Range(0, rand.Next(3, 13)).Select(i => new Vector2d(scale * rand.NextDouble(), scale * rand.NextDouble())).ToArray();
                Circle2d result = new ContMinCircle2(points).Result;
                foreach (Vector2d p in points)
                    Assert.True(result.Contains(p), $"set {n}: {p} is outside");
            }
        }

        [Theory]
        [InlineData(16)]    // 16 ulps at this coordinate: the review's input, 1.5e-8 written out
        [InlineData(5)]
        [InlineData(2)]
        public void MinCircle_ExtentOfAFewUlps_IsNotSwallowedByTheCoordinates(int ulps)
        {
            // The slack was relative to the coordinates: below about 22 ulps the second point was "inside" a circle of
            // radius 0, and the center stayed on whichever endpoint came first.
            double ulp = Math.Pow(2, -30);
            Vector2d a = new Vector2d(5712000, 0), b = new Vector2d(5712000 + ulps * ulp, 0);
            Assert.Equal(ulps * ulp, b.x - a.x);
            foreach (Vector2d[] points in new[] { new[] { a, b }, new[] { b, a } }) {
                Circle2d result = new ContMinCircle2(points).Result;
                // the center is a coordinate too: half an ulp more if the midpoint falls between two of them
                double expected = (ulps % 2 == 0) ? 0.5 * ulps * ulp : 0.5 * ulps * ulp + 0.5 * ulp;
                Assert.Equal(expected, result.Radius);
                Assert.True(result.Contains(a) && result.Contains(b));
            }
        }

        [Fact]
        public void MinCircle_SmallTriangleFarFromTheOrigin_EqualExactSolver()
        {
            // an extent of 2^-24 at UTM, 64 ulps: all of its points were inside the slack of the coordinates
            double ulp = Math.Pow(2, -30);
            var rand = new Random(81);
            for (int n = 0; n < 500; ++n) {
                Vector2d[] points = Enumerable.Range(0, rand.Next(3, 8)).Select(i => new Vector2d(
                    632117.25 + rand.Next(0, 65) * ulp, 5712043.5 + rand.Next(0, 65) * ulp)).ToArray();
                Circle2d result = new ContMinCircle2(points).Result;
                AssertMinimal(points, result, 2 * ulp, 2 * ulp, $"set {n}");
            }
        }

        public static IEnumerable<object[]> BadEpsilons()
        {
            foreach (double e in new[] { -1, -1e-5, double.NaN, double.NegativeInfinity, double.PositiveInfinity, 0 })
                yield return new object[] { e };
        }

        static IEnumerable<Vector2d[]> NearlyCollinearSets()
        {
            var rand = new Random(82);
            for (int n = 0; n < 2000; ++n) {
                double angle = rand.NextDouble() * 6.28, height = Math.Pow(10, -rand.Next(0, 17));
                yield return Enumerable.Range(0, rand.Next(3, 16)).Select(i => {
                    double t = rand.Next(0, 9) / 8.0, h = height * (2 * rand.NextDouble() - 1);
                    return new Vector2d(t * Math.Cos(angle) - h * Math.Sin(angle), t * Math.Sin(angle) + h * Math.Cos(angle));
                }).ToArray();
            }
        }

        [Theory]
        [MemberData(nameof(BadEpsilons))]
        public void MinCircle_NoEpsilonGivesNaN_NegativeOrNaNGiveTheDefault(double epsilon)
        {
            foreach (Vector2d[] points in NearlyCollinearSets()) {
                Circle2d result = new ContMinCircle2(points, epsilon).Result;
                Assert.True(MathUtil.IsFinite(result.Center.x) && MathUtil.IsFinite(result.Center.y) && MathUtil.IsFinite(result.Radius),
                    $"epsilon {epsilon}: center {result.Center}, radius {result.Radius}");
                foreach (Vector2d p in points)
                    Assert.True(result.Contains(p), $"epsilon {epsilon}: {p} is outside");
                if (epsilon < 0 || double.IsNaN(epsilon)) {
                    Circle2d standard = new ContMinCircle2(points).Result;
                    Assert.Equal(standard.Radius, result.Radius);
                    Assert.Equal(standard.Center, result.Center);
                }
            }
        }

        [Fact]
        public void MinCircle_NearlyCollinearSets_EqualExactSolver()
        {
            // A point within the slack of 16 eps of the extent counts as inside. (The center of a set this thin is not sharply
            // defined by its radius, so AssertMinimal gets no bound on it here.)
            int n = 0;
            foreach (Vector2d[] points in NearlyCollinearSets()) {
                Circle2d result = new ContMinCircle2(points).Result;
                AssertMinimal(points, result, 32 * MathUtil.Epsilon * MaxCoord(points), 1, $"set {n++}");
            }
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(-1e-5)]
        [InlineData(-1.0)]
        public void MinCircle_NegativeOrNaNEpsilon_ExactlyCollinearTriple_IsNotNaN(double epsilon)
        {
            // Found by fuzzing: near-collinear triples give huge circles, whose rounding then lets an exactly collinear
            // triple through to 0.5 / det, as |det| <= epsilon * x is false at det = 0. The center was NaN.
            Vector2d[] points = {
                new Vector2d(1.6370932387488324E-11, -4.9112769406889356E-11), new Vector2d(1.4551970739518083E-11, -4.3655690173949324E-11),
                new Vector2d(3.638034318242944E-12, -1.0913880910123908E-11), new Vector2d(3.6379510515160973E-12, -1.0913908665699523E-11),
                new Vector2d(1.6370904631912708E-11, -4.9112658384586894E-11), new Vector2d(5.456995966213185E-12, -1.6370932387488324E-11),
                new Vector2d(3.6379510515160973E-12, -1.0913908665699523E-11), new Vector2d(7.275985369759042E-12, -2.182792835370151E-11) };
            Circle2d result = new ContMinCircle2(points, epsilon).Result;
            AssertMinimal(points, result, 16 * MathUtil.Epsilon * 2e-11, 1e-13 * 1e-11, $"epsilon {epsilon}");
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(-1e-5)]
        [InlineData(0.0)]
        public void MinCircle_CircleThrough_CollinearTriple_IsTheCircleOverTheLongestEdge(double epsilon)
        {
            // By reflection: the scaled points no longer reach this path with det = 0 (no NaN in 278 million near-collinear
            // sets without the guard on epsilon), so no public input exercises that guard.
            var method = typeof(ContMinCircle2).GetMethod("CircleThrough",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            object[] args = { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(0.25, 0), null, null };
            method.Invoke(new ContMinCircle2(new[] { new Vector2d(0, 0) }, epsilon), args);
            Assert.Equal(new Vector2d(0.5, 0), (Vector2d)args[3]);
            Assert.Equal(0.25, (double)args[4]);
        }

        // A point is doubled. At the shipped slack of 16 eps no input was found where the copy counts as outside; at 4 eps,
        // without the copy check in ContMinCircle2, the first two sets failed through that.
        public static IEnumerable<object[]> DuplicateSets()
        {
            yield return new object[] { new[] {
                new Vector2d(0.5011188758076722, 0.6514758242533896), new Vector2d(0.7880659125689724, 0.9612834299734251),
                new Vector2d(0.016571717810151966, 0.6469553055460356), new Vector2d(0.40097002098381984, 0.28854680959533285),
                new Vector2d(0.7880659125689724, 0.9612834299734251) } };
            yield return new object[] { new[] {
                new Vector2d(0.5714285714285705, 0.8571428571428562), new Vector2d(0.5714285714285705, 0.8571428571428562),
                new Vector2d(0.5714285714285714, 1.1428571428571428), new Vector2d(0.2857142857142857, 0.7142857142857143),
                new Vector2d(1, 0.8571428571428571), new Vector2d(0.42857142857142855, 0.42857142857142855),
                new Vector2d(1, 0.8571428571428571), new Vector2d(0.2857142857142857, 1), new Vector2d(0.2857142857142857, 1),
                new Vector2d(0.5714285714285714, 1), new Vector2d(0.5714285714285714, 0.8571428571428571) } };
            // not doubled exactly: two points 2 ulps apart
            yield return new object[] { new[] {
                new Vector2d(0.7142857142857143, 0.7142857142857143), new Vector2d(0.5714285714285714, 1.1428571428571428),
                new Vector2d(0.7142857142857145, 0.2857142857142857), new Vector2d(1, 0.5714285714285714),
                new Vector2d(0.7142857142857143, 0.2857142857142857), new Vector2d(1.1428571428571428, 1),
                new Vector2d(1, 0.8571428571428571) } };
        }

        [Theory]
        [MemberData(nameof(DuplicateSets))]
        public void MinCircle_CopyOfASupportPoint_KeepsTheThirdPoint(Vector2d[] input)
        {
            // Without the skip of copies, with a slack of 4 eps: the first set had radius 0.51557, exact 0.42763. The copy was
            // falsely outside by 1e-15, its triple has det = 0, and the circle over the edge dropped the third point.
            foreach (Similarity s in Similarity.All()) {
                var s2 = new Similarity2(s);
                Vector2d[] points = input.Select(s2.Apply).ToArray();
                double tol = 32 * s.Scale * s.Resolution(2);
                AssertMinimal(points, new ContMinCircle2(points).Result, tol, 1e5 * tol, s.Name);
            }
        }

        [Theory]
        [InlineData(48, 8)]     // outside the slack: the circle through the apex, radius 1 + delta^2
        [InlineData(16, 40)]    // inside the slack of 16 eps of the extent (2), so 32 eps: the radius grows to reach the apex
        public void MinCircle_ThePointOutsideTheSlack_IsNotIgnored(int deltaEps, int excessEps)
        {
            // (-1, 0), (1, 0) and an apex at 1 + delta: the circle through all three has radius about 1, whereas leaving the
            // center on the edge has 1 + delta.
            Vector2d a = new Vector2d(-1, 0), b = new Vector2d(1, 0), c = new Vector2d(0, 1 + deltaEps * MathUtil.Epsilon);
            foreach (Vector2d[] points in new[] {
                new[] { a, b, c }, new[] { a, c, b }, new[] { b, a, c }, new[] { b, c, a }, new[] { c, a, b }, new[] { c, b, a } }) {
                Circle2d result = new ContMinCircle2(points).Result;
                double excess = result.Radius - new ExactCircle(points).Radius;
                Assert.True(excess <= excessEps * MathUtil.Epsilon, $"excess {excess / MathUtil.Epsilon} eps");
                Assert.True(result.Contains(a) && result.Contains(b) && result.Contains(c));
            }
        }

        public static IEnumerable<object[]> SortedSetNames()
        {
            foreach (string name in new[] { "line", "half circle", "parabola" })
                yield return new object[] { name };
        }

        static Vector2d[] SortedSet(string name, int n)
        {
            return Enumerable.Range(0, n).Select(i => {
                double t = (double)i / n;
                return name == "line" ? new Vector2d(t, 0.5 * t)
                    : name == "half circle" ? new Vector2d(Math.Cos(Math.PI * t), Math.Sin(Math.PI * t))
                    : new Vector2d(t, t * t);
            }).ToArray();
        }

        [Theory]
        [MemberData(nameof(SortedSetNames))]
        public void MinCircle_SortedInput_TestsPerPointStayBounded(string name)
        {
            // Sorted input is the worst order for the incremental loops: without the shuffle every point is outside, so it is
            // quadratic or worse. Counted, not timed, as other jobs may share the machine.
            // (per point at n and 4n, not their ratio: a single shuffle at 1000 can be lucky, 1.6 tests per point on these sets)
            foreach (int n in new[] { 1000, 4000 }) {
                long tests = new ContMinCircle2(SortedSet(name, n)).ContainsTests;
                Assert.True(tests <= 40 * n, $"{tests} tests for {n} points");
            }
        }

        [Fact]
        public void MinCircle_ReusesNoState_ComputedTwiceFromTheSameInput()
        {
            // no Compute(): the constructor is the only entry; the input must stay as it was
            Vector2d[] points = ClusterSet(new Random(83), 1e-7, 1e-9);
            Vector2d[] copy = points.ToArray();
            Circle2d first = new ContMinCircle2(points).Result;
            Assert.Equal(copy, points);
            Circle2d second = new ContMinCircle2(points).Result;
            Assert.Equal(first.Center, second.Center);
            Assert.Equal(first.Radius, second.Radius);
        }


        // ---- S19: QuadricError.OptimalPoint ----

        static readonly Vector3d QuadricPoint = new Vector3d(0.25, 0.5, 0.75);

        /// <summary>Area-weighted plane quadrics of planes with the given normals through a common point (A is L^2, as in Reducer).</summary>
        static QuadricError PlaneQuadrics(Similarity s, Vector3d point, params Vector3d[] normals)
        {
            QuadricError q = QuadricError.Zero;
            foreach (Vector3d normal in normals) {
                Vector3d n = s.ApplyDirection(normal.Normalized);
                Vector3d p = s.Apply(point);
                var plane = new QuadricError(ref n, ref p);
                q.Add(s.Scale * s.Scale, ref plane);
            }
            return q;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void QuadricOptimalPoint_FindsThePlanesCommonPointUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            QuadricError q = PlaneQuadrics(s, QuadricPoint,
                Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ, new Vector3d(1, 1, 1));
            Vector3d result = Vector3d.Zero;
            Assert.True(q.OptimalPoint(ref result), name);
            double error = s.Inverse(result).Distance(QuadricPoint);
            Assert.True(error <= 64 * s.Resolution(1), $"{name}: error {error}");
        }

        [Fact]
        public void QuadricOptimalPoint_PowerOfTwoScales_BitIdentical()
        {
            Vector3d reference = Vector3d.Zero;
            Vector3d[] normals = { new Vector3d(1, 2, 2), new Vector3d(-2, 1, 3), new Vector3d(3, -1, 1), new Vector3d(1, 1, -4) };
            Assert.True(PlaneQuadrics(Similarity.Identity, QuadricPoint, normals).OptimalPoint(ref reference));
            foreach (int exponent in new[] { -24, -10, 10, 30 }) {
                var s = new Similarity("2^" + exponent, Math.Pow(2, exponent), Matrix3d.Identity, Vector3d.Zero);
                Vector3d result = Vector3d.Zero;
                Assert.True(PlaneQuadrics(s, QuadricPoint, normals).OptimalPoint(ref result), s.Name);
                Assert.Equal(reference * s.Scale, result);
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void QuadricOptimalPoint_RankDeficient_RefusedAtEveryScale(string name)
        {
            // Two planes do not determine a point. These normals leave rounding noise in the determinant, which at
            // large scale was above the old absolute threshold.
            Similarity s = Similarity.ByName(name);
            Vector3d result = Vector3d.Zero;
            QuadricError q = PlaneQuadrics(s, QuadricPoint, new Vector3d(0.6, 0.8, 0.1), new Vector3d(0.1, 0.6, 0.8));
            Assert.False(q.OptimalPoint(ref result), name);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void QuadricOptimalPoint_NearlySingular_SameDecisionAtEveryScale(string name)
        {
            // Three planes whose normals are within angle a of one plane: det/(trace/3)^3 is of order a^2. The first
            // set is above the threshold of 1000 * Epsilon, the second below it.
            Similarity s = Similarity.ByName(name);
            Vector3d[] Normals(double a) => new[] { new Vector3d(1, 0, a), new Vector3d(0, 1, a), new Vector3d(-1, -1, a) };
            Vector3d result = Vector3d.Zero;
            Assert.True(PlaneQuadrics(s, QuadricPoint, Normals(1e-3)).OptimalPoint(ref result), name);
            Assert.False(PlaneQuadrics(s, QuadricPoint, Normals(1e-9)).OptimalPoint(ref result), name);
        }

        [Fact]
        public void QuadricOptimalPoint_DifferentPlaneWeights_AreSolvedUpToAFactorOfAbout1e7()
        {
            // orthogonal planes with weights w, 1, 1 through (1, 2, 3): det / (trace/3)^3 is about 27 / w^2. The price of a
            // threshold that also refuses nearly coplanar normals, whose diagonal can look just as uneven.
            // The boundary is at 1.1e7.
            Func<double, QuadricError> planes = w => new QuadricError { Axx = w, Ayy = 1, Azz = 1, bx = -w, by = -2, bz = -3 };
            Vector3d result = Vector3d.Zero;
            foreach (double weight in new[] { 1e6, 1e7 }) {
                QuadricError accepted = planes(weight);
                Assert.True(accepted.OptimalPoint(ref result), $"{weight}");
                Assert.True(result.Distance(new Vector3d(1, 2, 3)) <= 8 * MathUtil.Epsilon, $"{weight}: {result}");
            }
            foreach (double weight in new[] { 2e7, 1e8 }) {
                QuadricError refused = planes(weight);
                Assert.False(refused.OptimalPoint(ref result), $"{weight}");
            }
        }

        // Normals within angle a of the xy plane: A is (N/3) times that of the first three, whose det/(trace/3)^3 is about 5 a^2.
        static QuadricError FlatUnitPlanes(int triples, double a)
        {
            QuadricError q = QuadricError.Zero;
            for (int i = 0; i < triples; ++i) {
                foreach (Vector3d normal in new[] { new Vector3d(1, 0, a), new Vector3d(0, 1, a), new Vector3d(-1, -1, a) }) {
                    Vector3d n = normal.Normalized, p = QuadricPoint;
                    var plane = new QuadricError(ref n, ref p);
                    q.Add(1, ref plane);
                }
            }
            return q;
        }

        [Fact]
        public void QuadricOptimalPoint_DecidesOnTheShapeOfA_NotItsTotalWeight()
        {
            // The documented limit against upstream, which compared det with 1000 * Epsilon absolutely: at total weight
            // N the threshold is (N/3)^3 times the old one, so 1000 planes of tilt 1e-7 are refused, and were solved.
            Vector3d result = Vector3d.Zero;
            foreach (int triples in new[] { 1, 333 }) {
                QuadricError q = FlatUnitPlanes(triples, 1e-7);
                Assert.False(q.OptimalPoint(ref result), $"{triples}");
                q = FlatUnitPlanes(triples, 1e-5);
                Assert.True(q.OptimalPoint(ref result), $"{triples}");
            }
            QuadricError heavy = FlatUnitPlanes(333, 1e-7);
            double det = heavy.Axx * (heavy.Ayy * heavy.Azz - heavy.Ayz * heavy.Ayz)
                - heavy.Axy * (heavy.Axy * heavy.Azz - heavy.Axz * heavy.Ayz)
                + heavy.Axz * (heavy.Axy * heavy.Ayz - heavy.Axz * heavy.Ayy);
            Assert.True(det > 1000 * MathUtil.Epsilon, $"det {det}");
        }

        static QuadricError ScaledIdentityQuadric(double weight) =>
            new QuadricError { Axx = weight, Ayy = weight, Azz = weight, bx = -weight, by = -2 * weight, bz = -3 * weight };

        [Theory]
        [InlineData(3e-103)]
        [InlineData(1e-102)]
        [InlineData(1e-100)]
        [InlineData(1e-3)]
        [InlineData(1)]
        [InlineData(1e3)]
        [InlineData(1e100)]
        [InlineData(1e102)]
        [InlineData(5e102)]
        public void QuadricOptimalPoint_SizesWithAUsableInverse_AreSolved(double weight)
        {
            QuadricError q = ScaledIdentityQuadric(weight);
            Vector3d result = new Vector3d(9, 9, 9);
            Assert.True(q.OptimalPoint(ref result));
            Assert.True(result.Distance(new Vector3d(1, 2, 3)) <= 8 * MathUtil.Epsilon, $"{result}");
        }

        [Theory]
        [InlineData(6e102)]     // det overflows, so 1/det is 0: this returned true with (0,0,0)
        [InlineData(1e103)]
        [InlineData(1e104)]
        [InlineData(1e106)]
        [InlineData(9e106)]
        [InlineData(1e108)]     // from about here the threshold overflows too
        [InlineData(1.7e-103)]  // det is so far subnormal that 1/det overflows: this returned true with NaN
        [InlineData(1e-103)]
        [InlineData(1e-105)]
        [InlineData(1e-107)]
        [InlineData(1e-110)]    // det is 0
        public void QuadricOptimalPoint_SizesWithoutAUsableInverse_AreRefused(double weight)
        {
            QuadricError q = ScaledIdentityQuadric(weight);
            Vector3d result = new Vector3d(9, 9, 9);
            Assert.False(q.OptimalPoint(ref result), $"{result}");
            Assert.Equal(new Vector3d(9, 9, 9), result);
        }

        [Fact]
        public void QuadricOptimalPoint_OneSmallEigenvalue_IsAcceptedAtACondition1e13()
        {
            // The documented asymmetry: a condition of 2e7 from one large eigenvalue is refused (the test above).
            QuadricError q = new QuadricError { Axx = 1, Ayy = 1, Azz = 1e-13, bx = -1, by = -2, bz = -3e-13 };
            Vector3d result = Vector3d.Zero;
            Assert.True(q.OptimalPoint(ref result));
            Assert.True(result.Distance(new Vector3d(1, 2, 3)) <= 1e-12, $"{result}");
        }

        [Fact]
        public void QuadricOptimalPoint_NegativeDeterminant_IsSolved()
        {
            QuadricError q = new QuadricError { Axx = -1, Ayy = -1, Azz = -1, bx = 1, by = 2, bz = 3 };
            Vector3d result = Vector3d.Zero;
            Assert.True(q.OptimalPoint(ref result));
            Assert.Equal(new Vector3d(1, 2, 3), result);
        }

        [Fact]
        public void QuadricOptimalPoint_NegativeDiagonal_SizeIsNotTheSignedTrace()
        {
            // det = -1e-14 against the absolute diagonal (size 2/3): nearly singular. The signed trace would be 1e-14, and accept it.
            QuadricError q = new QuadricError { Axx = 1, Ayy = -1, Azz = 1e-14, bx = 1, by = 2, bz = 3 };
            Vector3d result = new Vector3d(9, 9, 9);
            Assert.False(q.OptimalPoint(ref result), $"{result}");
            Assert.Equal(new Vector3d(9, 9, 9), result);
        }

        [Theory]
        [InlineData(0.5, 1e308, 1, 1)]
        [InlineData(0.5, 1, 1e308, 1)]
        [InlineData(0.5, 1, 1, 1e308)]
        [InlineData(0.5, 1e308, 1e308, 1e308)]
        [InlineData(1, double.PositiveInfinity, 0, 0)]
        [InlineData(1, double.NaN, 0, 0)]
        [InlineData(1, 0, double.NegativeInfinity, 0)]
        [InlineData(1, 0, 0, double.NaN)]
        public void QuadricOptimalPoint_NonFiniteSolution_IsRefused(double weight, double bx, double by, double bz)
        {
            QuadricError q = new QuadricError { Axx = weight, Ayy = weight, Azz = weight, bx = bx, by = by, bz = bz };
            Vector3d result = new Vector3d(9, 9, 9);
            Assert.False(q.OptimalPoint(ref result), $"{result}");
            Assert.Equal(new Vector3d(9, 9, 9), result);
        }

        [Theory]
        [InlineData(5e102, -5e102, -1e103, -1.5e103)]       // the products overflow before the division by det
        [InlineData(1e-103, -1e-103, -2e-103, -3e-103)]     // 1/det is infinite
        [InlineData(6e102, -6e102, -1.2e103, -1.8e103)]
        [InlineData(1e103, 0, 0, 0)]                        // 1/det is 0: this gave a solution of (0,0,0)
        [InlineData(0.5, 1e308, 1, 1)]
        [InlineData(0.5, 1, 1e308, 1)]
        [InlineData(0.5, 1, 1, 1e308)]
        [InlineData(1e-3, 1e308, 1e308, 1e308)]
        [InlineData(1, double.NaN, 0, 0)]
        [InlineData(1, 0, double.NaN, 0)]
        [InlineData(1, 0, 0, double.NaN)]
        [InlineData(1, double.PositiveInfinity, 0, 0)]
        public void QuadricRegularizedPoint_NoFiniteSolution_IsRefused(double weight, double bx, double by, double bz)
        {
            QuadricError q = new QuadricError { Axx = weight, Ayy = weight, Azz = weight, bx = bx, by = by, bz = bz };
            Vector3d result = new Vector3d(9, 9, 9);
            Assert.False(q.OptimalPoint(ref result, new Vector3d(1, 1, 1), 1e-6), $"{result}");
            Assert.Equal(new Vector3d(9, 9, 9), result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void QuadricRegularizedPoint_OverflowOfOneAxisInTheLastStep_IsRefused(int axis)
        {
            // Earlier overflows spread to all axes by 0 * infinity in the next step. A b for which only the third step's
            // product overflows: its residual is b (1 + q + q^2), the second's b (1 + q), with q = lambda / (1 + lambda).
            double lambda = 1e-3, q = lambda / (1 + lambda);
            double b = double.MaxValue / ((1 + lambda) * (1 + lambda) * (1 + q + 0.5 * q * q));
            QuadricError error = new QuadricError { Axx = 1, Ayy = 1, Azz = 1, bx = axis == 0 ? b : 1, by = axis == 1 ? b : 1, bz = axis == 2 ? b : 1 };
            Vector3d result = new Vector3d(9, 9, 9);
            Assert.False(error.OptimalPoint(ref result, Vector3d.Zero, lambda), $"{result}");
            Assert.Equal(new Vector3d(9, 9, 9), result);
        }

        [Theory]
        [InlineData(1e-90)]
        [InlineData(1e-3)]
        [InlineData(1)]
        [InlineData(1e3)]
        [InlineData(1e50)]
        public void QuadricRegularizedPoint_OrdinarySizes_AreSolved(double weight)
        {
            QuadricError q = new QuadricError { Axx = weight, Ayy = weight, Azz = weight, bx = -weight, by = -2 * weight, bz = -3 * weight };
            Vector3d result = Vector3d.Zero;
            Assert.True(q.OptimalPoint(ref result, new Vector3d(1, 1, 1), 1e-6));
            Assert.True(result.Distance(new Vector3d(1, 2, 3)) <= 1e-9, $"{result}");
        }

        public static IEnumerable<object[]> ReducerScales()
        {
            foreach (double s in new[] { 1e-150, 1e-100, 1e-90, 1e-80, 1e-70, 1e-60, 1e-50, 1e-30, 1e-10, 1, 1e10, 1e30, 1e35, 1e40, 1e43, 1e45, 1e50, 1e51, 1e52, 3e52, 1e53, 1e55, 1e60, 1e70, 1e100 })
                yield return new object[] { s };
        }

        [Theory]
        [MemberData(nameof(ReducerScales))]
        public void Reducer_SphereAtFarScales_KeepsFiniteVerticesAndAManifold(double scale)
        {
            // The overload of OptimalPoint that the Reducer calls gave NaN where the products overflow: this result was not
            // valid from 1e45 to 3e52. It now refuses, so the collapse takes an endpoint or the midpoint of the edge.
            // Below about 1e-30 (2^-100) the quadric arithmetic of an area weighted plane (products of up to the sixth power of the extent)
            // runs out of the double range: the reduction may stop short of the target (752 triangles at 1e-50, 972 below 1e-90), a valid mesh left
            // as it is. Within 2^+-128 it is exact under scaling (ReducerScaleInvarianceTests).
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            foreach (int vid in mesh.VertexIndices())
                mesh.SetVertex(vid, scale * mesh.GetVertex(vid));
            Assert.Equal(972, mesh.TriangleCount);

            new Reducer(mesh).ReduceToTriangleCount(486);

            if (scale >= 1e-30)
                Assert.True(mesh.TriangleCount <= 486, $"{mesh.TriangleCount} triangles");
            else
                Assert.True(mesh.TriangleCount <= 972, $"{mesh.TriangleCount} triangles");
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly), "not valid");
            Assert.True(mesh.IsClosed(), "not closed");
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                Assert.True(MathUtil.IsFinite(v.x) && MathUtil.IsFinite(v.y) && MathUtil.IsFinite(v.z), $"vertex {vid} is {v}");
                double r = (v / scale).Length;
                Assert.True(r >= 0.5 && r <= 1.5, $"vertex {vid} at radius {r} of the sphere");
            }
        }

        [Fact]
        public void QuadricOptimalPoint_EmptyQuadric_Refused()
        {
            Vector3d result = new Vector3d(1, 2, 3);
            QuadricError q = QuadricError.Zero;
            Assert.False(q.OptimalPoint(ref result));
            Assert.Equal(new Vector3d(1, 2, 3), result);
        }


        // ---- S8: PlaneIntersectionTarget ----

        // (float-exact, and so is its sum with the small plane origin below)
        static readonly Vector3d UtmPlaneOrigin = new Vector3d(632117.25, 5712043.5, 113.75);

        [Fact]
        public void PlaneTarget_Utm_AxisAlignedPlanes_ExactHit()
        {
            // the ray origin is not representable in float: x and y moved by up to 0.25 when cast
            var origin = new Vector3d(632117.3, 5712043.7, 120.1);
            var frame = new Frame3f((Vector3f)UtmPlaneOrigin);

            var z = new PlaneIntersectionTarget() { PlaneFrame = frame, NormalAxis = 2 };
            Vector3d hit, normal;
            Assert.True(z.RayIntersect(new Ray3d(origin, -Vector3d.AxisZ), out hit, out normal));
            Assert.Equal(new Vector3d(origin.x, origin.y, 113.75), hit);
            Assert.Equal(Vector3d.AxisZ, normal);

            var x = new PlaneIntersectionTarget() { PlaneFrame = frame, NormalAxis = 0 };
            Assert.True(x.RayIntersect(new Ray3d(origin, -Vector3d.AxisX), out hit, out normal));
            Assert.Equal(new Vector3d(632117.25, origin.y, origin.z), hit);
            Assert.Equal(Vector3d.AxisX, normal);

            var y = new PlaneIntersectionTarget() { PlaneFrame = frame, NormalAxis = 1 };
            Assert.True(y.RayIntersect(new Ray3d(origin, -Vector3d.AxisY), out hit, out normal));
            Assert.Equal(new Vector3d(origin.x, 5712043.5, origin.z), hit);
            Assert.Equal(Vector3d.AxisY, normal);
        }

        [Fact]
        public void PlaneTarget_UnitScale_TiltedRay_ExactHit()
        {
            // z: 10 - 0.8 t = 3, so t = 8.75 and x = 1 + 0.6 t = 6.25
            var target = new PlaneIntersectionTarget() { PlaneFrame = new Frame3f(new Vector3f(0, 0, 3)) };
            Vector3d hit, normal;
            Assert.True(target.RayIntersect(new Ray3d(new Vector3d(1, 2, 10), new Vector3d(0.6, 0, -0.8), true), out hit, out normal));
            Assert.True(hit.Distance(new Vector3d(6.25, 2, 3)) <= 8 * MathUtil.Epsilon * 10, $"hit {hit}");
        }

        static PlaneIntersectionTarget TiltedTarget(Similarity s)
        {
            var origin = new Vector3d(1, 2, 3);
            var normal = new Vector3d(1, 2, 2).Normalized;
            return new PlaneIntersectionTarget() {
                PlaneFrame = new Frame3f((Vector3f)s.Apply(origin), (Vector3f)s.ApplyDirection(normal))
            };
        }

        static Ray3d TiltedRay(Similarity s)
        {
            return new Ray3d(s.Apply(new Vector3d(7.1, -3.3, 11.7)), s.ApplyDirection(new Vector3d(-0.3, 0.5, -1).Normalized));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void PlaneTarget_HitUnderSimilarity_OnPlaneAndOnRay(string name)
        {
            Similarity s = Similarity.ByName(name);
            PlaneIntersectionTarget target = TiltedTarget(s);
            Ray3d ray = TiltedRay(s);
            Vector3d hit, normal;
            Assert.True(target.RayIntersect(ray, out hit, out normal), name);

            // The plane as it is stored (its origin and normal are floats) and the ray, mapped back to the input's
            // units, where they can be intersected without any of the large coordinates.
            Matrix3d back = s.Rotation.Transpose();
            Vector3d p = s.Inverse(target.PlaneFrame.Origin), n = back * (Vector3d)target.PlaneFrame.GetAxis(2);
            Vector3d o = s.Inverse(ray.Origin), d = back * ray.Direction;
            Vector3d expected = o + ((p - o).Dot(n) / d.Dot(n)) * d;

            double tol = 32 * s.Resolution(20);
            Vector3d mapped = s.Inverse(hit);
            Assert.True(mapped.Distance(expected) <= tol, $"{name}: hit {mapped}, expected {expected}, tol {tol}");
            Assert.True(Math.Abs((mapped - p).Dot(n)) <= tol, $"{name}: not on the plane");
            Assert.True((mapped - o).Cross(d).Length <= tol, $"{name}: not on the ray");
            Assert.True(normal.Distance(target.PlaneFrame.GetAxis(2)) == 0, name);
        }

        [Fact]
        public void PlaneTarget_PowerOfTwoScales_BitIdentical()
        {
            Vector3d reference, referenceNormal;
            Assert.True(TiltedTarget(Similarity.Identity).RayIntersect(TiltedRay(Similarity.Identity), out reference, out referenceNormal));
            foreach (Similarity s in Similarity.All().Where(s => s.IsExact)) {
                Vector3d hit, normal;
                Assert.True(TiltedTarget(s).RayIntersect(TiltedRay(s), out hit, out normal), s.Name);
                Assert.Equal(reference * s.Scale, hit);
                Assert.Equal(referenceNormal, normal);
            }
        }

        [Fact]
        public void PlaneTarget_FiniteHitWhoseCoordinatesSumToInfinity_IsAHit()
        {
            var target = new PlaneIntersectionTarget() { PlaneFrame = new Frame3f(new Vector3f(0, 0, 0)) };
            Vector3d hit, normal;
            Assert.True(target.RayIntersect(new Ray3d(new Vector3d(1e308, 1e308, 1), new Vector3d(0, 0, -1), true), out hit, out normal));
            Assert.Equal(new Vector3d(1e308, 1e308, 0), hit);
        }

        [Theory]
        [InlineData(2, 1.7e308, 0, 1e308, 0.6, 0, -0.8)]    // only x overflows
        [InlineData(2, 0, 1.7e308, 1e308, 0, 0.6, -0.8)]    // only y
        [InlineData(0, 1e308, 0, 1.7e308, -0.8, 0, 0.6)]    // only z
        public void PlaneTarget_HitBeyondTheDoubleRange_NoHit(int axis, double ox, double oy, double oz, double dx, double dy, double dz)
        {
            var target = new PlaneIntersectionTarget() { PlaneFrame = new Frame3f(new Vector3f(0, 0, 0)), NormalAxis = axis };
            Vector3d hit, normal;
            Assert.False(target.RayIntersect(new Ray3d(new Vector3d(ox, oy, oz), new Vector3d(dx, dy, dz), true), out hit, out normal));
            Assert.Equal((Vector3d)Vector3f.Invalid, hit);
        }

        [Fact]
        public void PlaneTarget_NearlyParallelRay_ThresholdOnBothSides()
        {
            // the threshold on the direction's part along the normal is 1e-6
            var target = new PlaneIntersectionTarget() { PlaneFrame = new Frame3f(new Vector3f(0, 0, 0)) };
            Vector3d hit, normal;
            double d = 1e-4;
            Assert.True(target.RayIntersect(new Ray3d(new Vector3d(0, 0, 1), new Vector3d(Math.Sqrt(1 - d * d), 0, -d), true), out hit, out normal));
            Assert.True(Math.Abs(hit.z) <= 1e-11 && Math.Abs(hit.x - 1e4) <= 1, $"hit {hit}");
            d = 1e-7;
            Assert.False(target.RayIntersect(new Ray3d(new Vector3d(0, 0, 1), new Vector3d(Math.Sqrt(1 - d * d), 0, -d), true), out hit, out normal));
            Assert.Equal((Vector3d)Vector3f.Invalid, hit);
        }

        [Fact]
        public void PlaneTarget_ParallelRay_NoHit()
        {
            var target = new PlaneIntersectionTarget() { PlaneFrame = new Frame3f(new Vector3f(0, 0, 3)) };
            Vector3d hit, normal;
            Assert.False(target.RayIntersect(new Ray3d(new Vector3d(1, 2, 10), Vector3d.AxisX), out hit, out normal));
            Assert.Equal(Vector3d.AxisZ, normal);
            Assert.Equal((Vector3d)Vector3f.Invalid, hit);
        }

        [Theory]
        [InlineData(double.NaN, 0, 10, 0, 0, -1)]
        [InlineData(0, 0, double.PositiveInfinity, 0, 0, -1)]
        [InlineData(0, 0, 10, double.NaN, 0, -1)]
        [InlineData(0, double.NaN, 10, 0, 0, -1)]
        public void PlaneTarget_NonFiniteRay_NoHit(double ox, double oy, double oz, double dx, double dy, double dz)
        {
            var target = new PlaneIntersectionTarget() { PlaneFrame = new Frame3f(new Vector3f(0, 0, 3)) };
            Vector3d hit, normal;
            var ray = new Ray3d(new Vector3d(ox, oy, oz), new Vector3d(dx, dy, dz), true);
            Assert.False(target.RayIntersect(ray, out hit, out normal));
            Assert.Equal((Vector3d)Vector3f.Invalid, hit);
        }
    }
}
