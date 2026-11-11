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
            byte[] currentSeed = null, currentInput = null; // (the input being checked, for the message of a stall)
            // (a watchdog for a hang: one input that does not end within 30 s; the whole replay is only as slow as the machine is loaded,
            // which the net48 job under load showed with 60 s as a total budget)
            bool finished = Timed.MakesProgress(beat => {
                foreach (byte[] seed in seeds) {
                    string seedProblem = target.Check(seed);
                    beat();
                    if (seedProblem != "") { failures.Add("seed: " + seedProblem); continue; }
                    foreach (byte[] input in ReplayMutations.All(seed, target.Text)) {
                        ++count;
                        currentSeed = seed; currentInput = input;
                        string problem = target.Check(input);
                        beat();
                        if (problem != "" && failures.Count < 20)
                            failures.Add($"{input.Length} bytes, first difference at {FirstDifference(seed, input)}: {problem}");
                    }
                }
            }, TimeSpan.FromSeconds(30));
            string stuck = currentInput == null ? "" : $" ({currentInput.Length} bytes, first difference from its seed at {FirstDifference(currentSeed, currentInput)}, seed of {currentSeed.Length} bytes)";
            Assert.True(finished, $"{name}: no progress for 30 s, stuck on input {count}{stuck}");
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
