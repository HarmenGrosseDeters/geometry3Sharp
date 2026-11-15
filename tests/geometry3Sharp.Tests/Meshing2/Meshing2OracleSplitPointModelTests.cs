using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #870 (#864 part B, MeshFuzzPilot fuzz #624 group f): the oracle's O11 excuse (a piece the mesher may not split, because its split
    /// point lies within the tolerance of another constraint) judged other points than the mesher's and judged them too strictly:
    /// - it took the piece's midpoint and both shell points on the chord between its ends, each needed within 127/128 of the tolerance; the
    ///   mesher's point lies on the piece's first source, at the midpoint or at the one shell point its ends allow, and is refused up to
    ///   the tolerance plus its rounding (refusals at 0.998 to 1.000 of the tolerance were not excused);
    /// - it computed the piece's length from an exact square that underflowed a double (a corner at 2e-260 after the fuzz target's
    ///   normalisation): no shell point, no excuse.
    /// The oracle now models the mesher's split point and its rounding (Oracle.ComputeO11Unsplittable, O11RefusalSlack2). These are three
    /// of the eleven dumps; the mesher's results were right and are accepted now (ten of the eleven; d6710852 needs the output to say
    /// which piece ends existed before refinement, #870).
    /// </summary>
    public class Meshing2OracleSplitPointModelTests
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
            // crash-10f725e0: a cube with one corner far out; after the normalisation (2^94) that corner is at (1.0002, 2e-260), the others
            // within 3e-6 of the origin: the exact squared length of a piece next to it is not a double (base: AngleBelowBound, NotDelaunayGlobal)
            new Case { Name = "10f725e0", Angle = 20, Triangles = Cube, Corners = new[] {
                new[] { 2.524972642498625E-29, 5.390273434445654E-289, 7.106459463986118E-309 }, new[] { 2.407412430484045E-35, -1.504632769052528E-36, -1.504632769052528E-36 },
                new[] { 2.407412430484045E-35, 2.256949153578792E-35, -1.504632769052528E-36 }, new[] { 0, 2.256949153578792E-35, -1.504632769052528E-36 },
                new[] { -8.425943506694157E-35, -1.504632769052528E-36, 2.256949153578792E-35 }, new[] { 2.407412430484045E-35, -1.504632769052528E-36, 2.256949153578792E-35 },
                new[] { 7.673627122167893E-35, 4.363435030252331E-35, 3.159728815010309E-35 }, new[] { 0, 2.256949153578792E-35, 2.256949153578792E-35 } } },
            // crash-6e262d9a: two nested cubes, one corner moved; refusals at 0.998 to 1.000 of the tolerance (base: NotDelaunayGlobal)
            new Case { Name = "6e262d9a", Angle = 25, Triangles = TwoCubes, Corners = new[] {
                new[] { -5.230949861159179E-37, -5.230949861159179E-37, -5.230949861159179E-37 }, new[] { 6.162452727053039E-33, -5.230949861159179E-37, -5.230949861159179E-37 },
                new[] { 1.2325428549092194E-32, 7.703196682562827E-33, 1.0784684593582405E-32 }, new[] { 1.92540684940112E-33, 8.858754649195169E-33, 9.629126626950063E-33 },
                new[] { 6.547638715930486E-33, -5.230949861159179E-37, 6.162452727053039E-33 }, new[] { 6.162452727053039E-33, -5.230949861159179E-37, 6.162452727053039E-33 },
                new[] { 6.162452727053039E-33, 6.162452727053039E-33, 6.162452727053039E-33 }, new[] { -5.230949861159179E-37, 6.162452727053039E-33, 6.162452727053039E-33 },
                new[] { 3.0809648160334614E-33, 3.0809648160334614E-33, 3.0809648160334614E-33 }, new[] { 9.243940638072616E-33, 3.0809648160334614E-33, 3.0809648160334614E-33 },
                new[] { 9.243940638072616E-33, 9.243940638072616E-33, 3.0809648160334614E-33 }, new[] { 3.0809648160334614E-33, 9.243940638072616E-33, 3.0809648160334614E-33 },
                new[] { 3.0809648160334614E-33, 3.0809648160334614E-33, 9.243940638072616E-33 }, new[] { 9.243940638072616E-33, 3.0809648160334614E-33, 9.243940638072616E-33 },
                new[] { 9.243940638072616E-33, 9.243940638072616E-33, 9.243940638072616E-33 }, new[] { 3.0809648160334614E-33, 9.243940638072616E-33, 9.243940638072616E-33 } } },
            // crash-4b2646a5: an octahedron at 3.6e-26 with one corner at the subnormal 2.6e-318 (after the normalisation (2^84) at
            // 5e-293 and 2e-224), 25 degrees, conforming (base: NotDelaunayGlobal)
            new Case { Name = "4b2646a5", Angle = 25, Conforming = true, Triangles = Octahedron, Corners = new[] {
                new[] { 2.591587E-318, 1.1125369292536007E-308, 0 }, new[] { 3.6350710512208066E-26, -8.077935669463161E-28, -8.077935669463161E-28 },
                new[] { 3.6350710512748793E-26, -8.077935665231381E-28, -8.077935667817469E-28 }, new[] { 3.6350710512584224E-26, -8.077935673224743E-28, -8.077935669463161E-28 },
                new[] { 3.635071051281932E-26, -8.0779356682876665E-28, -8.077935662175096E-28 }, new[] { 3.635071051307793E-26, -8.077935668522765E-28, -8.077935670168457E-28 } } },
        };

        public static IEnumerable<object[]> Names() => Cases.Select(c => new object[] { c.Name });

        // (10f725e0 and 4b2646a5 take 10 to 13 s under the similarities: their subnormal and 1e-293 corners make the mappings' exact
        // checks long; 6e262d9a covers the scaling)
        public static IEnumerable<object[]> FastNames() => new[] { new object[] { "6e262d9a" } };

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

        /// <summary> the mesher's result is accepted, and the excuse is what accepts it </summary>
        [Theory]
        [MemberData(nameof(Names))]
        public void FuzzCase_RefusalsModelledAsTheMesher_IsAccepted(string name)
        {
            Case c = Cases.First(k => k.Name == name);
            Mesh2Input input = Input(c);
            Mesh2Output o = new QualityMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            Assert.True(o.Triangles.Length > 10 && o.VX.All(v => !double.IsNaN(v)) && o.VY.All(v => !double.IsNaN(v)));
            var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.O11GlobalExcused + report.O11AngleExcused + report.O11LocalExcused > 0, report.ExcusedSummary());
        }

        /// <summary> under every similarity the mesher's result is accepted wherever the case survives the mapping; at the exact ones (2^k)
        /// the output is the same, bit for bit (triangles and vertices), and so is the oracle's verdict </summary>
        [Theory]
        [MemberData(nameof(FastNames))]
        public void FuzzCase_UnderEverySimilarity(string name)
        {
            Case c = Cases.First(k => k.Name == name);
            Mesh2Input input0 = Input(c);
            Mesh2Output o0 = new QualityMesh2Candidate().Run(input0);
            var r0 = Oracle.Check(input0, o0, new OracleExpect { Milestone = Milestone.M3 });
            int checkedCases = 0;
            var skipped = new List<string>();
            foreach (Similarity s in Similarity.All2D()) {
                Mesh2Input input = Input(c, p => s.Apply2d(p));
                Mesh2Output o = new QualityMesh2Candidate().Run(input);
                if (!s.IsExact && o.Status != Mesh2Status.Success) { skipped.Add(s.Name); continue; }
                Assert.True(o.Status == Mesh2Status.Success, s.Name);
                var report = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                Assert.True(report.Ok, s.Name + ": " + report);
                if (s.IsExact) {
                    Assert.Equal(o0.Triangles.SelectMany(t => t), o.Triangles.SelectMany(t => t));
                    for (int i = 0; i < o0.VX.Length; ++i) Assert.Equal(s.Apply2d(new Vector2d(o0.VX[i], o0.VY[i])), new Vector2d(o.VX[i], o.VY[i]));
                    Assert.Equal(r0.O11GlobalExcused, report.O11GlobalExcused);
                    Assert.Equal(r0.O11AngleExcused, report.O11AngleExcused);
                }
                checkedCases++;
            }
            // (a non-exact similarity whose rounded input the mesher could not mesh would be skipped: none is)
            Assert.Empty(skipped);
            Assert.True(checkedCases > 4);
        }
    }
}
