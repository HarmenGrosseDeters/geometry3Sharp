// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// Exact arrangement of the triangles of several meshes: every triangle is split along its intersections
    /// with the triangles of the other meshes, so that the pieces only meet along shared edges and vertices.
    ///
    /// All input coordinates are converted exactly to one integer grid (a double is m * 2^e). Every new point is
    /// the intersection of three planes with integer coefficients (the triangle planes, and for each triangle edge
    /// the plane through the edge perpendicular to the triangle), computed as a canonical rational point
    /// (ExactPoint3), so equal points are identical, also when they are computed from different triangles.
    /// Each split triangle is retriangulated with an exact constrained Delaunay triangulation in a coordinate
    /// projection. A point on an edge is always also computed by the triangles on the other side of the edge
    /// (it is the end of their intersection with the same triangle), so the pieces stay connected.
    ///
    /// Intersections between triangles of the same mesh are not computed (meshes must not self-intersect).
    /// Degenerate (zero-area) triangles are skipped.
    /// </summary>
    internal class ExactMeshArrangement
    {
        public struct Plane
        {
            // A x + B y + C z = D
            public BigInteger A, B, C, D;
            public Plane(BigInteger a, BigInteger b, BigInteger c, BigInteger d) { A = a; B = b; C = c; D = d; }
        }

        public struct Segment
        {
            public int P, Q;
            public Plane Line;     // a plane that intersects the triangle plane in the segment's line
            public Segment(int p, int q, Plane line) { P = p; Q = q; Line = line; }
        }

        public class InputTriangle
        {
            public int Mesh, Tid;
            public int[] Corners = new int[3];      // point ids
            public bool Degenerate;
            public List<Segment> Segments = new List<Segment>();
            public HashSet<int> Points = new HashSet<int>();
            public List<int> Coplanar = new List<int>();    // input triangles of other meshes in the same plane, intersecting

            // planes are computed when first needed (most triangles are far from all intersections)
            internal List<ExactPoint3> pts;
            Plane p;
            Plane[] edgePlanes;
            int axis, sign;
            bool haveP;

            public Plane P { get { ensure(); return p; } }
            /// <summary> edge k: Corners[k] -> Corners[k+1], plane through the edge perpendicular to the triangle </summary>
            public Plane[] EdgePlanes { get { ensure(); return edgePlanes; } }
            /// <summary> unnormalized normal (b-a)x(c-a) </summary>
            public BigInteger NX { get { ensure(); return p.A; } }
            public BigInteger NY { get { ensure(); return p.B; } }
            public BigInteger NZ { get { ensure(); return p.C; } }
            /// <summary> projection: drop Axis; Sign = sign of the normal component on that axis </summary>
            public int Axis { get { ensure(); return axis; } }
            public int Sign { get { ensure(); return sign; } }

            void ensure()
            {
                if (haveP)
                    return;
                if (Degenerate)
                    throw new InvalidOperationException("ExactMeshArrangement: degenerate triangle has no plane");
                ExactPoint3 a = pts[Corners[0]], b = pts[Corners[1]], c = pts[Corners[2]];
                BigInteger ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
                BigInteger vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                BigInteger nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                p = new Plane(nx, ny, nz, nx * a.X + ny * a.Y + nz * a.Z);
                BigInteger ax = BigInteger.Abs(nx), ay = BigInteger.Abs(ny), az = BigInteger.Abs(nz);
                axis = (ax >= ay && ax >= az) ? 0 : (ay >= az) ? 1 : 2;
                sign = ((axis == 0) ? nx : (axis == 1) ? ny : nz).Sign;
                edgePlanes = new Plane[3];
                for (int k = 0; k < 3; ++k)
                    edgePlanes[k] = edge_plane(nx, ny, nz, pts[Corners[k]], pts[Corners[(k + 1) % 3]]);
                haveP = true;
            }

            // plane through the integer points p, q, perpendicular to the triangle plane
            static Plane edge_plane(BigInteger NX, BigInteger NY, BigInteger NZ, ExactPoint3 p, ExactPoint3 q)
            {
                BigInteger dx = q.X - p.X, dy = q.Y - p.Y, dz = q.Z - p.Z;
                BigInteger nx = dy * NZ - dz * NY;
                BigInteger ny = dz * NX - dx * NZ;
                BigInteger nz = dx * NY - dy * NX;
                return new Plane(nx, ny, nz, nx * p.X + ny * p.Y + nz * p.Z);
            }
        }

        public class OutputTriangle
        {
            public int Mesh, Source;     // Source: index into Triangles
            public int A, B, C;
        }

        public List<DMesh3> Meshes;
        public int ScaleExp;        // input coordinate = integer coordinate * 2^ScaleExp
        public List<ExactPoint3> Points = new List<ExactPoint3>();
        public List<InputTriangle> Triangles = new List<InputTriangle>();
        public List<OutputTriangle> Output = new List<OutputTriangle>();
        public HashSet<long> ConstraintEdges = new HashSet<long>(EdgeKeyComparer.Instance);     // undirected point id pairs on intersection curves
        public int[][] VertexPoints;        // per mesh: vertex id -> point id
        public List<int>[] MeshTriangles;   // per mesh: indices into Triangles
        public int IntersectingPairs;
        public int SegmentCrossings;        // points where two intersection segments cross inside a triangle

        readonly Dictionary<ExactPoint3, int> pointIndex = new Dictionary<ExactPoint3, int>();

        public ExactMeshArrangement(List<DMesh3> meshes)
        {
            Meshes = meshes;
        }

        public static long EdgeKey(int a, int b)
        {
            return (a < b) ? (((long)a << 32) | (uint)b) : (((long)b << 32) | (uint)a);
        }

        public int AddPoint(ExactPoint3 p)
        {
            int id;
            if (pointIndex.TryGetValue(p, out id))
                return id;
            id = Points.Count;
            Points.Add(p);
            pointIndex[p] = id;
            return id;
        }

        public Vector3d PointToVector(int id)
        {
            return Points[id].ToVector3d(ScaleExp);
        }


        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();

        public void Compute()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            build_points();
            build_triangles();
            PhaseMilliseconds["setup"] = sw.ElapsedMilliseconds; sw.Restart();
            find_intersections();
            PhaseMilliseconds["intersections"] = sw.ElapsedMilliseconds; sw.Restart();
            for (int ti = 0; ti < Triangles.Count; ++ti)
                prepare_triangle(Triangles[ti]);
            for (int ti = 0; ti < Triangles.Count; ++ti)
                triangulate(ti);
            PhaseMilliseconds["triangulation"] = sw.ElapsedMilliseconds;
        }



        void build_points()
        {
            // common exponent: every coordinate is an integer multiple of 2^ScaleExp
            int eMin = int.MaxValue;
            foreach (DMesh3 mesh in Meshes) {
                foreach (int vid in mesh.VertexIndices()) {
                    Vector3d v = mesh.GetVertex(vid);
                    for (int k = 0; k < 3; ++k) {
                        BigInteger m; int e;
                        ExactPoint3.Decompose(v[k], out m, out e);
                        if (m.IsZero == false)
                            eMin = Math.Min(eMin, e);
                    }
                }
            }
            if (eMin == int.MaxValue)
                eMin = 0;
            ScaleExp = eMin;

            VertexPoints = new int[Meshes.Count][];
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                DMesh3 mesh = Meshes[mi];
                VertexPoints[mi] = new int[mesh.MaxVertexID];
                foreach (int vid in mesh.VertexIndices()) {
                    Vector3d v = mesh.GetVertex(vid);
                    BigInteger[] c = new BigInteger[3];
                    for (int k = 0; k < 3; ++k) {
                        BigInteger m; int e;
                        ExactPoint3.Decompose(v[k], out m, out e);
                        c[k] = m.IsZero ? BigInteger.Zero : (m << (e - eMin));
                    }
                    VertexPoints[mi][vid] = AddPoint(ExactPoint3.FromInteger(c[0], c[1], c[2]));
                }
            }
        }


        void build_triangles()
        {
            MeshTriangles = new List<int>[Meshes.Count];
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                MeshTriangles[mi] = new List<int>();
                DMesh3 mesh = Meshes[mi];
                foreach (int tid in mesh.TriangleIndices()) {
                    Index3i t = mesh.GetTriangle(tid);
                    InputTriangle it = new InputTriangle() { Mesh = mi, Tid = tid };
                    it.Corners[0] = VertexPoints[mi][t.a];
                    it.Corners[1] = VertexPoints[mi][t.b];
                    it.Corners[2] = VertexPoints[mi][t.c];
                    it.pts = Points;
                    // degenerate iff the corners are collinear, ie collinear in all three coordinate projections (exact)
                    Vector3d a = mesh.GetVertex(t.a), b = mesh.GetVertex(t.b), c = mesh.GetVertex(t.c);
                    it.Degenerate = ExactPredicates.Orient2d(new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(c.x, c.y)) == 0
                        && ExactPredicates.Orient2d(new Vector2d(a.y, a.z), new Vector2d(b.y, b.z), new Vector2d(c.y, c.z)) == 0
                        && ExactPredicates.Orient2d(new Vector2d(a.z, a.x), new Vector2d(b.z, b.x), new Vector2d(c.z, c.x)) == 0;
                    MeshTriangles[mi].Add(Triangles.Count);
                    Triangles.Add(it);
                }
            }
        }

        /// <summary> the point common to three planes (they must be independent) </summary>
        public int PlanesPoint(Plane p1, Plane p2, Plane p3)
        {
            // Cramer: x = (d1 (n2 x n3) + d2 (n3 x n1) + d3 (n1 x n2)) / det(n1, n2, n3)
            BigInteger c23x = p2.B * p3.C - p2.C * p3.B, c23y = p2.C * p3.A - p2.A * p3.C, c23z = p2.A * p3.B - p2.B * p3.A;
            BigInteger c31x = p3.B * p1.C - p3.C * p1.B, c31y = p3.C * p1.A - p3.A * p1.C, c31z = p3.A * p1.B - p3.B * p1.A;
            BigInteger c12x = p1.B * p2.C - p1.C * p2.B, c12y = p1.C * p2.A - p1.A * p2.C, c12z = p1.A * p2.B - p1.B * p2.A;
            BigInteger det = p1.A * c23x + p1.B * c23y + p1.C * c23z;
            if (det.IsZero)
                throw new InvalidOperationException("ExactMeshArrangement: planes are not independent");
            return AddPoint(ExactPoint3.Create(
                p1.D * c23x + p2.D * c31x + p3.D * c12x,
                p1.D * c23y + p2.D * c31y + p3.D * c12y,
                p1.D * c23z + p2.D * c31z + p3.D * c12z, det));
        }


        /// <summary> orientation of three points in the projection of triangle t, +1 if counter-clockwise like t </summary>
        public int Orient(InputTriangle t, int p, int q, int r)
        {
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            ExactPoint3 a = Points[p], b = Points[q], c = Points[r];
            BigInteger det = ExactPoint3.Det3(a[i], a[j], a.W, b[i], b[j], b.W, c[i], c[j], c.W);
            return det.Sign * t.Sign;
        }

        /// <summary> +1 if s is inside the circle through p, q, r (counter-clockwise in the projection of t) </summary>
        public int InCircle(InputTriangle t, int p, int q, int r, int s)
        {
            int i = (t.Axis + 1) % 3, j = (t.Axis + 2) % 3;
            ExactPoint3 a = Points[p], b = Points[q], c = Points[r], d = Points[s];
            // rows (x w, y w, x^2 + y^2, w^2) = w^2 (x/w, y/w, |.|^2, 1); with orientation flipped by t.Sign
            BigInteger[,] m = new BigInteger[4, 4];
            ExactPoint3[] pts = { a, b, c, d };
            for (int k = 0; k < 4; ++k) {
                ExactPoint3 v = pts[k];
                m[k, 0] = v[i] * v.W;
                m[k, 1] = v[j] * v.W;
                m[k, 2] = v[i] * v[i] + v[j] * v[j];
                m[k, 3] = v.W * v.W;
            }
            BigInteger det = Det4(m);
            // for counter-clockwise a, b, c this determinant is positive iff d is inside; the projection
            // may mirror the triangle, which flips the orientation of a, b, c and the sign
            return det.Sign * t.Sign;
        }

        public static BigInteger Det4(BigInteger[,] m)
        {
            BigInteger det = BigInteger.Zero;
            for (int c = 0; c < 4; ++c) {
                int c0 = (c == 0) ? 1 : 0, c1 = (c <= 1) ? 2 : 1, c2 = (c <= 2) ? 3 : 2;
                BigInteger minor = ExactPoint3.Det3(
                    m[1, c0], m[1, c1], m[1, c2],
                    m[2, c0], m[2, c1], m[2, c2],
                    m[3, c0], m[3, c1], m[3, c2]);
                det += ((c % 2 == 0) ? m[0, c] : -m[0, c]) * minor;
            }
            return det;
        }

        // closed triangle contains point (which lies in its plane)
        bool inside(InputTriangle t, int r)
        {
            return Orient(t, t.Corners[0], t.Corners[1], r) >= 0
                && Orient(t, t.Corners[1], t.Corners[2], r) >= 0
                && Orient(t, t.Corners[2], t.Corners[0], r) >= 0;
        }

        // position of point along a direction: compare (d . p1) / w1 with (d . p2) / w2
        int compare_along(BigInteger dx, BigInteger dy, BigInteger dz, int p1, int p2)
        {
            if (p1 == p2)
                return 0;
            ExactPoint3 a = Points[p1], b = Points[p2];
            BigInteger ka = (dx * a.X + dy * a.Y + dz * a.Z) * b.W;
            BigInteger kb = (dx * b.X + dy * b.Y + dz * b.Z) * a.W;
            return ka.CompareTo(kb);
        }

        // direction of the line where the plane line meets the triangle plane
        static void line_direction(InputTriangle t, Plane line, out BigInteger dx, out BigInteger dy, out BigInteger dz)
        {
            dx = t.NY * line.C - t.NZ * line.B;
            dy = t.NZ * line.A - t.NX * line.C;
            dz = t.NX * line.B - t.NY * line.A;
        }



        void find_intersections()
        {
            if (Meshes.Count < 2)
                return;
            // boxes of the exact input coordinates
            var boxes = new List<AxisAlignedBox3d>[Meshes.Count];
            var trees = new TriangleBoxTree[Meshes.Count];
            for (int mi = 0; mi < Meshes.Count; ++mi) {
                boxes[mi] = new List<AxisAlignedBox3d>();
                foreach (int ti in MeshTriangles[mi])
                    boxes[mi].Add(triangle_box(Triangles[ti]));
                trees[mi] = new TriangleBoxTree(boxes[mi]);
            }
            for (int ma = 0; ma < Meshes.Count; ++ma) {
                for (int mb = ma + 1; mb < Meshes.Count; ++mb) {
                    for (int ka = 0; ka < MeshTriangles[ma].Count; ++ka) {
                        InputTriangle T = Triangles[MeshTriangles[ma][ka]];
                        if (T.Degenerate) continue;
                        trees[mb].Query(boxes[ma][ka], (kb) => {
                            InputTriangle U = Triangles[MeshTriangles[mb][kb]];
                            if (U.Degenerate == false)
                                intersect(MeshTriangles[ma][ka], MeshTriangles[mb][kb]);
                        });
                    }
                }
            }
        }

        AxisAlignedBox3d triangle_box(InputTriangle t)
        {
            DMesh3 mesh = Meshes[t.Mesh];
            Index3i tri = mesh.GetTriangle(t.Tid);
            AxisAlignedBox3d box = new AxisAlignedBox3d(mesh.GetVertex(tri.a));
            box.Contain(mesh.GetVertex(tri.b));
            box.Contain(mesh.GetVertex(tri.c));
            return box;
        }

        Vector3d input_vertex(InputTriangle t, int k)
        {
            DMesh3 mesh = Meshes[t.Mesh];
            Index3i tri = mesh.GetTriangle(t.Tid);
            return mesh.GetVertex(tri[k]);
        }


        void intersect(int ti, int ui)
        {
            InputTriangle T = Triangles[ti], U = Triangles[ui];
            Vector3d t0 = input_vertex(T, 0), t1 = input_vertex(T, 1), t2 = input_vertex(T, 2);
            Vector3d u0 = input_vertex(U, 0), u1 = input_vertex(U, 1), u2 = input_vertex(U, 2);
            // sides with the exact double predicates (same sign as N . (v - a) on the integer grid)
            int[] sT = { ExactPredicates.Orient3d(u0, u1, u2, t0), ExactPredicates.Orient3d(u0, u1, u2, t1), ExactPredicates.Orient3d(u0, u1, u2, t2) };
            if ((sT[0] > 0 && sT[1] > 0 && sT[2] > 0) || (sT[0] < 0 && sT[1] < 0 && sT[2] < 0))
                return;
            int[] sU = { ExactPredicates.Orient3d(t0, t1, t2, u0), ExactPredicates.Orient3d(t0, t1, t2, u1), ExactPredicates.Orient3d(t0, t1, t2, u2) };
            if ((sU[0] > 0 && sU[1] > 0 && sU[2] > 0) || (sU[0] < 0 && sU[1] < 0 && sU[2] < 0))
                return;

            if (sT[0] == 0 && sT[1] == 0 && sT[2] == 0) {
                intersect_coplanar(ti, ui);
                return;
            }

            List<int> pT = plane_points(T, sT, U.P), pU = plane_points(U, sU, T.P);
            BigInteger dx = T.NY * U.NZ - T.NZ * U.NY, dy = T.NZ * U.NX - T.NX * U.NZ, dz = T.NX * U.NY - T.NY * U.NX;
            int loT, hiT, loU, hiU;
            min_max(pT, dx, dy, dz, out loT, out hiT);
            min_max(pU, dx, dy, dz, out loU, out hiU);
            int lo = (compare_along(dx, dy, dz, loT, loU) >= 0) ? loT : loU;
            int hi = (compare_along(dx, dy, dz, hiT, hiU) <= 0) ? hiT : hiU;
            int c = compare_along(dx, dy, dz, lo, hi);
            if (c > 0)
                return;
            IntersectingPairs++;
            if (c == 0) {
                T.Points.Add(lo);
                U.Points.Add(lo);
            } else {
                T.Segments.Add(new Segment(lo, hi, U.P));
                U.Segments.Add(new Segment(lo, hi, T.P));
            }
        }

        // the points where the boundary of t meets the plane (t is not in the plane)
        List<int> plane_points(InputTriangle t, int[] side, Plane plane)
        {
            var pts = new List<int>();
            for (int k = 0; k < 3; ++k) {
                if (side[k] == 0)
                    pts.Add(t.Corners[k]);
                int k1 = (k + 1) % 3;
                if (side[k] * side[k1] < 0)
                    pts.Add(PlanesPoint(t.P, t.EdgePlanes[k], plane));
            }
            return pts;
        }

        void min_max(List<int> pts, BigInteger dx, BigInteger dy, BigInteger dz, out int lo, out int hi)
        {
            lo = hi = pts[0];
            for (int k = 1; k < pts.Count; ++k) {
                if (compare_along(dx, dy, dz, pts[k], lo) < 0) lo = pts[k];
                if (compare_along(dx, dy, dz, pts[k], hi) > 0) hi = pts[k];
            }
        }


        void intersect_coplanar(int ti, int ui)
        {
            InputTriangle T = Triangles[ti], U = Triangles[ui];
            bool any = false;
            // the edges of each triangle, clipped to the other, become constraints of the other
            for (int k = 0; k < 3; ++k) {
                any |= clip_into(T, U.Corners[k], U.Corners[(k + 1) % 3], U.EdgePlanes[k]);
                any |= clip_into(U, T.Corners[k], T.Corners[(k + 1) % 3], T.EdgePlanes[k]);
            }
            if (any) {
                IntersectingPairs++;
                T.Coplanar.Add(ui);
                U.Coplanar.Add(ti);
            }
        }

        // clip segment p-q (in the plane of t, on the line t.P ∩ line) to the closed triangle t, add the result to t
        bool clip_into(InputTriangle t, int p, int q, Plane line)
        {
            var cands = new List<int>();
            if (inside(t, p)) cands.Add(p);
            if (inside(t, q)) cands.Add(q);
            BigInteger dx, dy, dz;
            line_direction(t, line, out dx, out dy, out dz);
            for (int k = 0; k < 3; ++k) {
                int ci = t.Corners[k], cj = t.Corners[(k + 1) % 3];
                int o1 = Orient(t, p, q, ci), o2 = Orient(t, p, q, cj);
                if (o1 == 0 && between(dx, dy, dz, p, q, ci))
                    cands.Add(ci);
                if (o1 * o2 < 0) {
                    int o3 = Orient(t, ci, cj, p), o4 = Orient(t, ci, cj, q);
                    if (o3 * o4 < 0)
                        cands.Add(PlanesPoint(t.P, line, t.EdgePlanes[k]));
                }
            }
            if (cands.Count == 0)
                return false;
            int lo, hi;
            min_max(cands, dx, dy, dz, out lo, out hi);
            if (lo == hi)
                t.Points.Add(lo);
            else
                t.Segments.Add(new Segment(lo, hi, line));
            return true;
        }

        // r (on the line p-q) is in the closed segment p-q
        bool between(BigInteger dx, BigInteger dy, BigInteger dz, int p, int q, int r)
        {
            int a = compare_along(dx, dy, dz, p, r), b = compare_along(dx, dy, dz, r, q);
            return (a <= 0 && b <= 0) || (a >= 0 && b >= 0);
        }



        // points where the segments of t cross (only happens with three or more meshes, or self-intersections)
        void prepare_triangle(InputTriangle t)
        {
            if (t.Degenerate || (t.Segments.Count == 0 && t.Points.Count == 0))
                return;
            // unique segments
            var unique = new List<Segment>();
            var seen = new HashSet<long>(EdgeKeyComparer.Instance);
            foreach (Segment s in t.Segments) {
                if (s.P == s.Q) { t.Points.Add(s.P); continue; }
                if (seen.Add(EdgeKey(s.P, s.Q)))
                    unique.Add(s);
            }
            t.Segments = unique;
            foreach (Segment s in unique) {
                t.Points.Add(s.P);
                t.Points.Add(s.Q);
            }
            for (int i = 0; i < unique.Count; ++i) {
                Segment a = unique[i];
                for (int j = i + 1; j < unique.Count; ++j) {
                    Segment b = unique[j];
                    if (a.P == b.P || a.P == b.Q || a.Q == b.P || a.Q == b.Q)
                        continue;
                    int o1 = Orient(t, a.P, a.Q, b.P), o2 = Orient(t, a.P, a.Q, b.Q);
                    if (o1 * o2 >= 0) continue;
                    int o3 = Orient(t, b.P, b.Q, a.P), o4 = Orient(t, b.P, b.Q, a.Q);
                    if (o3 * o4 >= 0) continue;
                    t.Points.Add(PlanesPoint(t.P, a.Line, b.Line));
                    SegmentCrossings++;
                }
            }
            foreach (int c in t.Corners)
                t.Points.Remove(c);
        }


        void triangulate(int ti)
        {
            InputTriangle t = Triangles[ti];
            if (t.Degenerate)
                return;
            var pts = new HashSet<int>(t.Points);
            foreach (int c in t.Corners)
                pts.Remove(c);
            if (pts.Count == 0 && t.Segments.Count == 0) {
                Output.Add(new OutputTriangle() { Mesh = t.Mesh, Source = ti, A = t.Corners[0], B = t.Corners[1], C = t.Corners[2] });
                return;
            }

            var tri = new ExactTriangulation2(t.Corners[0], t.Corners[1], t.Corners[2],
                (a, b, c) => Orient(t, a, b, c), (a, b, c, d) => InCircle(t, a, b, c, d));
            var ordered = new List<int>(pts);
            ordered.Sort();     // deterministic
            foreach (int p in ordered)
                tri.InsertPoint(p);
            tri.MakeDelaunay();

            // segments split at the points on them
            var all = new List<int>(ordered);
            all.AddRange(t.Corners);
            foreach (Segment s in t.Segments) {
                BigInteger dx, dy, dz;
                line_direction(t, s.Line, out dx, out dy, out dz);
                var on = new List<int>() { s.P, s.Q };
                foreach (int r in all) {
                    if (r == s.P || r == s.Q) continue;
                    if (Orient(t, s.P, s.Q, r) == 0 && between(dx, dy, dz, s.P, s.Q, r))
                        on.Add(r);
                }
                on.Sort((a, b) => compare_along(dx, dy, dz, a, b));
                for (int k = 0; k + 1 < on.Count; ++k) {
                    tri.InsertConstraint(on[k], on[k + 1]);
                    ConstraintEdges.Add(EdgeKey(on[k], on[k + 1]));
                }
            }
            tri.MakeDelaunay();
            foreach (Index3i f in tri.Triangles())
                Output.Add(new OutputTriangle() { Mesh = t.Mesh, Source = ti, A = f.a, B = f.b, C = f.c });
        }
    }



    /// <summary>
    /// Hash for edge keys (a << 32 | b): the default long hash is a ^ b, which collides for most edges of a mesh
    /// </summary>
    internal sealed class EdgeKeyComparer : IEqualityComparer<long>
    {
        public static readonly EdgeKeyComparer Instance = new EdgeKeyComparer();
        public bool Equals(long a, long b) { return a == b; }
        public int GetHashCode(long key)
        {
            // SplitMix64 finalizer
            ulong z = (ulong)key + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)z ^ (int)(z >> 32);
        }
    }



    /// <summary>
    /// Bounding volume hierarchy of boxes with closed overlap tests and no padding, so that it never
    /// misses a pair of touching boxes (for exact computations).
    /// </summary>
    internal class TriangleBoxTree
    {
        readonly List<AxisAlignedBox3d> boxes;
        readonly int[] order;
        readonly List<AxisAlignedBox3d> nodeBox = new List<AxisAlignedBox3d>();
        readonly List<int> nodeStart = new List<int>(), nodeCount = new List<int>(), nodeLeft = new List<int>(), nodeRight = new List<int>();
        const int LeafSize = 8;

        public TriangleBoxTree(List<AxisAlignedBox3d> boxes)
        {
            this.boxes = boxes;
            order = new int[boxes.Count];
            for (int i = 0; i < order.Length; ++i)
                order[i] = i;
            if (order.Length > 0)
                build(0, order.Length);
        }

        int build(int start, int count)
        {
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            for (int i = start; i < start + count; ++i)
                box.Contain(boxes[order[i]]);
            int node = nodeBox.Count;
            nodeBox.Add(box); nodeStart.Add(start); nodeCount.Add(count); nodeLeft.Add(-1); nodeRight.Add(-1);
            if (count <= LeafSize)
                return node;
            Vector3d d = box.Diagonal;
            int axis = (d.x >= d.y && d.x >= d.z) ? 0 : (d.y >= d.z) ? 1 : 2;
            Array.Sort(order, start, count, Comparer<int>.Create((a, b) =>
                (boxes[a].Min[axis] + boxes[a].Max[axis]).CompareTo(boxes[b].Min[axis] + boxes[b].Max[axis])));
            int half = count / 2;
            int left = build(start, half);
            int right = build(start + half, count - half);
            nodeLeft[node] = left;
            nodeRight[node] = right;
            return node;
        }

        /// <summary> call f for every box that the segment p-q intersects, with all boxes expanded by pad </summary>
        public void QuerySegment(Vector3d p, Vector3d q, double pad, Action<int> f)
        {
            if (nodeBox.Count == 0)
                return;
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0) {
                int node = stack.Pop();
                if (segment_hits(nodeBox[node], p, q, pad) == false)
                    continue;
                if (nodeLeft[node] < 0) {
                    for (int i = nodeStart[node]; i < nodeStart[node] + nodeCount[node]; ++i)
                        if (segment_hits(boxes[order[i]], p, q, pad))
                            f(order[i]);
                } else {
                    stack.Push(nodeLeft[node]);
                    stack.Push(nodeRight[node]);
                }
            }
        }

        // slab test of segment p-q against the box expanded by pad (conservative: pad covers the rounding)
        static bool segment_hits(AxisAlignedBox3d box, Vector3d p, Vector3d q, double pad)
        {
            double t0 = 0, t1 = 1;
            for (int k = 0; k < 3; ++k) {
                double lo = box.Min[k] - pad, hi = box.Max[k] + pad;
                double d = q[k] - p[k];
                if (Math.Abs(d) < 1e-300) {
                    if (p[k] < lo || p[k] > hi) return false;
                    continue;
                }
                double ta = (lo - p[k]) / d, tb = (hi - p[k]) / d;
                if (ta > tb) { double tmp = ta; ta = tb; tb = tmp; }
                // widen the interval a little against rounding of the divisions
                double slack = 1e-9 * (Math.Abs(ta) + Math.Abs(tb)) + 1e-12;
                t0 = Math.Max(t0, ta - slack);
                t1 = Math.Min(t1, tb + slack);
                if (t0 > t1) return false;
            }
            return true;
        }

        static bool overlaps(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return !(a.Max.x < b.Min.x || a.Min.x > b.Max.x || a.Max.y < b.Min.y || a.Min.y > b.Max.y || a.Max.z < b.Min.z || a.Min.z > b.Max.z);
        }

        /// <summary> call f for every box that overlaps the closed query box </summary>
        public void Query(AxisAlignedBox3d query, Action<int> f)
        {
            if (nodeBox.Count == 0)
                return;
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0) {
                int node = stack.Pop();
                if (overlaps(nodeBox[node], query) == false)
                    continue;
                if (nodeLeft[node] < 0) {
                    for (int i = nodeStart[node]; i < nodeStart[node] + nodeCount[node]; ++i)
                        if (overlaps(boxes[order[i]], query))
                            f(order[i]);
                } else {
                    stack.Push(nodeLeft[node]);
                    stack.Push(nodeRight[node]);
                }
            }
        }
    }
}
