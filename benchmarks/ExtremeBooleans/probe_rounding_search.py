# Scratch instrumentation of mesh_ops/MeshRoundingRepair.cs for the upper bound of the rounding-search levers of
# docs/perf/EXTREME_RUNTIME_BOOLEANS.md section 8 (lever 1: the box-neighbour enumeration of the full pair searches;
# lever 2: `near`, the box query of the evaluations). Per MeshRoundingRepair.Apply, timed on the calling thread with
# Stopwatch: the whole Apply, every full search (intersecting_pairs_all; the first one separately), the incremental
# searches, the tree rebuilds, and the time and calls of near(box) summed over the threads that call it (with the
# number of distinct threads, so a sum over parallel callers can be told apart from wall time).
# Applied to a throw-away worktree, never committed to the library:
#     git worktree add --detach <wt> <commit>; python3 probe_rounding_search.py <wt>/mesh_ops/MeshRoundingRepair.cs
#     dotnet build <wt>/benchmarks/BooleanComparison -c Release -o <tool>
# With G3_ROUND_PROBE=1 every Apply prints one line "ROUND_PROBE {...}" on stderr (the last one of a `profile` process
# is the timed operation; the earlier ones are its warm-up booleans).
import sys
path = sys.argv[1]; s = open(path).read()
def rep(old, new):
    global s
    if s.count(old) != 1: raise SystemExit("anchor not unique or missing: " + old[:80])
    s = s.replace(old, new)
rep("""        [MethodImpl(JitHints.Optimize)]
        public bool Apply()
        {""", """        [MethodImpl(JitHints.Optimize)]
        public bool Apply()
        {
            if (RoundProbe.On == false) return apply0();
            RoundProbe.Begin();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return apply0(); } finally { RoundProbe.End(System.Diagnostics.Stopwatch.GetTimestamp() - t0, Mesh.TriangleCount); }
        }

        [MethodImpl(JitHints.Optimize)]
        bool apply0()
        {""")
rep("""        void near(AxisAlignedBox3d query, List<int> result)
        {""", """        void near(AxisAlignedBox3d query, List<int> result)
        {
            if (RoundProbe.On == false) { near0(query, result); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            near0(query, result);
            RoundProbe.Near(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        }

        [MethodImpl(JitHints.Optimize)]
        void near0(AxisAlignedBox3d query, List<int> result)
        {""")
rep("""        List<Index2i> intersecting_pairs_all(bool skipExact = true)
        {""", """        List<Index2i> intersecting_pairs_all(bool skipExact = true)
        {
            if (RoundProbe.On == false) return intersecting_pairs_all0(skipExact);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return intersecting_pairs_all0(skipExact); } finally { RoundProbe.Full(System.Diagnostics.Stopwatch.GetTimestamp() - t0); }
        }

        [MethodImpl(JitHints.Optimize)]
        List<Index2i> intersecting_pairs_all0(bool skipExact = true)
        {""")
rep("""        void rebuild_tree()
        {""", """        void rebuild_tree()
        {
            if (RoundProbe.On == false) { rebuild_tree0(); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            rebuild_tree0();
            RoundProbe.Tree(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        }

        void rebuild_tree0()
        {""")
rep("""                pairs = intersecting_pairs_of_changed();""", """                long probe_t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                pairs = intersecting_pairs_of_changed();
                if (RoundProbe.On) RoundProbe.Incremental(System.Diagnostics.Stopwatch.GetTimestamp() - probe_t0);""")
s = s.rstrip()
assert s.endswith("}")
s = s[:-1] + """
    internal static class RoundProbe
    {
        public static readonly bool On = System.Environment.GetEnvironmentVariable("G3_ROUND_PROBE") == "1";
        static long full, fullFirst, fullCalls, incr, incrCalls, tree, treeCalls, near, nearCalls;
        static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> nearThreads = new System.Collections.Concurrent.ConcurrentDictionary<int, bool>();
        static double ms(long t) => t * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        public static void Begin() { full = fullFirst = fullCalls = incr = incrCalls = tree = treeCalls = near = nearCalls = 0; nearThreads.Clear(); }
        public static void Full(long t) { if (System.Threading.Interlocked.Increment(ref fullCalls) == 1) fullFirst = t; System.Threading.Interlocked.Add(ref full, t); }
        public static void Incremental(long t) { System.Threading.Interlocked.Increment(ref incrCalls); System.Threading.Interlocked.Add(ref incr, t); }
        public static void Tree(long t) { System.Threading.Interlocked.Increment(ref treeCalls); System.Threading.Interlocked.Add(ref tree, t); }
        public static void Near(long t) { System.Threading.Interlocked.Increment(ref nearCalls); System.Threading.Interlocked.Add(ref near, t); nearThreads.TryAdd(System.Environment.CurrentManagedThreadId, true); }
        public static void End(long t, int triangles)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            System.Console.Error.WriteLine("ROUND_PROBE {\\"apply_ms\\": " + ms(t).ToString("F1", c) + ", \\"triangles\\": " + triangles
                + ", \\"full_ms\\": " + ms(full).ToString("F1", c) + ", \\"full_first_ms\\": " + ms(fullFirst).ToString("F1", c) + ", \\"full_calls\\": " + fullCalls
                + ", \\"incremental_ms\\": " + ms(incr).ToString("F1", c) + ", \\"incremental_calls\\": " + incrCalls
                + ", \\"tree_ms\\": " + ms(tree).ToString("F1", c) + ", \\"tree_calls\\": " + treeCalls
                + ", \\"near_ms\\": " + ms(near).ToString("F1", c) + ", \\"near_calls\\": " + nearCalls + ", \\"near_threads\\": " + nearThreads.Count + "}");
        }
    }
}
"""
open(path, "w").write(s)
