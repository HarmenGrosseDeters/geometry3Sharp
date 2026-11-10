using System;
using System.Collections.Generic;
using ReaderFuzz;
using Xunit;

namespace g3cli.Tests
{
    /// <summary>
    /// The MCP request parser (McpServer.Handle) as a coverage-guided fuzz target (tools/ReaderFuzzPilot), replayed without a
    /// campaign: the seeds and their systematic mutations (truncations, replaced bytes, extreme numbers) must never throw; a
    /// malformed message is answered with a JSON-RPC error. Tool calls are not passed on (they read and write files).
    /// </summary>
    public class McpFuzzReplayTests
    {
        [Fact]
        public void SeedsAndTheirSystematicMutations_AreAnsweredWithoutAnException()
        {
            FuzzTarget target = McpTarget.Target;
            var failures = new List<string>();
            int count = 0;
            foreach (byte[] seed in target.MakeSeeds()) {
                Assert.Equal("", target.Check(seed));
                foreach (byte[] input in ReplayMutations.All(seed, true)) {
                    ++count;
                    string problem = target.Check(input);
                    if (problem != "" && failures.Count < 20) failures.Add(System.Text.Encoding.UTF8.GetString(input) + " -> " + problem);
                }
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
            Assert.True(count > 200, "only " + count + " inputs");
        }
    }
}
