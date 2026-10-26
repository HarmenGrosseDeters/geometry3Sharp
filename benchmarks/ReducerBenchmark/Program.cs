// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Cost of the Reducer's certified deviation check (MaxDeviation, TriangleDeviationBounds): reduction with the
// check until no collapse stays within the tolerance, and without it to the same triangle count. The difference
// in time is what a faster containment check (eg a polyhedral envelope) could save at most. Procedurally
// generated meshes (no mesh files).
//
//   dotnet run -c Release --project benchmarks/ReducerBenchmark
using System;
using System.Diagnostics;
using g3;
using gs;

static class Program
{
    static DMesh3 BumpySphere(int edgeVertices)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        foreach (int vid in m.VertexIndices()) {
            Vector3d p = m.GetVertex(vid);
            m.SetVertex(vid, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5) + 0.05 * Math.Sin(9 * p.z)));
        }
        return m;
    }

    // n x n heightfield on [0, 1]^2: smooth hills plus small ripples
    static DMesh3 Terrain(int n)
    {
        var m = new DMesh3();
        int[,] ids = new int[n + 1, n + 1];
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i) {
                double x = (double)i / n, y = (double)j / n;
                double z = 0.08 * Math.Sin(6 * x) * Math.Cos(5 * y) + 0.01 * Math.Sin(40 * x + 13 * y) + 0.004 * Math.Cos(90 * y - 31 * x);
                ids[i, j] = m.AppendVertex(new Vector3d(x, y, z));
            }
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
            }
        return m;
    }

    // certified upper bound of the one-sided distance, to 1 % of the tolerance
    static double OneSidedHausdorff(DMesh3 from, DMesh3 to, double tol)
    {
        var h = new MeshHausdorffDistance(from, to) { Symmetric = false, Tolerance = 0.01 * tol };
        h.Compute();
        return h.UpperBound;
    }

    static void Main(string[] args)
    {
        var meshes = new (string, DMesh3)[] { ("bumpy sphere", BumpySphere(130)), ("terrain", Terrain(300)) };
        Console.WriteLine("| mesh | triangles | tolerance (of bbox diagonal) | with check: triangles | time with check | time without, same count | check share | deviation without check |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (var (name, input) in meshes) {
            double diag = input.CachedBounds.DiagonalLength;
            foreach (double rel in new[] { 1e-3, 3e-3, 1e-2 }) {
                double tol = rel * diag;
                // warm-up (JIT) on a copy
                new Reducer(new DMesh3(input)) { MaxDeviation = tol }.ReduceToTriangleCount(input.TriangleCount / 2);

                var withCheck = new DMesh3(input);
                var sw = Stopwatch.StartNew();
                new Reducer(withCheck) { MaxDeviation = tol }.ReduceToTriangleCount(4);
                long tWith = sw.ElapsedMilliseconds;

                var without = new DMesh3(input);
                sw.Restart();
                new Reducer(without).ReduceToTriangleCount(withCheck.TriangleCount);
                long tWithout = sw.ElapsedMilliseconds;

                double devWith = OneSidedHausdorff(withCheck, input, tol), devWithout = OneSidedHausdorff(without, input, tol);
                Console.WriteLine($"| {name} | {input.TriangleCount} | {rel:0.###} | {withCheck.TriangleCount} | {tWith} ms | {tWithout} ms | {100.0 * (tWith - tWithout) / tWith:F0} % | {devWithout / tol:F2} x tolerance (with check: {devWith / tol:F2}) |");
            }
        }
    }
}
