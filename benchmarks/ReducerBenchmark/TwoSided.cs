// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Reducer.MaxDeviationTwoSided (docs/design/BOUNDED_SIMPLIFICATION.md): the one-sided MaxDeviation reduction (A) against the
// two-sided one (B), reduced as far as the checks allow, at 0.001 and 0.003 of the bounding-box diagonal. Rounds alternate
// A and B; the times are medians. The Hausdorff distances are certified upper bounds (lower bounds in brackets) of
// MeshHausdorffDistance at a tolerance of 0.01 eps, in units of eps. Procedural meshes; further meshes (eg Thingi10K,
// fetched at run time, never committed) can be given as file arguments.
//
//   dotnet run -c Release --project benchmarks/ReducerBenchmark -- twosided [rounds] [mesh files...]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class TwoSided
{
    // a sphere with three narrow spikes of height 0.3: features thinner than the tolerance, which the one-sided bound cuts
    internal static DMesh3 SpikySphere(int edgeVertices)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        var dirs = new[] { new Vector3d(1, 0.2, 0.1).Normalized, new Vector3d(-0.3, 1, 0.2).Normalized, new Vector3d(0.1, -0.4, -1).Normalized };
        foreach (int vid in m.VertexIndices()) {
            Vector3d p = m.GetVertex(vid).Normalized;
            double s = 1;
            foreach (Vector3d d in dirs) { double c = p.Dot(d); if (c > 0.9995) s += 0.3 * (c - 0.9995) / 0.0005; }
            m.SetVertex(vid, p * s);
        }
        return m;
    }

    static (double up, double low) Hausdorff(DMesh3 from, DMesh3 to, double eps)
    {
        var h = new MeshHausdorffDistance(from, to) { Symmetric = false, Tolerance = 0.01 * eps };
        h.Compute();
        return (h.UpperBound / eps, h.LowerBound / eps);
    }

    internal static void Run(Func<int, DMesh3> bumpy, Func<int, DMesh3> terrain, string[] args)
    {
        int rounds = args.Length > 1 && int.TryParse(args[1], out int r) ? r : 3;
        var cases = new List<(string, DMesh3)> { ("bumpy sphere 40", bumpy(40)), ("spiky sphere 60", SpikySphere(60)), ("terrain 80", terrain(80)) };
        foreach (string f in args.Skip(1).Where(x => !int.TryParse(x, out int _))) {
            DMesh3 m = StandardMeshReader.ReadMesh(f);
            if (m != null && m.TriangleCount > 0) cases.Add((System.IO.Path.GetFileNameWithoutExtension(f), m));
        }
        Console.WriteLine($"rounds: {rounds}, processors: {Environment.ProcessorCount}");
        Console.WriteLine("| mesh | triangles | eps / diag | A: triangles, ms | B: triangles, ms | time B / A | A: h(M,S) / eps (lower) | B: h(M,S) / eps | B: h(S,M) / eps | B: certified / refused |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
        var ratios = new List<double>();
        foreach (var (name, input) in cases)
            foreach (double rel in new[] { 1e-3, 3e-3 }) {
                double eps = rel * input.CachedBounds.DiagonalLength;
                new Reducer(new DMesh3(input)) { MaxDeviation = eps, MaxDeviationTwoSided = true }.ReduceToTriangleCount(input.TriangleCount / 2);  // warm-up
                var ta = new List<double>(); var tb = new List<double>();
                DMesh3 ma = null, mb = null; Reducer rb = null;
                for (int k = 0; k < rounds; ++k) {
                    ma = new DMesh3(input);
                    var sw = Stopwatch.StartNew();
                    new Reducer(ma) { MaxDeviation = eps }.ReduceToTriangleCount(4);
                    ta.Add(sw.Elapsed.TotalMilliseconds);
                    mb = new DMesh3(input);
                    rb = new Reducer(mb) { MaxDeviation = eps, MaxDeviationTwoSided = true };
                    sw.Restart();
                    rb.ReduceToTriangleCount(4);
                    tb.Add(sw.Elapsed.TotalMilliseconds);
                }
                ta.Sort(); tb.Sort();
                double a = ta[ta.Count / 2], b = tb[tb.Count / 2];
                ratios.Add(b / a);
                var (aUp, aLow) = Hausdorff(input, ma, eps);
                var (bUp, _) = Hausdorff(input, mb, eps);
                var (bFwd, _) = Hausdorff(mb, input, eps);
                Console.WriteLine($"| {name} | {input.TriangleCount} | {rel:0.###} | {ma.TriangleCount}, {a:F0} | {mb.TriangleCount}, {b:F0} | {b / a:F2}x | {aUp:F3} ({aLow:F3}) | {bUp:F3} | {bFwd:F3} | {rb.reverseCoverage.Certified} / {rb.reverseCoverage.Refused} |");
            }
        ratios.Sort();
        Console.WriteLine($"time B / A: median {ratios[ratios.Count / 2]:F2}x, max {ratios[ratios.Count - 1]:F2}x");
    }
}
