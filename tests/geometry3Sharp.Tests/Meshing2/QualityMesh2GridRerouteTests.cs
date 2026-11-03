using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Refinement on the snap grid: a split point that rounds onto a vertex already there (the vertex of another constraint) reroutes
    /// the piece through that vertex without rewriting the triangles between the old piece and the new chain, and they change sides
    /// of the boundary. Their tags must change with them. Found with the grid fuzz with holes in M3: a hole edge rerouted through a
    /// segment's vertex inside the hole left a domain triangle tagged as hole (SegmentMissing, BoundaryNotConstrained), and the other
    /// way round a hole triangle tagged as domain.
    /// </summary>
    public class QualityMesh2GridRerouteTests
    {
        /// <summary> a 16 square with a triangular hole and the segments of the reduced fuzz cases, on a grid of the pixel </summary>
        internal static Mesh2Input Case(int which, double? minAngle)
        {
            var x = new List<double> { 0, 16, 16, 0, 6, 9, 7 }; var y = new List<double> { 0, 0, 16, 16, 6, 6.5, 9 };
            var input = new Mesh2Input();
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
            // 292: found again after the carve (T15) took case 30's reroute away, whose target vertex lay inside the hole; it fails without
            // the reroute fix as well (oracle INVALID before #337)
            var segments = which == 30 ? new[] { (3.328125, 3.90625, 8.96875, 7.515625) }
                : which == 292 ? new[] { (5.65625, 12.875, 11.765625, 1.15625) }
                : new[] { (7.234375, 13.734375, 6.078125, 4.453125), (2.78125, 4.375, 8.75, 11.5) };
            foreach (var (ax, ay, bx, by) in segments) {
                x.Add(ax); y.Add(ay); x.Add(bx); y.Add(by);
                input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
            }
            input.X = x.ToArray(); input.Y = y.ToArray();
            input.SnapTolerance = which == 31 ? 0.25 : 0.5;
            input.MinAngleDeg = minAngle; input.SteinerLimit = 50000;
            return input;
        }

        internal static Mesh2Output RunChecked(Mesh2Input input)
        {
            QualityMesh2.Refiner.CheckTagsForTests = true;
            try { return new QualityMesh2Candidate().Run(input); }
            finally { QualityMesh2.Refiner.CheckTagsForTests = false; }
        }

        [Theory]
        [InlineData(30, 0.0)]
        [InlineData(30, 20.0)]
        [InlineData(30, 25.0)]
        [InlineData(31, 0.0)]
        [InlineData(31, 20.0)]
        [InlineData(31, 25.0)]
        [InlineData(292, 20.0)]
        public void SplitOntoAVertexAlreadyThere_RetagsTheTrianglesBetween(int which, double angle)
        {
            var input = Case(which, angle);
            // the tags against domain membership from scratch after every operation (throws on the first difference)
            var o = RunChecked(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> the same under every similarity: 2^k scales give the same mesh bit for bit, the others pass the oracle </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void SplitOntoAVertexAlreadyThere_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            foreach (int which in new[] { 30, 31, 292 }) {
                var plain = Case(which, 20);
                var moved = Case(which, 20);
                for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
                moved.SnapTolerance = plain.SnapTolerance * s.Scale;
                var b = new QualityMesh2Candidate().Run(moved);
                Assert.Equal(Mesh2Status.Success, b.Status);
                var report = Oracle.Check(moved, b, new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(report.Ok, name + ", case " + which + ": " + report);
                if (s.IsExact) {
                    var a = new QualityMesh2Candidate().Run(plain);
                    Assert.Equal(a.VX.Select(v => v * s.Scale), b.VX);
                    Assert.Equal(a.VY.Select(v => v * s.Scale), b.VY);
                    Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
                }
            }
        }

        /// <summary> one mesher, two computes: the same mesh </summary>
        [Fact]
        public void SplitOntoAVertexAlreadyThere_Reuse()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Case(31, 20));
            var mesher = new QualityMesh2 { MinAngle = 20, SnapTolerance = 0.25, SteinerLimit = 50000 };
            var first = mesher.Compute(c).Mesh; var second = mesher.Compute(c).Mesh;
            Assert.Equal(first.Vertices, second.Vertices);
            Assert.Equal(first.Triangles, second.Triangles);
        }
    }
}
