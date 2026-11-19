// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

// Prototype of the signed heat method (N. Feng, K. Crane, "A Heat Method for Generalized Signed Distance", SIGGRAPH 2024) on a regular grid,
// written from the method's description in docs/design/INSIDE_OUTSIDE_OPEN_INPUTS.md (no authors' code read):
//   1. splat the oriented area normals of the input onto the cells, diffuse them by one backward-Euler step of heat flow, t = h^2,
//      (I - t L) u = b, a 7-point Laplacian with Neumann boundary,
//   2. normalize u to the unit field X,
//   3. integrate: L phi = div X (cell-centered, face-averaged, zero flux through the box boundary), the constant of phi from the mean over the input
//      (area weighted), inside = phi < 0.
// Deviations from the paper's grid variant: step 1 is a solve here (the paper has a closed form), the splat is nearest cell, not the exact kernel.
class SignedHeatGrid
{
    public int N; public double H; public Vector3d Origin;
    public double[] Phi;
    public double SecondsSplat, SecondsStep1, SecondsStep2; public int Iterations;
    public bool UseCG;

    int Id(int i, int j, int k) { return (k * N + j) * N + i; }

    public void Compute(DMesh3 mesh, int n, bool flipOrientation = false, double padFraction = 0.25)
    {
        N = n;
        AxisAlignedBox3d b = mesh.GetBounds();
        double side = Math.Max(b.Width, Math.Max(b.Height, b.Depth)) * (1 + 2 * padFraction);
        H = side / n; Vector3d c = b.Center; Origin = c - new Vector3d(side / 2, side / 2, side / 2);
        int cells = n * n * n;
        var sw = Stopwatch.StartNew();
        double[][] rhs = { new double[cells], new double[cells], new double[cells] };
        double sgn = flipOrientation ? -1 : 1;
        foreach (int t in mesh.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, bb = Vector3d.Zero, cc = Vector3d.Zero; mesh.GetTriVertices(t, ref a, ref bb, ref cc);
            Vector3d an = sgn * 0.5 * (bb - a).Cross(cc - a);
            double area = an.Length; if (area == 0) continue;
            int k = Math.Min(64, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(4 * area / (H * H)))));
            double w = 1.0 / (k * k);
            for (int s = 0; s < k; ++s) for (int r = 0; r < k - s; ++r) {
                // two sub-triangle families of the k-subdivision
                for (int up = 0; up < 2; ++up) {
                    if (up == 1 && r >= k - s - 1) continue;
                    double uu = (r + (up == 0 ? 1.0 / 3 : 2.0 / 3)) / k, v = (s + (up == 0 ? 1.0 / 3 : 2.0 / 3)) / k;
                    Vector3d p = a + uu * (bb - a) + v * (cc - a);
                    int i = (int)((p.x - Origin.x) / H), j = (int)((p.y - Origin.y) / H), l = (int)((p.z - Origin.z) / H);
                    if (i < 0 || j < 0 || l < 0 || i >= n || j >= n || l >= n) continue;
                    int id = Id(i, j, l);
                    rhs[0][id] += w * an.x; rhs[1][id] += w * an.y; rhs[2][id] += w * an.z;
                }
            }
        }
        SecondsSplat = sw.Elapsed.TotalSeconds; sw.Restart();
        // step 1: (I + t A) u = b with A = -Laplacian, t = h^2: diagonal 1 + deg, off-diagonal -1 (scaled by h^2)
        var A1 = new SparseCholesky(cells);
        double[][] u = { new double[cells], new double[cells], new double[cells] };
        if (UseCG) {
            for (int ch = 0; ch < 3; ++ch) {
                var cg = new SparseSymmetricCG { MultiplyF = (x, y) => ApplyShifted(x, y, 1.0, 1.0), B = rhs[ch], X = u[ch], MaxIterations = 2000 };
                cg.Solve(); Iterations += cg.Iterations;
            }
        } else {
            Fill(A1, 1.0, 1.0);
            if (!A1.Factorize()) throw new Exception("factorize 1: " + A1.Error);
            for (int ch = 0; ch < 3; ++ch) A1.Solve(rhs[ch], u[ch]);
        }
        SecondsStep1 = sw.Elapsed.TotalSeconds; sw.Restart();
        // step 2
        double[][] X = { new double[cells], new double[cells], new double[cells] };
        for (int q = 0; q < cells; ++q) {
            double len = Math.Sqrt(u[0][q] * u[0][q] + u[1][q] * u[1][q] + u[2][q] * u[2][q]);
            if (len > 1e-300) { X[0][q] = u[0][q] / len; X[1][q] = u[1][q] / len; X[2][q] = u[2][q] / len; }
        }
        double[] div = new double[cells]; // h^2 * div X
        for (int k = 0; k < n; ++k) for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
            int q = Id(i, j, k); double d = 0;
            if (i + 1 < n) d += 0.5 * (X[0][q] + X[0][Id(i + 1, j, k)]);
            if (i > 0) d -= 0.5 * (X[0][q] + X[0][Id(i - 1, j, k)]);
            if (j + 1 < n) d += 0.5 * (X[1][q] + X[1][Id(i, j + 1, k)]);
            if (j > 0) d -= 0.5 * (X[1][q] + X[1][Id(i, j - 1, k)]);
            if (k + 1 < n) d += 0.5 * (X[2][q] + X[2][Id(i, j, k + 1)]);
            if (k > 0) d -= 0.5 * (X[2][q] + X[2][Id(i, j, k - 1)]);
            div[q] = -d * H; // A phi = -h^2 div X, A = -h^2 Laplacian; phi in length units
        }
        Phi = new double[cells];
        double eps = 1e-8;
        if (UseCG) {
            var cg = new SparseSymmetricCG { MultiplyF = (x, y) => ApplyShifted(x, y, 0.0, 1.0, eps), B = div, X = Phi, MaxIterations = 5000 };
            cg.Solve(); Iterations += cg.Iterations;
        } else {
            var A2 = new SparseCholesky(cells);
            Fill(A2, 0.0, 1.0, eps);
            if (!A2.Factorize()) throw new Exception("factorize 2: " + A2.Error);
            A2.Solve(div, Phi);
        }
        // constant: mean over the input surface, area weighted
        double sum = 0, wsum = 0;
        foreach (int t in mesh.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, bb = Vector3d.Zero, cc = Vector3d.Zero; mesh.GetTriVertices(t, ref a, ref bb, ref cc);
            double area = 0.5 * (bb - a).Cross(cc - a).Length;
            sum += area * Eval((a + bb + cc) / 3); wsum += area;
        }
        double shift = sum / wsum;
        for (int q = 0; q < cells; ++q) Phi[q] -= shift;
        SecondsStep2 = sw.Elapsed.TotalSeconds;
    }

    void Fill(SparseCholesky M, double mass, double stiff, double eps = 0)
    {
        for (int k = 0; k < N; ++k) for (int j = 0; j < N; ++j) for (int i = 0; i < N; ++i) {
            int q = Id(i, j, k), deg = 0;
            if (i + 1 < N) { M.Add(q, Id(i + 1, j, k), -stiff); deg++; }
            if (j + 1 < N) { M.Add(q, Id(i, j + 1, k), -stiff); deg++; }
            if (k + 1 < N) { M.Add(q, Id(i, j, k + 1), -stiff); deg++; }
            if (i > 0) deg++; if (j > 0) deg++; if (k > 0) deg++;
            M.Add(q, q, mass + stiff * deg + eps);
        }
    }

    void ApplyShifted(double[] x, double[] y, double mass, double stiff, double eps = 0)
    {
        for (int k = 0; k < N; ++k) for (int j = 0; j < N; ++j) for (int i = 0; i < N; ++i) {
            int q = Id(i, j, k), deg = 0; double s = 0;
            if (i + 1 < N) { s += x[Id(i + 1, j, k)]; deg++; } if (i > 0) { s += x[Id(i - 1, j, k)]; deg++; }
            if (j + 1 < N) { s += x[Id(i, j + 1, k)]; deg++; } if (j > 0) { s += x[Id(i, j - 1, k)]; deg++; }
            if (k + 1 < N) { s += x[Id(i, j, k + 1)]; deg++; } if (k > 0) { s += x[Id(i, j, k - 1)]; deg++; }
            y[q] = (mass + eps + stiff * deg) * x[q] - stiff * s;
        }
    }

    // trilinear phi at p (cell-centered samples); points outside the box are clamped
    public double Eval(Vector3d p)
    {
        double fx = (p.x - Origin.x) / H - 0.5, fy = (p.y - Origin.y) / H - 0.5, fz = (p.z - Origin.z) / H - 0.5;
        fx = Math.Min(Math.Max(fx, 0), N - 1.000001); fy = Math.Min(Math.Max(fy, 0), N - 1.000001); fz = Math.Min(Math.Max(fz, 0), N - 1.000001);
        int i = (int)fx, j = (int)fy, k = (int)fz; double tx = fx - i, ty = fy - j, tz = fz - k;
        double r = 0;
        for (int dz = 0; dz < 2; ++dz) for (int dy = 0; dy < 2; ++dy) for (int dx = 0; dx < 2; ++dx)
            r += (dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty) * (dz == 0 ? 1 - tz : tz) * Phi[Id(i + dx, j + dy, k + dz)];
        return r;
    }
}
