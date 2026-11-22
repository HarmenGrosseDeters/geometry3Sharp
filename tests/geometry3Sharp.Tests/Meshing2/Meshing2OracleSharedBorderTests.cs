using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1318: on the snap grid the oracle asked for an output boundary near every ring edge that has the domain beside it (rule O-G,
    /// the direction from the input to the output). A border that two outer rings share has the domain on both sides: it is no
    /// boundary, the mesh has triangles on both sides of it, and every mesh of parcels on the grid was judged INVALID
    /// (BoundaryOutsideTube). Found with the jittered parcels of `Meshing2Benchmark perf --quality`, from 2 x 2 rings on.
    /// </summary>
    public class Meshing2OracleSharedBorderTests
    {
        /// <summary> side x side square rings that share their borders, the inner corners jittered (the generator of perf --quality) </summary>
        static Mesh2Input Parcels(int side, double snap, double? minAngle)
        {
            var rnd = new Random(7);
            var xs = new List<double>(); var ys = new List<double>();
            for (int i = 0; i <= side; ++i)
                for (int j = 0; j <= side; ++j) {
                    bool edge = i == 0 || j == 0 || i == side || j == side;
                    xs.Add(i + (edge ? 0 : (rnd.NextDouble() - 0.5) * 0.5)); ys.Add(j + (edge ? 0 : (rnd.NextDouble() - 0.5) * 0.5));
                }
            var input = new Mesh2Input { X = xs.ToArray(), Y = ys.ToArray(), MinAngleDeg = minAngle, SnapTolerance = snap };
            for (int i = 0; i < side; ++i)
                for (int j = 0; j < side; ++j) { int a = i * (side + 1) + j; input.Rings.Add(new RingInput(new[] { a, a + side + 1, a + side + 2, a + 1 }, false)); }
            return input;
        }

        static Mesh2Input TwoSquares()
        {
            var input = new Mesh2Input { X = new double[] { 0, 16, 16, 0, 32, 32 }, Y = new double[] { 0, 0, 16, 16, 0, 16 }, SnapTolerance = 1 };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false)); input.Rings.Add(new RingInput(new[] { 1, 4, 5, 2 }, false));
            return input;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwoSquaresThatShareABorder_OnTheGrid_AreValid(bool reference)
        {
            var input = TwoSquares();
            var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            Assert.Equal(4, o.Triangles.Length);
            var expect = new OracleExpect { Milestone = Milestone.M2 };
            var report = Oracle.Check(input, o, reference ? expect.WithReference() : expect);
            Assert.True(report.Ok, report.ToString());
        }

        [Theory]
        [InlineData(2, false)]
        [InlineData(2, true)]
        [InlineData(6, false)]
        public void JitteredParcels_OnTheGrid_AreValid(int side, bool reference)
        {
            var input = Parcels(side, 1.0 / 64, 20);
            var o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            Assert.False(o.SteinerLimitReached);
            var expect = new OracleExpect { Milestone = Milestone.M3 };
            var report = Oracle.Check(input, o, reference ? expect.WithReference() : expect);
            Assert.True(report.Ok, report.ToString());
        }

        /// <summary> the other side of the fix: a ring edge with the domain on one side only still needs an output boundary </summary>
        [Fact]
        public void ASquareLeftOutOfTheMesh_IsStillRejected()
        {
            var input = TwoSquares();
            var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
            // keep only the triangles of the left square (centroid x below 16)
            var keep = Enumerable.Range(0, o.Triangles.Length).Where(i => o.Triangles[i].Sum(v => o.VX[v]) / 3 < 16).ToArray();
            Assert.Equal(2, keep.Length);
            var half = new Mesh2Output { Status = Mesh2Status.Success, VX = o.VX, VY = o.VY, InputToVertex = o.InputToVertex, Triangles = keep.Select(i => o.Triangles[i]).ToArray(),
                TriangleLabels = keep.Select(i => o.TriangleLabels[i]).ToArray(), Subsegments = o.Subsegments, BadTriangles = new int[0], NeighboursReported = false };
            var report = Oracle.Check(input, half, new OracleExpect { Milestone = Milestone.M2 });
            Assert.False(report.Ok);
        }

        /// <summary> a hole inside one of two parcels keeps its boundary rule: the hole left out of the mesh is rejected for it </summary>
        [Fact]
        public void AHoleInAParcel_LeftOutOfTheMesh_IsStillBoundaryOutsideTube()
        {
            var input = TwoSquares();
            input.X = input.X.Concat(new double[] { 4, 12, 12, 4 }).ToArray(); input.Y = input.Y.Concat(new double[] { 4, 4, 12, 12 }).ToArray();
            input.Rings.Add(new RingInput(new[] { 6, 7, 8, 9 }, true));
            var solid = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 1, 4, 5 }, new[] { 1, 5, 2 } };
            var o = new Mesh2Output { Status = Mesh2Status.Success, VX = input.X, VY = input.Y, InputToVertex = Enumerable.Range(0, 10).ToArray(), Triangles = solid,
                TriangleLabels = new[] { 0, 0, 0, 0 }, Subsegments = new List<SubsegmentOutput>(), BadTriangles = new int[0], NeighboursReported = false };
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Has(Violation.BoundaryOutsideTube), report.ToString());
        }
    }
}
