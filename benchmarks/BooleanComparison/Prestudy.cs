// Exact Boolean 3.0 pre-study (docs/design/EXACT_BOOLEAN_30_PRESTUDY.md): probes on benchmark level, no library change.
//   prestudy-memory <case dir> <op> [--no-boolean] [--no-arrangement-probe]
//       one operation per process: the arrangement alone (retained heap after Compute, points by representation,
//       pieces), then the full boolean (allocations per phase, GC, peak working set). One JSON line.
//   prestudy-exact <case dir> <op>
//       exact output against rounded output: time with SafeRounding (default), without, and with ComputeExactVertices
//       (SafeRounding off); bit sizes of the exact result vertices. One JSON line.
//   prestudy-csg <kind> <n> <seed>
//       a procedural CSG tree (kind: boxes | mixed) of n rotated primitives, evaluated chained (n - 1 binary booleans,
//       each rounded) and in one pass (one arrangement, one rounding); time, roundings, clean status, volume and the
//       distance of each result to the exact one-pass surface. One JSON line per variant.
//   prestudy-csg-thingi <a.off> <b.off> <c.off>
//       the same on three Thingi10K models (A, B rotated and shifted, C rotated the other way): (A u B) - C.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using g3;
using gs;

static class Prestudy
{
    static readonly Dictionary<string, ExactMeshBoolean.Operations> opOf = new Dictionary<string, ExactMeshBoolean.Operations> {
        ["union"] = ExactMeshBoolean.Operations.Union, ["intersection"] = ExactMeshBoolean.Operations.Intersection,
        ["difference"] = ExactMeshBoolean.Operations.Difference };

    public static int Run(string mode, string[] args)
    {
        switch (mode) {
            case "prestudy-memory": return Memory(args[0], args[1], args.Contains("--no-boolean"), args.Contains("--no-arrangement-probe"));
            case "prestudy-exact": return Exact(args[0], args[1]);
            case "prestudy-csg": return Csg(args[0], int.Parse(args[1]), int.Parse(args[2]));
            case "prestudy-csg-thingi": return CsgThingi(args[0], args[1], args[2]);
        }
        Console.Error.WriteLine("unknown prestudy mode " + mode);
        return 1;
    }

    static void Warm()
    {
        DMesh3 w1 = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        DMesh3 w2 = new DMesh3(w1);
        MeshTransforms.Translate(w2, new Vector3d(0.3, 0.2, 0.1));
        for (int k = 0; k < 3; ++k)
            foreach (var o in opOf.Values)
                new ExactMeshBoolean(w1, w2, o).Compute();
    }

    static long Retained()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return GC.GetTotalMemory(true);
    }

    static double Mb(long b) { return Math.Round(b / 1048576.0, 1); }

    static int Memory(string dir, string op, bool noBoolean, bool noProbe)
    {
        DMesh3 a = StandardMeshReader.ReadMesh(Path.Combine(dir, "A.off")), b = StandardMeshReader.ReadMesh(Path.Combine(dir, "B.off"));
        var row = new Dictionary<string, object> { ["case"] = Path.GetFileName(dir.TrimEnd('/')), ["op"] = op,
            ["tris_a"] = a.TriangleCount, ["tris_b"] = b.TriangleCount };
        long baseHeap = Retained();
        row["inputs_mb"] = Mb(baseHeap);
        // the arrangement alone (as ExactMeshBoolean builds it; open inputs are not reoriented here)
        if (noProbe == false) {
            long alloc0 = GC.GetTotalAllocatedBytes(true);
            var sw = Stopwatch.StartNew();
            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true };
            arr.Compute();
            row["arr_ms"] = sw.ElapsedMilliseconds;
            row["arr_alloc_mb"] = Mb(GC.GetTotalAllocatedBytes(true) - alloc0);
            long heap = Retained();
            row["arr_retained_mb"] = Mb(heap - baseHeap);
            int n = arr.PointCount, fixedW = 0, big = 0, none = 0, inputP = 0;
            for (int id = 0; id < n; ++id) {
                if (arr.KindOf(id) == ExactMeshArrangement.PointKind.Input) inputP++;
                if (arr.IsExactFixed(id)) fixedW++;
            }
            row["points"] = n;
            row["input_points"] = inputP;
            row["fixed_points"] = fixedW;
            row["pieces"] = arr.Output.Count;
            row["input_triangles"] = arr.Triangles.Count;
            int touched = 0;
            foreach (var t in arr.Triangles)
                if (t.Segments.Count > 0 || t.Points.Count > 0) touched++;
            row["touched_triangles"] = touched;
            row["bytes_per_point"] = n > 0 ? Math.Round((heap - baseHeap) / (double)n, 1) : 0;
            GC.KeepAlive(arr);
            _ = big; _ = none;
        }
        Retained();
        if (noBoolean == false) {
            Warm();
            Retained();
            long alloc0 = GC.GetTotalAllocatedBytes(true);
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            TimeSpan pause0 = GC.GetTotalPauseDuration();
            var boolean = new ExactMeshBoolean(a, b, opOf[op]);
            var sw = Stopwatch.StartNew();
            bool ok = boolean.Compute();
            row["ms"] = sw.ElapsedMilliseconds;
            row["ok"] = ok;
            row["alloc_mb"] = Mb(GC.GetTotalAllocatedBytes(true) - alloc0);
            row["phases_ms"] = boolean.PhaseMilliseconds;
            row["phases_alloc_mb"] = boolean.PhaseAllocatedBytes.ToDictionary(kv => kv.Key, kv => Mb(kv.Value));
            row["gc"] = new Dictionary<string, object> { ["gen0"] = GC.CollectionCount(0) - gc0, ["gen1"] = GC.CollectionCount(1) - gc1,
                ["gen2"] = GC.CollectionCount(2) - gc2, ["pause_ms"] = (long)(GC.GetTotalPauseDuration() - pause0).TotalMilliseconds };
            row["counts"] = boolean.Counts;
            row["result_tris"] = ok ? boolean.Result.TriangleCount : -1;
        }
        row["peak_ws_mb"] = Mb(Process.GetCurrentProcess().PeakWorkingSet64);
        row["gc_heap_peak_mb"] = Mb(GC.GetGCMemoryInfo(GCKind.Any).HeapSizeBytes);
        Console.WriteLine(JsonSerializer.Serialize(row));
        return 0;
    }

    // x / w is a double (w > 0): w a power of two and the odd part of x within 53 bits (exponent range not checked)
    static bool IsDouble(BigInteger x, BigInteger w)
    {
        if (x.IsZero) return true;
        if ((w & (w - 1)).IsZero == false) return false;
        BigInteger ax = BigInteger.Abs(x);
        while (ax.IsEven) ax >>= 1;
        return ax.GetBitLength() <= 53;
    }

    static int Bits(BigInteger v) { return v.IsZero ? 0 : (int)BigInteger.Abs(v).GetBitLength(); }

    static int Exact(string dir, string op)
    {
        DMesh3 a = StandardMeshReader.ReadMesh(Path.Combine(dir, "A.off")), b = StandardMeshReader.ReadMesh(Path.Combine(dir, "B.off"));
        Warm();
        var row = new Dictionary<string, object> { ["case"] = Path.GetFileName(dir.TrimEnd('/')), ["op"] = op };
        foreach (string variant in new[] { "rounded", "unrounded", "exact" }) {
            Retained();
            var boolean = new ExactMeshBoolean(a, b, opOf[op]) { SafeRounding = variant == "rounded", ComputeExactVertices = variant == "exact" };
            long alloc0 = GC.GetTotalAllocatedBytes(true);
            var sw = Stopwatch.StartNew();
            bool ok = boolean.Compute();
            row[variant + "_ms"] = sw.ElapsedMilliseconds;
            row[variant + "_alloc_mb"] = Mb(GC.GetTotalAllocatedBytes(true) - alloc0);
            row[variant + "_phases_ms"] = boolean.PhaseMilliseconds;
            if (ok == false) { row[variant + "_error"] = boolean.Error; continue; }
            row[variant + "_tris"] = boolean.Result.TriangleCount;
            if (variant == "rounded") {
                row["rounding_clean"] = boolean.RoundingClean;
                row["rounded_not_clean_reason"] = MeshRobustRepair.NotCleanReason(boolean.Result);
            }
            if (variant == "unrounded")
                row["unrounded_not_clean_reason"] = MeshRobustRepair.NotCleanReason(boolean.Result);
            if (variant == "exact") {
                ExactPoint3[] v = boolean.ResultExactVertices;
                int maxNum = 0, maxW = 0, wOne = 0, inputLike = 0;
                long sumBits = 0;
                foreach (ExactPoint3 p in v) {
                    int nb = Math.Max(Bits(p.X), Math.Max(Bits(p.Y), Bits(p.Z))), wb = Bits(p.W);
                    maxNum = Math.Max(maxNum, nb); maxW = Math.Max(maxW, wb);
                    sumBits += Bits(p.X) + Bits(p.Y) + Bits(p.Z) + wb;
                    if (p.W.IsOne) wOne++;
                    // a point whose coordinates are all doubles (as the input points are)
                    if (IsDouble(p.X, p.W) && IsDouble(p.Y, p.W) && IsDouble(p.Z, p.W)) inputLike++;
                }
                row["exact_vertices"] = v.Length;
                row["exact_double_vertices"] = inputLike;
                row["exact_w_one"] = wOne;
                row["exact_max_num_bits"] = maxNum;
                row["exact_max_w_bits"] = maxW;
                row["exact_mean_bits_per_vertex"] = v.Length > 0 ? Math.Round(sumBits / (double)v.Length, 1) : 0;
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(row));
        return 0;
    }

    // ---------------------------------------------------------------- CSG trees

    static DMesh3 Primitive(string kind, Random r, int k)
    {
        DMesh3 m;
        string shape = kind == "boxes" ? "box" : new[] { "box", "sphere", "cylinder" }[k % 3];
        if (shape == "box")
            m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.3 + 0.4 * r.NextDouble(), 0.3 + 0.4 * r.NextDouble(), 0.3 + 0.4 * r.NextDouble())) }.Generate().MakeDMesh();
        else if (shape == "sphere")
            m = new Sphere3Generator_NormalizedCube() { Radius = 0.35 + 0.3 * r.NextDouble(), EdgeVertices = 12 }.Generate().MakeDMesh();
        else
            m = new CappedCylinderGenerator() { BaseRadius = (float)(0.25 + 0.2 * r.NextDouble()), TopRadius = (float)(0.25 + 0.2 * r.NextDouble()), Height = 1.2f, Slices = 32 }.Generate().MakeDMesh();
        Quaterniond q = new Quaterniond(new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5).Normalized, 360 * r.NextDouble());
        MeshTransforms.Rotate(m, Vector3d.Zero, q);
        MeshTransforms.Translate(m, new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5) * 0.8);
        return m;
    }

    // op sequence of the tree: left-deep, ((P0 op1 P1) op2 P2) ...; ops cycle union, union, difference, intersection
    static ExactMeshBoolean.Operations OpAt(int k)
    {
        switch (k % 4) {
            case 0: case 1: return ExactMeshBoolean.Operations.Union;
            case 2: return ExactMeshBoolean.Operations.Difference;
            default: return ExactMeshBoolean.Operations.Intersection;
        }
    }

    // the indicator of the left-deep tree on the winding numbers of all inputs
    static Func<int[], bool> TreePredicate(IList<ExactMeshBoolean.Operations> ops)
    {
        return (w) => {
            bool v = w[0] > 0;
            for (int k = 1; k < w.Length; ++k) {
                bool x = w[k] > 0;
                switch (ops[k - 1]) {
                    case ExactMeshBoolean.Operations.Union: v = v || x; break;
                    case ExactMeshBoolean.Operations.Intersection: v = v && x; break;
                    default: v = v && x == false; break;
                }
            }
            return v;
        };
    }

    static int Csg(string kind, int n, int seed)
    {
        var r = new Random(seed);
        var prims = new List<DMesh3>();
        for (int k = 0; k < n; ++k)
            prims.Add(Primitive(kind, r, k));
        var ops = Enumerable.Range(1, n - 1).Select(OpAt).ToList();
        Warm();
        CompareTree($"{kind}_{n}_{seed}", prims, ops);
        return 0;
    }

    static int CsgThingi(string fa, string fb, string fc)
    {
        DMesh3 a = Program.Load(fa), b = Program.Load(fb), c = Program.Load(fc);
        var prims = new List<DMesh3>();
        foreach (DMesh3 m in new[] { a, b, c }) {
            // unit box normalized, as the thingi-pairs class
            AxisAlignedBox3d box = m.CachedBounds;
            MeshTransforms.Translate(m, -box.Center);
            MeshTransforms.Scale(m, 1.0 / box.MaxDim);
            prims.Add(m);
        }
        MeshTransforms.Rotate(prims[1], Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
        MeshTransforms.Translate(prims[1], new Vector3d(0.2, 0.1, 0.05));
        MeshTransforms.Rotate(prims[2], Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(0, 1, 1).Normalized, -35));
        MeshTransforms.Translate(prims[2], new Vector3d(-0.1, 0.15, 0.1));
        Warm();
        CompareTree($"thingi_{Path.GetFileNameWithoutExtension(fa)}_{Path.GetFileNameWithoutExtension(fb)}_{Path.GetFileNameWithoutExtension(fc)}",
            prims, new List<ExactMeshBoolean.Operations> { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Difference });
        return 0;
    }

    static double Volume(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

    // one-sided distances: max and mean over the vertices of m to the surface of reference
    static void Distance(DMesh3 m, DMeshAABBTree3 reference, out double max, out double mean)
    {
        max = 0; double sum = 0; int n = 0;
        foreach (int vid in m.VertexIndices()) {
            Vector3d p = m.GetVertex(vid);
            int t = reference.FindNearestTriangle(p);
            if (t < 0) continue;
            double d = Math.Sqrt(MeshQueries.TriDistanceSqr(reference.Mesh, t, p));
            max = Math.Max(max, d); sum += d; n++;
        }
        mean = n > 0 ? sum / n : 0;
    }

    static void CompareTree(string name, List<DMesh3> prims, List<ExactMeshBoolean.Operations> ops)
    {
        // reference: the one pass with exact vertices, rounded to nearest (no repair): within half an ulp per coordinate
        var reference = new ExactMeshBoolean(prims, TreePredicate(ops)) { SafeRounding = false };
        if (reference.Compute() == false) {
            Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object> { ["case"] = name, ["error"] = reference.Error }));
            return;
        }
        DMesh3 refMesh = reference.Result;
        double diag = refMesh.TriangleCount > 0 ? refMesh.CachedBounds.Diagonal.Length : 1;
        var refTree = new DMeshAABBTree3(refMesh, true);
        double refVolume = Volume(refMesh);
        foreach (string variant in new[] { "chained", "onepass" }) {
            Retained();
            var row = new Dictionary<string, object> { ["case"] = name, ["variant"] = variant, ["inputs"] = prims.Count,
                ["input_tris"] = prims.Sum(m => m.TriangleCount) };
            var sw = Stopwatch.StartNew();
            DMesh3 result;
            int roundings = 0, edits = 0, notClean = 0;
            long roundingMs = 0;
            var stepClean = new List<bool>();
            bool failed = false;
            if (variant == "chained") {
                result = prims[0];
                for (int k = 1; k < prims.Count && failed == false; ++k) {
                    var op = new ExactMeshBoolean(result, prims[k], ops[k - 1]);
                    if (op.Compute() == false) { row["error"] = op.Error; failed = true; break; }
                    roundings++;
                    roundingMs += op.PhaseMilliseconds.TryGetValue("rounding", out long rm) ? rm : 0;
                    edits += (int)op.Counts["rounding_edits"];
                    stepClean.Add(op.RoundingClean);
                    if (op.RoundingClean == false) notClean++;
                    result = op.Result;
                }
            } else {
                var op = new ExactMeshBoolean(prims, TreePredicate(ops));
                if (op.Compute() == false) { row["error"] = op.Error; failed = true; result = null; }
                else {
                    roundings = 1;
                    roundingMs = op.PhaseMilliseconds.TryGetValue("rounding", out long rm) ? rm : 0;
                    edits = (int)op.Counts["rounding_edits"];
                    stepClean.Add(op.RoundingClean);
                    if (op.RoundingClean == false) notClean++;
                    result = op.Result;
                }
            }
            row["ms"] = sw.ElapsedMilliseconds;
            if (failed == false) {
                row["roundings"] = roundings;
                row["rounding_ms"] = roundingMs;
                row["rounding_edits"] = edits;
                row["steps_not_clean"] = notClean;
                row["tris"] = result.TriangleCount;
                row["not_clean_reason"] = MeshRobustRepair.NotCleanReason(result);
                double vol = Volume(result);
                row["volume_rel_err"] = refVolume != 0 ? Math.Abs(vol - refVolume) / Math.Abs(refVolume) : Math.Abs(vol);
                double maxTo, meanTo, maxFrom, meanFrom;
                Distance(result, refTree, out maxTo, out meanTo);
                Distance(refMesh, new DMeshAABBTree3(result, true), out maxFrom, out meanFrom);
                row["hausdorff_rel"] = Math.Max(maxTo, maxFrom) / diag;
                row["mean_dist_rel"] = meanTo / diag;
            }
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
    }
}
