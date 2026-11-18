// Rows of the certified patch bound (#848, docs/design/CERTIFIED_PATCH_BOUND.md section 10): the library's own
// MeshHausdorffDistance with CertifiedUpper, one-sided A -> B, without and with the patch bound (MeshHausdorffDistance.PatchBound, an
// internal field, set by reflection). Same cases and tolerance (1e-4 of the diagonal) as the `patch` mode, whose "patch oracle"
// columns are the uncertified patch bound in a benchmark-local cascade.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using g3;
using gs;

static partial class Program
{
    static void CertifiedPatchRows(int runs)
    {
        var cases = new List<(string, DMesh3, DMesh3)>();
        var big1 = Bumpy(9, 30); var big2 = Bumpy(11, 60);
        cases.Add(("reduced 1/8 of 5k bumpy", Reduced(big1, 8), big1));
        cases.Add(("reduced 1/32 of 5k bumpy", Reduced(big1, 32), big1));
        cases.Add(("reduced 1/8 of 21k bumpy", Reduced(big2, 8), big2));
        cases.Add(("reduced 1/32 of 21k bumpy", Reduced(big2, 32), big2));
        var smooth = Sphere(60, 1, Vector3d.Zero);
        cases.Add(("reduced 1/16 of 21k sphere", Reduced(smooth, 16), smooth));
        cases.Add(("spheres6 vs 20", Sphere(6, 1, Vector3d.Zero), Sphere(20, 1, Vector3d.Zero)));
        cases.Add(("bumpy5 -> bumpy6", Bumpy(5, 12), Bumpy(6, 8)));
        cases.Add(("coplanar grids 16 -> 17 (jitter .3)", Grid(16, 0, 1), Grid(17, 0.3, 2)));
        FieldInfo patch = typeof(MeshHausdorffDistance).GetField("PatchBound", BindingFlags.NonPublic | BindingFlags.Instance);
        Console.WriteLine($"CertifiedUpper, one-sided A -> B, tolerance 1e-4 of the diagonal, the library's parallel run, median of {runs} runs");
        Console.WriteLine("| case (tris A / B) | upper without → with patch | subdivisions without → with | ms without → with | time |");
        Console.WriteLine("|---|---|---|---|---|");
        foreach (var (name, a, b) in cases) {
            double tol = 1e-4 * a.GetBounds().DiagonalLength;
            var res = new MeshHausdorffDistance[2]; var ts = new List<double>[] { new List<double>(), new List<double>() };
            for (int r = 0; r <= runs; ++r)
                for (int m = 0; m < 2; ++m) {
                    var sw = Stopwatch.StartNew();
                    var h = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = tol, CertifiedUpper = true, MaxSubdivisions = 3000000 };
                    patch.SetValue(h, m == 1);
                    h.Compute();
                    if (r > 0) ts[m].Add(sw.Elapsed.TotalMilliseconds);
                    res[m] = h;
                }
            ts[0].Sort(); ts[1].Sort();
            double t0 = ts[0][runs / 2], t1 = ts[1][runs / 2];
            bool conv = res[0].Converged && res[1].Converged;
            Console.WriteLine($"| {name} ({a.TriangleCount} / {b.TriangleCount}){(conv ? "" : " (not converged)")} | {res[0].UpperBound:G6} → {res[1].UpperBound:G6} | " +
                $"{res[0].Subdivisions} → {res[1].Subdivisions} | {t0:F0} → {t1:F0} | {100 * (t1 / t0 - 1):+0;-0}% |");
        }
    }
}
