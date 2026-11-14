using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #864 part A (MeshFuzzPilot, fuzz #624 group f): the oracle's O11 excuse for pieces with no split point outside the tolerance of another
    /// constraint withheld it when the two lines were "nearly coincident" by the tube (twice the mesher's tolerance). Between the tolerance and
    /// the tube the spec allows either (the gap band: merged below the tolerance, kept beyond twice it), and the mesher keeps them apart: a
    /// projected octahedron, two of whose constraints meet at 0.88 degrees with their far ends 1.65 tolerances apart, got a false
    /// NotDelaunayGlobal and AngleBelowBound. The coincidence test now uses the tolerance (SelfCheck 7b' and the short-constraint items pin
    /// both sides of it); this checks it against the mesher's real output.
    /// </summary>
    public class Meshing2OracleGapBandTests
    {
        // the case of the issue (MeshFuzzPilot dump crash-7e1184c2…): an octahedron at 3.6e-26 whose corners differ by about 1e-36,
        // normalised by 2^119 as the fuzz target does; minimum angle 25 degrees (options 71: quality, conforming)
        static readonly double[][] Corners = {
            new[] { 3.635071051296038E-26, 3.6350710512584224E-26, -3.6350710512584224E-26 }, new[] { 3.6350710512208066E-26, 3.6350710512584224E-26, -3.6350710512584224E-26 },
            new[] { 3.635071051364217E-26, 3.6350710514018327E-26, -3.635071051152628E-26 }, new[] { 3.635071051364217E-26, 3.635071051326601E-26, -3.635071051152628E-26 },
            new[] { 3.6350710512584224E-26, 3.6350710512537204E-26, -3.6350710512137536E-26 }, new[] { 3.6350710512584224E-26, 3.6350710512584224E-26, -3.635071051296038E-26 } };
        static readonly int[] Triangles = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };

        static Mesh2Input Issue(Func<Vector2d, Vector2d> map = null)
        {
            var m = new DMesh3();
            foreach (var c in Corners) m.AppendVertex(new Vector3d(c[0], c[1], c[2]) * Math.Pow(2, 119));
            for (int t = 0; t < Triangles.Length; t += 3) m.AppendTriangle(Triangles[t], Triangles[t + 1], Triangles[t + 2]);
            var x = new List<double>(); var y = new List<double>();
            foreach (int v in m.VertexIndices()) {
                Vector2d p = new Vector2d(m.GetVertex(v).x, m.GetVertex(v).y);
                if (map != null) p = map(p);
                x.Add(p.x); y.Add(p.y);
            }
            var input = new Mesh2Input { X = x.ToArray(), Y = y.ToArray(), MinAngleDeg = 25, SteinerLimit = 1500 };
            var seen = new HashSet<(int, int)>();
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e);
                if (x[ev.a] == x[ev.b] && y[ev.a] == y[ev.b]) continue;
                if (seen.Add((Math.Min(ev.a, ev.b), Math.Max(ev.a, ev.b)))) input.Segments.Add(new SegmentInput(ev.a, ev.b));
            }
            return input;
        }

        /// <summary> the mesher's result is accepted, its refusals excused as O11-unsplittable (base: NotDelaunayGlobal: vertex 52 inside the
        /// circumcircle of 11 23 12, and AngleBelowBound) </summary>
        [Fact]
        public void ProjectedOctahedron_GapBand_IsAccepted()
        {
            Mesh2Input input = Issue();
            Mesh2Output o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            // (the excuse is what accepts it: O11-unsplittable pieces hide vertices and circumcentres here)
            Assert.True(report.O11GlobalExcused + report.O11AngleExcused > 0, report.ExcusedSummary());
            Assert.True(o.Triangles.Length > 20 && o.VX.All(v => !double.IsNaN(v)));
        }

        /// <summary> under every similarity the mesher's result is accepted wherever the case survives the mapping; at the exact ones (2^k)
        /// the output is the same, bit for bit (triangles and vertices) </summary>
        [Fact]
        public void ProjectedOctahedron_UnderEverySimilarity()
        {
            Mesh2Output o0 = new QualityMesh2Candidate().Run(Issue());
            int checkedCases = 0;
            var skipped = new List<string>();
            foreach (Similarity s in Similarity.All2D()) {
                Mesh2Input input = Issue(p => s.Apply2d(p));
                Mesh2Output o = new QualityMesh2Candidate().Run(input);
                if (!s.IsExact && o.Status != Mesh2Status.Success) { skipped.Add(s.Name); continue; }
                Assert.True(o.Status == Mesh2Status.Success, s.Name);
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(report.Ok, s.Name + ": " + report);
                if (s.IsExact) {
                    // (bit for bit: the same triangles, and the vertices of the mapped input are the mapped vertices)
                    Assert.Equal(o0.Triangles.SelectMany(t => t), o.Triangles.SelectMany(t => t));
                    for (int i = 0; i < o0.VX.Length; ++i) Assert.Equal(s.Apply2d(new Vector2d(o0.VX[i], o0.VY[i])), new Vector2d(o.VX[i], o.VY[i]));
                }
                checkedCases++;
            }
            // (a non-exact similarity whose rounded input the mesher could not mesh would be skipped: none is)
            Assert.Empty(skipped);
            Assert.True(checkedCases > 4);
        }
    }
}
