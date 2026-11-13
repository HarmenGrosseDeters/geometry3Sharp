# Scratch A/B of docs/perf/EXTREME_RUNTIME_BOOLEANS.md (lever A2): the shard of the point indexes of
# mesh_ops/ExactMeshArrangement.cs chosen from a multiplicatively mixed hash (Fibonacci hashing, Knuth TAOCP 6.4)
# instead of hash % 64. Only the lock and dictionary a point goes to change; the dictionaries' own lookups are as before.
# With G3_SHARD_PROBE=1 the patched build also counts the points per shard of the unmixed choice and prints the largest
# share at exit ("SHARD_PROBE"). Throw-away worktree only, never committed.
import sys
path = sys.argv[1]; s = open(path).read()
for old in ["fixedIndex[(fk.Hash & 0x7FFFFFFF) % Shards]", "rationalIndex[(hp.Hash & 0x7FFFFFFF) % Shards]",
            "keyIndex[(key.GetHashCode() & 0x7FFFFFFF) % Shards]"]:
    if s.count(old) != 1: raise SystemExit("anchor: " + old)
    name, expr = old.split("[", 1)
    h = expr[1:expr.index(" & ")]
    s = s.replace(old, name + "[ShardMix.Of(" + h + ")]")
s = s.rstrip()[:-1] + """
    internal static class ShardMix
    {
        static readonly bool Mix = System.Environment.GetEnvironmentVariable("G3_SHARD_MIX") == "1";
        static readonly bool Probe = System.Environment.GetEnvironmentVariable("G3_SHARD_PROBE") == "1";
        static readonly long[] count = new long[64];
        static ShardMix()
        {
            if (Probe) System.AppDomain.CurrentDomain.ProcessExit += (o, e) => {
                long t = 0, m = 0; foreach (long c in count) { t += c; m = System.Math.Max(m, c); }
                int used = 0; foreach (long c in count) if (c > 0) used++;
                System.Console.Error.WriteLine("SHARD_PROBE {\\"calls\\": " + t + ", \\"largest_share\\": " + (t > 0 ? (double)m / t : 0).ToString(System.Globalization.CultureInfo.InvariantCulture) + ", \\"shards_used\\": " + used + "}");
            };
        }
        public static int Of(int hash)
        {
            if (Probe) System.Threading.Interlocked.Increment(ref count[(hash & 0x7FFFFFFF) % 64]);
            return Mix ? (int)(((uint)hash * 2654435769u) >> 26) : (hash & 0x7FFFFFFF) % 64;
        }
    }
}
"""
open(path, "w").write(s)
