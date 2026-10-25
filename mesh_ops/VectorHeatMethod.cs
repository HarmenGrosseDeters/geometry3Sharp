// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The Vector Heat Method (Sharp, Soliman and Crane, "The Vector Heat Method", ACM TOG 2019): parallel
    /// transport of tangent vectors along shortest geodesics, extension of scalar values to the nearest
    /// source, and the logarithmic map (geodesic polar coordinates around a vertex), each by a few
    /// prefactored sparse linear solves of short-time heat flow.
    ///
    /// Computed on the intrinsic Delaunay triangulation of the mesh (IntrinsicTriangulation; can be turned
    /// off with IntrinsicDelaunay = false): its cotan weights are non-negative, so the scalar heat is an
    /// M-matrix solve that is accurate however small the heat gets, and poor input triangles (slivers,
    /// obtuse angles) do not spoil the result.
    ///
    /// Tangent vectors are Vector2d in the tangent space of each vertex, in the signpost coordinates of
    /// IntrinsicTriangulation (angle 0 along the vertex' first mesh edge, angles scaled to sum to 2 pi at
    /// interior vertices);
    /// Triangulation.ToWorld / FromWorld convert to and from 3D vectors. Results are arrays indexed by
    /// mesh vertex id, NaN for vertices without a source in their connected component (and for vertices
    /// without triangles).
    ///
    /// The log map is a robust replacement for MeshLocalParam's discrete exponential map, which
    /// accumulates error along Dijkstra paths. Its length is the heat method distance r on the intrinsic
    /// triangulation. Its direction: by the Gauss lemma the gradient of r^2 / 2 at a vertex is the log map
    /// vector parallel transported along the geodesic, so measured against the transported reference
    /// vector of the source (vector heat) it gives the angle at the source. (Diffusing outward vectors
    /// from the one-ring of the source instead is biased by the shape of the one-ring, by some 20 degrees
    /// on a mesh whose vertices have six neighbours, independently of the resolution.) r^2 is smooth at
    /// the source, so the direction is well defined up to the cut locus. Like the heat method distance,
    /// it is less accurate within a few edge lengths of boundary corners.
    ///
    /// Heat decays by about a factor 2.6 per edge length; beyond ~600 edge lengths from a source it
    /// would underflow, and then the diffusion time is increased automatically (DiffusionTime).
    /// </summary>
    public class VectorHeatMethod
    {
        public DMesh3 Mesh { get; private set; }

        /// <summary> diffusion time = TimeFactor * (mean edge length)^2 </summary>
        public double TimeFactor = 1.0;

        /// <summary> work on the intrinsic Delaunay triangulation (true) or on the mesh triangles </summary>
        public bool IntrinsicDelaunay = true;

        /// <summary>
        /// also refine the intrinsic Delaunay triangulation (IntrinsicTriangulation.DelaunayRefine with
        /// RefineMinAngle, RefineMaxInsertions): no negative cotan weights at boundary edges opposite obtuse
        /// angles (otherwise clamped to 0 in the heat operators), and a minimum angle. Results are still
        /// reported at the mesh vertices.
        /// </summary>
        public bool IntrinsicRefine = false;
        /// <summary> minimum angle (degrees, at most 30) of the refinement; 0 only splits boundary edges opposite obtuse angles </summary>
        public double RefineMinAngle = 25;
        /// <summary> insertion limit of the refinement (0: 10 * vertices + 1000) </summary>
        public int RefineMaxInsertions = 0;
        /// <summary> result of the refinement; null if not refined </summary>
        public IntrinsicRefinementResult Refinement { get { check(); return refinement; } }
        IntrinsicRefinementResult refinement;

        /// <summary> the triangulation the operators are built on (built on first use) </summary>
        public IntrinsicTriangulation Triangulation { get { check(); return triangulation; } }
        IntrinsicTriangulation triangulation;

        /// <summary> diffusion time of the last computation </summary>
        public double DiffusionTime { get; private set; }


        public VectorHeatMethod(DMesh3 mesh)
        {
            Mesh = mesh;
        }


        int N;
        double[] mass;
        int[] component;
        int componentCount;
        int[] pinned;               // per component: vertex fixed to 0 in the Poisson problem
        SparseCholesky scalarHeat, vectorHeat, poisson;
        int timestamp = -1;
        bool builtIntrinsic, builtRefine;
        double builtRefineAngle;
        int builtRefineMax;
        double builtTimeFactor;
        double timeScale = 1;       // growth of the diffusion time against underflow

        void precompute()
        {
            timestamp = Mesh.ShapeTimestamp;
            builtIntrinsic = IntrinsicDelaunay;
            builtRefine = IntrinsicRefine; builtRefineAngle = RefineMinAngle; builtRefineMax = RefineMaxInsertions;
            builtTimeFactor = TimeFactor;
            timeScale = 1;
            triangulation = new IntrinsicTriangulation(Mesh);
            refinement = null;
            if (IntrinsicDelaunay && IntrinsicRefine)
                refinement = triangulation.DelaunayRefine(RefineMinAngle, RefineMaxInsertions);
            else if (IntrinsicDelaunay)
                triangulation.FlipToDelaunay();
            IntrinsicTriangulation T = triangulation;
            N = T.VertexCount;
            mass = T.VertexAreas();

            // connected components
            int[] parent = new int[N];
            for (int i = 0; i < N; ++i) parent[i] = i;
            Func<int, int> find = (i) => { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; };
            for (int h = 0; h < T.HalfedgeCount; ++h)
                parent[find(T.Tail(h))] = find(T.Head(h));
            component = new int[N];
            var ids = new Dictionary<int, int>();
            var pin = new List<int>();
            for (int i = 0; i < N; ++i) {
                int r = find(i), c;
                if (ids.TryGetValue(r, out c) == false) { c = ids.Count; ids[r] = c; pin.Add(i); }
                component[i] = c;
            }
            componentCount = ids.Count;
            pinned = pin.ToArray();
            isBoundary = new bool[N];
            hasBoundary = false;
            for (int i = 0; i < N; ++i)
                if (T.IsBoundaryVertex(i)) { isBoundary[i] = true; hasBoundary = true; }
            dirichletHeat = null;

            // Poisson problem: cotan Laplacian with one vertex per component fixed
            bool[] isPinned = new bool[N];
            foreach (int p in pinned) isPinned[p] = true;
            var entries = new List<KeyValuePair<Index2i, double>>();
            for (int h = 0; h < T.HalfedgeCount; ++h) {
                int i = T.Tail(h), j = T.Head(h);
                if (i == j) continue;
                double c = 0.5 * T.OppositeCotan(h);
                if (isPinned[i] == false) entries.Add(entry(i, i, c));
                if (isPinned[j] == false) entries.Add(entry(j, j, c));
                if (isPinned[i] == false && isPinned[j] == false) entries.Add(entry(i, j, -c));
            }
            foreach (int p in pinned) entries.Add(entry(p, p, 1));
            poisson = new SparseCholesky(N, entries);
            factor_heat();
        }

        static KeyValuePair<Index2i, double> entry(int i, int j, double v)
        {
            return new KeyValuePair<Index2i, double>(new Index2i(i, j), v);
        }

        void factor_heat()
        {
            IntrinsicTriangulation T = triangulation;
            double h0 = T.MeanEdgeLength;
            double t = TimeFactor * timeScale * h0 * h0;
            DiffusionTime = t;
            var s = new List<KeyValuePair<Index2i, double>>();
            var v = new List<KeyValuePair<Index2i, double>>();
            for (int i = 0; i < N; ++i) {
                s.Add(entry(i, i, mass[i]));
                v.Add(entry(2 * i, 2 * i, mass[i]));
                v.Add(entry(2 * i + 1, 2 * i + 1, mass[i]));
            }
            for (int h = 0; h < T.HalfedgeCount; ++h) {
                if (T.Twin(h) >= 0 && T.Twin(h) < h) continue;      // once per edge
                int i = T.Tail(h), j = T.Head(h);
                double c = t * heat_weight(h);
                double rho = transport_angle(h);
                double cr = Math.Cos(rho), sr = Math.Sin(rho);
                if (i == j) {
                    // a loop edge: c |x_i - r x_i|^2 = 2c (1 - cos rho) |x_i|^2
                    v.Add(entry(2 * i, 2 * i, 2 * c * (1 - cr)));
                    v.Add(entry(2 * i + 1, 2 * i + 1, 2 * c * (1 - cr)));
                    continue;
                }
                s.Add(entry(i, i, c)); s.Add(entry(j, j, c)); s.Add(entry(i, j, -c));
                // Hermitian connection Laplacian: (i, j) = -c r_ji, r_ji = exp(i rho) transports from j to i;
                // as a real 2x2 block [[a, -b], [b, a]] of the complex entry a + ib
                double a = -c * cr, b = -c * sr;
                v.Add(entry(2 * i, 2 * i, c)); v.Add(entry(2 * i + 1, 2 * i + 1, c));
                v.Add(entry(2 * j, 2 * j, c)); v.Add(entry(2 * j + 1, 2 * j + 1, c));
                v.Add(entry(2 * i, 2 * j, a));
                v.Add(entry(2 * i, 2 * j + 1, -b));
                v.Add(entry(2 * i + 1, 2 * j, b));
                v.Add(entry(2 * i + 1, 2 * j + 1, a));
            }
            scalarHeat = new SparseCholesky(N, s);
            vectorHeat = new SparseCholesky(2 * N, v);
        }

        // Edge weight of the heat operators: the cotan weight, but at least 0. After the Delaunay flips only
        // boundary edges opposite an obtuse angle can have negative weights (they cannot be flipped);
        // clamping them keeps the heat operators M-matrices and the connection Laplacian positive
        // semidefinite. The Poisson problem uses the exact cotan Laplacian.
        double heat_weight(int h)
        {
            return Math.Max(0, triangulation.CotanWeight(h));
        }

        // rotation angle that transports a tangent vector from the head of h to its tail along the edge:
        // the angle relative to the edge is kept, and the edge direction at the head (pointing to the tail)
        // continues as the opposite of the direction of h at the tail
        double transport_angle(int h)
        {
            return triangulation.Signpost(h) - triangulation.ReverseSignpost(h) + Math.PI;
        }

        void check()
        {
            if (triangulation == null || timestamp != Mesh.ShapeTimestamp || builtIntrinsic != IntrinsicDelaunay
                || builtTimeFactor != TimeFactor || builtRefine != IntrinsicRefine
                || (IntrinsicRefine && (builtRefineAngle != RefineMinAngle || builtRefineMax != RefineMaxInsertions)))
                precompute();
        }

        int index_of(int vid)
        {
            if (vid < 0 || vid >= Mesh.MaxVertexID || Mesh.IsVertex(vid) == false)
                throw new ArgumentException("VectorHeatMethod: " + vid + " is not a vertex of the mesh");
            return triangulation.VertexIndex(vid);
        }

        // scalar heat of the indicator of the sources; increases the diffusion time while it underflows
        double[] indicator_heat(List<int> src, bool[] hasSource)
        {
            while (true) {
                double[] b = new double[N];
                foreach (int s in src) b[s] = 1;
                double[] phi = new double[N];
                scalarHeat.Solve(b, phi);
                bool underflow = false;
                for (int i = 0; i < N && underflow == false; ++i)
                    if (hasSource[component[i]] && !(phi[i] > 1e-250)) underflow = true;
                if (underflow == false || timeScale > 1e12)
                    return phi;
                timeScale *= 4;
                factor_heat();
            }
        }

        bool[] source_components(List<int> src)
        {
            bool[] has = new bool[componentCount];
            foreach (int s in src) has[component[s]] = true;
            return has;
        }

        T[] per_vertex<T>(Func<int, T> value, T invalid, bool[] hasSource)
        {
            T[] r = new T[Mesh.MaxVertexID];
            for (int k = 0; k < r.Length; ++k) r[k] = invalid;
            for (int i = 0; i < N; ++i)
                if (hasSource[component[i]] && triangulation.IsInserted(i) == false) r[triangulation.VertexID(i)] = value(i);
            return r;
        }


        /// <summary>
        /// Extends values given at source vertices to every vertex: each vertex gets the value of the
        /// geodesically nearest source (smoothly blended near equidistant points).
        /// </summary>
        public double[] ExtendScalar(IEnumerable<KeyValuePair<int, double>> sources)
        {
            check();
            var src = new List<int>();
            double[] b = new double[N];
            foreach (var kv in sources) {
                int i = index_of(kv.Key);
                if (i < 0) continue;
                src.Add(i);
                b[i] += kv.Value;
            }
            if (src.Count == 0) throw new ArgumentException("VectorHeatMethod.ExtendScalar: no source vertex with triangles");
            bool[] has = source_components(src);
            double[] phi = indicator_heat(src, has);
            double[] u = new double[N];
            scalarHeat.Solve(b, u);
            return per_vertex(i => u[i] / phi[i], double.NaN, has);
        }


        /// <summary>
        /// Parallel transport of tangent vectors at source vertices to every vertex, along the shortest
        /// geodesic to the nearest source. Result in each vertex' tangent coordinates (see class comment).
        /// </summary>
        public Vector2d[] TransportVectors(IEnumerable<KeyValuePair<int, Vector2d>> sources)
        {
            check();
            var src = new List<int>();
            double[] b = new double[2 * N], bm = new double[N];
            foreach (var kv in sources) {
                int i = index_of(kv.Key);
                if (i < 0) continue;
                src.Add(i);
                b[2 * i] += kv.Value.x; b[2 * i + 1] += kv.Value.y;
                bm[i] += kv.Value.Length;
            }
            if (src.Count == 0) throw new ArgumentException("VectorHeatMethod.TransportVectors: no source vertex with triangles");
            bool[] has = source_components(src);
            double[] phi = indicator_heat(src, has);
            double[] u = new double[N];
            scalarHeat.Solve(bm, u);
            double[] y = new double[2 * N];
            vectorHeat.Solve(b, y);
            Vector2d nan = new Vector2d(double.NaN, double.NaN);
            return per_vertex(i => {
                double l = Math.Sqrt(y[2 * i] * y[2 * i] + y[2 * i + 1] * y[2 * i + 1]);
                if (!(l > 0)) return nan;
                return (u[i] / phi[i] / l) * new Vector2d(y[2 * i], y[2 * i + 1]);
            }, nan, has);
        }

        /// <summary> parallel transport of the tangent vector v at a source vertex to every vertex </summary>
        public Vector2d[] TransportVector(int sourceVid, Vector2d v)
        {
            return TransportVectors(new KeyValuePair<int, Vector2d>[] { new KeyValuePair<int, Vector2d>(sourceVid, v) });
        }


        /// <summary>
        /// Logarithmic map around a vertex: for every vertex the tangent vector at the source whose
        /// geodesic (exponential map) reaches it; its length is the geodesic distance. In the source's
        /// tangent coordinates (Triangulation.ToWorld(sourceVid, v) gives the 3D direction).
        /// </summary>
        public Vector2d[] ComputeLogMap(int sourceVid)
        {
            check();
            IntrinsicTriangulation T = triangulation;
            int x = index_of(sourceVid);
            if (x < 0) throw new ArgumentException("VectorHeatMethod.ComputeLogMap: the source vertex has no triangles");
            var src = new List<int>() { x };
            bool[] has = source_components(src);
            double[] heat = indicator_heat(src, has);

            // reference field: the transported unit vector of angle 0 at the source
            double[] b = new double[2 * N];
            b[2 * x] = 1;
            double[] R = new double[2 * N];
            vectorHeat.Solve(b, R);

            // geodesic distance r (heat method), and per vertex the gradient of r^2 / 2: by the Gauss
            // lemma it is the log map vector transported along the geodesic, so relative to the
            // transported reference it gives the log map in the source's tangent space. r^2 is smooth at
            // the source, unlike r.
            double[] r = heat_distance(x, heat, has);
            double[] gx = new double[N], gy = new double[N], gw = new double[N];
            Vector2d[] q = new Vector2d[3];
            for (int t = 0; t < T.TriangleCount; ++t) {
                T.GetTriangleLayout(t, out q[0], out q[1], out q[2]);
                Index3i tv = T.GetTriangleIndices(t);
                double area2 = (q[1].x - q[0].x) * (q[2].y - q[0].y) - (q[2].x - q[0].x) * (q[1].y - q[0].y);
                if (!(area2 > 0)) continue;
                Vector2d g = Vector2d.Zero;
                for (int k = 0; k < 3; ++k) {
                    Vector2d e = q[(k + 2) % 3] - q[(k + 1) % 3];
                    g += (0.5 * r[tv[k]] * r[tv[k]]) * new Vector2d(-e.y, e.x);
                }
                g /= area2;
                for (int k = 0; k < 3; ++k) {
                    int h = 3 * t + k;
                    // triangle to vertex: rotate back by the direction of the corner's edge in the layout
                    // minus its signpost
                    Vector2d e = q[(k + 1) % 3] - q[k];
                    double rot = T.Signpost(h) - Math.Atan2(e.y, e.x);
                    double cr = Math.Cos(rot), sr = Math.Sin(rot), w = T.CornerAngle(h);
                    int i = tv[k];
                    gx[i] += w * (cr * g.x - sr * g.y);
                    gy[i] += w * (sr * g.x + cr * g.y);
                    gw[i] += w;
                }
            }

            Vector2d nan = new Vector2d(double.NaN, double.NaN);
            Vector2d[] log = new Vector2d[N];
            for (int i = 0; i < N; ++i) {
                if (has[component[i]] == false) continue;
                double rl = Math.Sqrt(R[2 * i] * R[2 * i] + R[2 * i + 1] * R[2 * i + 1]);
                Vector2d G = new Vector2d(gx[i], gy[i]);
                double gl = G.Length;
                if (!(rl > 0) || !(gl > 0) || i == x) { log[i] = Vector2d.Zero; continue; }
                // G * conj(R / |R|), scaled to length r
                double cx = (G.x * R[2 * i] + G.y * R[2 * i + 1]) / rl, cy = (G.y * R[2 * i] - G.x * R[2 * i + 1]) / rl;
                log[i] = (r[i] / gl) * new Vector2d(cx, cy);
            }
            // the neighbours of the source: exactly along their (shortest) edge
            var shortest = new Dictionary<int, double>();
            int h0 = T.VertexHalfedge(x), hh = h0, guard = 0;
            do {
                int j = T.Head(hh);
                double l = T.Length(hh), cur;
                if (j != x && (shortest.TryGetValue(j, out cur) == false || l < cur)) {
                    shortest[j] = l;
                    double a = T.Signpost(hh);
                    log[j] = new Vector2d(l * Math.Cos(a), l * Math.Sin(a));
                }
                hh = T.NextAroundVertex(hh);
            } while (hh >= 0 && hh != h0 && ++guard < T.HalfedgeCount);
            return per_vertex(i => log[i], nan, has);
        }


        // heat method distance from vertex index x (Crane et al. 2013) on the triangulation, given the heat
        // of the source; on meshes with a boundary the mean of the Neumann and Dirichlet heat
        double[] heat_distance(int x, double[] heat, bool[] has)
        {
            IntrinsicTriangulation T = triangulation;
            double[] u = heat;
            if (T.IsBoundaryVertex(x) == false && hasBoundary) {
                double[] b = new double[N];
                b[x] = 1;
                double[] ud = new double[N];
                dirichlet_heat().Solve(b, ud);
                u = new double[N];
                for (int i = 0; i < N; ++i) u[i] = 0.5 * (heat[i] + (isBoundary[i] ? 0 : ud[i]));
            }
            double[] rhs = new double[N];
            Vector2d[] q = new Vector2d[3];
            for (int t = 0; t < T.TriangleCount; ++t) {
                T.GetTriangleLayout(t, out q[0], out q[1], out q[2]);
                Index3i tv = T.GetTriangleIndices(t);
                double um = Math.Max(u[tv.a], Math.Max(u[tv.b], u[tv.c]));
                if (!(um > 0)) continue;
                // direction of -grad u (the heat is scaled by its maximum in the triangle)
                Vector2d g = Vector2d.Zero;
                for (int k = 0; k < 3; ++k) {
                    Vector2d e = q[(k + 2) % 3] - q[(k + 1) % 3];
                    g += (u[tv[k]] / um) * new Vector2d(-e.y, e.x);
                }
                double gl = g.Length;
                if (!(gl > 0)) continue;
                Vector2d X = -g / gl;
                for (int k = 0; k < 3; ++k) {
                    // integral of X . grad(hat function) over the triangle = 1/2 X . J (q_k+2 - q_k+1)
                    Vector2d e = q[(k + 2) % 3] - q[(k + 1) % 3];
                    rhs[tv[k]] += 0.5 * X.Dot(new Vector2d(-e.y, e.x));
                }
            }
            foreach (int p in pinned) rhs[p] = 0;
            double[] r = new double[N];
            poisson.Solve(rhs, r);
            double r0 = r[x];
            for (int i = 0; i < N; ++i) r[i] = Math.Max(0, r[i] - r0);
            return r;
        }

        SparseCholesky dirichletHeat;
        bool hasBoundary;
        bool[] isBoundary;

        SparseCholesky dirichlet_heat()
        {
            if (dirichletHeat != null && dirichletTime == DiffusionTime)
                return dirichletHeat;
            IntrinsicTriangulation T = triangulation;
            double t = DiffusionTime;
            var s = new List<KeyValuePair<Index2i, double>>();
            for (int i = 0; i < N; ++i)
                s.Add(entry(i, i, isBoundary[i] ? 1 : mass[i]));
            for (int h = 0; h < T.HalfedgeCount; ++h) {
                if (T.Twin(h) >= 0 && T.Twin(h) < h) continue;
                int i = T.Tail(h), j = T.Head(h);
                if (i == j) continue;
                double c = t * heat_weight(h);
                if (isBoundary[i] == false) s.Add(entry(i, i, c));
                if (isBoundary[j] == false) s.Add(entry(j, j, c));
                if (isBoundary[i] == false && isBoundary[j] == false) s.Add(entry(i, j, -c));
            }
            dirichletHeat = new SparseCholesky(N, s);
            dirichletTime = t;
            return dirichletHeat;
        }
        double dirichletTime;
    }
}
