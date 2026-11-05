using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using g3;

internal static class EdgeHashBenchmark
{
    readonly record struct Edge(long Key, int Increment);

    static long Pack(int a, int b) => ((long)a << 32) | (uint)b;

    static Dictionary<long, int> Count(Edge[] edges, int count, IEqualityComparer<long> comparer)
    {
        var uses = new Dictionary<long, int>(comparer);
        for (int i = 0; i < count; ++i) {
            Edge edge = edges[i];
            uses[edge.Key] = uses.TryGetValue(edge.Key, out int c) ? c + edge.Increment : edge.Increment;
        }
        return uses;
    }

    static void Equal(Dictionary<long, int> a, Dictionary<long, int> b)
    {
        if (a.Count != b.Count || a.Any(kv => !b.TryGetValue(kv.Key, out int c) || c != kv.Value))
            throw new InvalidOperationException("Edge counts changed");
    }

    static void SelfTest()
    {
        var comparer = EdgeKeyComparer.Instance;
        var hashes = new HashSet<int>();
        for (int i = 0; i < 65536; ++i) {
            long key = Pack(2 * i, 2 * i + 1);
            if (key.GetHashCode() != 1) throw new InvalidOperationException("Unexpected Int64 hash");
            hashes.Add(comparer.GetHashCode(key));
        }
        if (hashes.Count < 64000) throw new InvalidOperationException("Correlated keys still collapse");
        long first = Pack(1, 2);
        int secondLow = new Index2i(3, 0).GetHashCode() ^ comparer.GetHashCode(first);
        long second = Pack(3, secondLow);
        if (comparer.GetHashCode(first) != comparer.GetHashCode(second) || comparer.Equals(first, second))
            throw new InvalidOperationException("Hash collisions must not define equality");
        foreach (long key in new[] { 0L, long.MinValue, long.MaxValue, -1L, first, second }) {
            if (!comparer.Equals(key, key) || comparer.Equals(key, key ^ 1))
                throw new InvalidOperationException("Packed key identity changed");
        }
        var random = new Random(385);
        var edges = new Edge[6000];
        for (int i = 0; i < edges.Length; ++i) {
            int a = random.Next(200), b = random.Next(200);
            edges[i] = new Edge(Pack(Math.Min(a, b), Math.Max(a, b)), a < b ? 1 : 1 << 16);
        }
        Equal(Count(edges, edges.Length, null), Count(edges, edges.Length, comparer));
        Equal(Count(edges, edges.Length, comparer), Count(edges, edges.Length, comparer));
        Console.WriteLine(JsonSerializer.Serialize(new { self_test = "passed", correlated_keys = 65536,
            mixed_hashes = hashes.Count, equality_collision_checked = true, repeated_counts_equal = true }));
    }

    static Edge[] ReadEdges(string path)
    {
        var args = new object[] { path, null, null };
        var reader = typeof(Program).GetMethod("ReadSoup", BindingFlags.NonPublic | BindingFlags.Static);
        if (!(bool)reader.Invoke(null, args)) throw new InvalidOperationException("ReadSoup failed");
        var vertices = (List<Vector3d>)args[1];
        var faces = (List<Index3i>)args[2];
        var ids = new Dictionary<Vector3d, int>();
        var map = new int[vertices.Count];
        for (int i = 0; i < map.Length; ++i) {
            Vector3d p = vertices[i];
            if (p.x == 0) p.x = 0;
            if (p.y == 0) p.y = 0;
            if (p.z == 0) p.z = 0;
            if (!ids.TryGetValue(p, out int id)) { id = ids.Count; ids[p] = id; }
            map[i] = id;
        }
        var edges = new List<Edge>(checked(faces.Count * 3));
        foreach (Index3i f in faces) {
            var g = new Index3i(map[f.a], map[f.b], map[f.c]);
            if (g.a == g.b || g.b == g.c || g.a == g.c) continue;
            for (int k = 0; k < 3; ++k) {
                int x = g[k], y = g[(k + 1) % 3];
                edges.Add(new Edge(Pack(Math.Min(x, y), Math.Max(x, y)), x < y ? 1 : 1 << 16));
            }
        }
        return edges.ToArray();
    }

    static object Distribution(Dictionary<long, int> uses, IEqualityComparer<long> comparer)
    {
        var bins = new Dictionary<int, int>();
        foreach (long key in uses.Keys) {
            int hash = comparer == null ? key.GetHashCode() : comparer.GetHashCode(key);
            bins[hash] = bins.TryGetValue(hash, out int c) ? c + 1 : 1;
        }
        return new { unique_keys = uses.Count, distinct_hashes = bins.Count,
            largest_hash_group = bins.Count == 0 ? 0 : bins.Values.Max() };
    }

    static void Measure(string path, string report)
    {
        Edge[] edges = ReadEdges(path);
        Count(edges, Math.Min(512, edges.Length), null);
        Count(edges, Math.Min(512, edges.Length), EdgeKeyComparer.Instance);
        var samples = new List<object>();
        foreach (int requested in new[] { 8192, 32768, 131072 }) {
            int n = Math.Min(requested, edges.Length);
            var before = new List<double>();
            var after = new List<double>();
            for (int repeat = 0; repeat < 3; ++repeat) {
                var timer = Stopwatch.StartNew();
                var original = Count(edges, n, null);
                timer.Stop();
                before.Add(timer.Elapsed.TotalMilliseconds);
                timer.Restart();
                var optimized = Count(edges, n, EdgeKeyComparer.Instance);
                timer.Stop();
                after.Add(timer.Elapsed.TotalMilliseconds);
                Equal(original, optimized);
            }
            samples.Add(new { edge_uses = n, original_ms = before, optimized_ms = after, counts_equal = true });
        }
        var fullTimer = Stopwatch.StartNew();
        var full = Count(edges, edges.Length, EdgeKeyComparer.Instance);
        fullTimer.Stop();
        using var input = File.OpenRead(path);
        var result = new { command = Environment.CommandLine, input = Path.GetFileName(path),
            input_sha256 = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(),
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            library_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Index2i).Assembly.Location))).ToLowerInvariant(),
            samples, full_edge_uses = edges.Length, full_optimized_ms = fullTimer.Elapsed.TotalMilliseconds,
            original_hashes = Distribution(full, null), optimized_hashes = Distribution(full, EdgeKeyComparer.Instance),
            full_original_timing = "not run: bounded prefixes avoid the known long-running collision chain",
            wclosed = full.Values.All(c => (c & 0xFFFF) == (c >> 16)),
            manifold = full.Values.All(c => (c & 0xFFFF) == 1 && (c >> 16) == 1) };
        File.WriteAllText(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }

    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test") { SelfTest(); return 0; }
        if (args.Length != 2) throw new ArgumentException("Use --self-test or <OFF-path> <external-report.json>");
        Measure(args[0], args[1]);
        return 0;
    }
}
