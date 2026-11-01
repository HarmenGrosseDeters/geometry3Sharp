using System.Collections.Generic;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle's domain rule D5 (owner, 2026-11-01): the innermost ring containing a point decides by its role. Areas worked out by
    /// hand, so that the oracle's rule is checked against the decision, not only against the mesher.
    /// </summary>
    public class Meshing2DomainTests
    {
        static Mesh2Input Rings(params (double x0, double y0, double x1, double y1, bool hole)[] rings)
        {
            var x = new List<double>(); var y = new List<double>();
            var input = new Mesh2Input();
            foreach (var (x0, y0, x1, y1, hole) in rings) {
                int b = x.Count;
                x.AddRange(new[] { x0, x1, x1, x0 }); y.AddRange(new[] { y0, y0, y1, y1 });
                input.Rings.Add(new RingInput(new[] { b, b + 1, b + 2, b + 3 }, hole));
            }
            input.X = x.ToArray(); input.Y = y.ToArray();
            return input;
        }

        static Where At(Domain d, double x, double y) => d.Classify(Pt.FromDoubles(x, y));

        [Fact]
        public void IslandInALake_IsInTheDomain()
        {
            var d = new Domain(Rings((0, 0, 10, 10, false), (2, 2, 8, 8, true), (4, 4, 6, 6, false)));
            Assert.Equal(new Q(68, 1), d.Area);   // 100 - 36 + 4; the rule before 2026-11-01 gave 64
            Assert.Equal(Where.Inside, At(d, 5, 5));
            Assert.Equal(Where.Outside, At(d, 3, 3));
            Assert.Equal(Where.Inside, At(d, 1, 1));
            Assert.Equal(Where.Boundary, At(d, 4, 5));   // the island's shore
            Assert.Equal(Where.Boundary, At(d, 2, 5));   // the lake's shore
        }

        [Fact]
        public void PondOnAnIsland_HoleInAHole_AndAnyOrder()
        {
            var pond = new Domain(Rings((0, 0, 10, 10, false), (2, 2, 8, 8, true), (4, 4, 6, 6, false), (4.5, 4.5, 5.5, 5.5, true)));
            Assert.Equal(new Q(67, 1), pond.Area);
            Assert.Equal(Where.Outside, At(pond, 5, 5));
            Assert.Equal(Where.Inside, At(pond, 4.2, 4.2));
            var reversed = new Domain(Rings((4.5, 4.5, 5.5, 5.5, true), (4, 4, 6, 6, false), (2, 2, 8, 8, true), (0, 0, 10, 10, false)));
            Assert.Equal(new Q(67, 1), reversed.Area);
            // F-44: a hole in a hole stays removed
            var holeInHole = new Domain(Rings((0, 0, 10, 10, false), (2, 2, 8, 8, true), (4, 4, 6, 6, true)));
            Assert.Equal(new Q(64, 1), holeInHole.Area);
            Assert.Equal(Where.Outside, At(holeInHole, 5, 5));
            Assert.Equal(Where.Boundary, At(holeInHole, 2, 5));
            Assert.Equal(Where.Outside, At(holeInHole, 4, 5));   // between two holes
        }

        [Fact]
        public void OverlappingRings_TheLatestDecides()
        {
            // two outer rings overlapping without nesting, a hole across both: 68 - 14
            var d = new Domain(Rings((0, 0, 6, 6, false), (4, 4, 10, 10, false), (3, 3, 7, 7, true)));
            Assert.Equal(new Q(54, 1), d.Area);
            Assert.Equal(Where.Outside, At(d, 5, 5));
            Assert.Equal(Where.Inside, At(d, 1, 1));
            Assert.Equal(Where.Inside, At(d, 9, 9));
            // a hole first, then an outer ring overlapping it without nesting: the outer ring (later) decides in the overlap
            var e = new Domain(Rings((0, 0, 10, 10, false), (2, 2, 6, 6, true), (4, 4, 8, 8, false)));
            Assert.Equal(Where.Inside, At(e, 5, 5));
            Assert.Equal(Where.Outside, At(e, 3, 3));
            Assert.Equal(new Q(100 - 16 + 4, 1), e.Area);
        }

        [Fact]
        public void OuterRingInsideAHoleOnly_IsTheDomain()
        {
            // an outer ring exists, so the hull is not the domain: only the island inside the hole remains
            Assert.Equal(new Q(4, 1), new Domain(Rings((2, 2, 8, 8, true), (4, 4, 6, 6, false))).Area);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Mesher_AgreesWithTheOracle_OnNestedAndOverlappingRings(int which)
        {
            var input = new[] {
                Rings((0, 0, 10, 10, false), (2, 2, 8, 8, true), (4, 4, 6, 6, false), (4.5, 4.5, 5.5, 5.5, true)),
                Rings((0, 0, 6, 6, false), (4, 4, 10, 10, false), (3, 3, 7, 7, true)),
                Rings((0, 0, 10, 10, false), (2, 2, 6, 6, true), (4, 4, 8, 8, false)) }[which];
            var output = new ConstrainedDelaunayMesh2Candidate().Run(input);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }
    }
}
