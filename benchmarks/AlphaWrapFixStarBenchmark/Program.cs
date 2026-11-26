// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Diagnostics;
using g3;
using gs;

// The manifold step of the alpha wrap at stars with many cells (issue #1607, README.md): a sphere (6 edge vertices per cube side) wrapped
// at RelativeAlpha 1e5 and RelativeOffset 1e6 with a point limit far below what the alpha needs, so that the box corners have stars of
// tens of thousands of cells. For each MaxSteinerPoints: total seconds, the seconds of the wrap phase and of the manifold phase, the moves
// of the step (Counts["manifold_flips"]) and the cells it visited (Counts["manifold_star_work"]), the result triangles.
//     dotnet run --project benchmarks/AlphaWrapFixStarBenchmark -c Release [-- --quick]   (--quick: up to 20000 points)
static class Program
{
    static void Main(string[] args)
    {
        bool quick = Array.IndexOf(args, "--quick") >= 0;
        DMesh3 sphere = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
        Console.WriteLine("points   total_s  wrap_ms  manifold_ms  moves     cells_visited  triangles  status");
        foreach (int points in new[] { 2000, 5000, 10000, 20000, 40000, 80000, 160000 }) {
            if (quick && points > 20000) break;
            var sw = Stopwatch.StartNew();
            var w = new MeshAlphaWrap(sphere) { RelativeAlpha = 1e5, RelativeOffset = 1e6, MaxSteinerPoints = points };
            w.Compute();
            long wrap = w.PhaseMilliseconds.TryGetValue("wrap", out long a) ? a : -1, man = w.PhaseMilliseconds.TryGetValue("manifold", out long b) ? b : -1;
            long moves = w.Counts.TryGetValue("manifold_flips", out long f) ? f : -1, work = w.Counts.TryGetValue("manifold_star_work", out long k) ? k : -1;
            Console.WriteLine($"{points,-8} {sw.Elapsed.TotalSeconds,7:F2}  {wrap,7}  {man,11}  {moves,-8}  {work,-13}  {w.Result?.TriangleCount,-9}  {w.Status}");
        }
    }
}
