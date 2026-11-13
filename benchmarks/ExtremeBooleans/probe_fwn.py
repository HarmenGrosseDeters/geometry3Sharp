# Scratch instrumentation of mesh_ops/ExactMeshBoolean.cs for docs/perf/EXTREME_RUNTIME_BOOLEANS.md (lever C1): counts
# the fast-winding-number queries of the open-operand classification (front_generalized_winding), split into queries
# against the piece's own input mesh and against another mesh, with their time. Needs probe_bigint.py applied to
# math/ExactPredicates.cs first (it reuses its BigProbe class); prints "FWN_PROBE {...}" on stderr at exit with
# G3_BIGINT_PROBE=1. Throw-away worktree only, never committed.
import sys
path = sys.argv[1]; s = open(path).read()
old = """            double w = tree.FastWindingNumber(pv);
            List<int> on = stack ?? new List<int>() { ot.Source };"""
if s.count(old) != 1: raise SystemExit("anchor")
s = s.replace(old, """            long fwn_t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            double w = tree.FastWindingNumber(pv);
            if (BigProbe.On) FwnProbe.Add(src.Mesh == mesh, System.Diagnostics.Stopwatch.GetTimestamp() - fwn_t0);
            List<int> on = stack ?? new List<int>() { ot.Source };""")
s = s.rstrip()
assert s.endswith("}")
s = s[:-1] + """
    internal static class FwnProbe
    {
        static long[] n = new long[2], ticks = new long[2];
        static FwnProbe()
        {
            System.AppDomain.CurrentDomain.ProcessExit += (o, e) => System.Console.Error.WriteLine("FWN_PROBE {\\"own\\": [" + n[1] + ", "
                + (ticks[1] * 1000 / System.Diagnostics.Stopwatch.Frequency) + "], \\"other\\": [" + n[0] + ", " + (ticks[0] * 1000 / System.Diagnostics.Stopwatch.Frequency) + "]}");
        }
        public static void Add(bool own, long t) { int k = own ? 1 : 0; System.Threading.Interlocked.Increment(ref n[k]); System.Threading.Interlocked.Add(ref ticks[k], t); }
    }
}
"""
open(path, "w").write(s)
