using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    public class McpTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public McpTests(TestMeshes meshes) { m = meshes; }

        static JsonObject Request(int id, string method, JsonNode prms = null)
        {
            var o = new JsonObject() { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
            if (prms != null) o["params"] = prms;
            return o;
        }

        static JsonObject Call(int id, string tool, JsonObject arguments)
        {
            return Request(id, "tools/call", new JsonObject() { ["name"] = tool, ["arguments"] = arguments });
        }

        static JsonObject Handle(McpServer server, JsonObject request)
        {
            return server.Handle(request.ToJsonString())?.AsObject();
        }

        static McpServer Server() => new McpServer(TextReader.Null, TextWriter.Null);


        [Fact]
        public void Initialize_And_ListTools()
        {
            var server = Server();
            JsonObject r = Handle(server, Request(1, "initialize", new JsonObject() {
                ["protocolVersion"] = "2025-03-26", ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject() { ["name"] = "test", ["version"] = "1" }
            }));
            Assert.Equal(1, I(r["id"]));
            Assert.Equal("2025-03-26", S(r["result"]["protocolVersion"]));
            Assert.NotNull(r["result"]["capabilities"]["tools"]);
            Assert.Equal("g3cli", S(r["result"]["serverInfo"]["name"]));

            // unknown protocol version: the server answers with its latest
            r = Handle(server, Request(2, "initialize", new JsonObject() { ["protocolVersion"] = "1999-01-01" }));
            Assert.Equal(McpServer.ProtocolVersions[0], S(r["result"]["protocolVersion"]));

            Assert.Null(server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));

            r = Handle(server, Request(3, "tools/list"));
            var tools = r["result"]["tools"].AsArray();
            Assert.Equal(new[] { "measure", "repair", "boolean", "resolve", "remesh", "simplify", "uv", "convert", "mesh2d", "query2d", "slope", "dgm", "dgm_tin", "cell_volumes", "fidelity", "quality", "deviation", "thickness", "fit", "wrap", "geodesic", "query3d", "polygon2d", "polygon_solids" },
                tools.Select(t => S(t["name"])).ToArray());
            foreach (JsonNode t in tools) {
                Assert.False(string.IsNullOrEmpty(S(t["description"])));
                Assert.Equal("object", S(t["inputSchema"]["type"]));
            }
            JsonNode boolean = tools.First(t => S(t["name"]) == "boolean");
            Assert.Equal(new[] { "a", "b", "output", "op" }, boolean["inputSchema"]["required"].AsArray().Select(S).ToArray());
            Assert.Equal(new[] { "union", "intersection", "difference", "at_least", "xor" },
                boolean["inputSchema"]["properties"]["op"]["enum"].AsArray().Select(S).ToArray());
            Assert.True(B(boolean["inputSchema"]["properties"]["safe_rounding"]["default"]));
            JsonNode mesh2d = tools.First(t => S(t["name"]) == "mesh2d");
            Assert.Equal(new[] { "output" }, mesh2d["inputSchema"]["required"].AsArray().Select(S).ToArray());
            Assert.Equal("string", S(mesh2d["inputSchema"]["properties"]["geometry"]["type"]));
            Assert.Equal("number", S(mesh2d["inputSchema"]["properties"]["min_angle"]["type"]));
            Assert.Equal("integer", S(mesh2d["inputSchema"]["properties"]["steiner_limit"]["type"]));
            JsonNode query2d = tools.First(t => S(t["name"]) == "query2d");
            Assert.Equal(new[] { "input" }, query2d["inputSchema"]["required"].AsArray().Select(S).ToArray());
            Assert.Equal("integer", S(boolean["inputSchema"]["properties"]["k"]["type"]));
            // (slope: the terrain and the tolerance are required; a local frame's outputs are local, used_origin is added back)
            JsonNode slope = tools.First(t => S(t["name"]) == "slope");
            Assert.Equal(new[] { "terrain", "tolerance" }, slope["inputSchema"]["required"].AsArray().Select(S).ToArray());
            Assert.Equal(new[] { "cut", "fill", "auto" }, slope["inputSchema"]["properties"]["direction"]["enum"].AsArray().Select(S).ToArray());
            Assert.Contains("used_origin", S(slope["description"]));
            Assert.Contains("local frame", S(slope["description"]));
            // (dgm_tin: the output and epsilon are required; the breakline heights are a choice)
            JsonNode dgmTin = tools.First(t => S(t["name"]) == "dgm_tin");
            Assert.Equal(new[] { "input", "output", "epsilon" }, dgmTin["inputSchema"]["required"].AsArray().Select(S).OrderBy(x => x == "input" ? 0 : x == "output" ? 1 : 2).ToArray());
            Assert.Equal(new[] { "grid", "input" }, dgmTin["inputSchema"]["properties"]["breakline_heights"]["enum"].AsArray().Select(S).ToArray());

            r = Handle(server, Request(4, "ping"));
            Assert.NotNull(r["result"]);
        }

        [Fact]
        public void ToolCalls()
        {
            var server = Server();
            JsonObject r = Handle(server, Call(1, "measure", new JsonObject() { ["input"] = m.Cube, ["compare"] = m.Shifted }));
            Assert.False(B(r["result"]["isError"]));
            JsonNode sc = r["result"]["structuredContent"];
            Assert.Equal("ok", S(sc["code"]));
            Assert.InRange(D(sc["mesh"]["volume"]), 8 - 1e-9, 8 + 1e-9);
            Assert.InRange(D(sc["hausdorff"]["distance"]), 1 - 1e-9, 1.001);
            // the text content is the same JSON
            JsonNode text = JsonNode.Parse(S(r["result"]["content"][0]["text"]));
            Assert.Equal("measure", S(text["command"]));

            string output = m.Path_("mcp_union.glb");
            r = Handle(server, Call(2, "boolean", new JsonObject() { ["a"] = m.Cube, ["b"] = m.Shifted, ["output"] = output, ["op"] = "union" }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            Assert.InRange(D(r["result"]["structuredContent"]["mesh"]["volume"]), 12 - 1e-6, 12 + 1e-6);
            Assert.True(File.Exists(output));

            // more than two inputs, inside at least two of them
            r = Handle(server, Call(2, "boolean", new JsonObject() {
                ["a"] = m.Cube, ["b"] = m.Shifted, ["more"] = m.Far, ["output"] = m.Path_("mcp_k.obj"), ["op"] = "at_least", ["k"] = 2 }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            Assert.InRange(D(r["result"]["structuredContent"]["mesh"]["volume"]), 4 - 1e-6, 4 + 1e-6);

            // a 2D quality mesh from inline JSON (an agent needs no input file), then its heights sampled
            string terrain = m.Path_("mcp_mesh2d.obj");
            r = Handle(server, Call(4, "mesh2d", new JsonObject() {
                ["geometry"] = "{\"points\": [[0,0],[4,0],[4,4],[0,4]], \"heights\": [0,4,8,4]}", ["output"] = terrain,
                ["max_area"] = 0.5, ["min_angle"] = 30 }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            sc = r["result"]["structuredContent"];
            Assert.True(D(sc["smallest_angle"]) >= 30 - 1e-9);
            Assert.True(D(sc["largest_area"]) <= 0.5);
            Assert.Equal(16, D(sc["area"]), 9);
            Assert.Equal(I(sc["triangles"]), g3.StandardMeshReader.ReadMesh(terrain).TriangleCount);
            r = Handle(server, Call(5, "query2d", new JsonObject() { ["input"] = terrain, ["points"] = "1,1;3,2.5", ["rect"] = "0,0,4,4" }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            sc = r["result"]["structuredContent"];
            Assert.Equal(2, D(sc["points"][0]["height"]), 9);
            Assert.Equal(5.5, D(sc["points"][1]["height"]), 9);
            Assert.Equal(I(sc["triangles"]), I(sc["rect"]["triangles_count"]));

            // a bad geometry is a tool error with a result code, not a protocol error
            r = Handle(server, Call(6, "mesh2d", new JsonObject() { ["geometry"] = "{\"points\": 1}", ["output"] = terrain }));
            Assert.True(B(r["result"]["isError"]));
            Assert.Equal("invalid_arguments", S(r["result"]["structuredContent"]["code"]));

            r = Handle(server, Call(3, "simplify", new JsonObject() { ["input"] = m.Sphere, ["output"] = m.Path_("mcp_s.off"), ["triangles"] = 50.0 }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            Assert.InRange(I(r["result"]["structuredContent"]["mesh"]["triangles"]), 1, 50);
        }

        [Fact]
        public void Polygon2d_LineOps_AreToolArguments()
        {
            var server = Server();
            string line = "{\"type\":\"LineString\",\"coordinates\":[[-5,5],[15,5]]}";
            string square = "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]}";
            JsonObject r = Handle(server, Call(1, "polygon2d", new JsonObject() { ["op"] = "clip_lines", ["subject_geojson"] = line, ["clip_geojson"] = square, ["clip_type"] = "difference" }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            Assert.Equal(2, I(r["result"]["structuredContent"]["result"]["lines"]));
            r = Handle(server, Call(2, "polygon2d", new JsonObject() { ["op"] = "offset_lines", ["subject_geojson"] = line, ["delta"] = 1.0, ["end_type"] = "butt" }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            Assert.Equal(40.0, D(r["result"]["structuredContent"]["result"]["area"]), 6);
            r = Handle(server, Call(3, "polygon2d", new JsonObject() { ["op"] = "simplify", ["subject_geojson"] = square }));
            Assert.True(B(r["result"]["isError"]));
            Assert.Equal("invalid_arguments", S(r["result"]["structuredContent"]["code"]));
        }

        [Fact]
        public void PolygonSolids_IsATool()
        {
            var server = Server();
            string ring(double x, double s) => $"[[{x},{x}],[{x + s},{x}],[{x + s},{x + s}],[{x},{x + s}],[{x},{x}]]";
            string g = "{\"type\":\"MultiPolygon\",\"coordinates\":[[" + ring(0, 10) + "],[" + ring(3, 4) + "]]}";
            JsonObject r = Handle(server, Call(1, "polygon_solids", new JsonObject() { ["input_geojson"] = g, ["return_geojson"] = true }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            var sc = r["result"]["structuredContent"];
            Assert.Equal(1, I(sc["solids"]));
            Assert.Equal(1, I(sc["holes"]));
            Assert.Equal(84, D(sc["area"]));
            Assert.Equal("MultiPolygon", S(sc["result_geojson"]["features"][0]["geometry"]["type"]));
            r = Handle(server, Call(2, "polygon_solids", new JsonObject() { ["input_geojson"] = g, ["simplify_tolerance"] = -1.0 }));
            Assert.True(B(r["result"]["isError"]));
            Assert.Equal("invalid_arguments", S(r["result"]["structuredContent"]["code"]));
        }

        [Fact]
        public void Query3d_OutputAndInsideAuto_AreToolArguments()
        {
            var server = Server();
            string csv = m.Path_("mcp_q3.csv");
            JsonObject r = Handle(server, Call(1, "query3d", new JsonObject() { ["input"] = m.Cube, ["points"] = "[[0,0,0],[3,0,0]]", ["inside"] = "auto", ["output"] = csv }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            var sc = r["result"]["structuredContent"];
            Assert.Equal("crossings", S(sc["points"]["method_used"]));
            Assert.Equal(2, I(sc["output_rows"]));
            Assert.Equal(3, System.IO.File.ReadAllLines(csv).Length);
            r = Handle(server, Call(2, "query3d", new JsonObject() { ["input"] = m.Cube, ["rays"] = "[{\"origin\":[-5,0,0],\"direction\":[1,0,0]}]", ["normalize"] = false }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            Assert.Equal(4.0, D(r["result"]["structuredContent"]["rays"]["results"][0]["t"]), 9);
        }

        [Fact]
        public void Repair_NewOptions_AreToolArguments()
        {
            var server = Server();
            JsonObject r = Handle(server, Call(1, "repair", new JsonObject() { ["input"] = m.Overlapping, ["output"] = m.Path_("mcp_rep.stl"),
                ["exact_resolve_rounds"] = 3, ["wrap_relative_alpha"] = 200.0, ["passthrough_clean_parts"] = false, ["separate_fraction"] = 0.001 }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            var o = r["result"]["structuredContent"]["robust_options"];
            Assert.Equal(3, I(o["exact_resolve_rounds"]));
            Assert.Equal(200.0, D(o["wrap_relative_alpha"]));
            Assert.False(B(o["passthrough_clean_parts"]));
            Assert.Equal(1e-3, D(o["separate_fraction"]));

            r = Handle(server, Call(2, "repair", new JsonObject() { ["input"] = m.Overlapping, ["output"] = m.Path_("mcp_rep2.stl"), ["exact_resolve_rounds"] = 0 }));
            Assert.True(B(r["result"]["isError"]));
            Assert.Equal("invalid_arguments", S(r["result"]["structuredContent"]["code"]));
        }

        [Fact]
        public void ToolErrors_AreResults()
        {
            var server = Server();
            JsonObject r = Handle(server, Call(1, "measure", new JsonObject() { ["input"] = m.Path_("missing.obj") }));
            Assert.True(B(r["result"]["isError"]));
            Assert.Equal("read_error", S(r["result"]["structuredContent"]["code"]));

            r = Handle(server, Call(2, "boolean", new JsonObject() { ["a"] = m.Cube }));
            Assert.True(B(r["result"]["isError"]));
            Assert.Equal("invalid_arguments", S(r["result"]["structuredContent"]["code"]));

            r = Handle(server, Call(3, "remesh", new JsonObject() { ["input"] = m.Cube, ["output"] = m.Path_("x.obj"), ["iterations"] = "ten" }));
            Assert.Equal("invalid_arguments", S(r["result"]["structuredContent"]["code"]));

            r = Handle(server, Call(4, "measure", new JsonObject() { ["input"] = m.Cube, ["nope"] = true }));
            Assert.Equal("invalid_arguments", S(r["result"]["structuredContent"]["code"]));

            r = Handle(server, Call(5, "repair", new JsonObject() { ["input"] = m.Cube, ["output"] = m.Path_("x.dwg") }));
            Assert.Equal("unknown_format", S(r["result"]["structuredContent"]["code"]));
        }

        [Fact]
        public void NonFiniteNumbers_AreInvalidArguments()
        {
            // 1e400 is a valid JSON number but infinite as a double; the CLI already rejected "--tolerance 1e400"
            var server = Server();
            string call = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"measure\",\"arguments\":" +
                "{\"input\":" + JsonValue.Create(m.Cube).ToJsonString() + ",\"compare\":" + JsonValue.Create(m.Shifted).ToJsonString() + ",\"tolerance\":1e400}}}";
            JsonObject r = server.Handle(call).AsObject();
            Assert.True(B(r["result"]["isError"]), r.ToJsonString());
            Assert.Equal("invalid_arguments", S(r["result"]["structuredContent"]["code"]));
            Assert.Contains("tolerance", S(r["result"]["structuredContent"]["message"]));
        }

        [Fact]
        public void ShapeAnalysisTools_TakeIntegerAndNumberArguments()
        {
            var server = Server();
            JsonObject r = Handle(server, Call(1, "fit", new JsonObject() {
                ["input"] = m.Sphere, ["primitive"] = "sphere", ["seed"] = 2, ["max_iterations"] = 500, ["inlier_distance"] = 0.01 }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            Assert.Equal("success", S(r["result"]["structuredContent"]["fit_result"]));
            r = Handle(server, Call(2, "thickness", new JsonObject() {
                ["input"] = m.Cube, ["method"] = "ray", ["ray_count"] = 10, ["min_thickness"] = 3.0 }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            Assert.Equal(12, I(r["result"]["structuredContent"]["thin_triangles"]));
        }

        [Fact]
        public void ProtocolErrors()
        {
            var server = Server();
            JsonObject r = server.Handle("{not json").AsObject();
            Assert.Equal(-32700, I(r["error"]["code"]));

            r = Handle(server, Request(1, "resources/list"));
            Assert.Equal(-32601, I(r["error"]["code"]));
            Assert.Equal(1, I(r["id"]));

            r = Handle(server, Call(2, "frobnicate", new JsonObject()));
            Assert.Equal(-32602, I(r["error"]["code"]));

            r = Handle(server, Request(3, "tools/call"));
            Assert.Equal(-32602, I(r["error"]["code"]));

            r = server.Handle("{\"jsonrpc\":\"2.0\",\"id\":7}").AsObject();
            Assert.Equal(-32600, I(r["error"]["code"]));
        }

        /// <summary> the real executable: g3cli mcp over stdin/stdout, then a plain CLI call with its exit code </summary>
        [Fact]
        public void ChildProcess_StdioServerAndCli()
        {
            string dll = typeof(g3cli.Program).Assembly.Location;
            string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrEmpty(dotnet) || File.Exists(dotnet) == false)
                dotnet = "dotnet";

            var psi = new ProcessStartInfo(dotnet) {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add("mcp");
            using (Process p = Process.Start(psi)) {
                p.StandardInput.WriteLine(Request(1, "initialize", new JsonObject() { ["protocolVersion"] = "2025-06-18" }).ToJsonString());
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                p.StandardInput.WriteLine(Call(2, "measure", new JsonObject() { ["input"] = m.Cube }).ToJsonString());
                p.StandardInput.Close();
                string[] lines = p.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Assert.True(p.WaitForExit(60000));
                Assert.Equal(0, p.ExitCode);
                Assert.Equal(2, lines.Length);
                Assert.Equal("2025-06-18", S(JsonNode.Parse(lines[0])["result"]["protocolVersion"]));
                JsonNode call = JsonNode.Parse(lines[1]);
                Assert.Equal(2, I(call["id"]));
                Assert.Equal(12, I(call["result"]["structuredContent"]["mesh"]["triangles"]));
            }

            psi = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add("measure");
            psi.ArgumentList.Add(m.Path_("missing.stl"));
            using (Process p = Process.Start(psi)) {
                string stdout = p.StandardOutput.ReadToEnd();
                Assert.True(p.WaitForExit(60000));
                Assert.Equal(3, p.ExitCode);
                Assert.Equal("read_error", S(JsonNode.Parse(stdout)["code"]));
            }
        }
    }
}
