using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// OracleReport.BelowMpwFloorExcused (HP 6 option 1): of the triangles below the angle that the oracle excuses as spanning a small
    /// input angle, those below the floor arctan(sin phi / (2 - cos phi)) of Miller, Pav and Walkington 2005, exactly. A report, no rule:
    /// the verdicts are not changed. The expected counts are the column below_mpw_floor of
    /// benchmarks/Meshing2HardProblems/perf/hp6-net10.csv (the floating-point classifier of Hp6.cs), on the same inputs and meshers.
    /// </summary>
    public class Meshing2OracleMpwFloorTests
    {
        static double Rad(double deg) => deg * Math.PI / 180;

        /// <summary> Hp6's families on their box: boxwedge (two segments from the origin, phi apart) and fan5 (five, neighbours phi apart) </summary>
        static (List<Vector2d> points, List<(int, int)> segments) Family(string family, double phi)
        {
            var p = new List<Vector2d> { new Vector2d(-1, -1), new Vector2d(2, -1), new Vector2d(2, 2), new Vector2d(-1, 2), new Vector2d(0, 0) };
            var s = new List<(int, int)>();
            void Spoke(double deg) { p.Add(new Vector2d(Math.Cos(Rad(deg)), Math.Sin(Rad(deg)))); s.Add((4, p.Count - 1)); }
            if (family == "boxwedge") { Spoke(0); Spoke(phi); }
            else for (int i = 0; i < 5; ++i) Spoke(phi * i);
            return (p, s);
        }

        static OracleReport Judge(string family, double phi, double bound, bool constrained, out Mesh2Input input)
        {
            var (points, segments) = Family(family, phi);
            var c = new ConstrainedMesh2Input();
            c.Points.AddRange(points);
            c.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }, false));
            foreach (var (a, b) in segments) c.Segments.Add(new Mesh2Segment(a, b));
            var result = new QualityMesh2 { MinAngle = bound, Conforming = !constrained, SteinerLimit = 400000 }.Compute(c);
            var mesh = result.Mesh;
            input = new Mesh2Input { X = points.Select(q => q.x).ToArray(), Y = points.Select(q => q.y).ToArray(), MinAngleDeg = bound, ConstrainedQuality = constrained };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            foreach (var (a, b) in segments) input.Segments.Add(new SegmentInput(a, b));
            var output = new Mesh2Output {
                Status = Mesh2Status.Success, VX = mesh.Vertices.Select(q => q.x).ToArray(), VY = mesh.Vertices.Select(q => q.y).ToArray(),
                InputToVertex = mesh.InputToVertex.ToArray(), Triangles = mesh.Triangles.Select(t => new[] { t.a, t.b, t.c }).ToArray(),
                Neighbours = mesh.Neighbours.Select(t => new[] { t.a, t.b, t.c }).ToArray(),
                Subsegments = mesh.Subsegments.Select(q => new SubsegmentOutput(q.A, q.B, q.Marker)).ToList(),
                TriangleLabels = mesh.TriangleLabels.ToArray(), BadTriangles = mesh.BadTriangles.ToArray(), SteinerLimitReached = mesh.SteinerLimitReached };
            return Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
        }

        // family, phi, bound, constrained, below the bound (all excused), below the floor (hp6-net10.csv: below, below_mpw_floor)
        [Theory]
        [InlineData("boxwedge", 40.0, 20.0, true, 1, 1)]
        [InlineData("boxwedge", 20.0, 20.0, false, 1, 0)]
        [InlineData("boxwedge", 20.0, 20.0, true, 1, 1)]
        [InlineData("boxwedge", 10.0, 20.0, false, 2, 1)]
        [InlineData("fan5", 20.0, 20.0, false, 4, 3)]
        [InlineData("fan5", 20.0, 20.0, true, 4, 4)]
        [InlineData("fan5", 1.0, 20.0, false, 8, 4)]
        [InlineData("boxwedge", 0.1, 20.0, true, 2, 1)]
        public void CountsTheExcusedTrianglesBelowTheFloor_AsTheClassifierOfHp6(string family, double phi, double bound, bool constrained, int below, int floor)
        {
            var report = Judge(family, phi, bound, constrained, out _);
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(below, report.BelowAngle);
            Assert.Equal(floor, report.BelowMpwFloorExcused);
            Assert.Equal(floor > 0, report.ExcusedSummary().Contains("floor of Miller, Pav and Walkington"));
        }

        /// <summary> the exact comparison against the angles in doubles, for angles of 1e-6 to 60 degrees and wedges of 1e-3 to 60 degrees,
        /// away from the boundary case (where doubles cannot decide); at the boundary itself (theta equal to the floor) it is false </summary>
        [Fact]
        public void BelowMpwFloor_AgreesWithTheAnglesInDoubles()
        {
            int below = 0, above = 0;
            foreach (double phiDeg in new[] { 1e-3, 0.1, 1, 10, 20, 30, 45, 60 })
                foreach (double thetaDeg in new[] { 1e-6, 1e-3, 0.05, 0.5, 5, 10, 15, 20, 25, 30, 40, 60 }) {
                    double phi = Rad(phiDeg), theta = Rad(thetaDeg), floor = Math.Atan(Math.Sin(phi) / (2 - Math.Cos(phi)));
                    if (Math.Abs(theta - floor) < 1e-9 * Math.Max(theta, floor)) continue;
                    Q s = Q.FromDouble(Math.Sin(theta) * Math.Sin(theta)), big = Q.FromDouble(Math.Sin(phi) * Math.Sin(phi));
                    bool want = theta < floor;
                    Assert.Equal(want, Exact.BelowMpwFloor(s, big));
                    if (want) ++below; else ++above;
                }
            Assert.True(below > 20 && above > 20, below + " / " + above);
            // equality: a right isosceles-like exact case is not constructible with rational sines, so the boundary is probed from both sides
            // in rationals: s just below and just above S / (5 - 4 cos phi) for cos phi = 3/5 (S = 16/25): the floor's sin^2 is (16/25) / (13/5) = 16/65
            Assert.True(Exact.BelowMpwFloor(new Q(16, 65) - new Q(1, 1000000), new Q(16, 25)));
            Assert.False(Exact.BelowMpwFloor(new Q(16, 65), new Q(16, 25)));
            Assert.False(Exact.BelowMpwFloor(new Q(16, 65) + new Q(1, 1000000), new Q(16, 25)));
        }

        /// <summary> the hand-built case of the oracle's self-check for #664: a hull domain with a segment S that ends d off the bottom edge
        /// (52.1 degrees to it), the triangle p E1 q with an angle of about 3 degrees at E1, far below the floor of the pair (29.6 degrees).
        /// d = 0: the constraints meet exactly; d = 1e-13: S's end is in the tube of the bottom edge, so the excuse is the near-meet one
        /// (NearMeetAngleExcused). The counter counts through both </summary>
        [Theory]
        [InlineData(0.0, false)]
        [InlineData(1e-13, true)]
        public void NearMeetAndExactExcuses_CountBelowTheFloor(double d, bool nearMeet)
        {
            var input = new Mesh2Input { X = new[] { 0.0, 30, 24, 10 }, Y = new[] { 0.0, 0, 18, d }, MinAngleDeg = 20 };
            input.Segments.Add(new SegmentInput(3, 2));
            double len = Math.Sqrt(14 * 14 + (18 - d) * (18 - d)), ux = 14 / len, uy = (18 - d) / len;
            var tris = new[] { new[] { 0, 3, 5 }, new[] { 0, 5, 2 }, new[] { 3, 4, 5 }, new[] { 4, 1, 5 }, new[] { 5, 1, 2 } };
            var output = new Mesh2Output { Status = Mesh2Status.Success, VX = new[] { 0.0, 30, 24, 10, 11, 10 + 1.2 * ux }, VY = new[] { 0.0, 0, 18, d, 0, d + 1.2 * uy },
                InputToVertex = new[] { 0, 1, 2, 3 }, Triangles = tris, Neighbours = SelfCheck.NeighboursOf(tris), TriangleLabels = new[] { 0, 0, 0, 0, 0 },
                Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(3, 5), new SubsegmentOutput(5, 2) }, BadTriangles = new[] { 0, 3 } };
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
            Assert.DoesNotContain(report.Violations, x => x.Kind == Violation.AngleBelowBound && x.Detail.StartsWith("triangle 4 1 5 "));
            if (nearMeet) Assert.True(report.NearMeetAngleExcused > 0, report.ExcusedSummary());
            Assert.True(report.BelowMpwFloorExcused >= 1, report.ExcusedSummary());
            // every near-meet excuse of this mesh is below the floor: the triangles it excuses have angles of a few degrees
            Assert.True(report.NearMeetAngleExcused <= report.BelowMpwFloorExcused, report.ExcusedSummary());
        }

        /// <summary> a mesh without small input angles excuses nothing by them: the counter stays 0 </summary>
        [Fact]
        public void NoSmallAngle_NoCount()
        {
            var report = Judge("boxwedge", 90.0, 20.0, false, out _);
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(0, report.BelowMpwFloorExcused);
            Assert.DoesNotContain("Walkington", report.ExcusedSummary());
        }
    }
}
