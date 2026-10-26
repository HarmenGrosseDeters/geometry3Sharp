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
    /// Accuracy (tests on planes, discs, cylinders and terrain): about 2% of the distance plus half the mean
    /// edge length; near the sources the distance is smoothed over a few edge lengths. Near open boundaries,
    /// and for sources on the boundary, allow 2% plus one mean edge length: the boundary condition of the heat
    /// flow (the average of the Neumann and Dirichlet solutions, Crane et al.) is only an approximation there.
    /// Regular grids whose diagonals all point the same way are anisotropic and the least accurate;
    /// alternating diagonals or irregular triangulations do better. Computed relative to the mesh
    /// center, so UTM coordinates are fine. Each connected component is measured from its own sources;
    /// components without a source (and isolated vertices) get NaN. Far from the sources (beyond about
    /// 700 edge lengths) the heat underflows doubles; then it is computed as its logarithm.
    /// Computed on the intrinsic Delaunay triangulation of the mesh (IntrinsicDelaunay, IntrinsicTriangulation;
    /// Sharp, Soliman and Crane 2019): caps, needles and obtuse triangles, eg along constrained feature lines
    /// after remeshing, would otherwise give negative cotan weights and wrong distances. Degenerate triangles
    /// are kept by intrinsic mollification (Sharp and Crane 2020). Meshes with non-manifold vertices (bowties)
    /// have no intrinsic triangulation; they use the mesh triangles (IntrinsicFlips 0), without the
    /// degenerate ones.
    ///
    /// Solvers: sparse Cholesky factorizations (SparseCholesky) of the heat flow and the Poisson problem,
    /// reused for further sources. The heat system is an M-matrix (cotan weights clamped to at least 0), so
    /// its solution is accurate entry by entry; where it underflows it is solved in the log domain
    /// (SparseCholesky.SolveLog). Several thousand edge lengths from the sources cost no more than near ones.
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
        /// <summary> no longer used: the linear systems are solved directly (kept for API compatibility) </summary>
        public int MaxIterations = 20000;
        /// <summary>
        /// compute on the intrinsic Delaunay triangulation of the mesh (same surface, edges flipped by
        /// length): robust against caps and needles, eg from remeshing with constrained feature lines.
        /// </summary>
        public bool IntrinsicDelaunay = true;
        /// <summary>
        /// also refine the intrinsic Delaunay triangulation by vertex insertion (IntrinsicTriangulation.DelaunayRefine,
        /// with RefineMinAngle and RefineMaxInsertions): boundary edges opposite obtuse angles are split, so
        /// all cotan weights are non-negative, and triangles get angles of at least RefineMinAngle degrees
        /// (0: only the boundary edges). More vertices (slower), more accurate on poor meshes. The distances
        /// are still reported at the mesh vertices. Needs manifold vertices; otherwise Refinement.Status is
        /// NonManifoldInput and the flips alone are used.
        /// </summary>
        public bool IntrinsicRefine = false;
        /// <summary> minimum angle (degrees, at most 30) of the refinement </summary>
        public double RefineMinAngle = 25;
        /// <summary> insertion limit of the refinement (0: 10 * vertices + 1000) </summary>
        public int RefineMaxInsertions = 0;
        /// <summary> result of the refinement (status, inserted vertices, minimum angle); null if not refined </summary>
        public IntrinsicRefinementResult Refinement;

        /// <summary> distance per vertex id (size MaxVertexID; NaN for unreachable or unused ids) </summary>
        public double[] Distance;
        /// <summary> number of intrinsic edge flips (0 on a Delaunay mesh) </summary>
        public int IntrinsicFlips;


        /// <summary> Set up geodesic distances on mesh. Nothing is computed until Compute() is called. </summary>
        public MeshGeodesicDistance(DMesh3 mesh)
        {
            Mesh = mesh;
        }


        int N;                      // indices: mesh vertices, then vertices inserted by the refinement
        int[] toIndex, toVertex;
        PackedSparseMatrix L;      // cotan Laplacian, positive semidefinite
        double[] mass;
        double meanEdge;
        List<Index3i> tris;         // the (intrinsic) triangles, by vertex index
        List<Vector3d> cot;         // cotangent at each corner of tris
        List<Vector3d> layout;      // tris in the plane: corners (0,0), (x,0), (y,z)
        List<KeyValuePair<Index2i, double>> heatEdges;     // edge weights of the heat flow (cotan, at least 0)

        int timestamp = -1;
        bool[] boundaryIndex;       // per index: on the boundary (mesh vertices and inserted vertices)
        int[] component;            // per index; -1 for vertices without a (non-degenerate) triangle
        int componentCount;
        int[] representative;       // per component: a vertex, fixed in the Neumann Poisson problem

        // factorizations: heat flow (Neumann, Dirichlet on the boundary) of diffusion time heatTime, Poisson
        SparseCholesky heatN, heatD, poissonN;
        double heatTime = double.NaN;

        // the options the triangulation was built with (a change rebuilds it)
        string builtSettings;
        string built_settings()
        {
            if (IntrinsicDelaunay && IntrinsicRefine)
                return "refine " + RefineMinAngle.ToString("R") + " " + RefineMaxInsertions;
            return IntrinsicDelaunay ? "delaunay" : "mesh";       // (the refine options do not matter)
        }

        internal int PrecomputeCount;       // (for tests)

        void precompute()
        {
            PrecomputeCount++;
            L = null;
            heatN = heatD = poissonN = null; heatTime = double.NaN;
            spatial = null;
            toIndex = new int[Mesh.MaxVertexID];
            toVertex = new int[Mesh.VertexCount];
            N = 0;
            foreach (int vid in Mesh.VertexIndices()) {
                toIndex[vid] = N;
                toVertex[N++] = vid;
            }
            int n0 = N;
            tris = new List<Index3i>();
            cot = new List<Vector3d>();
            layout = new List<Vector3d>();
            var area = new List<double>();
            double edgeSum = 0; int edgeCount = 0;
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                Vector3d p0 = Mesh.GetVertex(t.a), p1 = Mesh.GetVertex(t.b), p2 = Mesh.GetVertex(t.c);
                if (0.5 * (p1 - p0).Cross(p2 - p0).Length < 1e-14 * ((p1 - p0).LengthSquared + (p2 - p0).LengthSquared))
                    continue;
                edgeSum += (p1 - p0).Length + (p2 - p1).Length + (p0 - p2).Length;
                edgeCount += 3;
            }
            meanEdge = (edgeCount > 0) ? edgeSum / edgeCount : 1;

            // the intrinsic Delaunay triangulation (flipped, or refined); non-manifold meshes and
            // IntrinsicDelaunay = false use the mesh triangles
            IntrinsicTriangulation T = null;
            Refinement = null;
            IntrinsicFlips = 0;
            if (IntrinsicDelaunay) {
                try {
                    T = new IntrinsicTriangulation(Mesh);
                } catch (ArgumentException) {
                    if (IntrinsicRefine)
                        Refinement = new IntrinsicRefinementResult() { Status = IntrinsicRefinementStatus.NonManifoldInput };
                }
                if (T != null && IntrinsicRefine) {
                    Refinement = T.DelaunayRefine(RefineMinAngle, RefineMaxInsertions);
                    IntrinsicFlips = Refinement.Flips;
                } else if (T != null)
                    IntrinsicFlips = T.FlipToDelaunay();
            }
            if (T != null) {
                N = n0 + (T.VertexCount - T.InputVertexCount);
                Array.Resize(ref toVertex, N);
                boundaryIndex = new bool[N];
                Func<int, int> index = (i) => T.IsInserted(i) ? n0 + (i - T.InputVertexCount) : toIndex[T.VertexID(i)];
                for (int i = 0; i < T.VertexCount; ++i) {
                    if (T.IsInserted(i)) toVertex[index(i)] = -1;
                    boundaryIndex[index(i)] = T.IsBoundaryVertex(i);
                }
                for (int t = 0; t < T.TriangleCount; ++t) {
                    Index3i ti = T.GetTriangleIndices(t);
                    Vector2d q0, q1, q2;
                    T.GetTriangleLayout(t, out q0, out q1, out q2);
                    // cotangent at corner k: opposite halfedge k+1
                    add_triangle(new Index3i(index(ti.a), index(ti.b), index(ti.c)),
                        new Vector3d(T.OppositeCotan(3 * t + 1), T.OppositeCotan(3 * t + 2), T.OppositeCotan(3 * t)),
                        new Vector3d(q1.x, q2.x, q2.y), T.TriangleArea(t), area);
                }
            } else {
                boundaryIndex = new bool[N];
                foreach (int vid in Mesh.VertexIndices())
                    if (Mesh.IsBoundaryVertex(vid)) boundaryIndex[toIndex[vid]] = true;
                foreach (int tid in Mesh.TriangleIndices()) {
                    Index3i t = Mesh.GetTriangle(tid);
                    Vector3d p0 = Mesh.GetVertex(t.a), p1 = Mesh.GetVertex(t.b), p2 = Mesh.GetVertex(t.c);
                    double l0 = (p1 - p0).Length, l1 = (p2 - p1).Length, l2 = (p0 - p2).Length;
                    double A = IntrinsicTriangulation.area_from_lengths(l0, l1, l2);
                    if (!(A > 1e-14 * (l0 * l0 + l2 * l2)))
                        continue;
                    double x = (l0 * l0 + l2 * l2 - l1 * l1) / (2 * l0);
                    add_triangle(new Index3i(toIndex[t.a], toIndex[t.b], toIndex[t.c]),
                        new Vector3d(IntrinsicTriangulation.cotan_from_lengths(l1, l0, l2),
                                     IntrinsicTriangulation.cotan_from_lengths(l2, l0, l1),
                                     IntrinsicTriangulation.cotan_from_lengths(l0, l1, l2)),
                        new Vector3d(l0, x, 2 * A / l0), A, area);
                }
            }

            // cotan Laplacian (Poisson) and the heat flow weights: per vertex pair, summed over both triangles
            // (and parallel intrinsic edges), negative sums (boundary edges opposite obtuse angles, or the mesh
            // triangles) set to 0 so that the heat system is an M-matrix
            mass = new double[N];
            var weights = new Dictionary<long, double>();
            for (int k = 0; k < tris.Count; ++k) {
                Index3i ti = tris[k];
                for (int a = 0; a < 3; ++a) {
                    int i = ti[a], j = ti[(a + 1) % 3];
                    mass[i] += area[k] / 3;
                    if (i == j) continue;
                    long key = (long)Math.Min(i, j) * N + Math.Max(i, j);
                    double w = 0.5 * cot[k][(a + 2) % 3], cur;     // the corner opposite edge (i, j)
                    weights[key] = weights.TryGetValue(key, out cur) ? cur + w : w;
                }
            }
            var M = new SymmetricSparseMatrix(N);
            double[] diag = new double[N];
            heatEdges = new List<KeyValuePair<Index2i, double>>(weights.Count);
            foreach (var kv in weights) {
                int i = (int)(kv.Key / N), j = (int)(kv.Key % N);
                M[i, j] = -kv.Value;
                diag[i] += kv.Value; diag[j] += kv.Value;
                if (kv.Value > 0) heatEdges.Add(new KeyValuePair<Index2i, double>(new Index2i(i, j), kv.Value));
            }
            for (int i = 0; i < N; ++i) M[i, i] = diag[i];
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
            var reps = new List<int>();
            for (int i = 0; i < N; ++i) {
                if (used[i] == false) { component[i] = -1; continue; }
                int r = find(i), c;
                if (ids.TryGetValue(r, out c) == false) { c = ids.Count; ids[r] = c; reps.Add(i); }
                component[i] = c;
            }
            componentCount = ids.Count;
            representative = reps.ToArray();
            timestamp = Mesh.ShapeTimestamp;
            builtSettings = built_settings();
        }

        void add_triangle(Index3i ti, Vector3d c, Vector3d q, double A, List<double> area)
        {
            if (!(A > 0)) return;
            tris.Add(ti); cot.Add(c); layout.Add(q); area.Add(A);
        }

        // (mass + t * heat weights) with the rows of the fixed vertices (and of vertices without triangles)
        // replaced by the identity
        SparseCholesky heat_factor(double t, bool[] fixedZero)
        {
            var e = new List<KeyValuePair<Index2i, double>>(N + 3 * heatEdges.Count);
            for (int i = 0; i < N; ++i)
                e.Add(entry(i, i, (fixedZero != null && fixedZero[i]) || !(mass[i] > 0) ? 1 : mass[i]));
            foreach (var kv in heatEdges) {
                int i = kv.Key.a, j = kv.Key.b;
                bool fi = fixedZero != null && fixedZero[i], fj = fixedZero != null && fixedZero[j];
                double w = t * kv.Value;
                if (fi == false) e.Add(entry(i, i, w));
                if (fj == false) e.Add(entry(j, j, w));
                if (fi == false && fj == false) e.Add(entry(i, j, -w));
            }
            return new SparseCholesky(N, e);
        }

        // cotan Laplacian with the rows of the fixed vertices (and of vertices without triangles) replaced by the identity
        SparseCholesky poisson_factor(bool[] fixedV)
        {
            var e = new List<KeyValuePair<Index2i, double>>();
            for (int r = 0; r < N; ++r) {
                bool fr = fixedV[r] || component[r] < 0;
                if (fr) { e.Add(entry(r, r, 1)); continue; }
                for (int k = L.Rows[r].Length - 1; k >= 0; --k) {
                    int c = L.Rows[r][k].j;
                    if (c < r || (c != r && (fixedV[c] || component[c] < 0))) continue;
                    e.Add(entry(r, c, L.Rows[r][k].d));
                }
            }
            return new SparseCholesky(N, e);
        }

        static KeyValuePair<Index2i, double> entry(int i, int j, double v)
        {
            return new KeyValuePair<Index2i, double>(new Index2i(i, j), v);
        }

        // log of the heat (M + t L) u = sources, from a solve, or in the log domain where it underflows
        bool log_heat(SparseCholesky A, List<int> src, bool[] hasSource, double[] logU)
        {
            if (A.IsValid == false) return false;
            double[] b = new double[N];
            foreach (int s in src) b[s] = 1;
            double[] u = new double[N];
            A.Solve(b, u);
            bool underflow = false;
            for (int i = 0; i < N && underflow == false; ++i)
                if (component[i] >= 0 && hasSource[component[i]] && !(u[i] > 1e-280)) underflow = true;
            if (underflow == false) {
                for (int i = 0; i < N; ++i) logU[i] = (u[i] > 0) ? Math.Log(u[i]) : double.NegativeInfinity;
                return true;
            }
            for (int i = 0; i < N; ++i) b[i] = double.NegativeInfinity;
            foreach (int s in src) b[s] = 0;
            return A.SolveLog(b, logU);
        }

        static double log_add(double a, double b)
        {
            double m = Math.Max(a, b);
            if (double.IsNegativeInfinity(m)) return m;
            return m + Math.Log(Math.Exp(a - m) + Math.Exp(b - m));
        }


        /// <summary>
        /// Compute distances from the given source vertex IDs (to the nearest of them) into Distance.
        /// Invalid and duplicate IDs are ignored. Returns false (and Distance is null) if there is no valid
        /// source or the solver fails; otherwise true.
        /// </summary>
        public bool Compute(IEnumerable<int> sources)
        {
            if (L == null || timestamp != Mesh.ShapeTimestamp || built_settings() != builtSettings)
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

            // 1. heat flow: (M + t L) u = delta, with non-negative weights an M-matrix: the Cholesky
            // substitutions only add terms of one sign, so every entry of u is accurate relative to itself,
            // however small (u decays like exp(-distance / sqrt(t)), eg 1e-30 at 70 edge lengths). Where u
            // underflows doubles (beyond about 700 edge lengths) the same substitutions run on log(u).
            // On meshes with a boundary, the average of the Neumann (insulated) and Dirichlet (u = 0)
            // solutions avoids the bending of the distance along the boundary (Crane et al.).
            double t = TimeFactor * meanEdge * meanEdge;
            if (t != heatTime) {
                heatN = heat_factor(t, null);
                heatD = null;
                heatTime = t;
            }
            double[] u = new double[N];
            if (log_heat(heatN, src, hasSource, u) == false)
                return false;
            bool open = false;
            for (int i = 0; i < N && open == false; ++i) open = boundaryIndex[i];
            if (open) {
                // Dirichlet: u = 0 on the boundary, except at sources on the boundary
                bool srcOnBoundary = false;
                foreach (int s in src) if (boundaryIndex[s]) srcOnBoundary = true;
                SparseCholesky D;
                if (srcOnBoundary) {
                    bool[] fixedZero = (bool[])boundaryIndex.Clone();
                    foreach (int s in src) fixedZero[s] = false;
                    D = heat_factor(t, fixedZero);
                } else {
                    if (heatD == null) heatD = heat_factor(t, boundaryIndex);
                    D = heatD;
                }
                double[] ud = new double[N];
                if (log_heat(D, src, hasSource, ud) == false)
                    return false;
                for (int i = 0; i < N; ++i) u[i] = log_add(u[i], ud[i]) - Math.Log(2);
            }

            // 2. normalized gradient X = -grad u / |grad u| per triangle, 3. its divergence per vertex
            double[] div = new double[N];
            for (int k = 0; k < tris.Count; ++k) {
                Index3i ti = tris[k];
                // the triangle laid out in the plane (intrinsic: its edges need not be straight in 3D)
                Vector3d q = layout[k];
                Vector3d p0 = Vector3d.Zero, p1 = new Vector3d(q.x, 0, 0), p2 = new Vector3d(q.y, q.z, 0);
                Vector3d n = Vector3d.AxisZ;
                double area2 = q.x * q.z;
                // grad u = 1/(2A) sum_i u_i (N x e_i), e_i the edge opposite corner i (counter-clockwise);
                // only its direction is used, so the heat is scaled by its maximum in the triangle
                double lm = Math.Max(u[ti.a], Math.Max(u[ti.b], u[ti.c]));
                if (double.IsNegativeInfinity(lm)) continue;
                double ua = Math.Exp(u[ti.a] - lm), ub = Math.Exp(u[ti.b] - lm), uc = Math.Exp(u[ti.c] - lm);
                Vector3d g = (ua * n.Cross(p2 - p1) + ub * n.Cross(p0 - p2) + uc * n.Cross(p1 - p0)) / area2;
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

            // 4. Poisson: L phi = -div (L is minus the Laplacian). With several sources (eg a crest line)
            // phi = 0 at all of them (Dirichlet): otherwise phi differs between them, and shifting it by the
            // minimum puts the other sources (and their surroundings) metres too far away. A single source is
            // not pinned (Neumann, phi shifted to 0 there): pinning the source concentrates the discretization
            // error of the divergence there and gives an error growing with the distance (7% at 500 m on a
            // 1 m grid, below the straight distance). The Neumann problem is solved exactly: the divergence
            // sums to 0 on each component (up to rounding, removed per component), so fixing one arbitrary
            // vertex per component only picks the additive constant.
            bool pin = src.Count > 1;
            bool[] isSrc = new bool[N];
            if (pin)
                foreach (int s in src) isSrc[s] = true;
            double[] phi = new double[N], rhs = new double[N];
            double[] csum = new double[componentCount]; int[] ccount = new int[componentCount];
            if (pin == false)
                for (int i = 0; i < N; ++i)
                    if (component[i] >= 0) { csum[component[i]] += div[i]; ccount[component[i]]++; }
            for (int i = 0; i < N; ++i)
                if (component[i] >= 0 && hasSource[component[i]] && isSrc[i] == false)
                    rhs[i] = -(div[i] - csum[component[i]] / Math.Max(1, ccount[component[i]]));
            SparseCholesky P;
            if (pin) {
                bool[] fixedV = (bool[])isSrc.Clone();
                bool[] compFixed = new bool[componentCount];
                foreach (int s in src) compFixed[component[s]] = true;
                for (int c = 0; c < componentCount; ++c)
                    if (compFixed[c] == false) fixedV[representative[c]] = true;
                P = poisson_factor(fixedV);
            } else {
                if (poissonN == null) {
                    bool[] fixedV = new bool[N];
                    foreach (int r in representative) fixedV[r] = true;
                    poissonN = poisson_factor(fixedV);
                }
                P = poissonN;
                foreach (int r in representative) rhs[r] = 0;
            }
            if (P.IsValid == false)
                return false;
            P.Solve(rhs, phi);

            // distances relative to the sources of each component (0 at all of them when pinned)
            double[] minSrc = new double[componentCount];
            for (int c = 0; c < componentCount; ++c) minSrc[c] = double.MaxValue;
            foreach (int s in src) minSrc[component[s]] = Math.Min(minSrc[component[s]], phi[s]);
            Distance = new double[Mesh.MaxVertexID];
            for (int k = 0; k < Distance.Length; ++k)
                Distance[k] = double.NaN;
            for (int i = 0; i < N; ++i)
                if (component[i] >= 0 && hasSource[component[i]] && toVertex[i] >= 0)      // (not inserted vertices)
                    Distance[toVertex[i]] = Math.Max(0, phi[i] - minSrc[component[i]]);
            foreach (int s in srcVertices) Distance[s] = 0;
            return true;
        }

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

    }
}
