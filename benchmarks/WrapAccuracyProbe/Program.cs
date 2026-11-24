// WrapAccuracyProbe: how accurate can a wrap-based result get (research note docs/research/ALPHA_WRAP_ACCURACY_2026-11.md).
//   Spike A: wrap, then "snap back" (every vertex onto the input, naive and validity-preserving).
//   Spike B: inputs with holes: which part of the wrap is made-up surface, against MeshAutoRepair / MeshRobustRepair.
// No library change; public API only. See README.md.
//
//   dotnet run -c Release --project benchmarks/WrapAccuracyProbe -- a   <out.jsonl> <input>... [--alphas 4,20,100] [--offsets 1e-3,1e-4,1e-5] [--limit 60]
//   dotnet run -c Release --project benchmarks/WrapAccuracyProbe -- b   <out.jsonl> <input>... [--alphas ...] [--offsets ...]
//   dotnet run -c Release --project benchmarks/WrapAccuracyProbe -- classify <dir-or-ids> [--seed N]
//   dotnet run -c Release --project benchmarks/WrapAccuracyProbe -- selftest
//   input = a mesh file, proc:<name> (Inputs.SolidNames), or for b: hole:<solid>:<kind>:<param> (see SpikeB.cs), sheet
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("usage: WrapAccuracyProbe a|b|classify|selftest ..."); return 2; }
        string cmd = args[0];
        if (cmd == "selftest") return SelfTest.Run();
        if (cmd == "classify") return Classify.Run(args);
        if (cmd == "report") return Report.Run(args);
        if (cmd != "a" && cmd != "b") { Console.Error.WriteLine("unknown command " + cmd); return 2; }
        string outFile = args[1];
        var inputs = new List<string>();
        double[] alphas = { 4, 20, 100 }, offsets = { 1e-3, 1e-4, 1e-5 };
        double limit = 60;
        for (int i = 2; i < args.Length; ++i) {
            if (args[i] == "--alphas") alphas = args[++i].Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            else if (args[i] == "--offsets") offsets = args[++i].Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            else if (args[i] == "--limit") limit = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (args[i] == "proc:all") inputs.AddRange(Inputs.SolidNames.Select(n => "proc:" + n));
            else if (args[i].StartsWith("@")) inputs.AddRange(File.ReadAllLines(args[i].Substring(1)).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")));
            else inputs.Add(args[i]);
        }
        var done = new HashSet<string>();
        if (File.Exists(outFile))
            foreach (string line in File.ReadLines(outFile)) {
                using (var doc = JsonDocument.Parse(line))
                    done.Add(doc.RootElement.GetProperty("input").GetString() + "|" + doc.RootElement.GetProperty("alpha_den").GetDouble().ToString("R") + "|" + doc.RootElement.GetProperty("offset_rel").GetDouble().ToString("R") + "|" + doc.RootElement.GetProperty("variant").GetString());
            }
        // warm-up (JIT), not recorded
        { var w = new MeshAlphaWrap(Inputs.Solid("box")) { RelativeAlpha = 20, RelativeOffset = 1000 }; w.Compute(); }
        foreach (string spec in inputs) {
            foreach (double ad in alphas) foreach (double off in offsets) {
                string key = Inputs.NameOf(spec) + "|" + ad.ToString("R") + "|" + off.ToString("R") + "|";
                string probeVariant = cmd == "a" ? "wrap" : "wrap";
                if (done.Contains(key + probeVariant)) continue;
                List<Dictionary<string, object>> rows;
                try {
                    rows = cmd == "a" ? SpikeA.Run(spec, ad, off, limit) : SpikeB.Run(spec, ad, off, limit);
                } catch (Exception e) {
                    rows = new List<Dictionary<string, object>> { new Dictionary<string, object> { ["input"] = Inputs.NameOf(spec), ["alpha_den"] = ad, ["offset_rel"] = off, ["variant"] = "wrap", ["status"] = "exception", ["error"] = e.GetType().Name + ": " + e.Message } };
                }
                foreach (var r in rows) {
                    string json = JsonSerializer.Serialize(r, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
                    File.AppendAllText(outFile, json + "\n");
                    Console.WriteLine(Summary(r));
                }
            }
        }
        return 0;
    }

    static string Summary(Dictionary<string, object> r)
    {
        var parts = new List<string>();
        foreach (string k in new[] { "input", "alpha_den", "offset_rel", "variant", "status", "tris", "ms", "verdict", "vol_err", "h_res_in", "h_in_res", "added_share", "lost_share" })
            if (r.ContainsKey(k)) parts.Add(k + "=" + (r[k] is double d ? d.ToString("G4") : r[k]));
        return string.Join(" ", parts);
    }
}
