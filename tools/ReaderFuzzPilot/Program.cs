using System;
using System.IO;
using System.Linq;
using ReaderFuzz;
using SharpFuzz;

namespace ReaderFuzzPilot;

/// <summary>
/// Coverage-guided fuzzing of the readers under SharpFuzz and libFuzzer (README.md). The target is chosen by the environment
/// variable READER_FUZZ_TARGET (default stl); the commands below prepare seeds and replay findings.
/// </summary>
public static class Program
{
    sealed class UnexpectedFailureStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("synthetic unexpected failure");
        public override int ReadByte() => throw new InvalidOperationException("synthetic unexpected failure");
    }

    static FuzzTarget Target(string name)
    {
        ReaderTargets.Add(McpTarget.Target);
        var t = ReaderTargets.Find(name);
        if (t == null) {
            Console.Error.WriteLine("unknown target " + name + "; targets: " + string.Join(" ", ReaderTargets.All.Select(x => x.Name)));
            Environment.Exit(2);
        }
        return t;
    }

    // a seed must be accepted by its reader (a meaningless seed makes a campaign look busy and find nothing)
    static string VerifySeed(FuzzTarget target, byte[] data)
    {
        string problem = target.Check(data);
        if (problem != "") return problem;
        if (ReaderTargets.IsMeshFormat(target.Name)) {
            var builder = new g3.DMesh3Builder();
            var result = ReaderTargets.FormatReader(target.Name).ReadFile(new MemoryStream(data, false), builder, g3.ReadOptions.Defaults, null);
            if (result.code != g3.IOCode.Ok || builder.Meshes.Count < 1 || builder.Meshes[0].TriangleCount < 1) return "seed was not read as a mesh: " + result.code + " " + result.message;
        }
        return "";
    }

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "targets") {
            ReaderTargets.Add(McpTarget.Target);
            foreach (var t in ReaderTargets.All) Console.WriteLine(t.Name);
            return 0;
        }
        if (args.Length == 1 && args[0] == "policy-self-test") {
            // a caught reader exception and an unexpected exception type must both be findings
            if (ReaderTargets.CaughtExceptionProblem(new g3.IOReadResult(g3.IOCode.GenericReaderError, "bad input")) != "") throw new InvalidOperationException("a plain parse error is not a finding");
            if (ReaderTargets.CaughtExceptionProblem(new g3.IOReadResult(g3.IOCode.GenericReaderError, "exception: synthetic failure")) == "") throw new InvalidOperationException("Caught reader exception was silently accepted");
            if (ReaderTargets.CheckMeshStream("stl", new UnexpectedFailureStream(), 0) == "") throw new InvalidOperationException("Unexpected reader exception was silently accepted");
            return 0;
        }
        if (args.Length == 1 && args[0] == "check-seeds") {
            int bad = 0;
            ReaderTargets.Add(McpTarget.Target);
            foreach (var t in ReaderTargets.All)
                foreach (var seed in t.MakeSeeds()) {
                    string p = VerifySeed(t, seed);
                    if (p != "") { Console.Error.WriteLine(t.Name + ": " + p); ++bad; }
                }
            return bad == 0 ? 0 : 1;
        }
        if (args.Length == 3 && args[0] == "seeds") {
            var target = Target(args[1]);
            Directory.CreateDirectory(args[2]);
            int i = 0;
            foreach (var seed in target.MakeSeeds()) {
                string problem = VerifySeed(target, seed);
                if (problem != "") { Console.Error.WriteLine("Invalid seed: " + problem); return 1; }
                string path = Path.Combine(args[2], target.Name + "-seed" + i++);
                using (var f = new FileStream(path, FileMode.CreateNew)) f.Write(seed, 0, seed.Length);
                Console.WriteLine(path);
            }
            return 0;
        }
        if (args.Length >= 2 && args[0] == "verify-seed") {
            var target = Target(args.Length == 3 ? args[1] : "stl");
            string problem = VerifySeed(target, File.ReadAllBytes(args[args.Length - 1]));
            if (problem != "") Console.Error.WriteLine("Invalid seed: " + problem);
            return problem == "" ? 0 : 1;
        }
        if (args.Length >= 2 && args[0] == "replay") {
            var target = Target(args.Length == 3 ? args[1] : "stl");
            target.Run(File.ReadAllBytes(args[args.Length - 1]));
            return 0;
        }
        if (args.Length != 0) {
            Console.Error.WriteLine("usage: ReaderFuzzPilot [targets | policy-self-test | check-seeds | seeds <target> <new-dir> | verify-seed [target] <input> | replay [target] <input>]\n"
                + "       READER_FUZZ_TARGET=<target> ReaderFuzzPilot   (as the libFuzzer target)");
            return 2;
        }
        var fuzzTarget = Target(Environment.GetEnvironmentVariable("READER_FUZZ_TARGET") ?? "stl");
        Fuzzer.LibFuzzer.Run(data => fuzzTarget.Run(data.ToArray()));
        return 0;
    }
}
