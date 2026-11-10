using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Seeded fuzzing of the mesh readers (docs/QA.md): valid files written by our writers, mutated (flipped and inserted
    /// bytes, truncation, duplicated slices, extreme numbers in text formats), read through StandardMeshReader. Each read
    /// must return within a time budget, must not throw, must stay within an allocation budget relative to the input
    /// size, and a read that reports Ok must give valid meshes whose size is bounded by the input. A failure message
    /// names the format, seed and mutation, so that the case can be replayed (and turned into an example test).
    /// </summary>
    public class ReaderFuzzTests
    {
        // 150 cases per format in the normal suite (about 2 s); G3_FUZZ_CASES=n runs more, eg in a scheduled job
        static readonly int CasesPerFormat = int.TryParse(Environment.GetEnvironmentVariable("G3_FUZZ_CASES"), out int n) && n > 0 ? n : 150;
        static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(10);

        static readonly string[] ExtremeNumbers = {
            "1e400", "-1e400", "NaN", "nan", "inf", "-0", "1,5", "2147483647", "2147483648", "-2147483649",
            "4294967296", "99999999999999999999", "-1", "0", "1e-320", "0x10", "", "1e", "--1",
            // long digit strings, signed zeros and range limits: the exact decimal parser (docs/design/DETERMINISM.md C2)
            "5.6348333975312060066e-8", "-0.0", "0.1000000000000000055511151231257827", "1.7976931348623159e308", "4.9406564584124654e-324",
            "9007199254740993", "+.5e1", "123456789012345678901234567890e-30", "1e-400", ".e5", "1e+"
        };

        static readonly Type[] ParseErrorTypes = {
            typeof(Exception), typeof(FormatException), typeof(EndOfStreamException), typeof(InvalidDataException),
            typeof(IOException), typeof(System.Xml.XmlException), typeof(NotSupportedException), typeof(OverflowException)
        };

        static MeshFormatReader FormatReader(string extension)
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

        static byte[] Write(string extension, bool binary)
        {
            DMesh3 mesh = MeshFormatTestUtil.AttributeSphere(new Vector3d(1, 2, 3));
            string dir = MeshFormatTestUtil.TempDir();
            try {
                string path = Path.Combine(dir, "mesh." + extension);
                IOWriteResult w = StandardMeshWriter.WriteMesh(path, mesh, MeshFormatTestUtil.AllAttributes(binary));
                Assert.Equal(IOCode.Ok, w.code);
                return File.ReadAllBytes(path);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        public static IEnumerable<object[]> Formats()
        {
            yield return new object[] { "stl", false };
            yield return new object[] { "stl", true };
            yield return new object[] { "3mf", true };
            yield return new object[] { "glb", true };
            yield return new object[] { "obj", false };
            yield return new object[] { "off", false };
            yield return new object[] { "ply", false };
            yield return new object[] { "ply", true };
        }

        /// <summary>One mutation of the input, described for the failure message.</summary>
        static byte[] Mutate(byte[] input, bool text, Random rand, out string what)
        {
            int n = input.Length;
            if (rand.Next(4) == 0) {
                // a number in the header (the first 512 bytes: counts in PLY, OFF and binary STL headers) replaced
                byte[] r = MutateHeaderNumber(input, rand, out what);
                if (r != null) return r;
            }
            switch (rand.Next(text ? 6 : 5)) {
                case 0: {
                    byte[] r = (byte[])input.Clone();
                    int count = 1 + rand.Next(8);
                    for (int k = 0; k < count; ++k)
                        r[rand.Next(n)] = (byte)rand.Next(256);
                    what = $"{count} random bytes";
                    return r;
                }
                case 1: {
                    int len = rand.Next(n);
                    what = $"truncated to {len}";
                    return input.Take(len).ToArray();
                }
                case 2: {
                    int start = rand.Next(n), len = 1 + rand.Next(Math.Min(4096, n - start));
                    int at = rand.Next(n);
                    what = $"[{start}, +{len}) copied to {at}";
                    return input.Take(at).Concat(input.Skip(start).Take(len)).Concat(input.Skip(at)).ToArray();
                }
                case 3: {
                    // a 32-bit word set to an extreme value (counts, offsets, lengths in binary headers)
                    byte[] r = (byte[])input.Clone();
                    int at = rand.Next(Math.Max(1, n - 4));
                    int[] words = { int.MaxValue, int.MinValue, -1, 0, 0x7FFFFFF, 1 << 30 };
                    int word = words[rand.Next(words.Length)];
                    byte[] b = BitConverter.GetBytes(word);
                    Array.Copy(b, 0, r, at, Math.Min(4, n - at));
                    what = $"int32 {word} at {at}";
                    return r;
                }
                case 4: {
                    int at = rand.Next(n), len = 1 + rand.Next(64);
                    byte[] noise = new byte[len];
                    rand.NextBytes(noise);
                    what = $"{len} random bytes inserted at {at}";
                    return input.Take(at).Concat(noise).Concat(input.Skip(at)).ToArray();
                }
                default: {
                    // text: a number token replaced by an extreme or malformed one
                    string s = Encoding.ASCII.GetString(input);
                    MatchCollection numbers = Regex.Matches(s, @"-?\d+(\.\d+)?(e-?\d+)?");
                    if (numbers.Count == 0) { what = "unchanged"; return input; }
                    Match m = numbers[rand.Next(numbers.Count)];
                    string repl = ExtremeNumbers[rand.Next(ExtremeNumbers.Length)];
                    what = $"number '{m.Value}' at {m.Index} replaced by '{repl}'";
                    return Encoding.ASCII.GetBytes(s.Substring(0, m.Index) + repl + s.Substring(m.Index + m.Length));
                }
            }
        }

        static readonly string[] HeaderNumbers = {
            "2147483647", "2147483648", "-1", "0", "100000000", "400000000", "4294967295", "99999999999", "1e9", "NaN"
        };

        /// <summary>A decimal number in the first 512 bytes (if any) replaced by an extreme count, or null.</summary>
        static byte[] MutateHeaderNumber(byte[] input, Random rand, out string what)
        {
            int headerLength = Math.Min(512, input.Length);
            string header = Encoding.ASCII.GetString(input, 0, headerLength);
            MatchCollection numbers = Regex.Matches(header, @"(?<![\w.])\d+(?![\w.])");
            if (numbers.Count == 0) { what = ""; return null; }
            Match m = numbers[rand.Next(numbers.Count)];
            string repl = HeaderNumbers[rand.Next(HeaderNumbers.Length)];
            what = $"header number '{m.Value}' at {m.Index} replaced by '{repl}'";
            byte[] r = Encoding.ASCII.GetBytes(repl);
            return input.Take(m.Index).Concat(r).Concat(input.Skip(m.Index + m.Length)).ToArray();
        }

        /// <summary>Reads data, returns a failure description or "".</summary>
        static string Check(byte[] data, string extension, bool requireFinite)
        {
            IOReadResult result = default(IOReadResult);
            DMesh3Builder builder = new DMesh3Builder();
            long allocated = 0;
            Exception thrown = null;
            bool finished = Timed.Finishes(() => {
                try {
#if NETCOREAPP
                    long before = GC.GetAllocatedBytesForCurrentThread();
#endif
                    // the format reader itself: StandardMeshReader turns every exception into GenericReaderError,
                    // which would hide crashes (with the invariant culture StandardMeshReader sets)
                    System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                    result = FormatReader(extension).ReadFile(new MemoryStream(data, false), builder, ReadOptions.Defaults, null);
#if NETCOREAPP
                    allocated = GC.GetAllocatedBytesForCurrentThread() - before;
#endif
                } catch (Exception e) {
                    thrown = e;
                }
            }, TimeBudget);
            if (finished == false)
                return $"no result after {TimeBudget.TotalSeconds} s";
            // readers report bad input with these (StandardMeshReader returns them as GenericReaderError); anything else,
            // eg IndexOutOfRange, NullReference, OutOfMemory, is a crash on the input
            if (thrown != null && ParseErrorTypes.Contains(thrown.GetType()) == false)
                return "threw " + thrown.GetType().Name + ": " + thrown.Message + " at " + string.Join(" < ", (thrown.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Contains("g3.")).Take(2));
            // a reader may allocate in proportion to the input, not to sizes the input claims (measured on .NET Core
            // only; on net48 the check is compiled out)
            long budget = 64L * 1024 * 1024 + 4000L * data.Length;
            if (allocated > budget)
                return $"allocated {allocated / (1024 * 1024)} MB for {data.Length} bytes of input ({result.code})";
            if (result.code != IOCode.Ok)
                return "";
            foreach (DMesh3 mesh in builder.Meshes) {
                if (mesh.VertexCount > data.Length || mesh.TriangleCount > data.Length)
                    return $"Ok with {mesh.VertexCount} vertices / {mesh.TriangleCount} triangles from {data.Length} bytes";
                string problem = StructureProblem(mesh);
                if (problem != "")
                    return "Ok with an invalid mesh: " + problem;
                if (requireFinite && mesh.Vertices().All(v => v.IsFinite) == false)
                    return "Ok with non-finite coordinates";
            }
            return "";
        }

        /// <summary>
        /// The invariants of the DMesh3 data structure that later operations rely on. Unlike DMesh3.CheckValidity, it
        /// allows inconsistently oriented neighbors, which DMesh3 stores (and a file may contain).
        /// </summary>
        static string StructureProblem(DMesh3 mesh)
        {
            int nv = mesh.MaxVertexID;
            if (mesh.VerticesBuffer.size != 3 * nv) return "vertex buffer size";
            if (mesh.HasVertexNormals && mesh.NormalsBuffer.size != 3 * nv) return "normal buffer size";
            if (mesh.HasVertexColors && mesh.ColorsBuffer.size != 3 * nv) return "color buffer size";
            if (mesh.HasVertexUVs && mesh.UVBuffer.size != 2 * nv) return "uv buffer size";
            if (mesh.HasTriangleGroups && mesh.GroupsBuffer.size != mesh.MaxTriangleID) return "group buffer size";
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                if (!mesh.IsVertex(tv.a) || !mesh.IsVertex(tv.b) || !mesh.IsVertex(tv.c)) return $"triangle {t}: missing vertex";
                if (tv.a == tv.b || tv.b == tv.c || tv.a == tv.c) return $"triangle {t}: repeated vertex";
                Index3i te = mesh.GetTriEdges(t);
                for (int j = 0; j < 3; ++j) {
                    if (!mesh.IsEdge(te[j])) return $"triangle {t}: missing edge";
                    Index2i ev = mesh.GetEdgeV(te[j]), et = mesh.GetEdgeT(te[j]);
                    if (!IndexUtil.same_pair_unordered(tv[j], tv[(j + 1) % 3], ev.a, ev.b)) return $"triangle {t}: edge {te[j]} has other vertices";
                    if (et.a != t && et.b != t) return $"triangle {t}: edge {te[j]} does not reference it";
                }
            }
            foreach (int e in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(e);
                if (!mesh.IsTriangle(et.a) || (et.b != DMesh3.InvalidID && !mesh.IsTriangle(et.b))) return $"edge {e}: missing triangle";
            }
            return "";
        }

        static void AssertMutatedFilesReadSafely(string extension, bool binary, bool requireFinite)
        {
            byte[] valid = Write(extension, binary);
            Assert.Equal("", Check(valid, extension, requireFinite));
            bool text = binary == false;
            var failures = new List<string>();
            for (int seed = 0; seed < CasesPerFormat; ++seed) {
                // (string.GetHashCode is randomized per process: the seed must be reproducible)
                var rand = new Random(seed * 7919 + extension.Sum(c => (int)c) + (binary ? 1 : 0));
                string what;
                byte[] data = Mutate(valid, text, rand, out what);
                string failure = Check(data, extension, requireFinite);
                if (failure != "") {
                    string name = $"{extension}{(binary ? " binary" : "")} seed {seed}";
                    // G3_MINIMIZE=1: the smallest input with the same failure (Harness/FailureMinimizer.cs; not for hangs,
                    // whose reads keep running)
                    if (FailureMinimizer.Enabled && failure.StartsWith("no result") == false)
                        failure += "\n  " + FailureMinimizer.BytesReport(name, extension, data, failure, d => Check(d, extension, requireFinite), text);
                    failures.Add($"{name} ({what}): {failure}");
                }
                // a read that timed out keeps running (it cannot be cancelled): stop before more of them pile up
                if (failure.StartsWith("no result"))
                    break;
            }
            // (replay a seed on the same target framework: the input comes from our writers, and eg the 3MF deflate
            // output differs between net48 and net10)
            Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void MutatedFiles_ReadSafely(string extension, bool binary) => AssertMutatedFilesReadSafely(extension, binary, false);

        // .g3mesh (#130: the reader used to trust the stored buffers and sizes)
        [Fact]
        public void MutatedFiles_ReadSafely_G3Mesh() => AssertMutatedFilesReadSafely("g3mesh", true, false);

        // a result reported as Ok has finite coordinates (ReadOptions.AllowNonFiniteCoordinates is false by default)
        [Theory]
        [MemberData(nameof(Formats))]
        public void MutatedFiles_OkMeansFiniteCoordinates(string extension, bool binary) => AssertMutatedFilesReadSafely(extension, binary, true);

        // ---- Triangle's .node / .poly / .ele (g3cli TrianglePolyFormat, also used by Meshing2Benchmark): text, read from strings ----

        static string ValidTriangleFile(string kind)
        {
            var input = new ConstrainedMesh2Input();
            double[] xy = { 0, 0, 2, 0, 2, 1, 1, 1, 1, 2, 0, 2, 0.25, 0.25, 0.75, 0.25, 0.75, 0.75, 0.25, 0.75 };
            for (int i = 0; i < xy.Length; i += 2) input.Points.Add(new Vector2d(xy[i], xy[i + 1]));
            input.Heights = input.Points.Select(p => p.x + 2 * p.y).ToList();
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3, 4, 5 }, false, 5));
            for (int i = 6; i < 10; ++i) input.Segments.Add(new Mesh2Segment(i, i == 9 ? 6 : i + 1, 9));
            input.HoleSeeds.Add(new Vector2d(0.5, 0.5));
            input.Regions.Add(Mesh2Region.BySeed(new Vector2d(1.5, 0.5), 3, 0.01));
            switch (kind) {
                case "poly": return g3cli.TrianglePolyFormat.WritePolyOfInput(input);
                case "node": return g3cli.TrianglePolyFormat.WriteNode(input.Points, input.Heights, new[] { (0, 1, 5), (6, 7, 9) });
                default: return g3cli.TrianglePolyFormat.WriteEle(new[] { new Index3i(0, 1, 2), new Index3i(0, 2, 3), new Index3i(3, 4, 5), new Index3i(0, 3, 5) }, new[] { 1, 1, 2, 2 });
            }
        }

        /// <summary>"" if the reader returned within the budget, with a result bounded by the input or a FormatException; else the problem.</summary>
        static string CheckTriangleFile(string kind, string text)
        {
            Exception error = null; int items = 0;
            var task = System.Threading.Tasks.Task.Run(() => {
                try {
                    if (kind == "poly") {
                        var r = g3cli.TrianglePolyFormat.ReadPoly(text, null, true, out _);
                        items = r.Points.Count + r.Segments.Count + r.HoleSeeds.Count + r.Regions.Count + r.Rings.Sum(g => g.Vertices.Length);
                        if (r.Rings.Any(g => g.Vertices.Any(v => v < 0 || v >= r.Points.Count))) error = new Exception("a ring index out of range");
                        if (r.Points.Any(p => double.IsNaN(p.x) || double.IsInfinity(p.x) || double.IsNaN(p.y) || double.IsInfinity(p.y))) error = new Exception("a non-finite point was accepted");
                        if (r.Segments.Any(s => s.A < 0 || s.B < 0 || s.A >= r.Points.Count || s.B >= r.Points.Count)) error = new Exception("a segment index out of range was accepted");
                    } else if (kind == "node") {
                        items = g3cli.TrianglePolyFormat.ReadNode(text, true, out _).Points.Count;
                    } else {
                        var t = g3cli.TrianglePolyFormat.ReadEle(text, 6, 0, out _);
                        items = t.Count;
                        if (t.Any(x => x.a < 0 || x.b < 0 || x.c < 0 || x.a >= 6 || x.b >= 6 || x.c >= 6)) error = new Exception("a corner out of range was accepted");
                    }
                } catch (FormatException) {
                } catch (Exception e) {
                    error = e;
                }
            });
            if (!task.Wait(TimeBudget)) return "no result within " + TimeBudget.TotalSeconds + " s";
            if (error != null) return error.GetType().Name + ": " + error.Message;
            // the result is bounded by the input (rings repeat point indices: at most the segments' end points)
            // (each item takes at least two characters: a number and a separator; rings repeat segment end points)
            if (items > text.Length + 16) return items + " items from " + text.Length + " bytes";
            return "";
        }

        [Theory]
        [InlineData("poly")]
        [InlineData("node")]
        [InlineData("ele")]
        public void MutatedTriangleFiles_ReadSafely(string kind)
        {
            string valid = ValidTriangleFile(kind);
            Assert.Equal("", CheckTriangleFile(kind, valid));
            if (kind == "poly") {
                // the valid file is meaningful: its segments give the L's ring (the hole loop gives a second)
                g3cli.TrianglePolyFormat.ReadPoly(valid, null, false, out var info);
                Assert.Equal(2, info.Loops);
            }
            var failures = new List<string>();
            for (int seed = 0; seed < CasesPerFormat; ++seed) {
                var rand = new Random(seed * 7919 + kind.Sum(c => (int)c) + 17);
                byte[] data = Mutate(Encoding.ASCII.GetBytes(valid), true, rand, out string what);
                string failure = CheckTriangleFile(kind, Encoding.ASCII.GetString(data));
                if (failure != "") failures.Add($"{kind} seed {seed} ({what}): {failure}");
                if (failure.StartsWith("no result")) break;
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
        }

        // ---- height grids (HeightGridReader: XYZ and ESRI ASCII), text read from strings ----

        static string ValidHeightGridFile(string kind)
        {
            var g = new HeightGrid(new double[12 * 9], 12, 9, 350000.5, 5600000.25, 0.5);
            for (int j = 0; j < g.NY; ++j)
                for (int i = 0; i < g.NX; ++i)
                    g[i, j] = 100 + 0.25 * i - 0.5 * j + (i * 7 + j * 3) % 5;
            g[4, 4] = double.NaN;
            var sw = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            if (kind == "xyz") HeightGridWriter.WriteXyz(sw, g); else HeightGridWriter.WriteEsriAscii(sw, g);
            return sw.ToString();
        }

        /// <summary>"" if the reader returned within the budget without throwing, and an Ok result is a valid grid bounded by the input; else the problem.</summary>
        static string CheckHeightGridFile(string kind, string text)
        {
            HeightGridReadResult r = default(HeightGridReadResult);
            long allocated = 0;
            Exception thrown = null;
            bool finished = Timed.Finishes(() => {
                try {
#if NETCOREAPP
                    long before = GC.GetAllocatedBytesForCurrentThread();
#endif
                    var reader = new StringReader(text);
                    r = kind == "xyz" ? HeightGridReader.ReadXyz(reader) : HeightGridReader.ReadEsriAscii(reader);
#if NETCOREAPP
                    allocated = GC.GetAllocatedBytesForCurrentThread() - before;
#endif
                } catch (Exception e) {
                    thrown = e;
                }
            }, TimeBudget);
            if (!finished) return "no result within " + TimeBudget.TotalSeconds + " s";
            // the readers report bad input by status: any exception is a crash on the input
            if (thrown != null) return "threw " + thrown.GetType().Name + ": " + thrown.Message;
            if (allocated > 64L * 1024 * 1024 + 4000L * text.Length)
                return $"allocated {allocated / (1024 * 1024)} MB for {text.Length} characters ({r.Status})";
            if (r.Status == HeightGridStatus.Ok) {
                HeightGrid g = r.Grid;
                if (g == null || g.Z == null || g.Z.Length != (long)g.NX * g.NY || g.NX < 1 || g.NY < 1) return "Ok with an inconsistent grid";
                if (!(g.CellSize > 0) || double.IsInfinity(g.CellSize) || double.IsNaN(g.X0) || double.IsInfinity(g.X0) || double.IsNaN(g.Y0) || double.IsInfinity(g.Y0)) return "Ok with a non-finite origin or cell size";
                if (g.Z.Any(double.IsInfinity)) return "Ok with an infinite height";
                if (g.Z.Length > Math.Max(1024, 64L * text.Length)) return $"Ok with {g.Z.Length} nodes from {text.Length} characters";
            } else if (r.Grid != null || r.Message == null) {
                return "a failure " + r.Status + " with a grid or without a message";
            }
            return "";
        }

        [Theory]
        [InlineData("xyz")]
        [InlineData("esri")]
        public void MutatedHeightGridFiles_ReadSafely(string kind)
        {
            string valid = ValidHeightGridFile(kind);
            Assert.Equal("", CheckHeightGridFile(kind, valid));
            var failures = new List<string>();
            for (int seed = 0; seed < CasesPerFormat; ++seed) {
                var rand = new Random(seed * 7919 + kind.Sum(c => (int)c) + 29);
                byte[] data = Mutate(Encoding.ASCII.GetBytes(valid), true, rand, out string what);
                string failure = CheckHeightGridFile(kind, Encoding.ASCII.GetString(data));
                if (failure != "") failures.Add($"{kind} seed {seed} ({what}): {failure}");
                if (failure.StartsWith("no result")) break;
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
        }
    }
}
