using System;
using System.Collections.Generic;
using System.Linq;
using ReaderFuzz;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The coverage-guided fuzz targets of tools/ReaderFuzzPilot (SharpFuzz + libFuzzer, campaigns on a runner) run in CI
    /// without a campaign: for every reader its procedurally written seeds, and the inputs that follow from them
    /// deterministically (every truncation, replaced bytes at every position, extreme counts in aligned words, and for text
    /// each number replaced by extreme tokens), through the checks the campaigns use: only the documented parse errors,
    /// no caught-and-hidden exception, a mesh that is Ok has valid structure, finite coordinates and a size bounded by the
    /// input, bounded allocation. A finding of a campaign is replayed with `ReaderFuzzPilot replay <target> <file>`, fixed,
    /// and added as an example test (no corpus files are committed). The blind seeded mutations are in ReaderFuzzTests.
    /// </summary>
    public class ReaderFuzzReplayTests
    {
        public static IEnumerable<object[]> TargetNames()
        {
            return ReaderTargets.All.Select(t => new object[] { t.Name });
        }

        [Theory]
        [MemberData(nameof(TargetNames))]
        public void SeedsAndTheirSystematicMutations_AreReadSafely(string name)
        {
            FuzzTarget target = ReaderTargets.Find(name);
            List<byte[]> seeds = target.MakeSeeds();
            Assert.NotEmpty(seeds);
            var failures = new List<string>();
            int count = 0;
            bool finished = Timed.Finishes(() => {
                foreach (byte[] seed in seeds) {
                    string seedProblem = target.Check(seed);
                    if (seedProblem != "") { failures.Add("seed: " + seedProblem); continue; }
                    foreach (byte[] input in ReplayMutations.All(seed, target.Text)) {
                        ++count;
                        string problem = target.Check(input);
                        if (problem != "" && failures.Count < 20)
                            failures.Add($"{input.Length} bytes, first difference at {FirstDifference(seed, input)}: {problem}");
                    }
                }
            }, TimeSpan.FromSeconds(60));
            Assert.True(finished, $"{name}: no result within 60 s after {count} inputs");
            Assert.True(failures.Count == 0, name + ":\n" + string.Join("\n", failures));
            Assert.True(count > 200, $"{name}: only {count} inputs");
        }

        static int FirstDifference(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) ++i;
            return i;
        }

        [Fact]
        public void EveryReaderHasATarget()
        {
            string[] expected = { "stl", "obj", "off", "ply", "3mf", "glb", "g3mesh", "minijson", "esri", "xyz", "poly", "node", "ele" };
            foreach (string name in expected) Assert.NotNull(ReaderTargets.Find(name));
        }

        [Fact]
        public void TheChecksFlagWhatTheyAreMeantToFlag()
        {
            // a reader exception outside the documented parse errors, and a hidden one, are findings; a parse error is not
            Assert.NotEqual("", ReaderTargets.CheckMeshStream("stl", new ThrowingStream(new InvalidOperationException("synthetic")), 16));
            Assert.Equal("", ReaderTargets.CheckMeshStream("stl", new ThrowingStream(new System.IO.InvalidDataException("bad")), 16));
            Assert.NotEqual("", ReaderTargets.CaughtExceptionProblem(new g3.IOReadResult(g3.IOCode.GenericReaderError, "exception: x")));
            Assert.Equal("", ReaderTargets.CaughtExceptionProblem(new g3.IOReadResult(g3.IOCode.GenericReaderError, "bad input")));
            // the structural check sees a broken mesh
            g3.DMesh3 mesh = ReaderTargets.SeedMesh();
            Assert.Equal("", ReaderTargets.StructureProblem(mesh));
            mesh.VerticesBuffer.Add(1.0);
            Assert.NotEqual("", ReaderTargets.StructureProblem(mesh));
        }

        sealed class ThrowingStream : System.IO.MemoryStream
        {
            readonly Exception e;
            public ThrowingStream(Exception e) { this.e = e; }
            public override int Read(byte[] buffer, int offset, int count) { throw e; }
            public override int ReadByte() { throw e; }
        }
    }
}
