// The slow-tail workload (the same cases as the BigInteger profile of HIGHER_MATH_STUDY_2026-11.md): coplanar boxes and the
// nine DegenerateFamilies at 2^-20 and 2^-40, each against a shifted copy and against itself, as exact unions.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using geometry3Sharp.Tests;

static class Workload
{
    static DMesh3 Box(Vector3d min, Vector3d max) =>
        new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(min, max)) }.Generate().MakeDMesh();
    static DMesh3 Shifted(DMesh3 m, Vector3d d) { var c = new DMesh3(m); MeshTransforms.Translate(c, d); return c; }

    public static IEnumerable<(string name, Func<ExactMeshBoolean> make)> Cases()
    {
        DMesh3 b1 = Box(new Vector3d(0, 0, 0), new Vector3d(2, 2, 1)), b2 = Box(new Vector3d(1, 1, 0), new Vector3d(3, 3, 1));
        yield return ("coplanar boxes", () => new ExactMeshBoolean(b1, b2, ExactMeshBoolean.Operations.Union));
        foreach (DegenerateFamilies.Family f in Enum.GetValues(typeof(DegenerateFamilies.Family)))
            foreach (int k in new[] { 20, 40 }) {
                DMesh3 m = DegenerateFamilies.Make(f, k, 1);
                DMesh3 o = Shifted(m, new Vector3d(0.5, 0, 0));
                yield return ($"{f} k={k} vs shifted copy", () => new ExactMeshBoolean(m, o, ExactMeshBoolean.Operations.Union));
                yield return ($"{f} k={k} vs itself", () => new ExactMeshBoolean(m, new DMesh3(m), ExactMeshBoolean.Operations.Union));
            }
    }

    public static long Run(Func<ExactMeshBoolean> make, out ExactMeshBoolean bo)
    {
        var sw = Stopwatch.StartNew();
        bo = make();
        bo.AllowOpenMeshes = true;
        bo.Compute();
        return sw.ElapsedMilliseconds;
    }

    public static void RunCases()
    {
        foreach (var (name, make) in Cases()) Run(make, out _);      // JIT warm-up
        Console.WriteLine("| case | ms | status | phases (ms) |");
        Console.WriteLine("|---|---|---|---|");
        foreach (var (name, make) in Cases()) {
            long ms = Run(make, out var bo);
            Console.WriteLine($"| {name} | {ms} | {bo.Status} | {string.Join(", ", bo.PhaseMilliseconds)} |");
        }
    }
}
