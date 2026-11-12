using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// No silent deletion (roadmap meshing2d, open item of 2026-11-10): QualityMesh2 drops a part of the domain narrower than the automatic
    /// tolerance, eg the sliver tail of a hull domain (the MeshFuzzPilot input of QualityMesh2VanishingPartTests: points 0, 1 and 2 merge,
    /// the tail to point 3, 1.7 away, keeps no triangle). The oracle excuses such a part (OracleReport.VanishedPoints / VanishedSegments);
    /// the mesher now reports it in its diagnostics, with the input points and segments it left in no triangle. The mesh is unchanged.
    /// </summary>
    public class QualityMesh2DroppedPartDiagnosticTests
    {
        static readonly double[] X = { -9.04844107523677E-87, -1.7763568394002505E-13, 4.689582056016661E-13, -1.7304660291117036, -3.197442310920451E-13, 4.547473508864641E-13, 8.171241461241152E-13, 9.094947017729282E-13 };
        static readonly double[] Y = { 5.780948464734603E-86, 7.105427357601002E-14, -4.547473508864641E-13, -1.4127834472400946E-104, -4.902744876744691E-13, 5.780948464734603E-86, -4.973799150320701E-13, 3.126388037344441E-13 };
        static readonly int[][] Segments = { new[] { 0, 2 }, new[] { 1, 2 }, new[] { 0, 1 }, new[] { 1, 3 }, new[] { 0, 3 }, new[] { 2, 3 }, new[] { 4, 6 }, new[] { 5, 6 }, new[] { 4, 5 }, new[] { 5, 7 }, new[] { 4, 7 }, new[] { 6, 7 } };

        static Mesh2Input Sliver(bool constrainedQuality)
        {
            var input = new Mesh2Input { X = (double[])X.Clone(), Y = (double[])Y.Clone(), MinAngleDeg = 20, ConstrainedQuality = constrainedQuality, SteinerLimit = 1500 };
            foreach (var s in Segments) input.Segments.Add(new SegmentInput(s[0], s[1]));
            return input;
        }

        /// <summary> the unit square with two crossing diagonals and a point inside: nothing vanishes </summary>
        static Mesh2Input Square(bool constrainedQuality)
        {
            var input = new Mesh2Input { X = new double[] { 0, 1, 1, 0, 0.3 }, Y = new double[] { 0, 0, 1, 1, 0.6 }, MinAngleDeg = 20, ConstrainedQuality = constrainedQuality, SteinerLimit = 1500 };
            foreach (var s in new[] { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 }, new[] { 0, 2 }, new[] { 1, 3 } }) input.Segments.Add(new SegmentInput(s[0], s[1]));
            return input;
        }

        static QualityMesh2 Mesher(Mesh2Input input) => new QualityMesh2 { MinAngle = input.MinAngleDeg ?? 0, Conforming = !input.ConstrainedQuality, SteinerLimit = input.SteinerLimit };

        static (ConstrainedTriangulation2Result Result, OracleReport Report) Run(Mesh2Input input, string what)
        {
            var result = Mesher(input).Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
            Assert.True(result.Status == Triangulation2Status.Success, what + ": " + result.Status + " " + result.FailureKind);
            var report = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, what + ": " + report + " / " + report.ExcusedSummary());
            return (result, report);
        }

        /// <summary> the mesher's dropped points and segments are the oracle's vanished ones (its segment sources are numbered segments
        /// first), and the diagnostic names them </summary>
        static void AssertReportMatchesOracle(ConstrainedTriangulation2Result result, OracleReport report, int segmentCount, string what)
        {
            var mesh = result.Mesh;
            Assert.Equal(report.VanishedPointIds.OrderBy(i => i), mesh.DroppedInputPoints);
            Assert.Equal(report.VanishedSourceIds.Where(i => i < segmentCount).OrderBy(i => i), mesh.DroppedInputSegments);
            var used = new HashSet<int>(mesh.Triangles.SelectMany(t => new[] { t.a, t.b, t.c }));
            foreach (int i in mesh.DroppedInputPoints) Assert.False(used.Contains(mesh.InputToVertex[i]), what + ": dropped point " + i + " is in a triangle");
            var line = result.Diagnostics.Where(d => d.Contains("vanished in refinement")).ToList();
            if (mesh.DroppedInputPoints.Length + mesh.DroppedInputSegments.Length == 0) Assert.Empty(line);
            else {
                Assert.Single(line);
                Assert.Contains(mesh.DroppedInputPoints.Length + " input points and " + mesh.DroppedInputSegments.Length + " input segments are in no triangle", line[0]);
                Assert.Contains("(points " + string.Join(", ", mesh.DroppedInputPoints) + "; segments " + string.Join(", ", mesh.DroppedInputSegments) + ")", line[0]);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SliverTail_IsReported_AsTheOracleExcusesIt(bool constrainedQuality)
        {
            var input = Sliver(constrainedQuality);
            var r = Run(input, "sliver");
            Assert.Equal(new[] { 3 }, r.Result.Mesh.DroppedInputPoints);
            Assert.Equal(new[] { 3, 4, 5 }, r.Result.Mesh.DroppedInputSegments);
            AssertReportMatchesOracle(r.Result, r.Report, input.Segments.Count, "sliver");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NoVanishedPart_ReportsNone(bool constrainedQuality)
        {
            var input = Square(constrainedQuality);
            var r = Run(input, "square");
            Assert.Empty(r.Result.Mesh.DroppedInputPoints);
            Assert.Empty(r.Result.Mesh.DroppedInputSegments);
            Assert.Empty(r.Report.VanishedPointIds);
            AssertReportMatchesOracle(r.Result, r.Report, input.Segments.Count, "square");
        }

        public static TheoryData<string> Similarities()
        {
            var data = new TheoryData<string>();
            foreach (var s in Similarity.All2D()) data.Add(s.Name);
            return data;
        }

        /// <summary> under every similarity the report is the oracle's vanished sets; the 2^k scales give the same mesh and the same
        /// report bit for bit </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void SliverTail_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Sliver(true);
            var moved = Sliver(true);
            for (int i = 0; i < plain.X.Length; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            var b = Run(moved, name);
            AssertReportMatchesOracle(b.Result, b.Report, moved.Segments.Count, name);
            if (s.IsExact) {
                var a = Run(plain, "plain").Result.Mesh;
                var m = b.Result.Mesh;
                Assert.Equal(a.Vertices.Select(v => v * s.Scale), m.Vertices);
                Assert.Equal(a.Triangles, m.Triangles);
                Assert.Equal(a.DroppedInputPoints, m.DroppedInputPoints);
                Assert.Equal(a.DroppedInputSegments, m.DroppedInputSegments);
            }
        }

        /// <summary> one mesher, two computes, and a fresh mesher: the same mesh and the same report </summary>
        [Fact]
        public void SliverTail_Reuse_SameInstance()
        {
            var input = Sliver(true);
            var c = ConstrainedDelaunayMesh2Candidate.Convert(input);
            var mesher = Mesher(input);
            var a = mesher.Compute(c); var b = mesher.Compute(c);
            var fresh = Mesher(input).Compute(c);
            foreach (var r in new[] { b, fresh }) {
                Assert.Equal(Triangulation2Status.Success, r.Status);
                Assert.Equal(a.Mesh.Vertices, r.Mesh.Vertices);
                Assert.Equal(a.Mesh.Triangles, r.Mesh.Triangles);
                Assert.Equal(a.Mesh.DroppedInputPoints, r.Mesh.DroppedInputPoints);
                Assert.Equal(a.Mesh.DroppedInputSegments, r.Mesh.DroppedInputSegments);
                Assert.Equal(a.Diagnostics, r.Diagnostics);
            }
            Assert.Equal(new[] { 3 }, a.Mesh.DroppedInputPoints);
        }
    }
}
