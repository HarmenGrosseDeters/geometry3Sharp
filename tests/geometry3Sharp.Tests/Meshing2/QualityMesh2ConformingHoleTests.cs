using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Conforming refinement (O2) with a constraint inside a hole: a piece of an input segment that crosses a hole lies between a hole
    /// edge and a vertex on the opposite hole edge. Encroachment counts every triangle with Conforming, but only the domain's
    /// constrained edges were queued, so the piece inside the hole was never looked at; encroached and left as it was, it hid the vertex
    /// across the hole from the hole edge, and the mesh was not Delaunay across the hole (NotDelaunayGlobal). Found with the grid fuzz
    /// with holes in M3 (seeds 551 at 30 degrees and 1051 at 25), reduced to one segment and two loose points.
    /// </summary>
    public class QualityMesh2ConformingHoleTests
    {
        /// <summary> a 16 square with two triangular holes, a segment across the second hole, two loose points, on a grid of 1/4 </summary>
        static Mesh2Input Case(double angle)
        {
            var input = new Mesh2Input {
                X = new[] { 0, 16, 16, 0, 6, 9, 7, 10, 13, 12, 11.453125, 11.78125, 13.921875, 10.921875 },
                Y = new[] { 0, 0, 16, 16, 6, 6.5, 9, 10, 11, 14, 13.234375, 14.375, 5.140625, 6.90625 },
                SnapTolerance = 0.25, MinAngleDeg = angle, SteinerLimit = 50000 };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
            input.Rings.Add(new RingInput(new[] { 7, 8, 9 }, true));
            input.Segments.Add(new SegmentInput(11, 12));
            return input;
        }

        [Theory]
        [InlineData(20.0)]
        [InlineData(25.0)]
        [InlineData(30.0)]
        public void SegmentInsideAHole_IsSplit_TheMeshIsDelaunayAcrossTheHole(double angle)
        {
            var input = Case(angle);
            QualityMesh2.Refiner.CheckTagsForTests = true;
            Mesh2Output o;
            try { o = new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.CheckTagsForTests = false; }
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        /// <summary> the same under every similarity: 2^k scales give the same mesh bit for bit, the others pass the oracle </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void SegmentInsideAHole_Invariance(string name)
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
        public void SegmentInsideAHole_Reuse()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Case(30));
            var mesher = new QualityMesh2 { MinAngle = 30, SnapTolerance = 0.25, SteinerLimit = 50000, Conforming = true };
            var first = mesher.Compute(c).Mesh; var second = mesher.Compute(c).Mesh;
            Assert.Equal(first.Vertices, second.Vertices);
            Assert.Equal(first.Triangles, second.Triangles);
        }
    }
}
