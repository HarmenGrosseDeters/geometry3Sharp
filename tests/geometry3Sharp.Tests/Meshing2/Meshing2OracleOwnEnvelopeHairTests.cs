using System;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #728 (NotDelaunayLocal / Global, conforming q30) and #729 (AngleBelowBound, constrained q30), ALKIS tiles at UTM coordinates
    /// with snap 0.01, minimized to 6 points each: the mesher was right, the oracle judged a subsegment at an input point splittable that
    /// the mesher (rightly) could not split. A decimal pixel width is not exact in doubles: the input point lies on its pixel's border, and
    /// the square around the output vertex, the pixel centre k w rounded to doubles, missed it by 4.7e-12. The oracle's GridUnsplittable
    /// then found no input constraint whose envelope holds both ends of the piece (none "own"), took the piece as splittable, and excused
    /// neither the encroached subsegment (#728) nor the triangle whose circumcentre lies behind it (#729). A constraint now also holds a
    /// piece's end when one of its own endpoints snaps to that end's pixel (only then, so a constraint that merely runs along the pixel
    /// border beside the piece is not taken in).
    /// </summary>
    public class Meshing2OracleOwnEnvelopeHairTests
    {
        static Mesh2Input Make(double[] x, double[] y, int[][] segments, bool constrainedQuality, double scale = 1, double snap = 0.01)
        {
            var input = new Mesh2Input {
                X = x.Select(v => v * scale).ToArray(), Y = y.Select(v => v * scale).ToArray(), MinAngleDeg = 30, SnapTolerance = snap * scale,
                SteinerLimit = 200000, ConstrainedQuality = constrainedQuality, Conforming = !constrainedQuality };
            foreach (var s in segments) input.Segments.Add(new SegmentInput(s[0], s[1]));
            return input;
        }

        /// <summary> #728, Rhein-Erft tile 315200_5639000, conforming q30 </summary>
        internal static Mesh2Input Issue728(double scale = 1, double snap = 0.01) => Make(
            new[] { 315210.12, 315039.619, 315043.073, 315041.956, 315039.695, 315204.372 },
            new[] { 5639173.029, 5639231.474, 5639233.705, 5639243.649, 5639231.851, 5639177.646 },
            new[] { new[] { 0, 5 }, new[] { 1, 2 }, new[] { 1, 4 }, new[] { 3, 4 } }, false, scale, snap);

        /// <summary> #729, Köln tile 366200_5642600, constrained q30 </summary>
        internal static Mesh2Input Issue729(double scale = 1, double snap = 0.01) => Make(
            new[] { 368661.295, 368660.76, 368644.969, 368641.315, 368630.317, 368629.544 },
            new[] { 5643047.98, 5643051.545, 5643328.292, 5643394.081, 5643448.676, 5643452.513 },
            new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 } }, true, scale, snap);

        static (Mesh2Output Output, OracleReport Report) Run(Mesh2Input input, string what)
        {
            var output = new QualityMesh2Candidate().Run(input);
            Assert.True(output.Status == Mesh2Status.Success, what + ": " + output.Status + " " + output.FailureKind);
            Assert.False(output.SteinerLimitReached, what + ": the Steiner limit was reached");
            Assert.True(output.Triangles.Length > 0, what + ": no triangles");
            Assert.All(output.VX.Concat(output.VY), v => Assert.False(double.IsNaN(v) || double.IsInfinity(v), what + ": a vertex is not finite"));
            return (output, Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 }));
        }

        [Fact]
        public void Issue728_ConformingQ30_IsValid()
        {
            var r = Run(Issue728(), "#728");
            Assert.True(r.Report.Ok, r.Report.ToString());
        }

        [Fact]
        public void Issue729_ConstrainedQ30_IsValid()
        {
            var r = Run(Issue729(), "#729");
            Assert.True(r.Report.Ok, r.Report.ToString());
        }

        public static TheoryData<string, int> Cases()
        {
            var data = new TheoryData<string, int>();
            foreach (var s in Similarity.All2D()) { data.Add(s.Name, 728); data.Add(s.Name, 729); }
            return data;
        }

        /// <summary> under every similarity (the snap tolerance scaled with it) the mesh is valid; the 2^k scales give the same mesh bit
        /// for bit (a rotation or translation moves the input against the pixel lattice: another mesh, judged on its own) </summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void UnderEverySimilarity(string name, int issue)
        {
            var s = Similarity.ByName(name);
            Func<double, double, Mesh2Input> make = (scale, snap) => issue == 728 ? Issue728(scale, snap) : Issue729(scale, snap);
            var plain = make(1, 0.01);
            var moved = make(1, 0.01 * s.Scale);
            for (int i = 0; i < plain.X.Length; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            var b = Run(moved, name);
            Assert.True(b.Report.Ok, name + ": " + b.Report);
            if (s.IsExact) {
                var a = Run(plain, "plain").Output;
                Assert.Equal(a.VX.Select(v => v * s.Scale), b.Output.VX);
                Assert.Equal(a.VY.Select(v => v * s.Scale), b.Output.VY);
                Assert.Equal(a.Triangles.SelectMany(t => t), b.Output.Triangles.SelectMany(t => t));
            }
        }

        /// <summary> one mesher, two computes, and a fresh mesher: the same mesh </summary>
        [Fact]
        public void Issue728_Reuse_SameInstance()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Issue728());
            Func<QualityMesh2> make = () => new QualityMesh2 { MinAngle = 30, SnapTolerance = 0.01, SteinerLimit = 200000, Conforming = true };
            var mesher = make();
            var a = mesher.Compute(c); var b = mesher.Compute(c);
            var fresh = make().Compute(c);
            Assert.Equal(Triangulation2Status.Success, a.Status);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
            Assert.Equal(a.Mesh.Vertices, fresh.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, fresh.Mesh.Triangles);
        }
    }
}
