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
    /// center, so UTM coordinates are fine. Each connected component is measured from its own sources;
    /// components without a source (and isolated vertices) get NaN. Far from the sources (beyond about
    /// 700 edge lengths) the heat underflows doubles; then it is computed as its logarithm.
    ///
    /// With IntrinsicDelaunay the heat flow and the Poisson equation are solved on the intrinsic Delaunay
    /// triangulation of the mesh (see IntrinsicTriangulation): same surface, but all cotan weights are
    /// non-negative. Much more accurate on poor triangulations (slivers, obtuse triangles), where the
    /// cotan Laplacian of the mesh itself has negative weights.
    ///
    /// Usage: <c>var geo = new MeshGeodesicDistance(mesh); if (geo.Compute(new[] { sourceVid })) d = geo.Distance[vid];</c>
    /// The mesh is not modified. The first Compute() builds the Laplacian (cost grows with the vertex count);
    /// later calls with other sources reuse it until the mesh changes (ShapeTimestamp).
    /// Distances are in mesh units, 0 at the sources and non-negative elsewhere.
    /// </summary>
    public class MeshGeodesicDistance
    {
        /// <summary> input mesh (not modified) </summary>
        public DMesh3 Mesh;
        /// <summary> diffusion time = TimeFactor * (mean edge length)^2 </summary>
        public double TimeFactor = 1.0;
        /// <summary> iteration limit of the linear solvers; Compute() returns false if the Poisson solve does not converge </summary>
        public int MaxIterations = 20000;
        /// <summary> compute on the intrinsic Delaunay triangulation of the mesh (recommended for poor meshes) </summary>
        public bool IntrinsicDelaunay = false;
        /// <summary> the intrinsic triangulation used by the last computation (null unless IntrinsicDelaunay) </summary>
        public IntrinsicTriangulation Intrinsic { get; private set; }

        /// <summary> distance per vertex id (size MaxVertexID; NaN for unreachable or unused ids) </summary>
        public double[] Distance;


        /// <summary> Set up geodesic distances on mesh. Nothing is computed until Compute() is called. </summary>
        public MeshGeodesicDistance(DMesh3 mesh)
        {
            Mesh = mesh;
        }


        int N;
        int[] toIndex, toVertex;
        PackedSparseMatrix L;      // cotan Laplacian, positive semidefinite
        double[] mass, diagL;
        double meanEdge;
        List<Index3i> tris;
        List<Vector3d> cot;         // cotangent at each corner of tris
        List<Vector2d[]> layout;    // isometric planar layout of each of tris (counter-clockwise)
        bool computedIntrinsic;

        int timestamp = -1;
        int[] component;            // per index; -1 for vertices without a (non-degenerate) triangle
        int componentCount;

        void precompute()
        {
            timestamp = Mesh.ShapeTimestamp;
            nbStart = null; spatial = null;
            toIndex = new int[Mesh.MaxVertexID];
            toVertex = new int[Mesh.VertexCount];
            N = 0;
            foreach (int vid in Mesh.VertexIndices()) {
                toIndex[vid] = N;
                toVertex[N++] = vid;
            }
            Vector3d center = Mesh.CachedBounds.Center;
            Vector3d[] pos = new Vector3d[N];
            for (int i = 0; i < N; ++i)
                pos[i] = Mesh.GetVertex(toVertex[i]) - center;

            SymmetricSparseMatrix M = new SymmetricSparseMatrix();
            mass = new double[N];
            diagL = new double[N];
            tris = new List<Index3i>();
            cot = new List<Vector3d>();
            layout = new List<Vector2d[]>();
            double edgeSum = 0; int edgeCount = 0;
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                Vector3d p0 = pos[toIndex[t.a]], p1 = pos[toIndex[t.b]], p2 = pos[toIndex[t.c]];
                edgeSum += (p1 - p0).Length + (p2 - p1).Length + (p0 - p2).Length;
                edgeCount += 3;
            }
            meanEdge = (edgeCount > 0) ? edgeSum / edgeCount : 1;
            Intrinsic = null;
            computedIntrinsic = IntrinsicDelaunay;
            if (IntrinsicDelaunay) {
                Intrinsic = new IntrinsicTriangulation(Mesh);
                Intrinsic.FlipToDelaunay();
                for (int k = 0; k < Intrinsic.TriangleCount; ++k) {
                    Index3i t = Intrinsic.GetTriangle(k);
                    Vector2d q0, q1, q2;
                    Intrinsic.GetTriangleLayout(k, out q0, out q1, out q2);
                    add_triangle(M, new Index3i(toIndex[t.a], toIndex[t.b], toIndex[t.c]), q0, q1, q2,
                        Intrinsic.TriangleArea(k), false);
                }
            } else {
                foreach (int tid in Mesh.TriangleIndices()) {
                    Index3i t = Mesh.GetTriangle(tid);
                    Index3i ti = new Index3i(toIndex[t.a], toIndex[t.b], toIndex[t.c]);
                    Vector3d p0 = pos[ti.a], p1 = pos[ti.b], p2 = pos[ti.c];
                    Vector3d n = (p1 - p0).Cross(p2 - p0);
                    double area = 0.5 * n.Length;
                    if (area < 1e-14 * ((p1 - p0).LengthSquared + (p2 - p0).LengthSquared))
                        continue;
                    Vector3d ex = (p1 - p0) / (p1 - p0).Length, ey = (n / (2 * area)).Cross(ex);
                    Vector2d q2 = new Vector2d((p2 - p0).Dot(ex), (p2 - p0).Dot(ey));
                    add_triangle(M, ti, Vector2d.Zero, new Vector2d((p1 - p0).Length, 0), q2, area, true);
                }
            }
            for (int i = 0; i < N; ++i)
                M.Set(i, i, diagL[i]);
            L = new PackedSparseMatrix(M);

            // connected components of the (non-degenerate) triangles
            int[] parent = new int[N];
            for (int i = 0; i < N; ++i) parent[i] = i;
            Func<int, int> find = null;
            find = (i) => { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; };
            bool[] used = new bool[N];
            foreach (Index3i ti in tris) {
                used[ti.a] = used[ti.b] = used[ti.c] = true;
                parent[find(ti.b)] = find(ti.a);
                parent[find(ti.c)] = find(ti.a);
            }
            component = new int[N];
            var ids = new Dictionary<int, int>();
            for (int i = 0; i < N; ++i) {
                if (used[i] == false) { component[i] = -1; continue; }
                int r = find(i), c;
                if (ids.TryGetValue(r, out c) == false) { c = ids.Count; ids[r] = c; }
                component[i] = c;
            }
            componentCount = ids.Count;
        }

        void add_triangle(SymmetricSparseMatrix M, Index3i ti, Vector2d q0, Vector2d q1, Vector2d q2, double area, bool lengthsFromLayout)
        {
            Vector3d c = new Vector3d(cotangent(q1 - q0, q2 - q0), cotangent(q2 - q1, q0 - q1), cotangent(q0 - q2, q1 - q2));
            tris.Add(ti);
            cot.Add(c);
            layout.Add(new Vector2d[] { q0, q1, q2 });
            for (int k = 0; k < 3; ++k) {
                int i = ti[k], j = ti[(k + 1) % 3];
                double w = 0.5 * c[(k + 2) % 3];     // the corner opposite edge (i, j)
                if (i != j) {
                    M.Set(i, j, M[i, j] - w);
                    diagL[i] += w; diagL[j] += w;
                }
                mass[ti[k]] += area / 3;
            }
        }

        static double cotangent(Vector2d a, Vector2d b)
        {
            double s = Math.Abs(a.x * b.y - a.y * b.x);
            return (s > 1e-300) ? a.Dot(b) / s : 0;
        }


        /// <summary>
        /// Compute distances from the given source vertex IDs (to the nearest of them) into Distance.
        /// Invalid and duplicate IDs are ignored. Returns false (and Distance is null) if there is no valid
        /// source or the solver fails; otherwise true.
        /// </summary>
        public bool Compute(IEnumerable<int> sources)
        {
            if (L == null || timestamp != Mesh.ShapeTimestamp || computedIntrinsic != IntrinsicDelaunay)
                precompute();
            Distance = null; sourceVertices = null;
            List<int> src = new List<int>();
            var srcVertices = new HashSet<int>();
            bool[] hasSource = new bool[componentCount];
            foreach (int s in sources) {
                if (s < 0 || s >= Mesh.MaxVertexID || Mesh.IsVertex(s) == false || srcVertices.Contains(s)) continue;
                srcVertices.Add(s);
                if (component[toIndex[s]] < 0) continue;      // isolated: distance 0 only to itself
                src.Add(toIndex[s]);
                hasSource[component[toIndex[s]]] = true;
            }
            if (srcVertices.Count == 0 || N == 0)
                return false;
            sourceVertices = srcVertices;
            if (src.Count == 0) {
                Distance = new double[Mesh.MaxVertexID];
                for (int k = 0; k < Distance.Length; ++k) Distance[k] = double.NaN;
                foreach (int s in srcVertices) Distance[s] = 0;
                return true;
            }

            // 1. heat flow: (M + t L) u = delta. u decays like exp(-distance / sqrt(t)) (eg 1e-30 at 70 edge
            // lengths), far below the accuracy of CG relative to |u|. With non-negative weights the system is
            // an M-matrix, and Jacobi iteration sums only positive terms: every entry is accurate relative
            // to itself, however small.
            // On meshes with a boundary, the average of the Neumann (insulated) and Dirichlet (u = 0)
            // solutions avoids the bending of the distance along the boundary (Crane et al.).
            double t = TimeFactor * meanEdge * meanEdge;
            bool open = Mesh.IsClosed() == false;
            // u holds log(heat) (-inf for 0): first computed directly, in the log domain if it underflows
            double[] u = heat(src, t, false);
            bool underflow = false;
            for (int i = 0; i < N && underflow == false; ++i)
                if (component[i] >= 0 && hasSource[component[i]] && !(u[i] > 1e-280)) underflow = true;
            if (underflow) {
                u = heat_log(src, t, false);
                if (open) {
                    double[] ud = heat_log(src, t, true);
                    for (int i = 0; i < N; ++i) u[i] = log_add(u[i], ud[i]) - Math.Log(2);
                }
            } else {
                if (open) {
                    double[] ud = heat(src, t, true);
                    for (int i = 0; i < N; ++i) u[i] = 0.5 * (u[i] + ud[i]);
                }
                for (int i = 0; i < N; ++i) u[i] = Math.Log(u[i]);
            }

            // 2. normalized gradient X = -grad u / |grad u| per triangle, 3. its divergence per vertex
            double[] div = new double[N];
            for (int k = 0; k < tris.Count; ++k) {
                Index3i ti = tris[k];
                Vector2d[] p = layout[k];
                Vector2d p0 = p[0], p1 = p[1], p2 = p[2];
                double area2 = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
                // grad u = 1/(2A) sum_i u_i J e_i, e_i the edge opposite corner i (counter-clockwise) and J
                // the rotation by 90 degrees; only its direction is used, so the heat is scaled by its maximum
                double lm = Math.Max(u[ti.a], Math.Max(u[ti.b], u[ti.c]));
                if (double.IsNegativeInfinity(lm)) continue;
                double ua = Math.Exp(u[ti.a] - lm), ub = Math.Exp(u[ti.b] - lm), uc = Math.Exp(u[ti.c] - lm);
                Vector2d g = (ua * perp(p2 - p1) + ub * perp(p0 - p2) + uc * perp(p1 - p0)) / area2;
                double gl = g.Length;
                if (gl < 1e-300) continue;
                Vector2d X = -g / gl;
                Vector3d c = cot[k];
                for (int a = 0; a < 3; ++a) {
                    int i = ti[a];
                    Vector2d e1 = p[(a + 1) % 3] - p[a], e2 = p[(a + 2) % 3] - p[a];
                    // cot of the angle opposite e1 is at corner a+2, opposite e2 at corner a+1
                    div[i] += 0.5 * (c[(a + 2) % 3] * e1.Dot(X) + c[(a + 1) % 3] * e2.Dot(X));
                }
            }

            // 4. Poisson: L phi = -div (L is minus the Laplacian), regularized to be definite
            double eps = 1e-8;
            // (the divergence sums to 0 on each component; the rounding error is removed per component)
            double[] phi = new double[N], rhs = new double[N];
            double[] csum = new double[componentCount]; int[] ccount = new int[componentCount];
            for (int i = 0; i < N; ++i)
                if (component[i] >= 0) { csum[component[i]] += div[i]; ccount[component[i]]++; }
            for (int i = 0; i < N; ++i)
                if (component[i] >= 0 && hasSource[component[i]])
                    rhs[i] = -(div[i] - csum[component[i]] / ccount[component[i]]);
            if (solve((x, r) => { L.Multiply_Parallel(x, r); for (int i = 0; i < N; ++i) r[i] += eps * mass[i] * x[i]; },
                      (i) => diagL[i] + eps * mass[i], rhs, phi) == false)
                return false;

            // distances are relative to the sources of each component
            double[] minSrc = new double[componentCount];
            for (int c = 0; c < componentCount; ++c) minSrc[c] = double.MaxValue;
            foreach (int s in src) minSrc[component[s]] = Math.Min(minSrc[component[s]], phi[s]);
            Distance = new double[Mesh.MaxVertexID];
            for (int k = 0; k < Distance.Length; ++k)
                Distance[k] = double.NaN;
            for (int i = 0; i < N; ++i)
                if (component[i] >= 0 && hasSource[component[i]])
                    Distance[toVertex[i]] = Math.Max(0, phi[i] - minSrc[component[i]]);
            foreach (int s in srcVertices) Distance[s] = 0;
            return true;
        }

        static Vector2d perp(Vector2d v) { return new Vector2d(-v.y, v.x); }

        HashSet<int> sourceVertices;
        DMeshAABBTree3 spatial;

        /// <summary>
        /// Shortest path on the surface from the point (projected onto the mesh) to the nearest source of
        /// the last Compute(), as a polyline ending at that source vertex: follows the steepest descent of
        /// the distance across the triangles (along an edge where it runs into a valley). From a component
        /// without a source the path is just the start point.
        /// </summary>
        public List<Vector3d> TracePath(Vector3d point)
        {
            check_computed();
            if (spatial == null)
                spatial = new DMeshAABBTree3(Mesh, true);
            int tid = spatial.FindNearestTriangle(point);
            if (tid < 0)
                return new List<Vector3d>();
            Vector3d p = MeshQueries.TriangleDistance(Mesh, tid, point).TriangleClosest;
            List<Vector3d> path = new List<Vector3d>() { p };
            trace(path, tid, p, -1);
            return path;
        }

        /// <summary> shortest path from a vertex to the nearest source (see TracePath(Vector3d)) </summary>
        public List<Vector3d> TracePath(int vertex)
        {
            check_computed();
            if (vertex < 0 || vertex >= Mesh.MaxVertexID || Mesh.IsVertex(vertex) == false)
                throw new ArgumentException("TracePath: not a vertex of the mesh");
            List<Vector3d> path = new List<Vector3d>() { Mesh.GetVertex(vertex) };
            trace(path, -1, Vector3d.Zero, vertex);
            return path;
        }

        void check_computed()
        {
            if (Distance == null || sourceVertices == null || timestamp != Mesh.ShapeTimestamp)
                throw new InvalidOperationException("TracePath: no distances computed for the current mesh");
        }

        double phi(int vid) { return Distance[vid]; }

        // gradient of the linear distance over the triangle, and the gradients of its barycentric coordinates
        bool gradients(int tid, out Vector3d g, Vector3d[] grads)
        {
            Index3i t = Mesh.GetTriangle(tid);
            Vector3d p0 = Mesh.GetVertex(t.a), p1 = Mesh.GetVertex(t.b), p2 = Mesh.GetVertex(t.c);
            Vector3d n = (p1 - p0).Cross(p2 - p0);
            double area2 = n.Length;
            g = Vector3d.Zero;
            if (area2 < 1e-300 || double.IsNaN(phi(t.a) + phi(t.b) + phi(t.c))) return false;
            n /= area2;
            grads[0] = n.Cross(p2 - p1) / area2;
            grads[1] = n.Cross(p0 - p2) / area2;
            grads[2] = n.Cross(p1 - p0) / area2;
            g = phi(t.a) * grads[0] + phi(t.b) * grads[1] + phi(t.c) * grads[2];
            return true;
        }

        // Walks down the distance: across triangle tid from p, or (v >= 0) from vertex v. One loop with a
        // bound on the total number of steps, so it always terminates.
        void trace(List<Vector3d> path, int tid, Vector3d p, int v)
        {
            Vector3d[] grads = new Vector3d[3];
            int maxSteps = 4 * Mesh.TriangleCount + 2 * Mesh.VertexCount + 10;
            for (int step = 0; step < maxSteps; ++step) {
                if (v >= 0) {
                    add(path, Mesh.GetVertex(v));
                    if (sourceVertices.Contains(v) || double.IsNaN(phi(v)))
                        return;
                    // a triangle around v into which the steepest descent points (inside the corner at v)
                    int bestT = -1; double bestSlope = 0;
                    foreach (int t in Mesh.VtxTrianglesItr(v)) {
                        Vector3d g;
                        if (gradients(t, out g, grads) == false || g.Length < 1e-12) continue;
                        Index3i tv = Mesh.GetTriangle(t);
                        bool inside = true;
                        for (int k = 0; k < 3; ++k)
                            if (tv[k] != v && grads[k].Dot(-g) < 0) inside = false;
                        if (inside && g.Length > bestSlope) { bestSlope = g.Length; bestT = t; }
                    }
                    if (bestT >= 0) {
                        tid = bestT; p = Mesh.GetVertex(v); v = -1;
                        continue;
                    }
                    // otherwise down the steepest edge; at a local minimum next to a source, to the source
                    int nextV = -1; double bestDrop = 0;
                    foreach (int nb in Mesh.VtxVerticesItr(v)) {
                        double drop = (phi(v) - phi(nb)) / Mesh.GetVertex(v).Distance(Mesh.GetVertex(nb));
                        if (drop > bestDrop) { bestDrop = drop; nextV = nb; }
                    }
                    if (nextV < 0)
                        foreach (int nb in Mesh.VtxVerticesItr(v))
                            if (sourceVertices.Contains(nb)) { nextV = nb; break; }
                    if (nextV < 0)
                        return;     // local minimum (should not happen away from the sources)
                    v = nextV;
                    continue;
                }

                Index3i tri = Mesh.GetTriangle(tid);
                for (int k = 0; k < 3; ++k)
                    if (sourceVertices.Contains(tri[k])) { add(path, Mesh.GetVertex(tri[k])); return; }
                Vector3d grad;
                if (gradients(tid, out grad, grads) == false || grad.Length < 1e-12) {
                    if (double.IsNaN(phi(tri.a) + phi(tri.b) + phi(tri.c))) return;
                    v = lowest_vertex(tri); continue;
                }
                Vector3d d = -grad.Normalized;
                // barycentric coordinates of p and their rates along d; leave through the first one to reach 0
                Vector3d bary = MathUtil.BarycentricCoords(p, Mesh.GetVertex(tri.a), Mesh.GetVertex(tri.b), Mesh.GetVertex(tri.c));
                double best = double.MaxValue; int exit = -1;
                for (int k = 0; k < 3; ++k) {
                    double rate = grads[k].Dot(d);
                    if (rate < -1e-15) {
                        double sk = Math.Max(0, bary[k]) / -rate;
                        if (sk < best) { best = sk; exit = k; }
                    }
                }
                if (exit < 0) { v = lowest_vertex(tri); continue; }
                Vector3d q = p + best * d;
                add(path, q);
                // edge opposite corner 'exit'
                int va = tri[(exit + 1) % 3], vb = tri[(exit + 2) % 3];
                Vector3d qb = MathUtil.BarycentricCoords(q, Mesh.GetVertex(tri.a), Mesh.GetVertex(tri.b), Mesh.GetVertex(tri.c));
                // at (or extremely close to) a corner: continue from the vertex
                int corner = -1;
                for (int k = 0; k < 3; ++k)
                    if (qb[k] > 1 - 1e-9) corner = tri[k];
                if (corner >= 0) { v = corner; continue; }
                int eid = Mesh.FindEdge(va, vb);
                Index2i et = Mesh.GetEdgeT(eid);
                int next = (et.a == tid) ? et.b : et.a;
                if (next < 0 || descent_enters(next, va, vb) == false) {
                    // boundary, or a valley along the edge: follow the edge downhill
                    v = phi(va) < phi(vb) ? va : vb;
                    continue;
                }
                tid = next; p = q;
            }
        }

        // does the steepest descent in triangle tid point into it from its edge (va, vb)?
        bool descent_enters(int tid, int va, int vb)
        {
            Vector3d[] grads = new Vector3d[3];
            Vector3d g;
            if (gradients(tid, out g, grads) == false || g.Length < 1e-12) return false;
            Index3i t = Mesh.GetTriangle(tid);
            int opp = (t.a != va && t.a != vb) ? 0 : (t.b != va && t.b != vb) ? 1 : 2;
            return grads[opp].Dot(-g) > 1e-15;
        }

        int lowest_vertex(Index3i t)
        {
            int v = t.a;
            if (phi(t.b) < phi(v)) v = t.b;
            if (phi(t.c) < phi(v)) v = t.c;
            return v;
        }

        static void add(List<Vector3d> path, Vector3d p)
        {
            if (path.Count == 0 || path[path.Count - 1].DistanceSquared(p) > 1e-24)
                path.Add(p);
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
                        if (fixedZero[i] || diag[i] <= 0) { next[i] = 0; continue; }
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

        static double log_add(double a, double b)
        {
            double m = Math.Max(a, b);
            if (double.IsNegativeInfinity(m)) return m;
            return m + Math.Log(Math.Exp(a - m) + Math.Exp(b - m));
        }

        // the same Jacobi iteration on log(u): no underflow however far from the sources
        double[] heat_log(List<int> src, double t, bool dirichlet)
        {
            if (nbStart == null)
                build_neighbours();
            bool[] fixedZero = new bool[N];
            if (dirichlet) {
                foreach (int vid in Mesh.VertexIndices())
                    if (Mesh.IsBoundaryVertex(vid)) fixedZero[toIndex[vid]] = true;
                foreach (int s in src) fixedZero[s] = false;
            }
            bool[] isSource = new bool[N];
            foreach (int s in src) isSource[s] = true;
            double[] u = new double[N], next = new double[N], logDiag = new double[N];
            double[] logW = new double[nbIndex.Length];
            for (int k = 0; k < logW.Length; ++k) logW[k] = (nbWeight[k] > 0) ? Math.Log(t * nbWeight[k]) : double.NegativeInfinity;
            for (int i = 0; i < N; ++i) {
                double sw = 0;
                for (int k = nbStart[i]; k < nbStart[i + 1]; ++k) sw += nbWeight[k];
                logDiag[i] = Math.Log(mass[i] + t * sw);
                u[i] = double.NegativeInfinity;
            }
            for (int iter = 0; iter < MaxIterations; ++iter) {
                double maxChange = 0;
                object lockObj = new object();
                gParallel.BlockStartEnd(0, N - 1, (i0, i1) => {
                    double localMax = 0;
                    for (int i = i0; i <= i1; ++i) {
                        if (fixedZero[i] || double.IsNegativeInfinity(logDiag[i])) { next[i] = double.NegativeInfinity; continue; }
                        double m = isSource[i] ? 0 : double.NegativeInfinity;
                        for (int k = nbStart[i]; k < nbStart[i + 1]; ++k)
                            m = Math.Max(m, logW[k] + u[nbIndex[k]]);
                        if (double.IsNegativeInfinity(m)) { next[i] = m; continue; }
                        double sum = isSource[i] ? Math.Exp(-m) : 0;
                        for (int k = nbStart[i]; k < nbStart[i + 1]; ++k)
                            sum += Math.Exp(logW[k] + u[nbIndex[k]] - m);
                        double v = m + Math.Log(sum) - logDiag[i];
                        next[i] = v;
                        localMax = Math.Max(localMax, double.IsNegativeInfinity(u[i]) ? 1 : Math.Abs(v - u[i]));
                    }
                    lock (lockObj) maxChange = Math.Max(maxChange, localMax);
                });
                double[] tmp = u; u = next; next = tmp;
                if (maxChange < 1e-10 && iter > 2)
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
