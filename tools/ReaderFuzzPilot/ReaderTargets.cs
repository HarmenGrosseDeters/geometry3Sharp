// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The reader fuzz targets: for each reader its procedurally generated seeds and the checks of one input. Shared by the
// SharpFuzz / libFuzzer harness (tools/ReaderFuzzPilot, campaigns on a runner) and by the replay tests in the unit suites,
// which run the seeds and their systematic mutations through the same checks in CI (docs/QA.md 2.4).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using g3;

namespace ReaderFuzz
{
    public static class ReaderTargets
    {
        // readers report bad input with these (StandardMeshReader returns them as GenericReaderError); anything else, eg
        // IndexOutOfRange, NullReference, OutOfMemory, is a crash on the input (the policy of ReaderFuzzTests)
        public static readonly Type[] ParseErrorTypes = {
            typeof(Exception), typeof(FormatException), typeof(EndOfStreamException), typeof(InvalidDataException),
            typeof(IOException), typeof(System.Xml.XmlException), typeof(NotSupportedException), typeof(OverflowException)
        };

        public static bool IsParseError(Exception e) { return ParseErrorTypes.Contains(e.GetType()); }

        static readonly List<FuzzTarget> list = Build();

        public static IReadOnlyList<FuzzTarget> All { get { return list; } }

        public static bool IsMeshFormat(string name)
        {
            return name == "stl" || name == "obj" || name == "off" || name == "ply" || name == "3mf" || name == "glb" || name == "g3mesh";
        }

        public static FuzzTarget Find(string name)
        {
            return list.FirstOrDefault(t => t.Name == name);
        }

        /// <summary> adds targets from another assembly (the command line tool's readers) </summary>
        public static void Add(FuzzTarget target)
        {
            if (list.All(t => t.Name != target.Name)) list.Add(target);
        }

        static List<FuzzTarget> Build()
        {
            var l = new List<FuzzTarget>();
            foreach (string ext in new[] { "stl", "obj", "off", "ply", "3mf", "glb", "g3mesh" }) {
                string e = ext;
                l.Add(new FuzzTarget {
                    Name = e, Text = e == "obj" || e == "off",
                    MakeSeeds = () => MeshSeeds(e),
                    Check = data => CheckMesh(e, data)
                });
            }
            l.Add(new FuzzTarget { Name = "minijson", Text = true, MakeSeeds = JsonSeeds, Check = CheckJson });
            l.Add(new FuzzTarget { Name = "esri", Text = true, MakeSeeds = EsriSeeds, Check = d => CheckGrid(d, true) });
            l.Add(new FuzzTarget { Name = "xyz", Text = true, MakeSeeds = XyzSeeds, Check = d => CheckGrid(d, false) });
            foreach (string kind in new[] { "poly", "node", "ele" }) {
                string k = kind;
                l.Add(new FuzzTarget { Name = k, Text = true, MakeSeeds = () => TriangleSeeds(k), Check = d => CheckTriangle(k, d) });
            }
            return l;
        }

        // ---------------------------------------------------------------- meshes

        public static MeshFormatReader FormatReader(string extension)
        {
            switch (extension) {
                case "obj": return new OBJFormatReader();
                case "off": return new OFFFormatReader();
                case "stl": return new STLFormatReader();
                case "ply": return new PLYFormatReader();
                case "3mf": return new ThreeMFFormatReader();
                case "glb": return new GLTFFormatReader();
                case "g3mesh": return new BinaryG3FormatReader();
            }
            throw new ArgumentException(extension);
        }

        /// <summary> a small mesh with normals, colors and uvs: a cube sphere with 26 vertices </summary>
        public static DMesh3 SeedMesh()
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 3 }.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, new Vector3d(1, 2, 3));
            mesh.EnableVertexColors(Vector3f.One);
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d n = mesh.GetVertexNormal(vid);
                mesh.SetVertexColor(vid, new Vector3f(0.5 + 0.5 * n.x, 0.5 + 0.5 * n.y, 0.5 + 0.5 * n.z));
            }
            return mesh;
        }

        static WriteOptions AllAttributes(bool binary)
        {
            WriteOptions options = WriteOptions.Defaults;
            options.bWriteBinary = binary;
            options.bPerVertexNormals = true;
            options.bPerVertexColors = true;
            options.bPerVertexUVs = true;
            return options;
        }

        static byte[] WriteMesh(string extension, bool binary)
        {
            string dir = Path.Combine(Path.GetTempPath(), "g3fuzzseed_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                string path = Path.Combine(dir, "mesh." + extension);
                IOWriteResult w = StandardMeshWriter.WriteMesh(path, SeedMesh(), AllAttributes(binary));
                if (w.code != IOCode.Ok) throw new InvalidOperationException("seed for " + extension + " not written: " + w.message);
                return File.ReadAllBytes(path);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        static List<byte[]> MeshSeeds(string ext)
        {
            switch (ext) {
                case "stl": return new List<byte[]> { WriteMesh("stl", true), WriteMesh("stl", false) };
                case "ply": return new List<byte[]> { WriteMesh("ply", false), WriteMesh("ply", true) };
                case "3mf": case "glb": case "g3mesh": return new List<byte[]> { WriteMesh(ext, true) };
                default: return new List<byte[]> { WriteMesh(ext, false) };
            }
        }

        // The invariants of the DMesh3 data structure that later operations rely on. Unlike DMesh3.CheckValidity, it allows
        // inconsistently oriented neighbors, which DMesh3 stores (and a file may contain).
        public static string StructureProblem(DMesh3 mesh)
        {
            int nv = mesh.MaxVertexID;
            if (mesh.VerticesBuffer.size != 3 * nv) return "vertex buffer size";
            if (mesh.HasVertexNormals && mesh.NormalsBuffer.size != 3 * nv) return "normal buffer size";
            if (mesh.HasVertexColors && mesh.ColorsBuffer.size != 3 * nv) return "color buffer size";
            if (mesh.HasVertexUVs && mesh.UVBuffer.size != 2 * nv) return "uv buffer size";
            if (mesh.HasTriangleGroups && mesh.GroupsBuffer.size != mesh.MaxTriangleID) return "group buffer size";
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                if (!mesh.IsVertex(tv.a) || !mesh.IsVertex(tv.b) || !mesh.IsVertex(tv.c)) return "triangle " + t + ": missing vertex";
                if (tv.a == tv.b || tv.b == tv.c || tv.a == tv.c) return "triangle " + t + ": repeated vertex";
                Index3i te = mesh.GetTriEdges(t);
                for (int j = 0; j < 3; ++j) {
                    if (!mesh.IsEdge(te[j])) return "triangle " + t + ": missing edge";
                    Index2i ev = mesh.GetEdgeV(te[j]), et = mesh.GetEdgeT(te[j]);
                    if (!IndexUtil.same_pair_unordered(tv[j], tv[(j + 1) % 3], ev.a, ev.b)) return "triangle " + t + ": edge " + te[j] + " has other vertices";
                    if (et.a != t && et.b != t) return "triangle " + t + ": edge " + te[j] + " does not reference it";
                }
            }
            foreach (int e in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(e);
                if (!mesh.IsTriangle(et.a) || (et.b != DMesh3.InvalidID && !mesh.IsTriangle(et.b))) return "edge " + e + ": missing triangle";
            }
            return "";
        }

        static string Describe(Exception e)
        {
            return e.GetType().Name + ": " + e.Message + " at " + string.Join(" < ", (e.StackTrace ?? "").Split('\n').Select(s => s.Trim()).Where(s => s.Contains("g3.")).Take(4).ToArray());
        }

        // (the allocation budget of ReaderFuzzTests: a reader may allocate in proportion to the input, not to the sizes it claims)
        static string AllocationProblem(long allocated, int length)
        {
            long budget = 64L * 1024 * 1024 + 4000L * length;
            return allocated > budget ? "allocated " + allocated / (1024 * 1024) + " MB for " + length + " bytes of input" : "";
        }

        static long AllocatedBytes()
        {
#if NETCOREAPP
            return GC.GetAllocatedBytesForCurrentThread();
#else
            return 0;
#endif
        }

        public static string CheckMesh(string extension, byte[] data)
        {
            return CheckMeshStream(extension, new MemoryStream(data, false), data.Length);
        }

        /// <summary> (the binary readers turn a caught loop exception into this result: the exception type is gone, so it is a finding to triage) </summary>
        public static string CaughtExceptionProblem(IOReadResult result)
        {
            if (result.code == IOCode.GenericReaderError && result.message != null && result.message.StartsWith("exception: ", StringComparison.Ordinal))
                return "the reader caught an exception: " + result.message;
            return "";
        }

        public static string CheckMeshStream(string extension, Stream stream, int length)
        {
            Thread_InvariantCulture();
            var builder = new DMesh3Builder();
            IOReadResult result;
            long before = AllocatedBytes();
            try {
                // the format reader itself: StandardMeshReader turns every exception into GenericReaderError and would hide crashes
                result = FormatReader(extension).ReadFile(stream, builder, ReadOptions.Defaults, null);
            } catch (Exception e) {
                return IsParseError(e) ? "" : "threw " + Describe(e);
            }
            string alloc = AllocationProblem(AllocatedBytes() - before, length);
            if (alloc != "") return alloc + " (" + result.code + ")";
            string caught = CaughtExceptionProblem(result);
            if (caught != "") return caught;
            if (result.code != IOCode.Ok) return "";
            var data = new byte[length];
            foreach (DMesh3 mesh in builder.Meshes) {
                if (mesh.VertexCount > data.Length || mesh.TriangleCount > data.Length)
                    return "Ok with " + mesh.VertexCount + " vertices / " + mesh.TriangleCount + " triangles from " + data.Length + " bytes";
                string problem = StructureProblem(mesh);
                if (problem != "") return "Ok with an invalid mesh: " + problem;
                // (.g3mesh is the library's own binary format of raw doubles and keeps what it stores, as in ReaderFuzzTests)
                if (extension != "g3mesh" && mesh.Vertices().All(v => v.IsFinite) == false) return "Ok with non-finite coordinates";
            }
            return "";
        }

        static void Thread_InvariantCulture()
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        }

        // ---------------------------------------------------------------- MiniJson (glTF, 3MF extension data, ...)

        static List<byte[]> JsonSeeds()
        {
            return new List<byte[]> {
                Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"scenes\":[{\"nodes\":[0,1]}],\"n\":-1.5e3,\"b\":[true,false,null],\"s\":\"a\\u00e9\\n\\\"q\\\"\",\"o\":{\"k\":[[],{}]}}"),
                Encoding.UTF8.GetBytes("[0,1,2.5,-0,1e400,\"x\",[[[]]]]")
            };
        }

        static string CheckJson(byte[] data)
        {
            Thread_InvariantCulture();
            try {
                MiniJson.Parse(Encoding.UTF8.GetString(data));
                return "";
            } catch (Exception e) {
                return IsParseError(e) ? "" : "threw " + Describe(e);
            }
        }

        // ---------------------------------------------------------------- height grids

        static List<byte[]> EsriSeeds()
        {
            return new List<byte[]> { Encoding.ASCII.GetBytes("ncols 4\nnrows 3\nxllcorner 100.5\nyllcorner 200.25\ncellsize 2\nNODATA_value -9999\n1 2 3 4\n5 -9999 7 8\n9.5 10 11e0 12\n") };
        }

        static List<byte[]> XyzSeeds()
        {
            var sb = new StringBuilder();
            for (int j = 0; j < 3; ++j)
                for (int i = 0; i < 4; ++i)
                    sb.Append((i * 2.0).ToString("R", CultureInfo.InvariantCulture)).Append(' ').Append((j * 2.0).ToString("R", CultureInfo.InvariantCulture)).Append(' ').Append((i + 3 * j + 0.5).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            return new List<byte[]> { Encoding.ASCII.GetBytes(sb.ToString()) };
        }

        static string CheckGrid(byte[] data, bool esri)
        {
            Thread_InvariantCulture();
            string text = Encoding.UTF8.GetString(data);
            long before = AllocatedBytes();
            HeightGridReadResult r;
            try {
                r = esri ? HeightGridReader.ReadEsriAscii(new StringReader(text)) : HeightGridReader.ReadXyz(new StringReader(text));
            } catch (Exception e) {
                // the readers report bad input by status: any exception is a crash on the input
                return "threw " + Describe(e);
            }
            string alloc = AllocationProblem(AllocatedBytes() - before, text.Length);
            if (alloc != "") return alloc + " (" + r.Status + ")";
            if (r.Status == HeightGridStatus.Ok) {
                HeightGrid g = r.Grid;
                if (g == null || g.Z == null || g.Z.Length != (long)g.NX * g.NY || g.NX < 1 || g.NY < 1) return "Ok with an inconsistent grid";
                if (!(g.CellSize > 0) || double.IsInfinity(g.CellSize) || double.IsNaN(g.X0) || double.IsInfinity(g.X0) || double.IsNaN(g.Y0) || double.IsInfinity(g.Y0)) return "Ok with a non-finite origin or cell size";
                if (g.Z.Any(double.IsInfinity)) return "Ok with an infinite height";
                if (g.Z.Length > Math.Max(1024, 64L * text.Length)) return "Ok with " + g.Z.Length + " nodes from " + text.Length + " characters";
            } else if (r.Grid != null || r.Message == null) {
                return "a failure " + r.Status + " with a grid or without a message";
            }
            return "";
        }

        // ---------------------------------------------------------------- Triangle .poly / .node / .ele (g3cli.TrianglePolyFormat)

        static List<byte[]> TriangleSeeds(string kind)
        {
            var input = new ConstrainedMesh2Input();
            double[] xy = { 0, 0, 2, 0, 2, 1, 1, 1, 1, 2, 0, 2, 0.25, 0.25, 0.75, 0.25, 0.75, 0.75, 0.25, 0.75 };
            for (int i = 0; i < xy.Length; i += 2) input.Points.Add(new Vector2d(xy[i], xy[i + 1]));
            input.Heights = input.Points.Select(p => p.x + 2 * p.y).ToList();
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3, 4, 5 }, false, 5));
            for (int i = 6; i < 10; ++i) input.Segments.Add(new Mesh2Segment(i, i == 9 ? 6 : i + 1, 9));
            input.HoleSeeds.Add(new Vector2d(0.5, 0.5));
            input.Regions.Add(Mesh2Region.BySeed(new Vector2d(1.5, 0.5), 3, 0.01));
            string text;
            switch (kind) {
                case "poly": text = g3cli.TrianglePolyFormat.WritePolyOfInput(input); break;
                case "node": text = g3cli.TrianglePolyFormat.WriteNode(input.Points, input.Heights, new[] { (0, 1, 5), (6, 7, 9) }); break;
                default: text = g3cli.TrianglePolyFormat.WriteEle(new[] { new Index3i(0, 1, 2), new Index3i(0, 2, 3), new Index3i(3, 4, 5), new Index3i(0, 3, 5) }, new[] { 1, 1, 2, 2 }); break;
            }
            return new List<byte[]> { Encoding.ASCII.GetBytes(text) };
        }

        static string CheckTriangle(string kind, byte[] data)
        {
            Thread_InvariantCulture();
            string text = Encoding.ASCII.GetString(data);
            int items = 0;
            try {
                if (kind == "poly") {
                    var r = g3cli.TrianglePolyFormat.ReadPoly(text, null, true, out _);
                    items = r.Points.Count + r.Segments.Count + r.HoleSeeds.Count + r.Regions.Count + r.Rings.Sum(g => g.Vertices.Length);
                    if (r.Rings.Any(g => g.Vertices.Any(v => v < 0 || v >= r.Points.Count))) return "a ring index out of range";
                    if (r.Points.Any(p => double.IsNaN(p.x) || double.IsInfinity(p.x) || double.IsNaN(p.y) || double.IsInfinity(p.y))) return "a non-finite point was accepted";
                    if (r.Segments.Any(s => s.A < 0 || s.B < 0 || s.A >= r.Points.Count || s.B >= r.Points.Count)) return "a segment index out of range was accepted";
                } else if (kind == "node") {
                    items = g3cli.TrianglePolyFormat.ReadNode(text, true, out _).Points.Count;
                } else {
                    var t = g3cli.TrianglePolyFormat.ReadEle(text, 6, 0, out _);
                    items = t.Count;
                    if (t.Any(x => x.a < 0 || x.b < 0 || x.c < 0 || x.a >= 6 || x.b >= 6 || x.c >= 6)) return "a corner out of range was accepted";
                }
            } catch (FormatException) {
                return "";
            } catch (Exception e) {
                return "threw " + Describe(e);
            }
            // the result is bounded by the input (each item takes at least two characters: a number and a separator)
            return items > text.Length + 16 ? items + " items from " + text.Length + " bytes" : "";
        }
    }
}
