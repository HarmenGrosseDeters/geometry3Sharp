using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Content inside a hole blocked a split point on the grid (T15): a piece of an input segment lying wholly in a hole stayed a
    /// constraint, and ValidGridSplit refused the split points of the hole's edge that its pixel covered, while the oracle, which sees
    /// only the output, found one of them valid and reported the hole edge's encroachment as NotDelaunayGlobal. A hole removes what lies
    /// inside its ring (spec Q7), so refinement now starts from the carved triangulation: constraints with the outside on both sides are
    /// lifted and the vertices made from them deleted (owner 2026-11-03). Found with the grid fuzz with holes in M3 (seeds 867 at 30
    /// degrees and 1367 at 25, pixel 1/3), reduced to two segments.
    /// </summary>
    public class QualityMesh2HoleCarveTests
    {
        /// <summary>
        /// the fuzz's 16 square with two triangular holes, on a grid of 1/3: one segment crosses the first hole's edge and ends inside the
        /// square, the other runs from the first hole across the domain into the second
        /// </summary>
        static Mesh2Input Case(double angle, bool constrainedQuality = false)
        {
            var input = new Mesh2Input {
                X = new[] { 0, 16, 16, 0, 6, 9, 7, 10, 13, 12, 13.90625, 7.375, 8.453125, 9.90625 },
                Y = new[] { 0, 0, 16, 16, 6, 6.5, 9, 10, 11, 14, 8.578125, 6.453125, 6.015625, 9.78125 },
                SnapTolerance = 1.0 / 3, MinAngleDeg = angle, SteinerLimit = 50000, ConstrainedQuality = constrainedQuality };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
            input.Rings.Add(new RingInput(new[] { 7, 8, 9 }, true));
            input.Segments.Add(new SegmentInput(10, 11));
            input.Segments.Add(new SegmentInput(12, 13));
            return input;
        }

        [Theory]
        [InlineData(20.0, false)]
        [InlineData(25.0, false)]
        [InlineData(30.0, false)]
        [InlineData(20.0, true)]
        [InlineData(25.0, true)]
        public void SegmentPieceInsideAHole_DoesNotBlockASplitOnTheGrid(double angle, bool constrainedQuality)
        {
            var input = Case(angle, constrainedQuality);
            QualityMesh2.Refiner.CheckTagsForTests = true;
            Mesh2Output o;
            try { o = new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.CheckTagsForTests = false; }
            Assert.Equal(Mesh2Status.Success, o.Status);
            Assert.False(o.SteinerLimitReached);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        /// <summary> the same under every similarity: 2^k scales give the same mesh bit for bit, the others pass the oracle </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void SegmentPieceInsideAHole_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Case(30);
            var moved = Case(30);
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            moved.SnapTolerance = plain.SnapTolerance * s.Scale;
            var b = new QualityMesh2Candidate().Run(moved);
            Assert.Equal(Mesh2Status.Success, b.Status);
            var report = Oracle.Check(moved, b, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, name + ": " + report);
            if (s.IsExact) {
                var a = new QualityMesh2Candidate().Run(plain);
                Assert.Equal(a.VX.Select(v => v * s.Scale), b.VX);
                Assert.Equal(a.VY.Select(v => v * s.Scale), b.VY);
                Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
            }
        }

        /// <summary> one mesher, two computes: the same mesh </summary>
        [Fact]
        public void SegmentPieceInsideAHole_Reuse()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Case(30));
            var mesher = new QualityMesh2 { MinAngle = 30, SnapTolerance = 1.0 / 3, SteinerLimit = 50000, Conforming = true };
            var first = mesher.Compute(c).Mesh; var second = mesher.Compute(c).Mesh;
            Assert.Equal(first.Vertices, second.Vertices);
            Assert.Equal(first.Triangles, second.Triangles);
        }
    }
}
