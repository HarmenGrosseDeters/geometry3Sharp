// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Bit-for-bit determinism harness (docs/design/DETERMINISM.md).
//   run:      DeterminismHarness run --out <dir> [--serial] [--filter <substring>]
//   compare:  DeterminismHarness compare <dirA> <dirB>
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
            Console.Error.WriteLine("usage: run --out <dir> [--serial] [--filter <substring>] | compare <dirA> <dirB>");
            return 2;
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // gParallel.SerialOnThisThread (internal, [ThreadStatic]) makes the library's own parallel loops run in order on the calling thread
        static readonly FieldInfo SerialField = typeof(g3.gParallel).GetField("SerialOnThisThread", BindingFlags.NonPublic | BindingFlags.Static);

        static int Run(string[] args)
        {
            string outDir = Arg(args, "--out") ?? ".";
            string filter = Arg(args, "--filter");
            bool serial = args.Contains("--serial");
            Directory.CreateDirectory(outDir);
            var summary = new StringBuilder();
            string runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            summary.Append("# runtime\t" + runtime + "\n# serial\t" + serial + "\n# processors\t" + Environment.ProcessorCount + "\n");
            summary.Append("# os\t" + System.Runtime.InteropServices.RuntimeInformation.OSDescription + "\n");
            summary.Append("# arch\t" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture + "\n");
            Console.Write(summary.ToString());
            foreach (Case c in Cases.All()) {
                if (filter != null && c.Name.IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                Trace trace = null; Exception error = null;
                var thread = new Thread(() => {
                    try {
                        if (SerialField != null) SerialField.SetValue(null, serial);
                        trace = c.Run(serial);
                    } catch (Exception e) { error = e; }
                }, 256 * 1024 * 1024);
                thread.Start(); thread.Join();
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
