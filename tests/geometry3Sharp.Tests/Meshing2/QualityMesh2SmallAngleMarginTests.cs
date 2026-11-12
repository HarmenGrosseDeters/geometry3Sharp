using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// An input angle counts as small (and a triangle spanning it is not refined, Shewchuk 2002) only where the oracle excuses the
    /// triangle: sin^2 strictly below the bound's by the oracle's slack 2^-30. The refiner used a relative 1e-9 (2.5e-10 at 30
    /// degrees), so for an input angle just below the bound, inside that window, it kept triangles the oracle reports as
    /// AngleBelowBound (found by review of the small-angle design; reproduced with a wedge at 30 degrees - 6e-10 rad and a few points in it).
    /// </summary>
    public class QualityMesh2SmallAngleMarginTests
    {
        /// <summary> a wedge of two segments from the origin at 30 degrees - 6e-10 rad (sin^2 about 0.25 - 5.2e-10, in the window), in a
        /// box ring, with 1 to 4 random points inside the wedge and 0 to 3 next to its first segment; constrained mode at 30 degrees </summary>
        internal static Mesh2Input WindowWedge(int seed)
        {
            var rnd = new Random(seed);
            double phi = 30 * Math.PI / 180 - 6e-10;
            var xy = new List<double> { 0, 0, 1, 0, Math.Cos(phi), Math.Sin(phi), -0.5, -0.5, 1.5, -0.5, 1.5, 1.2, -0.5, 1.2 };
            int inside = rnd.Next(1, 5);
            for (int i = 0; i < inside; ++i) {
                double r = 0.05 + 0.85 * rnd.NextDouble(), a = (0.05 + 0.9 * rnd.NextDouble()) * phi;
                xy.Add(r * Math.Cos(a)); xy.Add(r * Math.Sin(a));
            }
            int along = rnd.Next(0, 4);
            for (int i = 0; i < along; ++i) { xy.Add(0.1 + 0.8 * rnd.NextDouble()); xy.Add(0); }
            var input = Generators.Literal(xy.ToArray());
            input.Segments.Add(new SegmentInput(0, 1)); input.Segments.Add(new SegmentInput(0, 2));
            input.Rings.Add(new RingInput(new[] { 3, 4, 5, 6 }, false));
            input.MinAngleDeg = 30;
            input.ConstrainedQuality = true;
            return input;
        }

        // (seeds that the oracle judged INVALID, AngleBelowBound, before the fix: 2, 5, 6, 10 and 13 of the first 400)
        internal static readonly int[] WindowSeeds = { 2, 5, 6, 10, 13 };
        public static IEnumerable<object[]> Seeds() => WindowSeeds.Select(s => new object[] { s });

        [Theory]
        [MemberData(nameof(Seeds))]
        public void InputAngleInTheWindow_IsNotTreatedAsSmall(int seed)
        {
            var input = WindowWedge(seed);
            var output = new QualityMesh2Candidate().Run(input);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, "seed " + seed + ": " + report);
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void WindowWedge_IsValid_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var input = WindowWedge(2);
            for (int i = 0; i < input.Count; ++i) { var q = s.Apply(new Vector3d(input.X[i], input.Y[i], 0)); input.X[i] = q.x; input.Y[i] = q.y; }
            var output = new QualityMesh2Candidate().Run(input);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(output.Status == Mesh2Status.Success && report.Ok, name + ": " + report);
        }
    }
}
