// Hot spot 2: the triangle-triangle tests of MeshRoundingRepair (TriangleTriangleFilter.by_projection and the exact test
// behind it): how many tests per case, and how many distinct pairs (by the 18 coordinates) they are. The repetition is
// the upper bound of a cache of pair results; an indirect predicate cannot help here, because these tests run on the
// rounded doubles and construct no point.
using System;
using System.Collections.Generic;
using System.Reflection;
using g3;

static class PairProbe
{
    public static void Run()
    {
        var t = typeof(ExactMeshBoolean).Assembly.GetType("g3.MeshRoundingRepair");
        var setF = t?.GetField("ProbePairs"); var callsF = t?.GetField("ProbePairCalls");
        if (setF == null) throw new InvalidOperationException("mode pairs needs results/probe_counters.patch applied to the library");
        Console.WriteLine("| case | ms | pair tests | distinct pairs | tests per distinct pair |");
        Console.WriteLine("|---|---|---|---|---|");
        long tc = 0, td = 0;
        foreach (var (name, make) in Workload.Cases()) {
            setF.SetValue(null, new HashSet<(long, long)>()); callsF.SetValue(null, 0L);
            long ms = Workload.Run(make, out _);
            long calls = (long)callsF.GetValue(null); int distinct = ((HashSet<(long, long)>)setF.GetValue(null)).Count;
            setF.SetValue(null, null);
            tc += calls; td += distinct;
            Console.WriteLine($"| {name} | {ms} | {calls} | {distinct} | {(distinct == 0 ? 0 : (double)calls / distinct):F1} |");
        }
        Console.WriteLine($"\ntotal: {tc} tests, {td} distinct pairs, {(double)tc / Math.Max(1, td):F1} tests per distinct pair");
    }
}
