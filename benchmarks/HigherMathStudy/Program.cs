// The probes of docs/research/HIGHER_MATH_STUDY_2026-11.md. Procedural inputs only. Research only: nothing here is library code.
//   dotnet run -c Release -- valuation   idea 1: valuation identity on the exact Boolean results, defects caught / missed, cost
//   dotnet run -c Release -- bigint      idea 2: the workload profiled with dotnet-trace for the BigInteger share
//   dotnet run -c Release -- orient      idea 3: orientation of triangle soups, library vs Z2 synchronization
using System;

static class Program
{
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "";
        switch (mode) {
            case "valuation": Valuation.RunAll(); return 0;
            case "bigint": BigIntWorkload.Run(args); return 0;
            case "orient": Orientation.Run(); return 0;
            default: Console.Error.WriteLine("usage: valuation | bigint [reps] [stratum] | orient"); return 1;
        }
    }
}
