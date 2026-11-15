using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary>
    /// Error paths and option checks of curvature, query3d, offset, polygon2d and polygon_solids that the changed-line coverage run
    /// found uncovered (round 3). Every test checks the code, the message or the computed result, not the exit code alone.
    /// </summary>
    public class CoverageRound3Tests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public CoverageRound3Tests(TestMeshes meshes) { m = meshes; }

        static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        static void AssertError(int exit, JsonObject r, string code, string messagePart = null)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
            if (messagePart != null) Assert.Contains(messagePart, S(r["message"]));
        }

        // ---- the output file cannot be written although its directory exists: the target is a directory ----

        [Fact]
        public void Curvature_OutputThatIsADirectory_IsAWriteError_AndLeavesNoTempFile()
        {
            string target = m.Path_("curv_dir.csv");
            Directory.CreateDirectory(target);
            var (exit, r) = Run("curvature", m.Sphere, "--output", target, "--compact");
            AssertError(exit, r, "write_error", "cannot write");
            Assert.True(Directory.Exists(target));
            Assert.Empty(Directory.GetFiles(m.Dir, "curv_dir*.g3tmp"));
            Assert.Null(r["output_rows"]);
        }

        [Fact]
        public void Query3d_OutputThatIsADirectory_IsAWriteError_AndLeavesNoTempFile()
        {
            string target = m.Path_("q3d_dir.csv");
            Directory.CreateDirectory(target);
            var (exit, r) = Run("query3d", m.Cube, "--points", "0,0,0", "--nearest", "--output", target, "--compact");
            AssertError(exit, r, "write_error", "cannot write");
            Assert.True(Directory.Exists(target));
            Assert.Empty(Directory.GetFiles(m.Dir, "q3d_dir*.g3tmp"));
        }

        // ---- query3d: JSON lists ----

        [Fact]
        public void Query3d_JsonPointList_WithMoreThanAMillionEntries_IsRefused()
        {
            string file = m.Path_("many_points.json");
            var sb = new StringBuilder(2_100_000);
            sb.Append('[');
            for (int i = 0; i < 1_000_001; i++) sb.Append(i == 0 ? "0" : ",0");
            sb.Append(']');
            File.WriteAllText(file, sb.ToString());
            var (exit, r) = Run("query3d", m.Cube, "--points", "@" + file, "--nearest", "--compact");
            AssertError(exit, r, "invalid_arguments", "at most 1000000 entries");
            Assert.Contains("'points'", S(r["message"]));
        }

        // ---- polygon2d: simplify, simplify_lines, clip_lines, offset_lines ----

        static string Line(params double[] xy) =>
            "{\"type\":\"LineString\",\"coordinates\":[" + string.Join(",", Enumerable.Range(0, xy.Length / 2).Select(i => $"[{N(xy[2 * i])},{N(xy[2 * i + 1])}]")) + "]}";
        static string Square(double x0, double y0, double size) =>
            $"{{\"type\":\"Polygon\",\"coordinates\":[[[{N(x0)},{N(y0)}],[{N(x0 + size)},{N(y0)}],[{N(x0 + size)},{N(y0 + size)}],[{N(x0)},{N(y0 + size)}],[{N(x0)},{N(y0)}]]]}}";

        (int exit, JsonObject r) P(string op, string subject, string clip, params string[] more)
        {
            var args = new List<string> { "polygon2d", "--op", op, "--subject-geojson", subject, "--compact" };
            if (clip != null) { args.Add("--clip-geojson"); args.Add(clip); }
            args.AddRange(more);
            return Run(args.ToArray());
        }

        [Fact]
        public void LineOps_NeedExactlyOneSubject_AndAtMostOneClip()
        {
            string file = m.Path_("r3_line.geojson");
            File.WriteAllText(file, Line(0, 2, 10, 2));
            string clip = m.Path_("r3_clip.geojson");
            File.WriteAllText(clip, Square(0, 0, 5));

            var (e1, r1) = Run("polygon2d", "--op", "simplify_lines", "--epsilon", "1", "--compact");                          // none
            AssertError(e1, r1, "invalid_arguments", "exactly one of subject");
            var (e2, r2) = Run("polygon2d", file, "--op", "simplify_lines", "--epsilon", "1", "--subject-geojson", Line(0, 0, 10, 0), "--compact");   // both
            AssertError(e2, r2, "invalid_arguments", "exactly one of subject");

            var (e3, r3) = Run("polygon2d", file, "--op", "clip_lines", "--clip", clip, "--clip-geojson", Square(0, 0, 5), "--compact");
            AssertError(e3, r3, "invalid_arguments", "clip or clip_geojson, not both");
            var (e4, ok) = Run("polygon2d", file, "--op", "clip_lines", "--clip", clip, "--compact");                         // one clip is fine
            Assert.Equal(0, e4);
            Assert.Equal(5.0, D(ok["result"]["length"]), 6);                                              // y = 2 inside [0, 5]^2
        }

        [Fact]
        public void ClipType_IsAnOptionOfClipLinesOnly()
        {
            var (exit, r) = P("simplify_lines", Line(0, 0, 10, 0), null, "--epsilon", "1", "--clip-type", "difference");
            AssertError(exit, r, "invalid_arguments", "clip_type is an option of clip_lines");
        }

        [Fact]
        public void OffsetLines_ArcTolerance_NeedsARoundJoinOrEnd()
        {
            string v = Line(0, 0, 10, 0, 10, 10);
            var (e1, r1) = P("offset_lines", v, null, "--delta", "1", "--join", "miter", "--end-type", "butt", "--arc-tolerance", "0.1");
            AssertError(e1, r1, "invalid_arguments", "arc_tolerance applies to round joins and ends");
            var (e2, ok) = P("offset_lines", v, null, "--delta", "1", "--join", "miter", "--end-type", "round", "--arc-tolerance", "0.1");
            Assert.Equal(0, e2);
            Assert.False(B(ok["empty_result"]));
        }

        [Fact]
        public void LineOps_PrecisionBits_AreRangeChecked_AndSetTheGrid()
        {
            string line = Line(0, 0, 10, 0, 20, 1);
            foreach (string bits in new[] { "7", "51" }) {
                var (exit, r) = P("simplify_lines", line, null, "--epsilon", "0", "--precision-bits", bits);
                AssertError(exit, r, "invalid_arguments", "'precision_bits' must be in 8..50");
            }
            var (e1, coarse) = P("simplify_lines", line, null, "--epsilon", "0", "--precision-bits", "12");
            var (e2, fine) = P("simplify_lines", line, null, "--epsilon", "0");
            Assert.Equal(0, e1);
            Assert.Equal(0, e2);
            // the grid has 2^bits units across the half extent: 28 more bits make the grid 2^28 times finer
            Assert.Equal(Math.Pow(2, 40 - 12), D(fine["frame"]["scale"]) / D(coarse["frame"]["scale"]), 6);
        }

        [Theory]
        [InlineData("0.5")]
        [InlineData("1001")]
        public void OffsetLines_MiterLimit_OutOfRange_IsRefused(string limit)
        {
            var (exit, r) = P("offset_lines", Line(0, 0, 10, 0, 10, 10), null, "--delta", "1", "--join", "miter", "--miter-limit", limit);
            AssertError(exit, r, "invalid_arguments", "'miter_limit' must be in 1..1000");
        }

        [Fact]
        public void OffsetLines_MiterLimit_IsUsed()
        {
            // a sharp corner (about 11 degrees): a miter longer than twice the delta is squared off by the default limit 2
            string sharp = Line(0, 0, 10, 0, 0, 2);
            var (e1, byDefault) = P("offset_lines", sharp, null, "--delta", "0.5", "--join", "miter", "--end-type", "butt");
            var (e2, wide) = P("offset_lines", sharp, null, "--delta", "0.5", "--join", "miter", "--end-type", "butt", "--miter-limit", "100");
            Assert.Equal(0, e1);
            Assert.Equal(0, e2);
            Assert.True(D(wide["result"]["area"]) > D(byDefault["result"]["area"]) + 0.1,
                $"{D(wide["result"]["area"])} vs {D(byDefault["result"]["area"])}");
            Assert.True(D(wide["result"]["bounds"]["max"][0]) > D(byDefault["result"]["bounds"]["max"][0]) + 1);     // the miter reaches further out
        }

        [Fact]
        public void OffsetLines_ArcTolerance_IsUsed_AndANegativeOneIsRefused()
        {
            string line = Line(0, 0, 10, 0);
            var (e1, r1) = P("offset_lines", line, null, "--delta", "1", "--arc-tolerance", "-0.5");
            AssertError(e1, r1, "invalid_arguments", "'arc_tolerance' must be a finite number >= 0");
            var (e2, fineArcs) = P("offset_lines", line, null, "--delta", "1", "--arc-tolerance", "0.001");
            var (e3, coarseArcs) = P("offset_lines", line, null, "--delta", "1", "--arc-tolerance", "0.5");
            Assert.Equal(0, e2);
            Assert.Equal(0, e3);
            Assert.True(I(fineArcs["result"]["vertices"]) > I(coarseArcs["result"]["vertices"]),
                $"{I(fineArcs["result"]["vertices"])} vs {I(coarseArcs["result"]["vertices"])}");
        }

        [Fact]
        public void ClipLines_ClipWithoutPolygons_IsEmptyMesh()
        {
            var (exit, r) = P("clip_lines", Line(0, 0, 10, 0), "{\"type\":\"FeatureCollection\",\"features\":[]}");
            AssertError(exit, r, "empty_mesh", "the clip has no polygons");
        }

        [Fact]
        public void LineOps_UnusableExtent_IsOperationFailedWithTheClipperStatus()
        {
            var (exit, r) = P("simplify", Square(0, 0, 1e-320), null, "--epsilon", "0");
            AssertError(exit, r, "operation_failed", "invalid_input");
            Assert.Equal("invalid_input", S(r["clipper_status"]));
        }

        [Fact]
        public void OffsetLines_MarginBeyondDoubleRange_IsInvalidArgumentsWithTheClipperStatus()
        {
            // delta passes the bound of 1000 times the size, but its margin (times the miter limit) is not finite
            var (exit, r) = P("offset_lines", Line(0, 0, 1e307, 1e307), null, "--delta", "1e307", "--join", "miter", "--miter-limit", "1000");
            AssertError(exit, r, "invalid_arguments", "offset distance too large");
            Assert.Equal("invalid_parameter", S(r["clipper_status"]));
        }

        // ---- GeoJSON size limits (polygon2d and polygon_solids) ----

        [Fact]
        public void LineSubject_WithMoreThanAMillionLines_IsRefused()
        {
            var sb = new StringBuilder(14_000_000);
            sb.Append("{\"type\":\"MultiLineString\",\"coordinates\":[");
            for (int i = 0; i < 1_000_001; i++) sb.Append(i == 0 ? "[[0,0],[1,1]]" : ",[[0,0],[1,1]]");
            sb.Append("]}");
            var (exit, r) = P("simplify_lines", sb.ToString(), null, "--epsilon", "0");
            AssertError(exit, r, "invalid_arguments", "more than 1000000 lines");
        }

        // one 40 MB text shared by the two tests below (a LineString with MaxVertices + 1 plain numbers)
        static readonly Lazy<string> TooManyPositions = new Lazy<string>(() =>
        {
            var sb = new StringBuilder(2 * 20_000_001 + 100);
            sb.Append("{\"type\":\"LineString\",\"coordinates\":[");
            for (int i = 0; i < 20_000_001; i++) sb.Append(i == 0 ? "0" : ",0");
            sb.Append("]}");
            return sb.ToString();
        });

        [Fact]
        [Trait("Category", "Slow")]     // parses a 40 MB text with 20 million values (about 2 s alone, more under load)
        public void LineSubject_WithMoreThan20MillionPositions_IsRefused()
        {
            // (the positions are counted before they are read, so 20 million + 1 plain numbers are enough)
            var (exit, r) = P("simplify_lines", TooManyPositions.Value, null, "--epsilon", "0");
            AssertError(exit, r, "invalid_arguments", "more than 20000000 positions in all");
        }

        [Fact]
        [Trait("Category", "Slow")]     // as above
        public void PolygonSolids_LineWithMoreThan20MillionPositions_IsRefused()
        {
            var (exit, r) = Run("polygon_solids", "--input-geojson", TooManyPositions.Value, "--compact");
            AssertError(exit, r, "invalid_arguments", "more than 20000000 positions in all");
        }

        // ---- polygon_solids: GeoJSON reader ----

        [Fact]
        public void PolygonSolids_InlineGeoJsonOver256MillionCharacters_IsRefusedBeforeParsing()
        {
            string text = new string(' ', 256 * 1024 * 1024 + 1);
            var (exit, r) = Run("polygon_solids", "--input-geojson", text, "--compact");
            AssertError(exit, r, "invalid_arguments", "longer than 256 million characters");
        }

        [Fact]
        public void PolygonSolids_FeatureCollectionWithoutFeatures_IsInvalid()
        {
            var (exit, r) = Run("polygon_solids", "--input-geojson", "{\"type\":\"FeatureCollection\"}", "--compact");
            AssertError(exit, r, "invalid_arguments", "a FeatureCollection needs a \"features\" array");
        }

        [Fact]
        public void PolygonSolids_MultiLineString_ClosedLinesAreLoops_AndALineMustBeAnArray()
        {
            string ring = "[[0,0],[4,0],[4,4],[0,4],[0,0]]";
            var (e1, r1) = Run("polygon_solids", "--input-geojson", "{\"type\":\"MultiLineString\",\"coordinates\":[" + ring + ",[[10,10],[11,11]]]}", "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(1, I(r1["loops"]["input"]));
            Assert.Equal(1, I(r1["loops"]["open_lines_ignored"]));
            Assert.Equal(1, I(r1["solids"]));
            var (e2, r2) = Run("polygon_solids", "--input-geojson", "{\"type\":\"MultiLineString\",\"coordinates\":[1]}", "--compact");
            AssertError(e2, r2, "invalid_arguments", "a line is an array of positions");
        }

        [Fact]
        public void PolygonSolids_FileLargerThanTheLimit_IsAReadError()
        {
            string big = m.Path_("r3_big.geojson");
            using (var f = new FileStream(big, FileMode.Create)) f.SetLength(256L * 1024 * 1024 + 1);          // sparse: never read
            var (exit, r) = Run("polygon_solids", big, "--compact");
            AssertError(exit, r, "read_error", "is larger than 256 MB");
            File.Delete(big);
        }

        [Fact]
        public void PolygonSolids_InputThatCannotBeOpened_IsAReadError()
        {
            if (OperatingSystem.IsWindows()) return;
            // a unix domain socket exists as a file with length 0, but reading it fails (also for root, unlike a file without permissions)
            string file = m.Path_("r3_socket.geojson");
            using (var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified)) {
                socket.Bind(new System.Net.Sockets.UnixDomainSocketEndPoint(file));
                Assert.True(File.Exists(file));
                var (exit, r) = Run("polygon_solids", file, "--compact");
                AssertError(exit, r, "read_error", "cannot read");
                Assert.Contains("r3_socket.geojson", S(r["message"]));
            }
            File.Delete(file);
        }

        // ---- offset ----

        [Fact]
        public void Offset_InputWithoutExtent_IsOperationFailed()
        {
            // a closed tetrahedron whose four vertices coincide: no boundary edges, volume 0, diagonal 0
            string input = m.Path_("point_tetra.obj");
            File.WriteAllText(input, "v 1 1 1\nv 1 1 1\nv 1 1 1\nv 1 1 1\nf 1 3 2\nf 1 2 4\nf 2 3 4\nf 1 4 3\n");
            string output = m.Path_("point_tetra_off.obj");
            var (exit, r) = Run("offset", input, output, "--distance", "0.1", "--cell-size", "0.1", "--compact");
            AssertError(exit, r, "operation_failed", "no extent");
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void Offset_CellSizeBeyondFloatRange_IsInvalidArguments_FromTheSignedDistanceGrid()
        {
            // 1e39 passes the cell budget (a handful of cells) but is infinite as a float: the grid cannot be sized
            string output = m.Path_("off_huge_cell.obj");
            var (exit, r) = Run("offset", m.Cube, output, "--distance", "0", "--cell-size", "1e39", "--compact");
            AssertError(exit, r, "invalid_arguments", "the signed distance grid does not fit");
            Assert.False(File.Exists(output));
        }
    }
}
