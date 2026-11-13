// Benchmark of ContinuousCollision and MeshSweepCollision (docs/design/CCD.md).
//   dotnet run -c Release -- synthetic            random and near-contact queries (filter vs interval path), sphere sweeps
//   dotnet run -c Release -- sample <dir> [ms]    the Apache-2.0 sample of the ToI dataset, fetched by fetch_sample.sh
//                                                 (false negatives, false positives, time per query against the ground truth)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "synthetic") { Synthetic(); return 0; }
        if (args.Length >= 2 && args[0] == "sample") return Sample(args[1], args.Length >= 3 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 0);
        Console.Error.WriteLine("usage: synthetic | sample <dir> [minSeparation]");
        return 2;
    }

    static Vector3d V(double x, double y, double z) => new Vector3d(x, y, z);

    // ---- synthetic: the queries of the stage-1 probe (CCD.md section 7.1), now through the library
    static void Synthetic()
    {
        var rnd = new Random(1);
        const int n = 20000;
        Console.WriteLine("kind,offset,class,path,us_per_query,boxes_per_query,collisions");
        foreach (bool easy in new[] { false, true })
            foreach (double offset in new[] { 0.0, 632117.25 })
                foreach (bool vf in new[] { true, false }) {
                    var qs = new List<double[]>();
                    for (int i = 0; i < n; ++i) qs.Add(query(rnd, vf, easy, offset));
                    foreach (bool intervals in new[] { false, true }) {
                        var solver = new CcdSolver { ForceIntervals = intervals };
                        var o = new CcdOptions();
                        long boxes = 0; int hits = 0;
                        // warm-up
                        for (int i = 0; i < 200; ++i) run(solver, qs[i], vf, o);
                        var sw = Stopwatch.StartNew();
                        foreach (var q in qs) { var r = run(solver, q, vf, o); boxes += r.Iterations; if (r.Status != CcdStatus.NoCollision) ++hits; }
                        sw.Stop();
                        Console.WriteLine(FormattableString.Invariant($"{(vf ? "VF" : "EE")},{offset},{(easy ? "random" : "near")},{(intervals ? "interval" : "filter")},{sw.Elapsed.TotalMilliseconds * 1000 / n:F2},{boxes / (double)n:F1},{hits}"));
                    }
                }
        Console.WriteLine();
        Console.WriteLine("sweep,tightening,triangles_each,candidate_pairs,serial_ms,parallel_ms,earliest_toi,earliest_near,max_iterations,mean_iterations");
        foreach (int tighten in new[] { 256, 1024, 4096 })
        foreach (int ev in new[] { 8, 16, 32 }) {
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = ev }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Translate(b, V(2.1, 0.2, 0.1));
            var endA = new Vector3d[a.MaxVertexID];
            foreach (int vid in a.VertexIndices()) { Vector3d p = a.GetVertex(vid); endA[vid] = p + V(0.6, 0.05 * p.y, 0.02 * p.z); }
            var endB = new Vector3d[b.MaxVertexID];
            foreach (int vid in b.VertexIndices()) endB[vid] = b.GetVertex(vid) - V(0.3, 0, 0);
            foreach (double ms in new[] { 0.0, 0.01 }) {
                double[] t = new double[2];
                MeshSweepCollision last = null;
                for (int k = 0; k < 2; ++k) {
                    var s = new MeshSweepCollision(a, endA, b, endB) { Parallel = k == 1, CollectAll = true, Options = new CcdOptions { MinSeparation = ms, MaxTighteningIterations = tighten } };
                    s.Compute();     // warm-up
                    var sw = Stopwatch.StartNew();
                    s.Compute();
                    t[k] = sw.Elapsed.TotalMilliseconds;
                    last = s;
                }
                Console.WriteLine(FormattableString.Invariant($"ms={ms},{tighten},{a.TriangleCount},{last.CandidatePairs},{t[0]:F1},{t[1]:F1},{last.EarliestTimeOfImpact:R},{last.EarliestNearContactTime:R},{last.Contacts.Max(c => c.Result.Iterations)},{last.Contacts.Average(c => c.Result.Iterations):F1}"));
            }
        }
    }

    static CcdResult run(CcdSolver s, double[] q, bool vf, CcdOptions o)
    {
        s.Set(V(q[0], q[1], q[2]), V(q[3], q[4], q[5]), V(q[6], q[7], q[8]), V(q[9], q[10], q[11]),
              V(q[12], q[13], q[14]), V(q[15], q[16], q[17]), V(q[18], q[19], q[20]), V(q[21], q[22], q[23]));
        return s.Solve(vf, o);
    }

    // q[0..11] the four points at t = 0, q[12..23] at t = 1 (as the stage-1 probe)
    static double[] query(Random rnd, bool vf, bool easy, double offset)
    {
        var q = new double[24];
        for (int j = 0; j < 24; ++j) q[j] = rnd.NextDouble() - 0.5;
        if (easy) {
            for (int j = 12; j < 24; ++j) q[j] = q[j - 12] + (rnd.NextDouble() - 0.5) * 0.1;
        } else if (vf) {
            for (int ax = 0; ax < 3; ++ax) {
                double cen = (q[3 + ax] + q[6 + ax] + q[9 + ax]) / 3;
                q[ax] = cen + (ax == 2 ? 0.6 : 0) + (rnd.NextDouble() - 0.5) * 0.4;
                q[12 + ax] = cen - (ax == 2 ? 0.6 : 0) + (rnd.NextDouble() - 0.5) * 0.4;
                for (int k = 1; k < 4; ++k) q[12 + 3 * k + ax] = q[3 * k + ax] + (rnd.NextDouble() - 0.5) * 0.05;
            }
        } else {
            for (int k = 2; k < 4; ++k) for (int ax = 0; ax < 3; ++ax) q[12 + 3 * k + ax] = q[3 * k + ax] + (rnd.NextDouble() - 0.5) * 0.05;
            for (int ax = 0; ax < 3; ++ax) {
                double m = (q[6 + ax] + q[9 + ax]) / 2, s = ax == 2 ? 0.6 : 0;
                q[ax] = m + s + (rnd.NextDouble() - .5) * .6; q[3 + ax] = m + s - (rnd.NextDouble() - .5) * .6;
                q[12 + ax] = q[ax] - 2 * s; q[15 + ax] = q[3 + ax] - 2 * s;
            }
        }
        for (int j = 0; j < 24; ++j) q[j] += offset;
        return q;
    }

    // ---- sample: the ToI dataset sample (queries/*.csv: 8 rows per query, 3 rationals per row; mma_bool/*.json)
    static int Sample(string dir, double ms)
    {
        var files = Directory.GetFiles(dir, "*.csv", SearchOption.AllDirectories).Where(f => f.Contains(Path.DirectorySeparatorChar + "queries" + Path.DirectorySeparatorChar)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count == 0) { Console.Error.WriteLine($"no queries under {dir} (run fetch_sample.sh first)"); return 2; }
        Console.WriteLine("scene,file,kind,path,queries,collide_truth,false_negatives,false_positives,iteration_limits,us_per_query,us_max,boxes_mean,boxes_max");
        long fnTotal = 0;
        foreach (string f in files) {
            string scene = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f)));
            string name = Path.GetFileNameWithoutExtension(f);
            bool vf = name.EndsWith("vf");
            string truthFile = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(f)), "mma_bool", name + "_mma_bool.json");
            bool[] truth = File.ReadAllText(truthFile).Trim().TrimStart('[').TrimEnd(']').Split(',').Select(s => s.Trim() == "true").ToArray();
            List<double[]> qs = read_queries(f);
            if (qs.Count != truth.Length) { Console.Error.WriteLine($"{f}: {qs.Count} queries, {truth.Length} truths"); return 1; }
            foreach (bool intervals in new[] { false, true }) {
                var solver = new CcdSolver { ForceIntervals = intervals };
                var o = new CcdOptions { MinSeparation = ms };
                int fn = 0, fp = 0, lim = 0, coll = 0;
                long boxes = 0; int boxesMax = 0; double tMax = 0;
                for (int i = 0; i < qs.Count; ++i) {
                    long t0 = Stopwatch.GetTimestamp();
                    CcdResult r = run(solver, qs[i], vf, o);
                    double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
                    tMax = Math.Max(tMax, us);
                    bool hit = r.Status != CcdStatus.NoCollision;
                    if (truth[i]) ++coll;
                    if (truth[i] && hit == false) ++fn;
                    if (truth[i] == false && hit) ++fp;
                    if (r.Status == CcdStatus.IterationLimit) ++lim;
                    boxes += r.Iterations; boxesMax = Math.Max(boxesMax, r.Iterations);
                }
                // time the whole file once more, without per-query timers
                var all = Stopwatch.StartNew();
                for (int i = 0; i < qs.Count; ++i) run(solver, qs[i], vf, o);
                double usPer = all.Elapsed.TotalMilliseconds * 1000 / qs.Count;
                if (ms == 0) fnTotal += fn;
                Console.WriteLine(FormattableString.Invariant($"{scene},{name},{(vf ? "VF" : "EE")},{(intervals ? "interval" : "filter")},{qs.Count},{coll},{fn},{fp},{lim},{usPer:F2},{tMax:F0},{boxes / (double)qs.Count:F1},{boxesMax}"));
            }
        }
        if (ms == 0 && fnTotal > 0) { Console.Error.WriteLine($"{fnTotal} false negatives"); return 1; }
        return 0;
    }

    static List<double[]> read_queries(string file)
    {
        var rows = new List<double[]>();
        foreach (string line in File.ReadLines(file)) {
            if (line.Length == 0) continue;
            string[] c = line.Split(',');
            var row = new double[3];
            for (int k = 0; k < 3; ++k) row[k] = rational(c[2 * k].Trim(), c[2 * k + 1].Trim());
            rows.Add(row);
        }
        var qs = new List<double[]>();
        for (int i = 0; i + 8 <= rows.Count; i += 8) {
            var q = new double[24];
            for (int r = 0; r < 8; ++r) for (int k = 0; k < 3; ++k) q[3 * r + k] = rows[i + r][k];
            qs.Add(q);
        }
        return qs;
    }

    // num / den with den a power of two and |num| < 2^53: exact in doubles above the subnormal range (else the
    // benchmark stops)
    static double rational(string num, string den)
    {
        var n = System.Numerics.BigInteger.Parse(num, CultureInfo.InvariantCulture);
        var d = System.Numerics.BigInteger.Parse(den, CultureInfo.InvariantCulture);
        if (d.Sign <= 0 || (d & (d - 1)) != 0 || System.Numerics.BigInteger.Abs(n) >= (System.Numerics.BigInteger.One << 53))
            throw new InvalidDataException($"not an exact double: {num}/{den}");
        int k = (int)(d.GetBitLength() - 1);
        double r = (double)n;
        while (k > 0) {
            int step = Math.Min(k, 1000);
            r *= BitConverter.Int64BitsToDouble((long)(1023 - step) << 52);   // 2^-step, exact while r stays normal
            k -= step;
        }
        if (r != 0 && Math.Abs(r) < 2.2250738585072014e-308)
            throw new InvalidDataException($"subnormal: {num}/{den}");
        return r;
    }
}
