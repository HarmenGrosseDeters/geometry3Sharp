using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;

namespace Meshing2Acceptance
{
    /// <summary>
    /// The measurement of docs/research/ROBUST_PLANAR_MAPS_2026-11.md: the library's exact planar map (the constrained mesher with its
    /// noding, at the automatic tolerance and with snap rounding) on procedural segment sets that are near-degenerate in the ways the
    /// planar maps of projected 3D curves are (Fuchs and Crane 2026): bundles of near-parallel segments, many segments through one point,
    /// endpoints that almost meet, collinear overlaps, chains of T-junctions; a random soup is the generic reference.
    ///
    /// Two yardsticks bound what another method could gain: `detect` finds the crossing pairs only (a grid, then the filtered exact
    /// Orient2d), which every arrangement method has to do at least; `delaunay` triangulates the output's vertices without constraints,
    /// the part of our time that buys the triangulated output.
    /// With a snap tolerance, `snap_ms` is the snap rounding alone (one run), with its rounds and the points of the snapped input.
    /// A measurement, not a gate: `--judge` runs the oracle on the cases up to `--judge-limit` used output vertices.
    /// The exit code is 1 if a result is Failed or judged invalid.
    /// </summary>
    static class PlanarMapStress
    {
        sealed class Rng
        {
            ulong s;
            public Rng(ulong seed) { s = 0x9E3779B97F4A7C15UL ^ seed; }
            public double Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)); }
            public int Next(int n) => (int)(Next() * n);
        }

        static void Add(ConstrainedMesh2Input input, double ax, double ay, double bx, double by)
        {
            if (ax == bx && ay == by) return;
            int i = input.Points.Count;
            input.Points.Add(new Vector2d(ax, ay)); input.Points.Add(new Vector2d(bx, by));
            input.Segments.Add(new Mesh2Segment(i, i + 1, input.Segments.Count % 7));
        }

        const double Tiny = 1.0 / (1L << 30);      // 2^-30: far above the automatic tolerance (2^-41), far below the snap pixel (2^-20)
        const double Ulps = 1.0 / (1L << 48);      // 2^-48: 32 ulps at 0.5, below the automatic tolerance

        /// <summary> the families; n is the number of segments asked for (some families round it) </summary>
        static ConstrainedMesh2Input Family(string kind, int n)
        {
            var input = new ConstrainedMesh2Input();
            var rng = new Rng((ulong)n * 31 + (ulong)kind.Length);
            switch (kind) {
                case "soup":            // random segments in the unit square: about 0.11 n^2 crossings, none degenerate
                    for (int i = 0; i < n; ++i) Add(input, rng.Next(), rng.Next(), rng.Next(), rng.Next());
                    break;
                case "bundle": {        // n segments across the square in a band of height n 2^-30, the right ends a random permutation of
                                        // the left ones: about n^2 / 4 crossings at angles of 2^-30 to 2 n 2^-30
                    var perm = Enumerable.Range(0, n).ToArray();
                    for (int i = n - 1; i > 0; --i) { int j = rng.Next(i + 1); (perm[i], perm[j]) = (perm[j], perm[i]); }
                    for (int i = 0; i < n; ++i) Add(input, 0, 0.5 + i * Tiny, 1, 0.5 + perm[i] * Tiny);
                    break;
                }
                case "pencil-exact":    // n segments exactly through (1/2, 1/2): symmetric dyadic ends
                case "pencil-near":     // the same with every end moved by up to 2^-48: n^2 / 2 crossings near the centre (within about n 2^-48)
                    for (int i = 0; i < n; ++i) {
                        double a = Math.PI * (i + 0.5) / n;
                        double dx = Math.Round(Math.Cos(a) * (1 << 20)) / (1 << 21), dy = Math.Round(Math.Sin(a) * (1 << 20)) / (1 << 21);
                        double e() => kind == "pencil-near" ? (rng.Next() - 0.5) * 2 * Ulps : 0;
                        Add(input, 0.5 - dx + e(), 0.5 - dy + e(), 0.5 + dx + e(), 0.5 + dy + e());
                    }
                    break;
                case "junction-ulp":    // a square grid of nodes; every grid edge is its own segment, and each of its ends misses the node by
                case "junction-tiny": { // up to 2^-48 (ulp) or 2^-30 (tiny) in x and y: gaps, overshoots and slivers at every node
                    int side = Math.Max(2, (int)Math.Sqrt(n / 2.0));
                    double h = 1.0 / side, eps = kind == "junction-ulp" ? Ulps : Tiny;
                    double e() => (rng.Next() - 0.5) * 2 * eps;
                    for (int j = 0; j <= side; ++j)
                        for (int i = 0; i <= side; ++i) {
                            if (i < side) Add(input, i * h + e(), j * h + e(), (i + 1) * h + e(), j * h + e());
                            if (j < side) Add(input, i * h + e(), j * h + e(), i * h + e(), (j + 1) * h + e());
                        }
                    break;
                }
                case "overlap-exact":   // 32 lines (16 horizontal, 16 vertical, dyadic), each with n / 32 random intervals with dyadic ends:
                case "overlap-near": {  // every stretch of a line is covered many times; near: every end moved by up to 2^-48 across the line
                    int per = Math.Max(1, n / 32);
                    for (int l = 0; l < 16; ++l)
                        for (int k = 0; k < 2 * per; ++k) {
                            double c = (l + 0.5) / 16, u = rng.Next(1 << 20) / (double)(1 << 20), v = rng.Next(1 << 20) / (double)(1 << 20);
                            double e() => kind == "overlap-near" ? (rng.Next() - 0.5) * 2 * Ulps : 0;
                            if (k % 2 == 0) Add(input, u, c + e(), v, c + e()); else Add(input, c + e(), u, c + e(), v);
                        }
                    break;
                }
                case "tchain": {        // one slanted base segment and n short segments that start on it (the start computed in doubles, so up
                                        // to an ulp off the base) and leave to alternating sides: a long chain of T-junctions
                    double ax = 0.0, ay = 0.1, bx = 1.0, by = 0.87;
                    Add(input, ax, ay, bx, by);
                    for (int i = 0; i < n; ++i) {
                        double t = (i + 0.5) / n, px = ax + t * (bx - ax), py = ay + t * (by - ay), side = i % 2 == 0 ? 1 : -1;
                        Add(input, px, py, px - side * 0.77 * 0.01, py + side * 0.01);
                    }
                    break;
                }
                default: throw new ArgumentException("unknown family " + kind);
            }
            return input;
        }

        /// <summary> the yardstick `detect`: the pairs of segments that meet (proper crossings, and contacts with a zero orientation),
        /// from a uniform grid over the boxes (cell: the mean box extent, so for long segments
        /// all pairs are tested) and the filtered exact Orient2d; no map is built </summary>
        static (long crossings, long contacts) Detect(ConstrainedMesh2Input input)
        {
            int n = input.Segments.Count;
            if (n == 0) return (0, 0);
            var lo = new Vector2d[n]; var hi = new Vector2d[n];
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, extent = 0;
            for (int i = 0; i < n; ++i) {
                var a = input.Points[input.Segments[i].A]; var b = input.Points[input.Segments[i].B];
                lo[i] = new Vector2d(Math.Min(a.x, b.x), Math.Min(a.y, b.y)); hi[i] = new Vector2d(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
                minX = Math.Min(minX, lo[i].x); minY = Math.Min(minY, lo[i].y); maxX = Math.Max(maxX, hi[i].x); maxY = Math.Max(maxY, hi[i].y);
                extent += Math.Max(hi[i].x - lo[i].x, hi[i].y - lo[i].y);
            }
            double cell = Math.Max(extent / Math.Max(1, n), Math.Max(maxX - minX, maxY - minY) / 2048);
            if (!(cell > 0)) cell = 1;
            int nx = (int)((maxX - minX) / cell) + 1, ny = (int)((maxY - minY) / cell) + 1;
            int Cx(double x) => Math.Min(nx - 1, Math.Max(0, (int)((x - minX) / cell)));
            int Cy(double y) => Math.Min(ny - 1, Math.Max(0, (int)((y - minY) / cell)));
            var count = new int[nx * ny + 1];
            for (int i = 0; i < n; ++i)
                for (int y = Cy(lo[i].y); y <= Cy(hi[i].y); ++y) for (int x = Cx(lo[i].x); x <= Cx(hi[i].x); ++x) ++count[y * nx + x + 1];
            for (int c = 0; c < nx * ny; ++c) count[c + 1] += count[c];
            var items = new int[count[nx * ny]]; var fill = (int[])count.Clone();
            for (int i = 0; i < n; ++i)
                for (int y = Cy(lo[i].y); y <= Cy(hi[i].y); ++y) for (int x = Cx(lo[i].x); x <= Cx(hi[i].x); ++x) items[fill[y * nx + x]++] = i;
            long crossings = 0, contacts = 0;
            for (int c = 0; c < nx * ny; ++c) {
                int cx = c % nx, cy = c / nx;
                for (int u = count[c]; u < count[c + 1]; ++u)
                    for (int v = u + 1; v < count[c + 1]; ++v) {
                        int i = items[u], j = items[v];
                        double ox = Math.Max(lo[i].x, lo[j].x), oy = Math.Max(lo[i].y, lo[j].y);
                        if (ox > Math.Min(hi[i].x, hi[j].x) || oy > Math.Min(hi[i].y, hi[j].y)) continue;
                        if (Cx(ox) != cx || Cy(oy) != cy) continue;     // each pair once: in the cell of the lower left corner of the boxes' overlap
                        var a = input.Points[input.Segments[i].A]; var b = input.Points[input.Segments[i].B];
                        var p = input.Points[input.Segments[j].A]; var q = input.Points[input.Segments[j].B];
                        int o1 = ExactPredicates.Orient2d(a, b, p), o2 = ExactPredicates.Orient2d(a, b, q);
                        if (o1 * o2 > 0) continue;
                        int o3 = ExactPredicates.Orient2d(p, q, a), o4 = ExactPredicates.Orient2d(p, q, b);
                        if (o3 * o4 > 0) continue;
                        if (o1 * o2 < 0 && o3 * o4 < 0) ++crossings; else ++contacts;
                    }
            }
            return (crossings, contacts);
        }

        static T OnBigStack<T>(Func<T> work)
        {
            T result = default(T); Exception error = null;
            var thread = new System.Threading.Thread(() => { try { result = work(); } catch (Exception e) { error = e; } }, 256 << 20);
            thread.Start(); thread.Join();
            if (error != null) throw new InvalidOperationException(error.GetType().Name + ": " + error.Message, error);
            return result;
        }

        static (double median, double min, double mb, T result) Time<T>(int repeats, Func<T> work)
        {
            var times = new List<double>(); long allocated = 0; T result = default(T);
            for (int r = 0; r < repeats; ++r) {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                (double ms, long bytes, T value) = OnBigStack(() => {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    var sw = Stopwatch.StartNew();
                    T value2 = work();
                    sw.Stop();
                    return (sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, value2);
                });
                times.Add(ms); allocated = bytes; result = value;
            }
            times.Sort();
            return (times[times.Count / 2], times[0], allocated / 1048576.0, result);
        }

        /// <summary> the vertices a triangle or a subsegment uses: the mesh keeps every input point as a vertex, also the merged ones </summary>
        static Vector2d[] Used(ConstrainedMesh2 mesh)
        {
            var used = new bool[mesh.Vertices.Count];
            foreach (var t in mesh.Triangles) { used[t.a] = true; used[t.b] = true; used[t.c] = true; }
            foreach (var e in mesh.Subsegments) { used[e.A] = true; used[e.B] = true; }
            var list = new List<Vector2d>();
            for (int i = 0; i < used.Length; ++i) if (used[i]) list.Add(mesh.Vertices[i]);
            return list.ToArray();
        }

        static string F(double v, string format = "F1") => v.ToString(format, CultureInfo.InvariantCulture);
        static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

        internal static int Run(string[] args, Func<string[], string, string> arg)
        {
            var kinds = (arg(args, "--kinds") ?? "soup,bundle,pencil-exact,pencil-near,junction-ulp,junction-tiny,overlap-exact,overlap-near,tchain").Split(',');
            var sizes = (arg(args, "--sizes") ?? "small,large").Split(',');
            var tolerances = (arg(args, "--tolerances") ?? "0,9.5367431640625e-07").Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            int repeats = int.Parse(arg(args, "--repeats") ?? "3", CultureInfo.InvariantCulture);
            bool judge = args.Contains("--judge");
            int judgeLimit = int.Parse(arg(args, "--judge-limit") ?? "40000", CultureInfo.InvariantCulture);
            // segments per family and size: for the soup, the bundle, the junctions and the chain some 10^4 (small) and 10^5 (large) output
            // vertices at the automatic tolerance; the pencils and the overlaps have few vertices and many meeting pairs instead
            var counts = new Dictionary<string, int[]> {
                { "soup", new[] { 500, 2000 } }, { "bundle", new[] { 250, 1000 } }, { "pencil-exact", new[] { 1000, 16000 } }, { "pencil-near", new[] { 1000, 16000 } },
                { "junction-ulp", new[] { 10000, 100000 } }, { "junction-tiny", new[] { 10000, 100000 } },
                { "overlap-exact", new[] { 2000, 16000 } }, { "overlap-near", new[] { 2000, 16000 } }, { "tchain", new[] { 10000, 100000 } },
            };
            Console.WriteLine("runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ", " + Environment.ProcessorCount + " cores");
            Console.WriteLine("family,segments,crossing_pairs,contact_pairs,detect_ms,tolerance,status,vertices,used_vertices,triangles,subsegments,median_ms,min_ms,allocated_mb,us_per_used_vertex,delaunay_ms,snap_ms,snap_rounds,snapped_points,verdict");
            foreach (var kind in kinds) if (!counts.ContainsKey(kind)) { Console.Error.WriteLine("unknown family " + kind); return 2; }
            // warm-up: every family of the run three times at a small size on every path (tiered compilation needs more than one call)
            foreach (var kind in kinds) {
                var warm = Family(kind, 96);
                for (int r = 0; r < 3; ++r) {
                    Detect(warm);
                    foreach (double tol in tolerances) {
                        var result = new ConstrainedDelaunayMesh2 { SnapTolerance = tol }.Compute(warm);
                        if (result.Mesh != null && result.Mesh.Vertices.Count >= 3) new DelaunayMesh2().Compute(Used(result.Mesh));
                    }
                }
            }
            int failures = 0;
            foreach (var kind in kinds)
                foreach (var size in sizes) {
                    int n = size == "small" ? counts[kind][0] : size == "large" ? counts[kind][1] : int.Parse(size, CultureInfo.InvariantCulture);
                    var input = Family(kind, n);
                    var detect = Time(repeats, () => Detect(input));
                    foreach (double tol in tolerances) {
                        string head = string.Join(",", kind, I(input.Segments.Count), I(detect.result.crossings), I(detect.result.contacts), F(detect.median), tol.ToString("R", CultureInfo.InvariantCulture));
                        (double median, double min, double mb, ConstrainedTriangulation2Result result) run;
                        try { run = Time(repeats, () => new ConstrainedDelaunayMesh2 { SnapTolerance = tol }.Compute(input)); }
                        catch (InvalidOperationException e) { Console.WriteLine(head + ",THREW " + e.Message.Replace(',', ';')); ++failures; continue; }
                        var mesh = run.result.Mesh;
                        int vertices = mesh?.Vertices.Count ?? 0;
                        var usedPoints = mesh == null ? new Vector2d[0] : Used(mesh);
                        int used = usedPoints.Length;
                        double delaunay = used < 3 ? 0 : Time(repeats, () => new DelaunayMesh2().Compute(usedPoints)).median;
                        if (run.result.Status == Triangulation2Status.Failed) ++failures;
                        // the snap rounding alone (internal to the library): its share of the time, its rounds and the hot pixels it ends with
                        string snap = ",,";
                        if (tol > 0) {
                            var alone = Time(1, () => SnapRounding2.Apply(input, tol));
                            snap = F(alone.median) + "," + I(alone.result.Rounds) + "," + I(alone.result.Result.Points.Count);   // (points of the snapped input: the input's and the hot pixel centres made)
                        }
                        string verdict = "not judged";
                        if (run.result.Status == Triangulation2Status.Failed)
                            verdict = "Failed: " + run.result.FailureKind + ": " + (run.result.FailureDetail ?? "").Replace(',', ';');
                        else if (judge && used <= judgeLimit) {
                            var m2 = MeshCommand.FromTriangle(input); m2.SnapTolerance = tol;
                            var sw = Stopwatch.StartNew();
                            var judged = MeshCommand.Run(MeshCommand.FromInput(m2, kind + "-" + I(n), Milestone.M2));
                            // every kind of violation with its count, then the first one in full
                            verdict = (judged.Report.Ok ? "VALID" : "INVALID: " + string.Join(" ", judged.Report.Violations.GroupBy(x => x.Kind).Select(g => g.Key + " x" + I(g.Count())))
                                + "; first: " + judged.Report.Violations[0].ToString().Replace(',', ';')) + " (" + F(sw.Elapsed.TotalSeconds) + " s)";
                            if (!judged.Report.Ok) ++failures;
                        }
                        Console.WriteLine(string.Join(",", head, run.result.Status.ToString(), I(vertices), I(used), I(mesh?.TriangleCount ?? 0), I(mesh?.Subsegments.Count ?? 0),
                            F(run.median), F(run.min), F(run.mb), used == 0 ? "" : F(1000 * run.median / used, "F2"), F(delaunay), snap, verdict));
                    }
                }
            return failures == 0 ? 0 : 1;
        }
    }
}
