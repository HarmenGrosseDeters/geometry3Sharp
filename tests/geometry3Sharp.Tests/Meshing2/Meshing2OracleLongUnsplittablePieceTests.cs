using System;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #723 (ALKIS corpus run, Euskirchen tile 332400_5586800, minimized to 4 segments and 7 points): QualityMesh2 at M3, conforming,
    /// minimum angle 20, snap tolerance 0.01 was judged INVALID, AngleBelowBound "with no forced small angle". The mesher was right. The
    /// points lie on a nearly straight diagonal, so the hull domain is a thin strip; segment 3-4 and the hull edge 3-6 leave point 3 at
    /// 0.007 degrees and run through the same pixels for about 80 m. A piece of the segment's chain, 3 to (1132.36, 723.93) in the translated
    /// frame, then has no split point on the grid: every pixel centre between its ends is crossed by the hull's chain. The bad triangle's
    /// circumcentre lies behind that piece, outside the domain, so the triangle stays (MESHING2D_SNAP.md 3g), and the oracle excuses such
    /// a triangle (CircumcentreBehindGridUnsplittable). But the oracle's GridUnsplittable gave up on pieces whose pixel box exceeded 2^16
    /// pixels and judged them splittable; this diagonal piece has a box of 607 x 417 pixels. It now enumerates only the band of pixels
    /// along the piece (the pixels its distance filter keeps), so no box limit is needed.
    /// </summary>
    public class Meshing2OracleLongUnsplittablePieceTests
    {
        // the issue's input translated by (-331000, -5585000) (the translation keeps the decimal pixel lattice: still INVALID before)
        static readonly double[] X = { 950.804, 994.359, 1110.982, 1126.306, 1200.227, 1244.149, 1255.012 };
        static readonly double[] Y = { 879.486, 821.981, 738.592, 728.075, 677.362, 647.218, 639.753 };

        static Mesh2Input Input(double minAngle, double scale = 1, double snap = 0.01)
        {
            var input = new Mesh2Input {
                X = X.Select(v => v * scale).ToArray(), Y = Y.Select(v => v * scale).ToArray(),
                MinAngleDeg = minAngle, SnapTolerance = snap * scale, SteinerLimit = 200000, Conforming = true };
            foreach (var s in new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 5, 6 } }) input.Segments.Add(new SegmentInput(s[0], s[1]));
            return input;
        }

        static (Mesh2Output Output, OracleReport Report) Run(Mesh2Input input, string what)
        {
            var output = new QualityMesh2Candidate().Run(input);
            Assert.True(output.Status == Mesh2Status.Success, what + ": " + output.Status + " " + output.FailureKind);
            Assert.False(output.SteinerLimitReached, what + ": the Steiner limit was reached");
            Assert.True(output.Triangles.Length > 0, what + ": no triangles");
            Assert.All(output.VX.Concat(output.VY), v => Assert.False(double.IsNaN(v) || double.IsInfinity(v), what + ": a vertex is not finite"));
            return (output, Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 }));
        }

        /// <summary> the issue's input at the angles of the ALKIS run: valid (q20 and q25 were INVALID before) </summary>
        [Theory]
        [InlineData(20)]
        [InlineData(25)]
        [InlineData(30)]
        public void DiagonalStrip_IsValid(double minAngle)
        {
            var r = Run(Input(minAngle), "q" + minAngle);
            Assert.True(r.Report.Ok, r.Report.ToString());
        }

        public static TheoryData<string> Similarities()
        {
            var data = new TheoryData<string>();
            foreach (var s in Similarity.All2D()) data.Add(s.Name);
            return data;
        }

        /// <summary>
        /// under every similarity (the snap tolerance scaled with it) the mesh is valid; the 2^k scales give the same mesh bit for bit.
        /// (A rotation or a translation moves the input against the pixel lattice, so its mesh is another one, judged on its own.)
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void DiagonalStrip_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Input(20);
            var moved = Input(20, 1, 0.01 * s.Scale);
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
        public void DiagonalStrip_Reuse_SameInstance()
        {
            var input = Input(20);
            var c = ConstrainedDelaunayMesh2Candidate.Convert(input);
            Func<QualityMesh2> make = () => new QualityMesh2 { MinAngle = 20, SnapTolerance = 0.01, SteinerLimit = 200000, Conforming = true };
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
