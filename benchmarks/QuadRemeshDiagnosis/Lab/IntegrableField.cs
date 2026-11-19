// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs.Lab
{
    /// <summary>
    /// Lab lever "integrable frames": per vertex a rotation phi_i of the cross field and a log scale sigma_i such that the scaled and
    /// rotated frames close up, i.e. the lattice they stand for has no Burgers vector in the triangles without a field singularity.
    /// Own derivation (the integrability condition d(e^-sigma theta_k) = 0 of a coframe, the idea of the scale and rotation freedom of
    /// the global parametrization of Bommes, Zimmer, Kobbelt, "Mixed-Integer Quadrangulation", SIGGRAPH 2009; no code read):
    /// per triangle w = sum over the three edges of e^-sigma_e Rot(phi_e) a_e (a_e: the edge vector in the aligned, averaged cross frame
    /// of the edge, in units of s; sigma_e, phi_e the means of the end points). Gauss-Newton on the sum of |w|^2 plus a Tikhonov term,
    /// every step by conjugate gradients on the normal equations.
    /// </summary>
    static class IntegrableField
    {
        public static void Solve(DMesh3 mesh, CrossField cf, double s, bool useScale, bool useRotation, int gaussNewton, double lambda, double maxLog, double maxRotDeg,
            out Vector3d[] qOut, out double[] scaleOut, out string report)
        {
            var swT = System.Diagnostics.Stopwatch.StartNew();
            var toV = new List<int>(); var toC = new int[mesh.MaxVertexID];
            for (int v = 0; v < mesh.MaxVertexID; ++v) { toC[v] = -1; if (mesh.IsVertex(v) && cf.HasField(v)) { toC[v] = toV.Count; toV.Add(v); } }
            int n = toV.Count;
            var V = new Vector3d[n]; var N = new Vector3d[n]; var Q = new Vector3d[n]; var bnd = new bool[n];
            for (int i = 0; i < n; ++i) { int v = toV[i]; V[i] = mesh.GetVertex(v); N[i] = cf.VertexNormal(v); Q[i] = cf.VertexDirection(v, 0); bnd[i] = mesh.IsBoundaryVertex(v); }
            var sing = new HashSet<int>(cf.SingularFaces);
            var tri = new List<int[]>(); var aE = new List<Vector2d[]>();
            foreach (int t in mesh.TriangleIndices()) {
                if (sing.Contains(t)) continue;
                Index3i tv = mesh.GetTriangle(t);
                int[] c = { toC[tv.a], toC[tv.b], toC[tv.c] };
                if (c[0] < 0 || c[1] < 0 || c[2] < 0) continue;
                Vector3d qa = Q[c[0]], na = N[c[0]];
                var a = new Vector2d[3]; bool ok = true;
                for (int e = 0; e < 3; ++e) {
                    int i = c[e], j = c[(e + 1) % 3];
                    Vector3d q0, q1, u0, u1;
                    PositionFieldLab.CompatOrient(qa, na, Q[i], N[i], out q0, out q1);
                    PositionFieldLab.CompatOrient(qa, na, Q[j], N[j], out u0, out u1);
                    Vector3d qe = q1 + u1; double ql = qe.Length; if (ql < 1e-9) { ok = false; break; }
                    qe /= ql;
                    Vector3d ev = (V[j] - V[i]) / s;
                    a[e] = new Vector2d(ev.Dot(qe), ev.Dot(na.Cross(qe)));
                }
                if (!ok) continue;
                tri.Add(c); aE.Add(a);
            }
            int m = tri.Count;
            var sig = new double[n]; var phi = new double[n];
            double sumW0 = 0, sumW1 = 0;
            for (int gn = 0; gn < gaussNewton; ++gn) {
                // residuals and the blocks
                var w = new Vector2d[m]; var B = new double[m][];    // per triangle, per vertex slot: [dwx/dsig, dwx/dphi, dwy/dsig, dwy/dphi]
                double sumW = 0;
                for (int t = 0; t < m; ++t) {
                    int[] c = tri[t]; var cE = new Vector2d[3];
                    Vector2d ws = Vector2d.Zero;
                    for (int e = 0; e < 3; ++e) {
                        int i = c[e], j = c[(e + 1) % 3];
                        double se = useScale ? 0.5 * (sig[i] + sig[j]) : 0, pe = useRotation ? 0.5 * (phi[i] + phi[j]) : 0;
                        double g = Math.Exp(-se), cs = Math.Cos(pe), sn = Math.Sin(pe);
                        cE[e] = new Vector2d(g * (cs * aE[t][e].x - sn * aE[t][e].y), g * (sn * aE[t][e].x + cs * aE[t][e].y));
                        ws += cE[e];
                    }
                    w[t] = ws; sumW += ws.Length;
                    var b = new double[12];
                    for (int k = 0; k < 3; ++k) {
                        // vertex slot k is the start of edge k and the end of edge k-1
                        Vector2d e1 = cE[k], e2 = cE[(k + 2) % 3];
                        b[4 * k + 0] = useScale ? -0.5 * (e1.x + e2.x) : 0;
                        b[4 * k + 1] = useRotation ? 0.5 * (-e1.y - e2.y) : 0;
                        b[4 * k + 2] = useScale ? -0.5 * (e1.y + e2.y) : 0;
                        b[4 * k + 3] = useRotation ? 0.5 * (e1.x + e2.x) : 0;
                    }
                    B[t] = b;
                }
                if (gn == 0) sumW0 = sumW; sumW1 = sumW;
                // CG on (J^T J + lambda I) d = -J^T w, unknowns d = (sigma, phi) per vertex
                var rhs = new double[2 * n];
                for (int t = 0; t < m; ++t)
                    for (int k = 0; k < 3; ++k) {
                        int v = tri[t][k];
                        rhs[2 * v] -= B[t][4 * k + 0] * w[t].x + B[t][4 * k + 2] * w[t].y;
                        rhs[2 * v + 1] -= B[t][4 * k + 1] * w[t].x + B[t][4 * k + 3] * w[t].y;
                    }
                Func<double[], double[]> A = x => {
                    var y = new double[2 * n];
                    for (int t = 0; t < m; ++t) {
                        double jx = 0, jy = 0;
                        for (int k = 0; k < 3; ++k) { int v = tri[t][k]; jx += B[t][4 * k + 0] * x[2 * v] + B[t][4 * k + 1] * x[2 * v + 1]; jy += B[t][4 * k + 2] * x[2 * v] + B[t][4 * k + 3] * x[2 * v + 1]; }
                        for (int k = 0; k < 3; ++k) { int v = tri[t][k]; y[2 * v] += B[t][4 * k + 0] * jx + B[t][4 * k + 2] * jy; y[2 * v + 1] += B[t][4 * k + 1] * jx + B[t][4 * k + 3] * jy; }
                    }
                    for (int i = 0; i < 2 * n; ++i) y[i] += lambda * x[i];
                    return y;
                };
                var d = new double[2 * n]; var r = (double[])rhs.Clone(); var p = (double[])r.Clone();
                double rr = 0; for (int i = 0; i < 2 * n; ++i) rr += r[i] * r[i];
                double rr0 = rr;
                for (int it = 0; it < 800 && rr > 1e-14 * rr0 && rr > 0; ++it) {
                    var Ap = A(p); double pAp = 0; for (int i = 0; i < 2 * n; ++i) pAp += p[i] * Ap[i];
                    if (!(pAp > 0)) break;
                    double alpha = rr / pAp; double rrn = 0;
                    for (int i = 0; i < 2 * n; ++i) { d[i] += alpha * p[i]; r[i] -= alpha * Ap[i]; rrn += r[i] * r[i]; }
                    double beta = rrn / rr; rr = rrn;
                    for (int i = 0; i < 2 * n; ++i) p[i] = r[i] + beta * p[i];
                }
                for (int i = 0; i < n; ++i) {
                    sig[i] = Math.Max(-maxLog, Math.Min(maxLog, sig[i] + d[2 * i]));
                    phi[i] = bnd[i] ? 0 : Math.Max(-maxRotDeg * Math.PI / 180, Math.Min(maxRotDeg * Math.PI / 180, phi[i] + d[2 * i + 1]));
                }
            }
            // final residual
            double fin = 0;
            for (int t = 0; t < m; ++t) {
                int[] c = tri[t]; Vector2d ws = Vector2d.Zero;
                for (int e = 0; e < 3; ++e) {
                    int i = c[e], j = c[(e + 1) % 3];
                    double se = useScale ? 0.5 * (sig[i] + sig[j]) : 0, pe = useRotation ? 0.5 * (phi[i] + phi[j]) : 0;
                    double g = Math.Exp(-se), cs = Math.Cos(pe), sn = Math.Sin(pe);
                    ws += new Vector2d(g * (cs * aE[t][e].x - sn * aE[t][e].y), g * (sn * aE[t][e].x + cs * aE[t][e].y));
                }
                fin += ws.Length;
            }
            // mean log scale zero (the area mean edge length stays s); the rotation is relative to the field
            double mean = 0; for (int i = 0; i < n; ++i) mean += sig[i]; mean /= Math.Max(1, n);
            double smin = double.MaxValue, smax = double.MinValue, pmax = 0;
            qOut = new Vector3d[n]; scaleOut = new double[n];
            for (int i = 0; i < n; ++i) {
                sig[i] -= mean;
                smin = Math.Min(smin, sig[i]); smax = Math.Max(smax, sig[i]); pmax = Math.Max(pmax, Math.Abs(phi[i]));
                Vector3d q = Math.Cos(phi[i]) * Q[i] + Math.Sin(phi[i]) * N[i].Cross(Q[i]);
                qOut[i] = q / q.Length; scaleOut[i] = Math.Exp(sig[i]);
            }
            report = $"sum|w| {sumW0:F1} -> {fin:F1}; scale factor {Math.Exp(smin):F2}..{Math.Exp(smax):F2}; rotation max {pmax * 180 / Math.PI:F1} deg; solve {swT.Elapsed.TotalSeconds:F2} s";
        }
    }
}
