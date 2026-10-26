// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary> outcome of IntrinsicTriangulation.DelaunayRefine </summary>
    public enum IntrinsicRefinementStatus
    {
        /// <summary> Delaunay, all cotan weights non-negative, every triangle meets the angle bound (except SkippedTriangles) </summary>
        Success,
        /// <summary> stopped at the insertion limit; the triangulation is valid and Delaunay, but triangles may still be below the bound </summary>
        InsertionLimitReached,
        /// <summary> some triangles could not be refined (their circumcenter fell onto an existing vertex) </summary>
        Incomplete,
        /// <summary>
        /// not refined: the mesh has non-manifold (bowtie) vertices; MeshGeodesicDistance then uses the flips
        /// alone (IntrinsicTriangulation and VectorHeatMethod do not accept such meshes at all: ArgumentException)
        /// </summary>
        NonManifoldInput
    }

    /// <summary> result of IntrinsicTriangulation.DelaunayRefine </summary>
    public class IntrinsicRefinementResult
    {
        public IntrinsicRefinementStatus Status;
        /// <summary> requested minimum angle in degrees (after clamping to [0, 30]) </summary>
        public double MinAngleBound;
        /// <summary> vertices inserted by this call </summary>
        public int InsertedVertices;
        /// <summary> insertions at circumcenters inside a triangle / on an interior edge </summary>
        public int CircumcenterInsertions;
        /// <summary> boundary edges split (opposite an obtuse angle, or encroached by a circumcenter) </summary>
        public int BoundarySplits;
        public int Flips;
        /// <summary> smallest triangle angle afterwards (degrees) </summary>
        public double MinAngle;
        /// <summary>
        /// triangles below the bound that are not refined because the small angle comes from a small input
        /// angle (a vertex with an angle sum below 60 degrees, or two boundary edges meeting there)
        /// </summary>
        public int SkippedTriangles;
        /// <summary> triangles below the bound that are neither skipped nor refined (limit reached or insertion failed) </summary>
        public int RemainingBadTriangles;
        /// <summary> edges with a negative cotan weight afterwards (0 unless the insertion limit was hit) </summary>
        public int NegativeWeights;
        /// <summary> smallest cotan weight afterwards </summary>
        public double MinCotanWeight;

        public override string ToString()
        {
            return string.Format("{0}: {1} vertices inserted ({2} circumcenters, {3} boundary splits), {4} flips, min angle {5:F2} deg (bound {6}), {7} skipped, {8} remaining, {9} negative weights",
                Status, InsertedVertices, CircumcenterInsertions, BoundarySplits, Flips, MinAngle, MinAngleBound,
                SkippedTriangles, RemainingBadTriangles, NegativeWeights);
        }
    }


    // Delaunay refinement: vertex insertion in faces and on edges with the signpost updates (Sharp, Soliman
    // and Crane, "Navigating Intrinsic Triangulations", 2019, section 3.4/4.2), and Chew's / Ruppert's
    // refinement by circumcenters (Chew 1993; Ruppert 1995; Shewchuk 2002 for small input angles).
    public partial class IntrinsicTriangulation
    {
        /// <summary>
        /// Intrinsic Delaunay triangulation of a mesh, for its cotan Laplacian (CotanLaplacian, VertexAreas):
        /// flipped to Delaunay, and with refine also refined (DelaunayRefine(minAngleDegrees, maxInsertions)),
        /// so that the cotan weights are non-negative also at boundary edges opposite obtuse angles. Then the
        /// Laplacian and the areas include the inserted vertices (indices from InputVertexCount on, VertexID
        /// -1); values at them can be interpolated or eliminated by the caller. result is null without refine.
        /// </summary>
        public static IntrinsicTriangulation BuildDelaunay(DMesh3 mesh, bool refine, out IntrinsicRefinementResult result,
            double minAngleDegrees = 25, int maxInsertions = 0)
        {
            var T = new IntrinsicTriangulation(mesh);
            result = null;
            if (refine) result = T.DelaunayRefine(minAngleDegrees, maxInsertions);
            else T.FlipToDelaunay();
            return T;
        }

        /// <summary>
        /// Intrinsic Delaunay refinement: flips to Delaunay, then inserts vertices until
        /// (1) no boundary edge lies opposite an obtuse angle (such edges cannot be flipped and keep a
        /// negative cotan weight; they are split at their midpoint, Ruppert's encroached segments), and
        /// (2) every triangle has all angles at least minAngleDegrees: the circumcenter of a bad triangle
        /// is located by walking straight from its barycenter across the intrinsic triangles and inserted
        /// (Chew's second algorithm); if the walk leaves the surface, or the circumcenter lies in the
        /// diametral circle of a boundary edge of its triangle, that boundary edge is split instead.
        /// After each insertion the triangulation is flipped back to Delaunay. So afterwards all cotan
        /// weights are non-negative, also on the boundary.
        ///
        /// minAngleDegrees is clamped to [0, 30] (above about 30 degrees termination is not guaranteed); 0
        /// only splits the boundary edges opposite obtuse angles. Triangles whose small angle comes from
        /// the input (at a vertex with an angle sum below 60 degrees, or whose shortest edge connects two
        /// boundary edges of such a vertex) cannot be improved and are skipped (Shewchuk 2002); boundary
        /// edges at such vertices are split at powers of two from the vertex (concentric shells), so the
        /// refinement there terminates. maxInsertions (0: 10 * input vertices + 1000) bounds the work;
        /// boundary edges opposite obtuse angles are split up to twice that limit.
        /// The geometry is not changed; inserted vertices lie on the input surface.
        /// </summary>
        public IntrinsicRefinementResult DelaunayRefine(double minAngleDegrees = 25, int maxInsertions = 0)
        {
            var r = new IntrinsicRefinementResult();
            r.MinAngleBound = MathUtil.Clamp(minAngleDegrees, 0, 30);
            double theta = r.MinAngleBound * Math.PI / 180;
            int limit = maxInsertions > 0 ? maxInsertions : 10 * N0 + 1000;
            int n0 = N;
            r.Flips += FlipToDelaunay();

            var faceQ = new Queue<int>();
            var inFaceQ = new HashSet<int>();
            var boundaryQ = new Queue<int>();
            for (int f = 0; f < F; ++f) { faceQ.Enqueue(f); inFaceQ.Add(f); }
            for (int h = 0; h < 3 * F; ++h) if (hTwin[h] < 0) boundaryQ.Enqueue(h);
            var changed = new List<int>();
            var seeds = new List<int>();
            bool limitHit = false;
            int inserted = 0;
            while (true) {
                int v;
                if (boundaryQ.Count > 0) {
                    int h = boundaryQ.Dequeue();
                    if (h >= 3 * F || hTwin[h] >= 0 || is_encroached(h) == false) continue;
                    if (inserted >= 2 * limit) { limitHit = true; break; }
                    v = split_boundary(h);
                    if (v < 0) continue;
                    r.BoundarySplits++;
                } else {
                    if (faceQ.Count == 0 || theta <= 0) break;
                    int f = faceQ.Dequeue();
                    inFaceQ.Remove(f);
                    if (needs_refinement(f, theta) == false) continue;
                    if (inserted >= limit) { limitHit = true; break; }
                    bool boundarySplit;
                    v = insert_circumcenter(f, out boundarySplit);
                    if (v < 0) continue;
                    if (boundarySplit) r.BoundarySplits++; else r.CircumcenterInsertions++;
                    // a boundary edge split instead may leave f as it is: look at it again
                    if (inFaceQ.Add(f)) faceQ.Enqueue(f);
                }
                inserted++;
                // back to Delaunay around the new vertex; requeue every face that changed
                changed.Clear(); seeds.Clear();
                faces_around(v, changed);
                foreach (int f in changed)
                    for (int k = 0; k < 3; ++k) seeds.Add(3 * f + k);
                r.Flips += flip_from(seeds, changed, 1e-10);
                foreach (int f in changed) {
                    if (inFaceQ.Add(f)) faceQ.Enqueue(f);
                    for (int k = 0; k < 3; ++k)
                        if (hTwin[3 * f + k] < 0) boundaryQ.Enqueue(3 * f + k);
                }
            }

            r.InsertedVertices = N - n0;
            r.MinAngle = MinAngle() * 180 / Math.PI;
            for (int f = 0; f < F; ++f) {
                double a = Math.Min(CornerAngle(3 * f), Math.Min(CornerAngle(3 * f + 1), CornerAngle(3 * f + 2)));
                if (a >= theta) continue;
                if (needs_refinement(f, theta)) r.RemainingBadTriangles++; else r.SkippedTriangles++;
            }
            r.MinCotanWeight = double.MaxValue;
            for (int h = 0; h < 3 * F; ++h) {
                if (hTwin[h] >= 0 && hTwin[h] < h) continue;
                double w = CotanWeight(h);
                r.MinCotanWeight = Math.Min(r.MinCotanWeight, w);
                if (w < -1e-9) r.NegativeWeights++;
            }
            if (limitHit) r.Status = IntrinsicRefinementStatus.InsertionLimitReached;
            else if (r.RemainingBadTriangles > 0 || r.NegativeWeights > 0) r.Status = IntrinsicRefinementStatus.Incomplete;
            else r.Status = IntrinsicRefinementStatus.Success;
            return r;
        }


        // a boundary edge is encroached if the angle opposite it is obtuse (its cotan weight is negative);
        // in a Delaunay triangulation that is the case if any vertex lies in its diametral circle
        bool is_encroached(int h)
        {
            return OppositeCotan(h) < -1e-12;
        }

        // vertex whose angle sum is below 60 degrees: triangles there cannot all meet the bound
        bool small_angle_vertex(int v)
        {
            return v < N0 && vAngleSum[v] < Math.PI / 3 + 1e-9;
        }

        // input boundary halfedges a boundary vertex lies on: the one it was inserted on, or for an input
        // vertex its two boundary edges
        void boundary_segments(int v, List<int> segs)
        {
            segs.Clear();
            if (vBoundary[v] == false) return;
            if (v >= N0) {
                if (vInEdge[v] >= 0) segs.Add(vInEdge[v]);
                return;
            }
            int h0 = inVHe[v], hh = h0, guard = 0;
            segs.Add(h0);                               // outgoing boundary edge
            while (inTwin[Prev(hh)] >= 0 && ++guard < 3 * F0) hh = inTwin[Prev(hh)];
            segs.Add(Prev(hh));                         // incoming boundary edge
        }

        int input_tail(int ih) { return toIndex[Mesh.GetTriangle(inTid[ih / 3])[ih % 3]]; }
        int input_head(int ih) { return hV_input_head(ih); }

        readonly List<int> segsA = new List<int>(), segsB = new List<int>();

        // does triangle f have an angle below theta that refinement can and should improve?
        bool needs_refinement(int f, double theta)
        {
            int hm = 3 * f; double am = CornerAngle(hm);
            for (int k = 1; k < 3; ++k) {
                double a = CornerAngle(3 * f + k);
                if (a < am) { am = a; hm = 3 * f + k; }
            }
            if (am >= theta) return false;
            if (small_angle_vertex(hV[hm])) return false;
            // shortest edge (opposite the smallest angle) joining two different boundary edges that meet at
            // a small input angle (Shewchuk 2002); its ends are not that vertex itself
            int va = hV[Next(hm)], vb = hV[Prev(hm)];
            boundary_segments(va, segsA);
            boundary_segments(vb, segsB);
            foreach (int sa in segsA)
                foreach (int sb in segsB) {
                    if (sa == sb) continue;
                    int at = input_tail(sa), ah = input_head(sa), bt = input_tail(sb), bh = input_head(sb);
                    int x = (at == bt || at == bh) ? at : (ah == bt || ah == bh) ? ah : -1;
                    if (x >= 0 && x != va && x != vb && small_angle_vertex(x)) return false;
                }
            return true;
        }

        // faces around vertex v (each once per incident corner)
        void faces_around(int v, List<int> faces)
        {
            int h0 = vHe[v], h = h0, guard = 0;
            do {
                faces.Add(h / 3);
                h = NextAroundVertex(h);
            } while (h >= 0 && h != h0 && ++guard < 3 * F);
        }


        // ---------------------------------------------------------------- insertion

        // Inserts the circumcenter of face f (see DelaunayRefine). Returns the new vertex, -1 if nothing
        // was inserted; boundarySplit tells whether a boundary edge was split instead.
        int insert_circumcenter(int f, out bool boundarySplit)
        {
            boundarySplit = false;
            Vector2d[] q = new Vector2d[3];
            layout(f, q);
            Vector2d b = q[1] - q[0], c = q[2] - q[0];
            double D = 2 * (b.x * c.y - b.y * c.x);
            if (!(D > 0)) return -1;
            Vector2d C = q[0] + new Vector2d(c.y * b.LengthSquared - b.y * c.LengthSquared,
                                             b.x * c.LengthSquared - c.x * b.LengthSquared) / D;
            Vector2d B = (q[0] + q[1] + q[2]) / 3;
            Vector2d dir = C - B;
            double len = dir.Length;
            int fe = f;
            Vector2d pe = B;
            if (len > 0) {
                Vector2d d = dir / len;
                int hit;
                WalkEnd end = walk_intrinsic(ref fe, ref pe, ref d, len, 3 * F + 20, out hit);
                if (end == WalkEnd.OutOfSteps) return -1;
                if (end == WalkEnd.Boundary) {
                    boundarySplit = true;
                    return split_boundary(hit);
                }
            }
            layout(fe, q);
            // the circumcenter in the diametral circle of a nearby boundary edge: split the edge (Ruppert)
            int enc = encroached_boundary(fe, q, pe);
            if (enc >= 0) { boundarySplit = true; return split_boundary(enc); }
            // on an edge or a vertex?
            double scale = Math.Max(hLen[3 * fe], Math.Max(hLen[3 * fe + 1], hLen[3 * fe + 2]));
            int onEdge = -1, count = 0;
            double onU = 0;
            for (int k = 0; k < 3; ++k) {
                Vector2d a = q[k], e = q[(k + 1) % 3] - a;
                double el = e.Length;
                double dist = (e.x * (pe.y - a.y) - e.y * (pe.x - a.x)) / el;
                if (dist < 1e-9 * scale) { onEdge = 3 * fe + k; count++; onU = (pe - a).Dot(e) / (el * el); }
            }
            if (count >= 2) return -1;
            if (count == 1) {
                if (onU < 1e-6 || onU > 1 - 1e-6) return -1;
                if (hTwin[onEdge] < 0) { boundarySplit = true; return split_boundary(onEdge); }
                return split_edge(onEdge, onU);
            }
            return insert_in_face(fe, pe);
        }

        // A boundary edge in the faces around the corners of face f whose diametral circle contains the
        // point p (in the layout q of f), -1 if none. The fans are unfolded into the plane of f by their
        // signposts (exact on flat surfaces; a test only).
        int encroached_boundary(int f, Vector2d[] q, Vector2d p)
        {
            for (int k = 0; k < 3; ++k) {
                int href = 3 * f + k, c = hV[href];
                Vector2d qc = q[k], e = q[(k + 1) % 3] - qc;
                double a0 = Math.Atan2(e.y, e.x);
                int h0 = vHe[c], g = h0, guard = 0;
                do {
                    double delta = hAngle[g] - hAngle[href];
                    if (vBoundary[c] == false) delta = wrap(delta);
                    double ag = a0 + delta / vScale[c];
                    double ap = ag + CornerAngle(g);
                    Vector2d P1 = qc + hLen[g] * new Vector2d(Math.Cos(ag), Math.Sin(ag));
                    Vector2d P2 = qc + hLen[Prev(g)] * new Vector2d(Math.Cos(ap), Math.Sin(ap));
                    if (hTwin[g] < 0 && (p - qc).Dot(p - P1) < 0) return g;
                    if (hTwin[Next(g)] < 0 && (p - P1).Dot(p - P2) < 0) return Next(g);
                    if (hTwin[Prev(g)] < 0 && (p - P2).Dot(p - qc) < 0) return Prev(g);
                    g = NextAroundVertex(g);
                } while (g >= 0 && g != h0 && ++guard < 3 * F);
            }
            return -1;
        }

        // Splits a boundary edge: at its midpoint, or, if it ends at a small input angle, at a power of two
        // from that vertex (concentric shells, Shewchuk 2002), so that splits from both sides of the angle
        // match and do not encroach each other forever
        int split_boundary(int h)
        {
            int ib; double ti, tj;
            if (boundary_param(h, out ib, out ti, out tj) == false) return -1;
            double s = 0.5;
            int vt = input_tail(ib), vh = input_head(ib);
            double L = inLayout[ib].Distance(inLayout[Next(ib)]);
            int v = -1; double di = 0, dj = 0;
            if (ti == 0 && small_angle_vertex(vt)) { v = vt; di = 0; dj = tj * L; }
            else if (tj == 1 && small_angle_vertex(vh)) { v = vh; di = (1 - ti) * L; dj = 0; }
            if (v >= 0) {
                double hi = Math.Max(di, dj);
                double pw = Math.Pow(2, Math.Round(Math.Log(0.5 * hi, 2)));
                if (pw > 0.25 * hi && pw < 0.75 * hi) s = (pw - di) / (dj - di);
            }
            return split_edge(h, s);
        }

        // input boundary halfedge under the intrinsic boundary halfedge h and the parameters of its ends along it
        bool boundary_param(int h, out int ib, out double ti, out double tj)
        {
            int i = hV[h], j = Head(h);
            ib = -1; ti = 0; tj = 1;
            if (i >= N0) ib = vInEdge[i];
            else if (j >= N0) ib = vInEdge[j];
            else ib = inVHe[i];
            if (ib < 0 || inTwin[ib] >= 0) return false;
            ti = (i >= N0) ? vInEdgeT[i] : 0;
            tj = (j >= N0) ? vInEdgeT[j] : 1;
            if (i < N0 && input_tail(ib) != i) return false;
            if (j < N0 && input_head(ib) != j) return false;
            return tj > ti;
        }

        // straight walk across the intrinsic triangles (see walk); stops at the boundary (hit)
        internal WalkEnd walk_intrinsic(ref int f, ref Vector2d p, ref Vector2d d, double length, int maxSteps, out int hit)
        {
            return walk(ref f, ref p, ref d, length, layout, hTwin, maxSteps, false, null, out hit);
        }

        // isometric layout of triangle t (corners of halfedges 3t, 3t+1, 3t+2)
        void layout(int t, Vector2d[] q)
        {
            GetTriangleLayout(t, out q[0], out q[1], out q[2]);
        }

        /// <summary>
        /// Inserts a vertex into triangle t at the point p of its layout (GetTriangleLayout), splitting it
        /// into three; the triangulation is not flipped. Returns the new vertex index, -1 if p is not
        /// strictly inside.
        /// </summary>
        public int InsertVertex(int t, Vector2d p)
        {
            Vector2d[] q = new Vector2d[3];
            layout(t, q);
            for (int k = 0; k < 3; ++k) {
                Vector2d a = q[k], e = q[(k + 1) % 3] - a;
                if (e.x * (p.y - a.y) - e.y * (p.x - a.x) <= 0) return -1;
            }
            return insert_in_face(t, p);
        }

        /// <summary>
        /// Inserts a vertex on the edge of halfedge h at parameter s in (0, 1) from its tail, splitting its
        /// triangles (boundary edges too); the triangulation is not flipped. Returns the new vertex index, -1
        /// if the edge cannot be split (s outside (0, 1), or both sides of the edge in the same triangle).
        /// </summary>
        public int SplitEdge(int h, double s)
        {
            if (!(s > 0 && s < 1)) return -1;
            return split_edge(h, s);
        }

        int insert_in_face(int f, Vector2d P)
        {
            int h0 = 3 * f, h1 = 3 * f + 1, h2 = 3 * f + 2;
            int a = hV[h0], b = hV[h1], c = hV[h2];
            Vector2d[] q = new Vector2d[3];
            layout(f, q);
            double lab = hLen[h0], lbc = hLen[h1], lca = hLen[h2];
            double lap = P.Distance(q[0]), lbp = P.Distance(q[1]), lcp = P.Distance(q[2]);
            if (!(lap > 0 && lbp > 0 && lcp > 0)) return -1;
            double phA = add_angle(a, hAngle[h0], vScale[a] * angle_from_lengths(lbp, lab, lap));
            double phB = add_angle(b, hAngle[h1], vScale[b] * angle_from_lengths(lcp, lbc, lbp));
            double phC = add_angle(c, hAngle[h2], vScale[c] * angle_from_lengths(lap, lca, lcp));
            // locate on the input by tracing from the nearest corner, before anything is changed
            int corner = 0;
            if (lbp < lap && lbp <= lcp) corner = 1;
            else if (lcp < lap && lcp < lbp) corner = 2;
            int lt; Vector2d lp, ld;
            if (trace_to(corner == 0 ? a : corner == 1 ? b : c, corner == 0 ? phA : corner == 1 ? phB : phC,
                    corner == 0 ? lap : corner == 1 ? lbp : lcp, out lt, out lp, out ld) == false)
                return -1;
            int f1 = add_face(), f2 = add_face();
            int p = add_vertex(false);
            relocate(new[] { h1, h2 }, new[] { 3 * f1, 3 * f2 });
            // (a, b, p), (b, c, p), (c, a, p)
            set_he(3 * f + 1, b, 3 * f1 + 2, lbp, phB);
            set_he(3 * f + 2, p, 3 * f2 + 1, lap, 0);
            set_he(3 * f1 + 1, c, 3 * f2 + 2, lcp, phC);
            set_he(3 * f1 + 2, p, 3 * f + 1, lbp, 0);
            set_he(3 * f2 + 1, a, 3 * f + 2, lap, phA);
            set_he(3 * f2 + 2, p, 3 * f1 + 1, lcp, 0);
            vHe[p] = 3 * f + 2;
            init_vertex_signposts(p);
            set_location(p, corner == 0 ? 3 * f2 + 1 : corner == 1 ? 3 * f + 1 : 3 * f1 + 1, lt, lp, ld);
            return p;
        }

        int split_edge(int h, double s)
        {
            int ta = hTwin[h];
            bool boundary = ta < 0;
            if (!boundary && ta / 3 == h / 3) return -1;
            int ib = -1; double ti = 0, tj = 1;
            if (boundary && boundary_param(h, out ib, out ti, out tj) == false) return -1;
            int hb = Next(h), hc = Prev(h);
            int i = hV[h], j = hV[hb], k = hV[hc];
            double l = hLen[h], lki = hLen[hc];
            double lim = s * l, lmj = (1 - s) * l;
            double alpha = CornerAngle(h);
            double lmk = new Vector2d(lki * Math.Cos(alpha) - lim, lki * Math.Sin(alpha)).Length;
            if (!(lim > 0 && lmj > 0 && lmk > 0)) return -1;
            double phK = add_angle(k, hAngle[hc], vScale[k] * angle_from_lengths(lim, lki, lmk));
            int tb = -1, tc = -1, lv = -1;
            double lml = 0, phL = 0;
            if (!boundary) {
                tb = Next(ta); tc = Prev(ta); lv = hV[tc];
                double lil = hLen[tb], llj = hLen[tc];
                double beta = CornerAngle(tb);
                lml = new Vector2d(lil * Math.Cos(beta) - lim, lil * Math.Sin(beta)).Length;
                if (!(lml > 0)) return -1;
                phL = add_angle(lv, hAngle[tc], vScale[lv] * angle_from_lengths(lmj, llj, lml));
            }
            // an interior point is located by tracing from the nearer end, before anything is changed
            int lt = -1; Vector2d lp = Vector2d.Zero, ld = Vector2d.Zero;
            if (!boundary && trace_to(s <= 0.5 ? i : j, s <= 0.5 ? hAngle[h] : hAngle[ta], s <= 0.5 ? lim : lmj, out lt, out lp, out ld) == false)
                return -1;
            int m = add_vertex(boundary);
            int f2 = add_face();
            int g2 = boundary ? -1 : add_face();
            if (boundary) relocate(new[] { hb }, new[] { 3 * f2 + 1 });
            else relocate(new[] { hb, tb }, new[] { 3 * f2 + 1, 3 * g2 + 1 });
            // (i, m, k) in h's slots, (m, j, k) new; (j, m, l) in ta's slots, (m, i, l) new
            hLen[h] = lim; hTwin[h] = boundary ? -1 : 3 * g2;
            set_he(hb, m, 3 * f2 + 2, lmk, 0);
            set_he(3 * f2, m, boundary ? -1 : ta, lmj, 0);
            set_he(3 * f2 + 2, k, hb, lmk, phK);
            if (!boundary) {
                hLen[ta] = lmj; hTwin[ta] = 3 * f2;
                set_he(tb, m, 3 * g2 + 2, lml, 0);
                set_he(3 * g2, m, h, lim, 0);
                set_he(3 * g2 + 2, lv, tb, lml, phL);
            }
            vHe[m] = 3 * f2;
            init_vertex_signposts(m);
            if (boundary) {
                // directly on the input boundary edge: signpost 0 (m -> j) runs along it
                double tm = ti + s * (tj - ti);
                Vector2d A = inLayout[ib], B = inLayout[Next(ib)];
                vInTri[m] = ib / 3;
                vInPos[m] = A + tm * (B - A);
                vInRef[m] = Math.Atan2(B.y - A.y, B.x - A.x);
                vInEdge[m] = ib;
                vInEdgeT[m] = tm;
            } else
                set_location(m, s <= 0.5 ? h : ta, lt, lp, ld);
            return m;
        }

        // traces from vertex u in the direction of signpost phi for the given length over the input: the
        // input triangle, position and arriving direction; false if the walk does not arrive (it ran into
        // the boundary or out of steps), and then the point is not inserted
        bool trace_to(int u, double phi, double length, out int t, out Vector2d p, out Vector2d d)
        {
            start_walk(u, phi, out t, out p, out d);
            return walk_input(ref t, ref p, ref d, length, null);
        }

        // input location of the new vertex v at the head of halfedge hFrom, traced to (t, p) arriving in
        // direction d (trace_to)
        void set_location(int v, int hFrom, int t, Vector2d p, Vector2d d)
        {
            vInTri[v] = t;
            vInPos[v] = p;
            // the edge back to the tail of hFrom leaves v in direction -d
            vInRef[v] = Math.Atan2(-d.y, -d.x) - hAngle[hTwin[hFrom]] / vScale[v];
        }

        // angle sum, scale and signposts of a new vertex from its corner angles, starting at vHe (angle 0)
        void init_vertex_signposts(int v)
        {
            var fan = new List<int>();
            int h0 = vHe[v], h = h0;
            double sum = 0;
            do {
                fan.Add(h);
                sum += CornerAngle(h);
                h = NextAroundVertex(h);
            } while (h >= 0 && h != h0 && fan.Count < 3 * F);
            vAngleSum[v] = sum;
            vScale[v] = (vBoundary[v] || !(sum > 0)) ? 1 : 2 * Math.PI / sum;
            double phi = 0;
            foreach (int g in fan) {
                hAngle[g] = phi;
                phi += vScale[v] * CornerAngle(g);
            }
        }

        double add_angle(int v, double a, double delta)
        {
            return vBoundary[v] ? a + delta : wrap(a + delta);
        }

        void set_he(int h, int v, int twin, double len, double angle)
        {
            hV[h] = v; hTwin[h] = twin; hLen[h] = len; hAngle[h] = angle;
        }

        // moves halfedge records from[m] to slot to[m], keeping twins and vertex halfedges consistent
        void relocate(int[] from, int[] to)
        {
            int n = from.Length;
            int[] oV = new int[n], oT = new int[n];
            double[] oL = new double[n], oA = new double[n];
            for (int m = 0; m < n; ++m) {
                oV[m] = hV[from[m]]; oT[m] = hTwin[from[m]]; oL[m] = hLen[from[m]]; oA[m] = hAngle[from[m]];
            }
            var vfix = new List<KeyValuePair<int, int>>();
            for (int m = 0; m < n; ++m)
                if (vHe[oV[m]] == from[m]) vfix.Add(new KeyValuePair<int, int>(oV[m], to[m]));
            Func<int, int> map = (x) => {
                for (int m = 0; m < n; ++m) if (from[m] == x) return to[m];
                return x;
            };
            for (int m = 0; m < n; ++m) {
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
            foreach (var kv in vfix) vHe[kv.Key] = kv.Value;
        }

        int add_face()
        {
            if (3 * (F + 1) > hV.Length) {
                int cap = Math.Max(3 * (F + 1), 2 * hV.Length);
                Array.Resize(ref hV, cap); Array.Resize(ref hTwin, cap);
                Array.Resize(ref hLen, cap); Array.Resize(ref hAngle, cap);
            }
            return F++;
        }

        int add_vertex(bool boundary)
        {
            if (N + 1 > vHe.Length) {
                int cap = Math.Max(N + 1, 2 * vHe.Length);
                Array.Resize(ref vHe, cap); Array.Resize(ref vScale, cap); Array.Resize(ref vAngleSum, cap);
                Array.Resize(ref vBoundary, cap); Array.Resize(ref toVertex, cap);
                Array.Resize(ref vInTri, cap); Array.Resize(ref vInPos, cap); Array.Resize(ref vInRef, cap);
                Array.Resize(ref vInEdge, cap); Array.Resize(ref vInEdgeT, cap);
            }
            int v = N++;
            toVertex[v] = -1; vBoundary[v] = boundary; vScale[v] = 1; vAngleSum[v] = 0;
            vInTri[v] = -1; vInEdge[v] = -1; vInEdgeT[v] = 0; vInRef[v] = 0; vInPos[v] = Vector2d.Zero;
            return v;
        }


        /// <summary>
        /// Checks the connectivity, lengths and signposts of the triangulation (for tests): twins are
        /// mutual with equal lengths, triangles are non-degenerate, every vertex' fan contains all its
        /// halfedges, and consecutive signposts around a vertex differ by the scaled corner angle.
        /// </summary>
        public bool CheckConsistency(out string error, double angleTolerance = 1e-6)
        {
            error = null;
            int[] count = new int[N];
            for (int h = 0; h < 3 * F; ++h) {
                count[hV[h]]++;
                int tw = hTwin[h];
                if (tw >= 0) {
                    if (hTwin[tw] != h) { error = "twin of twin of " + h; return false; }
                    if (hV[tw] != Head(h)) { error = "twin of " + h + " does not run backwards"; return false; }
                    if (hLen[tw] != hLen[h]) { error = "twin lengths differ at " + h; return false; }
                }
            }
            for (int t = 0; t < F; ++t)
                if (!(TriangleArea(t) > 0)) { error = "degenerate triangle " + t; return false; }
            for (int v = 0; v < N; ++v) {
                int h0 = vHe[v];
                if (hV[h0] != v) { error = "vertex halfedge of " + v; return false; }
                if (vBoundary[v] != (hTwin[h0] < 0)) { error = "boundary vertex halfedge of " + v; return false; }
                int h = h0, n = 0;
                double sum = 0;
                while (true) {
                    n++;
                    sum += CornerAngle(h);
                    int nh = NextAroundVertex(h);
                    if (nh >= 0 && nh != h0) {
                        double expect = add_angle(v, hAngle[h], vScale[v] * CornerAngle(h));
                        double diff = hAngle[nh] - expect;
                        if (vBoundary[v] == false) diff = Math.IEEERemainder(diff, 2 * Math.PI);
                        if (Math.Abs(diff) > angleTolerance) { error = "signpost of " + nh + " at vertex " + v + " off by " + diff; return false; }
                    }
                    if (nh < 0 || nh == h0 || n > 3 * F) break;
                    h = nh;
                }
                if (n != count[v]) { error = "fan of vertex " + v + " has " + n + " of " + count[v] + " halfedges"; return false; }
                if (Math.Abs(sum - vAngleSum[v]) > angleTolerance * Math.Max(1, n)) { error = "angle sum of " + v; return false; }
            }
            return true;
        }
    }
}
