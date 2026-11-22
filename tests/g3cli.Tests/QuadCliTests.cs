using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using gs;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary>
    /// quad (QuadRemesher, PolyMeshIO; docs/design/QUAD_REMESHING.md): the quad-dominant default, the opt-in pure quads and its fallback status,
    /// OBJ / PLY output with whole faces, the result codes, nothing written on failure. Small procedural inputs (a sphere of 384 triangles).
    /// </summary>
    public class QuadCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public QuadCliTests(TestMeshes meshes) { m = meshes; }

        const string Edge = "0.4";
        static readonly string[] Compact = { "--compact" };

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        (int exit, JsonObject r) Quad(params string[] args) => Run(new[] { "quad" }.Concat(args).Concat(Compact).ToArray());

        static string[] FaceLines(string objPath) => File.ReadAllLines(objPath).Where(l => l.StartsWith("f ")).ToArray();
        static string[] VertexLines(string objPath) => File.ReadAllLines(objPath).Where(l => l.StartsWith("v ")).ToArray();
        static int Sides(string faceLine) => faceLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length - 1;

        string OpenSphere()
        {
            string path = m.Path_("open_sphere.obj");
            if (File.Exists(path)) return path;
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 9 }.Generate().MakeDMesh();
            foreach (int tid in s.TriangleIndices().ToList())
                if (s.GetTriCentroid(tid).z > 0.6) s.RemoveTriangle(tid);
            var w = StandardMeshWriter.WriteMesh(path, new DMesh3(s, true), WriteOptions.Defaults);
            Assert.Equal(IOCode.Ok, w.code);
            return path;
        }

        [Fact]
        public void PureQuads_OnAClosedMesh_WritesQuadsOnly_AndReportsIt()
        {
            string output = m.Path_("pure.obj");
            var (exit, r) = Quad(m.Sphere, output, "--edge-length", Edge, "--pure-quads");
            Assert.Equal(0, exit);
            Assert.Equal("ok", S(r["status"]));
            Assert.Equal("success", S(r["quad_status"]));
            Assert.Equal("success", S(r["pure_quad_status"]));
            Assert.True(B(r["pure_quads_requested"]));
            Assert.False(B(r["pure_quads_fell_back"]));
            Assert.Empty(r["warnings"].AsArray());
            Assert.Equal("obj", S(r["format"]));
            Assert.Equal(0.4, D(r["used_edge_length"]));
            Assert.False(B(r["edge_length_defaulted"]));
            JsonNode f = r["faces"];
            Assert.Equal(I(f["total"]), I(f["quads"]));
            Assert.Equal(0, I(f["non_quads"]));
            Assert.True(B(f["pure_quads"]));
            Assert.Equal(1.0, D(f["quad_fraction"]));
            Assert.InRange(I(f["total"]), 20, 400);
            Assert.True(B(r["manifold"]));
            Assert.True(B(r["closed"]));
            Assert.Equal(2, I(r["euler_characteristic"]));
            Assert.True(I(r["irregular_vertices"]) >= 8);   // a quad mesh of a sphere has at least 8 singularities (Euler)
            // the file: real quads, as many as reported, vertices as reported
            string[] faces = FaceLines(output);
            Assert.Equal(I(f["total"]), faces.Length);
            Assert.All(faces, l => Assert.Equal(4, Sides(l)));
            Assert.Equal(I(r["vertices"]), VertexLines(output).Length);
            Assert.True(D(r["timings_seconds"]["pure_quad_flow"]) >= 0);
            Assert.Equal(0, I(r["pure_quad_counters"]["inverted_triangles"]));
            Assert.Equal(0, I(r["pure_quad_counters"]["flipped_quads"]));
            Assert.True(D(r["quad_quality"]["corner_deviation_degrees"]["median"]) < 45);
        }

        [Fact]
        public void Default_IsQuadDominant_LikeTheLibrary_AndNotRequestedStatus()
        {
            string output = m.Path_("dominant.obj");
            var (exit, r) = Quad(m.Sphere, output, "--edge-length", Edge);
            Assert.Equal(0, exit);
            Assert.Equal("success", S(r["quad_status"]));
            Assert.Equal("not_requested", S(r["pure_quad_status"]));
            Assert.False(B(r["pure_quads_requested"]));
            Assert.False(B(r["pure_quads_fell_back"]));
            JsonNode f = r["faces"];
            Assert.Equal(I(f["total"]), I(f["quads"]) + I(f["non_quads"]));
            Assert.Equal(I(f["non_quads"]), I(f["triangles"]) + I(f["pentagons"]) + I(f["hexagons"]) + I(f["larger"]));
            Assert.True(I(f["quads"]) > I(f["non_quads"]));
            string[] faces = FaceLines(output);
            Assert.Equal(I(f["total"]), faces.Length);
            Assert.Equal(I(f["quads"]), faces.Count(l => Sides(l) == 4));
            Assert.Equal(I(f["triangles"]), faces.Count(l => Sides(l) == 3));
            // the same mesh as the library gives with its default settings
            DMesh3 input = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 9 }.Generate().MakeDMesh();
            var qr = new QuadRemesher(input) { TargetEdgeLength = 0.4 };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(qr.Result.FaceCount, faces.Length);
            Assert.Equal(qr.Result.VertexCount, I(r["vertices"]));
            Assert.Equal(PolyMeshMetrics.Compute(qr.Result).IrregularVertices, I(r["irregular_vertices"]));
        }

        [Fact]
        public void DefaultEdgeLength_IsReported_AndGivesAMesh()
        {
            var (exit, r) = Quad(m.Sphere, m.Path_("default_edge.obj"));
            Assert.Equal(0, exit);
            Assert.True(B(r["edge_length_defaulted"]));
            DMesh3 input = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 9 }.Generate().MakeDMesh();
            double sum = 0; int n = 0;
            foreach (int eid in input.EdgeIndices()) { Index2i ev = input.GetEdgeV(eid); sum += input.GetVertex(ev.a).Distance(input.GetVertex(ev.b)); n++; }
            Assert.Equal(Math.Max(input.GetBounds().DiagonalLength / 30, 2 * sum / n), D(r["used_edge_length"]));
            Assert.True(I(r["faces"]["quads"]) > 20);
            Assert.True(B(r["closed"]));
        }

        [Fact]
        public void PureQuads_OnAnOpenMesh_ReportsTheFallbackStatus_NotAnError()
        {
            string open = OpenSphere();
            string pure = m.Path_("open_pure.obj"), plain = m.Path_("open_plain.obj");
            var (exit, r) = Quad(open, pure, "--edge-length", Edge, "--pure-quads");
            Assert.Equal(0, exit);
            Assert.Equal("ok", S(r["status"]));
            Assert.Equal("success", S(r["quad_status"]));
            Assert.Equal("flow_infeasible", S(r["pure_quad_status"]));
            Assert.True(B(r["pure_quads_requested"]));
            Assert.True(B(r["pure_quads_fell_back"]));
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("flow_infeasible"));
            Assert.False(B(r["closed"]));
            Assert.True(I(r["boundary_vertices"]) > 0);
            // the file is the quad-dominant mesh, byte for byte what the run without --pure-quads writes
            var (exit2, r2) = Quad(open, plain, "--edge-length", Edge);
            Assert.Equal(0, exit2);
            Assert.Equal("not_requested", S(r2["pure_quad_status"]));
            Assert.Equal(File.ReadAllText(plain), File.ReadAllText(pure));
            Assert.Empty(r2["warnings"].AsArray());
        }

        [Fact]
        public void PlyOutput_AsciiAndBinary_KeepTheQuads()
        {
            string obj = m.Path_("for_ply.obj"), asc = m.Path_("quads_ascii.ply"), bin = m.Path_("quads_binary.ply");
            var (e0, r0) = Quad(m.Sphere, obj, "--edge-length", Edge, "--pure-quads");
            var (e1, r1) = Quad(m.Sphere, asc, "--edge-length", Edge, "--pure-quads", "--binary", "false");
            var (e2, r2) = Quad(m.Sphere, bin, "--edge-length", Edge, "--pure-quads");
            Assert.Equal(0, e0 + e1 + e2);
            Assert.Equal("ply", S(r1["format"]));
            Assert.StartsWith("ply\nformat ascii 1.0", File.ReadAllText(asc));
            Assert.StartsWith("ply\nformat binary_little_endian 1.0", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(bin), 0, 40));
            DPolyMesh fromObj, fromAscii, fromBinary;
            Assert.Equal(IOCode.Ok, PolyMeshIO.ReadFile(obj, ReadOptions.Defaults, out fromObj).code);
            Assert.Equal(IOCode.Ok, PolyMeshIO.ReadFile(asc, ReadOptions.Defaults, out fromAscii).code);
            Assert.Equal(IOCode.Ok, PolyMeshIO.ReadFile(bin, ReadOptions.Defaults, out fromBinary).code);
            Assert.Equal(I(r0["faces"]["total"]), fromObj.FaceCount);
            foreach (DPolyMesh p in new[] { fromAscii, fromBinary }) {
                Assert.Equal(fromObj.FaceCount, p.FaceCount);
                Assert.Equal(fromObj.VertexCount, p.VertexCount);
                Assert.Equal(fromObj.FaceIndices.ToArray(), p.FaceIndices.ToArray());
                for (int f = 0; f < p.FaceCount; ++f) Assert.Equal(4, p.FaceVertexCount(f));
            }
        }

        [Theory]
        [InlineData("out.dat", "obj", "obj")]
        [InlineData("out.obj", "ply", "ply")]
        [InlineData("out.PLY", "auto", "ply")]
        [InlineData("out.OBJ", "auto", "obj")]
        public void Format_FollowsTheOptionOrTheExtension(string file, string format, string reported)
        {
            string output = m.Path_("fmt_" + file);
            var (exit, r) = Quad(m.Sphere, output, "--edge-length", Edge, "--format", format);
            Assert.Equal(0, exit);
            Assert.Equal(reported, S(r["format"]));
            string head = File.ReadAllText(output);
            if (reported == "obj") { Assert.NotEmpty(FaceLines(output)); Assert.True(FaceLines(output).Count(l => Sides(l) == 4) > FaceLines(output).Length / 2); }
            Assert.Equal(reported == "ply", head.StartsWith("ply") || File.ReadAllBytes(output).Take(3).SequenceEqual(new byte[] { (byte)'p', (byte)'l', (byte)'y' }));
        }

        [Theory]
        [InlineData("out.stl")]
        [InlineData("out.off")]
        [InlineData("out")]
        public void OtherFormats_CannotHoldQuads_UnknownFormat_NothingWritten(string file)
        {
            string output = m.Path_("bad_" + file);
            var (exit, r) = Quad(m.Sphere, output, "--edge-length", Edge);
            AssertError(exit, r, "unknown_format");
            Assert.Contains("OBJ or PLY", S(r["message"]));
            Assert.False(File.Exists(output));
            (exit, r) = Quad(m.Sphere, m.Path_("bad_fmt.obj"), "--edge-length", Edge, "--format", "stl");
            AssertError(exit, r, "invalid_arguments");
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("nan")]
        [InlineData("abc")]
        public void EdgeLength_MustBePositiveAndFinite(string value)
        {
            string output = m.Path_("bad_edge_" + value + ".obj");
            var (exit, r) = Quad(m.Sphere, output, "--edge-length", value);
            AssertError(exit, r, "invalid_arguments");
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void NoFaces_IsOperationFailed_WithTheStatus_NothingWritten()
        {
            // the cube (side 2, 12 triangles) has edges of 2 and 2.8: the default edge length is twice the mean edge (about 4.8), larger than the whole cube, so no lattice face is extracted
            string output = m.Path_("nofaces.obj");
            var (exit, r) = Quad(m.Cube, output);
            AssertError(exit, r, "operation_failed");
            Assert.Equal("no_faces", S(r["quad_status"]));
            Assert.Contains("no_faces", S(r["message"]));
            Assert.False(File.Exists(output));
            (exit, r) = Quad(m.Cube, output, "--edge-length", "1e9", "--pure-quads");
            AssertError(exit, r, "operation_failed");
            Assert.Equal("no_faces", S(r["quad_status"]));
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void DegenerateBoundingBox_NeedsAnExplicitEdgeLength()
        {
            // one point repeated: the diagonal and the mean edge are 0, so no default edge length exists
            string input = m.Path_("one_point.obj");
            File.WriteAllText(input, "v 1 1 1\nv 1 1 1\nv 1 1 1\nf 1 2 3\n");
            string output = m.Path_("one_point_out.obj");
            var (exit, r) = Quad(input, output);
            AssertError(exit, r, "invalid_arguments");
            Assert.Contains("give 'edge_length'", S(r["message"]));
            Assert.False(File.Exists(output));
            // with an explicit edge length the remeshing runs and fails in the cross field
            (exit, r) = Quad(input, output, "--edge-length", "0.5");
            AssertError(exit, r, "operation_failed");
            Assert.Equal("field_failed", S(r["quad_status"]));
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void FieldFailure_IsOperationFailed_WithTheFieldStatus_NothingWritten()
        {
            // two collinear triangles: every triangle is degenerate, the cross field fails
            string input = m.Path_("degenerate.obj");
            File.WriteAllText(input, "v 0 0 0\nv 1 0 0\nv 2 0 0\nv 3 0 0\nf 1 2 3\nf 2 3 4\n");
            string output = m.Path_("degenerate_out.obj");
            var (exit, r) = Quad(input, output, "--edge-length", "0.5");
            AssertError(exit, r, "operation_failed");
            Assert.Equal("field_failed", S(r["quad_status"]));
            Assert.Equal("invalid_mesh", S(r["field_status"]));
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void InputAndOutputErrors_UseTheCommonResultCodes()
        {
            string output = m.Path_("err.obj");
            var (exit, r) = Quad(m.Path_("missing.obj"), output, "--edge-length", Edge);
            AssertError(exit, r, "read_error");
            (exit, r) = Quad(m.Garbage, output, "--edge-length", Edge);
            AssertError(exit, r, "read_error");
            (exit, r) = Quad(m.Empty, output, "--edge-length", Edge);
            AssertError(exit, r, "empty_mesh");
            (exit, r) = Quad(m.Path_("input.xyz"), output, "--edge-length", Edge);
            AssertError(exit, r, "unknown_format");
            (exit, r) = Quad(m.Sphere, Path.Combine(m.Dir, "no_such_dir", "out.obj"), "--edge-length", Edge);
            AssertError(exit, r, "write_error");
            Assert.False(File.Exists(output));
            (exit, r) = Run("quad", m.Sphere);
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Quad(m.Sphere, output, "--edge-length", Edge, "--bogus", "1");
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void Parallel_DoesNotChangeTheFile_AndRunsAreRepeatable()
        {
            string a = m.Path_("par_a.obj"), b = m.Path_("par_b.obj"), c = m.Path_("par_c.obj");
            Assert.Equal(0, Quad(m.Sphere, a, "--edge-length", Edge, "--pure-quads").exit);
            Assert.Equal(0, Quad(m.Sphere, b, "--edge-length", Edge, "--pure-quads", "--parallel", "false").exit);
            Assert.Equal(0, Quad(m.Sphere, c, "--edge-length", Edge, "--pure-quads").exit);
            Assert.Equal(File.ReadAllText(a), File.ReadAllText(b));
            Assert.Equal(File.ReadAllText(a), File.ReadAllText(c));
        }

        [Fact]
        public void PowerOfTwoScale_GivesTheSameMeshScaled_BitForBit()
        {
            DMesh3 big = new Sphere3Generator_NormalizedCube() { Radius = 2, EdgeVertices = 9 }.Generate().MakeDMesh();
            string input = m.Path_("sphere_r2.obj");
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(input, big, WriteOptions.Defaults).code);
            string a = m.Path_("scale_a.obj"), b = m.Path_("scale_b.obj");
            Assert.Equal(0, Quad(m.Sphere, a, "--edge-length", Edge, "--pure-quads").exit);
            Assert.Equal(0, Quad(input, b, "--edge-length", "0.8", "--pure-quads").exit);
            Assert.Equal(FaceLines(a), FaceLines(b));
            string[] va = VertexLines(a), vb = VertexLines(b);
            Assert.Equal(va.Length, vb.Length);
            for (int i = 0; i < va.Length; ++i) {
                double[] p = va[i].Split(' ').Skip(1).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                double[] q = vb[i].Split(' ').Skip(1).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                for (int k = 0; k < 3; ++k) Assert.Equal(BitConverter.DoubleToInt64Bits(p[k] * 2), BitConverter.DoubleToInt64Bits(q[k]));
            }
        }

        [Fact]
        public void Deviation_ReportsTheHausdorffDistance()
        {
            var (exit, r) = Quad(m.Sphere, m.Path_("dev.obj"), "--edge-length", Edge, "--deviation");
            Assert.Equal(0, exit);
            Assert.True(B(r["deviation"]["hausdorff"]["converged"]));
            Assert.InRange(D(r["deviation"]["hausdorff"]["distance"]), 0, 0.4);
            (exit, r) = Quad(m.Sphere, m.Path_("nodev.obj"), "--edge-length", Edge);
            Assert.Null(r["deviation"]);
        }

        [Fact]
        public void WriteFailure_KeepsAnExistingFile_AndLeavesNoTemporaryFile()
        {
            // the output path is a directory: the final move fails after the temporary file was written completely
            string dir = m.Path_("out_is_dir.obj");
            Directory.CreateDirectory(dir);
            var (exit, r) = Quad(m.Sphere, dir, "--edge-length", Edge);
            AssertError(exit, r, "write_error");
            Assert.Empty(Directory.GetFiles(m.Dir, ".g3cli-quad-*"));
            // an existing file is replaced by a complete one on success
            string existing = m.Path_("existing.obj");
            File.WriteAllText(existing, "old");
            Assert.Equal(0, Quad(m.Sphere, existing, "--edge-length", Edge).exit);
            Assert.StartsWith("v ", File.ReadAllText(existing));
            Assert.Empty(Directory.GetFiles(m.Dir, ".g3cli-quad-*"));
        }

        [Fact]
        public void HelpDescribesTheOptions()
        {
            var (exit, r) = Run("help", "quad");
            Assert.Equal(0, exit);
            JsonNode a = r["arguments"];
            Assert.True(B(a["input"]["required"]));
            Assert.True(B(a["output"]["required"]));
            Assert.False(B(a["pure_quads"]["default"]));
            Assert.True(B(a["parallel"]["default"]));
            Assert.Equal("auto", S(a["format"]["default"]));
            Assert.Equal(new[] { "auto", "obj", "ply" }, a["format"]["enum"].AsArray().Select(S).ToArray());
            Assert.Null(a["edge_length"]["default"]);
            Assert.Contains("1/30", S(a["edge_length"]["description"]));
            Assert.Equal("off", S(a["size_adaptation"]["default"]));
            Assert.Equal(new[] { "off", "scale", "scale_and_rotation" }, a["size_adaptation"]["enum"].AsArray().Select(S).ToArray());
            Assert.Equal(0.0, D(a["curvature_alignment"]["default"]));
            Assert.False(B(a["align_creases"]["default"]));
            Assert.Equal(30.0, D(a["crease_angle"]["default"]));
            Assert.Equal(30.0, D(a["corner_angle"]["default"]));
        }

        [Fact]
        public void QualityMetrics_IncludeP95_AndDefaultSizeAdaptationIsOff()
        {
            string output = m.Path_("p95_default.obj");
            var (exit, r) = Quad(m.Sphere, output, "--edge-length", Edge);
            Assert.Equal(0, exit);
            Assert.Equal("off", S(r["size_adaptation"]));
            Assert.False(B(r["size_adaptation_applied"]));
            Assert.Equal(0.0, D(r["curl_before"]));
            Assert.Equal(0.0, D(r["curl_after"]));
            Assert.Equal(1.0, D(r["min_spacing"]));
            Assert.Equal(1.0, D(r["max_spacing"]));
            Assert.Equal(0.0, D(r["timings_seconds"]["size_adaptation"]));
            JsonNode cd = r["quad_quality"]["corner_deviation_degrees"];
            Assert.True(D(cd["median"]) <= D(cd["p95"]) && D(cd["p95"]) <= D(cd["p99"]) && D(cd["p99"]) <= D(cd["max"]));
            JsonNode asp = r["quad_quality"]["aspect"];
            Assert.True(D(asp["median"]) <= D(asp["p95"]) && D(asp["p95"]) <= D(asp["p99"]) && D(asp["p99"]) <= D(asp["max"]));
        }

        string DenseSphere()
        {
            string path = m.Path_("dense_sphere.obj");
            if (File.Exists(path)) return path;
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 17 }.Generate().MakeDMesh();
            var w = StandardMeshWriter.WriteMesh(path, s, WriteOptions.Defaults);
            Assert.Equal(IOCode.Ok, w.code);
            return path;
        }

        string SharpBoxFile()
        {
            string path = m.Path_("sharp_box.obj");
            if (File.Exists(path)) return path;
            var g = new GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.7, 0.5)), EdgeVertices = 9 };
            g.Generate();
            var w = StandardMeshWriter.WriteMesh(path, g.MakeDMesh(), WriteOptions.Defaults);
            Assert.Equal(IOCode.Ok, w.code);
            return path;
        }

        [Theory]
        [InlineData("scale")]
        [InlineData("scale_and_rotation")]
        public void SizeAdaptation_AppliesAndMatchesLibrary(string mode)
        {
            string input = DenseSphere();
            string output = m.Path_("adapt_" + mode + ".obj");
            var (exit, r) = Quad(input, output, "--edge-length", "0.25", "--size-adaptation", mode);
            Assert.Equal(0, exit);
            Assert.Equal("ok", S(r["status"]));
            Assert.Equal(mode, S(r["size_adaptation"]));
            Assert.True(B(r["size_adaptation_applied"]));
            Assert.True(D(r["curl_before"]) > 0);
            Assert.True(D(r["curl_after"]) < D(r["curl_before"]));
            Assert.True(D(r["min_spacing"]) > 0 && D(r["min_spacing"]) < D(r["max_spacing"]));
            Assert.True(D(r["timings_seconds"]["size_adaptation"]) >= 0);
            Assert.True(B(r["manifold"]));
            Assert.True(B(r["closed"]));
            // matches library directly
            DMesh3 mesh = MeshFiles.Read(input, new List<string>());
            var qr = new QuadRemesher(mesh) {
                TargetEdgeLength = 0.25,
                SizeAdaptation = mode == "scale" ? SizeAdaptationMode.Scale : SizeAdaptationMode.ScaleAndRotation
            };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(qr.Result.FaceCount, I(r["faces"]["total"]));
            Assert.Equal(qr.Result.VertexCount, I(r["vertices"]));
        }

        [Theory]
        [InlineData("scale")]
        [InlineData("scale_and_rotation")]
        public void SizeAdaptation_WithPureQuads_IsAccepted_AndMatchesTheLibrary(string mode)
        {
            string output = m.Path_("adapt_pure_" + mode + ".obj");
            var (exit, r) = Quad(m.Sphere, output, "--edge-length", Edge, "--size-adaptation", mode, "--pure-quads");
            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));
            DMesh3 mesh = MeshFiles.Read(m.Sphere, new List<string>());
            var qr = new QuadRemesher(mesh) {
                TargetEdgeLength = double.Parse(Edge, System.Globalization.CultureInfo.InvariantCulture), PureQuads = true,
                SizeAdaptation = mode == "scale" ? SizeAdaptationMode.Scale : SizeAdaptationMode.ScaleAndRotation
            };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.NotEqual(PureQuadStatus.NotRequested, qr.PureQuadStatus);
            Assert.Equal(qr.Result.FaceCount, I(r["faces"]["total"]));
            Assert.Equal(qr.Result.VertexCount, I(r["vertices"]));
        }

        [Fact]
        public void SizeAdaptation_UnknownValue_IsInvalidArguments()
        {
            string output = m.Path_("adapt_bad.obj");
            var (exit, r) = Quad(m.Sphere, output, "--edge-length", Edge, "--size-adaptation", "bogus");
            AssertError(exit, r, "invalid_arguments");
            Assert.False(File.Exists(output));
        }

        string OpenPatchFile()
        {
            string path = m.Path_("open_patch.obj");
            if (File.Exists(path)) return path;
            var mesh = new DMesh3();
            int n = 16;
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    ids[i, j] = mesh.AppendVertex(new Vector3d(x, y, 0.15 * Math.Sin(3 * x) * Math.Cos(2 * y)));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    mesh.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    mesh.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            var w = StandardMeshWriter.WriteMesh(path, mesh, WriteOptions.Defaults);
            Assert.Equal(IOCode.Ok, w.code);
            return path;
        }

        string CylinderFile()
        {
            string path = m.Path_("aniso_cylinder.obj");
            if (File.Exists(path)) return path;
            var g = new CappedCylinderGenerator { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 2.0f, Slices = 24, NoSharedVertices = false };
            g.Generate();
            DMesh3 cyl = g.MakeDMesh();
            var r = new Remesher(cyl) { SmoothSpeedT = 0.5 };
            r.SetProjectionTarget(MeshProjectionTarget.Auto(new DMesh3(cyl)));
            r.SetTargetEdgeLength(0.2);
            for (int k = 0; k < 5; ++k) r.BasicRemeshPass();
            var w = StandardMeshWriter.WriteMesh(path, new DMesh3(cyl, true), WriteOptions.Defaults);
            Assert.Equal(IOCode.Ok, w.code);
            return path;
        }

        [Fact]
        public void FieldAndCornerOptions_WorkViaCli_AndMatchLibrary()
        {
            // --align-creases + --crease-angle: 30 aligns 90-degree edges, 100 disables them (must differ and match library)
            string sharp = SharpBoxFile();
            string outCrease30 = m.Path_("sharp_crease_30.obj"), outCrease100 = m.Path_("sharp_crease_100.obj");
            var (exit, r) = Quad(sharp, outCrease30, "--edge-length", "0.25", "--align-creases", "--crease-angle", "30");
            var (exit100, r100) = Quad(sharp, outCrease100, "--edge-length", "0.25", "--align-creases", "--crease-angle", "100");
            Assert.Equal(0, exit);
            Assert.Equal(0, exit100);
            Assert.True(B(r["manifold"]));
            Assert.True(B(r100["manifold"]));
            Assert.NotEqual(File.ReadAllText(outCrease30), File.ReadAllText(outCrease100));
            DMesh3 sharpMesh = MeshFiles.Read(sharp, new List<string>());
            var qr = new QuadRemesher(sharpMesh) { TargetEdgeLength = 0.25, AlignCreases = true, CreaseAngleDegrees = 30 };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            PolyMeshMetrics pm = PolyMeshMetrics.Compute(qr.Result);
            Assert.Equal(pm.IsManifold && pm.BoundaryVertices == 0, B(r["closed"]));
            Assert.Equal(qr.Result.FaceCount, I(r["faces"]["total"]));
            Assert.Equal(qr.Result.VertexCount, I(r["vertices"]));

            var qr100 = new QuadRemesher(sharpMesh) { TargetEdgeLength = 0.25, AlignCreases = true, CreaseAngleDegrees = 100 };
            Assert.Equal(QuadRemeshStatus.Success, qr100.Compute());
            Assert.Equal(qr100.Result.FaceCount, I(r100["faces"]["total"]));
            Assert.Equal(qr100.Result.VertexCount, I(r100["vertices"]));

            // --curvature-alignment: changes output on an anisotropic cylinder relative to default 0 and matches library
            string cyl = CylinderFile();
            string outCurv0 = m.Path_("cyl_curv0.obj"), outCurv3 = m.Path_("cyl_curv3.obj");
            var (ec0, rc0) = Quad(cyl, outCurv0, "--edge-length", "0.35");
            var (ec3, rc3) = Quad(cyl, outCurv3, "--edge-length", "0.35", "--curvature-alignment", "3");
            Assert.Equal(0, ec0);
            Assert.Equal(0, ec3);
            Assert.True(B(rc3["manifold"]));
            Assert.NotEqual(File.ReadAllText(outCurv0), File.ReadAllText(outCurv3));
            DMesh3 cylMesh = MeshFiles.Read(cyl, new List<string>());
            var qrc = new QuadRemesher(cylMesh) { TargetEdgeLength = 0.35, CurvatureAlignment = 3 };
            Assert.Equal(QuadRemeshStatus.Success, qrc.Compute());
            Assert.Equal(qrc.Result.FaceCount, I(rc3["faces"]["total"]));
            Assert.Equal(qrc.Result.VertexCount, I(rc3["vertices"]));

            // SizeAdaptation requested on a sharp box where curl does not drop enough -> applied false, spacing stays 1
            string outNoAdapt = m.Path_("sharp_no_adapt.obj");
            var (exitNa, rNa) = Quad(sharp, outNoAdapt, "--edge-length", "0.25", "--size-adaptation", "scale");
            Assert.Equal(0, exitNa);
            Assert.Equal("scale", S(rNa["size_adaptation"]));
            Assert.False(B(rNa["size_adaptation_applied"]));
            Assert.Equal(1.0, D(rNa["min_spacing"]));
            Assert.Equal(1.0, D(rNa["max_spacing"]));

            // --corner-angle: 30 vs 100 on an open patch with four 90-degree corners must differ and match library
            string patch = OpenPatchFile();
            string outCorner30 = m.Path_("patch_corner_30.obj"), outCorner100 = m.Path_("patch_corner_100.obj");
            var (ep30, rp30) = Quad(patch, outCorner30, "--edge-length", "0.14", "--corner-angle", "30");
            var (ep100, rp100) = Quad(patch, outCorner100, "--edge-length", "0.14", "--corner-angle", "100");
            Assert.Equal(0, ep30);
            Assert.Equal(0, ep100);
            Assert.True(B(rp100["manifold"]));
            Assert.NotEqual(File.ReadAllText(outCorner30), File.ReadAllText(outCorner100));
            DMesh3 patchMesh = MeshFiles.Read(patch, new List<string>());
            var qr2 = new QuadRemesher(patchMesh) { TargetEdgeLength = 0.14, CornerAngleDegrees = 100 };
            Assert.Equal(QuadRemeshStatus.Success, qr2.Compute());
            Assert.Equal(qr2.Result.FaceCount, I(rp100["faces"]["total"]));
            Assert.Equal(qr2.Result.VertexCount, I(rp100["vertices"]));
        }

        [Theory]
        [InlineData("--curvature-alignment", "-0.5")]
        [InlineData("--curvature-alignment", "nan")]
        [InlineData("--curvature-alignment", "inf")]
        [InlineData("--crease-angle", "-1")]
        [InlineData("--crease-angle", "181")]
        [InlineData("--crease-angle", "nan")]
        [InlineData("--corner-angle", "-1")]
        [InlineData("--corner-angle", "181")]
        [InlineData("--corner-angle", "nan")]
        public void InvalidFieldOrCornerOption_ReturnsInvalidArguments_NothingWritten(string flag, string value)
        {
            string output = m.Path_("bad_opt_" + flag.TrimStart('-') + "_" + value + ".obj");
            var (exit, r) = Quad(m.Sphere, output, "--edge-length", Edge, flag, value);
            AssertError(exit, r, "invalid_arguments");
            Assert.False(File.Exists(output));
        }
    }
}
