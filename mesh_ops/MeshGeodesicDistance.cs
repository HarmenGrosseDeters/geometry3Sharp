// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Geodesic distances along a triangle mesh with the heat method (Crane, Weischedel and Wardetzky,
    /// "Geodesics in Heat", 2013): heat diffused from the sources for a short time t gives the direction
    /// to them; its normalized gradient field is integrated by a Poisson equation into the distance.
    /// Much closer to the true surface distance than Dijkstra along mesh edges (which zigzags), and the
    /// matrices are reused for further sources.
    ///
    /// Accuracy (tests on planes, cylinders and terrain): about 2% of the distance plus half an edge length;
    /// near the sources the distance is smoothed over a few edge lengths. Computed relative to the mesh
    /// center, so UTM coordinates are fine.
    /// </summary>
    public class MeshGeodesicDistance
    {
        public DMesh3 Mesh;
        /// <summary> diffusion time = TimeFactor * (mean edge length)^2 </summary>
        public double TimeFactor = 1.0;
        public int MaxIterations = 20000;

        /// <summary> distance per vertex id (size MaxVertexID; NaN for unreachable or unused ids) </summary>
        public double[] Distance;


        public MeshGeodesicDistance(DMesh3 mesh)
        {
            Mesh = mesh;
        }


        int N;
        int[] toIndex, toVertex;
        Vector3d[] pos;
        PackedSparseMatrix L;      // cotan Laplacian, positive semidefinite
        double[] mass, diagL;
        double meanEdge;
        List<Index3i> tris;
        List<Vector3d> cot;         // cotangent at each corner of tris

        void precompute()
        {
            toIndex = new int[Mesh.MaxVertexID];
            toVertex = new int[Mesh.VertexCount];
            N = 0;
            foreach (int vid in Mesh.VertexIndices()) {
                toIndex[vid] = N;
                toVertex[N++] = vid;
            }
            Vector3d center = Mesh.CachedBounds.Center;
            pos = new Vector3d[N];
            for (int i = 0; i < N; ++i)
                pos[i] = Mesh.GetVertex(toVertex[i]) - center;

            SymmetricSparseMatrix M = new SymmetricSparseMatrix();
            mass = new double[N];
            diagL = new double[N];
            tris = new List<Index3i>();
            cot = new List<Vector3d>();
            double edgeSum = 0; int edgeCount = 0;
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                Index3i ti = new Index3i(toIndex[t.a], toIndex[t.b], toIndex[t.c]);
                Vector3d p0 = pos[ti.a], p1 = pos[ti.b], p2 = pos[ti.c];
                double area = 0.5 * (p1 - p0).Cross(p2 - p0).Length;
                if (area < 1e-14 * ((p1 - p0).LengthSquared + (p2 - p0).LengthSquared))
                    continue;
                Vector3d c = new Vector3d(cotangent(p1 - p0, p2 - p0), cotangent(p2 - p1, p0 - p1), cotangent(p0 - p2, p1 - p2));
                tris.Add(ti);
                cot.Add(c);
                for (int k = 0; k < 3; ++k) {
                    int i = ti[k], j = ti[(k + 1) % 3];
                    double w = 0.5 * c[(k + 2) % 3];     // the corner opposite edge (i, j)
                    M.Set(i, j, M[i, j] - w);
                    diagL[i] += w; diagL[j] += w;
                    mass[ti[k]] += area / 3;
                }
                edgeSum += (p1 - p0).Length + (p2 - p1).Length + (p0 - p2).Length;
                edgeCount += 3;
            }
            for (int i = 0; i < N; ++i)
                M.Set(i, i, diagL[i]);
            L = new PackedSparseMatrix(M);
            meanEdge = (edgeCount > 0) ? edgeSum / edgeCount : 1;
        }

        static double cotangent(Vector3d a, Vector3d b)
        {
            double s = a.Cross(b).Length;
            return (s > 1e-300) ? a.Dot(b) / s : 0;
        }


        /// <summary> distances from the given source vertices (to the nearest of them) </summary>
        public bool Compute(IEnumerable<int> sources)
        {
            if (L == null)
                precompute();
            List<int> src = new List<int>();
            foreach (int s in sources)
                if (Mesh.IsVertex(s)) src.Add(toIndex[s]);
            if (src.Count == 0 || N == 0)
                return false;

            // 1. heat flow: (M + t L) u = delta. u decays like exp(-distance / sqrt(t)) (eg 1e-30 at 70 edge
            // lengths), far below the accuracy of CG relative to |u|. With non-negative weights the system is
            // an M-matrix, and Jacobi iteration sums only positive terms: every entry is accurate relative
            // to itself, however small.
            // On meshes with a boundary, the average of the Neumann (insulated) and Dirichlet (u = 0)
            // solutions avoids the bending of the distance along the boundary (Crane et al.).
            double t = TimeFactor * meanEdge * meanEdge;
            double[] u = heat(src, t, false);
            if (u == null)
                return false;
            if (Mesh.IsClosed() == false) {
                double[] ud = heat(src, t, true);
                for (int i = 0; i < N; ++i)
                    u[i] = 0.5 * (u[i] + ud[i]);
            }

            // 2. normalized gradient X = -grad u / |grad u| per triangle, 3. its divergence per vertex
            double[] div = new double[N];
            for (int k = 0; k < tris.Count; ++k) {
                Index3i ti = tris[k];
                Vector3d p0 = pos[ti.a], p1 = pos[ti.b], p2 = pos[ti.c];
                Vector3d n = (p1 - p0).Cross(p2 - p0);
                double area2 = n.Length;
                n /= area2;
                // grad u = 1/(2A) sum_i u_i (N x e_i), e_i the edge opposite corner i (counter-clockwise)
                Vector3d g = (u[ti.a] * n.Cross(p2 - p1) + u[ti.b] * n.Cross(p0 - p2) + u[ti.c] * n.Cross(p1 - p0)) / area2;
                double gl = g.Length;
                if (gl < 1e-300) continue;
                Vector3d X = -g / gl;
                Vector3d c = cot[k];
                Vector3d[] p = { p0, p1, p2 };
                for (int a = 0; a < 3; ++a) {
                    int i = ti[a];
                    Vector3d e1 = p[(a + 1) % 3] - p[a], e2 = p[(a + 2) % 3] - p[a];
                    // cot of the angle opposite e1 is at corner a+2, opposite e2 at corner a+1
                    div[i] += 0.5 * (c[(a + 2) % 3] * e1.Dot(X) + c[(a + 1) % 3] * e2.Dot(X));
                }
            }

            // 4. Poisson: L phi = -div (L is minus the Laplacian), regularized to be definite
            double eps = 1e-8;
            double[] phi = new double[N], rhs = new double[N];
            double mean = 0;
            for (int i = 0; i < N; ++i) mean += div[i];
            mean /= N;
            for (int i = 0; i < N; ++i) rhs[i] = -(div[i] - mean);
            if (solve((x, r) => { L.Multiply_Parallel(x, r); for (int i = 0; i < N; ++i) r[i] += eps * mass[i] * x[i]; },
                      (i) => diagL[i] + eps * mass[i], rhs, phi) == false)
                return false;

            // distances are relative to the sources
            double minSrc = double.MaxValue;
            foreach (int s in src) minSrc = Math.Min(minSrc, phi[s]);
            Distance = new double[Mesh.MaxVertexID];
            for (int k = 0; k < Distance.Length; ++k)
                Distance[k] = double.NaN;
            for (int i = 0; i < N; ++i)
                Distance[toVertex[i]] = Math.Max(0, phi[i] - minSrc);
            return true;
        }

        // neighbours with non-negative cotan weights (negative ones, at obtuse angles, set to 0)
        int[] nbStart, nbIndex;
        double[] nbWeight;

        void build_neighbours()
        {
            var lists = new List<KeyValuePair<int, double>>[N];
            for (int i = 0; i < N; ++i) lists[i] = new List<KeyValuePair<int, double>>();
            Dictionary<long, double> w = new Dictionary<long, double>();
            for (int k = 0; k < tris.Count; ++k) {
                Index3i ti = tris[k];
                for (int a = 0; a < 3; ++a) {
                    int i = ti[a], j = ti[(a + 1) % 3];
                    long key = (long)Math.Min(i, j) * N + Math.Max(i, j);
                    double c = 0.5 * cot[k][(a + 2) % 3];
                    double cur;
                    w[key] = w.TryGetValue(key, out cur) ? cur + c : c;
                }
            }
            foreach (var kv in w) {
                int i = (int)(kv.Key / N), j = (int)(kv.Key % N);
                double c = Math.Max(0, kv.Value);
                lists[i].Add(new KeyValuePair<int, double>(j, c));
                lists[j].Add(new KeyValuePair<int, double>(i, c));
            }
            nbStart = new int[N + 1];
            for (int i = 0; i < N; ++i) nbStart[i + 1] = nbStart[i] + lists[i].Count;
            nbIndex = new int[nbStart[N]];
            nbWeight = new double[nbStart[N]];
            for (int i = 0; i < N; ++i)
                for (int k = 0; k < lists[i].Count; ++k) {
                    nbIndex[nbStart[i] + k] = lists[i][k].Key;
                    nbWeight[nbStart[i] + k] = lists[i][k].Value;
                }
        }

        // Jacobi iteration for (M + t L+) u = delta until every entry has converged relative to itself
        double[] heat(List<int> src, double t, bool dirichlet)
        {
            if (nbStart == null)
                build_neighbours();
            bool[] fixedZero = new bool[N];
            if (dirichlet) {
                foreach (int vid in Mesh.VertexIndices())
                    if (Mesh.IsBoundaryVertex(vid)) fixedZero[toIndex[vid]] = true;
                foreach (int s in src) fixedZero[s] = false;
            }
            double[] u = new double[N], next = new double[N], b = new double[N];
            foreach (int s in src) b[s] = 1;
            double[] diag = new double[N];
            for (int i = 0; i < N; ++i) {
                double sw = 0;
                for (int k = nbStart[i]; k < nbStart[i + 1]; ++k) sw += nbWeight[k];
                diag[i] = mass[i] + t * sw;
            }
            for (int iter = 0; iter < MaxIterations; ++iter) {
                double maxRel = 0;
                object lockObj = new object();
                gParallel.BlockStartEnd(0, N - 1, (i0, i1) => {
                    double localMax = 0;
                    for (int i = i0; i <= i1; ++i) {
                        if (fixedZero[i]) { next[i] = 0; continue; }
                        double sum = b[i];
                        for (int k = nbStart[i]; k < nbStart[i + 1]; ++k)
                            sum += t * nbWeight[k] * u[nbIndex[k]];
                        double v = sum / diag[i];
                        next[i] = v;
                        if (v > 0)
                            localMax = Math.Max(localMax, Math.Abs(v - u[i]) / v);
                        else if (u[i] != 0)
                            localMax = 1;
                    }
                    lock (lockObj) maxRel = Math.Max(maxRel, localMax);
                });
                double[] tmp = u; u = next; next = tmp;
                // all entries reached (non-zero) and converged
                if (maxRel < 1e-10 && iter > 2)
                    return u;
            }
            return u;
        }

        // Jacobi-preconditioned conjugate gradients
        bool solve(Action<double[], double[]> multiply, Func<int, double> diag, double[] b, double[] x)
        {
            double[] inv = new double[N];
            for (int i = 0; i < N; ++i) {
                double d = diag(i);
                inv[i] = (d > 0) ? 1.0 / d : 1.0;
            }
            var cg = new SparseSymmetricCG() {
                B = b, X = x, MultiplyF = multiply, MaxIterations = MaxIterations, UseXAsInitialGuess = false,
                PreconditionMultiplyF = (v, r) => { for (int i = 0; i < N; ++i) r[i] = inv[i] * v[i]; }
            };
            return cg.SolvePreconditioned();
        }
    }
}
