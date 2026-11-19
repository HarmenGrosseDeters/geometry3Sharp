using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Runtime;

namespace Clipper2Benchmark
{
    /// <summary>
    /// prof [filter] [seconds]: per op wall time, allocated bytes, gen0/1/2 collections and GC pause per call over a window of
    /// about `seconds` (GC.GetTotalPauseDuration), and the time of the same op inside a no-GC region (no collection, so the pauses
    /// are gone but the allocation itself stays).
    /// sites [filter]: allocated bytes by type (GCAllocationTick events, one per about 100 KB allocated, so an estimate).
    /// alloc: what allocating costs on this machine (ns per KB: small objects and arrays, dropped at once, collections included).
    /// </summary>
    static class Profile
    {
        public static int Run(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "prof";
            string filter = args.Length > 1 ? args[1] : "";
            if (mode == "alloc") return AllocCost();
            if (mode == "sites") return Sites(filter);
            if (mode == "ab") return AB(filter, args.Length > 2 ? double.Parse(args[2]) : 2.0);
            double window = args.Length > 2 ? double.Parse(args[2]) : 4.0;
            Console.WriteLine($"# runtime {Environment.Version} gcServer={GCSettings.IsServerGC} cores={Environment.ProcessorCount} gen0MaxBudget={Environment.GetEnvironmentVariable("DOTNET_GCgen0MaxBudget") ?? "default"}");
            Console.WriteLine("op\tms\tallocKB\tgen0/call\tgen1/call\tgen2/call\tpauseMs/call\tpauseShare\tms_noGC");
            foreach (var (name, run) in Ops.All()) {
                if (filter.Length > 0 && !name.Contains(filter)) continue;
                double sink = 0;
                for (int i = 0; i < 3; i++) sink += run();
                var sw = Stopwatch.StartNew(); sink += run(); sink += run(); sw.Stop();
                int per = Math.Clamp((int)(window * 1000 / Math.Max(0.05, sw.Elapsed.TotalMilliseconds / 2)), 3, 3000);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                int c0 = GC.CollectionCount(0), c1 = GC.CollectionCount(1), c2 = GC.CollectionCount(2);
                var p0 = GC.GetTotalPauseDuration();
                var t = Stopwatch.StartNew();
                for (int i = 0; i < per; i++) sink += run();
                t.Stop();
                double pause = (GC.GetTotalPauseDuration() - p0).TotalMilliseconds / per;
                double kb = (GC.GetAllocatedBytesForCurrentThread() - b0) / 1024.0 / per;
                double ms = t.Elapsed.TotalMilliseconds / per;
                double g0 = (GC.CollectionCount(0) - c0) / (double)per, g1 = (GC.CollectionCount(1) - c1) / (double)per, g2 = (GC.CollectionCount(2) - c2) / (double)per;
                double noGc = NoGc(run, kb, ms, ref sink);
                Console.WriteLine($"{name}\t{ms:F3}\t{kb:F0}\t{g0:F3}\t{g1:F3}\t{g2:F3}\t{pause:F3}\t{pause / ms:P1}\t{noGc:F3}");
                if (sink == 42.4242) Console.WriteLine();
            }
            return 0;
        }

        static double NoGc(Func<double> run, double kb, double ms, ref double sink)
        {
            // iterations such that the region holds all allocations (cap 200 MB; the runtime allows up to the segment size)
            int per = (int)Math.Min(200.0 * 1024 / Math.Max(kb, 1) * 0.8, Math.Max(2, 1500 / Math.Max(ms, 0.05)));
            if (per < 1) return double.NaN;
            var times = new List<double>();
            try {
                for (int rep = 0; rep < 5; rep++) {
                    GC.Collect();
                    if (!GC.TryStartNoGCRegion((long)(kb * 1024 * per * 1.15) + (4L << 20))) return double.NaN;
                    var t = Stopwatch.StartNew();
                    for (int i = 0; i < per; i++) sink += run();
                    t.Stop();
                    if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion) GC.EndNoGCRegion();
                    times.Add(t.Elapsed.TotalMilliseconds / per);
                }
            } catch (Exception e) when (e is InvalidOperationException || e is ArgumentOutOfRangeException) {
                if (times.Count == 0) return double.NaN;
            }
            return times.OrderBy(x => x).ElementAt(times.Count / 2);
        }

        sealed class AllocListener : EventListener
        {
            public readonly Dictionary<string, long> Bytes = new Dictionary<string, long>();
            public long Total;
            protected override void OnEventSourceCreated(EventSource source)
            {
                if (source.Name == "Microsoft-Windows-DotNETRuntime")
                    EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
            }
            protected override void OnEventWritten(EventWrittenEventArgs e)
            {
                if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick")) return;
                string type = null; long amount = 100 * 1024;
                for (int i = 0; i < e.PayloadNames.Count; i++) {
                    if (e.PayloadNames[i] == "TypeName") type = e.Payload[i] as string;
                    if (e.PayloadNames[i] == "AllocationAmount64") amount = Convert.ToInt64(e.Payload[i]);
                }
                lock (Bytes) {
                    Bytes.TryGetValue(type ?? "?", out long b);
                    Bytes[type ?? "?"] = b + amount; Total += amount;
                }
            }
        }

        static int Sites(string filter)
        {
            foreach (var (name, run) in Ops.All()) {
                if (filter.Length > 0 && !name.Contains(filter)) continue;
                double sink = 0;
                for (int i = 0; i < 3; i++) sink += run();
                using var l = new AllocListener();
                var sw = Stopwatch.StartNew(); sink += run(); sw.Stop();
                int per = Math.Clamp((int)(3000 / Math.Max(0.05, sw.Elapsed.TotalMilliseconds)), 5, 300);
                lock (l.Bytes) { l.Bytes.Clear(); l.Total = 0; }
                for (int i = 0; i < per; i++) sink += run();
                Console.WriteLine($"== {name}: {l.Total / 1024.0 / per:F0} KB/op sampled");
                lock (l.Bytes)
                    foreach (var kv in l.Bytes.OrderByDescending(k => k.Value).Take(8))
                        Console.WriteLine($"   {100.0 * kv.Value / l.Total,5:F1} %  {kv.Key}");
                if (sink == 42.4242) Console.WriteLine();
            }
            return 0;
        }

        static readonly (string Name, Action Set)[] Configs = {
            ("base", () => g3.Clipper2.Clipper2Levers.SetAll(false)),
            ("regions", () => { g3.Clipper2.Clipper2Levers.SetAll(false); g3.Clipper2.Clipper2Levers.ReuseRegions = true; }),
            ("regions+actives", () => { g3.Clipper2.Clipper2Levers.SetAll(false); g3.Clipper2.Clipper2Levers.ReuseRegions = true; g3.Clipper2.Clipper2Levers.KeepActives = true; }),
            ("engine", () => { g3.Clipper2.Clipper2Levers.SetAll(false); g3.Clipper2.Clipper2Levers.ReuseEngine = true; }),
            ("engine+actives", () => { g3.Clipper2.Clipper2Levers.SetAll(false); g3.Clipper2.Clipper2Levers.ReuseEngine = true; g3.Clipper2.Clipper2Levers.KeepActives = true; }),
            ("bridge", () => { g3.Clipper2.Clipper2Levers.SetAll(false); g3.Clipper2.Clipper2Levers.LeanBridge = true; }),
            ("all", () => g3.Clipper2.Clipper2Levers.SetAll(true)),
        };

        /// <summary> Each lever against the base in one process, interleaved rounds; digests must equal the base's (bit for bit). </summary>
        static int AB(string filter, double window)
        {
            Console.WriteLine("op\tconfig\tms\tallocKB\tpauseMs\tgen2/call\tvsBase\tidentical");
            foreach (var (name, run) in Ops.All()) {
                if (filter.Length > 0 && !name.Contains(filter)) continue;
                double sink = 0;
                Configs[0].Set();
                double baseDigest = run();
                for (int i = 0; i < 3; i++) sink += run();
                var sw = Stopwatch.StartNew(); sink += run(); sink += run(); sw.Stop();
                int per = Math.Clamp((int)(window * 1000 / 5 / Math.Max(0.05, sw.Elapsed.TotalMilliseconds / 2)), 1, 500);
                var ms = new List<double>[Configs.Length]; var kb = new double[Configs.Length]; var pa = new List<double>[Configs.Length]; var g2 = new double[Configs.Length];
                var same = new bool[Configs.Length];
                for (int c = 0; c < Configs.Length; c++) { ms[c] = new List<double>(); pa[c] = new List<double>(); same[c] = true; }
                for (int round = 0; round < 5; round++)
                    for (int c = 0; c < Configs.Length; c++) {
                        Configs[c].Set();
                        sink += run(); // warm this configuration (first call builds its pools)
                        if (run() != baseDigest) same[c] = false;
                        GC.Collect();
                        long b0 = GC.GetAllocatedBytesForCurrentThread(); int c2 = GC.CollectionCount(2);
                        var p0 = GC.GetTotalPauseDuration();
                        var t = Stopwatch.StartNew();
                        for (int i = 0; i < per; i++) sink += run();
                        t.Stop();
                        ms[c].Add(t.Elapsed.TotalMilliseconds / per);
                        pa[c].Add((GC.GetTotalPauseDuration() - p0).TotalMilliseconds / per);
                        kb[c] = (GC.GetAllocatedBytesForCurrentThread() - b0) / 1024.0 / per;
                        g2[c] = (GC.CollectionCount(2) - c2) / (double)per;
                    }
                Configs[0].Set();
                double baseMs = ms[0].OrderBy(x => x).ElementAt(2);
                for (int c = 0; c < Configs.Length; c++) {
                    double m = ms[c].OrderBy(x => x).ElementAt(2), p = pa[c].OrderBy(x => x).ElementAt(2);
                    Console.WriteLine($"{name}\t{Configs[c].Name}\t{m:F3}\t{kb[c]:F0}\t{p:F3}\t{g2[c]:F3}\t{(m / baseMs - 1):+0.0%;-0.0%}\t{(same[c] ? "yes" : "NO")}");
                }
                if (sink == 42.4242) Console.WriteLine();
            }
            return 0;
        }

        sealed class Node64 { public long A, B, C, D, E; public object P; }   // 56 bytes + header: a Vertex / OutPt sized object

        static int AllocCost()
        {
            Console.WriteLine("# allocation cost on this machine: object dropped at once, collections included");
            foreach (var (label, bytes, make) in new (string, int, Func<object>)[] {
                ("56-byte object", 56, () => new Node64()),
                ("long[64] (512 B)", 512 + 24, () => new long[64]),
                ("long[1024] (8 KB)", 8192 + 24, () => new long[1024]),
                ("long[8192] (64 KB)", 65536 + 24, () => new long[8192]) }) {
                for (int i = 0; i < 2_000_000; i++) make();
                long n = Math.Max(2_000_000, 400_000_000L / bytes);
                var t = Stopwatch.StartNew();
                for (long i = 0; i < n; i++) make();
                t.Stop();
                Console.WriteLine($"{label}\t{t.Elapsed.TotalMilliseconds * 1e6 / (n * bytes / 1024.0):F1} ns/KB\t{t.Elapsed.TotalMilliseconds * 1e6 / n:F1} ns/object");
            }
            return 0;
        }
    }
}
