using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using g3cli;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> boolean --volume-gradient (BooleanVolumeGradient): the .csv and .json formats, the contacts, the defaults unchanged, the errors, MCP. </summary>
    public class VolumeGradientCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public VolumeGradientCliTests(TestMeshes meshes) { m = meshes; }

        static double P(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        /// <summary> the cube [-1,1]^3 moved by +2 in x: it touches the cube's +x face (a flush contact of area 4) </summary>
        string Touching()
        {
            string path = m.Path_("touching.obj");
            if (File.Exists(path) == false)
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, CubeMesh(new Vector3d(2, 0, 0)), WriteOptions.Defaults).code);
            return path;
        }

        [Fact]
        public void Csv_OneRowPerInputVertex_SumsAreTheRigidMotionDerivatives()
        {
            // union of the cube and the cube shifted by +1 in x: V = 8 + 8 - 4; moving the shifted cube by +x grows V by its
            // cross-section 4 (the sum of the x entries of its vertices), moving the cube by +x shrinks it by 4
            string grad = m.Path_("union_gradient.csv");
            var (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("union_g.stl"), "--op", "union", "--volume-gradient", grad, "--compact");
            Assert.Equal(0, exit);
            JsonNode vg = r["volume_gradient"];
            Assert.Equal(grad, S(vg["output"]));
            Assert.Equal("csv", S(vg["format"]));
            Assert.Equal(12.0, D(vg["volume"]), 12);
            Assert.InRange(D(vg["volume_bound"]), 0, 1e-12);
            Assert.Equal(4, vg["contacts"].AsArray().Count);         // the side faces y = +-1, z = +-1 overlap flush in 0 <= x <= 1

            string[] lines = File.ReadAllLines(grad);
            Assert.Equal("mesh,vertex,x,y,z,gx,gy,gz,bx,by,bz", lines[0]);
            string[][] rows = lines.Skip(1).Select(l => l.Split(',')).ToArray();
            Assert.Equal(16, rows.Length);
            Assert.Equal(16, I(vg["rows"]));
            Assert.All(rows, row => Assert.Equal(11, row.Length));
            Assert.Equal(new[] { 0, 1 }, rows.Select(row => int.Parse(row[0])).Distinct().ToArray());
            double sumA = rows.Where(row => row[0] == "0").Sum(row => P(row[5])), sumB = rows.Where(row => row[0] == "1").Sum(row => P(row[5]));
            Assert.Equal(-4.0, sumA, 12);
            Assert.Equal(4.0, sumB, 12);
            Assert.Equal(0.0, rows.Sum(row => P(row[6])), 12);       // no y motion changes V
            // the positions are those of the mesh as read, and every bound is a small nonnegative number
            DMesh3 b = StandardMeshReader.ReadMesh(m.Shifted);
            foreach (string[] row in rows.Where(row => row[0] == "1")) {
                Vector3d p = b.GetVertex(int.Parse(row[1]));
                Assert.Equal(p, new Vector3d(P(row[2]), P(row[3]), P(row[4])));
            }
            Assert.All(rows, row => Assert.All(row.Skip(8), x => Assert.InRange(P(x), 0, 1e-12)));
        }

        [Fact]
        public void Json_HasVerticesVolumeAndContactsWithAlternatives()
        {
            // the touching cubes: their union has a kink at the common face; the gradient is the realized (merged) branch, the
            // alternative the change to the separated one
            string grad = m.Path_("touching_gradient.json");
            var (exit, r) = Run("boolean", m.Cube, Touching(), m.Path_("touching_union.stl"), "--op", "union", "--volume-gradient", grad, "--compact");
            Assert.Equal(0, exit);
            JsonNode file = JsonNode.Parse(File.ReadAllText(grad));
            Assert.Equal(16.0, D(file["volume"]), 12);
            Assert.Equal(D(r["volume_gradient"]["volume"]), D(file["volume"]));
            Assert.Equal(D(r["volume_gradient"]["volume_bound"]), D(file["volume_bound"]));
            Assert.Equal(r["volume_gradient"]["contacts"].ToJsonString(), file["contacts"].ToJsonString());
            JsonArray contacts = file["contacts"].AsArray();
            Assert.Single(contacts);
            Assert.Equal(new[] { 0, 1 }, contacts[0]["meshes"].AsArray().Select(I).ToArray());
            Assert.Equal(2, I(contacts[0]["sheets"]));
            Assert.Equal(4.0, D(contacts[0]["area"]), 12);
            JsonArray alternatives = contacts[0]["alternatives"].AsArray();
            Assert.NotEmpty(alternatives);
            foreach (JsonNode t in alternatives[0].AsArray()) {
                Assert.InRange(I(t["mesh"]), 0, 1);
                Assert.Equal(3, t["value"].AsArray().Count);
                Assert.Equal(3, t["bound"].AsArray().Count);
            }

            JsonArray vertices = file["vertices"].AsArray();
            Assert.Equal(16, vertices.Count);
            // dV/dt for the touching cube moving along x: the realized (merged) branch only moves its far face, +4 (the derivative
            // when it moves into the cube); the other branch, the gradient plus the alternative, is that of the separated cubes, 0
            double realized = vertices.Where(v => I(v["mesh"]) == 1).Sum(v => D(v["gradient"][0]));
            double other = realized + alternatives[0].AsArray().Where(t => I(t["mesh"]) == 1).Sum(t => D(t["value"][0]));
            Assert.Equal(4.0, realized, 12);
            Assert.Equal(0.0, other, 12);
            foreach (JsonNode v in vertices) {
                Assert.Equal(3, v["position"].AsArray().Count);
                Assert.Equal(3, v["gradient"].AsArray().Count);
                Assert.All(v["bound"].AsArray(), x => Assert.InRange(D(x), 0, 1e-12));
            }
        }

        [Fact]
        public void WithoutTheOption_TheResultIsUnchanged()
        {
            string plain = m.Path_("plain_union.stl"), withGradient = m.Path_("gradient_union.stl");
            var (e0, r0) = Run("boolean", m.Cube, m.Shifted, plain, "--op", "union", "--compact");
            var (e1, r1) = Run("boolean", m.Cube, m.Shifted, withGradient, "--op", "union", "--volume-gradient", m.Path_("g.json"), "--compact");
            Assert.Equal(0, e0);
            Assert.Equal(0, e1);
            Assert.Null(r0["volume_gradient"]);
            Assert.NotNull(r1["volume_gradient"]);
            Assert.Equal(File.ReadAllBytes(plain), File.ReadAllBytes(withGradient));     // RetainClassification does not change the result
            Assert.Equal(r0["mesh"].ToJsonString(), r1["mesh"].ToJsonString());
            Assert.Equal(r0["boolean"].ToJsonString(), r1["boolean"].ToJsonString());
        }

        [Fact]
        public void OpenInput_FailsAndWritesNothing()
        {
            // --allow-open lets the boolean run; the volume of an open result is not defined, so the gradient fails
            string grad = m.Path_("open_gradient.csv"), output = m.Path_("open_union.stl");
            var (exit, r) = Run("boolean", m.Cube, m.Open, output, "--op", "union", "--allow-open", "--volume-gradient", grad, "--compact");
            Assert.Equal(ResultCodes.ExitCode(ResultCodes.OperationFailed), exit);
            Assert.Equal(ResultCodes.OperationFailed, S(r["code"]));
            Assert.Contains("not closed", S(r["message"]));
            Assert.Contains("not closed", S(r["volume_gradient_error"]));
            Assert.False(File.Exists(grad));
            Assert.False(File.Exists(output));
            Assert.False(File.Exists(grad + ".g3tmp"));
        }

        [Fact]
        public void UnknownFormatAndMissingDirectory_FailBeforeTheBoolean()
        {
            var (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("x.stl"), "--op", "union", "--volume-gradient", m.Path_("g.txt"), "--compact");
            Assert.Equal(ResultCodes.UnknownFormat, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(ResultCodes.UnknownFormat), exit);
            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("x.stl"), "--op", "union", "--volume-gradient", Path.Combine(m.Dir, "missing", "g.csv"), "--compact");
            Assert.Equal(ResultCodes.WriteError, S(r["code"]));
            Assert.False(File.Exists(m.Path_("x.stl")));
        }

        [Fact]
        public void Mcp_BooleanTakesVolumeGradient()
        {
            var server = new McpServer(TextReader.Null, TextWriter.Null);
            JsonNode list = server.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
            JsonNode boolean = list["result"]["tools"].AsArray().First(t => S(t["name"]) == "boolean");
            Assert.Equal("string", S(boolean["inputSchema"]["properties"]["volume_gradient"]["type"]));
            Assert.DoesNotContain("volume_gradient", boolean["inputSchema"]["required"].AsArray().Select(S));

            string grad = m.Path_("mcp_gradient.json");
            var call = new JsonObject() { ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = "tools/call", ["params"] = new JsonObject() {
                ["name"] = "boolean", ["arguments"] = new JsonObject() { ["a"] = m.Cube, ["b"] = m.Shifted, ["output"] = m.Path_("mcp_union.stl"),
                    ["op"] = "union", ["volume_gradient"] = grad } } };
            JsonNode res = server.Handle(call.ToJsonString());
            Assert.False(B(res["result"]["isError"]));
            Assert.Equal(12.0, D(res["result"]["structuredContent"]["volume_gradient"]["volume"]), 12);
            Assert.Equal(16, JsonNode.Parse(File.ReadAllText(grad))["vertices"].AsArray().Count);
        }
    }
}
