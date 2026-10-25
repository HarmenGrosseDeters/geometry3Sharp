// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// As-rigid-as-possible parameterization with the local/global method of Liu, Zhang, Xu, Gotsman and
    /// Gortler (2008): minimizes Sum_T A_T |J_T - R_T|^2 over the UVs and a rotation R_T per triangle, so that
    /// lengths and angles are preserved as well as possible (natural scale, free boundary). The local step
    /// takes the rotation closest to each Jacobian, the global step solves a cotan Laplacian system, whose
    /// factorization is computed once.
    ///
    /// Starts from LSCM (or InitialUV). Works on any connected mesh with a boundary. Does not guarantee
    /// a flip-free result (use SLIM for that).
    /// </summary>
    public class ARAPParameterization
    {
        public DMesh3 Mesh;
        public int MaxIterations = 100;
        /// <summary> stop when the energy decreases by less than this fraction in an iteration </summary>
        public double ConvergenceTolerance = 1e-6;
        /// <summary> optional initial UVs by vertex id (default: LSCM, scaled to the surface area) </summary>
        public IList<Vector2d> InitialUV;

        public ParameterizationResult Result;

        public ARAPParameterization(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public ParameterizationResult Compute()
        {
            ParamMesh pm = new ParamMesh(Mesh);
            if (!pm.RequireOpenComponent())
                return Result = pm.Failure();
            int N = pm.N;
            double[] u = new double[N], v = new double[N];
            if (InitialUV != null) {
                for (int i = 0; i < N; ++i) {
                    Vector2d p = InitialUV[pm.ToVertex[i]];
                    u[i] = p.x; v[i] = p.y;
                }
            } else {
                string error;
                ParameterizationStatus status = LSCMParameterization.Solve(pm, null, out u, out v, out error);
                if (status != ParameterizationStatus.Success)
                    return Result = ParameterizationResult.Failure(status, error);
                pm.ScaleToSurfaceArea(u, v);
            }

            // one pinned vertex fixes the translation; the energy is invariant to it
            int pin = pm.BoundaryLoops[0][0];
            bool[] isFixed = new bool[N];
            isFixed[pin] = true;
            double[] fixedU = new double[N], fixedV = new double[N];
            fixedU[pin] = u[pin]; fixedV[pin] = v[pin];
            ConstrainedQuadratic qu = new ConstrainedQuadratic(N, isFixed, fixedU);
            ConstrainedQuadratic qv = new ConstrainedQuadratic(N, isFixed, fixedV);
            foreach (ConstrainedQuadratic q in new[] { qu, qv }) {
                ConstrainedQuadratic qq = q;
                pm.ForEachCotanEdge((i, j, w) => { qq.Add(i, i, w); qq.Add(j, j, w); qq.Add(i, j, -w); });
                if (!qq.Factorize())
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "ARAP system: " + qq.Error);
            }

            int T = pm.Tris.Count;
            double[] angle = new double[T];
            double energy = Energy(pm, u, v, angle);
            int iter = 0;
            bool converged = false;
            double[] bu = new double[N], bv = new double[N];
            while (iter < MaxIterations) {
                iter++;
                // global step: Sum_edges w (u_i - u_j - R e_ij)^2
                Array.Clear(bu, 0, N); Array.Clear(bv, 0, N);
                for (int ti = 0; ti < T; ++ti) {
                    TriangleFrame f = pm.Frames[ti];
                    if (f.Degenerate) continue;
                    Index3i t = pm.Tris[ti];
                    double c = Math.Cos(angle[ti]), s = Math.Sin(angle[ti]);
                    for (int k = 0; k < 3; ++k) {
                        int k1 = (k + 1) % 3, k2 = (k + 2) % 3;
                        double w = 0.5 * f.Cot[k];
                        Vector2d e = f.Corner(k1) - f.Corner(k2);
                        double rx = c * e.x - s * e.y, ry = s * e.x + c * e.y;
                        bu[t[k1]] += w * rx; bu[t[k2]] -= w * rx;
                        bv[t[k1]] += w * ry; bv[t[k2]] -= w * ry;
                    }
                }
                qu.Solve(bu, u);
                qv.Solve(bv, v);
                // local step (in Energy): rotations closest to the new Jacobians
                double e1 = Energy(pm, u, v, angle);
                double decrease = energy - e1;
                energy = e1;
                if (decrease <= ConvergenceTolerance * Math.Max(energy, 1e-12)) {
                    converged = true;
                    break;
                }
            }
            if (!pm.AllFinite(u, v))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "ARAP solution is not finite");
            Result = pm.MakeResult(u, v);
            Result.Iterations = iter;
            Result.Converged = converged;
            Result.Energy = energy;
            return Result;
        }

        /// <summary>
        /// ARAP energy per unit area, Sum A_T |J_T - R_T|^2 / A, with R_T the closest rotations (stored in angle)
        /// </summary>
        internal static double Energy(ParamMesh pm, double[] u, double[] v, double[] angle)
        {
            double e = 0;
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                TriangleFrame f = pm.Frames[ti];
                if (f.Degenerate) { angle[ti] = 0; continue; }
                Index3i t = pm.Tris[ti];
                Matrix2d J = f.Jacobian(new Vector2d(u[t.a], v[t.a]), new Vector2d(u[t.b], v[t.b]), new Vector2d(u[t.c], v[t.c]));
                double a = ParamMath.ClosestRotationAngle(J);
                angle[ti] = a;
                double c = Math.Cos(a), s = Math.Sin(a);
                double d00 = J.m00 - c, d01 = J.m01 + s, d10 = J.m10 - s, d11 = J.m11 - c;
                e += f.Area * (d00 * d00 + d01 * d01 + d10 * d10 + d11 * d11);
            }
            return e / pm.SurfaceArea;
        }
    }
}
