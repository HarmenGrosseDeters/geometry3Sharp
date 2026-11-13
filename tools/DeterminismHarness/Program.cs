// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Bit-for-bit determinism harness (docs/design/DETERMINISM.md).
//   run:      DeterminismHarness run --out <dir> [--serial] [--filter <substring>]
//   compare:  DeterminismHarness compare <dirA> <dirB>
//   adversary: DeterminismHarness adversary [--seeds <n>] [--filter <substring>]
//             every case serially in the given order, backwards and in <n> seeded permutations of every parallel loop's
//             items (gParallel.SerialOrderOnThisThread); reports the cases whose result changes with the order
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace DeterminismHarness
{
    static class Program
    {
        static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length >= 3 && args[0] == "compare") return Compare(args[1], args[2], args.Contains("--brief"));
            if (args.Length >= 1 && args[0] == "run") return Run(args);
            if (args.Length >= 1 && args[0] == "adversary") return Adversary(args);
            Console.Error.WriteLine("usage: run --out <dir> [--serial] [--order <n>] [--filter <substring>] | compare <dirA> <dirB> | adversary [--seeds <n>] [--filter <substring>]");
            return 2;
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // gParallel.SerialOnThisThread (internal, [ThreadStatic]) makes the library's own parallel loops run in order on the calling thread
        static readonly FieldInfo SerialField = typeof(g3.gParallel).GetField("SerialOnThisThread", BindingFlags.NonPublic | BindingFlags.Static);

        // gParallel.SerialOrderOnThisThread (internal, per thread): the order of the items of a serial loop (0 as given, -1 backwards,
        // else a seeded permutation)
        static readonly PropertyInfo OrderField = typeof(g3.gParallel).GetProperty("SerialOrderOnThisThread", BindingFlags.NonPublic | BindingFlags.Static);

        // one case on its own thread (large stack), with the library's serial switch and loop order. caseSerial is what the
        // case gets: true also turns the per-class parallel flags off (Remesher, DualContouring, MarchingCubes), which
        // takes their plain loops instead of gParallel's
        static Trace RunCase(Case c, bool serial, int order, out Exception error, bool? caseSerial = null)
        {
            Trace trace = null; Exception err = null;
            var thread = new Thread(() => {
                try {
                    if (SerialField != null) SerialField.SetValue(null, serial);
                    if (OrderField != null) OrderField.SetValue(null, order);
                    trace = c.Run(caseSerial ?? serial);
                } catch (Exception e) { err = e; }
            }, 256 * 1024 * 1024);
            thread.Start(); thread.Join();
            error = err;
            return trace;
        }

        // The environment adversary for the schedule (docs/design/DETERMINISM_PROOF.md, option B): a result that depends on the
        // order in which a parallel loop runs its items (shared appends, ids from a counter, a reduction) changes when the
        // loop runs them backwards or permuted. Compared in this process with the serial run in the given order
        static int Adversary(string[] args)
        {
            string filter = Arg(args, "--filter");
            int seeds = int.Parse(Arg(args, "--seeds") ?? "3", CultureInfo.InvariantCulture);
            if (OrderField == null || SerialField == null) { Console.Error.WriteLine("gParallel.SerialOnThisThread / SerialOrderOnThisThread not found"); return 2; }
            var orders = new List<int>() { -1 };
            for (int s = 1; s <= seeds; ++s) orders.Add(s);
            int differ = 0, same = 0, failed = 0;
            foreach (Case c in Cases.All()) {
                if (filter != null && c.Name.IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                // (the per-class parallel flags stay on, so that those classes go through gParallel's loops, which the switch
                // runs serially in the order under test)
                Trace reference = RunCase(c, true, 0, out Exception error, false);
                if (error != null) { Console.WriteLine(c.Name + "\tERROR\t" + error.GetType().Name + ": " + error.Message.Replace('\n', ' ')); failed++; continue; }
                string verdict = null;
                foreach (int order in orders) {
                    Trace t = RunCase(c, true, order, out error, false);
                    string name = (order == -1) ? "backwards" : "seed " + order;
                    if (error != null) { verdict = name + ": " + error.GetType().Name + ": " + error.Message.Replace('\n', ' '); break; }
                    int k = 0, m = Math.Min(t.Values.Count, reference.Values.Count);
                    while (k < m && t.Values[k] == reference.Values[k]) ++k;
                    if (k == m && t.Values.Count == reference.Values.Count) continue;
                    int diffCount = 0;
                    for (int i = 0; i < m; ++i) if (t.Values[i] != reference.Values[i]) diffCount++;
                    string label = k < reference.Values.Count ? reference.Labels[reference.LabelOf[k]] : "(end)";
                    verdict = $"{name}: first at value {k} ({label}); {diffCount} of {m} values differ; lengths {reference.Values.Count}/{t.Values.Count}";
                    break;
                }
                if (verdict == null) { same++; Console.WriteLine(c.Name + "\tsame\t" + reference.Values.Count); }
                else { differ++; Console.WriteLine(c.Name + "\tDIFFERS\t" + verdict); }
            }
            Console.WriteLine($"# orders: backwards and {seeds} seeded permutations; {same} same, {differ} different, {failed} failed");
            return (differ == 0 && failed == 0) ? 0 : 1;
        }

        static int Run(string[] args)
        {
            string outDir = Arg(args, "--out") ?? ".";
            string filter = Arg(args, "--filter");
            int order = int.Parse(Arg(args, "--order") ?? "0", CultureInfo.InvariantCulture);
            bool serial = args.Contains("--serial") || order != 0;
            if (order != 0 && OrderField == null) { Console.Error.WriteLine("gParallel.SerialOrderOnThisThread not found"); return 2; }
            Directory.CreateDirectory(outDir);
            var summary = new StringBuilder();
            string runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            summary.Append("# runtime\t" + runtime + "\n# serial\t" + serial + "\n# order\t" + order + "\n# processors\t" + Environment.ProcessorCount + "\n");
            summary.Append("# os\t" + System.Runtime.InteropServices.RuntimeInformation.OSDescription + "\n");
            summary.Append("# arch\t" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture + "\n");
            Console.Write(summary.ToString());
            foreach (Case c in Cases.All()) {
                if (filter != null && c.Name.IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                Trace trace = RunCase(c, serial, order, out Exception error, (order != 0) ? false : (bool?)null);
                string line;
                if (error != null) line = c.Name + "\tERROR\t" + error.GetType().Name + ": " + error.Message.Replace('\n', ' ');
                else {
                    trace.Write(Path.Combine(outDir, c.Name + ".trace"));
                    line = c.Name + "\t" + trace.Hash().ToString("x16") + "\t" + trace.Values.Count;
                }
                Console.WriteLine(line);
                summary.Append(line + "\n");
                if (trace != null) foreach (string note in trace.Notes) { Console.WriteLine("#   " + note); summary.Append("#   " + note + "\n"); }
            }
            File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary.ToString(), new UTF8Encoding(false));
            return 0;
        }

        // first diverging value of every case that differs
        static int Compare(string dirA, string dirB, bool brief)
        {
            var names = Directory.GetFiles(dirA, "*.trace").Select(Path.GetFileName)
                .Union(Directory.GetFiles(dirB, "*.trace").Select(Path.GetFileName)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            int differ = 0, same = 0;
            foreach (string n in names) {
                string a = Path.Combine(dirA, n), b = Path.Combine(dirB, n);
                if (!File.Exists(a) || !File.Exists(b)) { Console.WriteLine($"{n}\tONLY IN {(File.Exists(a) ? "A" : "B")}"); differ++; continue; }
                string[] la = File.ReadAllLines(a), lb = File.ReadAllLines(b);
                int k = 0, m = Math.Min(la.Length, lb.Length);
                while (k < m && la[k] == lb[k]) ++k;
                if (k == m && la.Length == lb.Length) { same++; continue; }
                differ++;
                int diffCount = 0;
                for (int i = 0; i < m; ++i) if (la[i] != lb[i]) diffCount++;
                string va = k < la.Length ? Describe(la[k]) : "(end)", vb = k < lb.Length ? Describe(lb[k]) : "(end)";
                string label = k < la.Length ? la[k].Split('\t')[2] : "";
                // per label: how many values differ (which part of the result diverges)
                var perLabel = new SortedDictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < m; ++i) if (la[i] != lb[i]) { string l = la[i].Split('\t')[2]; perLabel[l] = perLabel.TryGetValue(l, out int c) ? c + 1 : 1; }
                string parts = string.Join(",", perLabel.Select(kv => (kv.Key == "" ? "-" : kv.Key) + ":" + kv.Value));
                if (brief) Console.WriteLine($"{n.Replace(".trace", "")}\tfirst@{k}({(label == "" ? "-" : label)})\t{diffCount}/{m}\t{parts}");
                else Console.WriteLine($"{n.Replace(".trace", "")}\tDIFFERS first at value {k} ({label}) A={va} B={vb}; {diffCount} of {m} values differ; lengths {la.Length}/{lb.Length}; by section {parts}");
            }
            Console.WriteLine($"# {same} identical, {differ} different");
            return differ == 0 ? 0 : 1;
        }

        static string Describe(string line)
        {
            string[] p = line.Split('\t');
            long bits = long.Parse(p[1], NumberStyles.HexNumber);
            return p[0] == "d" ? BitConverter.Int64BitsToDouble(bits).ToString("R") + " (0x" + p[1] + ")" : bits.ToString();
        }
    }
}
