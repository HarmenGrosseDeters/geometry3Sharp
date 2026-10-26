// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Boundary First Flattening (Sawhney and Crane 2017): a conformal flattening of a disc that is fully
    /// controlled by the boundary. Either the scale factors u (log of the length change) or the exterior
    /// angles (geodesic curvature) along the boundary are prescribed; the other one follows from a
    /// Dirichlet-to-Neumann (or Neumann-to-Dirichlet) map of the cotan Laplacian (discrete Yamabe equation,
    /// flat interior). The boundary curve is then integrated from its target lengths and angles, closed with a
    /// minimal change of the lengths, and extended into the interior as a holomorphic function (harmonic
    /// x coordinate and its harmonic conjugate) or harmonically (exact boundary).
    ///
    /// Boundary modes: MinimalAreaDistortion (u = 0 on the boundary: lengths preserved, the conformal map
    /// with least area distortion), Disk (the boundary becomes a circle), TargetScaleFactors and TargetAngles
    /// (eg exterior angles of pi/2 at four corners give a rectangle). Only linear solves with two factorized
    /// Laplacians, so it is fast. Flips are possible near very sharp target corners.
    /// </summary>
    public class BoundaryFirstFlattening
    {
        public enum BoundaryModes { MinimalAreaDistortion = 0, Disk = 1, TargetScaleFactors = 2, TargetAngles = 3 }
        public enum ExtensionModes
        {
            /// <summary> harmonic x and its harmonic conjugate y: conformal, boundary close to the target curve </summary>
            Conformal = 0,
            /// <summary> both coordinates harmonic: boundary exactly the target curve, slightly less conformal </summary>
            Harmonic = 1
        }

        public DMesh3 Mesh;
        public BoundaryModes BoundaryMode = BoundaryModes.MinimalAreaDistortion;
        public ExtensionModes Extension = ExtensionModes.Conformal;
        /// <summary>
        /// TargetScaleFactors: log scale factor by boundary vertex id (missing vertices: 0; interior vertices are
        /// ignored, so a per-vertex field can be passed)
        /// </summary>
        public Dictionary<int, double> TargetScaleFactors;
        /// <summary>
        /// TargetAngles: exterior (turning) angle in radians by boundary vertex id (missing vertices: 0);
        /// the angles must sum to 2 pi
        /// </summary>
        public Dictionary<int, double> TargetAngles;
        /// <summary> scale the result so that the UV area equals the surface area </summary>
        public bool ScaleToSurfaceArea = false;

        public ParameterizationResult Result;

        /// <summary> target scale factors u per boundary vertex id after Compute() </summary>
        public Dictionary<int, double> BoundaryScaleFactors;
        /// <summary> target exterior angles per boundary vertex id after Compute() </summary>
        public Dictionary<int, double> BoundaryAngles;

        public BoundaryFirstFlattening(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public ParameterizationResult Compute()
        {
            ParamMesh pm = new ParamMesh(Mesh);
            if (!pm.RequireDisc())
                return Result = pm.Failure();
            int N = pm.N;
            int[] loop = pm.BoundaryLoops[0];
            int m = loop.Length;

            // angle defects: K (interior), k (boundary)
            // degenerate triangles count too (angles 0, 0, pi): Gauss-Bonnet, Sum K + Sum k = 2 pi, only holds
            // with all triangles, and the Neumann problem and the closing of the boundary curve rely on it
            double[] angleSum = new double[N];
            double[] corner = new double[3];
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                TriangleFrame f = pm.Frames[ti];
                Index3i t = pm.Tris[ti];
                if (f.Degenerate)
                    degenerateAngles(pm.Pos[t.a], pm.Pos[t.b], pm.Pos[t.c], corner);
                else
                    for (int j = 0; j < 3; ++j) corner[j] = Math.Atan2(1, f.Cot[j]);
                for (int j = 0; j < 3; ++j)
                    angleSum[t[j]] += corner[j];
            }
            double[] K = new double[N];   // interior curvature, boundary entries 0
            double[] kb = new double[m];  // boundary geodesic curvature
            for (int i = 0; i < N; ++i)
                if (!pm.IsBoundary[i]) K[i] = 2 * Math.PI - angleSum[i];
            for (int k = 0; k < m; ++k)
                kb[k] = Math.PI - angleSum[loop[k]];
            double[] len = new double[m];   // edge k: loop[k] -> loop[k+1]
            for (int k = 0; k < m; ++k)
                len[k] = pm.Pos[loop[k]].Distance(pm.Pos[loop[(k + 1) % m]]);

            // Dirichlet (boundary fixed) and Neumann (one pinned vertex) Laplacians, with the degenerate triangles
            // (mollified), which keep T-junction vertices on their edge
            bool[] dFixed = new bool[N];
            double[] dValue = new double[N];
            foreach (int i in loop) dFixed[i] = true;
            ConstrainedQuadratic dir = new ConstrainedQuadratic(N, dFixed, dValue);
            bool[] nFixed = new bool[N];
            nFixed[loop[0]] = true;
            ConstrainedQuadratic neu = new ConstrainedQuadratic(N, nFixed, new double[N]);
            pm.ForEachCotanEdge((i, j, w) => {
                dir.Add(i, i, w); dir.Add(j, j, w); dir.Add(i, j, -w);
                neu.Add(i, i, w); neu.Add(j, j, w); neu.Add(i, j, -w);
            }, true);
            if (!dir.Factorize())
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "BFF Dirichlet system: " + dir.Error);
            if (!neu.Factorize())
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "BFF Neumann system: " + neu.Error);

            double[] negK = new double[N];
            for (int i = 0; i < N; ++i) negK[i] = -K[i];
            double[] ub = new double[m];     // boundary scale factors
            double[] kt = new double[m];     // target exterior angles
            double[] x = new double[N];

            switch (BoundaryMode) {
                case BoundaryModes.MinimalAreaDistortion:
                case BoundaryModes.TargetScaleFactors:
                    if (BoundaryMode == BoundaryModes.TargetScaleFactors) {
                        if (TargetScaleFactors == null)
                            return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "TargetScaleFactors is null");
                        foreach (var kv in TargetScaleFactors) {
                            int vid = kv.Key;
                            // interior vertices may be given (a per-vertex field) and are ignored
                            if (vid < 0 || vid >= pm.ToIndex.Length || pm.ToIndex[vid] < 0)
                                return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "vertex " + vid + " is not a vertex of a triangle");
                            if (pm.IsBoundary[pm.ToIndex[vid]] && !MathUtil.IsFinite(kv.Value))
                                return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "target scale factor is not finite");
                        }
                    }
                    for (int k = 0; k < m; ++k) {
                        double s = 0;
                        if (BoundaryMode == BoundaryModes.TargetScaleFactors)
                            TargetScaleFactors.TryGetValue(pm.ToVertex[loop[k]], out s);
                        ub[k] = s;
                    }
                    dirichletToNeumann(pm, dir, dValue, negK, loop, kb, ub, kt, x);
                    break;

                case BoundaryModes.TargetAngles: {
                    if (TargetAngles == null)
                        return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "TargetAngles is null");
                    double sum = 0;
                    foreach (var kv in TargetAngles) {
                        int vid = kv.Key;
                        if (vid < 0 || vid >= pm.ToIndex.Length || pm.ToIndex[vid] < 0 || !pm.IsBoundary[pm.ToIndex[vid]])
                            return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "vertex " + vid + " is not a boundary vertex");
                        if (!MathUtil.IsFinite(kv.Value))
                            return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints, "target angle is not finite");
                    }
                    for (int k = 0; k < m; ++k) {
                        double a;
                        TargetAngles.TryGetValue(pm.ToVertex[loop[k]], out a);
                        kt[k] = a; sum += a;
                    }
                    if (Math.Abs(sum - 2 * Math.PI) > 1e-4)
                        return Result = ParameterizationResult.Failure(ParameterizationStatus.InvalidConstraints,
                            "target exterior angles sum to " + sum + ", not 2 pi");
                    for (int k = 0; k < m; ++k) kt[k] += (2 * Math.PI - sum) / m;
                    neumannToDirichlet(pm, neu, negK, loop, kb, kt, len, ub, x);
                    break;
                }

                case BoundaryModes.Disk: {
                    // the exterior angles of the polygon with the target lengths inscribed in a circle. The lengths
                    // depend on u, which depends on the angles; a few rounds bring them close (iterating further
                    // drifts: conformal maps onto the disk are only unique up to a Moebius transform). The final
                    // angles are those of the final lengths, so the boundary curve is exactly that polygon.
                    for (int iter = 0; iter < 4; ++iter) {
                        inscribedAngles(targetLengths(len, ub), kt);
                        neumannToDirichlet(pm, neu, negK, loop, kb, kt, len, ub, x);
                    }
                    inscribedAngles(targetLengths(len, ub), kt);
                    break;
                }
            }

            // boundary curve from target lengths and angles, closed by the least change of the lengths
            double[] ltgt = targetLengths(len, ub);
            Vector2d[] gamma = new Vector2d[m];
            {
                Vector2d[] T = new Vector2d[m];
                double phi = 0;
                for (int k = 0; k < m; ++k) {
                    if (k > 0) phi += kt[k];
                    T[k] = new Vector2d(Math.Cos(phi), Math.Sin(phi));
                }
                double g00 = 0, g01 = 0, g11 = 0, r0 = 0, r1 = 0;
                for (int k = 0; k < m; ++k) {
                    g00 += ltgt[k] * T[k].x * T[k].x; g01 += ltgt[k] * T[k].x * T[k].y; g11 += ltgt[k] * T[k].y * T[k].y;
                    r0 += ltgt[k] * T[k].x; r1 += ltgt[k] * T[k].y;
                }
                double det = g00 * g11 - g01 * g01;
                double l0 = 0, l1 = 0;
                if (Math.Abs(det) > 1e-14 * (g00 * g11 + g01 * g01)) {
                    l0 = (g11 * r0 - g01 * r1) / det;
                    l1 = (g00 * r1 - g01 * r0) / det;
                }
                Vector2d p = Vector2d.Zero;
                for (int k = 0; k < m; ++k) {
                    gamma[k] = p;
                    double lk = ltgt[k] - ltgt[k] * (T[k].x * l0 + T[k].y * l1);
                    p += lk * T[k];
                }
            }

            // extension into the interior
            double[] u = new double[N], v = new double[N];
            for (int k = 0; k < m; ++k) dValue[loop[k]] = gamma[k].x;
            dir.Solve(null, u);
            if (Extension == ExtensionModes.Harmonic) {
                for (int k = 0; k < m; ++k) dValue[loop[k]] = gamma[k].y;
                dir.Solve(null, v);
            } else {
                // harmonic conjugate: Neumann data dv/dn = -du/dt, integrated against the hat functions
                double[] h = new double[N];
                for (int k = 0; k < m; ++k)
                    h[loop[k]] = -0.5 * (u[loop[(k + 1) % m]] - u[loop[(k + m - 1) % m]]);
                neu.Solve(h, v);
                double shift = 0;
                for (int k = 0; k < m; ++k) shift += gamma[k].y - v[loop[k]];
                shift /= m;
                for (int i = 0; i < N; ++i) v[i] += shift;
            }
            if (!pm.AllFinite(u, v))
                return Result = ParameterizationResult.Failure(ParameterizationStatus.SolverFailed, "BFF solution is not finite");
            if (ScaleToSurfaceArea)
                pm.ScaleToSurfaceArea(u, v);

            BoundaryScaleFactors = new Dictionary<int, double>();
            BoundaryAngles = new Dictionary<int, double>();
            for (int k = 0; k < m; ++k) {
                BoundaryScaleFactors[pm.ToVertex[loop[k]]] = ub[k];
                BoundaryAngles[pm.ToVertex[loop[k]]] = kt[k];
            }
            return Result = pm.MakeResult(u, v);
        }

        // corner angles of a (nearly) degenerate triangle, summing to pi: the angles between its edges, and what
        // is missing to pi goes to the corners opposite the longest edge (the corner between the other two
        // points of a flat triangle; both ends of the zero-length edge of a triangle with coincident vertices)
        internal static void degenerateAngles(Vector3d p0, Vector3d p1, Vector3d p2, double[] angle)
        {
            Vector3d[] p = { p0, p1, p2 };
            double sum = 0, maxLen = 0;
            double[] opp = new double[3];
            for (int i = 0; i < 3; ++i) {
                Vector3d a = p[(i + 1) % 3] - p[i], b = p[(i + 2) % 3] - p[i];
                angle[i] = (a.LengthSquared > 0 && b.LengthSquared > 0) ? ParamMath.Angle(a, b) : 0;
                sum += angle[i];
                opp[i] = p[(i + 1) % 3].Distance(p[(i + 2) % 3]);
                maxLen = Math.Max(maxLen, opp[i]);
            }
            int count = 0;
            for (int i = 0; i < 3; ++i) if (opp[i] >= (1 - 1e-9) * maxLen) count++;
            for (int i = 0; i < 3; ++i)
                if (opp[i] >= (1 - 1e-9) * maxLen) angle[i] += (Math.PI - sum) / count;
        }

        // exterior angles of the polygon with edge lengths lt inscribed in a circle (inscribed angle theorem: the
        // turn at a vertex is half the arcs of its two edges). Usually every edge spans the minor arc 2 asin(l/2R)
        // and R makes them sum to 2 pi. If the longest edge is long enough that the center lies outside the
        // polygon, it spans the major arc 2 pi - 2 asin(lmax/2R) and the others sum to its minor arc. Only if the
        // longest edge is at least as long as all others together (no closed polygon) the arcs are proportional
        // to the lengths.
        internal static void inscribedAngles(double[] lt, double[] kt)
        {
            int m = lt.Length;
            double L = 0, lmax = 0;
            int imax = 0;
            for (int k = 0; k < m; ++k) {
                L += lt[k];
                if (lt[k] > lmax) { lmax = lt[k]; imax = k; }
            }
            Func<double, double, double> arc = (l, rad) => 2 * Math.Asin(Math.Min(1, l / (2 * rad)));
            Func<double, double> others = (rad) => { double a = 0; for (int k = 0; k < m; ++k) if (k != imax) a += arc(lt[k], rad); return a; };
            double[] theta = new double[m];
            bool centerInside = others(0.5 * lmax) + Math.PI >= 2 * Math.PI;
            if (!centerInside && L - lmax <= lmax * (1 + 1e-12)) {
                for (int k = 0; k < m; ++k) theta[k] = 2 * Math.PI * lt[k] / L;
            } else {
                // f(R) decreases from f(lmax/2) >= 0 (inside) or rises from f(lmax/2) < 0 (outside); bisect its sign change
                Func<double, double> f = centerInside
                    ? (Func<double, double>)((rad) => others(rad) + arc(lmax, rad) - 2 * Math.PI)
                    : (rad) => others(rad) - arc(lmax, rad);
                double lo = 0.5 * lmax, hi = lo;
                double flo = f(lo);
                do { hi *= 2; } while (Math.Sign(f(hi)) == Math.Sign(flo) && hi < 1e12 * L);
                for (int it = 0; it < 200 && hi - lo > 1e-15 * hi; ++it) {
                    double mid = 0.5 * (lo + hi);
                    if (Math.Sign(f(mid)) == Math.Sign(flo)) lo = mid; else hi = mid;
                }
                double R = 0.5 * (lo + hi), sum = 0;
                for (int k = 0; k < m; ++k) {
                    theta[k] = arc(lt[k], R);
                    if (k == imax && !centerInside) theta[k] = 2 * Math.PI - theta[k];
                    sum += theta[k];
                }
                for (int k = 0; k < m; ++k) theta[k] *= 2 * Math.PI / sum;     // remove the bisection residual
            }
            for (int k = 0; k < m; ++k)
                kt[k] = 0.5 * (theta[(k + m - 1) % m] + theta[k]);
        }

        static double[] targetLengths(double[] len, double[] ub)
        {
            int m = len.Length;
            double[] lt = new double[m];
            for (int k = 0; k < m; ++k)
                lt[k] = Math.Exp(0.5 * (ub[k] + ub[(k + 1) % m])) * len[k];
            return lt;
        }

        // given u on the boundary: flat interior (C u)_I = -K_I, then target angles k~ = k + (C u)_B
        static void dirichletToNeumann(ParamMesh pm, ConstrainedQuadratic dir, double[] dValue, double[] negK,
            int[] loop, double[] kb, double[] ub, double[] kt, double[] x)
        {
            int m = loop.Length;
            for (int k = 0; k < m; ++k) dValue[loop[k]] = ub[k];
            dir.Solve(negK, x);
            double[] Cu = new double[pm.N];
            pm.ForEachCotanEdge((i, j, w) => { double d = w * (x[i] - x[j]); Cu[i] += d; Cu[j] -= d; }, true);
            for (int k = 0; k < m; ++k)
                kt[k] = kb[k] + Cu[loop[k]];
        }

        // given target angles on the boundary: C u = [-K_I ; k~ - k], u up to a constant (chosen so that the
        // length weighted mean of u on the boundary is 0)
        static void neumannToDirichlet(ParamMesh pm, ConstrainedQuadratic neu, double[] negK,
            int[] loop, double[] kb, double[] kt, double[] len, double[] ub, double[] x)
        {
            int m = loop.Length;
            double[] rhs = (double[])negK.Clone();
            for (int k = 0; k < m; ++k)
                rhs[loop[k]] = kt[k] - kb[k];
            neu.Solve(rhs, x);
            double mean = 0, L = 0;
            for (int k = 0; k < m; ++k) {
                double w = 0.5 * (len[k] + len[(k + m - 1) % m]);
                mean += w * x[loop[k]]; L += w;
            }
            mean /= L;
            for (int k = 0; k < m; ++k)
                ub[k] = x[loop[k]] - mean;
        }
    }
}
