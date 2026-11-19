using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using g3;

namespace ReferenceParts
{
    /// <summary>
    /// The sweep of the reference parts (README.md): every part, defect set, resolution or size ratio and chain, with the first failing step
    /// and the measured values per check. Usage: dotnet run --project benchmarks/ReferenceParts -c Release -- [--verbose] [--csv file] [--part flange|rib|screw] [--defects welded|soup|faulty|faulty+gap1e-5]
    /// </summary>
    static class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string F(double v) { return double.IsNaN(v) ? "NaN" : v.ToString("0.#####e+0", Inv); }

        static int Main(string[] args)
        {
            bool verbose = args.Contains("--verbose");
            string only = null; string csvPath = null; string onlyDefects = null;
            for (int i = 0; i < args.Length; ++i) {
                if (args[i] == "--part" && i + 1 < args.Length) only = args[i + 1];
                if (args[i] == "--defects" && i + 1 < args.Length) onlyDefects = args[i + 1];
                if (args[i] == "--csv" && i + 1 < args.Length) csvPath = args[i + 1];
            }
            var rows = new List<string> { "part,defects,parameter,chain,first_failing_step,check,check_first_failing_step,value_at_first_failure,value_at_end,seconds" };
            var summary = new List<string[]>();

            var defectSets = new Dictionary<string, DefectSet>() {
                { "welded", DefectSet.None() },
                { "soup", DefectSet.Soup() },
                { "faulty", DefectSet.Faulty() },
                { "faulty+gap1e-5", DefectSet.FaultyWithGap(1e-5) },
            };

            var cases = new List<Tuple<string, string, DefectSet, Func<DefectSet, ReferencePart>>>();
            foreach (var d in defectSets) {
                if (onlyDefects != null && d.Key != onlyDefects) continue;
                DefectSet ds = d.Value; string dn = d.Key;
                if (only == null || only == "flange")
                    foreach (int n in new[] { 24, 64, 128 })
                        cases.Add(Tuple.Create("flange", "n=" + n, ds, (Func<DefectSet, ReferencePart>)(x => Parts.Flange(n, x, PartFrame.Identity))));
                if (only == null || only == "rib")
                    foreach (double t in new[] { 0.4, 0.1 })
                        cases.Add(Tuple.Create("rib", "t=" + t.ToString(Inv), ds, (Func<DefectSet, ReferencePart>)(x => Parts.HousingRib(x, PartFrame.Identity, t))));
                if (only == null || only == "screw")
                    foreach (double ratio in new[] { 1e-1, 1e-2, 1e-3, 1e-4, 1e-5, 1e-6 })
                        cases.Add(Tuple.Create("screw", "ratio=" + ratio.ToString("0e+0", Inv), ds, (Func<DefectSet, ReferencePart>)(x => Parts.ScrewAssembly(ratio, x, PartFrame.Identity))));
            }
            var names = new Dictionary<DefectSet, string>();
            foreach (var d in defectSets) names[d.Value] = d.Key;

            foreach (var c in cases) {
                ReferencePart part = c.Item4(c.Item3);
                foreach (string chainName in new[] { "library", "pipeline" }) {
                    ChainResult chain = chainName == "library" ? Chains.RunLibraryChain(part) : Chains.RunPipelineChain(part);
                    StepResult first = chain.FirstFailingStep();
                    double seconds = chain.Steps.Sum(s => s.Seconds);
                    string head = c.Item1 + " " + c.Item2 + " [" + names[c.Item3] + "] " + chainName + ": first failing step = " + chain.FirstFailingName()
                        + (first != null && first.Error.Length > 0 ? " (" + first.Error + ")" : "") + string.Format(Inv, "  ({0:0.0} s)", seconds);
                    Console.WriteLine(head);
                    if (verbose) PrintSteps(chain);
                    foreach (PartCheck pc in part.Checks.Where(x => x.Functional)) {
                        StepResult f = chain.FirstFailingStep(pc.Name);
                        StepResult last = chain.Steps.LastOrDefault(s => s.Ran);
                        double vFirst = f != null && f.Ran ? f.Check(pc.Name).Value : double.NaN;
                        double vEnd = last != null ? last.Check(pc.Name).Value : double.NaN;
                        rows.Add(string.Join(",", c.Item1, names[c.Item3], c.Item2, chainName, chain.FirstFailingName(), pc.Name, f == null ? "-" : f.Name, F(vFirst), F(vEnd), seconds.ToString("0.00", Inv)));
                        summary.Add(new[] { c.Item1, names[c.Item3], c.Item2, chainName, pc.Name, f == null ? "-" : f.Name });
                    }
                }
            }
            if (csvPath != null) File.WriteAllLines(csvPath, rows);
            Console.WriteLine();
            Console.WriteLine("first failing step per check (- = every step passes):");
            Console.WriteLine("part | defects | parameter | chain | check | first failing step");
            foreach (string[] s in summary) Console.WriteLine(string.Join(" | ", s));
            return 0;
        }

        static void PrintSteps(ChainResult chain)
        {
            foreach (StepResult s in chain.Steps) {
                var sb = new StringBuilder();
                sb.Append("    ").Append(s.Name.PadRight(10)).Append(s.Ran ? (s.Triangles + " tri").PadRight(10) : "-".PadRight(10));
                if (!s.Ran) sb.Append(s.Error);
                foreach (CheckResult r in s.Checks)
                    sb.Append(" ").Append(r.Name).Append(r.Pass ? "=ok(" : "=FAIL(").Append(F(r.Value)).Append(")");
                Console.WriteLine(sb.ToString());
                if (s.Ran && s.Error.Length > 0) Console.WriteLine("        note: " + s.Error);
                if (s.Info.Length > 0) Console.WriteLine("        info: " + s.Info);
            }
        }
    }
}
