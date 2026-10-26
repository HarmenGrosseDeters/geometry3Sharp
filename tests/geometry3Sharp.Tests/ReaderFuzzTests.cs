using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
            "4294967296", "99999999999999999999", "-1", "0", "1e-320", "0x10", "", "1e", "--1"
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
            Task task = Task.Run(() => {
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
            });
            if (task.Wait(TimeBudget) == false)
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
                if (failure != "")
                    failures.Add($"{extension}{(binary ? " binary" : "")} seed {seed} ({what}): {failure}");
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
    }
}
