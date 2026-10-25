using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using g3;

// Write and read a mesh in every format through the packaged library, and check the package contents.
static class Program
{
    static int Main(string[] args)
    {
        int failures = 0;
        Action<bool, string> check = (ok, what) => {
            Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
            if (!ok) failures++;
        };

        if (args.Length > 0) {
            using (ZipArchive zip = ZipFile.OpenRead(args[0])) {
                string[] entries = zip.Entries.Select(e => e.FullName).ToArray();
                foreach (string expected in new[] {
                        "lib/net10.0/geometry3Sharp.dll", "lib/net10.0/geometry3Sharp.xml",
                        "lib/netstandard2.0/geometry3Sharp.dll", "lib/netstandard2.0/geometry3Sharp.xml",
                        "README.md", "LICENSE" })
                    check(entries.Contains(expected), "package contains " + expected);
            }
        }

        string asm = typeof(DMesh3).Assembly.Location;
        Console.WriteLine("geometry3Sharp from " + asm);
        check(File.Exists(Path.ChangeExtension(asm, ".xml")), "XML documentation next to the assembly");

        DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 4 }.Generate().MakeDMesh();
        string dir = Path.Combine(Path.GetTempPath(), "g3smoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            foreach (string ext in new[] { "obj", "stl", "off", "g3mesh", "ply", "3mf", "glb", "gltf" }) {
                string path = Path.Combine(dir, "sphere." + ext);
                IOWriteResult w = StandardMeshWriter.WriteMesh(path, mesh, WriteOptions.Defaults);
                DMesh3 read = w.code == IOCode.Ok ? StandardMeshReader.ReadMesh(path) : null;
                check(read != null && read.TriangleCount == mesh.TriangleCount, "write + read ." + ext);
            }
        } finally {
            Directory.Delete(dir, true);
        }
        return failures == 0 ? 0 : 1;
    }
}
