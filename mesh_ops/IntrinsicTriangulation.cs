// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Intrinsic triangulation of a triangle mesh: the same surface and the same vertices, but triangles whose
    /// edges are geodesics along the surface, described only by their lengths (Sharp, Soliman and Crane,
    /// "Navigating Intrinsic Triangulations", 2019). FlipToDelaunay() flips edges until the triangulation
    /// is intrinsic Delaunay (Bobenko and Springborn 2007): then all cotan weights are non-negative, the
    /// cotan Laplacian satisfies a maximum principle and mass + t * Laplacian is an M-matrix, however
    /// bad (sliver, obtuse, needle) the input triangles are. The geometry is not changed.
    ///
    /// Data structure: halfedges in an array (halfedge 3t+k of triangle t runs from corner k to k+1, in
    /// the mesh orientation), which also represents triangulations that are not simplicial complexes
    /// (two edges between the same vertices), as they occur after intrinsic flips. Every halfedge carries
    /// its length and a signpost: its direction at its tail vertex, as an angle in the vertex' tangent
    /// space, measured counter-clockwise from the first edge of the vertex in the input mesh and scaled
    /// so that the angles around the vertex sum to 2 pi. Boundary vertices are not scaled: their first
    /// boundary edge has the angle 0, the other one the angle sum (so the discrete connection is exactly
    /// flat on flat meshes, boundaries included). The signposts give the correspondence with the input mesh
    /// (TraceEdge) and the Levi-Civita connection used by the vector heat method.
    ///
    /// Degenerate input triangles (zero area, coincident vertices) are handled by intrinsic mollification
    /// (Sharp and Crane, "A Laplacian for Nonmanifold Triangle Meshes", 2020): all edge lengths are
    /// increased by the smallest delta that makes every triangle inequality hold with a margin of
    /// MollifyFactor * mean edge length. Delta is 0 for meshes without (nearly) degenerate triangles.
    ///
    /// Vertices must be manifold (one fan of triangles each; split bowtie vertices first); boundaries are
    /// allowed. Vertices without triangles are not part of the triangulation.
    ///
    /// DelaunayRefine() also inserts vertices (circumcenters, boundary edge midpoints): then the
    /// triangulation has more vertices than the mesh; indices from InputVertexCount on are inserted
    /// vertices (VertexID -1), located on the input surface by VertexPosition / GetInputLocation.
    /// ExtractCommonSubdivision() gives the overlay of the intrinsic and the input triangles.
    /// </summary>
    public partial class IntrinsicTriangulation
    {
        public DMesh3 Mesh { get; private set; }

        /// <summary> amount added to every input edge length by the mollification (0 for good meshes) </summary>
        public double MollificationDelta { get; private set; }

        /// <summary> mean input edge length (before mollification) </summary>
        public double MeanEdgeLength { get; private set; }

        int N, F;               // current vertex and triangle counts (arrays may be larger)
        int N0, F0;             // input vertex and triangle counts
        int[] toIndex;          // mesh vid -> vertex index (-1 if not in a triangle)
        int[] toVertex;         // vertex index -> mesh vid (-1 for inserted vertices)

        // intrinsic halfedges
        int[] hV;               // tail vertex index
        int[] hTwin;            // -1 on the boundary
        double[] hLen;
        double[] hAngle;        // signpost angle at the tail
        int[] vHe;              // an outgoing halfedge; the first one counter-clockwise at boundary vertices
        double[] vScale;        // signpost angle per actual angle
        double[] vAngleSum;
        bool[] vBoundary;

        // the input mesh, in the same halfedge layout, for TraceEdge
        int[] inTwin;
        double[] inAngle;
        Vector2d[] inLayout;    // per input halfedge: the position of its tail in a layout of its triangle
        int[] inTid;            // triangle index -> mesh tid
        int[] inVHe;

        // inserted vertices (index >= N0): input triangle index, position in its layout (inLayout), and the
        // direction (in that layout) of signpost angle 0; on the boundary also the input boundary halfedge
        // and the parameter along it
        int[] vInTri;
        Vector2d[] vInPos;
        double[] vInRef;
        int[] vInEdge;
        double[] vInEdgeT;


        /// <param name="mollifyFactor"> minimum triangle inequality margin, relative to the mean edge length </param>
        public IntrinsicTriangulation(DMesh3 mesh, double mollifyFactor = 1e-6)
        {
            Mesh = mesh;
            build(mollifyFactor);
        }

        /// <summary> number of vertices of the triangulation (those used by triangles) </summary>
        public int VertexCount { get { return N; } }
        /// <summary> number of vertices of the input mesh in the triangulation; indices from here on are inserted </summary>
        public int InputVertexCount { get { return N0; } }
        /// <summary> is vertex index i inserted by DelaunayRefine (not a mesh vertex)? </summary>
        public bool IsInserted(int i) { return i >= N0; }
        public int TriangleCount { get { return F; } }
        public int HalfedgeCount { get { return 3 * F; } }

        /// <summary> mesh vertex id of vertex index i, -1 for inserted vertices </summary>
        public int VertexID(int i) { return i < N0 ? toVertex[i] : -1; }
        /// <summary> vertex index of a mesh vertex id, -1 if the vertex is not used by a triangle </summary>
        public int VertexIndex(int vid) { return (vid >= 0 && vid < toIndex.Length) ? toIndex[vid] : -1; }

        public static int Next(int h) { return 3 * (h / 3) + (h % 3 + 1) % 3; }
        public static int Prev(int h) { return 3 * (h / 3) + (h % 3 + 2) % 3; }
        /// <summary> tail vertex index of halfedge h </summary>
        public int Tail(int h) { return hV[h]; }
        /// <summary> head vertex index of halfedge h </summary>
        public int Head(int h) { return hV[Next(h)]; }
        public int Twin(int h) { return hTwin[h]; }
        public double Length(int h) { return hLen[h]; }
        /// <summary> signpost: direction of halfedge h in the tangent space of its tail (see class comment) </summary>
        public double Signpost(int h) { return hAngle[h]; }
        /// <summary> direction of the edge of h at its head vertex, pointing back to the tail </summary>
        public double ReverseSignpost(int h) { return hTwin[h] >= 0 ? hAngle[hTwin[h]] : LastSignpost(Head(h)); }
        /// <summary> signpost of the second boundary edge of boundary vertex index i (its angle sum; boundary vertices are not scaled) </summary>
        public double LastSignpost(int i) { return vScale[i] * vAngleSum[i]; }

        internal static double vertex_scale(bool boundary, double angleSum)
        {
            return (boundary || !(angleSum > 0)) ? 1 : 2 * Math.PI / angleSum;
        }
        public bool IsBoundaryVertex(int i) { return vBoundary[i]; }
        /// <summary> sum of the triangle angles at vertex index i (2 pi minus the angle defect) </summary>
        public double AngleSum(int i) { return vAngleSum[i]; }
        /// <summary> signpost angle per actual angle at vertex index i (2 pi / angle sum; 1 on the boundary) </summary>
        public double AngleScale(int i) { return vScale[i]; }
        /// <summary> an outgoing halfedge of vertex index i (at a boundary vertex the boundary edge with signpost 0) </summary>
        public int VertexHalfedge(int i) { return vHe[i]; }
        /// <summary> next outgoing halfedge counter-clockwise around the tail, -1 after the last at a boundary vertex </summary>
        public int NextAroundVertex(int h) { return hTwin[Prev(h)]; }

        /// <summary> triangle t as mesh vertex ids </summary>
        public Index3i GetTriangle(int t)
        {
            return new Index3i(toVertex[hV[3 * t]], toVertex[hV[3 * t + 1]], toVertex[hV[3 * t + 2]]);
        }
        /// <summary> triangle t as vertex indices </summary>
        public Index3i GetTriangleIndices(int t)
        {
            return new Index3i(hV[3 * t], hV[3 * t + 1], hV[3 * t + 2]);
        }

        /// <summary> interior angle of the triangle of h at the tail of h </summary>
        public double CornerAngle(int h)
        {
            // sides: h (tail-head), prev(h) (third-tail); opposite: next(h)
            return angle_from_lengths(hLen[Next(h)], hLen[h], hLen[Prev(h)]);
        }

        /// <summary> cotangent of the angle opposite halfedge h in its triangle </summary>
        public double OppositeCotan(int h)
        {
            return cotan_from_lengths(hLen[h], hLen[Next(h)], hLen[Prev(h)]);
        }

        /// <summary> cotan weight of the edge of h: half the sum of the cotangents of the opposite angles </summary>
        public double CotanWeight(int h)
        {
            double w = 0.5 * OppositeCotan(h);
            if (hTwin[h] >= 0) w += 0.5 * OppositeCotan(hTwin[h]);
            return w;
        }

        public double TriangleArea(int t)
        {
            return area_from_lengths(hLen[3 * t], hLen[3 * t + 1], hLen[3 * t + 2]);
        }

        /// <summary>
        /// isometric layout of triangle t in the plane, counter-clockwise: corner 0 at the origin, corner 1
        /// on the positive x axis
        /// </summary>
        public void GetTriangleLayout(int t, out Vector2d q0, out Vector2d q1, out Vector2d q2)
        {
            int h = 3 * t;
            double a0 = CornerAngle(h);
            q0 = Vector2d.Zero;
            q1 = new Vector2d(hLen[h], 0);
            double l2 = hLen[h + 2];
            q2 = new Vector2d(l2 * Math.Cos(a0), l2 * Math.Sin(a0));
        }

        /// <summary> smallest triangle angle (radians) </summary>
        public double MinAngle()
        {
            double m = Math.PI;
            for (int h = 0; h < 3 * F; ++h) m = Math.Min(m, CornerAngle(h));
            return m;
        }

        /// <summary> largest triangle angle (radians) </summary>
        public double MaxAngle()
        {
            double m = 0;
            for (int h = 0; h < 3 * F; ++h) m = Math.Max(m, CornerAngle(h));
            return m;
        }


        void build(double mollifyFactor)
        {
            toIndex = new int[Mesh.MaxVertexID];
            for (int k = 0; k < toIndex.Length; ++k) toIndex[k] = -1;
            var vlist = new List<int>();
            var tlist = new List<int>();
            foreach (int tid in Mesh.TriangleIndices()) {
                tlist.Add(tid);
                Index3i t = Mesh.GetTriangle(tid);
                for (int k = 0; k < 3; ++k)
                    if (toIndex[t[k]] < 0) { toIndex[t[k]] = vlist.Count; vlist.Add(t[k]); }
            }
            toVertex = vlist.ToArray();
            inTid = tlist.ToArray();
            N = toVertex.Length;
            F = inTid.Length;

            hV = new int[3 * F];
            hTwin = new int[3 * F];
            hLen = new double[3 * F];
            hAngle = new double[3 * F];
            var edgeHalf = new Dictionary<int, int>();      // mesh eid -> first halfedge seen
            double sum = 0;
            for (int t = 0; t < F; ++t) {
                Index3i tri = Mesh.GetTriangle(inTid[t]);
                for (int k = 0; k < 3; ++k) {
                    int h = 3 * t + k;
                    int a = tri[k], b = tri[(k + 1) % 3];
                    hV[h] = toIndex[a];
                    hTwin[h] = -1;
                    hLen[h] = Mesh.GetVertex(a).Distance(Mesh.GetVertex(b));
                    sum += hLen[h];
                    int eid = Mesh.FindEdge(a, b);
                    int other;
                    if (edgeHalf.TryGetValue(eid, out other)) {
                        hTwin[h] = other; hTwin[other] = h;
                        hLen[h] = hLen[other];      // bitwise the same length on both sides
                    } else
                        edgeHalf[eid] = h;
                }
            }
            MeanEdgeLength = (F > 0) ? sum / (3 * F) : 0;

            // mollification
            double eps = mollifyFactor * MeanEdgeLength;
            double delta = 0;
            for (int t = 0; t < F; ++t)
                for (int k = 0; k < 3; ++k) {
                    double a = hLen[3 * t + k], b = hLen[3 * t + (k + 1) % 3], c = hLen[3 * t + (k + 2) % 3];
                    delta = Math.Max(delta, eps - (b + c - a));
                }
            MollificationDelta = delta;
            if (delta > 0)
                for (int h = 0; h < 3 * F; ++h) hLen[h] += delta;

            // vertex fans: the outgoing halfedges counter-clockwise, starting at the boundary
            vHe = new int[N];
            for (int i = 0; i < N; ++i) vHe[i] = -1;
            for (int h = 0; h < 3 * F; ++h) {
                int i = hV[h];
                if (vHe[i] < 0 || hTwin[h] < 0) vHe[i] = h;
            }
            vScale = new double[N];
            vAngleSum = new double[N];
            vBoundary = new bool[N];
            int[] fanCount = new int[N];
            for (int i = 0; i < N; ++i) {
                int h0 = vHe[i];
                vBoundary[i] = hTwin[h0] < 0;
                double s = 0;
                int h = h0, count = 0;
                while (true) {
                    count++;
                    s += CornerAngle(h);
                    int nh = NextAroundVertex(h);
                    if (nh < 0 || nh == h0) break;
                    h = nh;
                    if (count > 3 * F) break;
                }
                fanCount[i] = count;
                vAngleSum[i] = s;
                vScale[i] = vertex_scale(vBoundary[i], s);
                double phi = 0;
                h = h0;
                for (int k = 0; k < count; ++k) {
                    hAngle[h] = phi;
                    phi += vScale[i] * CornerAngle(h);
                    h = NextAroundVertex(h);
                }
            }
            // manifold check: each vertex' fan must contain all its halfedges
            int[] heCount = new int[N];
            for (int h = 0; h < 3 * F; ++h) heCount[hV[h]]++;
            for (int i = 0; i < N; ++i)
                if (heCount[i] != fanCount[i])
                    throw new ArgumentException("IntrinsicTriangulation: non-manifold (bowtie) vertex " + toVertex[i]
                        + "; split bowtie vertices first");

            inTwin = (int[])hTwin.Clone();
            inAngle = (double[])hAngle.Clone();
            inVHe = (int[])vHe.Clone();
            inLayout = new Vector2d[3 * F];
            for (int t = 0; t < F; ++t) {
                Vector2d q0, q1, q2;
                GetTriangleLayout(t, out q0, out q1, out q2);
                inLayout[3 * t] = q0; inLayout[3 * t + 1] = q1; inLayout[3 * t + 2] = q2;
            }
            N0 = N; F0 = F;
            vInTri = new int[N]; vInPos = new Vector2d[N]; vInRef = new double[N];
            vInEdge = new int[N]; vInEdgeT = new double[N];
            for (int i = 0; i < N; ++i) { vInTri[i] = -1; vInEdge[i] = -1; }
        }


        /// <summary> is the edge of h locally Delaunay (opposite angles sum to at most pi)? Boundary edges are. </summary>
        public bool IsDelaunay(int h, double tolerance = 1e-10)
        {
            if (hTwin[h] < 0) return true;
            return OppositeCotan(h) + OppositeCotan(hTwin[h]) >= -tolerance;
        }

        /// <summary> are all edges locally Delaunay? </summary>
        public bool IsDelaunay(double tolerance = 1e-10)
        {
            for (int h = 0; h < 3 * F; ++h)
                if (IsDelaunay(h, tolerance) == false) return false;
            return true;
        }


        /// <summary>
        /// Flips edges until every edge is intrinsic Delaunay (Lawson's algorithm, which terminates for
        /// intrinsic triangulations, Indermitte et al. 2001). Returns the number of flips.
        /// </summary>
        public int FlipToDelaunay(double tolerance = 1e-10)
        {
            var seeds = new List<int>();
            for (int h = 0; h < 3 * F; ++h)
                if (hTwin[h] > h) seeds.Add(h);
            return flip_from(seeds, null, tolerance);
        }

        // Lawson flips starting from the edges of the seed halfedges; the faces of all flipped edges are
        // added to changedFaces (if not null). Returns the number of flips.
        int flip_from(IEnumerable<int> seeds, List<int> changedFaces, double tolerance)
        {
            var queue = new Queue<int>();
            var queued = new HashSet<int>();
            foreach (int s in seeds) {
                if (hTwin[s] < 0) continue;
                int rep = Math.Min(s, hTwin[s]);
                if (queued.Add(rep)) queue.Enqueue(rep);
            }
            int flips = 0;
            long maxFlips = 100L * 3 * F + 1000;
            while (queue.Count > 0 && flips < maxFlips) {
                int h = queue.Dequeue();
                queued.Remove(h);
                int t = hTwin[h];
                if (t < 0) continue;
                if (IsDelaunay(h, tolerance)) continue;
                if (FlipEdge(h) == false) continue;
                flips++;
                if (changedFaces != null) { changedFaces.Add(h / 3); changedFaces.Add(t / 3); }
                // the four outer edges of the diamond (now Next/Prev of h and its twin)
                int[] outer = { Next(h), Prev(h), Next(t), Prev(t) };
                foreach (int o in outer) {
                    int ot = hTwin[o];
                    if (ot < 0) continue;
                    int rep = Math.Min(o, ot);
                    if (queued.Add(rep)) queue.Enqueue(rep);
                }
            }
            return flips;
        }


        /// <summary>
        /// Flips the interior edge of halfedge h (i -> j, in triangle (i, j, k), twin in (j, i, l)) to the
        /// edge (k, l). Returns false (and changes nothing) if the edge is on the boundary or the two
        /// triangles do not form a convex quadrilateral. Afterwards h runs k -> l and its twin l -> k.
        /// </summary>
        public bool FlipEdge(int h)
        {
            int ta = hTwin[h];
            if (ta < 0 || ta / 3 == h / 3) return false;
            int ha = h, hb = Next(ha), hc = Prev(ha);       // i->j, j->k, k->i
            int tb = Next(ta), tc = Prev(ta);               // i->l, l->j
            int vi = hV[ha], vj = hV[hb], vk = hV[hc], vl = hV[tc];

            // layout: i at the origin, j on the x axis, k above, l below
            double lij = hLen[ha];
            double aI1 = CornerAngle(ha), aI2 = CornerAngle(tb);        // angles at i in both triangles
            double aJ1 = CornerAngle(hb), aJ2 = CornerAngle(ta);        // angles at j
            if (aI1 + aI2 >= Math.PI - 1e-12 || aJ1 + aJ2 >= Math.PI - 1e-12)
                return false;
            Vector2d pk = hLen[hc] * new Vector2d(Math.Cos(aI1), Math.Sin(aI1));
            Vector2d pl = hLen[tb] * new Vector2d(Math.Cos(aI2), -Math.Sin(aI2));
            double lkl = pk.Distance(pl);
            if (!(lkl > 0)) return false;

            // signposts of the new edge: from k->i (counter-clockwise, by the new angle at k), from l->j
            double angK = angle_from_lengths(hLen[tb], hLen[hc], lkl);      // in (k, i, l) at k: opposite i->l
            double angL = angle_from_lengths(hLen[hb], hLen[tc], lkl);      // in (l, j, k) at l: opposite j->k
            // (boundary signposts run from 0 to the angle sum, which can exceed 2 pi: not wrapped)
            double phiK = add_angle(vk, hAngle[hc], vScale[vk] * angK);
            double phiL = add_angle(vl, hAngle[tc], vScale[vl] * angL);

            // new triangles: slot (ha, hb, hc) = (k->l, l->j, j->k), slot (ta, tb, tc) = (l->k, k->i, i->l)
            int[] from = { hb, tc, hc, tb };
            int[] to = { hc, hb, tb, tc };
            int[] oV = new int[4], oT = new int[4];
            double[] oL = new double[4], oA = new double[4];
            for (int m = 0; m < 4; ++m) {
                oV[m] = hV[from[m]]; oT[m] = hTwin[from[m]]; oL[m] = hLen[from[m]]; oA[m] = hAngle[from[m]];
            }
            Func<int, int> map = (x) => {
                for (int m = 0; m < 4; ++m) if (from[m] == x) return to[m];
                return x;
            };
            for (int m = 0; m < 4; ++m) {
                int s = to[m];
                hV[s] = oV[m]; hLen[s] = oL[m]; hAngle[s] = oA[m];
                int tw = oT[m];
                if (tw < 0) hTwin[s] = -1;
                else {
                    int mt = map(tw);
                    hTwin[s] = mt;
                    if (mt == tw) hTwin[tw] = s;
                }
            }
            hV[ha] = vk; hLen[ha] = lkl; hAngle[ha] = phiK; hTwin[ha] = ta;
            hV[ta] = vl; hLen[ta] = lkl; hAngle[ta] = phiL; hTwin[ta] = ha;

            // vertex halfedges that moved or vanished (all read before any is written)
            int[] vs = { vi, vj, vk, vl };
            int[] nv = new int[4];
            for (int m = 0; m < 4; ++m) {
                int v = vs[m], cur = vHe[v];
                if (cur == ha) nv[m] = tc;                  // i->j is gone: i->l
                else if (cur == ta) nv[m] = hc;             // j->i is gone: j->k
                else nv[m] = map(cur);
            }
            for (int m = 0; m < 4; ++m) vHe[vs[m]] = nv[m];
            return true;
        }


        /// <summary>
        /// Cotan Laplacian of the triangulation (positive semidefinite: L_ii = sum of the weights, L_ij =
        /// minus the weight; summed over parallel edges), in vertex indices.
        /// </summary>
        public SymmetricSparseMatrix CotanLaplacian()
        {
            var L = new SymmetricSparseMatrix(N);
            double[] diag = new double[N];
            for (int h = 0; h < 3 * F; ++h) {
                int i = hV[h], j = Head(h);
                if (i == j) continue;
                double c = 0.5 * OppositeCotan(h);
                L[i, j] = L[i, j] - c;
                diag[i] += c; diag[j] += c;
            }
            for (int i = 0; i < N; ++i) L[i, i] = diag[i];
            return L;
        }

        /// <summary> lumped (barycentric) vertex areas, in vertex indices </summary>
        public double[] VertexAreas()
        {
            double[] a = new double[N];
            for (int t = 0; t < F; ++t) {
                double A = TriangleArea(t) / 3;
                for (int k = 0; k < 3; ++k) a[hV[3 * t + k]] += A;
            }
            return a;
        }


        /// <summary>
        /// The path of the intrinsic edge of halfedge h over the input mesh, from its tail to its head
        /// vertex: the points where it crosses input edges, found by walking straight from the tail in
        /// the direction of its signpost for its length (exact up to rounding; with mollification the
        /// path is computed in the mollified geometry and mapped to the input triangles barycentrically).
        /// </summary>
        public List<Vector3d> TraceEdge(int h)
        {
            var path = new List<Vector3d>();
            path.Add(VertexPosition(hV[h]));
            int t; Vector2d p, d;
            start_walk(hV[h], hAngle[h], out t, out p, out d);
            var steps = new List<WalkCrossing>();
            walk_input(ref t, ref p, ref d, hLen[h], steps);
            foreach (var c in steps) path.Add(to_world(c.Halfedge / 3, c.Point));
            path.Add(to_world(t, p));
            return path;
        }

        /// <summary> a crossing of a walk with an input edge: the input halfedge it leaves through, parameter along it </summary>
        internal struct WalkCrossing
        {
            public int Halfedge;
            public double U;
            public Vector2d Point;      // in the layout of the triangle of Halfedge
        }

        // Start of a straight walk from vertex index i in the direction of signpost angle phi: the input
        // triangle, the position in its layout and the unit direction there.
        internal void start_walk(int i, double phi, out int t, out Vector2d p, out Vector2d d)
        {
            if (i >= N0) {
                t = vInTri[i];
                p = vInPos[i];
                double a = vInRef[i] + phi / vScale[i];
                d = new Vector2d(Math.Cos(a), Math.Sin(a));
                return;
            }
            // the input corner at i that contains the direction
            int h0 = inVHe[i], best = -1;
            double bestOff = 0;
            int hh = h0, guard = 0;
            do {
                double off = vBoundary[i] ? phi - inAngle[hh] : wrap(phi - inAngle[hh]);
                if (off >= -1e-12 && (best < 0 || off < bestOff)) { best = hh; bestOff = off; }
                hh = inTwin[Prev(hh)];
            } while (hh >= 0 && hh != h0 && ++guard < 3 * F0);
            if (best < 0) { best = h0; bestOff = 0; }
            t = best / 3;
            int c = best % 3;
            p = inLayout[best];
            Vector2d e = inLayout[3 * t + (c + 1) % 3] - p;
            double a0 = Math.Atan2(e.y, e.x) + Math.Max(0, bestOff) / vScale[i];
            d = new Vector2d(Math.Cos(a0), Math.Sin(a0));
        }

        // Walks straight from p (in the layout of input triangle t) in the unit direction d for the given
        // length, unfolding across input edges; on return t, p, d describe the end point and the arriving
        // direction. The crossed input edges are added to crossings (if not null). Points on an edge of
        // the triangle (eg a start vertex) leave through it only if the direction points out of it;
        // along a boundary edge the walk slides. Returns false if it ran into the boundary.
        internal bool walk_input(ref int t, ref Vector2d p, ref Vector2d d, double length, List<WalkCrossing> crossings)
        {
            double remaining = length;
            int entered = -1;
            int maxSteps = 6 * F0 + 20;
            for (int step = 0; step < maxSteps; ++step) {
                double scale = 0;
                for (int k = 0; k < 3; ++k) scale = Math.Max(scale, inLayout[3 * t + k].Distance(inLayout[3 * t + (k + 1) % 3]));
                double tol = 1e-12 * scale;
                double bestS = double.MaxValue; int exit = -1;
                for (int k = 0; k < 3; ++k) {
                    if (3 * t + k == entered) continue;      // a straight line crosses an edge once
                    Vector2d a = inLayout[3 * t + k], b = inLayout[3 * t + (k + 1) % 3];
                    Vector2d e = b - a;
                    double el = e.Length;
                    if (!(el > 0)) continue;
                    double rate = (e.x * d.y - e.y * d.x) / el;           // < 0: towards the edge
                    if (rate >= -1e-12) continue;                         // away from it or parallel (rounding)
                    Vector2d ap = p - a;
                    double dist = (e.x * ap.y - e.y * ap.x) / el;        // > 0 inside
                    double s = (dist <= tol) ? 0 : dist / -rate;
                    if (s < bestS) { bestS = s; exit = k; }
                }
                if (exit < 0 || remaining <= bestS) {
                    p = p + remaining * d;
                    return true;
                }
                int eh = 3 * t + exit;
                Vector2d A = inLayout[eh], B = inLayout[3 * t + (exit + 1) % 3];
                Vector2d x = p + bestS * d;
                double u = MathUtil.Clamp((x - A).Dot(B - A) / (B - A).LengthSquared, 0, 1);
                x = A + u * (B - A);
                int tw = inTwin[eh];
                if (tw < 0) {
                    p = x;
                    remaining -= bestS;
                    if (bestS > 1e-9 * length) return remaining <= 1e-9 * length;
                    // on the boundary edge: slide along it
                    Vector2d ue = unit(B - A);
                    double along = d.Dot(ue);
                    if (Math.Abs(along) < 1e-9) return false;
                    d = along > 0 ? ue : -ue;
                    entered = eh;                   // do not leave through the edge it slides along
                    continue;
                }
                if (crossings != null) crossings.Add(new WalkCrossing() { Halfedge = eh, U = u, Point = x });
                remaining -= bestS;
                // unfold into the neighbour: the edge runs B -> A there
                Vector2d u1 = unit(B - A);
                Vector2d n1 = new Vector2d(-u1.y, u1.x);
                int t2 = tw / 3;
                Vector2d A2 = inLayout[3 * t2 + (tw % 3 + 1) % 3], B2 = inLayout[tw];
                Vector2d u2 = unit(B2 - A2);
                Vector2d n2 = new Vector2d(-u2.y, u2.x);
                d = unit(d.Dot(u1) * u2 + d.Dot(n1) * n2);
                p = A2 + u * (B2 - A2);
                t = t2; entered = tw;
            }
            return false;
        }

        /// <summary> position on the input surface of vertex index i (inserted vertices from their input location) </summary>
        public Vector3d VertexPosition(int i)
        {
            if (i < N0) return Mesh.GetVertex(toVertex[i]);
            return to_world(vInTri[i], vInPos[i]);
        }

        /// <summary>
        /// location of vertex index i on the input mesh: mesh triangle id and barycentric coordinates
        /// (for mesh vertices: a triangle of the vertex, with coordinate 1 at it)
        /// </summary>
        public void GetInputLocation(int i, out int tid, out Vector3d bary)
        {
            int t; Vector2d p;
            if (i < N0) { t = inVHe[i] / 3; p = inLayout[inVHe[i]]; } else { t = vInTri[i]; p = vInPos[i]; }
            tid = inTid[t];
            bary = barycentric(t, p);
        }

        Vector3d barycentric(int t, Vector2d p)
        {
            Vector2d q0 = inLayout[3 * t], q1 = inLayout[3 * t + 1], q2 = inLayout[3 * t + 2];
            double det = (q1.x - q0.x) * (q2.y - q0.y) - (q2.x - q0.x) * (q1.y - q0.y);
            double b1 = ((p.x - q0.x) * (q2.y - q0.y) - (q2.x - q0.x) * (p.y - q0.y)) / det;
            double b2 = ((q1.x - q0.x) * (p.y - q0.y) - (p.x - q0.x) * (q1.y - q0.y)) / det;
            return new Vector3d(1 - b1 - b2, b1, b2);
        }

        Vector3d to_world(int t, Vector2d p)
        {
            Vector3d b = barycentric(t, p);
            Index3i tri = Mesh.GetTriangle(inTid[t]);
            return b.x * Mesh.GetVertex(tri.a) + b.y * Mesh.GetVertex(tri.b) + b.z * Mesh.GetVertex(tri.c);
        }


        /// <summary>
        /// Tangent basis of a mesh vertex in which the signpost angles are measured: e1 is the direction of
        /// the vertex' first input edge projected into the tangent plane (normal: angle-weighted), e2 = n x e1.
        /// </summary>
        public void GetTangentBasis(int vid, out Vector3d e1, out Vector3d e2, out Vector3d n)
        {
            int i = VertexIndex(vid);
            if (i < 0)
                throw new ArgumentException("IntrinsicTriangulation: " + vid + " is not a mesh vertex with triangles");
            Vector3d p = Mesh.GetVertex(vid);
            n = Vector3d.Zero;
            int h0 = inVHe[i], hh = h0, guard = 0;
            do {
                Index3i tri = Mesh.GetTriangle(inTid[hh / 3]);
                Vector3d a = Mesh.GetVertex(tri.a), b = Mesh.GetVertex(tri.b), c = Mesh.GetVertex(tri.c);
                Vector3d fn = (b - a).Cross(c - a);
                double l = fn.Length;
                if (l > 0) n += CornerAngle_input(hh) * (fn / l);
                hh = inTwin[Prev(hh)];
            } while (hh >= 0 && hh != h0 && ++guard < 3 * F0);
            if (n.Length < 1e-300) n = Vector3d.AxisZ;
            n /= n.Length;
            Vector3d q = Mesh.GetVertex(toVertex[hV_input_head(h0)]);
            e1 = (q - p) - (q - p).Dot(n) * n;
            if (e1.Length < 1e-300) e1 = Math.Abs(n.x) < 0.9 ? Vector3d.AxisX.Cross(n) : Vector3d.AxisY.Cross(n);
            e1 /= e1.Length;
            e2 = n.Cross(e1);
        }

        double CornerAngle_input(int h)
        {
            int t = h / 3, c = h % 3;
            Vector2d a = inLayout[h], b = inLayout[3 * t + (c + 1) % 3], d = inLayout[3 * t + (c + 2) % 3];
            return Vector2d.AngleR(b - a, d - a);
        }

        int hV_input_head(int h)
        {
            Index3i tri = Mesh.GetTriangle(inTid[h / 3]);
            return toIndex[tri[(h % 3 + 1) % 3]];
        }

        // Corners around a vertex as pairs of (signpost angle, angle in the projected tangent plane), from
        // the first input edge counter-clockwise, closed by the first edge again at 2 pi (the gap outside
        // a boundary vertex is a corner too). Both angle sequences increase.
        void tangent_wedges(int vid, out List<double> sign, out List<double> world, out Vector3d e1, out Vector3d e2)
        {
            Vector3d n;
            GetTangentBasis(vid, out e1, out e2, out n);
            int i = VertexIndex(vid);
            Vector3d p = Mesh.GetVertex(vid);
            sign = new List<double>();
            world = new List<double>();
            int h0 = inVHe[i], hh = h0, guard = 0;
            double prevW = 0;
            while (true) {
                Vector3d d = Mesh.GetVertex(toVertex[hV_input_head(hh)]) - p;
                double w = Math.Atan2(d.Dot(e2), d.Dot(e1));
                if (sign.Count == 0) w = 0;
                else { while (w < prevW) w += 2 * Math.PI; }
                sign.Add(inAngle[hh]); world.Add(w);
                prevW = w;
                // the head of the last input corner, at a boundary vertex
                int nh = inTwin[Prev(hh)];
                if (nh < 0) {
                    Vector3d dl = Mesh.GetVertex(toVertex[hV_input_head(Next(hh))]) - p;
                    double wl = Math.Atan2(dl.Dot(e2), dl.Dot(e1));
                    while (wl < prevW) wl += 2 * Math.PI;
                    sign.Add(LastSignpost(i)); world.Add(wl);
                    break;
                }
                if (nh == h0 || ++guard > 3 * F0) break;
                hh = nh;
            }
            // close the circle back at the first edge, keeping both sequences increasing: a boundary vertex with
            // an angle sum above 2 pi has signposts beyond 2 pi, and the projection of a folded fan (saddles)
            // winds more than once. Directions are only defined modulo 2 pi, so map_angle uses the first corner
            // that contains an angle.
            sign.Add(next_turn(sign[sign.Count - 1])); world.Add(next_turn(world[world.Count - 1]));
        }

        // smallest multiple of 2 pi, at least 2 pi, that is above a (or equal to it)
        static double next_turn(double a)
        {
            const double twoPi = 2 * Math.PI;
            return Math.Max(1, Math.Ceiling(a / twoPi - 1e-12)) * twoPi;
        }

        static double map_angle(double a, List<double> from, List<double> to)
        {
            a = wrap(a);
            for (int k = 0; k + 1 < from.Count; ++k)
                if (a <= from[k + 1] || k + 2 == from.Count) {
                    double span = from[k + 1] - from[k];
                    double f = (span > 0) ? (a - from[k]) / span : 0;
                    return to[k] + f * (to[k + 1] - to[k]);
                }
            return a;
        }

        /// <summary>
        /// tangent vector (in the vertex' signpost coordinates) to a 3D vector in the tangent plane: the
        /// angle is mapped linearly within each corner between the signpost and the projected directions
        /// of its edges, so mesh edge directions map exactly (also at boundary and curved vertices)
        /// </summary>
        public Vector3d ToWorld(int vid, Vector2d v)
        {
            List<double> sign, world;
            Vector3d e1, e2;
            tangent_wedges(vid, out sign, out world, out e1, out e2);
            double len = v.Length;
            if (len == 0) return Vector3d.Zero;
            double a = map_angle(Math.Atan2(v.y, v.x), sign, world);
            return len * (Math.Cos(a) * e1 + Math.Sin(a) * e2);
        }

        /// <summary> 3D vector projected into the tangent plane of a vertex, in its signpost coordinates (inverse of ToWorld) </summary>
        public Vector2d FromWorld(int vid, Vector3d w)
        {
            List<double> sign, world;
            Vector3d e1, e2;
            tangent_wedges(vid, out sign, out world, out e1, out e2);
            Vector2d t = new Vector2d(w.Dot(e1), w.Dot(e2));
            double len = t.Length;
            if (len == 0) return Vector2d.Zero;
            double a = map_angle(Math.Atan2(t.y, t.x), world, sign);
            return new Vector2d(len * Math.Cos(a), len * Math.Sin(a));
        }


        static Vector2d unit(Vector2d v)
        {
            double l = Math.Sqrt(v.x * v.x + v.y * v.y);
            return l > 0 ? v / l : v;
        }

        internal static double wrap(double a)
        {
            const double twoPi = 2 * Math.PI;
            a = a % twoPi;
            if (a < 0) a += twoPi;
            if (a >= twoPi) a -= twoPi;
            return a;
        }

        // Kahan's formula for the area from the sides
        internal static double area_from_lengths(double a, double b, double c)
        {
            // sort a >= b >= c
            if (a < b) { double t = a; a = b; b = t; }
            if (b < c) { double t = b; b = c; c = t; }
            if (a < b) { double t = a; a = b; b = t; }
            double p = (a + (b + c)) * (c - (a - b)) * (c + (a - b)) * (a + (b - c));
            return p > 0 ? 0.25 * Math.Sqrt(p) : 0;
        }

        // angle opposite side a
        internal static double angle_from_lengths(double a, double b, double c)
        {
            double A4 = 4 * area_from_lengths(a, b, c);
            double x = b * b + c * c - a * a;           // 2 b c cos
            return Math.Atan2(A4, x);                   // tan = 4A / (b^2 + c^2 - a^2)
        }

        internal static double cotan_from_lengths(double a, double b, double c)
        {
            double A4 = 4 * area_from_lengths(a, b, c);
            double x = b * b + c * c - a * a;
            if (A4 <= 0) return x >= 0 ? 1e30 : -1e30;
            return x / A4;
        }
    }
}
