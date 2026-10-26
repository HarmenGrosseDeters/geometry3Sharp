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
    /// factorization is computed once. Triangles that are degenerate on the surface take part with mollified
    /// cotangents.
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
                string error = pm.ReadInitialUV(InitialUV, u, v);
                if (error != null)
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, error);
            } else {
                string error;
                ParameterizationStatus status = LSCMParameterization.Solve(pm, null, out u, out v, out error);
                if (status != ParameterizationStatus.Success)
                    return Result = ParameterizationResult.Failure(status, error);
                pm.ScaleToSurfaceArea(u, v);
            }

            int T = pm.Tris.Count;
            double[] w; Vector2d[] e;
            edgeTargets(pm, out w, out e);

            // one pinned vertex fixes the translation; the energy is invariant to it. u and v share the matrix:
            // one factorization, the pinned value is set before each solve
            int pin = pm.BoundaryLoops[0][0];
            double pinU = u[pin], pinV = v[pin];
            bool[] isFixed = new bool[N];
            isFixed[pin] = true;
            double[] fixedValue = new double[N];
            ConstrainedQuadratic q = new ConstrainedQuadratic(N, isFixed, fixedValue);
            for (int ti = 0; ti < T; ++ti) {
                Index3i t = pm.Tris[ti];
                for (int k = 0; k < 3; ++k) {
                    double wk = w[3 * ti + k];
                    if (wk == 0) continue;
                    int i = t[(k + 1) % 3], j = t[(k + 2) % 3];
                    q.Add(i, i, wk); q.Add(j, j, wk); q.Add(i, j, -wk);
                }
            }
            if (!q.Factorize())
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "ARAP system: " + q.Error);

            double[] angle = new double[T];
            double energy = Energy(pm, w, e, u, v, angle);
            int iter = 0;
            bool converged = false;
            double[] bu = new double[N], bv = new double[N];
            while (iter < MaxIterations) {
                iter++;
                // global step: Sum_edges w (u_i - u_j - R e_ij)^2
                Array.Clear(bu, 0, N); Array.Clear(bv, 0, N);
                for (int ti = 0; ti < T; ++ti) {
                    Index3i t = pm.Tris[ti];
                    double c = Math.Cos(angle[ti]), s = Math.Sin(angle[ti]);
                    for (int k = 0; k < 3; ++k) {
                        double wk = w[3 * ti + k];
                        if (wk == 0) continue;
                        Vector2d ek = e[3 * ti + k];
                        double rx = c * ek.x - s * ek.y, ry = s * ek.x + c * ek.y;
                        int i = t[(k + 1) % 3], j = t[(k + 2) % 3];
                        bu[i] += wk * rx; bu[j] -= wk * rx;
                        bv[i] += wk * ry; bv[j] -= wk * ry;
                    }
                }
                fixedValue[pin] = pinU;
                q.Solve(bu, u);
                fixedValue[pin] = pinV;
                q.Solve(bv, v);
                // local step (in Energy): rotations closest to the new edges
                double e1 = Energy(pm, w, e, u, v, angle);
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

        // per corner k of each triangle: weight w = cot/2 of the angle at k and the 2D edge e opposite k (from
        // corner k+1 to k+2). Sum_k w |M e|^2 = A |M|^2 for any 2x2 M, so Sum w |du - R e|^2 is the ARAP energy
        // A |J - R|^2. Triangles degenerate on the surface get mollified cotangents and a flat layout along their
        // longest edge, so that a vertex used only by them is still held (and a T-junction stays on its edge).
        static void edgeTargets(ParamMesh pm, out double[] w, out Vector2d[] e)
        {
            int T = pm.Tris.Count;
            w = new double[3 * T];
            e = new Vector2d[3 * T];
            for (int ti = 0; ti < T; ++ti) {
                TriangleFrame f = pm.Frames[ti];
                Index3i t = pm.Tris[ti];
                Vector3d cot = f.Cot;
                Vector2d[] corner = { Vector2d.Zero, f.P1, f.P2 };
                if (f.Degenerate) {
                    if (!ParamMesh.MollifiedCotangents(pm.Pos[t.a], pm.Pos[t.b], pm.Pos[t.c], out cot))
                        continue;
                    Vector3d axis = Vector3d.Zero;
                    for (int k = 0; k < 3; ++k) {
                        Vector3d d = pm.Pos[t[(k + 1) % 3]] - pm.Pos[t[k]];
                        if (d.LengthSquared > axis.LengthSquared) axis = d;
                    }
                    axis.Normalize();
                    for (int k = 0; k < 3; ++k)
                        corner[k] = new Vector2d((pm.Pos[t[k]] - pm.Pos[t.a]).Dot(axis), 0);
                }
                for (int k = 0; k < 3; ++k) {
                    w[3 * ti + k] = 0.5 * cot[k];
                    e[3 * ti + k] = corner[(k + 1) % 3] - corner[(k + 2) % 3];
                }
            }
        }

        /// <summary>
        /// ARAP energy per unit area, Sum_T A_T |J_T - R_T|^2 / A, with R_T the closest rotations (stored in
        /// angle): the rotation that best maps the 2D edges e onto the UV edges (for a non-degenerate triangle
        /// the one closest to J_T, since Sum_k w e e^T = A I)
        /// </summary>
        static double Energy(ParamMesh pm, double[] w, Vector2d[] e, double[] u, double[] v, double[] angle)
        {
            double energy = 0;
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                Index3i t = pm.Tris[ti];
                // S = Sum w du e^T
                double s00 = 0, s01 = 0, s10 = 0, s11 = 0;
                for (int k = 0; k < 3; ++k) {
                    int i = t[(k + 1) % 3], j = t[(k + 2) % 3];
                    double wk = w[3 * ti + k], du = u[i] - u[j], dv = v[i] - v[j];
                    Vector2d ek = e[3 * ti + k];
                    s00 += wk * du * ek.x; s01 += wk * du * ek.y; s10 += wk * dv * ek.x; s11 += wk * dv * ek.y;
                }
                double a = Math.Atan2(s10 - s01, s00 + s11);
                angle[ti] = a;
                double c = Math.Cos(a), s = Math.Sin(a);
                for (int k = 0; k < 3; ++k) {
                    int i = t[(k + 1) % 3], j = t[(k + 2) % 3];
                    Vector2d ek = e[3 * ti + k];
                    double rx = u[i] - u[j] - (c * ek.x - s * ek.y), ry = v[i] - v[j] - (s * ek.x + c * ek.y);
                    energy += w[3 * ti + k] * (rx * rx + ry * ry);
                }
            }
            return energy / pm.SurfaceArea;
        }
    }
}
