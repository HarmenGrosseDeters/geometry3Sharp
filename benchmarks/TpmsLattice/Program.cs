using System;
using System.Diagnostics;
using System.Globalization;
using g3;
using gs;

// Stage shares of TpmsLattice.Compute. Usage: TpmsLattice [periods list, default 2,4,6] [divisions, default 16]
// Columns: nodes, triangles; ms of the bare field (Evaluate at every
// node, serial, no clip), Compute without the exact check (field + clip + marching cubes + NotCleanReason without
// self-intersections + volume), the self-intersection check alone, and the allocated MB and GC counts of the whole Compute.
// All wall times include the garbage collector (the GC counts are listed so its share can be judged).
internal static class Program
{
    static void Main(string[] args)
    {
        int[] periods = args.Length > 0 ? Array.ConvertAll(args[0].Split(','), int.Parse) : new[] { 2, 4, 6 };
        int divisions = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 16;
        Console.WriteLine("cores " + Environment.ProcessorCount + ", divisions " + divisions);
        Console.WriteLine("type/style, periods, nodes, triangles, field ms, compute(no SI check) ms, SI check ms, alloc MB, gen0/1/2");
        var cases = new (TpmsType, TpmsStyle)[] { (TpmsType.Gyroid, TpmsStyle.Sheet), (TpmsType.SchwarzP, TpmsStyle.SkeletalLow), (TpmsType.Neovius, TpmsStyle.Sheet) };
        // warm-up (JIT)
        Run(cases[0].Item1, cases[0].Item2, 1, 8, false);
        foreach (var c in cases)
            foreach (int p in periods)
                Run(c.Item1, c.Item2, p, divisions, true);
        // the graded cell size (a linear grade along z, the period 1 to 2): the cost of one phase delegate call per sample
        foreach (int p in periods)
            Run(TpmsType.Gyroid, TpmsStyle.Sheet, p, divisions, true, true);
    }

    static void Run(TpmsType type, TpmsStyle style, int periods, int divisions, bool print, bool graded = false)
    {
        var sw = new Stopwatch();

        var lattice = new TpmsLattice {
            Type = type, Style = style, CellSize = 1, Divisions = divisions, VolumeFraction = 0.3, VerifySelfIntersections = false,
            Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(periods, periods, periods))
        };
        if (graded) {
            lattice.Box = new AxisAlignedBox3d(new Vector3d(-periods / 2.0, -periods / 2.0, 0), new Vector3d(periods / 2.0, periods / 2.0, periods));
            lattice.Grade = TpmsLattice.LinearGrade(2, 0, 1, periods, 2, periods / 2.0 + 1.0);
        }
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        long a0 = GC.GetTotalAllocatedBytes(true);
        sw.Restart();
        lattice.Compute();
        double computeMs = sw.Elapsed.TotalMilliseconds;
        double allocMb = (GC.GetTotalAllocatedBytes(true) - a0) / 1048576.0;
        string gc = (GC.CollectionCount(0) - g0) + "/" + (GC.CollectionCount(1) - g1) + "/" + (GC.CollectionCount(2) - g2);
        if (lattice.Status != TpmsStatus.Ok) { Console.WriteLine("failed: " + lattice.Status + " " + lattice.Message); return; }

        double k = 2 * Math.PI, h = 1.0 / divisions, sum = 0;
        long nodes = lattice.CellsEvaluated;
        int n = (int)Math.Round(Math.Pow(nodes, 1.0 / 3));
        sw.Restart();
        for (int i = 0; i < n; ++i)
            for (int j = 0; j < n; ++j)
                for (int l = 0; l < n; ++l)
                    sum += TpmsLattice.Evaluate(type, k * i * h, k * j * h, k * l * h);
        double fieldMs = sw.Elapsed.TotalMilliseconds;
        if (double.IsNaN(sum)) Console.WriteLine();

        sw.Restart();
        string reason = MeshRobustRepair.NotCleanReason(lattice.Mesh, true);
        double checkMs = sw.Elapsed.TotalMilliseconds;
        if (reason != "") Console.WriteLine("not clean: " + reason);

        if (print)
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}{10}/{1}, {2}, {3}, {4}, {5:F1}, {6:F1}, {7:F1}, {8:F1}, {9}",
                type, style, periods, nodes, lattice.Mesh.TriangleCount, fieldMs, computeMs, checkMs, allocMb, gc, graded ? "(graded)" : ""));
    }
}
