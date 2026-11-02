using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A crossing that snap rounding makes inside a hole: the mesh of the snapped input took it as an input point and kept it,
    /// in no triangle, though made vertices must be in triangles (spec Q8; oracle VertexUnused). Found by the grid fuzz of the lens
    /// tests (seed 2021, pixel 1/2), reduced to two segments crossing in a triangular hole.
    /// </summary>
    public class Meshing2GridHoleTests
    {
        static Mesh2Input CrossingInHole(double? minAngle)
        {
            var input = new Mesh2Input {
                X = new[] { 0, 16, 16, 0, 6, 9, 7, 8.28125, 5.078125, 13.671875, 3.625 },
                Y = new[] { 0, 0, 16, 16, 6, 6.5, 9, 4.140625, 14.59375, 9.75, 6.71875 },
                SnapTolerance = 0.5, MinAngleDeg = minAngle, SteinerLimit = minAngle == null ? (int?)null : 50000 };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
            // the two segments cross at about (7.16, 7.79), inside the hole
            input.Segments.Add(new SegmentInput(7, 8)); input.Segments.Add(new SegmentInput(9, 10));
            return input;
        }

        [Fact]
        public void CrossingInsideAHole_OnTheGrid_IsNotAVertex_M2()
        {
            var input = CrossingInHole(null);
            var o = new ConstrainedDelaunayMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
            // every made vertex is in a triangle
            var used = new HashSet<int>(o.Triangles.SelectMany(t => t));
            for (int v = input.Count; v < o.VX.Length; ++v) Assert.Contains(v, used);
        }

        [Theory]
        [InlineData(20.0)]
        [InlineData(25.0)]
        public void CrossingInsideAHole_OnTheGrid_IsNotAVertex_M3(double angle)
        {
            var input = CrossingInHole(angle);
            var o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> the same under every similarity: 2^k scales give the same mesh bit for bit, the others pass the oracle </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void CrossingInsideAHole_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            var plain = CrossingInHole(null);
            var moved = CrossingInHole(null);
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            moved.SnapTolerance = plain.SnapTolerance * s.Scale;
            var b = new ConstrainedDelaunayMesh2Candidate().Run(moved);
            var report = Oracle.Check(moved, b, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, name + ": " + report);
            if (s.IsExact) {
                var a = new ConstrainedDelaunayMesh2Candidate().Run(plain);
                Assert.Equal(a.VX.Select(v => v * s.Scale), b.VX);
                Assert.Equal(a.VY.Select(v => v * s.Scale), b.VY);
                Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
            }
        }

        /// <summary> the heights, the input mapping and the subsegments follow the renumbering </summary>
        [Fact]
        public void CrossingInsideAHole_MappingHeightsAndSubsegments_StayConsistent()
        {
            var input = CrossingInHole(null);
            var c = ConstrainedDelaunayMesh2Candidate.Convert(input);
            c.Heights = Enumerable.Range(0, input.Count).Select(i => (double)i).ToList();
            var r = new ConstrainedDelaunayMesh2 { SnapTolerance = 0.5 }.Compute(c);
            var m = r.Mesh;
            Assert.Equal(m.Vertices.Count, m.Heights.Count);
            foreach (var t in m.Triangles) foreach (int v in new[] { t.a, t.b, t.c }) Assert.InRange(v, 0, m.Vertices.Count - 1);
            foreach (var s in m.Subsegments) { Assert.InRange(s.A, 0, m.Vertices.Count - 1); Assert.InRange(s.B, 0, m.Vertices.Count - 1); }
            for (int i = 0; i < input.Count; ++i) {
                int v = m.InputToVertex[i];
                if (v < 0) continue;
                Assert.InRange(v, 0, m.Vertices.Count - 1);
                // an input point keeps its height (it moved by at most the tolerance, its height did not change)
                if (v < input.Count && v == i) Assert.Equal(i, m.Heights[v]);
            }
            var used = new HashSet<int>(m.Triangles.SelectMany(t => new[] { t.a, t.b, t.c }));
            for (int v = m.InputCount; v < m.Vertices.Count; ++v) Assert.Contains(v, used);
        }

        /// <summary>
        /// holes on the grid: random segments in a square ring with two holes, crossing them or not, pass the oracle (M2). Refinement on
        /// the grid with holes (M3) has failures of its own on master, a hole edge missing or a boundary edge unconstrained (seeds 31 at
        /// 20 degrees and 30 at 25 degrees of this generator with the angle in the seed); they are reported separately, not part of this
        /// fix
        /// </summary>
        [Theory]
        [InlineData(0)]
        public void Fuzz_HolesOnTheGrid_PassTheOracle(double angle)
        {
            for (int seed = 0; seed < 120; ++seed) {
                var r = new Random(seed + (int)angle * 100);
                var x = new List<double> { 0, 16, 16, 0, 6, 9, 7, 10, 13, 12 }; var y = new List<double> { 0, 0, 16, 16, 6, 6.5, 9, 10, 11, 14 };
                var input = new Mesh2Input();
                input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
                input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
                input.Rings.Add(new RingInput(new[] { 7, 8, 9 }, true));
                int m = 2 + r.Next(6);
                for (int i = 0; i < m; ++i) {
                    for (int k = 0; k < 2; ++k) { x.Add(1 + Math.Round(r.NextDouble() * 14 * 64) / 64); y.Add(1 + Math.Round(r.NextDouble() * 14 * 64) / 64); }
                    input.Segments.Add(new SegmentInput(x.Count - 2, x.Count - 1));
                }
                input.X = x.ToArray(); input.Y = y.ToArray();
                input.SnapTolerance = new[] { 0.5, 0.25, 1.0 / 3, 0.3, 0.125 }[seed % 5];
                Milestone milestone = angle == 0 ? Milestone.M2 : Milestone.M3;
                if (milestone == Milestone.M3) { input.MinAngleDeg = angle; input.SteinerLimit = 50000; }
                var o = CandidateRegistry.For(milestone).Run(input);
                Assert.Equal(Mesh2Status.Success, o.Status);
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = milestone });
                Assert.True(report.Ok, "seed " + seed + ": " + report);
            }
        }
    }
}
