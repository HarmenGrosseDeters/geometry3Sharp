using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #870 (#864 part B, MeshFuzzPilot fuzz #624 group f, the last dump d6710852 and three of the resumed group): the mesher refuses a
    /// piece whose split point lies within the tolerance of another constraint (O11, NearOtherSource), and the oracle excuses the
    /// Delaunay and angle tests behind such a piece. Its split point is the midpoint, or the shell point next to the one end that existed
    /// before refinement (input points and crossings). The output did not say which ends existed before, and a crossing lies within the
    /// tolerance of two constraints as a split point there may, so the oracle demanded both points refused: here the piece from an input
    /// point to a crossing, refused at its midpoint, was judged by a shell point the mesher never takes (NotDelaunayGlobal). The output
    /// now reports which vertices existed before refinement and each piece's sources (ConstrainedMesh2.ExistedBeforeRefinement and
    /// SubsegmentSources, internal; Mesh2Output), and the oracle models the mesher's split point from them. The mesher's output is
    /// unchanged.
    /// </summary>
    public class Meshing2OracleReportedProvenanceTests
    {
        sealed class Case
        {
            public string Name;
            public double[][] Corners;
            public int[] Triangles;
            public double? Angle;
            public bool Conforming;
        }

        static readonly int[] Cube = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 };
        static readonly int[] TwoCubes = Cube.Concat(Cube.Select(i => i + 8)).ToArray();
        static readonly int[] Octahedron = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };

        static readonly Case[] Cases = {
            // crash-d6710852: two nested cubes near (-2.2e-29, -5.8e-29), no angle bound, conforming (options 0 142); the piece from
            // input point 8 to the crossing 36 is refused at its midpoint (base: NotDelaunayGlobal, vertex 11 and 7 33 32)
            new Case { Name = "d6710852", Conforming = true, Triangles = TwoCubes, Corners = new[] {
                new[] { -2.208810534618833E-29, -5.837570698635487E-29, 2.3665827156630354E-30 }, new[] { -2.2088105345820989E-29, -5.837570698635487E-29, 2.3665827156630354E-30 },
                new[] { -2.2088105345820989E-29, -5.837570698598753E-29, 2.3665827156630354E-30 }, new[] { -2.2088105346073536E-29, -5.837570698605641E-29, 2.3665827156400765E-30 },
                new[] { -2.2088105344604168E-29, -5.837570698477071E-29, 2.3665827176145397E-30 }, new[] { -2.2088105344236826E-29, -5.837570698477071E-29, 2.3665827176145397E-30 },
                new[] { -2.2088105345820989E-29, -5.837570698598753E-29, 2.3665827160303774E-30 }, new[] { -2.208810534618833E-29, -5.837570698598753E-29, 2.3665827160303774E-30 },
                new[] { -2.208810534846126E-29, -5.83757069861712E-29, 2.3665827158467064E-30 }, new[] { -2.2088105345637318E-29, -5.83757069861712E-29, 2.3665827158467064E-30 },
                new[] { -2.2088105345637318E-29, -5.837570698580386E-29, 2.3665827158467064E-30 }, new[] { -2.208810534600466E-29, -5.837570698580386E-29, 2.3665827158467064E-30 },
                new[] { -2.208810534600466E-29, -5.83757069861712E-29, 2.3665827162140484E-30 }, new[] { -2.2088105345637318E-29, -5.83757069861712E-29, 2.3665827162140484E-30 },
                new[] { -2.2088105345637318E-29, -5.837570698580386E-29, 2.3665827162140484E-30 }, new[] { -2.208810534600466E-29, -5.837570698580386E-29, 2.3665827162140484E-30 } } },
            // resumed crash-6a3aaa34: a projected octahedron at 3.6e-26, 20 degrees (base: NotDelaunayLocal, NotDelaunayGlobal)
            new Case { Name = "6a3aaa34", Angle = 20, Triangles = Octahedron, Corners = new[] {
                new[] { 3.635071051296038E-26, 3.6350710512584224E-26, -3.6350710512584224E-26 }, new[] { 3.6350710510632903E-26, 3.635071051100906E-26, -3.6350710514159386E-26 },
                new[] { 3.635071051100906E-26, 3.635071051138522E-26, -3.6350710514159386E-26 }, new[] { 3.635071051150277E-26, 3.635071051112661E-26, -3.635071051366568E-26 },
                new[] { 3.6350710512584224E-26, 3.6350710512584224E-26, -3.6350710512208066E-26 }, new[] { 3.635071051364217E-26, 3.635071051364217E-26, -3.6350710514535545E-26 } } },
            // resumed crash-8afddcaf: a projected octahedron, 25 degrees (base: NotDelaunayGlobal)
            new Case { Name = "8afddcaf", Angle = 25, Triangles = Octahedron, Corners = new[] {
                new[] { 3.635071051314846E-26, 3.6350710512584224E-26, -1.0178198943523583E-25 }, new[] { 3.6350710512208066E-26, 3.6350710512584224E-26, -1.0178198943523583E-25 },
                new[] { 3.6350710512584224E-26, 3.635071051296038E-26, -1.0178198943523583E-25 }, new[] { 3.6350710510068666E-26, 3.635071050969251E-26, -1.0178198943775139E-25 },
                new[] { 3.6350710510068666E-26, 3.6350710510068666E-26, -1.0178198943737523E-25 }, new[] { 3.635071051368919E-26, 3.6350710512560714E-26, -1.0178198943455404E-25 } } },
            // resumed crash-a4805a84: a projected octahedron, 25 degrees (base: NotDelaunayGlobal)
            new Case { Name = "a4805a84", Angle = 25, Triangles = Octahedron, Corners = new[] {
                new[] { 3.6350710514018327E-26, 3.635071051364217E-26, 3.635071051364217E-26 }, new[] { 3.635071051326601E-26, 3.635071051364217E-26, 3.6350710512161046E-26 },
                new[] { 3.6350710512584224E-26, 3.635071051296038E-26, 3.6350710512584224E-26 }, new[] { 3.6350710512701773E-26, 3.6350710512231576E-26, 3.635071051359515E-26 },
                new[] { 3.6350710512513694E-26, 3.6350710512584224E-26, 3.635071051296038E-26 }, new[] { 3.6350710512584224E-26, 3.6350710512584224E-26, 3.6350710512208066E-26 } } },
        };

        public static IEnumerable<object[]> Names() => Cases.Select(c => new object[] { c.Name });

        static int Exponent(double x)
        {
            long b = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((b >> 52) & 0x7FF);
            return be == 0 ? Exponent(x * Math.Pow(2, 54)) - 54 : be - 1023;
        }

        /// <summary> the fuzz target's pipeline: scaled by the power of two that brings the box's diagonal to [1, 2), projected to xy, the
        /// mesh edges as constraints </summary>
        static Mesh2Input Input(Case c, Func<Vector2d, Vector2d> map = null)
        {
            var m = new DMesh3();
            foreach (var p in c.Corners) m.AppendVertex(new Vector3d(p[0], p[1], p[2]));
            for (int t = 0; t < c.Triangles.Length; t += 3) m.AppendTriangle(c.Triangles[t], c.Triangles[t + 1], c.Triangles[t + 2]);
            AxisAlignedBox3d box = m.GetBounds();
            double scale = Math.Pow(2, -Exponent((box.Max - box.Min).Length));
            var x = new List<double>(); var y = new List<double>();
            foreach (int v in m.VertexIndices()) {
                Vector3d q = scale * m.GetVertex(v);
                Vector2d p = new Vector2d(q.x, q.y);
                if (map != null) p = map(p);
                x.Add(p.x); y.Add(p.y);
            }
            var input = new Mesh2Input { X = x.ToArray(), Y = y.ToArray(), MinAngleDeg = c.Angle, Conforming = c.Conforming, SteinerLimit = 1500 };
            var seen = new HashSet<(int, int)>();
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e);
                if (x[ev.a] == x[ev.b] && y[ev.a] == y[ev.b]) continue;
                if (seen.Add((Math.Min(ev.a, ev.b), Math.Max(ev.a, ev.b)))) input.Segments.Add(new SegmentInput(ev.a, ev.b));
            }
            return input;
        }

        static OracleReport Check(Mesh2Input input, Mesh2Output o) => Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });

        /// <summary> the mesher's result is accepted, and the excuse at the refused pieces is what accepts it </summary>
        [Theory]
        [MemberData(nameof(Names))]
        public void FuzzCase_RefusalModelledFromTheReport_IsAccepted(string name)
        {
            Case c = Cases.First(k => k.Name == name);
            Mesh2Input input = Input(c);
            Mesh2Output o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            Assert.True(o.Triangles.Length > 10 && o.VX.All(v => !double.IsNaN(v) && !double.IsInfinity(v)) && o.VY.All(v => !double.IsNaN(v) && !double.IsInfinity(v)));
            Assert.NotNull(o.SubsegmentSources);
            Assert.NotNull(o.ExistedBeforeRefinement);
            var report = Check(input, o);
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.O11GlobalExcused + report.O11AngleExcused + report.O11LocalExcused > 0, report.ExcusedSummary());
        }

        /// <summary> without the report the oracle takes every possibility, as for a foreign producer, and judges d6710852 as before;
        /// a report it cannot believe (the refused piece's source replaced by a constraint far from it) gives no excuse either </summary>
        [Fact]
        public void FuzzCase_WithoutOrWithAWrongReport_IsJudged()
        {
            Case c = Cases.First(k => k.Name == "d6710852");
            Mesh2Input input = Input(c);
            Mesh2Output o = new QualityMesh2Candidate().Run(input);
            var sources = o.SubsegmentSources;
            var before = o.ExistedBeforeRefinement;
            o.SubsegmentSources = null; o.ExistedBeforeRefinement = null;
            var report = Check(input, o);
            Assert.Contains(report.Violations, v => v.Kind == Violation.NotDelaunayGlobal);
            // (the piece's ends as the oracle numbers them: the input point 8 and the crossing at the end of 8's constraint to 10 that
            // the mesher reports as existing before refinement)
            o.ExistedBeforeRefinement = before;
            bool crossing(int v) => v >= input.Count && before[v];
            int piece = Enumerable.Range(0, o.Subsegments.Count).Single(i => (o.Subsegments[i].A == 8 && crossing(o.Subsegments[i].B)) || (o.Subsegments[i].B == 8 && crossing(o.Subsegments[i].A)));
            o.SubsegmentSources = sources.Select(z => z.ToArray()).ToArray();
            int far = Enumerable.Range(0, input.Segments.Count).First(s => input.Segments[s].A != 8 && input.Segments[s].B != 8
                && Math.Abs(input.X[input.Segments[s].A] - input.X[8]) > 0.5 && Math.Abs(input.X[input.Segments[s].B] - input.X[8]) > 0.5);
            o.SubsegmentSources[piece] = new[] { far };
            report = Check(input, o);
            Assert.Contains(report.Violations, v => v.Kind == Violation.NotDelaunayGlobal);
        }

        /// <summary> under every similarity the mesher's result is accepted wherever the case survives the mapping; at the exact ones (2^k)
        /// the output, the report and the oracle's verdict are the same, bit for bit </summary>
        [Fact]
        public void FuzzCase_UnderEverySimilarity()
        {
            Case c = Cases.First(k => k.Name == "d6710852");
            Mesh2Input input0 = Input(c);
            Mesh2Output o0 = new QualityMesh2Candidate().Run(input0);
            var r0 = Check(input0, o0);
            Assert.True(r0.Ok, r0.ToString());
            int checkedCases = 0;
            var skipped = new List<string>();
            foreach (Similarity s in Similarity.All2D()) {
                Mesh2Input input = Input(c, p => s.Apply2d(p));
                Mesh2Output o = new QualityMesh2Candidate().Run(input);
                if (!s.IsExact && o.Status != Mesh2Status.Success) { skipped.Add(s.Name); continue; }
                Assert.True(o.Status == Mesh2Status.Success, s.Name);
                var report = Check(input, o);
                Assert.True(report.Ok, s.Name + ": " + report);
                if (s.IsExact) {
                    Assert.Equal(o0.Triangles.SelectMany(t => t), o.Triangles.SelectMany(t => t));
                    for (int i = 0; i < o0.VX.Length; ++i) Assert.Equal(s.Apply2d(new Vector2d(o0.VX[i], o0.VY[i])), new Vector2d(o.VX[i], o.VY[i]));
                    Assert.Equal(o0.ExistedBeforeRefinement, o.ExistedBeforeRefinement);
                    Assert.Equal(o0.SubsegmentSources.SelectMany(z => z.Append(-2)), o.SubsegmentSources.SelectMany(z => z.Append(-2)));
                    Assert.Equal(r0.O11GlobalExcused, report.O11GlobalExcused);
                    Assert.Equal(r0.O11AngleExcused, report.O11AngleExcused);
                    Assert.Equal(r0.O11LocalExcused, report.O11LocalExcused);
                }
                checkedCases++;
            }
            Assert.Empty(skipped);
            Assert.True(checkedCases > 4);
        }

        // ---- the report itself (library) ----

        /// <summary> a square ring and two segments crossing at 4 degrees inside it (refined at 25 degrees and an area of 2) </summary>
        static ConstrainedMesh2Input Crossing()
        {
            var input = new ConstrainedMesh2Input();
            double t = Math.Tan(2 * Math.PI / 180);
            input.Points.AddRange(new[] { new Vector2d(0, 0), new Vector2d(16, 0), new Vector2d(16, 16), new Vector2d(0, 16),
                new Vector2d(2, 8 - 6 * t), new Vector2d(14, 8 + 6 * t), new Vector2d(2, 8 + 6 * t), new Vector2d(14, 8 - 6 * t) });
            input.Segments.Add(new Mesh2Segment(4, 5));
            input.Segments.Add(new Mesh2Segment(6, 7));
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
            return input;
        }

        static double Dist(Vector2d p, Vector2d a, Vector2d b) => Math.Sqrt(new Segment2d(a, b).DistanceSquared(p));

        /// <summary>
        /// Input points and the crossing existed before refinement, the refiner's vertices did not; every piece reports its sources, in the
        /// documented numbering (the segments, then the ring's edges), and lies on each of them
        /// </summary>
        [Fact]
        public void QualityMesh2_ReportsWhatExistedBeforeRefinement_AndEachPiecesSources()
        {
            var input = Crossing();
            var mesh = new QualityMesh2 { MinAngle = 25, MaxArea = 2 }.Compute(input).Mesh;
            Assert.NotNull(mesh);
            Assert.Equal(mesh.Vertices.Count, mesh.ExistedBeforeRefinement.Length);
            for (int i = 0; i < input.Points.Count; ++i) Assert.True(mesh.ExistedBeforeRefinement[i]);
            var crossing = new Vector2d(8, 8);
            var made = Enumerable.Range(input.Points.Count, mesh.Vertices.Count - input.Points.Count).ToList();
            int at = made.Single(v => mesh.Vertices[v].Distance(crossing) < 1e-12);
            Assert.True(mesh.ExistedBeforeRefinement[at]);
            Assert.True(made.Count(v => !mesh.ExistedBeforeRefinement[v]) > 20, "made " + made.Count + ", before " + made.Count(v => mesh.ExistedBeforeRefinement[v]));
            Assert.Equal(made.Count - 1, made.Count(v => !mesh.ExistedBeforeRefinement[v]));
            Assert.Equal(mesh.Subsegments.Count, mesh.SubsegmentSources.Length);
            var ends = new List<(Vector2d, Vector2d)> { (input.Points[4], input.Points[5]), (input.Points[6], input.Points[7]) };
            for (int i = 0; i < 4; ++i) ends.Add((input.Points[i], input.Points[(i + 1) % 4]));
            var used = new HashSet<int>();
            for (int i = 0; i < mesh.Subsegments.Count; ++i) {
                Assert.NotEmpty(mesh.SubsegmentSources[i]);
                foreach (int s in mesh.SubsegmentSources[i]) {
                    Assert.InRange(s, 0, ends.Count - 1);
                    used.Add(s);
                    Assert.True(Dist(mesh.Vertices[mesh.Subsegments[i].A], ends[s].Item1, ends[s].Item2) < 1e-12, "piece " + i + " off source " + s);
                    Assert.True(Dist(mesh.Vertices[mesh.Subsegments[i].B], ends[s].Item1, ends[s].Item2) < 1e-12, "piece " + i + " off source " + s);
                }
            }
            Assert.Equal(ends.Count, used.Count);
            // a second run on the same instance reports the same
            var mesher = new QualityMesh2 { MinAngle = 25, MaxArea = 2 };
            var first = mesher.Compute(input).Mesh;
            var second = mesher.Compute(input).Mesh;
            Assert.Equal(first.ExistedBeforeRefinement, second.ExistedBeforeRefinement);
            Assert.Equal(first.SubsegmentSources.SelectMany(z => z.Append(-2)), second.SubsegmentSources.SelectMany(z => z.Append(-2)));
            Assert.Equal(mesh.ExistedBeforeRefinement, first.ExistedBeforeRefinement);
        }

        /// <summary> a piece of two overlapping segments reports both, each piece of the overlap (and only those) </summary>
        [Fact]
        public void QualityMesh2_ReportsBothSourcesOfAnOverlap()
        {
            var input = new ConstrainedMesh2Input();
            input.Points.AddRange(new[] { new Vector2d(0, 0), new Vector2d(16, 0), new Vector2d(16, 16), new Vector2d(0, 16),
                new Vector2d(2, 8), new Vector2d(14, 8), new Vector2d(5, 8), new Vector2d(11, 8) });
            input.Segments.Add(new Mesh2Segment(4, 5));
            input.Segments.Add(new Mesh2Segment(6, 7));
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
            var mesh = new QualityMesh2 { MinAngle = 25, MaxArea = 2 }.Compute(input).Mesh;
            Assert.NotNull(mesh);
            int inside = 0;
            for (int i = 0; i < mesh.Subsegments.Count; ++i) {
                Vector2d a = mesh.Vertices[mesh.Subsegments[i].A], b = mesh.Vertices[mesh.Subsegments[i].B];
                var src = mesh.SubsegmentSources[i].OrderBy(z => z).ToArray();
                if (a.y != 8 || b.y != 8) { Assert.True(src.All(z => z >= 2), "a ring piece reports a segment"); continue; }
                bool overlap = Math.Min(a.x, b.x) >= 5 && Math.Max(a.x, b.x) <= 11;
                Assert.Equal(overlap ? new[] { 0, 1 } : new[] { 0 }, src);
                if (overlap) ++inside;
            }
            Assert.True(inside > 2);
        }

        /// <summary> without refinement every vertex existed before it; with snap rounding (a mesh of the snapped input) nothing is reported </summary>
        [Fact]
        public void ConstrainedDelaunay_ReportsEveryVertexAsBefore_SnapRoundingReportsNothing()
        {
            var input = Crossing();
            var mesh = new ConstrainedDelaunayMesh2().Compute(input).Mesh;
            Assert.All(mesh.ExistedBeforeRefinement, Assert.True);
            Assert.Equal(mesh.Vertices.Count, mesh.ExistedBeforeRefinement.Length);
            Assert.Equal(mesh.Subsegments.Count, mesh.SubsegmentSources.Length);
            Assert.True(mesh.Vertices.Count > input.Points.Count);
            var snapped = new QualityMesh2 { MinAngle = 25, SnapTolerance = 1.0 / 64 }.Compute(input).Mesh;
            Assert.NotNull(snapped);
            Assert.Null(snapped.ExistedBeforeRefinement);
            Assert.Null(snapped.SubsegmentSources);
        }
    }
}
