// The corpus gate of E' stage 4 (docs/design/COPLANAR_REGION_CLIPPING.md, #1630): ExactMeshBoolean with and without RegionClipping on the procedural corpora,
// one JSON row per operation on stdout (RegionClippingCompare.Row: no mesh anywhere). The Thingi10K cases are `certify thingi ... compare=region`,
// driven by data/certify_thingi.py (run --command region-compare).
//
//   region-compare fuzz <family|all> <from> <count>     BooleanFuzzCases seeds [from, from + count) of a family (all: every closed family and the open one)
//   region-compare audit [class]                        the procedural adversarial corpus of the pipeline audit (benchmarks/SolidBooleanAudit/Corpus.cs), the operands as given
//   region-compare thingi self|pair|contact ...         as `certify thingi` (CertifyThingi.cs) with compare=region
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using geometry3Sharp.Tests;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

static class RegionCompareCases
{
    static readonly Ops[] AllOps = { Ops.Union, Ops.Intersection, Ops.Difference };

    public static int Run(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length == 0) { Console.Error.WriteLine("usage: region-compare (fuzz <family|all> <from> <count> | audit [class] | thingi ...)"); return 1; }
        switch (args[0]) {
            case "thingi":
                return CertifyThingi.Run(args.Skip(1).Append("compare=region").ToArray());
            case "fuzz": {
                string family = args[1];
                int from = int.Parse(args[2]), count = int.Parse(args[3]);
                var families = family == "all" ? BooleanFuzzCases.ClosedFamilies.Append(BooleanFuzzCases.OpenFamily).ToList() : new List<string> { family };
                foreach (string f in families)
                    for (int seed = from; seed < from + count; ++seed) {
                        FuzzCase c = BooleanFuzzCases.Make(f, seed);
                        foreach (Ops op in AllOps)
                            Emit(new Dictionary<string, object> { ["case"] = $"fuzz-{f}-{seed}", ["description"] = c.Description }, RegionClippingCompare.Row(new List<DMesh3> { c.A, c.B }, op));
                    }
                return 0;
            }
            case "audit": {
                string only = args.Length > 1 ? args[1] : null;
                foreach (Case c in Corpus.Cases().Where(c => only == null || c.Class == only || c.Name.Contains(only))) {
                    DMesh3[] given = c.Given();
                    Emit(new Dictionary<string, object> { ["case"] = $"audit-{c.Class}-{c.Name}" }, RegionClippingCompare.Row(given, c.Op));
                }
                return 0;
            }
            default:
                Console.Error.WriteLine("region-compare: unknown '" + args[0] + "'");
                return 1;
        }
    }

    static void Emit(Dictionary<string, object> head, Dictionary<string, object> row)
    {
        foreach (var kv in head) row[kv.Key] = kv.Value;
        Console.WriteLine(JsonSerializer.Serialize(row));
        Console.Out.Flush();
    }
}
