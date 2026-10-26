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
    /// Computed on the intrinsic Delaunay triangulation of the mesh (IntrinsicDelaunay; Sharp and Crane,
    /// "A Laplacian for Nonmanifold Triangle Meshes", 2020): caps and needles, eg along constrained
    /// feature lines after remeshing, would otherwise make the solves stall.
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


        int N;
        int[] toIndex, toVertex;
        PackedSparseMatrix L;      // cotan Laplacian, positive semidefinite
        double[] mass, diagL;
        double meanEdge;
        List<Index3i> tris;         // the (intrinsic) triangles, by vertex index
        List<Vector3d> cot;         // cotangent at each corner of tris
        List<Vector3d> layout;      // tris in the plane: corners (0,0), (x,0), (y,z)

        int timestamp = -1;
        bool[] boundaryIndex;       // per index: on the boundary (mesh vertices and inserted vertices)
        int[] component;            // per index; -1 for vertices without a (non-degenerate) triangle
        int componentCount;

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
            timestamp = Mesh.ShapeTimestamp;
            builtSettings = built_settings();
            nbStart = null; spatial = null;
            toIndex = new int[Mesh.MaxVertexID];
            toVertex = new int[Mesh.VertexCount];
            N = 0;
            foreach (int vid in Mesh.VertexIndices()) {
                toIndex[vid] = N;
                toVertex[N++] = vid;
            }
            Vector3d center = Mesh.CachedBounds.Center;
            Vector3d[] pos = new Vector3d[N];   // (N grows with the refinement below)
            for (int i = 0; i < N; ++i)
                pos[i] = Mesh.GetVertex(toVertex[i]) - center;

            // the triangles by their edge lengths (edge k from corner k to k+1), made intrinsic Delaunay
            tris = new List<Index3i>();
            var lengths = new List<Vector3d>();
            double edgeSum = 0; int edgeCount = 0;
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i t = Mesh.GetTriangle(tid);
                Index3i ti = new Index3i(toIndex[t.a], toIndex[t.b], toIndex[t.c]);
                Vector3d p0 = pos[ti.a], p1 = pos[ti.b], p2 = pos[ti.c];
                double area = 0.5 * (p1 - p0).Cross(p2 - p0).Length;
                if (area < 1e-14 * ((p1 - p0).LengthSquared + (p2 - p0).LengthSquared))
                    continue;
                tris.Add(ti);
                lengths.Add(new Vector3d((p1 - p0).Length, (p2 - p1).Length, (p0 - p2).Length));
                edgeSum += (p1 - p0).Length + (p2 - p1).Length + (p0 - p2).Length;
                edgeCount += 3;
            }
            boundaryIndex = new bool[N];
            foreach (int vid in Mesh.VertexIndices())
                if (Mesh.IsBoundaryVertex(vid)) boundaryIndex[toIndex[vid]] = true;
            Refinement = null;
            if (IntrinsicDelaunay && IntrinsicRefine && refine(tris, lengths)) {
                IntrinsicFlips = Refinement.Flips;
            } else
                IntrinsicFlips = IntrinsicDelaunay ? flip_to_delaunay(tris, lengths) : 0;

            SymmetricSparseMatrix M = new SymmetricSparseMatrix();
            mass = new double[N];
            diagL = new double[N];
            cot = new List<Vector3d>(tris.Count);
            layout = new List<Vector3d>(tris.Count);
            int keep = 0;
            for (int k = 0; k < tris.Count; ++k) {
                Index3i ti = tris[k];
                Vector3d l = lengths[k];
                double area = heron(l.x, l.y, l.z);
                if (!(area > 1e-14 * (l.x * l.x + l.z * l.z)))
                    continue;
                // cotangent at corner a from the lengths of its two edges and the opposite one
                Vector3d c = Vector3d.Zero;
                for (int a = 0; a < 3; ++a) {
                    double e0 = l[a], e1 = l[(a + 2) % 3], opp = l[(a + 1) % 3];
                    c[a] = (e0 * e0 + e1 * e1 - opp * opp) / (4 * area);
                }
                tris[keep++] = ti;
                cot.Add(c);
                // corners in the plane: (0,0), (l0,0), (x,y), counter-clockwise
                double x = (l.x * l.x + l.z * l.z - l.y * l.y) / (2 * l.x);
                layout.Add(new Vector3d(l.x, x, 2 * area / l.x));
                for (int a = 0; a < 3; ++a) {
                    int i = ti[a], j = ti[(a + 1) % 3];
                    double w = 0.5 * c[(a + 2) % 3];     // the corner opposite edge (i, j)
                    M.Set(i, j, M[i, j] - w);
                    diagL[i] += w; diagL[j] += w;
                    mass[ti[a]] += area / 3;
                }
            }
            tris.RemoveRange(keep, tris.Count - keep);
            for (int i = 0; i < N; ++i)
                M.Set(i, i, diagL[i]);
            L = new PackedSparseMatrix(M);
            meanEdge = (edgeCount > 0) ? edgeSum / edgeCount : 1;

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

        // Replaces tris and lengths by the refined intrinsic triangulation (IntrinsicTriangulation.DelaunayRefine);
        // inserted vertices get the indices from N on. False (nothing changed) for non-manifold meshes.
        bool refine(List<Index3i> tris, List<Vector3d> lengths)
        {
            IntrinsicTriangulation T;
            try {
                T = new IntrinsicTriangulation(Mesh);
            } catch (ArgumentException) {
                Refinement = new IntrinsicRefinementResult() { Status = IntrinsicRefinementStatus.NonManifoldInput };
                return false;
            }
            Refinement = T.DelaunayRefine(RefineMinAngle, RefineMaxInsertions);
            int n0 = N;
            Func<int, int> index = (i) => T.IsInserted(i) ? n0 + (i - T.InputVertexCount) : toIndex[T.VertexID(i)];
            N = n0 + (T.VertexCount - T.InputVertexCount);
            Array.Resize(ref toVertex, N);
            Array.Resize(ref boundaryIndex, N);
            for (int i = T.InputVertexCount; i < T.VertexCount; ++i) {
                toVertex[index(i)] = -1;
                boundaryIndex[index(i)] = T.IsBoundaryVertex(i);
            }
            tris.Clear(); lengths.Clear();
            for (int t = 0; t < T.TriangleCount; ++t) {
                Index3i ti = T.GetTriangleIndices(t);
                tris.Add(new Index3i(index(ti.a), index(ti.b), index(ti.c)));
                lengths.Add(new Vector3d(T.Length(3 * t), T.Length(3 * t + 1), T.Length(3 * t + 2)));
            }
            return true;
        }

        // triangle area from the edge lengths (Kahan's stable form of Heron's formula); 0 if not a triangle
        static double heron(double a, double b, double c)
        {
            if (a < b) { double t = a; a = b; b = t; }
            if (b < c) { double t = b; b = c; c = t; }
            if (a < b) { double t = a; a = b; b = t; }
            double p = (a + (b + c)) * (c - (a - b)) * (c + (a - b)) * (a + (b - c));
            return (p > 0) ? 0.25 * Math.Sqrt(p) : 0;
        }

        // cotangent of the angle opposite the side of length a in the triangle with sides a, b, c
        static double cot_opposite(double a, double b, double c)
        {
            double area = heron(a, b, c);
            return (area > 0) ? (b * b + c * c - a * a) / (4 * area) : double.NegativeInfinity;
        }

        /// <summary>
        /// Flips edges of the triangulation intrinsically (the edge lengths change, the surface does not;
        /// Bobenko and Springborn, Fisher et al. 2007) until every interior edge is Delaunay: the angles
        /// opposite it sum to at most pi. Removes the large and negative cotangent weights of caps and
        /// needles (eg vertices next to feature lines after remeshing), which make the heat and Poisson
        /// solves stall; on a Delaunay mesh nothing changes. Boundary and non-manifold edges are kept.
        /// Returns the number of flips; tris and lengths are updated (same number of triangles).
        /// </summary>
        static int flip_to_delaunay(List<Index3i> tris, List<Vector3d> lengths)
        {
            int H = 3 * tris.Count;
            int[] org = new int[H], nxt = new int[H], twin = new int[H];
            double[] len = new double[H];
            long nV = 0;
            for (int f = 0; f < tris.Count; ++f)
                for (int a = 0; a < 3; ++a) {
                    int h = 3 * f + a;
                    org[h] = tris[f][a]; nxt[h] = 3 * f + (a + 1) % 3; len[h] = lengths[f][a]; twin[h] = -1;
                    nV = Math.Max(nV, tris[f][a] + 1);
                }
            // interior edges: exactly two half edges in opposite directions. Same direction (inconsistent
            // orientation) or more than two (non-manifold): not flipped
            var first = new Dictionary<long, int>();
            var notFlippable = new HashSet<long>();
            for (int h = 0; h < H; ++h) {
                int i = org[h], j = org[nxt[h]];
                long key = Math.Min(i, j) * nV + Math.Max(i, j);
                int o;
                if (first.TryGetValue(key, out o) == false)
                    first[key] = h;
                else if (twin[o] < 0 && org[o] == j && notFlippable.Contains(key) == false) {
                    twin[h] = o; twin[o] = h;
                } else
                    notFlippable.Add(key);
            }
            foreach (long key in notFlippable) {
                int o = first[key];
                if (twin[o] >= 0) { twin[twin[o]] = -1; twin[o] = -1; }
            }
            // corner angle opposite half edge h in its triangle, as cotangent
            Func<int, double> cotOpp = (h) => cot_opposite(len[h], len[nxt[h]], len[nxt[nxt[h]]]);
            Func<double, double, double, double> angle = (a, b, opp) => Math.Acos(MathUtil.Clamp((a * a + b * b - opp * opp) / (2 * a * b), -1, 1));
            var queue = new Queue<int>();
            var queued = new bool[H];
            for (int h = 0; h < H; ++h)
                if (twin[h] > h) { queue.Enqueue(h); queued[h] = true; }
            int flips = 0, maxFlips = 50 * H + 100;
            while (queue.Count > 0 && flips < maxFlips) {
                int h = queue.Dequeue();
                queued[h] = false;
                int t = twin[h];
                if (t < 0 || cotOpp(h) + cotOpp(t) >= -1e-10)
                    continue;
                int h1 = nxt[h], h2 = nxt[h1], g1 = nxt[t], g2 = nxt[g1];
                int k = org[h2], l = org[g2];
                if (k == l)
                    continue;
                // unfold both triangles at the edge ends i = org[h] and j: the quad must be strictly convex there
                double ai = angle(len[h], len[h2], len[h1]) + angle(len[t], len[g1], len[g2]);
                double aj = angle(len[h], len[h1], len[h2]) + angle(len[t], len[g2], len[g1]);
                if (ai >= Math.PI - 1e-12 || aj >= Math.PI - 1e-12)
                    continue;
                double kl2 = len[h2] * len[h2] + len[g1] * len[g1] - 2 * len[h2] * len[g1] * Math.Cos(ai);
                if (!(kl2 > 0))
                    continue;
                // triangles (i, j, k) + (j, i, l) -> (l, k, i) + (k, l, j)
                org[h] = l; nxt[h] = h2; nxt[h2] = g1; nxt[g1] = h;
                org[t] = k; nxt[t] = g2; nxt[g2] = h1; nxt[h1] = t;
                len[h] = len[t] = Math.Sqrt(kl2);
                flips++;
                foreach (int e in new[] { h1, h2, g1, g2 }) {
                    int c = (twin[e] >= 0 && twin[e] < e) ? twin[e] : e;
                    if (twin[c] >= 0 && queued[c] == false) { queue.Enqueue(c); queued[c] = true; }
                }
            }
            if (flips == 0)
                return 0;
            // read the triangles back (each triangle is a cycle of three half edges)
            var seen = new bool[H];
            int nf = 0;
            for (int h = 0; h < H; ++h) {
                if (seen[h]) continue;
                int b = nxt[h], c = nxt[b];
                seen[h] = seen[b] = seen[c] = true;
                tris[nf] = new Index3i(org[h], org[b], org[c]);
                lengths[nf] = new Vector3d(len[h], len[b], len[c]);
                nf++;
            }
            return flips;
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
            // not pinned (Neumann, phi shifted to 0 there): pinning one vertex concentrates the discretization
            // error of the divergence there and gives an error growing with the distance (7% at 500 m on a
            // 1 m grid, below the straight distance). The regularization keeps the system definite.
            double eps = 1e-8;
            bool pin = src.Count > 1;
            bool[] isSrc = new bool[N];
            if (pin)
                foreach (int s in src) isSrc[s] = true;
            double[] phi = new double[N], rhs = new double[N], free = new double[N];
            // (Neumann: the divergence sums to 0 on each component; the rounding error is removed per component)
            double[] csum = new double[componentCount]; int[] ccount = new int[componentCount];
            if (pin == false)
                for (int i = 0; i < N; ++i)
                    if (component[i] >= 0) { csum[component[i]] += div[i]; ccount[component[i]]++; }
            for (int i = 0; i < N; ++i)
                if (component[i] >= 0 && hasSource[component[i]] && isSrc[i] == false)
                    rhs[i] = -(div[i] - csum[component[i]] / Math.Max(1, ccount[component[i]]));
            if (solve((x, r) => {
                    for (int i = 0; i < N; ++i) free[i] = isSrc[i] ? 0 : x[i];
                    L.Multiply_Parallel(free, r);
                    for (int i = 0; i < N; ++i) r[i] = isSrc[i] ? x[i] : r[i] + eps * mass[i] * x[i];
                },
                (i) => isSrc[i] ? 1 : diagL[i] + eps * mass[i], rhs, phi) == false)
                return false;

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
                for (int i = 0; i < N; ++i) fixedZero[i] = boundaryIndex[i];
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
                for (int i = 0; i < N; ++i) fixedZero[i] = boundaryIndex[i];
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
