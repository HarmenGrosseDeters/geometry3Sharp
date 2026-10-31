using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Meshing2Acceptance
{
    /// <summary>
    /// The acceptance gates of the 2D mesher. Every command runs the oracle's self-check first and stops if it fails. See README.md.
    /// Exit codes: 0 passed, 1 a gate failed, 2 wrong usage, 3 the oracle's self-check failed.
    /// </summary>
    internal static class Program
    {
        const string Usage = @"Meshing2Benchmark <command>
  selfcheck                         the oracle rejects broken results (runs before every command)
  ci                                selfcheck, differential of the CI subset: what CI runs
  differential [--larger]           M1 against NetTopologySuite; --larger adds the bigger cases
  oracle --milestone M1|M2|M3 --input <in.txt> --output <out.txt> [--tube 0|auto|<value>]
                                    judge any triangulation in the plain text format; a standalone check
  case --case <id> --output <file>  write one case in the plain text input format
  cases                             list the cases with their generators and input hashes
";

        static int Main(string[] args)
        {
            if (args.Length == 0) { Console.Error.WriteLine(Usage); return 2; }
            var items = SelfCheck.Run();
            var failed = items.Where(i => !i.Passed).ToList();
            foreach (var item in failed) Console.Error.WriteLine("SELF-CHECK FAILED: " + item.Name + ": " + item.Detail);
            if (failed.Count > 0) return 3;
            Console.WriteLine("oracle self-check: " + items.Count + " checks passed");
            try {
                switch (args[0]) {
                    case "selfcheck": return 0;
                    case "ci": {
                        return RunDifferential(false);
                    }
                    case "differential": return RunDifferential(Has(args, "--larger"));
                    case "oracle": return OracleCommand(args);
                    case "case": return WriteCase(args);
                    case "cases": return ListCases();
                    default: Console.Error.WriteLine(Usage); return 2;
                }
            } catch (FormatException e) {
                Console.Error.WriteLine("bad input: " + e.Message);
                return 2;
            }
        }

        static bool Has(string[] args, string name) => args.Contains(name);
        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static Milestone MilestoneArg(string[] args, Milestone fallback)
        {
            string text = Arg(args, "--milestone");
            return text == null ? fallback : (Milestone)Enum.Parse(typeof(Milestone), text);
        }

        static int RunDifferential(bool larger)
        {
            var cases = M1Cases.Ci().Concat(larger ? M1Cases.Larger() : new Mesh2Case[0]);
            var candidate = CandidateRegistry.For(Milestone.M1);
            var reference = new NtsReference();
            var tally = new Dictionary<DiffOutcome, int>();
            int problems = 0;
            foreach (var c in cases) {
                var result = Differential.Compare(c, candidate, reference);
                tally.TryGetValue(result.Outcome, out int n); tally[result.Outcome] = n + 1;
                if (result.Outcome != DiffOutcome.SameTriangles && result.Outcome != DiffOutcome.BothValid) Console.WriteLine(result);
                if (result.IsFailure) ++problems;
            }
            Console.WriteLine("differential " + candidate.Name + " against " + reference.Name + ": " +
                string.Join(", ", tally.OrderBy(p => p.Key).Select(p => p.Key + " " + p.Value)));
            return problems == 0 ? 0 : 1;
        }

        static int OracleCommand(string[] args)
        {
            string inputPath = Arg(args, "--input"), outputPath = Arg(args, "--output");
            if (inputPath == null || outputPath == null) { Console.Error.WriteLine(Usage); return 2; }
            var input = TextFormat.ReadInput(File.ReadAllText(inputPath));
            var output = TextFormat.ReadOutput(File.ReadAllText(outputPath), out string producer);
            var e = new OracleExpect { Milestone = MilestoneArg(args, Milestone.M1) };
            string tube = Arg(args, "--tube");
            if (tube != null && tube != "auto") e.Tube = Q.FromDouble(TextFormat.ParseCoordinate(tube));
            var report = Oracle.Check(input, output, e);
            foreach (var v in report.Violations) Console.WriteLine("VIOLATION " + v);
            Console.WriteLine((report.Ok ? "VALID" : "INVALID") + " " + producer + ": " + output.Triangles.Length + " triangles, unique " + report.Unique.ToString().ToLowerInvariant()
                + ", twice the area " + report.Area2 + ", min_angle_sin2 " + (report.MinSin2?.ToDecimal(12) ?? "-"));
            return report.Ok ? 0 : 1;
        }

        static IEnumerable<Mesh2Case> AllCases() => M1Cases.Ci().Concat(M1Cases.Larger());

        static int WriteCase(string[] args)
        {
            string id = Arg(args, "--case"), path = Arg(args, "--output");
            var c = AllCases().FirstOrDefault(x => x.Id == id);
            if (c == null || path == null) { Console.Error.WriteLine("unknown case or no --output: " + id); return 2; }
            File.WriteAllText(path, TextFormat.WriteInput(c.Input));
            return 0;
        }

        static int ListCases()
        {
            foreach (var c in AllCases()) Console.WriteLine(c.Id + "\t" + c.Milestone + "\t" + TextFormat.InputSha256(c.Input) + "\t" + c.Generator);
            return 0;
        }

    }
}
