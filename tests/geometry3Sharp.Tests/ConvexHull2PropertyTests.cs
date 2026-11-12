using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 gap list (thin tests): seeded properties of ConvexHull2 against an exact monotone chain (BigInteger
    /// orientation of the doubles' integer images). Point sets: random, on a grid (collinear points, duplicates), on a
    /// circle, near-collinear, and a thin sliver, at binary scales 2^-300 .. 2^300 and moved away from the origin.
    /// QT_DOUBLE on the exact-friendly families (random, grid) is compared with the chain; QT_DOUBLE and QT_INT64 on all
    /// families must give a valid ring of indices that contains every point up to a tolerance (the fast modes round).
    /// QT_RATIONAL, QT_FILTERED and QT_INTEGER throw NotImplementedException (pinned). Epsilon: the point test is absolute
    /// (extent below epsilon), the line test relative; the tests pass 1e-12 times the extent for the first.
    /// </summary>
    public class ConvexHull2PropertyTests
    {
        static BigInteger I(double v) => QuadtreeGeometry2d.Integer(v);

        static int Orient(Vector2d a, Vector2d b, Vector2d c)
        {
            BigInteger det = (I(b.x) - I(a.x)) * (I(c.y) - I(a.y)) - (I(b.y) - I(a.y)) * (I(c.x) - I(a.x));
            return det.Sign;
        }

        /// <summary> the strictly convex hull (no collinear points, no duplicates), counterclockwise, as a set of positions </summary>
        static List<Vector2d> Chain(List<Vector2d> pts)
        {
            var s = pts.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            if (s.Count < 3) return s;
            var hull = new List<Vector2d>();
            for (int pass = 0; pass < 2; ++pass) {
                int start = hull.Count;
                foreach (Vector2d p in s) {
                    while (hull.Count >= start + 2 && Orient(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
                s.Reverse();
            }
            return hull;
        }

        enum Family { Random, Grid, Circle, NearCollinear, Sliver }

        static List<Vector2d> Points(Family f, int n, Random rng)
        {
            var p = new List<Vector2d>();
            for (int i = 0; i < n; ++i)
                switch (f) {
                    case Family.Random: p.Add(new Vector2d(rng.NextDouble(), rng.NextDouble())); break;
                    case Family.Grid: p.Add(new Vector2d(rng.Next(5), rng.Next(5))); break;
                    case Family.Circle: { double a = rng.NextDouble() * 2 * Math.PI; p.Add(new Vector2d(Math.Cos(a), Math.Sin(a))); break; }
                    case Family.NearCollinear: { double t = rng.NextDouble(); p.Add(new Vector2d(t, 2 * t + (rng.Next(3) - 1) * 1e-9 * rng.NextDouble())); break; }
                    default: p.Add(new Vector2d(rng.NextDouble(), rng.NextDouble() * 1e-6)); break;
                }
            return p;
        }

        static List<Vector2d> Place(List<Vector2d> pts, int e, double shift) =>
            pts.Select(p => new Vector2d(ExactPoint3.ScaleB(p.x, e) + shift, ExactPoint3.ScaleB(p.y, e) + shift)).ToList();

        static readonly int[] Exponents = { -40, 0, 30, 300 };

        static double Eps(int e) => ExactPoint3.ScaleB(1e-12, Math.Min(e, 0));

        [Fact]
        public void Double_HullEqualsTheExactChain_AndContainsEveryPoint()
        {
            var rng = new Random(2026111501);
            int checkedHulls = 0;
            foreach (Family fam in new[] { Family.Random, Family.Grid })
                foreach (int e in Exponents)
                    foreach (bool moved in new[] { false, true }) {
                        List<Vector2d> pts = Place(Points(fam, 40, rng), e, moved ? ExactPoint3.ScaleB(1.0, e + 6) : 0);
                        List<Vector2d> chain = Chain(pts);
                        var hull = new ConvexHull2(pts, Eps(e), QueryNumberType.QT_DOUBLE);
                        Assert.Equal(2, hull.Dimension);
                        int[] idx = hull.HullIndices;
                        Assert.Equal(idx.Length, idx.Distinct().Count());
                        var ring = idx.Select(i => pts[i]).ToList();
                        for (int k = 0; k < ring.Count; ++k) {
                            Vector2d a = ring[k], b = ring[(k + 1) % ring.Count];
                            foreach (Vector2d p in pts)
                                Assert.True(Orient(a, b, p) >= 0, $"{fam} 2^{e} moved {moved}: a point is outside the hull edge {k}");
                        }
                        // every extreme point is a hull vertex; any other hull vertex lies on a chain edge
                        foreach (Vector2d c in chain)
                            Assert.True(ring.Contains(c), $"{fam} 2^{e} moved {moved}: extreme point missing");
                        foreach (Vector2d r in ring.Where(r => !chain.Contains(r))) {
                            bool onEdge = false;
                            for (int k = 0; k < chain.Count; ++k) onEdge |= Orient(chain[k], chain[(k + 1) % chain.Count], r) == 0;
                            Assert.True(onEdge, $"{fam} 2^{e} moved {moved}: a hull vertex that is neither extreme nor on an edge");
                        }
                        ++checkedHulls;
                    }
            Assert.True(checkedHulls >= 16, "vacuous: " + checkedHulls);
        }

        [Fact]
        public void Double_HullIsInvariantUnderPermutation_AndPowerOfTwoScale()
        {
            var rng = new Random(2026111502);
            foreach (Family fam in new[] { Family.Random, Family.Grid }) {
                List<Vector2d> pts = Points(fam, 30, rng);
                List<Vector2d> perm = pts.OrderBy(_ => rng.Next()).ToList();
                HashSet<Vector2d> a = Ring(pts, 1e-12, QueryNumberType.QT_DOUBLE), b = Ring(perm, 1e-12, QueryNumberType.QT_DOUBLE);
                // collinear hull points (the grid) may be listed or not: compare the extreme points
                Assert.True(Extreme(a).SetEquals(Extreme(b)), $"{fam}: the hull depends on the order of the points");
                foreach (int e in new[] { -30, 100 }) {
                    HashSet<Vector2d> s = Ring(Place(pts, e, 0), Eps(e), QueryNumberType.QT_DOUBLE);
                    Assert.True(Extreme(s).SetEquals(Extreme(a).Select(p => new Vector2d(ExactPoint3.ScaleB(p.x, e), ExactPoint3.ScaleB(p.y, e)))), $"{fam} 2^{e}: not the scaled hull");
                }
            }
        }

        static IEnumerable<Vector2d> ExtremeList(IEnumerable<Vector2d> ring) => Chain(ring.ToList());
        static HashSet<Vector2d> Extreme(IEnumerable<Vector2d> ring) => new HashSet<Vector2d>(ExtremeList(ring));

        [Theory]
        [InlineData(QueryNumberType.QT_RATIONAL)]
        [InlineData(QueryNumberType.QT_FILTERED)]
        [InlineData(QueryNumberType.QT_INTEGER)]
        public void ExactAndFilteredModes_AreNotImplemented_AndTheMessageSaysWhatIsSupported(QueryNumberType q)
        {
            // https://github.com/HarmenGrosseDeters/geometry3Sharp/issues/645: the exception type is kept (no API break), its message names the modes
            var pts = Points(Family.Random, 10, new Random(5));
            var e = Assert.Throws<NotImplementedException>(() => new ConvexHull2(pts, 1e-12, q));
            Assert.Contains(q.ToString(), e.Message);
            Assert.Contains("QT_DOUBLE", e.Message);
            Assert.Contains("QT_INT64", e.Message);
        }

        [Fact]
        public void TheClassComment_NamesOnlyTheSupportedModes()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && System.IO.File.Exists(System.IO.Path.Combine(dir, "CLAUDE.md")) == false) dir = System.IO.Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "comp_geom", "ConvexHull2.cs"));
            Assert.DoesNotContain("uses exact arithmetic", text);                // the comment of the original offered an exact mode
            Assert.DoesNotContain("various accuracy levels", text);
            Assert.Contains("QT_DOUBLE", text);
            Assert.Contains("never ported", text);
        }

        static HashSet<Vector2d> Ring(List<Vector2d> pts, double eps, QueryNumberType q)
        {
            var hull = new ConvexHull2(pts, eps, q);
            Assert.Equal(2, hull.Dimension);
            return new HashSet<Vector2d>(hull.HullIndices.Select(i => pts[i]));
        }

        [Theory]
        [InlineData(QueryNumberType.QT_DOUBLE)]
        [InlineData(QueryNumberType.QT_INT64)]
        public void FastModes_GiveARingOfDistinctValidIndices_ThatContainsThePointsUpToRounding(QueryNumberType q)
        {
            var rng = new Random(2026111503);
            foreach (Family fam in new[] { Family.Random, Family.Grid, Family.Circle })
                foreach (int e in new[] { -40, 0, 100 }) {
                    List<Vector2d> pts = Place(Points(fam, 40, rng), e, 0);
                    var hull = new ConvexHull2(pts, Eps(e), q);
                    Assert.Equal(2, hull.Dimension);
                    int[] idx = hull.HullIndices;
                    Assert.True(idx.Length >= 3 && idx.Length == idx.Distinct().Count() && idx.All(i => i >= 0 && i < pts.Count), $"{fam} 2^{e} {q}: bad indices");
                    var ring = idx.Select(i => pts[i]).ToList();
                    double tol = ExactPoint3.ScaleB(1e-5, e);      // the integer modes quantize to 2^20 / 2^24 cells of the extent
                    for (int k = 0; k < ring.Count; ++k) {
                        Vector2d a = ring[k], b = ring[(k + 1) % ring.Count];
                        Vector2d d = b - a;
                        double len = d.Length;
                        foreach (Vector2d p in pts) {
                            double left = (d.x * (p - a).y - d.y * (p - a).x) / len;      // > 0 inside a counterclockwise ring
                            Assert.True(len == 0 || left >= -tol, $"{fam} 2^{e} {q}: a point lies {-left} outside edge {k}");
                        }
                    }
                }
        }

        [Theory]
        [InlineData(-52)]
        [InlineData(-53)]
        [InlineData(-60)]
        [InlineData(-100)]
        public void TinyExtent_IsStillTwoDimensional(int e)
        {
            var pts = Place(Points(Family.Random, 40, new Random(7)), e, 0);
            var hull = new ConvexHull2(pts, 1e-300, QueryNumberType.QT_DOUBLE);
            Assert.Equal(2, hull.Dimension);
            // the second symptom (e = -100: epsilon * range underflows, Dimension is 2, but the ring is wrong, with an index twice):
            // distinct indices, and the extreme points are those of the exact chain
            int[] idx = hull.HullIndices;
            Assert.Equal(idx.Length, idx.Distinct().Count());
            Assert.True(Extreme(idx.Select(i => pts[i])).SetEquals(Chain(pts)), "the hull is not the exact hull");
        }

        [Theory]
        [InlineData(-200)]
        [InlineData(-100)]
        [InlineData(-60)]
        [InlineData(-52)]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(300)]
        public void Hull_IsTheScaledHull_AtEveryBinaryScale_BitForBit(int e)
        {
            // the reference at scale 1; the hull of a copy scaled by 2^e (exact) lists the same indices (docs/QA.md 2.4; #644)
            var rng = new Random(2026111801);
            foreach (Family fam in new[] { Family.Random, Family.Grid, Family.Circle }) {
                List<Vector2d> pts = Points(fam, 40, rng);
                var reference = new ConvexHull2(pts, 1e-300, QueryNumberType.QT_DOUBLE);
                var scaled = new ConvexHull2(Place(pts, e, 0), 1e-300, QueryNumberType.QT_DOUBLE);
                Assert.Equal(reference.Dimension, scaled.Dimension);
                Assert.True(reference.HullIndices.SequenceEqual(scaled.HullIndices), $"{fam} 2^{e}: other hull indices");
            }
        }

        [Theory]
        [InlineData(-1070)]      // coordinates up to 1000 * 2^-1070: the extent is subnormal (the exponent is read after scaling up by 2^64)
        [InlineData(-1060)]
        public void SubnormalExtent_GivesTheHullOfTheIntegerGrid(int e)
        {
            // small integers times 2^e are exact subnormal doubles, so the scaled copy is exact
            var rng = new Random(2026111803);
            var ints = new List<Vector2d>();
            for (int i = 0; i < 40; ++i) ints.Add(new Vector2d(rng.Next(1000), rng.Next(1000)));
            var reference = new ConvexHull2(ints, 1e-300, QueryNumberType.QT_DOUBLE);
            var tiny = ints.Select(p => new Vector2d(ExactPoint3.ScaleB(p.x, e), ExactPoint3.ScaleB(p.y, e))).ToList();
            Assert.True(tiny.Max(p => p.x) < 2.2250738585072014e-308, "the extent is not subnormal");
            var hull = new ConvexHull2(tiny, 1e-320, QueryNumberType.QT_DOUBLE);
            Assert.Equal(2, hull.Dimension);
            Assert.True(reference.HullIndices.SequenceEqual(hull.HullIndices), "other hull indices");
        }

        [Fact]
        public void ExtentThatOverflows_StillGivesTheExactHull()
        {
            // finite coordinates of both signs near the double range: max - min is infinite (the old code scaled by 1 / infinity = 0)
            var rng = new Random(2026111804);
            // (Vector2d.IsFinite tests x + y, so a point with two large coordinates of one sign is refused: the points sit near the axes)
            var pts = new List<Vector2d> { new Vector2d(9e307, 1), new Vector2d(-9e307, -2), new Vector2d(3, 9e307), new Vector2d(-1, -9e307) };
            for (int i = 0; i < 40; ++i) pts.Add(new Vector2d((rng.NextDouble() - 0.5) * 1e307, (rng.NextDouble() - 0.5) * 1e307));
            Assert.True(double.IsInfinity(pts.Max(p => p.x) - pts.Min(p => p.x)), "the extent does not overflow");
            var hull = new ConvexHull2(pts, 1e-12, QueryNumberType.QT_DOUBLE);
            Assert.Equal(2, hull.Dimension);
            Assert.True(Extreme(hull.HullIndices.Select(i => pts[i])).SetEquals(Chain(pts)), "not the exact hull");
        }

        [Fact]
        public void Hull_UnderTheSimilaritiesOfTheHarness_IsTheExactHull()
        {
            // the similarities of the plane (Similarity.All2D): scales, translation (UTM), rotations about z; power-of-two scales exact
            var rng = new Random(2026111802);
            List<Vector2d> pts = Points(Family.Random, 40, rng);
            var reference = new ConvexHull2(pts, 1e-300, QueryNumberType.QT_DOUBLE);
            int checkedSimilarities = 0;
            foreach (Similarity s in Similarity.All2D()) {
                List<Vector2d> moved = pts.Select(p => s.Apply2d(p)).ToList();
                var hull = new ConvexHull2(moved, 1e-300, QueryNumberType.QT_DOUBLE);
                Assert.Equal(2, hull.Dimension);
                if (s.IsExact)
                    Assert.True(reference.HullIndices.SequenceEqual(hull.HullIndices), $"{s.Name}: not the same hull indices");
                Assert.True(Extreme(hull.HullIndices.Select(i => moved[i])).SetEquals(Chain(moved)), $"{s.Name}: not the exact hull");
                ++checkedSimilarities;
            }
            Assert.True(checkedSimilarities == 8, "vacuous");
        }

        [Fact]
        public void OrdinaryScales_KeepTheDimensionOfTheUnscaledDecision()
        {
            // For extents between 1e-3 and 1e8, the decisions (point: extent below epsilon, absolute; line: distance below
            // epsilon times the extent, relative) are those of Vector2d.GetInformation on the original points, the code the
            // constructor used to call; near-collinear sets put the line decision at the edge of epsilon.
            int dims0 = 0, dims1 = 0, dims2 = 0;
            foreach (double ext in new[] { 1e-3, 0.37, 1, 1000, 123456.789, 1e6, 3e8 })
                foreach (double eps in new[] { 1e-12, 1e-9, 1e-6, 1e-3, 0.05 })
                    for (int seed = 0; seed < 60; ++seed) {
                        var r = new Random(seed * 31 + 7);
                        int m = 3 + r.Next(40);
                        double off = seed % 3 == 0 ? ext * 100 : 0;
                        var pts = new List<Vector2d>();
                        for (int i = 0; i < m; ++i) {
                            double u = r.NextDouble();
                            switch (seed % 4) {
                                case 0: pts.Add(new Vector2d(off + ext * r.NextDouble(), off + ext * r.NextDouble())); break;
                                case 1: pts.Add(new Vector2d(off + ext * u, off + ext * (2 * u + (r.NextDouble() - 0.5) * Math.Pow(10, -(seed % 12) - 1)))); break;
                                case 2: pts.Add(new Vector2d(off + ext * r.Next(6), off + ext * r.Next(6))); break;
                                default: pts.Add(new Vector2d(off + ext * Math.Cos(6.28 * u), off + ext * Math.Sin(6.28 * u))); break;
                            }
                        }
                        Vector2d.GetInformation(pts, eps, out Vector2d.Information info);
                        var hull = new ConvexHull2(pts, eps, QueryNumberType.QT_DOUBLE);
                        Assert.True(info.mDimension == hull.Dimension, $"extent {ext} epsilon {eps} seed {seed}: dimension {hull.Dimension}, unscaled decision {info.mDimension}");
                        if (hull.Dimension == 0) ++dims0; else if (hull.Dimension == 1) ++dims1; else ++dims2;
                    }
            Assert.True(dims0 > 30 && dims1 > 100 && dims2 > 1000, $"vacuous: {dims0} / {dims1} / {dims2}");
        }

        [Fact]
        public void OneDimensionalAndPointSets_KeepTheirDimension_AtATinyScale()
        {
            foreach (int e in new[] { -100, 0, 100 }) {
                var line = new List<Vector2d>();
                for (int i = 0; i < 6; ++i) line.Add(new Vector2d(ExactPoint3.ScaleB(3 + i, e), ExactPoint3.ScaleB(5 + 2 * i, e)));
                var h1 = new ConvexHull2(line, Eps(e), QueryNumberType.QT_DOUBLE);
                Assert.Equal(1, h1.Dimension);
                h1.Get1DHullInfo(out Vector2d origin, out Vector2d direction);
                Assert.Contains(origin, line);                             // a point of the input, in its units
                Assert.Equal(1.0, direction.Length, 12);
                // a cluster below epsilon (an absolute length) is a point
                var cluster = new List<Vector2d> { new Vector2d(1, 1), new Vector2d(1 + 1e-14, 1), new Vector2d(1, 1 + 1e-14) };
                Assert.Equal(0, new ConvexHull2(cluster, 1e-12, QueryNumberType.QT_DOUBLE).Dimension);
            }
        }

        [Fact]
        public void TwoDistinctPoints_AndRepeatedPoints_AreNotADimension2Hull()
        {
            var one = new ConvexHull2(new List<Vector2d> { new Vector2d(1, 1), new Vector2d(1, 1), new Vector2d(1, 1) }, 1e-12, QueryNumberType.QT_DOUBLE);
            Assert.Equal(0, one.Dimension);
            var line = new ConvexHull2(new List<Vector2d> { new Vector2d(0, 0), new Vector2d(1, 1), new Vector2d(2, 2), new Vector2d(0.5, 0.5) }, 1e-12, QueryNumberType.QT_DOUBLE);
            Assert.Equal(1, line.Dimension);
        }
    }
}
