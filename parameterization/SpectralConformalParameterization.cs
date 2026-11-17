// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Spectral conformal parameterization (Mullen, Tong, Alliez, Desbrun, "Spectral Conformal Parameterization",
    /// SGP 2008), an opt-in next to LSCMParameterization for disc charts: the same conformal energy
    /// E_C = E_D - A(uv) (cotan weights, mollified on degenerate surface triangles, and the signed UV area as a sum over
    /// the boundary edges), but without pins. E_C is minimized subject to x^T B x = 1 and x B-orthogonal to the two
    /// translations, B the lumped boundary mass (each boundary vertex: half the length of its two boundary edges). The
    /// minimizer is the eigenvector of A x = lambda B x of the smallest nonzero eigenvalue. As there is no pin choice,
    /// the map has the symmetries of the input (a mirror-symmetric disc gives a mirror-symmetric map to rounding, which
    /// LSCM gives only with pins at a mirrored pair). On smooth curved discs the maximum conformal distortion is lower
    /// than LSCM's or within 0.1 % of it (docs/research/GEOGRAM_MEASUREMENTS_2026-11.md, item 1); on the charts of real atlases the
    /// two are about even, and either one can be much worse on a given chart (benchmarks/UVAtlasBenchmark/README.md,
    /// chart-ab). It costs about 2x LSCM's time (inverse iterations on one factorization, and an estimate of the next
    /// eigenvalue pair).
    ///
    /// Only for discs (one boundary loop, Euler characteristic 1): any other chart is refused with
    /// ParameterizationStatus.InvalidTopology. On annuli (eg an open cylinder) the smallest eigenvalue is double or
    /// nearly so (two maps, either ring inside) and the computed eigenvector can be a folded mixture of both. The same is
    /// checked on discs: the next eigenvalue pair is estimated, and if it is within MinEigenvalueGap of the smallest, or
    /// the iteration does not converge (which means a small gap), the map is refused with
    /// ParameterizationStatus.SolverFailed (NearlyDoubleEigenvalue is set); so is a chart whose shifted matrix is not
    /// positive definite (more null modes than the translations, eg flat pieces joined only through zero-area
    /// triangles). In all these cases use LSCMParameterization.
    /// A computed map with flipped or degenerate UV triangles is reported as ParameterizationStatus.FlippedTriangles, as
    /// for LSCM (no flip-free guarantee; use SLIM for that). Deterministic: no parallel loop, and a start vector from the
    /// geometry (the vertices projected on the plane normal to the vector area of the surface).
    /// Degenerate surface triangles get mollified cotan weights, as in LSCM.
    /// </summary>
    /// <example>
    /// var r = new SpectralConformalParameterization(disc).Compute();
    /// if (!r.Succeeded) r = new LSCMParameterization(disc).Compute();
    /// </example>
    public class SpectralConformalParameterization
    {
        public DMesh3 Mesh;
        /// <summary>
        /// scale the result so that the UV area equals the surface area (centred at the UV centroid); false: the
        /// eigenvector as computed (largest coordinate 1, centred at the boundary's mass centre)
        /// </summary>
        public bool ScaleToSurfaceArea = true;
        /// <summary> the iteration stops when the B-norm of the change of the B-normalized iterate is at most this </summary>
        public double Tolerance = 1e-10;
        public int MaxIterations = 200;
        /// <summary> refuse unless the next eigenvalue pair is at least (1 + MinEigenvalueGap) times the smallest </summary>
        public double MinEigenvalueGap = 0.1;

        public ParameterizationResult Result;
        /// <summary>
        /// the smallest eigenvalue apart from the translations' zero (relative to trace(A) / trace(B), so scale-free; at
        /// rounding level on a developable disc)
        /// </summary>
        public double Eigenvalue;
        /// <summary>
        /// estimate of the next eigenvalue pair, relative as Eigenvalue (an upper bound: a Rayleigh quotient), or NaN if
        /// it could not be computed
        /// </summary>
        public double NextEigenvalue;
        /// <summary> inverse iterations of the smallest pair, and of the estimate of the next one </summary>
        public int Iterations, NextIterations;
        /// <summary>
        /// the last Compute() refused because the smallest eigenvalue is (nearly) double, or its iteration did not converge
        /// in MaxIterations (which a small gap causes, but also a MaxIterations set too low)
        /// </summary>
        public bool NearlyDoubleEigenvalue;

        // shift of A + sigma B, relative to trace(A) / trace(B) (A alone is singular: the translations)
        const double ShiftFraction = 1e-9;
        // an eigenvalue at most this (relative) is rounding: the next pair must be above it, else the smallest
        // eigenvalue has more than one map (a developable chart with few interior vertices)
        const double ZeroEigenvalue = 1e-12;
        // the estimate of the next pair stops when its Rayleigh quotient changes by less than this, relative
        const double NextTolerance = 1e-3;
        const int NextMaxIterations = 30;
        // NextMaxIterations, settable by the tests (an estimate that does not settle is refused)
        internal int NextMaxIterationsLimit = NextMaxIterations;
        // the change of the iterate below which a stall (no more decrease) counts as converged
        const double StallTolerance = 1e-6;

        public SpectralConformalParameterization(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public ParameterizationResult Compute()
        {
            Eigenvalue = NextEigenvalue = double.NaN;
            Iterations = NextIterations = 0;
            NearlyDoubleEigenvalue = false;
            if (MaxIterations < 1 || !(Tolerance >= 0) || !(MinEigenvalueGap >= 0))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints,
                    "spectral conformal: MaxIterations must be at least 1, Tolerance and MinEigenvalueGap at least 0");
            ParamMesh pm = new ParamMesh(Mesh);
            if (!pm.RequireDisc())
                return Result = pm.Failure();
            int n = pm.N, N = 2 * n;
            // A: the LSCM energy (1/2 x^T A x = E_C), as (row, col, value) with each off-diagonal pair once
            var rows = new List<int>(); var cols = new List<int>(); var vals = new List<double>();
            Action<int, int, double> add = (r, c, v) => { rows.Add(r); cols.Add(c); vals.Add(v); };
            pm.ForEachCotanEdge((i, j, w) => {
                add(2 * i, 2 * i, w); add(2 * j, 2 * j, w); add(2 * i, 2 * j, -w);
                add(2 * i + 1, 2 * i + 1, w); add(2 * j + 1, 2 * j + 1, w); add(2 * i + 1, 2 * j + 1, -w);
            }, true);
            double[] mass = new double[n];
            foreach (int[] loop in pm.BoundaryLoops)
                for (int k = 0; k < loop.Length; ++k) {
                    int i = loop[k], j = loop[(k + 1) % loop.Length];
                    add(2 * i, 2 * j + 1, -0.5);
                    add(2 * j, 2 * i + 1, 0.5);
                    double len = pm.Pos[i].Distance(pm.Pos[j]);
                    mass[i] += 0.5 * len; mass[j] += 0.5 * len;
                }
            double trA = 0, trB = 0, totalMass = 0;
            for (int k = 0; k < vals.Count; ++k)
                if (rows[k] == cols[k]) trA += vals[k];
            for (int i = 0; i < n; ++i) { trB += 2 * mass[i]; totalMass += mass[i]; }
            if (!(trA > 0) || !(totalMass > 0) || !MathUtil.IsFinite(trA)) {
                Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "spectral conformal: degenerate boundary or weights");
                return Result;
            }
            // the unit of the eigenvalues: trace(A) / trace(B), with trace(A) taken as 2 n times the median diagonal entry,
            // so that the mollified weights of degenerate triangles (about 1e8 each) do not inflate it (and the shift)
            var diag = new double[N];
            for (int k = 0; k < vals.Count; ++k)
                if (rows[k] == cols[k]) diag[rows[k]] += vals[k];
            Array.Sort(diag);
            double medianDiag = diag[N / 2];
            if (!(medianDiag > 0))
                medianDiag = trA / N;
            double scale = N * medianDiag / trB, sigma = ShiftFraction * scale;
            var chol = new SparseCholesky(N, 2);
            for (int k = 0; k < vals.Count; ++k)
                chol.Add(rows[k], cols[k], vals[k]);
            for (int i = 0; i < n; ++i) {
                chol.Add(2 * i, 2 * i, sigma * mass[i]);
                chol.Add(2 * i + 1, 2 * i + 1, sigma * mass[i]);
            }
            if (!chol.Factorize())
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "spectral conformal system: " + chol.Error);

            Func<double[], double[]> mulA = x => {
                var y = new double[N];
                for (int k = 0; k < vals.Count; ++k) {
                    int r = rows[k], c = cols[k];
                    y[r] += vals[k] * x[c];
                    if (r != c) y[c] += vals[k] * x[r];
                }
                return y;
            };
            Func<double[], double[]> mulB = x => {
                var y = new double[N];
                for (int i = 0; i < n; ++i) { y[2 * i] = mass[i] * x[2 * i]; y[2 * i + 1] = mass[i] * x[2 * i + 1]; }
                return y;
            };
            // B-orthogonal projection off the translations and off the vectors in extra, then scaled to a largest
            // coordinate of 1 (not B-normalized: a square root would round differently at 2^k scales, a division by
            // the largest coordinate does not); false if nothing is left
            Func<double[], List<double[]>, bool> deflate = (x, extra) => {
                for (int s = 0; s < 2; ++s) {
                    double mean = 0;
                    for (int i = 0; i < n; ++i) mean += mass[i] * x[2 * i + s];
                    mean /= totalMass;
                    for (int i = 0; i < n; ++i) x[2 * i + s] -= mean;
                }
                foreach (double[] e in extra) {
                    double[] be = mulB(e);
                    double c = Dot(x, be) / Dot(e, be);
                    for (int i = 0; i < N; ++i) x[i] -= c * e[i];
                }
                double max = 0;
                for (int i = 0; i < N; ++i) max = Math.Max(max, Math.Abs(x[i]));
                if (!(max > 0) || !MathUtil.IsFinite(max)) return false;
                for (int i = 0; i < N; ++i) x[i] /= max;
                return true;
            };
            Func<double[], double> rayleigh = x => Dot(x, mulA(x)) / Dot(x, mulB(x));

            double[] start = startVector(pm);
            double[] xk = (double[])start.Clone();
            bool converged;
            int its;
            if (!iterate(chol, mass, mulB, deflate, xk, new List<double[]>(), Tolerance, MaxIterations, out its, out converged, null, 0))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "spectral conformal: the iteration lost its start vector");
            Iterations = its;
            double lambda = rayleigh(xk);
            Eigenvalue = lambda / scale;
            if (!converged) {
                NearlyDoubleEigenvalue = true;
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed,
                    "spectral conformal: the iteration did not converge in " + MaxIterations + " steps (the smallest eigenvalue is nearly double); use LSCM");
            }
            // the next pair: deflate x and its rotation by 90 degrees, J x = (-v, u), and iterate from the start plus a
            // deterministic perturbation (all modes in it) until its Rayleigh quotient settles. The quotient is an upper
            // bound of the next eigenvalue, so a small one proves a small gap; a large one is an estimate, accepted only
            // when the iteration settled (a slow one means a small gap: refused). Also on a developable chart (smallest eigenvalue at rounding
            // level): there a next pair at rounding level too (eg a chart with few interior vertices, whose conformal maps
            // are many) leaves the map a mixture
            var jx = new double[N];
            for (int i = 0; i < n; ++i) { jx[2 * i] = -xk[2 * i + 1]; jx[2 * i + 1] = xk[2 * i]; }
            var extra = new List<double[]> { xk };
            var x2 = (double[])start.Clone();
            bool estimated = deflate(jx, extra);
            if (estimated) {
                extra.Add(jx);
                estimated = deflate(x2, new List<double[]>());
            }
            if (estimated) {
                for (int i = 0; i < N; ++i) x2[i] += 0.1 * Math.Sin(12.9898 * i);
                bool nextConverged;
                estimated = iterate(chol, mass, mulB, deflate, x2, extra, 0, NextMaxIterationsLimit, out its, out nextConverged, rayleigh, NextTolerance)
                    && nextConverged;
                NextIterations = its;
            }
            double lambda2 = estimated ? rayleigh(x2) : double.NaN;
            NextEigenvalue = lambda2 / scale;
            if (NearlyDouble(Eigenvalue, NextEigenvalue, MinEigenvalueGap)) {
                NearlyDoubleEigenvalue = true;
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed,
                    "spectral conformal: the smallest eigenvalue is nearly double (next " + NextEigenvalue.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)
                    + ", smallest " + Eigenvalue.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)
                    + ", relative; the next must be 1 + MinEigenvalueGap times larger and above " + ZeroEigenvalue + "); use LSCM");
            }
            double[] u = new double[n], v = new double[n];
            for (int i = 0; i < n; ++i) { u[i] = xk[2 * i]; v[i] = xk[2 * i + 1]; }
            if (ScaleToSurfaceArea)
                pm.ScaleToSurfaceArea(u, v);
            if (!pm.AllFinite(u, v))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "spectral conformal solution is not finite");
            Result = pm.MakeResult(u, v);
            Result.Iterations = Iterations;
            Result.Converged = true;
            Result.Energy = conformalEnergy(pm, u, v);
            return Result = ParameterizationResult.RejectFlipped(Result, "SpectralConformal");
        }

        /// <summary>
        /// the smallest eigenvalue (relative) counts as nearly double if the estimate of the next one is not at least
        /// (1 + gap) times larger, or is itself at rounding level (at most ZeroEigenvalue; also NaN)
        /// </summary>
        internal static bool NearlyDouble(double eigenvalue, double next, double gap)
        {
            return !(next >= (1 + gap) * eigenvalue) || !(next > ZeroEigenvalue);
        }

        // inverse power iteration on (A + sigma B)^-1 B, deflated: stops when the B-norm of the change is at most tol
        // relative to the B-norm of the iterate, or (with rq) when the Rayleigh quotient changes by at most rqTol
        // relative; false if the iterate vanished
        static bool iterate(SparseCholesky chol, double[] mass, Func<double[], double[]> mulB, Func<double[], List<double[]>, bool> deflate,
            double[] x, List<double[]> extra, double tol, int maxIterations, out int its, out bool converged,
            Func<double[], double> rq, double rqTol)
        {
            converged = false;
            its = 0;
            if (!deflate(x, extra)) return false;
            int N = x.Length;
            var y = new double[N];
            double q = rq != null ? rq(x) : 0, prev = double.PositiveInfinity;
            while (its < maxIterations) {
                double[] bx = mulB(x);
                chol.Solve(bx, y);
                if (!deflate(y, extra)) return false;
                ++its;
                double ch = 0, nrm = 0;
                for (int i = 0; i < N; ++i) {
                    double d = y[i] - x[i];   // y^T B x = (Bx)^T (A + sigma B)^-1 (Bx) > 0: no sign flip
                    ch += mass[i >> 1] * d * d; nrm += mass[i >> 1] * y[i] * y[i];
                    x[i] = y[i];
                }
                if (rq != null) {
                    double q1 = rq(x);
                    bool settled = Math.Abs(q1 - q) <= rqTol * Math.Abs(q1);
                    q = q1;
                    if (settled) { converged = true; break; }
                } else {
                    // converged at tol, or stalled at the rounding floor of the solve (about 1e-10 relative on nearly
                    // developable discs): small and no longer decreasing (by 10 % a step). A slow but steady decrease
                    // (a small gap) goes on to MaxIterations and is refused
                    double rel = Math.Sqrt(ch / nrm);
                    if (rel <= tol || rel <= StallTolerance && rel >= 0.9 * prev) {
                        converged = true;
                        break;
                    }
                    prev = rel;
                }
            }
            return true;
        }

        static double Dot(double[] a, double[] b)
        {
            double s = 0;
            for (int i = 0; i < a.Length; ++i) s += a[i] * b[i];
            return s;
        }

        // the vertices projected on the plane normal to the vector area of the boundary loop (1/2 Sum p_i x p_i+1, the
        // area vector of any surface it bounds): deterministic and exact under 2^k scales; under a rotation of the input the
        // map is the same up to a rotation in the plane (the in-plane axes depend on the fixed AxisX / AxisY)
        static double[] startVector(ParamMesh pm)
        {
            Vector3d an = Vector3d.Zero;
            int[] loop = pm.BoundaryLoops[0];
            for (int k = 0; k < loop.Length; ++k)
                an += pm.Pos[loop[k]].Cross(pm.Pos[loop[(k + 1) % loop.Length]]);
            double len = an.Length;
            Vector3d nrm = len > 0 && MathUtil.IsFinite(len) ? an / len : Vector3d.AxisZ;
            Vector3d e1 = Math.Abs(nrm.x) < 0.9 ? nrm.Cross(Vector3d.AxisX) : nrm.Cross(Vector3d.AxisY);
            e1 = e1 / e1.Length;
            Vector3d e2 = nrm.Cross(e1);
            var x = new double[2 * pm.N];
            for (int i = 0; i < pm.N; ++i) { x[2 * i] = pm.Pos[i].Dot(e1); x[2 * i + 1] = pm.Pos[i].Dot(e2); }
            return x;
        }

        static double conformalEnergy(ParamMesh pm, double[] u, double[] v)
        {
            double ed = 0;
            pm.ForEachCotanEdge((i, j, w) => {
                double du = u[i] - u[j], dv = v[i] - v[j];
                ed += 0.5 * w * (du * du + dv * dv);
            }, true);
            return (ed - pm.UVArea(u, v)) / pm.SurfaceArea;
        }
    }
}
