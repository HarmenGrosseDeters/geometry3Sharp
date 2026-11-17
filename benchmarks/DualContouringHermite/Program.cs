using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using g3;

/// <summary>
/// DualContouring with exact Hermite data (ExactHermite, QEFEigenTolerance 0.02) against the former interpolation (0.1):
/// surface error per cell against a bisection marching-cubes reference, triangle count and time.
/// Usage: DualContouringHermite [reference cells (default 256)] [repeats for the time (default 3)] [sweep: tolerance sweep]
/// </summary>
internal static class Program
{
    static DMesh3 Reference(Shape s, int n)
    {
        Vector3d size = s.Bounds.Diagonal;
        double cube = Math.Max(size.x, Math.Max(size.y, size.z)) / n;
        var mc = new MarchingCubes() {
            Implicit = s, IsoValue = 0, CubeSize = cube, Bounds = s.Bounds,
            RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 30
        };
        mc.Generate();
        return mc.Mesh;
    }

    static DualContouring Make(Shape s, int cells, bool exact, double tol)
    {
        Vector3d size = s.Bounds.Diagonal;
        double cell = Math.Max(size.x, Math.Max(size.y, size.z)) / cells;
        // grid offset by an irrational-ish fraction so that faces do not sit on grid nodes
        Vector3d o = s.Bounds.Min - cell * new Vector3d(0.37, 0.61, 0.23);
        return new DualContouring() { Simplify = false,
            Implicit = s, InsideIsLess = true, IsoValue = 0, CellSize = cell,
            Bounds = new AxisAlignedBox3d(o, o + size + 2 * cell * Vector3d.One),
            ExactHermite = exact, QEFEigenTolerance = tol
        };
    }

    // distance (in cells) of the points of a to the surface of b, accumulated
    static void Accumulate(DMesh3 a, DMeshAABBTree3 treeB, double cell, ref double sum, ref double max, ref long count)
    {
        foreach (int vid in a.VertexIndices()) {
            Vector3d p = a.GetVertex(vid);
            double dsq;
            int tid = treeB.FindNearestTriangle(p, out dsq);
            double d = Math.Sqrt(dsq) / cell;
            sum += d; count++; if (d > max) max = d;
        }
    }

    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        int refN = args.Length > 0 ? int.Parse(args[0]) : 256;
        int repeats = args.Length > 1 ? int.Parse(args[1]) : 3;
        bool sweep = args.Length > 2 && args[2] == "sweep";
        Console.WriteLine("shape;cells;mode;tris;mean_err_cells;max_err_cells;ms;closed;manifold_ok");
        foreach (Shape s in Shape.All()) {
            DMesh3 reference = Reference(s, refN);
            var refTree = new DMeshAABBTree3(reference, true);
            foreach (int cells in new[] { 16, 32, 64 }) {
                var modes = sweep
                    ? new[] { ("interpolated", false, 0.1), ("interpolated", false, 0.02), ("exact", true, 0.005), ("exact", true, 0.01), ("exact", true, 0.02), ("exact", true, 0.05), ("exact", true, 0.1) }
                    : new[] { ("interpolated", false, 0.1), ("exact", true, 0.02) };
                foreach (var mode in modes) {
                    long best = long.MaxValue; DualContouring dc = null;
                    for (int r = 0; r < repeats; ++r) {
                        dc = Make(s, cells, mode.Item2, mode.Item3);
                        var sw = Stopwatch.StartNew(); dc.Generate(); best = Math.Min(best, sw.ElapsedMilliseconds);
                    }
                    double cell = dc.CellSize;
                    var meshTree = new DMeshAABBTree3(dc.Mesh, true);
                    double sum = 0, max = 0; long count = 0;
                    Accumulate(dc.Mesh, refTree, cell, ref sum, ref max, ref count);
                    Accumulate(reference, meshTree, cell, ref sum, ref max, ref count);
                    Console.WriteLine($"{s.Name};{cells};{mode.Item1} tol {mode.Item3};{dc.Mesh.TriangleCount};{sum / count:F4};{max:F3};{best};{dc.Mesh.IsClosed()};{dc.NonManifoldTriangles == 0}");
                }
            }
        }
    }
}
