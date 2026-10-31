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
  ci                                selfcheck, differential of the CI subset, golden check: what CI runs
  differential [--larger]           M1 against NetTopologySuite; --larger adds the bigger cases
  golden check [--milestone M1]     recompute the golden rows and compare
  golden update --milestone M1 --reasons <file>
                                    rewrite the rows; <file> has one line per changed case: <case><TAB><reason>
  golden verify-changes --base <old.json> [--current <new.json>]
                                    every changed row states a new reason (the gate on a pull request)
  oracle --milestone M1|M2|M3 --input <in.txt> --output <out.txt> [--tube 0|auto|<value>] [--case <id>] [--row <file>]
                                    judge any triangulation in the plain text format; a standalone check
  case --case <id> --output <file>  write one case in the plain text input format
  cases                             list the cases with their generators and input hashes
  compare [--rows <file>]           our rows against rows of another producer, read from <file> or G3_MESH2_REFERENCE_ROWS;
                                    skipped with a message if there are none; not part of CI
  catalogue                         print the failure catalogue table
  format-tables                     print the generated parts of docs/design/MESHING2D_GOLDEN_FORMAT.md
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
                        int a = RunDifferential(false), b = GoldenCheck(Milestone.M1);
                        return a != 0 || b != 0 ? 1 : 0;
                    }
                    case "differential": return RunDifferential(Has(args, "--larger"));
                    case "golden": return Golden(args);
                    case "oracle": return OracleCommand(args);
                    case "case": return WriteCase(args);
                    case "cases": return ListCases();
                    case "compare": return Compare(args);
                    case "catalogue": Console.Write(Catalogue.Markdown()); return 0;
                    case "format-tables": PrintFormatTables(); return 0;
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

        static string RepositoryFile(string relative)
        {
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "geometry3Sharp_netstandard.csproj"))) return Path.Combine(dir.FullName, relative);
            throw new InvalidOperationException("run this inside the repository");
        }

        static string GoldenPath(Milestone m) => RepositoryFile(Path.Combine("benchmarks", "Meshing2Benchmark", "golden", m.ToString().ToLowerInvariant() + ".json"));

        static GoldenFile LoadGolden(Milestone m)
        {
            string path = GoldenPath(m);
            return File.Exists(path) ? GoldenFile.Parse(File.ReadAllText(path)) : new GoldenFile();
        }

        static IEnumerable<Mesh2Case> GoldenCases(Milestone m) => m == Milestone.M1 ? M1Cases.Ci() : Catalogue.Rows().Where(r => r.Case != null && r.Case.Milestone == m && r.State == CaseState.Covered).Select(r => r.Case);

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

        static int GoldenCheck(Milestone m)
        {
            var candidate = CandidateRegistry.For(m);
            if (candidate == null) { Console.WriteLine("golden " + m + ": no candidate yet, nothing to compare"); return 0; }
            var file = LoadGolden(m);
            var problems = GoldenProcess.Check(file, GoldenCases(m), candidate);
            foreach (var p in problems) Console.WriteLine("GOLDEN " + p);
            Console.WriteLine("golden " + m + ": " + file.Rows.Count + " rows, " + problems.Count + " differences");
            return problems.Count == 0 ? 0 : 1;
        }

        static int Golden(string[] args)
        {
            string verb = args.Length > 1 ? args[1] : "check";
            if (verb == "check") return GoldenCheck(MilestoneArg(args, Milestone.M1));
            if (verb == "verify-changes") {
                var before = GoldenFile.Parse(File.ReadAllText(Arg(args, "--base")));
                string current = Arg(args, "--current") ?? GoldenPath(MilestoneArg(args, Milestone.M1));
                var problems = GoldenProcess.VerifyChanges(before, GoldenFile.Parse(File.ReadAllText(current)));
                foreach (var p in problems) Console.WriteLine("GOLDEN " + p);
                Console.WriteLine("golden changes: " + problems.Count + " without a reason");
                return problems.Count == 0 ? 0 : 1;
            }
            if (verb == "update") {
                var m = MilestoneArg(args, Milestone.M1);
                var candidate = CandidateRegistry.For(m);
                if (candidate == null) { Console.Error.WriteLine("no candidate registered for " + m + ": nothing can be recorded"); return 1; }
                string reasonsPath = Arg(args, "--reasons");
                var reasons = new Dictionary<string, string>();
                if (reasonsPath != null)
                    foreach (var line in File.ReadAllLines(reasonsPath)) {
                        int tab = line.IndexOf('\t');
                        if (tab > 0) reasons[line.Substring(0, tab)] = line.Substring(tab + 1).Trim();
                    }
                var reference = new NtsReference();
                Func<Mesh2Case, Mesh2Output, OracleReport, string> differential = null;
                if (m == Milestone.M1)
                    differential = (c, o, r) => {
                        var result = Differential.Compare(c, candidate, reference);
                        return result.IsFailure ? result.ToString() : null;
                    };
                var file = LoadGolden(m);
                var problems = GoldenProcess.Update(file, GoldenCases(m), candidate, differential, reasons);
                foreach (var p in problems) Console.WriteLine("REFUSED " + p);
                if (problems.Count > 0) { Console.WriteLine("nothing written: " + problems.Count + " problems"); return 1; }
                Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath(m)));
                File.WriteAllText(GoldenPath(m), file.Write());
                Console.WriteLine("wrote " + GoldenPath(m) + ": " + file.Rows.Count + " rows");
                return 0;
            }
            Console.Error.WriteLine(Usage);
            return 2;
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
                + ", twice the area " + report.Area2 + ", min_angle_sin2 " + (report.MinSin2?.ToDecimal(GoldenRow.MinAngleDigits) ?? "-"));
            string rowPath = Arg(args, "--row");
            if (rowPath != null) {
                var c = new Mesh2Case { Id = Arg(args, "--case") ?? Path.GetFileNameWithoutExtension(inputPath), Milestone = e.Milestone, Input = input, Generator = Arg(args, "--generator") ?? "text:" + Path.GetFileName(inputPath), Expect = e };
                File.WriteAllText(rowPath, MiniJson.Write(GoldenRow.From(c, producer, output, report, report.Ok ? "" : report.ToString()).ToJson()));
            }
            return report.Ok ? 0 : 1;
        }

        static IEnumerable<Mesh2Case> AllCases() => M1Cases.Ci().Concat(M1Cases.Larger()).Concat(Catalogue.Rows().Where(r => r.Case != null).Select(r => r.Case)).GroupBy(c => c.Id).Select(g => g.First());

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

        static void PrintFormatTables()
        {
            Console.WriteLine("@@input");
            var example = Generators.Literal(0, 0, 1, 0, 0, 1);
            Console.Write(TextFormat.WriteInput(example));
            Console.WriteLine("@@inputhash");
            Console.WriteLine(TextFormat.InputSha256(example));
            Console.WriteLine("@@square1");
            var a = new Mesh2Output { VX = new double[] { 0, 1, 1, 0 }, VY = new double[] { 0, 0, 1, 1 }, Triangles = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, Subsegments = new List<SubsegmentOutput> { new SubsegmentOutput(0, 1), new SubsegmentOutput(1, 2) } };
            Console.Write(Fingerprint.TriangleLines(a));
            Console.WriteLine("@@square1hash");
            Console.WriteLine(Fingerprint.Triangles(a));
            Console.WriteLine("@@square2");
            var b = new Mesh2Output { VX = a.VX, VY = a.VY, Triangles = new[] { new[] { 0, 1, 3 }, new[] { 1, 2, 3 } } };
            Console.Write(Fingerprint.TriangleLines(b));
            Console.WriteLine("@@square2hash");
            Console.WriteLine(Fingerprint.Triangles(b));
            Console.WriteLine("@@segments");
            Console.Write(Fingerprint.SubsegmentLines(a));
            Console.WriteLine("@@segmentshash");
            Console.WriteLine(Fingerprint.Subsegments(a));
            Console.WriteLine("@@vertices");
            Console.Write(Fingerprint.VertexLines(a));
            Console.WriteLine("@@verticeshash");
            Console.WriteLine(Fingerprint.Vertices(a));
            Console.WriteLine("@@random");
            var r = new SplitMix64(1);
            Console.WriteLine(r.Next().ToString("x16") + " " + r.Next().ToString("x16"));
            Console.WriteLine("@@cases");
            foreach (var c in Catalogue.ReferenceCases())
                Console.WriteLine("| `" + c.Id + "` | " + c.Milestone + " | `" + c.Generator.Replace("|", "\\|") + "` | `" + TextFormat.InputSha256(c.Input) + "` |");
        }

        /// <summary>Not a gate: rows of another producer live outside the repository until the owner decides on publishing them.</summary>
        static int Compare(string[] args)
        {
            string path = Arg(args, "--rows") ?? Environment.GetEnvironmentVariable("G3_MESH2_REFERENCE_ROWS");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                Console.WriteLine("SKIPPED: no reference rows (--rows <file> or G3_MESH2_REFERENCE_ROWS). Comparison with another producer is optional and not part of CI.");
                return 0;
            }
            var theirs = GoldenFile.Parse(File.ReadAllText(path));
            var ours = Enum.GetValues(typeof(Milestone)).Cast<Milestone>().SelectMany(m => LoadGolden(m).Rows).ToDictionary(r => r.Case);
            string reasonsPath = Arg(args, "--reasons");
            var reasons = new Dictionary<string, string>();
            if (reasonsPath != null)
                foreach (var line in File.ReadAllLines(reasonsPath)) {
                    int tab = line.IndexOf('\t');
                    if (tab > 0) reasons[line.Substring(0, tab)] = line.Substring(tab + 1).Trim();
                }
            int same = 0, explained = 0, open = 0, notOurs = 0;
            foreach (var row in theirs.Rows) {
                if (!ours.TryGetValue(row.Case, out var mine)) { ++notOurs; Console.WriteLine("NO ROW OF OURS " + row.Case); continue; }
                var differences = RowComparison.Compare(mine, row);
                if (differences.Count == 0) { ++same; continue; }
                if (reasons.TryGetValue(row.Case, out string reason)) { ++explained; Console.WriteLine("EXPLAINED " + row.Case + ": " + reason); continue; }
                ++open;
                Console.WriteLine("DIFFERS " + row.Case + " (" + mine.Producer + " and " + row.Producer + "): " + string.Join("; ", differences));
            }
            Console.WriteLine("compared with " + path + ": " + same + " agree, " + explained + " differ with a reason, " + open + " differ without a reason, " + notOurs + " have no row of ours");
            Console.WriteLine("A reference is not truth. A difference needs a reason, and either side may be at fault: run the oracle on both outputs.");
            return open == 0 ? 0 : 1;
        }
    }
}
