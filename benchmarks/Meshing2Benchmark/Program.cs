using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using g3;

namespace Meshing2Benchmark
{
    internal static class Program
    {
        const double OracleScale = 1099511627776.0; // Fixtures are exact multiples of 2^-40.
        struct Point
        {
            public BigInteger X, Y;
            public Point(Vector2d p) { X = new BigInteger(p.x * OracleScale); Y = new BigInteger(p.y * OracleScale); }
        }

        static int Main(string[] args)
        {
            try {
                OracleSelfTest();
                int repeats = args.Length == 0 ? 3 : int.Parse(args[0], CultureInfo.InvariantCulture);
                if (repeats < 1 || repeats > 20) throw new ArgumentOutOfRangeException(nameof(repeats));
#if NET48
                AppDomain.MonitoringIsEnabled = true;
                Console.WriteLine("# runtime=net48; allocation_counter=AppDomain.MonitoringTotalAllocatedMemorySize");
#else
                Console.WriteLine("# runtime=" + Environment.Version + "; allocation_counter=GC.GetAllocatedBytesForCurrentThread");
#endif
                Console.WriteLine("# assembly=" + typeof(DelaunayMesh2).Assembly.GetName().Version);
                Console.WriteLine("case,n,input_sha256,engine,repeats,median_ms,median_allocated_bytes,triangles,topology_sha256,valid");
                foreach (int count in new[] { 64, 256 }) {
                    foreach (string kind in new[] { "uniform", "grid", "near-collinear" }) {
                        var points = Fixture(kind, count);
                        string expectedTopology = null;
                        foreach (string engine in new[] { "bootstrap", "public-m1" }) {
                            Func<Index3i[]> run = engine == "bootstrap"
                                ? (Func<Index3i[]>)(() => new BootstrapCandidate(points).Run())
                                : (() => PublicMesh(points));
                            var warmup = run();
                            Validate(points, warmup);
                            string topology = TopologyFingerprint(warmup);
                            if (expectedTopology == null) expectedTopology = topology;
                            else if (topology != expectedTopology) throw new InvalidOperationException("A/B topology mismatch: " + kind);
                            var times = new double[repeats];
                            var allocations = new long[repeats];
                            for (int i = 0; i < repeats; ++i) {
                                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                                var watch = new Stopwatch();
                                long before = AllocatedBytes();
                                watch.Start();
                                var faces = run();
                                watch.Stop();
                                allocations[i] = AllocatedBytes() - before;
                                times[i] = watch.Elapsed.TotalMilliseconds;
                                Validate(points, faces);
                                if (TopologyFingerprint(faces) != topology) throw new InvalidOperationException("Nondeterministic topology.");
                            }
                            Array.Sort(times); Array.Sort(allocations);
                            Console.WriteLine(string.Join(",", kind, count, InputFingerprint(points), engine, repeats,
                                times[repeats / 2].ToString("F3", CultureInfo.InvariantCulture), allocations[repeats / 2],
                                warmup.Length, topology, "true"));
                        }
                    }
                }
                return 0;
            } catch (Exception error) {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        static void OracleSelfTest()
        {
            var triangle = new[] { new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(0, 4) };
            Validate(triangle, new[] { new Index3i(0, 1, 2) });
            MustReject(() => Validate(triangle, new[] { new Index3i(0, 2, 1) }));
            MustReject(() => Validate(triangle, new Index3i[0]));
            MustReject(() => Validate(triangle, new[] { new Index3i(0, 1, 2), new Index3i(0, 1, 2) }));
            var interior = triangle.Concat(new[] { new Vector2d(1, 1) }).ToArray();
            MustReject(() => Validate(interior, new[] { new Index3i(0, 1, 2) }));
            var square = new[] { new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(4, 4), new Vector2d(0, 4) };
            Validate(square, new[] { new Index3i(0, 1, 2), new Index3i(0, 2, 3) });
            MustReject(() => Validate(square, new[] { new Index3i(0, 1, 2) }));
            var quadrilateral = new[] { new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(4, 4), new Vector2d(0, 2) };
            Validate(quadrilateral, new[] { new Index3i(0, 1, 3), new Index3i(1, 2, 3) });
            MustReject(() => Validate(quadrilateral, new[] { new Index3i(0, 1, 2), new Index3i(0, 2, 3) }));
        }

        static void MustReject(Action validation)
        {
            try { validation(); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("Oracle accepted deliberately corrupted topology.");
        }

        static long AllocatedBytes()
        {
#if NET48
            return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
#else
            return GC.GetAllocatedBytesForCurrentThread();
#endif
        }

        static Index3i[] PublicMesh(Vector2d[] points)
        {
            var result = new DelaunayMesh2().Compute(points);
            if (result.Status != Triangulation2Status.Success || result.Mesh == null)
                throw new InvalidOperationException("Public mesher failed: " + result.FailureKind);
            return result.Mesh.Triangles.ToArray();
        }

        static Vector2d[] Fixture(string kind, int count)
        {
            var result = new List<Vector2d>();
            if (kind == "grid") {
                int side = (int)Math.Sqrt(count);
                for (int x = 0; x < side; ++x)
                    for (int y = 0; y < side; ++y) result.Add(new Vector2d(x, y));
            } else if (kind == "near-collinear") {
                for (int i = 0; i < count; ++i)
                    result.Add(new Vector2d(i, i / 1048576.0 + (i % 2 == 0 ? 1 : -1) / OracleScale));
            } else {
                uint state = 0x5eed1234;
                var seen = new HashSet<Vector2d>();
                while (result.Count < count) {
                    state = unchecked(1664525 * state + 1013904223);
                    int x = (int)(state >> 16);
                    state = unchecked(1664525 * state + 1013904223);
                    var p = new Vector2d(x, (int)(state >> 16));
                    if (seen.Add(p)) result.Add(p);
                }
            }
            if (result.Count != count) throw new InvalidOperationException("Fixture size mismatch.");
            return result.ToArray();
        }

        static BigInteger Orient(Point a, Point b, Point c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        static BigInteger Circle(Point a, Point b, Point c, Point d)
        {
            BigInteger ax = a.X - d.X, ay = a.Y - d.Y, bx = b.X - d.X, by = b.Y - d.Y,
                cx = c.X - d.X, cy = c.Y - d.Y;
            return (ax * ax + ay * ay) * (bx * cy - by * cx)
                + (bx * bx + by * by) * (cx * ay - cy * ax)
                + (cx * cx + cy * cy) * (ax * by - ay * bx);
        }

        static void Validate(Vector2d[] input, Index3i[] faces)
        {
            var p = input.Select(x => new Point(x)).ToArray();
            var used = new HashSet<int>();
            var edges = new Dictionary<(int, int), List<(int a, int b)>>();
            BigInteger area = BigInteger.Zero;
            foreach (var face in faces) {
                for (int k = 0; k < 3; ++k) {
                    int id = face[k];
                    if (id < 0 || id >= input.Length) throw new InvalidOperationException("Invalid vertex index.");
                    used.Add(id);
                    int a = id, b = face[(k + 1) % 3];
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    if (!edges.TryGetValue(key, out var uses)) edges.Add(key, uses = new List<(int, int)>());
                    uses.Add((a, b));
                }
                BigInteger faceArea = Orient(p[face.a], p[face.b], p[face.c]);
                if (faceArea.Sign <= 0) throw new InvalidOperationException("Nonpositive triangle.");
                area += faceArea;
                for (int i = 0; i < input.Length; ++i)
                    if (Circle(p[face.a], p[face.b], p[face.c], p[i]).Sign > 0)
                        throw new InvalidOperationException("Nonempty circumcircle.");
            }
            if (used.Count != input.Length) throw new InvalidOperationException("Lost vertex.");
            if (input.Length - edges.Count + faces.Length != 1) throw new InvalidOperationException("Euler characteristic mismatch.");
            foreach (var entry in edges) {
                var uses = entry.Value;
                if (uses.Count > 2 || (uses.Count == 2 && (uses[0].a != uses[1].b || uses[0].b != uses[1].a)))
                    throw new InvalidOperationException("Invalid edge adjacency.");
                foreach (var point in p)
                    if (Orient(p[entry.Key.Item1], p[entry.Key.Item2], point).IsZero &&
                        point.X >= BigInteger.Min(p[entry.Key.Item1].X, p[entry.Key.Item2].X) &&
                        point.X <= BigInteger.Max(p[entry.Key.Item1].X, p[entry.Key.Item2].X) &&
                        point.Y >= BigInteger.Min(p[entry.Key.Item1].Y, p[entry.Key.Item2].Y) &&
                        point.Y <= BigInteger.Max(p[entry.Key.Item1].Y, p[entry.Key.Item2].Y) &&
                        !(point.X == p[entry.Key.Item1].X && point.Y == p[entry.Key.Item1].Y) &&
                        !(point.X == p[entry.Key.Item2].X && point.Y == p[entry.Key.Item2].Y))
                        throw new InvalidOperationException("Unsplit edge vertex.");
                if (uses.Count == 1 && p.Any(q => Orient(p[uses[0].a], p[uses[0].b], q).Sign < 0))
                    throw new InvalidOperationException("Boundary does not support convex hull.");
            }
            var keys = edges.Keys.ToArray();
            for (int i = 0; i < keys.Length; ++i) {
                var a = keys[i];
                for (int j = i + 1; j < keys.Length; ++j) {
                    var b = keys[j];
                    if (a.Item1 == b.Item1 || a.Item1 == b.Item2 || a.Item2 == b.Item1 || a.Item2 == b.Item2) continue;
                    if (Orient(p[a.Item1], p[a.Item2], p[b.Item1]).Sign * Orient(p[a.Item1], p[a.Item2], p[b.Item2]).Sign < 0 &&
                        Orient(p[b.Item1], p[b.Item2], p[a.Item1]).Sign * Orient(p[b.Item1], p[b.Item2], p[a.Item2]).Sign < 0)
                        throw new InvalidOperationException("Crossing edges.");
                }
            }
            var sorted = Enumerable.Range(0, p.Length).OrderBy(i => p[i].X).ThenBy(i => p[i].Y).ToArray();
            var lower = Chain(sorted, p);
            var upper = Chain(sorted.Reverse(), p);
            var hull = lower.Take(lower.Count - 1).Concat(upper.Take(upper.Count - 1)).ToArray();
            BigInteger hullArea = BigInteger.Zero;
            for (int i = 0; i < hull.Length; ++i) {
                var a = p[hull[i]]; var b = p[hull[(i + 1) % hull.Length]];
                hullArea += a.X * b.Y - a.Y * b.X;
            }
            if (area != hullArea) throw new InvalidOperationException("Hull area mismatch.");
        }

        static List<int> Chain(IEnumerable<int> ordered, Point[] p)
        {
            var chain = new List<int>();
            foreach (int id in ordered) {
                while (chain.Count >= 2 && Orient(p[chain[chain.Count - 2]], p[chain[chain.Count - 1]], p[id]).Sign <= 0)
                    chain.RemoveAt(chain.Count - 1);
                chain.Add(id);
            }
            return chain;
        }

        static string InputFingerprint(Vector2d[] points) => Hash(points.SelectMany(p =>
            BitConverter.GetBytes(p.x).Concat(BitConverter.GetBytes(p.y))).ToArray());

        static string TopologyFingerprint(Index3i[] faces)
        {
            var canonical = faces.Select(t => {
                if (t.b < t.a && t.b < t.c) return new Index3i(t.b, t.c, t.a);
                if (t.c < t.a && t.c < t.b) return new Index3i(t.c, t.a, t.b);
                return t;
            }).OrderBy(t => t.a).ThenBy(t => t.b).ThenBy(t => t.c);
            return Hash(canonical.SelectMany(t => BitConverter.GetBytes(t.a)
                .Concat(BitConverter.GetBytes(t.b)).Concat(BitConverter.GetBytes(t.c))).ToArray());
        }

        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
