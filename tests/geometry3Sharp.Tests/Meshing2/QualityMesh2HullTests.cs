using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QualityMesh2 on hull domains (D13) where a segment meets a hull edge at a small angle: the mesher refuses a split point of the
    /// hull edge within the tolerance of the segment (O11), so the oracle's merge zone must count the pair. Found with the Herne tiles
    /// of the reference comparison (ALKIS parcels and buildings; the tiles are not committed, the case here is a reduction of one).
    /// </summary>
    public class QualityMesh2HullTests
    {
        static OracleReport Judge(Mesh2Input input, out Mesh2Output output)
        {
            output = new QualityMesh2Candidate().Run(input);
            return Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
        }

        /// <summary>
        /// Two segments, one of them at 0.67 degrees to a hull edge at their common hull vertex (reduced from herne_381000_5710400 at
        /// 20 degrees without a tolerance; at the tile's coordinates, since the merge tolerance follows the magnitude). Refinement
        /// cascades towards the apex until the hull piece there may not be split, leaving triangles below the bound at the apex.
        /// </summary>
        static Mesh2Input HullSplitNearASegment()
        {
            var input = new Mesh2Input { X = new[] { 380960.662, 380962.679, 380933.037, 381067.864 }, Y = new[] { 5710405.368, 5710394.122, 5710558.95, 5710550.893 },
                MinAngleDeg = 20, SteinerLimit = 200000 };
            input.Segments.Add(new SegmentInput(0, 1));
            input.Segments.Add(new SegmentInput(2, 3));
            return input;
        }

        [Fact]
        public void HullEdge_AtASmallAngleToASegment_MergeZoneAtTheApex()
        {
            var report = Judge(HullSplitNearASegment(), out var output);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.MergeZoneExcused > 0, "no triangle excused in a merge zone: the case no longer reaches the apex");
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> the case under every similarity: 2^k scales bit for bit, the others pass the oracle </summary>
        [Trait("Category", "Slow")]
        [Theory]
        [MemberData(nameof(Similarities))]
        public void HullSplitNearASegment_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            var input = HullSplitNearASegment();
            var moved = new Mesh2Input { Segments = input.Segments, MinAngleDeg = input.MinAngleDeg, SteinerLimit = input.SteinerLimit, X = new double[input.Count], Y = new double[input.Count] };
            for (int i = 0; i < input.Count; ++i) { var q = s.Apply(new Vector3d(input.X[i], input.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            if (s.IsExact) {
                // (the oracle judges the plain case once, in PlainResult; a result bit for bit equal to the scaled plain one, which
                // passed, passes with the scaled coordinates: judging it again is 85 % of the time of the row)
                var scaled = new QualityMesh2Candidate().Run(moved);
                Assert.Equal(Mesh2Status.Success, scaled.Status);
                var plain = PlainResult.Value;
                Assert.Equal(plain.VX.Select(v => v * s.Scale), scaled.VX);
                Assert.Equal(plain.VY.Select(v => v * s.Scale), scaled.VY);
                Assert.Equal(plain.Triangles.SelectMany(t => t), scaled.Triangles.SelectMany(t => t));
            } else {
                var report = Judge(moved, out var output);
                Assert.Equal(Mesh2Status.Success, output.Status);
                Assert.True(report.Ok, name + ": " + report);
            }
        }

        // the unmoved case, run and judged by the oracle once for the class
        static readonly Lazy<Mesh2Output> PlainResult = new Lazy<Mesh2Output>(() => {
            var report = Judge(HullSplitNearASegment(), out var output);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(report.Ok, report.ToString());
            return output;
        });

        /// <summary>
        /// The Herne tiles themselves (polylines of ALKIS parcels and buildings; not committed): with G3_HERNE_DIR set to a folder of
        /// herne_*.txt files ("L x,y x,y ..." per polyline), each tile at 20 degrees without a tolerance must pass the oracle. (The
        /// runs with snap 0.01 wait for the fix of refinement on the grid at hull edges.) Takes some minutes.
        /// </summary>
        [Fact]
        public void HerneTiles_PassTheOracle()
        {
            string dir = Environment.GetEnvironmentVariable("G3_HERNE_DIR");
            if (dir == null || !Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "herne_*.txt").OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.NotEmpty(files);
            foreach (var file in files) {
                var input = ReadTile(file);
                input.MinAngleDeg = 20; input.SteinerLimit = 200000;
                var report = Judge(input, out var output);
                Assert.True(output.Status == Mesh2Status.Success, Path.GetFileName(file) + ": " + output.FailureKind);
                Assert.True(report.Ok, Path.GetFileName(file) + ": " + report);
            }
        }

        /// <summary> the polylines of a tile as segments between their points, each point once, each segment once </summary>
        static Mesh2Input ReadTile(string path)
        {
            var index = new Dictionary<(string, string), int>();
            var x = new List<double>(); var y = new List<double>();
            var segments = new HashSet<(int, int)>();
            var input = new Mesh2Input();
            foreach (var line in File.ReadLines(path)) {
                if (!line.StartsWith("L", StringComparison.Ordinal)) continue;
                int previous = -1;
                foreach (var token in line.Substring(1).Split((char[])null, StringSplitOptions.RemoveEmptyEntries)) {
                    var p = token.Split(',');
                    if (!index.TryGetValue((p[0], p[1]), out int id)) {
                        id = x.Count; index.Add((p[0], p[1]), id);
                        x.Add(double.Parse(p[0], CultureInfo.InvariantCulture)); y.Add(double.Parse(p[1], CultureInfo.InvariantCulture));
                    }
                    if (previous >= 0 && previous != id && segments.Add((Math.Min(previous, id), Math.Max(previous, id)))) input.Segments.Add(new SegmentInput(previous, id));
                    previous = id;
                }
            }
            input.X = x.ToArray(); input.Y = y.ToArray();
            return input;
        }
    }
}
