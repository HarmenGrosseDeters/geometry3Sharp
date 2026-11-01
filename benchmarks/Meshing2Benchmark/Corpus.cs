using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Meshing2Acceptance
{
    /// <summary>
    /// The corpus run: public test inputs of other projects (fetched by corpus/fetch.py at run time, never committed; sources
    /// and licenses in docs/design/MESHING2D_PROVENANCE.md), each meshed by the milestones' candidates and judged by the
    /// oracle. Only statistics are printed. Polygons (earcut, poly2tri) run through M2 and M3, point sets (delaunator)
    /// through M1 and M3; M3 at each angle of --angles.
    /// </summary>
    static class Corpus
    {
        sealed class Item
        {
            public string Name;
            public List<List<double[]>> Rings;   // polygon: first ring outer, the others holes; null for a point set
            public List<double[]> Points;        // point set
        }

        public static int Run(string[] args, Func<string[], string, string> arg)
        {
            string dir = arg(args, "--dir") ?? Path.Combine(RepositoryRoot(), "benchmarks", "Meshing2Benchmark", "corpus", "data");
            if (!Directory.Exists(dir)) {
                Console.WriteLine("no corpus at " + dir + ": run python3 benchmarks/Meshing2Benchmark/corpus/fetch.py first");
                return 2;
            }
            var angles = (arg(args, "--angles") ?? "0,20,30").Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            string filter = arg(args, "--filter");
            int seconds = int.Parse(arg(args, "--seconds") ?? "120", CultureInfo.InvariantCulture);
            int judgeLimit = int.Parse(arg(args, "--judge-limit") ?? "60000", CultureInfo.InvariantCulture);
            // a filter is a part of the name, or with a trailing $ the end of it (earcut/water-huge$ is not water-huge2)
            bool Matches(string name) => filter == null || (filter.EndsWith("$")
                ? name.EndsWith(filter.Substring(0, filter.Length - 1), StringComparison.OrdinalIgnoreCase)
                : name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            var items = Load(dir).Where(i => Matches(i.Name)).ToList();
            var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Count(string key) { tally.TryGetValue(key, out int n); tally[key] = n + 1; }
            int bad = 0;
            foreach (var item in items) {
                var runs = new List<(string mode, Milestone m, Mesh2Input input)>();
                var plain = Build(item);
                runs.Add((item.Rings != null ? "M2" : "M1", item.Rings != null ? Milestone.M2 : Milestone.M1, plain));
                foreach (double a in angles) {
                    var q = Build(item);
                    q.MinAngleDeg = a;
                    runs.Add(("M3@" + a.ToString(CultureInfo.InvariantCulture), Milestone.M3, q));
                }
                foreach (var (mode, m, input) in runs) {
                    string verdict = Judge(CandidateRegistry.For(m), m, input, seconds, judgeLimit, out string detail);
                    Count(mode.Substring(0, 2) + " " + verdict);
                    if (verdict != "ok" && verdict != "unjudged") ++bad;
                    Console.WriteLine(item.Name + " " + mode + ": " + verdict + " | " + detail);
                }
            }
            Console.WriteLine();
            Console.WriteLine("corpus: " + items.Count + " inputs");
            foreach (var pair in tally) Console.WriteLine("  " + pair.Key + ": " + pair.Value);
            return bad == 0 ? 0 : 1;
        }

        static string Judge(IMeshing2Candidate candidate, Milestone m, Mesh2Input input, int seconds, int judgeLimit, out string detail)
        {
            Mesh2Output output = null; Exception error = null;
            var sw = Stopwatch.StartNew();
            var thread = new System.Threading.Thread(() => { try { output = candidate.Run(input); } catch (Exception e) { error = e; } }, 256 << 20);
            thread.IsBackground = true;
            thread.Start();
            if (!thread.Join(seconds * 1000)) { detail = "after " + seconds + " s"; return "TIMEOUT"; }
            double ms = sw.Elapsed.TotalMilliseconds;
            if (error != null) { detail = error.GetType().Name + ": " + error.Message; return error is ArgumentException ? "rejected" : "THREW"; }
            string size = input.Count + " points, " + output.VX.Length + " vertices, " + output.Triangles.Length + " triangles, "
                + ms.ToString("F0", CultureInfo.InvariantCulture) + " ms" + (output.SteinerLimitReached ? ", Steiner limit" : "");
            if (output.Status == Mesh2Status.Failed) { detail = output.FailureKind + "; " + size; return "FAILED"; }
            if (output.VX.Length > judgeLimit) { detail = size + " (above --judge-limit)"; return "unjudged"; }
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = m });
            string text = report.ToString();
            detail = size + (report.Ok ? "" : "; " + (text.Length > 300 ? text.Substring(0, 300) : text));
            return report.Ok ? "ok" : "VIOLATION " + string.Join(",", report.Violations.Select(v => v.Kind.ToString()).Distinct());
        }

        static Mesh2Input Build(Item item)
        {
            var x = new List<double>(); var y = new List<double>();
            var input = new Mesh2Input();
            if (item.Rings == null) {
                foreach (var p in item.Points) { x.Add(p[0]); y.Add(p[1]); }
            } else {
                for (int r = 0; r < item.Rings.Count; ++r) {
                    var ids = new List<int>();
                    foreach (var p in item.Rings[r]) { ids.Add(x.Count); x.Add(p[0]); y.Add(p[1]); }
                    if (ids.Count >= 3) input.Rings.Add(new RingInput(ids.ToArray(), r > 0));
                }
            }
            input.X = x.ToArray(); input.Y = y.ToArray();
            return input;
        }

        static IEnumerable<Item> Load(string dir)
        {
            foreach (var f in Files(dir, "earcut", "*.json")) {
                var rings = ((List<object>)Parse(File.ReadAllText(f))).Select(r => ((List<object>)r).Select(Pair).ToList()).ToList();
                yield return new Item { Name = "earcut/" + Path.GetFileNameWithoutExtension(f), Rings = rings };
            }
            foreach (var f in Files(dir, "delaunator", "*.json")) {
                var points = ((List<object>)Parse(File.ReadAllText(f))).Select(Pair).ToList();
                yield return new Item { Name = "delaunator/" + Path.GetFileNameWithoutExtension(f), Points = points };
            }
            foreach (var f in Files(dir, "poly2tri", "*.dat")) {
                var ring = File.ReadAllLines(f).Select(l => l.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    .Where(t => t.Length >= 2).Select(t => new[] { double.Parse(t[0], CultureInfo.InvariantCulture), double.Parse(t[1], CultureInfo.InvariantCulture) }).ToList();
                yield return new Item { Name = "poly2tri/" + Path.GetFileNameWithoutExtension(f), Rings = new List<List<double[]>> { ring } };
            }
        }

        static IEnumerable<string> Files(string dir, string sub, string pattern)
        {
            string d = Path.Combine(dir, sub);
            return Directory.Exists(d) ? Directory.GetFiles(d, pattern).OrderBy(s => s, StringComparer.Ordinal) : Enumerable.Empty<string>();
        }

        static double[] Pair(object o)
        {
            var l = (List<object>)o;
            return new[] { (double)l[0], (double)l[1] };
        }

        /// <summary> nested JSON arrays of numbers (the fixtures' only content), doubles parsed correctly rounded </summary>
        static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            Skip(s, ref i);
            if (i != s.Length) throw new FormatException("trailing text at " + i);
            return v;
        }

        static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) ++i; }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (s[i] == '[') {
                var list = new List<object>();
                ++i; Skip(s, ref i);
                if (s[i] == ']') { ++i; return list; }
                while (true) {
                    list.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (s[i] == ',') { ++i; continue; }
                    if (s[i] == ']') { ++i; return list; }
                    throw new FormatException("expected , or ] at " + i);
                }
            }
            int start = i;
            while (i < s.Length && "+-.0123456789eE".IndexOf(s[i]) >= 0) ++i;
            if (start == i) throw new FormatException("unexpected character at " + i);
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string RepositoryRoot()
        {
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "geometry3Sharp_netstandard.csproj"))) return dir.FullName;
            throw new InvalidOperationException("run this inside the repository");
        }
    }
}
