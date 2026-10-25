// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Tutte embedding (Tutte 1963; Floater 1997, 2003): the boundary is mapped to a circle by arc length and
    /// every interior vertex to the average of its neighbours. For a disc, the map is guaranteed to be
    /// a bijection without flipped triangles (the boundary is strictly convex and the weights positive), but it
    /// distorts a lot; it is the initialization of SLIM.
    /// </summary>
    public class TutteEmbedding
    {
        public DMesh3 Mesh;
        /// <summary> scale the result so that the UV area equals the surface area (else the unit circle) </summary>
        public bool ScaleToSurfaceArea = true;

        public ParameterizationResult Result;

        public TutteEmbedding(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public ParameterizationResult Compute()
        {
            ParamMesh pm = new ParamMesh(Mesh);
            if (!pm.RequireDisc())
                return Result = pm.Failure();
            double[] u, v;
            string error;
            if (!Solve(pm, out u, out v, out error))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, error);
            if (ScaleToSurfaceArea)
                pm.ScaleToSurfaceArea(u, v);
            return Result = pm.MakeResult(u, v);
        }

        internal static bool Solve(ParamMesh pm, out double[] u, out double[] v, out string error)
        {
            int N = pm.N;
            u = new double[N]; v = new double[N];
            error = null;
            int[] loop = pm.BoundaryLoops[0];
            int m = loop.Length;
            double[] arc = new double[m + 1];
            for (int k = 0; k < m; ++k)
                arc[k + 1] = arc[k] + pm.Pos[loop[k]].Distance(pm.Pos[loop[(k + 1) % m]]);
            bool[] isFixed = new bool[N];
            double[] fu = new double[N], fv = new double[N];
            for (int k = 0; k < m; ++k) {
                double a = 2 * Math.PI * (arc[m] > 0 ? arc[k] / arc[m] : (double)k / m);
                isFixed[loop[k]] = true;
                fu[loop[k]] = Math.Cos(a); fv[loop[k]] = Math.Sin(a);
            }
            // uniform graph Laplacian over the edges
            var edges = new HashSet<long>();
            ConstrainedQuadratic qu = new ConstrainedQuadratic(N, isFixed, fu);
            ConstrainedQuadratic qv = new ConstrainedQuadratic(N, isFixed, fv);
            foreach (Index3i t in pm.Tris)
                for (int j = 0; j < 3; ++j) {
                    int a = t[j], b = t[(j + 1) % 3];
                    if (!edges.Add(a < b ? (long)a * N + b : (long)b * N + a))
                        continue;
                    foreach (ConstrainedQuadratic q in new[] { qu, qv }) {
                        q.Add(a, a, 1); q.Add(b, b, 1); q.Add(a, b, -1);
                    }
                }
            if (!qu.Factorize() || !qv.Factorize()) {
                error = "Tutte system: " + (qu.Error ?? qv.Error);
                return false;
            }
            qu.Solve(null, u);
            qv.Solve(null, v);
            return pm.AllFinite(u, v);
        }
    }


    /// <summary>
    /// Scalable locally injective mappings (Rabinovich, Poranne, Panozzo, Sorkine-Hornung 2017): minimizes
    /// the symmetric Dirichlet energy Sum_T A_T (|J|^2 + |J^-1|^2), which goes to infinity when a triangle
    /// degenerates, starting from the flip-free Tutte embedding. Each iteration solves a reweighted
    /// ARAP-like proxy (local/global with weights W_T from the singular values of J_T) and does a line search
    /// whose step is limited so that no triangle flips (Smith and Schaefer 2015).
    ///
    /// Guarantees: for a disc, the result has no flipped (or degenerate) UV triangles, including triangles
    /// that are degenerate on the surface. The boundary is free and may in rare cases overlap itself (local,
    /// not global injectivity).
    /// </summary>
    public class SLIMParameterization
    {
        public DMesh3 Mesh;
        public int MaxIterations = 100;
        /// <summary> stop when the energy decreases by less than this fraction in an iteration </summary>
        public double ConvergenceTolerance = 1e-5;
        /// <summary>
        /// optional initial UVs by vertex id, which must have no flipped triangles (default: Tutte embedding)
        /// </summary>
        public IList<Vector2d> InitialUV;

        public ParameterizationResult Result;

        public SLIMParameterization(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public ParameterizationResult Compute()
        {
            ParamMesh pm = new ParamMesh(Mesh);
            int N = pm.N;
            double[] u, v;
            if (InitialUV != null) {
                if (!pm.RequireOpenComponent())
                    return Result = pm.Failure();
                u = new double[N]; v = new double[N];
                for (int i = 0; i < N; ++i) {
                    Vector2d p = InitialUV[pm.ToVertex[i]];
                    u[i] = p.x; v[i] = p.y;
                }
            } else {
                if (!pm.RequireDisc())
                    return Result = pm.Failure();
                string error;
                if (!TutteEmbedding.Solve(pm, out u, out v, out error))
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, error);
                pm.ScaleToSurfaceArea(u, v);
            }
            int T = pm.Tris.Count;
            for (int ti = 0; ti < T; ++ti)
                if (!(ParamMesh.SignedArea2(u, v, pm.Tris[ti]) > 0))
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints,
                        "initial UVs have flipped or degenerate triangles");

            int pin = pm.BoundaryLoops[0][0];
            bool[] isFixed = new bool[2 * N];
            double[] fixedValue = new double[2 * N];
            ConstrainedQuadratic q = null;
            double energy = Energy(pm, u, v);
            int iter = 0;
            bool converged = false;
            double[] x = new double[2 * N], b = new double[2 * N];
            double[] du = new double[N], dv = new double[N], tu = new double[N], tv = new double[N];
            while (iter < MaxIterations) {
                iter++;
                // the proxy is translation invariant: pin one vertex at its current position
                isFixed[2 * pin] = isFixed[2 * pin + 1] = true;
                fixedValue[2 * pin] = u[pin]; fixedValue[2 * pin + 1] = v[pin];
                if (q == null)
                    q = new ConstrainedQuadratic(2 * N, isFixed, fixedValue, 2);
                else
                    q.Clear();
                Array.Clear(b, 0, b.Length);
                assembleProxy(pm, u, v, q, b);
                if (!q.Factorize())
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "SLIM proxy system: " + q.Error);
                q.Solve(b, x);
                for (int i = 0; i < N; ++i) { du[i] = x[2 * i] - u[i]; dv[i] = x[2 * i + 1] - v[i]; }

                // line search, never past the first step at which a triangle degenerates
                double t = Math.Min(1.0, 0.9 * maxInjectiveStep(pm, u, v, du, dv));
                double e1 = double.PositiveInfinity;
                while (t > 1e-12) {
                    for (int i = 0; i < N; ++i) { tu[i] = u[i] + t * du[i]; tv[i] = v[i] + t * dv[i]; }
                    e1 = Energy(pm, tu, tv);
                    if (e1 <= energy)
                        break;
                    t *= 0.5;
                }
                if (!(e1 <= energy)) {
                    converged = true;   // no descent possible along the proxy direction
                    break;
                }
                Array.Copy(tu, u, N); Array.Copy(tv, v, N);
                double decrease = energy - e1;
                energy = e1;
                if (decrease <= ConvergenceTolerance * energy) {
                    converged = true;
                    break;
                }
            }
            if (!pm.AllFinite(u, v))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "SLIM solution is not finite");
            Result = pm.MakeResult(u, v);
            Result.Iterations = iter;
            Result.Converged = converged;
            Result.Energy = energy;
            return Result;
        }

        /// <summary> symmetric Dirichlet energy per unit area (infinity if a triangle is flipped) </summary>
        internal static double Energy(ParamMesh pm, double[] u, double[] v)
        {
            double e = 0;
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                TriangleFrame f = pm.Frames[ti];
                if (f.Degenerate) continue;
                Index3i t = pm.Tris[ti];
                Matrix2d J = f.Jacobian(new Vector2d(u[t.a], v[t.a]), new Vector2d(u[t.b], v[t.b]), new Vector2d(u[t.c], v[t.c]));
                double det = J.m00 * J.m11 - J.m01 * J.m10;
                if (!(det > 0))
                    return double.PositiveInfinity;
                double fro = J.m00 * J.m00 + J.m01 * J.m01 + J.m10 * J.m10 + J.m11 * J.m11;
                // |J^-1|^2 = |J|^2 / det^2 for 2x2
                e += f.Area * (fro + fro / (det * det));
            }
            return e / pm.SurfaceArea;
        }

        // proxy Sum_T A_T |W_T (J_T - R_T)|^2, W_T = U diag(w1,w2) U^T, w_i = sqrt(f'(s_i) / (2 (s_i - 1)))
        // with f(s) = s^2 + s^-2, which simplifies to w = sqrt((s+1)(s^2+1)/s^3)
        static void assembleProxy(ParamMesh pm, double[] u, double[] v, ConstrainedQuadratic q, double[] b)
        {
            double[] coef = new double[6];
            int[] var = new int[6];
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                TriangleFrame f = pm.Frames[ti];
                Index3i t = pm.Tris[ti];
                if (f.Degenerate) {
                    // keep the nonzero pattern the same in every iteration
                    for (int k = 0; k < 3; ++k)
                        for (int l = 0; l < 3; ++l)
                            for (int c1 = 0; c1 < 2; ++c1)
                                for (int c2 = 0; c2 < 2; ++c2)
                                    if (2 * t[k] + c1 <= 2 * t[l] + c2)
                                        q.Add(2 * t[k] + c1, 2 * t[l] + c2, 0);
                    continue;
                }
                Matrix2d J = f.Jacobian(new Vector2d(u[t.a], v[t.a]), new Vector2d(u[t.b], v[t.b]), new Vector2d(u[t.c], v[t.c]));
                double s1, s2, phi, theta;
                ParamMath.SignedSVD(J, out s1, out s2, out phi, out theta);
                double w1 = weight(s1), w2 = weight(s2);
                double cp = Math.Cos(phi), sp = Math.Sin(phi);
                // W = U diag(w1, w2) U^T with U = Rot(phi)
                double W00 = w1 * cp * cp + w2 * sp * sp, W11 = w1 * sp * sp + w2 * cp * cp, W01 = (w1 - w2) * cp * sp;
                double ra = phi + theta, cr = Math.Cos(ra), sr = Math.Sin(ra);
                // R = [[cr, -sr], [sr, cr]], columns c = x, y
                for (int col = 0; col < 2; ++col) {
                    Vector3d D = col == 0 ? f.Dx : f.Dy;
                    double R0 = col == 0 ? cr : -sr, R1 = col == 0 ? sr : cr;
                    for (int row = 0; row < 2; ++row) {
                        double Wr0 = row == 0 ? W00 : W01, Wr1 = row == 0 ? W01 : W11;
                        // residual = Wr0 (D.u - R0) + Wr1 (D.v - R1)
                        for (int k = 0; k < 3; ++k) {
                            var[2 * k] = 2 * t[k]; coef[2 * k] = Wr0 * D[k];
                            var[2 * k + 1] = 2 * t[k] + 1; coef[2 * k + 1] = Wr1 * D[k];
                        }
                        double target = Wr0 * R0 + Wr1 * R1;
                        for (int p = 0; p < 6; ++p) {
                            b[var[p]] += f.Area * coef[p] * target;
                            for (int r = p; r < 6; ++r)
                                q.Add(var[p], var[r], (p == r ? 1 : (var[p] == var[r] ? 2 : 1)) * f.Area * coef[p] * coef[r]);
                        }
                    }
                }
            }
        }

        static double weight(double s)
        {
            s = Math.Max(Math.Abs(s), 1e-8);
            return Math.Sqrt((s + 1) * (s * s + 1) / (s * s * s));
        }

        // largest t in (0, inf) such that no triangle has zero area for any step in [0, t)
        static double maxInjectiveStep(ParamMesh pm, double[] u, double[] v, double[] du, double[] dv)
        {
            double tmax = double.PositiveInfinity;
            // all triangles, also those degenerate on the surface (which have no energy), keep their orientation
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                Index3i t = pm.Tris[ti];
                double ax = u[t.b] - u[t.a], ay = v[t.b] - v[t.a], bx = u[t.c] - u[t.a], by = v[t.c] - v[t.a];
                double dax = du[t.b] - du[t.a], day = dv[t.b] - dv[t.a], dbx = du[t.c] - du[t.a], dby = dv[t.c] - dv[t.a];
                // area(t) = c0 + c1 t + c2 t^2
                double c0 = ax * by - ay * bx;
                double c1 = ax * dby + dax * by - ay * dbx - day * bx;
                double c2 = dax * dby - day * dbx;
                double r = smallestPositiveRoot(c2, c1, c0);
                if (r < tmax) tmax = r;
            }
            return tmax;
        }

        static double smallestPositiveRoot(double a, double b, double c)
        {
            double scale = Math.Max(Math.Abs(b), Math.Abs(c));
            if (Math.Abs(a) <= 1e-14 * scale) {
                if (b == 0) return double.PositiveInfinity;
                double t = -c / b;
                return t > 0 ? t : double.PositiveInfinity;
            }
            double disc = b * b - 4 * a * c;
            if (disc < 0)
                return double.PositiveInfinity;
            double sq = Math.Sqrt(disc);
            // stable roots
            double qq = -0.5 * (b + (b >= 0 ? sq : -sq));
            double r1 = qq / a, r2 = qq != 0 ? c / qq : double.PositiveInfinity;
            double best = double.PositiveInfinity;
            if (r1 > 0) best = Math.Min(best, r1);
            if (r2 > 0) best = Math.Min(best, r2);
            return best;
        }
    }
}
