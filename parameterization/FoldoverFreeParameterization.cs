// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Fixed-boundary untangling from Garanzha et al. (2021), sections 2.2-2.3 and 4.1.
    /// A serial modified-Newton solver; convergence within the iteration budget is not guaranteed.
    /// Success certifies finite, nondegenerate, positively oriented triangles, not global injectivity.
    /// Input mesh and UVs are not modified. All boundary vertices retain their original double values.
    /// Energy is the normalized shape/area objective at InitialEpsilon; a retained earlier best map is not
    /// marked Converged. Unrepresentable output distortion is a solver failure even if normalization succeeded.
    /// </summary>
    public class FoldoverFreeParameterization
    {
        public DMesh3 Mesh;
        /// <summary> Initial UVs indexed by vertex id; read anew on each Compute. </summary>
        public IList<Vector2d> InitialUV;
        public int MaxIterations = 200;
        /// <summary> Positive regularization parameter before the equation-6 schedule. </summary>
        public double InitialEpsilon = 1;
        /// <summary> Positive lower bound on regularization; remaining inversions are reported as failure. </summary>
        public double MinimumEpsilon = 1e-12;
        public ParameterizationResult Result;

        public FoldoverFreeParameterization(DMesh3 mesh, IList<Vector2d> startUV)
        {
            Mesh = mesh; InitialUV = startUV;
        }

        ParameterizationResult Fail(ParameterizationStatus status, string reason)
        {
            return Result = ParameterizationResult.Failure(status, "FoldoverFree: " + reason);
        }

        public ParameterizationResult Compute()
        {
            Result = null;
            if (Mesh == null || Mesh.TriangleCount == 0)
                return Fail(ParameterizationStatus.InvalidMesh, "no triangles");
            if (InitialUV == null || MaxIterations < 0 || !Positive(InitialEpsilon) ||
                !Positive(MinimumEpsilon) || MinimumEpsilon > InitialEpsilon)
                return Fail(ParameterizationStatus.InvalidConstraints, "invalid initial UVs or iteration/epsilon options");
            ParamMesh pm;
            string error;
            if (!PrepareMesh(Mesh, out pm, out error))
                return Fail(ParameterizationStatus.InvalidMesh, error);
            if (!pm.RequireOpenComponent()) return Fail(pm.Status, pm.Error);
            int n = pm.N;
            var original = new Vector2d[Mesh.MaxVertexID];
            var x = new double[2 * n];
            Vector2d origin = Vector2d.Zero;
            double extent = 0;
            for (int i = 0; i < n; ++i) {
                int vid = pm.ToVertex[i];
                if (vid >= InitialUV.Count || !Finite(InitialUV[vid]))
                    return Fail(ParameterizationStatus.InvalidConstraints, "missing or non-finite UV at vertex " + vid);
                original[vid] = InitialUV[vid];
                if (i == 0) origin = original[vid];
                Vector2d p = original[vid] - origin;
                if (!Finite(p)) return Fail(ParameterizationStatus.InvalidConstraints, "UV range is not representable");
                x[2 * i] = p.x; x[2 * i + 1] = p.y;
                extent = Math.Max(extent, Math.Max(Math.Abs(p.x), Math.Abs(p.y)));
            }
            double scale = PowerScale(extent);
            var locked = new bool[2 * n];
            for (int i = 0; i < n; ++i) {
                x[2 * i] /= scale; x[2 * i + 1] /= scale;
                locked[2 * i] = locked[2 * i + 1] = pm.IsBoundary[i];
            }
            foreach (int[] loop in pm.BoundaryLoops)
                for (int i = 0; i < loop.Length; ++i)
                    if (original[pm.ToVertex[loop[i]]] == original[pm.ToVertex[loop[(i + 1) % loop.Length]]])
                        return Fail(ParameterizationStatus.InvalidConstraints, "a fixed boundary edge is collapsed");
            foreach (Index3i t in pm.Tris)
                if (pm.IsBoundary[t.a] && pm.IsBoundary[t.b] && pm.IsBoundary[t.c] && BadTriangle(x, t))
                    return Fail(ParameterizationStatus.InvalidConstraints, "a fully fixed triangle is inverted or degenerate");

            var q = new ConstrainedQuadratic(2 * n, locked, new double[2 * n], 2);
            var gradient = new double[2 * n];
            var step = new double[2 * n];
            var trial = new double[2 * n];
            var best = (double[])x.Clone();
            int bestBad = BadCount(pm, x), iterations = 0;
            bool bestIsStart = true;
            double bestEnergy = Evaluate(pm, x, InitialEpsilon, null, null, out _);
            if (!MathUtil.IsFinite(bestEnergy)) return Fail(ParameterizationStatus.SolverFailed, "initial energy is not representable");
            double epsilon = InitialEpsilon;
            bool converged = q.NumFree == 0;
            string stop = "iteration limit";
            while (iterations < MaxIterations && !converged) {
                double energy = Evaluate(pm, x, epsilon, gradient, q, out _);
                if (!MathUtil.IsFinite(energy) || !q.Factorize())
                    return Fail(ParameterizationStatus.SolverFailed, "non-finite derivatives or factorization failure: " + q.Error);
                for (int i = 0; i < gradient.Length; ++i) step[i] = -gradient[i];
                q.Solve(step, step);
                double slope = 0;
                for (int i = 0; i < step.Length; ++i) slope += gradient[i] * step[i];
                if (!MathUtil.IsFinite(slope) || slope > 0) return Fail(ParameterizationStatus.SolverFailed, "non-finite or non-descent search direction");
                int bad = BadCount(pm, x);
                if (bad == 0 && -slope <= 1e-10 * Math.Max(1, energy) && epsilon <= 1e-6) {
                    converged = true; stop = null; break;
                }
                double nextEnergy = energy, minDet = 0, alpha = 1;
                bool accepted = false;
                if (slope < 0) {
                    for (int backtrack = 0; backtrack < 40; ++backtrack, alpha *= 0.5) {
                        for (int i = 0; i < x.Length; ++i) trial[i] = locked[i] ? x[i] : x[i] + alpha * step[i];
                        nextEnergy = Evaluate(pm, trial, epsilon, null, null, out minDet);
                        if (MathUtil.IsFinite(nextEnergy) && nextEnergy < energy &&
                            nextEnergy <= energy + 1e-4 * alpha * slope) { accepted = true; break; }
                    }
                }
                iterations++;
                if (accepted) {
                    var swap = x; x = trial; trial = swap;
                    int count = BadCount(pm, x);
                    double commonEnergy = Evaluate(pm, x, InitialEpsilon, null, null, out _);
                    if (MathUtil.IsFinite(commonEnergy) && (count < bestBad || (count == bestBad && commonEnergy < bestEnergy))) {
                        Array.Copy(x, best, x.Length); bestBad = count; bestEnergy = commonEnergy; bestIsStart = false;
                    }
                } else {
                    nextEnergy = energy;
                    Evaluate(pm, x, epsilon, null, null, out minDet);
                }
                if (epsilon == MinimumEpsilon && !accepted) { stop = "line search stalled at minimum epsilon"; break; }
                epsilon = Math.Max(MinimumEpsilon, NextEpsilon(epsilon, minDet, energy, nextEnergy));
            }
            for (int i = 0; i < x.Length; ++i) converged &= best[i] == x[i];
            var uv = new Vector2d[Mesh.MaxVertexID];
            for (int i = 0; i < n; ++i) {
                int vid = pm.ToVertex[i];
                uv[vid] = pm.IsBoundary[i] || bestIsStart ? original[vid] : origin + scale * new Vector2d(best[2 * i], best[2 * i + 1]);
                if (!Finite(uv[vid])) return Fail(ParameterizationStatus.SolverFailed, "restored UVs are not finite");
            }
            var distortion = ParameterizationDistortion.Compute(Mesh, uv);
            if (distortion.Triangles != Mesh.TriangleCount)
                return Fail(ParameterizationStatus.SolverFailed, "distortion cannot cover every original triangle");
            Result = new ParameterizationResult { UV = uv, Distortion = distortion, Iterations = iterations,
                Converged = converged, Energy = bestEnergy };
            ParameterizationResult.RejectFlipped(Result, "FoldoverFree");
            // Check restored coordinates independently: inverse normalization can collapse an edge.
            int restoredBad = 0;
            foreach (Index3i t in pm.Tris) {
                Vector2d a = uv[pm.ToVertex[t.a]], b = uv[pm.ToVertex[t.b]], c = uv[pm.ToVertex[t.c]];
                if (BadShape(b - a, c - a)) restoredBad++;
            }
            if (restoredBad > 0) {
                Result.Status = ParameterizationStatus.FlippedTriangles;
                Result.Converged = false;
                Result.Error = "FoldoverFree: " + stop + "; " + restoredBad + " invalid restored UV triangles";
            } else if (!MathUtil.IsFinite(distortion.SymmetricDirichlet)) {
                return Fail(ParameterizationStatus.SolverFailed, "distortion is not representable");
            }
            return Result;
        }

        static bool Positive(double x) { return x > 0 && MathUtil.IsFinite(x); }
        static bool Finite(Vector2d p) { return MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y); }
        static bool Finite(Vector3d p) { return MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z); }

        static double PowerScale(double extent)
        {
            if (extent == 0) return 1;
            return Math.Pow(2, Math.Max(-1074, Math.Min(1023, Math.Floor(Math.Log(extent, 2)))));
        }

        static bool PrepareMesh(DMesh3 mesh, out ParamMesh pm, out string error)
        {
            pm = null; error = null;
            var used = new bool[mesh.MaxVertexID];
            Vector3d origin = Vector3d.Zero;
            bool first = true;
            double extent = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                for (int j = 0; j < 3; ++j) {
                    if (!mesh.IsVertex(t[j])) { error = "missing surface vertex"; return false; }
                    Vector3d p = mesh.GetVertex(t[j]);
                    if (!Finite(p)) { error = "non-finite surface vertex"; return false; }
                    if (first) { origin = p; first = false; }
                    p -= origin;
                    if (!Finite(p)) { error = "surface range is not representable"; return false; }
                    extent = Math.Max(extent, Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
                    used[t[j]] = true;
                }
            }
            var copy = new DMesh3(mesh);
            double scale = PowerScale(extent);
            foreach (int vid in copy.VertexIndices())
                copy.SetVertex(vid, used[vid] ? (mesh.GetVertex(vid) - origin) / scale : Vector3d.Zero);
            pm = new ParamMesh(copy);
            if (!pm.Valid) { error = pm.Error; return false; }
            foreach (TriangleFrame frame in pm.Frames)
                if (frame.Degenerate || !Positive(frame.Area)) { error = "degenerate surface triangle"; return false; }
            // Unit surface area removes the residual non-power-of-two scale after range preconditioning.
            double areaScale = Math.Sqrt(pm.SurfaceArea);
            if (!Positive(areaScale)) { error = "surface area is not representable"; return false; }
            foreach (int vid in copy.VertexIndices()) copy.SetVertex(vid, copy.GetVertex(vid) / areaScale);
            pm = new ParamMesh(copy);
            return pm.Valid;
        }

        static bool BadShape(Vector2d b, Vector2d c)
        {
            double area = b.Cross(c), longest = Math.Max(b.LengthSquared, Math.Max(c.LengthSquared, (c - b).LengthSquared));
            return !MathUtil.IsFinite(area) || !MathUtil.IsFinite(longest) || TriangleFrame.DegenerateShape(area, longest);
        }
        static bool BadTriangle(double[] x, Index3i t)
        {
            return BadShape(new Vector2d(x[2 * t.b] - x[2 * t.a], x[2 * t.b + 1] - x[2 * t.a + 1]),
                new Vector2d(x[2 * t.c] - x[2 * t.a], x[2 * t.c + 1] - x[2 * t.a + 1]));
        }
        static int BadCount(ParamMesh pm, double[] x)
        {
            int count = 0;
            foreach (Index3i t in pm.Tris) if (BadTriangle(x, t)) count++;
            return count;
        }

        internal static double Hypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            double hi = Math.Max(a, b), lo = Math.Min(a, b);
            return hi == 0 ? 0 : hi * Math.Sqrt(1 + (lo / hi) * (lo / hi));
        }

        internal static double NextEpsilon(double epsilon, double minDet, double oldEnergy, double newEnergy)
        {
            double sigma = Math.Max(0.1, 1 - newEnergy / oldEnergy);
            double h = Hypot(minDet, epsilon);
            return epsilon * (1 - sigma * (minDet < 0 ? 1 / (1 + Math.Abs(minDet) / h) : 1));
        }

        // Appendix A / section 4.1 in row-major Jacobian coordinates. H is the lifted convex
        // Hessian, not the true Hessian: terms containing chi'' and det'' are deliberately omitted.
        internal static double Element(double[] a, double epsilon, double[] gradient, double[,] hessian)
        {
            double det = a[0] * a[3] - a[1] * a[2], h = Hypot(det, epsilon);
            double chi = det >= 0 ? 0.5 * det + 0.5 * h : (0.5 * epsilon) * (epsilon / h) / (1 + Math.Abs(det) / h);
            if (!Positive(chi) || !Positive(h)) return double.PositiveInfinity;
            double norm = 0;
            for (int i = 0; i < 4; ++i) norm += a[i] * a[i];
            double numerator = norm + det * det + 1;
            double energy = numerator / chi;
            if (!MathUtil.IsFinite(energy) || gradient == null) return energy;
            double[] cofactor = { a[3], -a[2], -a[1], a[0] };
            double r = 1 / h, c = (2 * det - numerator * r) / chi;
            for (int i = 0; i < 4; ++i) gradient[i] = 2 * a[i] / chi + c * cofactor[i];
            double volume = (1 - det / h) * (1 - det / h) + r * r;
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j) {
                    double value = volume * cofactor[i] * cofactor[j];
                    for (int k = 0; k < 4; ++k)
                        value += ((i == k ? 1 : 0) - r * cofactor[i] * a[k]) *
                            ((j == k ? 1 : 0) - r * cofactor[j] * a[k]);
                    hessian[i, j] = 2 * value / chi;
                }
            return energy;
        }

        static double Evaluate(ParamMesh pm, double[] x, double epsilon, double[] gradient,
            ConstrainedQuadratic q, out double minDet)
        {
            minDet = double.PositiveInfinity;
            if (q != null) { q.Clear(); Array.Clear(gradient, 0, gradient.Length); }
            var a = new double[4]; var g = new double[4]; var h = new double[4, 4];
            var basis = new double[4, 6]; var ids = new int[6];
            double energy = 0;
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                Index3i t = pm.Tris[ti]; TriangleFrame f = pm.Frames[ti];
                Vector2d p0 = new Vector2d(x[2 * t.a], x[2 * t.a + 1]);
                Vector2d p1 = new Vector2d(x[2 * t.b], x[2 * t.b + 1]) - p0;
                Vector2d p2 = new Vector2d(x[2 * t.c], x[2 * t.c + 1]) - p0;
                a[0] = f.Dx.y * p1.x + f.Dx.z * p2.x; a[1] = f.Dy.y * p1.x + f.Dy.z * p2.x;
                a[2] = f.Dx.y * p1.y + f.Dx.z * p2.y; a[3] = f.Dy.y * p1.y + f.Dy.z * p2.y;
                minDet = Math.Min(minDet, a[0] * a[3] - a[1] * a[2]);
                double weight = f.Area / pm.SurfaceArea;
                energy += weight * Element(a, epsilon, q == null ? null : g, h);
                if (!MathUtil.IsFinite(energy)) return double.PositiveInfinity;
                if (q == null) continue;
                Array.Clear(basis, 0, basis.Length);
                for (int i = 0; i < 3; ++i) {
                    ids[2 * i] = 2 * t[i]; ids[2 * i + 1] = 2 * t[i] + 1;
                    basis[0, 2 * i] = basis[2, 2 * i + 1] = f.Dx[i];
                    basis[1, 2 * i] = basis[3, 2 * i + 1] = f.Dy[i];
                }
                for (int i = 0; i < 6; ++i) {
                    double gi = 0;
                    for (int k = 0; k < 4; ++k) gi += basis[k, i] * g[k];
                    gradient[ids[i]] += weight * gi;
                    if (!MathUtil.IsFinite(gradient[ids[i]])) return double.PositiveInfinity;
                    for (int j = i; j < 6; ++j) {
                        double hij = 0;
                        for (int k = 0; k < 4; ++k)
                            for (int l = 0; l < 4; ++l) hij += basis[k, i] * h[k, l] * basis[l, j];
                        if (!MathUtil.IsFinite(hij)) return double.PositiveInfinity;
                        q.Add(ids[i], ids[j], weight * hij);
                    }
                }
            }
            return energy;
        }
    }
}
