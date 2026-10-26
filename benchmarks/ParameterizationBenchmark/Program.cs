// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using g3;

/// <summary>
/// Timings and distortion of the parameterization methods and of SparseCholesky on procedural meshes
/// (sphere caps, an ellipsoid cap, a cut-open cylinder) of growing size. One line per case:
///   method, mesh, vertices, milliseconds, iterations, converged, flipped triangles, mean / max conformal
///   distortion, mean area distortion, symmetric Dirichlet energy.
/// Usage: dotnet run -c Release [-- maxRings]   (default 120: about 48k vertices)
/// </summary>
static class Program
{
    static DMesh3 Polar(int rings, int sectors, Func<double, double, Vector3d> pos, double jitter, int seed = 1)
    {
        var rand = new Random(seed);
        DMesh3 mesh = new DMesh3();
        int center = mesh.AppendVertex(pos(0, 0));
        int[,] vid = new int[rings + 1, sectors];
        for (int i = 1; i <= rings; ++i)
            for (int j = 0; j < sectors; ++j) {
                double r = (double)i / rings, a = 2 * Math.PI * j / sectors;
                if (i < rings && jitter > 0) {
                    r += jitter * (rand.NextDouble() - 0.5) / rings;
                    a += jitter * (rand.NextDouble() - 0.5) * 2 * Math.PI / sectors;
                }
                vid[i, j] = mesh.AppendVertex(pos(r, a));
            }
        for (int j = 0; j < sectors; ++j)
            mesh.AppendTriangle(center, vid[1, j], vid[1, (j + 1) % sectors]);
        for (int i = 1; i < rings; ++i)
            for (int j = 0; j < sectors; ++j) {
                int a = vid[i, j], b = vid[i, (j + 1) % sectors], c = vid[i + 1, j], d = vid[i + 1, (j + 1) % sectors];
                if ((i + j) % 2 == 0) { mesh.AppendTriangle(a, c, d); mesh.AppendTriangle(a, d, b); }
                else { mesh.AppendTriangle(a, c, b); mesh.AppendTriangle(b, c, d); }
            }
        return mesh;
    }

    static DMesh3 Cap(double maxPolar, int rings, double sx = 1)
    {
        return Polar(rings, rings * 10 / 3, (r, a) => {
            double p = r * maxPolar;
            return new Vector3d(sx * Math.Sin(p) * Math.Cos(a), Math.Sin(p) * Math.Sin(a), Math.Cos(p));
        }, 0.5);
    }

    static DMesh3 Cylinder(int n)
    {
        DMesh3 mesh = new DMesh3();
        int ns = 4 * n, nt = n;
        int[,] vid = new int[ns + 1, nt + 1];
        for (int j = 0; j <= nt; ++j)
            for (int i = 0; i <= ns; ++i) {
                double s = 2 * Math.PI * i / ns;
                vid[i, j] = mesh.AppendVertex(new Vector3d(Math.Cos(s), Math.Sin(s), 2.0 * j / nt));
            }
        for (int j = 0; j < nt; ++j)
            for (int i = 0; i < ns; ++i) {
                mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
            }
        return mesh;
    }

    static void Report(string method, string name, DMesh3 mesh, Func<ParameterizationResult> run)
    {
        var sw = Stopwatch.StartNew();
        ParameterizationResult r = run();
        double ms = sw.Elapsed.TotalMilliseconds;
        var d = r.Distortion;
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-6} {1,-10} V={2,6} {3,9:F0} ms  it={4,3} conv={5,-5} flips={6} conf={7:F4}/{8:F3} area={9:F4} SD={10:F4} {11}",
            method, name, mesh.VertexCount, ms, r.Iterations, r.Converged, d?.FlippedTriangles, d?.MeanConformal,
            d?.MaxConformal, d?.MeanAreaDistortion, d?.SymmetricDirichlet, r.Succeeded ? "" : r.Status + ": " + r.Error));
    }

    static void Main(string[] args)
    {
        int maxRings = args.Length > 0 ? int.Parse(args[0]) : 120;
        // warm up the JIT
        DMesh3 small = Cap(Math.PI / 2, 8);
        new LSCMParameterization(small).Compute(); new ARAPParameterization(small).Compute();
        new SLIMParameterization(small).Compute(); new BoundaryFirstFlattening(small).Compute();

        for (int rings = 30; rings <= maxRings; rings *= 2) {
            var meshes = new List<KeyValuePair<string, DMesh3>>() {
                new KeyValuePair<string, DMesh3>("hemi", Cap(Math.PI / 2, rings)),
                new KeyValuePair<string, DMesh3>("cap0.8", Cap(0.8 * Math.PI, rings)),
                new KeyValuePair<string, DMesh3>("ellipsoid", Cap(Math.PI / 2, rings, 2)),
                new KeyValuePair<string, DMesh3>("cylinder", Cylinder(rings)),
            };
            foreach (var kv in meshes) {
                DMesh3 m = kv.Value;
                Report("LSCM", kv.Key, m, () => new LSCMParameterization(m).Compute());
                Report("BFF", kv.Key, m, () => new BoundaryFirstFlattening(m).Compute());
                Report("BFFdsk", kv.Key, m, () => new BoundaryFirstFlattening(m) { BoundaryMode = BoundaryFirstFlattening.BoundaryModes.Disk }.Compute());
                Report("ARAP", kv.Key, m, () => new ARAPParameterization(m).Compute());
                Report("Tutte", kv.Key, m, () => new TutteEmbedding(m).Compute());
                Report("SLIM", kv.Key, m, () => new SLIMParameterization(m).Compute());
            }

            // the solver alone: cotan Laplacian with one fixed vertex (block 1) and the interleaved u,v system (block 2)
            DMesh3 hemi = meshes[0].Value;
            foreach (int block in new[] { 1, 2 }) {
                int n = hemi.MaxVertexID * block;
                var chol = new SparseCholesky(n, block);
                Action fill = () => {
                    chol.Clear();
                    foreach (int tid in hemi.TriangleIndices()) {
                        Index3i t = hemi.GetTriangle(tid);
                        Vector3d cot = Vector3d.Zero;
                        for (int k = 0; k < 3; ++k) {
                            Vector3d a = hemi.GetVertex(t[(k + 1) % 3]) - hemi.GetVertex(t[k]), b = hemi.GetVertex(t[(k + 2) % 3]) - hemi.GetVertex(t[k]);
                            double w = 0.5 * a.Dot(b) / a.Cross(b).Length;
                            int i = t[(k + 1) % 3], j = t[(k + 2) % 3];
                            for (int s = 0; s < block; ++s) {
                                chol.Add(block * i + s, block * i + s, w); chol.Add(block * j + s, block * j + s, w);
                                chol.Add(block * i + s, block * j + s, -w);
                            }
                        }
                    }
                    for (int s = 0; s < block; ++s) chol.Add(s, s, 1);   // pin vertex 0
                };
                fill();
                var sw = Stopwatch.StartNew();
                bool ok = chol.Factorize();
                double first = sw.Elapsed.TotalMilliseconds;
                fill();
                sw.Restart();
                ok = chol.Factorize() && ok;
                double refactor = sw.Elapsed.TotalMilliseconds;
                double[] bvec = new double[n], x = new double[n];
                bvec[n - 1] = 1;
                sw.Restart();
                chol.Solve(bvec, x);
                double solve = sw.Elapsed.TotalMilliseconds;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Cholesky block={0} n={1,6} nnz(L)={2,9} factor {3,7:F0} ms, refactor {4,7:F0} ms, solve {5,5:F1} ms {6}",
                    block, n, chol.FactorNonZeros, first, refactor, solve, ok ? "" : chol.Error));
            }
        }
    }
}
