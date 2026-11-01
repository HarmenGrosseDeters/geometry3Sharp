using System;
using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The gates of <c>m2.snap</c> (explicit tolerance, O11, D14). With snap rounding built the rows are covered and run through the
    /// oracle in the catalogue tests; here: the sharpened bounds still separate (the same rows without snapping fail them), the oracle
    /// accepts merged results built by hand, and snapped meshes of random inputs, restore pixels included, pass the oracle at M2 and M3.
    /// </summary>
    public class Meshing2SnapGateTests
    {
        static CatalogueRow Row(string id) => Catalogue.Rows().Single(r => r.Case != null && r.Case.Id == id);

        static int SurvivingVertices(Mesh2Input input, Mesh2Output output) =>
            output.VX.Length - Enumerable.Range(0, input.Count).Count(i => output.InputToVertex[i] != i);

        [Theory]
        [InlineData("f-16")]   // without snapping the near-collinear pair does not share S-B: refinement runs to the Steiner limit
        [InlineData("f-17")]   // the hole far below the tolerance stays: 8 vertices
        [InlineData("f-17/b")] // the contour around the hole marker stays
        [InlineData("f-22/b")] // the sliver within 2^-29 stays: the Steiner limit, as without tolerance
        public void SnapRow_MeetsItsBound_OnlyWithSnapping(string id)
        {
            var row = Row(id);
            Assert.Equal(CaseState.Covered, row.State);
            var candidate = CandidateRegistry.For(row.Case.Milestone);
            var snapped = candidate.Run(row.Case.Input);
            Assert.InRange(SurvivingVertices(row.Case.Input, snapped), 1, row.Case.Expect.MaxVertices.Value);
            // the vertex bound alone, as the oracle's CountOutOfRange judges it; the full oracle would take minutes on the unsnapped F-16
            var plain = CloneWithoutSnapping(row.Case.Input);
            var unsnapped = candidate.Run(plain);
            Assert.True(SurvivingVertices(plain, unsnapped) > row.Case.Expect.MaxVertices.Value, id + ": the bound does not separate snapping from none");
        }

        static Mesh2Input CloneWithoutSnapping(Mesh2Input input) => new Mesh2Input {
            X = input.X, Y = input.Y, Segments = input.Segments, Rings = input.Rings, HoleSeeds = input.HoleSeeds, Regions = input.Regions,
            MinAngleDeg = input.MinAngleDeg, MaxArea = input.MaxArea, SteinerLimit = input.SteinerLimit, Conforming = input.Conforming, SnapTolerance = 0 };

        /// <summary>
        /// the merged result of F-17 and F-17/b, built by hand: the outer square, the collapsed contour as one point at (5, 5), its pixel
        /// centre on the grid of the tolerance; the other contour points stay in the list as aliases of it (Q8)
        /// </summary>
        static Mesh2Output CollapsedHole(Mesh2Input input)
        {
            var map = new int[input.Count];
            for (int i = 0; i < input.Count; ++i) map[i] = i < 4 ? i : 4;
            Assert.Equal(5.0, input.X[4]); Assert.Equal(5.0, input.Y[4]);
            var tris = new[] { new[] { 0, 1, 4 }, new[] { 1, 2, 4 }, new[] { 2, 3, 4 }, new[] { 3, 0, 4 } };
            return new Mesh2Output {
                Status = Mesh2Status.Success, VX = (double[])input.X.Clone(), VY = (double[])input.Y.Clone(), InputToVertex = map, Triangles = tris,
                Neighbours = SelfCheck.NeighboursOf(tris),
                Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 2), new SubsegmentOutput(2, 3), new SubsegmentOutput(3, 0) },
            };
        }

        [Theory]
        [InlineData("f-17")]
        [InlineData("f-17/b")]
        public void SnapGate_AcceptsTheCollapsedHole(string id)
        {
            var row = Row(id);
            Assert.Equal(5, row.Case.Expect.MaxVertices.Value);   // the bound is tight: one vertex more is rejected
            var report = Oracle.Check(row.Case.Input, CollapsedHole(row.Case.Input), row.Case.Expect);
            Assert.True(report.Ok, report.ToString());
        }

        /// <summary>
        /// Snapped meshes of random inputs through the oracle. At M3 refinement puts every vertex on the grid (owner decision 2026-11-01,
        /// MESHING2D_SNAP.md section 3g); the oracle checks that (OffGrid) and judges the angle with the floor of two pixels and the
        /// grid-blocked exemption.
        /// </summary>
        [Theory]
        [InlineData(Milestone.M2, 0.0)]
        [InlineData(Milestone.M3, 20.0)]
        [InlineData(Milestone.M3, 30.0)]
        public void Fuzz_SnappedMeshes_PassTheOracle(Milestone milestone, double angle)
        {
            // random segments in a square ring, snapped at a coarse tolerance so that crossings, merges and restore pixels occur
            var random = new Random(milestone == Milestone.M2 ? 11 : (int)angle);
            for (int iter = 0; iter < 60; ++iter) {
                var x = new List<double> { 0, 16, 16, 0 }; var y = new List<double> { 0, 0, 16, 16 };
                var input = new Mesh2Input();
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                int m = 4 + random.Next(10);
                for (int i = 0; i < m; ++i) {
                    for (int k = 0; k < 2; ++k) { x.Add(1 + Math.Round(random.NextDouble() * 14 * 64) / 64); y.Add(1 + Math.Round(random.NextDouble() * 14 * 64) / 64); }
                    input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
                }
                input.X = x.ToArray(); input.Y = y.ToArray();
                input.SnapTolerance = milestone == Milestone.M2 ? new[] { 0.5, 1, 2 }[iter % 3] : new[] { 0.25, 0.5, 1, 0.3 }[iter % 4];
                if (milestone == Milestone.M3) { input.MinAngleDeg = angle; input.SteinerLimit = 50000; }
                var output = CandidateRegistry.For(milestone).Run(input);
                var report = Oracle.Check(input, output, new OracleExpect { Milestone = milestone });
                Assert.True(report.Ok, "iteration " + iter + ": " + report);
            }
        }

        /// <summary> the i-th input of a random stream: up to 24 segments on a 1/64 lattice in a square ring, pixel widths that do not divide it </summary>
        static Mesh2Input GridFuzzInput(int seed, double angle, int which)
        {
            var random = new Random(seed + (int)angle);
            Mesh2Input input = null;
            for (int iter = 0; iter <= which; ++iter) {
                var x = new List<double> { 0, 16, 16, 0 }; var y = new List<double> { 0, 0, 16, 16 };
                input = new Mesh2Input();
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                int m = 4 + random.Next(20);
                for (int i = 0; i < m; ++i) {
                    for (int k = 0; k < 2; ++k) { x.Add(1 + Math.Round(random.NextDouble() * 14 * 64) / 64); y.Add(1 + Math.Round(random.NextDouble() * 14 * 64) / 64); }
                    input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
                }
                input.X = x.ToArray(); input.Y = y.ToArray();
                input.SnapTolerance = new[] { 0.25, 0.5, 1, 0.3, 1.0 / 3, 0.125 }[iter % 6];
                input.MinAngleDeg = angle; input.SteinerLimit = 50000;
            }
            return input;
        }

        /// <summary>
        /// Inputs of a larger grid fuzz run (12000 meshes) that failed on the way to refinement on the grid, each for its own reason: a
        /// refusal that went stale when a neighbouring chain was split and bent away (the mesher looks again after a sweep with
        /// insertions); a lattice triangle with two equal shortest edges (the oracle tries both for a small input angle); a vertex, or a
        /// triangle's circumcentre, behind a subsegment with no split point on the grid; vertices on a grid of width 0.3 or 1/3, which
        /// doubles do not hold exactly, taken at their pixel centre and matched by their pixel, and pixel corners touched there.
        /// </summary>
        [Theory]
        [InlineData(1000, 20.0, 130)]   // stale refusal
        [InlineData(1000, 20.0, 563)]   // tie of shortest edges
        [InlineData(1000, 30.0, 234)]   // tie of shortest edges
        [InlineData(1000, 30.0, 11)]    // vertex behind an unsplittable piece
        [InlineData(1000, 20.0, 315)]   // vertex behind an unsplittable piece
        [InlineData(7000, 30.0, 866)]   // circumcentre behind an unsplittable piece (outside the domain)
        [InlineData(7000, 20.0, 103)]   // circumcentre behind an unsplittable piece
        [InlineData(7000, 20.0, 736)]   // vertex pixel at w = 1/3
        [InlineData(7000, 30.0, 1353)]  // vertex pixel at w = 0.3
        [InlineData(9100, 20.0, 1281)]  // subsegment end at w = 0.3
        [InlineData(9100, 20.0, 628)]   // pixel corner touched at w = 1/3
        [InlineData(12000, 30.0, 694)]  // circumcentre tried again after its encroached piece proved unsplittable
        public void GridRefinement_FuzzRegressions_PassTheOracle(int seed, double angle, int which)
        {
            var input = GridFuzzInput(seed, angle, which);
            var output = CandidateRegistry.For(Milestone.M3).Run(input);
            Assert.Equal(Mesh2Status.Success, output.Status);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
        }
    }
}
