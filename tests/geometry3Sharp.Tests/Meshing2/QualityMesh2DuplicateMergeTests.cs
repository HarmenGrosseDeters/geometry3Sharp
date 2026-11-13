using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #863 (MeshFuzzPilot, fuzz #624 group f): identical input points must map to one output vertex (D17, the oracle's MappingWrong). The
    /// automatic-tolerance merge (O11, Q14: a point within the tolerance of an earlier point merges into it) moved a merged point to its
    /// target and left it out of its cells, so a later exact duplicate of it no longer found it and merged into another point within the
    /// tolerance: the projection of two nested cubes 2^-41 wide, whose corners coincide in xy in pairs, mapped inputs 2 and 6 to 1 and 3.
    /// </summary>
    public class QualityMesh2DuplicateMergeTests
    {
        // the case of the issue (MeshFuzzPilot dump crash-da38935e…): A's corners, normalised by 2^98 as the fuzz target does; the target's
        // segments are the mesh edges in DMesh3's order (built below from the triangles, as the target does)
        static readonly double[][] Corners = {
            new[] { 9.560702058974368E-304, 0 }, new[] { 1.4349296274686127E-42, 3.1554436208840472E-30 }, new[] { 1.4349296274686127E-42, 3.155443620885482E-30 },
            new[] { 0, 3.155443620885482E-30 }, new[] { 0, 3.1554436208840472E-30 }, new[] { 1.4349296274686127E-42, 3.1554436208840472E-30 },
            new[] { 1.4349296274686127E-42, 3.155443620885482E-30 }, new[] { 0, 3.155443620885482E-30 }, new[] { 7.174648137343064E-43, 3.155443620884765E-30 },
            new[] { 2.152394441202919E-42, 3.155443620884765E-30 }, new[] { 2.152394441202919E-42, 3.1554436208861996E-30 }, new[] { 7.174648137343064E-43, 3.1554436208861996E-30 },
            new[] { 7.174648137343064E-43, 3.155443620884765E-30 }, new[] { 2.152394441202919E-42, 3.155443620884765E-30 }, new[] { 2.152394441202919E-42, 3.1554436208861996E-30 },
            new[] { 7.174648137343064E-43, 3.1554436208861996E-30 } };
        static readonly double[] Z = { 0, -7.888609052210118E-31, -7.888609052210118E-31, -7.888609052210118E-31, -7.888609052195769E-31, -7.888609052195769E-31,
            -7.888609052195769E-31, -7.888609052195769E-31, -7.888609052202943E-31, -7.888609052202943E-31, -7.888609052202943E-31, -7.888609052202943E-31,
            -7.888609052188594E-31, -7.888609052188594E-31, -7.888609052188594E-31, -7.888609052188594E-31 };
        static readonly int[] Triangles = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7,
            8, 10, 9, 8, 11, 10, 12, 13, 14, 12, 14, 15, 8, 9, 13, 8, 13, 12, 9, 10, 14, 9, 14, 13, 10, 11, 15, 10, 15, 14, 11, 8, 12, 11, 12, 15 };

        /// <summary> the fuzz target's input: the corners scaled by 2^98, projected to xy, the edges of positive length as segments </summary>
        static Mesh2Input Issue(Func<Vector2d, Vector2d> map = null)
        {
            var m = new DMesh3();
            for (int i = 0; i < Corners.Length; ++i) m.AppendVertex(new Vector3d(Corners[i][0] * Math.Pow(2, 98), Corners[i][1] * Math.Pow(2, 98), Z[i] * Math.Pow(2, 98)));
            for (int t = 0; t < Triangles.Length; t += 3) m.AppendTriangle(Triangles[t], Triangles[t + 1], Triangles[t + 2]);
            var x = new List<double>(); var y = new List<double>();
            foreach (int v in m.VertexIndices()) {
                Vector2d p = new Vector2d(m.GetVertex(v).x, m.GetVertex(v).y);
                if (map != null) p = map(p);
                x.Add(p.x); y.Add(p.y);
            }
            var input = new Mesh2Input { X = x.ToArray(), Y = y.ToArray(), SteinerLimit = 1500 };
            var seen = new HashSet<(int, int)>();
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e);
                if (x[ev.a] == x[ev.b] && y[ev.a] == y[ev.b]) continue;
                if (seen.Add((Math.Min(ev.a, ev.b), Math.Max(ev.a, ev.b)))) input.Segments.Add(new SegmentInput(ev.a, ev.b));
            }
            return input;
        }

        // identical inputs have one target
        static void AssertDuplicatesShareTheirTarget(Mesh2Input input, IReadOnlyList<int> map, string what)
        {
            var first = new Dictionary<(double, double), int>();
            for (int i = 0; i < input.X.Length; ++i) {
                if (first.TryGetValue((input.X[i], input.Y[i]), out int j)) Assert.True(map[i] == map[j], $"{what}: inputs {j} and {i} coincide but map to {map[j]} and {map[i]}");
                else first.Add((input.X[i], input.Y[i]), i);
            }
        }

        static ConstrainedTriangulation2Result Mesh(Mesh2Input input) =>
            new QualityMesh2 { MinAngle = 0, Conforming = false, SteinerLimit = input.SteinerLimit }.Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));

        /// <summary> the issue's case: identical inputs map to one vertex, and the oracle accepts the mesh (base: inputs 2 and 6, MappingWrong) </summary>
        [Fact]
        public void IssueCase_DuplicatesMapToOneVertex()
        {
            Mesh2Input input = Issue();
            var r = Mesh(input);
            Assert.Equal(Triangulation2Status.Success, r.Status);
            AssertDuplicatesShareTheirTarget(input, r.Mesh.InputToVertex, "issue");
            var report = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            // (the merge itself still happens: the nested cubes' corners, 2^-41 apart, are within the automatic tolerance)
            Assert.Contains(r.Diagnostics, d => d.Contains("within the automatic tolerance"));
        }

        /// <summary> the plain triangulation (ConstrainedDelaunayMesh2, the same merge) on the same input </summary>
        [Fact]
        public void IssueCase_ConstrainedDelaunay_DuplicatesMapToOneVertex()
        {
            Mesh2Input input = Issue();
            var r = new ConstrainedDelaunayMesh2().Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
            Assert.Equal(Triangulation2Status.Success, r.Status);
            AssertDuplicatesShareTheirTarget(input, r.Mesh.InputToVertex, "cdt");
            Assert.True(r.Mesh.Triangles.Count > 0);
        }

        /// <summary> under every similarity: identical inputs still share their target and the oracle accepts; at the exact similarities
        /// (2^k scales) the mapping is the same (at least one rounding similarity keeps the case too) </summary>
        [Fact]
        public void IssueCase_UnderEverySimilarity()
        {
            var r0 = Mesh(Issue());
            foreach (Similarity s in Similarity.All2D()) {
                Mesh2Input input = Issue(p => s.Apply2d(p));
                var r = Mesh(input);
                // (a similarity that rounds, eg the UTM offset, collapses the 2^-41 wide cubes into fewer points or a degenerate input,
                // whose status is the mesher's to report; the exact ones keep the case)
                if (!s.IsExact && r.Status != Triangulation2Status.Success) continue;
                Assert.True(r.Status == Triangulation2Status.Success, s.Name);
                AssertDuplicatesShareTheirTarget(input, r.Mesh.InputToVertex, s.Name);
                var report = Oracle.Check(input, new QualityMesh2Candidate().Run(input), new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(report.Ok, s.Name + ": " + report);
                if (s.IsExact) Assert.Equal(r0.Mesh.InputToVertex, r.Mesh.InputToVertex);
            }
        }

        /// <summary> computing twice on one mesher gives the same mapping and triangles </summary>
        [Fact]
        public void IssueCase_Reuse()
        {
            Mesh2Input input = Issue();
            var mesher = new QualityMesh2 { MinAngle = 0, Conforming = false, SteinerLimit = input.SteinerLimit };
            var a = mesher.Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
            var b = mesher.Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
            Assert.Equal(a.Mesh.InputToVertex, b.Mesh.InputToVertex);
            Assert.Equal(a.Mesh.Triangles.Count, b.Mesh.Triangles.Count);
        }
    }
}
