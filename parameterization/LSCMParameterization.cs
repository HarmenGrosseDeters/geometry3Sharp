// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Least squares conformal map (Lévy, Petitjean, Ray, Maillot 2002), equivalently discrete conformal
    /// parameterization (Desbrun, Meyer, Alliez 2002): minimizes the conformal energy
    /// E_C = E_Dirichlet - A(uv), which is zero exactly for conformal maps, with a free boundary.
    /// A(uv) is the signed UV area, written as a sum over the boundary edges (minus the triangles that are
    /// degenerate on the surface, which have no Dirichlet energy either).
    ///
    /// Works on any connected mesh with at least one boundary loop (discs, annuli, ...). Two (or more) pinned
    /// vertices fix the similarity transform; by default two boundary vertices far apart are pinned, and the
    /// result is scaled to the surface area. Triangles can flip on strongly curved inputs; use SLIM for a
    /// guaranteed flip-free map.
    /// </summary>
    /// <example>
    /// var lscm = new LSCMParameterization(mesh);
    /// ParameterizationResult r = lscm.Compute();
    /// if (r.Succeeded) r.ApplyToMesh(mesh);   // r.Distortion.MeanConformal is close to 1
    /// </example>
    public class LSCMParameterization
    {
        public DMesh3 Mesh;
        /// <summary>
        /// fixed UV positions by vertex id (at least two). Null: pin two boundary vertices far apart.
        /// </summary>
        public Dictionary<int, Vector2d> PinnedVertices;
        /// <summary> with automatic pins, scale the result so that the UV area equals the surface area </summary>
        public bool ScaleToSurfaceArea = true;

        public ParameterizationResult Result;

        public LSCMParameterization(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public ParameterizationResult Compute()
        {
            ParamMesh pm = new ParamMesh(Mesh);
            if (!pm.RequireOpenComponent())
                return Result = pm.Failure();
            double[] u, v;
            string error;
            ParameterizationStatus status = Solve(pm, PinnedVertices, out u, out v, out error);
            if (status != ParameterizationStatus.Success)
                return Result = ParameterizationResult.Failure(status, error);
            if (PinnedVertices == null && ScaleToSurfaceArea)
                pm.ScaleToSurfaceArea(u, v);
            Result = pm.MakeResult(u, v);
            Result.Energy = conformalEnergy(pm, u, v);
            return Result;
        }


        internal static ParameterizationStatus Solve(ParamMesh pm, Dictionary<int, Vector2d> pins,
            out double[] u, out double[] v, out string error)
        {
            u = v = null;
            error = null;
            int N = pm.N;
            bool[] isFixed = new bool[2 * N];
            double[] fixedValue = new double[2 * N];
            if (pins == null) {
                int a, b;
                farBoundaryPair(pm, out a, out b);
                isFixed[2 * a] = isFixed[2 * a + 1] = isFixed[2 * b] = isFixed[2 * b + 1] = true;
                fixedValue[2 * b] = pm.Pos[a].Distance(pm.Pos[b]);
            } else {
                if (pins.Count < 2) {
                    error = "LSCM needs at least two pinned vertices";
                    return ParameterizationStatus.InvalidConstraints;
                }
                var positions = new HashSet<Vector2d>();
                foreach (var kv in pins) {
                    int vid = kv.Key;
                    if (vid < 0 || vid >= pm.ToIndex.Length || pm.ToIndex[vid] < 0) {
                        error = "pinned vertex " + vid + " is not a vertex of a triangle";
                        return ParameterizationStatus.InvalidConstraints;
                    }
                    int i = pm.ToIndex[vid];
                    isFixed[2 * i] = isFixed[2 * i + 1] = true;
                    fixedValue[2 * i] = kv.Value.x; fixedValue[2 * i + 1] = kv.Value.y;
                    positions.Add(kv.Value);
                }
                if (positions.Count < 2) {
                    error = "pinned vertices need at least two distinct positions";
                    return ParameterizationStatus.InvalidConstraints;
                }
            }

            ConstrainedQuadratic q = new ConstrainedQuadratic(2 * N, isFixed, fixedValue, 2);
            // Dirichlet energy of u and v
            pm.ForEachCotanEdge((i, j, w) => {
                q.Add(2 * i, 2 * i, w); q.Add(2 * j, 2 * j, w); q.Add(2 * i, 2 * j, -w);
                q.Add(2 * i + 1, 2 * i + 1, w); q.Add(2 * j + 1, 2 * j + 1, w); q.Add(2 * i + 1, 2 * j + 1, -w);
            });
            // minus the signed area, 1/2 Sum over boundary edges (i->j) of (u_i v_j - u_j v_i)
            foreach (int[] loop in pm.BoundaryLoops)
                for (int k = 0; k < loop.Length; ++k) {
                    int i = loop[k], j = loop[(k + 1) % loop.Length];
                    q.Add(2 * i, 2 * j + 1, -0.5);
                    q.Add(2 * j, 2 * i + 1, 0.5);
                }
            // The boundary sum is the area of all triangles, but the Dirichlet energy skips the degenerate ones.
            // Add their area back, so that E_C = Sum over the other triangles of (E_D(T) - A(T)) >= 0: else the
            // minimizer gains by opening up a zero-area triangle (eg at a T-junction) and the map is distorted.
            for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                if (!pm.Frames[ti].Degenerate) continue;
                Index3i t = pm.Tris[ti];
                for (int k = 0; k < 3; ++k) {
                    int i = t[k], j = t[(k + 1) % 3];
                    q.Add(2 * i, 2 * j + 1, 0.5);
                    q.Add(2 * j, 2 * i + 1, -0.5);
                }
            }
            if (!q.Factorize()) {
                error = "LSCM system: " + q.Error;
                return ParameterizationStatus.SolverFailed;
            }
            double[] x = new double[2 * N];
            q.Solve(null, x);
            u = new double[N]; v = new double[N];
            for (int i = 0; i < N; ++i) { u[i] = x[2 * i]; v[i] = x[2 * i + 1]; }
            if (!pm.AllFinite(u, v)) {
                error = "LSCM solution is not finite";
                return ParameterizationStatus.SolverFailed;
            }
            return ParameterizationStatus.Success;
        }

        // two vertices of the longest boundary loop that are far apart (two passes of farthest point)
        static void farBoundaryPair(ParamMesh pm, out int a, out int b)
        {
            int[] loop = pm.BoundaryLoops[0];
            foreach (int[] l in pm.BoundaryLoops)
                if (l.Length > loop.Length) loop = l;
            a = loop[0]; b = loop[0];
            for (int pass = 0; pass < 2; ++pass) {
                double best = -1;
                int from = pass == 0 ? loop[0] : a;
                foreach (int i in loop) {
                    double d = pm.Pos[i].DistanceSquared(pm.Pos[from]);
                    if (d > best) { best = d; if (pass == 0) a = i; else b = i; }
                }
            }
            if (a == b)
                b = loop[loop.Length / 2];
        }

        static double conformalEnergy(ParamMesh pm, double[] u, double[] v)
        {
            double ed = 0;
            pm.ForEachCotanEdge((i, j, w) => {
                double du = u[i] - u[j], dv = v[i] - v[j];
                ed += 0.5 * w * (du * du + dv * dv);
            });
            double area = 0;
            for (int ti = 0; ti < pm.Tris.Count; ++ti)
                if (!pm.Frames[ti].Degenerate)
                    area += 0.5 * ParamMesh.SignedArea2(u, v, pm.Tris[ti]);
            return (ed - area) / pm.SurfaceArea;
        }
    }
}
