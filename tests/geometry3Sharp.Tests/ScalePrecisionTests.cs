using System;
using System.Collections.Generic;
using System.Linq;
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
            // the center of a needle is the end of a lever: rounding of its short edge (1e-6) moves it by 1/1e-6 of that
            double centerTol = set == "needle" ? 1e6 * tol : tol;
            Assert.True(s2.Inverse(result.Center).Distance(expected.Center) <= centerTol,
                $"center {s2.Inverse(result.Center)} expected {expected.Center}, tol {centerTol}");
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
            Func<double, QuadricError> planes = w => new QuadricError { Axx = w, Ayy = 1, Azz = 1, bx = -w, by = -2, bz = -3 };
            Vector3d result = Vector3d.Zero;
            QuadricError accepted = planes(1e6);
            Assert.True(accepted.OptimalPoint(ref result));
            Assert.Equal(new Vector3d(1, 2, 3), result);
            QuadricError refused = planes(1e8);
            Assert.False(refused.OptimalPoint(ref result));
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
