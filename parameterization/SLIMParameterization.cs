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
            // uniform graph Laplacian over the edges; u and v share it (one factorization, the boundary values
            // are set before each solve)
            var edges = new HashSet<long>();
            double[] fixedValue = new double[N];
            ConstrainedQuadratic q = new ConstrainedQuadratic(N, isFixed, fixedValue);
            foreach (Index3i t in pm.Tris)
                for (int j = 0; j < 3; ++j) {
                    int a = t[j], b = t[(j + 1) % 3];
                    if (!edges.Add(a < b ? (long)a * N + b : (long)b * N + a))
                        continue;
                    q.Add(a, a, 1); q.Add(b, b, 1); q.Add(a, b, -1);
                }
            if (!q.Factorize()) {
                error = "Tutte system: " + q.Error;
                return false;
            }
            Array.Copy(fu, fixedValue, N);
            q.Solve(null, u);
            Array.Copy(fv, fixedValue, N);
            q.Solve(null, v);
            if (!pm.AllFinite(u, v)) {
                error = "Tutte solution is not finite";
                return false;
            }
            return true;
        }
    }


    /// <summary>
    /// Scalable locally injective mappings (Rabinovich, Poranne, Panozzo, Sorkine-Hornung 2017): minimizes
    /// the symmetric Dirichlet energy Sum_T A_T (|J|^2 + |J^-1|^2), which goes to infinity when a triangle
    /// degenerates, starting from a flip-free map (LSCM if it is injective, else the Tutte embedding; see
    /// StartMap). Each iteration solves a reweighted
    /// ARAP-like proxy (with weights W_T from the singular values of J_T) and does a line search whose step is
    /// limited so that no triangle flips (Smith and Schaefer 2015).
    ///
    /// The proxy step is -H^-1 grad E, with H the proxy matrix (its gradient at the current UVs is the energy
    /// gradient). With ReuseFactorization, the factorization of H is kept for further iterations (a lagged
    /// Hessian) while each step lowers the energy at least at the average rate since the factorization was made,
    /// counting the factorization as 8 steps; this saves most factorizations. Converged means the proxy predicts a decrease below ConvergenceTolerance
    /// (the Newton decrement, on a new factorization), not merely that one step decreased little.
    ///
    /// Guarantees: for a disc, the result has no flipped (or degenerate) UV triangles, including triangles
    /// that are degenerate on the surface. The boundary is free and may in rare cases overlap itself (local,
    /// not global injectivity).
    /// </summary>
    public class SLIMParameterization
    {
        public DMesh3 Mesh;
        public int MaxIterations = 100;
        /// <summary>
        /// stop when the decrease predicted by the proxy for a full step, relative to the energy, is below this
        /// </summary>
        public double ConvergenceTolerance = 1e-5;
        /// <summary>
        /// optional initial UVs by vertex id, which must have no flipped triangles (default: see StartMap)
        /// </summary>
        public IList<Vector2d> InitialUV;

        public enum StartMaps
        {
            /// <summary>
            /// the LSCM map if it is injective (no flipped triangle, no overlap; exact test) and has no sliver
            /// UV triangle, else the Tutte embedding; if SLIM gets stuck from LSCM it starts over from Tutte. LSCM is usually much closer to the minimum: on a 48k vertex ellipsoid cap SLIM needs 5
            /// iterations from it instead of over 100 from Tutte.
            /// </summary>
            Auto = 0,
            /// <summary> the Tutte embedding (always injective, but strongly distorted) </summary>
            Tutte = 1
        }
        /// <summary> the initial map when InitialUV is null </summary>
        public StartMaps StartMap = StartMaps.Auto;
        /// <summary> keep the factorization of the proxy matrix while its steps are good (else one per iteration) </summary>
        public bool ReuseFactorization = true;
        /// <summary> called after each step with the iteration number and the new energy (eg for progress or timing) </summary>
        public Action<int, double> IterationCompleted;

        // cost of a factorization in steps with an existing one (assembly and numeric factorization against
        // gradient, solve and line search; measured about 8-10 at 12k-48k vertices)
        const double FactorCost = 8;

        public ParameterizationResult Result;
        /// <summary> factorizations of the proxy matrix in the last Compute() </summary>
        public int Factorizations;
        /// <summary> the last Compute() started from the LSCM map (StartMap Auto), not from the Tutte embedding </summary>
        public bool StartedFromLSCM;

        public SLIMParameterization(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public ParameterizationResult Compute()
        {
            Factorizations = 0;
            StartedFromLSCM = false;
            ParamMesh pm = new ParamMesh(Mesh);
            int N = pm.N;
            double[] u, v;
            if (InitialUV != null) {
                if (!pm.RequireOpenComponent())
                    return Result = pm.Failure();
                u = new double[N]; v = new double[N];
                string error = pm.ReadInitialUV(InitialUV, u, v);
                if (error != null)
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, error);
            } else {
                if (!pm.RequireDisc())
                    return Result = pm.Failure();
                if (!(StartMap == StartMaps.Auto && injectiveLSCM(pm, out u, out v)) && !tutte(pm, out u, out v, out string error))
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, error);
            }
            for (int ti = 0; ti < pm.Tris.Count; ++ti)
                if (!(ParamMesh.SignedArea2(u, v, pm.Tris[ti]) > 0))
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints,
                        "initial UVs have flipped or degenerate triangles");

            int iter;
            double energy;
            bool converged, stuck;
            string failure = minimize(pm, u, v, out iter, out energy, out converged, out stuck);
            if (failure == null && stuck && StartedFromLSCM) {
                // the LSCM start can still hold a nearly degenerate triangle that blocks every step: start over
                // from the Tutte embedding
                StartedFromLSCM = false;
                if (!tutte(pm, out u, out v, out failure))
                    return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, failure);
                int iterLSCM = iter;
                failure = minimize(pm, u, v, out iter, out energy, out converged, out stuck);
                iter += iterLSCM;
            }
            if (failure != null)
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, failure);
            if (!pm.AllFinite(u, v))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "SLIM solution is not finite");
            Result = pm.MakeResult(u, v);
            Result.Iterations = iter;
            Result.Converged = converged;
            Result.Energy = energy;
            return Result;
        }

        static bool tutte(ParamMesh pm, out double[] u, out double[] v, out string error)
        {
            if (!TutteEmbedding.Solve(pm, out u, out v, out error))
                return false;
            pm.ScaleToSurfaceArea(u, v);
            return true;
        }

        // SLIM iterations from (u, v), which are updated. Returns an error message if the proxy cannot be
        // factorized. stuck: no step lowers the energy although the proxy predicts a decrease.
        string minimize(ParamMesh pm, double[] u, double[] v, out int iter, out double energy, out bool converged, out bool stuck)
        {
            int N = pm.N;
            // the proxy is translation invariant: one vertex does not move (its step is fixed at 0)
            int pin = pm.BoundaryLoops[0][0];
            bool[] isFixed = new bool[2 * N];
            isFixed[2 * pin] = isFixed[2 * pin + 1] = true;
            ConstrainedQuadratic q = new ConstrainedQuadratic(2 * N, isFixed, new double[2 * N], 2);
            energy = Energy(pm, u, v);
            iter = 0;
            converged = stuck = false;
            bool factored = false;
            // steps and energy decrease since the last factorization
            int sinceFactor = 0;
            double sinceDecrease = 0;
            double[] g = new double[2 * N], d = new double[2 * N];
            double[] du = new double[N], dv = new double[N], tu = new double[N], tv = new double[N];
            while (iter < MaxIterations) {
                iter++;
                bool fresh = !factored;
                if (fresh) {
                    q.Clear();
                    assembleProxy(pm, u, v, q);
                    Factorizations++;
                    if (!q.Factorize())
                        return "SLIM proxy system: " + q.Error;
                    factored = ReuseFactorization;
                }
                // the minimizer of the quadratic model 1/2 d^T H d + g^T d (q holds H / 2 and gets g / 2)
                HalfGradient(pm, u, v, g);
                for (int i = 0; i < 2 * N; ++i) g[i] = -g[i];
                q.Solve(g, d);
                double slope = 0;   // -grad E . d, per unit area: twice the decrease the model predicts
                for (int i = 0; i < 2 * N; ++i) slope += g[i] * d[i];
                slope *= 2 / pm.SurfaceArea;
                for (int i = 0; i < N; ++i) { du[i] = d[2 * i]; dv[i] = d[2 * i + 1]; }
                if (!(0.5 * slope > ConvergenceTolerance * energy)) {
                    // the model predicts (almost) no decrease: converged, or check with a new factorization (a pass
                    // that only refactorizes is not counted as an iteration)
                    if (fresh) {
                        converged = true;
                        break;
                    }
                    factored = false;
                    iter--;
                    continue;
                }

                double e1 = LineSearch(pm, u, v, du, dv, energy, tu, tv);
                if (!(e1 < energy)) {
                    // no step lowers the energy: with an old factorization try a new one; else converged if the
                    // step itself is negligible (at a minimum the proxy reproduces the current UVs), else stuck
                    if (!fresh) {
                        factored = false;
                        iter--;
                        continue;
                    }
                    double step = 0;
                    for (int i = 0; i < N; ++i) step = Math.Max(step, Math.Max(Math.Abs(du[i]), Math.Abs(dv[i])));
                    converged = step <= 1e-6 * Math.Sqrt(pm.SurfaceArea);
                    stuck = !converged;
                    break;
                }
                Array.Copy(tu, u, N); Array.Copy(tv, v, N);
                // keep the factorization while its steps decrease the energy at least at the average rate since
                // it was made, counting the factorization as FactorCost steps (the marginal value rule)
                double decrease = energy - e1;
                if (fresh) {
                    sinceFactor = 0;
                    sinceDecrease = 0;
                }
                sinceFactor++;
                sinceDecrease += decrease;
                if (!fresh && decrease < sinceDecrease / (FactorCost + sinceFactor))
                    factored = false;
                energy = e1;
                IterationCompleted?.Invoke(iter, energy);
            }
            return null;
        }

        // the LSCM map (free boundary) scaled to the surface area, if it is injective (no flipped or degenerate
        // triangle, no overlap; exact test) and has no sliver UV triangle
        bool injectiveLSCM(ParamMesh pm, out double[] u, out double[] v)
        {
            string error;
            StartedFromLSCM = LSCMParameterization.Solve(pm, null, out u, out v, out error) == ParameterizationStatus.Success
                && pm.AllFinite(u, v);
            if (StartedFromLSCM) {
                pm.ScaleToSurfaceArea(u, v);
                Vector2d[] uv = new Vector2d[Mesh.MaxVertexID];
                for (int i = 0; i < pm.N; ++i)
                    uv[pm.ToVertex[i]] = new Vector2d(u[i], v[i]);
                // no nearly degenerate UV triangle (LSCM keeps the apex of a triangle of zero surface area on its
                // edge), which would block the steps of SLIM
                for (int ti = 0; ti < pm.Tris.Count && StartedFromLSCM; ++ti) {
                    Index3i t = pm.Tris[ti];
                    double maxLenSqr = 0;
                    for (int k = 0; k < 3; ++k) {
                        int i = t[k], j = t[(k + 1) % 3];
                        maxLenSqr = Math.Max(maxLenSqr, (u[i] - u[j]) * (u[i] - u[j]) + (v[i] - v[j]) * (v[i] - v[j]));
                    }
                    StartedFromLSCM = ParamMesh.SignedArea2(u, v, t) > 1e-6 * maxLenSqr;
                }
                StartedFromLSCM = StartedFromLSCM && UVOverlap.Compute(Mesh, uv, 0).IsInjective;
            }
            return StartedFromLSCM;
        }

        /// <summary>
        /// Backtracking line search along (du, dv) for a strict decrease of the energy, starting at the full step
        /// or 0.9 of the first step at which a triangle degenerates. Returns the new energy with the UVs in
        /// (tu, tv), or a value not below energy if no step down to 1e-12 lowers it.
        /// </summary>
        internal static double LineSearch(ParamMesh pm, double[] u, double[] v, double[] du, double[] dv, double energy,
            double[] tu, double[] tv)
        {
            int N = pm.N;
            double t = Math.Min(1.0, 0.9 * maxInjectiveStep(pm, u, v, du, dv));
            double e1 = double.PositiveInfinity;
            while (t > 1e-12) {
                for (int i = 0; i < N; ++i) { tu[i] = u[i] + t * du[i]; tv[i] = v[i] + t * dv[i]; }
                e1 = Energy(pm, tu, tv);
                if (e1 < energy)
                    break;
                t *= 0.5;
            }
            return e1;
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

        // half the gradient of Sum_T A_T f(J_T), f = |J|^2 (1 + det^-2), interleaved (u, v) per vertex
        internal static void HalfGradient(ParamMesh pm, double[] u, double[] v, double[] g)
        {
            Array.Clear(g, 0, g.Length);
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                TriangleFrame f = pm.Frames[ti];
                if (f.Degenerate) continue;
                Index3i t = pm.Tris[ti];
                Matrix2d J = f.Jacobian(new Vector2d(u[t.a], v[t.a]), new Vector2d(u[t.b], v[t.b]), new Vector2d(u[t.c], v[t.c]));
                double a = J.m00, b = J.m01, c = J.m10, dd = J.m11;
                double det = a * dd - b * c, fro = a * a + b * b + c * c + dd * dd;
                double s = 1 + 1 / (det * det), k = fro / (det * det * det);
                // half of df/dJ: J (1 + det^-2) - |J|^2 det^-3 cof(J)
                double ga = f.Area * (a * s - k * dd), gb = f.Area * (b * s + k * c);
                double gc = f.Area * (c * s + k * b), gd = f.Area * (dd * s - k * a);
                for (int j = 0; j < 3; ++j) {
                    g[2 * t[j]] += ga * f.Dx[j] + gb * f.Dy[j];
                    g[2 * t[j] + 1] += gc * f.Dx[j] + gd * f.Dy[j];
                }
            }
        }

        // proxy Sum_T A_T |W_T (J_T - R_T)|^2, W_T = U diag(w1,w2) U^T, w_i = sqrt(f'(s_i) / (2 (s_i - 1)))
        // with f(s) = s^2 + s^-2, which simplifies to w^2 = (s+1)(s^2+1)/s^3. q gets half its Hessian,
        // A_T (W_T^2 kron (Dx Dx^T + Dy Dy^T)) on the variables (u, v) of the corners.
        static void assembleProxy(ParamMesh pm, double[] u, double[] v, ConstrainedQuadratic q)
        {
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                TriangleFrame f = pm.Frames[ti];
                Index3i t = pm.Tris[ti];
                if (f.Degenerate) {
                    // no energy (and no Jacobian) on the surface: unit springs on its UV edges, so a vertex used
                    // only by such triangles is still held (the matrix stays positive definite)
                    for (int k = 0; k < 3; ++k) {
                        int i = t[k], j = t[(k + 1) % 3];
                        for (int c = 0; c < 2; ++c) {
                            int vi = 2 * i + c, vj = 2 * j + c;
                            q.Add(vi, vi, 1); q.Add(vj, vj, 1); q.Add(vi, vj, -1);
                        }
                    }
                    continue;
                }
                Matrix2d J = f.Jacobian(new Vector2d(u[t.a], v[t.a]), new Vector2d(u[t.b], v[t.b]), new Vector2d(u[t.c], v[t.c]));
                double s1, s2, phi, theta;
                ParamMath.SignedSVD(J, out s1, out s2, out phi, out theta);
                double w1 = weight2(s1), w2 = weight2(s2);
                double cp = Math.Cos(phi), sp = Math.Sin(phi);
                // W^2 = U diag(w1^2, w2^2) U^T with U = Rot(phi)
                double W00 = w1 * cp * cp + w2 * sp * sp, W11 = w1 * sp * sp + w2 * cp * cp, W01 = (w1 - w2) * cp * sp;
                for (int k = 0; k < 3; ++k)
                    for (int l = k; l < 3; ++l) {
                        double L = f.Area * (f.Dx[k] * f.Dx[l] + f.Dy[k] * f.Dy[l]);
                        int uk = 2 * t[k], ul = 2 * t[l];
                        q.Add(uk, ul, W00 * L);
                        q.Add(uk + 1, ul + 1, W11 * L);
                        q.Add(uk, ul + 1, W01 * L);
                        if (l != k)
                            q.Add(uk + 1, ul, W01 * L);
                    }
            }
        }

        static double weight2(double s)
        {
            s = Math.Max(Math.Abs(s), 1e-8);
            return (s + 1) * (s * s + 1) / (s * s * s);
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
