// Part "cull": the upper bound of a coarse-first (top-down, sparse) construction of the dual contouring grid from a
// Lipschitz bound L of the field (plan #1276). An octree over the domain is split only where |f(centre)| <= L * r
// (r = half the cell diagonal); a cell that fails the test cannot hold the surface. Reported: the evaluations of such a
// build (cell centres of the octree plus the distinct corner nodes of the surviving finest cells) against the dense grid,
// and the share of the dense run that the saving applies to (the phases "sample" and "crossings" of DualContouring).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

static class CullProbe
{
    static long centres;

    static void Visit(Func<Vector3d, double> f, double lip, Vector3d min, double size, int ix, int iy, int iz, int cells, int n1, HashSet<long> nodes, ref long leaves)
    {
        Vector3d c = min + 0.5 * size * Vector3d.One;
        ++centres;
        if (Math.Abs(f(c)) > lip * size * Math.Sqrt(3) / 2) return;
        if (cells == 1) {
            ++leaves;
            for (int q = 0; q < 8; ++q) nodes.Add((ix + (q & 1)) + (long)n1 * ((iy + ((q >> 1) & 1)) + (long)n1 * (iz + ((q >> 2) & 1))));
            return;
        }
        int hc = cells / 2; double hs = size / 2;
        for (int q = 0; q < 8; ++q) {
            int dx = q & 1, dy = (q >> 1) & 1, dz = (q >> 2) & 1;
            Visit(f, lip, min + hs * new Vector3d(dx, dy, dz), hs, ix + dx * hc, iy + dy * hc, iz + dz * hc, hc, n1, nodes, ref leaves);
        }
    }

    public static void Run(int maxDense)
    {
        Matrix3d R = Program.Rotation(0);
        double kg = 2 * Math.PI / 0.5;
        var shapes = new List<Tuple<string, Func<Vector3d, double>, double>> {
            Tuple.Create<string, Func<Vector3d, double>, double>("sphere", p => p.Length - 0.7, 1.0),
            Tuple.Create<string, Func<Vector3d, double>, double>("plate 0.04", p => ThinProbe.Box(R * p, new Vector3d(0.6, 0.5, 0.02)), 1.0),
            // gyroid sheet, 4 periods across the domain, wall 0.04 at most, clipped to a ball; sqrt 6 is a loose bound of |grad g| / k
            Tuple.Create<string, Func<Vector3d, double>, double>("gyroid 0.04", p => { Vector3d q = R * p;
                double g = Math.Sin(kg * q.x) * Math.Cos(kg * q.y) + Math.Sin(kg * q.y) * Math.Cos(kg * q.z) + Math.Sin(kg * q.z) * Math.Cos(kg * q.x);
                return Math.Max(Math.Abs(g) / kg - 0.02, p.Length - 0.8); }, Math.Sqrt(6)),
        };
        Console.WriteLine("# cull: octree over [-1, 1]^3 down to N cells per axis, split only where |f(centre)| <= L * half diagonal");
        Console.WriteLine("shape, N, dense nodes, surface cells, octree centres, distinct corner nodes, sparse evals / dense, dense DC ms, sample + crossings ms, their share, triangles, peak working set MB");
        foreach (var sh in shapes)
            foreach (int n in new[] { 128, 256, 512 }) {
                long dense = (long)(n + 1) * (n + 1) * (n + 1);
                var nodes = new HashSet<long>();
                long leaves = 0; centres = 0;
                Visit(sh.Item2, sh.Item3, -Vector3d.One, 2.0, 0, 0, 0, n, n + 1, nodes, ref leaves);
                long sparse = centres + nodes.Count;
                string dcPart = ", , , , , ";
                if (n <= maxDense) {
                    nodes = null; GC.Collect();
                    double h = 2.0 / n;
                    var dc = new DualContouring { Implicit = new FuncImplicit(sh.Item2), InsideIsLess = true, CellSize = h,
                        Bounds = new AxisAlignedBox3d(-Vector3d.One - 0.37 * h * Vector3d.One, Vector3d.One) };
                    var sw = Stopwatch.StartNew();
                    dc.Generate();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    long s, c;
                    dc.PhaseMilliseconds.TryGetValue("sample", out s); dc.PhaseMilliseconds.TryGetValue("crossings", out c);
                    using (Process proc = Process.GetCurrentProcess())
                        dcPart = string.Format(", {0:F0}, {1}, {2:F3}, {3}, {4:F0}", ms, s + c, (s + c) / ms, dc.Mesh.TriangleCount, proc.PeakWorkingSet64 / 1048576.0);
                }
                Console.WriteLine(string.Format("{0}, {1}, {2}, {3}, {4}, {5}, {6:F4}", sh.Item1, n, dense, leaves, centres, sparse - centres, (double)sparse / dense) + dcPart);
            }
    }
}
