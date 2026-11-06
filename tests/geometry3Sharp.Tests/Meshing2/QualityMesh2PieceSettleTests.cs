using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Inserting the pieces of a split subsegment settles: a vertex within the tolerance of a piece splits it only when both parts are
    /// strictly shorter. Without that, refinement in the constrained-quality mode at high angle bounds (a cascade at a small angle between
    /// a hull edge and a ring, down to pieces about as short as the tolerance) failed with NodingLimit, "a segment split did not settle":
    /// two vertices within the tolerance of each other's piece split it at each other in turn (found with ext/cdt/island at 32 and 34
    /// degrees, a public input; reproduced here with procedural jagged rings).
    /// </summary>
    public class QualityMesh2PieceSettleTests
    {
        /// <summary> a closed polyline of n points at random radii around a circle, coordinates rounded to 8 decimals, as segments
        /// (no ring: the domain is the convex hull, whose edges meet the polyline at small angles) </summary>
        /// (splitmix64, not System.Random, so the inputs are the same on every target framework)
        internal static Mesh2Input JaggedRing(int seed, int n, double angle)
        {
            ulong state = (ulong)seed;
            double Next()
            {
                ulong z = state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return ((z ^ (z >> 31)) >> 11) * (1.0 / (1UL << 53));
            }
            var xy = new double[2 * n];
            for (int i = 0; i < n; ++i) {
                double t = 2 * Math.PI * i / n, r = 0.5 + 0.35 * Next();
                xy[2 * i] = Math.Round(0.5 + r * Math.Cos(t), 8); xy[2 * i + 1] = Math.Round(0.5 + r * Math.Sin(t), 8);
            }
            var input = Generators.Literal(xy);
            for (int i = 0; i < n; ++i) input.Segments.Add(new SegmentInput(i, (i + 1) % n));
            input.MinAngleDeg = angle;
            input.ConstrainedQuality = true;
            return input;
        }

        public static IEnumerable<object[]> Seeds() => SettleSeeds.Select(s => new object[] { s });
        // (seeds that failed with NodingLimit at 34 degrees before the fix: 7 of the first 300)
        internal static readonly int[] SettleSeeds = { 67, 91, 161 };

        [Theory]
        [MemberData(nameof(Seeds))]
        public void JaggedRing_AtHighBounds_Settles(int seed)
        {
            var input = JaggedRing(seed, 40, 34);
            var output = new QualityMesh2Candidate().Run(input);
            Assert.True(output.Status == Mesh2Status.Success, "seed " + seed + ": " + output.Status + " " + output.FailureKind);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, "seed " + seed + ": " + report);
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> the case under every similarity: the others pass the oracle, the 2^k scales give the same mesh bit for bit </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void JaggedRing_Settles_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var plain = JaggedRing(SettleSeeds[0], 40, 34);
            var moved = JaggedRing(SettleSeeds[0], 40, 34);
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            var b = new QualityMesh2Candidate().Run(moved);
            Assert.True(b.Status == Mesh2Status.Success, name + ": " + b.Status + " " + b.FailureKind);
            var report = Oracle.Check(moved, b, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, name + ": " + report);
            if (s.IsExact) AssertScaled(new QualityMesh2Candidate().Run(plain), b, s.Scale);
        }

        /// <summary>
        /// scaled by 2^-500 and 2^200: the same mesh bit for bit (the guard's lengths at the extreme exponents: see
        /// ToleranceSplit_OnlyWhenItShortens)
        /// </summary>
        [Theory]
        [InlineData(-500)]
        [InlineData(200)]
        public void JaggedRing_AtExtremeScales_IsBitIdentical(int exponent)
        {
            double f = Math.Pow(2, exponent);
            var plain = JaggedRing(SettleSeeds[0], 40, 34);
            var moved = JaggedRing(SettleSeeds[0], 40, 34);
            for (int i = 0; i < plain.Count; ++i) { moved.X[i] = plain.X[i] * f; moved.Y[i] = plain.Y[i] * f; }
            var a = new QualityMesh2Candidate().Run(plain);
            Assert.Equal(Mesh2Status.Success, a.Status);
            AssertScaled(a, new QualityMesh2Candidate().Run(moved), f);
        }

        static void AssertScaled(Mesh2Output a, Mesh2Output b, double f)
        {
            Assert.Equal(a.Status, b.Status);
            Assert.Equal(a.VX.Select(v => v * f), b.VX);
            Assert.Equal(a.VY.Select(v => v * f), b.VY);
            Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
        }

        /// <summary>
        /// The guard alone, by hand: u = (0, 0), w = (L, 0) and v = (0.9 L, 0.495 L) with L = 2^-40, and a point (1, 1) that makes the
        /// automatic tolerance 2^-41 = L / 2. v is within the tolerance of the segment u-w (0.495 L), not of u or w (|vw| = 0.505 L), and
        /// |uv| = 1.027 L is longer than u-w: splitting u-w at v would not shorten it, so u-w stays one subsegment. Then a v that does
        /// shorten it (0.5 L, 0.495 L): u-w is split at v, as before. Both also scaled by 2^-900 and 2^200: the guard compares
        /// lengths in scaled units, since at 2^-900 the squares of the raw differences (about 2^-1880) underflow to 0 and the guard
        /// would refuse every split.
        /// </summary>
        [Theory]
        [InlineData(0.9, false, 0)]
        [InlineData(0.5, true, 0)]
        [InlineData(0.9, false, -900)]
        [InlineData(0.5, true, -900)]
        [InlineData(0.9, false, 200)]
        [InlineData(0.5, true, 200)]
        public void ToleranceSplit_OnlyWhenItShortens(double t, bool split, int exponent)
        {
            double f = Math.Pow(2, exponent), l = f / (1L << 40);
            var input = new ConstrainedMesh2Input();
            input.Points.Add(new Vector2d(0, 0));
            input.Points.Add(new Vector2d(l, 0));
            input.Points.Add(new Vector2d(t * l, 0.495 * l));
            input.Points.Add(new Vector2d(f, f));
            input.Segments.Add(new Mesh2Segment(0, 1));
            var result = new ConstrainedDelaunayMesh2().Compute(input);
            Assert.Equal(Triangulation2Status.Success, result.Status);
            var subs = result.Mesh.Subsegments.Select(q => (Math.Min(q.A, q.B), Math.Max(q.A, q.B))).OrderBy(q => q).ToList();
            var expected = split ? new List<(int, int)> { (0, 2), (1, 2) } : new List<(int, int)> { (0, 1) };
            Assert.Equal(expected, subs);
        }

        /// <summary>
        /// The same guard where u-w is not an edge yet: L = 2^-39 (the tolerance is L / 4), u = (0, 0), w = (L, 0),
        /// v = (0.973 L, 0.249 L), and p = (0.5 L, -0.35 L) inside the circle through u, w and v, so the triangulation has p-v instead of
        /// u-w and v is a corner of the triangle at u that holds the direction to w. v is within the tolerance of u-w (0.249 L), not of
        /// w (0.2505 L), and |uv| = 1.004 L: no split, the walk recovers u-w. With q = (0.3 L, 0.45 L) as well, v is met in the walk
        /// beyond the start triangle instead. At 2^0, 2^-900 and 2^200.
        /// </summary>
        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, -900)]
        [InlineData(false, 200)]
        [InlineData(true, 0)]
        [InlineData(true, -900)]
        public void ToleranceSplit_InTheWalk_OnlyWhenItShortens(bool withQ, int exponent)
        {
            double f = Math.Pow(2, exponent), l = f / (1L << 39);
            var input = new ConstrainedMesh2Input();
            input.Points.Add(new Vector2d(0, 0));
            input.Points.Add(new Vector2d(l, 0));
            input.Points.Add(new Vector2d(0.973 * l, 0.249 * l));
            input.Points.Add(new Vector2d(0.5 * l, -0.35 * l));
            input.Points.Add(new Vector2d(f, f));
            if (withQ) input.Points.Add(new Vector2d(0.3 * l, 0.45 * l));
            input.Segments.Add(new Mesh2Segment(0, 1));
            var result = new ConstrainedDelaunayMesh2().Compute(input);
            Assert.Equal(Triangulation2Status.Success, result.Status);
            var subs = result.Mesh.Subsegments.Select(q => (Math.Min(q.A, q.B), Math.Max(q.A, q.B))).ToList();
            Assert.Equal(new List<(int, int)> { (0, 1) }, subs);
        }
    }
}
