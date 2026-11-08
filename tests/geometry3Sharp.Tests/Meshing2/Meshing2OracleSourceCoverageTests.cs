using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    // Tests select the process-wide diagnostic setting; other collections must not
    // read it while a test switches between the grid and its scan control.
    [Collection("Meshing2OracleTiming")]
    public class Meshing2OracleSourceCoverageTests
    {
        const string Variable = "G3_ORACLE_SOURCE_INDEX";
        static readonly OracleExpect M1 = new OracleExpect { Milestone = Milestone.M1 };
        static long Work => (long)typeof(Oracle).GetProperty("SourceWorkForTests", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        static long GridWork => (long)typeof(Oracle).GetProperty("SourceGridWorkForTests", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        static T Mode<T>(string mode, Func<T> action)
        {
            string previous = Environment.GetEnvironmentVariable(Variable);
            try { Environment.SetEnvironmentVariable(Variable, mode); return action(); }
            finally { Environment.SetEnvironmentVariable(Variable, previous); }
        }
        static OracleReport Checked(Mesh2Input input, Mesh2Output output, OracleExpect e)
        {
            bool previous = Oracle.CheckFastPathForTests;
            try { Oracle.CheckFastPathForTests = true; return Oracle.Check(input, output, e); }
            finally { Oracle.CheckFastPathForTests = previous; }
        }
        // Every public report field, including ordered violation details and exact
        // rationals. Fail if a future report field has no explicit representation.
        static string Value(object value)
        {
            if (value == null) return "null";
            if (value is Q q) return q.ToString();
            if (value is ViolationRecord v) return v.Kind + ":" + v.Detail;
            if (value is ValueTuple<BigInteger, BigInteger> pixel) return pixel.Item1 + "," + pixel.Item2;
            if (value is IEnumerable items && !(value is string))
                return "[" + string.Join(";", items.Cast<object>().Select(Value)) + "]";
            if (value is string || value is bool || value is int) return Convert.ToString(value, CultureInfo.InvariantCulture);
            throw new InvalidOperationException("Unhandled report value: " + value.GetType());
        }
        static string[] Facts(OracleReport report) => typeof(OracleReport).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(f => f.Name).Select(f => f.Name + "=" + Value(f.GetValue(report))).ToArray();
        static OracleReport Same(Mesh2Input input, Mesh2Output output, OracleExpect e)
        {
            var scan = Mode("scan", () => Checked(input, output, e));
            var grid = Mode("grid", () => Checked(input, output, e));
            Assert.Equal(Facts(scan), Facts(grid));
            Assert.Equal(scan.Ok, grid.Ok);
            return grid;
        }
        static Mesh2Input Lens(int m)
        {
            var x = new List<double>(); var y = new List<double>();
            for (int i = 0; i < m; ++i) { double t = -1 + 2.0 * i / (m - 1); x.Add(t); y.Add(0.25 * (1 - t * t)); }
            for (int i = m - 2; i >= 1; --i) { double t = -1 + 2.0 * i / (m - 1); x.Add(t); y.Add(-0.25 * (1 - t * t)); }
            x.Reverse(); y.Reverse();
            var input = new Mesh2Input { X = x.ToArray(), Y = y.ToArray() };
            input.Rings.Add(new RingInput(Enumerable.Range(0, x.Count).ToArray(), false));
            return input;
        }
        static Mesh2Output Mesh(Mesh2Input input)
        {
            var output = new DelaunayMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.NotEmpty(output.Triangles);
            Assert.Null(output.Subsegments); // Exercise inference, rather than the reported-list shortcut.
            output.Locate = null;
            return output;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("grid")]
        [InlineData(" GRID ")]
        [InlineData("scan")]
        [InlineData(" ScAn ")]
        public void Selection_DefaultsAndAcceptedValues(string mode)
        {
            var input = Lens(17); var output = Mesh(input);
            long scan = Mode("scan", () => { Assert.True(Oracle.Check(input, output, M1).Ok); return Work; });
            long grid = Mode("grid", () => { Assert.True(Oracle.Check(input, output, M1).Ok); return Work; });
            Assert.True(grid < scan);
            long selected = Mode(mode, () => { Assert.True(Oracle.Check(input, output, M1).Ok); return Work; });
            Assert.Equal(string.Equals(mode?.Trim(), "scan", StringComparison.OrdinalIgnoreCase) ? scan : grid, selected);
        }

        [Fact]
        public void Selection_InvalidValueFailsBeforeInputWork()
        {
            Mode("gird", () => {
                var error = Assert.Throws<InvalidOperationException>(() => Oracle.Check(null, null, M1));
                Assert.Contains(Variable, error.Message); Assert.Contains("gird", error.Message); Assert.Contains("grid or scan", error.Message);
                return 0;
            });
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void FullReportsAgree_InEveryHarnessFrame(string name)
        {
            var s = Similarity.ByName(name); var input = Lens(17);
            var basis = Mesh(input); var basisReport = Same(input, basis, M1);
            var axis = s.Rotation * Vector3d.AxisX;
            double norm = Math.Sqrt(axis.x * axis.x + axis.y * axis.y), cos = axis.x / norm, sin = axis.y / norm;
            for (int i = 0; i < input.Count; ++i) {
                double x = input.X[i], y = input.Y[i];
                input.X[i] = s.Scale * (cos * x - sin * y) + s.Translation.x;
                input.Y[i] = s.Scale * (sin * x + cos * y) + s.Translation.y;
            }
            var output = Mesh(input); var report = Same(input, output, M1);
            Assert.True(report.Ok, report.ToString());
            if (s.IsExact) {
                Assert.Equal(basis.Triangles.Select(t => string.Join(",", t.OrderBy(i => i))).OrderBy(t => t),
                    output.Triangles.Select(t => string.Join(",", t.OrderBy(i => i))).OrderBy(t => t));
                Assert.Equal(basisReport.MinSin2, report.MinSin2);
                Assert.Equal(basisReport.Area2 * Q.FromDouble(s.Scale) * Q.FromDouble(s.Scale), report.Area2);
            }
        }

        [Theory]
        [InlineData(-500)]
        [InlineData(500)]
        public void FullReportsAgree_AtExtremeBinaryScales(int exponent)
        {
            var input = Lens(9); var output = Mesh(input); double scale = Math.Pow(2, exponent);
            input.X = input.X.Select(x => x * scale).ToArray(); input.Y = input.Y.Select(y => y * scale).ToArray();
            output.VX = output.VX.Select(x => x * scale).ToArray(); output.VY = output.VY.Select(y => y * scale).ToArray();
            Assert.True(Same(input, output, M1).Ok);
        }

        [Fact]
        public void FullReportsAgree_UnknownBoxesAndReferenceFallbacks()
        {
            var input = Lens(5); var output = Mesh(input);
            Assert.True(Same(input, output, new OracleExpect { Milestone = Milestone.M1, Tube = Q.FromDouble(1e308) }).Ok);
            foreach (bool exactVertices in new[] { false, true }) {
                if (exactVertices) output.ExactVertices = output.VX.Select((x, i) => Pt.FromDoubles(x, output.VY[i])).ToArray();
                var e = exactVertices ? M1 : M1.WithReference();
                long scan = Mode("scan", () => { Assert.True(Oracle.Check(input, output, e).Ok); return Work; });
                long grid = Mode("grid", () => { Assert.True(Oracle.Check(input, output, e).Ok); return Work; });
                Assert.Equal(scan, grid);
                Assert.Equal(Facts(Mode("scan", () => Oracle.Check(input, output, e))), Facts(Mode("grid", () => Oracle.Check(input, output, e))));
            }
        }

        [Fact]
        public void CoveringRequiresBothEndpoints_AndReportedEmptyListsStayEmpty()
        {
            var input = new Mesh2Input { X = new[] { 0d, 1, 1, 0, 0.5 }, Y = new[] { 0d, 0, 1, 1, 0.25 } };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Segments.Add(new SegmentInput(0, 2));
            var output = Mesh(input);
            // The edge from (0,0) to the interior point has its second endpoint in
            // the diagonal source's box, but outside its tube. Check mode must
            // compare the inferred boolean with the original exact two-end scan.
            Same(input, output, M1);
            output.Subsegments = new List<SubsegmentOutput>();
            Assert.False(Same(input, output, M1).Ok);
        }

        [Fact]
        public void InferredBoundaryAndReportedBoundaryKeepTheCompleteReport()
        {
            var input = Lens(17); var output = Mesh(input);
            var inferred = Mode("grid", () => Checked(input, output, M1));
            Assert.True(inferred.Ok, inferred.ToString());
            var ring = input.Rings[0].Vertices;
            output.Subsegments = Enumerable.Range(0, ring.Length)
                .Select(i => new SubsegmentOutput(ring[i], ring[(i + 1) % ring.Length])).ToList();
            var reported = Mode("scan", () => Checked(input, output, M1));
            Assert.True(reported.Ok, reported.ToString());
            Assert.Equal(Facts(inferred), Facts(reported));
        }

        [Fact]
        public void Lens_SourceCandidateWorkGrowsBelowQuadratic()
        {
            long Count(int m) {
                var input = Lens(m); var output = Mesh(input);
                return Mode(null, () => { var report = Oracle.Check(input, output, M1); Assert.True(report.Ok, report.ToString()); return Work; });
            }
            long small = Count(129), large = Count(513);
            Assert.True(small > 0 && large < 12 * small, "source visits: " + small + " -> " + large);
        }

        [Fact]
        public void DenseKnownBucket_HasBoundedEnumerationAndReusableStamps()
        {
            // A dense cluster of small known boxes beside a far outlier. All
            // cluster boxes share one cell, without entering the unknown list.
            const int count = 8193;
            var boxes = new double[4 * count];
            for (int i = 0; i < count - 1; ++i) { boxes[4 * i + 1] = 1; boxes[4 * i + 3] = 1; }
            boxes[4 * (count - 1)] = boxes[4 * (count - 1) + 2] = 1e9;
            boxes[4 * (count - 1) + 1] = boxes[4 * (count - 1) + 3] = 1e9 + 1;
            var type = typeof(Oracle).GetNestedType("Run", BindingFlags.NonPublic).GetNestedType("BoxGrid", BindingFlags.NonPublic);
            var grid = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { boxes, count }, null);
            var query = type.GetMethod("QueryBounded");
            object[] args = { 0.25, 0.25, 0.25, 0.25, 4096, 0 };
            Assert.Null(query.Invoke(grid, args));
            Assert.InRange((int)args[5], 1, 4097);
            Assert.Equal(8192, ((List<int>)type.GetMethod("Query").Invoke(grid, new object[] { 0.25, 0.25, 0.25, 0.25 })).Count);
            args = new object[] { 1e9, 1e9, 1e9, 1e9, 4096, 0 };
            Assert.Equal(new[] { count - 1 }, (List<int>)query.Invoke(grid, args));
        }

        [Fact]
        public void UnknownSources_KeepTheScanEarlyExitWithoutMaterializingAllCandidates()
        {
            var input = new Mesh2Input { X = new[] { 0d, 1, 0 }, Y = new[] { 0d, 0, 1 } };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2 }, false));
            for (int i = 0; i < 8192; ++i) input.Segments.Add(new SegmentInput(0, 1));
            var output = Mesh(input);
            var e = new OracleExpect { Milestone = Milestone.M1, Tube = Q.FromDouble(1e308) };
            var grid = Mode("grid", () => {
                var report = Oracle.Check(input, output, e);
                Assert.True(report.Ok, report.ToString());
                Assert.InRange(GridWork, 1, 3L * 4097);
                Assert.Equal(3, Work);
                return report;
            });
            Assert.Equal(Facts(Mode("scan", () => Oracle.Check(input, output, e))), Facts(grid));
        }

        [Fact]
        public void RepeatedAndParallelChecksKeepReportsAndPerThreadWork()
        {
            var input = Lens(17); var output = Mesh(input);
            Mode("grid", () => {
                var expected = Facts(Oracle.Check(input, output, M1)); long work = Work, gridWork = GridWork;
                var other = Lens(9); Oracle.Check(other, Mesh(other), M1);
                Assert.Equal(expected, Facts(Oracle.Check(input, output, M1))); Assert.Equal(work, Work); Assert.Equal(gridWork, GridWork);
                Parallel.For(0, 8, _ => {
                    Assert.Equal(expected, Facts(Oracle.Check(input, output, M1)));
                    Assert.Equal(work, Work); Assert.Equal(gridWork, GridWork);
                });
                return 0;
            });
        }
    }
}
