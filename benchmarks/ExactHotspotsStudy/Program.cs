// The probes of docs/research/EXACT_HOTSPOTS_STUDY_2026-11.md: the two hot spots of ExactMeshBoolean on the slow tail
// (the sign-bearing exact stage of orient3d, the projected tri-tri filter), measured on procedural degenerate inputs.
// Research only, nothing here is library code. The modes `filter` and `pairs` need the temporary counters of
// results/probe_counters.patch in the library (never committed); `cases` and `micro` run on the plain library.
//   dotnet run -c Release -- cases     the workload, time per case
//   dotnet run -c Release -- filter    orient3d calls that reach the exact stage: path taken, |det| against the filter bound
//   dotnet run -c Release -- pairs     tri-tri tests of MeshRoundingRepair: calls against distinct pairs
//   dotnet run -c Release -- cache     the spike: a cache of those tests, A/B with a bit-identity check
using System;

static class Program
{
    static int Main(string[] args)
    {
        switch (args.Length > 0 ? args[0] : "") {
            case "cases": Workload.RunCases(); return 0;
            case "filter": FilterProbe.Run(); return 0;
            case "pairs": PairProbe.Run(); return 0;
            case "cache": CacheProbe.Run(); return 0;
            default: Console.Error.WriteLine("usage: cases | filter | pairs | cache"); return 1;
        }
    }
}
