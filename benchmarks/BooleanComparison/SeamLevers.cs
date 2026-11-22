// Upper-bound probes of the speed study docs/research/EXACT_BOOLEAN_SPEED_LEVERS_2026-11.md: large operands with a small
// intersection seam. Measurement only: nothing here changes a result of the library, and no library code is changed.
//   seam-levers [options]
//     --k <n>                 segments around a tube (default 96; the cell size along the tube is the same)
//     --half <l1,l2,...>      half lengths of the two tubes (default 3,12,48): the seam is the same for every length, so
//                             the time above the shortest one is the work on triangles far from the seam
//     --ops union,...         operations (default all three)
//     --repeat <r>            timed runs per measurement, the fastest counts (default 3)
//     --parts a,b,...         boolean, arrangement, traversal, near, stamp (default all)
//     --no-rounding           SafeRounding = false in the boolean part
//     --check                 NotCleanReason of every result (untimed)
//     --cases <file>          instead of the tubes: the pairs A.off / B.off of the directories listed in the file (one per
//                             line, as `profile` reads them; eg the T6 set)
// The inputs are procedural: two closed tubes with a wobbling radius (no coplanar faces but the two flat end caps, which
// are far from the seam; general position), A along the
// x axis, B rotated and shifted so that it passes through A near the origin. One JSON line per measurement:
//   part "boolean":     ExactMeshBoolean with its phase timers (the rows of `profile`), per operation
//                       with --local-rounding also a prototype of the rounding's first pair search without a tree of
//                       all result triangles. Today (MeshRoundingRepair.intersecting_pairs_all, with the exact pairs of
//                       docs/design/ROUNDING_EXACT_PAIRS.md skipped): a tree of all triangles, queried from the new ones
//                       (those with a vertex that is no input vertex). The prototype: a tree of the new triangles only,
//                       joined with itself, and every other triangle's box passed through it. Both enumerate the pairs
//                       (new, any) with overlapping boxes, each once; "same_pairs" compares their numbers. "New" is by
//                       position here, on the final result; the library decides by vertex identity at the start of Apply
//                       (a crossing that rounds onto an input position is new there). Timed on this thread, without the
//                       triangle tests
//                       and "contacts": the count of positions with several vertices that ExactMeshBoolean takes of its
//                       result (a Dictionary by position, on one thread), against the same count with an open-addressing
//                       table of vertex ids (no allocation per vertex); both timed on this thread, the counts compared
//                       (the two also run on a small mesh with shared positions and -0.0 at every start: ContactsSelfCheck)
//   part "arrangement": ExactMeshArrangement of (A, B) and of each operand alone, with and without the operand
//                       self-join (SelfIntersections): the self-join's cost, which a certified clean operand would not pay
//                       ("same_pieces" compares the numbers of output pieces)
//   part "traversal":   the box-tree work alone, serially: build, self-join traversal (box pairs listed, no triangle
//                       test), the queries of A's triangles in B's tree: the most a faster (wide or packetised) tree
//                       could take off
//   part "near":        the triangles whose box meets a box of the other operand (the local set of an output-sensitive
//                       boolean) and the time to find them with the two trees
//   part "drop":        (only when named in --parts) a box [0, 1024]^3 united with a disjoint cube of edge 2^-k beside it, an
//                       untouched input solid: whether SafeRounding's removal of tiny components drops it, and what the
//                       result is called without the rounding (the finding of the SolidBooleanPipeline audit, 2026-11-22)
//   part "stamp":       the time to hash an operand's vertex bits and triangles (a 64-bit mix, and SHA-256): what a
//                       content stamp would cost that proves an operand is the unchanged clean result of an earlier boolean
//                       (timing only: a real stamp also hashes the vertex and triangle counts, so that the records cannot
//                       be read as each other)
// Run it under `taskset -c 0` with DOTNET_PROCESSOR_COUNT=1 for one thread. The report: perf/seam_levers_report.py.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

static class SeamLevers
{
    // a closed tube around the x axis, x in [-half, half], with k segments around and rings a cell apart; the radius
    // wobbles smoothly along and around the tube, so that no two neighbouring triangles of the wall are coplanar (each end cap
    // is a flat fan). Oriented outward.
    internal static DMesh3 Tube(int k, double half, double radius)
    {
        double h = 2 * Math.PI * radius / k;
        int rings = Math.Max(2, (int)Math.Ceiling(2 * half / h));
        var m = new DMesh3();
        for (int i = 0; i <= rings; ++i) {
            double x = -half + 2 * half * i / rings;
            for (int j = 0; j < k; ++j) {
                double phi = 2 * Math.PI * j / k;
                double r = radius * (1 + 0.06 * Math.Sin(1.3 * x + 0.5) * Math.Cos(3 * phi + 0.2) + 0.04 * Math.Cos(0.7 * x + 2 * phi));
                m.AppendVertex(new Vector3d(x, r * Math.Cos(phi), r * Math.Sin(phi)));
            }
        }
        for (int i = 0; i < rings; ++i)
            for (int j = 0; j < k; ++j) {
                int a = i * k + j, b = (i + 1) * k + j, c = (i + 1) * k + (j + 1) % k, d = i * k + (j + 1) % k;
                m.AppendTriangle(a, c, b);
                m.AppendTriangle(a, d, c);
            }
        int c0 = m.AppendVertex(new Vector3d(-half, 0, 0)), c1 = m.AppendVertex(new Vector3d(half, 0, 0));
        for (int j = 0; j < k; ++j) {
            m.AppendTriangle(c0, (j + 1) % k, j);
            m.AppendTriangle(c1, rings * k + j, rings * k + (j + 1) % k);
        }
        return m;
    }

    // the pair of a case: A along x, B thinner, rotated by 73 degrees about (0.3, 0.5, 0.8) and shifted, so that it passes
    // through A near the origin under about 69 degrees
    internal static void Pair(int k, double half, out DMesh3 a, out DMesh3 b)
    {
        a = Tube(k, half, 1.0);
        b = Tube(k, half, 0.8);
        MeshTransforms.Rotate(b, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(0.3, 0.5, 0.8).Normalized, 73));
        MeshTransforms.Translate(b, new Vector3d(0.11, 0.23, 0.37));
    }

    static double Volume(DMesh3 m)
    {
        double v = 0;
        foreach (int tid in m.TriangleIndices()) {
            Index3i t = m.GetTriangle(tid);
            v += m.GetVertex(t.a).Dot(m.GetVertex(t.b).Cross(m.GetVertex(t.c)));
        }
        return v / 6;
    }

    // MeshRobustRepair.NotCleanReason as text ("None" for a clean mesh)
    static string Reason(DMesh3 m)
    {
        string reason = MeshRobustRepair.NotCleanReason(m);
        return string.IsNullOrEmpty(reason) ? "None" : reason;
    }

    static List<AxisAlignedBox3d> Boxes(DMesh3 m)
    {
        var boxes = new List<AxisAlignedBox3d>(m.TriangleCount);
        foreach (int tid in m.TriangleIndices())
            boxes.Add(m.GetTriBounds(tid));
        return boxes;
    }

    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    static double Ms(Stopwatch sw) { return Math.Round(sw.Elapsed.TotalMilliseconds, 2); }

    public static int Run(string[] args)
    {
        int k = 96, repeat = 3;
        var halves = new List<double> { 3, 12, 48 };
        var ops = new List<string> { "union", "intersection", "difference" };
        var parts = new List<string> { "boolean", "arrangement", "traversal", "near", "stamp" };
        bool rounding = true, check = false, localRounding = false;
        var caseDirs = new List<string>();
        for (int i = 0; i < args.Length; ++i) {
            if (args[i] == "--k") k = int.Parse(args[++i]);
            else if (args[i] == "--half") halves = args[++i].Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToList();
            else if (args[i] == "--ops") ops = args[++i].Split(',').ToList();
            else if (args[i] == "--repeat") repeat = int.Parse(args[++i]);
            else if (args[i] == "--parts") parts = args[++i].Split(',').ToList();
            else if (args[i] == "--no-rounding") rounding = false;
            else if (args[i] == "--check") check = true;
            else if (args[i] == "--local-rounding") localRounding = true;
            else if (args[i] == "--cases") caseDirs.AddRange(System.IO.File.ReadAllLines(args[++i]).Select(l => l.Trim()).Where(l => l.Length > 0 && l.StartsWith("#") == false));
            else { Console.Error.WriteLine("unknown option " + args[i]); return 1; }
        }
        repeat = Math.Max(1, repeat);
        ContactsSelfCheck();
        var opOf = new Dictionary<string, ExactMeshBoolean.Operations> {
            ["union"] = ExactMeshBoolean.Operations.Union, ["intersection"] = ExactMeshBoolean.Operations.Intersection,
            ["difference"] = ExactMeshBoolean.Operations.Difference };

        // warm-up (JIT) on the smallest pair, every code path of the parts below
        {
            DMesh3 wa, wb;
            Pair(Math.Min(k, 48), 3, out wa, out wb);
            for (int r = 0; r < 3; ++r) {
                foreach (var o in opOf.Values)
                    new ExactMeshBoolean(wa, wb, o) { SafeRounding = rounding }.Compute();
                Arrangement(new List<DMesh3> { wa, wb }, true, out _, out _, out _);
                Arrangement(new List<DMesh3> { wa, wb }, false, out _, out _, out _);
                Traversal("warm", 0, wa, wb, 1, false);
                Near("warm", 0, wa, wb, 1, false);
                Stamp("warm", 0, wa, wb, 1, false);
                var wu = new ExactMeshBoolean(wa, wb, ExactMeshBoolean.Operations.Union) { SafeRounding = rounding };
                if (wu.Compute())
                    LocalRounding(wu.Result, wa, wb, 1);
            }
        }

        if (parts.Contains("drop")) {
            Drop();
            if (parts.Count == 1)
                return 0;
        }
        if (caseDirs.Count > 0)
            halves.Clear();
        for (int ci = 0; ci < halves.Count + caseDirs.Count; ++ci) {
            DMesh3 a, b;
            double half = 0;
            string name;
            if (ci < halves.Count) {
                half = halves[ci];
                Pair(k, half, out a, out b);
                name = string.Format(CultureInfo.InvariantCulture, "tubes:{0}:{1}", k, half);
                if (a.IsClosed() == false || b.IsClosed() == false || Volume(a) <= 0 || Volume(b) <= 0)
                    throw new InvalidOperationException("the tubes must be closed and oriented outward");
            } else {
                string dir = caseDirs[ci - halves.Count];
                name = System.IO.Path.GetFileName(dir.TrimEnd('/', '\\'));
                a = StandardMeshReader.ReadMesh(System.IO.Path.Combine(dir, "A.off"));
                b = StandardMeshReader.ReadMesh(System.IO.Path.Combine(dir, "B.off"));
                if (a == null || b == null) {
                    Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["case"] = name, ["error"] = "load failed" }));
                    continue;
                }
            }
            int tris = a.TriangleCount + b.TriangleCount;

            if (parts.Contains("boolean"))
                foreach (string op in ops) {
                    Dictionary<string, object> best = null;
                    double bestMs = double.MaxValue;
                    var times = new List<double>();
                    var hashes = new HashSet<string>();
                    string notClean = null;
                    for (int r = 0; r < repeat; ++r) {
                        Collect();
                        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                        TimeSpan pause0 = NetCompat.GcPause();
                        long alloc0 = NetCompat.AllocatedBytes();
                        var boolean = new ExactMeshBoolean(a, b, opOf[op]) { SafeRounding = rounding };
                        var sw = Stopwatch.StartNew();
                        bool ok = boolean.Compute();
                        double ms = Ms(sw);
                        times.Add(ms);
                        hashes.Add(ok ? Profile.Hash(boolean.Result) : "-");
                        if (ms >= bestMs)
                            continue;
                        bestMs = ms;
                        best = new Dictionary<string, object> {
                            ["part"] = "boolean", ["case"] = name, ["half"] = half, ["k"] = k, ["op"] = op, ["ok"] = ok,
                            ["cores"] = Environment.ProcessorCount, ["input_triangles"] = tris,
                            ["triangles"] = ok ? boolean.Result.TriangleCount : -1,
                            ["phases_ms"] = boolean.PhaseMilliseconds,
                            ["phases_alloc_mb"] = boolean.PhaseAllocatedBytes.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / 1048576.0, 1)),
                            ["alloc_mb"] = Math.Round((NetCompat.AllocatedBytes() - alloc0) / 1048576.0, 1),
                            ["gc"] = new Dictionary<string, object> { ["gen0"] = GC.CollectionCount(0) - gc0, ["gen1"] = GC.CollectionCount(1) - gc1,
                                ["gen2"] = GC.CollectionCount(2) - gc2, ["pause_ms"] = (long)(NetCompat.GcPause() - pause0).TotalMilliseconds },
                            ["counts"] = boolean.Counts,
                            ["rounding_clean"] = boolean.Rounding != null ? (object)boolean.RoundingClean : null,
                        };
                        if (check && ok)
                            best["not_clean_reason"] = notClean ?? (notClean = Reason(boolean.Result));
                        if (ok)
                            best["contacts"] = Contacts(boolean.Result, boolean.Contacts, repeat);
                        if (localRounding && ok)
                            best["local_rounding"] = LocalRounding(boolean.Result, a, b, repeat);
                    }
                    best["ms"] = times;
                    best["best_ms"] = bestMs;
                    best["hash"] = string.Join(" ", hashes);
                    Console.WriteLine(JsonSerializer.Serialize(best));
                }

            if (parts.Contains("arrangement")) {
                var sets = new List<KeyValuePair<string, List<DMesh3>>> {
                    new KeyValuePair<string, List<DMesh3>>("AB", new List<DMesh3> { a, b }),
                    new KeyValuePair<string, List<DMesh3>>("A", new List<DMesh3> { a }),
                    new KeyValuePair<string, List<DMesh3>>("B", new List<DMesh3> { b }) };
                foreach (var set in sets) {
                    var row = new Dictionary<string, object> { ["part"] = "arrangement", ["case"] = name, ["half"] = half, ["k"] = k, ["set"] = set.Key,
                        ["cores"] = Environment.ProcessorCount, ["input_triangles"] = set.Value.Sum(m => m.TriangleCount) };
                    int[] pieces = new int[2];
                    foreach (bool self in new[] { true, false }) {
                        double bestMs = double.MaxValue;
                        Dictionary<string, long> phases = null;
                        int touched = 0, selfPairs = 0;
                        for (int r = 0; r < repeat; ++r) {
                            Collect();
                            var sw = Stopwatch.StartNew();
                            ExactMeshArrangement arr = Arrangement(set.Value, self, out int n, out int t, out int sp);
                            double ms = Ms(sw);
                            pieces[self ? 0 : 1] = n;
                            if (ms < bestMs) {
                                bestMs = ms;
                                phases = arr.PhaseMilliseconds;
                                touched = t;
                                selfPairs = sp;
                            }
                        }
                        string tag = self ? "self" : "noself";
                        row[tag + "_ms"] = bestMs;
                        row[tag + "_phases_ms"] = phases;
                        row[tag + "_pieces"] = pieces[self ? 0 : 1];
                        row[tag + "_touched_triangles"] = touched;
                        if (self)
                            row["self_intersecting_pairs"] = selfPairs;
                    }
                    // (clean operands: the self-join finds nothing, so the arrangement is the same without it)
                    row["same_pieces"] = pieces[0] == pieces[1];
                    Console.WriteLine(JsonSerializer.Serialize(row));
                }
            }

            if (parts.Contains("traversal"))
                Traversal(name, half, a, b, repeat, true);
            if (parts.Contains("near"))
                Near(name, half, a, b, repeat, true);
            if (parts.Contains("stamp"))
                Stamp(name, half, a, b, repeat, true);
        }
        return 0;
    }

    // the arrangement as ExactMeshBoolean configures it (new_arrangement), with or without the operand self-join
    static ExactMeshArrangement Arrangement(List<DMesh3> meshes, bool self, out int pieces, out int touched, out int selfPairs)
    {
        var arr = new ExactMeshArrangement(meshes) { SelfIntersections = self };
        arr.Compute();
        pieces = arr.Output.Count;
        touched = arr.TriangulatedTriangles;
        selfPairs = arr.SelfIntersectingPairs;
        return arr;
    }

    // the box-tree work alone, on this thread (the tree build above TriangleBoxTree.ParallelBuildFrom uses all threads)
    static void Traversal(string name, double half, DMesh3 a, DMesh3 b, int repeat, bool print)
    {
        var row = new Dictionary<string, object> { ["part"] = "traversal", ["case"] = name, ["half"] = half, ["cores"] = Environment.ProcessorCount,
            ["input_triangles"] = a.TriangleCount + b.TriangleCount };
        List<AxisAlignedBox3d> boxesA = Boxes(a), boxesB = Boxes(b);
        double build = double.MaxValue, selfJoin = double.MaxValue, cross = double.MaxValue;
        long boxPairs = 0, crossHits = 0;
        for (int r = 0; r < repeat; ++r) {
            Collect();
            var sw = Stopwatch.StartNew();
            var treeA = new TriangleBoxTree(new List<AxisAlignedBox3d>(boxesA));
            var treeB = new TriangleBoxTree(new List<AxisAlignedBox3d>(boxesB));
            build = Math.Min(build, Ms(sw));
            sw.Restart();
            boxPairs = 0;
            var list = new List<Index2i>();
            foreach (TriangleBoxTree tree in new[] { treeA, treeB })
                foreach (Index2i task in tree.SelfPairTasks(256)) {
                    list.Clear();
                    tree.SelfPairs(task, list);
                    boxPairs += list.Count;
                }
            selfJoin = Math.Min(selfJoin, Ms(sw));
            sw.Restart();
            crossHits = 0;
            var hits = new List<int>();
            for (int i = 0; i < boxesA.Count; ++i) {
                hits.Clear();
                treeB.Query(boxesA[i], hits);
                crossHits += hits.Count;
            }
            cross = Math.Min(cross, Ms(sw));
        }
        row["tree_build_ms"] = build;
        row["self_traversal_ms"] = selfJoin;
        row["self_box_pairs"] = boxPairs;
        row["cross_query_ms"] = cross;
        row["cross_box_hits"] = crossHits;
        if (print)
            Console.WriteLine(JsonSerializer.Serialize(row));
    }

    // the local set: the triangles of each operand whose box meets a triangle box of the other one
    static void Near(string name, double half, DMesh3 a, DMesh3 b, int repeat, bool print)
    {
        var row = new Dictionary<string, object> { ["part"] = "near", ["case"] = name, ["half"] = half, ["cores"] = Environment.ProcessorCount,
            ["input_triangles"] = a.TriangleCount + b.TriangleCount };
        List<AxisAlignedBox3d> boxesA = Boxes(a), boxesB = Boxes(b);
        var treeA = new TriangleBoxTree(new List<AxisAlignedBox3d>(boxesA));
        var treeB = new TriangleBoxTree(new List<AxisAlignedBox3d>(boxesB));
        double best = double.MaxValue;
        int nearA = 0, nearB = 0;
        for (int r = 0; r < repeat; ++r) {
            var sw = Stopwatch.StartNew();
            nearA = nearB = 0;
            for (int i = 0; i < boxesA.Count; ++i)
                if (treeB.Any(boxesA[i]))
                    nearA++;
            for (int i = 0; i < boxesB.Count; ++i)
                if (treeA.Any(boxesB[i]))
                    nearB++;
            best = Math.Min(best, Ms(sw));
        }
        row["near_a"] = nearA;
        row["near_b"] = nearB;
        row["triangles_a"] = boxesA.Count;
        row["triangles_b"] = boxesB.Count;
        row["near_query_ms"] = best;
        if (print)
            Console.WriteLine(JsonSerializer.Serialize(row));
    }

    // a content stamp of both operands: every vertex coordinate's bits and every triangle, in id order
    static void Stamp(string name, double half, DMesh3 a, DMesh3 b, int repeat, bool print)
    {
        var row = new Dictionary<string, object> { ["part"] = "stamp", ["case"] = name, ["half"] = half, ["cores"] = Environment.ProcessorCount,
            ["input_triangles"] = a.TriangleCount + b.TriangleCount };
        double mix = double.MaxValue, sha = double.MaxValue;
        ulong h = 0;
        string digest = null;
        for (int r = 0; r < repeat; ++r) {
            var sw = Stopwatch.StartNew();
            h = 1469598103934665603UL;
            foreach (DMesh3 m in new[] { a, b }) {
                foreach (int vid in m.VertexIndices()) {
                    Vector3d v = m.GetVertex(vid);
                    h = Mix(h, (ulong)vid);
                    h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(v.x));
                    h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(v.y));
                    h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(v.z));
                }
                foreach (int tid in m.TriangleIndices()) {
                    Index3i t = m.GetTriangle(tid);
                    h = Mix(h, ((ulong)(uint)tid << 32) | (uint)t.a);
                    h = Mix(h, ((ulong)(uint)t.b << 32) | (uint)t.c);
                }
            }
            mix = Math.Min(mix, Ms(sw));
            sw.Restart();
            using (var hash = System.Security.Cryptography.SHA256.Create()) {
                var buffer = new byte[1 << 16];
                int n = 0;
                foreach (DMesh3 m in new[] { a, b }) {
                    foreach (int vid in m.VertexIndices()) {
                        Vector3d v = m.GetVertex(vid);
                        if (n + 28 > buffer.Length) { hash.TransformBlock(buffer, 0, n, null, 0); n = 0; }
                        Put(buffer, ref n, vid);
                        Put(buffer, ref n, BitConverter.DoubleToInt64Bits(v.x));
                        Put(buffer, ref n, BitConverter.DoubleToInt64Bits(v.y));
                        Put(buffer, ref n, BitConverter.DoubleToInt64Bits(v.z));
                    }
                    foreach (int tid in m.TriangleIndices()) {
                        Index3i t = m.GetTriangle(tid);
                        if (n + 16 > buffer.Length) { hash.TransformBlock(buffer, 0, n, null, 0); n = 0; }
                        Put(buffer, ref n, tid); Put(buffer, ref n, t.a); Put(buffer, ref n, t.b); Put(buffer, ref n, t.c);
                    }
                }
                hash.TransformFinalBlock(buffer, 0, n);
                digest = BitConverter.ToString(hash.Hash, 0, 8);
            }
            sha = Math.Min(sha, Ms(sw));
        }
        row["mix64_ms"] = mix;
        row["mix64"] = h.ToString("x16");
        row["sha256_ms"] = sha;
        row["sha256_prefix"] = digest;
        if (print)
            Console.WriteLine(JsonSerializer.Serialize(row));
    }

    static ulong Mix(ulong h, ulong x)
    {
        h ^= x;
        h *= 0x9E3779B97F4A7C15UL;
        return h ^ (h >> 29);
    }

    static void Put(byte[] buffer, ref int n, int x)
    {
        buffer[n++] = (byte)x; buffer[n++] = (byte)(x >> 8); buffer[n++] = (byte)(x >> 16); buffer[n++] = (byte)(x >> 24);
    }

    static void Put(byte[] buffer, ref int n, long x)
    {
        Put(buffer, ref n, (int)x);
        Put(buffer, ref n, (int)(x >> 32));
    }

    // the candidate pairs of the rounding's first pair search (boxes overlap, at least one of the two triangles new), found
    // the way MeshRoundingRepair finds them (a tree of all triangles, queried from the new ones) and by a tree of the new triangles only
    static Dictionary<string, object> LocalRounding(DMesh3 result, DMesh3 a, DMesh3 b, int repeat)
    {
        var inputPositions = new HashSet<Vector3d>();
        foreach (DMesh3 m in new[] { a, b })
            foreach (int vid in m.VertexIndices())
                inputPositions.Add(m.GetVertex(vid));
        var boxes = new List<AxisAlignedBox3d>(result.TriangleCount);
        var isNew = new List<bool>(result.TriangleCount);
        var newBoxes = new List<AxisAlignedBox3d>();
        foreach (int tid in result.TriangleIndices()) {
            Index3i t = result.GetTriangle(tid);
            bool n = inputPositions.Contains(result.GetVertex(t.a)) == false || inputPositions.Contains(result.GetVertex(t.b)) == false
                || inputPositions.Contains(result.GetVertex(t.c)) == false;
            boxes.Add(result.GetTriBounds(tid));
            isNew.Add(n);
            if (n)
                newBoxes.Add(boxes[boxes.Count - 1]);
        }
        double fullBuild = double.MaxValue, fullJoin = double.MaxValue, localBuild = double.MaxValue, localJoin = double.MaxValue, localScan = double.MaxValue;
        long fullPairs = 0, localPairs = 0;
        var list = new List<Index2i>();
        var hits = new List<int>();
        for (int r = 0; r < repeat; ++r) {
            Collect();
            var sw = Stopwatch.StartNew();
            var tree = new TriangleBoxTree(new List<AxisAlignedBox3d>(boxes));
            fullBuild = Math.Min(fullBuild, Ms(sw));
            sw.Restart();
            fullPairs = 0;
            for (int i = 0; i < boxes.Count; ++i)
                if (isNew[i]) {
                    hits.Clear();
                    tree.Query(boxes[i], hits);
                    // (a pair of two new triangles once, from the smaller index, as intersecting_pairs_all takes it)
                    foreach (int j in hits)
                        if (j != i && (j > i || isNew[j] == false))
                            fullPairs++;
                }
            fullJoin = Math.Min(fullJoin, Ms(sw));

            sw.Restart();
            var small = new TriangleBoxTree(new List<AxisAlignedBox3d>(newBoxes));
            localBuild = Math.Min(localBuild, Ms(sw));
            sw.Restart();
            localPairs = 0;
            foreach (Index2i task in small.SelfPairTasks(256)) {
                list.Clear();
                small.SelfPairs(task, list);
                localPairs += list.Count;
            }
            localJoin = Math.Min(localJoin, Ms(sw));
            sw.Restart();
            for (int i = 0; i < boxes.Count; ++i)
                if (isNew[i] == false) {
                    hits.Clear();
                    small.Query(boxes[i], hits);
                    localPairs += hits.Count;
                }
            localScan = Math.Min(localScan, Ms(sw));
        }
        return new Dictionary<string, object> {
            ["result_triangles"] = boxes.Count, ["new_triangles"] = newBoxes.Count,
            ["full_build_ms"] = fullBuild, ["full_query_ms"] = fullJoin, ["full_pairs"] = fullPairs,
            ["local_build_ms"] = localBuild, ["local_join_ms"] = localJoin, ["local_scan_ms"] = localScan, ["local_pairs"] = localPairs,
            ["same_pairs"] = fullPairs == localPairs };
    }

    // the two contact counts on a mesh that has contacts: two tetrahedra with a corner at one position (once as 0.0, once as
    // -0.0), a third with two corners on positions of the first, and one apart; 3 positions hold several vertices
    static void ContactsSelfCheck()
    {
        var m = new DMesh3();
        Action<Vector3d, Vector3d, Vector3d, Vector3d> tet = (a, b, c, d) => {
            int ia = m.AppendVertex(a), ib = m.AppendVertex(b), ic = m.AppendVertex(c), id = m.AppendVertex(d);
            m.AppendTriangle(ia, ic, ib); m.AppendTriangle(ia, ib, id); m.AppendTriangle(ib, ic, id); m.AppendTriangle(ic, ia, id);
        };
        tet(new Vector3d(0.0, 0.0, 0.0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1));
        tet(new Vector3d(-0.0, 0.0, -0.0), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, -1));
        tet(new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(2, 2, 0), new Vector3d(2, 2, 2));
        tet(new Vector3d(5, 5, 5), new Vector3d(6, 5, 5), new Vector3d(5, 6, 5), new Vector3d(5, 5, 6));
        Dictionary<string, object> counts = Contacts(m, 3, 1);
        if ((bool)counts["same_count"] == false || (int)counts["contacts"] != 3)
            throw new InvalidOperationException("the contact counts differ on the self-check mesh");
    }

    // the contact count of ExactMeshBoolean (count_contacts: a dictionary by position) and the same count with a flat table
    static Dictionary<string, object> Contacts(DMesh3 mesh, int expected, int repeat)
    {
        double dictMs = double.MaxValue, flatMs = double.MaxValue;
        int byDict = 0, byFlat = 0;
        for (int r = 0; r < repeat; ++r) {
            var sw = Stopwatch.StartNew();
            var seen = new Dictionary<Vector3d, int>();
            byDict = 0;
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                int n;
                seen.TryGetValue(p, out n);
                if (n == 1)
                    byDict++;
                seen[p] = n + 1;
            }
            dictMs = Math.Min(dictMs, Ms(sw));

            sw.Restart();
            int size = 16;
            while (size < 2 * mesh.MaxVertexID)
                size *= 2;
            var slot = new int[size];           // vertex id + 1 of the first vertex at the position, 0: free
            var several = new bool[size];
            byFlat = 0;
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                // (+ 0.0: -0.0 and 0.0 are one position, as in the dictionary)
                ulong h = Mix(Mix(Mix(1469598103934665603UL, (ulong)BitConverter.DoubleToInt64Bits(p.x + 0.0)),
                    (ulong)BitConverter.DoubleToInt64Bits(p.y + 0.0)), (ulong)BitConverter.DoubleToInt64Bits(p.z + 0.0));
                int i = (int)(h & (ulong)(size - 1));
                for (; ; i = (i + 1) & (size - 1)) {
                    if (slot[i] == 0) {
                        slot[i] = v + 1;
                        break;
                    }
                    Vector3d q = mesh.GetVertex(slot[i] - 1);
                    if (q.x == p.x && q.y == p.y && q.z == p.z) {
                        if (several[i] == false) {
                            several[i] = true;
                            byFlat++;
                        }
                        break;
                    }
                }
            }
            flatMs = Math.Min(flatMs, Ms(sw));
        }
        return new Dictionary<string, object> { ["vertices"] = mesh.VertexCount, ["dictionary_ms"] = dictMs, ["flat_ms"] = flatMs,
            ["same_count"] = byDict == byFlat && byDict == expected, ["contacts"] = byDict };
    }

    static DMesh3 Box(Vector3d min, Vector3d max)
    {
        var m = new DMesh3();
        for (int i = 0; i < 8; ++i)
            m.AppendVertex(new Vector3d((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
        int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
        foreach (int[] q in quads) {
            m.AppendTriangle(q[0], q[1], q[2]);
            m.AppendTriangle(q[0], q[2], q[3]);
        }
        return m;
    }

    // a large box and a small cube that do not meet: the union is both, unchanged
    static void Drop()
    {
        DMesh3 big = Box(Vector3d.Zero, new Vector3d(1024, 1024, 1024));
        for (int k = 0; k <= 12; ++k) {
            double e = Math.Pow(2, -k);
            DMesh3 small = Box(new Vector3d(1100, 0, 0), new Vector3d(1100 + e, e, e));
            var row = new Dictionary<string, object> { ["part"] = "drop", ["cube_edge"] = e, ["k"] = k };
            foreach (bool safe in new[] { true, false }) {
                var boolean = new ExactMeshBoolean(big, small, ExactMeshBoolean.Operations.Union) { SafeRounding = safe };
                bool ok = boolean.Compute();
                string tag = safe ? "rounded" : "unrounded";
                row[tag + "_triangles"] = ok ? boolean.Result.TriangleCount : -1;
                row[tag + "_not_clean_reason"] = ok ? Reason(boolean.Result) : "failed";
                if (safe) {
                    row["removed_components"] = boolean.Rounding.RemovedComponents;
                    row["rounding_clean"] = boolean.RoundingClean;
                    row["initial_degenerate"] = boolean.Rounding.InitialDegenerate;
                    row["cube_kept"] = ok && boolean.Result.TriangleCount == 24;
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
    }
}
