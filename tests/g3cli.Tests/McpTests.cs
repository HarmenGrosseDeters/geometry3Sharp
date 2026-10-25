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
            Assert.Equal(new[] { "measure", "repair", "boolean", "resolve", "remesh", "simplify", "convert" },
                tools.Select(t => S(t["name"])).ToArray());
            foreach (JsonNode t in tools) {
                Assert.False(string.IsNullOrEmpty(S(t["description"])));
                Assert.Equal("object", S(t["inputSchema"]["type"]));
            }
            JsonNode boolean = tools.First(t => S(t["name"]) == "boolean");
            Assert.Equal(new[] { "a", "b", "output", "op" }, boolean["inputSchema"]["required"].AsArray().Select(S).ToArray());
            Assert.Equal(3, boolean["inputSchema"]["properties"]["op"]["enum"].AsArray().Count);

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

            r = Handle(server, Call(3, "simplify", new JsonObject() { ["input"] = m.Sphere, ["output"] = m.Path_("mcp_s.off"), ["triangles"] = 50.0 }));
            Assert.False(B(r["result"]["isError"]), r.ToJsonString());
            Assert.InRange(I(r["result"]["structuredContent"]["mesh"]["triangles"]), 1, 50);
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
