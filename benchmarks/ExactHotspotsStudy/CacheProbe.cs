// The spike of the next candidate (section 3 of the note): a cache of MeshRoundingRepair's triangle-triangle results,
// keyed by the 18 coordinates (a temporary patch of the library, results/probe_counters.patch; never committed). Per case:
// the time without and with the cache (minimum of 3 alternating runs), and whether the results are identical bit for bit.
using System;
using System.Linq;
using System.Reflection;
using g3;

static class CacheProbe
{
    static ulong Hash(DMesh3 m)
    {
        if (m == null) return 0;
        ulong h = 1469598103934665603UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        foreach (int v in m.VertexIndices()) { var p = m.GetVertex(v); Mix(v); Mix(BitConverter.DoubleToInt64Bits(p.x)); Mix(BitConverter.DoubleToInt64Bits(p.y)); Mix(BitConverter.DoubleToInt64Bits(p.z)); }
        foreach (int t in m.TriangleIndices()) { var q = m.GetTriangle(t); Mix(t); Mix(q.a); Mix(q.b); Mix(q.c); }
        return h;
    }

    public static void Run()
    {
        var t = typeof(ExactMeshBoolean).Assembly.GetType("g3.MeshRoundingRepair");
        var on = t?.GetField("ProbeCache"); var hits = t.GetField("ProbeHits"); var clear = t.GetMethod("ProbeClearCache");
        if (on == null) throw new InvalidOperationException("mode cache needs results/probe_counters.patch applied to the library");
        foreach (var (name, make) in Workload.Cases()) Workload.Run(make, out _);      // JIT warm-up
        Console.WriteLine("| case | ms without | ms with cache | change | cache hits | identical |");
        Console.WriteLine("|---|---|---|---|---|---|");
        long ta = 0, tb = 0; int same = 0, n = 0;
        foreach (var (name, make) in Workload.Cases()) {
            long a = long.MaxValue, b = long.MaxValue; ulong ha = 0, hb = 0; long h = 0;
            for (int r = 0; r < 3; ++r) {
                on.SetValue(null, false);
                a = Math.Min(a, Workload.Run(make, out var bo)); ha = Hash(bo.Result);
                on.SetValue(null, true); clear.Invoke(null, null); hits.SetValue(null, 0L);
                b = Math.Min(b, Workload.Run(make, out var bc)); hb = Hash(bc.Result); h = (long)hits.GetValue(null);
            }
            on.SetValue(null, false);
            ta += a; tb += b; n++; if (ha == hb) same++;
            Console.WriteLine($"| {name} | {a} | {b} | {(a == 0 ? 0 : 100.0 * (b - a) / a):F0} % | {h} | {(ha == hb ? "yes" : "NO")} |");
        }
        Console.WriteLine($"\ntotal: {ta} ms without, {tb} ms with the cache ({100.0 * (tb - ta) / ta:F1} %); identical results {same} of {n}");
    }
}
