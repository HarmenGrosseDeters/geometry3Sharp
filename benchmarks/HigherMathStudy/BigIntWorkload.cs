// Idea 2, step (c): the workload whose BigInteger share is the upper bound of a multi-modular zero test.
// Run it under dotnet-trace (README) and fold the samples with tools/fold_speedscope.py.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using geometry3Sharp.Tests;

static class BigIntWorkload
{
    static DMesh3 Box(Vector3d min, Vector3d max) =>
        new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(min, max)) }.Generate().MakeDMesh();

    static DMesh3 Shifted(DMesh3 m, Vector3d d) { var c = new DMesh3(m); MeshTransforms.Translate(c, d); return c; }

    public static IEnumerable<(string name, string stratum, Func<ExactMeshBoolean> make)> Cases()
    {
        // stratum "normal": general position
        DMesh3 s = DegenerateFamilies.Sphere(24);
        yield return ("spheres general", "normal", () => new ExactMeshBoolean(s, Shifted(s, new Vector3d(0.7, 0.3, 0.1)), ExactMeshBoolean.Operations.Union));
        // stratum "degenerate": shared and coplanar faces, the degenerate families (k = 20, 40) against a coplanar-faced box
        DMesh3 b1 = Box(new Vector3d(0, 0, 0), new Vector3d(2, 2, 1)), b2 = Box(new Vector3d(1, 1, 0), new Vector3d(3, 3, 1));
        yield return ("coplanar boxes", "degenerate", () => new ExactMeshBoolean(b1, b2, ExactMeshBoolean.Operations.Union));
        foreach (DegenerateFamilies.Family f in Enum.GetValues(typeof(DegenerateFamilies.Family)))
            foreach (int k in new[] { 20, 40 }) {
                DMesh3 m;
                try { m = DegenerateFamilies.Make(f, k, 1); } catch (Exception) { continue; }
                DMesh3 o = Shifted(m, new Vector3d(0.5, 0, 0));    // the same family against a copy: coincident vertices and faces
                yield return ($"{f} k={k} vs shifted copy", "degenerate", () => new ExactMeshBoolean(m, o, ExactMeshBoolean.Operations.Union));
                yield return ($"{f} k={k} vs itself", "degenerate", () => new ExactMeshBoolean(m, new DMesh3(m), ExactMeshBoolean.Operations.Union));
            }
    }

    // only with the temporary counters of the study (not in the library): the zero share of orient3d's exact stage
    static void ProbeOrient()
    {
        var t = typeof(ExactPredicates);
        long Get(string n) { var f = t.GetField(n); if (f == null) throw new InvalidOperationException("G3_PROBE_ORIENT needs results/orient3d_zero_counters.patch applied to the library"); long v = (long)f.GetValue(null); f.SetValue(null, 0L); return v; }
        long zt = Get("ProbeZeroTicks"), nt = Get("ProbeNonzeroTicks"), zc = Get("ProbeZeroCalls"), nc = Get("ProbeNonzeroCalls"), tz = Get("ProbeTrivialZero");
        double f = 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"|   orient3d_exact | zero {zc} calls {zt * f:F1} ms | nonzero {nc} calls {nt * f:F1} ms | trivial zero {tz} |");
    }

    public static void Run(string[] args)
    {
        int reps = args.Length > 1 ? int.Parse(args[1]) : 1;
        string only = args.Length > 2 ? args[2] : null;
        Console.WriteLine("| case | stratum | status | ms (per run) |");
        Console.WriteLine("|---|---|---|---|");
        foreach (var (name, stratum, make) in Cases()) {
            if (only != null && stratum != only) continue;
            var sw = Stopwatch.StartNew();
            string st = "";
            for (int r = 0; r < reps; ++r) {
                var bo = make();
                bo.AllowOpenMeshes = true;
                bo.Compute();
                st = bo.Status.ToString();
            }
            Console.WriteLine($"| {name} | {stratum} | {st} | {sw.ElapsedMilliseconds / reps} |");
            if (Environment.GetEnvironmentVariable("G3_PROBE_ORIENT") == "1") ProbeOrient();
        }
    }
}
