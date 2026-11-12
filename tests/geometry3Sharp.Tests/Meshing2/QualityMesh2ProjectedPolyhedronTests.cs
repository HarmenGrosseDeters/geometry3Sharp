using System;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #637 (MeshFuzzPilot, QA 2.0 F5): about 1 in 1000 projected polyhedra gave AngleBelowBound ("below 25 degrees with no forced small
    /// angle") in every quality mode. The input below is the shortest finding (two boxes projected to xy, 16 points, 34 segments, the
    /// crossings resolved, MinAngle 25, constrained quality). Point 12 = (2^-5, 2^-5) ends segment 11-12 and lies 1.6e-40 beside segment
    /// 0-2, which starts at (-55 2^-136, -2^-132) instead of the origin: a T-junction at 16.7 degrees whose end misses the other segment
    /// by a rounding of the projection. The mesher's O11 rule makes that end a vertex of both chains, so in the meshed constraints the two
    /// meet there at 16.7 degrees, an input angle below 60 degrees, and the triangles at it cannot all be made better than that angle allows
    /// (Shewchuk 2002, small input angles). The oracle excused a small input angle only when the closed segments meet exactly: it was too
    /// strict (fixed with #664, NearMeetAngleExcused, together with the mesher's apex exemption for such pairs). This pins the fuzz input.
    /// </summary>
    public class QualityMesh2ProjectedPolyhedronTests
    {
        static readonly double Tiny = Math.Pow(2, -136);

        /// <summary> the projected input of the #637 finding, exactly as MeshFuzzPilot builds it </summary>
        internal static Mesh2Input Issue637()
        {
            double x0 = -55 * Tiny, y0 = -16 * Tiny;    // (-6.3e-40, -1.8e-40)
            var input = new Mesh2Input {
                X = new[] { x0, 0.51953125, 0.0625, -0.0625, x0, 0.0625, 0.00390625, x0, 0.109375, 0.09375, 0.46875, -0.0234375, 0.03125, 0.09375, 0.23828125, 0.39453125 },
                Y = new[] { y0, 0.35546875, 0.0625, -0.16015625, y0, y0, 0.0390625, 0.0625, -0.1796875, 0.03125, 0.51171875, -0.0703125, 0.03125, 0.03125, 0.25390625, -0.34375 },
                MinAngleDeg = 25, ConstrainedQuality = true, Conforming = true, SteinerLimit = 1500,
            };
            int[] s = { 0, 2, 1, 2, 0, 1, 0, 3, 2, 3, 4, 5, 5, 6, 4, 6, 6, 7, 4, 7, 1, 5, 0, 5, 2, 6, 1, 6, 3, 7, 2, 7, 3, 4,
                        8, 10, 9, 10, 8, 9, 8, 11, 10, 11, 12, 13, 13, 14, 12, 14, 14, 15, 12, 15, 8, 13, 8, 12, 10, 14, 9, 14, 11, 15, 10, 15, 11, 12 };
            for (int i = 0; i < s.Length; i += 2) input.Segments.Add(new SegmentInput(s[i], s[i + 1]));
            return input;
        }

        static (Mesh2Output Output, OracleReport Report) Run(Mesh2Input input, string what)
        {
            var output = new QualityMesh2Candidate().Run(input);
            Assert.True(output.Status == Mesh2Status.Success, what + ": " + output.Status + " " + output.FailureKind);
            Assert.False(output.SteinerLimitReached, what + ": the Steiner limit was reached");
            Assert.All(output.VX.Concat(output.VY), v => Assert.False(double.IsNaN(v) || double.IsInfinity(v), what + ": a vertex is not finite"));
            return (output, Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 }));
        }

        /// <summary> the finding: the mesh is oracle-valid, and the small angle is excused as the near-meeting T-junction (on 1f227db, before
        /// #664, the oracle reported AngleBelowBound for triangle 52 83 84) </summary>
        [Fact]
        public void Issue637_NearTJunctionOfTheProjection_IsOracleValid()
        {
            var r = Run(Issue637(), "#637");
            Assert.True(r.Report.Ok, r.Report.ToString());
            Assert.True(r.Report.NearMeetAngleExcused > 0, "the small angle was not excused as constraints that meet in the tube: " + r.Report);
        }

        /// <summary> the same input in conforming quality mode: a success-path check only (this input passes there also before #664; the
        /// finding came in every mode, with other inputs) </summary>
        [Fact]
        public void Issue637_Conforming_IsOracleValid()
        {
            var input = Issue637();
            input.ConstrainedQuality = false;
            var r = Run(input, "#637 conforming");
            Assert.True(r.Report.Ok, r.Report.ToString());
        }

        public static TheoryData<string> Similarities()
        {
            var data = new TheoryData<string>();
            foreach (var s in Similarity.All2D()) data.Add(s.Name);
            return data;
        }

        /// <summary> under every similarity the mesh is oracle-valid; the 2^k scales give the same mesh bit for bit (the translations to utm
        /// absorb the 1e-40 coordinates, so those cases have no near-meeting) </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void Issue637_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Issue637();
            var moved = Issue637();
            for (int i = 0; i < plain.X.Length; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            var b = Run(moved, name);
            Assert.True(b.Report.Ok, name + ": " + b.Report);
            if (s.IsExact) {
                var a = Run(plain, "plain");
                Assert.Equal(a.Output.VX.Select(v => v * s.Scale), b.Output.VX);
                Assert.Equal(a.Output.VY.Select(v => v * s.Scale), b.Output.VY);
                Assert.Equal(a.Output.Triangles.SelectMany(t => t), b.Output.Triangles.SelectMany(t => t));
            }
        }

        /// <summary> one mesher, two computes, and a fresh mesher: the same mesh </summary>
        [Fact]
        public void Issue637_Reuse_SameInstance()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Issue637());
            var mesher = new QualityMesh2 { MinAngle = 25, Conforming = false, SteinerLimit = 1500 };
            var a = mesher.Compute(c); var b = mesher.Compute(c);
            var fresh = new QualityMesh2 { MinAngle = 25, Conforming = false, SteinerLimit = 1500 }.Compute(c);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
            Assert.Equal(a.Mesh.Vertices, fresh.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, fresh.Mesh.Triangles);
        }
    }
}
