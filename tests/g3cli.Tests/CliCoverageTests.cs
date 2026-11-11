using System;
using System.Globalization;
using System.IO;
using Xunit;
using g3;
using gs;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary>
    /// Error paths of the cell_volumes, dgm and dgm_tin commands that no other test reaches: a lattice whose surfaces are outside the supported
    /// range (no overlap pairs to report), unreadable breakline and unwritable grid files, and malformed GeoJSON.
    /// </summary>
    public class CliCoverageTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public CliCoverageTests(TestMeshes meshes) { m = meshes; }

        static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        string GridFile(string name, int n = 9)
        {
            string path = m.Path_(name);
            var g = new HeightGrid(new double[n * n], n, n, 0, 0, 1.0);
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) g[i, j] = 0.5 * Math.Sin(i * 0.4) + 0.1 * j;
            using (var w = new StreamWriter(path)) HeightGridWriter.WriteXyz(w, g);
            return path;
        }

        // ---------------------------------------------------------------- cell_volumes

        [Fact]
        public void CellVolumes_SurfaceBelowTheSupportedExtent_FailsWithoutOverlapPairs()
        {
            // a triangle of extent 2^-9 (< 2^-8, TerrainInputRange): Compute() is false although the surface is a height field
            string tiny = m.Path_("cvc_tiny.obj");
            File.WriteAllText(tiny, "v 0 0 0\nv 0.001953125 0 0\nv 0 0.001953125 0\nf 1 2 3\n");
            var (exit, r) = Run("cell_volumes", tiny, tiny, "--cell-size", "1");
            Assert.Equal(1, exit);
            Assert.Equal(ResultCodes.OperationFailed, S(r["code"]));
            Assert.Contains("the volumes could not be computed", S(r["message"]));
            Assert.Null(r["before_overlaps"]);          // not the overlap message with its counters
            Assert.DoesNotContain("height field", S(r["message"]));
        }

        // ---------------------------------------------------------------- dgm_tin breaklines

        [Fact]
        public void DgmTin_BreaklinesFileThatCannotBeRead_IsAReadError()
        {
            string input = GridFile("cvc_in.xyz"), output = m.Path_("cvc_out.g3mesh");
            string lines = m.Path_("cvc_locked.geojson");
            File.WriteAllText(lines, "{\"type\": \"LineString\", \"coordinates\": [[1, 1], [6, 6]]}");
            // sanity: unlocked, the same file is accepted
            Assert.Equal(0, Run("dgm_tin", input, output, "--epsilon", "0.2", "--breaklines", lines).exit);
            // held open exclusively (.NET enforces FileShare on Unix with advisory locks), ReadAllText throws an IOException
            using (new FileStream(lines, FileMode.Open, FileAccess.Read, FileShare.None)) {
                var (exit, r) = Run("dgm_tin", input, m.Path_("cvc_out2.g3mesh"), "--epsilon", "0.2", "--breaklines", lines);
                Assert.Equal(ResultCodes.ExitCode(ResultCodes.ReadError), exit);
                Assert.Equal(ResultCodes.ReadError, S(r["code"]));
                Assert.Contains("cannot read '" + lines + "'", S(r["message"]));
            }
        }

        [Theory]
        [InlineData("{\"type\": \"FeatureCollection\"}", "needs a \\\"features\\\" array")]
        [InlineData("{\"type\": \"FeatureCollection\", \"features\": {}}", "needs a \\\"features\\\" array")]
        [InlineData("{\"type\": \"LineString\", \"coordinates\": [[1, 1]]}", "at least 2 positions")]
        [InlineData("{\"type\": \"LineString\", \"coordinates\": [[1, 1], [\"a\", 2]]}", "not a finite number")]
        [InlineData("{\"type\": \"LineString\", \"coordinates\": [[1, 1], [2, null]]}", "not a finite number")]
        public void DgmTin_MalformedBreaklines_AreInvalidArguments(string json, string expectedPart)
        {
            string input = GridFile("cvc_in2.xyz"), output = m.Path_("cvc_bad.g3mesh");
            var (exit, r) = Run("dgm_tin", input, output, "--epsilon", "0.2", "--breaklines-geojson", json);
            Assert.Equal(ResultCodes.ExitCode(ResultCodes.InvalidArguments), exit);
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));
            Assert.Contains(expectedPart.Replace("\\\"", "\""), S(r["message"]));
            Assert.False(File.Exists(output));
        }

        // ---------------------------------------------------------------- dgm

        [Fact]
        public void Dgm_OutputThatCannotBeWritten_IsAWriteError()
        {
            string input = GridFile("cvc_in3.xyz");
            // an existing directory as the output file: the parent exists, the write fails with an UnauthorizedAccessException
            string dir = m.Path_("cvc_outdir.xyz");
            Directory.CreateDirectory(dir);
            var (exit, r) = Run("dgm", input, dir);
            Assert.Equal(ResultCodes.ExitCode(ResultCodes.WriteError), exit);
            Assert.Equal(ResultCodes.WriteError, S(r["code"]));
            Assert.Contains("cannot write '" + dir + "'", S(r["message"]));
            Assert.Null(r["output_format"]);
            Assert.True(Directory.Exists(dir));
            // the same command to a regular path succeeds
            string ok = m.Path_("cvc_ok.xyz");
            Assert.Equal(0, Run("dgm", input, ok).exit);
            Assert.True(File.Exists(ok));
        }
    }
}
