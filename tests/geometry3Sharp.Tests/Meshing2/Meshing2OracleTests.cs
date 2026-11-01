using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>The oracle of the 2D mesher judges itself first. A weak oracle is the main risk of the acceptance process.</summary>
    public class Meshing2OracleTests
    {
        public static IEnumerable<object[]> SelfCheckNames() => SelfCheck.Run().Select(i => new object[] { i.Name });

        [Theory]
        [MemberData(nameof(SelfCheckNames))]
        public void SelfCheck_Item_Passes(string name)
        {
            var item = SelfCheck.Run().First(i => i.Name == name);
            Assert.True(item.Passed, item.Detail);
        }

        [Fact]
        public void SelfCheck_CoversEveryKindOfViolation_Except_TheListedOnes()
        {
            // a kind nothing provokes cannot be known to work; the listed ones need an input the self-check cannot build
            var notProvoked = new[] { Violation.CountOutOfRange }; // Fact below
            var names = SelfCheck.Run().Select(i => i.Name).ToList();
            foreach (Violation kind in Enum.GetValues(typeof(Violation))) {
                if (notProvoked.Contains(kind)) continue;
                Assert.True(names.Any(n => n.EndsWith(" as " + kind)), "no broken result is rejected as " + kind);
            }
        }

        [Fact]
        public void Oracle_ReferencesNothingOfTheLibrary()
        {
            var references = typeof(Oracle).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
            Assert.DoesNotContain("geometry3Sharp", references);
            Assert.Contains("netstandard", references);
        }

        static Mesh2Output Triangle(Mesh2Input input) => new Mesh2Output {
            Status = Mesh2Status.Success, VX = input.X, VY = input.Y, InputToVertex = Enumerable.Range(0, input.Count).ToArray(),
            Triangles = new[] { new[] { 0, 1, 2 } }
        };

        [Fact]
        public void InputInvalid_NonFiniteInput_IsNotJudged()
        {
            var input = Generators.Literal(0, 0, double.NaN, 0, 0, 1);
            var report = Oracle.Check(input, new Mesh2Output { Status = Mesh2Status.Empty }, new OracleExpect());
            Assert.True(report.Has(Violation.InputInvalid));
        }

        [Fact]
        public void CountOutOfRange_IsReported()
        {
            var input = Generators.Literal(0, 0, 8, 0, 0, 8);
            var low = Oracle.Check(input, Triangle(input), new OracleExpect { Milestone = Milestone.M1, MinTriangles = 2 });
            Assert.True(low.Has(Violation.CountOutOfRange));
            var high = Oracle.Check(input, Triangle(input), new OracleExpect { Milestone = Milestone.M1, MaxVertices = 2 });
            Assert.True(high.Has(Violation.CountOutOfRange));
            Assert.True(Oracle.Check(input, Triangle(input), new OracleExpect { Milestone = Milestone.M1, MinTriangles = 1, MaxVertices = 3 }).Ok);
        }

        [Fact]
        public void AutoTolerance_IsAbout5MicrometresAtUtm()
        {
            var input = Generators.Literal(632117.25, 5712043.5, 0, 0);
            double tolerance = (double)Oracle.AutoTolerance(input).N / (double)Oracle.AutoTolerance(input).D;
            Assert.InRange(tolerance, 4e-6, 6e-6);
        }

        [Fact]
        public void Fingerprint_DoesNotDependOnOrder_NumberingOrRotation()
        {
            var input = Generators.Literal(0, 0, 8, 0, 8, 8, 0, 8);
            var a = new Mesh2Output { VX = input.X, VY = input.Y, Triangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } } };
            var b = new Mesh2Output {
                VX = new double[] { 0, 8, 8, 0 }, VY = new double[] { 8, 8, 0, 0 },
                Triangles = new[] { new[] { 3, 1, 0 }, new[] { 1, 3, 2 } } // renumbered, rotated, other order
            };
            Assert.Equal(Fingerprint.Triangles(a), Fingerprint.Triangles(b));
            var other = new Mesh2Output { VX = input.X, VY = input.Y, Triangles = new[] { new[] { 0, 1, 3 }, new[] { 1, 2, 3 } } };
            Assert.NotEqual(Fingerprint.Triangles(a), Fingerprint.Triangles(other));
            var flipped = new Mesh2Output { VX = input.X, VY = input.Y, Triangles = new[] { new[] { 0, 2, 1 }, new[] { 0, 2, 3 } } };
            Assert.NotEqual(Fingerprint.Triangles(a), Fingerprint.Triangles(flipped));
        }

        [Fact]
        public void Fingerprint_CoordinateTokens_FollowTheFormat()
        {
            Assert.Equal("3ff0000000000000", Fingerprint.Coordinate(1.0));
            Assert.Equal("0000000000000000", Fingerprint.Coordinate(-0.0));
            Assert.Equal("c000000000000000", Fingerprint.Coordinate(-2.0));
            Assert.Equal("0000000000000001", Fingerprint.Coordinate(double.Epsilon));
        }

        [Fact]
        public void Fingerprint_Subsegments_IgnoreDirectionAndDuplicates()
        {
            var o = new Mesh2Output { VX = new double[] { 0, 1, 2 }, VY = new double[] { 0, 0, 0 }, Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 2) } };
            var p = new Mesh2Output { VX = o.VX, VY = o.VY, Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(2, 1), new SubsegmentOutput(1, 0), new SubsegmentOutput(0, 1) } };
            Assert.Equal(Fingerprint.Subsegments(o), Fingerprint.Subsegments(p));
            Assert.NotEqual(Fingerprint.Subsegments(o), Fingerprint.Subsegments(new Mesh2Output { VX = o.VX, VY = o.VY, Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1) } }));
        }

        [Fact]
        public void TextFormat_RoundTrips_AnInput()
        {
            var b = new B().Square().Seg(1, 1, 9, 2, 4, true).Hole(2, 5, 3, 5, 3, 6).HoleSeed(2.5, 5.25).RegionSeed(1, 1, 7, 0.5).RegionRing(8, null, 6, 6, 7, 6, 7, 7).Min(20).Area(2).Limit(99).Snap(0.25).NoNoding().Conforming();
            var input = b.Build();
            string text = TextFormat.WriteInput(input);
            Assert.Equal(text, TextFormat.WriteInput(TextFormat.ReadInput(text)));
            var parsed = TextFormat.ReadInput(text);
            Assert.Equal(input.X, parsed.X);
            Assert.False(parsed.ResolveCrossings);
            Assert.True(parsed.Conforming);
            Assert.Equal(99, parsed.SteinerLimit);
            Assert.True(parsed.Rings[1].Hole);
            Assert.Equal(7, parsed.Regions[0].Label);
            Assert.Equal(0.5, parsed.Regions[0].MaxArea);
            Assert.Null(parsed.Regions[1].MaxArea);
        }

        [Fact]
        public void TextFormat_KeepsTheBitsOfSignedZeroAndSubnormals()
        {
            var input = Generators.Literal(-0.0, double.Epsilon, 1e70, -1e-300);
            var parsed = TextFormat.ReadInput(TextFormat.WriteInput(input));
            Assert.Equal(BitConverter.DoubleToInt64Bits(input.X[0]), BitConverter.DoubleToInt64Bits(parsed.X[0]));
            Assert.Equal(BitConverter.DoubleToInt64Bits(input.Y[0]), BitConverter.DoubleToInt64Bits(parsed.Y[0]));
            Assert.Equal(1e70, parsed.X[1]);
        }

        [Fact]
        public void TextFormat_AcceptsDecimalsAndComments_InForeignFiles()
        {
            string text = "mesh2-output 1 # a comment\nproducer other\nstatus Success\nvertices 3\n0 0\n8.5 0\n0 2e0\ntriangles 1\n0 1 2\nsubsegments -\nmap -\nlabels -\nbad -\nsteiner_limit_reached 0\nend\n";
            var o = TextFormat.ReadOutput(text, out string producer);
            Assert.Equal("other", producer);
            Assert.Equal(8.5, o.VX[1]);
            Assert.Equal(2.0, o.VY[2]);
            Assert.False(o.IdentityRules);
            Assert.Null(o.Subsegments);
        }

        [Fact]
        public void TextFormat_RejectsWrongCounts()
        {
            Assert.Throws<FormatException>(() => TextFormat.ReadOutput("mesh2-output 2\n", out _));
            Assert.Throws<FormatException>(() => TextFormat.ReadOutput("mesh2-output 1\nproducer x\nstatus Success\nvertices 0\ntriangles 0\nsubsegments -\nmap 2 0\nlabels -\nbad -\nsteiner_limit_reached 0\nend\n", out _));
            Assert.Throws<FormatException>(() => TextFormat.ParseCoordinate("0x123"));
        }

        [Fact]
        public void ForeignOutput_IsJudged_ThroughTheTextFormat()
        {
            var input = Generators.Literal(0, 0, 8, 0, 0, 8, 2, 2);
            // a foreign producer: its own numbering, no map, the interior point at index 0
            string good = "mesh2-output 1\nproducer other\nstatus Success\nvertices 4\n2 2\n0 0\n8 0\n0 8\ntriangles 3\n0 1 2\n0 2 3\n0 3 1\nsubsegments -\nmap -\nlabels -\nbad -\nsteiner_limit_reached 0\nend\n";
            var report = Oracle.Check(input, TextFormat.ReadOutput(good, out _), new OracleExpect { Milestone = Milestone.M1 });
            Assert.True(report.Ok, report.ToString());
            string lost = "mesh2-output 1\nproducer other\nstatus Success\nvertices 3\n0 0\n8 0\n0 8\ntriangles 1\n0 1 2\nsubsegments -\nmap -\nlabels -\nbad -\nsteiner_limit_reached 0\nend\n";
            Assert.True(Oracle.Check(input, TextFormat.ReadOutput(lost, out _), new OracleExpect { Milestone = Milestone.M1 }).Has(Violation.VertexUnused));
            string constructed = "mesh2-output 1\nproducer other\nstatus Success\nvertices 5\n2 2\n0 0\n8 0\n0 8\n1 1\ntriangles 3\n0 1 2\n0 2 3\n0 3 1\nsubsegments -\nmap -\nlabels -\nbad -\nsteiner_limit_reached 0\nend\n";
            Assert.True(Oracle.Check(input, TextFormat.ReadOutput(constructed, out _), new OracleExpect { Milestone = Milestone.M1 }).Has(Violation.SteinerNotAllowed));
        }
    }
}
