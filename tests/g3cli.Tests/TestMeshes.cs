using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using g3;
using g3cli;

namespace g3cli.Tests
{
    /// <summary>
    /// Small test meshes, written to a fresh temp directory per test class, and helpers to run the CLI in process.
    /// </summary>
    public sealed class TestMeshes : IDisposable
    {
        public readonly string Dir;

        /// <summary> cube [-1,1]^3 (volume 8, area 24) </summary>
        public string Cube => Path.Combine(Dir, "cube.obj");
        /// <summary> the cube moved by +1 in x (overlaps the cube in a volume of 4) </summary>
        public string Shifted => Path.Combine(Dir, "shifted.stl");
        /// <summary> the cube moved by +5 in x (disjoint) </summary>
        public string Far => Path.Combine(Dir, "far.3mf");
        /// <summary> the cube with one triangle removed (open) </summary>
        public string Open => Path.Combine(Dir, "open.obj");
        /// <summary> cube and shifted cube in one file (self-intersecting, winding number 2 in the overlap) </summary>
        public string Overlapping => Path.Combine(Dir, "overlapping.off");
        /// <summary> unit sphere, 6 * 2 * 8^2 triangles </summary>
        public string Sphere => Path.Combine(Dir, "sphere.ply");
        /// <summary> not a mesh </summary>
        public string Garbage => Path.Combine(Dir, "garbage.obj");
        /// <summary> a valid file without triangles </summary>
        public string Empty => Path.Combine(Dir, "empty.off");

        public TestMeshes()
        {
            Dir = Path.Combine(Path.GetTempPath(), "g3cli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);

            DMesh3 cube = CubeMesh(Vector3d.Zero);
            DMesh3 shifted = CubeMesh(new Vector3d(1, 0, 0));
            Write(Cube, cube);
            Write(Shifted, shifted);
            Write(Far, CubeMesh(new Vector3d(5, 0, 0)));

            DMesh3 open = new DMesh3(cube);
            open.RemoveTriangle(open.TriangleIndices().First());
            Write(Open, new DMesh3(open, true));

            DMesh3 both = new DMesh3(cube);
            new MeshEditor(both).AppendMesh(shifted);
            Write(Overlapping, both);

            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 9 }.Generate().MakeDMesh();
            Write(Sphere, sphere);

            File.WriteAllText(Garbage, "this is not\na mesh file\nf 1 2 x\n");
            File.WriteAllText(Empty, "OFF\n1 0 0\n0 0 0\n");
        }

        public static DMesh3 CubeMesh(Vector3d center)
        {
            return new TrivialBox3Generator() { Box = new Box3d(center, Vector3d.One) }.Generate().MakeDMesh();
        }

        static void Write(string path, DMesh3 mesh)
        {
            WriteOptions options = WriteOptions.Defaults;
            options.bWriteBinary = true;
            IOWriteResult r = StandardMeshWriter.WriteMesh(path, mesh, options);
            if (r.code != IOCode.Ok)
                throw new Exception("could not write test mesh " + path + ": " + r.message);
        }

        public string Path_(string name) => Path.Combine(Dir, name);

        public void Dispose()
        {
            try { Directory.Delete(Dir, true); } catch { }
        }


        /// <summary> runs g3cli in process: exit code and the parsed JSON result </summary>
        public static (int exit, JsonObject result) Run(params string[] args)
        {
            var sw = new StringWriter();
            int exit = Cli.Run(args, sw);
            JsonNode node = JsonNode.Parse(sw.ToString());
            return (exit, node.AsObject());
        }

        public static double D(JsonNode n) => n.GetValue<double>();
        public static int I(JsonNode n) => n.GetValue<int>();
        public static bool B(JsonNode n) => n.GetValue<bool>();
        public static string S(JsonNode n) => n.GetValue<string>();
    }
}
